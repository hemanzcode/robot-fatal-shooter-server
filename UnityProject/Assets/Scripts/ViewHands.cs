using UnityEngine;

// Maos e antebracos em primeira pessoa + animacao de recarga por arma.
// Tudo fica sob o vmRoot (espaco da arma): a mao direita segura o punho, a esquerda o guarda-mao,
// e na recarga a esquerda tira/poe o carregador (que e um objeto separado no modelo: "Mag"/"Pump").
public class ViewHands
{
    // Punho (mao direita) e apoio (mao esquerda) por arma: posicao e rotacao no espaco da arma.
    static readonly Vector3[] GripP = { new Vector3(0f, -0.072f, -0.03f), new Vector3(0f, -0.045f, -0.105f), new Vector3(0f, -0.095f, -0.015f), new Vector3(0f, -0.07f, -0.02f), new Vector3(0f, -0.085f, -0.035f) };
    static readonly Vector3[] GripR = { new Vector3(18f, 0f, 0f), new Vector3(-50f, 0f, 0f), new Vector3(18f, 0f, 0f), new Vector3(10f, 0f, 0f), new Vector3(18f, 0f, 0f) };
    static readonly Vector3[] SupP = { new Vector3(0f, 0.042f, 0.2f), new Vector3(0f, 0.019f, 0.34f), new Vector3(0f, -0.012f, 0.18f), new Vector3(0f, -0.07f, 0.185f), new Vector3(0f, -0.078f, 0.205f) };
    // Guarda-mao horizontal (VETOR, PULSO, SNIPER): mao "de lado", copiada da pose do CS:GO —
    // costas da mao na lateral esquerda, polegar por cima apontando para a frente, dedos por baixo (escondidos).
    static readonly Quaternion Under = Quaternion.identity;   // mao lateral (HandSide) ja vem montada no guarda-mao
    static readonly Quaternion[] SupR = { Under, Under, Under, Quaternion.Euler(10f, 0f, 0f), Quaternion.Euler(-22f, 0f, 0f) };

    // Ombros/cotovelos no espaco da CAMERA: o antebraco sai da mao em direcao a eles,
    // assim quando a arma gira na recarga os bracos continuam vindo de baixo da tela.
    static readonly Vector3 ShoulderR = new Vector3(0.3f, -0.75f, -0.05f);
    static readonly Vector3 ShoulderL = new Vector3(-0.3f, -0.55f, 0.1f);
    const float ForeLen = 0.3f;

    Transform root;
    Transform hR, hL, hLs, fR, fL;
    bool hasArmMesh;
    GameObject shell;
    Transform[] mag;
    Vector3[] magHome;
    bool[] magFound;
    float lastT = -1f;
    float lastShotT = 999f;
    int lastW = -1;
    bool startedEmpty;

    public ViewHands(Transform vmRoot, GameObject[] vms, int team)
    {
        root = vmRoot;
        // Estilo tatico: luva fechada de couro preto e manga de uniforme.
        Material glove = Art.Mat(new Color(0.075f, 0.07f, 0.066f), Color.black, 0f, default(Color), 0.45f, 0.02f);
        Material plate = Art.Mat(new Color(0.16f, 0.16f, 0.165f), Color.black, 0f, default(Color), 0.3f, 0.05f);
        Material sleeve = Art.Mat(new Color(0.34f, 0.35f, 0.28f), Color.black, 0f, default(Color), 0.08f, 0f);
        Material stripe = Art.Mat(Art.TeamColor(team) * 0.55f, Color.black, 0f, default(Color), 0.2f, 0f);

        hR = Hand("handR", glove, plate, false);
        hL = Hand("handL", glove, plate, true);
        hLs = HandSide("handLside", glove, plate);
        fR = Forearm("foreR", sleeve, plate, stripe, glove);
        fL = Forearm("foreL", sleeve, plate, stripe, glove);

        // Cartucho da escopeta (na mao esquerda durante a recarga).
        shell = new GameObject("shell");
        shell.transform.SetParent(hLs, false);
        shell.transform.localPosition = new Vector3(0f, 0f, 0.03f);
        shell.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        Art.Prim(PrimitiveType.Cylinder, shell.transform, new Vector3(0f, 0.008f, 0f), new Vector3(0.019f, 0.022f, 0.019f), Art.Mat(new Color(0.6f, 0.06f, 0.05f), Color.black, 0f, default(Color), 0.4f, 0f), false, "hull");
        Art.Prim(PrimitiveType.Cylinder, shell.transform, new Vector3(0f, -0.019f, 0f), new Vector3(0.0195f, 0.006f, 0.0195f), Art.Mat(new Color(0.75f, 0.55f, 0.2f), Color.black, 0f, default(Color), 0.8f, 1f), false, "brass");
        shell.SetActive(false);

        // Carregadores: um pivo no centro de cada peca, para girar/cair em torno dela mesma.
        int n = vms.Length;
        mag = new Transform[n];
        magHome = new Vector3[n];
        magFound = new bool[n];
        for (int i = 0; i < n; i++)
        {
            Transform part = null;
            foreach (Transform c in vms[i].GetComponentsInChildren<Transform>(true))
                if (c.name == "Mag" || c.name == "Pump") { part = c; break; }
            GameObject piv = new GameObject("magPivot");
            piv.transform.SetParent(vms[i].transform, false);
            if (part != null)
            {
                Renderer r = part.GetComponent<Renderer>();
                Vector3 local = r != null ? vms[i].transform.InverseTransformPoint(r.bounds.center) : Vector3.zero;
                piv.transform.localPosition = local;
                part.SetParent(piv.transform, true);
                magFound[i] = true;
            }
            mag[i] = piv.transform;
            magHome[i] = piv.transform.localPosition;
        }
    }

