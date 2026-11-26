using System.Collections.Generic;
using UnityEngine;

// Mapa A CASA (3x3 / 4x4, estilo "pool day"): lote de 40 x 35 m numa rua de bairro.
// Casa em losango (quadrado de 14 m girado 45 graus) com sala, cozinha, corredor, lavabo, 3 quartos e suite;
// piscina e quiosque na ponta esquerda; garagem coberta para 4 carros na frente.
// Paredes da casa e da garagem abrem buracos com tiros e explosoes (Breakable).
// A malha dos bots tem celulas de 0,5 m e e medida pela fisica (cabe uma capsula de pessoa?).
public static partial class Arena
{
    public const int CasaId = 2;
    public static bool Casa { get { return mapId == CasaId; } }

    static readonly Vector3 CasaC = new Vector3(5f, 0f, 4.5f);
    const float WallH = 2.8f;
    // Piscina (retangulo no mundo)
    const float PoolX0 = -18f, PoolX1 = -13f, PoolZ0 = -4f, PoolZ1 = 6f, PoolDepth = 1.4f;

    // Local da casa (u, v) -> mundo.
    static Vector3 CW(float u, float v)
    {
        const float s = 0.70710678f;
        return new Vector3(CasaC.x + (u - v) * s, 0f, CasaC.z + (u + v) * s);
    }

    // Paredes da casa: u0, v0, u1, v1 e os vaos {inicio, fim, base, topo} ao longo da parede.
    // (mesma planta que foi conferida no prototipo: todo comodo chega no corredor pelas portas internas)
    static readonly float[][] CasaWalls =
    {
        new[] { -7.1f, -7f, 7.1f, -7f,   2.1f, 3.7f, 0f, 2.2f,   5.2f, 7.2f, 0.9f, 2.1f,   10.2f, 12.6f, 0.9f, 2.1f },
        new[] { 7f, -7.1f, 7f, 7.1f,     0.9f, 2.1f, 0.9f, 2.1f,   2.6f, 4.2f, 0f, 2.2f,   6.6f, 7.6f, 1.5f, 2.1f,   8.6f, 10.2f, 0f, 2.2f,   12.2f, 13.2f, 1.5f, 2.1f },
        new[] { -7.1f, 7f, 7.1f, 7f,     1.1f, 2.7f, 0.9f, 2.1f,   4.6f, 6.2f, 0.9f, 2.1f,   8.1f, 9.7f, 0.9f, 2.1f,   12.1f, 13.1f, 1.5f, 2.1f },
        new[] { -7f, -7.1f, -7f, 7.1f,   1.6f, 4.2f, 0f, 2.2f,   6.3f, 7.9f, 0f, 2.2f,   10.1f, 11.7f, 0.9f, 2.1f },
        new[] { -7f, -1f, 7f, -1f,       1.5f, 5.5f, 0f, 2.45f,   9.4f, 11f, 0f, 2.2f },
        new[] { -7f, 1f, 7f, 1f,         0.9f, 2.5f, 0f, 2.2f,   4.5f, 6.1f, 0f, 2.2f,   7.9f, 9.5f, 0f, 2.2f,   10.6f, 12f, 0f, 2.2f },
        new[] { 1f, -7f, 1f, -1f,        1.5f, 4f, 0f, 2.45f },
        new[] { 5f, -1f, 5f, 1f,         0.25f, 1.75f, 0f, 2.2f },
        new[] { -3.5f, 1f, -3.5f, 7f },
        new[] { 0f, 1f, 0f, 7f },
        new[] { 3.5f, 1f, 3.5f, 7f },
        new[] { 3.5f, 4.5f, 7f, 4.5f,    1.4f, 3f, 0f, 2.2f },
    };

    static Material grass, asphalt, plaster, plasterIn, tile, poolTile, water, roofMat, woodFloor;

