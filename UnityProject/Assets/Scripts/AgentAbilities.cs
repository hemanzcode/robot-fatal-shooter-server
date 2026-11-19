using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Habilidades dos agentes descritas em "Descrição de personagens fatal shooter.txt".
public static class AgentAbilities
{
    public static bool HasCustomSkill(int agent) { return agent >= 0 && agent <= 6; }
    public static bool HasSecondarySkill(int agent) { return agent >= 0 && agent <= 4; }

    public static void UseSkill(PlayerController pc)
    {
        if (pc == null || pc.actorForAbilities == null) return;
        pc.stats.nades++;
        if (NetGame.IsClient) { NetGame.SendAgentAbility(false); return; }
        Execute(pc.actorForAbilities, false, pc.cam.transform.forward);
    }

    public static void UseUltimate(PlayerController pc)
    {
        if (pc == null || pc.actorForAbilities == null) return;
        if (NetGame.IsClient) { NetGame.SendAgentAbility(true); return; }
        pc.ult = 0f;
        pc.stats.ults++;
        Execute(pc.actorForAbilities, true, pc.cam.transform.forward);
    }

    public static void UseSecondarySkill(PlayerController pc)
    {
        if (pc == null || pc.actorForAbilities == null || !HasSecondarySkill(pc.actorForAbilities.agent)) return;
        if (NetGame.IsClient) { NetGame.SendAgentAbility(false, true); return; }
        Execute(pc.actorForAbilities, false, pc.cam.transform.forward, 1);
    }

    public static void Execute(Actor owner, bool ultimate, Vector3 direction, int slot = 0)
    {
        if (owner == null || Game.I == null || !owner.alive) return;
        if (direction.sqrMagnitude < 0.01f) direction = owner.transform.forward;
        direction.Normalize();
        int agent = Mathf.Abs(owner.agent) % AgentBody.Defs.Length;
        if (ultimate) ExecuteUltimate(owner, agent, direction);
        else if (slot == 1) ExecuteSecondarySkill(owner, agent, direction);
        else ExecuteSkill(owner, agent, direction);
    }

    static void ExecuteSecondarySkill(Actor owner, int agent, Vector3 direction)
    {
        switch (agent)
        {
            case 0: // Jason: granada de dispersao com cegamento curto
            {
                Vector3 point = AimPoint(owner, direction, 22f);
                BlindEnemiesAt(owner, point, 8f, 2.5f);
                Fx.Explosion(point, Art.Cyan, 3.5f);
                Game.I.Announce("JASON: GRANADA DE DISPERSAO", Art.Cyan);
                break;
            }
            case 1: // Todd: molotov menor; Q segue sendo a poça de lava
            {
                Vector3 point = AimPoint(owner, direction, 22f);
                AgentHazardField.Spawn(owner, point, 3.2f, 4f, 20f, new Color(1f, 0.25f, 0.03f));
                Game.I.Announce("TODD: MOLOTOV", Art.Orange);
                break;
            }
            case 2: // Kate: drone que lança uma granada
            {
                Vector3 target = AimPoint(owner, direction, 32f);
                AgentAbilityRuntime.StartAirstrikeBomb(owner, owner.EyePos + Vector3.up * 8f, target);
                Game.I.Announce("KATE: DRONE BOMBARDEIRO", Art.Yellow);
                break;
            }
            case 3: // Harold: meca, separado do escaneamento Q
                Mech.Spawn(owner.transform.position + direction * 5f, owner.team, NetGame.IsClient);
                Game.I.Announce("HAROLD: MECA ENVIADO", Art.Green);
                break;
            case 4: // Death Girl: cura propria, separada do clarão Q
                owner.health = Mathf.Min(owner.maxHealth, owner.health + 55f);
                Fx.Explosion(owner.EyePos, new Color(1f, 0.3f, 0.75f), 2.2f);
                Game.I.Announce("DEATH GIRL: AUTOCURA", new Color(1f, 0.3f, 0.75f));
                break;
        }
    }

    static void BlindEnemiesAt(Actor owner, Vector3 center, float radius, float duration)
    {
        foreach (Actor a in Game.I.actors)
        {
            if (!Enemy(owner, a) || Vector3.Distance(a.transform.position, center) > radius) continue;
            a.blindUntil = Time.time + duration;
            NetGame.BroadcastAgentStatus(a, "blind", duration);
        }
    }

