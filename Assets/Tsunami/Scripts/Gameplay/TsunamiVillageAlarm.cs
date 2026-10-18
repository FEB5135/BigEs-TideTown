using UnityEngine;
using UnityEngine.SceneManagement;

namespace Tsunami
{
    /// <summary>
    /// Tells the villagers about the tsunami alarm: from the warning until the reset they drop their work and run
    /// into their houses (Gatherer.SetAlarm). Gatherer lives in the IdleTown.Runtime assembly, which cannot see this
    /// one, so the link is made from here. Needs no scene setup: it finds the controller whenever a scene loads.
    /// </summary>
    static class TsunamiVillageAlarm
    {
        static TsunamiController controller;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Init()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            Hook();
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => Hook();

        static void Hook()
        {
            if (controller != null)
                controller.StateChanged -= OnStateChanged;
            controller = Object.FindAnyObjectByType<TsunamiController>();
            Gatherer.SetAlarm(false);
            if (controller == null)
                return;
            controller.StateChanged += OnStateChanged;
            OnStateChanged(controller.State);
        }

        static void OnStateChanged(TsunamiState state) => Gatherer.SetAlarm(state != TsunamiState.Idle);
    }
}
