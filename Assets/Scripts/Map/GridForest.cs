using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Haritanın World'ündeki (World + MapView + WfcSolver, SampleScene'deki tek obje) ağaçları toplanabilir yapar.
// Orman alanı verilirse ağaçlar yalnız orada çıkabilir: alanın dışındaki hücreler WFC'ye zemin olarak sabitlenir.
// Her ağaç hücresine görünmez bir toplama noktası (ResourceNode + collider) konur. Ağaç tükenince hücre zemine
// döner ve yerine ağacın kopyası devrilir, yerde bekler, sonra saydamlaşarak kaybolur. Yeniden çıkınca hücre ağaç olur.
[RequireComponent(typeof(World))]
public class GridForest : MonoBehaviour
{
    [SerializeField] World map;
    [SerializeField] WfcSolver solver;
    [Tooltip("Ağaçların çıkabildiği alanın merkezi (dünya). Alanın dışı WFC'de zemine sabitlenir.")]
    [SerializeField] Vector3 forestCenter;
    [Tooltip("Orman alanının eni (x) ve boyu (z), metre. 0 ise ağaçlar WFC'nin koyduğu her yerde çıkar.")]
    [SerializeField] Vector2 forestSize;
    [Tooltip("Orman alanının Y dönüşü (kamera gibi 45°).")]
    [SerializeField] float forestYaw = 45f;
    [Tooltip("Orman alanının dışı zemine sabitlensin mi. Kapalıysa dışarıda WFC serbest (ekibin dağ/tepe kuralları).")]
    [SerializeField] bool pinOutsideForest = true;
    [Tooltip("Orman alanında bir hücrenin ağaç olma olasılığı.")]
    [SerializeField, Range(0f, 1f)] float treeChance = 0.5f;
    [SerializeField] int treeId = 5;
    [SerializeField] int groundId;
    [Tooltip("MapView'daki ağaç karoları, aynı sıra ve ağırlıkla (devrilen ağaç hücredekiyle aynı görünsün diye).")]
    [SerializeField] GameObject[] treeTiles;
    [SerializeField] int[] treeWeights;
    [Tooltip("Toplama noktasının tıklama alanı (hücre boyuna oranla genişlik ve yükseklik).")]
    [SerializeField] float colliderWidth = 0.7f;
    [SerializeField] float colliderHeight = 1.2f;
    [Tooltip("Ağaç karolarının hücreye göre büyütmesi (karo prefab'ında; burada tıklama alanı için).")]
    [SerializeField] float treeScale = 1f;
    [Tooltip("WFC'nin koyduğu bir ağacın kalma olasılığı (büyük ağaçlar seyrek olsun).")]
    [SerializeField, Range(0f, 1f)] float treeKeep = 1f;
    [Tooltip("İki ağaç arasında en az bu kadar boş hücre olur.")]
    [SerializeField] int treeSpacing;

    [Header("Devrilme")]
    [SerializeField] float fallSeconds = 1.1f;
    [Tooltip("Yere düştükten sonra kaybolmadan önce bekleme.")]
    [SerializeField] float lieSeconds = 2f;
    [SerializeField] float fadeSeconds = 1.5f;
    [Tooltip("MapView'ın çizdiği palet malzemesi (devrilen ağaç hücredekiyle aynı renkte olsun).")]
    [SerializeField] Material treeMaterial;
    [Tooltip("Saydamlaşabilen (Transparent) palet malzemesi.")]
    [SerializeField] Material fadeMaterial;

    [Header("Vuruş Sallanması")]
    [SerializeField] float shakeSeconds = 0.35f;
    [SerializeField] float shakeAngle = 4f;

    readonly HashSet<Vector2Int> trees = new();
    readonly Dictionary<Vector2Int, Shake> shakes = new();

    class Shake
    {
        public List<GameObject> Trees;
        public List<Quaternion> Rest;
        public Coroutine Routine;
    }
    Transform nodes;

