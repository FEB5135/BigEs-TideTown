using System.Collections.Generic;
using IdleTown.Construction;
using UnityEngine;

namespace Tsunami
{
    /// <summary>
    /// Fits the tsunami simulation onto the team's map (World + WFC ground + mountains + wall construction):
    /// - the simulation grid uses the World's cell size, lines up with its cells and extends into an open ocean on
    ///   the side the tsunami comes from (deep sea, continental slope, shelf, nearshore slope: BathymetryProfile);
    /// - ground inside the World is flat at the World's height, mountain meshes are rasterised as terrain, trees and
    ///   houses are not obstacles (water flows between them; houses are tracked for flooding);
    /// - every wall built by WallConstructionController becomes a thin barrier on the matching cell edge;
    /// - the sea floor outside the World is drawn by the TerrainGrid; inside, the team's own ground is shown.
    /// The same height field feeds the solver, the seabed mesh and all measurements.
    /// </summary>
    [ExecuteAlways]
    [DefaultExecutionOrder(-90)] // after TerrainGrid (-100), before WaterSimulation (-50)
    public class TeamMapTsunamiAdapter : MonoBehaviour
    {
        [SerializeField] World world;
        [SerializeField] TerrainGrid terrain;
        [SerializeField] WaterSimulation simulation;
        [Tooltip("Optional: walls built here block the water.")]
        [SerializeField] WallConstructionController wallController;
        [Tooltip("Roots whose meshes count as solid terrain (e.g. the Mountains group). Houses and trees should not be listed.")]
        [SerializeField] Transform[] terrainMeshRoots = new Transform[0];

        [Header("Domain")]
        [Tooltip("Cells of sea added on the land-locked sides of the World.")]
        [SerializeField, Range(4, 32)] int marginCells = 8;
        [Tooltip("Cells of open ocean added on the ocean side(s) of the World.")]
        [SerializeField, Range(16, 120)] int oceanCells = 72;
        [Tooltip("Which sides get the open ocean: +1 = +X/+Z side, -1 = -X/-Z side, 0 = only the small margin.")]
        [SerializeField] Vector2Int oceanSide = new Vector2Int(1, 1);

        [Header("Bathymetry (needs a reset to take effect)")]
        [SerializeField] BathymetryProfile bathymetry = BathymetryProfile.Default;

        [Header("Approach (where the tsunami is aimed)")]
        [Tooltip("World point the main wave should pass through, e.g. the valley between two mountains.")]
        [SerializeField] Vector3 approachThrough = new Vector3(75f, 0f, 67f);
        [Tooltip("World point the wave heads for, e.g. the village centre.")]
        [SerializeField] Vector3 approachTarget = Vector3.zero;

        [Header("Walls")]
        [Tooltip("Wall top above the ground (m) when it can't be read from the wall's renderers.")]
        [SerializeField, Min(0.1f)] float fallbackWallHeight = 1.25f;

        [Header("Wall durability (game model)")]
        [Tooltip("Hit points and kN.s-to-damage scales. Loads come from water density, depth and normal velocity; these rates are gameplay tuning, not structural certification.")]
        [SerializeField] WallDamageSettings wallDamage = WallDamageSettings.Default;
        [Tooltip("Maximum detached wall segments kept as physical debris. The oldest is removed first.")]
        [SerializeField, Range(0, 64)] int maxDebris = 20;
        [SerializeField, Range(0.5f, 30f)] float debrisLifetime = 8f;
        [SerializeField, Range(0f, 2f)] float debrisImpulseScale = 0.18f;

        bool subscribed;
        bool applying;
        Rect mapRectGrid; // World rectangle in grid cell indices
        TsunamiController tsunamiController;
        readonly Dictionary<WallEdge, SegmentState> segments = new Dictionary<WallEdge, SegmentState>();
        readonly Dictionary<WallSegmentHandle, SegmentState> segmentsByHandle = new Dictionary<WallSegmentHandle, SegmentState>();
        readonly List<WallEdge> pendingBreaks = new List<WallEdge>();
        readonly List<WallEdge> reconcileRemovals = new List<WallEdge>();
        readonly List<DebrisRecord> debris = new List<DebrisRecord>();

