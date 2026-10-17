using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Uses the user's prefab as an unchanged nested source. Generated assets live separately.
public static class VillagerPrefabSetup
{
    public const string SourcePath = "Assets/Prefabs/villager.prefab";
    public const string WorkerPath = "Assets/Prefabs/Economy/Woodcutter.prefab";
    public const string VisualPath = "Assets/Prefabs/Economy/VillagerVisual.prefab";
    const string MeshPath = "Assets/Meshes/VillagerAnimated.asset";

    [MenuItem("Tools/BigEs/Koylu Animasyon Prefabini Kur")]
    public static void Build()
    {
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(SourcePath);
        if (source == null)
            throw new System.InvalidOperationException("Assets/Prefabs/villager.prefab bulunamadı.");
        var root = new GameObject("Woodcutter");
        try
        {
            var gatherer = root.AddComponent<Gatherer>();
            var visual = new GameObject("Visual").transform;
            visual.SetParent(root.transform, false);
            var model = (GameObject)PrefabUtility.InstantiatePrefab(source, visual);
            model.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
            var filter = model.GetComponentInChildren<MeshFilter>();
            if (filter == null || !filter.sharedMesh.isReadable)
                throw new System.InvalidOperationException("Villager mesh Read/Write açık olmalı.");
            var original = filter.GetComponent<MeshRenderer>();
            model.transform.localScale *= 2f / original.bounds.size.y;
            var mesh = Object.Instantiate(filter.sharedMesh);
            mesh.name = "Villager Animated Mesh";
            var bones = new[] {
                Bone(filter.transform, "Body", Vector3.zero),
                Bone(filter.transform, "Left Shoulder", new Vector3(0f, 3.95f, 0.73f)),
                Bone(filter.transform, "Right Shoulder", new Vector3(0f, 3.95f, -0.73f)),
                Bone(filter.transform, "Left Hip", new Vector3(0f, 2.05f, 0.44f)),
                Bone(filter.transform, "Right Hip", new Vector3(0f, 2.05f, -0.44f))
            };
            var weights = new BoneWeight[mesh.vertexCount];
            var vertices = mesh.vertices;
            for (int i = 0; i < weights.Length; i++)
            {
                var v = vertices[i];
                int bone = 0;
                float amount = 0f;
                if (v.y < 2.25f)
                {
                    bone = v.z >= 0f ? 3 : 4;
                    amount = 1f - Mathf.InverseLerp(1.8f, 2.25f, v.y);
                }
                if (v.y >= 2.15f && v.y < 4.25f && Mathf.Abs(v.z) > 0.62f)
                {
                    bone = v.z >= 0f ? 1 : 2;
                    amount = Mathf.InverseLerp(0.62f, 0.94f, Mathf.Abs(v.z));
                }
                weights[i] = new BoneWeight { boneIndex0 = 0, weight0 = 1f - amount,
                    boneIndex1 = bone, weight1 = amount };
            }
            mesh.boneWeights = weights;
            mesh.bindposes = bones.Select(b => b.worldToLocalMatrix * filter.transform.localToWorldMatrix).ToArray();
            var existingMesh = AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath);
            if (existingMesh != null)
            {
                EditorUtility.CopySerialized(mesh, existingMesh);
                Object.DestroyImmediate(mesh);
                mesh = existingMesh;
                EditorUtility.SetDirty(mesh);
            }
            else
                AssetDatabase.CreateAsset(mesh, MeshPath);
            original.enabled = false;
            var skin = filter.gameObject.AddComponent<SkinnedMeshRenderer>();
            skin.sharedMesh = mesh;
            skin.bones = bones;
            skin.rootBone = bones[0];
            skin.sharedMaterials = Enumerable.Repeat(AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Flat.mat"), mesh.subMeshCount).ToArray();
            skin.localBounds = new Bounds(new Vector3(0f, 3.2f, 0f), new Vector3(5f, 8f, 5f));
            var carried = GameObject.CreatePrimitive(PrimitiveType.Cube);
            carried.name = "Carried Wood";
            Object.DestroyImmediate(carried.GetComponent<Collider>());
            carried.transform.SetParent(visual, false);
            carried.transform.localPosition = new Vector3(0f, 1.25f, -0.35f);
            carried.transform.localScale = new Vector3(0.8f, 0.3f, 0.3f);
            const string woodPath = "Assets/Materials/CarriedWood.mat";
            var woodMaterial = AssetDatabase.LoadAssetAtPath<Material>(woodPath);
            if (woodMaterial == null)
            {
                woodMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                woodMaterial.color = new Color(0.4f, 0.25f, 0.1f);
                woodMaterial.SetFloat("_Smoothness", 0f);
                AssetDatabase.CreateAsset(woodMaterial, woodPath);
            }
            carried.GetComponent<Renderer>().sharedMaterial = woodMaterial;
            PrefabUtility.SaveAsPrefabAsset(visual.gameObject, VisualPath);
            ConfigureAnimation(root, visual);
            var workerSettings = new SerializedObject(gatherer);
            workerSettings.FindProperty("carryVisual").objectReferenceValue = carried;
            workerSettings.FindProperty("moveSpeed").floatValue = 5f;
            workerSettings.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(root, WorkerPath);
            AssetDatabase.SaveAssets();
        }
        finally { Object.DestroyImmediate(root); } // Temporary authoring object only.
    }

