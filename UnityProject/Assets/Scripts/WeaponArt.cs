using UnityEngine;

// Modelos das armas, granada, foguete e cartuchos (Resources/Weapons/Weapons.fbx) com materiais PBR.
public static class WeaponArt
{
    // Ordem igual a Weapons.All: VETOR (fuzil), PULSO (escopeta), SNIPER (ferrolho), BAZUCA, PLASMA.
    static readonly string[] Names = { "Rifle", "Shotgun", "Rail", "Launcher", "Plasma" };
    public static readonly Vector3[] Muzzle = { new Vector3(0f, 0.034f, 0.79f), new Vector3(0f, 0.042f, 0.78f), new Vector3(0f, 0.012f, 1.07f), new Vector3(0f, 0.02f, 1.02f), new Vector3(0f, 0.02f, 0.58f) };
    // Onde a mao de apoio segura (para os agentes em terceira pessoa).
    public static readonly Vector3[] Support = { new Vector3(0f, 0.0f, 0.36f), new Vector3(0f, -0.02f, 0.34f), new Vector3(0f, -0.07f, 0.22f), new Vector3(0f, -0.1f, 0.42f), new Vector3(0f, -0.07f, 0.2f) };
    public static readonly Vector3[] Eject = { new Vector3(0.03f, 0.03f, 0.07f), new Vector3(0.03f, 0.03f, 0.05f), Vector3.zero, Vector3.zero, Vector3.zero };

    static Material For(string slot, Color glow)
    {
        string n = slot.ToLowerInvariant();
        if (n.StartsWith("metal")) return Art.PbrLocal("Metal046B", new Color(0.62f, 0.64f, 0.68f), 0.35f, 1.05f, 0f, 0.8f);
        if (n.StartsWith("poly")) return Art.PbrLocal("Plastic006", new Color(1.6f, 1.6f, 1.7f), 0.25f, 0.75f, 0f, 0.6f);
        if (n.StartsWith("dark")) return Art.PbrLocal("Plastic006", new Color(0.7f, 0.7f, 0.75f), 0.25f, 0.6f, 0f, 0.5f);
        if (n.StartsWith("wood")) return Art.PbrLocal("Planks037A", new Color(1.5f, 1.15f, 0.85f), 0.45f, 1.1f, 0f, 0.6f);
        if (n.StartsWith("brass")) return Art.PbrLocal("Metal032", new Color(1.5f, 1.05f, 0.4f), 0.2f, 1f, 0.4f, 0.3f);
        if (n.StartsWith("olive")) return Art.PbrLocal("Metal046B", new Color(0.75f, 0.95f, 0.5f), 0.2f, 0.6f, 0f, 0.8f);
        if (n.StartsWith("shell")) return Art.Mat(new Color(0.55f, 0.06f, 0.05f), Color.black, 0f, default(Color), 0.5f, 0f);
        // Vidro da mira: quase transparente (antes era opaco e tampava a visao ao mirar).
        if (n.StartsWith("lens")) return Art.Unlit(new Color(0.55f, 0.85f, 1f, 0.07f));
        if (n.StartsWith("glow")) return Art.Glow(glow.maxColorComponent < 0.05f ? Art.Orange : glow, 1.5f);
        if (n.StartsWith("paint")) return Art.PbrLocal("Metal046B", glow * 1.6f, 0.8f, 0.8f, 0f, 0.5f);
        if (n.StartsWith("hide")) return Art.PbrLocal("Ground054", glow, 0.9f, 0.7f, 0f, 1.4f);
        if (n.StartsWith("glass")) return Art.Mat(new Color(0.03f, 0.06f, 0.09f), Color.black, 0f, default(Color), 0.96f, 0.3f);
        if (n.StartsWith("rubber")) return Art.Mat(new Color(0.02f, 0.02f, 0.02f), Color.black, 0f, default(Color), 0.25f, 0f);
        if (n.StartsWith("bone")) return Art.Mat(new Color(0.85f, 0.82f, 0.7f), Color.black, 0f, default(Color), 0.5f, 0f);
        if (n.StartsWith("eye")) return Art.Glow(Art.Yellow, 1.2f);
        return Art.Mat(new Color(0.1f, 0.1f, 0.11f), Color.black, 0f, default(Color), 0.6f, 0.5f);
    }

    public static GameObject Part(string name, Transform parent, Color glow)
    {
        GameObject src = Resources.Load<GameObject>("Weapons/" + name);
        if (src == null)
        {
            // Sem o modelo (ainda nao importado): uma peca simples no lugar, para o jogo continuar testavel.
            return Art.Box(parent, Vector3.zero, new Vector3(0.05f, 0.08f, 0.5f), Art.Mat(new Color(0.1f, 0.1f, 0.12f), Color.black), false, name);
        }
        GameObject g = Object.Instantiate(src, parent);
        g.name = name;
        g.transform.localPosition = Vector3.zero;
        g.transform.localRotation = Quaternion.identity;
        g.transform.localScale = Vector3.one;
        foreach (Renderer r in g.GetComponentsInChildren<Renderer>())
        {
            Material[] ms = r.sharedMaterials;
            for (int i = 0; i < ms.Length; i++) ms[i] = For(ms[i] != null ? ms[i].name : "", glow);
            r.sharedMaterials = ms;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        }
        foreach (Collider c in g.GetComponentsInChildren<Collider>()) Object.Destroy(c);
        return g;
    }

    public static GameObject Weapon(int index, Transform parent)
    {
        index = Mathf.Clamp(index, 0, Names.Length - 1);
        return Part(Names[index], parent, Weapons.All[index].color);
    }

    // Capsula deflagrada saindo da janela de ejecao.
    public static void Casing(int weapon, Vector3 pos, Quaternion rot, Vector3 inherit)
    {
        if (weapon > 1) return;
        GameObject holder = new GameObject("casing");
        holder.transform.SetPositionAndRotation(pos, rot);
        Part(weapon == 1 ? "Shell" : "Casing", holder.transform, Color.black);
        Rigidbody rb = holder.AddComponent<Rigidbody>();
        rb.mass = 0.02f;
        rb.linearVelocity = inherit + rot * new Vector3(Random.Range(1.6f, 2.4f), Random.Range(1.2f, 2f), Random.Range(-0.4f, 0.2f));
        rb.angularVelocity = Random.insideUnitSphere * 30f;
        SphereCollider sc = holder.AddComponent<SphereCollider>();
        sc.radius = 0.01f;
        holder.layer = 2;   // Ignore Raycast: nao atrapalha tiros
        Object.Destroy(holder, 2.5f);
    }
}
