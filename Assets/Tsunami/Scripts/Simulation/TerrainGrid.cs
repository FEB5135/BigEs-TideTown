using System;
using UnityEngine;

namespace Tsunami
{
    /// <summary>
    /// Heightfield shared by the water solver and the visible island mesh.
    /// One height sample per simulation cell; sample (x, z) sits at GridToWorld(x, z).
    /// The grid is axis-aligned and centred on this transform (rotation and scale are ignored).
    /// </summary>
    [ExecuteAlways]
    [DefaultExecutionOrder(-100)]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class TerrainGrid : MonoBehaviour
    {
        [Serializable]
        public struct Hill
        {
            [Tooltip("Centre in normalised grid space (-1..1).")]
            public Vector2 center;
            [Tooltip("Height added at the centre (negative digs a basin).")]
            public float height;
            [Tooltip("Radius in normalised grid space.")]
            public float radius;

            public Hill(float x, float z, float height, float radius)
            {
                center = new Vector2(x, z);
                this.height = height;
                this.radius = radius;
            }
        }

        [Header("Grid (shared with WaterSimulation)")]
        [Tooltip("Samples per side. 96 is the default; on very weak machines use 64 with cellSize 2 (same island size).")]
        [SerializeField, Range(16, 256)] int resolution = 96;
        [Tooltip("World-space distance between samples (m). resolution * cellSize sets the island size (~127 m).")]
        [SerializeField, Min(0.25f)] float cellSize = 4f / 3f;

        [Header("Procedural island")]
        [SerializeField] int seed = 7;
        [Tooltip("Open-ocean floor. Deep water lets big waves travel fast and then rear up over the shallow coast.")]
        [SerializeField] float seaFloor = -12f;
        [Tooltip("Ground height just behind the beach.")]
        [SerializeField] float coastHeight = 1.2f;
        [Tooltip("Extra height gained towards the north (+Z) side of the island.")]
        [SerializeField] float inlandRise = 0.8f;
        [SerializeField, Range(0.3f, 1f)] float islandRadius = 0.78f;
        [SerializeField, Range(0.05f, 0.5f)] float beachWidth = 0.25f;
        [SerializeField] float noiseAmplitude = 0.6f;
        [SerializeField] float noiseScale = 3.5f;
        [SerializeField] Hill[] hills =
        {
            new Hill(0.05f, 0.4f, 26f, 0.24f),    // central mountain
            new Hill(-0.46f, 0.0f, 16f, 0.2f),    // west ridge
            new Hill(0.46f, -0.05f, 15f, 0.19f),  // east ridge
            new Hill(-0.25f, 0.58f, 12f, 0.16f),  // north hill
            new Hill(0.2f, -0.46f, 5f, 0.07f),    // small knoll on the beach (water flows around it)
            new Hill(0.24f, -0.22f, -2f, 0.08f)   // inland hollow (water collects here)
        };

        [Header("Optional: bake heights from existing scene geometry")]
        [Tooltip("Raycast down onto colliders (e.g. a teammate's island) instead of using the procedural island.")]
        [SerializeField] bool bakeFromColliders;
        [SerializeField] LayerMask bakeLayers = ~0;
        [SerializeField] float bakeRayHeight = 200f;

        [Header("Editing limits")]
        [SerializeField] float minHeight = -14f;
        [SerializeField] float maxHeight = 35f;

        [Header("Vertex colours (read by Tsunami/LowPolyTerrain)")]
        [SerializeField] Color seabedColor = new Color(0.55f, 0.5f, 0.38f);
        [SerializeField] Color sandColor = new Color(0.93f, 0.83f, 0.58f);
        [SerializeField] Color grassColor = new Color(0.45f, 0.62f, 0.27f);
        [SerializeField] Color stoneColor = new Color(0.5f, 0.48f, 0.46f);
        [SerializeField] Color highGrassColor = new Color(0.36f, 0.52f, 0.24f);
        [SerializeField] float sandTop = 1.6f;
        [Tooltip("Height where grass gives way to bare stone.")]
        [SerializeField] float stoneFromHeight = 17f;
        [Tooltip("Slopes steeper than this (1 - normal.y) turn to stone, e.g. freshly raised walls.")]
        [SerializeField, Range(0.2f, 0.9f)] float stoneSlope = 0.55f;

        float[] heights;
        float[] initialHeights;
        Mesh mesh;
        Vector3[] vertices;
        Vector3[] normals;
        Color[] colors;
        bool meshDirty;
        bool regenerateRequested;
        bool externalHeights;
        bool[] renderCover;      // cells drawn by another system's ground (e.g. the team's map)
        float coverSink = 0.08f;
        bool trianglesDirty = true;

        public int Resolution => resolution;
        public float CellSize => cellSize;
        public float MinHeight => minHeight;
        public float MaxHeight => maxHeight;
        /// <summary>Live height array (row-major, index = z * Resolution + x). Do not resize.</summary>
        public float[] Heights { get { EnsureInitialized(); return heights; } }
        /// <summary>Half the world-space width of the grid, measured between edge samples.</summary>
        public float HalfExtent => (resolution - 1) * 0.5f * cellSize;
        /// <summary>Incremented whenever heights change.</summary>
        public int Version { get; private set; }

        void OnEnable()
        {
            EnsureInitialized();
        }

        void OnDisable()
        {
            if (mesh != null)
            {
                if (Application.isPlaying) Destroy(mesh); else DestroyImmediate(mesh);
                mesh = null;
            }
        }

        void OnValidate()
        {
            if (externalHeights) return; // heights come from another system (e.g. TeamMapTsunamiAdapter)
            regenerateRequested = true;
        }

        void Update()
        {
            if (regenerateRequested && !Application.isPlaying)
            {
                regenerateRequested = false;
                Generate();
            }
        }

        void LateUpdate()
        {
            if (meshDirty) RebuildMesh();
        }

        public void EnsureInitialized()
        {
            if (heights == null || heights.Length != resolution * resolution) Generate();
            else if (mesh == null) RebuildMesh();
        }

        /// <summary>Regenerates the heightfield from the procedural settings (or bakes it from colliders).</summary>
        public void Generate()
        {
            int n = resolution;
            heights = new float[n * n];
            vertices = null;
            if (bakeFromColliders) BakeFromColliders();
            else GenerateIsland();
            initialHeights = (float[])heights.Clone();
            Version++;
            RebuildMesh();
        }

        /// <summary>Restores the heights produced by the last Generate() call.</summary>
        /// <summary>True when heights were supplied by UseExternalHeights instead of the procedural island.</summary>
        public bool HasExternalHeights => externalHeights;

        /// <summary>
        /// Replaces the procedural island with heights from another system (e.g. the team's map).
        /// Heights are relative to this transform's Y, row-major, one per cell (length = newResolution^2).
        /// </summary>
        public void UseExternalHeights(int newResolution, float newCellSize, float[] newHeights)
        {
            if (newHeights == null || newHeights.Length != newResolution * newResolution)
                throw new ArgumentException("Height array must have resolution * resolution entries.", nameof(newHeights));
            externalHeights = true;
            regenerateRequested = false;
            resolution = newResolution;
            cellSize = newCellSize;
            heights = (float[])newHeights.Clone();
            initialHeights = (float[])newHeights.Clone();
            vertices = null;
            Version++;
            RebuildMesh();
        }

        /// <summary>
        /// Marks cells whose ground is drawn by something else (e.g. the team's map tiles). Quads entirely inside
        /// are not drawn; covered vertices next to visible ones are tucked <paramref name="sink"/> below the surface,
        /// so the seabed meets the other ground without z-fighting. Null draws everything.
        /// </summary>
        public void SetRenderCover(bool[] covered, float sink = 0.08f)
        {
            renderCover = covered != null && covered.Length == resolution * resolution ? covered : null;
            coverSink = sink;
            trianglesDirty = true;
            RebuildMesh();
        }

        public void ResetTerrain()
        {
            if (initialHeights == null) return;
            Array.Copy(initialHeights, heights, heights.Length);
            Version++;
            meshDirty = true;
        }

        void GenerateIsland()
        {
            int n = resolution;
            float half = (n - 1) * 0.5f;
            float ox = (seed * 12.9898f) % 997f;
            float oz = (seed * 78.233f) % 991f;
            for (int z = 0; z < n; z++)
            {
                for (int x = 0; x < n; x++)
                {
                    float u = (x - half) / half;
                    float v = (z - half) / half;
                    float r = Mathf.Sqrt(u * u + v * v * 1.15f);
                    float land = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(islandRadius, islandRadius - beachWidth, r));
                    float h = Mathf.Lerp(seaFloor, coastHeight, land) + land * (v + 1f) * inlandRise;
                    for (int k = 0; k < hills.Length; k++)
                    {
                        Hill hill = hills[k];
                        if (hill.radius <= 0f) continue;
                        float dx = u - hill.center.x, dz = v - hill.center.y;
                        h += land * hill.height * Mathf.Exp(-(dx * dx + dz * dz) / (hill.radius * hill.radius));
                    }
                    h += land * (Mathf.PerlinNoise(ox + u * noiseScale, oz + v * noiseScale) - 0.5f) * 2f * noiseAmplitude;
                    heights[z * n + x] = Mathf.Clamp(h, minHeight, maxHeight);
                }
            }
        }

