using TankBattle.Networking;
using UnityEngine;

namespace TankBattle
{
    [RequireComponent(typeof(Rigidbody2D), typeof(Collider2D))]
    public class TankBattlePlayer : MonoBehaviour
    {
        [Header("Input")]
        [SerializeField] private TankBattlePlayerInput playerInput;

        [Header("Components")]
        [SerializeField] private Rigidbody2D body;
        [SerializeField] private Collider2D hitCollider;

        [Header("Movement")]
        [SerializeField] private float moveSpeed = 3f;

        [Header("Facing")]
        [SerializeField] private Transform facingTransform;
        [SerializeField] private float facingInputThreshold = 0.05f;
        [SerializeField] private float comboPressToleranceMs = 300f;
        [SerializeField] private float releaseToleranceMs = 300f;

        [Header("Shooting")]
        [SerializeField] private GameObject bulletPrefab;
        [SerializeField] private TankBattleBulletSettings bulletSettings;
        [SerializeField] private Transform bulletSpawnPoint;
        [SerializeField] private float shootCooldown = 0.5f;

        [Header("Animation")]
        [SerializeField] private Animator animator;
        [SerializeField] private string shootAnimationState = "BlueTankShoot";

        [Header("Sorting")]
        [SerializeField] private bool applySortingOrderOnInitialize = true;
        [SerializeField] private int spriteSortingOrder = 2;
        [SerializeField] private bool useGlobalSortingConfig = true;
        [SerializeField] private TankBattleGlobalConfig globalConfig;

        [Header("Networking")]
        [SerializeField, Min(0.01f)] private float networkPositionSmoothness = 14f;
        [SerializeField, Min(0.01f)] private float networkFacingSmoothness = 18f;
        [SerializeField, Min(0f)] private float networkSnapDistance = 0.65f;
        [SerializeField, Min(0f)] private float localAuthorityCorrectionMultiplier = 0.35f;
        [SerializeField, Min(0f)] private float localAuthoritySnapDistance = 1.25f;

        [Header("Diagnostics")]
        [SerializeField] private bool enableInputDiagnostics;
        [SerializeField, Min(0.1f)] private float inputDiagnosticsIntervalSeconds = 0.5f;

        private const float MovementInputNormalizeThreshold = 1f;

        private bool isAlive = true;
        private bool simulationAuthority = true;
        private bool captureInputForNetwork;
        private bool isBotParticipant;
        private bool inputEnabled = true;
        private int chairId = -1;
        private string userId;
        private TankBattlePlayerControlMode controlMode;

        private Vector2 estimatedVelocity;
        private float nextShootTime;
        private int remainingShots = 5;
        private bool pendingShootForNetwork;

        private float currentRotationAngle;
        private float targetRotationAngle;
        private float rotationSpeed = 550f;
        private Vector2 lastConfirmedInput = Vector2.right;
        private Vector2 previousFrameInput = Vector2.zero;
        private float firstKeyPressTime;
        private float lastInputActiveTime;

        private Vector2 networkMoveVector;
        private bool networkShootRequested;
        private bool hasAuthoritativeState;
        private Vector2 authoritativePosition;
        private float authoritativeFacingAngle;
        private Vector2 authoritativeVelocity;
        private int authoritativeRemainingShots;
        private float nextCaptureFailLogAt;
        private float nextCaptureSuccessLogAt;

        public bool IsAlive => isAlive;
        public bool IsBotParticipant => isBotParticipant;
        public string UserId => userId;
        public int ChairId => chairId;
        public TankBattlePlayerControlMode ControlMode => controlMode;
        public Vector2 Position => GetCurrentPosition();
        public Vector2 EstimatedVelocity => estimatedVelocity;
        public float FacingAngle => facingTransform != null ? facingTransform.localEulerAngles.z : transform.localEulerAngles.z;
        public int RemainingShots => remainingShots;
        private int MaxShotsPerPlayer => bulletSettings != null ? Mathf.Max(1, bulletSettings.maxShotsPerPlayer) : 5;

        public void Initialize(GameObject bulletPrefabToUse, TankBattleBulletSettings bulletSettingsToUse, bool enableInput)
        {
            Initialize(
                bulletPrefabToUse,
                bulletSettingsToUse,
                enableInput ? TankBattlePlayerControlMode.LocalHuman : TankBattlePlayerControlMode.SnapshotOnly,
                null,
                -1,
                false,
                simulationAuthority: true,
                captureInputForNetwork: false);
        }

