using System.Collections.Generic;
using UnityEngine;

// MODO CAMPANHA - Missao 1: "Mercado".
// O esquadrao BAAL (voce, KATE e HAROLD) entra no mercado para resgatar SANKHYA, presa por BRUTUS (MOLOCH).
// Os aliados e os inimigos conversam com voce: as falas vem de uma IA no servidor (se voce estiver logado);
// sem conta, sem chave ou com o servidor lento, o jogo usa as falas escritas aqui.
public class Campaign : MonoBehaviour
{
    public enum Step { Brief, ToMarket, Complex, Rescue, Extract, Won, Lost }
    public Step step = Step.Brief;
    public bool won;
    public string failReason = "";

    const int KATE = 2, HAROLD = 3, BRUTUS = 6, SANKHYA = 7;

    // Falas prontas (usadas quando a IA nao responde). Chave = evento.
    static readonly Dictionary<string, string[]> Script = new Dictionary<string, string[]> {
        { "inicio|HAROLD",       new[] { "Esquadrao BAAL, aqui e o Harold. Sankhya esta presa no complexo do lado oeste do mercado." } },
        { "inicio|KATE",         new[] { "Entrar, resgatar e sair. Se der para explodir alguma coisa no caminho, melhor ainda." } },
        { "checkpoint|HAROLD",   new[] { "Centro do mercado. O complexo fica a oeste. Cuidado com os telhados." } },
        { "checkpoint|KATE",     new[] { "Estou na sua cobertura, novato. Vai na frente." } },
        { "contato|KATE",        new[] { "Um a menos! Eles sabem que estamos aqui agora." } },
        { "contato|HAROLD",      new[] { "Contato confirmado. Mantenham o esquadrao junto." } },
        { "chefe_provoca|BRUTUS",new[] { "Entraram no meu quintal, BAAL. Vao sair daqui em pedacos!" } },
        { "aproximando|HAROLD",  new[] { "Sinal da Sankhya a poucos metros. Brutus deve estar por perto." } },
        { "chefe_cai|KATE",      new[] { "O gigante caiu! Corre, solta a Sankhya!" } },
        { "resgatando|SANKHYA",  new[] { "Aguenta, eu ja estou saindo desta corda..." } },
        { "resgatada|SANKHYA",   new[] { "Livre! Obrigada, esquadrao. Agora me tirem daqui." } },
        { "resgatada|HAROLD",    new[] { "Extracao na base BAAL. Todos de volta, o caminho vai estar quente." } },
        { "vida_baixa|SANKHYA",  new[] { "Voce esta ferido! Chega perto de mim que eu cuido disso." } },
        { "extracao|HAROLD",     new[] { "Extracao confirmada. Missao cumprida, esquadrao. Bom trabalho." } },
        { "morte|HAROLD",        new[] { "Perdemos o contato... Esquadrao, abortar." } },
    };

    class Line { public string who, text; public float until; public bool ai; }
    class Pending { public string who, ev; public float deadline; }
    readonly List<Line> lines = new List<Line>();
    readonly Dictionary<int, Pending> pending = new Dictionary<int, Pending>();
    static int rid;
    static Campaign inst;

    Actor player, kate, harold, sankhya, brutus;
    readonly List<Actor> foes = new List<Actor>();
    Vector3 market, hostagePos, extract;
    float t0, nextLook, rescueProg, healAt, endAt, nextFollow, nextChat;
    int kills0, briefIdx;
    bool saidContact, saidProvoke, saidNear, saidLow, saidFall, reinforced;
    string objective = "";
    int chatSample;

    static readonly Color Gold = new Color(1f, 0.86f, 0.1f);
    GUIStyle sub;
    bool god;

    static Vector3 Near(int cx, int cy)
    {
        for (int r = 0; r < 8; r++)
            for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                    if (Arena.Walk(cx + dx, cy + dy) && Arena.Walk(cx + dx + 1, cy + dy) && Arena.Walk(cx + dx, cy + dy + 1)) return Arena.Center(cx + dx, cy + dy);
        return Arena.Center(cx, cy);
    }