    void Awake()
    {
        if (map == null)
            map = GetComponent<World>();
        if (solver == null)
            solver = GetComponent<WfcSolver>();
        nodes = new GameObject("Tree Nodes").transform;
        nodes.SetParent(transform, false);

        // WfcSolver Start'ta çözer; ondan önce orman alanının her hücresi ağaç ya da düz zemine sabitlenir (yokuş/tepe
        // çıkmaz, çözüm sonrası karo değiştirmek gerekmez; komşulukları WFC'nin kendisi tutarlı kurar). İstenirse orman
        // dışı da zemine sabitlenir. Dönüş serbest: çimen ve ağaçlar çeşitlensin.
        if (solver != null && HasForestArea)
            for (int y = 0; y < map.Height; y++)
                for (int x = 0; x < map.Width; x++)
                {
                    if (InForest(x, y))
                        solver.Pin(x, y, Random.value < treeChance ? treeId : groundId, -1);
                    else if (pinOutsideForest)
                        solver.Pin(x, y, groundId, -1);
                }
    }

    bool HasTreeNear(Vector2Int cell)
    {
        for (int dy = -treeSpacing; dy <= treeSpacing; dy++)
            for (int dx = -treeSpacing; dx <= treeSpacing; dx++)
                if ((dx != 0 || dy != 0) && trees.Contains(cell + new Vector2Int(dx, dy)))
                    return true;
        return false;
    }

    bool HasForestArea => forestSize.x > 0f && forestSize.y > 0f;

    public bool InForest(int x, int y)
    {
        var local = Quaternion.Euler(0f, -forestYaw, 0f) * (map.CellCenter(x, y) - forestCenter);
        return Mathf.Abs(local.x) <= forestSize.x * 0.5f && Mathf.Abs(local.z) <= forestSize.y * 0.5f;
    }

    void OnDestroy()
    {
        foreach (var parts in partsCache.Values)
            foreach (var part in parts)
                Destroy(part.Mesh);
    }

    void OnEnable() => map.CellChanged += OnCellChanged;
    void OnDisable() => map.CellChanged -= OnCellChanged;

    void OnCellChanged(int x, int y)
    {
        map.Get(x, y, out var id, out var rotation);
        // Orman alanında yalnız ağaç ve düz zemin olur; WFC'nin koyduğu yokuş/tepe karoları zemine çevrilir.
        if (id != treeId && id != groundId && HasForestArea && InForest(x, y))
        {
            map.Set(x, y, groundId, rotation);
            return;
        }
        if (id != treeId)
            return;
        var cell = new Vector2Int(x, y);
        if (trees.Contains(cell))
            return; // yeniden çıkan ağaç
        if (Random.value > treeKeep || HasTreeNear(cell))
        {
            map.Set(x, y, groundId, rotation);
            return;
        }
        trees.Add(cell);
        AddNode(x, y, rotation);
    }

    void AddNode(int x, int y, int rotation)
    {
        var node = new GameObject($"Tree {x},{y}");
        node.transform.SetParent(nodes, false);
        node.transform.SetPositionAndRotation(map.CellCenter(x, y), map.transform.rotation);
        var box = node.AddComponent<BoxCollider>();
        float width = map.CellSize * colliderWidth * treeScale;
        float height = map.CellSize * colliderHeight * treeScale;
        box.size = new Vector3(width, height, width);
        box.center = new Vector3(0f, height * 0.5f, 0f);
        var resource = node.AddComponent<ResourceNode>();
        resource.Hit += () => ShakeTree(x, y, rotation);
        resource.VisibilityChanged += visible =>
        {
            if (!visible)
                Fell(x, y, rotation);
            map.Set(x, y, visible ? treeId : groundId, visible ? rotation : 0);
        };
    }

