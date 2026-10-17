using System.Collections.Generic;
using System.Linq;
using IdleTown.Construction;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class SampleSceneSetup
{
    public const string ScenePath = "Assets/Scenes/SampleScene.unity";

    [MenuItem("Tools/BigEs/SampleScene Kaynak ve Koylu Kurulumu")]
    public static void Integrate()
    {
        if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;
        VillagerPrefabSetup.Build();
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        ApplyTerrainColor(Find<World>(scene));
        if (Find<Well>(scene) != null)
        {
            VillagerPrefabSetup.UpgradeScene(scene);
            EnsureGridForest(scene);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            return;
        }
        var world = Find<World>(scene);
        if (world == null || world.GetComponent<MapView>() == null || world.GetComponent<WfcSolver>() == null)
            throw new System.InvalidOperationException("SampleScene WFC grid kurulumu bulunamadı.");

        // Preserve the original demo objects, inactive, instead of deleting user scene data.
        var legacy = new GameObject("Legacy Demo Objects");
        SceneManager.MoveGameObjectToScene(legacy, scene);
        foreach (var root in scene.GetRootGameObjects())
            if (root.GetComponent<Camera>() != null || root.name == "Cube" || root.name == "well")
                root.transform.SetParent(legacy.transform, true);
        legacy.SetActive(false);

        // Only the in-memory copy is moved. ResourceTest.unity is never saved or overwritten.
        var source = EditorSceneManager.OpenScene("Assets/Scenes/ResourceTest.unity", OpenSceneMode.Additive);
        foreach (var root in source.GetRootGameObjects())
            if (root.GetComponent<Light>() == null)
                SceneManager.MoveGameObjectToScene(root, scene);
        EditorSceneManager.CloseScene(source, true);
        SceneManager.SetActiveScene(scene);
        var gameplay = scene.GetRootGameObjects().First(r => r.name == "[Placeholder] Resource Test");
        gameplay.name = "Gameplay";
        foreach (var oldWorld in gameplay.GetComponentsInChildren<World>(true))
            oldWorld.gameObject.SetActive(false);
        var ground = gameplay.transform.Find("Ground");
        if (ground != null) ground.gameObject.SetActive(false);

        // Keep main's 128x128 grid, chunk renderer, material and weighted terrain variants.
        var settings = new SerializedObject(world);
        settings.FindProperty("cellSize").floatValue = 2f;
        settings.ApplyModifiedPropertiesWithoutUndo();
        world.transform.position = new Vector3(-world.Width * world.CellSize / 2f, 0f,
            -world.Height * world.CellSize / 2f);
        ReserveGameplayGround(world, gameplay.transform);
        var view = new SerializedObject(world.GetComponent<MapView>());
        view.FindProperty("createColliders").boolValue = true;
        view.ApplyModifiedPropertiesWithoutUndo();
        var camera = Find<IsometricCamera>(scene).GetComponent<Camera>();
        var well = Find<Well>(scene);
        var controller = Find<WallConstructionController>(scene);
        controller.Configure(world, well, camera);
        controller.SetConstructionMode(false);
        var grid = new GameObject("Construction Grid");
        grid.transform.SetParent(world.transform, false);
        grid.AddComponent<WallDemoGrid>().Configure(world);
        grid.AddComponent<NavIgnore>();
        VillagerPrefabSetup.UpgradeScene(scene);
        EnsureGridForest(scene);
        var iso = new SerializedObject(camera.GetComponent<IsometricCamera>());
        iso.FindProperty("distance").floatValue = 80f;
        iso.FindProperty("panExtents").vector2Value = new Vector2(100f, 100f);
        iso.ApplyModifiedPropertiesWithoutUndo();
        camera.GetComponent<IsometricCamera>().FocusPoint = Quaternion.Euler(0f, 45f, 0f) * new Vector3(-8f, 0f, 0f);
        var scenes = EditorBuildSettings.scenes.Where(s => s.path != ScenePath).ToList();
        scenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
        EditorBuildSettings.scenes = scenes.ToArray();
        EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("SampleScene: kaynak toplama, köy, dağlar, ana menü ve animasyonlu köylüler güncel WFC ile hazır.");
    }

    // Zemin ve orman, ResourceTest'te onaylanan görünümle (feature/wfc-forest): MapView palet malzemesiyle (Flat.mat)
    // çizer, düz zeminde çimenli karolar görünür. Ağaçlar yalnız köyün solundaki orman alanında WFC ile çıkar, alanın
    // dışı zemine sabitlenir (GridForest). WFC ağaçları toplanabilir: kesilince devrilir, vurulunca sallanır.
    // ResourceTest'ten gelen eski tek tek ağaçlı "Forest" kapatılır (silinmez). İnşa grid'i yalnız inşa modunda görünür.
    const string PaletteMaterialPath = "Assets/Materials/Flat.mat";
    const int TreeId = 5;
    const float ForestYaw = 45f;
    // ResourceTest ormanı: kasabanın (yarıçap 36.5 m) 6 m dışında, ekranın solunda 30 x 55 m.
    static readonly Vector3 ForestCenter = Quaternion.Euler(0f, ForestYaw, 0f) * new Vector3(-(36.5f + 6f) - 15f, 0f, 0f);
    static readonly Vector2 ForestSize = new(30f, 55f);
    // ResourceTest'teki ağaçlar 5 m hücreyle çiziliyordu; burada hücre 2 m, ağaçlar aynı boyda olsun.
    const float TreeScale = 2.5f;
    const string BigTreeDir = "Assets/Prefabs/Map";

    public static bool EnsureGridForest(Scene scene)
    {
        var world = Find<World>(scene);
        var view = world != null ? world.GetComponent<MapView>() : null;
        var solver = world != null ? world.GetComponent<WfcSolver>() : null;
        if (view == null || solver == null)
            return false;

        foreach (var root in scene.GetRootGameObjects())
        {
            var oldForest = root.name == "Gameplay" ? root.transform.Find("Forest") : null;
            if (oldForest != null && oldForest.GetComponent<World>() == null)
                oldForest.gameObject.SetActive(false);
        }

        var palette = AssetDatabase.LoadAssetAtPath<Material>(PaletteMaterialPath);
        var viewSettings = new SerializedObject(view);
        viewSettings.FindProperty("material").objectReferenceValue = palette;
        viewSettings.ApplyModifiedPropertiesWithoutUndo();

        var forest = world.GetComponent<GridForest>();
        if (forest == null)
            forest = world.gameObject.AddComponent<GridForest>();
        var settings = new SerializedObject(forest);
        settings.FindProperty("map").objectReferenceValue = world;
        settings.FindProperty("solver").objectReferenceValue = solver;
        settings.FindProperty("forestCenter").vector3Value = ForestCenter;
        settings.FindProperty("forestSize").vector2Value = ForestSize;
        settings.FindProperty("forestYaw").floatValue = ForestYaw;
        settings.FindProperty("treeId").intValue = TreeId;
        settings.FindProperty("groundId").intValue = 0;
        var modules = viewSettings.FindProperty("modules");
        for (int i = 0; i < modules.arraySize; i++)
        {
            var module = modules.GetArrayElementAtIndex(i);
            var prefab = module.FindPropertyRelative("Prefab");
            if (module.FindPropertyRelative("Id").intValue == TreeId && prefab.objectReferenceValue is GameObject tile)
                prefab.objectReferenceValue = BigTreeTile(tile);
        }
        viewSettings.ApplyModifiedPropertiesWithoutUndo();
        var tiles = settings.FindProperty("treeTiles");
        var weights = settings.FindProperty("treeWeights");
        tiles.arraySize = weights.arraySize = 0;
        for (int i = 0; i < modules.arraySize; i++)
        {
            var module = modules.GetArrayElementAtIndex(i);
            var prefab = module.FindPropertyRelative("Prefab").objectReferenceValue;
            if (module.FindPropertyRelative("Id").intValue != TreeId || prefab == null)
                continue;
            int index = tiles.arraySize;
            tiles.arraySize = weights.arraySize = index + 1;
            tiles.GetArrayElementAtIndex(index).objectReferenceValue = prefab;
            weights.GetArrayElementAtIndex(index).intValue = module.FindPropertyRelative("Weight").intValue;
        }
        settings.FindProperty("treeScale").floatValue = TreeScale;
        settings.FindProperty("treeKeep").floatValue = 0.45f;
        settings.FindProperty("treeSpacing").intValue = 1;
        settings.FindProperty("treeMaterial").objectReferenceValue = palette;
        settings.FindProperty("fadeMaterial").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/FlatFade.mat");
        settings.ApplyModifiedPropertiesWithoutUndo();

        // Köy için ayrılan başlangıç hücrelerinden orman alanına düşenler kaldırılır, yoksa orada ağaç çıkmaz.
        var solverSettings = new SerializedObject(solver);
        var cells = solverSettings.FindProperty("initialCells");
        for (int i = cells.arraySize - 1; i >= 0; i--)
        {
            var cell = cells.GetArrayElementAtIndex(i);
            if (forest.InForest(cell.FindPropertyRelative("X").intValue, cell.FindPropertyRelative("Y").intValue))
                cells.DeleteArrayElementAtIndex(i);
        }
        solverSettings.ApplyModifiedPropertiesWithoutUndo();

        var controller = Find<WallConstructionController>(scene);
        foreach (var grid in world.GetComponentsInChildren<WallDemoGrid>(true))
            if (grid.GetComponent<ConstructionGridToggle>() == null)
                grid.gameObject.AddComponent<ConstructionGridToggle>().Configure(controller);
        return true;
    }

    // Ekibin ağaç karosunun büyütülmüş kopyası: zemin plakası çıkarılır (yoksa büyüyünce komşu karoların
    // üstüne biner), ağaçlar karonun ortası etrafında TreeScale kadar büyür, böylece hücrenin ortasında kalır.
    // Ekibin modeline dokunulmaz; mesh ve prefab Assets/Prefabs/Map'e yazılır.
    static GameObject BigTreeTile(GameObject tile)
    {
        if (tile.name.EndsWith("_big"))
            return tile;
        var prefabPath = $"{BigTreeDir}/{tile.name}_big.prefab";
        var existing = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (existing != null)
            return existing;

        var filter = tile.GetComponentInChildren<MeshFilter>();
        var source = filter != null ? filter.sharedMesh : null;
        if (source == null)
            return tile;
        var vertices = source.vertices;
        var kept = new List<int>();
        for (int s = 0; s < source.subMeshCount; s++)
        {
            var triangles = source.GetTriangles(s);
            for (int t = 0; t < triangles.Length; t += 3)
            {
                // Tamamı yerde olan üçgenler zemin plakası (ve gövdelerin görünmeyen alt yüzü).
                if (vertices[triangles[t]].y < 0.001f && vertices[triangles[t + 1]].y < 0.001f && vertices[triangles[t + 2]].y < 0.001f)
                    continue;
                kept.Add(triangles[t]);
                kept.Add(triangles[t + 1]);
                kept.Add(triangles[t + 2]);
            }
        }
        var mesh = Object.Instantiate(source);
        mesh.name = tile.name + "_big";
        mesh.subMeshCount = 1;
        mesh.SetTriangles(kept, 0);
        mesh.RecalculateBounds();
        if (!AssetDatabase.IsValidFolder(BigTreeDir))
            AssetDatabase.CreateFolder("Assets/Prefabs", "Map");
        AssetDatabase.CreateAsset(mesh, $"{BigTreeDir}/{tile.name}_big.asset");

        // Karo merkezi (plakanın ortası) büyütmede yerinde kalsın: kök TreeScale, çocuk merkez × (1/ölçek − 1).
        var center = source.bounds.center;
        center.y = 0f;
        var root = new GameObject(tile.name + "_big");
        root.transform.localScale = Vector3.one * TreeScale;
        var child = new GameObject("Mesh", typeof(MeshFilter), typeof(MeshRenderer));
        child.transform.SetParent(root.transform, false);
        child.transform.localPosition = filter.transform.localPosition + center * (1f / TreeScale - 1f);
        child.transform.localRotation = filter.transform.localRotation;
        child.GetComponent<MeshFilter>().sharedMesh = mesh;
        child.GetComponent<MeshRenderer>().sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(PaletteMaterialPath);
        var prefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        Object.DestroyImmediate(root);
        return prefab;
    }

    public static void RestoreTerrainColor()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        ApplyTerrainColor(Find<World>(scene));
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
    }

    static void ApplyTerrainColor(World world)
    {
        const string path = "Assets/Materials/TerrainGround.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Flat.mat"));
            material.name = "TerrainGround";
            material.SetTexture("_BaseMap", null);
            material.SetTexture("_MainTex", null);
            material.SetColor("_BaseColor", new Color(0.35f, 0.55f, 0.3f));
            material.SetColor("_Color", new Color(0.35f, 0.55f, 0.3f));
            material.SetFloat("_Smoothness", 0f);
            AssetDatabase.CreateAsset(material, path);
        }
        var settings = new SerializedObject(world.GetComponent<MapView>());
        settings.FindProperty("material").objectReferenceValue = material;
        settings.ApplyModifiedPropertiesWithoutUndo();
    }

    static void ReserveGameplayGround(World world, Transform gameplay)
    {
        var bounds = new Bounds(Vector3.zero, Vector3.one * 2f);
        foreach (Transform group in gameplay)
        {
            if (group.name == "Mountains" || group.name == "Ground" ||
                group.GetComponent<World>() != null || group.GetComponent<WallConstructionController>() != null)
                continue;
            foreach (var renderer in group.GetComponentsInChildren<Renderer>(true))
                bounds.Encapsulate(renderer.bounds);
        }
        bounds.Expand(12f);
        var solver = new SerializedObject(world.GetComponent<WfcSolver>());
        var cells = solver.FindProperty("initialCells");
        var pins = new Dictionary<Vector2Int, WfcSolver.FixedCell>();
        for (int i = 0; i < cells.arraySize; i++)
        {
            var cell = cells.GetArrayElementAtIndex(i);
            var pin = new WfcSolver.FixedCell {
                X = cell.FindPropertyRelative("X").intValue, Y = cell.FindPropertyRelative("Y").intValue,
                Id = cell.FindPropertyRelative("Id").intValue, Rotation = cell.FindPropertyRelative("Rotation").intValue
            };
            pins[new Vector2Int(pin.X, pin.Y)] = pin;
        }
        for (int y = 0; y < world.Height; y++)
        for (int x = 0; x < world.Width; x++)
        {
            var center = world.CellCenter(x, y);
            var half = world.CellSize / 2f;
            if (center.x + half >= bounds.min.x && center.x - half <= bounds.max.x &&
                center.z + half >= bounds.min.z && center.z - half <= bounds.max.z)
                pins[new Vector2Int(x, y)] = new WfcSolver.FixedCell { X = x, Y = y, Id = 0, Rotation = 0 };
        }
        cells.arraySize = pins.Count;
        int index = 0;
        foreach (var pin in pins.Values)
        {
            var cell = cells.GetArrayElementAtIndex(index++);
            cell.FindPropertyRelative("X").intValue = pin.X;
            cell.FindPropertyRelative("Y").intValue = pin.Y;
            cell.FindPropertyRelative("Id").intValue = pin.Id;
            cell.FindPropertyRelative("Rotation").intValue = pin.Rotation;
        }
        solver.ApplyModifiedPropertiesWithoutUndo();
    }

    static T Find<T>(Scene scene) where T : Component
    {
        foreach (var root in scene.GetRootGameObjects())
        {
            var component = root.GetComponentsInChildren<T>(true).FirstOrDefault(c => c.gameObject.activeInHierarchy);
            if (component != null) return component;
        }
        return null;
    }
}