    Actor Spawn(string n, int team, bool ai, int agent, Vector3 pos, float yaw)
    {
        Actor a = Game.I.MakeActor(n, team, false, ai, agent);
        a.Respawn(pos, yaw);
        return a;
    }

    void Start()
    {
        inst = this;
        useGUILayout = false;
        Game g = Game.I;
        g.roundMode = true;          // sem renascer, sem relogio
        player = g.player;
        t0 = Time.time;
        extract = Arena.spawnsA.Count > 0 ? Arena.spawnsA[0] : Arena.Center(5, 2);
        market = Near(Arena.W / 2, Arena.H / 2);
        hostagePos = Near(3, 17);
        Vector3 toMarket = (market - extract); toMarket.y = 0f;
        float yaw = Mathf.Atan2(toMarket.x, toMarket.z) * Mathf.Rad2Deg;

        kate = Spawn("KATE", 0, true, KATE, extract + new Vector3(1.8f, 0f, 1f), yaw);
        harold = Spawn("HAROLD", 0, true, HAROLD, extract + new Vector3(-1.8f, 0f, 1f), yaw);
        kate.maxHealth = kate.health = 260f; harold.maxHealth = harold.health = 260f;

        // Sankhya amarrada (nao age ate ser libertada) e Brutus de guarda ao lado dela.
        sankhya = Spawn("SANKHYA", 0, false, SANKHYA, hostagePos, 90f);
        sankhya.maxHealth = sankhya.health = 400f;
        sankhya.invulnerableUntil = float.MaxValue;
        brutus = Spawn("BRUTUS", 1, true, BRUTUS, hostagePos + new Vector3(3f, 0f, 1f), -90f);
        brutus.maxHealth = brutus.health = 700f;
        Hold(brutus, brutus.transform.position);
        foes.Add(brutus);

        // Guardas: alguns no caminho (centro) e alguns no complexo.
        int[] pool = { 0, 1, 4, 5, 8, 9, 10, 11 };
        int pi = 0;
        for (int i = 0; i < 9; i++)
        {
            Vector3 p = Vector3.zero; bool ok = false;
            for (int tries = 0; tries < 40 && !ok; tries++)
            {
                p = Arena.RandomPoint();
                Vector3 d = p - (i < 4 ? market : hostagePos); d.y = 0f;
                Vector3 e = p - extract; e.y = 0f;
                ok = d.magnitude < (i < 4 ? 12f : 9f) && e.magnitude > 24f;
            }
            if (!ok) continue;
            int ag = pool[pi++ % pool.Length];
            if (ag == Settings.agent) ag = pool[pi++ % pool.Length];
            Actor f = Spawn("SOLDADO MOLOCH", 1, true, ag, p, Random.Range(0f, 360f));
            Hold(f, p);
            foes.Add(f);
        }
        kills0 = player != null ? player.kills : 0;
        objective = "OUCA O BRIEFING";
        g.Banner("MISSAO 1  -  MERCADO");
    }

    static void Hold(Actor a, Vector3 pos)
    {
        Bot b = a.GetComponent<Bot>();
        if (b == null) return;
        b.hasGoal = true; b.hold = true; b.goalPos = pos;
    }

    void OnDestroy()
    {
        if (inst == this) inst = null;
        if (Game.I != null) Game.I.roundMode = false;
    }

    // ---------- falas ----------

    static bool AiOn { get { return Net.I != null && Net.I.loggedIn && Net.I.connected; } }

    void Say(string who, string text, bool ai = false, float secs = 5f)
    {
        if (string.IsNullOrEmpty(text)) return;
        lines.Add(new Line { who = who, text = text, until = Time.time + secs, ai = ai });
        if (lines.Count > 3) lines.RemoveAt(0);
        Sfx.UI("pickup", 0.35f);
    }

    string Scripted(string who, string ev)
    {
        string[] a;
        if (Script.TryGetValue(ev + "|" + who, out a)) return a[Random.Range(0, a.Length)];
        return "";
    }

    string Context()
    {
        int alive = 0; foreach (Actor f in foes) if (f != null && f.alive) alive++;
        return "etapa " + step + ", vida do jogador " + (player != null ? Mathf.RoundToInt(player.health) : 0) + "/" + (player != null ? Mathf.RoundToInt(player.maxHealth) : 0) + ", inimigos vivos " + alive + (sankhya != null && step >= Step.Extract ? ", Sankhya resgatada" : "");
    }

