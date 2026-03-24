using UnityEngine;

namespace StayAlive
{
    [CreateAssetMenu(menuName = "StayAlive/Settings/Player Settings", fileName = "StayAlivePlayerSettings")]
    public class StayAlivePlayerSettings : ScriptableObject
    {
        public bool spawnPlayers = true;
        [Min(1)] public int totalPlayers = 1;
        [Min(1)] public int humanPlayers = 1;
        [Min(0.1f)] public float moveSpeed = 5f;
        [Min(0f)] public float playerBounceSpeed = 4.8f;
        [Min(0f)] public float playerBounceMaxSpeed = 6.5f;
        [Min(0f)] public float playerBounceDecay = 8f;
        [Min(0f)] public float playerBounceCooldownSeconds = 0.16f;
        [Min(0f)] public float playerBounceInputSuppressionSeconds = 0.08f;

        public int ClampedTotalPlayers => Mathf.Max(1, totalPlayers);
        public int ClampedHumanPlayers => Mathf.Clamp(humanPlayers, 1, ClampedTotalPlayers);

#if UNITY_EDITOR
        private void OnValidate()
        {
            totalPlayers = Mathf.Max(1, totalPlayers);
            humanPlayers = Mathf.Clamp(humanPlayers, 1, totalPlayers);
            moveSpeed = Mathf.Max(0.1f, moveSpeed);
            playerBounceSpeed = Mathf.Max(0f, playerBounceSpeed);
            playerBounceMaxSpeed = Mathf.Max(0f, playerBounceMaxSpeed);
            playerBounceDecay = Mathf.Max(0f, playerBounceDecay);
            playerBounceCooldownSeconds = Mathf.Max(0f, playerBounceCooldownSeconds);
            playerBounceInputSuppressionSeconds = Mathf.Max(0f, playerBounceInputSuppressionSeconds);
            StayAliveGameSettings.NotifySplitSettingsChanged(this);
        }
#endif
    }
}
