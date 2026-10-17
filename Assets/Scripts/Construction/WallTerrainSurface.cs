using UnityEngine;

namespace IdleTown.Construction
{
    // Only explicitly registered landscape colliders count as ground, never roofs or walls.
    public sealed class WallTerrainSurface : MonoBehaviour
    {
        [SerializeField] Collider[] surfaces = System.Array.Empty<Collider>();
        public void Configure(Collider[] colliders) => surfaces = colliders;
        public bool Contains(Collider collider) => System.Array.IndexOf(surfaces, collider) >= 0;

        public bool Raycast(Ray ray, out RaycastHit nearest)
        {
            nearest = default;
            float distance = float.PositiveInfinity;
            foreach (var collider in surfaces)
                if (collider != null && collider.enabled && collider.gameObject.activeInHierarchy &&
                    collider.Raycast(ray, out var hit, float.MaxValue) && hit.distance < distance)
                { nearest = hit; distance = hit.distance; }
            return !float.IsPositiveInfinity(distance);
        }

        public bool Sample(Vector3 position, out Vector3 point)
        {
            float top = position.y;
            foreach (var collider in surfaces)
                if (collider != null) top = Mathf.Max(top, collider.bounds.max.y);
            bool found = Raycast(new Ray(new Vector3(position.x, top + 10f, position.z), Vector3.down), out var hit);
            point = found ? hit.point : position;
            return found;
        }
    }
}