    static void BuildCasa(Transform root)
    {
        Cell = 0.5f;
        W = 80; H = 70;
        map = new char[W, H];
        prev = new int[W * H];
        queue = new int[W * H];
        sewer = new bool[W, H];
        Breakable.All.Clear();

        grass = Art.Pbr("Ground054", new Color(0.5f, 0.72f, 0.36f), 4f, 0.8f, 0f, 1f, 0.8f);
        asphalt = Art.Pbr("Concrete034", new Color(0.32f, 0.32f, 0.34f), 3f, 0.9f, 0f, 1f);
        plaster = Art.Pbr("Plaster001", new Color(1f, 0.97f, 0.9f), 3f, 1f, 0f, 1f);
        plasterIn = Art.Pbr("Plaster001", new Color(0.95f, 0.9f, 0.82f), 3f, 1f, 0f, 0f);
        tile = Art.Pbr("PavingStones070", new Color(1f, 0.95f, 0.88f), 2f, 1f, 0f, 1f);
        poolTile = Art.Pbr("Plastic006", new Color(0.55f, 0.85f, 0.95f), 0.6f, 1.2f, 0f, 0f);
        water = Art.Mat(new Color(0.08f, 0.42f, 0.62f), new Color(0.01f, 0.06f, 0.09f), 0f, default(Color), 0.97f, 0.1f);
        roofMat = Art.Pbr("Concrete034", new Color(0.62f, 0.6f, 0.58f), 3f, 0.8f, 0f, 1f);
        woodFloor = Art.Pbr("Planks037A", new Color(1.1f, 0.88f, 0.66f), 1.6f, 1f, 0f, 0f);

        CasaGround(root);
        CasaLot(root);
        CasaGarage(root);
        CasaHouse(root);
        CasaPool(root);
        CasaKiosk(root);
        CasaYard(root);

        // Spawns: BAAL na rua, MOLOCH no quintal dos fundos (lado direito da casa).
        float[] ax = { -12f, -6f, 0f, 6f, 12f };
        foreach (float x in ax) spawnsA.Add(new Vector3(x, 0f, -15.6f));
        spawnsB.Add(new Vector3(14f, 0f, 10f)); spawnsB.Add(new Vector3(17.5f, 0f, 10f)); spawnsB.Add(new Vector3(14f, 0f, 14.5f));
        spawnsB.Add(new Vector3(17.5f, 0f, 14.5f)); spawnsB.Add(new Vector3(17.5f, 0f, 6.5f));

        Physics.SyncTransforms();
        RecheckAll();
        walkable.Clear();
        for (int y = 0; y < H; y++) for (int x = 0; x < W; x++) if (Walk(x, y)) walkable.Add(Center(x, y));
    }

    // ------------------------------ Malha dos bots ------------------------------

    static readonly Collider[] probe = new Collider[16];

