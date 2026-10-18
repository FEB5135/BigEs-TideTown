using System;
using System.Collections.Generic;
using UnityEngine;

// Yükseltilebilir yapı (her taş ocağı ayrı, orman tek): seviye 1'den başlar, kuyudaki odun ve taşla en fazla
// son seviyeye çıkar. Seviye arttıkça ona bağlı toplayıcılar (ocağın vagonları, oduncular) daha hızlı yürür ve
// daha kısa sürede toplar (Gatherer). Görseller şimdilik değişmez. Butonu UpgradeHUD çizer.
public class UpgradeLevel : MonoBehaviour
{
    [Serializable]
    public class Step
    {
        [Tooltip("Bu seviyeye çıkmanın bedeli (seviye 1'inki kullanılmaz).")]
        public int wood;
        public int stone;
        [Tooltip("Yürüme/vagon hızı çarpanı.")]
        public float speed = 1f;
        [Tooltip("Toplama süresi çarpanı (küçüldükçe hızlanır).")]
        public float workTime = 1f;
    }

    static readonly List<UpgradeLevel> all = new();

    [SerializeField] string displayName = "Taş Ocağı";
    [Tooltip("Ormanın yükseltmesi: ev kaynağı olmayan oduncular buna bağlıdır. Sahnede bir tane olur.")]
    [SerializeField] bool forest;
    [Tooltip("Butonun duracağı nokta. Boşsa modelin tepesinin biraz üstü.")]
    [SerializeField] Transform anchor;
    [Tooltip("Seviye 1, 2, 3...")]
    [SerializeField] Step[] steps =
    {
        new(),
        new() { wood = 20, stone = 10, speed = 1.35f, workTime = 0.7f },
        new() { wood = 45, stone = 30, speed = 1.7f, workTime = 0.45f },
    };

    int level = 1;
    Vector3? buttonPoint;

    public static IReadOnlyList<UpgradeLevel> All => all;
    public static UpgradeLevel Forest { get; private set; }

    // Yeni seviye.
    public event Action<int> Changed;

    public string DisplayName => displayName;
    public bool IsForest => forest;
    public int Level => level;
    public int MaxLevel => Mathf.Max(1, steps.Length);
    public bool IsMax => level >= MaxLevel;
    public float SpeedMultiplier => Current?.speed ?? 1f;
    public int MineWorkerCapacity => level >= 3 ? 5 : level == 2 ? 4 : 2;
    public float WorkTimeMultiplier => (Current?.workTime ?? 1f) / (!forest && level >= 3 ? 1.5f : 1f);

    Step Current => steps.Length > 0 ? steps[Mathf.Clamp(level - 1, 0, steps.Length - 1)] : null;

    void OnEnable()
    {
        if (!all.Contains(this))
            all.Add(this);
        if (forest)
            Forest = this;
    }

    void OnDisable()
    {
        all.Remove(this);
        if (Forest == this)
            Forest = null;
    }

    public bool TryGetNextCost(out int wood, out int stone)
    {
        wood = stone = 0;
        if (IsMax)
            return false;
        wood = steps[level].wood;
        stone = steps[level].stone;
        return true;
    }

    public bool CanUpgrade(Well well) => well != null && TryGetNextCost(out int wood, out int stone) && well.CanAfford(wood, stone);

    // Bedeli kuyudan öder ve bir seviye çıkar; parası yetmezse ya da son seviyedeyse hiçbir şey yapmaz.
    public bool TryUpgrade(Well well)
    {
        if (well == null || !TryGetNextCost(out int wood, out int stone) || !well.TrySpend(wood, stone))
            return false;
        level++;
        Changed?.Invoke(level);
        return true;
    }

    // Butonun dünyadaki yeri: anchor ya da modelin tepesi (bir kez hesaplanır; ocak tükenip gizlense de değişmez).
    public Vector3 ButtonPoint
    {
        get
        {
            if (anchor != null)
                return anchor.position;
            if (buttonPoint is Vector3 point)
                return point;
            Bounds? bounds = null;
            foreach (var renderer in GetComponentsInChildren<Renderer>())
            {
                if (bounds is Bounds b)
                {
                    b.Encapsulate(renderer.bounds);
                    bounds = b;
                }
                else
                    bounds = renderer.bounds;
            }
            point = bounds is Bounds box ? new Vector3(box.center.x, box.max.y + 1.5f, box.center.z) : transform.position + Vector3.up * 3f;
            buttonPoint = point;
            return point;
        }
    }
}