    // ---------- Construcao ----------

    static GameObject Seg(Transform parent, Vector3 a, Vector3 b, float thick, Material m, string name)
    {
        Vector3 d = b - a;
        GameObject g = Art.Prim(PrimitiveType.Capsule, parent, (a + b) * 0.5f, new Vector3(thick, Mathf.Max(d.magnitude * 0.5f + thick * 0.25f, thick * 0.5f), thick), m, false, name);
        g.transform.localRotation = Quaternion.FromToRotation(Vector3.up, d.normalized);
        g.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return g;
    }

    // Punho fechado em volta de um cabo vertical (eixo Y local, raio ~2 cm). Mao esquerda = espelhada.
    Transform Hand(string name, Material glove, Material plate, bool left)
    {
        GameObject h = new GameObject(name);
        h.transform.SetParent(root, false);
        Transform fist = new GameObject("fist").transform;
        fist.SetParent(h.transform, false);
        fist.localScale = new Vector3(left ? -1.12f : 1.12f, 1.12f, 1.12f);

        // Palma / costas da mao, do lado de fora (+X).
        // Palma arredondada (esfera achatada) e uma placa acolchoada nas costas da mao.
        GameObject palm = Art.Prim(PrimitiveType.Sphere, fist, new Vector3(0.028f, -0.002f, -0.012f), new Vector3(0.036f, 0.092f, 0.082f), glove, false, "palm");
        palm.transform.localRotation = Quaternion.Euler(0f, -18f, 0f);
        GameObject back = Art.Prim(PrimitiveType.Sphere, fist, new Vector3(0.04f, 0.006f, -0.008f), new Vector3(0.014f, 0.06f, 0.05f), plate, false, "backplate");
        back.transform.localRotation = Quaternion.Euler(0f, -18f, 0f);

        // Quatro dedos dando a volta no cabo pela frente.
        float R = 0.027f;
        for (int f = 0; f < 4; f++)
        {
            float y = 0.03f - f * 0.0195f;
            float[] ang = { 15f, 70f, 125f, 185f };
            for (int s = 0; s < 3; s++)
            {
                float a0 = ang[s] * Mathf.Deg2Rad, a1 = ang[s + 1] * Mathf.Deg2Rad;
                float r0 = s == 0 ? R + 0.004f : R, r1 = R - (s == 2 ? 0.002f : 0f);
                Vector3 p0 = new Vector3(Mathf.Cos(a0) * r0, y, Mathf.Sin(a0) * r0);
                Vector3 p1 = new Vector3(Mathf.Cos(a1) * r1, y - 0.002f * s, Mathf.Sin(a1) * r1);
                Seg(fist, p0, p1, 0.0175f - s * 0.001f, glove, "finger");
            }
            // Protecao do no dos dedos.
            if (f < 3) Art.Box(fist, new Vector3(0.036f, y - 0.002f, 0.022f), new Vector3(0.01f, 0.016f, 0.014f), plate, false, "knuckle");
        }
        // Polegar por cima, cruzando para o outro lado.
        Seg(fist, new Vector3(0.028f, 0.03f, -0.035f), new Vector3(0.0f, 0.048f, -0.012f), 0.021f, glove, "thumb1");
        Seg(fist, new Vector3(0.0f, 0.048f, -0.012f), new Vector3(-0.024f, 0.04f, 0.012f), 0.019f, glove, "thumb2");
        // Punho da luva.
        Seg(fist, new Vector3(0.026f, -0.012f, -0.05f), new Vector3(0.03f, -0.022f, -0.085f), 0.058f, glove, "cuff");
        return h.transform;
    }

