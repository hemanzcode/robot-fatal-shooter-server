using UnityEngine;

// Sistema de mira: a MIRA VISUAL (desenho no centro da tela), a ASSISTENCIA DE MIRA e a SENSIBILIDADE.
// Sao partes separadas: mudar a mira nao mexe na assistencia, e a assistencia nunca altera a sensibilidade salva.
// Tudo fica salvo no aparelho (PlayerPrefs) e vale na hora, sem reiniciar a partida.
public static class Aim
{
    // ---------------- Mira visual ----------------
    public static bool chOn;                 // mira personalizada (desligada = mira padrao do jogo)
    public static int chShape;               // 0 cruz, 1 ponto, 2 cruz + ponto, 3 circulo, 4 T
    public static int chColor = 1;
    public static float chSize = 10f, chThick = 2f, chGap = 5f, chAlpha = 0.95f;
    public static bool chOutline = true, chDot, chDynamic = true;
    public static readonly string[] Shapes = { "CRUZ", "PONTO", "CRUZ + PONTO", "CIRCULO", "T" };
    public static readonly string[] ColorNames = { "BRANCO", "VERDE", "AMARELO", "CIANO", "ROSA", "VERMELHO", "LARANJA", "ROXO" };
    public static readonly Color[] Colors =
    {
        Color.white, new Color(0.3f, 1f, 0.35f), new Color(1f, 0.92f, 0.2f), new Color(0.2f, 0.95f, 1f),
        new Color(1f, 0.35f, 0.85f), new Color(1f, 0.25f, 0.2f), new Color(1f, 0.6f, 0.15f), new Color(0.7f, 0.45f, 1f),
    };

    // ---------------- Assistencia de mira (perfil 0 = mouse e teclado, 1 = toque) ----------------
    // Valores sugeridos pelo teste: 35% geral, 30% desaceleracao, 20% acompanhamento, 40% no ADS (toque).
    // No mouse e teclado ela comeca desligada e mais discreta (precisao competitiva).
    public static readonly bool[] aaOn = { false, true };
    public static readonly float[] aaStrength = { 0.15f, 0.35f };
    public static readonly float[] aaSlow = { 0.20f, 0.30f };
    public static readonly float[] aaFollow = { 0.10f, 0.20f };
    public static readonly float[] aaAds = { 0.20f, 0.40f };
    public static readonly string[] ProfileNames = { "MOUSE E TECLADO", "TOQUE (CELULAR)" };
    public static int Profile { get { return TouchPad.On ? 1 : 0; } }

    // ---------------- Sensibilidade ----------------
    // Horizontal = Settings.sensitivity (ja existia). Vertical e ADS sao novas.
    public static float sensY = 2f, sensAds = 2f, adsMult = 1f;

    public static void Load()
    {
        chOn = PlayerPrefs.GetInt("ch_on", 0) == 1;
        chShape = Mathf.Clamp(PlayerPrefs.GetInt("ch_shape", 0), 0, Shapes.Length - 1);
        chColor = Mathf.Clamp(PlayerPrefs.GetInt("ch_color", 1), 0, Colors.Length - 1);
        chSize = PlayerPrefs.GetFloat("ch_size", 10f);
        chThick = PlayerPrefs.GetFloat("ch_thick", 2f);
        chGap = PlayerPrefs.GetFloat("ch_gap", 5f);
        chAlpha = PlayerPrefs.GetFloat("ch_alpha", 0.95f);
        chOutline = PlayerPrefs.GetInt("ch_outline", 1) == 1;
        chDot = PlayerPrefs.GetInt("ch_dot", 0) == 1;
        chDynamic = PlayerPrefs.GetInt("ch_dyn", 1) == 1;
        for (int p = 0; p < 2; p++)
        {
            aaOn[p] = PlayerPrefs.GetInt("aa_on" + p, p == 1 ? 1 : 0) == 1;
            aaStrength[p] = Mathf.Clamp01(PlayerPrefs.GetFloat("aa_str" + p, p == 1 ? 0.35f : 0.15f));
            aaSlow[p] = Mathf.Clamp01(PlayerPrefs.GetFloat("aa_slow" + p, p == 1 ? 0.30f : 0.20f));
            aaFollow[p] = Mathf.Clamp01(PlayerPrefs.GetFloat("aa_follow" + p, p == 1 ? 0.20f : 0.10f));
            aaAds[p] = Mathf.Clamp01(PlayerPrefs.GetFloat("aa_ads" + p, p == 1 ? 0.40f : 0.20f));
        }
        sensY = PlayerPrefs.GetFloat("na_sensy", Settings.sensitivity);
        sensAds = PlayerPrefs.GetFloat("na_sensads", Settings.sensitivity);
        adsMult = PlayerPrefs.GetFloat("na_adsmult", 1f);
    }

