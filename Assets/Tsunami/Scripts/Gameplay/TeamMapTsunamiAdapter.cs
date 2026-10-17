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

        bool subscribed;
        Rect mapRectGrid; // World rectangle in grid cell indices

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
        }

        void Start()
        {
            if (!Application.isPlaying || wallController == null || subscribed) return;
            wallController.WallBuilt += OnWallBuilt;
            subscribed = true;
        }

        void OnDestroy()
        {
            if (subscribed && wallController != null) wallController.WallBuilt -= OnWallBuilt;
            subscribed = false;
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

            simulation.SeaLevel = -bathymetry.seaBelowGround;
            simulation.EnsureInitialized();
            WallSegments = 0;
            if (Application.isPlaying && wallController != null)
                foreach (WallEdge edge in wallController.Edges) AddWall(edge, null);
            simulation.ResetWater();
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
            simulation.AddWallSegment(NodePosition(edge.A), NodePosition(edge.B), top);
            WallSegments++;
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
