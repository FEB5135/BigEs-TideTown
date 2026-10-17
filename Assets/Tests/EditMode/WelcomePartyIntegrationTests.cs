using System.Collections;
using IdleTown.Construction;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;

namespace IdleTown.Tests
{
    public sealed class WelcomePartyIntegrationTests
    {
        [UnityTest]
        public IEnumerator Welcome_GatesGameStartPausesAndRestoresControlsAndCanBeReopened()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity", OpenSceneMode.Single);
            yield return new EnterPlayMode();
            yield return null;
            var menu = Object.FindAnyObjectByType<MainMenu>();
            var camera = Object.FindAnyObjectByType<IsometricCamera>();
            var harvester = Object.FindAnyObjectByType<ClickHarvester>();
            var construction = Object.FindAnyObjectByType<WallConstructionController>();
            int starts = 0;
            void CountStart() => starts++;
            MainMenu.GameStarted += CountStart;
            try
            {
                menu.Play();
                float deadline = Time.realtimeSinceStartup + 10f;
                var welcome = Object.FindAnyObjectByType<WelcomeParty>();
                while (!welcome.IsOpen && Time.realtimeSinceStartup < deadline)
                    yield return null;
                Assert.That(welcome.IsOpen, Is.True);
                Assert.That(starts, Is.Zero, "Gameplay must wait until the guide is closed.");
                Assert.That(Time.timeScale, Is.Zero);
                Assert.That(camera.InputEnabled, Is.False);
                Assert.That(harvester.enabled, Is.False);
                Assert.That(construction.enabled, Is.False);
                var worker = Object.FindAnyObjectByType<Gatherer>();
                var position = worker.transform.position;
                yield return new WaitForSecondsRealtime(0.15f);
                Assert.That(Vector3.Distance(position, worker.transform.position), Is.LessThan(0.001f));
                welcome.PreviousPage();
                Assert.That(welcome.PageIndex, Is.Zero);
                for (int page = 1; page < welcome.PageCount; page++)
                {
                    welcome.NextPage();
                    Assert.That(welcome.PageIndex, Is.EqualTo(page));
                }
                welcome.PreviousPage();
                Assert.That(welcome.PageIndex, Is.EqualTo(2));
                welcome.NextPage();
                welcome.NextPage();
                Assert.That(welcome.IsOpen, Is.False);
                Assert.That(Time.timeScale, Is.EqualTo(1f));
                Assert.That(starts, Is.EqualTo(1));
                Assert.That(camera.InputEnabled && harvester.enabled && construction.enabled, Is.True);

                // Reopening is help, not a new session; preserve custom pause speed and disabled controls.
                Time.timeScale = 0.5f;
                harvester.enabled = false;
                welcome.Show();
                Assert.That(welcome.PageIndex, Is.Zero);
                Assert.That(Time.timeScale, Is.Zero);
                Assert.That(construction.enabled, Is.False);
                welcome.Complete();
                welcome.Complete();
                Assert.That(Time.timeScale, Is.EqualTo(0.5f));
                Assert.That(harvester.enabled, Is.False);
                Assert.That(camera.InputEnabled && construction.enabled, Is.True);
                Assert.That(starts, Is.EqualTo(1));
                welcome.Show();
                welcome.enabled = false;
                Assert.That(Time.timeScale, Is.EqualTo(0.5f), "Disabling the guide must release its pause.");
                Assert.That(welcome.IsOpen, Is.False);
                welcome.enabled = true;
                welcome.Show();
                var ui = GameObject.Find("Welcome Party");
                Object.Destroy(ui);
                yield return null;
                welcome.enabled = false;
                Assert.That(Time.timeScale, Is.EqualTo(0.5f));
            }
            finally
            {
                MainMenu.GameStarted -= CountStart;
                Time.timeScale = 1f;
            }
        }

        [UnityTearDown]
        public IEnumerator LeavePlayMode()
        {
            if (Application.isPlaying)
                yield return new ExitPlayMode();
        }
    }
}
