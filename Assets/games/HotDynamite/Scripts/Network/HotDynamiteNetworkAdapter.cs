using System;
using System.Threading.Tasks;
using UnityEngine;

namespace HotDynamite.Network
{
    public class HotDynamiteNetworkAdapter : MonoBehaviour
    {
        [SerializeField] private HotDynamiteWebSocketClient webSocketClient;
        [SerializeField] private HotDynamiteGameManager gameManager;
        [SerializeField] private HotDynamitePlayerInput localInput;
        [SerializeField] private float inputSendRateHz = 20f;
        [SerializeField] private bool sendInputOnlyDuringMatch = true;
        [SerializeField] private bool verboseLogging = false;

        private int inputSequence;
        private float nextInputSendAt;
        private bool matchStarted;
        private string roomId;
        private string localPlayerId;

        public event Action<RoomStatePayload> RoomStateReceived;
        public event Action<MatchStartedPayload> MatchStartedReceived;
        public event Action<StateSnapshotPayload> SnapshotReceived;
        public event Action<MatchEndedPayload> MatchEndedReceived;
        public event Action<ErrorPayload> ErrorReceived;
        public event Action<string, string> UnknownMessageReceived;

        public string CurrentRoomId => roomId;
        public string LocalPlayerId => localPlayerId;
        public bool IsInMatch => matchStarted;

        private void Awake()
        {
            if (webSocketClient == null)
            {
                webSocketClient = GetComponent<HotDynamiteWebSocketClient>();
            }

            if (gameManager == null)
            {
                gameManager = FindAnyObjectByType<HotDynamiteGameManager>();
            }
        }

        private void OnEnable()
        {
            if (webSocketClient == null)
            {
                return;
            }

            webSocketClient.MessageReceived += OnMessageReceived;
            webSocketClient.Closed += OnSocketClosed;
        }

        private void OnDisable()
        {
            if (webSocketClient == null)
            {
                return;
            }

            webSocketClient.MessageReceived -= OnMessageReceived;
            webSocketClient.Closed -= OnSocketClosed;
        }

        private void Update()
        {
            if (webSocketClient == null || !webSocketClient.IsConnected)
            {
                return;
            }

            if (sendInputOnlyDuringMatch && !matchStarted)
            {
                return;
            }

            if (string.IsNullOrEmpty(roomId))
            {
                return;
            }

            float interval = 1f / Mathf.Max(1f, inputSendRateHz);
            if (Time.unscaledTime < nextInputSendAt)
            {
                return;
            }

            nextInputSendAt = Time.unscaledTime + interval;
            HotDynamitePlayerInput inputSource = ResolveInputSource();
            Vector2 move = inputSource != null ? inputSource.MoveVector : Vector2.zero;

            InputMessage message = new InputMessage
            {
                payload = new InputPayload
                {
                    roomId = roomId,
                    seq = ++inputSequence,
                    moveX = move.x,
                    moveY = move.y,
                    clientTimeMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                }
            };

            _ = SendMessageAsync(message);
        }

        private HotDynamitePlayerInput ResolveInputSource()
        {
            if (localInput != null)
            {
                return localInput;
            }

            return gameManager != null ? gameManager.GetLocalNetworkInput() : null;
        }

        public async Task ConnectAsync()
        {
            if (webSocketClient == null)
            {
                return;
            }

            await webSocketClient.ConnectAsync();
        }

        public async Task DisconnectAsync()
        {
            if (webSocketClient == null)
            {
                return;
            }

            await webSocketClient.DisconnectAsync();
        }

        public async Task CreateRoomAsync(string roomName, int maxPlayers = 4, bool withBots = true)
        {
            CreateRoomMessage message = new CreateRoomMessage
            {
                payload = new CreateRoomPayload
                {
                    roomName = roomName,
                    maxPlayers = maxPlayers,
                    withBots = withBots
                }
            };

            await SendMessageAsync(message);
        }

        public async Task JoinRoomAsync(string targetRoomId)
        {
            JoinRoomMessage message = new JoinRoomMessage
            {
                payload = new JoinRoomPayload
                {
                    roomId = targetRoomId
                }
            };

            await SendMessageAsync(message);
        }

        public async Task SetReadyAsync(bool ready)
        {
            ReadyMessage message = new ReadyMessage
            {
                payload = new ReadyPayload
                {
                    roomId = roomId,
                    ready = ready
                }
            };

            await SendMessageAsync(message);
        }

        public async Task StartMatchAsync()
        {
            StartMatchMessage message = new StartMatchMessage
            {
                payload = new StartMatchPayload
                {
                    roomId = roomId
                }
            };

            await SendMessageAsync(message);
        }

        private async Task SendMessageAsync<T>(T message)
        {
            if (webSocketClient == null || !webSocketClient.IsConnected)
            {
                return;
            }

            string json = HotDynamiteWireJson.ToJson(message);
            if (verboseLogging)
            {
                Debug.Log($"HotDynamiteNetworkAdapter send: {json}");
            }

            await webSocketClient.SendTextAsync(json);
        }

        private void OnMessageReceived(string json)
        {
            if (!HotDynamiteWireJson.TryGetMessageType(json, out string type))
            {
                UnknownMessageReceived?.Invoke(string.Empty, json);
                return;
            }

            switch (type)
            {
                case "room_state":
                    HandleRoomState(json);
                    break;
                case "match_started":
                    HandleMatchStarted(json);
                    break;
                case "state_snapshot":
                    HandleStateSnapshot(json);
                    break;
                case "match_ended":
                    HandleMatchEnded(json);
                    break;
                case "error":
                    HandleError(json);
                    break;
                default:
                    UnknownMessageReceived?.Invoke(type, json);
                    break;
            }
        }

        private void HandleRoomState(string json)
        {
            RoomStateMessage message = JsonUtility.FromJson<RoomStateMessage>(json);
            if (message == null || message.payload == null)
            {
                return;
            }

            roomId = message.payload.roomId;
            localPlayerId = message.payload.youId;
            RoomStateReceived?.Invoke(message.payload);
        }

        private void HandleMatchStarted(string json)
        {
            MatchStartedMessage message = JsonUtility.FromJson<MatchStartedMessage>(json);
            if (message == null || message.payload == null)
            {
                return;
            }

            matchStarted = true;
            MatchStartedReceived?.Invoke(message.payload);
        }

        private void HandleStateSnapshot(string json)
        {
            StateSnapshotMessage message = JsonUtility.FromJson<StateSnapshotMessage>(json);
            if (message == null || message.payload == null)
            {
                return;
            }

            SnapshotReceived?.Invoke(message.payload);
        }

        private void HandleMatchEnded(string json)
        {
            MatchEndedMessage message = JsonUtility.FromJson<MatchEndedMessage>(json);
            if (message == null || message.payload == null)
            {
                return;
            }

            matchStarted = false;
            MatchEndedReceived?.Invoke(message.payload);
        }

        private void HandleError(string json)
        {
            ErrorMessage message = JsonUtility.FromJson<ErrorMessage>(json);
            if (message == null || message.payload == null)
            {
                return;
            }

            ErrorReceived?.Invoke(message.payload);
        }

        private void OnSocketClosed(int closeCode, string reason)
        {
            if (verboseLogging)
            {
                Debug.Log($"HotDynamiteNetworkAdapter socket closed ({closeCode}): {reason}");
            }

            matchStarted = false;
        }
    }
}
