using System.Collections.Generic;
using UnityEngine;

// Modo BOMBA (rodadas, sem renascer): o time BAAL ataca e precisa plantar a bomba em A ou B;
// o time MOLOCH defende e precisa impedir ou desarmar. Vence a partida quem ganhar RoundsToWin rodadas.
public class BombMode : MonoBehaviour
{
    public const int Attackers = 0, Defenders = 1;
    public const int RoundsToWin = 5;
    const float RoundTime = 105f, FuseTime = 40f, PlantTime = 3.2f, DefuseTime = 6f, SiteRadius = 4.2f;

    enum Phase { Freeze, Live, Planted, Over }
    Phase phase;
    float phaseEnd, plantProg, defuseProg, nextBeep;
    readonly Vector3[] sites = new Vector3[2];
    Actor carrier;
    GameObject bombObj, dropObj;
    Light bombLamp;
    Vector3 bombPos;
    Transform root;
    int round;

    void Start()
    {
        useGUILayout = false;
        root = new GameObject("BombSites").transform;
        root.SetParent(transform, false);
        sites[0] = SiteAt(Arena.SiteA[0], Arena.SiteA[1]);
        sites[1] = SiteAt(Arena.SiteB[0], Arena.SiteB[1]);
        for (int i = 0; i < 2; i++)
        {
            Color c = i == 0 ? Art.Red : Art.Yellow;
            Art.Prim(PrimitiveType.Cylinder, root, sites[i] + Vector3.up * 0.03f, new Vector3(SiteRadius * 2f, 0.02f, SiteRadius * 2f), Art.Mat(new Color(0.5f, 0.08f, 0.05f), Color.black, 0f, default(Color), 0.2f, 0f), false, "site");
            Art.Prim(PrimitiveType.Cylinder, root, sites[i] + Vector3.up * 0.04f, new Vector3(SiteRadius * 1.7f, 0.02f, SiteRadius * 1.7f), Art.Mat(new Color(0.75f, 0.68f, 0.55f), Color.black, 0f, default(Color), 0.2f, 0f), false, "siteIn");
            Art.Box(root, sites[i] + new Vector3(0f, 2.6f, 0f), new Vector3(0.08f, 5.2f, 0.08f), Art.Glow(c, 1.2f));
        }
        Game.I.roundMode = true;
        if (!NetGame.IsClient) NewRound();
    }

    // Celula pedida ou, se estiver ocupada, a livre mais proxima.
    static Vector3 SiteAt(int cx, int cy)
    {
        for (int r = 0; r < 6; r++)
            for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                    if (Arena.Walk(cx + dx, cy + dy) && Arena.Walk(cx + dx + 1, cy + dy) && Arena.Walk(cx + dx, cy + dy + 1)) return Arena.Center(cx + dx, cy + dy);
        return Arena.Center(cx, cy);
    }

    void OnDestroy()
    {
        if (root != null) Destroy(root.gameObject);
        if (bombObj != null) Destroy(bombObj);
        if (dropObj != null) Destroy(dropObj);
        if (Game.I != null) Game.I.roundMode = false;
    }

    void NewRound()
    {
        round++;
        if (bombObj != null) Destroy(bombObj);
        if (dropObj != null) Destroy(dropObj);
        plantProg = defuseProg = 0f;
        if (round > 1) { Breakable.ResetAll(); NetGame.SendWalls("BW;R"); }   // CASA: paredes inteiras a cada rodada
        Game.I.RespawnAll();
        phase = Phase.Freeze;
        phaseEnd = Time.time + 4f;
        // A bomba comeca com um atacante (o jogador, se ele estiver nesse time, em uma a cada duas rodadas).
        List<Actor> att = new List<Actor>();
        foreach (Actor a in Game.I.actors) if (a != null && a.team == Attackers && !a.creature) att.Add(a);
        carrier = att.Count > 0 ? att[Random.Range(0, att.Count)] : null;
        if (Game.I.player != null && Game.I.player.team == Attackers && round % 2 == 1) carrier = Game.I.player;
        Game.I.BannerAll("RODADA " + round + (Game.I.player != null && Game.I.player.team == Attackers ? "  -  PLANTE A BOMBA EM A OU B" : "  -  DEFENDA OS PONTOS A E B"));
        Orders();
    }

