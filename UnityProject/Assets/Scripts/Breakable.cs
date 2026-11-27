using System.Collections.Generic;
using UnityEngine;

// Parede que abre buracos: feita de placas (~0,5 x 0,47 m). Tiros gastam a placa atingida; explosoes arrancam
// as placas em volta. Sem pedacos com fisica: a placa some com um pouco de poeira e a parede vira uma malha so.
// Online, quem quebra avisa os outros ("W;id;placas") e todo mundo ve o mesmo buraco.
public class Breakable : MonoBehaviour
{
    public static readonly List<Breakable> All = new List<Breakable>();
    public const float TileHp = 75f;

    public int id;
    float len, height, thick;
    float[] colX, rowY;          // bordas das colunas e das linhas (locais)
    int nc, nr;
    bool[] hole;                 // vao de porta/janela (nunca tem placa)
    bool[] alive;
    float[] hp;
    BoxCollider[] cols;
    readonly Dictionary<Collider, int> index = new Dictionary<Collider, int>();
    Mesh mesh;
    Color dust;

    // Parede de a ate b (no chao), com vaos: cada vao e {inicio, fim, base, topo} ao longo da parede.
    public static Breakable Create(Transform parent, Vector3 a, Vector3 b, float height, float thick, Material m, Color dust, float[] openings)
    {
        Vector3 dir = b - a; dir.y = 0f;
        float len = dir.magnitude;
        dir /= Mathf.Max(0.001f, len);
        GameObject g = new GameObject("parede");
        g.transform.SetParent(parent, false);
        g.transform.position = a;
        g.transform.rotation = Quaternion.LookRotation(Vector3.Cross(dir, Vector3.up));   // x local = ao longo da parede
        Breakable w = g.AddComponent<Breakable>();
        w.len = len; w.height = height; w.thick = thick; w.dust = dust;
        g.AddComponent<MeshFilter>();
        MeshRenderer r = g.AddComponent<MeshRenderer>();
        r.sharedMaterial = m;
        w.Setup(openings ?? new float[0]);
        w.id = All.Count;
        All.Add(w);
        return w;
    }

    static float[] Edges(List<float> cuts, float total, float step)
    {
        cuts.Add(0f); cuts.Add(total);
        cuts.Sort();
        List<float> e = new List<float>();
        for (int i = 0; i < cuts.Count - 1; i++)
        {
            float a = Mathf.Clamp(cuts[i], 0f, total), b = Mathf.Clamp(cuts[i + 1], 0f, total);
            if (b - a < 0.05f) continue;
            int n = Mathf.Max(1, Mathf.CeilToInt((b - a) / step - 0.15f));
            for (int k = 0; k < n; k++) e.Add(a + (b - a) * k / n);
        }
        e.Add(total);
        return e.ToArray();
    }

    void Setup(float[] op)
    {
        List<float> cx = new List<float>(), cy = new List<float>();
        for (int i = 0; i + 3 < op.Length; i += 4) { cx.Add(op[i]); cx.Add(op[i + 1]); if (op[i + 2] > 0.05f) cy.Add(op[i + 2]); if (op[i + 3] < height - 0.05f) cy.Add(op[i + 3]); }
        colX = Edges(cx, len, 0.5f);
        rowY = Edges(cy, height, 0.5f);
        nc = colX.Length - 1; nr = rowY.Length - 1;
        int n = nc * nr;
        hole = new bool[n]; alive = new bool[n]; hp = new float[n]; cols = new BoxCollider[n];
        for (int j = 0; j < nr; j++)
            for (int i = 0; i < nc; i++)
            {
                int k = j * nc + i;
                float x = (colX[i] + colX[i + 1]) / 2f, y = (rowY[j] + rowY[j + 1]) / 2f;
                for (int o = 0; o + 3 < op.Length; o += 4)
                    if (x > op[o] && x < op[o + 1] && y > op[o + 2] && y < op[o + 3]) hole[k] = true;
                if (hole[k]) continue;
                BoxCollider c = gameObject.AddComponent<BoxCollider>();
                c.center = new Vector3(x, y, 0f);
                c.size = new Vector3(colX[i + 1] - colX[i], rowY[j + 1] - rowY[j], thick);
                cols[k] = c;
                index[c] = k;
            }
        mesh = new Mesh();
        mesh.name = "parede";
        GetComponent<MeshFilter>().sharedMesh = mesh;
        Restore();
    }

    void Restore()
    {
        for (int k = 0; k < alive.Length; k++)
        {
            alive[k] = !hole[k];
            hp[k] = TileHp;
            if (cols[k] != null) cols[k].enabled = alive[k];
        }
        Rebuild();
    }

    void OnDestroy() { All.Remove(this); }

    bool On(int i, int j) { return i >= 0 && j >= 0 && i < nc && j < nr && alive[j * nc + i]; }