        sealed class SegmentState
        {
            public WallEdge edge;
            public WallSegmentHandle handle;
            public readonly WallDamageState damage = new WallDamageState();
            public WallFaceImpactSample lastImpact;
            public WallLoadSample lastLoad;
            public bool breakQueued;
        }

        sealed class DebrisRecord
        {
            public GameObject gameObject;
            public Rigidbody body;
            public float remaining;
            public bool paused;
            public Vector3 pausedLinearVelocity;
            public Vector3 pausedAngularVelocity;
        }

        public int WallSegments { get; private set; }
        public BathymetryProfile Bathymetry => bathymetry;
        public Vector3 ApproachThrough => approachThrough;
        public Vector3 ApproachTarget => approachTarget;
        public World World => world;
        /// <summary>The World's rectangle in world space (x, z, width, depth).</summary>
        public Rect MapRectWorld => world == null ? default :
            new Rect(world.transform.position.x, world.transform.position.z, world.Width * world.CellSize, world.Height * world.CellSize);

        void OnEnable()
        {
            Apply();
            BindRuntime();
        }

        void Start()
        {
            BindRuntime();
        }

        void BindRuntime()
        {
            if (!Application.isPlaying || subscribed) return;
            if (wallController == null && world != null)
                wallController = world.GetComponentInParent<WallConstructionController>() ??
                                 world.GetComponentInChildren<WallConstructionController>(true);
            if (wallController == null) return;
            wallController.WallBuilt += OnWallBuilt;
            wallController.WallRemoved += OnWallRemoved;
            simulation.WallLoadSampled += OnWallLoad;
            simulation.WallFaceImpactSampled += OnWallFaceImpact;
            simulation.WallRegistryInvalidated += OnWallRegistryInvalidated;
            tsunamiController = transform.parent != null
                ? transform.parent.GetComponentInChildren<TsunamiController>(true)
                : GetComponentInParent<TsunamiController>();
            if (tsunamiController != null) tsunamiController.SimulationReset += ResetStandingWallDamage;
            subscribed = true;
        }

        void OnDisable()
        {
            if (subscribed && wallController != null)
            {
                wallController.WallBuilt -= OnWallBuilt;
                wallController.WallRemoved -= OnWallRemoved;
            }
            if (subscribed && simulation != null) simulation.WallLoadSampled -= OnWallLoad;
            if (subscribed && simulation != null) simulation.WallFaceImpactSampled -= OnWallFaceImpact;
            if (subscribed && simulation != null) simulation.WallRegistryInvalidated -= OnWallRegistryInvalidated;
            if (subscribed && tsunamiController != null) tsunamiController.SimulationReset -= ResetStandingWallDamage;
            subscribed = false;
            pendingBreaks.Clear();
            ClearDebris();
            if (simulation != null) simulation.ClearWalls();
            segmentsByHandle.Clear();
            WallSegments = 0;
        }

        void Update()
        {
            for (int i = 0; i < pendingBreaks.Count; i++)
            {
                WallEdge edge = pendingBreaks[i];
                if (wallController != null && wallController.TryDetachDestroyedWall(edge, out _)) continue;
                if (segments.TryGetValue(edge, out SegmentState state)) state.breakQueued = false;
            }
            pendingBreaks.Clear();

            for (int i = debris.Count - 1; i >= 0; i--)
            {
                DebrisRecord item = debris[i];
                if (item.gameObject != null)
                {
                    bool pauseDebris = simulation != null && simulation.Paused;
                    if (item.paused != pauseDebris && item.body != null)
                    {
                        if (pauseDebris)
                        {
                            item.pausedLinearVelocity = item.body.linearVelocity;
                            item.pausedAngularVelocity = item.body.angularVelocity;
                            item.body.isKinematic = true;
                        }
                        else
                        {
                            item.body.isKinematic = false;
                            item.body.linearVelocity = item.pausedLinearVelocity;
                            item.body.angularVelocity = item.pausedAngularVelocity;
                        }
                        item.paused = pauseDebris;
                    }
                    if (!pauseDebris) item.remaining -= Time.deltaTime;
                    if (item.remaining > 0f) continue;
                }
                if (item.gameObject != null) Destroy(item.gameObject);
                debris.RemoveAt(i);
            }
        }

