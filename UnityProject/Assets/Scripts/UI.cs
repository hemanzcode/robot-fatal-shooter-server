using System.Collections.Generic;
using UnityEngine;

// Kit visual do Fatal Shooter (identidade nova, nov/2026): cores, fontes e componentes desenhados com IMGUI.
// Regra da paleta: quase tudo em tons escuros e neutros; amarelo so em pequenos destaques (borda do botao
// principal, aba selecionada, icone de moeda, confirmacao); ciano para informacao de agente e estados.
// Fontes: Barlow Condensed (titulos/botoes) e Barlow (textos), licenca livre OFL, em Resources/Fonts.
public static class UI
{
    public static readonly Color Bg = Hex(0x0B0E13);
    public static readonly Color Panel = Hex(0x121821);
    public static readonly Color Raised = Hex(0x1B2430);
    public static readonly Color Line = Hex(0x263140);
    public static readonly Color LineHi = Hex(0x3A4757);
    public static readonly Color Text = Hex(0xE8ECF1);
    public static readonly Color Text2 = Hex(0xB7C1CD);
    public static readonly Color Muted = Hex(0x8E9AAB);
    public static readonly Color Dim = Hex(0x5A6575);
    public static readonly Color Yellow = Hex(0xE6B422);
    public static readonly Color Cyan = Hex(0x4FC3D9);
    public static readonly Color Ok = Hex(0x5BC27A);
    public static readonly Color Bad = Hex(0xE5484D);

    public enum Kind { Normal, Primary, Ghost, Confirm }

    static Color Hex(int h) { return new Color(((h >> 16) & 255) / 255f, ((h >> 8) & 255) / 255f, (h & 255) / 255f, 1f); }
    public static Color A(Color c, float a) { c.a = a; return c; }

    // No navegador (WebGL com espaco de cor linear) a Unity nao converte as cores da interface IMGUI:
    // sem esta correcao o grafite vira cinza (#0B0E13 aparece como #3D4350). No Editor fica como esta.
    static int gfix = -1;
    public static Color Fix(Color c)
    {
        if (gfix < 0) gfix = Application.platform == RuntimePlatform.WebGLPlayer && QualitySettings.activeColorSpace == ColorSpace.Linear ? 1 : 0;
        if (gfix == 0) return c;
        return new Color(Mathf.GammaToLinearSpace(c.r), Mathf.GammaToLinearSpace(c.g), Mathf.GammaToLinearSpace(c.b), c.a);
    }

    // ------------------------------- Fontes -------------------------------
    static Font head, headBold, body, bodyBold;
    static bool loaded;
    static void Load()
    {
        if (loaded) return;
        loaded = true;
        head = Resources.Load<Font>("Fonts/BarlowCondensed-SemiBold");
        headBold = Resources.Load<Font>("Fonts/BarlowCondensed-Bold");
        body = Resources.Load<Font>("Fonts/Barlow-Medium");
        bodyBold = Resources.Load<Font>("Fonts/Barlow-SemiBold");
    }
    public static Font Head { get { Load(); return head; } }
    public static Font HeadBold { get { Load(); return headBold != null ? headBold : head; } }
    public static Font Body { get { Load(); return body; } }
    public static Font BodyBold { get { Load(); return bodyBold != null ? bodyBold : body; } }

    static GUIStyle st;
    // Texto sem sombra (visual limpo). font nulo = fonte padrao do jogo.
    public static void Label(Rect r, string s, Font f, int size, Color c, TextAnchor a)
    {
        if (st == null) { st = new GUIStyle(GUI.skin.label); st.wordWrap = false; st.richText = false; st.clipping = TextClipping.Clip; st.padding = new RectOffset(0, 0, 0, 0); }
        st.font = f;
        st.fontStyle = f == null ? FontStyle.Bold : FontStyle.Normal;
        st.fontSize = size;
        st.alignment = a;
        st.wordWrap = false;
        st.normal.textColor = Fix(c);
        GUI.Label(r, s, st);
    }
    public static void Wrap(Rect r, string s, Font f, int size, Color c)
    {
        Label(r, "", f, size, c, TextAnchor.UpperLeft);
        st.wordWrap = true;
        st.normal.textColor = Fix(c);
        GUI.Label(r, s, st);
        st.wordWrap = false;
    }

