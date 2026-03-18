using System.Collections.Generic;
using UnityEngine;

namespace BubbleBattle
{
    public class BubbleBattleGameManager : MonoBehaviour
    {
        private const int CircleSegmentCount = 64;

        [Header("Configuration")]
        [SerializeField] private BubbleBattleGameSettings settings;
        [SerializeField] private BubbleBattlePlayer playerPrefab;
        [SerializeField] private PhysicsMaterial2D bounceMaterial;

        [Header("Scene References")]
        [SerializeField] private Transform arenaCenter;
        [SerializeField] private GameObject arenaRoot;
        [SerializeField] private Transform arenaVisual;
        [SerializeField] private SpriteRenderer arenaVisualRenderer;
        [SerializeField] private EdgeCollider2D arenaBoundaryCollider;
        [SerializeField] private Transform playerParent;
        [SerializeField] private Transform pickupParent;
        [SerializeField] private Transform projectileParent;
        [SerializeField] private Transform[] spawnPoints;
        [SerializeField] private Transform[] weaponSpawnPoints;

        [Header("Debug Colors")]
        [SerializeField] private Color[] playerColors =
        {
            new Color(0.33f, 0.78f, 0.98f, 0.95f),
            new Color(1f, 0.55f, 0.32f, 0.95f),
            new Color(0.45f, 0.95f, 0.66f, 0.95f),
            new Color(0.97f, 0.84f, 0.34f, 0.95f)
        };

        private readonly List<BubbleBattlePlayer> players = new List<BubbleBattlePlayer>();

        public BubbleBattleGameSettings Settings => settings;
        public Transform ArenaCenter => arenaCenter;
        public Vector2 ArenaCenterPosition => arenaCenter != null ? (Vector2)arenaCenter.position : Vector2.zero;
        public float BubbleRadiusWorld => settings != null ? settings.BubbleDiameter * 0.5f : 0.5f;
        public GameObject ArenaRoot => arenaRoot;
        public Transform PlayerParent => playerParent;
        public Transform PickupParent => pickupParent;
        public Transform ProjectileParent => projectileParent;
        public Transform[] SpawnPoints => spawnPoints;
        public Transform[] WeaponSpawnPoints => weaponSpawnPoints;
        public bool IsMatchRunning { get; private set; }

        public void StartLocalGame(int requestedPlayerCount, int requestedHumanPlayers)
        {
            if (!ValidateConfiguration(logErrors: true))
            {
                return;
            }

            int configuredPlayerCount = settings != null ? settings.PlayerCount : 2;
            int finalPlayerCount = Mathf.Clamp(requestedPlayerCount, 2, Mathf.Max(2, configuredPlayerCount));
            int finalHumanPlayers = Mathf.Clamp(requestedHumanPlayers, 1, finalPlayerCount);

            ClearPlayers();
            ConfigureArena();
            SpawnPlayers(finalPlayerCount, finalHumanPlayers);

            IsMatchRunning = true;
            if (arenaRoot != null)
            {
                arenaRoot.SetActive(true);
            }
        }

        public void StopMatch()
        {
            IsMatchRunning = false;
            ClearPlayers();
        }

        public BubbleBattlePlayer GetClosestOpponent(BubbleBattlePlayer source)
        {
            BubbleBattlePlayer best = null;
            float bestDistance = float.MaxValue;

            for (int i = 0; i < players.Count; i++)
            {
                BubbleBattlePlayer candidate = players[i];
                if (candidate == null || candidate == source)
                {
                    continue;
                }

                float distance = (candidate.Position - source.Position).sqrMagnitude;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = candidate;
                }
            }

            return best;
        }

        public bool IsArenaBoundary(Collider2D candidate)
        {
            return candidate != null && candidate == arenaBoundaryCollider;
        }

        public bool ValidateConfiguration(bool logErrors)
        {
            bool isValid = true;
            isValid &= ValidateReference(settings, nameof(settings), logErrors);
            isValid &= ValidateReference(playerPrefab, nameof(playerPrefab), logErrors);
            isValid &= ValidateReference(bounceMaterial, nameof(bounceMaterial), logErrors);
            isValid &= ValidateReference(arenaCenter, nameof(arenaCenter), logErrors);
            isValid &= ValidateReference(arenaRoot, nameof(arenaRoot), logErrors);
            isValid &= ValidateReference(arenaVisual, nameof(arenaVisual), logErrors);
            isValid &= ValidateReference(arenaVisualRenderer, nameof(arenaVisualRenderer), logErrors);
            isValid &= ValidateReference(arenaBoundaryCollider, nameof(arenaBoundaryCollider), logErrors);
            isValid &= ValidateReference(playerParent, nameof(playerParent), logErrors);
            isValid &= ValidateReference(pickupParent, nameof(pickupParent), logErrors);
            isValid &= ValidateReference(projectileParent, nameof(projectileParent), logErrors);
            isValid &= ValidateArray(weaponSpawnPoints, nameof(weaponSpawnPoints), 1, logErrors);
            return isValid;
        }

