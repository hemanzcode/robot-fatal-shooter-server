using System.Collections.Generic;
using UnityEngine;

// CORRIDA DA MONTANHA - easter egg do mapa A CASA.
// Quem pega a chave da piscina (RaceKeys, so no treino) e levado na hora para uma corrida descendo a montanha onde a casa fica,
// ate a praia la embaixo. Largada ja em movimento (pega de surpresa), 4 carros, sem itens: so pilotar.
// A partida na CASA continua para os outros; no fim o jogador volta para ela.
//
// A pista e gerada por codigo (sempre a mesma): ~5,6 km de curvas e descida (~2 min).
// Os carros sao "arcade" e presos na pista (sem fisica de colisor): a altura e a inclinacao vem da linha
// central, e o guard-rail e um limite lateral. Isso deixa a corrida leve no celular.
public class Race : MonoBehaviour
{
    public static Race I;
    public static bool Active { get { return I != null; } }

    // ---------------- Pista ----------------
    static readonly Vector3 O = new Vector3(6000f, 0f, 0f);   // longe do mapa da partida
    const float Len = 5600f;          // ate a linha de chegada (m)
    const float Extra = 320f;         // estrada depois da chegada, para os carros pararem
    const float Step = 5f;            // distancia entre pontos da linha central
    const float HalfW = 8f;           // meia largura da pista (16 m, cabem 4 carros lado a lado)
    const float Top = 380f;           // altura da largada (topo da montanha)
    Vector3[] P, T, R;                // ponto, direcao e "direita" de cada amostra
    float[] S, curv;
    int N;

    // ---------------- Carros ----------------
    class Car
    {
        public string name;
        public bool human;
        public int model;
        public Transform tr, body;
        public Vector3 pos;
        public float yaw, speed, steer, driftK, lane, skill, prog, finishTime, scrapeT;
        public int idx;
        public bool done;
    }
    readonly List<Car> cars = new List<Car>();
    Car me;
    Actor driver;
    Transform root;
    Camera cam;
    float t0, endAt, camShake, camYaw, camY;
    bool finished;
    AudioSource engine;
    float nextScrapeSfx;
    readonly List<Renderer> hidden = new List<Renderer>();
    Vector3 parkedAt;
    bool savedFog; float savedFogDensity; Color savedFogColor;

    const float VMax = 74f;       // m/s (~266 km/h)
    const float Grip = 19f;       // aceleracao lateral maxima (m/s2)
    const float DriftGrip = 27f;

    static readonly string[] Rivals = { "VIPER", "NOVA", "TROVAO", "FALCAO", "RAIO", "SOMBRA" };

    // Chamado quando o jogador pega a chave.
    public static void Begin(Actor human)
    {
        if (I != null || human == null || Game.I == null) return;
        GameObject g = new GameObject("CorridaDaMontanha");
        Race r = g.AddComponent<Race>();
        I = r;
        r.Setup(human);
    }

    void Setup(Actor human)
    {
        useGUILayout = false;
        driver = human;
        root = transform;
        BuildTrack();
        BuildWorld();

        // Tira o jogador da partida: some do mapa, fica invulneravel e longe dos bots.
        foreach (Renderer rr in human.GetComponentsInChildren<Renderer>()) if (rr.enabled) { rr.enabled = false; hidden.Add(rr); }
        if (human.cc != null) human.cc.enabled = false;
        human.invulnerableUntil = float.MaxValue;
        parkedAt = human.transform.position;
        human.transform.position = O + new Vector3(0f, -600f, 0f);
        PlayerController pc = human.GetComponent<PlayerController>();
        cam = pc != null ? pc.cam : Camera.main;
        if (cam != null) { cam.transform.SetParent(null, true); cam.farClipPlane = 3200f; }
        savedFog = RenderSettings.fog; savedFogDensity = RenderSettings.fogDensity; savedFogColor = RenderSettings.fogColor;
        RenderSettings.fog = true;
        RenderSettings.fogDensity = 0.0011f;
        RenderSettings.fogColor = new Color(0.72f, 0.8f, 0.9f);

        // Grid de largada (ja em movimento): voce comeca na segunda fila.
        string[] pool = (string[])Rivals.Clone();
        for (int i = 0; i < pool.Length; i++) { int j = Random.Range(i, pool.Length); string tmp = pool[i]; pool[i] = pool[j]; pool[j] = tmp; }
        float[][] grid = { new[] { 46f, -3.5f }, new[] { 46f, 3.5f }, new[] { 32f, -3.5f }, new[] { 32f, 3.5f } };
        int[] models = { 1, 2, 0, 3 };
        for (int i = 0; i < 4; i++)
        {
            bool h = i == 2;
            Car c = MakeCar(h ? (string.IsNullOrEmpty(human.displayName) ? "VOCE" : human.displayName) : pool[i], h, models[i], grid[i][0], grid[i][1]);
            c.skill = h ? 1f : Random.Range(0.93f, 0.99f);
            cars.Add(c);
            if (h) me = c;
        }

        engine = gameObject.AddComponent<AudioSource>();
        engine.clip = Sfx.Get("engine");
        engine.loop = true;
        engine.spatialBlend = 0f;
        engine.volume = 0f;
        engine.Play();
        t0 = Time.time;
        camYaw = me.yaw; camY = me.pos.y;
        Sfx.UI("ult", 0.9f);
    }

    // ================= Geracao da pista =================

    float Height(float s)
    {
        float u = Mathf.Clamp01(s / Len);
        float h = Top * Mathf.Pow(1f - u, 1.15f) + 7f * Mathf.Sin(s / 55f) * (1f - u);
        float beach = Mathf.Clamp01((s - (Len - 320f)) / 320f);
        h = Mathf.Lerp(h, 1.2f, beach);
        return Mathf.Max(1.2f, h);
    }

