using System.Collections.Generic;
using UnityEngine;
using HotDynamite.Network;

namespace HotDynamite
{
    public class HotDynamiteGameManager : MonoBehaviour
    {
        private const float MinBombFuseSeconds = 0.1f;

        [SerializeField] private HotDynamiteGameSettings settings;
        [SerializeField] private HotDynamitePlayer playerPrefab;
        [SerializeField] private Transform playerParent;
        [SerializeField] private Transform[] spawnPoints;
        [Header("VFX")]
        [SerializeField] private GameObject explosionPrefab;
        [SerializeField, Min(0f)] private float explosionLifetimeSeconds = 2f;
        [Header("Bots")]
        [SerializeField] private bool hardModeBots = false;
        [Header("Network")]
        [SerializeField] private bool runAsNetworkClient = false;
        [SerializeField] private HotDynamiteNetworkAdapter networkAdapter;

        private readonly List<HotDynamitePlayer> alivePlayers = new List<HotDynamitePlayer>();
        private readonly Dictionary<string, HotDynamitePlayer> networkPlayersById = new Dictionary<string, HotDynamitePlayer>();

        private HotDynamitePlayer bombHolder;
        private float bombTimer;
        private float bombFuseSeconds = 10f;
        private float bombPassLockUntil;
        private bool gameOver;
        private bool networkMode;
        private string localNetworkPlayerId;
        private string activeRoomId;

        public HotDynamitePlayer BombHolder => bombHolder;
        public bool HardModeBots => hardModeBots;
        public bool IsNetworkMode => networkMode;
        public string LocalNetworkPlayerId => localNetworkPlayerId;
        public float BombTimeRemaining => Mathf.Max(0f, bombTimer);
        public float BombUrgency01
        {
            get
            {
                if (settings == null || bombHolder == null)
                {
                    return 0f;
                }

                float remaining01 = bombTimer / Mathf.Max(MinBombFuseSeconds, bombFuseSeconds);
                return Mathf.Clamp01(1f - remaining01);
            }
        }

        private void Awake()
        {
            networkMode = runAsNetworkClient;
        }

        private void OnEnable()
        {
            if (networkAdapter == null)
            {
                return;
            }

            networkAdapter.RoomStateReceived += HandleNetworkRoomState;
            networkAdapter.MatchStartedReceived += HandleNetworkMatchStarted;
            networkAdapter.SnapshotReceived += HandleNetworkSnapshot;
            networkAdapter.MatchEndedReceived += HandleNetworkMatchEnded;
        }

        private void OnDisable()
        {
            if (networkAdapter == null)
            {
                return;
            }

            networkAdapter.RoomStateReceived -= HandleNetworkRoomState;
            networkAdapter.MatchStartedReceived -= HandleNetworkMatchStarted;
            networkAdapter.SnapshotReceived -= HandleNetworkSnapshot;
            networkAdapter.MatchEndedReceived -= HandleNetworkMatchEnded;
        }

        private void Start()
        {
            if (networkMode)
            {
                if (!ValidateSettingsAssigned() || !ValidatePlayerPrefabAssigned())
                {
                    enabled = false;
                    return;
                }

                bombFuseSeconds = Mathf.Max(MinBombFuseSeconds, settings.BombTimerSeconds);
                return;
            }

            if (!TryInitializeGame())
            {
                enabled = false;
                return;
            }

            SpawnPlayers();
            AssignBombToRandomAlivePlayer();
        }

        private void Update()
        {
            if (networkMode)
            {
                return;
            }

            if (!IsRoundActive())
            {
                return;
            }

            TickBombTimer();
        }

        private bool TryInitializeGame()
        {
            if (!ValidateSettingsAssigned() || !ValidatePlayerPrefabAssigned())
            {
                return false;
            }

            bombFuseSeconds = Mathf.Max(MinBombFuseSeconds, settings.BombTimerSeconds);
            return ValidateSpawnPoints(settings.ClampedTotalPlayers);
        }

