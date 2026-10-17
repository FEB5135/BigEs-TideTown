using System;
using System.Linq;
using System.Reflection;
using IdleTown.Construction;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Tsunami.EditorTools
{
    // Keeps the existing team map; no destructive scene rebuild is needed for the collision upgrade.
    [InitializeOnLoad]
    public static class CollisionIntegrationSetup
    {
        const string ScenePath = "Assets/Scenes/SampleScene.unity";
        const string SmokeKey = "CollisionIntegration.Smoke";
        static double readyAt;

        static CollisionIntegrationSetup()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (!SessionState.GetBool(SmokeKey, false)) return;
                if (state == PlayModeStateChange.EnteredPlayMode)
                {
                    readyAt = EditorApplication.timeSinceStartup + 3;
                    EditorApplication.update += RunRuntimeCheck;
                }
            };
        }

        [MenuItem("Tsunami/Collision/Validate and Save Team Map")]
        public static void PrepareTeamMap()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Stop Play Mode before preparing the map.");
            var scene = SceneManager.GetActiveScene();
            if (scene.path != ScenePath)
            {
                if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
                scene = EditorSceneManager.OpenScene(ScenePath);
            }
            var adapter = Find<TeamMapTsunamiAdapter>(scene);
            var sim = Find<WaterSimulation>(scene);
            var controller = Find<WallConstructionController>(scene);
            var splash = Find<ImpactSplashEmitter>(scene);
            var tsunami = Find<TsunamiController>(scene);
            Require(adapter && sim && controller && splash && tsunami, "Team map tsunami components are missing.");
            var wallSO = new SerializedObject(controller);
            var world = wallSO.FindProperty("world").objectReferenceValue as World;
            Require(world != null, "Construction World is not assigned.");
            var simSO = new SerializedObject(sim);
            var terrain = simSO.FindProperty("terrain").objectReferenceValue as TerrainGrid;
            Require(terrain != null, "WaterSimulation Terrain is not assigned.");
            var adapterSO = new SerializedObject(adapter);
            adapterSO.FindProperty("world").objectReferenceValue = world;
            adapterSO.FindProperty("terrain").objectReferenceValue = terrain;
            adapterSO.FindProperty("simulation").objectReferenceValue = sim;
            adapterSO.FindProperty("wallController").objectReferenceValue = controller;
            adapterSO.ApplyModifiedPropertiesWithoutUndo();
            var splashSO = new SerializedObject(splash);
            splashSO.FindProperty("simulation").objectReferenceValue = sim;
            splashSO.ApplyModifiedPropertiesWithoutUndo();
            var tsunamiSO = new SerializedObject(tsunami);
            tsunamiSO.FindProperty("simulation").objectReferenceValue = sim;
            tsunamiSO.FindProperty("mapAdapter").objectReferenceValue = adapter;
            tsunamiSO.ApplyModifiedPropertiesWithoutUndo();
            var menu = Find<MainMenu>(scene);
            var ui = Find<TsunamiUI>(scene);
            if (menu != null && ui != null)
            {
                // Use the game's existing intro/fade lifecycle rather than showing simulation controls over the menu.
                var visibility = ui.GetComponent<CanvasGroup>();
                if (visibility == null) visibility = ui.gameObject.AddComponent<CanvasGroup>();
                visibility.alpha = 0f;
                visibility.interactable = false;
                visibility.blocksRaycasts = false;
                var menuSO = new SerializedObject(menu);
                var groups = menuSO.FindProperty("gameplayUI");
                bool registered = false;
                for (int i = 0; i < groups.arraySize; i++)
                    registered |= groups.GetArrayElementAtIndex(i).objectReferenceValue == visibility;
                if (!registered)
                {
                    int i = groups.arraySize++;
                    groups.GetArrayElementAtIndex(i).objectReferenceValue = visibility;
                    menuSO.ApplyModifiedPropertiesWithoutUndo();
                }
                EditorUtility.SetDirty(visibility);
            }
            Require(splash.GetComponent<ParticleSystemRenderer>().sharedMaterial != null, "Splash material is missing.");
            // Persist the newly introduced serialized defaults without replacing existing gameplay settings.
            EditorUtility.SetDirty(adapter);
            EditorUtility.SetDirty(splash);
            EditorSceneManager.MarkSceneDirty(scene);
            Require(EditorSceneManager.SaveScene(scene), "Could not save the team map.");
            Debug.Log("[CollisionIntegration] Team map saved: matching construction/simulation World, damage settings and splash links.");
        }

        // Batch entry point; this modifies only in-memory Play Mode objects after saving the clean scene.
        public static void PrepareAndSmoke()
        {
            PrepareTeamMap();
            SessionState.SetBool(SmokeKey, true);
            EditorApplication.isPlaying = true;
        }

        static void RunRuntimeCheck()
        {
            if (EditorApplication.timeSinceStartup < readyAt) return;
            EditorApplication.update -= RunRuntimeCheck;
            try
            {
                var scene = SceneManager.GetActiveScene();
                var adapter = Find<TeamMapTsunamiAdapter>(scene);
                var sim = Find<WaterSimulation>(scene);
                var wall = Find<WallConstructionController>(scene);
                sim.Paused = true;
                wall.enabled = true;
                wall.SetResourceStore(new TestResources());
                var a = new Vector2Int(30, 30);
                var b = a + Vector2Int.right;
                var c = b + Vector2Int.right;
                Require(wall.TryBuild(a, c, out var reason), reason);
                Require(wall.Edges.Count == 2 && adapter.WallSegments == 2, "Built walls were not registered with water.");
                var edge = wall.Edges.First(e => e.A == a || e.B == a);
                Require(wall.TryGetWallInstance(edge, out var instance), "Wall instance lookup failed.");
                var world = adapter.World;
                var pos = world.transform.TransformPoint(new Vector3(a.x * world.CellSize, 0, a.y * world.CellSize));
                var g = sim.Terrain.WorldToGrid(pos);
                int x = Mathf.RoundToInt(g.x + .5f);
                int z = Mathf.RoundToInt(g.y + .5f) - 1;
                int i = z * sim.Solver.Size + x;
                Require(sim.Solver.BarrierZ[i] > float.NegativeInfinity, "Built wall has no water barrier.");
                // Exercise the real sampling/event/damage/removal pipeline with a deterministic normal inflow.
                sim.Solver.Depth[i] = 1f;
                sim.Solver.VelocityZ[i] = 30f;
                typeof(WaterSimulation).GetMethod("SampleWallLoads", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(sim, new object[] { 1f });
                typeof(TeamMapTsunamiAdapter).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(adapter, null);
                Require(wall.Edges.Count == 1 && adapter.WallSegments == 1, "Damaged segment did not break independently.");
                Require(!wall.TryGetWallInstance(edge, out _), "Broken segment is still registered in construction.");
                Require(float.IsNegativeInfinity(sim.Solver.BarrierZ[i]), "Broken segment left an invisible water barrier.");
                Require(!float.IsNegativeInfinity(sim.Solver.BarrierZ[i + 1]), "Neighbouring wall barrier was removed.");
                Require(instance != null && instance.GetComponent<Rigidbody>() != null, "Broken wall did not become physical debris.");
                Require(wall.TryBuild(a, b, out reason), "Cannot rebuild broken edge: " + reason);
                Require(adapter.WallSegments == 2, "Rebuilt segment did not restore the water barrier.");
                Debug.Log("[CollisionIntegration] RUNTIME PASS: real team map build -> water registration -> sampled load -> damage -> debris -> barrier removal; neighbour preserved; same edge rebuilt.");
                SessionState.SetBool(SmokeKey, false);
                EditorApplication.Exit(0);
            }
            catch (Exception error)
            {
                Debug.LogException(error);
                SessionState.SetBool(SmokeKey, false);
                EditorApplication.Exit(1);
            }
        }

        static T Find<T>(Scene scene) where T : Component => scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<T>(true)).FirstOrDefault();
        static void Require(bool condition, string reason)
        {
            if (!condition) throw new InvalidOperationException(reason);
        }
        sealed class TestResources : IWallResourceStore
        {
            public int Wood => 100000;
            public int Stone => 100000;
            public bool CanAfford(int wood, int stone) => wood >= 0 && stone >= 0;
            public bool TrySpend(int wood, int stone) => CanAfford(wood, stone);
        }
    }
}
