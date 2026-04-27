using UnityEngine;

namespace HotDynamite
{
    public class HotDynamitePlayerInput : MonoBehaviour
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
        [SerializeField] private Vector2 joystickIdleScreenPadding = new Vector2(140f, 140f);

        [Header("Keyboard")]
        [SerializeField, Min(0.1f)] private float keyboardAcceleration = 7.5f;
        [SerializeField, Min(0.1f)] private float keyboardDeceleration = 10f;

        private int activeFingerId = -1;
        private Vector2 joystickOrigin;
        private Vector2 joystickDelta;
        private Texture2D joystickBaseTexture;
        private Texture2D joystickKnobTexture;
        private Vector2 smoothedKeyboardInput;

        public Vector2 MoveVector { get; private set; }

        private void OnEnable()
        {
            ResetJoystick();
        }

        private void Update()
        {
            Vector2 keyboardInput = GetKeyboardInput();
            Vector2 input = keyboardInput.sqrMagnitude > 0.0001f
                ? keyboardInput
                : UseMobileInput()
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

            if (Application.isMobilePlatform)
            {
                return true;
            }

            return forceMobileJoystickInEditor;
        }

        private Vector2 GetKeyboardInput()
        {
            Vector2 targetInput = NormalizeInput(ReadKeyboardInput());
            float response = targetInput.sqrMagnitude > 0f ? keyboardAcceleration : keyboardDeceleration;
            smoothedKeyboardInput = Vector2.MoveTowards(
                smoothedKeyboardInput,
                targetInput,
                Mathf.Max(0.1f, response) * Time.deltaTime);

            if (smoothedKeyboardInput.sqrMagnitude < 0.0001f)
            {
                smoothedKeyboardInput = Vector2.zero;
            }

            return smoothedKeyboardInput;
        }

        private static Vector2 ReadLegacyAxisInput()
        {
            Vector2 axisInput = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
            return axisInput.sqrMagnitude > 0f
                ? axisInput
                : ReadKeyboardInput();
        }

        private static Vector2 ReadKeyboardInput()
        {
            float horizontal = 0f;
            if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow))
            {
                horizontal -= 1f;
            }

            if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow))
            {
                horizontal += 1f;
            }

            float vertical = 0f;
            if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow))
            {
                vertical -= 1f;
            }

            if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow))
            {
                vertical += 1f;
            }

            return new Vector2(horizontal, vertical);
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

#if UNITY_EDITOR || UNITY_STANDALONE
            if (forceMobileJoystickInEditor)
            {
                return ReadMouseJoystickInput();
            }
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

#if UNITY_EDITOR || UNITY_STANDALONE
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
            joystickOrigin = GetIdleJoystickOrigin();
            joystickDelta = Vector2.zero;
        }

        private void OnGUI()
        {
            if (!showJoystick || !UseMobileInput())
            {
                return;
            }

            DrawJoystick();
        }

        private void DrawJoystick()
        {
            EnsureJoystickTextures();

            if (activeFingerId < 0)
            {
                joystickOrigin = GetIdleJoystickOrigin();
            }

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

        private Vector2 GetIdleJoystickOrigin()
        {
            float radius = Mathf.Max(1f, joystickRadiusPixels);
            float x = Mathf.Max(radius, joystickIdleScreenPadding.x);
            float y = Mathf.Max(radius, joystickIdleScreenPadding.y);
            return new Vector2(x, y);
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

            Object.Destroy(texture);
            texture = null;
        }
    }
}