    // ------------------------------- Formas -------------------------------
    public static void Fill(Rect r, Color c)
    {
        GUI.color = Fix(c);
        GUI.DrawTexture(r, Texture2D.whiteTexture);
        GUI.color = Color.white;
    }
    public static void Border(Rect r, Color c, float t = 1f)
    {
        Fill(new Rect(r.x, r.y, r.width, t), c);
        Fill(new Rect(r.x, r.yMax - t, r.width, t), c);
        Fill(new Rect(r.x, r.y + t, t, r.height - 2f * t), c);
        Fill(new Rect(r.xMax - t, r.y + t, t, r.height - 2f * t), c);
    }
    // Painel: fundo escuro definido + borda discreta.
    public static void PanelBox(Rect r, float alpha = 0.9f)
    {
        Fill(r, A(Panel, alpha));
        Border(r, Line);
    }

    static Texture2D gradH;
    // Degrade horizontal (da cor da esquerda para a da direita).
    public static void GradientH(Rect r, Color left, Color right)
    {
        if (gradH == null)
        {
            gradH = new Texture2D(256, 1, TextureFormat.RGBA32, false);
            gradH.wrapMode = TextureWrapMode.Clamp;
            for (int i = 0; i < 256; i++) gradH.SetPixel(i, 0, new Color(1f, 1f, 1f, 1f - i / 255f));
            gradH.Apply();
        }
        // Camada da direita inteira + camada da esquerda sumindo.
        Fill(r, right);
        GUI.color = Fix(left);
        GUI.DrawTexture(r, gradH);
        GUI.color = Color.white;
    }
    static Texture2D gradV;
    public static void GradientUp(Rect r, Color bottom)
    {
        if (gradV == null)
        {
            gradV = new Texture2D(1, 256, TextureFormat.RGBA32, false);
            gradV.wrapMode = TextureWrapMode.Clamp;
            for (int i = 0; i < 256; i++) gradV.SetPixel(0, i, new Color(1f, 1f, 1f, 1f - i / 255f));
            gradV.Apply();
        }
        GUI.color = Fix(bottom);
        GUI.DrawTexture(r, gradV);
        GUI.color = Color.white;
    }

    // Losango (icone de moeda / marcador).
    public static void Diamond(float cx, float cy, float size, Color c)
    {
        Matrix4x4 keep = GUI.matrix;
        GUIUtility.RotateAroundPivot(45f, new Vector2(cx, cy));
        Fill(new Rect(cx - size / 2f, cy - size / 2f, size, size), c);
        GUI.matrix = keep;
    }
    // Seta ">" (caractere da fonte de titulos).
    public static void Chevron(float cx, float cy, float size, Color c)
    {
        Label(new Rect(cx - size, cy - size, size * 2f, size * 2f), "\u203A", HeadBold, Mathf.RoundToInt(size * 1.9f), c, TextAnchor.MiddleCenter);
    }
    // Cadeado (botao desabilitado).
    public static void Lock(float cx, float cy, float s, Color c)
    {
        Fill(new Rect(cx - s * 0.4f, cy - s * 0.05f, s * 0.8f, s * 0.55f), c);
        float t = Mathf.Max(2f, s * 0.12f);
        Fill(new Rect(cx - s * 0.28f, cy - s * 0.42f, t, s * 0.4f), c);
        Fill(new Rect(cx + s * 0.28f - t, cy - s * 0.42f, t, s * 0.4f), c);
        Fill(new Rect(cx - s * 0.28f, cy - s * 0.45f, s * 0.56f, t), c);
    }

    // ------------------------------- Animacao de hover -------------------------------
    // Cada botao guarda um valor 0..1 que sobe/desce suave (~120 ms), pela posicao do retangulo.
    static readonly Dictionary<int, float> hover = new Dictionary<int, float>();
    static int lastFrame = -1;
    static float Hover(Rect r, bool on)
    {
        int key = (Mathf.RoundToInt(r.x) * 73856093) ^ (Mathf.RoundToInt(r.y) * 19349663) ^ (Mathf.RoundToInt(r.width) * 83492791);
        float v;
        hover.TryGetValue(key, out v);
        if (Event.current.type == EventType.Repaint)
        {
            if (lastFrame != Time.frameCount) { lastFrame = Time.frameCount; if (hover.Count > 400) hover.Clear(); }
            v = Mathf.MoveTowards(v, on ? 1f : 0f, Time.unscaledDeltaTime / 0.12f);
            hover[key] = v;
        }
        return v;
    }

    // Toque no celular: o "hover" so aparece enquanto o dedo esta no botao.
    static bool Over(Rect r)
    {
        Event e = Event.current;
        if (TouchPad.On && Input.touchCount == 0 && e.type != EventType.MouseDown) return false;
        return r.Contains(e.mousePosition);
    }

