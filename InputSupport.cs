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
/// Free-roam forward, strafe, and yaw are applied in func_800DBD58, after it
/// has added its digital step. Full deflection matches the halfword that
/// function just wrote (turn is 50, or 66 while running). Free-roam look is
/// func_800DBFB4, which runs after func_800DBBCC on the same player. Its step
/// is 32, or 64 when pad bit 0x0040 is set, and its clamp is −512..768.
/// The mode-menu path (180 / 143 / 66 / 32) stays on func_800C4988 and
/// func_800C44BC, whose look clamp still snaps an unsigned halfword above
/// 0xFF00 back to −256. A stick between the deadzone and the digital threshold
/// uses the last full step that axis produced. An idle stick leaves the game
/// halfword, so the D-pad and the keyboard stay full-step.
///
/// Mouse motion is summed by the host until the first walk hook of a logic
/// pass takes it. Later hooks in that pass read the same snapshot. Yaw reads
/// X once. Each look path reads Y once, so a mode-menu look cannot spend the
/// free-roam look. The sample hook drains a pass that never took, so menu
/// motion does not become the next walk step, and it does not drain during a
/// walk pass. Capture hides the cursor and locks it inside the window:
/// HostWindow asks for raw mode, which is GLFW's disabled cursor plus raw
/// motion, and falls back to the disabled cursor. Escape captures and
/// releases while the window is focused and no host popup is open. Dialog
/// and a door keep the pointer, because the mover stopping is not a menu.
/// A host popup, losing focus, or turning mouse look off releases. A click
/// does not capture.
/// Scripted pad playback (0x80086BBA bit 0x0200) leaves the digital steps and
/// still consumes the pass's mouse. A cleared Analog setting leaves the stick
/// steps; mouse look is its own setting. Playback also keeps the retail rule
/// that a blocked mode-menu probe drops the turn. Outside playback, that
/// dropped turn is written to player+0xE with the mover's 12-bit mask. The
/// position stays blocked. Free-roam already adds its turn after the probe,
/// so this split is only on func_800C4554. A scripted pitch return
/// (0x80101490) is left alone until the player looks, and that look cancels it.
/// </summary>
public static class InputSupport
{
    static readonly ModInfo Self = new()
    {
        Id = "deception.input",
        Name = "Deception input",
        Version = "1",
    };

    const float ForwardStep = 180f;
    const float StrafeStep = 143f;
    const float TurnStep = 66f;
    const float LookStep = 32f;

    // One digital turn is about eight pixels. Look moves four units a pixel.
    // MouseSensitivityX / Y scale those rates. The stored halfword is the limit.
    const float MouseTurnPerPixel = 8f;
    const float MouseLookPerPixel = 4f;

    // Mode-menu look, func_800C44BC. Spawn writes −256. Below −512 becomes
    // −512, and an unsigned halfword above 0xFF00 becomes −256.
    const int MenuLookMin = -512;
    const ushort MenuLookTop = 0xFF00;

    // Free-roam look, func_800DBFB4. Below −512 becomes −512. Below 769 stays.
    // Anything else becomes 768. This band is wider than the mode menu.
    const int FreeLookMin = -512;
    const int FreeLookMax = 768;

    const uint PlaybackWord = 0x80086BBAu;
    const ushort PlaybackBit = 0x0200;
    const uint LookScriptPointer = 0x80101490u;
    const uint PadWord = 0x800A1AE8u;
    const ushort FreeLookFastBit = 0x0040;

    static volatile bool _latched;
    static volatile bool _walking;
    static bool _inPlayerMove;

    static float _fwdCarry, _strafeCarry, _turnCarry, _menuLookCarry, _freeLookCarry;
    static int _fwdFull, _strafeFull, _turnFull;
    static uint _lookPtr;
    static short _lookBefore;
    static uint _freeLookPtr;
    static short _freeLookBefore;
    static uint _freeVecA, _freeVecB;

