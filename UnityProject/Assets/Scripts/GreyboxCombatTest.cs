using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Controlador da cena Greybox_CombatTest: spawna jogador (FOV 62), alvos estaticos/moveis (100 HP)
/// em 12m, 25m e 35m, atirador controlado para Bullet Flyby / Tagging / Audio 3D e exibe telemetria em tempo real.
/// </summary>
public class GreyboxCombatTest : MonoBehaviour
{
    class TargetEntry
    {
        public Actor actor;
        public Vector3 basePos;
        public bool mobile;
        public float phase;
        public float firstHitTime = -1f;
    }

    readonly List<TargetEntry> targets = new List<TargetEntry>();
    readonly List<Actor> clones = new List<Actor>();
    readonly Dictionary<Actor, Transform[]> hitboxVis = new Dictionary<Actor, Transform[]>();
    bool showHitboxes;
    Actor shooterActor;
    float nextShooterFire;
    bool shooterDirectHitMode;
    float lastMeasuredTTK = -1f;
    static GUIStyle boxStyle;

    void Start()
    {
        if (Game.I == null) return;
        PlayerController.TacticalMode = true;
        Settings.fov = 62f; // FOV entre 60 e 65 graus

        Actor player = SpawnActor("VOCE", 0, true, new Vector3(0f, 0f, 0f), 0f, 0);
        Game.I.player = player;
        Game.I.pc = player.GetComponent<PlayerController>();
        if (Game.I.pc != null && Game.I.pc.cam != null) Game.I.pc.cam.fieldOfView = 62f;

        // Alvos estaticos e moveis com 100 HP em 12m, 25m e 35m
        float[] dists = { 12f, 25f, 35f };
        for (int i = 0; i < dists.Length; i++)
        {
            float z = dists[i];
            SpawnTarget("ALVO_" + (int)z + "M_EST", new Vector3(-2.4f, 0f, z), false, i);
            SpawnTarget("ALVO_" + (int)z + "M_MOV", new Vector3(2.4f, 0f, z), true, i);
        }

        // Atirador de teste controlado (dispara proximo ao jogador para validar Bullet Flyby 2.0m, 3D Audio e Tagging)
        shooterActor = SpawnActor("ATIRADOR_TESTE", 1, false, new Vector3(6.5f, 0f, 18f), 200f, 1);
        shooterActor.maxHealth = shooterActor.health = 100f;
        nextShooterFire = Time.time + 1.5f;

        // Garante muro de 1.50m (bloqueio de Auto-Vault) e drone hostil de teste para Ping contextual
        if (GameObject.Find("HighWall_1_50m") == null)
        {
            GameObject hw = GameObject.CreatePrimitive(PrimitiveType.Cube);
            hw.name = "HighWall_1_50m";
            hw.transform.position = new Vector3(3.8f, 0.75f, 7.5f);
            hw.transform.localScale = new Vector3(3.2f, 1.50f, 0.45f);
        }
        if (GameObject.Find("DroneHostil_Teste") == null)
        {
            GameObject dr = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            dr.name = "DroneHostil_Teste";
            dr.transform.position = new Vector3(-4.5f, 3.2f, 16f);
            dr.transform.localScale = new Vector3(0.7f, 0.35f, 0.7f);
            dr.GetComponent<Renderer>().sharedMaterial = Art.Glow(new Color(1f, 0.3f, 0.8f), 1.6f);
            HostileDroneMarker m = dr.AddComponent<HostileDroneMarker>();
            m.team = 1;
        }
        BuildParkourCourse();
    }