    static Vector3 AimPoint(Actor owner, Vector3 direction, float range)
    {
        Vector3 origin = owner.EyePos;
        PlayerController pc = owner.GetComponent<PlayerController>();
        if (pc != null && pc.cam != null) origin = pc.cam.transform.position;
        RaycastHit hit;
        return Physics.Raycast(origin, direction, out hit, range, ~0, QueryTriggerInteraction.Ignore)
            ? hit.point : origin + direction * range;
    }

    static void ExecuteSkill(Actor owner, int agent, Vector3 direction)
    {
        switch (agent)
        {
            case 0: // Jason: drone de reconhecimento
                foreach (Actor a in Game.I.actors)
                    if (Enemy(owner, a) && Vector3.Distance(owner.transform.position, a.transform.position) <= 60f) { a.revealedUntil = Time.time + 8f; NetGame.BroadcastAgentStatus(a, "reveal", 8f); }
                Game.I.Announce("DRONE DE JASON: INIMIGOS REVELADOS", Art.Cyan);
                break;
            case 1: // Todd: molotov e poça de lava
            {
                Vector3 point = AimPoint(owner, direction, 24f);
                AgentHazardField.Spawn(owner, point, 4.2f, 5f, 18f, new Color(1f, 0.25f, 0.03f));
                Game.I.Announce("MOLOTOV: AREA EM CHAMAS", Art.Orange);
                break;
            }
            case 2: // Kate: três minas
                for (int i = 0; i < 3; i++)
                {
                    Vector3 side = Vector3.Cross(Vector3.up, direction).normalized * ((i - 1) * 1.4f);
                    ProximityMine.Spawn(owner, owner.transform.position + direction * 2.2f + side);
                }
                Game.I.Announce("KATE: 3 MINAS ARMADAS", Art.Yellow);
                break;
            case 3: // Harold: câmeras e apoio do meca
                foreach (Actor a in Game.I.actors)
                    if (Enemy(owner, a)) { a.revealedUntil = Time.time + 8f; NetGame.BroadcastAgentStatus(a, "reveal", 8f); }
                Game.I.Announce("HAROLD: CAMERAS ATIVAS", Art.Green);
                break;
            case 4: // Death Girl: clarão e autocura
                BlindEnemies(owner, 9f, 3f);
                Fx.Explosion(owner.EyePos, new Color(1f, 0.3f, 0.75f), 3.2f);
                Game.I.Announce("DEATH GIRL: CLARAO", new Color(1f, 0.3f, 0.75f));
                break;
            case 5: // Aurora: impulso curto, além dos passivos de mobilidade
                PlayerController aurora = owner.GetComponent<PlayerController>();
                if (aurora != null) aurora.speedBoostUntil = Time.time + 5f;
                owner.externalVel += direction * 12f + Vector3.up * 2f;
                Game.I.Announce("AURORA: IMPULSO", Art.Cyan);
                break;
            case 6: // Brutus: onda sonora direcional
                SonicWave(owner, direction, 13f, 55f, 32f, 2.2f);
                Game.I.Announce("BRUTUS: ONDA SONORA", Art.Red);
                break;
            default:
                PlayerController pcDefault = owner.GetComponent<PlayerController>();
                if (pcDefault != null) GenericGrenade(pcDefault, direction);
                break;
        }
    }

