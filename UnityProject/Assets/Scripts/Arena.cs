using System.Collections.Generic;
using UnityEngine;

// Vila no deserto (layout original) gerada a partir de um mapa de texto. O mesmo mapa serve de malha de navegacao dos bots.
// '#' casa/muro  'o' pilar  'x' caixotes  'd' portal em arco  'T' passagem coberta  'J' plataforma de salto  'P' item  'A' spawn (espelhado vira 'B')
// 'S' escada encostada na casa vizinha (sobe ao telhado)  'U' entrada do esgoto (escada descendo para os tuneis)
public static partial class Arena
{
    public static float Cell = 4f;   // 4 m nos mapas grandes; 0,5 m na CASA
    public static int W, H;
    public static char[,] map;
    public static readonly List<Vector3> spawnsA = new List<Vector3>();
    public static readonly List<Vector3> spawnsB = new List<Vector3>();
    public static readonly List<Vector3> walkable = new List<Vector3>();
    public static Texture2D minimap;

    // Mapas: 0 = MERCADO (vila no deserto), 1 = NO GELO (base congelada, fechada, sem mecas nem dinossauros).
    public static int mapId = -1;
    public static bool Ice { get { return mapId == 1; } }
    public static readonly string[] MapNames = { "MERCADO", "NO GELO", "A CASA" };
    public static int MapFromName(string n) { int i = System.Array.IndexOf(MapNames, n); return i < 0 ? 0 : i; }
    // Celulas pedidas para os pontos de bomba (o BombMode acha a livre mais proxima).
    public static int[] SiteA { get { return Casa ? CellOf(5f, -8.5f) : (Ice ? new[] { 7, 16 } : new[] { 3, 17 }); } }
    public static int[] SiteB { get { return Casa ? CellOf(-15.7f, 11f) : (Ice ? new[] { 11, 21 } : new[] { 19, 17 }); } }
    static int[] CellOf(float x, float z) { int cx, cy; ToCell(new Vector3(x, 0f, z), out cx, out cy); return new[] { cx, cy }; }

    // NO GELO: corredores de uma celula, muitos trechos cobertos ('T'), portoes 'G' (abrem e fecham),
    // (26/11: colunas de gelo saíram da frente das entradas do subterraneo e do corredor central dos tuneis;
    //  agora ficam nas pracas ao lado dos tuneis do norte/sul. Os caixotes do corredor central foram para (8,8).)
    // escadas 'S' para os tetos, entradas 'U' para o subterraneo e colunas de gelo 'o'.
    static readonly string[] iceHalf =
    {
        "#########################",
        "#A.A.#......T......#..P.#",
        "#....G.###o.T.o###.#....#",
        "#A.A.#.#S.......S#.G.x..#",
        "##G###.#..x...x..#.###.##",
        "#....T.###.#U#.###.T....#",
        "#.##.#.....#.#.....#.##.#",
        "#.#S.G.x.###.###.x.G.S#.#",
        "#.#..#..x..T.T.....#..#.#",
        "#U#.####.#.....#.####.#.#",
        "#...T..S.#.x.x.#.S..T...#",
        "####.#####G###G#####.####",
        "#P.......T.....T.......P#"
    };
    static readonly int[][] IceSewerHalf =
    {
        new[] { 1, 10 }, new[] { 1, 11 },
        new[] { 12, 6 }, new[] { 12, 7 }, new[] { 12, 8 }, new[] { 12, 9 }, new[] { 12, 10 }, new[] { 12, 11 }
    };

    // Metade de cima; a metade de baixo e gerada girando 180 graus (mapa simetrico e justo).
    static readonly string[] half =
    {
        "#########################",
        "#A.A.....#......T......##",
        "#........#..x...T....P..#",
        "#A.A..x..d......####....#",
        "#.......S#....U.....#.x.#",
        "####d#####....o.....d...#",
        "#.......##.x.....####...#",
        "#..x.....d.......#S.....#",
        "#.P......#####d###...x..#",
        "#U...o...#S.....T.......#",
        "###d######..x...T..o..###",
        "#.......T.......#.......#",
        "#J..x...T...P...d...x..J#"
    };

    static int[] prev;
    static int[] queue;
    static readonly int[] DX = { 1, -1, 0, 0, 1, 1, -1, -1 };
    static readonly int[] DY = { 0, 0, 1, -1, 1, -1, 1, -1 };

    static string Fix(string s, int w)
    {
        if (s.Length > w) return s.Substring(0, w);
        return s.PadRight(w, '.');
    }

    static void BuildMap()
    {
        string[] half = Ice ? iceHalf : Arena.half;
        W = half[0].Length;
        H = half.Length * 2 - 1;
        map = new char[W, H];
        for (int y = 0; y < H; y++)
        {
            string row;
            if (y < half.Length) row = Fix(half[y], W);
            else
            {
                char[] src = Fix(half[H - 1 - y], W).ToCharArray();
                System.Array.Reverse(src);
                row = new string(src).Replace('A', 'B');
            }
            for (int x = 0; x < W; x++)
            {
                char c = row[x];
                if (x == 0 || y == 0 || x == W - 1 || y == H - 1) c = '#';
                map[x, y] = c;
            }
        }
        prev = new int[W * H];
        queue = new int[W * H];
        BuildSewerMap();
    }

    public static bool Walk(int x, int y)
    {
        if (x < 0 || y < 0 || x >= W || y >= H) return false;
        char c = map[x, y];
        return c != '#' && c != 'o' && c != 'x' && c != 'U' && c != '~';
    }

    // ------------------------------ Esgoto (subterraneo) ------------------------------
    // Tuneis a 3,6 m de profundidade, celula a celula (metade de cima; a outra metade e espelhada).
    // Um tunel atravessa o mapa pela linha do meio; dois ramais sobem ate as entradas 'U'.
    public const float SewerFloor = -4.2f, SewerCeil = -0.9f;   // 3,3 m de altura
    public static bool[,] sewer;
    static readonly int[][] SewerHalf =
    {
        new[] { 1, 10 }, new[] { 1, 11 },                                                            // ramal oeste (entrada 1,9)
        new[] { 14, 5 }, new[] { 14, 6 }, new[] { 14, 7 }, new[] { 14, 8 }, new[] { 14, 9 }, new[] { 14, 10 }, new[] { 14, 11 }  // ramal norte (entrada 14,4)
    };

    public static bool Sewer(int x, int y) { return x >= 0 && y >= 0 && x < W && y < H && (sewer[x, y] || map[x, y] == 'U'); }

    static void BuildSewerMap()
    {
        sewer = new bool[W, H];
        int mid = (H - 1) / 2;
        for (int x = 1; x < W - 1; x++) sewer[x, mid] = true;   // tunel principal, de ponta a ponta
        foreach (int[] c in (Ice ? IceSewerHalf : SewerHalf))
        {
            sewer[c[0], c[1]] = true;
            sewer[W - 1 - c[0], H - 1 - c[1]] = true;
        }
    }

    public static Vector3 Center(int x, int y)
    {
        return new Vector3((x - (W - 1) / 2f) * Cell, 0f, (y - (H - 1) / 2f) * Cell);
    }

    public static void ToCell(Vector3 p, out int x, out int y)
    {
        x = Mathf.RoundToInt(p.x / Cell + (W - 1) / 2f);
        y = Mathf.RoundToInt(p.z / Cell + (H - 1) / 2f);
    }

    public static Vector3 RandomPoint()
    {
        return walkable[Random.Range(0, walkable.Count)];
    }

    // Busca em largura no grid, com diagonais que nao cortam quinas.
    public static List<Vector3> FindPath(Vector3 from, Vector3 to)
    {
        List<Vector3> res = new List<Vector3>();
        int sx, sy, tx, ty;
        ToCell(from, out sx, out sy);
        ToCell(to, out tx, out ty);
        Vector3 goal = new Vector3(to.x, 0f, to.z);

        if (!Walk(tx, ty))
        {
            bool found = false;
            for (int i = 0; i < 8 && !found; i++)
            {
                if (Walk(tx + DX[i], ty + DY[i]))
                {
                    tx += DX[i]; ty += DY[i];
                    goal = Center(tx, ty);
                    found = true;
                }
            }
            if (!found) { res.Add(goal); return res; }
        }
        if (!Walk(sx, sy) || (sx == tx && sy == ty)) { res.Add(goal); return res; }

        int n = W * H;
        for (int i = 0; i < n; i++) prev[i] = -1;
        int start = sy * W + sx, target = ty * W + tx;
        int head = 0, tail = 0;
        queue[tail++] = target;
        prev[target] = target;
        while (head < tail && prev[start] == -1)
        {
            int cur = queue[head++];
            int cx = cur % W, cy = cur / W;
            for (int i = 0; i < 8; i++)
            {
                int nx = cx + DX[i], ny = cy + DY[i];
                if (!Walk(nx, ny)) continue;
                if (i >= 4 && (!Walk(cx + DX[i], cy) || !Walk(cx, cy + DY[i]))) continue;
                int ni = ny * W + nx;
                if (prev[ni] != -1) continue;
                prev[ni] = cur;
                queue[tail++] = ni;
            }
        }
        if (prev[start] == -1) { res.Add(goal); return res; }

        int c = start;
        int guard = 0;
        while (c != target && guard++ < n)
        {
            c = prev[c];
            res.Add(Center(c % W, c / W));
        }
        if (res.Count > 0) res[res.Count - 1] = goal; else res.Add(goal);
        return res;
    }

