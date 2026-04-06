using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace TankBattle
{
    public class TankBattlePlayerInput : MonoBehaviour
    {
        private const int MousePointerId = int.MinValue;
        private const int JoystickTextureSize = 128;

        [Header("Mobile Joystick")]
        [SerializeField] private bool useMobileJoystick = true;
        [SerializeField] private bool showJoystick = true;
        [SerializeField] private bool limitToLeftHalf = true;
        [SerializeField] private float joystickRadiusPixels = 120f;
        [SerializeField] private float deadZone = 0.1f;
        [SerializeField] private bool forceMobileJoystickInEditor = true;
        [SerializeField] private Color joystickColor = Color.white;

        private int activeFingerId = -1;
        private Vector2 joystickOrigin;
        private Vector2 joystickDelta;
        private Texture2D joystickBaseTexture;
        private Texture2D joystickKnobTexture;
        private bool shootPressed;
        private bool inputSuppressed = false;
        private bool wasInputActive = false;
        private TankBattleBotController botController;
        private bool warnedLegacyInputUnavailable;
        private bool warnedTouchInputUnavailable;

        public Vector2 MoveVector { get; private set; }
        public bool ShootPressed { get; private set; }

        private void Update()
        {
            if (botController == null)
            {
                botController = GetComponent<TankBattleBotController>();
            }

            if (botController != null && botController.enabled)
            {
                UpdateBotInput();
                return;
            }

            Vector2 joystickInput = UseMobileInput() ? ReadMobileJoystickInput() : Vector2.zero;
            Vector2 keyboardInput = ReadLegacyAxisInputSafe();
            
            Vector2 rawInput = joystickInput.sqrMagnitude > keyboardInput.sqrMagnitude 
                ? joystickInput 
                : keyboardInput;

            bool isInputActive = rawInput.sqrMagnitude > 0.01f;

            if (inputSuppressed)
            {
                if (isInputActive)
                {
                    MoveVector = Vector2.zero;
                    wasInputActive = true;
                }
                else
                {
                    if (wasInputActive)
                    {
                        inputSuppressed = false;
                        wasInputActive = false;
                    }
                    MoveVector = Vector2.zero;
                }
            }
            else
            {
                MoveVector = NormalizeInput(rawInput);
            }

            bool keyboardShoot = ReadKeyboardShootSafe();
            bool touchShoot = UseMobileInput() && DetectRightSideTouch();
            ShootPressed = keyboardShoot || touchShoot;
        }

        private bool DetectRightSideTouch()
        {
            float halfScreenWidth = Screen.width * 0.5f;

#if ENABLE_INPUT_SYSTEM
            if (TryDetectRightSideTouchNewInputSystem(halfScreenWidth))
            {
                return true;
            }
#endif

            int touchCount = GetTouchCountSafe();

            for (int i = 0; i < touchCount; i++)
            {
                if (!TryGetTouchSafe(i, out Touch touch))
                {
                    continue;
                }

                if (touch.phase == UnityEngine.TouchPhase.Began && touch.position.x > halfScreenWidth)
                {
                    return true;
                }
            }

#if UNITY_EDITOR || UNITY_STANDALONE
            if (forceMobileJoystickInEditor && Input.GetMouseButtonDown(0))
            {
                Vector2 mousePosition = Input.mousePosition;
                if (mousePosition.x > halfScreenWidth)
                {
                    return true;
                }
            }
#endif

            return false;
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

#if ENABLE_INPUT_SYSTEM
            if (Touchscreen.current != null)
            {
                return true;
            }
#endif

            if (IsTouchAvailableSafe())
            {
                return true;
            }

            return forceMobileJoystickInEditor;
        }

        private static Vector2 ReadLegacyAxisInput()
        {
            return new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
        }

        private Vector2 ReadLegacyAxisInputSafe()
        {
            try
            {
                return ReadLegacyAxisInput();
            }
            catch (System.Exception)
            {
                if (!warnedLegacyInputUnavailable)
                {
                    warnedLegacyInputUnavailable = true;
                    Debug.LogWarning("TankBattlePlayerInput: Legacy axis input is unavailable. Falling back to joystick/touch input only.");
                }

#if ENABLE_INPUT_SYSTEM
                return ReadKeyboardVectorNewInputSystem();
#else
                return Vector2.zero;
#endif
            }
        }

        private bool ReadKeyboardShootSafe()
        {
            try
            {
                return Input.GetKeyDown(KeyCode.Space);
            }
            catch (System.Exception)
            {
#if ENABLE_INPUT_SYSTEM
                Keyboard keyboard = Keyboard.current;
                return keyboard != null && keyboard.spaceKey.wasPressedThisFrame;
#else
                return false;
#endif
            }
        }

        private Vector2 ReadMobileJoystickInput()
        {
#if ENABLE_INPUT_SYSTEM
            if (TryReadActiveTouchNewInputSystem(out Vector2 newInputActiveTouch))
            {
                return newInputActiveTouch;
            }

            if (TryBeginTouchControlNewInputSystem())
            {
                return Vector2.zero;
            }
#endif

            if (TryReadActiveTouch(out Vector2 activeTouchInput))
            {
                return activeTouchInput;
            }

            if (TryBeginTouchControl())
            {
                return Vector2.zero;
            }

#if UNITY_WEBGL && !UNITY_EDITOR
            return ReadWebPointerJoystickInput();
#endif

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

            int touchCount = GetTouchCountSafe();
            for (int i = 0; i < touchCount; i++)
            {
                if (!TryGetTouchSafe(i, out Touch touch))
                {
                    continue;
                }

                if (touch.fingerId != activeFingerId)
                {
                    continue;
                }

                if (touch.phase == UnityEngine.TouchPhase.Ended || touch.phase == UnityEngine.TouchPhase.Canceled)
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
            if (activeFingerId >= 0)
            {
                return false;
            }

            float halfScreenWidth = Screen.width * 0.5f;
            int touchCount = GetTouchCountSafe();
            for (int i = 0; i < touchCount; i++)
            {
                if (!TryGetTouchSafe(i, out Touch touch))
                {
                    continue;
                }

                if (touch.phase != UnityEngine.TouchPhase.Began
                    && touch.phase != UnityEngine.TouchPhase.Moved
                    && touch.phase != UnityEngine.TouchPhase.Stationary)
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
            if (activeFingerId == -1 && (ReadMouseButtonDownSafe() || ReadMouseButtonSafe()))
            {
                Vector2 mousePosition = ReadMousePositionSafe();
                if (!limitToLeftHalf || mousePosition.x <= Screen.width * 0.5f)
                {
                    BeginJoystickControl(MousePointerId, mousePosition);
                }
            }

            if (activeFingerId == MousePointerId && ReadMouseButtonSafe())
            {
                UpdateJoystickDelta(ReadMousePositionSafe());
                return GetNormalizedJoystickVector();
            }

            if (activeFingerId == MousePointerId && ReadMouseButtonUpSafe())
            {
                ResetJoystick();
            }

            return Vector2.zero;
        }
#endif

#if UNITY_WEBGL && !UNITY_EDITOR
        private Vector2 ReadWebPointerJoystickInput()
        {
            if (ReadMouseButtonDownSafe())
            {
                Vector2 pointerPosition = ReadMousePositionSafe();
                if (!limitToLeftHalf || pointerPosition.x <= Screen.width * 0.5f)
                {
                    BeginJoystickControl(MousePointerId, pointerPosition);
                }
            }

            if (activeFingerId == MousePointerId && ReadMouseButtonSafe())
            {
                UpdateJoystickDelta(ReadMousePositionSafe());
                return GetNormalizedJoystickVector();
            }

            if (activeFingerId == MousePointerId && ReadMouseButtonUpSafe())
            {
                ResetJoystick();
            }

            return Vector2.zero;
        }
#endif

#if ENABLE_INPUT_SYSTEM
        private bool TryReadActiveTouchNewInputSystem(out Vector2 input)
        {
            input = Vector2.zero;
            if (activeFingerId < 0)
            {
                return false;
            }

            Touchscreen touchscreen = Touchscreen.current;
            if (touchscreen == null)
            {
                return false;
            }

            var touches = touchscreen.touches;
            for (int i = 0; i < touches.Count; i++)
            {
                var touch = touches[i];
                int touchId = touch.touchId.ReadValue();
                if (touchId != activeFingerId)
                {
                    continue;
                }

                UnityEngine.InputSystem.TouchPhase phase = touch.phase.ReadValue();
                bool pressed = touch.press.isPressed;
                if (!pressed
                    || phase == UnityEngine.InputSystem.TouchPhase.Ended
                    || phase == UnityEngine.InputSystem.TouchPhase.Canceled)
                {
                    ResetJoystick();
                    return true;
                }

                UpdateJoystickDelta(touch.position.ReadValue());
                input = GetNormalizedJoystickVector();
                return true;
            }

            return false;
        }

        private bool TryBeginTouchControlNewInputSystem()
        {
            if (activeFingerId >= 0)
            {
                return false;
            }

            Touchscreen touchscreen = Touchscreen.current;
            if (touchscreen == null)
            {
                return false;
            }

            float halfScreenWidth = Screen.width * 0.5f;
            var touches = touchscreen.touches;
            for (int i = 0; i < touches.Count; i++)
            {
                var touch = touches[i];
                if (!touch.press.isPressed)
                {
                    continue;
                }

                UnityEngine.InputSystem.TouchPhase phase = touch.phase.ReadValue();
                if (phase != UnityEngine.InputSystem.TouchPhase.Began
                    && phase != UnityEngine.InputSystem.TouchPhase.Moved
                    && phase != UnityEngine.InputSystem.TouchPhase.Stationary)
                {
                    continue;
                }

                Vector2 position = touch.position.ReadValue();
                if (limitToLeftHalf && position.x > halfScreenWidth)
                {
                    continue;
                }

                BeginJoystickControl(touch.touchId.ReadValue(), position);
                return true;
            }

            return false;
        }

        private bool TryDetectRightSideTouchNewInputSystem(float halfScreenWidth)
        {
            Touchscreen touchscreen = Touchscreen.current;
            if (touchscreen == null)
            {
                return false;
            }

            var touches = touchscreen.touches;
            for (int i = 0; i < touches.Count; i++)
            {
                var touch = touches[i];
                if (!touch.press.isPressed)
                {
                    continue;
                }

                UnityEngine.InputSystem.TouchPhase phase = touch.phase.ReadValue();
                if (phase != UnityEngine.InputSystem.TouchPhase.Began)
                {
                    continue;
                }

                if (touch.position.ReadValue().x > halfScreenWidth)
                {
                    return true;
                }
            }

            return false;
        }

        private static Vector2 ReadKeyboardVectorNewInputSystem()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return Vector2.zero;
            }

            float right = (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed) ? 1f : 0f;
            float left = (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed) ? 1f : 0f;
            float up = (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed) ? 1f : 0f;
            float down = (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed) ? 1f : 0f;

            return new Vector2(right - left, up - down);
        }
#endif

        private bool IsTouchAvailableSafe()
        {
            try
            {
                return Input.touchSupported;
            }
            catch (System.Exception)
            {
                WarnTouchInputUnavailableOnce();
#if ENABLE_INPUT_SYSTEM
                return Touchscreen.current != null;
#else
                return false;
#endif
            }
        }

        private int GetTouchCountSafe()
        {
            try
            {
                return Input.touchCount;
            }
            catch (System.Exception)
            {
                WarnTouchInputUnavailableOnce();
                return 0;
            }
        }

        private bool TryGetTouchSafe(int index, out Touch touch)
        {
            touch = default;

            try
            {
                if (index < 0 || index >= Input.touchCount)
                {
                    return false;
                }

                touch = Input.GetTouch(index);
                return true;
            }
            catch (System.Exception)
            {
                WarnTouchInputUnavailableOnce();
                return false;
            }
        }

        private bool ReadMouseButtonDownSafe()
        {
            try
            {
                return Input.GetMouseButtonDown(0);
            }
            catch (System.Exception)
            {
#if ENABLE_INPUT_SYSTEM
                Mouse mouse = Mouse.current;
                return mouse != null && mouse.leftButton.wasPressedThisFrame;
#else
                return false;
#endif
            }
        }

        private bool ReadMouseButtonSafe()
        {
            try
            {
                return Input.GetMouseButton(0);
            }
            catch (System.Exception)
            {
#if ENABLE_INPUT_SYSTEM
                Mouse mouse = Mouse.current;
                return mouse != null && mouse.leftButton.isPressed;
#else
                return false;
#endif
            }
        }

        private bool ReadMouseButtonUpSafe()
        {
            try
            {
                return Input.GetMouseButtonUp(0);
            }
            catch (System.Exception)
            {
#if ENABLE_INPUT_SYSTEM
                Mouse mouse = Mouse.current;
                return mouse != null && mouse.leftButton.wasReleasedThisFrame;
#else
                return false;
#endif
            }
        }

        private static Vector2 ReadMousePositionSafe()
        {
            try
            {
                return Input.mousePosition;
            }
            catch (System.Exception)
            {
#if ENABLE_INPUT_SYSTEM
                Mouse mouse = Mouse.current;
                return mouse != null ? mouse.position.ReadValue() : Vector2.zero;
#else
                return Vector2.zero;
#endif
            }
        }

        private void WarnTouchInputUnavailableOnce()
        {
            if (warnedTouchInputUnavailable)
            {
                return;
            }

            warnedTouchInputUnavailable = true;
            Debug.LogWarning("TankBattlePlayerInput: Touch APIs are unavailable in current Input handling mode.");
        }

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

        public void ResetMovementInput()
        {
            ResetJoystick();
            MoveVector = Vector2.zero;
            inputSuppressed = true;
            wasInputActive = false;
        }

        public void SetJoystickColor(Color color)
        {
            joystickColor = color;
        }

        private void UpdateBotInput()
        {
            MoveVector = botController.MoveVector;
            ShootPressed = botController.WantsToShoot;
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
            GUI.color = new Color(joystickColor.r, joystickColor.g, joystickColor.b, 0.2f);
            GUI.DrawTexture(baseRect, joystickBaseTexture);
            GUI.color = new Color(joystickColor.r, joystickColor.g, joystickColor.b, 0.45f);
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
                    if (distanceSquared > (radiusSquared + 2f * radius))
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
