using System.Collections.Generic;
using System.Text;
using UnityEngine;

// Painel de medicao de desempenho (Etapa 1a). Desligado para o jogador comum:
//  - no navegador so existe com "?debug=1" no endereço (ex.: fatalshooter.com.br/play/?debug=1);
//  - no Editor existe sempre, mas fica escondido ate apertar F9.
// Quando nao esta ativo, o componente nem e criado: custo zero.
// Mede em janelas de 30 s: FPS medio, tempo de quadro (mediana, p95, p99, maximo), engasgos e memoria.
// Cada janela vai para o console do navegador como "[PERF] ..." (chrome://inspect no celular) e pode ser copiada.
public class PerfHud : MonoBehaviour
{
#if UNITY_WEBGL && !UNITY_EDITOR
    [System.Runtime.InteropServices.DllImport("__Internal")] static extern float RfsDebugInfo(int kind);
    [System.Runtime.InteropServices.DllImport("__Internal")] static extern void RfsCopy(string text);
#endif

    const float Window = 30f;
    static PerfHud inst;
    bool visible;
    readonly List<float> frames = new List<float>(4096);
    float windowStart, liveTimer, countTimer;
    int liveFrames; float liveTime, liveMax;
    string live = "", last = "", counts = "";
    int windowNo;
    int gc0, gcSpikes, lastGc; long lastMem, allocBytes; bool first = true;
    GUIStyle style;

    public static void Init()
    {
        if (inst != null) return;
        bool on = false;
#if UNITY_EDITOR
        on = true;
#else
        string u = Application.absoluteURL ?? "";
        on = u.Contains("debug=1");
#endif
        if (!on) return;
        GameObject g = new GameObject("PerfHud");
        DontDestroyOnLoad(g);
        inst = g.AddComponent<PerfHud>();
#if UNITY_EDITOR
        inst.visible = false;   // F9 mostra
#else
        inst.visible = true;
#endif
    }

    void Start() { windowStart = Time.unscaledTime; useGUILayout = false; }

    void Update()
    {
#if UNITY_EDITOR
        if (Input.GetKeyDown(KeyCode.F9) || Input.GetKeyDown(KeyCode.F12)) visible = !visible;
#endif
        float dt = Time.unscaledDeltaTime;
        if (dt <= 0f || dt > 5f) return;   // aba em segundo plano ou pausa
        frames.Add(dt * 1000f);
        // Coleta de lixo e alocacao (estimada pela subida da memoria gerenciada entre quadros).
        int gcNow = System.GC.CollectionCount(0);
        long mem = System.GC.GetTotalMemory(false);
        if (first) { first = false; gc0 = gcNow; lastGc = gcNow; lastMem = mem; }
        if (mem > lastMem) allocBytes += mem - lastMem;
        lastMem = mem;
        if (gcNow != lastGc) { if (dt > 0.05f) gcSpikes++; lastGc = gcNow; }
        liveFrames++; liveTime += dt; if (dt > liveMax) liveMax = dt;
        liveTimer += dt;
        if (liveTimer >= 0.5f)
        {
            live = (liveFrames / liveTime).ToString("0") + " FPS   " + (liveTime / liveFrames * 1000f).ToString("0.0") + " ms   pior " + (liveMax * 1000f).ToString("0") + " ms";
            liveTimer = 0f; liveFrames = 0; liveTime = 0f; liveMax = 0f;
        }
        countTimer += dt;
        if (countTimer >= 2f && visible) { countTimer = 0f; Count(); }
        if (Time.unscaledTime - windowStart >= Window) CloseWindow();
    }

    // Quantos objetos estao desenhando agora (so roda com o painel visivel).
    void Count()
    {
        int rend = 0, lights = 0, ps = 0;
        foreach (Renderer r in FindObjectsByType<Renderer>()) if (r.enabled && r.gameObject.activeInHierarchy) rend++;
        foreach (Light l in FindObjectsByType<Light>()) if (l.enabled && l.gameObject.activeInHierarchy) lights++;
        foreach (ParticleSystem p in FindObjectsByType<ParticleSystem>()) if (p.isPlaying) ps++;
        int cams = 0;
        foreach (Camera c in Camera.allCameras) if (c.enabled) cams++;
        counts = "renderers " + rend + "   luzes " + lights + "   particulas " + ps + "   cameras " + cams;
#if UNITY_EDITOR
        counts += "   drawcalls " + UnityEditor.UnityStats.drawCalls + " setpass " + UnityEditor.UnityStats.setPassCalls + " tris " + (UnityEditor.UnityStats.triangles / 1000) + "k";
#endif
    }

