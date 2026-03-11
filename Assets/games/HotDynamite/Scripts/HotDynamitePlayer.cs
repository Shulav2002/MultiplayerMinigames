using UnityEngine;
using HotDynamite.Networking;

namespace HotDynamite
{
    [RequireComponent(typeof(Rigidbody2D), typeof(Collider2D))]
    public class HotDynamitePlayer : MonoBehaviour
    {
        [Tooltip("Input provider used when this player is controlled by a human.")]
        [SerializeField] private HotDynamitePlayerInput humanInput;
        [Tooltip("Input provider used when this player is controlled by AI.")]
        [SerializeField] private HotDynamiteBotController botInput;
        [Tooltip("Rigidbody used for 2D movement and collision response.")]
        [SerializeField] private Rigidbody2D body;
        [Tooltip("Primary collider used for contact, bomb passing, and bounce interactions.")]
        [SerializeField] private Collider2D hitCollider;
        [Tooltip("Bomb visual prefab instantiated as a child when this player is the bomb holder.")]
        [SerializeField] private GameObject bombPrefab;
        [Tooltip("Local position offset for the spawned bomb visual.")]
        [SerializeField] private Vector3 bombLocalOffset = new Vector3(0f, 0.9f, 0f);
        [Header("Facing")]
        [Tooltip("Sprite renderer used to determine the transform to rotate for facing direction.")]
        [SerializeField] private SpriteRenderer facingRenderer;
        [Tooltip("Transform rotated toward movement direction. Falls back to renderer transform if not assigned.")]
        [SerializeField] private Transform facingTransform;
        [Tooltip("Minimum movement magnitude required before updating facing rotation.")]
        [SerializeField] private float facingInputThreshold = 0.05f;
        [Tooltip("Extra angle offset (degrees) applied on top of movement-facing rotation.")]
        [SerializeField] private float facingAngleOffset = 0f;
        [Header("Animation")]
        [Tooltip("Animator that drives run and appearance animation states.")]
        [SerializeField] private Animator runAnimator;
        [Tooltip("Color set used for player visual variants.")]
        [SerializeField] private Color[] appearanceColors;
        [Tooltip("Minimum movement magnitude required for run animation to be considered active.")]
        [SerializeField] private float runAnimationThreshold = 0.05f;
        [Tooltip("Animator playback speed while running.")]
        [SerializeField] private float runAnimationSpeed = 1f;
        [Tooltip("Bomb starts animating when remaining fuse time is at or below this value.")]
        [SerializeField] private float bombAnimationCountdownSeconds = 3f;
        [Header("Appearance")]
        [Tooltip("Run animation clips used as appearance variants. Chosen by appearance index.")]
        [SerializeField] private AnimationClip[] runAnimationVariants;

        private const float MovementInputNormalizeThreshold = 1f;
        private const float VectorEpsilonSqr = 0.0001f;
        private HotDynamiteGameManager manager;
        private HotDynamiteGameSettings settings;
        private const float DefaultPlayerBounceSpeed = 4.8f;
        private const float DefaultPlayerBounceMaxSpeed = 6.5f;
        private const float DefaultPlayerBounceDecay = 8f;
        private const float DefaultPlayerBounceCooldownSeconds = 0.16f;
        private const float DefaultPlayerBounceInputSuppressionSeconds = 0.08f;
        private bool isHuman;
        private bool hasBomb;
        private bool isAlive = true;
        private Vector2 estimatedVelocity;
        private Vector2 networkMoveVector;
        private GameObject bombInstance;
        private float temporarySpeedMultiplier = 1f;
        private float temporarySpeedUntil = -1f;
        private int appearanceIndex;
        private AnimatorOverrideController runAnimatorOverride;
        private Animator bombAnimator;
        private Vector2 bounceVelocity;
        private float bounceCooldownUntil = -1f;
        private float bounceInputSuppressionUntil = -1f;
        private string userId;
        private int chairId = -1;
        private bool isBotParticipant;
        private HotDynamitePlayerControlMode controlMode;