        void BakeFromColliders()
        {
            int n = resolution;
            for (int z = 0; z < n; z++)
            {
                for (int x = 0; x < n; x++)
                {
                    Vector3 p = GridToWorld(x, z, 0f);
                    p.y = transform.position.y + bakeRayHeight;
                    float h = seaFloor;
                    if (Physics.Raycast(p, Vector3.down, out RaycastHit hit, bakeRayHeight * 2f, bakeLayers, QueryTriggerInteraction.Ignore))
                        h = hit.point.y - transform.position.y;
                    heights[z * n + x] = Mathf.Clamp(h, minHeight, maxHeight);
                }
            }
        }

        // ---------------------------------------------------------------- Coordinates

        public int Index(int x, int z) => z * resolution + x;

        public Vector3 GridToWorld(int x, int z, float height)
        {
            float half = (resolution - 1) * 0.5f;
            Vector3 o = transform.position;
            return new Vector3(o.x + (x - half) * cellSize, o.y + height, o.z + (z - half) * cellSize);
        }

        public Vector3 GridToWorld(int x, int z) => GridToWorld(x, z, Heights[z * resolution + x]);

        /// <summary>Continuous grid coordinates (not clamped). Integer values land exactly on samples.</summary>
        public Vector2 WorldToGrid(Vector3 world)
        {
            float half = (resolution - 1) * 0.5f;
            Vector3 o = transform.position;
            return new Vector2((world.x - o.x) / cellSize + half, (world.z - o.z) / cellSize + half);
        }

