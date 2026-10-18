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
    ResourceNode node;
    UpgradeLevel upgrade;

    public IReadOnlyList<Gatherer> Spawned => spawned;

    // Kimsenin itmediği ve kimsenin ona yürümediği ilk vagon (yoksa null).
    public Gatherer FreeCart()
    {
        foreach (var cart in spawned)
            if (cart != null && cart.isActiveAndEnabled && !cart.IsReserved)
                return cart;
        return null;
    }

    void Start()
    {
        node = GetComponent<ResourceNode>();
        upgrade = node.Upgrade;
        if (upgrade != null) upgrade.Changed += OnUpgraded;
        EnsureCapacity();
    }

    void OnUpgraded(int level) => EnsureCapacity();

    void OnDestroy()
    {
        if (upgrade != null) upgrade.Changed -= OnUpgraded;
    }

    void EnsureCapacity()
    {
        if (prefab == null)
            return;
        spawned.RemoveAll(cart => cart == null);
        int capacity = node.Type == ResourceType.Stone ? node.WorkerCapacity : count;
        // Yolun başında (ray başı), içeri bakarak başlar; yol yoksa kaynağın önünde.
        var path = node.ApproachPath;
        var start = path != null && path.Length > 0 && path[0] != null ? path[0] : transform;
        var inward = path != null && path.Length > 1 && path[1] != null ? path[1].position - start.position : -transform.forward;
        inward.y = 0f;
        var rotation = inward.sqrMagnitude > 0.001f ? Quaternion.LookRotation(inward) : Quaternion.identity;
        for (int i = spawned.Count; i < capacity; i++)
        {
            var gatherer = Instantiate(prefab, start.position - inward.normalized * (i * 2.5f), rotation);
            gatherer.name = $"{prefab.name} ({name})";
            gatherer.SetHome(node);
            gatherer.NeedsPusher = needsPusher;
            if (gatherer.GetComponent<CartSound>() == null)
                gatherer.gameObject.AddComponent<CartSound>();
            spawned.Add(gatherer);
        }
    }
}
