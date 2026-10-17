using IdleTown.Construction;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace IdleTown.Tests
{
    public sealed class MergedAssetIntegrationTests
    {
        [Test]
        public void MainGridScene_HasWorldSolverAndAllSixModelReferences()
        {
            var scene = EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity", OpenSceneMode.Additive);
            try
            {
                World world = null;
                MapView view = null;
                WfcSolver solver = null;
                foreach (var root in scene.GetRootGameObjects())
                {
                    world ??= root.GetComponentInChildren<World>(true);
                    view ??= root.GetComponentInChildren<MapView>(true);
                    solver ??= root.GetComponentInChildren<WfcSolver>(true);
                }
                Assert.That(world, Is.Not.Null);
                Assert.That(world.Width, Is.EqualTo(32));
                Assert.That(world.Height, Is.EqualTo(32));
                Assert.That(view, Is.Not.Null);
                Assert.That(solver, Is.Not.Null);
                var viewSettings = new SerializedObject(view);
                Assert.That(viewSettings.FindProperty("world").objectReferenceValue, Is.EqualTo(world));
                var modules = viewSettings.FindProperty("modules");
                Assert.That(modules.arraySize, Is.EqualTo(6));
                for (var i = 0; i < modules.arraySize; i++)
                    Assert.That(modules.GetArrayElementAtIndex(i).FindPropertyRelative("Prefab").objectReferenceValue,
                        Is.Not.Null, "Grid model " + i);
                var solverSettings = new SerializedObject(solver);
                Assert.That(solverSettings.FindProperty("world").objectReferenceValue, Is.EqualTo(world));
                Assert.That(solverSettings.FindProperty("rules").arraySize, Is.GreaterThan(0));
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }
        [TestCase("Assets/UI/Icons/Wood.png")]
        [TestCase("Assets/UI/Icons/Stone.png")]
        [TestCase("Assets/UI/Kenney/buttonLong_brown.png")]
        [TestCase("Assets/UI/Kenney/buttonLong_brown_pressed.png")]
        [TestCase("Assets/UI/Kenney/panelInset_beige.png")]
        public void ResourceUiTextures_ImportAsSprites(string path)
        {
            Assert.That(AssetDatabase.LoadAssetAtPath<Sprite>(path), Is.Not.Null, path);
        }

        [TestCase("Assets/Prefabs/Economy/Quarry.prefab")]
        [TestCase("Assets/Prefabs/Economy/MineCart.prefab")]
        public void EconomyPrefabs_KeepTheirModelMeshes(string path)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.That(prefab, Is.Not.Null, path);
            var meshes = prefab.GetComponentsInChildren<MeshFilter>(true);
            Assert.That(meshes, Is.Not.Empty, path);
            foreach (var mesh in meshes)
                Assert.That(mesh.sharedMesh, Is.Not.Null, path + "/" + mesh.name);
        }

        [TestCase("Assets/Scenes/ResourceTest.unity")]
        [TestCase("Assets/Scenes/SampleScene.unity")]
        public void ResourceScene_KeepsForestQuarriesAndConstructionReferences(string scenePath)
        {
            var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
            try
            {
                Transform forest = null;
                Transform quarries = null;
                WallConstructionController controller = null;
                foreach (var root in scene.GetRootGameObjects())
                {
                    foreach (var child in root.GetComponentsInChildren<Transform>(true))
                    {
                        Assert.That(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(child.gameObject),
                            Is.Zero, child.name);
                        Assert.That(PrefabUtility.GetPrefabAssetType(child.gameObject),
                            Is.Not.EqualTo(PrefabAssetType.MissingAsset), child.name);
                        foreach (var component in child.GetComponents<Component>())
                        {
                            if (component != null && component.GetType().FullName == "UnityEngine.UI.Image")
                            {
                                var image = new SerializedObject(component);
                                Assert.That(image.FindProperty("m_Sprite").objectReferenceValue,
                                    Is.Not.Null, child.name + " sprite reference");
                            }
                        }
                        if (child.name == "Forest") forest = child;
                        if (child.name == "Quarries") quarries = child;
                    }
                    controller ??= root.GetComponentInChildren<WallConstructionController>(true);
                }
                Assert.That(forest, Is.Not.Null);
                Assert.That(forest.childCount, Is.EqualTo(34));
                Assert.That(forest.GetComponentsInChildren<Renderer>(true), Is.Not.Empty);
                Assert.That(quarries, Is.Not.Null);
                Assert.That(quarries.childCount, Is.EqualTo(3));
                Assert.That(quarries.GetComponentsInChildren<Renderer>(true), Is.Not.Empty);
                Assert.That(controller, Is.Not.Null);
                var serialized = new SerializedObject(controller);
                Assert.That(serialized.FindProperty("world").objectReferenceValue, Is.Not.Null);
                Assert.That(serialized.FindProperty("resourceStoreSource").objectReferenceValue, Is.Not.Null);
                Assert.That(serialized.FindProperty("inputCamera").objectReferenceValue, Is.Not.Null);
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }
    }
}
