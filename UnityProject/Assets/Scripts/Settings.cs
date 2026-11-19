using UnityEngine;

// Opcoes do jogador e da partida, salvas entre sessoes.
public static class Settings
{
    public static float sensitivity = 2f;
    public static float fov = 90f;
    public static float master = 1f;
    public static float music = 0.7f;
    public static float sfx = 0.9f;
    public static bool invertY;
    public static bool bloom = true;
    public static bool rain = false;   // mapa arido: sem chuva
    public static int quality = -1;
    public static int difficulty = 1;   // 0 facil, 1 normal, 2 dificil
    public static int teamSize = 4;
    public static int scoreLimit = 30;
    public static int minutes = 8;
    public static bool tutorialDone;
    public static int agent;
    public static int mode;            // 0 mata-mata, 1 bomba
    public static int side = 2;        // time preferido: 0 BALL, 1 MOLOCH, 2 automatico
    public static readonly string[] SideNames = { "BALL (AZUL)", "MOLOCH (VERMELHO)", "AUTOMATICO" };
    // Time da partida offline (automatico sorteia).
    public static int ResolveSide() { return side == 2 ? Random.Range(0, 2) : side; }
    // Celular: graficos (0 desempenho, 1 equilibrado, 2 nitido) e controles de toque personalizados.
    public static int gfx = 1;
    public static readonly string[] GfxNames = { "DESEMPENHO", "EQUILIBRADO", "NITIDO" };
    public static float hudAlpha = 0.6f;
    public static bool aimHold;
    public static bool voiceOn = true;      // ouvir/falar com o time
    public static float voiceVol = 1f;
    public static string hudLayout = "";
    public static readonly string[] ModeNames = { "MATA-MATA", "BOMBA" };

    public static readonly string[] DifficultyNames = { "FACIL", "NORMAL", "DIFICIL" };

    public static void Load()
    {
        sensitivity = PlayerPrefs.GetFloat("na_sens", 2f);
        fov = PlayerPrefs.GetFloat("na_fov", 90f);
        master = PlayerPrefs.GetFloat("na_master", 1f);
        music = PlayerPrefs.GetFloat("na_music", 0.7f);
        sfx = PlayerPrefs.GetFloat("na_sfx", 0.9f);
        invertY = PlayerPrefs.GetInt("na_inverty", 0) == 1;
        bloom = PlayerPrefs.GetInt("na_bloom", 1) == 1;
        rain = false;
        quality = PlayerPrefs.GetInt("na_quality", QualitySettings.GetQualityLevel());
        difficulty = Mathf.Clamp(PlayerPrefs.GetInt("na_diff", 1), 0, 2);
        teamSize = Mathf.Clamp(PlayerPrefs.GetInt("na_team", 4), 1, 5);
        scoreLimit = Mathf.Clamp(PlayerPrefs.GetInt("na_score", 30), 10, 50);
        minutes = Mathf.Clamp(PlayerPrefs.GetInt("na_minutes", 8), 3, 15);
        tutorialDone = PlayerPrefs.GetInt("na_tutdone", 0) == 1;
        agent = Mathf.Clamp(PlayerPrefs.GetInt("na_agent", 0), 0, 11);
        mode = Mathf.Clamp(PlayerPrefs.GetInt("na_mode", 0), 0, 1);
        side = Mathf.Clamp(PlayerPrefs.GetInt("na_side", 2), 0, 2);
        gfx = Mathf.Clamp(PlayerPrefs.GetInt("na_gfx", 1), 0, 2);
        hudAlpha = Mathf.Clamp(PlayerPrefs.GetFloat("na_hudalpha", 0.6f), 0.2f, 1f);
        aimHold = PlayerPrefs.GetInt("na_aimhold", 0) == 1;
        voiceOn = PlayerPrefs.GetInt("na_voice", 1) == 1;
        voiceVol = Mathf.Clamp01(PlayerPrefs.GetFloat("na_voicevol", 1f));
        hudLayout = PlayerPrefs.GetString("na_hud", "");
        TouchPad.ReloadLayout();
    }

    public static void Save()
    {
        PlayerPrefs.SetFloat("na_sens", sensitivity);
        PlayerPrefs.SetFloat("na_fov", fov);
        PlayerPrefs.SetFloat("na_master", master);
        PlayerPrefs.SetFloat("na_music", music);
        PlayerPrefs.SetFloat("na_sfx", sfx);
        PlayerPrefs.SetInt("na_inverty", invertY ? 1 : 0);
        PlayerPrefs.SetInt("na_bloom", bloom ? 1 : 0);
        PlayerPrefs.SetInt("na_rain", rain ? 1 : 0);
        PlayerPrefs.SetInt("na_quality", quality);
        PlayerPrefs.SetInt("na_diff", difficulty);
        PlayerPrefs.SetInt("na_team", teamSize);
        PlayerPrefs.SetInt("na_score", scoreLimit);
        PlayerPrefs.SetInt("na_minutes", minutes);
        PlayerPrefs.SetInt("na_side", side);
        PlayerPrefs.SetInt("na_gfx", gfx);
        PlayerPrefs.SetFloat("na_hudalpha", hudAlpha);
        PlayerPrefs.SetInt("na_aimhold", aimHold ? 1 : 0);
        PlayerPrefs.SetInt("na_voice", voiceOn ? 1 : 0);
        PlayerPrefs.SetFloat("na_voicevol", voiceVol);
        PlayerPrefs.SetString("na_hud", hudLayout);
        PlayerPrefs.SetInt("na_tutdone", tutorialDone ? 1 : 0);
        PlayerPrefs.SetInt("na_agent", agent);
        PlayerPrefs.SetInt("na_mode", mode);
        PlayerPrefs.Save();
    }

    public static void Apply()
    {
        AudioListener.volume = master;
        int count = QualitySettings.names.Length;
        if (!TouchPad.On && quality >= 0 && quality < count && quality != QualitySettings.GetQualityLevel())
            QualitySettings.SetQualityLevel(quality, true);
        if (!TouchPad.On) QualitySettings.pixelLightCount = 8;
        ApplyGfx();
    }

#if UNITY_WEBGL && !UNITY_EDITOR
    [System.Runtime.InteropServices.DllImport("__Internal")] static extern void RfsSetDpr(float max);
#endif

    // Nitidez no celular: resolucao da tela (pixels por ponto), escala de render, texturas e suavizacao de bordas.
    // Aplica na hora, sem recarregar.
    public static void ApplyGfx()
    {
        if (!TouchPad.On) return;
        float dpr = gfx == 0 ? 1f : gfx == 1 ? 1.5f : 2f;
#if UNITY_WEBGL && !UNITY_EDITOR
        RfsSetDpr(dpr);
#endif
        QualitySettings.globalTextureMipmapLimit = gfx == 0 ? 1 : 0;
        var urp = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline as UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset;
        if (urp != null) urp.renderScale = gfx == 2 ? 1f : 0.85f;
        Post.LiteAA = gfx >= 1;
        foreach (Camera c in Camera.allCameras) Post.SetupCamera(c);
    }
}
