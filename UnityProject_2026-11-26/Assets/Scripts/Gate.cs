using System.Collections.Generic;
using UnityEngine;

// Portao de metal do mapa NO GELO: a folha corre para dentro do muro (aberto) ou volta (fechado). Tecla F perto dele alterna.
// Quem manda e o host (ou o jogo offline): clientes pedem com "K;indice" e o host espalha o estado "K;0101...".
// Bot que chega num portao fechado abre sozinho (nao fica preso).
public class Gate : MonoBehaviour
{
    public static readonly List<Gate> All = new List<Gate>();

    public int index;
    public bool open = true;
    Transform leaf;
    Vector3 slide;
    Renderer lampOpen, lampClosed;
    float k = 1f;              // 0 fechado, 1 aberto (animado)
    float botCheck;
    const float Height = 3.4f;

    public static void Build(Transform root, int x, int y, Vector3 p, bool wallsOnX, Material frame, Material dark)
    {
        // wallsOnX: muros a esquerda e direita (passagem no eixo Z); a folha fica atravessada no eixo X.
        Vector3 across = wallsOnX ? Vector3.right : Vector3.forward;
        Quaternion rot = Quaternion.LookRotation(wallsOnX ? Vector3.forward : Vector3.right);
        float w = Arena.Cell;
        GameObject g = new GameObject("gate");
        g.transform.SetParent(root, false);
        g.transform.position = p;
        Gate gate = g.AddComponent<Gate>();
        gate.index = All.Count;
        All.Add(gate);

        // Batentes e caixa em cima (onde a folha some quando abre).
        Art.Box(g.transform, across * (w / 2f - 0.2f) + Vector3.up * 2.4f, wallsOnX ? new Vector3(0.4f, 4.8f, 0.7f) : new Vector3(0.7f, 4.8f, 0.4f), frame, true, "gatePost");
        Art.Box(g.transform, -across * (w / 2f - 0.2f) + Vector3.up * 2.4f, wallsOnX ? new Vector3(0.4f, 4.8f, 0.7f) : new Vector3(0.7f, 4.8f, 0.4f), frame, true, "gatePost");
        GameObject box = Art.Box(g.transform, Vector3.up * 3.95f, Vector3.one, frame, true, "gateLintel");
        box.transform.rotation = rot;
        box.transform.localScale = new Vector3(w, 0.7f, 0.9f);
        gate.slide = across * (w - 0.7f);   // aberto: a folha entra no muro ao lado

        // Folha: chapa de aco com faixa zebrada.
        Material plate = Art.Pbr("DiamondPlate008C", new Color(0.75f, 0.8f, 0.88f), 1.5f, 1f, 0.3f, 0f);
        GameObject lf = new GameObject("gateLeaf");
        lf.transform.SetParent(g.transform, false);
        lf.transform.localPosition = Vector3.zero;
        gate.leaf = lf.transform;
        GameObject body = Art.Box(lf.transform, Vector3.up * (Height / 2f), Vector3.one, plate, true, "leafBody");
        body.transform.rotation = rot;
        body.transform.localScale = new Vector3(w - 0.8f, Height, 0.22f);
        for (int i = 0; i < 6; i++)
        {
            GameObject st = Art.Box(lf.transform, Vector3.up * 0.25f + across * ((i - 2.5f) * 0.55f), Vector3.one, i % 2 == 0 ? Art.Mat(new Color(0.95f, 0.7f, 0.05f), Color.black, 0f, default(Color), 0.4f, 0f) : dark, false, "stripe");
            st.transform.rotation = rot * Quaternion.Euler(0f, 0f, 35f);
            st.transform.localScale = new Vector3(0.32f, 0.5f, 0.26f);
        }

        // Luzes de status nos dois lados: verde aberto, vermelho fechado.
        Vector3 fwd = rot * Vector3.forward;
        gate.lampOpen = Art.Box(g.transform, across * (w / 2f - 0.2f) + Vector3.up * 3.3f + fwd * 0.38f, new Vector3(0.18f, 0.18f, 0.06f), Art.Glow(Art.Green, 1.4f), false, "lampOpen").GetComponent<Renderer>();
        gate.lampClosed = Art.Box(g.transform, across * (w / 2f - 0.2f) + Vector3.up * 3.3f - fwd * 0.38f, new Vector3(0.18f, 0.18f, 0.06f), Art.Glow(Art.Red, 1.4f), false, "lampClosed").GetComponent<Renderer>();
        gate.Apply(true);
    }

    void Apply(bool instant)
    {
        if (instant) k = open ? 1f : 0f;
        if (leaf != null) leaf.position = transform.position + slide * k;
        if (lampOpen != null) lampOpen.enabled = open;
        if (lampClosed != null) lampClosed.enabled = !open;
    }

    void Update()
    {
        float target = open ? 1f : 0f;
        if (!Mathf.Approximately(k, target))
        {
            k = Mathf.MoveTowards(k, target, Time.deltaTime / 0.7f);
            Apply(false);
        }
        // Bots (e criaturas) abrem o portao fechado quando chegam nele.
        if (!open && !NetGame.IsClient && Time.time >= botCheck && Game.I != null)
        {
            botCheck = Time.time + 0.25f;
            for (int i = 0; i < Game.I.actors.Count; i++)
            {
                Actor a = Game.I.actors[i];
                if (a == null || !a.alive || a.isPlayer || a.GetComponent<NetPuppet>() != null) continue;
                Vector3 d = a.transform.position - transform.position; d.y = 0f;
                if (d.sqrMagnitude < 2.6f * 2.6f && Mathf.Abs(a.transform.position.y - transform.position.y) < 2f) { Set(true); break; }
            }
        }
    }

    public static Gate Near(Vector3 pos, float range)
    {
        Gate best = null; float bd = range * range;
        for (int i = 0; i < All.Count; i++)
        {
            Gate g = All[i];
            if (g == null) continue;
            Vector3 d = g.transform.position - pos; d.y = 0f;
            if (Mathf.Abs(pos.y - g.transform.position.y) > 2.5f) continue;
            float s = d.sqrMagnitude;
            if (s < bd) { bd = s; best = g; }
        }
        return best;
    }

    // Jogador local apertou F: offline/host muda na hora; cliente pede ao host.
    public void Request()
    {
        if (NetGame.IsClient) { if (Net.I != null) Net.I.SendGame(-1, "K;" + index); return; }
        Set(!open);
    }

    // So o host/offline chama.
    public void Set(bool o)
    {
        if (open == o) return;
        open = o;
        Sfx.At("gate", transform.position + Vector3.up * 2f, 0.9f, o ? 1f : 0.85f);
        if (NetGame.Online && !NetGame.IsClient) NetGame.SendGates();
    }

    public static string State()
    {
        char[] c = new char[All.Count];
        for (int i = 0; i < All.Count; i++) c[i] = All[i] != null && All[i].open ? '1' : '0';
        return new string(c);
    }

    // Cliente: estado vindo do host.
    public static void ApplyState(string s)
    {
        for (int i = 0; i < All.Count && i < s.Length; i++)
        {
            Gate g = All[i];
            if (g == null) continue;
            bool o = s[i] == '1';
            if (g.open != o) { g.open = o; Sfx.At("gate", g.transform.position + Vector3.up * 2f, 0.9f, o ? 1f : 0.85f); }
        }
    }

    // Partida nova: todos abertos.
    public static void ResetAll()
    {
        for (int i = 0; i < All.Count; i++) if (All[i] != null) { All[i].open = true; All[i].Apply(true); }
    }
}
