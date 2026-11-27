using System.Collections.Generic;
using UnityEngine;

// Efeitos visuais curtos: tracantes, faiscas, clarões e explosoes.
public class Fx : MonoBehaviour
{
    public float life = 0.2f;
    public Vector3 startScale = Vector3.one;
    public Vector3 endScale = Vector3.one;
    public Light lamp;
    float age;
    float lamp0;

    void Start()
    {
        if (lamp != null) lamp0 = lamp.intensity;
        transform.localScale = startScale;
    }

    void Update()
    {
        age += Time.deltaTime;
        float k = Mathf.Clamp01(age / life);
        transform.localScale = Vector3.Lerp(startScale, endScale, k);
        if (lamp != null) lamp.intensity = lamp0 * (1f - k);
        if (k >= 1f) Destroy(gameObject);
    }

    static Fx Blob(Vector3 pos, Color c, float s0, float s1, float life, Transform parent = null)
    {
        GameObject g = Art.Prim(PrimitiveType.Sphere, null, pos, Vector3.one * s0, Art.Glow(c, 2.2f), false, "fx");
        if (parent != null) g.transform.SetParent(parent, true);
        Fx f = g.AddComponent<Fx>();
        f.life = life;
        f.startScale = Vector3.one * s0;
        f.endScale = Vector3.one * s1;
        return f;
    }

    public static void Tracer(Vector3 a, Vector3 b, Color c, float width = 0.035f, float life = 0.07f)
    {
        GameObject g = new GameObject("tracer");
        LineRenderer lr = g.AddComponent<LineRenderer>();
        lr.sharedMaterial = Art.Unlit(c);
        lr.positionCount = 2;
        lr.SetPosition(0, a);
        lr.SetPosition(1, b);
        lr.startWidth = width;
        lr.endWidth = width;
        lr.startColor = c;
        lr.endColor = c;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;
        Destroy(g, life);
    }

    // Descarga eletrica entre a e b: raio em zigue-zague que treme, nucleo branco fino + brilho colorido, e some rapido.
    static Material arcMat;
    public static void Arc(Vector3 a, Vector3 b, Color c, float life = 0.16f)
    {
        if (arcMat == null)
        {
            // Textura de perfil macio na largura: centro forte, bordas somem (o raio nao fica "chapado").
            Texture2D t = new Texture2D(4, 32, TextureFormat.RGBA32, false);
            t.wrapMode = TextureWrapMode.Clamp;
            for (int y = 0; y < 32; y++)
            {
                float v = 1f - Mathf.Abs((y + 0.5f) / 32f * 2f - 1f);
                float al = Mathf.Pow(v, 1.8f);
                for (int x = 0; x < 4; x++) t.SetPixel(x, y, new Color(1f, 1f, 1f, al));
            }
            t.Apply();
            arcMat = new Material(Shader.Find("Sprites/Default"));
            arcMat.mainTexture = t;
        }
        GameObject g = new GameObject("arc");
        ArcFx f = g.AddComponent<ArcFx>();
        f.Init(a, b, c, life, arcMat, true);
        // Galhinho saindo do meio de vez em quando.
        if (Random.value < 0.45f)
        {
            Vector3 m = Vector3.Lerp(a, b, Random.Range(0.3f, 0.8f));
            Vector3 e = m + (Random.onUnitSphere * 0.6f + (b - a).normalized * 0.4f) * Random.Range(0.4f, 0.9f);
            GameObject g2 = new GameObject("arc");
            g2.AddComponent<ArcFx>().Init(m, e, c, life * 0.7f, arcMat, false);
        }
    }

    public static void Spark(Vector3 point, Vector3 normal, Color c)
    {
        Blob(point + normal * 0.03f, c, 0.16f, 0.02f, 0.18f);
    }

    public static void Flash(Transform muzzle, Color c)
    {
        if (muzzle == null) return;
        Fx f = Blob(muzzle.position, c, 0.14f, 0.03f, 0.06f, muzzle);
        f.lamp = Art.PointLight(f.transform, Vector3.zero, c, 6f, 2.5f);
    }

    public static void Explosion(Vector3 pos, Color c, float radius)
    {
        Fx f = Blob(pos, c, 0.4f, radius * 1.6f, 0.28f);
        f.lamp = Art.PointLight(f.transform, Vector3.zero, c, radius * 4f, 5f);
        Blob(pos, Color.white, 0.2f, radius * 0.7f, 0.16f);
    }

