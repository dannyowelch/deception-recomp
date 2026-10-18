namespace RecompOne.Runtime.Bios;

/// <summary>
/// Guest putc, putchar, puts and printf. Lines whose text starts with a prefix
/// in <see cref="SuppressedPrefixes"/> are dropped. Everything else is written
/// through unchanged.
/// </summary>
static class GuestConsole
{
    // The BIOS thread table's stack fields are left unset, so a game that
    // checks them prints a false STACK OVER. Add a prefix to drop another line.
    static readonly string[] SuppressedPrefixes =
    [
        "STACK OVER",
        "sp_now:",
    ];

    static readonly object _gate = new();
    static readonly System.Text.StringBuilder _pending = new();
    static bool _suppressing;
    static bool _passing;

    public static void Write(char c)
    {
        lock (_gate) WriteLocked(c);
    }

    public static void Write(string? text)
    {
        if (string.IsNullOrEmpty(text)) return;
        lock (_gate)
            foreach (var c in text)
                WriteLocked(c);
    }

    static void WriteLocked(char c)
    {
        if (c == '\n')
        {
            EndLine();
            return;
        }

        if (_suppressing) return;

        if (_passing)
        {
            Console.Write(c);
            return;
        }

        _pending.Append(c);
        var soFar = _pending.ToString();
        if (IsSuppressed(soFar))
        {
            _pending.Clear();
            _suppressing = true;
            return;
        }

        if (StillPossible(soFar)) return;

        Console.Write(soFar);
        _pending.Clear();
        _passing = true;
    }

    static void EndLine()
    {
        if (_suppressing)
        {
            _suppressing = false;
            return;
        }

        if (_passing)
        {
            _passing = false;
            Console.Write('\n');
            return;
        }

        var line = _pending.ToString();
        _pending.Clear();
        if (IsSuppressed(line)) return;
        Console.Write(line);
        Console.Write('\n');
    }

    static bool IsSuppressed(string line)
    {
        foreach (var prefix in SuppressedPrefixes)
            if (line.StartsWith(prefix, StringComparison.Ordinal))
                return true;
        return false;
    }

    // True while the text so far is still the start of a suppressed prefix.
    static bool StillPossible(string line)
    {
        foreach (var prefix in SuppressedPrefixes)
            if (prefix.StartsWith(line, StringComparison.Ordinal))
                return true;
        return false;
    }
}
