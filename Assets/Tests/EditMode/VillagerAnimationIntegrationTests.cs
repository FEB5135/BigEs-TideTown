using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;

namespace IdleTown.Tests
{
    public sealed class VillagerAnimationIntegrationTests
    {
        [Test]
        public void WorkerPrefab_UsesSuppliedVillagerWithBoundShoulderAndHipBones()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Economy/Woodcutter.prefab");
            Assert.That(prefab, Is.Not.Null);
            Assert.That(prefab.GetComponent<Gatherer>(), Is.Not.Null);
            Assert.That(prefab.GetComponent<VillagerAnimation>(), Is.Not.Null);
            var model = prefab.transform.Find("Visual/villager");
            Assert.That(model, Is.Not.Null);
            Assert.That(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(model.gameObject),
                Is.EqualTo("Assets/Prefabs/villager.prefab"));
            var skin = prefab.GetComponentInChildren<SkinnedMeshRenderer>(true);
            Assert.That(skin, Is.Not.Null);
            Assert.That(skin.bones.Length, Is.EqualTo(5));
            Assert.That(skin.bones.All(b => b != null), Is.True);
            Assert.That(skin.sharedMesh.bindposes.Length, Is.EqualTo(5));
            Assert.That(skin.sharedMesh.boneWeights.Any(w => w.boneIndex1 == 1 && w.weight1 > 0.9f), Is.True);
            Assert.That(skin.sharedMesh.boneWeights.Any(w => w.boneIndex1 == 2 && w.weight1 > 0.9f), Is.True);
            Assert.That(model.GetComponentsInChildren<MeshRenderer>(true).All(r => !r.enabled), Is.True,
                "The original static body must not overlap the skinned body.");
        }

        [UnityTest]
        public IEnumerator SampleScene_WalksChopsCarriesAndDepositsUsingAnimatedVillagers()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity", OpenSceneMode.Single);
            yield return new EnterPlayMode();
            yield return null;
            Assert.That(Object.FindObjectsByType<World>().Length, Is.EqualTo(1), "One active grid must own the terrain.");
            Assert.That(NavigationBaker.Ready, Is.True);
            Assert.That(Object.FindObjectsByType<Camera>().Length, Is.EqualTo(1));
            var menu = Object.FindAnyObjectByType<MainMenu>();
            Assert.That(menu, Is.Not.Null, "The main menu must survive the scene migration.");
            menu.Play();
            var intro = Object.FindAnyObjectByType<CameraIntro>();
            var introDeadline = Time.realtimeSinceStartup + 8f;
            while (intro.InMenu && Time.realtimeSinceStartup < introDeadline)
                yield return null;
            Assert.That(intro.InMenu, Is.False);
            var welcome = Object.FindAnyObjectByType<WelcomeParty>();
            Assert.That(welcome.IsOpen, Is.True);
            welcome.Complete();
            var villagers = Object.FindObjectsByType<VillagerAnimation>();
            Assert.That(villagers.Length, Is.EqualTo(5));
            var well = Well.Instance;
            Assert.That(well, Is.Not.Null);
            var deadline = Time.realtimeSinceStartup + 90f;
            bool walked = false, chopped = false, carried = false, deformed = false;
            VillagerAnimation observed = null;
            Vector3[] previousSkin = null;
            var baked = new Mesh();
            try
            {
                while (Time.realtimeSinceStartup < deadline)
                {
                    foreach (var animation in villagers)
                    {
                        var worker = animation.GetComponent<Gatherer>();
                        var settings = new SerializedObject(animation);
                        var body = (Transform)settings.FindProperty("visualRoot").objectReferenceValue;
                        var arm = (Transform)settings.FindProperty("leftArm").objectReferenceValue;
                        var carryObject = (GameObject)new SerializedObject(worker).FindProperty("carryVisual").objectReferenceValue;
                        if (animation.IsWalking)
                        {
                            walked |= Quaternion.Angle(Quaternion.identity, body.localRotation) > 2f;
                            Assert.That(Quaternion.Angle(Quaternion.identity, body.localRotation), Is.LessThanOrEqualTo(16f));
                            carried |= carryObject.activeInHierarchy;
                        }
                        if (!animation.IsChopping)
                            continue;
                        Assert.That(worker.Activity, Is.EqualTo(GathererActivity.Harvesting));
                        var direction = worker.CurrentTarget.transform.position - worker.transform.position;
                        direction.y = 0f;
                        Assert.That(Vector3.Angle(worker.transform.forward, direction), Is.LessThan(8f));
                        chopped |= Quaternion.Angle(Quaternion.identity, arm.localRotation) > 20f;
                        if (observed != null && observed != animation)
                            continue;
                        observed = animation;
                        var skin = animation.GetComponentInChildren<SkinnedMeshRenderer>();
                        skin.BakeMesh(baked);
                        var vertices = baked.vertices;
                        if (previousSkin != null)
                        {
                            var weights = skin.sharedMesh.boneWeights;
                            for (int i = 0; i < vertices.Length; i++)
                                if ((weights[i].boneIndex1 == 1 || weights[i].boneIndex1 == 2) &&
                                    weights[i].weight1 > 0.9f && Vector3.Distance(vertices[i], previousSkin[i]) > 0.02f)
                                    deformed = true;
                        }
                        previousSkin = vertices;
                    }
                    if (walked && chopped && carried && deformed && well.Wood > 0 && well.Stone > 0)
                        break;
                    yield return null;
                }
                Assert.That(walked, Is.True, "Walking must rock the visual without rotating the navigation root.");
                Assert.That(chopped, Is.True, "A wood harvester must raise its shoulders at the tree.");
                Assert.That(deformed, Is.True, "Shoulder rotation must visibly move the supplied mesh's arm vertices.");
                Assert.That(carried, Is.True, "The worker must return carrying harvested wood.");
                Assert.That(well.Wood, Is.GreaterThan(0), "Animation must preserve wood delivery.");
                Assert.That(well.Stone, Is.GreaterThan(0), "Mine carts must keep working.");
                var pose = new SerializedObject(villagers[0]);
                var visual = (Transform)pose.FindProperty("visualRoot").objectReferenceValue;
                var leftArm = (Transform)pose.FindProperty("leftArm").objectReferenceValue;
                var rootPosition = villagers[0].transform.position;
                villagers[0].enabled = false;
                Assert.That(Quaternion.Angle(visual.localRotation, Quaternion.identity), Is.LessThan(0.01f));
                Assert.That(Quaternion.Angle(leftArm.localRotation, Quaternion.identity), Is.LessThan(0.01f));
                Assert.That(villagers[0].transform.position, Is.EqualTo(rootPosition));
                villagers[0].enabled = true;
                Debug.Log($"Villager animation integration passed: walking, chopping, arm mesh deformation, carrying, {well.Wood} wood/{well.Stone} stone delivered.");
            }
            finally { Object.DestroyImmediate(baked); }
            yield return new ExitPlayMode();
        }

        [UnityTearDown]
        public IEnumerator LeavePlayMode()
        {
            if (EditorApplication.isPlaying)
                yield return new ExitPlayMode();
        }
    }
}
