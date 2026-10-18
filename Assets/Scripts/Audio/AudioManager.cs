using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// Oyun içi müzik ve ses efektlerini yöneten merkezi ses yöneticisi (Singleton).
/// İki kanallı AudioSource yapısı sayesinde parçalar arası yumuşak geçiş (Crossfade) sağlar.
/// </summary>
[DisallowMultipleComponent]
public class AudioManager : MonoBehaviour
{
    public static AudioManager Instance { get; private set; }

    public enum GamePhase
    {
        Menu,
        IdleBuilding,     // Köy kurma, odun/taş toplama ve duvar örme fazı
        TsunamiDefense,   // Tsunami dalgasının köye yaklaştığı savaş/savunma fazı (Age of War stili)
        Victory,          // Köy dalgadan kurtulduğunda
        GameOver          // Köy sular altında kaldığında
    }

    [Header("Müzik Parçaları (Music Tracks)")]
    [Tooltip("Köy inşa ve kaynak toplama fazında çalacak parça (Örn: Townie_Loop)")]
    [SerializeField] private AudioClip idlePhaseMusic;

    [Tooltip("Tsunami dalgası yaklaşırken çalacak parça (Örn: Glorious_Morning veya Volatile_Reaction)")]
    [SerializeField] private AudioClip tsunamiPhaseMusic;

    [Tooltip("Ana menüde çalacak parça (Opsiyonel)")]
    [SerializeField] private AudioClip menuMusic;

    [Tooltip("Köy dalgayı atlattığında çalacak zafer müziği (Opsiyonel)")]
    [SerializeField] private AudioClip victoryMusic;

    [Tooltip("Köy yıkıldığında çalacak yenilgi müziği (Opsiyonel)")]
    [SerializeField] private AudioClip gameOverMusic;

    [Header("Ses Seviyeleri (Volume)")]
    [Range(0f, 1f)] [SerializeField] private float masterVolume = 1f;
    [Range(0f, 1f)] [SerializeField] private float musicVolume = 0.8f;
    [Range(0f, 1f)] [SerializeField] private float sfxVolume = 1f;

    [Header("Başlangıç Ayarları")]
    [Tooltip("Sahneler arası geçişte ses yöneticisinin korunmasını sağlar")]
    [SerializeField] private bool persistAcrossScenes = true;

    [Tooltip("Oyun başlar başlamaz müzik çalınsın mı?")]
    [SerializeField] private bool autoPlayOnStart = true;

    [Tooltip("Oyun başladığında hangi fazın müziği çalsın?")]
    [SerializeField] private GamePhase startingPhase = GamePhase.IdleBuilding;

    [Tooltip("Parçalar arası yumuşak geçiş süresi (saniye)")]
    [SerializeField] private float defaultCrossfadeDuration = 1.5f;

    [Tooltip("Test: 1 / 2 tuşları müzik fazını değiştirir. Oyunda kapalı olmalı (aynı tuşlar duvar malzemesini seçer).")]
    [SerializeField] private bool debugPhaseKeys;

    [Header("Ses Kanalları (Boş bırakılırsa otomatik oluşturulur)")]
    [SerializeField] private AudioSource musicSourceA;
    [SerializeField] private AudioSource musicSourceB;
    [SerializeField] private AudioSource sfxSource;

    private AudioSource activeMusicSource;
    private Coroutine crossfadeCoroutine;
    private GamePhase currentPhase = GamePhase.IdleBuilding;

    public GamePhase CurrentPhase => currentPhase;
    public float MasterVolume => masterVolume;
    public float MusicVolume => musicVolume;
    public float SfxVolume => sfxVolume;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;

        if (persistAcrossScenes)
        {
            transform.SetParent(null);
            DontDestroyOnLoad(gameObject);
        }

