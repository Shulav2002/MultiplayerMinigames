using UnityEngine;

namespace HotDynamite
{
    [CreateAssetMenu(menuName = "HotDynamite/Settings/Bot Settings", fileName = "HotDynamiteBotSettings")]
    public class HotDynamiteBotSettings : ScriptableObject
    {
        [Tooltip("Time between bot decision ticks.")]
        [Min(0.02f)] public float botDecisionInterval = 0.1f;
        [Tooltip("Random movement contribution when bots are not under pressure.")]
        [Min(0f)] public float botWanderStrength = 0.2f;
        [Tooltip("Distance scale used for danger/avoidance behavior from threats and edges.")]
        [Min(0f)] public float botAvoidanceDistance = 1.5f;
        [Tooltip("Strength multiplier applied to avoidance steering forces.")]
        [Min(0f)] public float botAvoidanceStrength = 2f;
        [Tooltip("Chance bomber bots pick a random alive target instead of the closest one.")]
        [Range(0f, 1f)] public float botRandomChaseTargetChance = 0.3f;
        [Tooltip("Min/Max time bomber bots keep the same chase target before retargeting.")]
        public Vector2 botChaseTargetHoldTime = new Vector2(0.35f, 0.9f);
        [Tooltip("Min/Max look-ahead time for intercept prediction while chasing.")]
        public Vector2 botInterceptLookAhead = new Vector2(0.12f, 0.55f);
        [Tooltip("Preferred non-bomber spacing from bomb holder: x=min, y=max.")]
        public Vector2 botPreferredBombDistance = new Vector2(3.2f, 6.3f);
        [Tooltip("How strongly non-bomber bots orbit around the bomb holder while in range.")]
        [Range(0f, 1f)] public float botOrbitBlend = 0.65f;
        [Tooltip("Weight pulling non-bomber bots toward arena center.")]
        [Range(0f, 2f)] public float botCenterBias = 0.9f;
        [Tooltip("Weight pulling non-bomber bots toward ally centroid.")]
        [Range(0f, 2f)] public float botCohesionBias = 0.7f;
        [Tooltip("Weight for tangential edge movement near arena boundaries.")]
        [Range(0f, 3f)] public float botEdgePatrolBias = 1.1f;
        [Tooltip("If measured speed drops below this, bot can enter unstuck logic.")]
        [Min(0f)] public float botStuckSpeedThreshold = 0.12f;
        [Tooltip("Minimum time between forced orbit direction flips for unstuck behavior.")]
        [Min(0f)] public float botStuckDirectionRefreshTime = 0.45f;
        [Tooltip("Steering interpolation sharpness. Higher values react faster.")]
        [Min(0f)] public float botSteeringSmoothness = 12f;
        [Tooltip("Per-decision chance to flip orbit direction.")]
        [Range(0f, 0.1f)] public float botOrbitFlipChance = 0.008f;

#if UNITY_EDITOR
        private void OnValidate()
        {
            HotDynamiteGameSettings.NotifySplitSettingsChanged(this);
        }
#endif
    }
}
