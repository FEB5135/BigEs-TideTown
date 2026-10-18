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

        [Test]
        public void Remove_OnlyRemovesRequestedEdge_AndAllowsRebuild()
        {
            var model = new WallConstructionModel();
            var first = new WallEdge(Vector2Int.zero, Vector2Int.right);
            var neighbour = new WallEdge(Vector2Int.right, new Vector2Int(2, 0));
            model.Add(new List<WallEdge> { first, neighbour });

            Assert.That(model.Remove(first), Is.True);
            Assert.That(model.Edges, Has.Count.EqualTo(1));
            Assert.That(model.Edges, Does.Contain(neighbour));
            Assert.That(model.Validate(new List<WallEdge> { first }, 4, 4, out _), Is.True);
        }

        [Test]
        public void Validate_TracksOccupancyAndCrossingsPerFloor()
        {
            var model = new WallConstructionModel();
            var diagonal = new List<WallEdge>
            {
                new(Vector2Int.zero, Vector2Int.one)
            };
            var crossing = new List<WallEdge>
            {
                new(new Vector2Int(0, 1), new Vector2Int(1, 0))
            };
            model.Set(diagonal, 0, WallTier.Timber);

            Assert.That(model.Validate(crossing, 4, 4, 0, WallTier.Timber, 3, out _), Is.False);
            // Crossing is floor-local, but an upper edge still needs support on that same edge.
            model.Set(crossing, 0, WallTier.Timber);
            Assert.That(model.Validate(crossing, 4, 4, 1, WallTier.Timber, 3, out _), Is.True);
        }

        [Test]
        public void Validate_UpperFloorRequiresSameEdgeAndEqualOrStrongerSupport()
        {
            var model = new WallConstructionModel();
            var edge = new List<WallEdge>
            {
                new(Vector2Int.zero, Vector2Int.right)
            };

            Assert.That(model.Validate(edge, 4, 4, 1, WallTier.Timber, 3, out _), Is.False);

            model.Set(edge, 0, WallTier.Timber);
            Assert.That(model.Validate(edge, 4, 4, 1, WallTier.Stone, 3, out _), Is.False);
            Assert.That(model.Validate(edge, 4, 4, 1, WallTier.Timber, 3, out _), Is.True);

            model.Set(edge, 0, WallTier.Reinforced);
            Assert.That(model.Validate(edge, 4, 4, 1, WallTier.Stone, 3, out _), Is.True);
        }

        [Test]
        public void Validate_AllowsOnlyStrictTierUpgrades()
        {
            var model = new WallConstructionModel();
            var edge = new List<WallEdge>
            {
                new(Vector2Int.zero, Vector2Int.right)
            };
            model.Set(edge, 0, WallTier.Stone);

            Assert.That(model.Validate(edge, 4, 4, 0, WallTier.Timber, 3, out _), Is.False);
            Assert.That(model.Validate(edge, 4, 4, 0, WallTier.Stone, 3, out _), Is.False);
            Assert.That(model.Validate(edge, 4, 4, 0, WallTier.Reinforced, 3, out _), Is.True);
        }
    }

    public sealed class WallConstructionControllerTests
    {
        [Test]
        public void DestroyingAnUpperFloor_PreservesBaseAndNeighbour_AndCollapsesUnsupportedFloors()
        {
            var root = new GameObject("Stack Collapse Test");
            try
            {
                var world = root.AddComponent<World>();
                var controller = root.AddComponent<WallConstructionController>();
                controller.Configure(world, null);
                controller.SetResourceStore(new FakeResourceStore(1000, 1000));
                var a = Vector2Int.zero;
                var b = Vector2Int.right;
                var c = new Vector2Int(2, 0);
                Assert.That(controller.TryBuild(a, c, 0, WallTier.Reinforced, out var reason), Is.True, reason);
                Assert.That(controller.TryBuild(a, b, 1, WallTier.Stone, out reason), Is.True, reason);
                Assert.That(controller.TryBuild(a, b, 2, WallTier.Timber, out reason), Is.True, reason);
                Assert.That(controller.Walls, Has.Count.EqualTo(4));
                var edge = new WallEdge(a, b);
                Assert.That(controller.TryDetachDestroyedWall(edge, 1, out _), Is.True);
                Assert.That(controller.Walls, Has.Count.EqualTo(2));
                Assert.That(controller.TryGetWallInstance(edge, 0, out _), Is.True);
                Assert.That(controller.TryGetWallInstance(edge, 2, out _), Is.False);
                Assert.That(controller.TryGetWallInstance(new WallEdge(b, c), 0, out _), Is.True);
                Assert.That(controller.TryBuild(a, b, 1, WallTier.Stone, out reason), Is.True, reason);
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        [Test]
        public void DestroyingBase_RemovesWholeStack_AndAllowsFreshConstruction()
        {
            var root = new GameObject("Base Collapse Test");
            try
            {
                var world = root.AddComponent<World>();
                var controller = root.AddComponent<WallConstructionController>();
                controller.Configure(world, null);
                controller.SetResourceStore(new FakeResourceStore(1000, 1000));
                var edge = new WallEdge(Vector2Int.zero, Vector2Int.right);
                Assert.That(controller.TryBuild(edge.A, edge.B, 0, WallTier.Reinforced, out var reason), Is.True, reason);
                Assert.That(controller.TryBuild(edge.A, edge.B, 1, WallTier.Stone, out reason), Is.True, reason);
                Assert.That(controller.TryBuild(edge.A, edge.B, 2, WallTier.Timber, out reason), Is.True, reason);
                Assert.That(controller.TryDetachDestroyedWall(edge, 0, out _), Is.True);
                Assert.That(controller.Walls, Is.Empty);
                Assert.That(controller.Edges, Is.Empty);
                Assert.That(controller.TryBuild(edge.A, edge.B, 0, WallTier.Timber, out reason), Is.True, reason);
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        [Test]
        public void RefreshPreview_BeforeAwake_CreatesOneRendererAndClearsItOutsideMap()
        {
            var root = new GameObject("Preview Initialization Test");
            try
            {
                var world = root.AddComponent<World>();
                var controller = root.AddComponent<WallConstructionController>();
                controller.Configure(world, null);
                controller.SetResourceStore(new FakeResourceStore(10, 10));

                controller.RefreshPreview(null);
                controller.HandleNodeClick(Vector2Int.zero);
                controller.RefreshPreview(Vector2Int.right);

                var renderers = root.GetComponentsInChildren<LineRenderer>();
                Assert.That(renderers, Has.Length.EqualTo(1));
                Assert.That(renderers[0].positionCount, Is.EqualTo(2));

                controller.RefreshPreview(null);

                Assert.That(renderers[0].positionCount, Is.Zero);
                Assert.That(root.GetComponentsInChildren<LineRenderer>(), Has.Length.EqualTo(1));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

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
                Assert.That(resources.Stone, Is.EqualTo(initialStone));
                AssertWallsAreActiveAndPhysical(firstBuild);
                Assert.That(GetBuiltWalls(root.transform), Has.Count.EqualTo(5));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void TryDetachDestroyedWall_RemovesOnlyEdgeWithoutRefund_AndAllowsRebuild()
        {
            var root = new GameObject("Wall Break Test");
            try
            {
                var world = root.AddComponent<World>();
                var resources = new FakeResourceStore(10, 10);
                var controller = root.AddComponent<WallConstructionController>();
                controller.Configure(world, null);
                controller.SetResourceStore(resources);
                GameObject eventVisual = null;
                controller.WallRemoved += (_, instance) => { eventVisual = instance; return true; };
                Assert.That(controller.TryBuild(Vector2Int.zero, new Vector2Int(2, 0), out var reason), Is.True, reason);
                var broken = new WallEdge(Vector2Int.zero, Vector2Int.right);

                Assert.That(controller.TryDetachDestroyedWall(broken, out var visual), Is.True);
                Assert.That(visual, Is.Not.Null);
                Assert.That(eventVisual, Is.SameAs(visual));
                Assert.That(controller.Edges, Has.Count.EqualTo(1));
                Assert.That(resources.Wood, Is.EqualTo(8));
                Assert.That(resources.Stone, Is.EqualTo(10));

                Assert.That(controller.TryBuild(Vector2Int.zero, Vector2Int.right, out reason), Is.True, reason);
                Assert.That(resources.Wood, Is.EqualTo(7));
                Assert.That(resources.Stone, Is.EqualTo(10));
                UnityEngine.Object.DestroyImmediate(visual);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void TryDetachDestroyedWall_WithoutRemovalOwner_DestroysDetachedVisual()
        {
            var root = new GameObject("Unobserved Wall Break Test");
            try
            {
                var world = root.AddComponent<World>();
                var resources = new FakeResourceStore(10, 10);
                var controller = root.AddComponent<WallConstructionController>();
                controller.Configure(world, null);
                controller.SetResourceStore(resources);
                Assert.That(controller.TryBuild(Vector2Int.zero, Vector2Int.right, out var reason), Is.True, reason);

                Assert.That(controller.TryDetachDestroyedWall(
                    new WallEdge(Vector2Int.zero, Vector2Int.right), out var visual), Is.True);
                Assert.That(visual, Is.Null);
                Assert.That(controller.Edges, Is.Empty);
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
                Assert.That(resources.Stone, Is.EqualTo(10));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void ConstructionPanelHitTest_ConvertsInputCoordinatesAndUsesExclusiveMaxEdges()
        {
            var root = new GameObject("Responsive construction HUD test");
            try
            {
            var controller = root.AddComponent<WallConstructionController>();
            var hud = root.AddComponent<WallConstructionHUD>();
            hud.Initialize(controller);
            Canvas.ForceUpdateCanvases();
            var panel = WallConstructionController.PanelRect;
            Assert.That(panel.width, Is.GreaterThan(0));
            Assert.That(
                WallConstructionController.IsPointerOverConstructionPanel(
                    new Vector2(panel.xMin, Screen.height - panel.yMin)),
                Is.True);
            Assert.That(
                WallConstructionController.IsPointerOverConstructionPanel(
                    new Vector2(panel.xMax, Screen.height - panel.yMin)),
                Is.False);
            Assert.That(
                WallConstructionController.IsPointerOverConstructionPanel(
                    new Vector2(panel.xMin, Screen.height - panel.yMax)),
                Is.False);
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        [Test]
        public void TryBuild_UpgradeChargesOnlyCumulativeCostDeltaAndReplacesMetadata()
        {
            var root = new GameObject("Tier Upgrade Test");
            try
            {
                var world = root.AddComponent<World>();
                var resources = new FakeResourceStore(20, 20);
                var controller = root.AddComponent<WallConstructionController>();
                controller.Configure(world, null);
                controller.SetResourceStore(resources);

                Assert.That(controller.TryBuild(Vector2Int.zero, Vector2Int.right, 0, WallTier.Timber, out var timberReason), Is.True, timberReason);
                Assert.That(resources.Wood, Is.EqualTo(19));
                Assert.That(resources.Stone, Is.EqualTo(20));

                Assert.That(controller.TryBuild(Vector2Int.right, Vector2Int.zero, 0, WallTier.Stone, out var stoneReason), Is.True, stoneReason);

                Assert.That(resources.SpendCalls, Is.EqualTo(2));
                Assert.That(resources.Wood, Is.EqualTo(19));
                Assert.That(resources.Stone, Is.EqualTo(16));
                Assert.That(controller.Edges, Has.Count.EqualTo(1));
                Assert.That(controller.Walls, Has.Count.EqualTo(1));
                var walls = GetBuiltWalls(root.transform);
                Assert.That(walls, Has.Count.EqualTo(1));
                Assert.That(walls[0].GetComponent<WallSegment>().Tier, Is.EqualTo(WallTier.Stone));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void TryBuild_FailedUpgradePreservesPriorWallAndResources()
        {
            var root = new GameObject("Failed Upgrade Test");
            try
            {
                var world = root.AddComponent<World>();
                var resources = new FakeResourceStore(20, 20);
                var controller = root.AddComponent<WallConstructionController>();
                controller.Configure(world, null);
                controller.SetResourceStore(resources);
                Assert.That(controller.TryBuild(Vector2Int.zero, Vector2Int.right, 0, WallTier.Timber, out _), Is.True);
                var original = GetBuiltWalls(root.transform)[0];
                var woodBefore = resources.Wood;
                var stoneBefore = resources.Stone;
                resources.FailSpend = true;

                Assert.That(controller.TryBuild(Vector2Int.zero, Vector2Int.right, 0, WallTier.Stone, out _), Is.False);

                Assert.That(resources.Wood, Is.EqualTo(woodBefore));
                Assert.That(resources.Stone, Is.EqualTo(stoneBefore));
                Assert.That(original.activeInHierarchy, Is.True);
                Assert.That(original.GetComponent<WallSegment>().Tier, Is.EqualTo(WallTier.Timber));
                Assert.That(controller.Walls, Has.Count.EqualTo(1));
                Assert.That(GetBuiltWalls(root.transform), Has.Count.EqualTo(1));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void TryBuild_UpperFloorRequiresSupportAndUsesElevatedGeometryMetadata()
        {
            var root = new GameObject("Floor Support Test");
            try
            {
                var world = root.AddComponent<World>();
                var resources = new FakeResourceStore(20, 20);
                var controller = root.AddComponent<WallConstructionController>();
                controller.Configure(world, null);
                controller.SetResourceStore(resources);

                Assert.That(controller.TryBuild(Vector2Int.zero, Vector2Int.right, 1, WallTier.Timber, out _), Is.False);
                Assert.That(resources.SpendCalls, Is.Zero);
                Assert.That(controller.TryBuild(Vector2Int.zero, Vector2Int.right, 0, WallTier.Timber, out _), Is.True);
                Assert.That(controller.TryBuild(Vector2Int.zero, Vector2Int.right, 1, WallTier.Timber, out var reason), Is.True, reason);

                var walls = GetBuiltWalls(root.transform);
                Assert.That(walls, Has.Count.EqualTo(2));
                walls.Sort((left, right) => left.GetComponent<WallSegment>().Floor.CompareTo(right.GetComponent<WallSegment>().Floor));
                Assert.That(walls[0].transform.position.y, Is.EqualTo(1f).Within(0.001f));
                Assert.That(walls[1].transform.position.y, Is.EqualTo(3f).Within(0.001f));
                Assert.That(walls[1].GetComponent<WallSegment>().Floor, Is.EqualTo(1));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void TryBuild_ConfigurableFloorZeroBaseAndMaximumAreApplied()
        {
            var root = new GameObject("Floor Configuration Test");
            try
            {
                var world = root.AddComponent<World>();
                var resources = new FakeResourceStore(20, 20);
                var controller = root.AddComponent<WallConstructionController>();
                controller.Configure(world, null);
                controller.SetResourceStore(resources);
                var serializedController = new SerializedObject(controller);
                serializedController.FindProperty("floorZeroBaseHeight").floatValue = 2f;
                serializedController.FindProperty("maxFloors").intValue = 2;
                serializedController.ApplyModifiedPropertiesWithoutUndo();

                Assert.That(controller.TryBuild(Vector2Int.zero, Vector2Int.right, 0, WallTier.Timber, out var reason), Is.True, reason);
                Assert.That(GetBuiltWalls(root.transform)[0].transform.position.y, Is.EqualTo(3f).Within(0.001f));
                Assert.That(controller.TryBuild(Vector2Int.zero, Vector2Int.right, 2, WallTier.Timber, out _), Is.False);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void TryBuild_ObstacleChecksUseTheSelectedFloorElevation()
        {
            var root = new GameObject("Elevated Obstacle Test");
            var obstacle = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                var world = root.AddComponent<World>();
                var resources = new FakeResourceStore(20, 20);
                var controller = root.AddComponent<WallConstructionController>();
                controller.Configure(world, null);
                controller.SetResourceStore(resources);
                controller.ObstacleLayers = 1 << 8;
                obstacle.layer = 8;
                obstacle.transform.position = new Vector3(0.5f, 3f, 0f);
                obstacle.transform.localScale = new Vector3(0.2f, 0.2f, 0.2f);
                Physics.SyncTransforms();

                Assert.That(controller.TryBuild(Vector2Int.zero, Vector2Int.right, 0, WallTier.Timber, out var baseReason), Is.True, baseReason);
                var woodAfterBase = resources.Wood;
                var stoneAfterBase = resources.Stone;
                Assert.That(controller.TryBuild(Vector2Int.zero, Vector2Int.right, 1, WallTier.Timber, out var upperReason), Is.False);
                Assert.That(upperReason, Does.Contain("engelle"));
                Assert.That(resources.Wood, Is.EqualTo(woodAfterBase));
                Assert.That(resources.Stone, Is.EqualTo(stoneAfterBase));
                Assert.That(controller.Walls, Has.Count.EqualTo(1));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(obstacle);
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void CachedPreview_IsInvalidatedByFloorChangeAndResourceLoss()
        {
            var root = new GameObject("Cached Preview Test");
            try
            {
                var world = root.AddComponent<World>();
                var resources = new FakeResourceStore(2, 2);
                var controller = root.AddComponent<WallConstructionController>();
                controller.Configure(world, null);
                controller.SetResourceStore(resources);
                controller.HandleNodeClick(Vector2Int.zero);
                controller.RefreshPreview(Vector2Int.right);

                controller.SetSelectedFloor(1);
                Assert.That(controller.ConfirmLastValidPreview(), Is.False);

                controller.SetSelectedFloor(0);
                controller.RefreshPreview(Vector2Int.right);
                Assert.That(resources.TrySpend(2, 2), Is.True);
                controller.RefreshPreview(Vector2Int.right);
                Assert.That(controller.ConfirmLastValidPreview(), Is.False);
                Assert.That(controller.Edges, Is.Empty);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void PreviewAndGeneratedMaterials_ReflectSelectedFloorAndTier()
        {
            var root = new GameObject("Floor Preview And Tier Visual Test");
            try
            {
                var world = root.AddComponent<World>();
                var resources = new FakeResourceStore(20, 20);
                var controller = root.AddComponent<WallConstructionController>();
                controller.Configure(world, null);
                controller.SetResourceStore(resources);
                controller.SetSelectedFloor(1);
                controller.HandleNodeClick(Vector2Int.zero);
                controller.RefreshPreview(Vector2Int.right);

                var preview = root.GetComponentInChildren<LineRenderer>();
                Assert.That(preview, Is.Not.Null);
                Assert.That(preview.GetPosition(0).y, Is.EqualTo(4.05f).Within(0.001f));
                Assert.That(preview.GetPosition(1).y, Is.EqualTo(4.05f).Within(0.001f));

                Assert.That(controller.TryBuild(new Vector2Int(0, 1), new Vector2Int(1, 1), 0, WallTier.Timber, out var timberReason), Is.True, timberReason);
                Assert.That(controller.TryBuild(new Vector2Int(0, 2), new Vector2Int(1, 2), 0, WallTier.Stone, out var stoneReason), Is.True, stoneReason);
                var walls = GetBuiltWalls(root.transform);
                var timberColor = default(Color);
                var stoneColor = default(Color);
                foreach (var wall in walls)
                {
                    var segment = wall.GetComponent<WallSegment>();
                    if (segment.Tier == WallTier.Timber)
                        timberColor = wall.GetComponentInChildren<Renderer>().sharedMaterial.color;
                    if (segment.Tier == WallTier.Stone)
                        stoneColor = wall.GetComponentInChildren<Renderer>().sharedMaterial.color;
                }
                Assert.That(stoneColor, Is.Not.EqualTo(timberColor));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
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
                Assert.That(resources.Stone, Is.EqualTo(initialStone));
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
                Assert.That(resources.Stone, Is.EqualTo(initialStone));
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
                Assert.That(resources.Stone, Is.EqualTo(10));
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

        [Test]
        public void AutoHeight_RepeatedPathPreviewsAndBuildsNextFloorWithoutDrifting()
        {
            var root = new GameObject("Automatic wall height");
            try
            {
                var controller = root.AddComponent<WallConstructionController>();
                controller.Configure(root.AddComponent<World>(), null);
                controller.SetResourceStore(new FakeResourceStore(100, 100));
                Assert.That(controller.TryBuild(Vector2Int.zero, Vector2Int.right, out _), Is.True);
                controller.HandleNodeClick(Vector2Int.zero);
                for (int i = 0; i < 5; i++) controller.RefreshPreview(Vector2Int.right);
                Assert.That(controller.SelectedFloor, Is.EqualTo(1));
                controller.RefreshPreview(Vector2Int.up);
                Assert.That(controller.SelectedFloor, Is.Zero, "Moving to fresh ground should return to the base floor.");
                controller.RefreshPreview(Vector2Int.right);
                Assert.That(controller.ConfirmHoveredPreview(), Is.True);
                Assert.That(controller.Walls, Has.Count.EqualTo(2));
                controller.HandleNodeClick(Vector2Int.zero);
                controller.RefreshPreview(Vector2Int.right);
                Assert.That(controller.SelectedFloor, Is.EqualTo(2));
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        [Test]
        public void AutoHeight_ManualFloorAndDisabledAutoAreRespected()
        {
            var root = new GameObject("Manual wall height");
            try
            {
                var controller = root.AddComponent<WallConstructionController>();
                controller.Configure(root.AddComponent<World>(), null);
                controller.SetResourceStore(new FakeResourceStore(100, 100));
                controller.TryBuild(Vector2Int.zero, Vector2Int.right, out _);
                controller.HandleNodeClick(Vector2Int.zero);
                controller.RefreshPreview(Vector2Int.right);
                controller.SetSelectedFloor(0);
                Assert.That(controller.SelectedFloor, Is.Zero);
                Assert.That(controller.CanConfirm, Is.False);
                controller.SetAutomaticHeight(false);
                controller.RefreshPreview(Vector2Int.right);
                Assert.That(controller.SelectedFloor, Is.Zero);
                controller.SetAutomaticHeight(true);
                Assert.That(controller.SelectedFloor, Is.EqualTo(1));
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        [Test]
        public void AutoHeight_PreservesMaterialUpgradeAndRejectsUnsupportedMixedPath()
        {
            var root = new GameObject("Wall height support");
            try
            {
                var controller = root.AddComponent<WallConstructionController>();
                controller.Configure(root.AddComponent<World>(), null);
                controller.SetResourceStore(new FakeResourceStore(100, 100));
                controller.TryBuild(Vector2Int.zero, Vector2Int.right, out _);
                controller.SetSelectedTier(WallTier.Stone);
                controller.HandleNodeClick(Vector2Int.zero);
                controller.RefreshPreview(Vector2Int.right);
                Assert.That(controller.SelectedFloor, Is.Zero, "Stronger materials should still upgrade the existing wall.");
                Assert.That(controller.ConfirmHoveredPreview(), Is.True);
                controller.HandleNodeClick(Vector2Int.zero);
                controller.RefreshPreview(new Vector2Int(2, 0));
                Assert.That(controller.SelectedFloor, Is.Zero, "A partly supported line must not jump to a floating upper floor.");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        [Test]
        public void AutoHeight_FullStackStopsAtTwentyMetresWithoutSpending()
        {
            var root = new GameObject("Wall height limit");
            try
            {
                var controller = root.AddComponent<WallConstructionController>();
                controller.Configure(root.AddComponent<World>(), null);
                var resources = new FakeResourceStore(100, 100);
                controller.SetResourceStore(resources);
                for (int floor = 0; floor < 10; floor++)
                    Assert.That(controller.TryBuild(Vector2Int.zero, Vector2Int.right, floor, WallTier.Timber, out _), Is.True);
                int before = resources.SpendCalls;
                controller.HandleNodeClick(Vector2Int.zero);
                controller.RefreshPreview(Vector2Int.right);
                Assert.That(controller.SelectedFloor, Is.EqualTo(9));
                Assert.That(controller.ConfirmHoveredPreview(), Is.False);
                Assert.That(resources.SpendCalls, Is.EqualTo(before));
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        static bool HasWall(WallConstructionController controller, WallEdge edge, int floor)
        {
            foreach (var wall in controller.Walls)
                if (wall.Floor == floor && wall.Edge.Equals(edge))
                    return true;
            return false;
        }

        static void WithController(int wood, Action<WallConstructionController> test)
        {
            var root = new GameObject("Auto Floor Test");
            try
            {
                var world = root.AddComponent<World>();
                var controller = root.AddComponent<WallConstructionController>();
                controller.Configure(world, null);
                controller.SetResourceStore(new FakeResourceStore(wood, 100));
                test(controller);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void AutoFloor_PickingTheEndsOfAWallInReverseRaisesThatSameWall()
        {
            WithController(100, controller =>
            {
                Assert.That(controller.TryBuild(new Vector2Int(0, 0), new Vector2Int(5, 2), out var reason), Is.True, reason);
                var floorOne = new List<WallEdge>();
                foreach (var wall in controller.Walls) floorOne.Add(wall.Edge);

                controller.HandleNodeClick(new Vector2Int(5, 2));
                controller.HandleNodeClick(new Vector2Int(0, 0));
                controller.HandleNodeClick(new Vector2Int(0, 0));
                controller.HandleNodeClick(new Vector2Int(5, 2));

                Assert.That(controller.Walls, Has.Count.EqualTo(floorOne.Count * 3));
                foreach (var edge in floorOne)
                {
                    Assert.That(HasWall(controller, edge, 1), Is.True, $"floor 2 {edge}");
                    Assert.That(HasWall(controller, edge, 2), Is.True, $"floor 3 {edge}");
                }
                Assert.That(controller.SelectedFloor, Is.EqualTo(2));
            });
        }

        [Test]
        public void AutoFloor_FollowsAWallBuiltInTwoStrokes()
        {
            WithController(100, controller =>
            {
                // (0,0)-(3,1) then (3,1)-(6,2): both ends sit on one wall that is as short as a straight line.
                Assert.That(controller.TryBuild(new Vector2Int(0, 0), new Vector2Int(3, 1), out var a), Is.True, a);
                Assert.That(controller.TryBuild(new Vector2Int(3, 1), new Vector2Int(6, 2), out var b), Is.True, b);
                var floorOne = controller.Walls.Count;

                controller.HandleNodeClick(new Vector2Int(0, 0));
                controller.HandleNodeClick(new Vector2Int(6, 2));

                Assert.That(controller.Walls, Has.Count.EqualTo(floorOne * 2));
                foreach (var wall in controller.Walls)
                    if (wall.Floor == 1)
                        Assert.That(HasWall(controller, wall.Edge, 0), Is.True, wall.Edge.ToString());
            });
        }

        [Test]
        public void AutoFloor_OpenEndsOfAUCloseWithAGroundWall()
        {
            WithController(100, controller =>
            {
                Assert.That(controller.TryBuild(new Vector2Int(0, 0), new Vector2Int(0, 4), out var a), Is.True, a);
                Assert.That(controller.TryBuild(new Vector2Int(0, 4), new Vector2Int(3, 4), out var b), Is.True, b);
                Assert.That(controller.TryBuild(new Vector2Int(3, 4), new Vector2Int(3, 0), out var c), Is.True, c);

                controller.HandleNodeClick(new Vector2Int(0, 0));
                controller.HandleNodeClick(new Vector2Int(3, 0));

                Assert.That(controller.Walls, Has.Count.EqualTo(14));
                Assert.That(HasWall(controller, new WallEdge(new Vector2Int(1, 0), new Vector2Int(2, 0)), 0), Is.True);
                foreach (var wall in controller.Walls) Assert.That(wall.Floor, Is.Zero);
            });
        }

        [Test]
        public void AutoFloor_RaisesTheLowestPartFirstAndExtendsOnlyTheMissingGround()
        {
            WithController(100, controller =>
            {
                Assert.That(controller.TryBuild(new Vector2Int(0, 0), new Vector2Int(4, 0), out var a), Is.True, a);
                Assert.That(controller.TryBuild(new Vector2Int(0, 0), new Vector2Int(2, 0), 1, WallTier.Timber, out var b), Is.True, b);

                controller.HandleNodeClick(new Vector2Int(0, 0));
                controller.HandleNodeClick(new Vector2Int(4, 0));
                // Only the two one-storey edges rose; the wall is now level at two storeys.
                Assert.That(controller.Walls, Has.Count.EqualTo(8));
                Assert.That(HasWall(controller, new WallEdge(new Vector2Int(3, 0), new Vector2Int(4, 0)), 1), Is.True);
                Assert.That(HasWall(controller, new WallEdge(new Vector2Int(0, 0), new Vector2Int(1, 0)), 2), Is.False);

                controller.HandleNodeClick(new Vector2Int(2, 0));
                controller.HandleNodeClick(new Vector2Int(6, 0));
                // Past the wall's end only the new ground edges are built.
                Assert.That(controller.Walls, Has.Count.EqualTo(10));
                Assert.That(HasWall(controller, new WallEdge(new Vector2Int(5, 0), new Vector2Int(6, 0)), 0), Is.True);
            });
        }

        [Test]
        public void AutoFloor_UpgradesAWeakerMaterialBeforeStacking()
        {
            WithController(100, controller =>
            {
                Assert.That(controller.TryBuild(new Vector2Int(0, 0), new Vector2Int(2, 0), out var a), Is.True, a);
                controller.SetSelectedTier(WallTier.Stone);

                controller.HandleNodeClick(new Vector2Int(0, 0));
                controller.HandleNodeClick(new Vector2Int(2, 0));
                Assert.That(controller.Walls, Has.Count.EqualTo(2));
                foreach (var wall in controller.Walls) Assert.That(wall.Tier, Is.EqualTo(WallTier.Stone));

                controller.HandleNodeClick(new Vector2Int(0, 0));
                controller.HandleNodeClick(new Vector2Int(2, 0));
                Assert.That(controller.Walls, Has.Count.EqualTo(4));
                Assert.That(HasWall(controller, new WallEdge(new Vector2Int(0, 0), new Vector2Int(1, 0)), 1), Is.True);
            });
        }

        [Test]
        public void AutoFloor_StopsAtTheTopFloorAndArrowsOverrideItForOneSelection()
        {
            WithController(1000, controller =>
            {
                var top = controller.MaxFloors;
                for (var floor = 0; floor < top; floor++)
                {
                    controller.HandleNodeClick(new Vector2Int(0, 0));
                    controller.HandleNodeClick(new Vector2Int(1, 0));
                }
                Assert.That(controller.Walls, Has.Count.EqualTo(top));

                controller.HandleNodeClick(new Vector2Int(0, 0));
                Assert.That(controller.ConfirmEndpoint(new Vector2Int(1, 0)), Is.False);
                Assert.That(controller.Walls, Has.Count.EqualTo(top));

                controller.SetSelectedFloor(0);
                Assert.That(controller.AutoFloorActive, Is.False);
                Assert.That(controller.ConfirmEndpoint(new Vector2Int(0, 1)), Is.True);
                Assert.That(HasWall(controller, new WallEdge(new Vector2Int(0, 0), new Vector2Int(0, 1)), 0), Is.True);
                Assert.That(controller.AutoFloorActive, Is.True);
            });
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
