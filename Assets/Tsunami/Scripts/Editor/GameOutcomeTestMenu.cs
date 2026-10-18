using UnityEditor;
using UnityEngine;

namespace Tsunami.EditorTools
{
    /// <summary>Play-mode shortcut for testing the win/lose card without waiting for the countdown.</summary>
    static class GameOutcomeTestMenu
    {
        [MenuItem("Tsunami/Test: Release Tsunami Now")]
        static void ReleaseNow()
        {
            var controller = Object.FindAnyObjectByType<TsunamiController>();
            if (controller != null)
                controller.ReleaseNow();
        }

        [MenuItem("Tsunami/Test: Release Tsunami Now", true)]
        static bool CanRelease() => Application.isPlaying;
    }
}
