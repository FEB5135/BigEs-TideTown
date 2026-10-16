using System.IO;
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
        return changed;
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

        if (Object.FindAnyObjectByType<Terrain>() == null)
        {
            var ground = Primitive(PrimitiveType.Plane, "Ground", root.transform, Vector3.zero, new Vector3(10f, 1f, 10f), new Color(0.35f, 0.55f, 0.3f));
            ground.isStatic = true;
        }

        BuildWell(root.transform);

        // Orman: köyün solu (-X).
        // Kamera 45° döndüğü için orman da 45° döndürülür, böylece ekranda köyün tam soluna düşer.
        var forest = new GameObject("Forest").transform;
        forest.SetParent(root.transform);
        forest.localRotation = Quaternion.Euler(0f, 45f, 0f);
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

    // Köyün ortasındaki kuyu: taş bloklardan halka, ortada karanlık boşluk ve içine düşeni yakalayan tetikleyici.
    static void BuildWell(Transform parent)
    {
        const int blockCount = 14;
        const float ringRadius = 1.6f;
        const float wallHeight = 0.9f;

        var well = new GameObject("Well");
        well.transform.SetParent(parent, false);
        well.AddComponent<Well>();

        var stone = new Color(0.5f, 0.5f, 0.54f);
        for (int i = 0; i < blockCount; i++)
        {
            float angle = i * 360f / blockCount;
            var direction = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
            var block = Primitive(PrimitiveType.Cube, $"Wall {i + 1}", well.transform, direction * ringRadius + Vector3.up * wallHeight * 0.5f,
                new Vector3(0.78f, wallHeight, 0.42f), i % 2 == 0 ? stone : stone * 0.88f);
            block.transform.localRotation = Quaternion.LookRotation(direction);
        }

        var hole = Primitive(PrimitiveType.Cylinder, "Hole", well.transform, new Vector3(0f, 0.02f, 0f), new Vector3(2.9f, 0.01f, 2.9f), new Color(0.05f, 0.05f, 0.07f));
        Object.DestroyImmediate(hole.GetComponent<Collider>());

        var intake = new GameObject("Intake", typeof(BoxCollider), typeof(WellIntake));
        intake.transform.SetParent(well.transform, false);
        var trigger = intake.GetComponent<BoxCollider>();
        trigger.isTrigger = true;
        trigger.center = new Vector3(0f, wallHeight * 0.5f, 0f);
        trigger.size = new Vector3(2.4f, wallHeight, 2.4f);

        var mouth = new GameObject("Drop Point").transform;
        mouth.SetParent(well.transform, false);
        mouth.localPosition = new Vector3(0f, wallHeight, 0f);
        var so = new SerializedObject(well.GetComponent<Well>());
        so.FindProperty("dropPoint").objectReferenceValue = mouth;
        so.ApplyModifiedPropertiesWithoutUndo();
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