    // Vurulan ağaç hafifçe sallanır. Ağaç MapView'ın mesh'inde çizildiği için sallanırken hücre zemine çevrilir
    // ve yerine ağaçların ayrı kopyaları konur; sallanma bitince hücre yeniden ağaç olur.
    void ShakeTree(int x, int y, int rotation)
    {
        var cell = new Vector2Int(x, y);
        if (shakes.TryGetValue(cell, out var shake))
            StopCoroutine(shake.Routine);
        else
        {
            var spawned = SpawnTrees(x, y, rotation, "Shaking Tree");
            shake = new Shake { Trees = spawned, Rest = spawned.ConvertAll(t => t.transform.rotation) };
            shakes.Add(cell, shake);
            map.Set(x, y, groundId);
        }
        shake.Routine = StartCoroutine(ShakeRoutine(cell, rotation, shake));
    }

    IEnumerator ShakeRoutine(Vector2Int cell, int rotation, Shake shake)
    {
        var axis = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * Vector3.right;
        for (float t = 0f; t < shakeSeconds; t += Time.deltaTime)
        {
            float k = t / shakeSeconds;
            float angle = Mathf.Sin(k * Mathf.PI * 3f) * shakeAngle * (1f - k);
            for (int i = 0; i < shake.Trees.Count; i++)
                shake.Trees[i].transform.rotation = Quaternion.AngleAxis(angle, axis) * shake.Rest[i];
            yield return null;
        }
        shake.Routine = null;
        EndShake(cell);
        map.Set(cell.x, cell.y, treeId, rotation);
    }

    void EndShake(Vector2Int cell)
    {
        if (!shakes.Remove(cell, out var shake))
            return;
        if (shake.Routine != null)
            StopCoroutine(shake.Routine);
        foreach (var tree in shake.Trees)
            Destroy(tree);
    }

    // Hücredeki ağaç karosunun ağaçları (zemin plakası olmadan) ayrı ayrı devrilir; karoda iki ağaç varsa
    // her biri kendi gövdesinin dibinden, kendi rastgele yönüne düşer.
    void Fell(int x, int y, int rotation)
    {
        EndShake(new Vector2Int(x, y));
        if (GameAudio.Instance != null)
            GameAudio.Play(GameAudio.Instance.treeCrack, map.CellCenter(x, y), 0.8f);
        foreach (var tree in SpawnTrees(x, y, rotation, "Falling Tree"))
            StartCoroutine(FallRoutine(tree.transform, tree.GetComponent<Renderer>()));
    }

