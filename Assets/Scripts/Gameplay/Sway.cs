using UnityEngine;

// Asılı objeleri (fener, kova) pivotu etrafında hafifçe sallar. Pivot asıldığı kancada olmalı.
public class Sway : MonoBehaviour
{
    [SerializeField] float angle = 4f;
    [SerializeField] float speed = 1.3f;

    Quaternion rest;
    float phase;

    void Awake()
    {
        rest = transform.localRotation;
        phase = Random.value * 100f; // hepsi aynı anda sallanmasın
    }

    void Update()
    {
        // İki eksende farklı hızla: düz bir sarkaç yerine rüzgârda hafifçe dönen bir salınım.
        float t = Time.time * speed + phase;
        transform.localRotation = rest * Quaternion.Euler(Mathf.Sin(t) * angle, 0f, Mathf.Sin(t * 0.73f + 1.3f) * angle * 0.6f);
    }
}
