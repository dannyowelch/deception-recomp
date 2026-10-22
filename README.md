# Deception-recomp

A PC port of Tecmo's Deception: Invitation to Darkness (`SLUS-00340`), built with [RecompOne](https://github.com/BlackLabelHQ/RecompOne).

## Features

- Native Windows and Linux
- Full analog gamepad support
- Keyboard and mouse look
- Separate walk and menu controls
- Fully remappable controls
- Memory card backup and restore
- Adjustable render scale
- Windowed, fullscreen, and borderless options
- Anisotropic filtering and mipmaps
- Optional side art on a 16:9 frame
- High-detail textures for walls and doors

## Status

Playable. A full playthrough is not yet verified.

## Requirements

Your own dump of the North American disc (`SLUS-00340`), as cue/bin or CHD v5, and the [.NET 10 SDK](https://dotnet.microsoft.com/download) - only needed to build. No game data, BIOS, or assets are included.

## Building and running

Point `cue` in `config/deception.json/Deception.json` at your disc, then from the repository root:

```bash
dotnet run --project tools/RecompOne/RecompOne.Recompiler -c Release -- config/deception.json/Deception.json
dotnet build Deception.csproj -c Release
dotnet run --project Deception.csproj -c Release
```

To play, set `CdPath` in `settings.json`, or choose the file when the game asks. Settings are described in [docs/CONFIG.md](docs/CONFIG.md). A new version keeps your memory cards and settings. System > Memory Cards can back them up.

## Credits

This project is MIT, Copyright (c) 2026 Danny Welch. See [LICENSE](LICENSE).

[RecompOne](https://github.com/BlackLabelHQ/RecompOne) is MIT, Copyright (c) 2026 flaffy. Verdite Core (`tools/verdite-core`) is MIT, Copyright (c) 2026 Voicedrew11.

This project is not affiliated with Tecmo or Koei Tecmo.
