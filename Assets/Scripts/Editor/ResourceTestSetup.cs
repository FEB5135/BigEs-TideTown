using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Kaynak sistemini denemek için geçici placeholder objeler kurar.
// Assets/Scenes/ResourceTest.unity yoksa editör açılınca bir kez otomatik oluşturur.
// Modeller gelince bu objelerin görselleri değiştirilir, script'ler aynen kalır.
[InitializeOnLoad]
public static class ResourceTestSetup
{
    const string RootName = "[Placeholder] Resource Test";
    const string ScenePath = "Assets/Scenes/ResourceTest.unity";

    static ResourceTestSetup()
    {
        EditorApplication.delayCall += CreateSceneAssetIfMissing;
    }

    static void CreateSceneAssetIfMissing()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            return;
        if (File.Exists(ScenePath))
        {
            UpgradePlaceholderTreesIfOpen();
            return;
        }

        var current = SceneManager.GetActiveScene();
        bool currentIsEmptyUntitled = string.IsNullOrEmpty(current.path) && !current.isDirty;
        if (!currentIsEmptyUntitled && string.IsNullOrEmpty(current.path))
        {
            Debug.Log($"{ScenePath} oluşturulamadı: açık sahne kaydedilmemiş. Sahneyi kaydedip Tools > BigEs > Kaynak Test Sahnesi Kur'u kullanın.");
            return;
        }