    void CloseWindow()
    {
        windowStart = Time.unscaledTime;
        if (frames.Count < 10) { frames.Clear(); return; }
        float total = 0f; int spikes50 = 0, spikes100 = 0;
        foreach (float f in frames) { total += f; if (f > 50f) spikes50++; if (f > 100f) spikes100++; }
        frames.Sort();
        int n = frames.Count;
        float fps = n / (total / 1000f);
        int gcs = System.GC.CollectionCount(0) - gc0; gc0 = System.GC.CollectionCount(0);
        float allocMBs = allocBytes / (1024f * 1024f) / Mathf.Max(1f, total / 1000f);
        int gcSp = gcSpikes; gcSpikes = 0; allocBytes = 0;
        windowNo++;
        Count();
        StringBuilder sb = new StringBuilder();
        sb.Append("[PERF] #").Append(windowNo).Append(' ').Append(Scene())
          .Append(" | fps ").Append(fps.ToString("0.0"))
          .Append(" | ms med ").Append(frames[n / 2].ToString("0.0"))
          .Append(" p95 ").Append(frames[Mathf.Min(n - 1, (int)(n * 0.95f))].ToString("0.0"))
          .Append(" p99 ").Append(frames[Mathf.Min(n - 1, (int)(n * 0.99f))].ToString("0.0"))
          .Append(" max ").Append(frames[n - 1].ToString("0"))
          .Append(" | engasgos>50ms ").Append(spikes50).Append(" >100ms ").Append(spikes100)
          .Append(" | coletas GC ").Append(gcs).Append(" (").Append(gcSp).Append(" em engasgo) aloca ").Append(allocMBs.ToString("0.00")).Append(" MB/s")
          .Append(" | ").Append(counts)
          .Append(" | ").Append(Memory())
          .Append(" | ").Append(Render());
        last = sb.ToString();
        frames.Clear();
        Debug.Log(last);
#if UNITY_EDITOR
        try { System.IO.Directory.CreateDirectory("Logs"); System.IO.File.AppendAllText("Logs/perf.txt", System.DateTime.Now.ToString("HH:mm:ss ") + last + "\n"); } catch (System.Exception) { }
#endif
    }

    static string Scene()
    {
        Game g = Game.I;
        if (g == null) return "inicio";
        if (Race.Active) return "corrida";
        if (g.state == Game.State.Menu) return "menu";
        string map = Arena.mapId >= 0 && Arena.mapId < Arena.MapNames.Length ? Arena.MapNames[Arena.mapId] : "?";
        return (NetGame.Online ? "online " : "partida ") + map;
    }

    static string Memory()
    {
        long mono = System.GC.GetTotalMemory(false) / (1024 * 1024);
#if UNITY_WEBGL && !UNITY_EDITOR
        float heap = RfsDebugInfo(0), js = RfsDebugInfo(1);
        return "heap wasm " + heap.ToString("0") + " MB" + (js > 0f ? "  js " + js.ToString("0") + " MB" : "") + "  gc " + mono + " MB";
#else
        long res = UnityEngine.Profiling.Profiler.GetTotalReservedMemoryLong() / (1024 * 1024);
        return "reservada " + res + " MB  gc " + mono + " MB";
#endif
    }

    static string Render()
    {
        var urp = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline as UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset;
        string s = Screen.width + "x" + Screen.height;
        if (urp != null) s += " escala " + urp.renderScale.ToString("0.00") + " sombra " + urp.mainLightShadowmapResolution + "/" + urp.shadowDistance.ToString("0") + "m";
#if UNITY_WEBGL && !UNITY_EDITOR
        s += " dpr " + RfsDebugInfo(2).ToString("0.00") + "/" + RfsDebugInfo(3).ToString("0.00");
        if (RfsDebugInfo(4) > 0f) s += " tela " + RfsDebugInfo(4).ToString("0") + "Hz a cada " + RfsDebugInfo(5).ToString("0");
#endif
        s += " celular " + (TouchPad.On ? "sim" : "nao") + " gfx " + Settings.gfx + " fpsAlvo " + Application.targetFrameRate + " vsync " + QualitySettings.vSyncCount;
        return s;
    }

    void OnGUI()
    {
        if (!visible) return;
        GUI.depth = -1000;
        if (style == null) { style = new GUIStyle(GUI.skin.label); style.richText = false; style.wordWrap = true; }
        float s = Mathf.Max(1f, Screen.height / 1080f);
        style.fontSize = Mathf.RoundToInt(16 * s);
        float w = Mathf.Min(780f * s, Screen.width - 16f), x = Screen.width - w - 8f, y = 8f * s, lh = 22f * s;
        float h = lh * 4.4f + (last != "" ? lh * 6.4f : 0f);
        GUI.color = new Color(0f, 0f, 0f, 0.72f);
        GUI.DrawTexture(new Rect(x, y, w, h), Texture2D.whiteTexture);
        GUI.color = Color.white;
        style.normal.textColor = new Color(0.55f, 1f, 0.6f);
        GUI.Label(new Rect(x + 8f * s, y + 4f * s, w - 16f * s, lh), "DEBUG  " + live, style);
        style.normal.textColor = new Color(0.85f, 0.88f, 0.92f);
        int left = Mathf.CeilToInt(Window - (Time.unscaledTime - windowStart));
        GUI.Label(new Rect(x + 8f * s, y + 4f * s + lh, w - 16f * s, lh), Scene() + "   proxima medicao em " + left + " s", style);
        GUI.Label(new Rect(x + 8f * s, y + 4f * s + lh * 2f, w - 16f * s, lh), counts, style);
        GUI.Label(new Rect(x + 8f * s, y + 4f * s + lh * 3f, w - 16f * s, lh), Memory(), style);
        if (last != "")
        {
            style.normal.textColor = new Color(1f, 0.85f, 0.4f);
            GUI.Label(new Rect(x + 8f * s, y + 4f * s + lh * 4.2f, w - 130f * s, lh * 6.2f), last, style);
            if (GUI.Button(new Rect(x + w - 112f * s, y + h - 44f * s, 104f * s, 36f * s), "COPIAR"))
            {
#if UNITY_WEBGL && !UNITY_EDITOR
                RfsCopy(last);
#else
                GUIUtility.systemCopyBuffer = last;
#endif
            }
        }
    }
}
