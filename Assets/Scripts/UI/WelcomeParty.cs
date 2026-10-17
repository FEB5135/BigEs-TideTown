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
        "Köyünü koru", "Orman: odun kaynağın", "Maden: taş kaynağın", "Kuyu: ortak depon", "Duvarlarla savun"
    };
    static readonly string[] Descriptions = {
        "Vadiden gelecek tsunamiye karşı bu köyü koruyacaksın. Önce kaynak biriktir, sonra savunmanı kur.",
        "Oduncular bu ağaçlardan odun toplar. Toplama modunda sen de ağaçlara sol tıklayarak yardım edebilirsin.",
        "Vagonlar bu ocaklardan taş çıkarıp kuyuya taşır. Taş, odunla birlikte duvar inşa etmek için kullanılır.",
        "Toplanan odun ve taş burada birikir. Kaynak kuyuya ulaştığında sol alttaki sayaçlara eklenir.",
        "B ile İnşa moduna geç. Bir düğüme tıkla, fareyle duvar yolunu ön izle. İkinci tık veya Enter ile inşa et."
    };
    static readonly string[] Tips = {
        "Birlikte köyü kısa bir turla tanıyalım.",
        "Odun → oduncu → kuyu → duvar",
        "Taş → vagon → kuyu → duvar",
        "Duvar maliyeti bu ortak depodan karşılanır.",
        "Sağ tık / Esc: iptal   •   B: toplama modu"
    };
    static readonly string[] TargetNames = { "KÖYÜN", "ORMAN / ODUN", "MADEN / TAŞ", "KUYU / DEPO", "SAVUNMA ALANI" };

    readonly List<(Behaviour behaviour, bool enabled)> blockedBehaviours = new();
    readonly List<(IsometricCamera camera, bool input)> blockedCameras = new();
    GameObject uiRoot, modal;
    Button help, back, next;
    Text title, description, tip, counter, nextLabel, targetName;
    readonly Image[] shades = new Image[4];
    readonly RectTransform[] brackets = new RectTransform[8];
    RectTransform targetFrame, marker;
    WelcomeMapTour mapTour;
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
        if (mapTour == null)
            mapTour = gameObject.AddComponent<WelcomeMapTour>();
        mapTour.Begin();
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
        if (mapTour != null)
            mapTour.End();
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
        tip.text = Tips[PageIndex];
        targetName.text = TargetNames[PageIndex];
        counter.text = $"KÖY TURU   /   {PageIndex + 1} — {PageCount}";
        back.interactable = PageIndex > 0;
        nextLabel.text = PageIndex == PageCount - 1 ? "Başlayalım  →" : "Sıradaki  →";
        float width = 516f * (PageIndex + 1f) / PageCount;
        progress.rectTransform.sizeDelta = new Vector2(width, 4);
        progress.rectTransform.anchoredPosition = new Vector2(-258f + width / 2f, -180);
        mapTour.SelectStep(PageIndex);
    }

    // Kamerayla birlikte hareket eden açık pencere, anlatılan alanın üzerini kapatmaz.
    void LateUpdate()
    {
        if (!IsOpen || uiRoot == null || mapTour == null || mapTour.ViewCamera == null)
            return;
        var root = (RectTransform)uiRoot.transform;
        var screen = root.rect;
        Vector2 min = new(float.MaxValue, float.MaxValue), max = new(float.MinValue, float.MinValue);
        bool visible = false;
        foreach (var point in WelcomeMapTour.Corners(mapTour.FocusBounds))
        {
            var projected = mapTour.ViewCamera.WorldToScreenPoint(point);
            if (projected.z <= 0)
                continue;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(root, projected, null, out var local);
            min = Vector2.Min(min, local);
            max = Vector2.Max(max, local);
            visible = true;
        }
        if (!visible)
            return;
        min = Vector2.Max(min - Vector2.one * 22, screen.min + Vector2.one * 24);
        max = Vector2.Min(max + Vector2.one * 22, screen.max - Vector2.one * 24);
        // Hedef geçişte ekran dışında kalsa da ters/negatif boyut oluşturma.
        min.x = Mathf.Clamp(min.x, screen.xMin + 24, screen.xMax - 25);
        min.y = Mathf.Clamp(min.y, screen.yMin + 24, screen.yMax - 25);
        max = Vector2.Max(max, min + Vector2.one);
        SetArea(shades[0].rectTransform, new Vector2(screen.xMin, screen.yMin), new Vector2(min.x, screen.yMax));
        SetArea(shades[1].rectTransform, new Vector2(max.x, screen.yMin), screen.max);
        SetArea(shades[2].rectTransform, new Vector2(min.x, screen.yMin), new Vector2(max.x, min.y));
        SetArea(shades[3].rectTransform, new Vector2(min.x, max.y), new Vector2(max.x, screen.yMax));
        targetFrame.anchoredPosition = (min + max) * 0.5f;
        targetFrame.sizeDelta = max - min;
        for (int i = 0; i < 4; i++)
        {
            float x = (i & 1) == 0 ? -1 : 1;
            float y = (i & 2) == 0 ? -1 : 1;
            var corner = new Vector2(x * (max.x - min.x) / 2, y * (max.y - min.y) / 2);
            brackets[i * 2].anchoredPosition = corner - new Vector2(x * 15, 0);
            brackets[i * 2 + 1].anchoredPosition = corner - new Vector2(0, y * 15);
        }
        marker.anchoredPosition = new Vector2(Mathf.Clamp((min.x + max.x) / 2, screen.xMin + 144, screen.xMax - 144),
            Mathf.Clamp(max.y + 34, screen.yMin + 44, screen.yMax - 40));
    }

    static void SetArea(RectTransform rect, Vector2 min, Vector2 max)
    {
        rect.anchoredPosition = (min + max) * 0.5f;
        rect.sizeDelta = Vector2.Max(max - min, Vector2.zero);
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

        var backdrop = Rect(uiRoot.transform, "Guide Modal", Vector2.zero, Vector2.zero);
        Stretch(backdrop);
        // Saydam tıklama perdesi; dünya tıklamaları ayrıca kapalı tutulur.
        backdrop.gameObject.AddComponent<Image>().color = Color.clear;
        modal = backdrop.gameObject;
        for (int i = 0; i < shades.Length; i++)
            shades[i] = Panel(backdrop, $"Grey Veil {i}", new Color(0.30f, 0.32f, 0.35f, 0.38f), Vector2.zero, Vector2.zero, 0).GetComponent<Image>();
        targetFrame = Rect(backdrop, "Target Frame", Vector2.zero, Vector2.zero);
        for (int i = 0; i < 4; i++)
        {
            brackets[i * 2] = Panel(targetFrame, $"Corner {i} Horizontal", new Color(1f, 0.93f, 0.73f, 0.9f), new Vector2(30, 3), Vector2.zero, 1.5f);
            brackets[i * 2 + 1] = Panel(targetFrame, $"Corner {i} Vertical", new Color(1f, 0.93f, 0.73f, 0.9f), new Vector2(3, 30), Vector2.zero, 1.5f);
        }
        marker = Panel(backdrop, "Map Label", new Color(0.24f, 0.20f, 0.16f, 0.86f), new Vector2(260, 52), Vector2.zero, 18);
        targetName = Label(marker, "Target Name", "", 23, Cream, new Vector2(236, 46), Vector2.zero);
        targetName.alignment = TextAnchor.MiddleCenter;

        var callout = Rect(backdrop, "Map Callout", new Vector2(580, 380), Vector2.zero);
        callout.anchorMin = callout.anchorMax = Vector2.one;
        callout.pivot = Vector2.one;
        callout.anchoredPosition = new Vector2(-28, -28);
        // Sağ üstteki küçük kart haritanın büyük bölümünü görünür bırakır.
        Panel(callout, "Shadow", new Color(0, 0, 0, 0.16f), new Vector2(592, 392), new Vector2(0, -6), 28);
        var card = Panel(callout, "Guide Card", new Color(Cream.r, Cream.g, Cream.b, 0.94f), new Vector2(580, 380), Vector2.zero, 24);
        counter = Label(card, "Step", "", 20, Wood, new Vector2(516, 38), new Vector2(0, 151));
        title = Label(card, "Title", "", 35, Ink, new Vector2(516, 66), new Vector2(0, 105));
        description = Label(card, "Description", "", 26, Ink, new Vector2(516, 134), new Vector2(0, 12));
        tip = Label(card, "Tip", "", 22, Wood, new Vector2(516, 54), new Vector2(0, -80));
        back = MakeButton(card, "Previous", "←", Wood, new Vector2(84, 54), new Vector2(-216, -143), PreviousPage);
        back.GetComponent<Image>().color = new Color(0.90f, 0.85f, 0.76f);
        back.GetComponentInChildren<Text>().color = Ink;
        var skip = MakeButton(card, "Skip", "Geç", Wood, new Vector2(118, 54), new Vector2(-83, -143), Complete);
        skip.GetComponent<Image>().color = Color.clear;
        skip.GetComponentInChildren<Text>().color = Wood;
        next = MakeButton(card, "Next", "", Red, new Vector2(234, 54), new Vector2(141, -143), NextPage);
        nextLabel = next.GetComponentInChildren<Text>();
        Panel(card, "Progress Track", new Color(0.86f, 0.81f, 0.72f), new Vector2(516, 4), new Vector2(0, -180), 2);
        progress = Panel(card, "Progress", Red, new Vector2(516, 4), new Vector2(0, -180), 2).GetComponent<Image>();
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
