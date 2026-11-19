using System.Collections.Generic;
using UnityEngine;

// Toda a arte visual e gerada por codigo: texturas procedurais, materiais e pecas feitas de primitivas.
public static class Art
{
    public static readonly Color Cyan = new Color(0.1f, 0.9f, 1f);
    public static readonly Color Magenta = new Color(1f, 0.15f, 0.75f);
    public static readonly Color Violet = new Color(0.6f, 0.35f, 1f);
    public static readonly Color Orange = new Color(1f, 0.55f, 0.1f);
    public static readonly Color Yellow = new Color(1f, 0.9f, 0.2f);
    public static readonly Color Green = new Color(0.25f, 1f, 0.45f);
    public static readonly Color Red = new Color(1f, 0.12f, 0.15f);
    public static readonly Color Sodium = new Color(1f, 0.62f, 0.28f);
    public static readonly Color Dark = new Color(0.05f, 0.05f, 0.07f);
    public static readonly Color Steel = new Color(0.16f, 0.17f, 0.2f);

    public static Texture2D Facade, FacadeEmis;

    static Shader neon;
    static Shader unlit;
    static Shader textShader;
    static Font font;
    static readonly Dictionary<string, Material> cache = new Dictionary<string, Material>();

    public static readonly Color Ball = new Color(0.12f, 0.45f, 1f);      // time BALL (azul)
    public static readonly Color Moloch = new Color(1f, 0.13f, 0.1f);    // time MOLOCH (vermelho)
    public static Color TeamColor(int team) { return team == 0 ? Ball : Moloch; }
    public static string TeamName(int team) { return team == 0 ? "BALL" : "MOLOCH"; }

    // ------------------------------ Texturas procedurais ------------------------------

    static float[,] Grid(int n, System.Random r)
    {
        float[,] g = new float[n, n];
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
                g[x, y] = (float)r.NextDouble();
        return g;
    }

    // Ruido de valor suave e repetivel (ladrilhavel).
    static float VNoise(float[,] g, float x, float y)
    {
        int n = g.GetLength(0);
        int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
        float fx = x - x0, fy = y - y0;
        fx = fx * fx * (3f - 2f * fx);
        fy = fy * fy * (3f - 2f * fy);
        int xa = ((x0 % n) + n) % n, xb = (xa + 1) % n;
        int ya = ((y0 % n) + n) % n, yb = (ya + 1) % n;
        return Mathf.Lerp(Mathf.Lerp(g[xa, ya], g[xb, ya], fx), Mathf.Lerp(g[xa, yb], g[xb, yb], fx), fy);
    }

    static Texture2D NewTex(string name, int size, Color[] px, int aniso)
    {
        Texture2D t = new Texture2D(size, size, TextureFormat.RGBA32, true);
        t.name = name;
        t.wrapMode = TextureWrapMode.Repeat;
        t.filterMode = FilterMode.Trilinear;
        t.anisoLevel = aniso;
        t.SetPixels(px);
        t.Apply(true);
        return t;
    }

    public static void BuildTextures()
    {
        if (Facade != null) return;
        const int S = 512;
        System.Random r = new System.Random(1234);
        float[,] g16 = Grid(16, r);
        Color[] px = new Color[S * S];

        // Fachada: 8 x 8 janelas. Uma mascara (RGB multiplica a cor da parede, A marca vidro)
        // e uma textura de luz (janelas acesas).
        Color[] em = new Color[S * S];
        const int cell = 64;
        Color[] litCol = new Color[64];
        for (int i = 0; i < 64; i++)
        {
            double d = r.NextDouble();
            if (d > 0.3) litCol[i] = Color.black;
            else if (d > 0.14) litCol[i] = new Color(1f, 0.74f, 0.42f);
            else if (d > 0.05) litCol[i] = new Color(0.55f, 0.8f, 1f);
            else litCol[i] = d > 0.02 ? new Color(1f, 0.3f, 0.7f) : new Color(0.3f, 1f, 0.9f);
        }
        for (int y = 0; y < S; y++)
        {
            for (int x = 0; x < S; x++)
            {
                int cx = x / cell, cy = y / cell, lx = x % cell, ly = y % cell;
                bool glass = lx >= 10 && lx < 54 && ly >= 14 && ly < 52;
                bool frame = !glass && lx >= 7 && lx < 57 && ly >= 11 && ly < 55;
                float fine = (float)r.NextDouble();
                float mid = VNoise(g16, x / 32f, y / 32f);
                Color c;
                Color e = Color.black;
                if (glass)
                {
                    c = new Color(0.05f, 0.06f, 0.08f, 1f);
                    Color lc = litCol[cy * 8 + cx];
                    if (lc.maxColorComponent > 0f)
                    {
                        float blind = (ly % 6 < 1) ? 0.55f : 1f;
                        float grad = 0.55f + 0.45f * ((ly - 14) / 38f);
                        float mull = (lx == 31 || lx == 32) ? 0.25f : 1f;
                        float curtain = lx < 10 + (int)(Hash01(cx, cy) * 18f) ? 0.45f : 1f;
                        e = lc * (blind * grad * mull * curtain * (0.8f + fine * 0.2f));
                    }
                }
                else if (frame) c = new Color(0.18f, 0.18f, 0.2f, 0.35f);
                else
                {
                    float v = 0.8f + mid * 0.2f;
                    if (ly < 3) v *= 0.6f;
                    c = new Color(v, v, v, 0f);
                }
                px[y * S + x] = c;
                em[y * S + x] = new Color(e.r, e.g, e.b, 1f);
            }
        }
        Facade = NewTex("facade", S, px, 8);
        FacadeEmis = NewTex("facadeEmis", S, em, 8);
    }

