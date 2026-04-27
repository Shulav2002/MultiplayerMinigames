using UnityEngine;

namespace TankBattle
{
    [CreateAssetMenu(menuName = "TankBattle/Bullet Settings", fileName = "TankBattleBulletSettings")]
    public class TankBattleBulletSettings : ScriptableObject
    {
        [Header("Movement")]
        [Tooltip("Speed of the bullet in units per second.")]
        [Min(0.1f)] public float bulletSpeed = 8f;

        [Header("Lifetime")]
        [Tooltip("Time in seconds before bullet is destroyed.")]
        [Min(0.1f)] public float bulletLifetime = 5f;

        [Header("Reflection")]
        [Tooltip("Speed multiplier after each wall reflection.")]
        [Range(0.5f, 1f)] public float reflectionSpeedRetention = 0.95f;

        [Header("Player Stats")]
        [Tooltip("Maximum number of shots per player.")]
        [Min(1)] public int maxShotsPerPlayer = 5;
    }
}
