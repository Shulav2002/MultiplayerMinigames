using System;
using System.Collections.Generic;
using UnityEngine;
using HotDynamite.Networking;

namespace HotDynamite
{
    public class HotDynamiteGameManager : MonoBehaviour
    {
        private const float MinBombFuseSeconds = 0.1f;
        private const float CollisionSfxCooldownSeconds = 0.05f;

        [SerializeField] private HotDynamiteGameSettings settings;
        [SerializeField] private HotDynamitePlayer playerPrefab;
        [SerializeField] private Transform playerParent;
        [SerializeField] private Transform[] spawnPoints;

        [Header("VFX")]
        [SerializeField] private GameObject explosionPrefab;

        [SerializeField, Min(0f)] private float explosionLifetimeSeconds = 2f;

        [Header("Bots")]
        [SerializeField] private bool hardModeBots = false;

        [Header("Audio")]
        [SerializeField] private AudioClip backgroundMusic;

        [SerializeField] private AudioClip startWhistleClip;
        [SerializeField] private AudioClip playerCollisionClip;
        [SerializeField, Range(0f, 1f)] private float backgroundMusicVolume = 0.45f;
        [SerializeField, Range(0f, 1f)] private float startWhistleVolume = 0.9f;
        [SerializeField, Range(0f, 1f)] private float playerCollisionVolume = 0.7f;

        private readonly List<HotDynamitePlayer> alivePlayers = new List<HotDynamitePlayer>();
        private readonly List<HotDynamitePlayer> allPlayers = new List<HotDynamitePlayer>();
        private readonly Dictionary<int, HotDynamitePlayer> playersByChair = new Dictionary<int, HotDynamitePlayer>();

        private HotDynamitePlayer bombHolder;
        private float bombTimer;
        private float bombFuseSeconds = 10f;
        private float bombPassLockUntil;
        private bool gameOver;

        private float matchStartDelayTimer;
        private bool isMatchStarting;
        private AudioSource musicSource;
        private AudioSource sfxSource;
        private float lastCollisionSfxTime = float.NegativeInfinity;
        private HotDynamiteGameSettings baseSettings;
        private bool localSimulationEnabled = true;

        public HotDynamitePlayer BombHolder => bombHolder;
        public bool HardModeBots => hardModeBots;
        public float BombTimeRemaining => Mathf.Max(0f, bombTimer);
        public bool IsMatchStarting => isMatchStarting;
        public float MatchStartDelayRemaining => Mathf.Max(0f, matchStartDelayTimer);
        public bool LocalSimulationEnabled => localSimulationEnabled;
        public IReadOnlyList<HotDynamitePlayer> Players => allPlayers;
        public bool IsGameOver => gameOver;
        public event Action<Vector2, Color> ExplosionOccurred;

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
            baseSettings = settings;
            EnsureAudioSources();
        }

        private void Start()
        {
            // Game is started via StartGameLocal() from the Lobby Manager.
        }

        public void StartGameLocal()
        {
            StartGameLocal(
                baseSettings != null ? baseSettings.ClampedTotalPlayers : 4,
                baseSettings != null ? baseSettings.ClampedHumanPlayers : 1);
        }

        public void StartGameLocal(int totalPlayers, int humanPlayers)
        {
            totalPlayers = Mathf.Max(2, totalPlayers);
            humanPlayers = Mathf.Clamp(humanPlayers, 1, totalPlayers);

            PrepareRuntimeSettings(totalPlayers, humanPlayers);
            localSimulationEnabled = true;
            if (!TryInitializeGame())
            {
                enabled = false;
                return;
            }

            ClearSpawnedPlayers();
            SpawnPlayers();
            AssignBombToRandomAlivePlayer();
            StartBackgroundMusic();

            // Set up match start delay
            isMatchStarting = true;
            matchStartDelayTimer = 3f;
        }

        public void StartOnlineGame(HotDynamiteRosterEntry[] roster, string localUserId, bool simulationAuthority, float startDelaySeconds = 3f)
        {
            if (roster == null || roster.Length == 0)
            {
                Debug.LogError("HotDynamiteGameManager: Cannot start online game without a roster.");
                return;
            }

            PrepareRuntimeSettings(roster.Length, CountHumanParticipants(roster));
            localSimulationEnabled = simulationAuthority;

            if (!TryInitializeGame())
            {
                enabled = false;
                return;
            }

            ClearSpawnedPlayers();
            SpawnPlayers(roster, localUserId, simulationAuthority);
            if (simulationAuthority)
            {
                AssignBombToRandomAlivePlayer();
            }
            else
            {
                bombHolder = null;
                bombTimer = bombFuseSeconds;
            }

            gameOver = false;
            StartBackgroundMusic();
            isMatchStarting = startDelaySeconds > 0f;
            matchStartDelayTimer = Mathf.Max(0f, startDelaySeconds);
        }

