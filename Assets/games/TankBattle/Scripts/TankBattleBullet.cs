using UnityEngine;
using TankBattle.Networking;

namespace TankBattle
{
    [RequireComponent(typeof(Rigidbody2D), typeof(Collider2D))]
    public class TankBattleBullet : MonoBehaviour
    {
        [Header("Settings")]
        [SerializeField] private TankBattleBulletSettings settings;

        [Header("Components")]
        [SerializeField] private Rigidbody2D body;
        [SerializeField] private Collider2D bulletCollider;

        [Header("Sorting")]
        [SerializeField] private bool applySortingOrderOnInitialize = true;
        [SerializeField] private int spriteSortingOrder = 3;
        [SerializeField] private bool useGlobalSortingConfig = true;
        [SerializeField] private TankBattleGlobalConfig globalConfig;

        private static int nextNetworkBulletId = 1;

        private Vector2 currentVelocity;
        private float destroyTime;
        private bool snapshotProxy;
        private int ownerChairId = -1;
        private int networkBulletId;
        private bool returnShotOnDestroy;

        public GameObject OwnerPlayer { get; private set; }
        public bool IsSnapshotProxy => snapshotProxy;
        public int OwnerChairId => ownerChairId;
        public int NetworkBulletId => networkBulletId;
        public Vector2 CurrentVelocity => currentVelocity;

        public void Initialize(TankBattleBulletSettings bulletSettings, Vector2 direction, GameObject owner)
        {
            Initialize(bulletSettings, direction, owner, -1);
        }