    void BuildTrack()
    {
        N = Mathf.RoundToInt((Len + Extra) / Step) + 1;
        P = new Vector3[N]; T = new Vector3[N]; R = new Vector3[N]; S = new float[N]; curv = new float[N];
        float x = 0f, z = 0f;
        float[] head = new float[N];
        for (int i = 0; i < N; i++)
        {
            float s = i * Step;
            // Curvas: duas ondas somadas (no maximo ~80 graus para um lado) -> a estrada sempre avanca em x,
            // entao nunca cruza consigo mesma. Perto da praia as curvas abrem numa reta final.
            float k = Mathf.Lerp(0.2f, 1f, Mathf.Clamp01((Len - 200f - s) / 450f));
            float start = Mathf.Clamp01(s / 260f);
            float hd = (55f * Mathf.Sin(2f * Mathf.PI * s / 1150f + 0.6f) + 24f * Mathf.Sin(2f * Mathf.PI * s / 430f + 1.3f)) * k * start;
            head[i] = hd * Mathf.Deg2Rad;
            S[i] = s;
            P[i] = O + new Vector3(x, Height(s), z);
            x += Mathf.Cos(head[i]) * Step;
            z += Mathf.Sin(head[i]) * Step;
        }
        for (int i = 0; i < N; i++)
        {
            Vector3 a = P[Mathf.Max(0, i - 1)], b = P[Mathf.Min(N - 1, i + 1)];
            Vector3 t = b - a; t.y = 0f; t.Normalize();
            T[i] = t;
            R[i] = new Vector3(t.z, 0f, -t.x);
            curv[i] = i > 0 ? Mathf.Abs(head[i] - head[i - 1]) / Step : 0f;
        }
    }

    // Amostra mais perto de p (a estrada avanca sempre em x: busca binaria e depois ajuste fino).
    int Nearest(Vector3 p, int hint)
    {
        int best = 0;
        if (hint < 0)
        {
            int lo = 0, hi = N - 1;
            while (hi - lo > 1) { int m = (lo + hi) / 2; if (P[m].x < p.x) lo = m; else hi = m; }
            hint = lo;
        }
        float bd = float.MaxValue;
        int a = Mathf.Max(0, hint - 40), b = Mathf.Min(N - 1, hint + 40);
        for (int i = a; i <= b; i++)
        {
            float dx = P[i].x - p.x, dz = P[i].z - p.z, d = dx * dx + dz * dz;
            if (d < bd) { bd = d; best = i; }
        }
        return best;
    }

    // Altura da pista e deslocamento lateral num ponto.
    void OnRoad(Vector3 p, int k, out float h, out float lat, out float along)
    {
        int k2 = Mathf.Min(N - 1, k + 1);
        Vector3 d = p - P[k]; d.y = 0f;
        along = Vector3.Dot(d, T[k]);
        float t = Mathf.Clamp01(along / Step);
        if (along < 0f && k > 0) { t = Mathf.Clamp01(1f + along / Step); h = Mathf.Lerp(P[k - 1].y, P[k].y, t); }
        else h = Mathf.Lerp(P[k].y, P[k2].y, t);
        lat = Vector3.Dot(d, R[k]);
    }

    // Altura do terreno em (x, z): vale em volta da estrada, praia no fim, mar depois.
    float Ground(float x, float z, out float roadDist, out float roadH)
    {
        Vector3 p = new Vector3(x, 0f, z);
        int k = Nearest(p, -1);
        Vector3 d = p - P[k]; d.y = 0f;
        roadDist = d.magnitude;
        roadH = P[k].y;
        float along = Vector3.Dot(d, T[k]);
        if (k >= N - 2 && along > 0f) return 0.9f - along * 0.035f - Mathf.Max(0f, roadDist - along - 60f) * 0.01f;   // praia descendo para o mar
        float coast = Mathf.Clamp01((Len - S[k] - 120f) / 500f);                 // 0 na praia, 1 na montanha
        float wall = Mathf.Max(0f, roadDist - (HalfW + 9f));
        float n = (Mathf.PerlinNoise(x * 0.004f + 3.1f, z * 0.004f + 7.7f) - 0.5f) * 60f + (Mathf.PerlinNoise(x * 0.017f, z * 0.017f) - 0.5f) * 14f;
        float h = roadH - 0.7f + (wall * 0.42f + n * Mathf.Clamp01(wall / 40f)) * coast;
        if (coast < 1f) h = Mathf.Lerp(Mathf.Min(roadH - 0.6f, 1.2f - wall * 0.015f), h, coast);
        if (k == 0 && along < 0f) h = roadH - 0.7f + Mathf.Max(0f, -along - 60f) * 0.3f;   // plato da largada
        return h;
    }

    // ================= Cenario =================

    Material asphalt, line, curbR, curbW, rail, grass, sand, water, trunk, leaves, rock;

    void BuildWorld()
    {
        asphalt = Art.Pbr("Concrete034", new Color(0.28f, 0.28f, 0.3f), 4f, 0.9f, 0f, 1f);
        line = Art.Mat(new Color(0.95f, 0.95f, 0.9f), new Color(0.1f, 0.1f, 0.1f), 0f, default(Color), 0.4f, 0f);
        curbR = Art.Mat(new Color(0.85f, 0.12f, 0.1f), Color.black, 0f, default(Color), 0.3f, 0f);
        curbW = Art.Mat(new Color(0.95f, 0.95f, 0.95f), Color.black, 0f, default(Color), 0.3f, 0f);
        rail = Art.Mat(new Color(0.7f, 0.72f, 0.75f), Color.black, 0f, default(Color), 0.7f, 0.8f);
        grass = Art.Pbr("Ground054", new Color(0.48f, 0.62f, 0.34f), 14f, 0.9f, 0f, 1f, 0.8f);
        sand = Art.Pbr("Ground054", new Color(1.15f, 1.0f, 0.74f), 8f, 0.9f, 0f, 1f, 0.8f);
        water = Art.Mat(new Color(0.06f, 0.36f, 0.55f), new Color(0.01f, 0.05f, 0.08f), 0f, default(Color), 0.97f, 0.1f);
        trunk = Art.Mat(new Color(0.35f, 0.24f, 0.15f), Color.black, 0f, default(Color), 0.1f, 0f);
        leaves = Art.Mat(new Color(0.16f, 0.36f, 0.18f), Color.black, 0f, default(Color), 0.1f, 0f);
        rock = Art.Pbr("Concrete034", new Color(0.55f, 0.52f, 0.5f), 3f, 0.9f, 0f, 1f);

        // Estrada em pedacos (para a camera so desenhar o que esta perto)
        const int chunk = 60;
        for (int a = 0; a < N - 1; a += chunk) BuildRoadChunk(a, Mathf.Min(N - 1, a + chunk));
        BuildTerrain();
        BuildProps();
        // Mar
        Vector3 end = P[N - 1];
        Art.Box(root, new Vector3(end.x + 600f, -0.05f, end.z), new Vector3(3200f, 0.1f, 3200f), water, false, "mar");
        // Largada e chegada
        Gantry(0 + 8, "largada");
        Gantry(Mathf.RoundToInt(Len / Step), "chegada");
    }

