using System.Collections.Generic;
using UnityEngine;

// Ponto de entrada. Monta tudo sozinho ao apertar Play (nao precisa configurar cena):
// cidade, menus, partidas contra bots, tutorial, HUD, chuva e musica.
public class Game : MonoBehaviour
{
    public static Game I;

    public enum State { Menu, Playing, Tutorial }
    public State state = State.Menu;

    public readonly List<Actor> actors = new List<Actor>();
    public int[] score = new int[2];
    public int scoreLimit = 30;
    public bool matchOver;
    public float timeLeft;
    public float damageFlash = -99f;
    public Actor player;
    public PlayerController pc;
    public Menus menus;
    public Tutorial tutorial;
    public bool roundMode;          // modo bomba: rodadas sem renascer
    public BombMode bomb;
    public Campaign campaign;
    float nextCrate;

    struct Feed { public string text; public Color color; public float time; }
    readonly List<Feed> feed = new List<Feed>();
    float hitTime = -99f;
    bool hitHead, hitKill;
    string banner = "";
    float bannerTime = -99f;
    static GUIStyle style;
    AudioSource music, rainAudio;
    Light sunLight;
    float fogClock, fogWarned = -1f;
    ParticleSystem rain;
    Camera menuCam;
    AgentBody menuAgent;
    int menuAgentIndex = -1;
    AudioListener menuEar;
    Transform world;

    static readonly string[] BotNames = { "VEX", "NYX", "KAIRO", "ZED", "LUMEN", "ORBIT", "HEX", "RAZE", "ION", "FLUX" };

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (I != null) return;
        GameObject g = new GameObject("RobotFatalShooter");
        g.AddComponent<Game>();
    }

    static T[] All<T>() where T : Object
    {
#if UNITY_2023_1_OR_NEWER
        return FindObjectsByType<T>(FindObjectsSortMode.None);
#else
        return FindObjectsOfType<T>();
#endif
    }

#if UNITY_WEBGL && !UNITY_EDITOR
    [System.Runtime.InteropServices.DllImport("__Internal")] static extern void RfsAudioSetup();
#endif

    void Awake()
    {
        I = this;
        Time.timeScale = 1f;
        TouchPad.Detect();
        gameObject.AddComponent<TouchPad>();
        gameObject.AddComponent<DamagePopups>();
        gameObject.AddComponent<Ping>();
        PerfHud.Init();   // painel de medicao: so com ?debug=1 (ou F9 no Editor)
#if UNITY_WEBGL && !UNITY_EDITOR
        RfsAudioSetup();
#endif
        useGUILayout = false;
        if (TouchPad.On) MobileProfile();
#if UNITY_WEBGL && !UNITY_EDITOR
        else if ((Application.absoluteURL ?? "").Contains("capteste")) RfsFrameCap(60);   // teste do limite de FPS no PC
#endif
#if UNITY_EDITOR
        try { System.IO.Directory.CreateDirectory("Logs"); System.IO.File.WriteAllText("Logs/runtime.txt", "PLAY " + System.DateTime.Now.ToString("HH:mm:ss") + "\n"); } catch (System.Exception) { }
        Application.logMessageReceived += OnLog;
#endif
        if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "Greybox_CombatTest")
        {
            greybox = true;
            Settings.Load();
            Settings.Apply();
            Sfx.Warm(); Fx.Warm();
            menus = gameObject.AddComponent<Menus>();
            state = State.Playing;
            scoreLimit = 9999;
            timeLeft = 99999f;
            return;
        }

        // Desliga cameras, luzes e ouvintes que ja existam na cena.
        foreach (Camera c in All<Camera>()) c.gameObject.SetActive(false);
        foreach (Light l in All<Light>()) l.gameObject.SetActive(false);
        foreach (AudioListener a in All<AudioListener>()) a.enabled = false;

        Settings.Load();
        Settings.Apply();

        // Tarde no deserto: ceu limpo, sol forte e baixo, poeira no ar.
        Post.Sky();
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Exponential;
        RenderSettings.fogColor = new Color(0.78f, 0.7f, 0.58f);
        RenderSettings.fogDensity = 0.0045f;

        GameObject sun = new GameObject("Sun");
        sun.transform.SetParent(transform, false);
        sun.transform.rotation = Quaternion.Euler(46f, 38f, 0f);
        Light sl = sun.AddComponent<Light>();
        sunLight = sl;
        sl.type = LightType.Directional;
        sl.color = new Color(1f, 0.94f, 0.82f);
        sl.intensity = 1.6f;
        sl.shadows = TouchPad.On ? LightShadows.Hard : LightShadows.Soft;
        sl.shadowStrength = 0.9f;
        RenderSettings.sun = sl;
        Post.Sky();

        Post.Build(transform);

        GameObject w = new GameObject("World");
        w.transform.SetParent(transform, false);
        world = w.transform;
        Arena.Build(world, 0);
        Post.Probe(transform, new Vector3(Arena.W * Arena.Cell, 40f, Arena.H * Arena.Cell));

        GameObject mc = new GameObject("MenuCamera");
        mc.transform.SetParent(transform, false);
        menuCam = mc.AddComponent<Camera>();
        SetupCamera(menuCam);
        menuCam.fieldOfView = 60f;
        menuEar = mc.AddComponent<AudioListener>();
        // MenuVideo.Attach(menuCam);   // identidade nova: o menu mostra a cena do jogo + agente (o video fica guardado)
        VoiceChat.Ensure();          // voz do time nas salas online (versao Web)   // video promocional em tela cheia atras do menu
        Sfx.Warm(); Fx.Warm();       // gravacoes dos tiros e texturas de sangue prontas antes do primeiro disparo

        BuildRain();

        music = gameObject.AddComponent<AudioSource>();
        music.clip = Sfx.Get("music");
        music.loop = true;
        music.spatialBlend = 0f;
        music.pitch = 1f;
        music.volume = 0.24f * Settings.music;
        music.Play();

        rainAudio = gameObject.AddComponent<AudioSource>();
        rainAudio.clip = Sfx.Get("rain");
        rainAudio.loop = true;
        rainAudio.spatialBlend = 0f;
        rainAudio.volume = Settings.rain ? 0.2f * Settings.sfx : 0f;
        rainAudio.Play();

        Sfx.UI("startup", 0.65f);

        menus = gameObject.AddComponent<Menus>();
        menus.Open(Menus.Page.Main);
        // Primeira execucao: o tutorial abre sozinho (depois fica disponivel no menu).
        // Abriu por link de convite: vai direto para a conta/sala (sem tutorial).
        if (Net.InviteCode != "") { Net.Ensure(); menus.Open(Menus.Page.Login); }
        else if (!Settings.tutorialDone) StartMatch(true);
    }

#if UNITY_EDITOR
    void OnDestroy() { Application.logMessageReceived -= OnLog; }

    static void OnLog(string msg, string stack, LogType type)
    {
        if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
        try { System.IO.File.AppendAllText("Logs/runtime.txt", type + ": " + msg + "\n" + stack + "\n"); } catch (System.Exception) { }
    }
