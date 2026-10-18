using System;
using System.Reflection;
using System.Text.RegularExpressions;
using IdleTown.Construction;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace IdleTown.Tests
{
    public sealed class WellWallIntegrationTests
    {
        GameObject root;
        Well well;

        [SetUp]
        public void SetUp()
        {
            root = new GameObject("Well integration test");
            well = root.AddComponent<Well>();
            well.Deposit(ResourceType.Wood, 10);
            well.Deposit(ResourceType.Stone, 10);
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(root);
        }

        [TestCase(11, 1)]
        [TestCase(1, 11)]
        public void TrySpend_WhenEitherResourceIsInsufficient_DoesNotMutate(int wood, int stone)
        {
            Assert.That(well.TrySpend(wood, stone), Is.False);
            Assert.That(well.Wood, Is.EqualTo(10));
            Assert.That(well.Stone, Is.EqualTo(10));
        }

        [Test]
        public void TrySpend_WhenBothResourcesAreAvailable_CommitsBoth()
        {
            Assert.That(well.TrySpend(4, 3), Is.True);
            Assert.That(well.Wood, Is.EqualTo(6));
            Assert.That(well.Stone, Is.EqualTo(7));
        }

        [Test]
        public void BalanceAccess_WhenSerializedArrayIsShortAndNegative_NormalizesBeforeUse()
        {
            var serializedWell = new SerializedObject(well);
            var amounts = serializedWell.FindProperty("amounts");
            amounts.arraySize = 1;
            amounts.GetArrayElementAtIndex(0).intValue = -7;
            serializedWell.ApplyModifiedPropertiesWithoutUndo();

            Assert.That(well.Wood, Is.Zero);
            Assert.That(well.Stone, Is.Zero);
            Assert.That(well.CanAfford(0, 0), Is.True);

            well.Deposit(ResourceType.Stone, 3);
            Assert.That(well.Stone, Is.EqualTo(3));
        }

        [Test]
        public void BalanceAccess_WhenSerializedArrayIsNull_NormalizesBeforeUse()
        {
            var amountsField = typeof(Well).GetField("amounts", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(amountsField, Is.Not.Null);
            amountsField.SetValue(well, null);

            Assert.That(well.Get(ResourceType.Wood), Is.Zero);
            Assert.That(well.TrySpend(0, 0), Is.True);
            well.Deposit(ResourceType.Wood, 2);
            Assert.That(well.Wood, Is.EqualTo(2));
            Assert.That(well.Stone, Is.Zero);
        }

        [Test]
        public void TrySpend_WhenObserverThrows_RemainsCommittedAndNotifiesLaterObserver()
        {
            var laterObserverCalls = 0;
            well.Changed += (_, _) => throw new InvalidOperationException("well observer failure");
            well.Changed += (_, _) => laterObserverCalls++;
            LogAssert.Expect(LogType.Exception, new Regex("InvalidOperationException: well observer failure"));
            LogAssert.Expect(LogType.Exception, new Regex("InvalidOperationException: well observer failure"));

            Assert.That(well.TrySpend(2, 3), Is.True);
            Assert.That(well.Wood, Is.EqualTo(8));
            Assert.That(well.Stone, Is.EqualTo(7));
            Assert.That(laterObserverCalls, Is.EqualTo(2));
        }

        [Test]
        public void Construction_WithWell_SpendsOnceAndRejectedBuildLeavesBalancesUnchanged()
        {
            var world = root.AddComponent<World>();
            var controller = root.AddComponent<WallConstructionController>();
            controller.Configure(world, well);

            Assert.That(controller.TryBuild(Vector2Int.zero, new Vector2Int(2, 0), out var reason), Is.True, reason);
            Assert.That(well.Wood, Is.EqualTo(8));
            Assert.That(well.Stone, Is.EqualTo(10));

            var woodAfterBuild = well.Wood;
            var stoneAfterBuild = well.Stone;
            Assert.That(controller.TryBuild(new Vector2Int(2, 0), Vector2Int.zero, out _), Is.False);
            Assert.That(well.Wood, Is.EqualTo(woodAfterBuild));
            Assert.That(well.Stone, Is.EqualTo(stoneAfterBuild));
            Assert.That(controller.Edges, Has.Count.EqualTo(2));
        }

        [TestCase(ResourceType.Wood)]
        [TestCase(ResourceType.Stone)]
        public void Construction_WhenEitherResourceIsInsufficient_DoesNotBuildOrMutate(ResourceType scarceType)
        {
            Assert.That(well.TrySpend(scarceType, 9), Is.True);
            var world = root.AddComponent<World>();
            var controller = root.AddComponent<WallConstructionController>();
            controller.Configure(world, well);
            var tier = scarceType == ResourceType.Stone ? WallTier.Stone : WallTier.Timber;
            var woodBefore = well.Wood;
            var stoneBefore = well.Stone;

            Assert.That(controller.TryBuild(Vector2Int.zero, new Vector2Int(2, 0), 0, tier, out _), Is.False);
            Assert.That(well.Wood, Is.EqualTo(woodBefore));
            Assert.That(well.Stone, Is.EqualTo(stoneBefore));
            Assert.That(controller.Edges, Is.Empty);
        }

        [Test]
        public void Construction_WhenPathIsInvalid_DoesNotBuildOrMutate()
        {
            var world = root.AddComponent<World>();
            var controller = root.AddComponent<WallConstructionController>();
            controller.Configure(world, well);

            Assert.That(controller.TryBuild(Vector2Int.zero, Vector2Int.zero, out _), Is.False);
            Assert.That(well.Wood, Is.EqualTo(10));
            Assert.That(well.Stone, Is.EqualTo(10));
            Assert.That(controller.Edges, Is.Empty);
        }

        [Test]
        public void Construction_WhenWellObserverThrows_RemainsBuiltAndCommitted()
        {
            var laterObserverCalls = 0;
            well.Changed += (_, _) => throw new InvalidOperationException("construction observer failure");
            well.Changed += (_, _) => laterObserverCalls++;
            var world = root.AddComponent<World>();
            var controller = root.AddComponent<WallConstructionController>();
            controller.Configure(world, well);
            LogAssert.Expect(LogType.Exception, new Regex("InvalidOperationException: construction observer failure"));

            Assert.That(controller.TryBuild(Vector2Int.zero, Vector2Int.right, out var reason), Is.True, reason);
            Assert.That(well.Wood, Is.EqualTo(9));
            Assert.That(well.Stone, Is.EqualTo(10));
            Assert.That(controller.Edges, Has.Count.EqualTo(1));
            Assert.That(laterObserverCalls, Is.EqualTo(1));
        }
    }
}
