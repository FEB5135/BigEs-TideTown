using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

// Sol tıkla bir kaynağa (ağaç...) tıklayınca kaynak kuyuya fırlatılır.
[RequireComponent(typeof(Camera))]
public class ClickHarvester : MonoBehaviour
{
    [SerializeField] int amountPerClick = 1;
    [SerializeField] float maxRayDistance = 200f;

    Camera cam;

    void Awake()
    {
        cam = GetComponent<Camera>();
    }

    void Update()
    {
        var mouse = Mouse.current;
        if (mouse == null || !mouse.leftButton.wasPressedThisFrame)
            return;
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            return;

        var ray = cam.ScreenPointToRay(mouse.position.ReadValue());
        if (!Physics.Raycast(ray, out var hit, maxRayDistance, ~0, QueryTriggerInteraction.Ignore))
            return;

        var node = hit.collider.GetComponentInParent<ResourceNode>();
        var well = Well.Instance;
        if (node == null || well == null)
            return;

        int taken = node.Harvest(amountPerClick);
        if (taken <= 0)
            return;
        node.PlayHitFeedback();
        // Tıklanan noktadan kuyuya fırlatılır; miktar kuyuya düşünce artar.
        well.Throw(node.Type, taken, hit.point);
    }
}
