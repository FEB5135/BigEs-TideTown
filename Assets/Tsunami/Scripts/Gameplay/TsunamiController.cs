using System;
using UnityEngine;

namespace Tsunami
{
    public enum TsunamiState
    {
        /// <summary>Initial conditions not applied yet; the sea is at rest.</summary>
        Idle,
        /// <summary>Optional gameplay countdown before the tsunami is released.</summary>
        Warning,
        /// <summary>The solver is advancing.</summary>
        Running,
        /// <summary>Simulation time is frozen; Resume continues from exactly this state.</summary>
        Paused
    }

    /// <summary>
    /// Tsunami simulation manager: owns the lifecycle (play, pause, resume, reset, restart), simulation speed and
    /// scenario selection, and turns the current scenario into the solver's initial conditions and sea floor.
    ///
    /// Initial-condition parameters (source and sea floor) only take effect when the simulation is (re)started:
    /// editing them while a tsunami is running never silently recreates it; HasPendingChanges tells the UI to ask
    /// for a reset. Speed and friction are live parameters.
    /// </summary>
    public class TsunamiController : MonoBehaviour
    {
        [SerializeField] WaterSimulation simulation;
        [Tooltip("Optional; reset together with the simulation.")]
        [SerializeField] VillageFloodTracker village;
        [Tooltip("Optional: the team map adapter (sea floor, approach axis). Without it the source uses the fallback placement.")]
        [SerializeField] TeamMapTsunamiAdapter mapAdapter;

        [Header("Scenarios")]
        [SerializeField] TsunamiScenario[] scenarios = TsunamiScenario.CreateDefaults();
        [SerializeField] int scenarioIndex;

        [Header("Gameplay")]
        [Tooltip("Show a countdown before the tsunami is released (gameplay). Restart skips it. During the countdown the " +
                 "incoming swell (TsunamiSwell) travels in from the open sea to the source.")]
        [SerializeField] bool useWarning = true;
        [SerializeField, Range(0f, 30f)] float warningDuration = 4f;
        [Tooltip("Release the tsunami by itself a random time after the game starts (MainMenu.GameStarted, or Start " +
                 "when the scene has no main menu). The storm clouds approach during this time.")]
        [SerializeField] bool autoRelease;
        [SerializeField, Min(0f)] float releaseDelayMin = 120f;
        [SerializeField, Min(0f)] float releaseDelayMax = 180f;

        [Header("Fallback placement (scenes without a map adapter)")]
        [SerializeField] Vector3 fallbackSourcePosition = new Vector3(0f, 0f, -90f);
        [Tooltip("Travel direction in degrees: 0 = towards +Z (north), 90 = towards +X (east).")]
        [SerializeField] float fallbackDirection = 0f;

        TsunamiScenario current;            // live copy of the selected scenario (edited by the UI)
        bool hasCustomPosition;
        Vector3 customPosition;
        float timer;
        BathymetryProfile appliedBathymetry;
        bool appliedBathymetryValid;
        bool gameStarted;
        bool releaseScheduled;
        float releaseDelay;
        float releaseRemaining;

        public TsunamiState State { get; private set; } = TsunamiState.Idle;
        public float WarningRemaining => State == TsunamiState.Warning ? Mathf.Max(0f, timer) : 0f;
        public float WarningDuration => warningDuration;
        /// <summary>True while the automatic release is counting down.</summary>
        public bool ReleaseScheduled => releaseScheduled && State == TsunamiState.Idle;
        /// <summary>Seconds until the automatic release, or 0 when none is scheduled.</summary>
        public float ReleaseRemaining => ReleaseScheduled ? Mathf.Max(0f, releaseRemaining) : 0f;
        /// <summary>
        /// How close the tsunami is: 0 = calm (nothing scheduled), rising to 1 at the automatic release, 1 while a
        /// tsunami is announced, running or paused. Drives the storm.
        /// </summary>
        public float StormProgress
        {
            get
            {
                if (State != TsunamiState.Idle) return 1f;
                if (!releaseScheduled || releaseDelay <= 0f) return 0f;
                return Mathf.Clamp01(1f - releaseRemaining / releaseDelay);
            }
        }
        public WaterSimulation Simulation => simulation;
        public TeamMapTsunamiAdapter MapAdapter => mapAdapter;
        public TsunamiScenario[] Scenarios => scenarios;
        public int ScenarioIndex => scenarioIndex;
        /// <summary>The editable copy of the selected scenario.</summary>
        public TsunamiScenario Current => current ?? (current = scenarios[Mathf.Clamp(scenarioIndex, 0, scenarios.Length - 1)].Clone());
        /// <summary>Initial-condition settings changed since the last (re)start: a reset is needed to apply them.</summary>
        public bool HasPendingChanges { get; private set; }
        public bool UseWarning { get => useWarning; set => useWarning = value; }
        public float TimeScale { get => simulation != null ? simulation.TimeScale : 1f; set { if (simulation != null) simulation.TimeScale = value; } }
        /// <summary>
        /// Height of the initial sea-surface bulge out at sea (m) for the current scenario. Takes effect on the next
        /// start / restart; a tsunami already in progress is not changed.
        /// </summary>
        public float WaveHeight
        {
            get => Current.amplitude;
            set
            {
                Current.amplitude = Mathf.Clamp(value, MinWaveHeight, MaxWaveHeight);
                NotifyParametersEdited();
            }
        }

