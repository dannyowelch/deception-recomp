using System.Numerics;
using ImGuiNET;
using Silk.NET.Input;
using RecompOne.Runtime.Config;

namespace RecompOne.Runtime.Host.Window;

internal sealed class InputSettingsSection : ISettingsSection
{
    public string Id => "input";
    public string TitleKey => "settings.input";
    public int Order => 0;

    private static readonly (string Label, Func<KeyBindings, string[]> GetKey, Action<KeyBindings, string[]> SetKey,
        Func<GamepadBindings, int[]> GetPad, Action<GamepadBindings, int[]> SetPad)[] _rows =
        [
            ("Cross", b => b.Cross, (b, v) => b.Cross = v, p => p.Cross, (p, v) => p.Cross = v),
            ("Circle", b => b.Circle, (b, v) => b.Circle = v, p => p.Circle, (p, v) => p.Circle = v),
            ("Square", b => b.Square, (b, v) => b.Square = v, p => p.Square, (p, v) => p.Square = v),
            ("Triangle", b => b.Triangle, (b, v) => b.Triangle = v, p => p.Triangle, (p, v) => p.Triangle = v),
            ("L1", b => b.L1, (b, v) => b.L1 = v, p => p.L1, (p, v) => p.L1 = v),
            ("R1", b => b.R1, (b, v) => b.R1 = v, p => p.R1, (p, v) => p.R1 = v),
            ("L2", b => b.L2, (b, v) => b.L2 = v, p => p.L2, (p, v) => p.L2 = v),
            ("R2", b => b.R2, (b, v) => b.R2 = v, p => p.R2, (p, v) => p.R2 = v),
            ("L3", b => b.L3, (b, v) => b.L3 = v, p => p.L3, (p, v) => p.L3 = v),
            ("R3", b => b.R3, (b, v) => b.R3 = v, p => p.R3, (p, v) => p.R3 = v),
            ("Start", b => b.Start, (b, v) => b.Start = v, p => p.Start, (p, v) => p.Start = v),
            ("Select", b => b.Select, (b, v) => b.Select = v, p => p.Select, (p, v) => p.Select = v),
            ("Up", b => b.Up, (b, v) => b.Up = v, p => p.Up, (p, v) => p.Up = v),
            ("Down", b => b.Down, (b, v) => b.Down = v, p => p.Down, (p, v) => p.Down = v),
            ("Left", b => b.Left, (b, v) => b.Left = v, p => p.Left, (p, v) => p.Left = v),
            ("Right", b => b.Right, (b, v) => b.Right = v, p => p.Right, (p, v) => p.Right = v)
        ];

    private bool _gamepadMode;
    private bool _mouseMode;
    private bool _walkProfile;
    private int _padIndex;
    private int _remapRow = -1;
    private bool _remapAdd;

    public void Draw()
    {
        DrawDeviceSelector();
        if (_mouseMode)
        {
            ImGui.Spacing();
            DrawMouse();
            return;
        }

        ImGui.Spacing();
        DrawPadSelector();
        DrawProfileSelector();
        ImGui.Spacing();
        DrawPadKind();
        ImGui.Spacing();

        if (_gamepadMode)
        {
            DrawDeviceCombo();
            ImGui.Spacing();
        }

        if (_gamepadMode && !InputManager.IsPadConnected(_padIndex))
        {
            ImGuiEx.TextColored(new Vector4(1f, 0.75f, 0.3f, 1f), Localization.T("settings.input.no_gamepad"));
            ImGui.Spacing();
        }

        DrawBindings();

        if (_gamepadMode && ConfigManager.Game.KindFor(_padIndex) == PadKind.Analog)
        {
            ImGui.Spacing();
            DrawStickAxes();
        }

        ImGui.Spacing();
        if (ImGui.Button(Localization.T("settings.input.reset_defaults")))
        {
            if (!_gamepadMode)
            {
                if (_padIndex != 0)
                    ConfigManager.Game.Keys2 = KeyBindings.Empty();
                else if (_walkProfile)
                    ConfigManager.Game.Keys = new KeyBindings();
                else
                    ConfigManager.Game.KeysMenu = KeyBindings.Menu();
            }
            else if (_padIndex == 0)
            {
                // Port 0 edits the menu set or the walk set, never the analog
                // table. Reset restores whichever profile is on screen.
                if (_walkProfile) ConfigManager.Game.PadWalk = GamepadBindings.Walk();
                else ConfigManager.Game.Pad = new GamepadBindings();
            }
            else
            {
                var analog = ConfigManager.Game.KindFor(_padIndex) == PadKind.Analog;
                if (analog) ConfigManager.Game.PadAnalog2 = GamepadBindings.Empty();
                else ConfigManager.Game.Pad2 = GamepadBindings.Empty();
            }

            _remapRow = -1;
            ConfigManager.SaveGame();
        }
    }

