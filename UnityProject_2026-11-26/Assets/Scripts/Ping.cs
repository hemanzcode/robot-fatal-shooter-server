using System.Collections.Generic;
using UnityEngine;

// Marcacao ("ping") para o time: Z ou clique do botao do meio do mouse no PC; toque rapido na tela no celular.
// Marca o que estiver na mira: inimigo, vida, municao, caixa, jetpack, veiculo ou so o lugar.
// Online: os companheiros de time veem a marca, com o nome de quem marcou e a distancia.
public class Ping : MonoBehaviour
{
    public static Ping I;

    class Mark { public Vector3 pos; public Actor actor; public int kind; public string who; public float born, until; }
    readonly List<Mark> marks = new List<Mark>();
    float nextAllowed;

    // 0 lugar, 1 inimigo, 2 vida, 3 municao, 4 caixa, 5 jetpack, 6 veiculo
    static readonly string[] Labels = { "AQUI", "INIMIGO AQUI", "VIDA AQUI", "MUNICAO AQUI", "CAIXA AQUI", "JETPACK AQUI", "VEICULO AQUI" };
    static Color KindColor(int k)
    {
        switch (k)
        {
            case 1: return new Color(1f, 0.22f, 0.2f);
            case 2: return new Color(0.3f, 1f, 0.45f);
            case 3: return new Color(1f, 0.6f, 0.15f);
            case 4: return new Color(1f, 0.45f, 0.05f);
            case 5: return new Color(0.3f, 0.85f, 1f);
            case 6: return new Color(0.9f, 0.9f, 1f);
            default: return new Color(1f, 0.9f, 0.25f);
        }
    }

    void Awake() { I = this; }

