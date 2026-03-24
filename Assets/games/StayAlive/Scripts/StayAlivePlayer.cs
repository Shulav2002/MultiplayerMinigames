using UnityEngine;

namespace StayAlive
{
    [RequireComponent(typeof(Rigidbody2D), typeof(Collider2D))]
    public class StayAlivePlayer : MonoBehaviour
    {
        private const float MovementInputNormalizeThreshold = 1f;
        private const float VectorEpsilonSqr = 0.0001f;
        [SerializeField] private StayAlivePlayerInput humanInput;
        [SerializeField] private StayAliveBotController botInput;
        [SerializeField] private Rigidbody2D body;
        [SerializeField] private Collider2D hitCollider;
        [Header("Facing")]
        [SerializeField] private SpriteRenderer facingRenderer;
        [SerializeField] private Transform facingTransform;
        [SerializeField] private float facingInputThreshold = 0.05f;
        [SerializeField] private float facingAngleOffset = 0f;
        [Header("Animation")]
        [SerializeField] private Animator runAnimator;
        [SerializeField] private Color[] appearanceColors;
        [SerializeField] private float runAnimationThreshold = 0.05f;
        [SerializeField] private float runAnimationSpeed = 1f;
        [SerializeField] private AnimationClip[] runAnimationVariants;
        [Header("Local Player Indicator")]
        [SerializeField] private SpriteRenderer localPlayerIndicatorRenderer;
        [SerializeField] private Sprite localPlayerIndicatorSprite;
        [SerializeField] private Vector3 localPlayerIndicatorOffset = new Vector3(0f, 1.65f, 0f);
        [SerializeField] private Vector3 localPlayerIndicatorScale = Vector3.one;
        [SerializeField, Min(0f)] private float localPlayerIndicatorPulseAmount = 0.14f;
        [SerializeField, Min(0f)] private float localPlayerIndicatorPulseSpeed = 3.25f;
        [SerializeField] private int localPlayerIndicatorSortingOrderOffset = 20;

        private StayAliveGameManager manager;
        private bool isHuman;
        private bool isAlive = true;
        private int chairId = -1;
        private int appearanceIndex;
        private Vector2 estimatedVelocity;
        private Vector2 bounceVelocity;
        private float bounceCooldownUntil = -1f;
        private float bounceInputSuppressionUntil = -1f;
        private AnimatorOverrideController runAnimatorOverride;
        private Transform localPlayerIndicatorTransform;
        private Vector3 localPlayerIndicatorBaseScale = Vector3.one;

        public bool IsAlive => isAlive;
        public bool IsHuman => isHuman;
        public int ChairId => chairId;
        public Vector2 Position => GetCurrentPosition();
        public Vector2 EstimatedVelocity => estimatedVelocity;
        public Bounds WorldBounds => hitCollider != null ? hitCollider.bounds : new Bounds(transform.position, Vector3.zero);

        public void SetAppearanceIndex(int index)
        {
            appearanceIndex = Mathf.Max(0, index);
            ApplyAppearanceVariant();
        }

        public Color GetAppearanceColor()
        {
            if (appearanceColors == null || appearanceColors.Length == 0)
            {
                return Color.white;
            }

            return appearanceColors[appearanceIndex % appearanceColors.Length];
        }

        public void Initialize(StayAliveGameManager manager, bool isHuman, int chairId)
        {
            this.manager = manager;
            this.isHuman = isHuman;
            this.chairId = chairId;

            CacheRequiredComponents();
            ApplyAppearanceVariant();
            ConfigureInputSources();
            RefreshLocalPlayerIndicator();

            if (body != null)
            {
                body.gravityScale = 0f;
                body.constraints = RigidbodyConstraints2D.FreezeRotation;
                body.simulated = true;
                body.linearVelocity = Vector2.zero;
                body.angularVelocity = 0f;
            }

            if (hitCollider != null && hitCollider.isTrigger)
            {
                hitCollider.isTrigger = false;
            }

            isAlive = true;
            estimatedVelocity = Vector2.zero;
            ResetBounceState();
            gameObject.SetActive(true);
            SetLocalPlayerIndicatorVisible(isHuman);
            UpdateRunAnimation(Vector2.zero);
        }

        private void FixedUpdate()
        {
            if (!CanSimulateMovement())
            {
                estimatedVelocity = Vector2.zero;
                if (body != null)
                {
                    body.linearVelocity = Vector2.zero;
                }
                return;
            }

            Vector2 input = GetNormalizedMoveInput();
            estimatedVelocity = CalculateEstimatedVelocity(input);
            UpdateRunAnimation(input);
            UpdateFacing(input);
            ApplyPhysicsMovement();
            ClampToBoardBounds();
            DecayBounceVelocity();
        }

        private void Update()
        {
            UpdateLocalPlayerIndicator();
        }

        private void OnCollisionEnter2D(Collision2D collision)
        {
            TryBounceOnPlayerCollision(collision);
        }

