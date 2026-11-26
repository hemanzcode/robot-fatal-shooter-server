using System.Collections.Generic;
using UnityEngine;

// Base dos veiculos pilotaveis (helicoptero e moto): entrar/sair, vida, destruicao e reaparecimento.
public class Vehicle : MonoBehaviour
{
    public static readonly List<Vehicle> All = new List<Vehicle>();
    public int team = -1;               // -1 = qualquer time pode usar
    public string label = "VEICULO";
    public float health = 400f, maxHealth = 400f;
    public Actor driver;
    public bool dead;
    public Vector3 eye = new Vector3(0f, 1.5f, 0f);   // olho do piloto, no espaco do veiculo
    public float lookLimit = 110f;
    protected Transform visual;
    protected AudioSource engine;
    Vector3 homePos; Quaternion homeRot;
    float backAt;

    protected virtual void OnEnable() { All.Add(this); useGUILayout = false; }
    protected virtual void OnDisable() { All.Remove(this); }

    protected void Home() { homePos = transform.position; homeRot = transform.rotation; }

    protected void Engine(string clip)
    {
        engine = gameObject.AddComponent<AudioSource>();
        engine.clip = Sfx.Get(clip);
        engine.loop = true;
        engine.spatialBlend = 1f;
        engine.minDistance = 6f; engine.maxDistance = 90f;
        engine.rolloffMode = AudioRolloffMode.Linear;
        engine.volume = 0f;
        engine.Play();
    }

    public bool CanUse(Actor a)
    {
        if (dead || driver != null || a == null || !a.alive) return false;
        if (team >= 0 && team != a.team) return false;
        return (a.transform.position - transform.position).sqrMagnitude < 4.2f * 4.2f;
    }

    public virtual void Enter(Actor a)
    {
        driver = a;
        a.vehicle = this;
        if (a.cc != null) a.cc.enabled = false;
        Sfx.At("reload", transform.position, 0.8f);
    }

    public virtual void Exit()
    {
        Actor a = driver;
        driver = null;
        if (a == null) return;
        a.vehicle = null;
        // Sai pelo lado que estiver livre.
        Vector3 basePos = transform.position + Vector3.up * 0.4f;
        Vector3 outPos = basePos - transform.right * 2.2f;
        if (Physics.CheckSphere(outPos + Vector3.up * 0.9f, 0.4f, ~0, QueryTriggerInteraction.Ignore)) outPos = basePos + transform.right * 2.2f;
        a.transform.position = outPos;
        a.transform.rotation = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
        if (a.cc != null && a.alive) a.cc.enabled = true;
        PlayerController pc = a.GetComponent<PlayerController>();
        if (pc != null) pc.LeftVehicle(transform.eulerAngles.y);
    }

    public void Damage(float d, Actor from)
    {
        if (dead || d <= 0f) return;
        health -= d;
        if (driver != null && driver.isPlayer && Game.I != null) Game.I.damageFlash = Time.time;
        if (health > 0f) return;
        dead = true;
        health = 0f;
        Actor a = driver;
        Vector3 p = transform.position + Vector3.up;
        if (a != null) { Exit(); a.TakeDamage(9999f, from, false, label); }
        Fx.Blast(p, 6f);
        Sfx.At("explosion", p, 1f);
        SetShown(false);
        backAt = Time.time + 30f;
    }

    void SetShown(bool on)
    {
        if (visual != null) visual.gameObject.SetActive(on);
        foreach (Collider c in GetComponents<Collider>()) c.enabled = on;
    }

    protected virtual void OnRestore() { }

    protected virtual void Update()
    {
        if (dead && Time.time >= backAt)
        {
            transform.SetPositionAndRotation(homePos, homeRot);
            health = maxHealth;
            dead = false;
            SetShown(true);
            OnRestore();
        }
    }

    // Prende o piloto no assento; o olhar livre fica por conta do PlayerController.
    protected void SeatDriver()
    {
        if (driver == null) return;
        PlayerController pc = driver.GetComponent<PlayerController>();
        float ly = pc != null ? pc.vehYaw : 0f;
        Quaternion r = transform.rotation * Quaternion.Euler(0f, ly, 0f);
        driver.transform.rotation = r;
        driver.transform.position = transform.TransformPoint(eye) - r * Vector3.up * 1.6f;
    }

