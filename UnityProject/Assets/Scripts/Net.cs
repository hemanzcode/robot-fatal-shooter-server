using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

[Serializable] public class NetPlayer { public int id; public string name; public int team; public int agent; public bool away; public int lvl; public string[] equip; }
[Serializable] public class NetFriend { public string uid, name, code, map, mode; public int lvl, st, n; }   // st: 0 offline, 1 no menu, 2 em sala
[Serializable] public class NetPack { public string id; public int shots; public string price; }
[Serializable] public class NetRow { public string map; public string mode; public int rooms; public int players; }
[Serializable] public class NetOpen { public string code; public string map; public string mode; public string host; public int players; public bool bots; public bool custom; }
[Serializable]
public class NetMsg
{
    public string t, name, code, map, mode, err, d, token, resume;
    public int id, host, from, to, agent, online;
    public bool priv, bots, resumed;
    public NetPlayer[] players;
    public NetRow[] rooms;
    public NetOpen[] open;
    // Progresso (perfil, recompensa, PIX)
    public int xp, lvl, lvlXp, lvlNeed, shots, lastXp, lastShots, lvlUp;
    public string rank, qr, price, pid;
    public string[] owned, equip, lines;
    public bool pix;
    public NetPack[] packs;
    // Amigos
    public NetFriend[] fr, inc, sent;
    public bool ok;
    [NonSerialized] public float at;
}
[Serializable] class AuthMeta { public string username; }
[Serializable] class AuthUser { public string id; public string email; public AuthMeta user_metadata; }
[Serializable]
class AuthResp
{
    public string access_token, refresh_token, error_description, msg, error_code, error;
    public AuthUser user;
    // Cadastro com confirmacao de e-mail devolve o usuario direto na raiz.
    public string id, email;
}

// Conta (Supabase), conexao com o servidor de salas e estado do lobby.
public class Net : MonoBehaviour
{
    public static Net I;
    public string userName = "", status = "", error = "";
    public bool loggedIn, connected, busy;
    public int myId, online;
    public string roomCode = "", roomMap = "", roomMode = "";
    public bool roomPriv, roomBots = true;
    public int roomHost;
    public NetPlayer[] players = new NetPlayer[0];
    public NetRow[] rows = new NetRow[0];
    public NetOpen[] open = new NetOpen[0];
    public bool InRoom { get { return roomCode != ""; } }
    public bool IsHost { get { return InRoom && roomHost == myId; } }

    string token = "";
    ClientWebSocket ws;
    CancellationTokenSource cts;
    readonly ConcurrentQueue<string> inbox = new ConcurrentQueue<string>();
    readonly ConcurrentQueue<string> outbox = new ConcurrentQueue<string>();
    readonly SemaphoreSlim outSignal = new SemaphoreSlim(0);
    volatile bool socketDown;

#if UNITY_WEBGL && !UNITY_EDITOR
    [System.Runtime.InteropServices.DllImport("__Internal")] static extern void RfsWsConnect(string url);
    [System.Runtime.InteropServices.DllImport("__Internal")] static extern int RfsWsState();
    [System.Runtime.InteropServices.DllImport("__Internal")] static extern void RfsWsSend(string msg);
    [System.Runtime.InteropServices.DllImport("__Internal")] static extern IntPtr RfsWsRecv();
    [System.Runtime.InteropServices.DllImport("__Internal")] static extern void RfsFree(IntPtr p);
    [System.Runtime.InteropServices.DllImport("__Internal")] static extern void RfsWsClose();
    [System.Runtime.InteropServices.DllImport("__Internal")] static extern void RfsShare(string text, string url);
    bool webOpen, webActive;
    readonly List<string> webPending = new List<string>();
#endif

    // ---------------------------- Convite por link ----------------------------
    // Link: .../play/?sala=CODIGO. Quem abre entra direto na sala depois do login.
    static string invite;
    static bool inviteRead;
    public static string InviteCode
    {
        get { if (!inviteRead) { inviteRead = true; invite = ReadInvite(); } return invite; }
        set { inviteRead = true; invite = value; }
    }
    public static float copiedAt = -99f;
    const string PublicPlayUrl = "https://robot-fatal-shooter.onrender.com/play/";

