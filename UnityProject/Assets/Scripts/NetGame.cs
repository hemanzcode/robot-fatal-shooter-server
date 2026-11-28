using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

// Boneco de rede: ator controlado por outra maquina. Recebe posicao/mira e suaviza o movimento.
public class NetPuppet : MonoBehaviour
{
    public bool human;          // jogador de verdade (no host) - os outros sao copias de bots/dinos nos clientes
    public bool use;            // segurando F (plantar/desarmar)
    public Vector3 pos, vel;
    public float yaw, pitch;
    public float holdUntil;
    Actor actor;
    Collider col;
    Dino dino;
    Mech mech;

    void Awake() { actor = GetComponent<Actor>(); col = GetComponent<Collider>(); dino = GetComponent<Dino>(); mech = GetComponent<Mech>(); pos = transform.position; yaw = transform.eulerAngles.y; }

    public void Snap(Vector3 p, float y) { pos = p; yaw = y; vel = Vector3.zero; transform.SetPositionAndRotation(p, Quaternion.Euler(0f, y, 0f)); }

    void Update()
    {
        if (actor == null) return;
        if (col != null && !(col is CharacterController) && col.enabled != actor.alive) col.enabled = actor.alive;
        if (!actor.alive) return;
        float k = 1f - Mathf.Exp(-14f * Time.deltaTime);
        Vector3 to = pos + vel * 0.05f;
        transform.position = (to - transform.position).sqrMagnitude > 36f ? to : Vector3.Lerp(transform.position, to, k);
        transform.rotation = Quaternion.Lerp(transform.rotation, Quaternion.Euler(0f, yaw, 0f), k);
        Vector3 aim = Quaternion.Euler(pitch, yaw, 0f) * Vector3.forward;
        actor.Animate(new Vector3(vel.x, 0f, vel.z).magnitude, vel, aim);
        if (dino != null) dino.netVel = vel;
        if (mech != null) mech.netVel = vel;
    }
}

// Sincronizacao da partida online. O host (dono da sala) simula bots, dano, placar e modo de jogo;
// os clientes mandam a propria posicao e os acertos, e desenham o que o host informa.
public class NetGame : MonoBehaviour
{
    public static NetGame I;
    float nextGates;
    public static bool Online { get { return I != null; } }
    public static bool IsHost { get { return I != null && I.host; } }
    public static bool IsClient { get { return I != null && !I.host; } }
    public const int TeamSize = 5;

    bool host;
    bool applying;      // tratando mensagem recebida: nao reenviar
    readonly Dictionary<int, Actor> byId = new Dictionary<int, Actor>();
    readonly Dictionary<Actor, int> ids = new Dictionary<Actor, int>();
    readonly Dictionary<int, SupplyCrate> crates = new Dictionary<int, SupplyCrate>();
    readonly HashSet<int> seen = new HashSet<int>();
    int nextBotId = 1000, botSeq;
    float nextSend, nextSlow, lastSnap;
    bool lastUse;
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    readonly StringBuilder sb = new StringBuilder(4096);

    static string F(float v) { return v.ToString("0.##", Inv); }
    static float P(string s) { float v; float.TryParse(s, NumberStyles.Float, Inv, out v); return v; }
    static int N(string s) { int v; int.TryParse(s, out v); return v; }
    static string V(Vector3 v) { return F(v.x) + "," + F(v.y) + "," + F(v.z); }
    static Vector3 PV(string s) { string[] a = s.Split(','); return a.Length < 3 ? Vector3.zero : new Vector3(P(a[0]), P(a[1]), P(a[2])); }

    public static NetGame Begin(bool asHost)
    {
        NetGame n = Game.I.gameObject.AddComponent<NetGame>();
        n.host = asHost;
        I = n;
        return n;
    }

    void OnDestroy()
    {
        if (I == this) I = null;
        foreach (var kv in crates) if (kv.Value != null) Destroy(kv.Value.gameObject);
    }

    public Actor ActorById(int id) { Actor a; return byId.TryGetValue(id, out a) ? a : null; }

