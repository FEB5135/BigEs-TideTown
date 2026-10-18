using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Sahneyi yeniden yüklerken ekranı kapatan geçiş: ekran kararır, "YÜKLENİYOR" yazar, sahne arka planda yüklenir.
// Yeni sahnenin ilk karelerindeki ağır işler (WFC haritası, parça mesh'leri ve collider'ları, NavMesh) bu perdenin
// arkasında olur; harita hazır olunca perde açılır. Oyun sonu ekranının "Tekrar Oyna" ve "Ana Menü" butonları kullanır.
public class SceneTransition : MonoBehaviour
{
    const float FadeIn = 0.3f;
    const float FadeOut = 0.45f;
    const float MaxWait = 6f;

    static SceneTransition running;

    // Perde açıkken (sahne yüklenirken) true; IMGUI paneller bu sürede çizilmez.
    public static bool Active => running != null;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => running = null; // Domain reload kapalı: Play'ler arasında kalmasın.

    CanvasGroup group;
    Text label;

    // play: sahne açılınca menü beklenmeden oyun başlar (MainMenu.PlayOnLoad).
    public static void Reload(bool play, Font font = null)
    {
        if (running != null)
            return;
        var host = new GameObject("Scene Transition");
        DontDestroyOnLoad(host);
        running = host.AddComponent<SceneTransition>();
        running.Build(font != null ? font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));
        running.StartCoroutine(running.Run(play));
    }

    IEnumerator Run(bool play)
    {
        yield return Fade(0f, 1f, FadeIn);

        var load = SceneManager.LoadSceneAsync(SceneManager.GetActiveScene().buildIndex);
        load.allowSceneActivation = false;
        while (load.progress < 0.9f)
            yield return null;
        // Oyun sonu oyunu durdurmuştu; yeni sahne normal hızda başlar.
        Time.timeScale = 1f;
        MainMenu.PlayOnLoad = play;
        load.allowSceneActivation = true;
        while (!load.isDone)
            yield return null;

        // Harita ve köylülerin yolları hazır olana kadar perde kapalı kalır.
        float waited = 0f;
        do
        {
            yield return null;
            waited += Time.unscaledDeltaTime;
        } while (waited < 0.2f || (NavigationBaker.Exists && !NavigationBaker.Ready && waited < MaxWait));

        yield return Fade(1f, 0f, FadeOut);
        running = null;
        Destroy(gameObject);
    }

    void Update()
    {
        if (label != null)
            label.color = new Color(label.color.r, label.color.g, label.color.b, 0.55f + 0.45f * (0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 4f)));
    }

    IEnumerator Fade(float from, float to, float duration)
    {
        for (float t = 0f; t < duration; t += Time.unscaledDeltaTime)
        {
            group.alpha = Mathf.Lerp(from, to, Mathf.SmoothStep(0f, 1f, t / duration));
            yield return null;
        }
        group.alpha = to;
    }

    void Build(Font font)
    {
        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 1000; // her şeyin üstünde
        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        gameObject.AddComponent<GraphicRaycaster>(); // yüklenirken tıklamalar alttakilere geçmesin
        group = gameObject.AddComponent<CanvasGroup>();
        group.alpha = 0f;

        var background = new GameObject("Background", typeof(RectTransform)).AddComponent<Image>();
        background.transform.SetParent(transform, false);
        Stretch(background.rectTransform);
        background.color = new Color(0.027f, 0.106f, 0.157f, 1f);

        label = new GameObject("Label", typeof(RectTransform)).AddComponent<Text>();
        label.transform.SetParent(transform, false);
        Stretch(label.rectTransform);
        label.font = font;
        label.fontSize = 40;
        label.alignment = TextAnchor.MiddleCenter;
        label.color = new Color(0.910f, 0.969f, 0.980f, 1f);
        label.text = "YÜKLENİYOR";
        label.raycastTarget = false;
    }

    static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
    }
}
