using UnityEngine;

namespace Tsunami
{
    /// <summary>
    /// Draws the simulated water as one mesh with one vertex per simulation cell.
    /// Vertex height = terrain + depth. Dry vertices are tucked under the terrain so they are hidden.
    /// UV0 carries (depth, flow speed) for the water shader (colour and foam only).
    /// Buffers are allocated once; per-step updates are allocation-free.
    /// Also runs in edit mode (as a calm sea) so the Scene view never shows the bare seabed square.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class WaterMeshRenderer : MonoBehaviour
    {
        [SerializeField] WaterSimulation simulation;
        [Tooltip("How far below the terrain dry vertices are pushed (m).")]
        [SerializeField, Range(0.05f, 2f)] float dryOffset = 0.3f;
        [Tooltip("Visual-only smoothing of the rendered surface (0 = raw simulation heights). Removes the crumpled " +
                 "look on steep wave faces; edge vertices are never smoothed, so the ocean seam stays exact.")]
        [SerializeField, Range(0f, 1f)] float surfaceSmoothing = 0.6f;

        Mesh mesh;
        Vector3[] vertices;
        Vector3[] normals;
        Vector2[] uvs;
        Vector2[] flows; // UV1: simulated velocity (m/s) so the shader can stream foam with the water
        float[] smoothedY;
        int lastStep = -1;
        int lastTerrainVersion = -1;
        int lastOceanVersion = -1;

        void OnEnable()
        {
            if (simulation == null)
            {
                if (Application.isPlaying) Debug.LogError("[WaterMeshRenderer] No WaterSimulation assigned.", this);
                return;
            }
            simulation.EnsureInitialized();
            TerrainGrid terrain = simulation.Terrain;
            // Vertices are generated in the terrain's local frame.
            transform.SetPositionAndRotation(terrain.transform.position, Quaternion.identity);
            transform.localScale = Vector3.one;

            int n = terrain.Resolution;
            int count = n * n;
            vertices = new Vector3[count];
            normals = new Vector3[count];
            uvs = new Vector2[count];
            flows = new Vector2[count];
            smoothedY = new float[count];
            mesh = new Mesh { name = "Flood Water (generated)", hideFlags = HideFlags.DontSave };
            mesh.MarkDynamic();
            mesh.indexFormat = count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            Fill();
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetUVs(1, flows);
            mesh.SetTriangles(TerrainGrid.BuildGridTriangles(n), 0);
            float e = terrain.HalfExtent * 2f + terrain.CellSize;
            mesh.bounds = new Bounds(Vector3.zero, new Vector3(e, 140f, e));
            GetComponent<MeshFilter>().sharedMesh = mesh;
            lastStep = simulation.StepCount;
            lastTerrainVersion = terrain.Version;
            lastOceanVersion = simulation.OpenOceanVersion;
        }

        void OnDisable()
        {
            if (mesh == null) return;
            if (Application.isPlaying) Destroy(mesh); else DestroyImmediate(mesh);
            mesh = null;
        }

        void LateUpdate()
        {
            if (mesh == null || simulation == null) return;
            TerrainGrid terrain = simulation.Terrain;
            if (terrain.Resolution * terrain.Resolution != vertices.Length)
            {
                // Grid size changed in the Inspector: rebuild buffers.
                OnDisable();
                OnEnable();
                return;
            }
            bool terrainChanged = terrain.Version != lastTerrainVersion;
            bool oceanChanged = simulation.OpenOceanVersion != lastOceanVersion;
            if (simulation.StepCount == lastStep && !terrainChanged && !oceanChanged) return;
            lastStep = simulation.StepCount;
            // Calm water: nothing to re-upload (saves the whole mesh update most of the time).
            if (!terrainChanged && !oceanChanged && !simulation.WaterMoving) return;
            // In edit mode the island can be regenerated from the Inspector: refill the calm sea to match.
            if (!Application.isPlaying && terrainChanged) simulation.ResetWater();
            lastTerrainVersion = terrain.Version;
            lastOceanVersion = simulation.OpenOceanVersion;
            Fill();
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetUVs(1, flows);
        }

        void Fill()
        {
            TerrainGrid terrain = simulation.Terrain;
            float[] h = terrain.Heights;
            float[] depth = simulation.Depth;
            float[] speed = simulation.Speed;
            float[] velX = simulation.Solver.VelocityX, velZ = simulation.Solver.VelocityZ;
            float[] blend = simulation.EdgeBlendField;
            byte[] walls = simulation.Solver.BarrierMask; // thin walls on cell faces (bits: 1 -x, 2 +x, 4 -z, 8 +z)
            float wet = simulation.WetThreshold;
            int n = terrain.Resolution;
            float cell = terrain.CellSize;
            float half = (n - 1) * 0.5f;

            for (int z = 0; z < n; z++)
            {
                for (int x = 0; x < n; x++)
                {
                    int i = z * n + x;
                    float d = depth[i];
                    float y, u, v;
                    Vector2 flow = Vector2.zero;
                    if (d > wet) { y = h[i] + d; u = d; v = speed[i]; flow = new Vector2(velX[i], velZ[i]); }
                    else { y = h[i] - dryOffset; u = 0f; v = 0f; }
                    // Near the grid edge, fade into the open-ocean surface shared with OceanRing, so the
                    // square simulation boundary never shows (identical heights, foam values and normals).
                    float b = blend[i];
                    if (b > 0f)
                    {
                        float oceanY = simulation.OceanAt(x, z, out float oceanSpeed);
                        y += (oceanY - y) * b;
                        u += (WaterSimulation.OpenOceanShaderDepth - u) * b;
                        v += (oceanSpeed - v) * b;
                        flow *= 1f - b;
                    }
                    vertices[i] = new Vector3((x - half) * cell, y, (z - half) * cell);
                    uvs[i] = new Vector2(u, v);
                    flows[i] = flow;
                }
            }

            // Smooth waterline: dry vertices next to water continue the water surface flat (but never above
            // their own ground), so the visible edge is where the surface meets the terrain, not a staircase of
            // grid cells. Their depth is 0, so the shader fades the water out softly there.
            // Across a wall the surface must not continue: the dry vertex is mirrored below the ground instead,
            // so the water edge lands exactly on the wall line (half-way between the two cells).
            for (int z = 1; z < n - 1; z++)
            {
                for (int x = 1; x < n - 1; x++)
                {
                    int i = z * n + x;
                    if (depth[i] > wet || blend[i] > 0f) continue;
                    byte m = walls[i];
                    float sum = 0f, walled = float.NegativeInfinity;
                    int count = 0;
                    Neighbour(i - 1, 1);
                    Neighbour(i + 1, 2);
                    Neighbour(i - n, 4);
                    Neighbour(i + n, 8);
                    if (count > 0) vertices[i].y = Mathf.Min(sum / count, h[i] - 0.02f);
                    else if (walled > h[i]) vertices[i].y = h[i] - (walled - h[i]) - 0.05f;

                    void Neighbour(int j, byte face)
                    {
                        if (depth[j] <= wet) return;
                        if ((m & face) != 0) { if (vertices[j].y > walled) walled = vertices[j].y; }
                        else { sum += vertices[j].y; count++; }
                    }
                }
            }

            // Light smoothing of fully wet interior vertices (visual only; the simulation is untouched).
            // The two outermost rows are pure open ocean and are never smoothed, keeping the seam exact.
            if (surfaceSmoothing > 0f)
            {
                for (int i = 0; i < smoothedY.Length; i++) smoothedY[i] = vertices[i].y;
                for (int z = 2; z < n - 2; z++)
                {
                    for (int x = 2; x < n - 2; x++)
                    {
                        int i = z * n + x;
                        if (depth[i] <= wet || depth[i - 1] <= wet || depth[i + 1] <= wet || depth[i - n] <= wet || depth[i + n] <= wet)
                            continue;
                        if (walls[i] != 0) continue; // never average heights across a wall
                        float avg = 0.25f * (vertices[i - 1].y + vertices[i + 1].y + vertices[i - n].y + vertices[i + n].y);
                        float y = Mathf.Lerp(vertices[i].y, avg, surfaceSmoothing);
                        smoothedY[i] = Mathf.Max(y, h[i] + 0.02f); // never sink below the ground
                    }
                }
                for (int i = 0; i < smoothedY.Length; i++) vertices[i].y = smoothedY[i];
            }

            // Normals from central differences. Neighbours beyond the grid come from the same cached
            // open-ocean surface OceanRing uses, so normals on both sides of the seam are identical.
            float twoCell = 2f * cell;
            for (int z = 0; z < n; z++)
            {
                for (int x = 0; x < n; x++)
                {
                    int i = z * n + x;
                    if (!IsWet(i)) { normals[i] = Vector3.up; continue; }
                    byte m = walls[i];
                    float yl = (m & 1) != 0 ? vertices[i].y : Surface(x - 1, z, i);
                    float yr = (m & 2) != 0 ? vertices[i].y : Surface(x + 1, z, i);
                    float yd = (m & 4) != 0 ? vertices[i].y : Surface(x, z - 1, i);
                    float yu = (m & 8) != 0 ? vertices[i].y : Surface(x, z + 1, i);
                    normals[i] = new Vector3(yl - yr, twoCell, yd - yu).normalized;
                }
            }

            bool IsWet(int i) => depth[i] > wet || blend[i] > 0f;

            // Use the centre vertex for dry neighbours so shorelines don't tilt sharply.
            float Surface(int x, int z, int center)
            {
                if (x < 0 || z < 0 || x >= n || z >= n) return simulation.OceanAt(x, z, out _);
                int j = z * n + x;
                return IsWet(j) ? vertices[j].y : vertices[center].y;
            }
        }
    }
}
