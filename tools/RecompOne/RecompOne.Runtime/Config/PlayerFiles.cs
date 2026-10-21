using System.Text.Json;

namespace RecompOne.Runtime.Config;

/// <summary>
/// Player files a new install must not replace: settings, the display file,
/// and both memory cards. Copy runs only when the destination is absent.
/// </summary>
public static class PlayerFiles
{
    public static readonly string[] KeptNames =
    [
        "settings.json",
        "interface.ini",
        "carda.sav",
        "cardb.sav",
        "carda.sav.bak",
        "cardb.sav.bak",
        "carda.fog",
        "cardb.fog",
    ];

    /// <summary>
    /// Copy player files from <paramref name="sources"/> into <paramref name="data"/>
    /// when that file is not already there. The newest source wins. A file
    /// already in <paramref name="data"/> is left untouched. Returns how many
    /// files were copied.
    /// </summary>
    public static int BringForward(string data, IEnumerable<string> sources)
    {
        var dataFull = Path.GetFullPath(data);
        var roots = new List<string>();
        foreach (var source in sources)
        {
            if (string.IsNullOrWhiteSpace(source)) continue;
            string full;
            try { full = Path.GetFullPath(source); }
            catch { continue; }
            if (!Directory.Exists(full) || Same(full, dataFull)) continue;
            if (!roots.Contains(full, StringComparer.OrdinalIgnoreCase)) roots.Add(full);
        }

        var names = new HashSet<string>(KeptNames, StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots)
            foreach (var extra in CardNames(root))
                names.Add(extra);

        var copied = 0;
        foreach (var name in names)
            if (CopyNewest(dataFull, name, roots)) copied++;
        return copied;
    }

    static List<string> CardNames(string root)
    {
        var names = new List<string>();
        var settings = Path.Combine(root, "settings.json");
        if (!File.Exists(settings)) return names;

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(settings));
            foreach (var key in new[] { "CardAPath", "CardBPath" })
            {
                if (!doc.RootElement.TryGetProperty(key, out var prop) || prop.ValueKind != JsonValueKind.String)
                    continue;
                var rel = prop.GetString();
                if (string.IsNullOrWhiteSpace(rel) || Path.IsPathRooted(rel)) continue;

                var full = Path.GetFullPath(Path.Combine(root, rel));
                if (!IsInside(root, full)) continue;

                var slash = rel.Replace('\\', '/');
                names.Add(slash);
                names.Add(slash + ".bak");
                var fog = Path.ChangeExtension(rel, ".fog");
                if (!string.IsNullOrEmpty(fog)) names.Add(fog.Replace('\\', '/'));
            }
        }
        catch (JsonException)
        {
        }
        catch (IOException)
        {
        }

        return names;
    }

    static bool CopyNewest(string dataFull, string relative, List<string> roots)
    {
        var dst = Path.GetFullPath(Path.Combine(dataFull, relative));
        if (!IsInside(dataFull, dst) || File.Exists(dst)) return false;

        string? best = null;
        var bestTime = DateTime.MinValue;
        foreach (var root in roots)
        {
            var src = Path.GetFullPath(Path.Combine(root, relative));
            if (!IsInside(root, src) || !File.Exists(src)) continue;
            var written = File.GetLastWriteTimeUtc(src);
            if (best != null && written <= bestTime) continue;
            best = src;
            bestTime = written;
        }

        if (best == null) return false;

        var parent = Path.GetDirectoryName(dst);
        if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
        File.Copy(best, dst, overwrite: false);
        Console.WriteLine($"[Config] kept {relative} from {best}");
        return true;
    }

    static bool Same(string a, string b) =>
        string.Equals(
            Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);

    static bool IsInside(string root, string full)
    {
        root = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        full = Path.GetFullPath(full);
        return full.Equals(root, StringComparison.OrdinalIgnoreCase)
               || full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
}
