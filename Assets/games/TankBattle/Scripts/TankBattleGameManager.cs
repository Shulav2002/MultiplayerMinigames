using System;
using System.Collections.Generic;
using TankBattle.Lobby;
using TankBattle.Networking;
using UnityEngine;

namespace TankBattle
{
    public class TankBattleGameManager : MonoBehaviour
    {
        [Header("Map")]
        public TankBattleMapGenerator mapGenerator;

        [Header("Tank Prefabs")]
        public GameObject tank1Prefab;
        public GameObject tank2Prefab;

        [Header("Bullet")]
        public GameObject bulletPrefab;
        public TankBattleBulletSettings bulletSettings;

        [Header("Bot")]
        public TankBattleBotSettings botSettings;

        [Header("UI")]
        public TankBattleUI gameUI;

        [Header("Lobby")]
        [SerializeField] private GameObject lobbyCanvas;

        [Header("Global Config")]
        [SerializeField] private TankBattleGlobalConfig globalConfig;
        [SerializeField] private bool autoCreateGlobalConfigIfMissing = true;

        [Header("Diagnostics")]
        [SerializeField] private bool enableNetworkDiagnostics;
        [SerializeField, Min(0.1f)] private float networkDiagnosticIntervalSeconds = 0.5f;

        private readonly Dictionary<int, TankBattlePlayer> playersByChair = new Dictionary<int, TankBattlePlayer>();
        private readonly Dictionary<int, TankBattleBullet> snapshotBullets = new Dictionary<int, TankBattleBullet>();
        private readonly List<TankBattlePlayer> spawnedPlayers = new List<TankBattlePlayer>();

        private TankBattlePlayer localInputPlayer;
        private string localUserId;
        private int localChairId = -1;
        private int currentMapSeed;
        private int lastOfflineTotalPlayers = 2;
        private int lastOfflineHumanPlayers = 1;
        private int winnerChairId = -1;
        private float nextInputCaptureFailureLogAt;
        private float nextInputCaptureSuccessLogAt;
        private float nextNetworkInputAppliedLogAt;

        public bool IsOnlineMatch { get; private set; }
        public bool HasSimulationAuthority { get; private set; } = true;
        public bool IsGameOver { get; private set; }
        public bool IsMatchStarting { get; private set; }
        public float MatchStartDelayRemaining { get; private set; }

        public event Action<int> GameEnded;

        private void Awake()
        {
            EnsureGlobalConfig();
            EnsureLobbyReferences();
            EnsureLobbyManagerIfNeeded();
        }

        private void Start()
        {
            EnsureLobbyReferences();
            if (lobbyCanvas != null)
            {
                ShowLobbyState();
            }
            else
            {
                StartGameLocal(lastOfflineTotalPlayers, lastOfflineHumanPlayers);
            }
        }

        private void Update()
        {
            if (!IsMatchStarting)
            {
                return;
            }

            MatchStartDelayRemaining = Mathf.Max(0f, MatchStartDelayRemaining - Time.deltaTime);
            if (MatchStartDelayRemaining <= 0f)
            {
                IsMatchStarting = false;
            }
        }

        public void ShowLobbyState()
        {
            ClearMatchObjects();
            IsGameOver = false;
            winnerChairId = -1;
            IsOnlineMatch = false;
            HasSimulationAuthority = true;
            IsMatchStarting = false;
            MatchStartDelayRemaining = 0f;

            if (gameUI != null)
            {
                gameUI.SetGameplayUiVisible(false);
            }
        }

        public void StartGameLocal(int totalPlayers = 2, int humanPlayers = 1)
        {
            totalPlayers = 2;
            humanPlayers = Mathf.Clamp(humanPlayers, 1, totalPlayers);

            lastOfflineTotalPlayers = totalPlayers;
            lastOfflineHumanPlayers = humanPlayers;

            TankBattleRosterEntry[] roster = BuildOfflineRoster(totalPlayers, humanPlayers);
            int seed = UnityEngine.Random.Range(1, int.MaxValue);
            StartMatch(roster, "local-player-0", localChairId: 0, simulationAuthority: true, isOnlineMatch: false, seed, countdownSeconds: 0f);
        }

