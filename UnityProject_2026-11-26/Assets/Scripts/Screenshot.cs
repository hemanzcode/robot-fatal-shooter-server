using System;
using System.Collections;
using System.IO;
using UnityEngine;

// Foto da tela: tecla P (no celular, botao FOTO no topo). Salva em Downloads:
// - Windows/editor: pasta Downloads do usuario;
// - navegador (PC e celular): baixa o arquivo PNG (vai para Downloads do aparelho).
public class Screenshot : MonoBehaviour
{
    static Screenshot inst;
    float flashAt = -99f;
    string msg = "";
    bool busy;

#if UNITY_WEBGL && !UNITY_EDITOR
    [System.Runtime.InteropServices.DllImport("__Internal")] static extern void RfsSaveFile(byte[] data, int len, string name, string mime);
#endif

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (inst != null) return;
        GameObject g = new GameObject("Screenshot");
        DontDestroyOnLoad(g);
        inst = g.AddComponent<Screenshot>();
    }

    public static void Take() { if (inst != null && !inst.busy) inst.StartCoroutine(inst.Capture()); }

    void Update()
    {
        // Nao dispara enquanto digita (login, codigo da sala).
        bool typing = GUIUtility.keyboardControl != 0;
        bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
        if (!typing && !ctrl && Input.GetKeyDown(KeyCode.P)) Take();   // Ctrl+P e atalho do editor, nao foto
        if (TouchPad.ShotDown) { TouchPad.ShotDown = false; Take(); }
    }

    IEnumerator Capture()
    {
        busy = true;
        yield return new WaitForEndOfFrame();
        Texture2D tex = ScreenCapture.CaptureScreenshotAsTexture();
        byte[] png = tex.EncodeToPNG();
        Destroy(tex);
        string name = "FatalShooter_" + DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss") + ".png";
        try
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            RfsSaveFile(png, png.Length, name, "image/png");
            msg = "FOTO SALVA EM DOWNLOADS";
#else
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            if (!Directory.Exists(dir)) dir = Application.persistentDataPath;
            File.WriteAllBytes(Path.Combine(dir, name), png);
            msg = "FOTO SALVA EM " + (dir.EndsWith("Downloads") ? "DOWNLOADS" : dir);
#endif
            Sfx.UI("pickup", 0.5f, 1.4f);
        }
        catch (Exception e) { msg = "NAO FOI POSSIVEL SALVAR A FOTO"; Debug.LogWarning("Screenshot: " + e.Message); }
        flashAt = Time.unscaledTime;
        busy = false;
    }

    // Flash branco rapido + aviso (desenhado depois da foto, entao nao aparece nela).
    void OnGUI()
    {
        float t = Time.unscaledTime - flashAt;
        if (t > 2.2f) return;
        GUI.depth = -100;
        float vw = Screen.width, vh = Screen.height;
        if (t < 0.25f) { GUI.color = new Color(1f, 1f, 1f, 0.55f * (1f - t / 0.25f)); GUI.DrawTexture(new Rect(0f, 0f, vw, vh), Texture2D.whiteTexture); }
        GUI.color = Color.white;
        float k = Screen.height / 1080f;
        Matrix4x4 m = GUI.matrix;
        GUI.matrix = Matrix4x4.Scale(new Vector3(k, k, 1f));
        float w = vw / k;
        Game.Fill(w / 2f - 260f, 120f, 520f, 56f, new Color(0f, 0f, 0f, 0.7f));
        Game.Text(w / 2f - 260f, 120f, 520f, 56f, msg, 22, new Color(1f, 0.86f, 0.1f), TextAnchor.MiddleCenter);
        GUI.matrix = m;
    }
}