    static float Hash(int x, int y)
    {
        unchecked
        {
            int h = x * 374761393 + y * 668265263;
            h = (h ^ (h >> 13)) * 1274126177;
            return ((h ^ (h >> 16)) & 0x7fffffff) / (float)0x7fffffff;
        }
    }

    static Material sand, paving, wood, stone, dark;
    static Material[] walls;

    public static void Build(Transform root, int id = 0)
    {
        mapId = id;
        Gate.All.Clear();
        sewerLamps = 0;
        Cell = 4f;
        Breakable.All.Clear();
        if (!Casa) BuildMap();
        Art.BuildTextures();
        spawnsA.Clear(); spawnsB.Clear(); walkable.Clear();
        float sizeX = W * Cell, sizeZ = H * Cell;

        sand = Art.Pbr("Ground054", new Color(1.05f, 0.95f, 0.8f), 5f, 1f, 0f, 0f, 1.2f);
        paving = Art.Pbr("PavingStones070", new Color(0.95f, 0.86f, 0.7f), 3.5f, 1f, 0f, 0f);
        wood = Art.Pbr("Planks037A", new Color(1.35f, 1.1f, 0.85f), 1.6f, 1f, 0f, 0f);
        stone = Art.Pbr("PavingStones070", new Color(0.8f, 0.72f, 0.6f), 2.5f, 1f, 0f, 0f);
        dark = Art.Mat(new Color(0.05f, 0.04f, 0.035f), Color.black, 0f, default(Color), 0.2f, 0f);
        walls = new Material[]
        {
            Art.Pbr("Bricks084", new Color(1f, 0.93f, 0.78f), 3f, 1f, 0f, 0f, 1.1f),
            Art.Pbr("Plaster001", new Color(0.9f, 0.76f, 0.55f), 4f, 1f, 0f, 0f),
            Art.Pbr("Plaster001", new Color(0.82f, 0.8f, 0.72f), 4f, 1f, 0f, 0f),
            Art.Pbr("Bricks084", new Color(0.85f, 0.72f, 0.58f), 3f, 1f, 0f, 0f, 1.1f)
        };

        if (Casa)
        {
            BuildCasa(root);
            BuildMinimap();
            return;
        }

        if (Ice)
        {
            // Paleta fria: neve no chao, concreto e metal congelados nas paredes.
            sand = Art.Pbr("Plaster001", new Color(1.12f, 1.17f, 1.26f), 6f, 1.3f, 0f, 0f, 0.6f);   // neve
            paving = Art.Pbr("Concrete034", new Color(0.82f, 0.88f, 0.96f), 3.5f, 1.2f, 0f, 0f);
            wood = Art.Pbr("Planks037A", new Color(0.78f, 0.74f, 0.72f), 1.6f, 1f, 0f, 0f);
            stone = Art.Pbr("Concrete034", new Color(0.7f, 0.76f, 0.84f), 2.5f, 1f, 0f, 0f);
            dark = Art.Mat(new Color(0.03f, 0.04f, 0.06f), Color.black, 0f, default(Color), 0.3f, 0f);
            walls = new Material[]
            {
                Art.Pbr("Plaster001", new Color(0.8f, 0.88f, 0.98f), 4f, 1f, 0f, 0f),
                Art.Pbr("Metal032", new Color(0.62f, 0.7f, 0.8f), 2.5f, 1f, 0.2f, 0f),
                Art.Pbr("Concrete034", new Color(0.78f, 0.86f, 0.96f), 3f, 1.1f, 0f, 0f),
                Art.Pbr("Concrete034", new Color(0.55f, 0.62f, 0.72f), 3f, 1f, 0f, 0f)
            };
        }

        // Chao: areia em toda a volta; so as entradas do esgoto ficam abertas.
        Ground(root, sizeX, sizeZ);
        // Casas num grupo proprio: no fim viram poucas malhas (static batching) para nao pesar no celular.
        Transform houses = new GameObject("houses").transform;
        houses.SetParent(root, false);

        for (int y = 0; y < H; y++)
        {
            for (int x = 0; x < W; x++)
            {
                char c = map[x, y];
                Vector3 p = Center(x, y);
                float hsh = Hash(x, y);
                if (Walk(x, y)) walkable.Add(p);

                if (sewer[x, y] && c != 'U') SewerCell(root, x, y, p);
                if (c == '#') { if (Ice) IceBlock(houses, x, y, p); else House(houses, x, y, p); }
                else if (c == 'G') Gate.Build(root, x, y, p, !Walk(x - 1, y) && !Walk(x + 1, y), stone, dark);
                else if (c == 'o' && Ice) IcePillar(root, p, hsh);
                else if (c == 'S') Stairs(root, x, y, p);
                else if (c == 'U') SewerEntrance(root, x, y, p);
                else if (c == 'o') Fountain.Build(root, p, stone, hsh);
                else if (c == 'x') Crates(root, p, hsh);
                else if (c == 'd') Arch(root, x, y, p);
                else if (c == 'T') Tunnel(root, x, y, p);
                else if (c == 'J')
                {
                    Art.Prim(PrimitiveType.Cylinder, root, p + Vector3.up * 0.02f, new Vector3(3f, 0.02f, 3f), stone, false, "padBase");
                    GameObject pad = Art.Prim(PrimitiveType.Cylinder, root, p + Vector3.up * 0.05f, new Vector3(2.3f, 0.03f, 2.3f), Art.Glow(Art.Yellow, 1.3f), false, "jumpPad");
                    pad.AddComponent<JumpPad>();
                }
                else if (c == 'P')
                {
                    GameObject pk = new GameObject("pickup");
                    pk.transform.SetParent(root, false);
                    pk.transform.position = p;
                    Pickup pu = pk.AddComponent<Pickup>();
                    pu.kind = ((x + y) % 2 == 0) ? 0 : 1;
                }
                else if (c == 'A') spawnsA.Add(p);
                else if (c == 'B') spawnsB.Add(p);

                if (Ice) { if (c == '.' && hsh > 0.82f) SnowPile(root, p, hsh); }
                else if (c == '.' && x % 6 == 3 && y % 5 == 1 && hsh > 0.35f) Palm(root, p + new Vector3(1.2f, 0f, 1.1f), hsh);
                else if (c == '.' && ExtraPalm(x, y)) Palm(root, p + new Vector3(0.9f, 0f, -0.8f), Hash(x * 3 + 1, y * 7 + 2));
                if (c == '.' && hsh > 0.93f) Barrels(root, p + new Vector3(-1.2f, 0f, 1.2f), hsh);
            }
        }
        if (Ice) IceSurroundings(root, sizeX, sizeZ); else Surroundings(root, sizeX, sizeZ);
        BuildMinimap();
        try { StaticBatchingUtility.Combine(houses.gameObject); } catch (System.Exception) { }
    }

    // Casa / muro: um bloco por celula; a altura e o material sao por "quarteirao" para formar predios.
    static bool IsEdge(int x, int y) { return x == 0 || y == 0 || x == W - 1 || y == H - 1; }
    static bool Inner(int x, int y) { return x > 0 && y > 0 && x < W - 1 && y < H - 1 && map[x, y] == '#'; }

    static float HouseHeight(int x, int y)
    {
        float g = Hash(x / 2 + 31, y / 2 + 7);
        if (IsEdge(x, y)) return 7.5f + g * 3f;
        if (NextToStairs(x, y)) return 4.6f;   // casa com escada: altura padrao, para a escada alcancar o telhado
        return 4.6f + Mathf.Floor(g * 3f) * 1.3f;
    }