        public void StartOnlineGame(
            TankBattleRosterEntry[] roster,
            string localUserId,
            int localChairId,
            bool simulationAuthority,
            int mapSeed,
            float countdownSeconds)
        {
            if (roster == null || roster.Length == 0)
            {
                Debug.LogError("TankBattleGameManager: Cannot start online game without roster.");
                return;
            }

            LogNetworkDiagnostic(
                $"StartOnlineGame localUser={localUserId}, localChair={localChairId}, simAuthority={simulationAuthority}, seed={mapSeed}, countdown={countdownSeconds:0.00}, roster={BuildRosterSummary(roster)}");
            StartMatch(roster, localUserId, localChairId, simulationAuthority, isOnlineMatch: true, mapSeed, countdownSeconds);
        }

        public void SetNetworkInput(int chairId, Vector2 moveInput, bool shootPressed)
        {
            if (!playersByChair.TryGetValue(chairId, out TankBattlePlayer player) || player == null)
            {
                if (ShouldLogNow(ref nextNetworkInputAppliedLogAt))
                {
                    LogNetworkDiagnostic($"SetNetworkInput dropped: chair={chairId} not found. move=({moveInput.x:0.00},{moveInput.y:0.00}) shoot={shootPressed}");
                }
                return;
            }

            if (player.IsBotParticipant)
            {
                if (ShouldLogNow(ref nextNetworkInputAppliedLogAt))
                {
                    LogNetworkDiagnostic(
                        $"SetNetworkInput ignored for bot seat: chair={chairId}, user={player.UserId}, move=({moveInput.x:0.00},{moveInput.y:0.00}), shoot={shootPressed}");
                }
                return;
            }

            player.SetNetworkInput(moveInput, shootPressed);
            if (ShouldLogNow(ref nextNetworkInputAppliedLogAt))
            {
                LogNetworkDiagnostic(
                    $"SetNetworkInput applied: chair={chairId}, user={player.UserId}, mode={player.ControlMode}, move=({moveInput.x:0.00},{moveInput.y:0.00}), shoot={shootPressed}");
            }
        }

        public void SetNetworkReportedState(int chairId, Vector2 position, float facingAngle, Vector2 velocity)
        {
            if (!playersByChair.TryGetValue(chairId, out TankBattlePlayer player) || player == null)
            {
                return;
            }

            if (player.IsBotParticipant)
            {
                return;
            }

            player.ApplyNetworkReportedState(position, facingAngle, velocity);
        }

        public void ApplyNetworkBulletHit(int shooterChairId, int victimChairId)
        {
            if (!playersByChair.TryGetValue(victimChairId, out TankBattlePlayer victim) || victim == null)
            {
                return;
            }

            if (gameUI != null)
            {
                gameUI.ApplyNetworkHitByChair(victimChairId);
            }

            ResolveBulletAfterHit(shooterChairId, victim.Position);
        }

