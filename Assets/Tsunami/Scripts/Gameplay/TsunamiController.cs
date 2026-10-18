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
                 "wave travels in from the open sea (drawn by TsunamiSwell beyond the simulated area) and flows into " +
                 "the simulation through its edge: the wave that hits the coast is the same wave, never a new one.")]
        [SerializeField] bool useWarning = true;
        [SerializeField, Range(0f, 30f)] float warningDuration = 4f;
        [Tooltip("Crest height of the incoming wave far out at the start of the countdown, as a share of the wave " +
                 "height; it reaches the full height where it enters the simulated area.")]
        [SerializeField, Range(0f, 1f)] float incomingStartHeight = 0.35f;
        [Tooltip("Release the tsunami by itself a random time after the game starts (MainMenu.GameStarted, or Start " +
                 "when the scene has no main menu). The storm clouds come in during the last minute (TsunamiStorm).")]
        [SerializeField] bool autoRelease;
        [SerializeField, Min(0f)] float releaseDelayMin = 120f;
        [SerializeField, Min(0f)] float releaseDelayMax = 180f;

        [Serializable]
        public struct Wave
        {
            [Tooltip("Crest height (m) of this tsunami.")]
            public float height;
            [Tooltip("Seconds after the game starts (first wave) or after the previous wave was released.")]
            public float delay;
        }

        [Header("Waves")]
        [Tooltip("With automatic release on, the game's tsunamis in order. Empty: a single tsunami at a random time " +
                 "between releaseDelayMin and releaseDelayMax.")]
        [SerializeField] Wave[] waves =
        {
            new Wave { height = 15f, delay = 180f },
            new Wave { height = 20f, delay = 120f },
            new Wave { height = 25f, delay = 120f }
        };
        [Tooltip("Seconds after its release that a wave counts as in progress: the villagers shelter and the storm " +
                 "stays. Afterwards they come out until the next wave; after the last one WavesFinished is raised.")]
        [SerializeField, Min(1f)] float waveDuration = 60f;
        [Tooltip("Seconds after its release when the water left on land starts seeping into the ground, so walls can " +
                 "be built on dry land before the next wave.")]
        [SerializeField, Min(0f)] float drainStart = 45f;
        [Tooltip("Seconds the land takes to dry; the water goes slowly at first and is all gone at the end.")]
        [SerializeField, Min(0.1f)] float drainDuration = 15f;
        [Tooltip("Each wave comes from a random corner of the map (the ocean on two neighbouring edges), each corner " +
                 "equally likely. Drawn when the wave's countdown starts, so the storm already comes from there.")]
        [SerializeField] bool randomCorners = true;

        [Header("Fallback placement (scenes without a map adapter)")]
        [SerializeField] Vector3 fallbackSourcePosition = new Vector3(0f, 0f, -90f);
        [Tooltip("Travel direction in degrees: 0 = towards +Z (north), 90 = towards +X (east).")]
        [SerializeField] float fallbackDirection = 0f;

        TsunamiScenario current;            // live copy of the selected scenario (edited by the UI)
        bool hasCustomPosition;
        Vector3 customPosition;
        float timer;
        bool incomingRun;    // the current tsunami comes in from the open sea through the domain edge
        float warningLength; // simulation seconds from the start of the incoming wave to its release point
        BathymetryProfile appliedBathymetry;
        bool appliedBathymetryValid;
        bool gameStarted;
        bool releaseScheduled;
        float releaseRemaining;
        int wavesReleased;
        int aimedWave = -1;  // index of the wave whose corner has been drawn
        float dangerRemaining;
        float waveAge = -1f; // seconds since the latest wave was released (-1: none in progress)
        bool dangerShown;

        public TsunamiState State { get; private set; } = TsunamiState.Idle;
        /// <summary>True when the game's tsunamis come as a sequence of waves (see waves).</summary>
        public bool UsesWaves => autoRelease && waves != null && waves.Length > 0;
        public int WaveCount => UsesWaves ? waves.Length : 0;
        /// <summary>How many waves of the sequence have been released so far (the number of the latest one).</summary>
        public int WavesReleased => wavesReleased;
        /// <summary>Height (m) of the next wave of the sequence, or 0 when none is left.</summary>
        public float NextWaveHeight => UsesWaves && wavesReleased < waves.Length ? waves[wavesReleased].height : 0f;
        /// <summary>True while the next release (first tsunami or next wave) is counting down, in any state.</summary>
        public bool NextReleaseScheduled => releaseScheduled;
        public float NextReleaseRemaining => releaseScheduled ? Mathf.Max(0f, releaseRemaining) : 0f;
        /// <summary>
        /// A tsunami is announced or in progress: the villagers shelter. With waves, each wave is dangerous from its
        /// release for waveDuration seconds; in between the village is calm until the next one.
        /// </summary>
        public bool InDanger => UsesWaves ? State == TsunamiState.Warning || dangerRemaining > 0f : State != TsunamiState.Idle;
        public float WarningRemaining
        {
            get
            {
                if (State != TsunamiState.Warning) return 0f;
                return incomingRun ? Mathf.Max(0f, warningLength - (float)simulation.SimTime) : Mathf.Max(0f, timer);
            }
        }
        public float WarningDuration => warningDuration;
        /// <summary>
        /// Deep-sea speed of the incoming wave (m/s): long-wave speed sqrt(g (depth + height / 2)), the speed the
        /// simulated wave keeps in the deep part of the domain.
        /// </summary>
        public float IncomingSpeed => Mathf.Sqrt(9.81f * (Current.bathymetry.Validated().deepDepth + 0.5f * Mathf.Max(0f, Current.amplitude)));
        /// <summary>True while the automatic release is counting down.</summary>
        public bool ReleaseScheduled => releaseScheduled && State == TsunamiState.Idle;
        /// <summary>Seconds until the automatic release, or 0 when none is scheduled.</summary>
        public float ReleaseRemaining => ReleaseScheduled ? Mathf.Max(0f, releaseRemaining) : 0f;
        /// <summary>
        /// Seconds until the tsunami is released: the automatic countdown while it runs, 0 while a tsunami is
        /// announced, running or paused, infinity when nothing is scheduled (calm). Drives the storm.
        /// </summary>
        public float SecondsToRelease
        {
            get
            {
                if (InDanger) return 0f;
                return releaseScheduled ? Mathf.Max(0f, releaseRemaining) : float.PositiveInfinity;
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
        /// <summary>A wave of the sequence was released (0-based index).</summary>
        public event Action<int> WaveReleased;
        /// <summary>The last wave is over (waveDuration after its release): the game can be won or lost.</summary>
        public event Action WavesFinished;
        /// <summary>InDanger changed: the villagers shelter (true) or come out (false).</summary>
        public event Action<bool> DangerChanged;

        void OnEnable()
        {
            MainMenu.GameStarted += OnGameStarted;
            TutorialHints.TsunamiAllowed += OnTutorialAllowed;
        }

        void OnDisable()
        {
            MainMenu.GameStarted -= OnGameStarted;
            TutorialHints.TsunamiAllowed -= OnTutorialAllowed;
        }

        void OnGameStarted()
        {
            gameStarted = true;
            ScheduleRelease();
        }

        // The tutorial holds the countdown until it reaches the wall step (or is skipped).
        void OnTutorialAllowed()
        {
            if (!ReleaseScheduled) ScheduleRelease();
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
            // The countdown to the next release runs before the first tsunami and, with waves, while one is in
            // progress (the gap counts from the previous release); a paused tsunami holds it.
            if (releaseScheduled && State != TsunamiState.Paused)
            {
                releaseRemaining -= Time.deltaTime;
                if (releaseRemaining <= 0f)
                {
                    ReleaseNext();
                    return;
                }
            }
            if (waveAge >= 0f && State != TsunamiState.Paused) Drain(Time.deltaTime);
            if (dangerRemaining > 0f && State != TsunamiState.Paused)
            {
                dangerRemaining -= Time.deltaTime;
                if (dangerRemaining <= 0f)
                {
                    dangerRemaining = 0f;
                    CheckDanger();
                    if (UsesWaves && wavesReleased >= waves.Length) WavesFinished?.Invoke();
                    else AimNextWave(); // the land is dry again: the ocean can move to the next wave's corner
                }
            }
            if (State != TsunamiState.Warning) return;
            if (incomingRun)
            {
                // The wave is already on its way in the simulation: the release is only the moment its crest
                // passes the source point. Nothing is reset or added.
                if (simulation.SimTime >= warningLength) SetState(TsunamiState.Running);
                return;
            }
            timer -= Time.deltaTime;
            if (timer <= 0f) BeginRun();
        }

        /// <summary>
        /// Starts a new random countdown to the automatic release (when enabled, the game has started and the
        /// tutorial does not hold it).
        /// </summary>
        public void ScheduleRelease()
        {
            releaseScheduled = false;
            if (!autoRelease || !gameStarted || TutorialHints.HoldsTsunami || State != TsunamiState.Idle) return;
            if (UsesWaves)
            {
                if (wavesReleased >= waves.Length) return;
                releaseRemaining = Mathf.Max(0f, waves[wavesReleased].delay);
            }
            else
            {
                float min = Mathf.Min(releaseDelayMin, releaseDelayMax), max = Mathf.Max(releaseDelayMin, releaseDelayMax);
                releaseRemaining = UnityEngine.Random.Range(min, max);
            }
            releaseScheduled = true;
            Debug.Log($"[TsunamiController] Tsunami in {releaseRemaining:0} s.", this);
            AimNextWave();
        }

        // Draws the corner the next wave of the sequence comes from (once per wave), each corner 1 in 4.
        void AimNextWave()
        {
            if (!UsesWaves || !randomCorners || mapAdapter == null || wavesReleased >= waves.Length) return;
            if (aimedWave == wavesReleased) return;
            aimedWave = wavesReleased;
            Vector2Int corner = TeamMapTsunamiAdapter.Corners[UnityEngine.Random.Range(0, TeamMapTsunamiAdapter.Corners.Length)];
            Debug.Log($"[TsunamiController] Wave {wavesReleased + 1} comes from corner {corner}.", this);
            mapAdapter.AimFromCorner(corner);
        }

        /// <summary>
        /// Skips the countdown (testing): the next wave (or the tsunami) sets off now, with its warning / incoming
        /// swell. Without waves, or after the last one, the tsunami is reset and sent again.
        /// </summary>
        public void ReleaseNow()
        {
            if (!enabled || State == TsunamiState.Warning) return;
            if (UsesWaves && wavesReleased < waves.Length)
            {
                ReleaseNext();
                return;
            }
            if (State != TsunamiState.Idle) ResetSimulation();
            if (UsesWaves) ReleaseNext();
            else Play();
        }

        // Releases the next wave of the sequence at its height (or, without waves, the tsunami) and starts the
        // countdown to the one after it. A wave released while the last one is still in the water comes in on top
        // of it: the water is set back to rest at sea, the village keeps its damage.
        void ReleaseNext()
        {
            releaseScheduled = false;
            if (!UsesWaves || wavesReleased >= waves.Length)
            {
                PlayCurrent();
                return;
            }
            AimNextWave(); // released early (testing): its corner may not have been drawn yet
            int index = wavesReleased++;
            Current.amplitude = Mathf.Clamp(waves[index].height, MinWaveHeight, MaxWaveHeight);
            ParametersChanged?.Invoke();
            if (State == TsunamiState.Idle) PlayCurrent();
            else SendWave();
            dangerRemaining = waveDuration;
            waveAge = 0f;
            if (wavesReleased < waves.Length)
            {
                releaseRemaining = Mathf.Max(0f, waves[wavesReleased].delay);
                releaseScheduled = true;
            }
            Debug.Log($"[TsunamiController] Wave {index + 1}/{waves.Length}: {Current.amplitude:0} m.", this);
            CheckDanger();
            WaveReleased?.Invoke(index);
        }

        // During the drain window each frame removes the share of the remaining land water that leaves it all gone
        // at the window's end: the puddles shrink gradually instead of vanishing.
        void Drain(float dt)
        {
            float before = waveAge;
            waveAge += dt;
            float end = drainStart + drainDuration;
            if (waveAge <= drainStart) return;
            if (before >= end)
            {
                waveAge = -1f;
                return;
            }
            float from = Mathf.Max(before, drainStart);
            float left = end - from;
            simulation.DrainLand(waveAge >= end ? 1f : Mathf.Clamp01((waveAge - from) / left));
            if (waveAge >= end) waveAge = -1f;
        }

        // A new wave while one is in progress (or paused): sent in from the open sea like the first.
        void SendWave()
        {
            if (useWarning && warningDuration > 0f && UsesIncomingWave)
            {
                BeginIncoming(warningDuration, incomingStartHeight);
                SetState(TsunamiState.Warning);
            }
            else if (UsesIncomingWave)
            {
                BeginIncoming(0f, 1f);
                SetState(TsunamiState.Running);
            }
            else StartSource();
        }

        // ------------------------------------------------------------------ playback

        /// <summary>
        /// Starts the tsunami from the current initial conditions, or resumes when paused. With waves, starting from
        /// rest releases the next wave of the sequence.
        /// </summary>
        public void Play()
        {
            if (!enabled) return;
            if (State == TsunamiState.Idle && UsesWaves && wavesReleased < waves.Length)
            {
                ReleaseNext();
                return;
            }
            PlayCurrent();
        }

        void PlayCurrent()
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
                if (UsesIncomingWave)
                {
                    BeginIncoming(warningDuration, incomingStartHeight);
                }
                else
                {
                    incomingRun = false;
                    timer = warningDuration;
                }
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
                ResetSimulation(); // cancel the pending release (the wave may already be entering)
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
            incomingRun = false;
            wavesReleased = 0;
            aimedWave = -1;
            dangerRemaining = 0f;
            waveAge = -1f;
            SetState(TsunamiState.Idle);
            SimulationReset?.Invoke();
            ScheduleRelease();
        }

        /// <summary>Reset and immediately release the tsunami (no countdown); with waves, the first wave again.</summary>
        public void Restart()
        {
            ResetSimulation();
            if (UsesWaves) ReleaseNext();
            else BeginRun();
        }

        // Kept for older UI / scripts.
        public void StartTsunami() => Play();
        public void StopTsunami() => Pause();

        void BeginRun()
        {
            if (State != TsunamiState.Idle && State != TsunamiState.Warning) ResetSimulation();
            if (HasPendingChanges) ResetSimulation();
            if (UsesIncomingWave)
            {
                // No countdown: the wave starts just beyond the simulated area at its full height.
                BeginIncoming(0f, 1f);
                SetState(TsunamiState.Running);
                return;
            }
            StartSource();
        }

        // Fresh initial state: sea at rest + the scenario's disturbance at the source.
        void StartSource()
        {
            ApplyBathymetryIfNeeded(false);
            simulation.ResetWater();
            SimulationReset?.Invoke();
            TsunamiScenario s = Current;
            LastSourceVolume = simulation.AddSource(SourcePosition, TravelDirection, s.amplitude, s.lengthRadius,
                s.widthRadius, s.profile, s.motion);
            incomingRun = false;
            HasPendingChanges = false;
            releaseScheduled = false;
            simulation.Paused = false;
            SetState(TsunamiState.Running);
        }

        /// <summary>
        /// A directional Gaussian wave comes in from the open sea (the game's tsunami). Other set-ups (radial
        /// spreading, N-wave) start from an initial disturbance at the source as before.
        /// </summary>
        bool UsesIncomingWave => Current.profile == SourceProfile.Gaussian && Current.motion == SourceMotion.Directional;

        /// <summary>
        /// Sea at rest, then the wave on its way in: its crest starts far enough out that it reaches the source point
        /// after <paramref name="lead"/> seconds (at least: from beyond the domain edge), grows to the full height
        /// by the time it reaches the domain edge and flows in through it. From then on the simulation carries it.
        /// </summary>
        void BeginIncoming(float lead, float startShare)
        {
            ApplyBathymetryIfNeeded(false);
            simulation.ResetWater();
            SimulationReset?.Invoke();
            TsunamiScenario s = Current;
            Vector2 dir = TravelDirection;
            Vector3 source = SourcePosition;
            float speed = IncomingSpeed;
            float length = Mathf.Max(4f, s.lengthRadius);
            float toEdge = simulation.DistanceToEdge(source, -dir);
            // The whole hump starts outside the domain, so nothing appears inside out of nowhere.
            lead = Mathf.Max(lead, (toEdge + 3f * length) / speed);
            Vector3 crestAtStart = source - new Vector3(dir.x, 0f, dir.y) * (speed * lead);
            float growTime = Mathf.Max(0f, lead - toEdge / speed);
            simulation.StartIncomingWave(crestAtStart, dir, speed, s.amplitude, startShare, growTime, length,
                Mathf.Max(4f, s.widthRadius));
            LastSourceVolume = 0;
            incomingRun = true;
            warningLength = lead;
            HasPendingChanges = false;
            releaseScheduled = false;
            simulation.Paused = false;
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
            CheckDanger();
        }

        void CheckDanger()
        {
            bool danger = InDanger;
            if (danger == dangerShown) return;
            dangerShown = danger;
            DangerChanged?.Invoke(danger);
        }
    }
}
