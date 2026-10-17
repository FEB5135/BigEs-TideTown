using IdleTown.Construction;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace IdleTown.Editor
{
    public static class WallConstructionDemoSetup
    {
        const string DemoScenePath = "Assets/Scenes/WallConstructionDemo.unity";

        [MenuItem("Tools/Idle Town/Create Wall Construction Demo Scene")]
        public static void CreateDemoScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var worldObject = new GameObject("World");
            var world = worldObject.AddComponent<World>();

            var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.name = "Ground";
            ground.transform.SetParent(worldObject.transform, false);
            ground.transform.localPosition = new Vector3(
                world.Width * world.CellSize * 0.5f,
                -0.11f,
                world.Height * world.CellSize * 0.5f);
            ground.transform.localScale = new Vector3(
                world.Width * world.CellSize,
                0.2f,
                world.Height * world.CellSize);
            ground.isStatic = true;

            var gridObject = new GameObject("Visible Grid");
            gridObject.transform.SetParent(worldObject.transform, false);
            gridObject.AddComponent<WallDemoGrid>().Configure(world);

            var storeObject = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            storeObject.name = "Central Resource Store";
            storeObject.transform.SetParent(worldObject.transform, false);
            storeObject.transform.localPosition = new Vector3(
                world.Width * world.CellSize * 0.5f,
                0.25f,
                world.Height * world.CellSize * 0.5f);
            storeObject.transform.localScale = new Vector3(0.8f, 0.25f, 0.8f);
            var store = storeObject.AddComponent<Well>();
            store.Deposit(ResourceType.Wood, 60);
            store.Deposit(ResourceType.Stone, 30);

            var cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            var camera = cameraObject.AddComponent<Camera>();
            cameraObject.AddComponent<AudioListener>();
            camera.fieldOfView = 50f;
            cameraObject.transform.position = new Vector3(
                world.Width * 0.5f,
                16f,
                -9f);
            cameraObject.transform.LookAt(new Vector3(
                world.Width * 0.5f,
                0f,
                world.Height * 0.5f));

            var lightObject = new GameObject("Directional Light");
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.4f;
            lightObject.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            var wallLayer = new GameObject("Wall Construction Layer");
            var controller = wallLayer.AddComponent<WallConstructionController>();
            controller.Configure(world, store, camera);

            var scenePath = AssetDatabase.GenerateUniqueAssetPath(DemoScenePath);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, scenePath);
            Selection.activeGameObject = wallLayer;
            Debug.Log($"Wall construction demo created at {scenePath}. Press Play to use it.");
        }
    }
}