    static string BaseUrl()
    {
        string u = Application.absoluteURL ?? "";
        if (!u.StartsWith("http")) return PublicPlayUrl;
        int q = u.IndexOfAny(new[] { '?', '#' });
        return q >= 0 ? u.Substring(0, q) : u;
    }

    static string ReadInvite()
    {
        string u = Application.absoluteURL ?? "";
        int i = u.IndexOf("sala=", StringComparison.OrdinalIgnoreCase);
        if (i < 0) return "";
        string c = "";
        for (int k = i + 5; k < u.Length && c.Length < 5 && char.IsLetterOrDigit(u[k]); k++) c += char.ToUpperInvariant(u[k]);
        return c.Length == 5 ? c : "";
    }

    public string InviteLink { get { return BaseUrl() + "?sala=" + roomCode; } }

    public void ShareInvite()
    {
        if (!InRoom) return;
        string text = "Bora jogar Fatal Shooter! Sala " + roomCode + " (" + roomMode + "):";
        copiedAt = Time.unscaledTime;
#if UNITY_WEBGL && !UNITY_EDITOR
        RfsShare(text, InviteLink);
#else
        GUIUtility.systemCopyBuffer = InviteLink;
#endif
    }

    public static Net Ensure()
    {
        if (I != null) return I;
        GameObject g = new GameObject("Net");
        DontDestroyOnLoad(g);
        I = g.AddComponent<Net>();
        return I;
    }

    public static bool Configured { get { return NetConfig.ServerUrl.StartsWith("ws") && NetConfig.SupabaseUrl.StartsWith("http"); } }

    void Start()
    {
        // Login automatico com a sessao salva.
        string rt = PlayerPrefs.GetString("na_refresh", "");
        if (rt != "" && Configured) StartCoroutine(Auth("token?grant_type=refresh_token", "{\"refresh_token\":\"" + Esc(rt) + "\"}", false));
    }

    static string Esc(string s) { return (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\""); }

    // ---------------------------- Conta ----------------------------

    public void Login(string email, string pass)
    {
        if (busy) return;
        StartCoroutine(Auth("token?grant_type=password", "{\"email\":\"" + Esc(email.Trim()) + "\",\"password\":\"" + Esc(pass) + "\"}", false));
    }

    public void SignUp(string email, string user, string pass)
    {
        if (busy) return;
        user = user.Trim();
        if (user.Length < 3) { error = "Escolha um nome de jogador com 3 letras ou mais."; return; }
        if (pass.Length < 6) { error = "A senha precisa de 6 caracteres ou mais."; return; }
        StartCoroutine(Auth("signup", "{\"email\":\"" + Esc(email.Trim()) + "\",\"password\":\"" + Esc(pass) + "\",\"data\":{\"username\":\"" + Esc(user) + "\"}}", true));
    }

    public void Logout()
    {
        PlayerPrefs.DeleteKey("na_refresh");
        loggedIn = false; token = ""; userName = "";
        Skins.ClearServer();
        friends = new NetFriend[0]; friendReqs = new NetFriend[0]; friendSent = new NetFriend[0]; invites.Clear(); friendsLoaded = false;
        Disconnect();
    }