    public static GameObject CreateWorker(Transform parent, Vector3 position, string name)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(WorkerPath);
        if (prefab == null) { Build(); prefab = AssetDatabase.LoadAssetAtPath<GameObject>(WorkerPath); }
        var worker = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        worker.name = name;
        worker.transform.position = position;
        return worker;
    }

    // Upgrade in place: preserve the existing Gatherer, its tuning, position and scene references.
    public static void UpgradeScene(Scene scene)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(VisualPath);
        foreach (var worker in scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Gatherer>(true)).ToArray())
        {
            if (worker.Type != ResourceType.Wood || worker.GetComponent<VillagerAnimation>() != null)
                continue;
            var settings = new SerializedObject(worker);
            var oldCarry = settings.FindProperty("carryVisual").objectReferenceValue as GameObject;
            if (oldCarry != null) oldCarry.SetActive(false);
            foreach (var renderer in worker.GetComponentsInChildren<Renderer>(true))
                renderer.enabled = false;
            var visual = (GameObject)PrefabUtility.InstantiatePrefab(prefab, worker.transform);
            visual.name = "Villager Visual";
            var scale = worker.transform.lossyScale;
            visual.transform.localScale = new Vector3(1f / scale.x, 1f / scale.y, 1f / scale.z);
            visual.transform.localPosition = Vector3.down * (worker.transform.position.y / scale.y);
            ConfigureAnimation(worker.gameObject, visual.transform);
            settings.FindProperty("carryVisual").objectReferenceValue = visual.transform.Find("Carried Wood").gameObject;
            settings.ApplyModifiedPropertiesWithoutUndo();
        }
    }

    static void ConfigureAnimation(GameObject worker, Transform visual)
    {
        var animation = worker.AddComponent<VillagerAnimation>();
        var settings = new SerializedObject(animation);
        settings.FindProperty("visualRoot").objectReferenceValue = visual;
        var transforms = visual.GetComponentsInChildren<Transform>(true);
        foreach (var pair in new[] { ("leftArm", "Left Shoulder"), ("rightArm", "Right Shoulder"),
                     ("leftLeg", "Left Hip"), ("rightLeg", "Right Hip") })
            settings.FindProperty(pair.Item1).objectReferenceValue = transforms.Single(t => t.name == pair.Item2);
        settings.ApplyModifiedPropertiesWithoutUndo();
    }

    static Transform Bone(Transform parent, string name, Vector3 position)
    {
        var bone = new GameObject(name).transform;
        bone.SetParent(parent, false);
        bone.localPosition = position;
        return bone;
    }
}
