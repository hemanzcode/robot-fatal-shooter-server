using UnityEngine;
using System.Collections.Generic;

// Todos os menus do jogo, desenhados com IMGUI: principal, partida, opcoes, controles,
// creditos, pausa e fim de partida.
public class Menus : MonoBehaviour
{
    public enum Page { None, Main, Setup, Options, Controls, Credits, Pause, End, Agents, Login, Online, Hud, Shop }
    string fEmail = "", fUser = "", fPass = "", fCode = "", fFriend = "";
    bool friendsTab; int friendScroll; string removeAsk = "";
    public Page page = Page.None;
    // O video do menu cobre a tela inteira (opaco): ai a camera do menu nao precisa desenhar o mapa por tras.
    public bool VideoCovers
    {
        get
        {
            if (Game.I == null || Game.I.state != Game.State.Menu || page == Page.None || page == Page.Agents || page == Page.Shop) return false;
            return MenuVideo.Tex != null && MenuVideo.Alpha >= 1f;
        }
    }
    Page backTo = Page.Main;

    public bool IsOpen { get { return page != Page.None; } }

    static readonly Color Accent = UI.Yellow;                 // identidade nova: amarelo so em detalhes
    static readonly Color Panel = UI.A(UI.Bg, 0.9f);

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

    bool Button(float x, float y, float w, float h, string label, bool primary = false, int size = 24)
    {
        bool small = w < 80f;   // setas dos seletores: texto centralizado
        return UI.Button(new Rect(x, y, w, h), label, primary ? UI.Kind.Primary : UI.Kind.Normal, size, true, null, null, small ? TextAnchor.MiddleCenter : TextAnchor.MiddleLeft);
    }

    float Slider(float x, float y, float w, string label, float value, float min, float max, string format)
    {
        Game.Text(x, y, 360f, 40f, label, 22, UI.Text2, TextAnchor.MiddleLeft);
        float sx = x + 370f, sw = w - 370f - 110f;
        Rect track = new Rect(sx, y + 6f, sw, 28f);
        Event e = Event.current;
        if ((e.type == EventType.MouseDown || e.type == EventType.MouseDrag) && e.button == 0 && track.Contains(e.mousePosition))
        {
            value = Mathf.Lerp(min, max, Mathf.Clamp01((e.mousePosition.x - sx) / sw));
            e.Use();
        }
        float k = Mathf.InverseLerp(min, max, value);
        Game.Fill(sx, y + 18f, sw, 4f, UI.Line);
        Game.Fill(sx, y + 18f, sw * k, 4f, UI.Text2);
        Game.Fill(sx + sw * k - 4f, y + 9f, 8f, 22f, UI.Yellow);
        Game.Text(x + w - 100f, y, 100f, 40f, value.ToString(format), 22, Color.white, TextAnchor.MiddleRight);
        return value;
    }

    // Seletor com setas: devolve -1, 0 ou +1.
    int Stepper(float x, float y, float w, string label, string value)
    {
        Game.Text(x, y, 360f, 44f, label, 22, UI.Text2, TextAnchor.MiddleLeft);
        float sx = x + 370f, sw = w - 370f;
        int delta = 0;
        if (Button(sx, y, 54f, 44f, "<")) delta = -1;
        UI.PanelBox(new Rect(sx + 60f, y, sw - 120f, 44f), 0.85f);
        Game.Text(sx + 60f, y, sw - 120f, 44f, value, value.Length > 10 ? 16 : value.Length > 8 ? 18 : 22, Color.white, TextAnchor.MiddleCenter);
        if (Button(sx + sw - 54f, y, 54f, 44f, ">")) delta = 1;
        return delta;
    }

    // Escolha de time: BAAL (azul), MOLOCH (vermelho) ou automatico.
    void TeamPicker(float x, float y, float w, string label)
    {
        Game.Text(x, y, 360f, 44f, label, 22, UI.Text2, TextAnchor.MiddleLeft);
        // Rotulo curto ("SEU TIME"): sobra mais largura para os 3 botoes.
        float sx = x + (w >= 600f ? 190f : 0f), sw = w - (w >= 600f ? 190f : 0f);
        if (w < 600f) { y += 48f; }
        string[] names = { "BAAL", "MOLOCH", "AUTO" };
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
        UI.Title(x, y + 4f, text, sub);
    }

    // ------------------------------- Paginas -------------------------------

    void Awake() { useGUILayout = false; }

    void OnGUI()
    {
        if (page == Page.None || page == Page.Hud || Game.I == null) return;
        GUI.depth = -10;
        Game.EnsureStyle();
        Game.textFont = UI.BodyBold; Game.textShadow = false;   // fonte e visual novos nos menus
        try { DrawPage(); } finally { Game.textFont = null; Game.textShadow = true; }
    }