        public bool TryGetLocalInputForNetwork(out int chairId, out Vector2 moveInput, out bool shootPressed)
        {
            chairId = -1;
            moveInput = Vector2.zero;
            shootPressed = false;

            if (localInputPlayer == null)
            {
                if (localChairId >= 0)
                {
                    playersByChair.TryGetValue(localChairId, out localInputPlayer);
                }
                else if (!string.IsNullOrEmpty(localUserId))
                {
                    localInputPlayer = FindPlayerByUserId(localUserId);
                }
            }

            if (localInputPlayer == null)
            {
                if (ShouldLogNow(ref nextInputCaptureFailureLogAt))
                {
                    LogNetworkDiagnostic(
                        $"TryGetLocalInputForNetwork: localInputPlayer unresolved. localUser={localUserId}, localChair={localChairId}, players={playersByChair.Count}");
                }
                return false;
            }

            if (!localInputPlayer.TryCaptureInputForNetwork(out moveInput, out shootPressed))
            {
                if (ShouldLogNow(ref nextInputCaptureFailureLogAt))
                {
                    LogNetworkDiagnostic(
                        $"TryGetLocalInputForNetwork: capture unavailable for chair={localInputPlayer.ChairId}, user={localInputPlayer.UserId}, mode={localInputPlayer.ControlMode}.");
                }
                return false;
            }

            chairId = localInputPlayer.ChairId;
            if ((moveInput.sqrMagnitude > 0.001f || shootPressed) && ShouldLogNow(ref nextInputCaptureSuccessLogAt))
            {
                LogNetworkDiagnostic(
                    $"TryGetLocalInputForNetwork: chair={chairId}, user={localInputPlayer.UserId}, mode={localInputPlayer.ControlMode}, move=({moveInput.x:0.00},{moveInput.y:0.00}), shoot={shootPressed}");
            }
            return chairId >= 0;
        }

        public TankBattleStateSnapshotPayload BuildStateSnapshot(string hostUserId, int tick)
        {
            TankBattlePlayerStateSnapshot[] playerSnapshots = BuildPlayerSnapshots();
            TankBattleBulletStateSnapshot[] bulletSnapshots = BuildBulletSnapshots();

            return new TankBattleStateSnapshotPayload
            {
                roomId = string.Empty,
                hostUserId = hostUserId,
                tick = tick,
                isMatchStarting = IsMatchStarting,
                matchStartDelayRemaining = MatchStartDelayRemaining,
                players = playerSnapshots,
                bullets = bulletSnapshots,
                ui = gameUI != null ? gameUI.BuildUiSnapshot() : new TankBattleUiStateSnapshot()
            };
        }

        public void ApplyStateSnapshot(TankBattleStateSnapshotPayload snapshot)
        {
            if (snapshot == null)
            {
                return;
            }

            IsMatchStarting = snapshot.isMatchStarting;
            MatchStartDelayRemaining = Mathf.Max(0f, snapshot.matchStartDelayRemaining);

            if (HasSimulationAuthority && snapshot.players != null)
            {
                for (int i = 0; i < snapshot.players.Length; i++)
                {
                    TankBattlePlayerStateSnapshot playerSnapshot = snapshot.players[i];
                    if (playerSnapshot == null)
                    {
                        continue;
                    }

                    if (playersByChair.TryGetValue(playerSnapshot.chairId, out TankBattlePlayer player) && player != null)
                    {
                        player.ApplyStateSnapshot(playerSnapshot);
                    }
                }
            }

            if (HasSimulationAuthority)
            {
                SyncSnapshotBullets(snapshot.bullets);
            }

            if (snapshot.ui != null && gameUI != null)
            {
                gameUI.ApplyOnlineSnapshot(snapshot.ui);
                IsGameOver = snapshot.ui.gameIsOver;
                winnerChairId = PlayerNumberToChair(snapshot.ui.winnerPlayerNumber);
            }

            if (IsGameOver)
            {
                DisablePlayerInput();
            }
        }

        public void OnNetworkMatchEnded(int winningChairId)
        {
            winnerChairId = winningChairId;
            IsGameOver = true;
            DisablePlayerInput();

            if (gameUI != null)
            {
                gameUI.ShowWinnerByChair(winningChairId);
            }
        }

        public TankBattlePlayer GetPlayerByChair(int chairId)
        {
            playersByChair.TryGetValue(chairId, out TankBattlePlayer player);
            return player;
        }

