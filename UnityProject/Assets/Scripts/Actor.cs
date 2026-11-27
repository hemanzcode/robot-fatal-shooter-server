using UnityEngine;

// Estado comum a jogador e bots: time, vida, morte, renascimento e o modelo do robo.
public class Actor : MonoBehaviour
{
    public string displayName = "?";
    public int team;
    public bool isPlayer;
    public float health = 100f;     // jogadores e bots: 100 de vida (modo tatico)
    public float maxHealth = 100f;
    public bool alive = true;
    public int kills, deaths;
    public int headshots, bigKills, objectives;   // para o XP da partida online
    public float respawnAt;
    public float lastDamageTime = -99f;
    public float lastRemoved;       // vida que o ultimo TakeDamage tirou de fato (e o numero mostrado na tela)
    public float lastShotTime = -99f;
    public float lastPadTime = -99f;
    public float pendingLaunch;
    public float blindUntil, stunnedUntil, invulnerableUntil, revealedUntil, taggedUntil;
    public float MoveSpeedMult { get { return Time.time < taggedUntil ? 0.65f : 1f; } }
    public Vector3 externalVel;
    public Vector3 velocity;
    public bool noRespawn;
    public bool metal;             // meca: solta faisca em vez de sangue
    public float lastBloodTime = -9f;
    public bool creature;          // dinossauro: sem tiro na cabeca, nao conta ponto ao morrer
    public Vehicle vehicle;        // veiculo que esta pilotando
    public CharacterController cc;
    public GameObject model;
    // Agachado / deitado: capsula mais baixa, modelo ajustado e cabeca/peito mais baixos para tiros e mira.
    public bool crouching;
    public bool prone;
    public float crouchK;
    public float proneK;
    public float CrouchScale { get { return Mathf.Lerp(1f - 0.33f * crouchK, 0.32f, proneK); } }
    Vector3 modelScale = Vector3.zero;
    CapsuleCollider capCol;
    public System.Action onRespawn;
    public System.Action<Actor> onDamaged;

    Vector3 lastPos;

    public Vector3 EyePos { get { return transform.position + Vector3.up * (1.6f * CrouchScale); } }
    public Vector3 ChestPos { get { return transform.position + Vector3.up * (1.1f * CrouchScale); } }
    public Vector3 HeadPos { get { return transform.position + Vector3.up * (1.66f * CrouchScale); } }

    void Awake()
    {
        cc = GetComponent<CharacterController>();
        lastPos = transform.position;
    }

    public void Init(string n, int t, bool player, int agentIndex = 0, bool buildBody = true)
    {
        agent = agentIndex;
        displayName = n;
        team = t;
        isPlayer = player;
        if (!PlayerController.TacticalMode && Mathf.Abs(agentIndex) % AgentBody.Defs.Length == 6) maxHealth = health = 250f;
        if (!player && buildBody) BuildModel();
    }

    public AgentBody body;
    public int agent;

    void BuildModel()
    {
        AgentDef d = AgentBody.Defs[Mathf.Abs(agent) % AgentBody.Defs.Length];
        body = AgentBody.Create(transform, d, team, true);
        model = body.gameObject;
    }

    public void Animate(float speed, Vector3 moveDir, Vector3 aimDir)
    {
        if (body == null) return;
        body.speed = speed;
        body.moveDir = moveDir;
        body.aimDir = aimDir;
    }

    void ApplyPosture(float dt)
    {
        bool active = alive && vehicle == null && !creature;
        float targetProne = active && prone ? 1f : 0f;
        float targetCrouch = active && crouching && !prone ? 1f : 0f;
        if (Mathf.Approximately(crouchK, targetCrouch) && Mathf.Approximately(proneK, targetProne)) return;
        crouchK = Mathf.MoveTowards(crouchK, targetCrouch, dt * 7f);
        proneK = Mathf.MoveTowards(proneK, targetProne, dt * 5.5f);
        float standOrCrouchH = Mathf.Lerp(1.8f, 1.2f, crouchK);
        // cc.radius = 0.35f -> altura minima segura >= 0.70f para nao atravessar o piso.
        float hgt = Mathf.Lerp(standOrCrouchH, 0.72f, proneK);
        if (cc != null) { cc.height = hgt; cc.center = new Vector3(0f, hgt / 2f, 0f); }
        else
        {
            if (capCol == null) capCol = GetComponent<CapsuleCollider>();
            if (capCol != null) { capCol.height = hgt; capCol.center = new Vector3(0f, hgt / 2f, 0f); }
        }
        if (model != null)
        {
            if (modelScale == Vector3.zero) modelScale = model.transform.localScale;
            float sy = Mathf.Lerp(Mathf.Lerp(1f, 0.7f, crouchK), 0.55f, proneK);
            model.transform.localScale = new Vector3(modelScale.x, modelScale.y * sy, modelScale.z);
        }
    }

    void Update()
    {
        float dt = Mathf.Max(Time.deltaTime, 0.0001f);
        ApplyPosture(dt);
        velocity = (transform.position - lastPos) / dt;
        lastPos = transform.position;
        float damp = (cc != null && cc.enabled && cc.isGrounded) ? 8f : 2.5f;
        externalVel = Vector3.Lerp(externalVel, Vector3.zero, 1f - Mathf.Exp(-damp * dt));

        if (!NetGame.IsClient && alive && health < maxHealth && Time.time - lastDamageTime > 5f)
            health = Mathf.Min(maxHealth, health + 12f * dt);

        if (alive && transform.position.y < -25f)
        {
            if (NetGame.IsClient) { if (isPlayer) Game.I.RespawnLocal(); }
            else TakeDamage(9999f, null, false, "queda");
        }
    }