#endif

    // Agente escolhido, em tamanho real, ao lado do menu.
    void UpdateMenuAgent()
    {
        if (menuAgentIndex != Settings.agent || menuAgent == null)
        {
            if (menuAgent != null) Destroy(menuAgent.gameObject);
            menuAgentIndex = Settings.agent;
            GameObject holder = new GameObject("MenuAgent");
            holder.transform.SetParent(menuCam.transform, false);
            holder.transform.localPosition = new Vector3(1.0f, -1.08f, 2.7f);
            holder.transform.localRotation = Quaternion.Euler(0f, 205f, 0f);
            menuAgent = AgentBody.Create(holder.transform, AgentBody.Defs[Settings.agent % AgentBody.Defs.Length], 0, false);
            menuAgent.transform.SetParent(holder.transform, false);
            Light key = Art.PointLight(holder.transform, new Vector3(0.9f, 1.9f, 1.3f), new Color(1f, 0.93f, 0.85f), 6f, 0.9f);
            key.transform.localPosition = new Vector3(0.9f, 1.9f, 1.3f);
            Light rim = Art.PointLight(holder.transform, new Vector3(-1.2f, 1.6f, -0.9f), AgentBody.Defs[Settings.agent % AgentBody.Defs.Length].accent, 5f, 1.1f);
            rim.transform.localPosition = new Vector3(-1.2f, 1.6f, -0.9f);
        }
        Transform h = menuAgent.transform.parent;
        menuAgent.aimDir = h.TransformDirection(new Vector3(-0.25f, -0.12f, 1f));
        menuAgent.speed = 0f;
    }

    public static void SetupCamera(Camera cam)
    {
        cam.nearClipPlane = 0.03f;
        cam.farClipPlane = 600f;
        cam.backgroundColor = new Color(0.6f, 0.7f, 0.85f);
        Post.SetupCamera(cam);
    }

    void BuildRain()
    {
        GameObject g = new GameObject("Rain");
        g.transform.SetParent(transform, false);
        rain = g.AddComponent<ParticleSystem>();
        ParticleSystem.MainModule main = rain.main;
        main.loop = true;
        main.startLifetime = 0.8f;
        main.startSpeed = 0f;
        main.startSize = 0.028f;
        main.maxParticles = 5000;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startColor = new Color(0.72f, 0.8f, 1f, 0.3f);
        ParticleSystem.EmissionModule em = rain.emission;
        em.rateOverTime = 3600f;
        ParticleSystem.ShapeModule sh = rain.shape;
        sh.shapeType = ParticleSystemShapeType.Box;
        sh.scale = new Vector3(46f, 0.5f, 46f);
        ParticleSystem.VelocityOverLifetimeModule vel = rain.velocityOverLifetime;
        vel.enabled = true;
        vel.space = ParticleSystemSimulationSpace.World;
        vel.x = new ParticleSystem.MinMaxCurve(-2.5f);
        vel.y = new ParticleSystem.MinMaxCurve(-30f);
        vel.z = new ParticleSystem.MinMaxCurve(1f);
        ParticleSystemRenderer pr = g.GetComponent<ParticleSystemRenderer>();
        pr.renderMode = ParticleSystemRenderMode.Stretch;
        pr.lengthScale = 1f;
        pr.velocityScale = 0.05f;
        pr.sharedMaterial = Art.Unlit(Color.white);
        pr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    // ------------------------------- Partidas -------------------------------

    // Troca o mapa (refaz o cenario so quando muda) e o clima de cada um.
    public void LoadMap(int id)
    {
        if (Arena.mapId != id)
        {
            // Desliga na hora (colisores somem ja) e apaga no fim do quadro.
            for (int i = world.childCount - 1; i >= 0; i--) { GameObject old = world.GetChild(i).gameObject; old.SetActive(false); Destroy(old); }
            world.DetachChildren();
            Arena.Build(world, id);
            Post.ProbeSize(new Vector3(Arena.W * Arena.Cell, 40f, Arena.H * Arena.Cell));
        }
        Gate.ResetAll();
        Breakable.ResetAll();
        MapLook();
        fogClock = 0f; fogWarned = -1f;
    }

#if UNITY_EDITOR
    // Ferramenta (so no editor, durante o Play): fotografa os dois mapas do alto para as imagens de preview do menu.
    public System.Collections.IEnumerator CapturePreviews()
    {
        int keep = Arena.mapId < 0 ? 0 : Arena.mapId;
        string[] files = { "mercado", "gelo", "casa" };
        RenderTexture rt = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32);
        MenuBackdrop.Release(menuCam);   // a foto do mapa precisa da camera desenhando tudo
        Transform agent = menuAgent != null ? menuAgent.transform.parent : null;
        if (agent != null) agent.gameObject.SetActive(false);
        string dir = Application.dataPath + "/Resources/MapPreviews";
        System.IO.Directory.CreateDirectory(dir);
        for (int id = 0; id < 3; id++)
        {
            LoadMap(id);
            for (int f = 0; f < 3; f++) yield return null;
            yield return new WaitForSecondsRealtime(0.6f);
            RenderSettings.fogDensity = id == 1 ? 0.006f : 0.003f;
            Vector3 pos = id == 0 ? new Vector3(-46f, 36f, -58f) : (id == 1 ? new Vector3(-44f, 40f, -56f) : new Vector3(-27f, 26f, 30f));
            Camera c = menuCam;
            float fov = c.fieldOfView;
            c.transform.position = pos;
            c.transform.rotation = Quaternion.LookRotation((id == 2 ? new Vector3(2f, 0f, -2f) : new Vector3(3f, 0f, 6f)) - pos);
            c.fieldOfView = 52f;
            RenderTexture old = c.targetTexture;
            c.targetTexture = rt;
            c.Render();
            c.targetTexture = old;
            c.fieldOfView = fov;
            RenderTexture.active = rt;
            Texture2D t = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            t.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
            t.Apply();
            RenderTexture.active = null;
            System.IO.File.WriteAllBytes(dir + "/" + files[id] + ".png", t.EncodeToPNG());
            Destroy(t);
        }
        rt.Release();
        if (agent != null) agent.gameObject.SetActive(true);
        LoadMap(keep);
        Debug.Log("Previews dos mapas salvos em Resources/MapPreviews");
        MenuBackdrop.Invalidate();
    }
