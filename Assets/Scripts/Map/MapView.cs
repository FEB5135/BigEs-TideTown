using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

[DefaultExecutionOrder(-25)]
public class MapView : MonoBehaviour
{
    [SerializeField] World world;
    [SerializeField] Material material;
    [SerializeField] int chunkSize = 16;
    [SerializeField] bool createColliders;
    [SerializeField] Module[] modules;
    [SerializeField, Tooltip("Sunken models drawn in place of a house's tiles once the tsunami destroys it.")]
    Ruin[] ruins;

    readonly Dictionary<int, List<Variant>> variants = new();
    readonly Dictionary<Vector2Int, Chunk> chunks = new();
    readonly HashSet<Vector2Int> dirtyChunks = new();
    CellDraw[] cells;
    GameObject defaultPrefab;
    bool filling;
    readonly Dictionary<Mesh, Mesh> treeGroundMeshes = new();
    readonly Dictionary<GameObject, GameObject> ruinOf = new();
    readonly HashSet<int> ruinedCells = new();
    HouseCollapse collapse;

    public IEnumerable<Collider> ConstructionSurfaces
    {
        get { foreach (var chunk in chunks.Values) if (chunk.GroundCollider != null) yield return chunk.GroundCollider; }
    }

    public bool OwnsSurface(Collider collider)
    {
        if (collider.gameObject == gameObject) return true;
        foreach (var chunk in chunks.Values)
            if (chunk.Collider == collider || chunk.GroundCollider == collider) return true;
        return false;
    }

    static bool IsLandscape(string name) => name.StartsWith("ground_") || name.StartsWith("slope_") ||
        name.StartsWith("cornerslope_") || name.StartsWith("innercornerslope_") ||
        name.StartsWith("tree_") || name == "sand" || name.StartsWith("sand_");

    public bool IsConstructionGround(Vector3 position)
    {
        if (world == null || modules == null) return false;
        var local = world.transform.InverseTransformPoint(position);
        int x = Mathf.FloorToInt(local.x / world.CellSize), z = Mathf.FloorToInt(local.z / world.CellSize);
        if (!world.InBounds(x, z)) return false;
        world.Get(x, z, out var id, out _);
        bool found = false;
        foreach (var module in modules)
        {
            if (module.Id != id || module.Prefab == null) continue;
            found = true;
            string name = module.Prefab.name;
            if (!IsLandscape(name)) return false;
        }
        return found;
    }

    public bool IsConstructionGroundPath(Vector3 a, Vector3 b, float padding)
    {
        if (world == null) return false;
        var across = Vector3.Cross(world.transform.up, b - a).normalized * padding;
        int samples = Mathf.Max(2, Mathf.CeilToInt(Vector3.Distance(a, b) / (world.CellSize * .2f)));
        // Vegetation is allowed; a neighbouring building tile must still block.
        for (int i = 0; i <= samples; i++)
        {
            var point = Vector3.Lerp(a, b, Mathf.Lerp(.001f, .999f, i / (float)samples));
            if (!IsConstructionGround(point + across) || !IsConstructionGround(point - across)) return false;
        }
        return true;
    }

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

        if (ruins != null)
            foreach (var ruin in ruins)
                if (ruin.Intact != null && ruin.Ruined != null)
                    ruinOf[ruin.Intact] = ruin.Ruined;

        cells = new CellDraw[world.Width * world.Height];
        world.CellChanged += Refresh;
        collapse = new GameObject("House Collapse").AddComponent<HouseCollapse>();
        collapse.transform.SetParent(transform, false);
        collapse.Init(material);
        Houses.Destroyed += OnHouseDestroyed;
        Houses.Repaired += OnHouseRepaired;
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
        Houses.Destroyed -= OnHouseDestroyed;
        Houses.Repaired -= OnHouseRepaired;

