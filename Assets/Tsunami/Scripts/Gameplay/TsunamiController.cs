using System;
using UnityEngine;

namespace Tsunami
{
    public enum TsunamiState
    {
        Idle,
        Warning,
        Running,
        Stopped
    }

    public enum TsunamiStopReason
    {
        None,
        Player,
        WaveEnded
    }

    /// <summary>
    /// Single authority for the tsunami event. Uses a timer in Update (no coroutines), so repeated
    /// Start/Stop calls can never create duplicate timers or sources. Stopping only switches the
    /// source off; the water solver keeps running and existing water stays in the scene.
    ///
    /// The event follows the classic tsunami sequence:
    ///   Warning - the sea draws back from the shore.
    ///   Running - a train of long waves (each smaller than the last) rides on a slow rise of sea level.
    ///             The solver makes them slow down, bunch up and grow taller over the shallow coast.
    ///   Stopped - no more water comes in; the flood drains back out (backwash) or pools in hollows.
    /// </summary>
    public class TsunamiController : MonoBehaviour
    {
        [SerializeField] WaterSimulation simulation;
        [Tooltip("Optional; reset together with the simulation.")]
        [SerializeField] VillageFloodTracker village;

        [Header("Source")]
        [Tooltip("Grid edge the tsunami arrives from (South = -Z).")]
        [SerializeField] BoundarySide sourceSide = BoundarySide.South;
        [Tooltip("Portion of that edge that carries the waves (0..1 along the edge).")]
        [SerializeField, Range(0f, 1f)] float sourceStart = 0f;
        [SerializeField, Range(0f, 1f)] float sourceEnd = 1f;
        [Tooltip("Extra rows inward from the edge that receive water (0 = edge only, best for travelling waves).")]
        [SerializeField, Range(0, 16)] int sourceRows = 0;
        [Tooltip("Fraction of the source edge, at each end, over which the wave fades out (keeps the corners calm).")]
        [SerializeField, Range(0f, 0.5f)] float edgeTaper = 0.2f;
        [Tooltip("How far the waves run along the side edges (fraction of the grid width). Only matters when edgeTaper is 0.")]
        [SerializeField, Range(0f, 1f)] float sideReach = 0.35f;
        [Tooltip("Open-ocean distance (m) the waves travel, visibly, before entering the grid.")]
        [SerializeField, Range(0f, 150f)] float approachDistance = 60f;

        [Header("Wave train")]
        [Tooltip("Number of waves. 0 = keep coming until Stop is pressed.")]
        [SerializeField, Range(0, 10)] int numberOfWaves = 1;
        [Tooltip("Tsunami strength: height of the first wave out at sea (m). It rears up over the shallow coast.")]
        [SerializeField, Range(0.5f, 30f)] float waveHeight = 12f;
        [Tooltip("Each following wave is this fraction of the previous one.")]
        [SerializeField, Range(0.3f, 1.5f)] float waveDecay = 0.7f;
        [Tooltip("Duration of one wave passing a point (s). Longer = longer wavelength.")]
        [SerializeField, Range(2f, 20f)] float wavePeriod = 9f;
        [Tooltip("Time between wave crests (s).")]
        [SerializeField, Range(3f, 40f)] float waveInterval = 16f;

        [Header("Sea-level surge")]
        [Tooltip("Slow rise of sea level under the waves during the event (m). Drives the inland flooding.")]
        [SerializeField, Range(0f, 5f)] float floodLevel = 1f;
        [SerializeField, Min(0f)] float floodRampTime = 8f;
        [Tooltip("Seconds the surge holds after the last wave before fading.")]
        [SerializeField, Min(0f)] float floodHoldTime = 10f;
        [SerializeField, Min(0f)] float floodFadeTime = 4f;
        [Tooltip("Seconds for the incoming wave to fade in on Start and out on Stop (avoids a sudden jump at the edge).")]
        [SerializeField, Range(0.1f, 5f)] float sourceFadeTime = 1f;

        [Header("Warning")]
        [SerializeField] bool useWarning = true;
        [SerializeField, Min(0f)] float warningDuration = 5f;
        [Tooltip("How far the sea draws back during the warning (m). 0 disables.")]
        [SerializeField, Range(0f, 4f)] float drawback = 1.2f;

        [Header("Reset")]
        [Tooltip("Also restore the original terrain when ResetSimulation is called.")]
        [SerializeField] bool resetTerrainOnReset;

        float timer;

        public TsunamiState State { get; private set; } = TsunamiState.Idle;
        public TsunamiStopReason StopReason { get; private set; }
        /// <summary>Seconds left in the warning countdown (0 outside Warning).</summary>
        public float WarningRemaining => State == TsunamiState.Warning ? Mathf.Max(0f, timer) : 0f;
        /// <summary>Seconds since the waves started (0 outside Running).</summary>
        public float RunningTime => State == TsunamiState.Running ? timer : 0f;
        /// <summary>0 = unlimited.</summary>
        public int WaveCount => numberOfWaves;
        /// <summary>1-based index of the latest wave sent out (0 before the first).</summary>
        public int CurrentWave
        {
            get
            {
                if (State != TsunamiState.Running) return 0;
                int k = Mathf.FloorToInt(timer / waveInterval) + 1;
                return numberOfWaves > 0 ? Mathf.Min(k, numberOfWaves) : k;
            }
        }