        private void OnCollisionStay2D(Collision2D collision)
        {
            TryBounceOnPlayerCollision(collision);
        }

        public void Eliminate()
        {
            if (!isAlive)
            {
                return;
            }

            isAlive = false;
            estimatedVelocity = Vector2.zero;
            ResetBounceState();

            if (body != null)
            {
                body.linearVelocity = Vector2.zero;
                body.angularVelocity = 0f;
            }

            if (humanInput != null)
            {
                humanInput.enabled = false;
            }

            if (botInput != null)
            {
                botInput.enabled = false;
            }

            SetLocalPlayerIndicatorVisible(false);
            gameObject.SetActive(false);
        }

        private bool CanSimulateMovement()
        {
            return isAlive && manager != null && body != null && !manager.IsMatchOver;
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

            if (humanInput == null)
            {
                humanInput = GetComponent<StayAlivePlayerInput>();
            }

            if (botInput == null)
            {
                botInput = GetComponent<StayAliveBotController>();
            }
        }

        private void ConfigureInputSources()
        {
            if (humanInput != null)
            {
                humanInput.enabled = isHuman;
            }

            if (!isHuman && botInput == null)
            {
                botInput = gameObject.AddComponent<StayAliveBotController>();
            }

            if (botInput != null)
            {
                botInput.enabled = !isHuman;
                if (!isHuman)
                {
                    botInput.Initialize(manager, this);
                }
            }
        }

        private Vector2 GetNormalizedMoveInput()
        {
            Vector2 input = Vector2.zero;
            if (isHuman)
            {
                if (humanInput != null)
                {
                    input = humanInput.MoveVector;
                }
            }
            else if (botInput != null)
            {
                input = botInput.MoveVector;
            }

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
                : input * Mathf.Max(0f, manager.MoveSpeed);
            return baseVelocity + bounceVelocity;
        }

        private void ApplyPhysicsMovement()
        {
            if (body == null || manager == null)
            {
                return;
            }

            body.linearVelocity = estimatedVelocity;
        }

        private void DecayBounceVelocity()
        {
            if (bounceVelocity.sqrMagnitude > VectorEpsilonSqr)
            {
                bounceVelocity = Vector2.MoveTowards(
                    bounceVelocity,
                    Vector2.zero,
                    Mathf.Max(0f, manager.PlayerBounceDecay) * Time.fixedDeltaTime);
            }
            else
            {
                bounceVelocity = Vector2.zero;
            }
        }