    static bool CellBlocked(int x, int y)
    {
        if (x == 0 || y == 0 || x == W - 1 || y == H - 1) return true;
        Vector3 c = Center(x, y);
        if (c.x > PoolX0 - 0.1f && c.x < PoolX1 + 0.1f && c.z > PoolZ0 - 0.1f && c.z < PoolZ1 + 0.1f) return true;
        int n = Physics.OverlapCapsuleNonAlloc(c + Vector3.up * 0.35f, c + Vector3.up * 1.6f, 0.25f, probe, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
        {
            Collider col = probe[i];
            if (col == null || col is CharacterController) continue;
            if (col.GetComponentInParent<Actor>() != null || col.GetComponentInParent<Vehicle>() != null) continue;
            return true;
        }
        return false;
    }

    static void SetCell(int x, int y)
    {
        Vector3 c = Center(x, y);
        bool pool = c.x > PoolX0 - 0.1f && c.x < PoolX1 + 0.1f && c.z > PoolZ0 - 0.1f && c.z < PoolZ1 + 0.1f && x > 0 && y > 0 && x < W - 1 && y < H - 1;
        map[x, y] = pool ? '~' : (CellBlocked(x, y) ? '#' : '.');
    }

    public static void RecheckAll()
    {
        if (!Casa || map == null) return;
        for (int y = 0; y < H; y++) for (int x = 0; x < W; x++) SetCell(x, y);
    }

    // Uma placa de parede caiu: refaz as celulas em volta (um buraco grande vira passagem para os bots).
    public static void Recheck(Vector3 p, float r)
    {
        if (!Casa || map == null) return;
        int x0, y0, x1, y1;
        ToCell(p - new Vector3(r, 0f, r), out x0, out y0);
        ToCell(p + new Vector3(r, 0f, r), out x1, out y1);
        for (int y = Mathf.Max(0, y0); y <= Mathf.Min(H - 1, y1); y++)
            for (int x = Mathf.Max(0, x0); x <= Mathf.Min(W - 1, x1); x++) SetCell(x, y);
    }

    // ------------------------------ Pecas ------------------------------

    // Placa de chao (so visual) do retangulo x0..x1, z0..z1, com o topo na altura top.
    static void Slab(Transform root, float x0, float z0, float x1, float z1, float top, Material m, string name = "piso")
    {
        if (x1 - x0 < 0.01f || z1 - z0 < 0.01f) return;
        Art.Box(root, new Vector3((x0 + x1) / 2f, top - 0.05f, (z0 + z1) / 2f), new Vector3(x1 - x0, 0.1f, z1 - z0), m, false, name);
    }

    static GameObject Solid(Transform root, Vector3 c, Vector3 size, Material m, string name = "solido")
    {
        return Art.Box(root, c, size, m, true, name);
    }

    static GameObject Rot(GameObject g, float yaw) { g.transform.rotation = Quaternion.Euler(0f, yaw, 0f); return g; }

    // Chao: grama no lote, asfalto na rua, calcada; colisor em tudo menos no buraco da piscina.
    static void CasaGround(Transform root)
    {
        float hx = W * Cell / 2f, hz = H * Cell / 2f, big = 200f;
        Material far = Art.Pbr("Ground054", new Color(0.45f, 0.62f, 0.32f), 6f, 0.8f, 0f, 1f, 0.8f);
        Slab(root, -hx - big, -hz - big, hx + big, -hz, 0f, far, "longe");
        Slab(root, -hx - big, hz, hx + big, hz + big, 0f, far, "longe");
        Slab(root, -hx - big, -hz, -hx, hz, 0f, far, "longe");
        Slab(root, hx, -hz, hx + big, hz, 0f, far, "longe");
        // Grama (com o buraco da piscina), rua e calcada
        Slab(root, -hx, -12f, hx, PoolZ0, 0f, grass, "grama");
        Slab(root, -hx, PoolZ1, hx, hz, 0f, grass, "grama");
        Slab(root, -hx, PoolZ0, PoolX0, PoolZ1, 0f, grass, "grama");
        Slab(root, PoolX1, PoolZ0, hx, PoolZ1, 0f, grass, "grama");
        Slab(root, -hx, -hz, hx, -13.4f, 0f, asphalt, "rua");
        Slab(root, -hx, -13.4f, hx, -12f, 0.03f, tile, "calcada");
        for (float x = -18f; x < 20f; x += 6f) Slab(root, x, -15.65f, x + 3f, -15.45f, 0.012f, Art.Mat(new Color(0.9f, 0.85f, 0.6f), Color.black, 0f, default(Color), 0.3f, 0f), "faixa");
        // Colisores do chao (o da piscina fica no fundo dela)
        GroundCollider(root, new Vector3(0f, 0f, (-hz - 20f + PoolZ0) / 2f), new Vector3(2f * hx + 40f, 1f, PoolZ0 + hz + 20f));
        GroundCollider(root, new Vector3(0f, 0f, (PoolZ1 + hz + 20f) / 2f), new Vector3(2f * hx + 40f, 1f, hz + 20f - PoolZ1));
        GroundCollider(root, new Vector3((-hx - 20f + PoolX0) / 2f, 0f, (PoolZ0 + PoolZ1) / 2f), new Vector3(PoolX0 + hx + 20f, 1f, PoolZ1 - PoolZ0));
        GroundCollider(root, new Vector3((PoolX1 + hx + 20f) / 2f, 0f, (PoolZ0 + PoolZ1) / 2f), new Vector3(hx + 20f - PoolX1, 1f, PoolZ1 - PoolZ0));
    }

    // Muros do lote, muro baixo na frente (com o portao da garagem e o portao social) e as casas do outro lado da rua.
    static void CasaLot(Transform root)
    {
        float hx = W * Cell / 2f, hz = H * Cell / 2f;
        Material muro = Art.Pbr("Plaster001", new Color(0.92f, 0.86f, 0.74f), 3f, 1f, 0f, 1f);
        Material cap = Art.Pbr("Concrete034", new Color(0.75f, 0.72f, 0.68f), 2f, 1f, 0f, 1f);
        Solid(root, new Vector3(-hx + 0.15f, 1.3f, (-12f + hz) / 2f), new Vector3(0.3f, 2.6f, hz + 12f), muro, "muro");
        Solid(root, new Vector3(hx - 0.15f, 1.3f, (-12f + hz) / 2f), new Vector3(0.3f, 2.6f, hz + 12f), muro, "muro");
        Solid(root, new Vector3(0f, 1.3f, hz - 0.15f), new Vector3(2f * hx, 2.6f, 0.3f), muro, "muro");
        float[] seg = { -20f, -16.5f, -14.5f, -7f, -5f, -1f, 11f, 20f };
        for (int i = 0; i < seg.Length; i += 2)
        {
            Solid(root, new Vector3((seg[i] + seg[i + 1]) / 2f, 0.55f, -12f), new Vector3(seg[i + 1] - seg[i], 1.1f, 0.22f), muro, "muro baixo");
            Art.Box(root, new Vector3((seg[i] + seg[i + 1]) / 2f, 1.13f, -12f), new Vector3(seg[i + 1] - seg[i] + 0.04f, 0.06f, 0.3f), cap, false, "rufo");
            foreach (float x in new[] { seg[i], seg[i + 1] })
                if (Mathf.Abs(x) < 19.9f) Solid(root, new Vector3(x, 0.7f, -12f), new Vector3(0.35f, 1.4f, 0.35f), cap, "pilar");
        }
        // Rua sem saida dos lados e casas do outro lado (fecham o mapa)
        Solid(root, new Vector3(-hx + 0.15f, 1.3f, (-hz - 12f) / 2f), new Vector3(0.3f, 2.6f, hz - 12f), muro, "muro");
        Solid(root, new Vector3(hx - 0.15f, 1.3f, (-hz - 12f) / 2f), new Vector3(0.3f, 2.6f, hz - 12f), muro, "muro");
        for (int i = 0; i < 4; i++)
        {
            float x = -15f + i * 10f, h = 5.5f + (i % 2) * 1.6f;
            Material f = walls[i % walls.Length];
            Solid(root, new Vector3(x, h / 2f, -hz - 4.5f), new Vector3(9.4f, h, 8f), f, "vizinho");
            Art.Box(root, new Vector3(x, h + 0.15f, -hz - 4.5f), new Vector3(9.8f, 0.3f, 8.4f), roofMat, false, "telhado");
            Solid(root, new Vector3(x, 0.6f, -hz - 0.25f), new Vector3(9.4f, 1.2f, 0.25f), muro, "muro vizinho");
        }
        Solid(root, new Vector3(0f, 1.5f, -hz - 0.6f), new Vector3(2f * hx, 3f, 0.2f), Art.Mat(new Color(0f, 0f, 0f, 0f), Color.black), "limite").GetComponent<Renderer>().enabled = false;
    }

    // Garagem coberta, aberta para a rua e para a casa, com 4 carros (cobertura e paredes laterais que quebram).
    static void CasaGarage(Transform root)
    {
        Slab(root, -1f, -12f, 11f, -5.4f, 0.03f, Art.Pbr("Concrete034", new Color(0.82f, 0.8f, 0.76f), 2.5f, 1f, 0f, 1f), "piso garagem");
        Breakable.Create(root, new Vector3(-1f, 0f, -11.6f), new Vector3(-1f, 0f, -5.4f), 2.7f, 0.2f, plaster, new Color(0.9f, 0.88f, 0.84f), new[] { 2.2f, 3.8f, 0.9f, 2.1f });
        Breakable.Create(root, new Vector3(11f, 0f, -5.4f), new Vector3(11f, 0f, -11.6f), 2.7f, 0.2f, plaster, new Color(0.9f, 0.88f, 0.84f), null);
        Solid(root, new Vector3(5f, 2.82f, -8.5f), new Vector3(12.6f, 0.24f, 6.6f), roofMat, "cobertura");
        Color[] cor = { new Color(0.75f, 0.08f, 0.06f), new Color(0.9f, 0.9f, 0.92f), new Color(0.08f, 0.1f, 0.14f), new Color(0.15f, 0.3f, 0.6f) };
        float[] xs = { 0.5f, 3.5f, 6.5f, 9.5f };
        for (int i = 0; i < 4; i++) Car(root, new Vector3(xs[i], 0f, -8.5f), i % 2 == 0 ? 0f : 180f, cor[i], i);
        Art.PointLight(root, new Vector3(5f, 2.5f, -8.5f), new Color(1f, 0.9f, 0.75f), 8f, 0.6f);
    }

    // Carros da garagem: modelos 3D (Resources/Cars/car0..car3.glb, esportivos originais gerados no Higgsfield).
    // O modelo e girado para o comprimento ficar no eixo z, escalado para 4,5 m e apoiado no chao; a colisao e uma
    // caixa invisivel do tamanho do carro. Sem o arquivo, usa o carro de blocos.
    static void Car(Transform root, Vector3 p, float yaw, Color c, int model)
    {
        Transform t = new GameObject("carro").transform;
        t.SetParent(root, false);
        t.position = p;
        t.rotation = Quaternion.Euler(0f, yaw, 0f);
        GameObject g = CarModel(t, model);
        if (g == null) { Object.Destroy(t.gameObject); BoxCar(root, p, yaw, c); return; }
        Bounds b = LocalBounds(t, g);
        BoxCollider box = t.gameObject.AddComponent<BoxCollider>();
        box.center = new Vector3(0f, b.size.y * 0.42f, 0f);
        box.size = new Vector3(Mathf.Min(b.size.x, 2.05f), b.size.y * 0.84f, b.size.z * 0.98f);
    }

    // Modelo 3D de um dos 4 esportivos, filho de t: frente para +z, 4,5 m de comprimento, rodas no chao.
    // Os GLBs tem o comprimento no eixo x com a frente no -x do arquivo (conferido nas previas).
    public static GameObject CarModel(Transform t, int model)
    {
        GameObject prefab = Resources.Load<GameObject>("Cars/car" + (model % 4));
        if (prefab == null) return null;
        GameObject g = Object.Instantiate(prefab, t, false);
        g.name = "modelo";
        foreach (Collider col in g.GetComponentsInChildren<Collider>()) Object.Destroy(col);
        foreach (Animator an in g.GetComponentsInChildren<Animator>()) an.enabled = false;
        Bounds b = LocalBounds(t, g);
        if (b.size.x > b.size.z) { g.transform.localRotation = Quaternion.Euler(0f, CarFrontYaw, 0f) * g.transform.localRotation; b = LocalBounds(t, g); }
        float k = 4.5f / Mathf.Max(0.01f, Mathf.Max(b.size.x, b.size.z));
        g.transform.localScale *= k;
        b = LocalBounds(t, g);
        g.transform.localPosition -= new Vector3(b.center.x, b.min.y, b.center.z);
        return g;
    }
    // Giro que leva a frente do modelo para +z (se o carro aparecer de re na corrida, troque para 90).
    public const float CarFrontYaw = -90f;

    public static void PalmAt(Transform root, Vector3 p, float seed) { Palm(root, p, seed); }

    // Caixa que envolve o modelo, nas coordenadas de t.
    static Bounds LocalBounds(Transform t, GameObject g)
    {
        Bounds b = new Bounds(Vector3.zero, Vector3.zero);
        bool first = true;
        foreach (MeshFilter mf in g.GetComponentsInChildren<MeshFilter>())
        {
            if (mf.sharedMesh == null) continue;
            Bounds mb = mf.sharedMesh.bounds;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                Vector3 lp = t.InverseTransformPoint(mf.transform.TransformPoint(corner));
                if (first) { b = new Bounds(lp, Vector3.zero); first = false; } else b.Encapsulate(lp);
            }
        }
        foreach (SkinnedMeshRenderer sm in g.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            Bounds wb = sm.bounds;
            Vector3 lp = t.InverseTransformPoint(wb.center);
            if (first) { b = new Bounds(lp, wb.size); first = false; } else b.Encapsulate(new Bounds(lp, wb.size));
        }
        return b;
    }

