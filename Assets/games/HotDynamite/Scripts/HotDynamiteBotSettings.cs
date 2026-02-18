using UnityEngine;

namespace HotDynamite
{
    [CreateAssetMenu(menuName = "HotDynamite/Settings/Bot Settings", fileName = "HotDynamiteBotSettings")]
    public class HotDynamiteBotSettings : ScriptableObject
    {
        [Min(0.02f)] public float botDecisionInterval = 0.1f;
        [Min(0f)] public float botWanderStrength = 0.2f;
        [Min(0f)] public float botAvoidanceDistance = 1.5f;
        [Min(0f)] public float botAvoidanceStrength = 2f;

#if UNITY_EDITOR
        private void OnValidate()
        {
            HotDynamiteGameSettings.NotifySplitSettingsChanged(this);
        }
#endif
    }
}