    static void House(Transform root, int x, int y, Vector3 p)
    {
        bool edge = IsEdge(x, y);
        float h = HouseHeight(x, y);
        Material m = walls[(int)(Hash(x / 2 + 5, y / 2 + 11) * 3.99f)];
        if (!edge) { HollowHouse(root, x, y, p, h, m); return; }
        Art.Box(root, p + Vector3.up * (h / 2f), new Vector3(Cell, h, Cell), m, true, "house");
        Art.Box(root, p + Vector3.up * (h + 0.12f), new Vector3(Cell + 0.3f, 0.3f, Cell + 0.3f), stone, false, "parapet");

        // Detalhes nas faces viradas para a rua.
        for (int d = 0; d < 4; d++)
        {
            int nx = x + DX[d], ny = y + DY[d];
            if (!Walk(nx, ny)) continue;
            if (map[nx, ny] == 'S') continue;   // lado da escada: sem toldo/porta no caminho
            Vector3 n = new Vector3(DX[d], 0f, DY[d]);
            Vector3 side = new Vector3(-n.z, 0f, n.x);
            Vector3 face = p + n * (Cell / 2f);
            Quaternion rot = Quaternion.LookRotation(n);
            float k = Hash(x * 7 + d, y * 13 + d);
            if (k < 0.5f && h > 4.5f)
            {
                // Janela com veneziana de madeira.
                Piece(root, face + Vector3.up * 2.9f + n * 0.02f, rot, new Vector3(1.2f, 1.4f, 0.12f), dark, "window");
                Piece(root, face + Vector3.up * 2.9f + n * 0.07f - side * 0.86f, rot * Quaternion.Euler(0f, 25f, 0f), new Vector3(0.6f, 1.4f, 0.06f), wood, "shutter");
                Piece(root, face + Vector3.up * 2.9f + n * 0.07f + side * 0.86f, rot * Quaternion.Euler(0f, -25f, 0f), new Vector3(0.6f, 1.4f, 0.06f), wood, "shutter");
                Piece(root, face + Vector3.up * 2.12f + n * 0.1f, rot, new Vector3(1.5f, 0.12f, 0.3f), stone, "sill");
            }
            else if (k < 0.66f)
            {
                // Porta de madeira com verga de pedra.
                Piece(root, face + Vector3.up * 1.15f + n * 0.04f, rot, new Vector3(1.3f, 2.3f, 0.1f), wood, "door");
                Piece(root, face + Vector3.up * 2.42f + n * 0.08f, rot, new Vector3(1.7f, 0.25f, 0.25f), stone, "lintel");
            }
            else if (k < 0.8f)
            {
                // Toldo de pano.
                Color cloth = k < 0.73f ? new Color(0.55f, 0.16f, 0.1f) : new Color(0.15f, 0.3f, 0.42f);
                Piece(root, face + Vector3.up * 2.75f + n * 0.75f, rot * Quaternion.Euler(18f, 0f, 0f), new Vector3(3f, 0.05f, 1.6f), Art.Mat(cloth, Color.black, 0f, default(Color), 0.15f, 0f), "awning");
                Piece(root, face + Vector3.up * 1.3f + n * 1.45f - side * 1.4f, Quaternion.identity, new Vector3(0.07f, 2.6f, 0.07f), wood, "pole");
                Piece(root, face + Vector3.up * 1.3f + n * 1.45f + side * 1.4f, Quaternion.identity, new Vector3(0.07f, 2.6f, 0.07f), wood, "pole");
            }
            if (h > 5.5f && k > 0.3f)
                for (int b = -1; b <= 1; b++)
                    Piece(root, face + Vector3.up * (h - 0.9f) + n * 0.2f + side * (b * 1.2f), rot, new Vector3(0.16f, 0.16f, 0.6f), wood, "beam");
        }
    }

    const float WallT = 0.3f;
    const float Floor2 = 3.0f;   // piso do segundo andar

    // Faixa de parede (de yA ate yB) numa face da celula, com um vao no meio (largura w, de h0 ate h1). w = 0: inteira.
    // centered: parede na divisa entre duas casas (feita uma vez so); senao colada na face, por dentro.
    static void WallBand(Transform root, Vector3 p, int d, float yA, float yB, Material m, float w, float h0, float h1, bool centered)
    {
        if (yB - yA < 0.01f) return;
        Vector3 n = new Vector3(DX[d], 0f, DY[d]);
        Vector3 side = new Vector3(-n.z, 0f, n.x);
        bool alongX = DX[d] != 0;
        Vector3 basePos = p + n * (centered ? Cell / 2f : Cell / 2f - WallT / 2f);
        System.Action<float, float, float, float> seg = (s0, s1, a, b) =>
        {
            a = Mathf.Max(a, yA); b = Mathf.Min(b, yB);
            if (s1 - s0 < 0.01f || b - a < 0.01f) return;
            Vector3 c = basePos + side * ((s0 + s1) / 2f) + Vector3.up * ((a + b) / 2f);
            Vector3 size = alongX ? new Vector3(WallT, b - a, s1 - s0) : new Vector3(s1 - s0, b - a, WallT);
            Art.Box(root, c, size, m, true, "wall");
        };
        float L = Cell / 2f;
        if (w <= 0f || h1 <= yA || h0 >= yB) { seg(-L, L, yA, yB); return; }
        seg(-L, -w / 2f, yA, yB);
        seg(w / 2f, L, yA, yB);
        seg(-w / 2f, w / 2f, yA, h0);
        seg(-w / 2f, w / 2f, h1, yB);
    }

    static bool TwoStoryCandidate(int x, int y) { return Inner(x, y) && HouseHeight(x, y) > 5.5f; }

