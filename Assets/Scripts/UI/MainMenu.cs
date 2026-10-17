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
    public static bool IsOpen { get; private set; }

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
        GameStarted?.Invoke();
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
