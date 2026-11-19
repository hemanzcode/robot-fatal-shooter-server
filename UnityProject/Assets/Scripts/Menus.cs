using UnityEngine;

// Todos os menus do jogo, desenhados com IMGUI: principal, partida, opcoes, controles,
// creditos, pausa e fim de partida.
public class Menus : MonoBehaviour
{
    public enum Page { None, Main, Setup, Options, Controls, Credits, Pause, End, Agents, Login, Online, Hud }
    string fEmail = "", fUser = "", fPass = "", fCode = "";
    public Page page = Page.None;
    Page backTo = Page.Main;

    public bool IsOpen { get { return page != Page.None; } }

    static readonly Color Accent = new Color(1f, 0.86f, 0.1f);
    static readonly Color Panel = new Color(0.02f, 0.025f, 0.04f, 0.9f);

    public void Open(Page p)
    {
        page = p;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        if (p == Page.Pause && !NetGame.Online) Time.timeScale = 0f;   // online o jogo nao para
    }

    // Editor dos controles de toque (desenhado pelo TouchPad); ao concluir volta para as opcoes.
    public void EndHudEdit() { page = Page.Options; }

    public void Close()
    {
        if (page == Page.Pause) Settings.Save();   // guarda voz/volume ajustados na pausa
        page = Page.None;
        Time.timeScale = 1f;
        if (Game.I != null && Game.I.state != Game.State.Menu)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }

    public void TogglePause()
    {
        if (page == Page.None) Open(Page.Pause);
        else if (page == Page.Pause) Close();
        else if (page == Page.Options || page == Page.Controls) Back();
    }

    void Back()
    {
        if (page == Page.Options) { Settings.Save(); Settings.Apply(); }
        page = backTo;
    }

    void Goto(Page p, Page from)
    {
        backTo = from;
        page = p;
    }

    // ------------------------------- Componentes -------------------------------

    bool Button(float x, float y, float w, float h, string label, bool primary = false)
    {
        Rect r = new Rect(x, y, w, h);
        Event e = Event.current;
        bool hover = r.Contains(e.mousePosition);
        Color bg = primary ? new Color(Accent.r, Accent.g, Accent.b, hover ? 1f : 0.88f)
                           : (hover ? new Color(1f, 1f, 1f, 0.16f) : new Color(1f, 1f, 1f, 0.06f));
        Game.Fill(x, y, w, h, bg);
        Game.Fill(x, y, 5f, h, hover || primary ? Accent : new Color(1f, 1f, 1f, 0.25f));
        Color tc = primary ? new Color(0.03f, 0.03f, 0.04f) : (hover ? Color.white : new Color(1f, 1f, 1f, 0.85f));
        // Texto sem sombra nos botoes amarelos para manter a leitura.
        if (primary)
        {
            GUIStyle st = new GUIStyle(GUI.skin.label);
            st.fontStyle = FontStyle.Bold;
            st.fontSize = 24;
            st.alignment = TextAnchor.MiddleLeft;
            st.normal.textColor = tc;
            GUI.Label(new Rect(x + 24f, y, w - 30f, h), label, st);
        }
        else Game.Text(x + 24f, y, w - 30f, h, label, 24, tc, TextAnchor.MiddleLeft);

        if (e.type == EventType.MouseDown && e.button == 0 && hover)
        {
            e.Use();
            Sfx.UI("hit", 0.45f, 0.8f);
            return true;
        }
        return false;
    }

    float Slider(float x, float y, float w, string label, float value, float min, float max, string format)
    {
        Game.Text(x, y, 360f, 40f, label, 22, new Color(1f, 1f, 1f, 0.85f), TextAnchor.MiddleLeft);
        float sx = x + 370f, sw = w - 370f - 110f;
        Rect track = new Rect(sx, y + 6f, sw, 28f);
        Event e = Event.current;
        if ((e.type == EventType.MouseDown || e.type == EventType.MouseDrag) && e.button == 0 && track.Contains(e.mousePosition))
        {
            value = Mathf.Lerp(min, max, Mathf.Clamp01((e.mousePosition.x - sx) / sw));
            e.Use();
        }
        float k = Mathf.InverseLerp(min, max, value);
        Game.Fill(sx, y + 17f, sw, 6f, new Color(1f, 1f, 1f, 0.18f));
        Game.Fill(sx, y + 17f, sw * k, 6f, Accent);
        Game.Fill(sx + sw * k - 5f, y + 8f, 10f, 24f, Color.white);
        Game.Text(x + w - 100f, y, 100f, 40f, value.ToString(format), 22, Color.white, TextAnchor.MiddleRight);
        return value;
    }

