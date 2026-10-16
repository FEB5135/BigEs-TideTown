using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Kaynak sistemini denemek için açık sahneye geçici placeholder objeler kurar.
// Modeller gelince bu objelerin görselleri değiştirilir, script'ler aynen kalır.
public static class ResourceTestSetup
{
    const string RootName = "[Placeholder] Resource Test";

    [MenuItem("Tools/BigEs/Kaynak Test Sahnesi Kur")]
    static void Create()
    {
        if (GameObject.Find(RootName) != null)
        {
            Debug.LogWarning($"'{RootName}' zaten sahnede.");
            return;
        }

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
        for (int i = 0; i < 12; i++)
        {
            var pos = new Vector3(-16f - (float)random.NextDouble() * 14f, 1.5f, -12f + (float)random.NextDouble() * 24f);
            var tree = Primitive(PrimitiveType.Cylinder, $"Tree {i + 1}", forest, pos, new Vector3(0.5f, 1.5f, 0.5f), new Color(0.4f, 0.25f, 0.1f));
            tree.AddComponent<ResourceNode>();
            var leaves = Primitive(PrimitiveType.Sphere, "Leaves", tree.transform, Vector3.zero, Vector3.one, new Color(0.15f, 0.45f, 0.15f));
            Object.DestroyImmediate(leaves.GetComponent<Collider>());
            leaves.transform.localPosition = new Vector3(0f, 1.3f, 0f);
            leaves.transform.localScale = new Vector3(5f, 1.6f, 5f);
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
