using System.Text.Json;
using System.Text.Json.Serialization;
using RecompOne.Runtime.Hardware;

namespace RecompOne.Runtime.Config;

/// <summary>
/// Reads a JSON string or an array of strings into <see cref="string[]"/>.
/// An empty string and null become an empty array, so a settings file that
/// still names one key per button keeps loading.
/// </summary>
public sealed class OneOrManyStringConverter : JsonConverter<string[]>
{
    public override string[] Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Null:
                return [];
            case JsonTokenType.String:
            {
                var s = reader.GetString();
                return string.IsNullOrEmpty(s) ? [] : [s];
            }
            case JsonTokenType.StartArray:
            {
                var list = new List<string>();
                while (reader.Read())
                {
                    if (reader.TokenType == JsonTokenType.EndArray) break;
                    if (reader.TokenType == JsonTokenType.String)
                    {
                        var s = reader.GetString();
                        if (!string.IsNullOrEmpty(s)) list.Add(s);
                    }
                    else if (reader.TokenType == JsonTokenType.Null)
                        continue;
                    else
                        throw new JsonException($"Expected a key name, got {reader.TokenType}.");
                }

                return list.ToArray();
            }
            default:
                throw new JsonException($"Expected a key name or a list of key names, got {reader.TokenType}.");
        }
    }

    public override void Write(Utf8JsonWriter writer, string[] value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        if (value != null)
            foreach (var s in value)
                if (!string.IsNullOrEmpty(s))
                    writer.WriteStringValue(s);
        writer.WriteEndArray();
    }
}

public class KeyBindings
{
    // Walk layout. Up and Down move, L1 and R1 strafe, Left and Right turn.
    // Arrows stay on the D-pad. Menus use KeyBindings.Menu, where strafe
    // becomes Left and Right.
    [JsonConverter(typeof(OneOrManyStringConverter))]
    public string[] Cross { get; set; } = ["Space"];
    [JsonConverter(typeof(OneOrManyStringConverter))]
    public string[] Circle { get; set; } = ["C"];
    [JsonConverter(typeof(OneOrManyStringConverter))]
    public string[] Square { get; set; } = ["Z"];
    [JsonConverter(typeof(OneOrManyStringConverter))]
    public string[] Triangle { get; set; } = ["Tab"];
    [JsonConverter(typeof(OneOrManyStringConverter))]
    public string[] L1 { get; set; } = ["A"];
    [JsonConverter(typeof(OneOrManyStringConverter))]
    public string[] R1 { get; set; } = ["D"];
    [JsonConverter(typeof(OneOrManyStringConverter))]
    public string[] L2 { get; set; } = ["R"];
    [JsonConverter(typeof(OneOrManyStringConverter))]
    public string[] R2 { get; set; } = ["F"];
    [JsonConverter(typeof(OneOrManyStringConverter))]
    public string[] L3 { get; set; } = ["V"];
    [JsonConverter(typeof(OneOrManyStringConverter))]
    public string[] R3 { get; set; } = ["B"];
    [JsonConverter(typeof(OneOrManyStringConverter))]
    public string[] Start { get; set; } = ["Enter"];
    [JsonConverter(typeof(OneOrManyStringConverter))]
    public string[] Select { get; set; } = ["ShiftRight"];
    [JsonConverter(typeof(OneOrManyStringConverter))]
    public string[] Up { get; set; } = ["W", "Up"];
    [JsonConverter(typeof(OneOrManyStringConverter))]
    public string[] Down { get; set; } = ["S", "Down"];
    [JsonConverter(typeof(OneOrManyStringConverter))]
    public string[] Left { get; set; } = ["Q", "Left"];
    [JsonConverter(typeof(OneOrManyStringConverter))]
    public string[] Right { get; set; } = ["E", "Right"];

