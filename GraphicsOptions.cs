using ImGuiNET;
using RecompOne.Runtime;
using RecompOne.Runtime.Config;
using RecompOne.Runtime.Events;
using RecompOne.Runtime.Host.Window;

namespace Deception;

/// <summary>
/// Anisotropic filtering and mipmaps. Both are uniforms on the next batch.
/// A glyph or menu drawn at about one texel per pixel stays a single sample.
/// </summary>
public static class GraphicsOptions
{
    public const string AnisoKey = "Anisotropy";
    public const string MipKey = "Mipmaps";

    static readonly int[] Levels = [1, 2, 4, 8, 16];
    static readonly string[] AnisoLabels = ["1 (off)", "2", "4", "8", "16"];

    public static void Install()
    {
        SettingsRegistry.Extend("display.render_scale", Draw);
        Event.AddListener<RuntimeReadyEvent>(_ => Apply());
    }

    public static void Apply()
    {
        int stored = ConfigManager.View.GetInt(AnisoKey, 16);
        GteDepth.Anisotropy = Snap(stored);
        GteDepth.Mipmaps = ConfigManager.View.GetBool(MipKey, true);
        Console.WriteLine($"[deception] aniso {GteDepth.Anisotropy}, mipmaps {(GteDepth.Mipmaps ? "on" : "off")}");
    }

    static void Draw()
    {
        int aniso = IndexOf(GteDepth.Anisotropy);
        if (ImGui.Combo("Anisotropic filtering", ref aniso, AnisoLabels, AnisoLabels.Length))
        {
            GteDepth.Anisotropy = Levels[aniso];
            ConfigManager.View.SetInt(AnisoKey, Levels[aniso]);
            ConfigManager.SaveView(PanelManager.Panels);
        }

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Samples along a pixel's long texture axis. 1 is one texel. Menus drawn near 1:1 stay one sample.");

        bool mips = GteDepth.Mipmaps;
        if (ImGui.Checkbox("Mipmaps", ref mips))
        {
            GteDepth.Mipmaps = mips;
            ConfigManager.View.SetBool(MipKey, mips);
            ConfigManager.SaveView(PanelManager.Panels);
        }

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("A mip chain for minified textures. Texture windows skip it.");
    }

    static int Snap(int n)
    {
        int best = 1;
        foreach (int level in Levels)
            if (n >= level) best = level;
        return best;
    }

    static int IndexOf(int n)
    {
        for (int i = 0; i < Levels.Length; i++)
            if (Levels[i] == n) return i;
        return Levels.Length - 1;
    }
}
