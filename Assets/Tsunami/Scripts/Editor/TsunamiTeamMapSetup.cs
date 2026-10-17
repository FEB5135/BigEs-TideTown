using System.Collections.Generic;
using IdleTown.Construction;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

namespace Tsunami.EditorTools
{
    /// <summary>
    /// Menu: Tsunami > Add Tsunami To Open Scene (Team Map).
    /// Adds the tsunami to the team's map (e.g. SampleScene) under a single "Tsunami" object:
    /// water grid fitted to the World, mountains as terrain, player-built walls as barriers, the Village's
    /// houses tracked for flooding, and a Start/Stop/Reset panel in the top-right corner.
    /// Nothing else in the scene is modified; delete the "Tsunami" object to remove it again.
    /// </summary>
    public static class TsunamiTeamMapSetup
    {
        const string RootName = "Tsunami";
        const string TeamScenePath = "Assets/Scenes/SampleScene.unity";

        /// <summary>Batch-mode entry point (no dialogs): opens the team map, adds / replaces the tsunami and saves.</summary>
        public static void AddToTeamMapBatch()
        {
            EditorSceneManager.OpenScene(TeamScenePath, OpenSceneMode.Single);
            AddToOpenScene();
        }

        // Dialogs answer "OK / Continue" automatically in batch mode.
        static bool Ask(string title, string message, string ok, string cancel = null)
        {
            if (Application.isBatchMode)
            {
                Debug.Log($"[Tsunami] {message.Replace('\n', ' ')}");
                return true;
            }
            return cancel == null ? EditorUtility.DisplayDialog(title, message, ok) : EditorUtility.DisplayDialog(title, message, ok, cancel);
        }

