using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Gera a cena Assets/Scenes/Greybox_CombatTest.unity usando exclusivamente colisores primitivos
/// (BoxCollider, CapsuleCollider), iluminacao simples, piso com marcacoes em 12m, 25m e 35m,
/// corredores com quinas de 90 graus e muretas de 1,15m.
/// </summary>
public static class GreyboxSceneBuilder
{
    [MenuItem("FatalShooter/Build Greybox CombatTest Scene")]
    public static void Build()
    {
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // Iluminacao simples
        GameObject sun = new GameObject("Directional Light");
        Light light = sun.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.25f;
        light.color = new Color(1f, 0.96f, 0.9f);
        light.shadows = LightShadows.Soft;
        sun.transform.rotation = Quaternion.Euler(48f, 35f, 0f);

        Material floorMat = MakeMat(new Color(0.32f, 0.34f, 0.36f));
        Material wallMat = MakeMat(new Color(0.55f, 0.57f, 0.60f));
        Material coverMat = MakeMat(new Color(0.75f, 0.45f, 0.22f));
        Material m12Mat = MakeMat(new Color(0.2f, 0.85f, 0.35f));
        Material m25Mat = MakeMat(new Color(0.95f, 0.78f, 0.15f));
        Material m35Mat = MakeMat(new Color(0.9f, 0.25f, 0.25f));

        // Piso neutro (50m x 60m)
        Box("Floor", new Vector3(0f, -0.5f, 20f), new Vector3(50f, 1f, 60f), floorMat);

        // Marcacoes visuais de distancia no piso: 12m, 25m e 35m
        Marker("Mark_12m", 12f, m12Mat);
        Marker("Mark_25m", 25f, m25Mat);
        Marker("Mark_35m", 35f, m35Mat);

        // Paredes externas da arena Greybox
        Box("Wall_North", new Vector3(0f, 2.5f, 48f), new Vector3(50f, 5f, 1f), wallMat);
        Box("Wall_South", new Vector3(0f, 2.5f, -8f), new Vector3(50f, 5f, 1f), wallMat);
        Box("Wall_West", new Vector3(-25f, 2.5f, 20f), new Vector3(1f, 5f, 56f), wallMat);
        Box("Wall_East", new Vector3(25f, 2.5f, 20f), new Vector3(1f, 5f, 56f), wallMat);

        // Corredores com quinas limpas de 90 graus para peek lateral (lado esquerdo e direito)
        Box("Corridor_L_Outer", new Vector3(-13f, 2f, 14f), new Vector3(1f, 4f, 22f), wallMat);
        Box("Corridor_L_Corner", new Vector3(-9.5f, 2f, 25f), new Vector3(8f, 4f, 1f), wallMat);
        Box("Corridor_L_Inner", new Vector3(-7f, 2f, 10f), new Vector3(1f, 4f, 12f), wallMat);

        Box("Corridor_R_Outer", new Vector3(13f, 2f, 14f), new Vector3(1f, 4f, 22f), wallMat);
        Box("Corridor_R_Corner", new Vector3(9.5f, 2f, 25f), new Vector3(8f, 4f, 1f), wallMat);
        Box("Corridor_R_Inner", new Vector3(7f, 2f, 10f), new Vector3(1f, 4f, 12f), wallMat);

        // Muretas de 1,15m de altura posicionadas para agachar (Ctrl/C) e fazer peek nos alvos
        Box("LowWall_1_15m_Center", new Vector3(0f, 0.575f, 4.5f), new Vector3(4.2f, 1.15f, 0.6f), coverMat);
        Box("LowWall_1_15m_L", new Vector3(-4.2f, 0.575f, 7.5f), new Vector3(3.2f, 1.15f, 0.6f), coverMat);
        Box("LowWall_1_15m_R", new Vector3(4.2f, 0.575f, 7.5f), new Vector3(3.2f, 1.15f, 0.6f), coverMat);
        Box("LowWall_1_15m_Mid", new Vector3(0f, 0.575f, 18f), new Vector3(4.0f, 1.15f, 0.6f), coverMat);

        // Coluna cilindrica (CapsuleCollider) para teste de quina/cobertura
        GameObject pillar = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        pillar.name = "Cover_Capsule";
        pillar.transform.position = new Vector3(-7f, 1.5f, 20f);
        pillar.transform.localScale = new Vector3(1.2f, 1.5f, 1.2f);
        pillar.GetComponent<Renderer>().sharedMaterial = wallMat;

        // Controlador de testes Greybox
        GameObject ctrl = new GameObject("GreyboxCombatTest");
        ctrl.AddComponent<GreyboxCombatTest>();

        System.IO.Directory.CreateDirectory("Assets/Scenes");
        EditorSceneManager.SaveScene(scene, "Assets/Scenes/Greybox_CombatTest.unity");
        Debug.Log("[GreyboxSceneBuilder] Saved Assets/Scenes/Greybox_CombatTest.unity");
    }

    static void Box(string name, Vector3 pos, Vector3 scale, Material mat)
    {
        GameObject g = GameObject.CreatePrimitive(PrimitiveType.Cube);
        g.name = name;
        g.transform.position = pos;
        g.transform.localScale = scale;
        g.GetComponent<Renderer>().sharedMaterial = mat;
    }

    static void Marker(string name, float z, Material mat)
    {
        GameObject g = GameObject.CreatePrimitive(PrimitiveType.Cube);
        g.name = name;
        g.transform.position = new Vector3(0f, 0.01f, z);
        g.transform.localScale = new Vector3(16f, 0.02f, 0.25f);
        Object.DestroyImmediate(g.GetComponent<Collider>());
        g.GetComponent<Renderer>().sharedMaterial = mat;
    }

    static Material MakeMat(Color c)
    {
        Shader sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        Material m = new Material(sh);
        m.color = c;
        return m;
    }
}
