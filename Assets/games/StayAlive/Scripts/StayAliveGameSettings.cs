using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace StayAlive
{
    [CreateAssetMenu(menuName = "StayAlive/Game Settings", fileName = "StayAliveGameSettings")]
    public class StayAliveGameSettings : ScriptableObject
    {
        private enum SyncMode
        {
            SplitAssetsOverrideFallback = 0,
            FallbackOverridesSplitAssets = 1
        }

        [Header("Split Settings Assets")]
        [SerializeField] private StayAliveBoardSettings boardSettings;
        [SerializeField] private StayAlivePlayerSettings playerSettings;
        [SerializeField] private StayAliveRoundSettings roundSettings;
        [SerializeField] private StayAliveBotSettings botSettings;

        [Header("Sync")]
        [SerializeField] private bool keepSettingsSynced = true;
        [SerializeField] private SyncMode syncMode = SyncMode.SplitAssetsOverrideFallback;

        [Header("Fallback Values")]
        [Header("Board")]
        [Min(2)] public int columns = 11;
        [Min(2)] public int rows = 8;
        [Min(0.2f)] public float cellSize = 1f;
        [Min(0f)] public float cellGap = 0f;
        public Vector2 boardCenter = Vector2.zero;

        [Header("Players")]
        public bool spawnPlayers = true;
        [Min(1)] public int totalPlayers = 1;
        [Min(1)] public int humanPlayers = 1;
        [Min(0.1f)] public float moveSpeed = 5f;
        [Min(0f)] public float playerBounceSpeed = 4.8f;
        [Min(0f)] public float playerBounceMaxSpeed = 6.5f;
        [Min(0f)] public float playerBounceDecay = 8f;
        [Min(0f)] public float playerBounceCooldownSeconds = 0.16f;
        [Min(0f)] public float playerBounceInputSuppressionSeconds = 0.08f;

        [Header("Round")]
        [Min(0.1f)] public float spinDurationSeconds = 4f;
        [Min(0.02f)] public float symbolSwapIntervalSeconds = 0.04f;
        [Min(0.1f)] public float warningDurationSeconds = 4f;
        [Min(0.1f)] public float explosionDurationSeconds = 0.5f;
        [Min(0.1f)] public float collapseDurationSeconds = 0.5f;
        [Min(0f)] public float refillDelaySeconds = 0.25f;
        [Min(0.1f)] public float refillDurationSeconds = 0.35f;
        [Range(0f, 1f)] public float difficulty01 = 0.5f;
        [Min(1)] public int easyBombCellsPerRound = 16;
        [Min(1)] public int normalBombCellsPerRound = 22;
        [Min(1)] public int hardBombCellsPerRound = 30;
        [Min(0)] public int easyBombIncreasePerSpin = 5;
        [Min(0)] public int normalBombIncreasePerSpin = 7;
        [Min(0)] public int hardBombIncreasePerSpin = 10;
        public bool autoStartSpinOnPlay;

        [Header("Bots")]
        [Min(0.02f)] public float botDecisionInterval = 0.08f;
        [Min(0.05f)] public float botTargetRefreshInterval = 0.25f;
        [Min(0f)] public float botSteeringSmoothness = 10f;

        public int Columns => boardSettings != null ? boardSettings.columns : columns;
        public int Rows => boardSettings != null ? boardSettings.rows : rows;
        public float CellSize => boardSettings != null ? boardSettings.cellSize : cellSize;
        public float CellGap => boardSettings != null ? boardSettings.cellGap : cellGap;
        public Vector2 BoardCenter => boardSettings != null ? boardSettings.boardCenter : boardCenter;

        public bool SpawnPlayers => playerSettings != null ? playerSettings.spawnPlayers : spawnPlayers;
        public int TotalPlayers => playerSettings != null ? playerSettings.totalPlayers : totalPlayers;
        public int HumanPlayers => playerSettings != null ? playerSettings.humanPlayers : humanPlayers;
        public float MoveSpeed => playerSettings != null ? playerSettings.moveSpeed : moveSpeed;
        public float PlayerBounceSpeed => playerSettings != null ? playerSettings.playerBounceSpeed : playerBounceSpeed;
        public float PlayerBounceMaxSpeed => playerSettings != null ? playerSettings.playerBounceMaxSpeed : playerBounceMaxSpeed;
        public float PlayerBounceDecay => playerSettings != null ? playerSettings.playerBounceDecay : playerBounceDecay;
        public float PlayerBounceCooldownSeconds => playerSettings != null ? playerSettings.playerBounceCooldownSeconds : playerBounceCooldownSeconds;
        public float PlayerBounceInputSuppressionSeconds => playerSettings != null ? playerSettings.playerBounceInputSuppressionSeconds : playerBounceInputSuppressionSeconds;

        public float SpinDurationSeconds => roundSettings != null ? roundSettings.spinDurationSeconds : spinDurationSeconds;
        public float SymbolSwapIntervalSeconds => roundSettings != null ? roundSettings.symbolSwapIntervalSeconds : symbolSwapIntervalSeconds;
        public float WarningDurationSeconds => roundSettings != null ? roundSettings.warningDurationSeconds : warningDurationSeconds;
        public float ExplosionDurationSeconds => roundSettings != null ? roundSettings.explosionDurationSeconds : explosionDurationSeconds;
        public float CollapseDurationSeconds => roundSettings != null ? roundSettings.collapseDurationSeconds : collapseDurationSeconds;
        public float RefillDelaySeconds => roundSettings != null ? roundSettings.refillDelaySeconds : refillDelaySeconds;
        public float RefillDurationSeconds => roundSettings != null ? roundSettings.refillDurationSeconds : refillDurationSeconds;
        public float Difficulty01 => roundSettings != null ? roundSettings.difficulty01 : difficulty01;
        public int EasyBombCellsPerRound => roundSettings != null ? roundSettings.easyBombCellsPerRound : easyBombCellsPerRound;
        public int NormalBombCellsPerRound => roundSettings != null ? roundSettings.normalBombCellsPerRound : normalBombCellsPerRound;
        public int HardBombCellsPerRound => roundSettings != null ? roundSettings.hardBombCellsPerRound : hardBombCellsPerRound;
        public int EasyBombIncreasePerSpin => roundSettings != null ? roundSettings.easyBombIncreasePerSpin : easyBombIncreasePerSpin;
        public int NormalBombIncreasePerSpin => roundSettings != null ? roundSettings.normalBombIncreasePerSpin : normalBombIncreasePerSpin;
        public int HardBombIncreasePerSpin => roundSettings != null ? roundSettings.hardBombIncreasePerSpin : hardBombIncreasePerSpin;
        public bool AutoStartSpinOnPlay => roundSettings != null ? roundSettings.autoStartSpinOnPlay : autoStartSpinOnPlay;

        public float BotDecisionInterval => botSettings != null ? botSettings.botDecisionInterval : botDecisionInterval;
        public float BotTargetRefreshInterval => botSettings != null ? botSettings.botTargetRefreshInterval : botTargetRefreshInterval;
        public float BotSteeringSmoothness => botSettings != null ? botSettings.botSteeringSmoothness : botSteeringSmoothness;

        private void OnEnable()
        {
            ClampFallbackValues();
            SyncIfNeeded();
#if UNITY_EDITOR
            StayAliveGameManager.NotifySettingsChanged(this);
#endif
        }

        private void OnValidate()
        {
            ClampFallbackValues();
            SyncIfNeeded();
#if UNITY_EDITOR
            StayAliveGameManager.NotifySettingsChanged(this);
#endif
        }

        [ContextMenu("Sync From Split Assets")]
        private void SyncFromSplitAssetsContextMenu()
        {
            SyncFromSplitAssets();
        }

        [ContextMenu("Sync Fallback To Split Assets")]
        private void SyncFallbackToSplitAssetsContextMenu()
        {
            SyncFallbackToSplitAssets();
        }

        private void ClampFallbackValues()
        {
            columns = Mathf.Max(2, columns);
            rows = Mathf.Max(2, rows);
            cellSize = Mathf.Max(0.2f, cellSize);
            cellGap = Mathf.Max(0f, cellGap);

            totalPlayers = Mathf.Max(1, totalPlayers);
            humanPlayers = Mathf.Clamp(humanPlayers, 1, totalPlayers);
            moveSpeed = Mathf.Max(0.1f, moveSpeed);
            playerBounceSpeed = Mathf.Max(0f, playerBounceSpeed);
            playerBounceMaxSpeed = Mathf.Max(0f, playerBounceMaxSpeed);
            playerBounceDecay = Mathf.Max(0f, playerBounceDecay);
            playerBounceCooldownSeconds = Mathf.Max(0f, playerBounceCooldownSeconds);
            playerBounceInputSuppressionSeconds = Mathf.Max(0f, playerBounceInputSuppressionSeconds);

            spinDurationSeconds = Mathf.Max(0.1f, spinDurationSeconds);
            symbolSwapIntervalSeconds = Mathf.Max(0.02f, symbolSwapIntervalSeconds);
            warningDurationSeconds = Mathf.Max(0.1f, warningDurationSeconds);
            explosionDurationSeconds = Mathf.Max(0.1f, explosionDurationSeconds);
            collapseDurationSeconds = Mathf.Max(0.1f, collapseDurationSeconds);
            refillDelaySeconds = Mathf.Max(0f, refillDelaySeconds);
            refillDurationSeconds = Mathf.Max(0.1f, refillDurationSeconds);
            easyBombCellsPerRound = Mathf.Max(1, easyBombCellsPerRound);
            normalBombCellsPerRound = Mathf.Max(easyBombCellsPerRound, normalBombCellsPerRound);
            hardBombCellsPerRound = Mathf.Max(normalBombCellsPerRound, hardBombCellsPerRound);
            easyBombIncreasePerSpin = Mathf.Max(0, easyBombIncreasePerSpin);
            normalBombIncreasePerSpin = Mathf.Max(easyBombIncreasePerSpin, normalBombIncreasePerSpin);
            hardBombIncreasePerSpin = Mathf.Max(normalBombIncreasePerSpin, hardBombIncreasePerSpin);

            botDecisionInterval = Mathf.Max(0.02f, botDecisionInterval);
            botTargetRefreshInterval = Mathf.Max(0.05f, botTargetRefreshInterval);
            botSteeringSmoothness = Mathf.Max(0f, botSteeringSmoothness);
        }

        private void SyncIfNeeded()
        {
            if (!keepSettingsSynced)
            {
                return;
            }

            if (syncMode == SyncMode.SplitAssetsOverrideFallback)
            {
                SyncFromSplitAssets();
            }
            else
            {
                SyncFallbackToSplitAssets();
            }
        }

        internal void SyncFromSplitAssets()
        {
            bool changed = false;
            changed |= SyncBoardFallbackFromSplit();
            changed |= SyncPlayerFallbackFromSplit();
            changed |= SyncRoundFallbackFromSplit();
            changed |= SyncBotFallbackFromSplit();

#if UNITY_EDITOR
            if (changed)
            {
                EditorUtility.SetDirty(this);
            }
#endif
        }

        internal void SyncFallbackToSplitAssets()
        {
            bool changed = false;
            changed |= SyncBoardSplitFromFallback();
            changed |= SyncPlayerSplitFromFallback();
            changed |= SyncRoundSplitFromFallback();
            changed |= SyncBotSplitFromFallback();

#if UNITY_EDITOR
            if (changed)
            {
                EditorUtility.SetDirty(this);
            }
#endif
        }

        private bool SyncBoardFallbackFromSplit()
        {
            if (boardSettings == null)
            {
                return false;
            }

            bool changed = false;
            changed |= SetIfDifferent(ref columns, boardSettings.columns);
            changed |= SetIfDifferent(ref rows, boardSettings.rows);
            changed |= SetIfDifferent(ref cellSize, boardSettings.cellSize);
            changed |= SetIfDifferent(ref cellGap, boardSettings.cellGap);
            changed |= SetIfDifferent(ref boardCenter, boardSettings.boardCenter);
            return changed;
        }

        private bool SyncPlayerFallbackFromSplit()
        {
            if (playerSettings == null)
            {
                return false;
            }

            bool changed = false;
            changed |= SetIfDifferent(ref spawnPlayers, playerSettings.spawnPlayers);
            changed |= SetIfDifferent(ref totalPlayers, playerSettings.totalPlayers);
            changed |= SetIfDifferent(ref humanPlayers, playerSettings.humanPlayers);
            changed |= SetIfDifferent(ref moveSpeed, playerSettings.moveSpeed);
            changed |= SetIfDifferent(ref playerBounceSpeed, playerSettings.playerBounceSpeed);
            changed |= SetIfDifferent(ref playerBounceMaxSpeed, playerSettings.playerBounceMaxSpeed);
            changed |= SetIfDifferent(ref playerBounceDecay, playerSettings.playerBounceDecay);
            changed |= SetIfDifferent(ref playerBounceCooldownSeconds, playerSettings.playerBounceCooldownSeconds);
            changed |= SetIfDifferent(ref playerBounceInputSuppressionSeconds, playerSettings.playerBounceInputSuppressionSeconds);
            return changed;
        }

        private bool SyncRoundFallbackFromSplit()
        {
            if (roundSettings == null)
            {
                return false;
            }

            bool changed = false;
            changed |= SetIfDifferent(ref spinDurationSeconds, roundSettings.spinDurationSeconds);
            changed |= SetIfDifferent(ref symbolSwapIntervalSeconds, roundSettings.symbolSwapIntervalSeconds);
            changed |= SetIfDifferent(ref warningDurationSeconds, roundSettings.warningDurationSeconds);
            changed |= SetIfDifferent(ref explosionDurationSeconds, roundSettings.explosionDurationSeconds);
            changed |= SetIfDifferent(ref collapseDurationSeconds, roundSettings.collapseDurationSeconds);
            changed |= SetIfDifferent(ref refillDelaySeconds, roundSettings.refillDelaySeconds);
            changed |= SetIfDifferent(ref refillDurationSeconds, roundSettings.refillDurationSeconds);
            changed |= SetIfDifferent(ref difficulty01, roundSettings.difficulty01);
            changed |= SetIfDifferent(ref easyBombCellsPerRound, roundSettings.easyBombCellsPerRound);
            changed |= SetIfDifferent(ref normalBombCellsPerRound, roundSettings.normalBombCellsPerRound);
            changed |= SetIfDifferent(ref hardBombCellsPerRound, roundSettings.hardBombCellsPerRound);
            changed |= SetIfDifferent(ref easyBombIncreasePerSpin, roundSettings.easyBombIncreasePerSpin);
            changed |= SetIfDifferent(ref normalBombIncreasePerSpin, roundSettings.normalBombIncreasePerSpin);
            changed |= SetIfDifferent(ref hardBombIncreasePerSpin, roundSettings.hardBombIncreasePerSpin);
            changed |= SetIfDifferent(ref autoStartSpinOnPlay, roundSettings.autoStartSpinOnPlay);
            return changed;
        }

        private bool SyncBotFallbackFromSplit()
        {
            if (botSettings == null)
            {
                return false;
            }

            bool changed = false;
            changed |= SetIfDifferent(ref botDecisionInterval, botSettings.botDecisionInterval);
            changed |= SetIfDifferent(ref botTargetRefreshInterval, botSettings.botTargetRefreshInterval);
            changed |= SetIfDifferent(ref botSteeringSmoothness, botSettings.botSteeringSmoothness);
            return changed;
        }

        private bool SyncBoardSplitFromFallback()
        {
            if (boardSettings == null)
            {
                return false;
            }

            bool changed = false;
            changed |= SetIfDifferent(ref boardSettings.columns, columns);
            changed |= SetIfDifferent(ref boardSettings.rows, rows);
            changed |= SetIfDifferent(ref boardSettings.cellSize, cellSize);
            changed |= SetIfDifferent(ref boardSettings.cellGap, cellGap);
            changed |= SetIfDifferent(ref boardSettings.boardCenter, boardCenter);
#if UNITY_EDITOR
            if (changed) { EditorUtility.SetDirty(boardSettings); }
#endif
            return changed;
        }

        private bool SyncPlayerSplitFromFallback()
        {
            if (playerSettings == null)
            {
                return false;
            }

            bool changed = false;
            changed |= SetIfDifferent(ref playerSettings.spawnPlayers, spawnPlayers);
            changed |= SetIfDifferent(ref playerSettings.totalPlayers, totalPlayers);
            changed |= SetIfDifferent(ref playerSettings.humanPlayers, humanPlayers);
            changed |= SetIfDifferent(ref playerSettings.moveSpeed, moveSpeed);
            changed |= SetIfDifferent(ref playerSettings.playerBounceSpeed, playerBounceSpeed);
            changed |= SetIfDifferent(ref playerSettings.playerBounceMaxSpeed, playerBounceMaxSpeed);
            changed |= SetIfDifferent(ref playerSettings.playerBounceDecay, playerBounceDecay);
            changed |= SetIfDifferent(ref playerSettings.playerBounceCooldownSeconds, playerBounceCooldownSeconds);
            changed |= SetIfDifferent(ref playerSettings.playerBounceInputSuppressionSeconds, playerBounceInputSuppressionSeconds);
#if UNITY_EDITOR
            if (changed) { EditorUtility.SetDirty(playerSettings); }
#endif
            return changed;
        }

        private bool SyncRoundSplitFromFallback()
        {
            if (roundSettings == null)
            {
                return false;
            }

            bool changed = false;
            changed |= SetIfDifferent(ref roundSettings.spinDurationSeconds, spinDurationSeconds);
            changed |= SetIfDifferent(ref roundSettings.symbolSwapIntervalSeconds, symbolSwapIntervalSeconds);
            changed |= SetIfDifferent(ref roundSettings.warningDurationSeconds, warningDurationSeconds);
            changed |= SetIfDifferent(ref roundSettings.explosionDurationSeconds, explosionDurationSeconds);
            changed |= SetIfDifferent(ref roundSettings.collapseDurationSeconds, collapseDurationSeconds);
            changed |= SetIfDifferent(ref roundSettings.refillDelaySeconds, refillDelaySeconds);
            changed |= SetIfDifferent(ref roundSettings.refillDurationSeconds, refillDurationSeconds);
            changed |= SetIfDifferent(ref roundSettings.difficulty01, difficulty01);
            changed |= SetIfDifferent(ref roundSettings.easyBombCellsPerRound, easyBombCellsPerRound);
            changed |= SetIfDifferent(ref roundSettings.normalBombCellsPerRound, normalBombCellsPerRound);
            changed |= SetIfDifferent(ref roundSettings.hardBombCellsPerRound, hardBombCellsPerRound);
            changed |= SetIfDifferent(ref roundSettings.easyBombIncreasePerSpin, easyBombIncreasePerSpin);
            changed |= SetIfDifferent(ref roundSettings.normalBombIncreasePerSpin, normalBombIncreasePerSpin);
            changed |= SetIfDifferent(ref roundSettings.hardBombIncreasePerSpin, hardBombIncreasePerSpin);
            changed |= SetIfDifferent(ref roundSettings.autoStartSpinOnPlay, autoStartSpinOnPlay);
#if UNITY_EDITOR
            if (changed) { EditorUtility.SetDirty(roundSettings); }
#endif
            return changed;
        }

        private bool SyncBotSplitFromFallback()
        {
            if (botSettings == null)
            {
                return false;
            }

            bool changed = false;
            changed |= SetIfDifferent(ref botSettings.botDecisionInterval, botDecisionInterval);
            changed |= SetIfDifferent(ref botSettings.botTargetRefreshInterval, botTargetRefreshInterval);
            changed |= SetIfDifferent(ref botSettings.botSteeringSmoothness, botSteeringSmoothness);
#if UNITY_EDITOR
            if (changed) { EditorUtility.SetDirty(botSettings); }
#endif
            return changed;
        }

        private static bool SetIfDifferent(ref bool target, bool value)
        {
            if (target == value)
            {
                return false;
            }

            target = value;
            return true;
        }

        private static bool SetIfDifferent(ref int target, int value)
        {
            if (target == value)
            {
                return false;
            }

            target = value;
            return true;
        }

        private static bool SetIfDifferent(ref float target, float value)
        {
            if (Mathf.Approximately(target, value))
            {
                return false;
            }

            target = value;
            return true;
        }

        private static bool SetIfDifferent(ref Vector2 target, Vector2 value)
        {
            if (target == value)
            {
                return false;
            }

            target = value;
            return true;
        }

#if UNITY_EDITOR
        internal static void NotifySplitSettingsChanged(Object changedAsset)
        {
            if (changedAsset == null)
            {
                return;
            }

            string[] settingGuids = AssetDatabase.FindAssets("t:StayAliveGameSettings");
            for (int i = 0; i < settingGuids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(settingGuids[i]);
                StayAliveGameSettings settingsAsset = AssetDatabase.LoadAssetAtPath<StayAliveGameSettings>(path);
                if (settingsAsset == null || !settingsAsset.keepSettingsSynced)
                {
                    continue;
                }

                bool referencesChangedAsset =
                    settingsAsset.boardSettings == changedAsset ||
                    settingsAsset.playerSettings == changedAsset ||
                    settingsAsset.roundSettings == changedAsset ||
                    settingsAsset.botSettings == changedAsset;

                if (!referencesChangedAsset)
                {
                    continue;
                }

                if (settingsAsset.syncMode == SyncMode.SplitAssetsOverrideFallback)
                {
                    settingsAsset.SyncFromSplitAssets();
                }
                else
                {
                    settingsAsset.SyncFallbackToSplitAssets();
                }

                StayAliveGameManager.NotifySettingsChanged(settingsAsset);
            }
        }
#endif
    }
}
