using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using IdleTown.Construction;
using NUnit.Framework;
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
