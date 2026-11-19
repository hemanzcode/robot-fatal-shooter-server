using System.Collections.Generic;
using UnityEngine;

// Dinossauro solto pela caixa laranja: luta pelo time de quem abriu, caca o inimigo mais proximo e morde.
// kind 0 = T-REX (lento, muita vida, mordida forte); 1 = RAPTOR (rapido, mordidas curtas).
public class Dino : MonoBehaviour
{
    public int kind;
    public bool remote;          // copia em um cliente online: so anima, quem pensa e o host
    public Vector3 netVel;
    Actor actor;
    CharacterController cc;
    Transform body, head, jaw;
    readonly Transform[] thigh = new Transform[2], shin = new Transform[2], foot = new Transform[2];
    readonly List<Transform> tail = new List<Transform>();
    Actor target;
    List<Vector3> path; int pathIdx; float repathAt; Vector3 pathGoal, roam;
    float nextThink, nextBite, jawOpen, phase, vy, dieAt, size, runSpeed, reach, biteDmg, biteEvery, hip;
    Vector3 hvel;

    public static Dino Spawn(Vector3 pos, int team, int kind, bool remote = false)
    {
        string nm = kind == 0 ? "T-REX" : "RAPTOR";
        GameObject g = new GameObject("Dino_" + nm);
        g.transform.position = pos + Vector3.up * 0.2f;
        CharacterController c = g.AddComponent<CharacterController>();
        c.height = 1.8f; c.radius = 0.5f; c.center = new Vector3(0f, 0.9f, 0f); c.stepOffset = 0.5f;
        Actor a = g.AddComponent<Actor>();
        a.creature = true;
        a.noRespawn = true;
        a.Init(nm, team, false, 0, false);
        a.maxHealth = a.health = kind == 0 ? 375f : 140f;   // nerf: metade da vida
        Dino d = g.AddComponent<Dino>();
        d.kind = kind;
        d.remote = remote;
        d.Build(a, c);
        if (remote) { c.enabled = false; g.AddComponent<NetPuppet>(); }
        if (Game.I != null) Game.I.actors.Add(a);
        Sfx.At("roar", pos, 1f, kind == 0 ? 0.8f : 1.5f);
        return d;
    }

    Transform P(string part, Transform parent, Vector3 pos, Color c, float scale = 1f)
    {
        Transform t = WeaponArt.Part(part, parent, c).transform;
        t.localPosition = pos;
        t.localScale = Vector3.one * scale;
        return t;
    }

    void Build(Actor a, CharacterController c)
    {
        actor = a; cc = c;
        size = kind == 0 ? 0.82f : 0.36f;
        runSpeed = kind == 0 ? 6.4f : 9.6f;
        reach = kind == 0 ? 3.1f : 1.9f;
        biteDmg = kind == 0 ? 37.5f : 15f;               // nerf: metade da mordida
        biteEvery = kind == 0 ? 1.3f : 0.6f;
        dieAt = Time.time + 60f;
        hip = 2.02f * size;
        Color skin = kind == 0 ? new Color(0.62f, 0.6f, 0.4f) : new Color(1f, 0.62f, 0.34f);
        skin = Color.Lerp(skin, Art.TeamColor(a.team), 0.12f);

        GameObject vis = new GameObject("vis");
        vis.transform.SetParent(transform, false);
        a.model = vis;
        body = new GameObject("hip").transform;
        body.SetParent(vis.transform, false);
        body.localPosition = new Vector3(0f, hip, 0f);
        body.localScale = Vector3.one * size;
        P("Dino_Body", body, Vector3.zero, skin);
        head = P("Dino_Head", body, new Vector3(0f, 0.8f, 2.05f), skin, kind == 0 ? 1f : 0.8f);
        jaw = P("Dino_Jaw", head, new Vector3(0f, -0.12f, 0.1f), skin);
        Transform par = body; Vector3 at = new Vector3(0f, 0f, -1f);
        for (int i = 0; i < 4; i++)
        {
            Transform t = P("Dino_Tail", par, at, skin, i == 0 ? 1f : 0.74f);
            tail.Add(t); par = t; at = new Vector3(0f, 0f, -0.95f);
        }
        for (int i = 0; i < 2; i++)
        {
            float sx = i == 0 ? -1f : 1f;
            thigh[i] = P("Dino_Thigh", body, new Vector3(sx * 0.45f, -0.1f, 0.1f), skin);
            shin[i] = P("Dino_Shin", thigh[i], new Vector3(0f, -0.85f, 0.22f), skin);
            foot[i] = P("Dino_Foot", shin[i], new Vector3(0f, -0.75f, -0.3f), skin);
            P("Dino_Arm", body, new Vector3(sx * 0.4f, -0.25f, 1.25f), skin, kind == 0 ? 1f : 1.8f);
        }
        // Faixa do time no pescoco, para saber de quem ele e.
        Art.Prim(PrimitiveType.Cylinder, body, new Vector3(0f, 0.5f, 1.65f), new Vector3(0.85f, 0.06f, 0.85f), Art.Glow(Art.TeamColor(a.team), 1.6f), false, "collar").transform.localRotation = Quaternion.Euler(50f, 0f, 0f);

        // Corpo "atiravel": maior que a capsula de navegacao (que precisa caber nas ruas).
        GameObject hb = new GameObject("hitbox");
        hb.transform.SetParent(transform, false);
        hb.transform.localPosition = new Vector3(0f, hip, 0.5f * size);
        CapsuleCollider cap = hb.AddComponent<CapsuleCollider>();
        cap.direction = 2; cap.radius = 0.75f * size; cap.height = 4.6f * size;
        Physics.IgnoreCollision(cc, cap, true);
        roam = Arena.RandomPoint();
    }