    static void BoxCar(Transform root, Vector3 p, float yaw, Color c)
    {
        Transform t = new GameObject("carro").transform;
        t.SetParent(root, false);
        t.position = p;
        t.rotation = Quaternion.Euler(0f, yaw, 0f);
        Material paint = Art.Mat(c, Color.black, 0f, default(Color), 0.85f, 0.45f);
        Material glass = Art.Mat(new Color(0.05f, 0.07f, 0.09f), Color.black, 0f, default(Color), 0.95f, 0.2f);
        Material tire = Art.Mat(new Color(0.04f, 0.04f, 0.04f), Color.black, 0f, default(Color), 0.2f, 0f);
        Art.Box(t, new Vector3(0f, 0.62f, 0f), new Vector3(1.85f, 0.7f, 4.4f), paint, true, "lataria");
        Art.Box(t, new Vector3(0f, 1.22f, -0.25f), new Vector3(1.6f, 0.55f, 2.3f), paint, true, "cabine");
        Art.Box(t, new Vector3(0f, 1.22f, -0.25f), new Vector3(1.64f, 0.45f, 2.1f), glass, false, "vidros");
        Art.Box(t, new Vector3(0f, 0.75f, 2.21f), new Vector3(1.5f, 0.15f, 0.04f), Art.Glow(new Color(1f, 0.95f, 0.8f), 0.6f), false, "farol");
        Art.Box(t, new Vector3(0f, 0.8f, -2.21f), new Vector3(1.5f, 0.12f, 0.04f), Art.Glow(new Color(1f, 0.1f, 0.05f), 0.8f), false, "lanterna");
        for (int i = 0; i < 4; i++)
        {
            GameObject w = Art.Prim(PrimitiveType.Cylinder, t, new Vector3(i < 2 ? -0.86f : 0.86f, 0.34f, i % 2 == 0 ? 1.4f : -1.4f), new Vector3(0.68f, 0.12f, 0.68f), tire, false, "roda");
            w.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
        }
    }

