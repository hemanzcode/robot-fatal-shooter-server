using System.IO;
using System.IO.Compression;
using UnityEditor;
using UnityEngine;

// Texturas das skins chegam em zips na raiz do projeto (skins_streaming_*.zip). Ao abrir/recompilar,
// cada zip novo (ou alterado) e descompactado em Assets/StreamingAssets/Skins.
[InitializeOnLoad]
public static class SkinAssets
{
    static SkinAssets() { EditorApplication.delayCall += () => Extract(false); }

    [MenuItem("NeonArena/Extrair texturas das skins")]
    static void Menu() { Extract(true); }

    static void Extract(bool force)
    {
        string root = Path.GetDirectoryName(Application.dataPath);
        bool any = false;
        foreach (string zip in Directory.GetFiles(root, "skins_streaming*.zip")) any |= ExtractOne(root, zip, force);
        if (any) AssetDatabase.Refresh();
    }

    static bool ExtractOne(string root, string zip, bool force)
    {
        string dest = Path.Combine(Application.dataPath, "StreamingAssets");
        string mark = Path.Combine(root, "Library", Path.GetFileNameWithoutExtension(zip) + ".stamp");
        string stamp = File.GetLastWriteTimeUtc(zip).Ticks + "|" + new FileInfo(zip).Length;
        if (!force && File.Exists(mark) && File.ReadAllText(mark) == stamp) return false;
        Directory.CreateDirectory(dest);
        int n = 0;
        using (ZipArchive a = ZipFile.OpenRead(zip))
        {
            foreach (ZipArchiveEntry e in a.Entries)
            {
                if (string.IsNullOrEmpty(e.Name)) continue;
                string p = Path.Combine(dest, e.FullName.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(p));
                e.ExtractToFile(p, true);
                n++;
            }
        }
        File.WriteAllText(mark, stamp);
        Debug.Log("Skins: " + n + " texturas extraidas de " + Path.GetFileName(zip));
        return true;
    }
}
