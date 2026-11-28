using UnityEditor;
using UnityEngine;

// Regras de importacao: mapas de normal/mascara em espaco linear, texturas de cenario com mipmaps e anisotropia.
public class AssetImport : AssetPostprocessor
{
    void OnPreprocessTexture()
    {
        TextureImporter ti = (TextureImporter)assetImporter;
        string p = assetPath.Replace('\\', '/');
        // Navegador/celular: memoria curta, texturas de no maximo 1024.
        TextureImporterPlatformSettings web = ti.GetPlatformTextureSettings("WebGL");
        web.overridden = true;
        web.maxTextureSize = p.Contains("/Resources/Agents/") ? 512 : 1024;
        web.format = TextureImporterFormat.Automatic;
        ti.SetPlatformTextureSettings(web);
        if (p.Contains("/Resources/Tex/"))
        {
            string n = System.IO.Path.GetFileNameWithoutExtension(p);
            bool data = n.EndsWith("_n") || n.EndsWith("_m");
            ti.textureType = TextureImporterType.Default;
            ti.sRGBTexture = !data;
            ti.mipmapEnabled = true;
            ti.anisoLevel = 8;
            ti.wrapMode = TextureWrapMode.Repeat;
            ti.filterMode = FilterMode.Trilinear;
            ti.maxTextureSize = 2048;
            ti.textureCompression = TextureImporterCompression.CompressedHQ;
        }
        else if (p.Contains("/Resources/Agents/"))
        {
            string n = System.IO.Path.GetFileNameWithoutExtension(p).ToLowerInvariant();
            ti.textureType = n.Contains("normal") ? TextureImporterType.NormalMap : TextureImporterType.Default;
            ti.mipmapEnabled = true;
            ti.anisoLevel = 4;
            ti.maxTextureSize = 2048;
            if (p.EndsWith(".png")) ti.alphaIsTransparency = true;
        }
        else if (p.Contains("/Resources/Sky/"))
        {
            ti.textureType = TextureImporterType.Default;
            ti.textureShape = TextureImporterShape.Texture2D;
            ti.mipmapEnabled = false;
            ti.wrapMode = TextureWrapMode.Repeat;
            ti.maxTextureSize = 4096;
            ti.textureCompression = TextureImporterCompression.CompressedHQ;
        }
    }

    // Sons gravados (mobile-first): Mono, DecompressOnLoad; ADPCM para tiros/impactos curtos e Vorbis 75% para caudas longas.
    void OnPreprocessAudio()
    {
        string p = assetPath.Replace('\\', '/');
        if (!p.Contains("/Resources/Sfx/")) return;
        AudioImporter ai = (AudioImporter)assetImporter;
        ai.forceToMono = true;
        string name = System.IO.Path.GetFileNameWithoutExtension(p).ToLowerInvariant();
        bool longTail = name.StartsWith("explosion") || name.StartsWith("sniper") || name.Contains("_far");
        AudioImporterSampleSettings st = ai.defaultSampleSettings;
        st.loadType = AudioClipLoadType.DecompressOnLoad;
        if (longTail)
        {
            st.compressionFormat = AudioCompressionFormat.Vorbis;
            st.quality = 0.75f;
        }
        else
        {
            st.compressionFormat = AudioCompressionFormat.ADPCM;
        }
        ai.defaultSampleSettings = st;
    }

    static bool IsAgent(string path) { return path.Replace('\\', '/').Contains("/Resources/Agents/"); }

    // Agentes: esqueleto humanoide (para compartilhar as animacoes de captura de movimento).
    void OnPreprocessModel()
    {
        if (!IsAgent(assetPath)) return;
        ModelImporter mi = (ModelImporter)assetImporter;
        mi.animationType = ModelImporterAnimationType.Human;
        mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        mi.importAnimation = assetPath.EndsWith("Anims.fbx");
        mi.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
        mi.materialLocation = ModelImporterMaterialLocation.InPrefab;
        mi.importBlendShapes = false;
        mi.importCameras = false;
        mi.importLights = false;
    }

    void OnPreprocessAnimation()
    {
        if (!IsAgent(assetPath) || !assetPath.EndsWith("Anims.fbx")) return;
        ModelImporter mi = (ModelImporter)assetImporter;
        ModelImporterClipAnimation[] clips = mi.defaultClipAnimations;
        foreach (ModelImporterClipAnimation c in clips)
        {
            c.loopTime = true;
            c.lockRootRotation = true; c.keepOriginalOrientation = true;
            c.lockRootHeightY = true; c.keepOriginalPositionY = true;
            c.lockRootPositionXZ = true; c.keepOriginalPositionXZ = true;
        }
        mi.clipAnimations = clips;
    }

    // Cabelo, sobrancelhas e cilios usam recorte por alfa; o resto e opaco.
    void OnPostprocessModel(GameObject go)
    {
        if (!IsAgent(assetPath)) return;
        string[] cutNames = { "short", "bob", "ponytail", "braid", "afro", "eyebrow", "eyelash" };
        foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
        {
            string n = r.name.ToLowerInvariant();
            bool cut = false;
            foreach (string k in cutNames) if (n.Contains(k)) cut = true;
            foreach (Material m in r.sharedMaterials)
            {
                if (m == null) continue;
                m.SetFloat("_Surface", 0f);
                m.SetFloat("_Blend", 0f);
                m.SetFloat("_AlphaClip", cut ? 1f : 0f);
                m.SetFloat("_Cutoff", 0.3f);
                m.SetFloat("_SrcBlend", 1f);
                m.SetFloat("_DstBlend", 0f);
                m.SetFloat("_ZWrite", 1f);
                m.SetFloat("_Cull", cut ? 0f : 2f);
                m.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
                if (cut) m.EnableKeyword("_ALPHATEST_ON"); else m.DisableKeyword("_ALPHATEST_ON");
                m.SetOverrideTag("RenderType", cut ? "TransparentCutout" : "Opaque");
                m.renderQueue = cut ? 2450 : 2000;
            }
        }
    }
}