        private bool ValidateSettingsAssigned()
        {
            if (settings != null)
            {
                return true;
            }

            Debug.LogError("HotDynamiteGameManager: Settings not assigned.");
            return false;
        }

        private bool ValidatePlayerPrefabAssigned()
        {
            if (playerPrefab != null)
            {
                return true;
            }

            Debug.LogError("HotDynamiteGameManager: Player prefab not assigned.");
            return false;
        }

        private bool ValidateSpawnPoints(int requiredCount)
        {
            if (spawnPoints == null || spawnPoints.Length < requiredCount)
            {
                Debug.LogError($"HotDynamiteGameManager: Need at least {requiredCount} spawn points assigned.");
                return false;
            }

            for (int i = 0; i < requiredCount; i++)
            {
                if (spawnPoints[i] == null)
                {
                    Debug.LogError($"HotDynamiteGameManager: Spawn point at index {i} is not assigned.");
                    return false;
                }
            }

            return true;
        }

        private bool IsRoundActive()
        {
            return !gameOver && bombHolder != null;
        }

        private void TickBombTimer()
        {
            bombTimer -= Time.deltaTime;
            if (bombTimer <= 0f)
            {
                Explode();
            }
        }

        private void SpawnPlayers()
        {
            int total = settings.ClampedTotalPlayers;
            int humans = settings.ClampedHumanPlayers;

            for (int i = 0; i < total; i++)
            {
                Vector2 position = spawnPoints[i].position;
                Transform parent = playerParent != null ? playerParent : transform;
                HotDynamitePlayer player = Instantiate(playerPrefab, position, Quaternion.identity, parent);
                bool isHuman = i < humans;
                player.Initialize(this, settings, isHuman);
                player.SetAppearanceIndex(i);
                alivePlayers.Add(player);
            }
        }

        private void AssignBombToRandomAlivePlayer()
        {
            if (alivePlayers.Count == 0)
            {
                return;
            }

            HotDynamitePlayer randomHolder = alivePlayers[Random.Range(0, alivePlayers.Count)];
            SetBombHolder(randomHolder);
            bombTimer = bombFuseSeconds;
            bombPassLockUntil = Time.time + settings.PassCooldownSeconds;
        }

        private void SetBombHolder(HotDynamitePlayer nextHolder)
        {
            bombHolder = nextHolder;
            for (int i = 0; i < alivePlayers.Count; i++)
            {
                HotDynamitePlayer player = alivePlayers[i];
                player.SetHasBomb(player == bombHolder);
            }
        }

        public void RequestBombPass(HotDynamitePlayer from, HotDynamitePlayer to)
        {
            if (networkMode)
            {
                return;
            }

            if (!CanTransferBomb(from, to))
            {
                return;
            }

            TransferBomb(from, to);
        }

        private bool CanTransferBomb(HotDynamitePlayer from, HotDynamitePlayer to)
        {
            if (gameOver || from == null || to == null)
            {
                return false;
            }

            if (from != bombHolder || !to.IsAlive)
            {
                return false;
            }

            return Time.time >= bombPassLockUntil;
        }

        private void TransferBomb(HotDynamitePlayer from, HotDynamitePlayer to)
        {
            SetBombHolder(to);
            from.ApplyTemporarySpeedMultiplier(settings.BombPasserSpeedMultiplier, settings.BombPassSpeedEffectSeconds);
            to.ApplyTemporarySpeedMultiplier(settings.BombReceiverSpeedMultiplier, settings.BombPassSpeedEffectSeconds);
            bombPassLockUntil = Time.time + settings.PassCooldownSeconds;

            Debug.Log(
                $"HotDynamite: Bomb transferred from {from.name} (ID {from.GetInstanceID()}) to {to.name} (ID {to.GetInstanceID()}).");
        }

