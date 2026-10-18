using System;

namespace Tsunami
{
    /// <summary>Pure two-sided load calculation for one numerical wall face.</summary>
    public readonly struct WallFaceLoad
    {
        public readonly float ImpactForceNewtons;
        public readonly float SustainedForceNewtons;
        public readonly float LowImpactForceNewtons;
        public readonly float HighImpactForceNewtons;
        public readonly float LowHydroForceNewtons;
        public readonly float HighHydroForceNewtons;

        WallFaceLoad(float impact, float sustained, float lowImpact, float highImpact, float lowHydro, float highHydro)
        {
            ImpactForceNewtons = impact;
            SustainedForceNewtons = sustained;
            LowImpactForceNewtons = lowImpact;
            HighImpactForceNewtons = highImpact;
            LowHydroForceNewtons = lowHydro;
            HighHydroForceNewtons = highHydro;
        }

        public static WallFaceLoad Calculate(float density, float gravity, float width,
            float lowWetDepth, float lowInflowSpeed, float highWetDepth, float highInflowSpeed)
        {
            density = Math.Max(0f, density);
            gravity = Math.Max(0f, gravity);
            width = Math.Max(0f, width);
            lowWetDepth = Math.Max(0f, lowWetDepth);
            highWetDepth = Math.Max(0f, highWetDepth);
            lowInflowSpeed = Math.Max(0f, lowInflowSpeed);
            highInflowSpeed = Math.Max(0f, highInflowSpeed);
            float lowImpact = 0.5f * density * lowWetDepth * lowInflowSpeed * lowInflowSpeed * width;
            float highImpact = 0.5f * density * highWetDepth * highInflowSpeed * highInflowSpeed * width;
            float lowHydro = 0.5f * density * gravity * lowWetDepth * lowWetDepth * width;
            float highHydro = 0.5f * density * gravity * highWetDepth * highWetDepth * width;
            return new WallFaceLoad(lowImpact + highImpact, Math.Abs(lowHydro - highHydro), lowImpact, highImpact,
                lowHydro, highHydro);
        }
    }

    /// <summary>
    /// Rendering-only duplicate gate. Hydraulic metadata must be delivered before calling this gate, so two owners
    /// sharing one numerical face both receive their sample while only the first visual burst is emitted.
    /// </summary>
    public sealed class ImpactFaceEmissionGate
    {
        readonly long[] xFaceSequence;
        readonly long[] zFaceSequence;

        public ImpactFaceEmissionGate(int cellCount)
        {
            if (cellCount <= 0) throw new ArgumentOutOfRangeException(nameof(cellCount));
            xFaceSequence = new long[cellCount];
            zFaceSequence = new long[cellCount];
        }

        public bool ShouldEmit(long sequence, int faceIndex, bool alongX)
        {
            if (sequence <= 0 || faceIndex < 0 || faceIndex >= xFaceSequence.Length) return false;
            long[] stamps = alongX ? xFaceSequence : zFaceSequence;
            if (stamps[faceIndex] == sequence) return false;
            stamps[faceIndex] = sequence;
            return true;
        }
    }

    public static class WallImpactDelivery
    {
        /// <summary>Unconditionally delivers owner metadata; visual duplicate policy belongs downstream.</summary>
        public static void Deliver<T>(Action<T> callback, T sample)
        {
            callback?.Invoke(sample);
        }
    }

    /// <summary>
    /// Gameplay durability tuning. Hydraulic loads are supplied in newtons, simulation time in seconds and
    /// durability in game hit points. The impact rate converts kN.s to hit points; continuous pressure is
    /// non-damaging. These are gameplay scales rather than material-engineering predictions.
    /// </summary>
    [Serializable]
    public struct WallDamageSettings
    {
        public float durability;
        public float impactThresholdKilonewtons;
        public float impactDamagePerKilonewtonSecond;
        // Legacy serialized fields: pressure remains available to the solver, but contact no longer
        // consumes wall durability. Old scenes with a positive rate are normalized to zero below.
        public float sustainedThresholdKilonewtons;
        public float sustainedDamagePerKilonewtonSecond;
        public float minimumFailureSeconds;

        public static WallDamageSettings Default => new WallDamageSettings
        {
            durability = 100f,
            impactThresholdKilonewtons = 6f,
            impactDamagePerKilonewtonSecond = 0.32f,
            sustainedThresholdKilonewtons = 12f,
            sustainedDamagePerKilonewtonSecond = 0f,
            minimumFailureSeconds = 3f
        };

        public WallDamageSettings Validated()
        {
            // Components saved before durability existed have no serialized value for this struct.
            if (!(durability > 0f) && impactThresholdKilonewtons == 0f && impactDamagePerKilonewtonSecond == 0f &&
                sustainedThresholdKilonewtons == 0f && sustainedDamagePerKilonewtonSecond == 0f)
                return Default;
            WallDamageSettings value = this;
            value.durability = Math.Max(0.01f, value.durability);
            value.impactThresholdKilonewtons = Math.Max(0f, value.impactThresholdKilonewtons);
            value.impactDamagePerKilonewtonSecond = Math.Max(0f, value.impactDamagePerKilonewtonSecond);
            value.sustainedThresholdKilonewtons = Math.Max(0f, value.sustainedThresholdKilonewtons);
            value.sustainedDamagePerKilonewtonSecond = 0f;
            value.minimumFailureSeconds = value.minimumFailureSeconds > 0f ? value.minimumFailureSeconds : 3f;
            return value;
        }
    }

    /// <summary>Pure, timestep-based durability state. Apply must be called once for each solver step.</summary>
    public sealed class WallDamageState
    {
        public float Damage { get; private set; }
        public bool Broken { get; private set; }

        public float Remaining(WallDamageSettings settings) => Math.Max(0f, settings.Validated().durability - Damage);

        public bool Apply(float impactForceNewtons, float sustainedForceNewtons, float solverDt,
            WallDamageSettings settings, out float impactDamage, out float sustainedDamage)
        {
            impactDamage = 0f;
            sustainedDamage = 0f;
            if (Broken || !(solverDt > 0f)) return false;

            settings = settings.Validated();
            float impactKilonewtons = Math.Max(0f, impactForceNewtons) * 0.001f;
            impactDamage = Math.Max(0f, impactKilonewtons - settings.impactThresholdKilonewtons) *
                           settings.impactDamagePerKilonewtonSecond * solverDt;
            // Water left behind by a wave may redistribute or rest against a wall indefinitely.
            // Only the dynamic impact term damages it; hydrostatic/contact load never does.
            float total = impactDamage + sustainedDamage;
            float limit = settings.durability * solverDt / settings.minimumFailureSeconds;
            if (total > limit)
            {
                float scale = limit / total;
                impactDamage *= scale;
                sustainedDamage *= scale;
            }
            Damage = Math.Min(settings.durability, Damage + impactDamage + sustainedDamage);
            if (Damage < settings.durability) return false;
            Broken = true;
            return true;
        }

        public void Reset()
        {
            Damage = 0f;
            Broken = false;
        }
    }
}
