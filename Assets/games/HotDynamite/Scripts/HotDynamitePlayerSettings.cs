using UnityEngine;

namespace HotDynamite
{
    [CreateAssetMenu(menuName = "HotDynamite/Settings/Player Settings", fileName = "HotDynamitePlayerSettings")]
    public class HotDynamitePlayerSettings : ScriptableObject
    {
        [Min(2)] public int totalPlayers = 4;
        [Min(1)] public int humanPlayers = 1;
        [Min(0.1f)] public float moveSpeed = 5f;
        [Min(0f)] public float bombHolderSpeedMultiplier = 1.1f;

        public int ClampedTotalPlayers => Mathf.Max(2, totalPlayers);
        public int ClampedHumanPlayers => Mathf.Clamp(humanPlayers, 1, ClampedTotalPlayers);

#if UNITY_EDITOR
        private void OnValidate()
        {
            HotDynamiteGameSettings.NotifySplitSettingsChanged(this);
        }
#endif
    }
}
