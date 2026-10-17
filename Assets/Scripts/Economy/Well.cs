using System;
using System.Collections;
using System.Collections.Generic;
using IdleTown.Construction;
using UnityEngine;

// Köyün ortasındaki kuyu: toplanan kaynaklar buraya atılır ve buradan harcanır.
public class Well : MonoBehaviour, IWallResourceStore
{
    public static Well Instance { get; private set; }

    [Tooltip("Kuyunun ağzının ortası (duvarın üst kenarı hizasında). Boşsa kuyunun kendi pozisyonu kullanılır.")]
    [SerializeField] Transform dropPoint;
    [Tooltip("Kuyunun iç duvar yarıçapı. Atışın duvarın üstünden geçmesi bu değere göre hesaplanır.")]
    [SerializeField] float innerRadius = 1.4f;
    [Tooltip("Köylülerin kaynağı atmak için kuyunun merkezine en fazla ne kadar yaklaşacağı (duvarın dışı).")]
    [SerializeField] float approachRadius = 3f;

    [Header("Eğik atış")]
    [Tooltip("Atışın hedeflediği nokta, kuyu ağzının bu kadar altında.")]
    [SerializeField] float targetDepth = 1.2f;
    [Tooltip("Yörüngenin tepe noktası, başlangıç ile kuyu ağzının yüksek olanının bu kadar üstünde.")]
    [SerializeField] float arcHeight = 3f;
    [Tooltip("Yörüngenin kuyu duvarının üstünden en az ne kadar yüksekten geçeceği.")]
    [SerializeField] float rimClearance = 0.6f;
    [Tooltip("Uçarken dönme hızı (derece/sn).")]
    [SerializeField] float spinSpeed = 420f;
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
        NormalizeAmounts();
    }

    void OnValidate() => NormalizeAmounts();

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    public float ApproachRadius => approachRadius;
    public int Wood => Get(ResourceType.Wood);
    public int Stone => Get(ResourceType.Stone);

    public int Get(ResourceType type)
    {
        NormalizeAmounts();
        var index = (int)type;
        return (uint)index < (uint)ResourceTypeCount ? amounts[index] : 0;
    }

    public void Deposit(ResourceType type, int amount)
    {
        NormalizeAmounts();
        var index = (int)type;
        if ((uint)index >= (uint)ResourceTypeCount || amount <= 0)
            return;
        amounts[index] = (int)Math.Min(int.MaxValue, (long)amounts[index] + amount);
        NotifyChanged(type);
    }

    // Tüm kaynakları sıfırlar (ör. oyun menüden başlarken).
    public void ResetAmounts()
    {
        for (int i = 0; i < amounts.Length; i++)
        {
            if (amounts[i] == 0)
                continue;
            amounts[i] = 0;
            Changed?.Invoke((ResourceType)i, 0);
        }
    }

    public bool CanAfford(IReadOnlyList<ResourceCost> costs)
    {
        NormalizeAmounts();
        if (costs == null)
            return false;

        // Aynı tür listede birden fazla geçebilir, önce topla.
        var needed = new long[ResourceTypeCount];
        foreach (var cost in costs)
        {
            if ((uint)cost.type >= (uint)ResourceTypeCount || cost.amount < 0)
                return false;
            needed[(int)cost.type] += cost.amount;
        }
        for (int i = 0; i < ResourceTypeCount; i++)
            if (needed[i] > amounts[i])
                return false;
        return true;
    }

    // Ya tamamını harcar ya da hiçbir şey harcamaz.
    public bool TrySpend(IReadOnlyList<ResourceCost> costs)
    {
        NormalizeAmounts();
        if (!CanAfford(costs))
            return false;

        var spent = new int[ResourceTypeCount];
        foreach (var cost in costs)
        {
            spent[(int)cost.type] += cost.amount;
        }

        // Önce tüm bakiyeler commit edilir. Dinleyiciler daha sonra ve birbirinden izole çağrılır;
        // hatalı bir HUD/observer, tamamlanmış harcamayı kısmi veya başarısız gösteremez.
        for (int i = 0; i < ResourceTypeCount; i++)
            amounts[i] -= spent[i];
        for (int i = 0; i < ResourceTypeCount; i++)
            if (spent[i] > 0)
                NotifyChanged((ResourceType)i);
        return true;
    }

    public bool TrySpend(ResourceType type, int amount) => TrySpend(new[] { new ResourceCost(type, amount) });

    public bool CanAfford(int wood, int stone)
    {
        return wood >= 0 && stone >= 0 && Wood >= wood && Stone >= stone;
    }

    public bool TrySpend(int wood, int stone)
    {
        if (wood < 0 || stone < 0)
            return false;
        return TrySpend(new[]
        {
            new ResourceCost(ResourceType.Wood, wood),
            new ResourceCost(ResourceType.Stone, stone),
        });
    }

    void NormalizeAmounts()
    {
        if (amounts == null || amounts.Length != ResourceTypeCount)
            Array.Resize(ref amounts, ResourceTypeCount);
        for (int i = 0; i < amounts.Length; i++)
            amounts[i] = Mathf.Max(0, amounts[i]);
    }

    void NotifyChanged(ResourceType type)
    {
        var subscribers = Changed?.GetInvocationList();
        if (subscribers == null)
            return;

        var amount = amounts[(int)type];
        for (int i = 0; i < subscribers.Length; i++)
        {
            try
            {
                ((Action<ResourceType, int>)subscribers[i])(type, amount);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
            }
        }
    }

    // Kaynağı "from" noktasından eğik atışla kuyuya fırlatır. Yörünge baştan hesaplanır
    // (tek bir parabol), parça duvarın üstünden geçip kuyunun içine iner.
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
        var target = mouth + Vector3.down * targetDepth;
        var gravity = Physics.gravity;
        var velocity = SolveLaunchVelocity(from, target, mouth.y, out float flightTime);
        var spin = UnityEngine.Random.onUnitSphere * spinSpeed;

        // p(t) = p0 + v0·t + ½·g·t²  — fizik adımlarıyla aynı anda ilerler.
        for (float t = 0f; t < flightTime;)
        {
            yield return new WaitForFixedUpdate();
            if (piece == null)
                yield break;
            t = Mathf.Min(t + Time.fixedDeltaTime, flightTime);
            body.MovePosition(from + velocity * t + 0.5f * t * t * gravity);
            body.MoveRotation(Quaternion.Euler(spin * Time.fixedDeltaTime) * body.rotation);
        }
        if (piece == null)
            yield break;

        // Hedefe varınca fizik devralır, aynı hızla devam edip kuyunun dibine düşer.
        body.isKinematic = false;
        body.linearVelocity = velocity + gravity * flightTime;
        body.angularVelocity = spin * Mathf.Deg2Rad;
    }

    // Başlangıçtan hedefe giden parabolün ilk hızını bulur.
    // Tepe yüksekliği H seçilir: yukarı çıkış t1 = √(2(H−y0)/g), iniş t2 = √(2(H−yh)/g), T = t1 + t2.
    // Dikey hız vy = g·t1, yatay hız = yatay mesafe / T. Yörünge duvarın üstünden geçmezse H artırılır.
    Vector3 SolveLaunchVelocity(Vector3 from, Vector3 target, float rimY, out float flightTime)
    {
        float g = Mathf.Max(0.01f, -Physics.gravity.y);
        var horizontal = new Vector3(target.x - from.x, 0f, target.z - from.z);
        float distance = horizontal.magnitude;
        float apex = Mathf.Max(from.y, rimY) + arcHeight;

        Vector3 velocity = Vector3.zero;
        flightTime = 0f;
        for (int attempt = 0; attempt < 12; attempt++, apex += 1f)
        {
            float tUp = Mathf.Sqrt(2f * (apex - from.y) / g);
            float tDown = Mathf.Sqrt(2f * (apex - target.y) / g);
            flightTime = tUp + tDown;
            float horizontalSpeed = distance / flightTime;
            velocity = (distance > 0.0001f ? horizontal / distance * horizontalSpeed : Vector3.zero) + Vector3.up * g * tUp;

            // Duvarı geçtiği an: hedefe yatayda innerRadius kaldığında.
            if (distance <= innerRadius || horizontalSpeed <= 0.0001f)
                break;
            float tRim = (distance - innerRadius) / horizontalSpeed;
            float heightAtRim = from.y + velocity.y * tRim - 0.5f * g * tRim * tRim;
            if (heightAtRim >= rimY + rimClearance)
                break;
        }
        return velocity;
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