#endif

    // MERCADO: tarde quente e seca. NO GELO: ceu encoberto, luz fria, neve caindo e neblina leve.
    void MapLook()
    {
        bool ice = Arena.Ice;
        RenderSettings.fogColor = ice ? new Color(0.78f, 0.84f, 0.9f) : new Color(0.78f, 0.7f, 0.58f);
        RenderSettings.fogDensity = ice ? IceFogBase : 0.0045f;
        if (sunLight != null)
        {
            sunLight.transform.rotation = ice ? Quaternion.Euler(34f, 150f, 0f) : Quaternion.Euler(46f, 38f, 0f);
            sunLight.color = ice ? new Color(0.86f, 0.92f, 1f) : new Color(1f, 0.94f, 0.82f);
            sunLight.intensity = ice ? 1.15f : 1.6f;
        }
        Post.Sky(ice);
        if (rain != null)
        {
            // Mesmo sistema da chuva: no gelo vira neve (flocos grandes caindo devagar e de lado).
            ParticleSystem.MainModule main = rain.main;
            main.startLifetime = ice ? 4.5f : 0.8f;
            main.startSize = ice ? new ParticleSystem.MinMaxCurve(0.04f, 0.09f) : new ParticleSystem.MinMaxCurve(0.028f);
            main.startColor = ice ? new Color(1f, 1f, 1f, 0.85f) : new Color(0.72f, 0.8f, 1f, 0.3f);
            ParticleSystem.EmissionModule em = rain.emission;
            em.rateOverTime = ice ? 900f : 3600f;
            ParticleSystem.VelocityOverLifetimeModule vel = rain.velocityOverLifetime;
            vel.x = ice ? new ParticleSystem.MinMaxCurve(-1.5f, 1.2f) : new ParticleSystem.MinMaxCurve(-2.5f);
            vel.y = ice ? new ParticleSystem.MinMaxCurve(-2.6f, -1.6f) : new ParticleSystem.MinMaxCurve(-30f);
            vel.z = ice ? new ParticleSystem.MinMaxCurve(-0.8f, 1.4f) : new ParticleSystem.MinMaxCurve(1f);
            ParticleSystemRenderer pr = rain.GetComponent<ParticleSystemRenderer>();
            pr.renderMode = ice ? ParticleSystemRenderMode.Billboard : ParticleSystemRenderMode.Stretch;
            rain.Clear();
        }
    }

    // Nevoa do NO GELO: a cada 40 s um banco de neblina passa por ~13 s (sobe, segura e baixa).
    const float IceFogBase = 0.016f, IceFogPeak = 0.075f, FogEvery = 40f;
    void UpdateIceFog()
    {
        if (!Arena.Ice) return;
        bool playing = state == State.Playing || state == State.Tutorial;
        if (playing) fogClock += Time.deltaTime;
        float t = Mathf.Repeat(fogClock, FogEvery) - (FogEvery - 13f);   // os ultimos 13 s de cada ciclo
        float k = 0f;
        if (playing && fogClock > 5f && t > 0f) k = t < 3f ? t / 3f : (t < 9f ? 1f : Mathf.Clamp01((13f - t) / 4f));
        k = Mathf.SmoothStep(0f, 1f, k);
        RenderSettings.fogDensity = Mathf.Lerp(IceFogBase, IceFogPeak, k);
        RenderSettings.fogColor = Color.Lerp(new Color(0.78f, 0.84f, 0.9f), new Color(0.86f, 0.89f, 0.92f), k);
        int cycle = Mathf.FloorToInt(fogClock / FogEvery);
        if (playing && t > -2f && t < 0f && fogWarned != cycle) { fogWarned = cycle; AddFeed("NEVOA SE APROXIMANDO", new Color(0.7f, 0.9f, 1f)); }
    }

    public void StartMatch(bool isTutorial)
    {
        ClearMatch();
        LoadMap(isTutorial ? 0 : Settings.map);
        state = isTutorial ? State.Tutorial : State.Playing;
        scoreLimit = isTutorial ? 9999 : Settings.scoreLimit;
        timeLeft = Settings.minutes * 60f;
        menuCam.gameObject.SetActive(false);

        int mine = isTutorial ? 0 : Settings.ResolveSide();
        player = MakeActor("VOCE", mine, true, false, Settings.agent);
        pc = player.GetComponent<PlayerController>();
        if (!isTutorial)
        {
            int ni = 0;
            // Cada bot e um dos 12 agentes (sem repetir o do jogador enquanto houver outros).
            int n = AgentBody.Defs.Length;
            for (int i = 1; i < Settings.teamSize; i++) { int ag = (Settings.agent + 1 + ni++) % n; MakeActor(AgentBody.Defs[ag].name, mine, false, true, ag); }
            for (int i = 0; i < Settings.teamSize; i++) { int ag = (Settings.agent + 1 + ni++) % n; MakeActor(AgentBody.Defs[ag].name, 1 - mine, false, true, ag); }
            SpawnToys();
            nextCrate = Time.time + 25f;
            if (Settings.mode == 1) { scoreLimit = 9999; bomb = gameObject.AddComponent<BombMode>(); }
            else Banner("MATA-MATA EM EQUIPE  -  PRIMEIRO A " + scoreLimit);
            if (Settings.mode == 0) RaceKeys.Spawn();   // easter egg da CASA (so no mata-mata)
        }
        else tutorial = gameObject.AddComponent<Tutorial>();
        menus.Close();
    }

    // Modo campanha: missao 1 (Mercado). Voce, Kate e Harold contra a MOLOCH, para resgatar a Sankhya.
    public void StartCampaign()
    {
        ClearMatch();
        LoadMap(0);
        state = State.Playing;
        scoreLimit = 9999;
        timeLeft = 99999f;
        menuCam.gameObject.SetActive(false);
        int pa = Settings.agent;
        if (pa == 2 || pa == 3 || pa == 6 || pa == 7) pa = 0;   // esses agentes sao personagens da historia
        player = MakeActor("VOCE", 0, true, false, pa);
        pc = player.GetComponent<PlayerController>();
        nextCrate = float.MaxValue;   // sem caixa laranja na campanha
        campaign = gameObject.AddComponent<Campaign>();
        menus.Close();
    }

    public void FinishCampaign(bool won)
    {
        if (matchOver) return;
        matchOver = true;
        Sfx.UI("ult", 0.8f);
        menus.Open(Menus.Page.End);
    }

    // Um helicoptero por time (na base), duas motos e um jetpack no meio do mapa.
    void SpawnToys()
    {
        if (Arena.Casa) return;   // mapa pequeno: sem veiculos
        if (Arena.Ice) { JetpackPickup.Spawn(Arena.Center(10, 12)); return; }   // mapa fechado: sem helicoptero nem moto
        Helicopter.Spawn(Arena.Center(5, 2), 0f, 0);
        Helicopter.Spawn(Arena.Center(Arena.W - 6, Arena.H - 3), 180f, 1);
        Bike.Spawn(Arena.Center(12, 4), 0f);
        Bike.Spawn(Arena.Center(Arena.W - 13, Arena.H - 5), 180f);
        JetpackPickup.Spawn(Arena.Center(10, 12));
    }

    public void RespawnAll()
    {
        for (int i = 0; i < actors.Count; i++)
        {
            Actor a = actors[i];
            if (a == null || a.creature) continue;
            if (a.vehicle != null) a.vehicle.Exit();
            PlaceAtSpawn(a);
        }
        foreach (Projectile p in All<Projectile>()) Destroy(p.gameObject);
    }

    public void FinishRounds() { EndMatch(); }

    public void Announce(string text, Color c) { AddFeed(text, c); }

    // Celular: tudo o que pesa por pixel ou por quadro e desligado ou reduzido.
#if UNITY_WEBGL && !UNITY_EDITOR
    [System.Runtime.InteropServices.DllImport("__Internal")] static extern void RfsFrameCap(int maxFps);
