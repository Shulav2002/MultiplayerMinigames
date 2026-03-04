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
        [Tooltip("Time window in seconds to detect combo key press (e.g., W+D).")]
        [SerializeField] private float comboPressToleranceMs = 100f;
        [Tooltip("Time window in seconds to hold rotation after releasing keys.")]
        [SerializeField] private float releaseToleranceMs = 100f;

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
        private float currentRotationAngle = 0f;
        private float targetRotationAngle = 0f;
        private float rotationSpeed = 360f;
        private Vector2 lastConfirmedInput = Vector2.right;
        private Vector2 previousFrameInput = Vector2.zero;
        private float firstKeyPressTime = 0f;
        private float lastInputActiveTime = 0f;

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

            if (IsBlockedByWall(spawnPosition, shootDirection))
            {
                return;
            }

            GameObject bulletObj = Instantiate(bulletPrefab, spawnPosition, Quaternion.identity);
            TankBattleBullet bullet = bulletObj.GetComponent<TankBattleBullet>();
            if (bullet != null)
            {
                bullet.Initialize(bulletSettings, shootDirection, gameObject);
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

        private bool IsBlockedByWall(Vector3 spawnPos, Vector2 direction)
        {
            float checkDistance = 0.5f;
            float checkRadius = 0.2f;
            Vector2 startPos = body != null ? body.position : (Vector2)transform.position;
            
            RaycastHit2D hit = Physics2D.CircleCast(startPos, checkRadius, direction, checkDistance);
            
            if (hit.collider != null)
            {
                if (hit.collider.CompareTag("Wall") || 
                    hit.collider.name.Contains("Bdr") || 
                    hit.collider.name.Contains("HW") || 
                    hit.collider.name.Contains("VW"))
                {
                    return true;
                }
            }
            
            return false;
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
            if (facingTransform == null)
            {
                return;
            }

            Vector2 inputToUse = ProcessInputWithDualTolerance(movementInput);
            
            if (inputToUse.sqrMagnitude > facingInputThreshold * facingInputThreshold)
            {
                targetRotationAngle = GetSnapAngle(inputToUse);
            }

            float angleDifference = Mathf.DeltaAngle(currentRotationAngle, targetRotationAngle);
            
            if (Mathf.Abs(angleDifference) > 0.1f)
            {
                float rotationStep = rotationSpeed * Time.fixedDeltaTime;
                currentRotationAngle = Mathf.MoveTowardsAngle(currentRotationAngle, targetRotationAngle, rotationStep);
            }
            else
            {
                currentRotationAngle = targetRotationAngle;
            }

            facingTransform.localRotation = Quaternion.Euler(0f, 0f, currentRotationAngle);
        }

        private Vector2 ProcessInputWithDualTolerance(Vector2 currentInput)
        {
            bool hasInput = currentInput.sqrMagnitude > 0.01f;
            bool hadInput = previousFrameInput.sqrMagnitude > 0.01f;
            
            if (hasInput)
            {
                bool inputChanged = Vector2.Distance(currentInput.normalized, previousFrameInput.normalized) > 0.1f;
                
                if (!hadInput || inputChanged)
                {
                    firstKeyPressTime = Time.time;
                }
                
                float timeSinceInputChange = Time.time - firstKeyPressTime;
                float pressToleranceSec = comboPressToleranceMs / 1000f;
                
                if (timeSinceInputChange <= pressToleranceSec)
                {
                    bool isDiagonal = Mathf.Abs(currentInput.x) > 0.01f && Mathf.Abs(currentInput.y) > 0.01f;
                    bool wasDiagonal = Mathf.Abs(previousFrameInput.x) > 0.01f && Mathf.Abs(previousFrameInput.y) > 0.01f;
                    
                    if (isDiagonal)
                    {
                        lastConfirmedInput = currentInput;
                    }
                    else if (wasDiagonal)
                    {
                        lastConfirmedInput = previousFrameInput;
                    }
                    else
                    {
                        lastConfirmedInput = currentInput;
                    }
                }
                else
                {
                    lastConfirmedInput = currentInput;
                }
                
                lastInputActiveTime = Time.time;
                previousFrameInput = currentInput;
                return lastConfirmedInput;
            }
            else
            {
                float timeSinceRelease = Time.time - lastInputActiveTime;
                float releaseToleranceSec = releaseToleranceMs / 1000f;
                
                if (hadInput && timeSinceRelease < releaseToleranceSec)
                {
                    previousFrameInput = currentInput;
                    return lastConfirmedInput;
                }
                
                previousFrameInput = currentInput;
                return Vector2.zero;
            }
        }

        private float GetSnapAngle(Vector2 input)
        {
            if (input.sqrMagnitude < 0.01f)
            {
                return currentRotationAngle;
            }

            float angle = Mathf.Atan2(input.y, input.x) * Mathf.Rad2Deg;
            
            if (angle >= -22.5f && angle < 22.5f) return 0f;
            if (angle >= 22.5f && angle < 67.5f) return 45f;
            if (angle >= 67.5f && angle < 112.5f) return 90f;
            if (angle >= 112.5f && angle < 157.5f) return 135f;
            if (angle >= 157.5f || angle < -157.5f) return 180f;
            if (angle >= -157.5f && angle < -112.5f) return -135f;
            if (angle >= -112.5f && angle < -67.5f) return -90f;
            if (angle >= -67.5f && angle < -22.5f) return -45f;
            
            return 0f;
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