    static Bot BotOf(Actor a) { return a != null ? a.GetComponent<Bot>() : null; }

    // Diz a cada bot para onde ir.
    void Orders()
    {
        var list = Game.I.actors;
        int di = 0;
        int goSite = carrier != null ? NearestSite(carrier.transform.position) : 0;
        for (int i = 0; i < list.Count; i++)
        {
            Actor a = list[i];
            Bot b = BotOf(a);
            if (b == null || !a.alive) continue;
            b.hold = false;
            if (phase == Phase.Planted)
            {
                b.hasGoal = true;
                Vector2 o = Random.insideUnitCircle * (a.team == Defenders ? 0.6f : 3.5f);
                b.goalPos = bombPos + new Vector3(o.x, 0f, o.y);
            }
            else if (a.team == Attackers)
            {
                // O portador vai plantar; os outros escoltam ate o mesmo ponto.
                b.hasGoal = true;
                Vector2 o = a == carrier ? Vector2.zero : Random.insideUnitCircle * 3.5f;
                b.goalPos = (dropObj != null && carrier == null ? dropObj.transform.position : sites[goSite]) + new Vector3(o.x, 0f, o.y);
            }
            else
            {
                b.hasGoal = true;
                Vector2 o = Random.insideUnitCircle * 5f;
                b.goalPos = sites[di++ % 2] + new Vector3(o.x, 0f, o.y);
            }
        }
    }

    int NearestSite(Vector3 p) { return (p - sites[0]).sqrMagnitude <= (p - sites[1]).sqrMagnitude ? 0 : 1; }

    int InSite(Vector3 p)
    {
        for (int i = 0; i < 2; i++) { Vector3 d = p - sites[i]; d.y = 0f; if (d.sqrMagnitude < SiteRadius * SiteRadius) return i; }
        return -1;
    }

    // Sala sem bots: enquanto um time estiver vazio, ninguem vence por eliminacao.
    bool BothTeams()
    {
        bool a = false, b = false;
        foreach (Actor x in Game.I.actors) if (x != null && !x.creature) { if (x.team == 0) a = true; else b = true; }
        return a && b;
    }

    int Alive(int team)
    {
        int n = 0;
        foreach (Actor a in Game.I.actors) if (a != null && a.alive && a.team == team && !a.creature) n++;
        return n;
    }

    bool Working(Actor a)
    {
        if (a == null || !a.alive || a.vehicle != null) return false;
        if (a.isPlayer) return GameInput.Use && !Game.I.menus.IsOpen;
        NetPuppet np = a.GetComponent<NetPuppet>();
        if (np != null && np.human) return np.use;
        Bot b = BotOf(a);
        return b != null && !b.Fighting && Time.time - a.lastDamageTime > 1.2f;
    }

    void Win(int team, string why)
    {
        if (phase == Phase.Over) return;
        phase = Phase.Over;
        phaseEnd = Time.time + 5f;
        Game.I.score[team]++;
        Game.I.BannerAll(Art.TeamName(team) + " VENCE A RODADA  -  " + why);
        Game.I.Announce(why, Art.TeamColor(team));
        Sfx.UI("ult", 0.7f);
        foreach (Actor a in Game.I.actors) { Bot b = BotOf(a); if (b != null) { b.hasGoal = false; b.hold = false; } }
    }

