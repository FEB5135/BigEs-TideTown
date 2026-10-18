using UnityEngine;
using System.Collections.Generic;

namespace IdleTown.Construction
{
    // Shear rather than rotate: boards and stacked floors stay vertical; rails follow the slope.
    public sealed class WallSlopeGeometry : MonoBehaviour
    {
        Mesh source, visual, collision;
        float rise = float.NaN;
        float[] profile;
        float[] groundProfile;
        public float Rise => rise;
        public float[] Profile => profile;
        public float[] GroundProfile => groundProfile;
        public float HeightOffset(float z)
        {
            if (profile == null) return z * rise;
            return SampleProfile(profile, z);
        }
        public float BaseHeightOffset(float z)
        {
            if (groundProfile == null) return HeightOffset(z);
            // A cliff face may fall between samples. Keep the footing below both neighbouring
            // samples instead of drawing another diagonal underneath and leaving a daylight wedge.
            float index = Mathf.Clamp01(z + .5f) * (groundProfile.Length - 1);
            int lower = Mathf.Clamp(Mathf.FloorToInt(index) - 1, 0, groundProfile.Length - 1);
            int upper = Mathf.Clamp(Mathf.CeilToInt(index) + 1, 0, groundProfile.Length - 1);
            float height = groundProfile[lower];
            for (int i = lower + 1; i <= upper; i++) height = Mathf.Min(height, groundProfile[i]);
            return height;
        }
        static float SampleProfile(float[] heights, float z)
        {
            float index = Mathf.Clamp01(z + .5f) * (heights.Length - 1);
            int lower = Mathf.Min(Mathf.FloorToInt(index), heights.Length - 2);
            return Mathf.Lerp(heights[lower], heights[lower + 1], index - lower);
        }

        // A cliff can jump several metres within the final few centimetres of a tile. Spread that
        // jump over a supporting ramp instead of stretching the artwork into a thin spike. The upper
        // hull never cuts into terrain, keeps shared endpoints fixed and leaves ordinary hills alone.
        public static float[] SmoothCliffProfile(float[] heights)
        {
            bool cliff = false;
            for (int i = 1; i < heights.Length; i++) cliff |= Mathf.Abs(heights[i] - heights[i - 1]) > .5f;
            if (!cliff) return heights;
            var hull = new List<int>();
            for (int i = 0; i < heights.Length; i++)
            {
                while (hull.Count > 1)
                {
                    int a = hull[hull.Count - 2], b = hull[hull.Count - 1];
                    if ((heights[b] - heights[a]) / (b - a) >= (heights[i] - heights[b]) / (i - b)) break;
                    hull.RemoveAt(hull.Count - 1);
                }
                hull.Add(i);
            }
            var smooth = (float[])heights.Clone();
            bool changed = false;
            for (int part = 1; part < hull.Count; part++)
            {
                int a = hull[part - 1], b = hull[part];
                for (int i = a + 1; i < b; i++)
                {
                    smooth[i] = Mathf.Max(heights[i], Mathf.Lerp(heights[a], heights[b], (i - a) / (float)(b - a)));
                    changed |= smooth[i] - heights[i] > .002f;
                }
            }
            return changed ? smooth : heights;
        }

        public void Configure(MeshFilter filter, Mesh mesh, float normalizedRise, bool solid,
            float[] terrainProfile = null, float[] foundationProfile = null)
        {
            if (source == mesh && Mathf.Approximately(rise, normalizedRise) &&
                SameProfile(profile, terrainProfile) && SameProfile(groundProfile, foundationProfile))
            { filter.sharedMesh = visual; return; }
            Release(visual); Release(collision);
            source = mesh;
            rise = normalizedRise;
            profile = terrainProfile;
            groundProfile = foundationProfile;
            visual = profile == null ? Instantiate(mesh) : Subdivide(mesh, profile.Length - 1);
            visual.name = mesh.name + " Terrain Slope";
            var vertices = visual.vertices;
            for (int i = 0; i < vertices.Length; i++) vertices[i].y += HeightOffset(vertices[i].z);
            visual.vertices = vertices;
            if (groundProfile != null) AddFoundation(visual);
            visual.RecalculateNormals(); visual.RecalculateBounds();
            filter.sharedMesh = visual;
            if (!solid) return;
            var box = GetComponent<BoxCollider>();
            if (box != null) box.enabled = false;
            collision = new Mesh { name = "Sloped Wall Collider" };
            int sections = profile == null ? 1 : profile.Length - 1;
            vertices = new Vector3[(sections + 1) * 4];
            var triangles = new List<int>();
            for (int section = 0; section <= sections; section++)
            {
                float z = section / (float)sections - .5f;
                for (int corner = 0; corner < 4; corner++)
                    vertices[section * 4 + corner] = new Vector3((corner & 1) == 0 ? -.5f : .5f,
                        (corner & 2) == 0 ? -.5f + BaseHeightOffset(z) : .5f + HeightOffset(z), z);
                if (section == sections) break;
                int n = section * 4;
                foreach (int index in new[] { 0,1,5,0,5,4, 2,6,7,2,7,3, 0,4,6,0,6,2, 1,3,7,1,7,5 })
                    triangles.Add(n + index);
            }
            triangles.AddRange(new[] { 0,3,1,0,2,3 });
            int last = sections * 4;
            triangles.AddRange(new[] { last,last+1,last+3,last,last+3,last+2 });
            collision.vertices = vertices;
            collision.triangles = triangles.ToArray();
            collision.RecalculateBounds();
            var collider = GetComponent<MeshCollider>();
            if (collider == null) collider = gameObject.AddComponent<MeshCollider>();
            collider.sharedMesh = collision;
        }
        static bool SameProfile(float[] current, float[] other)
        {
            if (current == null || other == null) return current == other;
            if (current.Length != other.Length) return false;
            for (int i = 0; i < current.Length; i++) if (!Mathf.Approximately(current[i], other[i])) return false;
            return true;
        }

