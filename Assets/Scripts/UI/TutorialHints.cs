using System;
using System.Collections;
using IdleTown.Construction;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Oyun başında ekranın üst ortasında çıkan ipucu kartları. Oyunu durdurmaz: oyuncu söyleneni yapınca sıradaki
// adıma geçilir, zaten yaptıysa adım hiç gösterilmez. Bilgi adımları birkaç saniye sonra kendiliğinden geçer.
// "ATLA" hepsini kapatır. Bitince ya da atlanınca PlayerPrefs'e yazılır, sonraki oyunlarda çıkmaz
// (Tools > BigEs > Tutorial'ı Sıfırla).
// Tutorial sürerken tsunami sayacı başlamaz (HoldsTsunami): duvar adımı gelince ya da atlanınca TsunamiAllowed
// çağrılır ve TsunamiController sayacı başlatır.
// Kart oyun sırasında kod ile kurulur; sahnede yalnız bu bileşen ve yazı tipi/sprite bağlantıları durur.
public class TutorialHints : MonoBehaviour
{
    public const string DonePref = "TutorialDone";

    // Tutorial tsunami sayacını bekletiyor mu.
    public static bool HoldsTsunami { get; private set; }
    public static event Action TsunamiAllowed;

    [SerializeField] Font font;
    [SerializeField] Sprite panelSprite;
    [Tooltip("Yükseltme ve fırtına gibi bilgi adımlarının kendiliğinden geçme süresi (sn).")]
    [SerializeField] float infoDuration = 9f;
    [Tooltip("Bir adım bitince sıradakinin gelmesine kadar bekleme (sn).")]
    [SerializeField] float stepGap = 0.6f;
    [Tooltip("Test: PlayerPrefs'e bakmadan her oyunda göster.")]
    [SerializeField] bool alwaysShow;
    [Tooltip("Sağ üstteki tsunami panelinin kapladığı genişlik (1920 referans pikseli). Kart onun soluna sığdırılır.")]
    [SerializeField] float rightReserve = 510f;

    static readonly Color Navy = new(0.043f, 0.165f, 0.239f, 0.94f);
    static readonly Color Gold = new(1f, 0.824f, 0.247f);
    static readonly Color Water = new(0.180f, 0.769f, 0.839f);
    static readonly Color Foam = new(0.910f, 0.969f, 0.980f);
    static readonly Color Muted = new(0.812f, 0.910f, 0.949f, 0.8f);

    const float CardWidth = 860f;
    const float MinCardWidth = 600f;
    const float CardHeight = 150f;
    const float BodyTop = 68f;
    const float BodyBottom = 22f;
    const float Margin = 16f;
    const float CardTop = -24f;
    const float SlideDistance = 24f;
    const float FadeTime = 0.3f;

    class Step
    {
        public string title;
        public string body;
        public Func<bool> done;
        public Action begin;
        public float autoAdvance; // > 0: bilgi adımı, bu kadar saniye sonra geçer
        public bool releasesTsunami;
    }

    Step[] steps;
    bool active;
    bool running;
    Coroutine routine;

    IsometricCamera isoCamera;
    Vector3 cameraFocus;
    float cameraDistance;
    Well well;
    int lastWood, lastStone;
    bool gotWood, gotStone;
    WallConstructionController construction;
    bool wallBuilt;

