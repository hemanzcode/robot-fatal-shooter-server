using UnityEngine;
using System.Collections.Generic;

// Controle em primeira pessoa: movimento (corrida, pulo duplo, dash), armas, mira, habilidades e viewmodel.
public class PlayerController : MonoBehaviour
{
    public Camera cam;
    public int cur;
    public int[] mag;
    public int[] reserve;
    public bool reloading;
    public float reloadStart, reloadEnd;
    public float dashReady, nadeReady, secondaryReady;
    public float speedBoostUntil;
    public float ult;              // 0..100
    public float overUntil;        // sobrecarga ativa ate este instante
    public bool ads;
    public float curSpread;
    // Modo de jogo: TATICO (serio) ou DIVERSAO (o jogo classico). Escolhido em CONTROLES; online por enquanto e sempre DIVERSAO.
    public static int ForceMode = -1;   // cenas de teste: 1 tatico, 0 diversao
    public static bool TacticalMode { get { return ForceMode >= 0 ? ForceMode == 1 : (Settings.tactical && !NetGame.Online); } }
    public const float WalkSpeed = 4.8f;
    public const float SprintSpeed = 6.3f;
    public const float AdsSpeed = 3.2f;      // 4.0f - 20%
    public const float CrouchSpeed = 2.56f;  // 3.2f - 20%
    public const float ProneSpeed = 1.45f;
    public const float AccelTime = 0.15f;
    public const float DecelTime = 0.12f;
    public bool isSprinting;
    public float leanK { get; private set; }
    public float peekK { get; private set; }
    public float wallReadyK { get; private set; }
    public bool isVaulting { get; private set; }
    public float sprintFireReady;
    public float lastSprintToFireDelay;
    public Vector3 CurrentHorizontalVel { get { return hvel; } }
    public bool paused { get { return Game.I != null && Game.I.menus != null && Game.I.menus.IsOpen; } }

    // Contadores usados pelo tutorial.
    public struct Stats
    {
        public float look, moved, sprinted, adsTime;
        public int doubleJumps, dashes, shots, reloads, nades, ults, pads, switchMask, adsKills;
    }
    public Stats stats;

    // Veiculos, jetpack e interacao (tecla F).
    public float vehYaw, vehPitch;
    public float jetFuel;
    public string prompt = "";
    float nextJetFx, breath;

    public const float DashCooldown = 4f;
    public const float NadeCooldown = 10f;
    public const float SecondaryCooldown = 12f;

    Actor actor;
    public Actor actorForAbilities { get { return actor; } }
    CharacterController cc;
    float yaw, pitch, recoilOffset;
    public void SetYaw(float y) { yaw = y; }   // teleporte de teste no editor
    Vector3 hvel;
    float vy;
    int jumps;
    float dashUntil;
    Vector3 dashDir;
    float nextFire, equipReady, bloom;
    float vmKick, bobPhase, stepDist;
    Transform vmRoot;
    ViewHands hands;
    float cantK = 1f;
    float sprintK;
    float sprintCancelTime = -10f;
    float sprintBlockUntil;
    bool crouchToggle;
    bool proneToggle;
    float vaultT, vaultDur = VaultDuration, vaultCooldownUntil;
    Vector3 vaultStart, vaultPeak, vaultEnd;
    static readonly RaycastHit[] probeHits = new RaycastHit[16];
    const float LeanProbeDist = 1.0f;
    const float LeanMaxOffset = 0.40f;
    const float LeanMaxRoll = 11f;
    const float PeekMaxOffset = 0.36f;
    const float VaultMaxHeight = 1.15f;
    const float VaultDuration = 0.56f;
    const float VaultCooldown = 0.45f;
    static readonly float[] WeaponWeights = { 1.00f, 1.08f, 1.24f, 1.32f, 1.12f };
    float WeaponWeight { get { return (cur >= 0 && cur < WeaponWeights.Length) ? WeaponWeights[cur] : 1f; } }
    bool wasGrounded = true, isClimbing;
    float prevVy, landDip, strafeTilt, recoilYawOffset, lastLandTime = -10f;
    int jumpFatigueCount;
    Vector2 swayLag;
    float lastShotTime = -10f;
    int reloadShells;
    GameObject[] vms;
    Transform[] muzzles;

    public bool Overcharged { get { return Time.time < overUntil; } }