#endif
    static void MobileProfile()
    {
        // No navegador, -1 usa o requestAnimationFrame da propria tela (60 Hz estavel);
        // um numero fixo faz a Unity usar setTimeout e o quadro fica irregular.
        Application.targetFrameRate = -1;
        QualitySettings.vSyncCount = 0;
#if UNITY_WEBGL && !UNITY_EDITOR
        // Telas de 90/120 Hz: desenha a cada 2 atualizacoes (60 FPS). Em tela de 60 Hz nao muda nada.
        RfsFrameCap(60);
#endif
        Time.fixedDeltaTime = 1f / 30f;
        Post.Lite = true;
        Fx.Scale = 0.35f;
        QualitySettings.pixelLightCount = 1;
        QualitySettings.skinWeights = SkinWeights.TwoBones;
        QualitySettings.lodBias = 0.6f;
        QualitySettings.anisotropicFiltering = AnisotropicFiltering.Disable;
        QualitySettings.globalTextureMipmapLimit = 1;
        QualitySettings.realtimeReflectionProbes = false;
        QualitySettings.particleRaycastBudget = 16;
        var urp = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline as UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset;
        if (urp == null) return;
        urp.renderScale = 0.85f;
        urp.msaaSampleCount = 1;
        urp.shadowDistance = 18f;
        urp.mainLightShadowmapResolution = 1024;   // 4096 e para PC; com 18 m de sombra, 1024 mantem o detalhe
        urp.shadowCascadeCount = 1;
        urp.maxAdditionalLightsCount = 2;
        urp.supportsHDR = false;
        urp.supportsCameraDepthTexture = false;
        urp.supportsCameraOpaqueTexture = false;
        try
        {
            // Oclusao de ambiente (SSAO) e um recurso do renderizador: desliga direto nele.
            var f = typeof(UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset).GetField("m_RendererDataList", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var list = f != null ? f.GetValue(urp) as UnityEngine.Rendering.Universal.ScriptableRendererData[] : null;
            if (list != null)
                foreach (var rd in list)
                    if (rd != null)
                        foreach (var feat in rd.rendererFeatures)
                            if (feat != null) feat.SetActive(false);
        }
        catch (System.Exception) { }
    }

    public void BannerAll(string text) { Banner(text); NetGame.BannerAll(text); }

    // ------------------------------- Online -------------------------------

    // Boneco de um jogador remoto (no host) ou de qualquer outro ator (nos clientes).
    public Actor MakePuppet(string n, int team, int agent, bool human)
    {
        GameObject g = new GameObject("Net_" + n);
        CapsuleCollider col = g.AddComponent<CapsuleCollider>();
        col.height = 1.8f; col.radius = 0.35f; col.center = new Vector3(0f, 0.9f, 0f);
        Actor a = g.AddComponent<Actor>();
        a.Init(n, team, false, agent);
        NetPuppet np = g.AddComponent<NetPuppet>();
        np.human = human;
        actors.Add(a);
        if (human) PlaceAtSpawn(a);
        return a;
    }

    // Entrou em uma sala: comeca a partida online. O dono da sala e o host (simula bots e regras).
    public void StartOnline()
    {
        Net net = Net.I;
        NetPlayer me = net.Me();
        if (me == null) return;
        net.reward = null;
        ClearMatch();
        LoadMap(Arena.MapFromName(net.roomMap));
        bool host = net.IsHost;
        Settings.mode = net.roomMode == "BOMBA" ? 1 : 0;
        state = State.Playing;
        scoreLimit = Settings.mode == 1 ? 9999 : Settings.scoreLimit;
        timeLeft = Settings.minutes * 60f;
        menuCam.gameObject.SetActive(false);
        NetGame.Begin(host);
        player = MakeActor(net.userName, me.team, true, false, Settings.agent);
        pc = player.GetComponent<PlayerController>();
        JetpackPickup.Spawn(Arena.Center(10, 12));
        if (host)
        {
            NetGame.I.RosterChanged();
            nextCrate = Time.time + 25f;
        }
        else nextCrate = float.MaxValue;
        if (Settings.mode == 1) bomb = gameObject.AddComponent<BombMode>();
        else Banner("MATA-MATA 5 x 5  -  PRIMEIRO A " + scoreLimit);
        menus.Close();
    }

    public void EndOnline()
    {
        if (matchOver) return;
        matchOver = true;
        Sfx.UI("ult", 0.8f);
        menus.Open(Menus.Page.End);
    }

    // Troca de dono da sala online.
    public void OnBecameHost() { nextCrate = Time.time + 25f; }
    public void OnBecameClient()
    {
        nextCrate = float.MaxValue;
        for (int i = SupplyCrate.All.Count - 1; i >= 0; i--) if (SupplyCrate.All[i] != null) Destroy(SupplyCrate.All[i].gameObject);
    }

    // Cliente caiu do mapa: volta para a base (o host confirma a posicao no proximo renascimento).
    public void RespawnLocal() { if (player != null) PlaceAtSpawn(player); }

    public void ClearMatch()
    {
        RaceKeys.Clear();
        if (Race.I != null) Destroy(Race.I.gameObject);
        if (NetGame.I != null) { NetGame.I.enabled = false; Destroy(NetGame.I); NetGame.I = null; }
        if (bomb != null) Destroy(bomb);
        bomb = null;
        if (campaign != null) Destroy(campaign);
        campaign = null;
        roundMode = false;
        for (int i = Vehicle.All.Count - 1; i >= 0; i--) if (Vehicle.All[i] != null) Destroy(Vehicle.All[i].gameObject);
        for (int i = SupplyCrate.All.Count - 1; i >= 0; i--) if (SupplyCrate.All[i] != null) Destroy(SupplyCrate.All[i].gameObject);
        foreach (JetpackPickup j in All<JetpackPickup>()) Destroy(j.gameObject);
        foreach (Bullet b in All<Bullet>()) Destroy(b.gameObject);
        for (int i = 0; i < actors.Count; i++)
            if (actors[i] != null) Destroy(actors[i].gameObject);
        actors.Clear();
        player = null;
        pc = null;
        foreach (Projectile p in All<Projectile>()) Destroy(p.gameObject);
        if (tutorial != null) Destroy(tutorial);
        tutorial = null;
        score[0] = 0; score[1] = 0;
        matchOver = false;
        feed.Clear();
        damageFlash = -99f;
        Time.timeScale = 1f;
    }

    public void ToMenu()
    {
        bool wasOnline = NetGame.Online;
        if (Net.I != null && Net.I.InRoom) Net.I.Leave();
        ClearMatch();
        if (wasOnline) { state = State.Menu; menuCam.gameObject.SetActive(true); menus.Open(Menus.Page.Online); return; }
        state = State.Menu;
        menuCam.gameObject.SetActive(true);
        menus.Open(Menus.Page.Main);
    }

    public Actor MakeActor(string n, int team, bool isPlayer, bool ai, int agent = 0)
    {
        GameObject g = new GameObject(isPlayer ? "Player" : "Bot_" + n);
        CharacterController cc = g.AddComponent<CharacterController>();
        cc.height = 1.8f;
        cc.radius = 0.35f;
        cc.center = new Vector3(0f, 0.9f, 0f);
        cc.stepOffset = 0.4f;
        Actor a = g.AddComponent<Actor>();
        a.Init(n, team, isPlayer, agent);
        if (isPlayer) g.AddComponent<PlayerController>();
        else if (ai) g.AddComponent<Bot>();
        actors.Add(a);
        PlaceAtSpawn(a);
        return a;
    }

    // Alvo parado para o tutorial.
    public Actor SpawnDummy(Vector3 pos, float yaw)
    {
        Actor a = MakeActor("ALVO", 1, false, false, (Settings.agent + 3 + actors.Count) % AgentBody.Defs.Length);
        a.noRespawn = true;
        a.Respawn(pos, yaw);
        return a;
    }

    void PlaceAtSpawn(Actor a)
    {
        List<Vector3> list = a.team == 0 ? Arena.spawnsA : Arena.spawnsB;
        Vector3 best = list.Count > 0 ? list[0] : Vector3.zero;
        float bestScore = -1f;
        for (int i = 0; i < list.Count; i++)
        {
            float nearest = 999f;
            for (int j = 0; j < actors.Count; j++)
            {
                Actor o = actors[j];
                if (o == null || o == a || !o.alive) continue;
                float d = Vector3.Distance(o.transform.position, list[i]);
                if (o.team == a.team) d *= 3f; // so evita nascer em cima de aliado
                if (d < nearest) nearest = d;
            }
            nearest += Random.Range(0f, 2f);
            if (nearest > bestScore) { bestScore = nearest; best = list[i]; }
        }
        best += new Vector3(Random.Range(-1.2f, 1.2f), 0f, Random.Range(-1.2f, 1.2f));
        a.Respawn(best, a.team == 0 ? 0f : 180f);
    }

    bool greybox;   // cena de teste Greybox_CombatTest: sem musica, chuva nem logica de partida

    void Update()
    {
        if (greybox) return;
        music.volume = 0.24f * Settings.music;
        // Musica para quando o jogo esta pausado (menu de pausa/opcoes durante a partida).
        bool hold = state != State.Menu && menus != null && menus.IsOpen;
        if (hold && music.isPlaying) music.Pause();
        else if (!hold && !music.isPlaying) music.UnPause();
        rainAudio.volume = Settings.rain && !Arena.Ice ? 0.2f * Settings.sfx : 0f;
        UpdateRain();
        UpdateIceFog();
        Post.Tick();

        if (state == State.Menu)
        {
            // Fundo cinematografico: a cidade e fotografada uma vez; por quadro so o agente, a poeira e a fumaca.
            if (!menuCam.enabled) menuCam.enabled = true;
#if UNITY_EDITOR
            if (Input.GetKeyDown(KeyCode.F7)) TouchPad.On = !TouchPad.On;   // editor: ver o menu no layout de celular
#endif
            UpdateMenuAgent();
            MenuBackdrop.Tick(menuCam, menuAgent != null ? menuAgent.transform.parent : null);
            return;
        }
        if (matchOver) return;

#if UNITY_EDITOR
        if (Arena.Casa && player != null && !menus.IsOpen && Input.GetKeyDown(KeyCode.F8)) Arena.CasaTour(player);
#endif
        if (NetGame.IsClient) { while (feed.Count > 0 && Time.time - feed[0].time > 6f) feed.RemoveAt(0); return; }
        if (state == State.Playing && !roundMode)
        {
            timeLeft -= Time.deltaTime;
            if (timeLeft <= 0f) { timeLeft = 0f; EndMatch(); return; }
        }
        // Caixa laranja: de tempos em tempos cai uma em um ponto aleatorio (no maximo duas no mapa).
        if (state == State.Playing && Time.time >= nextCrate && !Arena.Ice && !Arena.Casa)
        {
            nextCrate = Time.time + 50f;
            if (SupplyCrate.All.Count < 2)
            {
                SupplyCrate.Drop(Arena.RandomPoint());
                AddFeed("CAIXA LARANJA A CAMINHO", Art.Orange);
            }
        }

        for (int i = actors.Count - 1; i >= 0; i--)
        {
            Actor a = actors[i];
            if (a == null) { actors.RemoveAt(i); continue; }
            if (a.alive || Time.time < a.respawnAt) continue;
            if (roundMode && !a.noRespawn) continue;
            if (a.noRespawn)
            {
                actors.RemoveAt(i);
                Destroy(a.gameObject);
            }
            else PlaceAtSpawn(a);
        }
        while (feed.Count > 0 && Time.time - feed[0].time > 6f) feed.RemoveAt(0);
    }

    void UpdateRain()
    {
        if (rain == null) return;
        Camera cam = (pc != null && pc.cam != null) ? pc.cam : menuCam;
        bool on = Settings.rain && cam != null;
        ParticleSystem.EmissionModule em = rain.emission;
        if (em.enabled != on) em.enabled = on;
        if (cam != null) rain.transform.position = cam.transform.position + Vector3.up * 13f + cam.transform.forward * 8f;
    }

    public void OnKill(Actor killer, Actor victim, bool head, string weapon)
    {
        bool suicide = killer == null || killer == victim;
        if (state == State.Tutorial)
        {
            if (!suicide && killer.isPlayer)
            {
                killer.kills++;
                if (pc != null && pc.ads) pc.stats.adsKills++;
                Sfx.UI("kill", 0.8f);
            }
            return;
        }
        if (!suicide)
        {
            if (head) killer.headshots++;
            if (victim.creature || victim.metal) killer.bigKills++;
        }
        if (victim.creature || roundMode)
        {
            // Dinossauros e rodadas do modo bomba nao mexem no placar de abates.
            if (!suicide)
            {
                killer.kills++;
                AddFeed(killer.displayName + "  [" + weapon + (head ? " *" : "") + "]  " + victim.displayName, Art.TeamColor(killer.team));
                if (killer.isPlayer) { if (pc != null) pc.AddUlt(34f); Sfx.UI("kill", 0.8f); Banner("ELIMINADO: " + victim.displayName); }
            }
            return;
        }
        if (suicide)
        {
            score[victim.team] = Mathf.Max(0, score[victim.team] - 1);
            AddFeed(victim.displayName + " se eliminou", Art.TeamColor(victim.team));
        }
        else
        {
            killer.kills++;
            score[killer.team]++;
            AddFeed(killer.displayName + "  [" + weapon + (head ? " *" : "") + "]  " + victim.displayName, Art.TeamColor(killer.team));
            if (killer.isPlayer)
            {
                if (pc != null) pc.AddUlt(34f);
                Sfx.UI("kill", 0.8f);
                Banner(head ? "TIRO NA CABECA" : "ELIMINADO: " + victim.displayName);
            }
        }
        if (score[0] >= scoreLimit || score[1] >= scoreLimit) EndMatch();
    }

    void EndMatch()
    {
        if (matchOver) return;
        matchOver = true;
        NetGame.Ended();
        Sfx.UI("ult", 0.8f);
        menus.Open(Menus.Page.End);
    }

    public void FinishTutorial()
    {
        if (matchOver) return;
        matchOver = true;
        Sfx.UI("ult", 0.8f);
        menus.Open(Menus.Page.End);
    }

    void AddFeed(string text, Color c)
    {
        Feed f = new Feed();
        f.text = text; f.color = c; f.time = Time.time;
        feed.Add(f);
        NetGame.Feed(text, c);
        if (feed.Count > 6) feed.RemoveAt(0);
    }

    public void Hitmarker(bool head, bool kill)
    {
        hitTime = Time.time;
        hitHead = head;
        hitKill = kill;
        Sfx.UI(head ? "headshot" : "hit", 0.6f);
    }

    public void Banner(string text) { banner = text; bannerTime = Time.time; }

    // ------------------------------- Desenho (IMGUI) -------------------------------

    // Os menus trocam a fonte e tiram a sombra enquanto desenham (identidade nova); o HUD continua igual.
    public static Font textFont;
    public static bool textShadow = true;

    public static void EnsureStyle()
    {
        if (style != null) return;
        style = new GUIStyle(GUI.skin.label);
        style.fontStyle = FontStyle.Bold;
        style.wordWrap = false;
        style.richText = false;
    }

    public static void Fill(float x, float y, float w, float h, Color c)
    {
        GUI.color = textFont != null ? UI.Fix(c) : c;   // nos menus: cor certa no navegador
        GUI.DrawTexture(new Rect(x, y, w, h), Texture2D.whiteTexture);
        GUI.color = Color.white;
    }

    public static void Text(float x, float y, float w, float h, string s, int size, Color c, TextAnchor anchor)
    {
        EnsureStyle();
        if (textFont != null) c = UI.Fix(c);
        style.font = textFont;
        style.fontStyle = textFont == null ? FontStyle.Bold : FontStyle.Normal;
        style.fontSize = size;
        style.alignment = anchor;
        if (textShadow)
        {
            style.normal.textColor = new Color(0f, 0f, 0f, c.a * 0.8f);
            GUI.Label(new Rect(x + 2f, y + 2f, w, h), s, style);
        }
        style.normal.textColor = c;
        GUI.Label(new Rect(x, y, w, h), s, style);
    }

    static void Bar(float x, float y, float w, float h, float k, Color c)
    {
        Fill(x, y, w, h, new Color(0f, 0f, 0f, 0.55f));
        Fill(x + 2f, y + 2f, (w - 4f) * Mathf.Clamp01(k), h - 4f, c);
    }

    void OnGUI()
    {
        if (state == State.Menu || player == null || pc == null || pc.cam == null) return;
        if (Race.Active) return;   // na corrida da montanha a tela e da corrida
        EnsureStyle();
        float s = Screen.height / 1080f;
        GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));
        float vw = Screen.width / s, vh = 1080f;
        float cx = vw / 2f, cy = vh / 2f;
        WeaponDef w = Weapons.All[pc.cur];
        Color tc = Art.TeamColor(player.team);
        bool tut = state == State.Tutorial;

        // Luneta do fuzil de precisao
        bool scoped = player.alive && pc.ads && pc.cur == 2 && pc.cam.fieldOfView < 40f;
        if (scoped)
        {
            float r = vh * 0.42f;
            Color blk = new Color(0f, 0f, 0f, 0.92f);
            Fill(0f, 0f, cx - r, vh, blk);
            Fill(cx + r, 0f, vw - cx - r, vh, blk);
            Fill(cx - r, 0f, r * 2f, cy - r, blk);
            Fill(cx - r, cy + r, r * 2f, vh - cy - r, blk);
            // Reticulo fino com marcas de queda da bala (cada marca ~ +100 m).
            Color rc = new Color(0f, 0f, 0f, 0.9f);
            Fill(cx - r, cy - 0.75f, r * 2f, 1.5f, rc);
            Fill(cx - 0.75f, cy - r, 1.5f, r * 2f, rc);
            for (int i = 1; i <= 4; i++) Fill(cx - 14f + i * 2f, cy + i * 26f, 28f - i * 4f, 1.5f, rc);
            Fill(cx - r, cy - 3f, r * 0.55f, 6f, rc); Fill(cx + r * 0.45f, cy - 3f, r * 0.55f, 6f, rc); Fill(cx - 3f, cy + r * 0.45f, 6f, r * 0.55f, rc);
            if (GameInput.Sprint) Text(cx - 200f, cy + r - 60f, 400f, 26f, "PRENDENDO O FOLEGO", 16, Color.white, TextAnchor.MiddleCenter);
            else Text(cx - 200f, cy + r - 60f, 400f, 26f, "SHIFT  -  PRENDER O FOLEGO", 14, new Color(1f, 1f, 1f, 0.6f), TextAnchor.MiddleCenter);
        }

        // Dano levado e vida baixa: brilho vermelho nas bordas (mais forte do lado de onde veio o tiro).
        float lowHp = player.maxHealth * 0.25f;
        float low = player.alive ? Mathf.Clamp01((lowHp - player.health) / lowHp) : 0f;
        Fx.DrawScreenBlood(vw, vh, low);
        if (player.blindUntil > Time.time)
        {
            float k = Mathf.Clamp01((player.blindUntil - Time.time) / 1.2f);
            Fill(0f, 0f, vw, vh, new Color(1f, 1f, 1f, 0.9f * k));
        }
        if (player.invulnerableUntil > Time.time)
        {
            float a = 0.12f + 0.08f * Mathf.Sin(Time.time * 11f);
            Fill(0f, 0f, vw, vh, new Color(0.5f, 0.9f, 1f, a));
            Text(cx - 200f, 170f, 400f, 36f, "INTANGIVEL", 24, Art.Cyan, TextAnchor.MiddleCenter);
        }
        if (pc.Overcharged)
        {
            Color oc = new Color(1f, 0.9f, 0.2f, 0.25f);
            Fill(0f, 0f, vw, 14f, oc); Fill(0f, vh - 14f, vw, 14f, oc);
            Text(cx - 300f, 170f, 600f, 40f, "SOBRECARGA  " + Mathf.CeilToInt(pc.overUntil - Time.time) + "s", 28, Art.Yellow, TextAnchor.MiddleCenter);
        }

        // Mira tatica (Crosshair reformulado + Hitmarker estilo Call of Duty)
        if (player.alive && !matchOver && !scoped && !(player.vehicle is Bike))
        {
            if (!(pc.ads && pc.cur != 1))
            {
                if (Aim.chOn) Aim.Draw(cx, cy, pc.curSpread);   // mira personalizada (OPCOES > MIRA)
                else
                {
                    if (pc.isSprinting)
                    {
                        // Em sprint: apenas ponto discreto central (estilo CoD)
                        Fill(cx - 2f, cy - 2f, 4f, 4f, new Color(0f, 0f, 0f, 0.7f));
                        Fill(cx - 1f, cy - 1f, 2f, 2f, new Color(1f, 1f, 1f, 0.75f));
                    }
                    else
                    {
                        float gap = 5f + pc.curSpread * 8f;
                        float len = 9f;
                        float th = 2f;
                        Color cc = new Color(1f, 1f, 1f, 0.94f);
                        Color ol = new Color(0f, 0f, 0f, 0.75f);
                        // Contorno escuro 1px
                        Fill(cx - gap - len - 1f, cy - th * 0.5f - 1f, len + 2f, th + 2f, ol);
                        Fill(cx + gap - 1f, cy - th * 0.5f - 1f, len + 2f, th + 2f, ol);
                        Fill(cx - th * 0.5f - 1f, cy - gap - len - 1f, th + 2f, len + 2f, ol);
                        Fill(cx - th * 0.5f - 1f, cy + gap - 1f, th + 2f, len + 2f, ol);
                        Fill(cx - 2f, cy - 2f, 4f, 4f, ol);
                        // Bracos nítidos + ponto central
                        Fill(cx - gap - len, cy - th * 0.5f, len, th, cc);
                        Fill(cx + gap, cy - th * 0.5f, len, th, cc);
                        Fill(cx - th * 0.5f, cy - gap - len, th, len, cc);
                        Fill(cx - th * 0.5f, cy + gap, th, len, cc);
                        Fill(cx - 1f, cy - 1f, 2f, 2f, cc);
                    }
                }
            }
            // Mirando (ADS): ponto vermelho limpo com contorno sutil
            if (pc.ads && (pc.cur == 0 || pc.cur == 3 || pc.cur == 4))
            {
                Fill(cx - 3.5f, cy - 3.5f, 7f, 7f, new Color(0f, 0f, 0f, 0.55f));
                Fill(cx - 2.5f, cy - 2.5f, 5f, 5f, new Color(1f, 0.14f, 0.12f, 0.96f));
                Fill(cx - 1f, cy - 1f, 2f, 2f, new Color(1f, 0.75f, 0.75f, 0.95f));
            }
        }
        float ht = Time.time - hitTime;
        if (ht < 0.20f)
        {
            float tNorm = ht / 0.20f;
            float alpha = 1f - tNorm * tNorm;
            float pop = hitHead ? Mathf.Lerp(1.35f, 1.05f, tNorm) : Mathf.Lerp(1.15f, 1.0f, tNorm);
            Color hc = hitHead ? new Color(1f, 0.18f, 0.18f, alpha) : new Color(1f, 1f, 1f, alpha);
            Color ho = new Color(0f, 0f, 0f, alpha * 0.7f);
            float gap = 5.5f * pop, len = 9.5f * pop, th = 2.2f;

            Matrix4x4 prevMat = GUI.matrix;
            GUIUtility.RotateAroundPivot(45f, new Vector2(cx, cy));
            // X principal estilo Call of Duty
            Fill(cx - gap - len - 1f, cy - th * 0.5f - 1f, len + 2f, th + 2f, ho);
            Fill(cx + gap - 1f, cy - th * 0.5f - 1f, len + 2f, th + 2f, ho);
            Fill(cx - th * 0.5f - 1f, cy - gap - len - 1f, th + 2f, len + 2f, ho);
            Fill(cx - th * 0.5f - 1f, cy + gap - 1f, th + 2f, len + 2f, ho);

            Fill(cx - gap - len, cy - th * 0.5f, len, th, hc);
            Fill(cx + gap, cy - th * 0.5f, len, th, hc);
            Fill(cx - th * 0.5f, cy - gap - len, th, len, hc);
            Fill(cx - th * 0.5f, cy + gap, th, len, hc);

            // Headshot: segundo X externo sutil estilo CoD
            if (hitHead)
            {
                float g2 = gap + len + 2.5f, l2 = 4.5f;
                Fill(cx - g2 - l2, cy - th * 0.5f, l2, th, hc);
                Fill(cx + g2, cy - th * 0.5f, l2, th, hc);
                Fill(cx - th * 0.5f, cy - g2 - l2, th, l2, hc);
                Fill(cx - th * 0.5f, cy + g2, th, l2, hc);
            }
            GUI.matrix = prevMat;
        }

        // Placar e tempo (ou o passo do tutorial)
        if (!tut && campaign == null)
        {
            Fill(cx - 250f, 18f, 500f, 64f, new Color(0f, 0f, 0f, 0.55f));
            Fill(cx - 250f, 80f, 250f, 2f, Art.Ball); Fill(cx, 80f, 250f, 2f, Art.Moloch);
            Text(cx - 240f, 18f, 150f, 64f, roundMode ? "BAAL (ATQ)" : "BAAL", 22, Art.Ball, TextAnchor.MiddleLeft);
            Text(cx - 130f, 18f, 80f, 64f, score[0].ToString(), 40, Art.Ball, TextAnchor.MiddleCenter);
            Text(cx + 50f, 18f, 80f, 64f, score[1].ToString(), 40, Art.Moloch, TextAnchor.MiddleCenter);
            Text(cx + 90f, 18f, 150f, 64f, roundMode ? "MOLOCH (DEF)" : "MOLOCH", 22, Art.Moloch, TextAnchor.MiddleRight);
            int secs = Mathf.CeilToInt(timeLeft);
            Text(cx - 50f, 18f, 100f, 64f, (secs / 60) + ":" + (secs % 60).ToString("00"), 26, Color.white, TextAnchor.MiddleCenter);
        }
        else if (tutorial != null && !matchOver)
        {
            Fill(cx - 430f, 24f, 860f, 128f, new Color(0f, 0f, 0f, 0.72f));
            Fill(cx - 430f, 24f, 6f, 128f, tutorial.StepDone ? Art.Green : Art.Yellow);
            Text(cx - 406f, 30f, 600f, 34f, tutorial.Header, 20, Art.Yellow, TextAnchor.MiddleLeft);
            Text(cx - 406f, 62f, 820f, 44f, tutorial.Title, 30, Color.white, TextAnchor.MiddleLeft);
            Text(cx - 406f, 104f, 820f, 36f, tutorial.StepDone ? "FEITO!" : tutorial.Body, 20, tutorial.StepDone ? Art.Green : new Color(1f, 1f, 1f, 0.85f), TextAnchor.MiddleLeft);
        }

        // Minimapa
        if (Arena.minimap != null)
        {
            float mx = 24f, my = 24f, ms = 230f;
            GUI.DrawTexture(new Rect(mx, my, ms, ms), Arena.minimap);
            for (int i = 0; i < actors.Count; i++)
            {
                Actor a = actors[i];
                if (a == null || !a.alive) continue;
                bool show = a.team == player.team || Time.time - a.lastShotTime < 2f || a.revealedUntil > Time.time || tut;
                if (!show) continue;
                float u = (a.transform.position.x / Arena.Cell + (Arena.W - 1) / 2f + 0.5f) / Arena.W;
                float v = (a.transform.position.z / Arena.Cell + (Arena.H - 1) / 2f + 0.5f) / Arena.H;
                float px = mx + u * ms, py = my + (1f - v) * ms;
                float ds = a.isPlayer ? 9f : 7f;
                Fill(px - ds / 2f, py - ds / 2f, ds, ds, a.isPlayer ? Color.white : Art.TeamColor(a.team));
                if (a.isPlayer)
                {
                    Vector3 f = a.transform.forward;
                    Fill(px + f.x * 9f - 2f, py - f.z * 9f - 2f, 4f, 4f, Color.white);
                }
            }
        }

        // Historico de abates
        for (int i = 0; i < feed.Count; i++)
        {
            Color fc = feed[i].color;
            fc.a = Mathf.Clamp01(6f - (Time.time - feed[i].time));
            Text(vw - 620f, 24f + i * 30f, 600f, 30f, feed[i].text, 20, fc, TextAnchor.MiddleRight);
        }

        // Vida
        Bar(40f, vh - 78f, 320f, 26f, player.health / player.maxHealth, player.health > player.maxHealth * 0.25f ? tc : new Color(1f, 0.2f, 0.25f));
        Text(40f, vh - 132f, 320f, 50f, Mathf.CeilToInt(Mathf.Max(0f, player.health)).ToString(), 44, Color.white, TextAnchor.MiddleLeft);

        if (player.vehicle == null)
        {
        // Arma e municao
        if (!TouchPad.On)   // no celular a municao aparece na barra de armas (os botoes ocupam este canto)
        {
            Text(vw - 440f, vh - 150f, 400f, 30f, (pc.cur + 1) + "  " + w.name, 22, w.color, TextAnchor.MiddleRight);
            string ammo = pc.Overcharged ? "--" : pc.mag[pc.cur].ToString();
            Text(vw - 440f, vh - 122f, 320f, 64f, ammo, 56, Color.white, TextAnchor.MiddleRight);
            Text(vw - 116f, vh - 104f, 80f, 40f, "/ " + pc.reserve[pc.cur], 24, new Color(1f, 1f, 1f, 0.7f), TextAnchor.MiddleLeft);
        }
        if (pc.reloading)
        {
            Bar(cx - 100f, cy + 70f, 200f, 12f, (Time.time - pc.reloadStart) / Mathf.Max(0.01f, pc.reloadEnd - pc.reloadStart), Color.white);
            Text(cx - 150f, cy + 84f, 300f, 28f, "RECARREGANDO", 18, Color.white, TextAnchor.MiddleCenter);
        }
        else if (player.alive && pc.mag[pc.cur] == 0 && !pc.Overcharged)
            Text(cx - 200f, cy + 70f, 400f, 28f, pc.reserve[pc.cur] > 0 ? "R  -  RECARREGAR" : "SEM MUNICAO", 18, Art.Orange, TextAnchor.MiddleCenter);

        // Habilidades (no celular a recarga ja aparece nos proprios botoes E, Q e X)
        if (!TouchPad.On)
        {
            // Modo tatico: sem dash (E) e sem utilidade secundaria (C).
            bool tac = PlayerController.TacticalMode;
            bool hasDash = !tac;
            bool hasSecondary = !tac && AgentAbilities.HasSecondarySkill(player.agent);
            float abilityCount = 2f + (hasDash ? 1f : 0f) + (hasSecondary ? 1f : 0f);
            float ax = cx - (abilityCount * 120f + (abilityCount - 1f) * 15f) * 0.5f, ay = vh - 92f;
            if (hasDash) { Ability(ax, ay, "E", "DASH", 1f - Mathf.Clamp01((pc.dashReady - Time.time) / PlayerController.DashCooldown), Art.Cyan); ax += 135f; }
            AgentDef agentDef = AgentBody.Defs[Mathf.Abs(player.agent) % AgentBody.Defs.Length];
            Ability(ax, ay, "Q", AgentAbilities.HasCustomSkill(player.agent) ? "PODER" : "GRANADA", 1f - Mathf.Clamp01((pc.nadeReady - Time.time) / PlayerController.NadeCooldown), Art.Green); ax += 135f;
            if (hasSecondary) { Ability(ax, ay, "C", "UTILIDADE", 1f - Mathf.Clamp01((pc.secondaryReady - Time.time) / PlayerController.SecondaryCooldown), Art.Orange); ax += 135f; }
            Ability(ax, ay, "X", "ULTIMATE", pc.ult / 100f, Art.Yellow);
            if (AgentAbilities.HasCustomSkill(player.agent)) Text(40f, vh - 190f, 760f, 34f, agentDef.skill + (hasSecondary && agentDef.skill2.Length > 0 ? "  /  C: " + agentDef.skill2 : "") + "  /  X: " + agentDef.ultimate, 16, agentDef.accent, TextAnchor.MiddleLeft);
        }
        }

        // Marcadores dos aliados
        if (player.alive)
        {
            for (int i = 0; i < actors.Count; i++)
            {
                Actor a = actors[i];
                if (a == null || a == player || !a.alive || a.team != player.team) continue;
                Vector3 sp = pc.cam.WorldToScreenPoint(a.transform.position + Vector3.up * 2.15f);
                if (sp.z <= 0f) continue;
                float x = sp.x / s, y = (Screen.height - sp.y) / s;
                Text(x - 80f, y - 22f, 160f, 22f, a.displayName, 15, new Color(tc.r, tc.g, tc.b, 0.85f), TextAnchor.MiddleCenter);
            }
        }

        // Tecla F, jetpack e caixas no mapa
        if (player.alive && !string.IsNullOrEmpty(pc.prompt))
        {
            Fill(cx - 330f, cy + 120f, 660f, 40f, new Color(0f, 0f, 0f, 0.6f));
            Text(cx - 330f, cy + 120f, 660f, 40f, pc.prompt, 20, Art.Yellow, TextAnchor.MiddleCenter);
        }
        if (pc.jetFuel > 0f)
        {
            Text(40f, vh - 196f, 200f, 24f, "JETPACK", 16, Art.Orange, TextAnchor.MiddleLeft);
            Bar(40f, vh - 170f, 200f, 16f, pc.jetFuel / 100f, Art.Orange);
        }
        for (int i = 0; i < SupplyCrate.All.Count; i++)
        {
            SupplyCrate sc = SupplyCrate.All[i];
            if (sc == null) continue;
            Vector3 sp = pc.cam.WorldToScreenPoint(sc.transform.position + Vector3.up * 2f);
            if (sp.z <= 0f) continue;
            float x = sp.x / s, y = (Screen.height - sp.y) / s;
            Fill(x - 9f, y - 9f, 18f, 18f, Art.Orange);
            Text(x - 80f, y + 10f, 160f, 22f, "CAIXA  " + Mathf.RoundToInt(Vector3.Distance(pc.cam.transform.position, sc.transform.position)) + " m", 14, Art.Orange, TextAnchor.MiddleCenter);
        }

        // Avisos
        float bt = Time.time - bannerTime;
        if (bt < 2.5f && !matchOver)
        {
            Color bc = Color.white;
            bc.a = Mathf.Clamp01(2.5f - bt);
            Text(cx - 500f, 230f, 1000f, 44f, banner, 30, bc, TextAnchor.MiddleCenter);
        }

        if (!player.alive && !matchOver && roundMode && pc != null && pc.spectating && Time.time - player.respawnAt > -1f)
        {
            // Espectador: quem esta sendo assistido e como trocar.
            Actor st = pc.specTarget;
            string who = st != null ? "ASSISTINDO:  " + st.displayName + "   " + Mathf.CeilToInt(Mathf.Max(0f, st.health)) + " HP" : "CAMERA LIVRE";
            Color wc = st != null ? Art.TeamColor(st.team) : Color.white;
            Fill(cx - 330f, vh - 210f, 660f, 44f, new Color(0f, 0f, 0f, 0.55f));
            Fill(cx - 330f, vh - 210f, 660f, 3f, wc);
            Text(cx - 330f, vh - 210f, 660f, 44f, who, 24, wc, TextAnchor.MiddleCenter);
            string hint = TouchPad.On ? "TIRO: proximo jogador     MIRA: camera livre" : "CLIQUE: proximo jogador     BOTAO DIREITO: camera livre     WASD / ESPACO / CTRL: andar";
            Text(cx - 500f, vh - 162f, 1000f, 26f, hint, 16, new Color(1f, 1f, 1f, 0.75f), TextAnchor.MiddleCenter);
            Text(cx - 500f, 92f, 1000f, 26f, "VOCE FOI ELIMINADO", 20, new Color(1f, 0.35f, 0.4f), TextAnchor.MiddleCenter);
        }
        else if (!player.alive && !matchOver)
        {
            Fill(0f, 0f, vw, vh, new Color(0.1f, 0f, 0.05f, 0.45f));
            Text(cx - 500f, cy - 80f, 1000f, 70f, "VOCE FOI ELIMINADO", 54, new Color(1f, 0.25f, 0.35f), TextAnchor.MiddleCenter);
            if (!roundMode) Text(cx - 500f, cy, 1000f, 40f, "Renascendo em " + Mathf.Max(0, Mathf.CeilToInt(player.respawnAt - Time.time)) + "...", 26, Color.white, TextAnchor.MiddleCenter);
        }

        if (NetGame.Online && Net.I != null && Net.I.InRoom)
            Text(vw - 420f, vh - 36f, 400f, 26f, "SALA " + Net.I.roomCode + (NetGame.IsHost ? "  (voce e o host)" : ""), 15, new Color(1f, 1f, 1f, 0.55f), TextAnchor.MiddleRight);
        if (!tut && GameInput.Scoreboard && !menus.IsOpen) Scoreboard(cx, cy);
    }

    void Ability(float x, float y, string key, string label, float k, Color c)
    {
        bool ready = k >= 0.999f;
        Fill(x, y, 120f, 54f, new Color(0f, 0f, 0f, 0.55f));
        Fill(x, y + 50f, 120f * Mathf.Clamp01(k), 4f, ready ? c : new Color(c.r, c.g, c.b, 0.5f));
        Text(x + 8f, y, 30f, 50f, key, 26, ready ? c : new Color(1f, 1f, 1f, 0.4f), TextAnchor.MiddleLeft);
        Text(x + 36f, y, 82f, 50f, label, 13, ready ? Color.white : new Color(1f, 1f, 1f, 0.4f), TextAnchor.MiddleLeft);
    }

    public void Scoreboard(float cx, float cy)
    {
        float w = 760f, h = 90f + actors.Count * 34f + 40f;
        float x = cx - w / 2f, y = cy - h / 2f + 20f;
        Fill(x, y, w, h, new Color(0.02f, 0.02f, 0.06f, 0.88f));
        Text(x + 30f, y + 10f, 300f, 36f, "JOGADOR", 18, new Color(1f, 1f, 1f, 0.6f), TextAnchor.MiddleLeft);
        Text(x + w - 260f, y + 10f, 100f, 36f, "ABATES", 18, new Color(1f, 1f, 1f, 0.6f), TextAnchor.MiddleCenter);
        Text(x + w - 140f, y + 10f, 100f, 36f, "MORTES", 18, new Color(1f, 1f, 1f, 0.6f), TextAnchor.MiddleCenter);
        float ry = y + 52f;
        for (int team = 0; team < 2; team++)
        {
            Color c = Art.TeamColor(team);
            Fill(x + 20f, ry, w - 40f, 2f, c);
            ry += 8f;
            for (int i = 0; i < actors.Count; i++)
            {
                Actor a = actors[i];
                if (a == null || a.team != team) continue;
                if (a.isPlayer) Fill(x + 20f, ry, w - 40f, 32f, new Color(1f, 1f, 1f, 0.08f));
                int lv = NetGame.LevelOf(a);
                if (lv > 0) Text(x + 30f, ry, 60f, 32f, "NV " + lv, 14, new Color(1f, 0.86f, 0.1f, 0.85f), TextAnchor.MiddleLeft);
                Text(x + (lv > 0 ? 86f : 30f), ry, 300f, 32f, a.displayName, 22, a.alive ? c : new Color(c.r, c.g, c.b, 0.45f), TextAnchor.MiddleLeft);
                Text(x + w - 260f, ry, 100f, 32f, a.kills.ToString(), 22, Color.white, TextAnchor.MiddleCenter);
                Text(x + w - 140f, ry, 100f, 32f, a.deaths.ToString(), 22, Color.white, TextAnchor.MiddleCenter);
                ry += 34f;
            }
            ry += 14f;
        }
    }
}
