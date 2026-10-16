using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using IdleTown.Construction;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace IdleTown.Tests
{
    public sealed class WallConstructionModelTests
    {
        [Test]
        public void SnappedPath_UsesOnlyEightDirectionNeighbours()
        {
            var path = WallConstructionModel.CreateSnappedPath(new Vector2Int(0, 0), new Vector2Int(5, 2));

            Assert.That(path, Has.Count.EqualTo(5));
            foreach (var edge in path)
                Assert.That(edge.IsNeighbour, Is.True);
            Assert.That(path.Exists(edge => edge.IsDiagonal), Is.True);
            Assert.That(path.Exists(edge => !edge.IsDiagonal), Is.True);
            Assert.That(path[0].A, Is.EqualTo(new Vector2Int(0, 0)));
            Assert.That(path[path.Count - 1].B, Is.EqualTo(new Vector2Int(5, 2)));
        }

        [Test]
        public void Validate_RejectsReversedDuplicateEdge()
        {
            var model = new WallConstructionModel();
            var original = new List<WallEdge>
            {
                new(new Vector2Int(1, 1), new Vector2Int(2, 1))
            };
            model.Add(original);

            var reversed = new List<WallEdge>
            {
                new(new Vector2Int(2, 1), new Vector2Int(1, 1))
            };

            Assert.That(model.Validate(reversed, 4, 4, out _), Is.False);
        }

        [Test]
        public void Validate_RejectsCrossingDiagonal()
        {
            var model = new WallConstructionModel();
            model.Add(new List<WallEdge>
            {
                new(new Vector2Int(0, 0), new Vector2Int(1, 1))
            });
            var crossing = new List<WallEdge>
            {
                new(new Vector2Int(0, 1), new Vector2Int(1, 0))
            };

            Assert.That(model.Validate(crossing, 4, 4, out _), Is.False);
        }

        [Test]
        public void Validate_AllowsNodesOnOuterMapBoundary()
        {
            var model = new WallConstructionModel();
            var edge = new List<WallEdge>
            {
                new(new Vector2Int(3, 4), new Vector2Int(4, 4))
            };

            Assert.That(model.Validate(edge, 4, 4, out _), Is.True);
        }
    }

    public sealed class WallConstructionControllerTests
    {
        [Test]
        public void TryBuild_DefaultGeneratedMultiEdgeWallsRemainVisibleAfterAnotherBuild()
        {
            var root = new GameObject("Default Wall Success Test");
            try
            {
                var world = root.AddComponent<World>();
                var resources = root.AddComponent<SimpleWallResourceStore>();
                resources.Add(10, 10);
                var initialWood = resources.Wood;
                var initialStone = resources.Stone;
                var controller = root.AddComponent<WallConstructionController>();
                controller.Configure(world, resources);

                var firstBuilt = controller.TryBuild(
                    Vector2Int.zero,
                    new Vector2Int(3, 0),
                    out var firstReason);

                Assert.That(firstBuilt, Is.True, firstReason);
                Assert.That(controller.Edges, Has.Count.EqualTo(3));
                var firstBuild = GetBuiltWalls(root.transform);
                Assert.That(firstBuild, Has.Count.EqualTo(3));
                AssertWallsAreActiveAndPhysical(firstBuild);

                var secondBuilt = controller.TryBuild(
                    new Vector2Int(0, 1),
                    new Vector2Int(0, 3),
                    out var secondReason);

                Assert.That(secondBuilt, Is.True, secondReason);
                Assert.That(controller.Edges, Has.Count.EqualTo(5));
                Assert.That(resources.Wood, Is.EqualTo(initialWood - 5));
                Assert.That(resources.Stone, Is.EqualTo(initialStone - 5));
                AssertWallsAreActiveAndPhysical(firstBuild);
                Assert.That(GetBuiltWalls(root.transform), Has.Count.EqualTo(5));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void HandleNodeClick_SecondClickBuildsAndNextClickStartsNewSelection()
        {
            var root = new GameObject("Click State Test");
            try
            {
                var world = root.AddComponent<World>();
                var resources = new FakeResourceStore(10, 10);
                var controller = root.AddComponent<WallConstructionController>();
                controller.Configure(world, null);
                controller.SetResourceStore(resources);

                controller.HandleNodeClick(Vector2Int.zero);

                Assert.That(controller.Edges, Is.Empty);
                Assert.That(resources.SpendCalls, Is.Zero);

                controller.HandleNodeClick(Vector2Int.right);

                Assert.That(controller.Edges, Has.Count.EqualTo(1));
                Assert.That(resources.SpendCalls, Is.EqualTo(1));

                controller.HandleNodeClick(new Vector2Int(0, 1));

                Assert.That(controller.Edges, Has.Count.EqualTo(1));
                Assert.That(resources.SpendCalls, Is.EqualTo(1));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void ConfirmationHandlers_BuildHoveredAndCachedLastValidEndpoints()
        {
            var root = new GameObject("Confirmation Test");
            try
            {
                var world = root.AddComponent<World>();
                var resources = new FakeResourceStore(10, 10);
                var controller = root.AddComponent<WallConstructionController>();
                controller.Configure(world, null);
                controller.SetResourceStore(resources);

                controller.HandleNodeClick(Vector2Int.zero);
                controller.RefreshPreview(Vector2Int.right);

                Assert.That(controller.ConfirmHoveredPreview(), Is.True);
                Assert.That(controller.Edges, Has.Count.EqualTo(1));

                controller.HandleNodeClick(new Vector2Int(0, 1));
                controller.RefreshPreview(new Vector2Int(0, 2));
                controller.RefreshPreview(new Vector2Int(0, 1));

                Assert.That(controller.ConfirmHoveredPreview(), Is.False);
                Assert.That(controller.ConfirmLastValidPreview(), Is.True);
                Assert.That(controller.Edges, Has.Count.EqualTo(2));
                Assert.That(resources.SpendCalls, Is.EqualTo(2));
                Assert.That(resources.Wood, Is.EqualTo(8));
                Assert.That(resources.Stone, Is.EqualTo(8));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void ConstructionPanelHitTest_ConvertsInputCoordinatesAndUsesExclusiveMaxEdges()
        {
            Assert.That(
                WallConstructionController.IsPointerOverConstructionPanel(
                    new Vector2(16f, Screen.height - 16f)),
                Is.True);
            Assert.That(
                WallConstructionController.IsPointerOverConstructionPanel(
                    new Vector2(456f, Screen.height - 16f)),
                Is.False);
            Assert.That(
                WallConstructionController.IsPointerOverConstructionPanel(
                    new Vector2(16f, Screen.height - 241f)),
                Is.False);
        }

        [Test]
        public void TryBuild_WhenActualStoreChangedSubscriberThrows_RemainsCommitted()
        {
            var root = new GameObject("Resource Observer Test");
            try
            {
                var world = root.AddComponent<World>();
                var resources = root.AddComponent<SimpleWallResourceStore>();
                resources.Add(10, 10);
                var initialWood = resources.Wood;
                var initialStone = resources.Stone;
                var laterSubscriberCalled = false;
                resources.Changed += () => throw new InvalidOperationException("resource observer failure");
                resources.Changed += () => laterSubscriberCalled = true;
                var controller = root.AddComponent<WallConstructionController>();
                controller.Configure(world, resources);
                LogAssert.Expect(LogType.Exception, new Regex("InvalidOperationException: resource observer failure"));

                var built = controller.TryBuild(Vector2Int.zero, Vector2Int.right, out var reason);

                Assert.That(built, Is.True, reason);
                Assert.That(reason, Is.Empty);
                Assert.That(resources.Wood, Is.EqualTo(initialWood - 1));
                Assert.That(resources.Stone, Is.EqualTo(initialStone - 1));
                Assert.That(laterSubscriberCalled, Is.True);
                Assert.That(controller.Edges, Has.Count.EqualTo(1));
                AssertWallsAreActiveAndPhysical(GetBuiltWalls(root.transform));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void TryBuild_InactiveCustomPrefab_IsActivatedOnCommit()
        {
            var root = new GameObject("Successful Build Test");
            var prefab = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                var world = root.AddComponent<World>();
                var resources = root.AddComponent<SimpleWallResourceStore>();
                resources.Add(10, 10);
                var initialWood = resources.Wood;
                var initialStone = resources.Stone;
                var controller = root.AddComponent<WallConstructionController>();
                controller.Configure(world, resources);

                // Inactive scene objects are commonly used as prefab-like templates. Unity preserves
                // activeSelf when cloning them, so the commit must explicitly activate the clone.
                prefab.SetActive(false);
                var serializedController = new SerializedObject(controller);
                serializedController.FindProperty("wallPrefab").objectReferenceValue = prefab;
                serializedController.ApplyModifiedPropertiesWithoutUndo();

                GameObject builtWall = null;
                controller.WallBuilt += (_, instance) => builtWall = instance;

                var built = controller.TryBuild(Vector2Int.zero, Vector2Int.right, out var reason);

                Assert.That(built, Is.True, reason);
                Assert.That(reason, Is.Empty);
                Assert.That(resources.Wood, Is.EqualTo(initialWood - 1));
                Assert.That(resources.Stone, Is.EqualTo(initialStone - 1));
                Assert.That(controller.Edges, Has.Count.EqualTo(1));
                Assert.That(builtWall, Is.Not.Null);
                Assert.That(builtWall.activeSelf, Is.True);
                Assert.That(builtWall.activeInHierarchy, Is.True);
                Assert.That(builtWall.GetComponentInChildren<Renderer>().enabled, Is.True);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(prefab);
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void TryBuild_WhenAtomicSpendFails_DoesNotCommitOrLeaveStagedWalls()
        {
            var root = new GameObject("Transaction Test");
            try
            {
                var world = root.AddComponent<World>();
                var controller = root.AddComponent<WallConstructionController>();
                var resources = new FakeResourceStore(10, 10) { FailSpend = true };
                controller.Configure(world, null);
                controller.SetResourceStore(resources);

                var built = controller.TryBuild(Vector2Int.zero, Vector2Int.right, out _);

                Assert.That(built, Is.False);
                Assert.That(resources.SpendCalls, Is.EqualTo(1));
                Assert.That(resources.Wood, Is.EqualTo(10));
                Assert.That(resources.Stone, Is.EqualTo(10));
                Assert.That(controller.Edges, Is.Empty);
                Assert.That(HasWallBuildChild(root.transform), Is.False);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void TryBuild_ThrowingSubscriber_IsolatedAfterCommitAndReentryIsRejected()
        {
            var root = new GameObject("Event Test");
            try
            {
                var world = root.AddComponent<World>();
                var controller = root.AddComponent<WallConstructionController>();
                var resources = new FakeResourceStore(10, 10);
                controller.Configure(world, null);
                controller.SetResourceStore(resources);
                var laterSubscriberCalled = false;
                var reentrantResult = true;

                controller.WallBuilt += (_, _) => throw new InvalidOperationException("subscriber failure");
                controller.WallBuilt += (_, _) => laterSubscriberCalled = true;
                controller.WallBuilt += (_, _) =>
                    reentrantResult = controller.TryBuild(Vector2Int.right, Vector2Int.one, out _);
                LogAssert.Expect(LogType.Exception, new Regex("InvalidOperationException: subscriber failure"));

                var built = controller.TryBuild(Vector2Int.zero, Vector2Int.right, out var reason);

                Assert.That(built, Is.True);
                Assert.That(reason, Is.Empty);
                Assert.That(laterSubscriberCalled, Is.True);
                Assert.That(reentrantResult, Is.False);
                Assert.That(resources.SpendCalls, Is.EqualTo(1));
                Assert.That(resources.Wood, Is.EqualTo(9));
                Assert.That(resources.Stone, Is.EqualTo(9));
                Assert.That(controller.Edges, Has.Count.EqualTo(1));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void TryBuild_SelectedObstacleLayerBlocksPreviewRulesWithoutSpending()
        {
            var root = new GameObject("Obstacle Test");
            var obstacle = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                var world = root.AddComponent<World>();
                var controller = root.AddComponent<WallConstructionController>();
                var resources = new FakeResourceStore(10, 10);
                controller.Configure(world, null);
                controller.SetResourceStore(resources);
                controller.ObstacleLayers = 1 << 8;
                obstacle.layer = 8;
                obstacle.transform.SetPositionAndRotation(new Vector3(0.5f, 0.625f, 0f), Quaternion.identity);
                obstacle.transform.localScale = new Vector3(0.2f, 1.25f, 0.2f);
                Physics.SyncTransforms();

                var built = controller.TryBuild(Vector2Int.zero, Vector2Int.right, out var reason);

                Assert.That(built, Is.False);
                Assert.That(reason, Does.Contain("engelle"));
                Assert.That(resources.SpendCalls, Is.Zero);
                Assert.That(controller.Edges, Is.Empty);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(obstacle);
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void TryBuild_DefaultEmptyObstacleMask_IgnoresGroundLayerCollider()
        {
            var root = new GameObject("Ground Mask Test");
            var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                var world = root.AddComponent<World>();
                var controller = root.AddComponent<WallConstructionController>();
                var resources = new FakeResourceStore(10, 10);
                controller.Configure(world, null);
                controller.SetResourceStore(resources);
                ground.name = "Ground";
                ground.layer = 0;
                ground.transform.position = new Vector3(0.5f, -0.1f, 0f);
                ground.transform.localScale = new Vector3(2f, 0.2f, 2f);
                Physics.SyncTransforms();

                var built = controller.TryBuild(Vector2Int.zero, Vector2Int.right, out _);

                Assert.That(built, Is.True);
                Assert.That(resources.SpendCalls, Is.EqualTo(1));
                Assert.That(controller.Edges, Has.Count.EqualTo(1));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(ground);
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        static bool HasWallBuildChild(Transform root)
        {
            for (var i = 0; i < root.childCount; i++)
            {
                if (root.GetChild(i).name.Contains("Wall Build"))
                    return true;
            }

            return false;
        }

        static List<GameObject> GetBuiltWalls(Transform root)
        {
            var walls = new List<GameObject>();
            for (var childIndex = 0; childIndex < root.childCount; childIndex++)
            {
                var build = root.GetChild(childIndex);
                if (build.name != "Wall Build")
                    continue;

                for (var wallIndex = 0; wallIndex < build.childCount; wallIndex++)
                    walls.Add(build.GetChild(wallIndex).gameObject);
            }

            return walls;
        }

        static void AssertWallsAreActiveAndPhysical(IReadOnlyList<GameObject> walls)
        {
            Assert.That(walls, Is.Not.Empty);
            for (var i = 0; i < walls.Count; i++)
            {
                Assert.That(walls[i].activeSelf, Is.True);
                Assert.That(walls[i].activeInHierarchy, Is.True);
                Assert.That(walls[i].GetComponentInChildren<Renderer>(), Is.Not.Null);
                Assert.That(walls[i].GetComponentInChildren<Renderer>().enabled, Is.True);
                Assert.That(walls[i].GetComponentInChildren<Collider>(), Is.Not.Null);
            }
        }

        sealed class FakeResourceStore : IWallResourceStore
        {
            public FakeResourceStore(int wood, int stone)
            {
                Wood = wood;
                Stone = stone;
            }

            public int Wood { get; private set; }
            public int Stone { get; private set; }
            public int SpendCalls { get; private set; }
            public bool FailSpend { get; set; }

            public bool CanAfford(int wood, int stone)
            {
                return Wood >= wood && Stone >= stone;
            }

            public bool TrySpend(int wood, int stone)
            {
                SpendCalls++;
                if (FailSpend || !CanAfford(wood, stone))
                    return false;

                Wood -= wood;
                Stone -= stone;
                return true;
            }
        }
    }
}
