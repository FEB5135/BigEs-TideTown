using System.Collections.Generic;
using UnityEngine;

// Tsunaminin yıktığı evin dağılma animasyonu. Evin her karo modeli, karonun kendi konumu ve dönüşüyle (MapView'daki
// çizim matrisi) parçalara bölünür; parçalar önce hafifçe sarsılır, sonra binanın ortasından dışarı ve suyun aktığı
// yöne savrulur, yere düşüp seker, en sonunda küçülerek batar. Altlarında MapView'un çizdiği batık model kalır.
public class HouseCollapse : MonoBehaviour
{
    [SerializeField, Tooltip("Each tile model is cut into this many pieces along x, y, z.")]
    Vector3Int cuts = new(3, 3, 3);
    [SerializeField] float shakeTime = 0.25f;
    [SerializeField] float lifetime = 5f;
    [SerializeField, Tooltip("How strongly the water's flow throws the pieces.")]
    float flowThrow = 0.7f;
    [SerializeField] int maxPieces = 3000;

    class Piece
    {
        public Transform Transform;
        public Vector3 Home, Velocity, Spin, Scale;
        public float Age, Ground;
    }

    struct Fragment
    {
        public Mesh Mesh;
        public Vector3 Center; // in the source mesh's space
    }

    readonly Dictionary<Mesh, Fragment[]> fragments = new();
    readonly List<Piece> pieces = new();
    readonly Stack<GameObject> pool = new();
    Material material;

    // The house models are open shells; cut apart, their insides show, so the pieces are drawn from both sides.
    public void Init(Material houseMaterial)
    {
        if (houseMaterial == null)
            return;
        material = new Material(houseMaterial) { name = houseMaterial.name + " (pieces)" };
        material.SetFloat("_Cull", 0f);
        material.doubleSidedGI = true;
    }

    // One tile of a falling house: its mesh drawn with `world` (cell position, rotation and scale). `origin` is the
    // building's centre, `flow` the water velocity that knocked it down.
    public void Break(Mesh mesh, Matrix4x4 world, Vector3 origin, Vector3 flow)
    {
        if (mesh == null || pieces.Count >= maxPieces)
            return;
        var ground = world.MultiplyPoint3x4(Vector3.zero).y;
        var rotation = world.rotation;
        var scale = world.lossyScale;
        var height = Mathf.Max(0.5f, (world.MultiplyPoint3x4(mesh.bounds.max).y - ground));
        foreach (var fragment in Fragments(mesh))
        {
            var position = world.MultiplyPoint3x4(fragment.Center);
            var up = Mathf.Clamp01((position.y - ground) / height); // 0 floor, 1 roof
            var outward = position - origin;
            outward.y = 0f;
            outward = outward.sqrMagnitude > 0.01f ? outward.normalized : Random.insideUnitCircle.normalized.X0Y();
            var piece = new Piece
            {
                Transform = Take(fragment.Mesh).transform,
                Home = position,
                Scale = scale,
                Ground = ground,
                Age = -Random.Range(0f, 0.2f) * (1f - up), // the roof goes first, the walls follow
                Velocity = outward * Random.Range(2f, 5f) * (0.5f + up)
                    + flow * flowThrow * Random.Range(0.6f, 1.3f)
                    + Vector3.up * Random.Range(3f, 7f) * (0.35f + up),
                Spin = Random.insideUnitSphere * Random.Range(240f, 720f)
            };
            piece.Transform.SetPositionAndRotation(position, rotation);
            piece.Transform.localScale = scale;
            pieces.Add(piece);
        }
    }

    void Update()
    {
        float dt = Time.deltaTime;
        float sinkFrom = lifetime * 0.7f;
        for (int i = pieces.Count - 1; i >= 0; i--)
        {
            var piece = pieces[i];
            var transform = piece.Transform;
            piece.Age += dt;
            if (piece.Age >= lifetime)
            {
                Give(transform.gameObject);
                pieces.RemoveAt(i);
                continue;
            }

            // Shake in place before the house gives way.
            if (piece.Age < shakeTime)
            {
                transform.position = piece.Home + Random.insideUnitSphere * 0.1f;
                continue;
            }

            piece.Velocity += Physics.gravity * dt;
            var position = transform.position + piece.Velocity * dt;
            if (piece.Age > sinkFrom)
                piece.Ground -= dt * 0.6f;
            if (position.y < piece.Ground)
            {
                position.y = piece.Ground;
                if (piece.Velocity.y < 0f)
                    piece.Velocity.y *= -0.3f;
                piece.Velocity.x *= 0.55f;
                piece.Velocity.z *= 0.55f;
                piece.Spin *= 0.5f;
            }
            transform.position = position;
            transform.rotation = Quaternion.Euler(piece.Spin * dt) * transform.rotation;
            if (piece.Age > sinkFrom)
                transform.localScale = piece.Scale * (1f - (piece.Age - sinkFrom) / (lifetime - sinkFrom));
        }
    }

