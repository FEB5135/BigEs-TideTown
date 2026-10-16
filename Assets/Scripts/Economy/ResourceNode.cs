using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Toplanabilir kaynak noktası (ağaç, kaya...). Tükenince gizlenir, süre ayarlıysa yeniden çıkar.
public class ResourceNode : MonoBehaviour
{
    static readonly List<ResourceNode> active = new();

    [SerializeField] ResourceType type = ResourceType.Wood;
    [SerializeField] int capacity = 20;
    [Tooltip("Tükendikten kaç saniye sonra yeniden çıkar. 0 = bir daha çıkmaz.")]
    [SerializeField] float regrowSeconds = 30f;

    int remaining;
    Gatherer claimant;

    public ResourceType Type => type;
    public int Remaining => remaining;
    public bool IsAvailable => remaining > 0 && claimant == null;

    void Awake()
    {
        remaining = capacity;
    }

    void OnEnable() => active.Add(this);

    void OnDisable()
    {
        active.Remove(this);
        claimant = null;
    }

    public static ResourceNode FindNearestFree(ResourceType type, Vector3 position)
    {
        ResourceNode best = null;
        float bestSqr = float.MaxValue;
        foreach (var node in active)
        {
            if (node.type != type || !node.IsAvailable)
                continue;
            float sqr = (node.transform.position - position).sqrMagnitude;
            if (sqr < bestSqr)
            {
                bestSqr = sqr;
                best = node;
            }
        }
        return best;
    }

    public void Claim(Gatherer gatherer) => claimant = gatherer;

    public void Release(Gatherer gatherer)
    {
        if (claimant == gatherer)
            claimant = null;
    }

    public int Harvest(int max)
    {
        int taken = Mathf.Min(max, remaining);
        remaining -= taken;
        if (taken > 0 && remaining <= 0)
            StartCoroutine(DepleteRoutine());
        return taken;
    }

    IEnumerator DepleteRoutine()
    {
        SetVisible(false);
        if (regrowSeconds <= 0f)
            yield break;
        yield return new WaitForSeconds(regrowSeconds);
        remaining = capacity;
        SetVisible(true);
    }

    void SetVisible(bool visible)
    {
        foreach (var r in GetComponentsInChildren<Renderer>())
            r.enabled = visible;
        foreach (var c in GetComponentsInChildren<Collider>())
            c.enabled = visible;
    }
}
