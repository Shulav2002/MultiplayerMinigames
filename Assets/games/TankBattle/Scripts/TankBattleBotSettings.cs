using UnityEngine;

namespace TankBattle
{
    [CreateAssetMenu(menuName = "TankBattle/Bot Settings", fileName = "TankBattleBotSettings")]
    public class TankBattleBotSettings : ScriptableObject
    {
        [Header("Decision Making")]
        [Tooltip("Time between bot decisions in seconds.")]
        [Range(0.1f, 1f)] public float decisionInterval = 0.3f;
        
        [Tooltip("Additional random delay added to each decision to mimic reaction variance.")]
        [Range(0f, 0.35f)] public float decisionJitter = 0.12f;

        [Header("Pathfinding")]
        [Tooltip("Distance to check ahead for walls when pathfinding.")]
        [Range(1f, 5f)] public float pathLookaheadDistance = 3f;
        
        [Tooltip("Distance to keep from walls.")]
        [Range(0.5f, 3f)] public float wallAvoidanceDistance = 1.5f;

        [Header("Combat")]
        [Tooltip("Preferred distance to maintain from enemy.")]
        [Range(2f, 8f)] public float preferredCombatDistance = 4f;
        
        [Tooltip("Minimum safe distance from enemy.")]
        [Range(1f, 5f)] public float minimumSafeDistance = 2f;
        
        [Tooltip("Raycast distance for clear shot detection.")]
        [Range(5f, 20f)] public float shootRaycastDistance = 15f;

        [Tooltip("How inaccurate the bot aim can be, in degrees.")]
        [Range(0f, 20f)] public float aimErrorDegrees = 6f;

        [Tooltip("How long the bot keeps the shoot input pressed.")]
        [Range(0.04f, 0.25f)] public float shootTapHoldSeconds = 0.08f;

        [Header("Tactics")]
        [Tooltip("Time to reposition after shooting.")]
        [Range(0.5f, 3f)] public float repositionTime = 1.2f;
        
        [Tooltip("Distance to move when repositioning.")]
        [Range(1f, 5f)] public float repositionDistance = 2.5f;

        [Header("Behavior")]
        [Tooltip("How much random wandering when no target.")]
        [Range(0f, 1f)] public float wanderStrength = 0.4f;
        
        [Tooltip("Movement smoothness (like human).")]
        [Range(1f, 20f)] public float steeringSmoothness = 8f;
        
        [Tooltip("Chance to change direction when wandering.")]
        [Range(0f, 0.5f)] public float wanderChangeChance = 0.1f;

        [Tooltip("Chance to hesitate briefly before committing to a shot.")]
        [Range(0f, 0.6f)] public float hesitationChance = 0.15f;
    }
}