    static float Hash01(int x, int y)
    {
        return Mathf.Abs(Mathf.Sin(x * 12.9898f + y * 78.233f) * 43758.5453f) % 1f;
    }

    // ------------------------------ Materiais ------------------------------

    static Shader Neon()
    {
        if (neon == null) neon = Resources.Load<Shader>("NeonSurface");
        if (neon == null) neon = Shader.Find("Universal Render Pipeline/Lit");
        return neon;
    }

    public static Material Mat(Color baseCol, Color emission, float gridScale = 0f, Color grid = default(Color), float smooth = 0.6f, float metal = 0.3f)
    {
        string key = baseCol + "|" + emission + "|" + gridScale + "|" + grid + "|" + smooth + "|" + metal;
        Material m;
        if (cache.TryGetValue(key, out m) && m != null) return m;

        m = new Material(Neon());
        m.SetColor("_Color", baseCol);
        m.SetColor("_EmissionColor", emission);
        m.SetColor("_GridColor", grid);
        m.SetFloat("_GridScale", gridScale);
        m.SetFloat("_Smoothness", smooth);
        m.SetFloat("_Metallic", metal);
        cache[key] = m;
        return m;
    }

    // Material com texturas PBR reais (Resources/Tex/<nome>_c/_n/_m), mapeadas pelo mundo.
    // "wet" e o quanto a superficie fica exposta a chuva (0 = interior/objeto seco).
    public static Material Pbr(string tex, Color tint, float metersPerTile, float smoothMul = 1f, float metalAdd = 0f, float wet = 1f, float normal = 1f)
    {
        string key = "pbr|" + tex + "|" + tint + "|" + metersPerTile + "|" + smoothMul + "|" + metalAdd + "|" + wet + "|" + normal;
        Material m;
        if (cache.TryGetValue(key, out m) && m != null) return m;

        m = new Material(Neon());
        m.SetColor("_Color", tint);
        m.SetColor("_EmissionColor", Color.black);
        m.SetFloat("_GridScale", 0f);
        m.SetFloat("_Wet", wet);
        Texture2D c = Resources.Load<Texture2D>("Tex/" + tex + "_c");
        if (c != null)
        {
            m.SetTexture("_BaseMap", c);
            m.SetTexture("_NormalMap", Resources.Load<Texture2D>("Tex/" + tex + "_n"));
            m.SetTexture("_MaskMap", Resources.Load<Texture2D>("Tex/" + tex + "_m"));
            m.SetFloat("_UseMaps", 1f);
            m.SetFloat("_TexScale", 1f / Mathf.Max(0.01f, metersPerTile));
            m.SetFloat("_NormalStrength", normal);
            m.SetFloat("_Smoothness", smoothMul);
            m.SetFloat("_Metallic", metalAdd);
        }
        else
        {
            m.SetFloat("_Smoothness", 0.5f * smoothMul);
            m.SetFloat("_Metallic", metalAdd);
        }
        cache[key] = m;
        return m;
    }

    // Igual a Pbr, mas com a textura presa ao objeto (para armas e outras pecas que se movem).
    public static Material PbrLocal(string tex, Color tint, float metersPerTile, float smoothMul = 1f, float metalAdd = 0f, float normal = 1f)
    {
        string key = "pbrL|" + tex + "|" + tint + "|" + metersPerTile + "|" + smoothMul + "|" + metalAdd + "|" + normal;
        Material m;
        if (cache.TryGetValue(key, out m) && m != null) return m;
        m = new Material(Pbr(tex, tint, metersPerTile, smoothMul, metalAdd, 0f, normal));
        m.SetFloat("_Local", 1f);
        cache[key] = m;
        return m;
    }