        public void SetApproach(Vector3 through, Vector3 target)
        {
            approachThrough = through;
            approachTarget = target;
        }

        /// <summary>Changes the sea floor and rebuilds the terrain (resets the water, keeps the walls).</summary>
        public void SetBathymetry(BathymetryProfile profile)
        {
            bathymetry = profile.Validated();
            Apply();
        }

        [ContextMenu("Re-apply Map Heights")]
        public void Apply()
        {
            if (world == null || terrain == null || simulation == null) return;
            if (Quaternion.Angle(world.transform.rotation, Quaternion.identity) > 0.1f)
                Debug.LogWarning("[TeamMapTsunamiAdapter] The World is rotated; the water grid assumes an axis-aligned World.", this);
            bathymetry = bathymetry.Validated();

            float cell = world.CellSize;
            int W = world.Width, H = world.Height;
            int extraX = oceanSide.x != 0 ? oceanCells : marginCells;
            int extraZ = oceanSide.y != 0 ? oceanCells : marginCells;
            int n = Mathf.Max(W + marginCells + extraX, H + marginCells + extraZ);
            // Map placement inside the square grid: the ocean side gets the extra cells.
            int offX = oceanSide.x > 0 ? marginCells : n - W - marginCells;
            int offZ = oceanSide.y > 0 ? marginCells : n - H - marginCells;
            if (oceanSide.x == 0) offX = (n - W) / 2;
            if (oceanSide.y == 0) offZ = (n - H) / 2;
            mapRectGrid = new Rect(offX, offZ, W, H);

            // Grid sample (x, z) sits on World cell (x - offX, z - offZ): place the grid centre accordingly.
            Vector3 origin = world.transform.position;
            float groundY = origin.y;
            float half = (n - 1) * 0.5f;
            terrain.transform.SetPositionAndRotation(
                new Vector3(origin.x + (half - offX + 0.5f) * cell, groundY, origin.z + (half - offZ + 0.5f) * cell),
                Quaternion.identity);

            var heights = new float[n * n];
            var covered = new bool[n * n];
            for (int z = 0; z < n; z++)
            {
                for (int x = 0; x < n; x++)
                {
                    int wx = x - offX, wz = z - offZ;
                    int i = z * n + x;
                    float dx = wx < 0 ? -wx : (wx >= W ? wx - W + 1 : 0);
                    float dz = wz < 0 ? -wz : (wz >= H ? wz - H + 1 : 0);
                    if (dx == 0 && dz == 0)
                    {
                        heights[i] = 0f;
                        covered[i] = true;
                    }
                    else
                    {
                        // Distance (m) out from the coast (the World's edge), rounded at the corners.
                        float d = (Mathf.Sqrt(dx * dx + dz * dz) - 0.5f) * cell;
                        heights[i] = bathymetry.FloorAt(d);
                    }
                }
            }

            // Apply once so WorldToGrid uses the new grid, then rasterise the mountains and apply again.
            terrain.UseExternalHeights(n, cell, heights);
            foreach (Transform root in terrainMeshRoots)
                if (root != null) RasterizeMeshes(root, heights, n, groundY);
            terrain.UseExternalHeights(n, cell, heights);
            terrain.SetRenderCover(covered);
            var seabed = terrain.GetComponent<MeshRenderer>();
            if (seabed != null) seabed.enabled = true;

            applying = true;
            try
            {
                simulation.SeaLevel = -bathymetry.seaBelowGround;
                simulation.EnsureInitialized();
                RebuildWallRegistrations();
                simulation.ResetWater();
            }
            finally { applying = false; }
        }