    // Triangles grouped by where their centre falls in a cuts.x × cuts.y × cuts.z grid over the mesh; each group is
    // its own mesh, centred on its vertices so it spins around itself.
    Fragment[] Fragments(Mesh mesh)
    {
        if (fragments.TryGetValue(mesh, out var cached))
            return cached;

        var vertices = mesh.vertices;
        var normals = mesh.normals;
        var uvs = mesh.uv;
        var colors = mesh.colors;
        var triangles = mesh.triangles;
        var bounds = mesh.bounds;
        var size = Vector3.Max(bounds.size, Vector3.one * 0.001f);
        var groups = new Dictionary<int, List<int>>();
        for (int t = 0; t < triangles.Length; t += 3)
        {
            var centre = (vertices[triangles[t]] + vertices[triangles[t + 1]] + vertices[triangles[t + 2]]) / 3f - bounds.min;
            int x = Mathf.Min(cuts.x - 1, (int)(centre.x / size.x * cuts.x));
            int y = Mathf.Min(cuts.y - 1, (int)(centre.y / size.y * cuts.y));
            int z = Mathf.Min(cuts.z - 1, (int)(centre.z / size.z * cuts.z));
            int key = (x * cuts.y + y) * cuts.z + z;
            if (!groups.TryGetValue(key, out var list))
                groups.Add(key, list = new List<int>());
            list.Add(triangles[t]);
            list.Add(triangles[t + 1]);
            list.Add(triangles[t + 2]);
        }

        var result = new List<Fragment>();
        foreach (var list in groups.Values)
        {
            var remap = new Dictionary<int, int>();
            var used = new List<int>();
            var indices = new int[list.Count];
            for (int i = 0; i < list.Count; i++)
            {
                if (!remap.TryGetValue(list[i], out var index))
                {
                    remap.Add(list[i], index = used.Count);
                    used.Add(list[i]);
                }
                indices[i] = index;
            }

            var centre = Vector3.zero;
            foreach (var v in used)
                centre += vertices[v];
            centre /= used.Count;

            var positions = new Vector3[used.Count];
            for (int i = 0; i < used.Count; i++)
                positions[i] = vertices[used[i]] - centre;
            var piece = new Mesh { name = mesh.name + " piece" };
            piece.vertices = positions;
            if (normals.Length == vertices.Length)
                piece.normals = used.ConvertAll(v => normals[v]).ToArray();
            if (uvs.Length == vertices.Length)
                piece.uv = used.ConvertAll(v => uvs[v]).ToArray();
            if (colors.Length == vertices.Length)
                piece.colors = used.ConvertAll(v => colors[v]).ToArray();
            piece.triangles = indices;
            piece.RecalculateBounds();
            result.Add(new Fragment { Mesh = piece, Center = centre });
        }

        cached = result.ToArray();
        fragments.Add(mesh, cached);
        return cached;
    }

    GameObject Take(Mesh mesh)
    {
        var piece = pool.Count > 0 ? pool.Pop() : null;
        if (piece == null)
        {
            piece = new GameObject("House piece", typeof(MeshFilter), typeof(MeshRenderer));
            piece.transform.SetParent(transform, false);
            piece.GetComponent<MeshRenderer>().sharedMaterial = material;
        }
        piece.GetComponent<MeshFilter>().sharedMesh = mesh;
        piece.SetActive(true);
        return piece;
    }

    void Give(GameObject piece)
    {
        piece.SetActive(false);
        pool.Push(piece);
    }

    void OnDestroy()
    {
        if (material != null)
            Destroy(material);
        foreach (var list in fragments.Values)
            foreach (var fragment in list)
                Destroy(fragment.Mesh);
    }
}

static class HouseCollapseVectors
{
    public static Vector3 X0Y(this Vector2 v) => new(v.x, 0f, v.y);
}