    // Uma fala de um personagem para um evento: pede a IA e, se ela nao vier, usa a fala pronta.
    void Speak(string who, string ev)
    {
        string fallback = Scripted(who, ev);
        if (AiOn)
        {
            int id = ++rid;
            pending[id] = new Pending { who = who, ev = ev, deadline = Time.time + 3.5f };
            Net.I.NpcAsk(who, ev, Context(), "", id);
        }
        else Say(who, fallback);
    }

    // Chegou a resposta do servidor.
    public static void OnReply(NetMsg m)
    {
        if (inst == null) return;
        inst.GotReply(m);
    }

    void GotReply(NetMsg m)
    {
        Pending p;
        if (!pending.TryGetValue(m.id, out p)) return;
        pending.Remove(m.id);
        if (m.ok && !string.IsNullOrEmpty(m.d)) Say(p.who, m.d, true, 6f);
        else Say(p.who, Scripted(p.who, p.ev));
    }

    // O jogador fala no radio (tecla T).
    void Chat()
    {
        if (Time.time < nextChat) return;
        nextChat = Time.time + 3f;
        string who = (kate != null && kate.alive && Vector3.Distance(kate.transform.position, player.transform.position) < 25f) ? "KATE" : "HAROLD";
        string[] samples = { "Qual e o plano?", "Voces estao bem?", "Tem inimigo perto?", "Estou ferido, me cubram!" };
        string said = TouchPad.Prompt("Fale com o esquadrao (radio)", samples[chatSample++ % samples.Length]);
        if (string.IsNullOrEmpty(said)) return;
        if (said.Length > 140) said = said.Substring(0, 140);
        Say("VOCE", said, false, 4f);
        if (AiOn)
        {
            int id = ++rid;
            pending[id] = new Pending { who = who, ev = "conversa", deadline = Time.time + 5f };
            Net.I.NpcAsk(who, "conversa", Context(), said, id);
        }
        else Say(who, who == "KATE" ? "Sem sinal de IA agora, novato. Foco na missao!" : "Radio com chiado. Entre na conta para eu conversar com voce. Foco no objetivo.");
    }

    // ---------- logica ----------

