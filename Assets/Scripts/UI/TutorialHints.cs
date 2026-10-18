using System;
using System.Collections;
using System.Collections.Generic;
using IdleTown.Construction;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// Oyun başında ekranın üst ortasında çıkan ipucu kartları. Oyunu durdurmaz: oyuncu söyleneni yapınca sıradaki
// adıma geçilir, zaten yaptıysa adım hiç gösterilmez. Bilgi adımları birkaç saniye sonra kendiliğinden geçer.
// "ATLA" hepsini kapatır. Bitince ya da atlanınca PlayerPrefs'e yazılır, sonraki oyunlarda çıkmaz
// (Tools > BigEs > Tutorial'ı Sıfırla).
// Tutorial sürerken tsunami sayacı başlamaz (HoldsTsunami): duvar adımı gelince ya da atlanınca TsunamiAllowed
// çağrılır ve TsunamiController sayacı başlatır.
// Oyuncu ilk dalgada vakit kaybetmesin diye her eylem adımı açılınca oyun zamanı durur (Time.timeScale = 0),
// ekran hafifçe kararır ve tıklanacak yerler (ağaç ve ocak, İşçi butonu, köylü) aydınlık bir halkayla gösterilir.
// Oyuncu tıklayınca (ya da adımı yapınca) zaman akmaya devam eder, sıradaki adıma kadar. Bilgi adımları zamanı durdurmaz.
// Kart oyun sırasında kod ile kurulur; sahnede yalnız bu bileşen ve yazı tipi/sprite bağlantıları durur.
public class TutorialHints : MonoBehaviour
{
    public const string DonePref = "TutorialDone";

    // Tutorial tsunami sayacını bekletiyor mu.
    public static bool HoldsTsunami { get; private set; }
    public static event Action TsunamiAllowed;

    // Kartın yazı tipi ve paneli (oyun sonu ekranı da aynı görünümü kullanır).
    public Font CardFont => font;
    public Sprite CardSprite => panelSprite;

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
    [Tooltip("Zaman durunca ekranın göze görünen kararma miktarı (0-1).")]
    [SerializeField, Range(0f, 1f)] float dimAlpha = 0.55f;

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
    const int DimTextureWidth = 160;
    const float DimFadeSpeed = 6f;
    const int MaxSpots = 3;
    const float RefreezeDelay = 1f;

    class Step
    {
        public string title;
        public string body;
        public Func<bool> done;
        public Action begin;
        public float autoAdvance; // > 0: bilgi adımı, bu kadar saniye sonra geçer
        public bool releasesTsunami;
        public Func<bool> resume; // zaman dururken hangi tıklama durmayı kaldırır (boş: herhangi bir sol tık)
        public Action<List<Spot>> spots; // zaman dururken aydınlatılacak yerler
        public Func<bool> freezeAgain; // zaman açıldıktan sonra adım bitmeden yeniden durdurulsun mu (örn. kalan kaynak türü)
    }

    // Ekran pikseli cinsinden aydınlık daire.
    struct Spot
    {
        public Vector2 center;
        public float radius;
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
    bool clickedWood, clickedStone;
    WallConstructionController construction;
    bool wallBuilt;

    Canvas canvas;
    RectTransform card;
    float cardX;
    CanvasGroup group;
    Text badge, title, body;