    // Casa por dentro: portas, janelas abertas, buracos baixos (so agachado), comodos ligados aos vizinhos do mesmo
    // predio, segundo andar com escada interna nas casas altas, laje no telhado (andavel por cima) e caixotes.
    static void HollowHouse(Transform root, int x, int y, Vector3 p, float h, Material m)
    {
        // Tipo de cada face: 0 nada (casa da borda), 1 vizinho interno, 2 rua, 3 parede cega (escada/caixote/chafariz)
        int[] kind = new int[4];
        float[] k = new float[4];
        int[] open = new int[4];   // rua: 0 janela, 1 porta, 2 buraco baixo (agachado), 3 parede sem abertura
        int street = 0, inner = 0;
        bool tall = h > 5.5f;
        for (int d = 0; d < 4; d++)
        {
            int nx = x + DX[d], ny = y + DY[d];
            k[d] = Hash(x * 7 + d, y * 13 + d);
            if (nx < 0 || ny < 0 || nx >= W || ny >= H || (map[nx, ny] == '#' && IsEdge(nx, ny))) kind[d] = 0;
            else if (Inner(nx, ny)) { kind[d] = 1; inner++; }
            else if (map[nx, ny] == 'S' && StairWall(nx, ny) == d) kind[d] = 3;
            else if (Walk(nx, ny))
            {
                kind[d] = 2; street++;
                // Metade das faces fica fechada; o resto vira porta, buraco baixo ou janela.
                open[d] = k[d] >= 0.75f ? 1 : (k[d] >= 0.65f ? 2 : (k[d] >= 0.5f ? 0 : 3));
            }
            else kind[d] = 3;
        }
        // Casa sozinha com rua em lados opostos: porta nos dois (vira passagem de um lado para o outro).
        if (inner == 0)
        {
            if (kind[0] == 2 && kind[1] == 2) open[0] = open[1] = 1;
            else if (kind[2] == 2 && kind[3] == 2) open[2] = open[3] = 1;
        }
        // Casa sozinha sem nenhuma abertura ganha uma porta (as do mesmo predio se ligam por dentro).
        bool any = false;
        for (int d = 0; d < 4; d++) any |= kind[d] == 2 && open[d] != 3;
        if (!any && street > 0 && inner == 0)
        {
            int bd = -1;
            for (int d = 0; d < 4; d++) if (kind[d] == 2 && (bd < 0 || k[d] > k[bd])) bd = d;
            open[bd] = 1;
        }

        // Segundo andar: precisa de uma parede livre para a escada (cega, colada na borda ou com janela).
        int stairD = -1;
        if (tall)
        {
            for (int d = 0; d < 4 && stairD < 0; d++) if (kind[d] == 0 || kind[d] == 3) stairD = d;
            for (int d = 0; d < 4 && stairD < 0; d++) if (kind[d] == 2 && open[d] == 3) stairD = d;
            for (int d = 0; d < 4 && stairD < 0; d++) if (kind[d] == 2 && open[d] == 0) stairD = d;
        }
        bool two = tall;   // casa alta sempre tem 2o andar; sem parede livre a escada fica no vizinho

        for (int d = 0; d < 4; d++)
        {
            Vector3 n = new Vector3(DX[d], 0f, DY[d]);
            Vector3 side = new Vector3(-n.z, 0f, n.x);
            Vector3 face = p + n * (Cell / 2f);
            Quaternion rot = Quaternion.LookRotation(n);
            switch (kind[d])
            {
                case 1:
                    // Passagem larga para o comodo vizinho; a parede sobe ate a casa mais alta. So um dos dois constroi.
                    if (DX[d] + DY[d] > 0)
                    {
                        int nx = x + DX[d], ny = y + DY[d];
                        float top = Mathf.Max(h, HouseHeight(nx, ny));
                        bool bothTwo = two && TwoStoryCandidate(nx, ny);
                        WallBand(root, p, d, 0f, Floor2, m, 2.0f, 0f, 2.7f, true);
                        if (bothTwo) WallBand(root, p, d, Floor2, top, m, 2.0f, Floor2, Floor2 + 2.4f, true);
                        else WallBand(root, p, d, Floor2, top, m, 0f, 0f, 0f, true);
                    }
                    break;
                case 2:
                {
                    bool stairHere = d == stairD;
                    if (stairHere) WallBand(root, p, d, 0f, Floor2, m, 0f, 0f, 0f, false);
                    else if (open[d] == 1)
                    {
                        WallBand(root, p, d, 0f, Floor2, m, 1.5f, 0f, 2.5f, false);
                        Piece(root, face + Vector3.up * 2.62f + n * 0.05f, rot, new Vector3(1.9f, 0.25f, 0.4f), stone, "lintel");
                        // Porta de madeira aberta, encostada na parede de dentro.
                        Piece(root, face - n * 0.75f + side * 1.05f + Vector3.up * 1.2f, rot * Quaternion.Euler(0f, 90f, 0f), new Vector3(1.3f, 2.3f, 0.08f), wood, "doorOpen");
                        if (k[d] > 0.6f && k[d] < 0.75f)
                        {
                            Color cloth = k[d] < 0.68f ? new Color(0.55f, 0.16f, 0.1f) : new Color(0.15f, 0.3f, 0.42f);
                            Piece(root, face + Vector3.up * 2.95f + n * 0.75f, rot * Quaternion.Euler(18f, 0f, 0f), new Vector3(3f, 0.05f, 1.6f), Art.Mat(cloth, Color.black, 0f, default(Color), 0.15f, 0f), "awning");
                            Piece(root, face + Vector3.up * 1.4f + n * 1.45f - side * 1.4f, Quaternion.identity, new Vector3(0.07f, 2.8f, 0.07f), wood, "pole");
                            Piece(root, face + Vector3.up * 1.4f + n * 1.45f + side * 1.4f, Quaternion.identity, new Vector3(0.07f, 2.8f, 0.07f), wood, "pole");
                        }
                    }
                    else if (open[d] == 2)
                    {
                        // Buraco baixo na parede: so passa agachado. Moldura de madeira e uma tabua quebrada.
                        // Vao de 1,5 m: em pe (1,8 m) nao passa; agachado (1,2 m) passa folgado.
                        WallBand(root, p, d, 0f, Floor2, m, 1.2f, 0f, 1.5f, false);
                        Piece(root, face + Vector3.up * 1.58f + n * 0.04f, rot, new Vector3(1.5f, 0.16f, 0.36f), wood, "crawlTop");
                        Piece(root, face + Vector3.up * 0.75f + n * 0.04f - side * 0.68f, rot, new Vector3(0.14f, 1.5f, 0.36f), wood, "crawlSide");
                        Piece(root, face + Vector3.up * 0.75f + n * 0.04f + side * 0.68f, rot, new Vector3(0.14f, 1.5f, 0.36f), wood, "crawlSide");
                        // Tabua quebrada pendurada no canto de cima (enfeite, sem colisao e fora do caminho).
                        Piece(root, face + Vector3.up * 1.36f + n * 0.2f - side * 0.38f, rot * Quaternion.Euler(0f, 0f, -32f), new Vector3(0.55f, 0.09f, 0.04f), wood, "plank");
                    }
                    else if (open[d] == 0)
                    {
                        // Janela aberta e alta o bastante para pular por ela (entra e sai da casa).
                        WallBand(root, p, d, 0f, Floor2, m, 1.3f, 0.8f, 2.85f, false);
                        Piece(root, face + Vector3.up * 1.83f + n * 0.07f - side * 0.98f, rot * Quaternion.Euler(0f, 25f, 0f), new Vector3(0.65f, 2f, 0.06f), wood, "shutter");
                        Piece(root, face + Vector3.up * 1.83f + n * 0.07f + side * 0.98f, rot * Quaternion.Euler(0f, -25f, 0f), new Vector3(0.65f, 2f, 0.06f), wood, "shutter");
                        Piece(root, face + Vector3.up * 0.74f + n * 0.08f, rot, new Vector3(1.6f, 0.12f, 0.3f), stone, "sill");
                    }
                    else WallBand(root, p, d, 0f, Floor2, m, 0f, 0f, 0f, false);   // parede fechada
                    // Parte de cima: janela aberta do segundo andar; casa alta sem segundo andar fica com janela fechada.
                    if (two && Hash(x * 5 + d, y * 11 + d) > 0.5f)
                    {
                        // Janela do 2o andar, tambem de pular.
                        float wtop = Mathf.Min(Floor2 + 2.7f, h - 0.32f);
                        WallBand(root, p, d, Floor2, h, m, 1.3f, Floor2 + 0.7f, wtop, false);
                        Piece(root, face + Vector3.up * (Floor2 + 0.64f) + n * 0.08f, rot, new Vector3(1.6f, 0.12f, 0.3f), stone, "sill");
                    }
                    else if (two) WallBand(root, p, d, Floor2, h, m, 0f, 0f, 0f, false);
                    else
                    {
                        WallBand(root, p, d, Floor2, h, m, 0f, 0f, 0f, false);
                        if (tall) Piece(root, face + Vector3.up * (h - 1.7f) + n * 0.02f, rot, new Vector3(1.0f, 1.1f, 0.12f), dark, "window");
                    }
                    if (tall && k[d] > 0.3f)
                        for (int b = -1; b <= 1; b++)
                            Piece(root, face + Vector3.up * (h - 0.9f) + n * 0.2f + side * (b * 1.2f), rot, new Vector3(0.16f, 0.16f, 0.6f), wood, "beam");
                    break;
                }
                case 3:
                    WallBand(root, p, d, 0f, h, m, 0f, 0f, 0f, false);
                    break;
            }
        }

        // Laje do telhado (andavel por cima, teto por dentro) e piso de madeira.
        Art.Box(root, p + Vector3.up * (h - 0.15f), new Vector3(Cell, 0.3f, Cell), m, true, "roof");
        Art.Box(root, p + Vector3.up * 0.02f, new Vector3(Cell - 0.1f, 0.04f, Cell - 0.1f), wood, false, "floor");
        Material lampM = Art.Glow(new Color(1f, 0.85f, 0.55f), 1.2f);
        Art.Box(root, p + Vector3.up * (h - 0.35f), new Vector3(0.5f, 0.1f, 0.5f), lampM, false, "lamp");

        Vector3 away = Vector3.zero;
        if (two && stairD < 0)
        {
            Art.Box(root, p + Vector3.up * (Floor2 - 0.1f), new Vector3(Cell, 0.2f, Cell), wood, true, "floor2");
            Art.Box(root, p + Vector3.up * (Floor2 - 0.3f), new Vector3(0.4f, 0.08f, 0.4f), lampM, false, "lamp");
        }
        if (two && stairD >= 0)
        {
            // Escada interna encostada na parede escolhida e piso do segundo andar com o vao da escada.
            Vector3 sn = new Vector3(DX[stairD], 0f, DY[stairD]);
            Vector3 sside = new Vector3(-sn.z, 0f, sn.x);
            away = -sn;
            const float sw = 1.0f, run = 3.1f;
            const int steps = 10;
            float tread = run / steps, rise = Floor2 / steps;
            float laneN = Cell / 2f - WallT - sw / 2f;
            bool alongX = DX[stairD] == 0;   // escada corre ao longo de "sside"
            for (int i = 0; i < steps; i++)
            {
                float hh = rise * (i + 1);
                Vector3 c = p + sn * laneN + sside * (-run / 2f + tread * (i + 0.5f)) + Vector3.up * (hh / 2f);
                Vector3 size = alongX ? new Vector3(tread + 0.01f, hh, sw) : new Vector3(sw, hh, tread + 0.01f);

                Art.Box(root, c, size, i % 2 == 0 ? stone : wood, true, "innerStair");
            }
            // Piso do 2o andar: toda a celula menos a faixa da escada.
            float holeIn = Cell / 2f - WallT - sw;          // borda do vao, medida a partir do centro na direcao sn
            float slabLen = holeIn + Cell / 2f;              // de -Cell/2 ate holeIn
            Vector3 sc = p + sn * ((holeIn - Cell / 2f) / 2f) + Vector3.up * (Floor2 - 0.1f);
            Vector3 ssize = Mathf.Abs(sn.x) > 0.5f ? new Vector3(slabLen, 0.2f, Cell) : new Vector3(Cell, 0.2f, slabLen);
            Art.Box(root, sc, ssize, wood, true, "floor2");
            // Guarda-corpo na beira do vao (deixa a saida da escada livre no alto).
            Vector3 r0 = p + sn * holeIn + sside * (-run / 2f) + Vector3.up * (Floor2 + 0.5f);
            Vector3 r1 = p + sn * holeIn + sside * (run / 2f - 1.1f) + Vector3.up * (Floor2 + 0.5f);
            Vector3 rsize = Mathf.Abs(sside.x) > 0.5f ? new Vector3((r1 - r0).magnitude, 0.08f, 0.08f) : new Vector3(0.08f, 0.08f, (r1 - r0).magnitude);
            Art.Box(root, (r0 + r1) / 2f + Vector3.up * 0.45f, rsize, wood, false, "rail");
            Art.Box(root, (r0 + r1) / 2f, Mathf.Abs(sside.x) > 0.5f ? new Vector3((r1 - r0).magnitude, 1f, 0.05f) : new Vector3(0.05f, 1f, (r1 - r0).magnitude), dark, true, "railGuard");
            Art.Box(root, p - sn * 0.6f + Vector3.up * (Floor2 - 0.3f), new Vector3(0.4f, 0.08f, 0.4f), lampM, false, "lamp");
        }

        // Caixotes num canto, longe da escada.
        float kk = Hash(x * 11 + 3, y * 17 + 5);
        if (kk < 0.45f)
        {
            Vector3 corner;
            if (stairD >= 0) { Vector3 sside = new Vector3(away.z, 0f, -away.x); corner = p + away * 1.2f + sside * (kk < 0.22f ? -1.2f : 1.2f); }
            else corner = p + new Vector3(kk < 0.22f ? -1.2f : 1.2f, 0f, kk * 7f % 1f < 0.5f ? -1.2f : 1.2f);
            GameObject cr = Art.Box(root, corner + Vector3.up * 0.45f, new Vector3(0.9f, 0.9f, 0.9f), wood, true, "crate");
            cr.transform.rotation = Quaternion.Euler(0f, kk * 40f, 0f);
            if (kk < 0.15f) Art.Box(root, corner + Vector3.up * 1.15f, new Vector3(0.55f, 0.5f, 0.55f), wood, true, "crate");
        }
        Art.Box(root, p + Vector3.up * (h + 0.12f), new Vector3(Cell + 0.3f, 0.3f, Cell + 0.3f), stone, false, "parapet");
    }

