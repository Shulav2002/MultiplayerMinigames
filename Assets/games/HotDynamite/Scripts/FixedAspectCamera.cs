using UnityEngine;

namespace HotDynamite
{
    [ExecuteAlways]
    [RequireComponent(typeof(Camera))]
    public class FixedAspectCamera : MonoBehaviour
    {
        private static readonly Rect FullViewport = new Rect(0f, 0f, 1f, 1f);

        [SerializeField] private float targetAspectWidth = 16f;
        [SerializeField] private float targetAspectHeight = 9f;

        private Camera cachedCamera;
        private int lastScreenWidth = -1;
        private int lastScreenHeight = -1;

        private void OnEnable()
        {
            CacheCamera();
            ApplyAspect();
        }

        private void Update()
        {
            if (!HasScreenSizeChanged())
            {
                return;
            }

            ApplyAspect();
        }

        private void OnValidate()
        {
            CacheCamera();
            ApplyAspect();
        }

        private void OnDisable()
        {
            if (cachedCamera != null)
            {
                cachedCamera.rect = FullViewport;
            }
        }

        private void CacheCamera()
        {
            if (cachedCamera == null)
            {
                cachedCamera = GetComponent<Camera>();
            }
        }

        private void ApplyAspect()
        {
            if (cachedCamera == null)
            {
                return;
            }

            float width = Mathf.Max(0.01f, targetAspectWidth);
            float height = Mathf.Max(0.01f, targetAspectHeight);
            float targetAspect = width / height;
            float currentAspect = (float)Screen.width / Mathf.Max(1, Screen.height);

            Rect viewport = FullViewport;
            float scaleHeight = currentAspect / targetAspect;

            if (scaleHeight < 1f)
            {
                viewport.height = scaleHeight;
                viewport.y = (1f - scaleHeight) * 0.5f;
            }
            else
            {
                float scaleWidth = 1f / scaleHeight;
                viewport.width = scaleWidth;
                viewport.x = (1f - scaleWidth) * 0.5f;
            }

            cachedCamera.rect = viewport;
            lastScreenWidth = Screen.width;
            lastScreenHeight = Screen.height;
        }

        private bool HasScreenSizeChanged()
        {
            return Screen.width != lastScreenWidth || Screen.height != lastScreenHeight;
        }
    }
}
