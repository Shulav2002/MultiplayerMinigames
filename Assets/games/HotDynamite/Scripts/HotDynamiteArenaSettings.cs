using UnityEngine;

namespace HotDynamite
{
    [CreateAssetMenu(menuName = "HotDynamite/Settings/Arena Settings", fileName = "HotDynamiteArenaSettings")]
    public class HotDynamiteArenaSettings : ScriptableObject
    {
        [Tooltip("Lower-left arena bound in world space (minimum X and minimum Y).")]
        public Vector2 arenaMin = new Vector2(-8f, -4.5f);
        [Tooltip("Upper-right arena bound in world space (maximum X and maximum Y).")]
        public Vector2 arenaMax = new Vector2(8f, 4.5f);

#if UNITY_EDITOR
        private void OnValidate()
        {
            HotDynamiteGameSettings.NotifySplitSettingsChanged(this);
        }
#endif
    }
}