    protected bool Mine(Collider c) { return c.transform.IsChildOf(transform) || (driver != null && c.transform.IsChildOf(driver.transform)); }

    protected bool LocalDriver { get { return driver != null && driver.isPlayer && !driver.GetComponent<PlayerController>().paused; } }
}

// Helicoptero de ataque leve, pilotado de dentro da cabine.
// W/S inclinam para frente/tras, A/D para os lados, mouse gira (pedais), ESPACO sobe, CTRL desce.
public class Helicopter : Vehicle
{
    Transform rotor, tail;
    GameObject cockpit;
    float spin, yaw, pitchA, rollA, nextGun, nextRocket;
    int gunSide;
    Vector3 vel;
    static WeaponDef gun;

    public static Helicopter Spawn(Vector3 pos, float yawDeg, int team)
    {
        GameObject g = new GameObject("Helicopter_" + team);
        g.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yawDeg, 0f));
        Helicopter h = g.AddComponent<Helicopter>();
        h.team = team;
        h.Build();
        return h;
    }

    void Build()
    {
        label = "HELICOPTERO";
        maxHealth = health = 900f;
        eye = new Vector3(0.32f, 1.78f, 0.85f);
        lookLimit = 75f;
        yaw = transform.eulerAngles.y;
        Home();
        visual = new GameObject("visual").transform;
        visual.SetParent(transform, false);
        Color paint = Color.Lerp(new Color(0.42f, 0.4f, 0.3f), Art.TeamColor(team), 0.22f);
        WeaponArt.Part("Heli_Body", visual, paint);
        rotor = WeaponArt.Part("Heli_Rotor", visual, paint).transform;
        rotor.localPosition = new Vector3(0f, 2.95f, 0.2f);
        tail = WeaponArt.Part("Heli_Tail", visual, paint).transform;
        tail.localPosition = new Vector3(0.12f, 2.3f, -4.6f);
        cockpit = WeaponArt.Part("Heli_Cockpit", visual, Art.Orange);
        cockpit.SetActive(false);
        foreach (Renderer r in cockpit.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        Art.Box(visual, new Vector3(0f, 3.3f, -4.6f), new Vector3(0.12f, 0.12f, 0.12f), Art.Glow(Art.TeamColor(team), 2f));
        BoxCollider b = gameObject.AddComponent<BoxCollider>();
        b.center = new Vector3(0f, 1.45f, 0.3f); b.size = new Vector3(1.9f, 2.3f, 4.2f);
        BoxCollider b2 = gameObject.AddComponent<BoxCollider>();
        b2.center = new Vector3(0f, 1.9f, -3.2f); b2.size = new Vector3(0.5f, 0.9f, 3.2f);
        Engine("heli");
        if (gun == null) gun = new WeaponDef { name = "METRALHADORA", sound = "rifle", damage = 15f, headMult = 1.5f, interval = 0.07f, range = 350f, color = Art.Orange };
    }

    protected override void OnRestore() { vel = Vector3.zero; spin = 0f; pitchA = rollA = 0f; yaw = transform.eulerAngles.y; }

    public override void Enter(Actor a) { base.Enter(a); cockpit.SetActive(a.isPlayer); }
    public override void Exit() { cockpit.SetActive(false); base.Exit(); }

    protected override void Update()
    {
        base.Update();
        if (dead) { engine.volume = 0f; return; }
        float dt = Time.deltaTime;
        if (dt <= 0f) return;
        bool on = driver != null;
        bool ctl = LocalDriver;
        spin = Mathf.MoveTowards(spin, on ? 1f : 0f, dt / (on ? 2.5f : 5f));
        rotor.Rotate(0f, spin * 1500f * dt, 0f, Space.Self);
        tail.Rotate(spin * 2200f * dt, 0f, 0f, Space.Self);
        engine.volume = spin * 0.8f * Settings.sfx;
        engine.pitch = 0.5f + spin * 0.55f;

        Vector2 mv = ctl ? GameInput.Move : Vector2.zero;
        float lift = ctl ? (GameInput.JumpHeld ? 1f : 0f) - (GameInput.Down ? 1f : 0f) : 0f;
        bool ready = spin > 0.95f;
        if (ctl) yaw += GameInput.Look.x * Settings.sensitivity * 0.55f;
        float k = 1f - Mathf.Exp(-2.6f * dt);
        pitchA = Mathf.Lerp(pitchA, ready ? mv.y * 20f : 0f, k);
        rollA = Mathf.Lerp(rollA, ready ? -mv.x * 20f : 0f, k);
        Quaternion rot = Quaternion.Euler(0f, yaw, 0f) * Quaternion.Euler(pitchA, 0f, rollA);
        transform.rotation = rot;

        // Sustentacao: com o rotor em giro, ESPACO/CTRL mandam na velocidade vertical; sem giro, cai.
        Vector3 up = rot * Vector3.up;
        float wantVy = ready ? lift * 9f : (on ? -1.5f : -9f);
        vel.y = Mathf.Lerp(vel.y, wantVy, 1f - Mathf.Exp(-(ready ? 2.2f : 0.9f) * dt));
        Vector3 flat = new Vector3(up.x, 0f, up.z);
        Vector3 hv = new Vector3(vel.x, 0f, vel.z);
        if (ready) hv += flat * 30f * dt;
        hv -= hv * 0.38f * dt;
        vel.x = hv.x; vel.z = hv.z;

        // Colisao: varre uma esfera no caminho; batida forte machuca.
        Vector3 center = transform.position + Vector3.up * 1.4f;
        Vector3 step = vel * dt;
        float dist = step.magnitude;
        if (dist > 0.0001f)
        {
            RaycastHit[] hits = Physics.SphereCastAll(center, 1.3f, step / dist, dist + 0.05f, ~0, QueryTriggerInteraction.Ignore);
            int best = -1;
            for (int i = 0; i < hits.Length; i++)
            {
                if (Mine(hits[i].collider) || hits[i].distance <= 0f) continue;
                if (best < 0 || hits[i].distance < hits[best].distance) best = i;
            }
            if (best >= 0)
            {
                Vector3 nrm = hits[best].normal;
                float into = -Vector3.Dot(vel, nrm);
                if (into > 0f)
                {
                    if (into > 9f) { Damage((into - 9f) * 28f, null); Sfx.At("hit", center, 1f, 0.5f); }
                    vel += nrm * into * (nrm.y > 0.7f ? 1f : 1.25f);
                }
                if (nrm.y > 0.7f) { vel.x *= 1f - Mathf.Clamp01(6f * dt); vel.z *= 1f - Mathf.Clamp01(6f * dt); }
                step = vel * dt;
                // Nao atravessa: reduz o passo ao que cabe antes do obstaculo.
                step = Vector3.ProjectOnPlane(step, nrm);
            }
            if (!dead) transform.position += step;
        }
        if (dead) return;
        Vector3 p = transform.position;
        float lim = Arena.W * Arena.Cell * 0.5f + 25f, limZ = Arena.H * Arena.Cell * 0.5f + 25f;
        p.x = Mathf.Clamp(p.x, -lim, lim); p.z = Mathf.Clamp(p.z, -limZ, limZ);
        if (p.y > 70f) { p.y = 70f; if (vel.y > 0f) vel.y = 0f; }
        if (p.y < 0f) { p.y = 0f; if (vel.y < 0f) vel.y = 0f; }
        transform.position = p;

        if (ctl && ready) Guns();
    }

    void Guns()
    {
        Camera cam = driver.GetComponent<PlayerController>().cam;
        if (cam == null) return;
        Vector3 fwd = cam.transform.forward;
        if (GameInput.Fire && Time.time >= nextGun)
        {
            nextGun = Time.time + gun.interval;
            gunSide = -gunSide; if (gunSide == 0) gunSide = 1;
            Vector3 mz = transform.TransformPoint(new Vector3(gunSide * 0.75f, 1.25f, 2.35f));
            bool head, killed;
            driver.lastShotTime = Time.time;
            if (Combat.Hitscan(driver, cam.transform.position + fwd * 2.6f, Combat.SpreadDir(fwd, 0.9f), gun, mz, 1f, out head, out killed)) Game.I.Hitmarker(head, killed);
            Sfx.UI("rifle", 0.5f, 0.8f);
        }
        if (GameInput.Aim && Time.time >= nextRocket)
        {
            nextRocket = Time.time + 1.6f;
            Vector3 mz = transform.TransformPoint(new Vector3(0f, 0.7f, 2.9f));
            Combat.Launch(driver, mz, fwd, 60f, 0f, 110f, 6f, "BAZUCA", new Color(1f, 0.6f, 0.2f), 1f);
            Sfx.UI("rocket", 0.8f);
        }
    }

    void LateUpdate() { SeatDriver(); }

    // Instrumentos da cabine.
    void OnGUI()
    {
        if (driver == null || !driver.isPlayer || Game.I == null || Game.I.menus.IsOpen) return;
        float s = Screen.height / 1080f;
        GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));
        float vw = Screen.width / s, cx = vw / 2f;
        float y = 1080f - 250f;
        Game.Fill(cx - 330f, y, 660f, 96f, new Color(0f, 0f, 0f, 0.6f));
        Vector3 hv = new Vector3(vel.x, 0f, vel.z);
        Gauge(cx - 320f, y, "ALTITUDE", Mathf.RoundToInt(transform.position.y) + " m");
        Gauge(cx - 190f, y, "VELOCIDADE", Mathf.RoundToInt(hv.magnitude * 3.6f) + " km/h");
        Gauge(cx - 60f, y, "SUBIDA", (vel.y >= 0f ? "+" : "") + vel.y.ToString("0.0") + " m/s");
        Gauge(cx + 70f, y, "RUMO", Mathf.RoundToInt(Mathf.Repeat(yaw, 360f)).ToString("000"));
        Gauge(cx + 200f, y, "ROTOR", Mathf.RoundToInt(spin * 100f) + " %");
        Game.Fill(cx - 330f, y + 96f, 660f, 12f, new Color(0f, 0f, 0f, 0.6f));
        float hk = health / maxHealth;
        Game.Fill(cx - 328f, y + 98f, 656f * hk, 8f, hk > 0.35f ? Art.Green : Art.Red);
        // Horizonte artificial
        float hx = cx, hy = y - 70f;
        Game.Fill(hx - 62f, hy - 62f, 124f, 124f, new Color(0f, 0f, 0f, 0.5f));
        Matrix4x4 m = GUI.matrix;
        GUIUtility.RotateAroundPivot(rollA, new Vector2(hx * s, hy * s));
        Game.Fill(hx - 56f, hy + Mathf.Clamp(pitchA * 1.6f, -50f, 50f) - 1.5f, 112f, 3f, Art.Green);
        GUI.matrix = m;
        Game.Fill(hx - 20f, hy - 1f, 14f, 2f, Color.white); Game.Fill(hx + 6f, hy - 1f, 14f, 2f, Color.white);
        string msg = spin < 0.95f ? "LIGANDO O ROTOR..." : "W/S/A/D inclina   MOUSE gira   ESPACO sobe   CTRL desce   TIRO metralhadora   MIRA foguete   F sai";
        Game.Text(cx - 600f, 1080f - 40f, 1200f, 30f, msg, 16, new Color(1f, 1f, 1f, 0.8f), TextAnchor.MiddleCenter);
    }

    static void Gauge(float x, float y, string name, string val)
    {
        Game.Text(x, y + 8f, 120f, 24f, name, 13, new Color(1f, 1f, 1f, 0.55f), TextAnchor.MiddleCenter);
        Game.Text(x, y + 34f, 120f, 50f, val, 26, Art.Green, TextAnchor.MiddleCenter);
    }
}