    // Nivel da conta de um jogador humano (0 = bot ou fora do online).
    public static int LevelOf(Actor a)
    {
        if (I == null || a == null || Net.I == null) return 0;
        int id = a.isPlayer ? Net.I.myId : 0, x;
        if (!a.isPlayer && I.ids.TryGetValue(a, out x)) id = x;
        if (id == 0) return 0;
        foreach (NetPlayer p in Net.I.players) if (p.id == id) return Mathf.Max(1, p.lvl);
        return 0;
    }

    public int IdOf(Actor a)
    {
        if (a == null) return 0;
        int id;
        if (ids.TryGetValue(a, out id)) return id;
        id = a.isPlayer ? Net.I.myId : nextBotId++;
        ids[a] = id; byId[id] = a;
        return id;
    }

    void Register(Actor a, int id) { ids[a] = id; byId[id] = a; }

    void Remove(Actor a)
    {
        if (a == null) return;
        int id;
        if (ids.TryGetValue(a, out id)) { ids.Remove(a); byId.Remove(id); }
        Game.I.actors.Remove(a);
        Destroy(a.gameObject);
    }

    // ---------------------------- Troca de dono ----------------------------
    // O dono caiu e o servidor passou a sala para outro jogador: quem vira dono assume bots, dano e placar
    // a partir do estado que ja tinha; o antigo dono, ao voltar, vira cliente.
    public void HostCheck()
    {
        if (Net.I == null || Game.I == null) return;
        bool should = Net.I.roomHost == Net.I.myId;
        if (should == host) return;
        if (should) Promote(); else Demote();
    }

    bool IsHumanId(int id)
    {
        foreach (NetPlayer p in Net.I.players) if (p.id == id) return true;
        return false;
    }

    void Promote()
    {
        Game g = Game.I;
        host = true;
        // Copias de bots e dinos viram lixo: o novo dono cria bots de verdade.
        List<Actor> drop = new List<Actor>();
        int maxId = 1000;
        foreach (var kv in byId)
        {
            Actor a = kv.Value;
            if (a == null || a == g.player) continue;
            maxId = Mathf.Max(maxId, kv.Key + 1);
            NetPuppet np = a.GetComponent<NetPuppet>();
            if (np == null) continue;
            if (IsHumanId(kv.Key))
            {
                np.human = true;
                int pid = kv.Key;
                a.onRespawn += () => SendSpawn(a, pid);
            }
            else drop.Add(a);
        }
        foreach (Actor a in drop) Remove(a);
        nextBotId = maxId + 100;
        foreach (var kv in crates) if (kv.Value != null) Destroy(kv.Value.gameObject);
        crates.Clear();
        g.OnBecameHost();
        RosterChanged();
        g.Announce("Voce agora e o dono da sala", Art.Yellow);
    }

    void Demote()
    {
        Game g = Game.I;
        host = false;
        // Bots e dinos que eu simulava saem: o novo dono manda as copias na proxima foto.
        List<Actor> drop = new List<Actor>();
        foreach (Actor a in g.actors) if (a != null && a != g.player && a.GetComponent<NetPuppet>() == null) drop.Add(a);
        foreach (Actor a in drop) Remove(a);
        foreach (var kv in byId) { NetPuppet np = kv.Value != null ? kv.Value.GetComponent<NetPuppet>() : null; if (np != null) np.human = false; }
        g.OnBecameClient();
        lastSnap = Time.unscaledTime;
    }

    // ---------------------------- Host: quem esta na sala ----------------------------

