using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace Tsunami.EditorTools
{
    /// <summary>
    /// Menu: Tsunami > Build Demo Scene.
    /// Creates Assets/Tsunami/Scenes/TsunamiDemo.unity with every component wired up. Existing scenes are
    /// left untouched; existing materials in Assets/Tsunami/Materials are reused, not overwritten.
    /// </summary>
    public static class TsunamiSceneBuilder
    {
        const string Root = "Assets/Tsunami";
        const string ScenePath = Root + "/Scenes/TsunamiDemo.unity";
        const string MaterialDir = Root + "/Materials";

        static readonly Color SkyColor = new Color(0.62f, 0.78f, 0.88f);

        // Normalised (-1..1) grid positions; low plain behind the beach plus a few on higher slopes.
        static readonly Vector2[] HouseSpots =
        {
            new Vector2(-0.08f, -0.22f), new Vector2(0.06f, -0.27f), new Vector2(0.0f, -0.1f),
            new Vector2(0.13f, -0.13f), new Vector2(-0.17f, -0.12f), new Vector2(0.08f, 0.02f),
            new Vector2(-0.06f, 0.05f), new Vector2(-0.3f, 0.03f), new Vector2(0.3f, 0.08f),
            new Vector2(0.0f, 0.2f)
        };

        internal struct Materials
        {
            public Material terrain, flood, ocean, wall, roof, flooded, destroyed, trunk, leaves, cursor, spray;
        }

        [MenuItem("Tsunami/Build Demo Scene")]
        public static void BuildDemoScene()
        {
            if (File.Exists(ScenePath) &&
                !EditorUtility.DisplayDialog("Tsunami", $"{ScenePath} already exists. Rebuild it?", "Rebuild", "Cancel"))
                return;
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            EnsureFolder(Root, "Scenes");
            EnsureFolder(Root, "Materials");
            Materials mats = CreateMaterials();

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            SetupLighting();

            // --- Terrain + simulation -------------------------------------------------------------
            var terrainGO = new GameObject("Island Terrain", typeof(MeshFilter), typeof(MeshRenderer));
            var terrain = terrainGO.AddComponent<TerrainGrid>();
            terrain.EnsureInitialized();
            ConfigureRenderer(terrainGO, mats.terrain);

            var simGO = new GameObject("Water Simulation");
            var sim = simGO.AddComponent<WaterSimulation>();
            SetRef(sim, "terrain", terrain);

            var waterGO = new GameObject("Flood Water Surface", typeof(MeshFilter), typeof(MeshRenderer));
            var waterRenderer = waterGO.AddComponent<WaterMeshRenderer>();
            SetRef(waterRenderer, "simulation", sim);
            ConfigureRenderer(waterGO, mats.flood);

            var oceanGO = new GameObject("Ocean", typeof(MeshFilter), typeof(MeshRenderer));
            var ocean = oceanGO.AddComponent<OceanRing>();
            SetRef(ocean, "terrain", terrain);
            SetRef(ocean, "simulation", sim);
            ConfigureRenderer(oceanGO, mats.ocean);

            var sprayGO = new GameObject("Water Spray", typeof(ParticleSystem));
            var sprayRenderer = sprayGO.GetComponent<ParticleSystemRenderer>();
            sprayRenderer.sharedMaterial = mats.spray;
            sprayRenderer.shadowCastingMode = ShadowCastingMode.Off;
            sprayRenderer.receiveShadows = false;
            var spray = sprayGO.AddComponent<WaterSprayEmitter>();
            SetRef(spray, "simulation", sim);

            // --- Village + vegetation -------------------------------------------------------------
            var villageGO = new GameObject("Village");
            var tracker = villageGO.AddComponent<VillageFloodTracker>();
            var houses = new List<Transform>();
            for (int i = 0; i < HouseSpots.Length; i++)
            {
                Vector3 p = new Vector3(HouseSpots[i].x * terrain.HalfExtent, 0f, HouseSpots[i].y * terrain.HalfExtent);
                p.y = terrain.SampleHeight(p);
                if (p.y < 0.4f) continue; // would be in the sea
                houses.Add(CreateHouse(villageGO.transform, $"House {houses.Count + 1}", p, i * 37f, mats));
            }
            CreateTrees(terrain, houses, mats);

            var trackerSO = new SerializedObject(tracker);
            trackerSO.FindProperty("simulation").objectReferenceValue = sim;
            SerializedProperty list = trackerSO.FindProperty("buildings");
            list.arraySize = houses.Count;
            for (int i = 0; i < houses.Count; i++)
            {
                SerializedProperty entry = list.GetArrayElementAtIndex(i);
                entry.FindPropertyRelative("transform").objectReferenceValue = houses[i];
                entry.FindPropertyRelative("footprintRadius").floatValue = 1.6f;
            }
            trackerSO.ApplyModifiedPropertiesWithoutUndo();

            // --- Gameplay -----------------------------------------------------------------------
            var controllerGO = new GameObject("Tsunami Controller");
            var controller = controllerGO.AddComponent<TsunamiController>();
            SetRef(controller, "simulation", sim);
            SetRef(controller, "village", tracker);
            var demoSO = new SerializedObject(controller);
            demoSO.FindProperty("fallbackSourcePosition").vector3Value = new Vector3(0f, 0f, -50f);
            demoSO.FindProperty("fallbackDirection").floatValue = 0f;
            demoSO.ApplyModifiedPropertiesWithoutUndo();

            var camGO = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
            camGO.tag = "MainCamera";
            var cam = camGO.GetComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = SkyColor;
            cam.fieldOfView = 45f;
            cam.nearClipPlane = 0.5f;
            cam.farClipPlane = 1200f;
            camGO.AddComponent<GodCameraController>();
            Quaternion camRot = Quaternion.Euler(52f, 0f, 0f);
            camGO.transform.SetPositionAndRotation(new Vector3(0f, 0f, -10f) - camRot * Vector3.forward * 170f, camRot);

            var brushGO = new GameObject("Terrain Brush", typeof(LineRenderer));
            var line = brushGO.GetComponent<LineRenderer>();
            line.widthMultiplier = 0.35f;
            line.sharedMaterial = mats.cursor;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            var brush = brushGO.AddComponent<TerrainBrush>();
            SetRef(brush, "terrain", terrain);
            SetRef(brush, "viewCamera", cam);
            SetRef(brush, "cursor", line);

            // --- UI -----------------------------------------------------------------------------
            GameObject demoCanvas = BuildUI(controller, tracker, mats, false,
                "WASD / Oklar: hareket    Q / E: döndür    Tekerlek: yakınlaş    Orta tuş: kaydır\n" +
                "Sol tık basılı: araziyi yükselt (set)    Sağ tık basılı: alçalt (kanal)    [ / ]: fırça boyutu",
                true);
            BuildVisualization(controllerGO.transform, sim, terrainGO.GetComponent<MeshRenderer>(),
                new Renderer[] { waterGO.GetComponent<MeshRenderer>(), oceanGO.GetComponent<MeshRenderer>() }, spray);
            BuildImpactSplash(controllerGO.transform, sim, tracker, mats);

            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), ScenePath);
            AddToBuildSettings(ScenePath);
            Selection.activeGameObject = controllerGO;

            if (!TsunamiGraphicsSetup.FastSettingsActive &&
                EditorUtility.DisplayDialog("Tsunami",
                    "Switch the active quality level to fast render settings (no SSAO, HDR, MSAA, shadows or extra " +
                    "texture copies) for high FPS on low-end PCs?\n\nCopies are created in Assets/Tsunami/Settings; the " +
                    "original URP assets are not changed. Revert any time with Tsunami > Graphics > Use Original Settings.",
                    "Use fast settings", "Keep current"))
                TsunamiGraphicsSetup.UseFastSettings();
            Debug.Log($"[Tsunami] Demo scene built at {ScenePath} with {houses.Count} houses. Press Play, then Start Tsunami.");
        }

        /// <summary>
        /// Adds the visualisation manager (shader globals, foam / spray / water toggles, animation clock that stops
        /// while the simulation is paused), shared by the demo and the team map.
        /// </summary>
        internal static TsunamiVisualizationManager BuildVisualization(Transform parent, WaterSimulation sim, Renderer seabed,
            Renderer[] water, WaterSprayEmitter spray)
        {
            var visGO = new GameObject("Visualization");
            visGO.transform.SetParent(parent, false);
            var vis = visGO.AddComponent<TsunamiVisualizationManager>();
            var visSO = new SerializedObject(vis);
            visSO.FindProperty("simulation").objectReferenceValue = sim;
            visSO.FindProperty("seabedRenderer").objectReferenceValue = seabed;
            visSO.FindProperty("spray").objectReferenceValue = spray;
            SerializedProperty wr = visSO.FindProperty("waterRenderers");
            wr.arraySize = water.Length;
            for (int i = 0; i < water.Length; i++) wr.GetArrayElementAtIndex(i).objectReferenceValue = water[i];
            visSO.ApplyModifiedPropertiesWithoutUndo();
            return vis;
        }

        /// <summary>Splash and mist where the water hits mountains, walls or houses taller than itself.</summary>
        internal static ImpactSplashEmitter BuildImpactSplash(Transform parent, WaterSimulation sim, VillageFloodTracker village,
            Materials mats)
        {
            var go = new GameObject("Impact Splash", typeof(ParticleSystem));
            go.transform.SetParent(parent, false);
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mats.spray;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            var splash = go.AddComponent<ImpactSplashEmitter>();
            SetRef(splash, "simulation", sim);
            SetRef(splash, "village", village);
            return splash;
        }

        // ---------------------------------------------------------------------------------------- UI

        /// <param name="topRight">Put the control panel top-right instead of top-left (e.g. next to another game's UI).</param>
        /// <param name="help">Controls help text for the bottom-left corner, or null for none.</param>
        /// <param name="createEventSystem">False when the scene already has an EventSystem.</param>
        internal static GameObject BuildUI(TsunamiController controller, VillageFloodTracker tracker, Materials mats,
            bool topRight, string help, bool createEventSystem)
        {
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            var canvasGO = new GameObject("Tsunami UI", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = canvasGO.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGO.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            // Control panel. Its Image blocks clicks so they never reach the world underneath.
            Vector2 corner = topRight ? new Vector2(1, 1) : new Vector2(0, 1);
            var panel = CreateRect("Control Panel", canvasGO.transform, corner, corner, corner,
                new Vector2(topRight ? -20 : 20, -20), new Vector2(480, 330));
            panel.gameObject.AddComponent<Image>().color = new Color(0.04f, 0.08f, 0.12f, 0.72f);

            CreateText("Title", panel, font, "TSUNAMİ", 34, FontStyle.Bold, TextAnchor.UpperLeft, Color.white,
                new Vector2(20, -14), new Vector2(440, 44));
            Text status = CreateText("Status", panel, font, "Durum: Hazır - deniz durgun", 22, FontStyle.Normal, TextAnchor.UpperLeft,
                new Color(0.85f, 0.95f, 1f), new Vector2(20, -62), new Vector2(440, 30));
            Text village = CreateText("Village Status", panel, font, "Köy: -", 20, FontStyle.Normal, TextAnchor.UpperLeft,
                new Color(1f, 0.92f, 0.7f), new Vector2(20, -96), new Vector2(440, 30));

            Text heightLabel = CreateText("Wave Height Label", panel, font, "Dalga yüksekliği: -", 20, FontStyle.Normal, TextAnchor.UpperLeft,
                Color.white, new Vector2(20, -130), new Vector2(440, 28));
            Slider heightSlider = CreateSlider("Wave Height Slider", panel, new Vector2(20, -162), new Vector2(440, 24),
                TsunamiController.MinWaveHeight, TsunamiController.MaxWaveHeight);

            Button start = CreateButton("Start Tsunami Button", panel, font, "Tsunamiyi Başlat", new Color(0.18f, 0.6f, 0.3f),
                new Vector2(20, -196), new Vector2(212, 60));
            Button stop = CreateButton("Pause Tsunami Button", panel, font, "Duraklat", new Color(0.78f, 0.45f, 0.15f),
                new Vector2(248, -196), new Vector2(212, 60));
            Button reset = CreateButton("Reset Simulation Button", panel, font, "Sıfırla", new Color(0.32f, 0.36f, 0.42f),
                new Vector2(20, -268), new Vector2(440, 46));

            // Countdown (top-centre).
            var countdownRect = CreateRect("Countdown", canvasGO.transform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0.5f, 1),
                new Vector2(0, -30), new Vector2(900, 160));
            Text countdown = AddText(countdownRect.gameObject, font, string.Empty, 54, FontStyle.Bold, TextAnchor.UpperCenter,
                new Color(1f, 0.38f, 0.25f));
            countdownRect.gameObject.AddComponent<Outline>().effectColor = new Color(0, 0, 0, 0.8f);

            // Controls help (bottom-left).
            if (!string.IsNullOrEmpty(help))
            {
                var helpRect = CreateRect("Controls Help", canvasGO.transform, new Vector2(0, 0), new Vector2(0, 0), new Vector2(0, 0),
                    new Vector2(20, 16), new Vector2(1100, 64));
                AddText(helpRect.gameObject, font, help, 19, FontStyle.Normal, TextAnchor.LowerLeft, Color.white);
                helpRect.gameObject.AddComponent<Shadow>().effectColor = new Color(0, 0, 0, 0.9f);
            }

            var ui = canvasGO.AddComponent<TsunamiUI>();
            SetRef(ui, "controller", controller);
            SetRef(ui, "startButton", start);
            SetRef(ui, "stopButton", stop);
            SetRef(ui, "resetButton", reset);
            SetRef(ui, "waveHeightSlider", heightSlider);
            SetRef(ui, "waveHeightText", heightLabel);
            SetRef(ui, "statusText", status);
            SetRef(ui, "countdownText", countdown);

            var so = new SerializedObject(tracker);
            so.FindProperty("statusText").objectReferenceValue = village;
            so.FindProperty("floodedMaterial").objectReferenceValue = mats.flooded;
            so.FindProperty("destroyedMaterial").objectReferenceValue = mats.destroyed;
            so.ApplyModifiedPropertiesWithoutUndo();

            if (createEventSystem)
            {
                var eventSystem = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
                eventSystem.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
            }
            return canvasGO;
        }

        static RectTransform CreateRect(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot,
            Vector2 position, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = pivot;
            rt.anchoredPosition = position;
            rt.sizeDelta = size;
            return rt;
        }

        static Text CreateText(string name, Transform parent, Font font, string text, int size, FontStyle style, TextAnchor anchor,
            Color color, Vector2 position, Vector2 rectSize)
        {
            var rt = CreateRect(name, parent, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), position, rectSize);
            return AddText(rt.gameObject, font, text, size, style, anchor, color);
        }

        static Text AddText(GameObject go, Font font, string text, int size, FontStyle style, TextAnchor anchor, Color color)
        {
            var t = go.AddComponent<Text>();
            t.font = font;
            t.text = text;
            t.fontSize = size;
            t.fontStyle = style;
            t.alignment = anchor;
            t.color = color;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            return t;
        }

        static Slider CreateSlider(string name, Transform parent, Vector2 position, Vector2 size, float min, float max)
        {
            var rt = CreateRect(name, parent, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), position, size);
            var background = CreateRect("Background", rt, new Vector2(0, 0.3f), new Vector2(1, 0.7f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            background.gameObject.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.18f);
            var fillArea = CreateRect("Fill Area", rt, new Vector2(0, 0.3f), new Vector2(1, 0.7f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(-12, 0));
            var fill = CreateRect("Fill", fillArea, Vector2.zero, new Vector2(0, 1), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            fill.gameObject.AddComponent<Image>().color = new Color(0.25f, 0.7f, 0.95f);
            var handleArea = CreateRect("Handle Slide Area", rt, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(-12, 0));
            var handle = CreateRect("Handle", handleArea, Vector2.zero, new Vector2(0, 1), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(14, 0));
            var handleImage = handle.gameObject.AddComponent<Image>();
            handleImage.color = Color.white;
            var slider = rt.gameObject.AddComponent<Slider>();
            slider.fillRect = fill;
            slider.handleRect = handle;
            slider.targetGraphic = handleImage;
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = min;
            slider.maxValue = max;
            return slider;
        }

        static Button CreateButton(string name, Transform parent, Font font, string label, Color color, Vector2 position, Vector2 size)
        {
            var rt = CreateRect(name, parent, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), position, size);
            var image = rt.gameObject.AddComponent<Image>();
            image.color = color;
            var button = rt.gameObject.AddComponent<Button>();
            ColorBlock cb = button.colors;
            cb.highlightedColor = new Color(1.15f, 1.15f, 1.15f, 1f);
            cb.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
            cb.disabledColor = new Color(0.45f, 0.45f, 0.45f, 0.55f);
            button.colors = cb;
            var labelRect = CreateRect("Label", rt, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            AddText(labelRect.gameObject, font, label, 24, FontStyle.Bold, TextAnchor.MiddleCenter, Color.white);
            return button;
        }

        // --------------------------------------------------------------------------------- Props

        static Transform CreateHouse(Transform parent, string name, Vector3 position, float yaw, Materials m)
        {
            var root = new GameObject(name).transform;
            root.SetParent(parent, false);
            root.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));

            var body = Primitive(PrimitiveType.Cube, "Walls", root, m.wall);
            body.localPosition = new Vector3(0f, 1.0f, 0f); // bottom sits 0.3 m into the ground for slopes
            body.localScale = new Vector3(3f, 2.6f, 3.4f);

            var roof = Primitive(PrimitiveType.Cube, "Roof", root, m.roof);
            roof.localPosition = new Vector3(0f, 2.3f, 0f);
            roof.localRotation = Quaternion.Euler(0f, 0f, 45f);
            roof.localScale = new Vector3(2.3f, 2.3f, 3.7f);
            return root;
        }

        static void CreateTrees(TerrainGrid terrain, List<Transform> houses, Materials m)
        {
            var parent = new GameObject("Vegetation").transform;
            var rng = new System.Random(11);
            int placed = 0;
            for (int attempt = 0; attempt < 400 && placed < 36; attempt++)
            {
                float x = (float)(rng.NextDouble() * 2 - 1) * terrain.HalfExtent * 0.75f;
                float z = (float)(rng.NextDouble() * 2 - 1) * terrain.HalfExtent * 0.75f;
                var p = new Vector3(x, 0f, z);
                p.y = terrain.SampleHeight(p);
                if (p.y < 1.8f || p.y > 9f) continue;
                bool nearHouse = false;
                foreach (var h in houses)
                    if ((h.position - p).sqrMagnitude < 36f) { nearHouse = true; break; }
                if (nearHouse) continue;

                var tree = new GameObject($"Tree {placed + 1}").transform;
                tree.SetParent(parent, false);
                tree.position = p;
                float s = 0.8f + (float)rng.NextDouble() * 0.6f;
                tree.localScale = new Vector3(s, s, s);
                var trunk = Primitive(PrimitiveType.Cylinder, "Trunk", tree, m.trunk);
                trunk.localPosition = new Vector3(0f, 1.5f, 0f);
                trunk.localScale = new Vector3(0.35f, 1.6f, 0.35f);
                var crown = Primitive(PrimitiveType.Sphere, "Crown", tree, m.leaves);
                crown.localPosition = new Vector3(0f, 3.4f, 0f);
                crown.localScale = new Vector3(2.4f, 1.5f, 2.4f);
                GameObjectUtility.SetStaticEditorFlags(trunk.gameObject, StaticEditorFlags.BatchingStatic);
                GameObjectUtility.SetStaticEditorFlags(crown.gameObject, StaticEditorFlags.BatchingStatic);
                placed++;
            }
        }

        static Transform Primitive(PrimitiveType type, string name, Transform parent, Material material)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            Object.DestroyImmediate(go.GetComponent<Collider>()); // no physics needed; flood checks sample the grid
            go.transform.SetParent(parent, false);
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            return go.transform;
        }

        // ----------------------------------------------------------------------------- Lighting

        static void SetupLighting()
        {
            RenderSettings.skybox = null;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.72f, 0.82f, 0.92f);
            RenderSettings.ambientEquatorColor = new Color(0.58f, 0.62f, 0.6f);
            RenderSettings.ambientGroundColor = new Color(0.36f, 0.32f, 0.26f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = SkyColor;
            RenderSettings.fogStartDistance = 300f;
            RenderSettings.fogEndDistance = 950f;

            var sunGO = new GameObject("Sun", typeof(Light));
            var sun = sunGO.GetComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.95f, 0.85f);
            sun.intensity = 1.1f;
            sun.shadows = LightShadows.None; // cheap on integrated GPUs; terrain shader doesn't sample shadows
            sunGO.transform.rotation = Quaternion.Euler(48f, -35f, 0f);
            RenderSettings.sun = sun;
        }

        internal static void ConfigureRenderer(GameObject go, Material material)
        {
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }

        // ---------------------------------------------------------------------------- Materials

        internal static Materials CreateMaterials()
        {
            Shader water = Shader.Find("Tsunami/StylizedWater");
            Shader terrain = Shader.Find("Tsunami/LowPolyTerrain");
            Shader lit = Shader.Find("Universal Render Pipeline/Simple Lit");
            Shader cursor = Shader.Find("Sprites/Default");
            if (water == null || terrain == null || lit == null)
                Debug.LogError("[Tsunami] Required shaders not found. Make sure Assets/Tsunami/Shaders imported without errors.");

            var m = new Materials
            {
                terrain = GetOrCreate("Terrain_Island", terrain, null),
                flood = GetOrCreate("Water_Flood", water, null),
                ocean = GetOrCreate("Water_Ocean", water, null),
                wall = GetOrCreate("House_Wall", lit, new Color(0.93f, 0.86f, 0.74f)),
                roof = GetOrCreate("House_Roof", lit, new Color(0.72f, 0.3f, 0.22f)),
                flooded = GetOrCreate("House_Flooded", lit, new Color(0.38f, 0.48f, 0.58f)),
                destroyed = GetOrCreate("House_Destroyed", lit, new Color(0.25f, 0.22f, 0.2f)),
                trunk = GetOrCreate("Tree_Trunk", lit, new Color(0.45f, 0.32f, 0.2f)),
                leaves = GetOrCreate("Tree_Leaves", lit, new Color(0.3f, 0.55f, 0.22f)),
                cursor = GetOrCreate("Brush_Cursor", cursor, null),
                spray = GetOrCreate("Water_Spray", Shader.Find("Tsunami/Spray"), null)
            };
            AssetDatabase.SaveAssets();
            return m;
        }

        static Material GetOrCreate(string name, Shader shader, Color? baseColor)
        {
            string path = $"{MaterialDir}/{name}.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) return existing; // keep any tuning the team has done
            var mat = new Material(shader) { name = name, enableInstancing = true };
            if (baseColor.HasValue && mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", baseColor.Value);
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        // -------------------------------------------------------------------------------- Utils

        internal static void SetRef(Object target, string field, Object value)
        {
            var so = new SerializedObject(target);
            SerializedProperty p = so.FindProperty(field);
            if (p == null)
            {
                Debug.LogError($"[Tsunami] Field '{field}' not found on {target.GetType().Name}.");
                return;
            }
            p.objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void EnsureFolder(string parent, string child)
        {
            if (!AssetDatabase.IsValidFolder($"{parent}/{child}")) AssetDatabase.CreateFolder(parent, child);
        }

        static void AddToBuildSettings(string path)
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            scenes.RemoveAll(s => s.path == path);
            scenes.Insert(0, new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}
