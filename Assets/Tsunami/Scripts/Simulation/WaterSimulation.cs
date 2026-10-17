using UnityEngine;

namespace Tsunami
{
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

            solver = new TsunamiPhysicsSolver(terrain.Resolution, terrain.CellSize) { SpongeCells = spongeCells };
            PushSettings();
            solver.ResetToSeaLevel(terrain.Heights);
            BuildEdgeBlend();
            StepCount++;
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

        /// <summary>Open-ocean surface at integer grid coords (-1..Resolution), shared by the flood mesh edge and OceanRing.</summary>
        public float OceanAt(int x, int z, out float speed)
        {
            speed = 0f;
            return seaLevel;
        }

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
        /// <paramref name="topWorldY"/> is the world height of the wall top; water above it spills over.
        /// </summary>
        public void AddWallSegment(Vector3 worldA, Vector3 worldB, float topWorldY)
        {
            EnsureInitialized();
            float top = topWorldY - terrain.transform.position.y;
            // Cell corners sit half a cell from the samples: corner (u,v) is the lower-left corner of cell (u,v).
            Vector2 ga = terrain.WorldToGrid(worldA), gb = terrain.WorldToGrid(worldB);
            var a = new Vector2Int(Mathf.RoundToInt(ga.x + 0.5f), Mathf.RoundToInt(ga.y + 0.5f));
            var b = new Vector2Int(Mathf.RoundToInt(gb.x + 0.5f), Mathf.RoundToInt(gb.y + 0.5f));
            var corner = new Vector2Int(b.x, a.y);
            WallAlongX(a.y, a.x, corner.x, top);      // horizontal leg a -> corner
            WallAlongZ(corner.x, corner.y, b.y, top); // vertical leg corner -> b
        }

        // Horizontal edge on corner row v between corner columns u0..u1: separates cells (u, v-1) and (u, v).
        void WallAlongX(int v, int u0, int u1, float top)
        {
            if (u0 > u1) { int t = u0; u0 = u1; u1 = t; }
            for (int u = u0; u < u1; u++) solver.SetBarrier(u, v - 1, false, top);
        }

        // Vertical edge on corner column u between corner rows v0..v1: separates cells (u-1, v) and (u, v).
        void WallAlongZ(int u, int v0, int v1, float top)
        {
            if (v0 > v1) { int t = v0; v0 = v1; v1 = t; }
            for (int v = v0; v < v1; v++) solver.SetBarrier(u - 1, v, true, top);
        }

        /// <summary>Removes all thin walls.</summary>
        public void ClearWalls()
        {
            EnsureInitialized();
            solver.ClearBarriers();
        }

        /// <summary>Water depth (m) at a world position, bilinearly interpolated.</summary>
        public float SampleDepth(Vector3 world) => terrain.SampleField(Depth, world);

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