    void DrawPage()
    {
        float s = Screen.height / 1080f;
        GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));
        float vw = Screen.width / s, vh = 1080f;
        screenW = vw;
        float cx = vw / 2f, cy = vh / 2f;
        bool inGame = Game.I.state != Game.State.Menu;

        // Identidade nova: atras do menu fica a cena do jogo com o agente (MenuBackdrop); o video nao e mais usado.
        Texture vid = null;
        if (vid != null)
        {
            Color keep = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, MenuVideo.Alpha);
            GUI.DrawTexture(new Rect(0f, 0f, vw, vh), vid, ScaleMode.ScaleAndCrop, false);
            GUI.color = keep;
        }
        // Fundo: faixa escura a esquerda (ou escurece o jogo pausado).
        if (inGame) Game.Fill(0f, 0f, vw, vh, new Color(0f, 0f, 0f, 0.62f));
        if (page != Page.End)
        {
            // Escuro a esquerda (leitura dos botoes) sumindo para a direita, onde fica o agente.
            float pw = page == Page.Agents ? 700f : 760f;
            if (page == Page.Main) pw = TouchPad.On ? Mathf.Min(1150f, vw * 0.52f) + 120f : 620f;
            UI.Fill(new Rect(0f, 0f, pw, vh), UI.A(UI.Bg, 0.93f));
            UI.GradientH(new Rect(pw, 0f, 520f, vh), UI.A(UI.Bg, 0.93f), UI.A(UI.Bg, 0f));
            if (!inGame) UI.GradientUp(new Rect(0f, vh - 300f, vw, 300f), UI.A(UI.Bg, 0.85f));
        }

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
            case Page.Shop: DrawShop(x, vw, vh); break;
        }
    }

    void DrawMain(float x, float vh)
    {
        bool phone = TouchPad.On;
        float vw = screenW, m = phone ? 70f : 85f;
        float k = phone ? 1.75f : 1f;   // no celular tudo maior (a tela fisica e pequena)

        // Marca
        UI.Label(new Rect(m, phone ? 34f : 44f, 900f, 70f * k), "FATAL SHOOTER", UI.HeadBold, Mathf.RoundToInt(54f * k), UI.Text, TextAnchor.MiddleLeft);
        float ly = (phone ? 34f : 44f) + 70f * k + 2f;
        UI.Fill(new Rect(m + 2f, ly, 37f * k, 3f * k), UI.Yellow);
        if (!phone) UI.Label(new Rect(m + 52f, ly - 13f, 600f, 28f), "FPS TÁTICO  ·  SETOR ÁRIDO", UI.Body, 16, UI.Muted, TextAnchor.MiddleLeft);

        // Perfil do jogador (canto superior direito)
        if (Skins.Server) ProfileChip(vw - m, phone ? 30f : 40f, phone);

        // Botoes: principal, segundo nivel, terceiro nivel
        Net n = null;
        if (!phone)
        {
            float y = 250f, w = 510f;
            if (UI.Button(new Rect(m, y, w, 88f), "JOGAR ONLINE", UI.Kind.Primary, 36, true, "Salas, partida rápida e amigos")) PlayOnline(ref n);
            y += 96f;
            if (UI.Button(new Rect(m, y, w, 70f), "CAMPANHA", UI.Kind.Normal, 29)) StartCampaignBtn();
            y += 78f;
            if (UI.Button(new Rect(m, y, w, 70f), "TREINO CONTRA BOTS", UI.Kind.Normal, 29)) Goto(Page.Setup, Page.Main);
            y += 84f;
            float hw = (w - 8f) / 2f;
            if (UI.Button(new Rect(m, y, hw, 62f), "AGENTES", UI.Kind.Normal, 26)) Goto(Page.Agents, Page.Main);
            if (UI.Button(new Rect(m + hw + 8f, y, hw, 62f), "LOJA", UI.Kind.Normal, 26)) { OpenShopNet(); Goto(Page.Shop, Page.Main); }
            y += 62f + 26f;
            UI.Fill(new Rect(m, y, w, 1f), UI.Line);
            y += 14f;
            string[] tert = { "TUTORIAL", "OPÇÕES", "CONTROLES", "CRÉDITOS", "SAIR" };
            for (int i = 0; i < tert.Length; i++)
                if (UI.Button(new Rect(m, y + i * 48f, 300f, 46f), tert[i], UI.Kind.Ghost, 22)) Tertiary(i);
            UI.Label(new Rect(m, vh - 56f, 600f, 28f), "v0.3", UI.Body, 15, UI.Dim, TextAnchor.MiddleLeft);
        }
        else
        {
            float y = 200f, w = Mathf.Min(1150f, vw * 0.52f);
            if (UI.Button(new Rect(m, y, w, 150f), "JOGAR ONLINE", UI.Kind.Primary, 62)) PlayOnline(ref n);
            y += 166f;
            float hw = (w - 16f) / 2f, bh = 128f;
            if (UI.Button(new Rect(m, y, hw, bh), "CAMPANHA", UI.Kind.Normal, 50)) StartCampaignBtn();
            if (UI.Button(new Rect(m + hw + 16f, y, hw, bh), "TREINO", UI.Kind.Normal, 50)) Goto(Page.Setup, Page.Main);
            y += bh + 16f;
            if (UI.Button(new Rect(m, y, hw, bh), "AGENTES", UI.Kind.Normal, 50)) Goto(Page.Agents, Page.Main);
            if (UI.Button(new Rect(m + hw + 16f, y, hw, bh), "LOJA", UI.Kind.Normal, 50)) { OpenShopNet(); Goto(Page.Shop, Page.Main); }
            y += bh + 32f;
            string[] tert = { "TUTORIAL", "OPÇÕES", "CONTROLES", "CRÉDITOS", "SAIR" };
            float tw = (w - 4f * 14f) / 5f;
            for (int i = 0; i < tert.Length; i++)
                if (UI.Button(new Rect(m + i * (tw + 14f), y, tw, 118f), tert[i], UI.Kind.Normal, 30, true, null, null, TextAnchor.MiddleCenter)) Tertiary(i);
        }

        // Agente em destaque (o modelo 3D fica na cena; aqui so o nome)
        AgentDef d = AgentBody.Defs[Settings.agent % AgentBody.Defs.Length];
        float bx = vw - m, by = vh - (phone ? 60f : 80f);
        if (!phone) UI.Label(new Rect(bx - 600f, by - 118f, 600f, 24f), "AGENTE SELECIONADO", UI.Body, 15, UI.Muted, TextAnchor.MiddleRight);
        UI.Label(new Rect(bx - 900f, by - 96f * (phone ? 1.4f : 1f), 900f, 70f * (phone ? 1.4f : 1f)), d.name.ToUpperInvariant(), UI.HeadBold, phone ? 84 : 60, UI.Text, TextAnchor.MiddleRight);
        string role = d.role.ToUpperInvariant();
        UI.Label(new Rect(bx - 900f, by - 28f, 900f, 32f * (phone ? 1.4f : 1f)), role, UI.Body, phone ? 30 : 19, UI.Text2, TextAnchor.MiddleRight);
    }

    void PlayOnline(ref Net n)
    {
        n = Net.Ensure();
        if (n.loggedIn) { n.Connect(); Goto(Page.Online, Page.Main); } else Goto(Page.Login, Page.Main);
    }

    void StartCampaignBtn()
    {
        Net cn = Net.Ensure();
        if (cn.loggedIn && !cn.connected) cn.Connect();
        Game.I.StartCampaign();
    }

    void Tertiary(int i)
    {
        if (i == 0) Game.I.StartMatch(true);
        else if (i == 1) Goto(Page.Options, Page.Main);
        else if (i == 2) Goto(Page.Controls, Page.Main);
        else if (i == 3) Goto(Page.Credits, Page.Main);
        else Quit();
    }

    // Bloco compacto do perfil: iniciais, nome, nivel/patente, barra de XP e SHOTS. right = borda direita.
    void ProfileChip(float right, float y, bool phone)
    {
        float k = phone ? 1.7f : 1f;
        float w = (phone ? 330f : 470f) * k * (phone ? 0.75f : 1f), h = 80f * k;
        float x = right - w;
        UI.PanelBox(new Rect(x, y, w, h), 0.85f);
        Net n = Net.I;
        string name = n != null && !string.IsNullOrEmpty(n.userName) ? n.userName.ToUpperInvariant() : "JOGADOR";
        float px = x + 16f * k;
        if (!phone)
        {
            Rect av = new Rect(px, y + 16f, 48f, 48f);
            UI.Fill(av, UI.Raised); UI.Border(av, UI.LineHi);
            UI.Label(av, name.Length > 1 ? name.Substring(0, 2) : name, UI.HeadBold, 22, UI.Text, TextAnchor.MiddleCenter);
            px += 64f;
        }
        float coinW = 120f * k;
        float iw = x + w - coinW - px - 16f * k;
        UI.Label(new Rect(px, y + 10f * k, iw, 30f * k), name, UI.Head, Mathf.RoundToInt(22f * k), UI.Text, TextAnchor.MiddleLeft);
        UI.Label(new Rect(px, y + 10f * k, iw, 30f * k), "NÍV. " + Skins.Level + (phone ? "" : "  ·  " + Skins.Rank.ToUpperInvariant()), UI.Body, Mathf.RoundToInt(13f * k), UI.Muted, TextAnchor.MiddleRight);
        float xp = Mathf.Clamp01(Skins.LevelXp / (float)Mathf.Max(1, Skins.LevelNeed));
        UI.Fill(new Rect(px, y + 46f * k, iw, 4f * k), UI.Line);
        UI.Fill(new Rect(px, y + 46f * k, iw * xp, 4f * k), UI.Text2);
        if (!phone) UI.Label(new Rect(px, y + 54f, iw, 20f), Skins.LevelXp + " / " + Skins.LevelNeed + " XP", UI.Body, 12, UI.Muted, TextAnchor.MiddleLeft);
        float dx = x + w - coinW - 6f * k;
        UI.Fill(new Rect(dx, y + 18f * k, 1f, h - 36f * k), UI.Line);
        UI.Label(new Rect(dx, y + 12f * k, coinW - 10f * k, 22f * k), Skins.Coin, UI.Body, Mathf.RoundToInt(12f * k), UI.Muted, TextAnchor.MiddleRight);
        string coins = Num(Skins.Coins);
        UI.Label(new Rect(dx, y + 32f * k, coinW - 10f * k, 34f * k), coins, UI.Head, Mathf.RoundToInt(26f * k), UI.Text, TextAnchor.MiddleRight);
        UI.Diamond(dx + coinW - 18f * k - coins.Length * 12f * k, y + 49f * k, 11f * k, UI.Yellow);
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

    float screenW = 1920f;
    static readonly Texture2D[] previews = new Texture2D[3];
    static readonly string[] previewFiles = { "mercado", "gelo", "casa" };
    static readonly string[] mapInfo =
    {
        "Vila no deserto, aberta: casas de dois andares, telhados, esgoto, fonte e tunel.\nCaixa laranja solta dinossauro e MECAS. Helicopteros e motos.",
        "Base congelada e fechada: corredores estreitos, portoes (F), subterraneo e tetos.\nNevoa a cada 40 s. Sem MECAS nem dinossauros.",
        "Casa em losango com piscina, quiosque e garagem com 4 carros. Mapa pequeno (3x3 ou 4x4).\nTiros e explosoes abrem buracos nas paredes."
    };

    // Cartao com a imagem do mapa escolhido, a direita do painel.
    void MapPreview(int map, float x, float y, float w)
    {
        if (previews[map] == null) previews[map] = Resources.Load<Texture2D>("MapPreviews/" + previewFiles[map]);
        float h = w * 9f / 16f;
        Game.Fill(x - 10f, y - 10f, w + 20f, h + 150f, new Color(0f, 0f, 0f, 0.72f));
        Game.Fill(x - 10f, y - 10f, w + 20f, 4f, Accent);
        if (previews[map] != null) GUI.DrawTexture(new Rect(x, y, w, h), previews[map], ScaleMode.ScaleAndCrop, false);
        else Game.Fill(x, y, w, h, new Color(0.15f, 0.15f, 0.15f, 1f));
        Game.Text(x, y + h + 12f, w, 50f, Arena.MapNames[map], 36, Color.white, TextAnchor.MiddleLeft);
        Game.Text(x, y + h + 62f, w, 70f, mapInfo[map], 18, new Color(1f, 1f, 1f, 0.75f), TextAnchor.UpperLeft);
    }

    // ------------------------------- Loja de skins -------------------------------

    int shopWeapon;
    string shopSel = "";
    string shopConfirm = null;
    Vector2 shopScroll;
    string shopMsg = ""; float shopMsgUntil;

    static string Num(int n)
    {
        string s = Mathf.Abs(n).ToString(), o = "";
        for (int i = 0; i < s.Length; i++) { if (i > 0 && (s.Length - i) % 3 == 0) o += "."; o += s[i]; }
        return (n < 0 ? "-" : "") + o;
    }

    // ---------- SHOTS (moeda), nivel e PIX ----------
    static Texture2D shotTex;
    public static void CoinIcon(float x, float y, float s)
    {
        if (shotTex == null) shotTex = Resources.Load<Texture2D>("UI/shot");
        if (shotTex != null) GUI.DrawTexture(new Rect(x, y, s, s), shotTex, ScaleMode.ScaleToFit, true);
    }
    static readonly Color Gold = new Color(0.95f, 0.76f, 0.35f);

    // Saldo + nivel/patente numa linha (menu principal).
    void Wallet(float x, float y)
    {
        CoinIcon(x, y, 40f);
        Game.Text(x + 48f, y, 220f, 40f, Num(Skins.Coins) + " " + Skins.Coin, 22, Gold, TextAnchor.MiddleLeft);
        Game.Text(x + 250f, y, 300f, 40f, "NIVEL " + Skins.Level + "  -  " + Skins.Rank, 18, Color.white, TextAnchor.MiddleLeft);
    }

    // Barra de XP do nivel atual.
    static void LevelBar(float x, float y, float w)
    {
        float k = Mathf.Clamp01(Skins.LevelXp / (float)Mathf.Max(1, Skins.LevelNeed));
        Game.Fill(x, y, w, 8f, new Color(1f, 1f, 1f, 0.12f));
        Game.Fill(x, y, w * k, 8f, Accent);
    }

    // Loja aberta: conecta ao servidor (se tiver login) para trazer saldo e skins.
    void OpenShopNet()
    {
        Net n = Net.Ensure();
        if (!n.loggedIn) return;
        if (!n.connected) n.Connect(); else n.RequestProfile();
    }

    bool pixOpen;
    string pixPack = "";
    float pixAsked = -99f, pixNextCheck;

#if UNITY_WEBGL && !UNITY_EDITOR
    [System.Runtime.InteropServices.DllImport("__Internal")] static extern void RfsCopy(string text);
#endif
    static void Copy(string t)
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        RfsCopy(t);
#else
        GUIUtility.systemCopyBuffer = t;
#endif
    }

    // Janela do PIX: escolhe o pacote de SHOTS, mostra o QR e o copia e cola, e espera a confirmacao.
    void DrawPix(float vw, float vh)
    {
        float w = 760f, h = 640f, x = (vw - w) / 2f, y = (vh - h) / 2f;
        Game.Fill(0f, 0f, vw, vh, new Color(0f, 0f, 0f, 0.6f));
        Game.Fill(x, y, w, h, new Color(0.04f, 0.045f, 0.06f, 0.98f));
        Game.Fill(x, y, w, 5f, Gold);
        CoinIcon(x + 28f, y + 26f, 64f);
        Game.Text(x + 108f, y + 22f, w - 140f, 44f, "COMPRAR SHOTS COM PIX", 30, Color.white, TextAnchor.MiddleLeft);
        Game.Text(x + 108f, y + 62f, w - 140f, 28f, "Saldo atual: " + Num(Skins.Coins) + " SHOTS", 17, Gold, TextAnchor.MiddleLeft);
        NetMsg px = Skins.pix;
        if (Skins.pixPaid > 0)
        {
            Game.Text(x, y + 200f, w, 60f, "PAGAMENTO CONFIRMADO", 40, new Color(0.45f, 1f, 0.55f), TextAnchor.MiddleCenter);
            Game.Text(x, y + 270f, w, 40f, "+" + Num(Skins.pixPaid) + " SHOTS na sua conta", 26, Gold, TextAnchor.MiddleCenter);
            if (Button(x + w / 2f - 160f, y + h - 100f, 320f, 62f, "CONTINUAR", true)) { pixOpen = false; Skins.pix = null; Skins.pixPaid = 0; }
            return;
        }
        if (px == null)
        {
            if (!Skins.PixOn)
                Game.Text(x + 40f, y + 110f, w - 80f, 30f, "Pagamento por PIX em breve. Por enquanto, SHOTS so jogando online.", 17, new Color(1f, 0.7f, 0.5f), TextAnchor.MiddleLeft);
            else
                Game.Text(x + 40f, y + 110f, w - 80f, 30f, "Escolha um pacote. Os SHOTS caem na conta assim que o PIX for pago.", 17, new Color(1f, 1f, 1f, 0.7f), TextAnchor.MiddleLeft);
            NetPack[] packs = Skins.Packs;
            for (int i = 0; i < packs.Length; i++)
            {
                float by = y + 160f + i * 86f;
                bool asking = pixPack == packs[i].id && Time.unscaledTime - pixAsked < 15f;
                if (Button(x + 40f, by, w - 80f, 72f, Num(packs[i].shots) + " SHOTS        R$ " + packs[i].price + (asking ? "   ..." : ""), i == 1, 24) && Skins.PixOn && !asking)
                {
                    pixPack = packs[i].id; pixAsked = Time.unscaledTime; Net.I.RequestPix(packs[i].id);
                }
            }
            if (Button(x + w / 2f - 160f, y + h - 90f, 320f, 58f, "FECHAR")) pixOpen = false;
            return;
        }
        // Cobranca gerada: QR + copia e cola. Pergunta ao servidor a cada 5 s se ja foi pago.
        Texture2D qr = Skins.PixQr();
        if (qr != null) { Game.Fill(x + 40f, y + 120f, 300f, 300f, Color.white); GUI.DrawTexture(new Rect(x + 50f, y + 130f, 280f, 280f), qr, ScaleMode.ScaleToFit, false); }
        Game.Text(x + 370f, y + 120f, w - 410f, 40f, Num(px.shots) + " SHOTS", 30, Gold, TextAnchor.MiddleLeft);
        Game.Text(x + 370f, y + 160f, w - 410f, 36f, "R$ " + px.price, 26, Color.white, TextAnchor.MiddleLeft);
        GUIStyle ds = new GUIStyle(GUI.skin.label); ds.wordWrap = true; ds.fontSize = 16; ds.normal.textColor = new Color(1f, 1f, 1f, 0.75f);
        GUI.Label(new Rect(x + 370f, y + 210f, w - 410f, 120f), "Abra o app do seu banco, escolha PIX e leia o QR code, ou use o copia e cola. A cobranca vale por 30 minutos.", ds);
        if (Button(x + 370f, y + 340f, w - 410f, 62f, "COPIAR CODIGO PIX", true)) { Copy(px.code ?? ""); shopMsg = "Codigo copiado!"; shopMsgUntil = Time.unscaledTime + 3f; }
        if (Time.unscaledTime < shopMsgUntil) Game.Text(x + 370f, y + 406f, w - 410f, 26f, shopMsg, 16, new Color(0.5f, 1f, 0.6f), TextAnchor.MiddleLeft);
        Game.Text(x + 40f, y + 450f, w - 80f, 30f, "Aguardando o pagamento...", 20, new Color(1f, 1f, 1f, 0.7f), TextAnchor.MiddleCenter);
        if (Time.unscaledTime > pixNextCheck && Net.I != null) { pixNextCheck = Time.unscaledTime + 5f; Net.I.CheckPix(px.pid); }
        if (Button(x + w / 2f - 160f, y + h - 90f, 320f, 58f, "FECHAR")) { pixOpen = false; Skins.pix = null; }
    }

    void DrawShop(float x, float vw, float vh)
    {
        SkinDef[] all = Skins.All;
        SkinDef sel = Skins.Get(shopSel);
        string[] wn = { "VETOR", "PULSO", "SNIPER", "BAZUCA", "PLASMA" };
        Title(x, 60f, "LOJA", "Skins de arma. Cada compra vale para a arma escolhida.");

        // Saldo.
        Game.Fill(x, 196f, 600f, 56f, new Color(1f, 1f, 1f, 0.05f));
        Game.Fill(x, 196f, 5f, 56f, Gold);
        CoinIcon(x + 14f, 200f, 48f);
        Game.Text(x + 70f, 196f, 300f, 40f, Num(Skins.Coins) + " SHOTS", 28, Gold, TextAnchor.MiddleLeft);
        int last = Skins.LastMatchCoins;
        bool logged = Net.I != null && Net.I.loggedIn;
        string sub = Skins.Server ? (last > 0 ? "+" + Num(last) + " na ultima partida" : "jogue online para ganhar") : logged ? "conectando..." : Application.isEditor ? "SHOTS de teste (editor)" : "entre na conta para ganhar e comprar";
        Game.Text(x + 70f, 230f, 300f, 22f, sub, 14, new Color(1f, 1f, 1f, 0.6f), TextAnchor.MiddleLeft);
        if (Skins.Server)
        {
            Game.Text(x + 330f, 198f, 260f, 30f, "NIVEL " + Skins.Level + "  -  " + Skins.Rank, 17, Color.white, TextAnchor.MiddleRight);
            LevelBar(x + 380f, 234f, 210f);
        }

        // Abas das armas.
        float tw = 600f / 5f;
        for (int i = 0; i < 5; i++)
            if (Button(x + i * tw, 266f, tw - 6f, 44f, wn[i], i == shopWeapon, 17)) { shopWeapon = i; shopConfirm = null; }

        // Lista: PADRAO + catalogo (por raridade e preco).
        float listY = 322f, listH = vh - listY - 110f, ch = 60f;
        Rect view = new Rect(x, listY, 620f, listH);
        Rect content = new Rect(0f, 0f, 600f, (all.Length + 1) * (ch + 6f));
        shopScroll = GUI.BeginScrollView(view, shopScroll, content, false, false, GUIStyle.none, GUI.skin.verticalScrollbar);
        for (int i = -1; i < all.Length; i++)
        {
            SkinDef s = i < 0 ? null : all[i];
            float cy = (i + 1) * (ch + 6f);
            if (cy + ch < shopScroll.y || cy > shopScroll.y + listH) continue;
            string id = s != null ? s.id : "";
            bool isSel = id == shopSel, own = Skins.Owns(id, shopWeapon), eq = Skins.Equipped(shopWeapon) == id;
            Color rc = s != null ? Skins.RarityColor(s.raridade) : new Color(1f, 1f, 1f, 0.5f);
            Rect r = new Rect(0f, cy, 600f, ch);
            bool hover = r.Contains(Event.current.mousePosition);
            Game.Fill(0f, cy, 600f, ch, isSel ? new Color(rc.r, rc.g, rc.b, 0.22f) : new Color(1f, 1f, 1f, hover ? 0.1f : 0.04f));
            Game.Fill(0f, cy, 6f, ch, rc);
            // Paleta (3 bolinhas) como miniatura.
            if (s != null && s.paleta != null)
                for (int k = 0; k < s.paleta.Length && k < 3; k++) Game.Fill(20f + k * 16f, cy + ch / 2f - 7f, 14f, 14f, Skins.Hex(s.paleta[k]));
            Game.Text(80f, cy + 4f, 330f, 30f, s != null ? s.nome : "PADRAO", 20, Color.white, TextAnchor.MiddleLeft);
            Game.Text(80f, cy + 30f, 330f, 24f, s != null ? Skins.RarityName(s.raridade) + (s.tipo == "textura" ? "  -  textura" : "") : "MODELO ORIGINAL", 13, new Color(rc.r, rc.g, rc.b, 0.9f), TextAnchor.MiddleLeft);
            string state = eq ? "EQUIPADA" : own ? "SUA" : Num(s.preco) + " SHOTS";
            Game.Text(400f, cy, 186f, ch, state, eq ? 17 : 18, eq ? Accent : own ? new Color(0.5f, 1f, 0.6f) : Color.white, TextAnchor.MiddleRight);
            if (Event.current.type == EventType.MouseDown && Event.current.button == 0 && hover && view.Contains(Event.current.mousePosition + new Vector2(view.x, view.y) - shopScroll))
            {
                shopSel = id; shopConfirm = null; Event.current.Use(); Sfx.UI("hit", 0.35f, 1.1f);
            }
        }
        GUI.EndScrollView();
        if (Button(x, vh - 90f, 300f, 58f, "VOLTAR")) { page = Page.Main; shopConfirm = null; }

        // ---- Vitrine 3D a direita (fundo escuro: a cidade nao aparece atras) ----
        Game.Fill(760f, 0f, vw - 760f, vh, new Color(0.03f, 0.03f, 0.035f, 0.94f));
        ShopView v = ShopView.Ensure();
        v.Keep();
        v.Show(shopWeapon, sel);
        float px = 790f, pw = vw - px - 40f, py = 40f, ph = Mathf.Min(vh - 330f, pw * 0.62f);
        if (pw < 300f) return;
        Rect pr = new Rect(px, py, pw, ph);
        Game.Fill(px - 2f, py - 2f, pw + 4f, ph + 4f, new Color(1f, 1f, 1f, 0.08f));
        GUI.DrawTexture(pr, v.rt, ScaleMode.ScaleAndCrop, false);
        Event e = Event.current;
        if (pr.Contains(e.mousePosition))
        {
            if (e.type == EventType.MouseDrag && e.button == 0) { v.Drag(e.delta); e.Use(); }
            else if (e.type == EventType.ScrollWheel) { v.Zoom(1f - e.delta.y * 0.06f); e.Use(); }
        }
        // Ferramentas (como no HTML): girar e zoom.
        if (Button(px + pw - 310f, py + 12f, 130f, 48f, "GIRAR", v.spin)) v.spin = !v.spin;
        if (Button(px + pw - 170f, py + 12f, 76f, 48f, "+")) v.Zoom(1.2f);
        if (Button(px + pw - 88f, py + 12f, 76f, 48f, "-")) v.Zoom(1f / 1.2f);
        Game.Text(px + 16f, py + ph - 34f, pw - 32f, 28f, "Arraste para girar  -  roda do mouse ou + / - para aproximar", 14, new Color(1f, 1f, 1f, 0.5f), TextAnchor.MiddleLeft);

        // Ficha da skin e acao.
        float iy = py + ph + 20f;
        Color tc = sel != null ? Skins.RarityColor(sel.raridade) : new Color(1f, 1f, 1f, 0.6f);
        Game.Text(px, iy, pw, 48f, (sel != null ? sel.nome : "Padrao") + "  -  " + wn[shopWeapon], 34, Color.white, TextAnchor.MiddleLeft);
        Game.Text(px, iy + 48f, 400f, 28f, sel != null ? Skins.RarityName(sel.raridade) : "MODELO ORIGINAL", 16, tc, TextAnchor.MiddleLeft);
        if (sel != null)
        {
            // Descricao com quebra de linha.
            GUIStyle ds = new GUIStyle(GUI.skin.label);
            ds.wordWrap = true; ds.fontSize = 16; ds.normal.textColor = new Color(1f, 1f, 1f, 0.72f);
            GUI.Label(new Rect(px, iy + 80f, pw - 390f, 90f), sel.descricao, ds);
        }

        string id0 = sel != null ? sel.id : "";
        bool owns = Skins.Owns(id0, shopWeapon), equipped = Skins.Equipped(shopWeapon) == id0;
        float bx = px + pw - 360f, by = iy + 10f;
        if (shopConfirm == id0 && sel != null && !owns)
        {
            Game.Text(bx, by - 6f, 360f, 30f, "Comprar por " + Num(sel.preco) + " SHOTS?", 18, Color.white, TextAnchor.MiddleLeft);
            if (Button(bx, by + 30f, 175f, 56f, "SIM", true))
            {
                if (Skins.Buy(id0, shopWeapon)) { if (!Skins.Server) Skins.Equip(shopWeapon, id0); shopMsg = "Comprada e equipada!"; Sfx.UI("pickup", 0.7f); }
                else shopMsg = "Nao foi possivel comprar.";
                shopMsgUntil = Time.unscaledTime + 3f; shopConfirm = null;
            }
            if (Button(bx + 185f, by + 30f, 175f, 56f, "NAO")) shopConfirm = null;
        }
        else if (equipped) { Game.Fill(bx, by, 360f, 62f, new Color(1f, 1f, 1f, 0.06f)); Game.Text(bx + 24f, by, 330f, 62f, "EQUIPADA", 24, Accent, TextAnchor.MiddleLeft); }
        else if (owns) { if (Button(bx, by, 360f, 62f, "EQUIPAR", true)) { Skins.Equip(shopWeapon, id0); Sfx.UI("pickup", 0.5f); } }
        else if (Skins.Coins >= sel.preco) { if (Button(bx, by, 360f, 62f, "COMPRAR  " + Num(sel.preco), true)) shopConfirm = id0; }
        else if (!Skins.CanBuy) { Game.Fill(bx, by, 360f, 62f, new Color(1f, 1f, 1f, 0.06f)); Game.Text(bx + 24f, by, 330f, 62f, "ENTRE NA CONTA PARA COMPRAR", 17, new Color(1f, 0.7f, 0.5f), TextAnchor.MiddleLeft); }
        else
        {
            Game.Fill(bx, by, 360f, 62f, new Color(1f, 1f, 1f, 0.06f));
            Game.Text(bx + 24f, by, 330f, 62f, "FALTAM " + Num(sel.preco - Skins.Coins) + " SHOTS", 22, new Color(1f, 0.5f, 0.4f), TextAnchor.MiddleLeft);
            if (Skins.Server && Button(bx, by + 70f, 360f, 54f, "COMPRAR SHOTS (PIX)", false, 20)) { pixOpen = true; Skins.pix = null; Skins.pixPaid = 0; }
        }
        if (Skins.Pending) Game.Text(bx, by + 132f, 360f, 30f, "Comprando...", 18, Color.white, TextAnchor.MiddleLeft);
        else if (Time.unscaledTime - Skins.shopErrorAt < 4f) Game.Text(bx, by + 132f, 360f, 30f, Skins.shopError, 18, new Color(1f, 0.5f, 0.4f), TextAnchor.MiddleLeft);
        else if (Time.unscaledTime < shopMsgUntil) Game.Text(bx, by + 132f, 360f, 30f, shopMsg, 18, new Color(0.5f, 1f, 0.6f), TextAnchor.MiddleLeft);
        if (pixOpen) DrawPix(vw, vh);
    }

    void DrawSetup(float x)
    {
        float pw = Mathf.Min(900f, screenW - 760f - 120f);
        if (pw > 360f) MapPreview(Settings.map, 760f + (screenW - 760f - pw) / 2f, 200f, pw);
        Title(x, 110f, "PARTIDA", Settings.mode == 1 ? "Bomba: ataque planta em A ou B, defesa desarma (5 rodadas)" : "Mata-mata em equipe contra bots");
        float y = 370f, w = 620f, gap = 66f;
        int d = Stepper(x, y - gap * 2f, w, "MAPA", Arena.MapNames[Settings.map]);
        int nm = Arena.MapNames.Length;
        if (d != 0) Settings.map = (Settings.map + d + nm) % nm;
        if (Settings.map == 1) Game.Text(x, y - gap * 2f + 46f, w, 18f, "Base congelada: corredores estreitos, portoes (F), subterraneo, tetos e nevoa a cada 40 s", 13, new Color(0.6f, 0.85f, 1f, 0.85f), TextAnchor.MiddleLeft);
        if (Settings.map == 2) Game.Text(x, y - gap * 2f + 46f, w, 18f, "Mapa pequeno (ideal 3x3 ou 4x4): as paredes da casa quebram com tiros e explosoes", 13, new Color(1f, 0.86f, 0.5f, 0.85f), TextAnchor.MiddleLeft);
        d = Stepper(x, y - gap, w, "MODO", Settings.ModeNames[Settings.mode]);
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

    // OPCOES em abas: GERAL (tela e audio), MIRA (como a mira aparece), ASSISTENCIA (ajuda de mira) e SENSIBILIDADE.
    // Tudo vale na hora; salva ao trocar de aba e ao voltar.
    int optTab;
    int aaEdit = -1;   // perfil da assistencia que esta sendo editado (-1 = o do aparelho atual)
    static readonly string[] OptTabs = { "GERAL", "MIRA", "ASSISTENCIA", "SENSIBILIDADE" };

    void Hint(float x, float y, float w, string t)
    {
        Game.Text(x, y, w, 20f, t, 13, new Color(1f, 1f, 1f, 0.5f), TextAnchor.MiddleLeft);
    }

    void DrawOptions(float x)
    {
        Title(x, 60f, "OPCOES", null);
        float w = 620f;
        for (int i = 0; i < OptTabs.Length; i++)
            if (Button(x + i * 157f, 140f, 150f, 44f, OptTabs[i], optTab == i, 17) && optTab != i) { optTab = i; Settings.Save(); }
        float y = 206f;
        switch (optTab)
        {
            case 1: DrawOptAim(x, y, w); break;
            case 2: DrawOptAssist(x, y, w); break;
            case 3: DrawOptSens(x, y, w); break;
            default: DrawOptGeneral(x, y, w); break;
        }
        if (Button(x, 990f, 520f, 56f, "SALVAR E VOLTAR", true)) Back();
    }

    void DrawOptGeneral(float x, float y, float w)
    {
        float gap = 58f;
        Settings.fov = Mathf.Round(Slider(x, y, w, "CAMPO DE VISAO", Settings.fov, 70f, 110f, "0"));
        float y2 = y + gap + 12f;
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
    }

    // Aba MIRA: aparencia da mira, com previa ao lado (fundo claro e escuro, para ver o contorno).
    void DrawOptAim(float x, float y, float w)
    {
        float g = 64f;
        Aim.chOn = Toggle(x, y, w, "MIRA PERSONALIZADA", Aim.chOn);
        Hint(x, y + 42f, w, Aim.chOn ? "Usando a mira abaixo. Desligue para voltar a mira padrao do jogo." : "Desligada: mira padrao do jogo. Ligue para usar a sua.");
        y += g;
        int d = Stepper(x, y, w, "FORMATO", Aim.Shapes[Aim.chShape]);
        if (d != 0) Aim.chShape = (Aim.chShape + d + Aim.Shapes.Length) % Aim.Shapes.Length;
        Hint(x, y + 42f, w, "Cruz, ponto, cruz com ponto, circulo ou T (sem a linha de cima).");
        y += g;
        d = Stepper(x, y, w, "COR", Aim.ColorNames[Aim.chColor]);
        if (d != 0) Aim.chColor = (Aim.chColor + d + Aim.Colors.Length) % Aim.Colors.Length;
        Hint(x, y + 42f, w, "Escolha uma cor que contraste com o mapa (verde e ciano aparecem bem).");
        y += g;
        Aim.chSize = Mathf.Round(Slider(x, y, w, "TAMANHO", Aim.chSize, 2f, 30f, "0"));
        Hint(x, y + 42f, w, "Comprimento de cada linha.");
        y += g;
        Aim.chThick = Mathf.Round(Slider(x, y, w, "ESPESSURA", Aim.chThick, 1f, 6f, "0"));
        Hint(x, y + 42f, w, "Grossura das linhas e do ponto.");
        y += g;
        Aim.chGap = Mathf.Round(Slider(x, y, w, "ESPACAMENTO", Aim.chGap, 0f, 20f, "0"));
        Hint(x, y + 42f, w, "Espaco vazio no centro (no circulo, aumenta o raio).");
        y += g;
        Aim.chOutline = Toggle(x, y, w, "CONTORNO", Aim.chOutline);
        Hint(x, y + 42f, w, "Borda escura: a mira continua visivel em cenario claro.");
        y += g;
        Aim.chAlpha = Slider(x, y, w, "OPACIDADE", Aim.chAlpha, 0.2f, 1f, "0%");
        Hint(x, y + 42f, w, "100% = solida. Menos deixa a mira transparente.");
        y += g;
        Aim.chDot = Toggle(x, y, w, "PONTO CENTRAL", Aim.chDot);
        Hint(x, y + 42f, w, "Ponto pequeno no centro da mira.");
        y += g;
        Aim.chDynamic = Toggle(x, y, w, "MIRA DINAMICA", Aim.chDynamic);
        Hint(x, y + 42f, w, "Abre ao correr, pular e atirar (mostra a precisao da arma). Desligado: fica parada.");
        y += g;
        if (Button(x, y + 2f, w, 46f, "RESTAURAR MIRA (SO A APARENCIA)", false, 18)) Aim.ResetCrosshair();

        // Previa em tempo real
        float px = x + 680f, py = 206f, ps = 380f;
        Game.Text(px, py - 34f, ps, 28f, "PREVIA", 16, Accent, TextAnchor.MiddleLeft);
        Game.Fill(px, py, ps / 2f, ps, new Color(0.86f, 0.78f, 0.6f, 1f));
        Game.Fill(px + ps / 2f, py, ps / 2f, ps, new Color(0.08f, 0.09f, 0.11f, 1f));
        Game.Fill(px, py + ps * 0.62f, ps, ps * 0.38f, new Color(0.35f, 0.42f, 0.3f, 1f));
        float pcx = px + ps / 2f, pcy = py + ps / 2f;
        if (Aim.chOn) Aim.Draw(pcx, pcy, Aim.chDynamic ? Mathf.PingPong(Time.unscaledTime * 0.8f, 1.2f) : 0f);
        else
        {
            Color cc = new Color(1f, 1f, 1f, 0.9f);
            Game.Fill(pcx - 18f, pcy - 1f, 12f, 2f, cc); Game.Fill(pcx + 6f, pcy - 1f, 12f, 2f, cc);
            Game.Fill(pcx - 1f, pcy - 18f, 2f, 12f, cc); Game.Fill(pcx - 1f, pcy + 6f, 2f, 12f, cc);
        }
        Game.Text(px, py + ps + 8f, ps, 22f, Aim.chOn ? (Aim.chDynamic ? "a mira abre e fecha como ao correr e atirar" : "mira personalizada") : "mira padrao do jogo", 13, new Color(1f, 1f, 1f, 0.55f), TextAnchor.MiddleCenter);
    }

    // Aba ASSISTENCIA: ajuda de mira sutil. Perfis separados para mouse/teclado e toque.
    void DrawOptAssist(float x, float y, float w)
    {
        int p = aaEdit < 0 ? Aim.Profile : aaEdit;
        int d = Stepper(x, y, w, "PERFIL", Aim.ProfileNames[p]);
        if (d != 0) aaEdit = 1 - p;
        Hint(x, y + 42f, w, "Este aparelho usa: " + Aim.ProfileNames[Aim.Profile] + ". Cada tipo de controle tem seus valores.");
        float g = 66f;
        y += g;
        Aim.aaOn[p] = Toggle(x, y, w, "ASSISTENCIA DE MIRA", Aim.aaOn[p]);
        Hint(x, y + 42f, w, Aim.aaOn[p] ? "Ajuda sutil: nunca mira sozinha, nunca trava no alvo e nao atira por voce." : "Desligada: nenhuma ajuda de mira.");
        y += g;
        bool on = Aim.aaOn[p];
        Color dim = new Color(1f, 1f, 1f, on ? 1f : 0.35f);
        GUI.color = dim;
        Aim.aaStrength[p] = Mathf.Round(Slider(x, y, w, "INTENSIDADE GERAL", Aim.aaStrength[p], 0f, 1f, "0%") * 100f) / 100f;
        Hint(x, y + 42f, w, "Forca total da ajuda (sugerido 35%). Escala a desaceleracao e o acompanhamento.");
        y += g;
        Aim.aaSlow[p] = Mathf.Round(Slider(x, y, w, "DESACELERACAO", Aim.aaSlow[p], 0f, 1f, "0%") * 100f) / 100f;
        Hint(x, y + 42f, w, "A camera fica mais lenta quando a mira passa sobre um inimigo (sugerido 30%). Nao para a mira.");
        y += g;
        Aim.aaFollow[p] = Mathf.Round(Slider(x, y, w, "ACOMPANHAMENTO", Aim.aaFollow[p], 0f, 1f, "0%") * 100f) / 100f;
        Hint(x, y + 42f, w, "Segue de leve o inimigo que ja esta na sua mira (sugerido 20%). Nao puxa de longe.");
        y += g;
        Aim.aaAds[p] = Mathf.Round(Slider(x, y, w, "ASSISTENCIA NO ADS", Aim.aaAds[p], 0f, 1f, "0%") * 100f) / 100f;
        Hint(x, y + 42f, w, "Intensidade enquanto mira com a arma (botao direito / MIRA). Sugerido 40% no toque.");
        GUI.color = Color.white;
        y += g + 6f;
        if (Button(x, y, w, 46f, "RESTAURAR VALORES SUGERIDOS (" + Aim.ProfileNames[p] + ")", false, 17)) Aim.ResetAssist(p);
        y += 64f;
        Game.Text(x, y, w, 90f, "So vale para inimigo visivel: nao atravessa paredes nem pega alvo fora da linha de visao.\nNao altera a sua sensibilidade. No mouse e teclado ela comeca desligada e mais discreta.", 14, new Color(1f, 1f, 1f, 0.6f), TextAnchor.UpperLeft);
    }

    // Aba SENSIBILIDADE
    void DrawOptSens(float x, float y, float w)
    {
        float g = 66f;
        string dev = TouchPad.On ? "do toque" : "do mouse";
        Settings.sensitivity = Slider(x, y, w, "HORIZONTAL", Settings.sensitivity, 0.3f, 6f, "0.00");
        Hint(x, y + 42f, w, "Velocidade para olhar para os lados (" + dev + ").");
        y += g;
        Aim.sensY = Slider(x, y, w, "VERTICAL", Aim.sensY, 0.3f, 6f, "0.00");
        Hint(x, y + 42f, w, "Velocidade para olhar para cima e para baixo.");
        y += g;
        Aim.sensAds = Slider(x, y, w, "SENSIBILIDADE ADS", Aim.sensAds, 0.3f, 6f, "0.00");
        Hint(x, y + 42f, w, "Velocidade da camera enquanto mira com a arma (o zoom da lente ja e compensado).");
        y += g;
        Aim.adsMult = Slider(x, y, w, "MULTIPLICADOR ADS", Aim.adsMult, 0.5f, 1.5f, "0.00x");
        Hint(x, y + 42f, w, "Ajuste fino extra da sensibilidade ADS (1.00x = sem mudanca).");
        y += g;
        Settings.invertY = Toggle(x, y, w, "INVERTER EIXO VERTICAL", Settings.invertY);
        Hint(x, y + 42f, w, "Mover para cima olha para baixo (estilo simulador de voo).");
        y += g;
        if (Button(x, y + 6f, w, 46f, "IGUALAR VERTICAL E ADS A HORIZONTAL", false, 17)) { Aim.sensY = Settings.sensitivity; Aim.sensAds = Settings.sensitivity; Aim.adsMult = 1f; }
    }

    void DrawControls(float x)
    {
        Title(x, 90f, "CONTROLES", null);
        bool tac = PlayerController.TacticalMode;
        string[,] rows = tac ? new string[,]
        {
            { "W A S D", "Mover" },
            { "MOUSE", "Olhar" },
            { "SHIFT", "Correr (atirar logo depois tem um pequeno atraso)" },
            { "ESPACO", "Pular" },
            { "MOUSE ESQ.", "Atirar" },
            { "MOUSE DIR.", "Mirar" },
            { "R", "Recarregar" },
            { "1  2  3  4  /  SCROLL", "Trocar de arma" },
            { "F", "Veiculo / caixa / bomba (segurar)" },
            { "ESPACO (no ar)", "Jetpack, se tiver pego" },
            { "SHIFT na luneta", "Prender o folego" },
            { "Q", "Habilidade / granada" },
            { "X", "Sobrecarga (carrega com abates)" },
            { "CTRL  /  C", "Agachar (segurar)  /  agachar (liga e desliga)" },
            { "V  (SEGURAR)", "Falar com o time (salas online)" },
            { "Z  /  BOTAO DO MEIO", "Marcar para o time (inimigo, vida...)" },
            { "TAB", "Placar" },
            { "ESC", "Pausar" }
        } : new string[,]
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
            "FATAL SHOOTER",
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
            Game.Text(x, y, 600f, 30f, "JOGO RAPIDO  -  5 x 5, bots completam os times (sem XP)", 17, Accent, TextAnchor.MiddleLeft);
            y += 36f;
            // Duas colunas (um botao por mapa e modo).
            for (int i = 0; i < n.rows.Length; i++)
            {
                NetRow r = n.rows[i];
                string label = r.map + " - " + r.mode + (r.players > 0 ? "  (" + r.players + ")" : "");
                float bx = x + (i % 2) * (w / 2f + 5f);
                if (Button(bx, y, w / 2f - 5f, 50f, label, i == 0, 17)) n.Quick(r.map, r.mode);
                if (i % 2 == 1 || i == n.rows.Length - 1) y += 56f;
            }
            y += 14f;
            Game.Text(x, y, 600f, 30f, "CRIAR SALA  -  XP e SHOTS so em sala SEM BOTS", 17, Accent, TextAnchor.MiddleLeft);
            y += 36f;
            // Publica aparece na lista de salas; privada so entra com o codigo.
            if (Button(x, y, 315f, 46f, "PUBLICA (NA LISTA)", newPublic)) newPublic = true;
            if (Button(x + 325f, y, 315f, 46f, "PRIVADA (SO CODIGO)", !newPublic)) newPublic = false;
            y += 52f;
            if (Button(x, y, 315f, 46f, "COM BOTS", newBots)) newBots = true;
            if (Button(x + 325f, y, 315f, 46f, "SO OS AMIGOS (SEM BOTS)", !newBots)) newBots = false;
            y += 56f;
            if (Button(x, y, 206f, 46f, "MERCADO", Settings.map == 0)) Settings.map = 0;
            if (Button(x + 217f, y, 206f, 46f, "NO GELO", Settings.map == 1)) Settings.map = 1;
            if (Button(x + 434f, y, 206f, 46f, "A CASA", Settings.map == 2)) Settings.map = 2;
            y += 52f;
            if (Button(x, y, 315f, 54f, "CRIAR: MATA-MATA", true)) { Settings.Save(); n.Create(Arena.MapNames[Settings.map], "MATA-MATA", !newPublic, newBots); }
            if (Button(x + 325f, y, 315f, 54f, "CRIAR: BOMBA", true)) { Settings.Save(); n.Create(Arena.MapNames[Settings.map], "BOMBA", !newPublic, newBots); }
            y += 66f;
            fCode = Field(x, y + 5f, 400f, "CODIGO DA SALA", fCode, false, 5).ToUpperInvariant();
            if (Button(x + 415f, y, 225f, 54f, "ENTRAR")) n.Join(fCode);
            y += 66f;

            // Coluna da direita: salas abertas ou amigos.
            float rx = x + 740f;
            int pend = n.friendReqs.Length;
            if (Button(rx - 24f, 112f, 318f, 50f, "SALAS ABERTAS", !friendsTab, 20)) friendsTab = false;
            if (Button(rx + 300f, 112f, 348f, 50f, "AMIGOS  " + n.FriendsOnline + "/" + n.friends.Length + (pend > 0 ? "   (+" + pend + ")" : ""), friendsTab, 20)) { friendsTab = true; n.RequestFriends(); }
            if (friendsTab) DrawFriends(n, rx, 180f, 600f, false); else DrawRoomList(n, rx);
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

    // Amigos: adicionar pelo nome, pedidos recebidos/enviados e a lista com quem esta online.
    // invite = true (menu da partida): so os amigos online, com botao CONVIDAR.
    void DrawFriends(Net n, float x, float y, float w, bool invite)
    {
        float top = y;
        Game.Fill(x - 24f, y - 20f, w + 48f, invite ? 720f : 800f, Panel);
        Color dim = new Color(1f, 1f, 1f, 0.55f);
        Game.Text(x, y, w, 34f, invite ? "CONVIDAR AMIGOS" : "AMIGOS", 24, Color.white, TextAnchor.MiddleLeft);
        y += 44f;
        if (!invite)
        {
            fFriend = Field(x, y, w - 190f, "NOME DO JOGADOR", fFriend, false, 16).ToUpperInvariant();
            if (Button(x + w - 180f, y, 180f, 46f, "ADICIONAR", true, 20)) { n.AddFriend(fFriend); fFriend = ""; }
            y += 54f;
        }
        bool showMsg = n.friendMsg != "" && Time.unscaledTime - n.friendMsgAt < 6f;
        Game.Text(x, y, w, 26f, showMsg ? n.friendMsg : (invite ? "Seu amigo recebe o convite na tela e entra com um clique." : "Peca amizade pelo nome que aparece no jogo."), 15,
            showMsg ? (n.friendOk ? Accent : new Color(1f, 0.45f, 0.4f)) : dim, TextAnchor.MiddleLeft);
        y += 36f;

        if (!invite && n.friendReqs.Length > 0)
        {
            Game.Text(x, y, w, 26f, "PEDIDOS DE AMIZADE", 16, Accent, TextAnchor.MiddleLeft);
            y += 30f;
            for (int i = 0; i < n.friendReqs.Length && i < 3; i++)
            {
                NetFriend f = n.friendReqs[i];
                Game.Fill(x, y, w, 54f, new Color(1f, 0.86f, 0.1f, 0.08f));
                Game.Text(x + 14f, y, w - 330f, 54f, f.name + "   NV " + f.lvl, 19, Color.white, TextAnchor.MiddleLeft);
                if (Button(x + w - 310f, y + 6f, 150f, 42f, "ACEITAR", true, 18)) n.AcceptFriend(f.uid);
                if (Button(x + w - 152f, y + 6f, 150f, 42f, "RECUSAR", false, 18)) n.RemoveFriend(f.uid);
                y += 60f;
            }
            y += 8f;
        }

        // Lista (no menu da partida, so quem esta online).
        List<NetFriend> list = new List<NetFriend>();
        foreach (NetFriend f in n.friends) if (!invite || f.st > 0) list.Add(f);
        if (!invite) foreach (NetFriend f in n.friendSent) list.Add(new NetFriend { uid = f.uid, name = f.name, lvl = f.lvl, st = -1 });
        float bottom = top + (invite ? 680f : 760f);
        int fit = Mathf.Max(1, (int)((bottom - y) / 64f));
        if (list.Count == 0)
        {
            string empty = !n.friendsLoaded ? "Carregando..." : invite ? "Nenhum amigo online agora." : "Voce ainda nao tem amigos. Adicione pelo nome!";
            Game.Text(x, y, w, 40f, empty, 18, dim, TextAnchor.MiddleLeft);
            return;
        }
        // Rolagem com a roda do mouse quando nao cabe.
        Event e = Event.current;
        if (e.type == EventType.ScrollWheel && new Rect(x, y, w, bottom - y).Contains(e.mousePosition)) { friendScroll += e.delta.y > 0 ? 1 : -1; e.Use(); }
        friendScroll = Mathf.Clamp(friendScroll, 0, Mathf.Max(0, list.Count - fit));
        for (int i = friendScroll; i < list.Count && i < friendScroll + fit; i++)
        {
            NetFriend f = list[i];
            Color dot = f.st == 2 ? Accent : f.st == 1 ? new Color(0.3f, 0.95f, 0.5f) : new Color(1f, 1f, 1f, 0.25f);
            Game.Fill(x, y, w, 58f, new Color(1f, 1f, 1f, 0.05f));
            Game.Fill(x + 12f, y + 23f, 12f, 12f, dot);
            Game.Text(x + 36f, y + 4f, w - 240f, 28f, f.name + "   NV " + f.lvl, 19, f.st > 0 ? Color.white : dim, TextAnchor.MiddleLeft);
            string sub = f.st == 2 ? "Em partida: " + f.mode + " - " + f.map + "  (" + f.n + "/10)" : f.st == 1 ? "Online, no menu" : f.st == 0 ? "Offline" : "Pedido enviado, aguardando";
            Game.Text(x + 36f, y + 30f, w - 240f, 24f, sub, 14, dim, TextAnchor.MiddleLeft);
            float bx = x + w - 196f;
            if (invite)
            {
                bool here = f.st == 2 && f.code == n.roomCode;
                if (here) Game.Text(bx, y, 190f, 58f, "NA SALA", 16, dim, TextAnchor.MiddleCenter);
                else if (Button(bx, y + 8f, 190f, 42f, "CONVIDAR", true, 18)) n.InviteFriend(f.uid);
            }
            else if (f.st == -1) { if (Button(bx, y + 8f, 190f, 42f, "CANCELAR", false, 17)) n.RemoveFriend(f.uid); }
            else
            {
                // Remover pede confirmacao (segundo clique).
                bool ask = removeAsk == f.uid;
                if (Button(bx + 140f, y + 8f, 50f, 42f, ask ? "?" : "X", ask, 18)) { if (ask) { n.RemoveFriend(f.uid); removeAsk = ""; } else removeAsk = f.uid; }
                if (ask) Game.Text(bx - 150f, y, 290f, 58f, "remover? clique de novo", 13, new Color(1f, 0.45f, 0.4f), TextAnchor.MiddleRight);
                else if (f.st == 2 && f.n < 10 && Button(bx, y + 8f, 132f, 42f, "ENTRAR", true, 17)) n.Join(f.code);
            }
            y += 64f;
        }
        if (list.Count > fit) Game.Text(x, bottom, w, 24f, "role para ver mais  (" + (friendScroll + 1) + "-" + Mathf.Min(list.Count, friendScroll + fit) + " de " + list.Count + ")", 13, dim, TextAnchor.MiddleCenter);
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
            if (Net.I.loggedIn) DrawFriends(Net.I, Mathf.Min(x + 1180f, screenW - 560f), 290f, 520f, true);
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

    // XP e SHOTS da partida online (o servidor calcula e manda linha a linha).
    void RewardPanel(float vw, float vh)
    {
        float w = 430f, x = vw - w - 40f, y = 200f;
        NetMsg r = Net.I != null ? Net.I.reward : null;
        int n = r != null && r.lines != null ? r.lines.Length : 0;
        float h = 190f + n * 30f;
        Game.Fill(x, y, w, h, new Color(0.02f, 0.025f, 0.04f, 0.9f));
        Game.Fill(x, y, w, 4f, Gold);
        Game.Text(x + 20f, y + 10f, w - 40f, 36f, "XP DA PARTIDA", 22, Color.white, TextAnchor.MiddleLeft);
        if (r != null && !string.IsNullOrEmpty(r.err))
        {
            GUIStyle es = new GUIStyle(GUI.skin.label); es.wordWrap = true; es.fontSize = 17; es.normal.textColor = new Color(1f, 0.75f, 0.5f);
            GUI.Label(new Rect(x + 20f, y + 56f, w - 40f, 110f), r.err, es);
            return;
        }
        if (r == null)
        {
            Game.Text(x + 20f, y + 60f, w - 40f, 60f, Skins.Server ? "Calculando..." : "Entre na conta para ganhar XP e SHOTS.", 17, new Color(1f, 1f, 1f, 0.6f), TextAnchor.MiddleLeft);
            return;
        }
        for (int i = 0; i < n; i++)
        {
            string[] p = r.lines[i].Split('|');
            Game.Text(x + 20f, y + 52f + i * 30f, w - 130f, 28f, p[0], 16, new Color(1f, 1f, 1f, 0.75f), TextAnchor.MiddleLeft);
            if (p.Length > 1) Game.Text(x + w - 120f, y + 52f + i * 30f, 100f, 28f, (p[1].StartsWith("-") ? "" : "+") + p[1], 16, Color.white, TextAnchor.MiddleRight);
        }
        float ty = y + 60f + n * 30f;
        Game.Fill(x + 20f, ty, w - 40f, 2f, new Color(1f, 1f, 1f, 0.2f));
        Game.Text(x + 20f, ty + 8f, w - 40f, 34f, "+" + Num(r.xp) + " XP", 26, Accent, TextAnchor.MiddleLeft);
        CoinIcon(x + w - 196f, ty + 8f, 34f);
        Game.Text(x + w - 160f, ty + 8f, 140f, 34f, "+" + Num(r.shots), 26, Gold, TextAnchor.MiddleRight);
        Game.Text(x + 20f, ty + 50f, w - 40f, 26f, (r.lvlUp > 0 ? "SUBIU PARA O NIVEL " : "NIVEL ") + Skins.Level + "  -  " + Skins.Rank, 17, r.lvlUp > 0 ? Accent : Color.white, TextAnchor.MiddleLeft);
        LevelBar(x + 20f, ty + 84f, w - 40f);
        Game.Text(x + 20f, ty + 94f, w - 40f, 24f, Num(Skins.LevelXp) + " / " + Num(Skins.LevelNeed) + " XP   -   saldo " + Num(Skins.Coins) + " SHOTS", 13, new Color(1f, 1f, 1f, 0.55f), TextAnchor.MiddleLeft);
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

        if (g.campaign != null)
        {
            Campaign c = g.campaign;
            Game.Text(cx - 600f, 200f, 1200f, 100f, c.won ? "MISSAO CUMPRIDA" : "MISSAO FALHOU", 72, c.won ? Accent : new Color(1f, 0.35f, 0.3f), TextAnchor.MiddleCenter);
            Game.Text(cx - 600f, 310f, 1200f, 40f, c.won ? "Sankhya esta a salvo. A MOLOCH vai se lembrar do Mercado." : c.failReason, 24, Color.white, TextAnchor.MiddleCenter);
            if (Button(cx - 260f, 440f, 520f, 62f, c.won ? "JOGAR DE NOVO" : "TENTAR DE NOVO", true)) g.StartCampaign();
            if (Button(cx - 260f, 514f, 520f, 62f, "MENU PRINCIPAL")) g.ToMenu();
            return;
        }
        bool tie = g.score[0] == g.score[1];
        int win = g.score[0] > g.score[1] ? 0 : 1;
        int team = g.player != null ? g.player.team : 0;
        string msg = tie ? "EMPATE" : (win == team ? "VITORIA" : "DERROTA");
        Game.Text(cx - 500f, 60f, 1000f, 90f, msg, 76, tie ? Color.white : Art.TeamColor(win), TextAnchor.MiddleCenter);
        Game.Text(cx - 500f, 150f, 1000f, 44f, "BAAL  " + g.score[0] + "   x   " + g.score[1] + "  MOLOCH", 30, Color.white, TextAnchor.MiddleCenter);
        g.Scoreboard(cx, cy - 20f);
        if (NetGame.Online) RewardPanel(vw, vh);
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