    public void RosterChanged()
    {
        if (!host || Game.I == null || Game.I.matchOver) return;
        Net net = Net.I;
        // Saiu alguem: tira o boneco.
        List<Actor> gone = new List<Actor>();
        foreach (var kv in byId)
        {
            NetPuppet np = kv.Value != null ? kv.Value.GetComponent<NetPuppet>() : null;
            if (np == null || !np.human) continue;
            bool still = false;
            for (int i = 0; i < net.players.Length; i++) if (net.players[i].id == kv.Key) still = true;
            if (!still) gone.Add(kv.Value);
        }
        foreach (Actor a in gone) { Game.I.Announce(a.displayName + " saiu da partida", Color.white); Remove(a); }
        // Entrou alguem: cria o boneco e manda nascer.
        int[] humans = new int[2];
        for (int i = 0; i < net.players.Length; i++)
        {
            NetPlayer p = net.players[i];
            humans[Mathf.Clamp(p.team, 0, 1)]++;
            if (p.id == net.myId || byId.ContainsKey(p.id)) continue;
            Actor a = Game.I.MakePuppet(p.name, p.team, p.agent, true);
            Register(a, p.id);
            int pid = p.id;
            a.onRespawn += () => SendSpawn(a, pid);
            SendSpawn(a, pid);
            Game.I.Announce(p.name + " entrou na partida", Art.TeamColor(p.team));
        }
        // Bots completam os times ate 5 x 5.
        for (int team = 0; team < 2; team++)
        {
            List<Actor> bots = new List<Actor>();
            foreach (Actor a in Game.I.actors) if (a != null && a.team == team && a.GetComponent<Bot>() != null) bots.Add(a);
            // Sala so com os amigos: sem bots.
            int want = Net.I != null && !Net.I.roomBots ? 0 : Mathf.Max(0, TeamSize - humans[team]);
            while (bots.Count > want) { Remove(bots[bots.Count - 1]); bots.RemoveAt(bots.Count - 1); }
            while (bots.Count < want)
            {
                int ag = (Settings.agent + 1 + botSeq++) % AgentBody.Defs.Length;
                bots.Add(Game.I.MakeActor(AgentBody.Defs[ag].name, team, false, true, ag));
            }
        }
    }

    void SendSpawn(Actor a, int peer)
    {
        NetPuppet np = a.GetComponent<NetPuppet>();
        if (np != null) { np.Snap(a.transform.position, a.transform.eulerAngles.y); np.holdUntil = Time.time + 0.7f; }
        Net.I.SendGame(peer, "SP;" + V(a.transform.position) + ";" + F(a.transform.eulerAngles.y));
    }

    // ---------------------------- Envio periodico ----------------------------

    void Update()
    {
        Game g = Game.I;
        if (g == null || Net.I == null || !Net.I.InRoom) return;
        SyncSkins();
        if (Time.unscaledTime < nextSend) return;
        nextSend = Time.unscaledTime + 1f / 15f;
        if (host) SendSnapshot(g); else SendMine(g);
        // Dono ausente ou eu reconectando: espera (a vaga fica guardada por 60 s no servidor).
        if (Net.I != null && (Net.I.HostAway || Net.I.reconnecting)) lastSnap = Time.unscaledTime;
        if (!host && lastSnap > 0f && Time.unscaledTime - lastSnap > 12f) { Net.I.error = "O host parou de responder."; g.ToMenu(); }
    }

    void SendMine(Game g)
    {
        if (g.player == null || g.pc == null || g.pc.cam == null) return;
        Actor p = g.player;
        float pitch = g.pc.cam.transform.localEulerAngles.x; if (pitch > 180f) pitch -= 360f;
        int posture = p.prone ? 2 : (p.crouching ? 1 : 0);
        Net.I.SendGame(-1, "P;" + V(p.transform.position) + ";" + F(p.transform.eulerAngles.y) + ";" + F(pitch) + ";" + F(p.velocity.x) + ";" + F(p.velocity.z) + ";" + posture);
        bool use = GameInput.Use && !g.menus.IsOpen;
        if (use != lastUse || Time.unscaledTime >= nextSlow) { lastUse = use; nextSlow = Time.unscaledTime + 0.5f; Net.I.SendGame(-1, "U;" + (use ? 1 : 0)); }
    }

    float nextFull;
    static string F1(float v) { return v.ToString("0.#", Inv); }
    static string V1(Vector3 v) { return F1(v.x) + "," + F1(v.y) + "," + F1(v.z); }