    // ---------- Explosao de verdade: clarao, bola de fogo, fumaca, fagulhas e marca no chao ----------

    static Texture2D puffTex;
    static Material fireMat, smokeMat, sparkMat, scorchMat;

    static void BlastAssets()
    {
        if (puffTex != null) return;
        const int S = 64;
        puffTex = new Texture2D(S, S, TextureFormat.RGBA32, true);
        Color[] px = new Color[S * S];
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float dx = (x + 0.5f) / S * 2f - 1f, dy = (y + 0.5f) / S * 2f - 1f;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                float n = Mathf.PerlinNoise(x * 0.13f, y * 0.13f) * 0.5f + 0.5f;
                float a = Mathf.Clamp01(1f - d);
                px[y * S + x] = new Color(1f, 1f, 1f, a * a * n);
            }
        puffTex.SetPixels(px);
        puffTex.Apply();
        Shader sh = Shader.Find("Sprites/Default");
        fireMat = new Material(sh); fireMat.mainTexture = puffTex; fireMat.color = new Color(5f, 2.2f, 0.5f, 1f);
        smokeMat = new Material(sh); smokeMat.mainTexture = puffTex; smokeMat.color = new Color(0.16f, 0.14f, 0.13f, 0.9f);
        sparkMat = new Material(sh); sparkMat.mainTexture = puffTex; sparkMat.color = new Color(6f, 3.5f, 1f, 1f);
        scorchMat = new Material(sh); scorchMat.mainTexture = puffTex; scorchMat.color = new Color(0.02f, 0.02f, 0.02f, 0.85f);
    }

    public static float Scale = 1f;   // celular: menos particulas

    static ParticleSystem Burst(Vector3 pos, Material m, int count, float lifeMin, float lifeMax, float speedMin, float speedMax, float sizeMin, float sizeMax, float gravity, float grow, float shapeRadius)
    {
        count = Mathf.Max(3, Mathf.RoundToInt(count * Scale));
        GameObject g = new GameObject("blastFx");
        g.transform.position = pos;
        ParticleSystem ps = g.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        ParticleSystem.MainModule main = ps.main;
        main.loop = false;
        main.duration = 1f;
        main.playOnAwake = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(lifeMin, lifeMax);
        main.startSpeed = new ParticleSystem.MinMaxCurve(speedMin, speedMax);
        main.startSize = new ParticleSystem.MinMaxCurve(sizeMin, sizeMax);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, 6.28f);
        main.gravityModifier = gravity;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 200;
        ParticleSystem.EmissionModule em = ps.emission;
        em.rateOverTime = 0f;
        em.SetBursts(new ParticleSystem.Burst[] { new ParticleSystem.Burst(0f, (short)count) });
        ParticleSystem.ShapeModule shp = ps.shape;
        shp.shapeType = ParticleSystemShapeType.Sphere;
        shp.radius = shapeRadius;
        ParticleSystem.ColorOverLifetimeModule col = ps.colorOverLifetime;
        col.enabled = true;
        Gradient gr = new Gradient();
        gr.SetKeys(new GradientColorKey[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                   new GradientAlphaKey[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.08f), new GradientAlphaKey(0.7f, 0.5f), new GradientAlphaKey(0f, 1f) });
        col.color = new ParticleSystem.MinMaxGradient(gr);
        ParticleSystem.SizeOverLifetimeModule sz = ps.sizeOverLifetime;
        sz.enabled = true;
        sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, grow));
        ParticleSystemRenderer r = g.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = m;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        ps.Play();
        Destroy(g, lifeMax + 1.5f);
        return ps;
    }

    public static void Blast(Vector3 pos, float radius)
    {
        BlastAssets();
        float r = Mathf.Max(1f, radius);
        Burst(pos, fireMat, 26, 0.25f, 0.55f, 1.5f, r * 2.2f, r * 0.35f, r * 0.75f, -0.1f, 1.8f, r * 0.12f);
        Burst(pos + Vector3.up * 0.3f, smokeMat, 22, 1.4f, 2.6f, 0.6f, r * 0.9f, r * 0.45f, r * 0.9f, -0.06f, 2.6f, r * 0.2f);
        Burst(pos, sparkMat, 36, 0.4f, 0.9f, r * 1.5f, r * 4.5f, 0.05f, 0.14f, 1.2f, 0.3f, 0.1f);
        Fx f = Blob(pos, new Color(1f, 0.85f, 0.55f), 0.3f, r * 1.1f, 0.1f);
        f.lamp = Art.PointLight(f.transform, Vector3.zero, new Color(1f, 0.6f, 0.25f), r * 5f, 9f);

        // Marca de queimado no chao (se houver chao por perto).
        RaycastHit hit;
        if (Physics.Raycast(pos + Vector3.up * 0.3f, Vector3.down, out hit, 2.5f, ~0, QueryTriggerInteraction.Ignore))
        {
            GameObject q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Destroy(q.GetComponent<Collider>());
            q.name = "scorch";
            q.transform.position = hit.point + hit.normal * 0.02f;
            q.transform.rotation = Quaternion.LookRotation(-hit.normal) * Quaternion.Euler(0f, 0f, Random.value * 360f);
            q.transform.localScale = Vector3.one * r * 1.1f;
            Renderer qr = q.GetComponent<Renderer>();
            qr.sharedMaterial = scorchMat;
            qr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            Destroy(q, 25f);
        }
    }

    // ---------- Sangue: jato no acerto, mancha na parede atras, gotas e poca no chao, respingo na tela ----------

    static Material bloodMat, mistMat;
    static Material[] splatMats;
    static Texture2D[] splatTex;
    static Mesh quadMesh;
    static readonly Queue<GameObject> decals = new Queue<GameObject>();
    static readonly RaycastHit[] rayBuf = new RaycastHit[12];
    static int bloodFrame, bloodCount;
    static float fleshAt;

    static void BloodAssets()
    {
        if (splatMats != null && splatMats[0] != null) return;
        BlastAssets();
        Shader sh = Shader.Find("Sprites/Default");
        bloodMat = new Material(sh); bloodMat.mainTexture = puffTex; bloodMat.color = new Color(0.5f, 0.01f, 0.01f, 1f);
        mistMat = new Material(sh); mistMat.mainTexture = puffTex; mistMat.color = new Color(0.42f, 0.02f, 0.02f, 0.55f);
        splatTex = new Texture2D[3];
        splatMats = new Material[3];
        for (int i = 0; i < 3; i++)
        {
            splatTex[i] = SplatTexture(31 + i * 17);
            splatMats[i] = new Material(sh);
            splatMats[i].mainTexture = splatTex[i];
            splatMats[i].color = new Color(0.36f, 0.008f, 0.008f, 0.93f);
        }
        quadMesh = new Mesh();
        quadMesh.vertices = new Vector3[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f), new Vector3(0.5f, 0.5f, 0f) };
        quadMesh.uv = new Vector2[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) };
        quadMesh.triangles = new int[] { 0, 2, 1, 2, 3, 1 };
        quadMesh.RecalculateNormals();
        quadMesh.RecalculateBounds();
    }

    // Mancha de respingo desenhada por codigo: bolha central irregular + gotas e filetes em volta.
    static Texture2D SplatTexture(int seed)
    {
        const int S = 128;
        System.Random r = new System.Random(seed);
        float[] a = new float[S * S];
        float p1 = (float)r.NextDouble() * 6.28f, p2 = (float)r.NextDouble() * 6.28f, p3 = (float)r.NextDouble() * 6.28f;
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float dx = (x + 0.5f) / S * 2f - 1f, dy = (y + 0.5f) / S * 2f - 1f;
                float d = Mathf.Sqrt(dx * dx + dy * dy), th = Mathf.Atan2(dy, dx);
                float rad = 0.3f * (1f + 0.28f * Mathf.Sin(3f * th + p1) + 0.2f * Mathf.Sin(5f * th + p2) + 0.13f * Mathf.Sin(9f * th + p3));
                a[y * S + x] = Mathf.Clamp01((rad - d) / 0.05f);
            }
        int drops = 16 + r.Next(8);
        for (int k = 0; k < drops; k++)
        {
            float th = (float)r.NextDouble() * 6.28f, dist = 0.3f + (float)r.NextDouble() * 0.6f;
            float rad = 0.018f + (float)r.NextDouble() * 0.05f;
            bool streak = r.NextDouble() < 0.45;
            int steps = streak ? 7 : 1;
            for (int q = 0; q < steps; q++)
            {
                float dd = dist - q * rad * 0.9f, rr = rad * (1f - q / (float)(steps + 1));
                if (dd < 0.2f) break;
                float cx = Mathf.Cos(th) * dd, cy = Mathf.Sin(th) * dd;
                int x0 = Mathf.Max(0, (int)((cx - rr + 1f) * 0.5f * S) - 1), x1 = Mathf.Min(S - 1, (int)((cx + rr + 1f) * 0.5f * S) + 1);
                int y0 = Mathf.Max(0, (int)((cy - rr + 1f) * 0.5f * S) - 1), y1 = Mathf.Min(S - 1, (int)((cy + rr + 1f) * 0.5f * S) + 1);
                for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++)
                    {
                        float dx = (x + 0.5f) / S * 2f - 1f - cx, dy = (y + 0.5f) / S * 2f - 1f - cy;
                        float v = Mathf.Clamp01((rr - Mathf.Sqrt(dx * dx + dy * dy)) / 0.02f);
                        if (v > a[y * S + x]) a[y * S + x] = v;
                    }
            }
        }
        Color[] px = new Color[S * S];
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float n = 0.65f + 0.35f * Mathf.PerlinNoise(x * 0.09f + seed, y * 0.09f);
                px[y * S + x] = new Color(n, n, n, a[y * S + x]);
            }
        Texture2D t = new Texture2D(S, S, TextureFormat.RGBA32, true);
        t.wrapMode = TextureWrapMode.Clamp;
        t.SetPixels(px);
        t.Apply();
        return t;
    }

    public static void Warm() { BloodAssets(); }

    // Primeira superficie do cenario no caminho (ignora atores e veiculos).
    static bool WorldRay(Vector3 from, Vector3 dir, float dist, out RaycastHit best)
    {
        best = new RaycastHit();
        int n = Physics.RaycastNonAlloc(from, dir, rayBuf, dist, ~0, QueryTriggerInteraction.Ignore);
        float bd = float.MaxValue;
        bool found = false;
        for (int i = 0; i < n; i++)
        {
            Collider c = rayBuf[i].collider;
            if (c == null || rayBuf[i].distance >= bd) continue;
            if (c.GetComponentInParent<Actor>() != null || c.GetComponentInParent<Vehicle>() != null) continue;
            if (c is CharacterController) continue;
            bd = rayBuf[i].distance; best = rayBuf[i]; found = true;
        }
        return found;
    }

    static void Decal(Vector3 p, Vector3 n, float size, float life, float grow)
    {
        GameObject q = new GameObject("blood");
        q.AddComponent<MeshFilter>().sharedMesh = quadMesh;
        MeshRenderer r = q.AddComponent<MeshRenderer>();
        r.sharedMaterial = splatMats[Random.Range(0, splatMats.Length)];
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        q.transform.position = p + n * (0.012f + Random.value * 0.012f);
        q.transform.rotation = Quaternion.LookRotation(-n) * Quaternion.Euler(0f, 0f, Random.value * 360f);
        BloodDecal d = q.AddComponent<BloodDecal>();
        d.size = size; d.life = life; d.grow = grow;
        decals.Enqueue(q);
        int max = Scale < 1f ? 16 : 48;
        while (decals.Count > max) { GameObject old = decals.Dequeue(); if (old != null) Destroy(old); }
    }

    static void Spray(Vector3 pos, Vector3 dir, int count, float speedMin, float speedMax, float sizeMin, float sizeMax, float angle, float life)
    {
        count = Mathf.Max(2, Mathf.RoundToInt(count * Scale));
        GameObject g = new GameObject("bloodFx");
        g.transform.position = pos;
        g.transform.rotation = Quaternion.LookRotation(dir);
        ParticleSystem ps = g.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        ParticleSystem.MainModule main = ps.main;
        main.loop = false;
        main.duration = 1f;
        main.playOnAwake = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(life * 0.6f, life);
        main.startSpeed = new ParticleSystem.MinMaxCurve(speedMin, speedMax);
        main.startSize = new ParticleSystem.MinMaxCurve(sizeMin, sizeMax);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, 6.28f);
        main.gravityModifier = 1.7f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 80;
        ParticleSystem.EmissionModule em = ps.emission;
        em.rateOverTime = 0f;
        em.SetBursts(new ParticleSystem.Burst[] { new ParticleSystem.Burst(0f, (short)count) });
        ParticleSystem.ShapeModule shp = ps.shape;
        shp.shapeType = ParticleSystemShapeType.Cone;
        shp.angle = angle;
        shp.radius = 0.03f;
        ParticleSystem.ColorOverLifetimeModule col = ps.colorOverLifetime;
        col.enabled = true;
        Gradient gr = new Gradient();
        gr.SetKeys(new GradientColorKey[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                   new GradientAlphaKey[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.6f), new GradientAlphaKey(0f, 1f) });
        col.color = new ParticleSystem.MinMaxGradient(gr);
        ParticleSystemRenderer r = g.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = bloodMat;
        // Gotas esticadas na direcao do movimento.
        r.renderMode = ParticleSystemRenderMode.Stretch;
        r.velocityScale = 0.045f;
        r.lengthScale = 1.4f;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        ps.Play();
        Destroy(g, life + 0.6f);
    }

    // Acerto de bala/explosao em quem sangra. dir = para onde a bala ia.
    public static void Blood(Vector3 point, Vector3 dir, float dmg, bool head)
    {
        if (bloodFrame != Time.frameCount) { bloodFrame = Time.frameCount; bloodCount = 0; }
        if (++bloodCount > (Scale < 1f ? 3 : 7)) return;
        BloodAssets();
        if (dir.sqrMagnitude < 0.0001f) dir = Random.onUnitSphere;
        dir.Normalize();
        float k = Mathf.Clamp(dmg / 40f, 0.45f, 2.2f) * (head ? 1.4f : 1f);
        float rk = Mathf.Sqrt(k);
        // Jato de saida (segue a bala), respingo de entrada (volta para o atirador) e nevoa.
        Spray(point, dir + Vector3.up * 0.12f, Mathf.RoundToInt(14f * k), 2f, 6.5f * rk, 0.03f, 0.085f, 20f, 0.55f);
        Spray(point, -dir + Vector3.up * 0.35f, Mathf.RoundToInt(6f * k), 0.8f, 2.8f, 0.025f, 0.07f, 38f, 0.42f);
        Burst(point, mistMat, Mathf.RoundToInt(4f * k), 0.22f, 0.42f, 0.2f, 1.1f, 0.16f * rk, 0.36f * rk, 0.25f, 1.9f, 0.04f);
        if (Time.time - fleshAt > 0.07f) { fleshAt = Time.time; Sfx.At("flesh", point, 0.55f, head ? 1.25f : 1f); }

        RaycastHit h;
        // Mancha na parede atras do alvo.
        if (WorldRay(point, dir, 3.4f, out h))
            Decal(h.point, h.normal, Random.Range(0.5f, 0.85f) * rk * Mathf.Lerp(1f, 0.6f, h.distance / 3.4f), 30f, 0.12f);
        // Gotas no chao, um pouco a frente do alvo.
        if (Random.value < 0.55f * k)
        {
            Vector3 from = point + dir * Random.Range(0.15f, 0.9f) + new Vector3(Random.Range(-0.25f, 0.25f), 0f, Random.Range(-0.25f, 0.25f));
            if (WorldRay(from, Vector3.down, 3.5f, out h)) Decal(h.point, h.normal, Random.Range(0.2f, 0.42f) * rk, 30f, 0.25f);
        }
    }

    // Morte: jorro maior e uma poca que cresce devagar embaixo do corpo.
    public static void BloodDeath(Vector3 chest, Vector3 feet)
    {
        BloodAssets();
        Spray(chest, Vector3.up, Mathf.RoundToInt(22f), 1.5f, 5f, 0.035f, 0.1f, 55f, 0.7f);
        Burst(chest, mistMat, 6, 0.3f, 0.55f, 0.3f, 1.4f, 0.3f, 0.55f, 0.25f, 1.9f, 0.08f);
        RaycastHit h;
        if (WorldRay(feet + Vector3.up * 0.6f, Vector3.down, 3f, out h)) Decal(h.point, h.normal, Random.Range(1.3f, 1.7f), 40f, 3.2f);
    }

    // ---------- Dano levado: brilho vermelho nas bordas da tela (sem respingo) ----------

    static Texture2D vignetteTex, edgeTex, edgeVTex;
    static float hurt;                       // intensidade atual (0..1), sobe a cada dano e apaga sozinha
    static float hurtAt = -99f;
    static Vector3 hurtFrom; static bool hurtHasDir;

    static void HurtAssets()
    {
        if (vignetteTex != null) return;
        // Vinheta: transparente no meio, vermelho cheio nas bordas (curva suave, cantos mais fortes).
        const int S = 128;
        vignetteTex = new Texture2D(S, S, TextureFormat.RGBA32, false);
        vignetteTex.wrapMode = TextureWrapMode.Clamp;
        Color[] px = new Color[S * S];
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float dx = Mathf.Abs((x + 0.5f) / S * 2f - 1f), dy = Mathf.Abs((y + 0.5f) / S * 2f - 1f);
                float d = Mathf.Pow(Mathf.Pow(dx, 4f) + Mathf.Pow(dy, 4f), 0.25f);   // "quadrado arredondado", acompanha a tela
                float a = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.55f, 1.02f, d));
                px[y * S + x] = new Color(1f, 1f, 1f, a * a);
            }
        vignetteTex.SetPixels(px); vignetteTex.Apply();
        // Faixa lateral: forte na borda esquerda da textura, some para a direita.
        edgeTex = new Texture2D(64, 1, TextureFormat.RGBA32, false);
        edgeTex.wrapMode = TextureWrapMode.Clamp;
        for (int x = 0; x < 64; x++) { float t = 1f - x / 63f; edgeTex.SetPixel(x, 0, new Color(1f, 1f, 1f, t * t * t)); }
        edgeTex.Apply();
        // Faixa de baixo (tiro pelas costas): forte embaixo, some para cima.
        edgeVTex = new Texture2D(1, 64, TextureFormat.RGBA32, false);
        edgeVTex.wrapMode = TextureWrapMode.Clamp;
        for (int y = 0; y < 64; y++) { float t = 1f - y / 63f; edgeVTex.SetPixel(0, y, new Color(1f, 1f, 1f, t * t * t)); }
        edgeVTex.Apply();
    }

    public static void ScreenBlood(float dmg) { Hurt(dmg, false, Vector3.zero); }
    public static void ScreenBlood(float dmg, Vector3 from) { Hurt(dmg, true, from); }

    static void Hurt(float dmg, bool hasDir, Vector3 from)
    {
        float cur = CurrentHurt();
        hurt = Mathf.Clamp01(cur + Mathf.Lerp(0.35f, 0.9f, Mathf.Clamp01(dmg / 60f)));
        hurtAt = Time.time;
        hurtHasDir = hasDir; hurtFrom = from;
    }

    static float CurrentHurt() { return hurt * Mathf.Clamp01(1f - (Time.time - hurtAt) / 1.1f); }

    public static void ClearScreenBlood() { hurt = 0f; hurtAt = -99f; }

    // Chamado no OnGUI do HUD (vw x vh = tela virtual). lowHp = 0..1 quando a vida esta baixa (pulsa).
    public static void DrawScreenBlood(float vw, float vh, float lowHp = 0f)
    {
        HurtAssets();
        float h = CurrentHurt();
        float pulse = lowHp > 0f ? lowHp * (0.55f + 0.25f * Mathf.Sin(Time.time * 5f)) : 0f;
        float a = Mathf.Clamp01(Mathf.Max(h, pulse));
        if (a < 0.01f) return;
        Color old = GUI.color;
        GUI.color = new Color(0.85f, 0.02f, 0.04f, a * 0.85f);
        GUI.DrawTexture(new Rect(0f, 0f, vw, vh), vignetteTex, ScaleMode.StretchToFill, true);
        // Lado de onde veio o tiro brilha mais forte.
        if (hurtHasDir && h > 0.01f && Camera.main != null)
        {
            Transform c = Camera.main.transform;
            Vector3 to = hurtFrom - c.position; to.y = 0f;
            Vector3 f = c.forward; f.y = 0f;
            if (to.sqrMagnitude > 0.01f && f.sqrMagnitude > 0.01f)
            {
                float ang = Vector3.SignedAngle(f, to, Vector3.up);   // + direita, - esquerda
                float w = vw * 0.22f, hh = vh * 0.3f;
                GUI.color = new Color(0.9f, 0.02f, 0.04f, h * 0.9f);
                if (ang < -30f && ang > -150f) GUI.DrawTextureWithTexCoords(new Rect(0f, 0f, w, vh), edgeTex, new Rect(0f, 0f, 1f, 1f), true);
                if (ang > 30f && ang < 150f) GUI.DrawTextureWithTexCoords(new Rect(vw - w, 0f, w, vh), edgeTex, new Rect(1f, 0f, -1f, 1f), true);
                if (Mathf.Abs(ang) >= 120f) GUI.DrawTextureWithTexCoords(new Rect(0f, vh - hh, vw, hh), edgeVTex, new Rect(0f, 0f, 1f, 1f), true);
            }
        }
        GUI.color = old;
    }
}

