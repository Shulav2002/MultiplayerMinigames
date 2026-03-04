using UnityEngine;


namespace TankBattle
{
    [RequireComponent(typeof(Rigidbody2D), typeof(Collider2D))]
    public class TankBattleBullet : MonoBehaviour
    {
        [Header("Settings")]
        [Tooltip("Bullet configuration settings.")]
        [SerializeField] private TankBattleBulletSettings settings;

        [Header("Components")]
        [Tooltip("Rigidbody used for movement.")]
        [SerializeField] private Rigidbody2D body;
        [Tooltip("Collider for hit detection.")]
        [SerializeField] private Collider2D bulletCollider;

        private Vector2 currentVelocity;
        private float destroyTime;

        public void Initialize(TankBattleBulletSettings bulletSettings, Vector2 direction)
        {
            settings = bulletSettings;

            CacheComponents();
            ConfigurePhysics();

            if (settings != null)
            {
                currentVelocity = direction.normalized * settings.bulletSpeed;
                destroyTime = Time.time + settings.bulletLifetime;
            }
            else
            {
                currentVelocity = direction.normalized * 8f;
                destroyTime = Time.time + 5f;
            }
        }

        private void CacheComponents()
        {
            if (body == null)
            {
                body = GetComponent<Rigidbody2D>();
            }

            if (bulletCollider == null)
            {
                bulletCollider = GetComponent<Collider2D>();
            }
        }

        private void ConfigurePhysics()
        {
            if (body == null)
            {
                return;
            }

            body.gravityScale = 0f;
            body.constraints = RigidbodyConstraints2D.FreezeRotation;
            body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

            if (bulletCollider != null)
            {
                bulletCollider.isTrigger = false;
            }
        }

        private void FixedUpdate()
        {
            if (Time.time >= destroyTime)
            {
                Destroy(gameObject);
                return;
            }

            if (body != null)
            {
                body.MovePosition(body.position + currentVelocity * Time.fixedDeltaTime);
            }
        }

        private void OnCollisionEnter2D(Collision2D collision)
        {
            if (collision == null)
            {
                return;
            }

            if (collision.collider.GetComponent("TankBattlePlayer") != null)
            {
                HandlePlayerHit();
                return;
            }

            if (IsWall(collision.gameObject))
            {
                ReflectOffWall(collision);
            }
        }

        private bool IsWall(GameObject obj)
        {
            return obj.CompareTag("Wall");
        }

        private void HandlePlayerHit()
        {
            Destroy(gameObject);
        }

        private void ReflectOffWall(Collision2D collision)
        {
            if (collision.contactCount == 0)
            {
                return;
            }

            Vector2 normal = collision.GetContact(0).normal;
            currentVelocity = Vector2.Reflect(currentVelocity, normal);

            if (settings != null)
            {
                currentVelocity *= settings.reflectionSpeedRetention;
            }
        }
    }
}
