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
    /// Also wears down the generated map's houses (Houses, WFC tiles 10-13): every house loses health from deep
    /// standing water and, much faster, from fast flowing water (depth x speed^2, the flow's momentum flux).
    /// Damage accrues on simulation time, so a paused tsunami does no harm. Visuals listen to the Houses events.
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

        [Header("Houses (generated map)")]
        [Tooltip("Water shallower than this (m) does not hurt a house.")]
        [SerializeField, Min(0f)] float houseSafeDepth = 0.3f;
        [Tooltip("Health lost per simulated second for each metre of standing water above the safe depth (1 = whole house).")]
        [SerializeField, Min(0f)] float floodDamage = 0.06f;
        [Tooltip("Health lost per simulated second per unit of depth x speed^2 (m^3/s^2). 3 m of water at 4 m/s flattens a house in about half a second.")]
        [SerializeField, Min(0f)] float flowDamage = 0.04f;

        [Header("Feedback")]
        [SerializeField] Material floodedMaterial;
        [SerializeField] Material destroyedMaterial;
        [Tooltip("Optional status label.")]
        [SerializeField] Text statusText;

        float nextCheck;
        double lastSimTime;
        int labelledHouses;

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
            DamageHouses();
            if (changed) Recount();
        }

        void DamageHouses()
        {
            // The house list is built on first use, after the map has been generated.
            bool changed = Houses.All.Count != labelledHouses;
            double now = simulation.SimTime;
            float dt = (float)(now - lastSimTime);
            lastSimTime = now;
            if (dt <= 0f) // paused, or the water was just reset
            {
                if (changed) Recount();
                return;
            }
            foreach (Houses.House house in Houses.All)
            {
                if (house.IsDestroyed) continue;
                float rate = 0f;
                if (house.CellCenters.Count == 0) rate = HouseDamageRate(house.Center);
                foreach (Vector3 cell in house.CellCenters) rate = Mathf.Max(rate, HouseDamageRate(cell));
                if (rate <= 0f) continue;
                var stage = house.Stage;
                house.Damage(rate * dt);
                changed |= house.Stage != stage;
            }
            if (changed) Recount();
        }

        // Health lost per simulated second by a house cell at p.
        float HouseDamageRate(Vector3 p)
        {
            float depth = simulation.SampleDepth(p);
            if (depth <= houseSafeDepth) return 0f;
            float speed = simulation.Terrain.SampleField(simulation.Speed, p);
            return (depth - houseSafeDepth) * floodDamage + depth * speed * speed * flowDamage;
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
            if (simulation != null) lastSimTime = simulation.SimTime;
            Houses.RepairAll();
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
            labelledHouses = Houses.Built.Count;
            if (statusText != null)
                statusText.text = labelledHouses > 0
                    ? $"Evler: {Houses.CountIn(Houses.DamageStage.Intact)} sağlam   " +
                      $"{Houses.CountIn(Houses.DamageStage.Damaged) + Houses.CountIn(Houses.DamageStage.HeavilyDamaged)} hasarlı   " +
                      $"{Houses.CountIn(Houses.DamageStage.Destroyed)} yıkık   (toplam {labelledHouses})"
                    : $"Village: {safe} safe   {flooded} flooded   {destroyed} destroyed   (of {safe + flooded + destroyed})";
            Changed?.Invoke();
        }
    }
}
