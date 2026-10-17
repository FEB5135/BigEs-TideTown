using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Tsunami
{
    /// <summary>
    /// Storm that announces the tsunami. Broken low-poly clouds wait far out at sea on the side the wave will come
    /// from and drift slowly towards the coast as <see cref="TsunamiController.StormProgress"/> rises; on the way
    /// they grow and turn from white to dark grey, the sea under and beyond them darkens (the island keeps its
    /// light), and lightning starts to flash in the clouds, more often the closer the release. While a tsunami is
    /// running the storm stays at full strength. Everything is built at runtime.
    /// </summary>
    public class TsunamiStorm : MonoBehaviour
    {
        [SerializeField] TsunamiController controller;
        [Tooltip("Clouds (Simple Lit, opaque). Each cloud gets its own instance for colour and flashes.")]
        [SerializeField] Material cloudMaterial;
        [Tooltip("Lightning bolts (unlit).")]
        [SerializeField] Material lightningMaterial;
        [Tooltip("Shadow over the stormy sea: a transparent shader that uses vertex colours (e.g. Sprites/Default).")]
        [SerializeField] Material seaShadeMaterial;

        [Header("Clouds")]
        [SerializeField, Range(1, 60)] int cloudCount = 18;
        [SerializeField] Vector2Int puffsPerCloud = new Vector2Int(5, 9);
        [Tooltip("Height of the cloud bases above the ground (m).")]
        [SerializeField] Vector2 altitude = new Vector2(34f, 46f);
        [Tooltip("Width of the cloud bank across the approach axis (m).")]
        [SerializeField, Min(10f)] float bankWidth = 440f;
        [Tooltip("Depth of the cloud bank along the approach axis (m).")]
        [SerializeField, Min(10f)] float bankDepth = 140f;
        [Tooltip("Bank centre beyond the coast (m, along the approach axis) when the storm starts...")]
        [SerializeField] float startBeyondCoast = 560f;
        [Tooltip("...and when the tsunami is released.")]
        [SerializeField] float endBeyondCoast = 80f;
        [SerializeField] Vector2 cloudScale = new Vector2(0.75f, 1.35f);
        [SerializeField] Color calmColor = new Color(0.96f, 0.97f, 0.99f);
        [SerializeField] Color stormColor = new Color(0.24f, 0.26f, 0.3f);
        [Tooltip("Share of the cloud colour that glows by itself, so shaded sides don't turn black.")]
        [SerializeField, Range(0f, 1f)] float selfLight = 0.4f;
        [Tooltip("Storm progress at which the clouds start to darken and where they are fully dark.")]
        [SerializeField] Vector2 darkenRange = new Vector2(0.4f, 0.95f);
        [Tooltip("How fast the shown storm follows a jump (Release now, Reset), progress per second. " +
                 "0.1 = the clouds take about 10 s to come in, the length of the warning.")]
        [SerializeField, Min(0.01f)] float catchUpRate = 0.1f;

        [Header("Stormy sea")]
        [Tooltip("Darkness of the sea under and behind the clouds at full storm (0..1).")]
        [SerializeField, Range(0f, 1f)] float seaShade = 0.5f;
        [Tooltip("How far beyond the bank the dark sea reaches (m).")]
        [SerializeField, Min(50f)] float shadeReach = 900f;

        [Header("Lightning")]
        [Tooltip("Storm progress from which lightning flashes.")]
        [SerializeField, Range(0f, 1f)] float lightningFrom = 0.55f;
        [Tooltip("Seconds between flashes when lightning begins / at the release and while the tsunami runs.")]
        [SerializeField] Vector2 lightningInterval = new Vector2(6f, 1f);
        [Tooltip("Share of the flashes that also send a bolt down to the sea (the rest light up inside the cloud).")]
        [SerializeField, Range(0f, 1f)] float boltChance = 0.45f;
        [SerializeField, Min(0.05f)] float boltDuration = 0.3f;

        sealed class Cloud
        {
            public Transform transform;
            public Material material;
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
        Mesh puffMesh;
        float shown;                // displayed storm progress (follows the controller smoothly)
        float nextStrike;
        // Bank frame, updated every frame by PlaceClouds.
        Vector3 bankCentre, outward, across;
        float seaY;

        MeshRenderer shadeRenderer;
        Mesh shadeMesh;
        Material shadeMaterial;
        Color[] shadeColors;
        float[] shadeWeights;
        float shownShade = -1f;
        const int ShadeAlong = 24, ShadeAcross = 16;

        public float ShownProgress => shown;

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
            BuildSeaShade();
            shown = controller.StormProgress;
            nextStrike = Random.Range(1f, 3f);
        }

        void OnDestroy()
        {
            if (puffMesh != null) Destroy(puffMesh);
            if (shadeMesh != null) Destroy(shadeMesh);
            if (shadeMaterial != null) Destroy(shadeMaterial);
            foreach (Cloud c in clouds) if (c.material != null) Destroy(c.material);
        }

        void Update()
        {
            if (controller == null || root == null) return;
            float target = controller.StormProgress;
            // A countdown moves far slower than catchUpRate, so the clouds follow it exactly; jumps are smoothed.
            shown = Mathf.MoveTowards(shown, target, catchUpRate * Time.deltaTime);

            float darkness = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(darkenRange.x, darkenRange.y, shown));
            PlaceClouds(darkness);
            UpdateSeaShade(darkness);
            UpdateLightning();
        }

        // ---------------------------------------------------------------------------------------- clouds

        void BuildClouds()
        {
            root = new GameObject("Storm Clouds").transform;
            root.SetParent(transform, false);
            puffMesh = CreateFlatIcosphere();
            for (int i = 0; i < cloudCount; i++)
            {
                var cloudGO = new GameObject($"Cloud {i}");
                cloudGO.transform.SetParent(root, false);
                var cloud = new Cloud
                {
                    transform = cloudGO.transform,
                    material = new Material(cloudMaterial) { name = $"Storm Cloud {i}" },
                    // Spread over the bank, jittered on a loose grid so the clouds stay broken but cover it.
                    offset = new Vector2((Random.value - 0.5f) * bankDepth,
                                         ((i + Random.Range(0.1f, 0.9f)) / cloudCount - 0.5f) * bankWidth),
                    height = Random.Range(altitude.x, altitude.y),
                    scale = Random.Range(0.8f, 1.25f),
                    drift = Random.value * Mathf.PI * 2f
                };
                cloud.material.EnableKeyword("_EMISSION");
                int puffs = Random.Range(puffsPerCloud.x, puffsPerCloud.y + 1);
                float halfLength = puffs * 3.2f;
                for (int p = 0; p < puffs; p++)
                {
                    // Wide, flat-bottomed cloud: big puffs in the middle stacked a little higher, small ones at the
                    // ends, a second row offset behind so it isn't a string of balls.
                    float u = puffs == 1 ? 0f : p / (puffs - 1f) * 2f - 1f;
                    float middle = 1f - Mathf.Abs(u);
                    float size = Mathf.Lerp(8f, 16f, middle) * Random.Range(0.85f, 1.15f);
                    float height = size * Random.Range(0.38f, 0.48f);
                    var puff = new GameObject("Puff", typeof(MeshFilter), typeof(MeshRenderer));
                    puff.transform.SetParent(cloudGO.transform, false);
                    puff.transform.localPosition = new Vector3(u * halfLength + Random.Range(-1.5f, 1.5f),
                        height * 0.5f + middle * Random.Range(0.5f, 2.5f), (p % 2 == 0 ? -1f : 1f) * Random.Range(1f, 4f));
                    puff.transform.localRotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
                    puff.transform.localScale = new Vector3(size * Random.Range(1.2f, 1.5f), height, size * Random.Range(0.8f, 1f));
                    puff.GetComponent<MeshFilter>().sharedMesh = puffMesh;
                    var r = puff.GetComponent<MeshRenderer>();
                    r.sharedMaterial = cloud.material;
                    r.shadowCastingMode = ShadowCastingMode.On;
                    r.receiveShadows = false;
                    r.lightProbeUsage = LightProbeUsage.Off;
                    r.reflectionProbeUsage = ReflectionProbeUsage.Off;
                }
                clouds.Add(cloud);
            }
        }

        void PlaceClouds(float darkness)
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
            // Steady drift towards the coast from the first second, slowing a little only at the very end.
            float travel = shown * (1.3f - 0.3f * shown);
            float centre = coast + Mathf.Lerp(startBeyondCoast, endBeyondCoast, travel);
            bankCentre = target + outward * centre;
            bankCentre.y = groundY;
            float scale = Mathf.Lerp(cloudScale.x, cloudScale.y, shown);
            Color baseColor = Color.Lerp(calmColor, stormColor, darkness);
            Quaternion facing = Quaternion.LookRotation(across, Vector3.up);
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

        // A flat, transparent dark sheet just above the sea under the bank and out to the horizon behind it. It
        // fades out towards the coast and at the sides. The island is higher than the sheet, so the depth test
        // keeps it off the land: only the sea darkens.
        void BuildSeaShade()
        {
            if (seaShadeMaterial == null) return;
            var go = new GameObject("Stormy Sea", typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(transform, false);
            shadeRenderer = go.GetComponent<MeshRenderer>();
            shadeMaterial = new Material(seaShadeMaterial) { name = seaShadeMaterial.name + " (Storm)" };
            // After the ocean (transparent, no depth write), before the incoming swell.
            shadeMaterial.renderQueue = Mathf.Max(seaShadeMaterial.renderQueue, (int)RenderQueue.Transparent) + 1;
            shadeRenderer.sharedMaterial = shadeMaterial;
            shadeRenderer.shadowCastingMode = ShadowCastingMode.Off;
            shadeRenderer.receiveShadows = false;
            shadeRenderer.lightProbeUsage = LightProbeUsage.Off;
            shadeRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;

            int count = ShadeAlong * ShadeAcross;
            var verts = new Vector3[count];
            shadeColors = new Color[count];
            shadeWeights = new float[count];
            float near = bankDepth * 1.1f, halfWidth = bankWidth * 0.7f;
            for (int a = 0; a < ShadeAlong; a++)
            {
                // Denser near the bank, sparse out to the horizon.
                float f = a / (ShadeAlong - 1f);
                float along = f < 0.5f ? Mathf.Lerp(-near, bankDepth * 0.6f, f * 2f)
                                       : Mathf.Lerp(bankDepth * 0.6f, shadeReach, (f - 0.5f) * 2f);
                float towardCoast = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-near, -near * 0.15f, along));
                // The dark area widens a little behind the bank.
                float width = halfWidth * Mathf.Lerp(0.75f, 1f, Mathf.InverseLerp(0f, shadeReach, along));
                for (int b = 0; b < ShadeAcross; b++)
                {
                    float side = (b / (ShadeAcross - 1f) * 2f - 1f) * halfWidth;
                    float sides = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(width * 0.55f, width, Mathf.Abs(side)));
                    int i = a * ShadeAcross + b;
                    verts[i] = new Vector3(side, 0f, along);
                    shadeWeights[i] = towardCoast * sides;
                }
            }
            var tris = new List<int>();
            for (int a = 0; a < ShadeAlong - 1; a++)
            {
                for (int b = 0; b < ShadeAcross - 1; b++)
                {
                    int i = a * ShadeAcross + b;
                    tris.Add(i); tris.Add(i + ShadeAcross); tris.Add(i + 1);
                    tris.Add(i + 1); tris.Add(i + ShadeAcross); tris.Add(i + ShadeAcross + 1);
                }
            }
            shadeMesh = new Mesh { name = "Stormy Sea (generated)" };
            shadeMesh.MarkDynamic();
            shadeMesh.vertices = verts;
            shadeMesh.SetTriangles(tris, 0);
            shadeMesh.colors = shadeColors;
            shadeMesh.RecalculateNormals();
            shadeMesh.RecalculateBounds();
            go.GetComponent<MeshFilter>().sharedMesh = shadeMesh;
        }

        void UpdateSeaShade(float darkness)
        {
            if (shadeRenderer == null) return;
            float strength = seaShade * darkness;
            shadeRenderer.enabled = strength > 0.005f;
            // Local +Z points out to sea, so the sheet trails out behind the bank.
            shadeRenderer.transform.SetPositionAndRotation(new Vector3(bankCentre.x, seaY + 0.15f, bankCentre.z),
                Quaternion.LookRotation(outward, Vector3.up));
            if (Mathf.Abs(strength - shownShade) < 0.002f) return;
            shownShade = strength;
            var tint = new Color(0.03f, 0.05f, 0.09f, 0f);
            for (int i = 0; i < shadeColors.Length; i++)
            {
                tint.a = strength * shadeWeights[i];
                shadeColors[i] = tint;
            }
            shadeMesh.colors = shadeColors;
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

            if (shown < lightningFrom || clouds.Count == 0) return;
            nextStrike -= Time.deltaTime;
            if (nextStrike > 0f) return;
            float strength = Mathf.InverseLerp(lightningFrom, 1f, shown);
            float interval = Mathf.Lerp(lightningInterval.x, lightningInterval.y, strength);
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

        // ------------------------------------------------------------------------------------------ mesh

        /// <summary>Once-subdivided icosahedron with flat normals (low-poly puff), unit diameter.</summary>
        static Mesh CreateFlatIcosphere()
        {
            float t = (1f + Mathf.Sqrt(5f)) / 2f;
            var v = new List<Vector3>
            {
                new Vector3(-1, t, 0), new Vector3(1, t, 0), new Vector3(-1, -t, 0), new Vector3(1, -t, 0),
                new Vector3(0, -1, t), new Vector3(0, 1, t), new Vector3(0, -1, -t), new Vector3(0, 1, -t),
                new Vector3(t, 0, -1), new Vector3(t, 0, 1), new Vector3(-t, 0, -1), new Vector3(-t, 0, 1)
            };
            int[] f =
            {
                0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
                3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1
            };
            for (int i = 0; i < v.Count; i++) v[i] = v[i].normalized * 0.5f;
            var verts = new List<Vector3>();
            var tris = new List<int>();
            for (int i = 0; i < f.Length; i += 3)
            {
                Vector3 a = v[f[i]], b = v[f[i + 1]], c = v[f[i + 2]];
                Vector3 ab = (a + b).normalized * 0.5f, bc = (b + c).normalized * 0.5f, ca = (c + a).normalized * 0.5f;
                AddTri(verts, tris, a, ab, ca);
                AddTri(verts, tris, b, bc, ab);
                AddTri(verts, tris, c, ca, bc);
                AddTri(verts, tris, ab, bc, ca);
            }
            var mesh = new Mesh { name = "Storm Cloud Puff" };
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals(); // every triangle has its own vertices: flat shading
            mesh.RecalculateBounds();
            return mesh;
        }

        static void AddTri(List<Vector3> verts, List<int> tris, Vector3 a, Vector3 b, Vector3 c)
        {
            int i = verts.Count;
            verts.Add(a);
            verts.Add(b);
            verts.Add(c);
            tris.Add(i);
            tris.Add(i + 1);
            tris.Add(i + 2);
        }
    }
}
