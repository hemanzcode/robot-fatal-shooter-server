using UnityEngine;

// Tutorial interativo: cada passo so avanca quando o jogador realmente executa a acao.
public class Tutorial : MonoBehaviour
{
    struct Step
    {
        public string title, body;
        public System.Func<bool> done;
        public System.Action begin;
    }

    Step[] steps;
    int idx;
    float doneAt = -1f;
    PlayerController.Stats s0;
    int kills0;
    PlayerController pc;
    Actor player;

    public bool StepDone { get { return doneAt >= 0f; } }

    // Botao "PULAR ETAPA": da o passo atual como feito (nunca prende o jogador num passo).
    public void SkipStep()
    {
        if (steps == null || idx >= steps.Length || doneAt >= 0f) return;
        doneAt = Time.time - 0.8f;
        Sfx.UI("pickup", 0.5f);
    }
    public string Header { get { return "TUTORIAL  " + Mathf.Min(idx + 1, steps.Length) + " / " + steps.Length; } }
    public string Title { get { return idx < steps.Length ? steps[idx].title : "CONCLUIDO"; } }
    public string Body
    {
        get
        {
            if (idx >= steps.Length) return "";
            if (nearestPad != null && beacon != null && player != null)
                return steps[idx].body + "  (" + Mathf.RoundToInt(Vector3.Distance(player.transform.position, nearestPad.position)) + " m)";
            return steps[idx].body;
        }
    }

    void Start()
    {
        pc = Game.I.pc;
        player = Game.I.player;
        bool t = TouchPad.On;   // celular: instrucoes de toque em vez de teclado e mouse
        steps = new Step[]
        {
            S("OLHE AO REDOR", t ? "Arraste o dedo no lado DIREITO da tela para olhar em volta." : "Mova o mouse para olhar em volta.", () => D.look > 220f),
            S("ANDE", t ? "Arraste o dedo no lado ESQUERDO da tela para andar." : "Use W A S D para se mover.", () => D.moved > 14f),
            S("CORRA", t ? "Empurre o analogico ate o fim para a frente para correr." : "Segure SHIFT enquanto anda para a frente.", () => D.sprinted > 14f),
            S("PULO DUPLO", t ? "Toque em PULO e, ainda no ar, toque de novo." : "Aperte ESPACO e, no ar, ESPACO de novo.", () => D.doubleJumps >= 1),
            S("DASH", t ? "Toque em E para um impulso rapido na direcao em que anda." : "Aperte E para um impulso rapido na direcao em que anda.", () => D.dashes >= 1),
            S("ATIRE", t ? "Segure o botao TIRO. De 5 tiros (arrastar o dedo no TIRO tambem mira)." : "Botao esquerdo do mouse dispara. De 5 tiros.", () => D.shots >= 5),
            S("RECARREGUE", t ? "Toque em R para recarregar a arma." : "Aperte R para recarregar a arma.", () => D.reloads >= 1),
            S("MIRE", t ? "Toque em MIRA para mirar (toque de novo para soltar): menos dispersao." : "Segure o botao direito para mirar: menos dispersao, mais precisao.", () => D.adsTime > 1.2f),
            S("ELIMINE OS ALVOS", "Acerte os 3 alvos a frente. Tiros na cabeca dao o dobro de dano.", () => player.kills - kills0 >= 3, SpawnTargets),
            S("TROQUE DE ARMA", t ? "Toque nas armas no topo da tela. Experimente outras duas." : "Use 1 2 3 4 5 ou o scroll. Experimente outras duas armas.", () => Bits(pc.stats.switchMask & ~s0.switchMask) >= 2),
            S("UTILIDADE", t ? "Toque em Q para usar sua habilidade principal. O tempo de recarga aparece no botao." : "Aperte Q para usar sua habilidade principal. O tempo de recarga aparece no HUD.", () => D.nades >= 1),
            S("SOBRECARGA", t ? "Sua habilidade final esta carregada. Toque em X: tiro mais rapido sem gastar municao." : "Sua habilidade final esta carregada. Aperte X: tiro mais rapido sem gastar municao.", () => D.ults >= 1, () => pc.AddUlt(100f)),
            S("PLATAFORMA DE SALTO", "As plataformas amarelas lancam voce para o alto. Siga o feixe amarelo e pise em uma.", () => D.pads >= 1, MarkPad),
            S("OBJETIVO", t ? "Na partida, seu time vence ao atingir o limite de abates. Itens verdes curam, laranjas dao municao. PLACAR mostra o placar." : "Na partida, seu time vence ao atingir o limite de abates. Itens verdes curam, laranjas dao municao. TAB mostra o placar.", () => Time.time - stepStart > 6f)
        };
        Begin();
    }