        public bool Contains(Vector3 world)
        {
            Vector2 g = WorldToGrid(world);
            return g.x >= 0f && g.y >= 0f && g.x <= resolution - 1 && g.y <= resolution - 1;
        }

        /// <summary>Bilinear sample of any per-cell array (heights, water depth...) at a world position.</summary>
        public float SampleField(float[] field, Vector3 world)
        {
            Vector2 g = WorldToGrid(world);
            int n = resolution;
            float gx = Mathf.Clamp(g.x, 0f, n - 1.001f);
            float gz = Mathf.Clamp(g.y, 0f, n - 1.001f);
            int x0 = (int)gx, z0 = (int)gz;
            float tx = gx - x0, tz = gz - z0;
            int i = z0 * n + x0;
            float a = Mathf.Lerp(field[i], field[i + 1], tx);
            float b = Mathf.Lerp(field[i + n], field[i + n + 1], tx);
            return Mathf.Lerp(a, b, tz);
        }

        /// <summary>Terrain height (world Y) at a world position.</summary>
        public float SampleHeight(Vector3 world) => transform.position.y + SampleField(Heights, world);

        /// <summary>Ray-marches the heightfield; no collider required.</summary>
        public bool Raycast(Ray ray, float maxDistance, out Vector3 hitPoint)
        {
            hitPoint = default;
            if (ray.direction.y >= 0f) return false; // god camera always looks down
            // Only march the part of the ray between the highest and lowest possible terrain.
            float top = transform.position.y + maxHeight, bottom = transform.position.y + minHeight;
            float tStart = Mathf.Max(0f, (ray.origin.y - top) / -ray.direction.y);
            float tEnd = Mathf.Min(maxDistance, (ray.origin.y - bottom) / -ray.direction.y);
            float step = cellSize * 0.5f;
            Vector3 start = ray.origin + ray.direction * tStart;
            float prevDiff = start.y - SampleHeight(start);
            if (prevDiff <= 0f) return false;
            for (float t = tStart + step; t <= tEnd; t += step)
            {
                Vector3 p = ray.origin + ray.direction * t;
                float diff = p.y - SampleHeight(p);
                if (diff <= 0f)
                {
                    float a = prevDiff / (prevDiff - diff);
                    hitPoint = ray.origin + ray.direction * (t - step + step * a);
                    return Contains(hitPoint);
                }
                prevDiff = diff;
            }
            return false;
        }

