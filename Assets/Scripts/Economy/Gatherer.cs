using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Idle toplayıcı: kaynağa gider, toplar, kuyuya getirip içine atar. Sonsuz döngü.
// Ev kaynağı verilirse (vagonun ocağı) yalnız oraya gider; verilmezse en yakın boş kaynağı arar (oduncu).
// Sahnede NavigationBaker varsa engellerin etrafından dolaşır, yoksa düz çizgide yürür.
public class Gatherer : MonoBehaviour
{
    [SerializeField] ResourceType resourceType = ResourceType.Wood;
    [Tooltip("Ev kaynağı (örn. vagonun ocağı). Boşsa en yakın boş kaynak aranır.")]
    [SerializeField] ResourceNode home;
    [SerializeField] int carryCapacity = 5;
    [SerializeField] float harvestDuration = 2f;
    [SerializeField] float moveSpeed = 3f;
    [Tooltip("Saniyede en fazla kaç derece döner (köşelerde yumuşak dönüş).")]
    [SerializeField] float turnSpeed = 360f;
    [Tooltip("Kaynağa bu mesafeye gelince durur.")]
    [SerializeField] float stopDistance = 1.5f;
    [Tooltip("Kuyunun merkezine bu mesafeye gelince durup atar.")]
    [SerializeField] float wellStopDistance = 3f;
    [Tooltip("Taşırken gösterilecek obje (örn. sırttaki odun). Opsiyonel.")]
    [SerializeField] GameObject carryVisual;
    [Tooltip("Zemine yapışmak için aşağı doğru atılan ışının çarpacağı katmanlar.")]
    [SerializeField] LayerMask groundMask = ~0;

    const float PathStopDistance = 0.1f;

    readonly RaycastHit[] groundHits = new RaycastHit[8];
    readonly List<Vector3> corners = new();
    float heightOffset;

    public ResourceType Type => resourceType;

    public void SetHome(ResourceNode node) => home = node;

    IEnumerator Start()
    {
        // Pivot ile zemin arasındaki mesafe korunur (örn. capsule'ün merkezi zeminin üstünde).
        if (TryGetGroundHeight(transform.position, out float groundY))
            heightOffset = transform.position.y - groundY;
        SetCarrying(false);
        // Harita kurulup NavMesh hazır olana kadar beklenir.
        while (NavigationBaker.Exists && !NavigationBaker.Ready)
            yield return null;

        while (true)
        {
            var well = Well.Instance;
            var node = home != null ? home : ResourceNode.FindNearestFree(resourceType, transform.position);
            if (well == null || node == null)
            {
                yield return new WaitForSeconds(1f);
                continue;
            }

            // Yolu olan kaynakta (maden ocağı) önce yolun başına gidilir, oradan yol boyunca içeri girilir.
            var path = PathPoints(node);
            if (home != null && home.Remaining <= 0)
            {
                // Ocak tükenmiş: girişte yenilenmesini bekler.
                yield return TravelTo(path.Length > 0 ? path[0] : node.transform.position, stopDistance);
                yield return new WaitForSeconds(1f);
                continue;
            }

            node.Claim(this);
            if (path.Length > 0)
            {
                yield return TravelTo(path[0], PathStopDistance);
                for (int i = 1; i < path.Length; i++)
                    yield return MoveTo(path[i], PathStopDistance);
            }
            else
                yield return TravelTo(node.transform.position, stopDistance);
            if (node == null)
                continue;
            yield return new WaitForSeconds(harvestDuration);
            if (node == null)
                continue;
            int carried = node.Harvest(carryCapacity);
            node.Release(this);
            SetCarrying(carried > 0);
            for (int i = path.Length - 2; i >= 0; i--)
                yield return MoveTo(path[i], PathStopDistance);
            if (carried <= 0)
                continue;

            // Kuyunun bu taraftaki kenarına gidilir; ortasına yol aranırsa kuyunun etrafında dolanır.
            float approach = Mathf.Max(wellStopDistance, well.ApproachRadius);
            var fromWell = transform.position - well.transform.position;
            fromWell.y = 0f;
            var rim = well.transform.position + (fromWell.sqrMagnitude > 0.01f ? fromWell.normalized : Vector3.forward) * (approach + 0.5f);
            yield return TravelTo(rim, 0.5f);
            SetCarrying(false);
            if (well != null)
                well.Throw(resourceType, carried, transform.position + Vector3.up);
        }
    }

    static Vector3[] PathPoints(ResourceNode node)
    {
        var path = node.ApproachPath;
        if (path == null)
            return System.Array.Empty<Vector3>();
        return System.Array.ConvertAll(System.Array.FindAll(path, p => p != null), p => p.position);
    }

    // NavMesh varsa engellerin etrafından, yoksa düz çizgide gider.
    IEnumerator TravelTo(Vector3 target, float distance)
    {
        if (NavigationBaker.TryFindPath(transform.position, target, corners))
        {
            var points = corners.ToArray();
            for (int i = 1; i < points.Length; i++)
                yield return MoveTo(points[i], PathStopDistance);
        }
        // Hedef bir engelin içindeyse NavMesh kenarında biter; kalan kısa mesafe düz gidilir.
        yield return MoveTo(target, distance);
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
            transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(toTarget), turnSpeed * Time.deltaTime);
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
