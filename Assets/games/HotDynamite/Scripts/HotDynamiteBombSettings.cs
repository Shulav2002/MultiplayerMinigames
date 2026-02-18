using UnityEngine;

namespace HotDynamite
{
    [CreateAssetMenu(menuName = "HotDynamite/Settings/Bomb Settings", fileName = "HotDynamiteBombSettings")]
    public class HotDynamiteBombSettings : ScriptableObject
    {
        [Min(0.1f)] public float bombTimerSeconds = 10f;
        [Min(0f)] public float passCooldownSeconds = 0.5f;
        [Min(0f)] public float bombPassSpeedEffectSeconds = 1.5f;
        [Min(0.01f)] public float bombPasserSpeedMultiplier = 1.15f;
        [Min(0.01f)] public float bombReceiverSpeedMultiplier = 0.85f;

#if UNITY_EDITOR
        private void OnValidate()
        {
            HotDynamiteGameSettings.NotifySplitSettingsChanged(this);
        }
#endif
    }
}