    void Start()
    {
        actor = GetComponent<Actor>();
        cc = GetComponent<CharacterController>();

        GameObject c = new GameObject("PlayerCamera");
        c.transform.SetParent(transform, false);
        c.transform.localPosition = new Vector3(0f, 1.6f, 0f);
        cam = c.AddComponent<Camera>();
        cam.nearClipPlane = 0.03f;
        cam.farClipPlane = 500f;
        cam.fieldOfView = Settings.fov;
        Game.SetupCamera(cam);
        c.AddComponent<AudioListener>();
        c.tag = "MainCamera";

        int n = Weapons.All.Length;
        mag = new int[n];
        reserve = new int[n];
        BuildViewmodels();
        ResetLoadout();
        yaw = transform.eulerAngles.y;
        actor.onRespawn += OnRespawn;
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    void ResetLoadout()
    {
        for (int i = 0; i < Weapons.All.Length; i++)
        {
            mag[i] = Weapons.All[i].magSize;
            reserve[i] = Weapons.All[i].reserve;
        }
        cur = 0;
        reloading = false;
        ShowWeapon();
    }

    void OnRespawn()
    {
        ResetLoadout();
        yaw = transform.eulerAngles.y;
        pitch = 0f;
        recoilOffset = 0f;
        hvel = Vector3.zero;
        vy = 0f;
        bloom = 0f;
        isSprinting = false;
        sprintFireReady = 0f;
        sprintBlockUntil = 0f;
        crouchToggle = false;
        proneToggle = false;
        leanK = 0f;
        peekK = 0f;
        wallReadyK = 0f;
        isVaulting = false;
        wasGrounded = true;
        prevVy = 0f;
        landDip = 0f;
        strafeTilt = 0f;
        recoilYawOffset = 0f;
        lastLandTime = -10f;
        jumpFatigueCount = 0;
        swayLag = Vector2.zero;
        if (cc != null && !cc.enabled) cc.enabled = true;
        sprintK = 0f;
        jetFuel = 0f;
        vehYaw = 0f; vehPitch = 0f;
    }

    public bool RefillAmmo()
    {
        bool any = false;
        for (int i = 0; i < Weapons.All.Length; i++)
        {
            if (reserve[i] < Weapons.All[i].reserve) { reserve[i] = Weapons.All[i].reserve; any = true; }
        }
        return any;
    }

    public void AddUlt(float amount) { ult = Mathf.Min(100f, ult + amount); }

    void Update()
    {
        if (Game.I == null) return;
        if (GameInput.PauseDown && !Game.I.matchOver)
        {
#if UNITY_EDITOR
            // Diagnostico do "pausa sozinho": registra o que estava apertado quando a pausa abriu.
            try { System.IO.File.AppendAllText("Logs/runtime.txt", "PAUSA t=" + Time.time.ToString("0.0") + " veiculo=" + (actor.vehicle != null ? actor.vehicle.label : "-") + " espaco=" + GameInput.JumpHeld + " ctrl=" + GameInput.Down + " shift=" + GameInput.Sprint + " f=" + GameInput.Use + " foco=" + Application.isFocused + "\n"); } catch (System.Exception) { }
#endif
            Game.I.menus.TogglePause();
        }
        if (paused) return;
        if (Race.Active)
        {
            // Correndo na montanha: a corrida controla a camera e o carro.
            if (vmRoot != null) vmRoot.gameObject.SetActive(false);
            ads = false; reloading = false;
            return;
        }
        if (!actor.alive || Game.I.matchOver)
        {
            if (vmRoot != null) vmRoot.gameObject.SetActive(false);
            ads = false;
            if (!actor.alive && Game.I.roundMode && !Game.I.matchOver) Spectate(); else EndSpectate();
            return;
        }
        EndSpectate();
        if (actor.vehicle != null) { InVehicle(); return; }
        if (Time.time < actor.stunnedUntil) { ads = false; return; }
        if (vmRoot != null && !vmRoot.gameObject.activeSelf) vmRoot.gameObject.SetActive(true);
        Interact();
        if (actor.vehicle != null) return;

        WeaponDef w = Weapons.All[cur];
        ads = GameInput.Aim && !reloading && Time.time >= equipReady;

        Look();
        Move();
        UpdateWallReady(Time.deltaTime);
        HandleWeapons(w);
        Abilities();
        UpdateViewmodel(w);
    }

    // ---------------------------- Espectador (modo bomba, depois de morrer) ----------------------------
    // Comeca assistindo um companheiro vivo; CLIQUE (ou TIRO no celular) passa para o proximo; depois do ultimo,
    // ou com o botao direito / MIRA, vira camera livre para andar pela fase (WASD, ESPACO sobe, CTRL desce).
    public bool spectating;
    public Actor specTarget;
    float specStart, specYaw, specPitch;
    bool freeCam;
    readonly List<Renderer> hiddenR = new List<Renderer>();

    void Spectate()
    {
        if (!spectating)
        {
            spectating = true;
            specStart = Time.time;
            specTarget = null;
            freeCam = false;
            specYaw = yaw; specPitch = pitch;
            cam.transform.SetParent(null, true);
        }
        if (Time.time - specStart < 2f) return;   // 2 s vendo onde morreu
        if (Game.I.menus.IsOpen) return;

        if (!freeCam && (specTarget == null || !specTarget.alive))
        {
            Actor next = NextTarget(specTarget);
            if (next != null) Watch(next); else { Watch(null); freeCam = true; }
        }
        if (GameInput.FireDown)
        {
            Actor next = NextTarget(freeCam ? null : specTarget);
            if (next != null && (freeCam || next != specTarget)) { freeCam = false; Watch(next); }
            else { freeCam = true; Watch(null); }
        }
        if (GameInput.Aim && !freeCam && specTarget != null) { freeCam = true; specYaw = cam.transform.eulerAngles.y; specPitch = Mathf.DeltaAngle(0f, cam.transform.eulerAngles.x); Watch(null); }

        if (freeCam)
        {
            Vector2 lk = GameInput.Look * Settings.sensitivity * (cam.fieldOfView / Mathf.Max(1f, Settings.fov));
            if (Settings.invertY) lk.y = -lk.y;
            specYaw += lk.x;
            specPitch = Mathf.Clamp(specPitch - lk.y, -89f, 89f);
            Quaternion rot = Quaternion.Euler(specPitch, specYaw, 0f);
            Vector2 mv = GameInput.Move;
            float sp = GameInput.Sprint ? 22f : 11f;
            Vector3 v = rot * new Vector3(mv.x, 0f, mv.y) * sp;
            v += Vector3.up * ((GameInput.JumpHeld ? 1f : 0f) - (GameInput.Crouch ? 1f : 0f)) * sp * 0.7f;
            Vector3 pos = cam.transform.position + v * Time.deltaTime;
            float half = Arena.W * Arena.Cell / 2f + 6f;
            pos.x = Mathf.Clamp(pos.x, -half, half); pos.z = Mathf.Clamp(pos.z, -half, half);
            pos.y = Mathf.Clamp(pos.y, Arena.SewerFloor + 1f, 45f);
            cam.transform.SetPositionAndRotation(pos, rot);
        }
        else if (specTarget != null)
        {
            // Olho do jogador assistido, virando para onde ele mira.
            Vector3 eye = specTarget.transform.position + Vector3.up * (1.6f * specTarget.CrouchScale);
            Vector3 fwd = specTarget.transform.forward;
            NetPuppet np = specTarget.GetComponent<NetPuppet>();
            if (np != null && np.human) fwd = Quaternion.Euler(np.pitch, np.yaw, 0f) * Vector3.forward;
            else if (specTarget.body != null && specTarget.body.aimDir.sqrMagnitude > 0.01f) fwd = specTarget.body.aimDir.normalized;
            Quaternion want = Quaternion.LookRotation(fwd);
            cam.transform.position = Vector3.Lerp(cam.transform.position, eye, 1f - Mathf.Exp(-20f * Time.deltaTime));
            cam.transform.rotation = Quaternion.Slerp(cam.transform.rotation, want, 1f - Mathf.Exp(-12f * Time.deltaTime));
        }
    }

    // Proximo companheiro vivo depois do atual (na ordem da lista); sem companheiros, qualquer jogador vivo.
    Actor NextTarget(Actor after)
    {
        var list = Game.I.actors;
        for (int pass = 0; pass < 2; pass++)
        {
            int start = after != null ? list.IndexOf(after) : -1;
            for (int i = 1; i <= list.Count; i++)
            {
                Actor a = list[(start + i + list.Count) % list.Count];
                if (a == null || a == actor || !a.alive || a.creature) continue;
                if (pass == 0 && a.team != actor.team) continue;
                if (a == after) return null;   // deu a volta: so ele
                return a;
            }
        }
        return null;
    }

    void Watch(Actor a)
    {
        for (int i = 0; i < hiddenR.Count; i++) if (hiddenR[i] != null) hiddenR[i].enabled = true;
        hiddenR.Clear();
        specTarget = a;
        if (a == null) return;
        // Esconde o corpo de quem estamos assistindo (a camera fica dentro da cabeca dele).
        if (a.model != null)
            foreach (Renderer r in a.model.GetComponentsInChildren<Renderer>())
                if (r.enabled) { r.enabled = false; hiddenR.Add(r); }
        cam.transform.position = a.transform.position + Vector3.up * 1.6f;
        cam.transform.rotation = a.transform.rotation;
    }

    // Fim da corrida da montanha: camera de volta na cabeca, campo de visao normal.
    public void ReturnFromRace()
    {
        spectating = false;
        cam.transform.SetParent(transform, false);
        cam.transform.localPosition = new Vector3(0f, 1.6f, 0f);
        cam.transform.localRotation = Quaternion.Euler(pitch, 0f, 0f);
        cam.fieldOfView = Settings.fov;
        cam.farClipPlane = 500f;
        yaw = transform.eulerAngles.y;
        if (vmRoot != null) vmRoot.gameObject.SetActive(true);
    }

    void EndSpectate()
    {
        if (!spectating) return;
        spectating = false;
        Watch(null);
        freeCam = false;
        cam.transform.SetParent(transform, false);
        cam.transform.localPosition = new Vector3(0f, Mathf.Lerp(1.6f, 1.02f, actor.crouchK), 0f);
        cam.transform.localRotation = Quaternion.Euler(pitch, 0f, 0f);
    }

    // Pilotando: o veiculo cuida do movimento; aqui so o olhar livre e a saida.
    void InVehicle()
    {
        if (vmRoot != null && vmRoot.gameObject.activeSelf) vmRoot.gameObject.SetActive(false);
        ads = false; reloading = false;
        Vehicle v = actor.vehicle;
        Vector2 lk = GameInput.Look * Settings.sensitivity;
        if (Settings.invertY) lk.y = -lk.y;
        if (!(v is Helicopter)) vehYaw = Mathf.Clamp(vehYaw + lk.x, -v.lookLimit, v.lookLimit);
        vehPitch = Mathf.Clamp(vehPitch - lk.y, -70f, 70f);
        cam.transform.localRotation = Quaternion.Euler(vehPitch, 0f, 0f);
        cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, Settings.fov, 1f - Mathf.Exp(-10f * Time.deltaTime));
        prompt = "";
        if (GameInput.UseDown) v.Exit();
    }

