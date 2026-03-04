using UnityEngine;

using TankBattle;


namespace TankBattle
{
    public class TankBattleGameManager : MonoBehaviour
    {
        [Header("Map")]
        public TankBattleMapGenerator mapGenerator;

        [Header("Tank Prefabs")]
        public GameObject tank1Prefab;
        public GameObject tank2Prefab;

        [Header("Bullet")]
        public GameObject bulletPrefab;
        public TankBattleBulletSettings bulletSettings;

        [Header("Player Control")]
        public bool enablePlayer1Input = true;
        public bool enablePlayer2Input = false;

        GameObject spawnedTank1;
        GameObject spawnedTank2;

        void Start()
        {
            // === ORDER MATTERS ===
            // 1. Generate map first (sets spawn positions)
            // 2. THEN spawn tanks at those positions
            if (mapGenerator != null)
                mapGenerator.GenerateMap();

            SpawnTanks();
        }


        void SpawnTanks()
        {
            if (mapGenerator == null) return;

            Vector3 p1Pos = mapGenerator.GetPlayer1Spawn();
            Vector3 p2Pos = mapGenerator.GetPlayer2Spawn();

            if (tank1Prefab != null)
            {
                spawnedTank1 = Instantiate(tank1Prefab, p1Pos, Quaternion.identity);
                SetupPlayer(spawnedTank1, enablePlayer1Input, 1);
            }

            if (tank2Prefab != null)
            {
                spawnedTank2 = Instantiate(tank2Prefab, p2Pos, Quaternion.identity);
                SetupPlayer(spawnedTank2, enablePlayer2Input, 2);
            }
        }

    void SetupPlayer(GameObject tank, bool enableInput, int playerNumber)
    {
        if (tank == null) return;

        TankBattlePlayer player = tank.GetComponent<TankBattlePlayer>();
        if (player == null)
        {
            player = tank.AddComponent<TankBattlePlayer>();
        }

        player.Initialize(bulletPrefab, bulletSettings, enableInput);

        if (enableInput)
        {
            TankBattlePlayerInput input = tank.GetComponent<TankBattlePlayerInput>();
            if (input != null)
            {
                typeof(TankBattlePlayerInput).GetField("forceMobileJoystickInEditor", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.SetValue(input, true);
                
                Color joystickColor = playerNumber == 1 
                    ? new Color(0.055f, 0.514f, 0.996f, 1f)
                    : new Color(0.996f, 0.145f, 0.145f, 1f);
                input.SetJoystickColor(joystickColor);
            }
        }
    }

        // Call this to regenerate the map and respawn tanks (e.g., between rounds)
        public void RegenerateMap()
        {
            if (spawnedTank1 != null) Destroy(spawnedTank1);
            if (spawnedTank2 != null) Destroy(spawnedTank2);

            mapGenerator.seed = Random.Range(0, 999999);
            mapGenerator.GenerateMap();

            SpawnTanks();
        }
    }
}