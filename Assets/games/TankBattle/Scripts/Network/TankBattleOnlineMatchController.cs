using System;
using System.Collections.Generic;
using UnityEngine;

namespace TankBattle.Networking
{
    public class TankBattleOnlineMatchController : MonoBehaviour
    {
        [Header("Match Flow")]
        [SerializeField, Min(2f)] private float randomModeAutoStartSeconds = 10f;
        [SerializeField, Min(0f)] private float matchStartCountdownSeconds = 1f;

        [Header("Networking")]
        [SerializeField, Min(0.02f)] private float inputSendIntervalSeconds = 0.05f;
        [SerializeField, Min(0.02f)] private float snapshotSendIntervalSeconds = 0.05f;
        [SerializeField] private bool verboseMatchDiagnostics;
        [SerializeField] private bool forceDetailedDiagnostics;
        [SerializeField, Min(0.1f)] private float diagnosticsIntervalSeconds = 0.5f;

        private TankBattleGameManager gameManager;
        private TankBattleLobbyNetworkClient networkClient;
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
        private int localBulletHitSequence;
        private float nextInputSendDiagnosticAt;
        private float nextInputReceiveDiagnosticAt;
        private readonly HashSet<string> receivedBulletHitKeys = new HashSet<string>();
        private readonly Queue<string> receivedBulletHitOrder = new Queue<string>();
        private const int MaxBulletHitDeduplicationEntries = 256;

        public event Action<int> MatchEnded;

        public bool CanHostStartMatch => isInitialized
            && networkClient != null
            && networkClient.IsHost
            && networkClient.IsInRoom
            && networkClient.RoomMode == TankBattleRoomModes.Private
            && !matchPreparing
            && !matchRunning
            && networkClient.OccupiedSeats >= 2;

