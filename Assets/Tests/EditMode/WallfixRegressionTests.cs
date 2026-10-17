using IdleTown.Construction;
using NUnit.Framework;
using UnityEngine;

namespace IdleTown.Tests
{
    public sealed class WallfixRegressionTests
    {
        GameObject root;
        WallConstructionController controller;
        Store store;
        Mesh terrainMesh;
        readonly System.Collections.Generic.List<Collider> sceneColliders = new();

        [SetUp]
        public void SetUp()
        {
            // Integration tests may leave the team map loaded. Keep its physics out of this fixture.
            sceneColliders.Clear();
            foreach (var collider in Object.FindObjectsByType<Collider>(FindObjectsSortMode.None))
                if (collider.enabled)
                { sceneColliders.Add(collider); collider.enabled = false; }
            root = new GameObject("Wallfix test world");
            var world = root.AddComponent<World>();
            controller = root.AddComponent<WallConstructionController>();
            controller.Configure(world, null);
            controller.SetResourceStore(store = new Store());
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(root);
            if (terrainMesh != null) Object.DestroyImmediate(terrainMesh);
            foreach (var collider in sceneColliders)
                if (collider != null) collider.enabled = true;
            sceneColliders.Clear();
        }

        WallTerrainSurface CreateSlope()
        {
            var ground = new GameObject("Test hillside", typeof(MeshFilter), typeof(MeshCollider));
            ground.transform.SetParent(root.transform);
            terrainMesh = new Mesh();
            terrainMesh.vertices = new[] { new Vector3(0,0,0), new Vector3(16,4.8f,0),
                new Vector3(0,0,16), new Vector3(16,4.8f,16) };
            terrainMesh.triangles = new[] { 0,2,1,1,2,3 };
            terrainMesh.RecalculateNormals(); terrainMesh.RecalculateBounds();
            ground.GetComponent<MeshFilter>().sharedMesh = terrainMesh;
            ground.GetComponent<MeshCollider>().sharedMesh = terrainMesh;
            var surface = root.AddComponent<WallTerrainSurface>();
            surface.Configure(new Collider[] { ground.GetComponent<MeshCollider>() });
            controller.ConfigureTerrain(surface);
            Physics.SyncTransforms();
            return surface;
        }

        [Test]
        public void SlopedWallColliderMeetsTerrainAtBothEndsAndSupportsTenFloors()
        {
            var terrain = CreateSlope();
            controller.ConfigureTerrain(terrain, 100);
            Assert.That(controller.MaxFloors, Is.EqualTo(10), "The 20 metre cap must also clamp configuration overrides.");
            var a = new Vector2Int(2, 2); var b = new Vector2Int(3, 2);
            for (int floor = 0; floor < 10; floor++)
                Assert.That(controller.TryBuild(a, b, floor, WallTier.Timber, out var reason), Is.True, reason);
            Assert.That(controller.TryBuild(a, b, 10, WallTier.Timber, out _), Is.False);
            controller.SetSelectedFloor(100);
            Assert.That(controller.SelectedFloor, Is.EqualTo(9));
            controller.TryGetWallInstance(new WallEdge(a, b), 0, out var wall);
            var collider = wall.GetComponent<MeshCollider>();
            Assert.That(collider, Is.Not.Null);
            foreach (var vertex in collider.sharedMesh.vertices)
                if ((vertex.y - vertex.z * wall.GetComponent<WallSlopeGeometry>().Rise) < 0)
                {
                    var point = wall.transform.TransformPoint(vertex);
                    terrain.Sample(point, out var ground);
                    // Width lies across the slope here, so each side differs by at most half wall thickness * grade.
                    Assert.That(Mathf.Abs(point.y - ground.y), Is.LessThan(.03f));
                }
            Assert.That(wall.GetComponent<WallSlopeGeometry>().Rise, Is.GreaterThan(.1f));
            Assert.That(wall.transform.localScale.y, Is.EqualTo(2f));
            controller.TryGetWallInstance(new WallEdge(a, b), 9, out var topWall);
            Assert.That(topWall.transform.position.y - wall.transform.position.y, Is.EqualTo(18f).Within(.001f));
            Assert.That(WallConstructionController.MaximumWallHeight, Is.EqualTo(20f));
            controller.SetDemolitionMode(true);
            controller.RefreshDemolitionPreview(wall.GetComponent<WallSegment>());
            Assert.That(System.Array.FindAll(root.GetComponentsInChildren<LineRenderer>(),
                line => line.name == "Demolition Outline"), Has.Length.EqualTo(10));
        }

