using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

// Skins das armas (catalogo do amigo: Resources/Skins/skins.json).
// Dois tipos: "cor" (cor/metal/rugosidade por material, nos modelos originais) e "textura"
// (atlas por arma em StreamingAssets/Skins/<id>/<Modelo>_a|n|m.jpg, nos modelos com UV:
// Resources/Weapons/UV_A ou UV_P). Compra e por arma, como nos FPS grandes.
// FASE 1: carteira e inventario guardados localmente (moedas de teste no editor). Depois o servidor assume.
[Serializable] public class SkinMat { public string slot, cor; public float metallic, roughness; public bool emissivo; }
[Serializable] public class SkinDef { public string id, nome, raridade, descricao, tipo, uv; public int preco; public string[] paleta; public SkinMat[] mats; }
[Serializable] public class SkinRarity { public string id, nome, cor; public int preco; }

public static class Skins
{
    [Serializable] class Catalog { public SkinRarity[] raridades; public SkinDef[] skins; }

    public static readonly string[] WeaponKeys = { "vetor", "pulso", "sniper", "bazuca", "plasma" };
    public static readonly string[] Models = { "Rifle", "Shotgun", "Rail", "Launcher", "Plasma" };
    static SkinDef[] all;
    static SkinRarity[] rarities;
    static readonly Dictionary<string, SkinDef> byId = new Dictionary<string, SkinDef>();

    public static SkinDef[] All { get { Load(); return all; } }

    static void Load()
    {
        if (all != null) return;
        TextAsset ta = Resources.Load<TextAsset>("Skins/skins");
        Catalog c = ta != null ? JsonUtility.FromJson<Catalog>(ta.text) : null;
        all = c != null && c.skins != null ? c.skins : new SkinDef[0];
        rarities = c != null && c.raridades != null ? c.raridades : new SkinRarity[0];
        foreach (SkinDef s in all) byId[s.id] = s;
    }

    public static SkinDef Get(string id)
    {
        Load();
        SkinDef s;
        return !string.IsNullOrEmpty(id) && byId.TryGetValue(id, out s) ? s : null;
    }

    public static Color RarityColor(string r)
    {
        Load();
        foreach (SkinRarity x in rarities) if (x.id == r) return Hex(x.cor);
        return Color.gray;
    }

    public static string RarityName(string r)
    {
        Load();
        foreach (SkinRarity x in rarities) if (x.id == r) return x.nome.ToUpperInvariant();
        return "";
    }

    public static Color Hex(string h)
    {
        Color c;
        return !string.IsNullOrEmpty(h) && ColorUtility.TryParseHtmlString(h, out c) ? c : Color.gray;
    }

    // ---------- Carteira e inventario ----------
    // Com login: tudo vem do servidor (SHOTS, skins, equipadas, nivel). O jogo so pede; o servidor decide.
    // Sem login: no editor, carteira local de teste (50.000 SHOTS) para testar a loja; no jogo publicado, saldo 0.
    public const string Coin = "SHOTS";
    public static bool Server;                         // perfil recebido do servidor
    static int sShots, sLastShots;
    static readonly HashSet<string> sOwned = new HashSet<string>();
    static readonly string[] sEquip = { "", "", "", "", "" };
    public static int Level = 1, LevelXp, LevelNeed = 1000, Xp, LastXp;
    public static string Rank = "RECRUTA";
    public static bool PixOn;
    public static NetPack[] Packs = new NetPack[0];
    public static string shopError = "";
    public static float shopErrorAt = -99f, pendingAt = -99f;
    public static NetMsg pix;                          // cobranca PIX aberta (QR na tela)
    public static int pixPaid;                         // SHOTS creditados pelo ultimo PIX
    static Texture2D pixQr;
    static string pixQrFor;

