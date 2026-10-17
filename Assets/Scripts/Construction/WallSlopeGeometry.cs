using UnityEngine;

namespace IdleTown.Construction
{
    // Shear rather than rotate: boards and stacked floors stay vertical; rails follow the slope.
    public sealed class WallSlopeGeometry : MonoBehaviour
    {
        Mesh source, visual, collision;
        float rise = float.NaN;
        public float Rise => rise;
        public void Configure(MeshFilter filter, Mesh mesh, float normalizedRise, bool solid)
        {
            if (source == mesh && Mathf.Approximately(rise, normalizedRise))
            { filter.sharedMesh = visual; return; }
            Release(visual); Release(collision);
            source = mesh;
            rise = normalizedRise;
            visual = Instantiate(mesh);
            visual.name = mesh.name + " Terrain Slope";
            var vertices = visual.vertices;
            for (int i = 0; i < vertices.Length; i++) vertices[i].y += vertices[i].z * rise;
            visual.vertices = vertices;
            visual.RecalculateNormals(); visual.RecalculateBounds();
            filter.sharedMesh = visual;
            if (!solid) return;
            var box = GetComponent<BoxCollider>();
            if (box != null) box.enabled = false;
            collision = new Mesh { name = "Sloped Wall Collider" };
            vertices = new Vector3[8];
            for (int i = 0; i < 8; i++)
            {
                vertices[i] = new Vector3((i & 1) == 0 ? -.5f : .5f,
                    (i & 2) == 0 ? -.5f : .5f, (i & 4) == 0 ? -.5f : .5f);
                vertices[i].y += vertices[i].z * rise;
            }
            collision.vertices = vertices;
            collision.triangles = new[] { 0,3,1,0,2,3, 4,5,7,4,7,6, 0,1,5,0,5,4,
                2,6,7,2,7,3, 0,4,6,0,6,2, 1,3,7,1,7,5 };
            collision.RecalculateBounds();
            var collider = GetComponent<MeshCollider>();
            if (collider == null) collider = gameObject.AddComponent<MeshCollider>();
            collider.sharedMesh = collision;
        }
        static void Release(Object value)
        {
            if (value == null) return;
            if (Application.isPlaying) Destroy(value); else DestroyImmediate(value);
        }
        void OnDestroy() { Release(visual); Release(collision); }
    }
}
