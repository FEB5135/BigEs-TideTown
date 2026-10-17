using System;
using UnityEngine;
using UnityEngine.UI;

namespace Tsunami
{
    public enum BuildingStatus
    {
        Safe,
        Flooded,
        Destroyed
    }

    /// <summary>
    /// Samples simulated water depth around each building at a fixed interval (no physics queries).
    /// Damage is latched: once flooded/destroyed, a building stays that way until ResetVillage().
    /// </summary>
    public class VillageFloodTracker : MonoBehaviour
    {
        [Serializable]
        public class Building
        {
            public Transform transform;
            [Tooltip("Depth is sampled at the centre and four points on this radius (m).")]
            public float footprintRadius = 1.5f;

            [NonSerialized] public BuildingStatus status;
            [NonSerialized] public float currentDepth;
            [NonSerialized] public Renderer[] renderers;
            [NonSerialized] public Material[] originalMaterials;
            [NonSerialized] public Vector3 originalPosition;
            [NonSerialized] public Quaternion originalRotation;
            [NonSerialized] public float top; // world height of the building's highest point at rest
        }

        [SerializeField] WaterSimulation simulation;
        [SerializeField] Building[] buildings = Array.Empty<Building>();

        [Header("Thresholds")]
        [Tooltip("Seconds between flood checks.")]
        [SerializeField, Range(0.05f, 2f)] float checkInterval = 0.25f;
        [Tooltip("Water depth (m) at which a building counts as flooded.")]
        [SerializeField, Min(0.01f)] float floodDepth = 0.25f;
        [Tooltip("Water depth (m) at which a building is destroyed.")]
        [SerializeField, Min(0.01f)] float destroyDepth = 1.2f;

        [Header("Feedback")]
        [SerializeField] Material floodedMaterial;
        [SerializeField] Material destroyedMaterial;
        [Tooltip("Optional status label.")]
        [SerializeField] Text statusText;

        float nextCheck;

        public int TotalCount => buildings.Length;

        /// <summary>Resting position, footprint radius and roof height of building i (for splash effects).</summary>
        public bool GetBuilding(int i, out Vector3 position, out float radius, out float top)
        {
            position = default; radius = 0f; top = 0f;
            if (i < 0 || i >= buildings.Length || buildings[i].transform == null || buildings[i].renderers == null) return false;
            position = buildings[i].originalPosition;
            radius = buildings[i].footprintRadius;
            top = buildings[i].top;
            return true;
        }
        public int SafeCount { get; private set; }
        public int FloodedCount { get; private set; }
        public int DestroyedCount { get; private set; }
        public event Action Changed;

        void Start()
        {
            for (int i = 0; i < buildings.Length; i++)
            {
                Building b = buildings[i];
                if (b.transform == null) continue;
                b.renderers = b.transform.GetComponentsInChildren<Renderer>();
                b.originalMaterials = new Material[b.renderers.Length];
                for (int r = 0; r < b.renderers.Length; r++) b.originalMaterials[r] = b.renderers[r].sharedMaterial;
                b.originalPosition = b.transform.position;
                b.top = b.originalPosition.y;
                foreach (Renderer r in b.renderers) b.top = Mathf.Max(b.top, r.bounds.max.y);
                b.originalRotation = b.transform.rotation;
            }
            ResetVillage();
        }

        void Update()
        {
            if (simulation == null || Time.time < nextCheck) return;
            nextCheck = Time.time + checkInterval;
            bool changed = false;
            for (int i = 0; i < buildings.Length; i++)
            {
                Building b = buildings[i];
                if (b.transform == null) continue;
                b.currentDepth = SampleFootprint(b.originalPosition, b.footprintRadius);
                BuildingStatus next = b.status;
                if (b.currentDepth >= destroyDepth) next = BuildingStatus.Destroyed;
                else if (b.currentDepth >= floodDepth && b.status == BuildingStatus.Safe) next = BuildingStatus.Flooded;
                if (next != b.status)
                {
                    b.status = next;
                    ApplyVisual(b, i);
                    changed = true;
                }
            }
            if (changed) Recount();
        }

        float SampleFootprint(Vector3 p, float r)
        {
            float d = simulation.SampleDepth(p);
            d = Mathf.Max(d, simulation.SampleDepth(p + new Vector3(r, 0, 0)));
            d = Mathf.Max(d, simulation.SampleDepth(p + new Vector3(-r, 0, 0)));
            d = Mathf.Max(d, simulation.SampleDepth(p + new Vector3(0, 0, r)));
            return Mathf.Max(d, simulation.SampleDepth(p + new Vector3(0, 0, -r)));
        }

        void ApplyVisual(Building b, int index)
        {
            Material m = b.status == BuildingStatus.Destroyed ? destroyedMaterial
                       : b.status == BuildingStatus.Flooded ? floodedMaterial : null;
            for (int r = 0; r < b.renderers.Length; r++)
                b.renderers[r].sharedMaterial = m != null ? m : b.originalMaterials[r];

            if (b.status == BuildingStatus.Destroyed)
            {
                // Knocked over and half sunk; deterministic per building so it doesn't jitter.
                float side = (index & 1) == 0 ? 1f : -1f;
                b.transform.SetPositionAndRotation(b.originalPosition + Vector3.down * 0.8f,
                    b.originalRotation * Quaternion.Euler(18f * side, 0f, 24f * side));
            }
            else
            {
                b.transform.SetPositionAndRotation(b.originalPosition, b.originalRotation);
            }
        }

        public void ResetVillage()
        {
            for (int i = 0; i < buildings.Length; i++)
            {
                Building b = buildings[i];
                if (b.transform == null || b.renderers == null) continue;
                b.status = BuildingStatus.Safe;
                b.currentDepth = 0f;
                ApplyVisual(b, i);
            }
            nextCheck = 0f;
            Recount();
        }

        void Recount()
        {
            int safe = 0, flooded = 0, destroyed = 0;
            for (int i = 0; i < buildings.Length; i++)
            {
                if (buildings[i].transform == null) continue;
                switch (buildings[i].status)
                {
                    case BuildingStatus.Safe: safe++; break;
                    case BuildingStatus.Flooded: flooded++; break;
                    default: destroyed++; break;
                }
            }
            SafeCount = safe;
            FloodedCount = flooded;
            DestroyedCount = destroyed;
            if (statusText != null)
                statusText.text = $"Village: {safe} safe   {flooded} flooded   {destroyed} destroyed   (of {safe + flooded + destroyed})";
            Changed?.Invoke();
        }
    }
}
