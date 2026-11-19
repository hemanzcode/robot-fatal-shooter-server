using System.Collections.Generic;
using UnityEngine;

// Chafariz de pedra no lugar das antigas pilastras: bacia octogonal com agua, coluna central com prato
// de onde a agua cai em cortina, e peixinhos nadando em volta.
public class Fountain : MonoBehaviour
{
    readonly List<Transform> fish = new List<Transform>();
    readonly List<Transform> tails = new List<Transform>();
    float[] radius, speed, phase, depth;
    Transform curtain, water;

    public static void Build(Transform root, Vector3 p, Material stone, float hsh)
    {
        GameObject g = new GameObject("fountain");
        g.transform.SetParent(root, false);
        g.transform.position = p;
        Transform t = g.transform;

        Material rim = Art.Pbr("PavingStones070", new Color(0.92f, 0.85f, 0.72f), 1.5f, 1f, 0f, 0f);
        Material floor = Art.Mat(new Color(0.12f, 0.32f, 0.36f), Color.black, 0f, default(Color), 0.6f, 0f);
        Material waterMat = Art.Unlit(new Color(0.25f, 0.62f, 0.78f, 0.5f));
        Material fall = Art.Unlit(new Color(0.75f, 0.9f, 1f, 0.35f));

        // Bacia octogonal: 8 muretas com colisor (da para se abrigar atras).
        const float R = 1.6f;
        float seg = 2f * R * Mathf.Tan(Mathf.PI / 8f) + 0.08f;
        for (int i = 0; i < 8; i++)
        {
            float a = i * Mathf.PI / 4f;
            Vector3 n = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            GameObject w = Art.Box(t, n * R + Vector3.up * 0.33f, new Vector3(0.34f, 0.66f, seg), rim, true, "rim");
            w.transform.rotation = Quaternion.LookRotation(Vector3.Cross(n, Vector3.up));
            GameObject cap = Art.Box(t, n * R + Vector3.up * 0.69f, new Vector3(0.44f, 0.08f, seg + 0.04f), rim, false, "rimCap");
            cap.transform.rotation = w.transform.rotation;
        }
        Art.Prim(PrimitiveType.Cylinder, t, Vector3.up * 0.04f, new Vector3(R * 2f, 0.04f, R * 2f), floor, false, "basinFloor");

        Fountain f = g.AddComponent<Fountain>();
        f.water = Art.Prim(PrimitiveType.Cylinder, t, Vector3.up * 0.5f, new Vector3(R * 2f - 0.2f, 0.005f, R * 2f - 0.2f), waterMat, false, "water").transform;

        // Coluna central, prato e bico.
        Art.Prim(PrimitiveType.Cylinder, t, Vector3.up * 0.8f, new Vector3(0.55f, 0.8f, 0.55f), rim, true, "column");
        Art.Prim(PrimitiveType.Cylinder, t, Vector3.up * 1.62f, new Vector3(1.5f, 0.08f, 1.5f), rim, false, "bowl");
        Art.Prim(PrimitiveType.Cylinder, t, Vector3.up * 1.72f, new Vector3(1.3f, 0.02f, 1.3f), waterMat, false, "bowlWater");
        Art.Prim(PrimitiveType.Cylinder, t, Vector3.up * 1.95f, new Vector3(0.22f, 0.25f, 0.22f), rim, false, "spout");
        Art.Prim(PrimitiveType.Sphere, t, Vector3.up * 2.25f, new Vector3(0.28f, 0.28f, 0.28f), rim, false, "finial");
        // Cortina de agua caindo do prato ate a bacia.
        f.curtain = Art.Prim(PrimitiveType.Cylinder, t, Vector3.up * 1.08f, new Vector3(1.45f, 0.54f, 1.45f), fall, false, "curtain").transform;

        // Peixes (corpo + rabo), cores variadas.
        Color[] cols = { new Color(1f, 0.45f, 0.08f), new Color(1f, 0.82f, 0.2f), new Color(0.95f, 0.95f, 0.92f), new Color(0.95f, 0.3f, 0.15f), new Color(1f, 0.6f, 0.3f) };
        int count = 5;
        f.radius = new float[count]; f.speed = new float[count]; f.phase = new float[count]; f.depth = new float[count];
        for (int i = 0; i < count; i++)
        {
            Material fm = Art.Mat(cols[i % cols.Length], cols[i % cols.Length] * 0.25f, 0f, default(Color), 0.7f, 0f);
            Transform body = new GameObject("fish").transform;
            body.SetParent(t, false);
            Art.Box(body, Vector3.zero, new Vector3(0.11f, 0.09f, 0.28f), fm, false, "fishBody");
            Art.Box(body, new Vector3(0f, 0.05f, 0.02f), new Vector3(0.02f, 0.05f, 0.1f), fm, false, "fin");
            Transform tail = new GameObject("tail").transform;
            tail.SetParent(body, false);
            tail.localPosition = new Vector3(0f, 0f, -0.14f);
            Art.Box(tail, new Vector3(0f, 0f, -0.06f), new Vector3(0.02f, 0.12f, 0.12f), fm, false, "tailFin");
            f.fish.Add(body); f.tails.Add(tail);
            float k = (hsh * 7.3f + i * 0.37f) % 1f;
            f.radius[i] = 0.75f + k * 0.55f;
            f.speed[i] = (0.35f + k * 0.4f) * (i % 2 == 0 ? 1f : -1f);
            f.phase[i] = i * 1.3f + hsh * 6f;
            f.depth[i] = 0.24f + k * 0.16f;
        }
    }

    void Update()
    {
        float tm = Time.time;
        for (int i = 0; i < fish.Count; i++)
        {
            float a = phase[i] + tm * speed[i];
            float r = radius[i] + Mathf.Sin(tm * 0.7f + i) * 0.08f;
            Vector3 pos = new Vector3(Mathf.Cos(a) * r, depth[i] + Mathf.Sin(tm * 1.3f + i) * 0.03f, Mathf.Sin(a) * r);
            Vector3 dir = new Vector3(-Mathf.Sin(a), 0f, Mathf.Cos(a)) * Mathf.Sign(speed[i]);
            fish[i].localPosition = pos;
            fish[i].localRotation = Quaternion.LookRotation(dir) * Quaternion.Euler(0f, Mathf.Sin(tm * 8f + i) * 6f, 0f);
            tails[i].localRotation = Quaternion.Euler(0f, Mathf.Sin(tm * 12f + i * 2f) * 30f, 0f);
        }
        // Agua mexendo de leve.
        if (curtain != null) curtain.localRotation = Quaternion.Euler(0f, tm * 40f, 0f);
        if (water != null) water.localPosition = new Vector3(0f, 0.5f + Mathf.Sin(tm * 2f) * 0.01f, 0f);
    }
}
