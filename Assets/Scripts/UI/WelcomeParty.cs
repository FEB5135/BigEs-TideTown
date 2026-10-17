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
    Text title, description, tip, tipHeading, counter, nextLabel;
    readonly Image[] steps = new Image[4];
    Sprite roundedSprite;
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
        if (roundedSprite != null)
        {
            Destroy(roundedSprite.texture);
            Destroy(roundedSprite);
        }
    }

    void RefreshPage()
    {
        title.text = Titles[PageIndex];
        description.text = Descriptions[PageIndex];
        var hint = Tips[PageIndex].Split(new[] { '\n' }, 2);
        tipHeading.text = hint[0];
        tip.text = hint[1];
        for (int i = 0; i < steps.Length; i++)
            steps[i].color = i <= PageIndex ? Red : new Color(0.85f, 0.80f, 0.71f);
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
        CreateRoundedSprite();
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

        var backdrop = Panel(uiRoot.transform, "Guide Modal", new Color(0.06f, 0.08f, 0.10f, 0.78f), Vector2.zero, Vector2.zero, 0);
        Stretch(backdrop);
        modal = backdrop.gameObject;
        // Katmanlı, düşük opaklıklı gölge kartı zeminden ayırır.
        Panel(backdrop, "Soft Shadow", new Color(0, 0, 0, 0.10f), new Vector2(1172, 792), new Vector2(0, -15), 42);
        Panel(backdrop, "Near Shadow", new Color(0, 0, 0, 0.15f), new Vector2(1152, 772), new Vector2(0, -8), 36);
        Panel(backdrop, "Card Border", new Color(0.66f, 0.52f, 0.36f), new Vector2(1144, 764), Vector2.zero, 34);
        var card = Panel(backdrop, "Guide Card", Cream, new Vector2(1140, 760), Vector2.zero, 32);
        counter = Label(card, "Step", "", 23, Wood, new Vector2(800, 44), new Vector2(-98, 301));
        for (int i = 0; i < steps.Length; i++)
            steps[i] = Panel(card, $"Step {i + 1}", Red, new Vector2(14, 14), new Vector2(406 + i * 28, 301), 7).GetComponent<Image>();
        title = Label(card, "Title", "", 52, Ink, new Vector2(996, 104), new Vector2(0, 207));
        description = Label(card, "Description", "", 31, new Color(0.39f, 0.30f, 0.22f), new Vector2(996, 150), new Vector2(0, 83));
        var tipPanel = Panel(card, "Tip Panel", new Color(0.98f, 0.89f, 0.68f), new Vector2(996, 184), new Vector2(0, -105), 22);
        tipHeading = Label(tipPanel, "Tip Heading", "", 25, Wood, new Vector2(916, 42), new Vector2(0, 48));
        tip = Label(tipPanel, "Tip", "", 27, Ink, new Vector2(916, 102), new Vector2(0, -22));
        back = MakeButton(card, "Previous", "←  Geri", Wood, new Vector2(180, 68), new Vector2(-408, -264), PreviousPage);
        back.GetComponent<Image>().color = new Color(0.91f, 0.86f, 0.77f);
        back.GetComponentInChildren<Text>().color = Ink;
        var skip = MakeButton(card, "Skip", "Rehberi geç", Wood, new Vector2(220, 68), new Vector2(-170, -264), Complete);
        skip.GetComponent<Image>().color = Cream;
        skip.GetComponentInChildren<Text>().color = Wood;
        Panel(card, "Next Shadow", new Color(0.67f, 0.20f, 0.18f), new Vector2(398, 68), new Vector2(299, -269), 17);
        next = MakeButton(card, "Next", "", Red, new Vector2(398, 68), new Vector2(299, -264), NextPage);
        nextLabel = next.GetComponentInChildren<Text>();
        Panel(card, "Progress Track", new Color(0.88f, 0.83f, 0.74f), new Vector2(996, 6), new Vector2(0, -322), 3);
        progress = Panel(card, "Progress", Red, new Vector2(996, 6), new Vector2(0, -322), 3).GetComponent<Image>();
        var keyboardHint = Label(card, "Keyboard Hint", "← →  adımlar arasında       Esc  rehberi kapatır", 18, new Color(0.56f, 0.46f, 0.35f), new Vector2(996, 40), new Vector2(0, -350));
        keyboardHint.alignment = TextAnchor.MiddleCenter;
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
        var rect = Panel(parent, name, color, size, position, 17);
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

    RectTransform Panel(Transform parent, string name, Color color, Vector2 size, Vector2 position, float radius = 24)
    {
        var rect = Rect(parent, name, size, position);
        var image = rect.gameObject.AddComponent<Image>();
        image.color = color;
        if (radius > 0)
        {
            image.sprite = roundedSprite;
            image.type = Image.Type.Sliced;
            image.pixelsPerUnitMultiplier = 32f / radius;
        }
        return rect;
    }

    // Tek küçük doku tüm yuvarlak kart/düğmelerde 9-slice ile paylaşılır.
    // Kenar alfa geçişi farklı çözünürlüklerde köşeleri yumuşak tutar.
    void CreateRoundedSprite()
    {
        const int size = 128;
        const float radius = 32f;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        texture.name = "Welcome Rounded Surface";
        texture.wrapMode = TextureWrapMode.Clamp;
        texture.filterMode = FilterMode.Bilinear;
        var pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                var q = new Vector2(Mathf.Abs(x + 0.5f - size / 2f), Mathf.Abs(y + 0.5f - size / 2f)) - Vector2.one * (size / 2f - radius);
                float distance = Vector2.Max(q, Vector2.zero).magnitude + Mathf.Min(Mathf.Max(q.x, q.y), 0f) - radius;
                byte alpha = (byte)Mathf.RoundToInt(Mathf.Clamp01(0.5f - distance) * 255f);
                pixels[y * size + x] = new Color32(255, 255, 255, alpha);
            }
        texture.SetPixels32(pixels);
        texture.Apply(false, true);
        roundedSprite = Sprite.Create(texture, new Rect(0, 0, size, size), Vector2.one * 0.5f, 100f, 0, SpriteMeshType.FullRect, Vector4.one * radius);
        roundedSprite.name = "Welcome Rounded Surface";
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
