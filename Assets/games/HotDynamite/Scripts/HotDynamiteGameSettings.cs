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
        [Tooltip("Optional player settings asset. If assigned, its values override player fallback values.")]
        [SerializeField] private HotDynamitePlayerSettings playerSettings;
        [Tooltip("Optional bomb settings asset. If assigned, its values override bomb fallback values.")]
        [SerializeField] private HotDynamiteBombSettings bombSettings;
        [Tooltip("Optional arena settings asset. If assigned, its values override arena fallback values.")]
        [SerializeField] private HotDynamiteArenaSettings arenaSettings;
        [Tooltip("Optional bot settings asset. If assigned, its values override bot fallback values.")]
        [SerializeField] private HotDynamiteBotSettings botSettings;
        [Header("Sync")]
        [Tooltip("When enabled, fallback values and split assets are kept synchronized according to Sync Mode.")]
        [SerializeField] private bool keepSettingsSynced = true;
        [Tooltip("Controls sync direction: split assets -> fallback, or fallback -> split assets.")]
        [SerializeField] private SyncMode syncMode = SyncMode.SplitAssetsOverrideFallback;

        [Header("Fallback Values (used if split asset is missing)")]
        [Header("Players")]
        [Tooltip("Fallback: total players spawned (humans + bots).")]
        [Min(2)] public int totalPlayers = 4;
        [Tooltip("Fallback: number of human-controlled players.")]
        [Min(1)] public int humanPlayers = 1;
        [Tooltip("Fallback: base movement speed.")]
        [Min(0.1f)] public float moveSpeed = 5f;
        [Tooltip("Fallback: speed multiplier while holding the bomb.")]
        [Min(0f)] public float bombHolderSpeedMultiplier = 1.1f;
        [Tooltip("Fallback: bounce impulse added on player collision.")]
        [Min(0f)] public float playerBounceSpeed = 4.8f;
        [Tooltip("Fallback: maximum bounce velocity magnitude.")]
        [Min(0f)] public float playerBounceMaxSpeed = 6.5f;
        [Tooltip("Fallback: bounce velocity decay rate.")]
        [Min(0f)] public float playerBounceDecay = 8f;
        [Tooltip("Fallback: minimum delay between bounce responses.")]
        [Min(0f)] public float playerBounceCooldownSeconds = 0.16f;
        [Tooltip("Fallback: input suppression duration after bounce.")]
        [Min(0f)] public float playerBounceInputSuppressionSeconds = 0.08f;

        [Header("Bomb")]
        [Tooltip("Fallback: bomb fuse duration in seconds.")]
        [Min(0.1f)] public float bombTimerSeconds = 10f;
        [Tooltip("Fallback: minimum delay between bomb pass events.")]
        [Min(0f)] public float passCooldownSeconds = 0.5f;
        [Tooltip("Fallback: duration of temporary post-pass speed effects.")]
        [Min(0f)] public float bombPassSpeedEffectSeconds = 1.5f;
        [Tooltip("Fallback: temporary speed multiplier for the passer.")]
        [Min(0.01f)] public float bombPasserSpeedMultiplier = 1.15f;
        [Tooltip("Fallback: temporary speed multiplier for the receiver.")]
        [Min(0.01f)] public float bombReceiverSpeedMultiplier = 0.85f;

        [Header("Arena")]
        [Tooltip("Fallback: lower-left arena bound (min X, min Y).")]
        public Vector2 arenaMin = new Vector2(-8f, -4.5f);
        [Tooltip("Fallback: upper-right arena bound (max X, max Y).")]
        public Vector2 arenaMax = new Vector2(8f, 4.5f);

        [Header("Bots")]
        [Tooltip("Fallback: time between bot decision ticks.")]
        [Min(0.02f)] public float botDecisionInterval = 0.1f;
        [Tooltip("Fallback: random movement contribution for bots.")]
        [Min(0f)] public float botWanderStrength = 0.2f;
        [Tooltip("Fallback: distance scale for avoidance behavior.")]
        [Min(0f)] public float botAvoidanceDistance = 1.5f;
        [Tooltip("Fallback: strength multiplier for avoidance steering.")]
        [Min(0f)] public float botAvoidanceStrength = 2f;
        [Tooltip("Fallback: chance bomber bots pick a random target.")]
        [Range(0f, 1f)] public float botRandomChaseTargetChance = 0.3f;
        [Tooltip("Fallback: min/max time to keep current chase target.")]
        public Vector2 botChaseTargetHoldTime = new Vector2(0.35f, 0.9f);
        [Tooltip("Fallback: min/max intercept prediction look-ahead.")]
        public Vector2 botInterceptLookAhead = new Vector2(0.12f, 0.55f);
        [Tooltip("Fallback: preferred non-bomber distance band from bomb holder.")]
        public Vector2 botPreferredBombDistance = new Vector2(3.2f, 6.3f);
        [Tooltip("Fallback: orbit tendency while in preferred distance band.")]
        [Range(0f, 1f)] public float botOrbitBlend = 0.65f;
        [Tooltip("Fallback: pull weight toward arena center.")]
        [Range(0f, 2f)] public float botCenterBias = 0.9f;
        [Tooltip("Fallback: pull weight toward non-bomber teammate centroid.")]
        [Range(0f, 2f)] public float botCohesionBias = 0.7f;
        [Tooltip("Fallback: edge tangential movement weight near boundaries.")]
        [Range(0f, 3f)] public float botEdgePatrolBias = 1.1f;
        [Tooltip("Fallback: speed threshold used to detect stuck movement.")]
        [Min(0f)] public float botStuckSpeedThreshold = 0.12f;
        [Tooltip("Fallback: minimum time between unstuck direction flips.")]
        [Min(0f)] public float botStuckDirectionRefreshTime = 0.45f;
        [Tooltip("Fallback: steering interpolation sharpness.")]
        [Min(0f)] public float botSteeringSmoothness = 12f;
        [Tooltip("Fallback: per-decision chance to flip orbit direction.")]
        [Range(0f, 0.1f)] public float botOrbitFlipChance = 0.008f;

        public int TotalPlayers => playerSettings != null ? playerSettings.totalPlayers : totalPlayers;
        public int HumanPlayers => playerSettings != null ? playerSettings.humanPlayers : humanPlayers;
        public float MoveSpeed => playerSettings != null ? playerSettings.moveSpeed : moveSpeed;
        public float BombHolderSpeedMultiplier => playerSettings != null ? playerSettings.bombHolderSpeedMultiplier : bombHolderSpeedMultiplier;
        public float PlayerBounceSpeed => playerSettings != null ? playerSettings.playerBounceSpeed : playerBounceSpeed;
        public float PlayerBounceMaxSpeed => playerSettings != null ? playerSettings.playerBounceMaxSpeed : playerBounceMaxSpeed;
        public float PlayerBounceDecay => playerSettings != null ? playerSettings.playerBounceDecay : playerBounceDecay;
        public float PlayerBounceCooldownSeconds => playerSettings != null ? playerSettings.playerBounceCooldownSeconds : playerBounceCooldownSeconds;
        public float PlayerBounceInputSuppressionSeconds => playerSettings != null ? playerSettings.playerBounceInputSuppressionSeconds : playerBounceInputSuppressionSeconds;

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
        public float BotRandomChaseTargetChance => botSettings != null ? botSettings.botRandomChaseTargetChance : botRandomChaseTargetChance;
        public Vector2 BotChaseTargetHoldTime => botSettings != null ? botSettings.botChaseTargetHoldTime : botChaseTargetHoldTime;
        public Vector2 BotInterceptLookAhead => botSettings != null ? botSettings.botInterceptLookAhead : botInterceptLookAhead;
        public Vector2 BotPreferredBombDistance => botSettings != null ? botSettings.botPreferredBombDistance : botPreferredBombDistance;
        public float BotOrbitBlend => botSettings != null ? botSettings.botOrbitBlend : botOrbitBlend;
        public float BotCenterBias => botSettings != null ? botSettings.botCenterBias : botCenterBias;
        public float BotCohesionBias => botSettings != null ? botSettings.botCohesionBias : botCohesionBias;
        public float BotEdgePatrolBias => botSettings != null ? botSettings.botEdgePatrolBias : botEdgePatrolBias;
        public float BotStuckSpeedThreshold => botSettings != null ? botSettings.botStuckSpeedThreshold : botStuckSpeedThreshold;
        public float BotStuckDirectionRefreshTime => botSettings != null ? botSettings.botStuckDirectionRefreshTime : botStuckDirectionRefreshTime;
        public float BotSteeringSmoothness => botSettings != null ? botSettings.botSteeringSmoothness : botSteeringSmoothness;
        public float BotOrbitFlipChance => botSettings != null ? botSettings.botOrbitFlipChance : botOrbitFlipChance;

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
                changed |= SetIfDifferent(ref playerBounceSpeed, playerSettings.playerBounceSpeed);
                changed |= SetIfDifferent(ref playerBounceMaxSpeed, playerSettings.playerBounceMaxSpeed);
                changed |= SetIfDifferent(ref playerBounceDecay, playerSettings.playerBounceDecay);
                changed |= SetIfDifferent(ref playerBounceCooldownSeconds, playerSettings.playerBounceCooldownSeconds);
                changed |= SetIfDifferent(ref playerBounceInputSuppressionSeconds, playerSettings.playerBounceInputSuppressionSeconds);
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
                changed |= SetIfDifferent(ref botRandomChaseTargetChance, botSettings.botRandomChaseTargetChance);
                changed |= SetIfDifferent(ref botChaseTargetHoldTime, botSettings.botChaseTargetHoldTime);
                changed |= SetIfDifferent(ref botInterceptLookAhead, botSettings.botInterceptLookAhead);
                changed |= SetIfDifferent(ref botPreferredBombDistance, botSettings.botPreferredBombDistance);
                changed |= SetIfDifferent(ref botOrbitBlend, botSettings.botOrbitBlend);
                changed |= SetIfDifferent(ref botCenterBias, botSettings.botCenterBias);
                changed |= SetIfDifferent(ref botCohesionBias, botSettings.botCohesionBias);
                changed |= SetIfDifferent(ref botEdgePatrolBias, botSettings.botEdgePatrolBias);
                changed |= SetIfDifferent(ref botStuckSpeedThreshold, botSettings.botStuckSpeedThreshold);
                changed |= SetIfDifferent(ref botStuckDirectionRefreshTime, botSettings.botStuckDirectionRefreshTime);
                changed |= SetIfDifferent(ref botSteeringSmoothness, botSettings.botSteeringSmoothness);
                changed |= SetIfDifferent(ref botOrbitFlipChance, botSettings.botOrbitFlipChance);
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
                bool localChanged = false;
                localChanged |= SetIfDifferent(ref playerSettings.totalPlayers, totalPlayers);
                localChanged |= SetIfDifferent(ref playerSettings.humanPlayers, humanPlayers);
                localChanged |= SetIfDifferent(ref playerSettings.moveSpeed, moveSpeed);
                localChanged |= SetIfDifferent(ref playerSettings.bombHolderSpeedMultiplier, bombHolderSpeedMultiplier);
                localChanged |= SetIfDifferent(ref playerSettings.playerBounceSpeed, playerBounceSpeed);
                localChanged |= SetIfDifferent(ref playerSettings.playerBounceMaxSpeed, playerBounceMaxSpeed);
                localChanged |= SetIfDifferent(ref playerSettings.playerBounceDecay, playerBounceDecay);
                localChanged |= SetIfDifferent(ref playerSettings.playerBounceCooldownSeconds, playerBounceCooldownSeconds);
                localChanged |= SetIfDifferent(ref playerSettings.playerBounceInputSuppressionSeconds, playerBounceInputSuppressionSeconds);
                changed |= localChanged;
