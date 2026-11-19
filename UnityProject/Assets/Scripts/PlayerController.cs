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
    Vector3 hvel;
    float vy;
    int jumps;
    float dashUntil;
    Vector3 dashDir;
    float nextFire, equipReady, bloom;
    float vmKick, bobPhase, stepDist;
    Transform vmRoot;
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
        SupplyCrate c = SupplyCrate.Near(transform.position, 3.2f);
        if (c != null)
        {
            prompt = "F  -  ABRIR A CAIXA (solta um dinossauro + 2 MECAS do seu time)";
            if (GameInput.UseDown) c.Open(actor);
        }
    }

    void Look()
    {
        Vector2 lk = GameInput.Look * Settings.sensitivity * (cam.fieldOfView / Mathf.Max(1f, Settings.fov));
        if (Settings.invertY) lk.y = -lk.y;
        yaw += lk.x;
        pitch = Mathf.Clamp(pitch - lk.y, -88f, 88f);
        stats.look += Mathf.Abs(lk.x) + Mathf.Abs(lk.y);
        if (TouchPad.On) AimAssist();
        recoilOffset = Mathf.Lerp(recoilOffset, 0f, 1f - Mathf.Exp(-7f * Time.deltaTime));
        // Luneta: a mira balanca com a respiracao; SHIFT prende o folego por alguns segundos.
        float swayX = 0f, swayY = 0f;
        if (ads && cur == 2)
        {
            bool holding = GameInput.Sprint && breath < 4f;
            breath = holding ? breath + Time.deltaTime : Mathf.Max(0f, breath - Time.deltaTime * 0.7f);
            float amp = (holding ? 0.06f : 0.42f) * (cc.isGrounded ? 1f : 2.5f) + Mathf.Clamp01(new Vector3(hvel.x, 0f, hvel.z).magnitude / 4f) * 0.5f;
            swayX = Mathf.Sin(Time.time * 1.15f) * amp + Mathf.Sin(Time.time * 2.7f) * amp * 0.3f;
            swayY = Mathf.Sin(Time.time * 0.83f + 1f) * amp * 0.8f;
        }
        else breath = Mathf.Max(0f, breath - Time.deltaTime);
        transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        cam.transform.localRotation = Quaternion.Euler(Mathf.Clamp(pitch - recoilOffset + swayY, -89f, 89f), swayX, 0f);
    }

    // ---------------------------- Mira assistida (so no celular) ----------------------------
    // Puxa a mira suavemente para o inimigo visivel mais perto do centro; mais forte atirando/mirando.
    // Ao apertar MIRA com um inimigo perto da mira, ela gruda nele.
    bool wasAds;

    Actor AssistTarget(float cone, out Vector3 point)
    {
        point = Vector3.zero;
        if (Game.I == null || actor == null) return null;
        WeaponDef w = Weapons.All[cur];
        float range = Mathf.Min(w.range, cur == 2 ? 250f : 90f);
        Vector3 eye = cam.transform.position, fwd = cam.transform.forward;
        Actor best = null; float bestAng = cone;
        foreach (Actor a in Game.I.actors)
        {
            if (a == null || a == actor || !a.alive || a.team == actor.team) continue;
            // Sniper mira na cabeca so com a luneta; o resto no peito.
            Vector3 p = (cur == 2 && ads && !a.creature) ? a.HeadPos - Vector3.up * 0.05f : a.ChestPos;
            Vector3 to = p - eye;
            float dist = to.magnitude;
            if (dist < 0.5f || dist > range) continue;
            float ang = Vector3.Angle(fwd, to);
            if (ang >= bestAng) continue;
            RaycastHit h;
            if (Physics.Raycast(eye, to / dist, out h, dist, ~0, QueryTriggerInteraction.Ignore) && Combat.ActorOf(h.collider) != a) continue;
            best = a; bestAng = ang; point = p;
        }
        return best;
    }

    void AimAssist()
    {
        bool adsPressed = ads && !wasAds;
        wasAds = ads;
        if (actor == null || !actor.alive) return;
        bool firing = GameInput.Fire;
        float cone = adsPressed ? 25f : (ads || firing ? 16f : 10f);
        Vector3 p;
        if (AssistTarget(cone, out p) == null) return;
        Vector3 to = p - cam.transform.position;
        float tYaw = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
        float tPitch = -Mathf.Asin(Mathf.Clamp(to.normalized.y, -1f, 1f)) * Mathf.Rad2Deg + recoilOffset;
        float dy = Mathf.DeltaAngle(yaw, tYaw), dp = tPitch - pitch;
        if (adsPressed) { yaw += dy; pitch = Mathf.Clamp(pitch + dp, -88f, 88f); return; }
        // graus por segundo; perto do alvo desacelera para nao tremer
        float rate = (ads || firing ? 110f : 45f) * Time.deltaTime;
        float mag = Mathf.Sqrt(dy * dy + dp * dp);
        float step = Mathf.Min(mag, rate * Mathf.Clamp01(mag / 2f + 0.25f));
        if (mag > 0.001f) { yaw += dy / mag * step; pitch = Mathf.Clamp(pitch + dp / mag * step, -88f, 88f); }
    }

    void Move()
    {
        float dt = Time.deltaTime;
        Vector2 mv = GameInput.Move;
        bool grounded = cc.isGrounded;
        Vector3 wish = transform.right * mv.x + transform.forward * mv.y;
        if (wish.sqrMagnitude > 1f) wish.Normalize();

        // Agachar (CTRL ou C no PC, botao AGACHAR no celular). So levanta se tiver espaco em cima.
        bool wantCrouch = GameInput.Crouch;
        if (!wantCrouch && actor.crouching && Physics.SphereCast(transform.position + Vector3.up * 0.9f, 0.3f, Vector3.up, out RaycastHit roofHit, 0.75f, ~0, QueryTriggerInteraction.Ignore)
            && roofHit.collider.GetComponentInParent<Actor>() != actor) wantCrouch = true;
        actor.crouching = wantCrouch;
        float ck = actor.crouchK;
        cam.transform.localPosition = new Vector3(0f, Mathf.Lerp(1.6f, 1.02f, ck), 0f);

        bool aurora = actor.agent == 5;
        bool sprint = GameInput.Sprint && mv.y > 0.1f && !ads && !actor.crouching;
        float speed = ads ? 4f : (sprint ? (aurora ? 11.5f : 9.5f) : (aurora ? 8f : 6.5f));
        if (aurora && Time.time < speedBoostUntil) speed *= 1.3f;
        if (actor.crouching) speed = Mathf.Min(speed, 3.2f);
        hvel = Vector3.Lerp(hvel, wish * speed, 1f - Mathf.Exp(-(grounded ? 12f : 3f) * dt));

        if (grounded)
        {
            if (vy < 0f) vy = -2f;
            jumps = 0;
        }
        if (GameInput.JumpDown)
        {
            if (grounded) { vy = aurora ? 10f : 8f; jumps = 1; Sfx.UI("jump", 0.5f); }
            else if (jumps < 2) { vy = aurora ? 9.5f : 7.5f; jumps = 2; stats.doubleJumps++; Sfx.UI("jump", 0.5f, 1.3f); }
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

        if (GameInput.DashDown && Time.time >= dashReady)
        {
            dashDir = wish.sqrMagnitude > 0.01f ? wish.normalized : transform.forward;
            dashUntil = Time.time + (aurora ? 0.28f : 0.16f);
            dashReady = Time.time + DashCooldown;
            stats.dashes++;
            Sfx.UI("dash", 0.7f);
        }

        Vector3 vel;
        if (Time.time < dashUntil)
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
        if (sprint) stats.sprinted += hs * dt;
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
        float moveFrac = Mathf.Clamp01(new Vector3(hvel.x, 0f, hvel.z).magnitude / 9.5f);
        curSpread = (ads ? w.adsSpread : w.spread) + bloom * (ads ? 0.45f : 1f) + moveFrac * (ads ? 0.3f : 1.1f) + (cc.isGrounded ? 0f : 1.6f);
        if (actor.crouchK > 0.5f && cc.isGrounded) curSpread *= 0.7f;   // agachado: tiro mais preciso

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

        if (GameInput.ReloadDown && mag[cur] < w.magSize && reserve[cur] > 0) { StartReload(w); return; }
        if (Time.time < nextFire || Time.time < equipReady) return;

        bool trigger = w.auto ? GameInput.Fire : GameInput.FireDown;
        if (!trigger) return;

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
        Sfx.UI("reload", 0.7f);
    }

    void Shoot(WeaponDef w)
    {
        bool over = Overcharged;
        nextFire = Time.time + w.interval * (over ? 0.6f : 1f);
        if (!over) mag[cur]--;
        actor.lastShotTime = Time.time;
        stats.shots++;

        Vector3 origin = cam.transform.position;
        Vector3 fwd = cam.transform.forward;
        // Celular: "ima" de bala - se o alvo esta quase na mira (3 graus), o tiro sai na direcao dele.
        if (TouchPad.On)
        {
            Vector3 tp;
            if (AssistTarget(3f, out tp) != null) fwd = (tp - origin).normalized;
        }
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

        float k = ads ? 0.65f : 1f;
        recoilOffset += w.recoilPitch * k;
        yaw += Random.Range(-w.recoilYaw, w.recoilYaw) * k;
        bloom = Mathf.Min(3.5f, bloom + w.bloom);
        vmKick = 1f;
        Fx.Flash(muzzles[cur], w.color);
        if (vms != null && vms[cur] != null) WeaponArt.Casing(cur, vms[cur].transform.TransformPoint(WeaponArt.Eject[cur]), vms[cur].transform.rotation, actor != null ? actor.velocity : Vector3.zero);
        Sfx.UI(w.sound, cur == 2 ? 1f : 0.75f);
    }

    void Abilities()
    {
        if (GameInput.SecondaryDown && AgentAbilities.HasSecondarySkill(actor.agent) && Time.time >= secondaryReady)
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
            WeaponArt.Weapon(i, t);
            Vector3 mz = WeaponArt.Muzzle[i];
            GameObject m = new GameObject("muzzle");
            m.transform.SetParent(t, false);
            m.transform.localPosition = mz;
            muzzles[i] = m.transform;
            vms[i] = g;
        }
    }

    void ShowWeapon()
    {
        if (vms == null) return;
        for (int i = 0; i < vms.Length; i++) vms[i].SetActive(i == cur);
    }

    void UpdateViewmodel(WeaponDef w)
    {
        float dt = Time.deltaTime;
        float baseFov = Settings.fov;
        float sprintFov = (GameInput.Sprint && GameInput.Move.y > 0.1f && !ads) ? baseFov + 6f : baseFov;
        float targetFov = ads ? w.zoomFov * baseFov / 90f : sprintFov;
        cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, targetFov, 1f - Mathf.Exp(-14f * dt));

        float hs = new Vector3(hvel.x, 0f, hvel.z).magnitude;
        bobPhase += hs * dt * 1.6f;
        Vector3 bob = cc.isGrounded ? new Vector3(Mathf.Cos(bobPhase) * 0.008f, Mathf.Abs(Mathf.Sin(bobPhase)) * 0.01f, 0f) * Mathf.Clamp01(hs / 6f) : Vector3.zero;
        if (ads) bob *= 0.2f;

        vmKick = Mathf.Lerp(vmKick, 0f, 1f - Mathf.Exp(-16f * dt));
        Vector3 hip = new Vector3(0.2f, -0.2f, 0.3f);
        // Plasma: arma mais baixa ao mirar (as bobinas luminosas tampavam a visao); mira de ponto na tela.
        Vector3 aim = new Vector3(0f, cur == 0 ? -0.118f : (cur == 1 ? -0.07f : (cur == 4 ? -0.14f : -0.1f)), 0.16f);
        Vector3 target = (ads ? aim : hip) + bob + new Vector3(0f, 0f, -0.07f * vmKick);
        float lower = 0f;
        if (Time.time < equipReady) lower = (equipReady - Time.time) / 0.35f;
        else if (reloading) lower = Mathf.Sin(Mathf.PI * Mathf.Clamp01((Time.time - reloadStart) / Mathf.Max(0.01f, reloadEnd - reloadStart)));
        target += new Vector3(0f, -0.22f * lower, 0f);

        vmRoot.localPosition = Vector3.Lerp(vmRoot.localPosition, target, 1f - Mathf.Exp(-18f * dt));
        vmRoot.localRotation = Quaternion.Euler(-4f * vmKick + 35f * lower, 0f, 0f);

        // Com a luneta da LANCA aberta, esconde a arma.
        bool scoped = ads && cur == 2 && cam.fieldOfView < 40f;
        if (vms[cur].activeSelf == scoped) vms[cur].SetActive(!scoped);
    }
}
