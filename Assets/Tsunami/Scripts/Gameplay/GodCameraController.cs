using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace Tsunami
{
    /// <summary>
    /// Elevated, angled god-game camera orbiting a focus point on the ground.
    /// WASD / arrows pan, Q / E rotate, mouse wheel zooms, middle-mouse drag pans, optional edge scrolling.
    /// </summary>
    public class GodCameraController : MonoBehaviour
    {
        [Header("Framing")]
        [SerializeField] Vector3 focus = new Vector3(0f, 0f, -10f);
        [SerializeField, Range(20f, 85f)] float pitch = 52f;
        [SerializeField] float yaw;
        [SerializeField] float distance = 170f;
        [SerializeField] Vector2 distanceLimits = new Vector2(30f, 320f);
        [Tooltip("Focus point is clamped to this half-size square around the origin (m).")]
        [SerializeField] float boundsHalfSize = 90f;

        [Header("Speeds")]
        [Tooltip("Pan speed as a fraction of the current distance per second.")]
        [SerializeField] float panSpeed = 0.8f;
        [SerializeField] float rotateSpeed = 90f;
        [Tooltip("Fraction of the distance zoomed per wheel notch.")]
        [SerializeField, Range(0.02f, 0.5f)] float zoomStep = 0.12f;
        [Tooltip("Higher = snappier zoom/rotation.")]
        [SerializeField, Range(1f, 30f)] float smoothing = 10f;

        [Header("Edge scrolling")]
        [SerializeField] bool edgeScrolling;
        [SerializeField] float edgePixels = 12f;

        float targetDistance;
        float targetYaw;
        Camera cam;

        void Awake()
        {
            cam = GetComponent<Camera>();
            targetDistance = distance;
            targetYaw = yaw;
            Apply();
        }

        void LateUpdate()
        {
            float dt = Time.unscaledDeltaTime;
            Keyboard kb = Keyboard.current;
            Mouse mouse = Mouse.current;
            bool overUI = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();

            Vector2 move = Vector2.zero;
            if (kb != null)
            {
                if (kb.wKey.isPressed || kb.upArrowKey.isPressed) move.y += 1f;
                if (kb.sKey.isPressed || kb.downArrowKey.isPressed) move.y -= 1f;
                if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) move.x += 1f;
                if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) move.x -= 1f;
                if (kb.qKey.isPressed) targetYaw += rotateSpeed * dt;
                if (kb.eKey.isPressed) targetYaw -= rotateSpeed * dt;
            }

            if (mouse != null)
            {
                Vector2 mp = mouse.position.ReadValue();
                if (edgeScrolling && Application.isFocused)
                {
                    if (mp.x <= edgePixels) move.x -= 1f;
                    else if (mp.x >= Screen.width - edgePixels) move.x += 1f;
                    if (mp.y <= edgePixels) move.y -= 1f;
                    else if (mp.y >= Screen.height - edgePixels) move.y += 1f;
                }

                if (!overUI)
                {
                    float wheel = mouse.scroll.ReadValue().y;
                    if (Mathf.Abs(wheel) > 0.01f)
                        targetDistance *= 1f - Mathf.Sign(wheel) * zoomStep;
                }
                targetDistance = Mathf.Clamp(targetDistance, distanceLimits.x, distanceLimits.y);

                if (mouse.middleButton.isPressed)
                {
                    Vector2 delta = mouse.delta.ReadValue();
                    float worldPerPixel = 2f * distance * Mathf.Tan(GetFovRadians() * 0.5f) / Mathf.Max(1, Screen.height);
                    Quaternion flat = Quaternion.Euler(0f, yaw, 0f);
                    focus -= flat * new Vector3(delta.x, 0f, delta.y) * worldPerPixel;
                }
            }

            if (move.sqrMagnitude > 0f)
            {
                Quaternion flat = Quaternion.Euler(0f, yaw, 0f);
                focus += flat * new Vector3(move.x, 0f, move.y).normalized * (panSpeed * distance * dt);
            }
            focus.x = Mathf.Clamp(focus.x, -boundsHalfSize, boundsHalfSize);
            focus.z = Mathf.Clamp(focus.z, -boundsHalfSize, boundsHalfSize);

            float k = 1f - Mathf.Exp(-smoothing * dt);
            distance = Mathf.Lerp(distance, targetDistance, k);
            yaw = Mathf.LerpAngle(yaw, targetYaw, k);
            Apply();
        }

        float GetFovRadians()
        {
            return (cam != null ? cam.fieldOfView : 60f) * Mathf.Deg2Rad;
        }

        void Apply()
        {
            Quaternion rot = Quaternion.Euler(pitch, yaw, 0f);
            transform.SetPositionAndRotation(focus - rot * Vector3.forward * distance, rot);
        }
    }
}
