using System;
using UnityEngine;

namespace Tsunami
{
    /// <summary>
    /// An illustrative tsunami set-up: initial disturbance + sea floor. Applying a scenario re-initialises the
    /// actual numerical model (bathymetry and initial conditions). Scenarios are demonstrations, not forecasts.
    /// </summary>
    [Serializable]
    public class TsunamiScenario
    {
        public string name;
        [TextArea] public string description;

        [Header("Source (initial condition)")]
        [Tooltip("Peak initial sea-surface displacement (m).")]
        [Range(-20f, 25f)] public float amplitude = 6f;
        [Tooltip("Gaussian radius along the travel direction (m).")]
        [Range(4f, 120f)] public float lengthRadius = 15f;
        [Tooltip("Gaussian radius across the travel direction (m). Large = long, straight wave front.")]
        [Range(4f, 300f)] public float widthRadius = 60f;
        [Tooltip("Distance of the source centre out from the coast along the approach axis (m).")]
        [Range(10f, 250f)] public float offshoreDistance = 100f;
        [Tooltip("Rotation of the travel direction away from the approach axis (degrees, + = clockwise seen from above).")]
        [Range(-90f, 90f)] public float directionOffset = 0f;
        public SourceProfile profile = SourceProfile.Gaussian;
        public SourceMotion motion = SourceMotion.Directional;

        [Header("Sea floor")]
        public BathymetryProfile bathymetry = BathymetryProfile.Default;

        public TsunamiScenario Clone() => (TsunamiScenario)MemberwiseClone();

        /// <summary>Built-in scenarios (the last one is the editable "Custom" set-up).</summary>
        public static TsunamiScenario[] CreateDefaults()
        {
            var deep = BathymetryProfile.Default;
            var gentle = deep;
            gentle.coastSlopeWidth = 80f;
            gentle.shelfDepth = 5f;
            gentle.shelfWidth = 70f;
            gentle.slopeWidth = 60f;
            return new[]
            {
                new TsunamiScenario
                {
                    name = "Vadi Tsunamisi (Oyun)",
                    description = "Oyunun dalgası: açık denizde büyük bir kabarma vadiye yönelir, dağların arasından geçip köyü vurur.",
                    amplitude = 10f, lengthRadius = 18f, widthRadius = 70f, offshoreDistance = 100f,
                    profile = SourceProfile.Gaussian, motion = SourceMotion.Directional, bathymetry = deep
                },
                new TsunamiScenario
                {
                    name = "Küçük Açık Deniz Bozulması",
                    description = "Derin denizde küçük, yerel bir kabarma. Durgun başlar, her yöne halkalar halinde yayılır; kıyıya etkisi sınırlıdır.",
                    amplitude = 2f, lengthRadius = 14f, widthRadius = 14f, offshoreDistance = 120f,
                    profile = SourceProfile.Gaussian, motion = SourceMotion.Radial, bathymetry = deep
                },
                new TsunamiScenario
                {
                    name = "Büyük Açık Deniz Tsunamisi",
                    description = "Derin denizde uzun bir fay boyunca büyük kabarma. Sahanlığa çıkınca yavaşlar ve yükselir.",
                    amplitude = 8f, lengthRadius = 16f, widthRadius = 90f, offshoreDistance = 120f,
                    profile = SourceProfile.Gaussian, motion = SourceMotion.Directional, bathymetry = deep
                },
                new TsunamiScenario
                {
                    name = "Sığ Kıyıya Yaklaşma",
                    description = "Geniş ve sığ bir sahanlık. Dalga sığlaştıkça yavaşlar, boyu kısalır, yüksekliği artar. Önce deniz çekilir (N-dalgası).",
                    amplitude = 4f, lengthRadius = 14f, widthRadius = 90f, offshoreDistance = 160f,
                    profile = SourceProfile.NWave, motion = SourceMotion.Directional, bathymetry = gentle
                },
                new TsunamiScenario
                {
                    name = "Kıyı Taşkını",
                    description = "Kıyıya yakın güçlü bir dalga alçak araziyi basar. Azami taşkın alanı kırmızıyla gösterilir.",
                    amplitude = 7f, lengthRadius = 18f, widthRadius = 110f, offshoreDistance = 60f,
                    profile = SourceProfile.Gaussian, motion = SourceMotion.Directional, bathymetry = deep
                },
                new TsunamiScenario
                {
                    name = "Özel",
                    description = "Kendi ayarların: kaynağı haritada seç, genliği, boyutu, yönü ve deniz tabanını değiştir.",
                    amplitude = 6f, lengthRadius = 15f, widthRadius = 60f, offshoreDistance = 100f,
                    profile = SourceProfile.Gaussian, motion = SourceMotion.Directional, bathymetry = deep
                }
            };
        }
    }
}
