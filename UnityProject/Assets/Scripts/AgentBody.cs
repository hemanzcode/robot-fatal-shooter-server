using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

// Dados de um agente jogavel (identidade e aparencia; as habilidades exclusivas entram na Fase 5).
public class AgentDef
{
    public string name, file, role, blurb, portrait, skill, skill2, ultimate;
    public float height;
    public Color accent, cloth;
    public AgentDef(string n, string f, string r, string b, float h, Color a, Color c, string s, string u)
    { name = n; file = f; role = r; blurb = b; height = h; accent = a; cloth = c; portrait = f; skill = s; skill2 = ""; ultimate = u; }
}

// Corpo humano de um agente: modelo com esqueleto humanoide, animacao de captura de movimento
// (parado / andar / correr), bracos presos a arma por IK e tronco que acompanha a mira.
public class AgentBody : MonoBehaviour
{
    public static readonly AgentDef[] Defs =
    {
        new AgentDef("JASON", "Jason", "Reconhecimento", "Revela inimigos com um drone.", 1.80f, new Color(0.1f, 0.9f, 1f), new Color(0.5f, 0.55f, 0.6f), "DRONE DE RECONHECIMENTO", "CEGUE INIMIGOS: 20 M / 10 S"),
        new AgentDef("TODD", "Todd", "Duelista de fogo", "Molotov, lava e lanca-chamas.", 1.82f, new Color(1f, 0.45f, 0.08f), new Color(0.55f, 0.5f, 0.5f), "MOLOTOV DE LAVA", "LANCA-CHAMAS: 10 S"),
        new AgentDef("KATE", "Kate", "Explosivos", "Minas e bombardeio aereo.", 1.70f, new Color(1f, 0.85f, 0.1f), new Color(0.45f, 0.4f, 0.35f), "DRONE / 3 MINAS", "BOMBARDEIO AEREO"),
        new AgentDef("HAROLD", "Harold", "Vigilancia", "Vigilancia e apoio de meca.", 1.78f, new Color(0.3f, 1f, 0.45f), new Color(0.3f, 0.36f, 0.3f), "CAMERAS + MECA", "CAO DE PLASMA"),
        new AgentDef("DEATH GIRL", "DeathGirl", "Combate tatico", "ClarÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â£o e cura propria.", 1.66f, new Color(1f, 0.2f, 0.75f), new Color(0.6f, 0.6f, 0.65f), "CLARAO + CURA", "INTANGIBILIDADE: 6 S"),
        new AgentDef("AURORA", "Aurora", "Mobilidade", "Pulo alto, dash longo e velocidade.", 1.74f, new Color(0.55f, 0.8f, 1f), new Color(0.6f, 0.6f, 0.65f), "IMPULSO DE VELOCIDADE", "3 LANCAS MORTAIS"),
        new AgentDef("BRUTUS", "Brutus", "Controle de combate", "Onda sonora e terremoto. 250 HP.", 1.90f, new Color(1f, 0.15f, 0.12f), new Color(0.35f, 0.33f, 0.33f), "ONDA SONORA DIRECIONAL", "TERREMOTO: RAIO 10 M"),
        new AgentDef("SANKHYA", "Sankya", "Suporte", "Cura tecnologica e protecao de aliados.", 1.68f, new Color(0.2f, 1f, 0.8f), new Color(0.6f, 0.65f, 0.65f), "GRANADA", "SOBRECARGA"),
        new AgentDef("OMNIS", "Omnis", "Furtividade", "Teleporte, distorcao visual e ocultacao.", 1.80f, new Color(0.6f, 0.3f, 1f), new Color(0.4f, 0.4f, 0.45f), "GRANADA", "SOBRECARGA"),
        new AgentDef("SADE", "Sade", "Controle quimico", "Nuvens toxicas e barreiras de contaminacao.", 1.74f, new Color(0.6f, 1f, 0.1f), new Color(0.5f, 0.55f, 0.45f), "GRANADA", "SOBRECARGA"),
        new AgentDef("ZERO", "Zero", "Interferencia eletronica", "Derruba HUD, sensores e habilidades inimigas.", 1.78f, new Color(0.2f, 0.45f, 1f), new Color(0.5f, 0.52f, 0.6f), "GRANADA", "SOBRECARGA"),
        new AgentDef("TITAN", "Titan", "Combate pesado", "Blindagem frontal e poder de fogo de supressao.", 1.94f, new Color(1f, 0.6f, 0.15f), new Color(0.5f, 0.48f, 0.45f), "GRANADA", "SOBRECARGA")
    };

