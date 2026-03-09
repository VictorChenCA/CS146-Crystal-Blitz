/// <summary>
/// Shared runtime settings readable by any script without injection.
/// Written by GameManager, read by PlayerController and AutoAttacker.
/// </summary>
public static class GameSettings
{
    /// <summary>True = WASD movement; false = Point & Click / NavMesh movement.</summary>
    public static bool UseWasd = true;
}