    void Update()
    {
        Game g = Game.I;
        if (g == null || g.matchOver) return;
        if (NetGame.IsClient) { RemoteUpdate(); return; }
        g.timeLeft = Mathf.Max(0f, phaseEnd - Time.time);
        // Quem levava a bomba saiu da partida: passa para outro atacante vivo.
        if (phase == Phase.Live && carrier == null && dropObj == null)
        {
            foreach (Actor a in g.actors) if (a != null && a.alive && a.team == Attackers && !a.creature) { carrier = a; Orders(); break; }
        }

        if (phase == Phase.Over)
        {
            if (Time.time < phaseEnd) return;
            if (g.score[0] >= RoundsToWin || g.score[1] >= RoundsToWin) { g.FinishRounds(); return; }
            NewRound();
            return;
        }
        if (phase == Phase.Freeze)
        {
            if (Time.time >= phaseEnd) { phase = Phase.Live; phaseEnd = Time.time + RoundTime; }
            return;
        }

        if (phase == Phase.Live)
        {
            // Portador morreu: a bomba fica no chao ate outro atacante passar por cima.
            if (carrier != null && !carrier.alive)
            {
                dropObj = MakeBomb(carrier.transform.position, false);
                carrier = null;
                g.Announce("A BOMBA CAIU", Art.Yellow);
                Orders();
            }
            if (carrier == null && dropObj != null)
            {
                foreach (Actor a in g.actors)
                {
                    if (a == null || !a.alive || a.team != Attackers || a.creature || a.vehicle != null) continue;
                    if ((a.transform.position - dropObj.transform.position).sqrMagnitude > 1.6f * 1.6f) continue;
                    carrier = a; Destroy(dropObj); dropObj = null;
                    g.Announce(a.displayName + " PEGOU A BOMBA", Art.TeamColor(Attackers));
                    Orders();
                    break;
                }
            }
            // Plantar: parado dentro do ponto, segurando F (bots: sem inimigo a vista).
            bool planting = carrier != null && InSite(carrier.transform.position) >= 0 && Working(carrier);
            Bot cb = BotOf(carrier);
            if (cb != null) cb.hold = InSite(carrier.transform.position) >= 0;
            plantProg = planting ? plantProg + Time.deltaTime : 0f;
            if (plantProg >= PlantTime)
            {
                carrier.objectives++;
                bombPos = carrier.transform.position;
                bombObj = MakeBomb(bombPos, true);
                carrier = null;
                phase = Phase.Planted;
                phaseEnd = Time.time + FuseTime;
                plantProg = 0f;
                g.BannerAll("BOMBA PLANTADA  -  " + (int)FuseTime + " SEGUNDOS");
                g.Announce("BOMBA PLANTADA EM " + (NearestSite(bombPos) == 0 ? "A" : "B"), Art.Red);
                Sfx.UI("beep", 1f);
                Orders();
                return;
            }
            if (BothTeams() && Alive(Defenders) == 0) { Win(Attackers, "DEFENSORES ELIMINADOS"); return; }
            if (BothTeams() && Alive(Attackers) == 0) { Win(Defenders, "ATACANTES ELIMINADOS"); return; }
            if (Time.time >= phaseEnd) { Win(Defenders, "TEMPO ESGOTADO"); return; }
            return;
        }

        // Bomba plantada: bipes cada vez mais rapidos; defensor ao lado, segurando F, desarma.
        float left = phaseEnd - Time.time;
        if (Time.time >= nextBeep)
        {
            nextBeep = Time.time + Mathf.Lerp(0.12f, 1f, Mathf.Clamp01(left / FuseTime));
            Sfx.At("beep", bombPos, 1f);
            if (bombLamp != null) bombLamp.enabled = !bombLamp.enabled;
        }
        Actor defuser = null;
        foreach (Actor a in g.actors)
        {
            if (a == null || !a.alive || a.team != Defenders || a.creature) continue;
            Vector3 d = a.transform.position - bombPos;
            if (d.sqrMagnitude > 1.9f * 1.9f) continue;
            Bot b = BotOf(a);
            if (b != null) b.hold = true;
            if (defuser == null && Working(a)) defuser = a;
        }
        defuseProg = defuser != null ? defuseProg + Time.deltaTime : 0f;
        if (defuseProg >= DefuseTime) { defuser.objectives++; if (bombObj != null) Destroy(bombObj); Win(Defenders, "BOMBA DESARMADA"); return; }
        if (left <= 0f)
        {
            if (bombObj != null) Destroy(bombObj);
            Combat.Explode(bombPos + Vector3.up * 0.5f, 15f, 600f, null, "BOMBA", Art.Orange);
            Win(Attackers, "A BOMBA EXPLODIU");
            return;
        }
        if (BothTeams() && Alive(Defenders) == 0) Win(Attackers, "DEFENSORES ELIMINADOS");
    }

