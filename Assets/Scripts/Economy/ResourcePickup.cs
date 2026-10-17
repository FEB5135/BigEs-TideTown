using System.Collections;
using UnityEngine;

// Kuyuya atılan kaynak parçası (odun, taş). Kuyunun ağzındaki tetikleyiciye girince depoya eklenir.
// Bir şeye takılıp kuyuya hiç düşmezse süre dolunca yine eklenir, kaynak kaybolmaz.
[RequireComponent(typeof(Rigidbody))]
public class ResourcePickup : MonoBehaviour
{
    [SerializeField] float sinkDuration = 0.25f;

    Well well;
    float fallbackTime;

    public ResourceType Type { get; private set; }
    public int Amount { get; private set; }
    public bool Collected { get; private set; }

    public void Init(Well target, ResourceType type, int amount, float timeout)
    {
        well = target;
        Type = type;
        Amount = amount;
        fallbackTime = Time.time + timeout;
    }

    void Update()
    {
        if (!Collected && well != null && Time.time >= fallbackTime)
            Collect();
    }

    public void Collect()
    {
        if (Collected || well == null)
            return;
        Collected = true;
        well.Deposit(Type, Amount);
        StartCoroutine(SinkRoutine());
    }

    IEnumerator SinkRoutine()
    {
        foreach (var c in GetComponentsInChildren<Collider>())
            c.enabled = false;
        var body = GetComponent<Rigidbody>();
        body.isKinematic = true;

        var startScale = transform.localScale;
        for (float t = 0f; t < sinkDuration; t += Time.deltaTime)
        {
            transform.localScale = Vector3.Lerp(startScale, Vector3.zero, t / sinkDuration);
            transform.position += Vector3.down * Time.deltaTime;
            yield return null;
        }
        Destroy(gameObject);
    }
}
