using UnityEngine;

namespace StayAlive
{
    public class StayAlivePlayerInput : MonoBehaviour
    {
        private const int MousePointerId = int.MinValue;
        private const int JoystickTextureSize = 128;

        [Header("Mobile Joystick")]
        [SerializeField] private bool useMobileJoystick = true;
        [SerializeField] private bool showJoystick = true;
        [SerializeField] private bool limitToLeftHalf = false;
        [SerializeField] private float joystickRadiusPixels = 120f;
        [SerializeField] private float deadZone = 0.1f;
        [SerializeField] private bool forceMobileJoystickInEditor = false;

        private int activeFingerId = -1;
        private Vector2 joystickOrigin;
        private Vector2 joystickDelta;
        private Texture2D joystickBaseTexture;
        private Texture2D joystickKnobTexture;

        public Vector2 MoveVector { get; private set; }

        private void Update()
        {
            Vector2 input = UseMobileInput()
                ? ReadMobileJoystickInput()
                : ReadLegacyAxisInput();
            MoveVector = NormalizeInput(input);
        }

        private static Vector2 NormalizeInput(Vector2 input)
        {
            if (input.sqrMagnitude > 1f)
            {
                input.Normalize();
            }

            return input;
        }

        private bool UseMobileInput()
        {
            if (!useMobileJoystick)
            {
                return false;
            }

            if (activeFingerId >= 0 || Input.touchCount > 0)
            {
                return true;
            }

            if (Application.isMobilePlatform)
            {
                return true;
            }

#if UNITY_WEBGL && !UNITY_EDITOR
            if (Input.touchSupported)
            {
                return true;
            }
#endif

            return forceMobileJoystickInEditor;
        }

        private static Vector2 ReadLegacyAxisInput()
        {
            return new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
        }

        private Vector2 ReadMobileJoystickInput()
        {
            if (TryReadActiveTouch(out Vector2 activeTouchInput))
            {
                return activeTouchInput;
            }

            if (TryBeginTouchControl())
            {
                return Vector2.zero;
            }

#if UNITY_EDITOR || UNITY_STANDALONE || UNITY_WEBGL
            if (forceMobileJoystickInEditor)
            {
                return ReadMouseJoystickInput();
            }

#if UNITY_WEBGL && !UNITY_EDITOR
            return ReadMouseJoystickInput();
#endif
#endif

            return Vector2.zero;
        }

        private bool TryReadActiveTouch(out Vector2 input)
        {
            input = Vector2.zero;
            if (activeFingerId < 0)
            {
                return false;
            }

            for (int i = 0; i < Input.touchCount; i++)
            {
                Touch touch = Input.GetTouch(i);
                if (touch.fingerId != activeFingerId)
                {
                    continue;
                }

                if (touch.phase == TouchPhase.Ended || touch.phase == TouchPhase.Canceled)
                {
                    ResetJoystick();
                    return true;
                }

                UpdateJoystickDelta(touch.position);
                input = GetNormalizedJoystickVector();
                return true;
            }

            ResetJoystick();
            return false;
        }

        private bool TryBeginTouchControl()
        {
            float halfScreenWidth = Screen.width * 0.5f;
            for (int i = 0; i < Input.touchCount; i++)
            {
                Touch touch = Input.GetTouch(i);
                if (touch.phase != TouchPhase.Began)
                {
                    continue;
                }

                if (limitToLeftHalf && touch.position.x > halfScreenWidth)
                {
                    continue;
                }

                BeginJoystickControl(touch.fingerId, touch.position);
                return true;
            }

            return false;
        }

#if UNITY_EDITOR || UNITY_STANDALONE || UNITY_WEBGL
        private Vector2 ReadMouseJoystickInput()
        {
            if (Input.GetMouseButtonDown(0))
            {
                Vector2 mousePosition = Input.mousePosition;
                if (!limitToLeftHalf || mousePosition.x <= Screen.width * 0.5f)
                {
                    BeginJoystickControl(MousePointerId, mousePosition);
                }
            }

            if (activeFingerId == MousePointerId && Input.GetMouseButton(0))
            {
                UpdateJoystickDelta(Input.mousePosition);
                return GetNormalizedJoystickVector();
            }

            if (activeFingerId == MousePointerId && Input.GetMouseButtonUp(0))
            {
                ResetJoystick();
            }

            return Vector2.zero;
        }
#endif