    private static readonly (string Label, Func<GamepadBindings, int> Get, Action<GamepadBindings, int> Set)[]
        _axisRows =
        [
            ("L Stick X", p => p.LeftStickX, (p, v) => p.LeftStickX = v),
            ("L Stick Y", p => p.LeftStickY, (p, v) => p.LeftStickY = v),
            ("R Stick X", p => p.RightStickX, (p, v) => p.RightStickX = v),
            ("R Stick Y", p => p.RightStickY, (p, v) => p.RightStickY = v)
        ];

    private static readonly string[] AxisNames = ["Left X", "Left Y", "Right X", "Right Y", "Trigger L", "Trigger R"];

    private void DrawStickAxes()
    {
        var pad = ConfigManager.Game.PadFor(_padIndex);
        ImGuiEx.TextColored(new Vector4(0.6f, 0.6f, 0.65f, 1f), Localization.T("settings.input.sticks"));

        foreach (var (label, get, set) in _axisRows)
        {
            var cur = get(pad);
            var name = cur >= 0 && cur < AxisNames.Length ? AxisNames[cur] : $"axis {cur}";

            ImGui.SetNextItemWidth(160f);
            if (ImGui.BeginCombo(label, name))
            {
                for (var i = 0; i < AxisNames.Length; i++)
                    if (ImGui.Selectable(AxisNames[i], i == cur))
                    {
                        set(pad, i);
                        ConfigManager.SaveGame();
                    }

                ImGui.EndCombo();
            }
        }
    }

    private static readonly string[] PadKindKeys = ["settings.input.kind.digital", "settings.input.kind.analog"];

    private void DrawPadKind()
    {
        var cfg = ConfigManager.Game;
        var kind = _padIndex == 0 ? cfg.PadKind : cfg.PadKind2;
        var index = (int)kind;

        ImGui.SetNextItemWidth(220f);
        if (ImGui.BeginCombo(Localization.T("settings.input.kind"), Localization.T(PadKindKeys[index])))
        {
            for (var i = 0; i < PadKindKeys.Length; i++)
                if (ImGui.Selectable(Localization.T(PadKindKeys[i]), i == index))
                {
                    if (_padIndex == 0) cfg.PadKind = (PadKind)i;
                    else cfg.PadKind2 = (PadKind)i;
                    ConfigManager.SaveGame();
                }

            ImGui.EndCombo();
        }

        ImGui.SameLine();
        ImGuiEx.TextColored(new Vector4(0.6f, 0.6f, 0.65f, 1f),
            Localization.T(index == 1 ? "settings.input.kind.analog.hint" : "settings.input.kind.digital.hint"));
    }

    /// <summary>Port 0's gamepad and keyboard each have a menu set and a walk set. Pad 2 keeps one table.</summary>
    private void DrawProfileSelector()
    {
        if (_padIndex != 0) return;

        ImGui.Spacing();
        var label = Localization.T(_walkProfile ? "settings.input.profile.walk" : "settings.input.profile.menu");
        ImGui.SetNextItemWidth(220f);
        if (ImGui.BeginCombo(Localization.T("settings.input.profile"), label))
        {
            if (ImGui.Selectable(Localization.T("settings.input.profile.menu"), !_walkProfile))
            {
                if (_walkProfile) _remapRow = -1;
                _walkProfile = false;
            }

            if (ImGui.Selectable(Localization.T("settings.input.profile.walk"), _walkProfile))
            {
                if (!_walkProfile) _remapRow = -1;
                _walkProfile = true;
            }

            ImGui.EndCombo();
        }

        ImGui.SameLine();
        var hint = _gamepadMode
            ? (_walkProfile ? "settings.input.profile.hint.walk" : "settings.input.profile.hint.menu")
            : (_walkProfile ? "settings.input.profile.hint.walk.keys" : "settings.input.profile.hint.menu.keys");
        ImGuiEx.TextColored(new Vector4(0.6f, 0.6f, 0.65f, 1f), Localization.T(hint));
    }

