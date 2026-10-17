using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Tsunami.EditorTools
{
    /// <summary>
    /// Menu: Tsunami > Graphics > Use Fast Settings / Use Original Settings.
    /// Creates copies of the project's current URP asset and renderer with the expensive features this game
    /// doesn't need turned off (SSAO, HDR, depth/opaque texture copies, MSAA, shadows, Forward+), and
    /// assigns the copy to the active quality level. The original assets are never modified.
    /// </summary>
    public static class TsunamiGraphicsSetup
    {
        const string Folder = "Assets/Tsunami/Settings";
        const string FastAssetPath = Folder + "/Tsunami_FastRPAsset.asset";
        const string FastRendererPath = Folder + "/Tsunami_FastRenderer.asset";
        const string OriginalPrefKey = "Tsunami.OriginalRPAsset.";

        [MenuItem("Tsunami/Graphics/Use Fast Settings (low-end, high FPS)")]
        public static void UseFastSettings()
        {
            var current = ActiveAsset();
            if (current == null)
            {
                Debug.LogError("[Tsunami] No URP asset is active; nothing to copy.");
                return;
            }
            var fast = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(FastAssetPath);
            if (fast == null) fast = CreateFastCopy(current);
            if (fast == null) return;
            if (current != fast)
                EditorPrefs.SetString(OriginalPrefKey + Application.dataPath, AssetDatabase.GetAssetPath(current));
            AssignToActiveQualityLevel(fast);
            Debug.Log($"[Tsunami] Quality level '{QualitySettings.names[QualitySettings.GetQualityLevel()]}' now uses {FastAssetPath}. " +
                      "Revert with Tsunami > Graphics > Use Original Settings.");
        }

        [MenuItem("Tsunami/Graphics/Use Original Settings")]
        public static void UseOriginalSettings()
        {
            string path = EditorPrefs.GetString(OriginalPrefKey + Application.dataPath, "Assets/Settings/PC_RPAsset.asset");
            var original = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(path);
            if (original == null)
            {
                Debug.LogError($"[Tsunami] Original URP asset not found at {path}. Assign it in Project Settings > Quality.");
                return;
            }
            AssignToActiveQualityLevel(original);
            Debug.Log($"[Tsunami] Restored {path} for the active quality level.");
        }

        /// <summary>True when the active quality level already uses the fast settings.</summary>
        public static bool FastSettingsActive => ActiveAsset() != null && AssetDatabase.GetAssetPath(ActiveAsset()) == FastAssetPath;

        static UniversalRenderPipelineAsset ActiveAsset()
        {
            return (QualitySettings.renderPipeline ?? GraphicsSettings.defaultRenderPipeline) as UniversalRenderPipelineAsset;
        }

        static UniversalRenderPipelineAsset CreateFastCopy(UniversalRenderPipelineAsset source)
        {
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/Tsunami", "Settings");

            var sourceSO = new SerializedObject(source);
            SerializedProperty list = sourceSO.FindProperty("m_RendererDataList");
            int index = sourceSO.FindProperty("m_DefaultRendererIndex").intValue;
            Object sourceRenderer = list != null && list.arraySize > index ? list.GetArrayElementAtIndex(index).objectReferenceValue : null;
            if (!(sourceRenderer is UniversalRendererData))
            {
                Debug.LogError("[Tsunami] The active URP asset's default renderer is not a Universal Renderer; cannot create fast settings.");
                return null;
            }

            if (!AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(sourceRenderer), FastRendererPath) ||
                !AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(source), FastAssetPath))
            {
                Debug.LogError("[Tsunami] Could not copy the URP assets.");
                return null;
            }

            // Renderer: no renderer features (SSAO etc.), plain Forward path, no depth priming.
            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(FastRendererPath);
            foreach (var feature in renderer.rendererFeatures.ToArray())
            {
                if (feature == null) continue;
                AssetDatabase.RemoveObjectFromAsset(feature);
                Object.DestroyImmediate(feature, true);
            }
            renderer.rendererFeatures.Clear();
            var rso = new SerializedObject(renderer);
            rso.FindProperty("m_RendererFeatureMap")?.ClearArray();
            SetInt(rso, "m_RenderingMode", 0);    // Forward
            SetInt(rso, "m_DepthPrimingMode", 0); // Disabled
            rso.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(renderer);

            // Pipeline asset: only the copied renderer, and cheap global settings.
            var fast = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(FastAssetPath);
            var aso = new SerializedObject(fast);
            SerializedProperty fastList = aso.FindProperty("m_RendererDataList");
            fastList.arraySize = 1;
            fastList.GetArrayElementAtIndex(0).objectReferenceValue = renderer;
            SetInt(aso, "m_DefaultRendererIndex", 0);
            SetBool(aso, "m_RequireDepthTexture", false);
            SetBool(aso, "m_RequireOpaqueTexture", false);
            SetBool(aso, "m_SupportsHDR", false);
            SetInt(aso, "m_MSAA", 1);
            SetBool(aso, "m_MainLightShadowsSupported", false);
            SetBool(aso, "m_AdditionalLightShadowsSupported", false);
            SetBool(aso, "m_SoftShadowsSupported", false);
            aso.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(fast);
            AssetDatabase.SaveAssets();
            return fast;
        }

        static void AssignToActiveQualityLevel(RenderPipelineAsset asset)
        {
            Object[] objs = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/QualitySettings.asset");
            if (objs.Length == 0) { QualitySettings.renderPipeline = asset; return; }
            var so = new SerializedObject(objs[0]);
            SerializedProperty levels = so.FindProperty("m_QualitySettings");
            int level = QualitySettings.GetQualityLevel();
            if (levels == null || level >= levels.arraySize) { QualitySettings.renderPipeline = asset; return; }
            SerializedProperty entry = levels.GetArrayElementAtIndex(level);
            entry.FindPropertyRelative("customRenderPipeline").objectReferenceValue = asset;
            SerializedProperty vsync = entry.FindPropertyRelative("vSyncCount");
            if (vsync != null && asset is UniversalRenderPipelineAsset && AssetDatabase.GetAssetPath(asset) == FastAssetPath)
                vsync.intValue = 0; // never cap the frame rate to the monitor refresh
            so.ApplyModifiedPropertiesWithoutUndo();
            QualitySettings.renderPipeline = asset; // apply immediately in the open editor
            AssetDatabase.SaveAssets();
        }

        static void SetBool(SerializedObject so, string name, bool value)
        {
            SerializedProperty p = so.FindProperty(name);
            if (p == null) { Debug.LogWarning($"[Tsunami] URP setting '{name}' not found; skipped."); return; }
            if (p.propertyType == SerializedPropertyType.Boolean) p.boolValue = value;
            else p.intValue = value ? 1 : 0;
        }

        static void SetInt(SerializedObject so, string name, int value)
        {
            SerializedProperty p = so.FindProperty(name);
            if (p == null) { Debug.LogWarning($"[Tsunami] URP setting '{name}' not found; skipped."); return; }
            p.intValue = value; // for enums this is the underlying value (Forward = 0, Disabled = 0)
        }
    }
}
