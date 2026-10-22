namespace RecompOne.Recompiler.CodeGen;

/// <summary>
/// A player's first run recompiles the disc, which would drop the sharp wall and
/// door art. These sites are the distance tests that pick the blocky page.
/// HighDetailWalls keeps the close art there. Off is the original test.
/// The room check measures bytes left before 0x80093AB4. The primitive
/// pointer now lives at 0x80200000, so that difference is negative and the
/// room skips its doors and models. The end moves up with the two 2 MB slots.
/// </summary>
public static class DeceptionLodRewrite
{
    public static readonly (string Name, string Old, string New)[] Sites =
    [
        ("wall", "        { var _v = 0u; c.S0 = 0u | 0x0001u; if (RecompOne.Runtime.Pgxp.PgxpGate.Cpu && RecompOne.Runtime.Pgxp.Pgxp.CpuTracking) RecompOne.Runtime.Pgxp.PgxpCpu.Ori(16, 0, 0x0001, c.S0, _v); }\n        L800DF370: ;", "        { var _v = 0u; c.S0 = Deception.WallLod.FarS0(); if (RecompOne.Runtime.Pgxp.PgxpGate.Cpu && RecompOne.Runtime.Pgxp.Pgxp.CpuTracking) RecompOne.Runtime.Pgxp.PgxpCpu.Ori(16, 0, (ushort)c.S0, c.S0, _v); }\n        L800DF370: ;"),
        ("list", "        if (c.FP == 0u) {\n            { var _s = c.V1; var _t = c.S3; c.V0 = _s + _t; if (RecompOne.Runtime.Pgxp.PgxpGate.Cpu && RecompOne.Runtime.Pgxp.Pgxp.CpuTracking) RecompOne.Runtime.Pgxp.PgxpCpu.Add(2, 3, 19, c.V0, _s, _t); }\n            goto L800E19EC;", "        if (c.FP == 0u && Deception.WallLod.UseFarDoor()) {\n            { var _s = c.V1; var _t = c.S3; c.V0 = _s + _t; if (RecompOne.Runtime.Pgxp.PgxpGate.Cpu && RecompOne.Runtime.Pgxp.Pgxp.CpuTracking) RecompOne.Runtime.Pgxp.PgxpCpu.Add(2, 3, 19, c.V0, _s, _t); }\n            goto L800E19EC;"),
        ("mesh", "        if (c.A1 != 0u) {\n            goto L800E2734;\n        }", "        if (c.A1 != 0u || !Deception.WallLod.UseFarDoor()) {\n            goto L800E2734;\n        }"),
        ("leafA", "        if (c.S1 == 0u) {\n            { var _s = c.S0; var _t = c.S5; c.A1 = _s + _t; if (RecompOne.Runtime.Pgxp.PgxpGate.Cpu && RecompOne.Runtime.Pgxp.Pgxp.CpuTracking) RecompOne.Runtime.Pgxp.PgxpCpu.Add(5, 16, 21, c.A1, _s, _t); }\n            goto L800E27F8;", "        if (c.S1 == 0u && Deception.WallLod.UseFarDoor()) {\n            { var _s = c.S0; var _t = c.S5; c.A1 = _s + _t; if (RecompOne.Runtime.Pgxp.PgxpGate.Cpu && RecompOne.Runtime.Pgxp.Pgxp.CpuTracking) RecompOne.Runtime.Pgxp.PgxpCpu.Add(5, 16, 21, c.A1, _s, _t); }\n            goto L800E27F8;"),
        ("leafB", "        if (c.S1 == 0u) {\n            { var _v = c.V1; c.V1 = c.V1 << 4; if (RecompOne.Runtime.Pgxp.PgxpGate.Cpu && RecompOne.Runtime.Pgxp.Pgxp.CpuTracking) RecompOne.Runtime.Pgxp.PgxpCpu.Sll(3, 3, 4, c.V1, _v); }\n            goto L800E289C;", "        if (c.S1 == 0u && Deception.WallLod.UseFarDoor()) {\n            { var _v = c.V1; c.V1 = c.V1 << 4; if (RecompOne.Runtime.Pgxp.PgxpGate.Cpu && RecompOne.Runtime.Pgxp.Pgxp.CpuTracking) RecompOne.Runtime.Pgxp.PgxpCpu.Sll(3, 3, 4, c.V1, _v); }\n            goto L800E289C;"),
        ("pieceA", "        if (c.S0 == 0u) {\n            { var _s = c.A2; var _t = c.V0; c.A2 = _s + _t; if (RecompOne.Runtime.Pgxp.PgxpGate.Cpu && RecompOne.Runtime.Pgxp.Pgxp.CpuTracking) RecompOne.Runtime.Pgxp.PgxpCpu.Add(6, 6, 2, c.A2, _s, _t); }\n            goto L800E3E14;", "        if (c.S0 == 0u && Deception.WallLod.UseFarDoor()) {\n            { var _s = c.A2; var _t = c.V0; c.A2 = _s + _t; if (RecompOne.Runtime.Pgxp.PgxpGate.Cpu && RecompOne.Runtime.Pgxp.Pgxp.CpuTracking) RecompOne.Runtime.Pgxp.PgxpCpu.Add(6, 6, 2, c.A2, _s, _t); }\n            goto L800E3E14;"),
        ("pieceB", "        if (c.S0 == 0u) {\n            { var _v = c.V1; c.V1 = c.V1 << 4; if (RecompOne.Runtime.Pgxp.PgxpGate.Cpu && RecompOne.Runtime.Pgxp.Pgxp.CpuTracking) RecompOne.Runtime.Pgxp.PgxpCpu.Sll(3, 3, 4, c.V1, _v); }\n            goto L800E3EB8;", "        if (c.S0 == 0u && Deception.WallLod.UseFarDoor()) {\n            { var _v = c.V1; c.V1 = c.V1 << 4; if (RecompOne.Runtime.Pgxp.PgxpGate.Cpu && RecompOne.Runtime.Pgxp.Pgxp.CpuTracking) RecompOne.Runtime.Pgxp.PgxpCpu.Sll(3, 3, 4, c.V1, _v); }\n            goto L800E3EB8;"),
    ];