        foreach (var chunk in chunks.Values)
        {
            if (chunk.Mesh != null)
                ReleaseMesh(chunk.Mesh);
            if (chunk.GroundMesh != null) ReleaseMesh(chunk.GroundMesh);
        }
        foreach (var mesh in treeGroundMeshes.Values) if (mesh != null) ReleaseMesh(mesh);
    }

    void OnHouseDestroyed(Houses.House house)
    {
        Collapse(house);
        SetRuined(house, true);
    }
    void OnHouseRepaired(Houses.House house) => SetRuined(house, false);

    // Breaks the house's standing tiles apart, each exactly where and how it is drawn (position and rotation).
    void Collapse(Houses.House house)
    {
        if (cells == null || collapse == null)
            return;
        var origin = Vector3.zero;
        var count = 0;
        foreach (var home in house.Building)
            foreach (var centre in home.CellCenters)
            {
                origin += centre;
                count++;
            }
        origin = count > 0 ? origin / count : house.Center;

        foreach (var cell in house.Cells)
        {
            if (!world.InBounds(cell.x, cell.y) || ruinedCells.Contains(cell.y * world.Width + cell.x))
                continue;
            var draw = cells[cell.y * world.Width + cell.x];
            if (draw.Prefab == null)
                continue;
            foreach (var filter in draw.Prefab.GetComponentsInChildren<MeshFilter>())
                if (filter.sharedMesh != null && filter.sharedMesh.isReadable)
                    collapse.Break(filter.sharedMesh,
                        transform.localToWorldMatrix * draw.Local * LocalToRoot(filter.transform, draw.Prefab.transform),
                        origin, house.Impact);
        }
    }

    // A house's tiles switch to (or back from) their sunken models; its chunks are rebuilt once.
    void SetRuined(Houses.House house, bool ruined)
    {
        if (cells == null)
            return;
        filling = true;
        foreach (var cell in house.Cells)
        {
            if (!world.InBounds(cell.x, cell.y))
                continue;
            var index = cell.y * world.Width + cell.x;
            if (ruined ? ruinedCells.Add(index) : ruinedCells.Remove(index))
                Refresh(cell.x, cell.y);
        }
        filling = false;
        RebuildDirty();
    }

    void Refresh(int x, int y)
    {
        var index = y * world.Width + x;
        world.Get(x, y, out var id, out var rotation);
        var prefab = Pick(id, x, y);
        if (prefab != null && ruinedCells.Contains(index) && ruinOf.TryGetValue(prefab, out var ruin))
            prefab = ruin;
        if (prefab == null)
        {
            cells[index] = default;
        }
        else
        {
            var yaw = world.transform.rotation * Quaternion.Euler(0f, rotation * 90f, 0f);
            var half = world.CellSize * 0.5f;
            var origin = world.CellCenter(x, y) + yaw * new Vector3(-half, 0f, half);
            var worldMatrix = Matrix4x4.TRS(origin, yaw, prefab.transform.localScale * world.CellSize);
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
            // The visual chunk also contains roofs and tree canopies. Picking and height sampling
            // need a separate mesh containing only the actual landscape, even without solid colliders.
            var groundObject = new GameObject("Construction terrain");
            groundObject.transform.SetParent(chunkObject.transform, false);
            chunk.GroundCollider = groundObject.AddComponent<MeshCollider>();
            groundObject.layer = 2; // Ignore Raycast: ordinary picking keeps using the original chunk.
            if (createColliders)
                chunk.Collider = chunkObject.AddComponent<MeshCollider>();
            chunks.Add(key, chunk);
        }

        var combines = new List<CombineInstance>();
        var groundCombines = new List<CombineInstance>();
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
                    if (IsLandscape(draw.Prefab.name))
                    {
                        var landscape = draw.Prefab.name.StartsWith("tree_") ? TreeGround(source) : source;
                        if (landscape != null)
                            for (int submesh = 0; submesh < landscape.subMeshCount; submesh++)
                            groundCombines.Add(new CombineInstance
                            {
                                mesh = landscape,
                                subMeshIndex = submesh,
                                transform = draw.Local * LocalToRoot(filter.transform, draw.Prefab.transform)
                            });
                    }
                }
            }
        }

        if (chunk.Collider != null)
            chunk.Collider.sharedMesh = null;
        if (chunk.Mesh != null)
            ReleaseMesh(chunk.Mesh);
        chunk.GroundCollider.sharedMesh = null;
        if (chunk.GroundMesh != null) ReleaseMesh(chunk.GroundMesh);
        chunk.GroundMesh = null;
        if (groundCombines.Count > 0)
        {
            chunk.GroundMesh = new Mesh { name = "Construction landscape", indexFormat = IndexFormat.UInt32 };
            chunk.GroundMesh.CombineMeshes(groundCombines.ToArray(), true, true);
            chunk.GroundCollider.sharedMesh = chunk.GroundMesh;
        }

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
        if (chunk.Collider != null)
            chunk.Collider.sharedMesh = mesh;
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

    Mesh TreeGround(Mesh source)
    {
        if (treeGroundMeshes.TryGetValue(source, out var cached)) return cached;
        // Team tree tiles contain a ground plate at local y=0, plus trunks and crowns.
        // Retain only plate triangles so a wall never climbs to a treetop.
        var vertices = source.vertices;
        var triangles = new List<int>();
        var indices = source.triangles;
        for (int i = 0; i < indices.Length; i += 3)
            if (Mathf.Abs(vertices[indices[i]].y) < .001f &&
                Mathf.Abs(vertices[indices[i + 1]].y) < .001f &&
                Mathf.Abs(vertices[indices[i + 2]].y) < .001f)
            { triangles.Add(indices[i]); triangles.Add(indices[i + 1]); triangles.Add(indices[i + 2]); }
        Mesh mesh = null;
        if (triangles.Count > 0)
        {
            mesh = new Mesh { name = source.name + " ground", indexFormat = source.indexFormat };
            mesh.vertices = vertices; mesh.triangles = triangles.ToArray(); mesh.RecalculateBounds();
        }
        treeGroundMeshes.Add(source, mesh);
        return mesh;
    }

    static void ReleaseMesh(Mesh mesh)
    {
        if (Application.isPlaying) Destroy(mesh); else DestroyImmediate(mesh);
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
        public MeshCollider Collider;
        public Mesh GroundMesh;
        public MeshCollider GroundCollider;
    }

    [Serializable]
    public struct Ruin
    {
        public GameObject Intact;
        public GameObject Ruined;
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