    void BuildRoadChunk(int a, int b)
    {
        var v = new List<Vector3>(); var t = new List<int>();
        var cv = new List<Vector3>(); var ct = new List<int>();     // zebra vermelha
        var wv = new List<Vector3>(); var wt = new List<int>();     // zebra branca e faixa central
        var rv = new List<Vector3>(); var rt = new List<int>();     // guard-rail
        var kv = new List<Vector3>(); var kt = new List<int>();     // barranco embaixo da pista
        for (int i = a; i < b; i++)
        {
            int j = i + 1;
            Strip(v, t, i, j, -HalfW - 1.2f, HalfW + 1.2f, 0.02f);
            List<Vector3> zv = (i / 2) % 2 == 0 ? cv : wv; List<int> zt = (i / 2) % 2 == 0 ? ct : wt;
            Strip(zv, zt, i, j, -HalfW - 1.2f, -HalfW + 0.2f, 0.05f);
            Strip(zv, zt, i, j, HalfW - 0.2f, HalfW + 1.2f, 0.05f);
            if (i % 4 < 2) Strip(wv, wt, i, j, -0.15f, 0.15f, 0.04f);
            Wall(rv, rt, i, j, -HalfW - 1.5f, 0.25f, 1.0f);
            Wall(rv, rt, i, j, HalfW + 1.5f, 0.25f, 1.0f);
            Skirt(kv, kt, i, j, -HalfW - 1.6f, -1f);
            Skirt(kv, kt, i, j, HalfW + 1.6f, 1f);
        }
        MeshPart("estrada", v, t, asphalt);
        MeshPart("zebra", cv, ct, curbR);
        MeshPart("faixas", wv, wt, curbW);
        MeshPart("guard-rail", rv, rt, rail);
        MeshPart("barranco", kv, kt, rock);
    }

    Vector3 At(int i, float lat, float up) { return P[i] + R[i] * lat + Vector3.up * up; }

    void Strip(List<Vector3> v, List<int> t, int i, int j, float l0, float l1, float up)
    {
        int s = v.Count;
        v.Add(At(i, l0, up)); v.Add(At(i, l1, up)); v.Add(At(j, l1, up)); v.Add(At(j, l0, up));
        // virada para cima
        t.Add(s); t.Add(s + 2); t.Add(s + 1); t.Add(s); t.Add(s + 3); t.Add(s + 2);
    }

    void Wall(List<Vector3> v, List<int> t, int i, int j, float lat, float y0, float y1)
    {
        TwoSided(v, t, At(i, lat, y0), At(j, lat, y0), At(j, lat, y1), At(i, lat, y1));
    }