    Canvas canvas;
    RectTransform card;
    float cardX;
    CanvasGroup group;
    Text badge, title, body;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        // Domain reload kapalı: Play'ler arasında static değerler kalır.
        HoldsTsunami = false;
        TsunamiAllowed = null;
    }

    void Awake()
    {
        active = alwaysShow || PlayerPrefs.GetInt(DonePref, 0) == 0;
        HoldsTsunami = active;
        steps = CreateSteps();
        BuildCard();
    }

    void OnEnable() => MainMenu.GameStarted += OnGameStarted;
    void OnDisable() => MainMenu.GameStarted -= OnGameStarted;

    void Start()
    {
        // Menüsüz sahnede oyun hemen başlar.
        if (FindAnyObjectByType<MainMenu>() == null)
            OnGameStarted();
    }

    void OnDestroy()
    {
        if (well != null)
            well.Changed -= OnWellChanged;
        if (construction != null)
            construction.WallTierBuilt -= OnWallBuilt;
        // Sahne kapanırken sayaç başlatılmaz, yalnız bekletme kalkar.
        HoldsTsunami = false;
    }

    void OnGameStarted()
    {
        if (!active || running)
            return;
        running = true;

        isoCamera = FindAnyObjectByType<IsometricCamera>();
        well = Well.Instance;
        if (well != null)
        {
            lastWood = well.Get(ResourceType.Wood);
            lastStone = well.Get(ResourceType.Stone);
            well.Changed += OnWellChanged;
        }
        construction = FindAnyObjectByType<WallConstructionController>();
        if (construction != null)
            construction.WallTierBuilt += OnWallBuilt;

        routine = StartCoroutine(Run());
    }

    Step[] CreateSteps() => new[]
    {
        new Step
        {
            title = "ETRAFA BAK",
            body = "Sağ tıkla sürükleyerek haritayı kaydır, fare tekerleğiyle yaklaş ve uzaklaş.",
            begin = () =>
            {
                if (isoCamera == null) return;
                cameraFocus = isoCamera.FocusPoint;
                cameraDistance = isoCamera.Distance;
            },
            done = () => isoCamera == null
                || Vector3.Distance(isoCamera.FocusPoint, cameraFocus) > 4f
                || Mathf.Abs(isoCamera.Distance - cameraDistance) > cameraDistance * 0.15f,
        },
        new Step
        {
            title = "KAYNAK TOPLA",
            body = "Ağaçlara ve taş ocaklarına sol tıkla. Odun ve taş ortadaki kuyuya uçar.",
            done = () => well == null || (gotWood && gotStone),
        },
        new Step
        {
            title = "KÖYLÜ AL",
            body = "Topladıklarınla sol alttaki işçi butonundan bir köylü al.",
            done = () => Gatherer.Villagers.Count > 0,
        },
        new Step
        {
            title = "KÖYLÜYE İŞ VER",
            body = "Köylüyü sol tıkla tutup bir ağacın yanına bırak: odun keser. Taş ocağına bırakırsan vagon iter.",
            done = AnyVillagerWorking,
        },
        new Step
        {
            title = "YÜKSELT",
            body = "Kaynak yetince taş ocaklarının üstünde Yükselt butonu çıkar. Alttaki İşçi Hızı tüm işçileri hızlandırır.",
            autoAdvance = infoDuration,
            done = AnyUpgraded,
        },
        new Step
        {
            title = "DUVAR ÖR",
            body = "Fırtına yaklaşıyor! B ile inşa moduna geç, başlangıç ve bitiş noktasına sol tıkla. "
                + "1 / 2 / 3: ahşap, taş, güçlü. Sağ tık: iptal.",
            releasesTsunami = true,
            done = () => construction == null || wallBuilt,
        },
        new Step
        {
            title = "KÖYÜ KORU",
            body = "Bulutlar kararınca tsunami gelir. Köyü duvarla çevir, eksik kalan yerlerden su girer.",
            autoAdvance = infoDuration,
            done = () => false,
        },
    };

    static bool AnyVillagerWorking()
    {
        foreach (var villager in Gatherer.Villagers)
            if (villager != null && (villager.HasJob || villager.PushedCart != null))
                return true;
        return false;
    }

    static bool AnyUpgraded()
    {
        foreach (var upgrade in UpgradeLevel.All)
            if (upgrade != null && upgrade.Level > 1)
                return true;
        return false;
    }

    void OnWellChanged(ResourceType type, int amount)
    {
        if (type == ResourceType.Wood)
        {
            gotWood |= amount > lastWood;
            lastWood = amount;
        }
        else if (type == ResourceType.Stone)
        {
            gotStone |= amount > lastStone;
            lastStone = amount;
        }
    }

    void OnWallBuilt(WallRecord record, GameObject instance) => wallBuilt = true;

    void LateUpdate()
    {
        if (card != null && card.gameObject.activeSelf)
            Layout();
    }

    // Kart soldaki duvar panelinin (IMGUI, ekranla ölçeklenmez) ve sağ üstteki tsunami panelinin arasına sığdırılır:
    // yer varsa ortada durur, dar ekranda kayar ve daralır; metin alt satıra geçer, kart uzar.
    void Layout()
    {
        float scale = canvas.scaleFactor;
        if (scale <= 0f)
            return;
        float width = Screen.width / scale;
        float left = Margin;
        if (construction != null && construction.isActiveAndEnabled)
            left = WallConstructionController.PanelRect.xMax / scale + Margin;
        float right = width - rightReserve;
        float cardWidth = Mathf.Clamp(right - left, MinCardWidth, CardWidth);
        float minX = left + cardWidth * 0.5f - width * 0.5f;
        float maxX = right - cardWidth * 0.5f - width * 0.5f;
        cardX = minX > maxX ? minX : Mathf.Clamp(0f, minX, maxX);

        if (!Mathf.Approximately(card.sizeDelta.x, cardWidth))
            card.sizeDelta = new Vector2(cardWidth, card.sizeDelta.y);
        float height = Mathf.Max(CardHeight, BodyTop + body.preferredHeight + BodyBottom);
        if (!Mathf.Approximately(card.sizeDelta.y, height))
        {
            card.sizeDelta = new Vector2(cardWidth, height);
            body.rectTransform.offsetMin = new Vector2(body.rectTransform.offsetMin.x, -height + BodyBottom);
        }
        card.anchoredPosition = new Vector2(cardX, card.anchoredPosition.y);
    }

    IEnumerator Run()
    {
        for (int i = 0; i < steps.Length; i++)
        {
            var step = steps[i];
            step.begin?.Invoke();
            if (step.releasesTsunami)
                AllowTsunami();
            if (step.done())
                continue;

            SetStep(i, step);
            card.gameObject.SetActive(true);
            Layout();
            yield return Fade(true);
            float shown = 0f;
            while (!step.done() && (step.autoAdvance <= 0f || shown < step.autoAdvance))
            {
                shown += Time.deltaTime;
                yield return null;
            }
            yield return Fade(false);
            yield return new WaitForSeconds(stepGap);
        }
        Finish();
    }

    public void Skip()
    {
        if (!running)
            return;
        if (routine != null)
            StopCoroutine(routine);
        routine = StartCoroutine(SkipRoutine());
    }

    IEnumerator SkipRoutine()
    {
        AllowTsunami();
        yield return Fade(false);
        Finish();
    }

    void Finish()
    {
        AllowTsunami();
        PlayerPrefs.SetInt(DonePref, 1);
        PlayerPrefs.Save();
        running = false;
        routine = null;
        card.gameObject.SetActive(false);
    }

    static void AllowTsunami()
    {
        if (!HoldsTsunami)
            return;
        HoldsTsunami = false;
        TsunamiAllowed?.Invoke();
    }

    void SetStep(int index, Step step)
    {
        badge.text = $"{index + 1} / {steps.Length}";
        title.text = step.title;
        body.text = step.body;
    }

    IEnumerator Fade(bool show)
    {
        if (show)
            card.gameObject.SetActive(true);
        group.interactable = show;
        group.blocksRaycasts = show;
        float from = group.alpha, to = show ? 1f : 0f;
        for (float t = 0f; t < FadeTime; t += Time.unscaledDeltaTime)
        {
            float k = Mathf.SmoothStep(0f, 1f, t / FadeTime);
            float alpha = Mathf.Lerp(from, to, k);
            group.alpha = alpha;
            card.anchoredPosition = new Vector2(cardX, CardTop + (1f - alpha) * SlideDistance);
            yield return null;
        }
        group.alpha = to;
        card.anchoredPosition = new Vector2(cardX, CardTop + (1f - to) * SlideDistance);
        if (!show)
            card.gameObject.SetActive(false);
    }

    // ------------------------------------------------------------------ kart

    void BuildCard()
    {
        if (font == null)
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        var canvasObject = new GameObject("Tutorial Canvas", typeof(RectTransform));
        canvasObject.transform.SetParent(transform, false);
        canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 20;
        var scaler = canvasObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        canvasObject.AddComponent<GraphicRaycaster>();

        card = NewRect("Card", canvasObject.transform);
        card.anchorMin = card.anchorMax = card.pivot = new Vector2(0.5f, 1f);
        card.sizeDelta = new Vector2(CardWidth, CardHeight);
        card.anchoredPosition = new Vector2(0f, CardTop);
        group = card.gameObject.AddComponent<CanvasGroup>();
        group.alpha = 0f;

        var background = card.gameObject.AddComponent<Image>();
        background.sprite = panelSprite;
        background.type = panelSprite != null ? Image.Type.Sliced : Image.Type.Simple;
        background.color = Navy;

        var accent = NewRect("Accent", card).gameObject.AddComponent<Image>();
        var accentRect = accent.rectTransform;
        accentRect.anchorMin = new Vector2(0f, 1f);
        accentRect.anchorMax = new Vector2(1f, 1f);
        accentRect.pivot = new Vector2(0.5f, 1f);
        accentRect.sizeDelta = new Vector2(-24f, 4f);
        accentRect.anchoredPosition = new Vector2(0f, -8f);
        accent.color = Water;
        accent.raycastTarget = false;

        badge = NewText("Step", card, 22, Water, TextAnchor.UpperLeft);
        Place(badge.rectTransform, new Vector2(36f, -24f), new Vector2(120f, 30f));

        title = NewText("Title", card, 34, Gold, TextAnchor.UpperLeft);
        Stretch(title.rectTransform, 124f, 150f, -20f, 44f);
        var shadow = title.gameObject.AddComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.45f);
        shadow.effectDistance = new Vector2(0f, -3f);

        body = NewText("Body", card, 26, Foam, TextAnchor.UpperLeft);
        body.horizontalOverflow = HorizontalWrapMode.Wrap;
        body.lineSpacing = 1.05f;
        Stretch(body.rectTransform, 36f, 36f, -BodyTop, 76f);

        var skipRect = NewRect("Skip Button", card);
        skipRect.anchorMin = skipRect.anchorMax = skipRect.pivot = new Vector2(1f, 1f);
        skipRect.sizeDelta = new Vector2(120f, 44f);
        skipRect.anchoredPosition = new Vector2(-20f, -18f);
        var skipImage = skipRect.gameObject.AddComponent<Image>();
        skipImage.color = new Color(1f, 1f, 1f, 0.1f);
        var skip = skipRect.gameObject.AddComponent<Button>();
        skip.targetGraphic = skipImage;
        var colors = skip.colors;
        colors.highlightedColor = new Color(1.6f, 1.6f, 1.6f);
        colors.pressedColor = new Color(0.7f, 0.7f, 0.7f);
        skip.colors = colors;
        skip.onClick.AddListener(Skip);
        var skipText = NewText("Label", skipRect, 22, Muted, TextAnchor.MiddleCenter);
        var skipTextRect = skipText.rectTransform;
        skipTextRect.anchorMin = Vector2.zero;
        skipTextRect.anchorMax = Vector2.one;
        skipTextRect.sizeDelta = Vector2.zero;
        skipText.text = "ATLA";

        card.gameObject.SetActive(false);
        EnsureEventSystem();
    }

    static RectTransform NewRect(string name, Transform parent)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        return rect;
    }

    Text NewText(string name, Transform parent, int size, Color color, TextAnchor alignment)
    {
        var text = NewRect(name, parent).gameObject.AddComponent<Text>();
        text.font = font;
        text.fontSize = size;
        text.color = color;
        text.alignment = alignment;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.raycastTarget = false;
        return text;
    }

    static void Place(RectTransform rect, Vector2 topLeft, Vector2 size)
    {
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = topLeft;
        rect.sizeDelta = size;
    }

    // Yatayda kartın iki kenarına yapışık, üstten sabit uzaklıkta.
    static void Stretch(RectTransform rect, float left, float right, float top, float height)
    {
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.offsetMin = new Vector2(left, top - height);
        rect.offsetMax = new Vector2(-right, top);
    }

    static void EnsureEventSystem()
    {
        if (EventSystem.current != null || FindAnyObjectByType<EventSystem>() != null)
            return;
        var host = new GameObject("EventSystem", typeof(EventSystem),
            typeof(UnityEngine.InputSystem.UI.InputSystemUIInputModule));
        host.transform.SetParent(null);
    }
}
