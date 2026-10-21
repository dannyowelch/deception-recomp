using RecompOne.Runtime.Config;

namespace Deception;

/// <summary>
/// Wallpaper cells and doors outside the sharp distance that are still drawn.
/// On keeps the close-up art with no extra subdivision. Off is the original far art.
/// </summary>
public static class WallLod
{
    public static uint FarS0()
    {
        return ConfigManager.Game.HighDetailWalls ? 0u : 1u;
    }

    /// <summary>True when a door past the sharp distance keeps the blocky page.</summary>
    public static bool UseFarDoor()
    {
        return !ConfigManager.Game.HighDetailWalls;
    }
}
