using UnityEngine;

namespace Tsunami
{
    public enum TsunamiVisMode
    {
        Realistic = 0,
        SurfaceElevation = 1,
        WaterDepth = 2,
        FlowSpeed = 3
    }

    /// <summary>
    /// Controls how the simulation is shown: colour-map mode (realistic / surface elevation / water depth / flow
    /// speed), foam, spray, the water surface itself and the bathymetry view (all adjustable in the Inspector).
    /// Pushes shared shader globals (TsunamiVis.hlsl) and drives the decorative animation clock, which stops
    /// while the simulation is paused. Purely visual: nothing here changes the physics.
    /// </summary>
    public class TsunamiVisualizationManager : MonoBehaviour
    {
        [SerializeField] WaterSimulation simulation;
        [SerializeField] Renderer[] waterRenderers = new Renderer[0];
        [Tooltip("Sea-floor renderer (TerrainGrid). Shown by the bathymetry view.")]
        [SerializeField] Renderer seabedRenderer;
        [SerializeField] WaterSprayEmitter spray;

        [Header("Ranges")]
        [Tooltip("Surface elevation (m) shown at full red / blue.")]
        [SerializeField, Range(0.2f, 20f)] float elevationRange = 4f;
        [Tooltip("Water depth (m) shown as the darkest blue.")]
        [SerializeField, Range(1f, 100f)] float depthRange = 30f;
        [Tooltip("Flow speed (m/s) shown as the brightest colour.")]
        [SerializeField, Range(0.5f, 30f)] float speedRange = 8f;

        [Header("State")]
        [SerializeField] TsunamiVisMode mode = TsunamiVisMode.Realistic;
        [SerializeField] bool foam = true;
        [SerializeField] bool sprayEnabled = true;
        [SerializeField] bool waterVisible = true;
        [SerializeField] bool bathymetryView;

        static readonly int VisMode = Shader.PropertyToID("_TsunamiVisMode");
        static readonly int SeaLevel = Shader.PropertyToID("_TsunamiSeaLevel");
        static readonly int ElevRange = Shader.PropertyToID("_TsunamiElevRange");
        static readonly int DepthRangeId = Shader.PropertyToID("_TsunamiDepthRange");
        static readonly int SpeedRangeId = Shader.PropertyToID("_TsunamiSpeedRange");
        static readonly int NoFoam = Shader.PropertyToID("_TsunamiNoFoam");
        static readonly int AnimTime = Shader.PropertyToID("_TsunamiTime");
        static readonly int AnimTimeSet = Shader.PropertyToID("_TsunamiTimeSet");
        static readonly int BathyVis = Shader.PropertyToID("_TsunamiBathyVis");

        float animTime;
        Texture2D[] legendTextures;

        public TsunamiVisMode Mode { get => mode; set { mode = value; Push(); } }
        public float ElevationRange { get => elevationRange; set { elevationRange = Mathf.Clamp(value, 0.2f, 20f); Push(); } }
        public float DepthRange => depthRange;
        public float SpeedRange => speedRange;
        public bool Foam { get => foam; set { foam = value; Push(); } }
        public bool Spray { get => sprayEnabled; set { sprayEnabled = value; ApplyToggles(); } }
        public bool WaterVisible { get => waterVisible; set { waterVisible = value; ApplyToggles(); } }
        public bool BathymetryView { get => bathymetryView; set { bathymetryView = value; ApplyToggles(); Push(); } }

        void OnEnable()
        {
            Push();
            ApplyToggles();
        }

        void OnDisable()
        {
            Shader.SetGlobalFloat(AnimTimeSet, 0f);
            Shader.SetGlobalFloat(VisMode, 0f);
            Shader.SetGlobalFloat(NoFoam, 0f);
            Shader.SetGlobalFloat(BathyVis, 0f);
        }

        void OnDestroy()
        {
            if (legendTextures != null)
                foreach (var t in legendTextures) if (t != null) Destroy(t);
        }

        void OnValidate()
        {
            if (isActiveAndEnabled) { Push(); ApplyToggles(); }
        }

        void Update()
        {
            // Decorative ripples and foam scroll only while the simulation runs.
            if (simulation != null && !simulation.Paused) animTime += Time.deltaTime * simulation.TimeScale;
            Shader.SetGlobalFloat(AnimTime, animTime);
            if (simulation != null) Shader.SetGlobalFloat(SeaLevel, simulation.SeaLevelWorld);
            if (spray != null)
            {
                var ps = spray.GetComponent<ParticleSystem>();
                if (ps != null)
                {
                    bool frozen = simulation != null && simulation.Paused;
                    if (frozen && !ps.isPaused) ps.Pause(true);
                    else if (!frozen && ps.isPaused) ps.Play(true);
                }
            }
        }

