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

        [Test]
        public void ResourceScene_KeepsForestQuarriesAndConstructionReferences()
        {
            var scene = EditorSceneManager.OpenScene("Assets/Scenes/ResourceTest.unity", OpenSceneMode.Additive);
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