    /// <summary>
    /// Port 0's keyboard comes from the menu set or the walk set. Pad 2 keeps
    /// <see cref="GameConfig.Keys2"/>.
    /// </summary>
    private KeyBindings KeysBeingEdited()
    {
        var game = ConfigManager.Game;
        if (_padIndex != 0) return game.Keys2 ?? KeyBindings.Empty();
        if (_walkProfile) return game.Keys ??= new KeyBindings();
        return game.KeysMenu ??= KeyBindings.Menu();
    }

    /// <summary>
    /// Port 0's buttons come from the menu set or the walk set. Pad kind does
    /// not choose between them. Pad 2 still follows <see cref="GameConfig.PadFor"/>.
    /// </summary>
    private GamepadBindings PadBeingEdited()
    {
        if (_padIndex == 0 && _gamepadMode)
            return _walkProfile ? ConfigManager.Game.PadWalk : ConfigManager.Game.Pad;
        return ConfigManager.Game.PadFor(_padIndex);
    }

    private void DrawDeviceSelector()
    {
        if (ImGui.BeginTabBar("##input-device"))
        {
            if (ImGui.BeginTabItem(Localization.T("settings.input.keyboard")))
            {
                if (_gamepadMode || _mouseMode) _remapRow = -1;
                _gamepadMode = false;
                _mouseMode = false;
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem(Localization.T("settings.input.gamepad")))
            {
                if (!_gamepadMode || _mouseMode) _remapRow = -1;
                _gamepadMode = true;
                _mouseMode = false;
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem(Localization.T("settings.input.mouse")))
            {
                if (!_mouseMode) _remapRow = -1;
                _gamepadMode = false;
                _mouseMode = true;
                ImGui.EndTabItem();
            }

            ImGui.EndTabBar();
        }
    }

    private static readonly (string Label, Func<GameConfig, int> Get, Action<GameConfig, int> Set)[] _mouseButtons =
    [
        ("settings.input.mouse.left", c => c.MouseLeft, (c, v) => c.MouseLeft = v),
        ("settings.input.mouse.right", c => c.MouseRight, (c, v) => c.MouseRight = v),
        ("settings.input.mouse.middle", c => c.MouseMiddle, (c, v) => c.MouseMiddle = v),
    ];

    private static void DrawMouse()
    {
        var cfg = ConfigManager.Game;
        var enabled = cfg.MouseLook;
        if (ImGui.Checkbox(Localization.T("settings.input.mouse.enable"), ref enabled))
        {
            cfg.MouseLook = enabled;
            if (!enabled) HostWindow.MouseCaptured = false;
            ConfigManager.SaveGame();
        }

        ImGui.Spacing();
        ImGui.BeginDisabled(!enabled);

        Slider("settings.input.mouse.turn", cfg.MouseSensitivityX, v => cfg.MouseSensitivityX = v);
        Slider("settings.input.mouse.look", cfg.MouseSensitivityY, v => cfg.MouseSensitivityY = v);

        var invert = cfg.InvertLookY;
        if (ImGui.Checkbox(Localization.T("settings.input.mouse.invert"), ref invert))
        {
            cfg.InvertLookY = invert;
            ConfigManager.SaveGame();
        }

        ImGui.Spacing();
        var names = MousePad.Buttons.Select(b => b.Name).ToArray();
        foreach (var (label, get, set) in _mouseButtons)
        {
            var index = MousePad.Clamp(get(cfg));
            ImGui.SetNextItemWidth(220f);
            if (ImGui.Combo(Localization.T(label), ref index, names, names.Length))
            {
                set(cfg, MousePad.Clamp(index));
                ConfigManager.SaveGame();
            }
        }

        ImGui.EndDisabled();

        ImGui.Spacing();
        if (!HostWindow.MouseAvailable)
            Note(Localization.T("settings.input.mouse.none"));
        else
            Note(HostWindow.MouseCaptured
                ? Localization.T("settings.input.mouse.captured")
                : Localization.T("settings.input.mouse.free"));
        Note(Localization.T("settings.input.mouse.note"));

        ImGui.Spacing();
        if (ImGui.Button(Localization.T("settings.input.reset_defaults")))
        {
            cfg.MouseLook = true;
            cfg.MouseSensitivityX = 1f;
            cfg.MouseSensitivityY = 1f;
            cfg.InvertLookY = false;
            cfg.MouseLeft = 1;
            cfg.MouseRight = 2;
            cfg.MouseMiddle = 4;
            ConfigManager.SaveGame();
        }
    }