    static bool NextToStairs(int x, int y)
    {
        for (int d = 0; d < 4; d++)
        {
            int nx = x + DX[d], ny = y + DY[d];
            if (nx >= 0 && ny >= 0 && nx < W && ny < H && map[nx, ny] == 'S' && StairWall(nx, ny) == d) return true;
        }
        return false;
    }

    // Lado (indice em DX/DY, visto da casa) da casa onde a escada 'S' encosta: a primeira casa interna vizinha.
    static int StairWall(int sx, int sy)
    {
        for (int d = 0; d < 4; d++)
        {
            int hx = sx + DX[d], hy = sy + DY[d];
            if (hx <= 0 || hy <= 0 || hx >= W - 1 || hy >= H - 1) continue;
            if (map[hx, hy] == '#') return d ^ 1;   // direcao oposta: da casa para a escada
        }
        return -1;
    }

    // Chao em faixas (por linha), pulando as celulas de entrada do esgoto; areia larga em volta.
    static void Ground(Transform root, float sizeX, float sizeZ)
    {
        float big = 250f, m = 20f;
        // Areia ate o horizonte em volta (so visual) e colisor de 20 m colado na arena.
        Art.Box(root, new Vector3(0f, -0.06f, sizeZ / 2f + big / 2f), new Vector3(sizeX + 2f * big, 0.1f, big), sand, false, "sand");
        Art.Box(root, new Vector3(0f, -0.06f, -sizeZ / 2f - big / 2f), new Vector3(sizeX + 2f * big, 0.1f, big), sand, false, "sand");
        Art.Box(root, new Vector3(sizeX / 2f + big / 2f, -0.06f, 0f), new Vector3(big, 0.1f, sizeZ), sand, false, "sand");
        Art.Box(root, new Vector3(-sizeX / 2f - big / 2f, -0.06f, 0f), new Vector3(big, 0.1f, sizeZ), sand, false, "sand");
        GroundCollider(root, new Vector3(0f, 0f, sizeZ / 2f + m / 2f), new Vector3(sizeX + 2f * m, 1f, m));
        GroundCollider(root, new Vector3(0f, 0f, -sizeZ / 2f - m / 2f), new Vector3(sizeX + 2f * m, 1f, m));
        GroundCollider(root, new Vector3(sizeX / 2f + m / 2f, 0f, 0f), new Vector3(m, 1f, sizeZ));
        GroundCollider(root, new Vector3(-sizeX / 2f - m / 2f, 0f, 0f), new Vector3(m, 1f, sizeZ));
        for (int y = 0; y < H; y++)
        {
            int x = 0;
            while (x < W)
            {
                if (map[x, y] == 'U') { x++; continue; }
                int x0 = x;
                while (x < W && map[x, y] != 'U') x++;
                Vector3 c = (Center(x0, y) + Center(x - 1, y)) / 2f;
                Vector3 size = new Vector3((x - x0) * Cell, 1f, Cell);
                Art.Box(root, new Vector3(c.x, -0.06f, c.z), new Vector3(size.x, 0.1f, size.z), sand, false, "sand");
                GroundCollider(root, c, size);
            }
        }
    }

    static void GroundCollider(Transform root, Vector3 c, Vector3 size)
    {
        GameObject g = new GameObject("ground");
        g.transform.SetParent(root, false);
        g.transform.position = new Vector3(c.x, -0.5f, c.z);
        g.AddComponent<BoxCollider>().size = size;
    }

    // Escada de pedra encostada na parede da casa, subindo ate o telhado (degraus de 0,35 m).
    static void Stairs(Transform root, int x, int y, Vector3 p)
    {
        Art.Box(root, p + Vector3.up * -0.04f, new Vector3(Cell, 0.1f, Cell), paving, false, "paving");
        int d = StairWall(x, y);
        if (d < 0) return;
        Vector3 toStair = new Vector3(DX[d], 0f, DY[d]);          // da casa para a rua
        Vector3 along = new Vector3(-toStair.z, 0f, toStair.x);   // sobe ao longo da parede
        // O pe da escada fica do lado aberto (antes podia comecar colado numa parede).
        int bx = x - Mathf.RoundToInt(along.x), by = y - Mathf.RoundToInt(along.z);
        if (!Walk(bx, by)) along = -along;
        float top = 4.6f + 0.27f;                                  // casa de 4,6 m + parapeito
        const int steps = 16;                                      // degraus de 30 cm
        float width = 1.5f, run = Cell - 0.2f, tread = run / steps, rise = top / steps;
        Vector3 baseP = p - toStair * (Cell / 2f - width / 2f) - along * (run / 2f);
        for (int i = 0; i < steps; i++)
        {
            float hgt = rise * (i + 1);
            Vector3 c = baseP + along * (tread * (i + 0.5f)) + Vector3.up * (hgt / 2f);
            GameObject st = Art.Box(root, c, Vector3.one, i % 2 == 0 ? stone : walls[0], true, "stair");
            st.transform.rotation = Quaternion.LookRotation(along);
            st.transform.localScale = new Vector3(width, hgt, tread + 0.01f);
        }
        // Corrimao de madeira so na metade de cima (a entrada fica livre).
        Vector3 rail0 = baseP + toStair * (width / 2f) + along * (run * 0.5f) + Vector3.up * (top * 0.5f + 1f);
        Vector3 rail1 = baseP + toStair * (width / 2f) + along * run + Vector3.up * (top + 1f);
        GameObject rail = Art.Box(root, (rail0 + rail1) / 2f, new Vector3(0.08f, 0.08f, (rail1 - rail0).magnitude), wood, false, "rail");
        rail.transform.rotation = Quaternion.LookRotation(rail1 - rail0);
        Art.Box(root, rail0 - Vector3.up * 0.5f, new Vector3(0.08f, 1f, 0.08f), wood, false, "post");
        Art.Box(root, rail1 - Vector3.up * 0.5f, new Vector3(0.08f, 1f, 0.08f), wood, false, "post");
    }

    // Direcao (indice DX/DY) em que a escada da entrada desce: para o tunel vizinho.
    static int SewerDir(int x, int y)
    {
        for (int d = 0; d < 4; d++) if (sewer[x + DX[d], y + DY[d]]) return d;
        return 0;
    }

    static Material Rust() { return Art.Pbr("Metal046B", new Color(0.9f, 0.55f, 0.38f), 1.2f, 0.7f, 0f, 0f); }

    // Entrada do esgoto: buraco na rua com escada descendo ate o tunel, mureta em volta e grade.
    static void SewerEntrance(Transform root, int x, int y, Vector3 p)
    {
        int d = SewerDir(x, y);
        Vector3 down = new Vector3(DX[d], 0f, DY[d]), side = new Vector3(-down.z, 0f, down.x);
        float depth = -SewerFloor;
        const int steps = 12;
        float run = Cell, tread = run / steps, rise = depth / steps;
        Vector3 start = p - down * (Cell / 2f);
        for (int i = 0; i < steps; i++)
        {
            float topY = -rise * (i + 1);
            float hgt = topY - SewerFloor;
            Vector3 c = start + down * (tread * (i + 0.5f)) + Vector3.up * (SewerFloor + Mathf.Max(0.05f, hgt) / 2f);
            GameObject st = Art.Box(root, c, Vector3.one, i % 2 == 0 ? stone : paving, true, "sewerStair");
            st.transform.rotation = Quaternion.LookRotation(down);
            // Degraus entram um pouco nas paredes e um no outro: sem frestas mostrando o lado de baixo da fase.
            st.transform.localScale = new Vector3(Cell - 0.1f, Mathf.Max(0.05f, hgt) + 0.02f, tread + 0.06f);
        }
        // Parede de terra sob a rua na borda de cima da escada (o chao e fino: sem isso aparecia um vao
        // mostrando o lado de baixo da fase).
        GameObject lip = Art.Box(root, start - down * 0.15f + Vector3.up * (-0.77f), Vector3.one, paving, true, "pitLip");
        lip.transform.rotation = Quaternion.LookRotation(down);
        lip.transform.localScale = new Vector3(Cell, 1.5f, 0.3f);
        // Paredes laterais do poco (do fundo ate a rua) e mureta acima do chao nos lados e no fundo.
        for (int s2 = -1; s2 <= 1; s2 += 2)
        {
            Vector3 wc = p + side * (s2 * (Cell / 2f - 0.1f));
            GameObject w = Art.Box(root, wc + Vector3.up * (SewerFloor / 2f + 0.45f), Vector3.one, walls[3], true, "pitWall");
            w.transform.rotation = Quaternion.LookRotation(down);
            w.transform.localScale = new Vector3(0.2f, depth + 0.9f, Cell);
        }
        GameObject back = Art.Box(root, p + down * (Cell / 2f - 0.1f) + Vector3.up * ((SewerCeil + 0.9f) / 2f), Vector3.one, walls[3], true, "pitWall");
        back.transform.rotation = Quaternion.LookRotation(down);
        back.transform.localScale = new Vector3(Cell, 0.9f - SewerCeil, 0.2f);
        // Grade de ferro em cima da mureta (so visual) e placa amarela.
        Material rust = Rust();
        for (int s2 = -1; s2 <= 1; s2 += 2)
        {
            Vector3 b0 = p + side * (s2 * (Cell / 2f - 0.1f)) + Vector3.up * 1.15f;
            GameObject bar = Art.Box(root, b0, Vector3.one, rust, false, "grate");
            bar.transform.rotation = Quaternion.LookRotation(down);
            bar.transform.localScale = new Vector3(0.06f, 0.06f, Cell);
        }
        Art.Box(root, p - down * (Cell / 2f) + side * (Cell / 2f - 0.1f) + Vector3.up * 1.4f, new Vector3(0.5f, 0.35f, 0.04f), Art.Glow(Art.Yellow, 0.6f), false, "sign");
        Art.Box(root, p + Vector3.up * (SewerFloor - 0.05f), new Vector3(Cell, 0.1f, Cell), paving, true, "pitFloor");
    }

