using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class Priority1Validation
{
    public static void BuildAndValidate()
    {
        GreyboxSceneBuilder.Build();
        PlayerController.ForceMode = 1;

        // 1. Validate Actor HP & Tagging
        GameObject g = new GameObject("TestActor");
        Actor a = g.AddComponent<Actor>();
        a.Init("TEST", 1, false, 0, false);
        Assert(Mathf.Approximately(a.maxHealth, 100f), "Actor maxHealth must be 100");
        Assert(Mathf.Approximately(a.health, 100f), "Actor health must be 100");

        // Setup minimal Game.I for TakeDamage
        GameObject gmObj = new GameObject("TestGame");
        Game gm = gmObj.AddComponent<Game>();
        Game.I = gm;

        a.TakeDamage(29f, null, false, "VETOR");
        Assert(Mathf.Approximately(a.health, 71f), "Actor health after 29 dmg must be 71");
        Assert(Mathf.Approximately(a.MoveSpeedMult, 0.65f), "Tagging MoveSpeedMult must be 0.65 (-35%)");

        // Second hit refreshes without stacking exponentially
        a.TakeDamage(29f, null, false, "VETOR");
        Assert(Mathf.Approximately(a.MoveSpeedMult, 0.65f), "Tagging must not stack exponentially");

        // 2. Validate VETOR TTK (3-4 torso shots, 0.25s-0.35s) & Drop-off (12m vs 25m+)
        WeaponDef vetor = Weapons.All[0];
        Assert(vetor.name == "VETOR", "Weapon 0 must be VETOR");
        Assert(Mathf.Approximately(vetor.headMult, 2f), "VETOR headMult must be 2.0");
        float d12 = Combat.ApplyDropoff(vetor, 12f, vetor.damage) * vetor.chestMult;
        float d25 = Combat.ApplyDropoff(vetor, 25f, vetor.damage) * vetor.chestMult;
        float d35 = Combat.ApplyDropoff(vetor, 35f, vetor.damage) * vetor.chestMult;
        Assert(Mathf.Approximately(d12, vetor.damage * vetor.chestMult), "VETOR full damage at 12m");
        Assert(d25 < d12 * 0.6f, "VETOR drop-off at 25m");
        Assert(Mathf.Approximately(d35, d25), "VETOR clamped at minDamageMult above 25m");
        int shotsToKill = Mathf.CeilToInt(100f / Mathf.Round(d12));
        float ttk = (shotsToKill - 1) * vetor.interval;
        Assert(shotsToKill >= 3 && shotsToKill <= 4, "VETOR torso shots to kill must be 3-4, got " + shotsToKill);
        Assert(ttk >= 0.25f && ttk <= 0.35f, "VETOR TTK must be 0.25s-0.35s, got " + ttk);
        Assert(vetor.sprintToFire >= 0.35f && vetor.sprintToFire <= 0.45f, "VETOR sprintToFire must be 0.35-0.45s");

        // 3. Validate Sfx 3-layer clips & 3D Pool
        Sfx.Warm();
        Assert(Sfx.Get("rifle") != null && Sfx.Get("flyby") != null && Sfx.Get("headshot") != null, "Sfx clips must generate");

        // 4. Validate Bullet Flyby (2.0m radius)
        GameObject pObj = new GameObject("TestPlayer");
        Actor pAct = pObj.AddComponent<Actor>();
        pAct.Init("P", 0, true, 0, false);
        gm.player = pAct;
        GameObject cObj = new GameObject("TestCam");
        cObj.transform.position = new Vector3(0f, 1.6f, 0f);
        Camera cam = cObj.AddComponent<Camera>();
        cObj.tag = "MainCamera";
        Combat.flybyCount = 0;
        // Shot passing 1.0m from camera -> should trigger flyby
        Combat.TryBulletFlyby(a, new Vector3(1f, 1.6f, 10f), new Vector3(1f, 1.6f, -10f), false);
        Assert(Combat.flybyCount == 1, "Flyby must trigger within 2.0m");
        // Shot passing 3.0m from camera -> should NOT trigger flyby
        Combat.TryBulletFlyby(a, new Vector3(3f, 1.6f, 10f), new Vector3(3f, 1.6f, -10f), false);
        Assert(Combat.flybyCount == 1, "Flyby must NOT trigger beyond 2.0m");

        // Cleanup
        Object.DestroyImmediate(g);
        Object.DestroyImmediate(pObj);
        Object.DestroyImmediate(cObj);
        Object.DestroyImmediate(gmObj);
        Game.I = null;
        PlayerController.ForceMode = -1;

        Debug.Log("[Priority1Validation] ALL CHECKS PASSED! VETOR shots=" + shotsToKill + " TTK=" + ttk.ToString("0.000") + "s d12=" + d12.ToString("0.0") + " d25=" + d25.ToString("0.0"));
    }

    static void Assert(bool cond, string msg)
    {
        if (!cond) throw new System.Exception("[Priority1Validation FAILED] " + msg);
    }
}
