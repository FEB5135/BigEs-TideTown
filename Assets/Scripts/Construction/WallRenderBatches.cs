using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace IdleTown.Construction
{
    // Only intact procedural art is batched. The source objects still own selection,
    // colliders, barrier bounds and the transition to separate fracture art.
    internal sealed class WallRenderBatches : IDisposable
    {
        const int ChunkSize = 8;
        const int MinimumSources = 4;
        const int RebuildsPerFrame = 2;

        sealed class Source
        {
            public MeshRenderer Renderer;
            public MeshFilter Filter;
            public Material[] Materials;
            public bool Visible;
            public bool IsVisible => Renderer != null && Renderer.enabled && Renderer.gameObject.activeInHierarchy;
        }

        sealed class Chunk
        {
            public readonly Dictionary<GameObject, Source> Sources = new();
            public readonly List<Mesh> Meshes = new();
            public GameObject Output;
            public bool Dirty = true;

            public void Invalidate()
            {
                if (Dirty) return;
                Dirty = true;
                if (Output != null) Output.SetActive(false);
                foreach (var source in Sources.Values)
                    if (source.Renderer != null) source.Renderer.forceRenderingOff = false;
            }

            public void ClearOutput()
            {
                if (Output != null)
                {
                    Output.SetActive(false);
                    UnityEngine.Object.Destroy(Output);
                }
                Output = null;
                foreach (var mesh in Meshes) UnityEngine.Object.Destroy(mesh);
                Meshes.Clear();
            }
        }

        readonly Transform parent;
        readonly Dictionary<Vector2Int, Chunk> chunks = new();
        readonly Dictionary<GameObject, Chunk> owners = new();
        readonly Dictionary<Material, List<CombineInstance>> groups = new();

        public WallRenderBatches(Transform parent) => this.parent = parent;

        public void Set(GameObject root, Vector2Int node)
        {
            if (!owners.TryGetValue(root, out var chunk))
            {
                var key = new Vector2Int(Mathf.FloorToInt(node.x / (float)ChunkSize),
                    Mathf.FloorToInt(node.y / (float)ChunkSize));
                if (!chunks.TryGetValue(key, out chunk)) chunks.Add(key, chunk = new Chunk());
                owners.Add(root, chunk);
                var filter = root.GetComponentInChildren<MeshFilter>();
                chunk.Sources.Add(root, new Source { Filter = filter, Renderer = filter.GetComponent<MeshRenderer>() });
            }

            chunk.Invalidate();
            var source = chunk.Sources[root];
            source.Materials = source.Renderer.sharedMaterials;
            source.Visible = source.IsVisible;
        }

        public void Remove(GameObject root)
        {
            if (!owners.Remove(root, out var chunk)) return;
            chunk.Invalidate();
            var renderer = chunk.Sources[root].Renderer;
            if (renderer != null) renderer.forceRenderingOff = false;
            chunk.Sources.Remove(root);
        }

        public void Update()
        {
            // Damage visuals switch the original renderer off independently of construction.
            // Invalidate before drawing so an old batch never covers a newly fractured panel.
            foreach (var chunk in chunks.Values)
            {
                foreach (var source in chunk.Sources.Values)
                {
                    bool visible = source.IsVisible;
                    if (visible == source.Visible) continue;
                    source.Visible = visible;
                    chunk.Invalidate();
                }
            }

            int remaining = RebuildsPerFrame;
            foreach (var chunk in chunks.Values)
            {
                if (!chunk.Dirty) continue;
                if (remaining-- <= 0) break;
                Rebuild(chunk);
            }
        }

        void Rebuild(Chunk chunk)
        {
            chunk.ClearOutput();
            chunk.Dirty = false;
            int visible = 0;
            foreach (var source in chunk.Sources.Values)
                if (source.Visible) visible++;
            if (visible < MinimumSources) return;

            Matrix4x4 toLocal = parent.worldToLocalMatrix;
            foreach (var source in chunk.Sources.Values)
            {
                if (!source.Visible) continue;
                Mesh mesh = source.Filter.sharedMesh;
                Matrix4x4 transform = toLocal * source.Filter.transform.localToWorldMatrix;
                for (int i = 0; i < mesh.subMeshCount; i++)
                {
                    if (mesh.GetIndexCount(i) == 0) continue;
                    Material material = source.Materials[i];
                    if (!groups.TryGetValue(material, out var parts))
                        groups.Add(material, parts = new List<CombineInstance>());
                    parts.Add(new CombineInstance { mesh = mesh, subMeshIndex = i, transform = transform });
                }
            }

            chunk.Output = new GameObject("Wall Render Batch");
            chunk.Output.transform.SetParent(parent, false);
            foreach (var group in groups)
            {
                if (group.Value.Count == 0) continue;
                var mesh = new Mesh { name = "Combined Wall Art", indexFormat = IndexFormat.UInt32 };
                mesh.CombineMeshes(group.Value.ToArray(), true, true);
                mesh.UploadMeshData(true);
                group.Value.Clear();
                chunk.Meshes.Add(mesh);
                var output = new GameObject(group.Key.name, typeof(MeshFilter), typeof(MeshRenderer));
                output.transform.SetParent(chunk.Output.transform, false);
                output.GetComponent<MeshFilter>().sharedMesh = mesh;
                output.GetComponent<MeshRenderer>().sharedMaterial = group.Key;
            }

            foreach (var source in chunk.Sources.Values)
                if (source.Visible) source.Renderer.forceRenderingOff = true;
        }

        public void Dispose()
        {
            foreach (var chunk in chunks.Values)
            {
                chunk.Invalidate();
                chunk.ClearOutput();
            }
        }
    }
}
