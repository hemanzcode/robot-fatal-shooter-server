using UnityEngine;

// Foguetes e granadas: voam, colidem por raycast e explodem com dano em area.
public class Projectile : MonoBehaviour
{
    public Actor owner;
    public Vector3 vel;
    public float gravity;
    public float damage = 100f;
    public float radius = 5f;
    public string weapon = "BAZUCA";
    public Color color = Color.white;
    float age;
    Vector3 lastTrail;
    bool flybyPlayed;

    void Start() { lastTrail = transform.position; }

    void Update()
    {
        float dt = Time.deltaTime;
        age += dt;
        vel += Vector3.down * gravity * dt;
        Vector3 step = vel * dt;
        float dist = step.magnitude;
        if (dist > 0.0001f)
        {
            RaycastHit[] hits = Physics.SphereCastAll(transform.position, 0.12f, step / dist, dist, ~0, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            bool found = false;
            Vector3 point = transform.position, raw = transform.position;
            Collider bestCol = null;
            for (int i = 0; i < hits.Length; i++)
            {
                Actor a = hits[i].collider.GetComponentInParent<Actor>();
                if (a != null && (a == owner || !a.alive)) continue;
                Vehicle hv = hits[i].collider.GetComponentInParent<Vehicle>();
                if (hv != null && owner != null && hv.driver == owner) continue;
                if (hits[i].distance < best)
                {
                    best = hits[i].distance;
                    found = true;
                    point = hits[i].distance <= 0f ? transform.position : hits[i].point + hits[i].normal * 0.1f;
                    raw = hits[i].distance <= 0f ? transform.position : hits[i].point;
                    bestCol = hits[i].collider;
                }
            }
            if (found)
            {
                // Plasma: acerto direto conta a regiao do corpo (cabeca, peito, resto); a explosao pega so os outros.
                Actor direct = null;
                if (weapon == "PLASMA" && bestCol != null)
                {
                    Actor a = bestCol.GetComponentInParent<Actor>();
                    if (a != null && a.alive && a != owner && (owner == null || a.team != owner.team))
                    {
                        direct = a;
                        Combat.ProjectileDirect(owner, a, raw, damage, weapon);
                    }
                }
                Boom(point, direct);
                return;
            }
        }
        Vector3 prevPos = transform.position;
        transform.position += step;
        if (!flybyPlayed) flybyPlayed = Combat.TryBulletFlyby(owner, prevPos, transform.position, false);
        if (gravity > 0.01f) transform.Rotate(540f * dt, 180f * dt, 0f, Space.Self);
        else if (vel.sqrMagnitude > 0.01f) transform.rotation = Quaternion.LookRotation(vel);
        // Rastro: plasma vira descarga eletrica; bazuca nao deixa feixe (so o foguete e a luz).
        if (weapon == "BAZUCA") lastTrail = transform.position;
        else if (weapon == "PLASMA")
        {
            if ((transform.position - lastTrail).sqrMagnitude > 2.2f)
            {
                Fx.Arc(lastTrail, transform.position, color, 0.16f);
                lastTrail = transform.position;
            }
        }
        else if ((transform.position - lastTrail).sqrMagnitude > 1.5f)
        {
            Fx.Tracer(lastTrail, transform.position, color, 0.08f, 0.25f);
            lastTrail = transform.position;
        }
        if (age > 6f || transform.position.y < -30f) Boom(transform.position, null);
    }

    void Boom(Vector3 p, Actor direct)
    {
        Combat.Explode(p, radius, damage, owner, weapon, color, direct);
        Destroy(gameObject);
    }
}