    // A casa em losango.
    static void CasaHouse(Transform root)
    {
        Transform h = new GameObject("casa").transform;
        h.SetParent(root, false);
        // Piso de madeira e calcada em volta
        Rot(Art.Box(h, CasaC + new Vector3(0f, -0.03f, 0f), new Vector3(16f, 0.1f, 16f), tile, false, "calcada casa"), 45f);
        Rot(Art.Box(h, CasaC + new Vector3(0f, 0.02f, 0f), new Vector3(14f, 0.04f, 14f), woodFloor, false, "piso"), 45f);
        Color dust = new Color(0.95f, 0.92f, 0.86f);
        for (int i = 0; i < CasaWalls.Length; i++)
        {
            float[] w = CasaWalls[i];
            float[] op = new float[w.Length - 4];
            System.Array.Copy(w, 4, op, 0, op.Length);
            bool outside = i < 4;
            Breakable.Create(h, CW(w[0], w[1]), CW(w[2], w[3]), WallH, outside ? 0.22f : 0.16f, outside ? plaster : plasterIn, dust, op);
        }
        // Laje (com beiral) e platibanda
        Rot(Solid(h, CasaC + new Vector3(0f, WallH + 0.12f, 0f), new Vector3(15f, 0.24f, 15f), roofMat, "laje"), 45f);
        // Luzes de teto (poucas: o celular aguenta pouca luz por pixel)
        Material lamp = Art.Glow(new Color(1f, 0.92f, 0.78f), 0.9f);
        float[][] lights = { new[] { -3f, -4f }, new[] { 4f, -4f }, new[] { -1f, 0f }, new[] { -1.7f, 4f }, new[] { 4f, 3f } };
        foreach (float[] l in lights)
        {
            Vector3 p = CW(l[0], l[1]);
            Art.Box(h, p + Vector3.up * (WallH - 0.02f), new Vector3(0.5f, 0.03f, 0.5f), lamp, false, "luminaria");
        }
        Art.PointLight(h, CW(-3f, -4f) + Vector3.up * 2.4f, new Color(1f, 0.9f, 0.75f), 9f, 0.7f);
        Art.PointLight(h, CW(0f, 3.5f) + Vector3.up * 2.4f, new Color(1f, 0.9f, 0.75f), 9f, 0.6f);
        CasaFurniture(h);
    }

