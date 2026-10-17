using System.Collections;
using System.Linq;
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
                    yield return new WaitForSecondsRealtime(1.3f);
                    var tour = menu.GetComponent<WelcomeMapTour>();
                    Assert.That(tour.IsMoving, Is.False, "Camera flights must work while gameplay is paused.");
                    var view = tour.ViewCamera.WorldToViewportPoint(tour.FocusBounds.center);
                    Assert.That(view.z, Is.GreaterThan(0));
                    Assert.That(view.x, Is.InRange(0.2f, 0.65f));
                    Assert.That(view.y, Is.InRange(0.35f, 0.8f));
                    if (page == 1 || page == 2)
                    {
                        var type = page == 1 ? ResourceType.Wood : ResourceType.Stone;
                        Assert.That(Object.FindObjectsByType<ResourceNode>().Any(n => n.Type == type && n.Remaining > 0 &&
                            Vector3.Distance(n.transform.position, tour.FocusBounds.center) < tour.FocusBounds.extents.magnitude + 5), Is.True,
                            "Forest and mine steps must point at actual resource nodes.");
                    }
                }
                welcome.PreviousPage();
                Assert.That(welcome.PageIndex, Is.EqualTo(welcome.PageCount - 2));
                welcome.NextPage();
                welcome.NextPage();
                Assert.That(welcome.IsOpen, Is.False);
                Assert.That(Time.timeScale, Is.EqualTo(1f));
                Assert.That(starts, Is.EqualTo(1));
                Assert.That(camera.InputEnabled && harvester.enabled && construction.enabled, Is.True);

                // Reopening is help, not a new session; preserve custom pause speed and disabled controls.
                Time.timeScale = 0.5f;
                harvester.enabled = false;
                var returnFocus = camera.FocusPoint + new Vector3(7, 0, -3);
                camera.SetPose(returnFocus, 66, camera.Pitch, camera.Yaw);
                welcome.Show();
                Assert.That(welcome.PageIndex, Is.Zero);
                Assert.That(Time.timeScale, Is.Zero);
                Assert.That(construction.enabled, Is.False);
                welcome.NextPage();
                yield return new WaitForSecondsRealtime(0.3f);
                welcome.Complete();
                welcome.Complete();
                Assert.That(Vector3.Distance(camera.FocusPoint, returnFocus), Is.LessThan(0.001f));
                Assert.That(camera.Distance, Is.EqualTo(66f).Within(0.001f));
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
