using System;
using System.Collections.Generic;
using UnityEngine;

namespace Tsunami
{
    /// <summary>Opaque identifier for one registered physical wall segment.</summary>
    public readonly struct WallSegmentHandle : IEquatable<WallSegmentHandle>
    {
        internal readonly int Value;
        internal WallSegmentHandle(int value) { Value = value; }
        public bool IsValid => Value > 0;
        public bool Equals(WallSegmentHandle other) => Value == other.Value;
        public override bool Equals(object obj) => obj is WallSegmentHandle other && Equals(other);
        public override int GetHashCode() => Value;
    }

    /// <summary>Per-segment hydraulic totals sampled once after one completed solver step.</summary>
    public readonly struct WallLoadSample
    {
        public readonly WallSegmentHandle Handle;
        public readonly float ImpactForceNewtons;
        public readonly float SustainedForceNewtons;
        public readonly float SolverTimestep;
        public readonly Vector3 LoadPosition;
        public readonly Vector3 LoadNormal;
        public readonly bool HasDirectionalImpact;

        internal WallLoadSample(WallSegmentHandle handle, float impactForceNewtons,
            float sustainedForceNewtons, float solverTimestep, Vector3 loadPosition, Vector3 loadNormal,
            bool hasDirectionalImpact)
        {
            Handle = handle;
            ImpactForceNewtons = impactForceNewtons;
            SustainedForceNewtons = sustainedForceNewtons;
            SolverTimestep = solverTimestep;
            LoadPosition = loadPosition;
            LoadNormal = loadNormal;
            HasDirectionalImpact = hasDirectionalImpact;
        }
    }

    /// <summary>Strongest directional inflow on one numerical face of a real segment.</summary>
    public readonly struct WallFaceImpactSample
    {
        public readonly WallSegmentHandle Handle;
        public readonly Vector3 Position;
        /// <summary>Real segment normal, oriented to agree with the selected grid-face inflow.</summary>
        public readonly Vector3 IntoNormal;
        /// <summary>Axis-aligned numerical face normal used to calculate normal velocity and force.</summary>
        public readonly Vector3 GridIntoNormal;
        public readonly float WaterDepth;
        public readonly float InflowSpeed;
        public readonly float ContactWidth;
        public readonly float ImpactForceNewtons;
        public readonly long SampleSequence;
        public readonly int FaceIndex;
        public readonly bool AlongX;

        internal WallFaceImpactSample(WallSegmentHandle handle, Vector3 position, Vector3 intoNormal,
            Vector3 gridIntoNormal, float waterDepth, float inflowSpeed, float contactWidth, float impactForceNewtons,
            long sampleSequence, int faceIndex, bool alongX)
        {
            Handle = handle;
            Position = position;
            IntoNormal = intoNormal;
            GridIntoNormal = gridIntoNormal;
            WaterDepth = waterDepth;
            InflowSpeed = inflowSpeed;
            ContactWidth = contactWidth;
            ImpactForceNewtons = impactForceNewtons;
            SampleSequence = sampleSequence;
            FaceIndex = faceIndex;
            AlongX = alongX;
        }
    }