    public static void Save()
    {
        PlayerPrefs.SetInt("ch_on", chOn ? 1 : 0);
        PlayerPrefs.SetInt("ch_shape", chShape);
        PlayerPrefs.SetInt("ch_color", chColor);
        PlayerPrefs.SetFloat("ch_size", chSize);
        PlayerPrefs.SetFloat("ch_thick", chThick);
        PlayerPrefs.SetFloat("ch_gap", chGap);
        PlayerPrefs.SetFloat("ch_alpha", chAlpha);
        PlayerPrefs.SetInt("ch_outline", chOutline ? 1 : 0);
        PlayerPrefs.SetInt("ch_dot", chDot ? 1 : 0);
        PlayerPrefs.SetInt("ch_dyn", chDynamic ? 1 : 0);
        for (int p = 0; p < 2; p++)
        {
            PlayerPrefs.SetInt("aa_on" + p, aaOn[p] ? 1 : 0);
            PlayerPrefs.SetFloat("aa_str" + p, aaStrength[p]);
            PlayerPrefs.SetFloat("aa_slow" + p, aaSlow[p]);
            PlayerPrefs.SetFloat("aa_follow" + p, aaFollow[p]);
            PlayerPrefs.SetFloat("aa_ads" + p, aaAds[p]);
        }
        PlayerPrefs.SetFloat("na_sensy", sensY);
        PlayerPrefs.SetFloat("na_sensads", sensAds);
        PlayerPrefs.SetFloat("na_adsmult", adsMult);
    }

    // "Restaurar mira": so as opcoes visuais (nao mexe em assistencia nem sensibilidade).
    public static void ResetCrosshair()
    {
        chShape = 0; chColor = 1; chSize = 10f; chThick = 2f; chGap = 5f; chAlpha = 0.95f;
        chOutline = true; chDot = false; chDynamic = true;
    }

    public static void ResetAssist(int p)
    {
        aaOn[p] = p == 1;
        aaStrength[p] = p == 1 ? 0.35f : 0.15f;
        aaSlow[p] = p == 1 ? 0.30f : 0.20f;
        aaFollow[p] = p == 1 ? 0.20f : 0.10f;
        aaAds[p] = p == 1 ? 0.40f : 0.20f;
    }

    // Desenha a mira personalizada em (cx, cy). spread = abertura atual da arma (so conta com "mira dinamica").
    public static void Draw(float cx, float cy, float spread)
    {
        Color c = Colors[chColor]; c.a = chAlpha;
        Color o = new Color(0f, 0f, 0f, chAlpha * 0.85f);
        float t = Mathf.Max(1f, Mathf.Round(chThick)), len = chSize;
        float gap = chGap + (chDynamic ? spread * 9f : 0f);
        bool cross = chShape == 0 || chShape == 2 || chShape == 4;
        bool dot = chDot || chShape == 1 || chShape == 2;
        if (chOutline)
        {
            if (cross) Arms(cx, cy, gap, len, t, o, 1f);
            if (chShape == 3) Ring(cx, cy, gap + len * 0.5f, t, o, 1f);
            if (dot) { float d = t + 2f; Game.Fill(cx - d / 2f - 1f, cy - d / 2f - 1f, d + 2f, d + 2f, o); }
        }
        if (cross) Arms(cx, cy, gap, len, t, c, 0f);
        if (chShape == 3) Ring(cx, cy, gap + len * 0.5f, t, c, 0f);
        if (dot) { float d = t + 2f; Game.Fill(cx - d / 2f, cy - d / 2f, d, d, c); }
    }

    static void Arms(float cx, float cy, float gap, float len, float t, Color c, float pad)
    {
        float h = t / 2f;
        Game.Fill(cx - gap - len - pad, cy - h - pad, len + pad * 2f, t + pad * 2f, c);           // esquerda
        Game.Fill(cx + gap - pad, cy - h - pad, len + pad * 2f, t + pad * 2f, c);                 // direita
        if (chShape != 4) Game.Fill(cx - h - pad, cy - gap - len - pad, t + pad * 2f, len + pad * 2f, c);   // cima (o T nao tem)
        Game.Fill(cx - h - pad, cy + gap - pad, t + pad * 2f, len + pad * 2f, c);                 // baixo
    }

    static void Ring(float cx, float cy, float r, float t, Color c, float pad)
    {
        int n = Mathf.Clamp(Mathf.RoundToInt(r * 1.6f), 16, 64);
        float s = t + 0.5f + pad * 2f;
        for (int i = 0; i < n; i++)
        {
            float a = i * Mathf.PI * 2f / n;
            Game.Fill(cx + Mathf.Cos(a) * r - s / 2f, cy + Mathf.Sin(a) * r - s / 2f, s, s, c);
        }
    }
}
