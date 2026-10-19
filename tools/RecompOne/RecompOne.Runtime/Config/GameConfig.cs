using System.Text.Json;
using System.Text.Json.Serialization;

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
    // Walk layout. Arrows stay on the D-pad so menus still move. One keyboard
    // set is used in both walk and menus; the gamepad is what splits.
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
    public KeyBindings Keys { get; set; } = new();
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