        public TankBattlePlayer FindPlayerByUserId(string userId)
        {
            if (string.IsNullOrEmpty(userId))
            {
                return null;
            }

            foreach (TankBattlePlayer player in spawnedPlayers)
            {
                if (player != null && string.Equals(player.UserId, userId, StringComparison.Ordinal))
                {
                    return player;
                }
            }

            return null;
        }

        public int ResolveWinningChairFromCurrentMatch()
        {
            if (winnerChairId >= 0)
            {
                return winnerChairId;
            }

            if (gameUI != null)
            {
                int uiChair = PlayerNumberToChair(gameUI.WinnerPlayerNumber);
                if (uiChair >= 0)
                {
                    return uiChair;
                }
            }

            foreach (KeyValuePair<int, TankBattlePlayer> entry in playersByChair)
            {
                if (entry.Value != null && entry.Value.IsAlive)
                {
                    return entry.Key;
                }
            }

            return -1;
        }

        public void OnGameOver(int winnerPlayerNumber)
        {
            if (IsGameOver)
            {
                return;
            }

            winnerChairId = PlayerNumberToChair(winnerPlayerNumber);
            IsGameOver = true;
            DisablePlayerInput();

            if (IsOnlineMatch && !HasSimulationAuthority)
            {
                return;
            }

            GameEnded?.Invoke(winnerChairId);
        }

        public void RestartGame()
        {
            if (IsOnlineMatch)
            {
                return;
            }

            StartGameLocal(lastOfflineTotalPlayers, lastOfflineHumanPlayers);
        }

        public void OnBackToLobby()
        {
            TankBattleLobbyManager lobbyManager = GetComponent<TankBattleLobbyManager>();
            if (lobbyManager != null)
            {
                lobbyManager.ReturnToMainMenu();
                return;
            }

            ShowLobbyState();
            EnsureLobbyReferences();
            if (lobbyCanvas != null)
            {
                lobbyCanvas.SetActive(true);
            }
        }

        private void StartMatch(
            TankBattleRosterEntry[] roster,
            string localUserId,
            int localChairId,
            bool simulationAuthority,
            bool isOnlineMatch,
            int mapSeed,
            float countdownSeconds)
        {
            this.localUserId = localUserId;
            this.localChairId = localChairId;
            currentMapSeed = mapSeed;
            IsOnlineMatch = isOnlineMatch;
            HasSimulationAuthority = simulationAuthority;
            IsGameOver = false;
            winnerChairId = -1;
            IsMatchStarting = countdownSeconds > 0f;
            MatchStartDelayRemaining = Mathf.Max(0f, countdownSeconds);
            LogNetworkDiagnostic(
                $"StartMatch localUser={this.localUserId}, localChair={this.localChairId}, online={IsOnlineMatch}, simAuthority={HasSimulationAuthority}, seed={currentMapSeed}, countdown={MatchStartDelayRemaining:0.00}");

            EnsureLobbyReferences();
            if (lobbyCanvas != null)
            {
                lobbyCanvas.SetActive(false);
            }

            ClearMatchObjects();
            GenerateMap(currentMapSeed);
            SpawnParticipants(roster);

            if (gameUI != null)
            {
                gameUI.Initialize(this, GetPlayerByChair(0), GetPlayerByChair(1));
                gameUI.SetGameplayUiVisible(true);
            }
        }

        private void GenerateMap(int seed)
        {
            if (mapGenerator == null)
            {
                return;
            }

            mapGenerator.randomSeedEveryRound = false;
            mapGenerator.seed = seed;
            mapGenerator.GenerateMap();
        }

