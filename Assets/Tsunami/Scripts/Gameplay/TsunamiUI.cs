using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace Tsunami
{
    /// <summary>
    /// In-game tsunami panel: wave height slider, Start / Pause-Resume / Reset / Release now, status and countdown.
    /// The wave height is an initial condition: it is used the next time the tsunami is started or restarted.
    /// Labels and interactability refresh only when something changes.
    /// </summary>
    public class TsunamiUI : MonoBehaviour
    {
        [SerializeField] TsunamiController controller;
        [Tooltip("Play; becomes 'Restart' while a tsunami is in progress.")]
        [SerializeField] Button startButton;
        [Tooltip("Pause / Resume.")]
        [SerializeField] Button stopButton;
        [SerializeField] Button resetButton;
        [Tooltip("Releases the tsunami at once, skipping the storm countdown (testing). Optional.")]
        [SerializeField] Button releaseNowButton;
        [Tooltip("Height (m) of the initial sea-surface bulge out at sea.")]
        [SerializeField] Slider waveHeightSlider;
        [SerializeField] Text waveHeightText;
        [SerializeField] Text statusText;
        [Tooltip("Large countdown / alert label. Optional.")]
        [SerializeField] Text countdownText;
        [Tooltip("Seconds the 'get ready for the next wave' notice stays on the large label.")]
        [SerializeField, Min(0f)] float prepareNoticeTime = 5f;
        [Tooltip("With waves, the large countdown appears only this many seconds before the wave hits; until then " +
                 "the storm clouds are the only sign of how close it is.")]
        [SerializeField, Min(0f)] float finalCountdown = 10f;

        const float StormNoticeLeadSeconds = 30f;
        const float StormNoticeDuration = 5f;

        int shownKey = int.MinValue;
        int shownReleaseSecond = int.MinValue;
        int shownWaves = int.MinValue;
        bool shownDanger;
        bool calmCountdown;  // the next wave is counting down and the village is out of danger
        float prepareUntil = -1f;
        bool shownPending;
        Text startLabel, stopLabel;
        CanvasGroup visibility;

        void Awake()
        {
            // Keep wave alerts, remove the developer control panel from gameplay.
            var controls = transform.Find("Control Panel");
            if (controls != null) controls.gameObject.SetActive(false);
            if (startButton != null) startLabel = startButton.GetComponentInChildren<Text>();
            if (stopButton != null) stopLabel = stopButton.GetComponentInChildren<Text>();
            visibility = GetComponent<CanvasGroup>();
            MainMenu.GameStarted += Reveal;
        }

        void OnDestroy() => MainMenu.GameStarted -= Reveal;

        // The panel is hidden (CanvasGroup alpha 0) while the main menu is open. It reveals itself when the game
        // starts instead of relying only on the menu's gameplayUI list, which can lose the entry when the menu is
        // rebuilt. Without a menu in the scene it is shown at once.
        void Start()
        {
            if (visibility != null && visibility.alpha < 1f && FindAnyObjectByType<MainMenu>() == null) Reveal();
        }

        void Reveal()
        {
            if (visibility == null || !isActiveAndEnabled) return;
            StartCoroutine(FadeIn());
        }

        IEnumerator FadeIn()
        {
            const float duration = 0.4f;
            float from = visibility.alpha;
            for (float t = 0f; t < duration && visibility.alpha < 1f; t += Time.unscaledDeltaTime)
            {
                visibility.alpha = Mathf.Max(visibility.alpha, Mathf.Lerp(from, 1f, t / duration));
                yield return null;
            }
            visibility.alpha = 1f;
            visibility.interactable = visibility.blocksRaycasts = true;
        }

        void OnEnable()
        {
            if (controller == null)
            {
                Debug.LogError("[TsunamiUI] No TsunamiController assigned.", this);
                return;
            }
            if (startButton != null) startButton.onClick.AddListener(OnStart);
            if (stopButton != null) stopButton.onClick.AddListener(controller.TogglePause);
            if (resetButton != null) resetButton.onClick.AddListener(controller.ResetSimulation);
            if (releaseNowButton != null) releaseNowButton.onClick.AddListener(controller.ReleaseNow);
            if (waveHeightSlider != null)
            {
                waveHeightSlider.minValue = TsunamiController.MinWaveHeight;
                waveHeightSlider.maxValue = TsunamiController.MaxWaveHeight;
                waveHeightSlider.SetValueWithoutNotify(controller.WaveHeight);
                waveHeightSlider.onValueChanged.AddListener(OnWaveHeight);
            }
            controller.StateChanged += OnStateChanged;
            OnStateChanged(controller.State);
            RefreshWaveHeight();
        }

        void OnDisable()
        {
            if (controller == null) return;
            if (startButton != null) startButton.onClick.RemoveListener(OnStart);
            if (stopButton != null) stopButton.onClick.RemoveListener(controller.TogglePause);
            if (resetButton != null) resetButton.onClick.RemoveListener(controller.ResetSimulation);
            if (releaseNowButton != null) releaseNowButton.onClick.RemoveListener(controller.ReleaseNow);
            if (waveHeightSlider != null) waveHeightSlider.onValueChanged.RemoveListener(OnWaveHeight);
            controller.StateChanged -= OnStateChanged;
        }

        void OnStart()
        {
            if (controller.State == TsunamiState.Idle) controller.Play();
            else controller.Restart();
        }

        void OnWaveHeight(float value)
        {
            controller.WaveHeight = value;
            RefreshWaveHeight();
        }

        void RefreshWaveHeight()
        {
            shownPending = controller.HasPendingChanges;
            if (waveHeightSlider != null) waveHeightSlider.SetValueWithoutNotify(controller.WaveHeight);
            if (waveHeightText == null) return;
            string note = shownPending ? "  (yeniden başlatınca)" : string.Empty;
            waveHeightText.text = $"Dalga yüksekliği: {controller.WaveHeight:0.0} m{note}";
        }

        void Update()
        {
            if (controller == null) return;
            if (controller.HasPendingChanges != shownPending) RefreshWaveHeight();
            int releaseSecond = controller.NextReleaseScheduled ? Mathf.CeilToInt(controller.NextReleaseRemaining) : -1;
            if (releaseSecond != shownReleaseSecond || controller.WavesReleased != shownWaves || controller.InDanger != shownDanger)
            {
                RefreshStatus();
                RefreshWaveHeight();
            }
            // A calm countdown to the next wave begins at the game start and when the water of the last one has
            // drained: tell the player to get ready.
            bool calm = controller.WaveCount > 0 && controller.NextReleaseScheduled && !controller.InDanger;
            if (calm && !calmCountdown) prepareUntil = Time.time + prepareNoticeTime;
            calmCountdown = calm;
            if (countdownText == null) return;
            int key = -1;
            if (controller.State == TsunamiState.Warning)
            {
                float left = controller.WarningRemaining;
                if (controller.WaveCount == 0 || left <= finalCountdown) key = Mathf.CeilToInt(left);
            }
            else if (controller.NextReleaseScheduled &&
                     controller.NextReleaseRemaining <= StormNoticeLeadSeconds &&
                     controller.NextReleaseRemaining > StormNoticeLeadSeconds - StormNoticeDuration) key = 3000;
            else if (controller.State == TsunamiState.Running && controller.Simulation != null && controller.Simulation.SimTime < 6.0) key = 1000;
            else if (calm && Time.time < prepareUntil) key = 2000;
            if (key == shownKey) return;
            shownKey = key;
            string wave = controller.WaveCount > 0 ? $"{controller.WavesReleased}. DALGA" : "TSUNAMİ";
            if (key == 3000) countdownText.text = "Kara bulutlar yaklaşıyor";
            else if (key == 2000) countdownText.text = $"{controller.WavesReleased + 1}. DALGAYA HAZIRLAN!";
            else if (key == 1000) countdownText.text = $"{wave} GELİYOR";
            else if (key >= 0) countdownText.text = controller.WaveCount > 0 ? $"{wave} ({controller.WaveHeight:0} m)\n{key}" : $"TSUNAMİ UYARISI\n{key}";
            else countdownText.text = string.Empty;
        }

        void OnStateChanged(TsunamiState state)
        {
            bool started = state != TsunamiState.Idle;
            if (startLabel != null) startLabel.text = started ? "Yeniden Başlat" : "Tsunamiyi Başlat";
            if (stopButton != null) stopButton.interactable = state == TsunamiState.Running || state == TsunamiState.Paused || state == TsunamiState.Warning;
            if (stopLabel != null) stopLabel.text = state == TsunamiState.Paused ? "Devam" : "Duraklat";
            RefreshStatus();
            shownKey = int.MinValue;
            RefreshWaveHeight();
        }

        void RefreshStatus()
        {
            shownReleaseSecond = controller.NextReleaseScheduled ? Mathf.CeilToInt(controller.NextReleaseRemaining) : -1;
            shownWaves = controller.WavesReleased;
            shownDanger = controller.InDanger;
            if (statusText == null) return;
            string time = $"{shownReleaseSecond / 60}:{shownReleaseSecond % 60:00}";
            if (controller.WaveCount == 0)
            {
                statusText.text = shownReleaseSecond >= 0 && controller.State == TsunamiState.Idle
                    ? $"Durum: Fırtına yaklaşıyor - tsunamiye {time}"
                    : "Durum: " + Describe(controller.State);
                return;
            }
            // The wave sequence: what is happening now. No times: the storm clouds tell how close the next wave is.
            int released = controller.WavesReleased, count = controller.WaveCount;
            bool coming = shownReleaseSecond >= 0;
            string now;
            if (controller.State == TsunamiState.Paused) now = Describe(TsunamiState.Paused);
            else if (released == 0) now = coming ? "Fırtına yaklaşıyor" : Describe(controller.State);
            else if (controller.InDanger) now = released == count ? $"Son dalga ({released}/{count})" : $"{released}. dalga";
            else now = released == count ? "Dalgalar bitti" : coming ? $"Sakin - {released + 1}. dalga gelecek" : "Sakin";
            statusText.text = $"Durum: {now}";
        }

        public static string Describe(TsunamiState state)
        {
            switch (state)
            {
                case TsunamiState.Warning: return "Uyarı - dalga birazdan bırakılacak";
                case TsunamiState.Running: return "Tsunami ilerliyor";
                case TsunamiState.Paused: return "Duraklatıldı";
                default: return "Hazır - deniz durgun";
            }
        }
    }
}
