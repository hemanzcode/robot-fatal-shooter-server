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
            "Postura: {4} (H:{5:0.00}m)  |  Lean: {6:0.00}  |  Peek: {7:0.00}  |  Vault: {8}\n" +
            "Tagging (-35%): {9}  |  Ultimo Radio/Ping: {10}\n" +
            "Ultimo Acerto: {11:0} dmg @ {12:0.0}m  |  Bullet Flybys: {13}  |  [T] Modo: {14}",
            pc.cam != null ? pc.cam.fieldOfView : 62f,
            speed,
            pc.isSprinting ? "SIM" : "NAO",
            pc.ads ? "SIM" : "NAO",
            posture,
            p.cc != null ? p.cc.height : 1.8f,
            pc.leanK,
            pc.peekK,
            pc.isVaulting ? "SIM" : "NAO",
            tagRem > 0f ? ("ATIVO (" + tagRem.ToString("0.00") + "s)") : "INATIVO",
            pingTxt,
            Combat.lastHitDamage,
            Combat.lastHitDistance,
            Combat.flybyCount,
            shooterDirectHitMode ? "TAGGING" : "FLYBY"
        );
        GUI.Box(new Rect(18f, 18f, 580f, 134f), text, boxStyle);
    }
}