    // Foto da partida 15x por segundo; nome e placar de cada um so 1x por segundo (mensagem menor).
    void SendSnapshot(Game g)
    {
        bool full = Time.unscaledTime >= nextFull;
        if (full) nextFull = Time.unscaledTime + 1f;
        sb.Length = 0;
        sb.Append("W;").Append(g.score[0]).Append(';').Append(g.score[1]).Append(';').Append(F(g.timeLeft)).Append(';').Append(g.roundMode ? 1 : 0).Append(';');
        bool first = true;
        for (int i = 0; i < g.actors.Count; i++)
        {
            Actor a = g.actors[i];
            if (a == null) continue;
            float yaw = a.transform.eulerAngles.y, pitch = 0f;
            if (a.isPlayer && g.pc != null && g.pc.cam != null) { pitch = g.pc.cam.transform.localEulerAngles.x; if (pitch > 180f) pitch -= 360f; }
            else
            {
                NetPuppet np = a.GetComponent<NetPuppet>();
                if (np != null) pitch = np.pitch;
                else if (a.body != null && a.body.aimDir.sqrMagnitude > 0.01f) pitch = -Mathf.Asin(Mathf.Clamp(a.body.aimDir.normalized.y, -1f, 1f)) * Mathf.Rad2Deg;
            }
            int kind = 0;
            Dino d = a.creature ? a.GetComponent<Dino>() : null;
            if (d != null) kind = d.kind + 1;
            else if (a.creature && a.GetComponent<Mech>() != null) kind = 3;
            if (!first) sb.Append('|');
            first = false;
            sb.Append(IdOf(a)).Append(',').Append(a.team).Append(',').Append(a.isPlayer ? Settings.agent : a.agent).Append(',').Append(kind).Append(',')
              .Append(V1(a.transform.position)).Append(',').Append(Mathf.RoundToInt(yaw)).Append(',').Append(Mathf.RoundToInt(pitch)).Append(',')
              .Append(F1(a.velocity.x)).Append(',').Append(F1(a.velocity.z)).Append(',').Append(Mathf.CeilToInt(Mathf.Max(0f, a.health))).Append(',').Append(a.alive ? (a.prone ? 3 : (a.crouching ? 2 : 1)) : 0);
            if (full) sb.Append(',').Append(a.kills).Append(',').Append(a.deaths).Append(',').Append(a.isPlayer ? Net.I.userName : a.displayName);
        }
        sb.Append(';');
        first = true;
        for (int i = 0; i < SupplyCrate.All.Count; i++)
        {
            SupplyCrate c = SupplyCrate.All[i];
            if (c == null) continue;
            if (!first) sb.Append('|');
            first = false;
            sb.Append(c.netId).Append(',').Append(V(c.transform.position)).Append(',').Append(c.landed ? 1 : 0);
        }
        Net.I.SendGame(0, sb.ToString());
        if (g.bomb != null && Time.unscaledTime >= nextSlow) { nextSlow = Time.unscaledTime + 0.2f; Net.I.SendGame(0, g.bomb.NetState(this)); }
        // Portoes: reenviados de tempos em tempos para quem entrou depois.
        if (Gate.All.Count > 0 && Time.unscaledTime >= nextGates) { nextGates = Time.unscaledTime + 2f; SendGates(); }
    }

    // ---------------------------- Ganchos chamados pelo jogo ----------------------------

    // Cliente acertou alguem: quem decide o dano e o host.
    public static void SendHit(Actor target, float dmg, bool head, string weapon)
    {
        if (!IsClient) return;
        int id;
        if (I.ids.TryGetValue(target, out id)) Net.I.SendGame(-1, "H;" + id + ";" + F(dmg) + ";" + (head ? 1 : 0) + ";" + weapon);
    }

    public static void SendAgentAbility(bool ultimate, bool secondary = false)
    {
        if (IsClient && Net.I != null) Net.I.SendGame(-1, "A;" + (ultimate ? 1 : (secondary ? 2 : 0)));
    }

    public static void BroadcastAgentStatus(Actor target, string kind, float duration)
    {
        int id;
        if (IsHost && target != null && I.ids.TryGetValue(target, out id))
            Net.I.SendGame(0, "S;" + id + ";" + kind + ";" + F(duration));
    }

    public static void Tracer(Actor shooter, WeaponDef w, Vector3 muzzle, Vector3 end)
    {
        if (I == null || I.applying || Game.I == null) return;
        if (!I.host && shooter != Game.I.player) return;
        Net.I.SendGame(0, "T;" + w.sound + ";" + (w.name == "PLASMA" ? 0 : 1) + ";" + V(muzzle) + ";" + V(end));
    }

    public static void Launched(Actor owner, Vector3 pos, Vector3 dir, float speed, float gravity, float dmg, float radius, string weapon)
    {
        if (I == null || I.applying || Game.I == null) return;
        if (!I.host && owner != Game.I.player) return;
        Net.I.SendGame(0, "L;" + I.IdOf(owner) + ";" + weapon + ";" + V(pos) + ";" + V(dir) + ";" + F(speed) + ";" + F(gravity) + ";" + F(dmg) + ";" + F(radius));
    }