    static AgentBody()
    {
        Defs[0].skill = "DRONE DE RECONHECIMENTO"; Defs[0].skill2 = "DISPERSAO / FUMACA";
        Defs[1].skill = "POCA DE LAVA"; Defs[1].skill2 = "MOLOTOV DE AREA";
        Defs[2].skill = "3 MINAS"; Defs[2].skill2 = "DRONE BOMBARDEIRO";
        Defs[3].skill = "CAMERAS DE VIGILANCIA"; Defs[3].skill2 = "ENVIAR MECA";
        Defs[4].skill = "CLARAO DE DISPERSAO"; Defs[4].skill2 = "AUTOCURA";
    }

    public float speed;                 // velocidade horizontal (m/s)
    public Vector3 moveDir;             // direcao do movimento no mundo
    public Vector3 aimDir = Vector3.forward;
    public bool ok;

    AgentDef def;
    GameObject model, gun;
    Animator anim;
    Transform[] poseBones;
    Quaternion[] bindPose;
    Transform thighL, thighR, calfL, calfR, footL, footR;
    Transform hips;
    Actor owner;
    bool proceduralRig;
    bool directBoneLookup;
    PlayableGraph graph;
    AnimationMixerPlayable mixer;
    AnimationClipPlayable[] plays = new AnimationClipPlayable[3];
    float[] clipSpeed = { 1f, 1.41f, 3.2f };
    Transform spine, spine1, spine2, head, armL, foreL, handL, armR, foreR, handR;
    Transform midL, idxL, pinL, midR, idxR, pinR;
    float scale = 1f, yaw, dead = -1f, wWalk, wRun, breath;
    float gaitPhase;
    static AnimationClip[] clips;

    public static AgentBody Create(Transform parent, AgentDef d, int team, bool bands)
    {
        GameObject g = new GameObject("body");
        g.transform.SetParent(parent, false);
        AgentBody b = g.AddComponent<AgentBody>();
        b.Setup(d, team, bands);
        return b;
    }

    static void LoadClips()
    {
        if (clips != null) return;
        clips = new AnimationClip[3];
        string[] want = { "Idle", "Walk", "Run" };
        foreach (AnimationClip c in Resources.LoadAll<AnimationClip>("Agents/Anims"))
        {
            if (c.name.StartsWith("__")) continue;
            for (int i = 0; i < 3; i++) if (c.name.EndsWith(want[i])) clips[i] = c;
        }
    }