        [Test]
        public void TerrainDoesNotHideBuildingsOrAllowWallsBeyondItsFootprint()
        {
            CreateSlope();
            var building = GameObject.CreatePrimitive(PrimitiveType.Cube);
            building.transform.SetParent(root.transform);
            building.transform.position = new Vector3(2.5f, 1.2f, 2);
            Assert.That(controller.TryBuild(new Vector2Int(2,2), new Vector2Int(3,2), out _), Is.False);
            Assert.That(store.Wood, Is.EqualTo(1000));
            Object.DestroyImmediate(building);
            controller.transform.position = Vector3.zero;
            terrainMesh.vertices = new[] { new Vector3(0,0,0), new Vector3(4,1.2f,0),
                new Vector3(0,0,4), new Vector3(4,1.2f,4) };
            terrainMesh.RecalculateBounds();
            var collider = root.GetComponentInChildren<MeshCollider>();
            collider.sharedMesh = null; collider.sharedMesh = terrainMesh;
            Physics.SyncTransforms();
            Assert.That(controller.TryBuild(new Vector2Int(6,2), new Vector2Int(7,2), out _), Is.False);
            Assert.That(controller.Walls, Is.Empty);
        }

        [Test]
        public void NEntersDemolitionFromBothModesAndIgnoresRepeat()
        {
            controller.SetConstructionMode(false);
            controller.HandleDemolitionKey(true);
            Assert.That(controller.ConstructionModeActive, Is.True);
            Assert.That(controller.DemolitionMode, Is.True);
            controller.HandleDemolitionKey(true);
            Assert.That(controller.DemolitionMode, Is.True);
            controller.HandleDemolitionKey(false);
            controller.HandleDemolitionKey(true);
            Assert.That(controller.DemolitionMode, Is.False);
            controller.HandleDemolitionKey(false);
            controller.HandleDemolitionKey(true);
            Assert.That(controller.DemolitionMode, Is.True);
        }

        [Test]
        public void DemolitionPreviewShowsOnlySelectedColumnAndClearsOnModeExit()
        {
            var a = new Vector2Int(2, 2);
            var b = new Vector2Int(3, 2);
            for (int floor = 0; floor < 3; floor++)
                Assert.That(controller.TryBuild(a, b, floor, WallTier.Timber, out _), Is.True);
            Assert.That(controller.TryBuild(b, new Vector2Int(4, 2), out _), Is.True);
            controller.SetDemolitionMode(true);
            controller.TryGetWallInstance(new WallEdge(a, b), 1, out var wall);
            controller.RefreshDemolitionPreview(wall.GetComponent<WallSegment>());
            var outlines = System.Array.FindAll(root.GetComponentsInChildren<LineRenderer>(),
                line => line.name == "Demolition Outline");
            Assert.That(outlines, Has.Length.EqualTo(2));
            foreach (var line in outlines)
            {
                Assert.That(line.positionCount, Is.EqualTo(16));
                Assert.That(line.sharedMaterial.color.r, Is.GreaterThan(.9f));
                Assert.That(line.sharedMaterial.color.b, Is.LessThan(.1f));
                Assert.That(line.GetComponent<Collider>(), Is.Null);
            }
            Assert.That(controller.Walls.Count, Is.EqualTo(4));
            controller.RefreshDemolitionPreview(null);
            Assert.That(System.Array.FindAll(root.GetComponentsInChildren<LineRenderer>(),
                line => line.name == "Demolition Outline"), Is.Empty);
            controller.RefreshDemolitionPreview(wall.GetComponent<WallSegment>());
            controller.SetConstructionMode(false);
            Assert.That(System.Array.FindAll(root.GetComponentsInChildren<LineRenderer>(),
                line => line.name == "Demolition Outline"), Is.Empty);
        }

