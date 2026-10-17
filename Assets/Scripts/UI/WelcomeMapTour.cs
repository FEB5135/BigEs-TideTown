using System.Collections;
using UnityEngine;

// Tanıtımın hedefini sahneden bulur; yalnız kamerayı geçici olarak gezdirir.
public sealed class WelcomeMapTour : MonoBehaviour
{
    IsometricCamera iso;
    Vector3 originalFocus;
    float originalDistance, originalPitch, originalYaw;
    Coroutine flight;
    bool running;

    public Bounds FocusBounds { get; private set; }
    public Camera ViewCamera { get; private set; }
    public bool IsMoving => flight != null;

    public void Begin()
    {
        End();
        iso = Camera.main != null ? Camera.main.GetComponent<IsometricCamera>() : null;
        if (iso == null)
            iso = FindAnyObjectByType<IsometricCamera>();
        if (iso == null)
            return;
        ViewCamera = iso.GetComponent<Camera>();
        originalFocus = iso.FocusPoint;
        originalDistance = iso.Distance;
        originalPitch = iso.Pitch;
        originalYaw = iso.Yaw;
        running = true;
    }

    public void SelectStep(int step)
    {
        FocusBounds = Locate(step);
        if (!running || iso == null)
            return;
        if (flight != null)
            StopCoroutine(flight);
        var rotation = Quaternion.Euler(originalPitch, originalYaw, 0);
        var right = rotation * Vector3.right;
        var up = rotation * Vector3.up;
        var forward = rotation * Vector3.forward;
        float halfWidth = 0, halfHeight = 0, halfDepth = 0;
        foreach (var corner in Corners(FocusBounds))
        {
            var offset = corner - FocusBounds.center;
            halfWidth = Mathf.Max(halfWidth, Mathf.Abs(Vector3.Dot(offset, right)));
            halfHeight = Mathf.Max(halfHeight, Mathf.Abs(Vector3.Dot(offset, up)));
            halfDepth = Mathf.Max(halfDepth, Mathf.Abs(Vector3.Dot(offset, forward)));
        }
        float tangent = Mathf.Tan(ViewCamera.fieldOfView * Mathf.Deg2Rad * 0.5f);
        float distance = Mathf.Max(halfHeight / (tangent * 0.70f), halfWidth / (tangent * ViewCamera.aspect * 0.65f)) + halfDepth;
        distance = Mathf.Clamp(distance, 48f, 180f);
        // Gösterilen alan sol/üstte kalır; sağ alttaki küçük kart nesneyi örtmez.
        var focus = FocusBounds.center + right * (distance * tangent * ViewCamera.aspect * 0.20f)
            - up * (distance * tangent * 0.15f);
        flight = StartCoroutine(Fly(focus, distance));
    }

    IEnumerator Fly(Vector3 focus, float distance)
    {
        var from = iso.FocusPoint;
        float fromDistance = iso.Distance;
        const float duration = 1.1f;
        for (float elapsed = 0; elapsed < duration; elapsed += Time.unscaledDeltaTime)
        {
            float t = Mathf.SmoothStep(0, 1, elapsed / duration);
            iso.SetPose(Vector3.Lerp(from, focus, t), Mathf.Lerp(fromDistance, distance, t), originalPitch, originalYaw);
            yield return null;
        }
        iso.SetPose(focus, distance, originalPitch, originalYaw);
        flight = null;
    }

    Bounds Locate(int step)
    {
        var well = Well.Instance;
        Vector3 center = well != null ? well.transform.position : originalFocus;
        if (step == 1 || step == 2)
        {
            bool found = false;
            var bounds = new Bounds();
            var type = step == 1 ? ResourceType.Wood : ResourceType.Stone;
            foreach (var node in FindObjectsByType<ResourceNode>())
            {
                if (node.Type != type || node.Remaining <= 0 || node.gameObject.scene != gameObject.scene)
                    continue;
                if (step == 1)
                {
                    foreach (var collider in node.GetComponentsInChildren<Collider>())
                        Include(ref bounds, ref found, collider.bounds);
                }
                else
                {
                    foreach (var renderer in node.GetComponentsInChildren<Renderer>())
                        if (renderer.enabled)
                            Include(ref bounds, ref found, renderer.bounds);
                }
                if (!found)
                    Include(ref bounds, ref found, new Bounds(node.transform.position, Vector3.one * 6));
            }
            if (found)
                return bounds;
        }
        if (step == 3 && well != null)
        {
            bool found = false;
            var bounds = new Bounds();
            foreach (var renderer in well.GetComponentsInChildren<Renderer>())
                if (renderer.enabled)
                    Include(ref bounds, ref found, renderer.bounds);
            if (found)
                return bounds;
        }
        return new Bounds(center + Vector3.up * 4, new Vector3(78, 12, 78));
    }

    static void Include(ref Bounds bounds, ref bool found, Bounds addition)
    {
        if (found)
            bounds.Encapsulate(addition);
        else
        {
            bounds = addition;
            found = true;
        }
    }

    internal static Vector3[] Corners(Bounds bounds)
    {
        var corners = new Vector3[8];
        for (int i = 0; i < corners.Length; i++)
            corners[i] = bounds.center + Vector3.Scale(bounds.extents,
                new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
        return corners;
    }

    public void End()
    {
        if (flight != null)
            StopCoroutine(flight);
        flight = null;
        if (running && iso != null)
            iso.SetPose(originalFocus, originalDistance, originalPitch, originalYaw);
        running = false;
    }

    void OnDisable() => End();
}
