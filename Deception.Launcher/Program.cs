using Verdite.Launcher;

// Deception -- the shipped entry point.
//
// The launcher is Verdite Core's (tools/verdite-core/launcher): it settles where
// files live, asks for a disc, builds the game from it on first run and hands
// over. This is what it needs to know about this port. See docs/PACKAGING.md.

return Launcher.Run(new LauncherGame
{
    Name = "Deception",
    AppId = "deception",
    GameTitle = "Deception",
    Serial = "SLUS-00340",

    // King's Field is the disc this tree used to ship, and a player who still has
    // that image will reach for it. Every address in Deception.json is wrong for
    // it. Any other serial is refused by the boot-file check; these two say which
    // game it actually is.
    WrongDiscs = new Dictionary<string, string>
    {
        ["SLUS-00158"] =
            "This is King's Field (SLUS-00158), which is a different game. " +
            "This port is Tecmo's Deception (SLUS-00340).",
        ["SLUS-00255"] =
            "This is King's Field II (SLUS-00255), which is a different game. " +
            "This port is Tecmo's Deception (SLUS-00340).",
    },

    RecompilerConfig = "Deception.json",
    // The game assembly, matching Deception.csproj. The launcher assembly is
    // DeceptionLauncher so this process can load Deception.dll into the default
    // context without colliding with itself.
    GameAssembly = "Deception",
    PreviousAssemblies = ["KingsField2"],
    UpdateRepository = "dannyowelch/deception-recomp",

    // title and open are the front of the disc. The other named overlays are
    // play, so the update notice waits. Anything else is neither: a full
    // playthrough is not verified, and this does not invent one.
    PlayAfter = overlay => overlay switch
    {
        "title" or "open" => false,
        "main" or "kokumei" or "bin_3dmd" or "enty" or "plan" or "staff" => true,
        _ => null,
    },
}, args);