    Vector3 PathDir(Vector3 goal)
    {
        if (path == null || Time.time >= repathAt || (goal - pathGoal).sqrMagnitude > 16f)
        {
            path = Arena.FindPath(transform.position, goal);
            pathIdx = 0; repathAt = Time.time + 1.2f; pathGoal = goal;
        }
        while (pathIdx < path.Count && Flat(transform.position, path[pathIdx]) < 1f) pathIdx++;
        Vector3 to = (pathIdx < path.Count ? path[pathIdx] : goal) - transform.position;
        to.y = 0f;
        return to.sqrMagnitude > 0.2f ? to.normalized : Vector3.zero;
    }

    static float Flat(Vector3 a, Vector3 b) { float dx = a.x - b.x, dz = a.z - b.z; return Mathf.Sqrt(dx * dx + dz * dz); }

    void Update()
    {
        if (Game.I == null || actor == null || !actor.alive || Game.I.matchOver) return;
        float dt = Time.deltaTime;
        if (remote) { hvel = netVel; Animate(dt); return; }
        if (Time.time >= dieAt) { actor.TakeDamage(99999f, null, false, "tempo"); return; }
        if (Time.time >= nextThink)
        {
            nextThink = Time.time + 0.3f;
            target = null; float bd = 90f;
            var list = Game.I.actors;
            for (int i = 0; i < list.Count; i++)
            {
                Actor a = list[i];
                if (a == null || !a.alive || a.team == actor.team) continue;
                float d = Flat(a.transform.position, transform.position) + (a.vehicle != null ? 40f : 0f);
                if (d < bd) { bd = d; target = a; }
            }
        }

        Vector3 dir; float speed = runSpeed;
        if (target != null && target.alive)
        {
            Vector3 to = target.transform.position - transform.position;
            float dy = to.y; to.y = 0f;
            float dist = to.magnitude;
            bool direct = dist < 7f && !Physics.Linecast(transform.position + Vector3.up, target.ChestPos, ~0, QueryTriggerInteraction.Ignore);
            dir = direct ? to.normalized : PathDir(target.transform.position);
            if (dist < reach * 0.7f) dir = Vector3.zero;
            if (dist < reach && Mathf.Abs(dy) < 2.5f && Time.time >= nextBite)
            {
                nextBite = Time.time + biteEvery;
                jawOpen = 1f;
                Vector3 push = (to.sqrMagnitude > 0.01f ? to.normalized : transform.forward) * (kind == 0 ? 9f : 4f) + Vector3.up * 3f;
                if (target.vehicle != null) target.vehicle.Damage(biteDmg * 1.5f, actor);
                else { target.externalVel += push; target.TakeDamage(biteDmg, actor, false, actor.displayName); }
                Sfx.At("roar", transform.position, 0.8f, kind == 0 ? 1.1f : 1.9f);
            }
            if (to.sqrMagnitude > 0.01f && dir == Vector3.zero)
                transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(to), 300f * dt);
        }
        else
        {
            if (Flat(transform.position, roam) < 2.5f) roam = Arena.RandomPoint();
            dir = PathDir(roam);
            speed = runSpeed * 0.5f;
        }

