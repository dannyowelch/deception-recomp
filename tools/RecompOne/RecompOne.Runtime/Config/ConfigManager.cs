using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using ImGuiNET;
using RecompOne.Runtime.Host.Window;

namespace RecompOne.Runtime.Config;

file static class PanelDefaults
{
    public static bool IsOpenByDefault(IPanel p)
    {
        return p.Name == "Output";
    }
}

public static class ConfigManager
{
    private static readonly JsonSerializerOptions _opts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    private const string GameConfigPath = "settings.json";
    private const string InterfaceFile = "interface.ini";

    public static GameConfig Game { get; private set; } = new();
    public static ViewConfig View { get; private set; } = new();

    /// <summary>
    /// Port 0's button source for one poll. Null uses <see cref="GameConfig.PadFor"/>.
    /// Axis bytes are still taken from the returned object's stick indices.
    /// </summary>
    public static Func<GamepadBindings>? Port0PadBindings { get; set; }

    /// <summary>
    /// Port 0's keyboard source for one poll. Null uses <see cref="GameConfig.Keys"/>.
    /// </summary>
    public static Func<KeyBindings>? Port0KeyBindings { get; set; }

    /// <summary>
    /// True when settings.json names this property. A missing or unreadable
    /// file reports true so a caller does not replace an in-memory table.
    /// </summary>
    public static bool GameFileHas(string property)
    {
        if (!File.Exists(GameConfigPath)) return true;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(GameConfigPath));
            return doc.RootElement.ValueKind == JsonValueKind.Object &&
                   doc.RootElement.TryGetProperty(property, out _);
        }
        catch
        {
            return true;
        }
    }

    private static string? _pendingImGuiIni;

    public static void Load()
    {
        //0106. A file that will not parse falls back to the defaults, as before, but
        //is kept aside first: the defaults are written over it at the next save.
        if (File.Exists(GameConfigPath))
            try
            {
                var text = File.ReadAllText(GameConfigPath);
                if (JsonNode.Parse(text) is not JsonObject player)
                    throw new JsonException("settings.json is not an object");

                Game = JsonSerializer.Deserialize<GameConfig>(text, _opts) ?? new GameConfig();
                AddMissingSettings(player);
            }
            catch (Exception e)
            {
                var kept = IO.DurableFile.Keep(GameConfigPath, "damaged");
                Console.Error.WriteLine($"[Config] {GameConfigPath} unreadable, defaults used ({e.Message}); " +
                                        $"kept as {kept ?? "(none)"}");
                Game = new GameConfig();
            }
        else
            SaveGame();

        if (File.Exists(InterfaceFile))
            try
            {
                var text = File.ReadAllText(InterfaceFile);
                var (view, imguiIni) = ParseInterfaceFile(text);
                View = view;
                _pendingImGuiIni = imguiIni;
                AddMissingDisplay(text);
            }
            catch (Exception e)
            {
                var kept = IO.DurableFile.Keep(InterfaceFile, "damaged");
                Console.Error.WriteLine($"[Config] {InterfaceFile} unreadable, defaults used ({e.Message}); " +
                                        $"kept as {kept ?? "(none)"}");
            }
    }

    /// <summary>
    /// Write any setting this version adds into the player's settings.json.
    /// Values already in the file stay, including names this version does not
    /// know. The file is not rewritten when nothing is missing.
    /// </summary>
    static void AddMissingSettings(JsonObject player)
    {
        if (JsonSerializer.SerializeToNode(new GameConfig(), _opts) is not JsonObject defaults) return;
        if (!SettingsKeep.MissingAny(player, defaults)) return;
        if (JsonSerializer.SerializeToNode(Game, _opts) is not JsonObject merged) return;

        SettingsKeep.KeepUnknown(merged, player);
        IO.DurableFile.TryWriteText(GameConfigPath, merged.ToJsonString(_opts), "the settings");
        Console.WriteLine("[Config] settings.json gained the settings this version adds; existing values were kept");
    }

    /// <summary>
    /// Insert display keys this version adds. Existing lines and the ImGui
    /// layout are copied through unchanged.
    /// </summary>
    static void AddMissingDisplay(string text)
    {
        var missing = new List<KeyValuePair<string, string>>();
        foreach (var (key, value) in InterfaceMerge.Defaults)
        {
            if (View.Has(key)) continue;
            missing.Add(new KeyValuePair<string, string>(key, value));
            View.SetString(key, value);
        }

        if (missing.Count == 0) return;

        var updated = InterfaceMerge.InsertMissing(text, missing);
        if (updated == text) return;
        IO.DurableFile.TryWriteText(InterfaceFile, updated, "the interface settings");
        Console.WriteLine("[Config] interface.ini gained the display settings this version adds; existing values were kept");
    }


    public static bool ApplyImGuiLayout()
    {
        if (_pendingImGuiIni == null) return false;
        ImGui.LoadIniSettingsFromMemory(_pendingImGuiIni);
        _pendingImGuiIni = null;
        return true;
    }

    public static void ApplyViewToPanels(IReadOnlyList<IPanel> panels)
    {
        foreach (var p in panels)
            if (View.Panels.TryGetValue(p.Name, out var state))
                p.IsOpen = state.Open;
    }

    public static void SaveView(IReadOnlyList<IPanel> panels)
    {
        foreach (var p in panels)
            View.Panels[p.Name] = new PanelState { Open = p.IsOpen };

        var imguiIni = ImGui.SaveIniSettingsToMemory();
        var sb = new StringBuilder();
        sb.AppendLine("[RecompOne]");
        foreach (var (key, value) in View.Values)
            sb.AppendLine($"{key}={value}");
        foreach (var (name, state) in View.Panels)
            sb.AppendLine($"Panels.{name}={state.Open}");
        sb.AppendLine();
        sb.Append(imguiIni);
        //0106. Replaced in one step (DurableFile); a failure is logged, not thrown
        //into whichever settings widget changed a value.
        IO.DurableFile.TryWriteText(InterfaceFile, sb.ToString(), "the interface settings");
    }

    public static void ResetView(IReadOnlyList<IPanel> panels)
    {
        View = new ViewConfig();
        foreach (var p in panels)
            p.IsOpen = PanelDefaults.IsOpenByDefault(p);
        ImGui.LoadIniSettingsFromMemory("");
        SaveView(panels);
    }

    public static void SaveGame()
    {
        var json = JsonSerializer.Serialize(Game, _opts);
        if (JsonNode.Parse(json) is JsonObject node && File.Exists(GameConfigPath))
        {
            try
            {
                if (JsonNode.Parse(File.ReadAllText(GameConfigPath)) is JsonObject existing)
                    SettingsKeep.KeepUnknown(node, existing);
                json = node.ToJsonString(_opts);
            }
            catch (JsonException)
            {
            }
        }

        IO.DurableFile.TryWriteText(GameConfigPath, json, "the settings");
    }

    private static (ViewConfig view, string imguiIni) ParseInterfaceFile(string content)
    {
        var view = new ViewConfig();
        var imguiLines = new List<string>();
        var inRecompOne = false;

        foreach (var rawLine in content.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');

            if (line == "[RecompOne]")
            {
                inRecompOne = true;
                continue;
            }

            if (line.StartsWith('['))
                inRecompOne = false;

            if (inRecompOne)
            {
                if (line.Length == 0) continue;
                var eq = line.IndexOf('=');
                if (eq <= 0) continue;
                var key = line[..eq];
                var value = line[(eq + 1)..];
                if (key.StartsWith("Panels."))
                {
                    var panelName = key[7..];
                    var open = bool.TryParse(value, out var b) && b;
                    view.Panels[panelName] = new PanelState { Open = open };
                }
                else
                {
                    view.Values[key] = value;
                }
            }
            else
            {
                imguiLines.Add(line);
            }
        }

        return (view, string.Join('\n', imguiLines));
    }
}