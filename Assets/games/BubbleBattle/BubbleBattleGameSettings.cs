using UnityEngine;

using UnityEngine.Serialization;

namespace BubbleBattle
{
    [CreateAssetMenu(menuName = "BubbleBattle/Game Settings", fileName = "BubbleBattleGameSettings")]
    public class BubbleBattleGameSettings : ScriptableObject
    {
        [Header("Match")]
        [SerializeField, Min(2)] private int playerCount = 2;
        [SerializeField, Min(1)] private int humanPlayerCount = 1;
        [SerializeField, Min(0.1f)] private float countdownSeconds = 3f;

        [Header("Movement")]
        [SerializeField, Min(0f)] private float gravityScale = 0f;
        [SerializeField, Min(0f)] private float linearDamping = 0f;
        [SerializeField, Min(0f)] private float angularDamping = 0f;
        [SerializeField, Min(0.1f)] private float bubbleDiameter = 0.95f;
        [FormerlySerializedAs("tapImpulse")]
        [SerializeField, Min(0.1f)] private float startImpulse = 7.8f;
        [SerializeField, Min(0.1f)] private float maxSpeed = 9f;
        [SerializeField, Min(0f)] private float startDirectionJitterDegrees = 12f;
        [SerializeField, Min(0f)] private float centerSpawnSpacing = 0.7f;

        [Header("Arena")]
        [SerializeField, Min(0.1f)] private float arenaRadius = 7f;
        [SerializeField, Min(0.01f)] private float boundaryThickness = 0.18f;
        [SerializeField, Range(0f, 1f)] private float bounceRestitution = 1f;
        [SerializeField] private Color boundaryColor = new Color(0.65f, 0.93f, 1f, 0.95f);

        [Header("Weapons")]
        [SerializeField, Min(0f)] private float weaponRespawnDelaySeconds = 1.5f;
        [SerializeField, Min(1)] private int maxActiveWeaponPickups = 1;

        public int PlayerCount => Mathf.Max(2, playerCount);
        public int HumanPlayerCount => Mathf.Clamp(humanPlayerCount, 1, PlayerCount);
        public float CountdownSeconds => Mathf.Max(0.1f, countdownSeconds);
        public float GravityScale => Mathf.Max(0f, gravityScale);
        public float LinearDamping => Mathf.Max(0f, linearDamping);
        public float AngularDamping => Mathf.Max(0f, angularDamping);
        public float BubbleDiameter => Mathf.Max(0.1f, bubbleDiameter);
        public float StartImpulse => Mathf.Max(0.1f, startImpulse);
        public float MaxSpeed => Mathf.Max(0.1f, maxSpeed);
        public float StartDirectionJitterDegrees => Mathf.Max(0f, startDirectionJitterDegrees);
        public float CenterSpawnSpacing => Mathf.Max(0f, centerSpawnSpacing);
        public float ArenaRadius => Mathf.Max(0.1f, arenaRadius);
        public float BoundaryThickness => Mathf.Max(0.01f, boundaryThickness);
        public float BounceRestitution => Mathf.Clamp01(bounceRestitution);
        public Color BoundaryColor => boundaryColor;
        public float WeaponRespawnDelaySeconds => Mathf.Max(0f, weaponRespawnDelaySeconds);
        public int MaxActiveWeaponPickups => Mathf.Max(1, maxActiveWeaponPickups);
    }
}
