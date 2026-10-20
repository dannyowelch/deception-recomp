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
    public const string ScaleKey = "WindowScale";

    const float SixteenNine = 16f / 9f;
    const int PictureW = 320;
    const int PictureH = 240;

    static readonly int[] Levels = [1, 2, 4, 8, 16];
    static readonly string[] AnisoLabels = ["1 (off)", "2", "4", "8", "16"];
    static readonly string[] FrameLabels = ["4:3", "16:9"];
    static readonly string[] ScaleLabels = new string[8];

    static bool _wide;
    static bool _hide;
    static int _scale = 4;
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
        _wide = ConfigManager.View.GetString(FrameKey, "4:3") == "16:9";
        _hide = ConfigManager.View.GetBool(HideBordersKey, false);
        _scale = Math.Clamp(ConfigManager.View.GetInt(ScaleKey, 4), 1, 8);
        Push();
        FitWindow();
        var (w, h) = ContentSize();
        Console.WriteLine($"[deception] aniso {GteDepth.Anisotropy}, mipmaps {(GteDepth.Mipmaps ? "on" : "off")}, " +
                          $"frame {(_wide ? "16:9" : "4:3")}, borders {(_wide && !_hide ? "on" : "off")}, " +
                          $"window {_scale}x ({w}x{h})");
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

        FillScaleLabels();
        int scale = _scale - 1;
        if (ImGui.Combo("Window size", ref scale, ScaleLabels, ScaleLabels.Length))
        {
            _scale = scale + 1;
            ConfigManager.View.SetInt(ScaleKey, _scale);
            ConfigManager.SaveView(PanelManager.Panels);
            FitWindow();
        }

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("The picture is 320×240 times this. Render scale is the detail inside it. The menu bar sits outside this size.");

        int frame = _wide ? 1 : 0;
        if (ImGui.Combo("Frame", ref frame, FrameLabels, FrameLabels.Length))
        {
            _wide = frame == 1;
            ConfigManager.View.SetString(FrameKey, _wide ? "16:9" : "4:3");
            ConfigManager.SaveView(PanelManager.Panels);
            Push();
            FitWindow();
        }

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("16:9 widens the window around the picture and draws the side art. 4:3 fits the window to the picture.");

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

    static void FitWindow()
    {
        var (w, h) = ContentSize();
        HostWindow.SetContentSize(w, h);
    }

    static (int W, int H) ContentSize()
    {
        int height = PictureH * _scale;
        int width = _wide ? (int)MathF.Round(height * SixteenNine) : PictureW * _scale;
        return (width, height);
    }

    static void FillScaleLabels()
    {
        for (int i = 1; i <= ScaleLabels.Length; i++)
        {
            int height = PictureH * i;
            int width = _wide ? (int)MathF.Round(height * SixteenNine) : PictureW * i;
            ScaleLabels[i - 1] = $"{i}x  {width}x{height}";
        }
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
