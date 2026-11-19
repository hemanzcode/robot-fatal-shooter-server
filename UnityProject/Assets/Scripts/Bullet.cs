using UnityEngine;

// Bala do fuzil de precisao: voa a 380 m/s, cai com a gravidade e deixa um rastro curto.
public class Bullet : MonoBehaviour
{
    public Actor owner;
    public Vector3 vel;
    public WeaponDef def;
    public float mult = 1f;
    float age;

    void Update()
    {
        float dt = Time.deltaTime;
        age += dt;
        vel += Vector3.down * 9.81f * dt;
        Vector3 step = vel * dt;
        float dist = step.magnitude;
        if (dist < 0.0001f) return;
        Vector3 from = transform.position;
        RaycastHit[] hits = Physics.RaycastAll(from, step / dist, dist, ~0, QueryTriggerInteraction.Ignore);
        int best = -1;
        for (int i = 0; i < hits.Length; i++)
        {
            if (owner != null && Combat.ActorOf(hits[i].collider) == owner) continue;
            if (best < 0 || hits[i].distance < hits[best].distance) best = i;
        }
        if (best >= 0)
        {
            bool head, killed;
            // Sniper: dano cheio em qualquer distancia (80 no corpo).
            bool hit = Combat.BulletHit(owner, hits[best], def.damage * mult, def.headMult, def.chestMult, def.name, def.color, out head, out killed);
            Fx.Tracer(from, hits[best].point, new Color(1f, 0.9f, 0.7f, 0.5f), 0.01f, 0.05f);
            if (hit && owner != null && owner.isPlayer && Game.I != null) Game.I.Hitmarker(head, killed);
            Destroy(gameObject);
            return;
        }
        transform.position = from + step;
        Fx.Tracer(from, transform.position, new Color(1f, 0.9f, 0.7f, 0.35f), 0.008f, 0.04f);
        if (age > 3f || transform.position.y < -30f) Destroy(gameObject);
    }
}
