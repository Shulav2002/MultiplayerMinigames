using UnityEngine;

namespace StayAlive
{
    [CreateAssetMenu(menuName = "StayAlive/Settings/Board Settings", fileName = "StayAliveBoardSettings")]
    public class StayAliveBoardSettings : ScriptableObject
    {
        [Min(2)] public int columns = 11;
        [Min(2)] public int rows = 8;
        [Min(0.2f)] public float cellSize = 1f;
        [Min(0f)] public float cellGap = 0f;
        public Vector2 boardCenter = Vector2.zero;

#if UNITY_EDITOR
        private void OnValidate()
        {
            columns = Mathf.Max(2, columns);
            rows = Mathf.Max(2, rows);
            cellSize = Mathf.Max(0.2f, cellSize);
            cellGap = Mathf.Max(0f, cellGap);
            StayAliveGameSettings.NotifySplitSettingsChanged(this);
        }
#endif
    }
}