        void AddFoundation(Mesh mesh)
        {
            var vertices = new List<Vector3>(mesh.vertices);
            var uv = new List<Vector2>(mesh.uv);
            var faces = new List<int>(mesh.GetTriangles(0));
            int start = vertices.Count;
            for (int i = 0; i < groundProfile.Length; i++)
            {
                float z = i / (float)(groundProfile.Length - 1) - .5f;
                float bottom = -.5f + BaseHeightOffset(z), top = -.5f + HeightOffset(z);
                // Slightly overlap the continuous core so no daylight seam appears along the ramp.
                vertices.Add(new Vector3(-.36f, bottom, z)); vertices.Add(new Vector3(.36f, bottom, z));
                vertices.Add(new Vector3(-.36f, top + .005f, z)); vertices.Add(new Vector3(.36f, top + .005f, z));
                for (int corner = 0; corner < 4; corner++) uv.Add(Vector2.zero);
                if (i == 0) continue;
                int n = start + (i - 1) * 4;
                foreach (int index in new[] { 0,4,6,0,6,2, 1,3,7,1,7,5, 0,1,5,0,5,4, 2,6,7,2,7,3 }) faces.Add(n + index);
            }
            faces.AddRange(new[] { start,start+2,start+3,start,start+3,start+1 });
            int end = vertices.Count - 4;
            faces.AddRange(new[] { end,end+1,end+3,end,end+3,end+2 });
            mesh.SetVertices(vertices); mesh.SetUVs(0, uv); mesh.SetTriangles(faces, 0);
        }

        readonly struct Vertex
        {
            public readonly Vector3 Point;
            public readonly Vector2 UV;
            public Vertex(Vector3 point, Vector2 uv) { Point = point; UV = uv; }
        }

        // Split the original artwork at terrain sample positions before deforming it. Merely moving
        // its end vertices would still cut through a summit or bridge over a valley within one edge.
        static Mesh Subdivide(Mesh source, int sections)
        {
            var positions = source.vertices;
            var uv = source.uv;
            var vertices = new List<Vector3>();
            var uvs = new List<Vector2>();
            var submeshes = new List<int[]>();
            for (int submesh = 0; submesh < source.subMeshCount; submesh++)
            {
                var indices = source.GetTriangles(submesh);
                var output = new List<int>();
                for (int i = 0; i < indices.Length; i += 3)
                    for (int section = 0; section < sections; section++)
                    {
                        var polygon = new List<Vertex>();
                        for (int corner = 0; corner < 3; corner++)
                        {
                            int index = indices[i + corner];
                            polygon.Add(new Vertex(positions[index], index < uv.Length ? uv[index] : Vector2.zero));
                        }
                        if (section > 0) polygon = Clip(polygon, section / (float)sections - .5f, true);
                        if (section < sections - 1) polygon = Clip(polygon, (section + 1f) / sections - .5f, false);
                        int first = vertices.Count;
                        foreach (var vertex in polygon) { vertices.Add(vertex.Point); uvs.Add(vertex.UV); }
                        for (int corner = 1; corner + 1 < polygon.Count; corner++)
                        { output.Add(first); output.Add(first + corner); output.Add(first + corner + 1); }
                    }
                submeshes.Add(output.ToArray());
            }
            var mesh = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.SetVertices(vertices); mesh.SetUVs(0, uvs); mesh.subMeshCount = submeshes.Count;
            for (int i = 0; i < submeshes.Count; i++) mesh.SetTriangles(submeshes[i], i);
            return mesh;
        }
        static List<Vertex> Clip(List<Vertex> polygon, float boundary, bool above)
        {
            var output = new List<Vertex>();
            if (polygon.Count == 0) return output;
            var previous = polygon[polygon.Count - 1];
            bool Inside(Vertex vertex) => above ? vertex.Point.z >= boundary : vertex.Point.z <= boundary;
            foreach (var current in polygon)
            {
                if (Inside(previous) != Inside(current))
                {
                    float t = (boundary - previous.Point.z) / (current.Point.z - previous.Point.z);
                    output.Add(new Vertex(Vector3.Lerp(previous.Point, current.Point, t), Vector2.Lerp(previous.UV, current.UV, t)));
                }
                if (Inside(current)) output.Add(current);
                previous = current;
            }
            return output;
        }
        static void Release(Object value)
        {
            if (value == null) return;
            if (Application.isPlaying) Destroy(value); else DestroyImmediate(value);
        }
        void OnDestroy() { Release(visual); Release(collision); }
    }
}
