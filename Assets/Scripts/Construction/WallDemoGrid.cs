using UnityEngine;

namespace IdleTown.Construction
{
    /// <summary>Lightweight, art-free grid used by the generated demo scene.</summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class WallDemoGrid : MonoBehaviour
    {
        [SerializeField] World world;
        [SerializeField] Color color = new(0.16f, 0.24f, 0.2f, 1f);
        Mesh mesh;
        Material material;

        public void Configure(World targetWorld) => world = targetWorld;

        void Awake()
        {
            if (world == null)
                return;

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

            var indices = new int[vertices.Length];
            for (var i = 0; i < indices.Length; i++)
                indices[i] = i;

            mesh = new Mesh { name = "Demo Grid Mesh" };
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