    void Update()
    {
        Game g = Game.I;
        if (g == null || g.player == null || g.matchOver) return;
        player = g.player;
        lines.RemoveAll(l => Time.time > l.until);
        List<int> expired = null;
        foreach (var kv in pending) if (Time.time > kv.Value.deadline) { if (expired == null) expired = new List<int>(); expired.Add(kv.Key); }
        if (expired != null) foreach (int id in expired) { Pending p = pending[id]; pending.Remove(id); Say(p.who, Scripted(p.who, p.ev)); }

        if (step == Step.Won || step == Step.Lost) { if (Time.time > endAt) g.FinishCampaign(won); return; }
        if (!player.alive) { Fail("Voce caiu em combate.", "morte", "HAROLD"); return; }
        if (!g.menus.IsOpen && (Input.GetKeyDown(KeyCode.T))) Chat();
#if UNITY_EDITOR
        // Atalhos de teste (so no editor): F6 invulneravel, F8 leva ao objetivo atual, F7 derruba o Brutus.
        if (Input.GetKeyDown(KeyCode.F8))
        {
            Vector3 dir = market - hostagePos; dir.y = 0f; dir.Normalize();
            Vector3 to = step <= Step.ToMarket ? market : (step <= Step.Rescue ? hostagePos + dir * 1.6f : extract);
            float yaw = step <= Step.ToMarket ? player.transform.eulerAngles.y : Mathf.Atan2(-dir.x, -dir.z) * Mathf.Rad2Deg;
            float hp = player.health; player.Respawn(to, yaw); player.health = hp;
            if (step == Step.Extract && sankhya != null) sankhya.Respawn(to + new Vector3(1.5f, 0f, 0f), 0f);
        }
        if (Input.GetKeyDown(KeyCode.F6)) { god = !god; Game.I.Banner(god ? "TESTE: INVULNERAVEL" : "TESTE: NORMAL"); }
        if (god) player.invulnerableUntil = Time.time + 1f;
        if (Input.GetKeyDown(KeyCode.F7)) foreach (Actor f in foes) if (f != null && f.alive) f.TakeDamage(9999f, player, false, "TESTE");
#endif

        float tt = Time.time - t0;
        Vector3 pp = player.transform.position;
        int myKills = player.kills - kills0;

        if (!saidContact && myKills >= 1 && step >= Step.ToMarket) { saidContact = true; Speak(Random.value < 0.5f ? "KATE" : "HAROLD", "contato"); }
        if (!saidLow && player.alive && player.health < player.maxHealth * 0.3f && step >= Step.Extract && sankhya != null) { saidLow = true; Speak("SANKHYA", "vida_baixa"); }
        if (step >= Step.Extract && sankhya != null && sankhya.alive && Time.time > healAt)
        {
            healAt = Time.time + 1.5f;
            if (Vector3.Distance(sankhya.transform.position, pp) < 7f && player.health < player.maxHealth) player.health = Mathf.Min(player.maxHealth, player.health + 8f);
            // Ela tambem se cura quando passa uns segundos sem levar tiro.
            if (Time.time - sankhya.lastDamageTime > 4f) sankhya.health = Mathf.Min(sankhya.maxHealth, sankhya.health + 15f);
        }

        // Aliados seguem o jogador (e lutam quando veem inimigos).
        if (Time.time > nextFollow)
        {
            nextFollow = Time.time + 0.8f;
            Follow(kate, pp, 4f); Follow(harold, pp, 6.5f);
            if (step >= Step.Extract) Follow(sankhya, pp, 3f);
        }
        if (sankhya != null && !sankhya.alive && step < Step.Won) { Fail("Sankhya nao sobreviveu.", "morte", "HAROLD"); return; }

        switch (step)
        {
            case Step.Brief:
                objective = "OUCA O BRIEFING";
                if (briefIdx == 0 && tt > 0.5f) { briefIdx = 1; Speak("HAROLD", "inicio"); }
                if (briefIdx == 1 && tt > 5f) { briefIdx = 2; Speak("KATE", "inicio"); }
                if (briefIdx == 2 && tt > 9.5f) { step = Step.ToMarket; Sfx.UI("ult", 0.5f); }
                break;
            case Step.ToMarket:
                objective = "CHEGUE AO CENTRO DO MERCADO";
                if (Flat(pp, market) < 8f)
                {
                    step = Step.Complex; Speak("HAROLD", "checkpoint"); Invoke("KateCheck", 3.2f);
                    Game.I.Banner("ELIMINE OS GUARDAS E ENCONTRE A SANKHYA");
                }
                break;
            case Step.Complex:
                objective = "ENCONTRE A SANKHYA  -  DERRUBE O BRUTUS";
                if (!saidNear && Flat(pp, hostagePos) < 32f) { saidNear = true; Speak("HAROLD", "aproximando"); }
                if (!saidProvoke && Flat(pp, hostagePos) < 20f) { saidProvoke = true; Speak("BRUTUS", "chefe_provoca"); }
                if (brutus != null && !brutus.alive)
                {
                    step = Step.Rescue; Speak("KATE", "chefe_cai");
                    Game.I.Banner("SEGURE F PERTO DA SANKHYA PARA LIBERTA-LA");
                }
                break;
            case Step.Rescue:
                objective = "LIBERTE A SANKHYA  (SEGURE F)";
                if (NearHostage(pp) && GameInput.Use && !g.menus.IsOpen)
                {
                    if (rescueProg <= 0f) Speak("SANKHYA", "resgatando");
                    rescueProg += Time.deltaTime;
                    if (rescueProg >= 3f) Free();
                }
                else rescueProg = Mathf.Max(0f, rescueProg - Time.deltaTime * 2f);
                break;
            case Step.Extract:
                objective = "VOLTE PARA A EXTRACAO COM A SANKHYA";
                if (!reinforced && Flat(pp, hostagePos) > 14f) { reinforced = true; Reinforce(); }
                if (Flat(pp, extract) < 6f)
                {
                    if (Flat(sankhya.transform.position, extract) < 14f)
                    {
                        step = Step.Won; won = true; endAt = Time.time + 4f;
                        Speak("HAROLD", "extracao"); Game.I.Banner("MISSAO CUMPRIDA"); Sfx.UI("ult", 0.8f);
                    }
                    else objective = "ESPERE A SANKHYA CHEGAR";
                }
                break;
        }
    }

