using UnityEngine;

namespace Tsunami
{
    /// <summary>
    /// The tsunami coming in from the open sea, beyond where the camera can go. While the controller is in its
    /// Warning state, a long ridge of water rises far out on the approach axis and travels to the source position;
    /// when it arrives the controller releases the simulated wave there, under it. The ridge has the simulated
    /// wave's shape and moves at its speed (long-wave speed in the deep sea), and on the last stretch it takes on
    /// its colours, so after the release it simply carries on with the simulated wave and fades into it.
    /// The simulation grid only covers the map and a band of sea, so this part is drawn rather than simulated.
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class TsunamiSwell : MonoBehaviour
    {
        [SerializeField] TsunamiController controller;
        [Tooltip("Distance (m) beyond the source where the swell first rises. Unused while Match Wave Speed is on: " +
                 "then it is the distance the simulated wave covers in the warning time.")]
        [SerializeField, Min(50f)] float startDistance = 520f;
        [Tooltip("Travel at the simulated wave's speed, sqrt(g (depth + height/2)) in the deep sea, so the hand-over " +
                 "at the source has no jump in speed.")]
        [SerializeField] bool matchWaveSpeed = true;
        [Tooltip("Height at the start as a share of the wave height (deep water: low and long).")]
        [SerializeField, Range(0f, 1f)] float startHeight = 0.35f;
        [Tooltip("Seconds the ridge keeps travelling with the simulated wave while it fades into it after the release.")]
        [SerializeField, Min(0.05f)] float fadeTime = 1.5f;
        [Tooltip("Share of the trip (0..1) from which the ridge's light, foamy crest turns into the simulated wave's " +
                 "deep-sea look, so nothing changes at the hand-over.")]
        [SerializeField, Range(0f, 1f)] float blendToSimFrom = 0.6f;
        [Tooltip("Ridge height (m) from which the crest is fully light and foamy.")]
        [SerializeField, Min(0.1f)] float foamHeight = 6f;
        [Tooltip("Flow speed (m/s) given to the water shader at the crest; higher = more breaking foam.")]
        [SerializeField, Min(0f)] float crestSpeed = 14f;
        [SerializeField, Range(4, 64)] int crossSegments = 14;
        [SerializeField, Range(8, 128)] int frontSegments = 48;

        Mesh mesh;
        Vector3[] vertices;
        Vector2[] uvs;
        MeshRenderer meshRenderer;
        Material material;
        float fade;              // 1 while travelling, falls to 0 after the release
        float sinceRelease;

        void Awake()
        {
            meshRenderer = GetComponent<MeshRenderer>();
            meshRenderer.enabled = false;
            // The water is transparent and doesn't write depth: drawn in the same queue, the far bigger ocean ring
            // (sorted as closer) would paint over the swell. Draw it after the ocean and the stormy-sea shade.
            if (meshRenderer.sharedMaterial != null)
            {
                material = new Material(meshRenderer.sharedMaterial) { name = meshRenderer.sharedMaterial.name + " (Swell)" };
                material.renderQueue = meshRenderer.sharedMaterial.renderQueue + 2;
                meshRenderer.sharedMaterial = material;
            }
            if (controller == null) controller = FindAnyObjectByType<TsunamiController>();
            BuildMesh();
        }

        void OnDestroy()
        {
            if (mesh != null) Destroy(mesh);
            if (material != null) Destroy(material);
        }

        void LateUpdate()
        {
            if (controller == null) return;
            bool warning = controller.State == TsunamiState.Warning && controller.WarningDuration > 0f;
            if (warning)
            {
                Place(-controller.WarningRemaining, 1f);
                fade = 1f;
                sinceRelease = 0f;
            }
            else if (fade > 0f && controller.State == TsunamiState.Running)
            {
                // The simulated wave has taken over at the source and moves on at the same speed: travel with it
                // and sink into it.
                sinceRelease += Time.deltaTime;
                fade = Mathf.Clamp01(1f - sinceRelease / fadeTime);
                Place(sinceRelease, fade * fade);
            }
            else
            {
                fade = 0f;
            }
            meshRenderer.enabled = fade > 0f;
        }

        float SeaDepth => controller.MapAdapter != null ? controller.MapAdapter.Bathymetry.Validated().deepDepth : 35f;

        /// <summary>Long-wave speed of the simulated wave in the deep sea (m/s).</summary>
        float WaveSpeed() => Mathf.Sqrt(9.81f * (SeaDepth + 0.5f * Mathf.Max(0f, controller.Current.amplitude)));

        /// <param name="time">Seconds relative to the release: negative while coming in, positive after.</param>
        void Place(float time, float heightScale)
        {
            float duration = Mathf.Max(0.01f, controller.WarningDuration);
            float speed = matchWaveSpeed ? WaveSpeed() : startDistance / duration;
            float t = Mathf.Clamp01(1f + time / duration); // trip progress: 0 = far out, 1 = at the source
            Vector3 source = controller.SourcePosition;
            Vector2 d = controller.TravelDirection;
            if (d.sqrMagnitude < 1e-4f) d = Vector2.up;
            Vector3 travel = new Vector3(d.x, 0f, d.y).normalized;
            float seaY = controller.Simulation != null ? controller.Simulation.SeaLevel : 0f;
            if (controller.MapAdapter != null && controller.MapAdapter.World != null)
                seaY += controller.MapAdapter.World.transform.position.y;
            // Constant speed: the swell reaches the source exactly when the countdown ends, then keeps going.
            Vector3 position = source + travel * (speed * time);
            position.y = seaY;
            transform.SetPositionAndRotation(position, Quaternion.LookRotation(travel, Vector3.up));

            TsunamiScenario s = controller.Current;
            float height = Mathf.Max(0f, s.amplitude) * Mathf.Lerp(startHeight, 1f, t * t) * heightScale;
            float simLook = time >= 0f ? 1f : Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(blendToSimFrom, 1f, t));
            UpdateMesh(height, Mathf.Max(4f, s.lengthRadius), Mathf.Max(4f, s.widthRadius), simLook, SeaDepth);
        }