// Mancha de sangue no cenario: abre rapido (ou devagar, na poca), fica um tempo e some.
public class BloodDecal : MonoBehaviour
{
    public float size = 0.5f, life = 30f, grow = 0.15f;
    float age;
    Renderer rend;
    MaterialPropertyBlock mpb;
    Color baseCol;

    void Start()
    {
        rend = GetComponent<Renderer>();
        baseCol = rend.sharedMaterial.color;
        transform.localScale = Vector3.one * size * 0.3f;
    }

    void Update()
    {
        age += Time.deltaTime;
        if (age < grow * 4f) transform.localScale = Vector3.one * size * Mathf.Lerp(0.3f, 1f, 1f - Mathf.Exp(-age / Mathf.Max(0.01f, grow) * 1.6f));
        float left = life - age;
        if (left < 4f)
        {
            if (mpb == null) mpb = new MaterialPropertyBlock();
            Color c = baseCol; c.a *= Mathf.Clamp01(left / 4f);
            mpb.SetColor("_Color", c);
            rend.SetPropertyBlock(mpb);
            if (left <= 0f) Destroy(gameObject);
        }
    }
}

// Um trecho da descarga eletrica: refaz o zigue-zague varias vezes por segundo (efeito de faisca viva).
public class ArcFx : MonoBehaviour
{
    LineRenderer glow, core;
    Vector3 a, b;
    Color col;
    float life, age, nextJitter;
    int n;
    bool main;

