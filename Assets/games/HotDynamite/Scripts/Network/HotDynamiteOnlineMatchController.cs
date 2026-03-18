using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace HotDynamite.Networking
{
    public class HotDynamiteOnlineMatchController : MonoBehaviour
    {
        [Header("Match Flow")]
        [SerializeField, Min(1f)] private float randomModeAutoStartSeconds = 4f;

        [SerializeField, Min(0.1f)] private float matchStartCountdownSeconds = 3f;
        [Header("Networking")]
        [SerializeField, Min(0.02f)] private float inputSendIntervalSeconds = 0.05f;
        [SerializeField, Min(0.02f)] private float snapshotSendIntervalSeconds = 0.066f;
        [SerializeField] private bool verboseMatchDiagnostics = false;

        private HotDynamiteGameManager gameManager;
        private HotDynamiteLobbyNetworkClient networkClient;
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
        private int nextExplosionEventId = 1;
        private int latestAppliedExplosionEventId;

        public event Action<int> MatchEnded;

        public bool CanHostStartMatch => isInitialized
            && networkClient != null
            && networkClient.IsHost
            && networkClient.IsInRoom
            && networkClient.RoomMode == HotDynamiteRoomModes.Private
            && !matchPreparing
            && !matchRunning;
        public void Initialize(
            HotDynamiteGameManager gameManager,
            GameObject lobbyCanvas,
            GameObject gameContainer,
            HotDynamiteLobbyNetworkClient networkClient)
        {
            if (gameManager == null || networkClient == null)
            {
                Debug.LogError("HotDynamiteOnlineMatchController: GameManager and NetworkClient are required.");
                return;
            }

            if (this.gameManager != null)
            {
                this.gameManager.ExplosionOccurred -= HandleHostExplosionOccurred;
            }

            this.gameManager = gameManager;
            this.gameManager.ExplosionOccurred += HandleHostExplosionOccurred;
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

            if (networkClient.IsHost && !matchPreparing && !matchRunning && hostAutoStartAt > 0f && Time.time >= hostAutoStartAt)
            {
                LogDiagnostic($"Host auto-start timer reached at t={Time.time:0.00}. Starting match preparation.");
                StartHostMatchPreparation();
            }

            if (matchRunning)
            {
                if (networkClient.IsHost)
                {
                    LogDiagnostic($"Host match loop active. t={Time.time:0.00}, nextSnapshotAt={nextSnapshotSendAt:0.00}, gameOver={gameManager.IsGameOver}.");
                    if (Time.time >= nextSnapshotSendAt)
                    {
                        LogDiagnostic($"Host snapshot schedule fired at t={Time.time:0.00}. Calling BroadcastSnapshot().");
                        nextSnapshotSendAt = Time.time + snapshotSendIntervalSeconds;
                        BroadcastSnapshot();
                    }

                    if (gameManager.IsGameOver && !matchEndSent)
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
        }

        private void OnDestroy()
        {
            if (gameManager != null)
            {
                gameManager.ExplosionOccurred -= HandleHostExplosionOccurred;
            }

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
            networkClient.ExplosionReceived += HandleExplosionReceived;
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
            networkClient.ExplosionReceived -= HandleExplosionReceived;
            networkClient.MatchEndReceived -= HandleMatchEndReceived;
            networkClient.RoomMessageReceived -= HandleRoomMessageReceived;
            networkClient.Disconnected -= HandleDisconnected;
            networkClient.LeftRoom -= HandleLeftRoom;
            isSubscribed = false;
        }

        private void HandleJoinedRoom(HotDynamiteJoinedMessage joined)
        {
            ResetMatchState();
            LogDiagnostic(
                $"Joined room. localUser={networkClient.LocalUserId}, chair={networkClient.CurrentChairId}, host={networkClient.HostUserId}, capacity={networkClient.CurrentCapacity}.");

            if (networkClient.IsHost)
            {
                if (networkClient.RoomMode == HotDynamiteRoomModes.Private)
                {
                    hostAutoStartAt = -1f;
                    LogDiagnostic("Private room joined by host. Waiting for manual start.");
                }
                else
                {
                    hostAutoStartAt = Time.time + randomModeAutoStartSeconds;
                    LogDiagnostic($"Host auto-start armed for {hostAutoStartAt:0.00} (in {randomModeAutoStartSeconds:0.00}s).");
                }
            }
        }

        private void HandleRoomOccupancyChanged(int occupiedSeats, int capacity, string[] chairs)
        {
            LogDiagnostic($"Room occupancy update {occupiedSeats}/{capacity}. isHost={networkClient.IsHost}, matchPreparing={matchPreparing}, matchRunning={matchRunning}.");

            if (!networkClient.IsHost || matchPreparing || matchRunning)
            {
                return;
            }

            if (networkClient.RoomMode == HotDynamiteRoomModes.Private)
            {
                LogDiagnostic("Private room occupancy changed. Waiting for host manual start.");
                return;
            }

            if (occupiedSeats >= capacity && capacity > 0)
            {
                LogDiagnostic("Room full. Starting host match preparation immediately.");
                StartHostMatchPreparation();
            }
        }

        private void HandleMatchPrepareReceived(string senderUserId, HotDynamiteMatchPreparePayload payload)
        {
            LogDiagnostic(
                $"Received match_prepare from={senderUserId}, room={(payload != null ? payload.roomId : "null")}, host={(payload != null ? payload.hostUserId : "null")}, trusted={(payload != null && IsTrustedHostMessage(senderUserId, payload.roomId, payload.hostUserId))}.");

            if (networkClient.IsHost || payload == null || !IsTrustedHostMessage(senderUserId, payload.roomId, payload.hostUserId))
            {
                return;
            }

            matchPreparing = true;
            ActivateGameView();
            gameManager.StartOnlineGame(payload.roster, networkClient.LocalUserId, simulationAuthority: false, payload.countdownSeconds);
            matchRunning = true;
            nextInputSendAt = Time.time;
            latestAppliedSnapshotTick = -1;
            LogDiagnostic($"Client entered online match from match_prepare. localPlayer={DescribeLocalPlayer()}.");
        }

        private void HandleStateSnapshotReceived(string senderUserId, HotDynamiteStateSnapshotPayload payload)
        {
            LogDiagnostic(
                $"Received snapshot tick={(payload != null ? payload.tick : -1)}, from={senderUserId}, host={(payload != null ? payload.hostUserId : "null")}, trusted={(payload != null && IsTrustedHostMessage(senderUserId, payload.roomId, payload.hostUserId))}.");

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
            LogDiagnostic($"Applied snapshot tick={payload.tick}. localPlayer={DescribeLocalPlayer()}.");
        }

        private void HandleExplosionReceived(string senderUserId, HotDynamiteExplosionPayload payload)
        {
            if (networkClient.IsHost || payload == null || !IsTrustedHostMessage(senderUserId, payload.roomId, payload.hostUserId))
            {
                return;
            }

            if (payload.eventId <= latestAppliedExplosionEventId)
            {
                return;
            }

            latestAppliedExplosionEventId = payload.eventId;
            gameManager.PlayExplosionEffect(
                new Vector2(payload.x, payload.y),
                new Color(payload.colorR, payload.colorG, payload.colorB, payload.colorA));
        }

        private void HandleMatchEndReceived(string senderUserId, HotDynamiteMatchEndPayload payload)
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

            HotDynamiteRoomPayloadEnvelope envelope = JsonUtility.FromJson<HotDynamiteRoomPayloadEnvelope>(payloadJson);
            if (envelope == null || envelope.kind != HotDynamitePayloadKinds.PlayerInput)
            {
                return;
            }

            HotDynamitePlayerInputPayload payload = JsonUtility.FromJson<HotDynamitePlayerInputPayload>(payloadJson);
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
        private async void StartHostMatchPreparation()
        {
            if (!isInitialized || !networkClient.IsHost || matchPreparing || matchRunning)
            {
                LogDiagnostic(
                    $"Skipped host match preparation. initialized={isInitialized}, isHost={networkClient.IsHost}, matchPreparing={matchPreparing}, matchRunning={matchRunning}.");
                return;
            }

            HotDynamiteMatchPreparePayload payload = BuildMatchPreparePayload();
            if (payload == null)
            {
                LogDiagnostic("BuildMatchPreparePayload returned null.");
                return;
            }

            matchPreparing = true;
            ActivateGameView();
            gameManager.StartOnlineGame(payload.roster, networkClient.LocalUserId, simulationAuthority: true, payload.countdownSeconds);
            matchRunning = true;
            nextSnapshotSendAt = Time.time + 0.1f;
            snapshotTick = 0;
            hostAutoStartAt = -1f;
            LogDiagnostic($"Host started match preparation. roster={DescribeRoster(payload.roster)}.");

            try
            {
                await networkClient.BroadcastToOthersAsync(JsonUtility.ToJson(payload));
                LogDiagnostic("Host broadcasted match_prepare.");
            }
            catch (Exception exception)
            {
                Debug.LogError($"HotDynamiteOnlineMatchController: Failed to broadcast match prepare payload. {exception.Message}");
            }
        }

        private HotDynamiteMatchPreparePayload BuildMatchPreparePayload()
        {
            string[] chairs = networkClient.CurrentChairs;
            int capacity = Mathf.Max(networkClient.CurrentCapacity, 2);
            if (chairs == null || chairs.Length == 0)
            {
                chairs = new string[capacity];
            }

            List<HotDynamiteRosterEntry> roster = new List<HotDynamiteRosterEntry>(capacity);
            for (int chairId = 0; chairId < capacity; chairId++)
            {
                string userId = chairId < chairs.Length ? chairs[chairId] : null;
                bool isBot = string.IsNullOrEmpty(userId);
                if (isBot)
                {
                    userId = $"bot-{networkClient.CurrentRoomId}-{chairId}";
                }

                roster.Add(new HotDynamiteRosterEntry
                {
                    userId = userId,
                    chairId = chairId,
                    isBot = isBot,
                    isHost = userId == networkClient.HostUserId,
                    appearanceIndex = chairId
                });
            }

            return new HotDynamiteMatchPreparePayload
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

            LogDiagnostic($"Entered BroadcastSnapshot(). nextTick={snapshotTick}, room={networkClient.CurrentRoomId}, hostUser={networkClient.HostUserId}.");

            HotDynamiteStateSnapshotPayload snapshot = gameManager.BuildStateSnapshot(networkClient.HostUserId, snapshotTick++);
            snapshot.roomId = networkClient.CurrentRoomId;

            try
            {
                await networkClient.BroadcastToOthersAsync(JsonUtility.ToJson(snapshot));
                LogDiagnostic($"Host broadcasted snapshot tick={snapshot.tick}, bombHolderChair={snapshot.bombHolderChairId}, players={(snapshot.players != null ? snapshot.players.Length : 0)}.");
            }
            catch (Exception exception)
            {
                Debug.LogError($"HotDynamiteOnlineMatchController: Failed to broadcast snapshot. {exception.Message}");
            }
        }

        private async void BroadcastMatchEnd(int winningChairId)
        {
            HotDynamiteMatchEndPayload payload = new HotDynamiteMatchEndPayload
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
                Debug.LogError($"HotDynamiteOnlineMatchController: Failed to broadcast match end. {exception.Message}");
            }
        }

        private async void HandleHostExplosionOccurred(Vector2 explosionPosition, Color explosionColor)
        {
            if (!isInitialized || networkClient == null || !networkClient.IsHost || !matchRunning)
            {
                return;
            }

            HotDynamiteExplosionPayload payload = new HotDynamiteExplosionPayload
            {
                roomId = networkClient.CurrentRoomId,
                hostUserId = networkClient.HostUserId,
                eventId = nextExplosionEventId++,
                x = explosionPosition.x,
                y = explosionPosition.y,
                colorR = explosionColor.r,
                colorG = explosionColor.g,
                colorB = explosionColor.b,
                colorA = explosionColor.a
            };

            try
            {
                await networkClient.BroadcastToOthersAsync(JsonUtility.ToJson(payload));
            }
            catch (Exception exception)
            {
                Debug.LogError($"HotDynamiteOnlineMatchController: Failed to broadcast explosion. {exception.Message}");
            }
        }
        private async void SendLocalInput()
        {
            HotDynamitePlayer localPlayer = FindLocalPlayer();
            if (localPlayer == null)
            {
                LogDiagnostic("SendLocalInput skipped because FindLocalPlayer returned null.");
                return;
            }

            if (!localPlayer.IsAlive || localPlayer.HumanInput == null || !localPlayer.gameObject.activeInHierarchy)
            {
                LogDiagnostic(
                    $"SendLocalInput skipped. alive={localPlayer.IsAlive}, hasInput={localPlayer.HumanInput != null}, active={localPlayer.gameObject.activeInHierarchy}, chair={localPlayer.ChairId}.");
                return;
            }

            Vector2 move = localPlayer.HumanInput.MoveVector;
            HotDynamitePlayerInputPayload payload = new HotDynamitePlayerInputPayload
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
                Debug.LogError($"HotDynamiteOnlineMatchController: Failed to send player input. {exception.Message}");
            }
        }

        private HotDynamitePlayer FindLocalPlayer()
        {
            IReadOnlyList<HotDynamitePlayer> players = gameManager.Players;
            for (int i = 0; i < players.Count; i++)
            {
                HotDynamitePlayer player = players[i];
                if (player != null && player.UserId == networkClient.LocalUserId)
                {
                    return player;
                }
            }

            return null;
        }

        private void LogDiagnostic(string message)
        {
            if (!verboseMatchDiagnostics)
            {
                return;
            }

            string side = networkClient == null
                ? "unknown"
                : (networkClient.IsHost ? "host" : "client");
            Debug.Log($"HotDynamiteOnlineMatchController[{side}]: {message}");
        }

        private string DescribeLocalPlayer()
        {
            HotDynamitePlayer localPlayer = FindLocalPlayer();
            if (localPlayer == null)
            {
                return "null";
            }

            return $"user={localPlayer.UserId}, chair={localPlayer.ChairId}, mode={localPlayer.ControlMode}, alive={localPlayer.IsAlive}, active={localPlayer.gameObject.activeInHierarchy}";
        }

        private static string DescribeRoster(HotDynamiteRosterEntry[] roster)
        {
            if (roster == null || roster.Length == 0)
            {
                return "empty";
            }

            string[] parts = new string[roster.Length];
            for (int i = 0; i < roster.Length; i++)
            {
                HotDynamiteRosterEntry entry = roster[i];
                parts[i] = entry == null
                    ? "null"
                    : $"[{entry.chairId}:{entry.userId}:bot={entry.isBot}:host={entry.isHost}]";
            }

            return string.Join(", ", parts);
        }

        private int FindWinningChair()
        {
            IReadOnlyList<HotDynamitePlayer> players = gameManager.Players;
            for (int i = 0; i < players.Count; i++)
            {
                HotDynamitePlayer player = players[i];
                if (player != null && player.IsAlive)
                {
                    return player.ChairId;
                }
            }

            return -1;
        }

        private void ActivateGameView()
        {
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
            nextExplosionEventId = 1;
            latestAppliedExplosionEventId = 0;
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
    }
}