        public void Initialize(
            TankBattleGameManager gameManager,
            GameObject lobbyCanvas,
            GameObject gameContainer,
            TankBattleLobbyNetworkClient networkClient)
        {
            if (gameManager == null || networkClient == null)
            {
                Debug.LogError("TankBattleOnlineMatchController: GameManager and NetworkClient are required.");
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

            if (networkClient.IsHost && !matchPreparing && !matchRunning && hostAutoStartAt > 0f && Time.time >= hostAutoStartAt)
            {
                LogDiagnostic("Host auto-start timer reached. Starting match preparation.");
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

                if (Time.time >= nextInputSendAt)
                {
                    nextInputSendAt = Time.time + inputSendIntervalSeconds;
                    SendLocalInput();
                }

                if (gameManager.IsGameOver && !matchEndSent)
                {
                    int winningChairId = gameManager.ResolveWinningChairFromCurrentMatch();
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

        private void HandleJoinedRoom(TankBattleJoinedMessage joined)
        {
            ResetMatchState();
            LogDiagnostic(
                $"Joined room={networkClient.CurrentRoomId}, localUser={networkClient.LocalUserId}, localChair={networkClient.CurrentChairId}, host={networkClient.HostUserId}, mode={networkClient.RoomMode}, chairs={DescribeChairs(networkClient.CurrentChairs)}");

            if (!networkClient.IsHost)
            {
                return;
            }

            if (networkClient.RoomMode == TankBattleRoomModes.Private)
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
            LogDiagnostic($"Room occupancy changed: occupied={occupiedSeats}/{capacity}, chairs={DescribeChairs(chairs)}");

            if (!networkClient.IsHost || matchPreparing || matchRunning)
            {
                return;
            }

            if (networkClient.RoomMode == TankBattleRoomModes.Private)
            {
                return;
            }

            if (occupiedSeats >= capacity && capacity > 0)
            {
                StartHostMatchPreparation();
            }
        }

        private void HandleMatchPrepareReceived(string senderUserId, TankBattleMatchPreparePayload payload)
        {
            if (payload == null || networkClient.IsHost || !IsTrustedHostMessage(senderUserId, payload.roomId, payload.hostUserId))
            {
                return;
            }

            matchPreparing = true;
            ActivateGameView();
            int localChairId = ResolveLocalChairIdForRoster(payload.roster);
            gameManager.StartOnlineGame(
                payload.roster,
                networkClient.LocalUserId,
                localChairId,
                simulationAuthority: false,
                payload.simulationSeed,
                payload.countdownSeconds);
            matchRunning = true;
            nextInputSendAt = Time.time;
            latestAppliedSnapshotTick = -1;
            LogDiagnostic($"Client match_prepare applied. localUser={networkClient.LocalUserId}, localChair={localChairId}.");
            LogDiagnostic($"Client roster from host: {DescribeRoster(payload.roster)}");
            LogDiagnostic("Client started match from host match_prepare payload.");
        }

        private void HandleStateSnapshotReceived(string senderUserId, TankBattleStateSnapshotPayload payload)
        {
            if (payload == null || networkClient.IsHost || !IsTrustedHostMessage(senderUserId, payload.roomId, payload.hostUserId))
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

        private void HandleMatchEndReceived(string senderUserId, TankBattleMatchEndPayload payload)
        {
            if (!IsTrustedHostMessage(senderUserId, payload != null ? payload.roomId : null, payload != null ? payload.hostUserId : null))
            {
                return;
            }

            matchRunning = false;
            matchPreparing = false;
            int winningChairId = payload != null ? payload.winningChairId : -1;
            gameManager.OnNetworkMatchEnded(winningChairId);
            MatchEnded?.Invoke(winningChairId);
        }

        private void HandleRoomMessageReceived(string fromUserId, string payloadJson)
        {
            if (!matchRunning || string.IsNullOrWhiteSpace(payloadJson) || payloadJson == "null")
            {
                return;
            }

            if (!string.IsNullOrEmpty(fromUserId)
                && !string.IsNullOrEmpty(networkClient.LocalUserId)
                && string.Equals(fromUserId, networkClient.LocalUserId, StringComparison.Ordinal))
            {
                return;
            }

            TankBattleRoomPayloadEnvelope envelope = JsonUtility.FromJson<TankBattleRoomPayloadEnvelope>(payloadJson);
            if (envelope == null)
            {
                return;
            }

            if (envelope.kind == TankBattlePayloadKinds.BulletHit)
            {
                HandleBulletHitMessage(fromUserId, payloadJson);
                return;
            }

            if (envelope.kind != TankBattlePayloadKinds.PlayerInput)
            {
                return;
            }

            TankBattlePlayerInputPayload payload = JsonUtility.FromJson<TankBattlePlayerInputPayload>(payloadJson);
            if (payload == null)
            {
                return;
            }

            int resolvedChairId = ResolveChairIdForSender(fromUserId, payload);
            if (resolvedChairId >= 0 && !IsRemoteHumanChair(resolvedChairId))
            {
                resolvedChairId = -1;
            }

            if (resolvedChairId < 0)
            {
                int fallbackChair = ResolveFallbackOpponentChair();
                if (fallbackChair >= 0 && IsRemoteHumanChair(fallbackChair))
                {
                    resolvedChairId = fallbackChair;
                    LogDiagnostic($"Resolved player_input via fallback chair={resolvedChairId} from sender={fromUserId}.");
                }
            }

            if (resolvedChairId < 0)
            {
                LogDiagnostic($"Dropped player_input from {fromUserId}: unresolved chair.");
                return;
            }

            gameManager.SetNetworkInput(resolvedChairId, new Vector2(payload.moveX, payload.moveY), payload.shootPressed);
            gameManager.SetNetworkReportedState(
                resolvedChairId,
                new Vector2(payload.x, payload.y),
                payload.facingAngle,
                new Vector2(payload.velocityX, payload.velocityY));
            if (ShouldLogInputReceiveDiagnostic())
            {
                LogDiagnostic(
                    $"Received input from={fromUserId}, payloadChair={payload.chairId}, resolvedChair={resolvedChairId}, playerId={payload.playerId}, seq={payload.sequence}, move=({payload.moveX:0.00},{payload.moveY:0.00}), pos=({payload.x:0.00},{payload.y:0.00}), facing={payload.facingAngle:0.0}, shoot={payload.shootPressed}");
            }
        }

        private void HandleBulletHitMessage(string fromUserId, string payloadJson)
        {
            TankBattleBulletHitPayload payload = JsonUtility.FromJson<TankBattleBulletHitPayload>(payloadJson);
            if (payload == null)
            {
                return;
            }

            if (!string.IsNullOrEmpty(payload.roomId) && payload.roomId != networkClient.CurrentRoomId)
            {
                return;
            }

            string dedupeKey = $"{fromUserId}:{payload.sequence}";
            if (!RegisterBulletHitDeduplication(dedupeKey))
            {
                return;
            }

            gameManager.ApplyNetworkBulletHit(payload.shooterChairId, payload.victimChairId);
        }

        public async void BroadcastLocalBulletHit(int shooterChairId, int victimChairId)
        {
            if (!isInitialized || networkClient == null || !networkClient.IsInRoom || !matchRunning)
            {
                return;
            }

            TankBattleBulletHitPayload payload = new TankBattleBulletHitPayload
            {
                roomId = networkClient.CurrentRoomId,
                playerId = networkClient.LocalUserId,
                sequence = localBulletHitSequence++,
                shooterChairId = shooterChairId,
                victimChairId = victimChairId
            };

            try
            {
                await networkClient.BroadcastToOthersAsync(JsonUtility.ToJson(payload));
            }
            catch (Exception exception)
            {
                Debug.LogError($"TankBattleOnlineMatchController: Failed to broadcast bullet_hit. {exception.Message}");
            }
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
                return;
            }

            StartHostMatchPreparation();
        }

        private async void StartHostMatchPreparation()
        {
            if (!isInitialized || !networkClient.IsHost || matchPreparing || matchRunning)
            {
                return;
            }

            TankBattleMatchPreparePayload payload = BuildMatchPreparePayload();
            if (payload == null)
            {
                return;
            }

            matchPreparing = true;
            ActivateGameView();
            int localChairId = ResolveLocalChairIdForRoster(payload.roster);
            gameManager.StartOnlineGame(
                payload.roster,
                networkClient.LocalUserId,
                localChairId,
                simulationAuthority: true,
                payload.simulationSeed,
                payload.countdownSeconds);
            matchRunning = true;
            nextSnapshotSendAt = Time.time + 0.1f;
            snapshotTick = 0;
            hostAutoStartAt = -1f;
            LogDiagnostic($"Host match_prepare broadcast roster: {DescribeRoster(payload.roster)}");

            try
            {
                await networkClient.BroadcastToOthersAsync(JsonUtility.ToJson(payload));
            }
            catch (Exception exception)
            {
                Debug.LogError($"TankBattleOnlineMatchController: Failed to broadcast match_prepare. {exception.Message}");
            }
        }

        private TankBattleMatchPreparePayload BuildMatchPreparePayload()
        {
            string[] chairs = networkClient.CurrentChairs;
            int capacity = Mathf.Max(networkClient.CurrentCapacity, 2);
            if (chairs == null || chairs.Length == 0)
            {
                chairs = new string[capacity];
            }

            int mapSeed = UnityEngine.Random.Range(1, int.MaxValue);
            List<TankBattleRosterEntry> roster = new List<TankBattleRosterEntry>(capacity);
            for (int chairId = 0; chairId < capacity; chairId++)
            {
                string userId = chairId < chairs.Length ? chairs[chairId] : null;
                bool isBot = string.IsNullOrEmpty(userId);
                if (isBot)
                {
                    userId = $"bot-{networkClient.CurrentRoomId}-{chairId}";
                }

                roster.Add(new TankBattleRosterEntry
                {
                    userId = userId,
                    chairId = chairId,
                    isBot = isBot,
                    isHost = userId == networkClient.HostUserId,
                    appearanceIndex = chairId
                });
            }

            return new TankBattleMatchPreparePayload
            {
                roomMode = networkClient.RoomMode,
                roomId = networkClient.CurrentRoomId,
                hostUserId = networkClient.HostUserId,
                fillMissingSeatsWithBots = true,
                targetPlayerCount = capacity,
                countdownSeconds = matchStartCountdownSeconds,
                simulationSeed = mapSeed,
                roster = roster.ToArray()
            };
        }

        private async void BroadcastSnapshot()
        {
            if (!networkClient.IsHost || gameManager == null)
            {
                return;
            }

            TankBattleStateSnapshotPayload snapshot = gameManager.BuildStateSnapshot(networkClient.HostUserId, snapshotTick++);
            snapshot.roomId = networkClient.CurrentRoomId;

            try
            {
                await networkClient.BroadcastToOthersAsync(JsonUtility.ToJson(snapshot));
            }
            catch (Exception exception)
            {
                Debug.LogError($"TankBattleOnlineMatchController: Failed to broadcast snapshot. {exception.Message}");
            }
        }

        private async void BroadcastMatchEnd(int winningChairId)
        {
            TankBattleMatchEndPayload payload = new TankBattleMatchEndPayload
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
                Debug.LogError($"TankBattleOnlineMatchController: Failed to broadcast match end. {exception.Message}");
            }
        }

        private async void SendLocalInput()
        {
            if (!gameManager.TryGetLocalInputForNetwork(out int chairId, out Vector2 move, out bool shootPressed))
            {
                LogDiagnostic("SendLocalInput skipped because local input player was unresolved or input capture unavailable.");
                return;
            }

            TankBattlePlayer localPlayer = gameManager.GetPlayerByChair(chairId);
            Vector2 localPosition = localPlayer != null ? localPlayer.Position : Vector2.zero;
            Vector2 localVelocity = localPlayer != null ? localPlayer.EstimatedVelocity : Vector2.zero;
            float localFacing = localPlayer != null ? localPlayer.FacingAngle : 0f;

            TankBattlePlayerInputPayload payload = new TankBattlePlayerInputPayload
            {
                playerId = networkClient.LocalUserId,
                chairId = chairId,
                sequence = localInputSequence++,
                moveX = move.x,
                moveY = move.y,
                x = localPosition.x,
                y = localPosition.y,
                facingAngle = localFacing,
                velocityX = localVelocity.x,
                velocityY = localVelocity.y,
                shootPressed = shootPressed
            };

            try
            {
                await networkClient.BroadcastToOthersAsync(JsonUtility.ToJson(payload));
                if (ShouldLogInputSendDiagnostic())
                {
                    LogDiagnostic(
                        $"Client sent input localUser={networkClient.LocalUserId}, chair={chairId}, seq={payload.sequence}, move=({move.x:0.00},{move.y:0.00}), shoot={shootPressed}");
                }
            }
            catch (Exception exception)
            {
                Debug.LogError($"TankBattleOnlineMatchController: Failed to send player input. {exception.Message}");
            }
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
            localBulletHitSequence = 0;
            receivedBulletHitKeys.Clear();
            receivedBulletHitOrder.Clear();
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

        private int ResolveLocalChairIdForRoster(TankBattleRosterEntry[] roster)
        {
            if (roster != null && !string.IsNullOrEmpty(networkClient.LocalUserId))
            {
                for (int i = 0; i < roster.Length; i++)
                {
                    TankBattleRosterEntry entry = roster[i];
                    if (entry != null && string.Equals(entry.userId, networkClient.LocalUserId, StringComparison.Ordinal))
                    {
                        return entry.chairId;
                    }
                }
            }

            if (networkClient.CurrentChairId >= 0)
            {
                return networkClient.CurrentChairId;
            }

            if (roster != null && !networkClient.IsHost)
            {
                for (int i = 0; i < roster.Length; i++)
                {
                    TankBattleRosterEntry entry = roster[i];
                    if (entry != null && !entry.isBot && !entry.isHost)
                    {
                        return entry.chairId;
                    }
                }
            }

            return -1;
        }

        private int ResolveChairIdForSender(string senderUserId, TankBattlePlayerInputPayload payload)
        {
            string[] chairs = networkClient.CurrentChairs;

            if (!string.IsNullOrEmpty(senderUserId))
            {
                for (int i = 0; i < chairs.Length; i++)
                {
                    if (string.Equals(chairs[i], senderUserId, StringComparison.Ordinal))
                    {
                        return i;
                    }
                }
            }

            if (payload != null && !string.IsNullOrEmpty(payload.playerId))
            {
                for (int i = 0; i < chairs.Length; i++)
                {
                    if (string.Equals(chairs[i], payload.playerId, StringComparison.Ordinal))
                    {
                        return i;
                    }
                }
            }

            if (payload != null && payload.chairId >= 0)
            {
                return payload.chairId;
            }

            return -1;
        }

        private int ResolveFallbackOpponentChair()
        {
            if (networkClient == null)
            {
                return -1;
            }

            if (networkClient.CurrentCapacity == 2 && networkClient.HostChairId >= 0)
            {
                return networkClient.HostChairId == 0 ? 1 : 0;
            }

            return -1;
        }

        private bool IsRemoteHumanChair(int chairId)
        {
            if (networkClient == null || chairId < 0)
            {
                return false;
            }

            string[] chairs = networkClient.CurrentChairs;
            if (chairs == null || chairId >= chairs.Length)
            {
                return false;
            }

            string userId = chairs[chairId];
            if (string.IsNullOrEmpty(userId))
            {
                return false;
            }

            if (!string.IsNullOrEmpty(networkClient.LocalUserId)
                && string.Equals(userId, networkClient.LocalUserId, StringComparison.Ordinal))
            {
                return false;
            }

            return true;
        }

        private void LogDiagnostic(string message)
        {
            if (!verboseMatchDiagnostics && !forceDetailedDiagnostics)
            {
                return;
            }

            string side = networkClient == null
                ? "unknown"
                : (networkClient.IsHost ? "host" : "client");
            Debug.Log($"TankBattleOnlineMatchController[{side}]: {message}");
        }

        private bool ShouldLogInputSendDiagnostic()
        {
            float now = Time.unscaledTime;
            if (now < nextInputSendDiagnosticAt)
            {
                return false;
            }

            nextInputSendDiagnosticAt = now + Mathf.Max(0.1f, diagnosticsIntervalSeconds);
            return true;
        }

        private bool ShouldLogInputReceiveDiagnostic()
        {
            float now = Time.unscaledTime;
            if (now < nextInputReceiveDiagnosticAt)
            {
                return false;
            }

            nextInputReceiveDiagnosticAt = now + Mathf.Max(0.1f, diagnosticsIntervalSeconds);
            return true;
        }

        private bool RegisterBulletHitDeduplication(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return true;
            }

            if (!receivedBulletHitKeys.Add(key))
            {
                return false;
            }

            receivedBulletHitOrder.Enqueue(key);
            while (receivedBulletHitOrder.Count > MaxBulletHitDeduplicationEntries)
            {
                string oldKey = receivedBulletHitOrder.Dequeue();
                receivedBulletHitKeys.Remove(oldKey);
            }

            return true;
        }

        private static string DescribeRoster(TankBattleRosterEntry[] roster)
        {
            if (roster == null || roster.Length == 0)
            {
                return "[]";
            }

            List<string> parts = new List<string>(roster.Length);
            for (int i = 0; i < roster.Length; i++)
            {
                TankBattleRosterEntry entry = roster[i];
                if (entry == null)
                {
                    parts.Add("null");
                    continue;
                }

                parts.Add($"[{entry.chairId}:{entry.userId}:bot={entry.isBot}:host={entry.isHost}]");
            }

            return string.Join(", ", parts);
        }

        private static string DescribeChairs(string[] chairs)
        {
            if (chairs == null || chairs.Length == 0)
            {
                return "[]";
            }

            List<string> parts = new List<string>(chairs.Length);
            for (int i = 0; i < chairs.Length; i++)
            {
                string user = string.IsNullOrEmpty(chairs[i]) ? "empty" : chairs[i];
                parts.Add($"[{i}:{user}]");
            }

            return string.Join(", ", parts);
        }
    }
}