        private void ConfigureArena()
        {
            arenaRoot.transform.position = arenaCenter.position;
            bounceMaterial.friction = 0.05f;
            bounceMaterial.bounciness = settings.BounceRestitution;

            Vector2[] edgePoints = BuildCirclePoints(settings.ArenaRadius, closedLoop: true);
            arenaBoundaryCollider.points = edgePoints;
            arenaBoundaryCollider.edgeRadius = settings.BoundaryThickness * 0.5f;
            arenaBoundaryCollider.sharedMaterial = bounceMaterial;

            if (arenaVisual != null)
            {
                float diameter = settings.ArenaRadius * 2f;
                arenaVisual.localPosition = Vector3.zero;
                arenaVisual.localScale = new Vector3(diameter, diameter, 1f);
            }

            if (arenaVisualRenderer != null)
            {
                Color arenaColor = settings.BoundaryColor;
                arenaColor.a = 0.18f;
                arenaVisualRenderer.color = arenaColor;
            }
        }

        private Vector2[] BuildCirclePoints(float radius, bool closedLoop)
        {
            int pointCount = closedLoop ? CircleSegmentCount + 1 : CircleSegmentCount;
            Vector2[] points = new Vector2[pointCount];
            for (int i = 0; i < CircleSegmentCount; i++)
            {
                float angle = (Mathf.PI * 2f * i) / CircleSegmentCount;
                points[i] = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
            }

            if (closedLoop)
            {
                points[CircleSegmentCount] = points[0];
            }

            return points;
        }

        private void SpawnPlayers(int totalPlayers, int humanPlayers)
        {
            for (int i = 0; i < totalPlayers; i++)
            {
                Vector2 spawnPosition = GetCenterSpawnPosition(i, totalPlayers);
                BubbleBattlePlayer player = Instantiate(playerPrefab, spawnPosition, Quaternion.identity, playerParent);
                player.name = $"BubblePlayer_{i + 1}";

                BubbleBattleControlMode controlMode = i < humanPlayers
                    ? BubbleBattleControlMode.LocalHuman
                    : BubbleBattleControlMode.Bot;

                player.Initialize(
                    this,
                    settings,
                    controlMode,
                    i,
                    ResolvePlayerColor(i),
                    bounceMaterial);

                players.Add(player);
            }
        }

        private Vector2 GetCenterSpawnPosition(int playerIndex, int totalPlayers)
        {
            Vector2 center = ArenaCenterPosition;
            float spacing = Mathf.Max(settings.CenterSpawnSpacing, BubbleRadiusWorld * 1.5f);

            if (totalPlayers <= 1)
            {
                return center;
            }

            float start = -0.5f * (totalPlayers - 1) * spacing;
            return center + new Vector2(start + (playerIndex * spacing), 0f);
        }

        private Color ResolvePlayerColor(int index)
        {
            if (playerColors == null || playerColors.Length == 0)
            {
                return Color.white;
            }

            return playerColors[Mathf.Abs(index) % playerColors.Length];
        }

        private void ClearPlayers()
        {
            for (int i = 0; i < players.Count; i++)
            {
                BubbleBattlePlayer player = players[i];
                if (player != null)
                {
                    Destroy(player.gameObject);
                }
            }

            players.Clear();
        }
        private bool ValidateReference(Object reference, string fieldName, bool logErrors)
        {
            if (reference != null)
            {
                return true;
            }

            if (logErrors)
            {
                Debug.LogError($"BubbleBattleGameManager: Missing serialized reference `{fieldName}`.", this);
            }

            return false;
        }

        private bool ValidateArray(Transform[] references, string fieldName, int minimumCount, bool logErrors)
        {
            if (references != null && references.Length >= minimumCount)
            {
                for (int i = 0; i < references.Length; i++)
                {
                    if (references[i] != null)
                    {
                        continue;
                    }

                    if (logErrors)
                    {
                        Debug.LogError($"BubbleBattleGameManager: `{fieldName}` has an empty element at index {i}.", this);
                    }

                    return false;
                }

                return true;
            }

            if (logErrors)
            {
                Debug.LogError(
                    $"BubbleBattleGameManager: `{fieldName}` needs at least {minimumCount} serialized transforms.",
                    this);
            }

            return false;
        }
    }
}