        [Test]
        public void FloorKeysRequireReleaseBeforeAnotherStep()
        {
            controller.HandleFloorKey(KeyCode.UpArrow, true);
            Assert.That(controller.SelectedFloor, Is.EqualTo(1));
            controller.HandleFloorKey(KeyCode.UpArrow, true);
            Assert.That(controller.SelectedFloor, Is.EqualTo(1));
            controller.HandleFloorKey(KeyCode.UpArrow, false);
            controller.HandleFloorKey(KeyCode.UpArrow, true);
            Assert.That(controller.SelectedFloor, Is.EqualTo(2));
            controller.HandleFloorKey(KeyCode.DownArrow, true);
            Assert.That(controller.SelectedFloor, Is.EqualTo(1));
            controller.HandleFloorKey(KeyCode.DownArrow, true);
            Assert.That(controller.SelectedFloor, Is.EqualTo(1));
            controller.HandleFloorKey(KeyCode.DownArrow, false);
            controller.HandleFloorKey(KeyCode.DownArrow, true);
            Assert.That(controller.SelectedFloor, Is.Zero);
        }

        [Test]
        public void DemolitionRemovesUpperFloorsWithoutRefundAndAllowsRebuild()
        {
            var a = new Vector2Int(2, 2);
            var b = new Vector2Int(3, 2);
            for (int floor = 0; floor < 3; floor++)
                Assert.That(controller.TryBuild(a, b, floor, WallTier.Timber, out var reason), Is.True, reason);
            controller.TryGetWallInstance(new WallEdge(a, b), 0, out var wall);
            Assert.That(controller.TryDemolishWall(wall.GetComponent<WallSegment>()), Is.False);
            controller.SetDemolitionMode(true);
            int wood = store.Wood, stone = store.Stone;
            Assert.That(controller.TryDemolishWall(wall.GetComponent<WallSegment>()), Is.True);
            Assert.That(controller.Walls, Is.Empty);
            Assert.That(CountJoints(), Is.Zero);
            Assert.That(store.Wood, Is.EqualTo(wood));
            Assert.That(store.Stone, Is.EqualTo(stone));
            controller.SetDemolitionMode(false);
            Assert.That(controller.TryBuild(a, b, out _), Is.True);
        }

        [Test]
        public void DefaultLayerBuildingAndLowFoundationBlockWithoutSpending()
        {
            var building = GameObject.CreatePrimitive(PrimitiveType.Cube);
            building.transform.SetParent(root.transform);
            building.transform.position = new Vector3(2.5f, .02f, 3);
            building.transform.localScale = new Vector3(.15f, .04f, .15f);
            Assert.That(controller.ObstacleLayers.value, Is.Zero);
            Assert.That(controller.TryBuild(new Vector2Int(1, 3), new Vector2Int(4, 3), out var reason), Is.False);
            Assert.That(reason, Does.Contain("çakışıyor"));
            Assert.That(store.Wood, Is.EqualTo(1000));
            Assert.That(controller.Walls.Count, Is.Zero);
        }

        [Test]
        public void ForeignControllerWallBlocksEvenWithEmptyMask()
        {
            var foreign = new GameObject("Other construction owner");
            foreign.transform.SetParent(root.transform);
            var other = foreign.AddComponent<WallConstructionController>();
            other.Configure(root.GetComponent<World>(), null);
            other.SetResourceStore(new Store());
            Assert.That(other.TryBuild(new Vector2Int(2, 3), new Vector2Int(3, 3), out _), Is.True);
            Assert.That(controller.TryBuild(new Vector2Int(2, 3), new Vector2Int(3, 3), out _), Is.False);
            Assert.That(store.Wood, Is.EqualTo(1000));
        }

