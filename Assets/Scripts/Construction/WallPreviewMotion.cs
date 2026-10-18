using UnityEngine;

namespace IdleTown.Construction
{
    /// <summary>A short, non-overshooting settle for pooled preview art. Validation stays snapped.</summary>
    public sealed class WallPreviewMotion : MonoBehaviour
    {
        Vector3 targetPosition, targetScale;
        Quaternion targetRotation;
        bool initialized;

        public void SetTarget(Vector3 position, Quaternion rotation, Vector3 scale)
        {
            targetPosition = position; targetRotation = rotation; targetScale = scale;
            if (!initialized || !Application.isPlaying)
            {
                transform.SetPositionAndRotation(position, rotation);
                transform.localScale = scale;
                initialized = true;
            }
        }

        void LateUpdate()
        {
            if (!initialized) return;
            float settle = 1f - Mathf.Exp(-40f * Time.unscaledDeltaTime);
            transform.SetPositionAndRotation(Vector3.Lerp(transform.position, targetPosition, settle),
                Quaternion.Slerp(transform.rotation, targetRotation, settle));
            transform.localScale = Vector3.Lerp(transform.localScale, targetScale, settle);
        }

        void OnDisable() => initialized = false;
    }
}
