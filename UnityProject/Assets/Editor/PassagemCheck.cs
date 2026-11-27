using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

// NeonArena > Verificar passagens do mapa (em Play): com uma partida aberta, testa a fisica de verdade
// numa grade fina (0,5 m) no nivel do chao, com uma capsula do tamanho do jogador, e procura:
//  - celulas que o mapa marca como andaveis mas tem colisor invisivel no meio;
//  - tuneis cobertos (T), entradas do subterraneo (U), itens (P) e spawns (A/B) sem caminho ate o spawn A.
// Portoes (G) contam como abertos. Resultado no Console e em Logs/passagens.txt. Nao entra no jogo publicado.
public static class PassagemCheck
{
    const float Step = 0.5f, Radius = 0.35f;

    [MenuItem("NeonArena/Verificar passagens do mapa (em Play)")]
    static void Run()
    {
        if (!Application.isPlaying || Arena.map == null) { Debug.LogWarning("Entre numa partida e rode de novo."); return; }
        int W = Arena.W, H = Arena.H; float cell = Arena.Cell;
        Vector3 origin = Arena.Center(0, 0) - new Vector3(cell / 2f, 0f, cell / 2f);
        int nx = Mathf.RoundToInt(W * cell / Step), nz = Mathf.RoundToInt(H * cell / Step);
        bool[,] free = new bool[nx, nz];
        Collider[] hits = new Collider[16];
        for (int i = 0; i < nx; i++)
            for (int k = 0; k < nz; k++)
            {
                Vector3 p = origin + new Vector3((i + 0.5f) * Step, 0f, (k + 0.5f) * Step);
                int n = Physics.OverlapCapsuleNonAlloc(p + Vector3.up * (Radius + 0.1f), p + Vector3.up * 1.6f, Radius, hits, ~0, QueryTriggerInteraction.Ignore);
                bool ok = true;
                for (int h = 0; h < n && ok; h++)
                {
                    Collider c = hits[h];
                    if (c == null || c is CharacterController) continue;
                    if (c.GetComponentInParent<Actor>() != null || c.GetComponentInParent<Vehicle>() != null || c.GetComponentInParent<Gate>() != null) continue;
                    ok = false;
                }
                free[i, k] = ok;
            }

        // Caminho a partir do primeiro spawn A, andando pela grade fina.
        int si = -1, sk = -1;
        for (int y = 0; y < H && si < 0; y++) for (int x = 0; x < W && si < 0; x++) if (Arena.map[x, y] == 'A') { Fine(Arena.Center(x, y), origin, out si, out sk); }
        bool[,] seen = new bool[nx, nz];
        Queue<int> q = new Queue<int>();
        if (si >= 0 && free[si, sk]) { seen[si, sk] = true; q.Enqueue(si * nz + sk); }
        int[] dx = { 1, -1, 0, 0 }, dz = { 0, 0, 1, -1 };
        while (q.Count > 0)
        {
            int v = q.Dequeue(), a = v / nz, b = v % nz;
            for (int d = 0; d < 4; d++)
            {
                int c = a + dx[d], e = b + dz[d];
                if (c < 0 || e < 0 || c >= nx || e >= nz || seen[c, e] || !free[c, e]) continue;
                seen[c, e] = true; q.Enqueue(c * nz + e);
            }
        }

        StringBuilder sb = new StringBuilder();
        string map = Arena.mapId >= 0 && Arena.mapId < Arena.MapNames.Length ? Arena.MapNames[Arena.mapId] : "?";
        sb.AppendLine("Verificacao de passagens: " + map + "  (" + System.DateTime.Now.ToString("HH:mm:ss") + ")");
        int invisible = 0, cut = 0;
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                char ch = Arena.map[x, y];
                bool special = ch == 'T' || ch == 'P' || ch == 'A' || ch == 'B';
                if (ch == 'U')
                {
                    // Entrada do subterraneo: precisa de um vizinho alcancavel do lado de cima da escada.
                    bool reach = false;
                    for (int d = 0; d < 4; d++)
                    {
                        int ax = x + dx[d], ay = y + dz[d];
                        if (ax < 0 || ay < 0 || ax >= W || ay >= H || Arena.Sewer(ax, ay)) continue;
                        int fi, fk; Fine(Arena.Center(ax, ay), origin, out fi, out fk);
                        if (Seen(seen, fi, fk)) reach = true;
                    }
                    sb.AppendLine("  Entrada do subterraneo U (" + x + "," + y + "): " + (reach ? "OK, tem acesso" : "SEM ACESSO"));
                    if (!reach) cut++;
                    continue;
                }
                if (!Arena.Walk(x, y)) continue;
                if (ch == 'S') continue;   // escada: os degraus ocupam a celula (normal)
                int ci, ck; Fine(Arena.Center(x, y), origin, out ci, out ck);
                if (!free[ci, ck]) { invisible++; sb.AppendLine("  Obstaculo no centro de celula andavel '" + ch + "' (" + x + "," + y + ")"); continue; }
                if (special && !seen[ci, ck]) { cut++; sb.AppendLine("  SEM CAMINHO ate '" + ch + "' (" + x + "," + y + ")"); }
            }
        int total = 0, reached = 0;
        for (int y = 0; y < H; y++) for (int x = 0; x < W; x++) if (Arena.Walk(x, y)) { total++; int ci, ck; Fine(Arena.Center(x, y), origin, out ci, out ck); if (seen[ci, ck]) reached++; }
        sb.AppendLine("Celulas andaveis alcancadas a partir do spawn A: " + reached + " de " + total);
        sb.AppendLine("Obstaculos em celulas andaveis: " + invisible + "   pontos importantes sem caminho: " + cut);
        Debug.Log(sb.ToString());
        try { System.IO.Directory.CreateDirectory("Logs"); System.IO.File.AppendAllText("Logs/passagens.txt", sb.ToString() + "\n"); } catch (System.Exception) { }
    }

    static void Fine(Vector3 p, Vector3 origin, out int i, out int k)
    {
        i = Mathf.FloorToInt((p.x - origin.x) / Step);
        k = Mathf.FloorToInt((p.z - origin.z) / Step);
    }

    static bool Seen(bool[,] s, int i, int k) { return i >= 0 && k >= 0 && i < s.GetLength(0) && k < s.GetLength(1) && s[i, k]; }

    // Foto de cima do mapa atual (ortografica) em Logs/mapa_topo.png, para conferir a posicao das estruturas.
    [MenuItem("NeonArena/Foto de cima do mapa (em Play)")]
    static void TopShot()
    {
        if (!Application.isPlaying || Arena.map == null) { Debug.LogWarning("Entre numa partida e rode de novo."); return; }
        GameObject g = new GameObject("TopCam");
        Camera cam = g.AddComponent<Camera>();
        float w = Arena.W * Arena.Cell, h = Arena.H * Arena.Cell;
        cam.orthographic = true; cam.orthographicSize = h / 2f + 2f; cam.aspect = w / h;
        g.transform.position = new Vector3(0f, 120f, 0f); g.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        cam.nearClipPlane = 1f; cam.farClipPlane = 200f; cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = Color.black;
        int pw = 2000, ph = Mathf.RoundToInt(2000f * h / w);
        RenderTexture rt = new RenderTexture(pw, ph, 24);
        cam.targetTexture = rt; cam.Render();
        RenderTexture.active = rt;
        Texture2D t = new Texture2D(pw, ph, TextureFormat.RGB24, false);
        t.ReadPixels(new Rect(0, 0, pw, ph), 0, 0); t.Apply();
        RenderTexture.active = null; cam.targetTexture = null;
        System.IO.Directory.CreateDirectory("Logs");
        System.IO.File.WriteAllBytes("Logs/mapa_topo.png", t.EncodeToJPG(85));
        Object.Destroy(t); rt.Release(); Object.Destroy(g);
        Debug.Log("Foto de cima salva em Logs/mapa_topo.png");
    }

    [MenuItem("NeonArena/Verificar passagens do mapa (em Play)", true)]
    static bool Valid() { return Application.isPlaying; }
}
