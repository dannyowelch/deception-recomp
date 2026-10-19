using System.Reflection;
using RecompOne.Runtime.Config;
using RecompOne.Runtime.Context;
using RecompOne.Runtime.Events;
using RecompOne.Runtime.Hardware;
using RecompOne.Runtime.Host;
using RecompOne.Runtime.Host.Window;
using RecompOne.Runtime.Memory;
using RecompOne.Runtime.Modding;

namespace Deception;

/// <summary>
/// Walk-versus-menu input, plus stick and mouse magnitude on the walk step.
/// The walking flag is set when the player's free-roam mover, bin_3dmd
/// func_800DBBCC, ran during the current scheduler pass. That function is
/// called from kokumei thread func_8002076C, and the game itself skips the
/// call when halfword 0x80086BBA has bit 0x0010 set. The mode-menu mover
/// func_800C186C also sets the flag; it is not the control state. The flag is
/// published at the start of kokumei func_80016028. Guest threads run before
/// that sample, so the bit the mover just earned is the bit the next frame reads.
///
/// Free-roam magnitude is applied in func_800DBD58, after it has added its
/// digital step and before the caller rotates and probes. Full deflection
/// matches the halfword that function just wrote (turn is 50, or 66 while
/// running). A stick between the deadzone and the digital threshold uses the
/// last full step that axis produced. An idle stick leaves the game halfword,
/// so the D-pad and the keyboard stay full-step. Mouse X adds turn and may
/// exceed that step. Mouse Y is not applied here: free-roam look is still the
/// digital L2/R2 step. The mode-menu path (180 / 143 / 66 / 32, including
/// mouse Y) stays on func_800C4988 and func_800C44BC.
/// Menus and the scripted pad stream are not touched, and PAD_dr is not blocked.
/// </summary>
public static class InputSupport
{
    static readonly ModInfo Self = new()
    {
        Id = "deception.input",
        Name = "Deception input",
        Version = "1",
    };

    // Edge of the deadzone is 0 and full deflection is 1. 0.15 is Verdite2's look
    // deadzone. The digital stick bits still use the host's 16000 threshold, so a
    // deflection between the two is a small step the buttons never asked for.
    const float Deadzone = 0.15f;

    const float ForwardStep = 180f;
    const float StrafeStep = 143f;
    const float TurnStep = 66f;
    const float LookStep = 32f;

    // One digital turn is about eight pixels. The cap is three of those steps in
    // a frame. Look's whole band is 256 units, so four units a pixel and a cap of
    // 96 cross it in about three frames instead of the shoulder buttons' eight.
    const float MouseTurnPerPixel = 8f;
    const float MouseTurnCap = 200f;
    const float MouseLookPerPixel = 4f;
    const float MouseLookCap = 96f;

    // Look is stored at player+0x14. Spawn writes −256, and the game's own clamp
    // snaps every halfword above 0xFF00 back to −256, so looking up does nothing
    // until the player has looked down. The band is not widened.
    const int LookMin = -512;
    const ushort LookTop = 0xFF00;

    static volatile bool _latched;
    static volatile bool _walking;

    static float _fwdCarry, _strafeCarry, _turnCarry, _lookCarry;
    static int _fwdFull, _strafeFull, _turnFull;
    static float _mouseLook;
    static uint _lookPtr;
    static short _lookBefore;
    static uint _freeVecA, _freeVecB;

    // func_800DBD58 adds these halfwords onto the two stack vectors, then the
    // caller rotates them. They are the digital step for this frame.
    const uint FreeForwardDelta = 0x80065844;
    const uint FreeStrafeDelta = 0x80065854;
    const uint FreeTurnDelta = 0x80065864;

    /// <summary>True when the player was walking on the last completed scheduler pass.</summary>
    public static bool Walking => _walking;

