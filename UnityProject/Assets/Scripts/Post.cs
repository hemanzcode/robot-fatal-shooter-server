using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// Imagem final: pos-processamento do URP (bloom, tonemapping ACES, cor, vinheta, granulado),
// ceu HDRI e sonda de reflexo para o asfalto molhado.
public static class Post
{
    static Volume volume;
    static Bloom bloom;
    static ReflectionProbe probe;
    static float nextProbe;

    public static void Build(Transform parent)
    {
        GameObject g = new GameObject("PostVolume");
        g.transform.SetParent(parent, false);
        volume = g.AddComponent<Volume>();
        volume.isGlobal = true;
        volume.priority = 10f;
        VolumeProfile p = ScriptableObject.CreateInstance<VolumeProfile>();
        volume.sharedProfile = p;
        // Celular: sem bloom/cor; o pos-processamento la serve so para a suavizacao de bordas.
        if (Lite) volume.enabled = false;

        bloom = p.Add<Bloom>(true);
        bloom.threshold.value = 1.2f;
        bloom.intensity.value = 0.35f;
        bloom.scatter.value = 0.72f;
        bloom.highQualityFiltering.value = true;

        Tonemapping tm = p.Add<Tonemapping>(true);
        tm.mode.value = TonemappingMode.ACES;

        ColorAdjustments ca = p.Add<ColorAdjustments>(true);
        ca.postExposure.value = -0.15f;
        ca.contrast.value = 10f;
        ca.saturation.value = 4f;
        ca.colorFilter.value = new Color(1.03f, 1f, 0.95f);

        WhiteBalance wb = p.Add<WhiteBalance>(true);
        wb.temperature.value = 10f;

        Vignette vg = p.Add<Vignette>(true);
        vg.intensity.value = 0.2f;
        vg.smoothness.value = 0.45f;

        FilmGrain fg = p.Add<FilmGrain>(true);
        fg.intensity.value = 0.1f;
        fg.response.value = 0.8f;

        ChromaticAberration ab = p.Add<ChromaticAberration>(true);
        ab.intensity.value = 0.06f;
    }

    public static bool Lite;   // celular: sem pos-processamento
    public static bool LiteAA; // celular: suavizacao de bordas (FXAA, barata)

    public static void SetupCamera(Camera cam)
    {
        if (Lite)
        {
            cam.allowHDR = false;
            cam.clearFlags = RenderSettings.skybox != null ? CameraClearFlags.Skybox : CameraClearFlags.SolidColor;
            UniversalAdditionalCameraData lite = cam.GetUniversalAdditionalCameraData();
            // FXAA roda no passe final de pos-processamento (sem volume de efeitos, so a suavizacao).
            lite.renderPostProcessing = LiteAA;
            lite.renderShadows = true;
            lite.antialiasing = LiteAA ? AntialiasingMode.FastApproximateAntialiasing : AntialiasingMode.None;
            lite.requiresDepthTexture = false;
            lite.requiresColorTexture = false;
            cam.farClipPlane = Mathf.Min(cam.farClipPlane, 260f);
            return;
        }
        cam.allowHDR = true;
        cam.clearFlags = RenderSettings.skybox != null ? CameraClearFlags.Skybox : CameraClearFlags.SolidColor;
        UniversalAdditionalCameraData d = cam.GetUniversalAdditionalCameraData();
        d.renderPostProcessing = true;
        d.renderShadows = true;
        d.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
        d.antialiasingQuality = AntialiasingQuality.High;
        d.dithering = true;
    }

    public static void Sky()
    {
        Shader sh = Shader.Find("Skybox/Procedural");
        if (sh == null) { RenderSettings.skybox = null; RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = new Color(0.55f, 0.55f, 0.6f); return; }
        Material m = new Material(sh);
        m.SetFloat("_SunSize", 0.045f);
        m.SetFloat("_SunSizeConvergence", 6f);
        m.SetFloat("_AtmosphereThickness", 1.15f);
        m.SetColor("_SkyTint", new Color(0.62f, 0.6f, 0.52f));
        m.SetColor("_GroundColor", new Color(0.62f, 0.52f, 0.38f));
        m.SetFloat("_Exposure", 1.15f);
        RenderSettings.skybox = m;
        RenderSettings.ambientMode = AmbientMode.Skybox;
        RenderSettings.ambientIntensity = 1.15f;
        RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
        RenderSettings.reflectionIntensity = 1f;
        DynamicGI.UpdateEnvironment();
    }

    // Sonda de reflexo em tempo real cobrindo a arena (refeita de vez em quando: os letreiros nao se mexem).
    public static void Probe(Transform parent, Vector3 size)
    {
        if (Lite) return;   // celular: refazer a sonda trava o quadro
        GameObject g = new GameObject("ReflectionProbe");
        g.transform.SetParent(parent, false);
        g.transform.position = new Vector3(0f, 3.5f, 0f);
        probe = g.AddComponent<ReflectionProbe>();
        probe.mode = ReflectionProbeMode.Realtime;
        probe.refreshMode = ReflectionProbeRefreshMode.ViaScripting;
        probe.timeSlicingMode = ReflectionProbeTimeSlicingMode.IndividualFaces;
        probe.resolution = 256;
        probe.hdr = true;
        probe.size = size;
        probe.boxProjection = true;
        probe.nearClipPlane = 0.3f;
        probe.farClipPlane = 400f;
        probe.intensity = 1f;
        probe.cullingMask = ~0;
        nextProbe = 0f;
    }

    public static void Tick()
    {
        if (bloom != null) bloom.active = Settings.bloom;
        Shader.SetGlobalFloat("_NeonRain", Settings.rain ? 1f : 0f);
        if (probe != null && Time.unscaledTime >= nextProbe)
        {
            probe.RenderProbe();
            nextProbe = Time.unscaledTime + 20f;
        }
    }
}
