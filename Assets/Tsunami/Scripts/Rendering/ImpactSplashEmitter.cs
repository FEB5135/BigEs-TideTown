using UnityEngine;

namespace Tsunami
{
    /// <summary>
    /// Impact and splash effects where the simulated water hits something taller than itself: mountain sides and
    /// steep banks, player-built walls and village houses. Detection uses the solver state (velocity into the
    /// obstacle, water depth, obstacle height); a stronger impact throws water higher (vertical speed grows with
    /// the impact speed, like the stagnation run-up u^2 / 2g) and adds a mist burst. Visual only.
    /// Two pooled particle systems (droplets + mist), a capped number of particles per frame, no allocations.
    /// </summary>
    [RequireComponent(typeof(ParticleSystem))]
    public class ImpactSplashEmitter : MonoBehaviour
    {
        [SerializeField] WaterSimulation simulation;
        [Tooltip("Optional: village houses also make splashes.")]
        [SerializeField] VillageFloodTracker village;

        [Header("When water hits")]
        [Tooltip("Minimum velocity into the obstacle (m/s).")]
        [SerializeField, Range(0.5f, 15f)] float minImpactSpeed = 2.5f;
        [Tooltip("Minimum water depth (m).")]
        [SerializeField, Range(0.05f, 3f)] float minDepth = 0.3f;
        [Tooltip("The obstacle must rise at least this far above the water surface (m).")]
        [SerializeField, Range(0f, 3f)] float clearance = 0.2f;
        [Tooltip("Cells inspected per frame (the scan wraps around the grid).")]
        [SerializeField, Range(512, 65536)] int cellsPerFrame = 8192;
        [Tooltip("Seconds between checks of the village houses.")]
        [SerializeField, Range(0.02f, 1f)] float houseInterval = 0.12f;

        [Header("Amount")]
        [SerializeField, Range(1, 200)] int maxImpactsPerFrame = 24;
        [SerializeField, Range(100, 4000)] int maxDroplets = 900;
        [SerializeField, Range(10, 1000)] int maxMist = 160;

        [Header("Look")]
        [Tooltip("Vertical splash speed per m/s of impact speed.")]
        [SerializeField, Range(0.3f, 3f)] float upPerImpact = 1.1f;
        [SerializeField, Range(2f, 40f)] float maxUpSpeed = 20f;
        [SerializeField] Vector2 dropletSize = new Vector2(0.6f, 2.2f);
        [SerializeField] Vector2 dropletLifetime = new Vector2(0.9f, 2f);
        [SerializeField] Vector2 mistSize = new Vector2(3.5f, 8f);
        [SerializeField] Color dropletColor = new Color(0.95f, 0.98f, 1f, 0.9f);
        [SerializeField] Color mistColor = new Color(0.92f, 0.96f, 1f, 0.32f);

        ParticleSystem droplets, mist;
        int cursor;
        float nextHouseCheck;
        ParticleSystem.EmitParams ep;

        void Awake()
        {
            droplets = GetComponent<ParticleSystem>();
            Configure(droplets, maxDroplets, 1f, 0.6f);
            var mistGO = new GameObject("Mist", typeof(ParticleSystem));
            mistGO.transform.SetParent(transform, false);
            mist = mistGO.GetComponent<ParticleSystem>();
            Configure(mist, maxMist, 0.12f, 1.5f);
            var src = droplets.GetComponent<ParticleSystemRenderer>();
            var dst = mist.GetComponent<ParticleSystemRenderer>();
            dst.sharedMaterial = src.sharedMaterial;
            dst.sortingFudge = 10f;
            ep = new ParticleSystem.EmitParams { applyShapeToPosition = false };
        }

