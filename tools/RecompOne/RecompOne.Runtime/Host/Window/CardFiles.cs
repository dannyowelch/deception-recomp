using NativeFileDialogNET;
using RecompOne.Runtime.Config;
using RecompOne.Runtime.Hardware;

namespace RecompOne.Runtime.Host.Window;

/// <summary>
/// System-menu copies of the two memory cards. Backup writes carda.sav and
/// cardb.sav into backups/ under the working directory. Restore opens in that
/// folder and puts a chosen copy back, after keeping the cards that are in use.
/// </summary>
public static class CardFiles
{
    public readonly record struct Piece(string Source, string Dest, string Name);

    public static IReadOnlyList<Piece> Pending { get; private set; } = [];
    public static IReadOnlyList<string> PendingSkipped { get; private set; } = [];
    public static string PendingFolder { get; private set; } = "";

    public static string BackupRoot => Path.Combine(Directory.GetCurrentDirectory(), "backups");

    public static void Backup()
    {
        try
        {
            var dir = Path.Combine(BackupRoot, DateTime.Now.ToString("yyyyMMdd-HHmmss"));
            Directory.CreateDirectory(dir);

            var names = new List<string>();
            foreach (var (live, name) in Slots())
            {
                if (!File.Exists(live)) continue;
                var bytes = File.ReadAllBytes(live);
                if (!IsCard(bytes))
                {
                    NoticePopup.Show(Localization.T("cards.restore.bad", name));
                    continue;
                }

                IO.DurableFile.Write(Path.Combine(dir, name), bytes, sync: true);
                names.Add(name);
            }

            if (names.Count == 0)
                NoticePopup.Show(Localization.T("cards.backup.none"));
            else
                NoticePopup.Show(Localization.T("cards.backup.done", string.Join(", ", names), dir));
        }
        catch (Exception e)
        {
            NoticePopup.Show(Localization.T("cards.backup.failed", e.Message));
        }
    }

    public static void ChooseRestore()
    {
        try
        {
            if (!PickFolder(out var folder) || string.IsNullOrWhiteSpace(folder))
                return;

            var pieces = new List<Piece>();
            var skipped = new List<string>();
            var already = 0;
            foreach (var (live, name) in Slots())
            {
                var source = Path.Combine(folder, name);
                if (!File.Exists(source)) continue;
                if (SameFile(source, live))
                {
                    already++;
                    continue;
                }

                if (!IsCard(File.ReadAllBytes(source)))
                {
                    skipped.Add(name);
                    continue;
                }

                pieces.Add(new Piece(source, live, name));
            }

            if (pieces.Count == 0)
            {
                NoticePopup.Show(skipped.Count > 0
                    ? Localization.T("cards.restore.bad", string.Join(", ", skipped))
                    : already > 0
                        ? Localization.T("cards.restore.same")
                        : Localization.T("cards.restore.none"));
                return;
            }

            PendingFolder = folder;
            Pending = pieces;
            PendingSkipped = skipped;
            PopupManager.Open<CardRestorePopup>();
        }
        catch (Exception e)
        {
            NoticePopup.Show(Localization.T("cards.restore.failed", e.Message));
        }
    }

    public static void CommitRestore()
    {
        var done = new List<string>();
        try
        {
            foreach (var piece in Pending)
            {
                var bytes = File.ReadAllBytes(piece.Source);
                if (!IsCard(bytes)) continue;
                if (File.Exists(piece.Dest))
                    IO.DurableFile.Keep(piece.Dest, "pre-restore");
                var parent = Path.GetDirectoryName(Path.GetFullPath(piece.Dest));
                if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
                IO.DurableFile.Write(piece.Dest, bytes, sync: true);
                done.Add(piece.Name);
            }

            if (done.Count > 0) Runtime.ReloadMemoryCards();

            var message = done.Count == 0
                ? Localization.T("cards.restore.none")
                : Localization.T("cards.restore.done", string.Join(", ", done));
            if (PendingSkipped.Count > 0)
                message += " " + Localization.T("cards.restore.bad", string.Join(", ", PendingSkipped));
            NoticePopup.Show(message);
        }
        catch (Exception e)
        {
            NoticePopup.Show(Localization.T("cards.restore.failed", e.Message));
        }
        finally
        {
            Pending = [];
            PendingSkipped = [];
            PendingFolder = "";
        }
    }

    /// <summary>
    /// backups/ under the working directory. The launcher has already moved
    /// into the per-user data folder, so this is that folder's backups/. It is
    /// created when missing so the picker can open there before the first backup.
    /// </summary>
    static string RestoreStart()
    {
        var root = Path.GetFullPath(BackupRoot);
        try
        {
            Directory.CreateDirectory(root);
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"[Host] backups folder: {e.Message}");
        }

        if (Directory.Exists(root)) return root;
        return Path.GetFullPath(Directory.GetCurrentDirectory());
    }

    static bool PickFolder(out string? folder)
    {
        folder = null;
        var start = RestoreStart();
        if (OperatingSystem.IsWindows())
        {
            switch (WindowsFolderDialog.Pick(start, Localization.T("cards.restore.title"), out folder))
            {
                case WindowsFolderDialog.Result.Chosen:
                    return true;
                case WindowsFolderDialog.Result.Cancelled:
                    return false;
            }
        }

        using var dialog = new NativeFileDialog();
        dialog.SelectFolder();
        return dialog.Open(out folder, start) == DialogResult.Okay && !string.IsNullOrWhiteSpace(folder);
    }

    static (string Live, string Name)[] Slots()
    {
        var game = ConfigManager.Game;
        return
        [
            (LivePath(game.CardAPath, "carda.sav"), "carda.sav"),
            (LivePath(game.CardBPath, "cardb.sav"), "cardb.sav"),
        ];
    }

    static string LivePath(string configured, string fallback) =>
        string.IsNullOrWhiteSpace(configured) ? fallback : configured;

    static bool IsCard(ReadOnlySpan<byte> bytes) =>
        bytes.Length == MemoryCard.CardSize && bytes[0] == (byte)'M' && bytes[1] == (byte)'C';

    static bool SameFile(string a, string b)
    {
        try
        {
            return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}
