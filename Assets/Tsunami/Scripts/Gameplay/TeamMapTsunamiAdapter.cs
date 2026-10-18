using System;
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

        [Header("Runtime map (WFC hills and mountains)")]
        [Tooltip("After the WFC map is built at runtime, draw its hill / mountain / slope tiles into the water terrain.")]
        [SerializeField] bool rasterizeWfcTiles = true;
        [Tooltip("World tile ids that are not obstacles for water (trees: water flows between them).")]
        [SerializeField] int[] ignoredTileIds = { 5 };
        [Tooltip("Pick the wave's approach automatically: the most open corridor from the map edge to the village.")]
        [SerializeField] bool autoApproach = true;
        [Tooltip("Half-width (m) of the corridor checked for hills on each candidate approach.")]
        [SerializeField, Range(2f, 40f)] float corridorHalfWidth = 12f;
        [SerializeField, Range(4, 64)] int corridorHeadings = 16;

        [Header("Walls")]
        [Tooltip("Wall top above the ground (m) when it can't be read from the wall's renderers.")]
        [SerializeField, Min(0.1f)] float fallbackWallHeight = WallConstructionController.StoreyHeight;

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
        readonly Dictionary<(WallEdge edge, int floor), SegmentState> segments = new Dictionary<(WallEdge edge, int floor), SegmentState>();
        readonly Dictionary<WallSegmentHandle, SegmentState> segmentsByHandle = new Dictionary<WallSegmentHandle, SegmentState>();
        readonly List<(WallEdge edge, int floor)> pendingBreaks = new List<(WallEdge edge, int floor)>();
        readonly List<(WallEdge edge, int floor)> reconcileRemovals = new List<(WallEdge edge, int floor)>();
        readonly List<DebrisRecord> debris = new List<DebrisRecord>();

        sealed class SegmentState
        {
            public WallEdge edge;
            public int floor;
            public WallTier tier;
            public WallDamageVisual visual;
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
        public Vector2Int OceanSide => oceanSide;
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
            if (Application.isPlaying) StartCoroutine(RebuildAfterMapGenerated());
        }

        /// <summary>Approach heading chosen at runtime (compass degrees, 0 = +Z / north), or NaN.</summary>
        public float ChosenHeading { get; private set; } = float.NaN;

        /// <summary>The four corners of the map: the ocean on two neighbouring edges (+1 = +X / +Z side).</summary>
        public static readonly Vector2Int[] Corners =
            { new Vector2Int(1, 1), new Vector2Int(1, -1), new Vector2Int(-1, -1), new Vector2Int(-1, 1) };

        bool mapReady;
        bool hasRequestedCorner;
        Vector2Int requestedCorner;
        // The map's tile heights per World cell, rasterised once the map exists. Every water grid sample sits on a
        // World cell whatever side the ocean is on, so moving the ocean only copies these instead of rasterising
        // every chunk triangle again (that took ~0.35 s).
        float[] mapHeights;

        // WfcSolver and MapView build the map in their Start; wait until the chunks exist, then rebuild the water
        // terrain from them and aim the wave through the most open corridor of this (random) map.
        System.Collections.IEnumerator RebuildAfterMapGenerated()
        {
            yield return null;
            yield return null;
            if (world == null || terrain == null || simulation == null) yield break;
            mapReady = true;
            Apply();
            if (hasRequestedCorner) { if (ChooseApproach(requestedCorner)) Apply(); }
            else if (autoApproach && ChooseApproach(null)) Apply();
        }

        /// <summary>
        /// The next wave comes from this corner: the open ocean lies along its two edges and the wave is aimed down
        /// the most open corridor within that quarter. Rebuilds the water domain (water back at rest) when the ocean
        /// moves, so call it while the land is dry. Before the map exists the corner is kept for when it does.
        /// </summary>
        public void AimFromCorner(Vector2Int corner)
        {
            requestedCorner = corner;
            hasRequestedCorner = true;
            if (mapReady && ChooseApproach(corner)) Apply();
        }

        /// <summary>
        /// Scores straight corridors from the village (approach target) to the map edge in corridorHeadings
        /// directions by the hills they cross, aims the wave down the most open one and puts the open ocean on that
        /// side. With a corner, only the headings out through that corner's quarter count and the ocean goes on its
        /// two edges. Returns true when the ocean side changed (the domain must be rebuilt).
        /// </summary>
        bool ChooseApproach(Vector2Int? corner)
        {
            Rect r = MapRectWorld;
            Vector3 target = approachTarget;
            float groundY = world.transform.position.y;
            float step = world.CellSize;
            float bestScore = float.MaxValue, bestExit = 0f;
            Vector2 bestDir = Vector2.zero;
            for (int k = 0; k < corridorHeadings; k++)
            {
                float a = k * Mathf.PI * 2f / corridorHeadings;
                var d = new Vector2(Mathf.Sin(a), Mathf.Cos(a));
                if (corner.HasValue && (d.x * corner.Value.x < 0.38f || d.y * corner.Value.y < 0.38f)) continue;
                float tExit = ExitDistance(r, target, d);
                if (tExit < 30f) continue;
                var perp = new Vector2(-d.y, d.x);
                float sum = 0f, peak = 0f;
                int samples = 0;
                for (float t = 20f; t < tExit; t += step)
                {
                    for (int s = -1; s <= 1; s++)
                    {
                        Vector2 p = new Vector2(target.x, target.z) + d * t + perp * (s * corridorHalfWidth);
                        float h = terrain.SampleHeight(new Vector3(p.x, 0f, p.y)) - groundY;
                        if (h > 0.4f) sum += h;
                        if (h > peak) peak = h;
                        samples++;
                    }
                }
                float score = samples > 0 ? sum / samples + 0.25f * peak : float.MaxValue;
                if (score < bestScore) { bestScore = score; bestDir = d; bestExit = tExit; }
            }
            if (bestDir == Vector2.zero && corner.HasValue)
            {
                // No corridor long enough in that quarter: straight in from the corner.
                bestDir = new Vector2(corner.Value.x, corner.Value.y).normalized;
                bestExit = ExitDistance(r, target, bestDir);
                bestScore = float.NaN;
            }
            if (bestDir == Vector2.zero) return false;
            approachThrough = new Vector3(target.x + bestDir.x * bestExit * 0.75f, target.y, target.z + bestDir.y * bestExit * 0.75f);
            ChosenHeading = Mathf.Repeat(Mathf.Atan2(bestDir.x, bestDir.y) * Mathf.Rad2Deg, 360f);
            var side = corner ?? new Vector2Int(Mathf.Abs(bestDir.x) > 0.38f ? (int)Mathf.Sign(bestDir.x) : 0,
                                                Mathf.Abs(bestDir.y) > 0.38f ? (int)Mathf.Sign(bestDir.y) : 0);
            Debug.Log($"[TeamMapTsunamiAdapter] Wave approach: {ChosenHeading:0}° (most open corridor, mean hill {bestScore:0.00}); ocean side {side}.", this);
            if (side == oceanSide) return false;
            oceanSide = side;
            return true;
        }

        static float ExitDistance(Rect r, Vector3 from, Vector2 d)
        {
            float t = float.MaxValue;
            if (d.x > 1e-4f) t = Mathf.Min(t, (r.xMax - from.x) / d.x);
            if (d.x < -1e-4f) t = Mathf.Min(t, (r.xMin - from.x) / d.x);
            if (d.y > 1e-4f) t = Mathf.Min(t, (r.yMax - from.z) / d.y);
            if (d.y < -1e-4f) t = Mathf.Min(t, (r.yMin - from.z) / d.y);
            return t == float.MaxValue ? 0f : Mathf.Max(0f, t);
        }

        void BindRuntime()
        {
            if (!Application.isPlaying || subscribed) return;
            if (wallController == null && world != null)
                wallController = world.GetComponentInParent<WallConstructionController>() ??
                                 world.GetComponentInChildren<WallConstructionController>(true);
            if (wallController == null) return;
            wallController.WallTierBuilt += OnWallBuilt;
            wallController.WallTierRemoved += OnWallRemoved;
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
                wallController.WallTierBuilt -= OnWallBuilt;
                wallController.WallTierRemoved -= OnWallRemoved;
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
                var key = pendingBreaks[i];
                if (wallController != null && wallController.TryDetachDestroyedWall(key.edge, key.floor, out _)) continue;
                if (segments.TryGetValue(key, out SegmentState state)) state.breakQueued = false;
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
                if (root != null && root.gameObject.activeInHierarchy) RasterizeMeshes(root, heights, n, groundY, null);
            if (Application.isPlaying && rasterizeWfcTiles)
            {
                if (mapHeights != null && mapHeights.Length == W * H) PasteMapHeights(heights, n, offX, offZ, W, H);
                else
                {
                    RasterizeWfcChunks(heights, n, groundY, offX, offZ);
                    if (mapReady) mapHeights = CopyMapHeights(heights, n, offX, offZ, W, H);
                }
            }
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

        void OnWallBuilt(WallRecord record, GameObject instance)
        {
            AddWall(record, instance);
        }

        bool OnWallRemoved(WallRecord record, GameObject instance)
        {
            var key = (record.Edge, record.Floor);
            if (!segments.TryGetValue(key, out SegmentState state))
            {
                if (instance != null) Destroy(instance);
                return true;
            }
            simulation.RemoveWallSegment(state.handle);
            segmentsByHandle.Remove(state.handle);
            segments.Remove(key);
            WallSegments = Mathf.Max(0, WallSegments - 1);
            CreateDebris(instance, state);
            return true;
        }

        void OnWallLoad(WallLoadSample sample)
        {
            if (!segmentsByHandle.TryGetValue(sample.Handle, out SegmentState state) || state.breakQueued) return;
            state.lastLoad = sample;
            var settings = SettingsFor(state.tier);
            bool broken = state.damage.Apply(sample.ImpactForceNewtons, sample.SustainedForceNewtons,
                    sample.SolverTimestep, settings, out _, out _);
            if (state.visual != null) state.visual.SetDamage(state.damage.Damage / settings.durability);
            if (!broken) return;
            state.breakQueued = true;
            pendingBreaks.Add((state.edge, state.floor));
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
            foreach (WallRecord record in wallController.Walls)
            {
                wallController.TryGetWallInstance(record.Edge, record.Floor, out GameObject instance);
                AddWall(record, instance);
            }
        }

        void ReconcileSegmentStates()
        {
            reconcileRemovals.Clear();
            foreach (var tracked in segments.Keys)
            {
                bool found = false;
                foreach (WallRecord live in wallController.Walls)
                    if (tracked.edge.Equals(live.Edge) && tracked.floor == live.Floor) { found = true; break; }
                if (!found) reconcileRemovals.Add(tracked);
            }
            for (int i = 0; i < reconcileRemovals.Count; i++) segments.Remove(reconcileRemovals[i]);
            reconcileRemovals.Clear();
        }

        void AddWall(WallRecord record, GameObject instance)
        {
            var edge = record.Edge;
            var key = (edge, record.Floor);
            float top = world.transform.position.y + fallbackWallHeight;
            float bottom = world.transform.position.y + record.Floor * fallbackWallHeight;
            if (instance != null)
            {
                var existingVisual = instance.GetComponent<WallDamageVisual>();
                var renderers = existingVisual != null ? existingVisual.SourceRenderers : instance.GetComponentsInChildren<Renderer>();
                if (renderers.Length > 0)
                {
                    top = float.NegativeInfinity;
                    bottom = float.PositiveInfinity;
                    foreach (var r in renderers) { top = Mathf.Max(top, r.bounds.max.y); bottom = Mathf.Min(bottom, r.bounds.min.y); }
                }
            }
            // Upgrading/rebinding replaces an owner's old barrier rather than leaking a duplicate crest.
            if (segments.TryGetValue(key, out var previous) && previous.handle.IsValid)
                simulation.RemoveWallSegment(previous.handle);
            WallSegmentHandle handle = simulation.AddWallSegment(NodePosition(edge.A), NodePosition(edge.B), top, bottom, true);
            if (!handle.IsValid) return;
            if (!segments.TryGetValue(key, out SegmentState state))
            {
                state = new SegmentState { edge = edge, floor = record.Floor, tier = record.Tier };
                segments.Add(key, state);
            }
            else if (state.handle.IsValid)
            {
                segmentsByHandle.Remove(state.handle);
            }
            state.handle = handle;
            if (state.tier != record.Tier) state.damage.Reset();
            state.tier = record.Tier;
            if (instance != null)
            {
                state.visual = instance.GetComponent<WallDamageVisual>() ?? instance.AddComponent<WallDamageVisual>();
                state.visual.SetDamage(state.damage.Damage / SettingsFor(state.tier).durability);
            }
            state.breakQueued = false;
            segmentsByHandle[handle] = state;
            WallSegments = segmentsByHandle.Count;
        }

        WallDamageSettings SettingsFor(WallTier tier)
        {
            var settings = wallDamage.Validated();
            settings.durability *= tier == WallTier.Reinforced ? 3.5f : tier == WallTier.Stone ? 2f : 1f;
            return settings;
        }

        void ResetStandingWallDamage()
        {
            foreach (SegmentState state in segments.Values)
            {
                state.damage.Reset();
                if (state.visual != null) state.visual.SetDamage(0f);
                state.breakQueued = false;
            }
            pendingBreaks.Clear();
            ClearDebris();
        }

        void CreateDebris(GameObject instance, SegmentState state)
        {
            if (instance == null) return;
            if (maxDebris <= 0) { Destroy(instance); return; }
            // Disable the intact collider immediately; the root is discarded after its pieces detach.
            foreach (var collider in instance.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
            var visual = state.visual != null ? state.visual : instance.AddComponent<WallDamageVisual>();
            var pieces = visual.DetachFragments();
            WallLoadSample load = state.lastLoad;
            float effectiveLoad = Mathf.Max(load.ImpactForceNewtons, load.SustainedForceNewtons * .25f);
            float impulse = Mathf.Clamp(effectiveLoad * .001f * debrisImpulseScale, .2f, 6f);
            Vector3 direction = load.LoadNormal.sqrMagnitude > .5f ? load.LoadNormal : instance.transform.right;
            for (int i = 0; i < pieces.Count; i++)
            {
                var piece = pieces[i];
                if (!piece) continue;
                while (debris.Count >= maxDebris)
                {
                    if (debris[0].gameObject != null)
                    {
                        debris[0].gameObject.SetActive(false);
                        Destroy(debris[0].gameObject);
                    }
                    debris.RemoveAt(0);
                }
                var body = piece.AddComponent<Rigidbody>();
                body.mass = 2f;
                body.linearDamping = .6f;
                body.angularDamping = .8f;
                body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                body.AddForce((direction + Vector3.up * (.35f + i % 3 * .12f)) * impulse, ForceMode.Impulse);
                body.AddTorque(new Vector3(direction.z, .4f, -direction.x) * impulse * (i % 2 == 0 ? .25f : -.25f), ForceMode.Impulse);
                debris.Add(new DebrisRecord { gameObject = piece, body = body, remaining = debrisLifetime });
            }
            Destroy(instance);
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

        /// <summary>
        /// Draws the WFC map's chunk meshes (built by MapView at runtime) into the heights, except cells whose tile
        /// is listed in ignoredTileIds (trees) - so hills, mountains and slopes block and steer the water.
        /// </summary>
        static float[] CopyMapHeights(float[] heights, int n, int offX, int offZ, int W, int H)
        {
            var map = new float[W * H];
            for (int wz = 0; wz < H; wz++)
                for (int wx = 0; wx < W; wx++)
                    map[wz * W + wx] = heights[(wz + offZ) * n + wx + offX];
            return map;
        }

        void PasteMapHeights(float[] heights, int n, int offX, int offZ, int W, int H)
        {
            for (int wz = 0; wz < H; wz++)
                for (int wx = 0; wx < W; wx++)
                {
                    int i = (wz + offZ) * n + wx + offX;
                    heights[i] = Mathf.Max(heights[i], mapHeights[wz * W + wx]);
                }
        }

        void RasterizeWfcChunks(float[] heights, int n, float groundY, int offX, int offZ)
        {
            Func<int, int, bool> skip = (x, z) =>
            {
                int wx = x - offX, wz = z - offZ;
                if (!world.InBounds(wx, wz)) return true;
                world.Get(wx, wz, out int id, out _);
                for (int k = 0; k < ignoredTileIds.Length; k++) if (ignoredTileIds[k] == id) return true;
                return false;
            };
            foreach (MapView view in FindObjectsByType<MapView>())
            {
                foreach (Transform child in view.transform)
                {
                    if (!child.name.StartsWith("Chunk ") || !child.gameObject.activeInHierarchy) continue;
                    RasterizeMeshes(child, heights, n, groundY, skip);
                }
            }
        }

        /// <summary>Raises grid heights to the top surface of every mesh under <paramref name="root"/>.</summary>
        void RasterizeMeshes(Transform root, float[] heights, int n, float groundY, Func<int, int, bool> skipCell)
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
                    RasterizeTriangle(verts[tris[t]], verts[tris[t + 1]], verts[tris[t + 2]], heights, n, groundY, skipCell);
            }
        }

        void RasterizeTriangle(Vector3 a, Vector3 b, Vector3 c, float[] heights, int n, float groundY, Func<int, int, bool> skipCell)
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
                    if (y <= heights[i]) continue;
                    if (skipCell != null && skipCell(x, z)) continue;
                    if (y > heights[i]) heights[i] = y;
                }
            }
        }
    }
}
