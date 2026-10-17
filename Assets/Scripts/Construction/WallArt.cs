using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace IdleTown.Construction
{
    /// <summary>Original modular wall art. Meshes fit inside a unit footprint; no imported art is needed.</summary>
    public sealed class WallArt : IDisposable
    {
        readonly Dictionary<(WallTier, int), Mesh> meshes = new();
        readonly Dictionary<WallTier, Material[]> palettes = new();
        Mesh post;

        public Mesh Panel(WallTier tier, float endInset)
        {
            int inset = Mathf.RoundToInt(Mathf.Clamp(endInset, 0f, .42f) * 10000f);
            var key = (tier, inset);
            if (!meshes.TryGetValue(key, out var mesh))
                meshes[key] = mesh = CreatePanel(tier, inset / 10000f);
            return mesh;
        }

        public Mesh Post => post != null ? post : post = CreatePost();

        public Material[] Palette(WallTier tier)
        {
            if (palettes.TryGetValue(tier, out var materials)) return materials;
            Color body = tier switch
            {
                WallTier.Timber => new Color(.46f, .25f, .105f),
                WallTier.Stone => new Color(.46f, .51f, .53f),
                _ => new Color(.29f, .36f, .4f)
            };
            materials = new[]
            {
                Material(body, false), Material(body * new Color(.73f, .76f, .78f, 1f), false),
                Material(tier == WallTier.Timber ? new Color(.19f, .125f, .075f) : new Color(.15f, .21f, .24f), false)
            };
            for (int i = 0; i < materials.Length; i++) materials[i].name = $"Wall {tier} palette {i}";
            palettes[tier] = materials;
            return materials;
        }

        public static Material Material(Color color, bool unlit)
        {
            var shader = Shader.Find(unlit ? "Universal Render Pipeline/Unlit" : "Universal Render Pipeline/Lit")
                         ?? Shader.Find(unlit ? "Unlit/Color" : "Standard");
            var material = new Material(shader) { color = color };
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", .18f);
            return material;
        }

        public static Mesh CreatePanel(WallTier tier, float inset)
        {
            var builder = new Builder();
            float length = 1f - 2f * inset;
            if (tier == WallTier.Timber)
            {
                // Staggered pickets, chamfered edges and two dark horizontal rails.
                const int boards = 6;
                for (int i = 0; i < boards; i++)
                {
                    float z = -length * .5f + length * (i + .5f) / boards;
                    float height = i % 2 == 0 ? .94f : .89f;
                    builder.Box(new Vector3(0, -.5f + height * .5f, z),
                        new Vector3(.78f, height, length / boards * .93f), i % 2, .025f);
                }
                for (int rail = 0; rail < 2; rail++)
                    builder.Box(new Vector3(0, rail == 0 ? -.27f : .22f, 0),
                        new Vector3(.98f, .075f, length), 2, .014f);
            }
            else
            {
                // Offset masonry courses. Fine mortar gaps are actual geometry, not a texture.
                const int rows = 4;
                for (int row = 0; row < rows; row++)
                {
                    float offset = row % 2 == 0 ? 0f : -.5f;
                    for (int col = 0; col < 4; col++)
                    {
                        float left = Mathf.Max(0, (col + offset) / 3f);
                        float right = Mathf.Min(1, (col + offset + 1) / 3f);
                        if (right <= left) continue;
                        builder.Box(new Vector3(0, -.5f + (row + .5f) / rows * .9f,
                                -length * .5f + length * (left + right) * .5f),
                            new Vector3(.86f, .9f / rows - .012f, length * (right - left) - .009f),
                            (col + row) % 2, .016f);
                    }
                }
                builder.Box(new Vector3(0, .45f, 0), new Vector3(1f, .1f, length), 0, .018f);
                if (tier == WallTier.Reinforced)
                {
                    for (int rail = 0; rail < 2; rail++)
                        builder.Box(new Vector3(0, rail == 0 ? -.3f : .23f, 0),
                            new Vector3(1f, .085f, length), 2, .012f);
                    builder.Box(new Vector3(0, -.02f, 0), new Vector3(1f, .9f, .065f), 2, .009f);
                }
            }
            return builder.Build($"Wall {tier} panel");
        }

        public static Mesh CreatePost()
        {
            var b = new Builder();
            // Square columns sit exactly against each trimmed panel, one shared column per grid node.
            b.Box(Vector3.zero, new Vector3(1, 1, 1), 1, .045f);
            b.Box(new Vector3(0, .435f, 0), new Vector3(1, .13f, 1), 2, .025f);
            b.Box(new Vector3(0, -.445f, 0), new Vector3(1, .11f, 1), 2, .018f);
            return b.Build("Wall shared joint column");
        }

        public void Dispose()
        {
            foreach (var mesh in meshes.Values) Release(mesh);
            foreach (var palette in palettes.Values) foreach (var material in palette) Release(material);
            Release(post);
        }

        static void Release(UnityEngine.Object value)
        {
            if (value == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(value);
            else UnityEngine.Object.DestroyImmediate(value);
        }

        sealed class Builder
        {
            readonly List<Vector3> vertices = new();
            readonly List<int>[] triangles = { new(), new(), new() };

            public void Box(Vector3 center, Vector3 size, int material, float bevel)
            {
                var half = size * .5f;
                // A clipped rectangle in the horizontal plane gives readable, flat shaded bevels.
                float bx = Mathf.Min(bevel, half.x * .3f), bz = Mathf.Min(bevel, half.z * .3f);
                var ring = new[]
                {
                    new Vector2(-half.x + bx, -half.z), new Vector2(half.x - bx, -half.z),
                    new Vector2(half.x, -half.z + bz), new Vector2(half.x, half.z - bz),
                    new Vector2(half.x - bx, half.z), new Vector2(-half.x + bx, half.z),
                    new Vector2(-half.x, half.z - bz), new Vector2(-half.x, -half.z + bz)
                };
                for (int i = 0; i < 8; i++)
                {
                    var a = ring[i]; var c = ring[(i + 1) % 8];
                    Quad(center + new Vector3(a.x, -half.y, a.y), center + new Vector3(a.x, half.y, a.y),
                        center + new Vector3(c.x, half.y, c.y), center + new Vector3(c.x, -half.y, c.y), material);
                    Triangle(center + Vector3.up * half.y, center + new Vector3(c.x, half.y, c.y),
                        center + new Vector3(a.x, half.y, a.y), material);
                    Triangle(center - Vector3.up * half.y, center + new Vector3(a.x, -half.y, a.y),
                        center + new Vector3(c.x, -half.y, c.y), material);
                }
            }

            void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, int mat)
            {
                Triangle(a, b, c, mat); Triangle(a, c, d, mat);
            }

            void Triangle(Vector3 a, Vector3 b, Vector3 c, int mat)
            {
                int index = vertices.Count;
                vertices.Add(a); vertices.Add(b); vertices.Add(c);
                triangles[mat].Add(index); triangles[mat].Add(index + 1); triangles[mat].Add(index + 2);
            }

            public Mesh Build(string name)
            {
                var mesh = new Mesh { name = name, subMeshCount = 3 };
                mesh.SetVertices(vertices);
                for (int i = 0; i < 3; i++) mesh.SetTriangles(triangles[i], i);
                mesh.RecalculateNormals(); mesh.RecalculateBounds();
                return mesh;
            }
        }
    }
}