    void Setup(AgentDef d, int team, bool bands)
    {
        def = d;
        GameObject prefab = Resources.Load<GameObject>("Agents/GLB/" + d.file);
        bool useNewModel = prefab != null;
        directBoneLookup = useNewModel;
        if (prefab == null) prefab = Resources.Load<GameObject>("Agents/" + d.file);
        if (prefab == null) { Debug.LogWarning("Agente sem modelo: " + d.file); return; }
        model = Instantiate(prefab, transform);
        model.name = "model";
        model.transform.localPosition = Vector3.zero;
        model.transform.localRotation = Quaternion.identity;
        owner = GetComponentInParent<Actor>();

        // Altura padronizada (hitbox justa: todos entre 1,66 e 1,94 m).
        Renderer[] rs = model.GetComponentsInChildren<Renderer>();
        float top = 0f;
        foreach (Renderer r in rs)
        {
            top = Mathf.Max(top, r.bounds.max.y - transform.position.y);
            SkinnedMeshRenderer sk = r as SkinnedMeshRenderer;
            if (sk != null) sk.updateWhenOffscreen = true;
            if (!useNewModel) Tint(r);
        }
        scale = d.height / Mathf.Max(0.5f, top);
        model.transform.localScale = model.transform.localScale * scale;

        anim = model.GetComponentInChildren<Animator>();
        if (anim == null) anim = model.AddComponent<Animator>();
        anim.applyRootMotion = false;
        anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;

        spine = Bone(HumanBodyBones.Spine, "Spine"); spine1 = Bone(HumanBodyBones.Chest, "Spine1"); spine2 = Bone(HumanBodyBones.UpperChest, "Spine2");
        hips = Bone(HumanBodyBones.Hips, "Hips");
        thighL = Bone(HumanBodyBones.LeftUpperLeg, "LeftUpLeg"); thighR = Bone(HumanBodyBones.RightUpperLeg, "RightUpLeg");
        calfL = Bone(HumanBodyBones.LeftLowerLeg, "LeftLeg"); calfR = Bone(HumanBodyBones.RightLowerLeg, "RightLeg");
        footL = Bone(HumanBodyBones.LeftFoot, "LeftFoot"); footR = Bone(HumanBodyBones.RightFoot, "RightFoot");
        head = Bone(HumanBodyBones.Head, "Head");
        armL = Bone(HumanBodyBones.LeftUpperArm, "LeftArm"); foreL = Bone(HumanBodyBones.LeftLowerArm, "LeftForeArm"); handL = Bone(HumanBodyBones.LeftHand, "LeftHand");
        armR = Bone(HumanBodyBones.RightUpperArm, "RightArm"); foreR = Bone(HumanBodyBones.RightLowerArm, "RightForeArm"); handR = Bone(HumanBodyBones.RightHand, "RightHand");
        midL = Bone(HumanBodyBones.LeftMiddleProximal, "LeftHandMiddle1"); idxL = Bone(HumanBodyBones.LeftIndexProximal, "LeftHandIndex1"); pinL = Bone(HumanBodyBones.LeftLittleProximal, "LeftHandPinky1");
        midR = Bone(HumanBodyBones.RightMiddleProximal, "RightHandMiddle1"); idxR = Bone(HumanBodyBones.RightIndexProximal, "RightHandIndex1"); pinR = Bone(HumanBodyBones.RightLittleProximal, "RightHandPinky1");

        if (useNewModel)
        {
            SetupProceduralRig();
        }
        else
        {
            LoadClips();
            if (clips[0] != null && clips[1] != null && clips[2] != null && anim.avatar != null && anim.avatar.isHuman)
            {
                graph = PlayableGraph.Create("legacy-agent");
                graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
                mixer = AnimationMixerPlayable.Create(graph, 3);
                for (int i = 0; i < 3; i++)
                {
                    plays[i] = AnimationClipPlayable.Create(graph, clips[i]);
                    plays[i].SetTime(Random.value * clips[i].length);
                    graph.Connect(plays[i], 0, mixer, i);
                    mixer.SetInputWeight(i, i == 0 ? 1f : 0f);
                }
                AnimationPlayableOutput o = AnimationPlayableOutput.Create(graph, "out", anim);
                o.SetSourcePlayable(mixer);
                graph.Play();
            }
            else Debug.LogWarning("Agente sem animacao humanoide: " + d.file + " avatar=" + (anim.avatar != null ? anim.avatar.isHuman.ToString() : "null"));
        }

        BuildGun();
        if (bands && armL != null && foreL != null)
        {
            Material glow = Art.Glow(Art.TeamColor(team), 1.6f);
            Band(armL, foreL, glow);
            Band(armR, foreR, glow);
        }
        ok = armL != null && armR != null && handL != null && handR != null && foreL != null && foreR != null;
    }

    // Os GLBs novos compartilham o mesmo rig. Desativamos qualquer clip embutido
    // e animamos os ossos sobre a pose de bind, sem misturar o retarget dos FBXs antigos.
    void SetupProceduralRig()
    {
        foreach (Animation legacy in model.GetComponentsInChildren<Animation>(true)) legacy.enabled = false;
        anim.runtimeAnimatorController = null;
        anim.enabled = false;
        poseBones = new Transform[] { hips, spine, spine1, spine2, head, thighL, thighR, calfL, calfR, footL, footR };
        bindPose = new Quaternion[poseBones.Length];
        for (int i = 0; i < poseBones.Length; i++) if (poseBones[i] != null) bindPose[i] = poseBones[i].localRotation;
        proceduralRig = true;
    }

    static void RotateFromBind(Transform bone, Quaternion localBind, Vector3 axis, float degrees)
    {
        if (bone == null || Mathf.Abs(degrees) < 0.001f) return;
        Quaternion baseWorld = bone.parent != null ? bone.parent.rotation * localBind : localBind;
        bone.rotation = Quaternion.AngleAxis(degrees, axis) * baseWorld;
    }

