using UnityEngine;

namespace HotDynamite
{
    [CreateAssetMenu(menuName = "HotDynamite/Game Settings", fileName = "HotDynamiteGameSettings")]
    public class HotDynamiteGameSettings : ScriptableObject
    {
        [Header("Players")]
        [Min(2)] public int totalPlayers = 4;
        [Min(1)] public int humanPlayers = 1;
        [Min(0.1f)] public float moveSpeed = 5f;
        [Min(0f)] public float bombHolderSpeedMultiplier = 1.1f;

        [Header("Bomb")]
        [Min(0.1f)] public float bombTimerSeconds = 10f;
        [Min(0f)] public float passCooldownSeconds = 0.5f;
        [Min(0f)] public float bombPassSpeedEffectSeconds = 1.5f;
        [Min(0.01f)] public float bombPasserSpeedMultiplier = 1.15f;
        [Min(0.01f)] public float bombReceiverSpeedMultiplier = 0.85f;

        [Header("Arena")]
        public Vector2 arenaMin = new Vector2(-8f, -4.5f);
        public Vector2 arenaMax = new Vector2(8f, 4.5f);

        [Header("Bots")]
        [Min(0.02f)] public float botDecisionInterval = 0.1f;
        [Min(0f)] public float botWanderStrength = 0.2f;
        [Min(0f)] public float botAvoidanceDistance = 1.5f;
        [Min(0f)] public float botAvoidanceStrength = 2f;

        public int ClampedTotalPlayers => Mathf.Max(2, totalPlayers);
        public int ClampedHumanPlayers => Mathf.Clamp(humanPlayers, 1, Mathf.Max(2, totalPlayers));
    }
}