    // Quadrilatero com as duas faces (vertices separados, para a normal de cada lado ficar certa).
    static void TwoSided(List<Vector3> v, List<int> t, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
    {
        int s = v.Count;
        v.Add(a); v.Add(b); v.Add(c); v.Add(d);
        t.Add(s); t.Add(s + 1); t.Add(s + 2); t.Add(s); t.Add(s + 2); t.Add(s + 3);
        s = v.Count;
        v.Add(a); v.Add(b); v.Add(c); v.Add(d);
        t.Add(s); t.Add(s + 2); t.Add(s + 1); t.Add(s); t.Add(s + 3); t.Add(s + 2);
    }

    // Parede inclinada da beira da pista ate 6 m abaixo (esconde o vao entre a pista e o terreno).
    void Skirt(List<Vector3> v, List<int> t, int i, int j, float lat, float side)
    {
        float out1 = lat + side * 4f;
        TwoSided(v, t, At(i, lat, 0.02f), At(j, lat, 0.02f), At(j, out1, -6f), At(i, out1, -6f));
    }

    void MeshPart(string name, List<Vector3> v, List<int> t, Material m)
    {
        if (v.Count == 0) return;
        Mesh mesh = new Mesh();
        mesh.indexFormat = v.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
        mesh.SetVertices(v);
        mesh.SetTriangles(t, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        GameObject g = new GameObject(name);
        g.transform.SetParent(root, false);
        g.AddComponent<MeshFilter>().sharedMesh = mesh;
        MeshRenderer r = g.AddComponent<MeshRenderer>();
        r.sharedMaterial = m;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    // Terreno em ladrilhos de 40 x 40 celulas de 20 m; grama no alto, areia perto do mar.
    void BuildTerrain()
    {
        float minZ = float.MaxValue, maxZ = float.MinValue;
        for (int i = 0; i < N; i++) { minZ = Mathf.Min(minZ, P[i].z); maxZ = Mathf.Max(maxZ, P[i].z); }
        const float cell = 20f;
        float x0 = O.x - 420f, x1 = P[N - 1].x + 900f, z0 = minZ - 620f, z1 = maxZ + 620f;
        int nx = Mathf.CeilToInt((x1 - x0) / cell), nz = Mathf.CeilToInt((z1 - z0) / cell);
        float[,] h = new float[nx + 1, nz + 1];
        for (int ix = 0; ix <= nx; ix++)
            for (int iz = 0; iz <= nz; iz++)
            {
                float rd, rh;
                h[ix, iz] = Ground(x0 + ix * cell, z0 + iz * cell, out rd, out rh);
            }
        const int tile = 40;
        for (int tx = 0; tx < nx; tx += tile)
            for (int tz = 0; tz < nz; tz += tile)
            {
                int ex = Mathf.Min(nx, tx + tile), ez = Mathf.Min(nz, tz + tile);
                var v = new List<Vector3>(); var g = new List<int>(); var sd = new List<int>();
                int w = ez - tz + 1;
                for (int ix = tx; ix <= ex; ix++)
                    for (int iz = tz; iz <= ez; iz++)
                        v.Add(new Vector3(x0 + ix * cell, h[ix, iz], z0 + iz * cell));
                for (int ix = 0; ix < ex - tx; ix++)
                    for (int iz = 0; iz < ez - tz; iz++)
                    {
                        int a = ix * w + iz, b = a + 1, c = a + w, d = c + 1;
                        float avg = (v[a].y + v[b].y + v[c].y + v[d].y) * 0.25f;
                        List<int> l = avg < 4f ? sd : g;
                        l.Add(a); l.Add(b); l.Add(d); l.Add(a); l.Add(d); l.Add(c);
                    }
                Mesh mesh = new Mesh();
                mesh.SetVertices(v);
                mesh.subMeshCount = 2;
                mesh.SetTriangles(g, 0);
                mesh.SetTriangles(sd, 1);
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
                GameObject go = new GameObject("terreno");
                go.transform.SetParent(root, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                MeshRenderer mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterials = new[] { grass, sand };
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
    }

    // Pinheiros na montanha, coqueiros na praia, pedras na beira. Arvores e pedras viram poucas malhas grandes
    // (mil arvores como objetos separados travariam o celular).
    readonly List<Vector3> tv = new List<Vector3>(), lv = new List<Vector3>(), rv2 = new List<Vector3>();
    readonly List<int> tt = new List<int>(), lt = new List<int>(), rt2 = new List<int>();

    void BuildProps()
    {
        Transform props = new GameObject("praia").transform;
        props.SetParent(root, false);
        System.Random rnd = new System.Random(42);
        for (int i = 6; i < N - 2; i += 4)
        {
            for (int side = -1; side <= 1; side += 2)
            {
                if (rnd.NextDouble() < 0.4) continue;
                float lat = side * (HalfW + 7f + (float)rnd.NextDouble() * 80f);
                Vector3 p = P[i] + R[i] * lat;
                float rd, rh;
                float gy = Ground(p.x, p.z, out rd, out rh);
                if (rd < HalfW + 5f) continue;
                p.y = gy;
                float yaw = (float)rnd.NextDouble() * 360f;
                if (S[i] > Len - 380f)
                {
                    if (rnd.NextDouble() < 0.45 && gy > 0.3f) Arena.PalmAt(props, p, (float)rnd.NextDouble());
                }
                else if (rnd.NextDouble() < 0.84) Pine(p, 0.8f + (float)rnd.NextDouble() * 0.8f, yaw);
                else AddBox(rv2, rt2, p + Vector3.up * 0.8f, new Vector3(3f, 2.2f, 2.6f) * (0.6f + (float)rnd.NextDouble() * 1.2f), yaw);
                if (tv.Count > 60000) FlushProps();
            }
        }
        FlushProps();
        // A casa no alto da montanha, ao lado da largada (a mesma do mapa, vista de longe)
        Vector3 hp = P[2] + R[2] * 34f - T[2] * 20f;
        hp.y = Ground(hp.x, hp.z, out _, out _);
        Material wallM = Art.Pbr("Plaster001", new Color(1f, 0.97f, 0.9f), 3f, 1f, 0f, 1f);
        Art.Box(props, hp + Vector3.up * 1.4f, new Vector3(14f, 2.8f, 14f), wallM, false, "casa").transform.rotation = Quaternion.Euler(0f, 45f, 0f);
        Art.Box(props, hp + Vector3.up * 2.95f, new Vector3(15f, 0.25f, 15f), rock, false, "laje").transform.rotation = Quaternion.Euler(0f, 45f, 0f);
    }

    void FlushProps()
    {
        MeshPart("troncos", tv, tt, trunk); MeshPart("copas", lv, lt, leaves); MeshPart("pedras", rv2, rt2, rock);
        tv.Clear(); tt.Clear(); lv.Clear(); lt.Clear(); rv2.Clear(); rt2.Clear();
    }

    void Pine(Vector3 p, float s, float yaw)
    {
        AddBox(tv, tt, p + Vector3.up * 1.2f * s, new Vector3(0.45f, 2.4f, 0.45f) * s, yaw);
        for (int k = 0; k < 3; k++)
        {
            float w = (3.6f - k * 1.0f) * s;
            AddBox(lv, lt, p + Vector3.up * (2.6f + k * 1.6f) * s, new Vector3(w, 1.9f * s, w), yaw + 45f + k * 20f);
        }
    }

    // Caixa com faces separadas (sombreamento certo) girada em y.
    static void AddBox(List<Vector3> v, List<int> t, Vector3 c, Vector3 size, float yaw)
    {
        Quaternion q = Quaternion.Euler(0f, yaw, 0f);
        Vector3 h = size * 0.5f;
        Vector3[] n = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
        foreach (Vector3 f in n)
        {
            Vector3 a = new Vector3(f.y, f.z, f.x), b = Vector3.Cross(f, a);
            Vector3 fc = Vector3.Scale(f, h);
            Vector3 aa = Vector3.Scale(a, h), bb = Vector3.Scale(b, h);
            int s0 = v.Count;
            v.Add(c + q * (fc - aa - bb)); v.Add(c + q * (fc + aa - bb)); v.Add(c + q * (fc + aa + bb)); v.Add(c + q * (fc - aa + bb));
            t.Add(s0); t.Add(s0 + 1); t.Add(s0 + 2); t.Add(s0); t.Add(s0 + 2); t.Add(s0 + 3);
        }
    }

    // Portico xadrez sobre a pista (largada / chegada).
    void Gantry(int i, string name)
    {
        i = Mathf.Clamp(i, 0, N - 1);
        Quaternion q = Quaternion.LookRotation(T[i]);
        Material dark = Art.Mat(new Color(0.08f, 0.08f, 0.09f), Color.black, 0f, default(Color), 0.4f, 0.2f);
        foreach (int side in new[] { -1, 1 })
            Art.Box(root, At(i, side * (HalfW + 2.2f), 3.5f), new Vector3(0.6f, 7f, 0.6f), dark, false, name).transform.rotation = q;
        int n = 16;
        for (int k = 0; k < n; k++)
            for (int row = 0; row < 2; row++)
            {
                float lat = -HalfW - 2.2f + (k + 0.5f) * (2f * HalfW + 4.4f) / n;
                GameObject b = Art.Box(root, At(i, lat, 6.4f + row * 0.7f), new Vector3((2f * HalfW + 4.4f) / n, 0.7f, 0.3f), (k + row) % 2 == 0 ? curbW : dark, false, name);
                b.transform.rotation = q;
            }
    }

    // ================= Carros =================

    Car MakeCar(string name, bool human, int model, float s, float lat)
    {
        Car c = new Car { name = name, human = human, model = model, lane = lat };
        int k = Mathf.Clamp(Mathf.RoundToInt(s / Step), 0, N - 1);
        c.idx = k;
        c.pos = At(k, lat, 0f);
        c.yaw = Mathf.Atan2(T[k].x, T[k].z) * Mathf.Rad2Deg;
        c.speed = 30f;
        c.tr = new GameObject("carro " + name).transform;
        c.tr.SetParent(root, false);
        c.body = new GameObject("carroceria").transform;
        c.body.SetParent(c.tr, false);
        if (Arena.CarModel(c.body, model) == null)
            Art.Box(c.body, new Vector3(0f, 0.6f, 0f), new Vector3(1.9f, 0.8f, 4.4f), Art.Mat(Color.red, Color.black), false, "carro");
        return c;
    }

    void Update()
    {
        if (Game.I == null || Game.I.player == null || Game.I.matchOver || driver == null) { End(); return; }
        float dt = Time.deltaTime;
        if (dt <= 0f) return;
        bool menu = Game.I.menus.IsOpen;
#if UNITY_EDITOR
        // So no editor: F11 leva todos para perto da chegada (para testar o final).
        if (Input.GetKeyDown(KeyCode.F11))
            foreach (Car c in cars) { int k = Mathf.RoundToInt((Len - 260f + (c.human ? 0f : 40f)) / Step); c.idx = k; c.pos = At(k, c.lane, 0f); c.yaw = Mathf.Atan2(T[k].x, T[k].z) * Mathf.Rad2Deg; }
#endif

        foreach (Car c in cars)
        {
            float thr, brk, st; bool drift;
            if (c.human && !c.done && !menu) PlayerInput(out thr, out brk, out st, out drift);
            else AIInput(c, out thr, out brk, out st, out drift);
            Drive(c, thr, brk, st, drift, dt);
        }
        Collide();
        foreach (Car c in cars) Place(c, dt);

        // Ordem e chegada
        foreach (Car c in cars)
            if (!c.done && c.prog >= Len) { c.done = true; c.finishTime = Time.time - t0; if (c.human) { finished = true; endAt = Time.time + 8f; Sfx.UI("ult", 1f); Game.I.Banner(Rank(me) + "º LUGAR!"); } }
        if (finished && (Time.time > endAt || (!menu && (GameInput.UseDown || Input.GetKeyDown(KeyCode.Return))))) { End(); return; }

        engine.volume = 0.42f * Settings.sfx * (menu ? 0f : 1f);
        engine.pitch = 0.55f + Mathf.Abs(me.speed) / 30f;
    }

    void LateUpdate()
    {
        if (cam == null || me == null) return;
        // Camera de perseguicao: atras e acima, olhando um pouco a frente; abre o campo de visao com a velocidade.
        // So a direcao da camera e suavizada (seguir a posicao com atraso deixava o carro pequeno la na frente).
        camYaw = Mathf.LerpAngle(camYaw, me.yaw + me.driftK * me.steer * 10f, 1f - Mathf.Exp(-6f * Time.deltaTime));
        camY = Mathf.Lerp(camY, me.pos.y, 1f - Mathf.Exp(-10f * Time.deltaTime));
        if (Mathf.Abs(camY - me.pos.y) > 8f) camY = me.pos.y;
        Vector3 fwd = Quaternion.Euler(0f, camYaw, 0f) * Vector3.forward;
        Vector3 basePos = new Vector3(me.pos.x, camY, me.pos.z);
        cam.transform.position = basePos - fwd * 6.4f + Vector3.up * 2.4f;
        Vector3 look = basePos + fwd * 5f + Vector3.up * 1.1f;
        Quaternion q = Quaternion.LookRotation(look - cam.transform.position);
        if (camShake > 0f) { q *= Quaternion.Euler(Random.Range(-1f, 1f) * camShake, Random.Range(-1f, 1f) * camShake, 0f); camShake = Mathf.Max(0f, camShake - Time.deltaTime * 4f); }
        cam.transform.rotation = q;
        cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, 64f + Mathf.Abs(me.speed) * 0.22f, 1f - Mathf.Exp(-3f * Time.deltaTime));
    }

    void PlayerInput(out float thr, out float brk, out float st, out bool drift)
    {
        Vector2 mv = GameInput.Move;
        if (TouchPad.On)
        {
            // Celular: acelera sozinho (como nos jogos de corrida); puxar para baixo freia.
            thr = mv.y < -0.5f ? 0f : 1f;
            brk = mv.y < -0.5f ? 1f : 0f;
        }
        else
        {
            thr = Mathf.Max(0f, mv.y);
            brk = Mathf.Max(0f, -mv.y);
        }
        st = Mathf.Clamp(mv.x, -1f, 1f);
        drift = GameInput.JumpHeld;
    }

    void AIInput(Car c, out float thr, out float brk, out float st, out bool drift)
    {
        // Olha um ponto a frente na pista (na sua faixa) e ajusta a velocidade pela curva que vem.
        float look = Mathf.Max(14f, c.speed * 0.55f);
        int ahead = Mathf.Min(N - 1, c.idx + Mathf.RoundToInt(look / Step));
        // muda de faixa se tem carro logo a frente
        foreach (Car o in cars)
        {
            if (o == c) continue;
            float gap = o.prog - c.prog;
            if (gap > 0f && gap < 18f && Mathf.Abs(o.lane - c.lane) < 2.5f && c.speed > o.speed) c.lane = Mathf.Clamp(o.lane + (o.lane > 0f ? -5f : 5f), -HalfW + 2f, HalfW - 2f);
        }
        Vector3 target = At(ahead, c.lane, 0f);
        Vector3 to = target - c.pos; to.y = 0f;
        float want = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
        st = Mathf.Clamp(Mathf.DeltaAngle(c.yaw, want) / 18f, -1f, 1f);
        float maxC = 0f;
        int span = Mathf.RoundToInt(Mathf.Max(60f, c.speed * 2.4f) / Step);
        for (int i = c.idx; i < Mathf.Min(N - 1, c.idx + span); i++) maxC = Mathf.Max(maxC, curv[i]);
        float vCurve = maxC > 0.0001f ? Mathf.Sqrt(Grip * 0.92f * c.skill / maxC) : 999f;
        float vmax = VMax * c.skill;
        // leve "elastico": quem esta muito atras do jogador acelera um pouco mais, quem esta muito a frente alivia
        if (me != null && !me.done)
        {
            float diff = c.prog - me.prog;
            if (diff < -150f) vmax *= 1.05f; else if (diff > 200f) vmax *= 0.94f;
        }
        float target_v = Mathf.Min(vmax, vCurve);
        if (c.done) target_v = c.prog > Len + Extra - 120f ? 0f : 18f;
        thr = c.speed < target_v - 1f ? 1f : 0f;
        brk = c.speed > target_v + 3f ? 1f : 0f;
        drift = false;
    }

    void Drive(Car c, float thr, float brk, float st, bool drift, float dt)
    {
        float vmax = c.human ? VMax : VMax * c.skill * 1.03f;
        float sp = c.speed;
        // motor, freio, arrasto
        float acc = thr * (16f * (1f - (sp / vmax) * (sp / vmax)) + 2f);
        if (brk > 0f) acc -= brk * (sp > 0f ? 34f : 8f);
        acc -= 0.5f + 0.0006f * sp * sp;
        c.driftK = Mathf.MoveTowards(c.driftK, drift && sp > 15f ? 1f : 0f, dt * 4f);
        acc -= c.driftK * 7f;
        if (thr <= 0f && brk <= 0f && Mathf.Abs(sp) < 0.6f) sp = 0f;
        sp = Mathf.Clamp(sp + acc * dt, -10f, vmax);
        c.speed = sp;
        // direcao: rapida em baixa velocidade, limitada pela aderencia em alta
        c.steer = Mathf.MoveTowards(c.steer, st, dt * (c.human ? 4.5f : 6f));
        float grip = Mathf.Lerp(Grip, DriftGrip, c.driftK);
        float rate = Mathf.Min(95f, grip / Mathf.Max(3f, Mathf.Abs(sp)) * Mathf.Rad2Deg);
        c.yaw += c.steer * rate * Mathf.Sign(sp == 0f ? 1f : sp) * dt * Mathf.Clamp01(Mathf.Abs(sp) / 3f);
        Vector3 fwd = Quaternion.Euler(0f, c.yaw, 0f) * Vector3.forward;
        c.pos += fwd * sp * dt;

        // Presa na pista: o guard-rail e um limite lateral.
        c.idx = Nearest(c.pos, c.idx);
        float h, lat, along;
        OnRoad(c.pos, c.idx, out h, out lat, out along);
        float lim = HalfW + 0.6f;
        if (Mathf.Abs(lat) > lim)
        {
            c.pos -= R[c.idx] * (lat - Mathf.Sign(lat) * lim);
            float tyaw = Mathf.Atan2(T[c.idx].x, T[c.idx].z) * Mathf.Rad2Deg;
            c.yaw = Mathf.LerpAngle(c.yaw, tyaw, 0.25f);
            c.speed *= 1f - Mathf.Clamp01(dt * 1.6f);
            c.scrapeT = Time.time;
            if (c.human) { camShake = 0.6f; if (Time.time > nextScrapeSfx) { Sfx.UI("hit", 0.5f, 0.6f); nextScrapeSfx = Time.time + 0.25f; } }
        }
        c.pos.y = h;
        c.prog = S[c.idx] + along;
    }

    // Batidas entre carros: empurra para os lados e troca um pouco de velocidade.
    void Collide()
    {
        for (int i = 0; i < cars.Count; i++)
            for (int j = i + 1; j < cars.Count; j++)
            {
                Car a = cars[i], b = cars[j];
                Vector3 d = b.pos - a.pos; d.y = 0f;
                if (d.sqrMagnitude > 25f) continue;
                Vector3 fa = Quaternion.Euler(0f, a.yaw, 0f) * Vector3.forward;
                Vector3 side = new Vector3(fa.z, 0f, -fa.x);
                float along = Vector3.Dot(d, fa), across = Vector3.Dot(d, side);
                if (Mathf.Abs(along) > 4.4f || Mathf.Abs(across) > 2.0f) continue;
                if (Mathf.Abs(across) > Mathf.Abs(along) * 0.45f)
                {
                    float push = (2.0f - Mathf.Abs(across)) * 0.5f * Mathf.Sign(across == 0f ? 1f : across);
                    a.pos -= side * push; b.pos += side * push;
                }
                else
                {
                    float push = (4.4f - Mathf.Abs(along)) * 0.5f * Mathf.Sign(along);
                    a.pos -= fa * push; b.pos += fa * push;
                    Car back = along > 0f ? a : b, front = along > 0f ? b : a;
                    float v = (back.speed + front.speed) * 0.5f;
                    if (back.speed > front.speed) { back.speed = v - 1f; front.speed = v + 1f; }
                }
                if (a.human || b.human) { camShake = 0.9f; if (Time.time > nextScrapeSfx) { Sfx.UI("hit", 0.7f, 0.5f); nextScrapeSfx = Time.time + 0.3f; } }
            }
    }

    void Place(Car c, float dt)
    {
        int k2 = Mathf.Min(N - 1, c.idx + 1);
        float slope = Mathf.Atan2(P[Mathf.Max(0, c.idx - 1)].y - P[k2].y, Step * 2f) * Mathf.Rad2Deg;
        c.tr.SetPositionAndRotation(c.pos, Quaternion.Euler(slope, c.yaw, 0f));
        // derrapando: a carroceria vira um pouco para dentro da curva
        float slide = c.driftK * c.steer * 18f;
        c.body.localRotation = Quaternion.Slerp(c.body.localRotation, Quaternion.Euler(0f, slide, -c.steer * Mathf.Clamp01(Mathf.Abs(c.speed) / 40f) * 3f), 1f - Mathf.Exp(-8f * dt));
    }

    int Rank(Car c)
    {
        int p = 1;
        foreach (Car o in cars)
        {
            if (o == c) continue;
            if (o.done && c.done) { if (o.finishTime < c.finishTime) p++; }
            else if (o.done) p++;
            else if (!c.done && o.prog > c.prog) p++;
        }
        return p;
    }

    // ================= Fim =================

    void End()
    {
        if (I != this) return;
        I = null;
        RenderSettings.fog = savedFog; RenderSettings.fogDensity = savedFogDensity; RenderSettings.fogColor = savedFogColor;
        if (driver != null)
        {
            foreach (Renderer rr in hidden) if (rr != null) rr.enabled = true;
            driver.invulnerableUntil = Time.time + 3f;
            // volta para o proprio lado do mapa
            List<Vector3> sp = driver.team == 0 ? Arena.spawnsA : Arena.spawnsB;
            Vector3 back = sp.Count > 0 ? sp[Random.Range(0, sp.Count)] : parkedAt;
            driver.Respawn(back, driver.team == 0 ? 0f : 180f);
            if (driver.cc != null) driver.cc.enabled = true;
            PlayerController pc = driver.GetComponent<PlayerController>();
            if (pc != null) pc.ReturnFromRace();
        }
        Destroy(gameObject);
    }

    void OnDestroy()
    {
        if (I != this) return;
        // Partida encerrada no meio da corrida (ex.: voltou ao menu): devolve a camera e a neblina.
        I = null;
        RenderSettings.fog = savedFog; RenderSettings.fogDensity = savedFogDensity; RenderSettings.fogColor = savedFogColor;
        if (cam != null && cam.transform.parent == null)
        {
            PlayerController pc = driver != null ? driver.GetComponent<PlayerController>() : null;
            if (pc != null) pc.ReturnFromRace(); else Destroy(cam.gameObject);
        }
    }

    // ================= Tela =================

    void OnGUI()
    {
        if (me == null || Game.I == null || Game.I.menus.IsOpen) return;
        float s = Screen.height / 1080f;
        GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));
        float vw = Screen.width / s, cx = vw / 2f;
        float el = (me.done ? me.finishTime : Time.time - t0);
        Color gold = new Color(1f, 0.86f, 0.1f);

        // Posicao e tempo
        int pos = Rank(me);
        Game.Fill(24f, 24f, 220f, 120f, new Color(0f, 0f, 0f, 0.55f));
        Game.Text(36f, 24f, 200f, 80f, pos + "º / 4", 64, pos == 1 ? gold : Color.white, TextAnchor.MiddleLeft);
        Game.Text(36f, 100f, 200f, 36f, Clock(el), 26, Color.white, TextAnchor.MiddleLeft);

        // Progresso (pontinhos de cada carro numa linha)
        float bx = cx - 360f, by = 30f, bw = 720f;
        Game.Fill(bx, by + 10f, bw, 6f, new Color(1f, 1f, 1f, 0.3f));
        Game.Fill(bx + bw - 4f, by, 8f, 26f, gold);
        foreach (Car c in cars)
        {
            float f = Mathf.Clamp01(c.prog / Len);
            Color cc = c.human ? new Color(0.3f, 0.85f, 1f) : new Color(1f, 0.4f, 0.3f);
            Game.Fill(bx + bw * f - 7f, by + 6f, 14f, 14f, cc);
            if (c.human) Game.Text(bx + bw * f - 40f, by + 22f, 80f, 20f, "VOCE", 13, cc, TextAnchor.MiddleCenter);
        }
        Game.Text(bx, by + 40f, bw, 22f, Mathf.Max(0, Mathf.RoundToInt((Len - me.prog))) + " m ate a praia", 15, new Color(1f, 1f, 1f, 0.7f), TextAnchor.MiddleCenter);

        // Velocimetro
        float kmh = Mathf.Abs(me.speed) * 3.6f;
        Game.Fill(vw - 300f, 1080f - 190f, 270f, 150f, new Color(0f, 0f, 0f, 0.55f));
        Game.Text(vw - 300f, 1080f - 190f, 250f, 110f, Mathf.RoundToInt(kmh).ToString(), 76, Color.white, TextAnchor.MiddleRight);
        Game.Text(vw - 300f, 1080f - 82f, 250f, 34f, "km/h", 22, new Color(1f, 1f, 1f, 0.7f), TextAnchor.MiddleRight);
        Game.Fill(vw - 290f, 1080f - 52f, 250f * Mathf.Clamp01(kmh / 270f), 6f, me.driftK > 0.3f ? Art.Orange : gold);

        // Abertura (pega de surpresa) e dicas de controle
        float t = Time.time - t0;
        if (t < 4f)
        {
            Game.Text(cx - 500f, 300f, 1000f, 90f, "CORRIDA DA MONTANHA!", 64, gold, TextAnchor.MiddleCenter);
            Game.Text(cx - 500f, 380f, 1000f, 44f, "A chave da piscina abriu a estrada. Chegue primeiro na praia!", 26, Color.white, TextAnchor.MiddleCenter);
        }
        if (t < 9f && !me.done)
            Game.Text(cx - 500f, 1080f - 60f, 1000f, 30f, TouchPad.On ? "Acelera sozinho  -  incline o direcional para virar  -  PULO derrapa  -  puxe para baixo para frear" : "W acelera   S freia   A / D vira   ESPACO derrapa", 18, new Color(1f, 1f, 1f, 0.8f), TextAnchor.MiddleCenter);
        Vector3 fwd = Quaternion.Euler(0f, me.yaw, 0f) * Vector3.forward;
        if (!me.done && me.speed > 5f && Vector3.Dot(fwd, T[me.idx]) < -0.3f)
            Game.Text(cx - 300f, 460f, 600f, 60f, "CONTRAMAO!", 48, new Color(1f, 0.3f, 0.2f), TextAnchor.MiddleCenter);

        // Resultado
        if (me.done)
        {
            List<Car> order = new List<Car>(cars);
            order.Sort((a, b) => Rank(a).CompareTo(Rank(b)));
            float px = cx - 330f, py = 300f;
            Game.Fill(px, py, 660f, 90f + order.Count * 56f + 60f, new Color(0f, 0f, 0f, 0.78f));
            Game.Text(px, py + 10f, 660f, 70f, pos == 1 ? "VOCE VENCEU!" : pos + "º LUGAR", 48, pos == 1 ? gold : Color.white, TextAnchor.MiddleCenter);
            for (int i = 0; i < order.Count; i++)
            {
                Car c = order[i];
                float y = py + 90f + i * 56f;
                Game.Fill(px + 20f, y, 620f, 50f, c.human ? new Color(0.3f, 0.85f, 1f, 0.18f) : new Color(1f, 1f, 1f, 0.05f));
                Game.Text(px + 36f, y, 60f, 50f, (i + 1) + "º", 26, i == 0 ? gold : Color.white, TextAnchor.MiddleLeft);
                Game.Text(px + 100f, y, 320f, 50f, c.name, 24, Color.white, TextAnchor.MiddleLeft);
                string tm = c.done ? Clock(c.finishTime) : "+" + Mathf.RoundToInt(Len - c.prog) + " m";
                Game.Text(px + 400f, y, 220f, 50f, tm, 24, Color.white, TextAnchor.MiddleRight);
            }
            Game.Text(px, py + 90f + order.Count * 56f + 6f, 660f, 40f, "Voltando para a partida em " + Mathf.CeilToInt(Mathf.Max(0f, endAt - Time.time)) + " s  (F para voltar agora)", 18, new Color(1f, 1f, 1f, 0.75f), TextAnchor.MiddleCenter);
        }
    }

    static string Clock(float t)
    {
        int m = Mathf.FloorToInt(t / 60f);
        float sec = t - m * 60f;
        return m + ":" + sec.ToString("00.00");
    }
}

// A chave da montanha no mapa A CASA (treino): fica boiando na piscina.
// So jogador humano pega. (Treino: quem juntar as 4 sozinho corre contra 3 pilotos do jogo.
// Online, com 4 pessoas diferentes, vem na proxima etapa.)
public class RaceKeys : MonoBehaviour
{
    public static RaceKeys I;
    readonly List<Transform> keys = new List<Transform>();
    readonly List<float> baseY = new List<float>();
    int got;
    const int need = 1;   // chaves para abrir a corrida (eram 4 sorteadas pelo mapa)
    float noteUntil;

    public static void Spawn()
    {
        Clear();
        if (!Arena.Casa || NetGame.Online || Game.I == null) return;
        GameObject g = new GameObject("ChavesDaMontanha");
        I = g.AddComponent<RaceKeys>();
        I.Build();
    }

    public static void Clear()
    {
        if (I != null) Destroy(I.gameObject);
        I = null;
    }

    void Build()
    {
        useGUILayout = false;
        Material gold = Art.Glow(new Color(1f, 0.78f, 0.2f), 1.2f);
        List<Vector3> picked = new List<Vector3>();
        // Treino: uma chave so, boiando no meio da piscina. Pulou na piscina e pegou: corrida.
        picked.Add(Arena.PoolCenter + Vector3.up * -0.45f);   // boia a ~0,45 m acima da agua: alcanca de dentro da piscina
        for (int tries = 0; tries < 400 && picked.Count < need; tries++)
        {
            Vector3 p = Arena.RandomPoint();
            bool ok = true;
            foreach (Vector3 q in picked) if (Vector3.Distance(p, q) < 10f) ok = false;
            foreach (Vector3 q in Arena.spawnsA) if (Vector3.Distance(p, q) < 6f) ok = false;
            foreach (Vector3 q in Arena.spawnsB) if (Vector3.Distance(p, q) < 6f) ok = false;
            if (ok) picked.Add(p);
        }
        foreach (Vector3 p in picked)
        {
            Transform k = new GameObject("chave").transform;
            k.SetParent(transform, false);
            k.position = p + Vector3.up * 0.9f;
            GameObject ring = Art.Prim(PrimitiveType.Cylinder, k, new Vector3(0f, 0.16f, 0f), new Vector3(0.26f, 0.025f, 0.26f), gold, false, "argola");
            ring.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            Art.Box(k, new Vector3(0f, -0.12f, 0f), new Vector3(0.05f, 0.42f, 0.05f), gold, false, "haste");
            Art.Box(k, new Vector3(0.06f, -0.26f, 0f), new Vector3(0.09f, 0.05f, 0.04f), gold, false, "dente");
            Art.Box(k, new Vector3(0.05f, -0.17f, 0f), new Vector3(0.07f, 0.05f, 0.04f), gold, false, "dente");
            Art.PointLight(k, Vector3.zero, new Color(1f, 0.8f, 0.3f), 4f, 1.2f);   // brilho para achar dentro da agua
            keys.Add(k);
            baseY.Add(k.position.y);
        }
    }

    void Update()
    {
        Game g = Game.I;
        if (g == null || g.player == null || Race.Active) return;
        float t = Time.time;
#if UNITY_EDITOR
        // So no editor: F10 abre a corrida direto (para testar sem cacar as chaves).
        if (Input.GetKeyDown(KeyCode.F10)) { Race.Begin(g.player); Clear(); return; }
        // F11: pula para a borda da piscina (testar a chave).
        if (Input.GetKeyDown(KeyCode.F11))
        {
            CharacterController pcc = g.player.GetComponent<CharacterController>();
            if (pcc != null) pcc.enabled = false;
            g.player.transform.position = Arena.PoolCenter + new Vector3(0f, 0.2f, -6.5f);
            if (pcc != null) pcc.enabled = true;
            if (g.pc != null) g.pc.SetYaw(0f);   // olhando para a piscina
        }
#endif
        for (int i = keys.Count - 1; i >= 0; i--)
        {
            Transform k = keys[i];
            if (k == null) { keys.RemoveAt(i); baseY.RemoveAt(i); continue; }
            k.rotation = Quaternion.Euler(0f, t * 120f + i * 40f, 0f);
            Vector3 p = k.position; p.y = baseY[i] + Mathf.Sin(t * 2f + i) * 0.12f;
            k.position = p;
            if (!g.player.alive || g.player.vehicle != null) continue;
            Vector3 d = g.player.transform.position + Vector3.up * 0.9f - k.position;
            if (d.sqrMagnitude < 1.6f * 1.6f)
            {
                Destroy(k.gameObject);
                keys.RemoveAt(i); baseY.RemoveAt(i);
                got++;
                noteUntil = t + 4f;
                Sfx.UI("pickup", 0.9f, 0.8f + got * 0.1f);
                if (got >= need) { Race.Begin(g.player); Clear(); return; }
            }
        }
    }

    void OnGUI()
    {
        if (got == 0 || got >= need || Game.I == null || Game.I.menus.IsOpen || Race.Active) return;
        float s = Screen.height / 1080f;
        GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));
        float y = 260f;
        Game.Fill(24f, y, 260f, 44f, new Color(0f, 0f, 0f, 0.55f));
        Game.Text(36f, y, 240f, 44f, "CHAVES  " + got + " / " + need, 22, new Color(1f, 0.82f, 0.25f), TextAnchor.MiddleLeft);
        if (Time.time < noteUntil) Game.Text(24f, y + 46f, 520f, 26f, got < need ? "Uma chave misteriosa... faltam " + (need - got) + "." : "", 16, new Color(1f, 1f, 1f, 0.75f), TextAnchor.MiddleLeft);
    }
}