        if (dir.sqrMagnitude > 0.01f)
            transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(dir), (kind == 0 ? 200f : 380f) * dt);
        if (cc.isGrounded && vy < 0f) vy = -2f;
        vy -= 22f * dt;
        hvel = Vector3.Lerp(hvel, dir * speed, 1f - Mathf.Exp(-6f * dt));
        if (cc.enabled) cc.Move((hvel + Vector3.up * vy + actor.externalVel * (kind == 0 ? 0.15f : 0.6f)) * dt);

        Animate(dt);
    }

    // Animacao por codigo: passadas, balanco do rabo e da cabeca, mordida.
    void Animate(float dt)
    {
        float sp = new Vector3(hvel.x, 0f, hvel.z).magnitude;
        phase += sp * dt * (1.15f / Mathf.Max(0.2f, size)) * 0.55f;
        float amp = Mathf.Clamp01(sp / 2f);
        for (int i = 0; i < 2; i++)
        {
            float ph = phase + i * Mathf.PI;
            float sw = Mathf.Sin(ph);
            thigh[i].localRotation = Quaternion.Euler(-sw * 34f * amp, 0f, 0f);
            shin[i].localRotation = Quaternion.Euler(Mathf.Max(0f, Mathf.Cos(ph)) * 48f * amp, 0f, 0f);
            foot[i].localRotation = Quaternion.Euler(-Mathf.Max(0f, Mathf.Cos(ph)) * 30f * amp + sw * 14f * amp, 0f, 0f);
        }
        body.localPosition = new Vector3(0f, hip + Mathf.Abs(Mathf.Sin(phase)) * 0.07f * size * amp, 0f);
        body.localRotation = Quaternion.Euler(Mathf.Sin(phase * 2f) * 2.5f * amp, 0f, Mathf.Sin(phase) * 3f * amp);
        float t = Time.time;
        for (int i = 0; i < tail.Count; i++)
            tail[i].localRotation = Quaternion.Euler(-3f, Mathf.Sin(t * 1.8f - i * 0.7f + phase) * (7f + 6f * amp), 0f);
        jawOpen = Mathf.MoveTowards(jawOpen, 0f, dt * 2.2f);
        head.localRotation = Quaternion.Euler(18f + Mathf.Sin(t * 1.3f) * 4f - jawOpen * 22f, Mathf.Sin(t * 0.7f) * 6f, 0f);
        jaw.localRotation = Quaternion.Euler(6f + jawOpen * 34f, 0f, 0f);
    }
}

// Caixa laranja de suprimentos: cai de paraquedas em um ponto aleatorio. Quem chegar pode abrir (F) ou deixar para la.
// Aberta, solta um dos dois dinossauros (sorteado) para lutar pelo time de quem abriu.
public class SupplyCrate : MonoBehaviour
{
    public static readonly List<SupplyCrate> All = new List<SupplyCrate>();
    public bool landed;
    public bool remote;          // copia em cliente online
    public int netId;
    static int nextNetId = 1;
    GameObject chute;
    float groundY, botTimer;
    Light lamp;

    public static SupplyCrate Drop(Vector3 at, bool remote = false)
    {
        GameObject g = new GameObject("SupplyCrate");
        g.transform.position = remote ? at : new Vector3(at.x, 55f, at.z);
        SupplyCrate c = g.AddComponent<SupplyCrate>();
        c.groundY = at.y;
        c.remote = remote;
        c.netId = nextNetId++;
        return c;
    }

    public void NetSet(Vector3 pos, bool isLanded)
    {
        transform.position = pos;
        if (isLanded && !landed) { landed = true; transform.rotation = Quaternion.identity; if (chute != null) Destroy(chute); }
    }

    public static SupplyCrate Near(Vector3 p, float range)
    {
        for (int i = 0; i < All.Count; i++)
            if (All[i] != null && All[i].landed && (All[i].transform.position - p).sqrMagnitude < range * range) return All[i];
        return null;
    }

    void OnEnable() { All.Add(this); }
    void OnDisable() { All.Remove(this); }

