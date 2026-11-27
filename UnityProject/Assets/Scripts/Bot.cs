using System.Collections.Generic;
using UnityEngine;

// IA dos bots: patrulha pelo grid, persegue o ultimo ponto onde viu um inimigo e luta com strafes e rajadas.
public class Bot : MonoBehaviour
{
    public float aimError = 2.6f;      // graus; menor = mais dificil
    public float reaction = 0.4f;      // segundos ate o primeiro tiro
    public float damageMult = 0.6f;
    public bool customSkill;           // se verdadeiro, nao usa a dificuldade das opcoes
    // Ordens do modo de jogo (bomba): ir ate um ponto e, se preciso, ficar parado nele.
    public bool hasGoal, hold;
    public Vector3 goalPos;
    public bool Fighting { get; private set; }

    Actor actor;
    CharacterController cc;
    WeaponDef weapon;
    float prefRange = 16f;

    Actor target;
    bool targetVisible;
    float firstSeen;
    Vector3 lastSeenPos;
    float lastSeenTime = -99f;

    Vector3 roamGoal;
    float roamUntil;
    List<Vector3> path;
    int pathIdx;
    float repathAt;
    Vector3 pathGoal;

    float nextThink, nextFire, strafeFlip;
    float strafeSign = 1f;
    int burstLeft;
    Vector3 hvel;
    float vy;
    Vector3 stuckPos;
    float stuckCheck;

    void Start()
    {
        actor = GetComponent<Actor>();
        cc = GetComponent<CharacterController>();
        if (!customSkill)
        {
            if (Settings.difficulty == 0) { aimError = 4.6f; reaction = 0.7f; damageMult = 0.4f; }
            else if (Settings.difficulty == 2) { aimError = 1.5f; reaction = 0.25f; damageMult = 0.85f; }
            else { aimError = 2.6f; reaction = 0.4f; damageMult = 0.6f; }
        }
        actor.onRespawn += OnRespawn;
        actor.onDamaged += OnDamaged;
        nextThink = Time.time + Random.Range(0f, 0.2f);
        OnRespawn();
    }

    void OnRespawn()
    {
        float r = Random.value;
        int idx = r < 0.5f ? 0 : (r < 0.7f ? 1 : (r < 0.82f ? 2 : (r < 0.92f ? 3 : 4)));
        weapon = Weapons.All[idx];
        prefRange = idx == 1 ? 7f : (idx == 2 ? 28f : 16f);
        target = null;
        targetVisible = false;
        lastSeenTime = -99f;
        path = null;
        hvel = Vector3.zero;
        vy = 0f;
        burstLeft = 0;
        PickRoam();
        stuckPos = transform.position;
        stuckCheck = Time.time + 1.5f;
    }

    void OnDamaged(Actor from)
    {
        if (from == null || from == actor || targetVisible) return;
        lastSeenPos = from.transform.position;
        lastSeenTime = Time.time;
    }

    void PickRoam()
    {
        // Tende a avancar para o lado inimigo e para o centro do mapa.
        Vector3 best = Arena.RandomPoint();
        float enemyZ = actor != null && actor.team == 0 ? 1f : -1f;
        for (int i = 0; i < 3; i++)
        {
            Vector3 p = Arena.RandomPoint();
            if (p.z * enemyZ > best.z * enemyZ && Random.value < 0.6f) best = p;
        }
        roamGoal = best;
        roamUntil = Time.time + Random.Range(8f, 16f);
        path = null;
    }

    bool CanSee(Actor a)
    {
        Vector3 o = actor.EyePos;
        Vector3 t = a.ChestPos;
        RaycastHit h;
        if (Physics.Linecast(o, t, out h, ~0, QueryTriggerInteraction.Ignore))
            return Combat.ActorOf(h.collider) == a;
        return true;
    }

    void Think()
    {
        Actor best = null;
        float bestD = float.MaxValue;
        var list = Game.I.actors;
        for (int i = 0; i < list.Count; i++)
        {
            Actor a = list[i];
            if (a == null || !a.alive || a.team == actor.team) continue;
            Vector3 to = a.transform.position - transform.position;
            float d = to.magnitude;
            if (d > 75f) continue;
            to.y = 0f;
            bool inFov = d < 9f || Vector3.Angle(transform.forward, to) < 100f || a == target;
            if (!inFov || !CanSee(a)) continue;
            if (a == target) d *= 0.7f; // evita ficar trocando de alvo
            if (d < bestD) { bestD = d; best = a; }
        }
        if (best != null)
        {
            if (best != target || !targetVisible) firstSeen = Time.time;
            target = best;
            targetVisible = true;
            lastSeenPos = best.transform.position;
            lastSeenTime = Time.time;
        }
        else targetVisible = false;
    }

    static float Flat(Vector3 a, Vector3 b)
    {
        float dx = a.x - b.x, dz = a.z - b.z;
        return Mathf.Sqrt(dx * dx + dz * dz);
    }

    Vector3 PathDir(Vector3 goal)
    {
        if (path == null || Time.time >= repathAt || (goal - pathGoal).sqrMagnitude > 16f)
        {
            path = Arena.FindPath(transform.position, goal);
            pathIdx = 0;
            repathAt = Time.time + 1.5f;
            pathGoal = goal;
        }
        while (pathIdx < path.Count && Flat(transform.position, path[pathIdx]) < 0.8f) pathIdx++;
        Vector3 to = (pathIdx < path.Count ? path[pathIdx] : goal) - transform.position;
        to.y = 0f;
        return to.sqrMagnitude > 0.25f ? to.normalized : Vector3.zero;
    }