    private static void Slider(string key, float value, Action<float> set)
    {
        if (float.IsNaN(value) || value < 0f) value = 0f;
        ImGui.SetNextItemWidth(220f);
        if (ImGui.SliderFloat(Localization.T(key), ref value, 0f, 5f, "%.2f", ImGuiSliderFlags.AlwaysClamp))
            set(value);
        if (ImGui.IsItemDeactivatedAfterEdit())
            ConfigManager.SaveGame();
    }

    private static void Note(string text)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.6f, 0.6f, 0.65f, 1f));
        ImGui.TextWrapped(text);
        ImGui.PopStyleColor();
    }

    private void DrawPadSelector()
    {
        if (ImGui.BeginTabBar("##input-pad"))
        {
            if (ImGui.BeginTabItem(Localization.T("settings.input.pad", 1)))
            {
                if (_padIndex != 0) _remapRow = -1;
                _padIndex = 0;
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem(Localization.T("settings.input.pad", 2)))
            {
                if (_padIndex != 1) _remapRow = -1;
                _padIndex = 1;
                ImGui.EndTabItem();
            }

            ImGui.EndTabBar();
        }
    }

    private void DrawDeviceCombo()
    {
        var devices = InputManager.Devices;
        var current = _padIndex == 0 ? ConfigManager.Game.PadDevice : ConfigManager.Game.PadDevice2;

        var preview = Localization.T("settings.input.device.auto");
        foreach (var d in devices)
            if (d.Id == current)
            {
                preview = d.Name;
                break;
            }

        if (!string.IsNullOrEmpty(current) && preview == Localization.T("settings.input.device.auto"))
            preview = Localization.T("settings.input.device.missing");

        ImGui.TextUnformatted(Localization.T("settings.input.device"));
        ImGui.SameLine();
        ImGui.SetNextItemWidth(-70);

        if (ImGui.BeginCombo("##input-device-pick", preview))
        {
            if (ImGui.Selectable(Localization.T("settings.input.device.auto"), string.IsNullOrEmpty(current)))
                SetDevice("");

            foreach (var d in devices)
                if (ImGui.Selectable(d.Name, d.Id == current))
                    SetDevice(d.Id);

            ImGui.EndCombo();
        }

        ImGui.SameLine();
        if (ImGui.Button(Localization.T("settings.input.device.refresh")))
            InputManager.RefreshDevices();
    }

    private void SetDevice(string id)
    {
        if (_padIndex == 0) ConfigManager.Game.PadDevice = id;
        else ConfigManager.Game.PadDevice2 = id;
        ConfigManager.SaveGame();
        InputManager.RefreshDevices();
    }

    private void DrawBindings()
    {
        if (!ImGui.BeginTable("##bindings", 2,
                ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp))
            return;

        ImGui.TableSetupColumn(Localization.T("settings.input.button"), ImGuiTableColumnFlags.WidthFixed, 90);
        ImGui.TableSetupColumn(Localization.T(_gamepadMode ? "settings.input.gamepad" : "settings.input.keyboard"),
            ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableHeadersRow();

        var keys = KeysBeingEdited();
        var pad = PadBeingEdited();

        for (var i = 0; i < _rows.Length; i++)
        {
            var (label, getKey, setKey, getPad, setPad) = _rows[i];
            ImGui.TableNextRow();

            ImGui.TableSetColumnIndex(0);
            ImGui.TextUnformatted(label);

            ImGui.TableSetColumnIndex(1);
            var awaiting = _remapRow == i;

            if (_gamepadMode)
            {
                var bindings = getPad(pad);
                var text = awaiting
                    ? Localization.T(_remapAdd ? "settings.input.press_button_add" : "settings.input.press_button")
                    : bindings.Length == 0
                        ? Localization.T("settings.input.unbound")
                        : string.Join(" | ", bindings.Select(PadLabel));

                var side = ImGui.GetFrameHeight();
                var spacing = ImGui.GetStyle().ItemSpacing.X;
                if (ImGui.Button($"{text}##p{i}", new Vector2(-(side + spacing) * 2f, 0)))
                {
                    _remapRow = i;
                    _remapAdd = false;
                }

                ImGui.SameLine();
                if (ImGui.Button($"+##add{i}", new Vector2(side, 0)))
                {
                    _remapRow = i;
                    _remapAdd = true;
                }

                ImGui.SameLine();
                var cleared = ImGui.Button($"-##pclr{i}", new Vector2(side, 0));
                if (cleared)
                {
                    setPad(pad, []);
                    _remapRow = -1;
                    ConfigManager.SaveGame();
                }

                if (awaiting && !cleared)
                {
                    var p = InputManager.GetFirstPressedPadButton(_padIndex);
                    if (p.HasValue)
                    {
                        if (_remapAdd)
                        {
                            if (!bindings.Contains(p.Value)) setPad(pad, [.. bindings, p.Value]);
                        }
                        else
                        {
                            setPad(pad, [p.Value]);
                        }

                        _remapRow = -1;
                        ConfigManager.SaveGame();
                    }
                }
            }
            else
            {
                var names = getKey(keys) ?? [];
                var text = awaiting
                    ? Localization.T(_remapAdd ? "settings.input.press_key_add" : "settings.input.press_key")
                    : names.Length == 0
                        ? Localization.T("settings.input.unbound")
                        : string.Join(" | ", names);

                var side = ImGui.GetFrameHeight();
                var spacing = ImGui.GetStyle().ItemSpacing.X;
                if (ImGui.Button($"{text}##k{i}", new Vector2(-(side + spacing) * 2f, 0)))
                {
                    _remapRow = i;
                    _remapAdd = false;
                }

                ImGui.SameLine();
                if (ImGui.Button($"+##kadd{i}", new Vector2(side, 0)))
                {
                    _remapRow = i;
                    _remapAdd = true;
                }

                ImGui.SameLine();
                var cleared = ImGui.Button($"-##kclr{i}", new Vector2(side, 0));
                if (cleared)
                {
                    setKey(keys, []);
                    _remapRow = -1;
                    ConfigManager.SaveGame();
                }

                if (awaiting && !cleared)
                {
                    var p = GetPressedKey();
                    if (p != null)
                    {
                        if (_remapAdd)
                        {
                            if (!names.Contains(p)) setKey(keys, [.. names, p]);
                        }
                        else setKey(keys, [p]);

                        _remapRow = -1;
                        ConfigManager.SaveGame();
                    }
                }
            }
        }

        ImGui.EndTable();
    }

    private static string? GetPressedKey()
    {
        foreach (var k in Enum.GetValues<Key>())
        {
            if (k is Key.Unknown or Key.Menu) continue;
            if (InputManager.IsKeyDown(k)) return k.ToString();
        }

        return null;
    }

    private static string PadLabel(int b)
    {
        return b switch
        {
            0 => "Cross (A)",
            1 => "Circle (B)",
            2 => "Square (X)",
            3 => "Triangle (Y)",
            4 => "Select (Back)",
            5 => "Guide",
            6 => "Start",
            7 => "L3 (LStick)",
            8 => "R3 (RStick)",
            9 => "L1 (LBumper)",
            10 => "R1 (RBumper)",
            11 => "D-Up",
            12 => "D-Down",
            13 => "D-Left",
            14 => "D-Right",
            100 => "L2 (LTrigger)",
            101 => "R2 (RTrigger)",
            102 => "LStick Left",
            103 => "LStick Right",
            104 => "LStick Up",
            105 => "LStick Down",
            106 => "RStick Left",
            107 => "RStick Right",
            108 => "RStick Up",
            109 => "RStick Down",
            _ => $"Btn {b}"
        };
    }
}