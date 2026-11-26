using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

// Voz do time (estilo Valorant/CS): segurar V (PC) ou FALAR (celular) para falar com os companheiros de time.
// Cada jogador conversa direto com os outros pelo navegador (WebRTC); o servidor do jogo so repassa os
// "apresentacoes" (mensagens V). So funciona na versao Web (site e celular).
public class VoiceChat : MonoBehaviour
{
    public static VoiceChat I;
    public static bool Active { get { return I != null && I.running; } }

#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")] static extern void RfsVoiceStart();
    [DllImport("__Internal")] static extern void RfsVoicePeers(int me, string csv);
    [DllImport("__Internal")] static extern void RfsVoiceSignal(int from, string b64);
    [DllImport("__Internal")] static extern System.IntPtr RfsVoicePoll();
    [DllImport("__Internal")] static extern void RfsVoiceTalk(int on);
    [DllImport("__Internal")] static extern int RfsVoiceMicState();
    [DllImport("__Internal")] static extern System.IntPtr RfsVoiceStatus();
    [DllImport("__Internal")] static extern void RfsVoiceMute(int id, int muted);
    [DllImport("__Internal")] static extern void RfsVoiceVolume(float v);
    [DllImport("__Internal")] static extern void RfsVoiceStop();
    [DllImport("__Internal")] static extern void RfsFree(System.IntPtr p);
    static string Take(System.IntPtr p) { if (p == System.IntPtr.Zero) return null; string s = Marshal.PtrToStringUTF8(p); RfsFree(p); return s; }
    const bool Supported = true;
#else
    static void RfsVoiceStart() { }
    static void RfsVoicePeers(int me, string csv) { }
    static void RfsVoiceSignal(int from, string b64) { }
    static System.IntPtr RfsVoicePoll() { return System.IntPtr.Zero; }
    static void RfsVoiceTalk(int on) { }
    static int RfsVoiceMicState() { return 0; }
    static System.IntPtr RfsVoiceStatus() { return System.IntPtr.Zero; }
    static void RfsVoiceMute(int id, int muted) { }
    static void RfsVoiceVolume(float v) { }
    static void RfsVoiceStop() { }
    static string Take(System.IntPtr p) { return null; }
    const bool Supported = false;
#endif

    bool running, talking;
    string peersCsv = "";
    float nextPeers, nextStatus;
    float lastVol = -1f;
    public int connected;
    public readonly HashSet<int> speaking = new HashSet<int>();
    public readonly HashSet<int> muted = new HashSet<int>();

    public static void Ensure()
    {
        if (I != null) return;
        GameObject g = new GameObject("VoiceChat");
        DontDestroyOnLoad(g);
        I = g.AddComponent<VoiceChat>();
    }

    // Companheiros de time na sala (sem mim e sem quem esta fora).
    public List<NetPlayer> Mates()
    {
        List<NetPlayer> l = new List<NetPlayer>();
        Net n = Net.I;
        if (n == null || !n.InRoom) return l;
        NetPlayer me = null;
        foreach (NetPlayer p in n.players) if (p.id == n.myId) me = p;
        if (me == null) return l;
        foreach (NetPlayer p in n.players) if (p.id != n.myId && p.team == me.team) l.Add(p);
        return l;
    }

    public void ToggleMute(int id)
    {
        bool m = !muted.Contains(id);
        if (m) muted.Add(id); else muted.Remove(id);
        RfsVoiceMute(id, m ? 1 : 0);
    }

    // Mensagem V de outro jogador (repassada pelo servidor do jogo).
    public static void Incoming(int from, string b64)
    {
        if (I != null && I.running) RfsVoiceSignal(from, b64);
    }

