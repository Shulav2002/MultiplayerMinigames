using UnityEngine;


namespace TankBattle
{
    [RequireComponent(typeof(Rigidbody2D), typeof(Collider2D))]
    public class TankBattlePlayer : MonoBehaviour
    {
        [Header("Input")]
        [Tooltip("Input provider for player controls.")]
        [SerializeField] private TankBattlePlayerInput playerInput;

        [Header("Components")]
        [Tooltip("Rigidbody used for 2D movement.")]
        [SerializeField] private Rigidbody2D body;
        [Tooltip("Primary collider for the tank.")]
        [SerializeField] private Collider2D hitCollider;

        [Header("Movement")]
        [Tooltip("Base movement speed of the tank.")]
        [SerializeField] private float moveSpeed = 3f;

        [Header("Facing")]
        [Tooltip("Transform to rotate for facing direction.")]
        [SerializeField] private Transform facingTransform;
        [Tooltip("Minimum movement magnitude required before updating facing rotation.")]
        [SerializeField] private float facingInputThreshold = 0.05f;

        [Header("Shooting")]
        [Tooltip("Bullet prefab to spawn when shooting.")]
        [SerializeField] private GameObject bulletPrefab;
        [Tooltip("Bullet settings configuration.")]
        [SerializeField] private TankBattleBulletSettings bulletSettings;
        [Tooltip("Position where bullets spawn.")]
        [SerializeField] private Transform bulletSpawnPoint;
        [Tooltip("Minimum time between shots.")]
        [SerializeField] private float shootCooldown = 0.5f;

        private const float MovementInputNormalizeThreshold = 1f;
        private bool isAlive = true;
        private Vector2 estimatedVelocity;
        private float nextShootTime = 0f;
        private bool inputEnabled = true;
        private int remainingShots = 5;

        public bool IsAlive => isAlive;
        public Vector2 Position => body != null ? body.position : (Vector2)transform.position;
        public int RemainingShots => remainingShots;

        public void Initialize(GameObject bulletPrefabToUse, TankBattleBulletSettings bulletSettingsToUse, bool enableInput)
        {
            bulletPrefab = bulletPrefabToUse;
            bulletSettings = bulletSettingsToUse;
            inputEnabled = enableInput;
            
            if (bulletSettings != null)
            {
                remainingShots = bulletSettings.maxShotsPerPlayer;
            }
            else
            {
                remainingShots = 5;
            }
            
            EnsureComponentsAttached();
            CacheRequiredComponents();
            ConfigureRigidbodyForTopDown();
            
            if (playerInput != null)
            {
                playerInput.enabled = inputEnabled;
            }
        }

        private void Start()
        {
            if (playerInput == null && inputEnabled)
            {
                EnsureComponentsAttached();
            }
            
            CacheRequiredComponents();
            ConfigureRigidbodyForTopDown();
            IgnoreOtherPlayers();
        }

        private void IgnoreOtherPlayers()
        {
            TankBattlePlayer[] allPlayers = FindObjectsByType<TankBattlePlayer>(FindObjectsSortMode.None);
            foreach (TankBattlePlayer otherPlayer in allPlayers)
            {
                if (otherPlayer != this && otherPlayer.hitCollider != null && hitCollider != null)
                {
                    Physics2D.IgnoreCollision(hitCollider, otherPlayer.hitCollider, true);
                }
            }
        }

        private void EnsureComponentsAttached()
        {
            if (playerInput == null)
            {
                playerInput = GetComponent<TankBattlePlayerInput>();
                if (playerInput == null)
                {
                    playerInput = gameObject.AddComponent<TankBattlePlayerInput>();
                }
            }
        }

        private void ConfigureRigidbodyForTopDown()
        {
            if (body == null)
            {
                return;
            }

            body.gravityScale = 0f;
            body.constraints = RigidbodyConstraints2D.FreezeRotation;
            
            if (hitCollider != null)
            {
                hitCollider.isTrigger = false;
            }
        }

        private void FixedUpdate()
        {
            if (!CanSimulateMovement())
            {
                estimatedVelocity = Vector2.zero;
                return;
            }

            Vector2 input = GetNormalizedMoveInput();
            estimatedVelocity = input * moveSpeed;
            UpdateFacing(input);
            MoveByEstimatedVelocity();
        }

        private void Update()
        {
            if (!isAlive || !inputEnabled || playerInput == null)
            {
                return;
            }

            if (playerInput.ShootPressed && Time.time >= nextShootTime && remainingShots > 0)
            {
                Shoot();
            }
        }

        private void Shoot()
        {
            if (bulletPrefab == null || remainingShots <= 0)
            {
                return;
            }

            Vector3 spawnPosition = bulletSpawnPoint != null ? bulletSpawnPoint.position : transform.position;
            Vector2 shootDirection = facingTransform != null 
                ? (Vector2)(facingTransform.right) 
                : Vector2.right;

            GameObject bulletObj = Instantiate(bulletPrefab, spawnPosition, Quaternion.identity);
            TankBattleBullet bullet = bulletObj.GetComponent<TankBattleBullet>();
            if (bullet != null)
            {
                bullet.Initialize(bulletSettings, shootDirection);
            }

            remainingShots--;
            nextShootTime = Time.time + shootCooldown;

            if (playerInput != null)
            {
                playerInput.ResetMovementInput();
            }

            if (body != null)
            {
                body.linearVelocity = Vector2.zero;
            }
        }

        private bool CanSimulateMovement()
        {
            return isAlive && body != null;
        }

        private Vector2 GetNormalizedMoveInput()
        {
            if (playerInput == null)
            {
                return Vector2.zero;
            }

            Vector2 input = playerInput.MoveVector;
            if (input.sqrMagnitude > MovementInputNormalizeThreshold)
            {
                input.Normalize();
            }

            return input;
        }

        private void MoveByEstimatedVelocity()
        {
            if (body == null)
            {
                return;
            }

            Vector2 next = body.position + estimatedVelocity * Time.fixedDeltaTime;
            body.MovePosition(next);
        }

        private void UpdateFacing(Vector2 movementInput)
        {
            if (facingTransform == null || movementInput.sqrMagnitude < facingInputThreshold * facingInputThreshold)
            {
                return;
            }

            float facingAngle = Mathf.Atan2(movementInput.y, movementInput.x) * Mathf.Rad2Deg;
            facingTransform.localRotation = Quaternion.Euler(0f, 0f, facingAngle);
        }

        private void CacheRequiredComponents()
        {
            if (body == null)
            {
                body = GetComponent<Rigidbody2D>();
            }

            if (hitCollider == null)
            {
                hitCollider = GetComponent<Collider2D>();
            }

            if (facingTransform == null)
            {
                facingTransform = transform;
            }

            if (bulletSpawnPoint == null)
            {
                Transform nozzle = transform.Find("Nozzle");
                if (nozzle != null)
                {
                    bulletSpawnPoint = nozzle;
                }
                else
                {
                    bulletSpawnPoint = transform;
                }
            }

            if (playerInput == null)
            {
                playerInput = GetComponent<TankBattlePlayerInput>();
            }
        }

        private void OnDestroy()
        {
            if (hitCollider == null) return;

            TankBattlePlayer[] allPlayers = FindObjectsByType<TankBattlePlayer>(FindObjectsSortMode.None);
            foreach (TankBattlePlayer otherPlayer in allPlayers)
            {
                if (otherPlayer != this && otherPlayer.hitCollider != null)
                {
                    Physics2D.IgnoreCollision(hitCollider, otherPlayer.hitCollider, false);
                }
            }
        }
    }
}