    void Start()
    {
        Material orange = Art.Mat(new Color(1f, 0.38f, 0.02f), new Color(0.25f, 0.08f, 0f), 0f, default(Color), 0.35f, 0.1f);
        Material band = Art.Mat(new Color(0.08f, 0.08f, 0.09f), Color.black, 0f, default(Color), 0.5f, 0.6f);
        Art.Box(transform, new Vector3(0f, 0.6f, 0f), new Vector3(1.3f, 1.2f, 1.3f), orange, true, "crate");
        Art.Box(transform, new Vector3(0f, 0.6f, 0f), new Vector3(1.34f, 0.12f, 1.34f), band);
        Art.Box(transform, new Vector3(0f, 0.6f, 0f), new Vector3(0.12f, 1.24f, 1.34f), band);
        Art.Box(transform, new Vector3(0f, 0.6f, 0f), new Vector3(1.34f, 1.24f, 0.12f), band);
        chute = new GameObject("chute");
        chute.transform.SetParent(transform, false);
        Material cloth = Art.Mat(new Color(0.95f, 0.5f, 0.1f), Color.black, 0f, default(Color), 0.1f, 0f);
        Art.Prim(PrimitiveType.Sphere, chute.transform, new Vector3(0f, 5.2f, 0f), new Vector3(4.6f, 2.2f, 4.6f), cloth);
        for (int i = 0; i < 4; i++)
        {
            float a = i * Mathf.PI * 0.5f + 0.78f;
            GameObject l = Art.Box(chute.transform, new Vector3(Mathf.Cos(a) * 0.85f, 3.1f, Mathf.Sin(a) * 0.85f), new Vector3(0.025f, 4.2f, 0.025f), band);
            l.transform.localRotation = Quaternion.Euler(Mathf.Sin(a) * 22f, 0f, -Mathf.Cos(a) * 22f);
        }
        lamp = Art.PointLight(transform, new Vector3(0f, 1.8f, 0f), Art.Orange, 9f, 1.6f);
    }

    void Update()
    {
        if (Game.I == null) return;
        if (remote) { if (lamp != null) lamp.intensity = (1.2f + Mathf.Sin(Time.time * 5f) * 0.6f) * Art.LightBoost; return; }
        if (!landed)
        {
            transform.position += Vector3.down * 7f * Time.deltaTime;
            transform.rotation = Quaternion.Euler(Mathf.Sin(Time.time * 1.3f) * 4f, 0f, Mathf.Cos(Time.time * 1.1f) * 4f);
            if (transform.position.y <= groundY)
            {
                Vector3 p = transform.position; p.y = groundY;
                transform.SetPositionAndRotation(p, Quaternion.identity);
                landed = true;
                Destroy(chute);
                Sfx.At("hit", p, 1f, 0.5f);
            }
            return;
        }
        lamp.intensity = (1.2f + Mathf.Sin(Time.time * 5f) * 0.6f) * Art.LightBoost;
        // Bots que passam ao lado param um instante e abrem.
        var list = Game.I.actors;
        for (int i = 0; i < list.Count; i++)
        {
            Actor a = list[i];
            if (a == null || !a.alive || a.isPlayer || a.creature) continue;
            if ((a.transform.position - transform.position).sqrMagnitude > 2.6f * 2.6f) continue;
            botTimer += Time.deltaTime;
            if (botTimer > 1.5f) Open(a);
            return;
        }
        botTimer = 0f;
    }

    public void Open(Actor by)
    {
        if (!landed || by == null) return;
        if (NetGame.IsClient) { NetGame.OpenCrate(); return; }
        // Caixa aberta: sai um dinossauro (sorteado) ali mesmo e 2 MECAS caem do ceu em pontos aleatorios da cidade,
        // todos lutando pelo time de quem abriu.
        int kind = Random.value < 0.5f ? 0 : 1;
        Vector3 p = transform.position;
        Fx.Blast(p + Vector3.up * 0.6f, 2.2f);
        Dino.Spawn(p, by.team, kind);
        Mech.Drop(by.team);
        Mech.Drop(by.team);
        if (Game.I != null)
        {
            Game.I.Announce((kind == 0 ? "T-REX" : "RAPTOR") + " SOLTO PELO TIME " + Art.TeamName(by.team), Art.TeamColor(by.team));
            Game.I.Announce("2 MECAS CAINDO DO CEU PELO TIME " + Art.TeamName(by.team), Art.TeamColor(by.team));
            Game.I.BannerAll((kind == 0 ? "T-REX" : "RAPTOR") + " + 2 MECAS PELO TIME " + Art.TeamName(by.team));
        }
        Destroy(gameObject);
    }
}
