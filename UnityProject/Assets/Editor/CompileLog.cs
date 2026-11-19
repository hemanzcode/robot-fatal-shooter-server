using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Compilation;

// Grava os erros de compilacao em Logs/compile.txt para facilitar o diagnostico.
[InitializeOnLoad]
public static class CompileLog
{
    const string PathLog = "Logs/compile.txt";

    static CompileLog()
    {
        CompilationPipeline.compilationStarted += OnStarted;
        CompilationPipeline.assemblyCompilationFinished += OnAssembly;
        CompilationPipeline.compilationFinished += OnFinished;
    }

    static void Write(string text, bool append)
    {
        try
        {
            Directory.CreateDirectory("Logs");
            if (append) File.AppendAllText(PathLog, text); else File.WriteAllText(PathLog, text);
        }
        catch (System.Exception) { }
    }

    static void OnStarted(object ctx) { Write("STARTED " + System.DateTime.Now.ToString("HH:mm:ss") + "\n", false); }

    static void OnAssembly(string assembly, CompilerMessage[] messages)
    {
        StringBuilder sb = new StringBuilder();
        int errors = 0;
        foreach (CompilerMessage m in messages)
        {
            if (m.type != CompilerMessageType.Error) continue;
            errors++;
            sb.AppendLine("ERROR " + m.file + "(" + m.line + "): " + m.message);
        }
        sb.AppendLine("ASSEMBLY " + Path.GetFileName(assembly) + " errors=" + errors);
        Write(sb.ToString(), true);
    }

    static void OnFinished(object ctx) { Write("FINISHED " + System.DateTime.Now.ToString("HH:mm:ss") + "\n", true); }
}
