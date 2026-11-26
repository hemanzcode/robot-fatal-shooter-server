using UnityEngine;

// Item no mapa: 0 = vida, 1 = municao. Reaparece depois de um tempo.
public class Pickup : MonoBehaviour
{
    public int kind;
    public float respawn = 15f;
    GameObject visual;
    Light lamp;
    float backAt;

    void Start()
    {
        Color c = kind == 0 ? Art.Green : Art.Orange;
        visual = new GameObject("visual");
        visual.transform.SetParent(transform, false);
        visual.transform.localPosition = Vector3.up * 0.9f;
        if (kind == 0)
        {
            Art.Box(visual.transform, Vector3.zero, new Vector3(0.6f, 0.2f, 0.2f), Art.Glow(c, 2f));
            Art.Box(visual.transform, Vector3.zero, new Vector3(0.2f, 0.6f, 0.2f), Art.Glow(c, 2f));
        }
        else
        {
            Art.Box(visual.transform, Vector3.zero, new Vector3(0.45f, 0.35f, 0.3f), Art.Mat(Art.Steel, Color.black));
            Art.Box(visual.transform, new Vector3(0f, 0.19f, 0f), new Vector3(0.47f, 0.05f, 0.32f), Art.Glow(c, 2f));
            Art.Box(visual.transform, new Vector3(0f, -0.1f, 0f), new Vector3(0.47f, 0.05f, 0.32f), Art.Glow(c, 2f));
        }
        Art.Prim(PrimitiveType.Cylinder, transform, Vector3.up * 0.02f, new Vector3(1.4f, 0.02f, 1.4f), Art.Glow(c, 0.8f), false, "base");
        lamp = Art.PointLight(transform, Vector3.up * 1f, c, 5f, 1.2f);
    }

    void Update()
    {
        if (visual == null || Game.I == null) return;
        if (!visual.activeSelf)
        {
            if (Time.time >= backAt) { visual.SetActive(true); lamp.enabled = true; }
            return;
        }
        visual.transform.Rotate(0f, 90f * Time.deltaTime, 0f);
        visual.transform.localPosition = Vector3.up * (0.9f + Mathf.Sin(Time.time * 2.5f) * 0.12f);

        var list = Game.I.actors;
        for (int i = 0; i < list.Count; i++)
        {
            Actor a = list[i];
            if (a == null || !a.alive) continue;
            Vector3 d = a.transform.position - transform.position;
            if (Mathf.Abs(d.y) > 1.5f) continue;
            d.y = 0f;
            if (d.sqrMagnitude > 1.4f * 1.4f) continue;

            bool used = false;
            if (kind == 0)
            {
                if (a.health < a.maxHealth - 1f) { a.health = Mathf.Min(a.maxHealth, a.health + 80f); used = true; }
            }
            else
            {
                PlayerController pc = a.GetComponent<PlayerController>();
                if (pc != null) used = pc.RefillAmmo();
            }
            if (used)
            {
                Sfx.At("pickup", transform.position, 0.9f);
                visual.SetActive(false);
                lamp.enabled = false;
                backAt = Time.time + respawn;
                return;
            }
        }
    }
}