    // Moveis (cobertura dentro da casa). Medidas locais da casa: u, v, largura (u), fundo (v), altura.
    static void Furn(Transform h, float u, float v, float su, float sv, float y0, float hgt, Material m, bool col = true, string name = "movel")
    {
        Vector3 p = CW(u, v) + Vector3.up * (y0 + hgt / 2f);
        GameObject g = Art.Box(h, p, new Vector3(su, hgt, sv), m, col, name);
        g.transform.rotation = Quaternion.Euler(0f, -45f, 0f);
    }

    static void CasaFurniture(Transform h)
    {
        Material white = Art.Mat(new Color(0.92f, 0.92f, 0.9f), Color.black, 0f, default(Color), 0.3f, 0f);
        Material fabric = Art.Mat(new Color(0.25f, 0.32f, 0.42f), Color.black, 0f, default(Color), 0.15f, 0f);
        Material wood = Art.Pbr("Planks037A", new Color(0.75f, 0.55f, 0.38f), 1.2f, 1f, 0f, 0f);
        Material stoneTop = Art.Mat(new Color(0.18f, 0.18f, 0.2f), Color.black, 0f, default(Color), 0.8f, 0.1f);
        Material dark = Art.Mat(new Color(0.06f, 0.06f, 0.07f), Color.black, 0f, default(Color), 0.6f, 0.2f);
        // Camas (quartos 1, 2, 3 e suite)
        float[][] beds = { new[] { -5.5f, 5.5f, 1.4f, 2f }, new[] { -1.8f, 5.5f, 1.4f, 2f }, new[] { 1.8f, 5.5f, 1.4f, 2f }, new[] { 5.6f, 2.1f, 1.8f, 1.4f } };
        foreach (float[] b in beds)
        {
            Furn(h, b[0], b[1], b[2], b[3], 0f, 0.35f, wood, true, "cama");
            Furn(h, b[0], b[1], b[2] - 0.1f, b[3] - 0.1f, 0.35f, 0.2f, white, false, "colchao");
        }
        // Guarda-roupas
        Furn(h, -5.25f, 1.4f, 2.6f, 0.6f, 0f, 2.1f, wood, true, "armario");
        Furn(h, 1.75f, 1.4f, 2.4f, 0.55f, 0f, 2.1f, wood, true, "armario");
        // Sala: sofa, rack com TV e mesa de jantar
        Furn(h, -4.5f, -6.3f, 3f, 0.9f, 0f, 0.45f, fabric, true, "sofa");
        Furn(h, -4.5f, -6.65f, 3f, 0.25f, 0.45f, 0.4f, fabric, false, "encosto");
        Furn(h, -4.5f, -2.0f, 2.4f, 0.4f, 0f, 0.55f, wood, true, "rack");
        Furn(h, -4.5f, -1.88f, 1.6f, 0.06f, 0.75f, 0.9f, dark, false, "tv");
        Furn(h, -0.8f, -4.6f, 1.0f, 1.8f, 0f, 0.75f, wood, true, "mesa");
        // Cozinha: bancada no fundo e ilha
        Furn(h, 4f, -6.55f, 5f, 0.7f, 0f, 0.92f, white, true, "bancada");
        Furn(h, 4f, -6.55f, 5.04f, 0.74f, 0.92f, 0.04f, stoneTop, false, "pedra");
        Furn(h, 4.2f, -3.6f, 2.2f, 1f, 0f, 0.92f, white, true, "ilha");
        Furn(h, 4.2f, -3.6f, 2.24f, 1.04f, 0.92f, 0.04f, stoneTop, false, "pedra");
        Furn(h, 6.55f, -1.6f, 0.7f, 0.7f, 0f, 1.85f, Art.Mat(new Color(0.8f, 0.8f, 0.82f), Color.black, 0f, default(Color), 0.8f, 0.6f), true, "geladeira");
        // Banheiros: vaso e pia
        float[][] baths = { new[] { 6.3f, 0.5f }, new[] { 6.4f, 6.4f } };
        foreach (float[] b in baths)
        {
            Furn(h, b[0], b[1], 0.4f, 0.6f, 0f, 0.42f, white, true, "vaso");
            Furn(h, b[0] - 0.9f, b[1] + 0.1f, 0.6f, 0.45f, 0.6f, 0.25f, white, false, "pia");
        }
    }