    // Uma celula de tunel: piso com canaleta, teto, paredes onde nao ha tunel vizinho, canos e lampadas.
    static int sewerLamps;
    static void SewerCell(Transform root, int x, int y, Vector3 p)
    {
        float hgt = SewerCeil - SewerFloor;
        Material wallM = walls[3];
        Art.Box(root, p + Vector3.up * (SewerFloor - 0.15f), new Vector3(Cell, 0.3f, Cell), stone, true, "sewerFloor");
        Art.Box(root, p + Vector3.up * (SewerCeil + 0.1f), new Vector3(Cell, 0.2f, Cell), dark, true, "sewerCeil");
        // Canaleta de agua escura no meio (na direcao do tunel).
        bool alongX = Sewer(x - 1, y) || Sewer(x + 1, y);
        Material water = Art.Mat(new Color(0.06f, 0.08f, 0.06f), Color.black, 0f, default(Color), 0.95f, 0f);
        Art.Box(root, p + Vector3.up * (SewerFloor + 0.01f), alongX ? new Vector3(Cell, 0.02f, 0.7f) : new Vector3(0.7f, 0.02f, Cell), water, false, "water");
        Material rust = Rust();
        for (int d = 0; d < 4; d++)
        {
            if (Sewer(x + DX[d], y + DY[d])) continue;
            Vector3 n = new Vector3(DX[d], 0f, DY[d]);
            Vector3 along = new Vector3(-n.z, 0f, n.x);
            GameObject w = Art.Box(root, p + n * (Cell / 2f - 0.1f) + Vector3.up * (SewerFloor + hgt / 2f), Vector3.one, wallM, true, "sewerWall");
            w.transform.rotation = Quaternion.LookRotation(n);
            w.transform.localScale = new Vector3(Cell, hgt, 0.2f);
            // Canos correndo pela parede (o "encanamento").
            GameObject pipe = Art.Prim(PrimitiveType.Cylinder, root, p + n * (Cell / 2f - 0.35f) + Vector3.up * (SewerCeil - 0.45f), new Vector3(0.36f, Cell / 2f, 0.36f), rust, false, "pipe");
            pipe.transform.rotation = Quaternion.FromToRotation(Vector3.up, along);
            GameObject pipe2 = Art.Prim(PrimitiveType.Cylinder, root, p + n * (Cell / 2f - 0.3f) + Vector3.up * (SewerFloor + 0.55f), new Vector3(0.22f, Cell / 2f, 0.22f), stone, false, "pipe");
            pipe2.transform.rotation = Quaternion.FromToRotation(Vector3.up, along);
        }
        SewerProps(root, x, y, p);

        // Lampada no teto a cada 3 celulas (luz amarelada fraca).
        if ((x + y) % 3 == 0)
        {
            Art.Box(root, p + Vector3.up * (SewerCeil - 0.05f), new Vector3(0.6f, 0.08f, 0.25f), Art.Glow(new Color(1f, 0.8f, 0.45f), 1.4f), false, "lamp");
            if (sewerLamps++ % 2 == 0) Art.PointLight(root, p + Vector3.up * (SewerCeil - 0.4f), new Color(1f, 0.78f, 0.45f), 10f, 1.6f);
        }
    }

    // Objetos do esgoto encostados nas paredes (cobertura sem fechar a passagem) e itens de vida/municao.
    static void SewerProps(Transform root, int x, int y, Vector3 p)
    {
        Vector3 fl = p + Vector3.up * SewerFloor;
        int mid = (H - 1) / 2;
        // Itens: vida no meio de cada metade do tunel principal, municao no centro.
        if (y == mid && (x == 6 || x == W - 7 || x == (W - 1) / 2))
        {
            GameObject pk = new GameObject("pickup");
            pk.transform.SetParent(root, false);
            pk.transform.position = fl;
            pk.AddComponent<Pickup>().kind = x == (W - 1) / 2 ? 1 : 0;
            return;
        }
        // Parede onde encostar (so celulas com parede de algum lado).
        // Sorteia entre as paredes da celula (direita/esquerda variam ao longo do tunel).
        int[] cand = new int[4]; int nc = 0;
        for (int d = 0; d < 4; d++) if (!Sewer(x + DX[d], y + DY[d])) cand[nc++] = d;
        if (nc == 0) return;
        int wd = cand[(int)(Hash(x * 5 + 1, y * 3 + 9) * nc * 0.999f)];
        Vector3 n = new Vector3(DX[wd], 0f, DY[wd]), side = new Vector3(-n.z, 0f, n.x);
        float k = Hash(x * 3 + 17, y * 5 + 3);
        Vector3 at = fl + n * (Cell / 2f - 0.85f) + side * ((k - 0.5f) * 1.6f);
        Material rust = Rust();
        if (k < 0.5f)
        {
            // Pilha de caixotes
            GameObject a = Art.Box(root, at + Vector3.up * 0.6f, new Vector3(1.2f, 1.2f, 1.2f), wood, true, "crate");
            a.transform.rotation = Quaternion.Euler(0f, k * 90f, 0f);
            if (k < 0.25f) { GameObject b = Art.Box(root, at + Vector3.up * 1.6f + side * 0.1f, new Vector3(0.8f, 0.8f, 0.8f), wood, true, "crate"); b.transform.rotation = Quaternion.Euler(0f, k * 300f, 0f); }
            Art.Box(root, at + side * 1.1f + Vector3.up * 0.4f, new Vector3(0.8f, 0.8f, 0.8f), wood, true, "crate");
        }
        else if (k < 0.7f)
        {
            // Barris enferrujados
            Art.Prim(PrimitiveType.Cylinder, root, at + Vector3.up * 0.55f, new Vector3(0.7f, 0.55f, 0.7f), rust, true, "barrel");
            Art.Prim(PrimitiveType.Cylinder, root, at + side * 0.8f + Vector3.up * 0.55f, new Vector3(0.7f, 0.55f, 0.7f), rust, true, "barrel");
            if (k > 0.6f) { GameObject t = Art.Prim(PrimitiveType.Cylinder, root, at - side * 0.9f + Vector3.up * 0.35f, new Vector3(0.7f, 0.55f, 0.7f), rust, true, "barrel"); t.transform.rotation = Quaternion.FromToRotation(Vector3.up, side); }
        }
        else if (k < 0.88f)
        {
            // Palete com sacos
            Art.Box(root, at + Vector3.up * 0.08f, new Vector3(1.4f, 0.16f, 1.2f), wood, true, "pallet");
            Material sack = Art.Mat(new Color(0.55f, 0.47f, 0.33f), Color.black, 0f, default(Color), 0.1f, 0f);
            for (int i = 0; i < 3; i++) Art.Box(root, at + Vector3.up * (0.33f + (i == 2 ? 0.32f : 0f)) + side * (i == 1 ? 0.35f : (i == 0 ? -0.35f : 0f)), new Vector3(0.65f, 0.32f, 1f), sack, true, "sack");
        }
        else
        {
            // Registro de agua com valvula vermelha na parede
            Vector3 wall = fl + n * (Cell / 2f - 0.3f) + Vector3.up * 1.2f;
            GameObject v = Art.Prim(PrimitiveType.Cylinder, root, wall, new Vector3(0.3f, 0.6f, 0.3f), rust, false, "valvePipe");
            Art.Prim(PrimitiveType.Cylinder, root, wall - n * 0.2f + Vector3.up * 0.1f, new Vector3(0.45f, 0.03f, 0.45f), Art.Mat(new Color(0.6f, 0.08f, 0.06f), Color.black), false, "valve").transform.rotation = Quaternion.FromToRotation(Vector3.up, -n);
        }
    }

    static GameObject Piece(Transform root, Vector3 pos, Quaternion rot, Vector3 size, Material m, string name)
    {
        GameObject g = Art.Box(root, pos, size, m, false, name);
        g.transform.rotation = rot;
        return g;
    }