    IEnumerator Auth(string path, string body, bool signup)
    {
        busy = true; error = ""; status = signup ? "Criando conta..." : "Entrando...";
        using (UnityWebRequest rq = new UnityWebRequest(NetConfig.SupabaseUrl + "/auth/v1/" + path, "POST"))
        {
            rq.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
            rq.downloadHandler = new DownloadHandlerBuffer();
            rq.SetRequestHeader("Content-Type", "application/json");
            rq.SetRequestHeader("apikey", NetConfig.SupabaseKey);
            rq.timeout = 20;
            yield return rq.SendWebRequest();
            busy = false; status = "";
            AuthResp r = null;
            try { r = JsonUtility.FromJson<AuthResp>(rq.downloadHandler.text); } catch (Exception) { }
            if (r != null && !string.IsNullOrEmpty(r.access_token))
            {
                token = r.access_token;
                PlayerPrefs.SetString("na_refresh", r.refresh_token ?? "");
                PlayerPrefs.Save();
                userName = r.user != null && r.user.user_metadata != null && !string.IsNullOrEmpty(r.user.user_metadata.username) ? r.user.user_metadata.username : "JOGADOR";
                loggedIn = true;
                Connect();
            }
            else if (signup && rq.result == UnityWebRequest.Result.Success)
                error = "Conta criada. Confirme pelo link enviado ao seu e-mail e depois clique em ENTRAR.";
            else
            {
                string m = r != null ? (r.error_description ?? r.msg ?? r.error ?? "") : "";
                if (m.Contains("Invalid login")) m = "E-mail ou senha incorretos.";
                else if (m.Contains("already registered")) m = "Esse e-mail ja tem conta. Use ENTRAR.";
                else if (m.Contains("Refresh Token") || path.Contains("refresh_token")) { m = ""; PlayerPrefs.DeleteKey("na_refresh"); }
                else if (m == "") m = rq.result == UnityWebRequest.Result.ConnectionError ? "Sem conexao com o servidor de contas." : "Nao foi possivel entrar (" + rq.responseCode + ").";
                error = m;
            }
        }
    }

    // ---------------------------- Conexao ----------------------------

#if UNITY_WEBGL && !UNITY_EDITOR
    // Navegador: WebSocket do proprio browser (plugin rfs.jslib), consultado a cada quadro.
    public void Connect()
    {
        if (!loggedIn || webActive) return;
        DropSocket();
        status = "Conectando ao servidor de salas (pode levar ate 1 min se ele estiver dormindo)...";
        webActive = true; webOpen = false;
        webPending.Clear();
        webPending.Add(Hello());
        RfsWsConnect(NetConfig.ServerUrl);
    }

    public void Disconnect() { reconnecting = false; DropSocket(); ClearRoom(); }

    // Fecha so o socket (a sala fica guardada para a reconexao).
    void DropSocket()
    {
        if (webActive) RfsWsClose();
        webActive = false; webOpen = false;
        webPending.Clear();
        connected = false;
    }

    void Raw(string json)
    {
        if (!webActive) return;
        if (webOpen) RfsWsSend(json); else webPending.Add(json);
    }

    void Pump()
    {
        if (!webActive) return;
        int st = RfsWsState();
        if (st == 1 && !webOpen)
        {
            webOpen = true;
            for (int i = 0; i < webPending.Count; i++) RfsWsSend(webPending[i]);
            webPending.Clear();
        }
        if (st == 2) { webActive = false; RfsWsClose(); socketDown = true; return; }
        for (int n = 0; n < 400; n++)
        {
            IntPtr p = RfsWsRecv();
            if (p == IntPtr.Zero) break;
            string m = System.Runtime.InteropServices.Marshal.PtrToStringUTF8(p);
            RfsFree(p);
            inbox.Enqueue(m);
        }
    }
#else
    public void Connect()
    {
        if (!loggedIn || (ws != null && !socketDown)) return;
        DropSocket();
        socketDown = false;
        status = "Conectando ao servidor de salas (pode levar ate 1 min se ele estiver dormindo)...";
        cts = new CancellationTokenSource();
        ws = new ClientWebSocket();
        ClientWebSocket sock = ws; CancellationToken ct = cts.Token;
        string hello = Hello();
        Task.Run(async () =>
        {
            try
            {
                await sock.ConnectAsync(new Uri(NetConfig.ServerUrl), ct);
                outbox.Enqueue(hello); outSignal.Release();
                Task sender = Task.Run(async () =>
                {
                    while (!ct.IsCancellationRequested && sock.State == WebSocketState.Open)
                    {
                        await outSignal.WaitAsync(ct);
                        string s;
                        while (outbox.TryDequeue(out s))
                            await sock.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(s)), WebSocketMessageType.Text, true, ct);
                    }
                });
                byte[] buf = new byte[65536];
                StringBuilder sb = new StringBuilder();
                while (!ct.IsCancellationRequested && sock.State == WebSocketState.Open)
                {
                    WebSocketReceiveResult res = await sock.ReceiveAsync(new ArraySegment<byte>(buf), ct);
                    if (res.MessageType == WebSocketMessageType.Close) break;
                    sb.Append(Encoding.UTF8.GetString(buf, 0, res.Count));
                    if (res.EndOfMessage) { inbox.Enqueue(sb.ToString()); sb.Length = 0; }
                }
            }
            catch (Exception) { }
            if (!ct.IsCancellationRequested) socketDown = true;
        });
    }

    public void Disconnect() { reconnecting = false; DropSocket(); ClearRoom(); }

    // Fecha so o socket (a sala fica guardada para a reconexao).
    void DropSocket()
    {
        if (cts != null) { try { cts.Cancel(); } catch (Exception) { } }
        if (ws != null) { try { ws.Abort(); ws.Dispose(); } catch (Exception) { } }
        ws = null; cts = null;
        connected = false;
        string junk; while (outbox.TryDequeue(out junk)) { }
    }

    void Raw(string json) { if (ws == null) return; outbox.Enqueue(json); outSignal.Release(); }

    void Pump() { }
