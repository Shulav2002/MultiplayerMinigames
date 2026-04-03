using System;
using System.Threading;
using System.Threading.Tasks;
using MiniGames.Networking;

namespace StayAlive.Networking
{
    public sealed class StayAliveWebSocketClient : IDisposable
    {
        private readonly ManualWebSocketClient client = new ManualWebSocketClient("StayAliveWebSocketClient");

        public event Action Connected;
        public event Action<string> MessageReceived;
        public event Action<string> ErrorReceived;
        public event Action<System.Net.WebSockets.WebSocketCloseStatus?, string> Disconnected;

        public bool IsConnected => client.IsConnected;

        public StayAliveWebSocketClient()
        {
            client.Connected += () => Connected?.Invoke();
            client.MessageReceived += message => MessageReceived?.Invoke(message);
            client.ErrorReceived += errorMessage => ErrorReceived?.Invoke(errorMessage);
            client.Disconnected += (status, description) => Disconnected?.Invoke(status, description);
        }

        public async Task ConnectAsync(string url, CancellationToken cancellationToken = default)
        {
            await client.ConnectAsync(url, cancellationToken);
        }

        public async Task SendTextAsync(string text, CancellationToken cancellationToken = default)
        {
            await client.SendTextAsync(text, cancellationToken);
        }

        public async Task CloseAsync(string statusDescription = "Client closing", CancellationToken cancellationToken = default)
        {
            await client.CloseAsync(statusDescription, cancellationToken);
        }

        public void DispatchQueuedEvents()
        {
            client.DispatchQueuedEvents();
        }

        public void Dispose()
        {
            client.Dispose();
        }
    }
}
