using UnityEngine;

// Vitrine 3D da loja: a arma com a skin escolhida, girando, longe do mapa (y = -500), renderizada numa textura
// que o menu desenha ao lado da lista. Arrastar gira, roda do mouse / botoes aproximam (como o HTML das skins).
public class ShopView : MonoBehaviour
{
    public static ShopView I;
    public RenderTexture rt;
    public bool spin = true;
    const int Layer = 30;
    Camera cam;
    Transform holder;
    GameObject shown;
    int shownWeapon = -1;
    string shownSkin = null;
    Vector3 center;
    float radius = 0.5f, yaw = -1.15f, pitch = 0.2f, zoom = 1f;

    public static ShopView Ensure()
    {
        if (I != null) return I;
        GameObject g = new GameObject("ShopView");
        g.transform.position = new Vector3(0f, -500f, 0f);
        DontDestroyOnLoad(g);
        I = g.AddComponent<ShopView>();
        I.Build();
        return I;
    }

    void Build()
    {
        holder = new GameObject("arma").transform;
        holder.SetParent(transform, false);
        GameObject cg = new GameObject("cam");
        cg.transform.SetParent(transform, false);
        cam = cg.AddComponent<Camera>();
        cam.cullingMask = 1 << Layer;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.07f, 0.06f, 0.05f, 1f);
        cam.fieldOfView = 34f;
        cam.nearClipPlane = 0.02f;
        cam.farClipPlane = 30f;
        rt = new RenderTexture(1600, 1000, 24, RenderTextureFormat.ARGB32);
        rt.antiAliasing = 4;
        cam.targetTexture = rt;
        cam.enabled = false;
        // Luz de estudio: principal quente, recorte frio por tras e preenchimento.
        Light(new Vector3(-1.2f, 1.4f, 1.6f), new Color(1f, 0.95f, 0.88f), 9f);
        Light(new Vector3(1.6f, 0.6f, -1.4f), new Color(0.6f, 0.75f, 1f), 6f);
        Light(new Vector3(0.2f, -1.2f, 0.8f), new Color(1f, 0.85f, 0.7f), 3f);
        Light(new Vector3(1.4f, 1.2f, 1.2f), new Color(1f, 1f, 1f), 4f);
    }

    void Light(Vector3 p, Color c, float intensity)
    {
        GameObject g = new GameObject("luz");
        g.transform.SetParent(transform, false);
        g.transform.localPosition = p;
        Light l = g.AddComponent<Light>();
        l.type = LightType.Point;
        l.color = c;
        l.intensity = intensity;
        l.range = 9f;
        l.shadows = LightShadows.None;
    }

    public void Show(int weapon, SkinDef skin)
    {
        string sid = skin != null ? skin.id : "";
        if (weapon == shownWeapon && sid == shownSkin && shown != null) return;
        bool sameWeapon = weapon == shownWeapon;
        if (shown != null) Destroy(shown);
        shown = WeaponArt.Weapon(weapon, holder, skin);
        foreach (Transform t in shown.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = Layer;
        foreach (Renderer r in shown.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        shownWeapon = weapon; shownSkin = sid;
        // Enquadra pela caixa do modelo.
        Bounds b = new Bounds(holder.position, Vector3.zero);
        bool first = true;
        foreach (Renderer r in shown.GetComponentsInChildren<Renderer>()) { if (first) { b = r.bounds; first = false; } else b.Encapsulate(r.bounds); }
        center = b.center;
        radius = Mathf.Max(0.15f, b.extents.magnitude);
        if (!sameWeapon) zoom = 1f;
    }

    public void Drag(Vector2 d) { spin = false; yaw -= d.x * 0.008f; pitch = Mathf.Clamp(pitch + d.y * 0.006f, -1.3f, 1.3f); }
    public void Zoom(float f) { zoom = Mathf.Clamp(zoom * f, 0.5f, 4f); }
    float seen = -9f;
    // O menu chama a cada quadro em que a loja aparece; sem chamada, a camera desliga sozinha.
    public void Keep() { seen = Time.unscaledTime; if (cam != null) cam.enabled = true; }

    void LateUpdate()
    {
        if (cam == null) return;
        if (Time.unscaledTime - seen > 0.25f) { cam.enabled = false; return; }
        if (spin) yaw += Time.unscaledDeltaTime * 0.36f;
        float dist = radius / Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * 1.05f / zoom;
        Vector3 dir = new Vector3(Mathf.Sin(yaw) * Mathf.Cos(pitch), Mathf.Sin(pitch), Mathf.Cos(yaw) * Mathf.Cos(pitch));
        cam.transform.position = center + dir * dist;
        cam.transform.LookAt(center);
    }
}