        /// <summary>
        /// A point in the open ocean on the approach axis: <paramref name="offshore"/> metres out from the coast,
        /// beyond <see cref="ApproachThrough"/>, kept inside the simulated ocean.
        /// </summary>
        public Vector3 OffshorePoint(float offshore, float lateralOffset = 0f)
        {
            Vector3 dirOut = approachThrough - approachTarget;
            dirOut.y = 0f;
            if (dirOut.sqrMagnitude < 1e-4f) dirOut = new Vector3(oceanSide.x, 0f, oceanSide.y);
            dirOut.Normalize();
            Rect r = MapRectWorld;
            // Distance from the through-point to the map edge along dirOut.
            float tExit = float.MaxValue;
            if (dirOut.x > 1e-4f) tExit = Mathf.Min(tExit, (r.xMax - approachThrough.x) / dirOut.x);
            if (dirOut.x < -1e-4f) tExit = Mathf.Min(tExit, (r.xMin - approachThrough.x) / dirOut.x);
            if (dirOut.z > 1e-4f) tExit = Mathf.Min(tExit, (r.yMax - approachThrough.z) / dirOut.z);
            if (dirOut.z < -1e-4f) tExit = Mathf.Min(tExit, (r.yMin - approachThrough.z) / dirOut.z);
            if (tExit == float.MaxValue || tExit < 0f) tExit = 0f;
            Vector3 lateral = new Vector3(-dirOut.z, 0f, dirOut.x);
            Vector3 p = approachThrough + dirOut * (tExit + offshore) + lateral * lateralOffset;
            return ClampToOcean(p);
        }

        /// <summary>Unit direction (XZ) from the ocean towards the approach target.</summary>
        public Vector2 ApproachDirection
        {
            get
            {
                Vector3 d = approachTarget - approachThrough;
                return d.sqrMagnitude > 1e-4f ? new Vector2(d.x, d.z).normalized : new Vector2(-oceanSide.x, -oceanSide.y).normalized;
            }
        }

        /// <summary>Keeps a point away from the absorbing band at the domain edge.</summary>
        public Vector3 ClampToOcean(Vector3 p)
        {
            if (terrain == null) return p;
            float inner = terrain.HalfExtent - 12f * terrain.CellSize;
            Vector3 c = terrain.transform.position;
            p.x = Mathf.Clamp(p.x, c.x - inner, c.x + inner);
            p.z = Mathf.Clamp(p.z, c.z - inner, c.z + inner);
            return p;
        }

        void OnWallBuilt(WallEdge edge, GameObject instance)
        {
            AddWall(edge, instance);
        }

        bool OnWallRemoved(WallEdge edge, GameObject instance)
        {
            if (!segments.TryGetValue(edge, out SegmentState state))
            {
                if (instance != null) Destroy(instance);
                return true;
            }
            simulation.RemoveWallSegment(state.handle);
            segmentsByHandle.Remove(state.handle);
            segments.Remove(edge);
            WallSegments = Mathf.Max(0, WallSegments - 1);
            CreateDebris(instance, state);
            return true;
        }

        void OnWallLoad(WallLoadSample sample)
        {
            if (!segmentsByHandle.TryGetValue(sample.Handle, out SegmentState state) || state.breakQueued) return;
            state.lastLoad = sample;
            if (!state.damage.Apply(sample.ImpactForceNewtons, sample.SustainedForceNewtons,
                    sample.SolverTimestep, wallDamage, out _, out _)) return;
            state.breakQueued = true;
            pendingBreaks.Add(state.edge);
        }

        void OnWallFaceImpact(WallFaceImpactSample sample)
        {
            if (segmentsByHandle.TryGetValue(sample.Handle, out SegmentState state)) state.lastImpact = sample;
        }

        void OnWallRegistryInvalidated()
        {
            if (!applying && isActiveAndEnabled) RebuildWallRegistrations();
        }

        void RebuildWallRegistrations()
        {
            simulation.ClearWalls();
            segmentsByHandle.Clear();
            WallSegments = 0;
            if (!Application.isPlaying || wallController == null) return;
            ReconcileSegmentStates();
            foreach (WallEdge edge in wallController.Edges)
            {
                wallController.TryGetWallInstance(edge, out GameObject instance);
                AddWall(edge, instance);
            }
        }

        void ReconcileSegmentStates()
        {
            reconcileRemovals.Clear();
            foreach (WallEdge tracked in segments.Keys)
            {
                bool found = false;
                foreach (WallEdge live in wallController.Edges)
                    if (tracked.Equals(live)) { found = true; break; }
                if (!found) reconcileRemovals.Add(tracked);
            }
            for (int i = 0; i < reconcileRemovals.Count; i++) segments.Remove(reconcileRemovals[i]);
            reconcileRemovals.Clear();
        }