    /// <summary>
    /// Drives the TsunamiPhysicsSolver on the TerrainGrid. Simulation time is separate from rendering time:
    /// every frame the solver advances (frame time x TimeScale) in CFL-limited steps, capped by a per-frame
    /// step/time budget (if the budget runs out the simulation runs slower instead of becoming unstable).
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public class WaterSimulation : MonoBehaviour
    {
        [SerializeField] TerrainGrid terrain;

        [Header("Time")]
        [Tooltip("Simulation seconds per real second (playback speed). Does not change the physics.")]
        [SerializeField, Range(0.1f, 8f)] float timeScale = 1f;
        [Tooltip("Upper bound for one solver step (s). The CFL condition usually picks a smaller one.")]
        [SerializeField, Range(0.005f, 0.1f)] float maxTimestep = 0.05f;
        [Tooltip("Courant number of the solver (stability). 0.3 is safe; above 0.4 the shoreline may misbehave.")]
        [SerializeField, Range(0.05f, 0.45f)] float courantNumber = 0.3f;
        [Tooltip("At most this many solver steps per rendered frame.")]
        [SerializeField, Range(1, 32)] int maxStepsPerFrame = 8;
        [Tooltip("At most this much CPU time per frame for the solver (ms); the simulation slows down instead.")]
        [SerializeField, Range(2f, 40f)] float frameBudgetMs = 10f;
        [SerializeField] bool paused;

        [Header("Physics")]
        [SerializeField, Range(1f, 20f)] float gravity = 9.81f;
        [Tooltip("Manning roughness of the sea floor (s/m^1/3). 0.02-0.03 for sand/mud.")]
        [SerializeField, Range(0f, 0.1f)] float seaManning = 0.025f;
        [Tooltip("Manning roughness of land (s/m^1/3). Grass/streets 0.03-0.06, towns/forest up to 0.1.")]
        [SerializeField, Range(0f, 0.2f)] float landManning = 0.06f;
        [Tooltip("Second-order (MUSCL) reconstruction: sharper waves. Off = first order (more diffusive, cheaper).")]
        [SerializeField] bool secondOrder = true;
        [Tooltip("Spread the solver over all CPU cores.")]
        [SerializeField] bool multithreaded = true;
        [Tooltip("Depth (m) below which a cell counts as dry for rendering, foam and measurements.")]
        [SerializeField, Range(0.001f, 0.1f)] float wetThreshold = 0.01f;

        [Header("Sea and boundaries")]
        [SerializeField] float seaLevel = 0f;
        [Tooltip("Open sea at the domain edge (waves leave through an absorbing band). Off = solid walls (waves reflect).")]
        [SerializeField] bool openBoundaries = true;
        [Tooltip("Relaxation rate (1/s) of the absorbing band at the domain edge.")]
        [SerializeField, Range(0f, 10f)] float spongeStrength = 1.5f;
        [SerializeField, Range(0, 24)] int spongeCells = 6;

        [Header("Open-ocean blending (visual only)")]
        [Tooltip("Cells from the grid edge over which the rendered water fades into the still open ocean, hiding the " +
                 "square outline of the domain and its absorbing band.")]
        [SerializeField, Range(3, 24)] int edgeFadeCells = 8;

        /// <summary>Depth value the shader receives for open ocean (deep colour, no shoreline foam).</summary>
        public const float OpenOceanShaderDepth = 50f;

        TsunamiPhysicsSolver solver;
        double pendingSimTime;
        float[] edgeBlend;
        readonly System.Diagnostics.Stopwatch budget = new System.Diagnostics.Stopwatch();
        readonly Dictionary<int, RegisteredWall> walls = new Dictionary<int, RegisteredWall>();
        readonly List<RegisteredWall> wallIteration = new List<RegisteredWall>();
        int nextWallHandle = 1;
        long impactSampleSequence;

        const float WaterDensity = 1000f;

        sealed class RegisteredWall
        {
            public int id;
            public Vector3 worldA, worldB;
            public float topLocal, bottomLocal;
            public bool impermeable;
            public readonly List<WallFace> faces = new List<WallFace>(2);
        }

        readonly struct WallFace
        {
            public readonly int x, z;
            public readonly bool alongX;
            public WallFace(int x, int z, bool alongX) { this.x = x; this.z = z; this.alongX = alongX; }
        }

        public TerrainGrid Terrain => terrain;
        public TsunamiPhysicsSolver Solver { get { EnsureInitialized(); return solver; } }
        public float[] Depth => Solver.Depth;
        public float[] Speed => Solver.Speed;
        public float WetThreshold => wetThreshold;
        public float Gravity => gravity;
        public bool Paused { get => paused; set => paused = value; }
        public float TimeScale { get => timeScale; set => timeScale = Mathf.Clamp(value, 0.1f, 8f); }
        public float SeaManning { get => seaManning; set { seaManning = Mathf.Clamp(value, 0f, 0.1f); if (solver != null) PushSettings(); } }
        public float LandManning { get => landManning; set { landManning = Mathf.Clamp(value, 0f, 0.2f); if (solver != null) PushSettings(); } }
        /// <summary>Simulated time since the last reset (s).</summary>
        public double SimTime => solver != null ? solver.Time : 0.0;
        /// <summary>True when the last frame could not keep up with TimeScale (budget exhausted).</summary>
        public bool RunningSlow { get; private set; }
        public int StepsLastFrame { get; private set; }
        public float SolverMsLastFrame { get; private set; }
        /// <summary>Incremented after every frame in which the solver advanced (renderers use it to skip work).</summary>
        public int StepCount { get; private set; }
        /// <summary>Raised on Unity's main thread exactly once per loaded segment and completed solver step.</summary>
        public event Action<WallLoadSample> WallLoadSampled;
        /// <summary>Raised at most once per numerical face with normal inflow; visual effects consume this event.</summary>
        public event Action<WallFaceImpactSample> WallFaceImpactSampled;
        /// <summary>Raised after grid-size changes recreate the solver and invalidate all segment handles.</summary>
        public event Action WallRegistryInvalidated;

        /// <summary>Still-water level relative to the terrain transform. Takes effect on the next ResetWater.</summary>
        public float SeaLevel
        {
            get => seaLevel;
            set { seaLevel = value; if (solver != null) PushSettings(); }
        }

        /// <summary>World-space height of the still sea.</summary>
        public float SeaLevelWorld => (terrain != null ? terrain.transform.position.y : 0f) + seaLevel;

        void Awake()
        {
            EnsureInitialized();
        }

        void OnValidate()
        {
            if (solver == null) return;
            PushSettings();
            BuildEdgeBlend();
        }

        public void EnsureInitialized()
        {
            if (terrain == null) terrain = GetComponent<TerrainGrid>();
            if (terrain == null)
            {
                Debug.LogError("[WaterSimulation] No TerrainGrid assigned.", this);
                enabled = false;
                return;
            }
            terrain.EnsureInitialized();
            if (solver != null && solver.Size == terrain.Resolution && Mathf.Approximately(solver.CellSize, terrain.CellSize)) return;

            walls.Clear();
            wallIteration.Clear();
            solver = new TsunamiPhysicsSolver(terrain.Resolution, terrain.CellSize) { SpongeCells = spongeCells };
            PushSettings();
            solver.ResetToSeaLevel(terrain.Heights);
            BuildEdgeBlend();
            StepCount++;
            WallRegistryInvalidated?.Invoke();
        }

        void PushSettings()
        {
            solver.Gravity = gravity;
            solver.SeaManning = seaManning;
            solver.LandManning = landManning;
            solver.Cfl = courantNumber;
            solver.SecondOrder = secondOrder;
            solver.UseThreads = multithreaded;
            solver.SeaLevel = seaLevel;
            solver.OpenBoundaries = openBoundaries;
            solver.SpongeStrength = spongeStrength;
        }

        void Update()
        {
            StepsLastFrame = 0;
            SolverMsLastFrame = 0f;
            RunningSlow = false;
            if (solver == null || paused) return;

            pendingSimTime += Time.deltaTime * timeScale;
            float[] h = terrain.Heights;
            budget.Restart();
            int steps = 0;
            while (pendingSimTime > 1e-5 && steps < maxStepsPerFrame && budget.Elapsed.TotalMilliseconds < frameBudgetMs)
            {
                float dt = solver.Step(h, Mathf.Min(maxTimestep, (float)pendingSimTime));
                if (dt <= 0f) break;
                pendingSimTime -= dt;
                SampleWallLoads(dt);
                steps++;
            }
            if (pendingSimTime > maxTimestep)
            {
                // Could not keep up: drop the backlog (the simulation runs slower than requested, never unstable).
                RunningSlow = true;
                pendingSimTime = 0;
            }
            StepsLastFrame = steps;
            SolverMsLastFrame = (float)budget.Elapsed.TotalMilliseconds;
            if (steps > 0) StepCount++;
        }

        /// <summary>Removes all motion and refills the sea to the still-water level. Walls are kept.</summary>
        public void ResetWater()
        {
            EnsureInitialized();
            PushSettings();
            solver.ResetToSeaLevel(terrain.Heights);
            pendingSimTime = 0;
            StepCount++;
        }

        /// <summary>Adds an initial sea-surface disturbance centred on a world position.</summary>
        public double AddSource(Vector3 worldCentre, Vector2 travelDirection, float amplitude, float lengthRadius,
            float widthRadius, SourceProfile profile, SourceMotion motion)
        {
            EnsureInitialized();
            Vector3 local = worldCentre - terrain.transform.position;
            Vector2 d = travelDirection.sqrMagnitude > 1e-6f ? travelDirection.normalized : Vector2.right;
            double added = solver.AddSource(new TsunamiSource
            {
                centerX = local.x, centerZ = local.z, dirX = d.x, dirZ = d.y,
                amplitude = amplitude, lengthRadius = lengthRadius, widthRadius = widthRadius,
                profile = profile, motion = motion
            });
            StepCount++;
            return added;
        }

        /// <summary>True when any cell's depth changed noticeably in the last step (water not at rest).</summary>
        public bool WaterMoving => solver != null && solver.MaxDepthChange > 1e-5f;

        /// <summary>Changes whenever the still open ocean around the domain changes (reset, sea level).</summary>
        public int OpenOceanVersion => solver != null ? solver.OpenOceanVersion : 0;

        /// <summary>Still open-ocean surface (local Y) at any grid coordinate, including outside the domain.</summary>
        public float OpenOceanHeight(float gx, float gz, out float speed)
        {
            speed = 0f;
            return seaLevel;
        }

        /// <summary>
        /// Open-ocean surface at integer grid coords (-1..Resolution), shared by the flood mesh edge and OceanRing:
        /// the still sea plus, on the side it comes from, the incoming wave (so the edge band of the flood mesh
        /// blends into the same wave the swell draws beyond the domain).
        /// </summary>
        public float OceanAt(int x, int z, out float speed)
        {
            speed = 0f;
            if (solver == null || !solver.Incoming.active) return seaLevel;
            float half = (terrain.Resolution - 1) * 0.5f;
            float px = (x - half) * terrain.CellSize, pz = (z - half) * terrain.CellSize;
            IncomingWave w = solver.Incoming;
            if (px * w.dirX + pz * w.dirZ >= 0f) return seaLevel;
            float eta = w.Elevation(px, pz, solver.Time);
            speed = IncomingWave.FlowSpeed(eta, OpenOceanShaderDepth, gravity);
            return seaLevel + eta;
        }

        // ------------------------------------------------------------------ incoming wave

        /// <summary>
        /// Sends a long wave in from the open sea: a Gaussian ridge (radii along / across the travel direction)
        /// whose crest is at <paramref name="crestWorldAtStart"/> now and travels at <paramref name="speed"/>; its
        /// height grows from <paramref name="startShare"/> of <paramref name="amplitude"/> to the full amplitude
        /// over <paramref name="growTime"/> seconds. It flows into the domain through the sponge band on its side.
        /// Call right after ResetWater (simulation time 0); ResetWater clears it.
        /// </summary>
        public void StartIncomingWave(Vector3 crestWorldAtStart, Vector2 travelDirection, float speed, float amplitude,
            float startShare, float growTime, float lengthRadius, float widthRadius)
        {
            EnsureInitialized();
            Vector3 local = crestWorldAtStart - terrain.transform.position;
            Vector2 d = travelDirection.sqrMagnitude > 1e-6f ? travelDirection.normalized : Vector2.right;
            solver.Incoming = new IncomingWave
            {
                active = true,
                startX = local.x, startZ = local.z,
                dirX = d.x, dirZ = d.y, speed = speed, amplitude = amplitude,
                startShare = Mathf.Clamp01(startShare), growTime = Mathf.Max(0f, growTime),
                lengthRadius = lengthRadius, widthRadius = widthRadius
            };
            StepCount++;
        }

        public bool IncomingWaveActive => solver != null && solver.Incoming.active;
        /// <summary>The incoming wave (grid-local metres; evaluate it at <see cref="SimTime"/>).</summary>
        public IncomingWave Incoming => solver != null ? solver.Incoming : default;

        /// <summary>Incoming wave's surface elevation above the still sea at a world position, at the current simulation time.</summary>
        public float IncomingElevation(Vector3 world)
        {
            if (solver == null || !solver.Incoming.active) return 0f;
            Vector3 local = world - terrain.transform.position;
            return solver.Incoming.Elevation(local.x, local.z, solver.Time);
        }

        /// <summary>World position of the incoming wave's crest centre at the current simulation time.</summary>
        public Vector3 IncomingCrestWorld
        {
            get
            {
                if (solver == null) return terrain.transform.position;
                solver.Incoming.CrestCentre(solver.Time, out float x, out float z);
                return terrain.transform.position + new Vector3(x, seaLevel, z);
            }
        }

        /// <summary>
        /// Distance (m) from a world point to the domain's outer edge going along <paramref name="direction"/>
        /// (XZ). 0 when the point is outside the domain.
        /// </summary>
        public float DistanceToEdge(Vector3 world, Vector2 direction)
        {
            EnsureInitialized();
            Vector3 c = terrain.transform.position;
            float hx = terrain.HalfExtent;
            float px = world.x - c.x, pz = world.z - c.z;
            if (Mathf.Abs(px) > hx || Mathf.Abs(pz) > hx) return 0f;
            float t = float.MaxValue;
            if (direction.x > 1e-4f) t = Mathf.Min(t, (hx - px) / direction.x);
            if (direction.x < -1e-4f) t = Mathf.Min(t, (-hx - px) / direction.x);
            if (direction.y > 1e-4f) t = Mathf.Min(t, (hx - pz) / direction.y);
            if (direction.y < -1e-4f) t = Mathf.Min(t, (-hx - pz) / direction.y);
            return t == float.MaxValue ? 0f : t;
        }

        /// <summary>Width (m) of the edge band where the flood mesh fades into the open-ocean surface.</summary>
        public float EdgeFadeWidth => edgeFadeCells * (terrain != null ? terrain.CellSize : 1f);

        /// <summary>
        /// Per-cell blend (row-major, n*n): 1 on the two outermost rows (pure open ocean), fading smoothly
        /// to 0 at edgeFadeCells from the edge (pure simulation).
        /// </summary>
        public float[] EdgeBlendField { get { EnsureInitialized(); return edgeBlend; } }

        public float EdgeBlend(int x, int z) => EdgeBlendField[z * terrain.Resolution + x];

        void BuildEdgeBlend()
        {
            int n = terrain.Resolution;
            edgeBlend = new float[n * n];
            for (int z = 0; z < n; z++)
            {
                for (int x = 0; x < n; x++)
                {
                    int e = Mathf.Min(Mathf.Min(x, z), Mathf.Min(n - 1 - x, n - 1 - z));
                    float w;
                    if (e <= 1) w = 1f;
                    else if (e >= edgeFadeCells) w = 0f;
                    else { float t = (e - 1f) / (edgeFadeCells - 1f); w = 1f - t * t * (3f - 2f * t); }
                    edgeBlend[z * n + x] = w;
                }
            }
        }

        /// <summary>
        /// Adds a thin wall between two cell corners (world positions, e.g. the ends of a player-built wall segment
        /// on a grid aligned with this simulation). Diagonal segments become a two-face staircase.
        /// <paramref name="topWorldY"/> is the physical wall top. Impermeable player walls remain sealed until removed;
        /// other barriers allow water above their crest to spill over.
        /// </summary>
        public WallSegmentHandle AddWallSegment(Vector3 worldA, Vector3 worldB, float topWorldY, float bottomWorldY = float.NegativeInfinity, bool impermeable = false)
        {
            EnsureInitialized();
            float top = topWorldY - terrain.transform.position.y;
            // Cell corners sit half a cell from the samples: corner (u,v) is the lower-left corner of cell (u,v).
            Vector2 ga = terrain.WorldToGrid(worldA), gb = terrain.WorldToGrid(worldB);
            var a = new Vector2Int(Mathf.RoundToInt(ga.x + 0.5f), Mathf.RoundToInt(ga.y + 0.5f));
            var b = new Vector2Int(Mathf.RoundToInt(gb.x + 0.5f), Mathf.RoundToInt(gb.y + 0.5f));
            int id = nextWallHandle++;
            if (nextWallHandle <= 0) nextWallHandle = 1;
            var wall = new RegisteredWall { id = id, worldA = worldA, worldB = worldB, topLocal = top,
                bottomLocal = bottomWorldY - terrain.transform.position.y, impermeable = impermeable };
            WallBarrierRasterizer.Trace(a.x, a.y, b.x, b.y, (x,z,alongX) => AddFace(x,z,alongX,top,wall));
            if (wall.faces.Count == 0) return default;
            walls.Add(id, wall);
            wallIteration.Add(wall);
            return new WallSegmentHandle(id);
        }

        void AddFace(int x, int z, bool alongX, float top, RegisteredWall wall)
        {
            if (x < 0 || z < 0 || x >= solver.Size || z >= solver.Size ||
                (alongX ? x >= solver.Size - 1 : z >= solver.Size - 1)) return;
            solver.SetOwnedBarrier(wall.id, x, z, alongX, top, wall.impermeable);
            wall.faces.Add(new WallFace(x, z, alongX));
        }

        /// <summary>Removes one registered segment and rebuilds shared faces so adjacent walls remain intact.</summary>
        public bool RemoveWallSegment(WallSegmentHandle handle)
        {
            EnsureInitialized();
            if (!handle.IsValid || !walls.Remove(handle.Value)) return false;
            for (int i = 0; i < wallIteration.Count; i++)
                if (wallIteration[i].id == handle.Value) { wallIteration.RemoveAt(i); break; }
            solver.RemoveBarrierOwner(handle.Value);
            return true;
        }

        /// <summary>Removes all thin walls and their segment registrations.</summary>
        public void ClearWalls()
        {
            EnsureInitialized();
            solver.ClearBarrierOwners();
            walls.Clear();
            wallIteration.Clear();
        }

        void SampleWallLoads(float dt)
        {
            Action<WallLoadSample> loadCallback = WallLoadSampled;
            Action<WallFaceImpactSample> impactCallback = WallFaceImpactSampled;
            if ((loadCallback == null && impactCallback == null) || wallIteration.Count == 0) return;
            float[] bed = terrain.Heights;
            int n = solver.Size;
            impactSampleSequence++;
            if (impactSampleSequence <= 0) impactSampleSequence = 1;
            for (int w = 0; w < wallIteration.Count; w++)
            {
                RegisteredWall wall = wallIteration[w];
                float impact = 0f, sustained = 0f;
                float strongestImpact = 0f, strongestHydro = 0f;
                WallFace impactFace = default, hydroFace = default;
                Vector3 impactGridInto = Vector3.zero, hydroGridInto = Vector3.zero;
                float impactSurface = 0f;
                for (int f = 0; f < wall.faces.Count; f++)
                {
                    WallFace face = wall.faces[f];
                    int low = face.z * n + face.x;
                    int high = face.alongX ? low + 1 : low + n;
                    float lowInto = face.alongX ? solver.VelocityX[low] : solver.VelocityZ[low];
                    float highInto = face.alongX ? -solver.VelocityX[high] : -solver.VelocityZ[high];
                    float lowDepth = solver.Depth[low], highDepth = solver.Depth[high];
                    float lowSurface = bed[low] + lowDepth, highSurface = bed[high] + highDepth;
                    float lowBottom = Mathf.Max(bed[low], wall.bottomLocal);
                    float highBottom = Mathf.Max(bed[high], wall.bottomLocal);
                    float lowWet = Mathf.Clamp(lowSurface - lowBottom, 0f, Mathf.Max(0f, wall.topLocal - lowBottom));
                    float highWet = Mathf.Clamp(highSurface - highBottom, 0f, Mathf.Max(0f, wall.topLocal - highBottom));
                    float lowSpeed = Mathf.Max(0f, lowInto), highSpeed = Mathf.Max(0f, highInto);
                    WallFaceLoad load = WallFaceLoad.Calculate(WaterDensity, gravity, solver.CellSize,
                        lowWet, lowSpeed, highWet, highSpeed);
                    impact += load.ImpactForceNewtons;
                    sustained += load.SustainedForceNewtons;

                    float faceImpact = Mathf.Max(load.LowImpactForceNewtons, load.HighImpactForceNewtons);
                    if (faceImpact > 0f)
                    {
                        bool useLow = load.LowImpactForceNewtons >= load.HighImpactForceNewtons;
                        float sampleDepth = useLow ? lowDepth : highDepth;
                        float sampleSpeed = useLow ? lowSpeed : highSpeed;
                        float sampleSurface = useLow ? lowSurface : highSurface;
                        float sign = useLow ? 1f : -1f;
                        Vector3 gridInto = face.alongX ? new Vector3(sign, 0f, 0f) : new Vector3(0f, 0f, sign);
                        if (faceImpact > strongestImpact)
                        {
                            strongestImpact = faceImpact;
                            impactFace = face;
                            impactGridInto = gridInto;
                            impactSurface = sampleSurface;
                        }
                        if (impactCallback != null)
                            EmitFaceImpact(impactCallback, wall, face, low, gridInto, sampleSurface, sampleDepth,
                                sampleSpeed, faceImpact);
                    }

                    if (load.SustainedForceNewtons > strongestHydro)
                    {
                        strongestHydro = load.SustainedForceNewtons;
                        hydroFace = face;
                        float sign = load.LowHydroForceNewtons >= load.HighHydroForceNewtons ? 1f : -1f;
                        hydroGridInto = face.alongX ? new Vector3(sign, 0f, 0f) : new Vector3(0f, 0f, sign);
                    }
                }
                if (loadCallback != null && (impact > 0f || sustained > 0f))
                {
                    bool hasImpact = strongestImpact > 0f;
                    Vector3 position, normal;
                    if (hasImpact)
                        SegmentContact(wall, impactFace, impactGridInto, impactSurface, false, out position, out normal);
                    else
                        SegmentContact(wall, hydroFace, hydroGridInto, 0f, true, out position, out normal);
                    loadCallback(new WallLoadSample(new WallSegmentHandle(wall.id), impact, sustained, dt,
                        position, normal, hasImpact));
                }
            }
        }

        void EmitFaceImpact(Action<WallFaceImpactSample> callback, RegisteredWall wall, WallFace face, int faceIndex,
            Vector3 gridInto, float surfaceLocal, float depth, float speed, float force)
        {
            SegmentContact(wall, face, gridInto, surfaceLocal, false, out Vector3 position, out Vector3 realNormal);
            WallImpactDelivery.Deliver(callback, new WallFaceImpactSample(new WallSegmentHandle(wall.id), position,
                realNormal, gridInto, depth, speed, solver.CellSize, force, impactSampleSequence, faceIndex,
                face.alongX));
        }

        void SegmentContact(RegisteredWall wall, WallFace face, Vector3 gridInto, float surfaceLocal,
            bool segmentCentre, out Vector3 position, out Vector3 realNormal)
        {
            Vector3 segment = wall.worldB - wall.worldA;
            segment.y = 0f;
            float lengthSq = segment.sqrMagnitude;
            if (segmentCentre)
            {
                position = Vector3.Lerp(wall.worldA, wall.worldB, 0.5f);
                position.y = (position.y + terrain.transform.position.y + wall.topLocal) * 0.5f;
            }
            else
            {
                Vector3 faceWorld = FaceWorldPosition(face, surfaceLocal);
                float t = lengthSq > 1e-6f
                    ? Mathf.Clamp01(Vector3.Dot(faceWorld - wall.worldA, segment) / lengthSq)
                    : 0.5f;
                position = Vector3.Lerp(wall.worldA, wall.worldB, t);
                position.y = Mathf.Min(faceWorld.y, terrain.transform.position.y + wall.topLocal);
            }
            Vector3 tangent = lengthSq > 1e-6f ? segment / Mathf.Sqrt(lengthSq) : Vector3.right;
            realNormal = new Vector3(-tangent.z, 0f, tangent.x);
            if (Vector3.Dot(realNormal, gridInto) < 0f) realNormal = -realNormal;
        }

        Vector3 FaceWorldPosition(WallFace face, float surfaceLocal)
        {
            float half = (solver.Size - 1) * 0.5f;
            float gx = face.x + (face.alongX ? 0.5f : 0f);
            float gz = face.z + (face.alongX ? 0f : 0.5f);
            Vector3 origin = terrain.transform.position;
            return new Vector3(origin.x + (gx - half) * solver.CellSize, origin.y + surfaceLocal,
                origin.z + (gz - half) * solver.CellSize);
        }

        /// <summary>Water depth (m) at a world position, bilinearly interpolated.</summary>
        public float SampleDepth(Vector3 world) => terrain.SampleField(Depth, world);

        /// <summary>Water left on land seeps away: each land cell keeps (1 - fraction) of it (see the solver).</summary>
        public void DrainLand(float fraction)
        {
            Solver.DrainLand(fraction);
            StepCount++;
        }

        /// <summary>Total water volume in the grid (m^3). Diagnostic; iterates every cell.</summary>
        public double TotalVolume() => Solver.TotalVolume();

        [ContextMenu("Log Water Volume")]
        void LogVolume()
        {
            Debug.Log($"[WaterSimulation] t={SimTime:F1} s, volume={TotalVolume():F1} m^3, last dt={Solver.LastTimestep:F4} s, " +
                      $"steps/frame={StepsLastFrame}, solver {SolverMsLastFrame:F2} ms", this);
        }
    }
}
