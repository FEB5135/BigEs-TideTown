using UnityEngine;

namespace Tsunami
{
    /// <summary>
    /// Ocean surface surrounding the simulation grid: rings that start on the grid outline and become
    /// circular further out. Heights, normals and foam values all come from WaterSimulation.OpenOceanHeight,
    /// the same function the flood mesh fades into near the grid edge, so there is no visible seam or square.
    /// It shows the incoming wave approaching before it enters the grid.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class OceanRing : MonoBehaviour
    {
        [SerializeField] TerrainGrid terrain;
        [SerializeField] WaterSimulation simulation;
        [Tooltip("Half-size of the outer edge beyond the grid (m).")]
        [SerializeField, Min(50f)] float outerDistance = 1500f;
        [Tooltip("Distance (m) beyond the grid over which the square grid outline morphs into a circle (no corners).")]
        [SerializeField, Min(1f)] float roundingDistance = 40f;

        // Ring offsets beyond the grid edge (m). Dense near the grid, where the incoming crest is visible.
        static readonly float[] RingOffsets = { 0f, 2f, 5f, 10f, 16f, 24f, 34f, 46f, 60f, 78f, 100f, 130f, 175f, 250f, 400f };

        Mesh mesh;
        int perimeter;
        int[] edgeCells;
        Vector3[] vertices;
        Vector3[] normals;
        readonly System.Collections.Generic.List<Vector3> normalList = new System.Collections.Generic.List<Vector3>();
        Vector2[] uvs;
        int builtResolution = -1;
        float builtCellSize = -1f;
        int lastStep = -1;

        void OnEnable()
        {
            Rebuild();
        }

        void OnDisable()
        {
            if (mesh != null)
            {
                if (Application.isPlaying) Destroy(mesh); else DestroyImmediate(mesh);
                mesh = null;
            }
            builtResolution = -1;
        }

        void LateUpdate()
        {
            if (terrain == null) return;
            if (mesh == null || builtResolution != terrain.Resolution || !Mathf.Approximately(builtCellSize, terrain.CellSize))
                Rebuild();
            if (mesh == null) return;
            transform.SetPositionAndRotation(terrain.transform.position, Quaternion.identity);
            transform.localScale = Vector3.one;

            if (!Application.isPlaying || simulation == null) return;
            // The open ocean only changes while a wave/drawback is active; otherwise skip entirely.
            if (simulation.OpenOceanVersion == lastStep) return;
            lastStep = simulation.OpenOceanVersion;
            UpdateHeights();
        }

        void Rebuild()
        {
            if (terrain == null) return;
            int n = terrain.Resolution;
            builtResolution = n;
            builtCellSize = terrain.CellSize;
            perimeter = 4 * (n - 1);

            // Grid edge cells, counter-clockwise seen from above, starting at the south-west corner.
            edgeCells = new int[perimeter];
            int p = 0;
            for (int x = 0; x < n - 1; x++) edgeCells[p++] = x;                       // south, west -> east
            for (int z = 0; z < n - 1; z++) edgeCells[p++] = z * n + (n - 1);         // east, south -> north
            for (int x = n - 1; x > 0; x--) edgeCells[p++] = (n - 1) * n + x;         // north, east -> west
            for (int z = n - 1; z > 0; z--) edgeCells[p++] = z * n;                   // west, north -> south

            int rings = RingOffsets.Length + 1; // + outer rim
            vertices = new Vector3[rings * perimeter];
            uvs = new Vector2[vertices.Length];
            normals = new Vector3[vertices.Length];
            var tris = new int[(rings - 1) * perimeter * 6];
            int t = 0;
            for (int r = 0; r < rings - 1; r++)
            {
                for (int i = 0; i < perimeter; i++)
                {
                    int j = (i + 1) % perimeter;
                    int a = r * perimeter + i, b = r * perimeter + j;
                    int c = (r + 1) * perimeter + j, d = (r + 1) * perimeter + i;
                    tris[t++] = a; tris[t++] = b; tris[t++] = c;
                    tris[t++] = a; tris[t++] = c; tris[t++] = d;
                }
            }

            if (mesh == null)
            {
                mesh = new Mesh { name = "Ocean Ring (generated)", hideFlags = HideFlags.DontSave };
                mesh.MarkDynamic();
            }
            mesh.Clear();
            float sea = simulation != null ? simulation.SeaLevel : 0f;
            FillFlat(sea);
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            float e = terrain.HalfExtent + outerDistance;
            mesh.bounds = new Bounds(Vector3.zero, new Vector3(e * 2f, 80f, e * 2f));
            GetComponent<MeshFilter>().sharedMesh = mesh;
            lastStep = -1;
        }

        float RingOffset(int r) => r < RingOffsets.Length ? RingOffsets[r] : outerDistance;

        Vector3 RingPoint(int r, int edgeCell, out float gx, out float gz)
        {
            int n = terrain.Resolution;
            float half = (n - 1) * 0.5f;
            float a = terrain.HalfExtent;
            float cell = terrain.CellSize;
            float offset = RingOffset(r);
            // Edge point relative to the grid centre.
            var p0 = new Vector2((edgeCell % n - half) * cell, (edgeCell / n - half) * cell);
            // Square ring that matches the grid outline...
            Vector2 square = p0 * ((a + offset) / a);
            // ...morphing into a circle further out, so there are no corner folds in the open ocean.
            Vector2 circle = p0.normalized * (a * 1.41421356f + offset);
            float m = Mathf.SmoothStep(0f, 1f, offset / roundingDistance);
            Vector2 q = Vector2.Lerp(square, circle, m);
            gx = q.x / cell + half;
            gz = q.y / cell + half;
            return new Vector3(q.x, 0f, q.y);
        }

        void FillFlat(float level)
        {
            for (int r = 0; r * perimeter < vertices.Length; r++)
            {
                for (int i = 0; i < perimeter; i++)
                {
                    Vector3 v = RingPoint(r, edgeCells[i], out _, out _);
                    v.y = level;
                    vertices[r * perimeter + i] = v;
                    uvs[r * perimeter + i] = new Vector2(WaterSimulation.OpenOceanShaderDepth, 0f);
                    normals[r * perimeter + i] = Vector3.up;
                }
            }
        }

        void UpdateHeights()
        {
            int n = terrain.Resolution;
            int rings = vertices.Length / perimeter;
            for (int r = 0; r < rings; r++)
            {
                for (int i = 0; i < perimeter; i++)
                {
                    int vi = r * perimeter + i;
                    Vector3 v = RingPoint(r, edgeCells[i], out float gx, out float gz);
                    float speed;
                    // Ring 0 sits exactly on the grid edge: use the same cached values as the flood mesh.
                    v.y = r == 0 ? simulation.OceanAt(edgeCells[i] % n, edgeCells[i] / n, out speed)
                                 : simulation.OpenOceanHeight(gx, gz, out speed);
                    vertices[vi] = v;
                    uvs[vi] = new Vector2(WaterSimulation.OpenOceanShaderDepth, speed);
                }
            }
            mesh.SetVertices(vertices);
            mesh.RecalculateNormals();
            // Seam vertices: same central-difference normal as WaterMeshRenderer, from the shared cache.
            mesh.GetNormals(normalList);
            float twoCell = 2f * terrain.CellSize;
            for (int i = 0; i < perimeter; i++)
            {
                int x = edgeCells[i] % n, z = edgeCells[i] / n;
                float yl = simulation.OceanAt(x - 1, z, out _), yr = simulation.OceanAt(x + 1, z, out _);
                float yd = simulation.OceanAt(x, z - 1, out _), yu = simulation.OceanAt(x, z + 1, out _);
                normalList[i] = new Vector3(yl - yr, twoCell, yd - yu).normalized;
            }
            mesh.SetNormals(normalList);
            mesh.SetUVs(0, uvs);
        }
    }
}