        void Push()
        {
            Shader.SetGlobalFloat(VisMode, (float)mode);
            Shader.SetGlobalFloat(ElevRange, elevationRange);
            Shader.SetGlobalFloat(DepthRangeId, depthRange);
            Shader.SetGlobalFloat(SpeedRangeId, speedRange);
            Shader.SetGlobalFloat(NoFoam, foam ? 0f : 1f);
            Shader.SetGlobalFloat(AnimTimeSet, 1f);
            Shader.SetGlobalFloat(AnimTime, animTime);
            Shader.SetGlobalFloat(BathyVis, bathymetryView ? 1f : 0f);
            if (simulation != null) Shader.SetGlobalFloat(SeaLevel, simulation.SeaLevelWorld);
        }

        void ApplyToggles()
        {
            bool showWater = waterVisible && !bathymetryView;
            foreach (var r in waterRenderers) if (r != null) r.enabled = showWater;
            if (seabedRenderer != null) seabedRenderer.enabled = true;
            if (spray != null) spray.gameObject.SetActive(sprayEnabled && showWater);
        }

        // ------------------------------------------------------------------ legend (same stops as TsunamiVis.hlsl)

        /// <summary>Colour of the given mode's map at t (elevation: -1..1, others: 0..1).</summary>
        public static Color Colormap(TsunamiVisMode m, float t)
        {
            switch (m)
            {
                case TsunamiVisMode.SurfaceElevation:
                {
                    t = Mathf.Clamp(t, -1f, 1f);
                    Color neg = new Color(0.06f, 0.22f, 0.62f), mid = new Color(0.95f, 0.95f, 0.93f), pos = new Color(0.72f, 0.04f, 0.06f);
                    return t < 0f ? Color.Lerp(mid, neg, -t) : Color.Lerp(mid, pos, t);
                }
                case TsunamiVisMode.WaterDepth:
                {
                    t = Mathf.Clamp01(t);
                    Color a = new Color(0.78f, 0.95f, 0.98f), b = new Color(0.20f, 0.55f, 0.85f), c = new Color(0.02f, 0.08f, 0.30f);
                    return t < 0.5f ? Color.Lerp(a, b, t * 2f) : Color.Lerp(b, c, t * 2f - 1f);
                }
                case TsunamiVisMode.FlowSpeed:
                {
                    t = Mathf.Clamp01(t) * 4f;
                    Color c0 = new Color(0.27f, 0f, 0.33f), c1 = new Color(0.23f, 0.32f, 0.55f), c2 = new Color(0.13f, 0.57f, 0.55f),
                          c3 = new Color(0.37f, 0.79f, 0.38f), c4 = new Color(0.99f, 0.91f, 0.14f);
                    if (t < 1f) return Color.Lerp(c0, c1, t);
                    if (t < 2f) return Color.Lerp(c1, c2, t - 1f);
                    if (t < 3f) return Color.Lerp(c2, c3, t - 2f);
                    return Color.Lerp(c3, c4, t - 3f);
                }
                default: return new Color(0.1f, 0.4f, 0.6f);
            }
        }

        /// <summary>Horizontal gradient texture for the legend of the given mode.</summary>
        public Texture2D LegendTexture(TsunamiVisMode m)
        {
            if (legendTextures == null) legendTextures = new Texture2D[4];
            int k = (int)m;
            if (legendTextures[k] != null) return legendTextures[k];
            var tex = new Texture2D(128, 1, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "Legend " + m };
            for (int x = 0; x < 128; x++)
            {
                float u = x / 127f;
                tex.SetPixel(x, 0, Colormap(m, m == TsunamiVisMode.SurfaceElevation ? u * 2f - 1f : u));
            }
            tex.Apply(false, true);
            legendTextures[k] = tex;
            return tex;
        }

        /// <summary>Left / right legend labels for the current ranges.</summary>
        public void LegendLabels(TsunamiVisMode m, out string left, out string right, out string title)
        {
            switch (m)
            {
                case TsunamiVisMode.SurfaceElevation:
                    left = $"-{elevationRange:0.#} m"; right = $"+{elevationRange:0.#} m";
                    title = "Su yüzeyi yüksekliği (durgun deniz seviyesine göre)"; return;
                case TsunamiVisMode.WaterDepth:
                    left = "0 m"; right = $"{depthRange:0} m+"; title = "Su derinliği"; return;
                case TsunamiVisMode.FlowSpeed:
                    left = "0 m/s"; right = $"{speedRange:0.#} m/s+"; title = "Akış hızı (derinlik ortalamalı)"; return;
                default:
                    left = right = string.Empty; title = "Gerçekçi görünüm"; return;
            }
        }
    }
}