    // Malha: so as faces que aparecem (frente e verso de cada placa; lados so onde a vizinha falta).
    void Rebuild()
    {
        List<Vector3> v = new List<Vector3>();
        List<Vector3> nm = new List<Vector3>();
        List<int> t = new List<int>();
        float hz = thick / 2f;
        for (int j = 0; j < nr; j++)
            for (int i = 0; i < nc; i++)
            {
                if (!alive[j * nc + i]) continue;
                float x0 = colX[i], x1 = colX[i + 1], y0 = rowY[j], y1 = rowY[j + 1];
                Quad(v, nm, t, new Vector3(x0, y0, -hz), new Vector3(x1, y0, -hz), new Vector3(x1, y1, -hz), new Vector3(x0, y1, -hz), Vector3.back);
                Quad(v, nm, t, new Vector3(x1, y0, hz), new Vector3(x0, y0, hz), new Vector3(x0, y1, hz), new Vector3(x1, y1, hz), Vector3.forward);
                if (!On(i - 1, j)) Quad(v, nm, t, new Vector3(x0, y0, hz), new Vector3(x0, y0, -hz), new Vector3(x0, y1, -hz), new Vector3(x0, y1, hz), Vector3.left);
                if (!On(i + 1, j)) Quad(v, nm, t, new Vector3(x1, y0, -hz), new Vector3(x1, y0, hz), new Vector3(x1, y1, hz), new Vector3(x1, y1, -hz), Vector3.right);
                if (!On(i, j + 1)) Quad(v, nm, t, new Vector3(x0, y1, -hz), new Vector3(x1, y1, -hz), new Vector3(x1, y1, hz), new Vector3(x0, y1, hz), Vector3.up);
                if (!On(i, j - 1)) Quad(v, nm, t, new Vector3(x0, y0, hz), new Vector3(x1, y0, hz), new Vector3(x1, y0, -hz), new Vector3(x0, y0, -hz), Vector3.down);
            }
        mesh.Clear();
        mesh.SetVertices(v);
        mesh.SetNormals(nm);
        mesh.SetTriangles(t, 0);
        mesh.RecalculateBounds();
    }

    static void Quad(List<Vector3> v, List<Vector3> nm, List<int> t, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 n)
    {
        int s = v.Count;
        v.Add(a); v.Add(b); v.Add(c); v.Add(d);
        nm.Add(n); nm.Add(n); nm.Add(n); nm.Add(n);
        t.Add(s); t.Add(s + 2); t.Add(s + 1);
        t.Add(s); t.Add(s + 3); t.Add(s + 2);
    }

    Vector3 TileWorld(int k)
    {
        int i = k % nc, j = k / nc;
        return transform.TransformPoint(new Vector3((colX[i] + colX[i + 1]) / 2f, (rowY[j] + rowY[j + 1]) / 2f, 0f));
    }

    // Tiro acertou alguma coisa: se foi uma placa, gasta a vida dela.
    public static void Hit(Collider c, Vector3 point, Vector3 normal, float dmg)
    {
        if (c == null) return;
        Breakable w = c.GetComponent<Breakable>();
        if (w == null) return;
        int k;
        if (!w.index.TryGetValue(c, out k) || !w.alive[k]) return;
        w.hp[k] -= dmg;
        Fx.Spark(point, normal, w.dust);
        if (w.hp[k] <= 0f) w.Break(new List<int> { k }, true);
    }

    // Explosao: arranca as placas perto do centro (mais forte no meio).
    public static void Blast(Vector3 pos, float radius, float dmg)
    {
        float r = Mathf.Max(1.2f, radius * 0.75f);
        for (int b = 0; b < All.Count; b++)
        {
            Breakable w = All[b];
            if (w == null || !w.gameObject.activeInHierarchy) continue;
            Vector3 lp = w.transform.InverseTransformPoint(pos);
            if (lp.x < -r || lp.x > w.len + r || lp.y < -r || lp.y > w.height + r || Mathf.Abs(lp.z) > r) continue;
            List<int> broke = null;
            for (int k = 0; k < w.alive.Length; k++)
            {
                if (!w.alive[k]) continue;
                float d = Vector3.Distance(w.TileWorld(k), pos);
                if (d > r) continue;
                w.hp[k] -= dmg * 2.2f * (1f - d / r) + 20f;
                if (w.hp[k] <= 0f) { if (broke == null) broke = new List<int>(); broke.Add(k); }
            }
            if (broke != null) w.Break(broke, true);
        }
    }

    void Break(List<int> ks, bool send)
    {
        System.Text.StringBuilder sb = send ? new System.Text.StringBuilder("W;" + id + ";") : null;
        int n = 0;
        foreach (int k in ks)
        {
            if (k < 0 || k >= alive.Length || !alive[k]) continue;
            alive[k] = false;
            if (cols[k] != null) cols[k].enabled = false;
            Vector3 p = TileWorld(k);
            if (n < 6) Fx.Spark(p, transform.forward, dust);
            if (sb != null) { if (n > 0) sb.Append(','); sb.Append(k); }
            n++;
            Arena.Recheck(p, 0.9f);
        }
        if (n == 0) return;
        Rebuild();
        if (sb != null) NetGame.SendWalls(sb.ToString());
    }

    // Mensagem da rede: "W;R" refaz tudo, "W;id;k,k,k" quebra essas placas.
    public static void FromNet(string[] a)
    {
        if (a.Length < 2) return;
        if (a[1] == "R") { ResetAll(); return; }
        int id;
        if (a.Length < 3 || !int.TryParse(a[1], out id) || id < 0 || id >= All.Count || All[id] == null) return;
        List<int> ks = new List<int>();
        foreach (string s in a[2].Split(',')) { int k; if (int.TryParse(s, out k)) ks.Add(k); }
        All[id].Break(ks, false);
    }

    // Partida nova (ou rodada nova): paredes inteiras de novo.
    public static void ResetAll()
    {
        if (All.Count == 0) return;
        foreach (Breakable w in All) if (w != null) w.Restore();
        Arena.RecheckAll();
    }
}
