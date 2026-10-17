using System;
using IdleTown.Construction;

internal static class WallTierRulesTests
{
    static int Main()
    {
        Run("Cumulative costs must be nonnegative and monotonic", CumulativeCostsMustBeMonotonic);
        Run("New walls cost the target cumulative amount", NewWallCostsTargetAmount);
        Run("Upgrades cost only the cumulative delta", UpgradeCostsOnlyDelta);
        Run("Same-tier builds and downgrades are rejected", RejectsNonUpgrades);
        Run("Upper tiers require equal or stronger support", SupportUsesTierStrength);
        Console.WriteLine("5 WallTierRules tests passed.");
        return 0;
    }

    static void CumulativeCostsMustBeMonotonic()
    {
        Assert(WallTierRules.AreCumulativeCostsValid(Cost(1, 1), Cost(3, 4), Cost(6, 6)));
        Assert(!WallTierRules.AreCumulativeCostsValid(Cost(-1, 0), Cost(3, 4), Cost(6, 6)));
        Assert(!WallTierRules.AreCumulativeCostsValid(Cost(1, 1), Cost(0, 4), Cost(6, 6)));
        Assert(!WallTierRules.AreCumulativeCostsValid(Cost(1, 1), Cost(3, 4), Cost(6, 3)));
    }

    static void NewWallCostsTargetAmount()
    {
        Assert(WallTierRules.TryGetBuildCost(null, WallTier.Stone, Cost(1, 1), Cost(3, 4), Cost(6, 6), out var cost));
        Assert(cost.Equals(Cost(3, 4)));
    }

    static void UpgradeCostsOnlyDelta()
    {
        Assert(WallTierRules.TryGetBuildCost(WallTier.Timber, WallTier.Stone, Cost(1, 1), Cost(3, 4), Cost(6, 6), out var first));
        Assert(first.Equals(Cost(2, 3)));
        Assert(WallTierRules.TryGetBuildCost(WallTier.Stone, WallTier.Reinforced, Cost(1, 1), Cost(3, 4), Cost(6, 6), out var second));
        Assert(second.Equals(Cost(3, 2)));
    }

    static void RejectsNonUpgrades()
    {
        Assert(!WallTierRules.TryGetBuildCost(WallTier.Stone, WallTier.Stone, Cost(1, 1), Cost(3, 4), Cost(6, 6), out _));
        Assert(!WallTierRules.TryGetBuildCost(WallTier.Reinforced, WallTier.Timber, Cost(1, 1), Cost(3, 4), Cost(6, 6), out _));
        Assert(!WallTierRules.TryGetBuildCost(null, (WallTier)99, Cost(1, 1), Cost(3, 4), Cost(6, 6), out _));
    }

    static void SupportUsesTierStrength()
    {
        Assert(WallTierRules.HasSufficientSupport(WallTier.Reinforced, WallTier.Stone));
        Assert(WallTierRules.HasSufficientSupport(WallTier.Stone, WallTier.Stone));
        Assert(!WallTierRules.HasSufficientSupport(WallTier.Timber, WallTier.Stone));
    }

    static WallCost Cost(int wood, int stone) => new(wood, stone);

    static void Run(string name, Action test)
    {
        try
        {
            test();
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException($"Failed: {name}", exception);
        }
    }

    static void Assert(bool condition)
    {
        if (!condition)
            throw new InvalidOperationException("Assertion failed.");
    }
}