// Moto: W acelera, S freia/re, A/D viram. Atropela inimigos em velocidade.
public class Bike : Vehicle
{
    CharacterController cc;
    Transform tilt, fork, wheelF, wheelR;
    float speed, yaw, lean, steerVis, vy, nextRam;

    public static Bike Spawn(Vector3 pos, float yawDeg)
    {
        GameObject g = new GameObject("Bike");
        g.transform.SetPositionAndRotation(pos + Vector3.up * 0.1f, Quaternion.Euler(0f, yawDeg, 0f));
        Bike b = g.AddComponent<Bike>();
        b.Build();
        return b;
    }

    void Build()
    {
        label = "MOTO";
        maxHealth = health = 260f;
        eye = new Vector3(0f, 1.42f, -0.28f);
        lookLimit = 120f;
        yaw = transform.eulerAngles.y;
        Home();
        cc = gameObject.AddComponent<CharacterController>();
        cc.height = 1.3f; cc.radius = 0.42f; cc.center = new Vector3(0f, 0.68f, 0f); cc.stepOffset = 0.3f;
        visual = new GameObject("visual").transform;
        visual.SetParent(transform, false);
        tilt = visual;
        Color paint = new Color(0.55f, 0.12f, 0.08f);
        WeaponArt.Part("Bike_Frame", tilt, paint);
        fork = new GameObject("fork").transform;
        fork.SetParent(tilt, false);
        fork.localPosition = new Vector3(0f, 0.33f, 0.78f);
        WeaponArt.Part("Bike_Fork", fork, new Color(1f, 0.95f, 0.8f));
        wheelF = WeaponArt.Part("Bike_Wheel", fork, paint).transform;
        wheelR = WeaponArt.Part("Bike_Wheel", tilt, paint).transform;
        wheelR.localPosition = new Vector3(0f, 0.33f, -0.72f);
        Engine("engine");
    }