    public static void Install()
    {
        MigrateStockKeys();

        // Port 0 ignores PadKind. Pad is the menu set and PadWalk is the walk
        // set. Both keep the default axis indices, so the four axis bytes stay
        // on LeftX/LeftY/RightX/RightY. Magnitude reads the raw floats, not the
        // button bits, so either table still scales.
        ConfigManager.Port0PadBindings = () =>
        {
            var game = ConfigManager.Game;
            if (!Walking) return game.Pad ?? new GamepadBindings();
            return game.PadWalk ?? GamepadBindings.Walk();
        };

        // A click locks the pointer. Escape, a host popup, or leaving the walk
        // gives it back. The listeners run on the window thread, which is the
        // only thread GLFW will change the cursor on.
        Event.AddListener<MouseEvent>(OnMouse);
        Event.AddListener<KeyboardEvent>(OnKeyboard);
        HostWindow.FocusChanged += focused =>
        {
            if (!focused) HostWindow.MouseCaptured = false;
        };

        SymbolRegistry.Build();
        var flags = BindingFlags.Public | BindingFlags.Static;
        var self = typeof(InputSupport);
        var hooked = false;

        var roam = SymbolRegistry.Resolve("bin_3dmd", null, 0x800DBBCCu);
        var menuWalk = SymbolRegistry.Resolve("bin_3dmd", null, 0x800C186Cu);
        var sample = SymbolRegistry.Resolve("kokumei", null, 0x80016028u);
        var roamOk = roam?.Name == "func_800DBBCC";
        var menuWalkOk = menuWalk?.Name == "func_800C186C";
        if (sample?.Name == "func_80016028" && (roamOk || menuWalkOk))
        {
            if (roamOk)
                HookManager.AddPost(Self, roam!, self.GetMethod(nameof(OnWalk), flags)!);
            if (menuWalkOk)
                HookManager.AddPost(Self, menuWalk!, self.GetMethod(nameof(OnWalk), flags)!);
            HookManager.AddPre(Self, sample, self.GetMethod(nameof(OnSample), flags)!);
            hooked = true;
            Console.WriteLine(
                "[Deception] input: walk detector hooked " +
                (roamOk ? "func_800DBBCC" : "missing func_800DBBCC") +
                " (free roam) and " +
                (menuWalkOk ? "func_800C186C" : "missing func_800C186C") +
                " (mode menu), sampled at func_80016028.");
        }
        else
        {
            Console.Error.WriteLine(
                $"[Deception] input: walk detector did not resolve " +
                $"(bin_3dmd 0x800DBBCC={(roam == null ? "missing" : roam.Name)}, " +
                $"bin_3dmd 0x800C186C={(menuWalk == null ? "missing" : menuWalk.Name)}, " +
                $"kokumei 0x80016028={(sample == null ? "missing" : sample.Name)}). " +
                "Port 0 stays on the menu pad bindings.");
        }

        var freeStep = SymbolRegistry.Resolve("bin_3dmd", null, 0x800DBD58u);
        if (freeStep?.Name == "func_800DBD58")
        {
            HookManager.AddPre(Self, freeStep, self.GetMethod(nameof(OnFreeMoveBefore), flags)!);
            HookManager.AddPost(Self, freeStep, self.GetMethod(nameof(OnFreeMove), flags)!);
            hooked = true;
            Console.WriteLine(
                "[Deception] input: free-roam stick scales the step func_800DBD58 just wrote. " +
                "Mouse X turns. Look up and down stay on L2/R2. " +
                "Mouse look is click to capture, Escape releases.");
        }
        else
        {
            Console.Error.WriteLine(
                $"[Deception] input: free-roam step hook did not resolve " +
                $"(func_800DBD58={(freeStep == null ? "missing" : freeStep.Name)}).");
        }

        var move = SymbolRegistry.Resolve("bin_3dmd", null, 0x800C4988u);
        var look = SymbolRegistry.Resolve("bin_3dmd", null, 0x800C44BCu);
        var moveOk = move?.Name == "func_800C4988";
        var lookOk = look?.Name == "func_800C44BC";
        if (moveOk)
            HookManager.AddPost(Self, move!, self.GetMethod(nameof(OnMove), flags)!);
        if (lookOk)
        {
            HookManager.AddPre(Self, look!, self.GetMethod(nameof(OnLookBefore), flags)!);
            HookManager.AddPost(Self, look!, self.GetMethod(nameof(OnLook), flags)!);
        }

        if (moveOk && lookOk)
        {
            hooked = true;
            Console.WriteLine(
                "[Deception] input: mode-menu stick scales that step (full deflection is " +
                "180 forward, 143 strafe, 66 turn, 32 look).");
        }
        else
        {
            Console.Error.WriteLine(
                $"[Deception] input: mode-menu analog hooks did not resolve " +
                $"(func_800C4988={(move == null ? "missing" : move.Name)}, " +
                $"func_800C44BC={(look == null ? "missing" : look.Name)}).");
        }

        if (hooked) HookManager.Commit();
    }

    /// <summary>Post-hook. The walk/look function ran, so this pass is a walk pass.</summary>
    public static void OnWalk(CpuContext c, IMemory m) => _latched = true;

    /// <summary>
    /// Pre-hook on the scheduler's pad sample. Publish the latch, release the
    /// pointer when this pass was not a walk, then clear the latch. Returning
    /// normally lets func_80016028, including PAD_dr, run.
    /// </summary>
    public static void OnSample(CpuContext c, IMemory m)
    {
        var now = _latched;
        _latched = false;
        _walking = now;

        // The walk hooks already consumed this pass's motion. Drain here only
        // when they did not run, so menu motion does not become the first step.
        if (!now)
        {
            HostWindow.TakeMouseMotion();
            _fwdFull = _strafeFull = _turnFull = 0;
        }
        if (!now || !HostWindow.Focused || PopupManager.AnyOpen ||
            HostWindow.IsKeyDown(Silk.NET.Input.Key.Escape))
        {
            if (HostWindow.MouseCaptured) HostWindow.MouseCaptured = false;
        }
    }

