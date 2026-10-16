using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Köyün ortasındaki kuyu: toplanan kaynaklar buraya atılır ve buradan harcanır.
public class Well : MonoBehaviour
{
    public static Well Instance { get; private set; }

    [Tooltip("Kaynakların düştüğü nokta. Boşsa kuyunun kendi pozisyonu kullanılır.")]
    [SerializeField] Transform dropPoint;
    [SerializeField] float throwDuration = 0.6f;
    [SerializeField] float throwHeight = 2f;
    [Tooltip("ResourceType sırasıyla (Wood, Stone) atış görselleri. Boşsa küçük bir küp kullanılır.")]
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

    // Kaynağı "from" noktasından kuyuya fırlatır; kuyuya düşünce depoya eklenir.
    public void Throw(ResourceType type, int amount, Vector3 from)
    {
        StartCoroutine(ThrowRoutine(type, amount, from));
    }

    IEnumerator ThrowRoutine(ResourceType type, int amount, Vector3 from)
    {
        var visual = CreateThrowVisual(type);
        var target = dropPoint != null ? dropPoint.position : transform.position;

        for (float t = 0f; t < 1f; t += Time.deltaTime / throwDuration)
        {
            var pos = Vector3.Lerp(from, target, t);
            pos.y += Mathf.Sin(t * Mathf.PI) * throwHeight;
            visual.transform.position = pos;
            yield return null;
        }

        Destroy(visual);
        Deposit(type, amount);
    }

    GameObject CreateThrowVisual(ResourceType type)
    {
        int index = (int)type;
        if (throwPrefabs != null && index < throwPrefabs.Length && throwPrefabs[index] != null)
            return Instantiate(throwPrefabs[index]);

        var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Destroy(cube.GetComponent<Collider>());
        cube.transform.localScale = Vector3.one * 0.3f;
        return cube;
    }
}
