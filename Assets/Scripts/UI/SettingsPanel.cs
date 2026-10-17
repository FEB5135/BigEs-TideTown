using UnityEngine;
using UnityEngine.UI;

// Ana menüdeki ayarlar sayfası: ses kaydırıcısı, tam ekran ve grafik kalitesi seçicileri.
public class SettingsPanel : MonoBehaviour
{
    [SerializeField] Slider volumeSlider;
    [SerializeField] Text volumeValue;

    [SerializeField] Button fullscreenPrev;
    [SerializeField] Button fullscreenNext;
    [SerializeField] Text fullscreenValue;

    [SerializeField] Button qualityPrev;
    [SerializeField] Button qualityNext;
    [SerializeField] Text qualityValue;
    [Tooltip("Kalite seviyelerinin görünen adları (QualitySettings sırasıyla). Eksikse Unity'deki ad yazılır.")]
    [SerializeField] string[] qualityLabels = { "Düşük", "Yüksek" };

    void Awake()
    {
        if (volumeSlider != null)
        {
            volumeSlider.minValue = 0f;
            volumeSlider.maxValue = 1f;
            volumeSlider.onValueChanged.AddListener(v =>
            {
                GameSettings.Volume = v;
                Refresh();
            });
        }
        fullscreenPrev?.onClick.AddListener(ToggleFullscreen);
        fullscreenNext?.onClick.AddListener(ToggleFullscreen);
        qualityPrev?.onClick.AddListener(() => StepQuality(-1));
        qualityNext?.onClick.AddListener(() => StepQuality(1));
    }

    void OnEnable()
    {
        if (volumeSlider != null)
            volumeSlider.SetValueWithoutNotify(GameSettings.Volume);
        Refresh();
    }

    void OnDisable() => GameSettings.Save();

    void ToggleFullscreen()
    {
        GameSettings.Fullscreen = !GameSettings.Fullscreen;
        Refresh();
    }

    void StepQuality(int step)
    {
        int count = QualitySettings.names.Length;
        GameSettings.Quality = (GameSettings.Quality + step + count) % count;
        Refresh();
    }

    void Refresh()
    {
        if (volumeValue != null)
            volumeValue.text = $"%{Mathf.RoundToInt(GameSettings.Volume * 100f)}";
        if (fullscreenValue != null)
            fullscreenValue.text = GameSettings.Fullscreen ? "Açık" : "Kapalı";
        if (qualityValue != null)
        {
            int q = GameSettings.Quality;
            qualityValue.text = qualityLabels != null && q < qualityLabels.Length && !string.IsNullOrEmpty(qualityLabels[q])
                ? qualityLabels[q]
                : QualitySettings.names[q];
        }
    }
}