    /// <summary>Pre-hook. func_800DBD58 clobbers A0 and A1 before it returns.</summary>
    public static void OnFreeMoveBefore(CpuContext c, IMemory m)
    {
        _freeVecA = c.A0;
        _freeVecB = c.A1;
    }

    /// <summary>
    /// Post-hook on the free-roam step. The digital halfwords are already in
    /// the vectors. A live stick replaces that contribution with a fraction of
    /// the same magnitude. Mouse X is added to the turn.
    /// </summary>
    public static void OnFreeMove(CpuContext c, IMemory m)
    {
        _mouseLook = 0f;
        var turnMouse = 0f;
        if (HostWindow.MouseCaptured)
        {
            // Y is taken so it does not leak into a later menu frame. Free-roam
            // look is not this vector; L2 and R2 still own it.
            var (dx, _) = HostWindow.TakeMouseMotion();
            turnMouse = Math.Clamp(dx * MouseTurnPerPixel, -MouseTurnCap, MouseTurnCap);
        }
        else
        {
            HostWindow.TakeMouseMotion();
        }

        ScaleInto(m, _freeVecA, 4u, FreeForwardDelta, Shape(-Controller.LeftAxisY), ref _fwdCarry, ref _fwdFull, 0f);
        ScaleInto(m, _freeVecA, 0u, FreeStrafeDelta, Shape(Controller.LeftAxisX), ref _strafeCarry, ref _strafeFull, 0f);
        ScaleInto(m, _freeVecB, 2u, FreeTurnDelta, Shape(Controller.RightAxisX), ref _turnCarry, ref _turnFull, turnMouse);
    }

    static void OnMouse(MouseEvent e)
    {
        if (e.Action != MouseAction.Button || !e.Pressed || e.Button != MouseButton.Left) return;
        if (!_walking || HostWindow.MouseCaptured || PopupManager.AnyOpen || !HostWindow.Focused) return;
        HostWindow.MouseCaptured = true;
    }

    static void OnKeyboard(KeyboardEvent e)
    {
        if (!e.Pressed || e.Repeat || e.Key != (int)Silk.NET.Input.Key.Escape) return;
        HostWindow.MouseCaptured = false;
    }

    /// <summary>
    /// Post-hook on the step writer. Replaces a component the stick is driving
    /// and adds mouse X to the turn. vecA+2, vecB+0, and vecB+4 stay 0.
    /// </summary>
    public static void OnMove(CpuContext c, IMemory m)
    {
        _mouseLook = 0f;
        var turnMouse = 0f;
        if (HostWindow.MouseCaptured)
        {
            var (dx, dy) = HostWindow.TakeMouseMotion();
            turnMouse = Math.Clamp(dx * MouseTurnPerPixel, -MouseTurnCap, MouseTurnCap);
            // Window Y grows downward. Look-up is positive, the same way L2 is.
            _mouseLook = Math.Clamp(-dy * MouseLookPerPixel, -MouseLookCap, MouseLookCap);
        }
        else
        {
            HostWindow.TakeMouseMotion();
        }

        if (c.A0 != 0)
        {
            // Stick up is a negative LeftY. Forward is the positive halfword.
            Apply(m, c.A0 + 4u, Shape(-Controller.LeftAxisY), ForwardStep, ref _fwdCarry);
            Apply(m, c.A0 + 0u, Shape(Controller.LeftAxisX), StrafeStep, ref _strafeCarry);
        }
        else
        {
            _fwdCarry = _strafeCarry = 0f;
        }

        if (c.A1 != 0)
            Apply(m, c.A1 + 2u, Shape(Controller.RightAxisX), TurnStep, ref _turnCarry, turnMouse);
        else
            _turnCarry = 0f;
    }

    /// <summary>Pre-hook. The look halfword is about to gain the digital ±32.</summary>
    public static void OnLookBefore(CpuContext c, IMemory m)
    {
        _lookPtr = c.A0;
        _lookBefore = c.A0 == 0 ? (short)0 : ReadI16(m, c.A0 + 0x14u);
    }

