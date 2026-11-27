using UnityEditor;
using UnityEngine;

// NeonArena > Gerar previews dos mapas: com o jogo em Play (no menu), fotografa MERCADO e NO GELO
// e salva em Assets/Resources/MapPreviews (imagens mostradas ao lado da escolha de mapa).
public static class MapPreviewMenu
{
    [MenuItem("NeonArena/Gerar previews dos mapas (em Play)")]
    static void Capture()
    {
        if (!Application.isPlaying || Game.I == null) { Debug.LogWarning("Entre no Play (menu principal) e rode de novo."); return; }
        Game.I.StartCoroutine(Game.I.CapturePreviews());
    }

    [MenuItem("NeonArena/Gerar previews dos mapas (em Play)", true)]
    static bool Valid() { return Application.isPlaying; }

    // Teste: solta um MECA do seu time 8 m a sua frente (para ver o modelo de perto).
    [MenuItem("NeonArena/Soltar MECA na frente (em Play)")]
    static void SpawnMech()
    {
        if (!Application.isPlaying || Game.I == null || Game.I.player == null) { Debug.LogWarning("Entre numa partida primeiro."); return; }
        Transform p = Game.I.player.transform;
        Vector3 at = p.position + p.forward * 8f;
        Mech.Spawn(at, Game.I.player.team, false).transform.rotation = Quaternion.LookRotation(-p.forward);
    }

    // Teste: fotografa o MECA parado (3 angulos) em Logs/mech_N.png, longe do mapa.
    [MenuItem("NeonArena/Fotografar MECA (em Play)")]
    static void ShootMech()
    {
        if (!Application.isPlaying || Game.I == null) { Debug.LogWarning("Entre no Play primeiro."); return; }
        Vector3 at = new Vector3(0f, 300f, 0f);
        GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        floor.transform.position = at + Vector3.down * 0.05f;
        floor.transform.localScale = new Vector3(30f, 0.1f, 30f);
        floor.GetComponent<Renderer>().sharedMaterial = Art.Mat(new Color(0.35f, 0.36f, 0.38f), Color.black, 0f, default(Color), 0.3f, 0f);
        Mech m = Mech.Spawn(at, 0, false);
        m.enabled = false;
        foreach (Transform t in m.GetComponentsInChildren<Transform>(true)) if (t.name == "flame") t.gameObject.SetActive(false);
        foreach (Light l in m.GetComponentsInChildren<Light>(true)) l.enabled = false;
        GameObject cg = new GameObject("shotCam");
        Camera cam = cg.AddComponent<Camera>();
        Game.SetupCamera(cam);
        cam.fieldOfView = 40f;
        RenderTexture rt = new RenderTexture(1024, 1024, 24, RenderTextureFormat.ARGB32);
        Vector3[] views = { new Vector3(4.5f, 2.6f, 6.5f), new Vector3(-6.5f, 2.2f, 4f), new Vector3(3f, 3.2f, -7f) };
        for (int i = 0; i < views.Length; i++)
        {
            cam.transform.position = at + views[i];
            cam.transform.rotation = Quaternion.LookRotation(at + Vector3.up * 1.9f - cam.transform.position);
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            Texture2D tex = new Texture2D(1024, 1024, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 1024, 1024), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            System.IO.File.WriteAllBytes(Application.dataPath + "/../Logs/mech_" + i + ".png", tex.EncodeToPNG());
            Object.Destroy(tex);
        }
        cam.targetTexture = null;
        rt.Release();
        Object.Destroy(cg);
        Object.Destroy(m.gameObject);
        Object.Destroy(floor);
        Debug.Log("Fotos do MECA em Logs/mech_0..2.png");
    }
}
