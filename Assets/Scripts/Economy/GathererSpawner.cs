using System.Collections.Generic;
using UnityEngine;

// Kaynak kendi toplayıcılarını üretir (örn. her maden ocağı kendi vagonunu). Toplayıcı yalnız bu kaynağa çalışır.
// Harita her oyunda rastgele kurulduğu için ocak sayısı ve yeri ne olursa olsun vagonlar kendiliğinden gelir.
[RequireComponent(typeof(ResourceNode))]
public class GathererSpawner : MonoBehaviour
{
    [SerializeField] Gatherer prefab;
    [SerializeField] int count = 1;
    [Tooltip("Üretilen toplayıcılar (vagonlar) yalnız bir köylü iterken ilerler.")]
    [SerializeField] bool needsPusher = true;

    readonly List<Gatherer> spawned = new();

    public IReadOnlyList<Gatherer> Spawned => spawned;

    // Kimsenin itmediği ve kimsenin ona yürümediği ilk vagon (yoksa null).
    public Gatherer FreeCart()
    {
        foreach (var cart in spawned)
            if (cart != null && !cart.IsReserved)
                return cart;
        return null;
    }

    void Start()
    {
        if (prefab == null)
            return;
        var node = GetComponent<ResourceNode>();
        // Yolun başında (ray başı), içeri bakarak başlar; yol yoksa kaynağın önünde.
        var path = node.ApproachPath;
        var start = path != null && path.Length > 0 && path[0] != null ? path[0] : transform;
        var inward = path != null && path.Length > 1 && path[1] != null ? path[1].position - start.position : -transform.forward;
        inward.y = 0f;
        var rotation = inward.sqrMagnitude > 0.001f ? Quaternion.LookRotation(inward) : Quaternion.identity;
        for (int i = 0; i < count; i++)
        {
            var gatherer = Instantiate(prefab, start.position - inward.normalized * (i * 2.5f), rotation);
            gatherer.name = $"{prefab.name} ({name})";
            gatherer.SetHome(node);
            gatherer.NeedsPusher = needsPusher;
            spawned.Add(gatherer);
        }
    }
}