    public static void FromServer(NetMsg m)
    {
        Server = true;
        sShots = m.shots; sLastShots = m.lastShots; Xp = m.xp; LastXp = m.lastXp;
        Level = Mathf.Max(1, m.lvl); LevelXp = m.lvlXp; LevelNeed = Mathf.Max(1, m.lvlNeed); Rank = m.rank ?? "RECRUTA";
        PixOn = m.pix; Packs = m.packs ?? new NetPack[0];
        sOwned.Clear();
        if (m.owned != null) foreach (string o in m.owned) sOwned.Add(o);
        for (int i = 0; i < 5; i++) sEquip[i] = m.equip != null && i < m.equip.Length ? (m.equip[i] ?? "") : "";
        pendingAt = -99f;
    }
    public static void ClearServer() { Server = false; sOwned.Clear(); for (int i = 0; i < 5; i++) sEquip[i] = ""; pix = null; }
    public static void ServerError(string e) { shopError = e; shopErrorAt = Time.unscaledTime; pendingAt = -99f; }
    public static bool Pending { get { return Time.unscaledTime - pendingAt < 8f; } }
    public static bool CanBuy { get { return Server || Application.isEditor; } }

    public static int Coins
    {
        get
        {
            if (Server) return sShots;
#if UNITY_EDITOR
            // SHOTS de teste so no editor e sem login.
            if (PlayerPrefs.GetInt("na_coins_seed", 0) == 0) { PlayerPrefs.SetInt("na_coins_seed", 1); PlayerPrefs.SetInt("na_coins", 50000); }
            return PlayerPrefs.GetInt("na_coins", 0);
#else
            return 0;
#endif
        }
        set { if (!Server) { PlayerPrefs.SetInt("na_coins", Mathf.Max(0, value)); PlayerPrefs.Save(); } }
    }
    public static int LastMatchCoins { get { return Server ? sLastShots : 0; } }

    static HashSet<string> owned;
    static HashSet<string> Owned()
    {
        if (Server) return sOwned;
        if (owned != null) return owned;
        owned = new HashSet<string>();
        foreach (string s in PlayerPrefs.GetString("na_skins", "").Split(',')) if (s.Length > 0) owned.Add(s);
        return owned;
    }

    public static bool Owns(string id, int w) { return string.IsNullOrEmpty(id) || Owned().Contains(id + "|" + w); }

    // Com servidor: so manda o pedido (a resposta chega como perfil novo). Retorna se o pedido saiu.
    public static bool Buy(string id, int w)
    {
        SkinDef s = Get(id);
        if (s == null || Owns(id, w) || Coins < s.preco) return false;
        if (Server)
        {
            if (Net.I == null || !Net.I.connected) { ServerError("Sem conexao com o servidor."); return false; }
            pendingAt = Time.unscaledTime; shopError = "";
            Net.I.Buy(id, w);
            return true;
        }
        if (!Application.isEditor) return false;
        Coins = Coins - s.preco;
        Owned().Add(id + "|" + w);
        PlayerPrefs.SetString("na_skins", string.Join(",", new List<string>(Owned()).ToArray()));
        PlayerPrefs.Save();
        return true;
    }

    // Skin equipada por arma ("" = padrao).
    public static string Equipped(int w)
    {
        if (Server) { string e = w >= 0 && w < 5 ? sEquip[w] : ""; return Owns(e, w) ? e : ""; }
        string id = PlayerPrefs.GetString("na_equip_" + w, ""); return Owns(id, w) ? id : "";
    }
    public static void Equip(int w, string id)
    {
        if (Server)
        {
            if (w >= 0 && w < 5) sEquip[w] = id ?? "";       // muda na hora; o servidor confirma
            if (Net.I != null && Net.I.connected) Net.I.EquipSkin(id ?? "", w);
            return;
        }
        PlayerPrefs.SetString("na_equip_" + w, id ?? ""); PlayerPrefs.Save();
    }

    // Imagem do QR do PIX (vem em base64 do servidor).
    public static Texture2D PixQr()
    {
        if (pix == null || string.IsNullOrEmpty(pix.qr)) return null;
        if (pixQrFor == pix.pid && pixQr != null) return pixQr;
        pixQrFor = pix.pid;
        pixQr = new Texture2D(2, 2);
        try { pixQr.LoadImage(Convert.FromBase64String(pix.qr)); pixQr.filterMode = FilterMode.Point; } catch (Exception) { pixQr = null; }
        return pixQr;
    }

    // ---------- Modelo e materiais ----------

