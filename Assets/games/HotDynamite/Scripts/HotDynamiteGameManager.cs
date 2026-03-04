using System.Collections.Generic;
using UnityEngine;

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

        private readonly List<HotDynamitePlayer> alivePlayers = new List<HotDynamitePlayer>();

        private HotDynamitePlayer bombHolder;
        private float bombTimer;
        private float bombFuseSeconds = 10f;
        private float bombPassLockUntil;
        private bool gameOver;

        private float matchStartDelayTimer;
        private bool isMatchStarting;

        public HotDynamitePlayer BombHolder => bombHolder;
        public bool HardModeBots => hardModeBots;
        public float BombTimeRemaining => Mathf.Max(0f, bombTimer);
        public bool IsMatchStarting => isMatchStarting;
        public float MatchStartDelayRemaining => Mathf.Max(0f, matchStartDelayTimer);

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



        private void Start()
        {
            // Game is started via StartGameLocal() from the Lobby Manager.
        }

        public void StartGameLocal()
        {
            if (!TryInitializeGame())
            {
                enabled = false;
                return;
            }

            SpawnPlayers();
            AssignBombToRandomAlivePlayer();
            
            // Set up match start delay
            isMatchStarting = true;
            matchStartDelayTimer = 3f;
        }

        private void Update()
        {
            if (isMatchStarting)
            {
                matchStartDelayTimer -= Time.deltaTime;
                if (matchStartDelayTimer <= 0f)
                {
                    isMatchStarting = false;
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


    }
}