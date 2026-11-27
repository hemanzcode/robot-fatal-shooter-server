using UnityEngine;

// Convites de amigos e avisos (pedido de amizade): aparecem no canto da tela em qualquer lugar do jogo.
// Na partida o mouse fica preso: o aviso pede para abrir o menu (ESC) e responder ali.
public class FriendsUI : MonoBehaviour
{
    static FriendsUI inst;
    static readonly Color Accent = new Color(1f, 0.86f, 0.1f);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (inst != null) return;
        GameObject g = new GameObject("FriendsUI");
        DontDestroyOnLoad(g);
        inst = g.AddComponent<FriendsUI>();
    }

    bool Btn(float x, float y, float w, float h, string label, bool primary)
    {
        Rect r = new Rect(x, y, w, h);
        Event e = Event.current;
        bool hover = r.Contains(e.mousePosition);
        Game.Fill(x, y, w, h, primary ? new Color(Accent.r, Accent.g, Accent.b, hover ? 1f : 0.85f) : new Color(1f, 1f, 1f, hover ? 0.2f : 0.08f));
        Game.Text(x, y, w, h, label, 18, primary ? new Color(0.03f, 0.03f, 0.04f) : Color.white, TextAnchor.MiddleCenter);
        if (e.type == EventType.MouseDown && e.button == 0 && hover) { e.Use(); return true; }
        return false;
    }

    void OnGUI()
    {
        Net n = Net.I;
        if (n == null || Game.I == null || !n.loggedIn) return;
        if (Event.current.type == EventType.Layout) n.invites.RemoveAll(v => Time.unscaledTime - v.at > 90f || (n.InRoom && v.code == n.roomCode));
        bool show = n.invites.Count > 0;
        bool note = n.friendMsg != "" && Time.unscaledTime - n.friendMsgAt < 5f && Game.I.menus != null && Game.I.menus.page != Menus.Page.Online && Game.I.menus.page != Menus.Page.Pause;
        if (!show && !note) return;
        GUI.depth = -30;
        Game.EnsureStyle();
        float s = Screen.height / 1080f;
        GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));
        float vw = Screen.width / s;
        float w = 520f, x = vw - w - 30f, y = 140f;
        bool locked = Cursor.lockState == CursorLockMode.Locked;

        if (note)
        {
            Game.Fill(x, y, w, 50f, new Color(0.02f, 0.025f, 0.04f, 0.92f));
            Game.Fill(x, y, 5f, 50f, n.friendOk ? Accent : new Color(1f, 0.45f, 0.4f));
            Game.Text(x + 18f, y, w - 30f, 50f, n.friendMsg, 17, Color.white, TextAnchor.MiddleLeft);
            y += 60f;
        }
        for (int i = n.invites.Count - 1; i >= 0; i--)
        {
            NetMsg inv = n.invites[i];
            Game.Fill(x, y, w, 120f, new Color(0.02f, 0.025f, 0.04f, 0.94f));
            Game.Fill(x, y, 5f, 120f, Accent);
            Game.Text(x + 18f, y + 8f, w - 30f, 30f, inv.name + " te convidou", 21, Accent, TextAnchor.MiddleLeft);
            Game.Text(x + 18f, y + 38f, w - 30f, 26f, inv.mode + "  -  " + inv.map + "   //   sala " + inv.code, 16, new Color(1f, 1f, 1f, 0.75f), TextAnchor.MiddleLeft);
            if (locked)
                Game.Text(x + 18f, y + 70f, w - 30f, 40f, "Aperte ESC para responder", 16, new Color(1f, 1f, 1f, 0.6f), TextAnchor.MiddleLeft);
            else
            {
                if (Btn(x + 18f, y + 70f, 230f, 40f, "ENTRAR", true)) { n.AcceptInvite(inv); break; }
                if (Btn(x + 258f, y + 70f, 230f, 40f, "RECUSAR", false)) { n.invites.RemoveAt(i); break; }
            }
            y += 130f;
        }
    }
}
