using System.Collections.Generic;
using IdleTown.Construction;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.UI;

// Geçici yükseltme arayüzü (HUD'da):
// - her yükseltilebilir yapının (her taş ocağı ayrı, orman) üstünde ekranda onu takip eden küçük buton; yalnız
//   kuyudaki odun ve taş bir sonraki seviyeye yetince belirir, tıklayınca o bedelle yükseltir;
// - taş butonunun yanında odun ve taşla yeni işçi (köylü) üreten buton; işçi kuyunun yanında doğar ve dolaşır.
// Butonlar sahnedeki taş butonundan kopyalanır, böylece görünüm aynı kalır.
[RequireComponent(typeof(Canvas))]
public class UpgradeHUD : MonoBehaviour
{
    [Tooltip("Kopyalanacak buton (sol alttaki taş butonu). İşçi butonu bunun yanına konur.")]
    [SerializeField] RectTransform template;
    [SerializeField] Sprite woodIcon;
    [SerializeField] Sprite stoneIcon;
    [Tooltip("Yapıların üstündeki butonların boyutu (taş butonuna göre).")]
    [SerializeField] float upgradeButtonScale = 0.6f;
    [SerializeField] float upgradeButtonWidth = 300f;
    [Tooltip("Yapı butonlarının canvas sırası; Tsunami paneli (-1) ve HUD (0) bunun üstünde kalır.")]
    [SerializeField] int buttonSortingOrder = -2;

    [Header("İşçi")]
    [SerializeField] Gatherer workerPrefab;
    [SerializeField] float workerButtonWidth = 290f;
    [SerializeField] int workerWood = 15;
    [SerializeField] int workerStone = 10;

    class Entry
    {
        public UpgradeLevel upgrade;
        public RectTransform rect;
        public Button button;
        public Text text;
        public CanvasGroup group;
        public bool affordable;
    }

    readonly List<Entry> entries = new();
    Well well;
    Camera cam;
    RectTransform buttonLayer;
    WallConstructionController construction;
    Button workerButton;
    Text workerText;
    CanvasGroup workerGroup;
    int hired;

    void Start()
    {
        well = Well.Instance;
        if (template == null || well == null)
        {
            Debug.LogWarning("UpgradeHUD: taş butonu ya da kuyu yok.", this);
            enabled = false;
            return;
        }
        BuildWorkerButton();
        buttonLayer = BuildButtonLayer();
        foreach (var upgrade in UpgradeLevel.All)
            AddEntry(upgrade);
        well.Changed += OnWellChanged;
        Refresh();
    }

    void OnDestroy()
    {
        if (well != null)
            well.Changed -= OnWellChanged;
        foreach (var entry in entries)
            if (entry.upgrade != null)
                entry.upgrade.Changed -= OnUpgraded;
    }

    void OnWellChanged(ResourceType type, int amount) => Refresh();

    void OnUpgraded(int level) => Refresh();

    // ---------------------------------------------------------------- Butonlar

    void BuildWorkerButton()
    {
        var panel = template.parent as RectTransform;
        var copy = Copy(template.parent, "Worker Button", null, out workerText, out workerGroup);
        copy.SetSiblingIndex(template.GetSiblingIndex() + 1);
        copy.sizeDelta = new Vector2(workerButtonWidth, copy.sizeDelta.y);
        if (panel != null && panel.TryGetComponent<HorizontalLayoutGroup>(out var layout))
            panel.sizeDelta += new Vector2(workerButtonWidth + layout.spacing, 0f);

        // İşçi ikonu olmadığı için ikon yuvasında büyük bir "+".
        var slot = copy.Find("Icon Slot");
        if (slot != null && workerText != null)
        {
            var plus = new GameObject("Plus", typeof(RectTransform), typeof(Text), typeof(Outline)).GetComponent<RectTransform>();
            plus.SetParent(slot, false);
            plus.anchorMin = Vector2.zero;
            plus.anchorMax = Vector2.one;
            plus.offsetMin = plus.offsetMax = Vector2.zero;
            var text = plus.GetComponent<Text>();
            text.font = workerText.font;
            text.fontSize = 52;
            text.fontStyle = FontStyle.Bold;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = workerText.color;
            text.text = "+";
            text.raycastTarget = false;
            var outline = plus.GetComponent<Outline>();
            var source = workerText.GetComponent<Outline>();
            if (source != null)
            {
                outline.effectColor = source.effectColor;
                outline.effectDistance = source.effectDistance;
            }
        }
        workerButton = copy.GetComponent<Button>();
        workerButton.onClick.AddListener(HireWorker);
    }