    // ---------- Online ----------
    // Estado enviado pelo host: fase; quem leva; bomba caida; bomba plantada; progresso; tempo.
    public string NetState(NetGame n)
    {
        System.Globalization.CultureInfo inv = System.Globalization.CultureInfo.InvariantCulture;
        Vector3 d = dropObj != null ? dropObj.transform.position : Vector3.zero;
        return "B;" + (int)phase + ";" + (carrier != null ? n.IdOf(carrier) : 0) + ";" + (dropObj != null ? 1 : 0) + ";" + d.x.ToString("0.#", inv) + "," + d.y.ToString("0.#", inv) + "," + d.z.ToString("0.#", inv)
            + ";" + bombPos.x.ToString("0.#", inv) + "," + bombPos.y.ToString("0.#", inv) + "," + bombPos.z.ToString("0.#", inv)
            + ";" + plantProg.ToString("0.##", inv) + ";" + defuseProg.ToString("0.##", inv) + ";" + Mathf.Max(0f, phaseEnd - Time.time).ToString("0.#", inv);
    }

    static Vector3 V3(string s)
    {
        System.Globalization.CultureInfo inv = System.Globalization.CultureInfo.InvariantCulture;
        string[] a = s.Split(',');
        if (a.Length < 3) return Vector3.zero;
        float x, y, z;
        float.TryParse(a[0], System.Globalization.NumberStyles.Float, inv, out x);
        float.TryParse(a[1], System.Globalization.NumberStyles.Float, inv, out y);
        float.TryParse(a[2], System.Globalization.NumberStyles.Float, inv, out z);
        return new Vector3(x, y, z);
    }

    public void ApplyNet(NetGame n, string[] a)
    {
        if (a.Length < 9) return;
        System.Globalization.CultureInfo inv = System.Globalization.CultureInfo.InvariantCulture;
        int ph; int.TryParse(a[1], out ph);
        int cid; int.TryParse(a[2], out cid);
        phase = (Phase)Mathf.Clamp(ph, 0, 3);
        carrier = cid == Net.I.myId ? Game.I.player : n.ActorById(cid);
        bool hasDrop = a[3] == "1";
        if (hasDrop && dropObj == null) dropObj = MakeBomb(V3(a[4]), false);
        if (!hasDrop && dropObj != null) { Destroy(dropObj); dropObj = null; }
        bool planted = phase == Phase.Planted;
        if (planted && bombObj == null) { bombPos = V3(a[5]); bombObj = MakeBomb(bombPos, true); }
        if (!planted && bombObj != null) { Destroy(bombObj); bombObj = null; }
        float rem;
        float.TryParse(a[6], System.Globalization.NumberStyles.Float, inv, out plantProg);
        float.TryParse(a[7], System.Globalization.NumberStyles.Float, inv, out defuseProg);
        float.TryParse(a[8], System.Globalization.NumberStyles.Float, inv, out rem);
        phaseEnd = Time.time + rem;
    }

    void RemoteUpdate()
    {
        Game.I.timeLeft = Mathf.Max(0f, phaseEnd - Time.time);
        if (phase != Phase.Planted || Time.time < nextBeep) return;
        nextBeep = Time.time + Mathf.Lerp(0.12f, 1f, Mathf.Clamp01((phaseEnd - Time.time) / FuseTime));
        Sfx.At("beep", bombPos, 1f);
        if (bombLamp != null) bombLamp.enabled = !bombLamp.enabled;
    }