#if UNITY_EDITOR
                if (localChanged) { EditorUtility.SetDirty(playerSettings); }
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
                localChanged |= SetIfDifferent(ref botSettings.botRandomChaseTargetChance, botRandomChaseTargetChance);
                localChanged |= SetIfDifferent(ref botSettings.botChaseTargetHoldTime, botChaseTargetHoldTime);
                localChanged |= SetIfDifferent(ref botSettings.botInterceptLookAhead, botInterceptLookAhead);
                localChanged |= SetIfDifferent(ref botSettings.botPreferredBombDistance, botPreferredBombDistance);
                localChanged |= SetIfDifferent(ref botSettings.botOrbitBlend, botOrbitBlend);
                localChanged |= SetIfDifferent(ref botSettings.botCenterBias, botCenterBias);
                localChanged |= SetIfDifferent(ref botSettings.botCohesionBias, botCohesionBias);
                localChanged |= SetIfDifferent(ref botSettings.botEdgePatrolBias, botEdgePatrolBias);
                localChanged |= SetIfDifferent(ref botSettings.botStuckSpeedThreshold, botStuckSpeedThreshold);
                localChanged |= SetIfDifferent(ref botSettings.botStuckDirectionRefreshTime, botStuckDirectionRefreshTime);
                localChanged |= SetIfDifferent(ref botSettings.botSteeringSmoothness, botSteeringSmoothness);
                localChanged |= SetIfDifferent(ref botSettings.botOrbitFlipChance, botOrbitFlipChance);
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
