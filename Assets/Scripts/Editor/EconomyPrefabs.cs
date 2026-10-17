using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

// Maden ocağı ve vagon prefab'larını FBX modellerden kurar (Assets/Prefabs/Economy/).
// Rastgele harita (WFC) Quarry.prefab'ı istediği yere ve yöne koyar; ocak kendi vagonunu üretir,
// vagon NavMesh üzerinden ocağına gider. Girişi prefab'ın +Z yönündedir.
public static class EconomyPrefabs
{
    const string Folder = "Assets/Prefabs/Economy";
    public const string QuarryPrefabPath = Folder + "/Quarry.prefab";
    public const string MineCartPrefabPath = Folder + "/MineCart.prefab";
    const string QuarryModelPath = "Assets/Meshes/Quarry.fbx";
    const string MineCartModelPath = "Assets/Meshes/MineCart.fbx";
    const string PaletteMaterialPath = "Assets/Materials/Flat.mat";

    [MenuItem("Tools/BigEs/Ekonomi Prefab'larını Kur")]
    public static void Build()
    {
        if (!AssetDatabase.IsValidFolder(Folder))
        {
            Directory.CreateDirectory(Folder);
            AssetDatabase.Refresh();
        }
        var cart = BuildMineCart();
        BuildQuarry(cart);
    }

    static GameObject Instantiate(string modelPath, Transform parent)
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(model, parent);
        instance.name = "Model";
        var palette = AssetDatabase.LoadAssetAtPath<Material>(PaletteMaterialPath);
        // Model renklerini UV'leriyle palette.png'den alır; ekibin palet malzemesi atanır.
        if (palette != null)
            foreach (var renderer in instance.GetComponentsInChildren<Renderer>())
                renderer.sharedMaterials = System.Array.ConvertAll(renderer.sharedMaterials, _ => palette);
        return instance;
    }

    // Vagon: yükü taşırken görünür, tekerlekler gidilen yola göre döner, arkadaki fener sallanır.
    static Gatherer BuildMineCart()
    {
        var root = new GameObject("MineCart");
        var model = Instantiate(MineCartModelPath, root.transform);
        GameObject load = null;
        var wheels = new List<Transform>();
        foreach (var renderer in model.GetComponentsInChildren<Renderer>())
        {
            switch (renderer.name)
            {
                case "Load":
                    load = renderer.gameObject;
                    break;
                case "WheelsFront":
                case "WheelsBack":
                    wheels.Add(renderer.transform);
                    break;
                case "CartLantern":
                    renderer.gameObject.AddComponent<Sway>();
                    break;
            }
        }

        var rolling = new SerializedObject(root.AddComponent<RollingWheels>());
        var list = rolling.FindProperty("wheels");
        list.arraySize = wheels.Count;
        for (int i = 0; i < wheels.Count; i++)
            list.GetArrayElementAtIndex(i).objectReferenceValue = wheels[i];
        rolling.ApplyModifiedPropertiesWithoutUndo();

        var gatherer = new SerializedObject(root.AddComponent<Gatherer>());
        gatherer.FindProperty("resourceType").enumValueIndex = (int)ResourceType.Stone;
        gatherer.FindProperty("carryCapacity").intValue = 8;
        gatherer.FindProperty("harvestDuration").floatValue = 3f;
        gatherer.FindProperty("moveSpeed").floatValue = 4f;
        gatherer.FindProperty("turnSpeed").floatValue = 180f;
        gatherer.FindProperty("carryVisual").objectReferenceValue = load;
        gatherer.ApplyModifiedPropertiesWithoutUndo();

        var prefab = PrefabUtility.SaveAsPrefabAsset(root, MineCartPrefabPath);
        Object.DestroyImmediate(root);
        return prefab.GetComponent<Gatherer>();
    }

    // Ocak: taş kaynağı. Tıklama yalnız kaya kütlesine ve taş kemere tutar; tükenince yalnız taş istifi gizlenir.
    // Vagonun yolu: ray başı -> tünelin içi. Kendi vagonunu ray başında üretir.
    static void BuildQuarry(Gatherer cart)
    {
        var root = new GameObject("Quarry");
        var model = Instantiate(QuarryModelPath, root.transform);
        GameObject pile = null;
        foreach (var renderer in model.GetComponentsInChildren<Renderer>())
        {
            switch (renderer.name)
            {
                case "Rocks":
                case "Portal":
                    renderer.gameObject.AddComponent<MeshCollider>().sharedMesh = renderer.GetComponent<MeshFilter>().sharedMesh;
                    break;
                case "Lantern":
                case "PostLantern0":
                case "PostLantern1":
                case "Bucket":
                    renderer.gameObject.AddComponent<Sway>(); // pivotları kancada
                    break;
                case "Rails":
                    // Raylar alçak; yine de NavMesh'te ray başına giden yolu kesmesin.
                    renderer.gameObject.AddComponent<NavIgnore>();
                    break;
                case "StonePile":
                    pile = renderer.gameObject;
                    break;
            }
        }
        // Model biriminde: raylar z = 12.7'de biter, kemer z = 2.7'de, tünel z = -5.3'e kadar uzanır.
        var path = new[]
        {
            PathPoint(root.transform, "Rail Entry", new Vector3(0f, 0f, 12.2f)),
            PathPoint(root.transform, "Mine Inside", new Vector3(0f, 0f, -2.5f)),
        };

        var node = new SerializedObject(root.AddComponent<ResourceNode>());
        node.FindProperty("type").enumValueIndex = (int)ResourceType.Stone;
        node.FindProperty("capacity").intValue = 200;
        node.FindProperty("regrowSeconds").floatValue = 15f;
        node.FindProperty("maxGatherers").intValue = 1;
        node.FindProperty("hitShakeAngle").floatValue = 1.5f;
        node.FindProperty("depletedHide").objectReferenceValue = pile;
        var pathProperty = node.FindProperty("approachPath");
        pathProperty.arraySize = path.Length;
        for (int i = 0; i < path.Length; i++)
            pathProperty.GetArrayElementAtIndex(i).objectReferenceValue = path[i];
        node.ApplyModifiedPropertiesWithoutUndo();

        var spawner = new SerializedObject(root.AddComponent<GathererSpawner>());
        spawner.FindProperty("prefab").objectReferenceValue = cart;
        spawner.ApplyModifiedPropertiesWithoutUndo();

        PrefabUtility.SaveAsPrefabAsset(root, QuarryPrefabPath);
        Object.DestroyImmediate(root);
    }

    static Transform PathPoint(Transform parent, string name, Vector3 localPosition)
    {
        var point = new GameObject(name).transform;
        point.SetParent(parent, false);
        point.localPosition = localPosition;
        return point;
    }
}
