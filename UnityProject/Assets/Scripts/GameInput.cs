using UnityEngine;
#if !ENABLE_LEGACY_INPUT_MANAGER
using UnityEngine.InputSystem;
#endif

// Camada de entrada: funciona com o Input Manager antigo ou com o novo Input System,
// conforme a configuracao do projeto.
public static class GameInput
{
#if ENABLE_LEGACY_INPUT_MANAGER
    // Teclado e mouse somados aos controles de toque (TouchPad), quando o jogo roda em celular.
    public static Vector2 Move
    {
        get
        {
            float x = (Input.GetKey(KeyCode.D) ? 1f : 0f) - (Input.GetKey(KeyCode.A) ? 1f : 0f);
            float y = (Input.GetKey(KeyCode.W) ? 1f : 0f) - (Input.GetKey(KeyCode.S) ? 1f : 0f);
            return TouchPad.On ? Vector2.ClampMagnitude(new Vector2(x, y) + TouchPad.Move, 1f) : new Vector2(x, y);
        }
    }
    public static Vector2 Look { get { return TouchPad.On ? TouchPad.Look : new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y")); } }
    public static bool Fire { get { return TouchPad.On ? TouchPad.Fire : Input.GetMouseButton(0); } }
    public static bool FireDown { get { return TouchPad.On ? TouchPad.FireDown : Input.GetMouseButtonDown(0); } }
    public static bool Aim { get { return TouchPad.On ? TouchPad.Aim : Input.GetMouseButton(1); } }
    public static bool JumpDown { get { return Input.GetKeyDown(KeyCode.Space) || TouchPad.JumpDown; } }
    public static bool Sprint { get { return Input.GetKey(KeyCode.LeftShift) || (TouchPad.On && TouchPad.SprintLock); } }
    public static bool JumpHeld { get { return Input.GetKey(KeyCode.Space) || TouchPad.Jump; } }
    public static bool Crouch { get { return Input.GetKey(KeyCode.LeftControl) || TouchPad.Crouch; } }
    public static bool CrouchToggleDown { get { return Input.GetKeyDown(KeyCode.C); } }
    public static bool Prone { get { return TouchPad.Prone; } }
    public static bool ProneToggleDown { get { return Input.GetKeyDown(KeyCode.B); } }
    public static bool Down { get { return Input.GetKey(KeyCode.LeftControl) || TouchPad.Down; } }
    public static bool Use { get { return Input.GetKey(KeyCode.F) || TouchPad.Use; } }
    public static bool UseDown { get { return Input.GetKeyDown(KeyCode.F) || TouchPad.UseDown; } }
    public static bool ReloadDown { get { return Input.GetKeyDown(KeyCode.R) || TouchPad.ReloadDown; } }
    public static bool DashDown { get { return Input.GetKeyDown(KeyCode.E) || TouchPad.DashDown; } }
    public static bool GrenadeDown { get { return Input.GetKeyDown(KeyCode.Q) || TouchPad.GrenadeDown; } }
    public static bool SecondaryDown { get { return Input.GetKeyDown(KeyCode.C) || TouchPad.SecondaryDown; } }
    public static bool UltDown { get { return Input.GetKeyDown(KeyCode.X) || TouchPad.UltDown; } }
    public static bool PauseDown { get { return Input.GetKeyDown(KeyCode.Escape) || TouchPad.PauseDown; } }
    public static bool ConfirmDown { get { return Input.GetKeyDown(KeyCode.Return); } }
    public static bool Scoreboard { get { return Input.GetKey(KeyCode.Tab) || (TouchPad.On && TouchPad.Score); } }
    public static float Scroll { get { return TouchPad.On ? 0f : Input.mouseScrollDelta.y; } }
    public static int WeaponSlot
    {
        get
        {
            if (TouchPad.SelectSlot >= 0) return TouchPad.SelectSlot;
            if (TouchPad.NextWeapon && Game.I != null && Game.I.pc != null) return (Game.I.pc.cur + 1) % Weapons.All.Length;
            if (Input.GetKeyDown(KeyCode.Alpha1)) return 0;
            if (Input.GetKeyDown(KeyCode.Alpha2)) return 1;
            if (Input.GetKeyDown(KeyCode.Alpha3)) return 2;
            if (Input.GetKeyDown(KeyCode.Alpha4)) return 3;
            if (Input.GetKeyDown(KeyCode.Alpha5)) return 4;
            return -1;
        }
    }
#else
    static Keyboard K { get { return Keyboard.current; } }
    static Mouse M { get { return Mouse.current; } }

    public static Vector2 Move
    {
        get
        {
            if (K == null) return Vector2.zero;
            float x = (K.dKey.isPressed ? 1f : 0f) - (K.aKey.isPressed ? 1f : 0f);
            float y = (K.wKey.isPressed ? 1f : 0f) - (K.sKey.isPressed ? 1f : 0f);
            return new Vector2(x, y);
        }
    }
    public static Vector2 Look { get { return M == null ? Vector2.zero : M.delta.ReadValue() * 0.1f; } }
    public static bool Fire { get { return M != null && M.leftButton.isPressed; } }
    public static bool FireDown { get { return M != null && M.leftButton.wasPressedThisFrame; } }
    public static bool Aim { get { return M != null && M.rightButton.isPressed; } }
    public static bool JumpDown { get { return K != null && K.spaceKey.wasPressedThisFrame; } }
    public static bool Sprint { get { return K != null && K.leftShiftKey.isPressed; } }
    public static bool JumpHeld { get { return K != null && K.spaceKey.isPressed; } }
    public static bool Crouch { get { return K != null && K.leftCtrlKey.isPressed; } }
    public static bool CrouchToggleDown { get { return K != null && K.cKey.wasPressedThisFrame; } }
    public static bool Prone { get { return false; } }
    public static bool ProneToggleDown { get { return K != null && K.bKey.wasPressedThisFrame; } }
    public static bool Down { get { return K != null && K.leftCtrlKey.isPressed; } }
    public static bool Use { get { return K != null && K.fKey.isPressed; } }
    public static bool UseDown { get { return K != null && K.fKey.wasPressedThisFrame; } }
    public static bool ReloadDown { get { return K != null && K.rKey.wasPressedThisFrame; } }
    public static bool DashDown { get { return K != null && K.eKey.wasPressedThisFrame; } }
    public static bool GrenadeDown { get { return K != null && K.qKey.wasPressedThisFrame; } }
    public static bool SecondaryDown { get { return K != null && K.cKey.wasPressedThisFrame; } }
    public static bool UltDown { get { return K != null && K.xKey.wasPressedThisFrame; } }
    public static bool PauseDown { get { return K != null && K.escapeKey.wasPressedThisFrame; } }
    public static bool ConfirmDown { get { return K != null && K.enterKey.wasPressedThisFrame; } }
    public static bool Scoreboard { get { return K != null && K.tabKey.isPressed; } }
    public static float Scroll { get { return M == null ? 0f : M.scroll.ReadValue().y; } }
    public static int WeaponSlot
    {
        get
        {
            if (K == null) return -1;
            if (K.digit1Key.wasPressedThisFrame) return 0;
            if (K.digit2Key.wasPressedThisFrame) return 1;
            if (K.digit3Key.wasPressedThisFrame) return 2;
            if (K.digit4Key.wasPressedThisFrame) return 3;
            if (K.digit5Key.wasPressedThisFrame) return 4;
            return -1;
        }
    }
#endif
}
