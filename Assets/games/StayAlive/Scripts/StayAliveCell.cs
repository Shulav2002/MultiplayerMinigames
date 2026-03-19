using UnityEngine;

namespace StayAlive
{
    [RequireComponent(typeof(SpriteRenderer))]
    public class StayAliveCell : MonoBehaviour
    {
        public enum CellVisualState
        {
            Idle = 0,
            Spinning = 1,
            Warning = 2,
            Exploding = 3,
            Collapsing = 4,
            Hidden = 5,
            Refilling = 6
        }

        [SerializeField] private SpriteRenderer spriteRenderer;

        private Vector3 authoredScale = Vector3.one;
        private Sprite currentSprite;
        private Color baseColor = Color.white;
        private Color accentColor = Color.white;
        private CellVisualState visualState;
        private float stateElapsed;
        private float stateDuration;

        public int Column { get; private set; }
        public int Row { get; private set; }
        public int CellIndex { get; private set; }
        public SpriteRenderer Renderer => spriteRenderer;

        private void Awake()
        {
            EnsureRenderer();
            authoredScale = transform.localScale;
        }

        private void Update()
        {
            if (spriteRenderer == null)
            {
                return;
            }

            switch (visualState)
            {
                case CellVisualState.Warning:
                {
                    float pulse = (Mathf.Sin(Time.time * 10f) + 1f) * 0.5f;
                    spriteRenderer.color = Color.Lerp(baseColor, accentColor, pulse);
                    transform.localScale = authoredScale;
                    break;
                }

                case CellVisualState.Exploding:
                {
                    float flash = (Mathf.Sin(Time.time * 18f) + 1f) * 0.5f;
                    spriteRenderer.color = Color.Lerp(baseColor, accentColor, flash);
                    transform.localScale = authoredScale;
                    break;
                }

                case CellVisualState.Collapsing:
                {
                    float collapse = GetStateProgress();
                    float easedCollapse = EaseInCubic(collapse);
                    spriteRenderer.color = Color.Lerp(baseColor, accentColor, easedCollapse);
                    transform.localScale = authoredScale * Mathf.Lerp(1f, 0f, easedCollapse);
                    break;
                }

                case CellVisualState.Refilling:
                {
                    float refill = GetStateProgress();
                    float scale = EvaluateRefillScale(refill);
                    spriteRenderer.color = Color.Lerp(accentColor, baseColor, EaseOutCubic(refill));
                    transform.localScale = authoredScale * scale;
                    break;
                }

                case CellVisualState.Hidden:
                    spriteRenderer.color = accentColor;
                    transform.localScale = Vector3.zero;
                    break;

                default:
                    spriteRenderer.color = baseColor;
                    transform.localScale = authoredScale;
                    break;
            }
        }

        public void Initialize(int cellIndex, int column, int row)
        {
            CellIndex = cellIndex;
            Column = column;
            Row = row;
            EnsureRenderer();
            authoredScale = transform.localScale;
        }

        public void ShowIdle(Sprite sprite, Color color)
        {
            ApplyVisual(sprite, color, color, CellVisualState.Idle);
        }

        public void ShowSpinning(Sprite sprite, Color color)
        {
            ApplyVisual(sprite, color, color, CellVisualState.Spinning);
        }

        public void ShowWarning(Sprite sprite, Color color, Color warningColor)
        {
            ApplyVisual(sprite, color, warningColor, CellVisualState.Warning);
        }

        public void ShowExplosion(Sprite sprite, Color color, Color explosionColor)
        {
            ApplyVisual(sprite, color, explosionColor, CellVisualState.Exploding);
        }

        public void ShowCollapse(Sprite sprite, Color color, Color collapseColor)
        {
            ShowCollapse(sprite, color, collapseColor, 0f);
        }

        public void ShowCollapse(Sprite sprite, Color color, Color collapseColor, float durationSeconds)
        {
            ApplyVisual(sprite, color, collapseColor, CellVisualState.Collapsing, durationSeconds);
        }

        public void ShowRefill(Sprite sprite, Color color, Color startColor, float durationSeconds)
        {
            ApplyVisual(sprite, color, startColor, CellVisualState.Refilling, durationSeconds);
        }

        public void ShowHidden(Sprite sprite, Color color)
        {
            ApplyVisual(sprite, color, WithAlpha(color, 0f), CellVisualState.Hidden, 0f);
        }

        private void ApplyVisual(Sprite sprite, Color color, Color accent, CellVisualState state)
        {
            ApplyVisual(sprite, color, accent, state, 0f);
        }

        private void ApplyVisual(Sprite sprite, Color color, Color accent, CellVisualState state, float durationSeconds)
        {
            EnsureRenderer();
            currentSprite = sprite;
            baseColor = color;
            accentColor = accent;
            visualState = state;
            stateElapsed = 0f;
            stateDuration = Mathf.Max(0.0001f, durationSeconds);

            spriteRenderer.sprite = currentSprite;
            spriteRenderer.color = state == CellVisualState.Refilling ? accentColor : baseColor;
            transform.localScale = state == CellVisualState.Refilling ? Vector3.zero : authoredScale;
        }

        private float GetStateProgress()
        {
            stateElapsed += Time.deltaTime;
            return Mathf.Clamp01(stateElapsed / Mathf.Max(0.0001f, stateDuration));
        }

        private static float EaseInCubic(float t)
        {
            return t * t * t;
        }

        private static float EaseOutCubic(float t)
        {
            float inverse = 1f - t;
            return 1f - (inverse * inverse * inverse);
        }

        private static float EvaluateRefillScale(float t)
        {
            if (t <= 0.72f)
            {
                float earlyT = EaseOutCubic(t / 0.72f);
                return Mathf.Lerp(0f, 1.12f, earlyT);
            }

            float lateT = (t - 0.72f) / 0.28f;
            return Mathf.Lerp(1.12f, 1f, lateT);
        }

        private static Color WithAlpha(Color color, float alpha)
        {
            color.a = alpha;
            return color;
        }

        private void EnsureRenderer()
        {
            if (spriteRenderer == null)
            {
                spriteRenderer = GetComponent<SpriteRenderer>();
            }
        }
    }
}
