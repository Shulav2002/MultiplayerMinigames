using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace StayAlive.Networking
{
    public class StayAliveOnlineMatchController : MonoBehaviour
    {
        [Header("Match Flow")]
        [SerializeField, Min(1f)] private float randomModeAutoStartSeconds = 4f;
        [SerializeField, Min(0.1f)] private float matchStartCountdownSeconds = 3f;

        [Header("Networking")]
        [SerializeField, Min(0.02f)] private float inputSendIntervalSeconds = 0.05f;
        [SerializeField, Min(0.02f)] private float snapshotSendIntervalSeconds = 0.066f;
        [SerializeField] private bool verboseMatchDiagnostics = false;

        private StayAliveGameManager gameManager;
        private StayAliveLobbyNetworkClient networkClient;
        private GameObject lobbyCanvas;
        private GameObject gameContainer;

        private bool isInitialized;
        private bool isSubscribed;
        private bool matchPreparing;
        private bool matchRunning;
        private bool matchEndSent;
        private float hostAutoStartAt = -1f;
        private float nextInputSendAt;
        private float nextSnapshotSendAt;
        private int snapshotTick;
        private int latestAppliedSnapshotTick = -1;
        private int localInputSequence;

        public event Action<int> MatchEnded;

        public bool CanHostStartMatch => isInitialized
            && networkClient != null
            && networkClient.IsHost
            && networkClient.IsConnected
            && networkClient.IsInRoom
            && networkClient.RoomMode == StayAliveRoomModes.Private
            && !matchPreparing
            && !matchRunning;

        private void Awake()
        {
            ResetMatchState();
        }

        private void OnEnable()
        {
            ResetMatchState();
        }

        public void Initialize(
            StayAliveGameManager gameManager,
            GameObject lobbyCanvas,
            GameObject gameContainer,
            StayAliveLobbyNetworkClient networkClient)
        {
            if (gameManager == null || networkClient == null)
            {
                Debug.LogError("StayAliveOnlineMatchController: GameManager and NetworkClient are required.");
                return;
            }

            this.gameManager = gameManager;
            this.lobbyCanvas = lobbyCanvas;
            this.gameContainer = gameContainer;

            if (this.networkClient != networkClient)
            {
                Unsubscribe();
                this.networkClient = networkClient;
            }

            Subscribe();
            isInitialized = true;
        }

        private void Update()
        {
            if (!isInitialized || networkClient == null || gameManager == null)
            {
                return;
            }

            if (networkClient.IsConnected
                && networkClient.IsInRoom
                && networkClient.IsHost
                && !matchPreparing
                && !matchRunning
                && hostAutoStartAt > 0f
                && Time.time >= hostAutoStartAt)
            {
                LogDiagnostic($"Host auto-start timer reached at t={Time.time:0.00}. Starting match preparation.");
                StartHostMatchPreparation();
            }

            if (!matchRunning)
            {
                return;
            }

            if (networkClient.IsHost)
            {
                if (Time.time >= nextSnapshotSendAt)
                {
                    nextSnapshotSendAt = Time.time + snapshotSendIntervalSeconds;
                    BroadcastSnapshot();
                }

                if (gameManager.IsMatchOver && !matchEndSent)
                {
                    int winningChairId = FindWinningChair();
                    matchEndSent = true;
                    matchRunning = false;
                    matchPreparing = false;
                    BroadcastMatchEnd(winningChairId);
                    MatchEnded?.Invoke(winningChairId);
                }
            }
            else if (Time.time >= nextInputSendAt)
            {
                nextInputSendAt = Time.time + inputSendIntervalSeconds;
                SendLocalInput();
            }
        }

        private void OnDestroy()
        {
            Unsubscribe();
        }

        private void Subscribe()
        {
            if (isSubscribed || networkClient == null)
            {
                return;
            }

            networkClient.JoinedRoom += HandleJoinedRoom;
            networkClient.RoomOccupancyChanged += HandleRoomOccupancyChanged;
            networkClient.MatchPrepareReceived += HandleMatchPrepareReceived;
            networkClient.StateSnapshotReceived += HandleStateSnapshotReceived;
            networkClient.MatchEndReceived += HandleMatchEndReceived;
            networkClient.RoomMessageReceived += HandleRoomMessageReceived;
            networkClient.Disconnected += HandleDisconnected;
            networkClient.LeftRoom += HandleLeftRoom;
            isSubscribed = true;
        }

        private void Unsubscribe()
        {
            if (!isSubscribed || networkClient == null)
            {
                return;
            }

            networkClient.JoinedRoom -= HandleJoinedRoom;
            networkClient.RoomOccupancyChanged -= HandleRoomOccupancyChanged;
            networkClient.MatchPrepareReceived -= HandleMatchPrepareReceived;
            networkClient.StateSnapshotReceived -= HandleStateSnapshotReceived;
            networkClient.MatchEndReceived -= HandleMatchEndReceived;
            networkClient.RoomMessageReceived -= HandleRoomMessageReceived;
            networkClient.Disconnected -= HandleDisconnected;
            networkClient.LeftRoom -= HandleLeftRoom;
            isSubscribed = false;
        }

        private void HandleJoinedRoom(StayAliveJoinedMessage joined)
        {
            ResetMatchState();
            LogDiagnostic(
                $"Joined room. localUser={networkClient.LocalUserId}, chair={networkClient.CurrentChairId}, host={networkClient.HostUserId}, capacity={networkClient.CurrentCapacity}.");

            if (!networkClient.IsHost)
            {
                return;
            }

            if (networkClient.RoomMode == StayAliveRoomModes.Private)
            {
                hostAutoStartAt = -1f;
                LogDiagnostic("Private room joined by host. Waiting for manual start.");
            }
            else
            {
                hostAutoStartAt = Time.time + randomModeAutoStartSeconds;
                LogDiagnostic($"Host auto-start armed for {hostAutoStartAt:0.00}.");
            }
        }

        private void HandleRoomOccupancyChanged(int occupiedSeats, int capacity, string[] chairs)
        {
            LogDiagnostic($"Room occupancy update {occupiedSeats}/{capacity}. isHost={networkClient.IsHost}, matchPreparing={matchPreparing}, matchRunning={matchRunning}.");

            if (!networkClient.IsHost || matchPreparing || matchRunning)
            {
                return;
            }

            if (networkClient.RoomMode == StayAliveRoomModes.Private)
            {
                return;
            }

            if (occupiedSeats >= capacity && capacity > 0)
            {
                StartHostMatchPreparation();
            }
        }

        private void HandleMatchPrepareReceived(string senderUserId, StayAliveMatchPreparePayload payload)
        {
            if (networkClient.IsHost || payload == null || !IsTrustedHostMessage(senderUserId, payload.roomId, payload.hostUserId))
            {
                return;
            }

            if (!CanEnterOnlineMatch(payload.roomId))
            {
                LogDiagnostic("Ignoring match_prepare because there is no active connected online room.");
                ResetToLobbyState();
                return;
            }

            matchPreparing = true;
            ActivateGameView();
            gameManager.StartOnlineGame(payload.roster, networkClient.LocalUserId, simulationAuthority: false, payload.countdownSeconds);
            matchRunning = true;
            nextInputSendAt = Time.time;
            latestAppliedSnapshotTick = -1;
        }

        private void HandleStateSnapshotReceived(string senderUserId, StayAliveStateSnapshotPayload payload)
        {
            if (networkClient.IsHost || payload == null || !IsTrustedHostMessage(senderUserId, payload.roomId, payload.hostUserId))
            {
                return;
            }

            if (payload.tick <= latestAppliedSnapshotTick)
            {
                return;
            }

            latestAppliedSnapshotTick = payload.tick;
            gameManager.ApplyStateSnapshot(payload);
        }

        private void HandleMatchEndReceived(string senderUserId, StayAliveMatchEndPayload payload)
        {
            if (!IsTrustedHostMessage(senderUserId, payload != null ? payload.roomId : null, payload != null ? payload.hostUserId : null))
            {
                return;
            }

            matchRunning = false;
            matchPreparing = false;
            MatchEnded?.Invoke(payload != null ? payload.winningChairId : -1);
        }

        private void HandleRoomMessageReceived(string fromUserId, string payloadJson)
        {
            if (!networkClient.IsHost || !matchRunning || string.IsNullOrWhiteSpace(payloadJson) || payloadJson == "null")
            {
                return;
            }

            StayAliveRoomPayloadEnvelope envelope = JsonUtility.FromJson<StayAliveRoomPayloadEnvelope>(payloadJson);
            if (envelope == null || envelope.kind != StayAlivePayloadKinds.PlayerInput)
            {
                return;
            }

            StayAlivePlayerInputPayload payload = JsonUtility.FromJson<StayAlivePlayerInputPayload>(payloadJson);
            if (payload == null)
            {
                return;
            }

            gameManager.SetNetworkMoveInput(payload.chairId, new Vector2(payload.moveX, payload.moveY));
        }

        private void HandleDisconnected(WebSocketCloseReason closeReason)
        {
            ResetMatchState();
        }

        private void HandleLeftRoom()
        {
            ResetMatchState();
        }

        public void StartHostedMatchNow()
        {
            if (!CanHostStartMatch)
            {
                LogDiagnostic("Manual host start ignored because the host is not in a startable state.");
                return;
            }

            StartHostMatchPreparation();
        }

        public void ResetToLobbyState()
        {
            ResetMatchState();

            if (lobbyCanvas != null)
            {
                lobbyCanvas.SetActive(true);
            }

            if (gameContainer != null)
            {
                gameContainer.SetActive(false);
            }
        }

        private async void StartHostMatchPreparation()
        {
            if (!CanEnterOnlineMatch()
                || !networkClient.IsHost
                || matchPreparing
                || matchRunning)
            {
                return;
            }

            StayAliveMatchPreparePayload payload = BuildMatchPreparePayload();
            if (payload == null)
            {
                return;
            }

            matchPreparing = true;
            ActivateGameView();
            gameManager.StartOnlineGame(payload.roster, networkClient.LocalUserId, simulationAuthority: true, payload.countdownSeconds);
            matchRunning = true;
            nextSnapshotSendAt = Time.time + 0.1f;
            snapshotTick = 0;
            hostAutoStartAt = -1f;

            try
            {
                await networkClient.BroadcastToOthersAsync(JsonUtility.ToJson(payload));
            }
            catch (Exception exception)
            {
                Debug.LogError($"StayAliveOnlineMatchController: Failed to broadcast match prepare payload. {exception.Message}");
            }
        }

        private StayAliveMatchPreparePayload BuildMatchPreparePayload()
        {
            string[] chairs = networkClient.CurrentChairs;
            int capacity = Mathf.Max(networkClient.CurrentCapacity, 2);
            if (chairs == null || chairs.Length == 0)
            {
                chairs = new string[capacity];
            }

            List<StayAliveRosterEntry> roster = new List<StayAliveRosterEntry>(capacity);
            for (int chairId = 0; chairId < capacity; chairId++)
            {
                string userId = chairId < chairs.Length ? chairs[chairId] : null;
                bool isBot = string.IsNullOrEmpty(userId);
                if (isBot)
                {
                    userId = $"bot-{networkClient.CurrentRoomId}-{chairId}";
                }

                roster.Add(new StayAliveRosterEntry
                {
                    userId = userId,
                    chairId = chairId,
                    isBot = isBot,
                    isHost = userId == networkClient.HostUserId,
                    appearanceIndex = chairId
                });
            }

            return new StayAliveMatchPreparePayload
            {
                roomMode = networkClient.RoomMode,
                roomId = networkClient.CurrentRoomId,
                hostUserId = networkClient.HostUserId,
                fillMissingSeatsWithBots = true,
                targetPlayerCount = capacity,
                countdownSeconds = matchStartCountdownSeconds,
                roster = roster.ToArray()
            };
        }

        private async void BroadcastSnapshot()
        {
            if (!networkClient.IsHost || gameManager == null)
            {
                return;
            }

            StayAliveStateSnapshotPayload snapshot = gameManager.BuildStateSnapshot(networkClient.HostUserId, snapshotTick++);
            snapshot.roomId = networkClient.CurrentRoomId;

            try
            {
                await networkClient.BroadcastToOthersAsync(JsonUtility.ToJson(snapshot));
            }
            catch (Exception exception)
            {
                Debug.LogError($"StayAliveOnlineMatchController: Failed to broadcast snapshot. {exception.Message}");
            }
        }

        private async void BroadcastMatchEnd(int winningChairId)
        {
            StayAliveMatchEndPayload payload = new StayAliveMatchEndPayload
            {
                roomId = networkClient.CurrentRoomId,
                hostUserId = networkClient.HostUserId,
                winningChairId = winningChairId
            };

            try
            {
                await networkClient.BroadcastToOthersAsync(JsonUtility.ToJson(payload));
            }
            catch (Exception exception)
            {
                Debug.LogError($"StayAliveOnlineMatchController: Failed to broadcast match end. {exception.Message}");
            }
        }

        private async void SendLocalInput()
        {
            StayAlivePlayer localPlayer = FindLocalPlayer();
            if (localPlayer == null)
            {
                return;
            }

            if (!localPlayer.IsAlive || localPlayer.HumanInput == null || !localPlayer.gameObject.activeInHierarchy)
            {
                return;
            }

            Vector2 move = localPlayer.HumanInput.MoveVector;
            StayAlivePlayerInputPayload payload = new StayAlivePlayerInputPayload
            {
                playerId = networkClient.LocalUserId,
                chairId = localPlayer.ChairId,
                sequence = localInputSequence++,
                moveX = move.x,
                moveY = move.y
            };

            try
            {
                await networkClient.BroadcastToOthersAsync(JsonUtility.ToJson(payload));
            }
            catch (Exception exception)
            {
                Debug.LogError($"StayAliveOnlineMatchController: Failed to send player input. {exception.Message}");
            }
        }

        private StayAlivePlayer FindLocalPlayer()
        {
            IReadOnlyList<StayAlivePlayer> players = gameManager.Players;
            for (int i = 0; i < players.Count; i++)
            {
                StayAlivePlayer player = players[i];
                if (player != null && player.UserId == networkClient.LocalUserId)
                {
                    return player;
                }
            }

            return null;
        }

        private int FindWinningChair()
        {
            IReadOnlyList<StayAlivePlayer> players = gameManager.Players;
            for (int i = 0; i < players.Count; i++)
            {
                StayAlivePlayer player = players[i];
                if (player != null && player.IsAlive)
                {
                    return player.ChairId;
                }
            }

            return -1;
        }

        private void ActivateGameView()
        {
            if (!CanEnterOnlineMatch())
            {
                LogDiagnostic("Blocked ActivateGameView because there is no active connected online room.");
                ResetToLobbyState();
                return;
            }

            if (lobbyCanvas != null)
            {
                lobbyCanvas.SetActive(false);
            }

            if (gameContainer != null)
            {
                gameContainer.SetActive(true);
            }
        }

        private void ResetMatchState()
        {
            matchPreparing = false;
            matchRunning = false;
            matchEndSent = false;
            hostAutoStartAt = -1f;
            nextInputSendAt = 0f;
            nextSnapshotSendAt = 0f;
            snapshotTick = 0;
            latestAppliedSnapshotTick = -1;
            localInputSequence = 0;
        }

        private bool CanEnterOnlineMatch(string expectedRoomId = null)
        {
            if (!isInitialized || networkClient == null || gameManager == null)
            {
                return false;
            }

            if (!networkClient.IsConnected || !networkClient.IsInRoom || string.IsNullOrEmpty(networkClient.LocalUserId))
            {
                return false;
            }

            if (string.IsNullOrEmpty(networkClient.CurrentRoomId))
            {
                return false;
            }

            if (!string.IsNullOrEmpty(expectedRoomId) && expectedRoomId != networkClient.CurrentRoomId)
            {
                return false;
            }

            return true;
        }

        private bool IsTrustedHostMessage(string senderUserId, string roomId, string hostUserId)
        {
            if (networkClient == null)
            {
                return false;
            }

            if (!string.IsNullOrEmpty(roomId) && roomId != networkClient.CurrentRoomId)
            {
                return false;
            }

            string expectedHostUserId = networkClient.HostUserId;
            if (!string.IsNullOrEmpty(hostUserId) && !string.IsNullOrEmpty(expectedHostUserId) && hostUserId != expectedHostUserId)
            {
                return false;
            }

            if (!string.IsNullOrEmpty(expectedHostUserId) && senderUserId != expectedHostUserId)
            {
                return false;
            }

            return !string.IsNullOrEmpty(senderUserId);
        }

        private void LogDiagnostic(string message)
        {
            if (!verboseMatchDiagnostics)
            {
                return;
            }

            string side = networkClient == null ? "unknown" : (networkClient.IsHost ? "host" : "client");
            Debug.Log($"StayAliveOnlineMatchController[{side}]: {message}");
        }
    }
}