    // 0x80090000 + 0x3AB4 is 0x80093AB4, the end of the first 48000-byte buffer.
    // 0x805F0000 + 0x4480 is 0x805F4480. Plus the index stride of 48000 that is
    // 0x80600000, the end of the second relocated slot. Twelve room loops.
    const string RoomOld =
        "        c.V1 = 0x80090000u; if (RecompOne.Runtime.Pgxp.PgxpGate.Cpu && RecompOne.Runtime.Pgxp.Pgxp.CpuTracking) RecompOne.Runtime.Pgxp.PgxpCpu.Lui(3, c.V1);\n" +
        "        { var _v = c.V1; c.V1 = c.V1 + 0x3AB4u; if (RecompOne.Runtime.Pgxp.PgxpGate.Cpu && RecompOne.Runtime.Pgxp.Pgxp.CpuTracking) RecompOne.Runtime.Pgxp.PgxpCpu.Addi(3, 3, 15028, c.V1, _v); }";

    const string RoomNew =
        "        c.V1 = 0x805F0000u; if (RecompOne.Runtime.Pgxp.PgxpGate.Cpu && RecompOne.Runtime.Pgxp.Pgxp.CpuTracking) RecompOne.Runtime.Pgxp.PgxpCpu.Lui(3, c.V1);\n" +
        "        { var _v = c.V1; c.V1 = c.V1 + 0x4480u; if (RecompOne.Runtime.Pgxp.PgxpGate.Cpu && RecompOne.Runtime.Pgxp.Pgxp.CpuTracking) RecompOne.Runtime.Pgxp.PgxpCpu.Addi(3, 3, 17536, c.V1, _v); }";

    public static void ApplyFile(string path)
    {
        var source = File.ReadAllText(path);
        var rewritten = Apply(source);
        if (!string.Equals(source, rewritten, StringComparison.Ordinal))
            File.WriteAllText(path, rewritten);
    }

    public static string Apply(string source)
    {
        var nl = source.Contains("\r\n") ? "\r\n" : "\n";
        var text = source.Replace("\r\n", "\n");
        foreach (var (name, oldText, newText) in Sites)
            text = Once(text, oldText, newText, name);
        text = Many(text, RoomOld, RoomNew, "room", 12);
        return text.Replace("\n", nl);
    }

    private static string Once(string text, string oldText, string newText, string name)
    {
        var hasOld = text.Contains(oldText, StringComparison.Ordinal);
        var hasNew = text.Contains(newText, StringComparison.Ordinal);
        if (hasNew && !hasOld)
            return text;
        if (hasOld && !hasNew)
        {
            var count = 0;
            var index = 0;
            while ((index = text.IndexOf(oldText, index, StringComparison.Ordinal)) >= 0)
            {
                count++;
                index += oldText.Length;
            }
            if (count != 1)
                throw new InvalidOperationException($"Deception LOD rewrite '{name}' matched {count} times.");
            return text.Replace(oldText, newText, StringComparison.Ordinal);
        }
        throw new InvalidOperationException($"Deception LOD rewrite '{name}' did not match the recompiled wall and door code.");
    }

    private static string Many(string text, string oldText, string newText, string name, int expected)
    {
        var hasOld = text.Contains(oldText, StringComparison.Ordinal);
        var hasNew = text.Contains(newText, StringComparison.Ordinal);
        if (hasNew && !hasOld)
            return text;
        if (!hasOld || hasNew)
            throw new InvalidOperationException($"Deception LOD rewrite '{name}' did not match the recompiled wall and door code.");

        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(oldText, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += oldText.Length;
        }
        if (count != expected)
            throw new InvalidOperationException($"Deception LOD rewrite '{name}' matched {count} times.");
        return text.Replace(oldText, newText, StringComparison.Ordinal);
    }
}
