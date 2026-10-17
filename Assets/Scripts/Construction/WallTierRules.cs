using System;

namespace IdleTown.Construction
{
    public enum WallTier
    {
        Timber = 0,
        Stone = 1,
        Reinforced = 2
    }

    /// <summary>Cumulative resource cost of one edge at a tier.</summary>
    public readonly struct WallCost : IEquatable<WallCost>
    {
        public WallCost(int wood, int stone)
        {
            Wood = wood;
            Stone = stone;
        }

        public int Wood { get; }
        public int Stone { get; }

        public bool Equals(WallCost other) => Wood == other.Wood && Stone == other.Stone;
        public override bool Equals(object obj) => obj is WallCost other && Equals(other);
        public override int GetHashCode() => (Wood * 397) ^ Stone;
    }

    /// <summary>Unity-independent tier, support and cumulative-cost rules.</summary>
    public static class WallTierRules
    {
        public static bool IsTier(WallTier tier) => tier >= WallTier.Timber && tier <= WallTier.Reinforced;

        public static bool HasSufficientSupport(WallTier support, WallTier upper)
        {
            return IsTier(support) && IsTier(upper) && support >= upper;
        }

        public static bool AreCumulativeCostsValid(WallCost timber, WallCost stone, WallCost reinforced)
        {
            return IsNonNegative(timber) && IsNonNegative(stone) && IsNonNegative(reinforced) &&
                   IsAtLeast(stone, timber) && IsAtLeast(reinforced, stone);
        }

        public static bool TryGetBuildCost(
            WallTier? existingTier,
            WallTier targetTier,
            WallCost timber,
            WallCost stone,
            WallCost reinforced,
            out WallCost cost)
        {
            cost = default;
            if (!IsTier(targetTier) || !AreCumulativeCostsValid(timber, stone, reinforced))
                return false;
            if (existingTier.HasValue && (!IsTier(existingTier.Value) || existingTier.Value >= targetTier))
                return false;

            var target = CostFor(targetTier, timber, stone, reinforced);
            if (!existingTier.HasValue)
            {
                cost = target;
                return true;
            }

            var existing = CostFor(existingTier.Value, timber, stone, reinforced);
            cost = new WallCost(target.Wood - existing.Wood, target.Stone - existing.Stone);
            return true;
        }

        static WallCost CostFor(WallTier tier, WallCost timber, WallCost stone, WallCost reinforced)
        {
            return tier == WallTier.Timber ? timber : tier == WallTier.Stone ? stone : reinforced;
        }

        static bool IsNonNegative(WallCost cost) => cost.Wood >= 0 && cost.Stone >= 0;
        static bool IsAtLeast(WallCost value, WallCost minimum)
        {
            return value.Wood >= minimum.Wood && value.Stone >= minimum.Stone;
        }
    }
}
