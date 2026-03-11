using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace HotDynamite.Networking
{
    public class HotDynamiteLobbyNetworkClient : MonoBehaviour
    {
        [Header("Backend")]
        [SerializeField] private string serverUrl = "ws://10.0.15.223:3000";
        [SerializeField] private string gameId = "HotDynamite";
        [SerializeField, Min(2)] private int roomCapacity = 4;
        [SerializeField] private bool connectOnStart;
        [SerializeField] private string roomMode = HotDynamiteRoomModes.Random;

        private HotDynamiteWebSocketClient socketClient;

        public event Action Connected;
        public event Action<string> ConnectionError;
        public event Action<WebSocketCloseReason> Disconnected;
        public event Action<HotDynamiteWelcomeMessage> WelcomeReceived;
        public event Action<HotDynamiteJoinedMessage> JoinedRoom;
        public event Action LeftRoom;
        public event Action<HotDynamiteErrorMessage> BackendErrorReceived;
        public event Action<string, string> RoomMessageReceived;
        public event Action<int, int, string[]> RoomOccupancyChanged;
        public event Action<string[]> RoomReady;
        public event Action<HotDynamiteLobbyPhase> LobbyPhaseChanged;
        public event Action<bool, string, int> HostStatusChanged;
        public event Action<HotDynamiteHostAnnouncementPayload> HostAnnouncementReceived;
        public event Action<HotDynamiteRosterUpdatePayload> RosterUpdated;
        public event Action<HotDynamiteMatchPreparePayload> MatchPrepareReceived;
        public event Action<HotDynamiteMatchStartPayload> MatchStartReceived;
        public event Action<HotDynamiteStateSnapshotPayload> StateSnapshotReceived;
        public event Action<HotDynamiteMatchEndPayload> MatchEndReceived;

        public string ServerUrl => serverUrl;
        public string GameId => gameId;
        public int RoomCapacity => roomCapacity;
        public string RoomMode => roomMode;
        public string LocalUserId { get; private set; }
        public string CurrentRoomId { get; private set; }
        public int CurrentChairId { get; private set; } = -1;
        public int CurrentCapacity { get; private set; }
        public string HostUserId { get; private set; }
        public int HostChairId { get; private set; } = -1;
        public HotDynamiteLobbyPhase LobbyPhase { get; private set; } = HotDynamiteLobbyPhase.Disconnected;
        public bool IsHost => !string.IsNullOrEmpty(LocalUserId) && LocalUserId == HostUserId;
        public bool IsConnected => socketClient != null && socketClient.IsConnected;
        public bool IsInRoom => !string.IsNullOrEmpty(CurrentRoomId);

        private string[] currentChairs;
        private bool roomReadyAnnounced;

        private void Awake()
        {
            socketClient = new HotDynamiteWebSocketClient();
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

            SetLobbyPhase(HotDynamiteLobbyPhase.Connecting);
            return socketClient.ConnectAsync(serverUrl, cancellationToken);
        }

        public Task JoinAsync(CancellationToken cancellationToken = default)
        {
            SetLobbyPhase(HotDynamiteLobbyPhase.JoiningRoom);
            HotDynamiteJoinRequest request = new HotDynamiteJoinRequest
            {
                capacity = roomCapacity,
                game_id = gameId
            };

            string payload = JsonUtility.ToJson(request);
            return socketClient.SendTextAsync(payload, cancellationToken);
        }

        public Task LeaveAsync(CancellationToken cancellationToken = default)
        {
            string payload = JsonUtility.ToJson(new HotDynamiteLeaveRequest());
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
            SetLobbyPhase(HotDynamiteLobbyPhase.Connected);
            Connected?.Invoke();
        }

        private void HandleSocketError(string errorMessage)
        {
            ConnectionError?.Invoke(errorMessage);
        }

        private void HandleDisconnected(System.Net.WebSockets.WebSocketCloseStatus? closeStatus, string description)
        {
            ResetRoomState();
            SetLobbyPhase(HotDynamiteLobbyPhase.Disconnected);

            Disconnected?.Invoke(new WebSocketCloseReason(closeStatus, description));
        }

        private void HandleMessageReceived(string rawJson)
        {
            HotDynamiteMessageTypeEnvelope envelope = JsonUtility.FromJson<HotDynamiteMessageTypeEnvelope>(rawJson);
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
                    SetLobbyPhase(HotDynamiteLobbyPhase.Connected);
                    LeftRoom?.Invoke();
                    break;
                case "error":
                    HotDynamiteErrorMessage error = JsonUtility.FromJson<HotDynamiteErrorMessage>(rawJson);
                    if (error != null)
                    {
                        BackendErrorReceived?.Invoke(error);
                    }
                    break;
                default:
                    Debug.Log($"HotDynamiteLobbyNetworkClient: Unhandled backend message `{envelope.type}`.");
                    break;
            }
        }

        private void HandleWelcome(string rawJson)
        {
            HotDynamiteWelcomeMessage welcome = JsonUtility.FromJson<HotDynamiteWelcomeMessage>(rawJson);
            if (welcome == null)
            {
                return;
            }

            LocalUserId = welcome.userId;
            WelcomeReceived?.Invoke(welcome);
        }

        private void HandleJoined(string rawJson)
        {
            HotDynamiteJoinedMessage joined = JsonUtility.FromJson<HotDynamiteJoinedMessage>(rawJson);
            if (joined == null)
            {
                return;
            }

            CurrentRoomId = joined.roomId;
            CurrentChairId = joined.chairId;
            CurrentCapacity = joined.capacity;
            currentChairs = joined.chairs != null ? (string[])joined.chairs.Clone() : new string[joined.capacity];
            roomReadyAnnounced = false;
            SetHost(joined.chairId == 0 ? LocalUserId : null, joined.chairId == 0 ? joined.chairId : -1);
            SetLobbyPhase(HotDynamiteLobbyPhase.InRoomWaiting);
            JoinedRoom?.Invoke(joined);
            NotifyRoomOccupancyChanged();
            BroadcastPresence();

            if (IsHost)
            {
                BroadcastHostAnnouncement();
            }
        }

        private void HandleRoomMessage(string rawJson)
        {
            HotDynamiteRoomMessage roomMessage = JsonUtility.FromJson<HotDynamiteRoomMessage>(rawJson);
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

            HotDynamiteRoomPayloadEnvelope payloadEnvelope = JsonUtility.FromJson<HotDynamiteRoomPayloadEnvelope>(payloadJson);
            if (payloadEnvelope == null)
            {
                return;
            }

            switch (payloadEnvelope.kind)
            {
                case HotDynamitePayloadKinds.Presence:
                    HandlePresencePayload(senderUserId, payloadJson);
                    break;
                case HotDynamitePayloadKinds.HostAnnounce:
                    HandleHostAnnouncementPayload(payloadJson);
                    break;
                case HotDynamitePayloadKinds.RosterUpdate:
                    HandleRosterUpdatePayload(payloadJson);
                    break;
                case HotDynamitePayloadKinds.MatchPrepare:
                    HandleMatchPreparePayload(payloadJson);
                    break;
                case HotDynamitePayloadKinds.MatchStart:
                    HandleMatchStartPayload(payloadJson);
                    break;
                case HotDynamitePayloadKinds.StateSnapshot:
                    HandleStateSnapshotPayload(payloadJson);
                    break;
                case HotDynamitePayloadKinds.MatchEnd:
                    HandleMatchEndPayload(payloadJson);
                    break;
            }
        }

        private void HandlePresencePayload(string senderUserId, string payloadJson)
        {
            HotDynamitePresencePayload payload = JsonUtility.FromJson<HotDynamitePresencePayload>(payloadJson);
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

            HotDynamitePresencePayload payload = new HotDynamitePresencePayload
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

            HotDynamiteHostAnnouncementPayload payload = new HotDynamiteHostAnnouncementPayload
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

        private void HandleHostAnnouncementPayload(string payloadJson)
        {
            HotDynamiteHostAnnouncementPayload payload = JsonUtility.FromJson<HotDynamiteHostAnnouncementPayload>(payloadJson);
            if (payload == null)
            {
                return;
            }

            if (!string.IsNullOrEmpty(payload.roomMode))
            {
                roomMode = payload.roomMode;
            }

            SetHost(payload.hostUserId, payload.hostChairId);
            HostAnnouncementReceived?.Invoke(payload);
        }

        private void HandleRosterUpdatePayload(string payloadJson)
        {
            HotDynamiteRosterUpdatePayload payload = JsonUtility.FromJson<HotDynamiteRosterUpdatePayload>(payloadJson);
            if (payload == null)
            {
                return;
            }

            if (!string.IsNullOrEmpty(payload.hostUserId))
            {
                SetHost(payload.hostUserId, FindChairForUser(payload.hostUserId, payload.roster));
            }

            RosterUpdated?.Invoke(payload);
        }

        private void HandleMatchPreparePayload(string payloadJson)
        {
            HotDynamiteMatchPreparePayload payload = JsonUtility.FromJson<HotDynamiteMatchPreparePayload>(payloadJson);
            if (payload == null)
            {
                return;
            }

            SetLobbyPhase(HotDynamiteLobbyPhase.MatchPreparing);
            MatchPrepareReceived?.Invoke(payload);
        }

        private void HandleMatchStartPayload(string payloadJson)
        {
            HotDynamiteMatchStartPayload payload = JsonUtility.FromJson<HotDynamiteMatchStartPayload>(payloadJson);
            if (payload == null)
            {
                return;
            }

            SetLobbyPhase(HotDynamiteLobbyPhase.MatchStarting);
            MatchStartReceived?.Invoke(payload);
        }

        private void HandleStateSnapshotPayload(string payloadJson)
        {
            HotDynamiteStateSnapshotPayload payload = JsonUtility.FromJson<HotDynamiteStateSnapshotPayload>(payloadJson);
            if (payload == null)
            {
                return;
            }

            SetLobbyPhase(HotDynamiteLobbyPhase.InMatch);
            StateSnapshotReceived?.Invoke(payload);
        }

        private void HandleMatchEndPayload(string payloadJson)
        {
            HotDynamiteMatchEndPayload payload = JsonUtility.FromJson<HotDynamiteMatchEndPayload>(payloadJson);
            if (payload == null)
            {
                return;
            }

            SetLobbyPhase(HotDynamiteLobbyPhase.InRoomWaiting);
            MatchEndReceived?.Invoke(payload);
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

        private void SetLobbyPhase(HotDynamiteLobbyPhase nextPhase)
        {
            if (LobbyPhase == nextPhase)
            {
                return;
            }

            LobbyPhase = nextPhase;
            LobbyPhaseChanged?.Invoke(LobbyPhase);
        }

        private static int FindChairForUser(string userId, HotDynamiteRosterEntry[] roster)
        {
            if (string.IsNullOrEmpty(userId) || roster == null)
            {
                return -1;
            }

            for (int i = 0; i < roster.Length; i++)
            {
                HotDynamiteRosterEntry entry = roster[i];
                if (entry != null && entry.userId == userId)
                {
                    return entry.chairId;
                }
            }

            return -1;
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