    /// <summary>
    /// Post-hook. A live right stick replaces the ±32 this function just applied.
    /// Mouse Y is added to whichever value won, then the game's own clamp runs.
    /// </summary>
    public static void OnLook(CpuContext c, IMemory m)
    {
        var ptr = _lookPtr != 0 ? _lookPtr : c.A0;
        var mouse = _mouseLook;
        _mouseLook = 0f;
        if (ptr == 0)
        {
            _lookCarry = 0f;
            return;
        }

        var addr = ptr + 0x14u;
        // Stick up is a negative RightY and matches L2, which adds to the look.
        var ry = Shape(-Controller.RightAxisY);
        int look;
        if (ry == 0f && mouse == 0f)
        {
            _lookCarry = 0f;
            look = ReadI16(m, addr);
        }
        else
        {
            var basis = ry != 0f ? _lookBefore : (int)ReadI16(m, addr);
            var sum = (ry != 0f ? ry * LookStep : 0f) + mouse + _lookCarry;
            var whole = (int)sum;
            _lookCarry = sum - whole;
            look = basis + whole;
        }

        m.WriteU16(addr, (ushort)(short)ClampLook(look));
    }

    /// <summary>
    /// An untouched RecompOne keyboard file still names one key per button.
    /// That loads, and when it is exactly <see cref="KeyBindings.Stock"/> it is
    /// rewritten to the walk layout so the new defaults actually take effect.
    /// A file that differs in any button is left as the player saved it.
    /// </summary>
    static void MigrateStockKeys()
    {
        if (!KeyBindings.Matches(ConfigManager.Game.Keys, KeyBindings.Stock())) return;

        ConfigManager.Game.Keys = new KeyBindings();
        ConfigManager.SaveGame();
        Console.WriteLine(
            "[Deception] keys: stock keyboard layout migrated to the walk defaults " +
            "(W/S move, A/D strafe, Q/E turn, arrows stay the D-pad).");
    }

    /// <summary>
    /// Replaces the digital delta <paramref name="deltaAddr"/> that func_800DBD58
    /// already added to <paramref name="vec"/>+<paramref name="offset"/>. An idle
    /// stick keeps that delta. A live stick writes the same magnitude times the
    /// shaped axis, using the last nonzero delta when this frame's button was off.
    /// </summary>
    static void ScaleInto(
        IMemory m, uint vec, uint offset, uint deltaAddr, float shaped,
        ref float carry, ref int learned, float extra)
    {
        if (vec == 0)
        {
            carry = 0f;
            return;
        }

        var game = (int)ReadI16(m, deltaAddr);
        if (game != 0) learned = Math.Abs(game);
        if (shaped == 0f && extra == 0f)
        {
            carry = 0f;
            return;
        }

        float sum;
        int desired;
        if (shaped != 0f)
        {
            var full = learned != 0 ? learned : Math.Abs(game);
            if (full == 0 && extra == 0f)
            {
                carry = 0f;
                return;
            }

            sum = shaped * full + extra + carry;
            desired = (int)sum;
        }
        else
        {
            sum = extra + carry;
            desired = game + (int)sum;
        }

        var whole = (int)sum;
        carry = sum - whole;
        var fix = desired - game;
        if (fix != 0)
            WriteI16(m, vec + offset, ReadI16(m, vec + offset) + fix);
    }

    /// <summary>Past the deadzone, remapped so the edge is 0 and ±1 is full.</summary>
    static float Shape(float axis)
    {
        var mag = MathF.Abs(axis);
        if (!(mag > Deadzone)) return 0f;
        var t = (mag - Deadzone) / (1f - Deadzone);
        if (t > 1f) t = 1f;
        return MathF.CopySign(t, axis);
    }

    /// <summary>
    /// A live stick overwrites the halfword (the game already added the full
    /// digital step). An idle stick keeps it and <paramref name="extra"/>, the
    /// mouse, is added. The fractional part is carried so a slow deflection does
    /// not truncate to nothing; the carry is dropped when nothing is driving.
    /// The cast truncates toward zero.
    /// </summary>
    static void Apply(IMemory m, uint addr, float shaped, float full, ref float carry, float extra = 0f)
    {
        if (shaped == 0f && extra == 0f)
        {
            carry = 0f;
            return;
        }

        var basis = shaped != 0f ? 0 : (int)ReadI16(m, addr);
        var sum = shaped * full + extra + carry;
        var whole = (int)sum;
        carry = sum - whole;
        WriteI16(m, addr, basis + whole);
    }

    /// <summary>func_800C44BC's two compares: below −512 becomes −512, and an
    /// unsigned halfword above 0xFF00 becomes −256. Positive values stay.</summary>
    static int ClampLook(int value)
    {
        if (value < LookMin) return LookMin;
        if (value > short.MaxValue) value = short.MaxValue;
        var bits = (ushort)(short)value;
        if (bits > LookTop) return -256;
        return (short)bits;
    }

    static short ReadI16(IMemory m, uint addr) => (short)m.ReadU16(addr);

    static void WriteI16(IMemory m, uint addr, int value)
    {
        if (value > short.MaxValue) value = short.MaxValue;
        else if (value < short.MinValue) value = short.MinValue;
        m.WriteU16(addr, (ushort)(short)value);
    }
}
