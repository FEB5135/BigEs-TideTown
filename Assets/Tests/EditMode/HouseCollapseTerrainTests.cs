using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace IdleTown.Tests
{
    public class HouseCollapseTerrainTests
    {
        GameObject root;
        readonly List<Mesh> meshes = new();

        [SetUp] public void SetUp() => root = new GameObject("House collapse regression");

        [TearDown] public void TearDown()
        {
            Object.DestroyImmediate(root);
            foreach (var mesh in meshes) Object.DestroyImmediate(mesh);
            meshes.Clear();
        }

        [TestCase("slope_0", 1)]
        [TestCase("ground_1", 4)]
        [TestCase("cornerslope_1", 6)]
        public void StaleHouseCells_OnlyBreakRegisteredHouse_LeaveLandscapeUnchanged(string terrainName, int terrainId)
        {
            var world = root.AddComponent<World>();
            Set(world, "width", 2);
            Set(world, "height", 1);
            Call(world, "Awake");
            world.Set(0, 0, 10);
            world.Set(1, 0, terrainId);
            var housePrefab = Model("house_0_0");
            var ruin = Model("house_0_0f");
            var terrain = Model(terrainName);
            var view = root.AddComponent<MapView>();
            Set(view, "world", world);
            Set(view, "modules", new[] {
                new MapView.Module { Id = 10, Prefab = housePrefab },
                new MapView.Module { Id = terrainId, Prefab = terrain }
            });
            Set(view, "ruins", new[] { new MapView.Ruin { Intact = housePrefab, Ruined = ruin } });
            Call(view, "Start");
            var house = new Houses.House();
            house.Cells.Add(new Vector2Int(0, 0));
            // Reproduces an obsolete/wrong cell list including a neighbouring hill or mountain.
            house.Cells.Add(new Vector2Int(1, 0));
            Call(view, "OnHouseDestroyed", house);
            Call(view, "LateUpdate");

            var ruined = (HashSet<int>)Get(view, "ruinedCells");
            Assert.That(ruined, Is.EquivalentTo(new[] { 0 }));
            var collapse = (HouseCollapse)Get(view, "collapse");
            int activePieces = 0;
            foreach (var filter in collapse.GetComponentsInChildren<MeshFilter>())
            {
                if (!filter.gameObject.activeSelf) continue;
                activePieces++;
                Assert.That(filter.sharedMesh.name, Does.StartWith("house_0_0 piece"));
            }
            Assert.That(activePieces, Is.GreaterThan(0), "The actual house must still break.");
            var draws = (System.Array)Get(view, "cells");
            Assert.That(draws.GetValue(1).GetType().GetField("Prefab").GetValue(draws.GetValue(1)), Is.SameAs(terrain));
            Call(view, "OnHouseRepaired", house);
            Assert.That(ruined, Is.Empty);
        }

        [Test]
        public void Break_RejectsUnregisteredLandscapeMesh()
        {
            var effects = new GameObject("House effects");
            effects.transform.SetParent(root.transform, false);
            var collapse = effects.AddComponent<HouseCollapse>();
            var house = Model("house_0_0").GetComponent<MeshFilter>().sharedMesh;
            var terrain = Model("mountain").GetComponent<MeshFilter>().sharedMesh;
            collapse.Init(null, new[] { house });
            collapse.Break(terrain, Matrix4x4.identity, Vector3.zero, Vector3.forward);
            Assert.That(collapse.GetComponentsInChildren<MeshFilter>(), Is.Empty);
            collapse.Break(house, Matrix4x4.identity, Vector3.zero, Vector3.forward);
            Assert.That(collapse.GetComponentsInChildren<MeshFilter>(), Is.Not.Empty);
        }

        GameObject Model(string name)
        {
            var model = GameObject.CreatePrimitive(PrimitiveType.Cube);
            model.name = name;
            model.transform.SetParent(root.transform, false);
            var mesh = Object.Instantiate(model.GetComponent<MeshFilter>().sharedMesh);
            mesh.name = name;
            meshes.Add(mesh);
            model.GetComponent<MeshFilter>().sharedMesh = mesh;
            return model;
        }

        static object Get(object target, string field) => target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);
        static void Set(object target, string field, object value) => target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
        static void Call(object target, string method, params object[] args) => target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, args);
    }
}
