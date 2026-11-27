using UnityEngine;

// Controles de toque (celular): analogico virtual na esquerda, arrastar na direita para mirar e botoes na tela.
// O GameInput consulta este estado junto com teclado e mouse.
[DefaultExecutionOrder(-200)]
public class TouchPad : MonoBehaviour
{
    public static bool On;
    public static bool ShotDown;           // botao FOTO (captura de tela)
    public static Vector2 Move, Look;
    public static int SelectSlot = -1;   // arma tocada na barra de armas (-1 = nenhuma)
    public static bool Talk;                // segurando FALAR (voz do time)
    public static bool Crouch;              // agachado (toque curto liga/desliga)
    public static bool Prone;               // deitado (toque longo ~0.38s liga/desliga)
    public static bool SprintLock;          // histerese do sprint no analogico virtual
    public static bool PingTap;            // toque rapido na area de mira: marcar para o time
    public static Vector2 PingPos;         // posicao do toque (pixels de tela)
    float lookStart; Vector2 lookFrom; float lookTravel;
    float downHoldStart; bool downLongTriggered;
    public static bool Fire, FireDown, Aim, Jump, JumpDown, Use, UseDown, ReloadDown, DashDown, GrenadeDown, SecondaryDown, UltDown, PauseDown, Down, NextWeapon, Score;

#if UNITY_WEBGL && !UNITY_EDITOR
    [System.Runtime.InteropServices.DllImport("__Internal")] static extern int RfsIsMobile();
    [System.Runtime.InteropServices.DllImport("__Internal")] static extern System.IntPtr RfsPrompt(string title, string val);
    [System.Runtime.InteropServices.DllImport("__Internal")] static extern void RfsFree(System.IntPtr p);
#endif

    public static void Detect()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        On = RfsIsMobile() == 1;
#else
        On = Application.isMobilePlatform;
#endif
    }

    // Caixa de texto do sistema (no celular o teclado nao abre sozinho nos campos do jogo).
    public static string Prompt(string title, string val)
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        System.IntPtr p = RfsPrompt(title, val ?? "");
        if (p == System.IntPtr.Zero) return val;
        string r = System.Runtime.InteropServices.Marshal.PtrToStringUTF8(p);
        RfsFree(p);
        return r;
#else
        return val;