#endif

    void ClearRoom() { roomCode = ""; players = new NetPlayer[0]; roomHost = 0; }

    // ---------------------------- Reconexao ----------------------------
    // Saiu do app (ex.: mandar o link no WhatsApp) ou o sinal caiu: o servidor guarda a vaga por 60 s.
    // Ao voltar, o jogo reconecta sozinho com a "ficha" e continua na mesma sala.
    string resumeToken = "";
    public bool reconnecting;
    float reconnectUntil, nextRetry;

    string Hello()
    {
        return "{\"t\":\"hello\",\"token\":\"" + Esc(token) + "\",\"agent\":" + Settings.agent +
               (reconnecting && resumeToken != "" ? ",\"resume\":\"" + Esc(resumeToken) + "\"" : "") + "}";
    }

    bool SocketIdle
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        get { return !webActive; }
#else
        get { return ws == null || socketDown; }
#endif
    }

    // Dono da sala ausente (saiu do app): os outros esperam ele voltar.
    public bool HostAway
    {
        get
        {
            if (!InRoom || roomHost == myId) return false;
            for (int i = 0; i < players.Length; i++) if (players[i].id == roomHost) return players[i].away;
            return false;
        }
    }

    void OnGUI()
    {
        if (!NetGame.Online || !InRoom) return;
        string msg = null;
        if (reconnecting) msg = "CONEXAO CAIU - RECONECTANDO... (" + Mathf.Max(0, Mathf.CeilToInt(reconnectUntil - Time.unscaledTime)) + " s)";
        else if (HostAway)
        {
            string hn = "o dono da sala";
            for (int i = 0; i < players.Length; i++) if (players[i].id == roomHost) hn = players[i].name;
            msg = "AGUARDANDO " + hn + " VOLTAR PARA A PARTIDA...";
        }
        if (msg == null) return;
        Game.EnsureStyle();
        float s = Screen.height / 1080f, vw = Screen.width / s;
        GUI.depth = -20;
        GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));
        Game.Fill(vw / 2f - 520f, 300f, 1040f, 90f, new Color(0f, 0f, 0f, 0.8f));
        Game.Fill(vw / 2f - 520f, 300f, 1040f, 4f, new Color(1f, 0.86f, 0.1f, 1f));
        Game.Text(vw / 2f - 520f, 300f, 1040f, 90f, msg, 26, Color.white, TextAnchor.MiddleCenter);
    }

    void OnDestroy() { Disconnect(); }
    void OnApplicationQuit() { Disconnect(); }


    public void Quick(string map, string mode) { error = ""; status = "Procurando partida..."; Raw("{\"t\":\"quick\",\"map\":\"" + Esc(map) + "\",\"mode\":\"" + Esc(mode) + "\",\"side\":" + Settings.side + "}"); }
    public void Create(string map, string mode, bool priv = true, bool bots = true)
    {
        error = ""; status = "Criando sala...";
        Raw("{\"t\":\"create\",\"map\":\"" + Esc(map) + "\",\"mode\":\"" + Esc(mode) + "\",\"side\":" + Settings.side +
            ",\"priv\":" + (priv ? "true" : "false") + ",\"bots\":" + (bots ? "true" : "false") + "}");
    }
    public void Join(string code) { error = ""; status = "Entrando na sala..."; Raw("{\"t\":\"join\",\"code\":\"" + Esc(code.Trim()) + "\",\"side\":" + Settings.side + "}"); }
    public void Leave() { if (InRoom) Raw("{\"t\":\"leave\"}"); ClearRoom(); }
    public void RefreshList() { Raw("{\"t\":\"list\"}"); }
    // Campanha: pede uma fala por IA para um personagem (o servidor responde com npcline).
    public void NpcAsk(string who, string ev, string ctx, string say, int id)
    {
        Raw("{\"t\":\"npc\",\"who\":\"" + Esc(who) + "\",\"ev\":\"" + Esc(ev) + "\",\"ctx\":\"" + Esc(ctx) + "\",\"say\":\"" + Esc(say) + "\",\"rid\":" + id + "}");
    }

    public void SendAgent() { Raw("{\"t\":\"agent\",\"agent\":" + Settings.agent + "}"); }

    public void Send(string json) { Raw(json); }
    // Loja e progresso (o servidor confere preco e saldo e devolve o perfil novo).
    public void Buy(string skin, int w) { Raw("{\"t\":\"buy\",\"skin\":\"" + Esc(skin) + "\",\"w\":" + w + "}"); }
    public void EquipSkin(string skin, int w) { Raw("{\"t\":\"equip\",\"skin\":\"" + Esc(skin) + "\",\"w\":" + w + "}"); }
    public void RequestProfile() { Raw("{\"t\":\"profile\"}"); }
    public void RequestPix(string pack) { Raw("{\"t\":\"pix\",\"pack\":\"" + Esc(pack) + "\"}"); }
    public void CheckPix(string id) { Raw("{\"t\":\"pixcheck\",\"id\":\"" + Esc(id) + "\"}"); }
    public NetMsg reward;          // ultima recompensa de partida (tela de fim)

    // Amigos: lista (com quem esta online/em sala), pedidos recebidos e enviados, convites.
    public NetFriend[] friends = new NetFriend[0], friendReqs = new NetFriend[0], friendSent = new NetFriend[0];
    public string friendMsg = ""; public bool friendOk; public float friendMsgAt = -99f;
    public readonly List<NetMsg> invites = new List<NetMsg>();
    public bool friendsLoaded;
    public int FriendsOnline { get { int c = 0; foreach (NetFriend f in friends) if (f.st > 0) c++; return c; } }
    public void AddFriend(string name) { if (name.Trim() == "") return; Raw("{\"t\":\"fadd\",\"name\":\"" + Esc(name.Trim()) + "\"}"); }
    public void AcceptFriend(string uid) { Raw("{\"t\":\"fok\",\"uid\":\"" + Esc(uid) + "\"}"); }
    public void RemoveFriend(string uid) { Raw("{\"t\":\"fno\",\"uid\":\"" + Esc(uid) + "\"}"); }
    public void InviteFriend(string uid) { Raw("{\"t\":\"finv\",\"uid\":\"" + Esc(uid) + "\"}"); }
    public void RequestFriends() { Raw("{\"t\":\"friends\"}"); }
    // Aceitar convite: sai da partida atual (se estiver em uma) e entra na sala do amigo.
    public void AcceptInvite(NetMsg inv)
    {
        invites.Remove(inv);
        if (InRoom && roomCode == inv.code) return;
        if (InRoom && Game.I != null) Game.I.ToMenu();
        Join(inv.code);
    }
    void FriendNote(string msg, bool ok) { friendMsg = msg; friendOk = ok; friendMsgAt = Time.unscaledTime; }

    // Mensagem de jogo: to = -1 host, 0 todos os outros, >0 um jogador.
    public void SendGame(int to, string d) { if (InRoom && !reconnecting) Raw("{\"t\":\"g\",\"to\":" + to + ",\"d\":\"" + Esc(d) + "\"}"); }

    float nextList;

    void Update()
    {
        Pump();
        if (socketDown && InRoom && NetGame.Online && resumeToken != "")
        {
            // Caiu no meio da partida: tenta voltar para a mesma sala.
            socketDown = false;
            DropSocket();
            if (!reconnecting) { reconnecting = true; reconnectUntil = Time.unscaledTime + 55f; }
            nextRetry = Time.unscaledTime + 1f;
        }
        if (reconnecting && SocketIdle && Time.unscaledTime >= nextRetry)
        {
            if (Time.unscaledTime > reconnectUntil)
            {
                Disconnect();
                error = "Nao foi possivel voltar para a sala.";
                if (NetGame.Online && Game.I != null) Game.I.ToMenu();
            }
            else { socketDown = false; Connect(); nextRetry = Time.unscaledTime + 4f; }
        }
        if (socketDown)
        {
            Disconnect();
            socketDown = false;
            error = "Conexao com o servidor caiu.";
            status = "";
            if (NetGame.Online && Game.I != null) Game.I.ToMenu();
        }
        string s;
        int guard = 0;
        while (guard++ < 400 && inbox.TryDequeue(out s))
        {
            NetMsg m = null;
            try { m = JsonUtility.FromJson<NetMsg>(s); } catch (Exception) { }
            if (m == null || m.t == null) continue;
            try { Handle(m); } catch (Exception e) { Debug.LogError("Net: " + e); }
        }
        if (connected && !InRoom && Time.unscaledTime >= nextList) { nextList = Time.unscaledTime + 4f; RefreshList(); }
    }

    void Handle(NetMsg m)
    {
        switch (m.t)
        {
            case "welcome":
                connected = true; myId = m.id; userName = m.name; status = "";
                if (!string.IsNullOrEmpty(m.resume)) resumeToken = m.resume;
                if (reconnecting)
                {
                    reconnecting = false;
                    if (!m.resumed && InRoom)
                    {
                        ClearRoom();
                        error = "Voce ficou fora mais de 1 minuto e saiu da sala.";
                        if (NetGame.Online && Game.I != null) Game.I.ToMenu();
                    }
                }
                if (InviteCode != "" && !InRoom) { string c = InviteCode; InviteCode = ""; Join(c); }
                break;
            case "list": rows = m.rooms ?? new NetRow[0]; open = m.open ?? new NetOpen[0]; online = m.online; break;
            case "error": error = m.err ?? "Erro."; status = ""; break;
            case "closed":
                ClearRoom();
                error = m.err ?? "A sala fechou.";
                if (Game.I != null && NetGame.Online) Game.I.ToMenu();
                break;
            case "room":
                bool entering = !InRoom;
                roomCode = m.code; roomMap = m.map; roomMode = m.mode; roomPriv = m.priv; roomBots = m.bots; roomHost = m.host;
                players = m.players ?? new NetPlayer[0];
                status = "";
                if (entering && Game.I != null) Game.I.StartOnline();
                else if (NetGame.I != null) { NetGame.I.HostCheck(); NetGame.I.RosterChanged(); }
                break;
            case "g": if (NetGame.I != null && m.d != null) NetGame.I.Receive(m.from, m.d); break;
            case "profile": Skins.FromServer(m); break;
            case "reward": reward = m; break;
            case "shoperr": Skins.ServerError(m.err ?? "Erro."); break;
            case "pix": Skins.pix = m; Skins.pixPaid = 0; break;
            case "pixok": Skins.pixPaid = m.shots; break;
            case "friends":
                friends = m.fr ?? new NetFriend[0]; friendReqs = m.inc ?? new NetFriend[0]; friendSent = m.sent ?? new NetFriend[0];
                friendsLoaded = true;
                break;
            case "npcline": Campaign.OnReply(m); break;
            case "fmsg": FriendNote(m.err ?? "", m.ok); break;
            case "freq": FriendNote(m.name + " quer ser seu amigo.", true); Sfx.UI("pickup", 0.6f); break;
            case "finvite":
                invites.RemoveAll(x => x.name == m.name);
                m.at = Time.unscaledTime;
                invites.Add(m);
                if (invites.Count > 3) invites.RemoveAt(0);
                Sfx.UI("pickup", 0.7f);
                break;
        }
    }

    public NetPlayer Me()
    {
        for (int i = 0; i < players.Length; i++) if (players[i].id == myId) return players[i];
        return null;
    }
}
