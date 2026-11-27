using System.Collections.Generic;
using UnityEngine;

public class WeaponDef
{
    public string name;
    public string sound;
    public float damage;
    public float headMult = 2f;     // cabeca
    public float chestMult = 1f;    // peito e pescoco
    public float interval;
    public int magSize;
    public int reserve;
    public float reloadTime;
    public float spread;        // graus, atirando da cintura
    public float adsSpread;     // graus, mirando
    public float bloom;         // graus somados por tiro seguido
    public float recoilPitch;
    public float recoilYaw;
    public int pellets = 1;
    public float range = 200f;
    public bool auto;
    public bool projectile;
    public float projSpeed = 40f;
    public float projRadius = 5.5f;
    public float zoomFov = 65f;
    public float sprintToFire = 0.38f;
    public float dropoffStart = 15f;
    public float dropoffEnd = 40f;
    public float minDamageMult = 0.5f;
    public Color color;
}

public static class Weapons
{
    // A tabela usada depende do modo de jogo (TATICO ou DIVERSAO).
    public static WeaponDef[] All { get { return PlayerController.TacticalMode ? Tactical : Classic; } }

    // DIVERSAO: os valores classicos. Pulso perde forca com a distancia como antes; as outras armas, dano cheio.
    public static readonly WeaponDef[] Classic =
    {
        new WeaponDef { name = "VETOR", sound = "rifle", damage = 25f, headMult = 2f, chestMult = 1.2f, interval = 0.095f, magSize = 30, reserve = 120, reloadTime = 1.9f,
                        spread = 0.5f, adsSpread = 0.08f, bloom = 0.26f, recoilPitch = 0.55f, recoilYaw = 0.35f, auto = true, zoomFov = 60f,
                        sprintToFire = 0f, dropoffStart = 9999f, dropoffEnd = 10000f, minDamageMult = 1f, color = Art.Cyan },
        new WeaponDef { name = "PULSO", sound = "shotgun", damage = 10f, headMult = 1.3f, chestMult = 1.1f, interval = 0.8f, magSize = 6, reserve = 30, reloadTime = 2.3f,
                        spread = 4.2f, adsSpread = 3.2f, bloom = 0f, recoilPitch = 3.5f, recoilYaw = 1f, pellets = 9, range = 45f, zoomFov = 72f,
                        sprintToFire = 0f, dropoffStart = 6f, dropoffEnd = 45f, minDamageMult = 0.3f, color = Art.Orange },
        new WeaponDef { name = "SNIPER", sound = "sniper", damage = 80f, headMult = 2f, chestMult = 1.15f, interval = 1.5f, magSize = 5, reserve = 20, reloadTime = 3.1f,
                        spread = 3.2f, adsSpread = 0f, bloom = 0f, recoilPitch = 6.5f, recoilYaw = 1.1f, range = 600f, zoomFov = 18f,
                        sprintToFire = 0f, dropoffStart = 9999f, dropoffEnd = 10000f, minDamageMult = 1f, color = new Color(1f, 0.85f, 0.55f) },
        new WeaponDef { name = "BAZUCA", sound = "rocket", damage = 90f, interval = 1f, magSize = 3, reserve = 9, reloadTime = 2.8f,
                        spread = 0.3f, adsSpread = 0.1f, bloom = 0f, recoilPitch = 3f, recoilYaw = 0.5f, projectile = true, projSpeed = 48f, projRadius = 6.5f, zoomFov = 72f,
                        sprintToFire = 0f, dropoffStart = 9999f, dropoffEnd = 10000f, minDamageMult = 1f, color = new Color(1f, 0.6f, 0.2f) },
        new WeaponDef { name = "PLASMA", sound = "rail", damage = 35f, headMult = 1.5f, chestMult = 1.15f, interval = 0.15f, magSize = 40, reserve = 160, reloadTime = 2.2f,
                        spread = 0.6f, adsSpread = 0.2f, bloom = 0.05f, recoilPitch = 0.5f, recoilYaw = 0.3f, auto = true, projectile = true, projSpeed = 75f, projRadius = 1.8f, zoomFov = 65f,
                        sprintToFire = 0f, dropoffStart = 9999f, dropoffEnd = 10000f, minDamageMult = 1f, color = Art.Green }
    };

