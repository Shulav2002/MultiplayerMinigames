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
        private GameObject ownerPlayer;

        public void Initialize(TankBattleBulletSettings bulletSettings, Vector2 direction, GameObject owner)
        {
            settings = bulletSettings;
            ownerPlayer = owner;

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

            body.bodyType = RigidbodyType2D.Dynamic;
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
                body.linearVelocity = currentVelocity;
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
                HandlePlayerHit(collision.gameObject);
                return;
            }

            if (IsWall(collision.gameObject))
            {
                ReflectOffWall(collision);
            }
        }

        private bool IsWall(GameObject obj)
        {
            if (obj == null) return false;
            
            try
            {
                return obj.CompareTag("Wall");
            }
            catch
            {
                return obj.name.Contains("Bdr") || obj.name.Contains("HW") || obj.name.Contains("VW");
            }
        }

        private void HandlePlayerHit(GameObject hitPlayerObj)
        {
            TankBattleUI ui = FindObjectsByType<TankBattleUI>(FindObjectsSortMode.None).Length > 0 
                ? FindObjectsByType<TankBattleUI>(FindObjectsSortMode.None)[0] 
                : null;

            if (ui != null && hitPlayerObj != null && ownerPlayer != null)
            {
                TankBattlePlayer hitPlayer = hitPlayerObj.GetComponent<TankBattlePlayer>();
                TankBattlePlayer shooter = ownerPlayer.GetComponent<TankBattlePlayer>();
                
                if (hitPlayer != null && shooter != null)
                {
                    ui.OnPlayerHit(hitPlayer, shooter);
                }
            }

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