    public static void Feed(string text, Color c)
    {
        if (IsHost && !I.applying) Net.I.SendGame(0, "M;" + F(c.r) + "," + F(c.g) + "," + F(c.b) + ";" + text.Replace(';', ','));
    }

    public static void BannerAll(string text) { if (IsHost) Net.I.SendGame(0, "N;" + text.Replace(';', ',')); }
    public static void Ended()
    {
        if (!IsHost) return;
        Net.I.SendGame(0, "E");
        I.SendResult();
    }

    // Fim de partida: o dono da sala manda o placar ao servidor, que calcula XP e SHOTS de cada conta.
    void SendResult()
    {
        Game g = Game.I;
        int win = g.score[0] == g.score[1] ? -1 : (g.score[0] > g.score[1] ? 0 : 1);
        StringBuilder r = new StringBuilder(1024);
        r.Append("{\"t\":\"result\",\"winner\":").Append(win).Append(",\"players\":[");
        bool first = true;
        foreach (Actor a in g.actors)
        {
            if (a == null || a.creature || a.metal) continue;
            int id = 0, x;
            if (a.isPlayer) id = Net.I.myId;
            else if (ids.TryGetValue(a, out x) && IsHumanId(x)) id = x;
            if (!first) r.Append(',');
            first = false;
            r.Append("{\"id\":").Append(id).Append(",\"k\":").Append(a.kills).Append(",\"d\":").Append(a.deaths)
             .Append(",\"hs\":").Append(a.headshots).Append(",\"big\":").Append(a.bigKills).Append(",\"obj\":").Append(a.objectives).Append('}');
        }
        r.Append("]}");
        Net.I.Send(r.ToString());
    }

    // Skin do fuzil dos outros jogadores (a que cada um equipou na loja), conferida 1 vez por segundo.
    float nextSkins;
    void SyncSkins()
    {
        if (Time.unscaledTime < nextSkins || Net.I == null) return;
        nextSkins = Time.unscaledTime + 1f;
        foreach (NetPlayer p in Net.I.players)
        {
            if (p.id == Net.I.myId) continue;
            Actor a = ActorById(p.id);
            if (a == null || a.body == null) continue;
            string want = p.equip != null && p.equip.Length > 0 ? p.equip[0] ?? "" : "";
            if (a.body.gunSkin != want) a.body.SetGunSkin(want);
        }
    }
    // Marcacao para o time: G;time;tipo;idDoAlvo;posicao;nome
    public static void SendPing(int team, int kind, Actor target, Vector3 pos, string who)
    {
        if (I == null || Net.I == null || !Net.I.InRoom) return;
        int id = target != null ? I.IdOf(target) : 0;
        Net.I.SendGame(0, "G;" + team + ";" + kind + ";" + id + ";" + V(pos) + ";" + who.Replace(';', ','));
    }

    void ShowPing(Game g, string[] a)
    {
        if (a.Length < 6 || Ping.I == null || g.player == null || N(a[1]) != g.player.team) return;
        int id = N(a[3]);
        Ping.I.Add(PV(a[4]), id != 0 ? ActorById(id) : null, Mathf.Clamp(N(a[2]), 0, 7), a[5]);
    }

    // Portoes (mapa NO GELO): estado de todos, "K;0101...".
    public static void SendGates() { if (IsHost && Net.I != null) Net.I.SendGame(0, "K;" + Gate.State()); }

    // Paredes da CASA: quem quebrou avisa todos ("BW;id;placas"); "BW;R" refaz as paredes.
    public static void SendWalls(string d) { if (I != null && !I.applying && Net.I != null) Net.I.SendGame(0, d); }

    public static void OpenCrate() { if (IsClient) Net.I.SendGame(-1, "O"); }

    // ---------------------------- Recebimento ----------------------------

    public void Receive(int from, string d)
    {
        Game g = Game.I;
        if (g == null || d.Length == 0) return;
        string[] a = d.Split(';');
        applying = true;
        try
        {
            if (a[0] == "V") { if (a.Length > 1) VoiceChat.Incoming(from, a[1]); }
            else if (a[0] == "G") ShowPing(g, a);
            else if (a[0] == "BW") Breakable.FromNet(a);   // paredes da CASA ("W" e a foto da partida que o host manda)
            else if (host) FromClient(g, from, a); else if (from == Net.I.roomHost || a[0] == "T" || a[0] == "L") FromHost(g, from, a);
        }
        finally { applying = false; }
    }

