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
            EnsureVillagerJobs(scene);
            EnsureAudio(scene);
            EnsureUpgrades(scene);
            EnsureWellClearing(scene);
            EnsureNavigation(scene);
            EnsureTutorial(scene);
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
        EnsureVillagerJobs(scene);
        EnsureAudio(scene);
        EnsureUpgrades(scene);
        EnsureWellClearing(scene);
        EnsureNavigation(scene);
        EnsureTutorial(scene);
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

    public static bool EnsureGridForest(Scene scene)
    {
        var world = Find<World>(scene);
        var view = world != null ? world.GetComponent<MapView>() : null;
        var solver = world != null ? world.GetComponent<WfcSolver>() : null;
        if (view == null || solver == null)
            return false;
        EnsureTerrain(scene, view, solver);

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
        // Orman dışında WFC serbest: ekibin dağ ve tepeleri çıkar (köy alanı initialCells ile düz).
        settings.FindProperty("pinOutsideForest").boolValue = false;
        var modules = viewSettings.FindProperty("modules");
        for (int i = 0; i < modules.arraySize; i++)
        {
            var module = modules.GetArrayElementAtIndex(i);
            var prefab = module.FindPropertyRelative("Prefab");
            if (module.FindPropertyRelative("Id").intValue == TreeId && prefab.objectReferenceValue is GameObject tile)
                prefab.objectReferenceValue = TeamTreeTile(tile);
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
        // Ağaçlar ekibin karosunun kendi boyunda, WFC'nin koyduğu sıklıkta.
        settings.FindProperty("treeScale").floatValue = 1f;
        settings.FindProperty("treeKeep").floatValue = 1f;
        settings.FindProperty("treeSpacing").intValue = 0;
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

    // Dağlar ve tepeler ekibin grid'inden: tasarımcının SampleScene2'deki WFC kuralları ve karo listesi (yokuş, köşe,
    // düzlük, karlı kayalık...) olduğu gibi bu World'e kopyalanır; SampleScene2'ye ve modellere dokunulmaz. Köy, ocaklar
    // ve kuyunun alanı main kurulumunun başlangıç hücreleriyle (ReserveGameplayGround) düz kalır, dışı rastgele kurulur.
    // Elle konmuş eski dağlar (Gameplay/Mountains) kapatılır, silinmez.
    const string TerrainSourcePath = "Assets/Scenes/SampleScene2.unity";

    static void EnsureTerrain(Scene scene, MapView view, WfcSolver solver)
    {
        foreach (var root in scene.GetRootGameObjects())
        {
            var oldMountains = root.name == "Gameplay" ? root.transform.Find("Mountains") : null;
            if (oldMountains != null)
                oldMountains.gameObject.SetActive(false);
        }

        if (!System.IO.File.Exists(TerrainSourcePath))
        {
            Debug.LogWarning($"{TerrainSourcePath} bulunamadı; dağ/tepe kuralları kopyalanmadı.");
            return;
        }
        var source = SceneManager.GetSceneByPath(TerrainSourcePath);
        bool wasOpen = source.IsValid() && source.isLoaded;
        if (!wasOpen)
            source = EditorSceneManager.OpenScene(TerrainSourcePath, OpenSceneMode.Additive);
        try
        {
            var sourceSolver = Find<WfcSolver>(source);
            var sourceView = Find<MapView>(source);
            if (sourceSolver == null || sourceView == null)
            {
                Debug.LogWarning($"{TerrainSourcePath} içinde WfcSolver/MapView yok; dağ/tepe kuralları kopyalanmadı.");
                return;
            }
            var solverSettings = new SerializedObject(solver);
            solverSettings.CopyFromSerializedProperty(new SerializedObject(sourceSolver).FindProperty("rules"));
            solverSettings.ApplyModifiedPropertiesWithoutUndo();
            var viewSettings = new SerializedObject(view);
            viewSettings.CopyFromSerializedProperty(new SerializedObject(sourceView).FindProperty("modules"));
            viewSettings.ApplyModifiedPropertiesWithoutUndo();
        }
        finally
        {
            if (!wasOpen)
                EditorSceneManager.CloseScene(source, true);
        }
    }

    // Ağaç karosu ekibin modelinin kendisi. Önceki sürümün 2.5 kat büyütülmüş kopyaları (*_big) ekibin .obj'sine
    // geri çevrilir.
    static GameObject TeamTreeTile(GameObject tile)
    {
        if (!tile.name.EndsWith("_big"))
            return tile;
        var original = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Meshes/WorldMeshes/{tile.name.Substring(0, tile.name.Length - 4)}.obj");
        return original != null ? original : tile;
    }

    // Köylüleri elle işe koyma: oduncular işsiz köylü olarak başlar (köyde dolaşır), oyuncu onları imleçle tutup
    // ormana (oduncu) ya da maden ocağına (vagonu iter) bırakır. Vagonlar iten köylü olmadan beklemede kalır
    // (GathererSpawner.needsPusher, varsayılan açık). İmleç dokuları Art/UI/make_menu_sprites.py ile çizilir.
    const string CursorDir = "Assets/UI/Cursor/";

    public static bool EnsureVillagerJobs(Scene scene)
    {
        var camera = Find<IsometricCamera>(scene);
        if (camera == null)
            return false;
        var picker = camera.GetComponent<VillagerPicker>();
        if (picker == null)
            picker = camera.gameObject.AddComponent<VillagerPicker>();
        var settings = new SerializedObject(picker);
        settings.FindProperty("defaultCursor").objectReferenceValue = CursorTexture("cursor_default.png");
        settings.FindProperty("openHand").objectReferenceValue = CursorTexture("cursor_open.png");
        settings.FindProperty("closedHand").objectReferenceValue = CursorTexture("cursor_clenched.png");
        settings.ApplyModifiedPropertiesWithoutUndo();

        foreach (var root in scene.GetRootGameObjects())
            foreach (var worker in root.GetComponentsInChildren<Gatherer>(true))
            {
                if (worker.Type != ResourceType.Wood || worker.GetComponent<VillagerAnimation>() == null)
                    continue;
                var workerSettings = new SerializedObject(worker);
                workerSettings.FindProperty("villager").boolValue = true;
                // Hücre 2 m, gövde ince: kol boyu kadar yakına gelsin (önceki 5 m hücrelik haritada 2.5 m idi).
                workerSettings.FindProperty("stopDistance").floatValue = 1.1f;
                workerSettings.ApplyModifiedPropertiesWithoutUndo();
            }
        return true;
    }

    // Yükseltmeler: her taş ocağı (vagon üreten kaynak) ayrı, orman tek yükseltilir; 3 seviye, odun ve taşla.
    // Butonları ve taş butonunun yanındaki işçi butonunu HUD'daki UpgradeHUD çizer.
    [MenuItem("Tools/BigEs/Yukseltmeleri Kur")]
    public static void SetupUpgrades()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (!EnsureUpgrades(scene))
        {
            Debug.LogWarning("Yükseltmeler kurulamadı: sahnede HUD (ResourceHUD) ya da taş butonu yok.");
            return;
        }
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("Yükseltmeler kuruldu: taş ocakları, orman ve işçi butonu.");
    }

    // Play modunda yükseltme/işçi denemek için kuyuya kaynak ekler (HUD ve butonlar güncellenir).
    [MenuItem("Tools/BigEs/Test: Kuyuya 100 Odun 100 Tas")]
    public static void AddTestResources()
    {
        var well = Well.Instance;
        if (!Application.isPlaying || well == null)
        {
            Debug.LogWarning("Önce Play moduna girip oyunu başlatın.");
            return;
        }
        well.Deposit(ResourceType.Wood, 100);
        well.Deposit(ResourceType.Stone, 100);
    }

    public static bool EnsureUpgrades(Scene scene)
    {
        var hud = Find<ResourceHUD>(scene);
        var stoneButton = hud != null ? hud.transform.Find("Resource Panel/Stone Button") as RectTransform : null;
        if (stoneButton == null)
            return false;

        foreach (var root in scene.GetRootGameObjects())
            foreach (var spawner in root.GetComponentsInChildren<GathererSpawner>(true))
                if (spawner.GetComponent<UpgradeLevel>() == null)
                    spawner.gameObject.AddComponent<UpgradeLevel>();

        var forest = Find<GridForest>(scene);
        if (forest != null)
        {
            var upgrade = forest.GetComponent<UpgradeLevel>();
            if (upgrade == null)
                upgrade = forest.gameObject.AddComponent<UpgradeLevel>();
            var anchor = forest.transform.Find("Forest Upgrade Anchor");
            if (anchor == null)
            {
                anchor = new GameObject("Forest Upgrade Anchor").transform;
                anchor.SetParent(forest.transform, false);
            }
            anchor.position = forest.ForestCenter + Vector3.up * 4f;
            var settings = new SerializedObject(upgrade);
            settings.FindProperty("displayName").stringValue = "Orman";
            settings.FindProperty("forest").boolValue = true;
            settings.FindProperty("anchor").objectReferenceValue = anchor;
            settings.ApplyModifiedPropertiesWithoutUndo();
        }

        var upgrades = hud.GetComponent<UpgradeHUD>();
        if (upgrades == null)
            upgrades = hud.gameObject.AddComponent<UpgradeHUD>();
        var ui = new SerializedObject(upgrades);
        ui.FindProperty("template").objectReferenceValue = stoneButton;
        ui.FindProperty("woodIcon").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/UI/Icons/Wood.png");
        ui.FindProperty("stoneIcon").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/UI/Icons/Stone.png");
        ui.FindProperty("workerPrefab").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Gatherer>("Assets/Prefabs/Economy/Woodcutter.prefab");
        ui.ApplyModifiedPropertiesWithoutUndo();
        EnsureWorkerTemplate(scene);
        return true;
    }

    // Oyun köylüsüz başlar: oyuncu elle topladığıyla işçi alır. Sahnedeki köylülerden biri kapalı "Worker Template"
    // olarak kalır ve yeni işçiler onun ayarlarını (boy, hız, mesafeler) alır; diğerleri silinir.
    public static bool EnsureWorkerTemplate(Scene scene)
    {
        var hud = Find<UpgradeHUD>(scene);
        if (hud == null)
            return false;

        Gatherer template = null;
        var extra = new List<Gatherer>();
        foreach (var root in scene.GetRootGameObjects())
            foreach (var worker in root.GetComponentsInChildren<Gatherer>(true))
            {
                if (worker.Type != ResourceType.Wood || worker.GetComponent<VillagerAnimation>() == null)
                    continue;
                if (template == null || (worker.name == WorkerTemplateName && template.name != WorkerTemplateName))
                {
                    if (template != null)
                        extra.Add(template);
                    template = worker;
                }
                else
                    extra.Add(worker);
            }
        if (template == null)
            return false;

        foreach (var worker in extra)
            Object.DestroyImmediate(worker.gameObject);
        template.name = WorkerTemplateName;
        template.gameObject.SetActive(false);
        var workerSettings = new SerializedObject(template);
        workerSettings.FindProperty("idleMarkerMaterial").objectReferenceValue = IdleMarkerMaterial();
        workerSettings.FindProperty("markerScreenSize").floatValue = 0.11f;
        workerSettings.ApplyModifiedPropertiesWithoutUndo();

        var ui = new SerializedObject(hud);
        ui.FindProperty("workerTemplate").objectReferenceValue = template;
        ui.ApplyModifiedPropertiesWithoutUndo();
        return true;
    }

    const string WorkerTemplateName = "Worker Template";
    const string IdleMarkerPath = "Assets/Materials/IdleMarker.mat";

    // Boştaki köylünün belirteci: ışıktan etkilenmeyen arayüzün su mavisi (#2EC4D6). Sarı, sarımsı yeşil çimende
    // seçilmiyordu.
    static Material IdleMarkerMaterial()
    {
        var material = AssetDatabase.LoadAssetAtPath<Material>(IdleMarkerPath);
        if (material == null)
        {
            material = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = "IdleMarker" };
            AssetDatabase.CreateAsset(material, IdleMarkerPath);
        }
        material.SetColor("_BaseColor", new Color(0.18f, 0.77f, 0.84f));
        EditorUtility.SetDirty(material);
        return material;
    }

    // Yol bulma köylüler için: 0.8 m (vagon genişliği) evlerin ve ağaçların arasındaki 2 m'lik geçitleri kapatıyordu;
    // köylüler yolu bulamayıp uzaktan vuruyor, kaçarken takılıyordu.
    public static bool EnsureNavigation(Scene scene)
    {
        var baker = Find<NavigationBaker>(scene);
        if (baker == null)
            return false;
        var settings = new SerializedObject(baker);
        settings.FindProperty("agentRadius").floatValue = 0.45f;
        settings.ApplyModifiedPropertiesWithoutUndo();
        return true;
    }

    // Kuyunun çevresinde ağaç çıkmaz (WfcSolver.clearCenter): işçiler odunu uzaktan taşısın.
    public static bool EnsureWellClearing(Scene scene)
    {
        var solver = Find<WfcSolver>(scene);
        var well = Find<Well>(scene);
        if (solver == null || well == null)
            return false;
        var settings = new SerializedObject(solver);
        settings.FindProperty("clearCenter").objectReferenceValue = well.transform;
        settings.ApplyModifiedPropertiesWithoutUndo();
        return true;
    }

    // Oyun başındaki ipucu kartları (TutorialHints): kart oyun sırasında kodla kurulur, burada yalnız menünün
    // yazı tipi ve panel sprite'ı bağlanır.
    public static bool EnsureTutorial(Scene scene)
    {
        var hints = Find<TutorialHints>(scene);
        if (hints == null)
        {
            var host = new GameObject("Tutorial");
            SceneManager.MoveGameObjectToScene(host, scene);
            hints = host.AddComponent<TutorialHints>();
        }
        var settings = new SerializedObject(hints);
        settings.FindProperty("font").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Font>("Assets/UI/Fonts/Rubik-Bold.ttf");
        settings.FindProperty("panelSprite").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/UI/Menu/Panel.png");
        settings.ApplyModifiedPropertiesWithoutUndo();
        return true;
    }

    [MenuItem("Tools/BigEs/Tutorial'ı Sıfırla")]
    public static void ResetTutorial()
    {
        PlayerPrefs.DeleteKey(TutorialHints.DonePref);
        PlayerPrefs.Save();
        Debug.Log("Tutorial sıfırlandı: bir sonraki oyunda yeniden gösterilecek.");
    }

    // Oyun içi kısa sesler (GameAudio): Kenney RPG Audio ve Impact Sounds (CC0) ile bizim ürettiğimiz vagon sesi
    // (Art/Audio/make_cart_roll.py). Hepsi Assets/Audio/SFX'te.
    const string SfxDir = "Assets/Audio/SFX/";

    public static bool EnsureAudio(Scene scene)
    {
        var audio = Find<GameAudio>(scene);
        if (audio == null)
        {
            var host = new GameObject("Game Audio");
            SceneManager.MoveGameObjectToScene(host, scene);
            audio = host.AddComponent<GameAudio>();
        }
        // Kullanıcı: ağaca vurmada tek, tok ses olsun (cırt sesli "chop" ve diğer çeşitler çıkarıldı).
        audio.treeHit = Clips("impactWood_medium_000");
        audio.treeCrack = Clips("creak1", "creak2", "creak3");
        audio.treeThud = Clips("impactWood_heavy_000", "impactWood_heavy_001", "impactWood_heavy_002");
        // Kullanıcının Pixabay "heavy rock rolling" kaydından kendi kestiği tok, kıtırtılı taş sesi.
        audio.stoneHit = new[] { AssetDatabase.LoadAssetAtPath<AudioClip>(SfxDir + "RockBreak.mp3") }.Where(c => c != null).ToArray();
        audio.cartRoll = AssetDatabase.LoadAssetAtPath<AudioClip>(SfxDir + "CartRoll.wav");
        audio.cartClank = Clips("impactMetal_light_000", "impactMetal_light_001", "impactMetal_light_002");
        EditorUtility.SetDirty(audio);
        return true;
    }

    static AudioClip[] Clips(params string[] names) =>
        names.Select(n => AssetDatabase.LoadAssetAtPath<AudioClip>(SfxDir + n + ".ogg")).Where(c => c != null).ToArray();

    static Texture2D CursorTexture(string file)
    {
        var path = CursorDir + file;
        if (AssetImporter.GetAtPath(path) is TextureImporter importer && importer.textureType != TextureImporterType.Cursor)
        {
            importer.textureType = TextureImporterType.Cursor;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
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
