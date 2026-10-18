using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
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
    [SerializeField, Tooltip("Chunks rebuilt per frame for changes that may wait (fallen houses). Changes to the World's " +
        "cells (felled trees...) are always rebuilt in the same frame.")]
    int rebuildsPerFrame = 2;

    readonly Dictionary<int, List<Variant>> variants = new();
    readonly Dictionary<Vector2Int, Chunk> chunks = new();
    readonly HashSet<Vector2Int> dirtyChunks = new();
    // Chunks whose rebuild can wait a few frames, oldest first (fallen houses: the break-apart effect covers them).
    readonly List<Vector2Int> lazyChunks = new();
    readonly HashSet<Vector2Int> groundDirty = new();
    // Mesh colliders are cooked on worker threads (Physics.BakeMesh) and swapped in when ready.
    readonly List<(MeshCollider collider, Mesh mesh)> baking = new();
    readonly List<Mesh> releaseAfterBake = new();
    JobHandle bakeHandle;
    NativeArray<EntityId> bakeIds;
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
        // The first map's colliders are needed right away (navigation, construction); cook them all in parallel.
        StartBake();
        FinishBake();
    }

    // Changes made during the frame are rebuilt once per chunk here, before the frame is drawn.
    void LateUpdate()
    {
        if (cells == null)
            return;
        if (baking.Count > 0 && bakeHandle.IsCompleted)
            FinishBake();
        RebuildDirty();
        for (int i = 0; i < rebuildsPerFrame && lazyChunks.Count > 0; i++)
        {
            Rebuild(lazyChunks[0]);
            lazyChunks.RemoveAt(0);
        }
        if (baking.Count == 0)
            StartBake();
    }

    void OnDestroy()
    {
        if (world != null)
            world.CellChanged -= Refresh;
        Houses.Destroyed -= OnHouseDestroyed;
        Houses.Repaired -= OnHouseRepaired;
        bakeHandle.Complete();
        if (bakeIds.IsCreated)
            bakeIds.Dispose();

        var meshes = new HashSet<Mesh>(releaseAfterBake);
        foreach (var chunk in chunks.Values)
        {
            meshes.Add(chunk.Mesh);
            meshes.Add(chunk.GroundMesh);
            if (chunk.Collider != null) meshes.Add(chunk.Collider.sharedMesh);
            if (chunk.GroundCollider != null) meshes.Add(chunk.GroundCollider.sharedMesh);
        }
        foreach (var (_, mesh) in baking)
            meshes.Add(mesh);
        foreach (var mesh in meshes)
            if (mesh != null)
                ReleaseMesh(mesh);
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

    // A house's tiles switch to (or back from) their sunken models. Many houses fall at once; their chunks are
    // rebuilt a few per frame, each once, while the break-apart effect plays over them.
    void SetRuined(Houses.House house, bool ruined)
    {
        if (cells == null)
            return;
        foreach (var cell in house.Cells)
        {
            if (!world.InBounds(cell.x, cell.y))
                continue;
            var index = cell.y * world.Width + cell.x;
            if (ruined ? ruinedCells.Add(index) : ruinedCells.Remove(index))
            {
                SetDraw(cell.x, cell.y);
                var key = new Vector2Int(cell.x / chunkSize, cell.y / chunkSize);
                if (!dirtyChunks.Contains(key) && !lazyChunks.Contains(key))
                    lazyChunks.Add(key);
            }
        }
    }

    void Refresh(int x, int y)
    {
        var key = new Vector2Int(x / chunkSize, y / chunkSize);
        dirtyChunks.Add(key);
        lazyChunks.Remove(key);
        SetDraw(x, y);
    }

    // Works out what the cell draws. A change to or from a landscape tile also changes the construction ground.
    void SetDraw(int x, int y)
    {
        var index = y * world.Width + x;
        var before = cells[index].Prefab;
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

        if ((before != null && IsLandscape(before.name)) || (prefab != null && IsLandscape(prefab.name)))
            groundDirty.Add(new Vector2Int(x / chunkSize, y / chunkSize));
    }

    void RebuildDirty()
    {
        if (filling)
            return;
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

        bool ground = groundDirty.Remove(key);
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
                    if (ground && IsLandscape(draw.Prefab.name))
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

        // Each collider keeps its old mesh until the new one is cooked (see StartBake/FinishBake).
        if (ground)
        {
            Mesh groundMesh = null;
            if (groundCombines.Count > 0)
            {
                groundMesh = new Mesh { name = "Construction landscape", indexFormat = IndexFormat.UInt32 };
                groundMesh.CombineMeshes(groundCombines.ToArray(), true, true);
            }
            Replace(chunk, ref chunk.GroundMesh, groundMesh);
        }

        Mesh mesh = null;
        if (combines.Count > 0)
        {
            mesh = new Mesh { indexFormat = IndexFormat.UInt32 };
            mesh.CombineMeshes(combines.ToArray(), true, true);
        }
        Replace(chunk, ref chunk.Mesh, mesh);
        chunk.Filter.sharedMesh = mesh;
        chunk.Renderer.enabled = mesh != null;
        if (mesh != null)
            chunk.Renderer.sharedMaterial = chunkMaterial;
    }

    void Replace(Chunk chunk, ref Mesh slot, Mesh mesh)
    {
        var old = slot;
        slot = mesh;
        Release(chunk, old);
    }

    // A mesh is destroyed once no renderer, collider or bake job of the chunk uses it any more.
    void Release(Chunk chunk, Mesh mesh)
    {
        if (mesh == null || mesh == chunk.Mesh || mesh == chunk.GroundMesh)
            return;
        if ((chunk.Collider != null && chunk.Collider.sharedMesh == mesh) || chunk.GroundCollider.sharedMesh == mesh)
            return;
        foreach (var (_, baked) in baking)
            if (baked == mesh)
            {
                releaseAfterBake.Add(mesh);
                return;
            }
        ReleaseMesh(mesh);
    }

    // Cooks the collision data of every chunk whose collider is out of date, all chunks in parallel.
    void StartBake()
    {
        foreach (var chunk in chunks.Values)
        {
            if (chunk.Collider != null && chunk.Collider.sharedMesh != chunk.Mesh)
                Queue(chunk, chunk.Collider, chunk.Mesh);
            if (chunk.GroundCollider.sharedMesh != chunk.GroundMesh)
                Queue(chunk, chunk.GroundCollider, chunk.GroundMesh);
        }
        if (baking.Count == 0)
            return;
        bakeIds = new NativeArray<EntityId>(baking.Count, Allocator.Persistent);
        for (int i = 0; i < baking.Count; i++)
            bakeIds[i] = baking[i].mesh.GetEntityId();
        bakeHandle = new BakeJob { meshIds = bakeIds }.Schedule(baking.Count, 1);
        JobHandle.ScheduleBatchedJobs();
    }

    // An emptied chunk drops its collider mesh at once; there is nothing to cook.
    void Queue(Chunk chunk, MeshCollider collider, Mesh mesh)
    {
        if (mesh != null)
        {
            baking.Add((collider, mesh));
            return;
        }
        var old = collider.sharedMesh;
        collider.sharedMesh = null;
        Release(chunk, old);
    }

    void FinishBake()
    {
        bakeHandle.Complete();
        if (bakeIds.IsCreated)
            bakeIds.Dispose();
        var done = new List<(MeshCollider collider, Mesh mesh)>(baking);
        baking.Clear();
        foreach (var (collider, mesh) in done)
        {
            var old = collider.sharedMesh;
            collider.sharedMesh = mesh; // uses the cooked data
            foreach (var chunk in chunks.Values)
                if (chunk.Collider == collider || chunk.GroundCollider == collider)
                {
                    Release(chunk, old);
                    break;
                }
        }
        var waiting = new List<Mesh>(releaseAfterBake);
        releaseAfterBake.Clear();
        foreach (var mesh in waiting)
        {
            bool used = false;
            foreach (var chunk in chunks.Values)
                used |= mesh == chunk.Mesh || mesh == chunk.GroundMesh || mesh == chunk.GroundCollider.sharedMesh ||
                        (chunk.Collider != null && chunk.Collider.sharedMesh == mesh);
            if (!used)
                ReleaseMesh(mesh);
        }
    }

    struct BakeJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<EntityId> meshIds;

        public void Execute(int index) => Physics.BakeMesh(meshIds[index], false);
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
