using UnityEngine;

namespace Tsunami
{
    /// <summary>
    /// The tsunami coming in from the open sea, beyond where the camera can go. While the controller is in its
    /// Warning state, a long ridge of water rises far out on the approach axis and travels to the source position;
    /// when it arrives the controller releases the simulated wave there, under it, and the ridge fades out.
    /// The simulation grid only covers the map and a band of sea, so this part is drawn rather than simulated.
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class TsunamiSwell : MonoBehaviour
    {
        [SerializeField] TsunamiController controller;
        [Tooltip("Distance (m) beyond the source where the swell first rises.")]
        [SerializeField, Min(50f)] float startDistance = 520f;
        [Tooltip("Height at the start as a share of the wave height (deep water: low and long).")]
        [SerializeField, Range(0f, 1f)] float startHeight = 0.35f;
        [Tooltip("Seconds the ridge takes to sink back once the simulated wave is released.")]
        [SerializeField, Min(0.05f)] float fadeTime = 0.6f;
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
                float t = 1f - controller.WarningRemaining / controller.WarningDuration;
                Place(t, 1f);
                fade = 1f;
            }
            else if (fade > 0f && controller.State == TsunamiState.Running)
            {
                // The simulated wave has taken over at the source: sink the ridge into it.
                fade = Mathf.MoveTowards(fade, 0f, Time.deltaTime / fadeTime);
                Place(1f, fade);
            }
            else
            {
                fade = 0f;
            }
            meshRenderer.enabled = fade > 0f;
        }

        /// <param name="t">0 = far out, 1 = at the source.</param>
        void Place(float t, float heightScale)
        {
            Vector3 source = controller.SourcePosition;
            Vector2 d = controller.TravelDirection;
            if (d.sqrMagnitude < 1e-4f) d = Vector2.up;
            Vector3 travel = new Vector3(d.x, 0f, d.y).normalized;
            float seaY = controller.Simulation != null ? controller.Simulation.SeaLevel : 0f;
            if (controller.MapAdapter != null && controller.MapAdapter.World != null)
                seaY += controller.MapAdapter.World.transform.position.y;
            // Constant speed: the swell reaches the source exactly when the countdown ends.
            Vector3 position = source - travel * (startDistance * (1f - t));
            position.y = seaY;
            transform.SetPositionAndRotation(position, Quaternion.LookRotation(travel, Vector3.up));

            TsunamiScenario s = controller.Current;
            float height = Mathf.Max(0f, s.amplitude) * Mathf.Lerp(startHeight, 1f, t * t) * heightScale;
            UpdateMesh(height, Mathf.Max(4f, s.lengthRadius), Mathf.Max(4f, s.widthRadius));
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

        void UpdateMesh(float height, float lengthRadius, float widthRadius)
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
                    // Water shader: x = depth, y = flow speed. Towards the crest the water reads as shallow (light
                    // teal, like the simulated wave) and fast enough to break into foam, so the ridge stands out
                    // from the open sea around it.
                    float c = crest * Mathf.Clamp01(height / foamHeight);
                    uvs[i] = new Vector2(Mathf.Lerp(WaterSimulation.OpenOceanShaderDepth, 2f, c), crestSpeed * c * c);
                }
            }
            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
        }
    }
}
