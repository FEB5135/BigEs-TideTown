using UnityEngine;

namespace IdleTown.Construction
{
    /// <summary>Lightweight, art-free grid used by the generated demo scene.</summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class WallDemoGrid : MonoBehaviour
    {
        [SerializeField] World world;
        [SerializeField] WallTerrainSurface terrainSurface;
        [SerializeField] Color color = new(0.16f, 0.24f, 0.2f, 1f);
        Mesh mesh;
        Material material;

        public void Configure(World targetWorld) => world = targetWorld;
        public void Configure(World targetWorld, WallTerrainSurface surface)
        { world = targetWorld; terrainSurface = surface; }

        // MapView builds the landscape in Start at execution order -25. Awake was too early,
        // and left the main scene's grid on the flat plane even after walls followed the hills.
        void Start()
        {
            if (world == null)
                return;
            if (terrainSurface == null && world.TryGetComponent<MapView>(out var map))
            {
                terrainSurface = world.GetComponent<WallTerrainSurface>();
                if (terrainSurface == null)
                {
                    terrainSurface = world.gameObject.AddComponent<WallTerrainSurface>();
                    terrainSurface.ConfigureMap(map);
                }
            }

            var verticalCount = world.Width + 1;
            var horizontalCount = world.Height + 1;
            var vertices = new Vector3[(verticalCount + horizontalCount) * 2];
            var index = 0;
            var y = 0.035f;

            for (var x = 0; x <= world.Width; x++)
            {
                vertices[index++] = new Vector3(x * world.CellSize, y, 0f);
                vertices[index++] = new Vector3(x * world.CellSize, y, world.Height * world.CellSize);
            }

            for (var z = 0; z <= world.Height; z++)
            {
                vertices[index++] = new Vector3(0f, y, z * world.CellSize);
                vertices[index++] = new Vector3(world.Width * world.CellSize, y, z * world.CellSize);
            }

            if (terrainSurface != null)
            {
                var conformed = new System.Collections.Generic.List<Vector3>();
                Physics.SyncTransforms();
                // Each line is drawn in half-cell sections. Every corner and edge midpoint is shared by several
                // sections, so the terrain is sampled once per point (half-cell lattice) and the sections reuse it.
                int columns = world.Width * 2 + 1, rows = world.Height * 2 + 1;
                var positions = new Vector3[columns * rows];
                for (int j = 0; j < rows; j++)
                    for (int i = 0; i < columns; i++)
                        positions[j * columns + i] = world.transform.TransformPoint(new Vector3(i * world.CellSize * .5f, 0, j * world.CellSize * .5f));
                var points = new Vector3[positions.Length];
                var found = new bool[positions.Length];
                terrainSurface.SampleMany(positions, points, found);
                void Add(int i, int j, int bi, int bj)
                {
                    int a = j * columns + i, b = bj * columns + bi;
                    if (!found[a] || !found[b]) return;
                    conformed.Add(transform.InverseTransformPoint(points[a] + Vector3.up * .035f));
                    conformed.Add(transform.InverseTransformPoint(points[b] + Vector3.up * .035f));
                }
                for (int x = 0; x <= world.Width; x++)
                    for (int z = 0; z < world.Height; z++)
                    {
                        Add(x * 2, z * 2, x * 2, z * 2 + 1);
                        Add(x * 2, z * 2 + 1, x * 2, z * 2 + 2);
                    }
                for (int z = 0; z <= world.Height; z++)
                    for (int x = 0; x < world.Width; x++)
                    {
                        Add(x * 2, z * 2, x * 2 + 1, z * 2);
                        Add(x * 2 + 1, z * 2, x * 2 + 2, z * 2);
                    }
                vertices = conformed.ToArray();
            }

            var indices = new int[vertices.Length];
            for (var i = 0; i < indices.Length; i++)
                indices[i] = i;

            mesh = new Mesh { name = "Demo Grid Mesh", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.vertices = vertices;
            mesh.SetIndices(indices, MeshTopology.Lines, 0);
            mesh.RecalculateBounds();
            GetComponent<MeshFilter>().sharedMesh = mesh;

            var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
            material = new Material(shader) { color = color };
            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", color);
            GetComponent<MeshRenderer>().sharedMaterial = material;
        }

        void OnDestroy()
        {
            if (mesh != null)
                Destroy(mesh);
            if (material != null)
                Destroy(material);
        }
    }
}