    void KateCheck() { if (step != Step.Lost && step != Step.Won) Speak("KATE", "checkpoint"); }

    bool NearHostage(Vector3 p) { return sankhya != null && Flat(p, sankhya.transform.position) < 3.2f; }

    static float Flat(Vector3 a, Vector3 b) { a.y = 0f; b.y = 0f; return Vector3.Distance(a, b); }

    void Follow(Actor a, Vector3 target, float dist)
    {
        if (a == null || !a.alive) return;
        Bot b = a.GetComponent<Bot>();
        if (b == null) return;
        bool far = Flat(a.transform.position, target) > dist + 3f;
        b.hold = false;
        b.hasGoal = far;
        if (far) b.goalPos = target;
    }

    void Free()
    {
        step = Step.Extract;
        rescueProg = 0f;
        sankhya.invulnerableUntil = Time.time + 2f;
        sankhya.maxHealth = sankhya.health = 600f;
        if (sankhya.GetComponent<Bot>() == null) sankhya.gameObject.AddComponent<Bot>();
        Speak("SANKHYA", "resgatada");
        Invoke("HaroldGo", 3.5f);
        Game.I.Banner("SANKHYA LIBERTADA  -  VOLTE PARA A BASE");
        Sfx.UI("ult", 0.7f);
    }

    void HaroldGo() { if (step == Step.Extract) Speak("HAROLD", "resgatada"); }

    // Reforcos da MOLOCH pelo caminho de volta.
    void Reinforce()
    {
        int[] pool = { 0, 1, 4, 5, 8, 9 };
        for (int i = 0; i < 4; i++)
        {
            Vector3 p = Arena.RandomPoint();
            for (int tries = 0; tries < 30; tries++)
            {
                p = Arena.RandomPoint();
                if (Flat(p, market) < 14f && Flat(p, player.transform.position) > 12f) break;
            }
            Actor f = Spawn("SOLDADO MOLOCH", 1, true, pool[i % pool.Length], p, Random.Range(0f, 360f));
            foes.Add(f);
        }
        Game.I.Banner("REFORCOS DA MOLOCH A CAMINHO");
    }

    void Fail(string why, string ev, string who)
    {
        if (step == Step.Lost) return;
        step = Step.Lost; won = false; failReason = why; endAt = Time.time + 3f;
        Speak(who, ev);
    }

    // ---------- tela ----------

    void Marker(Camera cam, float s, Vector3 world, string text, Color c)
    {
        Vector3 sp = cam.WorldToScreenPoint(world);
        if (sp.z <= 0f) return;
        float x = sp.x / s, y = (Screen.height - sp.y) / s;
        float dist = Vector3.Distance(cam.transform.position, world);
        Game.Fill(x - 20f, y - 20f, 40f, 40f, new Color(0f, 0f, 0f, 0.55f));
        Game.Text(x - 20f, y - 20f, 40f, 40f, text, 24, c, TextAnchor.MiddleCenter);
        Game.Text(x - 60f, y + 20f, 120f, 22f, Mathf.RoundToInt(dist) + " m", 14, Color.white, TextAnchor.MiddleCenter);
    }

    static Color Who(string who)
    {
        switch (who)
        {
            case "KATE": return new Color(1f, 0.55f, 0.2f);
            case "HAROLD": return new Color(0.45f, 0.8f, 1f);
            case "SANKHYA": return new Color(0.5f, 1f, 0.7f);
            case "BRUTUS": return new Color(1f, 0.3f, 0.3f);
            default: return Color.white;
        }
    }

