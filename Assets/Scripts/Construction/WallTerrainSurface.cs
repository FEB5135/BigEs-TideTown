using UnityEngine;
using System.Collections.Generic;

namespace IdleTown.Construction
{
    // Only explicitly registered landscape colliders count as ground, never roofs or walls.
    public sealed class WallTerrainSurface : MonoBehaviour
    {
        [SerializeField] Collider[] surfaces = System.Array.Empty<Collider>();
        MapView map;
        public void Configure(Collider[] colliders) => surfaces = colliders;
        public void ConfigureMap(MapView view)
        {
            map = view;
            var landscape = new List<Collider>(surfaces);
            // Older main scenes also have manually placed ground/mountain meshes outside WFC.
            // Register only these landscape groups, never arbitrary scene meshes such as roofs.
            foreach (var filter in FindObjectsByType<MeshFilter>(FindObjectsSortMode.None))
            {
                if (filter.gameObject.scene != view.gameObject.scene || filter.sharedMesh == null) continue;
                bool isLandscape = false;
                for (var parent = filter.transform; parent != null; parent = parent.parent)
                    if (parent.name == "Ground" || parent.name == "Mountains") { isLandscape = true; break; }
                if (!isLandscape) continue;
                var collider = filter.GetComponent<MeshCollider>();
                if (collider == null) collider = filter.gameObject.AddComponent<MeshCollider>();
                collider.sharedMesh = filter.sharedMesh;
                if (!landscape.Contains(collider)) landscape.Add(collider);
            }
            foreach (var terrain in FindObjectsByType<TerrainCollider>(FindObjectsSortMode.None))
                if (terrain.gameObject.scene == view.gameObject.scene && !landscape.Contains(terrain)) landscape.Add(terrain);
            surfaces = landscape.ToArray();
        }
        IEnumerable<Collider> Surfaces
        {
            get
            {
                foreach (var surface in surfaces) yield return surface;
                if (map != null) foreach (var surface in map.ConstructionSurfaces) yield return surface;
            }
        }
        public bool Contains(Collider collider)
        {
            foreach (var surface in Surfaces) if (surface == collider) return true;
            return false;
        }

        public bool Raycast(Ray ray, out RaycastHit nearest)
        {
            nearest = default;
            float distance = float.PositiveInfinity;
            foreach (var collider in Surfaces)
                if (collider != null && collider.enabled && collider.gameObject.activeInHierarchy &&
                    collider.Raycast(ray, out var hit, float.MaxValue) && hit.distance < distance)
                { nearest = hit; distance = hit.distance; }
            return !float.IsPositiveInfinity(distance);
        }

        public bool Sample(Vector3 position, out Vector3 point)
        {
            float top = position.y;
            foreach (var collider in Surfaces)
                if (collider != null) top = Mathf.Max(top, collider.bounds.max.y);
            bool found = Raycast(new Ray(new Vector3(position.x, top + 10f, position.z), Vector3.down), out var hit);
            point = found ? hit.point : position;
            return found;
        }

        // Sample for many points at once (the construction grid). The surfaces, their highest point and their layers
        // are gathered once and each point is one physics query, instead of a raycast against every surface.
        public void SampleMany(Vector3[] positions, Vector3[] points, bool[] found)
        {
            var set = new HashSet<Collider>();
            float top = float.NegativeInfinity;
            int mask = 0;
            foreach (var collider in Surfaces)
            {
                if (collider == null) continue;
                top = Mathf.Max(top, collider.bounds.max.y);
                if (!collider.enabled || !collider.gameObject.activeInHierarchy) continue;
                set.Add(collider);
                mask |= 1 << collider.gameObject.layer;
            }
            var hits = new RaycastHit[32];
            for (int i = 0; i < positions.Length; i++)
            {
                var position = positions[i];
                var ray = new Ray(new Vector3(position.x, Mathf.Max(position.y, top) + 10f, position.z), Vector3.down);
                int count = set.Count > 0 ? Physics.RaycastNonAlloc(ray, hits, float.PositiveInfinity, mask, QueryTriggerInteraction.Collide) : 0;
                float distance = float.PositiveInfinity;
                points[i] = position;
                for (int h = 0; h < count; h++)
                    if (hits[h].distance < distance && set.Contains(hits[h].collider))
                    {
                        distance = hits[h].distance;
                        points[i] = hits[h].point;
                    }
                found[i] = !float.IsPositiveInfinity(distance);
            }
        }
    }
}