        private void Update()
        {
            if (!localSimulationEnabled)
            {
                return;
            }

            if (isMatchStarting)
            {
                matchStartDelayTimer -= Time.deltaTime;
                if (matchStartDelayTimer <= 0f)
                {
                    isMatchStarting = false;
                    PlayStartWhistle();
                }
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
                allPlayers.Add(player);
            }
        }

        private void SpawnPlayers(HotDynamiteRosterEntry[] roster, string localUserId, bool simulationAuthority)
        {
            for (int i = 0; i < roster.Length; i++)
            {
                HotDynamiteRosterEntry entry = roster[i];
                if (entry == null)
                {
                    continue;
                }

                Vector2 position = spawnPoints[Mathf.Clamp(entry.chairId, 0, spawnPoints.Length - 1)].position;
                Transform parent = playerParent != null ? playerParent : transform;
                HotDynamitePlayer player = Instantiate(playerPrefab, position, Quaternion.identity, parent);
                HotDynamitePlayerControlMode controlMode = ResolveControlMode(entry, localUserId, simulationAuthority);
                player.Initialize(this, settings, controlMode, entry.userId, entry.chairId, entry.isBot);
                player.SetAppearanceIndex(entry.appearanceIndex);
                alivePlayers.Add(player);
                allPlayers.Add(player);
                playersByChair[entry.chairId] = player;
            }
        }

        private void AssignBombToRandomAlivePlayer()
        {
            if (alivePlayers.Count == 0)
            {
                return;
            }

            HotDynamitePlayer randomHolder = alivePlayers[UnityEngine.Random.Range(0, alivePlayers.Count)];
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
            if (!localSimulationEnabled)
            {
                return;
            }

            if (isMatchStarting)
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

            Vector2 explosionPosition = bombHolder.Position;
            Color explosionColor = bombHolder.GetAppearanceColor();
            ExplosionOccurred?.Invoke(explosionPosition, explosionColor);
            SpawnExplosion(explosionPosition, explosionColor);
            EliminatePlayer(bombHolder);

            if (alivePlayers.Count <= 1)
            {
                gameOver = true;
                return;
            }

            AssignBombToRandomAlivePlayer();
        }

        public void PlayExplosionEffect(Vector2 explosionPosition, Color explosionColor)
        {
            SpawnExplosion(explosionPosition, explosionColor);
        }