    // Yapı butonları ayrı bir canvas katmanında, diğer panellerin (Tsunami paneli, kaynak paneli) altında çizilir ve
    // onların tıklamalarını kapmaz.
    RectTransform BuildButtonLayer()
    {
        var layer = new GameObject("Upgrade Buttons", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster)).GetComponent<RectTransform>();
        layer.SetParent(transform, false);
        layer.anchorMin = Vector2.zero;
        layer.anchorMax = Vector2.one;
        layer.offsetMin = layer.offsetMax = Vector2.zero;
        var canvas = layer.GetComponent<Canvas>();
        canvas.overrideSorting = true;
        canvas.sortingOrder = buttonSortingOrder;
        return layer;
    }

    void AddEntry(UpgradeLevel upgrade)
    {
        var copy = Copy(buttonLayer, $"Upgrade Button ({upgrade.name})", upgrade.IsForest ? woodIcon : stoneIcon, out var text, out var group);
        copy.anchorMin = copy.anchorMax = copy.pivot = new Vector2(0.5f, 0.5f);
        copy.sizeDelta = new Vector2(upgradeButtonWidth, copy.sizeDelta.y);
        copy.localScale = Vector3.one * upgradeButtonScale;
        var entry = new Entry { upgrade = upgrade, rect = copy, button = copy.GetComponent<Button>(), text = text, group = group };
        entry.button.onClick.AddListener(() =>
        {
            entry.upgrade.TryUpgrade(well);
            Refresh();
        });
        upgrade.Changed += OnUpgraded;
        entries.Add(entry);
    }

    RectTransform Copy(Transform parent, string name, Sprite icon, out Text text, out CanvasGroup group)
    {
        var copy = Instantiate(template, parent, false);
        copy.name = name;
        copy.localScale = Vector3.one;
        var iconImage = copy.Find("Icon Slot/Icon")?.GetComponent<Image>();
        if (iconImage != null)
        {
            iconImage.sprite = icon;
            iconImage.enabled = icon != null;
        }
        text = copy.Find("Amount")?.GetComponent<Text>();
        if (text != null)
        {
            text.supportRichText = true;
            text.fontSize = 30;
            text.lineSpacing = 0.85f;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
        }
        group = copy.gameObject.AddComponent<CanvasGroup>();
        return copy;
    }

    void Refresh()
    {
        if (well == null)
            return;
        foreach (var entry in entries)
        {
            var upgrade = entry.upgrade;
            // Bedel küçük butonda okunmuyordu: buton yalnız kuyudaki kaynak yetince görünür, hedef seviyeyi yazar.
            entry.affordable = upgrade.CanUpgrade(well);
            entry.button.interactable = entry.affordable;
            if (entry.text != null)
                entry.text.text = $"Yükselt\n<size=26>Sv {upgrade.Level + 1}</size>";
        }

        bool canHire = workerPrefab != null && well.CanAfford(workerWood, workerStone);
        if (workerButton != null)
            workerButton.interactable = canHire;
        if (workerGroup != null)
            workerGroup.alpha = canHire ? 1f : 0.7f;
        if (workerText != null)
            workerText.text = $"İşçi\n<size=22>{workerWood} odun {workerStone} taş</size>";
    }

    // Yapı butonları yapının ekrandaki yerini takip eder; para yetmezken, son seviyede, menüde, inşa modunda ve yapı
    // kamera arkasındayken gizlenir.
    void LateUpdate()
    {
        if (cam == null)
        {
            var iso = FindAnyObjectByType<IsometricCamera>();
            cam = iso != null ? iso.GetComponent<Camera>() : Camera.main;
            if (cam == null)
                return;
        }
        construction ??= FindAnyObjectByType<WallConstructionController>();
        bool hidden = MainMenu.IsOpen || (construction != null && construction.isActiveAndEnabled && construction.ConstructionModeActive);
        foreach (var entry in entries)
        {
            bool visible = !hidden && entry.affordable && entry.upgrade != null && entry.upgrade.isActiveAndEnabled;
            Vector3 screen = default;
            if (visible)
            {
                screen = cam.WorldToScreenPoint(entry.upgrade.ButtonPoint);
                visible = screen.z > 0f;
            }
            if (entry.rect.gameObject.activeSelf != visible)
                entry.rect.gameObject.SetActive(visible);
            if (visible && RectTransformUtility.ScreenPointToLocalPointInRectangle(buttonLayer, screen, null, out var local))
                entry.rect.anchoredPosition = local;
        }
    }

    // ---------------------------------------------------------------- İşçi

    // Bedeli kuyudan öder; kuyunun yanında, sahnedeki köylülerin ayarlarıyla yeni bir köylü doğar ve dolaşır.
    void HireWorker()
    {
        if (workerPrefab == null || !well.TrySpend(workerWood, workerStone))
        {
            Refresh();
            return;
        }
        var template = Gatherer.Villagers.Count > 0 ? Gatherer.Villagers[0] : null;
        var direction = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * Vector3.forward;
        var spot = well.transform.position + direction * (well.ApproachRadius + 2.5f);
        if (NavMesh.SamplePosition(spot, out var hit, 6f, NavMesh.AllAreas))
            spot = hit.position;
        spot.y += workerPrefab.transform.position.y; // Köylü varsa BecomeVillagerLike zemine göre düzeltir.
        var worker = Instantiate(workerPrefab, spot, Quaternion.LookRotation(direction), template != null ? template.transform.parent : null);
        worker.name = $"Worker {++hired}";
        worker.BecomeVillagerLike(template);
        Refresh();
    }
}