    void BuildParkourCourse()
    {
        if (GameObject.Find("Greybox_Stairs") != null) return;
        GameObject root = new GameObject("Greybox_Stairs");
        Material stepMat = Art.Mat(new Color(0.32f, 0.35f, 0.40f), Color.black, 0f, default(Color), 0.25f, 0f);
        Material climbMat = Art.Mat(new Color(0.85f, 0.48f, 0.12f), Color.black, 0f, default(Color), 0.3f, 0f);

        // Escada progressiva (8 degraus de 0.30m ate plataforma de 2.40m)
        for (int i = 0; i < 8; i++)
        {
            float h = (i + 1) * 0.30f;
            GameObject st = GameObject.CreatePrimitive(PrimitiveType.Cube);
            st.name = "Stair_" + (i + 1);
            st.transform.SetParent(root.transform, false);
            st.transform.position = new Vector3(-9.5f, h * 0.5f, 4f + i * 0.65f);
            st.transform.localScale = new Vector3(2.2f, h, 0.65f);
            st.GetComponent<Renderer>().sharedMaterial = stepMat;
        }
        GameObject plat = GameObject.CreatePrimitive(PrimitiveType.Cube);
        plat.name = "Stair_TopPlatform";
        plat.transform.SetParent(root.transform, false);
        plat.transform.position = new Vector3(-9.5f, 1.20f, 10.6f);
        plat.transform.localScale = new Vector3(3.2f, 2.40f, 3.0f);
        plat.GetComponent<Renderer>().sharedMaterial = stepMat;

        // Pontos para pulos e escalada de paredes altas (1.50m, 2.10m, 2.30m)
        Vector3[] boxPos = { new Vector3(-13.2f, 0.75f, 6f), new Vector3(-13.2f, 1.05f, 9.2f), new Vector3(-13.2f, 1.15f, 12.6f) };
        Vector3[] boxScale = { new Vector3(2.2f, 1.50f, 2.0f), new Vector3(2.2f, 2.10f, 2.0f), new Vector3(2.6f, 2.30f, 1.2f) };
        string[] boxNames = { "JumpBox_1_50m", "JumpBox_2_10m", "ClimbWall_2_30m" };
        for (int i = 0; i < boxNames.Length; i++)
        {
            GameObject b = GameObject.CreatePrimitive(PrimitiveType.Cube);
            b.name = boxNames[i];
            b.transform.SetParent(root.transform, false);
            b.transform.position = boxPos[i];
            b.transform.localScale = boxScale[i];
            b.GetComponent<Renderer>().sharedMaterial = climbMat;
        }
    }

    Actor SpawnActor(string label, int team, bool isPlayer, Vector3 pos, float yaw, int agentIdx)
    {
        GameObject g = new GameObject(isPlayer ? "Player" : label);
        CharacterController cc = g.AddComponent<CharacterController>();
        cc.height = 1.8f;
        cc.radius = 0.35f;
        cc.center = new Vector3(0f, 0.9f, 0f);
        cc.stepOffset = 0.4f;
        Actor a = g.AddComponent<Actor>();
        a.Init(label, team, isPlayer, agentIdx);
        a.maxHealth = a.health = 100f;
        if (isPlayer) g.AddComponent<PlayerController>();
        Game.I.actors.Add(a);
        a.Respawn(pos, yaw);
        return a;
    }

    void SpawnTarget(string label, Vector3 pos, bool mobile, int idx)
    {
        Actor a = SpawnActor(label, 1, false, pos, 180f, (idx + 2) % AgentBody.Defs.Length);
        TargetEntry entry = new TargetEntry { actor = a, basePos = pos, mobile = mobile, phase = idx * 1.1f };
        a.onDamaged += _ =>
        {
            if (entry.firstHitTime < 0f || a.health >= a.maxHealth - 1f) entry.firstHitTime = Time.time;
            if (a.health <= 0f && entry.firstHitTime > 0f)
            {
                lastMeasuredTTK = Time.time - entry.firstHitTime;
                entry.firstHitTime = -1f;
            }
        };
        a.onRespawn += () => { entry.firstHitTime = -1f; };
        targets.Add(entry);
    }