    // TATICO: TTK curto, queda de dano com a distancia e atraso para atirar depois de correr.
    public static readonly WeaponDef[] Tactical =
    {
        new WeaponDef { name = "VETOR", sound = "rifle", damage = 26f, headMult = 2f, chestMult = 1.1f, interval = 0.095f, magSize = 30, reserve = 120, reloadTime = 1.9f,
                        spread = 0.5f, adsSpread = 0.08f, bloom = 0.26f, recoilPitch = 0.55f, recoilYaw = 0.35f, auto = true, zoomFov = 60f,
                        sprintToFire = 0.35f, dropoffStart = 12f, dropoffEnd = 25f, minDamageMult = 0.55f, color = Art.Cyan },
        new WeaponDef { name = "PULSO", sound = "shotgun", damage = 8f, headMult = 2f, chestMult = 1.1f, interval = 0.8f, magSize = 6, reserve = 30, reloadTime = 2.3f,
                        spread = 4.2f, adsSpread = 3.2f, bloom = 0f, recoilPitch = 3.5f, recoilYaw = 1f, pellets = 9, range = 45f, zoomFov = 72f,
                        sprintToFire = 0.38f, dropoffStart = 6f, dropoffEnd = 20f, minDamageMult = 0.3f, color = Art.Orange },
        new WeaponDef { name = "SNIPER", sound = "sniper", damage = 80f, headMult = 2f, chestMult = 1.15f, interval = 1.35f, magSize = 5, reserve = 20, reloadTime = 3.1f,
                        spread = 3.2f, adsSpread = 0f, bloom = 0f, recoilPitch = 6.5f, recoilYaw = 1.1f, range = 600f, zoomFov = 18f,
                        sprintToFire = 0.45f, dropoffStart = 100f, dropoffEnd = 300f, minDamageMult = 0.85f, color = new Color(1f, 0.85f, 0.55f) },
        new WeaponDef { name = "BAZUCA", sound = "rocket", damage = 95f, headMult = 2f, interval = 1f, magSize = 3, reserve = 9, reloadTime = 2.8f,
                        spread = 0.3f, adsSpread = 0.1f, bloom = 0f, recoilPitch = 3f, recoilYaw = 0.5f, projectile = true, projSpeed = 48f, projRadius = 6.5f, zoomFov = 72f,
                        sprintToFire = 0.45f, dropoffStart = 30f, dropoffEnd = 80f, minDamageMult = 1f, color = new Color(1f, 0.6f, 0.2f) },
        new WeaponDef { name = "PLASMA", sound = "rail", damage = 30f, headMult = 2f, chestMult = 1.15f, interval = 0.135f, magSize = 40, reserve = 160, reloadTime = 2.2f,
                        spread = 0.6f, adsSpread = 0.2f, bloom = 0.05f, recoilPitch = 0.5f, recoilYaw = 0.3f, auto = true, projectile = true, projSpeed = 75f, projRadius = 1.8f, zoomFov = 65f,
                        sprintToFire = 0.40f, dropoffStart = 20f, dropoffEnd = 45f, minDamageMult = 0.6f, color = Art.Green }
    };
}

public static class Combat
{
    // Desvia a direcao dentro de um cone de "deg" graus.
    public static Vector3 SpreadDir(Vector3 fwd, float deg)
    {
        if (deg <= 0.0001f) return fwd.normalized;
        Vector3 perp = Vector3.Cross(fwd, Vector3.up);
        if (perp.sqrMagnitude < 0.0001f) perp = Vector3.right;
        perp.Normalize();
        Vector3 tilted = Quaternion.AngleAxis(Random.Range(0f, deg), perp) * fwd;
        return (Quaternion.AngleAxis(Random.Range(0f, 360f), fwd) * tilted).normalized;
    }

    // Dono do colisor: o proprio ator, ou quem pilota o veiculo atingido.
    public static Actor ActorOf(Collider c)
    {
        Actor a = c.GetComponentInParent<Actor>();
        if (a != null) return a;
        Vehicle v = c.GetComponentInParent<Vehicle>();
        return v != null ? v.driver : null;
    }

    // Regiao do acerto: 2 cabeca, 1 peito/pescoco, 0 resto do corpo (dinossauro: sempre corpo).
    public static int Region(Actor a, Vector3 point)
    {
        if (a.creature) return 0;
        float y = (point.y - a.transform.position.y) / a.CrouchScale;
        return y > 1.48f ? 2 : y > 1.0f ? 1 : 0;
    }

    public static float RegionMult(int region, float headMult, float chestMult) { return region == 2 ? headMult : region == 1 ? chestMult : 1f; }