        [MenuItem("Tsunami/Add Tsunami To Open Scene (Team Map)")]
        public static void AddToOpenScene()
        {
            Scene scene = SceneManager.GetActiveScene();
            var world = FindActive<World>(scene);
            if (world == null)
            {
                // Not the team map (e.g. the TsunamiDemo island is open): offer to open the team's scene.
                if (AssetDatabase.LoadAssetAtPath<SceneAsset>(TeamScenePath) == null)
                {
                    Ask("Tsunami", $"No World in '{scene.name}', and {TeamScenePath} was not found.", "OK");
                    return;
                }
                if (!Ask("Tsunami",
                        $"'{scene.name}' is not the team map (it has no World).\n\nOpen the team's map ({TeamScenePath}) and add the tsunami there?",
                        "Open team map", "Cancel"))
                    return;
                if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
                scene = EditorSceneManager.OpenScene(TeamScenePath, OpenSceneMode.Single);
                world = FindActive<World>(scene);
                if (world == null)
                {
                    Ask("Tsunami", $"{TeamScenePath} has no active World.", "OK");
                    return;
                }
            }

            GameObject existing = FindRoot(scene, RootName);
            string action = existing != null ? "replace the existing \"Tsunami\" object in" : "add a \"Tsunami\" object to";
            if (!Ask("Tsunami",
                    $"This will {action} '{scene.name}' and save the scene.\n\nNothing else in the scene is changed. " +
                    "To remove the tsunami later, delete the \"Tsunami\" object.", "Continue", "Cancel"))
                return;
            if (existing != null) Object.DestroyImmediate(existing);

            var wallController = FindActive<WallConstructionController>(scene);
            Transform village = FindByName(scene, "Village");
            Transform mountains = FindByName(scene, "Mountains");
            bool hasEventSystem = FindActive<EventSystem>(scene) != null;
            TsunamiSceneBuilder.Materials mats = TsunamiSceneBuilder.CreateMaterials();

            var root = new GameObject(RootName);
            SceneManager.MoveGameObjectToScene(root, scene);

            // Simulation terrain: fed from the team's map; its own mesh is not drawn (their ground is).
            var terrainGO = Child(root, "Tsunami Terrain", typeof(MeshFilter), typeof(MeshRenderer));
            var terrainRenderer = terrainGO.GetComponent<MeshRenderer>();
            TsunamiSceneBuilder.ConfigureRenderer(terrainGO, mats.terrain);
            terrainRenderer.enabled = false;
            var terrain = terrainGO.AddComponent<TerrainGrid>();

            var sim = Child(root, "Water Simulation").AddComponent<WaterSimulation>();
            TsunamiSceneBuilder.SetRef(sim, "terrain", terrain);

            var adapter = Child(root, "Team Map Adapter").AddComponent<TeamMapTsunamiAdapter>();
            var adapterSO = new SerializedObject(adapter);
            adapterSO.FindProperty("world").objectReferenceValue = world;
            adapterSO.FindProperty("terrain").objectReferenceValue = terrain;
            adapterSO.FindProperty("simulation").objectReferenceValue = sim;
            adapterSO.FindProperty("wallController").objectReferenceValue = wallController;
            SerializedProperty roots = adapterSO.FindProperty("terrainMeshRoots");
            roots.arraySize = mountains != null ? 1 : 0;
            if (mountains != null) roots.GetArrayElementAtIndex(0).objectReferenceValue = mountains;
            adapterSO.ApplyModifiedPropertiesWithoutUndo();

            var waterGO = Child(root, "Flood Water Surface", typeof(MeshFilter), typeof(MeshRenderer));
            TsunamiSceneBuilder.ConfigureRenderer(waterGO, mats.flood);
            TsunamiSceneBuilder.SetRef(waterGO.AddComponent<WaterMeshRenderer>(), "simulation", sim);

            var oceanGO = Child(root, "Ocean", typeof(MeshFilter), typeof(MeshRenderer));
            TsunamiSceneBuilder.ConfigureRenderer(oceanGO, mats.ocean);
            var ocean = oceanGO.AddComponent<OceanRing>();
            TsunamiSceneBuilder.SetRef(ocean, "terrain", terrain);
            TsunamiSceneBuilder.SetRef(ocean, "simulation", sim);

            var sprayGO = Child(root, "Water Spray", typeof(ParticleSystem));
            var sprayRenderer = sprayGO.GetComponent<ParticleSystemRenderer>();
            sprayRenderer.sharedMaterial = mats.spray;
            sprayRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            sprayRenderer.receiveShadows = false;
            TsunamiSceneBuilder.SetRef(sprayGO.AddComponent<WaterSprayEmitter>(), "simulation", sim);

            // Village: every child of "Village" that has renderers is a house.
            var tracker = Child(root, "Village Flood Tracker").AddComponent<VillageFloodTracker>();
            var houses = new List<Transform>();
            if (village != null)
                foreach (Transform house in village)
                    if (house.GetComponentInChildren<Renderer>() != null) houses.Add(house);
            var trackerSO = new SerializedObject(tracker);
            trackerSO.FindProperty("simulation").objectReferenceValue = sim;
            SerializedProperty list = trackerSO.FindProperty("buildings");
            list.arraySize = houses.Count;
            for (int i = 0; i < houses.Count; i++)
            {
                SerializedProperty entry = list.GetArrayElementAtIndex(i);
                entry.FindPropertyRelative("transform").objectReferenceValue = houses[i];
                entry.FindPropertyRelative("footprintRadius").floatValue = 2.2f;
            }
            trackerSO.ApplyModifiedPropertiesWithoutUndo();

            // Tsunami manager. Its scenarios place the source offshore on the approach axis (through the valley
            // between "Valley Left Mountain" and the nearest ridge mountain, towards the village).
            var controller = Child(root, "Tsunami Controller").AddComponent<TsunamiController>();
            var controllerSO = new SerializedObject(controller);
            controllerSO.FindProperty("simulation").objectReferenceValue = sim;
            controllerSO.FindProperty("village").objectReferenceValue = tracker;
            controllerSO.FindProperty("mapAdapter").objectReferenceValue = adapter;
            controllerSO.ApplyModifiedPropertiesWithoutUndo();
            ConfigureGameRelease(controller);
            string aim = AimThroughValley(adapter, mountains, houses, world);

            GameObject canvas = TsunamiSceneBuilder.BuildUI(controller, tracker, mats, true, null, !hasEventSystem);
            canvas.transform.SetParent(root.transform, false);
            canvas.GetComponent<Canvas>().sortingOrder = -1; // the team's menus draw on top of it

            TsunamiSceneBuilder.BuildVisualization(root.transform, sim, terrainRenderer,
                new Renderer[] { waterGO.GetComponent<MeshRenderer>(), oceanGO.GetComponent<MeshRenderer>() },
                sprayGO.GetComponent<WaterSprayEmitter>());
            TsunamiSceneBuilder.BuildImpactSplash(root.transform, sim, tracker, mats);
            AddStorm(root.transform, controller, mats);
            AddSwell(root.transform, controller, mats);

            adapter.Apply();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Selection.activeGameObject = root;
            Debug.Log($"[Tsunami] Added to '{scene.name}': grid {terrain.Resolution}x{terrain.Resolution} at {terrain.CellSize} m " +
                      $"(aligned to World), {houses.Count} houses tracked, mountains: {(mountains != null ? "yes" : "not found")}, " +
                      $"wall construction: {(wallController != null ? "connected" : "not found")}, wave: {aim}. Press Play, then Start Tsunami.");
        }

