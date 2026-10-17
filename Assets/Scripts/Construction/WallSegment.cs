using UnityEngine;

namespace IdleTown.Construction
{
    /// <summary>Stable metadata for future wave queries; it intentionally carries no health simulation.</summary>
    public sealed class WallSegment : MonoBehaviour
    {
        [SerializeField] Vector2Int nodeA;
        [SerializeField] Vector2Int nodeB;
        [SerializeField, Min(0)] int floor;
        [SerializeField] WallTier tier;

        public WallEdge Edge => new(nodeA, nodeB);
        public int Floor => floor;
        public WallTier Tier => tier;

        internal void Configure(WallRecord record)
        {
            nodeA = record.Edge.A;
            nodeB = record.Edge.B;
            floor = record.Floor;
            tier = record.Tier;
        }
    }
}
