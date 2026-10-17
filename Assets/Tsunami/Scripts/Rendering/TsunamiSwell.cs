using UnityEngine;

namespace Tsunami
{
    /// <summary>
    /// Draws the incoming tsunami (WaterSimulation's incoming wave) beyond the simulated area, where the camera
    /// cannot go: a long ridge of water rising far out on the approach axis and travelling in. It is the same wave
    /// the simulation lets in through its edge: same formula, same simulation clock, same water data for the
    /// shader. Inside the simulated area the ridge fades out over the edge band (shader clip box) and the simulated
    /// water carries the wave on to the coast, so there is only ever one wave.
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class TsunamiSwell : MonoBehaviour
    {
        [SerializeField] TsunamiController controller;
        [SerializeField, Range(4, 64)] int crossSegments = 20;
        [SerializeField, Range(8, 128)] int frontSegments = 48;

        Mesh mesh;
        Vector3[] vertices;
        Vector2[] uvs;
        MeshRenderer meshRenderer;
        Material material;

        static readonly int ClipBoxId = Shader.PropertyToID("_ClipBox");
        static readonly int ClipBandId = Shader.PropertyToID("_ClipBand");

        void Awake()
        {
            meshRenderer = GetComponent<MeshRenderer>();
            meshRenderer.enabled = false;
            // The water is transparent and doesn't write depth: drawn in the same queue, the far bigger ocean ring
            // (sorted as closer) would paint over the swell. Draw it after the ocean and the simulated sea.
            if (meshRenderer.sharedMaterial != null)
            {
                material = new Material(meshRenderer.sharedMaterial) { name = meshRenderer.sharedMaterial.name + " (Swell)" };
                material.renderQueue = meshRenderer.sharedMaterial.renderQueue + 1;
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
            meshRenderer.enabled = controller != null && controller.Simulation != null && Place(controller.Simulation);
        }

        float SeaDepth => controller.MapAdapter != null ? controller.MapAdapter.Bathymetry.Validated().deepDepth : 35f;

        /// <summary>Places and shapes the ridge; false when there is nothing to draw outside the simulated area.</summary>
        bool Place(WaterSimulation sim)
        {
            if (!sim.IncomingWaveActive) return false;
            IncomingWave wave = sim.Incoming;
            double time = sim.SimTime;
            float height = wave.CrestHeight(time);
            if (height < 0.05f) return false;

            TerrainGrid grid = sim.Terrain;
            Vector3 centre = grid.transform.position;
            wave.CrestCentre(time, out float cx, out float cz);
            Vector3 travel = new Vector3(wave.dirX, 0f, wave.dirZ);
            Vector3 across = new Vector3(travel.z, 0f, -travel.x);
            Vector3 crest = new Vector3(centre.x + cx, sim.SeaLevelWorld, centre.z + cz);
            float halfFront = wave.widthRadius * 2.5f, halfCross = wave.lengthRadius * 3f;

            // Hidden once the whole ridge is inside the simulated area beyond the edge band.
            float bandStart = grid.CellSize, bandEnd = sim.EdgeFadeWidth;
            bool outside = false;
            for (int k = 0; k < 4 && !outside; k++)
            {
                Vector3 corner = crest + across * ((k & 1) == 0 ? -halfFront : halfFront) +
                                 travel * ((k & 2) == 0 ? -halfCross : halfCross);
                float inside = grid.HalfExtent - Mathf.Max(Mathf.Abs(corner.x - centre.x), Mathf.Abs(corner.z - centre.z));
                outside = inside < bandEnd;
            }
            if (!outside) return false;

            transform.SetPositionAndRotation(crest, Quaternion.LookRotation(travel, Vector3.up));
            if (material != null)
            {
                material.SetVector(ClipBoxId, new Vector4(centre.x, centre.z, grid.HalfExtent, 1f));
                material.SetVector(ClipBandId, new Vector4(bandStart, bandEnd, 0f, 0f));
            }
            UpdateMesh(height, wave.lengthRadius, wave.widthRadius, halfFront, halfCross, SeaDepth, sim.Gravity);
            return true;
        }

        // Local space: +Z = travel direction, X = along the wave front. Vertices on a grid, heights from the same
        // Gaussian as WaterSimulation's incoming wave.
        void BuildMesh()
        {
            int nx = frontSegments + 1;
            vertices = new Vector3[nx * (crossSegments + 1)];
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

        void UpdateMesh(float height, float lengthRadius, float widthRadius, float halfFront, float halfCross,
            float seaDepth, float gravity)
        {
            int nx = frontSegments + 1, nz = crossSegments + 1;
            for (int z = 0; z < nz; z++)
            {
                float v = (z / (float)crossSegments * 2f - 1f) * halfCross;
                float acrossCrest = Mathf.Exp(-0.5f * v * v / (lengthRadius * lengthRadius));
                for (int x = 0; x < nx; x++)
                {
                    float u = (x / (float)frontSegments * 2f - 1f) * halfFront;
                    float shape = acrossCrest * Mathf.Exp(-0.5f * u * u / (widthRadius * widthRadius));
                    float eta = height * shape;
                    int i = z * nx + x;
                    // The rim sits a little under the sea surface so the ocean hides the seam.
                    vertices[i] = new Vector3(u, eta - 0.3f * (1f - shape), v);
                    // Water shader: x = depth, y = flow speed, exactly what the simulation has for the same wave.
                    uvs[i] = new Vector2(seaDepth + eta, IncomingWave.FlowSpeed(eta, seaDepth, gravity));
                }
            }
            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
        }
    }
}