        private void SpawnParticipants(TankBattleRosterEntry[] roster)
        {
            playersByChair.Clear();
            spawnedPlayers.Clear();
            localInputPlayer = null;

            if (roster == null)
            {
                return;
            }

            Array.Sort(roster, (a, b) => (a != null ? a.chairId : int.MaxValue).CompareTo(b != null ? b.chairId : int.MaxValue));
            int resolvedLocalChair = ResolveLocalChairFromRoster(roster);
            localChairId = resolvedLocalChair;
            LogNetworkDiagnostic($"SpawnParticipants resolvedLocalChair={resolvedLocalChair}, roster={BuildRosterSummary(roster)}");

            for (int i = 0; i < roster.Length; i++)
            {
                TankBattleRosterEntry entry = roster[i];
                if (entry == null)
                {
                    continue;
                }

                GameObject prefab = entry.chairId == 0 ? tank1Prefab : tank2Prefab;
                if (prefab == null)
                {
                    continue;
                }

                Vector3 spawnPosition = GetSpawnForChair(entry.chairId);
                GameObject tank = Instantiate(prefab, spawnPosition, Quaternion.identity);
                TankBattlePlayer player = tank.GetComponent<TankBattlePlayer>();
                if (player == null)
                {
                    player = tank.AddComponent<TankBattlePlayer>();
                }

                bool isLocal = resolvedLocalChair >= 0 && entry.chairId == resolvedLocalChair;
                TankBattlePlayerControlMode controlMode = ResolveControlMode(entry, isLocal);
                bool captureInputForNetworking = isLocal && IsOnlineMatch && !HasSimulationAuthority;
                LogNetworkDiagnostic(
                    $"Spawn chair={entry.chairId}, user={entry.userId}, isBot={entry.isBot}, isHost={entry.isHost}, isLocal={isLocal}, mode={controlMode}, capture={captureInputForNetworking}");

                bool playerSimulatesLocally = !IsOnlineMatch
                    || isLocal
                    || (HasSimulationAuthority && entry.isBot);

                player.Initialize(
                    bulletPrefab,
                    bulletSettings,
                    controlMode,
                    entry.userId,
                    entry.chairId,
                    entry.isBot,
                    playerSimulatesLocally,
                    captureInputForNetworking);

                if (controlMode == TankBattlePlayerControlMode.Bot)
                {
                    TankBattleBotController bot = tank.GetComponent<TankBattleBotController>();
                    if (bot == null)
                    {
                        bot = tank.AddComponent<TankBattleBotController>();
                    }

                    bot.Initialize(botSettings, player);
                }
                else
                {
                    TankBattleBotController bot = tank.GetComponent<TankBattleBotController>();
                    if (bot != null)
                    {
                        bot.enabled = false;
                    }
                }

                AssignJoystickColor(player, entry.chairId);

                playersByChair[entry.chairId] = player;
                spawnedPlayers.Add(player);

                if (isLocal)
                {
                    localInputPlayer = player;
                    LogNetworkDiagnostic($"Local input player assigned at spawn: chair={entry.chairId}, user={entry.userId}");
                }
            }

            if (localInputPlayer == null && localChairId >= 0 && playersByChair.TryGetValue(localChairId, out TankBattlePlayer fallbackPlayer))
            {
                localInputPlayer = fallbackPlayer;
                LogNetworkDiagnostic($"Local input player fallback by chair: chair={localChairId}, user={fallbackPlayer.UserId}, mode={fallbackPlayer.ControlMode}");
            }

            if (IsOnlineMatch && !HasSimulationAuthority && localInputPlayer == null)
            {
                Debug.LogWarning("TankBattleGameManager: Could not resolve local online participant. Client input forwarding will be disabled.");
                LogNetworkDiagnostic($"Local input unresolved. localUser={localUserId}, localChair={localChairId}, roster={BuildRosterSummary(roster)}");
            }
        }