        public bool IsAlive => isAlive;
        public bool HasBomb => hasBomb;
        public HotDynamitePlayerInput HumanInput => humanInput;
        public Vector2 Position => body != null ? body.position : (Vector2)transform.position;
        public Vector2 EstimatedVelocity => estimatedVelocity;
        public string UserId => userId;
        public int ChairId => chairId;
        public bool IsBotParticipant => isBotParticipant;
        public HotDynamitePlayerControlMode ControlMode => controlMode;
        public int AppearanceIndex => appearanceIndex;
        public float FacingAngle => facingTransform != null ? facingTransform.localEulerAngles.z : transform.localEulerAngles.z;
        private float PlayerBounceSpeed => settings != null ? settings.PlayerBounceSpeed : DefaultPlayerBounceSpeed;
        private float PlayerBounceMaxSpeed => settings != null ? settings.PlayerBounceMaxSpeed : DefaultPlayerBounceMaxSpeed;
        private float PlayerBounceDecay => settings != null ? settings.PlayerBounceDecay : DefaultPlayerBounceDecay;
        private float PlayerBounceCooldownSeconds => settings != null ? settings.PlayerBounceCooldownSeconds : DefaultPlayerBounceCooldownSeconds;
        private float PlayerBounceInputSuppressionSeconds => settings != null
            ? settings.PlayerBounceInputSuppressionSeconds
            : DefaultPlayerBounceInputSuppressionSeconds;

        public void Initialize(HotDynamiteGameManager manager, HotDynamiteGameSettings settings, bool isHuman)
        {
            Initialize(manager, settings, isHuman ? HotDynamitePlayerControlMode.LocalHuman : HotDynamitePlayerControlMode.Bot);
        }

        public void Initialize(
            HotDynamiteGameManager manager,
            HotDynamiteGameSettings settings,
            HotDynamitePlayerControlMode controlMode,
            string userId = null,
            int chairId = -1,
            bool isBotParticipant = false)
        {
            this.manager = manager;
            this.settings = settings;
            this.controlMode = controlMode;
            this.isHuman = controlMode == HotDynamitePlayerControlMode.LocalHuman;
            this.userId = userId;
            this.chairId = chairId;
            this.isBotParticipant = isBotParticipant;

            CacheRequiredComponents();
            ApplyAppearanceVariant();
            ConfigureInputSources();
            ResetForNewRound();
            UpdateBombVisual();
            RefreshPhysicsSimulationState();
        }

        private void FixedUpdate()
        {
            if (!CanSimulateMovement())
            {
                estimatedVelocity = Vector2.zero;
                return;
            }

            Vector2 input = GetNormalizedMoveInput();
            estimatedVelocity = CalculateEstimatedVelocity(input);
            UpdateRunAnimation(input);
            UpdateFacing(input);
            MoveByEstimatedVelocity();
            DecayBounceVelocity();
        }

        private void Update()
        {
            UpdateBombAnimationState();
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            TryPassBomb(other);
        }

        private void OnTriggerStay2D(Collider2D other)
        {
            TryPassBomb(other);
        }

        private void OnCollisionEnter2D(Collision2D collision)
        {
            TryPassBomb(collision != null ? collision.collider : null);
            TryBounceOnPlayerCollision(collision);
        }

        private void OnCollisionStay2D(Collision2D collision)
        {
            TryPassBomb(collision != null ? collision.collider : null);
        }

        private void TryPassBomb(Collider2D other)
        {
            if (!hasBomb || !isAlive || manager == null || other == null)
            {
                return;
            }

            HotDynamitePlayer otherPlayer = other.GetComponentInParent<HotDynamitePlayer>();
            if (otherPlayer == null || otherPlayer == this || !otherPlayer.IsAlive)
            {
                return;
            }

            manager.RequestBombPass(this, otherPlayer);
        }