    public static string ModelPath(int w, SkinDef s)
    {
        string m = Models[Mathf.Clamp(w, 0, Models.Length - 1)];
        if (s == null || s.tipo != "textura" || string.IsNullOrEmpty(s.uv)) return m;
        return "UV_" + s.uv + "/" + m;
    }

    static readonly Dictionary<string, Material> mats = new Dictionary<string, Material>();
    static Shader skinShader;

    // Material para uma peca da arma (slot = nome do material no modelo). null = usar o original (vidro, luz do time).
    public static Material MaterialFor(SkinDef s, int w, string slot)
    {
        if (s == null) return null;
        string n = slot.ToLowerInvariant();
        if (n.StartsWith("glow") || n.StartsWith("lens")) return null;
        if (s.tipo == "textura") return AtlasMaterial(s, w);
        SkinMat sm = null;
        if (s.mats != null)
        {
            foreach (SkinMat x in s.mats) if (n.StartsWith(x.slot)) { sm = x; break; }
            if (sm == null) foreach (SkinMat x in s.mats) if (x.slot == "metal") { sm = x; break; }
        }
        if (sm == null) return null;
        Color c = Hex(sm.cor);
        return Art.Mat(c, sm.emissivo ? c * 1.4f : Color.black, 0f, default(Color), Mathf.Clamp01(1f - sm.roughness), Mathf.Clamp01(sm.metallic));
    }

    static Material AtlasMaterial(SkinDef s, int w)
    {
        string key = s.id + "|" + w;
        Material m;
        if (mats.TryGetValue(key, out m) && m != null) return m;
        if (skinShader == null) skinShader = Resources.Load<Shader>("SkinSurface");
        if (skinShader == null) skinShader = Shader.Find("NeonArena/SkinSurface");
        m = new Material(skinShader);
        // Enquanto as texturas baixam: a cor principal da paleta.
        m.SetColor("_Color", s.paleta != null && s.paleta.Length > 0 ? Hex(s.paleta[0]) : Color.gray);
        m.SetFloat("_UseMaps", 0f);
        mats[key] = m;
        Loader().StartCoroutine(LoadMaps(s.id, Models[w], m));
        return m;
    }

    static IEnumerator LoadMaps(string id, string model, Material m)
    {
        string root = Application.streamingAssetsPath + "/Skins/" + id + "/" + model + "_";
        if (!root.Contains("://")) root = "file://" + root;
        Texture2D a = null, n = null, mk = null;
        yield return Fetch(root + "a.jpg", false, t => a = t);
        yield return Fetch(root + "n.jpg", true, t => n = t);
        yield return Fetch(root + "m.jpg", true, t => mk = t);
        if (m == null || a == null) yield break;
        m.SetTexture("_BaseMap", a);
        if (n != null) m.SetTexture("_NormalMap", n);
        if (mk != null) m.SetTexture("_MaskMap", mk);
        m.SetColor("_Color", Color.white);
        m.SetFloat("_UseMaps", 1f);
    }

    static IEnumerator Fetch(string url, bool linear, Action<Texture2D> done)
    {
        using (UnityWebRequest rq = UnityWebRequest.Get(url))
        {
            yield return rq.SendWebRequest();
            if (rq.result != UnityWebRequest.Result.Success) { Debug.LogWarning("Skin: falhou " + url + " " + rq.error); yield break; }
            Texture2D t = new Texture2D(2, 2, TextureFormat.RGBA32, true, linear);
            if (!t.LoadImage(rq.downloadHandler.data, false)) yield break;
            t.wrapMode = TextureWrapMode.Clamp;
            t.anisoLevel = 4;
            t.filterMode = FilterMode.Trilinear;
            t.Apply(true, true);   // libera a copia na memoria do processador
            done(t);
        }
    }

    class Runner : MonoBehaviour { }
    static Runner runner;
    static MonoBehaviour Loader()
    {
        if (runner == null)
        {
            GameObject g = new GameObject("SkinLoader");
            UnityEngine.Object.DontDestroyOnLoad(g);
            runner = g.AddComponent<Runner>();
        }
        return runner;
    }
}
