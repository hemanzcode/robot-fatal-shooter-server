using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// Cria e ativa os assets do URP (uma vez): pipeline com HDR, sombras suaves, Forward+ e oclusao de ambiente.
[InitializeOnLoad]
public static class UrpSetup
{
    const string Dir = "Assets/Settings";
    const string PipelinePath = Dir + "/NeonURP.asset";
    const string RendererPath = Dir + "/NeonRenderer.asset";
    const string LogPath = "Logs/urp_setup.txt";
    const int Version = 2;

    static UrpSetup() { EditorApplication.delayCall += Ensure; }

    static void Log(string s) { try { Directory.CreateDirectory("Logs"); File.AppendAllText(LogPath, s + "\n"); } catch (System.Exception) { } }

    [MenuItem("NeonArena/Refazer configuracao do URP")]
    static void Redo() { EditorPrefs.DeleteKey("neon_urp_" + Application.dataPath); Ensure(); }

    static void Ensure()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        try
        {
            UniversalRenderPipelineAsset pipe = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelinePath);
            bool current = GraphicsSettings.defaultRenderPipeline == pipe && pipe != null;
            if (current && EditorPrefs.GetInt("neon_urp_" + Application.dataPath, 0) == Version) return;
            try { File.WriteAllText(LogPath, "SETUP " + System.DateTime.Now.ToString("HH:mm:ss") + "\n"); } catch (System.Exception) { }
            Directory.CreateDirectory(Dir);

            UniversalRendererData rd = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
            if (rd == null)
            {
                rd = ScriptableObject.CreateInstance<UniversalRendererData>();
                AssetDatabase.CreateAsset(rd, RendererPath);
                try { ResourceReloader.ReloadAllNullIn(rd, "Packages/com.unity.render-pipelines.universal"); } catch (System.Exception e) { Log("reload: " + e.Message); }
                if (rd.postProcessData == null)
                {
                    PostProcessData ppd = AssetDatabase.LoadAssetAtPath<PostProcessData>("Packages/com.unity.render-pipelines.universal/Runtime/Data/PostProcessData.asset");
                    if (ppd == null)
                    {
                        foreach (string guid in AssetDatabase.FindAssets("t:PostProcessData"))
                        {
                            ppd = AssetDatabase.LoadAssetAtPath<PostProcessData>(AssetDatabase.GUIDToAssetPath(guid));
                            if (ppd != null) break;
                        }
                    }
                    rd.postProcessData = ppd;
                    Log("postProcessData: " + (ppd != null ? AssetDatabase.GetAssetPath(ppd) : "NULL"));
                }
                Log("renderer created");
            }
            Set(rd, "m_RenderingMode", 2);          // Forward+
            AddSsao(rd);
            EditorUtility.SetDirty(rd);

            if (pipe == null)
            {
                pipe = UniversalRenderPipelineAsset.Create(rd);
                AssetDatabase.CreateAsset(pipe, PipelinePath);
                Log("pipeline created");
            }
            Set(pipe, "m_SupportsHDR", true);
            Set(pipe, "m_MSAA", 1);
            Set(pipe, "m_RenderScale", 1f);
            Set(pipe, "m_MainLightShadowsSupported", true);
            Set(pipe, "m_MainLightShadowmapResolution", 4096);
            Set(pipe, "m_AdditionalLightsRenderingMode", 1);
            Set(pipe, "m_AdditionalLightsPerObjectLimit", 8);
            Set(pipe, "m_AdditionalLightShadowsSupported", true);
            Set(pipe, "m_AdditionalLightsShadowmapResolution", 2048);
            Set(pipe, "m_ShadowDistance", 90f);
            Set(pipe, "m_ShadowCascadeCount", 4);
            Set(pipe, "m_SoftShadowsSupported", true);
            Set(pipe, "m_SoftShadowQuality", 3);
            Set(pipe, "m_ColorGradingMode", 1);     // HDR
            Set(pipe, "m_ColorGradingLutSize", 32);
            Set(pipe, "m_RequireDepthTexture", true);
            Set(pipe, "m_RequireOpaqueTexture", true);
            Set(pipe, "m_UseSRPBatcher", true);
            Set(pipe, "m_ReflectionProbeBlending", true);
            Set(pipe, "m_ReflectionProbeBoxProjection", true);
            EditorUtility.SetDirty(pipe);
            AssetDatabase.SaveAssets();

