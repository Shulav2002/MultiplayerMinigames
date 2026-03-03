using UnityEngine;

public class TankBattleGameManager : MonoBehaviour
{
    [Header("Map")]
    public TankBattleMapGenerator mapGenerator;

    [Header("Tank Prefabs")]
    public GameObject tank1Prefab;
    public GameObject tank2Prefab;

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
            spawnedTank1 = Instantiate(tank1Prefab, p1Pos, Quaternion.identity);

        if (tank2Prefab != null)
            spawnedTank2 = Instantiate(tank2Prefab, p2Pos, Quaternion.identity);
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
