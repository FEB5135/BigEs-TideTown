using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Tsunami
{
    /// <summary>
    /// Storm that announces the tsunami. Broken low-poly clouds in the map's style (flat-shaded, faceted, coloured
    /// from the team palette like the snowy mountains) wait white on the horizon on the side the wave will come
    /// from. In the last minute of the countdown (<see cref="TsunamiController.SecondsToRelease"/>) they drift very
    /// slowly towards the coast and grow; in the last 20 seconds they slowly darken, the sea under and beyond them
    /// darkens (the island keeps its light) and lightning flashes inside them, more often the closer the release.
    /// While a tsunami is announced or running the storm stays at full strength. Everything is built at runtime.
    /// </summary>
    public class TsunamiStorm : MonoBehaviour
    {
        [SerializeField] TsunamiController controller;
        [Tooltip("Clouds (Simple Lit, opaque, base map = the team palette). Each cloud gets its own instance for " +
                 "colour and flashes.")]
        [SerializeField] Material cloudMaterial;
        [Tooltip("Lightning bolts (unlit).")]
        [SerializeField] Material lightningMaterial;

        [Header("Timing (seconds before the release)")]
        [Tooltip("The clouds wait on the horizon until this many seconds are left, then drift very slowly in.")]
        [SerializeField, Min(1f)] float approachSeconds = 60f;
        [Tooltip("The clouds start to darken and lightning starts this many seconds before the release.")]
        [SerializeField, Min(1f)] float darkenSeconds = 20f;
        [Tooltip("How fast the shown storm follows a jump (Release now, Reset), per second. 0.1 = about 10 s.")]
        [SerializeField, Min(0.01f)] float catchUpRate = 0.1f;

        [Header("Clouds")]
        [SerializeField, Range(1, 60)] int cloudCount = 18;
        [Tooltip("Rounded lumps along each cloud (one or two more are stacked on top).")]
        [SerializeField] Vector2Int lumpsPerCloud = new Vector2Int(4, 7);
        [Tooltip("Height of the cloud bases above the ground (m).")]
        [SerializeField] Vector2 altitude = new Vector2(34f, 46f);
        [Tooltip("Width of the cloud bank across the approach axis (m).")]
        [SerializeField, Min(10f)] float bankWidth = 440f;
        [Tooltip("Depth of the cloud bank along the approach axis (m).")]
        [SerializeField, Min(10f)] float bankDepth = 140f;
        [Tooltip("Bank centre beyond the coast (m, along the approach axis) while waiting on the horizon...")]
        [SerializeField] float startBeyondCoast = 560f;
        [Tooltip("...and when the tsunami is released.")]
        [SerializeField] float endBeyondCoast = 80f;
        [SerializeField] Vector2 cloudScale = new Vector2(0.75f, 1.35f);
        [SerializeField] Color calmColor = new Color(0.96f, 0.97f, 0.99f);
        [SerializeField] Color stormColor = new Color(0.24f, 0.26f, 0.3f);
        [Tooltip("Share of the cloud colour that glows by itself, so shaded sides don't turn black.")]
        [SerializeField, Range(0f, 1f)] float selfLight = 0.4f;

        [Header("Stormy sea")]
        [Tooltip("Darkness of the sea under and behind the clouds at full storm (0..1).")]
        [SerializeField, Range(0f, 1f)] float seaShade = 0.5f;
        [Tooltip("How far beyond the bank the dark sea reaches (m).")]
        [SerializeField, Min(50f)] float shadeReach = 900f;

        [Header("Lightning")]
        [Tooltip("Seconds between flashes when lightning begins / at the release and while the tsunami runs.")]
        [SerializeField] Vector2 lightningInterval = new Vector2(6f, 1f);
        [Tooltip("Share of the flashes that also send a bolt down to the sea (the rest light up inside the cloud).")]
        [SerializeField, Range(0f, 1f)] float boltChance = 0.45f;
        [SerializeField, Min(0.05f)] float boltDuration = 0.3f;

        sealed class Cloud
        {
            public Transform transform;
            public Material material;
            public Mesh mesh;
            public Vector2 offset;      // along / across the approach axis, relative to the bank centre (m)
            public float height;
            public float scale;
            public float drift;         // phase of the slow sideways drift
            public float flash;         // 0..1, decays
        }

        readonly List<Cloud> clouds = new List<Cloud>();
        readonly List<LineRenderer> bolts = new List<LineRenderer>();
        readonly List<float> boltAges = new List<float>();
        Transform root;
        float shownApproach;        // 0 = waiting on the horizon .. 1 = at the coast (follows the countdown smoothly)
        float shownDark;            // 0 = white .. 1 = storm dark
        float nextStrike;
        // Bank frame, updated every frame by PlaceClouds.
        Vector3 bankCentre, outward, across;
        float seaY;

        void Start()
        {
            if (controller == null) controller = FindAnyObjectByType<TsunamiController>();
            if (controller == null || cloudMaterial == null)
            {
                Debug.LogError("[TsunamiStorm] Needs a TsunamiController and a cloud material.", this);
                enabled = false;
                return;
            }
            BuildClouds();
            shownApproach = ApproachTarget;
            shownDark = DarkTarget;
            nextStrike = Random.Range(1f, 3f);
        }

        void OnDestroy()
        {
            Shader.SetGlobalVector(StormShadeParamsId, Vector4.zero);
            foreach (Cloud c in clouds)
            {
                if (c.material != null) Destroy(c.material);
                if (c.mesh != null) Destroy(c.mesh);
            }
        }

        // Infinity (nothing scheduled) gives 0: calm.
        float ApproachTarget => Mathf.Clamp01(1f - controller.SecondsToRelease / approachSeconds);
        float DarkTarget => Mathf.Clamp01(1f - controller.SecondsToRelease / darkenSeconds);

        void Update()
        {
            if (controller == null || root == null) return;
            // The countdown moves slower than catchUpRate, so the clouds follow it exactly; jumps are smoothed.
            float step = catchUpRate * Time.deltaTime;
            shownApproach = Mathf.MoveTowards(shownApproach, ApproachTarget, step);
            shownDark = Mathf.MoveTowards(shownDark, DarkTarget, step);

            float darkness = Mathf.SmoothStep(0f, 1f, shownDark);
            PlaceClouds(Mathf.SmoothStep(0f, 1f, shownApproach), darkness);
            UpdateSeaShade(darkness);
            UpdateLightning();
        }

        // ---------------------------------------------------------------------------------------- clouds

        void BuildClouds()
        {
            root = new GameObject("Storm Clouds").transform;
            root.SetParent(transform, false);
            for (int i = 0; i < cloudCount; i++)
            {
                var cloudGO = new GameObject($"Cloud {i}", typeof(MeshFilter), typeof(MeshRenderer));
                cloudGO.transform.SetParent(root, false);
                var cloud = new Cloud
                {
                    transform = cloudGO.transform,
                    material = new Material(cloudMaterial) { name = $"Storm Cloud {i}" },
                    mesh = BuildCloudMesh(i),
                    // Spread over the bank, jittered on a loose grid so the clouds stay broken but cover it.
                    offset = new Vector2((Random.value - 0.5f) * bankDepth,
                                         ((i + Random.Range(0.1f, 0.9f)) / cloudCount - 0.5f) * bankWidth),
                    height = Random.Range(altitude.x, altitude.y),
                    scale = Random.Range(0.8f, 1.25f),
                    drift = Random.value * Mathf.PI * 2f
                };
                cloud.material.EnableKeyword("_EMISSION");
                cloudGO.GetComponent<MeshFilter>().sharedMesh = cloud.mesh;
                var r = cloudGO.GetComponent<MeshRenderer>();
                r.sharedMaterial = cloud.material;
                r.shadowCastingMode = ShadowCastingMode.On;
                r.receiveShadows = false;
                r.lightProbeUsage = LightProbeUsage.Off;
                r.reflectionProbeUsage = ReflectionProbeUsage.Off;
                clouds.Add(cloud);
            }
        }

        /// <param name="approach">0 = waiting on the horizon, 1 = at the coast.</param>
        /// <param name="darkness">0 = white, 1 = storm dark.</param>
        void PlaceClouds(float approach, float darkness)
        {
            Vector3 target = controller.MapAdapter != null ? controller.MapAdapter.ApproachTarget : Vector3.zero;
            Vector2 toLand = controller.TravelDirection;
            if (toLand.sqrMagnitude < 1e-4f) toLand = Vector2.up;
            outward = new Vector3(-toLand.x, 0f, -toLand.y).normalized;
            across = new Vector3(-outward.z, 0f, outward.x);
            float groundY = controller.MapAdapter != null && controller.MapAdapter.World != null
                ? controller.MapAdapter.World.transform.position.y : 0f;
            seaY = groundY + (controller.Simulation != null ? controller.Simulation.SeaLevel : 0f);
            float coast = CoastDistance(target, outward);
            float centre = coast + Mathf.Lerp(startBeyondCoast, endBeyondCoast, approach);
            bankCentre = target + outward * centre;
            bankCentre.y = groundY;
            float scale = Mathf.Lerp(cloudScale.x, cloudScale.y, approach);
            Color baseColor = Color.Lerp(calmColor, stormColor, darkness);
            // Each cloud's long side runs along the coast.
            Quaternion facing = Quaternion.LookRotation(outward, Vector3.up);
            float t = Time.time;

            foreach (Cloud c in clouds)
            {
                float side = c.offset.y + Mathf.Sin(t * 0.05f + c.drift) * 6f;
                c.transform.position = bankCentre + outward * c.offset.x + across * side +
                                       Vector3.up * (c.height + Mathf.Sin(t * 0.13f + c.drift) * 0.6f);
                c.transform.rotation = facing;
                c.transform.localScale = Vector3.one * (c.scale * scale);
                c.flash = Mathf.MoveTowards(c.flash, 0f, Time.deltaTime * 4f);
                Color col = Color.Lerp(baseColor, new Color(0.88f, 0.9f, 1f), c.flash);
                c.material.SetColor(BaseColor, col);
                if (c.material.HasProperty(ColorId)) c.material.SetColor(ColorId, col);
                c.material.SetColor(EmissionId, col * (selfLight + c.flash * 1.2f));
            }
        }

        /// <summary>Distance from the target to the World's edge along <paramref name="dir"/> (the coast).</summary>
        float CoastDistance(Vector3 from, Vector3 dir)
        {
            if (controller.MapAdapter == null || controller.MapAdapter.World == null) return 130f;
            Rect r = controller.MapAdapter.MapRectWorld;
            float t = float.MaxValue;
            if (dir.x > 1e-4f) t = Mathf.Min(t, (r.xMax - from.x) / dir.x);
            if (dir.x < -1e-4f) t = Mathf.Min(t, (r.xMin - from.x) / dir.x);
            if (dir.z > 1e-4f) t = Mathf.Min(t, (r.yMax - from.z) / dir.z);
            if (dir.z < -1e-4f) t = Mathf.Min(t, (r.yMin - from.z) / dir.z);
            return t == float.MaxValue ? 130f : Mathf.Max(0f, t);
        }

        // ------------------------------------------------------------------------------------- stormy sea

        // The water shader (StylizedWater) darkens the sea itself under the bank and out to the horizon behind it,
        // fading towards the coast and at the sides. Being part of the water, the darkening lies under the foam of
        // an incoming wave instead of over it, and is the same on the open ocean, the simulated sea and the swell.
        // Water on land is never darkened.
        void UpdateSeaShade(float darkness)
        {
            float near = bankDepth * 1.1f;
            Shader.SetGlobalVector(StormShadeId, new Vector4(bankCentre.x, bankCentre.z, outward.x, outward.z));
            Shader.SetGlobalVector(StormShadeParamsId, new Vector4(seaShade * darkness, near, bankWidth * 0.7f, shadeReach));
        }

        // ------------------------------------------------------------------------------------- lightning

        void UpdateLightning()
        {
            for (int i = bolts.Count - 1; i >= 0; i--)
            {
                boltAges[i] += Time.deltaTime;
                float age = boltAges[i] / boltDuration;
                // Two quick flickers, then gone.
                bool on = age < 1f && (age < 0.3f || (age > 0.45f && age < 0.75f));
                bolts[i].enabled = on;
            }

            // Lightning comes with the darkening: from the last 20 s, more and more often.
            if (shownDark <= 0f || clouds.Count == 0) return;
            nextStrike -= Time.deltaTime;
            if (nextStrike > 0f) return;
            float interval = Mathf.Lerp(lightningInterval.x, lightningInterval.y, shownDark);
            nextStrike = interval * Random.Range(0.5f, 1.5f);
            Strike();
        }

        // Lightning lives in the clouds: the struck cloud (and a little its neighbours) lights up from inside; some
        // flashes also drop a bolt from its base to the sea. Bolts are only drawn under clouds the camera can see,
        // so no line cuts across the screen from a cloud out of view. Nothing else on screen flashes.
        void Strike()
        {
            Cloud cloud = clouds[Random.Range(0, clouds.Count)];
            cloud.flash = 1f;
            foreach (Cloud other in clouds)
                if (other != cloud && (other.transform.position - cloud.transform.position).sqrMagnitude < 45f * 45f)
                    other.flash = Mathf.Max(other.flash, 0.45f);
            if (lightningMaterial == null || Random.value > boltChance || !CloudInView(cloud)) return;

            LineRenderer bolt = GetBolt();
            Vector3 top = cloud.transform.position + Vector3.down * 0.5f;
            Vector3 bottom = new Vector3(top.x + Random.Range(-8f, 8f), seaY, top.z + Random.Range(-8f, 8f));
            const int segments = 14;
            bolt.positionCount = segments + 1;
            for (int i = 0; i <= segments; i++)
            {
                float f = i / (float)segments;
                Vector3 p = Vector3.Lerp(top, bottom, f);
                if (i > 0 && i < segments)
                {
                    float jag = 3.2f * Mathf.Sin(f * Mathf.PI);
                    p += new Vector3(Random.Range(-jag, jag), Random.Range(-0.8f, 0.8f), Random.Range(-jag, jag));
                }
                bolt.SetPosition(i, p);
            }
            float width = Random.Range(0.5f, 0.9f);
            bolt.startWidth = width;
            bolt.endWidth = width * 0.35f;
            bolt.enabled = true;
            boltAges[bolts.IndexOf(bolt)] = 0f;
        }

        static bool CloudInView(Cloud cloud)
        {
            Camera cam = Camera.main;
            if (cam == null) return false;
            Vector3 top = cam.WorldToViewportPoint(cloud.transform.position);
            Vector3 bottom = cam.WorldToViewportPoint(cloud.transform.position + Vector3.down * 40f);
            return top.z > 0f && top.x > 0.02f && top.x < 0.98f && top.y > 0.02f && top.y < 0.98f &&
                   bottom.z > 0f && bottom.y > 0.02f;
        }

        LineRenderer GetBolt()
        {
            for (int i = 0; i < bolts.Count; i++)
                if (boltAges[i] >= boltDuration) return bolts[i];
            var go = new GameObject("Lightning", typeof(LineRenderer));
            go.transform.SetParent(transform, false);
            var lr = go.GetComponent<LineRenderer>();
            lr.sharedMaterial = lightningMaterial;
            lr.useWorldSpace = true;
            lr.numCornerVertices = 0;
            lr.numCapVertices = 0;
            lr.alignment = LineAlignment.View;
            lr.shadowCastingMode = ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.enabled = false;
            bolts.Add(lr);
            boltAges.Add(boltDuration);
            return lr;
        }

        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        static readonly int ColorId = Shader.PropertyToID("_Color");
        static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");
        static readonly int StormShadeId = Shader.PropertyToID("_TsunamiStormShade");
        static readonly int StormShadeParamsId = Shader.PropertyToID("_TsunamiStormShadeParams");

        // ------------------------------------------------------------------------------------------ mesh

        // Team palette cells (64 x 64 texture, like the trees and mountains): the snow column runs from white to a
        // pale cyan, the next columns are pure white.
        static readonly Vector2 TopUV = PaletteUV(8, 2);      // white
        static readonly Vector2 SideUV = PaletteUV(6, 12);    // snow white
        static readonly Vector2 BottomUV = PaletteUV(6, 40);  // pale cyan underside

        static Vector2 PaletteUV(int column, int row) => new Vector2((column + 0.5f) / 64f, 1f - (row + 0.5f) / 64f);

        /// <summary>
        /// One cumulus cloud in the map's low-poly style: faceted lumps (jittered icospheres) along its length, big
        /// in the middle and small at the ends, one or two more stacked on top, all cut flat at the base (local
        /// y = 0). Flat shaded, coloured by face direction from the palette: white tops, snow-white sides, a pale
        /// cyan underside. Long side along local X.
        /// </summary>
        Mesh BuildCloudMesh(int index)
        {
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            int lumps = Random.Range(lumpsPerCloud.x, lumpsPerCloud.y + 1);
            float halfLength = lumps * Random.Range(3.4f, 4.2f);
            float tallest = 0f;
            for (int p = 0; p < lumps; p++)
            {
                float u = lumps == 1 ? 0f : p / (lumps - 1f) * 2f - 1f;
                u = Mathf.Clamp(u + Random.Range(-0.08f, 0.08f), -1f, 1f);
                float middle = 1f - Mathf.Abs(u);
                float r = Mathf.Lerp(4.5f, 9f, middle) * Random.Range(0.85f, 1.15f);
                float y = r * Mathf.Lerp(0.1f, 0.45f, middle);
                var centre = new Vector3(u * halfLength, y, (p % 2 == 0 ? -1f : 1f) * Random.Range(0.5f, 3f));
                AddLump(verts, uvs, tris, centre, new Vector3(r * 1.35f, r * 0.8f, r * Random.Range(0.85f, 1.05f)));
                tallest = Mathf.Max(tallest, y + r * 0.8f);
            }
            int tops = Random.Range(1, 3);
            for (int p = 0; p < tops; p++)
            {
                float r = Random.Range(4.5f, 7f);
                var centre = new Vector3(Random.Range(-0.35f, 0.35f) * halfLength, tallest - r * 0.35f, Random.Range(-1.5f, 1.5f));
                AddLump(verts, uvs, tris, centre, new Vector3(r * 1.25f, r * 0.85f, r));
            }
            var mesh = new Mesh { name = $"Storm Cloud {index} (generated)" };
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals(); // every triangle has its own vertices: flat shading
            mesh.RecalculateBounds();
            return mesh;
        }

        static void AddLump(List<Vector3> verts, List<Vector2> uvs, List<int> tris, Vector3 centre, Vector3 radii)
        {
            Vector3[] unit = UnitIcosphere;
            var p = new Vector3[unit.Length];
            for (int i = 0; i < unit.Length; i++)
            {
                // Jitter each corner a little (shared by its faces, so the lump stays closed): faceted, not round.
                Vector3 q = centre + Vector3.Scale(unit[i] * Random.Range(0.88f, 1.12f), radii);
                if (q.y < 0f) q.y = 0f; // flat cloud base
                p[i] = q;
            }
            int[] faces = UnitIcosphereFaces;
            for (int f = 0; f < faces.Length; f += 3)
            {
                Vector3 a = p[faces[f]], b = p[faces[f + 1]], c = p[faces[f + 2]];
                Vector3 n = Vector3.Cross(b - a, c - a);
                float area = n.magnitude;
                if (area < 1e-4f) continue; // collapsed on the flat base
                n /= area;
                Vector2 uv = n.y > 0.35f ? TopUV : n.y > -0.45f ? SideUV : BottomUV;
                int k = verts.Count;
                verts.Add(a); verts.Add(b); verts.Add(c);
                uvs.Add(uv); uvs.Add(uv); uvs.Add(uv);
                tris.Add(k); tris.Add(k + 1); tris.Add(k + 2);
            }
        }

        static Vector3[] unitIcosphere;
        static int[] unitIcosphereFaces;
        static Vector3[] UnitIcosphere { get { if (unitIcosphere == null) BuildUnitIcosphere(); return unitIcosphere; } }
        static int[] UnitIcosphereFaces { get { if (unitIcosphereFaces == null) BuildUnitIcosphere(); return unitIcosphereFaces; } }

        /// <summary>Once-subdivided icosahedron with shared corners (42 corners, 80 faces), unit radius.</summary>
        static void BuildUnitIcosphere()
        {
            float t = (1f + Mathf.Sqrt(5f)) / 2f;
            var v = new List<Vector3>
            {
                new Vector3(-1, t, 0), new Vector3(1, t, 0), new Vector3(-1, -t, 0), new Vector3(1, -t, 0),
                new Vector3(0, -1, t), new Vector3(0, 1, t), new Vector3(0, -1, -t), new Vector3(0, 1, -t),
                new Vector3(t, 0, -1), new Vector3(t, 0, 1), new Vector3(-t, 0, -1), new Vector3(-t, 0, 1)
            };
            for (int i = 0; i < v.Count; i++) v[i] = v[i].normalized;
            int[] f =
            {
                0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
                3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1
            };
            var midpoints = new Dictionary<long, int>();
            int Mid(int a, int b)
            {
                long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                if (midpoints.TryGetValue(key, out int m)) return m;
                v.Add((v[a] + v[b]).normalized);
                midpoints[key] = v.Count - 1;
                return v.Count - 1;
            }
            var faces = new List<int>();
            for (int i = 0; i < f.Length; i += 3)
            {
                int a = f[i], b = f[i + 1], c = f[i + 2];
                int ab = Mid(a, b), bc = Mid(b, c), ca = Mid(c, a);
                faces.AddRange(new[] { a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca });
            }
            unitIcosphere = v.ToArray();
            unitIcosphereFaces = faces.ToArray();
        }
    }
}