        public const float MinWaveHeight = 1f;
        public const float MaxWaveHeight = 25f;

        /// <summary>Volume added by the last source (m^3).</summary>
        public double LastSourceVolume { get; private set; }

        public event Action<TsunamiState> StateChanged;
        /// <summary>Raised after the water was reset to rest (new initial state): trackers clear their maxima.</summary>
        public event Action SimulationReset;
        public event Action ScenarioChanged;
        public event Action ParametersChanged;

        void OnEnable() => MainMenu.GameStarted += OnGameStarted;
        void OnDisable() => MainMenu.GameStarted -= OnGameStarted;

        void OnGameStarted()
        {
            gameStarted = true;
            ScheduleRelease();
        }

        void Start()
        {
            if (simulation == null)
            {
                Debug.LogError("[TsunamiController] No WaterSimulation assigned.", this);
                enabled = false;
                return;
            }
            if (scenarios == null || scenarios.Length == 0) scenarios = TsunamiScenario.CreateDefaults();
            current = null;
            ApplyBathymetryIfNeeded(true);
            simulation.Paused = true;
            SimulationReset?.Invoke();
            // Without a main menu the game starts right away.
            if (!gameStarted && FindAnyObjectByType<MainMenu>() == null) OnGameStarted();
        }

        void Update()
        {
            if (State == TsunamiState.Idle && releaseScheduled)
            {
                releaseRemaining -= Time.deltaTime;
                if (releaseRemaining <= 0f) Play();
                return;
            }
            if (State != TsunamiState.Warning) return;
            timer -= Time.deltaTime;
            if (timer <= 0f) BeginRun();
        }

        /// <summary>Starts a new random countdown to the automatic release (when enabled and the game has started).</summary>
        public void ScheduleRelease()
        {
            releaseScheduled = false;
            if (!autoRelease || !gameStarted || State != TsunamiState.Idle) return;
            float min = Mathf.Min(releaseDelayMin, releaseDelayMax), max = Mathf.Max(releaseDelayMin, releaseDelayMax);
            releaseDelay = releaseRemaining = UnityEngine.Random.Range(min, max);
            releaseScheduled = true;
            Debug.Log($"[TsunamiController] Tsunami in {releaseDelay:0} s.", this);
        }

        /// <summary>
        /// Skips the storm countdown (testing): the tsunami sets off now, with its warning / incoming swell. A
        /// tsunami already running is reset and sent again.
        /// </summary>
        public void ReleaseNow()
        {
            if (!enabled || State == TsunamiState.Warning) return;
            if (State != TsunamiState.Idle) ResetSimulation();
            Play();
        }

        // ------------------------------------------------------------------ playback

        /// <summary>Starts the tsunami from the current initial conditions, or resumes when paused.</summary>
        public void Play()
        {
            if (!enabled) return;
            switch (State)
            {
                case TsunamiState.Paused: Resume(); return;
                case TsunamiState.Running:
                case TsunamiState.Warning: return;
            }
            releaseScheduled = false;
            if (useWarning && warningDuration > 0f)
            {
                timer = warningDuration;
                SetState(TsunamiState.Warning);
            }
            else BeginRun();
        }

        public void Pause()
        {
            if (State == TsunamiState.Running)
            {
                simulation.Paused = true;
                SetState(TsunamiState.Paused);
            }
            else if (State == TsunamiState.Warning)
            {
                SetState(TsunamiState.Idle); // cancel the pending release
                ScheduleRelease();
            }
        }

        public void Resume()
        {
            if (State != TsunamiState.Paused) return;
            simulation.Paused = false;
            SetState(TsunamiState.Running);
        }

        public void TogglePause()
        {
            if (State == TsunamiState.Paused) Resume();
            else Pause();
        }

        /// <summary>Restores the initial state: sea at rest, current scenario's sea floor, village repaired.</summary>
        public void ResetSimulation()
        {
            if (simulation == null) return;
            simulation.Paused = true;
            ApplyBathymetryIfNeeded(false);
            simulation.ResetWater();
            if (village != null) village.ResetVillage();
            HasPendingChanges = false;
            timer = 0f;
            SetState(TsunamiState.Idle);
            SimulationReset?.Invoke();
            ScheduleRelease();
        }

        /// <summary>Reset and immediately release the tsunami (no countdown).</summary>
        public void Restart()
        {
            ResetSimulation();
            BeginRun();
        }

        // Kept for older UI / scripts.
        public void StartTsunami() => Play();
        public void StopTsunami() => Pause();