        private void ClampToBoardBounds()
        {
            if (body == null || manager == null)
            {
                return;
            }

            Vector2 currentPosition = body.position;
            Vector2 clampedPosition = manager.ClampToBoard(currentPosition);
            if ((clampedPosition - currentPosition).sqrMagnitude <= 0.0001f)
            {
                return;
            }

            Vector2 clampedVelocity = body.linearVelocity;
            if (!Mathf.Approximately(clampedPosition.x, currentPosition.x))
            {
                clampedVelocity.x = 0f;
            }

            if (!Mathf.Approximately(clampedPosition.y, currentPosition.y))
            {
                clampedVelocity.y = 0f;
            }

            body.position = clampedPosition;
            body.linearVelocity = clampedVelocity;
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

            AnimationClip selectedClip = runAnimationVariants[appearanceIndex % runAnimationVariants.Length];
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

        private void TryBounceOnPlayerCollision(Collision2D collision)
        {
            if (collision == null || !isAlive || body == null || manager == null || Time.time < bounceCooldownUntil)
            {
                return;
            }

            StayAlivePlayer otherPlayer = collision.collider != null
                ? collision.collider.GetComponentInParent<StayAlivePlayer>()
                : null;
            if (otherPlayer == null || otherPlayer == this || !otherPlayer.isAlive || otherPlayer.body == null)
            {
                return;
            }

            // Resolve each player pair once so both players receive the same rebound strength.
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
        }

        private void ApplyBounceImpulse(Vector2 direction)
        {
            Vector2 normalized = direction.sqrMagnitude > VectorEpsilonSqr ? direction.normalized : Vector2.zero;
            bounceVelocity += normalized * Mathf.Max(0f, manager.PlayerBounceSpeed);

            float maxBounce = Mathf.Max(0f, manager.PlayerBounceMaxSpeed);
            if (bounceVelocity.sqrMagnitude > maxBounce * maxBounce)
            {
                bounceVelocity = bounceVelocity.normalized * maxBounce;
            }

            if (body != null)
            {
                // Apply the recoil immediately so the bounce is visible on the contact frame.
                body.linearVelocity = bounceVelocity;
            }

            bounceCooldownUntil = Time.time + Mathf.Max(0f, manager.PlayerBounceCooldownSeconds);
            bounceInputSuppressionUntil = Time.time + Mathf.Max(0f, manager.PlayerBounceInputSuppressionSeconds);
        }

        private void ResetBounceState()
        {
            bounceVelocity = Vector2.zero;
            bounceCooldownUntil = -1f;
            bounceInputSuppressionUntil = -1f;
        }

        private void RefreshLocalPlayerIndicator()
        {
            EnsureLocalPlayerIndicator();

            if (localPlayerIndicatorTransform != null)
            {
                localPlayerIndicatorTransform.localPosition = localPlayerIndicatorOffset;
            }

            SetLocalPlayerIndicatorVisible(isAlive && isHuman);
        }

        private void EnsureLocalPlayerIndicator()
        {
            if (localPlayerIndicatorRenderer == null)
            {
                Transform existingChild = transform.Find("LocalPlayerIndicator");
                if (existingChild == null)
                {
                    existingChild = transform.Find("localPOinter");
                }

                if (existingChild != null)
                {
                    localPlayerIndicatorRenderer = existingChild.GetComponent<SpriteRenderer>();
                }
            }

            if (localPlayerIndicatorRenderer == null && localPlayerIndicatorSprite != null)
            {
                GameObject indicatorObject = new GameObject("LocalPlayerIndicator");
                indicatorObject.transform.SetParent(transform, false);
                localPlayerIndicatorRenderer = indicatorObject.AddComponent<SpriteRenderer>();
            }

            if (localPlayerIndicatorRenderer == null)
            {
                localPlayerIndicatorTransform = null;
                return;
            }

            localPlayerIndicatorTransform = localPlayerIndicatorRenderer.transform;
            localPlayerIndicatorTransform.SetParent(transform, false);
            localPlayerIndicatorTransform.localPosition = localPlayerIndicatorOffset;
            Vector3 authoredIndicatorScale = localPlayerIndicatorTransform.localScale;

            if (localPlayerIndicatorSprite != null)
            {
                localPlayerIndicatorRenderer.sprite = localPlayerIndicatorSprite;
            }

            if (localPlayerIndicatorRenderer.sprite == null)
            {
                localPlayerIndicatorTransform = null;
                return;
            }

            localPlayerIndicatorRenderer.color = Color.white;
            localPlayerIndicatorRenderer.drawMode = SpriteDrawMode.Simple;
            if (facingRenderer != null)
            {
                localPlayerIndicatorRenderer.sortingLayerID = facingRenderer.sortingLayerID;
                localPlayerIndicatorRenderer.sortingOrder = facingRenderer.sortingOrder + localPlayerIndicatorSortingOrderOffset;
            }

            localPlayerIndicatorBaseScale = new Vector3(
                authoredIndicatorScale.x * localPlayerIndicatorScale.x,
                authoredIndicatorScale.y * localPlayerIndicatorScale.y,
                authoredIndicatorScale.z * localPlayerIndicatorScale.z);
            localPlayerIndicatorTransform.localScale = localPlayerIndicatorBaseScale;
        }

        private void UpdateLocalPlayerIndicator()
        {
            bool shouldShow = isHuman && isAlive;
            SetLocalPlayerIndicatorVisible(shouldShow);

            if (!shouldShow || localPlayerIndicatorTransform == null || localPlayerIndicatorRenderer == null || !localPlayerIndicatorRenderer.enabled)
            {
                return;
            }

            localPlayerIndicatorTransform.localPosition = localPlayerIndicatorOffset;

            if (localPlayerIndicatorPulseSpeed <= 0f || localPlayerIndicatorPulseAmount <= 0f)
            {
                localPlayerIndicatorTransform.localScale = localPlayerIndicatorBaseScale;
                return;
            }

            float pulse = (Mathf.Sin(Time.unscaledTime * localPlayerIndicatorPulseSpeed * Mathf.PI * 2f) + 1f) * 0.5f;
            float scaleMultiplier = 1f + (localPlayerIndicatorPulseAmount * pulse);
            localPlayerIndicatorTransform.localScale = localPlayerIndicatorBaseScale * scaleMultiplier;
        }

        private void SetLocalPlayerIndicatorVisible(bool visible)
        {
            if (localPlayerIndicatorRenderer == null)
            {
                return;
            }

            bool shouldShow = visible && localPlayerIndicatorRenderer.sprite != null;
            localPlayerIndicatorRenderer.enabled = shouldShow;
            if (localPlayerIndicatorTransform != null)
            {
                localPlayerIndicatorTransform.localScale = shouldShow ? localPlayerIndicatorBaseScale : Vector3.zero;
            }
        }

        private Vector2 GetCurrentPosition()
        {
            if (body != null && body.simulated)
            {
                return body.position;
            }

            return transform.position;
        }

        private void OnDestroy()
        {
            if (localPlayerIndicatorRenderer != null
                && localPlayerIndicatorRenderer.gameObject != null
                && localPlayerIndicatorRenderer.gameObject.name == "LocalPlayerIndicator"
                && localPlayerIndicatorRenderer.transform.parent == transform)
            {
                Destroy(localPlayerIndicatorRenderer.gameObject);
            }
        }
    }
}