        public event Action<TsunamiState> StateChanged;

        void Start()
        {
            if (simulation == null)
            {
                Debug.LogError("[TsunamiController] No WaterSimulation assigned.", this);
                enabled = false;
                return;
            }
            ApplySource(false, 0f);
        }

        /// <summary>Starts the warning countdown (or the waves directly). Ignored while Warning/Running.</summary>
        public void StartTsunami()
        {
            if (!enabled || State == TsunamiState.Warning || State == TsunamiState.Running) return;
            StopReason = TsunamiStopReason.None;
            if (useWarning && warningDuration > 0f)
            {
                timer = warningDuration;
                SetState(TsunamiState.Warning);
            }
            else
            {
                BeginRunning();
            }
        }

        /// <summary>Cancels a pending countdown or stops incoming water. Existing water is kept.</summary>
        public void StopTsunami()
        {
            if (State != TsunamiState.Warning && State != TsunamiState.Running) return;
            EndEvent(TsunamiStopReason.Player);
        }

        /// <summary>Stops the event, removes all simulated water and returns to Idle.</summary>
        public void ResetSimulation()
        {
            if (simulation == null) return;
            ApplySource(false, 0f);
            if (resetTerrainOnReset) simulation.Terrain.ResetTerrain();
            simulation.ResetWater();
            if (village != null) village.ResetVillage();
            StopReason = TsunamiStopReason.None;
            timer = 0f;
            SetState(TsunamiState.Idle);
        }

        void Update()
        {
            switch (State)
            {
                case TsunamiState.Warning:
                    timer -= Time.deltaTime;
                    float progress = 1f - Mathf.Clamp01(timer / warningDuration);
                    simulation.Solver.BoundaryLevelOffset = -drawback * Mathf.SmoothStep(0f, 1f, progress);
                    if (timer <= 0f) BeginRunning();
                    break;

                case TsunamiState.Running:
                    timer += Time.deltaTime;
                    float height = WaveSignal(timer, out bool finished);
                    if (finished) EndEvent(TsunamiStopReason.WaveEnded);
                    else ApplySource(true, height);
                    break;
            }
        }

        /// <summary>Height of the incoming signal out at sea, t seconds after the waves started.</summary>
        float WaveSignal(float t, out bool finished)
        {
            finished = false;
            float fade = 1f;
            if (numberOfWaves > 0)
            {
                float end = (numberOfWaves - 1) * waveInterval + wavePeriod + floodHoldTime;
                if (t >= end + floodFadeTime) { finished = true; return 0f; }
                if (t > end) fade = floodFadeTime > 0f ? 1f - (t - end) / floodFadeTime : 0f;
            }

            float surge = floodLevel * (floodRampTime > 0f ? Mathf.SmoothStep(0f, 1f, t / floodRampTime) : 1f) * fade;

            // Each wave is a smooth sin^2 hump; only the one or two overlapping the current time matter.
            float waves = 0f;
            int last = Mathf.FloorToInt(t / waveInterval);
            int first = Mathf.Max(0, last - Mathf.CeilToInt(wavePeriod / waveInterval));
            for (int k = first; k <= last; k++)
            {
                if (numberOfWaves > 0 && k >= numberOfWaves) break;
                float x = (t - k * waveInterval) / wavePeriod;
                if (x <= 0f || x >= 1f) continue;
                float s = Mathf.Sin(Mathf.PI * x);
                waves += waveHeight * Mathf.Pow(waveDecay, k) * s * s;
            }
            return surge + waves;
        }

        void BeginRunning()
        {
            timer = 0f;
            simulation.Solver.BoundaryLevelOffset = 0f;
            ApplySource(true, 0f);
            SetState(TsunamiState.Running);
        }

        void EndEvent(TsunamiStopReason reason)
        {
            ApplySource(false, 0f);
            simulation.Solver.BoundaryLevelOffset = 0f;
            timer = 0f;
            StopReason = reason;
            SetState(TsunamiState.Stopped);
        }

        void ApplySource(bool active, float height)
        {
            ShallowWaterSolver s = simulation.Solver;
            s.SourceActive = active;
            s.SurgeHeight = height;
            s.SourceSide = sourceSide;
            s.SourceStart01 = Mathf.Min(sourceStart, sourceEnd);
            s.SourceEnd01 = Mathf.Max(sourceStart, sourceEnd);
            s.SourceBandCells = sourceRows;
            s.SideSurgeReach = sideReach;
            s.SourceEdgeTaper = edgeTaper;
            s.ApproachDistance = approachDistance;
            s.SourceFadeRate = 1f / sourceFadeTime;
        }

        void SetState(TsunamiState next)
        {
            if (State == next) return;
            State = next;
            StateChanged?.Invoke(next);
        }
    }
}