    static void ExecuteUltimate(Actor owner, int agent, Vector3 direction)
    {
        switch (agent)
        {
            case 0: // Jason: flash em raio de 20 m por 10 s
                BlindEnemies(owner, 20f, 10f);
                Fx.Explosion(owner.EyePos, Art.Cyan, 8f);
                Game.I.Announce("ULTIMATE: FLASH DE JASON", Art.Cyan);
                break;
            case 1: // Todd: lança-chamas por 10 s
                AgentAbilityRuntime.StartFlamethrower(owner, direction, 10f);
                Game.I.Announce("ULTIMATE: LANCA-CHAMAS", Art.Orange);
                break;
            case 2: // Kate: bombardeio no ponto visado
            {
                Vector3 target = AimPoint(owner, direction, 55f);
                for (int i = 0; i < 3; i++)
                {
                    Vector3 p = target + new Vector3((i - 1) * 4f, 13f, (i % 2 == 0 ? 2f : -2f));
                    AgentAbilityRuntime.StartAirstrikeBomb(owner, p, target + new Vector3((i - 1) * 2f, 0f, 0f));
                }
                Game.I.Announce("ULTIMATE: BOMBARDEIO AEREO", Art.Yellow);
                break;
            }
            case 3: // Harold: cão de plasma
                PlasmaHound.Spawn(owner);
                Game.I.Announce("ULTIMATE: CAO DE PLASMA", Art.Green);
                break;
            case 4: // Death Girl: intangibilidade
                owner.invulnerableUntil = Time.time + 6f;
                NetGame.BroadcastAgentStatus(owner, "invuln", 6f);
                Game.I.Announce("ULTIMATE: INTANGIVEL POR 6 S", new Color(1f, 0.3f, 0.75f));
                break;
            case 5: // Aurora: três lanças
                ThreeSpears(owner, direction);
                Game.I.Announce("ULTIMATE: LANCA MORTAL", Art.Cyan);
                break;
            case 6: // Brutus: terremoto de raio 10 m
                DamageRadius(owner, owner.transform.position, 10f, 110f, "TERREMOTO");
                foreach (Actor a in Game.I.actors)
                    if (Enemy(owner, a) && Vector3.Distance(owner.transform.position, a.transform.position) <= 10f)
                    { a.stunnedUntil = Time.time + 1.8f; NetGame.BroadcastAgentStatus(a, "stun", 1.8f); a.externalVel += (a.transform.position - owner.transform.position).normalized * 16f + Vector3.up * 7f; }
                Fx.Blast(owner.transform.position, 10f);
                Game.I.Announce("ULTIMATE: TERREMOTO", Art.Red);
                break;
            default: // mantém a sobrecarga para os agentes sem ultimate definido
            {
                PlayerController pc = owner.GetComponent<PlayerController>();
                if (pc != null) { pc.overUntil = Time.time + 8f; owner.health = Mathf.Min(owner.maxHealth, owner.health + 80f); }
                break;
            }
        }
    }

    static bool Enemy(Actor owner, Actor target)
    { return target != null && target != owner && target.alive && !target.creature && target.team != owner.team; }

    static void BlindEnemies(Actor owner, float radius, float duration)
    {
        foreach (Actor a in Game.I.actors)
        {
            if (!Enemy(owner, a) || Vector3.Distance(owner.transform.position, a.transform.position) > radius) continue;
            a.blindUntil = Time.time + duration;
            NetGame.BroadcastAgentStatus(a, "blind", duration);
        }
    }

    public static void DamageAirstrike(Actor owner, Vector3 point) { DamageRadius(owner, point, 5f, 72f, "BOMBARDEIO"); }
    public static void DamageMine(Actor owner, Vector3 point) { DamageRadius(owner, point, 3.5f, 82f, "MINA"); }
    public static void DamageField(Actor owner, Vector3 point, float radius, float damage) { DamageRadius(owner, point, radius, damage, "LAVA"); }

    static void SonicWave(Actor owner, Vector3 direction, float radius, float angle, float damage, float stun)
    {
        foreach (Actor a in Game.I.actors)
        {
            if (!Enemy(owner, a)) continue;
            Vector3 to = a.ChestPos - owner.EyePos;
            if (to.magnitude > radius || Vector3.Angle(direction, to) > angle) continue;
            a.stunnedUntil = Time.time + stun;
            NetGame.BroadcastAgentStatus(a, "stun", stun);
            bool killed; Combat.HitActor(owner, a, a.ChestPos, damage, 1, "ONDA SONORA", out killed);
            a.externalVel += to.normalized * 7f;
            Fx.Tracer(owner.EyePos, a.ChestPos, Art.Red, 0.08f, 0.16f);
        }
    }

    static void ThreeSpears(Actor owner, Vector3 direction)
    {
        HashSet<Actor> hit = new HashSet<Actor>();
        for (int i = -1; i <= 1; i++)
        {
            Vector3 ray = Quaternion.AngleAxis(i * 9f, Vector3.up) * direction;
            RaycastHit h;
            Vector3 end = owner.EyePos + ray * 65f;
            if (Physics.Raycast(owner.EyePos, ray, out h, 65f, ~0, QueryTriggerInteraction.Ignore))
            {
                end = h.point;
                Actor a = Combat.ActorOf(h.collider);
                if (Enemy(owner, a) && hit.Add(a)) { bool killed; Combat.HitActor(owner, a, h.point, 90f, Combat.Region(a, h.point), "LANCA", out killed); }
            }
            Fx.Tracer(owner.EyePos, end, Art.Cyan, 0.1f, 0.22f);
        }
    }

