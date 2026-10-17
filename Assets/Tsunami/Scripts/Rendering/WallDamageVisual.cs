using System.Collections.Generic;
using UnityEngine;

namespace Tsunami
{
    /// <summary>Cracked block/slat representation; intact physics stays authoritative until structural failure.</summary>
    public sealed class WallDamageVisual : MonoBehaviour
    {
        readonly List<GameObject> fragments = new List<GameObject>(12);
        readonly List<Vector3> restPositions = new List<Vector3>(12);
        readonly List<Vector3> sizes = new List<Vector3>(12);
        Renderer[] sources;
        float damage;
        public Renderer[] SourceRenderers => sources ??= GetComponentsInChildren<Renderer>(true);
        public int FragmentCount => fragments.Count;

        public void SetDamage(float fraction)
        {
            damage = Mathf.Clamp01(fraction);
            if (damage < .15f)
            {
                foreach (var source in SourceRenderers) if (source) source.enabled = true;
                foreach (var fragment in fragments) if (fragment) fragment.SetActive(false);
                return;
            }
            EnsureFragments();
            foreach (var source in SourceRenderers) if (source) source.enabled = false;
            int stage = damage < .4f ? 1 : damage < .7f ? 2 : 3;
            for (int i = 0; i < fragments.Count; i++)
            {
                var piece = fragments[i];
                if (!piece) continue;
                piece.SetActive(true);
                float sign = i % 2 == 0 ? 1f : -1f;
                piece.transform.localPosition = restPositions[i] + new Vector3(sign * stage * .018f, 0f, 0f);
                piece.transform.localRotation = Quaternion.Euler(sign * stage * .7f, 0f, sign * stage * .6f);
                piece.transform.localScale = Vector3.Scale(sizes[i],
                    Vector3.one * (1f - stage * .015f));
            }
        }

        void EnsureFragments()
        {
            if (fragments.Count > 0) return;
            var originals = SourceRenderers;
            if (originals.Length == 0) return;
            Vector3 min = Vector3.positiveInfinity, max = Vector3.negativeInfinity;
            foreach (var source in originals)
            {
                var b = source.localBounds;
                for (int c = 0; c < 8; c++)
                {
                    var p = transform.InverseTransformPoint(source.transform.TransformPoint(new Vector3((c & 1) == 0 ? b.min.x : b.max.x,
                        (c & 2) == 0 ? b.min.y : b.max.y, (c & 4) == 0 ? b.min.z : b.max.z)));
                    min = Vector3.Min(min, p); max = Vector3.Max(max, p);
                }
            }
            var size = max - min;
            for (int row = 0; row < 3; row++)
            for (int column = 0; column < 4; column++)
            {
                var piece = GameObject.CreatePrimitive(PrimitiveType.Cube);
                piece.name = "Wall fracture " + fragments.Count;
                piece.transform.SetParent(transform, false);
                var p = min + new Vector3(size.x * .5f, size.y * (row + .5f) / 3f, size.z * (column + .5f) / 4f);
                piece.transform.localPosition = p;
                var pieceSize = new Vector3(size.x, size.y / 3f, size.z / 4f);
                piece.transform.localScale = pieceSize * .985f;
                sizes.Add(pieceSize);
                piece.GetComponent<Renderer>().sharedMaterial = originals[0].sharedMaterial;
                piece.GetComponent<Collider>().enabled = false;
                fragments.Add(piece); restPositions.Add(p);
            }
        }

        public IReadOnlyList<GameObject> DetachFragments()
        {
            EnsureFragments();
            foreach (var source in SourceRenderers) if (source) source.enabled = false;
            foreach (var piece in fragments)
            {
                if (!piece) continue;
                piece.SetActive(true);
                piece.transform.SetParent(null, true);
                piece.GetComponent<Collider>().enabled = true;
            }
            return fragments;
        }
    }

}
