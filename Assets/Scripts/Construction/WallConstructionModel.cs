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

    /// <summary>Pure grid rules shared by preview, construction and edit-mode tests.</summary>
    public sealed class WallConstructionModel
    {
        readonly HashSet<WallEdge> edges = new();

        public IReadOnlyCollection<WallEdge> Edges => edges;

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
            if (path == null || path.Count == 0)
            {
                reason = "Başlangıçtan farklı bir bitiş düğümü seçin.";
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

                if (edges.Contains(edge) || !pending.Add(edge))
                {
                    reason = "Bu kenarda zaten bir duvar var.";
                    return false;
                }

                if (edge.IsDiagonal)
                {
                    var crossing = edge.CrossingDiagonal();
                    if (edges.Contains(crossing) || pending.Contains(crossing))
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
            for (var i = 0; i < path.Count; i++)
                edges.Add(path[i]);
        }

        /// <summary>Removes exactly one undirected edge. Used when a physical segment is destroyed.</summary>
        public bool Remove(WallEdge edge) => edges.Remove(edge);

        static bool InBounds(Vector2Int node, int width, int height)
        {
            // Walls live on cell corners, so the final row/column is inclusive.
            return node.x >= 0 && node.y >= 0 && node.x <= width && node.y <= height;
        }
    }
}