        private void Explode()
        {
            if (bombHolder == null)
            {
                return;
            }

            SpawnExplosionAtBombHolder();
            EliminatePlayer(bombHolder);

            if (alivePlayers.Count <= 1)
            {
                gameOver = true;
                return;
            }

            AssignBombToRandomAlivePlayer();
        }

        private void SpawnExplosionAtBombHolder()
        {
            if (explosionPrefab == null || bombHolder == null)
            {
                return;
            }

            Vector3 explosionPosition = new Vector3(bombHolder.Position.x, bombHolder.Position.y, 0f);
            GameObject explosionInstance = Instantiate(explosionPrefab, explosionPosition, Quaternion.identity);
            if (explosionInstance == null)
            {
                return;
            }

            float oneShotDuration = ConfigureExplosionParticleSystems(explosionInstance.transform, bombHolder.GetAppearanceColor());
            float destroyDelay = explosionLifetimeSeconds > 0f ? explosionLifetimeSeconds : oneShotDuration;
            if (destroyDelay > 0f)
            {
                Destroy(explosionInstance, destroyDelay);
            }
        }

        private static float ConfigureExplosionParticleSystems(Transform explosionRoot, Color holderColor)
        {
            float oneShotDuration = 0f;
            Transform particleBodyRoot = FindParticleBodyRoot(explosionRoot);
            ParticleSystem[] particleSystems = explosionRoot.GetComponentsInChildren<ParticleSystem>(true);

            for (int i = 0; i < particleSystems.Length; i++)
            {
                ParticleSystem particleSystem = particleSystems[i];
                ParticleSystem.MainModule main = particleSystem.main;
                main.loop = false;

                bool isBodyParticle = particleBodyRoot != null
                    && (particleSystem.transform == particleBodyRoot || particleSystem.transform.IsChildOf(particleBodyRoot));
                if (isBodyParticle)
                {
                    float alpha = main.startColor.color.a;
                    main.startColor = new ParticleSystem.MinMaxGradient(
                        new Color(holderColor.r, holderColor.g, holderColor.b, alpha));
                }

                oneShotDuration = Mathf.Max(oneShotDuration, main.duration + main.startLifetime.constantMax);
                particleSystem.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                particleSystem.Play(true);
            }

            return oneShotDuration;
        }

        private void EliminatePlayer(HotDynamitePlayer player)
        {
            player.Eliminate();
            alivePlayers.Remove(player);
            if (player == bombHolder)
            {
                bombHolder = null;
            }
        }

        public HotDynamitePlayer GetClosestAlivePlayer(HotDynamitePlayer from)
        {
            HotDynamitePlayer closest = null;
            float bestDistance = float.MaxValue;

            for (int i = 0; i < alivePlayers.Count; i++)
            {
                HotDynamitePlayer candidate = alivePlayers[i];
                if (candidate == from)
                {
                    continue;
                }

                float distance = Vector2.SqrMagnitude(candidate.Position - from.Position);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    closest = candidate;
                }
            }

            return closest;
        }

        public HotDynamitePlayer GetRandomAlivePlayer(HotDynamitePlayer exclude = null)
        {
            HotDynamitePlayer picked = null;
            int seen = 0;

            for (int i = 0; i < alivePlayers.Count; i++)
            {
                HotDynamitePlayer candidate = alivePlayers[i];
                if (candidate == exclude)
                {
                    continue;
                }

                seen++;
                if (Random.Range(0, seen) == 0)
                {
                    picked = candidate;
                }
            }

            return picked;
        }

        public bool TryGetAliveCentroid(HotDynamitePlayer exclude, bool includeBombHolder, out Vector2 centroid)
        {
            centroid = Vector2.zero;
            int count = 0;
            for (int i = 0; i < alivePlayers.Count; i++)
            {
                HotDynamitePlayer candidate = alivePlayers[i];
                if (candidate == null || candidate == exclude)
                {
                    continue;
                }

                if (!includeBombHolder && candidate == bombHolder)
                {
                    continue;
                }

                centroid += candidate.Position;
                count++;
            }

            if (count == 0)
            {
                return false;
            }

            centroid /= count;
            return true;
        }