        [Test]
        public void SharedCornerHasOnePostAndUpgradeDoesNotCreateOverlappingDuplicates()
        {
            Assert.That(controller.TryBuild(new Vector2Int(2, 2), new Vector2Int(4, 2), out var a), Is.True, a);
            Assert.That(controller.TryBuild(new Vector2Int(4, 2), new Vector2Int(4, 4), out var b), Is.True, b);
            Assert.That(CountJoints(), Is.EqualTo(5));
            Assert.That(controller.TryBuild(new Vector2Int(2, 2), new Vector2Int(4, 2), 0, WallTier.Stone, out var c), Is.True, c);
            Assert.That(CountJoints(), Is.EqualTo(5));
            Assert.That(root.GetComponentsInChildren<WallSegment>(), Has.Length.EqualTo(4));
            Assert.That(controller.TryBuild(new Vector2Int(4, 2), new Vector2Int(2, 2), out _), Is.False);
            Assert.That(controller.TryDetachDestroyedWall(new WallEdge(new Vector2Int(4, 3), new Vector2Int(4, 4)), out _), Is.True);
            Assert.That(CountJoints(), Is.EqualTo(4));
        }

        [Test]
        public void RejectedConfirmationUsesRedMaterialAndGhostsNeverBlockPhysics()
        {
            controller.HandleNodeClick(new Vector2Int(1, 4));
            controller.RefreshPreview(new Vector2Int(3, 4));
            var line = root.GetComponentInChildren<LineRenderer>();
            Assert.That(line.sharedMaterial.color.g, Is.GreaterThan(line.sharedMaterial.color.r));
            Assert.That(root.GetComponentsInChildren<Collider>(), Is.Empty);
            var building = GameObject.CreatePrimitive(PrimitiveType.Cube);
            building.transform.SetParent(root.transform);
            building.transform.position = new Vector3(2, .5f, 4);
            Assert.That(controller.ConfirmEndpoint(new Vector2Int(3, 4)), Is.False);
            Assert.That(line.sharedMaterial.color.r, Is.GreaterThan(line.sharedMaterial.color.g));
            Assert.That(store.Wood, Is.EqualTo(1000));
            controller.SetConstructionMode(false);
            Assert.That(line.positionCount, Is.Zero);
            foreach (var filter in root.GetComponentsInChildren<MeshFilter>())
                Assert.That(filter.name, Is.Not.EqualTo("Wall Ghost"));
        }

        [Test]
        public void ResourceLossTurnsActualPreviewMaterialRedWithoutBuilding()
        {
            controller.HandleNodeClick(new Vector2Int(1, 4));
            controller.RefreshPreview(new Vector2Int(3, 4));
            store.Available = false;
            controller.RefreshPreview(new Vector2Int(3, 4));
            Assert.That(controller.ConfirmHoveredPreview(), Is.False);
            var color = root.GetComponentInChildren<LineRenderer>().sharedMaterial.color;
            Assert.That(color.r, Is.GreaterThan(color.g));
            Assert.That(controller.Walls.Count, Is.Zero);
        }

        [Test]
        public void ArtFitsPlacementEnvelopeAndHasThreeDistinctPalettes()
        {
            using var art = new WallArt();
            foreach (WallTier tier in new[] { WallTier.Timber, WallTier.Stone, WallTier.Reinforced })
            {
                var mesh = art.Panel(tier, .117f);
                Assert.That(mesh.vertexCount, Is.GreaterThan(100));
                foreach (var vertex in mesh.vertices)
                {
                    Assert.That(Mathf.Abs(vertex.x), Is.LessThanOrEqualTo(.501f));
                    Assert.That(Mathf.Abs(vertex.y), Is.LessThanOrEqualTo(.501f));
                    Assert.That(Mathf.Abs(vertex.z), Is.LessThanOrEqualTo(.384f));
                }
            }
            Assert.That(art.Palette(WallTier.Stone)[0].color, Is.Not.EqualTo(art.Palette(WallTier.Timber)[0].color));
        }

        int CountJoints()
        {
            int result = 0;
            foreach (Transform child in root.transform) if (child.name == "Wall Joint") result++;
            return result;
        }

        sealed class Store : IWallResourceStore
        {
            public int Wood { get; private set; } = 1000;
            public int Stone { get; private set; } = 1000;
            public bool Available = true;
            public bool CanAfford(int wood, int stone) => Available && Wood >= wood && Stone >= stone;
            public bool TrySpend(int wood, int stone)
            {
                if (!CanAfford(wood, stone)) return false;
                Wood -= wood; Stone -= stone; return true;
            }
        }
    }
}
