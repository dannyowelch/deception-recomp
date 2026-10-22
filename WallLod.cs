using RecompOne.Runtime.Config;

namespace Deception;

/// <summary>
/// Wallpaper cells and doors outside the sharp distance that are still drawn.
/// On keeps the close-up art with no extra subdivision. Off is the original far art.
/// The art stays blocky until <see cref="PrimHeap"/> has moved the primitive buffers.
/// </summary>
public static class WallLod
{
    public static uint FarS0()
    {
        return Sharp ? 0u : 1u;
    }

    /// <summary>True when a door past the sharp distance keeps the blocky page.</summary>
    public static bool UseFarDoor()
    {
        return !Sharp;
    }

    static bool Sharp => ConfigManager.Game.HighDetailWalls && PrimHeap.Ready;
}