    // Sangue (ou faisca, no meca) no ponto do acerto. dir = para onde a bala ia.
    public void Bleed(Vector3 point, Vector3 dir, float dmg, bool head)
    {
        if (dmg <= 0f || Time.time < invulnerableUntil) return;
        lastBloodTime = Time.time;
        if (metal) { Fx.Spark(point, -dir.normalized, new Color(1f, 0.8f, 0.4f)); return; }
        // O proprio jogador nao ve o jato (esta dentro da camera): ganha o respingo na tela.
        if (isPlayer) return;
        Fx.Blood(point, dir, dmg, head);
    }

    void DeathFx()
    {
        if (metal) Fx.Explosion(ChestPos, Art.TeamColor(team), 0.35f);
        else Fx.BloodDeath(ChestPos, transform.position);
    }

    // Cliente online: vida e morte vem do host.
    public void NetState(float hp, bool isAlive, bool puppet)
    {
        if (isPlayer && hp < health - 0.5f && Game.I != null) { Game.I.damageFlash = Time.time; Sfx.UI("hurt", 0.5f); lastDamageTime = Time.time; taggedUntil = Time.time + 0.35f; Fx.ScreenBlood(health - hp); }
        // Dano que este cliente nao viu acontecer (tiro de outro jogador, explosao): sangra no peito.
        else if (!isPlayer && alive && hp < health - 0.5f && Time.time - lastBloodTime > 0.6f)
            Bleed(ChestPos, new Vector3(Random.Range(-1f, 1f), 0.1f, Random.Range(-1f, 1f)), health - hp, false);
        health = hp;
        if (isAlive == alive) return;
        if (!isAlive)
        {
            if (vehicle != null) vehicle.Exit();
            alive = false;
            respawnAt = Time.time + 3f;
            DeathFx();
            Sfx.At("death", ChestPos, 0.8f);
            if (body != null) body.Die(); else if (model != null) model.SetActive(false);
            if (cc != null) cc.enabled = false;
        }
        else if (puppet)
        {
            alive = true;
            if (model != null) model.SetActive(true);
            if (body != null) body.Revive();
        }
        // O jogador local so volta quando o host manda o ponto de renascimento (mensagem SP).
    }

    public bool TakeDamage(float dmg, Actor from, bool head, string weapon)
    {
        lastRemoved = 0f;
        if (NetGame.IsClient) return false;
        if (!alive || Game.I == null || Game.I.matchOver || Time.time < invulnerableUntil) return false;
        // Vida e dano inteiros: o numero na tela bate exatamente com a vida descontada.
        health = Mathf.Ceil(Mathf.Max(0f, health));
        dmg = Mathf.Round(dmg);
        if (dmg <= 0f) return false;
        lastRemoved = Mathf.Min(dmg, health);
        health -= lastRemoved;
        lastDamageTime = Time.time;
        taggedUntil = Time.time + 0.35f;
        if (isPlayer)
        {
            Game.I.damageFlash = Time.time;
            Sfx.UI("hurt", 0.5f);
            if (weapon != "queda") { if (from != null) Fx.ScreenBlood(lastRemoved, from.EyePos); else Fx.ScreenBlood(lastRemoved); }
        }
        // Dano sem ponto de acerto (mordida, atropelamento, tiro vindo de um cliente): sangra no peito.
        else if (Time.time - lastBloodTime > 0.05f && weapon != "tempo" && weapon != "queda")
            Bleed(ChestPos, from != null ? ChestPos - from.ChestPos : Vector3.down, lastRemoved, head);
        if (onDamaged != null) onDamaged(from);
        if (health <= 0f)
        {
            Die(from, head, weapon);
            return true;
        }
        return false;
    }

    void Die(Actor from, bool head, string weapon)
    {
        if (vehicle != null) vehicle.Exit();
        alive = false;
        health = 0f;
        deaths++;
        respawnAt = Time.time + 3f;
        DeathFx();
        Sfx.At("death", ChestPos, 0.8f);
        if (body != null) body.Die(); else if (model != null) model.SetActive(false);
        if (cc != null) cc.enabled = false;
        externalVel = Vector3.zero;
        pendingLaunch = 0f;
        Game.I.OnKill(from, this, head, weapon);
    }

    public void Respawn(Vector3 pos, float yaw)
    {
        if (cc != null) cc.enabled = false;
        transform.position = pos + Vector3.up * 0.1f;
        transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        lastPos = transform.position;
        if (cc != null) cc.enabled = true;
        health = maxHealth;
        alive = true;
        externalVel = Vector3.zero;
        pendingLaunch = 0f;
        lastDamageTime = -99f;
        blindUntil = stunnedUntil = invulnerableUntil = revealedUntil = taggedUntil = 0f;
        if (model != null) model.SetActive(true);
        if (body != null) body.Revive();
        if (onRespawn != null) onRespawn();
    }
}
