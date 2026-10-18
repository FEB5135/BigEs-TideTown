using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Toplanabilir kaynak noktası (ağaç, kaya...). Tükenince gizlenir, süre ayarlıysa yeniden çıkar.
// Model değişebilir: üzerinde collider yoksa görselin sınırlarına göre otomatik eklenir,
// böylece tıklama ve toplama yeni modelle de çalışır.
public class ResourceNode : MonoBehaviour
{
    static readonly List<ResourceNode> active = new();

    [SerializeField] ResourceType type = ResourceType.Wood;
    [SerializeField] int capacity = 20;
    [Tooltip("Tükendikten kaç saniye sonra yeniden çıkar. 0 = bir daha çıkmaz.")]
    [SerializeField] float regrowSeconds = 30f;
    [Tooltip("Aynı anda kaç toplayıcı çalışabilir (ağaç 1, maden ocağı birkaç vagon).")]
    [SerializeField] int maxGatherers = 1;
    [Tooltip("Tükenince yalnız bu gizlenir (örn. ocaktaki taş yığını). Boşsa tüm model gizlenir.")]
    [SerializeField] GameObject depletedHide;
    [Tooltip("Toplayıcı kaynağa bu noktalardan sırayla geçerek girer, toplayınca aynı yoldan geri çıkar (örn. ray başı, tünel içi). Boşsa doğrudan gider.")]
    [SerializeField] Transform[] approachPath;

    [Header("Tıklama Geri Bildirimi")]
    [SerializeField] float hitShakeDuration = 0.2f;
    [SerializeField] float hitShakeAngle = 6f;

    int remaining;
    readonly List<Gatherer> claimants = new();
    Coroutine shake;
    Quaternion restRotation;

    // Tükenince false, yeniden çıkınca true (örn. grid'deki hücreyi zemine/ağaca çevirmek için).
    public event Action<bool> VisibilityChanged;
    // Tıklanınca ya da toplayıcı vurunca (örn. grid'de çizilen ağacı sallamak için).
    public event Action Hit;

    public ResourceType Type => type;
    public int Remaining => remaining;
    public bool IsAvailable => remaining > 0 && claimants.Count < maxGatherers;
    public Transform[] ApproachPath => approachPath;
    // Ocağın seviyesi (yoksa null); ocağın vagonları buna göre hızlanır.
    public UpgradeLevel Upgrade { get; private set; }

    void Awake()
    {
        Upgrade = GetComponent<UpgradeLevel>();
        remaining = capacity;
        restRotation = transform.localRotation;
        EnsureCollider();
    }

    void EnsureCollider()
    {
        if (GetComponentInChildren<Collider>() != null)
            return;
        var renderers = GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0)
            return;

        var bounds = renderers[0].bounds;
        foreach (var r in renderers)
            bounds.Encapsulate(r.bounds);

        // Sınırlar dünya biriminde, collider değerleri yerel birimde.
        var capsule = gameObject.AddComponent<CapsuleCollider>();
        var scale = transform.lossyScale;
        float horizontalScale = Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z), 0.0001f);
        float verticalScale = Mathf.Max(Mathf.Abs(scale.y), 0.0001f);
        capsule.center = transform.InverseTransformPoint(bounds.center);
        capsule.height = bounds.size.y / verticalScale;
        capsule.radius = Mathf.Min(bounds.extents.x, bounds.extents.z) / horizontalScale;
    }

    void OnEnable() => active.Add(this);

    void OnDisable()
    {
        active.Remove(this);
        claimants.Clear();
    }

    public static ResourceNode FindNearestFree(ResourceType type, Vector3 position) => FindNearestFree(type, position, null);

    // skip: atlanacak kaynaklar (örn. toplayıcının yolunu bulamadığı ağaçlar).
    public static ResourceNode FindNearestFree(ResourceType type, Vector3 position, System.Func<ResourceNode, bool> skip)
    {
        ResourceNode best = null;
        float bestSqr = float.MaxValue;
        foreach (var node in active)
        {
            if (node.type != type || !node.IsAvailable || (skip != null && skip(node)))
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

    public void Claim(Gatherer gatherer)
    {
        if (!claimants.Contains(gatherer))
            claimants.Add(gatherer);
    }

    public void Release(Gatherer gatherer) => claimants.Remove(gatherer);

    public int Harvest(int max)
    {
        int taken = Mathf.Min(max, remaining);
        remaining -= taken;
        if (taken > 0 && remaining <= 0)
            StartCoroutine(DepleteRoutine());
        return taken;
    }

    public void PlayHitFeedback()
    {
        if (!isActiveAndEnabled || remaining <= 0)
            return;
        Hit?.Invoke();
        // Ağaçta balta, ocakta tok taş sesi.
        if (GameAudio.Instance != null)
            GameAudio.Play(type == ResourceType.Wood ? GameAudio.Instance.treeHit : GameAudio.Instance.stoneHit, transform.position);
        if (shake != null)
            StopCoroutine(shake);
        shake = StartCoroutine(ShakeRoutine());
    }

    IEnumerator ShakeRoutine()
    {
        for (float t = 0f; t < hitShakeDuration; t += Time.deltaTime)
        {
            float angle = Mathf.Sin(t / hitShakeDuration * Mathf.PI * 4f) * hitShakeAngle * (1f - t / hitShakeDuration);
            transform.localRotation = restRotation * Quaternion.Euler(angle, 0f, 0f);
            yield return null;
        }
        transform.localRotation = restRotation;
        shake = null;
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
        if (!visible && shake != null)
        {
            StopCoroutine(shake);
            shake = null;
            transform.localRotation = restRotation;
        }
        if (depletedHide != null)
            depletedHide.SetActive(visible);
        else
            foreach (var r in GetComponentsInChildren<Renderer>())
                r.enabled = visible;
        foreach (var c in GetComponentsInChildren<Collider>())
            c.enabled = visible;
        VisibilityChanged?.Invoke(visible);
    }
}