    void UpdateProceduralPose(float dt)
    {
        for (int i = 0; i < poseBones.Length; i++) if (poseBones[i] != null) poseBones[i].localRotation = bindPose[i];
        float move = Mathf.Clamp01(speed / 4.8f);
        float run = Mathf.InverseLerp(2.7f, 4.8f, speed);
        float crouch = owner != null ? owner.crouchK : 0f;
        float air = owner != null ? Mathf.Clamp01(Mathf.Abs(owner.velocity.y) / 9f) : 0f;
        gaitPhase += dt * Mathf.Lerp(1.2f, 2.7f, run) * Mathf.Lerp(0.2f, 1f, move) * Mathf.PI * 2f;
        float left = Mathf.Sin(gaitPhase), right = -left;
        float leftLift = Mathf.Max(0f, Mathf.Cos(gaitPhase)), rightLift = Mathf.Max(0f, -Mathf.Cos(gaitPhase));
        breath += dt;
        float breathe = Mathf.Sin(breath * 1.35f) * 0.012f;
        float crouchLean = crouch * 4f;
        float recoil = owner != null ? Mathf.Exp(-Mathf.Max(0f, Time.time - owner.lastShotTime) * 12f) : 0f;
        Vector3 localMove = model.transform.InverseTransformDirection(moveDir);
        Vector3 rightAxis = model.transform.right;
        float stride = move * Mathf.Lerp(16f, 25f, run);
        RotateFromBind(thighL, bindPose[5], rightAxis, -left * stride);
        RotateFromBind(thighR, bindPose[6], rightAxis, -right * stride);
        RotateFromBind(calfL, bindPose[7], rightAxis, leftLift * move * 22f + crouch * 8f + air * 5f);
        RotateFromBind(calfR, bindPose[8], rightAxis, rightLift * move * 22f + crouch * 8f + air * 5f);
        RotateFromBind(footL, bindPose[9], rightAxis, -left * move * 10f);
        RotateFromBind(footR, bindPose[10], rightAxis, -right * move * 10f);
        RotateFromBind(spine, bindPose[1], rightAxis, breathe * 45f + move * (1f + run * 1.5f) + crouchLean - recoil * 2.5f);
        RotateFromBind(spine1, bindPose[2], rightAxis, breathe * 30f + move * 1.2f + crouchLean * 0.6f - recoil * 3f);
        RotateFromBind(spine2, bindPose[3], rightAxis, breathe * 22f - localMove.z * move * 1.5f - recoil * 2f);
        RotateFromBind(head, bindPose[4], rightAxis, -breathe * 15f + localMove.z * move * 0.8f);
    }

    Transform Bone(HumanBodyBones b, string fallback)
    {
        Transform t = null;
        if (!directBoneLookup && anim != null && anim.avatar != null && anim.avatar.isHuman) t = anim.GetBoneTransform(b);
        if (t == null)
            foreach (Transform c in model.GetComponentsInChildren<Transform>())
                if (c.name.EndsWith(":" + fallback) || c.name == fallback) { t = c; break; }
        return t;
    }

    // Pele de cada agente (o resto das texturas sai do nome da peca).
    static readonly Dictionary<string, string> Skin = new Dictionary<string, string> {
        {"Jason","young_lightskinned_male_diffuse"},{"Todd","young_darkskinned_male_diffuse"},{"Kate","young_lightskinned_female_diffuse"},
        {"Harold","middleage_lightskinned_male_diffuse"},{"DeathGirl","young_lightskinned_female_diffuse3"},{"Aurora","young_darkskinned_female_diffuse"},
        {"Brutus","middleage_lightskinned_male_diffuse"},{"Sankya","middleage_lightskinned_female_diffuse2"},{"Omnis","young_lightskinned_male_diffuse3"},
        {"Sade","young_darkskinned_female_diffuse"},{"Zero","young_lightskinned_male_diffuse3"},{"Titan","young_lightskinned_male_diffuse"} };