    // func_800C4554 drops the turn with the move when the probe returns 1.
    // The pre saves the yaw; OnMove saves the turn halfword after any analog
    // replace; the post writes it back only when the function itself did not.
    static uint _menuPlayer;
    static short _menuYaw;
    static short _menuTurn;

    // One take per logic pass. Yaw is spent once. Each look path spends Y on
    // its own pitch, so one path cannot drop the other's.
    static bool _motionTaken;
    static float _tickDx, _tickDy;
    static bool _turnUsed, _freeLookSpent, _menuLookSpent;

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
        EnsureMenuKeys();

        // Port 0 ignores PadKind. Pad is the menu set and PadWalk is the walk
        // set. Both keep the default axis indices, so the four axis bytes stay
        // on LeftX/LeftY/RightX/RightY. Magnitude reads the raw floats, not the
        // button bits, so either table still scales. The keyboard splits on
        // the same walk latch: Keys while walking, KeysMenu otherwise.
        ConfigManager.Port0PadBindings = () =>
        {
            var game = ConfigManager.Game;
            if (!Walking) return game.Pad ?? new GamepadBindings();
            return game.PadWalk ?? GamepadBindings.Walk();
        };
        ConfigManager.Port0KeyBindings = () =>
        {
            var game = ConfigManager.Game;
            if (!Walking) return game.KeysMenu ?? KeyBindings.Menu();
            return game.Keys ?? new KeyBindings();
        };

