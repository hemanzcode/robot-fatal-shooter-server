using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;

// Instala os pacotes que o projeto precisa (uma vez) e anota o resultado em Logs/packages.txt.
[InitializeOnLoad]
public static class PackageInstall
{
    static readonly string[] Wanted = { "com.unity.render-pipelines.universal", "com.unity.ai.navigation" };
    static readonly Queue<string> queue = new Queue<string>();
    static AddRequest req;

    static PackageInstall() { EditorApplication.delayCall += Begin; }

    static void Begin()
    {
        string manifest = File.Exists("Packages/manifest.json") ? File.ReadAllText("Packages/manifest.json") : "";
        foreach (string p in Wanted) if (!manifest.Contains("\"" + p + "\"")) queue.Enqueue(p);
        if (queue.Count == 0) return;
        Log("BEGIN " + System.DateTime.Now.ToString("HH:mm:ss") + "\n", false);
        Next();
        EditorApplication.update += Poll;
    }

    static void Next() { req = queue.Count > 0 ? Client.Add(queue.Dequeue()) : null; }

    static void Poll()
    {
        if (req == null) { EditorApplication.update -= Poll; Log("DONE\n", true); return; }
        if (!req.IsCompleted) return;
        if (req.Status == StatusCode.Success) Log("OK " + req.Result.name + " " + req.Result.version + "\n", true);
        else Log("FAIL " + (req.Error != null ? req.Error.message : "?") + "\n", true);
        Next();
    }

    static void Log(string s, bool append)
    {
        try { Directory.CreateDirectory("Logs"); if (append) File.AppendAllText("Logs/packages.txt", s); else File.WriteAllText("Logs/packages.txt", s); }
        catch (System.Exception) { }
    }
}