    public void LeftVehicle(float newYaw)
    {
        yaw = newYaw; pitch = 0f; vehYaw = 0f; vehPitch = 0f;
        hvel = Vector3.zero; vy = 0f;
        if (cam != null) cam.transform.localRotation = Quaternion.identity;
    }

    // Tecla F: entra em veiculo ou abre a caixa laranja que estiver perto.
    void Interact()
    {
        prompt = "";
        Vehicle best = null; float bd = 999f;
        for (int i = 0; i < Vehicle.All.Count; i++)
        {
            Vehicle v = Vehicle.All[i];
            if (v == null || !v.CanUse(actor)) continue;
            float d = (v.transform.position - transform.position).sqrMagnitude;
            if (d < bd) { bd = d; best = v; }
        }
        if (best != null)
        {
            prompt = "F  -  PILOTAR " + best.label;
            if (GameInput.UseDown) { vehYaw = 0f; vehPitch = 0f; best.Enter(actor); }
            return;
        }
        Gate gt = Gate.Near(transform.position, 3f);
        if (gt != null)
        {
            prompt = "F  -  " + (gt.open ? "FECHAR" : "ABRIR") + " O PORTAO";
            if (GameInput.UseDown) gt.Request();
            return;
        }
        SupplyCrate c = SupplyCrate.Near(transform.position, 3.2f);
        if (c != null)
        {
            prompt = "F  -  ABRIR A CAIXA (solta um dinossauro + 2 MECAS do seu time)";
            if (GameInput.UseDown) c.Open(actor);
        }
    }

    void Look()
    {
        // Sensibilidade: horizontal e vertical separadas; mirando (ADS) usa a sensibilidade de ADS x multiplicador.
        // A lente (zoom) ainda compensa pelo campo de visao, para a mira nao "voar" na luneta.
        Vector2 raw = GameInput.Look;
        float sx = ads ? Aim.sensAds * Aim.adsMult : Settings.sensitivity;
        float sy = ads ? sx * (Aim.sensY / Mathf.Max(0.05f, Settings.sensitivity)) : Aim.sensY;
        float zoom = cam.fieldOfView / Mathf.Max(1f, Settings.fov);
        Vector2 lk = new Vector2(raw.x * sx, raw.y * sy) * zoom;
        if (Settings.invertY) lk.y = -lk.y;
        lk = AimAssist(lk);
        yaw += lk.x;
        pitch = Mathf.Clamp(pitch - lk.y, -88f, 88f);
        stats.look += Mathf.Abs(lk.x) + Mathf.Abs(lk.y);
        float swayScale = ads ? 0.35f : 1f;
        Vector2 targetSway = new Vector2(Mathf.Clamp(-lk.x * 0.008f, -0.025f, 0.025f), Mathf.Clamp(-lk.y * 0.008f, -0.025f, 0.025f)) * swayScale;
        swayLag = Vector2.Lerp(swayLag, targetSway, 1f - Mathf.Exp(-14f * Time.deltaTime));
        WeaponDef rw = Weapons.All[cur];
        bool firingActive = GameInput.Fire && Time.time - lastShotTime <= rw.interval * 1.15f;
        if (!firingActive)
        {
            float recDecay = 1f - Mathf.Exp(-8f * Time.deltaTime);
            recoilOffset = Mathf.Lerp(recoilOffset, 0f, recDecay);
            recoilYawOffset = Mathf.Lerp(recoilYawOffset, 0f, recDecay);
        }
        // Luneta: a mira balanca com a respiracao; SHIFT prende o folego por alguns segundos.
        float swayX = 0f, swayY = 0f;
        if (ads && cur == 2)
        {
            bool holding = GameInput.Sprint && breath < 4f;
            breath = holding ? breath + Time.deltaTime : Mathf.Max(0f, breath - Time.deltaTime * 0.7f);
            float amp = (holding ? 0.06f : 0.42f) * (cc.isGrounded ? 1f : 2.5f) * (actor.proneK > 0.5f ? 0.35f : 1f) + Mathf.Clamp01(new Vector3(hvel.x, 0f, hvel.z).magnitude / 4f) * 0.5f;
            swayX = Mathf.Sin(Time.time * 1.15f) * amp + Mathf.Sin(Time.time * 2.7f) * amp * 0.3f;
            swayY = Mathf.Sin(Time.time * 0.83f + 1f) * amp * 0.8f;
        }
        else breath = Mathf.Max(0f, breath - Time.deltaTime);
        transform.rotation = Quaternion.Euler(0f, yaw + recoilYawOffset, 0f);
        Vector3 localVel = transform.InverseTransformDirection(hvel);
        float wantTilt = Mathf.Clamp(-localVel.x / SprintSpeed, -1f, 1f) * 1.6f * (ads ? 0.45f : 1f);
        strafeTilt = Mathf.Lerp(strafeTilt, wantTilt, 1f - Mathf.Exp(-10f * Time.deltaTime));
        cam.transform.localRotation = Quaternion.Euler(Mathf.Clamp(pitch - recoilOffset + swayY, -89f, 89f), swayX, -leanK * LeanMaxRoll + strafeTilt);
    }

    // ---------------------------- Assistencia de mira ----------------------------
    // Ajuda sutil, sem mirar sozinha: (1) DESACELERA a camera quando a mira passa sobre um inimigo e
    // (2) ACOMPANHA de leve o alvo que ja esta na mira. Nunca puxa a mira de longe, nunca trava, nunca atira,
    // nao muda a sensibilidade salva e so vale para inimigo visivel (nao atravessa parede).
    // Os valores vem do menu (Aim); 35% de intensidade = os valores do menu como estao.
    Actor aaTarget;
    float aaYaw, aaPitch;

    // Inimigo visivel e o quanto ele esta fora da mira (graus); r = "tamanho" do alvo na tela (graus).
    bool AssistCheck(Actor a, out float off, out float r, out float tYaw, out float tPitch)
    {
        off = r = tYaw = tPitch = 0f;
        if (a == null || a == actor || !a.alive || a.team == actor.team) return false;
        WeaponDef w = Weapons.All[cur];
        float range = Mathf.Min(w.range, cur == 2 ? 250f : 90f);
        Vector3 eye = cam.transform.position;
        Vector3 p = (cur == 2 && ads && !a.creature) ? a.HeadPos - Vector3.up * 0.05f : a.ChestPos;
        Vector3 to = p - eye;
        float dist = to.magnitude;
        if (dist < 0.5f || dist > range) return false;
        RaycastHit h;
        if (Physics.Raycast(eye, to / dist, out h, dist, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) && Combat.ActorOf(h.collider) != a)
        {
            if (a.creature) return false;
            // Fallback para cabeca quando o peito esta atras de cobertura baixa.
            p = a.HeadPos - Vector3.up * 0.04f;
            to = p - eye;
            dist = to.magnitude;
            if (dist < 0.5f || dist > range) return false;
            if (Physics.Raycast(eye, to / dist, out h, dist, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) && Combat.ActorOf(h.collider) != a) return false;
        }
        off = Vector3.Angle(cam.transform.forward, to);
        r = Mathf.Clamp(Mathf.Atan2(a.creature ? 2f : 0.9f, dist) * Mathf.Rad2Deg + 1f, 1.5f, 9f);
        tYaw = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
        tPitch = -Mathf.Asin(Mathf.Clamp(to.y / dist, -1f, 1f)) * Mathf.Rad2Deg;
        return true;
    }