        private static Transform FindParticleBodyRoot(Transform explosionRoot)
        {
            if (explosionRoot == null)
            {
                return null;
            }

            Transform[] allTransforms = explosionRoot.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < allTransforms.Length; i++)
            {
                Transform current = allTransforms[i];
                if (current != null && current.name == "Particlebody")
                {
                    return current;
                }
            }

            return null;
        }

        public void EnableNetworkMode(string localPlayerId)
        {
            if (!networkMode)
            {
                ClearAllSpawnedPlayersForNetworkMode();
            }

            networkMode = true;
            localNetworkPlayerId = localPlayerId;
            gameOver = false;
            bombHolder = null;
            bombTimer = 0f;
            bombPassLockUntil = -1f;
            bombFuseSeconds = settings != null
                ? Mathf.Max(MinBombFuseSeconds, settings.BombTimerSeconds)
                : bombFuseSeconds;
        }

        public void ApplyNetworkSnapshot(StateSnapshotPayload snapshot)
        {
            if (snapshot == null)
            {
                return;
            }

            if (!networkMode)
            {
                EnableNetworkMode(localNetworkPlayerId);
            }

            if (!string.IsNullOrEmpty(snapshot.roomId))
            {
                activeRoomId = snapshot.roomId;
            }

            SyncNetworkPlayers(snapshot.players);
            RefreshAlivePlayersFromNetwork();

            bombTimer = snapshot.bomb != null ? Mathf.Max(0f, snapshot.bomb.timeLeft) : 0f;
            bombHolder = null;
            if (snapshot.bomb != null && !string.IsNullOrEmpty(snapshot.bomb.holderId))
            {
                networkPlayersById.TryGetValue(snapshot.bomb.holderId, out bombHolder);
            }

            bool roundFinished = IsRoundFinishedPhase(snapshot.phase) || !string.IsNullOrEmpty(snapshot.winnerId);
            gameOver = roundFinished;
        }

        public HotDynamitePlayerInput GetLocalNetworkInput()
        {
            if (!networkMode || string.IsNullOrEmpty(localNetworkPlayerId))
            {
                return null;
            }

            if (!networkPlayersById.TryGetValue(localNetworkPlayerId, out HotDynamitePlayer localPlayer) || localPlayer == null)
            {
                return null;
            }

            return localPlayer.HumanInput;
        }

        private void HandleNetworkRoomState(RoomStatePayload payload)
        {
            if (payload == null)
            {
                return;
            }

            localNetworkPlayerId = payload.youId;
            activeRoomId = payload.roomId;
        }

        private void HandleNetworkMatchStarted(MatchStartedPayload payload)
        {
            EnableNetworkMode(localNetworkPlayerId);
        }

        private void HandleNetworkSnapshot(StateSnapshotPayload payload)
        {
            ApplyNetworkSnapshot(payload);
        }

        private void HandleNetworkMatchEnded(MatchEndedPayload payload)
        {
            gameOver = true;
        }

        private void SyncNetworkPlayers(List<PlayerState> networkStates)
        {
            if (networkStates == null)
            {
                RemoveAllNetworkPlayers();
                return;
            }

            HashSet<string> seenIds = new HashSet<string>();
            for (int i = 0; i < networkStates.Count; i++)
            {
                PlayerState state = networkStates[i];
                if (state == null || string.IsNullOrEmpty(state.id))
                {
                    continue;
                }

                seenIds.Add(state.id);
                HotDynamitePlayer player = GetOrCreateNetworkPlayer(state, i);
                if (player == null)
                {
                    continue;
                }

                player.SetNetworkControlled(true, state.id == localNetworkPlayerId);
                player.ApplyNetworkState(
                    new Vector2(state.x, state.y),
                    new Vector2(state.vx, state.vy),
                    state.alive,
                    state.hasBomb,
                    state.appearance);
            }

            RemoveMissingNetworkPlayers(seenIds);
        }

        private HotDynamitePlayer GetOrCreateNetworkPlayer(PlayerState state, int fallbackAppearanceIndex)
        {
            if (state == null || string.IsNullOrEmpty(state.id))
            {
                return null;
            }

            if (networkPlayersById.TryGetValue(state.id, out HotDynamitePlayer existingPlayer) && existingPlayer != null)
            {
                return existingPlayer;
            }

            if (playerPrefab == null)
            {
                Debug.LogError("HotDynamiteGameManager: Cannot create network player because player prefab is missing.");
                return null;
            }

            Transform parent = playerParent != null ? playerParent : transform;
            Vector2 startPosition = new Vector2(state.x, state.y);
            HotDynamitePlayer newPlayer = Instantiate(playerPrefab, startPosition, Quaternion.identity, parent);
            newPlayer.name = $"NetPlayer_{state.id}";
            newPlayer.Initialize(this, settings, isHuman: false);
            int appearance = state.appearance >= 0 ? state.appearance : fallbackAppearanceIndex;
            newPlayer.SetAppearanceIndex(Mathf.Max(0, appearance));
            newPlayer.SetNetworkControlled(true, state.id == localNetworkPlayerId);
            networkPlayersById[state.id] = newPlayer;
            return newPlayer;
        }

        private void RefreshAlivePlayersFromNetwork()
        {
            alivePlayers.Clear();
            foreach (KeyValuePair<string, HotDynamitePlayer> pair in networkPlayersById)
            {
                HotDynamitePlayer player = pair.Value;
                if (player != null && player.IsAlive)
                {
                    alivePlayers.Add(player);
                }
            }
        }

        private void RemoveMissingNetworkPlayers(HashSet<string> seenIds)
        {
            if (networkPlayersById.Count == 0)
            {
                return;
            }

            List<string> missingIds = null;
            foreach (KeyValuePair<string, HotDynamitePlayer> pair in networkPlayersById)
            {
                if (!seenIds.Contains(pair.Key))
                {
                    if (missingIds == null)
                    {
                        missingIds = new List<string>();
                    }

                    missingIds.Add(pair.Key);
                }
            }

            if (missingIds == null)
            {
                return;
            }

            for (int i = 0; i < missingIds.Count; i++)
            {
                string id = missingIds[i];
                if (!networkPlayersById.TryGetValue(id, out HotDynamitePlayer player))
                {
                    continue;
                }

                if (bombHolder == player)
                {
                    bombHolder = null;
                }

                if (player != null)
                {
                    Destroy(player.gameObject);
                }

                networkPlayersById.Remove(id);
            }
        }

        private void RemoveAllNetworkPlayers()
        {
            foreach (KeyValuePair<string, HotDynamitePlayer> pair in networkPlayersById)
            {
                HotDynamitePlayer player = pair.Value;
                if (player != null)
                {
                    Destroy(player.gameObject);
                }
            }

            networkPlayersById.Clear();
            alivePlayers.Clear();
            bombHolder = null;
        }

        private static bool IsRoundFinishedPhase(string phase)
        {
            return !string.IsNullOrEmpty(phase) &&
                (phase == "ended" || phase == "match_ended" || phase == "finished");
        }

        private void ClearAllSpawnedPlayersForNetworkMode()
        {
            Transform root = playerParent != null ? playerParent : transform;
            HotDynamitePlayer[] players = root.GetComponentsInChildren<HotDynamitePlayer>(true);
            for (int i = 0; i < players.Length; i++)
            {
                HotDynamitePlayer player = players[i];
                if (player != null)
                {
                    Destroy(player.gameObject);
                }
            }

            alivePlayers.Clear();
            networkPlayersById.Clear();
            bombHolder = null;
        }
    }
}