        public void SetHasBomb(bool value)
        {
            hasBomb = value;
            UpdateBombVisual();
        }

        public void ApplyTemporarySpeedMultiplier(float multiplier, float durationSeconds)
        {
            if (!isAlive)
            {
                return;
            }

            temporarySpeedMultiplier = Mathf.Max(0.01f, multiplier);
            temporarySpeedUntil = Time.time + Mathf.Max(0f, durationSeconds);
        }

        public void SetAppearanceIndex(int index)
        {
            appearanceIndex = Mathf.Max(0, index);
            ApplyAppearanceVariant();
        }

        public void SetNetworkMoveInput(Vector2 moveInput)
        {
            networkMoveVector = Vector2.ClampMagnitude(moveInput, 1f);
        }

        public void ApplyStateSnapshot(HotDynamitePlayerStateSnapshot snapshot)
        {
            if (snapshot == null)
            {
                return;
            }

            if (appearanceIndex != snapshot.appearanceIndex)
            {
                SetAppearanceIndex(snapshot.appearanceIndex);
            }

            isBotParticipant = snapshot.isBot;
            isAlive = snapshot.isAlive;
            hasBomb = snapshot.hasBomb;
            estimatedVelocity = new Vector2(snapshot.velocityX, snapshot.velocityY);

            if (body != null)
            {
                body.position = new Vector2(snapshot.x, snapshot.y);
            }
            else
            {
                transform.position = new Vector3(snapshot.x, snapshot.y, transform.position.z);
            }

            if (facingTransform != null)
            {
                facingTransform.localRotation = Quaternion.Euler(0f, 0f, snapshot.facingAngle);
            }

            if (body != null)
            {
                body.linearVelocity = Vector2.zero;
            }

            UpdateBombVisual();
            UpdateRunAnimation(estimatedVelocity);
            gameObject.SetActive(snapshot.isAlive);
        }

        public Color GetAppearanceColor()
        {
            if (appearanceColors == null || appearanceColors.Length == 0)
            {
                return Color.white;
            }

            int colorIndex = appearanceIndex % appearanceColors.Length;
            return appearanceColors[colorIndex];
        }

        public void Eliminate()
        {
            isAlive = false;
            hasBomb = false;
            UpdateBombVisual();
            if (humanInput != null)
            {
                humanInput.enabled = false;
            }

            if (botInput != null)
            {
                botInput.enabled = false;
            }

            if (body != null)
            {
                body.linearVelocity = Vector2.zero;
            }

            estimatedVelocity = Vector2.zero;
            ResetTemporarySpeed();
            ResetBounceState();
            UpdateRunAnimation(Vector2.zero);

            gameObject.SetActive(false);
        }

        private void TryBounceOnPlayerCollision(Collision2D collision)
        {
            if (collision == null || !isAlive || body == null || Time.time < bounceCooldownUntil)
            {
                return;
            }

            HotDynamitePlayer otherPlayer = collision.collider != null
                ? collision.collider.GetComponentInParent<HotDynamitePlayer>()
                : null;
            if (otherPlayer == null || otherPlayer == this || !otherPlayer.isAlive || otherPlayer.body == null)
            {
                return;
            }

            // Process each player pair once per contact event.
            if (GetInstanceID() > otherPlayer.GetInstanceID() || Time.time < otherPlayer.bounceCooldownUntil)
            {
                return;
            }

            Vector2 separation = otherPlayer.Position - Position;
            Vector2 direction = separation.sqrMagnitude > VectorEpsilonSqr
                ? separation.normalized
                : (collision.relativeVelocity.sqrMagnitude > VectorEpsilonSqr ? collision.relativeVelocity.normalized : Vector2.right);

            ApplyBounceImpulse(-direction);
            otherPlayer.ApplyBounceImpulse(direction);
            manager.NotifyPlayerCollision();
        }