    Vector2 AimAssist(Vector2 lk)
    {
        int prof = Aim.Profile;
        if (!Aim.aaOn[prof] || actor == null || !actor.alive || Game.I == null) { aaTarget = null; return lk; }
        float k = (ads ? Aim.aaAds[prof] : Aim.aaStrength[prof]) / 0.35f;
        if (k <= 0.001f) { aaTarget = null; return lk; }

        // Alvo: continua no mesmo enquanto ele estiver perto da mira (evita trocar de alvo sem querer).
        Actor best = null; float bOff = 0f, bR = 0f, bYaw = 0f, bPitch = 0f;
        float off, r, ty, tp;
        if (aaTarget != null && AssistCheck(aaTarget, out off, out r, out ty, out tp) && off < r * 1.5f)
        { best = aaTarget; bOff = off; bR = r; bYaw = ty; bPitch = tp; }
        else
        {
            float score = 1f;
            foreach (Actor a in Game.I.actors)
            {
                if (!AssistCheck(a, out off, out r, out ty, out tp) || off > r) continue;
                float sc = off / r;
                if (sc < score) { score = sc; best = a; bOff = off; bR = r; bYaw = ty; bPitch = tp; }
            }
        }
        if (best == null) { aaTarget = null; return lk; }
        float close = Mathf.Clamp01(1f - bOff / (bR * 1.5f));   // 1 no centro do alvo, 0 na borda

        // (1) Desaceleracao: a camera fica mais lenta sobre o alvo (no maximo 75% mais lenta). Nunca para.
        float slow = Mathf.Clamp01(Aim.aaSlow[prof] * k) * 0.75f * close;
        lk *= 1f - slow;

        // (2) Acompanhamento: segue uma fracao do movimento do alvo na tela, com limite por segundo.
        if (best == aaTarget)
        {
            float dy = Mathf.DeltaAngle(aaYaw, bYaw), dp = bPitch - aaPitch;
            float f = Mathf.Clamp01(Aim.aaFollow[prof] * k) * close;
            float lim = 30f * Time.deltaTime;
            lk.x += Mathf.Clamp(dy * f, -lim, lim);
            lk.y -= Mathf.Clamp(dp * f, -lim, lim);
        }
        aaTarget = best; aaYaw = bYaw; aaPitch = bPitch;
        return lk;
    }

