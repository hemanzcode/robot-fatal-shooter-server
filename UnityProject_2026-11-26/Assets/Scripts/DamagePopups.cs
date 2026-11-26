using System.Collections.Generic;
using UnityEngine;

// Numeros de dano estilo "battle royale": aparecem no ponto do impacto, sobem um pouco e somem.
// Amarelo = corpo, laranja = peito/pescoco, vermelho = cabeca.
// Dano que o proprio jogador leva da sua explosao aparece em roxo, perto da barra de vida.
public class DamagePopups : MonoBehaviour
{
    struct Pop { public Vector3 pos; public Vector2 jitter; public int value; public int region; public float born; }
    struct SelfPop { public int value; public string src; public float born; }

    static readonly List<Pop> pops = new List<Pop>();
    static readonly List<SelfPop> selfPops = new List<SelfPop>();
    const float Life = 0.95f, SelfLife = 1.6f;
    const int Max = 40;

    static readonly Color Yellow = new Color(1f, 0.9f, 0.15f);
    static readonly Color Orange = new Color(1f, 0.55f, 0.08f);
    static readonly Color Red = new Color(1f, 0.16f, 0.12f);
    static readonly Color SelfColor = new Color(0.82f, 0.35f, 1f);

    public static void Show(Vector3 worldPos, float value, int region)
    {
        int v = Mathf.RoundToInt(value);
        if (v <= 0) return;
        if (pops.Count >= Max) pops.RemoveAt(0);
        pops.Add(new Pop { pos = worldPos, value = v, region = region, born = Time.time, jitter = new Vector2(Random.Range(-28f, 28f), Random.Range(-10f, 10f)) });
    }

    public static void Self(float value, string weapon)
    {
        int v = Mathf.RoundToInt(value);
        if (v <= 0) return;
        if (selfPops.Count >= 6) selfPops.RemoveAt(0);
        selfPops.Add(new SelfPop { value = v, src = weapon == "GRANADA" ? "SUA GRANADA" : "SUA EXPLOSAO", born = Time.time });
    }

    public static void Clear() { pops.Clear(); selfPops.Clear(); }

    static Color RegionColor(int r) { return r == 2 ? Red : r == 1 ? Orange : Yellow; }

    // Texto com contorno escuro (leitura em qualquer fundo).
    static void Outlined(float x, float y, float w, float h, string s, int size, Color c, TextAnchor a)
    {
        Color sh = new Color(0f, 0f, 0f, c.a * 0.85f);
        Game.Text(x - 2f, y, w, h, s, size, sh, a); Game.Text(x + 2f, y, w, h, s, size, sh, a);
        Game.Text(x, y - 2f, w, h, s, size, sh, a); Game.Text(x, y + 2f, w, h, s, size, sh, a);
        Game.Text(x, y, w, h, s, size, c, a);
    }

    void OnGUI()
    {
        if (Event.current.type != EventType.Repaint) return;
        if (Game.I == null || Game.I.state == Game.State.Menu) { if (pops.Count > 0 || selfPops.Count > 0) Clear(); return; }
        if (pops.Count == 0 && selfPops.Count == 0) return;
        Camera cam = Game.I.pc != null && Game.I.pc.cam != null ? Game.I.pc.cam : Camera.main;
        float s = Screen.height / 1080f;
        GUI.depth = 5;
        GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));
        Game.EnsureStyle();
        float now = Time.time;

        for (int i = pops.Count - 1; i >= 0; i--)
        {
            Pop p = pops[i];
            float t = (now - p.born) / Life;
            if (t >= 1f || t < 0f) { pops.RemoveAt(i); continue; }
            if (cam == null) continue;
            // Sobe 0,7 m com desaceleracao; "estoura" maior no comeco e some no final.
            Vector3 wp = p.pos + Vector3.up * (0.7f * (1f - (1f - t) * (1f - t)));
            Vector3 sp = cam.WorldToScreenPoint(wp);
            if (sp.z <= 0.1f) continue;
            float x = sp.x / s + p.jitter.x, y = (Screen.height - sp.y) / s + p.jitter.y;
            float pop = t < 0.12f ? Mathf.Lerp(1.45f, 1f, t / 0.12f) : 1f;
            float alpha = t > 0.65f ? 1f - (t - 0.65f) / 0.35f : 1f;
            int size = Mathf.RoundToInt((p.region == 2 ? 44f : p.region == 1 ? 38f : 34f) * pop);
            Color c = RegionColor(p.region); c.a = alpha;
            Outlined(x - 120f, y - 40f, 240f, 80f, p.value.ToString(), size, c, TextAnchor.MiddleCenter);
        }

        // Dano da propria explosao: roxo, acima do numero de vida (canto inferior esquerdo).
        float vh = 1080f;
        for (int i = selfPops.Count - 1; i >= 0; i--)
        {
            SelfPop p = selfPops[i];
            float t = (now - p.born) / SelfLife;
            if (t >= 1f || t < 0f) { selfPops.RemoveAt(i); continue; }
            float rise = 70f * (1f - (1f - t) * (1f - t));
            float alpha = t > 0.6f ? 1f - (t - 0.6f) / 0.4f : 1f;
            float pop = t < 0.1f ? Mathf.Lerp(1.4f, 1f, t / 0.1f) : 1f;
            float y = vh - 230f - rise - i * 8f;
            Color c = SelfColor; c.a = alpha;
            Outlined(40f, y, 400f, 60f, "-" + p.value, Mathf.RoundToInt(46f * pop), c, TextAnchor.MiddleLeft);
            Color l = new Color(1f, 1f, 1f, alpha * 0.85f);
            Outlined(40f, y + 46f, 400f, 28f, p.src, 18, l, TextAnchor.MiddleLeft);
        }
    }
}
