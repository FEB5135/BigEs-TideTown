using System;
using UnityEngine;

// Oyuncu ayarları (müzik ve efekt sesi, tam ekran). PlayerPrefs'te saklanır, oyun açılınca uygulanır.
public static class GameSettings
{
    const string MusicKey = "BigEs.MusicVolume";
    const string EffectsKey = "BigEs.EffectsVolume";
    const string FullscreenKey = "BigEs.Fullscreen";

    // Ses ayarlarından biri değişti: müzik ve efekt çalanlar düzeylerini yeniler.
    public static event Action VolumeChanged;

    public static float MusicVolume
    {
        get => PlayerPrefs.GetFloat(MusicKey, 0.6f);
        set
        {
            PlayerPrefs.SetFloat(MusicKey, Mathf.Clamp01(value));
            VolumeChanged?.Invoke();
        }
    }

    public static float EffectsVolume
    {
        get => PlayerPrefs.GetFloat(EffectsKey, 0.8f);
        set
        {
            PlayerPrefs.SetFloat(EffectsKey, Mathf.Clamp01(value));
            VolumeChanged?.Invoke();
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

    public static void Save() => PlayerPrefs.Save();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void ApplySaved()
    {
        // Müzik ve efektler kendi düzeylerini ayrı ayrı uygular; eski tek ses ayarı (dinleyici düzeyi) kullanılmıyor.
        AudioListener.volume = 1f;
        if (PlayerPrefs.HasKey(FullscreenKey) && !Application.isEditor)
            Screen.fullScreen = Fullscreen;
    }
}