    static void Crates(Transform root, Vector3 p, float hsh)
    {
        GameObject a = Art.Box(root, p + new Vector3(-0.5f, 0.75f, 0.2f), new Vector3(1.5f, 1.5f, 1.5f), wood, true, "crate");
        a.transform.rotation = Quaternion.Euler(0f, hsh * 40f - 20f, 0f);
        GameObject b = Art.Box(root, p + new Vector3(1.05f, 0.55f, -0.5f), new Vector3(1.1f, 1.1f, 1.1f), wood, true, "crate");
        b.transform.rotation = Quaternion.Euler(0f, hsh * 90f, 0f);
        if (hsh > 0.45f)
        {
            GameObject c = Art.Box(root, p + new Vector3(-0.45f, 1.95f, 0.2f), new Vector3(0.9f, 0.9f, 0.9f), wood, true, "crate");
            c.transform.rotation = Quaternion.Euler(0f, hsh * 200f, 0f);
        }
        // Colisor cheio da celula para os bots (que tratam 'x' como bloqueada) nao enroscarem.
        GameObject blk = new GameObject("crateBlock");
        blk.transform.SetParent(root, false);
        blk.transform.position = p + Vector3.up * 0.6f;
        BoxCollider bc = blk.AddComponent<BoxCollider>();
        bc.size = new Vector3(2.9f, 1.2f, 2.4f);
    }

    static void Barrels(Transform root, Vector3 p, float hsh)
    {
        Material rust = Art.Pbr("Metal046B", new Color(1.1f, 0.6f, 0.4f), 1.5f, 0.6f, 0f, 0f);
        Art.Prim(PrimitiveType.Cylinder, root, p + Vector3.up * 0.55f, new Vector3(0.7f, 0.55f, 0.7f), rust, true, "barrel");
        if (hsh > 0.96f) Art.Prim(PrimitiveType.Cylinder, root, p + new Vector3(0.75f, 0.55f, 0.15f), new Vector3(0.7f, 0.55f, 0.7f), rust, true, "barrel");
    }

    // Portal em arco entre dois muros.
    static void Arch(Transform root, int x, int y, Vector3 p)
    {
        bool alongX = !Walk(x - 1, y) && !Walk(x + 1, y);   // muros a esquerda e a direita: passagem no eixo Z
        Vector3 across = alongX ? Vector3.right : Vector3.forward;
        Vector3 depth = alongX ? Vector3.forward : Vector3.right;
        Material m = walls[0];
        Vector3 sz = alongX ? new Vector3(0.7f, 3.4f, 1.4f) : new Vector3(1.4f, 3.4f, 0.7f);
        Art.Box(root, p + across * 1.65f + Vector3.up * 1.7f, sz, m, true, "jamb");
        Art.Box(root, p - across * 1.65f + Vector3.up * 1.7f, sz, m, true, "jamb");
        Vector3 top = alongX ? new Vector3(Cell, 1.6f, 1.4f) : new Vector3(1.4f, 1.6f, Cell);
        Art.Box(root, p + Vector3.up * 4.2f, top, m, true, "lintel");
        for (int i = -1; i <= 1; i += 2)
        {
            GameObject k = Art.Box(root, p + across * (i * 1.1f) + Vector3.up * 3.25f, alongX ? new Vector3(0.9f, 0.5f, 1.4f) : new Vector3(1.4f, 0.5f, 0.9f), m, false, "haunch");
            k.transform.rotation = Quaternion.AngleAxis(i * (alongX ? -35f : 35f), depth);
        }
        Art.Box(root, p + Vector3.up * 5.1f, alongX ? new Vector3(Cell + 0.2f, 0.25f, 1.7f) : new Vector3(1.7f, 0.25f, Cell + 0.2f), stone, false, "cap");
        Art.Box(root, p + Vector3.up * -0.04f, new Vector3(Cell, 0.1f, Cell), paving, false, "paving");
    }

    // Passagem coberta: laje no alto, vigas de madeira e piso calcado (sombra fresca no meio do sol).
    static void Tunnel(Transform root, int x, int y, Vector3 p)
    {
        Art.Box(root, p + Vector3.up * 4.1f, new Vector3(Cell, 0.5f, Cell), walls[1], true, "roof");
        bool alongZ = Walk(x, y - 1) || Walk(x, y + 1);
        for (int i = -1; i <= 1; i += 2)
            Art.Box(root, p + Vector3.up * 3.72f + (alongZ ? Vector3.forward : Vector3.right) * (i * 1.2f), alongZ ? new Vector3(Cell, 0.26f, 0.22f) : new Vector3(0.22f, 0.26f, Cell), wood, false, "beam");
        for (int d = 0; d < 4; d++)
        {
            if (Walk(x + DX[d], y + DY[d]) && map[x + DX[d], y + DY[d]] != 'T')
            {
                Vector3 n = new Vector3(DX[d], 0f, DY[d]);
                Vector3 side = new Vector3(-n.z, 0f, n.x);
                Art.Box(root, p + n * 1.85f + side * 1.8f + Vector3.up * 1.9f, new Vector3(0.4f, 3.8f, 0.4f), stone, true, "post");
                Art.Box(root, p + n * 1.85f - side * 1.8f + Vector3.up * 1.9f, new Vector3(0.4f, 3.8f, 0.4f), stone, true, "post");
            }
        }
        Art.Box(root, p + Vector3.up * -0.04f, new Vector3(Cell, 0.1f, Cell), paving, false, "paving");
    }

    // +10 palmeiras espalhadas (5 numa metade, espelhadas na outra).
    static readonly int[,] extraPalms = { { 23, 8 }, { 7, 8 }, { 17, 4 }, { 2, 6 }, { 3, 9 } };
    static bool ExtraPalm(int x, int y)
    {
        for (int i = 0; i < extraPalms.GetLength(0); i++)
        {
            int px = extraPalms[i, 0], py = extraPalms[i, 1];
            if ((x == px && y == py) || (x == W - 1 - px && y == H - 1 - py)) return true;
        }
        return false;
    }

    static void Palm(Transform root, Vector3 p, float hsh)
    {
        Material trunk = Art.Pbr("Planks037A", new Color(0.95f, 0.8f, 0.6f), 0.8f, 0.8f, 0f, 0f);
        Material leaf = Art.Mat(new Color(0.16f, 0.3f, 0.1f), Color.black, 0f, default(Color), 0.25f, 0f);
        float h = 5.5f + hsh * 2.5f;
        Quaternion lean = Quaternion.Euler(hsh * 10f - 5f, hsh * 360f, 6f);
        GameObject t = Art.Prim(PrimitiveType.Cylinder, root, p + lean * Vector3.up * (h / 2f), new Vector3(0.32f, h / 2f, 0.32f), trunk, true, "palm");
        t.transform.rotation = lean;
        Vector3 top = p + lean * Vector3.up * h;
        for (int i = 0; i < 9; i++)
        {
            Quaternion r = Quaternion.Euler(0f, i * 40f + hsh * 50f, 0f) * Quaternion.Euler(0f, 0f, -22f - (i % 3) * 14f);
            GameObject l = Art.Box(root, top + r * new Vector3(1.5f, 0f, 0f), new Vector3(3f, 0.04f, 0.55f), leaf, false, "leaf");
            l.transform.rotation = r;
        }
    }

    // Em volta da arena: vila que continua, torres e dunas ao longe.
    static void Surroundings(Transform root, float sizeX, float sizeZ)
    {
        System.Random r = new System.Random(77);
        float half = Mathf.Max(sizeX, sizeZ) / 2f;
        for (int i = 0; i < 70; i++)
        {
            float ang = (float)r.NextDouble() * Mathf.PI * 2f;
            float dist = half + 10f + (float)r.NextDouble() * 70f;
            float w = 8f + (float)r.NextDouble() * 12f, d = 8f + (float)r.NextDouble() * 12f, h = 5f + (float)r.NextDouble() * 9f;
            Vector3 p = new Vector3(Mathf.Cos(ang) * dist, 0f, Mathf.Sin(ang) * dist);
            if (Mathf.Abs(p.x) < sizeX / 2f + 4f && Mathf.Abs(p.z) < sizeZ / 2f + 4f) continue;
            Material m = walls[r.Next(walls.Length)];
            Art.Box(root, p + Vector3.up * (h / 2f), new Vector3(w, h, d), m, false, "farHouse");
            if (r.NextDouble() < 0.3) Art.Prim(PrimitiveType.Sphere, root, p + Vector3.up * h, new Vector3(w * 0.55f, w * 0.4f, w * 0.55f), walls[2], false, "dome");
            if (r.NextDouble() < 0.12)
            {
                Art.Box(root, p + new Vector3(w * 0.3f, h + 6f, 0f), new Vector3(3f, 12f + h, 3f), walls[1], false, "tower");
                Art.Prim(PrimitiveType.Sphere, root, p + new Vector3(w * 0.3f, h * 1.5f + 12.5f, 0f), new Vector3(3.6f, 3.6f, 3.6f), walls[2], false, "towerDome");
            }
        }
        for (int i = 0; i < 16; i++)
        {
            float ang = i / 16f * Mathf.PI * 2f + (float)r.NextDouble() * 0.3f;
            float dist = half + 150f + (float)r.NextDouble() * 120f;
            float s = 120f + (float)r.NextDouble() * 140f;
            Art.Prim(PrimitiveType.Sphere, root, new Vector3(Mathf.Cos(ang) * dist, -6f, Mathf.Sin(ang) * dist), new Vector3(s, 22f + (float)r.NextDouble() * 26f, s * 0.8f), sand, false, "dune");
        }
    }