    // Seletor com setas: devolve -1, 0 ou +1.
    int Stepper(float x, float y, float w, string label, string value)
    {
        Game.Text(x, y, 360f, 44f, label, 22, new Color(1f, 1f, 1f, 0.85f), TextAnchor.MiddleLeft);
        float sx = x + 370f, sw = w - 370f;
        int delta = 0;
        if (Button(sx, y, 54f, 44f, "<")) delta = -1;
        Game.Fill(sx + 60f, y, sw - 120f, 44f, new Color(1f, 1f, 1f, 0.06f));
        Game.Text(sx + 60f, y, sw - 120f, 44f, value, value.Length > 10 ? 16 : value.Length > 8 ? 18 : 22, Color.white, TextAnchor.MiddleCenter);
        if (Button(sx + sw - 54f, y, 54f, 44f, ">")) delta = 1;
        return delta;
    }

    // Escolha de time: BALL (azul), MOLOCH (vermelho) ou automatico.
    void TeamPicker(float x, float y, float w, string label)
    {
        Game.Text(x, y, 360f, 44f, label, 22, new Color(1f, 1f, 1f, 0.85f), TextAnchor.MiddleLeft);
        // Rotulo curto ("SEU TIME"): sobra mais largura para os 3 botoes.
        float sx = x + (w >= 600f ? 190f : 0f), sw = w - (w >= 600f ? 190f : 0f);
        if (w < 600f) { y += 48f; }
        string[] names = { "BALL", "MOLOCH", "AUTO" };
        float bw = (sw - 12f) / 3f;
        for (int i = 0; i < 3; i++)
        {
            float bx = sx + i * (bw + 6f);
            bool sel = Settings.side == i;
            if (Button(bx, y, bw, 44f, names[i], sel)) { Settings.side = i; Settings.Save(); Sfx.UI("pickup", 0.5f); }
            Color c = i == 2 ? new Color(1f, 1f, 1f, 0.6f) : Art.TeamColor(i);
            Game.Fill(bx, y + 40f, bw, sel ? 4f : 2f, sel ? c : new Color(c.r, c.g, c.b, 0.35f));
        }
    }

    bool Toggle(float x, float y, float w, string label, bool value)
    {
        if (Stepper(x, y, w, label, value ? "LIGADO" : "DESLIGADO") != 0) return !value;
        return value;
    }

    void Title(float x, float y, string text, string sub)
    {
        Game.Fill(x, y + 8f, 8f, 60f, Accent);
        Game.Text(x + 26f, y, 1200f, 76f, text, 60, Color.white, TextAnchor.MiddleLeft);
        if (sub != null) Game.Text(x + 28f, y + 74f, 1200f, 32f, sub, 20, new Color(1f, 1f, 1f, 0.6f), TextAnchor.MiddleLeft);
    }

    // ------------------------------- Paginas -------------------------------

    void Awake() { useGUILayout = false; }