    bool WorldSphereProbe(Vector3 origin, float radius, Vector3 dir, float dist, out RaycastHit bestHit)
    {
        bestHit = default(RaycastHit);
        int n = Physics.SphereCastNonAlloc(origin, radius, dir, probeHits, dist, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        float best = float.MaxValue;
        bool found = false;
        for (int i = 0; i < n; i++)
        {
            Collider col = probeHits[i].collider;
            if (col == null || col.gameObject.layer == 2 || col.GetComponentInParent<Actor>() != null) continue;
            if (probeHits[i].distance < best)
            {
                best = probeHits[i].distance;
                bestHit = probeHits[i];
                found = true;
            }
        }
        return found;
    }

    bool WorldRayProbe(Vector3 origin, Vector3 dir, float dist, out RaycastHit bestHit)
    {
        bestHit = default(RaycastHit);
        int n = Physics.RaycastNonAlloc(origin, dir, probeHits, dist, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        float best = float.MaxValue;
        bool found = false;
        for (int i = 0; i < n; i++)
        {
            Collider col = probeHits[i].collider;
            if (col == null || col.gameObject.layer == 2 || col.GetComponentInParent<Actor>() != null) continue;
            if (probeHits[i].distance < best)
            {
                best = probeHits[i].distance;
                bestHit = probeHits[i];
                found = true;
            }
        }
        return found;
    }

    void UpdateAutoLean(float dt, Vector2 mv)
    {
        float targetLean = 0f;
        float targetPeek = 0f;
        if (ads && !actor.prone && !isVaulting && cc.isGrounded)
        {
            Vector3 chest = transform.position + Vector3.up * (1.25f * actor.CrouchScale);
            bool centerBlocked = WorldSphereProbe(chest, 0.14f, transform.forward, LeanProbeDist, out _);
            bool leftBlocked = WorldSphereProbe(chest - transform.right * 0.48f, 0.12f, transform.forward, 1.35f, out _);
            bool rightBlocked = WorldSphereProbe(chest + transform.right * 0.48f, 0.12f, transform.forward, 1.35f, out _);
            if (centerBlocked)
            {
                if (!leftBlocked && rightBlocked) targetLean = -1f;
                else if (!rightBlocked && leftBlocked) targetLean = 1f;
                else if (!leftBlocked && !rightBlocked) targetLean = mv.x < -0.1f ? -1f : 1f;
            }

            // Auto-Lean vertical em cobertura baixa (agachado atras de mureta: ergue para mirar por cima)
            if (actor.crouching)
            {
                bool lowCoverBlocked = WorldSphereProbe(transform.position + Vector3.up * 1.05f, 0.14f, transform.forward, 1.15f, out _);
                bool highBlocked = WorldSphereProbe(transform.position + Vector3.up * 1.45f, 0.14f, transform.forward, 1.25f, out _);
                if (lowCoverBlocked && !highBlocked)
                {
                    targetPeek = 1f;
                    targetLean = 0f;
                }
            }
        }
        float k = 1f - Mathf.Exp(-12f * dt);
        leanK = Mathf.Lerp(leanK, targetLean, k);
        peekK = Mathf.Lerp(peekK, targetPeek, k);
        if (Mathf.Abs(leanK) < 0.002f && targetLean == 0f) leanK = 0f;
        if (peekK < 0.002f && targetPeek == 0f) peekK = 0f;
    }

    void UpdateWallReady(float dt)
    {
        float targetWall = 0f;
        // Sonda na altura da camera e na altura real do cano (-0.08m) para evitar cover falso (head-glitch).
        Vector3 barrelOrigin = cam.transform.position - cam.transform.up * 0.08f;
        if (!isVaulting && (WorldSphereProbe(cam.transform.position, 0.09f, cam.transform.forward, 0.82f, out RaycastHit wallHit)
            || WorldSphereProbe(barrelOrigin, 0.09f, cam.transform.forward, 0.82f, out wallHit)))
            targetWall = Mathf.Clamp01((0.82f - wallHit.distance) / 0.45f);
        wallReadyK = Mathf.Lerp(wallReadyK, targetWall, 1f - Mathf.Exp(-18f * dt));
        if (wallReadyK < 0.002f && targetWall == 0f) wallReadyK = 0f;
    }

    bool TryStartVault(Vector2 mv)
    {
        bool runningForward = isSprinting || (GameInput.Sprint && mv.y > 0.25f && !ads);
        if (!runningForward || !cc.isGrounded || actor.prone || isVaulting || Time.time < vaultCooldownUntil) return false;
        if (!WorldSphereProbe(transform.position + Vector3.up * 0.50f, 0.20f, transform.forward, 0.90f, out RaycastHit lowHit)) return false;
        // Sonda alta em y + 1.38f (raio 0.18f -> base em 1.20m): livre sobre muretas de 1.15m, bloqueada em 1.50m+.
        if (WorldSphereProbe(transform.position + Vector3.up * 1.38f, 0.18f, transform.forward, 1.05f, out _)) return false;

        Vector3 vaultDir = Vector3.ProjectOnPlane(-lowHit.normal, Vector3.up);
        vaultDir = vaultDir.sqrMagnitude > 0.01f ? vaultDir.normalized : transform.forward;
        Vector3 topOrigin = new Vector3(lowHit.point.x, transform.position.y + 1.32f, lowHit.point.z) + vaultDir * 0.25f;
        if (!WorldRayProbe(topOrigin, Vector3.down, 1.35f, out RaycastHit topHit)) return false;
        float height = topHit.point.y - transform.position.y;
        if (height < 0.35f || height > VaultMaxHeight + 0.03f) return false;

        Vector3 landOrigin = new Vector3(lowHit.point.x, topHit.point.y + 0.35f, lowHit.point.z) + vaultDir * 1.15f;
        float landY = WorldRayProbe(landOrigin, Vector3.down, 2.2f, out RaycastHit landHit) ? landHit.point.y : transform.position.y;
        Vector3 topClear = new Vector3(lowHit.point.x, topHit.point.y + 0.05f, lowHit.point.z) + vaultDir * 0.35f;
        Vector3 endClear = new Vector3(lowHit.point.x, landY + 0.05f, lowHit.point.z) + vaultDir * 1.15f;
        if (Physics.CheckCapsule(topClear + Vector3.up * 0.38f, topClear + Vector3.up * 1.45f, 0.28f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) return false;
        if (Physics.CheckCapsule(endClear + Vector3.up * 0.38f, endClear + Vector3.up * 1.45f, 0.28f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) return false;

        isVaulting = true;
        isClimbing = false;
        vaultT = 0f;
        vaultDur = VaultDuration;
        vaultStart = transform.position;
        vaultPeak = topClear + Vector3.up * 0.05f;
        vaultEnd = endClear;
        cc.enabled = false;
        vy = 0f;
        crouchToggle = false;
        proneToggle = false;
        actor.crouching = false;
        actor.prone = false;
        Sfx.UI("step", 0.45f, 0.85f);
        return true;
    }

    bool TryStartLedgeClimb()
    {
        if (actor.prone || isVaulting || Time.time < vaultCooldownUntil || GameInput.Move.y <= 0.1f) return false;
        RaycastHit wallHit = default(RaycastHit);
        bool hitWall = WorldSphereProbe(transform.position + Vector3.up * 1.45f, 0.22f, transform.forward, 0.95f, out wallHit)
                    || WorldSphereProbe(transform.position + Vector3.up * 0.95f, 0.22f, transform.forward, 0.95f, out wallHit);
        if (!hitWall) return false;

        Vector3 climbDir = Vector3.ProjectOnPlane(-wallHit.normal, Vector3.up);
        climbDir = climbDir.sqrMagnitude > 0.01f ? climbDir.normalized : transform.forward;
        if (Vector3.Dot(transform.forward, climbDir) < 0.65f) return false;
        Vector3 topOrigin = new Vector3(wallHit.point.x, transform.position.y + 1.95f, wallHit.point.z) + climbDir * 0.35f;
        if (!WorldRayProbe(topOrigin, Vector3.down, 1.4f, out RaycastHit topHit)) return false;
        float height = topHit.point.y - transform.position.y;
        if (height < 1.18f || height > 1.85f) return false;

        Vector3 endClear = new Vector3(wallHit.point.x, topHit.point.y + 0.05f, wallHit.point.z) + climbDir * 0.55f;
        if (Physics.CheckCapsule(endClear + Vector3.up * 0.38f, endClear + Vector3.up * 1.55f, 0.28f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) return false;

        isVaulting = true;
        isClimbing = true;
        vaultT = 0f;
        vaultDur = 0.95f;
        vaultStart = transform.position;
        vaultPeak = new Vector3(transform.position.x, topHit.point.y + 0.15f, transform.position.z);
        vaultEnd = endClear;
        cc.enabled = false;
        vy = 0f;
        crouchToggle = false;
        proneToggle = false;
        actor.crouching = false;
        actor.prone = false;
        Sfx.UI("step", 0.55f, 0.72f);
        return true;
    }

    void StepVault(float dt)
    {
        vaultT += dt / Mathf.Max(0.1f, vaultDur);
        float t = Mathf.Clamp01(vaultT);
        float smooth = t * t * (3f - 2f * t);
        Vector3 p01 = Vector3.Lerp(vaultStart, vaultPeak, smooth);
        Vector3 p12 = Vector3.Lerp(vaultPeak, vaultEnd, smooth);
        transform.position = Vector3.Lerp(p01, p12, smooth);
        float arc = Mathf.Sin(Mathf.PI * t);
        float dipY = isClimbing ? -0.06f * arc : -0.04f * arc;
        cam.transform.localPosition = new Vector3(0f, 1.6f * actor.CrouchScale + dipY, 0f);
        float animPitch = isClimbing ? (11f * arc) : (7f * arc);
        float animRoll = isClimbing ? (Mathf.Sin(Mathf.PI * 2f * t) * 3.5f) : (-5f * arc);
        cam.transform.localRotation = Quaternion.Euler(Mathf.Clamp(pitch + animPitch, -89f, 89f), 0f, animRoll);
        if (t >= 1f)
        {
            isVaulting = false;
            cc.enabled = true;
            vy = -2f;
            hvel = transform.forward * (isClimbing ? WalkSpeed * 0.35f : WalkSpeed * 0.6f);
            if (isClimbing) { landDip = 0.08f; Sfx.UI("step", 0.45f, 0.8f); }
            isClimbing = false;
            vaultCooldownUntil = Time.time + VaultCooldown;
        }
    }

    void Move()
    {
        float dt = Time.deltaTime;
        if (isVaulting) { StepVault(dt); return; }
        Vector2 mv = GameInput.Move;
        bool grounded = cc.isGrounded;
        Vector3 wish = transform.right * mv.x + transform.forward * mv.y;
        if (wish.sqrMagnitude > 1f) wish.Normalize();

        // Postura unificada: Agachar (CTRL / C / toque curto) e Deitado - Prone (B / toque longo).
        if (GameInput.ProneToggleDown)
        {
            proneToggle = !proneToggle;
            if (proneToggle) crouchToggle = false;
        }
        if (TacticalMode && GameInput.CrouchToggleDown)
        {
            if (proneToggle || TouchPad.Prone) { proneToggle = false; TouchPad.Prone = false; crouchToggle = true; }
            else crouchToggle = !crouchToggle;
        }
        if (GameInput.JumpDown || (GameInput.Sprint && mv.y > 0.1f && !ads))
        {
            crouchToggle = false;
            proneToggle = false;
            TouchPad.Prone = false;
        }
        bool wantProne = GameInput.Prone || proneToggle;
        bool wantCrouch = !wantProne && (GameInput.Crouch || crouchToggle);
        if (!wantProne && actor.prone && Physics.SphereCast(transform.position + Vector3.up * 0.45f, 0.28f, Vector3.up, out RaycastHit proneRoof, 0.65f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
            && proneRoof.collider.GetComponentInParent<Actor>() != actor) wantProne = true;
        if (!wantCrouch && !wantProne && actor.crouching && Physics.SphereCast(transform.position + Vector3.up * 0.9f, 0.3f, Vector3.up, out RaycastHit roofHit, 0.75f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
            && roofHit.collider.GetComponentInParent<Actor>() != actor) wantCrouch = true;
        actor.prone = wantProne;
        actor.crouching = wantCrouch;

        UpdateAutoLean(dt, mv);
        float eyeY = 1.6f * actor.CrouchScale;
        float wantLat = leanK * LeanMaxOffset;
        if (Mathf.Abs(wantLat) > 0.005f)
        {
            Vector3 eyeWorld = transform.position + Vector3.up * eyeY;
            Vector3 latDir = transform.right * Mathf.Sign(wantLat);
            if (WorldSphereProbe(eyeWorld, 0.16f, latDir, Mathf.Abs(wantLat) + 0.08f, out RaycastHit camWall))
                wantLat = Mathf.Sign(wantLat) * Mathf.Max(0f, camWall.distance - 0.05f);
        }
        float wantPeek = peekK * PeekMaxOffset;
        if (wantPeek > 0.005f)
        {
            Vector3 eyeWorld = transform.position + Vector3.up * eyeY;
            if (WorldSphereProbe(eyeWorld, 0.16f, Vector3.up, wantPeek + 0.08f, out RaycastHit camRoof))
                wantPeek = Mathf.Max(0f, camRoof.distance - 0.05f);
        }
        if (!wasGrounded && grounded && prevVy < -4.2f)
        {
            float impact = Mathf.Clamp01((-prevVy - 4.2f) / 10f);
            landDip = Mathf.Max(landDip, impact * 0.14f * WeaponWeight);
            hvel *= 1f - impact * 0.35f;
            lastLandTime = Time.time;
            Sfx.UI("step", 0.35f + impact * 0.25f, 0.72f);
        }
        wasGrounded = grounded;
        landDip = Mathf.Lerp(landDip, 0f, 1f - Mathf.Exp(-14f * dt));

        float hsCam = new Vector3(hvel.x, 0f, hvel.z).magnitude;
        float bobScale = (grounded && hsCam > 0.6f) ? Mathf.Clamp01(hsCam / SprintSpeed) * (ads ? 0.25f : 1f) * (actor.prone ? 0.35f : 1f) : 0f;
        float camBobX = Mathf.Cos(bobPhase) * 0.006f * bobScale;
        float camBobY = (Mathf.Abs(Mathf.Sin(bobPhase)) - 0.5f) * 0.016f * bobScale;
        cam.transform.localPosition = new Vector3(wantLat + camBobX, eyeY + wantPeek - Mathf.Abs(leanK) * 0.05f - landDip + camBobY, 0f);

        if (TryStartVault(mv)) return;

        bool aurora = !TacticalMode && actor.agent == 5;
        WeaponDef w = Weapons.All[cur];
        float speed;
        if (!TacticalMode)
        {
            // DIVERSAO: movimento classico (mais rapido, sem atraso para atirar depois de correr).
            isSprinting = GameInput.Sprint && mv.y > 0.1f && !ads && !actor.crouching && !actor.prone;
            sprintFireReady = 0f; sprintBlockUntil = 0f;
            speed = ads ? 4f : (isSprinting ? (aurora ? 11.5f : 9.5f) : (aurora ? 8f : 6.5f));
            if (aurora && Time.time < speedBoostUntil) speed *= 1.3f;
            if (actor.prone) speed = Mathf.Min(speed, ProneSpeed);
            else if (actor.crouching) speed = Mathf.Min(speed, 3.2f);
            hvel = Vector3.Lerp(hvel, wish * speed, 1f - Mathf.Exp(-(grounded ? 12f : 3f) * dt));
        }
        else
        {
        bool firingHeld = w.auto ? GameInput.Fire : GameInput.FireDown;
        bool wantSprint = GameInput.Sprint && mv.y > 0.1f && !ads && !actor.crouching && !actor.prone && grounded;
        if (wantSprint && !firingHeld && Time.time >= sprintBlockUntil)
        {
            isSprinting = true;
            sprintFireReady = Time.time + w.sprintToFire;
        }
        else
        {
            if (isSprinting && firingHeld)
            {
                sprintCancelTime = Time.time;
                sprintBlockUntil = sprintFireReady;
            }
            isSprinting = false;
        }

        // Velocidade base reduzida em 20% no modo tatico + multiplicadores direcionais continuos (1.0 frente, 0.8 strafe, 0.7 recuo)
        speed = ads ? AdsSpeed : (isSprinting ? (aurora ? 9.2f : SprintSpeed) : (aurora ? 6.4f : WalkSpeed));
        if (aurora && Time.time < speedBoostUntil) speed *= 1.3f;
        if (actor.prone) speed = Mathf.Min(speed, ProneSpeed);
        else if (actor.crouching) speed = Mathf.Min(speed, CrouchSpeed);

        float dirMult = 1f;
        float magSq = mv.x * mv.x + mv.y * mv.y;
        if (magSq > 0.0001f)
        {
            float invMag = 1f / Mathf.Sqrt(magSq);
            float nx = mv.x * invMag;
            float ny = mv.y * invMag;
            float yMult = ny >= 0f ? 1.0f : 0.7f;
            dirMult = Mathf.Sqrt(nx * nx * 0.64f + ny * ny * yMult * yMult); // 0.8^2 = 0.64
        }
        speed *= dirMult * actor.MoveSpeedMult;

        // Inercia tatica (estilo Tarkov/Wardogs): massa por arma + frenagem de contra-strafe ao inverter direcao.
        Vector3 targetVel = wish * speed;
        float airMult = grounded ? 1f : 0.35f;
        float wMass = WeaponWeight;
        float dot = Vector3.Dot(hvel, targetVel);
        if (wish.sqrMagnitude < 0.0001f)
        {
            float decelRef = Mathf.Max(hvel.magnitude, WalkSpeed);
            float rate = (decelRef / (DecelTime * wMass)) * airMult;
            hvel = Vector3.Lerp(hvel, Vector3.zero, 1f - Mathf.Exp(-12f * airMult * dt / wMass));
            hvel = Vector3.MoveTowards(hvel, Vector3.zero, rate * 0.55f * dt);
        }
        else if (dot < -0.05f && hvel.magnitude > 1.8f)
        {
            float counterRate = (Mathf.Max(hvel.magnitude, WalkSpeed) / (DecelTime * 1.45f * wMass)) * airMult;
            hvel = Vector3.MoveTowards(hvel, Vector3.zero, counterRate * dt);
        }
        else
        {
            float accelRef = Mathf.Max(speed, WalkSpeed);
            float rate = (accelRef / (AccelTime * wMass)) * airMult;
            hvel = Vector3.Lerp(hvel, targetVel, 1f - Mathf.Exp(-11f * airMult * dt / wMass));
            hvel = Vector3.MoveTowards(hvel, targetVel, rate * 0.55f * dt);
        }
        }
        prevVy = vy;

        if (grounded)
        {
            if (vy < 0f) vy = -2f;
            jumps = 0;
            if (Time.time - lastLandTime > 0.85f) jumpFatigueCount = 0;
        }
        if (GameInput.JumpDown)
        {
            if (grounded)
            {
                float fatigue = jumpFatigueCount == 0 ? 1f : (jumpFatigueCount == 1 ? 0.78f : 0.60f);
                vy = (aurora ? 10f : 8f) * fatigue;
                if (jumpFatigueCount > 0) hvel *= 0.82f;
                jumpFatigueCount = Mathf.Min(2, jumpFatigueCount + 1);
                lastLandTime = Time.time;
                jumps = 1;
                Sfx.UI("jump", 0.5f);
            }
            else if (TryStartLedgeClimb()) { return; }
            else if (!TacticalMode && jumps < 2) { vy = aurora ? 9.5f : 7.5f; jumps = 2; stats.doubleJumps++; Sfx.UI("jump", 0.5f, 1.3f); }
        }
        if (actor.pendingLaunch > 0f)
        {
            vy = actor.pendingLaunch;
            actor.pendingLaunch = 0f;
            jumps = 1;
            stats.pads++;
        }
        vy -= 22f * dt;

        // Jetpack: segurando ESPACO fora do chao, empurra para cima enquanto houver combustivel.
        if (jetFuel > 0f && GameInput.JumpHeld && !grounded && vy < 7.5f)
        {
            vy = Mathf.Min(9f, vy + 52f * dt);
            jetFuel = Mathf.Max(0f, jetFuel - 13f * dt);
            if (Time.time >= nextJetFx)
            {
                nextJetFx = Time.time + 0.09f;
                Fx.Spark(transform.position + Vector3.up * 0.8f - transform.forward * 0.3f, Vector3.down, Art.Orange);
                Sfx.UI("dash", 0.12f, 0.6f);
            }
        }

        if (!TacticalMode && GameInput.DashDown && Time.time >= dashReady)
        {
            dashDir = wish.sqrMagnitude > 0.01f ? wish.normalized : transform.forward;
            dashUntil = Time.time + (aurora ? 0.28f : 0.16f);
            dashReady = Time.time + DashCooldown;
            stats.dashes++;
            Sfx.UI("dash", 0.7f);
        }

        Vector3 vel;
        if (!TacticalMode && Time.time < dashUntil)
        {
            vy = 0f;
            vel = dashDir * (aurora ? 31f : 24f) + actor.externalVel;
            hvel = dashDir * speed;
        }
        else vel = hvel + Vector3.up * vy + actor.externalVel;

        CollisionFlags flags = cc.Move(vel * dt);
        if ((flags & CollisionFlags.Above) != 0 && vy > 0f) vy = 0f;

        float hs = new Vector3(hvel.x, 0f, hvel.z).magnitude;
        stats.moved += hs * dt;
        if (isSprinting) stats.sprinted += hs * dt;
        if (ads) stats.adsTime += dt;
        if (grounded && hs > 1f)
        {
            stepDist += hs * dt;
            if (stepDist > 2.6f) { stepDist = 0f; Sfx.UI("step", 0.25f); }
        }
    }

    void HandleWeapons(WeaponDef w)
    {
        // Troca de arma
        int slot = GameInput.WeaponSlot;
        float sc = GameInput.Scroll;
        if (slot < 0 && Mathf.Abs(sc) > 0.01f)
            slot = (cur + (sc > 0f ? -1 : 1) + Weapons.All.Length) % Weapons.All.Length;
        if (slot >= 0 && slot != cur)
        {
            cur = slot;
            stats.switchMask |= 1 << slot;
            reloading = false;
            equipReady = Time.time + 0.35f;
            bloom = 0f;
            ShowWeapon();
            Sfx.UI("empty", 0.5f, 0.7f);
            w = Weapons.All[cur];
        }

        bloom = Mathf.Max(0f, bloom - 5f * Time.deltaTime);
        if (TacticalMode)
        {
            float hsNow = new Vector3(hvel.x, 0f, hvel.z).magnitude;
            float moveFrac = Mathf.Clamp01(hsNow / SprintSpeed);
            curSpread = (ads ? w.adsSpread : w.spread) + bloom * (ads ? 0.4f : 1f) + moveFrac * (ads ? 0.45f : 2.2f) + (isSprinting ? 2.5f : 0f) + (cc.isGrounded ? 0f : 3.2f);
            // Sniper andando em pe perde precisao: exige parar, agachar ou deitar para disparo preciso.
            if (cur == 2 && !actor.crouching && !actor.prone && hsNow > 0.45f) curSpread += moveFrac * (ads ? 3.8f : 5.5f);
            float proneTrans = Mathf.Abs((actor.prone ? 1f : 0f) - actor.proneK);
            if (proneTrans > 0.08f) curSpread += 2.2f * proneTrans;   // penalidade momentanea durante drop-shot
            if (actor.proneK > 0.5f && cc.isGrounded) curSpread *= 0.40f;   // deitado: maxima estabilidade/precisao
            else if (actor.crouchK > 0.5f && cc.isGrounded) curSpread *= 0.65f;   // agachado: tiro mais preciso
        }
        else
        {
            // DIVERSAO: dispersao classica.
            float moveFrac = Mathf.Clamp01(new Vector3(hvel.x, 0f, hvel.z).magnitude / 9.5f);
            curSpread = (ads ? w.adsSpread : w.spread) + bloom * (ads ? 0.45f : 1f) + moveFrac * (ads ? 0.3f : 1.1f) + (cc.isGrounded ? 0f : 1.6f);
            if (actor.proneK > 0.5f && cc.isGrounded) curSpread *= 0.40f;
            else if (actor.crouchK > 0.5f && cc.isGrounded) curSpread *= 0.7f;
        }

        if (reloading)
        {
            if (Time.time >= reloadEnd)
            {
                int need = w.magSize - mag[cur];
                int take = Mathf.Min(need, reserve[cur]);
                mag[cur] += take;
                reserve[cur] -= take;
                reloading = false;
            }
            return;
        }

        if (wallReadyK > 0.55f) return;   // arma levantada contra parede: bloqueia disparo/recarga na parede

        if (GameInput.ReloadDown && mag[cur] < w.magSize && reserve[cur] > 0) { StartReload(w); return; }
        if (Time.time < nextFire || Time.time < equipReady || Time.time < sprintFireReady) return;

        bool trigger = w.auto ? GameInput.Fire : GameInput.FireDown;
        if (!trigger) return;
        lastSprintToFireDelay = sprintCancelTime > 0f ? Mathf.Max(0f, Time.time - sprintCancelTime) : 0f;
        sprintCancelTime = -10f;

        if (mag[cur] > 0 || Overcharged) Shoot(w);
        else if (reserve[cur] > 0) StartReload(w);
        else if (GameInput.FireDown) Sfx.UI("empty", 0.7f);
    }

    void StartReload(WeaponDef w)
    {
        reloading = true;
        reloadStart = Time.time;
        reloadEnd = Time.time + w.reloadTime;
        stats.reloads++;
        reloadShells = Mathf.Min(w.magSize - mag[cur], reserve[cur]);
    }

    void Shoot(WeaponDef w)
    {
        bool over = Overcharged;
        lastShotTime = Time.time;
        nextFire = Time.time + w.interval * (over ? 0.6f : 1f);
        if (!over) mag[cur]--;
        actor.lastShotTime = Time.time;
        stats.shots++;

        Vector3 origin = cam.transform.position;
        Vector3 fwd = cam.transform.forward;
        Vector3 muzzle = muzzles[cur] != null ? muzzles[cur].position : origin + fwd * 0.6f;
        bool anyHit = false, anyHead = false, anyKill = false;

        if (w.projectile)
        {
            Combat.Launch(actor, origin + fwd * 0.7f, Combat.SpreadDir(fwd, curSpread), w.projSpeed, 0f, w.damage, w.projRadius, w.name, w.color, 1f);
        }
        else if (cur == 2)
        {
            // Fuzil de precisao: bala com tempo de voo e queda (mire um pouco acima em tiros longos).
            Combat.FireBullet(actor, origin + fwd * 0.3f, Combat.SpreadDir(fwd, curSpread), w, 1f);
        }
        else
        {
            // Varios projeteis (Pulso): soma o que acertou cada alvo e mostra um numero so por alvo.
            if (w.pellets > 1) Combat.BeginBatch();
            for (int i = 0; i < w.pellets; i++)
            {
                bool head, killed;
                if (Combat.Hitscan(actor, origin, Combat.SpreadDir(fwd, curSpread), w, muzzle, 1f, out head, out killed))
                {
                    anyHit = true;
                    anyHead |= head;
                    anyKill |= killed;
                }
            }
            if (w.pellets > 1) { bool bh; anyKill |= Combat.EndBatch(actor, w.name, out bh); anyHead |= bh; }
        }
        if (anyHit) Game.I.Hitmarker(anyHead, anyKill);

        float proneTrans = Mathf.Abs((actor.prone ? 1f : 0f) - actor.proneK);
        float k = (ads ? 0.65f : 1f) * (actor.proneK > 0.5f ? 0.65f : 1f) * (1f + 0.35f * proneTrans);
        recoilOffset += w.recoilPitch * k;
        recoilYawOffset = Mathf.Clamp(recoilYawOffset + Random.Range(-w.recoilYaw, w.recoilYaw) * k, -6f, 6f);
        bloom = Mathf.Min(3.5f, bloom + w.bloom);
        vmKick = 1f;
        Fx.Flash(muzzles[cur], w.color);
        if (vms != null && vms[cur] != null) WeaponArt.Casing(cur, vms[cur].transform.TransformPoint(WeaponArt.Eject[cur]), vms[cur].transform.rotation, actor != null ? actor.velocity : Vector3.zero);
        Sfx.UI(w.sound, w.sound == "rail" ? 0.55f : 1f);
    }

    void Abilities()
    {
        if (!TacticalMode && GameInput.SecondaryDown && AgentAbilities.HasSecondarySkill(actor.agent) && Time.time >= secondaryReady)
        {
            secondaryReady = Time.time + SecondaryCooldown;
            AgentAbilities.UseSecondarySkill(this);
        }
        if (GameInput.GrenadeDown && Time.time >= nadeReady)
        {
            nadeReady = Time.time + NadeCooldown;
            if (AgentAbilities.HasCustomSkill(actor.agent)) AgentAbilities.UseSkill(this);
            else
            {
                stats.nades++;
                Vector3 fwd = cam.transform.forward;
                Combat.Launch(actor, cam.transform.position + fwd * 0.7f, fwd + Vector3.up * 0.12f, 24f, 16f, 85f, 6f, "GRANADA", Art.Green, 1f);
                Sfx.UI("jump", 0.6f, 0.7f);
            }
        }
        if (GameInput.UltDown && ult >= 100f)
        {
            if (AgentAbilities.HasCustomSkill(actor.agent)) AgentAbilities.UseUltimate(this);
            else
            {
                ult = 0f;
                overUntil = Time.time + 8f;
                stats.ults++;
                actor.health = Mathf.Min(actor.maxHealth, actor.health + 80f);
                Sfx.UI("ult", 0.9f);
            }
        }
    }

    // ---------- Viewmodels (armas em primeira pessoa, feitas de primitivas) ----------

    void BuildViewmodels()
    {
        GameObject r = new GameObject("Viewmodel");
        r.transform.SetParent(cam.transform, false);
        vmRoot = r.transform;
        int n = Weapons.All.Length;
        vms = new GameObject[n];
        muzzles = new Transform[n];
        Material dark = Art.Mat(new Color(0.03f, 0.03f, 0.035f), Color.black, 0f, default(Color), 0.75f, 0.6f);
        Material steel = Art.Mat(new Color(0.13f, 0.14f, 0.16f), Color.black, 0f, default(Color), 0.8f, 0.9f);

        for (int i = 0; i < n; i++)
        {
            GameObject g = new GameObject("vm_" + Weapons.All[i].name);
            g.transform.SetParent(vmRoot, false);
            Transform t = g.transform;
            WeaponArt.Weapon(i, t, Skins.Get(Skins.Equipped(i)));   // skin equipada na loja
            Vector3 mz = WeaponArt.Muzzle[i];
            GameObject m = new GameObject("muzzle");
            m.transform.SetParent(t, false);
            m.transform.localPosition = mz;
            muzzles[i] = m.transform;
            vms[i] = g;
        }
        hands = new ViewHands(vmRoot, vms, actor != null ? actor.team : 0);
    }

    void ShowWeapon()
    {
        if (vms == null) return;
        for (int i = 0; i < vms.Length; i++) vms[i].SetActive(i == cur);
    }

    void UpdateViewmodel(WeaponDef w)
    {
#if UNITY_EDITOR
        // So no editor: F9 deixa o jogo em camera lenta, para conferir a animacao de recarga.
        if (Input.GetKeyDown(KeyCode.F9))
            Time.timeScale = Time.timeScale < 0.5f ? 1f : 0.12f;
#endif
        float dt = Time.deltaTime;
        bool effAds = ads && wallReadyK < 0.55f;
        float baseFov = Settings.fov;
        float sprintFov = (isSprinting && !effAds) ? baseFov + (TacticalMode ? 5f : 6f) : baseFov;
        float targetFov = effAds ? w.zoomFov * baseFov / 90f : sprintFov;
        cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, targetFov, 1f - Mathf.Exp(-14f * dt));

        float hs = new Vector3(hvel.x, 0f, hvel.z).magnitude;
        bobPhase += hs * dt * 1.6f;
        Vector3 bob = cc.isGrounded ? new Vector3(Mathf.Cos(bobPhase) * 0.008f, Mathf.Abs(Mathf.Sin(bobPhase)) * 0.01f, 0f) * Mathf.Clamp01(hs / 6f) : Vector3.zero;
        if (effAds) bob *= 0.2f;

        vmKick = Mathf.Lerp(vmKick, 0f, 1f - Mathf.Exp(-16f * dt));
        sprintK = Mathf.Lerp(sprintK, (isSprinting && !effAds) ? 1f : 0f, 1f - Mathf.Exp(-14f * dt));
        Vector3 hip = new Vector3(0.28f, -0.2f, 0.15f);   // perto da camera, como em CS/Valorant
        // Plasma: arma mais baixa ao mirar (as bobinas luminosas tampavam a visao); mira de ponto na tela.
        Vector3 aim = new Vector3(0f, cur == 0 ? -0.118f : (cur == 1 ? -0.07f : (cur == 4 ? -0.14f : -0.1f)), 0.16f);
        Vector3 target = (effAds ? aim : hip) + bob + new Vector3(swayLag.x, swayLag.y - landDip * 0.55f, -0.07f * vmKick) + WeaponArt.SprintPosOffset * sprintK + WeaponArt.HighReadyPosOffset * wallReadyK;
        if (isVaulting) target += isClimbing ? new Vector3(0f, -0.36f, -0.15f) : new Vector3(0.05f, -0.22f, -0.10f);
        float lower = 0f;
        if (Time.time < equipReady) lower = (equipReady - Time.time) / 0.35f;
        target += new Vector3(0f, -0.22f * lower, 0f);

        // Com a luneta da LANCA aberta, esconde a arma (e as maos).
        bool scoped = effAds && cur == 2 && cam.fieldOfView < 40f;
        if (vms[cur].activeSelf == scoped) vms[cur].SetActive(!scoped);

        // Maos + animacao de recarga (tirar/por carregador, ferrolho, cartuchos...).
        float dur = Mathf.Max(0.01f, reloadEnd - reloadStart);
        float rt = reloading ? Mathf.Clamp01((Time.time - reloadStart) / dur) : -1f;
        Vector3 dPos = Vector3.zero, dRot = Vector3.zero;
        if (hands != null) hands.Update(cur, !scoped, rt, dur, Time.time - lastShotTime, reloadShells, mag[cur] <= 0, out dPos, out dRot);
        if (effAds && rt < 0f) dRot *= 0.3f;

        vmRoot.localPosition = Vector3.Lerp(vmRoot.localPosition, target + dPos, 1f - Mathf.Exp(-18f * dt));
        cantK = Mathf.Lerp(cantK, effAds ? 0f : 1f, 1f - Mathf.Exp(-14f * dt));   // no quadril a arma fica levemente virada
        float cant = cantK;
        Vector3 spRot = WeaponArt.SprintRotOffset * sprintK + WeaponArt.HighReadyRotOffset * wallReadyK;
        if (isVaulting) spRot += isClimbing ? new Vector3(48f, -15f, 0f) : new Vector3(28f, -18f, 12f);
        vmRoot.localRotation = Quaternion.Euler(-4f * vmKick + 35f * lower + dRot.x + spRot.x - swayLag.y * 110f, dRot.y - 6f * cant + spRot.y + swayLag.x * 110f, dRot.z - 4f * cant + spRot.z + strafeTilt * 1.4f);
    }
}