        void AddWall(WallEdge edge, GameObject instance)
        {
            float top = world.transform.position.y + fallbackWallHeight;
            if (instance != null)
            {
                var renderers = instance.GetComponentsInChildren<Renderer>();
                if (renderers.Length > 0)
                {
                    top = float.NegativeInfinity;
                    foreach (var r in renderers) top = Mathf.Max(top, r.bounds.max.y);
                }
            }
            WallSegmentHandle handle = simulation.AddWallSegment(NodePosition(edge.A), NodePosition(edge.B), top);
            if (!handle.IsValid) return;
            if (!segments.TryGetValue(edge, out SegmentState state))
            {
                state = new SegmentState { edge = edge };
                segments.Add(edge, state);
            }
            else if (state.handle.IsValid)
            {
                segmentsByHandle.Remove(state.handle);
            }
            state.handle = handle;
            state.breakQueued = false;
            segmentsByHandle[handle] = state;
            WallSegments++;
        }

        void ResetStandingWallDamage()
        {
            foreach (SegmentState state in segments.Values)
            {
                state.damage.Reset();
                state.breakQueued = false;
            }
            pendingBreaks.Clear();
            ClearDebris();
        }

        void CreateDebris(GameObject instance, SegmentState state)
        {
            if (instance == null || maxDebris <= 0)
            {
                if (instance != null) Destroy(instance);
                return;
            }
            while (debris.Count >= maxDebris)
            {
                DebrisRecord oldest = debris[0];
                if (oldest.gameObject != null) Destroy(oldest.gameObject);
                debris.RemoveAt(0);
            }
            Collider[] originalColliders = instance.GetComponentsInChildren<Collider>(true);
            Renderer[] renderers = instance.GetComponentsInChildren<Renderer>(true);
            bool hasBounds = false;
            Vector3 localMin = Vector3.zero, localMax = Vector3.zero;
            for (int i = 0; i < renderers.Length; i++)
                IncludeWorldBounds(renderers[i].bounds, instance.transform, ref hasBounds, ref localMin, ref localMax);
            if (!hasBounds)
            {
                for (int i = 0; i < originalColliders.Length; i++)
                    IncludeWorldBounds(originalColliders[i].bounds, instance.transform, ref hasBounds, ref localMin, ref localMax);
            }
            for (int i = 0; i < originalColliders.Length; i++) originalColliders[i].enabled = false;

            // Dynamic non-convex MeshColliders are unsupported. Every broken segment gets one cheap conservative
            // root box; intact static wall colliders are never modified.
            BoxCollider debrisCollider = instance.GetComponent<BoxCollider>();
            if (debrisCollider == null) debrisCollider = instance.AddComponent<BoxCollider>();
            debrisCollider.center = hasBounds ? (localMin + localMax) * 0.5f : Vector3.zero;
            Vector3 proxySize = hasBounds ? localMax - localMin : Vector3.one;
            debrisCollider.size = new Vector3(
                Mathf.Max(0.02f, proxySize.x),
                Mathf.Max(0.02f, proxySize.y),
                Mathf.Max(0.02f, proxySize.z));
            debrisCollider.isTrigger = false;
            debrisCollider.enabled = true;
            Rigidbody body = instance.GetComponent<Rigidbody>();
            if (body == null) body = instance.AddComponent<Rigidbody>();
            body.isKinematic = false;
            body.mass = 25f;
            body.linearDamping = 0.6f;
            body.angularDamping = 0.8f;
            WallLoadSample load = state.lastLoad;
            float effectiveLoad = Mathf.Max(load.ImpactForceNewtons, load.SustainedForceNewtons * 0.25f);
            float impulse = Mathf.Min(18f, effectiveLoad * 0.001f * debrisImpulseScale);
            Vector3 direction = load.LoadNormal.sqrMagnitude > 0.5f ? load.LoadNormal : instance.transform.right;
            Vector3 position = load.SolverTimestep > 0f
                ? load.LoadPosition
                : (NodePosition(state.edge.A) + NodePosition(state.edge.B)) * 0.5f + Vector3.up * (fallbackWallHeight * 0.5f);
            body.AddForceAtPosition((direction + Vector3.up * 0.35f) * impulse, position,
                ForceMode.Impulse);
            body.AddTorque(new Vector3(direction.z, 0.4f, -direction.x) * impulse * 0.25f,
                ForceMode.Impulse);
            debris.Add(new DebrisRecord { gameObject = instance, body = body, remaining = debrisLifetime });
        }

