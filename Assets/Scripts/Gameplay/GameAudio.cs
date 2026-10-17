using System.Collections.Generic;
using UnityEngine;

// Oyun içi kısa sesler (ağaca vurma, ağacın kırılması ve yere düşmesi, ocağa vurma, vagon). Sahnede bir tane olur.
// Kamera uzakta ve açısı değiştiği için sesler 2B çalınır; ses yüksekliği olayın ekrandaki görüş noktasına uzaklığıyla
// azalır. Aynı ses peş peşe tekdüze olmasın diye çeşitlerden rastgele biri ve hafif farklı perdeyle çalınır.
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
    [Tooltip("Bu uzaklığa (m, kameranın baktığı noktadan) kadar tam ses, sonra azalır.")]
    [SerializeField] float fullVolumeDistance = 35f;
    [Tooltip("Bu uzaklıkta ses tamamen kaybolur.")]
    [SerializeField] float silentDistance = 160f;
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

    // Olayın, kameranın baktığı yere (ekranın ortasındaki zemin) uzaklığına göre 1 → 0.
    public float Attenuation(Vector3 position)
    {
        if (cam == null)
            cam = Camera.main;
        if (cam == null)
            return 1f;
        var ray = new Ray(cam.transform.position, cam.transform.forward);
        var focus = new Plane(Vector3.up, position).Raycast(ray, out float enter) ? ray.GetPoint(enter) : cam.transform.position;
        var delta = position - focus;
        delta.y = 0f;
        // Uzaklaştırınca (kamera yüksekte) her şey biraz daha kısık duyulur.
        float zoom = Mathf.InverseLerp(220f, 40f, Vector3.Distance(cam.transform.position, focus));
        float distance = Mathf.InverseLerp(silentDistance, fullVolumeDistance, delta.magnitude);
        return distance * Mathf.Lerp(0.45f, 1f, zoom);
    }
}