    protected override void OnRestore() { speed = 0f; vy = 0f; yaw = transform.eulerAngles.y; }

    protected override void Update()
    {
        base.Update();
        if (dead) { engine.volume = 0f; return; }
        float dt = Time.deltaTime;
        if (dt <= 0f) return;
        bool ctl = LocalDriver;
        Vector2 mv = ctl ? GameInput.Move : Vector2.zero;
        if (mv.y > 0.1f) speed = Mathf.MoveTowards(speed, 25f, 13f * dt);
        else if (mv.y < -0.1f) speed = Mathf.MoveTowards(speed, -5f, (speed > 0f ? 28f : 8f) * dt);
        else speed = Mathf.MoveTowards(speed, 0f, (driver != null ? 3.5f : 12f) * dt);
        float sp = Mathf.Abs(speed);
        if (sp > 0.4f) yaw += mv.x * Mathf.Lerp(150f, 60f, Mathf.Clamp01(sp / 25f)) * Mathf.Sign(speed) * dt;
        lean = Mathf.Lerp(lean, -mv.x * Mathf.Clamp01(sp / 12f) * 30f, 1f - Mathf.Exp(-6f * dt));
        steerVis = Mathf.Lerp(steerVis, mv.x * 24f, 1f - Mathf.Exp(-8f * dt));
        transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        tilt.localRotation = Quaternion.Euler(0f, 0f, lean);
        fork.localRotation = Quaternion.Euler(0f, steerVis, 0f);
        float roll = speed * dt / 0.335f * Mathf.Rad2Deg;
        wheelF.Rotate(roll, 0f, 0f, Space.Self); wheelR.Rotate(roll, 0f, 0f, Space.Self);

        if (cc.isGrounded && vy < 0f) vy = -2f;
        vy -= 22f * dt;
        Vector3 before = transform.position;
        CollisionFlags fl = cc.Move((transform.forward * speed + Vector3.up * vy) * dt);
        if ((fl & CollisionFlags.Sides) != 0)
        {
            float real = Vector3.Dot(transform.position - before, transform.forward) / dt;
            if (sp > 12f && Mathf.Abs(real) < sp * 0.5f) { Damage((sp - 12f) * 6f, null); Sfx.At("hit", transform.position, 1f, 0.6f); }
            speed = Mathf.Lerp(speed, real, 0.6f);
        }
        engine.volume = (driver != null ? 0.35f + 0.4f * Mathf.Clamp01(sp / 25f) : 0f) * Settings.sfx;
        engine.pitch = 0.7f + sp / 14f;

        // Atropelamento
        if (driver != null && sp > 8f && Time.time >= nextRam && Game.I != null)
        {
            Vector3 front = transform.position + transform.forward * Mathf.Sign(speed) * 1.0f + Vector3.up * 0.8f;
            var list = Game.I.actors;
            for (int i = 0; i < list.Count; i++)
            {
                Actor a = list[i];
                if (a == null || !a.alive || a == driver || a.team == driver.team) continue;
                if ((a.ChestPos - front).sqrMagnitude > 1.5f * 1.5f) continue;
                a.externalVel += transform.forward * speed * 0.7f + Vector3.up * 4f;
                bool killed = a.TakeDamage(sp * 5.5f, driver, false, "MOTO");
                if (driver.isPlayer) Game.I.Hitmarker(false, killed);
                speed *= 0.75f;
                nextRam = Time.time + 0.4f;
                break;
            }
        }
    }

