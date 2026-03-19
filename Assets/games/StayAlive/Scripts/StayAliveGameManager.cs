using System;
using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace StayAlive
{
    public class StayAliveGameManager : MonoBehaviour
    {
        private enum MatchPhase
        {
            None = 0,
            Countdown = 1,
            Spinning = 2,
            Warning = 3,
            Explosion = 4,
            Collapse = 5,
            RefillDelay = 6,
            Refill = 7,
            GameOver = 8
        }

        private const string DefaultSettingsPath = "Assets/games/StayAlive/Data/StayAliveGameSettings.asset";
        private const string DefaultPlayerPrefabPath = "Assets/games/StayAlive/Prefabs/Player.prefab";
        private const int RequiredSafeSpriteCount = 4;
        private const float MinCellSize = 0.2f;
        private const float MinSwapInterval = 0.02f;

        [Header("Player Source")]
        [SerializeField] private StayAliveGameSettings settings;
        [SerializeField] private GameObject basePlayerPrefab;
        [SerializeField] private Transform playerParent;
        [SerializeField] private Transform[] spawnPoints;

        [Header("Board")]
        [SerializeField] private Transform cellParent;
        [SerializeField, Min(2)] private int columns = 11;
        [SerializeField, Min(2)] private int rows = 8;
        [SerializeField, Min(MinCellSize)] private float cellSize = 1f;
        [SerializeField, Min(0f)] private float cellGap = 0f;
        [SerializeField] private Vector2 boardCenter = Vector2.zero;
        [SerializeField] private Sprite[] symbolSprites;
        [SerializeField] private Sprite[] blurredSymbolSprites;
        [SerializeField] private Sprite bombSprite;
        [SerializeField] private Sprite blurredBombSprite;
        [SerializeField] private Color bombFallbackColor = Color.red;
        [SerializeField] private Color idleCellColor = new Color(0.95f, 0.95f, 0.98f, 1f);
        [SerializeField] private Color warningCellColor = new Color(1f, 0.6f, 0.1f, 1f);
        [SerializeField] private Color explosionCellColor = new Color(1f, 0.12f, 0.12f, 1f);
        [SerializeField] private Color collapseCellColor = new Color(0.2f, 0.2f, 0.2f, 0f);
        [SerializeField] private Color[] fallbackSymbolColors =
        {
            Color.white,
            new Color(0.2f, 0.78f, 0.26f, 1f),
            new Color(1f, 0.88f, 0.12f, 1f)
        };

        [Header("Round Flow")]
        [SerializeField] private bool spawnPlayers = true;
        [SerializeField, Min(1)] private int totalPlayers = 1;
        [SerializeField, Min(1)] private int humanPlayers = 1;
        [SerializeField, Min(0f)] private float startCountdownSeconds = 3f;
        [SerializeField, Min(0.1f)] private float spinDurationSeconds = 4f;
        [SerializeField, Min(MinSwapInterval)] private float symbolSwapIntervalSeconds = 0.04f;
        [SerializeField, Min(0.1f)] private float warningDurationSeconds = 4f;
        [SerializeField, Min(0.1f)] private float explosionDurationSeconds = 0.5f;
        [SerializeField, Min(0.1f)] private float collapseDurationSeconds = 0.5f;
        [SerializeField, Min(0f)] private float refillDelaySeconds = 0.25f;
        [SerializeField, Min(0.1f)] private float refillDurationSeconds = 0.35f;
        [SerializeField, Range(0f, 1f)] private float difficulty01 = 0.5f;
        [SerializeField, Min(1)] private int easyBombCellsPerRound = 16;
        [SerializeField, Min(1)] private int normalBombCellsPerRound = 22;
        [SerializeField, Min(1)] private int hardBombCellsPerRound = 30;
        [SerializeField, Min(0)] private int easyBombIncreasePerSpin = 5;
        [SerializeField, Min(0)] private int normalBombIncreasePerSpin = 7;
        [SerializeField, Min(0)] private int hardBombIncreasePerSpin = 10;
        [SerializeField] private bool autoStartSpinOnPlay = false;

        [Header("Spin Button")]
        [SerializeField] private bool showSpinButton = true;
        [SerializeField] private string spinButtonLabel = "SPIN";
        [SerializeField] private Rect spinButtonRect = new Rect(20f, 20f, 180f, 56f);

        [Header("Movement")]
        [SerializeField, Min(0.1f)] private float moveSpeed = 5f;
        [SerializeField, Min(0f)] private float playerBounceSpeed = 4.8f;
        [SerializeField, Min(0f)] private float playerBounceMaxSpeed = 6.5f;
        [SerializeField, Min(0f)] private float playerBounceDecay = 8f;
        [SerializeField, Min(0f)] private float playerBounceCooldownSeconds = 0.16f;
        [SerializeField, Min(0f)] private float playerBounceInputSuppressionSeconds = 0.08f;
        [SerializeField, Range(0f, 1f)] private float bombHitRequiredBodyCoverage = 0.2f;

        [Header("Bots")]
        [SerializeField, Min(0.02f)] private float botDecisionInterval = 0.08f;
        [SerializeField, Min(0.05f)] private float botTargetRefreshInterval = 0.25f;
        [SerializeField, Min(0f)] private float botSteeringSmoothness = 10f;

        private readonly List<StayAliveCell> cells = new List<StayAliveCell>();
        private readonly List<StayAlivePlayer> alivePlayers = new List<StayAlivePlayer>();
        private readonly List<StayAlivePlayer> allPlayers = new List<StayAlivePlayer>();
        private readonly HashSet<int> activeBombCellIndices = new HashSet<int>();
        private readonly HashSet<int> queuedBombCellIndices = new HashSet<int>();

        private int[] symbolIndices;
        private int[] queuedStopSymbolIndices;
        private MatchPhase phase;
        private float phaseTimer;
        private float symbolSwapTimer;
        private Sprite fallbackCellSprite;
        private bool explosionResolved;
        private int completedSpinCount;
        private int completedSpinShiftCount;
        private int targetSpinShiftCount;

        private int Columns => settings != null ? settings.Columns : columns;
        private int Rows => settings != null ? settings.Rows : rows;
        private float CellSize => settings != null ? settings.CellSize : cellSize;
        private float CellGap => settings != null ? settings.CellGap : cellGap;
        private Vector2 BoardCenter => settings != null ? settings.BoardCenter : boardCenter;
        private bool SpawnPlayersEnabled => settings != null ? settings.SpawnPlayers : spawnPlayers;
        private int TotalPlayers => settings != null ? settings.TotalPlayers : totalPlayers;
        private int HumanPlayers => settings != null ? settings.HumanPlayers : humanPlayers;
        private float SpinDurationSeconds => settings != null ? settings.SpinDurationSeconds : spinDurationSeconds;
        private float SymbolSwapIntervalSeconds => settings != null ? settings.SymbolSwapIntervalSeconds : symbolSwapIntervalSeconds;
        private float WarningDurationSeconds => settings != null ? settings.WarningDurationSeconds : warningDurationSeconds;
        private float ExplosionDurationSeconds => settings != null ? settings.ExplosionDurationSeconds : explosionDurationSeconds;
        private float CollapseDurationSeconds => settings != null ? settings.CollapseDurationSeconds : collapseDurationSeconds;
        private float RefillDelaySeconds => settings != null ? settings.RefillDelaySeconds : refillDelaySeconds;
        private float RefillDurationSeconds => settings != null ? settings.RefillDurationSeconds : refillDurationSeconds;
        private int EasyBombCellsPerRound => settings != null ? settings.EasyBombCellsPerRound : easyBombCellsPerRound;
        private int NormalBombCellsPerRound => settings != null ? settings.NormalBombCellsPerRound : normalBombCellsPerRound;
        private int HardBombCellsPerRound => settings != null ? settings.HardBombCellsPerRound : hardBombCellsPerRound;
        private int EasyBombIncreasePerSpin => settings != null ? settings.EasyBombIncreasePerSpin : easyBombIncreasePerSpin;
        private int NormalBombIncreasePerSpin => settings != null ? settings.NormalBombIncreasePerSpin : normalBombIncreasePerSpin;
        private int HardBombIncreasePerSpin => settings != null ? settings.HardBombIncreasePerSpin : hardBombIncreasePerSpin;
        private bool AutoStartSpinOnPlayEnabled => settings != null ? settings.AutoStartSpinOnPlay : autoStartSpinOnPlay;

        public float MoveSpeed => settings != null ? settings.MoveSpeed : moveSpeed;
        public float PlayerBounceSpeed => settings != null ? settings.PlayerBounceSpeed : playerBounceSpeed;
        public float PlayerBounceMaxSpeed => settings != null ? settings.PlayerBounceMaxSpeed : playerBounceMaxSpeed;
        public float PlayerBounceDecay => settings != null ? settings.PlayerBounceDecay : playerBounceDecay;
        public float PlayerBounceCooldownSeconds => settings != null ? settings.PlayerBounceCooldownSeconds : playerBounceCooldownSeconds;
        public float PlayerBounceInputSuppressionSeconds => settings != null ? settings.PlayerBounceInputSuppressionSeconds : playerBounceInputSuppressionSeconds;
        public float BotDecisionInterval => settings != null ? settings.BotDecisionInterval : botDecisionInterval;
        public float BotTargetRefreshInterval => settings != null ? settings.BotTargetRefreshInterval : botTargetRefreshInterval;
        public float BotSteeringSmoothness => settings != null ? settings.BotSteeringSmoothness : botSteeringSmoothness;
        public bool IsWarningActive => phase == MatchPhase.Warning;
        public bool IsExplosionActive => phase == MatchPhase.Explosion;
        public bool IsMatchOver => phase == MatchPhase.GameOver;
        public bool IsSpinning => phase == MatchPhase.Spinning;
        public float Difficulty01 => settings != null ? settings.Difficulty01 : difficulty01;
        public int BombCellsPerRound => EvaluateBombCellsPerRound();
        public int CompletedSpinCount => completedSpinCount;
        public event Action<int> GameEnded;

        private void Awake()
        {
            EnsureFallbackSprite();
            CacheBoardCells();
        }

        private void Start()
        {
            StartMatch();
        }

        private void OnGUI()
        {
            if (phase != MatchPhase.Countdown)
            {
                return;
            }

            GUIStyle style = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 56,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.white }
            };

            Rect rect = new Rect(0f, 0f, Screen.width, Screen.height);
            int displayNumber = Mathf.Max(1, Mathf.CeilToInt(phaseTimer));
            GUI.Label(rect, displayNumber.ToString(), style);
        }

        private void Update()
        {
            if (phase == MatchPhase.GameOver || phase == MatchPhase.None)
            {
                return;
            }

            phaseTimer -= Time.deltaTime;

            switch (phase)
            {
                case MatchPhase.Spinning:
                    UpdateSpinningPhase();
                    break;

                case MatchPhase.Countdown:
                    if (phaseTimer <= 0f)
                    {
                        BeginSpinPhase();
                    }
                    break;

                case MatchPhase.Warning:
                    if (phaseTimer <= 0f)
                    {
                        BeginExplosionPhase();
                    }
                    break;

                case MatchPhase.Explosion:
                    UpdateExplosionPhase();
                    break;

                case MatchPhase.Collapse:
                    UpdateCollapsePhase();
                    break;

                case MatchPhase.RefillDelay:
                    UpdateRefillDelayPhase();
                    break;

                case MatchPhase.Refill:
                    UpdateRefillPhase();
                    break;
            }
        }

        public void StartMatch()
        {
            if (!ValidateSceneSetup())
            {
                phase = MatchPhase.GameOver;
                return;
            }

            if (SpawnPlayersEnabled)
            {
                ClearPlayers();
                SpawnPlayers();
            }

            completedSpinCount = 0;
            InitializeIdleBoardState();
            BeginCountdownPhase();
        }

        public void StartSpinButton()
        {
            if (phase != MatchPhase.None)
            {
                return;
            }

            InitializeIdleBoardState();
            BeginCountdownPhase();
        }

        public Vector2 ClampToBoard(Vector2 position)
        {
            Vector2 extents = GetBoardExtents();
            float padding = CellSize * 0.35f;
            return new Vector2(
                Mathf.Clamp(position.x, BoardCenter.x - extents.x + padding, BoardCenter.x + extents.x - padding),
                Mathf.Clamp(position.y, BoardCenter.y - extents.y + padding, BoardCenter.y + extents.y - padding));
        }

        public Vector2 GetBoardCenter()
        {
            return BoardCenter;
        }

        public Vector2 GetEdgeAvoidance(Vector2 position)
        {
            Vector2 extents = GetBoardExtents();
            Vector2 avoidance = Vector2.zero;
            float threshold = Mathf.Max(CellSize * 0.75f, 0.4f);

            if (position.x < BoardCenter.x - extents.x + threshold)
            {
                avoidance.x += 1f;
            }
            else if (position.x > BoardCenter.x + extents.x - threshold)
            {
                avoidance.x -= 1f;
            }

            if (position.y < BoardCenter.y - extents.y + threshold)
            {
                avoidance.y += 1f;
            }
            else if (position.y > BoardCenter.y + extents.y - threshold)
            {
                avoidance.y -= 1f;
            }

            return avoidance.sqrMagnitude > 1f ? avoidance.normalized : avoidance;
        }

        public Vector2 GetBombAvoidance(Vector2 position)
        {
            if (activeBombCellIndices.Count == 0)
            {
                return Vector2.zero;
            }

            Vector2 avoidance = Vector2.zero;
            foreach (int cellIndex in activeBombCellIndices)
            {
                Vector2 bombPosition = GetCellCenter(cellIndex);
                Vector2 away = position - bombPosition;
                float distance = away.magnitude;
                float radius = Mathf.Max(CellSize * 1.75f, 0.1f);
                if (distance <= 0.001f)
                {
                    avoidance += UnityEngine.Random.insideUnitCircle.normalized;
                    continue;
                }

                float weight = Mathf.Clamp01(1f - (distance / radius));
                if (weight <= 0f)
                {
                    continue;
                }

                avoidance += away.normalized * weight;
            }

            return avoidance.sqrMagnitude > 1f ? avoidance.normalized : avoidance;
        }

        public bool TryGetBestSafePoint(Vector2 currentPosition, out Vector2 safePoint)
        {
            safePoint = BoardCenter;
            if (cells.Count == 0)
            {
                return false;
            }

            bool found = false;
            float bestScore = float.NegativeInfinity;
            for (int i = 0; i < cells.Count; i++)
            {
                if (activeBombCellIndices.Contains(i))
                {
                    continue;
                }

                Vector2 center = GetCellCenter(i);
                float nearestBombDistance = activeBombCellIndices.Count == 0
                    ? CellSize * 4f
                    : GetNearestBombDistance(center);
                float distanceFromCurrent = Vector2.Distance(currentPosition, center);
                float edgePenalty = GetEdgePenalty(center);
                float score = (nearestBombDistance * 2f) - distanceFromCurrent - edgePenalty;

                if (score <= bestScore)
                {
                    continue;
                }

                bestScore = score;
                safePoint = center;
                found = true;
            }

            return found;
        }

        private void UpdateSpinningPhase()
        {
            symbolSwapTimer -= Time.deltaTime;
            while (symbolSwapTimer <= 0f)
            {
                ShiftSymbolsDownPerColumn();
                completedSpinShiftCount++;
                symbolSwapTimer += Mathf.Max(MinSwapInterval, SymbolSwapIntervalSeconds);

                if (completedSpinShiftCount >= targetSpinShiftCount)
                {
                    StopSpinPhase();
                    return;
                }
            }

            if (phaseTimer <= 0f && completedSpinShiftCount >= targetSpinShiftCount)
            {
                StopSpinPhase();
            }
        }

        private void UpdateExplosionPhase()
        {
            if (!explosionResolved)
            {
                ResolveExplosionHits();
                explosionResolved = true;
            }

            if (phaseTimer > 0f)
            {
                return;
            }

            BeginCollapsePhase();
        }

        private void UpdateCollapsePhase()
        {
            if (phaseTimer > 0f)
            {
                return;
            }

            RefillCollapsedBombCells();
            BeginRefillDelayPhase();
        }

        private void UpdateRefillDelayPhase()
        {
            if (phaseTimer > 0f)
            {
                return;
            }

            BeginRefillPhase();
        }

        private void UpdateRefillPhase()
        {
            if (phaseTimer > 0f)
            {
                return;
            }

            if (SpawnPlayersEnabled && allPlayers.Count > 1 && alivePlayers.Count <= 1)
            {
                FinishMatch();
                return;
            }

            activeBombCellIndices.Clear();
            queuedBombCellIndices.Clear();
            RefreshBoardVisualsForIdle();
            BeginSpinPhase();
        }

        private void BeginCountdownPhase()
        {
            phase = MatchPhase.Countdown;
            phaseTimer = Mathf.Max(0f, startCountdownSeconds);
            symbolSwapTimer = 0f;
            if (phaseTimer <= 0f)
            {
                BeginSpinPhase();
            }
        }

        private void BeginSpinPhase()
        {
            PrepareStoppedBoardState(BombCellsPerRound);
            phase = MatchPhase.Spinning;
            phaseTimer = Mathf.Max(0.1f, SpinDurationSeconds);
            symbolSwapTimer = 0f;
            explosionResolved = false;
            completedSpinShiftCount = 0;
            targetSpinShiftCount = Mathf.Max(Rows, Mathf.CeilToInt(SpinDurationSeconds / Mathf.Max(MinSwapInterval, SymbolSwapIntervalSeconds)));
            activeBombCellIndices.Clear();
            FillSymbolsRandomly(GetSpinningBombChance());
            RefreshBoardVisualsForSpin();
        }

        private void StopSpinPhase()
        {
            ApplyPreparedStoppedBoardState();
            symbolSwapTimer = 0f;
            RefreshBoardVisualsForIdle();
            completedSpinCount++;
            BeginWarningPhase();
        }

        private void BeginWarningPhase()
        {
            phase = MatchPhase.Warning;
            phaseTimer = Mathf.Max(0.1f, WarningDurationSeconds);
            explosionResolved = false;

            if (activeBombCellIndices.Count == 0)
            {
                ApplyPreparedStoppedBoardState();
            }

            RefreshBoardVisualsForWarning();
        }

        private void BeginExplosionPhase()
        {
            phase = MatchPhase.Explosion;
            phaseTimer = Mathf.Max(0.1f, ExplosionDurationSeconds);
            explosionResolved = false;
            RefreshBoardVisualsForExplosion();
        }

        private void BeginCollapsePhase()
        {
            phase = MatchPhase.Collapse;
            phaseTimer = Mathf.Max(0.1f, CollapseDurationSeconds);
            RefreshBoardVisualsForCollapse();
        }

        private void BeginRefillPhase()
        {
            phase = MatchPhase.Refill;
            phaseTimer = Mathf.Max(0.1f, RefillDurationSeconds);
            RefreshBoardVisualsForRefill();
        }

        private void BeginRefillDelayPhase()
        {
            phase = MatchPhase.RefillDelay;
            phaseTimer = Mathf.Max(0f, RefillDelaySeconds);
            RefreshBoardVisualsForHiddenRefill();
        }

        private void FinishMatch()
        {
            phase = MatchPhase.GameOver;
            RefreshBoardVisualsForSpin();
            GameEnded?.Invoke(FindWinningChairId());
        }

        private bool CacheBoardCells()
        {
            int requiredCellCount = Mathf.Max(2, Columns) * Mathf.Max(2, Rows);
            cells.Clear();

            if (cellParent == null)
            {
                return false;
            }

            symbolIndices = new int[requiredCellCount];
            queuedStopSymbolIndices = new int[requiredCellCount];
            PositionBoard();

            StayAliveCell[] existingCells = cellParent.GetComponentsInChildren<StayAliveCell>(true);
            if (existingCells == null || existingCells.Length != requiredCellCount)
            {
                return false;
            }

            Array.Sort(existingCells, CompareCellsByHierarchy);
            for (int i = 0; i < existingCells.Length; i++)
            {
                StayAliveCell cell = existingCells[i];
                int column = i % Columns;
                int row = i / Columns;
                cell.Initialize(i, column, row);
                cell.SetNormalWorldSize(Vector2.one * CellSize);
                cell.SetSpinningWorldSize(Vector2.one * CellSize);
                cell.transform.localPosition = GetLocalCellPosition(column, row);
                cells.Add(cell);
            }

            FillSymbolsRandomly();
            RefreshBoardVisualsForIdle();
            return true;
        }

        private void SpawnPlayers()
        {
            if (basePlayerPrefab == null)
            {
                Debug.LogError("StayAliveGameManager: Base player prefab is not assigned.");
                phase = MatchPhase.GameOver;
                return;
            }

            int playerCount = Mathf.Clamp(TotalPlayers, 1, 8);
            int localHumanCount = Mathf.Clamp(HumanPlayers, 1, playerCount);

            for (int i = 0; i < playerCount; i++)
            {
                Vector2 spawnPosition = GetSpawnPosition(i);
                GameObject playerObject = Instantiate(
                    basePlayerPrefab,
                    new Vector3(spawnPosition.x, spawnPosition.y, 0f),
                    Quaternion.identity,
                    playerParent != null ? playerParent : transform);

                StayAlivePlayer stayAlivePlayer = playerObject.GetComponent<StayAlivePlayer>();
                if (stayAlivePlayer == null)
                {
                    stayAlivePlayer = playerObject.AddComponent<StayAlivePlayer>();
                }

                stayAlivePlayer.SetAppearanceIndex(i);
                stayAlivePlayer.Initialize(this, isHuman: i < localHumanCount, chairId: i);

                allPlayers.Add(stayAlivePlayer);
                alivePlayers.Add(stayAlivePlayer);
            }
        }

        private void ClearPlayers()
        {
            for (int i = 0; i < allPlayers.Count; i++)
            {
                if (allPlayers[i] != null)
                {
                    DestroyImmediateSafe(allPlayers[i].gameObject);
                }
            }

            allPlayers.Clear();
            alivePlayers.Clear();
        }

        private void ResolveExplosionHits()
        {
            if (activeBombCellIndices.Count == 0)
            {
                return;
            }

            for (int i = alivePlayers.Count - 1; i >= 0; i--)
            {
                StayAlivePlayer player = alivePlayers[i];
                if (player == null || !player.IsAlive)
                {
                    alivePlayers.RemoveAt(i);
                    continue;
                }

                if (!IsPlayerTouchingBombCell(player))
                {
                    continue;
                }

                player.Eliminate();
                alivePlayers.RemoveAt(i);
            }
        }

        private bool IsPlayerTouchingBombCell(StayAlivePlayer player)
        {
            Bounds bounds = player.WorldBounds;
            Rect playerRect = new Rect(bounds.min.x, bounds.min.y, bounds.size.x, bounds.size.y);
            float playerArea = Mathf.Max(0.0001f, playerRect.width * playerRect.height);
            Vector2 playerCenter = bounds.center;

            foreach (int cellIndex in activeBombCellIndices)
            {
                Rect cellRect = GetCellRect(cellIndex);
                if (!cellRect.Overlaps(playerRect))
                {
                    continue;
                }

                if (cellRect.Contains(playerCenter))
                {
                    return true;
                }

                float overlapWidth = Mathf.Min(playerRect.xMax, cellRect.xMax) - Mathf.Max(playerRect.xMin, cellRect.xMin);
                float overlapHeight = Mathf.Min(playerRect.yMax, cellRect.yMax) - Mathf.Max(playerRect.yMin, cellRect.yMin);
                if (overlapWidth <= 0f || overlapHeight <= 0f)
                {
                    continue;
                }

                float overlapArea = overlapWidth * overlapHeight;
                float overlapRatio = overlapArea / playerArea;
                if (overlapRatio >= Mathf.Clamp01(bombHitRequiredBodyCoverage))
                {
                    return true;
                }
            }

            return false;
        }

        private void FillSymbolsRandomly()
        {
            FillSymbolsRandomly(GetDefaultBombChance());
        }

        private void FillSymbolsRandomly(float bombChance)
        {
            if (symbolIndices == null)
            {
                return;
            }

            for (int i = 0; i < symbolIndices.Length; i++)
            {
                symbolIndices[i] = GetRandomSymbolIndex(bombChance);
            }
        }

        private void ShiftSymbolsDownPerColumn()
        {
            float bombChance = GetSpinningBombChance();
            int landingRow = GetLandingSourceRow();
            for (int column = 0; column < Columns; column++)
            {
                for (int row = Rows - 1; row >= 1; row--)
                {
                    symbolIndices[GetCellIndex(column, row)] = symbolIndices[GetCellIndex(column, row - 1)];
                }

                symbolIndices[GetCellIndex(column, 0)] = landingRow >= 0
                    ? queuedStopSymbolIndices[GetCellIndex(column, landingRow)]
                    : GetRandomSymbolIndex(bombChance);
            }

            RefreshBoardVisualsForSpin();
        }

        private void PrepareStoppedBoardState(int desiredBombCount)
        {
            if (symbolIndices == null)
            {
                return;
            }

            EnsureQueuedStopBuffer();
            SelectBombCells(queuedBombCellIndices, desiredBombCount);

            int safeSymbolCount = GetSafeSymbolCount();
            for (int i = 0; i < queuedStopSymbolIndices.Length; i++)
            {
                queuedStopSymbolIndices[i] = UnityEngine.Random.Range(0, safeSymbolCount);
            }

            foreach (int bombCellIndex in queuedBombCellIndices)
            {
                if (bombCellIndex < 0 || bombCellIndex >= queuedStopSymbolIndices.Length)
                {
                    continue;
                }

                queuedStopSymbolIndices[bombCellIndex] = safeSymbolCount;
            }
        }

        private void ApplyPreparedStoppedBoardState()
        {
            if (symbolIndices == null)
            {
                return;
            }

            EnsureQueuedStopBuffer();
            if (queuedBombCellIndices.Count == 0)
            {
                PrepareStoppedBoardState(BombCellsPerRound);
            }

            for (int i = 0; i < symbolIndices.Length; i++)
            {
                symbolIndices[i] = queuedStopSymbolIndices[i];
            }

            activeBombCellIndices.Clear();
            foreach (int bombCellIndex in queuedBombCellIndices)
            {
                activeBombCellIndices.Add(bombCellIndex);
            }
        }

        private void SelectBombCells(HashSet<int> targetCellIndices, int desiredBombCount)
        {
            targetCellIndices.Clear();
            int availableCellCount = Mathf.Max(1, Columns * Rows);
            int clampedBombCount = Mathf.Clamp(desiredBombCount, 1, Mathf.Max(1, availableCellCount - 1));
            while (targetCellIndices.Count < clampedBombCount)
            {
                targetCellIndices.Add(UnityEngine.Random.Range(0, availableCellCount));
            }
        }

        private int EvaluateBombCellsPerRound()
        {
            int maxBombCells = Mathf.Max(1, (Columns * Rows) - 1);
            int baseBombCells = EvaluateBaseBombCellsPerRound();
            int increasePerSpin = EvaluateBombIncreasePerSpin();
            int scaledBombCount = baseBombCells + (completedSpinCount * increasePerSpin);
            return Mathf.Clamp(scaledBombCount, 1, maxBombCells);
        }

        private int EvaluateBaseBombCellsPerRound()
        {
            int easy = Mathf.Max(1, EasyBombCellsPerRound);
            int normal = Mathf.Max(easy, NormalBombCellsPerRound);
            int hard = Mathf.Max(normal, HardBombCellsPerRound);

            if (Difficulty01 <= 0.5f)
            {
                float t = Mathf.InverseLerp(0f, 0.5f, Difficulty01);
                return Mathf.RoundToInt(Mathf.Lerp(easy, normal, t));
            }

            float hardT = Mathf.InverseLerp(0.5f, 1f, Difficulty01);
            return Mathf.RoundToInt(Mathf.Lerp(normal, hard, hardT));
        }

        private int EvaluateBombIncreasePerSpin()
        {
            int easy = Mathf.Max(0, EasyBombIncreasePerSpin);
            int normal = Mathf.Max(easy, NormalBombIncreasePerSpin);
            int hard = Mathf.Max(normal, HardBombIncreasePerSpin);

            if (Difficulty01 <= 0.5f)
            {
                float t = Mathf.InverseLerp(0f, 0.5f, Difficulty01);
                return Mathf.RoundToInt(Mathf.Lerp(easy, normal, t));
            }

            float hardT = Mathf.InverseLerp(0.5f, 1f, Difficulty01);
            return Mathf.RoundToInt(Mathf.Lerp(normal, hard, hardT));
        }

        private float GetDefaultBombChance()
        {
            return 1f / Mathf.Max(1, GetSymbolCount());
        }

        private float GetSpinningBombChance()
        {
            int totalCellCount = Mathf.Max(1, Columns * Rows);
            return Mathf.Clamp01((float)BombCellsPerRound / totalCellCount);
        }

        private int GetLandingSourceRow()
        {
            if (queuedStopSymbolIndices == null || completedSpinShiftCount >= targetSpinShiftCount)
            {
                return -1;
            }

            int shiftsRemaining = targetSpinShiftCount - completedSpinShiftCount;
            if (shiftsRemaining > Rows)
            {
                return -1;
            }

            return shiftsRemaining - 1;
        }

        private int GetRandomSymbolIndex(float bombChance)
        {
            float clampedBombChance = Mathf.Clamp01(bombChance);
            int safeSymbolCount = GetSafeSymbolCount();
            if (UnityEngine.Random.value < clampedBombChance)
            {
                return safeSymbolCount;
            }

            return UnityEngine.Random.Range(0, safeSymbolCount);
        }

        private void RefreshBoardVisualsForSpin()
        {
            for (int i = 0; i < cells.Count; i++)
            {
                StayAliveCell cell = cells[i];
                if (cell == null)
                {
                    continue;
                }

                cell.ShowSpinning(GetSymbolSprite(symbolIndices[i], true), GetSymbolColor(symbolIndices[i]));
            }
        }

        private void RefreshBoardVisualsForIdle()
        {
            for (int i = 0; i < cells.Count; i++)
            {
                StayAliveCell cell = cells[i];
                if (cell == null)
                {
                    continue;
                }

                cell.ShowIdle(GetSymbolSprite(symbolIndices[i], false), GetSymbolColor(symbolIndices[i]));
            }
        }

        private void RefreshBoardVisualsForWarning()
        {
            for (int i = 0; i < cells.Count; i++)
            {
                StayAliveCell cell = cells[i];
                if (cell == null)
                {
                    continue;
                }

                bool isBombCell = activeBombCellIndices.Contains(i);
                if (isBombCell)
                {
                    Sprite sprite = bombSprite != null ? bombSprite : GetSymbolSprite(symbolIndices[i], false);
                    Color color = bombSprite != null ? Color.white : bombFallbackColor;
                    cell.ShowWarning(sprite, color, warningCellColor);
                }
                else
                {
                    cell.ShowIdle(GetSymbolSprite(symbolIndices[i], false), GetSymbolColor(symbolIndices[i]));
                }
            }
        }

        private void RefreshBoardVisualsForExplosion()
        {
            for (int i = 0; i < cells.Count; i++)
            {
                StayAliveCell cell = cells[i];
                if (cell == null)
                {
                    continue;
                }

                bool isBombCell = activeBombCellIndices.Contains(i);
                if (isBombCell)
                {
                    Sprite sprite = bombSprite != null ? bombSprite : GetSymbolSprite(symbolIndices[i], false);
                    cell.ShowExplosion(sprite, explosionCellColor, Color.white);
                }
                else
                {
                    cell.ShowIdle(GetSymbolSprite(symbolIndices[i], false), GetSymbolColor(symbolIndices[i]));
                }
            }
        }

        private void RefreshBoardVisualsForCollapse()
        {
            for (int i = 0; i < cells.Count; i++)
            {
                StayAliveCell cell = cells[i];
                if (cell == null)
                {
                    continue;
                }

                bool isBombCell = activeBombCellIndices.Contains(i);
                if (isBombCell)
                {
                    Sprite sprite = bombSprite != null ? bombSprite : GetSymbolSprite(symbolIndices[i], false);
                    Color color = bombSprite != null ? Color.white : bombFallbackColor;
                    cell.ShowCollapse(sprite, color, collapseCellColor, CollapseDurationSeconds);
                }
                else
                {
                    cell.ShowIdle(GetSymbolSprite(symbolIndices[i], false), GetSymbolColor(symbolIndices[i]));
                }
            }
        }

        private void RefreshBoardVisualsForRefill()
        {
            for (int i = 0; i < cells.Count; i++)
            {
                StayAliveCell cell = cells[i];
                if (cell == null)
                {
                    continue;
                }

                bool isRefillingCell = activeBombCellIndices.Contains(i);
                if (isRefillingCell)
                {
                    Color targetColor = GetSymbolColor(symbolIndices[i]);
                    cell.ShowRefill(
                        GetSymbolSprite(symbolIndices[i], false),
                        targetColor,
                        WithAlpha(targetColor, 0f),
                        RefillDurationSeconds);
                }
                else
                {
                    cell.ShowIdle(GetSymbolSprite(symbolIndices[i], false), GetSymbolColor(symbolIndices[i]));
                }
            }
        }

        private void RefreshBoardVisualsForHiddenRefill()
        {
            for (int i = 0; i < cells.Count; i++)
            {
                StayAliveCell cell = cells[i];
                if (cell == null)
                {
                    continue;
                }

                bool isRefillingCell = activeBombCellIndices.Contains(i);
                if (isRefillingCell)
                {
                    cell.ShowHidden(GetSymbolSprite(symbolIndices[i], false), GetSymbolColor(symbolIndices[i]));
                }
                else
                {
                    cell.ShowIdle(GetSymbolSprite(symbolIndices[i], false), GetSymbolColor(symbolIndices[i]));
                }
            }
        }

        private int FindWinningChairId()
        {
            for (int i = 0; i < alivePlayers.Count; i++)
            {
                StayAlivePlayer player = alivePlayers[i];
                if (player != null && player.IsAlive)
                {
                    return player.ChairId;
                }
            }

            return -1;
        }

        private void InitializeIdleBoardState()
        {
            activeBombCellIndices.Clear();
            queuedBombCellIndices.Clear();
            completedSpinShiftCount = 0;
            targetSpinShiftCount = 0;
            FillSymbolsRandomly();
            RefreshBoardVisualsForIdle();
        }

        private void RefillCollapsedBombCells()
        {
            int safeSymbolCount = GetSafeSymbolCount();
            foreach (int bombCellIndex in activeBombCellIndices)
            {
                if (bombCellIndex < 0 || bombCellIndex >= symbolIndices.Length)
                {
                    continue;
                }

                symbolIndices[bombCellIndex] = UnityEngine.Random.Range(0, safeSymbolCount);
            }
        }

        private static Color WithAlpha(Color color, float alpha)
        {
            color.a = alpha;
            return color;
        }

        private void EnsureQueuedStopBuffer()
        {
            int requiredCellCount = Mathf.Max(1, Columns * Rows);
            if (queuedStopSymbolIndices != null && queuedStopSymbolIndices.Length == requiredCellCount)
            {
                return;
            }

            queuedStopSymbolIndices = new int[requiredCellCount];
        }

        private Vector2 GetSpawnPosition(int playerIndex)
        {
            if (TotalPlayers <= 1 && playerIndex <= 0)
            {
                int centerColumn = Mathf.Clamp(Columns / 2, 0, Columns - 1);
                int centerRow = Mathf.Clamp(Rows / 2, 0, Rows - 1);
                return GetCellCenter(GetCellIndex(centerColumn, centerRow));
            }

            if (spawnPoints != null && spawnPoints.Length > 0)
            {
                Transform spawnPoint = spawnPoints[Mathf.Clamp(playerIndex, 0, spawnPoints.Length - 1)];
                if (spawnPoint != null)
                {
                    return spawnPoint.position;
                }
            }

            Vector2[] spawnCells =
            {
                new Vector2(1f, 1f),
                new Vector2(Columns - 2f, Rows - 2f),
                new Vector2(Columns - 2f, 1f),
                new Vector2(1f, Rows - 2f),
                new Vector2(Columns * 0.5f, 1f),
                new Vector2(Columns * 0.5f, Rows - 2f),
                new Vector2(1f, Rows * 0.5f),
                new Vector2(Columns - 2f, Rows * 0.5f)
            };

            int safeIndex = Mathf.Clamp(playerIndex, 0, spawnCells.Length - 1);
            Vector2 cellCoords = spawnCells[safeIndex];
            int column = Mathf.Clamp(Mathf.RoundToInt(cellCoords.x), 0, Columns - 1);
            int row = Mathf.Clamp(Mathf.RoundToInt(cellCoords.y), 0, Rows - 1);
            return GetCellCenter(GetCellIndex(column, row));
        }

        private bool HasValidSpawnPoints()
        {
            if (spawnPoints == null || spawnPoints.Length == 0)
            {
                return false;
            }

            int requiredCount = Mathf.Clamp(TotalPlayers, 1, 8);
            if (spawnPoints.Length < requiredCount)
            {
                return false;
            }

            for (int i = 0; i < requiredCount; i++)
            {
                if (spawnPoints[i] == null)
                {
                    return false;
                }
            }

            return true;
        }

        private float GetNearestBombDistance(Vector2 point)
        {
            float nearestDistance = float.MaxValue;
            foreach (int bombCellIndex in activeBombCellIndices)
            {
                nearestDistance = Mathf.Min(nearestDistance, Vector2.Distance(point, GetCellCenter(bombCellIndex)));
            }

            return nearestDistance < float.MaxValue ? nearestDistance : 0f;
        }

        private float GetEdgePenalty(Vector2 point)
        {
            Vector2 extents = GetBoardExtents();
            float left = Mathf.Abs(point.x - (BoardCenter.x - extents.x));
            float right = Mathf.Abs((BoardCenter.x + extents.x) - point.x);
            float bottom = Mathf.Abs(point.y - (BoardCenter.y - extents.y));
            float top = Mathf.Abs((BoardCenter.y + extents.y) - point.y);
            float nearestEdge = Mathf.Min(Mathf.Min(left, right), Mathf.Min(bottom, top));
            float threshold = Mathf.Max(CellSize, 0.5f);
            return nearestEdge >= threshold ? 0f : (threshold - nearestEdge) * 1.5f;
        }

        private Sprite GetSymbolSprite(int symbolIndex, bool useBlurredVariant = false)
        {
            if (IsBombSymbol(symbolIndex))
            {
                if (useBlurredVariant && blurredBombSprite != null)
                {
                    return blurredBombSprite;
                }

                return bombSprite != null ? bombSprite : fallbackCellSprite;
            }

            int safeIndex = Mathf.Abs(symbolIndex) % GetSafeSymbolCount();
            if (useBlurredVariant
                && blurredSymbolSprites != null
                && safeIndex >= 0
                && safeIndex < blurredSymbolSprites.Length
                && blurredSymbolSprites[safeIndex] != null)
            {
                return blurredSymbolSprites[safeIndex];
            }

            if (symbolSprites != null && safeIndex >= 0 && safeIndex < symbolSprites.Length && symbolSprites[safeIndex] != null)
            {
                return symbolSprites[safeIndex];
            }

            return fallbackCellSprite;
        }

        private Color GetSymbolColor(int symbolIndex)
        {
            if (IsBombSymbol(symbolIndex))
            {
                return bombSprite != null ? Color.white : bombFallbackColor;
            }

            return Color.white;
        }

        private int GetSymbolCount()
        {
            return GetSafeSymbolCount() + 1;
        }

        private int GetSafeSymbolCount()
        {
            return RequiredSafeSpriteCount;
        }

        private bool IsBombSymbol(int symbolIndex)
        {
            return Mathf.Abs(symbolIndex) % GetSymbolCount() == GetSafeSymbolCount();
        }

        private Vector2 GetBoardExtents()
        {
            return new Vector2(
                ((Columns - 1) * (CellSize + CellGap) * 0.5f) + (CellSize * 0.5f),
                ((Rows - 1) * (CellSize + CellGap) * 0.5f) + (CellSize * 0.5f));
        }

        private Rect GetCellRect(int cellIndex)
        {
            Vector2 center = GetCellCenter(cellIndex);
            float halfSize = CellSize * 0.5f;
            return new Rect(center.x - halfSize, center.y - halfSize, CellSize, CellSize);
        }

        private Vector2 GetCellCenter(int cellIndex)
        {
            int column = cellIndex % Columns;
            int row = cellIndex / Columns;
            Vector2 localPosition = GetLocalCellPosition(column, row);
            return BoardCenter + localPosition;
        }

        private Vector2 GetLocalCellPosition(int column, int row)
        {
            float pitch = CellSize + CellGap;
            float startX = -((Columns - 1) * pitch) * 0.5f;
            float startY = ((Rows - 1) * pitch) * 0.5f;
            return new Vector2(startX + (column * pitch), startY - (row * pitch));
        }

        private int GetCellIndex(int column, int row)
        {
            return (row * Columns) + column;
        }

        private void PositionBoard()
        {
            if (cellParent == null)
            {
                return;
            }

            cellParent.position = BoardCenter;
            for (int i = 0; i < cells.Count; i++)
            {
                StayAliveCell cell = cells[i];
                if (cell == null)
                {
                    continue;
                }

                cell.transform.localPosition = GetLocalCellPosition(cell.Column, cell.Row);
            }
        }

        private void EnsureFallbackSprite()
        {
            if (fallbackCellSprite != null)
            {
                return;
            }

            Texture2D texture = new Texture2D(2, 2, TextureFormat.ARGB32, false);
            texture.SetPixels(new[]
            {
                Color.white, Color.white,
                Color.white, Color.white
            });
            texture.Apply();
            texture.wrapMode = TextureWrapMode.Clamp;
            fallbackCellSprite = Sprite.Create(texture, new Rect(0f, 0f, 2f, 2f), new Vector2(0.5f, 0.5f), 2f);
        }

        private bool ValidateSceneSetup()
        {
            if (SpawnPlayersEnabled && playerParent == null)
            {
                Debug.LogError("StayAliveGameManager: Assign a pre-created Players parent in the scene.");
                return false;
            }

            if (SpawnPlayersEnabled && !HasValidSpawnPoints())
            {
                Debug.LogError("StayAliveGameManager: Assign pre-created spawnPoints in the scene.");
                return false;
            }

            if (cellParent == null)
            {
                Debug.LogError("StayAliveGameManager: Assign a pre-created Board parent in the scene.");
                return false;
            }

            if (!CacheBoardCells())
            {
                Debug.LogError($"StayAliveGameManager: Board must already contain {Columns * Rows} StayAliveCell children before play.");
                return false;
            }

            return true;
        }

        private static int CompareCellsByHierarchy(StayAliveCell left, StayAliveCell right)
        {
            if (left == right)
            {
                return 0;
            }

            if (left == null)
            {
                return 1;
            }

            if (right == null)
            {
                return -1;
            }

            return GetHierarchyPath(left.transform).CompareTo(GetHierarchyPath(right.transform));
        }

        private static string GetHierarchyPath(Transform target)
        {
            if (target == null)
            {
                return string.Empty;
            }

            List<string> parts = new List<string>();
            Transform current = target;
            while (current != null)
            {
                parts.Add(current.GetSiblingIndex().ToString("D4"));
                current = current.parent;
            }

            parts.Reverse();
            return string.Join("/", parts);
        }

        private static void DestroyImmediateSafe(UnityEngine.Object target)
        {
            if (target == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(target);
            }
            else
            {
                DestroyImmediate(target);
            }
        }

        private void OnDrawGizmosSelected()
        {
            Vector2 extents = GetBoardExtents();
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireCube(BoardCenter, extents * 2f);
        }

