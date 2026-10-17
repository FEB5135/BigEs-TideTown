using System;
using System.Collections.Generic;
using IdleTown.Construction;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// Oyna geçişinden sonra açılan ve oyun içinde tekrar okunabilen kısa rehber.
public sealed class WelcomeParty : MonoBehaviour
{
    static readonly Color Cream = new(1f, 0.957f, 0.863f);
    static readonly Color Ink = new(0.29f, 0.196f, 0.125f);
    static readonly Color Wood = new(0.37f, 0.247f, 0.141f);
    static readonly Color Red = new(0.91f, 0.32f, 0.29f);
    static readonly Color Gold = new(1f, 0.89f, 0.54f);

    static readonly string[] Titles = {
        "Köyün sana emanet",
        "Topla, taşı, biriktir",
        "Kaynaklarını savunmaya dönüştür",
        "Haritaya hâkim ol"
    };
    static readonly string[] Descriptions = {
        "Vadiden gelecek tsunamiye karşı köyü koru. Hazırlığın temeli odun ve taş toplamak, ardından köyün çevresine savunma duvarları kurmak.",
        "Oduncular ağaçlardan odun, maden vagonları ocaklardan taş toplar. Çalışanlar kaynakları kendileri merkezdeki kuyuya taşır.",
        "B tuşuyla İnşa moduna geç. Başlangıç düğümüne sol tıkla, sonra fareyi bitiş noktasına götürerek duvar yolunu ön izle.",
        "Fare tekerleğiyle yakınlaş veya uzaklaş. Sağ tuşa basılı tutup sürükleyerek köyü, ormanı ve madenleri incele."
    };
    static readonly string[] Tips = {
        "İlk hedefin\nKaynakları biriktir → duvarlarını planla → köyü koru.",
        "Sen de yardım edebilirsin\nToplama modunda ağaçlara veya madenlere sol tıkla. Kaynak kuyuya ulaşınca sol alttaki sayaç artar.",
        "İnşa etmeden önce kontrol et\nMaliyet panelde görünür. İkinci sol tık, Enter veya İnşa Et ile onayla; kaynaklar o anda harcanır. Sağ tık / Esc iptal eder. B ile toplamaya dön.",
        "Hazırsın!\nÖnce kaynakları gözle, sonra B ile duvarlarını kur. Bu rehberi sağ üstteki ? Rehber düğmesinden istediğin zaman tekrar açabilirsin."
    };

    readonly List<(Behaviour behaviour, bool enabled)> blockedBehaviours = new();
    readonly List<(IsometricCamera camera, bool input)> blockedCameras = new();
    GameObject uiRoot, modal;
    Button help, back, next;
    Text title, description, tip, counter, nextLabel;
    Image progress;
    Font font;
    float previousTimeScale;
    Action onCompleted;
    bool hasStarted;

    public bool IsOpen { get; private set; }
    public int PageIndex { get; private set; }
    public int PageCount => Titles.Length;

    public void Initialize(Font menuFont) => font = menuFont;

    public void Show(Action completed = null)
    {
        if (IsOpen)
            return;
        EnsureUI();
        onCompleted = completed;
        PageIndex = 0;
        IsOpen = true;
        previousTimeScale = Time.timeScale;
        Time.timeScale = 0f;
        foreach (var camera in FindObjectsByType<IsometricCamera>(FindObjectsSortMode.None))
        {
            blockedCameras.Add((camera, camera.InputEnabled));
            camera.InputEnabled = false;
        }
        foreach (var harvester in FindObjectsByType<ClickHarvester>(FindObjectsSortMode.None))
            Block(harvester);
        foreach (var construction in FindObjectsByType<WallConstructionController>(FindObjectsSortMode.None))
            Block(construction);
        help.gameObject.SetActive(false);
        modal.SetActive(true);
        RefreshPage();
    }

    void Block(Behaviour behaviour)
    {
        blockedBehaviours.Add((behaviour, behaviour.enabled));
        behaviour.enabled = false;
    }

    public void NextPage()
    {
        if (!IsOpen)
            return;
        if (PageIndex == PageCount - 1)
            Complete();
        else
        {
            PageIndex++;
            RefreshPage();
        }
    }

    public void PreviousPage()
    {
        if (!IsOpen || PageIndex == 0)
            return;
        PageIndex--;
        RefreshPage();
    }

    // Hem son sayfa hem Rehberi geç aynı güvenli kapanış yolunu kullanır.
    public void Complete() => Close(true);

    void Close(bool notify)
    {
        if (!IsOpen)
            return;
        IsOpen = false;
        Time.timeScale = previousTimeScale;
        foreach (var saved in blockedCameras)
            if (saved.camera != null)
                saved.camera.InputEnabled = saved.input;
        foreach (var saved in blockedBehaviours)
            if (saved.behaviour != null)
                saved.behaviour.enabled = saved.enabled;
        blockedCameras.Clear();
        blockedBehaviours.Clear();
        if (modal != null)
            modal.SetActive(false);
        var callback = onCompleted;
        onCompleted = null;
        if (notify)
        {
            hasStarted = true;
            if (help != null)
                help.gameObject.SetActive(true);
            callback?.Invoke();
        }
    }

    void Update()
    {
        if (!IsOpen || Keyboard.current == null)
            return;
        var keyboard = Keyboard.current;
        if (keyboard.escapeKey.wasPressedThisFrame)
            Complete();
        else if (keyboard.rightArrowKey.wasPressedThisFrame)
            NextPage();
        else if (keyboard.leftArrowKey.wasPressedThisFrame)
            PreviousPage();
    }