        private void ApplyBounceImpulse(Vector2 direction)
        {
            Vector2 normalized = direction.sqrMagnitude > VectorEpsilonSqr ? direction.normalized : Vector2.zero;
            bounceVelocity += normalized * Mathf.Max(0f, PlayerBounceSpeed);
            float maxBounce = Mathf.Max(0f, PlayerBounceMaxSpeed);
            if (bounceVelocity.sqrMagnitude > maxBounce * maxBounce)
            {
                bounceVelocity = bounceVelocity.normalized * maxBounce;
            }

            bounceCooldownUntil = Time.time + Mathf.Max(0f, PlayerBounceCooldownSeconds);
            bounceInputSuppressionUntil = Time.time + Mathf.Max(0f, PlayerBounceInputSuppressionSeconds);
        }

        private bool CanSimulateMovement()
        {
            return isAlive
                && settings != null
                && body != null
                && manager != null
                && manager.LocalSimulationEnabled
                && !manager.IsMatchStarting;
        }

        private Vector2 GetNormalizedMoveInput()
        {
            Vector2 input = ReadMoveInput();
            if (input.sqrMagnitude > MovementInputNormalizeThreshold)
            {
                input.Normalize();
            }

            return input;
        }

        private Vector2 CalculateEstimatedVelocity(Vector2 input)
        {
            Vector2 baseVelocity = Time.time < bounceInputSuppressionUntil
                ? Vector2.zero
                : input * GetCurrentMoveSpeed();
            return baseVelocity + bounceVelocity;
        }