    void Update()
    {
        Game g = Game.I;
        for (int i = marks.Count - 1; i >= 0; i--) if (Time.time > marks[i].until) marks.RemoveAt(i);
        if (g == null || g.state == Game.State.Menu || g.menus.IsOpen || g.matchOver) { marks.Clear(); return; }
        if (g.player == null || !g.player.alive || g.pc == null || g.pc.cam == null) return;
        Camera cam = g.pc.cam;
        if (Input.GetKeyDown(KeyCode.Z) || Input.GetMouseButtonDown(2))
            Do(cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f)));
        else if (TouchPad.PingTap)
            Do(cam.ScreenPointToRay(TouchPad.PingPos));
    }

    static float Angle(Ray r, Vector3 p) { return Vector3.Angle(r.direction, p - r.origin); }

    void Do(Ray r)
    {
        if (Time.time < nextAllowed) return;
        nextAllowed = Time.time + 0.5f;
        Game g = Game.I;
        Actor me = g.player;

        // Ponto que a mira toca (ignorando o proprio corpo).
        Vector3 point = r.origin + r.direction * 60f;
        Actor hitActor = null;
        RaycastHit[] hits = Physics.RaycastAll(r, 300f, ~0, QueryTriggerInteraction.Ignore);
        float best = float.MaxValue;
        for (int i = 0; i < hits.Length; i++)
        {
            Actor a = hits[i].collider.GetComponentInParent<Actor>();
            if (a == me) continue;
            if (hits[i].distance < best) { best = hits[i].distance; point = hits[i].point; hitActor = a; }
        }

        int kind = 0; Actor target = null; Vector3 pos = point;
        if (hitActor != null && hitActor.alive && hitActor.team != me.team) { kind = 1; target = hitActor; pos = hitActor.ChestPos; }
        else
        {
            // Perto da linha da mira: inimigo visivel primeiro, depois itens.
            float bestAng = 3.5f;
            for (int i = 0; i < g.actors.Count; i++)
            {
                Actor a = g.actors[i];
                if (a == null || !a.alive || a.team == me.team) continue;
                float ang = Angle(r, a.ChestPos);
                if (ang >= bestAng) continue;
                RaycastHit h;
                if (Physics.Linecast(r.origin, a.ChestPos, out h, ~0, QueryTriggerInteraction.Ignore) && h.collider.GetComponentInParent<Actor>() != a) continue;
                bestAng = ang; kind = 1; target = a; pos = a.ChestPos;
            }
            if (kind == 0)
            {
                bestAng = 4.5f;
                foreach (Pickup p in Object.FindObjectsByType<Pickup>(FindObjectsSortMode.None))
                {
                    Vector3 pp = p.transform.position + Vector3.up * 0.9f;
                    float ang = Angle(r, pp);
                    if (ang < bestAng && (pp - r.origin).sqrMagnitude < 150f * 150f) { bestAng = ang; kind = p.kind == 0 ? 2 : 3; pos = pp; }
                }
                for (int i = 0; i < SupplyCrate.All.Count; i++)
                {
                    SupplyCrate c = SupplyCrate.All[i];
                    if (c == null) continue;
                    Vector3 cp = c.transform.position + Vector3.up * 0.8f;
                    float ang = Angle(r, cp);
                    if (ang < bestAng) { bestAng = ang; kind = 4; pos = cp; }
                }
                foreach (JetpackPickup j in Object.FindObjectsByType<JetpackPickup>(FindObjectsSortMode.None))
                {
                    Vector3 jp = j.transform.position + Vector3.up * 1f;
                    float ang = Angle(r, jp);
                    if (ang < bestAng) { bestAng = ang; kind = 5; pos = jp; }
                }
                for (int i = 0; i < Vehicle.All.Count; i++)
                {
                    Vehicle v = Vehicle.All[i];
                    if (v == null) continue;
                    Vector3 vp = v.transform.position + Vector3.up * 1.2f;
                    float ang = Angle(r, vp);
                    if (ang < bestAng) { bestAng = ang; kind = 6; pos = vp; }
                }
            }
        }

        string who = me.isPlayer && Net.I != null && Net.I.InRoom ? Net.I.userName : me.displayName;
        Add(pos, target, kind, who);
        NetGame.SendPing(me.team, kind, target, pos, who);
    }

    public void Add(Vector3 pos, Actor target, int kind, string who)
    {
        // No maximo 2 marcas por pessoa: a mais antiga sai.
        int count = 0;
        for (int i = marks.Count - 1; i >= 0; i--) if (marks[i].who == who && ++count >= 2) marks.RemoveAt(i);
        marks.Add(new Mark { pos = pos, actor = target, kind = kind, who = who, born = Time.time, until = Time.time + (kind == 1 ? 5f : 8f) });
        if (Game.I != null)
        {
            Game.I.Announce(who + ":  " + Labels[kind], KindColor(kind));
            Sfx.UI("beep", 0.7f, kind == 1 ? 1.5f : 1.15f);
        }
    }

    void OnGUI()
    {
        Game g = Game.I;
        if (marks.Count == 0 || g == null || g.pc == null || g.pc.cam == null || g.state == Game.State.Menu || g.menus.IsOpen) return;
        Camera cam = g.pc.cam;
        float s = Screen.height / 1080f;
        GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));
        float vw = Screen.width / s, vh = 1080f;
        Vector3 eye = cam.transform.position;
        for (int i = 0; i < marks.Count; i++)
        {
            Mark m = marks[i];
            if (m.actor != null && m.actor.alive) m.pos = m.actor.ChestPos + Vector3.up * 0.6f;
            Vector3 sp = cam.WorldToScreenPoint(m.pos);
            bool behind = sp.z < 0f;
            float x = sp.x / s, y = (Screen.height - sp.y) / s;
            if (behind) { x = vw - x; y = vh - 80f; }
            bool edge = behind || x < 40f || x > vw - 40f || y < 60f || y > vh - 60f;
            x = Mathf.Clamp(x, 40f, vw - 40f); y = Mathf.Clamp(y, 60f, vh - 60f);

            Color c = KindColor(m.kind);
            float age = Time.time - m.born, fade = Mathf.Clamp01((m.until - Time.time) / 0.6f);
            float pop = 1f + Mathf.Max(0f, 0.6f - age) * 1.2f;
            float sz = 26f * pop;

            // Losango (quadrado girado 45 graus) com borda escura e anel pulsando no comeco.
            Matrix4x4 keep = GUI.matrix;
            GUIUtility.RotateAroundPivot(45f, new Vector2(x, y) * s);
            Game.Fill(x - sz / 2f - 3f, y - sz / 2f - 3f, sz + 6f, sz + 6f, new Color(0f, 0f, 0f, 0.7f * fade));
            Game.Fill(x - sz / 2f, y - sz / 2f, sz, sz, new Color(c.r, c.g, c.b, 0.95f * fade));
            if (age < 1f)
            {
                float ring = sz + age * 70f;
                Color rc = new Color(c.r, c.g, c.b, (1f - age) * 0.8f * fade);
                Game.Fill(x - ring / 2f, y - ring / 2f, ring, 3f, rc);
                Game.Fill(x - ring / 2f, y + ring / 2f - 3f, ring, 3f, rc);
                Game.Fill(x - ring / 2f, y - ring / 2f, 3f, ring, rc);
                Game.Fill(x + ring / 2f - 3f, y - ring / 2f, 3f, ring, rc);
            }
            GUI.matrix = keep;
            Game.Text(x - 9f, y - 14f, 18f, 28f, m.kind == 1 ? "!" : (m.kind == 2 ? "+" : ""), 22, new Color(0f, 0f, 0f, fade), TextAnchor.MiddleCenter);

            int dist = Mathf.RoundToInt(Vector3.Distance(eye, m.pos));
            string label = (edge ? "" : "") + Labels[m.kind] + "  " + dist + " m";
            Game.Fill(x - 120f, y + 24f, 240f, 30f, new Color(0f, 0f, 0f, 0.55f * fade));
            Game.Text(x - 120f, y + 24f, 240f, 30f, label, 19, new Color(c.r, c.g, c.b, fade), TextAnchor.MiddleCenter);
            Game.Text(x - 120f, y + 54f, 240f, 22f, m.who, 15, new Color(1f, 1f, 1f, 0.8f * fade), TextAnchor.MiddleCenter);
        }
    }
}
