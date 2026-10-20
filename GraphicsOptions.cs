using ImGuiNET;
using RecompOne.Runtime;
using RecompOne.Runtime.Config;
using RecompOne.Runtime.Events;
using RecompOne.Runtime.Host;
using RecompOne.Runtime.Host.Window;

namespace Deception;

/// <summary>
/// Anisotropic filtering, mipmaps, and optional side art for a 16:9 frame.
/// The game picture stays 4:3. A 16:9 frame leaves the spare columns for the
/// images in border/, or black when the borders are hidden.
/// </summary>
public static class GraphicsOptions
{
    public const string AnisoKey = "Anisotropy";
    public const string MipKey = "Mipmaps";
    public const string FrameKey = "Frame";
    public const string HideBordersKey = "HideBorders";

    const float SixteenNine = 16f / 9f;

    static readonly int[] Levels = [1, 2, 4, 8, 16];
    static readonly string[] AnisoLabels = ["1 (off)", "2", "4", "8", "16"];
    static readonly string[] FrameLabels = ["4:3", "16:9"];

    static bool _wide = true;
    static bool _hide;
    static bool _tried;
    static uint _left, _right;

    public static void Install()
    {
        SettingsRegistry.Extend("display.render_scale", Draw);
        OutputView.Prepare = EnsureArt;
        Event.AddListener<RuntimeReadyEvent>(_ => Apply());
    }

    public static void Apply()
    {
        int stored = ConfigManager.View.GetInt(AnisoKey, 16);
        GteDepth.Anisotropy = Snap(stored);
        GteDepth.Mipmaps = ConfigManager.View.GetBool(MipKey, true);
        _wide = ConfigManager.View.GetString(FrameKey, "16:9") == "16:9";
        _hide = ConfigManager.View.GetBool(HideBordersKey, false);
        Push();
        Console.WriteLine($"[deception] aniso {GteDepth.Anisotropy}, mipmaps {(GteDepth.Mipmaps ? "on" : "off")}, " +
                          $"frame {(_wide ? "16:9" : "4:3")}, borders {(_wide && !_hide ? "on" : "off")}");
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

        int frame = _wide ? 1 : 0;
        if (ImGui.Combo("Frame", ref frame, FrameLabels, FrameLabels.Length))
        {
            _wide = frame == 1;
            ConfigManager.View.SetString(FrameKey, _wide ? "16:9" : "4:3");
            ConfigManager.SaveView(PanelManager.Panels);
            Push();
        }

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("16:9 draws the side art when the window is wide. 4:3 leaves those columns black.");

        if (!_wide) ImGui.BeginDisabled();
        if (ImGui.Checkbox("Hide borders", ref _hide))
        {
            ConfigManager.View.SetBool(HideBordersKey, _hide);
            ConfigManager.SaveView(PanelManager.Panels);
            Push();
        }

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Leaves the side bars black.");
        if (!_wide) ImGui.EndDisabled();
    }

    static void EnsureArt()
    {
        if (_tried) return;
        _tried = true;
        _left = Load("left-border.jpg");
        _right = Load("right-border.jpg");
        if (_left == 0 || _right == 0)
            Console.WriteLine("[deception] border images were not found");
        Push();
    }

    static uint Load(string name)
    {
        string? path = Find(name);
        if (path == null) return 0;
        try { return HostWindow.UploadImage(File.ReadAllBytes(path), smooth: true); }
        catch (Exception e)
        {
            Console.WriteLine($"[deception] border {name}: {e.Message}");
            return 0;
        }
    }

    static string? Find(string name)
    {
        foreach (var root in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            var direct = Path.Combine(root, "border", name);
            if (File.Exists(direct)) return direct;
            var packed = Path.Combine(root, "content", "border", name);
            if (File.Exists(packed)) return packed;
        }
        return null;
    }

    static void Push() =>
        OutputView.SetSideBorders(_wide ? SixteenNine : 0f, _wide && !_hide, _left, _right);

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