        // Local space: +Z = travel direction, X = along the wave front. Vertices on a grid, heights from a
        // Gaussian across the ridge, tapering towards the ends of the front like the simulated source.
        void BuildMesh()
        {
            int nx = frontSegments + 1, nz = crossSegments + 1;
            vertices = new Vector3[nx * nz];
            uvs = new Vector2[vertices.Length];
            var tris = new int[frontSegments * crossSegments * 6];
            int k = 0;
            for (int z = 0; z < crossSegments; z++)
            {
                for (int x = 0; x < frontSegments; x++)
                {
                    int i = z * nx + x;
                    tris[k++] = i; tris[k++] = i + nx; tris[k++] = i + 1;
                    tris[k++] = i + 1; tris[k++] = i + nx; tris[k++] = i + nx + 1;
                }
            }
            mesh = new Mesh { name = "Tsunami Swell (generated)" };
            mesh.MarkDynamic();
            mesh.vertices = vertices;
            mesh.triangles = tris;
            GetComponent<MeshFilter>().sharedMesh = mesh;
        }

        void UpdateMesh(float height, float lengthRadius, float widthRadius, float simLook, float seaDepth)
        {
            int nx = frontSegments + 1, nz = crossSegments + 1;
            float halfFront = widthRadius * 2.5f, halfCross = lengthRadius * 3f;
            for (int z = 0; z < nz; z++)
            {
                float v = (z / (float)crossSegments * 2f - 1f) * halfCross;
                float across = Mathf.Exp(-0.5f * v * v / (lengthRadius * lengthRadius));
                for (int x = 0; x < nx; x++)
                {
                    float u = (x / (float)frontSegments * 2f - 1f) * halfFront;
                    float along = Mathf.Exp(-0.5f * u * u / (widthRadius * widthRadius));
                    // The rim sits a little under the sea surface so the ocean hides the seam.
                    float crest = across * along;
                    float y = height * crest - 0.3f;
                    int i = z * nx + x;
                    vertices[i] = new Vector3(u, y, v);
                    // Water shader: x = depth, y = flow speed. Far out the crest reads as shallow (light teal) and
                    // fast enough to break into foam, so the ridge stands out from the open sea. Near the source it
                    // turns into what the simulation shows for the same wave: the real depth under the crest and the
                    // long-wave flow speed eta * sqrt(g / h), so the hand-over doesn't change the look.
                    float c = crest * Mathf.Clamp01(height / foamHeight);
                    var showy = new Vector2(Mathf.Lerp(WaterSimulation.OpenOceanShaderDepth, 2f, c), crestSpeed * c * c);
                    float eta = Mathf.Max(0f, y);
                    var real = new Vector2(seaDepth + eta, eta * Mathf.Sqrt(9.81f / seaDepth));
                    uvs[i] = Vector2.Lerp(showy, real, simLook);
                }
            }
            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
        }
    }
}