        private int ResolveLocalChairFromRoster(TankBattleRosterEntry[] roster)
        {
            if (localChairId >= 0)
            {
                return localChairId;
            }

            if (roster == null || roster.Length == 0)
            {
                return -1;
            }

            if (!string.IsNullOrEmpty(localUserId))
            {
                for (int i = 0; i < roster.Length; i++)
                {
                    TankBattleRosterEntry entry = roster[i];
                    if (entry != null && string.Equals(entry.userId, localUserId, StringComparison.Ordinal))
                    {
                        return entry.chairId;
                    }
                }
            }

            if (IsOnlineMatch && !HasSimulationAuthority)
            {
                for (int i = 0; i < roster.Length; i++)
                {
                    TankBattleRosterEntry entry = roster[i];
                    if (entry != null && !entry.isBot && !entry.isHost)
                    {
                        LogNetworkDiagnostic($"ResolveLocalChairFromRoster chose non-host human chair={entry.chairId} for localUser={localUserId}.");
                        return entry.chairId;
                    }
                }

                for (int i = 0; i < roster.Length; i++)
                {
                    TankBattleRosterEntry entry = roster[i];
                    if (entry != null && !entry.isBot)
                    {
                        LogNetworkDiagnostic($"ResolveLocalChairFromRoster fallback human chair={entry.chairId} for localUser={localUserId}.");
                        return entry.chairId;
                    }
                }
            }

            LogNetworkDiagnostic($"ResolveLocalChairFromRoster failed for localUser={localUserId}. Returning -1.");
            return -1;
        }

        private Vector3 GetSpawnForChair(int chairId)
        {
            if (mapGenerator == null)
            {
                return Vector3.zero;
            }

            return chairId == 0
                ? mapGenerator.GetPlayer1Spawn()
                : mapGenerator.GetPlayer2Spawn();
        }

        private TankBattlePlayerControlMode ResolveControlMode(TankBattleRosterEntry entry, bool isLocal)
        {
            if (!IsOnlineMatch)
            {
                if (entry.isBot)
                {
                    return TankBattlePlayerControlMode.Bot;
                }

                return isLocal ? TankBattlePlayerControlMode.LocalHuman : TankBattlePlayerControlMode.Bot;
            }

            if (isLocal)
            {
                return TankBattlePlayerControlMode.LocalHuman;
            }

            if (entry != null && entry.isBot)
            {
                return HasSimulationAuthority
                    ? TankBattlePlayerControlMode.Bot
                    : TankBattlePlayerControlMode.SnapshotOnly;
            }

            return TankBattlePlayerControlMode.SnapshotOnly;
        }

        private bool ShouldLogNow(ref float nextLogAt)
        {
            float now = Time.unscaledTime;
            if (now < nextLogAt)
            {
                return false;
            }

            nextLogAt = now + Mathf.Max(0.1f, networkDiagnosticIntervalSeconds);
            return true;
        }

        private void LogNetworkDiagnostic(string message)
        {
            if (!enableNetworkDiagnostics)
            {
                return;
            }

            string side = HasSimulationAuthority ? "host/sim" : "client/view";
            Debug.Log($"TankBattleGameManager[{side}] {message}");
        }

        private static string BuildRosterSummary(TankBattleRosterEntry[] roster)
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

        private void AssignJoystickColor(TankBattlePlayer player, int chairId)
        {
            if (player == null)
            {
                return;
            }

            TankBattlePlayerInput input = player.GetComponent<TankBattlePlayerInput>();
            if (input == null)
            {
                return;
            }

            Color joystickColor = chairId == 0
                ? new Color(0.055f, 0.514f, 0.996f, 1f)
                : new Color(0.996f, 0.145f, 0.145f, 1f);
            input.SetJoystickColor(joystickColor);
        }

        private TankBattlePlayerStateSnapshot[] BuildPlayerSnapshots()
        {
            List<TankBattlePlayerStateSnapshot> snapshots = new List<TankBattlePlayerStateSnapshot>(playersByChair.Count);
            foreach (KeyValuePair<int, TankBattlePlayer> pair in playersByChair)
            {
                TankBattlePlayer player = pair.Value;
                if (player == null)
                {
                    continue;
                }

                snapshots.Add(new TankBattlePlayerStateSnapshot
                {
                    chairId = player.ChairId,
                    userId = player.UserId,
                    x = player.Position.x,
                    y = player.Position.y,
                    facingAngle = player.FacingAngle,
                    velocityX = player.EstimatedVelocity.x,
                    velocityY = player.EstimatedVelocity.y,
                    isAlive = player.IsAlive,
                    isBot = player.IsBotParticipant,
                    remainingShots = player.RemainingShots
                });
            }

            return snapshots.ToArray();
        }