    static readonly string[,] TexByPart = {
        {"helios","SciFi-Armor_Helios_MAT_color",""},{"guardian","SciFi-Armor_Guardian_MAT_color",""},{"jupiter7","SciFi-Armor_Jupiter7_MAT_color",""},
        {"skinsuit","MC-SkinSuit_2022_diffuse","MC-SkinSuit_2022_normal"},{"bodysuit","MC-BodySuit-2021_diffuse","MC-BodySuit-2021_normal"},
        {"male_casualsuit04","male_casualsuit04_diffuse","male_casualsuit04_normal"},{"female_sportsuit01","female_sportsuit01_diffuse","female_sportsuit01_normal"},
        {"gloves_short","Gloves03UV",""},{"gloves_medium","Gloves02UV",""},{"gloves_long","Gloves01UV",""},{"male_boots","boot",""},
        {"galactic","Glactic_Warrior_02",""},{"motorcycle_helmet","motorcyclehelmet",""},{"m1_helmet","M1Helmet_diffuse","M1Helmet_normal"},
        {"mouth_protection","munskydd","munskydd-normal"},{"low-poly","brown_eye",""},{"teeth","teeth",""},{"eyelashes","eyelashes01",""},{"afro","afro_diffuse",""} };

    static Texture2D AgentTex(string n) { return string.IsNullOrEmpty(n) ? null : Resources.Load<Texture2D>("Agents/Textures/" + n); }

