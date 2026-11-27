using UnityEngine;
using UnityEngine.Rendering.Universal;

// Fundo cinematografico do menu (identidade nova): a cena do proprio mapa e fotografada UMA vez numa imagem
// em meia resolucao (fica levemente desfocada = profundidade de campo barata) e posta num quadro atras do
// agente. A cada quadro a camera do menu so desenha a camada do menu: agente, quadro de fundo, poeira e fumaca.
// Assim o menu fica bonito sem o custo de desenhar os milhares de objetos da cidade (Etapa 1 de desempenho).
public static class MenuBackdrop
{
    public const int Layer = 30;                       // camada so do menu (agente, fundo, particulas)

    // Enquadramento da cena (ajustavel): camera parada, um pouco acima dos telhados, olhando a rua central.
    static readonly Vector3 CamPos = new Vector3(-26f, 9.5f, -30f);
    static readonly Vector3 CamLook = new Vector3(4f, 3f, 4f);
    static readonly Color Grade = new Color(0.40f, 0.45f, 0.53f);   // tom frio e escuro da proposta

    static RenderTexture rt;
    static Texture2D shot;   // copia sem canal de transparencia (no navegador o alfa mistura com a pagina)
    static GameObject quad, fx;
    static Material quadMat;
    static int shotMap = -2, shotW, shotH;
    static float shotFov;

    public static void Tick(Camera cam, Transform agentHolder)
    {
        cam.transform.position = CamPos;
        cam.transform.rotation = Quaternion.LookRotation(CamLook - CamPos);
        int w = Mathf.Max(320, Screen.width / 2), h = Mathf.Max(180, Screen.height / 2);
        if (rt == null || shotMap != Arena.mapId || w != shotW || h != shotH || !Mathf.Approximately(shotFov, cam.fieldOfView))
            Capture(cam, agentHolder, w, h);
        cam.cullingMask = 1 << Layer;
        if (agentHolder != null) SetLayer(agentHolder, Layer);
        PlaceQuad(cam);
        if (fx == null) fx = BuildFx(cam.transform);
    }

    // Antes de outra coisa usar a camera do menu (ex.: fotos dos mapas no editor): volta a desenhar tudo.
    public static void Release(Camera cam)
    {
        if (cam != null) cam.cullingMask = ~0;
        if (quad != null) quad.SetActive(false);
        if (fx != null) fx.SetActive(false);
    }

    public static void Invalidate() { shotMap = -2; }

    static void Capture(Camera cam, Transform agentHolder, int w, int h)
    {
        if (rt != null) { rt.Release(); Object.Destroy(rt); }
        rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
        rt.filterMode = FilterMode.Bilinear;
        bool agentOn = agentHolder != null && agentHolder.gameObject.activeSelf;
        if (agentHolder != null) agentHolder.gameObject.SetActive(false);
        if (quad != null) quad.SetActive(false);
        if (fx != null) fx.SetActive(false);
        UniversalAdditionalCameraData data = cam.GetUniversalAdditionalCameraData();
        bool post = data.renderPostProcessing;
        data.renderPostProcessing = false;  // foto crua: o tratamento de cor da camera entra uma vez so, por cima de tudo
        cam.cullingMask = ~(1 << Layer);
        RenderTexture old = cam.targetTexture;
        cam.targetTexture = rt;
        cam.Render();
        cam.targetTexture = old;
        data.renderPostProcessing = post;
        if (agentHolder != null) agentHolder.gameObject.SetActive(agentOn);
        if (quad != null) quad.SetActive(true);
        if (fx != null) fx.SetActive(true);
        shotMap = Arena.mapId; shotW = w; shotH = h; shotFov = cam.fieldOfView;
        // Copia para uma textura RGB (opaca): sem isso o navegador deixa o fundo acinzentado.
        RenderTexture.active = rt;
        if (shot == null || shot.width != w || shot.height != h)
        {
            if (shot != null) Object.Destroy(shot);
            shot = new Texture2D(w, h, TextureFormat.RGB24, false);
            shot.filterMode = FilterMode.Bilinear;
            shot.wrapMode = TextureWrapMode.Clamp;
        }
        shot.ReadPixels(new Rect(0, 0, w, h), 0, 0, false);
        shot.Apply(false);
        RenderTexture.active = null;
        if (quadMat != null) quadMat.mainTexture = shot;
    }

