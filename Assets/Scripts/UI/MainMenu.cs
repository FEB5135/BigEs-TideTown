using System;
using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// Ana menü: logo, en yüksek skor, Oyna / Ayarlar / Çıkış.
// Arka planda harita uzaktan ve bulanık görünür (CameraIntro). Oyna deyince menü kaybolur,
// kamera oyun duruşuna iner, kaynak paneli belirir ve GameStarted olayı çağrılır.
public class MainMenu : MonoBehaviour
{
    // Kamera oyuna indiğinde, oyuncu kontrolü aldığı anda çağrılır.
    public static event Action GameStarted;
    public static event Action TutorialReady;
    public static bool IsOpen { get; private set; }
    // "Tekrar Oyna" ile sahne yeniden yüklenince menü beklemeden oyun başlar.
    public static bool PlayOnLoad { get; set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        PlayOnLoad = false;
        TutorialReady = null;
    }

    [SerializeField] CameraIntro cameraIntro;
    [Tooltip("Menünün tamamı (kaybolurken kullanılır).")]
    [SerializeField] CanvasGroup menuGroup;
    [Tooltip("Menüdeyken gizlenen, oyun başlayınca beliren arayüz (kaynak paneli...).")]
    [SerializeField] CanvasGroup[] gameplayUI;
    [Tooltip("Menüdeyken kapalı tutulan, oyun başlayınca açılan bileşenler (duvar inşası...).")]
    [SerializeField] Behaviour[] gameplayBehaviours;

    [Header("Sayfalar")]
    [SerializeField] CanvasGroup mainPage;
    [SerializeField] CanvasGroup settingsPage;

    [Header("Ana sayfa")]
    [SerializeField] Text highScoreText;
    [SerializeField] Button playButton;
    [SerializeField] Button settingsButton;
    [SerializeField] Button quitButton;
    [SerializeField] Button[] backButtons;
    [Tooltip("Açılışta sırayla aşağıdan kayarak gelen öğeler.")]
    [SerializeField] RectTransform[] introItems;

    [Header("Oyun başlangıcı")]
    [Tooltip("Menüde arka planda toplanan kaynaklar, kamera oyuna inince sıfırlanır.")]
    [SerializeField] bool resetResourcesOnPlay = true;

    // Oyun sonu ekranı butonlarını bunlardan kopyalar (aynı görünüm).
    public Button PlayButtonTemplate => playButton;
    public Button QuitButtonTemplate => quitButton;

    CanvasGroup currentPage;
    Coroutine pageSwitch;

    void Start()
    {
        IsOpen = true;
        if (cameraIntro != null)
            cameraIntro.EnterMenu();
        foreach (var group in gameplayUI)
            SetVisible(group, false);
        foreach (var behaviour in gameplayBehaviours)
            if (behaviour != null)
                behaviour.enabled = false;

        playButton?.onClick.AddListener(Play);
        settingsButton?.onClick.AddListener(() => ShowPage(settingsPage));
        quitButton?.onClick.AddListener(Quit);
        foreach (var back in backButtons)
            back.onClick.AddListener(() => ShowPage(mainPage));
#if UNITY_WEBGL
        if (quitButton != null)
            quitButton.gameObject.SetActive(false);
#endif

        if (highScoreText != null)
            highScoreText.text = HighScore.Best.ToString("N0");

        SetVisible(settingsPage, false);
        SetVisible(mainPage, true);
        currentPage = mainPage;
        if (PlayOnLoad)
        {
            PlayOnLoad = false;
            Play();
            return;
        }
        StartCoroutine(Entrance());
    }

    void Update()
    {
        if (!IsOpen || currentPage == mainPage)
            return;
        var keyboard = Keyboard.current;
        if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
            ShowPage(mainPage);
    }

    void OnDestroy()
    {
        IsOpen = false;
    }

    public void Play()
    {
        if (!IsOpen)
            return;
        IsOpen = false;
        if (menuGroup != null)
            menuGroup.interactable = false;

        StartCoroutine(FadeOutMenu());
        if (cameraIntro != null)
            cameraIntro.PlayIntro(OnIntroFinished);
        else
            OnIntroFinished();
    }

    void OnIntroFinished()
    {
        if (resetResourcesOnPlay && Well.Instance != null)
            Well.Instance.ResetAmounts();
        foreach (var group in gameplayUI)
            StartCoroutine(Fade(group, 1f, 0.4f, true));
        foreach (var behaviour in gameplayBehaviours)
            if (behaviour != null)
                behaviour.enabled = true;
        StartCoroutine(ShowPreparationNotice());
        GameStarted?.Invoke();
    }

    IEnumerator ShowPreparationNotice()
    {
        var root = new GameObject("Preparation Notice", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(CanvasGroup));
        var canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 35;
        var scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        var group = root.GetComponent<CanvasGroup>();
        group.interactable = group.blocksRaycasts = false;
        var tutorial = FindAnyObjectByType<TutorialHints>();
        var font = tutorial != null ? tutorial.CardFont : highScoreText != null ? highScoreText.font : null;
        if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        var card = new GameObject("Card", typeof(RectTransform), typeof(Image));
        card.transform.SetParent(root.transform, false);
        var rect = (RectTransform)card.transform;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.58f);
        rect.sizeDelta = new Vector2(900f, 180f);
        var background = card.GetComponent<Image>();
        background.color = new Color(0.043f, 0.165f, 0.239f, 0.94f);
        background.raycastTarget = false;
        if (tutorial != null && tutorial.CardSprite != null)
        {
            background.sprite = tutorial.CardSprite;
            background.type = Image.Type.Sliced;
        }
        AddLine("Title", "Tsunamiye Hazırlan", 48, new Color(1f, 0.824f, 0.247f), 28f, 70f);
        AddLine("Goal", "Kaynak topla, işçi görevlendir ve köyünü duvarlarla koru.", 28, Color.white, -38f, 60f);
        try
        {
            for (float time = 0f; time < 3f; time += Time.unscaledDeltaTime)
            {
                group.alpha = Mathf.Min(Mathf.Clamp01(time / 0.3f), Mathf.Clamp01((3f - time) / 0.5f));
                rect.localScale = Vector3.one * Mathf.Lerp(0.96f, 1f, EaseOutCubic(time / 0.3f));
                yield return null;
            }
        }
        finally
        {
            if (root != null)
            {
                root.SetActive(false);
                Destroy(root);
            }
        }
        TutorialReady?.Invoke();