    Step current;
    bool frozen;
    int frozenFrame;
    float unfrozenAt;
    float dimLevel;
    readonly List<Spot> spots = new();
    RawImage dim;
    Texture2D dimTexture;
    Color32[] dimPixels;
    readonly RawImage[] rings = new RawImage[MaxSpots];
    UpgradeHUD upgradeHud;

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
        Unfreeze();
        if (dimTexture != null)
            Destroy(dimTexture);
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
        upgradeHud = FindAnyObjectByType<UpgradeHUD>();

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
            resume = ClickedResource,
            // Ocağa tıklayınca zaman açılır, taş kuyuya uçar; sonra yine durup ağacı gösterir (ya da tersi).
            freezeAgain = () => NeedWood || NeedStone,
            spots = list =>
            {
                if (NeedWood) AddNodeSpot(list, ResourceType.Wood);
                if (NeedStone) AddNodeSpot(list, ResourceType.Stone);
            },
        },
        new Step
        {
            title = "KÖYLÜ AL",
            body = "Topladıklarınla sol alttaki işçi butonundan bir köylü al.",
            done = () => Gatherer.Villagers.Count > 0,
            spots = list => AddUiSpot(list, upgradeHud != null ? upgradeHud.WorkerButton : null),
        },
        new Step
        {
            title = "KÖYLÜYE İŞ VER",
            body = "Köylüyü sol tıkla tutup bir ağacın yanına bırak: odun keser. Taş ocağına bırakırsan vagon iter.",
            done = AnyVillagerWorking,
            spots = list =>
            {
                var villager = IdleVillager();
                if (villager == null) return;
                AddWorldSpot(list, villager);
                AddWorldSpot(list, ResourceNode.FindNearestFree(ResourceType.Wood, villager.transform.position));
            },
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
            // Soldaki duvar paneli IMGUI ile karartmanın üstüne çizilir, ayrıca göstermeye gerek yok.
            resume = () => construction.ConstructionModeActive || LeftPressed(),
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

    static Gatherer IdleVillager()
    {
        foreach (var villager in Gatherer.Villagers)
            if (villager != null && !villager.HasJob && villager.PushedCart == null)
                return villager;
        return null;
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

    void Update()
    {
        // Durmayı başlatan karedeki tıklama (örn. Oyna butonu) sayılmaz.
        if (frozen && current != null && Time.frameCount > frozenFrame && (current.done() || (current.resume != null ? current.resume() : LeftPressed())))
            Unfreeze();
    }

    void LateUpdate()
    {
        if (card != null && card.gameObject.activeSelf)
            Layout();
        UpdateDim();
    }

    // ------------------------------------------------------------------ zaman durdurma

    void Freeze(Step step)
    {
        current = step;
        if (step.autoAdvance > 0f || frozen)
            return;
        frozen = true;
        frozenFrame = Time.frameCount;
        Time.timeScale = 0f;
    }

    void Unfreeze()
    {
        if (!frozen)
            return;
        frozen = false;
        unfrozenAt = Time.unscaledTime;
        Time.timeScale = 1f;
    }

    static bool LeftPressed()
    {
        var mouse = Mouse.current;
        return mouse != null && mouse.leftButton.wasPressedThisFrame;
    }

    bool NeedWood => !gotWood && !clickedWood;
    bool NeedStone => !gotStone && !clickedStone;

    // ClickHarvester'ın aynı tıklamada toplayacağı bir kaynağa mı basıldı; türü hatırlanır.
    bool ClickedResource()
    {
        var mouse = Mouse.current;
        if (mouse == null || !mouse.leftButton.wasPressedThisFrame)
            return false;
        var position = mouse.position.ReadValue();
        var cam = Camera.main;
        if (cam == null || WallConstructionController.IsPointerOverConstructionUi(position))
            return false;
        if (!Physics.Raycast(cam.ScreenPointToRay(position), out var hit, 200f, ~0, QueryTriggerInteraction.Ignore))
            return false;
        var node = hit.collider.GetComponentInParent<ResourceNode>();
        if (node == null || node.Remaining <= 0)
            return false;
        if (node.Type == ResourceType.Wood)
            clickedWood = true;
        else if (node.Type == ResourceType.Stone)
            clickedStone = true;
        return true;
    }

    // Bu türden, ekranın ortasına en yakın görünen kaynak; panellerin altında kalanlar ancak başka yoksa seçilir.
    void AddNodeSpot(List<Spot> list, ResourceType type)
    {
        var cam = Camera.main;
        if (cam == null)
            return;
        var middle = new Vector2(Screen.width, Screen.height) * 0.5f;
        ResourceNode best = null;
        float bestScore = float.MaxValue;
        foreach (var node in ResourceNode.Active)
        {
            if (node.Type != type || node.Remaining <= 0)
                continue;
            var screen = cam.WorldToScreenPoint(node.transform.position);
            if (screen.z <= 0f || screen.x < 0f || screen.y < 0f || screen.x > Screen.width || screen.y > Screen.height)
                continue;
            float score = ((Vector2)screen - middle).sqrMagnitude;
            if (UnderPanel(screen))
                score += Screen.width * Screen.width * 4f;
            if (score < bestScore)
            {
                bestScore = score;
                best = node;
            }
        }
        AddWorldSpot(list, best);
    }

    // Ekran noktası kartın, sağ üstteki tsunami panelinin, soldaki duvar panelinin ya da alttaki HUD şeridinin altında mı.
    bool UnderPanel(Vector2 screen)
    {
        float scale = canvas.scaleFactor;
        var corners = new Vector3[4];
        card.GetWorldCorners(corners);
        if (screen.y > corners[0].y - 20f * scale)
            return true;
        if (screen.x > Screen.width - rightReserve * scale && screen.y > Screen.height - 440f * scale)
            return true;
        if (screen.y < 140f * scale)
            return true;
        if (construction != null && construction.isActiveAndEnabled
            && WallConstructionController.PanelRect.Contains(new Vector2(screen.x, Screen.height - screen.y)))
            return true;
        return false;
    }

    // Nesnenin görselinin ekrandaki kaplamına göre bir daire.
    static void AddWorldSpot(List<Spot> list, Component target)
    {
        var cam = Camera.main;
        if (target == null || cam == null)
            return;
        Bounds bounds = default;
        bool any = false;
        foreach (var r in target.GetComponentsInChildren<Renderer>())
        {
            if (!r.enabled || r is ParticleSystemRenderer)
                continue;
            if (any) bounds.Encapsulate(r.bounds);
            else { bounds = r.bounds; any = true; }
        }
        if (!any)
        {
            var collider = target.GetComponentInChildren<Collider>();
            bounds = collider != null ? collider.bounds : new Bounds(target.transform.position, Vector3.one * 2f);
        }

        Vector2 min = new(float.MaxValue, float.MaxValue), max = new(float.MinValue, float.MinValue);
        var center = bounds.center;
        var extents = bounds.extents;
        for (int i = 0; i < 8; i++)
        {
            var corner = center + Vector3.Scale(extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
            var screen = cam.WorldToScreenPoint(corner);
            if (screen.z <= 0f)
                return;
            min = Vector2.Min(min, screen);
            max = Vector2.Max(max, screen);
        }
        AddSpot(list, (min + max) * 0.5f, (max - min).magnitude * 0.5f);
    }

    static void AddUiSpot(List<Spot> list, RectTransform target)
    {
        if (target == null || !target.gameObject.activeInHierarchy)
            return;
        var canvas = target.GetComponentInParent<Canvas>();
        canvas = canvas != null ? canvas.rootCanvas : null;
        var cam = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
        var corners = new Vector3[4];
        target.GetWorldCorners(corners);
        Vector2 a = RectTransformUtility.WorldToScreenPoint(cam, corners[0]);
        Vector2 b = RectTransformUtility.WorldToScreenPoint(cam, corners[2]);
        AddSpot(list, (a + b) * 0.5f, (b - a).magnitude * 0.5f);
    }

    static void AddSpot(List<Spot> list, Vector2 center, float radius)
    {
        float unit = Screen.height / 1080f;
        list.Add(new Spot { center = center, radius = Mathf.Clamp(radius + 18f * unit, 50f * unit, 190f * unit) });
    }

    // Zaman dururken ekran kararır, tıklanacak yerler aydınlık kalır; gösterilecek yer yoksa yalnız kenarlar kararır.
    void UpdateDim()
    {
        if (dim == null)
            return;
        dimLevel = Mathf.MoveTowards(dimLevel, frozen ? 1f : 0f, DimFadeSpeed * Time.unscaledDeltaTime);
        float alpha = dimLevel;
        dim.color = new Color(1f, 1f, 1f, alpha);
        bool visible = alpha > 0.001f;
        dim.enabled = visible;

        spots.Clear();
        if (visible && current?.spots != null)
            current.spots(spots);

        float scale = canvas.scaleFactor;
        float pulse = 1f + 0.06f * Mathf.Sin(Time.unscaledTime * 5f);
        for (int i = 0; i < rings.Length; i++)
        {
            bool on = visible && i < spots.Count && scale > 0f;
            rings[i].enabled = on;
            if (!on)
                continue;
            var rect = rings[i].rectTransform;
            rect.anchoredPosition = spots[i].center / scale;
            float size = spots[i].radius * 2f * pulse / scale;
            rect.sizeDelta = new Vector2(size, size);
            rings[i].color = new Color(Gold.r, Gold.g, Gold.b, alpha);
        }

        if (visible)
            PaintDim();
    }

    void PaintDim()
    {
        int width = DimTextureWidth;
        int height = Mathf.Max(1, Mathf.RoundToInt(width * (float)Screen.height / Mathf.Max(1, Screen.width)));
        if (dimTexture == null || dimTexture.height != height)
        {
            if (dimTexture != null)
                Destroy(dimTexture);
            dimTexture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                name = "Tutorial Dim",
            };
            dimPixels = new Color32[width * height];
            dim.texture = dimTexture;
        }

        float pixel = (float)Screen.width / width;
        var middle = new Vector2(Screen.width, Screen.height) * 0.5f;
        float corner = middle.magnitude;
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            var p = new Vector2((x + 0.5f) * pixel, (y + 0.5f) * pixel);
            float a;
            if (spots.Count == 0)
            {
                a = Mathf.Lerp(0.25f, 1f, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.15f, 0.8f, (p - middle).magnitude / corner)));
            }
            else
            {
                a = 1f;
                foreach (var spot in spots)
                    a = Mathf.Min(a, Mathf.SmoothStep(0f, 1f, ((p - spot.center).magnitude - spot.radius) / (spot.radius * 0.5f)));
            }
            // UI lineer renk uzayında karışır: göze görünen kararma için alfa büyütülür.
            float alpha = 1f - Mathf.Pow(1f - a * dimAlpha, 2.2f);
            dimPixels[y * width + x] = new Color32(0, 0, 0, (byte)(alpha * 255f));
        }
        dimTexture.SetPixels32(dimPixels);
        dimTexture.Apply(false);
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
            Freeze(step);
            yield return Fade(true);
            float shown = 0f;
            while (!step.done() && (step.autoAdvance <= 0f || shown < step.autoAdvance))
            {
                if (!frozen && step.freezeAgain != null && Time.unscaledTime - unfrozenAt > RefreezeDelay && step.freezeAgain())
                    Freeze(step);
                shown += Time.deltaTime;
                yield return null;
            }
            Unfreeze();
            current = null;
            yield return Fade(false);
            yield return new WaitForSecondsRealtime(stepGap);
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
        Unfreeze();
        current = null;
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
        Unfreeze();
        current = null;
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

