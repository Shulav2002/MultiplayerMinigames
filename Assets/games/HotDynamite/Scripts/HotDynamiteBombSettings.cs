using UnityEngine;

namespace HotDynamite
{
    [CreateAssetMenu(menuName = "HotDynamite/Settings/Bomb Settings", fileName = "HotDynamiteBombSettings")]
    public class HotDynamiteBombSettings : ScriptableObject
    {
        [Tooltip("Bomb fuse duration in seconds for each round.")]
        [Min(0.1f)] public float bombTimerSeconds = 10f;
        [Tooltip("Minimum delay between bomb pass events.")]
        [Min(0f)] public float passCooldownSeconds = 0.5f;
        [Tooltip("Duration of temporary speed effects after a bomb transfer.")]
        [Min(0f)] public float bombPassSpeedEffectSeconds = 1.5f;
        [Tooltip("Temporary speed multiplier for the player who passed the bomb.")]
        [Min(0.01f)] public float bombPasserSpeedMultiplier = 1.15f;
        [Tooltip("Temporary speed multiplier for the player who received the bomb.")]
        [Min(0.01f)] public float bombReceiverSpeedMultiplier = 0.85f;

#if UNITY_EDITOR
        private void OnValidate()
        {
            HotDynamiteGameSettings.NotifySplitSettingsChanged(this);
        }
#endif
    }
}
