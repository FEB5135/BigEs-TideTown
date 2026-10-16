using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Köyün ortasındaki kuyu: toplanan kaynaklar buraya atılır ve buradan harcanır.
public class Well : MonoBehaviour
{
    public static Well Instance { get; private set; }

    [Tooltip("Kuyunun ağzının ortası. Kaynaklar bunun tam üstünden bırakılır. Boşsa kuyunun kendi pozisyonu kullanılır.")]
    [SerializeField] Transform dropPoint;
    [Tooltip("Kaynağın kuyunun ne kadar üstünden serbest düşüşe bırakılacağı.")]
    [SerializeField] float releaseHeight = 3.5f;
    [Tooltip("Atış kavisinin, başlangıç ile bırakma noktası arasındaki en yüksek ek yüksekliği.")]
    [SerializeField] float arcHeight = 3f;
    [SerializeField] float minFlightTime = 0.45f;
    [SerializeField] float flightTimePerMeter = 0.025f;
    [Tooltip("Kaynak kuyuya hiç düşmezse bu kadar saniye sonra yine de depoya eklenir.")]
    [SerializeField] float pickupTimeout = 6f;
    [Tooltip("ResourceType sırasıyla (Wood, Stone) atış görselleri. Boşsa basit bir kütük/taş oluşturulur.")]
    [SerializeField] GameObject[] throwPrefabs;

    [Tooltip("ResourceType sırasıyla (Wood, Stone) mevcut miktarlar.")]
    [SerializeField] int[] amounts = new int[ResourceTypeCount];

    static readonly int ResourceTypeCount = Enum.GetValues(typeof(ResourceType)).Length;

    // (tür, yeni miktar)
    public event Action<ResourceType, int> Changed;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("Sahnede birden fazla Well var, fazlası yok sayılıyor.", this);
            return;
        }
        Instance = this;
        if (amounts == null || amounts.Length != ResourceTypeCount)
            Array.Resize(ref amounts, ResourceTypeCount);
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    public int Get(ResourceType type) => amounts[(int)type];

    public void Deposit(ResourceType type, int amount)
    {
        if (amount <= 0)
            return;
        amounts[(int)type] += amount;
        Changed?.Invoke(type, amounts[(int)type]);
    }

    public bool CanAfford(IReadOnlyList<ResourceCost> costs)
    {
        // Aynı tür listede birden fazla geçebilir, önce topla.
        var needed = new int[ResourceTypeCount];
        foreach (var cost in costs)
            needed[(int)cost.type] += cost.amount;
        for (int i = 0; i < ResourceTypeCount; i++)
            if (needed[i] > amounts[i])
                return false;
        return true;
    }

    // Ya tamamını harcar ya da hiçbir şey harcamaz.
    public bool TrySpend(IReadOnlyList<ResourceCost> costs)
    {
        if (!CanAfford(costs))
            return false;
        foreach (var cost in costs)
        {
            if (cost.amount <= 0)
                continue;
            amounts[(int)cost.type] -= cost.amount;
            Changed?.Invoke(cost.type, amounts[(int)cost.type]);
        }
        return true;
    }

    public bool TrySpend(ResourceType type, int amount) => TrySpend(new[] { new ResourceCost(type, amount) });

    // Kaynağı "from" noktasından kavisle kuyunun üstüne uçurur, oradan kuyunun boşluğuna düşürür.
    // Miktar, parça kuyunun içindeki WellIntake tetikleyicisine girince artar.
    public void Throw(ResourceType type, int amount, Vector3 from)
    {
        StartCoroutine(ThrowRoutine(type, amount, from));
    }

    IEnumerator ThrowRoutine(ResourceType type, int amount, Vector3 from)
    {
        var piece = CreatePiece(type);
        piece.transform.position = from;
        var body = piece.GetComponent<Rigidbody>();
        body.isKinematic = true;
        piece.GetComponent<ResourcePickup>().Init(this, type, amount, pickupTimeout);

        var mouth = dropPoint != null ? dropPoint.position : transform.position;
        var release = mouth + Vector3.up * releaseHeight;
        float duration = minFlightTime + Vector3.Distance(from, release) * flightTimePerMeter;
        var spin = UnityEngine.Random.onUnitSphere * 540f;

        for (float t = 0f; t < 1f; t += Time.deltaTime / duration)
        {
            if (piece == null)
                yield break;
            var pos = Vector3.Lerp(from, release, t);
            pos.y += Mathf.Sin(t * Mathf.PI) * arcHeight;
            piece.transform.position = pos;
            piece.transform.Rotate(spin * Time.deltaTime, Space.World);
            yield return null;
        }
        if (piece == null)
            yield break;

        // Tepeden serbest düşüş: fizik devralır, kütük dönerek boşluğa düşer.
        piece.transform.position = release;
        body.isKinematic = false;
        body.linearVelocity = Vector3.down * 2f;
        body.angularVelocity = UnityEngine.Random.insideUnitSphere * 6f;
    }

    GameObject CreatePiece(ResourceType type)
    {
        int index = (int)type;
        GameObject piece;
        if (throwPrefabs != null && index < throwPrefabs.Length && throwPrefabs[index] != null)
        {
            piece = Instantiate(throwPrefabs[index]);
            if (piece.GetComponentInChildren<Collider>() == null)
                piece.AddComponent<SphereCollider>().radius = 0.3f;
        }
        else
        {
            piece = type == ResourceType.Wood ? CreateLog() : CreateStone();
        }

        if (!piece.TryGetComponent<Rigidbody>(out var body))
            body = piece.AddComponent<Rigidbody>();
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        if (!piece.TryGetComponent<ResourcePickup>(out _))
            piece.AddComponent<ResourcePickup>();
        return piece;
    }

    // Model gelene kadar kullanılan basit kütük: gövde + açık renkli kesik uçlar.
    static GameObject CreateLog()
    {
        var log = new GameObject("Log");
        var capsule = log.AddComponent<CapsuleCollider>();
        capsule.direction = 0; // X ekseni boyunca
        capsule.radius = 0.18f;
        capsule.height = 1.1f;

        var bark = Visual(PrimitiveType.Cylinder, log.transform, new Color(0.42f, 0.26f, 0.12f));
        bark.localRotation = Quaternion.Euler(0f, 0f, 90f);
        bark.localScale = new Vector3(0.36f, 0.55f, 0.36f);
        foreach (float side in new[] { -1f, 1f })
        {
            var cut = Visual(PrimitiveType.Cylinder, log.transform, new Color(0.86f, 0.66f, 0.42f));
            cut.localPosition = new Vector3(0.551f * side, 0f, 0f);
            cut.localRotation = Quaternion.Euler(0f, 0f, 90f);
            cut.localScale = new Vector3(0.3f, 0.005f, 0.3f);
        }
        return log;
    }

    static GameObject CreateStone()
    {
        var stone = new GameObject("Stone");
        stone.AddComponent<SphereCollider>().radius = 0.22f;
        var rock = Visual(PrimitiveType.Sphere, stone.transform, new Color(0.55f, 0.56f, 0.6f));
        rock.localScale = new Vector3(0.5f, 0.38f, 0.44f);
        return stone;
    }

    static Transform Visual(PrimitiveType type, Transform parent, Color color)
    {
        var go = GameObject.CreatePrimitive(type);
        Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        var block = new MaterialPropertyBlock();
        block.SetColor("_BaseColor", color);
        go.GetComponent<Renderer>().SetPropertyBlock(block);
        return go.transform;
    }
}
