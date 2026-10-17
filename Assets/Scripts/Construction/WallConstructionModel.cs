using System;
using System.Collections.Generic;
using UnityEngine;

namespace IdleTown.Construction
{
    /// <summary>An undirected edge between two neighbouring grid nodes.</summary>
    public readonly struct WallEdge : IEquatable<WallEdge>
    {
        public readonly Vector2Int A;
        public readonly Vector2Int B;

        public WallEdge(Vector2Int a, Vector2Int b)
        {
            A = a;
            B = b;
        }

        public bool IsNeighbour => Mathf.Max(Mathf.Abs(B.x - A.x), Mathf.Abs(B.y - A.y)) == 1;
        public bool IsDiagonal => Mathf.Abs(B.x - A.x) == 1 && Mathf.Abs(B.y - A.y) == 1;

        public WallEdge CrossingDiagonal()
        {
            return new WallEdge(new Vector2Int(A.x, B.y), new Vector2Int(B.x, A.y));
        }

        public bool Equals(WallEdge other)
        {
            return (A == other.A && B == other.B) || (A == other.B && B == other.A);
        }

        public override bool Equals(object obj) => obj is WallEdge other && Equals(other);
        public override int GetHashCode() => A.GetHashCode() ^ B.GetHashCode();
        public override string ToString() => $"{A} -> {B}";
    }

    public readonly struct WallRecord
    {
        public WallRecord(WallEdge edge, int floor, WallTier tier)
        {
            Edge = edge;
            Floor = floor;
            Tier = tier;
        }

        public WallEdge Edge { get; }
        public int Floor { get; }
        public WallTier Tier { get; }
    }

    readonly struct WallKey : IEquatable<WallKey>
    {
        public WallKey(WallEdge edge, int floor)
        {
            Edge = edge;
            Floor = floor;
        }

        public WallEdge Edge { get; }
        public int Floor { get; }
        public bool Equals(WallKey other) => Floor == other.Floor && Edge.Equals(other.Edge);
        public override bool Equals(object obj) => obj is WallKey other && Equals(other);
        public override int GetHashCode() => (Edge.GetHashCode() * 397) ^ Floor;
    }

    /// <summary>Pure grid rules shared by preview, construction and edit-mode tests.</summary>
    public sealed class WallConstructionModel
    {
        readonly HashSet<WallEdge> floorZeroEdges = new();
        readonly Dictionary<WallKey, WallRecord> walls = new();

        /// <summary>Legacy floor-zero edge view.</summary>
        public IReadOnlyCollection<WallEdge> Edges => floorZeroEdges;
        public IReadOnlyCollection<WallRecord> Walls => walls.Values;

        public static List<WallEdge> CreateSnappedPath(Vector2Int start, Vector2Int end)
        {
            var result = new List<WallEdge>();
            var current = start;
            var dx = Mathf.Abs(end.x - start.x);
            var dy = Mathf.Abs(end.y - start.y);
            var stepX = start.x < end.x ? 1 : -1;
            var stepY = start.y < end.y ? 1 : -1;
            var error = dx - dy;

            while (current != end)
            {
                var next = current;
                var doubledError = error * 2;
                if (doubledError > -dy)
                {
                    error -= dy;
                    next.x += stepX;
                }

                if (doubledError < dx)
                {
                    error += dx;
                    next.y += stepY;
                }

                result.Add(new WallEdge(current, next));
                current = next;
            }

            return result;
        }

        public bool Validate(IReadOnlyList<WallEdge> path, int width, int height, out string reason)
        {
            return Validate(path, width, height, 0, WallTier.Timber, 1, out reason);
        }

        public bool Validate(
            IReadOnlyList<WallEdge> path,
            int width,
            int height,
            int floor,
            WallTier tier,
            int maxFloors,
            out string reason)
        {
            if (path == null || path.Count == 0)
            {
                reason = "Başlangıçtan farklı bir bitiş düğümü seçin.";
                return false;
            }

            if (floor < 0 || floor >= maxFloors)
            {
                reason = $"Kat 1 ile {Math.Max(1, maxFloors)} arasında olmalı.";
                return false;
            }

            if (!WallTierRules.IsTier(tier))
            {
                reason = "Geçersiz duvar seviyesi.";
                return false;
            }

            var pending = new HashSet<WallEdge>();
            for (var i = 0; i < path.Count; i++)
            {
                var edge = path[i];
                if (!InBounds(edge.A, width, height) || !InBounds(edge.B, width, height))
                {
                    reason = "Duvar harita sınırlarının dışında kalıyor.";
                    return false;
                }

                if (!edge.IsNeighbour)
                {
                    reason = "Duvar parçaları yalnızca komşu düğümleri bağlayabilir.";
                    return false;
                }

                if (!pending.Add(edge))
                {
                    reason = "Aynı yol içinde bir kenar tekrarlanamaz.";
                    return false;
                }

                if (TryGet(edge, floor, out var existing))
                {
                    if (existing.Tier == tier)
                    {
                        reason = "Bu kenar ve katta aynı seviyede duvar zaten var.";
                        return false;
                    }
                    if (existing.Tier > tier)
                    {
                        reason = "Duvar daha düşük bir seviyeye indirilemez.";
                        return false;
                    }
                }

                if (floor > 0 &&
                    (!TryGet(edge, floor - 1, out var support) ||
                     !WallTierRules.HasSufficientSupport(support.Tier, tier)))
                {
                    reason = "Üst kat için aynı kenarda en az aynı seviyede alt kat desteği gerekir.";
                    return false;
                }

                if (edge.IsDiagonal)
                {
                    var crossing = edge.CrossingDiagonal();
                    if (walls.ContainsKey(new WallKey(crossing, floor)) || pending.Contains(crossing))
                    {
                        reason = "Çapraz duvarlar aynı hücrenin içinde kesişemez.";
                        return false;
                    }
                }
            }

            reason = string.Empty;
            return true;
        }

        public void Add(IReadOnlyList<WallEdge> path)
        {
            Set(path, 0, WallTier.Timber);
        }

        public void Set(IReadOnlyList<WallEdge> path, int floor, WallTier tier)
        {
            for (var i = 0; i < path.Count; i++)
            {
                var record = new WallRecord(path[i], floor, tier);
                walls[new WallKey(path[i], floor)] = record;
                if (floor == 0)
                    floorZeroEdges.Add(path[i]);
            }
        }

        public bool TryGet(WallEdge edge, int floor, out WallRecord record)
        {
            return walls.TryGetValue(new WallKey(edge, floor), out record);
        }

        static bool InBounds(Vector2Int node, int width, int height)
        {
            // Walls live on cell corners, so the final row/column is inclusive.
            return node.x >= 0 && node.y >= 0 && node.x <= width && node.y <= height;
        }
    }
}
