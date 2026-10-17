using UnityEngine;

// Oyuncu ayarları (ses, tam ekran, grafik). PlayerPrefs'te saklanır, oyun açılınca uygulanır.
public static class GameSettings
{
    const string VolumeKey = "BigEs.Volume";
    const string FullscreenKey = "BigEs.Fullscreen";
    const string QualityKey = "BigEs.Quality";

    public static float Volume
    {
        get => PlayerPrefs.GetFloat(VolumeKey, 0.8f);
        set
        {
            value = Mathf.Clamp01(value);
            PlayerPrefs.SetFloat(VolumeKey, value);
            AudioListener.volume = value;
        }
    }

    public static bool Fullscreen
    {
        get => PlayerPrefs.GetInt(FullscreenKey, Screen.fullScreen ? 1 : 0) == 1;
        set
        {
            PlayerPrefs.SetInt(FullscreenKey, value ? 1 : 0);
            Screen.fullScreen = value;
        }
    }

    public static int Quality
    {
        get => Mathf.Clamp(PlayerPrefs.GetInt(QualityKey, QualitySettings.GetQualityLevel()), 0, QualitySettings.names.Length - 1);
        set
        {
            value = Mathf.Clamp(value, 0, QualitySettings.names.Length - 1);
            PlayerPrefs.SetInt(QualityKey, value);
            QualitySettings.SetQualityLevel(value, true);
        }
    }

    public static void Save() => PlayerPrefs.Save();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void ApplySaved()
    {
        AudioListener.volume = Volume;
        if (PlayerPrefs.HasKey(QualityKey))
            QualitySettings.SetQualityLevel(Quality, true);
        if (PlayerPrefs.HasKey(FullscreenKey) && !Application.isEditor)
            Screen.fullScreen = Fullscreen;
    }
}