        /// <summary>
        /// Aims the wave through the narrowest gap between "Valley Left Mountain" and the other mountains, toward
        /// the village centre. Returns a short description for the log.
        /// </summary>
        static string AimThroughValley(TeamMapTsunamiAdapter adapter, Transform mountains, List<Transform> houses, World world)
        {
            Transform valley = mountains != null ? mountains.Find("Valley Left Mountain") : null;
            if (valley == null || !Footprint(valley, out Vector3 vc, out float vr))
                return "from the south edge (no 'Valley Left Mountain' found)";

            float bestGap = float.MaxValue;
            Vector3 bestThrough = Vector3.zero;
            foreach (Transform m in mountains)
            {
                if (m == valley || !Footprint(m, out Vector3 c, out float r)) continue;
                Vector3 d = c - vc;
                d.y = 0f;
                float dist = d.magnitude;
                float gap = dist - vr - r;
                if (dist < 1e-3f || gap >= bestGap) continue;
                bestGap = gap;
                Vector3 dir = d / dist;
                Vector3 edgeA = vc + dir * vr, edgeB = c - dir * r;
                bestThrough = (edgeA + edgeB) * 0.5f;
            }
            if (bestGap == float.MaxValue) return "from the south edge (no second mountain found)";

            Vector3 target = world.transform.position + new Vector3(world.Width, 0f, world.Height) * (world.CellSize * 0.5f);
            if (houses.Count > 0)
            {
                target = Vector3.zero;
                foreach (Transform h in houses) target += h.position;
                target /= houses.Count;
            }
            bestThrough.y = target.y = 0f;
            adapter.SetApproach(bestThrough, target);
            EditorUtility.SetDirty(adapter);
            return $"aimed through the valley at ({bestThrough.x:F0}, {bestThrough.z:F0}), gap {bestGap:F0} m, toward the village";
        }

        static bool Footprint(Transform t, out Vector3 centre, out float radius)
        {
            centre = Vector3.zero;
            radius = 0f;
            Renderer[] renderers = t.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return false;
            Bounds b = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
            centre = b.center;
            radius = 0.5f * (b.extents.x + b.extents.z);
            return true;
        }

        /// <summary>
        /// Brings a tsunami added before the storm existed up to date: automatic release 2-3 min after the game
        /// starts, the "Anında Etkinleştir" button and the storm. Leaves everything else as it is.
        /// </summary>
        [MenuItem("Tsunami/Add Storm And Release Button To Open Scene")]
        public static void UpgradeOpenScene()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (!UpgradeStorm(scene)) return;
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        public static bool UpgradeStorm(Scene scene)
        {
            GameObject root = FindRoot(scene, RootName);
            var controller = root != null ? root.GetComponentInChildren<TsunamiController>(true) : null;
            if (controller == null)
            {
                Debug.LogWarning($"[Tsunami] No \"{RootName}\" object with a TsunamiController in '{scene.name}'.");
                return false;
            }
            ConfigureGameRelease(controller);

            var ui = root.GetComponentInChildren<TsunamiUI>(true);
            if (ui != null)
            {
                var uiSO = new SerializedObject(ui);
                SerializedProperty button = uiSO.FindProperty("releaseNowButton");
                Transform panel = ui.transform.Find("Control Panel");
                if (button.objectReferenceValue == null && panel != null)
                {
                    var rect = (RectTransform)panel;
                    rect.sizeDelta = new Vector2(rect.sizeDelta.x, 388f);
                    button.objectReferenceValue = TsunamiSceneBuilder.CreateReleaseNowButton(panel,
                        Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));
                    uiSO.ApplyModifiedPropertiesWithoutUndo();
                }
            }

