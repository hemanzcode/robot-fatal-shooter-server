using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

// Gera o jogo para Windows e o zip que vai para download (menu NeonArena > Gerar build Windows).
[InitializeOnLoad]
public static class BuildGame
{
    static BuildGame()
    {
        // Se um script muda durante o Play, a Unity recompila na hora e zera o estado do jogo
        // (erros em cascata). Com esta opcao ela espera o Play terminar.
        EditorPrefs.SetInt("ScriptCompilationDuringPlay", 1);
    }

    [MenuItem("NeonArena/Gerar build Windows (zip)")]
    public static void Build()
    {
        string dir = "Build/RobotFatalShooter";
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
        Directory.CreateDirectory(dir);
        BuildPlayerOptions o = new BuildPlayerOptions();
        o.scenes = new[] { "Assets/Scenes/RobotFatalShooter.unity" };
        o.locationPathName = dir + "/RobotFatalShooter.exe";
        o.target = BuildTarget.StandaloneWindows64;
        o.options = BuildOptions.None;
        BuildReport rep = BuildPipeline.BuildPlayer(o);
        string log = "Logs/build.txt";
        if (rep.summary.result != BuildResult.Succeeded)
        {
            File.WriteAllText(log, "FALHOU: " + rep.summary.result + " erros=" + rep.summary.totalErrors);
            Debug.LogError("Build falhou: " + rep.summary.result);
            return;
        }
        string zip = "Build/RobotFatalShooter.zip";
        if (File.Exists(zip)) File.Delete(zip);
        foreach (string d in Directory.GetDirectories(dir, "*DoNotShip*")) Directory.Delete(d, true);
        System.IO.Compression.ZipFile.CreateFromDirectory(dir, zip, System.IO.Compression.CompressionLevel.Optimal, true);
        long mb = new FileInfo(zip).Length / (1024 * 1024);
        File.WriteAllText(log, "OK zip=" + mb + "MB build=" + (rep.summary.totalSize / (1024 * 1024)) + "MB");
        Debug.Log("Build pronto: " + zip + " (" + mb + " MB)");
    }

    // Versao de navegador (PC e celular), servida em /play pelo servidor do jogo.
    [MenuItem("NeonArena/Gerar build Web (navegador e celular)")]
    public static void BuildWeb()
    {
        string dir = "Build/play";
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
        PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;
        PlayerSettings.WebGL.decompressionFallback = true;   // funciona sem cabecalhos especiais no servidor
        PlayerSettings.WebGL.exceptionSupport = WebGLExceptionSupport.ExplicitlyThrownExceptionsOnly;
        PlayerSettings.WebGL.dataCaching = true;
        PlayerSettings.runInBackground = true;
        PlayerSettings.WebGL.template = "PROJECT:RFS";   // pagina propria: tela cheia e resolucao 1x no celular
        BuildPlayerOptions o = new BuildPlayerOptions();
        o.scenes = new[] { "Assets/Scenes/RobotFatalShooter.unity" };
        o.locationPathName = dir;
        o.target = BuildTarget.WebGL;
        o.options = BuildOptions.None;
        BuildReport rep = BuildPipeline.BuildPlayer(o);
        string log = "Logs/buildweb.txt";
        if (rep.summary.result != BuildResult.Succeeded)
        {
            File.WriteAllText(log, "FALHOU: " + rep.summary.result + " erros=" + rep.summary.totalErrors);
            Debug.LogError("Build Web falhou: " + rep.summary.result);
            return;
        }
        string list = "";
        foreach (string f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories)) list += f.Replace('\\', '/') + " " + (new FileInfo(f).Length / 1024) + "KB\n";
        File.WriteAllText(log, "OK total=" + (rep.summary.totalSize / (1024 * 1024)) + "MB\n" + list);
        Debug.Log("Build Web pronto em " + dir);
    }
}
