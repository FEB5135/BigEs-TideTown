using System;
using System.Collections.Generic;
using UnityEngine;

public class MapView : MonoBehaviour
{
    [SerializeField] World world;
    [SerializeField] Material material;
    [SerializeField] Module[] modules;

    readonly Dictionary<int, List<Variant>> variants = new();
    readonly Dictionary<Vector2Int, GameObject> instances = new();
    GameObject defaultPrefab;

    void Start()
    {
        if (world == null)
        {
            Debug.LogError("MapView needs a World.", this);
            return;
        }

        if (modules != null)
        {
            foreach (var module in modules)
            {
                if (module.Prefab == null)
                    continue;

                defaultPrefab ??= module.Prefab;
                if (!variants.TryGetValue(module.Id, out var list))
                {
                    list = new List<Variant>();
                    variants.Add(module.Id, list);
                }

                list.Add(new Variant
                {
                    Prefab = module.Prefab,
                    Weight = module.Weight > 0 ? module.Weight : 1
                });
            }
        }

        world.CellChanged += Refresh;

        for (var y = 0; y < world.Height; y++)
        {
            for (var x = 0; x < world.Width; x++)
                Refresh(x, y);
        }
    }

    void OnDestroy()
    {
        if (world != null)
            world.CellChanged -= Refresh;
    }

    void Refresh(int x, int y)
    {
        var key = new Vector2Int(x, y);
        if (instances.TryGetValue(key, out var existing))
        {
            instances.Remove(key);
            if (existing != null)
                Destroy(existing);
        }

        world.Get(x, y, out var id, out var rotation);

        var prefab = Pick(id, x, y);

        if (prefab == null)
            return;

        var yaw = world.transform.rotation * Quaternion.Euler(0f, rotation * 90f, 0f);
        var half = world.CellSize * 0.5f;
        var origin = world.CellCenter(x, y) + yaw * new Vector3(-half, 0f, half);
        var instance = Instantiate(prefab, origin, yaw, transform);
        if (material != null)
        {
            foreach (var renderer in instance.GetComponentsInChildren<Renderer>())
                renderer.sharedMaterial = material;
        }

        instances[key] = instance;
    }

    GameObject Pick(int id, int x, int y)
    {
        if (!variants.TryGetValue(id, out var list) || list.Count == 0)
            return defaultPrefab;

        var total = 0;
        foreach (var variant in list)
            total += variant.Weight;

        var roll = new System.Random(x * 73856093 ^ y * 19349663 ^ id * 83492791).Next(total);
        foreach (var variant in list)
        {
            roll -= variant.Weight;
            if (roll < 0)
                return variant.Prefab;
        }

        return list[list.Count - 1].Prefab;
    }

    struct Variant
    {
        public GameObject Prefab;
        public int Weight;
    }

    [Serializable]
    public struct Module
    {
        public int Id;
        public GameObject Prefab;
        [Tooltip("How often this prefab is chosen among entries with the same id. 0 counts as 1.")]
        public int Weight;
    }
}
