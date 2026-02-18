using System.Collections.Generic;
using UnityEngine;

namespace HotDynamite
{
    public class HotDynamiteGameManager : MonoBehaviour
    {
        private const float BombFuseSeconds = 10f;

        [SerializeField] private HotDynamiteGameSettings settings;
        [SerializeField] private HotDynamitePlayer playerPrefab;
        [SerializeField] private Transform playerParent;
        [SerializeField] private Transform[] spawnPoints;

        private readonly List<HotDynamitePlayer> alivePlayers = new List<HotDynamitePlayer>();

        private HotDynamitePlayer bombHolder;
        private float bombTimer;
        private float bombPassLockUntil;
        private bool gameOver;

        public HotDynamitePlayer BombHolder => bombHolder;
        public float BombTimeRemaining => Mathf.Max(0f, bombTimer);
        public float BombUrgency01
        {
            get
            {
                if (settings == null || bombHolder == null)
                {
                    return 0f;
                }

                float remaining01 = bombTimer / BombFuseSeconds;
                return Mathf.Clamp01(1f - remaining01);
            }
        }

        private void Start()
        {
            if (settings == null)
            {
                Debug.LogError("HotDynamiteGameManager: Settings not assigned.");
                enabled = false;
                return;
            }

            if (playerPrefab == null)
            {
                Debug.LogError("HotDynamiteGameManager: Player prefab not assigned.");
                enabled = false;
                return;
            }

            int total = settings.ClampedTotalPlayers;
            if (spawnPoints == null || spawnPoints.Length < total)
            {
                Debug.LogError($"HotDynamiteGameManager: Need at least {total} spawn points assigned.");
                enabled = false;
                return;
            }

            for (int i = 0; i < total; i++)
            {
                if (spawnPoints[i] == null)
                {
                    Debug.LogError($"HotDynamiteGameManager: Spawn point at index {i} is not assigned.");
                    enabled = false;
                    return;
                }
            }

            SpawnPlayers();
            AssignBombToRandom();
        }

        private void Update()
        {
            if (gameOver || bombHolder == null)
            {
                return;
            }

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
                alivePlayers.Add(player);
            }
        }

        private void AssignBombToRandom()
        {
            if (alivePlayers.Count == 0)
            {
                return;
            }

            bombHolder = alivePlayers[Random.Range(0, alivePlayers.Count)];
            for (int i = 0; i < alivePlayers.Count; i++)
            {
                alivePlayers[i].SetHasBomb(alivePlayers[i] == bombHolder);
            }

            bombTimer = BombFuseSeconds;
            bombPassLockUntil = Time.time + settings.passCooldownSeconds;
        }

        public void RequestBombPass(HotDynamitePlayer from, HotDynamitePlayer to)
        {
            if (gameOver || from == null || to == null)
            {
                return;
            }

            if (from != bombHolder || !to.IsAlive)
            {
                return;
            }

            if (Time.time < bombPassLockUntil)
            {
                return;
            }

            bombHolder = to;
            from.SetHasBomb(false);
            to.SetHasBomb(true);
            from.ApplyTemporarySpeedMultiplier(settings.bombPasserSpeedMultiplier, settings.bombPassSpeedEffectSeconds);
            to.ApplyTemporarySpeedMultiplier(settings.bombReceiverSpeedMultiplier, settings.bombPassSpeedEffectSeconds);
            bombPassLockUntil = Time.time + settings.passCooldownSeconds;
            Debug.Log($"HotDynamite: Bomb transferred from {from.name} (ID {from.GetInstanceID()}) to {to.name} (ID {to.GetInstanceID()}).");
        }

        private void Explode()
        {
            if (bombHolder == null)
            {
                return;
            }

            Eliminate(bombHolder);

            if (alivePlayers.Count <= 1)
            {
                gameOver = true;
                return;
            }

            AssignBombToRandom();
        }

        private void Eliminate(HotDynamitePlayer player)
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
    }
}
