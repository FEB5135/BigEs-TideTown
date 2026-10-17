using UnityEngine;

// Araç ilerledikçe tekerlekleri yola göre döndürür (vagon). Tekerleklerin pivotu aksta, dönme ekseni yerel X olmalı.
public class RollingWheels : MonoBehaviour
{
    [SerializeField] Transform[] wheels;
    [SerializeField] float radius = 0.24f;

    Vector3 lastPosition;

    void Start() => lastPosition = transform.position;

    void LateUpdate()
    {
        float travelled = Vector3.Dot(transform.position - lastPosition, transform.forward);
        lastPosition = transform.position;
        if (Mathf.Abs(travelled) < 0.0001f)
            return;
        float degrees = travelled / radius * Mathf.Rad2Deg;
        foreach (var wheel in wheels)
            if (wheel != null)
                wheel.localRotation *= Quaternion.Euler(degrees, 0f, 0f);
    }
}
