using System;
using System.Collections.Generic;
using UnityEngine;

public class MapView : MonoBehaviour
{
    [SerializeField] World world;
    [SerializeField] Material material;
    [SerializeField] Module[] modules;

    readonly Dictionary<int, GameObject> prefabs = new();
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
                prefabs[module.Id] = module.Prefab;
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

        if (!prefabs.TryGetValue(id, out var prefab))
            prefab = defaultPrefab;

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

    [Serializable]
    public struct Module
    {
        public int Id;
        public GameObject Prefab;
    }
}