        private void UpdateJoystickDelta(Vector2 currentPosition)
        {
            joystickDelta = currentPosition - joystickOrigin;
            joystickDelta = Vector2.ClampMagnitude(joystickDelta, Mathf.Max(1f, joystickRadiusPixels));
        }

        private Vector2 GetNormalizedJoystickVector()
        {
            float radius = Mathf.Max(1f, joystickRadiusPixels);
            Vector2 normalized = joystickDelta / radius;
            if (normalized.sqrMagnitude < deadZone * deadZone)
            {
                return Vector2.zero;
            }

            return normalized;
        }

        private void BeginJoystickControl(int pointerId, Vector2 origin)
        {
            activeFingerId = pointerId;
            joystickOrigin = origin;
            joystickDelta = Vector2.zero;
        }

        private void ResetJoystick()
        {
            activeFingerId = -1;
            joystickDelta = Vector2.zero;
        }

        private void OnGUI()
        {
            if (!showJoystick || !UseMobileInput() || activeFingerId == -1)
            {
                return;
            }

            DrawJoystick();
        }

        private void DrawJoystick()
        {
            EnsureJoystickTextures();

            float baseSize = joystickRadiusPixels * 2f;
            float knobSize = joystickRadiusPixels;

            Vector2 guiOrigin = new Vector2(joystickOrigin.x, Screen.height - joystickOrigin.y);
            Vector2 guiKnob = new Vector2(
                joystickOrigin.x + joystickDelta.x,
                Screen.height - (joystickOrigin.y + joystickDelta.y));

            Rect baseRect = new Rect(guiOrigin.x - joystickRadiusPixels, guiOrigin.y - joystickRadiusPixels, baseSize, baseSize);
            Rect knobRect = new Rect(guiKnob.x - knobSize * 0.5f, guiKnob.y - knobSize * 0.5f, knobSize, knobSize);

            Color previousColor = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, 0.2f);
            GUI.DrawTexture(baseRect, joystickBaseTexture);
            GUI.color = new Color(1f, 1f, 1f, 0.45f);
            GUI.DrawTexture(knobRect, joystickKnobTexture);
            GUI.color = previousColor;
        }

        private void EnsureJoystickTextures()
        {
            if (joystickBaseTexture == null)
            {
                joystickBaseTexture = CreateCircleTexture(JoystickTextureSize);
            }

            if (joystickKnobTexture == null)
            {
                joystickKnobTexture = CreateCircleTexture(JoystickTextureSize);
            }
        }

        private static Texture2D CreateCircleTexture(int size)
        {
            Texture2D texture = new Texture2D(size, size, TextureFormat.ARGB32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };

            float radius = (size - 1) * 0.5f;
            float radiusSquared = radius * radius;
            float center = radius;

            for (int y = 0; y < size; y++)
            {
                float dy = y - center;
                for (int x = 0; x < size; x++)
                {
                    float dx = x - center;
                    float distanceSquared = dx * dx + dy * dy;
                    float distance = Mathf.Sqrt(distanceSquared);
                    float alpha = Mathf.Clamp01(radius - distance + 1f);
                    if (distanceSquared > radiusSquared + (2f * radius))
                    {
                        alpha = 0f;
                    }

                    texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                }
            }

            texture.Apply();
            return texture;
        }

        private void OnDestroy()
        {
            DestroyTextureIfAny(ref joystickBaseTexture);
            DestroyTextureIfAny(ref joystickKnobTexture);
        }

        private static void DestroyTextureIfAny(ref Texture2D texture)
        {
            if (texture == null)
            {
                return;
            }

            Destroy(texture);
            texture = null;
        }
    }
}