    static void SetTex(Material m, string col, string nor)
    {
        Texture2D c = AgentTex(col), nm = AgentTex(nor);
        // Pecas com recorte (gola da malha, viseira): a versao "_a" traz a transparencia original.
        Texture2D cut = AgentTex(col + "_a");
        if (cut != null)
        {
            c = cut;
            m.SetFloat("_AlphaClip", 1f); m.SetFloat("_Cutoff", 0.5f); m.SetFloat("_Cull", 0f);
            m.EnableKeyword("_ALPHATEST_ON");
            m.SetOverrideTag("RenderType", "TransparentCutout");
            m.renderQueue = 2450;
        }
        if (c != null) { if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", c); if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", c); }
        if (nm != null && m.HasProperty("_BumpMap")) { m.SetTexture("_BumpMap", nm); m.EnableKeyword("_NORMALMAP"); }
    }

    void Tint(Renderer r)
    {
        string raw = r.name, n = raw.ToLowerInvariant();
        // Unity acrescenta sufixos em nomes repetidos; fica so o nome da peca.
        foreach (Material m in r.materials)
        {
            if (!m.HasProperty("_BaseColor")) continue;
            m.SetColor("_BaseColor", Color.white);
            if (m.HasProperty("_EmissionColor")) { m.SetColor("_EmissionColor", Color.black); m.DisableKeyword("_EMISSION"); }
            Set(m, 0f, 0.3f);

            if (n == "body") { string sk; if (Skin.TryGetValue(def.file, out sk)) SetTex(m, sk, null); Set(m, 0f, 0.36f); }
            else if (n.StartsWith("eyebrow")) SetTex(m, raw, null);
            else if (n.StartsWith("short") || n.StartsWith("bob") || n.StartsWith("ponytail") || n.StartsWith("braid")) SetTex(m, raw + "_diffuse", null);
            else for (int i = 0; i < TexByPart.GetLength(0); i++)
                if (n.Contains(TexByPart[i, 0])) { SetTex(m, TexByPart[i, 1], TexByPart[i, 2]); break; }

            if (n.Contains("armor")) { m.SetColor("_BaseColor", Color.Lerp(new Color(0.7f, 0.71f, 0.74f), def.accent, 0.3f)); Set(m, 0.55f, 0.55f); }
            else if (n.Contains("helmet")) { m.SetColor("_BaseColor", new Color(0.55f, 0.56f, 0.6f)); Set(m, 0.5f, 0.7f); }
            else if (n.Contains("bodysuit") || n.Contains("skinsuit")) { m.SetColor("_BaseColor", Color.Lerp(Color.white, def.accent, 0.12f)); Set(m, 0f, 0.3f); }
            else if (n.Contains("casualsuit") || n.Contains("sportsuit") || n.Contains("galactic")) { m.SetColor("_BaseColor", def.cloth); Set(m, 0f, 0.2f); }
            else if (n.Contains("glove") || n.Contains("boot")) { m.SetColor("_BaseColor", new Color(0.45f, 0.45f, 0.47f)); Set(m, 0f, 0.35f); }
            else if (n.Contains("low-poly")) Set(m, 0f, 0.85f);
        }
    }

    static void Set(Material m, float metal, float smooth)
    {
        if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metal);
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smooth);
    }

    void Band(Transform a, Transform b, Material glow)
    {
        Vector3 dir = b.position - a.position;
        GameObject g = Art.Box(a, Vector3.zero, Vector3.one, glow, false, "teamBand");
        g.transform.position = a.position + dir * 0.42f;
        g.transform.rotation = Quaternion.FromToRotation(Vector3.up, dir.normalized);
        float w = 0.125f * scale / Mathf.Max(0.0001f, a.lossyScale.x);
        g.transform.localScale = new Vector3(w, 0.035f * scale / Mathf.Max(0.0001f, a.lossyScale.x), w);
    }

    void BuildGun()
    {
        gun = new GameObject("rifle");
        gun.transform.SetParent(transform, false);
        WeaponArt.Weapon(0, gun.transform);
    }

    public Vector3 Muzzle { get { return gun != null ? gun.transform.TransformPoint(WeaponArt.Muzzle[0]) : transform.position + Vector3.up * 1.4f; } }

    public void Die()
    {
        if (dead >= 0f) return;
        dead = 0f;
        if (gun != null) gun.SetActive(false);
    }

    public void Revive()
    {
        dead = -1f;
        yaw = 0f;
        if (gun != null) gun.SetActive(true);
        if (model != null) model.transform.localRotation = Quaternion.identity;
        if (graph.IsValid()) for (int i = 0; i < 3; i++) plays[i].SetSpeed(1.0);
    }

    void Update()
    {
        if (model == null) return;
        float dt = Time.deltaTime;
        if (dead >= 0f)
        {
            // Queda: o corpo tomba para tras e fica no chao ate renascer.
            dead += dt;
            float k = Mathf.Clamp01(dead / 0.55f);
            k = k * k * (3f - 2f * k);
            model.transform.localRotation = Quaternion.Euler(-86f * k, yaw, 0f);
            if (proceduralRig) { speed = 0f; UpdateProceduralPose(dt); }
            if (graph.IsValid()) for (int i = 0; i < 3; i++) plays[i].SetSpeed(0.0);
            return;
        }

        // O corpo vira para onde anda; para tras, anda de costas em vez de girar.
        float want = 0f, dirSign = 1f;
        if (speed > 0.4f && moveDir.sqrMagnitude > 0.001f)
        {
            Vector3 l = transform.InverseTransformDirection(moveDir);
            want = Mathf.Atan2(l.x, l.z) * Mathf.Rad2Deg;
            if (Mathf.Abs(want) > 100f) { want -= Mathf.Sign(want) * 180f; dirSign = -1f; }
            want = Mathf.Clamp(want, -70f, 70f);
        }
        yaw = Mathf.LerpAngle(yaw, want, 1f - Mathf.Exp(-9f * dt));
        model.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);

        if (proceduralRig) { UpdateProceduralPose(dt); return; }
        if (!graph.IsValid()) return;
        float moving = Mathf.Clamp01(speed / 1.0f);
        float run = Mathf.InverseLerp(2.2f, 4.6f, speed);
        float f = 1f - Mathf.Exp(-10f * dt);
        wWalk = Mathf.Lerp(wWalk, moving * (1f - run), f);
        wRun = Mathf.Lerp(wRun, moving * run, f);
        mixer.SetInputWeight(0, Mathf.Clamp01(1f - wWalk - wRun));
        mixer.SetInputWeight(1, wWalk);
        mixer.SetInputWeight(2, wRun);
        plays[1].SetSpeed(dirSign * Mathf.Clamp(speed / (clipSpeed[1] * scale), 0.7f, 1.7f));
        plays[2].SetSpeed(dirSign * Mathf.Clamp(speed / (clipSpeed[2] * scale), 0.8f, 1.6f));
    }

    // Depois da animacao: tronco acompanha a mira, arma no ombro, maos na arma.
    void LateUpdate()
    {
        if (!ok || dead >= 0f) return;
        // Tudo relativo ao "para cima" do proprio agente (no menu ele fica inclinado junto com a camera).
        Vector3 upAxis = transform.up;
        Vector3 fwd = aimDir.sqrMagnitude > 0.001f ? aimDir.normalized : transform.forward;
        Vector3 flat = Vector3.ProjectOnPlane(fwd, upAxis);
        if (flat.sqrMagnitude < 0.0001f) flat = transform.forward;
        flat.Normalize();
        float pitch = Mathf.Clamp(-Mathf.Asin(Mathf.Clamp(Vector3.Dot(fwd, upAxis), -1f, 1f)) * Mathf.Rad2Deg, -55f, 55f);
        Quaternion aimRot = Quaternion.LookRotation(flat, upAxis) * Quaternion.Euler(pitch, 0f, 0f);
        Vector3 right = aimRot * Vector3.right, up = aimRot * Vector3.up;
        fwd = aimRot * Vector3.forward;

        breath += Time.deltaTime;
        float sway = Mathf.Sin(breath * 1.7f) * 0.8f;
        Quaternion twist = Quaternion.AngleAxis(-yaw / 3f, upAxis) * Quaternion.AngleAxis((pitch * 0.6f + sway) / 3f, right);
        if (spine != null) spine.rotation = twist * spine.rotation;
        if (spine1 != null) spine1.rotation = twist * spine1.rotation;
        if (spine2 != null) spine2.rotation = twist * spine2.rotation;
        if (head != null) head.rotation = Quaternion.AngleAxis(pitch * 0.35f, right) * head.rotation;

        float s = scale > 0.01f ? def.height / 1.8f : 1f;
        Vector3 g = armR.position + (right * -0.18f + up * -0.15f + fwd * 0.32f) * s;
        gun.transform.SetPositionAndRotation(g, aimRot);
        gun.transform.localScale = Vector3.one * 1.05f * s;

        Vector3 gripR = g + (up * -0.02f + fwd * -0.04f + right * 0.03f) * s;
        Vector3 gripL = g + (up * 0.02f + fwd * 0.22f + right * -0.08f) * s;
        Solve(armR, foreR, handR, gripR, armR.position + right * 0.5f - up * 0.6f - fwd * 0.2f);
        Solve(armL, foreL, handL, gripL, armL.position - right * 0.35f - up * 0.7f);
        Orient(handR, midR, idxR, pinR, fwd * 0.75f - right * 0.35f - up * 0.2f, up * 0.9f + fwd * 0.3f);
        Orient(handL, midL, idxL, pinL, right * 0.8f + fwd * 0.25f + up * 0.25f, fwd);
    }

    // IK de dois ossos: dobra o cotovelo na direcao de "hint" e leva a mao ate o alvo.
    static void Solve(Transform a, Transform b, Transform c, Vector3 target, Vector3 hint)
    {
        float la = (b.position - a.position).magnitude, lb = (c.position - b.position).magnitude;
        Vector3 to = target - a.position;
        float d = Mathf.Clamp(to.magnitude, 0.02f, la + lb - 0.002f);
        Vector3 dir = to.normalized;
        float cosA = Mathf.Clamp((la * la + d * d - lb * lb) / (2f * la * d), -1f, 1f);
        Vector3 axis = Vector3.Cross(dir, hint - a.position);
        if (axis.sqrMagnitude < 0.00001f) axis = Vector3.up;
        Vector3 upper = Quaternion.AngleAxis(Mathf.Acos(cosA) * Mathf.Rad2Deg, axis.normalized) * dir;
        a.rotation = Quaternion.FromToRotation(b.position - a.position, upper) * a.rotation;
        Vector3 t2 = a.position + dir * d;
        b.rotation = Quaternion.FromToRotation(c.position - b.position, t2 - b.position) * b.rotation;
    }

    // Gira a mao para os dedos apontarem em "fingers" com o indicador para o lado "indexSide".
    static void Orient(Transform hand, Transform mid, Transform idx, Transform pin, Vector3 fingers, Vector3 indexSide)
    {
        if (hand == null || mid == null || idx == null || pin == null) return;
        Vector3 f = mid.position - hand.position, sd = idx.position - pin.position;
        if (f.sqrMagnitude < 0.000001f || sd.sqrMagnitude < 0.000001f) return;
        Quaternion cur = Quaternion.LookRotation(f, Vector3.Cross(f, sd));
        Quaternion des = Quaternion.LookRotation(fingers, Vector3.Cross(fingers, indexSide));
        hand.rotation = des * Quaternion.Inverse(cur) * hand.rotation;
    }

    void OnDestroy()
    {
        if (graph.IsValid()) graph.Destroy();
    }
}