        // ---------------------------------------------------------------- Editing

        /// <summary>
        /// Raises (amount &gt; 0) or lowers terrain with a smooth circular falloff.
        /// Water depth is left untouched, so editing under water moves water rather than creating it.
        /// </summary>
        public bool ModifyHeights(Vector3 worldCenter, float worldRadius, float amount)
        {
            if (Mathf.Approximately(amount, 0f) || worldRadius <= 0f) return false;
            float[] h = Heights;
            int n = resolution;
            Vector2 c = WorldToGrid(worldCenter);
            float rCells = worldRadius / cellSize;
            int x0 = Mathf.Max(0, Mathf.FloorToInt(c.x - rCells));
            int x1 = Mathf.Min(n - 1, Mathf.CeilToInt(c.x + rCells));
            int z0 = Mathf.Max(0, Mathf.FloorToInt(c.y - rCells));
            int z1 = Mathf.Min(n - 1, Mathf.CeilToInt(c.y + rCells));
            bool changed = false;
            float invR2 = 1f / (rCells * rCells);
            for (int z = z0; z <= z1; z++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    float dx = x - c.x, dz = z - c.y;
                    float t = 1f - (dx * dx + dz * dz) * invR2;
                    if (t <= 0f) continue;
                    int i = z * n + x;
                    float nh = Mathf.Clamp(h[i] + amount * t * t, minHeight, maxHeight);
                    if (nh != h[i]) { h[i] = nh; changed = true; }
                }
            }
            if (changed)
            {
                Version++;
                meshDirty = true;
            }
            return changed;
        }

        // ---------------------------------------------------------------- Mesh

        void RebuildMesh()
        {
            meshDirty = false;
            int n = resolution;
            int count = n * n;
            var filter = GetComponent<MeshFilter>();

            if (mesh == null)
            {
                mesh = new Mesh { name = "Island Terrain (generated)", hideFlags = HideFlags.DontSave };
                mesh.MarkDynamic();
            }
            if (vertices == null || vertices.Length != count)
            {
                vertices = new Vector3[count];
                normals = new Vector3[count];
                colors = new Color[count];
                mesh.Clear();
                mesh.indexFormat = count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
                FillPositions();
                mesh.SetVertices(vertices);
                trianglesDirty = true;
            }
            if (renderCover != null && renderCover.Length != count) renderCover = null;
            if (trianglesDirty)
            {
                mesh.SetTriangles(BuildGridTriangles(n, renderCover), 0);
                trianglesDirty = false;
            }
            FillPositions();
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetColors(colors);
            float pad = cellSize;
            mesh.bounds = new Bounds(Vector3.zero, new Vector3(HalfExtent * 2f + pad, (maxHeight - minHeight) * 2f, HalfExtent * 2f + pad));
            if (filter.sharedMesh != mesh) filter.sharedMesh = mesh;
        }

        void FillPositions()
        {
            int n = resolution;
            float half = (n - 1) * 0.5f;
            for (int z = 0; z < n; z++)
            {
                for (int x = 0; x < n; x++)
                {
                    int i = z * n + x;
                    float h = heights[i];
                    float y = renderCover != null && renderCover[i] ? h - coverSink : h;
                    vertices[i] = new Vector3((x - half) * cellSize, y, (z - half) * cellSize);
                    float hl = heights[x > 0 ? i - 1 : i], hr = heights[x < n - 1 ? i + 1 : i];
                    float hd = heights[z > 0 ? i - n : i], hu = heights[z < n - 1 ? i + n : i];
                    normals[i] = new Vector3(hl - hr, 2f * cellSize, hd - hu).normalized;
                    colors[i] = ColorForHeight(h, normals[i].y);
                }
            }
        }

        Color ColorForHeight(float h, float upness)
        {
            if (h < 0f) return Color.Lerp(seabedColor, sandColor, Mathf.InverseLerp(-4f, 0f, h));
            if (h < sandTop) return sandColor;
            Color c = Color.Lerp(sandColor, grassColor, Mathf.InverseLerp(sandTop, sandTop + 0.6f, h));
            c = Color.Lerp(c, highGrassColor, Mathf.InverseLerp(4f, stoneFromHeight, h));
            c = Color.Lerp(c, stoneColor, Mathf.InverseLerp(stoneFromHeight, stoneFromHeight + 4f, h));
            // Very steep slopes (e.g. freshly raised walls) read as stone.
            float steep = 1f - upness;
            return Color.Lerp(c, stoneColor, Mathf.InverseLerp(stoneSlope - 0.1f, stoneSlope + 0.1f, steep));
        }

        /// <summary>Triangle list for an n x n vertex grid with alternating diagonals (low-poly look).</summary>
        public static int[] BuildGridTriangles(int n) => BuildGridTriangles(n, null);

        /// <summary>Grid triangles, skipping quads whose four corners are all covered (when a mask is given).</summary>
        public static int[] BuildGridTriangles(int n, bool[] skipCovered)
        {
            int quads = 0;
            for (int z = 0; z < n - 1; z++)
                for (int x = 0; x < n - 1; x++)
                    if (!AllCovered(skipCovered, n, x, z)) quads++;
            var tris = new int[quads * 6];
            int t = 0;
            for (int z = 0; z < n - 1; z++)
            {
                for (int x = 0; x < n - 1; x++)
                {
                    if (AllCovered(skipCovered, n, x, z)) continue;
                    int a = z * n + x, b = a + 1, c = a + n, d = c + 1;
                    if (((x + z) & 1) == 0)
                    {
                        tris[t++] = a; tris[t++] = c; tris[t++] = d;
                        tris[t++] = a; tris[t++] = d; tris[t++] = b;
                    }
                    else
                    {
                        tris[t++] = a; tris[t++] = c; tris[t++] = b;
                        tris[t++] = b; tris[t++] = c; tris[t++] = d;
                    }
                }
            }
            return tris;
        }

        static bool AllCovered(bool[] m, int n, int x, int z)
        {
            if (m == null) return false;
            int a = z * n + x;
            return m[a] && m[a + 1] && m[a + n] && m[a + n + 1];
        }
    }
}
