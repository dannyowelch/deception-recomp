using ImGuiNET;
using RecompOne.Runtime;
using RecompOne.Runtime.Config;
using RecompOne.Runtime.Events;
using RecompOne.Runtime.Hle;
using RecompOne.Runtime.Host.Window;

namespace Deception;

/// <summary>
/// Picture settings that the runtime already implements.
/// Anisotropy and mipmaps are uniforms on the next batch. 16:9 sets
/// <see cref="GpuHle.WideAspect"/> and leaves the game's clip tests alone.
/// While 16:9 is selected, one console line a second names the present and
/// the share of primitives that fell outside the draw clip.
/// A rectangle's second corner is the exclusive edge, so a fade that fills
/// the clip is not counted as past it.
/// </summary>
public static class GraphicsOptions
{
    public const string AnisoKey = "Anisotropy";
    public const string MipKey = "Mipmaps";
    public const string AspectKey = "WideAspect";

    const float SixteenNine = 16f / 9f;

    static readonly int[] Levels = [1, 2, 4, 8, 16];
    static readonly string[] AnisoLabels = ["1 (off)", "2", "4", "8", "16"];
    static readonly string[] AspectLabels = ["4:3", "16:9"];

    static bool _census;
    static long _windowStart;
    static int _prims, _past, _fullWidth;
    static int _clipW, _clipH, _clipVotes;
    static int _altW, _altH, _altVotes;
    static int _kinds;
    static int _walkSamples, _idleSamples;

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
        bool wide = ConfigManager.View.GetString(AspectKey, "4:3") == "16:9";
        GpuHle.WideAspect = wide ? SixteenNine : 0f;
        if (wide) Arm(); else Disarm();
        Console.WriteLine($"[deception] aniso {GteDepth.Anisotropy}, mipmaps {(GteDepth.Mipmaps ? "on" : "off")}, " +
                          $"aspect {(wide ? "16:9" : "4:3")}");
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

        int aspect = GpuHle.WideAspect > 1.5f ? 1 : 0;
        if (ImGui.Combo("Aspect", ref aspect, AspectLabels, AspectLabels.Length))
        {
            bool wide = aspect == 1;
            GpuHle.WideAspect = wide ? SixteenNine : 0f;
            ConfigManager.View.SetString(AspectKey, wide ? "16:9" : "4:3");
            ConfigManager.SaveView(PanelManager.Panels);
            if (wide) Arm(); else Disarm();
        }

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("16:9 shows columns beside the 320-wide picture when geometry reaches them. 4:3 is the console frame.");
    }

    static void Arm()
    {
        if (_census) return;
        _census = true;
        _windowStart = Environment.TickCount64;
        ResetCounts();
        Event.AddListener<RenderPrimEvent>(OnPrim);
        Event.AddListener<VSyncEvent>(OnVSync);
    }

    static void Disarm()
    {
        if (!_census) return;
        _census = false;
        Event.RemoveListener<RenderPrimEvent>(OnPrim);
        Event.RemoveListener<VSyncEvent>(OnVSync);
    }

    static void OnPrim(RenderPrimEvent e)
    {
        int clipW = e.DrawRight - e.DrawLeft + 1;
        int clipH = e.DrawBottom - e.DrawTop + 1;
        if (clipW <= 0 || e.Count <= 0) return;

        NoteClip(clipW, clipH);
        _prims++;

        int lo = int.MaxValue, hi = int.MinValue;
        for (int i = 0; i < e.Count; i++)
        {
            if (e.X[i] < lo) lo = e.X[i];
            if (e.X[i] > hi) hi = e.X[i];
        }

        // A rectangle stores the exclusive far corner. A polygon's vertices are inclusive.
        int right = e.Count == 2 ? hi - 1 : hi;
        if (lo < e.DrawLeft || right > e.DrawRight) _past++;

        int overlap = Math.Min(right, e.DrawRight) - Math.Max(lo, e.DrawLeft) + 1;
        if (overlap * 10 >= clipW * 9) _fullWidth++;
    }

    static void OnVSync(VSyncEvent _)
    {
        if (GpuHle.WideAspect <= 0f) return;
        if (InputSupport.Walking) _walkSamples++; else _idleSamples++;
        _kinds |= KindBit(Volatile.Read(ref GpuHle.PresentKind));

        long now = Environment.TickCount64;
        if (now - _windowStart < 1000) return;

        int prims = _prims, past = _past, full = _fullWidth;
        int clipW = _clipW, clipH = _clipH, other = _altVotes, kinds = _kinds;
        int walk = _walkSamples, idle = _idleSamples;
        ResetCounts();
        _windowStart = now;

        string share = prims == 0
            ? "no primitives"
            : $"past {past * 100.0 / prims:F1}% of {prims} prims (clip {clipW}x{clipH}{(other > 0 ? $", {other} on another clip" : "")})";
        string where = walk > 0 && idle > 0 ? "walk+other" : walk > 0 ? "walk" : "not-walk";
        Console.WriteLine($"[deception] present {KindText(kinds)}; display {GpuHle.LastDisplayW}x{GpuHle.LastDisplayH}; " +
                          $"margin {GpuHle.WideMargin(GpuHle.LastDisplayW > 0 ? GpuHle.LastDisplayW : 320)}; " +
                          $"{share}; full-width {full}; {where}; " +
                          $"mipfull {GteDepth.MipFull}; aniso {GteDepth.Anisotropy}" +
                          $"{(GteDepth.AnisotropyLive ? "" : " (uniform missing)")}" +
                          $"{(GteDepth.Mipmaps ? (GteDepth.MipmapsLive ? "" : " (no atlas)") : " (mipmaps off)")}");
    }

    static void NoteClip(int w, int h)
    {
        if (_clipVotes == 0 || (w == _clipW && h == _clipH))
        {
            _clipW = w;
            _clipH = h;
            _clipVotes++;
            return;
        }

        if (_altVotes == 0 || (w == _altW && h == _altH))
        {
            _altW = w;
            _altH = h;
            _altVotes++;
        }

        if (_altVotes > _clipVotes)
            (_clipW, _clipH, _clipVotes, _altW, _altH, _altVotes) =
                (_altW, _altH, _altVotes, _clipW, _clipH, _clipVotes);
    }

    static void ResetCounts()
    {
        _prims = _past = _fullWidth = 0;
        _clipW = _clipH = _clipVotes = 0;
        _altW = _altH = _altVotes = 0;
        _kinds = 0;
        _walkSamples = _idleSamples = 0;
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

    static int KindBit(int kind) => kind switch
    {
        -1 => 1,
        0 => 2,
        1 => 4,
        2 => 8,
        3 => 16,
        _ => 32,
    };

    static string KindText(int bits)
    {
        if (bits == 0 || bits == 32) return "pending";
        string text = "";
        void Add(string name)
        {
            if (text.Length > 0) text += "+";
            text += name;
        }
        if ((bits & 8) != 0) Add("wide");
        if ((bits & 4) != 0) Add("plain");
        if ((bits & 2) != 0) Add("vram fallback");
        if ((bits & 1) != 0) Add("vram fallback (latch refused)");
        if ((bits & 16) != 0) Add("vram fallback (24-bit)");
        return text.Length == 0 ? "pending" : text;
    }
}
