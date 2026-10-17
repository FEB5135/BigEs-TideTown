using System.Collections;
using System.Linq;
using IdleTown.Construction;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;

namespace IdleTown.Tests
{
    public sealed class SampleSceneIntegrationTests
    {
        [Test]
        public void SampleScene_UsesOneSharedWorldWellAndCamera()
        {
            var scene = EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity", OpenSceneMode.Additive);
            try
            {
                var roots = scene.GetRootGameObjects();
                var worlds = roots.SelectMany(r => r.GetComponentsInChildren<World>(true)).ToArray();
                var wells = roots.SelectMany(r => r.GetComponentsInChildren<Well>(true)).ToArray();
                var cameras = roots.SelectMany(r => r.GetComponentsInChildren<Camera>(true)).ToArray();
                Assert.That(worlds.Length, Is.EqualTo(1));
                Assert.That(wells.Length, Is.EqualTo(1));
                Assert.That(cameras.Length, Is.EqualTo(1));
                var world = worlds[0];
                Assert.That(world.transform.position, Is.EqualTo(new Vector3(-96f, 0f, -96f)));
                Assert.That(world.CellSize, Is.EqualTo(6f));
                var walls = roots.SelectMany(r => r.GetComponentsInChildren<WallConstructionController>(true)).Single();
                var settings = new SerializedObject(walls);
                Assert.That(settings.FindProperty("world").objectReferenceValue, Is.EqualTo(world));
                Assert.That(settings.FindProperty("resourceStoreSource").objectReferenceValue, Is.EqualTo(wells[0]));
                Assert.That(settings.FindProperty("inputCamera").objectReferenceValue, Is.EqualTo(cameras[0]));
                Assert.That(walls.ConstructionModeActive, Is.False);
                var view = new SerializedObject(world.GetComponent<MapView>());
                Assert.That(view.FindProperty("createColliders").boolValue, Is.True);
                Assert.That(roots.SelectMany(r => r.GetComponentsInChildren<Transform>(true))
                    .Count(t => t.name.StartsWith("House ")), Is.EqualTo(40));
                Assert.That(EditorBuildSettings.scenes.First(s => s.enabled).path,
                    Is.EqualTo("Assets/Scenes/SampleScene.unity"));
            }
            finally { EditorSceneManager.CloseScene(scene, true); }
        }

        [UnityTest]
        public IEnumerator SampleScene_GathersBothResourcesAndBuildsOnWfcGrid()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity", OpenSceneMode.Single);
            yield return new EnterPlayMode();
            yield return null;
            var world = Object.FindAnyObjectByType<World>();
            var well = Well.Instance;
            Assert.That(well, Is.Not.Null);
            Assert.That(NavigationBaker.Ready, Is.True, "WFC terrain must have walkable navigation.");
            Assert.That(Object.FindObjectsByType<Gatherer>().Length, Is.EqualTo(8));
            var solver = new SerializedObject(world.GetComponent<WfcSolver>());
            var pins = solver.FindProperty("initialCells");
            for (int i = 0; i < pins.arraySize; i++)
            {
                var pin = pins.GetArrayElementAtIndex(i);
                world.Get(pin.FindPropertyRelative("X").intValue, pin.FindPropertyRelative("Y").intValue,
                    out var id, out var rotation);
                Assert.That(id, Is.EqualTo(pin.FindPropertyRelative("Id").intValue));
            }
            var terrain = world.GetComponent<MapView>();
            var ground = terrain.GetComponentsInChildren<MeshFilter>()
                .First(f => f.sharedMesh != null && f.GetComponent<MeshCollider>() != null && f.GetComponent<Renderer>().bounds.size.y < 0.1f);
            Assert.That(ground.GetComponent<Renderer>().bounds.size.x, Is.EqualTo(world.CellSize).Within(0.01f));
            Assert.That(ground.GetComponent<MeshCollider>(), Is.Not.Null);

            var deadline = Time.realtimeSinceStartup + 75f;
            while ((well.Wood < 12 || well.Stone < 13) && Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.That(well.Wood, Is.GreaterThanOrEqualTo(12), "Woodcutters must deposit wood in the well.");
            Assert.That(well.Stone, Is.GreaterThanOrEqualTo(13), "Mine carts must deposit stone in the well.");
            int wood = well.Wood, stone = well.Stone;
            var controller = Object.FindAnyObjectByType<WallConstructionController>();
            Assert.That(controller.TryBuild(new Vector2Int(13, 9), new Vector2Int(17, 9), out var reason),
                Is.True, reason);
            Assert.That(controller.Edges.Count, Is.EqualTo(4));
            Assert.That(well.Wood, Is.EqualTo(wood - 4));
            Assert.That(well.Stone, Is.EqualTo(stone - 4));
            var a = new Vector2Int(13, 9);
            var b = new Vector2Int(14, 9);
            Assert.That(controller.TryBuild(a, b, 0, WallTier.Stone, out reason), Is.True, reason);
            Assert.That(well.Wood, Is.EqualTo(wood - 6));
            Assert.That(well.Stone, Is.EqualTo(stone - 7));
            Assert.That(controller.TryBuild(a, b, 1, WallTier.Stone, out reason), Is.True, reason);
            Assert.That(well.Wood, Is.EqualTo(wood - 9));
            Assert.That(well.Stone, Is.EqualTo(stone - 11));
            Assert.That(controller.TryBuild(a, b, 0, WallTier.Reinforced, out reason), Is.True, reason);
            Assert.That(well.Wood, Is.EqualTo(wood - 12));
            Assert.That(well.Stone, Is.EqualTo(stone - 13));
            Assert.That(controller.Walls.Count, Is.EqualTo(5));
            Assert.That(controller.Walls.Any(w => w.Floor == 1 && w.Tier == WallTier.Stone), Is.True);
            Assert.That(controller.Walls.Any(w => w.Floor == 0 && w.Tier == WallTier.Reinforced), Is.True);
            var segments = controller.GetComponentsInChildren<WallSegment>();
            Assert.That(segments.Length, Is.EqualTo(5));
            Assert.That(segments.Single(s => s.Floor == 1).transform.position.y,
                Is.GreaterThan(segments.First(s => s.Floor == 0).transform.position.y));
            Debug.Log($"SampleScene smoke test passed: gathered {wood} wood/{stone} stone; built 4 walls, upgraded Stone/Reinforced, added supported Stone floor, verified cumulative costs.");
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