        private void SpawnExplosion(Vector2 explosionPosition, Color explosionColor)
        {
            if (explosionPrefab == null)
            {
                return;
            }

            GameObject explosionInstance = Instantiate(
                explosionPrefab,
                new Vector3(explosionPosition.x, explosionPosition.y, 0f),
                Quaternion.identity);
            if (explosionInstance == null)
            {
                return;
            }

            float oneShotDuration = ConfigureExplosionParticleSystems(explosionInstance.transform, explosionColor);
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
                if (UnityEngine.Random.Range(0, seen) == 0)
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

        public HotDynamitePlayer GetPlayerByChair(int chairId)
        {
            playersByChair.TryGetValue(chairId, out HotDynamitePlayer player);
            return player;
        }

        public void SetNetworkMoveInput(int chairId, Vector2 moveInput)
        {
            if (!playersByChair.TryGetValue(chairId, out HotDynamitePlayer player) || player == null)
            {
                return;
            }

            player.SetNetworkMoveInput(moveInput);
        }

        public HotDynamiteStateSnapshotPayload BuildStateSnapshot(string hostUserId, int tick)
        {
            HotDynamitePlayerStateSnapshot[] snapshots = new HotDynamitePlayerStateSnapshot[allPlayers.Count];
            int bombHolderChairId = -1;

            for (int i = 0; i < allPlayers.Count; i++)
            {
                HotDynamitePlayer player = allPlayers[i];
                bool playerHasBomb = player != null && player.HasBomb;
                if (playerHasBomb)
                {
                    bombHolderChairId = player.ChairId;
                }

                snapshots[i] = new HotDynamitePlayerStateSnapshot
                {
                    chairId = player != null ? player.ChairId : -1,
                    x = player != null ? player.Position.x : 0f,
                    y = player != null ? player.Position.y : 0f,
                    facingAngle = player != null ? player.FacingAngle : 0f,
                    velocityX = player != null ? player.EstimatedVelocity.x : 0f,
                    velocityY = player != null ? player.EstimatedVelocity.y : 0f,
                    isAlive = player != null && player.IsAlive,
                    hasBomb = playerHasBomb,
                    isBot = player != null && player.IsBotParticipant,
                    appearanceIndex = player != null ? player.AppearanceIndex : 0
                };
            }

            return new HotDynamiteStateSnapshotPayload
            {
                roomId = string.Empty,
                hostUserId = hostUserId,
                tick = tick,
                bombTimeRemaining = BombTimeRemaining,
                bombHolderChairId = bombHolderChairId,
                isMatchStarting = isMatchStarting,
                matchStartDelayRemaining = MatchStartDelayRemaining,
                explosionEventId = 0,
                explosionX = 0f,
                explosionY = 0f,
                explosionColorR = 1f,
                explosionColorG = 1f,
                explosionColorB = 1f,
                explosionColorA = 1f,
                players = snapshots
            };
        }

        public void ApplyStateSnapshot(HotDynamiteStateSnapshotPayload snapshot)
        {
            if (snapshot == null)
            {
                return;
            }

            bool wasMatchStarting = isMatchStarting;

            bombTimer = Mathf.Max(0f, snapshot.bombTimeRemaining);
            isMatchStarting = snapshot.isMatchStarting;
            matchStartDelayTimer = Mathf.Max(0f, snapshot.matchStartDelayRemaining);
            bombHolder = null;

            if (snapshot.players != null)
            {
                for (int i = 0; i < snapshot.players.Length; i++)
                {
                    HotDynamitePlayerStateSnapshot playerSnapshot = snapshot.players[i];
                    HotDynamitePlayer player = GetPlayerByChair(playerSnapshot.chairId);
                    if (player == null)
                    {
                        continue;
                    }

                    player.ApplyStateSnapshot(playerSnapshot);
                    if (playerSnapshot.hasBomb)
                    {
                        bombHolder = player;
                    }
                }
            }

            if (wasMatchStarting && !isMatchStarting)
            {
                PlayStartWhistle();
            }
        }

        public void NotifyPlayerCollision()
        {
            if (playerCollisionClip == null || Time.unscaledTime < lastCollisionSfxTime + CollisionSfxCooldownSeconds)
            {
                return;
            }

            EnsureAudioSources();
            if (sfxSource == null)
            {
                return;
            }

            lastCollisionSfxTime = Time.unscaledTime;
            sfxSource.PlayOneShot(playerCollisionClip, playerCollisionVolume);
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

        private void EnsureAudioSources()
        {
            if (musicSource == null)
            {
                musicSource = CreateAudioSource(loop: true);
            }

            if (sfxSource == null)
            {
                sfxSource = CreateAudioSource(loop: false);
            }
        }

        private AudioSource CreateAudioSource(bool loop)
        {
            AudioSource source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = loop;
            source.spatialBlend = 0f;
            source.volume = 1f;
            return source;
        }

        private void StartBackgroundMusic()
        {
            if (backgroundMusic == null)
            {
                return;
            }

            EnsureAudioSources();
            if (musicSource == null || (musicSource.isPlaying && musicSource.clip == backgroundMusic))
            {
                return;
            }

            musicSource.clip = backgroundMusic;
            musicSource.volume = backgroundMusicVolume;
            musicSource.Play();
        }

        private void PlayStartWhistle()
        {
            if (startWhistleClip == null)
            {
                return;
            }

            EnsureAudioSources();
            if (sfxSource == null)
            {
                return;
            }

            sfxSource.PlayOneShot(startWhistleClip, startWhistleVolume);
        }

        private void PrepareRuntimeSettings(int totalPlayers, int humanPlayers)
        {
            if (baseSettings == null)
            {
                return;
            }

            settings = baseSettings.CreateRuntimeCopy(totalPlayers, humanPlayers);
            bombFuseSeconds = Mathf.Max(MinBombFuseSeconds, settings.BombTimerSeconds);
        }

        private void ClearSpawnedPlayers()
        {
            for (int i = 0; i < allPlayers.Count; i++)
            {
                HotDynamitePlayer player = allPlayers[i];
                if (player != null)
                {
                    Destroy(player.gameObject);
                }
            }

            allPlayers.Clear();
            alivePlayers.Clear();
            playersByChair.Clear();
            bombHolder = null;
            bombTimer = 0f;
            bombPassLockUntil = 0f;
            gameOver = false;
            isMatchStarting = false;
            matchStartDelayTimer = 0f;
        }

        private static int CountHumanParticipants(HotDynamiteRosterEntry[] roster)
        {
            int humans = 0;
            for (int i = 0; i < roster.Length; i++)
            {
                if (roster[i] != null && !roster[i].isBot)
                {
                    humans++;
                }
            }

            return Mathf.Max(1, humans);
        }

        private static HotDynamitePlayerControlMode ResolveControlMode(
            HotDynamiteRosterEntry entry,
            string localUserId,
            bool simulationAuthority)
        {
            if (simulationAuthority)
            {
                if (entry.isBot)
                {
                    return HotDynamitePlayerControlMode.Bot;
                }

                return entry.userId == localUserId
                    ? HotDynamitePlayerControlMode.LocalHuman
                    : HotDynamitePlayerControlMode.NetworkDriven;
            }

            return entry.userId == localUserId
                ? HotDynamitePlayerControlMode.LocalHuman
                : HotDynamitePlayerControlMode.SnapshotOnly;
        }
    }
}

