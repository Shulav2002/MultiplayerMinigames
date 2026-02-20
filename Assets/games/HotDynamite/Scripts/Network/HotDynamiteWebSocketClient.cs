using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace HotDynamite.Network
{
    public class HotDynamiteWebSocketClient : MonoBehaviour
    {
        [SerializeField] private string serverUrl = "ws://localhost:8080/ws";
        [SerializeField] private bool connectOnStart = true;
        [SerializeField] private bool verboseLogging = false;

        private ClientWebSocket socket;
        private CancellationTokenSource cts;
        private Task receiveTask;
        private readonly ConcurrentQueue<Action> mainThreadQueue = new ConcurrentQueue<Action>();

        public event Action Connected;
        public event Action<string> MessageReceived;
        public event Action<int, string> Closed;
        public event Action<string> Error;

        public bool IsConnected => socket != null && socket.State == WebSocketState.Open;
        public string ServerUrl
        {
            get => serverUrl;
            set => serverUrl = value;
        }

        private async void Start()
        {
            if (connectOnStart)
            {
                await ConnectAsync();
            }
        }

        private void Update()
        {
            while (mainThreadQueue.TryDequeue(out Action action))
            {
                action?.Invoke();
            }
        }

        public async Task ConnectAsync()
        {
            if (IsConnected)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(serverUrl))
            {
                QueueError("WebSocket URL is empty.");
                return;
            }

            CleanupSocket();
            socket = new ClientWebSocket();
            cts = new CancellationTokenSource();

            try
            {
                await socket.ConnectAsync(new Uri(serverUrl), cts.Token);
                QueueMainThread(() =>
                {
                    if (verboseLogging)
                    {
                        Debug.Log($"HotDynamiteWebSocketClient connected: {serverUrl}");
                    }

                    Connected?.Invoke();
                });

                receiveTask = Task.Run(() => ReceiveLoop(cts.Token), cts.Token);
            }
            catch (Exception ex)
            {
                QueueError($"WebSocket connect failed: {ex.Message}");
                CleanupSocket();
            }
        }

        public async Task DisconnectAsync()
        {
            if (socket == null)
            {
                return;
            }

            CancellationToken token = cts != null ? cts.Token : CancellationToken.None;
            try
            {
                if (socket.State == WebSocketState.Open || socket.State == WebSocketState.CloseReceived)
                {
                    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Client disconnect", token);
                }
            }
            catch (Exception ex)
            {
                QueueError($"WebSocket disconnect error: {ex.Message}");
            }
            finally
            {
                CleanupSocket();
            }
        }

        public async Task SendTextAsync(string text)
        {
            if (!IsConnected)
            {
                return;
            }

            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            try
            {
                byte[] bytes = Encoding.UTF8.GetBytes(text);
                ArraySegment<byte> segment = new ArraySegment<byte>(bytes);
                await socket.SendAsync(segment, WebSocketMessageType.Text, true, cts.Token);
            }
            catch (Exception ex)
            {
                QueueError($"WebSocket send failed: {ex.Message}");
            }
        }

        private async Task ReceiveLoop(CancellationToken token)
        {
            ArraySegment<byte> buffer = new ArraySegment<byte>(new byte[8192]);
            MemoryStream messageBuffer = new MemoryStream();
            try
            {
                while (!token.IsCancellationRequested && socket != null && socket.State == WebSocketState.Open)
                {
                    WebSocketReceiveResult result = await socket.ReceiveAsync(buffer, token);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        int closeCode = result.CloseStatus.HasValue ? (int)result.CloseStatus.Value : 0;
                        string closeReason = result.CloseStatusDescription ?? "Closed";
                        QueueClosed(closeCode, closeReason);
                        break;
                    }

                    if (result.Count > 0)
                    {
                        messageBuffer.Write(buffer.Array, 0, result.Count);
                    }

                    if (!result.EndOfMessage)
                    {
                        continue;
                    }

                    string message = Encoding.UTF8.GetString(messageBuffer.ToArray());
                    messageBuffer.SetLength(0);
                    QueueMainThread(() =>
                    {
                        if (verboseLogging)
                        {
                            Debug.Log($"HotDynamiteWebSocketClient recv: {message}");
                        }

                        MessageReceived?.Invoke(message);
                    });
                }
            }
            catch (OperationCanceledException)
            {
                // Expected during normal shutdown.
            }
            catch (Exception ex)
            {
                QueueError($"WebSocket receive failed: {ex.Message}");
            }
            finally
            {
                messageBuffer.Dispose();
            }
        }

        private void QueueClosed(int closeCode, string reason)
        {
            QueueMainThread(() =>
            {
                if (verboseLogging)
                {
                    Debug.Log($"HotDynamiteWebSocketClient closed ({closeCode}): {reason}");
                }

                Closed?.Invoke(closeCode, reason);
            });
        }

        private void QueueError(string error)
        {
            QueueMainThread(() =>
            {
                Debug.LogError(error);
                Error?.Invoke(error);
            });
        }

        private void QueueMainThread(Action action)
        {
            if (action == null)
            {
                return;
            }

            mainThreadQueue.Enqueue(action);
        }

        private void CleanupSocket()
        {
            if (cts != null)
            {
                try
                {
                    cts.Cancel();
                }
                catch
                {
                    // Ignore cancellation cleanup errors.
                }

                cts.Dispose();
                cts = null;
            }

            if (socket != null)
            {
                socket.Dispose();
                socket = null;
            }

            receiveTask = null;
        }

        private async void OnDestroy()
        {
            await DisconnectAsync();
        }
    }
}
