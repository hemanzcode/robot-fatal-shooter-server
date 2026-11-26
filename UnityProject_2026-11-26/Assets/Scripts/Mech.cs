using UnityEngine;
using System.Collections.Generic;

// MECA de combate (design proprio, "TITAN"): blindado bipede de assalto que cai do ceu com propulsores quando alguem abre
// a caixa laranja. Luta pelo time de quem abriu: rajadas dos dois canhoes dos bracos e um foguete de vez em quando.
// Dura 75 s e se autodestroi. Online: o host simula, os clientes so animam a copia.
public class Mech : MonoBehaviour
{
    public bool remote;
    public Vector3 netVel;
    Actor actor;
    CharacterController cc;
    Transform hip, torso, armL, armR, gatSpin;
    float spin;
    readonly Transform[] thigh = new Transform[2], shin = new Transform[2];
    readonly List<GameObject> flames = new List<GameObject>();
    Light jetLight;
    Actor target;
    List<Vector3> path; int pathIdx; float repathAt; Vector3 pathGoal, roam;
    float nextThink, nextBurst, nextShot, nextRocket, dieAt, phase, vy, groundY, recoil;
    int burstLeft, side;
    bool falling;
    Vector3 hvel;

    const float Hp = 400f, Speed = 3.6f, Range = 45f, ShotDmg = 9f;

    // Cai do ceu em um ponto aleatorio da cidade.
    public static Mech Drop(int team)
    {
        Vector3 p = Arena.RandomPoint();
        Mech m = Spawn(new Vector3(p.x, 45f, p.z), team, false);
        m.groundY = p.y;
        m.falling = true;
        m.cc.enabled = false;
        return m;
    }

    public static Mech Spawn(Vector3 pos, int team, bool remote)
    {
        GameObject g = new GameObject("Mech");
        g.transform.position = pos;
        CharacterController c = g.AddComponent<CharacterController>();
        c.height = 3.2f; c.radius = 0.75f; c.center = new Vector3(0f, 1.6f, 0f); c.stepOffset = 0.5f;
        Actor a = g.AddComponent<Actor>();
        a.creature = true;
        a.metal = true;
        a.noRespawn = true;
        a.Init("MECA", team, false, 0, false);
        a.maxHealth = a.health = Hp;
        Mech m = g.AddComponent<Mech>();
        m.remote = remote;
        m.Build(a, c);
        if (remote) { c.enabled = false; g.AddComponent<NetPuppet>(); }
        if (Game.I != null) Game.I.actors.Add(a);
        return m;
    }