    /// <summary>RecompOne's original one-key layout, before the walk defaults.</summary>
    public static KeyBindings Stock()
    {
        return new KeyBindings
        {
            Cross = ["Z"], Circle = ["X"], Square = ["A"], Triangle = ["S"],
            L1 = ["Q"], R1 = ["W"], L2 = ["E"], R2 = ["R"],
            L3 = ["F"], R3 = ["G"], Start = ["Enter"], Select = ["ShiftRight"],
            Up = ["Up"], Down = ["Down"], Left = ["Left"], Right = ["Right"]
        };
    }

    public static KeyBindings Empty()
    {
        return new KeyBindings
        {
            Cross = [], Circle = [], Square = [], Triangle = [],
            L1 = [], R1 = [], L2 = [], R2 = [],
            L3 = [], R3 = [], Start = [], Select = [],
            Up = [], Down = [], Left = [], Right = []
        };
    }

    /// <summary>
    /// Menu set built from the walk defaults. Strafe keys become Left and
    /// Right, turn keys become the shoulders, and the arrows stay on the D-pad.
    /// </summary>
    public static KeyBindings Menu() => MenuFrom(new KeyBindings());

    /// <summary>
    /// Menu set for a walk layout the player already saved. Face buttons and
    /// Up and Down are copied. Strafe becomes Left and Right. Arrow keys that
    /// were on the turn rows stay on the D-pad, and the other turn keys move
    /// to the shoulders.
    /// </summary>
    public static KeyBindings MenuFrom(KeyBindings? walk)
    {
        walk ??= new KeyBindings();
        var menu = Clone(walk);
        menu.Left = Merge(walk.L1, Arrows(walk.Left, "Left"));
        menu.Right = Merge(walk.R1, Arrows(walk.Right, "Right"));
        menu.L1 = Except(WithoutArrows(walk.Left), menu.Left);
        menu.R1 = Except(WithoutArrows(walk.Right), menu.Right);
        return menu;
    }

    public static KeyBindings Clone(KeyBindings? src)
    {
        src ??= new KeyBindings();
        return new KeyBindings
        {
            Cross = Copy(src.Cross), Circle = Copy(src.Circle), Square = Copy(src.Square),
            Triangle = Copy(src.Triangle), L1 = Copy(src.L1), R1 = Copy(src.R1),
            L2 = Copy(src.L2), R2 = Copy(src.R2), L3 = Copy(src.L3), R3 = Copy(src.R3),
            Start = Copy(src.Start), Select = Copy(src.Select),
            Up = Copy(src.Up), Down = Copy(src.Down), Left = Copy(src.Left), Right = Copy(src.Right)
        };
    }

    static readonly string[] ArrowNames = ["Up", "Down", "Left", "Right"];

    static string[] Copy(string[]? names) =>
        names == null ? [] : names.Where(s => !string.IsNullOrEmpty(s)).ToArray();

    static string[] Merge(string[]? a, string[]? b)
    {
        var list = new List<string>();
        Add(list, a);
        Add(list, b);
        return list.ToArray();
    }

    static void Add(List<string> list, string[]? names)
    {
        if (names == null) return;
        foreach (var s in names)
        {
            if (string.IsNullOrEmpty(s)) continue;
            if (!list.Exists(x => string.Equals(x, s, StringComparison.Ordinal)))
                list.Add(s);
        }
    }

    static string[] WithoutArrows(string[]? names) =>
        Copy(names).Where(s => Array.IndexOf(ArrowNames, s) < 0).ToArray();

    static string[] Arrows(string[]? names, string arrow) =>
        Copy(names).Where(s => string.Equals(s, arrow, StringComparison.Ordinal)).ToArray();

    static string[] Except(string[]? names, string[]? drop) =>
        Copy(names).Where(s => drop == null || !drop.Contains(s)).ToArray();

    public static bool Matches(KeyBindings a, KeyBindings b) =>
        Same(a.Cross, b.Cross) && Same(a.Circle, b.Circle) && Same(a.Square, b.Square) &&
        Same(a.Triangle, b.Triangle) && Same(a.L1, b.L1) && Same(a.R1, b.R1) &&
        Same(a.L2, b.L2) && Same(a.R2, b.R2) && Same(a.L3, b.L3) && Same(a.R3, b.R3) &&
        Same(a.Start, b.Start) && Same(a.Select, b.Select) &&
        Same(a.Up, b.Up) && Same(a.Down, b.Down) && Same(a.Left, b.Left) && Same(a.Right, b.Right);