        public void Initialize(
            GameObject bulletPrefabToUse,
            TankBattleBulletSettings bulletSettingsToUse,
            TankBattlePlayerControlMode mode,
            string userId,
            int chairId,
            bool isBotParticipant,
            bool simulationAuthority,
            bool captureInputForNetwork)
        {
            bulletPrefab = bulletPrefabToUse;
            bulletSettings = bulletSettingsToUse;
            controlMode = mode;
            this.userId = userId;
            this.chairId = chairId;
            this.isBotParticipant = isBotParticipant;
            this.simulationAuthority = simulationAuthority;
            this.captureInputForNetwork = captureInputForNetwork;
            inputEnabled = mode == TankBattlePlayerControlMode.LocalHuman || captureInputForNetwork || mode == TankBattlePlayerControlMode.Bot;

            remainingShots = bulletSettings != null ? bulletSettings.maxShotsPerPlayer : 5;
            authoritativeRemainingShots = remainingShots;
            networkMoveVector = Vector2.zero;
            networkShootRequested = false;
            pendingShootForNetwork = false;
            hasAuthoritativeState = false;

            EnsureComponentsAttached();
            CacheRequiredComponents();
            ConfigureRigidbodyForTopDown();
            ApplySortingOrderIfNeeded();
            RefreshInputEnabledState();
            RefreshPhysicsSimulationState();
            LogInputDiagnostic(
                $"Initialize user={this.userId}, chair={this.chairId}, mode={controlMode}, simAuthority={this.simulationAuthority}, capture={this.captureInputForNetwork}, inputEnabled={inputEnabled}, isBot={this.isBotParticipant}");
        }

        public void SetInputEnabled(bool enabled)
        {
            inputEnabled = enabled;
            if (playerInput != null)
            {
                playerInput.enabled = enabled;
            }
        }

        public void SetRuntimeControlMode(TankBattlePlayerControlMode mode, bool captureInputForNetwork, bool isBotParticipant)
        {
            controlMode = mode;
            this.captureInputForNetwork = captureInputForNetwork;
            this.isBotParticipant = isBotParticipant;
            RefreshInputEnabledState();
            RefreshPhysicsSimulationState();
            LogInputDiagnostic(
                $"SetRuntimeControlMode user={userId}, chair={chairId}, mode={controlMode}, capture={this.captureInputForNetwork}, inputEnabled={inputEnabled}, isBot={this.isBotParticipant}");
        }

        public void SetNetworkInput(Vector2 moveInput, bool shootPressed)
        {
            networkMoveVector = Vector2.ClampMagnitude(moveInput, 1f);
            if (shootPressed)
            {
                networkShootRequested = true;
            }
        }

        public void ApplyNetworkReportedState(Vector2 position, float facingAngle, Vector2 velocity)
        {
            authoritativePosition = position;
            authoritativeFacingAngle = facingAngle;
            authoritativeVelocity = velocity;

            bool hadAuthoritativeState = hasAuthoritativeState;
            hasAuthoritativeState = true;

            if (UsesAuthoritativeTransform() && !hadAuthoritativeState)
            {
                ApplyAuthoritativeTransformImmediate();
            }
        }

        public void ReturnShot(int amount = 1)
        {
            if (amount <= 0)
            {
                return;
            }

            remainingShots = Mathf.Clamp(remainingShots + amount, 0, MaxShotsPerPlayer);
            authoritativeRemainingShots = Mathf.Max(authoritativeRemainingShots, remainingShots);
        }

        public bool TryCaptureInputForNetwork(out Vector2 moveInput, out bool shootPressed)
        {
            moveInput = Vector2.zero;
            shootPressed = false;

            if (playerInput == null || !inputEnabled)
            {
                if (ShouldLogInputDiagnostic(ref nextCaptureFailLogAt))
                {
                    LogInputDiagnostic(
                        $"TryCaptureInputForNetwork failed user={userId}, chair={chairId}, mode={controlMode}, inputEnabled={inputEnabled}, hasInputComp={(playerInput != null)}");
                }
                return false;
            }

            moveInput = Vector2.ClampMagnitude(playerInput.MoveVector, 1f);
            shootPressed = playerInput.ShootPressed || pendingShootForNetwork;
            if (shootPressed)
            {
                pendingShootForNetwork = false;
            }
            if ((moveInput.sqrMagnitude > 0.001f || shootPressed) && ShouldLogInputDiagnostic(ref nextCaptureSuccessLogAt))
            {
                LogInputDiagnostic(
                    $"TryCaptureInputForNetwork success user={userId}, chair={chairId}, mode={controlMode}, move=({moveInput.x:0.00},{moveInput.y:0.00}), shoot={shootPressed}");
            }
            return true;
        }