    void Build(Actor a, CharacterController c)
    {
        actor = a; cc = c;
        dieAt = Time.time + 75f;
        nextRocket = Time.time + 4f;
        Color tc = Art.TeamColor(a.team);
        // "TITAN": blindado de assalto (design proprio). Chapas de aco com textura, juntas escuras, faixas do time.
        Material armor = Art.PbrLocal("Metal032", new Color(1.55f, 1.58f, 1.62f), 1.2f, 0.9f, 0f);
        Material plate = Art.PbrLocal("DiamondPlate008C", new Color(1.45f, 1.48f, 1.52f), 0.8f, 0.9f, 0f);
        Material dark = Art.PbrLocal("Metal032", new Color(0.62f, 0.64f, 0.68f), 1.2f, 0.8f, 0f);
        Material black = Art.Mat(new Color(0.16f, 0.17f, 0.19f), Color.black, 0f, default(Color), 0.5f, 0.4f);
        Material stripe = Art.Mat(Color.Lerp(tc, Color.white, 0.1f), Color.black, 0f, default(Color), 0.45f, 0.3f);
        Material glow = Art.Glow(tc, 2.4f);
        Material hazard = Art.Mat(new Color(0.95f, 0.72f, 0.08f), Color.black, 0f, default(Color), 0.4f, 0.2f);
        Material chrome = Art.Mat(new Color(0.75f, 0.77f, 0.8f), Color.black, 0f, default(Color), 0.9f, 0.95f);

        GameObject vis = new GameObject("vis");
        vis.transform.SetParent(transform, false);
        a.model = vis;
        hip = new GameObject("hip").transform;
        hip.SetParent(vis.transform, false);
        hip.localPosition = new Vector3(0f, 1.85f, 0f);
        Art.Box(hip, Vector3.zero, new Vector3(1.0f, 0.42f, 0.75f), dark, false, "pelvis");
        Art.Box(hip, new Vector3(0f, -0.1f, 0.4f), new Vector3(0.7f, 0.45f, 0.12f), armor, false, "codPlate");

        // Pernas: coxa larga com placa inclinada, pistao hidraulico ate a canela, pe de dois dedos com espora.
        for (int i = 0; i < 2; i++)
        {
            float sx = i == 0 ? -1f : 1f;
            thigh[i] = new GameObject("thigh").transform;
            thigh[i].SetParent(hip, false);
            thigh[i].localPosition = new Vector3(sx * 0.68f, -0.05f, 0f);
            Art.Prim(PrimitiveType.Sphere, thigh[i], Vector3.zero, new Vector3(0.48f, 0.48f, 0.48f), black, false, "hipJoint");
            Art.Box(thigh[i], new Vector3(0f, -0.45f, 0f), new Vector3(0.44f, 0.8f, 0.52f), dark, false, "thighCore");
            GameObject tp = Art.Box(thigh[i], new Vector3(sx * 0.06f, -0.4f, 0.28f), new Vector3(0.52f, 0.85f, 0.12f), armor, false, "thighPlate");
            tp.transform.localRotation = Quaternion.Euler(-8f, 0f, 0f);
            Art.Box(thigh[i], new Vector3(sx * 0.27f, -0.4f, 0.05f), new Vector3(0.08f, 0.7f, 0.42f), plate, false, "thighSide");
            Art.Box(thigh[i], new Vector3(sx * 0.32f, -0.35f, 0.05f), new Vector3(0.02f, 0.5f, 0.1f), stripe, false, "thighStripe");
            // Pistao atras da coxa (desce ate a canela).
            Art.Prim(PrimitiveType.Cylinder, thigh[i], new Vector3(0f, -0.55f, -0.32f), new Vector3(0.12f, 0.38f, 0.12f), chrome, false, "piston");
            shin[i] = new GameObject("shin").transform;
            shin[i].SetParent(thigh[i], false);
            shin[i].localPosition = new Vector3(0f, -0.88f, 0f);
            Art.Prim(PrimitiveType.Sphere, shin[i], Vector3.zero, new Vector3(0.42f, 0.42f, 0.42f), black, false, "kneeJoint");
            GameObject kp = Art.Box(shin[i], new Vector3(0f, 0.05f, 0.24f), new Vector3(0.5f, 0.42f, 0.16f), armor, false, "kneePad");
            kp.transform.localRotation = Quaternion.Euler(20f, 0f, 0f);
            Art.Box(shin[i], new Vector3(0f, -0.42f, 0f), new Vector3(0.38f, 0.78f, 0.42f), dark, false, "shinCore");
            GameObject sp = Art.Box(shin[i], new Vector3(0f, -0.4f, 0.22f), new Vector3(0.46f, 0.8f, 0.1f), armor, false, "shinPlate");
            sp.transform.localRotation = Quaternion.Euler(6f, 0f, 0f);
            Art.Prim(PrimitiveType.Cylinder, shin[i], new Vector3(0f, -0.3f, -0.26f), new Vector3(0.09f, 0.32f, 0.09f), chrome, false, "shinPiston");
            Art.Box(shin[i], new Vector3(0f, -0.82f, 0f), new Vector3(0.34f, 0.16f, 0.34f), black, false, "ankle");
            // Dois dedos abertos na frente e espora atras.
            for (int t = -1; t <= 1; t += 2)
            {
                GameObject toe = Art.Box(shin[i], new Vector3(t * 0.16f, -0.86f, 0.36f), new Vector3(0.24f, 0.13f, 0.62f), plate, false, "toe");
                toe.transform.localRotation = Quaternion.Euler(0f, t * 12f, 0f);
                Art.Box(shin[i], new Vector3(t * 0.21f, -0.85f, 0.67f), new Vector3(0.22f, 0.08f, 0.06f), hazard, false, "toeTip");
            }
            Art.Box(shin[i], new Vector3(0f, -0.87f, -0.3f), new Vector3(0.26f, 0.12f, 0.36f), plate, false, "heel");
        }

        // Tronco: casco largo e baixo com chapa frontal inclinada, cabine em cunha com visor estreito,
        // ombreiras pesadas, aletas de ventilacao, reator do time nas costas e duas chamines.
        torso = new GameObject("torso").transform;
        torso.SetParent(hip, false);
        torso.localPosition = new Vector3(0f, 0.85f, 0f);
        Art.Box(torso, new Vector3(0f, 0f, -0.05f), new Vector3(1.55f, 1.0f, 1.2f), dark, false, "hull");
        GameObject glacis = Art.Box(torso, new Vector3(0f, -0.05f, 0.62f), new Vector3(1.45f, 0.9f, 0.14f), armor, false, "glacis");
        glacis.transform.localRotation = Quaternion.Euler(-18f, 0f, 0f);
        Art.Box(torso, new Vector3(0f, 0.48f, 0f), new Vector3(1.6f, 0.12f, 1.25f), armor, false, "deck");
        Art.Box(torso, new Vector3(0f, -0.38f, 0.73f), new Vector3(1.2f, 0.07f, 0.04f), stripe, false, "teamStripe");
        for (int v = 0; v < 4; v++) Art.Box(torso, new Vector3(-0.79f, 0.25f - v * 0.14f, -0.05f), new Vector3(0.04f, 0.06f, 0.8f), black, false, "vent");
        for (int v = 0; v < 4; v++) Art.Box(torso, new Vector3(0.79f, 0.25f - v * 0.14f, -0.05f), new Vector3(0.04f, 0.06f, 0.8f), black, false, "vent");
        // Cabine em cunha, sem cabeca separada.
        GameObject cab = Art.Box(torso, new Vector3(0f, 0.62f, 0.32f), new Vector3(0.8f, 0.34f, 0.7f), armor, false, "cockpit");
        cab.transform.localRotation = Quaternion.Euler(12f, 0f, 0f);
        Art.Box(torso, new Vector3(0f, 0.6f, 0.7f), new Vector3(0.66f, 0.07f, 0.04f), glow, false, "visor");
        Art.Box(torso, new Vector3(-0.42f, 0.72f, 0.45f), new Vector3(0.08f, 0.08f, 0.08f), Art.Glow(Art.Red, 2f), false, "lamp");
        Art.Box(torso, new Vector3(0.42f, 0.72f, 0.45f), new Vector3(0.08f, 0.08f, 0.08f), Art.Glow(Art.Red, 2f), false, "lamp");
        // Reator nas costas e chamines.
        Art.Box(torso, new Vector3(0f, 0f, -0.72f), new Vector3(1.0f, 0.75f, 0.25f), plate, false, "backPlate");
        Art.Prim(PrimitiveType.Cylinder, torso, new Vector3(0f, 0f, -0.86f), new Vector3(0.42f, 0.04f, 0.42f), glow, false, "reactor").transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        for (int k = -1; k <= 1; k += 2)
        {
            Art.Prim(PrimitiveType.Cylinder, torso, new Vector3(k * 0.38f, 0.75f, -0.55f), new Vector3(0.18f, 0.35f, 0.18f), black, false, "stack");
            Art.Prim(PrimitiveType.Cylinder, torso, new Vector3(k * 0.38f, 1.1f, -0.55f), new Vector3(0.2f, 0.02f, 0.2f), Art.Glow(new Color(1f, 0.45f, 0.1f), 1.2f), false, "stackGlow");
        }
        Art.Box(torso, new Vector3(-0.55f, 0.75f, -0.3f), new Vector3(0.03f, 0.9f, 0.03f), black, false, "antenna");
        // Lancador de misseis no ombro direito (6 tubos).
        Transform pod = new GameObject("pod").transform;
        pod.SetParent(torso, false);
        pod.localPosition = new Vector3(0.55f, 0.82f, 0.0f);
        Art.Box(pod, Vector3.zero, new Vector3(0.62f, 0.42f, 0.8f), armor, false, "podBox");
        Art.Box(pod, new Vector3(0f, 0.22f, 0f), new Vector3(0.64f, 0.04f, 0.82f), stripe, false, "podStripe");
        for (int t = 0; t < 6; t++)
        {
            Art.Box(pod, new Vector3((t % 3 - 1f) * 0.18f, ((t / 3) - 0.5f) * 0.17f, 0.41f), new Vector3(0.13f, 0.12f, 0.02f), black, false, "tube");
            Art.Box(pod, new Vector3((t % 3 - 1f) * 0.18f, ((t / 3) - 0.5f) * 0.17f, 0.4f), new Vector3(0.07f, 0.06f, 0.02f), Art.Glow(Art.Orange, 1.3f), false, "tubeTip");
        }

        // Braco esquerdo: canhao pesado de cano duplo com escudo. Braco direito: metralhadora rotativa de 6 canos.
        for (int i = 0; i < 2; i++)
        {
            float sx = i == 0 ? -1f : 1f;
            Transform arm = new GameObject(i == 0 ? "armL" : "armR").transform;
            arm.SetParent(torso, false);
            arm.localPosition = new Vector3(sx * 1.18f, 0.1f, 0.35f);
            GameObject sh = Art.Box(arm, new Vector3(sx * 0.05f, 0.3f, -0.05f), new Vector3(0.62f, 0.3f, 0.8f), armor, false, "pauldron");
            sh.transform.localRotation = Quaternion.Euler(0f, 0f, -sx * 14f);
            Art.Box(arm, new Vector3(sx * 0.05f, 0.42f, -0.05f), new Vector3(0.5f, 0.04f, 0.7f), hazard, false, "pauldronStripe");
            Art.Prim(PrimitiveType.Sphere, arm, Vector3.zero, new Vector3(0.45f, 0.45f, 0.45f), black, false, "shoulderJoint");
            Art.Box(arm, new Vector3(0f, -0.1f, 0.45f), new Vector3(0.42f, 0.44f, 0.8f), dark, false, "gunBody");
            if (i == 0)
            {
                for (int bb = 0; bb < 2; bb++)
                {
                    GameObject barrel = Art.Prim(PrimitiveType.Cylinder, arm, new Vector3((bb - 0.5f) * 0.18f, -0.1f, 1.08f), new Vector3(0.14f, 0.32f, 0.14f), black, false, "barrel");
                    barrel.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                    GameObject brake = Art.Prim(PrimitiveType.Cylinder, arm, new Vector3((bb - 0.5f) * 0.18f, -0.1f, 1.38f), new Vector3(0.18f, 0.06f, 0.18f), dark, false, "muzzleBrake");
                    brake.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                }
                GameObject shield = Art.Box(arm, new Vector3(-0.26f, -0.12f, 0.55f), new Vector3(0.08f, 0.75f, 0.95f), plate, false, "shield");
                shield.transform.localRotation = Quaternion.Euler(0f, -6f, 0f);
                Art.Box(arm, new Vector3(-0.31f, -0.12f, 0.55f), new Vector3(0.02f, 0.08f, 0.8f), stripe, false, "shieldStripe");
                armL = arm;
            }
            else
            {
                Art.Prim(PrimitiveType.Cylinder, arm, new Vector3(0f, -0.1f, 0.9f), new Vector3(0.36f, 0.06f, 0.36f), black, false, "gatMount").transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                gatSpin = new GameObject("gatSpin").transform;
                gatSpin.SetParent(arm, false);
                gatSpin.localPosition = new Vector3(0f, -0.1f, 1.2f);
                for (int bb = 0; bb < 6; bb++)
                {
                    float ang = bb * Mathf.PI / 3f;
                    GameObject barrel = Art.Prim(PrimitiveType.Cylinder, gatSpin, new Vector3(Mathf.Cos(ang) * 0.1f, Mathf.Sin(ang) * 0.1f, 0f), new Vector3(0.07f, 0.3f, 0.07f), black, false, "gatBarrel");
                    barrel.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                }
                Art.Prim(PrimitiveType.Cylinder, gatSpin, new Vector3(0f, 0f, 0.22f), new Vector3(0.3f, 0.03f, 0.3f), dark, false, "gatRing").transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                Art.Box(arm, new Vector3(0.24f, -0.05f, 0.45f), new Vector3(0.12f, 0.3f, 0.5f), plate, false, "ammoBox");
                armR = arm;
            }
        }

        // Propulsores nas costas (acesos so durante a queda).
        for (int i = 0; i < 2; i++)
        {
            float sx = i == 0 ? -0.36f : 0.36f;
            Art.Prim(PrimitiveType.Cylinder, torso, new Vector3(sx, -0.3f, -0.8f), new Vector3(0.28f, 0.2f, 0.28f), black, false, "nozzle");
            GameObject f = Art.Prim(PrimitiveType.Cylinder, torso, new Vector3(sx, -1.05f, -0.8f), new Vector3(0.2f, 0.6f, 0.2f), Art.Glow(new Color(1f, 0.55f, 0.15f), 3f), false, "flame");
            flames.Add(f);
        }
        jetLight = Art.PointLight(torso, new Vector3(0f, -1.2f, -0.85f), new Color(1f, 0.55f, 0.2f), 9f, 2f);
        vis.transform.localScale = Vector3.one * 1.08f;

        // Corpo atiravel.
        GameObject hb = new GameObject("hitbox");
        hb.transform.SetParent(transform, false);
        BoxCollider box = hb.AddComponent<BoxCollider>();
        box.center = new Vector3(0f, 2.2f, 0f);
        box.size = new Vector3(1.9f, 2.7f, 1.4f);
        Physics.IgnoreCollision(cc, box, true);
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

    Vector3 Muzzle(Transform arm) { return arm.TransformPoint(new Vector3(0f, -0.1f, arm == armL ? 1.45f : 1.4f)); }

    void Update()
    {
        if (Game.I == null || actor == null || !actor.alive || Game.I.matchOver) return;
        float dt = Time.deltaTime;
        if (remote)
        {
            hvel = netVel;
            SetJets(transform.position.y > 2f);
            Animate(dt, Vector3.zero);
            return;
        }
        if (falling) { Fall(dt); return; }
        if (Time.time >= dieAt) { Fx.Explosion(transform.position + Vector3.up * 2f, Art.Orange, 3f); actor.TakeDamage(99999f, null, false, "tempo"); return; }

        if (Time.time >= nextThink)
        {
            nextThink = Time.time + 0.4f;
            target = null; float bd = Range + 25f;
            var list = Game.I.actors;
            for (int i = 0; i < list.Count; i++)
            {
                Actor a = list[i];
                if (a == null || !a.alive || a.team == actor.team) continue;
                float d = Flat(a.transform.position, transform.position);
                if (d < bd) { bd = d; target = a; }
            }
        }

        Vector3 dir = Vector3.zero, aim = Vector3.zero;
        if (target != null && target.alive)
        {
            Vector3 eye = transform.position + Vector3.up * 2.6f;
            Vector3 to = target.ChestPos - eye;
            float dist = Flat(target.transform.position, transform.position);
            bool sight = dist < Range && SeesThrough(eye, target);
            // Chega a uns 12 m e para para atirar; sem visao, vai atras pelo caminho.
            if (!sight || dist > 14f) dir = PathDir(target.transform.position);
            if (sight)
            {
                aim = to;
                Vector3 flat = new Vector3(to.x, 0f, to.z);
                if (flat.sqrMagnitude > 0.01f) transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(flat), 140f * dt);
                Fire(to, dist);
            }
        }
        else
        {
            if (Flat(transform.position, roam) < 2.5f) roam = Arena.RandomPoint();
            dir = PathDir(roam);
        }

        if (dir.sqrMagnitude > 0.01f && aim == Vector3.zero)
            transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(dir), 120f * dt);
        if (cc.isGrounded && vy < 0f) vy = -2f;
        vy -= 22f * dt;
        hvel = Vector3.Lerp(hvel, dir * Speed, 1f - Mathf.Exp(-4f * dt));
        if (cc.enabled) cc.Move((hvel + Vector3.up * vy + actor.externalVel * 0.1f) * dt);
        Animate(dt, aim);
    }

    // A linha de visao pode bater no proprio alvo (colisor dele): conta como visivel.
    static bool SeesThrough(Vector3 eye, Actor t)
    {
        RaycastHit h;
        if (!Physics.Linecast(eye, t.ChestPos, out h, ~0, QueryTriggerInteraction.Ignore)) return true;
        return h.collider.GetComponentInParent<Actor>() == t;
    }

    void Fire(Vector3 to, float dist)
    {
        if (Time.time >= nextBurst && burstLeft == 0) { burstLeft = 6; nextBurst = Time.time + 1.8f; }
        if (burstLeft > 0 && Time.time >= nextShot)
        {
            burstLeft--;
            nextShot = Time.time + 0.11f;
            side ^= 1;
            Transform arm = side == 0 ? armL : armR;
            Vector3 m = Muzzle(arm);
            Vector3 d = Combat.SpreadDir((target.ChestPos - m).normalized, 2.2f);
            Vector3 end = m + d * Range;
            RaycastHit h;
            if (Physics.Raycast(m, d, out h, Range, ~0, QueryTriggerInteraction.Ignore))
            {
                end = h.point;
                bool head, killed;
                Combat.BulletHit(actor, h, ShotDmg, 1f, 1f, "MECA", Art.TeamColor(actor.team), out head, out killed);
            }
            Color tc = Color.Lerp(Art.TeamColor(actor.team), Color.white, 0.4f);
            Fx.Tracer(m, end, tc, 0.05f, 0.08f);
            NetGame.Tracer(actor, Weapons.All[0], m, end);
            Sfx.At("rifle", m, 0.9f, 0.7f);
            recoil = 1f;
        }
        // Foguete a cada 7 s em alvo que nao esteja colado.
        if (Time.time >= nextRocket && dist > 6f)
        {
            nextRocket = Time.time + 7f;
            Vector3 m = torso.TransformPoint(new Vector3(0.55f, 0.82f, 0.5f));
            Combat.Launch(actor, m, (target.ChestPos - m).normalized, 28f, 0f, 45f, 3f, "BAZUCA", Art.Orange, 1f);
            Sfx.At("rocket", m, 1f, 0.8f);
        }
    }

    void Fall(float dt)
    {
        // Desce rapido e freia com os propulsores perto do chao.
        float h = transform.position.y - groundY;
        float v = h > 12f ? 22f : Mathf.Lerp(6f, 22f, h / 12f);
        transform.position += Vector3.down * v * dt;
        SetJets(true);
        Animate(dt, Vector3.zero);
        if (transform.position.y <= groundY + 0.05f)
        {
            Vector3 p = transform.position; p.y = groundY;
            transform.position = p;
            falling = false;
            cc.enabled = true;
            SetJets(false);
            Fx.Blast(p + Vector3.up * 0.3f, 3f);
            Sfx.At("explosion", p, 1f, 0.6f);
            // Quem estiver embaixo leva o impacto.
            var list = Game.I.actors;
            for (int i = 0; i < list.Count; i++)
            {
                Actor a = list[i];
                if (a == null || !a.alive || a == actor || a.team == actor.team) continue;
                Vector3 d = a.transform.position - p;
                if (d.sqrMagnitude < 9f) { a.externalVel += (new Vector3(d.x, 0f, d.z).normalized * 8f + Vector3.up * 5f); a.TakeDamage(40f, actor, false, "MECA"); }
            }
        }
    }

    void SetJets(bool on)
    {
        for (int i = 0; i < flames.Count; i++)
        {
            if (flames[i].activeSelf != on) flames[i].SetActive(on);
            if (on) flames[i].transform.localScale = new Vector3(0.2f, 0.5f + Random.value * 0.35f, 0.2f);
        }
        if (jetLight != null) jetLight.enabled = on;
    }

    // Passadas pesadas, balanco do tronco e mira dos bracos.
    void Animate(float dt, Vector3 aim)
    {
        float sp = new Vector3(hvel.x, 0f, hvel.z).magnitude;
        phase += sp * dt * 1.5f;
        float amp = Mathf.Clamp01(sp / 1.5f);
        for (int i = 0; i < 2; i++)
        {
            float ph = phase + i * Mathf.PI;
            thigh[i].localRotation = Quaternion.Euler(-Mathf.Sin(ph) * 24f * amp, 0f, 0f);
            shin[i].localRotation = Quaternion.Euler(Mathf.Max(0f, Mathf.Cos(ph)) * 30f * amp, 0f, 0f);
        }
        hip.localPosition = new Vector3(0f, 1.85f - Mathf.Abs(Mathf.Cos(phase)) * 0.06f * amp, 0f);
        torso.localRotation = Quaternion.Euler(0f, Mathf.Sin(phase) * 4f * amp, Mathf.Sin(phase) * 2f * amp);
        float pitch = 0f;
        if (aim.sqrMagnitude > 0.01f) pitch = Mathf.Clamp(-Mathf.Asin(Mathf.Clamp(aim.normalized.y, -1f, 1f)) * Mathf.Rad2Deg, -35f, 35f);
        recoil = Mathf.MoveTowards(recoil, 0f, dt * 8f);
        Quaternion ar = Quaternion.Euler(pitch, 0f, 0f);
        armL.localRotation = ar; armR.localRotation = ar;
        armL.localPosition = new Vector3(-1.18f, 0.1f, 0.35f - (side == 0 ? recoil * 0.14f : 0f));
        armR.localPosition = new Vector3(1.18f, 0.1f, 0.35f - (side == 1 ? recoil * 0.06f : 0f));
        // Metralhadora gira enquanto atira e desacelera depois.
        spin = Mathf.MoveTowards(spin, burstLeft > 0 || recoil > 0.2f ? 1500f : 0f, dt * 1800f);
        if (gatSpin != null) gatSpin.Rotate(0f, 0f, spin * dt, Space.Self);
    }
}