#if UNITY_EDITOR
        [ContextMenu("Rebuild Board In Editor")]
        private void RebuildBoardInEditor()
        {
            EnsureEditorParentsExist();
            if (cellParent == null)
            {
                return;
            }

            List<GameObject> existingChildren = new List<GameObject>();
            for (int i = cellParent.childCount - 1; i >= 0; i--)
            {
                existingChildren.Add(cellParent.GetChild(i).gameObject);
            }

            for (int i = 0; i < existingChildren.Count; i++)
            {
                Undo.DestroyObjectImmediate(existingChildren[i]);
            }

            int requiredCellCount = Mathf.Max(2, Columns) * Mathf.Max(2, Rows);
            for (int i = 0; i < requiredCellCount; i++)
            {
                int column = i % Columns;
                int row = i / Columns;

                GameObject cellObject = new GameObject($"Cell_{column}_{row}");
                Undo.RegisterCreatedObjectUndo(cellObject, "Create StayAlive Cell");
                cellObject.transform.SetParent(cellParent, false);
                cellObject.transform.localPosition = GetLocalCellPosition(column, row);

                SpriteRenderer renderer = cellObject.AddComponent<SpriteRenderer>();
                renderer.color = idleCellColor;
                renderer.sortingOrder = 0;

                StayAliveCell cell = cellObject.AddComponent<StayAliveCell>();
                cell.Initialize(i, column, row);
                cell.SetNormalWorldSize(Vector2.one * CellSize);
                cell.SetSpinningWorldSize(Vector2.one * CellSize);
            }

            CacheBoardCells();
            EditorUtility.SetDirty(gameObject);
            if (cellParent != null)
            {
                EditorUtility.SetDirty(cellParent.gameObject);
            }
        }

        internal static void NotifySettingsChanged(StayAliveGameSettings changedSettings)
        {
            if (changedSettings == null)
            {
                return;
            }

            EditorApplication.delayCall += () =>
            {
                StayAliveGameManager[] managers = Resources.FindObjectsOfTypeAll<StayAliveGameManager>();
                for (int i = 0; i < managers.Length; i++)
                {
                    StayAliveGameManager manager = managers[i];
                    if (manager == null || EditorUtility.IsPersistent(manager) || manager.settings != changedSettings)
                    {
                        continue;
                    }

                    manager.RefreshEditorFromSettings();
                }
            };
        }

        private void OnValidate()
        {
            if (symbolSprites == null || symbolSprites.Length != RequiredSafeSpriteCount)
            {
                Array.Resize(ref symbolSprites, RequiredSafeSpriteCount);
            }

            if (blurredSymbolSprites == null || blurredSymbolSprites.Length != RequiredSafeSpriteCount)
            {
                Array.Resize(ref blurredSymbolSprites, RequiredSafeSpriteCount);
            }

            columns = Mathf.Max(2, columns);
            rows = Mathf.Max(2, rows);
            totalPlayers = Mathf.Max(1, totalPlayers);
            humanPlayers = Mathf.Clamp(humanPlayers, 1, totalPlayers);
            int maxBombCells = Mathf.Max(1, (columns * rows) - 1);
            easyBombCellsPerRound = Mathf.Clamp(easyBombCellsPerRound, 1, maxBombCells);
            normalBombCellsPerRound = Mathf.Clamp(normalBombCellsPerRound, easyBombCellsPerRound, maxBombCells);
            hardBombCellsPerRound = Mathf.Clamp(hardBombCellsPerRound, normalBombCellsPerRound, maxBombCells);
            easyBombIncreasePerSpin = Mathf.Clamp(easyBombIncreasePerSpin, 0, maxBombCells);
            normalBombIncreasePerSpin = Mathf.Clamp(normalBombIncreasePerSpin, easyBombIncreasePerSpin, maxBombCells);
            hardBombIncreasePerSpin = Mathf.Clamp(hardBombIncreasePerSpin, normalBombIncreasePerSpin, maxBombCells);

            if (settings == null)
            {
                settings = AssetDatabase.LoadAssetAtPath<StayAliveGameSettings>(DefaultSettingsPath);
            }

            if (basePlayerPrefab == null)
            {
                basePlayerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(DefaultPlayerPrefabPath);
            }

            RefreshEditorFromSettings();
        }

        private void RefreshEditorFromSettings()
        {
            if (Application.isPlaying)
            {
                return;
            }

            ApplySettingsToSerializedFallbacks();
            EnsureEditorParentsExist();
            EnsureEditorSpawnPointsExist();
            int requiredCellCount = Mathf.Max(2, Columns) * Mathf.Max(2, Rows);
            int existingCellCount = cellParent != null ? cellParent.GetComponentsInChildren<StayAliveCell>(true).Length : 0;
            if (existingCellCount != requiredCellCount)
            {
                RebuildBoardInEditor();
            }
            else
            {
                CacheBoardCells();
            }

            EditorUtility.SetDirty(gameObject);
            if (cellParent != null)
            {
                EditorUtility.SetDirty(cellParent.gameObject);
            }

            SceneView.RepaintAll();
        }

        private void ApplySettingsToSerializedFallbacks()
        {
            if (settings == null)
            {
                return;
            }

            columns = settings.Columns;
            rows = settings.Rows;
            cellSize = settings.CellSize;
            cellGap = settings.CellGap;
            boardCenter = settings.BoardCenter;

            spawnPlayers = settings.SpawnPlayers;
            totalPlayers = settings.TotalPlayers;
            humanPlayers = settings.HumanPlayers;

            spinDurationSeconds = settings.SpinDurationSeconds;
            symbolSwapIntervalSeconds = settings.SymbolSwapIntervalSeconds;
            warningDurationSeconds = settings.WarningDurationSeconds;
            explosionDurationSeconds = settings.ExplosionDurationSeconds;
            collapseDurationSeconds = settings.CollapseDurationSeconds;
            refillDelaySeconds = settings.RefillDelaySeconds;
            refillDurationSeconds = settings.RefillDurationSeconds;
            difficulty01 = settings.Difficulty01;
            easyBombCellsPerRound = settings.EasyBombCellsPerRound;
            normalBombCellsPerRound = settings.NormalBombCellsPerRound;
            hardBombCellsPerRound = settings.HardBombCellsPerRound;
            easyBombIncreasePerSpin = settings.EasyBombIncreasePerSpin;
            normalBombIncreasePerSpin = settings.NormalBombIncreasePerSpin;
            hardBombIncreasePerSpin = settings.HardBombIncreasePerSpin;
            autoStartSpinOnPlay = settings.AutoStartSpinOnPlay;

            moveSpeed = settings.MoveSpeed;
            playerBounceSpeed = settings.PlayerBounceSpeed;
            playerBounceMaxSpeed = settings.PlayerBounceMaxSpeed;
            playerBounceDecay = settings.PlayerBounceDecay;
            playerBounceCooldownSeconds = settings.PlayerBounceCooldownSeconds;
            playerBounceInputSuppressionSeconds = settings.PlayerBounceInputSuppressionSeconds;

            botDecisionInterval = settings.BotDecisionInterval;
            botTargetRefreshInterval = settings.BotTargetRefreshInterval;
            botSteeringSmoothness = settings.BotSteeringSmoothness;
        }

        private void EnsureEditorParentsExist()
        {
            if (cellParent == null)
            {
                Transform existingBoard = transform.Find("Board");
                if (existingBoard != null)
                {
                    cellParent = existingBoard;
                }
                else
                {
                    GameObject boardObject = new GameObject("Board");
                    Undo.RegisterCreatedObjectUndo(boardObject, "Create StayAlive Board");
                    boardObject.transform.SetParent(transform, false);
                    cellParent = boardObject.transform;
                }
            }

            if (playerParent == null)
            {
                Transform existingPlayers = transform.Find("Players");
                if (existingPlayers != null)
                {
                    playerParent = existingPlayers;
                }
                else
                {
                    GameObject playersObject = new GameObject("Players");
                    Undo.RegisterCreatedObjectUndo(playersObject, "Create StayAlive Players");
                    playersObject.transform.SetParent(transform, false);
                    playerParent = playersObject.transform;
                }
            }

            PositionBoard();
        }

        private void EnsureEditorSpawnPointsExist()
        {
            if (playerParent == null)
            {
                return;
            }

            const int defaultSpawnPointCount = 4;
            if (spawnPoints == null || spawnPoints.Length != defaultSpawnPointCount)
            {
                spawnPoints = new Transform[defaultSpawnPointCount];
            }

            Vector3[] localPositions =
            {
                new Vector3(-4f, 2.5f, 0f),
                new Vector3(4f, 2.5f, 0f),
                new Vector3(-4f, -2.5f, 0f),
                new Vector3(4f, -2.5f, 0f)
            };

            for (int i = 0; i < defaultSpawnPointCount; i++)
            {
                string spawnPointName = $"SpawnPoint_{i}";
                Transform existingSpawnPoint = playerParent.Find(spawnPointName);
                if (existingSpawnPoint == null)
                {
                    GameObject spawnPointObject = new GameObject(spawnPointName);
                    Undo.RegisterCreatedObjectUndo(spawnPointObject, "Create StayAlive Spawn Point");
                    spawnPointObject.transform.SetParent(playerParent, false);
                    existingSpawnPoint = spawnPointObject.transform;
                }

                existingSpawnPoint.localPosition = localPositions[i];
                existingSpawnPoint.localRotation = Quaternion.identity;
                existingSpawnPoint.localScale = Vector3.one;
                spawnPoints[i] = existingSpawnPoint;
            }
        }
#endif
    }
}