            GraphicsSettings.defaultRenderPipeline = pipe;
            int cur = QualitySettings.GetQualityLevel();
            for (int i = 0; i < QualitySettings.names.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.renderPipeline = pipe;
            }
            QualitySettings.SetQualityLevel(cur, false);

            AlwaysInclude("Skybox/Procedural", "Sprites/Default", "Universal Render Pipeline/Lit", "Universal Render Pipeline/Unlit", "Universal Render Pipeline/Particles/Unlit");
            if (PlayerSettings.colorSpace != ColorSpace.Linear) PlayerSettings.colorSpace = ColorSpace.Linear;
            AssetDatabase.SaveAssets();
            EditorPrefs.SetInt("neon_urp_" + Application.dataPath, Version);
            Log("OK pipeline=" + GraphicsSettings.defaultRenderPipeline + " colorSpace=" + PlayerSettings.colorSpace + " features=" + rd.rendererFeatures.Count);
        }
        catch (System.Exception e)
        {
            Log("FAIL " + e);
        }
    }

    static void Set(Object o, string prop, object value)
    {
        SerializedObject so = new SerializedObject(o);
        SerializedProperty p = so.FindProperty(prop);
        if (p == null) { Log("no property " + prop); return; }
        if (value is bool) p.boolValue = (bool)value;
        else if (value is int) p.intValue = (int)value;
        else if (value is float) p.floatValue = (float)value;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    static void AddSsao(UniversalRendererData rd)
    {
        try
        {
            foreach (ScriptableRendererFeature f in rd.rendererFeatures)
                if (f != null && f.GetType().Name == "ScreenSpaceAmbientOcclusion") return;
            System.Type t = typeof(UniversalRendererData).Assembly.GetType("UnityEngine.Rendering.Universal.ScreenSpaceAmbientOcclusion");
            if (t == null) { Log("ssao type missing"); return; }
            ScriptableRendererFeature feat = (ScriptableRendererFeature)ScriptableObject.CreateInstance(t);
            feat.name = "SSAO";
            AssetDatabase.AddObjectToAsset(feat, rd);
            rd.rendererFeatures.Add(feat);
            MethodInfo validate = typeof(ScriptableRendererData).GetMethod("ValidateRendererFeatures", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (validate != null) validate.Invoke(rd, null);
            else
            {
                // Mantem o mapa de ids em dia (o URP usa para achar as features dentro do asset).
                SerializedObject so = new SerializedObject(rd);
                SerializedProperty map = so.FindProperty("m_RendererFeatureMap");
                string guid; long id;
                if (map != null && AssetDatabase.TryGetGUIDAndLocalFileIdentifier(feat, out guid, out id))
                {
                    map.arraySize = rd.rendererFeatures.Count;
                    map.GetArrayElementAtIndex(rd.rendererFeatures.Count - 1).longValue = id;
                    so.ApplyModifiedPropertiesWithoutUndo();
                }
            }
            SerializedObject fs = new SerializedObject(feat);
            SerializedProperty inten = fs.FindProperty("m_Settings.Intensity");
            if (inten != null) inten.floatValue = 1.6f;
            SerializedProperty rad = fs.FindProperty("m_Settings.Radius");
            if (rad != null) rad.floatValue = 0.5f;
            fs.ApplyModifiedPropertiesWithoutUndo();
            Log("ssao added");
        }
        catch (System.Exception e) { Log("ssao: " + e.Message); }
    }

    static void AlwaysInclude(params string[] names)
    {
        Object gs = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset")[0];
        SerializedObject so = new SerializedObject(gs);
        SerializedProperty arr = so.FindProperty("m_AlwaysIncludedShaders");
        if (arr == null) return;
        foreach (string n in names)
        {
            Shader sh = Shader.Find(n);
            if (sh == null) { Log("shader not found " + n); continue; }
            bool has = false;
            for (int i = 0; i < arr.arraySize; i++) if (arr.GetArrayElementAtIndex(i).objectReferenceValue == sh) has = true;
            if (has) continue;
            arr.arraySize++;
            arr.GetArrayElementAtIndex(arr.arraySize - 1).objectReferenceValue = sh;
        }
        so.ApplyModifiedPropertiesWithoutUndo();
    }
}