    // Dano final de um acerto em ator (inteiro: e exatamente o que aparece na tela).
    // Host/offline aplica e mostra o que a vida perdeu; cliente online avisa o host e mostra a previsao.
    public static bool HitActor(Actor shooter, Actor a, Vector3 point, float dmg, int region, string weapon, out bool killed)
    {
        killed = false;
        dmg = Mathf.Round(dmg);
        if (dmg <= 0f) return false;
        a.Bleed(point, shooter != null ? point - shooter.EyePos : Vector3.down, dmg, region == 2);
        float removed;
        if (NetGame.IsClient)
        {
            NetGame.SendHit(a, dmg, region == 2, weapon);
            removed = Mathf.Min(dmg, Mathf.Ceil(Mathf.Max(0f, a.health)));
        }
        else
        {
            killed = a.TakeDamage(dmg, shooter, region == 2, weapon);
            removed = a.lastRemoved;
        }
        if (shooter != null && Game.I != null && shooter == Game.I.player && removed > 0f) DamagePopups.Show(point, removed, region);
        return true;
    }

    // Projetil que acertou um ator em cheio (plasma). Cliente online so mostra a previsao:
    // o dano vem da copia do projetil que roda no host.
    public static void ProjectileDirect(Actor owner, Actor a, Vector3 point, float dmg, string weapon)
    {
        WeaponDef w = null;
        foreach (WeaponDef x in Weapons.All) if (x.name == weapon) w = x;
        int region = Region(a, point);
        float d = Mathf.Round(dmg * (w != null ? RegionMult(region, w.headMult, w.chestMult) : 1f));
        bool mine = owner != null && Game.I != null && owner == Game.I.player;
        a.Bleed(point, owner != null ? point - owner.EyePos : Vector3.down, d, region == 2);
        if (NetGame.IsClient)
        {
            if (mine) { DamagePopups.Show(point, Mathf.Min(d, Mathf.Ceil(Mathf.Max(0f, a.health))), region); Game.I.Hitmarker(region == 2, false); }
            return;
        }
        bool killed = a.TakeDamage(d, owner, region == 2, weapon);
        if (mine && a.lastRemoved > 0f) { DamagePopups.Show(point, a.lastRemoved, region); Game.I.Hitmarker(region == 2, killed); }
    }

    // Tiro com varios projeteis (Pulso): soma os que acertaram cada alvo e aplica uma vez so.
    class Pending { public Actor a; public float dmg; public int region; public Vector3 point; }
    static List<Pending> batch;
    public static void BeginBatch() { batch = new List<Pending>(); }
    public static bool EndBatch(Actor shooter, string weapon, out bool head)
    {
        bool killedAny = false; head = false;
        List<Pending> b = batch; batch = null;
        if (b == null) return false;
        foreach (Pending p in b)
        {
            bool k;
            if (p.a != null && p.a.alive && HitActor(shooter, p.a, p.point, p.dmg, p.region, weapon, out k)) killedAny |= k;
            head |= p.region == 2;
        }
        return killedAny;
    }

    // Aplica o dano de uma bala no que o raio acertou. Devolve verdadeiro se feriu um inimigo.
    public static bool BulletHit(Actor shooter, RaycastHit h, float dmg, float headMult, float chestMult, string weapon, Color c, out bool head, out bool killed)
    {
        head = false; killed = false;
        Vehicle v = h.collider.GetComponentInParent<Vehicle>();
        if (v != null)
        {
            Fx.Spark(h.point, h.normal, new Color(1f, 0.8f, 0.4f));
            if (shooter != null && v.driver == shooter) return false;
            bool foe = v.driver == null || shooter == null || v.driver.team != shooter.team;
            if (foe) v.Damage(dmg, shooter);
            return foe && v.driver != null;
        }
        Actor a = h.collider.GetComponentInParent<Actor>();
        if (a != null && a.alive)
        {
            if (a != shooter && (shooter == null || a.team != shooter.team))
            {
                int region = Region(a, h.point);
                head = region == 2;
                float d = dmg * RegionMult(region, headMult, chestMult);
                if (batch != null)
                {
                    Pending p = batch.Find(x => x.a == a);
                    if (p == null) { p = new Pending { a = a }; batch.Add(p); }
                    p.dmg += d; p.region = Mathf.Max(p.region, region); p.point = h.point;
                    return true;
                }
                HitActor(shooter, a, h.point, d, region, weapon, out killed);
                return true;
            }
            return false;
        }
        Fx.Spark(h.point, h.normal, c);
        Breakable.Hit(h.collider, h.point, h.normal, dmg);   // parede da CASA: gasta a placa
        return false;
    }