        void AddLine(string name, string message, int size, Color color, float y, float height)
        {
            var line = new GameObject(name, typeof(RectTransform), typeof(Text));
            line.transform.SetParent(card.transform, false);
            var text = line.GetComponent<Text>();
            text.font = font;
            text.text = message;
            text.fontSize = size;
            text.color = color;
            text.alignment = TextAnchor.MiddleCenter;
            text.raycastTarget = false;
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = 20;
            text.resizeTextMaxSize = size;
            text.rectTransform.sizeDelta = new Vector2(840f, height);
            text.rectTransform.anchoredPosition = new Vector2(0f, y);
        }
    }

    void Quit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    void ShowPage(CanvasGroup page)
    {
        if (page == null || page == currentPage)
            return;
        if (pageSwitch != null)
            StopCoroutine(pageSwitch);
        pageSwitch = StartCoroutine(SwitchPage(currentPage, page));
        currentPage = page;
    }

    IEnumerator SwitchPage(CanvasGroup from, CanvasGroup to)
    {
        const float duration = 0.16f;
        if (from != null)
        {
            from.interactable = from.blocksRaycasts = false;
            yield return Fade(from, 0f, duration, false, 1f, 0.97f);
            from.gameObject.SetActive(false);
        }
        to.gameObject.SetActive(true);
        yield return Fade(to, 1f, duration, true, 1.03f, 1f);
        pageSwitch = null;
    }

    // Logo, skor ve butonlar kısa aralıklarla aşağıdan kayarak belirir.
    IEnumerator Entrance()
    {
        if (introItems == null || introItems.Length == 0)
            yield break;
        var groups = new CanvasGroup[introItems.Length];
        var targets = new Vector2[introItems.Length];
        for (int i = 0; i < introItems.Length; i++)
        {
            var item = introItems[i];
            groups[i] = item.GetComponent<CanvasGroup>();
            if (groups[i] == null)
                groups[i] = item.gameObject.AddComponent<CanvasGroup>();
            groups[i].alpha = 0f;
            targets[i] = item.anchoredPosition;
        }

        const float stagger = 0.07f, duration = 0.45f, rise = 26f;
        float total = stagger * (introItems.Length - 1) + duration;
        for (float t = 0f; t < total + Time.unscaledDeltaTime; t += Time.unscaledDeltaTime)
        {
            for (int i = 0; i < introItems.Length; i++)
            {
                float k = EaseOutCubic(Mathf.Clamp01((t - i * stagger) / duration));
                groups[i].alpha = k;
                introItems[i].anchoredPosition = targets[i] - new Vector2(0f, (1f - k) * rise);
            }
            yield return null;
        }
        for (int i = 0; i < introItems.Length; i++)
        {
            groups[i].alpha = 1f;
            introItems[i].anchoredPosition = targets[i];
        }
    }

    IEnumerator FadeOutMenu()
    {
        if (menuGroup == null)
            yield break;
        var rect = (RectTransform)menuGroup.transform;
        const float duration = 0.5f;
        float startAlpha = menuGroup.alpha;
        for (float t = 0f; t < duration; t += Time.unscaledDeltaTime)
        {
            float k = t / duration;
            menuGroup.alpha = Mathf.Lerp(startAlpha, 0f, k * k);
            rect.localScale = Vector3.one * Mathf.Lerp(1f, 1.06f, EaseOutCubic(k));
            yield return null;
        }
        menuGroup.alpha = 0f;
        menuGroup.blocksRaycasts = false;
        // Obje kapatılmaz: bu script'in coroutine'leri (HUD'un belirmesi) çalışmaya devam etmeli.
        var canvas = menuGroup.GetComponent<Canvas>();
        if (canvas != null)
            canvas.enabled = false;
    }

    IEnumerator Fade(CanvasGroup group, float to, float duration, bool interactableAtEnd, float fromScale = 1f, float toScale = 1f)
    {
        if (group == null)
            yield break;
        group.gameObject.SetActive(true);
        var rect = (RectTransform)group.transform;
        float from = group.alpha;
        for (float t = 0f; t < duration; t += Time.unscaledDeltaTime)
        {
            float k = EaseOutCubic(t / duration);
            group.alpha = Mathf.Lerp(from, to, k);
            rect.localScale = Vector3.one * Mathf.Lerp(fromScale, toScale, k);
            yield return null;
        }
        group.alpha = to;
        rect.localScale = Vector3.one * toScale;
        group.interactable = group.blocksRaycasts = interactableAtEnd;
    }

    static void SetVisible(CanvasGroup group, bool visible)
    {
        if (group == null)
            return;
        group.alpha = visible ? 1f : 0f;
        group.interactable = group.blocksRaycasts = visible;
        if (group.transform.parent != null && group.GetComponent<Canvas>() == null)
            group.gameObject.SetActive(visible);
    }

    static float EaseOutCubic(float x)
    {
        x = 1f - Mathf.Clamp01(x);
        return 1f - x * x * x;
    }
}