        static void Configure(ParticleSystem ps, int max, float gravity, float drag)
        {
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.playOnAwake = false;
            main.loop = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = gravity;
            main.maxParticles = max;
            main.startSpeed = 0f;
            var emission = ps.emission; emission.enabled = false;
            var shape = ps.shape; shape.enabled = false;
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.7f, 1f, 1.8f));
            var fade = ps.colorOverLifetime;
            fade.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.7f, 0.5f), new GradientAlphaKey(0f, 1f) });
            fade.color = g;
            var limit = ps.limitVelocityOverLifetime;
            limit.enabled = true;
            limit.drag = drag;
            limit.multiplyDragByParticleSize = false;
            limit.multiplyDragByParticleVelocity = true;
            var r = ps.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            ps.Play();
        }

        void Start()
        {
            if (simulation == null)
            {
                Debug.LogError("[ImpactSplashEmitter] No WaterSimulation assigned.", this);
                enabled = false;
            }
        }

        void LateUpdate()
        {
            bool paused = simulation.Paused;
            SetPaused(droplets, paused);
            SetPaused(mist, paused);
            if (paused || !simulation.WaterMoving) return;

            TerrainGrid terrain = simulation.Terrain;
            TsunamiPhysicsSolver s = simulation.Solver;
            float[] bed = terrain.Heights;
            float[] blend = simulation.EdgeBlendField; // the outer band is drawn as calm open ocean
            int n = s.Size, count = n * n;
            int budget = Mathf.Min(cellsPerFrame, count);
            int impacts = 0;
            float half = (n - 1) * 0.5f, cell = s.CellSize;
            Vector3 origin = terrain.transform.position;

            for (int k = 0; k < budget && impacts < maxImpactsPerFrame; k++)
            {
                int i = cursor;
                cursor = (cursor + 1) % count;
                if (blend[i] > 0f) continue;
                if (!ImpactDetection.Find(s, bed, i, minDepth, minImpactSpeed, clearance,
                        out int dx, out int dz, out float un, out float top)) continue;
                // Stronger impacts are more likely to throw water each scan.
                if (Random.value > Mathf.Clamp01(0.25f + (un - minImpactSpeed) / (minImpactSpeed * 3f))) continue;
                int x = i % n, z = i / n;
                float surface = bed[i] + s.Depth[i];
                // On the face between the cell and the obstacle, at the water surface.
                var face = new Vector3(origin.x + (x - half + 0.5f * dx) * cell, origin.y + surface, origin.z + (z - half + 0.5f * dz) * cell);
                var normal = new Vector3(dx, 0f, dz);
                var along = new Vector3(-dz, 0f, dx);
                float rise = Mathf.Min(top - surface, 8f);
                Splash(face, normal, along, cell, un, rise);
                impacts++;
            }

            if (village != null && Time.time >= nextHouseCheck)
            {
                nextHouseCheck = Time.time + houseInterval;
                for (int b = 0; b < village.TotalCount && impacts < maxImpactsPerFrame; b++)
                {
                    if (!village.GetBuilding(b, out Vector3 pos, out float radius, out float roof)) continue;
                    HouseSplash(terrain, s, pos, radius, roof, ref impacts);
                }
            }
        }

        void HouseSplash(TerrainGrid terrain, TsunamiPhysicsSolver s, Vector3 pos, float radius, float roof, ref int impacts)
        {
            if (!terrain.Contains(pos)) return;
            Vector2 g = terrain.WorldToGrid(pos);
            int n = s.Size;
            int i = Mathf.Clamp(Mathf.RoundToInt(g.y), 0, n - 1) * n + Mathf.Clamp(Mathf.RoundToInt(g.x), 0, n - 1);
            float h = s.Depth[i];
            if (h < minDepth) return;
            var v = new Vector2(s.VelocityX[i], s.VelocityZ[i]);
            float speed = v.magnitude;
            if (speed < minImpactSpeed) return;
            float surface = terrain.transform.position.y + terrain.Heights[i] + h;
            if (roof <= surface + clearance) return; // the house is already under water
            if (Random.value > Mathf.Clamp01(0.35f + (speed - minImpactSpeed) / (minImpactSpeed * 3f))) return;
            Vector3 dir = new Vector3(v.x, 0f, v.y) / speed;
            Vector3 face = new Vector3(pos.x, surface, pos.z) - dir * radius;
            Splash(face, dir, new Vector3(-dir.z, 0f, dir.x), radius * 2f, speed, Mathf.Min(roof - surface, 6f));
            impacts++;
        }

        /// <summary>Droplets thrown up and back from an impact plus a mist puff.</summary>
        void Splash(Vector3 face, Vector3 into, Vector3 along, float width, float impactSpeed, float obstacleRise)
        {
            float up = Mathf.Min(maxUpSpeed, impactSpeed * upPerImpact);
            int drops = 2 + Mathf.Min(6, Mathf.FloorToInt(impactSpeed / 3f));
            for (int k = 0; k < drops && droplets.particleCount < maxDroplets; k++)
            {
                ep.position = face + along * ((Random.value - 0.5f) * width) - into * (Random.value * 0.6f);
                ep.velocity = Vector3.up * (up * Random.Range(0.6f, 1.25f))
                            - into * (impactSpeed * Random.Range(0.1f, 0.4f))
                            + along * (impactSpeed * Random.Range(-0.3f, 0.3f));
                ep.startSize = Random.Range(dropletSize.x, dropletSize.y);
                ep.startLifetime = Random.Range(dropletLifetime.x, dropletLifetime.y);
                ep.startColor = dropletColor;
                droplets.Emit(ep, 1);
            }
            if (mist.particleCount < maxMist && Random.value < 0.5f)
            {
                ep.position = face + Vector3.up * Mathf.Clamp(obstacleRise * 0.5f, 0.5f, 3f) - into * 0.5f;
                ep.velocity = Vector3.up * (up * 0.25f) - into * (impactSpeed * 0.15f);
                ep.startSize = Random.Range(mistSize.x, mistSize.y) * Mathf.Clamp(impactSpeed / 6f, 0.6f, 1.6f);
                ep.startLifetime = Random.Range(1.4f, 2.4f);
                ep.startColor = mistColor;
                mist.Emit(ep, 1);
            }
        }

        static void SetPaused(ParticleSystem ps, bool paused)
        {
            if (ps == null) return;
            if (paused && !ps.isPaused) ps.Pause(true);
            else if (!paused && ps.isPaused) ps.Play(true);
        }
    }
}