    void FromClient(Game g, int from, string[] a)
    {
        Actor who = ActorById(from);
        if (who == null) return;
        NetPuppet np = who.GetComponent<NetPuppet>();
        switch (a[0])
        {
            case "P":
                if (np == null || a.Length < 6 || Time.time < np.holdUntil) break;
                np.pos = PV(a[1]); np.yaw = P(a[2]); np.pitch = P(a[3]); np.vel = new Vector3(P(a[4]), 0f, P(a[5]));
                who.crouching = a.Length > 6 && a[6] == "1";
                who.prone = a.Length > 6 && a[6] == "2";
                break;
            case "U": if (np != null && a.Length > 1) np.use = a[1] == "1"; break;
            case "H":
            {
                if (a.Length < 5 || !who.alive || g.matchOver) break;
                Actor target = ActorById(N(a[1]));
                if (target == null || !target.alive || target.team == who.team) break;
                who.lastShotTime = Time.time;
                target.TakeDamage(Mathf.Clamp(P(a[2]), 0f, 400f), who, a[3] == "1", a[4]);
                break;
            }
            case "T": Trace(a); who.lastShotTime = Time.time; break;
            case "L": Launch(a, true); break;
            case "K":
                if (a.Length > 1 && who.alive)
                {
                    int gi = N(a[1]);
                    if (gi >= 0 && gi < Gate.All.Count && Gate.All[gi] != null && (Gate.All[gi].transform.position - who.transform.position).sqrMagnitude < 25f)
                        Gate.All[gi].Set(!Gate.All[gi].open);
                }
                break;
            case "O":
            {
                SupplyCrate c = SupplyCrate.Near(who.transform.position, 4f);
                if (c != null && who.alive) c.Open(who);
                break;
            }
            case "A":
                if (a.Length > 1 && who.alive)
                {
                    Vector3 aim = who.transform.forward;
                    if (np != null) aim = Quaternion.Euler(np.pitch, np.yaw, 0f) * Vector3.forward;
                    AgentAbilities.Execute(who, a[1] == "1", aim, a[1] == "2" ? 1 : 0);
                }
                break;
        }
    }

    void FromHost(Game g, int from, string[] a)
    {
        switch (a[0])
        {
            case "W": Snapshot(g, a); break;
            case "T": Trace(a); break;
            case "L": Launch(a, false); break;
            case "M": if (a.Length > 2) { Vector3 c = PV(a[1]); g.Announce(a[2], new Color(c.x, c.y, c.z)); } break;
            case "N": if (a.Length > 1) g.Banner(a[1]); break;
            case "K": if (a.Length > 1) Gate.ApplyState(a[1]); break;
            case "E": g.EndOnline(); break;
            case "B": if (g.bomb != null) g.bomb.ApplyNet(this, a); break;
            case "SP":
                if (a.Length > 2 && g.player != null) g.player.Respawn(PV(a[1]), P(a[2]));
                break;
            case "S":
                if (a.Length > 3)
                {
                    Actor target = ActorById(N(a[1]));
                    if (target != null)
                    {
                        float until = Time.time + Mathf.Clamp(P(a[3]), 0f, 20f);
                        if (a[2] == "blind") target.blindUntil = until;
                        else if (a[2] == "stun") target.stunnedUntil = until;
                        else if (a[2] == "invuln") target.invulnerableUntil = until;
                        else if (a[2] == "reveal") target.revealedUntil = until;
                    }
                }
                break;
        }
    }

    void Trace(string[] a)
    {
        if (a.Length < 5) return;
        Vector3 m = PV(a[3]), e = PV(a[4]);
        Fx.Tracer(m, e, a[2] == "1" ? new Color(1f, 0.82f, 0.45f, 0.8f) : Art.Green, 0.018f, 0.07f);
        Combat.TryBulletFlyby(null, m, e, false);
        Sfx.At(a[1], m, 0.7f);
    }