    void OnGUI()
    {
        Game g = Game.I;
        if (g == null || g.player == null || g.pc == null || g.pc.cam == null || g.menus.IsOpen) return;
        float s = Screen.height / 1080f;
        GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));
        float vw = Screen.width / s, cx = vw / 2f;
        Camera cam = g.pc.cam;

        // Objetivo (no alto, no centro; o canto esquerdo e do minimapa)
        Game.Fill(cx - 330f, 18f, 660f, 78f, new Color(0f, 0f, 0f, 0.6f));
        Game.Fill(cx - 330f, 18f, 6f, 78f, Gold);
        Game.Text(cx - 310f, 20f, 620f, 28f, "MISSAO 1  -  MERCADO", 16, Gold, TextAnchor.MiddleLeft);
        Game.Text(cx - 310f, 46f, 630f, 46f, objective, 22, Color.white, TextAnchor.MiddleLeft);

        // Marcadores
        if (step == Step.ToMarket) Marker(cam, s, market + Vector3.up * 2.4f, ">", Gold);
        else if ((step == Step.Complex || step == Step.Rescue) && sankhya != null) Marker(cam, s, sankhya.transform.position + Vector3.up * 2.4f, "S", Who("SANKHYA"));
        else if (step == Step.Extract) Marker(cam, s, extract + Vector3.up * 2.4f, "E", Gold);
        if (kate != null && kate.alive) Marker(cam, s, kate.transform.position + Vector3.up * 2.3f, "K", Who("KATE"));
        if (harold != null && harold.alive) Marker(cam, s, harold.transform.position + Vector3.up * 2.3f, "H", Who("HAROLD"));
        if (brutus != null && brutus.alive && step == Step.Complex && Flat(player.transform.position, brutus.transform.position) < 40f)
        {
            float f = Mathf.Clamp01(brutus.health / brutus.maxHealth);
            Game.Fill(cx - 220f, 134f, 440f, 12f, new Color(0f, 0f, 0f, 0.7f));
            Game.Fill(cx - 218f, 136f, 436f * f, 8f, Who("BRUTUS"));
            Game.Text(cx - 220f, 108f, 440f, 24f, "BRUTUS  -  CHEFE DA MOLOCH", 16, Who("BRUTUS"), TextAnchor.MiddleCenter);
        }

        // Barra de resgate
        if (step == Step.Rescue && NearHostage(player.transform.position))
        {
            Game.Text(cx - 300f, 600f, 600f, 34f, rescueProg > 0f ? "LIBERTANDO..." : "SEGURE F PARA LIBERTAR A SANKHYA", 24, Color.white, TextAnchor.MiddleCenter);
            Game.Fill(cx - 160f, 640f, 320f, 12f, new Color(0f, 0f, 0f, 0.7f));
            Game.Fill(cx - 158f, 642f, 316f * Mathf.Clamp01(rescueProg / 3f), 8f, Gold);
        }

        // Legendas (acima da linha de habilidades, no centro; quebram linha)
        if (sub == null) { sub = new GUIStyle(GUI.skin.label); sub.wordWrap = true; sub.fontSize = 19; sub.alignment = TextAnchor.MiddleLeft; }
        float y = 790f, bw = 980f, tw = bw - 210f;
        for (int i = lines.Count - 1; i >= 0; i--)
        {
            Line l = lines[i];
            float a = Mathf.Clamp01((l.until - Time.time) * 2f);
            float h = Mathf.Max(50f, sub.CalcHeight(new GUIContent(l.text), tw) + 16f);
            y -= h;
            Game.Fill(cx - bw / 2f, y, bw, h, new Color(0f, 0f, 0f, 0.62f * a));
            Color c = Who(l.who); c.a = a;
            Game.Text(cx - bw / 2f + 14f, y, 150f, h, l.who, 18, c, TextAnchor.MiddleLeft);
            sub.normal.textColor = new Color(1f, 1f, 1f, a);
            GUI.Label(new Rect(cx - bw / 2f + 170f, y, tw, h), l.text, sub);
            if (l.ai) Game.Text(cx + bw / 2f - 40f, y, 36f, h, "IA", 14, new Color(1f, 0.86f, 0.1f, a), TextAnchor.MiddleCenter);
            y -= 6f;
        }
        Game.Text(cx - 300f, 796f, 600f, 22f, "T  falar no radio", 14, new Color(1f, 1f, 1f, 0.45f), TextAnchor.MiddleCenter);
    }
}
