using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

// 45 derece izometrik bakış. Fare tekerleği ile min-max mesafe arasında yakınlaşır/uzaklaşır.
[RequireComponent(typeof(Camera))]
public class IsometricCamera : MonoBehaviour
{
    [Tooltip("Kameranın baktığı nokta.")]
    [SerializeField] Vector3 focusPoint = Vector3.zero;
    [SerializeField] float pitch = 45f;
    [SerializeField] float yaw = 45f;

    [Header("Zoom")]
    [SerializeField] float minDistance = 12f;
    [SerializeField] float maxDistance = 45f;
    [SerializeField] float distance = 30f;
    [Tooltip("Tekerleğin her çentiğinde değişen mesafe.")]
    [SerializeField] float zoomStep = 4f;
    [SerializeField] float zoomSmoothTime = 0.12f;

    float targetDistance;
    float zoomVelocity;

    public Vector3 FocusPoint
    {
        get => focusPoint;
        set
        {
            focusPoint = value;
            Apply();
        }
    }

    void Awake()
    {
        distance = Mathf.Clamp(distance, minDistance, maxDistance);
        targetDistance = distance;
        Apply();
    }

    void LateUpdate()
    {
        var mouse = Mouse.current;
        if (mouse != null)
        {
            float scroll = mouse.scroll.ReadValue().y;
            bool overUI = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
            if (scroll != 0f && !overUI)
                targetDistance = Mathf.Clamp(targetDistance - Mathf.Sign(scroll) * zoomStep, minDistance, maxDistance);
        }

        distance = Mathf.SmoothDamp(distance, targetDistance, ref zoomVelocity, zoomSmoothTime);
        Apply();
    }

    void Apply()
    {
        var rotation = Quaternion.Euler(pitch, yaw, 0f);
        transform.SetPositionAndRotation(focusPoint - rotation * Vector3.forward * distance, rotation);
    }

    void OnValidate()
    {
        maxDistance = Mathf.Max(minDistance, maxDistance);
        distance = Mathf.Clamp(distance, minDistance, maxDistance);
        Apply();
    }
}