    void Launch(string[] a, bool real)
    {
        if (a.Length < 9) return;
        Actor owner = ActorById(N(a[1]));
        string weapon = a[2];
        Color c = weapon == "PLASMA" ? Art.Green : (weapon == "GRANADA" ? Art.Green : new Color(1f, 0.6f, 0.2f));
        // No host o projetil e de verdade (da dano); nos clientes e so a imagem.
        Combat.Launch(owner, PV(a[3]), PV(a[4]), P(a[5]), P(a[6]), real ? Mathf.Clamp(P(a[7]), 0f, 200f) : 0f, Mathf.Clamp(P(a[8]), 0.5f, 8f), weapon, c, 1f);
        if (weapon != "GRANADA") Sfx.At(weapon == "PLASMA" ? "rail" : "rocket", PV(a[3]), 0.7f);
    }

    void Snapshot(Game g, string[] a)
    {
        if (a.Length < 6) return;
        lastSnap = Time.unscaledTime;
        g.score[0] = N(a[1]); g.score[1] = N(a[2]); g.timeLeft = P(a[3]);
        seen.Clear();
        if (a[5].Length > 0)
        {
            string[] recs = a[5].Split('|');
            for (int i = 0; i < recs.Length; i++)
            {
                string[] r = recs[i].Split(',');
                if (r.Length < 13) continue;
                bool full = r.Length >= 16;
                int id = N(r[0]), team = N(r[1]), agent = N(r[2]), kind = N(r[3]);
                Vector3 pos = new Vector3(P(r[4]), P(r[5]), P(r[6]));
                float hp = P(r[11]); bool alive = r[12] != "0";
                seen.Add(id);
                if (id == Net.I.myId)
                {
                    if (g.player != null) { if (full) { g.player.kills = N(r[13]); g.player.deaths = N(r[14]); } g.player.NetState(hp, alive, false); }
                    continue;
                }
                Actor act = ActorById(id);
                if (act == null)
                {
                    if (kind == 3) act = Mech.Spawn(pos, team, true).GetComponent<Actor>();
                    else if (kind > 0) act = Dino.Spawn(pos, team, kind - 1, true).GetComponent<Actor>();
                    else act = g.MakePuppet(full ? r[15] : "...", team, agent, false);
                    Register(act, id);
                    act.GetComponent<NetPuppet>().Snap(pos, P(r[7]));
                }
                NetPuppet np = act.GetComponent<NetPuppet>();
                np.pos = pos; np.yaw = P(r[7]); np.pitch = P(r[8]); np.vel = new Vector3(P(r[9]), 0f, P(r[10]));
                if (full) { act.kills = N(r[13]); act.deaths = N(r[14]); if (act.displayName != r[15]) act.displayName = r[15]; }
                act.NetState(hp, alive, true);
                act.crouching = r[12] == "2";
                act.prone = r[12] == "3";
            }
        }
        // Quem nao veio na foto saiu da partida.
        List<Actor> gone = null;
        foreach (var kv in byId) if (!seen.Contains(kv.Key) && kv.Value != g.player) { if (gone == null) gone = new List<Actor>(); gone.Add(kv.Value); }
        if (gone != null) foreach (Actor x in gone) Remove(x);

        // Caixas laranja
        seen.Clear();
        if (a.Length > 6 && a[6].Length > 0)
        {
            string[] cs = a[6].Split('|');
            for (int i = 0; i < cs.Length; i++)
            {
                string[] r = cs[i].Split(',');
                if (r.Length < 5) continue;
                int id = N(r[0]);
                Vector3 pos = new Vector3(P(r[1]), P(r[2]), P(r[3]));
                seen.Add(id);
                SupplyCrate c;
                if (!crates.TryGetValue(id, out c) || c == null) { c = SupplyCrate.Drop(pos, true); crates[id] = c; }
                c.NetSet(pos, r[4] == "1");
            }
        }
        List<int> dead = null;
        foreach (var kv in crates) if (!seen.Contains(kv.Key)) { if (dead == null) dead = new List<int>(); dead.Add(kv.Key); }
        if (dead != null) foreach (int id in dead)
        {
            if (crates[id] != null) { Fx.Blast(crates[id].transform.position + Vector3.up * 0.6f, 2.2f); Destroy(crates[id].gameObject); }
            crates.Remove(id);
        }
    }
}
