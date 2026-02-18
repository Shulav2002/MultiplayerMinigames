using UnityEngine;

namespace HotDynamite
{
    [ExecuteAlways]
    public class ShaderPlayToggle : MonoBehaviour
    {
        [SerializeField] private Material targetMaterial;
        [SerializeField] private bool animateInEditMode = false;

        private static readonly int AnimateId = Shader.PropertyToID("_Animate");
        private float lastAppliedValue = -1f;

        private void OnEnable()
        {
            Apply();
        }

        private void OnValidate()
        {
            Apply();
        }

        private void Update()
        {
            Apply();
        }

        private void Apply()
        {
            if (targetMaterial == null || !targetMaterial.HasProperty(AnimateId))
            {
                return;
            }

            float animateValue = (Application.isPlaying || animateInEditMode) ? 1f : 0f;
            if (Mathf.Approximately(animateValue, lastAppliedValue))
            {
                return;
            }

            targetMaterial.SetFloat(AnimateId, animateValue);
            lastAppliedValue = animateValue;
        }
    }
}
