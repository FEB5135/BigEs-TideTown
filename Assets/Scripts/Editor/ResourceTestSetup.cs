using System.IO;
using IdleTown.Construction;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

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
            var open = SceneManager.GetActiveScene();
            if (open.path != ScenePath)
                return;
            bool changed = UpgradePlaceholderTrees();
            changed |= EnsureGameplayFeatures();
            if (changed)
            {
                EditorSceneManager.SaveScene(open);
                Debug.Log("Kaynak test sahnesi güncellendi.");
            }
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
        EnsureGameplayFeatures();
        EditorSceneManager.SaveScene(scene, ScenePath);

        if (mode == NewSceneMode.Additive)
        {
            SceneManager.SetActiveScene(current);
            EditorSceneManager.CloseScene(scene, true);
        }
        Debug.Log($"Kaynak test sahnesi oluşturuldu: {ScenePath}");
    }

    // Sahne ağaç paketi gelmeden oluşturulduysa placeholder'ları gerçek ağaçlarla yeniden kurar.
    static bool UpgradePlaceholderTrees()
    {
        if (FindTreePrefabs().Length == 0)
            return false;
        var root = GameObject.Find(RootName);
        var forest = root != null ? root.transform.Find("Forest") : null;
        if (forest == null || forest.childCount == 0 || PrefabUtility.IsPartOfPrefabInstance(forest.GetChild(0).gameObject))
            return false;

        Object.DestroyImmediate(root);
        Build();
        return true;
    }

    // İzometrik kamera, tıklayarak toplama, EventSystem ve sol alt kaynak paneli; eksik olanları ekler.
    internal static bool EnsureGameplayFeatures()
    {
        bool changed = false;

        var camera = Camera.main;
        if (camera != null && camera.GetComponent<IsometricCamera>() == null)
        {
            var iso = camera.gameObject.AddComponent<IsometricCamera>();
            // Köy ile orman arasına, ekranın biraz soluna bakar.
            iso.FocusPoint = Quaternion.Euler(0f, 45f, 0f) * new Vector3(-8f, 0f, 0f);
            changed = true;
        }
        if (camera != null && camera.GetComponent<ClickHarvester>() == null)
        {
            camera.gameObject.AddComponent<ClickHarvester>();
            changed = true;
        }

        if (Object.FindAnyObjectByType<NavigationBaker>() == null)
        {
            new GameObject("Navigation", typeof(NavigationBaker));
            changed = true;
        }

        if (Object.FindAnyObjectByType<EventSystem>() == null)
        {
            new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            changed = true;
        }

        if (Object.FindAnyObjectByType<ResourceHUD>() == null)
        {
            BuildHud();
            changed = true;
        }
        changed |= EnsureConstructionFeatures(camera);
        changed |= MainMenuSetup.EnsureMenu();
        return changed;
    }

    // Kaynak test haritası dünya merkezinin iki tarafına yayıldığı için World kökeni negatife
    // taşınır. 120x120 ve 2 m hücre, mevcut 300 m zeminin kullanılabilir bölümünü kapsar.
    static bool EnsureConstructionFeatures(Camera camera)
    {
        var well = Object.FindAnyObjectByType<Well>();
        if (well == null)
            return false;

        bool changed = false;
        var world = Object.FindAnyObjectByType<World>();
        if (world == null)
        {
            var worldObject = new GameObject("Construction World");
            var root = GameObject.Find(RootName);
            if (root != null)
                worldObject.transform.SetParent(root.transform, false);
            worldObject.transform.position = new Vector3(-120f, 0f, -120f);
            world = worldObject.AddComponent<World>();
            var serializedWorld = new SerializedObject(world);
            serializedWorld.FindProperty("width").intValue = 120;
            serializedWorld.FindProperty("height").intValue = 120;
            serializedWorld.FindProperty("cellSize").floatValue = 2f;
            serializedWorld.ApplyModifiedPropertiesWithoutUndo();

            var grid = new GameObject("Visible Construction Grid");
            grid.transform.SetParent(worldObject.transform, false);
            grid.AddComponent<WallDemoGrid>().Configure(world);
            changed = true;
        }

        var controller = Object.FindAnyObjectByType<WallConstructionController>();
        if (controller == null)
        {
            var controllerObject = new GameObject("Wall Construction");
            var root = GameObject.Find(RootName);
            if (root != null)
                controllerObject.transform.SetParent(root.transform, false);
            controller = controllerObject.AddComponent<WallConstructionController>();
            controller.Configure(world, well, camera);
            changed = true;
        }
        else
        {
            // Daha önce SimpleWallResourceStore veya başka bir World ile kurulmuş sahneleri de
            // aynı komutla güvenli biçimde gerçek merkezi depoya yükselt.
            var serializedController = new SerializedObject(controller);
            var worldProperty = serializedController.FindProperty("world");
            var storeProperty = serializedController.FindProperty("resourceStoreSource");
            var cameraProperty = serializedController.FindProperty("inputCamera");
            if (worldProperty.objectReferenceValue != world ||
                storeProperty.objectReferenceValue != well ||
                cameraProperty.objectReferenceValue != camera)
            {
                controller.Configure(world, well, camera);
                changed = true;
            }
        }

        // ResourceTest mevcut toplama demosu olarak açılır. Eski controller'lar da tek seferde
        // aynı başlangıç moduna yükseltilir; bakiye burada asla üretilmez veya yenilenmez.
        if (controller.ConstructionModeActive)
        {
            controller.SetConstructionMode(false);
            changed = true;
        }
        return changed;
    }

    [MenuItem("Tools/BigEs/Kaynak Test Sahnesine Duvar Entegrasyonu Ekle")]
    public static void IntegrateResourceTestScene()
    {
        if (!File.Exists(ScenePath))
        {
            Debug.LogError($"Sahne bulunamadı: {ScenePath}");
            return;
        }
        if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;

        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        if (EnsureGameplayFeatures())
            EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("ResourceTest sahnesi Well tabanlı duvar inşasıyla hazır.");
    }

    static void BuildHud()
    {
        var canvasGo = new GameObject("HUD", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGo.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        var panel = new GameObject("Resource Panel", typeof(RectTransform), typeof(HorizontalLayoutGroup)).GetComponent<RectTransform>();
        panel.SetParent(canvasGo.transform, false);
        panel.anchorMin = panel.anchorMax = panel.pivot = Vector2.zero;
        panel.anchoredPosition = new Vector2(24f, 24f);
        panel.sizeDelta = new Vector2(2f * 240f + 14f, 88f);
        var layout = panel.GetComponent<HorizontalLayoutGroup>();
        layout.spacing = 14f;
        layout.childControlWidth = layout.childControlHeight = false;
        layout.childForceExpandWidth = layout.childForceExpandHeight = false;

        var hud = canvasGo.AddComponent<ResourceHUD>();
        var entries = new ResourceHUD.Entry[]
        {
            ResourceButton(panel, ResourceType.Wood, "Assets/UI/Icons/Wood.png"),
            ResourceButton(panel, ResourceType.Stone, "Assets/UI/Icons/Stone.png"),
        };
        var so = new SerializedObject(hud);
        var list = so.FindProperty("entries");
        list.arraySize = entries.Length;
        for (int i = 0; i < entries.Length; i++)
        {
            var element = list.GetArrayElementAtIndex(i);
            element.FindPropertyRelative("type").enumValueIndex = (int)entries[i].type;
            element.FindPropertyRelative("amountText").objectReferenceValue = entries[i].amountText;
            element.FindPropertyRelative("pulseTarget").objectReferenceValue = entries[i].pulseTarget;
        }
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    const string ButtonSprite = "Assets/UI/Kenney/buttonLong_brown.png";
    const string ButtonPressedSprite = "Assets/UI/Kenney/buttonLong_brown_pressed.png";
    const string IconSlotSprite = "Assets/UI/Kenney/panelInset_beige.png";

    // Ahşap çerçeveli buton, solda parşömen zeminli ikon yuvası, sağda miktar.
    static ResourceHUD.Entry ResourceButton(Transform parent, ResourceType type, string iconPath)
    {
        var button = new GameObject($"{type} Button", typeof(RectTransform), typeof(Image), typeof(Button)).GetComponent<RectTransform>();
        button.SetParent(parent, false);
        button.sizeDelta = new Vector2(240f, 88f);
        var background = button.GetComponent<Image>();
        background.sprite = LoadSprite(ButtonSprite, new Vector4(10f, 12f, 10f, 10f));
        background.type = Image.Type.Sliced;
        background.pixelsPerUnitMultiplier = 0.5f; // Çerçeve kalınlığını ikiye katlar.
        var uiButton = button.GetComponent<Button>();
        uiButton.transition = Selectable.Transition.SpriteSwap;
        uiButton.spriteState = new SpriteState
        {
            pressedSprite = LoadSprite(ButtonPressedSprite, new Vector4(10f, 8f, 10f, 10f)),
        };

        // Alttaki gölge payı kadar yukarıda dursun.
        var slot = new GameObject("Icon Slot", typeof(RectTransform), typeof(Image)).GetComponent<RectTransform>();
        slot.SetParent(button, false);
        slot.anchorMin = slot.anchorMax = slot.pivot = new Vector2(0f, 0.5f);
        slot.anchoredPosition = new Vector2(12f, 3f);
        slot.sizeDelta = new Vector2(66f, 66f);
        var slotImage = slot.GetComponent<Image>();
        slotImage.sprite = LoadSprite(IconSlotSprite, new Vector4(12f, 12f, 12f, 12f));
        slotImage.type = Image.Type.Sliced;
        slotImage.pixelsPerUnitMultiplier = 0.75f;
        slotImage.raycastTarget = false;

        var icon = new GameObject("Icon", typeof(RectTransform), typeof(Image)).GetComponent<RectTransform>();
        icon.SetParent(slot, false);
        icon.anchorMin = Vector2.zero;
        icon.anchorMax = Vector2.one;
        icon.offsetMin = new Vector2(7f, 7f);
        icon.offsetMax = new Vector2(-7f, -7f);
        var iconImage = icon.GetComponent<Image>();
        iconImage.sprite = LoadSprite(iconPath, Vector4.zero);
        iconImage.preserveAspect = true;
        iconImage.raycastTarget = false;

        var label = new GameObject("Amount", typeof(RectTransform), typeof(Text), typeof(Outline)).GetComponent<RectTransform>();
        label.SetParent(button, false);
        label.anchorMin = Vector2.zero;
        label.anchorMax = Vector2.one;
        label.offsetMin = new Vector2(92f, 3f);
        label.offsetMax = new Vector2(-16f, 3f);
        var text = label.GetComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = 40;
        text.fontStyle = FontStyle.Bold;
        text.alignment = TextAnchor.MiddleLeft;
        text.color = new Color(1f, 0.95f, 0.82f);
        text.text = "0";
        text.raycastTarget = false;
        var outline = label.GetComponent<Outline>();
        outline.effectColor = new Color(0.24f, 0.14f, 0.07f, 1f);
        outline.effectDistance = new Vector2(2f, -2f);

        return new ResourceHUD.Entry { type = type, amountText = text, pulseTarget = button };
    }

    // Dokuyu sprite olarak içe aktarır; border 9-slice kenarlarıdır (sol, alt, sağ, üst; piksel).
    static Sprite LoadSprite(string path, Vector4 border)
    {
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer != null && (importer.textureType != TextureImporterType.Sprite || importer.spriteBorder != border))
        {
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spriteBorder = border;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
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
        EnsureGameplayFeatures();
    }

    static void Build()
    {
        var root = new GameObject(RootName);
        Undo.RegisterCreatedObjectUndo(root, "Kaynak Test Sahnesi Kur");

        // Kamera 45° döndüğü için "ekranın sağı/solu" dünya eksenlerinin 45° döndürülmüşü.
        var screenFrame = Quaternion.Euler(0f, 45f, 0f);
        if (Object.FindAnyObjectByType<Terrain>() == null)
        {
            var ground = Primitive(PrimitiveType.Plane, "Ground", root.transform, Vector3.zero, new Vector3(30f, 1f, 30f), new Color(0.35f, 0.55f, 0.3f));
            ground.isStatic = true;
        }
        BuildMountains(root.transform, screenFrame);

        BuildWell(root.transform);
        BuildVillage(root.transform, screenFrame * Vector3.left, screenFrame * Vector3.right);
        EconomyPrefabs.Build();
        BuildQuarries(root.transform, screenFrame);

        // Orman: kasabanın dışında, ekranın solunda.
        // Kamera 45° döndüğü için orman da 45° döndürülür, böylece ekranda köyün tam soluna düşer.
        var forest = new GameObject("Forest").transform;
        forest.SetParent(root.transform);
        forest.localRotation = Quaternion.Euler(0f, 45f, 0f);
        var random = new System.Random(1234);
        var treePrefabs = FindTreePrefabs();
        for (int i = 0; i < 34; i++)
        {
            var pos = new Vector3(-(TownRadius + 6f) - (float)random.NextDouble() * 30f, 0f, -28f + (float)random.NextDouble() * 56f);
            float height = 6f + (float)random.NextDouble() * 3f;
            var tree = treePrefabs.Length > 0
                ? TreeFromPrefab(treePrefabs[random.Next(treePrefabs.Length)], forest, pos, height, (float)random.NextDouble() * 360f)
                : PlaceholderTree(forest, pos);
            tree.name = $"Tree {i + 1}";
            tree.AddComponent<ResourceNode>();
        }

        // Odun toplayan köylüler: kasabanın orman tarafındaki kenarında başlar.
        for (int i = 0; i < 5; i++)
        {
            var start = screenFrame * new Vector3(-(TownRadius + 2f), 1f, -6f + i * 3f);
            var villager = Primitive(PrimitiveType.Capsule, $"Woodcutter {i + 1}", root.transform, start, Vector3.one * 0.8f, new Color(0.85f, 0.7f, 0.5f));
            Object.DestroyImmediate(villager.GetComponent<Collider>());
            var log = Primitive(PrimitiveType.Cube, "Carried Wood", villager.transform, Vector3.zero, Vector3.one, new Color(0.4f, 0.25f, 0.1f));
            Object.DestroyImmediate(log.GetComponent<Collider>());
            log.transform.localPosition = new Vector3(0f, 1.4f, 0f);
            log.transform.localScale = new Vector3(0.4f, 0.4f, 1f);
            var gatherer = villager.AddComponent<Gatherer>();
            var so = new SerializedObject(gatherer);
            so.FindProperty("carryVisual").objectReferenceValue = log;
            so.FindProperty("moveSpeed").floatValue = 5f; // harita büyüdü, yol uzun
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        EditorSceneManager.MarkSceneDirty(root.scene);
        Selection.activeGameObject = root;
    }

    const string WellModelPath = "Assets/Meshes/well.obj";
    const string PaletteMaterialPath = "Assets/Materials/Flat.mat";
    // well.obj'de iç duvar yarıçapı dış yarıçapın ~%78'i (3.62 / 4.64 m),
    // içerideki taban ise yüksekliğin ~%34'ünde (0.98 / 2.85 m).
    const float WellInnerRatio = 0.78f;
    const float WellFloorRatio = 0.344f;

    // Ölçek: konsept evlerin ortalama genişliği ~3.8 m; kuyunun çapı 5 ev genişliği.
    const float AverageHouseWidth = 3.8f;
    const float WellDiameter = AverageHouseWidth * 5f;

    // Köyün ortasındaki kuyu: ekibin modeli (yoksa taş halka), ortada karanlık boşluk ve içine düşeni yakalayan tetikleyici.
    static void BuildWell(Transform parent)
    {
        var well = new GameObject("Well");
        well.transform.SetParent(parent, false);
        well.AddComponent<Well>();

        var model = AssetDatabase.LoadAssetAtPath<GameObject>(WellModelPath);
        float outerRadius, innerRadius, height, floorHeight = 0f;
        if (model != null)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(model, well.transform);
            instance.name = "Model";
            foreach (var filter in instance.GetComponentsInChildren<MeshFilter>())
                filter.gameObject.AddComponent<MeshCollider>().sharedMesh = filter.sharedMesh;
            // Model renklerini UV'leriyle palette.png'den alıyor; .mtl repoda olmadığı için
            // içe aktarılan malzeme beyaz kalıyor, ekibin palet malzemesi atanır.
            var palette = AssetDatabase.LoadAssetAtPath<Material>(PaletteMaterialPath);
            if (palette != null)
                foreach (var renderer in instance.GetComponentsInChildren<Renderer>())
                    renderer.sharedMaterials = System.Array.ConvertAll(renderer.sharedMaterials, _ => palette);
            var bounds = RendererBounds(instance);
            instance.transform.localScale *= WellDiameter / (2f * Mathf.Max(bounds.extents.x, bounds.extents.z));
            bounds = RendererBounds(instance);
            outerRadius = Mathf.Max(bounds.extents.x, bounds.extents.z);
            innerRadius = outerRadius * WellInnerRatio;
            height = bounds.size.y;
            floorHeight = height * WellFloorRatio;
        }
        else
        {
            outerRadius = WellDiameter * 0.5f;
            innerRadius = outerRadius * WellInnerRatio;
            height = 2.5f;
            BuildStoneRing(well.transform, (outerRadius + innerRadius) * 0.5f, outerRadius - innerRadius, height);
        }

        // Karanlık disk tabanın hemen üstünde: kuyu derin bir boşluk gibi görünür.
        var hole = Primitive(PrimitiveType.Cylinder, "Hole", well.transform, new Vector3(0f, floorHeight + 0.02f, 0f),
            new Vector3(innerRadius * 2f, 0.01f, innerRadius * 2f), new Color(0.05f, 0.05f, 0.07f));
        Object.DestroyImmediate(hole.GetComponent<Collider>());

        // Kare tetikleyici iç çembere sığsın diye kenarı yarıçap * √2.
        float intakeSize = innerRadius * 1.41f;
        var intake = new GameObject("Intake", typeof(BoxCollider), typeof(WellIntake));
        intake.transform.SetParent(well.transform, false);
        var trigger = intake.GetComponent<BoxCollider>();
        trigger.isTrigger = true;
        trigger.center = new Vector3(0f, height * 0.5f, 0f);
        trigger.size = new Vector3(intakeSize, height, intakeSize);

        var mouth = new GameObject("Drop Point").transform;
        mouth.SetParent(well.transform, false);
        mouth.localPosition = new Vector3(0f, height, 0f);
        var so = new SerializedObject(well.GetComponent<Well>());
        so.FindProperty("dropPoint").objectReferenceValue = mouth;
        so.FindProperty("approachRadius").floatValue = outerRadius + 0.8f;
        so.FindProperty("innerRadius").floatValue = innerRadius;
        so.FindProperty("targetDepth").floatValue = Mathf.Max(0.5f, (height - floorHeight) * 0.6f);
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    // Ekranın sağında yüksek, karlı dağ sırası (konsept placeholder). Kasabanın arkasındaki vadinin
    // (tsunaminin geleceği yön) sağ duvarı bu sıra, sol duvarı ise tek bir dağ.
    static void BuildMountains(Transform parent, Quaternion screenFrame)
    {
        var range = new GameObject("Mountains").transform;
        range.SetParent(parent, false);
        range.localRotation = screenFrame;

        var rock = ColorMaterial(new Color(0.46f, 0.43f, 0.4f));
        var snow = ColorMaterial(new Color(0.93f, 0.95f, 0.98f));
        var random = new System.Random(42);
        for (int i = 0; i < 12; i++)
        {
            float t = i / 11f;
            var position = new Vector3(TownRadius + 16f + (float)random.NextDouble() * 24f, 0f, Mathf.Lerp(-55f, 110f, t) + ((float)random.NextDouble() - 0.5f) * 8f);
            float radius = 18f + (float)random.NextDouble() * 10f;
            float height = 32f + (float)random.NextDouble() * 26f;
            // Maden ocaklarının hizasındaki dağlar geri itilir; yoksa eteği ocağı kaplar ve kamerada önünü kapatır.
            foreach (float offset in QuarryOffsets)
                if (Mathf.Abs(position.z - offset) < radius + 10f)
                    position.x = Mathf.Max(position.x, QuarryDistance + 12f + radius * 0.9f);

            var mountain = new GameObject($"Mountain {i + 1}", typeof(MeshFilter), typeof(MeshRenderer));
            mountain.transform.SetParent(range, false);
            mountain.transform.localPosition = position;
            mountain.transform.localRotation = Quaternion.Euler(0f, (float)random.NextDouble() * 360f, 0f);
            mountain.GetComponent<MeshFilter>().sharedMesh = MountainMesh(random, radius, height, 9);
            mountain.GetComponent<MeshRenderer>().sharedMaterials = new[] { rock, snow };
        }

        // Vadinin sol duvarı: ormanın arkasında tek, büyük dağ.
        var valleyLeft = new GameObject("Valley Left Mountain", typeof(MeshFilter), typeof(MeshRenderer));
        valleyLeft.transform.SetParent(range, false);
        valleyLeft.transform.localPosition = new Vector3(-(TownRadius + 18f), 0f, 105f);
        valleyLeft.GetComponent<MeshFilter>().sharedMesh = MountainMesh(random, 30f, 55f, 11);
        valleyLeft.GetComponent<MeshRenderer>().sharedMaterials = new[] { rock, snow };
    }

    // Alçak poligonlu dağ: taban halkası, omuz halkası ve zirve. Omuzdan zirveye kadar olan kısım karlı (2. alt mesh).
    static Mesh MountainMesh(System.Random random, float radius, float height, int sides)
    {
        var bottom = new Vector3[sides];
        var shoulder = new Vector3[sides];
        for (int i = 0; i < sides; i++)
        {
            float angle = (i + ((float)random.NextDouble() - 0.5f) * 0.4f) * Mathf.PI * 2f / sides;
            var direction = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
            float bottomRadius = radius * (0.8f + (float)random.NextDouble() * 0.4f);
            bottom[i] = direction * bottomRadius;
            shoulder[i] = direction * bottomRadius * (0.38f + (float)random.NextDouble() * 0.15f)
                + Vector3.up * height * (0.5f + (float)random.NextDouble() * 0.12f);
        }
        var peak = new Vector3(((float)random.NextDouble() - 0.5f) * radius * 0.15f, height, ((float)random.NextDouble() - 0.5f) * radius * 0.15f);

        var vertices = new System.Collections.Generic.List<Vector3>();
        var rockTriangles = new System.Collections.Generic.List<int>();
        var snowTriangles = new System.Collections.Generic.List<int>();
        var below = new Vector3(0f, -height, 0f);
        void Triangle(System.Collections.Generic.List<int> list, Vector3 a, Vector3 b, Vector3 c)
        {
            // Her üçgen kendi köşelerini alır (köşeli, düz gölgeli görünüm); yüzü dışa baksın.
            if (Vector3.Dot(Vector3.Cross(b - a, c - a), (a + b + c) / 3f - below) < 0f)
                (b, c) = (c, b);
            list.Add(vertices.Count); vertices.Add(a);
            list.Add(vertices.Count); vertices.Add(b);
            list.Add(vertices.Count); vertices.Add(c);
        }
        for (int i = 0; i < sides; i++)
        {
            int next = (i + 1) % sides;
            Triangle(rockTriangles, bottom[i], bottom[next], shoulder[next]);
            Triangle(rockTriangles, bottom[i], shoulder[next], shoulder[i]);
            Triangle(snowTriangles, shoulder[i], shoulder[next], peak);
        }

        var mesh = new Mesh { name = "Mountain" };
        mesh.SetVertices(vertices);
        mesh.subMeshCount = 2;
        mesh.SetTriangles(rockTriangles, 0);
        mesh.SetTriangles(snowTriangles, 1);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    // Ocakların kasaba merkezine uzaklığı ve ekranın derinlik yönündeki konumları (dağ sırası çerçevesinde).
    const float QuarryDistance = TownRadius + 9f;
    static readonly float[] QuarryOffsets = { -16f, 0f, 16f };

    // Dağ eteklerinde maden ocakları (taş kaynağı): ekonomi prefab'ı. Girişleri kasabaya bakar.
    // Her ocak kendi vagonunu oyun başında üretir; vagonlar NavMesh üzerinden ocaklarına gider.
    static void BuildQuarries(Transform parent, Quaternion screenFrame)
    {
        var quarries = new GameObject("Quarries").transform;
        quarries.SetParent(parent, false);
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(EconomyPrefabs.QuarryPrefabPath);
        if (prefab == null)
            return;
        for (int i = 0; i < QuarryOffsets.Length; i++)
        {
            var position = screenFrame * new Vector3(QuarryDistance, 0f, QuarryOffsets[i]);
            var quarry = (GameObject)PrefabUtility.InstantiatePrefab(prefab, quarries);
            quarry.name = $"Quarry {i + 1}";
            quarry.transform.localPosition = position;
            // +Z kasabaya bakar; izometrik kamera ocakları yandan gördüğü için giriş kameraya biraz döndürülür.
            quarry.transform.localRotation = Quaternion.LookRotation(-new Vector3(position.x, 0f, position.z)) * Quaternion.Euler(0f, -35f, 0f);
        }
    }

    // Kasabanın en dış halkasının yarıçapı; orman ve dağlar bunun dışına yerleşir.
    const float TownRadius = WellDiameter * 0.5f + 7f + 8f * 2f + 4f;

    // Kuyunun çevresinde kapıları kuyuya bakan ~50 konsept ev, iç içe 3 halka.
    // Orman ve maden ocakları yönü köylülerin ve vagonların yolu olduğu için boş bırakılır.
    static void BuildVillage(Transform parent, Vector3 forestDirection, Vector3 quarryDirection)
    {
        var village = new GameObject("Village").transform;
        village.SetParent(parent, false);

        var random = new System.Random(77);
        var walls = new[] { new Color(0.86f, 0.78f, 0.62f), new Color(0.8f, 0.7f, 0.55f), new Color(0.9f, 0.84f, 0.72f) };
        var roofs = new[] { new Color(0.6f, 0.25f, 0.18f), new Color(0.5f, 0.3f, 0.2f), new Color(0.42f, 0.22f, 0.16f) };
        const float houseSpacing = 7.2f; // halka üzerinde ev başına düşen yay uzunluğu
        int built = 0;
        for (int ring = 0; ring < 3; ring++)
        {
            float ringRadius = WellDiameter * 0.5f + 7f + ring * 8f;
            int slots = Mathf.FloorToInt(2f * Mathf.PI * ringRadius / houseSpacing);
            float ringOffset = ring % 2 == 0 ? 0f : 180f / slots; // halkalar arasında evler kaydırılır
            for (int i = 0; i < slots; i++)
            {
                var direction = Quaternion.Euler(0f, ringOffset + i * 360f / slots + ((float)random.NextDouble() - 0.5f) * 6f, 0f) * Vector3.forward;
                if (Vector3.Angle(direction, forestDirection) < 40f || Vector3.Angle(direction, quarryDirection) < 24f)
                    continue;

                float distance = ringRadius + ((float)random.NextDouble() - 0.5f) * 2f;
                float width = 3f + (float)random.NextDouble() * 1.6f;
                float depth = 3f + (float)random.NextDouble() * 1.2f;
                float wallHeight = 2.2f + (float)random.NextDouble() * 0.7f;

                var house = new GameObject($"House {++built}").transform;
                house.SetParent(village, false);
                house.localPosition = direction * distance;
                house.localRotation = Quaternion.LookRotation(-direction); // +Z kuyuya bakar

                var body = Primitive(PrimitiveType.Cube, "Walls", house, new Vector3(0f, wallHeight * 0.5f, 0f),
                    new Vector3(width, wallHeight, depth), walls[random.Next(walls.Length)]);
                // Çatı: 45° döndürülmüş küp, üst yarısı üçgen bir çatı sırtı gibi görünür.
                float side = depth / Mathf.Sqrt(2f) * 1.08f;
                var roof = Primitive(PrimitiveType.Cube, "Roof", house, new Vector3(0f, wallHeight, 0f),
                    new Vector3(width + 0.4f, side, side), roofs[random.Next(roofs.Length)]);
                roof.transform.localRotation = Quaternion.Euler(45f, 0f, 0f);
                var door = Primitive(PrimitiveType.Cube, "Door", house, new Vector3(0f, 0.75f, depth * 0.5f + 0.02f),
                    new Vector3(0.8f, 1.5f, 0.05f), new Color(0.3f, 0.18f, 0.1f));

                // Köylülerin zemine yapışması ve tıklama için engel olmasınlar.
                foreach (var part in new[] { body, roof, door })
                    Object.DestroyImmediate(part.GetComponent<Collider>());
            }
        }
    }

    static Material ColorMaterial(Color color)
    {
        var probe = GameObject.CreatePrimitive(PrimitiveType.Cube);
        var material = new Material(probe.GetComponent<Renderer>().sharedMaterial) { color = color };
        Object.DestroyImmediate(probe);
        return material;
    }

    static void BuildStoneRing(Transform well, float ringRadius, float thickness, float height)
    {
        int blockCount = Mathf.CeilToInt(2f * Mathf.PI * ringRadius / 1.1f);
        var stone = new Color(0.5f, 0.5f, 0.54f);
        for (int i = 0; i < blockCount; i++)
        {
            var direction = Quaternion.Euler(0f, i * 360f / blockCount, 0f) * Vector3.forward;
            var block = Primitive(PrimitiveType.Cube, $"Wall {i + 1}", well, direction * ringRadius + Vector3.up * height * 0.5f,
                new Vector3(1.2f, height, thickness), i % 2 == 0 ? stone : stone * 0.88f);
            block.transform.localRotation = Quaternion.LookRotation(direction);
        }
    }

    // Prefab'ı verilen yüksekliğe ölçekler ve tabanını zemine oturtur.
    static GameObject TreeFromPrefab(GameObject prefab, Transform parent, Vector3 position, float height, float yRotation)
    {
        var tree = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        tree.transform.SetLocalPositionAndRotation(position, Quaternion.Euler(0f, yRotation, 0f));

        var bounds = RendererBounds(tree);
        if (bounds.size.y > 0.01f)
            tree.transform.localScale *= height / bounds.size.y;
        bounds = RendererBounds(tree);
        tree.transform.position += Vector3.up * (position.y - bounds.min.y);
        // Collider eklenmez: ResourceNode oyunda modelin tamamına göre kendisi ekler.
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
