using UnityEngine;

// Kuyunun içindeki boşluğa konan tetikleyici. İçine düşen kaynak depoya eklenir.
[RequireComponent(typeof(Collider))]
public class WellIntake : MonoBehaviour
{
    void Reset()
    {
        GetComponent<Collider>().isTrigger = true;
    }

    void OnTriggerEnter(Collider other)
    {
        var pickup = other.GetComponentInParent<ResourcePickup>();
        if (pickup != null)
            pickup.Collect();
    }
}
