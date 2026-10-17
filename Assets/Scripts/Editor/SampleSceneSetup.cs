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

    // World'ün WFC ağaçlarını (id 5) toplanabilir yapar: kesilince devrilir, vurulunca sallanır (GridForest).
    // ResourceTest'ten gelen eski tek tek ağaçlı "Forest" kapatılır (silinmez); oduncular WFC ağaçlarına gider.
    public static bool EnsureGridForest(Scene scene)
    {
        var world = Find<World>(scene);
        var view = world != null ? world.GetComponent<MapView>() : null;
        if (view == null)
            return false;

        bool changed = false;
        foreach (var root in scene.GetRootGameObjects())
        {
            var oldForest = root.name == "Gameplay" ? root.transform.Find("Forest") : null;
            if (oldForest != null && oldForest.gameObject.activeSelf && oldForest.GetComponent<World>() == null)
            {
                oldForest.gameObject.SetActive(false);
                changed = true;
            }
        }
        if (world.GetComponent<GridForest>() != null)
            return changed;

        const int treeId = 5;
        var forest = world.gameObject.AddComponent<GridForest>();
        var settings = new SerializedObject(forest);
        settings.FindProperty("map").objectReferenceValue = world;
        settings.FindProperty("solver").objectReferenceValue = world.GetComponent<WfcSolver>();
        settings.FindProperty("treeId").intValue = treeId;
        settings.FindProperty("groundId").intValue = 0;
        var modules = new SerializedObject(view).FindProperty("modules");
        var tiles = settings.FindProperty("treeTiles");
        var weights = settings.FindProperty("treeWeights");
        int count = 0;
        for (int i = 0; i < modules.arraySize; i++)
        {
            var module = modules.GetArrayElementAtIndex(i);
            var prefab = module.FindPropertyRelative("Prefab").objectReferenceValue;
            if (module.FindPropertyRelative("Id").intValue != treeId || prefab == null)
                continue;
            tiles.arraySize = weights.arraySize = count + 1;
            tiles.GetArrayElementAtIndex(count).objectReferenceValue = prefab;
            weights.GetArrayElementAtIndex(count).intValue = module.FindPropertyRelative("Weight").intValue;
            count++;
        }
        settings.FindProperty("treeMaterial").objectReferenceValue = new SerializedObject(view).FindProperty("material").objectReferenceValue;
        settings.FindProperty("fadeMaterial").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/FlatFade.mat");
        settings.ApplyModifiedPropertiesWithoutUndo();
        return true;
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