    public void Init(Vector3 from, Vector3 to, Color c, float lifeTime, Material m, bool isMain)
    {
        a = from; b = to; col = c; life = lifeTime; main = isMain;
        n = Mathf.Clamp(Mathf.CeilToInt(Vector3.Distance(a, b) / 0.22f) + 1, 4, 14);
        glow = Line("glow", m, isMain ? 0.22f : 0.12f);
        core = Line("core", m, isMain ? 0.05f : 0.03f);
        Jitter();
        Apply(1f);
        Destroy(gameObject, life);
    }

    LineRenderer Line(string nm, Material m, float w)
    {
        GameObject g = new GameObject(nm);
        g.transform.SetParent(transform, false);
        LineRenderer lr = g.AddComponent<LineRenderer>();
        lr.sharedMaterial = m;
        lr.positionCount = n;
        lr.useWorldSpace = true;
        lr.numCapVertices = 2;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;
        AnimationCurve wc = new AnimationCurve(new Keyframe(0f, w * 0.35f), new Keyframe(0.5f, w), new Keyframe(1f, w * 0.6f));
        lr.widthCurve = wc;
        lr.widthMultiplier = 1f;
        return lr;
    }

    void Jitter()
    {
        Vector3 d = b - a;
        Vector3 side = Vector3.Cross(d.normalized, Vector3.up);
        if (side.sqrMagnitude < 0.01f) side = Vector3.right;
        side.Normalize();
        Vector3 up = Vector3.Cross(side, d.normalized);
        float amp = Mathf.Min(0.18f, d.magnitude * 0.09f) * (main ? 1f : 1.3f);
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)(n - 1);
            float env = Mathf.Sin(t * Mathf.PI);   // pontas presas, meio solto
            Vector3 off = (side * Random.Range(-1f, 1f) + up * Random.Range(-1f, 1f)) * amp * env;
            Vector3 p = Vector3.Lerp(a, b, t) + off;
            glow.SetPosition(i, p + off * 0.3f);
            core.SetPosition(i, p);
        }
        nextJitter = age + Random.Range(0.02f, 0.04f);
    }

    void Apply(float k)
    {
        float flick = Random.Range(0.6f, 1f);
        Color g = new Color(col.r, col.g, col.b, 0.45f * k * flick);
        Color g2 = new Color(col.r, col.g, col.b, 0.15f * k * flick);
        glow.startColor = g2; glow.endColor = g;
        Color w = Color.Lerp(Color.white, col, 0.25f);
        core.startColor = new Color(w.r, w.g, w.b, 0.35f * k);
        core.endColor = new Color(w.r, w.g, w.b, 0.95f * k * flick);
    }

    void Update()
    {
        age += Time.deltaTime;
        if (age >= nextJitter) Jitter();
        Apply(Mathf.Clamp01(1f - age / life));
    }
}
