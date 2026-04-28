using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace TankBattle.Networking
{
    public class TankBattleLobbyNetworkClient : MonoBehaviour
    {
        [Header("Backend")]
        [SerializeField] private string serverUrl = "wss://mini-games.checkmygame.live/ws/";
        [SerializeField] private string gameId = "TankBattle";
        [SerializeField, Min(2)] private int roomCapacity = 2;
        [SerializeField] private bool connectOnStart;
        [SerializeField] private string roomMode = TankBattleRoomModes.Random;
        [SerializeField] private bool forceNetworkTrafficDiagnostics;
        [SerializeField, Min(0.1f)] private float networkTrafficDiagnosticsIntervalSeconds = 0.5f;

        private TankBattleWebSocketClient socketClient;

        public event Action Connected;
        public event Action<string> ConnectionError;
        public event Action<WebSocketCloseReason> Disconnected;
        public event Action<TankBattleWelcomeMessage> WelcomeReceived;
        public event Action<TankBattleJoinedMessage> JoinedRoom;
        public event Action LeftRoom;
        public event Action<TankBattleErrorMessage> BackendErrorReceived;
        public event Action<string, string> RoomMessageReceived;
        public event Action<int, int, string[]> RoomOccupancyChanged;
        public event Action<string[]> RoomReady;
        public event Action<TankBattleLobbyPhase> LobbyPhaseChanged;
        public event Action<bool, string, int> HostStatusChanged;
        public event Action<string, TankBattleHostAnnouncementPayload> HostAnnouncementReceived;
        public event Action<string, TankBattleRosterUpdatePayload> RosterUpdated;
        public event Action<string, TankBattleMatchPreparePayload> MatchPrepareReceived;
        public event Action<string, TankBattleMatchStartPayload> MatchStartReceived;
        public event Action<string, TankBattleStateSnapshotPayload> StateSnapshotReceived;
        public event Action<string, TankBattleMatchEndPayload> MatchEndReceived;

        public string ServerUrl => serverUrl;
        public string GameId => gameId;
        public int RoomCapacity => roomCapacity;
        public string RoomMode => roomMode;
        public string LocalUserId { get; private set; }
        public string CurrentRoomId { get; private set; }
        public int CurrentChairId { get; private set; } = -1;
        public int CurrentCapacity { get; private set; }
        public string[] CurrentChairs => currentChairs != null ? (string[])currentChairs.Clone() : Array.Empty<string>();
        public string CurrentBackendRoomState { get; private set; }
        public string HostUserId { get; private set; }
        public int HostChairId { get; private set; } = -1;
        public TankBattleLobbyPhase LobbyPhase { get; private set; } = TankBattleLobbyPhase.Disconnected;
        public bool IsHost => !string.IsNullOrEmpty(LocalUserId) && LocalUserId == HostUserId;
        public bool IsConnected => socketClient != null && socketClient.IsConnected;
        public bool IsInRoom => !string.IsNullOrEmpty(CurrentRoomId);
        public int OccupiedSeats => CountOccupiedChairs(currentChairs);

        private string[] currentChairs;
        private bool roomReadyAnnounced;
        private float nextTrafficLogAt;

        private void Awake()
        {
            socketClient = new TankBattleWebSocketClient();
            socketClient.Connected += HandleConnected;
            socketClient.MessageReceived += HandleMessageReceived;
            socketClient.ErrorReceived += HandleSocketError;
            socketClient.Disconnected += HandleDisconnected;
        }

        private async void Start()
        {
            if (!connectOnStart)
            {
                return;
            }

            await ConnectAsync();
        }

        private void Update()
        {
            socketClient?.DispatchQueuedEvents();
        }

        private async void OnDestroy()
        {
            if (socketClient == null)
            {
                return;
            }

            socketClient.Connected -= HandleConnected;
            socketClient.MessageReceived -= HandleMessageReceived;
            socketClient.ErrorReceived -= HandleSocketError;
            socketClient.Disconnected -= HandleDisconnected;

            try
            {
                await socketClient.CloseAsync();
            }
            catch
            {
            }

            socketClient.Dispose();
            socketClient = null;
        }

        public Task ConnectAsync(CancellationToken cancellationToken = default)
        {
            if (socketClient == null)
            {
                throw new InvalidOperationException("Socket client is not initialized.");
            }

            SetLobbyPhase(TankBattleLobbyPhase.Connecting);
            return socketClient.ConnectAsync(serverUrl, cancellationToken);
        }

        public Task JoinAsync(CancellationToken cancellationToken = default)
        {
            return JoinAsync(null, TankBattleRoomModes.Random, false, cancellationToken);
        }

        public Task JoinPrivateAsync(string roomId, bool createRoom, CancellationToken cancellationToken = default)
        {
            return JoinAsync(roomId, TankBattleRoomModes.Private, createRoom, cancellationToken);
        }

        private Task JoinAsync(string roomId, string requestedRoomMode, bool createRoom, CancellationToken cancellationToken)
        {
            SetLobbyPhase(TankBattleLobbyPhase.JoiningRoom);

            string normalizedRoomMode = string.IsNullOrWhiteSpace(requestedRoomMode)
                ? TankBattleRoomModes.Random
                : requestedRoomMode.Trim().ToLowerInvariant();
            string normalizedRoomId = string.IsNullOrWhiteSpace(roomId)
                ? null
                : roomId.Trim();

            roomMode = normalizedRoomMode;

            TankBattleJoinRequest request = new TankBattleJoinRequest
            {
                capacity = roomCapacity,
                game_id = gameId,
                room_mode = normalizedRoomMode,
                room_id = normalizedRoomId,
                create_room = createRoom,
                roomMode = normalizedRoomMode,
                roomId = normalizedRoomId,
                createRoom = createRoom
            };

            string payload = JsonUtility.ToJson(request);
            return socketClient.SendTextAsync(payload, cancellationToken);
        }

        public Task LeaveAsync(CancellationToken cancellationToken = default)
        {
            string payload = JsonUtility.ToJson(new TankBattleLeaveRequest());
            return socketClient.SendTextAsync(payload, cancellationToken);
        }

        public Task BroadcastToAllAsync(string payloadJson, CancellationToken cancellationToken = default)
        {
            return SendBroadcastAsync("broadcast_all", payloadJson, cancellationToken);
        }

        public Task BroadcastToOthersAsync(string payloadJson, CancellationToken cancellationToken = default)
        {
            return SendBroadcastAsync("broadcast_others", payloadJson, cancellationToken);
        }

        private Task SendBroadcastAsync(string type, string payloadJson, CancellationToken cancellationToken)
        {
            string safePayload = string.IsNullOrWhiteSpace(payloadJson) ? "null" : payloadJson;
            string json = $"{{\"type\":\"{type}\",\"payload\":{safePayload}}}";
            LogTraffic($"Send {type} payload={safePayload}");
            return socketClient.SendTextAsync(json, cancellationToken);
        }

        private void HandleConnected()
        {
            SetLobbyPhase(TankBattleLobbyPhase.Connected);
            Connected?.Invoke();
        }

        private void HandleSocketError(string errorMessage)
        {
            ConnectionError?.Invoke(errorMessage);
        }

        private void HandleDisconnected(System.Net.WebSockets.WebSocketCloseStatus? closeStatus, string description)
        {
            ResetRoomState();
            SetLobbyPhase(TankBattleLobbyPhase.Disconnected);

            Disconnected?.Invoke(new WebSocketCloseReason(closeStatus, description));
        }

        private void HandleMessageReceived(string rawJson)
        {
            TankBattleMessageTypeEnvelope envelope = JsonUtility.FromJson<TankBattleMessageTypeEnvelope>(rawJson);
            if (envelope == null || string.IsNullOrEmpty(envelope.type))
            {
                ConnectionError?.Invoke($"Could not parse backend message: {rawJson}");
                return;
            }

            switch (envelope.type)
            {
                case "welcome":
                    HandleWelcome(rawJson);
                    break;
                case "joined":
                    HandleJoined(rawJson);
                    break;
                case "room_message":
                    HandleRoomMessage(rawJson);
                    break;
                case "left":
                    ResetRoomState();
                    SetLobbyPhase(TankBattleLobbyPhase.Connected);
                    LeftRoom?.Invoke();
                    break;
                case "error":
                    TankBattleErrorMessage error = JsonUtility.FromJson<TankBattleErrorMessage>(rawJson);
                    if (error != null)
                    {
                        BackendErrorReceived?.Invoke(error);
                    }

                    break;
                default:
                    Debug.Log($"TankBattleLobbyNetworkClient: Unhandled backend message `{envelope.type}`.");
                    break;
            }
        }

        private void HandleWelcome(string rawJson)
        {
            TankBattleWelcomeMessage welcome = JsonUtility.FromJson<TankBattleWelcomeMessage>(rawJson);
            if (welcome == null)
            {
                return;
            }

            LocalUserId = welcome.userId;
            WelcomeReceived?.Invoke(welcome);
        }

        private void HandleJoined(string rawJson)
        {
            TankBattleJoinedMessage joined = JsonUtility.FromJson<TankBattleJoinedMessage>(rawJson);
            if (joined == null)
            {
                return;
            }

            CurrentRoomId = joined.roomId;
            CurrentChairId = joined.chairId;
            CurrentCapacity = joined.capacity;
            CurrentBackendRoomState = joined.state;
            currentChairs = joined.chairs != null ? (string[])joined.chairs.Clone() : new string[joined.capacity];
            roomReadyAnnounced = false;
            LogTraffic(
                $"Joined room={CurrentRoomId}, localUser={LocalUserId}, chair={CurrentChairId}, capacity={CurrentCapacity}, host={joined.hostUserId}, chairs={DescribeChairs(currentChairs)}");
            SetHost(joined.hostUserId, FindChairFromChairs(joined.hostUserId, currentChairs));
            SetLobbyPhase(MapBackendRoomStateToLobbyPhase(joined.state));
            JoinedRoom?.Invoke(joined);
            NotifyRoomOccupancyChanged();
            BroadcastPresence();

            if (IsHost && string.IsNullOrEmpty(joined.hostUserId))
            {
                BroadcastHostAnnouncement();
            }
        }

        private void HandleRoomMessage(string rawJson)
        {
            TankBattleRoomMessage roomMessage = JsonUtility.FromJson<TankBattleRoomMessage>(rawJson);
            if (roomMessage == null)
            {
                return;
            }

            string payloadJson = TryExtractTopLevelField(rawJson, "payload", out string extractedPayload)
                ? extractedPayload
                : "null";
            payloadJson = NormalizePayloadJson(payloadJson);

            string payloadKind = "unknown";
            if (!string.IsNullOrWhiteSpace(payloadJson) && payloadJson != "null")
            {
                TankBattleRoomPayloadEnvelope payloadEnvelope = JsonUtility.FromJson<TankBattleRoomPayloadEnvelope>(payloadJson);
                if (payloadEnvelope != null && !string.IsNullOrEmpty(payloadEnvelope.kind))
                {
                    payloadKind = payloadEnvelope.kind;
                }
            }

            LogTraffic($"Room message from={roomMessage.from}, kind={payloadKind}, payload={payloadJson}");
            HandleRoomPayload(roomMessage.from, payloadJson);
            RoomMessageReceived?.Invoke(roomMessage.from, payloadJson);
        }

        private static string NormalizePayloadJson(string payloadJson)
        {
            if (string.IsNullOrWhiteSpace(payloadJson))
            {
                return payloadJson;
            }

            string trimmed = payloadJson.Trim();
            if (trimmed.Length >= 2 && trimmed[0] == '"' && trimmed[trimmed.Length - 1] == '"')
            {
                try
                {
                    JsonStringWrapper wrapper = JsonUtility.FromJson<JsonStringWrapper>($"{{\"value\":{trimmed}}}");
                    if (wrapper != null && !string.IsNullOrWhiteSpace(wrapper.value))
                    {
                        return wrapper.value;
                    }
                }
                catch
                {
                }
            }

            return payloadJson;
        }

        private void LogTraffic(string message)
        {
            if (!forceNetworkTrafficDiagnostics)
            {
                return;
            }

            float now = Time.unscaledTime;
            if (now < nextTrafficLogAt)
            {
                return;
            }

            nextTrafficLogAt = now + Mathf.Max(0.1f, networkTrafficDiagnosticsIntervalSeconds);
            Debug.Log($"TankBattleLobbyNetworkClient: {message}");
        }

        private static string DescribeChairs(string[] chairs)
        {
            if (chairs == null || chairs.Length == 0)
            {
                return "[]";
            }

            StringBuilder builder = new StringBuilder();
            for (int i = 0; i < chairs.Length; i++)
            {
                if (i > 0)
                {
                    builder.Append(", ");
                }

                string value = string.IsNullOrEmpty(chairs[i]) ? "empty" : chairs[i];
                builder.Append('[').Append(i).Append(':').Append(value).Append(']');
            }

            return builder.ToString();
        }

        [Serializable]
        private sealed class JsonStringWrapper
        {
            public string value;
        }

        private void HandleRoomPayload(string senderUserId, string payloadJson)
        {
            if (string.IsNullOrWhiteSpace(payloadJson) || payloadJson == "null")
            {
                return;
            }

            TankBattleRoomPayloadEnvelope payloadEnvelope = JsonUtility.FromJson<TankBattleRoomPayloadEnvelope>(payloadJson);
            if (payloadEnvelope == null)
            {
                return;
            }

            switch (payloadEnvelope.kind)
            {
                case TankBattlePayloadKinds.Presence:
                    HandlePresencePayload(senderUserId, payloadJson);
                    break;
                case TankBattlePayloadKinds.HostAnnounce:
                    HandleHostAnnouncementPayload(senderUserId, payloadJson);
                    break;
                case TankBattlePayloadKinds.RosterUpdate:
                    HandleRosterUpdatePayload(senderUserId, payloadJson);
                    break;
                case TankBattlePayloadKinds.MatchPrepare:
                    HandleMatchPreparePayload(senderUserId, payloadJson);
                    break;
                case TankBattlePayloadKinds.MatchStart:
                    HandleMatchStartPayload(senderUserId, payloadJson);
                    break;
                case TankBattlePayloadKinds.StateSnapshot:
                    HandleStateSnapshotPayload(senderUserId, payloadJson);
                    break;
                case TankBattlePayloadKinds.MatchEnd:
                    HandleMatchEndPayload(senderUserId, payloadJson);
                    break;
            }
        }

        private void HandlePresencePayload(string senderUserId, string payloadJson)
        {
            TankBattlePresencePayload payload = JsonUtility.FromJson<TankBattlePresencePayload>(payloadJson);
            if (payload == null)
            {
                return;
            }

            if (payload.capacity > 0 && CurrentCapacity <= 0)
            {
                CurrentCapacity = payload.capacity;
            }

            EnsureChairArrayCapacity();

            if (payload.chairs != null && payload.chairs.Length == currentChairs.Length)
            {
                Array.Copy(payload.chairs, currentChairs, currentChairs.Length);
            }
            else if (payload.chairId >= 0 && payload.chairId < currentChairs.Length)
            {
                currentChairs[payload.chairId] = senderUserId;
            }

            NotifyRoomOccupancyChanged();
        }

        private async void BroadcastPresence()
        {
            if (!IsConnected || !IsInRoom)
            {
                return;
            }

            EnsureChairArrayCapacity();

            TankBattlePresencePayload payload = new TankBattlePresencePayload
            {
                chairId = CurrentChairId,
                capacity = CurrentCapacity,
                chairs = currentChairs != null ? (string[])currentChairs.Clone() : Array.Empty<string>()
            };

            try
            {
                await BroadcastToOthersAsync(JsonUtility.ToJson(payload));
            }
            catch (Exception exception)
            {
                ConnectionError?.Invoke(exception.Message);
            }
        }

        private async void BroadcastHostAnnouncement()
        {
            if (!IsConnected || !IsInRoom || !IsHost)
            {
                return;
            }

            TankBattleHostAnnouncementPayload payload = new TankBattleHostAnnouncementPayload
            {
                roomMode = roomMode,
                roomId = CurrentRoomId,
                hostUserId = LocalUserId,
                hostChairId = CurrentChairId
            };

            try
            {
                await BroadcastToOthersAsync(JsonUtility.ToJson(payload));
            }
            catch (Exception exception)
            {
                ConnectionError?.Invoke(exception.Message);
            }
        }

        private void HandleHostAnnouncementPayload(string senderUserId, string payloadJson)
        {
            TankBattleHostAnnouncementPayload payload = JsonUtility.FromJson<TankBattleHostAnnouncementPayload>(payloadJson);
            if (payload == null)
            {
                return;
            }

            if (!string.IsNullOrEmpty(payload.roomMode))
            {
                roomMode = payload.roomMode;
            }

            CurrentBackendRoomState = string.IsNullOrEmpty(CurrentBackendRoomState) ? "waiting" : CurrentBackendRoomState;
            SetHost(payload.hostUserId, payload.hostChairId);
            HostAnnouncementReceived?.Invoke(senderUserId, payload);
        }

        private void HandleRosterUpdatePayload(string senderUserId, string payloadJson)
        {
            TankBattleRosterUpdatePayload payload = JsonUtility.FromJson<TankBattleRosterUpdatePayload>(payloadJson);
            if (payload == null)
            {
                return;
            }

            if (!string.IsNullOrEmpty(payload.hostUserId))
            {
                SetHost(payload.hostUserId, FindChairForUser(payload.hostUserId, payload.roster));
            }

            RosterUpdated?.Invoke(senderUserId, payload);
        }

        private void HandleMatchPreparePayload(string senderUserId, string payloadJson)
        {
            TankBattleMatchPreparePayload payload = JsonUtility.FromJson<TankBattleMatchPreparePayload>(payloadJson);
            if (payload == null)
            {
                return;
            }

            SetLobbyPhase(TankBattleLobbyPhase.MatchPreparing);
            MatchPrepareReceived?.Invoke(senderUserId, payload);
        }

        private void HandleMatchStartPayload(string senderUserId, string payloadJson)
        {
            TankBattleMatchStartPayload payload = JsonUtility.FromJson<TankBattleMatchStartPayload>(payloadJson);
            if (payload == null)
            {
                return;
            }

            SetLobbyPhase(TankBattleLobbyPhase.MatchStarting);
            MatchStartReceived?.Invoke(senderUserId, payload);
        }

        private void HandleStateSnapshotPayload(string senderUserId, string payloadJson)
        {
            TankBattleStateSnapshotPayload payload = JsonUtility.FromJson<TankBattleStateSnapshotPayload>(payloadJson);
            if (payload == null)
            {
                return;
            }

            SetLobbyPhase(TankBattleLobbyPhase.InMatch);
            StateSnapshotReceived?.Invoke(senderUserId, payload);
        }

        private void HandleMatchEndPayload(string senderUserId, string payloadJson)
        {
            TankBattleMatchEndPayload payload = JsonUtility.FromJson<TankBattleMatchEndPayload>(payloadJson);
            if (payload == null)
            {
                return;
            }

            SetLobbyPhase(TankBattleLobbyPhase.InRoomWaiting);
            MatchEndReceived?.Invoke(senderUserId, payload);
        }

        private void NotifyRoomOccupancyChanged()
        {
            EnsureChairArrayCapacity();

            string[] chairsSnapshot = currentChairs != null ? (string[])currentChairs.Clone() : Array.Empty<string>();
            int occupiedChairs = CountOccupiedChairs(chairsSnapshot);
            RoomOccupancyChanged?.Invoke(occupiedChairs, CurrentCapacity, chairsSnapshot);

            if (!roomReadyAnnounced && CurrentCapacity > 0 && occupiedChairs >= CurrentCapacity)
            {
                roomReadyAnnounced = true;
                RoomReady?.Invoke(chairsSnapshot);
            }
        }

        private void EnsureChairArrayCapacity()
        {
            int targetCapacity = Mathf.Max(0, CurrentCapacity);
            if (targetCapacity == 0)
            {
                return;
            }

            if (currentChairs == null || currentChairs.Length != targetCapacity)
            {
                string[] next = new string[targetCapacity];
                if (currentChairs != null)
                {
                    Array.Copy(currentChairs, next, Mathf.Min(currentChairs.Length, next.Length));
                }

                currentChairs = next;
            }
        }

        private void ResetRoomState()
        {
            CurrentRoomId = null;
            CurrentChairId = -1;
            CurrentCapacity = 0;
            CurrentBackendRoomState = null;
            SetHost(null, -1);
            currentChairs = null;
            roomReadyAnnounced = false;
        }

        private void SetHost(string hostUserId, int hostChairId)
        {
            bool changed = HostUserId != hostUserId || HostChairId != hostChairId;
            HostUserId = hostUserId;
            HostChairId = hostChairId;

            if (changed)
            {
                HostStatusChanged?.Invoke(IsHost, HostUserId, HostChairId);
            }
        }

        private void SetLobbyPhase(TankBattleLobbyPhase nextPhase)
        {
            if (LobbyPhase == nextPhase)
            {
                return;
            }

            LobbyPhase = nextPhase;
            LobbyPhaseChanged?.Invoke(LobbyPhase);
        }

        private static int FindChairFromChairs(string userId, string[] chairs)
        {
            if (string.IsNullOrEmpty(userId) || chairs == null)
            {
                return -1;
            }

            for (int i = 0; i < chairs.Length; i++)
            {
                if (chairs[i] == userId)
                {
                    return i;
                }
            }

            return -1;
        }

        private static int FindChairForUser(string userId, TankBattleRosterEntry[] roster)
        {
            if (string.IsNullOrEmpty(userId) || roster == null)
            {
                return -1;
            }

            for (int i = 0; i < roster.Length; i++)
            {
                TankBattleRosterEntry entry = roster[i];
                if (entry != null && entry.userId == userId)
                {
                    return entry.chairId;
                }
            }

            return -1;
        }

        private static TankBattleLobbyPhase MapBackendRoomStateToLobbyPhase(string roomState)
        {
            switch (roomState)
            {
                case "playing":
                    return TankBattleLobbyPhase.InMatch;
                case "complete":
                    return TankBattleLobbyPhase.MatchPreparing;
                case "waiting":
                default:
                    return TankBattleLobbyPhase.InRoomWaiting;
            }
        }

        private static int CountOccupiedChairs(string[] chairs)
        {
            if (chairs == null)
            {
                return 0;
            }

            int count = 0;
            for (int i = 0; i < chairs.Length; i++)
            {
                if (!string.IsNullOrEmpty(chairs[i]))
                {
                    count++;
                }
            }

            return count;
        }

        private static bool TryExtractTopLevelField(string json, string fieldName, out string rawValue)
        {
            rawValue = null;
            if (string.IsNullOrEmpty(json) || string.IsNullOrEmpty(fieldName))
            {
                return false;
            }

            string needle = $"\"{fieldName}\"";
            int keyIndex = json.IndexOf(needle, StringComparison.Ordinal);
            if (keyIndex < 0)
            {
                return false;
            }

            int colonIndex = json.IndexOf(':', keyIndex + needle.Length);
            if (colonIndex < 0)
            {
                return false;
            }

            int valueStart = colonIndex + 1;
            while (valueStart < json.Length && char.IsWhiteSpace(json[valueStart]))
            {
                valueStart++;
            }

            if (valueStart >= json.Length)
            {
                return false;
            }

            int valueEnd = FindJsonValueEnd(json, valueStart);
            if (valueEnd <= valueStart)
            {
                return false;
            }

            rawValue = json.Substring(valueStart, valueEnd - valueStart);
            return true;
        }

        private static int FindJsonValueEnd(string json, int startIndex)
        {
            char start = json[startIndex];
            if (start == '"')
            {
                return FindStringEnd(json, startIndex);
            }

            if (start == '{' || start == '[')
            {
                return FindCompositeEnd(json, startIndex);
            }

            int index = startIndex;
            while (index < json.Length && json[index] != ',' && json[index] != '}')
            {
                index++;
            }

            return index;
        }

        private static int FindStringEnd(string json, int startIndex)
        {
            bool escaped = false;
            for (int i = startIndex + 1; i < json.Length; i++)
            {
                char current = json[i];
                if (escaped)
                {
                    escaped = false;
                    continue;
                }

                if (current == '\\')
                {
                    escaped = true;
                    continue;
                }

                if (current == '"')
                {
                    return i + 1;
                }
            }

            return json.Length;
        }

        private static int FindCompositeEnd(string json, int startIndex)
        {
            StringBuilder stack = new StringBuilder();
            stack.Append(json[startIndex]);
            bool inString = false;
            bool escaped = false;

            for (int i = startIndex + 1; i < json.Length; i++)
            {
                char current = json[i];
                if (inString)
                {
                    if (escaped)
                    {
                        escaped = false;
                    }
                    else if (current == '\\')
                    {
                        escaped = true;
                    }
                    else if (current == '"')
                    {
                        inString = false;
                    }

                    continue;
                }

                if (current == '"')
                {
                    inString = true;
                    continue;
                }

                if (current == '{' || current == '[')
                {
                    stack.Append(current);
                    continue;
                }

                if (current == '}' || current == ']')
                {
                    if (stack.Length == 0)
                    {
                        return i + 1;
                    }

                    stack.Length -= 1;
                    if (stack.Length == 0)
                    {
                        return i + 1;
                    }
                }
            }

            return json.Length;
        }
    }

    public readonly struct WebSocketCloseReason
    {
        public readonly System.Net.WebSockets.WebSocketCloseStatus? Status;
        public readonly string Description;

        public WebSocketCloseReason(System.Net.WebSockets.WebSocketCloseStatus? status, string description)
        {
            Status = status;
            Description = description;
        }
    }
}
