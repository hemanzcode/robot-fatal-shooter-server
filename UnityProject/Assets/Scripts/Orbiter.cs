using UnityEngine;

// Luzes de veiculos voadores ao longe, circulando sobre a cidade.
public class Orbiter : MonoBehaviour
{
    public Vector3 center;
    public float radius = 100f;
    public float height = 60f;
    public float speed = 0.1f;
    public float phase;

    void Update()
    {
        float a = phase + Time.time * speed;
        transform.position = center + new Vector3(Mathf.Cos(a) * radius, height + Mathf.Sin(a * 3f) * 4f, Mathf.Sin(a) * radius);
    }
}