    static bool Same(string[]? a, string[]? b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a == null || b == null || a.Length != b.Length) return false;
        for (var i = 0; i < a.Length; i++)
            if (!string.Equals(a[i], b[i], StringComparison.Ordinal)) return false;
        return true;
    }
}

public enum PadKind
{
    Digital,
    Analog
}

public class GamepadBindings
{
    public int[] Cross { get; set; } = [0];
    public int[] Circle { get; set; } = [1];
    public int[] Square { get; set; } = [2];
    public int[] Triangle { get; set; } = [3];
    public int[] L1 { get; set; } = [9];
    public int[] R1 { get; set; } = [10];
    public int[] L2 { get; set; } = [100];
    public int[] R2 { get; set; } = [101];
    public int[] L3 { get; set; } = [7];
    public int[] R3 { get; set; } = [8];
    public int[] Start { get; set; } = [6];
    public int[] Select { get; set; } = [4];
    public int[] Up { get; set; } = [11, 104];
    public int[] Down { get; set; } = [12, 105];
    public int[] Left { get; set; } = [13, 102];
    public int[] Right { get; set; } = [14, 103];

    public int LeftStickX { get; set; } = 0;
    public int LeftStickY { get; set; } = 1;
    public int RightStickX { get; set; } = 2;
    public int RightStickY { get; set; } = 3;

    /// <summary>
    /// Walk set. D-pad and the physical shoulders stay, and the sticks add
    /// strafe, turn, and look beside them. 102/103 are left-stick X, 106/107
    /// are right-stick X, 108/109 are right-stick Y. 108 is stick up.
    /// </summary>
    public static GamepadBindings Walk()
    {
        return new GamepadBindings
        {
            Up = [11, 104], Down = [12, 105],
            Left = [13, 106], Right = [14, 107],
            L1 = [9, 102], R1 = [10, 103],
            L2 = [100, 108], R2 = [101, 109]
        };
    }

    public static GamepadBindings DefaultAnalog()
    {
        return new GamepadBindings
        {
            Up = [11], Down = [12], Left = [13], Right = [14]
        };
    }

    public static GamepadBindings Empty()
    {
        return new GamepadBindings
        {
            Cross = [], Circle = [], Square = [], Triangle = [],
            L1 = [], R1 = [], L2 = [], R2 = [],
            L3 = [], R3 = [], Start = [], Select = [],
            Up = [], Down = [], Left = [], Right = []
        };
    }
}

public class GameConfig
{
    public string CdPath { get; set; } = "";
    public string CardAPath { get; set; } = "carda.sav";
    public string CardBPath { get; set; } = "cardb.sav";
    public bool CardAEnabled { get; set; } = true;
    public bool CardBEnabled { get; set; } = true;
    public float MasterVolume { get; set; } = 0.5f; // We don't wanna bust your ear drums out on initial run <3
    public float SpuVolume { get; set; } = 1.0f;
    public float XaVolume { get; set; } = 1.0f;
    public bool Muted { get; set; } = false;
    /// <summary>Walk set. An older file stored its only keyboard table here.</summary>
    public KeyBindings Keys { get; set; } = new();
    /// <summary>
    /// Menu set, including planning. Missing from an older file, so the
    /// initializer stands until a save. Face buttons match the walk set and
    /// the strafe keys become Left and Right.
    /// </summary>
    public KeyBindings KeysMenu { get; set; } = KeyBindings.Menu();
    public KeyBindings Keys2 { get; set; } = KeyBindings.Empty();
    /// <summary>Menu set: left stick is the D-pad, right stick is unbound.</summary>
    public GamepadBindings Pad { get; set; } = new();
    /// <summary>Walk set. Missing from an older settings file, so the initializer stands.</summary>
    public GamepadBindings PadWalk { get; set; } = GamepadBindings.Walk();
    public GamepadBindings Pad2 { get; set; } = GamepadBindings.Empty();
    public string PadDevice { get; set; } = "";
    public string PadDevice2 { get; set; } = "";
    public GamepadBindings PadAnalog { get; set; } = GamepadBindings.DefaultAnalog();
    public GamepadBindings PadAnalog2 { get; set; } = GamepadBindings.Empty();
    public PadKind PadKind { get; set; } = PadKind.Digital;
    public PadKind PadKind2 { get; set; } = PadKind.Digital;

