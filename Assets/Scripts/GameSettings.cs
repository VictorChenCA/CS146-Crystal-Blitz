/// <summary>
/// Shared runtime settings readable by any script without injection.
/// Written by GameManager, read by PlayerController and AutoAttacker.
/// </summary>
public static class GameSettings
{
    /// <summary>True = WASD movement; false = Point & Click / NavMesh movement.</summary>
    public static bool UseWasd = true;

    /// <summary>Multiplier for the bottom ability bar (stacks on top of _guiScale in GameManager).</summary>
    public static float BottomBarScale = 1f;

    /// <summary>Scale multiplier for the procedural cursor textures in AutoAttacker.</summary>
    public static float CursorScale = 1f;
}
