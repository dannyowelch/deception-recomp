namespace Deception;

/// <summary>
/// One-time copy of files whose path used the old project name
/// (KingsField2Recomp, assembly KingsField2). The source is never deleted.
/// A file that already exists at the new path is left alone.
///
/// Play files are not stored under that name. The runtime addresses them
/// from the working directory: settings.json, interface.ini, carda.sav,
/// cardb.sav, a card's .bak and .fog, crashes/, and mods/.cache. Renaming
/// the exe does not move those, so this does not copy them onto themselves.
///
/// What does include the old name:
/// the launcher's cached game dll, {data}/builds/{key}/KingsField2.dll,
/// and any AppData folder literally named KingsField2 or KingsField2Recomp.
/// </summary>
static class LegacyData
{
    const string OldAssembly = "KingsField2";
    const string NewAssembly = "Deception";

    public static void Migrate()
    {
        try
        {
            MigrateAssemblyCaches();
            MigrateNamedFolders();
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"[Deception] could not migrate {OldAssembly} data: {e.Message}");
        }
    }

    static void MigrateAssemblyCaches()
    {
        foreach (var root in DataRoots())
        {
            var builds = Path.Combine(root, "builds");
            if (!Directory.Exists(builds)) continue;
            foreach (var dir in Directory.EnumerateDirectories(builds))
            {
                CopyIfMissing(Path.Combine(dir, OldAssembly + ".dll"), Path.Combine(dir, NewAssembly + ".dll"));
                CopyIfMissing(Path.Combine(dir, OldAssembly + ".pdb"), Path.Combine(dir, NewAssembly + ".pdb"));
            }
        }
    }

    /// <summary>The launcher's data directory. Name stays Verdite2; only the cached dll's file name changes.</summary>
    static IEnumerable<string> DataRoots()
    {
        var pinned = Environment.GetEnvironmentVariable("VERDITE2_DATA");
        if (!string.IsNullOrWhiteSpace(pinned)) yield return Path.GetFullPath(pinned);

        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrWhiteSpace(local)) yield return Path.Combine(local, "Verdite2");
    }

    static void MigrateNamedFolders()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        foreach (var parent in new[] { local, roaming })
        {
            if (string.IsNullOrWhiteSpace(parent)) continue;
            foreach (var oldName in new[] { "KingsField2", "KingsField2Recomp" })
                CopyTreeIfMissing(Path.Combine(parent, oldName), Path.Combine(parent, NewAssembly));
        }
    }

    static void CopyTreeIfMissing(string src, string dst)
    {
        if (!Directory.Exists(src)) return;
        foreach (var file in Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(src, file);
            CopyIfMissing(file, Path.Combine(dst, rel));
        }
    }

    static void CopyIfMissing(string src, string dst)
    {
        if (!File.Exists(src) || File.Exists(dst)) return;
        var parent = Path.GetDirectoryName(dst);
        if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
        File.Copy(src, dst);
        Console.WriteLine($"[Deception] migrated {src} -> {dst}");
    }
}