    static void PlaceQuad(Camera cam)
    {
        if (quad == null)
        {
            // Quadro montado na mao (CreatePrimitive pede MeshCollider, que o build Web nao inclui).
            quad = new GameObject("MenuBackdrop");
            quad.layer = Layer;
            Mesh mesh = new Mesh();
            mesh.vertices = new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f), new Vector3(0.5f, 0.5f, 0f) };
            mesh.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) };
            mesh.triangles = new[] { 0, 2, 1, 2, 3, 1 };
            mesh.RecalculateBounds();
            quad.AddComponent<MeshFilter>().sharedMesh = mesh;
            quad.AddComponent<MeshRenderer>();
            quadMat = new Material(Shader.Find("Sprites/Default"));
            quadMat.mainTexture = shot;
            quadMat.color = Grade;
            MeshRenderer mr = quad.GetComponent<MeshRenderer>();
            mr.sharedMaterial = quadMat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
        }
        if (!quad.activeSelf) quad.SetActive(true);
        if (fx != null && !fx.activeSelf) fx.SetActive(true);
        const float d = 80f;
        float hgt = 2f * d * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
        quad.transform.SetParent(cam.transform, false);
        quad.transform.localPosition = new Vector3(0f, 0f, d);
        quad.transform.localRotation = Quaternion.identity;
        quad.transform.localScale = new Vector3(hgt * cam.aspect, hgt, 1f);
    }

    static void SetLayer(Transform t, int layer)
    {
        if (t.gameObject.layer == layer) return;   // ja foi (o agente so muda quando troca de personagem)
        foreach (Transform c in t.GetComponentsInChildren<Transform>(true)) c.gameObject.layer = layer;
    }

    // Poeira no ar e fumaca baixa, bem discretas (poucas particulas; menos ainda no celular).
    static GameObject BuildFx(Transform cam)
    {
        GameObject root = new GameObject("MenuFx");
        root.transform.SetParent(cam, false);
        root.layer = Layer;
        Texture2D soft = SoftDot();
        Material mat = new Material(Shader.Find("Sprites/Default"));
        mat.mainTexture = soft;
        bool phone = TouchPad.On;

        // Poeira: pontinhos claros flutuando entre a camera e o agente.
        ParticleSystem dust = NewSystem(root.transform, "Poeira", mat, new Vector3(0f, 0f, 4f));
        var m = dust.main;
        m.startLifetime = 9f; m.startSpeed = 0.06f; m.startSize = new ParticleSystem.MinMaxCurve(0.012f, 0.03f);
        m.startColor = new Color(1f, 0.95f, 0.85f, 0.35f); m.maxParticles = phone ? 25 : 60;
        var em = dust.emission; em.rateOverTime = phone ? 3f : 7f;
        var sh = dust.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(7f, 4f, 4f);
        var vel = dust.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.Local;
        vel.x = new ParticleSystem.MinMaxCurve(0.02f, 0.08f); vel.y = new ParticleSystem.MinMaxCurve(-0.01f, 0.03f); vel.z = new ParticleSystem.MinMaxCurve(0f, 0f);
        FadeInOut(dust);
        dust.Simulate(9f, true, true); dust.Play();

        // Fumaca: poucas nuvens grandes e transparentes passando devagar na parte de baixo.
        ParticleSystem smoke = NewSystem(root.transform, "Fumaca", mat, new Vector3(0f, -2.2f, 12f));
        m = smoke.main;
        m.startLifetime = 26f; m.startSpeed = 0f; m.startSize = new ParticleSystem.MinMaxCurve(5f, 9f);
        m.startRotation = new ParticleSystem.MinMaxCurve(0f, 6.28f);
        m.startColor = new Color(0.55f, 0.57f, 0.6f, 0.10f); m.maxParticles = phone ? 6 : 12;
        em = smoke.emission; em.rateOverTime = phone ? 0.25f : 0.5f;
        sh = smoke.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(18f, 1.5f, 4f);
        vel = smoke.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.Local;
        vel.x = new ParticleSystem.MinMaxCurve(0.25f, 0.45f); vel.y = new ParticleSystem.MinMaxCurve(0.02f, 0.08f); vel.z = new ParticleSystem.MinMaxCurve(0f, 0f);
        var rot = smoke.rotationOverLifetime; rot.enabled = true; rot.z = new ParticleSystem.MinMaxCurve(-0.05f, 0.05f);
        FadeInOut(smoke);
        smoke.Simulate(26f, true, true); smoke.Play();
        return root;
    }

    static ParticleSystem NewSystem(Transform parent, string name, Material mat, Vector3 local)
    {
        GameObject g = new GameObject(name);
        g.layer = Layer;
        g.transform.SetParent(parent, false);
        g.transform.localPosition = local;
        ParticleSystem ps = g.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var m = ps.main;
        m.simulationSpace = ParticleSystemSimulationSpace.Local;
        m.useUnscaledTime = true;
        m.playOnAwake = false;
        ParticleSystemRenderer r = g.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        return ps;
    }

    static void FadeInOut(ParticleSystem ps)
    {
        var col = ps.colorOverLifetime; col.enabled = true;
        Gradient g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                  new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.25f), new GradientAlphaKey(1f, 0.7f), new GradientAlphaKey(0f, 1f) });
        col.color = g;
    }

    static Texture2D SoftDot()
    {
        const int n = 64;
        Texture2D t = new Texture2D(n, n, TextureFormat.RGBA32, false);
        t.wrapMode = TextureWrapMode.Clamp;
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
                float a = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
                t.SetPixel(x, y, new Color(1f, 1f, 1f, a * a));
            }
        t.Apply();
        return t;
    }
}
