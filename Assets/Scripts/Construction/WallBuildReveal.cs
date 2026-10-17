using UnityEngine;

namespace IdleTown.Construction
{
    /// <summary>Only the art moves. The wall's collider and gameplay coordinates remain fixed.</summary>
    public sealed class WallBuildReveal : MonoBehaviour
    {
        Transform visual;
        float started;

        public void Begin(Transform target)
        {
            visual = target;
            started = Time.unscaledTime;
        }

        void Update()
        {
            if (visual == null) { enabled = false; return; }
            float t = Mathf.Clamp01((Time.unscaledTime - started) / .2f);
            float height = Mathf.Lerp(.84f, 1f, 1f - Mathf.Pow(1f - t, 3f));
            visual.localScale = new Vector3(1, height, 1);
            visual.localPosition = new Vector3(0, (height - 1f) * .5f, 0);
            if (t >= 1f) enabled = false;
        }
    }
}