    // Mao de apoio lateral (copiada da referencia do CS:GO), no espaco do guarda-mao:
    // origem = centro do guarda-mao, +Z para a frente, +Y para cima, -X = lado de fora (esquerda).
    const float HW = 0.026f;   // meia largura do guarda-mao
    static readonly Vector3 SideWrist = new Vector3(-HW - 0.036f, -0.084f, -0.072f);
    // Tamanho do guarda-mao de cada arma (o braco modelado e para o do VETOR).
    static readonly Vector3[] SideScale = { new Vector3(1f, 1f, 1f), new Vector3(0.94f, 0.94f, 1f), new Vector3(0.84f, 0.92f, 1f) };

    Transform HandSide(string name, Material glove, Material plate)
    {
        GameObject h = new GameObject(name);
        h.transform.SetParent(root, false);
        // Braco + luva modelados no Blender (Resources/Hands/ArmL): uma malha so, ja na pose da referencia.
        GameObject src = Resources.Load<GameObject>("Hands/ArmL");
        if (src != null)
        {
            Material sleeveM = Art.Mat(new Color(0.34f, 0.35f, 0.28f), Color.black, 0f, default(Color), 0.08f, 0f);
            GameObject g = Object.Instantiate(src, h.transform);
            g.name = "arm";
            g.transform.localPosition = Vector3.zero;
            g.transform.localRotation = Quaternion.identity;
            g.transform.localScale = Vector3.one * 1.06f;
            foreach (Renderer r in g.GetComponentsInChildren<Renderer>())
            {
                Material[] ms = r.sharedMaterials;
                for (int i = 0; i < ms.Length; i++)
                    ms[i] = (ms[i] != null && ms[i].name.ToLower().Contains("sleeve")) || r.name.ToLower().Contains("sleeve") ? sleeveM : glove;
                r.sharedMaterials = ms;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            foreach (Collider c in g.GetComponentsInChildren<Collider>()) Object.Destroy(c);
            hasArmMesh = true;
            return h.transform;
        }
        Transform t = new GameObject("hand").transform;
        t.SetParent(h.transform, false);
        t.localScale = Vector3.one * 1.2f;
        // Costas da mao, encostadas na lateral esquerda.
        GameObject back = Art.Prim(PrimitiveType.Sphere, t, new Vector3(-HW - 0.012f, -0.012f, 0f), new Vector3(0.03f, 0.075f, 0.09f), glove, false, "back");
        back.transform.localRotation = Quaternion.Euler(0f, 0f, -12f);
        GameObject pad = Art.Prim(PrimitiveType.Sphere, t, new Vector3(-HW - 0.024f, -0.008f, 0.004f), new Vector3(0.012f, 0.05f, 0.06f), plate, false, "pad");
        pad.transform.localRotation = Quaternion.Euler(0f, 0f, -12f);
        // Polegar por cima, na lateral esquerda, apontando para a frente (a ponta aparece).
        Seg(t, new Vector3(-HW - 0.006f, 0.018f, -0.03f), new Vector3(-HW, 0.03f, 0.015f), 0.021f, glove, "thumb1");
        Seg(t, new Vector3(-HW, 0.03f, 0.015f), new Vector3(-HW + 0.004f, 0.036f, 0.05f), 0.019f, glove, "thumb2");
        // Dedos por baixo do guarda-mao, dando a volta para o outro lado (ficam escondidos pela arma).
        for (int i = 0; i < 4; i++)
        {
            float z = 0.03f - i * 0.02f;
            Seg(t, new Vector3(-HW - 0.01f, -0.045f, z), new Vector3(0f, -0.055f, z), 0.018f, glove, "finger");
            Seg(t, new Vector3(0f, -0.055f, z), new Vector3(HW, -0.035f, z), 0.017f, glove, "finger");
        }
        // Punho da luva.
        Seg(t, new Vector3(-HW - 0.014f, -0.04f, -0.03f), new Vector3(-HW - 0.03f, -0.07f, -0.06f), 0.052f, glove, "cuff");
        return h.transform;
    }

    Transform Forearm(string name, Material sleeve, Material plate, Material stripe, Material glove)
    {
        GameObject g = new GameObject(name);
        g.transform.SetParent(root, false);
        // Ao longo de +Z, de 0 a 1 (a escala Z vira o comprimento).
        Transform t = g.transform;
        // Manga: um tubo liso (malha propria) que engrossa para o cotovelo, com rugas suaves perto do punho,
        // e o punho da luva por cima.
        Tube(t, "sleeve", sleeve, 0.04f, 1f, z => Mathf.Lerp(0.034f, 0.041f, z) + 0.0018f * Mathf.Sin(z * 38f) * Mathf.Clamp01(1f - z * 1.6f), 0.92f);
        Tube(t, "gloveCuff", glove, -0.02f, 0.09f, z => 0.0345f, 0.9f);
        GameObject strap = Art.Box(t, new Vector3(0f, 0.031f, 0.04f), new Vector3(0.036f, 0.007f, 0.04f), plate, false, "strap");
        GameObject patch = Art.Box(t, new Vector3(-0.039f, 0.012f, 0.62f), new Vector3(0.006f, 0.04f, 0.07f), stripe, false, "patch");
        foreach (Renderer r in g.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return t;
    }

    // ---------- Animacao ----------

    // Tubo ao longo de +Z (z0..z1) com raio r(z) e secao levemente achatada (fy), tampado nas pontas.
    static void Tube(Transform parent, string name, Material m, float z0, float z1, System.Func<float, float> r, float fy)
    {
        const int seg = 18, rings = 24;
        var v = new System.Collections.Generic.List<Vector3>();
        var tri = new System.Collections.Generic.List<int>();
        for (int i = 0; i <= rings; i++)
        {
            float z = Mathf.Lerp(z0, z1, i / (float)rings);
            float rad = r(z);
            for (int j = 0; j < seg; j++)
            {
                float a = j * Mathf.PI * 2f / seg;
                v.Add(new Vector3(Mathf.Cos(a) * rad, Mathf.Sin(a) * rad * fy, z));
            }
        }
        for (int i = 0; i < rings; i++)
            for (int j = 0; j < seg; j++)
            {
                int a = i * seg + j, b = i * seg + (j + 1) % seg, c = a + seg, d = b + seg;
                tri.Add(a); tri.Add(b); tri.Add(c);
                tri.Add(b); tri.Add(d); tri.Add(c);
            }
        // Tampas
        int c0 = v.Count; v.Add(new Vector3(0f, 0f, z0));
        int c1 = v.Count; v.Add(new Vector3(0f, 0f, z1));
        int last = rings * seg;
        for (int j = 0; j < seg; j++)
        {
            int jn = (j + 1) % seg;
            tri.Add(c0); tri.Add(jn); tri.Add(j);
            tri.Add(c1); tri.Add(last + j); tri.Add(last + jn);
        }
        Mesh mesh = new Mesh();
        mesh.name = name;
        mesh.SetVertices(v);
        mesh.SetTriangles(tri, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        GameObject g = new GameObject(name);
        g.transform.SetParent(parent, false);
        g.AddComponent<MeshFilter>().sharedMesh = mesh;
        MeshRenderer mr = g.AddComponent<MeshRenderer>();
        mr.sharedMaterial = m;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    static float S(float t, float a, float b) { return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(a, b, t)); }

    struct Pose { public Vector3 p; public Quaternion q; public Pose(Vector3 p, Vector3 e) { this.p = p; q = Quaternion.Euler(e); } public Pose(Vector3 p, Quaternion q) { this.p = p; this.q = q; } }

    static Pose Mix(Pose a, Pose b, float k) { return new Pose(Vector3.Lerp(a.p, b.p, k), Quaternion.Slerp(a.q, b.q, k)); }

    // Sequencia de poses (tempo, pose) com transicoes suaves.
    static Pose Track(float t, float[] ts, Pose[] ps)
    {
        if (t <= ts[0]) return ps[0];
        for (int i = 1; i < ts.Length; i++)
            if (t < ts[i]) return Mix(ps[i - 1], ps[i], Mathf.SmoothStep(0f, 1f, (t - ts[i - 1]) / Mathf.Max(0.0001f, ts[i] - ts[i - 1])));
        return ps[ps.Length - 1];
    }

    void Cue(float t, float at, string id, float vol = 0.7f)
    {
        if (lastT < at && t >= at) Sfx.UI(id, vol);
    }

    // Chamado por quadro. reloadT = 0..1 durante a recarga (ou -1). Devolve o deslocamento/rotacao extra da arma.
    public void Update(int w, bool visible, float reloadT, float reloadDur, float sinceShot, int shells, bool empty, out Vector3 dPos, out Vector3 dRot)
    {
        dPos = Vector3.zero; dRot = Vector3.zero;
        bool show = visible;
        bool side = w <= 2;
        if (hR.gameObject.activeSelf != show) { hR.gameObject.SetActive(show); fR.gameObject.SetActive(show); }
        bool foreL = show && !(side && hasArmMesh);   // o braco modelado ja traz o antebraco
        if (fL.gameObject.activeSelf != foreL) fL.gameObject.SetActive(foreL);
        if (side) hLs.localScale = SideScale[w];
        if (hLs.gameObject.activeSelf != (show && side)) hLs.gameObject.SetActive(show && side);
        if (hL.gameObject.activeSelf != (show && !side)) hL.gameObject.SetActive(show && !side);
        if (w != lastW) { lastT = -1f; lastShotT = 999f; lastW = w; }
        if (reloadT < 0f) lastT = -1f;
        else if (lastT < 0f) { lastT = 0f; startedEmpty = empty; }

        Pose R = new Pose(GripP[w], GripR[w]);
        Pose L = new Pose(SupP[w], SupR[w]);
        Vector3 home = magHome[w];
        Vector3 mPos = home; Quaternion mRot = Quaternion.identity; bool mShow = true;
        bool shellShow = false;

        if (reloadT >= 0f)
        {
            float t = reloadT;
            switch (w)
            {
                case 0: Rifle(t, reloadDur, ref L, ref mPos, ref mRot, ref mShow, ref dPos, ref dRot); break;
                case 1: Shotgun(t, shells, ref L, ref mPos, ref shellShow, ref dPos, ref dRot); break;
                case 2: Rail(t, reloadDur, ref L, ref R, ref mPos, ref mRot, ref mShow, ref dPos, ref dRot); break;
                case 3: Launcher(t, ref L, ref mPos, ref mRot, ref mShow, ref dPos, ref dRot); break;
                case 4: Plasma(t, ref L, ref mPos, ref mRot, ref mShow, ref dPos, ref dRot); break;
            }
            lastT = t;
        }
        else
        {
            // Bomba da escopeta / ferrolho da sniper depois de cada tiro.
            if (sinceShot < lastShotT) lastShotT = 0f;
            if (w == 1 && lastShotT < 0.14f && sinceShot >= 0.14f) Sfx.UI("pump", 0.72f);
            if (w == 2 && lastShotT < 0.34f && sinceShot >= 0.34f) Sfx.UI("charge", 0.65f);
            lastShotT = sinceShot;

            if (w == 1 && sinceShot > 0.12f && sinceShot < 0.55f)
            {
                float u = (sinceShot - 0.12f) / 0.43f;
                float k = Mathf.Sin(Mathf.PI * u);
                L.p += new Vector3(0f, 0f, -0.085f * k);
                mPos = home + new Vector3(0f, 0f, -0.085f * k);
                dRot += new Vector3(-3f * k, 0f, 4f * k);
            }
            if (w == 2 && sinceShot > 0.3f && sinceShot < 1.15f)
            {
                float u = (sinceShot - 0.3f) / 0.85f;
                R = Bolt(u, R);
            }
            if (w == 3 && empty) mShow = false;   // bazuca descarregada: sem foguete na boca
        }

        Place(hR, fR, R, ShoulderR);
        Place(side ? hLs : hL, fL, L, ShoulderL);

        if (magFound[w])
        {
            mag[w].localPosition = mPos;
            mag[w].localRotation = mRot;
            if (mag[w].gameObject.activeSelf != mShow) mag[w].gameObject.SetActive(mShow);
        }
        if (shell.activeSelf != shellShow) shell.SetActive(shellShow);
    }

    void Place(Transform hand, Transform fore, Pose p, Vector3 shoulder)
    {
        hand.localPosition = p.p;
        hand.localRotation = p.q;
        Vector3 wl = hand == hLs ? SideWrist : new Vector3(hand == hL ? -0.03f : 0.03f, -0.025f, -0.08f);
        Vector3 wrist = p.p + p.q * wl;
        Vector3 sh = Quaternion.Inverse(root.localRotation) * (shoulder - root.localPosition);
        Vector3 dir = sh - wrist;
        if (dir.sqrMagnitude < 0.0001f) dir = Vector3.back;
        fore.localPosition = wrist;
        fore.localRotation = Quaternion.LookRotation(dir.normalized, Vector3.up);
        fore.localScale = new Vector3(1f, 1f, ForeLen);
    }

    // Segura o carregador por baixo; deslocamento fixo mao->carregador enquanto ele "anda" com a mao.
    static readonly Vector3 MagGrab = new Vector3(0f, -0.065f, 0f);
    static readonly Vector3 MagGrabRot = new Vector3(8f, 0f, 0f);

    Vector3 Carry(Pose hand, Pose grab, Vector3 home, out Quaternion rot)
    {
        Vector3 off = Quaternion.Inverse(grab.q) * (home - grab.p);
        rot = hand.q * Quaternion.Inverse(grab.q);
        return hand.p + hand.q * off;
    }

    void Rifle(float t, float dur, ref Pose L, ref Vector3 mPos, ref Quaternion mRot, ref bool mShow, ref Vector3 dPos, ref Vector3 dRot)
    {
        float k = S(t, 0f, 0.12f) * (1f - S(t, 0.86f, 1f));
        dRot = new Vector3(-8f, -16f, 24f) * k;
        dPos = new Vector3(-0.04f, 0.05f, 0.02f) * k;

        Vector3 home = magHome[0];
        Pose grab = new Pose(home + MagGrab, MagGrabRot);
        Pose rest = L;
        Pose outP = new Pose(home + MagGrab + new Vector3(-0.01f, -0.13f, -0.03f), new Vector3(25f, 0f, 10f));
        Pose away = new Pose(new Vector3(-0.2f, -0.45f, 0.05f), new Vector3(40f, 0f, 30f));
        Pose below = new Pose(home + MagGrab + new Vector3(0f, -0.11f, -0.01f), new Vector3(16f, 0f, 0f));
        // Tapa no retem do ferrolho, na lateral esquerda do receptor.
        Pose charge = new Pose(new Vector3(-0.07f, 0.0f, 0.1f), new Vector3(0f, 0f, 70f));
        Pose pulled = new Pose(new Vector3(-0.042f, 0.0f, 0.1f), new Vector3(0f, 0f, 70f));
        float[] ts = { 0f, 0.12f, 0.29f, 0.41f, 0.5f, 0.6f, 0.66f, 0.7f, 0.76f, 0.81f, 0.86f, 0.98f };
        Pose[] ps = { rest, grab, outP, away, below, grab, new Pose(grab.p + new Vector3(0f, -0.025f, 0f), grab.q), grab, charge, pulled, charge, rest };
        L = Track(t, ts, ps);

        if (t >= 0.12f && t < 0.29f) mPos = Carry(L, grab, home, out mRot);
        else if (t >= 0.29f && t < 0.42f)
        {
            Quaternion q;
            Vector3 rel = Carry(Track(0.29f, ts, ps), grab, home, out q);
            float s = (t - 0.29f) * dur;
            mPos = rel + new Vector3(-0.08f * s, -4.9f * s * s - 0.2f * s, 0f);
            mRot = q * Quaternion.Euler(-200f * s, 0f, 140f * s);
        }
        else if (t >= 0.42f && t < 0.44f) mShow = false;
        else if (t >= 0.44f && t < 0.6f) mPos = Carry(L, grab, home, out mRot);
        Cue(t, 0.13f, "magout");
        Cue(t, 0.6f, "magin");
        Cue(t, 0.79f, "charge");
    }

    void Rail(float t, float dur, ref Pose L, ref Pose R, ref Vector3 mPos, ref Quaternion mRot, ref bool mShow, ref Vector3 dPos, ref Vector3 dRot)
    {
        float k = S(t, 0f, 0.1f) * (1f - S(t, 0.88f, 1f));
        dRot = new Vector3(-7f, -15f, 22f) * k;
        dPos = new Vector3(-0.04f, 0.05f, 0.02f) * k;

        // Ferrolho: abre no comeco, fecha no fim.
        float bu = t < 0.5f ? Mathf.InverseLerp(0.02f, 0.3f, t) * 0.5f : 0.5f + Mathf.InverseLerp(0.72f, 0.96f, t) * 0.5f;
        R = Bolt(bu, R);

        Vector3 home = magHome[2];
        Pose grab = new Pose(home + new Vector3(0f, -0.06f, 0f), MagGrabRot);
        Pose rest = L;
        Pose outP = new Pose(grab.p + new Vector3(-0.01f, -0.12f, -0.02f), new Vector3(25f, 0f, 10f));
        Pose away = new Pose(new Vector3(-0.22f, -0.45f, 0.08f), new Vector3(40f, 0f, 30f));
        Pose below = new Pose(grab.p + new Vector3(0f, -0.1f, 0f), new Vector3(16f, 0f, 0f));
        float[] ts = { 0f, 0.3f, 0.38f, 0.5f, 0.58f, 0.66f, 0.74f, 0.9f };
        Pose[] ps = { rest, grab, outP, away, below, grab, grab, rest };
        L = Track(t, ts, ps);

        if (t >= 0.3f && t < 0.38f) mPos = Carry(L, grab, home, out mRot);
        else if (t >= 0.38f && t < 0.48f)
        {
            Quaternion q;
            Vector3 rel = Carry(outP, grab, home, out q);
            float s = (t - 0.38f) * dur;
            mPos = rel + new Vector3(-0.06f * s, -4.9f * s * s - 0.2f * s, 0f);
            mRot = q * Quaternion.Euler(-180f * s, 0f, 120f * s);
        }
        else if (t >= 0.48f && t < 0.52f) mShow = false;
        else if (t >= 0.52f && t < 0.66f) mPos = Carry(L, grab, home, out mRot);
        Cue(t, 0.08f, "charge");
        Cue(t, 0.31f, "magout");
        Cue(t, 0.66f, "magin");
        Cue(t, 0.76f, "charge");
    }

    // Mao direita no ferrolho: u 0..0.5 abre (sobe e puxa), 0.5..1 fecha e volta ao punho.
    static Pose Bolt(float u, Pose grip)
    {
        Pose atBolt = new Pose(new Vector3(0.055f, 0.0f, -0.02f), new Vector3(0f, 0f, 70f));
        Pose up = new Pose(new Vector3(0.05f, 0.03f, -0.02f), new Vector3(0f, 0f, 20f));
        Pose back = new Pose(new Vector3(0.05f, 0.03f, -0.1f), new Vector3(0f, 0f, 20f));
        float[] ts = { 0f, 0.14f, 0.24f, 0.36f, 0.5f, 0.64f, 0.76f, 0.86f, 1f };
        Pose[] ps = { grip, atBolt, up, back, back, up, atBolt, atBolt, grip };
        return Track(u, ts, ps);
    }

    void Shotgun(float t, int shells, ref Pose L, ref Vector3 mPos, ref bool shellShow, ref Vector3 dPos, ref Vector3 dRot)
    {
        float k = S(t, 0f, 0.1f) * (1f - S(t, 0.8f, 0.9f));
        dRot = new Vector3(-8f, -16f, 24f) * k;    // gira para mostrar a janela de carga (embaixo)
        dPos = new Vector3(-0.04f, 0.05f, 0.02f) * k;

        Pose rest = L;
        Pose port = new Pose(new Vector3(0f, -0.05f, 0.08f), new Vector3(-10f, 0f, 0f));
        Pose pushed = new Pose(new Vector3(0f, -0.042f, 0.125f), new Vector3(-10f, 0f, 0f));
        Pose low = new Pose(new Vector3(-0.08f, -0.3f, 0.02f), new Vector3(30f, 0f, 20f));
        int n = Mathf.Clamp(shells, 1, 6);
        float a = 0.1f, b = 0.8f;
        if (t < a) L = Mix(rest, low, S(t, 0f, a));
        else if (t < b)
        {
            float u = (t - a) / (b - a) * n;
            int i = Mathf.Min((int)u, n - 1);
            float f = u - i;
            if (f < 0.45f) L = Mix(low, port, S(f, 0f, 0.45f));
            else if (f < 0.62f) L = Mix(port, pushed, S(f, 0.45f, 0.62f));
            else L = Mix(pushed, low, S(f, 0.62f, 1f));
            shellShow = f < 0.6f;
            float cueAt = a + (i + 0.6f) / n * (b - a);
            Cue(t, cueAt, "shell");
        }
        else if (t < 0.88f) L = Mix(low, rest, S(t, b, 0.88f));
        else
        {
            float u = Mathf.InverseLerp(0.88f, 1f, t);
            float pk = Mathf.Sin(Mathf.PI * u);
            L = rest;
            L.p += new Vector3(0f, 0f, -0.085f * pk);
            mPos = magHome[1] + new Vector3(0f, 0f, -0.085f * pk);
        }
        Cue(t, 0.885f, "pump");
    }

    void Launcher(float t, ref Pose L, ref Vector3 mPos, ref Quaternion mRot, ref bool mShow, ref Vector3 dPos, ref Vector3 dRot)
    {
        float k = S(t, 0f, 0.15f) * (1f - S(t, 0.82f, 1f));
        dRot = new Vector3(-14f, -18f, 10f) * k;   // boca para cima e para o centro
        dPos = new Vector3(-0.1f, 0.02f, -0.05f) * k;

        Vector3 home = magHome[3];
        Pose grab = new Pose(home + new Vector3(0f, -0.06f, -0.02f), new Vector3(90f, 0f, 0f));
        Pose rest = L;
        Pose front = new Pose(grab.p + new Vector3(0f, 0f, 0.3f), grab.q);
        Pose away = new Pose(new Vector3(-0.15f, -0.45f, 0.45f), new Vector3(60f, 0f, 20f));
        float[] ts = { 0f, 0.15f, 0.27f, 0.38f, 0.5f, 0.66f, 0.72f, 0.9f };
        Pose[] ps = { rest, startedEmpty ? away : grab, startedEmpty ? away : front, away, front, grab, grab, rest };
        L = Track(t, ts, ps);

        if (t < 0.15f) mShow = !startedEmpty;
        else if (t < 0.38f)
        {
            if (startedEmpty) mShow = false;
            else mPos = Carry(L, grab, home, out mRot);
        }
        else if (t < 0.42f) mShow = false;
        else if (t < 0.66f) mPos = Carry(L, grab, home, out mRot);
        Cue(t, 0.6f, "rocketin", 0.8f);
    }

    void Plasma(float t, ref Pose L, ref Vector3 mPos, ref Quaternion mRot, ref bool mShow, ref Vector3 dPos, ref Vector3 dRot)
    {
        float k = S(t, 0f, 0.12f) * (1f - S(t, 0.85f, 1f));
        dRot = new Vector3(10f, -15f, -25f) * k;    // mostra o topo, onde fica a celula
        dPos = new Vector3(-0.1f, 0.06f, 0f) * k;

        Vector3 home = magHome[4];
        Pose grab = new Pose(home + new Vector3(-0.012f, 0.03f, 0f), new Vector3(90f, 0f, 90f));
        Pose rest = L;
        Pose lifted = new Pose(grab.p + new Vector3(-0.05f, 0.1f, -0.01f), new Vector3(90f, 0f, 70f));
        Pose away = new Pose(new Vector3(-0.42f, -0.05f, 0.05f), new Vector3(60f, 0f, 40f));
        Pose above = new Pose(grab.p + new Vector3(-0.03f, 0.1f, 0f), new Vector3(90f, 0f, 80f));
        float[] ts = { 0f, 0.14f, 0.28f, 0.4f, 0.47f, 0.6f, 0.7f, 0.76f, 0.92f };
        Pose[] ps = { rest, grab, lifted, away, away, above, grab, grab, rest };
        L = Track(t, ts, ps);

        if (t >= 0.14f && t < 0.4f) mPos = Carry(L, grab, home, out mRot);
        else if (t >= 0.4f && t < 0.47f) mShow = false;
        else if (t >= 0.47f && t < 0.7f) mPos = Carry(L, grab, home, out mRot);
        Cue(t, 0.16f, "cellout");
        Cue(t, 0.69f, "cellin");
    }
}