        private float GetCurrentMoveSpeed()
        {
            if (settings == null)
            {
                return 0f;
            }

            float speed = settings.MoveSpeed;
            if (hasBomb)
            {
                speed *= settings.BombHolderSpeedMultiplier;
            }

            return speed * GetTemporarySpeedMultiplier();
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

        private void DecayBounceVelocity()
        {
            if (bounceVelocity.sqrMagnitude > VectorEpsilonSqr)
            {
                bounceVelocity = Vector2.MoveTowards(
                    bounceVelocity,
                    Vector2.zero,
                    Mathf.Max(0f, PlayerBounceDecay) * Time.fixedDeltaTime);
            }
            else
            {
                bounceVelocity = Vector2.zero;
            }
        }

        private void UpdateFacing(Vector2 movementInput)
        {
            if (facingTransform == null || movementInput.sqrMagnitude < facingInputThreshold * facingInputThreshold)
            {
                return;
            }

            float facingAngle = Mathf.Atan2(movementInput.y, movementInput.x) * Mathf.Rad2Deg + facingAngleOffset;
            facingTransform.localRotation = Quaternion.Euler(0f, 0f, facingAngle);
        }

        private void UpdateRunAnimation(Vector2 movementInput)
        {
            if (runAnimator == null)
            {
                return;
            }

            bool isMoving = movementInput.sqrMagnitude >= runAnimationThreshold * runAnimationThreshold;
            runAnimator.speed = isMoving ? runAnimationSpeed : 0f;
        }

        private void ApplyAppearanceVariant()
        {
            if (runAnimator == null || runAnimationVariants == null || runAnimationVariants.Length == 0)
            {
                return;
            }

            AnimationClip selectedClip = GetSelectedAppearanceClip();
            if (selectedClip == null)
            {
                return;
            }

            RuntimeAnimatorController controller = runAnimator.runtimeAnimatorController;
            if (controller == null)
            {
                return;
            }

            RuntimeAnimatorController baseController = controller;
            if (controller is AnimatorOverrideController existingOverride)
            {
                baseController = existingOverride.runtimeAnimatorController;
            }

            if (runAnimatorOverride == null || runAnimatorOverride.runtimeAnimatorController != baseController)
            {
                runAnimatorOverride = new AnimatorOverrideController(baseController);
            }

            runAnimatorOverride["Run"] = selectedClip;
            runAnimator.runtimeAnimatorController = runAnimatorOverride;
        }

        private AnimationClip GetSelectedAppearanceClip()
        {
            if (runAnimationVariants == null || runAnimationVariants.Length == 0)
            {
                return null;
            }

            return runAnimationVariants[appearanceIndex % runAnimationVariants.Length];
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

            if (hitCollider != null && hitCollider.isTrigger)
            {
                hitCollider.isTrigger = false;
            }

            if (facingRenderer == null)
            {
                facingRenderer = GetComponent<SpriteRenderer>();
            }

            if (facingTransform == null)
            {
                facingTransform = facingRenderer != null ? facingRenderer.transform : transform;
            }

            if (runAnimator == null)
            {
                runAnimator = GetComponent<Animator>();
            }
        }

        private void ConfigureInputSources()
        {
            if (humanInput != null)
            {
                humanInput.enabled = controlMode == HotDynamitePlayerControlMode.LocalHuman;
            }

            if (botInput != null)
            {
                bool enableBot = controlMode == HotDynamitePlayerControlMode.Bot;
                botInput.enabled = enableBot;
                if (enableBot)
                {
                    botInput.Initialize(manager, settings, this);
                }
            }
        }

        private void ResetForNewRound()
        {
            isAlive = true;
            hasBomb = false;
            networkMoveVector = Vector2.zero;
            ResetTemporarySpeed();
            ResetBounceState();
        }

        private void ResetTemporarySpeed()
        {
            temporarySpeedMultiplier = 1f;
            temporarySpeedUntil = -1f;
        }

        private void ResetBounceState()
        {
            bounceVelocity = Vector2.zero;
            bounceCooldownUntil = -1f;
            bounceInputSuppressionUntil = -1f;
        }

        private Vector2 ReadMoveInput()
        {
            if (controlMode == HotDynamitePlayerControlMode.LocalHuman && humanInput != null)
            {
                return humanInput.MoveVector;
            }

            if (controlMode == HotDynamitePlayerControlMode.Bot && botInput != null)
            {
                return botInput.MoveVector;
            }

            if (controlMode == HotDynamitePlayerControlMode.NetworkDriven)
            {
                return networkMoveVector;
            }

            return Vector2.zero;
        }

        private float GetTemporarySpeedMultiplier()
        {
            if (Time.time < temporarySpeedUntil)
            {
                return temporarySpeedMultiplier;
            }

            ResetTemporarySpeed();
            return 1f;
        }

        private void UpdateBombVisual()
        {
            if (!hasBomb)
            {
                DestroyBombVisual();
                return;
            }

            EnsureBombVisual();
            UpdateBombAnimationState();
        }

        private void EnsureBombVisual()
        {
            if (bombInstance != null || bombPrefab == null)
            {
                return;
            }

            bombInstance = Instantiate(bombPrefab, transform);
            bombInstance.transform.localPosition = bombLocalOffset;
            bombInstance.transform.localRotation = Quaternion.identity;
            bombAnimator = bombInstance.GetComponent<Animator>();
            if (bombAnimator != null)
            {
                bombAnimator.enabled = false;
            }
        }

        private void DestroyBombVisual()
        {
            if (bombInstance == null)
            {
                return;
            }

            Destroy(bombInstance);
            bombInstance = null;
            bombAnimator = null;
        }

        private void UpdateBombAnimationState()
        {
            if (bombAnimator == null)
            {
                return;
            }

            bool shouldAnimate = hasBomb
                && manager != null
                && manager.BombTimeRemaining <= Mathf.Max(0f, bombAnimationCountdownSeconds);

            if (bombAnimator.enabled == shouldAnimate)
            {
                return;
            }

            bombAnimator.enabled = shouldAnimate;

            if (shouldAnimate)
            {
                bombAnimator.Play(0, 0, 0f);
            }
        }

        private void OnDestroy()
        {
            DestroyBombVisual();
        }

        private void RefreshPhysicsSimulationState()
        {
            if (body == null || manager == null)
            {
                return;
            }

            body.simulated = manager.LocalSimulationEnabled;
        }
    }
}
