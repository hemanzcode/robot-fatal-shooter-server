using UnityEngine;
using UnityEngine.Video;

// Video promocional em loop (sem som) ocupando a tela inteira atras do menu. O arquivo vem do servidor
// do jogo (/promo/menu.mp4), em cache no navegador. Enquanto nao carrega, aparece a cidade como antes.
public class MenuVideo : MonoBehaviour
{
    static MenuVideo inst;
    VideoPlayer vp;
    RenderTexture rt;
    bool failed;
    int fails;
    float retryAt;
    float shownAt = -1f, nextPrepare;

    public static Texture Tex { get { return inst != null && inst.Ready ? inst.rt : null; } }
    public static float Alpha { get { return inst == null || inst.shownAt < 0f ? 0f : Mathf.Clamp01((Time.unscaledTime - inst.shownAt) / 0.8f); } }

    bool Ready { get { return !failed && vp != null && vp.isPrepared && (vp.frame >= 1 || vp.time > 0.05); } }

    static string Url()
    {
        string u = Application.absoluteURL;
        if (Application.platform == RuntimePlatform.WebGLPlayer && !string.IsNullOrEmpty(u))
        {
            try { System.Uri uri = new System.Uri(u); return uri.GetLeftPart(System.UriPartial.Authority) + "/promo/menu.mp4"; } catch (System.Exception) { }
        }
        return "https://fatalshooter.com.br/promo/menu.mp4";
    }

    // Fica num objeto proprio, sempre ativo (a camera do menu e desligada durante a partida).
    public static void Attach(Camera cam)
    {
        if (inst != null) return;
        GameObject g = new GameObject("MenuVideo");
        DontDestroyOnLoad(g);
        inst = g.AddComponent<MenuVideo>();
        inst.rt = new RenderTexture(1280, 720, 0);
        inst.vp = g.AddComponent<VideoPlayer>();
        inst.vp.playOnAwake = false;
        inst.vp.source = VideoSource.Url;
        inst.vp.url = Url();
        inst.vp.isLooping = true;
        inst.vp.audioOutputMode = VideoAudioOutputMode.None;
        inst.vp.renderMode = VideoRenderMode.RenderTexture;
        inst.vp.targetTexture = inst.rt;
        inst.vp.skipOnDrop = true;
        // Erro (servidor dormindo/acordando, rede lenta): tenta de novo em alguns segundos, ate 8 vezes.
        inst.vp.errorReceived += (p, msg) =>
        {
            inst.failed = true; inst.fails++;
            inst.retryAt = Time.unscaledTime + Mathf.Min(30f, 5f * inst.fails);
            Debug.LogWarning("MenuVideo (tentativa " + inst.fails + "): " + msg);
        };
        inst.vp.prepareCompleted += p => Debug.Log("MenuVideo: pronto " + p.width + "x" + p.height);
        inst.vp.Prepare();
    }

    void Update()
    {
        if (failed && fails < 8 && Time.unscaledTime >= retryAt)
        {
            failed = false;
            vp.Stop();
            vp.url = Url();
            vp.Prepare();
            nextPrepare = Time.unscaledTime + 15f;
        }
        Game g = Game.I;
        bool want = !failed && g != null && g.state == Game.State.Menu;
        if (!want)
        {
            if (vp != null && vp.isPlaying) vp.Pause();
            shownAt = -1f;
            return;
        }
        if (!vp.isPrepared && !vp.isPlaying && Time.unscaledTime >= nextPrepare)
        {
            nextPrepare = Time.unscaledTime + 15f;
            vp.Prepare();
        }
        if (vp.isPrepared && !vp.isPlaying) vp.Play();
        if (Ready && shownAt < 0f) shownAt = Time.unscaledTime;
    }
}
