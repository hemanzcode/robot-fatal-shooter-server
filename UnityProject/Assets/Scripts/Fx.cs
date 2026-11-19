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
}