    void Update()
    {
        bool want = Supported && Settings.voiceOn && Net.I != null && Net.I.InRoom && Game.I != null && Game.I.state != Game.State.Menu;
        if (want && !running) { RfsVoiceStart(); running = true; peersCsv = ""; lastVol = -1f; foreach (int id in muted) RfsVoiceMute(id, 1); }
        if (!want && running) { RfsVoiceStop(); running = false; talking = false; speaking.Clear(); connected = 0; peersCsv = ""; }
        if (!running) return;

        if (Time.unscaledTime >= nextPeers)
        {
            nextPeers = Time.unscaledTime + 0.5f;
            var sb = new System.Text.StringBuilder();
            foreach (NetPlayer p in Mates()) if (!p.away) sb.Append(p.id).Append(',');
            string csv = sb.ToString();
            if (csv != peersCsv) { peersCsv = csv; RfsVoicePeers(Net.I.myId, csv); }
        }
        // Mensagens que o navegador quer mandar: "para|conteudo".
        for (int i = 0; i < 20; i++)
        {
            string m = Take(RfsVoicePoll());
            if (m == null) break;
            int bar = m.IndexOf('|');
            int to;
            if (bar > 0 && int.TryParse(m.Substring(0, bar), out to)) Net.I.SendGame(to, "V;" + m.Substring(bar + 1));
        }
        bool t = (Input.GetKey(KeyCode.V) || TouchPad.Talk) && !(Game.I.menus != null && Game.I.menus.IsOpen && !TouchPad.On);
        if (t != talking) { talking = t; RfsVoiceTalk(t ? 1 : 0); }
        if (lastVol != Settings.voiceVol) { lastVol = Settings.voiceVol; RfsVoiceVolume(Settings.voiceVol); }
        if (Time.unscaledTime >= nextStatus)
        {
            nextStatus = Time.unscaledTime + 0.12f;
            string st = Take(RfsVoiceStatus());
            speaking.Clear();
            if (st != null)
            {
                string[] parts = st.Split(';');
                int.TryParse(parts[0], out connected);
                if (parts.Length > 1) foreach (string s in parts[1].Split(',')) { int id; if (int.TryParse(s, out id)) speaking.Add(id); }
            }
        }
    }

    // Quem esta falando (esquerda, acima da vida) e o aviso do proprio microfone.
    void OnGUI()
    {
        if (!running || Game.I == null || Game.I.state == Game.State.Menu || (Game.I.menus != null && Game.I.menus.IsOpen)) return;
        float s = Screen.height / 1080f;
        GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));
        float x = 30f, y = 640f;
        int mic = RfsVoiceMicState();
        if (talking)
        {
            Color c = mic == 2 ? new Color(1f, 0.4f, 0.3f) : new Color(0.35f, 1f, 0.5f);
            Game.Fill(x, y, 270f, 34f, new Color(0f, 0f, 0f, 0.55f));
            Game.Fill(x, y, 6f, 34f, c);
            Game.Text(x + 16f, y, 260f, 34f, mic == 2 ? "MICROFONE BLOQUEADO" : "FALANDO COM O TIME", 17, c, TextAnchor.MiddleLeft);
            y -= 40f;
        }
        foreach (NetPlayer p in Mates())
        {
            if (!speaking.Contains(p.id)) continue;
            Game.Fill(x, y, 270f, 30f, new Color(0f, 0f, 0f, 0.5f));
            float pulse = 0.6f + 0.4f * Mathf.Sin(Time.unscaledTime * 12f);
            for (int b = 0; b < 3; b++) Game.Fill(x + 10f + b * 6f, y + 22f - (6f + b * 4f) * pulse, 4f, (6f + b * 4f) * pulse, Art.TeamColor(p.team));
            Game.Text(x + 40f, y, 230f, 30f, p.name, 17, Color.white, TextAnchor.MiddleLeft);
            y -= 34f;
        }
        if (mic == 2 && !talking && Time.unscaledTime % 20f < 5f)
            Game.Text(x, 600f, 600f, 26f, "Microfone bloqueado: libere no navegador para falar com o time.", 15, new Color(1f, 1f, 1f, 0.7f), TextAnchor.MiddleLeft);
    }
}