    // Piscina 5 x 10 m: azulejo, agua, escada de degraus no lado do quiosque e deck em volta.
    static void CasaPool(Transform root)
    {
        float cx = (PoolX0 + PoolX1) / 2f, cz = (PoolZ0 + PoolZ1) / 2f, w = PoolX1 - PoolX0, l = PoolZ1 - PoolZ0;
        // Deck (borda) em volta
        Slab(root, PoolX0 - 1.5f, PoolZ0 - 1.5f, PoolX1 + 1.5f, PoolZ0, 0.03f, tile, "deck");
        Slab(root, PoolX0 - 1.5f, PoolZ1, PoolX1 + 1.5f, PoolZ1 + 1.5f, 0.03f, tile, "deck");
        Slab(root, PoolX0 - 1.5f, PoolZ0, PoolX0, PoolZ1, 0.03f, tile, "deck");
        Slab(root, PoolX1, PoolZ0, PoolX1 + 1.5f, PoolZ1, 0.03f, tile, "deck");
        // Paredes e fundo (com colisor)
        Solid(root, new Vector3(cx, -PoolDepth / 2f, PoolZ0 - 0.15f), new Vector3(w + 0.6f, PoolDepth, 0.3f), poolTile, "borda");
        Solid(root, new Vector3(cx, -PoolDepth / 2f, PoolZ1 + 0.15f), new Vector3(w + 0.6f, PoolDepth, 0.3f), poolTile, "borda");
        Solid(root, new Vector3(PoolX0 - 0.15f, -PoolDepth / 2f, cz), new Vector3(0.3f, PoolDepth, l), poolTile, "borda");
        Solid(root, new Vector3(PoolX1 + 0.15f, -PoolDepth / 2f, cz), new Vector3(0.3f, PoolDepth, l), poolTile, "borda");
        Solid(root, new Vector3(cx, -PoolDepth - 0.15f, cz), new Vector3(w, 0.3f, l), poolTile, "fundo");
        // Degraus para sair (0,35 m cada), encostados no lado norte
        for (int i = 0; i < 3; i++)
        {
            float top = -0.35f * (i + 1), z1 = PoolZ1 - i * 0.6f;
            Solid(root, new Vector3(cx, (top - PoolDepth) / 2f, z1 - 0.3f), new Vector3(w, top + PoolDepth, 0.6f), poolTile, "degrau");
        }
        // Agua (sem colisor: da para pular dentro)
        Art.Box(root, new Vector3(cx, -0.28f, cz), new Vector3(w, 0.02f, l), water, false, "agua");
        Art.PointLight(root, new Vector3(cx, -0.9f, cz), new Color(0.3f, 0.75f, 1f), 7f, 0.5f);
        // Espreguicadeiras
        Material lounger = Art.Mat(new Color(0.95f, 0.95f, 0.95f), Color.black, 0f, default(Color), 0.4f, 0f);
        for (int i = 0; i < 3; i++) Solid(root, new Vector3(PoolX1 + 0.85f + 0.1f, 0.22f, -2.5f + i * 2.6f), new Vector3(0.7f, 0.35f, 1.9f), lounger, "espreguicadeira");
    }