    // Parede de predio: material PBR + grade de janelas (vidro e luzes acesas).
    public static Material FacadeMat(string tex, Color tint, float metersPerTile, float windowScale, Color lightColor)
    {
        Material m = new Material(Pbr(tex, tint, metersPerTile, 1f, 0f, 1f));
        m.SetTexture("_DetailTex", Facade);
        m.SetFloat("_UseDetail", 1f);
        m.SetTexture("_EmisTex", FacadeEmis);
        m.SetFloat("_EmisScale", windowScale);
        m.SetColor("_EmisTexColor", lightColor);
        return m;
    }

    public static Material Glow(Color c, float power = 1.6f)
    {
        return Mat(Color.black, c * power * GlowBoost, 0f, default(Color), 0.2f, 0f);
    }

    // Em HDR com bloom, as luzes de neon precisam passar bem de 1.0 para brilhar.
    public const float GlowBoost = 2.2f;

    public static Material Unlit(Color c)
    {
        string key = "unlit|" + c;
        Material m;
        if (cache.TryGetValue(key, out m) && m != null) return m;
        if (unlit == null) unlit = Shader.Find("Sprites/Default");
        if (unlit == null) return Glow(c);
        m = new Material(unlit);
        m.color = c;
        cache[key] = m;
        return m;
    }

    // ------------------------------ Pecas ------------------------------

    public static GameObject Prim(PrimitiveType type, Transform parent, Vector3 localPos, Vector3 size, Material m, bool collider = false, string name = "part")
    {
        GameObject g = GameObject.CreatePrimitive(type);
        g.name = name;
        g.transform.SetParent(parent, false);
        g.transform.localPosition = localPos;
        g.transform.localScale = size;
        Renderer r = g.GetComponent<Renderer>();
        r.sharedMaterial = m;
        if (!collider)
        {
            Collider c = g.GetComponent<Collider>();
            if (c != null) Object.DestroyImmediate(c);
        }
        // Pecas que so emitem luz (neon, paineis) nao projetam sombra.
        bool emissive = m != null && m.HasProperty("_EmissionColor") && m.GetColor("_EmissionColor").maxColorComponent > 0.3f;
        if (emissive) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return g;
    }

    public static GameObject Box(Transform parent, Vector3 localPos, Vector3 size, Material m, bool collider = false, string name = "box")
    {
        return Prim(PrimitiveType.Cube, parent, localPos, size, m, collider, name);
    }

    // No URP a luz cai com o quadrado da distancia; compensa para manter o alcance visual.
    public const float LightBoost = 6f;

    public static Light PointLight(Transform parent, Vector3 localPos, Color c, float range, float intensity)
    {
        GameObject g = new GameObject("light");
        g.transform.SetParent(parent, false);
        g.transform.localPosition = localPos;
        Light l = g.AddComponent<Light>();
        l.type = LightType.Point;
        l.color = c;
        l.range = range;
        l.intensity = intensity * LightBoost;
        l.shadows = LightShadows.None;
        return l;
    }

    // Texto 3D para letreiros. "forward" aponta para dentro da parede (o texto e lido de quem olha nessa direcao).
    public static TextMesh Text3D(Transform parent, Vector3 pos, Vector3 forward, string text, Color color, float maxWidth, float maxHeight)
    {
        if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (font == null) return null;
        if (textShader == null) textShader = Resources.Load<Shader>("Text3D");

        GameObject g = new GameObject("sign_" + text);
        g.transform.SetParent(parent, false);
        g.transform.position = pos;
        g.transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
        TextMesh tm = g.AddComponent<TextMesh>();
        tm.font = font;
        tm.fontSize = 64;
        tm.anchor = TextAnchor.MiddleCenter;
        tm.alignment = TextAlignment.Center;
        tm.fontStyle = FontStyle.Bold;
        tm.text = text;
        tm.color = color;
        // Com fontSize 64, characterSize 0.1 da cerca de 0.64 m de altura e ~0.38 m por letra.
        float byWidth = 0.1f * maxWidth / (0.4f * Mathf.Max(1, text.Length));
        float byHeight = 0.1f * maxHeight / 0.7f;
        tm.characterSize = Mathf.Min(byWidth, byHeight);

        MeshRenderer mr = g.GetComponent<MeshRenderer>();
        string key = "text3d";
        Material m;
        if (!cache.TryGetValue(key, out m) || m == null)
        {
            if (textShader != null)
            {
                m = new Material(textShader);
                m.mainTexture = font.material.mainTexture;
            }
            else m = font.material;
            cache[key] = m;
        }
        mr.sharedMaterial = m;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return tm;
    }
}