        void BeginRun()
        {
            if (State != TsunamiState.Idle && State != TsunamiState.Warning) ResetSimulation();
            if (HasPendingChanges) ResetSimulation();
            ApplyBathymetryIfNeeded(false);
            // Fresh initial state: sea at rest + the scenario's disturbance.
            simulation.ResetWater();
            SimulationReset?.Invoke();
            TsunamiScenario s = Current;
            LastSourceVolume = simulation.AddSource(SourcePosition, TravelDirection, s.amplitude, s.lengthRadius,
                s.widthRadius, s.profile, s.motion);
            HasPendingChanges = false;
            releaseScheduled = false;
            simulation.Paused = false;
            SetState(TsunamiState.Running);
        }

        // ------------------------------------------------------------------ scenarios and parameters

        public void SelectScenario(int index)
        {
            if (scenarios == null || scenarios.Length == 0) return;
            index = Mathf.Clamp(index, 0, scenarios.Length - 1);
            scenarioIndex = index;
            current = scenarios[index].Clone();
            hasCustomPosition = false;
            ScenarioChanged?.Invoke();
            ResetSimulation();
        }

        /// <summary>Call after editing <see cref="Current"/>: marks a reset as needed if a tsunami is in progress.</summary>
        public void NotifyParametersEdited()
        {
            // A new sea floor is rebuilt once on Reset / Play (not on every slider tick); a running tsunami is
            // never recreated silently.
            if (State != TsunamiState.Idle || !BathymetryEquals(Current.bathymetry.Validated(), appliedBathymetry))
                HasPendingChanges = true;
            ParametersChanged?.Invoke();
        }

        /// <summary>Places the source at a world position (custom placement, e.g. a click on the ocean).</summary>
        public void SetSourcePosition(Vector3 world)
        {
            hasCustomPosition = true;
            customPosition = mapAdapter != null ? mapAdapter.ClampToOcean(world) : world;
            NotifyParametersEdited();
        }

        public bool HasCustomSourcePosition => hasCustomPosition;

        /// <summary>Back to the scenario's placement (offshore on the approach axis).</summary>
        public void ClearSourcePosition()
        {
            if (!hasCustomPosition) return;
            hasCustomPosition = false;
            NotifyParametersEdited();
        }

        /// <summary>World position of the source centre for the current settings.</summary>
        public Vector3 SourcePosition
        {
            get
            {
                if (hasCustomPosition) return customPosition;
                if (mapAdapter != null) return mapAdapter.OffshorePoint(Current.offshoreDistance);
                return fallbackSourcePosition;
            }
        }

        /// <summary>Travel direction of the disturbance (unit XZ).</summary>
        public Vector2 TravelDirection
        {
            get
            {
                Vector2 axis;
                if (mapAdapter != null) axis = mapAdapter.ApproachDirection;
                else
                {
                    float a = fallbackDirection * Mathf.Deg2Rad;
                    axis = new Vector2(Mathf.Sin(a), Mathf.Cos(a));
                }
                if (hasCustomPosition && mapAdapter != null)
                {
                    // A placed source aims at the approach target unless rotated.
                    Vector3 d = mapAdapter.ApproachTarget - customPosition;
                    if (new Vector2(d.x, d.z).sqrMagnitude > 1f) axis = new Vector2(d.x, d.z).normalized;
                }
                float off = -Current.directionOffset * Mathf.Deg2Rad;
                float c = Mathf.Cos(off), s = Mathf.Sin(off);
                return new Vector2(axis.x * c - axis.y * s, axis.x * s + axis.y * c);
            }
        }

        /// <summary>Travel direction as a compass angle (0 = +Z/north, 90 = +X/east).</summary>
        public float TravelHeading => Mathf.Repeat(Mathf.Atan2(TravelDirection.x, TravelDirection.y) * Mathf.Rad2Deg, 360f);

        void ApplyBathymetryIfNeeded(bool force)
        {
            if (mapAdapter == null) return;
            BathymetryProfile wanted = Current.bathymetry.Validated();
            if (!force && appliedBathymetryValid && BathymetryEquals(wanted, appliedBathymetry)) return;
            mapAdapter.SetBathymetry(wanted);
            appliedBathymetry = wanted;
            appliedBathymetryValid = true;
        }

        static bool BathymetryEquals(BathymetryProfile a, BathymetryProfile b)
        {
            return Mathf.Approximately(a.seaBelowGround, b.seaBelowGround) && Mathf.Approximately(a.coastSlopeWidth, b.coastSlopeWidth) &&
                   Mathf.Approximately(a.shelfDepth, b.shelfDepth) && Mathf.Approximately(a.shelfWidth, b.shelfWidth) &&
                   Mathf.Approximately(a.slopeWidth, b.slopeWidth) && Mathf.Approximately(a.deepDepth, b.deepDepth) &&
                   Mathf.Approximately(a.ridgeHeight, b.ridgeHeight) && Mathf.Approximately(a.ridgeDistance, b.ridgeDistance);
        }

        void SetState(TsunamiState next)
        {
            if (State == next) return;
            State = next;
            StateChanged?.Invoke(next);
        }
    }
}