    void OnGUI()
    {
        if (page == Page.None || page == Page.Hud || Game.I == null) return;
        GUI.depth = -10;
        Game.EnsureStyle();
        float s = Screen.height / 1080f;
        GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));
        float vw = Screen.width / s, vh = 1080f;
        float cx = vw / 2f, cy = vh / 2f;
        bool inGame = Game.I.state != Game.State.Menu;

        // Fora da partida: o video promocional cobre a tela inteira (cidade e agente ficam escondidos atras).
        Texture vid = inGame || page == Page.Agents ? null : MenuVideo.Tex;   // em AGENTES o modelo 3D precisa aparecer
        if (vid != null)
        {
            Color keep = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, MenuVideo.Alpha);
            GUI.DrawTexture(new Rect(0f, 0f, vw, vh), vid, ScaleMode.ScaleAndCrop, false);
            GUI.color = keep;
        }
        // Fundo: faixa escura a esquerda (ou escurece o jogo pausado).
        Game.Fill(0f, 0f, vw, vh, new Color(0f, 0f, 0f, inGame ? 0.62f : (vid != null ? 0.12f : 0.3f)));
        if (page != Page.End) Game.Fill(0f, 0f, page == Page.Agents ? 700f : 760f, vh, Panel);

        float x = 90f;
        switch (page)
        {
            case Page.Main: DrawMain(x, vh); break;
            case Page.Setup: DrawSetup(x); break;
            case Page.Options: DrawOptions(x); break;
            case Page.Controls: DrawControls(x); break;
            case Page.Credits: DrawCredits(x); break;
            case Page.Pause: DrawPause(x); break;
            case Page.End: DrawEnd(cx, cy, vw, vh); break;
            case Page.Agents: DrawAgents(x, vw, vh); break;
            case Page.Login: DrawLogin(x); break;
            case Page.Online: DrawOnline(x); break;
        }
    }

    void DrawMain(float x, float vh)
    {
        Game.Text(x, 96f, 700f, 110f, "ROBOT FATAL SHOOTER", 48, Color.white, TextAnchor.MiddleLeft);
        Game.Fill(x, 208f, 250f, 6f, Accent);
        Game.Text(x, 226f, 700f, 34f, "FPS TATICO  //  SETOR ARIDO, 14:20", 20, new Color(1f, 1f, 1f, 0.6f), TextAnchor.MiddleLeft);

        float y = 290f, w = 520f, h = 58f, gap = 66f;
        if (Button(x, y, w, h, "JOGAR ONLINE", true))
        {
            Net n = Net.Ensure();
            if (n.loggedIn) { n.Connect(); Goto(Page.Online, Page.Main); } else Goto(Page.Login, Page.Main);
        }
        if (Button(x, y + gap, w, h, "TREINO CONTRA BOTS")) Goto(Page.Setup, Page.Main);
        if (Button(x, y + gap * 2f, w, h, "AGENTES")) Goto(Page.Agents, Page.Main);
        if (Button(x, y + gap * 3f, w, h, "TUTORIAL")) Game.I.StartMatch(true);
        if (Button(x, y + gap * 4f, w, h, "OPCOES")) Goto(Page.Options, Page.Main);
        if (Button(x, y + gap * 5f, w, h, "CONTROLES")) Goto(Page.Controls, Page.Main);
        if (Button(x, y + gap * 6f, w, h, "CREDITOS")) Goto(Page.Credits, Page.Main);
        if (Button(x, y + gap * 7f, w, h, "SAIR")) Quit();
        Game.Text(x, vh - 70f, 600f, 30f, "v0.3  -  prototipo", 16, new Color(1f, 1f, 1f, 0.4f), TextAnchor.MiddleLeft);
        if (MenuVideo.Tex == null) AgentCard(Screen.width / (Screen.height / 1080f), vh);   // com o video, so o menu
    }

    // Ficha do agente selecionado, no canto inferior direito (o modelo 3D fica ao lado, na cena).
    void AgentCard(float vw, float vh)
    {
        AgentDef d = AgentBody.Defs[Settings.agent % AgentBody.Defs.Length];
        float w = 540f, h = 290f, x = vw - w - 45f, y = vh - h - 36f;
        Game.Fill(x, y, w, h, new Color(0.02f, 0.025f, 0.04f, 0.86f));
        Game.Fill(x, y, 6f, h, d.accent);
        Texture2D portrait = Resources.Load<Texture2D>("Agents/Portraits/" + d.portrait);
        if (portrait != null) GUI.DrawTexture(new Rect(x + 16f, y + 14f, 132f, h - 28f), portrait, ScaleMode.ScaleToFit, true);
        float tx = x + 166f, tw = w - 180f;
        Game.Text(tx, y + 10f, tw, 24f, "AGENTE SELECIONADO", 14, new Color(1f, 1f, 1f, 0.5f), TextAnchor.MiddleLeft);
        Game.Text(tx, y + 32f, tw, 48f, d.name, 36, Color.white, TextAnchor.MiddleLeft);
        Game.Text(tx, y + 78f, tw, 26f, d.role.ToUpperInvariant(), 16, d.accent, TextAnchor.MiddleLeft);
        Game.Text(tx, y + 106f, tw, 42f, d.blurb, 15, new Color(1f, 1f, 1f, 0.75f), TextAnchor.MiddleLeft);
        Game.Text(tx, y + 142f, tw, 34f, "Q  " + d.skill, 14, d.accent, TextAnchor.MiddleLeft);
        if (AgentAbilities.HasSecondarySkill(Settings.agent)) Game.Text(tx, y + 177f, tw, 34f, "C  " + d.skill2, 14, d.accent, TextAnchor.MiddleLeft);
        Game.Text(tx, y + 218f, tw, 34f, "X  " + d.ultimate, 14, Art.Yellow, TextAnchor.MiddleLeft);
    }

    void DrawAgents(float x, float vw, float vh)
    {
        Title(x - 30f, 60f, "AGENTES", "Escolha quem voce leva para a partida");
        float bw = 290f, bh = 92f, gx = 300f, gy = 102f, y0 = 200f;
        for (int i = 0; i < AgentBody.Defs.Length; i++)
        {
            AgentDef d = AgentBody.Defs[i];
            float bx = x - 30f + (i % 2) * gx, by = y0 + (i / 2) * gy;
            bool sel = i == Settings.agent;
            if (Button(bx, by, bw, bh, "", sel)) { Settings.agent = i; Settings.Save(); Sfx.UI("pickup", 0.5f); }
            Game.Fill(bx, by, 6f, bh, d.accent);
            Game.Text(bx + 18f, by + 10f, bw - 26f, 40f, d.name, 24, sel ? Color.black : Color.white, TextAnchor.MiddleLeft);
            Game.Text(bx + 18f, by + 50f, bw - 26f, 28f, d.role, 14, sel ? new Color(0f, 0f, 0f, 0.75f) : new Color(1f, 1f, 1f, 0.6f), TextAnchor.MiddleLeft);
        }
        if (Button(x - 30f, y0 + 6f * gy + 10f, 590f, 58f, "CONFIRMAR", true)) { Settings.Save(); page = Page.Main; }
        Game.Text(x - 30f, y0 + 6f * gy + 78f, 600f, 26f, "Q/C: utilitarias    E: dash    X: ultimate com 100% de carga", 14, new Color(1f, 1f, 1f, 0.6f), TextAnchor.MiddleLeft);
        AgentCard(vw, vh);
    }

    void DrawSetup(float x)
    {
        Title(x, 110f, "PARTIDA", Settings.mode == 1 ? "Bomba: ataque planta em A ou B, defesa desarma (5 rodadas)" : "Mata-mata em equipe contra bots");
        float y = 300f, w = 620f, gap = 66f;
        int d = Stepper(x, y - gap, w, "MODO", Settings.ModeNames[Settings.mode]);
        if (d != 0) Settings.mode = (Settings.mode + d + 2) % 2;
        d = Stepper(x, y, w, "DIFICULDADE", Settings.DifficultyNames[Settings.difficulty]);
        if (d != 0) Settings.difficulty = (Settings.difficulty + d + 3) % 3;
        d = Stepper(x, y + gap, w, "JOGADORES POR TIME", Settings.teamSize + " x " + Settings.teamSize);
        if (d != 0) Settings.teamSize = Mathf.Clamp(Settings.teamSize + d, 1, 5);
        d = Stepper(x, y + gap * 2f, w, "ABATES PARA VENCER", Settings.scoreLimit.ToString());
        if (d != 0) Settings.scoreLimit = Mathf.Clamp(Settings.scoreLimit + d * 5, 10, 50);
        d = Stepper(x, y + gap * 3f, w, "DURACAO", Settings.minutes + " MIN");
        if (d != 0) Settings.minutes = Mathf.Clamp(Settings.minutes + d, 3, 15);
        TeamPicker(x, y + gap * 4f, w, "SEU TIME");
        y += gap;

        if (Button(x, y + gap * 4f + 40f, 520f, 62f, "INICIAR PARTIDA", true))
        {
            Settings.Save();
            Game.I.StartMatch(false);
        }
        if (Button(x, y + gap * 4f + 114f, 520f, 62f, "VOLTAR")) { Settings.Save(); page = Page.Main; }
    }

    void DrawOptions(float x)
    {
        Title(x, 70f, "OPCOES", null);
        float y = 190f, w = 620f, gap = 58f;
        Game.Text(x, y, 400f, 30f, "CONTROLE", 16, Accent, TextAnchor.MiddleLeft);
        Settings.sensitivity = Slider(x, y + 34f, w, TouchPad.On ? "SENSIBILIDADE DA MIRA" : "SENSIBILIDADE DO MOUSE", Settings.sensitivity, 0.3f, 6f, "0.0");
        Settings.fov = Mathf.Round(Slider(x, y + 34f + gap, w, "CAMPO DE VISAO", Settings.fov, 70f, 110f, "0"));
        Settings.invertY = Toggle(x, y + 34f + gap * 2f, w, "INVERTER EIXO Y", Settings.invertY);

        float y2 = y + 34f + gap * 3f + 14f;
        Game.Text(x, y2, 400f, 30f, "AUDIO", 16, Accent, TextAnchor.MiddleLeft);
        Settings.master = Slider(x, y2 + 34f, w, "VOLUME GERAL", Settings.master, 0f, 1f, "0%");
        Settings.music = Slider(x, y2 + 34f + gap, w, "MUSICA", Settings.music, 0f, 1f, "0%");
        Settings.sfx = Slider(x, y2 + 34f + gap * 2f, w, "EFEITOS", Settings.sfx, 0f, 1f, "0%");
        AudioListener.volume = Settings.master;

        float y3 = y2 + 34f + gap * 3f + 14f;
        Game.Text(x, y3, 400f, 30f, "VIDEO", 16, Accent, TextAnchor.MiddleLeft);
        if (TouchPad.On)
        {
            // Celular: nitidez aplicada na hora; NITIDO pesa mais em celulares simples.
            int g = Stepper(x, y3 + 34f, w, "GRAFICOS", Settings.GfxNames[Settings.gfx]);
            if (g != 0) { Settings.gfx = (Settings.gfx + g + 3) % 3; Settings.ApplyGfx(); }
            string hint = Settings.gfx == 0 ? "Mais leve, imagem mais borrada." : Settings.gfx == 1 ? "Imagem limpa com bom desempenho." : "Maxima nitidez (pede um celular mais forte).";
            Game.Text(x, y3 + 34f + 46f, w, 24f, hint, 14, new Color(1f, 1f, 1f, 0.55f), TextAnchor.MiddleLeft);
            if (Button(x, y3 + 34f + gap + 20f, w, 52f, "PERSONALIZAR CONTROLES DE TOQUE")) { page = Page.Hud; TouchPad.BeginEdit(); }
        }
        else
        {
            string[] names = QualitySettings.names;
            int q = Mathf.Clamp(Settings.quality, 0, Mathf.Max(0, names.Length - 1));
            int d = Stepper(x, y3 + 34f, w, "QUALIDADE", names.Length > 0 ? names[q].ToUpper() : "-");
            if (d != 0 && names.Length > 0)
            {
                Settings.quality = (q + d + names.Length) % names.Length;
                Settings.Apply();
            }
            Settings.bloom = Toggle(x, y3 + 34f + gap, w, "BRILHO NEON (BLOOM)", Settings.bloom);
        }
        bool fs = Toggle(x, y3 + 34f + gap * 3f, w, "TELA CHEIA", Screen.fullScreen);
        if (fs != Screen.fullScreen) Screen.fullScreen = fs;

        if (Button(x, y3 + 34f + gap * 4f + 16f, 520f, 58f, "SALVAR E VOLTAR", true)) Back();
    }

    void DrawControls(float x)
    {
        Title(x, 90f, "CONTROLES", null);
        string[,] rows =
        {
            { "W A S D", "Mover" },
            { "MOUSE", "Olhar" },
            { "SHIFT", "Correr" },
            { "ESPACO", "Pular (de novo no ar: pulo duplo)" },
            { "MOUSE ESQ.", "Atirar" },
            { "MOUSE DIR.", "Mirar" },
            { "R", "Recarregar" },
            { "1  2  3  4  /  SCROLL", "Trocar de arma" },
            { "F", "Veiculo / caixa / bomba (segurar)" },
            { "ESPACO (no ar)", "Jetpack, se tiver pego" },
            { "SHIFT na luneta", "Prender o folego" },
            { "E", "Dash" },
            { "Q", "Habilidade / granada" },
            { "C", "Utilidade secundaria (quando disponivel)" },
            { "X", "Sobrecarga (carrega com abates)" },
            { "CTRL", "Agachar (tiro mais preciso)" },
            { "V  (SEGURAR)", "Falar com o time (salas online)" },
            { "Z  /  BOTAO DO MEIO", "Marcar para o time (inimigo, vida...)" },
            { "TAB", "Placar" },
            { "ESC", "Pausar" }
        };
        float y = 220f;
        for (int i = 0; i < rows.GetLength(0); i++)
        {
            Game.Fill(x, y + i * 42f, 620f, 38f, new Color(1f, 1f, 1f, i % 2 == 0 ? 0.05f : 0.02f));
            Game.Text(x + 14f, y + i * 42f, 260f, 38f, rows[i, 0], 19, Accent, TextAnchor.MiddleLeft);
            Game.Text(x + 280f, y + i * 42f, 340f, 38f, rows[i, 1], 19, Color.white, TextAnchor.MiddleLeft);
        }
        if (Button(x, y + rows.GetLength(0) * 42f + 30f, 520f, 58f, "VOLTAR")) Back();
    }

    void DrawCredits(float x)
    {
        Title(x, 110f, "CREDITOS", null);
        string[] lines =
        {
            "ROBOT FATAL SHOOTER",
            "",
            "Direcao e ideia: Igor Melo",
            "Programacao, arte procedural, sons e trilha: Claude",
            "",
            "Feito em Unity. Toda a arte, os sons e a musica",
            "sao gerados por codigo, sem arquivos importados.",
            "",
            "Obrigado por jogar."
        };
        for (int i = 0; i < lines.Length; i++)
            Game.Text(x, 260f + i * 40f, 640f, 36f, lines[i], i == 0 ? 28 : 21, i == 0 ? Accent : Color.white, TextAnchor.MiddleLeft);
        if (Button(x, 260f + lines.Length * 40f + 50f, 520f, 58f, "VOLTAR")) page = Page.Main;
    }

    // ------------------------------- Online -------------------------------

    static GUIStyle fieldStyle;

    string Field(float x, float y, float w, string label, string value, bool password, int max)
    {
        if (fieldStyle == null)
        {
            fieldStyle = new GUIStyle(GUI.skin.textField);
            fieldStyle.fontSize = 22;
            fieldStyle.alignment = TextAnchor.MiddleLeft;
            fieldStyle.padding = new RectOffset(12, 12, 4, 4);
        }
        Game.Text(x, y, 220f, 46f, label, 20, new Color(1f, 1f, 1f, 0.8f), TextAnchor.MiddleLeft);
        Rect r = new Rect(x + 230f, y, w - 230f, 46f);
        if (TouchPad.On)
        {
            // No celular o teclado nao abre nos campos do jogo: toca e digita na caixa do navegador.
            string shown = password ? new string('*', value.Length) : value;
            Game.Fill(r.x, r.y, r.width, r.height, new Color(1f, 1f, 1f, 0.12f));
            Game.Text(r.x + 12f, r.y, r.width - 20f, r.height, shown == "" ? "toque para digitar" : shown, 20, shown == "" ? new Color(1f, 1f, 1f, 0.4f) : Color.white, TextAnchor.MiddleLeft);
            Event e = Event.current;
            if (e.type == EventType.MouseDown && r.Contains(e.mousePosition))
            {
                e.Use();
                string v = TouchPad.Prompt(label, password ? "" : value) ?? value;
                return v.Length > max ? v.Substring(0, max) : v;
            }
            return value;
        }
        return password ? GUI.PasswordField(r, value, '*', max, fieldStyle) : GUI.TextField(r, value, max, fieldStyle);
    }

    void NetStatus(float x, float y)
    {
        Net n = Net.I;
        if (n == null) return;
        if (n.error != "") Game.Text(x, y, 900f, 30f, n.error, 18, new Color(1f, 0.45f, 0.4f), TextAnchor.MiddleLeft);
        else if (n.status != "") Game.Text(x, y, 900f, 30f, n.status, 18, Accent, TextAnchor.MiddleLeft);
    }

    void DrawLogin(float x)
    {
        Net n = Net.Ensure();
        Title(x, 110f, "CONTA", "Entre ou crie sua conta para jogar online");
        if (!Net.Configured)
        {
            Game.Text(x, 300f, 700f, 40f, "O servidor online ainda nao foi configurado nesta versao.", 20, Color.white, TextAnchor.MiddleLeft);
            if (Button(x, 380f, 520f, 58f, "VOLTAR")) page = Page.Main;
            return;
        }
        if (n.loggedIn) { n.Connect(); page = Page.Online; return; }
        float y = 290f, w = 640f;
        if (Net.InviteCode != "")
            Game.Text(x, 220f, 800f, 36f, "Voce foi convidado para a sala " + Net.InviteCode + ". Entre na conta para jogar.", 20, Accent, TextAnchor.MiddleLeft);
        fEmail = Field(x, y, w, "E-MAIL", fEmail, false, 80);
        fPass = Field(x, y + 62f, w, "SENHA", fPass, true, 60);
        fUser = Field(x, y + 124f, w, "NOME NO JOGO", fUser, false, 16);
        Game.Text(x, y + 174f, 700f, 26f, "O nome no jogo so e usado ao criar a conta.", 14, new Color(1f, 1f, 1f, 0.5f), TextAnchor.MiddleLeft);
        if (Button(x, y + 220f, 310f, 58f, "ENTRAR", true)) n.Login(fEmail, fPass);
        if (Button(x + 330f, y + 220f, 310f, 58f, "CRIAR CONTA")) n.SignUp(fEmail, fUser, fPass);
        NetStatus(x, y + 296f);
        if (Button(x, y + 350f, 310f, 58f, "VOLTAR")) page = Page.Main;
    }

    bool newPublic = false, newBots = true;

    void DrawOnline(float x)
    {
        Net n = Net.Ensure();
        if (!n.loggedIn) { page = Page.Login; return; }
        Title(x, 60f, "ONLINE", n.userName + "   //   " + (n.connected ? n.online + " jogador(es) conectado(s)" : "conectando..."));
        float y = 180f, w = 640f;
        TeamPicker(x, y, w, "SEU TIME");
        y += 62f;
        if (n.connected)
        {
            Game.Text(x, y, 600f, 30f, "JOGO RAPIDO  -  5 x 5, bots completam os times", 17, Accent, TextAnchor.MiddleLeft);
            y += 36f;
            for (int i = 0; i < n.rows.Length; i++)
            {
                NetRow r = n.rows[i];
                string label = r.map + "  -  " + r.mode + (r.players > 0 ? "   (" + r.players + " jogando)" : "");
                if (Button(x, y, w, 52f, label, i == 0)) n.Quick(r.map, r.mode);
                y += 58f;
            }
            y += 14f;
            Game.Text(x, y, 600f, 30f, "CRIAR SALA", 17, Accent, TextAnchor.MiddleLeft);
            y += 36f;
            // Publica aparece na lista de salas; privada so entra com o codigo.
            if (Button(x, y, 315f, 46f, "PUBLICA (NA LISTA)", newPublic)) newPublic = true;
            if (Button(x + 325f, y, 315f, 46f, "PRIVADA (SO CODIGO)", !newPublic)) newPublic = false;
            y += 52f;
            if (Button(x, y, 315f, 46f, "COM BOTS", newBots)) newBots = true;
            if (Button(x + 325f, y, 315f, 46f, "SO OS AMIGOS (SEM BOTS)", !newBots)) newBots = false;
            y += 56f;
            if (Button(x, y, 315f, 54f, "CRIAR: MATA-MATA", true)) n.Create("VILA ARIDA", "MATA-MATA", !newPublic, newBots);
            if (Button(x + 325f, y, 315f, 54f, "CRIAR: BOMBA", true)) n.Create("VILA ARIDA", "BOMBA", !newPublic, newBots);
            y += 66f;
            fCode = Field(x, y + 5f, 400f, "CODIGO DA SALA", fCode, false, 5).ToUpperInvariant();
            if (Button(x + 415f, y, 225f, 54f, "ENTRAR")) n.Join(fCode);
            y += 66f;

            DrawRoomList(n, x + 740f);
        }
        else
        {
            if (Button(x, y, w, 56f, "TENTAR CONECTAR DE NOVO")) n.Connect();
            y += 70f;
        }
        NetStatus(x, y);
        y += 36f;
        if (Button(x, y, 315f, 54f, "VOLTAR")) page = Page.Main;
        if (Button(x + 325f, y, 315f, 54f, "SAIR DA CONTA")) { n.Logout(); page = Page.Login; }
    }

    // Lista das salas publicas abertas, a direita: clique para entrar.
    void DrawRoomList(Net n, float x)
    {
        float w = 600f, y = 180f;
        Game.Fill(x - 24f, y - 20f, w + 48f, 800f, Panel);
        Game.Text(x, y, w, 34f, "SALAS ABERTAS", 24, Color.white, TextAnchor.MiddleLeft);
        Game.Text(x, y + 34f, w, 24f, "Atualiza sozinha. Salas privadas so com o codigo.", 14, new Color(1f, 1f, 1f, 0.45f), TextAnchor.MiddleLeft);
        y += 74f;
        if (n.open.Length == 0)
        {
            Game.Text(x, y, w, 40f, "Nenhuma sala aberta agora. Crie uma!", 18, new Color(1f, 1f, 1f, 0.6f), TextAnchor.MiddleLeft);
            return;
        }
        int shown = Mathf.Min(n.open.Length, 10);
        for (int i = 0; i < shown; i++)
        {
            NetOpen r = n.open[i];
            bool full = r.players >= 10;
            Game.Fill(x, y, w, 62f, new Color(1f, 1f, 1f, 0.05f));
            Game.Text(x + 14f, y + 4f, w - 200f, 30f, r.mode + "  -  " + r.map, 19, Color.white, TextAnchor.MiddleLeft);
            string sub = "Sala de " + r.host + "   //   " + r.players + "/10   //   " + (r.bots ? "com bots" : "sem bots") + "   //   " + r.code;
            Game.Text(x + 14f, y + 32f, w - 200f, 24f, sub, 14, new Color(1f, 1f, 1f, 0.55f), TextAnchor.MiddleLeft);
            if (full) Game.Text(x + w - 170f, y, 160f, 62f, "CHEIA", 18, new Color(1f, 1f, 1f, 0.4f), TextAnchor.MiddleCenter);
            else if (Button(x + w - 170f, y + 8f, 160f, 46f, "ENTRAR", true)) n.Join(r.code);
            y += 70f;
        }
    }

    void DrawPause(float x)
    {
        if (NetGame.Online)
        {
            Title(x, 150f, "MENU", "Sala " + Net.I.roomCode + "  -  " + Net.I.roomMode + "  (a partida continua rodando)");
            if (Button(x, 330f, 520f, 62f, "CONTINUAR", true)) Close();
            if (Button(x, 404f, 520f, 62f, "CONVIDAR AMIGOS (LINK DA SALA)", true)) Net.I.ShareInvite();
            if (Button(x, 478f, 520f, 62f, "OPCOES")) Goto(Page.Options, Page.Pause);
            if (Button(x, 552f, 520f, 62f, "CONTROLES")) Goto(Page.Controls, Page.Pause);
            if (Button(x, 626f, 520f, 62f, "SAIR DA PARTIDA")) Game.I.ToMenu();
            bool copied = Time.unscaledTime - Net.copiedAt < 4f;
            Game.Text(x, 700f, 900f, 28f, copied ? (TouchPad.On ? "Escolha onde mandar o convite." : "Link copiado! Cole no WhatsApp/Discord.") : "Quem abrir o link entra direto nesta sala:", 17, copied ? Accent : new Color(1f, 1f, 1f, 0.6f), TextAnchor.MiddleLeft);
            Game.Text(x, 730f, 900f, 28f, Net.I.InviteLink, 17, Color.white, TextAnchor.MiddleLeft);
            DrawVoicePanel(x + 600f);
            return;
        }
        Title(x, 150f, "PAUSADO", Game.I.state == Game.State.Tutorial ? "Tutorial" : "Mata-mata em equipe");
        float y = 330f, w = 520f, h = 62f, gap = 74f;
        if (Button(x, y, w, h, "CONTINUAR", true)) Close();
        if (Button(x, y + gap, w, h, "OPCOES")) Goto(Page.Options, Page.Pause);
        if (Button(x, y + gap * 2f, w, h, "CONTROLES")) Goto(Page.Controls, Page.Pause);
        if (Button(x, y + gap * 3f, w, h, "REINICIAR")) Game.I.StartMatch(Game.I.state == Game.State.Tutorial);
        if (Game.I.state == Game.State.Tutorial && Button(x + w + 20f, y + gap * 3f, 330f, h, "PULAR TUTORIAL"))
        {
            Settings.tutorialDone = true;
            Settings.Save();
            Game.I.ToMenu();
            return;
        }
        if (Button(x, y + gap * 4f, w, h, "MENU PRINCIPAL")) Game.I.ToMenu();
        if (Button(x, y + gap * 5f, w, h, "SAIR DO JOGO")) Quit();
    }

    // Voz do time: liga/desliga, volume e silenciar cada companheiro.
    void DrawVoicePanel(float x)
    {
        float y = 330f, w = 520f;
        Game.Text(x, y - 40f, w, 30f, "VOZ DO TIME  " + (TouchPad.On ? "(segure FALAR)" : "(segure V)"), 16, Accent, TextAnchor.MiddleLeft);
        Settings.voiceOn = Toggle(x, y, w, "VOZ LIGADA", Settings.voiceOn);
        Settings.voiceVol = Slider(x, y + 58f, w, "VOLUME DA VOZ", Settings.voiceVol, 0f, 1f, "0%");
        VoiceChat vc = VoiceChat.I;
        if (vc == null) return;
        float ry = y + 130f;
        foreach (NetPlayer p in vc.Mates())
        {
            bool m = vc.muted.Contains(p.id);
            if (Button(x, ry, w, 46f, (m ? "OUVIR  " : "SILENCIAR  ") + p.name + (p.away ? "  (fora)" : ""))) vc.ToggleMute(p.id);
            ry += 54f;
        }
        if (vc.Mates().Count == 0) Game.Text(x, ry, w, 26f, "Ninguem do seu time na sala ainda.", 15, new Color(1f, 1f, 1f, 0.55f), TextAnchor.MiddleLeft);
#if !UNITY_WEBGL || UNITY_EDITOR
        Game.Text(x, ry + 30f, w, 26f, "A voz funciona so na versao do navegador.", 15, new Color(1f, 1f, 1f, 0.55f), TextAnchor.MiddleLeft);
#endif
    }

    void DrawEnd(float cx, float cy, float vw, float vh)
    {
        Game g = Game.I;
        bool tut = g.state == Game.State.Tutorial;
        if (tut)
        {
            Game.Text(cx - 600f, 200f, 1200f, 100f, "TUTORIAL CONCLUIDO", 72, Accent, TextAnchor.MiddleCenter);
            Game.Text(cx - 600f, 310f, 1200f, 40f, "Voce ja sabe o basico. Hora de uma partida de verdade.", 24, Color.white, TextAnchor.MiddleCenter);
            if (Button(cx - 260f, 440f, 520f, 62f, "JOGAR UMA PARTIDA", true)) { g.ToMenu(); Goto(Page.Setup, Page.Main); }
            if (Button(cx - 260f, 514f, 520f, 62f, "REPETIR O TUTORIAL")) g.StartMatch(true);
            if (Button(cx - 260f, 588f, 520f, 62f, "MENU PRINCIPAL")) g.ToMenu();
            return;
        }

        bool tie = g.score[0] == g.score[1];
        int win = g.score[0] > g.score[1] ? 0 : 1;
        int team = g.player != null ? g.player.team : 0;
        string msg = tie ? "EMPATE" : (win == team ? "VITORIA" : "DERROTA");
        Game.Text(cx - 500f, 60f, 1000f, 90f, msg, 76, tie ? Color.white : Art.TeamColor(win), TextAnchor.MiddleCenter);
        Game.Text(cx - 500f, 150f, 1000f, 44f, "BALL  " + g.score[0] + "   x   " + g.score[1] + "  MOLOCH", 30, Color.white, TextAnchor.MiddleCenter);
        g.Scoreboard(cx, cy - 20f);
        if (NetGame.Online) { if (Button(cx - 260f, vh - 150f, 520f, 62f, "VOLTAR AO LOBBY", true)) g.ToMenu(); return; }
        if (Button(cx - 530f, vh - 150f, 520f, 62f, "JOGAR DE NOVO", true)) g.StartMatch(false);
        if (Button(cx + 10f, vh - 150f, 520f, 62f, "MENU PRINCIPAL")) g.ToMenu();
    }

#if UNITY_WEBGL && !UNITY_EDITOR
    [System.Runtime.InteropServices.DllImport("__Internal")] static extern void RfsQuit();
#endif

    void Quit()
    {
        Settings.Save();
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#elif UNITY_WEBGL
        AudioListener.pause = true;
        RfsQuit();
#else
        Application.Quit();
#endif
    }
}