    static void DamageRadius(Actor owner, Vector3 pos, float radius, float damage, string weapon)
    {
        foreach (Actor a in Game.I.actors)
        {
            if (!Enemy(owner, a)) continue;
            float d = Vector3.Distance(pos, a.ChestPos);
            if (d > radius) continue;
            float k = 1f - Mathf.Clamp01(d / radius);
            bool killed; Combat.HitActor(owner, a, a.ChestPos, damage * k, 0, weapon, out killed);
        }
    }

    static void GenericGrenade(PlayerController pc, Vector3 direction)
    {
        Combat.Launch(pc.actorForAbilities, pc.cam.transform.position + direction * 0.7f, direction + Vector3.up * 0.12f,
            24f, 16f, 85f, 6f, "GRANADA", Art.Green, 1f);
        pc.stats.nades++;
    }
}

public class AgentAbilityRuntime : MonoBehaviour
{
    public static void StartFlamethrower(Actor owner, Vector3 direction, float duration)
    {
        if (Game.I == null) return;
        AgentAbilityRuntime r = Game.I.GetComponent<AgentAbilityRuntime>();
        if (r == null) r = Game.I.gameObject.AddComponent<AgentAbilityRuntime>();
        r.StartCoroutine(r.Flame(owner, direction, duration));
    }

    IEnumerator Flame(Actor owner, Vector3 direction, float duration)
    {
        float end = Time.time + duration;
        while (owner != null && owner.alive && Time.time < end)
        {
            Vector3 origin = owner.EyePos;
            PlayerController pc = owner.GetComponent<PlayerController>(); if (pc != null && pc.cam != null) origin = pc.cam.transform.position;
            Vector3 finish = origin + direction * 14f;
            RaycastHit h;
            if (Physics.SphereCast(origin, 1.1f, direction, out h, 14f, ~0, QueryTriggerInteraction.Ignore))
            {
                finish = h.point; Actor a = Combat.ActorOf(h.collider);
                if (a != null && a != owner && a.team != owner.team) { bool killed; Combat.HitActor(owner, a, h.point, 13f, Combat.Region(a, h.point), "LANCA-CHAMAS", out killed); }
            }
            Fx.Tracer(origin, finish, new Color(1f, 0.32f, 0.04f), 0.22f, 0.14f);
            yield return new WaitForSeconds(0.25f);
        }
    }

    public static void StartAirstrikeBomb(Actor owner, Vector3 from, Vector3 target)
    {
        if (Game.I == null) return;
        AgentAbilityRuntime r = Game.I.GetComponent<AgentAbilityRuntime>();
        if (r == null) r = Game.I.gameObject.AddComponent<AgentAbilityRuntime>();
        r.StartCoroutine(r.Bomb(owner, from, target));
    }

    IEnumerator Bomb(Actor owner, Vector3 from, Vector3 target)
    {
        float t = 0f;
        while (t < 0.55f)
        {
            t += Time.deltaTime;
            Vector3 p = Vector3.Lerp(from, target, t / 0.55f);
            Fx.Tracer(p + Vector3.up * 0.5f, p, Art.Orange, 0.04f, 0.05f);
            yield return null;
        }
        Fx.Blast(target, 5f);
        AgentAbilities.DamageAirstrike(owner, target);
    }
}

public class ProximityMine : MonoBehaviour
{
    Actor owner;
    float armAt;
    public static void Spawn(Actor owner, Vector3 position)
    {
        GameObject g = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        g.name = "KateMine"; g.transform.position = position + Vector3.up * 0.15f; g.transform.localScale = new Vector3(0.55f, 0.18f, 0.55f);
        Collider c = g.GetComponent<Collider>(); if (c != null) Destroy(c);
        Renderer r = g.GetComponent<Renderer>(); r.sharedMaterial = Art.Glow(Art.Yellow, 1.5f);
        ProximityMine mine = g.AddComponent<ProximityMine>(); mine.owner = owner; mine.armAt = Time.time + 1f;
    }
    void Update()
    {
        if (owner == null || !owner.alive) { Destroy(gameObject); return; }
        if (Time.time < armAt || Game.I == null) return;
        foreach (Actor a in Game.I.actors)
        {
            if (a == null || !a.alive || a.team == owner.team || a.creature || Vector3.Distance(transform.position, a.transform.position) > 2f) continue;
            Fx.Blast(transform.position, 3.5f);
            AgentAbilities.DamageMine(owner, transform.position);
            Destroy(gameObject); return;
        }
    }
}

