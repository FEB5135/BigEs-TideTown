using System.Collections.Generic;
using UnityEngine;

// Oyun içi kısa sesler (ağaca vurma, ağacın kırılması ve yere düşmesi, ocağa vurma, vagon). Sahnede bir tane olur.
// Sesler 2B çalınır; ses yüksekliği kameranın olaya uzaklığıyla azalır: oyun başındaki uzak görünümde (80 m) sessiz,
// oyuncu yakınlaştırınca duyulur. Aynı ses peş peşe tekdüze olmasın diye çeşitlerden rastgele biri ve hafif farklı perdeyle çalınır.
public class GameAudio : MonoBehaviour
{
    public static GameAudio Instance { get; private set; }

    [Header("Ağaç")]
    public AudioClip[] treeHit;
    public AudioClip[] treeCrack;
    public AudioClip[] treeThud;

    [Header("Maden")]
    public AudioClip[] stoneHit;
    public AudioClip cartRoll;
    public AudioClip[] cartClank;

    [Header("Ses düzeyi")]
    [Range(0f, 1f)] public float volume = 0.8f;
    [Tooltip("Kamera olaya bu uzaklıktaysa (m) tam ses, uzaklaştıkça azalır.")]
    [SerializeField] float fullVolumeDistance = 30f;
    [Tooltip("Kamera olaya bu uzaklıktan uzaksa ses duyulmaz.")]
    [SerializeField] float silentDistance = 60f;
    [SerializeField] int voices = 12;

    readonly List<AudioSource> pool = new();
    int next;
    Camera cam;

    void Awake()
    {
        Instance = this;
        for (int i = 0; i < voices; i++)
        {
            var source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            pool.Add(source);
        }
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    public static void Play(AudioClip[] clips, Vector3 position, float gain = 1f, float pitchJitter = 0.08f)
    {
        if (Instance == null || clips == null || clips.Length == 0)
            return;
        Instance.PlayAt(clips[Random.Range(0, clips.Length)], position, gain, pitchJitter);
    }

    void PlayAt(AudioClip clip, Vector3 position, float gain, float pitchJitter)
    {
        float level = volume * gain * Attenuation(position);
        if (clip == null || level <= 0.01f)
            return;
        var source = pool[next];
        next = (next + 1) % pool.Count;
        source.pitch = 1f + Random.Range(-pitchJitter, pitchJitter);
        source.PlayOneShot(clip, level);
    }

    // Kameranın olaya uzaklığına göre 1 (yakın) → 0 (uzak).
    public float Attenuation(Vector3 position)
    {
        if (cam == null)
            cam = Camera.main;
        if (cam == null)
            return 1f;
        float distance = Vector3.Distance(cam.transform.position, position);
        float k = Mathf.InverseLerp(silentDistance, fullVolumeDistance, distance);
        return k * k; // uzaktan yavaşça açılır
    }
}
