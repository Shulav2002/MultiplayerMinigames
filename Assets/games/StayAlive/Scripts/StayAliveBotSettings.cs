using UnityEngine;

namespace StayAlive
{
    [CreateAssetMenu(menuName = "StayAlive/Settings/Bot Settings", fileName = "StayAliveBotSettings")]
    public class StayAliveBotSettings : ScriptableObject
    {
        [Min(0.02f)] public float botDecisionInterval = 0.08f;
        [Min(0.05f)] public float botTargetRefreshInterval = 0.25f;
        [Min(0f)] public float botSteeringSmoothness = 10f;

#if UNITY_EDITOR
        private void OnValidate()
        {
            botDecisionInterval = Mathf.Max(0.02f, botDecisionInterval);
            botTargetRefreshInterval = Mathf.Max(0.05f, botTargetRefreshInterval);
            botSteeringSmoothness = Mathf.Max(0f, botSteeringSmoothness);
            StayAliveGameSettings.NotifySplitSettingsChanged(this);
        }
#endif
    }
}
