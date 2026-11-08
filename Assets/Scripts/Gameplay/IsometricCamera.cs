using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

// 45 derece izometrik bakış. Fare tekerleği ile min-max mesafe arasında yakınlaşır/uzaklaşır,
// WASD, kamerayı ekran yönlerine göre kaydırır.
[RequireComponent(typeof(Camera))]
public class IsometricCamera : MonoBehaviour
{
    [Tooltip("Kameranın baktığı nokta.")]
    [SerializeField] Vector3 focusPoint = Vector3.zero;
    [SerializeField] float pitch = 45f;
    [SerializeField] float yaw = 45f;

    [Header("Zoom")]
    [SerializeField] float minDistance = 15f;
    [SerializeField] float maxDistance = 160f;
    [SerializeField] float distance = 70f;
    [Tooltip("Tekerleğin her çentiğinde değişen mesafe.")]
    [SerializeField] float zoomStep = 8f;
    [SerializeField] float zoomSmoothTime = 0.12f;

    [Header("Kaydırma (WASD)")]
    [Tooltip("Odak noktasının gidebileceği alanın merkezi.")]
    [SerializeField] Vector3 panCenter = Vector3.zero;
    [Tooltip("Merkezden X ve Z yönünde en fazla ne kadar kayabileceği.")]
    [SerializeField] Vector2 panExtents = new Vector2(120f, 120f);
    [SerializeField, Min(0f)] float panSpeed = 35f;

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

    public float Distance => distance;
    public float Pitch => pitch;
    public float Yaw => yaw;

    // Kapalıyken zoom ve kaydırma çalışmaz (menü, açılış geçişi).
    public bool InputEnabled { get; set; } = true;

    // Kamerayı doğrudan bir duruşa koyar. Mesafe min-max sınırına bağlı değildir (menüde çok uzaktan bakmak için).
    public void SetPose(Vector3 focus, float newDistance, float newPitch, float newYaw)
    {
        focusPoint = focus;
        distance = targetDistance = newDistance;
        zoomVelocity = 0f;
        pitch = newPitch;
        yaw = newYaw;
        Apply();
    }

    void Awake()
    {
        distance = Mathf.Clamp(distance, minDistance, maxDistance);
        targetDistance = distance;
        Apply();
    }

    void LateUpdate()
    {
        if (InputEnabled)
        {
            if (Mouse.current is { } mouse)
            {
                float scroll = mouse.scroll.ReadValue().y;
                bool overUI = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
                if (scroll != 0f && !overUI)
                    targetDistance = Mathf.Clamp(targetDistance - Mathf.Sign(scroll) * zoomStep, minDistance, maxDistance);
            }

            if (Keyboard.current is { } keyboard)
                Pan(keyboard, Time.unscaledDeltaTime);
        }

        // Gerçek zamanla: tutorial oyunu durdurduğunda da kamera çalışsın.
        distance = Mathf.SmoothDamp(distance, targetDistance, ref zoomVelocity, zoomSmoothTime, Mathf.Infinity, Time.unscaledDeltaTime);
        Apply();
    }

    void Pan(Keyboard keyboard, float deltaTime)
    {
        float horizontal = (keyboard.dKey.isPressed ? 1f : 0f) - (keyboard.aKey.isPressed ? 1f : 0f);
        float vertical = (keyboard.wKey.isPressed ? 1f : 0f) - (keyboard.sKey.isPressed ? 1f : 0f);
        var direction = Vector3.ClampMagnitude(new Vector3(horizontal, 0f, vertical), 1f);
        if (direction == Vector3.zero) return;

        focusPoint += Quaternion.Euler(0f, yaw, 0f) * direction * (panSpeed * deltaTime);
        focusPoint.x = Mathf.Clamp(focusPoint.x, panCenter.x - panExtents.x, panCenter.x + panExtents.x);
        focusPoint.z = Mathf.Clamp(focusPoint.z, panCenter.z - panExtents.y, panCenter.z + panExtents.y);
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