    // Quiosque com churrasqueira, bancada e mesa (canto de tras, na ponta esquerda).
    static void CasaKiosk(Transform root)
    {
        float x0 = -19.4f, x1 = -12.6f, z0 = 8.6f, z1 = 14.2f;
        Slab(root, x0, z0, x1, z1, 0.05f, tile, "piso quiosque");
        Material wood = Art.Pbr("Planks037A", new Color(0.85f, 0.62f, 0.42f), 1.2f, 1f, 0f, 1f);
        Material brick = Art.Pbr("Bricks084", new Color(0.95f, 0.72f, 0.58f), 2f, 1f, 0f, 1f);
        Material straw = Art.Pbr("Planks037A", new Color(0.62f, 0.46f, 0.28f), 0.8f, 0.6f, 0f, 1f);
        foreach (float x in new[] { x0 + 0.3f, x1 - 0.3f })
            foreach (float z in new[] { z0 + 0.3f, z1 - 0.3f })
                Solid(root, new Vector3(x, 1.4f, z), new Vector3(0.25f, 2.8f, 0.25f), wood, "pilar");
        // Telhado de duas aguas
        float cxk = (x0 + x1) / 2f, czk = (z0 + z1) / 2f, half = (z1 - z0) / 2f + 0.5f;
        for (int s = -1; s <= 1; s += 2)
        {
            GameObject r = Art.Box(root, new Vector3(cxk, 3.25f, czk + s * half / 2f), new Vector3(x1 - x0 + 1f, 0.12f, half * 1.08f), straw, true, "telhado");
            r.transform.rotation = Quaternion.Euler(s * 22f, 0f, 0f);
        }
        // Bancada com churrasqueira (cobertura forte) e mesa
        Solid(root, new Vector3(cxk, 0.5f, z1 - 0.6f), new Vector3(x1 - x0 - 1.2f, 1f, 0.7f), brick, "bancada");
        Solid(root, new Vector3(x0 + 1.1f, 1.2f, z1 - 0.6f), new Vector3(1.2f, 2.4f, 0.8f), brick, "churrasqueira");
        Art.Box(root, new Vector3(x0 + 1.1f, 2.6f, z1 - 0.6f), new Vector3(0.5f, 0.6f, 0.5f), brick, false, "chamine");
        Solid(root, new Vector3(cxk + 0.5f, 0.38f, czk - 0.6f), new Vector3(2.2f, 0.76f, 1f), wood, "mesa");
        Art.PointLight(root, new Vector3(cxk, 2.6f, czk), new Color(1f, 0.85f, 0.6f), 7f, 0.6f);
    }

#if UNITY_EDITOR
    // So no editor: F8 leva o jogador por um passeio nos comodos (para conferir luz e passagens).
    static int tour;
    static readonly float[][] Tour =
    {
        new[] { -3f, -4f, 0f }, new[] { 4f, -2.5f, 180f }, new[] { -2f, 0f, 90f }, new[] { -5.2f, 3.5f, 0f },
        new[] { -1.7f, 3.5f, 0f }, new[] { 1.7f, 3.5f, 0f }, new[] { 4.5f, 3.2f, 0f }, new[] { 5.2f, 5.9f, 0f }, new[] { 6f, 0f, -90f },
    };
    public static void CasaTour(Actor p)
    {
        float[] t = Tour[tour++ % Tour.Length];
        // yaw local da casa -> mundo (u aponta para +x+z, ou seja 45 graus)
        p.Respawn(CW(t[0], t[1]), 45f + t[2]);
        Game.I.Banner("PASSEIO " + tour + " / " + Tour.Length);
    }
#endif

    // Quintal: coqueiros, vasos e a casinha de gas.
    static void CasaYard(Transform root)
    {
        Palm(root, new Vector3(-18.5f, 0f, -9.5f), 0.3f);
        Palm(root, new Vector3(18.3f, 0f, -9.8f), 0.7f);
        Palm(root, new Vector3(-9f, 0f, 15.8f), 0.5f);
        Palm(root, new Vector3(18.4f, 0f, 2.5f), 0.9f);
        Palm(root, new Vector3(-10.5f, 0f, -7.5f), 0.15f);
        Material pot = Art.Pbr("Concrete034", new Color(0.6f, 0.4f, 0.3f), 1f, 1f, 0f, 1f);
        Solid(root, new Vector3(-8.5f, 0.45f, 8.8f), new Vector3(1.2f, 0.9f, 1.2f), pot, "floreira");
        Solid(root, new Vector3(12.5f, 0.45f, -2f), new Vector3(1.2f, 0.9f, 1.2f), pot, "floreira");
        Solid(root, new Vector3(16.8f, 0.7f, -3.5f), new Vector3(2.2f, 1.4f, 1.2f), Art.Pbr("Bricks084", new Color(0.9f, 0.85f, 0.8f), 2f, 1f, 0f, 1f), "abrigo do gas");
        Solid(root, new Vector3(1f, 0.5f, 16.5f), new Vector3(3f, 1f, 1.2f), Art.Pbr("Planks037A", new Color(0.7f, 0.5f, 0.35f), 1.2f, 1f, 0f, 1f), "caixotes");
    }
}
