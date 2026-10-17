using UnityEngine;

namespace Tsunami
{
    /// <summary>
    /// Runs the ShallowWaterSolver on the TerrainGrid with a fixed timestep, independent of framerate.
    /// The solver keeps running whether or not a tsunami source is active.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public class WaterSimulation : MonoBehaviour
    {
        [SerializeField] TerrainGrid terrain;

        [Header("Timestep")]
        [Tooltip("Fixed solver timestep (s). Must stay well below cellSize / waveSpeed; see StabilityNumber.")]
        [SerializeField, Range(0.005f, 0.05f)] float timestep = 1f / 60f;
        [Tooltip("Upper bound on solver steps per rendered frame. Prevents a spiral of death on slow machines " +
                 "(the simulation slows down instead).")]
        [SerializeField, Range(1, 16)] int maxStepsPerFrame = 4;
        [SerializeField] bool paused;

        [Header("Flow")]
        [SerializeField] float gravity = 9.81f;
        // Field names differ from the first prototype on purpose, so scenes built with the old (plateau)
        // tuning pick up these defaults instead of the old serialized values.
        [Tooltip("Wave-speed multiplier: waves travel at about sqrt(g * depth * waveSpeed). 1 = physical.")]
        [SerializeField, Range(0.25f, 4f)] float waveSpeed = 1f;
        [Tooltip("Bed friction (1/s at 1 m depth, scaled by 1/depth^2). Higher = floods settle faster, waves lose more height.")]
        [SerializeField, Range(0f, 3f)] float bedFriction = 0.3f;
        [Tooltip("Extra drag (1/s) on water flowing over land, so floods settle instead of sloshing.")]
        [SerializeField, Range(0f, 2f)] float landDrag = 0.25f;
        [Tooltip("Small drag (1/s) everywhere so leftover ocean currents die out.")]
        [SerializeField, Range(0f, 0.5f)] float baseDrag = 0.04f;
        [Tooltip("Depth (m) below which a cell is treated as dry for rendering and speed estimates.")]
        [SerializeField, Range(0.001f, 0.1f)] float wetThreshold = 0.01f;

        [Header("Ocean")]
        [SerializeField] float seaLevel = 0f;
        [Tooltip("Grid edge cells act as open ocean held at sea level: water can drain out and the surge can flow in.")]
        [SerializeField] bool openOceanBoundaries = true;
        [Tooltip("Damping (1/s) in a 5-cell band along the edges so waves leave the grid instead of reflecting.")]
        [SerializeField, Range(0f, 10f)] float edgeAbsorption = 3f;

        [Header("Open-ocean blending (visual only)")]
        [Tooltip("Cells from the grid edge over which the rendered water fades into the open-ocean surface. " +
                 "The simulation boundary (absorbing band, fixed sea level) lives inside this band, so the square " +
                 "outline of the grid is never visible.")]
        [SerializeField, Range(3, 24)] int edgeFadeCells = 8;
        [Tooltip("The incoming wave out at sea fades between this distance and twice this distance beyond the source edge (m).")]
        [SerializeField, Min(10f)] float waveVisibleDistance = 60f;

        /// <summary>Depth value the shader receives for open ocean (deep colour, no shoreline foam).</summary>
        public const float OpenOceanShaderDepth = 50f;

        ShallowWaterSolver solver;
        float accumulator;

        // Cached open-ocean surface for grid coords -1..n (one ring beyond the grid), edge band only.
        float[] oceanHeight;
        float[] oceanSpeed;
        float[] edgeBlend;
        int oceanFieldVersion = int.MinValue;

        public TerrainGrid Terrain => terrain;
        public float SeaLevel => seaLevel;
        public float WetThreshold => wetThreshold;
        public bool Paused { get => paused; set => paused = value; }
        /// <summary>Incremented after every solver step; renderers use it to skip redundant updates.</summary>
        public int StepCount { get; private set; }
        public ShallowWaterSolver Solver { get { EnsureInitialized(); return solver; } }
        public float[] Depth => Solver.Depth;
        public float[] Speed => Solver.Speed;

        /// <summary>
        /// Courant number for a 15 m deep wave: dt * sqrt(g * 15 * waveSpeed) / cellSize. Keep below ~0.5
        /// (defaults give ~0.1).
        /// </summary>
        public float StabilityNumber => terrain == null ? 0f : timestep * Mathf.Sqrt(gravity * 15f * waveSpeed) / terrain.CellSize;

        void Awake()
        {
            EnsureInitialized();
        }

        void OnValidate()
        {
            if (terrain != null && StabilityNumber > 0.5f)
                Debug.LogWarning($"[WaterSimulation] Stability number {StabilityNumber:F2} is high; lower timestep or waveSpeed.", this);
            if (solver != null)
            {
                PushSettings();
                BuildEdgeBlend();
            }
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

            solver = new ShallowWaterSolver(terrain.Resolution, terrain.CellSize);
            PushSettings();
            solver.ResetToSeaLevel(terrain.Heights);
            BuildEdgeBlend();
            StepCount++;
        }

        void PushSettings()
        {
            solver.Gravity = gravity;
            solver.FlowSpeed = waveSpeed;
            solver.Friction = bedFriction;
            solver.SpongeStrength = edgeAbsorption;
            solver.LandDrag = landDrag;
            solver.BaseDrag = baseDrag;
            solver.WetThreshold = wetThreshold;
            solver.SeaLevel = seaLevel;
            solver.OpenOceanBoundaries = openOceanBoundaries;
        }

        void Update()
        {
            if (solver == null || paused) return;
            accumulator += Time.deltaTime;
            int steps = 0;
            float[] h = terrain.Heights;
            while (accumulator >= timestep && steps < maxStepsPerFrame)
            {
                solver.Step(h, timestep);
                accumulator -= timestep;
                steps++;
            }
            if (steps == maxStepsPerFrame && accumulator > timestep) accumulator = 0f; // drop backlog
            if (steps > 0) StepCount++;
            RefreshOceanField();
        }

        /// <summary>Clears all simulated water and refills the surrounding ocean to sea level.</summary>
        public void ResetWater()
        {
            EnsureInitialized();
            solver.ResetToSeaLevel(terrain.Heights);
            RefreshOceanField();
            accumulator = 0f;
            StepCount++;
        }

        /// <summary>
        /// Open-ocean water surface (local Y, relative to the terrain transform) at continuous grid coordinates,
        /// which may lie outside the grid. Used by both the flood mesh edge band and the surrounding ocean, so the
        /// two always match exactly at the grid edge. <paramref name="speed"/> is the long-wave particle speed.
        /// </summary>
        public float OpenOceanHeight(float gx, float gz, out float speed)
        {
            ShallowWaterSolver s = solver ?? Solver;
            s.SourceFrame(gx, gz, out float distance, out float along);
            float eta = s.IncomingWaveAt(distance, Mathf.Clamp01(along));
            if (distance < -waveVisibleDistance) eta *= Mathf.Clamp01(2f + distance / waveVisibleDistance);
            speed = Mathf.Max(0f, eta) * Mathf.Sqrt(s.Gravity / Mathf.Max(1f, s.ReferenceDepth));
            return s.SeaLevel + s.CurrentBoundaryOffset + eta;
        }

        /// <summary>True when any cell's depth changed noticeably in the last step (water not at rest).</summary>
        public bool WaterMoving => solver != null && solver.MaxDepthChange > 1e-5f;

        /// <summary>Changes whenever the cached open-ocean surface changes.</summary>
        public int OpenOceanVersion => solver != null ? solver.OpenOceanVersion : 0;

        /// <summary>
        /// Cached OpenOceanHeight at integer grid coords -1..Resolution. Valid for the edge band and the ring
        /// just outside the grid (elsewhere it returns sea level).
        /// </summary>
        public float OceanAt(int x, int z, out float speed)
        {
            RefreshOceanField();
            int i = (z + 1) * (terrain.Resolution + 2) + (x + 1);
            speed = oceanSpeed[i];
            return oceanHeight[i];
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
            int m = (n + 2) * (n + 2);
            oceanHeight = new float[m];
            oceanSpeed = new float[m];
            oceanFieldVersion = int.MinValue;
        }

        void RefreshOceanField()
        {
            if (solver == null || oceanFieldVersion == solver.OpenOceanVersion) return;
            oceanFieldVersion = solver.OpenOceanVersion;
            int n = terrain.Resolution;
            int w = n + 2;
            for (int z = -1; z <= n; z++)
            {
                for (int x = -1; x <= n; x++)
                {
                    bool outside = x < 0 || z < 0 || x >= n || z >= n;
                    if (!outside && edgeBlend[z * n + x] <= 0f) continue; // interior: not needed
                    int i = (z + 1) * w + (x + 1);
                    oceanHeight[i] = OpenOceanHeight(x, z, out oceanSpeed[i]);
                }
            }
        }

        /// <summary>Water depth (m) at a world position, bilinearly interpolated.</summary>
        public float SampleDepth(Vector3 world) => terrain.SampleField(Depth, world);

        /// <summary>Total water volume in the grid (m^3). Diagnostic; iterates every cell.</summary>
        public double TotalVolume() => Solver.TotalVolume();

        [ContextMenu("Log Water Volume")]
        void LogVolume()
        {
            Debug.Log($"[WaterSimulation] volume={TotalVolume():F1} m^3, boundary exchange={Solver.BoundaryVolumeExchange:F1} m^3, stability={StabilityNumber:F3}", this);
        }
    }
}