        // Escape toggles the pointer while focused and mouse look is on. A
        // host popup or focus loss releases it. Dialog does not. The listener
        // runs on the window thread, which is the only thread GLFW will
        // change the cursor on.
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
            {
                HookManager.AddPre(Self, roam!, self.GetMethod(nameof(OnPlayerMoveBefore), flags)!);
                HookManager.AddPost(Self, roam!, self.GetMethod(nameof(OnWalk), flags)!);
            }
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
        }
        else
        {
            Console.Error.WriteLine(
                $"[Deception] input: free-roam step hook did not resolve " +
                $"(func_800DBD58={(freeStep == null ? "missing" : freeStep.Name)}).");
        }

        var freeLook = SymbolRegistry.Resolve("bin_3dmd", null, 0x800DBFB4u);
        if (freeLook?.Name == "func_800DBFB4")
        {
            HookManager.AddPre(Self, freeLook, self.GetMethod(nameof(OnFreeLookBefore), flags)!);
            HookManager.AddPost(Self, freeLook, self.GetMethod(nameof(OnFreeLook), flags)!);
            hooked = true;
            Console.WriteLine(
                "[Deception] input: free-roam stick scales the step func_800DBD58 just wrote, " +
                "and look scales the step func_800DBFB4 just wrote. " +
                "Mouse X turns and mouse Y looks. Escape captures and releases.");
        }
        else
        {
            Console.Error.WriteLine(
                $"[Deception] input: free-roam look hook did not resolve " +
                $"(func_800DBFB4={(freeLook == null ? "missing" : freeLook.Name)}).");
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

        var menuBody = SymbolRegistry.Resolve("bin_3dmd", null, 0x800C4554u);
        if (menuBody?.Name == "func_800C4554")
        {
            HookManager.AddPre(Self, menuBody, self.GetMethod(nameof(OnMenuMoveBefore), flags)!);
            HookManager.AddPost(Self, menuBody, self.GetMethod(nameof(OnMenuMoveAfter), flags)!);
            hooked = true;
            Console.WriteLine(
                "[Deception] input: a blocked mode-menu probe still applies the turn at player+0xE.");
        }
        else
        {
            Console.Error.WriteLine(
                $"[Deception] input: mode-menu mover hook did not resolve " +
                $"(func_800C4554={(menuBody == null ? "missing" : menuBody.Name)}).");
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

    /// <summary>Pre-hook. Only the step inside func_800DBBCC may spend mouse yaw.</summary>
    public static void OnPlayerMoveBefore(CpuContext c, IMemory m) => _inPlayerMove = true;

    /// <summary>Post-hook. The walk/look function ran, so this pass is a walk pass.</summary>
    public static void OnWalk(CpuContext c, IMemory m)
    {
        _latched = true;
        _inPlayerMove = false;
    }

    /// <summary>
    /// Pre-hook on the scheduler's pad sample. Publish the latch, then clear
    /// it. Dialog and a door stop the mover and keep the pointer. Returning
    /// normally lets func_80016028, including PAD_dr, run.
    /// </summary>
    public static void OnSample(CpuContext c, IMemory m)
    {
        var now = _latched;
        _latched = false;
        _walking = now;
        _inPlayerMove = false;

        // The walk hooks already consumed this pass's motion. Drain here only
        // when they did not run, so menu motion does not become the first step.
        // A walk pass leaves whatever arrived after the mover for the next tick.
        if (!now && !_motionTaken)
            HostWindow.TakeMouseMotion();
        if (!now)
            _fwdFull = _strafeFull = _turnFull = 0;

        _motionTaken = false;
        _tickDx = _tickDy = 0f;
        _turnUsed = _freeLookSpent = _menuLookSpent = false;

        // Escape toggles on its own edge. A held Escape must not undo that.
        // The mover stops for dialog and for a door. That is not a menu, so
        // the pointer stays. Focus loss, mouse look off, or a host popup
        // releases it.
        var mouse = MouseOn();
        if (!mouse || !HostWindow.Focused || PopupManager.AnyOpen)
        {
            if (HostWindow.MouseCaptured) HostWindow.MouseCaptured = false;
        }

        // Show the label while walking, and while the pointer is still held
        // through dialog or a door. A host popup hides it.
        HostWindow.MouseCaptureHud = mouse && !PopupManager.AnyOpen &&
                                      (now || HostWindow.MouseCaptured);
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
    /// the same magnitude. Mouse X is added to the turn on the player call.
    /// </summary>
    public static void OnFreeMove(CpuContext c, IMemory m)
    {
        if (PlaybackOn(m))
        {
            if (_inPlayerMove)
            {
                DiscardMotion();
                _fwdCarry = _strafeCarry = _turnCarry = 0f;
            }
            return;
        }

        var turnMouse = 0f;
        if (_inPlayerMove)
            turnMouse = TakeTurnMouse();

        if (!ConfigManager.Game.Analog)
        {
            _fwdCarry = _strafeCarry = 0f;
            if (_inPlayerMove && turnMouse != 0f)
                ScaleInto(m, _freeVecB, 2u, FreeTurnDelta, 0f, ref _turnCarry, ref _turnFull, turnMouse);
            else
                _turnCarry = 0f;
            return;
        }

        ScaleInto(m, _freeVecA, 4u, FreeForwardDelta, Shape(-Controller.LeftAxisY), ref _fwdCarry, ref _fwdFull, 0f);
        ScaleInto(m, _freeVecA, 0u, FreeStrafeDelta, Shape(Controller.LeftAxisX), ref _strafeCarry, ref _strafeFull, 0f);
        ScaleInto(m, _freeVecB, 2u, FreeTurnDelta, Shape(Controller.RightAxisX), ref _turnCarry, ref _turnFull, turnMouse);
    }

    /// <summary>Pre-hook. The free-roam look halfword is about to change.</summary>
    public static void OnFreeLookBefore(CpuContext c, IMemory m)
    {
        _freeLookPtr = c.A0;
        _freeLookBefore = c.A0 == 0 ? (short)0 : ReadI16(m, c.A0 + 0x14u);
    }

    /// <summary>
    /// Post-hook on func_800DBFB4. A scripted pitch return (pointer 0x80101490)
    /// is left alone until the player looks, and that look clears it. Otherwise
    /// a live stick or a mouse replaces the step from the pitch this call
    /// started with, then this function's own clamp runs.
    /// </summary>
    public static void OnFreeLook(CpuContext c, IMemory m)
    {
        if (PlaybackOn(m))
        {
            DiscardMotion();
            _freeLookCarry = 0f;
            return;
        }

        var mouse = TakeLookMouse(ref _freeLookSpent);
        var ry = ConfigManager.Game.Analog ? LookStick() : 0f;
        var ptr = _freeLookPtr != 0 ? _freeLookPtr : c.A0;
        if (ptr == 0)
        {
            _freeLookCarry = 0f;
            return;
        }

        // The return-to-level ignores look buttons, and the old hook ate mouse
        // Y for as long as the pointer stayed set. Turn had already been
        // applied. Cancelling on a real look is what lets the mouse pitch
        // again without waiting for the stick.
        if (m.ReadU32(LookScriptPointer) != 0)
        {
            if (ry == 0f && mouse == 0f)
            {
                _freeLookCarry = 0f;
                return;
            }

            m.WriteU32(LookScriptPointer, 0);
            WriteFreeLook(m, ptr, ReadI16(m, ptr + 0x14u), ry, mouse);
            return;
        }

        if (ry == 0f && mouse == 0f)
        {
            _freeLookCarry = 0f;
            return;
        }

        WriteFreeLook(m, ptr, _freeLookBefore, ry, mouse);
    }

    static void WriteFreeLook(IMemory m, uint ptr, int basis, float ry, float mouse)
    {
        var step = (m.ReadU16(PadWord) & FreeLookFastBit) != 0 ? 64f : 32f;
        var sum = ry * step + mouse + _freeLookCarry;
        var whole = (int)sum;
        _freeLookCarry = sum - whole;
        m.WriteU16(ptr + 0x14u, (ushort)(short)ClampFreeLook(basis + whole));
    }

    static void OnKeyboard(KeyboardEvent e)
    {
        if (!e.Pressed || e.Repeat || e.Key != (int)Silk.NET.Input.Key.Escape) return;
        if (PopupManager.AnyOpen || !HostWindow.Focused || !MouseOn()) return;
        HostWindow.MouseCaptured = !HostWindow.MouseCaptured;
    }

    /// <summary>Pre-hook. func_800C4554 is about to build the move and the turn.</summary>
    public static void OnMenuMoveBefore(CpuContext c, IMemory m)
    {
        _menuPlayer = c.A0;
        _menuYaw = c.A0 == 0 ? (short)0 : ReadI16(m, c.A0 + 0xEu);
        _menuTurn = 0;
    }

    /// <summary>
    /// Post-hook. A probe return of 1 leaves both the move and the yaw unstored
    /// (bin_3dmd.cs L800C4970). The turn halfword is still the one func_800C4988
    /// and OnMove wrote. Apply that yaw with the same 12-bit mask the success
    /// path uses, and leave the position alone. Playback keeps the retail drop.
    /// A yaw the function already changed is left as it stored it.
    /// </summary>
    public static void OnMenuMoveAfter(CpuContext c, IMemory m)
    {
        if ((m.ReadU16(PlaybackWord) & PlaybackBit) != 0) return;
        if (_menuPlayer == 0 || _menuTurn == 0) return;
        var addr = _menuPlayer + 0xEu;
        if (ReadI16(m, addr) != _menuYaw) return;
        var applied = (_menuYaw + _menuTurn) & 0x0FFF;
        m.WriteU16(addr, (ushort)applied);
    }

    /// <summary>
    /// Post-hook on the mode-menu step writer. Replaces a component the stick
    /// is driving and adds mouse X to the turn. vecA+2, vecB+0, and vecB+4 stay 0.
    /// </summary>
    public static void OnMove(CpuContext c, IMemory m)
    {
        if (PlaybackOn(m))
        {
            DiscardMotion();
            _fwdCarry = _strafeCarry = _turnCarry = 0f;
            _menuTurn = c.A1 == 0 ? (short)0 : ReadI16(m, c.A1 + 2u);
            return;
        }

        var turnMouse = TakeTurnMouse();
        var sticks = ConfigManager.Game.Analog;
        if (c.A0 != 0 && sticks)
        {
            // Stick up is a negative LeftY. Forward is the positive halfword.
            Apply(m, c.A0 + 4u, Shape(-Controller.LeftAxisY), ForwardStep, ref _fwdCarry);
            Apply(m, c.A0 + 0u, Shape(Controller.LeftAxisX), StrafeStep, ref _strafeCarry);
        }
        else
        {
            _fwdCarry = _strafeCarry = 0f;
        }

        if (c.A1 != 0 && (sticks || turnMouse != 0f))
        {
            Apply(m, c.A1 + 2u, sticks ? Shape(Controller.RightAxisX) : 0f, TurnStep, ref _turnCarry, turnMouse);
            _menuTurn = ReadI16(m, c.A1 + 2u);
        }
        else if (c.A1 != 0)
        {
            _turnCarry = 0f;
            _menuTurn = ReadI16(m, c.A1 + 2u);
        }
        else
        {
            _turnCarry = 0f;
            _menuTurn = 0;
        }
    }

    /// <summary>Pre-hook. The look halfword is about to gain the digital ±32.</summary>
    public static void OnLookBefore(CpuContext c, IMemory m)
    {
        _lookPtr = c.A0;
        _lookBefore = c.A0 == 0 ? (short)0 : ReadI16(m, c.A0 + 0x14u);
    }

    /// <summary>
    /// Post-hook. A live right stick replaces the ±32 this function just applied.
    /// Mouse Y is added to whichever value won, then the mode-menu clamp runs.
    /// </summary>
    public static void OnLook(CpuContext c, IMemory m)
    {
        if (PlaybackOn(m))
        {
            DiscardMotion();
            _menuLookCarry = 0f;
            return;
        }

        var mouse = TakeLookMouse(ref _menuLookSpent);
        var ptr = _lookPtr != 0 ? _lookPtr : c.A0;
        if (ptr == 0)
        {
            _menuLookCarry = 0f;
            return;
        }

        var addr = ptr + 0x14u;
        var ry = ConfigManager.Game.Analog ? LookStick() : 0f;
        int look;
        if (ry == 0f && mouse == 0f)
        {
            _menuLookCarry = 0f;
            look = ReadI16(m, addr);
        }
        else
        {
            var basis = ry != 0f ? _lookBefore : (int)ReadI16(m, addr);
            var sum = (ry != 0f ? ry * LookStep : 0f) + mouse + _menuLookCarry;
            var whole = (int)sum;
            _menuLookCarry = sum - whole;
            look = basis + whole;
        }

        m.WriteU16(addr, (ushort)(short)ClampMenuLook(look));
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
    /// An older file has Keys and no KeysMenu. Keep that walk table. Fill the
    /// menu table in memory from it, and leave the file alone until a normal
    /// settings save.
    /// </summary>
    static void EnsureMenuKeys()
    {
        if (ConfigManager.GameFileHas("KeysMenu")) return;
        ConfigManager.Game.KeysMenu = KeyBindings.MenuFrom(ConfigManager.Game.Keys);
    }

    static bool MouseOn() => ConfigManager.Game.MouseLook;

    /// <summary>The scripted pad stream keeps the digital step and the mouse.</summary>
    static bool PlaybackOn(IMemory m) => (m.ReadU16(PlaybackWord) & PlaybackBit) != 0;

    /// <summary>The pass's mouse, taken on the first walk hook and reused after that.</summary>
    static void EnsureTick()
    {
        if (_motionTaken) return;
        (_tickDx, _tickDy) = HostWindow.TakeMouseMotion();
        _motionTaken = true;
    }

    /// <summary>Spend this pass's mouse without adding it, so it cannot burst later.</summary>
    static void DiscardMotion()
    {
        EnsureTick();
        _turnUsed = true;
        _freeLookSpent = true;
        _menuLookSpent = true;
    }

    /// <summary>dx times 8 times MouseSensitivityX. The fraction stays in the turn carry.</summary>
    static float TakeTurnMouse()
    {
        EnsureTick();
        if (_turnUsed) return 0f;
        _turnUsed = true;
        if (!MouseOn() || !HostWindow.MouseCaptured) return 0f;
        return _tickDx * MouseTurnPerPixel * SensitivityX();
    }

    /// <summary>
    /// Window Y grows downward. Uninverted look-up is negative dy times 4 times
    /// MouseSensitivityY. InvertLookY flips that sign. Each look path passes
    /// its own spent flag, and both read the same dy.
    /// </summary>
    static float TakeLookMouse(ref bool spent)
    {
        EnsureTick();
        if (spent) return 0f;
        spent = true;
        if (!MouseOn() || !HostWindow.MouseCaptured) return 0f;
        var sign = ConfigManager.Game.InvertLookY ? 1f : -1f;
        return sign * _tickDy * MouseLookPerPixel * SensitivityY();
    }

    static float SensitivityX()
    {
        var s = ConfigManager.Game.MouseSensitivityX;
        if (float.IsNaN(s) || s < 0f) return 0f;
        return s;
    }

    static float SensitivityY()
    {
        var s = ConfigManager.Game.MouseSensitivityY;
        if (float.IsNaN(s) || s < 0f) return 0f;
        return s;
    }

    /// <summary>Stick up is negative. Uninverted look-up is positive, matching L2.</summary>
    static float LookStick()
    {
        var axis = Controller.RightAxisY;
        if (!ConfigManager.Game.InvertLookY)
            axis = -axis;
        return Shape(axis);
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

    /// <summary>Past the deadzone, remapped so the edge is 0 and ±1 is full.
    /// StickCurve is a power on that remap. 1 skips the power.</summary>
    static float Shape(float axis)
    {
        var deadzone = Deadzone();
        var mag = MathF.Abs(axis);
        if (!(mag > deadzone)) return 0f;
        var t = (mag - deadzone) / (1f - deadzone);
        if (t > 1f) t = 1f;
        var curve = Curve();
        if (curve != 1f)
            t = MathF.Pow(t, curve);
        return MathF.CopySign(t, axis);
    }

    static float Deadzone()
    {
        var d = ConfigManager.Game.StickDeadzone;
        if (float.IsNaN(d)) return 0.15f;
        if (d < 0f) return 0f;
        if (d > 0.99f) return 0.99f;
        return d;
    }

    static float Curve()
    {
        var c = ConfigManager.Game.StickCurve;
        if (float.IsNaN(c) || c < 0f) return 1f;
        return c;
    }

    /// <summary>
    /// A live stick overwrites the halfword (the game already added the full
    /// digital step). An idle stick keeps it and <paramref name="extra"/>, the
    /// mouse, is added. The fractional part is carried so a slow deflection does
    /// not truncate to nothing; the carry is dropped when nothing is driving.
    /// The cast truncates toward zero. A sum past a signed halfword is clamped
    /// at the store, and that excess is not put back into the carry.
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

    /// <summary>func_800C44BC: below −512 becomes −512, and an unsigned halfword
    /// above 0xFF00 becomes −256.</summary>
    static int ClampMenuLook(int value)
    {
        if (value < MenuLookMin) return MenuLookMin;
        if (value > short.MaxValue) value = short.MaxValue;
        var bits = (ushort)(short)value;
        if (bits > MenuLookTop) return -256;
        return (short)bits;
    }

    /// <summary>func_800DBFB4: below −512 becomes −512, below 769 stays, else 768.</summary>
    static int ClampFreeLook(int value)
    {
        if (value < FreeLookMin) return FreeLookMin;
        if (value < FreeLookMax + 1) return value;
        return FreeLookMax;
    }

    static short ReadI16(IMemory m, uint addr) => (short)m.ReadU16(addr);

    static void WriteI16(IMemory m, uint addr, int value)
    {
        if (value > short.MaxValue) value = short.MaxValue;
        else if (value < short.MinValue) value = short.MinValue;
        m.WriteU16(addr, (ushort)(short)value);
    }
}
