using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace StayAlive.Networking
{
    public class StayAliveLobbyNetworkClient : MonoBehaviour
    {
        [Header("Backend")]
        [SerializeField] private string serverUrl = "ws://10.0.15.178:39003";
        [SerializeField] private string gameId = "StayAlive";
        [SerializeField, Min(2)] private int roomCapacity = 4;
        [SerializeField] private bool connectOnStart;
        [SerializeField] private string roomMode = StayAliveRoomModes.Random;

        private StayAliveWebSocketClient socketClient;

        public event Action Connected;
        public event Action<string> ConnectionError;
        public event Action<WebSocketCloseReason> Disconnected;
        public event Action<StayAliveWelcomeMessage> WelcomeReceived;
        public event Action<StayAliveJoinedMessage> JoinedRoom;
        public event Action LeftRoom;
        public event Action<StayAliveErrorMessage> BackendErrorReceived;
        public event Action<string, string> RoomMessageReceived;
        public event Action<int, int, string[]> RoomOccupancyChanged;
        public event Action<string[]> RoomReady;
        public event Action<StayAliveLobbyPhase> LobbyPhaseChanged;
        public event Action<bool, string, int> HostStatusChanged;
        public event Action<string, StayAliveHostAnnouncementPayload> HostAnnouncementReceived;
        public event Action<string, StayAliveRosterUpdatePayload> RosterUpdated;
        public event Action<string, StayAliveMatchPreparePayload> MatchPrepareReceived;
        public event Action<string, StayAliveMatchStartPayload> MatchStartReceived;
        public event Action<string, StayAliveStateSnapshotPayload> StateSnapshotReceived;
        public event Action<string, StayAliveMatchEndPayload> MatchEndReceived;

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
        public StayAliveLobbyPhase LobbyPhase { get; private set; } = StayAliveLobbyPhase.Disconnected;
        public bool IsHost => !string.IsNullOrEmpty(LocalUserId) && LocalUserId == HostUserId;
        public bool IsConnected => socketClient != null && socketClient.IsConnected;
        public bool IsInRoom => !string.IsNullOrEmpty(CurrentRoomId);

        private string[] currentChairs;
        private bool roomReadyAnnounced;

        private void Awake()
        {
            socketClient = new StayAliveWebSocketClient();
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

            SetLobbyPhase(StayAliveLobbyPhase.Connecting);
            return socketClient.ConnectAsync(serverUrl, cancellationToken);
        }

        public Task JoinAsync(CancellationToken cancellationToken = default)
        {
            return JoinAsync(null, StayAliveRoomModes.Random, false, cancellationToken);
        }

        public Task JoinPrivateAsync(string roomId, bool createRoom, CancellationToken cancellationToken = default)
        {
            return JoinAsync(roomId, StayAliveRoomModes.Private, createRoom, cancellationToken);
        }

        private Task JoinAsync(string roomId, string requestedRoomMode, bool createRoom, CancellationToken cancellationToken)
        {
            SetLobbyPhase(StayAliveLobbyPhase.JoiningRoom);

            string normalizedRoomMode = string.IsNullOrWhiteSpace(requestedRoomMode)
                ? StayAliveRoomModes.Random
                : requestedRoomMode.Trim().ToLowerInvariant();
            string normalizedRoomId = string.IsNullOrWhiteSpace(roomId)
                ? null
                : roomId.Trim();

            roomMode = normalizedRoomMode;

            StayAliveJoinRequest request = new StayAliveJoinRequest
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
            string payload = JsonUtility.ToJson(new StayAliveLeaveRequest());
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
            return socketClient.SendTextAsync(json, cancellationToken);
        }

        private void HandleConnected()
        {
            SetLobbyPhase(StayAliveLobbyPhase.Connected);
            Connected?.Invoke();
        }

        private void HandleSocketError(string errorMessage)
        {
            ConnectionError?.Invoke(errorMessage);
        }

        private void HandleDisconnected(System.Net.WebSockets.WebSocketCloseStatus? closeStatus, string description)
        {
            ResetRoomState();
            SetLobbyPhase(StayAliveLobbyPhase.Disconnected);
            Disconnected?.Invoke(new WebSocketCloseReason(closeStatus, description));
        }

        private void HandleMessageReceived(string rawJson)
        {
            StayAliveMessageTypeEnvelope envelope = JsonUtility.FromJson<StayAliveMessageTypeEnvelope>(rawJson);
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
                    SetLobbyPhase(StayAliveLobbyPhase.Connected);
                    LeftRoom?.Invoke();
                    break;
                case "error":
                    StayAliveErrorMessage error = JsonUtility.FromJson<StayAliveErrorMessage>(rawJson);
                    if (error != null)
                    {
                        BackendErrorReceived?.Invoke(error);
                    }
                    break;
                default:
                    Debug.Log($"StayAliveLobbyNetworkClient: Unhandled backend message `{envelope.type}`.");
                    break;
            }
        }

        private void HandleWelcome(string rawJson)
        {
            StayAliveWelcomeMessage welcome = JsonUtility.FromJson<StayAliveWelcomeMessage>(rawJson);
            if (welcome == null)
            {
                return;
            }

            LocalUserId = welcome.userId;
            WelcomeReceived?.Invoke(welcome);
        }

        private void HandleJoined(string rawJson)
        {
            StayAliveJoinedMessage joined = JsonUtility.FromJson<StayAliveJoinedMessage>(rawJson);
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
            StayAliveRoomMessage roomMessage = JsonUtility.FromJson<StayAliveRoomMessage>(rawJson);
            if (roomMessage == null)
            {
                return;
            }

            string payloadJson = TryExtractTopLevelField(rawJson, "payload", out string extractedPayload)
                ? extractedPayload
                : "null";
            HandleRoomPayload(roomMessage.from, payloadJson);
            RoomMessageReceived?.Invoke(roomMessage.from, payloadJson);
        }

        private void HandleRoomPayload(string senderUserId, string payloadJson)
        {
            if (string.IsNullOrWhiteSpace(payloadJson) || payloadJson == "null")
            {
                return;
            }

            StayAliveRoomPayloadEnvelope payloadEnvelope = JsonUtility.FromJson<StayAliveRoomPayloadEnvelope>(payloadJson);
            if (payloadEnvelope == null)
            {
                return;
            }

            switch (payloadEnvelope.kind)
            {
                case StayAlivePayloadKinds.Presence:
                    HandlePresencePayload(senderUserId, payloadJson);
                    break;
                case StayAlivePayloadKinds.HostAnnounce:
                    HandleHostAnnouncementPayload(senderUserId, payloadJson);
                    break;
                case StayAlivePayloadKinds.RosterUpdate:
                    HandleRosterUpdatePayload(senderUserId, payloadJson);
                    break;
                case StayAlivePayloadKinds.MatchPrepare:
                    HandleMatchPreparePayload(senderUserId, payloadJson);
                    break;
                case StayAlivePayloadKinds.MatchStart:
                    HandleMatchStartPayload(senderUserId, payloadJson);
                    break;
                case StayAlivePayloadKinds.StateSnapshot:
                    HandleStateSnapshotPayload(senderUserId, payloadJson);
                    break;
                case StayAlivePayloadKinds.MatchEnd:
                    HandleMatchEndPayload(senderUserId, payloadJson);
                    break;
            }
        }

        private void HandlePresencePayload(string senderUserId, string payloadJson)
        {
            StayAlivePresencePayload payload = JsonUtility.FromJson<StayAlivePresencePayload>(payloadJson);
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

            StayAlivePresencePayload payload = new StayAlivePresencePayload
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

            StayAliveHostAnnouncementPayload payload = new StayAliveHostAnnouncementPayload
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
            StayAliveHostAnnouncementPayload payload = JsonUtility.FromJson<StayAliveHostAnnouncementPayload>(payloadJson);
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
            StayAliveRosterUpdatePayload payload = JsonUtility.FromJson<StayAliveRosterUpdatePayload>(payloadJson);
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
            StayAliveMatchPreparePayload payload = JsonUtility.FromJson<StayAliveMatchPreparePayload>(payloadJson);
            if (payload == null)
            {
                return;
            }

            SetLobbyPhase(StayAliveLobbyPhase.MatchPreparing);
            MatchPrepareReceived?.Invoke(senderUserId, payload);
        }

        private void HandleMatchStartPayload(string senderUserId, string payloadJson)
        {
            StayAliveMatchStartPayload payload = JsonUtility.FromJson<StayAliveMatchStartPayload>(payloadJson);
            if (payload == null)
            {
                return;
            }

            SetLobbyPhase(StayAliveLobbyPhase.MatchStarting);
            MatchStartReceived?.Invoke(senderUserId, payload);
        }

        private void HandleStateSnapshotPayload(string senderUserId, string payloadJson)
        {
            StayAliveStateSnapshotPayload payload = JsonUtility.FromJson<StayAliveStateSnapshotPayload>(payloadJson);
            if (payload == null)
            {
                return;
            }

            SetLobbyPhase(StayAliveLobbyPhase.InMatch);
            StateSnapshotReceived?.Invoke(senderUserId, payload);
        }

        private void HandleMatchEndPayload(string senderUserId, string payloadJson)
        {
            StayAliveMatchEndPayload payload = JsonUtility.FromJson<StayAliveMatchEndPayload>(payloadJson);
            if (payload == null)
            {
                return;
            }

            SetLobbyPhase(StayAliveLobbyPhase.InRoomWaiting);
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

        private void SetLobbyPhase(StayAliveLobbyPhase nextPhase)
        {
            if (LobbyPhase == nextPhase)
            {
                return;
            }

            LobbyPhase = nextPhase;
            LobbyPhaseChanged?.Invoke(LobbyPhase);
        }

        private static int FindChairForUser(string userId, StayAliveRosterEntry[] roster)
        {
            if (string.IsNullOrEmpty(userId) || roster == null)
            {
                return -1;
            }

            for (int i = 0; i < roster.Length; i++)
            {
                StayAliveRosterEntry entry = roster[i];
                if (entry != null && entry.userId == userId)
                {
                    return entry.chairId;
                }
            }

            return -1;
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

        private static StayAliveLobbyPhase MapBackendRoomStateToLobbyPhase(string roomState)
        {
            switch (roomState)
            {
                case "playing":
                    return StayAliveLobbyPhase.InMatch;
                case "complete":
                    return StayAliveLobbyPhase.MatchPreparing;
                case "waiting":
                default:
                    return StayAliveLobbyPhase.InRoomWaiting;
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