    // Hücredeki karonun her ağacını, MapView'ın çizdiği yerde, köşesi gövde dibinde ayrı bir obje olarak koyar.
    List<GameObject> SpawnTrees(int x, int y, int rotation, string name)
    {
        var result = new List<GameObject>();
        var tile = TreeTile(x, y);
        if (tile == null)
            return result;

        var yaw = map.transform.rotation * Quaternion.Euler(0f, rotation * 90f, 0f);
        float half = map.CellSize * 0.5f;
        var origin = map.CellCenter(x, y) + yaw * new Vector3(-half, 0f, half); // MapView'ın karoyu çizdiği köşe
        foreach (var part in TreeParts(tile))
        {
            var tree = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer), typeof(NavIgnore));
            tree.transform.SetPositionAndRotation(origin + yaw * part.Base, yaw);
            tree.GetComponent<MeshFilter>().sharedMesh = part.Mesh;
            tree.GetComponent<MeshRenderer>().sharedMaterial = treeMaterial;
            result.Add(tree);
        }
        return result;
    }

    IEnumerator FallRoutine(Transform tree, Renderer renderer)
    {
        // 8 yönden biri (45°'nin katları). Gövdenin üst ucu bu yöne doğru yatar.
        var direction = Quaternion.Euler(0f, Random.Range(0, 8) * 45f, 0f) * Vector3.forward;
        var axis = Vector3.Cross(Vector3.up, direction);
        var start = tree.rotation;
        // Yavaş başlar, hızlanarak düşer, yere değince hafifçe sekip oturur.
        for (float t = 0f; t < fallSeconds; t += Time.deltaTime)
        {
            float k = t / fallSeconds;
            tree.rotation = Quaternion.AngleAxis(88f * k * k, axis) * start;
            yield return null;
        }
        // Yere çakıldığı an.
        if (GameAudio.Instance != null)
            GameAudio.Play(GameAudio.Instance.treeThud, tree.position);
        const float bounce = 0.25f;
        for (float t = 0f; t < bounce; t += Time.deltaTime)
        {
            tree.rotation = Quaternion.AngleAxis(88f - Mathf.Sin(t / bounce * Mathf.PI) * 6f, axis) * start;
            yield return null;
        }
        tree.rotation = Quaternion.AngleAxis(88f, axis) * start;

        yield return new WaitForSeconds(lieSeconds);

        Material faded = null;
        if (fadeMaterial != null)
        {
            // Saydam malzemenin rengi ve dokusu haritanın malzemesinden alınır (TerrainGround düz renk).
            faded = new Material(fadeMaterial);
            if (treeMaterial != null)
            {
                faded.mainTexture = treeMaterial.mainTexture;
                faded.color = treeMaterial.color;
            }
            renderer.sharedMaterial = faded;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
        var color = faded != null ? faded.color : Color.white;
        for (float t = 0f; t < fadeSeconds; t += Time.deltaTime)
        {
            color.a = 1f - t / fadeSeconds;
            if (faded != null)
                faded.color = color;
            else
                tree.localScale = Vector3.one * color.a; // saydam malzeme yoksa küçülerek kaybolur
            yield return null;
        }
        if (faded != null)
            Destroy(faded);
        Destroy(tree.gameObject);
    }

    struct TreePart
    {
        public Mesh Mesh;    // köşesi gövdenin dibinde, karonun ölçeğinde
        public Vector3 Base; // gövde dibinin karo köşesine göre yeri
    }

    readonly Dictionary<GameObject, List<TreePart>> partsCache = new();

    // Karo modelini birbirine değmeyen parçalara ayırır (konumu aynı köşeler tek sayılır). Düz parçalar zemin
    // plakasıdır, atılır. Tabanı yerde olan parçalar gövdedir; diğer parçalar (yaprak katları) yatayda en yakın
    // gövdeye bağlanır. Her gövde ile yaprakları tek mesh olur. Sonuç karo başına bir kez hesaplanır.
    List<TreePart> TreeParts(GameObject tile)
    {
        if (partsCache.TryGetValue(tile, out var cached))
            return cached;

        var vertices = new List<Vector3>();
        var normals = new List<Vector3>();
        var uvs = new List<Vector2>();
        var triangles = new List<int>();
        // MapView karoyu prefab ölçeği × hücre boyuyla çizer.
        var root = Matrix4x4.Scale(tile.transform.localScale * map.CellSize);
        foreach (var filter in tile.GetComponentsInChildren<MeshFilter>())
        {
            var mesh = filter.sharedMesh;
            if (mesh == null || !mesh.isReadable)
                continue;
            var matrix = root * LocalToRoot(filter.transform, tile.transform);
            int offset = vertices.Count;
            var meshUvs = mesh.uv;
            var meshNormals = mesh.normals;
            var meshVertices = mesh.vertices;
            for (int i = 0; i < meshVertices.Length; i++)
            {
                vertices.Add(matrix.MultiplyPoint3x4(meshVertices[i]));
                normals.Add(i < meshNormals.Length ? matrix.MultiplyVector(meshNormals[i]).normalized : Vector3.up);
                uvs.Add(i < meshUvs.Length ? meshUvs[i] : Vector2.zero);
            }
            for (int s = 0; s < mesh.subMeshCount; s++)
                foreach (var index in mesh.GetTriangles(s))
                    triangles.Add(offset + index);
        }

        // Konumu aynı köşeleri birleştirip üçgenleri bağlı parçalara ayır.
        var parent = new int[vertices.Count];
        var welded = new Dictionary<Vector3Int, int>();
        for (int i = 0; i < vertices.Count; i++)
        {
            var key = Vector3Int.RoundToInt(vertices[i] * 1000f);
            parent[i] = welded.TryGetValue(key, out var first) ? first : i;
            welded.TryAdd(key, i);
        }
        int Find(int a)
        {
            while (parent[a] != a)
                a = parent[a] = parent[parent[a]];
            return a;
        }
        for (int t = 0; t < triangles.Count; t += 3)
        {
            int a = Find(triangles[t]);
            parent[Find(triangles[t + 1])] = a;
            parent[Find(triangles[t + 2])] = a;
        }

        var pieces = new Dictionary<int, List<int>>();
        for (int t = 0; t < triangles.Count; t += 3)
        {
            int id = Find(triangles[t]);
            if (!pieces.TryGetValue(id, out var list))
                pieces.Add(id, list = new List<int>());
            list.Add(t);
        }

        var trunks = new List<(Bounds bounds, List<int> tris)>();
        var crowns = new List<(Bounds bounds, List<int> tris)>();
        foreach (var piece in pieces.Values)
        {
            var bounds = new Bounds(vertices[triangles[piece[0]]], Vector3.zero);
            foreach (var t in piece)
                for (int k = 0; k < 3; k++)
                    bounds.Encapsulate(vertices[triangles[t + k]]);
            if (bounds.size.y < 0.01f)
                continue; // zemin plakası
            (bounds.min.y < 0.01f ? trunks : crowns).Add((bounds, piece));
        }

        var groups = new List<List<int>>();
        foreach (var trunk in trunks)
            groups.Add(new List<int>(trunk.tris));
        foreach (var crown in crowns)
        {
            int best = -1;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < trunks.Count; i++)
            {
                var d = crown.bounds.center - trunks[i].bounds.center;
                float distance = d.x * d.x + d.z * d.z;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = i;
                }
            }
            if (best >= 0)
                groups[best].AddRange(crown.tris);
        }

        var parts = new List<TreePart>();
        for (int g = 0; g < groups.Count; g++)
        {
            var center = trunks[g].bounds.center;
            var pivot = new Vector3(center.x, 0f, center.z);
            var remap = new Dictionary<int, int>();
            var partVertices = new List<Vector3>();
            var partNormals = new List<Vector3>();
            var partUvs = new List<Vector2>();
            var partTriangles = new List<int>();
            foreach (var t in groups[g])
                for (int k = 0; k < 3; k++)
                {
                    int source = triangles[t + k];
                    if (!remap.TryGetValue(source, out var index))
                    {
                        index = partVertices.Count;
                        remap.Add(source, index);
                        partVertices.Add(vertices[source] - pivot);
                        partNormals.Add(normals[source]);
                        partUvs.Add(uvs[source]);
                    }
                    partTriangles.Add(index);
                }
            var mesh = new Mesh { name = $"{tile.name} tree {g}" };
            mesh.SetVertices(partVertices);
            mesh.SetNormals(partNormals);
            mesh.SetUVs(0, partUvs);
            mesh.SetTriangles(partTriangles, 0);
            mesh.RecalculateBounds();
            parts.Add(new TreePart { Mesh = mesh, Base = pivot });
        }
        partsCache.Add(tile, parts);
        return parts;
    }

    static Matrix4x4 LocalToRoot(Transform from, Transform root)
    {
        var matrix = Matrix4x4.identity;
        while (from != null && from != root)
        {
            matrix = Matrix4x4.TRS(from.localPosition, from.localRotation, from.localScale) * matrix;
            from = from.parent;
        }
        return matrix;
    }

    // MapView.Pick ile aynı seçim (aynı tohum), böylece devrilen ağaç hücredekiyle aynı modeldir.
    GameObject TreeTile(int x, int y)
    {
        if (treeTiles == null || treeTiles.Length == 0)
            return null;
        int total = 0;
        for (int i = 0; i < treeTiles.Length; i++)
            total += Weight(i);
        int roll = new System.Random(x * 73856093 ^ y * 19349663 ^ treeId * 83492791).Next(total);
        for (int i = 0; i < treeTiles.Length; i++)
        {
            roll -= Weight(i);
            if (roll < 0)
                return treeTiles[i];
        }
        return treeTiles[^1];
    }

    int Weight(int i) => treeWeights != null && i < treeWeights.Length && treeWeights[i] > 0 ? treeWeights[i] : 1;
}
