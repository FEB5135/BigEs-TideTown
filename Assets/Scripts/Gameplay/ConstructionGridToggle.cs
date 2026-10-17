using IdleTown.Construction;
using UnityEngine;

// İnşa grid'inin çizgilerini yalnız inşa modunda gösterir; toplama modunda zemin düz görünür.
[RequireComponent(typeof(Renderer))]
public class ConstructionGridToggle : MonoBehaviour
{
    [SerializeField] WallConstructionController controller;

    Renderer gridRenderer;

    public void Configure(WallConstructionController target) => controller = target;

    void Awake()
    {
        gridRenderer = GetComponent<Renderer>();
        if (controller == null)
            controller = FindAnyObjectByType<WallConstructionController>();
    }

    void LateUpdate()
    {
        gridRenderer.enabled = controller != null && controller.isActiveAndEnabled && controller.ConstructionModeActive;
    }
}
