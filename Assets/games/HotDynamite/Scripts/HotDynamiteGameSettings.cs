using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace HotDynamite
{
    [CreateAssetMenu(menuName = "HotDynamite/Game Settings", fileName = "HotDynamiteGameSettings")]
    public class HotDynamiteGameSettings : ScriptableObject
    {
        private enum SyncMode
        {
            SplitAssetsOverrideFallback = 0,
            FallbackOverridesSplitAssets = 1
        }

        [Header("Split Settings Assets")]
        [SerializeField] private HotDynamitePlayerSettings playerSettings;
        [SerializeField] private HotDynamiteBombSettings bombSettings;
        [SerializeField] private HotDynamiteArenaSettings arenaSettings;
        [SerializeField] private HotDynamiteBotSettings botSettings;
        [Header("Sync")]
        [SerializeField] private bool keepSettingsSynced = true;
        [SerializeField] private SyncMode syncMode = SyncMode.SplitAssetsOverrideFallback;

        [Header("Fallback Values (used if split asset is missing)")]
        [Header("Players")]
        [Min(2)] public int totalPlayers = 4;
        [Min(1)] public int humanPlayers = 1;
        [Min(0.1f)] public float moveSpeed = 5f;
        [Min(0f)] public float bombHolderSpeedMultiplier = 1.1f;

        [Header("Bomb")]
        [Min(0.1f)] public float bombTimerSeconds = 10f;
        [Min(0f)] public float passCooldownSeconds = 0.5f;
        [Min(0f)] public float bombPassSpeedEffectSeconds = 1.5f;
        [Min(0.01f)] public float bombPasserSpeedMultiplier = 1.15f;
        [Min(0.01f)] public float bombReceiverSpeedMultiplier = 0.85f;

        [Header("Arena")]
        public Vector2 arenaMin = new Vector2(-8f, -4.5f);
        public Vector2 arenaMax = new Vector2(8f, 4.5f);

        [Header("Bots")]
        [Min(0.02f)] public float botDecisionInterval = 0.1f;
        [Min(0f)] public float botWanderStrength = 0.2f;
        [Min(0f)] public float botAvoidanceDistance = 1.5f;
        [Min(0f)] public float botAvoidanceStrength = 2f;

        public int TotalPlayers => playerSettings != null ? playerSettings.totalPlayers : totalPlayers;
        public int HumanPlayers => playerSettings != null ? playerSettings.humanPlayers : humanPlayers;
        public float MoveSpeed => playerSettings != null ? playerSettings.moveSpeed : moveSpeed;
        public float BombHolderSpeedMultiplier => playerSettings != null ? playerSettings.bombHolderSpeedMultiplier : bombHolderSpeedMultiplier;

        public float BombTimerSeconds => bombSettings != null ? bombSettings.bombTimerSeconds : bombTimerSeconds;
        public float PassCooldownSeconds => bombSettings != null ? bombSettings.passCooldownSeconds : passCooldownSeconds;
        public float BombPassSpeedEffectSeconds => bombSettings != null ? bombSettings.bombPassSpeedEffectSeconds : bombPassSpeedEffectSeconds;
        public float BombPasserSpeedMultiplier => bombSettings != null ? bombSettings.bombPasserSpeedMultiplier : bombPasserSpeedMultiplier;
        public float BombReceiverSpeedMultiplier => bombSettings != null ? bombSettings.bombReceiverSpeedMultiplier : bombReceiverSpeedMultiplier;

        public Vector2 ArenaMin => arenaSettings != null ? arenaSettings.arenaMin : arenaMin;
        public Vector2 ArenaMax => arenaSettings != null ? arenaSettings.arenaMax : arenaMax;

        public float BotDecisionInterval => botSettings != null ? botSettings.botDecisionInterval : botDecisionInterval;
        public float BotWanderStrength => botSettings != null ? botSettings.botWanderStrength : botWanderStrength;
        public float BotAvoidanceDistance => botSettings != null ? botSettings.botAvoidanceDistance : botAvoidanceDistance;
        public float BotAvoidanceStrength => botSettings != null ? botSettings.botAvoidanceStrength : botAvoidanceStrength;

        public int ClampedTotalPlayers => Mathf.Max(2, TotalPlayers);
        public int ClampedHumanPlayers => Mathf.Clamp(HumanPlayers, 1, ClampedTotalPlayers);

        private void OnEnable()
        {
            SyncIfNeeded();
        }

        private void OnValidate()
        {
            SyncIfNeeded();
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

            if (playerSettings != null)
            {
                changed |= SetIfDifferent(ref totalPlayers, playerSettings.totalPlayers);
                changed |= SetIfDifferent(ref humanPlayers, playerSettings.humanPlayers);
                changed |= SetIfDifferent(ref moveSpeed, playerSettings.moveSpeed);
                changed |= SetIfDifferent(ref bombHolderSpeedMultiplier, playerSettings.bombHolderSpeedMultiplier);
            }

            if (bombSettings != null)
            {
                changed |= SetIfDifferent(ref bombTimerSeconds, bombSettings.bombTimerSeconds);
                changed |= SetIfDifferent(ref passCooldownSeconds, bombSettings.passCooldownSeconds);
                changed |= SetIfDifferent(ref bombPassSpeedEffectSeconds, bombSettings.bombPassSpeedEffectSeconds);
                changed |= SetIfDifferent(ref bombPasserSpeedMultiplier, bombSettings.bombPasserSpeedMultiplier);
                changed |= SetIfDifferent(ref bombReceiverSpeedMultiplier, bombSettings.bombReceiverSpeedMultiplier);
            }

            if (arenaSettings != null)
            {
                changed |= SetIfDifferent(ref arenaMin, arenaSettings.arenaMin);
                changed |= SetIfDifferent(ref arenaMax, arenaSettings.arenaMax);
            }

            if (botSettings != null)
            {
                changed |= SetIfDifferent(ref botDecisionInterval, botSettings.botDecisionInterval);
                changed |= SetIfDifferent(ref botWanderStrength, botSettings.botWanderStrength);
                changed |= SetIfDifferent(ref botAvoidanceDistance, botSettings.botAvoidanceDistance);
                changed |= SetIfDifferent(ref botAvoidanceStrength, botSettings.botAvoidanceStrength);
            }

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

            if (playerSettings != null)
            {
                changed |= SetIfDifferent(ref playerSettings.totalPlayers, totalPlayers);
                changed |= SetIfDifferent(ref playerSettings.humanPlayers, humanPlayers);
                changed |= SetIfDifferent(ref playerSettings.moveSpeed, moveSpeed);
                changed |= SetIfDifferent(ref playerSettings.bombHolderSpeedMultiplier, bombHolderSpeedMultiplier);
#if UNITY_EDITOR
                if (changed) { EditorUtility.SetDirty(playerSettings); }
#endif
            }

            if (bombSettings != null)
            {
                bool localChanged = false;
                localChanged |= SetIfDifferent(ref bombSettings.bombTimerSeconds, bombTimerSeconds);
                localChanged |= SetIfDifferent(ref bombSettings.passCooldownSeconds, passCooldownSeconds);
                localChanged |= SetIfDifferent(ref bombSettings.bombPassSpeedEffectSeconds, bombPassSpeedEffectSeconds);
                localChanged |= SetIfDifferent(ref bombSettings.bombPasserSpeedMultiplier, bombPasserSpeedMultiplier);
                localChanged |= SetIfDifferent(ref bombSettings.bombReceiverSpeedMultiplier, bombReceiverSpeedMultiplier);
                changed |= localChanged;
#if UNITY_EDITOR
                if (localChanged) { EditorUtility.SetDirty(bombSettings); }
#endif
            }

            if (arenaSettings != null)
            {
                bool localChanged = false;
                localChanged |= SetIfDifferent(ref arenaSettings.arenaMin, arenaMin);
                localChanged |= SetIfDifferent(ref arenaSettings.arenaMax, arenaMax);
                changed |= localChanged;
#if UNITY_EDITOR
                if (localChanged) { EditorUtility.SetDirty(arenaSettings); }
#endif
            }

            if (botSettings != null)
            {
                bool localChanged = false;
                localChanged |= SetIfDifferent(ref botSettings.botDecisionInterval, botDecisionInterval);
                localChanged |= SetIfDifferent(ref botSettings.botWanderStrength, botWanderStrength);
                localChanged |= SetIfDifferent(ref botSettings.botAvoidanceDistance, botAvoidanceDistance);
                localChanged |= SetIfDifferent(ref botSettings.botAvoidanceStrength, botAvoidanceStrength);
                changed |= localChanged;
#if UNITY_EDITOR
                if (localChanged) { EditorUtility.SetDirty(botSettings); }
#endif
            }

#if UNITY_EDITOR
            if (changed)
            {
                EditorUtility.SetDirty(this);
            }
#endif
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

            string[] settingGuids = AssetDatabase.FindAssets("t:HotDynamiteGameSettings");
            for (int i = 0; i < settingGuids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(settingGuids[i]);
                HotDynamiteGameSettings settingsAsset = AssetDatabase.LoadAssetAtPath<HotDynamiteGameSettings>(path);
                if (settingsAsset == null || !settingsAsset.keepSettingsSynced)
                {
                    continue;
                }

                bool referencesChangedAsset =
                    settingsAsset.playerSettings == changedAsset ||
                    settingsAsset.bombSettings == changedAsset ||
                    settingsAsset.arenaSettings == changedAsset ||
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
            }
        }
#endif
    }
}
