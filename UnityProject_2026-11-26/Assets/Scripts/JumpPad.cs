using UnityEngine;

// Plataforma de salto: lanca para cima quem pisar nela.
public class JumpPad : MonoBehaviour
{
    public float power = 14f;

    void Update()
    {
        if (Game.I == null) return;
        var list = Game.I.actors;
        for (int i = 0; i < list.Count; i++)
        {
            Actor a = list[i];
            if (a == null || !a.alive) continue;
            Vector3 d = a.transform.position - transform.position;
            if (d.y > 0.6f || d.y < -0.5f) continue;
            d.y = 0f;
            if (d.sqrMagnitude > 1.3f * 1.3f) continue;
            if (Time.time - a.lastPadTime < 0.6f) continue;
            a.lastPadTime = Time.time;
            a.pendingLaunch = power;
            Sfx.At("pad", transform.position, 0.8f);
        }
    }
}