        var mode = currentIsEmptyUntitled ? NewSceneMode.Single : NewSceneMode.Additive;
        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, mode);
        SceneManager.SetActiveScene(scene);
        Build();
        var camera = Camera.main;
        if (camera != null)
        {
            camera.transform.position = new Vector3(-8f, 22f, -26f);
            camera.transform.LookAt(new Vector3(-8f, 0f, 0f));
        }
        EditorSceneManager.SaveScene(scene, ScenePath);

        if (mode == NewSceneMode.Additive)
        {
            SceneManager.SetActiveScene(current);
            EditorSceneManager.CloseScene(scene, true);
        }
        Debug.Log($"Kaynak test sahnesi oluşturuldu: {ScenePath}");
    }

    // Sahne ağaç paketi gelmeden oluşturulduysa, açıkken placeholder'ları gerçek ağaçlarla yeniden kurar.
    static void UpgradePlaceholderTreesIfOpen()
    {
        var scene = SceneManager.GetActiveScene();
        if (scene.path != ScenePath || FindTreePrefabs().Length == 0)
            return;
        var root = GameObject.Find(RootName);
        var forest = root != null ? root.transform.Find("Forest") : null;
        if (forest == null || forest.childCount == 0 || PrefabUtility.IsPartOfPrefabInstance(forest.GetChild(0).gameObject))
            return;

        Object.DestroyImmediate(root);
        Build();
        EditorSceneManager.SaveScene(scene);
        Debug.Log("Kaynak test sahnesindeki placeholder ağaçlar gerçek ağaç modelleriyle değiştirildi.");
    }

    static GameObject[] FindTreePrefabs()
    {
        var prefabs = new System.Collections.Generic.List<GameObject>();
        foreach (var guid in AssetDatabase.FindAssets("StandardTree t:Prefab"))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            if (path.EndsWith("_LP.prefab"))
                prefabs.Add(AssetDatabase.LoadAssetAtPath<GameObject>(path));
        }
        return prefabs.ToArray();
    }

    [MenuItem("Tools/BigEs/Kaynak Test Sahnesi Kur")]
    static void Create()
    {
        if (GameObject.Find(RootName) != null)
        {
            Debug.LogWarning($"'{RootName}' zaten sahnede.");
            return;
        }
        Build();
    }

    static void Build()
    {
        var root = new GameObject(RootName);
        Undo.RegisterCreatedObjectUndo(root, "Kaynak Test Sahnesi Kur");

        if (Object.FindAnyObjectByType<Terrain>() == null)
        {
            var ground = Primitive(PrimitiveType.Plane, "Ground", root.transform, Vector3.zero, new Vector3(10f, 1f, 10f), new Color(0.35f, 0.55f, 0.3f));
            ground.isStatic = true;
        }

        // Kuyu: köyün ortası.
        var well = Primitive(PrimitiveType.Cylinder, "Well", root.transform, new Vector3(0f, 0.25f, 0f), new Vector3(3f, 0.25f, 3f), new Color(0.45f, 0.45f, 0.5f));
        well.AddComponent<Well>();

        // Orman: köyün solu (-X).
        var forest = new GameObject("Forest").transform;
        forest.SetParent(root.transform);
        var random = new System.Random(1234);
        var treePrefabs = FindTreePrefabs();
        for (int i = 0; i < 12; i++)
        {
            var pos = new Vector3(-16f - (float)random.NextDouble() * 14f, 0f, -12f + (float)random.NextDouble() * 24f);
            float height = 5f + (float)random.NextDouble() * 2f;
            var tree = treePrefabs.Length > 0
                ? TreeFromPrefab(treePrefabs[random.Next(treePrefabs.Length)], forest, pos, height, (float)random.NextDouble() * 360f)
                : PlaceholderTree(forest, pos);
            tree.name = $"Tree {i + 1}";
            tree.AddComponent<ResourceNode>();
        }

        // Odun toplayan köylüler.
        for (int i = 0; i < 3; i++)
        {
            var villager = Primitive(PrimitiveType.Capsule, $"Woodcutter {i + 1}", root.transform, new Vector3(-3f, 1f, -2f + i * 2f), Vector3.one * 0.8f, new Color(0.85f, 0.7f, 0.5f));
            Object.DestroyImmediate(villager.GetComponent<Collider>());
            var log = Primitive(PrimitiveType.Cube, "Carried Wood", villager.transform, Vector3.zero, Vector3.one, new Color(0.4f, 0.25f, 0.1f));
            Object.DestroyImmediate(log.GetComponent<Collider>());
            log.transform.localPosition = new Vector3(0f, 1.4f, 0f);
            log.transform.localScale = new Vector3(0.4f, 0.4f, 1f);
            var gatherer = villager.AddComponent<Gatherer>();
            var so = new SerializedObject(gatherer);
            so.FindProperty("carryVisual").objectReferenceValue = log;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        EditorSceneManager.MarkSceneDirty(root.scene);
        Selection.activeGameObject = root;
    }

    // Prefab'ı verilen yüksekliğe ölçekler, tabanını zemine oturtur ve gövdeye collider ekler.
    static GameObject TreeFromPrefab(GameObject prefab, Transform parent, Vector3 position, float height, float yRotation)
    {
        var tree = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        tree.transform.SetLocalPositionAndRotation(position, Quaternion.Euler(0f, yRotation, 0f));

        var bounds = RendererBounds(tree);
        if (bounds.size.y > 0.01f)
            tree.transform.localScale *= height / bounds.size.y;
        bounds = RendererBounds(tree);
        tree.transform.position += Vector3.up * (position.y - bounds.min.y);

        float scale = tree.transform.lossyScale.y;
        var collider = tree.AddComponent<CapsuleCollider>();
        collider.radius = 0.4f / scale;
        collider.height = height / scale;
        collider.center = tree.transform.InverseTransformPoint(new Vector3(tree.transform.position.x, position.y + height * 0.5f, tree.transform.position.z));
        return tree;
    }

    static Bounds RendererBounds(GameObject go)
    {
        var renderers = go.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0)
            return new Bounds(go.transform.position, Vector3.zero);
        var bounds = renderers[0].bounds;
        foreach (var r in renderers)
            bounds.Encapsulate(r.bounds);
        return bounds;
    }

    static GameObject PlaceholderTree(Transform parent, Vector3 position)
    {
        var tree = Primitive(PrimitiveType.Cylinder, "Tree", parent, position + Vector3.up * 1.5f, new Vector3(0.5f, 1.5f, 0.5f), new Color(0.4f, 0.25f, 0.1f));
        var leaves = Primitive(PrimitiveType.Sphere, "Leaves", tree.transform, Vector3.zero, Vector3.one, new Color(0.15f, 0.45f, 0.15f));
        Object.DestroyImmediate(leaves.GetComponent<Collider>());
        leaves.transform.localPosition = new Vector3(0f, 1.3f, 0f);
        leaves.transform.localScale = new Vector3(5f, 1.6f, 5f);
        return tree;
    }

    static GameObject Primitive(PrimitiveType type, string name, Transform parent, Vector3 localPosition, Vector3 localScale, Color color)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPosition;
        go.transform.localScale = localScale;
        var renderer = go.GetComponent<Renderer>();
        var material = new Material(renderer.sharedMaterial) { color = color };
        renderer.sharedMaterial = material;
        return go;
    }
}