    float stepStart;
    PlayerController.Stats D
    {
        get
        {
            PlayerController.Stats a = pc.stats, d = new PlayerController.Stats();
            d.look = a.look - s0.look; d.moved = a.moved - s0.moved; d.sprinted = a.sprinted - s0.sprinted;
            d.adsTime = a.adsTime - s0.adsTime; d.doubleJumps = a.doubleJumps - s0.doubleJumps; d.dashes = a.dashes - s0.dashes;
            d.shots = a.shots - s0.shots; d.reloads = a.reloads - s0.reloads; d.nades = a.nades - s0.nades;
            d.ults = a.ults - s0.ults; d.pads = a.pads - s0.pads;
            return d;
        }
    }

    static Step S(string t, string b, System.Func<bool> done, System.Action begin = null)
    {
        Step s = new Step(); s.title = t; s.body = b; s.done = done; s.begin = begin; return s;
    }

    static int Bits(int v) { int n = 0; while (v != 0) { n += v & 1; v >>= 1; } return n; }

    void Begin()
    {
        s0 = pc.stats;
        kills0 = player.kills;
        stepStart = Time.time;
        doneAt = -1f;
        if (steps[idx].begin != null) steps[idx].begin();
    }

    // Alvos sempre a vista: so usa pontos do mapa com linha de visada livre a partir do jogador.
    void SpawnTargets()
    {
        Transform t = player.transform;
        Vector3 eye = player.EyePos;
        System.Collections.Generic.List<Vector3> picks = new System.Collections.Generic.List<Vector3>();
        for (int pass = 0; pass < 2 && picks.Count < 3; pass++)
        {
            float bestDot = pass == 0 ? 0.35f : -2f;
            for (int n = picks.Count; n < 3; n++)
            {
                Vector3 best = Vector3.zero; float bs = -1e9f;
                for (int k = 0; k < Arena.walkable.Count; k++)
                {
                    Vector3 w = Arena.walkable[k];
                    Vector3 to = w - t.position; to.y = 0f;
                    float d = to.magnitude;
                    if (d < 7f || d > 24f) continue;
                    float dot = Vector3.Dot(to / d, t.forward);
                    if (dot < bestDot) continue;
                    bool near = false;
                    for (int j = 0; j < picks.Count; j++) if ((picks[j] - w).sqrMagnitude < 9f) near = true;
                    if (near) continue;
                    if (Physics.Linecast(eye, w + Vector3.up * 1.2f, ~0, QueryTriggerInteraction.Ignore)) continue;
                    float sc = dot * 10f - Mathf.Abs(d - 13f);
                    if (sc > bs) { bs = sc; best = w; }
                }
                if (bs < -1e8f) break;
                picks.Add(best);
            }
        }
        for (int i = picks.Count; i < 3; i++) picks.Add(t.position + t.forward * (8f + i * 2f) + t.right * ((i - 1) * 2.5f));
        for (int i = 0; i < 3; i++)
        {
            Vector3 look = t.position - picks[i]; look.y = 0f;
            Game.I.SpawnDummy(picks[i], look.sqrMagnitude > 0.01f ? Quaternion.LookRotation(look).eulerAngles.y : 0f);
        }
    }

    // Feixe de luz sobre a plataforma de salto mais proxima, para o jogador achar o caminho.
    GameObject beacon;
    Transform nearestPad;
    void MarkPad()
    {
        float bd = 1e9f;
        foreach (JumpPad p in Object.FindObjectsByType<JumpPad>(FindObjectsSortMode.None))
        {
            float d = (p.transform.position - player.transform.position).sqrMagnitude;
            if (d < bd) { bd = d; nearestPad = p.transform; }
        }
        if (nearestPad == null) return;
        beacon = Art.Box(transform, Vector3.zero, new Vector3(0.35f, 60f, 0.35f), Art.Glow(Art.Yellow, 1.2f), false, "padBeacon");
        beacon.transform.position = nearestPad.position + Vector3.up * 30f;
    }

    void OnDestroy()
    {
        if (beacon != null) Destroy(beacon);
    }

    void Update()
    {
        if (Game.I == null || Game.I.matchOver || pc == null || steps == null || idx >= steps.Length) return;
        if (doneAt < 0f)
        {
            if (steps[idx].done())
            {
                doneAt = Time.time;
                Sfx.UI("pickup", 0.7f);
            }
            return;
        }
        if (Time.time - doneAt < 1.1f) return;
        idx++;
        if (beacon != null) { Destroy(beacon); beacon = null; }
        if (idx >= steps.Length)
        {
            Settings.tutorialDone = true;
            Settings.Save();
            Game.I.FinishTutorial();
            return;
        }
        Begin();
    }
}
