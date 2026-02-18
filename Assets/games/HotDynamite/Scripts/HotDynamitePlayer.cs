using UnityEngine;

namespace HotDynamite
{
    [RequireComponent(typeof(Rigidbody2D), typeof(Collider2D))]
    public class HotDynamitePlayer : MonoBehaviour
    {
        [SerializeField] private HotDynamitePlayerInput humanInput;
        [SerializeField] private HotDynamiteBotController botInput;
        [SerializeField] private Rigidbody2D body;
        [SerializeField] private Collider2D hitCollider;
        [SerializeField] private GameObject bombPrefab;
        [SerializeField] private Vector3 bombLocalOffset = new Vector3(0f, 0.9f, 0f);
        [Header("Facing")]
        [SerializeField] private SpriteRenderer facingRenderer;
        [SerializeField] private float facingInputThreshold = 0.05f;
        [SerializeField] private bool spriteFacesRightByDefault = true;

        private HotDynamiteGameManager manager;
        private HotDynamiteGameSettings settings;
        private bool isHuman;
        private bool hasBomb;
        private bool isAlive = true;
        private Vector2 estimatedVelocity;
        private GameObject bombInstance;
        private float temporarySpeedMultiplier = 1f;
        private float temporarySpeedUntil = -1f;

        public bool IsAlive => isAlive;
        public bool HasBomb => hasBomb;
        public Vector2 Position => body != null ? body.position : (Vector2)transform.position;
        public Vector2 EstimatedVelocity => estimatedVelocity;

        public void Initialize(HotDynamiteGameManager manager, HotDynamiteGameSettings settings, bool isHuman)
        {
            this.manager = manager;
            this.settings = settings;
            this.isHuman = isHuman;

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

            if (humanInput != null)
            {
                humanInput.enabled = isHuman;
            }

            if (botInput != null)
            {
                botInput.enabled = !isHuman;
                botInput.Initialize(manager, settings, this);
            }

            isAlive = true;
            hasBomb = false;
            temporarySpeedMultiplier = 1f;
            temporarySpeedUntil = -1f;
            UpdateBombVisual();
        }

        private void FixedUpdate()
        {
            if (!isAlive || settings == null)
            {
                estimatedVelocity = Vector2.zero;
                return;
            }

            Vector2 input = ReadMoveInput();

            if (input.sqrMagnitude > 1f)
            {
                input.Normalize();
            }

            float speed = settings.moveSpeed
                * (hasBomb ? settings.bombHolderSpeedMultiplier : 1f)
                * GetTemporarySpeedMultiplier();
            estimatedVelocity = input * speed;
            UpdateFacing(input);
            Vector2 next = body.position + input * speed * Time.fixedDeltaTime;
            next.x = Mathf.Clamp(next.x, settings.arenaMin.x, settings.arenaMax.x);
            next.y = Mathf.Clamp(next.y, settings.arenaMin.y, settings.arenaMax.y);
            body.MovePosition(next);
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
            temporarySpeedMultiplier = 1f;
            temporarySpeedUntil = -1f;

            gameObject.SetActive(false);
        }

        private void OnDestroy()
        {
            DestroyBombVisual();
        }

        private void UpdateBombVisual()
        {
            if (hasBomb)
            {
                if (bombInstance == null && bombPrefab != null)
                {
                    bombInstance = Instantiate(bombPrefab, transform);
                    bombInstance.transform.localPosition = bombLocalOffset;
                    bombInstance.transform.localRotation = Quaternion.identity;
                }

                return;
            }

            if (bombInstance != null)
            {
                DestroyBombVisual();
            }
        }

        private Vector2 ReadMoveInput()
        {
            if (isHuman && humanInput != null)
            {
                return humanInput.MoveVector;
            }

            if (!isHuman && botInput != null)
            {
                return botInput.MoveVector;
            }

            return Vector2.zero;
        }

        private float GetTemporarySpeedMultiplier()
        {
            if (Time.time < temporarySpeedUntil)
            {
                return temporarySpeedMultiplier;
            }

            temporarySpeedMultiplier = 1f;
            temporarySpeedUntil = -1f;
            return 1f;
        }

        private void DestroyBombVisual()
        {
            if (bombInstance == null)
            {
                return;
            }

            Destroy(bombInstance);
            bombInstance = null;
        }

        private void UpdateFacing(Vector2 movementInput)
        {
            if (Mathf.Abs(movementInput.x) < facingInputThreshold || facingRenderer == null)
            {
                return;
            }

            bool movingRight = movementInput.x > 0f;
            bool shouldFlipX = spriteFacesRightByDefault ? !movingRight : movingRight;
            facingRenderer.flipX = shouldFlipX;
        }
    }
}