    // ------------------------------ NO GELO ------------------------------

    // Bloco macico (bunker/muro de gelo). Teto plano e firme: as escadas 'S' levam ate ele.
    static void IceBlock(Transform root, int x, int y, Vector3 p)
    {
        bool edge = IsEdge(x, y);
        float g = Hash(x / 2 + 31, y / 2 + 7);
        float h = edge ? 8f + g * 2.5f : (NextToStairs(x, y) || g < 0.6f ? 4.6f : 5.9f);
        Material m = walls[(int)(Hash(x / 2 + 5, y / 2 + 11) * 3.99f)];
        Art.Box(root, p + Vector3.up * (h / 2f), new Vector3(Cell, h, Cell), m, true, "block");
        // Laje do teto com neve por cima (pisavel).
        Art.Box(root, p + Vector3.up * (h + 0.12f), new Vector3(Cell + 0.3f, 0.3f, Cell + 0.3f), stone, true, "parapet");
        Art.Box(root, p + Vector3.up * (h + 0.29f), new Vector3(Cell + 0.1f, 0.06f, Cell + 0.1f), sand, false, "roofSnow");
        if (edge) return;
        Material ice = IceMat();
        for (int d = 0; d < 4; d++)
        {
            int nx = x + DX[d], ny = y + DY[d];
            if (!Walk(nx, ny) || map[nx, ny] == 'S' || map[nx, ny] == 'G') continue;
            Vector3 n = new Vector3(DX[d], 0f, DY[d]);
            Vector3 side = new Vector3(-n.z, 0f, n.x);
            Vector3 face = p + n * (Cell / 2f);
            Quaternion rot = Quaternion.LookRotation(n);
            float k = Hash(x * 7 + d, y * 13 + d);
            if (k < 0.3f)
            {
                // Faixa de luz fria na parede (ajuda a enxergar nos corredores fechados).
                Piece(root, face + Vector3.up * 2.6f + n * 0.03f, rot, new Vector3(2.6f, 0.08f, 0.05f), Art.Glow(new Color(0.45f, 0.85f, 1f), 1.1f), "lightStrip");
            }
            else if (k < 0.55f)
            {
                // Porta de servico de metal fechada, com placa.
                Piece(root, face + Vector3.up * 1.15f + n * 0.04f, rot, new Vector3(1.3f, 2.3f, 0.1f), walls[1], "hatch");
                Piece(root, face + Vector3.up * 2.55f + n * 0.06f, rot, new Vector3(0.5f, 0.2f, 0.04f), Art.Glow(Art.Yellow, 0.5f), "sign");
            }
            else if (k < 0.75f)
            {
                // Pingentes de gelo na beirada.
                for (int i = 0; i < 5; i++)
                {
                    float len = 0.25f + Hash(x * 3 + i, y * 5 + d) * 0.55f;
                    Piece(root, face + n * 0.08f + side * ((i - 2) * 0.7f) + Vector3.up * (4.45f - len / 2f), rot, new Vector3(0.09f, len, 0.09f), ice, "icicle");
                }
            }
            // Gelo acumulado no pe da parede.
            if (k > 0.4f) Piece(root, face + n * 0.3f + Vector3.up * 0.12f, rot, new Vector3(Cell * 0.85f, 0.24f, 0.6f), sand, "drift");
        }
    }

    static Material IceMat() { return Art.Mat(new Color(0.62f, 0.82f, 0.95f), new Color(0.05f, 0.14f, 0.2f), 0f, default(Color), 0.95f, 0.05f); }

    // Coluna de gelo (no lugar da fonte do MERCADO): cristais grandes com luz azulada.
    static void IcePillar(Transform root, Vector3 p, float hsh)
    {
        Material ice = IceMat();
        Art.Box(root, p + Vector3.up * 0.2f, new Vector3(3.2f, 0.4f, 3.2f), stone, true, "pillarBase");
        for (int i = 0; i < 4; i++)
        {
            float h = 3.2f + Hash(i, (int)(hsh * 100f)) * 2.6f;
            Vector3 off = new Vector3((i % 2 == 0 ? -1f : 1f) * 0.45f, 0f, (i < 2 ? -1f : 1f) * 0.45f) * (i == 0 ? 0.2f : 1f);
            GameObject c = Art.Box(root, p + off + Vector3.up * (h / 2f + 0.3f), new Vector3(0.9f, h, 0.9f), ice, true, "crystal");
            c.transform.rotation = Quaternion.Euler((i - 1.5f) * 6f, hsh * 90f + i * 37f, (i % 2 == 0 ? 5f : -7f));
        }
        Art.PointLight(root, p + Vector3.up * 2.5f, new Color(0.5f, 0.85f, 1f), 9f, 1.4f);
    }

    static void SnowPile(Transform root, Vector3 p, float hsh)
    {
        Vector3 off = new Vector3((hsh - 0.9f) * 12f, 0f, (0.5f - hsh) * 6f);
        Art.Prim(PrimitiveType.Sphere, root, p + off + Vector3.up * 0.05f, new Vector3(1.6f + hsh, 0.5f, 1.2f + hsh * 0.5f), sand, false, "snowPile");
    }

    // Em volta: outras construcoes da base e montanhas nevadas ao longe.
    static void IceSurroundings(Transform root, float sizeX, float sizeZ)
    {
        System.Random r = new System.Random(91);
        float half = Mathf.Max(sizeX, sizeZ) / 2f;
        for (int i = 0; i < 45; i++)
        {
            float ang = (float)r.NextDouble() * Mathf.PI * 2f;
            float dist = half + 12f + (float)r.NextDouble() * 70f;
            float w = 8f + (float)r.NextDouble() * 14f, d = 8f + (float)r.NextDouble() * 14f, h = 5f + (float)r.NextDouble() * 10f;
            Vector3 p = new Vector3(Mathf.Cos(ang) * dist, 0f, Mathf.Sin(ang) * dist);
            if (Mathf.Abs(p.x) < sizeX / 2f + 4f && Mathf.Abs(p.z) < sizeZ / 2f + 4f) continue;
            Art.Box(root, p + Vector3.up * (h / 2f), new Vector3(w, h, d), walls[r.Next(walls.Length)], false, "farBlock");
            Art.Box(root, p + Vector3.up * (h + 0.2f), new Vector3(w + 0.4f, 0.4f, d + 0.4f), sand, false, "farSnow");
            if (r.NextDouble() < 0.2) Art.Box(root, p + new Vector3(0f, h + 5f, 0f), new Vector3(0.4f, 10f, 0.4f), walls[1], false, "antenna");
        }
        Material rock = Art.Mat(new Color(0.22f, 0.25f, 0.3f), Color.black, 0f, default(Color), 0.2f, 0f);
        for (int i = 0; i < 18; i++)
        {
            float ang = i / 18f * Mathf.PI * 2f + (float)r.NextDouble() * 0.3f;
            float dist = half + 150f + (float)r.NextDouble() * 120f;
            float s = 110f + (float)r.NextDouble() * 150f, hh = 60f + (float)r.NextDouble() * 70f;
            Vector3 c = new Vector3(Mathf.Cos(ang) * dist, -8f, Mathf.Sin(ang) * dist);
            Art.Prim(PrimitiveType.Sphere, root, c, new Vector3(s, hh, s * 0.85f), rock, false, "mountain");
            Art.Prim(PrimitiveType.Sphere, root, c + Vector3.up * hh * 0.18f, new Vector3(s * 0.7f, hh * 0.75f, s * 0.6f), sand, false, "mountainSnow");
        }
    }

    static void BuildMinimap()
    {
        minimap = new Texture2D(W, H, TextureFormat.RGBA32, false);
        minimap.filterMode = FilterMode.Point;
        minimap.wrapMode = TextureWrapMode.Clamp;
        for (int y = 0; y < H; y++)
        {
            for (int x = 0; x < W; x++)
            {
                char c = map[x, y];
                Color col = Ice ? new Color(0.06f, 0.08f, 0.12f, 0.8f) : new Color(0.1f, 0.08f, 0.05f, 0.8f);
                if (c == '#' || c == 'o') col = Ice ? new Color(0.55f, 0.66f, 0.8f, 0.92f) : new Color(0.62f, 0.52f, 0.36f, 0.92f);
                else if (c == 'G') col = new Color(1f, 0.6f, 0.1f, 0.95f);
                else if (c == 'x') col = new Color(0.45f, 0.28f, 0.12f, 0.9f);
                else if (c == 'J') col = new Color(0.9f, 0.8f, 0.2f, 0.9f);
                else if (c == 'T' || c == 'd') col = Ice ? new Color(0.16f, 0.2f, 0.27f, 0.85f) : new Color(0.22f, 0.18f, 0.12f, 0.85f);
                else if (c == 'U') col = new Color(0.2f, 0.55f, 0.75f, 0.95f);
                else if (c == 'S') col = new Color(0.85f, 0.85f, 0.85f, 0.9f);
                else if (c == '~') col = new Color(0.2f, 0.6f, 0.9f, 0.95f);
                minimap.SetPixel(x, y, col);
            }
        }
        minimap.Apply();
    }
}