    void OnDisable()
    {
        Close(false);
        if (uiRoot != null)
            uiRoot.SetActive(false);
    }

    void OnEnable()
    {
        if (uiRoot != null)
        {
            uiRoot.SetActive(true);
            help.gameObject.SetActive(hasStarted);
        }
    }

    void OnDestroy()
    {
        if (uiRoot != null)
            Destroy(uiRoot);
    }

    void RefreshPage()
    {
        title.text = Titles[PageIndex];
        description.text = Descriptions[PageIndex];
        tip.text = Tips[PageIndex];
        counter.text = $"KÖY REHBERİ   /   {PageIndex + 1} — {PageCount}";
        back.interactable = PageIndex > 0;
        nextLabel.text = PageIndex == PageCount - 1 ? "Köyü korumaya başla" : "Devam et  →";
        float width = 996f * (PageIndex + 1f) / PageCount;
        progress.rectTransform.sizeDelta = new Vector2(width, 6);
        progress.rectTransform.anchoredPosition = new Vector2(-498f + width / 2f, -322);
    }

    void EnsureUI()
    {
        if (uiRoot != null)
            return;
        if (font == null)
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        uiRoot = new GameObject("Welcome Party", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        // Menü canvas'ı kapanınca da rehber erişilebilir kalır.
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(uiRoot, gameObject.scene);
        var canvas = uiRoot.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.overrideSorting = true;
        canvas.sortingOrder = 300;
        var scaler = uiRoot.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;

        help = MakeButton(uiRoot.transform, "Open Guide", "?  Rehber", Wood, new Vector2(210, 64), Vector2.zero, () => Show());
        var helpRect = (RectTransform)help.transform;
        helpRect.anchorMin = helpRect.anchorMax = helpRect.pivot = Vector2.one;
        helpRect.anchoredPosition = new Vector2(-24, -24);
        help.gameObject.SetActive(false);

        var backdrop = Panel(uiRoot.transform, "Guide Modal", new Color(0.06f, 0.10f, 0.12f, 0.82f), Vector2.zero, Vector2.zero);
        Stretch(backdrop);
        modal = backdrop.gameObject;
        Panel(backdrop, "Shadow", new Color(0, 0, 0, 0.25f), new Vector2(1160, 756), new Vector2(0, -12));
        var card = Panel(backdrop, "Guide Card", Cream, new Vector2(1140, 736), Vector2.zero);
        Panel(card, "Accent", Red, new Vector2(1140, 12), new Vector2(0, 362));
        counter = Label(card, "Step", "", 24, Wood, new Vector2(996, 44), new Vector2(0, 287));
        title = Label(card, "Title", "", 52, Ink, new Vector2(996, 90), new Vector2(0, 210));
        description = Label(card, "Description", "", 31, Ink, new Vector2(996, 156), new Vector2(0, 86));
        var tipPanel = Panel(card, "Tip Panel", Gold, new Vector2(996, 176), new Vector2(0, -99));
        tip = Label(tipPanel, "Tip", "", 27, Ink, new Vector2(936, 146), Vector2.zero);
        back = MakeButton(card, "Previous", "Geri", Wood, new Vector2(180, 66), new Vector2(-408, -253), PreviousPage);
        MakeButton(card, "Skip", "Rehberi geç", Wood, new Vector2(240, 66), new Vector2(-145, -253), Complete);
        next = MakeButton(card, "Next", "", Red, new Vector2(398, 66), new Vector2(299, -253), NextPage);
        nextLabel = next.GetComponentInChildren<Text>();
        Panel(card, "Progress Track", new Color(0.85f, 0.80f, 0.69f), new Vector2(996, 6), new Vector2(0, -322));
        progress = Panel(card, "Progress", Red, new Vector2(996, 6), new Vector2(0, -322)).GetComponent<Image>();
        if (modal != null)
            modal.SetActive(false);
    }

    Text Label(Transform parent, string name, string value, int size, Color color, Vector2 dimensions, Vector2 position)
    {
        var rect = Rect(parent, name, dimensions, position);
        var text = rect.gameObject.AddComponent<Text>();
        text.font = font;
        text.text = value;
        text.fontSize = size;
        text.color = color;
        text.alignment = TextAnchor.MiddleLeft;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Truncate;
        text.raycastTarget = false;
        return text;
    }

    Button MakeButton(Transform parent, string name, string label, Color color, Vector2 size, Vector2 position, UnityEngine.Events.UnityAction action)
    {
        var rect = Panel(parent, name, color, size, position);
        var button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = rect.GetComponent<Image>();
        var colors = button.colors;
        colors.highlightedColor = new Color(1f, 0.92f, 0.75f);
        colors.pressedColor = new Color(0.75f, 0.75f, 0.75f);
        colors.disabledColor = new Color(0.6f, 0.6f, 0.6f, 0.45f);
        button.colors = colors;
        button.onClick.AddListener(action);
        var text = Label(rect, "Label", label, 27, Cream, size - new Vector2(20, 8), Vector2.zero);
        text.alignment = TextAnchor.MiddleCenter;
        return button;
    }

    static RectTransform Panel(Transform parent, string name, Color color, Vector2 size, Vector2 position)
    {
        var rect = Rect(parent, name, size, position);
        rect.gameObject.AddComponent<Image>().color = color;
        return rect;
    }

    static RectTransform Rect(Transform parent, string name, Vector2 size, Vector2 position)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = size;
        rect.anchoredPosition = position;
        return rect;
    }

    static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
    }
}