        SetupAudioSources();
    }

    private void Start()
    {
        if (autoPlayOnStart)
        {
            SetPhase(startingPhase, defaultCrossfadeDuration);
        }
    }

    private void SetupAudioSources()
    {
        if (musicSourceA == null)
        {
            var childA = new GameObject("MusicSource_A");
            childA.transform.SetParent(transform);
            musicSourceA = childA.AddComponent<AudioSource>();
        }

        if (musicSourceB == null)
        {
            var childB = new GameObject("MusicSource_B");
            childB.transform.SetParent(transform);
            musicSourceB = childB.AddComponent<AudioSource>();
        }

        if (sfxSource == null)
        {
            var childSfx = new GameObject("SFXSource");
            childSfx.transform.SetParent(transform);
            sfxSource = childSfx.AddComponent<AudioSource>();
        }

        ConfigureMusicSource(musicSourceA);
        ConfigureMusicSource(musicSourceB);

        sfxSource.playOnAwake = false;
        sfxSource.loop = false;
        sfxSource.spatialBlend = 0f; // 2D ses

        activeMusicSource = musicSourceA;
    }

    private void ConfigureMusicSource(AudioSource source)
    {
        source.playOnAwake = false;
        source.loop = true;
        source.spatialBlend = 0f; // 2D müzik
        source.volume = 0f;
    }

    /// <summary>
    /// Oyun fazına göre müziği yumuşak bir geçişle değiştirir.
    /// Örnek: AudioManager.Instance.SetPhase(AudioManager.GamePhase.TsunamiDefense);
    /// </summary>
    public void SetPhase(GamePhase phase, float fadeDuration = -1f)
    {
        currentPhase = phase;
        var duration = fadeDuration >= 0f ? fadeDuration : defaultCrossfadeDuration;
        var targetClip = GetClipForPhase(phase);

        if (targetClip == null)
        {
            Debug.LogWarning($"[AudioManager] '{phase}' fazı için atanmış bir müzik klibi bulunamadı.");
            return;
        }

        PlayMusic(targetClip, true, duration);
    }

    /// <summary>
    /// Belirtilen AudioClip'i yumuşak geçişle (crossfade) çalmaya başlar.
    /// </summary>
    public void PlayMusic(AudioClip clip, bool loop = true, float fadeDuration = -1f)
    {
        if (clip == null) return;

        var duration = fadeDuration >= 0f ? fadeDuration : defaultCrossfadeDuration;

        // Halihazırda bu parça çalıyorsa yeniden başlatma
        if (activeMusicSource != null && activeMusicSource.clip == clip && activeMusicSource.isPlaying)
        {
            return;
        }

        if (crossfadeCoroutine != null)
        {
            StopCoroutine(crossfadeCoroutine);
        }

        crossfadeCoroutine = StartCoroutine(CrossfadeRoutine(clip, loop, duration));
    }

    private IEnumerator CrossfadeRoutine(AudioClip newClip, bool loop, float duration)
    {
        var fadingOutSource = activeMusicSource;
        var fadingInSource = (activeMusicSource == musicSourceA) ? musicSourceB : musicSourceA;

        fadingInSource.clip = newClip;
        fadingInSource.loop = loop;
        fadingInSource.time = 0f;
        fadingInSource.volume = 0f;
        fadingInSource.Play();

        float startOutVolume = fadingOutSource != null ? fadingOutSource.volume : 0f;
        float targetInVolume = masterVolume * musicVolume;
        float timer = 0f;

        if (duration <= 0.01f)
        {
            if (fadingOutSource != null)
            {
                fadingOutSource.volume = 0f;
                fadingOutSource.Stop();
            }
            fadingInSource.volume = targetInVolume;
        }
        else
        {
            while (timer < duration)
            {
                // Oyun durdurulsa bile (Time.timeScale = 0) ses geçişi düzgün çalışsın
                timer += Time.unscaledDeltaTime;
                float progress = Mathf.Clamp01(timer / duration);

                if (fadingOutSource != null)
                {
                    fadingOutSource.volume = Mathf.Lerp(startOutVolume, 0f, progress);
                }

                fadingInSource.volume = Mathf.Lerp(0f, targetInVolume, progress);
                yield return null;
            }

            if (fadingOutSource != null)
            {
                fadingOutSource.volume = 0f;
                fadingOutSource.Stop();
            }

            fadingInSource.volume = targetInVolume;
        }

        activeMusicSource = fadingInSource;
        crossfadeCoroutine = null;
    }

    /// <summary>
    /// Çalmakta olan müziği belirtilen süre içinde kısıp durdurur.
    /// </summary>
    public void StopMusic(float fadeDuration = 1f)
    {
        if (crossfadeCoroutine != null)
        {
            StopCoroutine(crossfadeCoroutine);
        }

        if (activeMusicSource != null && activeMusicSource.isPlaying)
        {
            StartCoroutine(FadeOutRoutine(activeMusicSource, fadeDuration));
        }
    }

    private IEnumerator FadeOutRoutine(AudioSource source, float duration)
    {
        float startVol = source.volume;
        float timer = 0f;

        while (timer < duration)
        {
            timer += Time.unscaledDeltaTime;
            source.volume = Mathf.Lerp(startVol, 0f, timer / duration);
            yield return null;
        }

        source.volume = 0f;
        source.Stop();
    }

    /// <summary>
    /// Tek seferlik 2D ses efekti çalar (Arayüz butonları, duvar koyma vb.)
    /// </summary>
    public void PlaySFX(AudioClip clip, float volumeScale = 1f)
    {
        if (clip == null || sfxSource == null) return;
        sfxSource.PlayOneShot(clip, Mathf.Clamp01(volumeScale) * masterVolume * sfxVolume);
    }

    /// <summary>
    /// Sahne üzerinde belirli bir 3D konumda ses efekti çalar.
    /// </summary>
    public void PlaySFXAtPosition(AudioClip clip, Vector3 position, float volumeScale = 1f)
    {
        if (clip == null) return;
        AudioSource.PlayClipAtPoint(clip, position, Mathf.Clamp01(volumeScale) * masterVolume * sfxVolume);
    }

    // Ses Seviyesi Kontrolleri
    public void SetMasterVolume(float volume)
    {
        masterVolume = Mathf.Clamp01(volume);
        UpdateActiveVolumes();
    }

    public void SetMusicVolume(float volume)
    {
        musicVolume = Mathf.Clamp01(volume);
        UpdateActiveVolumes();
    }

    public void SetSFXVolume(float volume)
    {
        sfxVolume = Mathf.Clamp01(volume);
    }

    private void UpdateActiveVolumes()
    {
        if (activeMusicSource != null && activeMusicSource.isPlaying && crossfadeCoroutine == null)
        {
            activeMusicSource.volume = masterVolume * musicVolume;
        }
    }

    private void Update()
    {
#if ENABLE_INPUT_SYSTEM
        var kb = UnityEngine.InputSystem.Keyboard.current;
        if (debugPhaseKeys && kb != null)
        {
            if (kb.digit1Key.wasPressedThisFrame)
            {
                SetPhase(GamePhase.IdleBuilding);
                Debug.Log("[AudioManager] Test: Idle (Köy / İnşa) fazına geçildi.");
            }
            else if (kb.digit2Key.wasPressedThisFrame)
            {
                SetPhase(GamePhase.TsunamiDefense);
                Debug.Log("[AudioManager] Test: Tsunami Savunması (Age of War) fazına geçildi!");
            }
        }
#endif
    }

    private AudioClip GetClipForPhase(GamePhase phase)
    {
        return phase switch
        {
            GamePhase.Menu => menuMusic != null ? menuMusic : idlePhaseMusic,
            GamePhase.IdleBuilding => idlePhaseMusic,
            GamePhase.TsunamiDefense => tsunamiPhaseMusic,
            GamePhase.Victory => victoryMusic,
            GamePhase.GameOver => gameOverMusic,
            _ => null
        };
    }

    // Unity Editöründe Hızlı Test İçin Sağ Tık Menüleri (Context Menu)
    [ContextMenu("Fazı Değiştir: Tsunami Savunması (Age of War)")]
    private void TestTsunamiPhase() => SetPhase(GamePhase.TsunamiDefense);

    [ContextMenu("Fazı Değiştir: Köy / İnşa (Idle)")]
    private void TestIdlePhase() => SetPhase(GamePhase.IdleBuilding);

#if UNITY_EDITOR
    private void Reset()
    {
        AutoAssignDefaultClips();
    }

    private void OnValidate()
    {
        if (tsunamiPhaseMusic == null || idlePhaseMusic == null)
        {
            AutoAssignDefaultClips();
        }
    }

    private void AutoAssignDefaultClips()
    {
        if (tsunamiPhaseMusic == null)
        {
            tsunamiPhaseMusic = FindAudioClipAsset("Glorious_Morning") ?? FindAudioClipAsset("Volatile_Reaction");
        }

        if (idlePhaseMusic == null)
        {
            idlePhaseMusic = FindAudioClipAsset("Townie_Loop");
        }
    }

    private static AudioClip FindAudioClipAsset(string nameWithoutExtension)
    {
        string[] guids = UnityEditor.AssetDatabase.FindAssets($"{nameWithoutExtension} t:AudioClip");
        foreach (string guid in guids)
        {
            string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
            var clip = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            if (clip != null && clip.name.Equals(nameWithoutExtension, StringComparison.OrdinalIgnoreCase))
            {
                return clip;
            }
        }
        return null;
    }
#endif
}