    void Update()
    {
        if (Game.I == null || Game.I.player == null) return;

        if (Input.GetKeyDown(KeyCode.T)) shooterDirectHitMode = !shooterDirectHitMode;
        if (Input.GetKeyDown(KeyCode.H)) { showHitboxes = !showHitboxes; UpdateHitboxVisuals(); }
        if (Input.GetKeyDown(KeyCode.K)) ClonePlayerAtCurrentPose();
        if (Input.GetKeyDown(KeyCode.L)) ClearClones();
        if (showHitboxes) UpdateHitboxVisuals();

        // Movimenta alvos moveis (strafe lateral) e renasce alvos abatidos apos 2.5s
        for (int i = 0; i < targets.Count; i++)
        {
            TargetEntry t = targets[i];
            if (t.actor == null) continue;
            if (!t.actor.alive)
            {
                if (Time.time >= t.actor.respawnAt) t.actor.Respawn(t.basePos, 180f);
                continue;
            }
            if (t.mobile)
            {
                float offset = Mathf.Sin(Time.time * 1.4f + t.phase) * 2.4f;
                Vector3 p = t.basePos + new Vector3(offset, 0f, 0f);
                Vector3 delta = p - t.actor.transform.position;
                delta.y = 0f;
                if (t.actor.cc != null && t.actor.cc.enabled) t.actor.cc.Move(delta);
                t.actor.Animate(3.2f, delta / Mathf.Max(0.001f, Time.deltaTime), Vector3.back);
            }
        }

        // Atirador de teste controlado
        if (shooterActor != null && shooterActor.alive && Game.I.player.alive && Time.time >= nextShooterFire)
        {
            nextShooterFire = Time.time + 1.4f;
            Vector3 eye = shooterActor.EyePos;
            Vector3 playerCam = Game.I.pc != null && Game.I.pc.cam != null ? Game.I.pc.cam.transform.position : Game.I.player.EyePos;
            // No modo Flyby, mira 0.9m ao lado da camera (< 2.0m) para disparar estalo supersonico sem matar o jogador
            Vector3 aimPoint = shooterDirectHitMode ? Game.I.player.ChestPos : (playerCam + Game.I.player.transform.right * 0.9f);
            Vector3 dir = (aimPoint - eye).normalized;
            shooterActor.transform.rotation = Quaternion.LookRotation(new Vector3(dir.x, 0f, dir.z));
            WeaponDef w = Weapons.All[0];
            bool head, killed;
            float dmgMult = shooterDirectHitMode ? 0.2f : 1f;
            Combat.Hitscan(shooterActor, eye, dir, w, eye + dir * 0.6f, dmgMult, out head, out killed);
            Sfx.At(w.sound, eye, 0.85f);
        }
    }

    void OnGUI()
    {
        if (Game.I == null || Game.I.pc == null || Game.I.player == null) return;
        if (boxStyle == null)
        {
            boxStyle = new GUIStyle(GUI.skin.box);
            boxStyle.alignment = TextAnchor.UpperLeft;
            boxStyle.fontSize = 14;
            boxStyle.normal.textColor = Color.white;
        }
        PlayerController pc = Game.I.pc;
        Actor p = Game.I.player;
        float speed = pc.CurrentHorizontalVel.magnitude;
        float tagRem = Mathf.Max(0f, p.taggedUntil - Time.time);
        string posture = p.prone ? "DEITADO (PRONE)" : (p.crouching ? "AGACHADO" : "EM PE");
        string pingTxt = (Ping.I != null && !string.IsNullOrEmpty(Ping.I.lastRadioText)) ? Ping.I.lastRadioText : "-";
        string text = string.Format(
            "GREYBOX COMBAT TEST (FOV {0:0})\n" +
            "Vel Horizontal: {1:0.00} m/s  |  Sprint: {2}  |  ADS: {3}\n" +
            "Postura: {4} (H:{5:0.00}m)  |  Lean: {6:0.00}  |  Peek: {7:0.00}  |  HighReady: {8:0.00}  |  Vault: {9}\n" +
            "Tagging (-35%): {10}  |  Ultimo Radio/Ping: {11}\n" +
            "Ultimo Acerto: {12:0} dmg @ {13:0.0}m  |  Bullet Flybys: {14}  |  [T] Modo: {15}\n" +
            "[H] Hitboxes: {16}  |  [K] Clonar NPC ({17})  |  [L] Limpar Clones",
            pc.cam != null ? pc.cam.fieldOfView : 62f,
            speed,
            pc.isSprinting ? "SIM" : "NAO",
            pc.ads ? "SIM" : "NAO",
            posture,
            p.cc != null ? p.cc.height : 1.8f,
            pc.leanK,
            pc.peekK,
            pc.wallReadyK,
            pc.isVaulting ? "SIM" : "NAO",
            tagRem > 0f ? ("ATIVO (" + tagRem.ToString("0.00") + "s)") : "INATIVO",
            pingTxt,
            Combat.lastHitDamage,
            Combat.lastHitDistance,
            Combat.flybyCount,
            shooterDirectHitMode ? "TAGGING" : "FLYBY",
            showHitboxes ? "ON" : "OFF",
            clones.Count
        );
        GUI.Box(new Rect(18f, 18f, 620f, 152f), text, boxStyle);
    }

