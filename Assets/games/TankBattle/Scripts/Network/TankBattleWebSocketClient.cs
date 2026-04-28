using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NativeCloseCode = NativeWebSocket.WebSocketCloseCode;
using NativeSocket = NativeWebSocket.WebSocket;
using NativeState = NativeWebSocket.WebSocketState;
using NetWebSocketCloseStatus = System.Net.WebSockets.WebSocketCloseStatus;

namespace TankBattle.Networking
{
    public sealed class TankBattleWebSocketClient : IDisposable
    {
        private NativeSocket socket;

        public event Action Connected;
        public event Action<string> MessageReceived;
        public event Action<string> ErrorReceived;
        public event Action<NetWebSocketCloseStatus?, string> Disconnected;

        public bool IsConnected => socket != null && socket.State == NativeState.Open;

        public async Task ConnectAsync(string url, CancellationToken cancellationToken = default)
        {
            if (socket != null && (socket.State == NativeState.Open || socket.State == NativeState.Connecting))
            {
                return;
            }

            DisposeSocket();

            socket = new NativeSocket(url);
            socket.OnOpen += HandleOpen;
            socket.OnMessage += HandleMessage;
            socket.OnError += HandleError;
            socket.OnClose += HandleClose;

            TaskCompletionSource<bool> completionSource = new TaskCompletionSource<bool>();
            NativeWebSocket.WebSocketOpenEventHandler openHandler = null;
            NativeWebSocket.WebSocketErrorEventHandler errorHandler = null;
            NativeWebSocket.WebSocketCloseEventHandler closeHandler = null;

            openHandler = () => completionSource.TrySetResult(true);
            errorHandler = errorMessage => completionSource.TrySetException(
                new InvalidOperationException(string.IsNullOrWhiteSpace(errorMessage) ? "WebSocket connection failed." : errorMessage));
            closeHandler = closeCode =>
            {
                if (socket != null && socket.State != NativeState.Open)
                {
                    completionSource.TrySetException(new InvalidOperationException(
                        "WebSocket closed during connect. Code: " + (int)closeCode));
                }
            };

            socket.OnOpen += openHandler;
            socket.OnError += errorHandler;
            socket.OnClose += closeHandler;

            CancellationTokenRegistration cancellationRegistration = default;
            if (cancellationToken.CanBeCanceled)
            {
                cancellationRegistration = cancellationToken.Register(() =>
                {
                    socket?.CancelConnection();
                    completionSource.TrySetCanceled();
                });
            }

            try
            {
                _ = socket.Connect();
                await completionSource.Task;
            }
            finally
            {
                cancellationRegistration.Dispose();

                if (socket != null)
                {
                    socket.OnOpen -= openHandler;
                    socket.OnError -= errorHandler;
                    socket.OnClose -= closeHandler;
                }
            }
        }

        public Task SendTextAsync(string text, CancellationToken cancellationToken = default)
        {
            if (!IsConnected)
            {
                throw new InvalidOperationException("WebSocket is not connected.");
            }

            cancellationToken.ThrowIfCancellationRequested();
            return socket.SendText(text ?? string.Empty);
        }

        public async Task CloseAsync(string statusDescription = "Client closing", CancellationToken cancellationToken = default)
        {
            if (socket == null)
            {
                return;
            }

            cancellationToken.ThrowIfCancellationRequested();

            if (socket.State == NativeState.Closing || socket.State == NativeState.Closed)
            {
                DisposeSocket();
                return;
            }

            try
            {
                await socket.Close(NativeCloseCode.Normal, statusDescription ?? string.Empty);
            }
            finally
            {
                DisposeSocket();
            }
        }

        public void DispatchQueuedEvents()
        {
#if !UNITY_WEBGL || UNITY_EDITOR
            socket?.DispatchMessageQueue();
#endif
        }

        public void Dispose()
        {
            DisposeSocket();
        }

        private void HandleOpen()
        {
            Connected?.Invoke();
        }

        private void HandleMessage(byte[] data)
        {
            string message = Encoding.UTF8.GetString(data ?? Array.Empty<byte>());
            MessageReceived?.Invoke(message);
        }

        private void HandleError(string errorMessage)
        {
            ErrorReceived?.Invoke(string.IsNullOrWhiteSpace(errorMessage) ? "WebSocket error." : errorMessage);
        }

        private void HandleClose(NativeCloseCode closeCode)
        {
            NetWebSocketCloseStatus? closeStatus = closeCode == NativeCloseCode.NotSet
                ? null
                : (NetWebSocketCloseStatus?)(int)closeCode;
            Disconnected?.Invoke(closeStatus, closeCode.ToString());
        }

        private void DisposeSocket()
        {
            if (socket == null)
            {
                return;
            }

            socket.OnOpen -= HandleOpen;
            socket.OnMessage -= HandleMessage;
            socket.OnError -= HandleError;
            socket.OnClose -= HandleClose;
            socket = null;
        }
    }
}
