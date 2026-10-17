using System.IO;
using IdleTown.Construction;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace IdleTown.Editor
{
    public static class WallfixWorkshop
    {
        const string ArtPath = "Assets/Art/Walls";

        [InitializeOnLoadMethod]
        static void ScheduleAssets() => EditorApplication.delayCall += () =>
        {
            if (!EditorApplication.isPlayingOrWillChangePlaymode && !AssetDatabase.IsValidFolder(ArtPath))
                ExportAssets();
        };

        [MenuItem("Tools/Idle Town/Wallfix/Export Wall Assets")]
        public static void ExportAssets()
        {
            Directory.CreateDirectory(ArtPath);
            AssetDatabase.Refresh();
            using var art = new WallArt();
            foreach (WallTier tier in new[] { WallTier.Timber, WallTier.Stone, WallTier.Reinforced })
            {
                string prefix = $"{ArtPath}/{tier}";
                var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(prefix + ".asset");
                if (mesh == null)
                {
                    mesh = WallArt.CreatePanel(tier, .117f);
                    AssetDatabase.CreateAsset(mesh, prefix + ".asset");
                }
                var materials = new Material[3];
                for (int i = 0; i < 3; i++)
                {
                    string path = prefix + $"_{i}.mat";
                    materials[i] = AssetDatabase.LoadAssetAtPath<Material>(path);
                    if (materials[i] == null)
                    {
                        materials[i] = new Material(art.Palette(tier)[i]);
                        AssetDatabase.CreateAsset(materials[i], path);
                    }
                }
                if (AssetDatabase.LoadAssetAtPath<GameObject>(prefix + ".prefab") == null)
                {
                    var root = new GameObject(tier + " Wall", typeof(MeshFilter), typeof(MeshRenderer), typeof(BoxCollider));
                    root.GetComponent<MeshFilter>().sharedMesh = mesh;
                    root.GetComponent<MeshRenderer>().sharedMaterials = materials;
                    root.transform.localScale = new Vector3(.18f, WallConstructionController.StoreyHeight, 1f);
                    PrefabUtility.SaveAsPrefabAsset(root, prefix + ".prefab");
                    Object.DestroyImmediate(root);
                }
            }
            if (AssetDatabase.LoadAssetAtPath<Mesh>(ArtPath + "/Joint.asset") == null)
                AssetDatabase.CreateAsset(WallArt.CreatePost(), ArtPath + "/Joint.asset");
            AssetDatabase.SaveAssets();
            Debug.Log("Wallfix: original Timber / Stone / Reinforced meshes, materials and prefabs exported to " + ArtPath);
        }

        [MenuItem("Tools/Idle Town/Wallfix/Open Workshop")]
        public static void OpenWorkshop()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            const string path = "Assets/Scenes/WallfixWorkshop.unity";
            if (File.Exists(path)) { EditorSceneManager.OpenScene(path); return; }
            ExportAssets();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var world = new GameObject("World").AddComponent<World>();
            var ground = Cube("Ground", new Vector3(8, -.16f, 8), new Vector3(16, .3f, 16), new Color(.22f, .29f, .25f));
            ground.transform.SetParent(world.transform, true);
            var grid = new GameObject("Construction Grid").AddComponent<WallDemoGrid>();
            grid.transform.SetParent(world.transform, false);
            grid.Configure(world);
            var store = Cube("Central Store", new Vector3(13.5f, .4f, 8), new Vector3(.9f, .8f, .9f), new Color(.39f, .31f, .2f));
            var well = store.AddComponent<Well>();
            well.Deposit(ResourceType.Wood, 300);
            well.Deposit(ResourceType.Stone, 200);
            Cube("Protected Workshop", new Vector3(8, .85f, 8), new Vector3(2.4f, 1.7f, 2.4f), new Color(.67f, .61f, .45f));
            Cube("Workshop Roof", new Vector3(8, 1.85f, 8), new Vector3(2.7f, .35f, 2.7f), new Color(.28f, .38f, .42f));
            Cube("Storage Crates", new Vector3(4, .45f, 8), new Vector3(1.3f, .9f, 1.3f), new Color(.47f, .29f, .14f));
            var camera = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener)).GetComponent<Camera>();
            camera.tag = "MainCamera";
            camera.transform.position = new Vector3(8, 17, -10);
            camera.transform.LookAt(new Vector3(8, 0, 7));
            camera.fieldOfView = 46;
            camera.backgroundColor = new Color(.12f, .17f, .2f);
            camera.clearFlags = CameraClearFlags.SolidColor;
            var light = new GameObject("Warm Sun").AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.65f;
            light.color = new Color(1, .91f, .78f);
            light.transform.rotation = Quaternion.Euler(48, -35, 0);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(.53f, .59f, .64f);
            var controller = new GameObject("Wall Construction").AddComponent<WallConstructionController>();
            controller.Configure(world, well, camera);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, path);
            Selection.activeGameObject = controller.gameObject;
        }

        static GameObject Cube(string name, Vector3 position, Vector3 size, Color color)
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = name; cube.transform.position = position; cube.transform.localScale = size;
            string materialPath = ArtPath + "/" + name.Replace(" ", "") + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material == null)
            {
                material = WallArt.Material(color, false);
                AssetDatabase.CreateAsset(material, materialPath);
            }
            cube.GetComponent<MeshRenderer>().sharedMaterial = material;
            return cube;
        }
    }
}
