using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

// Ana menüdeki ayarlar sayfası: müzik ve efekt sesi kaydırıcıları, tam ekran seçicisi.
public class SettingsPanel : MonoBehaviour
{
    [SerializeField, FormerlySerializedAs("volumeSlider")] Slider musicSlider;
    [SerializeField, FormerlySerializedAs("volumeValue")] Text musicValue;

    [SerializeField] Slider effectsSlider;
    [SerializeField] Text effectsValue;

    [SerializeField] Button fullscreenPrev;
    [SerializeField] Button fullscreenNext;
    [SerializeField] Text fullscreenValue;

    void Awake()
    {
        Setup(musicSlider, v => GameSettings.MusicVolume = v);
        Setup(effectsSlider, v => GameSettings.EffectsVolume = v);
        fullscreenPrev?.onClick.AddListener(ToggleFullscreen);
        fullscreenNext?.onClick.AddListener(ToggleFullscreen);
    }

    void Setup(Slider slider, System.Action<float> apply)
    {
        if (slider == null)
            return;
        slider.minValue = 0f;
        slider.maxValue = 1f;
        slider.onValueChanged.AddListener(v =>
        {
            apply(v);
            Refresh();
        });
    }

    void OnEnable()
    {
        if (musicSlider != null)
            musicSlider.SetValueWithoutNotify(GameSettings.MusicVolume);
        if (effectsSlider != null)
            effectsSlider.SetValueWithoutNotify(GameSettings.EffectsVolume);
        Refresh();
    }

    void OnDisable() => GameSettings.Save();

    void ToggleFullscreen()
    {
        GameSettings.Fullscreen = !GameSettings.Fullscreen;
        Refresh();
    }

    void Refresh()
    {
        if (musicValue != null)
            musicValue.text = $"%{Mathf.RoundToInt(GameSettings.MusicVolume * 100f)}";
        if (effectsValue != null)
            effectsValue.text = $"%{Mathf.RoundToInt(GameSettings.EffectsVolume * 100f)}";
        if (fullscreenValue != null)
            fullscreenValue.text = GameSettings.Fullscreen ? "Açık" : "Kapalı";
    }
}