    public static int flybyCount;
    public static float lastHitDamage;
    public static float lastHitDistance;

    public static float ApplyDropoff(WeaponDef w, float dist, float dmg)
    {
        if (w == null) return dmg;
        float maxMult = w.pellets > 1 ? 1.5f : 1f;
        float t = Mathf.InverseLerp(w.dropoffStart, Mathf.Max(w.dropoffStart + 0.01f, w.dropoffEnd), dist);
        return dmg * Mathf.Lerp(maxMult, w.minDamageMult, t);
    }

    /// <summary>
    /// Reproduz estalo supersonico/assobio 3D se um disparo inimigo cruzar a ate 2.0m da camera local sem acertar o jogador.
    /// </summary>
    public static bool TryBulletFlyby(Actor shooter, Vector3 start, Vector3 end, bool hitLocalPlayer)
    {
        if (hitLocalPlayer || Game.I == null || Game.I.player == null || !Game.I.player.alive) return false;
        if (shooter == Game.I.player) return false;
        if (shooter != null && shooter.team == Game.I.player.team) return false;
        Camera cam = Game.I.pc != null && Game.I.pc.cam != null ? Game.I.pc.cam : Camera.main;
        if (cam == null) return false;
        Vector3 camPos = cam.transform.position;
        Vector3 ab = end - start;
        float lenSq = ab.sqrMagnitude;
        if (lenSq < 0.0001f) return false;
        float t = Mathf.Clamp01(Vector3.Dot(camPos - start, ab) / lenSq);
        Vector3 closest = start + ab * t;
        if ((closest - camPos).sqrMagnitude <= 4f) // raio de 2.0m
        {
            flybyCount++;
            Sfx.Flyby(closest);
            return true;
        }
        return false;
    }

    public static bool Hitscan(Actor shooter, Vector3 origin, Vector3 dir, WeaponDef w, Vector3 muzzle, float mult, out bool head, out bool killed)
    {
        head = false;
        killed = false;
        bool hitEnemy = false;
        bool hitLocal = false;
        Vector3 end = origin + dir * w.range;
        RaycastHit h;
        if (Physics.Raycast(origin, dir, out h, w.range, ~0, QueryTriggerInteraction.Ignore))
        {
            end = h.point;
            float d = ApplyDropoff(w, h.distance, w.damage * mult);
            Actor hitActor = ActorOf(h.collider);
            hitLocal = Game.I != null && hitActor != null && hitActor == Game.I.player;
            hitEnemy = BulletHit(shooter, h, d, w.headMult, w.chestMult, w.name, w.color, out head, out killed);
            if (shooter != null && Game.I != null && shooter == Game.I.player && hitEnemy)
            {
                lastHitDistance = h.distance;
                lastHitDamage = Mathf.Round(d * RegionMult(head ? 2 : (Region(hitActor, h.point)), w.headMult, w.chestMult));
            }
        }
        TryBulletFlyby(shooter, origin, end, hitLocal);
        NetGame.Tracer(shooter, w, muzzle, end);
        bool hot = w.name != "PLASMA";
        Fx.Tracer(muzzle, end, hot ? new Color(1f, 0.82f, 0.45f, 0.8f) : w.color, w.pellets > 1 ? 0.012f : 0.018f, 0.07f);
        return hitEnemy;
    }

    // Bala de verdade (fuzil de precisao): tem tempo de voo e cai com a gravidade.
    public static void FireBullet(Actor owner, Vector3 pos, Vector3 dir, WeaponDef w, float mult)
    {
        GameObject g = new GameObject("bullet");
        g.transform.position = pos;
        Bullet b = g.AddComponent<Bullet>();
        b.owner = owner; b.vel = dir.normalized * 380f; b.def = w; b.mult = mult;
        NetGame.Tracer(owner, w, pos, pos + dir.normalized * 6f);
    }

    const float SelfBlast = 0.5f;   // a propria explosao fere o atirador pela metade

