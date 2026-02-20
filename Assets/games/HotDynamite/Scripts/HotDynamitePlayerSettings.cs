using UnityEngine;

namespace HotDynamite
{
    [CreateAssetMenu(menuName = "HotDynamite/Settings/Player Settings", fileName = "HotDynamitePlayerSettings")]
    public class HotDynamitePlayerSettings : ScriptableObject
    {
        [Tooltip("Total players spawned in a match (humans + bots).")]
        [Min(2)] public int totalPlayers = 4;
        [Tooltip("Number of human-controlled players. Clamped between 1 and total players.")]
        [Min(1)] public int humanPlayers = 1;
        [Tooltip("Base movement speed before bomb/temporary multipliers.")]
        [Min(0.1f)] public float moveSpeed = 5f;
        [Tooltip("Speed multiplier applied while the player currently holds the bomb.")]
        [Min(0f)] public float bombHolderSpeedMultiplier = 1.1f;
        [Tooltip("Bounce impulse added when colliding with another player.")]
        [Min(0f)] public float playerBounceSpeed = 4.8f;
        [Tooltip("Maximum allowed bounce velocity magnitude after adding bounce impulses.")]
        [Min(0f)] public float playerBounceMaxSpeed = 6.5f;
        [Tooltip("How quickly bounce velocity decays back to zero.")]
        [Min(0f)] public float playerBounceDecay = 8f;
        [Tooltip("Minimum time between bounce responses for the same player.")]
        [Min(0f)] public float playerBounceCooldownSeconds = 0.16f;
        [Tooltip("How long direct movement input is suppressed after a bounce.")]
        [Min(0f)] public float playerBounceInputSuppressionSeconds = 0.08f;

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