public class AgentHazardField : MonoBehaviour
{
    Actor owner; float radius, until, nextTick, damage; Color color;
    public static void Spawn(Actor source, Vector3 position, float range, float seconds, float dps, Color tint)
    {
        GameObject g = GameObject.CreatePrimitive(PrimitiveType.Cylinder); g.name = "LavaField"; g.transform.position = position + Vector3.up * 0.04f;
        g.transform.localScale = new Vector3(range * 0.45f, 0.025f, range * 0.45f);
        Collider c = g.GetComponent<Collider>(); if (c != null) Destroy(c);
        Renderer r = g.GetComponent<Renderer>(); r.sharedMaterial = Art.Glow(tint, 0.8f);
        AgentHazardField f = g.AddComponent<AgentHazardField>(); f.owner = source; f.radius = range; f.until = Time.time + seconds; f.damage = dps * 0.5f; f.color = tint;
    }
    void Update()
    {
        if (owner == null || Time.time >= until) { Destroy(gameObject); return; }
        if (Time.time < nextTick || Game.I == null) return;
        nextTick = Time.time + 0.5f;
        AgentAbilities.DamageField(owner, transform.position, radius, damage);
        Fx.Explosion(transform.position, color, radius * 0.15f);
    }
}

public class PlasmaHound : MonoBehaviour
{
    Actor owner; float until, nextShot; Renderer body; Transform head;
    public static void Spawn(Actor source)
    {
        if (source == null) return;
        GameObject g = new GameObject("PlasmaHound"); g.transform.position = source.transform.position + source.transform.forward * 1.3f;
        PlasmaHound h = g.AddComponent<PlasmaHound>(); h.owner = source; h.until = Time.time + 12f; h.Build();
    }
    void Build()
    {
        Material mat = Art.Mat(new Color(0.16f, 0.2f, 0.18f), Color.black, 0.2f, default(Color), 0.4f, 0.6f);
        Material glow = Art.Glow(Art.Green, 1.8f);
        GameObject torso = GameObject.CreatePrimitive(PrimitiveType.Capsule); torso.name = "houndCore"; torso.transform.SetParent(transform, false); torso.transform.localPosition = new Vector3(0f, 0.48f, 0f); torso.transform.localScale = new Vector3(0.38f, 0.5f, 0.34f); body = torso.GetComponent<Renderer>(); body.sharedMaterial = mat; Destroy(torso.GetComponent<Collider>());
        GameObject skull = GameObject.CreatePrimitive(PrimitiveType.Sphere); skull.name = "houndHead"; skull.transform.SetParent(transform, false); skull.transform.localPosition = new Vector3(0f, 0.57f, 0.35f); skull.transform.localScale = new Vector3(0.3f, 0.26f, 0.36f); head = skull.transform; skull.GetComponent<Renderer>().sharedMaterial = glow; Destroy(skull.GetComponent<Collider>());
        for (int x = -1; x <= 1; x += 2) for (int z = -1; z <= 1; z += 2)
        {
            GameObject leg = GameObject.CreatePrimitive(PrimitiveType.Capsule); leg.transform.SetParent(transform, false); leg.transform.localPosition = new Vector3(x * 0.18f, 0.2f, z * 0.18f); leg.transform.localScale = new Vector3(0.1f, 0.24f, 0.1f); leg.GetComponent<Renderer>().sharedMaterial = mat; Destroy(leg.GetComponent<Collider>());
        }
    }
    void Update()
    {
        if (owner == null || !owner.alive || Time.time >= until || Game.I == null) { Destroy(gameObject); return; }
        Actor target = null; float best = 30f;
        foreach (Actor a in Game.I.actors) if (a != null && a.alive && a.team != owner.team && !a.creature)
        { float d = Vector3.Distance(transform.position, a.ChestPos); if (d < best) { best = d; target = a; } }
        if (target == null) return;
        Vector3 to = target.ChestPos - transform.position; transform.rotation = Quaternion.LookRotation(new Vector3(to.x, 0f, to.z));
        if (Time.time < nextShot) return;
        nextShot = Time.time + 0.8f;
        RaycastHit hit;
        if (Physics.Raycast(transform.position + Vector3.up * 0.5f, to.normalized, out hit, best, ~0, QueryTriggerInteraction.Ignore))
        {
            Actor a = Combat.ActorOf(hit.collider);
            if (a == target) { bool killed; Combat.HitActor(owner, target, hit.point, 18f, Combat.Region(target, hit.point), "CAO DE PLASMA", out killed); }
            Fx.Tracer(transform.position + Vector3.up * 0.5f, hit.point, Art.Green, 0.06f, 0.12f);
        }
    }
}
