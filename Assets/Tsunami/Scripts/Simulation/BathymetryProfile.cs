using System;

namespace Tsunami
{
    /// <summary>
    /// Cross-shore sea-floor profile used around a land area (all heights relative to the land's ground, metres):
    ///   coast slope (nearshore, from just below sea level down to the shelf)
    ///   -> continental shelf (flat, shallow)
    ///   -> continental slope (down to the deep sea)
    ///   -> deep sea floor, with an optional submarine ridge.
    /// Pure C# so the simulation and tests share the exact same seabed.
    /// </summary>
    [Serializable]
    public struct BathymetryProfile
    {
        /// <summary>Still sea level below the land's ground (m, positive).</summary>
        public float seaBelowGround;
        /// <summary>Width of the nearshore slope (m). Small = steep coast, large = gentle beach approach.</summary>
        public float coastSlopeWidth;
        /// <summary>Water depth on the continental shelf (m).</summary>
        public float shelfDepth;
        /// <summary>Width of the flat shelf (m). 0 = no shelf.</summary>
        public float shelfWidth;
        /// <summary>Width of the continental slope (m).</summary>
        public float slopeWidth;
        /// <summary>Water depth of the deep sea (m).</summary>
        public float deepDepth;
        /// <summary>Height of a submarine ridge above the deep floor (m). 0 = none.</summary>
        public float ridgeHeight;
        /// <summary>Distance of the ridge crest from the coast (m).</summary>
        public float ridgeDistance;

        public static BathymetryProfile Default => new BathymetryProfile
        {
            seaBelowGround = 0.3f,
            coastSlopeWidth = 24f,
            shelfDepth = 8f,
            shelfWidth = 40f,
            slopeWidth = 40f,
            deepDepth = 35f,
            ridgeHeight = 0f,
            ridgeDistance = 140f
        };

        /// <summary>Returns a copy with every value clamped to the supported range.</summary>
        public BathymetryProfile Validated()
        {
            var p = this;
            p.seaBelowGround = Clamp(p.seaBelowGround, 0.05f, 5f);
            p.coastSlopeWidth = Clamp(p.coastSlopeWidth, 2f, 200f);
            p.shelfDepth = Clamp(p.shelfDepth, 1f, 60f);
            p.shelfWidth = Clamp(p.shelfWidth, 0f, 400f);
            p.slopeWidth = Clamp(p.slopeWidth, 2f, 400f);
            p.deepDepth = Clamp(p.deepDepth, p.shelfDepth, 200f);
            p.ridgeHeight = Clamp(p.ridgeHeight, 0f, p.deepDepth - 1f);
            p.ridgeDistance = Clamp(p.ridgeDistance, 0f, 2000f);
            return p;
        }

        /// <summary>Sea-floor height (relative to the land's ground) at a distance <paramref name="d"/> m out from the coast.</summary>
        public float FloorAt(float d)
        {
            float sea = -seaBelowGround;
            float z;
            float d1 = coastSlopeWidth, d2 = d1 + shelfWidth, d3 = d2 + slopeWidth;
            if (d <= 0f) z = 0f;
            else if (d < d1) z = Lerp(sea - 0.3f, -shelfDepth, Smooth(d / d1));
            else if (d < d2) z = -shelfDepth;
            else if (d < d3) z = Lerp(-shelfDepth, -deepDepth, Smooth((d - d2) / (d3 - d2)));
            else z = -deepDepth;
            if (ridgeHeight > 0f)
            {
                float r = (d - ridgeDistance) / 18f;
                z += ridgeHeight * MathF.Exp(-r * r);
                if (z > sea - 0.5f) z = sea - 0.5f; // a ridge never breaks the surface
            }
            return z;
        }

        static float Smooth(float t) { t = t < 0f ? 0f : (t > 1f ? 1f : t); return t * t * (3f - 2f * t); }
        static float Lerp(float a, float b, float t) => a + (b - a) * t;
        static float Clamp(float v, float a, float b) => v < a ? a : (v > b ? b : v);
    }
}