        private TankBattleBulletStateSnapshot[] BuildBulletSnapshots()
        {
            TankBattleBullet[] bullets = FindObjectsByType<TankBattleBullet>(FindObjectsSortMode.None);
            List<TankBattleBulletStateSnapshot> snapshots = new List<TankBattleBulletStateSnapshot>(bullets.Length);

            for (int i = 0; i < bullets.Length; i++)
            {
                TankBattleBullet bullet = bullets[i];
                if (bullet == null || bullet.IsSnapshotProxy)
                {
                    continue;
                }

                snapshots.Add(new TankBattleBulletStateSnapshot
                {
                    bulletId = bullet.NetworkBulletId,
                    ownerChairId = bullet.OwnerChairId,
                    x = bullet.transform.position.x,
                    y = bullet.transform.position.y,
                    velocityX = bullet.CurrentVelocity.x,
                    velocityY = bullet.CurrentVelocity.y
                });
            }

            return snapshots.ToArray();
        }

        private void SyncSnapshotBullets(TankBattleBulletStateSnapshot[] bulletSnapshots)
        {
            HashSet<int> seen = new HashSet<int>();

            if (bulletSnapshots != null)
            {
                for (int i = 0; i < bulletSnapshots.Length; i++)
                {
                    TankBattleBulletStateSnapshot snapshot = bulletSnapshots[i];
                    if (snapshot == null || snapshot.bulletId <= 0)
                    {
                        continue;
                    }

                    seen.Add(snapshot.bulletId);

                    if (snapshotBullets.TryGetValue(snapshot.bulletId, out TankBattleBullet existing) && existing != null)
                    {
                        existing.ApplySnapshotState(
                            new Vector2(snapshot.x, snapshot.y),
                            new Vector2(snapshot.velocityX, snapshot.velocityY));
                        continue;
                    }

                    if (bulletPrefab == null)
                    {
                        continue;
                    }

                    GameObject bulletObj = Instantiate(bulletPrefab, new Vector3(snapshot.x, snapshot.y, 0f), Quaternion.identity);
                    TankBattleBullet bullet = bulletObj.GetComponent<TankBattleBullet>();
                    if (bullet == null)
                    {
                        Destroy(bulletObj);
                        continue;
                    }

                    bullet.InitializeSnapshotProxy(
                        bulletSettings,
                        snapshot.bulletId,
                        new Vector2(snapshot.x, snapshot.y),
                        new Vector2(snapshot.velocityX, snapshot.velocityY),
                        snapshot.ownerChairId);
                    snapshotBullets[snapshot.bulletId] = bullet;
                }
            }

            List<int> toRemove = new List<int>();
            foreach (KeyValuePair<int, TankBattleBullet> pair in snapshotBullets)
            {
                if (seen.Contains(pair.Key))
                {
                    continue;
                }

                if (pair.Value != null)
                {
                    Destroy(pair.Value.gameObject);
                }

                toRemove.Add(pair.Key);
            }

            for (int i = 0; i < toRemove.Count; i++)
            {
                snapshotBullets.Remove(toRemove[i]);
            }
        }