    // exclude: ator que ja levou o acerto direto (plasma), para nao contar duas vezes.
    public static void Explode(Vector3 pos, float radius, float dmg, Actor owner, string weapon, Color c, Actor exclude = null)
    {
        if (weapon == "PLASMA") Fx.Explosion(pos, c, radius * 0.6f); else Fx.Blast(pos, radius);
        Sfx.At("explosion", pos, 1f);
        if (Game.I == null) return;
        bool client = NetGame.IsClient;   // cliente online: o dano e do host; aqui so mostra a previsao do proprio tiro
        if (client && (owner == null || owner != Game.I.player)) return;
        Breakable.Blast(pos, radius, dmg);   // abre buraco nas paredes da CASA
        if (!client)
        for (int i = Vehicle.All.Count - 1; i >= 0; i--)
        {
            Vehicle v = Vehicle.All[i];
            if (v == null || v.dead) continue;
            float dv = Vector3.Distance(v.transform.position + Vector3.up, pos);
            if (dv < radius + 1.5f && !(owner != null && v.driver == owner)) v.Damage(dmg * Mathf.Clamp01(1.15f - dv / (radius + 1.5f)), owner);
        }
        bool mine = owner != null && owner == Game.I.player;
        var list = Game.I.actors;
        for (int i = 0; i < list.Count; i++)
        {
            Actor a = list[i];
            if (a == null || !a.alive || a == exclude) continue;
            Vector3 to = a.ChestPos - pos;
            float d = to.magnitude;
            if (d > radius) continue;
            RaycastHit h;
            if (Physics.Linecast(pos, a.ChestPos, out h, ~0, QueryTriggerInteraction.Ignore))
            {
                if (h.collider.GetComponentInParent<Actor>() != a) continue; // parede ou veiculo no caminho
            }
            // Dano maximo no centro (ate 0,75 m) e caindo ate zero na borda do raio.
            float k = 1f - Mathf.Clamp01((d - 0.75f) / Mathf.Max(0.1f, radius - 0.75f));
            bool self = a == owner;
            bool enemy = owner == null || a.team != owner.team;
            if (!self && !enemy) continue;
            float hit = Mathf.Round(dmg * k * (self ? SelfBlast : 1f));
            if (hit <= 0f) continue;
            a.Bleed(a.ChestPos, to, hit, false);
            if (client)
            {
                // Previsao: o host aplica a mesma conta.
                float pred = Mathf.Min(hit, Mathf.Ceil(Mathf.Max(0f, a.health)));
                if (self) DamagePopups.Self(pred, weapon); else DamagePopups.Show(a.ChestPos, pred, 0);
                continue;
            }
            Vector3 push = (to.sqrMagnitude > 0.001f ? to.normalized : Vector3.up) * (14f * k) + Vector3.up * (5f * k);
            a.externalVel += push;
            a.TakeDamage(hit, owner, false, weapon);
            if (a.lastRemoved <= 0f) continue;
            if (self && a == Game.I.player) DamagePopups.Self(a.lastRemoved, weapon);
            else if (mine && !self) DamagePopups.Show(a.ChestPos, a.lastRemoved, 0);
        }
    }

    public static void Launch(Actor owner, Vector3 pos, Vector3 dir, float speed, float gravity, float dmg, float radius, string weapon, Color c, float mult)
    {
        NetGame.Launched(owner, pos, dir, speed, gravity, dmg * mult, radius, weapon);
        GameObject g = new GameObject("projectile");
        g.transform.position = pos;
        g.transform.rotation = Quaternion.LookRotation(dir.sqrMagnitude > 0.001f ? dir : Vector3.forward);
        if (weapon == "PLASMA")
        {
            Art.Prim(PrimitiveType.Sphere, g.transform, Vector3.zero, Vector3.one * 0.16f, Art.Glow(c, 3f), false, "plasma");
            Art.PointLight(g.transform, Vector3.zero, c, 5f, 1.4f);
        }
        else
        {
            GameObject vis = WeaponArt.Part(weapon == "GRANADA" ? "Grenade" : "Rocket", g.transform, c);
            if (weapon == "GRANADA") vis.transform.localScale = Vector3.one * 1.6f;
            else Art.PointLight(g.transform, new Vector3(0f, 0f, -0.2f), new Color(1f, 0.6f, 0.2f), 5f, 1.4f);
        }
        Projectile p = g.AddComponent<Projectile>();
        p.owner = owner;
        p.vel = dir.normalized * speed;
        p.gravity = gravity;
        p.damage = dmg * mult;
        p.radius = radius;
        p.weapon = weapon;
        p.color = c;
    }
}
