using System.Collections.Generic;
using System.IO;
using System.Linq;
using IdleTown.Construction;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace IdleTown.Editor
{
    public static class WallfixTerrainWorkshop
    {
        const string ScenePath = "Assets/Scenes/WallfixTerrainWorkshop.unity";
        const string ArtPath = "Assets/Art/Walls/MainTerrain";

        [MenuItem("Tools/Idle Town/Wallfix/Open Terrain Workshop")]
        public static void Open()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            if (File.Exists(ScenePath)) { EditorSceneManager.OpenScene(ScenePath); return; }
            var source = EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity", OpenSceneMode.Additive);
            var transforms = source.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)).ToArray();
            var ground = transforms.First(t => t.name == "Ground" && t.GetComponent<MeshFilter>() != null);
            var mountains = transforms.First(t => t.name == "Mountains");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            Directory.CreateDirectory(ArtPath); AssetDatabase.Refresh();
            var surfaces = new List<Collider>();
            CopyLandscape(ground.gameObject, "Main Grass", new Vector3(16, 0, 16), 32, scene, surfaces);
            for (int i = 0; i < Mathf.Min(2, mountains.childCount); i++)
                CopyLandscape(mountains.GetChild(i).gameObject, "Main Hill " + i,
                    new Vector3(i == 0 ? 10 : 23, 0, i == 0 ? 18 : 22), i == 0 ? 16 : 12, scene, surfaces);
            EditorSceneManager.CloseScene(source, true);
            var world = new GameObject("Construction World").AddComponent<World>();
            var so = new SerializedObject(world);
            so.FindProperty("width").intValue = 32; so.FindProperty("height").intValue = 32;
            so.FindProperty("cellSize").floatValue = 1; so.ApplyModifiedPropertiesWithoutUndo();
            var surface = new GameObject("Main Terrain Surfaces").AddComponent<WallTerrainSurface>();
            surface.Configure(surfaces.ToArray());
            var store = new GameObject("Test Resource Store").AddComponent<Well>();
            store.Deposit(ResourceType.Wood, 5000); store.Deposit(ResourceType.Stone, 5000);
            var camera = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener)).GetComponent<Camera>();
            camera.tag = "MainCamera";
            camera.transform.position = new Vector3(16, 35, -24);
            camera.transform.LookAt(new Vector3(16, 2, 17)); camera.fieldOfView = 46;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.12f, .17f, .2f);
            var light = new GameObject("Sun").AddComponent<Light>();
            light.type = LightType.Directional; light.intensity = 1.6f;
            light.transform.rotation = Quaternion.Euler(48, -35, 0);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(.6f, .64f, .68f);
            var controller = new GameObject("Terrain Wall Construction").AddComponent<WallConstructionController>();
            controller.Configure(world, store, camera); controller.ConfigureTerrain(surface, 10);
            var grid = new GameObject("Terrain Construction Grid").AddComponent<WallDemoGrid>();
            grid.Configure(world, surface);
            AssetDatabase.SaveAssets();
            EditorSceneManager.SaveScene(scene, ScenePath);
            foreach (var old in Enumerable.Range(0, SceneManager.sceneCount).Select(SceneManager.GetSceneAt).ToArray())
                if (old != scene) EditorSceneManager.CloseScene(old, true);
            Selection.activeGameObject = controller.gameObject;
            Debug.Log("Wallfix terrain workshop: main SampleScene Ground and two original Mountain meshes; 10 floors.");
        }

        static void CopyLandscape(GameObject source, string name, Vector3 center, float width,
            Scene scene, List<Collider> surfaces)
        {
            var clone = Object.Instantiate(source);
            clone.name = name; clone.transform.SetParent(null, true); clone.SetActive(true);
            SceneManager.MoveGameObjectToScene(clone, scene);
            var filters = clone.GetComponentsInChildren<MeshFilter>(true);
            var copies = new Dictionary<Mesh, Mesh>();
            for (int i = 0; i < filters.Length; i++)
            {
                var original = filters[i].sharedMesh;
                if (original == null) continue;
                string path = $"{ArtPath}/{name.Replace(" ", "")}_{i}.asset";
                var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                if (mesh == null) { mesh = Object.Instantiate(original); AssetDatabase.CreateAsset(mesh, path); }
                copies[original] = mesh; filters[i].sharedMesh = mesh;
                var collider = filters[i].GetComponent<MeshCollider>();
                if (collider == null) collider = filters[i].gameObject.AddComponent<MeshCollider>();
                collider.sharedMesh = mesh; collider.enabled = true;
                surfaces.Add(collider);
                var renderer = filters[i].GetComponent<MeshRenderer>();
                if (renderer == null) continue;
                var materials = renderer.sharedMaterials;
                for (int j = 0; j < materials.Length; j++)
                {
                    if (materials[j] == null || (AssetDatabase.Contains(materials[j]) &&
                        !AssetDatabase.GetAssetPath(materials[j]).EndsWith(".unity"))) continue;
                    string matPath = $"{ArtPath}/{name.Replace(" ", "")}_{i}_{j}.mat";
                    var material = AssetDatabase.LoadAssetAtPath<Material>(matPath);
                    if (material == null) { material = new Material(materials[j]); AssetDatabase.CreateAsset(material, matPath); }
                    materials[j] = material;
                }
                renderer.sharedMaterials = materials;
            }
            var renderers = clone.GetComponentsInChildren<Renderer>(true);
            var bounds = renderers[0].bounds;
            foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
            clone.transform.localScale *= width / Mathf.Max(bounds.size.x, bounds.size.z);
            bounds = renderers[0].bounds;
            foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
            clone.transform.position += center - new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
        }
    }
}