    GameObject MakeBomb(Vector3 pos, bool armed)
    {
        GameObject b = new GameObject("Bomb");
        b.transform.position = pos;
        Material body = Art.Mat(new Color(0.12f, 0.12f, 0.1f), Color.black, 0f, default(Color), 0.4f, 0.3f);
        Material brick = Art.Mat(new Color(0.75f, 0.68f, 0.5f), Color.black, 0f, default(Color), 0.2f, 0f);
        Art.Box(b.transform, new Vector3(0f, 0.09f, 0f), new Vector3(0.46f, 0.18f, 0.3f), body);
        for (int i = 0; i < 3; i++) Art.Box(b.transform, new Vector3(-0.14f + i * 0.14f, 0.2f, 0f), new Vector3(0.12f, 0.07f, 0.26f), brick);
        Art.Box(b.transform, new Vector3(0f, 0.25f, 0.05f), new Vector3(0.2f, 0.03f, 0.1f), Art.Glow(armed ? Art.Red : Art.Yellow, 1.6f));
        Light l = Art.PointLight(b.transform, new Vector3(0f, 0.5f, 0f), armed ? Art.Red : Art.Yellow, 6f, 1.5f);
        if (armed) bombLamp = l;
        return b;
    }

    void Marker(Camera cam, float s, Vector3 world, string text, Color c)
    {
        Vector3 sp = cam.WorldToScreenPoint(world);
        if (sp.z <= 0f) return;
        float x = sp.x / s, y = (Screen.height - sp.y) / s;
        float dist = Vector3.Distance(cam.transform.position, world);
        Game.Fill(x - 22f, y - 22f, 44f, 44f, new Color(0f, 0f, 0f, 0.55f));
        Game.Text(x - 22f, y - 22f, 44f, 44f, text, 26, c, TextAnchor.MiddleCenter);
        Game.Text(x - 60f, y + 22f, 120f, 22f, Mathf.RoundToInt(dist) + " m", 14, Color.white, TextAnchor.MiddleCenter);
    }

    void OnGUI()
    {
        Game g = Game.I;
        if (g == null || g.player == null || g.pc == null || g.pc.cam == null || g.menus.IsOpen) return;
        float s = Screen.height / 1080f;
        GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));
        float cx = Screen.width / s / 2f;
        Camera cam = g.pc.cam;
        if (phase == Phase.Planted) Marker(cam, s, bombPos + Vector3.up * 1.2f, "!", Art.Red);
        else
        {
            Marker(cam, s, sites[0] + Vector3.up * 2.5f, "A", Art.Red);
            Marker(cam, s, sites[1] + Vector3.up * 2.5f, "B", Art.Yellow);
            if (dropObj != null) Marker(cam, s, dropObj.transform.position + Vector3.up, "*", Art.Yellow);
        }
        Actor p = g.player;
        string hint = "";
        float prog = -1f;
        if (phase == Phase.Freeze) hint = "PREPARE-SE";
        else if (phase == Phase.Live && carrier == p)
        {
            bool inSite = InSite(p.transform.position) >= 0;
            hint = inSite ? "SEGURE F PARA PLANTAR A BOMBA" : "VOCE ESTA COM A BOMBA  -  LEVE ATE A OU B";
            if (plantProg > 0f) prog = plantProg / PlantTime;
        }
        else if (phase == Phase.Planted && p.team == Defenders && p.alive && (p.transform.position - bombPos).sqrMagnitude < 1.9f * 1.9f)
        {
            hint = "SEGURE F PARA DESARMAR";
            if (defuseProg > 0f) prog = defuseProg / DefuseTime;
        }
        else if (phase == Phase.Planted) hint = p.team == Attackers ? "PROTEJA A BOMBA ATE EXPLODIR" : "DESARME A BOMBA";
        if (hint != "") Game.Text(cx - 500f, 96f, 1000f, 34f, hint, 22, Art.Yellow, TextAnchor.MiddleCenter);
        if (prog >= 0f)
        {
            Game.Fill(cx - 160f, 640f, 320f, 18f, new Color(0f, 0f, 0f, 0.6f));
            Game.Fill(cx - 158f, 642f, 316f * Mathf.Clamp01(prog), 14f, Art.Yellow);
        }
        if (!p.alive && phase != Phase.Over) Game.Text(cx - 500f, 600f, 1000f, 34f, "AGUARDE A PROXIMA RODADA", 22, Color.white, TextAnchor.MiddleCenter);
    }
}