            // Rebuilt every time so they pick up the current defaults.
            foreach (var old in root.GetComponentsInChildren<TsunamiStorm>(true)) Object.DestroyImmediate(old.gameObject);
            foreach (var old in root.GetComponentsInChildren<TsunamiSwell>(true)) Object.DestroyImmediate(old.gameObject);
            TsunamiSceneBuilder.Materials mats = TsunamiSceneBuilder.CreateMaterials();
            AddStorm(root.transform, controller, mats);
            AddSwell(root.transform, controller, mats);
            Debug.Log($"[Tsunami] Storm, automatic release and 'Anında Etkinleştir' set up in '{scene.name}'.");
            return true;
        }

        /// <summary>
        /// Game settings: the tsunami sets off by itself 2-3 min after the game starts, its swell travels in from the
        /// open sea for 12 s, and the game scenario's source sits as far out as the water grid allows.
        /// </summary>
        static void ConfigureGameRelease(TsunamiController controller)
        {
            var so = new SerializedObject(controller);
            so.FindProperty("autoRelease").boolValue = true;
            so.FindProperty("releaseDelayMin").floatValue = 120f;
            so.FindProperty("releaseDelayMax").floatValue = 180f;
            so.FindProperty("useWarning").boolValue = true;
            so.FindProperty("warningDuration").floatValue = 12f;
            SerializedProperty scenarios = so.FindProperty("scenarios");
            if (scenarios.arraySize > 0)
                scenarios.GetArrayElementAtIndex(0).FindPropertyRelative("offshoreDistance").floatValue = 100f;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>The wave travelling in from beyond the camera's reach during the warning.</summary>
        internal static TsunamiSwell AddSwell(Transform root, TsunamiController controller, TsunamiSceneBuilder.Materials mats)
        {
            var go = new GameObject("Incoming Swell", typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(root, false);
            TsunamiSceneBuilder.ConfigureRenderer(go, mats.ocean);
            var swell = go.AddComponent<TsunamiSwell>();
            TsunamiSceneBuilder.SetRef(swell, "controller", controller);
            return swell;
        }

        /// <summary>Storm clouds and lightning that announce the tsunami (driven by the controller's countdown).</summary>
        internal static TsunamiStorm AddStorm(Transform root, TsunamiController controller, TsunamiSceneBuilder.Materials mats)
        {
            var go = new GameObject("Storm");
            go.transform.SetParent(root, false);
            var storm = go.AddComponent<TsunamiStorm>();
            TsunamiSceneBuilder.SetRef(storm, "controller", controller);
            TsunamiSceneBuilder.SetRef(storm, "cloudMaterial", mats.cloud);
            TsunamiSceneBuilder.SetRef(storm, "lightningMaterial", mats.lightning);
            return storm;
        }

        static GameObject Child(GameObject parent, string name, params System.Type[] components)
        {
            var go = new GameObject(name, components);
            go.transform.SetParent(parent.transform, false);
            return go;
        }

        static T FindActive<T>(Scene scene) where T : Component
        {
            foreach (GameObject root in scene.GetRootGameObjects())
                foreach (T c in root.GetComponentsInChildren<T>(true))
                    if (c.gameObject.activeInHierarchy) return c;
            return null;
        }

        static Transform FindByName(Scene scene, string name)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
                foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                    if (t.name == name && t.gameObject.activeInHierarchy) return t;
            return null;
        }

        static GameObject FindRoot(Scene scene, string name)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
                if (root.name == name) return root;
            return null;
        }
    }
}