        static void IncludeWorldBounds(Bounds bounds, Transform root, ref bool hasBounds,
            ref Vector3 localMin, ref Vector3 localMax)
        {
            Vector3 min = bounds.min, max = bounds.max;
            for (int x = 0; x < 2; x++)
                for (int y = 0; y < 2; y++)
                    for (int z = 0; z < 2; z++)
                    {
                        Vector3 local = root.InverseTransformPoint(new Vector3(
                            x == 0 ? min.x : max.x,
                            y == 0 ? min.y : max.y,
                            z == 0 ? min.z : max.z));
                        if (!hasBounds)
                        {
                            localMin = localMax = local;
                            hasBounds = true;
                        }
                        else
                        {
                            localMin = Vector3.Min(localMin, local);
                            localMax = Vector3.Max(localMax, local);
                        }
                    }
        }

        void ClearDebris()
        {
            for (int i = 0; i < debris.Count; i++)
                if (debris[i].gameObject != null) Destroy(debris[i].gameObject);
            debris.Clear();
        }

        // Same convention as WallConstructionController: nodes are World cell corners.
        Vector3 NodePosition(Vector2Int node)
        {
            return world.transform.TransformPoint(new Vector3(node.x * world.CellSize, 0f, node.y * world.CellSize));
        }

        /// <summary>Raises grid heights to the top surface of every mesh under <paramref name="root"/>.</summary>
        void RasterizeMeshes(Transform root, float[] heights, int n, float groundY)
        {
            foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>())
            {
                Mesh mesh = filter.sharedMesh;
                if (mesh == null) continue;
                if (!mesh.isReadable)
                {
                    Debug.LogWarning($"[TeamMapTsunamiAdapter] Mesh '{mesh.name}' is not readable; enable Read/Write so water can flow around it.", filter);
                    continue;
                }
                Matrix4x4 toWorld = filter.transform.localToWorldMatrix;
                Vector3[] verts = mesh.vertices;
                for (int v = 0; v < verts.Length; v++) verts[v] = toWorld.MultiplyPoint3x4(verts[v]);
                int[] tris = mesh.triangles;
                for (int t = 0; t < tris.Length; t += 3)
                    RasterizeTriangle(verts[tris[t]], verts[tris[t + 1]], verts[tris[t + 2]], heights, n, groundY);
            }
        }

        void RasterizeTriangle(Vector3 a, Vector3 b, Vector3 c, float[] heights, int n, float groundY)
        {
            Vector2 ga = terrain.WorldToGrid(a), gb = terrain.WorldToGrid(b), gc = terrain.WorldToGrid(c);
            int x0 = Mathf.Max(0, Mathf.CeilToInt(Mathf.Min(ga.x, Mathf.Min(gb.x, gc.x))));
            int x1 = Mathf.Min(n - 1, Mathf.FloorToInt(Mathf.Max(ga.x, Mathf.Max(gb.x, gc.x))));
            int z0 = Mathf.Max(0, Mathf.CeilToInt(Mathf.Min(ga.y, Mathf.Min(gb.y, gc.y))));
            int z1 = Mathf.Min(n - 1, Mathf.FloorToInt(Mathf.Max(ga.y, Mathf.Max(gb.y, gc.y))));
            float area = (gb.x - ga.x) * (gc.y - ga.y) - (gc.x - ga.x) * (gb.y - ga.y);
            if (Mathf.Abs(area) < 1e-6f) return; // vertical or degenerate in plan view
            float inv = 1f / area;
            for (int z = z0; z <= z1; z++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    float w1 = ((x - ga.x) * (gc.y - ga.y) - (gc.x - ga.x) * (z - ga.y)) * inv;
                    float w2 = ((gb.x - ga.x) * (z - ga.y) - (x - ga.x) * (gb.y - ga.y)) * inv;
                    float w0 = 1f - w1 - w2;
                    if (w0 < -1e-4f || w1 < -1e-4f || w2 < -1e-4f) continue;
                    float y = w0 * a.y + w1 * b.y + w2 * c.y - groundY;
                    int i = z * n + x;
                    if (y > heights[i]) heights[i] = y;
                }
            }
        }
    }
}