    void LateUpdate() { SeatDriver(); }

    void OnGUI()
    {
        if (driver == null || !driver.isPlayer || Game.I == null || Game.I.menus.IsOpen) return;
        float s = Screen.height / 1080f;
        GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));
        float cx = Screen.width / s / 2f;
        Game.Fill(cx - 110f, 1080f - 250f, 220f, 80f, new Color(0f, 0f, 0f, 0.6f));
        Game.Text(cx - 110f, 1080f - 250f, 220f, 56f, Mathf.RoundToInt(Mathf.Abs(speed) * 3.6f) + " km/h", 34, Color.white, TextAnchor.MiddleCenter);
        Game.Fill(cx - 100f, 1080f - 186f, 200f * (health / maxHealth), 8f, Art.Green);
        Game.Text(cx - 400f, 1080f - 40f, 800f, 30f, "W acelera   S freia   A/D vira   F desce da moto", 16, new Color(1f, 1f, 1f, 0.8f), TextAnchor.MiddleCenter);
    }
}

// Jetpack: quem pega ganha combustivel; segurar ESPACO no ar voa.
public class JetpackPickup : MonoBehaviour
{
    GameObject vis;
    float backAt;

    public static JetpackPickup Spawn(Vector3 pos)
    {
        GameObject g = new GameObject("JetpackPickup");
        g.transform.position = pos;
        return g.AddComponent<JetpackPickup>();
    }

