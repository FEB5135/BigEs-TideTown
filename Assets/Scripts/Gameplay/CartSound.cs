using UnityEngine;

// Vagonun ray sesi: hareket ederken yuvarlanma sesi hızla birlikte açılır, kalkışta ve duruşta metal tıkırtısı.
// GathererSpawner ürettiği vagona ekler.
public class CartSound : MonoBehaviour
{
    [Tooltip("Bu hızda (m/sn) yuvarlanma sesi tam düzeyde.")]
    [SerializeField] float fullSpeed = 4f;
    [SerializeField, Range(0f, 1f)] float rollVolume = 0.55f;

    AudioSource roll;
    Vector3 previous;
    float speed;
    bool moving;

    void Start()
    {
        previous = transform.position;
        var audio = GameAudio.Instance;
        if (audio == null || audio.cartRoll == null)
            return;
        roll = gameObject.AddComponent<AudioSource>();
        roll.clip = audio.cartRoll;
        roll.loop = true;
        roll.playOnAwake = false;
        roll.spatialBlend = 0f;
        roll.volume = 0f;
        // Vagonlar aynı anda başlayınca üst üste binmesin.
        roll.time = Random.Range(0f, audio.cartRoll.length);
        roll.Play();
    }

    void Update()
    {
        var delta = transform.position - previous;
        previous = transform.position;
        delta.y = 0f;
        float current = Time.deltaTime > 0f ? delta.magnitude / Time.deltaTime : 0f;
        speed = Mathf.Lerp(speed, current, 1f - Mathf.Exp(-8f * Time.deltaTime));

        bool nowMoving = speed > 0.4f;
        if (nowMoving != moving)
        {
            moving = nowMoving;
            GameAudio.Play(GameAudio.Instance != null ? GameAudio.Instance.cartClank : null, transform.position, 0.6f);
        }

        if (roll == null)
            return;
        var audio = GameAudio.Instance;
        float level = audio != null ? audio.volume * audio.Attenuation(transform.position) : 1f;
        float k = Mathf.Clamp01(speed / fullSpeed);
        roll.volume = rollVolume * k * level;
        roll.pitch = Mathf.Lerp(0.8f, 1.1f, k);
    }
}