    // ------------------------------- Botao -------------------------------
    // Devolve true no clique. sub = linha pequena abaixo do rotulo (opcional). tag = etiqueta a direita.
    public static bool Button(Rect r, string label, Kind kind = Kind.Normal, int size = 22, bool enabled = true, string sub = null, string tag = null, TextAnchor align = TextAnchor.MiddleLeft)
    {
        Event e = Event.current;
        bool over = enabled && Over(r);
        float h = Hover(r, over);
        Font f = kind == Kind.Primary || kind == Kind.Confirm ? HeadBold : Head;
        Color tc = enabled ? Text : Dim;
        if (!enabled)
        {
            Fill(r, A(Panel, 0.5f));
            Border(r, A(Line, 0.8f));
        }
        else if (kind == Kind.Primary)
        {
            Fill(r, A(Yellow, Mathf.Lerp(0.10f, 0.17f, h)));
            Border(r, A(Yellow, Mathf.Lerp(0.55f, 0.9f, h)));
        }
        else if (kind == Kind.Confirm)
        {
            Fill(r, Color.Lerp(Yellow, new Color(1f, 0.82f, 0.3f), h));
            tc = Bg;
        }
        else if (kind == Kind.Ghost)
        {
            if (h > 0f) Fill(r, A(Color.white, 0.06f * h));
            tc = Color.Lerp(Text2, Text, h);
        }
        else
        {
            Fill(r, Color.Lerp(A(Panel, 0.82f), A(Raised, 0.95f), h));
            Border(r, Color.Lerp(Line, LineHi, h));
        }
        float pad = align == TextAnchor.MiddleCenter ? 8f : Mathf.Min(24f, r.height * 0.36f);
        float right = 0f;
        if (!enabled) { Lock(r.xMax - 24f, r.center.y, 18f, Dim); right = 40f; }
        else if (kind == Kind.Primary) { Chevron(r.xMax - 22f, r.center.y, 18f, Yellow); right = 40f; }
        if (tag != null && enabled)
        {
            float tw = 16f + tag.Length * 8.5f;
            Rect tr = new Rect(r.xMax - right - tw - 14f, r.center.y - 13f, tw, 26f);
            Border(tr, A(Cyan, 0.55f));
            Label(tr, tag, Body, 13, Cyan, TextAnchor.MiddleCenter);
        }
        if (sub != null)
        {
            Label(new Rect(r.x + pad, r.y + r.height * 0.14f, r.width - pad - right, r.height * 0.5f), label, f, size, tc, TextAnchor.MiddleLeft);
            Label(new Rect(r.x + pad, r.y + r.height * 0.55f, r.width - pad - right, r.height * 0.32f), sub, Body, Mathf.RoundToInt(size * 0.52f), enabled ? Text2 : Dim, TextAnchor.MiddleLeft);
        }
        else Label(new Rect(r.x + pad, r.y, r.width - pad * (align == TextAnchor.MiddleCenter ? 2f : 1f) - right, r.height), label, f, size, tc, align);

        if (enabled && e.type == EventType.MouseDown && e.button == 0 && r.Contains(e.mousePosition))
        {
            e.Use();
            Sfx.UI("hit", 0.45f, 0.8f);
            return true;
        }
        return false;
    }

    // Aba: texto; a selecionada fica clara com um traco amarelo embaixo.
    public static bool Tab(Rect r, string label, bool selected, int size = 22)
    {
        Event e = Event.current;
        float h = Hover(r, Over(r));
        Color c = selected ? Text : Color.Lerp(Muted, Text2, h);
        Label(r, label, Head, size, c, TextAnchor.MiddleCenter);
        if (selected) Fill(new Rect(r.x + 6f, r.yMax - 3f, r.width - 12f, 3f), Yellow);
        if (e.type == EventType.MouseDown && e.button == 0 && r.Contains(e.mousePosition)) { e.Use(); Sfx.UI("hit", 0.4f, 0.9f); return true; }
        return false;
    }

    // Titulo de tela: nome grande + traco amarelo curto + subtitulo cinza.
    public static void Title(float x, float y, string text, string sub)
    {
        Label(new Rect(x, y, 1100f, 64f), text, HeadBold, 56, Text, TextAnchor.MiddleLeft);
        Fill(new Rect(x + 2f, y + 70f, 36f, 3f), Yellow);
        if (sub != null) Label(new Rect(x + 50f, y + 58f, 1000f, 28f), sub, Body, 18, Muted, TextAnchor.MiddleLeft);
    }

    // Rotulo pequeno de secao (caixa alta, cinza).
    public static void Caption(float x, float y, float w, string text)
    {
        Label(new Rect(x, y, w, 22f), text, Body, 15, Muted, TextAnchor.MiddleLeft);
    }
}