    void Update()
    {
        if (Game.I == null || !actor.alive || Game.I.matchOver) return;
        if (Time.time < actor.stunnedUntil || Time.time < actor.blindUntil)
        { actor.Animate(0f, Vector3.zero, transform.forward); return; }
        float dt = Time.deltaTime;
        if (Time.time >= nextThink) { nextThink = Time.time + 0.2f; Think(); }

        Vector3 moveDir = Vector3.zero;
        Vector3 lookDir = transform.forward;
        float speed = 4.5f;
        bool fighting = target != null && target.alive && targetVisible;
        Fighting = fighting;

        if (fighting)
        {
            Vector3 to = target.ChestPos - actor.EyePos;
            float dist = to.magnitude;
            lookDir = to;
            if (dist > prefRange * 1.4f) moveDir = PathDir(target.transform.position);
            else
            {
                if (Time.time >= strafeFlip)
                {
                    strafeSign = Random.value < 0.5f ? -1f : 1f;
                    strafeFlip = Time.time + Random.Range(0.6f, 1.5f);
                }
                moveDir = transform.right * strafeSign;
                if (dist < prefRange * 0.5f) moveDir -= transform.forward * 0.8f;
                speed = 3.5f;
            }
            TryFire(to, dist);
        }
        else
        {
            bool chasing = Time.time - lastSeenTime < 5f;
            bool ordered = hasGoal && !chasing;
            Vector3 goal = chasing ? lastSeenPos : (hasGoal ? goalPos : roamGoal);
            if (!ordered && (Flat(transform.position, goal) < 2f || (!chasing && Time.time > roamUntil)))
            {
                lastSeenTime = -99f;
                PickRoam();
                goal = hasGoal ? goalPos : roamGoal;
            }
            if (ordered && (hold || Flat(transform.position, goal) < 0.9f)) moveDir = Vector3.zero;
            else moveDir = PathDir(goal);
            if (moveDir.sqrMagnitude > 0.01f) lookDir = moveDir;
        }

        Vector3 flat = new Vector3(lookDir.x, 0f, lookDir.z);
        if (flat.sqrMagnitude > 0.001f)
            transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(flat), (fighting ? 420f : 300f) * dt);

        if (cc.isGrounded && vy < 0f) vy = -2f;
        if (actor.pendingLaunch > 0f) { vy = actor.pendingLaunch; actor.pendingLaunch = 0f; }
        vy -= 22f * dt;

        moveDir.y = 0f;
        if (moveDir.sqrMagnitude > 1f) moveDir.Normalize();
        hvel = Vector3.Lerp(hvel, moveDir * (speed * actor.MoveSpeedMult), 1f - Mathf.Exp(-10f * dt));
        cc.Move((hvel + Vector3.up * vy + actor.externalVel) * dt);
        Vector3 hv = new Vector3(hvel.x, 0f, hvel.z);
        actor.Animate(hv.magnitude, hv, fighting ? lookDir : transform.forward);

        // Destrava se ficar preso em alguma quina.
        if (Time.time >= stuckCheck)
        {
            if (!fighting && Flat(transform.position, stuckPos) < 0.5f)
            {
                lastSeenTime = -99f;
                PickRoam();
            }
            else if (fighting && Flat(transform.position, stuckPos) < 0.3f) strafeSign = -strafeSign;
            stuckPos = transform.position;
            stuckCheck = Time.time + 1.5f;
        }
    }

    void TryFire(Vector3 to, float dist)
    {
        if (Time.time - firstSeen < reaction) return;
        if (dist > weapon.range) return;
        Vector3 flat = new Vector3(to.x, 0f, to.z);
        if (Vector3.Angle(transform.forward, flat) > 14f) return;
        if (Time.time < nextFire) return;

        if (burstLeft <= 0)
        {
            burstLeft = weapon.auto ? Random.Range(3, 8) : 1;
            nextFire = Time.time + (weapon.auto ? Random.Range(0.3f, 0.7f) : weapon.interval * Random.Range(1.1f, 1.6f));
            return;
        }
        burstLeft--;
        nextFire = Time.time + weapon.interval * 1.15f;
        actor.lastShotTime = Time.time;

        Vector3 eye = actor.EyePos;
        Vector3 muzzle = actor.body != null && actor.body.ok ? actor.body.Muzzle : transform.position + transform.rotation * new Vector3(0.3f, 1.3f, 0.9f);
        float err = aimError + (target.velocity.magnitude > 3f ? 1.3f : 0f);

        if (weapon.projectile)
        {
            float speedP = weapon.projSpeed;
            Vector3 aimAt = target.transform.position + Vector3.up * 0.3f + target.velocity * (dist / speedP) * 0.6f;
            Vector3 dir = Combat.SpreadDir((aimAt - eye).normalized, err * 0.7f);
            Combat.Launch(actor, eye + dir * 0.7f, dir, speedP, 0f, weapon.damage, weapon.projRadius, weapon.name, weapon.color, damageMult);
        }
        else
        {
            Vector3 aimAt = Random.value < 0.15f ? target.HeadPos : target.ChestPos;
            Vector3 baseDir = (aimAt - eye).normalized;
            for (int i = 0; i < weapon.pellets; i++)
            {
                bool head, killed;
                Combat.Hitscan(actor, eye, Combat.SpreadDir(baseDir, err + weapon.spread * 0.5f), weapon, muzzle, damageMult, out head, out killed);
            }
        }
        Sfx.At(weapon.sound, muzzle, 0.7f);
    }
}
