using UnityEngine;

namespace TankBattle
{
    [DefaultExecutionOrder(-1000)]
    [DisallowMultipleComponent]
    public class TankBattleGlobalConfig : MonoBehaviour
    {
        private static TankBattleGlobalConfig instance;

        [Header("Enable")]
        [SerializeField] private bool applyGlobalSorting = true;
        [SerializeField] private bool forceRunInBackground = true;

        [Header("Sorting Layers")]
        [SerializeField] private string floorSortingLayerName = "Default";
        [SerializeField] private string wallSortingLayerName = "Default";
        [SerializeField] private string playerSortingLayerName = "Default";
        [SerializeField] private string bulletSortingLayerName = "Default";

        [Header("Sorting Orders")]
        [SerializeField] private int floorTilemapSortingOrder = 0;
        [SerializeField] private int floorBackgroundSortingOrder = -1;
        [SerializeField] private int wallSortingOrder = 2;
        [SerializeField] private int wallOutlineSortingOrder = 1;
        [SerializeField] private int playerSortingOrder = 3;
        [SerializeField] private int bulletSortingOrder = 4;

        public static TankBattleGlobalConfig Instance
        {
            get
            {
                if (instance == null)
                {
                    instance = FindAnyObjectByType<TankBattleGlobalConfig>();
                }

                return instance;
            }
        }

        public bool ApplyGlobalSorting => applyGlobalSorting;
        public bool ForceRunInBackground => forceRunInBackground;
        public string FloorSortingLayerName => floorSortingLayerName;
        public string WallSortingLayerName => wallSortingLayerName;
        public string PlayerSortingLayerName => playerSortingLayerName;
        public string BulletSortingLayerName => bulletSortingLayerName;
        public int FloorTilemapSortingOrder => floorTilemapSortingOrder;
        public int FloorBackgroundSortingOrder => floorBackgroundSortingOrder;
        public int WallSortingOrder => wallSortingOrder;
        public int WallOutlineSortingOrder => wallOutlineSortingOrder;
        public int PlayerSortingOrder => playerSortingOrder;
        public int BulletSortingOrder => bulletSortingOrder;

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Debug.LogWarning("TankBattleGlobalConfig: Multiple instances found. Using the first loaded instance.");
                return;
            }

            instance = this;
            Application.runInBackground = forceRunInBackground;
        }

        private void OnDestroy()
        {
            if (instance == this)
            {
                instance = null;
            }
        }
    }
}
