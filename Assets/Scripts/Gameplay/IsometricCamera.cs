using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

// 45 derece izometrik bakış. Fare tekerleği ile min-max mesafe arasında yakınlaşır/uzaklaşır,
// sağ tıkla sürükleyince harita kayar (zemin imlecin altında sabit kalır).
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

    [Header("Kaydırma (sağ tık)")]
    [Tooltip("Odak noktasının gidebileceği alanın merkezi.")]
    [SerializeField] Vector3 panCenter = Vector3.zero;
    [Tooltip("Merkezden X ve Z yönünde en fazla ne kadar kayabileceği.")]
    [SerializeField] Vector2 panExtents = new Vector2(120f, 120f);
    [Tooltip("Kapalı: harita imleçle birlikte sürüklenir. Açık: kamera imlecin gittiği yöne kayar.")]
    [SerializeField] bool invertPan;

    Camera cam;
    float targetDistance;
    float zoomVelocity;
    bool panning;
    Vector2 lastPointer;

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
        cam = GetComponent<Camera>();
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

            Pan(mouse, overUI);
        }

        distance = Mathf.SmoothDamp(distance, targetDistance, ref zoomVelocity, zoomSmoothTime);
        Apply();
    }

    void Pan(Mouse mouse, bool overUI)
    {
        var pointer = mouse.position.ReadValue();
        if (mouse.rightButton.wasPressedThisFrame && !overUI)
            panning = true;
        if (!mouse.rightButton.isPressed)
            panning = false;

        if (panning && TryGroundPoint(lastPointer, out var from) && TryGroundPoint(pointer, out var to))
        {
            focusPoint += invertPan ? to - from : from - to;
            focusPoint.x = Mathf.Clamp(focusPoint.x, panCenter.x - panExtents.x, panCenter.x + panExtents.x);
            focusPoint.z = Mathf.Clamp(focusPoint.z, panCenter.z - panExtents.y, panCenter.z + panExtents.y);
        }
        lastPointer = pointer;
    }

    // Ekran noktasının odak yüksekliğindeki yatay düzlemdeki karşılığı.
    bool TryGroundPoint(Vector2 screenPoint, out Vector3 point)
    {
        var ray = cam.ScreenPointToRay(screenPoint);
        var ground = new Plane(Vector3.up, new Vector3(0f, focusPoint.y, 0f));
        if (ground.Raycast(ray, out float enter))
        {
            point = ray.GetPoint(enter);
            return true;
        }
        point = default;
        return false;
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
