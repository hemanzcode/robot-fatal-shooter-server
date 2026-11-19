using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Cria uma cena vazia salva e a registra no Build Settings, para que o jogo
// tambem funcione ao gerar um executavel (File > Build And Run).
[InitializeOnLoad]
public static class NeonArenaSetup
{
    const string ScenePath = "Assets/Scenes/RobotFatalShooter.unity";

    static NeonArenaSetup()
    {
        EditorApplication.delayCall += Ensure;
    }

    static void Ensure()
    {
        try
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (!System.IO.File.Exists(ScenePath))
            {
                System.IO.Directory.CreateDirectory("Assets/Scenes");
                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                EditorSceneManager.SaveScene(scene, ScenePath);
                AssetDatabase.Refresh();
            }
            if (PlayerSettings.productName != "Robot Fatal Shooter") PlayerSettings.productName = "Robot Fatal Shooter";
            // Uma vez por projeto: reimporta modelos e texturas ja com as regras de importacao compiladas.
            string key = "rfs_reimport_" + Application.dataPath;
            if (EditorPrefs.GetInt(key, 0) != 1)
            {
                EditorPrefs.SetInt(key, 1);
                foreach (string f in new[] { "Assets/Resources/Agents", "Assets/Resources/Weapons", "Assets/Resources/Tex" })
                    if (AssetDatabase.IsValidFolder(f)) AssetDatabase.ImportAsset(f, ImportAssetOptions.ImportRecursive | ImportAssetOptions.ForceUpdate);
            }
            // Uma vez por projeto: extrai as texturas embutidas nos agentes e reimporta os modelos ja ligados a elas.
            string tkey = "rfs_agent_textures_" + Application.dataPath;
            if (EditorPrefs.GetInt(tkey, 0) != 1 && AssetDatabase.IsValidFolder("Assets/Resources/Agents"))
            {
                EditorPrefs.SetInt(tkey, 1);
                string dir = "Assets/Resources/Agents/Textures";
                System.IO.Directory.CreateDirectory(dir);
                string[] guids = AssetDatabase.FindAssets("t:Model", new[] { "Assets/Resources/Agents" });
                int ok = 0;
                foreach (string g in guids)
                {
                    ModelImporter mi = AssetImporter.GetAtPath(AssetDatabase.GUIDToAssetPath(g)) as ModelImporter;
                    if (mi != null && mi.ExtractTextures(dir)) ok++;
                }
                AssetDatabase.Refresh();
                foreach (string g in guids) AssetDatabase.ImportAsset(AssetDatabase.GUIDToAssetPath(g), ImportAssetOptions.ForceUpdate);
                try { System.IO.File.WriteAllText("Logs/agent_textures.txt", "models=" + guids.Length + " extracted=" + ok + " files=" + System.IO.Directory.GetFiles(dir).Length + "\n"); } catch (System.Exception) { }
            }
            bool has = false;
            foreach (var s in EditorBuildSettings.scenes) if (s.path == ScenePath) has = true;
            if (!has) EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("NeonArena: nao foi possivel preparar a cena automaticamente: " + e.Message);
        }
    }
}
