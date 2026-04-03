using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Security;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace MiniGames.Networking
{
    public sealed class ManualWebSocketClient : IDisposable
    {
        private const string UriSchemeWs = "ws";
        private const string UriSchemeWss = "wss";
        private const string WebSocketGuid = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";
        private const int HandshakeBufferLimit = 32 * 1024;

        private readonly ConcurrentQueue<Action> mainThreadEvents = new ConcurrentQueue<Action>();
        private readonly SemaphoreSlim sendLock = new SemaphoreSlim(1, 1);
        private readonly string logPrefix;

        private TcpClient tcpClient;
        private Stream activeStream;
        private CancellationTokenSource receiveLoopCts;
        private Task receiveLoopTask;
        private byte[] bufferedReceiveBytes = Array.Empty<byte>();
        private int bufferedReceiveOffset;
        private int bufferedReceiveCount;
        private int disconnectEventQueued;
        private bool isConnected;
        private bool isConnecting;
        private string currentUrl;

        public ManualWebSocketClient(string logPrefix)
        {
            this.logPrefix = string.IsNullOrWhiteSpace(logPrefix) ? nameof(ManualWebSocketClient) : logPrefix;
        }

        public event Action Connected;
        public event Action<string> MessageReceived;
        public event Action<string> ErrorReceived;
        public event Action<WebSocketCloseStatus?, string> Disconnected;

        public bool IsConnected
        {
            get { return isConnected && activeStream != null; }
        }

        public async Task ConnectAsync(string url, CancellationToken cancellationToken = default)
        {
            if (isConnecting || IsConnected)
            {
                return;
            }

            CleanupSocket();
            isConnecting = true;
            Interlocked.Exchange(ref disconnectEventQueued, 0);

            try
            {
                string connectUrl = url;
                const int MaxRedirectCount = 3;
                for (int redirectCount = 0; redirectCount <= MaxRedirectCount; redirectCount++)
                {
                    CleanupSocket();
                    currentUrl = connectUrl;

                    try
                    {
                        Uri uri = CreateValidatedUri(connectUrl);
                        receiveLoopCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                        tcpClient = new TcpClient();

                        int port = uri.IsDefaultPort
                            ? (uri.Scheme == UriSchemeWss ? 443 : 80)
                            : uri.Port;

                        await tcpClient.ConnectAsync(uri.Host, port);

                        Stream stream = tcpClient.GetStream();
                        if (uri.Scheme == UriSchemeWss)
                        {
                            SslStream sslStream = new SslStream(stream, false);
                            await Task.Run(() => sslStream.AuthenticateAsClient(uri.Host), cancellationToken);
                            stream = sslStream;
                        }

                        activeStream = stream;

                        string securityKey = CreateSecurityKey();
                        string request = BuildHandshakeRequest(uri, securityKey);
                        byte[] requestBytes = Encoding.ASCII.GetBytes(request);
                        await activeStream.WriteAsync(requestBytes, 0, requestBytes.Length, cancellationToken);
                        await activeStream.FlushAsync(cancellationToken);

                        await ReadAndValidateHandshakeAsync(uri, activeStream, securityKey, receiveLoopCts.Token);

                        isConnected = true;
                        EnqueueMainThreadEvent(() => Connected?.Invoke());
                        receiveLoopTask = ReceiveLoopAsync(receiveLoopCts.Token);
                        return;
                    }
                    catch (WebSocketRedirectException redirectException)
                    {
                        Debug.LogWarning(logPrefix + ": Following WebSocket redirect to `" + redirectException.RedirectUrl + "`.");
                        connectUrl = redirectException.RedirectUrl;
                    }
                }

                throw new InvalidOperationException("WebSocket connection exceeded the maximum redirect count for `" + url + "`.");
            }
            catch (Exception exception)
            {
                string errorDetails = FormatExceptionDetails(exception, currentUrl ?? url);
                Debug.LogError(logPrefix + ": " + errorDetails + "\n" + exception);
                EnqueueMainThreadEvent(() => ErrorReceived?.Invoke(errorDetails));
                CleanupSocket();
                throw;
            }
            finally
            {
                isConnecting = false;
            }
        }

        public async Task SendTextAsync(string text, CancellationToken cancellationToken = default)
        {
            if (!IsConnected)
            {
                throw new InvalidOperationException("WebSocket is not connected.");
            }

            byte[] payload = Encoding.UTF8.GetBytes(text ?? string.Empty);
            await SendFrameAsync(0x1, payload, cancellationToken);
        }

        public async Task CloseAsync(string statusDescription = "Client closing", CancellationToken cancellationToken = default)
        {
            if (activeStream == null)
            {
                return;
            }

            try
            {
                byte[] payload = BuildClosePayload(WebSocketCloseStatus.NormalClosure, statusDescription);
                await SendFrameAsync(0x8, payload, cancellationToken);
            }
            catch (Exception exception)
            {
                EnqueueMainThreadEvent(() => ErrorReceived?.Invoke(exception.Message));
            }
            finally
            {
                CleanupSocket();
                QueueDisconnectedOnce(WebSocketCloseStatus.NormalClosure, statusDescription);
            }
        }

        public void DispatchQueuedEvents()
        {
            Action queuedEvent;
            while (mainThreadEvents.TryDequeue(out queuedEvent))
            {
                if (queuedEvent != null)
                {
                    queuedEvent();
                }
            }
        }

        public void Dispose()
        {
            CleanupSocket();
            sendLock.Dispose();
        }

        private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
        {
            MemoryStream messageBuffer = new MemoryStream();
            bool assemblingTextMessage = false;

            try
            {
                while (!cancellationToken.IsCancellationRequested && IsConnected)
                {
                    WebSocketFrame frame = await ReadFrameAsync(cancellationToken);
                    switch (frame.Opcode)
                    {
                        case 0x1:
                            messageBuffer.SetLength(0);
                            if (frame.Payload.Length > 0)
                            {
                                messageBuffer.Write(frame.Payload, 0, frame.Payload.Length);
                            }

                            if (frame.Fin)
                            {
                                string message = Encoding.UTF8.GetString(messageBuffer.ToArray());
                                messageBuffer.SetLength(0);
                                EnqueueMainThreadEvent(() => MessageReceived?.Invoke(message));
                            }
                            else
                            {
                                assemblingTextMessage = true;
                            }
                            break;

                        case 0x0:
                            if (!assemblingTextMessage)
                            {
                                break;
                            }

                            if (frame.Payload.Length > 0)
                            {
                                messageBuffer.Write(frame.Payload, 0, frame.Payload.Length);
                            }

                            if (frame.Fin)
                            {
                                assemblingTextMessage = false;
                                string continuedMessage = Encoding.UTF8.GetString(messageBuffer.ToArray());
                                messageBuffer.SetLength(0);
                                EnqueueMainThreadEvent(() => MessageReceived?.Invoke(continuedMessage));
                            }
                            break;

                        case 0x2:
                            messageBuffer.SetLength(0);
                            assemblingTextMessage = false;
                            EnqueueMainThreadEvent(() => ErrorReceived?.Invoke("Binary WebSocket frames are not supported by this client."));
                            break;

                        case 0x9:
                            await SendFrameAsync(0xA, frame.Payload, cancellationToken);
                            break;

                        case 0xA:
                            break;

                        case 0x8:
                            CleanupSocket();
                            QueueDisconnectedOnce(frame.CloseStatus, frame.CloseDescription);
                            return;
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                string errorDetails = FormatExceptionDetails(exception, currentUrl);
                Debug.LogError(logPrefix + ": " + errorDetails + "\n" + exception);
                EnqueueMainThreadEvent(() => ErrorReceived?.Invoke(errorDetails));
                CleanupSocket();
                QueueDisconnectedOnce(null, exception.Message);
            }
            finally
            {
                messageBuffer.Dispose();
            }
        }

        private async Task ReadAndValidateHandshakeAsync(Uri requestUri, Stream stream, string securityKey, CancellationToken cancellationToken)
        {
            MemoryStream handshakeBuffer = new MemoryStream();
            byte[] readBuffer = new byte[1024];

            try
            {
                while (true)
                {
                    int bytesRead = await stream.ReadAsync(readBuffer, 0, readBuffer.Length, cancellationToken);
                    if (bytesRead <= 0)
                    {
                        throw new IOException("Connection closed before the WebSocket handshake completed.");
                    }

                    handshakeBuffer.Write(readBuffer, 0, bytesRead);
                    if (handshakeBuffer.Length > HandshakeBufferLimit)
                    {
                        throw new InvalidOperationException("WebSocket handshake response exceeded the supported size limit.");
                    }

                    byte[] rawData = handshakeBuffer.GetBuffer();
                    int headerEndIndex = IndexOfHeaderTerminator(rawData, (int)handshakeBuffer.Length);
                    if (headerEndIndex < 0)
                    {
                        continue;
                    }

                    int headerLength = headerEndIndex + 4;
                    string headerText = Encoding.ASCII.GetString(rawData, 0, headerLength);

                    int leftoverCount = (int)handshakeBuffer.Length - headerLength;
                    if (leftoverCount > 0)
                    {
                        bufferedReceiveBytes = new byte[leftoverCount];
                        Buffer.BlockCopy(rawData, headerLength, bufferedReceiveBytes, 0, leftoverCount);
                        bufferedReceiveOffset = 0;
                        bufferedReceiveCount = leftoverCount;
                    }
                    else
                    {
                        bufferedReceiveBytes = Array.Empty<byte>();
                        bufferedReceiveOffset = 0;
                        bufferedReceiveCount = 0;
                    }

                    ValidateHandshakeResponse(requestUri, headerText, securityKey);
                    return;
                }
            }
            finally
            {
                handshakeBuffer.Dispose();
            }
        }

        private async Task<WebSocketFrame> ReadFrameAsync(CancellationToken cancellationToken)
        {
            byte[] header = new byte[2];
            await ReadExactlyAsync(header, 0, header.Length, cancellationToken);

            bool fin = (header[0] & 0x80) != 0;
            int opcode = header[0] & 0x0F;
            bool masked = (header[1] & 0x80) != 0;
            ulong payloadLength = (ulong)(header[1] & 0x7F);

            if (payloadLength == 126)
            {
                byte[] extendedLength = new byte[2];
                await ReadExactlyAsync(extendedLength, 0, extendedLength.Length, cancellationToken);
                payloadLength = (ulong)((extendedLength[0] << 8) | extendedLength[1]);
            }
            else if (payloadLength == 127)
            {
                byte[] extendedLength = new byte[8];
                await ReadExactlyAsync(extendedLength, 0, extendedLength.Length, cancellationToken);
                payloadLength =
                    ((ulong)extendedLength[0] << 56) |
                    ((ulong)extendedLength[1] << 48) |
                    ((ulong)extendedLength[2] << 40) |
                    ((ulong)extendedLength[3] << 32) |
                    ((ulong)extendedLength[4] << 24) |
                    ((ulong)extendedLength[5] << 16) |
                    ((ulong)extendedLength[6] << 8) |
                    extendedLength[7];
            }

            if (payloadLength > int.MaxValue)
            {
                throw new InvalidOperationException("Received an unsupported WebSocket frame larger than 2 GB.");
            }

            byte[] maskKey = null;
            if (masked)
            {
                maskKey = new byte[4];
                await ReadExactlyAsync(maskKey, 0, maskKey.Length, cancellationToken);
            }

            byte[] payload = new byte[(int)payloadLength];
            if (payload.Length > 0)
            {
                await ReadExactlyAsync(payload, 0, payload.Length, cancellationToken);
            }

            if (masked && maskKey != null)
            {
                for (int i = 0; i < payload.Length; i++)
                {
                    payload[i] = (byte)(payload[i] ^ maskKey[i % maskKey.Length]);
                }
            }

            WebSocketCloseStatus? closeStatus;
            string closeDescription;
            ExtractCloseDetails(payload, opcode, out closeStatus, out closeDescription);
            return new WebSocketFrame(fin, opcode, payload, closeStatus, closeDescription);
        }

        private async Task ReadExactlyAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            int totalRead = 0;
            while (totalRead < count)
            {
                int copiedFromBuffer = ConsumeBufferedBytes(buffer, offset + totalRead, count - totalRead);
                totalRead += copiedFromBuffer;
                if (totalRead >= count)
                {
                    return;
                }

                int bytesRead = await activeStream.ReadAsync(buffer, offset + totalRead, count - totalRead, cancellationToken);
                if (bytesRead <= 0)
                {
                    throw new IOException("The remote WebSocket endpoint closed the connection unexpectedly.");
                }

                totalRead += bytesRead;
            }
        }

        private int ConsumeBufferedBytes(byte[] destination, int offset, int count)
        {
            if (bufferedReceiveCount <= 0)
            {
                return 0;
            }

            int bytesToCopy = Math.Min(count, bufferedReceiveCount);
            Buffer.BlockCopy(bufferedReceiveBytes, bufferedReceiveOffset, destination, offset, bytesToCopy);
            bufferedReceiveOffset += bytesToCopy;
            bufferedReceiveCount -= bytesToCopy;
            if (bufferedReceiveCount == 0)
            {
                bufferedReceiveBytes = Array.Empty<byte>();
                bufferedReceiveOffset = 0;
            }

            return bytesToCopy;
        }

        private async Task SendFrameAsync(int opcode, byte[] payload, CancellationToken cancellationToken)
        {
            if (activeStream == null)
            {
                throw new InvalidOperationException("WebSocket stream is not available.");
            }

            if (payload == null)
            {
                payload = Array.Empty<byte>();
            }

            byte[] frame = BuildClientFrame(opcode, payload);

            await sendLock.WaitAsync(cancellationToken);
            try
            {
                await activeStream.WriteAsync(frame, 0, frame.Length, cancellationToken);
                await activeStream.FlushAsync(cancellationToken);
            }
            finally
            {
                sendLock.Release();
            }
        }

        private void ValidateHandshakeResponse(Uri requestUri, string headerText, string securityKey)
        {
            string[] lines = headerText.Split(new[] { "\r\n" }, StringSplitOptions.None);
            int statusCode = ParseStatusCode(lines);
            if (statusCode != 101)
            {
                string redirectLocation = GetHeaderValue(lines, "Location");
                if (IsRedirectStatusCode(statusCode) && !string.IsNullOrWhiteSpace(redirectLocation))
                {
                    Uri redirectUri = BuildRedirectUri(requestUri, redirectLocation);
                    throw new WebSocketRedirectException(redirectUri.AbsoluteUri, headerText);
                }

                throw new InvalidOperationException(
                    "Unexpected WebSocket handshake status line: "
                    + (lines.Length > 0 ? lines[0] : "<empty>")
                    + "\n"
                    + headerText);
            }

            string connectionHeader = GetHeaderValue(lines, "Connection");
            string upgradeHeader = GetHeaderValue(lines, "Upgrade");
            string acceptHeader = GetHeaderValue(lines, "Sec-WebSocket-Accept");

            if (string.IsNullOrWhiteSpace(connectionHeader) || connectionHeader.IndexOf("upgrade", StringComparison.OrdinalIgnoreCase) < 0)
            {
                throw new InvalidOperationException("WebSocket handshake response is missing a valid Connection header.\n" + headerText);
            }

            if (!string.Equals(upgradeHeader, "websocket", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("WebSocket handshake response is missing a valid Upgrade header.\n" + headerText);
            }

            string expectedAccept = ComputeAcceptKey(securityKey);
            if (!string.Equals(acceptHeader, expectedAccept, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "WebSocket handshake validation failed. Expected Sec-WebSocket-Accept `" + expectedAccept
                    + "`, received `" + acceptHeader + "`.\n" + headerText);
            }
        }

        private static Uri CreateValidatedUri(string url)
        {
            Uri uri;
            if (!Uri.TryCreate(url, UriKind.Absolute, out uri))
            {
                throw new ArgumentException("A valid absolute WebSocket URL is required.", nameof(url));
            }

            if (uri.Scheme != UriSchemeWs && uri.Scheme != UriSchemeWss)
            {
                throw new ArgumentException("Only ws:// and wss:// URLs are supported.", nameof(url));
            }

            return uri;
        }

        private static int ParseStatusCode(string[] lines)
        {
            if (lines == null || lines.Length == 0 || string.IsNullOrWhiteSpace(lines[0]))
            {
                return -1;
            }

            string[] statusParts = lines[0].Split(' ');
            if (statusParts.Length < 2)
            {
                return -1;
            }

            int statusCode;
            return int.TryParse(statusParts[1], out statusCode) ? statusCode : -1;
        }

        private static string GetHeaderValue(string[] lines, string headerName)
        {
            if (lines == null || string.IsNullOrWhiteSpace(headerName))
            {
                return null;
            }

            for (int i = 1; i < lines.Length; i++)
            {
                string line = lines[i];
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                int separatorIndex = line.IndexOf(':');
                if (separatorIndex <= 0)
                {
                    continue;
                }

                string name = line.Substring(0, separatorIndex).Trim();
                if (!name.Equals(headerName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                return line.Substring(separatorIndex + 1).Trim();
            }

            return null;
        }

        private static bool IsRedirectStatusCode(int statusCode)
        {
            return statusCode == 301 || statusCode == 302 || statusCode == 307 || statusCode == 308;
        }

        private static Uri BuildRedirectUri(Uri requestUri, string redirectLocation)
        {
            Uri redirectUri;
            if (Uri.TryCreate(redirectLocation, UriKind.Absolute, out redirectUri))
            {
                return UpgradeRedirectSchemeIfNeeded(requestUri, redirectUri);
            }

            if (!Uri.TryCreate(requestUri, redirectLocation, out redirectUri))
            {
                throw new InvalidOperationException("WebSocket redirect Location header could not be parsed: " + redirectLocation);
            }

            return UpgradeRedirectSchemeIfNeeded(requestUri, redirectUri);
        }

        private static Uri UpgradeRedirectSchemeIfNeeded(Uri requestUri, Uri redirectUri)
        {
            if (requestUri == null || redirectUri == null)
            {
                return redirectUri;
            }

            bool requestWasSecureWebSocket = requestUri.Scheme == UriSchemeWss;
            bool requestWasInsecureWebSocket = requestUri.Scheme == UriSchemeWs;
            bool redirectIsHttpFamily = redirectUri.Scheme == Uri.UriSchemeHttp || redirectUri.Scheme == Uri.UriSchemeHttps;

            if (requestWasSecureWebSocket && redirectIsHttpFamily)
            {
                return new UriBuilder(redirectUri) { Scheme = UriSchemeWss, Port = redirectUri.IsDefaultPort ? -1 : redirectUri.Port }.Uri;
            }

            if (requestWasInsecureWebSocket && redirectIsHttpFamily)
            {
                return new UriBuilder(redirectUri) { Scheme = UriSchemeWs, Port = redirectUri.IsDefaultPort ? -1 : redirectUri.Port }.Uri;
            }

            return redirectUri;
        }

        private static string BuildHandshakeRequest(Uri uri, string securityKey)
        {
            StringBuilder builder = new StringBuilder();
            builder.Append("GET ")
                .Append(string.IsNullOrEmpty(uri.PathAndQuery) ? "/" : uri.PathAndQuery)
                .Append(" HTTP/1.1\r\n");
            builder.Append("Host: ").Append(BuildHostHeader(uri)).Append("\r\n");
            builder.Append("Connection: Upgrade\r\n");
            builder.Append("Upgrade: websocket\r\n");
            builder.Append("Sec-WebSocket-Version: 13\r\n");
            builder.Append("Sec-WebSocket-Key: ").Append(securityKey).Append("\r\n");
            builder.Append("User-Agent: UnityManualWebSocket/1.0\r\n");
            builder.Append("\r\n");
            return builder.ToString();
        }

        private static string BuildHostHeader(Uri uri)
        {
            bool isDefaultPort = uri.IsDefaultPort
                || (uri.Scheme == UriSchemeWs && uri.Port == 80)
                || (uri.Scheme == UriSchemeWss && uri.Port == 443);
            return isDefaultPort ? uri.Host : uri.Host + ":" + uri.Port;
        }

        private static string CreateSecurityKey()
        {
            byte[] keyBytes = new byte[16];
            using (RandomNumberGenerator randomNumberGenerator = RandomNumberGenerator.Create())
            {
                randomNumberGenerator.GetBytes(keyBytes);
            }

            return Convert.ToBase64String(keyBytes);
        }

        private static string ComputeAcceptKey(string securityKey)
        {
            byte[] combinedBytes = Encoding.ASCII.GetBytes(securityKey + WebSocketGuid);
            using (SHA1 sha1 = SHA1.Create())
            {
                byte[] hash = sha1.ComputeHash(combinedBytes);
                return Convert.ToBase64String(hash);
            }
        }

        private static byte[] BuildClientFrame(int opcode, byte[] payload)
        {
            byte[] maskKey = new byte[4];
            using (RandomNumberGenerator randomNumberGenerator = RandomNumberGenerator.Create())
            {
                randomNumberGenerator.GetBytes(maskKey);
            }

            MemoryStream frame = new MemoryStream();
            try
            {
                frame.WriteByte((byte)(0x80 | (opcode & 0x0F)));

                if (payload.Length <= 125)
                {
                    frame.WriteByte((byte)(0x80 | payload.Length));
                }
                else if (payload.Length <= ushort.MaxValue)
                {
                    frame.WriteByte(0x80 | 126);
                    frame.WriteByte((byte)((payload.Length >> 8) & 0xFF));
                    frame.WriteByte((byte)(payload.Length & 0xFF));
                }
                else
                {
                    frame.WriteByte(0x80 | 127);
                    ulong length = (ulong)payload.Length;
                    for (int shift = 56; shift >= 0; shift -= 8)
                    {
                        frame.WriteByte((byte)((length >> shift) & 0xFF));
                    }
                }

                frame.Write(maskKey, 0, maskKey.Length);

                byte[] maskedPayload = new byte[payload.Length];
                for (int i = 0; i < payload.Length; i++)
                {
                    maskedPayload[i] = (byte)(payload[i] ^ maskKey[i % maskKey.Length]);
                }

                if (maskedPayload.Length > 0)
                {
                    frame.Write(maskedPayload, 0, maskedPayload.Length);
                }

                return frame.ToArray();
            }
            finally
            {
                frame.Dispose();
            }
        }

        private static byte[] BuildClosePayload(WebSocketCloseStatus status, string description)
        {
            byte[] reasonBytes = string.IsNullOrEmpty(description)
                ? Array.Empty<byte>()
                : Encoding.UTF8.GetBytes(description);
            byte[] payload = new byte[2 + reasonBytes.Length];
            ushort statusCode = (ushort)status;
            payload[0] = (byte)((statusCode >> 8) & 0xFF);
            payload[1] = (byte)(statusCode & 0xFF);
            if (reasonBytes.Length > 0)
            {
                Buffer.BlockCopy(reasonBytes, 0, payload, 2, reasonBytes.Length);
            }

            return payload;
        }

        private static void ExtractCloseDetails(byte[] payload, int opcode, out WebSocketCloseStatus? closeStatus, out string closeDescription)
        {
            closeStatus = null;
            closeDescription = string.Empty;
            if (opcode != 0x8 || payload == null || payload.Length == 0)
            {
                return;
            }

            if (payload.Length >= 2)
            {
                ushort statusCode = (ushort)((payload[0] << 8) | payload[1]);
                closeStatus = (WebSocketCloseStatus)statusCode;
                if (payload.Length > 2)
                {
                    closeDescription = Encoding.UTF8.GetString(payload, 2, payload.Length - 2);
                }
            }
        }

        private static int IndexOfHeaderTerminator(byte[] data, int length)
        {
            for (int i = 0; i <= length - 4; i++)
            {
                if (data[i] == '\r'
                    && data[i + 1] == '\n'
                    && data[i + 2] == '\r'
                    && data[i + 3] == '\n')
                {
                    return i;
                }
            }

            return -1;
        }

        private static string FormatExceptionDetails(Exception exception, string url)
        {
            if (exception == null)
            {
                return "WebSocket connection failed for `" + url + "`.";
            }

            StringBuilder builder = new StringBuilder();
            builder.Append("WebSocket connection failed");
            if (!string.IsNullOrWhiteSpace(url))
            {
                builder.Append(" for `").Append(url).Append('`');
            }

            builder.Append(": ").Append(exception.GetType().Name).Append(": ").Append(exception.Message);

            Exception innerException = exception.InnerException;
            while (innerException != null)
            {
                builder.Append(" | Inner: ")
                    .Append(innerException.GetType().Name)
                    .Append(": ")
                    .Append(innerException.Message);
                innerException = innerException.InnerException;
            }

            return builder.ToString();
        }

        private void EnqueueMainThreadEvent(Action queuedEvent)
        {
            if (queuedEvent == null)
            {
                return;
            }

            mainThreadEvents.Enqueue(queuedEvent);
        }

        private void QueueDisconnectedOnce(WebSocketCloseStatus? closeStatus, string description)
        {
            if (Interlocked.Exchange(ref disconnectEventQueued, 1) != 0)
            {
                return;
            }

            EnqueueMainThreadEvent(() => Disconnected?.Invoke(closeStatus, description ?? string.Empty));
        }

        private void CleanupSocket()
        {
            isConnected = false;
            isConnecting = false;
            bufferedReceiveBytes = Array.Empty<byte>();
            bufferedReceiveOffset = 0;
            bufferedReceiveCount = 0;

            if (receiveLoopCts != null)
            {
                receiveLoopCts.Cancel();
                receiveLoopCts.Dispose();
                receiveLoopCts = null;
            }

            if (activeStream != null)
            {
                try
                {
                    activeStream.Dispose();
                }
                catch
                {
                }

                activeStream = null;
            }

            if (tcpClient != null)
            {
                try
                {
                    tcpClient.Dispose();
                }
                catch
                {
                }

                tcpClient = null;
            }

            receiveLoopTask = null;
        }

        private struct WebSocketFrame
        {
            public WebSocketFrame(bool fin, int opcode, byte[] payload, WebSocketCloseStatus? closeStatus, string closeDescription)
            {
                Fin = fin;
                Opcode = opcode;
                Payload = payload ?? Array.Empty<byte>();
                CloseStatus = closeStatus;
                CloseDescription = closeDescription ?? string.Empty;
            }

            public bool Fin;
            public int Opcode;
            public byte[] Payload;
            public WebSocketCloseStatus? CloseStatus;
            public string CloseDescription;
        }

        private sealed class WebSocketRedirectException : InvalidOperationException
        {
            public WebSocketRedirectException(string redirectUrl, string headerText)
                : base("WebSocket handshake was redirected to `" + redirectUrl + "`.\n" + headerText)
            {
                RedirectUrl = redirectUrl;
            }

            public string RedirectUrl { get; }
        }
    }
}
