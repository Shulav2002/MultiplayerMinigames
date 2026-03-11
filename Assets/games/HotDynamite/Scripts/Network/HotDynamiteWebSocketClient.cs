using System;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace HotDynamite.Networking
{
    public sealed class HotDynamiteWebSocketClient : IDisposable
    {
        private readonly ConcurrentQueue<Action> mainThreadEvents = new ConcurrentQueue<Action>();
        private readonly byte[] receiveBuffer = new byte[8192];

        private ClientWebSocket socket;
        private CancellationTokenSource receiveLoopCts;
        private Task receiveLoopTask;

        public event Action Connected;
        public event Action<string> MessageReceived;
        public event Action<string> ErrorReceived;
        public event Action<WebSocketCloseStatus?, string> Disconnected;

        public bool IsConnected => socket != null && socket.State == WebSocketState.Open;

        public async Task ConnectAsync(string url, CancellationToken cancellationToken = default)
        {
            if (socket != null && (socket.State == WebSocketState.Open || socket.State == WebSocketState.Connecting))
            {
                return;
            }

            CleanupSocket();

            socket = new ClientWebSocket();
            receiveLoopCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            try
            {
                await socket.ConnectAsync(new Uri(url), cancellationToken);
                EnqueueMainThreadEvent(() => Connected?.Invoke());
                receiveLoopTask = ReceiveLoopAsync(socket, receiveLoopCts.Token);
            }
            catch (Exception exception)
            {
                EnqueueMainThreadEvent(() => ErrorReceived?.Invoke(exception.Message));
                CleanupSocket();
                throw;
            }
        }

        public async Task SendTextAsync(string text, CancellationToken cancellationToken = default)
        {
            if (!IsConnected)
            {
                throw new InvalidOperationException("WebSocket is not connected.");
            }

            byte[] bytes = Encoding.UTF8.GetBytes(text);
            ArraySegment<byte> payload = new ArraySegment<byte>(bytes);
            await socket.SendAsync(payload, WebSocketMessageType.Text, true, cancellationToken);
        }

        public async Task CloseAsync(string statusDescription = "Client closing", CancellationToken cancellationToken = default)
        {
            if (socket == null)
            {
                return;
            }

            try
            {
                if (socket.State == WebSocketState.Open || socket.State == WebSocketState.CloseReceived)
                {
                    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, statusDescription, cancellationToken);
                }
            }
            catch (Exception exception)
            {
                EnqueueMainThreadEvent(() => ErrorReceived?.Invoke(exception.Message));
            }
            finally
            {
                CleanupSocket();
                EnqueueMainThreadEvent(() => Disconnected?.Invoke(WebSocketCloseStatus.NormalClosure, statusDescription));
            }
        }

        public void DispatchQueuedEvents()
        {
            while (mainThreadEvents.TryDequeue(out Action queuedEvent))
            {
                queuedEvent?.Invoke();
            }
        }

        public void Dispose()
        {
            CleanupSocket();
        }

        private async Task ReceiveLoopAsync(ClientWebSocket activeSocket, CancellationToken cancellationToken)
        {
            ArraySegment<byte> segment = new ArraySegment<byte>(receiveBuffer);
            StringBuilder builder = new StringBuilder();

            try
            {
                while (!cancellationToken.IsCancellationRequested && activeSocket.State == WebSocketState.Open)
                {
                    WebSocketReceiveResult result = await activeSocket.ReceiveAsync(segment, cancellationToken);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        WebSocketCloseStatus? closeStatus = result.CloseStatus;
                        string description = result.CloseStatusDescription ?? string.Empty;
                        CleanupSocket();
                        EnqueueMainThreadEvent(() => Disconnected?.Invoke(closeStatus, description));
                        return;
                    }

                    string chunk = Encoding.UTF8.GetString(receiveBuffer, 0, result.Count);
                    builder.Append(chunk);

                    if (!result.EndOfMessage)
                    {
                        continue;
                    }

                    string message = builder.ToString();
                    builder.Clear();
                    EnqueueMainThreadEvent(() => MessageReceived?.Invoke(message));
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                EnqueueMainThreadEvent(() => ErrorReceived?.Invoke(exception.Message));
                CleanupSocket();
                EnqueueMainThreadEvent(() => Disconnected?.Invoke(activeSocket.CloseStatus, activeSocket.CloseStatusDescription ?? string.Empty));
            }
        }

        private void EnqueueMainThreadEvent(Action queuedEvent)
        {
            if (queuedEvent == null)
            {
                return;
            }

            mainThreadEvents.Enqueue(queuedEvent);
        }

        private void CleanupSocket()
        {
            if (receiveLoopCts != null)
            {
                receiveLoopCts.Cancel();
                receiveLoopCts.Dispose();
                receiveLoopCts = null;
            }

            if (socket != null)
            {
                socket.Dispose();
                socket = null;
            }

            receiveLoopTask = null;
        }
    }
}