    void ClonePlayerAtCurrentPose()
    {
        Actor p = Game.I.player;
        if (p == null) return;
        float yaw = p.transform.eulerAngles.y;
        Vector3 exactPos = p.transform.position;
        Actor clone = SpawnActor("CLONE_POS_" + (clones.Count + 1), 0, false, exactPos, yaw, p.agent);
        if (clone.cc != null) clone.cc.enabled = false;
        clone.transform.SetPositionAndRotation(exactPos, p.transform.rotation);
        if (clone.cc != null)
        {
            clone.cc.enabled = true;
            clone.cc.detectCollisions = false;
            if (p.cc != null) Physics.IgnoreCollision(clone.cc, p.cc, true);
        }
        clone.SnapPosture(p.crouching, p.prone);
        PlayerController pc = Game.I.pc;
        if (pc != null && clone.body != null)
        {
            clone.body.SetWeapon(pc.cur);
            clone.body.transform.localPosition = new Vector3(pc.leanK * 0.40f, pc.peekK * 0.36f, 0f);
            clone.body.transform.localRotation = Quaternion.Euler(0f, 0f, -pc.leanK * 11f);
            clone.body.SnapPoseNow();
        }
        Vector3 aim = (pc != null && pc.cam != null) ? pc.cam.transform.forward : p.transform.forward;
        clone.Animate(0f, Vector3.zero, aim);
        clones.Add(clone);
        if (showHitboxes) UpdateHitboxVisuals();
    }

    void ClearClones()
    {
        for (int i = 0; i < clones.Count; i++)
        {
            Actor c = clones[i];
            if (c == null) continue;
            if (hitboxVis.TryGetValue(c, out Transform[] vis))
            {
                for (int k = 0; k < vis.Length; k++) if (vis[k] != null) Destroy(vis[k].gameObject);
                hitboxVis.Remove(c);
            }
            Game.I.actors.Remove(c);
            Destroy(c.gameObject);
        }
        clones.Clear();
    }

    void UpdateHitboxVisuals()
    {
        if (Game.I == null) return;
        for (int i = 0; i < Game.I.actors.Count; i++)
        {
            Actor a = Game.I.actors[i];
            if (a == null || a == Game.I.player) continue;
            Transform[] zones;
            if (!hitboxVis.TryGetValue(a, out zones))
            {
                zones = new Transform[3];
                Color[] cols = { new Color(0.15f, 0.85f, 1f, 0.4f), new Color(1f, 0.85f, 0.15f, 0.45f), new Color(1f, 0.2f, 0.2f, 0.55f) };
                for (int z = 0; z < 3; z++)
                {
                    GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    box.name = "HitboxZone_" + z;
                    Collider col = box.GetComponent<Collider>();
                    if (col != null) Destroy(col);
                    box.GetComponent<Renderer>().sharedMaterial = Art.Glow(cols[z], 0.9f);
                    zones[z] = box.transform;
                }
                hitboxVis[a] = zones;
            }
            bool visOn = showHitboxes && a.alive;
            float sc = a.CrouchScale;
            float[] yMin = { 0f, 1.00f * sc, 1.48f * sc };
            float[] yMax = { 1.00f * sc, 1.48f * sc, 1.80f * sc };
            float[] widths = { 0.62f, 0.58f, 0.36f };
            for (int z = 0; z < 3; z++)
            {
                if (zones[z] == null) continue;
                zones[z].gameObject.SetActive(visOn);
                if (!visOn) continue;
                float h = Mathf.Max(0.05f, yMax[z] - yMin[z]);
                Vector3 bodyOff = a.body != null ? (a.transform.rotation * a.body.transform.localPosition) : Vector3.zero;
                zones[z].position = a.transform.position + bodyOff + Vector3.up * ((yMin[z] + yMax[z]) * 0.5f);
                zones[z].rotation = a.transform.rotation;
                zones[z].localScale = new Vector3(widths[z], h, widths[z]);
            }
        }
    }
}
