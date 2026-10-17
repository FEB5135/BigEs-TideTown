using UnityEngine;

namespace Tsunami
{
    /// <summary>
    /// Throws spray particles off fast, rising wave fronts found in the simulation. One pooled
    /// ParticleSystem; a capped number of particles per frame; no allocations. Visual only.
    /// </summary>
    [RequireComponent(typeof(ParticleSystem))]
    public class WaterSprayEmitter : MonoBehaviour
    {
        [SerializeField] WaterSimulation simulation;

        [Header("Where spray appears")]
        [Tooltip("Minimum flow speed (m/s) for a cell to throw spray.")]
        [SerializeField, Range(1f, 20f)] float minSpeed = 6f;
        [Tooltip("Minimum surface rise per second (m/s): spray comes off advancing fronts, not steady currents.")]
        [SerializeField, Range(0f, 10f)] float minRise = 1.5f;
        [SerializeField, Range(0.05f, 3f)] float minDepth = 0.4f;
        [Tooltip("Minimum Froude number (speed / sqrt(g * depth)): spray only where the water is breaking.")]
        [SerializeField, Range(0.3f, 3f)] float minFroude = 1f;

        [Header("Amount")]
        [SerializeField, Range(0, 200)] int maxPerFrame = 25;
        [Tooltip("Cells inspected per frame (spread over frames on big grids).")]
        [SerializeField, Range(256, 65536)] int cellsPerFrame = 4096;
        [SerializeField, Range(100, 5000)] int maxParticles = 600;

        [Header("Look")]
        [SerializeField] Vector2 sizeRange = new Vector2(0.8f, 2.4f);
        [SerializeField] Vector2 lifetimeRange = new Vector2(0.8f, 1.8f);
        [SerializeField] Vector2 upSpeedRange = new Vector2(3f, 10f);
        [Tooltip("Fraction of the water velocity the spray inherits.")]
        [SerializeField, Range(0f, 1.5f)] float carry = 0.7f;
        [SerializeField] Color color = new Color(0.94f, 0.98f, 1f, 0.75f);

        ParticleSystem ps;
        float[] previousSurface;
        int cursor;
        float lastTime;

        void Awake()
        {
            ps = GetComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.playOnAwake = false;
            main.loop = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = 1f;
            main.maxParticles = maxParticles;
            main.startSpeed = 0f;

            var emission = ps.emission;
            emission.enabled = false;
            var shape = ps.shape;
            shape.enabled = false;

            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 1.6f));

            var fade = ps.colorOverLifetime;
            fade.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.8f, 0.4f), new GradientAlphaKey(0f, 1f) });
            fade.color = gradient;

            var drag = ps.limitVelocityOverLifetime;
            drag.enabled = true;
            drag.drag = 0.8f;
            drag.multiplyDragByParticleSize = false;
            drag.multiplyDragByParticleVelocity = true;

            var r = GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;

            ps.Play();
        }

        void Start()
        {
            if (simulation == null)
            {
                Debug.LogError("[WaterSprayEmitter] No WaterSimulation assigned.", this);
                enabled = false;
            }
        }

        void LateUpdate()
        {
            TerrainGrid terrain = simulation.Terrain;
            float[] h = terrain.Heights;
            TsunamiPhysicsSolver solver = simulation.Solver;
            float[] depth = solver.Depth;
            int count = depth.Length;
            if (previousSurface == null || previousSurface.Length != count)
            {
                previousSurface = new float[count];
                for (int i = 0; i < count; i++) previousSurface[i] = h[i] + depth[i];
                lastTime = Time.time;
                return;
            }

            // Calm water throws no spray: skip the scan entirely.
            if (!simulation.WaterMoving) { lastTime = Time.time; return; }
            float[] blend = simulation.EdgeBlendField;

            // Inspect a slice of cells each frame; the rise rate uses each cell's own last-visit time.
            float now = Time.time;
            int n = terrain.Resolution;
            int budget = Mathf.Min(cellsPerFrame, count);
            float sliceDt = Mathf.Max(1e-3f, (now - lastTime) * Mathf.Max(1f, (float)count / budget));
            lastTime = now;
            int emitted = 0;
            var p = new ParticleSystem.EmitParams { applyShapeToPosition = false };

            for (int k = 0; k < budget; k++)
            {
                int i = cursor;
                cursor = (cursor + 1) % count;
                float surface = h[i] + depth[i];
                float rise = (surface - previousSurface[i]) / sliceDt;
                previousSurface[i] = surface;

                if (emitted >= maxPerFrame || ps.particleCount >= maxParticles) continue;
                if (blend[i] > 0f) continue; // the edge band is drawn as calm open ocean
                float speed = solver.Speed[i];
                if (depth[i] < minDepth || speed < minSpeed || rise < minRise) continue;
                if (speed * speed < minFroude * minFroude * 9.81f * depth[i]) continue;
                // Faster, steeper fronts are more likely to throw spray.
                float chance = Mathf.Clamp01((speed - minSpeed) / minSpeed * 0.5f + 0.15f);
                if (Random.value > chance) continue;

                Vector3 pos = terrain.GridToWorld(i % n, i / n, surface);
                pos.x += (Random.value - 0.5f) * terrain.CellSize;
                pos.z += (Random.value - 0.5f) * terrain.CellSize;
                p.position = pos;
                p.velocity = new Vector3(solver.VelocityX[i] * carry, Random.Range(upSpeedRange.x, upSpeedRange.y),
                                         solver.VelocityZ[i] * carry);
                p.startSize = Random.Range(sizeRange.x, sizeRange.y);
                p.startLifetime = Random.Range(lifetimeRange.x, lifetimeRange.y);
                p.startColor = color;
                ps.Emit(p, 1);
                emitted++;
            }
        }
    }
}
