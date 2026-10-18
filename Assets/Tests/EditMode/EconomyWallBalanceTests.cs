using System.Collections.Generic;
using System.Reflection;
using IdleTown.Construction;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace IdleTown.Tests
{
    public sealed class EconomyWallBalanceTests
    {
        GameObject root;
        readonly List<Collider> colliders = new();
        readonly List<GameObject> carts = new();
        [SetUp] public void SetUp()
        {
            foreach (var collider in Object.FindObjectsByType<Collider>(FindObjectsSortMode.None))
                if (collider.enabled) { colliders.Add(collider); collider.enabled = false; }
            root = new GameObject("Economy balance tests");
        }
        [TearDown] public void TearDown()
        {
            foreach (var cart in carts) if (cart != null) Object.DestroyImmediate(cart);
            carts.Clear(); Object.DestroyImmediate(root);
            foreach (var collider in colliders) if (collider != null) collider.enabled = true;
            colliders.Clear();
        }
        static void Invoke(object target, string method) => target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, null);

        [TestCase(WallTier.Timber, 1, 0)]
        [TestCase(WallTier.Stone, 0, 4)]
        [TestCase(WallTier.Reinforced, 6, 6)]
        public void FreshWallConsumesOnlyItsMaterials(WallTier tier, int wood, int stone)
        {
            var world = root.AddComponent<World>();
            var well = root.AddComponent<Well>();
            well.Deposit(ResourceType.Wood, wood); well.Deposit(ResourceType.Stone, stone);
            var controller = root.AddComponent<WallConstructionController>(); controller.Configure(world, well);
            Assert.That(controller.TryBuild(Vector2Int.zero, Vector2Int.right, 0, tier, out var reason), Is.True, reason);
            Assert.That(well.Wood, Is.Zero); Assert.That(well.Stone, Is.Zero);
        }

        [Test] public void ChangingWallMaterialsNeverRefundsResources()
        {
            var world = root.AddComponent<World>(); var well = root.AddComponent<Well>();
            well.Deposit(ResourceType.Wood, 20); well.Deposit(ResourceType.Stone, 20);
            var controller = root.AddComponent<WallConstructionController>(); controller.Configure(world, well);
            Assert.That(controller.TryBuild(Vector2Int.zero,Vector2Int.right,0,WallTier.Timber,out _), Is.True);
            Assert.That(controller.TryBuild(Vector2Int.zero,Vector2Int.right,0,WallTier.Stone,out var reason), Is.True, reason);
            Assert.That(well.Wood, Is.EqualTo(19)); Assert.That(well.Stone, Is.EqualTo(16));
            Assert.That(controller.TryBuild(Vector2Int.zero,Vector2Int.right,0,WallTier.Reinforced,out reason), Is.True, reason);
            Assert.That(well.Wood, Is.EqualTo(13)); Assert.That(well.Stone, Is.EqualTo(14));
        }

        [Test] public void MineUpgradesAddIndependentCartsAndEnforceTwoFourFiveWorkers()
        {
            var mine = new GameObject("Mine"); mine.transform.SetParent(root.transform);
            var node = mine.AddComponent<ResourceNode>();
            var settings = new SerializedObject(node); settings.FindProperty("type").enumValueIndex = (int)ResourceType.Stone;
            settings.ApplyModifiedPropertiesWithoutUndo(); Invoke(node,"Awake");
            var upgrade = mine.AddComponent<UpgradeLevel>();
            var template = new GameObject("Cart template"); template.transform.SetParent(root.transform);
            var cartPrefab = template.AddComponent<Gatherer>();
            var spawner = mine.AddComponent<GathererSpawner>();
            settings = new SerializedObject(spawner); settings.FindProperty("prefab").objectReferenceValue = cartPrefab;
            settings.ApplyModifiedPropertiesWithoutUndo();
            var well = root.AddComponent<Well>(); well.Deposit(ResourceType.Wood,1000); well.Deposit(ResourceType.Stone,1000);
            Invoke(spawner,"Start");
            float baseInterval = spawner.Spawned[0].HitInterval;
            for (int level=1; level<=3; level++)
            {
                int capacity = level == 1 ? 2 : level == 2 ? 4 : 5;
                Assert.That(node.WorkerCapacity, Is.EqualTo(capacity));
                Assert.That(spawner.Spawned.Count, Is.EqualTo(capacity));
                foreach (var cart in spawner.Spawned)
                {
                    if (!carts.Contains(cart.gameObject)) carts.Add(cart.gameObject);
                    Assert.That(cart.NeedsPusher, Is.True);
                    Assert.That(node.TryClaim(cart), Is.True);
                    Assert.That(node.TryClaim(cart), Is.True, "Claiming twice must not consume a second slot.");
                }
                Assert.That(node.ActiveGatherers, Is.EqualTo(capacity));
                Assert.That(node.TryClaim(cartPrefab), Is.False, "An extra worker must wait for a free slot.");
                if (level == 3)
                {
                    Assert.That(upgrade.WorkTimeMultiplier, Is.EqualTo(.3f).Within(.001f));
                    Assert.That(spawner.Spawned[0].HitInterval, Is.EqualTo(baseInterval * .3f).Within(.001f), "The cart must use its own mine's speed bonus.");
                }
                foreach (var cart in spawner.Spawned) node.Release(cart);
                Assert.That(node.IsAvailable, Is.True);
                if (level < 3) Assert.That(upgrade.TryUpgrade(well), Is.True);
            }
        }
    }
}