        private void ResolveBulletAfterHit(int shooterChairId, Vector2 victimPosition)
        {
            TankBattleBullet[] bullets = FindObjectsByType<TankBattleBullet>(FindObjectsSortMode.None);
            TankBattleBullet selected = null;
            const float maxResolveDistance = 1.75f;
            float bestDistanceSqr = maxResolveDistance * maxResolveDistance;

            for (int i = 0; i < bullets.Length; i++)
            {
                TankBattleBullet bullet = bullets[i];
                if (bullet == null || bullet.IsSnapshotProxy || bullet.OwnerChairId != shooterChairId)
                {
                    continue;
                }

                float distanceSqr = ((Vector2)bullet.transform.position - victimPosition).sqrMagnitude;
                if (distanceSqr < bestDistanceSqr)
                {
                    bestDistanceSqr = distanceSqr;
                    selected = bullet;
                }
            }

            if (selected != null)
            {
                Destroy(selected.gameObject);
            }
        }

        private void ClearMatchObjects()
        {
            for (int i = 0; i < spawnedPlayers.Count; i++)
            {
                if (spawnedPlayers[i] != null)
                {
                    Destroy(spawnedPlayers[i].gameObject);
                }
            }

            spawnedPlayers.Clear();
            playersByChair.Clear();
            localInputPlayer = null;

            foreach (KeyValuePair<int, TankBattleBullet> pair in snapshotBullets)
            {
                if (pair.Value != null)
                {
                    Destroy(pair.Value.gameObject);
                }
            }

            snapshotBullets.Clear();

            TankBattleBullet[] liveBullets = FindObjectsByType<TankBattleBullet>(FindObjectsSortMode.None);
            for (int i = 0; i < liveBullets.Length; i++)
            {
                if (liveBullets[i] != null)
                {
                    Destroy(liveBullets[i].gameObject);
                }
            }
        }

        private void DisablePlayerInput()
        {
            for (int i = 0; i < spawnedPlayers.Count; i++)
            {
                TankBattlePlayer player = spawnedPlayers[i];
                if (player != null)
                {
                    player.SetInputEnabled(false);
                }
            }
        }

        private static int PlayerNumberToChair(int winnerPlayerNumber)
        {
            switch (winnerPlayerNumber)
            {
                case 1:
                    return 0;
                case 2:
                    return 1;
                default:
                    return -1;
            }
        }

        private static TankBattleRosterEntry[] BuildOfflineRoster(int totalPlayers, int humanPlayers)
        {
            int capacity = Mathf.Max(2, totalPlayers);
            int humans = Mathf.Clamp(humanPlayers, 1, capacity);
            List<TankBattleRosterEntry> roster = new List<TankBattleRosterEntry>(capacity);

            for (int chair = 0; chair < capacity; chair++)
            {
                bool isHuman = chair < humans;
                roster.Add(new TankBattleRosterEntry
                {
                    chairId = chair,
                    userId = isHuman ? $"local-player-{chair}" : $"bot-offline-{chair}",
                    isBot = !isHuman,
                    isHost = chair == 0,
                    appearanceIndex = chair
                });
            }

            return roster.ToArray();
        }

        private void EnsureLobbyManagerIfNeeded()
        {
            if (lobbyCanvas == null)
            {
                return;
            }

            if (GetComponent<TankBattleLobbyManager>() == null)
            {
                gameObject.AddComponent<TankBattleLobbyManager>();
            }
        }

        private void EnsureLobbyReferences()
        {
            if (lobbyCanvas == null)
            {
                GameObject found = GameObject.Find("LobbyCanvas");
                if (found != null)
                {
                    lobbyCanvas = found;
                }
            }

            if (gameUI == null)
            {
                gameUI = FindAnyObjectByType<TankBattleUI>();
            }
        }

        private void EnsureGlobalConfig()
        {
            if (globalConfig == null)
            {
                globalConfig = GetComponent<TankBattleGlobalConfig>();
            }

            if (globalConfig == null)
            {
                globalConfig = FindAnyObjectByType<TankBattleGlobalConfig>();
            }

            if (globalConfig == null && autoCreateGlobalConfigIfMissing)
            {
                globalConfig = gameObject.AddComponent<TankBattleGlobalConfig>();
            }
        }
    }
}