    void Start()
    {
        vis = new GameObject("vis");
        vis.transform.SetParent(transform, false);
        vis.transform.localPosition = Vector3.up * 1.1f;
        WeaponArt.Part("Jetpack", vis.transform, new Color(0.7f, 0.5f, 0.1f)).transform.localScale = Vector3.one * 1.3f;
        Art.Prim(PrimitiveType.Cylinder, transform, Vector3.up * 0.02f, new Vector3(1.6f, 0.02f, 1.6f), Art.Glow(Art.Orange, 0.8f), false, "base");
        Art.PointLight(vis.transform, Vector3.zero, Art.Orange, 5f, 1f);
    }

    void Update()
    {
        if (Game.I == null) return;
        if (!vis.activeSelf) { if (Time.time >= backAt) vis.SetActive(true); return; }
        vis.transform.Rotate(0f, 80f * Time.deltaTime, 0f);
        Actor p = Game.I.player;
        if (p == null || !p.alive || p.vehicle != null || Game.I.pc == null) return;
        Vector3 d = p.transform.position - transform.position; d.y = 0f;
        if (d.sqrMagnitude > 1.5f * 1.5f || Game.I.pc.jetFuel > 60f) return;
        Game.I.pc.jetFuel = 100f;
        Game.I.Banner("JETPACK  -  SEGURE ESPACO NO AR PARA VOAR");
        Sfx.UI("pickup", 0.9f);
        vis.SetActive(false);
        backAt = Time.time + 40f;
    }
}
