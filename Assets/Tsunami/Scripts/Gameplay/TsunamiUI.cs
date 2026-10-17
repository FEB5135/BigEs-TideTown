using UnityEngine;
using UnityEngine.UI;

namespace Tsunami
{
    /// <summary>
    /// Wires the Start / Stop / Reset buttons to the TsunamiController and shows its state.
    /// Interactability is refreshed only when the controller's state changes.
    /// </summary>
    public class TsunamiUI : MonoBehaviour
    {
        [SerializeField] TsunamiController controller;
        [SerializeField] Button startButton;
        [SerializeField] Button stopButton;
        [Tooltip("Optional.")]
        [SerializeField] Button resetButton;
        [SerializeField] Text statusText;
        [Tooltip("Large countdown / alert label. Optional.")]
        [SerializeField] Text countdownText;

        int shownSeconds = -1;

        void OnEnable()
        {
            if (controller == null)
            {
                Debug.LogError("[TsunamiUI] No TsunamiController assigned.", this);
                return;
            }
            if (startButton != null) startButton.onClick.AddListener(controller.StartTsunami);
            if (stopButton != null) stopButton.onClick.AddListener(controller.StopTsunami);
            if (resetButton != null) resetButton.onClick.AddListener(controller.ResetSimulation);
            controller.StateChanged += OnStateChanged;
            OnStateChanged(controller.State);
        }

        void OnDisable()
        {
            if (controller == null) return;
            if (startButton != null) startButton.onClick.RemoveListener(controller.StartTsunami);
            if (stopButton != null) stopButton.onClick.RemoveListener(controller.StopTsunami);
            if (resetButton != null) resetButton.onClick.RemoveListener(controller.ResetSimulation);
            controller.StateChanged -= OnStateChanged;
        }

        void Update()
        {
            if (controller == null || countdownText == null) return;
            // Key encodes what the label shows, so the string is rebuilt only when it changes.
            int key = -1;
            if (controller.State == TsunamiState.Warning) key = Mathf.CeilToInt(controller.WarningRemaining);
            else if (controller.State == TsunamiState.Running) key = 1000 + controller.CurrentWave;
            if (key == shownSeconds) return;
            shownSeconds = key;
            if (key >= 1000)
            {
                int wave = key - 1000;
                countdownText.text = controller.WaveCount == 1 ? "THE WAVE IS COMING"
                    : controller.WaveCount > 0 ? $"WAVE {wave} OF {controller.WaveCount} INCOMING"
                    : $"WAVE {wave} INCOMING";
            }
            else if (key >= 0) countdownText.text = $"TSUNAMI WARNING\nThe sea is pulling back... {key}";
            else countdownText.text = string.Empty;
        }

        void OnStateChanged(TsunamiState state)
        {
            bool active = state == TsunamiState.Warning || state == TsunamiState.Running;
            if (startButton != null) startButton.interactable = !active;
            if (stopButton != null) stopButton.interactable = active;
            if (statusText != null) statusText.text = "Status: " + Describe(state);
            shownSeconds = int.MinValue; // force countdown label refresh
            if (countdownText != null && state != TsunamiState.Warning && state != TsunamiState.Running)
                countdownText.text = string.Empty;
        }

        string Describe(TsunamiState state)
        {
            switch (state)
            {
                case TsunamiState.Warning: return "WARNING - sea is drawing back";
                case TsunamiState.Running: return "TSUNAMI ACTIVE - water incoming";
                case TsunamiState.Stopped:
                    return controller.StopReason == TsunamiStopReason.WaveEnded
                        ? "Wave passed - water settling"
                        : "Stopped - no more water incoming";
                default: return "Idle - calm sea";
            }
        }
    }
}
