using UnityEngine.InputSystem;

/// <summary>
/// All rebindable key bindings for WASD and Point & Click modes.
/// Persisted via PlayerPrefs. Call Load() on startup, Save() after any change.
/// </summary>
public static class GameKeybinds
{
    // ── WASD Mode ─────────────────────────────────────────────────────────────
    public static Key Wasd_MoveForward = Key.W;
    public static Key Wasd_MoveBack    = Key.S;
    public static Key Wasd_MoveLeft    = Key.A;
    public static Key Wasd_MoveRight   = Key.D;
    public static Key Wasd_Ability1    = Key.Space;
    public static Key Wasd_Ability2    = Key.LeftShift;
    public static Key Wasd_Ability3    = Key.E;

    // ── Point & Click Mode ────────────────────────────────────────────────────
    public static Key PnC_Stop         = Key.S;
    public static Key PnC_Ability1     = Key.Q;
    public static Key PnC_Ability2     = Key.W;
    public static Key PnC_Ability3     = Key.E;
    public static Key PnC_ForceAA      = Key.LeftShift;

    // ── Key queries ───────────────────────────────────────────────────────────

    public static bool IsPressed(Key key)
    {
        var kb = Keyboard.current;
        return kb != null && kb[key].isPressed;
    }

    public static bool WasPressedThisFrame(Key key)
    {
        var kb = Keyboard.current;
        return kb != null && kb[key].wasPressedThisFrame;
    }

    public static bool WasReleasedThisFrame(Key key)
    {
        var kb = Keyboard.current;
        return kb != null && kb[key].wasReleasedThisFrame;
    }

    // ── Save / Load ───────────────────────────────────────────────────────────

    public static void Save()
    {
        UnityEngine.PlayerPrefs.SetInt("kb_w_fwd",  (int)Wasd_MoveForward);
        UnityEngine.PlayerPrefs.SetInt("kb_w_back", (int)Wasd_MoveBack);
        UnityEngine.PlayerPrefs.SetInt("kb_w_left", (int)Wasd_MoveLeft);
        UnityEngine.PlayerPrefs.SetInt("kb_w_right",(int)Wasd_MoveRight);
        UnityEngine.PlayerPrefs.SetInt("kb_w_ab1",  (int)Wasd_Ability1);
        UnityEngine.PlayerPrefs.SetInt("kb_w_ab2",  (int)Wasd_Ability2);
        UnityEngine.PlayerPrefs.SetInt("kb_w_ab3",  (int)Wasd_Ability3);

        UnityEngine.PlayerPrefs.SetInt("kb_p_stop", (int)PnC_Stop);
        UnityEngine.PlayerPrefs.SetInt("kb_p_ab1",  (int)PnC_Ability1);
        UnityEngine.PlayerPrefs.SetInt("kb_p_ab2",  (int)PnC_Ability2);
        UnityEngine.PlayerPrefs.SetInt("kb_p_ab3",  (int)PnC_Ability3);
        UnityEngine.PlayerPrefs.SetInt("kb_p_faa",  (int)PnC_ForceAA);
        UnityEngine.PlayerPrefs.Save();
    }

    public static void Load()
    {
        Wasd_MoveForward = (Key)UnityEngine.PlayerPrefs.GetInt("kb_w_fwd",  (int)Key.W);
        Wasd_MoveBack    = (Key)UnityEngine.PlayerPrefs.GetInt("kb_w_back", (int)Key.S);
        Wasd_MoveLeft    = (Key)UnityEngine.PlayerPrefs.GetInt("kb_w_left", (int)Key.A);
        Wasd_MoveRight   = (Key)UnityEngine.PlayerPrefs.GetInt("kb_w_right",(int)Key.D);
        Wasd_Ability1    = (Key)UnityEngine.PlayerPrefs.GetInt("kb_w_ab1",  (int)Key.Space);
        Wasd_Ability2    = (Key)UnityEngine.PlayerPrefs.GetInt("kb_w_ab2",  (int)Key.LeftShift);
        Wasd_Ability3    = (Key)UnityEngine.PlayerPrefs.GetInt("kb_w_ab3",  (int)Key.E);

        PnC_Stop         = (Key)UnityEngine.PlayerPrefs.GetInt("kb_p_stop", (int)Key.S);
        PnC_Ability1     = (Key)UnityEngine.PlayerPrefs.GetInt("kb_p_ab1",  (int)Key.Q);
        PnC_Ability2     = (Key)UnityEngine.PlayerPrefs.GetInt("kb_p_ab2",  (int)Key.W);
        PnC_Ability3     = (Key)UnityEngine.PlayerPrefs.GetInt("kb_p_ab3",  (int)Key.E);
        PnC_ForceAA      = (Key)UnityEngine.PlayerPrefs.GetInt("kb_p_faa",  (int)Key.LeftShift);
    }

    public static void ResetToDefaults()
    {
        Wasd_MoveForward = Key.W;
        Wasd_MoveBack    = Key.S;
        Wasd_MoveLeft    = Key.A;
        Wasd_MoveRight   = Key.D;
        Wasd_Ability1    = Key.Space;
        Wasd_Ability2    = Key.LeftShift;
        Wasd_Ability3    = Key.E;

        PnC_Stop         = Key.S;
        PnC_Ability1     = Key.Q;
        PnC_Ability2     = Key.W;
        PnC_Ability3     = Key.E;
        PnC_ForceAA      = Key.LeftShift;
    }

    /// <summary>Returns a short display name for a Key (e.g. Key.Space → "Space").</summary>
    public static string KeyName(Key key)
    {
        return key switch
        {
            Key.Space      => "Space",
            Key.LeftShift  => "L.Shift",
            Key.RightShift => "R.Shift",
            Key.LeftCtrl   => "L.Ctrl",
            Key.RightCtrl  => "R.Ctrl",
            Key.LeftAlt    => "L.Alt",
            Key.RightAlt   => "R.Alt",
            Key.UpArrow    => "Up",
            Key.DownArrow  => "Down",
            Key.LeftArrow  => "Left",
            Key.RightArrow => "Right",
            Key.Backspace  => "Backspace",
            Key.Tab        => "Tab",
            Key.CapsLock   => "Caps",
            _              => key.ToString()
        };
    }
}