        public void Initialize(TankBattleBulletSettings bulletSettings, Vector2 direction, GameObject owner, int ownerChairId)
        {
            settings = bulletSettings;
            OwnerPlayer = owner;
            this.ownerChairId = ownerChairId;
            snapshotProxy = false;
            returnShotOnDestroy = true;
            if (networkBulletId <= 0)
            {
                networkBulletId = nextNetworkBulletId++;
            }

            CacheComponents();
            ApplySortingOrderIfNeeded();
            ConfigurePhysicsForSimulation();

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

        public void InitializeSnapshotProxy(
            TankBattleBulletSettings bulletSettings,
            int bulletId,
            Vector2 position,
            Vector2 velocity,
            int ownerChairId)
        {
            settings = bulletSettings;
            snapshotProxy = true;
            networkBulletId = bulletId;
            this.ownerChairId = ownerChairId;
            OwnerPlayer = null;
            currentVelocity = velocity;
            destroyTime = float.PositiveInfinity;
            returnShotOnDestroy = false;

            CacheComponents();
            ApplySortingOrderIfNeeded();
            ConfigurePhysicsForSnapshotProxy();
            transform.position = new Vector3(position.x, position.y, transform.position.z);
        }

        public void ApplySnapshotState(Vector2 position, Vector2 velocity)
        {
            currentVelocity = velocity;

            if (body != null)
            {
                body.position = position;
                body.linearVelocity = Vector2.zero;
            }
            else
            {
                transform.position = new Vector3(position.x, position.y, transform.position.z);
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

        private void ConfigurePhysicsForSimulation()
        {
            if (body == null)
            {
                return;
            }

            body.bodyType = RigidbodyType2D.Dynamic;
            body.simulated = true;
            body.gravityScale = 0f;
            body.constraints = RigidbodyConstraints2D.FreezeRotation;
            body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

            if (bulletCollider != null)
            {
                bulletCollider.enabled = true;
                bulletCollider.isTrigger = false;
            }
        }

        private void ConfigurePhysicsForSnapshotProxy()
        {
            if (body != null)
            {
                body.bodyType = RigidbodyType2D.Kinematic;
                body.simulated = true;
                body.gravityScale = 0f;
                body.constraints = RigidbodyConstraints2D.FreezeRotation;
                body.collisionDetectionMode = CollisionDetectionMode2D.Discrete;
                body.linearVelocity = Vector2.zero;
            }

            if (bulletCollider != null)
            {
                bulletCollider.enabled = false;
            }
        }

        private void FixedUpdate()
        {
            if (snapshotProxy)
            {
                Vector2 next = (Vector2)transform.position + currentVelocity * Time.fixedDeltaTime;
                if (body != null)
                {
                    body.MovePosition(next);
                }
                else
                {
                    transform.position = new Vector3(next.x, next.y, transform.position.z);
                }

                return;
            }

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
            if (snapshotProxy || collision == null)
            {
                return;
            }

            if (collision.collider.GetComponent<TankBattleBullet>() != null)
            {
                Destroy(gameObject);
                return;
            }

            TankBattlePlayer hitPlayer = collision.collider.GetComponentInParent<TankBattlePlayer>();
            if (hitPlayer != null)
            {
                HandlePlayerHit(hitPlayer);
                return;
            }

            if (IsWall(collision.gameObject))
            {
                ReflectOffWall(collision);
            }
        }

        private bool IsWall(GameObject obj)
        {
            if (obj == null)
            {
                return false;
            }

            try
            {
                return obj.CompareTag("Wall");
            }
            catch
            {
                return obj.name.Contains("Bdr") || obj.name.Contains("HW") || obj.name.Contains("VW");
            }
        }

        private void HandlePlayerHit(TankBattlePlayer hitPlayer)
        {
            if (hitPlayer != null && OwnerPlayer != null)
            {
                TankBattlePlayer shooter = OwnerPlayer.GetComponent<TankBattlePlayer>();
                TankBattleGameManager gameManager = FindAnyObjectByType<TankBattleGameManager>();
                TankBattleUI ui = gameManager != null ? gameManager.gameUI : FindAnyObjectByType<TankBattleUI>();
                bool shouldProcessHitHere = ShouldProcessHitLocally(gameManager, shooter);

                if (hitPlayer != null && shooter != null && shouldProcessHitHere)
                {
                    if (ui != null)
                    {
                        ui.OnPlayerHit(hitPlayer, shooter);
                    }

                    if (gameManager != null && gameManager.IsOnlineMatch)
                    {
                        TankBattleOnlineMatchController onlineController = FindAnyObjectByType<TankBattleOnlineMatchController>();
                        if (onlineController != null)
                        {
                            onlineController.BroadcastLocalBulletHit(shooter.ChairId, hitPlayer.ChairId);
                        }
                    }
                }
            }

            Destroy(gameObject);
        }

        private static bool ShouldProcessHitLocally(TankBattleGameManager gameManager, TankBattlePlayer shooter)
        {
            if (shooter == null)
            {
                return false;
            }

            if (gameManager == null || !gameManager.IsOnlineMatch)
            {
                return true;
            }

            if (shooter.ControlMode == TankBattlePlayerControlMode.LocalHuman)
            {
                return true;
            }

            return gameManager.HasSimulationAuthority && shooter.IsBotParticipant;
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

        private void ApplySortingOrderIfNeeded()
        {
            if (!applySortingOrderOnInitialize)
            {
                return;
            }

            int resolvedSortingOrder = spriteSortingOrder;
            string resolvedLayerName = null;

            if (useGlobalSortingConfig)
            {
                TankBattleGlobalConfig config = ResolveGlobalConfig();
                if (config != null && config.ApplyGlobalSorting)
                {
                    resolvedSortingOrder = config.BulletSortingOrder;
                    resolvedLayerName = config.BulletSortingLayerName;
                }
            }

            SpriteRenderer[] renderers = GetComponentsInChildren<SpriteRenderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                SpriteRenderer renderer = renderers[i];
                if (renderer == null)
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(resolvedLayerName))
                {
                    renderer.sortingLayerName = resolvedLayerName;
                }

                renderer.sortingOrder = resolvedSortingOrder;
            }
        }

        private TankBattleGlobalConfig ResolveGlobalConfig()
        {
            if (globalConfig == null)
            {
                globalConfig = TankBattleGlobalConfig.Instance;
            }

            return globalConfig;
        }

        private void OnDestroy()
        {
            if (!returnShotOnDestroy || OwnerPlayer == null)
            {
                return;
            }

            TankBattlePlayer ownerPlayer = OwnerPlayer.GetComponent<TankBattlePlayer>();
            if (ownerPlayer != null)
            {
                ownerPlayer.ReturnShot(1);
            }
        }
    }
}
