using UnityEngine;

namespace BubbleBattle
{
    [RequireComponent(typeof(Rigidbody2D), typeof(CircleCollider2D), typeof(SpriteRenderer))]
    public class BubbleBattlePlayer : MonoBehaviour
    {
        private const float DirectionEpsilon = 0.0001f;

        [Header("Scene References")]
        [SerializeField] private Rigidbody2D body;
        [SerializeField] private CircleCollider2D hitCollider;
        [SerializeField] private SpriteRenderer spriteRenderer;
        [SerializeField] private Transform pointerPivot;
        [SerializeField] private SpriteRenderer pointerRenderer;

        private BubbleBattleGameManager manager;
        private BubbleBattleGameSettings settings;
        private BubbleBattleControlMode controlMode;
        private int playerIndex;

        public int PlayerIndex => playerIndex;
        public BubbleBattleControlMode ControlMode => controlMode;
        public Vector2 Position => body != null ? body.position : (Vector2)transform.position;

        public void Initialize(
            BubbleBattleGameManager manager,
            BubbleBattleGameSettings settings,
            BubbleBattleControlMode controlMode,
            int playerIndex,
            Color color,
            PhysicsMaterial2D bounceMaterial)
        {
            this.manager = manager;
            this.settings = settings;
            this.controlMode = controlMode;
            this.playerIndex = playerIndex;

            CacheReferences();
            spriteRenderer.color = color;
            spriteRenderer.sortingOrder = 10 + playerIndex;

            body.gravityScale = settings.GravityScale;
            body.linearDamping = settings.LinearDamping;
            body.angularDamping = settings.AngularDamping;
            body.bodyType = RigidbodyType2D.Dynamic;
            body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            body.freezeRotation = true;
            body.interpolation = RigidbodyInterpolation2D.Interpolate;

            hitCollider.radius = 0.5f;
            hitCollider.sharedMaterial = bounceMaterial;
            transform.localScale = Vector3.one * settings.BubbleDiameter;

            if (pointerRenderer != null)
            {
                pointerRenderer.enabled = false;
            }

            body.linearVelocity = Vector2.zero;
            body.angularVelocity = 0f;
            ApplyStartingImpulse();
        }

        private void Awake()
        {
            CacheReferences();
        }

        private void FixedUpdate()
        {
            if (settings == null || body == null)
            {
                return;
            }

            ClampVelocity();
        }

        private void ClampVelocity()
        {
            Vector2 velocity = body.linearVelocity;
            float maxSpeed = settings.MaxSpeed;
            float maxSpeedSquared = maxSpeed * maxSpeed;
            if (velocity.sqrMagnitude > maxSpeedSquared)
            {
                body.linearVelocity = velocity.normalized * maxSpeed;
            }
        }

        private void ApplyStartingImpulse()
        {
            if (body == null || settings == null)
            {
                return;
            }

            Vector2 direction = (Vector2)transform.position - manager.ArenaCenterPosition;
            if (direction.sqrMagnitude <= DirectionEpsilon)
            {
                float angle = (Mathf.PI * 2f * playerIndex) / Mathf.Max(2, settings.PlayerCount);
                direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            }

            float jitter = Random.Range(-settings.StartDirectionJitterDegrees, settings.StartDirectionJitterDegrees);
            Vector2 launchDirection = Quaternion.Euler(0f, 0f, jitter) * direction.normalized;
            body.AddForce(launchDirection * settings.StartImpulse, ForceMode2D.Impulse);
            ClampVelocity();
        }

        private void CacheReferences()
        {
            if (body == null)
            {
                body = GetComponent<Rigidbody2D>();
            }

            if (hitCollider == null)
            {
                hitCollider = GetComponent<CircleCollider2D>();
            }

            if (spriteRenderer == null)
            {
                spriteRenderer = GetComponent<SpriteRenderer>();
            }

            if (pointerPivot == null)
            {
                pointerPivot = transform.Find("PointerPivot");
            }

            if (pointerRenderer == null && pointerPivot != null)
            {
                pointerRenderer = pointerPivot.GetComponentInChildren<SpriteRenderer>(true);
            }
        }
    }
}
