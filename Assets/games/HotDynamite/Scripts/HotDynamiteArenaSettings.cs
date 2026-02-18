using UnityEngine;

namespace HotDynamite
{
    [CreateAssetMenu(menuName = "HotDynamite/Settings/Arena Settings", fileName = "HotDynamiteArenaSettings")]
    public class HotDynamiteArenaSettings : ScriptableObject
    {
        public Vector2 arenaMin = new Vector2(-8f, -4.5f);
        public Vector2 arenaMax = new Vector2(8f, 4.5f);

#if UNITY_EDITOR
        private void OnValidate()
        {
            HotDynamiteGameSettings.NotifySplitSettingsChanged(this);
        }
#endif
    }
}
