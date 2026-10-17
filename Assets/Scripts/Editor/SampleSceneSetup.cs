using System.Collections.Generic;
using System.Linq;
using IdleTown.Construction;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Bir defalık taşıma: sonraki düzenlemeler doğrudan SampleScene'de yapılır.
public static class SampleSceneSetup
{
    public const string ScenePath = "Assets/Scenes/SampleScene.unity";

    [MenuItem("Tools/BigEs/SampleScene Oynanışını Kur")]
    public static void Integrate()
    {
        if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        if (Find<Well>(scene) != null)
        {
            Debug.Log("SampleScene oynanışı zaten kurulmuş; mevcut sahne düzenlemeleri korundu.");
            return;
        }
        var world = Find<World>(scene);
        if (world == null || world.GetComponent<WfcSolver>() == null || world.GetComponent<MapView>() == null)
            throw new System.InvalidOperationException("SampleScene grid/WFC bileşenleri bulunamadı.");

        // Eski grid demosundaki kamera, platform ve görsel kuyunun yerini çalışan kurulum alır.
        foreach (var root in scene.GetRootGameObjects())
            if (root.GetComponent<Camera>() != null || root.name == "Cube" || root.name == "well")
                Object.DestroyImmediate(root);

        var source = EditorSceneManager.OpenScene("Assets/Scenes/ResourceTest.unity", OpenSceneMode.Additive);
        foreach (var root in source.GetRootGameObjects())
        {
            if (root.GetComponent<Light>() != null)
                continue;
            SceneManager.MoveGameObjectToScene(root, scene);
        }
        EditorSceneManager.CloseScene(source, true);
        SceneManager.SetActiveScene(scene);

        var gameplay = scene.GetRootGameObjects().First(r => r.name == "[Placeholder] Resource Test");
        gameplay.name = "Gameplay";
        var controller = Find<WallConstructionController>(scene);
        var well = Find<Well>(scene);
        var camera = Find<Camera>(scene);
        foreach (var oldWorld in gameplay.GetComponentsInChildren<World>(true))
            Object.DestroyImmediate(oldWorld.gameObject);
        var ground = gameplay.transform.Find("Ground");
        if (ground != null)
            Object.DestroyImmediate(ground.gameObject);

        // 32x32 grid korunur; birim modeller 6 m hücreye ölçeklenir. Köy merkezi (0,0,0).
        var settings = new SerializedObject(world);
        settings.FindProperty("cellSize").floatValue = 6f;
        settings.ApplyModifiedPropertiesWithoutUndo();
        world.transform.position = new Vector3(-world.Width * world.CellSize / 2f, 0f,
            -world.Height * world.CellSize / 2f);
        ReserveGameplayGround(world, gameplay.transform);
        ApplyGreenGround(world);
        controller.Configure(world, well, camera);
        controller.SetConstructionMode(false);
        var grid = new GameObject("Construction Grid");
        grid.transform.SetParent(world.transform, false);
        grid.AddComponent<WallDemoGrid>().Configure(world);
        grid.AddComponent<NavIgnore>();

        var view = new SerializedObject(world.GetComponent<MapView>());
        view.FindProperty("createColliders").boolValue = true;
        view.ApplyModifiedPropertiesWithoutUndo();
        var iso = new SerializedObject(camera.GetComponent<IsometricCamera>());
        iso.FindProperty("distance").floatValue = 100f;
        iso.FindProperty("panExtents").vector2Value = new Vector2(80f, 80f);
        iso.ApplyModifiedPropertiesWithoutUndo();
        camera.GetComponent<IsometricCamera>().FocusPoint = Quaternion.Euler(0f, 45f, 0f) * new Vector3(-8f, 0f, 0f);

        var buildScenes = EditorBuildSettings.scenes.Where(s => s.path != ScenePath).ToList();
        buildScenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
        EditorBuildSettings.scenes = buildScenes.ToArray();
        EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("SampleScene: WFC, ortak inşa gridi, kuyu, köy, orman, madenler, toplayıcılar, HUD ve kamera birleştirildi.");
    }

    [MenuItem("Tools/BigEs/Yesil Zemini Geri Getir")]
    public static void RestoreGreenGround()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new System.InvalidOperationException("Önce Play modunu durdurun.");
        if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;
        var scene = SceneManager.GetActiveScene();
        if (scene.path != ScenePath)
            scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        ApplyGreenGround(Find<World>(scene));
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Debug.Log("SampleScene zemini eski yeşil rengine döndürüldü.");
    }

    static void ApplyGreenGround(World world)
    {
        const string path = "Assets/Materials/TerrainGround.mat";
        var ground = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (ground == null)
        {
            ground = new Material(AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Flat.mat"));
            ground.name = "TerrainGround";
            ground.SetTexture("_BaseMap", null);
            ground.SetTexture("_MainTex", null);
            ground.SetColor("_BaseColor", new Color(0.35f, 0.55f, 0.3f));
            ground.SetColor("_Color", new Color(0.35f, 0.55f, 0.3f));
            ground.SetFloat("_Smoothness", 0f);
            AssetDatabase.CreateAsset(ground, path);
        }
        var view = new SerializedObject(world.GetComponent<MapView>());
        view.FindProperty("material").objectReferenceValue = ground;
        view.ApplyModifiedPropertiesWithoutUndo();
    }

    static void ReserveGameplayGround(World world, Transform gameplay)
    {
        // Kaynaklar, evler ve aralarındaki yollar rastgele yükseltilerin altında kalmasın.
        var bounds = new Bounds(Vector3.zero, Vector3.one * 2f);
        foreach (Transform group in gameplay)
        {
            if (group.name == "Mountains" || group.GetComponent<WallConstructionController>() != null)
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
            var component = root.GetComponentInChildren<T>(true);
            if (component != null) return component;
        }
        return null;
    }
}