    /// <summary>When false, the walk hooks leave the game's digital steps.
    /// Mouse look is separate and stays on.</summary>
    public bool Analog { get; set; } = true;
    /// <summary>When false, the pointer is not captured and mouse motion is not
    /// turned into yaw, pitch, or pad buttons. Sticks are unaffected.</summary>
    public bool MouseLook { get; set; } = true;
    /// <summary>Yaw units per mouse pixel are 8 times this. 1 keeps that rate.</summary>
    public float MouseSensitivityX { get; set; } = 1f;
    /// <summary>Look units per mouse pixel are 4 times this. 1 keeps that rate.</summary>
    public float MouseSensitivityY { get; set; } = 1f;
    public bool InvertLookY { get; set; } = false;
    /// <summary>Pad button held by the left mouse button while the pointer is
    /// captured. 0 is none. See <see cref="MousePad"/>.</summary>
    public int MouseLeft { get; set; } = 1;
    /// <summary>Pad button held by the right mouse button while the pointer is
    /// captured. 2 is Circle. 0 is none. See <see cref="MousePad"/>.</summary>
    public int MouseRight { get; set; } = 2;
    /// <summary>Pad button held by the middle mouse button while the pointer is
    /// captured. 4 is Triangle. 0 is none. See <see cref="MousePad"/>.</summary>
    public int MouseMiddle { get; set; } = 4;
    /// <summary>Stick deadzone. Read sites clamp this to 0..0.99.</summary>
    public float StickDeadzone { get; set; } = 0.15f;
    /// <summary>Power applied after the deadzone remap. 1 leaves the remap as it is.</summary>
    public float StickCurve { get; set; } = 1f;
    /// <summary>
    /// Wallpaper and doors past the sharp distance keep the close-up art.
    /// False restores the original blocky swap. Subdivision on that path is unchanged.
    /// The primitive buffers have to be in the RAM above 2 MB first, or the extra
    /// packets walk out of the 48000-byte pair into the data that follows them.
    /// </summary>
    public bool HighDetailWalls { get; set; } = true;

    public PadKind KindFor(int port)
    {
        return port == 0 ? PadKind : PadKind2;
    }

    public GamepadBindings PadFor(int port)
    {
        return port == 0
            ? (PadKind == PadKind.Analog ? PadAnalog : Pad)
            : (PadKind2 == PadKind.Analog ? PadAnalog2 : Pad2);
    }

    public List<string> ActiveMods { get; set; } = [];
}

/// <summary>
/// What a mouse button holds in the pad word. Index 0 is none, and saved
/// settings store the index, so this order stays put.
/// </summary>
public static class MousePad
{
    public static readonly (string Name, ushort Bit)[] Buttons =
    [
        ("None", 0),
        ("Cross", Controller.Cross),
        ("Circle", Controller.Circle),
        ("Square", Controller.Square),
        ("Triangle", Controller.Triangle),
        ("L1", Controller.L1),
        ("R1", Controller.R1),
        ("L2", Controller.L2),
        ("R2", Controller.R2),
        ("L3", Controller.L3),
        ("R3", Controller.R3),
        ("Start", Controller.Start),
        ("Select", Controller.Select),
        ("Up", Controller.Up),
        ("Down", Controller.Down),
        ("Left", Controller.Left),
        ("Right", Controller.Right),
    ];

    public static int Clamp(int index) =>
        index > 0 && index < Buttons.Length ? index : 0;

    public static ushort Bit(int index) => Buttons[Clamp(index)].Bit;
}
