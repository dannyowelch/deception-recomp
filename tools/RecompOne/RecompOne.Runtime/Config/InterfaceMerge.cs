namespace RecompOne.Runtime.Config;

/// <summary>
/// Display settings live in interface.ini. A new key is inserted into the
/// existing [RecompOne] section. Lines the player already has, and the ImGui
/// layout after that section, are not rewritten.
/// </summary>
public static class InterfaceMerge
{
    /// <summary>Fallbacks the window uses when a key is absent. Empty on
    /// purpose is not a fallback worth writing.</summary>
    public static readonly (string Key, string Value)[] Defaults =
    [
        ("RenderScale", "8"),
        ("Anisotropy", "16"),
        ("Mipmaps", "True"),
        ("WindowScale", "4"),
        ("Frame", "4:3"),
        ("HideBorders", "False"),
        ("GpuBackend", "auto"),
        ("VSync", "False"),
        ("Fullscreen", "False"),
        ("Borderless", "False"),
        ("UiScale", "1"),
        ("ShowFps", "False"),
        ("HideTopBar", "False"),
        ("AutoHideMenuBar", "False"),
    ];

    public static string InsertMissing(string text, IEnumerable<KeyValuePair<string, string>> pairs)
    {
        var nl = text.Contains("\r\n") ? "\r\n" : "\n";
        var lines = text.Split(["\r\n", "\n"], StringSplitOptions.None).ToList();
        var section = -1;
        for (var i = 0; i < lines.Count; i++)
            if (lines[i].Trim() == "[RecompOne]")
            {
                section = i;
                break;
            }

        var end = lines.Count;
        var present = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (section >= 0)
        {
            for (var i = section + 1; i < lines.Count; i++)
            {
                if (lines[i].TrimStart().StartsWith('['))
                {
                    end = i;
                    break;
                }

                var eq = lines[i].IndexOf('=');
                if (eq > 0) present.Add(lines[i][..eq].Trim());
            }
        }

        var block = new List<string>();
        foreach (var pair in pairs)
        {
            if (string.IsNullOrWhiteSpace(pair.Key) || present.Contains(pair.Key)) continue;
            block.Add($"{pair.Key}={pair.Value}");
        }

        if (block.Count == 0) return text;

        if (section < 0)
        {
            var head = new List<string> { "[RecompOne]" };
            head.AddRange(block);
            head.Add("");
            lines.InsertRange(0, head);
        }
        else
        {
            var insert = end;
            while (insert > section + 1 && lines[insert - 1].Length == 0)
                insert--;
            lines.InsertRange(insert, block);
        }

        return string.Join(nl, lines);
    }
}
