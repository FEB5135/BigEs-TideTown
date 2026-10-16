using System.Collections;
using UnityEngine;

// Idle toplayıcı köylü: en yakın boş kaynağa gider, toplar, kuyuya getirip içine atar. Sonsuz döngü.
public class Gatherer : MonoBehaviour
{
    [SerializeField] ResourceType resourceType = ResourceType.Wood;
    [SerializeField] int carryCapacity = 5;
    [SerializeField] float harvestDuration = 2f;
    [SerializeField] float moveSpeed = 3f;
    [Tooltip("Kaynağa bu mesafeye gelince durur.")]
    [SerializeField] float stopDistance = 1.5f;
    [Tooltip("Kuyunun merkezine bu mesafeye gelince durup atar.")]
    [SerializeField] float wellStopDistance = 3f;
    [Tooltip("Taşırken gösterilecek obje (örn. sırttaki odun). Opsiyonel.")]
    [SerializeField] GameObject carryVisual;
    [Tooltip("Zemine yapışmak için aşağı doğru atılan ışının çarpacağı katmanlar.")]
    [SerializeField] LayerMask groundMask = ~0;

    readonly RaycastHit[] groundHits = new RaycastHit[8];
    float heightOffset;

    IEnumerator Start()
    {
        // Pivot ile zemin arasındaki mesafe korunur (örn. capsule'ün merkezi zeminin üstünde).
        if (TryGetGroundHeight(transform.position, out float groundY))
            heightOffset = transform.position.y - groundY;
        SetCarrying(false);
        while (true)
        {
            var well = Well.Instance;
            var node = ResourceNode.FindNearestFree(resourceType, transform.position);
            if (well == null || node == null)
            {
                yield return new WaitForSeconds(1f);
                continue;
            }

            node.Claim(this);
            yield return MoveTo(node.transform.position, stopDistance);
            if (node == null)
                continue;
            yield return new WaitForSeconds(harvestDuration);
            if (node == null)
                continue;
            int carried = node.Harvest(carryCapacity);
            node.Release(this);
            if (carried <= 0)
                continue;

            SetCarrying(true);
            yield return MoveTo(well.transform.position, wellStopDistance);
            SetCarrying(false);
            if (well != null)
                well.Throw(resourceType, carried, transform.position + Vector3.up);
        }
    }

    IEnumerator MoveTo(Vector3 target, float distance)
    {
        while (true)
        {
            var toTarget = target - transform.position;
            toTarget.y = 0f;
            if (toTarget.magnitude <= distance)
                yield break;

            var step = Vector3.ClampMagnitude(toTarget, moveSpeed * Time.deltaTime);
            var next = transform.position + step;
            if (TryGetGroundHeight(next, out float groundY))
                next.y = groundY + heightOffset;
            transform.position = next;
            transform.rotation = Quaternion.LookRotation(toTarget);
            yield return null;
        }
    }

    bool TryGetGroundHeight(Vector3 position, out float height)
    {
        int count = Physics.RaycastNonAlloc(position + Vector3.up * 50f, Vector3.down, groundHits, 100f, groundMask, QueryTriggerInteraction.Ignore);
        float bestDistance = float.MaxValue;
        height = position.y;
        for (int i = 0; i < count; i++)
        {
            var hit = groundHits[i];
            if (hit.transform.IsChildOf(transform)
                || hit.collider.GetComponentInParent<ResourceNode>() != null
                || hit.collider.GetComponentInParent<Well>() != null)
                continue;
            if (hit.distance < bestDistance)
            {
                bestDistance = hit.distance;
                height = hit.point.y;
            }
        }
        return bestDistance < float.MaxValue;
    }

    void SetCarrying(bool carrying)
    {
        if (carryVisual != null)
            carryVisual.SetActive(carrying);
    }
}