        public void ApplyStateSnapshot(TankBattlePlayerStateSnapshot snapshot)
        {
            if (snapshot == null)
            {
                return;
            }

            isAlive = snapshot.isAlive;
            isBotParticipant = snapshot.isBot;
            authoritativePosition = new Vector2(snapshot.x, snapshot.y);
            authoritativeFacingAngle = snapshot.facingAngle;
            authoritativeVelocity = new Vector2(snapshot.velocityX, snapshot.velocityY);
            authoritativeRemainingShots = Mathf.Max(0, snapshot.remainingShots);
            bool hadAuthoritativeState = hasAuthoritativeState;
            hasAuthoritativeState = true;

            if (UsesAuthoritativeTransform())
            {
                estimatedVelocity = authoritativeVelocity;
                if (!hadAuthoritativeState)
                {
                    ApplyAuthoritativeTransformImmediate();
                }
            }

            ResetPhysicsVelocity();

            if (!isAlive)
            {
                estimatedVelocity = Vector2.zero;
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
            RefreshPhysicsSimulationState();
            IgnoreOtherPlayers();
        }

        private void FixedUpdate()
        {
            if (UsesAuthoritativeTransform())
            {
                estimatedVelocity = hasAuthoritativeState ? authoritativeVelocity : Vector2.zero;
                ResetPhysicsVelocity();
                return;
            }

            if (!CanSimulateMovement())
            {
                if (!UsesAuthoritativeTransform())
                {
                    estimatedVelocity = Vector2.zero;
                }
                return;
            }

            Vector2 input = GetNormalizedMoveInput();
            estimatedVelocity = input * moveSpeed;
            UpdateFacing(input);
            MoveByEstimatedVelocity();
            ApplyAuthoritativeCorrectionForLocalHuman(Time.fixedDeltaTime);
        }

        private void Update()
        {
            UpdateSnapshotPresentation(Time.deltaTime);

            if (!isAlive)
            {
                return;
            }

            bool shootRequested = false;
            if ((controlMode == TankBattlePlayerControlMode.LocalHuman || controlMode == TankBattlePlayerControlMode.Bot)
                && inputEnabled
                && playerInput != null)
            {
                shootRequested = playerInput.ShootPressed;
                if (controlMode == TankBattlePlayerControlMode.LocalHuman && playerInput.ShootPressed)
                {
                    pendingShootForNetwork = true;
                }
            }
            else if (controlMode == TankBattlePlayerControlMode.NetworkDriven || controlMode == TankBattlePlayerControlMode.SnapshotOnly)
            {
                shootRequested = networkShootRequested;
            }

            if (shootRequested && Time.time >= nextShootTime && remainingShots > 0)
            {
                Shoot();
            }

            networkShootRequested = false;
        }

        private void Shoot()
        {
            bool enforceAmmo = controlMode == TankBattlePlayerControlMode.LocalHuman || controlMode == TankBattlePlayerControlMode.Bot;
            if (bulletPrefab == null || (enforceAmmo && remainingShots <= 0))
            {
                return;
            }

            Vector3 spawnPosition = bulletSpawnPoint != null ? bulletSpawnPoint.position : transform.position;
            Vector2 shootDirection = facingTransform != null
                ? (Vector2)facingTransform.right
                : Vector2.right;

            if (IsBlockedByWall(spawnPosition, shootDirection))
            {
                return;
            }

            GameObject bulletObj = Instantiate(bulletPrefab, spawnPosition, Quaternion.identity);
            TankBattleBullet bullet = bulletObj.GetComponent<TankBattleBullet>();
            if (bullet != null)
            {
                bullet.Initialize(bulletSettings, shootDirection, gameObject, chairId);
            }

            remainingShots = Mathf.Max(0, remainingShots - 1);
            nextShootTime = Time.time + shootCooldown;

            PlayShootAnimation();

            if (playerInput != null)
            {
                playerInput.ResetMovementInput();
            }

            if (body != null)
            {
                body.linearVelocity = Vector2.zero;
            }
        }

        private void SetCurrentPosition(Vector2 position)
        {
            if (body != null)
            {
                body.position = position;
                if (body.simulated)
                {
                    return;
                }
            }

            transform.position = new Vector3(position.x, position.y, transform.position.z);
        }

        private bool IsBlockedByWall(Vector3 spawnPos, Vector2 direction)
        {
            float checkDistance = 0.6f;
            float checkRadius = 0.15f;

            Vector2 tankCenter = body != null ? body.position : (Vector2)transform.position;
            RaycastHit2D centerHit = Physics2D.CircleCast(tankCenter, checkRadius, direction, checkDistance);

            if (IsWallHit(centerHit))
            {
                return true;
            }

            Vector2 nozzlePos = spawnPos;
            RaycastHit2D nozzleHit = Physics2D.Raycast(nozzlePos, direction, 0.3f);

            if (IsWallHit(nozzleHit))
            {
                return true;
            }

            RaycastHit2D touchCheck = Physics2D.CircleCast(nozzlePos, 0.1f, direction, 0.05f);

            return IsWallHit(touchCheck);
        }

        private bool IsWallHit(RaycastHit2D hit)
        {
            if (hit.collider == null)
            {
                return false;
            }

            return hit.collider.CompareTag("Wall")
                || hit.collider.name.Contains("Bdr")
                || hit.collider.name.Contains("HW")
                || hit.collider.name.Contains("VW");
        }

        private void RefreshInputEnabledState()
        {
            inputEnabled = controlMode == TankBattlePlayerControlMode.LocalHuman
                || controlMode == TankBattlePlayerControlMode.Bot
                || captureInputForNetwork;

            if (playerInput != null)
            {
                playerInput.enabled = inputEnabled;
            }
        }

        private bool ShouldLogInputDiagnostic(ref float nextLogAt)
        {
            float now = Time.unscaledTime;
            if (now < nextLogAt)
            {
                return false;
            }

            nextLogAt = now + Mathf.Max(0.1f, inputDiagnosticsIntervalSeconds);
            return true;
        }

        private void LogInputDiagnostic(string message)
        {
            if (!enableInputDiagnostics)
            {
                return;
            }

            Debug.Log($"TankBattlePlayer[{name}] {message}");
        }

        private void PlayShootAnimation()
        {
            if (animator != null && !string.IsNullOrEmpty(shootAnimationState))
            {
                animator.Play(shootAnimationState, 0, 0f);
            }
        }

        private bool CanSimulateMovement()
        {
            if (!isAlive || body == null)
            {
                return false;
            }

            if (controlMode == TankBattlePlayerControlMode.LocalHuman)
            {
                return inputEnabled;
            }

            return simulationAuthority;
        }

        private void ApplyAuthoritativeCorrectionForLocalHuman(float deltaTime)
        {
            if (controlMode != TankBattlePlayerControlMode.LocalHuman || simulationAuthority || !hasAuthoritativeState)
            {
                return;
            }

            Vector2 currentPosition = Position;
            float snapDistance = Mathf.Max(networkSnapDistance, localAuthoritySnapDistance);
            float error = Vector2.Distance(currentPosition, authoritativePosition);
            if (error > snapDistance)
            {
                SetCurrentPosition(authoritativePosition);
            }
            else
            {
                float smoothness = networkPositionSmoothness * Mathf.Max(0f, localAuthorityCorrectionMultiplier);
                float t = GetInterpolationFactor(smoothness, deltaTime);
                SetCurrentPosition(Vector2.Lerp(currentPosition, authoritativePosition, t));
            }

            if (facingTransform != null)
            {
                float smoothness = networkFacingSmoothness * Mathf.Max(0f, localAuthorityCorrectionMultiplier);
                float t = GetInterpolationFactor(smoothness, deltaTime);
                float correctedAngle = Mathf.LerpAngle(FacingAngle, authoritativeFacingAngle, t);
                facingTransform.localRotation = Quaternion.Euler(0f, 0f, correctedAngle);
            }

            remainingShots = authoritativeRemainingShots;
        }

        private Vector2 GetNormalizedMoveInput()
        {
            Vector2 input = GetMoveInputByControlMode();
            if (input.sqrMagnitude > MovementInputNormalizeThreshold)
            {
                input.Normalize();
            }

            return input;
        }

        private Vector2 GetMoveInputByControlMode()
        {
            if (controlMode == TankBattlePlayerControlMode.LocalHuman || controlMode == TankBattlePlayerControlMode.Bot)
            {
                if (playerInput != null)
                {
                    return playerInput.MoveVector;
                }

                return Vector2.zero;
            }

            if (controlMode == TankBattlePlayerControlMode.NetworkDriven)
            {
                return networkMoveVector;
            }

            return Vector2.zero;
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

        private void UpdateSnapshotPresentation(float deltaTime)
        {
            if (!UsesAuthoritativeTransform() || !hasAuthoritativeState)
            {
                return;
            }

            float snapDistance = Mathf.Max(1.5f, networkSnapDistance);
            float error = Vector2.Distance(GetCurrentPosition(), authoritativePosition);
            if (error > snapDistance)
            {
                SetCurrentPosition(authoritativePosition);
            }
            else
            {
                float t = GetInterpolationFactor(networkPositionSmoothness, deltaTime);
                SetCurrentPosition(Vector2.Lerp(GetCurrentPosition(), authoritativePosition, t));
            }

            if (facingTransform != null)
            {
                float t = GetInterpolationFactor(networkFacingSmoothness, deltaTime);
                float nextAngle = Mathf.LerpAngle(FacingAngle, authoritativeFacingAngle, t);
                facingTransform.localRotation = Quaternion.Euler(0f, 0f, nextAngle);
            }

            estimatedVelocity = authoritativeVelocity;
            remainingShots = authoritativeRemainingShots;
            ResetPhysicsVelocity();
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

            if (animator == null)
            {
                animator = GetComponent<Animator>();
            }

            if (facingTransform == null)
            {
                facingTransform = transform;
            }

            if (bulletSpawnPoint == null)
            {
                Transform nozzle = transform.Find("Nozzle");
                bulletSpawnPoint = nozzle != null ? nozzle : transform;
            }

            if (playerInput == null)
            {
                playerInput = GetComponent<TankBattlePlayerInput>();
            }
        }

        private bool UsesAuthoritativeTransform()
        {
            return controlMode == TankBattlePlayerControlMode.SnapshotOnly;
        }

        private void ApplyAuthoritativeTransformImmediate()
        {
            SetCurrentPosition(authoritativePosition);
            if (facingTransform != null)
            {
                facingTransform.localRotation = Quaternion.Euler(0f, 0f, authoritativeFacingAngle);
            }

            remainingShots = authoritativeRemainingShots;
        }

        private Vector2 GetCurrentPosition()
        {
            if (body != null && body.simulated)
            {
                return body.position;
            }

            return transform.position;
        }

        private void ResetPhysicsVelocity()
        {
            if (body == null)
            {
                return;
            }

            body.linearVelocity = Vector2.zero;
            body.angularVelocity = 0f;
        }

        private static float GetInterpolationFactor(float smoothness, float deltaTime)
        {
            if (smoothness <= 0f)
            {
                return 1f;
            }

            return 1f - Mathf.Exp(-smoothness * Mathf.Max(0f, deltaTime));
        }

        private bool ShouldSimulateLocally()
        {
            if (controlMode == TankBattlePlayerControlMode.LocalHuman)
            {
                return true;
            }

            if (controlMode == TankBattlePlayerControlMode.SnapshotOnly)
            {
                return false;
            }

            return simulationAuthority;
        }

        private void RefreshPhysicsSimulationState()
        {
            if (body == null)
            {
                return;
            }

            bool localSimulation = ShouldSimulateLocally();

            // Keep snapshot-driven players collision-active for bullet hits while
            // preventing local physics simulation from driving their movement.
            if (!body.simulated)
            {
                body.simulated = true;
            }

            RigidbodyType2D targetBodyType = localSimulation
                ? RigidbodyType2D.Dynamic
                : RigidbodyType2D.Kinematic;
            if (body.bodyType != targetBodyType)
            {
                body.bodyType = targetBodyType;
            }

            body.gravityScale = 0f;
            body.constraints = RigidbodyConstraints2D.FreezeRotation;
            body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

            if (!localSimulation)
            {
                body.position = (Vector2)transform.position;
            }

            ResetPhysicsVelocity();
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
                    resolvedSortingOrder = config.PlayerSortingOrder;
                    resolvedLayerName = config.PlayerSortingLayerName;
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
            if (hitCollider == null)
            {
                return;
            }

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