        BuildDim(canvasObject.transform);

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

    // Karartma ve halkalar kartın arkasında; tıklamaları kapmazlar (köylü ve kaynak tıklamaları UI'ya bakıyor).
    void BuildDim(Transform parent)
    {
        dim = NewRect("Dim", parent).gameObject.AddComponent<RawImage>();
        var dimRect = dim.rectTransform;
        dimRect.anchorMin = Vector2.zero;
        dimRect.anchorMax = Vector2.one;
        dimRect.sizeDelta = Vector2.zero;
        dim.raycastTarget = false;
        dim.color = new Color(1f, 1f, 1f, 0f);
        dim.enabled = false;

        var ringTexture = RingTexture();
        for (int i = 0; i < rings.Length; i++)
        {
            var ring = NewRect("Ring", parent).gameObject.AddComponent<RawImage>();
            ring.texture = ringTexture;
            ring.color = Gold;
            ring.raycastTarget = false;
            ring.rectTransform.anchorMin = ring.rectTransform.anchorMax = Vector2.zero;
            ring.enabled = false;
            rings[i] = ring;
        }
    }

    static Texture2D RingTexture()
    {
        const int size = 128;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "Tutorial Ring" };
        var pixels = new Color32[size * size];
        float half = size * 0.5f;
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float d = new Vector2(x + 0.5f - half, y + 0.5f - half).magnitude;
            float a = Mathf.Clamp01(1f - Mathf.Abs(d - (half - 5f)) / 2.5f);
            pixels[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
        }
        texture.SetPixels32(pixels);
        texture.Apply(false, true);
        return texture;
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
