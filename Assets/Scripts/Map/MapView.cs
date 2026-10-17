using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public class MapView : MonoBehaviour
{
    [SerializeField] World world;
    [SerializeField] Material material;
    [SerializeField] int chunkSize = 16;
    [SerializeField] Module[] modules;

    readonly Dictionary<int, List<Variant>> variants = new();
    readonly Dictionary<Vector2Int, Chunk> chunks = new();
    readonly HashSet<Vector2Int> dirtyChunks = new();
    CellDraw[] cells;
    GameObject defaultPrefab;
    bool filling;

    void Start()
    {
        if (world == null)
        {
            Debug.LogError("MapView needs a World.", this);
            return;
        }

        if (chunkSize < 1)
            chunkSize = 16;

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

        cells = new CellDraw[world.Width * world.Height];
        world.CellChanged += Refresh;
        filling = true;

        for (var y = 0; y < world.Height; y++)
        {
            for (var x = 0; x < world.Width; x++)
                Refresh(x, y);
        }

        filling = false;
        RebuildDirty();
    }

    void OnDestroy()
    {
        if (world != null)
            world.CellChanged -= Refresh;

        foreach (var chunk in chunks.Values)
        {
            if (chunk.Mesh != null)
                Destroy(chunk.Mesh);
        }
    }

    void Refresh(int x, int y)
    {
        var index = y * world.Width + x;
        world.Get(x, y, out var id, out var rotation);
        var prefab = Pick(id, x, y);
        if (prefab == null)
        {
            cells[index] = default;
        }
        else
        {
            var yaw = world.transform.rotation * Quaternion.Euler(0f, rotation * 90f, 0f);
            var half = world.CellSize * 0.5f;
            var origin = world.CellCenter(x, y) + yaw * new Vector3(-half, 0f, half);
            var worldMatrix = Matrix4x4.TRS(origin, yaw, prefab.transform.localScale);
            cells[index] = new CellDraw
            {
                Prefab = prefab,
                Local = transform.worldToLocalMatrix * worldMatrix
            };
        }

        dirtyChunks.Add(new Vector2Int(x / chunkSize, y / chunkSize));
        if (!filling)
            RebuildDirty();
    }

    void RebuildDirty()
    {
        foreach (var key in dirtyChunks)
            Rebuild(key);

        dirtyChunks.Clear();
    }

    void Rebuild(Vector2Int key)
    {
        if (!chunks.TryGetValue(key, out var chunk))
        {
            var chunkObject = new GameObject($"Chunk {key.x},{key.y}");
            chunkObject.transform.SetParent(transform, false);
            chunk = new Chunk
            {
                Filter = chunkObject.AddComponent<MeshFilter>(),
                Renderer = chunkObject.AddComponent<MeshRenderer>()
            };
            chunks.Add(key, chunk);
        }

        var combines = new List<CombineInstance>();
        Material chunkMaterial = material;
        var x0 = key.x * chunkSize;
        var y0 = key.y * chunkSize;
        var x1 = Mathf.Min(x0 + chunkSize, world.Width);
        var y1 = Mathf.Min(y0 + chunkSize, world.Height);

        for (var y = y0; y < y1; y++)
        {
            for (var x = x0; x < x1; x++)
            {
                var draw = cells[y * world.Width + x];
                if (draw.Prefab == null)
                    continue;

                foreach (var filter in draw.Prefab.GetComponentsInChildren<MeshFilter>())
                {
                    var source = filter.sharedMesh;
                    if (source == null)
                        continue;

                    if (!source.isReadable)
                    {
                        Debug.LogError($"'{source.name}' needs Read/Write enabled so chunks can be built.", source);
                        continue;
                    }

                    if (chunkMaterial == null)
                    {
                        var renderer = filter.GetComponent<MeshRenderer>();
                        if (renderer != null)
                            chunkMaterial = renderer.sharedMaterial;
                    }

                    combines.Add(new CombineInstance
                    {
                        mesh = source,
                        transform = draw.Local * LocalToRoot(filter.transform, draw.Prefab.transform)
                    });
                }
            }
        }

        if (chunk.Mesh != null)
            Destroy(chunk.Mesh);

        if (combines.Count == 0)
        {
            chunk.Mesh = null;
            chunk.Filter.sharedMesh = null;
            chunk.Renderer.enabled = false;
            return;
        }

        var mesh = new Mesh { indexFormat = IndexFormat.UInt32 };
        mesh.CombineMeshes(combines.ToArray(), true, true);
        chunk.Mesh = mesh;
        chunk.Filter.sharedMesh = mesh;
        chunk.Renderer.enabled = true;
        chunk.Renderer.sharedMaterial = chunkMaterial;
    }

    static Matrix4x4 LocalToRoot(Transform from, Transform root)
    {
        var matrix = Matrix4x4.identity;
        while (from != null && from != root)
        {
            matrix = Matrix4x4.TRS(from.localPosition, from.localRotation, from.localScale) * matrix;
            from = from.parent;
        }

        return matrix;
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

    struct CellDraw
    {
        public GameObject Prefab;
        public Matrix4x4 Local;
    }

    class Chunk
    {
        public MeshFilter Filter;
        public MeshRenderer Renderer;
        public Mesh Mesh;
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