#endif
    }

    struct Btn { public string id, label; public float x, y, size; }   // x,y medidos a partir do canto inferior direito
    static readonly Btn[] Defaults =
    {
        new Btn { id = "fire", label = "TIRO", x = 390f, y = 400f, size = 190f },
        new Btn { id = "aim", label = "MIRA", x = 500f, y = 250f, size = 130f },
        new Btn { id = "jump", label = "PULO", x = 150f, y = 200f, size = 130f },
        new Btn { id = "reload", label = "R", x = 150f, y = 480f, size = 110f },
        new Btn { id = "weapon", label = "ARMA", x = 320f, y = 540f, size = 120f },
        new Btn { id = "use", label = "F", x = 510f, y = 470f, size = 120f },
        new Btn { id = "dash", label = "E", x = 660f, y = 170f, size = 100f },
        new Btn { id = "nade", label = "Q", x = 790f, y = 170f, size = 100f },
        new Btn { id = "secondary", label = "C", x = 920f, y = 300f, size = 100f },
        new Btn { id = "ult", label = "X", x = 920f, y = 170f, size = 100f },
        new Btn { id = "down", label = "AGACHAR", x = 660f, y = 330f, size = 110f },   // a pe: agachar (liga/desliga); no veiculo: descer
        new Btn { id = "talk", label = "FALAR", x = 150f, y = 650f, size = 100f },     // voz do time (segurar), so online
    };

    // Layout em uso: o padrao com os ajustes do jogador (Settings.hudLayout: "id:x,y,tamanho;...").
    static Btn[] Buttons = LoadLayout();
    public static void ReloadLayout() { Buttons = LoadLayout(); }

    static Btn[] LoadLayout()
    {
        Btn[] l = (Btn[])Defaults.Clone();
        string data = Settings.hudLayout ?? "";
        foreach (string part in data.Split(';'))
        {
            int c = part.IndexOf(':');
            if (c <= 0) continue;
            string[] v = part.Substring(c + 1).Split(',');
            if (v.Length != 3) continue;
            float x, y, sz;
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            if (!float.TryParse(v[0], System.Globalization.NumberStyles.Float, inv, out x) ||
                !float.TryParse(v[1], System.Globalization.NumberStyles.Float, inv, out y) ||
                !float.TryParse(v[2], System.Globalization.NumberStyles.Float, inv, out sz)) continue;
            string id = part.Substring(0, c);
            for (int i = 0; i < l.Length; i++)
                if (l[i].id == id) { l[i].x = x; l[i].y = y; l[i].size = Mathf.Clamp(sz, 70f, 300f); }
        }
        return l;
    }

    static void SaveLayout()
    {
        var sb = new System.Text.StringBuilder();
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        foreach (Btn b in Buttons) sb.Append(b.id).Append(':').Append(b.x.ToString("0", inv)).Append(',').Append(b.y.ToString("0", inv)).Append(',').Append(b.size.ToString("0", inv)).Append(';');
        Settings.hudLayout = sb.ToString();
        Settings.Save();
    }

    // ---------------------------- Editor de controles ----------------------------
    public static bool Editing;
    static int sel = -1, dragId = -2;
    static Vector2 dragOff;
    static float savedAt = -99f;

    public static void BeginEdit() { Buttons = LoadLayout(); Editing = true; sel = 0; dragId = -2; }

    static readonly string[] Tools = { "-", "+", "OPACIDADE", "MIRA", "PADRAO", "CONCLUIR" };
    static Rect ToolRect(int i, float vw)
    {
        float[] w = { 90f, 90f, 260f, 300f, 190f, 230f };
        float total = 0f; foreach (float f in w) total += f + 10f;
        float x = vw / 2f - total / 2f;
        for (int k = 0; k < i; k++) x += w[k] + 10f;
        return new Rect(x, 30f, w[i], 76f);
    }

    static string ToolLabel(int i)
    {
        switch (i)
        {
            case 2: return "OPACIDADE " + Mathf.RoundToInt(Settings.hudAlpha * 100f) + "%";
            case 3: return Settings.aimHold ? "MIRA: SEGURAR" : "MIRA: TOCAR LIGA/DESLIGA";
            case 4: return "PADRAO";
            case 5: return "CONCLUIR";
        }
        return Tools[i];
    }

    static void Tool(int i)
    {
        switch (i)
        {
            case 0: if (sel >= 0) Resize(-15f); break;
            case 1: if (sel >= 0) Resize(15f); break;
            case 2: Settings.hudAlpha = Settings.hudAlpha >= 0.99f ? 0.2f : Mathf.Min(1f, Settings.hudAlpha + 0.2f); break;
            case 3: Settings.aimHold = !Settings.aimHold; break;
            case 4: Buttons = (Btn[])Defaults.Clone(); Settings.hudAlpha = 0.6f; Settings.aimHold = false; break;
            case 5:
                SaveLayout();
                Editing = false;
                if (Game.I != null) Game.I.menus.EndHudEdit();
                return;
        }
        SaveLayout();
        savedAt = Time.unscaledTime;
    }

    static void Resize(float d)
    {
        Btn b = Buttons[sel];
        float ns = Mathf.Clamp(b.size + d, 70f, 300f);
        // Cresce a partir do centro.
        b.x += (ns - b.size) / 2f; b.y += (ns - b.size) / 2f; b.size = ns;
        Buttons[sel] = b;
        Clamp(sel);
    }

    static void Clamp(int i)
    {
        float vw = Screen.width / S;
        Btn b = Buttons[i];
        float left = Mathf.Clamp(vw - b.x, 0f, vw - b.size), top = Mathf.Clamp(1080f - b.y, 120f, 1080f - b.size);
        b.x = vw - left; b.y = 1080f - top;
        Buttons[i] = b;
    }

    void EditUpdate()
    {
        float s = S, vw = Screen.width / s;
#if ENABLE_LEGACY_INPUT_MANAGER
        for (int i = 0; i < Input.touchCount; i++)
        {
            Touch t = Input.GetTouch(i);
            int id = t.fingerId; Vector2 sp = t.position;
            bool began = t.phase == TouchPhase.Began, ended = t.phase == TouchPhase.Ended || t.phase == TouchPhase.Canceled;
            Vector2 p = new Vector2(sp.x / s, (Screen.height - sp.y) / s);
            if (began)
            {
                bool tool = false;
                for (int k = 0; k < Tools.Length; k++) if (ToolRect(k, vw).Contains(p)) { Tool(k); tool = true; break; }
                if (tool || !Editing) return;
                for (int b = Buttons.Length - 1; b >= 0; b--)
                {
                    Rect r = RectOf(Buttons[b]);
                    if (!InButton(Buttons[b], p)) continue;
                    sel = b; dragId = id; dragOff = p - r.position;
                    break;
                }
            }
            else if (id == dragId && sel >= 0)
            {
                Btn b = Buttons[sel];
                Vector2 tl = p - dragOff;
                b.x = vw - tl.x; b.y = 1080f - tl.y;
                Buttons[sel] = b;
                Clamp(sel);
                if (ended) { dragId = -2; SaveLayout(); savedAt = Time.unscaledTime; }
            }
        }
#endif
    }

    void DrawEditor()
    {
        float s = S, vw = Screen.width / s;
        GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));
        Game.Fill(0f, 0f, vw, 1080f, new Color(0f, 0f, 0f, 0.55f));
        // Area do analogico (metade esquerda): onde o dedo toca vira o centro do analogico.
        Game.Fill(0f, 120f, vw * 0.4f, 960f, new Color(1f, 1f, 1f, 0.04f));
        Game.Text(30f, 1080f - 120f, vw * 0.4f - 60f, 80f, "ANALOGICO: toque em qualquer ponto livre desta area", 18, new Color(1f, 1f, 1f, 0.5f), TextAnchor.MiddleLeft);
        Game.Text(0f, 120f, vw, 60f, "Arraste os botoes para onde quiser. Toque em um botao e use - / + para o tamanho.", 20, Color.white, TextAnchor.MiddleCenter);
        if (Time.unscaledTime - savedAt < 1.5f) Game.Text(0f, 170f, vw, 40f, "salvo", 18, new Color(1f, 0.86f, 0.1f, 0.9f), TextAnchor.MiddleCenter);
        for (int i = 0; i < Buttons.Length; i++) DrawBtn(Buttons[i], false, i == sel);
        for (int k = 0; k < Tools.Length; k++)
        {
            Rect r = ToolRect(k, vw);
            bool main = k == 5;
            bool dis = k <= 1 && sel < 0;
            Game.Fill(r.x, r.y, r.width, r.height, main ? new Color(1f, 0.86f, 0.1f, 0.85f) : new Color(0.08f, 0.09f, 0.12f, 0.92f));
            Game.Text(r.x, r.y, r.width, r.height, ToolLabel(k), k <= 1 ? 34 : 18, main ? Color.black : new Color(1f, 1f, 1f, dis ? 0.3f : 1f), TextAnchor.MiddleCenter);
        }
    }

    // ---------------------------- Botoes redondos ----------------------------
    static Texture2D disc, ring;

    // Disco e anel suavizados (gerados uma vez; a cor vem do GUI.color).
    static Texture2D Circle(bool filled)
    {
        const int N = 128;
        Texture2D t = new Texture2D(N, N, TextureFormat.RGBA32, false);
        t.wrapMode = TextureWrapMode.Clamp;
        t.filterMode = FilterMode.Bilinear;
        Color32[] px = new Color32[N * N];
        float c = (N - 1) / 2f, R = N / 2f - 1f;
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
                float a = filled ? Mathf.Clamp01(R - d + 0.5f) : Mathf.Clamp01(1f - Mathf.Abs(d - (R - 3f)) / 3f);
                px[y * N + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
        t.SetPixels32(px);
        t.Apply();
        return t;
    }

    static void Round(Rect r, Color c, bool filled)
    {
        if (disc == null) { disc = Circle(true); ring = Circle(false); }
        Color old = GUI.color;
        GUI.color = c;
        GUI.DrawTexture(r, filled ? disc : ring);
        GUI.color = old;
    }

    // Recarga das habilidades (0 = pronta, 1 = acabou de usar) e carga da sobrecarga.
    static float Cooldown(string id)
    {
        PlayerController pc = Game.I != null ? Game.I.pc : null;
        if (pc == null) return 0f;
        if (id == "dash") return Mathf.Clamp01((pc.dashReady - Time.time) / PlayerController.DashCooldown);
        if (id == "nade") return Mathf.Clamp01((pc.nadeReady - Time.time) / PlayerController.NadeCooldown);
        if (id == "secondary") return Mathf.Clamp01((pc.secondaryReady - Time.time) / PlayerController.SecondaryCooldown);
        if (id == "ult") return 1f - Mathf.Clamp01(pc.ult / 100f);
        return 0f;
    }

    static void DrawBtn(Btn b, bool pressed, bool selected)
    {
        Rect r = RectOf(b);
        float k = Settings.hudAlpha / 0.6f;
        // Apertado: encolhe um pouco e acende.
        if (pressed) { float sh = r.width * 0.04f; r = new Rect(r.x + sh, r.y + sh, r.width - sh * 2f, r.height - sh * 2f); }
        bool fire = b.id == "fire";
        Color fill = fire ? new Color(1f, 0.25f, 0.2f, 1f) : new Color(0.82f, 0.86f, 0.92f, 1f);
        fill.a = Mathf.Clamp01((pressed ? 0.42f : (fire ? 0.2f : 0.08f)) * k);
        Round(r, fill, true);
        Color edge = fire ? new Color(1f, 0.35f, 0.3f, 1f) : new Color(0.8f, 0.86f, 0.95f, 1f);
        edge.a = Mathf.Clamp01((pressed ? 0.95f : 0.4f) * k + 0.05f);
        Round(r, edge, false);

        // Habilidade recarregando: escurece e mostra os segundos (sobrecarga: porcentagem).
        float cd = Editing ? 0f : Cooldown(b.id);
        string label = b.label;
        Color keyColor = b.id == "dash" ? Art.Cyan : (b.id == "nade" ? Art.Green : (b.id == "secondary" ? Art.Orange : (b.id == "ult" ? Art.Yellow : (b.id == "down" && Prone ? Art.Yellow : (b.id == "down" && Crouch ? Art.Cyan : Color.white)))));
        Color tc = new Color(keyColor.r, keyColor.g, keyColor.b, Mathf.Clamp01(Settings.hudAlpha + 0.35f));
        if (cd > 0.001f)
        {
            float inner = r.width * (0.15f + 0.85f * cd);
            Round(new Rect(r.center.x - inner / 2f, r.center.y - inner / 2f, inner, inner), new Color(0f, 0f, 0f, 0.45f), true);
            PlayerController pc = Game.I.pc;
            if (b.id == "ult") label = Mathf.FloorToInt(pc.ult) + "%";
            else label = Mathf.CeilToInt((b.id == "dash" ? pc.dashReady : (b.id == "secondary" ? pc.secondaryReady : pc.nadeReady)) - Time.time).ToString();
            tc.a *= 0.7f;
        }
        else if (!Editing && b.id == "ult") { Round(r, new Color(1f, 0.86f, 0.1f, 0.35f + 0.25f * Mathf.Sin(Time.time * 6f)), false); }
        // Arma vazia: o R pisca.
        if (!Editing && b.id == "reload" && Game.I != null && Game.I.pc != null && !Game.I.pc.reloading && Game.I.pc.mag != null && Game.I.pc.mag[Game.I.pc.cur] == 0)
            Round(r, new Color(1f, 0.3f, 0.2f, 0.4f + 0.4f * Mathf.Sin(Time.time * 8f)), false);

        if (selected)
        {
            Rect s2 = new Rect(r.x - 8f, r.y - 8f, r.width + 16f, r.height + 16f);
            Round(s2, new Color(1f, 0.86f, 0.1f, 1f), false);
        }
        if (fire)
        {
            // Icone de mira no TIRO.
            Vector2 c = r.center; float L = r.width * 0.16f, g = r.width * 0.07f;
            Color ic = new Color(1f, 1f, 1f, tc.a * 0.8f);
            Game.Fill(c.x - g - L, c.y - 1.5f, L, 3f, ic); Game.Fill(c.x + g, c.y - 1.5f, L, 3f, ic);
            Game.Fill(c.x - 1.5f, c.y - g - L, 3f, L, ic); Game.Fill(c.x - 1.5f, c.y + g, 3f, L, ic);
            Game.Text(r.x, r.y + r.height * 0.62f, r.width, r.height * 0.3f, label, 20, tc, TextAnchor.MiddleCenter);
        }
        else Game.Text(r.x, r.y, r.width, r.height, label, b.size > 150f ? 30 : (label.Length > 3 ? 20 : 24), tc, TextAnchor.MiddleCenter);
    }

    // Toque dentro do circulo (com 12% de folga, para nao errar o botao na borda).
    static bool InButton(Btn b, Vector2 p)
    {
        Rect r = RectOf(b);
        float rad = r.width / 2f * 1.12f;
        return (p - r.center).sqrMagnitude <= rad * rad;
    }

    int stickId = -1, lookId = -1, fireId = -1, jumpId = -1, useId = -1, downId = -1, aimId = -1, talkId = -1;
    Vector2 stickStart, stickNow;
    bool aimToggle, movedOnce;
    Vector2 lookPrev;

    void Awake() { useGUILayout = false; }

    static float S { get { return Screen.height / 1080f; } }

    static Rect RectOf(Btn b)
    {
        float vw = Screen.width / S;
        return new Rect(vw - b.x, 1080f - b.y, b.size, b.size);
    }

    void Update()
    {
        PingTap = false;
        FireDown = JumpDown = UseDown = ReloadDown = DashDown = GrenadeDown = SecondaryDown = UltDown = PauseDown = NextWeapon = false;
        SelectSlot = -1;
        Look = Vector2.zero;
        if (!On) return;
        if (Editing) { Move = Vector2.zero; Fire = Jump = Use = Down = Aim = false; EditUpdate(); return; }
        bool playing = Game.I != null && Game.I.state != Game.State.Menu && !Game.I.menus.IsOpen && !Game.I.matchOver;
        if (!playing) { lookPrev = Vector2.zero; stickId = lookId = fireId = jumpId = useId = downId = aimId = talkId = -1; downLongTriggered = false; Talk = false; Move = Vector2.zero; Fire = Jump = Use = Down = false; aimToggle = false; Aim = false; return; }
        if (Game.I.player != null && !Game.I.player.alive) { Crouch = false; Prone = false; }
        bool inVeh = Game.I.player != null && Game.I.player.vehicle != null;
#if ENABLE_LEGACY_INPUT_MANAGER
        float s = S, vw = Screen.width / s;
        for (int i = 0; i < Input.touchCount; i++)
        {
            Touch t = Input.GetTouch(i);
            Vector2 p = new Vector2(t.position.x / s, (Screen.height - t.position.y) / s);   // mesmo espaco do HUD
            if (t.phase == TouchPhase.Began)
            {
                // Pausa e placar (canto superior direito)
                if (p.y < 110f && p.x > vw - 130f) { PauseDown = true; continue; }
                if (p.y < 110f && p.x > vw - 270f) { Score = !Score; continue; }
                if (p.y < 110f && p.x > vw - 400f) { ShotDown = true; continue; }
                // Barra de armas (centro, em baixo)
                int slot = SlotAt(p, vw);
                if (slot >= 0) { SelectSlot = slot; continue; }
                // Pular etapa do tutorial
                if (SkipRect(vw).Contains(p) && Game.I.tutorial != null) { Game.I.tutorial.SkipStep(); continue; }
                string hit = null;
                for (int b = 0; b < Buttons.Length; b++) if ((Buttons[b].id != "talk" || VoiceChat.Active) && InButton(Buttons[b], p)) { hit = Buttons[b].id; break; }
                if (hit == "fire") { fireId = t.fingerId; FireDown = true; }
                else if (hit == "aim") { if (Settings.aimHold) aimId = t.fingerId; else aimToggle = !aimToggle; }
                else if (hit == "jump") { jumpId = t.fingerId; JumpDown = true; }
                else if (hit == "reload") ReloadDown = true;
                else if (hit == "weapon") NextWeapon = true;
                else if (hit == "use") { useId = t.fingerId; UseDown = true; }
                else if (hit == "dash") DashDown = true;
                else if (hit == "nade") GrenadeDown = true;
                else if (hit == "secondary") SecondaryDown = true;
                else if (hit == "ult") UltDown = true;
                else if (hit == "talk") talkId = t.fingerId;
                else if (hit == "down")
                {
                    downId = t.fingerId;
                    downHoldStart = Time.unscaledTime;
                    downLongTriggered = false;
                }
                else if (p.x < vw * 0.4f && stickId < 0) { stickId = t.fingerId; stickStart = stickNow = p; }
                else if (lookId < 0) { lookId = t.fingerId; lookStart = Time.unscaledTime; lookFrom = p; lookTravel = 0f; }
            }
            bool ended = t.phase == TouchPhase.Ended || t.phase == TouchPhase.Canceled;
            if (t.fingerId == stickId) { stickNow = p; if (ended) stickId = -1; }
            // Arrastar com o dedo do tiro tambem gira a mira.
            if ((t.fingerId == lookId || t.fingerId == fireId) && t.phase == TouchPhase.Moved) Look += t.deltaPosition / s * 0.12f;
            if (t.fingerId == lookId) lookTravel = Mathf.Max(lookTravel, (p - lookFrom).magnitude);
            if (ended)
            {
                if (t.fingerId == lookId)
                {
                    // Toque rapido sem arrastar = marcar o que esta embaixo do dedo.
                    if (t.phase == TouchPhase.Ended && Time.unscaledTime - lookStart < 0.25f && lookTravel < 18f) { PingTap = true; PingPos = t.position; }
                    lookId = -1;
                }
                if (t.fingerId == fireId) fireId = -1;
                if (t.fingerId == jumpId) jumpId = -1;
                if (t.fingerId == useId) useId = -1;
                if (t.fingerId == downId)
                {
                    if (!inVeh && !downLongTriggered)
                    {
                        if (Prone) { Prone = false; Crouch = true; }
                        else Crouch = !Crouch;
                    }
                    downId = -1;
                    downLongTriggered = false;
                }
                if (t.fingerId == talkId) talkId = -1;
                if (t.fingerId == aimId) aimId = -1;
            }
        }
        if (downId >= 0 && !inVeh && !downLongTriggered && Time.unscaledTime - downHoldStart >= 0.38f)
        {
            Prone = !Prone;
            if (Prone) Crouch = false;
            downLongTriggered = true;
        }
        if (Input.touchCount == 0) { stickId = lookId = fireId = jumpId = useId = downId = aimId = talkId = -1; downLongTriggered = false; }
#endif
        // Curva nao-linear de mira no toque (precisao em micro-arrastes + flick rapido) e suavizacao exponencial por dt (sem lag de 1 frame).
        float dt = Mathf.Max(Time.unscaledDeltaTime, 0.001f);
        float mag = Look.magnitude;
        float accel = mag > 0.0001f ? Mathf.Lerp(0.65f, 1.85f, Mathf.Pow(Mathf.Clamp01(mag / 24f), 1.35f)) : 1f;
        Vector2 raw = Look * accel * (Aim ? 0.55f : 1f);

        bool useGyro = SystemInfo.supportsGyroscope && Settings.gyroMode > 0 && (Settings.gyroMode == 2 || Aim);
        if (Input.gyro.enabled != useGyro) Input.gyro.enabled = useGyro;
        if (useGyro)
        {
            Vector3 gr = Input.gyro.rotationRateUnbiased;
            float gScale = Settings.gyroSens * dt * Mathf.Rad2Deg * 0.35f;
            raw += new Vector2(-gr.y * gScale, gr.x * gScale);
        }

        lookPrev = Vector2.Lerp(lookPrev, raw, 1f - Mathf.Exp(-32f * dt));
        Look = raw.sqrMagnitude > 0.00001f ? Vector2.Lerp(raw, lookPrev, 0.35f) : Vector2.zero;

        Vector2 d = stickId >= 0 ? (stickNow - stickStart) / 110f : Vector2.zero;
        d.y = -d.y;
        float m = d.magnitude;
        // Zona morta pequena e resposta suave perto do centro.
        Move = m < 0.12f ? Vector2.zero : d / m * Mathf.Clamp01((m - 0.12f) / 0.88f);
        if (stickId < 0) SprintLock = false;
        else if (Move.y > 0.96f && Mathf.Abs(Move.x) < 0.35f) SprintLock = true;
        else if (Move.y < 0.82f) SprintLock = false;
        if (Move != Vector2.zero) movedOnce = true;
        Fire = fireId >= 0; Jump = jumpId >= 0; Use = useId >= 0; Down = downId >= 0; Talk = talkId >= 0;
        Aim = Settings.aimHold ? aimId >= 0 : aimToggle;
    }

    const float SlotW = 112f, SlotH = 62f, SlotGap = 8f;

    static Rect SlotRect(int i, float vw)
    {
        int n = Weapons.All.Length;
        float total = n * SlotW + (n - 1) * SlotGap;
        return new Rect(vw / 2f - total / 2f + i * (SlotW + SlotGap), 168f, SlotW, SlotH);   // topo, abaixo do placar
    }

    static int SlotAt(Vector2 p, float vw)
    {
        for (int i = 0; i < Weapons.All.Length; i++) if (SlotRect(i, vw).Contains(p)) return i;
        return -1;
    }

    // Ao lado do quadro do tutorial (que fica no topo, no centro).
    static Rect SkipRect(float vw) { return new Rect(vw / 2f + 440f, 40f, 170f, 80f); }

    void OnGUI()
    {
        if (On && Editing) { Game.EnsureStyle(); DrawEditor(); return; }
        if (!On || Game.I == null || Game.I.state == Game.State.Menu || Game.I.menus.IsOpen || Game.I.matchOver) return;
        Game.EnsureStyle();
        float s = S;
        GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));
        float vw = Screen.width / s;
        bool veh = Game.I.player != null && Game.I.player.vehicle != null;
        for (int i = 0; i < Buttons.Length; i++)
        {
            Btn b = Buttons[i];
            if (b.id == "talk" && !VoiceChat.Active) continue;
            if (b.id == "secondary" && (Game.I.player == null || !AgentAbilities.HasSecondarySkill(Game.I.player.agent))) continue;
            bool on = (b.id == "talk" && Talk) || (b.id == "fire" && Fire) || (b.id == "aim" && Aim) || (b.id == "jump" && Jump) || (b.id == "use" && Use) || (b.id == "down" && (veh ? Down : (Crouch || Prone))) || (b.id == "dash" && DashDown) || (b.id == "nade" && GrenadeDown) || (b.id == "secondary" && SecondaryDown) || (b.id == "ult" && UltDown);
            if (b.id == "down") b.label = veh ? "DESCE" : (Prone ? "DEITADO" : (Crouch ? "AGACHADO" : "AGACHAR"));
            DrawBtn(b, on, false);
        }
        // Barra de armas: toque para trocar
        if (!veh && Game.I.pc != null)
        {
            for (int i = 0; i < Weapons.All.Length; i++)
            {
                Rect r = SlotRect(i, vw);
                bool cur = Game.I.pc.cur == i;
                Game.Fill(r.x, r.y, r.width, r.height, cur ? new Color(1f, 0.86f, 0.1f, 0.55f) : new Color(0f, 0f, 0f, 0.45f));
                string top = (i + 1).ToString();
                if (cur) top = Game.I.pc.Overcharged ? "--" : Game.I.pc.mag[i] + " / " + Game.I.pc.reserve[i];   // municao na arma em uso
                Game.Text(r.x, r.y + 2f, r.width, 22f, top, cur ? 16 : 14, cur ? Color.white : new Color(1f, 1f, 1f, 0.6f), TextAnchor.MiddleCenter);
                Game.Text(r.x, r.y + 22f, r.width, 36f, Weapons.All[i].name, 16, Color.white, TextAnchor.MiddleCenter);
            }
        }
        if (Game.I.tutorial != null && !Game.I.tutorial.StepDone)
        {
            Rect k = SkipRect(vw);
            Game.Fill(k.x, k.y, k.width, k.height, new Color(0f, 0f, 0f, 0.5f));
            Game.Fill(k.x, k.y, 4f, k.height, new Color(1f, 0.86f, 0.1f, 0.9f));
            Game.Text(k.x, k.y, k.width, k.height, "PULAR ETAPA", 18, Color.white, TextAnchor.MiddleCenter);
        }
        Round(new Rect(vw - 110f, 20f, 82f, 82f), new Color(0f, 0f, 0f, 0.45f), true);
        Round(new Rect(vw - 110f, 20f, 82f, 82f), new Color(1f, 1f, 1f, 0.5f), false);
        Game.Text(vw - 110f, 20f, 82f, 82f, "II", 28, Color.white, TextAnchor.MiddleCenter);
        Game.Fill(vw - 260f, 20f, 130f, 80f, new Color(0f, 0f, 0f, 0.45f));
        Game.Text(vw - 260f, 20f, 130f, 80f, "PLACAR", 18, Color.white, TextAnchor.MiddleCenter);
        Game.Fill(vw - 390f, 20f, 120f, 80f, new Color(0f, 0f, 0f, 0.45f));
        Game.Text(vw - 390f, 20f, 120f, 80f, "FOTO", 18, Color.white, TextAnchor.MiddleCenter);
        if (stickId >= 0)
        {
            Round(new Rect(stickStart.x - 110f, stickStart.y - 110f, 220f, 220f), new Color(1f, 1f, 1f, 0.1f), true);
            Round(new Rect(stickStart.x - 110f, stickStart.y - 110f, 220f, 220f), new Color(1f, 1f, 1f, 0.35f), false);
            Vector2 k = stickStart + Vector2.ClampMagnitude(stickNow - stickStart, 110f);
            Round(new Rect(k.x - 48f, k.y - 48f, 96f, 96f), new Color(1f, 1f, 1f, 0.38f), true);
            Round(new Rect(k.x - 48f, k.y - 48f, 96f, 96f), new Color(1f, 0.86f, 0.1f, 0.6f), false);
            if (SprintLock) Round(new Rect(stickStart.x - 10f, stickStart.y - 118f, 20f, 20f), Art.Yellow, true);
        }
        else if (!movedOnce) Game.Text(60f, 1080f - 260f, 360f, 40f, "arraste aqui para andar", 16, new Color(1f, 1f, 1f, 0.35f), TextAnchor.MiddleLeft);
    }
}
