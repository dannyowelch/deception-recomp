# Sharing Verdite2's work across Verdite1, Verdite2 and Verdite3

What in this repository could serve the other two games (King's Field and
King's Field III in the Japanese numbering), what is tied to this one, and the
running log of the work to share it. The plan, with its phases, gates and rules,
is `docs/SHARING_PLAN.md`. Read that first, then the progress log at the bottom of
this file.

**The classification below is a best guess, made before any second game exists.**
It comes from reading the code: an inventory of every file under `patches/`,
`scripts/`, the launcher, packaging, CI, `mcp/`, `mods/`, `config/` and
`tools/RecompOne`, spot-checked against grep. The real bucket of a patch is known
only once Phase 3 has ported it to a second game, and each entry should move when
that happens. **Inferred** is the standing of everything here unless it says
**Confirmed**.

The per-file detail (hook targets, addresses, struct layouts, env vars, settings
keys, mod-visible members, and where the seam is in each file) is in
`docs/SHARING_INVENTORY.md`. This file is the summary and the decisions.

The current full-coverage plan for Verdite3's native submission and GPU world
renderer is [VERDITE3_GPU_PLAN.md](VERDITE3_GPU_PLAN.md). It targets the final
mesh/pose path from the outset and identifies practical reuse from Verdite2.

## The buckets

- **A. RecompOne fork**: `tools/RecompOne`, the generic PS1 runtime and
  recompiler. Shared in Phase 1.
- **B. Game-agnostic infrastructure**: no knowledge of the game's RAM, code or
  data layouts. It may still carry trivial game strings (an env prefix, an
  assembly name, a path), which are listed.
- **C. A patch whose mechanism might generalize**: the idea is not
  King's-Field-specific, but the implementation reads this game's RAM or hooks
  its code.
- **D. KF2-only**: only makes sense for this game.

For bucket C the couplings are the five kinds in the plan: **hook targets**,
**data addresses**, **struct layouts**, **control-flow assumptions** and
**overlay names and bases**.

## The finding that shapes everything else: one per-game table removes most of the coupling

Most C patches carry the same few facts about this game, restated in each file.
Gathered once, as a per-game data structure each game supplies, they would turn
a large share of bucket C into bucket B. This is **Inferred** from reading: no
second game exists to confirm the counterparts are as regular.

| fact | KF2 value | restated in |
|---|---|---|
| libgpu `DrawOTag` per overlay (the frame boundary) | `open 0x80016078`, `game 0x80060818`, `end 0x80013D80` | `FramePacing`, `Perspective`, `Pgxp`, `Subpixel`, `ZBuffer`, `NoDither`, `Widescreen` (the one `Replace`), `FluidSmoothing`, `remaster/Lights`, others |
| libetc `VSync` thunk per overlay | `open 0x8001EB88`, `game 0x8005FCC8`, `end 0x8001B154` | `FramePacing` |
| libgpu `PutDrawEnv` | `game 0x80060870` (and per overlay in `NoDither`) | `NoDither`, `remaster/Atmosphere` |
| PSY-Q interrupt-callback table per overlay | `open 0x8003DD48`, `game 0x8006E3D4`, `end 0x80038D90` | root `Program.cs` (`0006`) |
| the game's own frame gate and its credit | `func_80017880`, `0x801B6CA8` | `FramePacing` |
| the main loop's stages | 13 stages, gated set `0x80037C0C`, `0x8002A550`, `0x80040348`, `0x80046A60`, `0x8004910C`, `0x80033FBC`, `0x8002DC78` | `FramePacing`, `LoopPacing`, `FullRateLogic`, `FrameProfiler`, `check_gate.py`, `kf2model.py` |
| the renderer (stage 13) | `func_800342D8`, 19 calls, camera block `func_8002E22C` | `Stage13`, `CameraBlock`, `MenuWorld`, `ObjectSmoothing`, `LoopPacing`, the walks |
| the four world tables | creatures `0x8016C544` 200 x `0x7C`, live `u8[+0x9]==1`; objects `0x80177714` 396 x `0x44`, free `u16[+0x6]==0xFF`; effects `0x8019CC6C` 128 x `0x48`, free `u8[+0x0]==0xFF`; sprites `0x80195174` 128 x `0x18`, free `u16[+0x0]==0xFFFF` | `ModelWalk`, `ObjectSmoothing`, `MapMarkers`, `CrashDump`, `SpriteAnim`, `mods/kf2debug` |
| the map | `0x801C8484`, 80 x 80 cells of 10 bytes, two halves; banks `0x8018E18C`, model table `0x8018E19C` (28-byte headers), vertex base `0x8018EAA0`, vertex cache `0x8018EB94` (8 bytes a vertex) | `TileWalk`, `RetainedMap`, `WaterSwell`, `RenderDistance`, `PlanarCull`, `ReflectionReach`, `Map`, `remaster/Identity` |
| light records | `0x801930F0`, 64 used (80 addressed by the remaster) x `0x68` | `PolyAssemblerFog`, `PolyAssemblerLight`, `RetainedMap`, `GpuWorld`, `remaster/Atmosphere` |
| primitive arena and ordering table | descriptor `0x8017E0A4` (`{start,end,current}`, two `0x19000` buffers swapped by `func_8002E064`), OT `0x8018E0A8` | `PrimBuffer`, `DrawCensus`, `FrameCapture`, `ScenePass`, `PlanarWalk` |
| scrolling-texture slots | `0x80192D58`, 8 x `0x18` | `FluidSmoothing`, `Waves`, `Reflections`, `RetainedPlanes`, `remaster/TextureKeys` |
| player and camera | player block from `0x80199414` (position `0x801994EC/F0/F4`, yaw `0x80199506`), camera block `0x80192E18`-`0x80192EA4`, area byte `0x8017E060`, slot `0x8006E5D4` | `Analog`, `Mouse`, `FrameSmoothing`, `AgentBeacon`, `AgentServer`, `AutoReload`, `Map`, `AoWorld` |
| angle convention | 12-bit, `0x1000` a turn, yaw `+0x800` bias in the renderer, pitch clamped `±0x2BC`; strafe along `yaw-0x400`; walk speed `0xC8` | `Analog`, `Mouse`, `ObjectSmoothing`, `ModelWalk` |

Two more facts every C patch already shares and that would go with the table:
how a hook is attached (`HookAttach.OnOverlayLoad`, resolved through
`SymbolRegistry` per overlay), and how a setting is read (`KF2_<FEATURE>` wins
over the saved `kf2.<feature>.*` key).

**The picture switches are the clearest case.** `Perspective`, `Pgxp`,
`Subpixel`, `ZBuffer`, `NoDither`, `TrueColor`, `Anisotropic`,
`AmbientOcclusion` and `EnhancementDistance` hold a switch and a probe; the
mechanism is entirely in the runtime (`0009`-`0014`, `0021`, `0034`-`0036`,
`0040`, `0041`, `0060`, `0083`). With the SDK address table above supplied by
the game, they have no other game coupling. `ZBuffer` is the exception: it also
brackets the arm and model submitters (`func_80032400`, `func_80032588`).

## Bucket A: the RecompOne fork

`tools/RecompOne`, as it stands (upstream `d81dec8` plus the port's `0001`-`0085`).
Phase 1 moves it, unchanged, into a fork repo consumed by subtree.

**It is not yet free of this game.** See "What the fork knows about this game"
below: the port's additions to the runtime read `KF2_*` environment variables by
name and carry some of this renderer's assumptions. None of it needs to change
for Phase 1, which is a move and not a change; it is a list for Phase 2, when a
second game first runs on the fork.

### What the fork knows about this game

The audit is the last section of `docs/SHARING_INVENTORY.md`, with file and line
for each finding. In short:

- **No game code, no game addresses.** Nothing in `0x80010000`-`0x801FFFFF` is
  hardcoded in runtime code, and no executable line names King's Field,
  `KingsField2`, `Verdite` or a serial; those appear only in comments. The
  constants at `0x8000F800`-`0x8000F830` in `LibCd` and `LibDs` are upstream's HLE
  scratch slots in kernel RAM, not this game's (**Confirmed**: the same
  constants are in upstream `d81dec8`).
- **Seven `KF2_*` environment variables are read by name**, every one a
  diagnostic or a comparison: `KF2_CDTRACE` (`PSMemory`), `KF2_GLDEBUG`
  (`GlDebug`), `KF2_GTE_FAST` and `KF2_GTE_LIGHTCACHE` (`Gte`), `KF2_RAM_PROBE`
  (`RamProbe`), `KF2_SWAP` (`HostWindow`) and `KF2_VRAMCHECK` (`VramCheck`). All
  seven are static fields set at type initialisation (five `static readonly`), so a
  prefix the game supplies has to be set before those types are first touched: first thing in the
  game's `Program.cs`, or through an environment variable of the runtime's own.
  Two diagnostic lines print `[KF2]` (`GlTexCache`, `ScreenReflections`).
- **The port's renderer additions carry this game's layout as constants.** The
  mechanism is generic in every case, but these hold only for King's Field:
  - `LibGpu.WalkOTag` draws the retained map when the walk reaches slot 1 and
    treats slot 0 as the sky; `WaterCut` is `4 * (0x1FFF - slot - 0xF0)`, which is
    a 0x2000-entry table with a tile linked at its mean SZ over four plus `0xF0`.
    `GlMainView` repeats it (`TileBias = 0xF0`, `SortBuckets = 0x2000`,
    `z * 0.25f`), as do `RetainedScene.ArmSlot` and `GlModelMeshes`.
  - `RetainedScene`: the 80 x 80 grid of two halves (`HalvesW/H = 160/80`), 8-tile
    chunks, the 2048-unit tile, and 64 light records of 52 ints.
  - `GteDepth`: `AoHeightSpan = 80`, `AoTileUnits = 2048`, `FluidSlots = 8`, and a
    `DepthClearThreshold` of 0 chosen because this game draws one world and a 2D HUD.
    `WaterWaves.MaxRects = 8` has to match `FluidSlots`.
  - `SurfaceMaterial` takes a translucent rect in blend mode 0 or 3 to be water.
  - Tuning in this game's world units: `PlanarReflections.Tolerance` and `Ripple`,
    the AO radius and depth, the wave defaults. These are defaults, not layout,
    and can stay.
- **Settings**: the only keys the port added to the runtime's own sections are
  generic (`ViewConfig.Borderless`, the `DrawSlot` and `AddRightItem` extension
  points, the display-mode strings). The KF2 pages are registered from `patches/`.

None of this blocks Phase 1, which moves the tree unchanged. For Phase 2 it is one
env prefix and one bundle of renderer layout constants (table length, map and sky
slots, tile bias and OTZ divisor, grid, tile size, record shape, fluid slot count,
the water blend modes), supplied by the game. Until the GPU world renderer is
ported to a second game, those constants only need to stay correct for this one.

## Bucket B: game-agnostic infrastructure

Grouped by what each is for. The last column is every game-specific string the
file carries; none of them reads the game's RAM.

### Build, launch, ship

| item | game-specific strings |
|---|---|
| `Verdite2.Launcher/Program.cs`, `Ver.cs`, `Console.cs`, `BuildProgressPopup.cs` | app id `verdite2`, window title `Verdite2 {ver}`, `KingsField2.dll`, `[Verdite2]` console tag, `verdite2.build.*` localisation keys |
| `Verdite2.Launcher/UpdateCheck.cs`, `UpdateBadge.cs` | repo `Voicedrew11/verdite2`, setting `Verdite2.UpdateCheck`, `verdite2.update.*` localisation keys |
| `Verdite2.Launcher/UpdatePopup.cs` | **one piece of game logic**: it waits for overlay `open` or `game` to load before offering an update. The overlay names should come from the game. |
| `Verdite2.Launcher/Build/BuildKey.cs` | the literal `verdite2`; the disc file list (`SYSTEM.CNF`, `SLUS_001.58`, `OPEN.EXE`, `GAME.EXE`, `END.EXE`, `CD/COM/FDAT.T`) |
| `Verdite2.Launcher/Build/DiscCheck.cs` | serial `SLUS-00158`, boot file `SLUS_001.58`, required files and their size floors, the `SLUS-00255` message, `config/kf2.json` (read for the `FDAT.T` floor) |
| `Verdite2.Launcher/Build/GameCompile.cs` | assembly name `KingsField2`; skips `Verdite2` among references |
| `Verdite2.Launcher/Build/Recompile.cs` | `kf2.json`, `kf2.build.json` |
| `Verdite2.Launcher/Build/Paths.cs` | data folder `Verdite2` / `verdite2` |
| `Verdite2.Launcher/Verdite2.Launcher.csproj` | assembly `Verdite2`, icons, the payload globs |
| `packaging/linux/build-appimage.sh`, `packaging/windows/build-windows.ps1`, `verdite2.iss`, `Stub/` | `Verdite2-*` artefact names, `verdite2` icons, the Inno AppId GUID |
| `packaging/shared/` | `verdite2.desktop` (Name, "King's Field", SLUS-00158), the icon art |
| `.github/workflows/ci.yml`, `release.yml` | names; the release body's `SLUS-00255` warning |
| `scripts/release.sh`, `VERSION` | none |
| `scripts/setup_tools.sh` | none beyond paths; rewritten in Phase 1 regardless |
| `mcp/Program.cs`, `KingsField2Mcp.csproj` | **B/C**: the JSON-RPC framing is generic; the tool set is this game's shell vocabulary (`warp <area>`, save slots 1-3, 8192 units ≈ four tiles), `KF2_MCP_ENDPOINT`, `kf2-mcp`, `Kf2.Mcp` |

**The launcher is a shell plus about twenty per-game strings.** Lifted from the
inventory, the record a shared launcher would take from the game:

| datum | KF2 value |
|---|---|
| disc serial; boot file in `SYSTEM.CNF` | `SLUS-00158`; `SLUS_001.58` |
| disc files the recompile reads (and `BuildKey` hashes) | `SYSTEM.CNF`, `SLUS_001.58`, `OPEN.EXE`, `GAME.EXE`, `END.EXE`, `CD/COM/FDAT.T` |
| required-file size floors | each `.EXE` `0x800`; `FDAT.T` from the config's last slice |
| known-wrong discs and their message | `SLUS-00255` |
| recompiler config | `config/kf2.json` |
| game assembly; built DLL | `KingsField2`; `KingsField2.dll` |
| launcher assembly, executable, root namespace | `Verdite2`, `Verdite2.exe`, `Verdite2.Launcher` |
| app id; per-user data folder | `verdite2`; `Verdite2` / `verdite2` |
| env prefixes | `VERDITE2_` (launcher), `KF2_` (game) |
| update repository | `Voicedrew11/verdite2` |
| window title; icons; desktop entry | `Verdite2 {ver}`; none shipped (the card icon off the disc); `verdite2.desktop` |
| package names; Inno AppId | `Verdite2-$VERSION-x86_64.AppImage`, `Verdite2-$v-win-x64.zip`, `-setup`; `{9F1F0C1E-…}` |
| overlays that mean "at the title" | `open`, `game` (`UpdatePopup`) |

What `BuildKey` covers, since Phase 2 has to extend it to shared files: the
literal `verdite2`, the launcher's assembly version, the six disc files by
SHA-256, and every `content/src/**/*.cs` and `content/config/**/*.json` by
relative path and SHA-256. Not the LBAs, the commit or the informational
version. **A shared subtree compiled into the game has to land under
`content/src`, or a change to it will not trigger a rebuild.**

What `GameCompile` assumes, which `Deception.csproj` must keep matching:
assembly `KingsField2`, a console application, unsafe on, nullable on, Release
optimisation, AnyCPU, every warning suppressed but CS5001 (an error), references
from `TRUSTED_PLATFORM_ASSEMBLIES` plus loaded assemblies, and a generated
`GlobalUsings.g.cs` of seven usings.

### Patch infrastructure

| item | game-specific strings | note |
|---|---|---|
| `patches/HookAttach.cs` | `[KF2]` log prefix | Every patch depends on it. |
| `patches/Differential.cs` | none | The verify harness behind every `=verify` mode. |
| `patches/settings/PatchSettings.cs` | `kf2.*` keys, the page list | **B with a seam**: `Install` and `SectionNames` hard-code this game's pages. The framework (`IPatchPage`, `Register`, `RegisterSlot`, `Get`/`Set`, `Note`) is generic. |
| `patches/settings/GameplaySection.cs`, `InputSection.cs` | section ids, localisation | `InputSection` replaces the runtime's `input` pane by id. |
| `patches/settings/BindingTable.cs` | none | A copy of an internal RecompOne type, with its SDL `PadLabel` indices duplicated: it will drift silently when the fork changes. A candidate to expose from the fork instead. |
| `patches/HotkeyGate.cs` | references `Remaster.Editor.Open` | Generic policy, one remaster reference. |
| `patches/Prejit.cs` | ranks overlay names `fdat*`, `game`, `main` | |
| `patches/CrashDump.cs` | **C**, not the B the plan guessed | The dump-on-fault mechanism is generic; every table it dumps is this game's. Splits along that line. |
| `patches/DesktopEntry.cs`, `patches/UiScale.cs`, `patches/MouseIndicator.cs` | app id, `KF2_` prefix, `kf2mouseind` panel id | |

### Diagnostics and measurement

| item | game-specific strings |
|---|---|
| `patches/FrameViewerPanel.cs`, `patches/ProfilerPanel.cs`, `tools/verdite-core/src/GpuFrames.cs`, `patches/TexProbe.cs`, `patches/AudioProbe.cs` | `KF2_` prefix; section names. `FrameViewerPanel` names `FrameCapture`'s slots, so it moves with `FrameCapture`. |
| `patches/RateCensus.cs` | the default RAM window `0x80060000:0x801C0000` |
| `patches/remaster/Snap.cs` | none |
| `patches/FrameProfiler.cs`, `patches/FrameCapture.cs` | **C**: the profiler, capture and replay are generic, but each is seeded with a table of this game's routine addresses (`Known[]`, the slot list). The table is the seam. |

### Picture and sound switches

`AudioQuality`, `Anisotropic`, `AmbientOcclusion`, `TrueColor`,
`EnhancementDistance`, `Perspective`, `Pgxp`: switches and probes over runtime
mechanisms. Their only game coupling is the `DrawOTag` table, where a probe
needs a frame boundary.

### Scripts

| script | bucket | what it hardcodes |
|---|---|---|
| `inspect_disc.py`, `extract_file.py`, `add_call_targets.py` | B | docstring examples only; **moved to Verdite Core in Phase 2** |
| `merge_branch_spans.py` | B | `disc/KingsField2.cue` and `config/kf2.json` as defaults: now from `config/verdite.json`; **moved to Verdite Core in Phase 2** |
| `merge_sdk_names.py` | B | `OVERLAYS = ("open", "game", "end")`, now `sdkOverlays` in `config/verdite.json`; **moved to Verdite Core in Phase 2** |
| `callgraph.py` | B | the generated class prefix `KingsField2(_\w+)?`, `generated/`, `config/funcmaps/` |
| `kf2run.py` | B | `Deception.csproj`, `bin/Release/net10.0/Deception`, `disc/KingsField2.cue` |
| `profile_report.py`, `audio_spectrum.py` | B | none of substance |
| `match_overlays.py` | C | a 17-entry libgpu seed table in `open`, the overlay names; the delta algorithm is generic |
| `find_writers.py`, `check_gate.py`, `rate_census.py`, `rate_matrix.py` | C | the main-loop model through `kf2model.py`; `rate_*` add RAM addresses (object table, death counter, needle) and scenario drivers |
| `light_probe.c`, `shader_probe.c` | C | the shader's uniform and material-table contract, which is the fork's (`0048`, `0067`, `0071`), not the game's |
| `kf2model.py` | D | the keystone: main loop address, stage labels, gate source, generated class. **A per-game JSON for it turns `check_gate.py`, `find_writers.py` and `rate_census.py` into B.** |
| `msg_glyphs.py` | D | the message archive, its ids, the glyph grid, the output class |

## Bucket C: patches whose mechanism might generalize

Clusters, because the patches share data and depend on each other: sharing goes
along this graph, not file by file. The table under "The finding that shapes
everything else" holds the addresses; this section is what each cluster
assumes beyond them.

**Frame pacing and the tick** (`FramePacing`, `LoopPacing`, `LoadPacing`,
`MenuPacing`, `SpriteAnim`, `TintHold`, `FullRateLogic`).
The mechanism (a fixed-step logic clock, a per-stage gate, a frame boundary at
a `DrawOTag` that follows a `VSync`, an absolute pacing deadline, the 500 ms
watchdog, pause as the gate held shut) is generic. The **control-flow model is
not**: KF2 is a loop of thirteen stage functions, stage 13 draws, stages 2-6 and
two of stage 13's sub-steps are world state, and stages 2 and 3 reach stage 13
only through their own modal loops (the two recorded exceptions in
`check_gate.py`). The world runs at 20 ticks a second because the game's speed is its
frame rate and it landed in the three-vblank band on the console (see "The
reference band is 3 vblanks, not 2" in `docs/PATCHES_AND_MODS.md`); its own gate
only forbids faster than two vblanks. AI runs one entity in four. A second game's loop has
to be drawn out stage by stage before any of this transfers, and it may not be
a stage loop at all. `FramePacing`'s full model is in its inventory entry. The
plan already expects this cluster to stay per-game; that is the likely outcome.

**Smoothing** (`FrameSmoothing`, `ObjectSmoothing`, `AnimSmoothing`,
`FluidSmoothing`). Interpolating between two ticks is generic; what is carried is
this game's camera block, its four tables, its MO clip format and its texture
slots. `AnimSmoothing` is the deepest coupling in the repository: it parses the
MO clip table (`bank+0x10`, segment durations) and reads the caller's stack
words (`SP+0x1C/0x20/0x10`) of `func_80034DA8`.

**The renderer taken over in C#** (`Stage13`, `CameraBlock`, `TileWalk`,
`ModelWalk`, `MoPose`, `PolyAssembler*`, `CullGrid`, `CullCone`, `ViewClip`,
`ScenePass`, `PrimBuffer`). Each is a C# transcription of a KF2 routine with a
`verify` mode against the recompiled one. The technique (replace hook, verify
by `Differential`, direct-RAM fast path, GTE save and restore) is generic; the
routines are not, and a sibling game's renderer is a fresh transcription. The
clean extraction boundary is `PolyAssemblerDepth`, `PolyAssemblerLight` and
`PolyAssemblerTexRect`: they fill the runtime's `GtePacketDepth`, `GteLightMap`
and `GteTexRect` records, and those record structs are the interface any game's
assemblers would fill.

**The GPU world renderer and the reflections** (`GpuWorld`, `GpuWorldCensus`,
`RetainedMap`, `RetainedModels`, `RetainedPlanes`, `PlanarWalk`, `PlanarCull`,
`ReflectionReach`, `Reflections`, `RenderDistance`, `Murk`, `Waves`,
`WaterSwell`, `EvenFog`, `PerPixelLighting`). The engine is in the fork
(`0067`, `0068`, `0072`, `0078`, `0085`); these files decode this game's map,
banks, model table and light records into it. `PlanarWalk`, `PlanarCull` and
`ReflectionReach` move as a unit or not at all; `PrimBuffer` is their
prerequisite. "Which VRAM rects are water" is a KF2 fact (the fluid slots) that
several of them assume.

**Input** (`Analog`, `Mouse`, `KeyLayout`, `MenuMouse`). `Analog` writes
velocities into the player block and folds the pad mask through this game's
table; `Mouse` rides `Analog`'s hook and speaks the 12-bit angle. `KeyLayout`'s
machinery (defaults before `ConfigManager.Load`, versioned migration,
`Superseded`) is generic, and only its key map is KF2's. `MenuMouse` needs this
game's menu routines and layout tables.

**Menus and messages** (`MenuWorld`, `MenuDraw`, `MenuPacing`, `MenuMouse`,
`GearCompare`, `MessageText`). One menu API with six routines (`func_80022754`
enter, `func_80022530` frame head, `func_800226A8` presenter, `func_800228C8`
leave, `func_80018E80` modal loop, `func_80022E58` pad read), each patch hooking
a different part. A sibling game re-identifies the API; nothing here is generic
but the ideas.

**The map** (`Map`, `MapFog`, `MapRender`, `MapPanel`, `MapFullscreen`,
`MapOverlay`; `MapMarkers` is D). The three ImGui viewports are generic shells
over one model; `Map` is the only reader of the grid and `MapMarkers` the only
reader of the tables. `Map` and `MapMarkers` never hook and never write: they
read during the game's own `VSync`, the most portable shape in the repository.
The `.fog` file is `"KF2FOG\0"`, version 1, a u16 count, then records of
`(slot u8, area u8, 800-byte bitset)`, written beside the memory card as
`carda.fog` through a `.tmp` and a rename. The container is generic; its record
size and keys are this game's 80 x 80 grid, its areas and its save slots.

**Driving and observing the game** (`AgentServer`, `AgentBeacon`, `AutoStart`,
`AutoReload`, `BlackProbe`, `CrossProbe`, `DrawCensus`, `AnalogProbe`,
`PacketMatch`). `AgentServer`'s transport and two-queue marshal onto the game
thread are generic; almost every verb's body is KF2 state.

**Sound** (`PositionalAudio`). The spatial mix is in the fork (`0044`); the
listener (`0x80198584`), the sound routine (`func_80013D08`) and stage 9 are
KF2's.

**The remaster** (`patches/remaster/`). The pack system (upstream's asset pack
plus a `remaster/` directory: `materials.json`, `surfaces.json`, `lights.json`,
`atmosphere.json`, `level.json`, `props.json`, `layers.json`), the editor, the
shell verbs and the measurement tools are generic in shape. **`Identity` and
`TileField` are the seam**: every key is a KF2 coordinate (`TileKey(area, x, z,
half)`, `ModelKey(area, kind, model)`, light records 0-79) and the area
fingerprint is an FNV-1a 64 over this game's 64,000-byte tile block, taken at
`func_80017244`. Only `Identity` (`0x80017244`), `Lights` (the `DrawOTag` table)
and `Atmosphere` (stage 1, `PutDrawEnv`) hook code. `Host`'s feature list
(`Surfaces`, `Lights`, `Atmosphere`, `Level`, `Props`) is the extension point.

## Bucket D: KF2-only

`AreaWarp`, `BootExe`, `EndingHold` (`END.EXE`'s spin and the boot stub's loader
at `0x80010254`/`0x80010268`), `CardIcon` (this disc's card-icon offsets),
`HitGuard` (`fdat23`'s final-boss fault), `MessageGlyphs` (a generated font hash;
a sibling regenerates its own), `MapMarkers`, `remaster/TileField`,
`remaster/Level`, `remaster/Props` (an object record above 2 MB through
`ModelWalk`), `remaster/SaveCheck` (`0x8006E98C`, `func_80049A88`),
`config/kf2.json` (63 SDK bindings by address, per overlay), `config/funcmaps/`
(13 maps, 2,225 functions over the boot stub, the three executables and the
`fdat` modules), `docs/GAME_INTERNALS.md`, `mods/kf2debug` (about 40 RAM
addresses and 25 routines), `scripts/kf2model.py`, `scripts/msg_glyphs.py`, and
the root `Program.cs`.

**`Program.cs` is the composition root and stays per-game.** It holds the 74
`Install()` calls, the IRQ table per overlay and the diagnostics, and reads 223
distinct `KF2_*` names itself (the patches and mods read another 80; the two
sets overlap). Each game writes its own.

`AreaWarp` is borderline: warping by the area loader is a general idea, but
nothing in it survives the change of game.

## What is game-specific in the infrastructure

- **The `KF2_` prefix.** `docs/ENV_VARS.md` names 244 distinct `KF2_*` variables
  (the plan's 216 was an earlier count); `Program.cs` reads 223 and
  `patches/` plus `mods/` 80. Shared code must take the prefix from the game, and
  every name must survive unchanged in Verdite2.
- **The `KingsField2` names.** Assembly `KingsField2`, project
  `Deception.csproj`, the generated classes `Recompiled.KingsField2_game`,
  `_open`, `_end`, `_fdat`, `_main`, the process name `KingsField2` (which
  `pkill` and `dotnet-stack` find), `KingsField2Mcp`.
- **The namespace `Kf2`** (patches), `Kf2.Mcp`, `Kf2.Mods.*`, and the
  `Verdite2.Launcher` namespace.
- **Settings keys** are `kf2.<feature>.<name>` in `interface.ini`, plus the
  launcher's `Verdite2.UpdateCheck`. Localisation keys: `verdite2.build.*`,
  `verdite2.update.*`, `settings.gameplay`, `settings.display`, and `kf2.*` keys
  in the distance, even-fog, fast-geometry, reflections, remaster and Z-buffer
  pages, each in en, pt-BR and es-419.
- **Disc identity**: `SLUS-00158`, `SLUS_001.58`, the `SLUS-00255` refusal
  (`DiscCheck`, `release.yml`), `disc/KingsField2.cue` (scripts, docs, the MCP
  instructions).
- **The command channel**: port `27900`, the shell's verbs.
- **The overlay names** `open`, `game`, `end`, `fdat*`, `main`, in `Prejit`,
  `UpdatePopup`, `merge_sdk_names.py`, every hook table.

### What a new repo's `.gitignore` must copy

`disc/*` (with `!disc/README.md`), `generated/`, `bin/` and `obj/`,
`tools/RecompOne/**/bin/` and `**/obj/`, the PSY-Q signature bank
(`…/AutoConfigure/signatures/psyq.json`), `*.sav`, `settings.json`,
`interface.ini`, `*.fog` and `*.fog.tmp`, `packs/`, `exports/`, `dump/`,
`mods/.cache/`, `scratch/`, `*.log`, `ratecensus.txt`, `dist/`, `__pycache__/`
and `*.pyc`. The ones that hold copyrighted or derived data are `disc/`,
`generated/`, `dump/` (the texture dumper writes the disc's art), `packs/` and
`exports/` (they can carry it), and the saves. `tools/RecompOne.git/` goes away
in Phase 1.

### What mods can see

The two mods in the tree reach the game through `Recompiled.KingsField2_game`
directly (`func_80048178`, `func_8002C3A8`, `func_80024CAC`, `func_800244CC`),
`Kf2.AreaWarp` (`Areas`, `CutArea`, `TryRun`), `Kf2.MouseIndicator.Suppressed`
and `Kf2.Settings.PatchSettings`; everything else they use is RecompOne's.
Third-party mods can reach any public member, and nearly every patch exposes
`Enabled`, `Configure`, `Install` and `SetEnabled`, so the safe assumption is that
**every public type under `Kf2` is mod-visible**. Moving one needs a forwarding
type or a wrapper under the old name. The inventory lists each file's public
surface.

## Corrections to the plan's guesses

- `CrashDump` is C, not B: its tables are this game's.
- `FrameProfiler` and `FrameCapture` (with `FrameViewerPanel`) are C: each is
  seeded with a table of this game's routines.
- `mcp/` is B/C: the transport is generic, the tool vocabulary is this game's.
- `UpdatePopup` carries one piece of game logic (the overlay names that mean "at
  the title").
- `Perspective` and `Pgxp` are nearly B: the mechanism is all in the fork.
- `BindingTable` copies an internal fork type and will drift; exposing it from
  the fork would be cleaner than sharing the copy.
- The env var count is 244 names in `docs/ENV_VARS.md`, not 216.
- **Frame pacing is the same mechanism in Verdite3, and it is not data** (Phase 3,
  2026-10-02). Verdite3's loop is also a stage loop with one drawing stage, but it
  has fifteen stages, its gate asks for 4 vblanks, and its gated set is "every
  stage but the last" chosen by call site and latched once per iteration rather
  than a list of functions. The two `FramePacing`s stay per game; only the clock,
  the floor and the boundary rule are line-for-line the same. See the progress
  log.

## Progress log

### 2026-10-04: Verdite3 complete native/GPU renderer plan

**Planned, not implemented.** The user wants complete coverage without repeating
Verdite2's recompiled-only, hand-written C#, and GPU renderer sequence.
`VERDITE3_GPU_PLAN.md` records the final target, source-backed native function
inventory, practical reuse from the final Verdite2 implementation, game-specific
fog/light/subdivision differences, phase gates, and whole-game coverage criteria.
Reference native replacements support verification and fallback; the primary
implementation is the retained GPU world path from the outset.

**Inspected.** Verdite2 `ebd5412`, Verdite3 `130f870` plus existing uncommitted
pacing/settings/docs changes. Shared `GteLightMap`, `AoGeometry` and `GteDepth`
sources match; shaders/backend contain some later Verdite2 fixes. No game was
run, no implementation or subtree pin changed, and no visual judgement made.

**Open before implementation.** Full caller/context census, all 28 areas' content
and variants, preview ownership, sky redraw, native submission side effects,
mutable assets, shared runtime parameterisation, and exact near/fog semantics.
The plan distinguishes renderer completion from water/reflection/remaster feature
completion. Those extensions use the same scene contract and need their own gates.

### 2026-10-02: Phase 0, the inventory

**Done.** `docs/SHARING_PLAN.md` (the brief) and this file, with the per-file
inventory in `docs/SHARING_INVENTORY.md`. No code changed. The inventory was made
by reading each file and checked against grep for addresses, env vars and
overlay names. The table entries for `ModelWalk`'s four tables, `Perspective`'s
hooks and the scripts' hardcodes were read against the source.

**Shared repos and pins.** None exist yet. Verdite2 vendors `tools/RecompOne` at
upstream `d81dec8` plus `0001`-`0085`.

**Measured.** Nothing: Phase 0 makes no change to measure.

**Needs your eyes.** This document, which is Phase 0's gate.

**Open, for Phase 1's gate.** The release that has shipped and is tagged is
`v0.3.3` (`VERSION` 0.3.3). `v0.4.0-staging` carries 54 commits not on `main`,
and this branch (`vendor-veditecore`) starts from it. The plan's gate needs the
current release shipped and no unreleased staged work. **Decided (2026-10-02):**
staging is settled enough to build on, so Phase 1 starts from `v0.4.0-staging`
without waiting for `v0.4.0` to ship; anything that lands on staging later is
rebased in.

**Decided (2026-10-02), for Phase 1.** The fork is a **standalone** repo (not a
network-linked GitHub fork of `BlackLabelHQ/RecompOne`, so nothing offers a
one-click PR upstream) named **`Voicedrew11/verdite-recompone`**. It does not
exist yet; Phase 1 creates it, with your go-ahead. **Verdite Core is
`Voicedrew11/verdite-core`** (renamed from `verdite_core` on 2026-10-02), which
already exists and is empty; it stays for Phase 2, apart from the fork, as the
plan says.

**Seen on GitHub, to confirm in Phase 2.** The repo descriptions name the discs:
`verdite1` is King's Field `SLPS-00017`, `verdite3` is King's Field II
`SLUS-00255`, and there is a `verdite4` for King's Field: The Ancient City
(`SLUS-20318`, a PS2 game, so not on RecompOne). `verditeST` exists and is empty.
Phase 2 still asks before taking any of these as the target.

**Decided (2026-10-02), for Phase 2.** Only one game is brought up at the start:
King's Field III in the Japanese numbering, **Verdite3**. Verdite1 waits. Which
disc Verdite3 targets (the US `SLUS-00255`, sold as "King's Field II", or the
Japanese release) is still to be asked when Phase 2 starts.

**Open, for Phase 2.** The fork reads seven `KF2_*` names itself and carries this
game's ordering-table and grid layout in its renderer additions (see "What the
fork knows about this game"). Phase 1 moves it unchanged; the first game after
this one will need the prefix, and (once the GPU world renderer is ported) the
layout, to come from the game.

**Corrected on review.** Three things the inventory's first draft got wrong, fixed
in `docs/SHARING_INVENTORY.md`: the `LibCd`/`LibDs` scratch addresses are
upstream's and not this game's link; `FramePacing`'s tick contract is a patch
(C), not runtime infrastructure; and the settings pane `InputSection` replaces
lives in the vendored tree, not a gitignored checkout.

### 2026-10-02: Phase 1, the fork extracted

**Done, locally.** The fork's history is built and Verdite2 takes `tools/RecompOne`
from it as a `git subtree --squash`. The working clone of the fork is
`~/Desktop/verdite-recompone` (branch `main`, remote `upstream` =
`BlackLabelHQ/RecompOne`, `origin` = `Voicedrew11/verdite-recompone`).

**How the history was built: the preferred approach, and it worked.** `git subtree
split --prefix=tools/RecompOne` of this branch gives 89 commits with one root (the
vendoring import, `7c198b5` here) and one merge of our own. Two grafts, baked in
with `git replace --graft` and `git filter-branch` over the split range only (a
whole-history rewrite re-hashed upstream's signed merge commits too and moved the
merge base, so the range is `main ^upstream/master`): the import onto upstream
`0409bc2` (the `UPSTREAM` it was vendored at), and the `d81dec8` harvest
(`1138329` here) as a real merge with upstream `d81dec8` as its second parent.
That was the only other commit that changed `UPSTREAM`. Checked:
`git merge-base main upstream/master` is `d81dec8`, the `UPSTREAM` sha; the
fork's `07f6537` has the tree of this branch's `tools/RecompOne` at `ea10fb6`
(`dd9c56b`), exactly.

**What upstream tracks that the fork does not.** Two files, both deliberate:
`RecompOne.Recompiler/AutoConfigure/signatures/psyq.json` (15.7 MB; gitignored
here, now gitignored in the fork too) and
`RecompOne.Runtime/Host/Window/Assets/NotoSansCJK-Regular.otf` (16.5 MB; left out
by `0033`, see `FontSet.cs`). The fork tracks 33 files upstream lacks: `UPSTREAM`
and the port's `.cs` additions.

**The fork's commits.** `07f6537` is the move (the tree above). `bbbf56b` adds what
is the fork's own: a fork section at the top of `README.md` (what it is, no
pull requests or issues upstream, the two files left out; upstream's `LICENSE`
untouched), `psyq.json` in its `.gitignore` (its `bin/`/`obj/` lines were already
there), and `harvest_upstream.sh`, which replaces `--sync-upstream` and carries
its acceptance-check text word for word.

**Verdite2's commits on `vendor-verditecore`.** `f4ee759` removes the tracked
tree; `8698a4d` adds it back as a subtree of `07f6537` (squash `b64c50f`).
`git diff ea10fb6 8698a4d` is **empty**, the whole repository and not only
`tools/RecompOne`. `50ac62e` is the game side: `setup_tools.sh`, `.gitignore`,
the docs, the CI and packaging wording. `8c000de` pulls `bbbf56b` through the new
`--pull-fork` (squash `6819790`); after it the subtree's tree equals the fork's
`main`. `tools/RecompOne.git/` is out of the repository: moved to
`~/Desktop/RecompOne.git.old`, not deleted, because its `kf2` branches (the
39-patch stack as commits) exist nowhere else. Delete it when you like.

**Pins.** Verdite2 is at fork `bbbf56b`. No other game exists yet.

**`--squash`, from the round-trip test.** With squash, push, pull and a second
push each came out as one linear commit (see "Why `--squash`" in
`RECOMPONE_FORK.md`). Full history would put the fork's 16 MiB pack, nearly all
of it upstream's, into every game. **The first push carried this repository's
whole history into the fork**: `git subtree split` maps the `git rm` commit to
itself. The add commit now carries `git-subtree-mainline`/`git-subtree-split`
trailers, which fixed it; the test branches were deleted and the fork's objects
pruned (`f4ee759` is not in it). See "The add commit says where the split
starts" in `RECOMPONE_FORK.md`. `--push-fork` also refuses a commit that touches
`tools/RecompOne` and anything else.

**Measured.**
- **Hashes.** As built, all four assemblies differ before and after, and **only
  because the commit is stamped into them**: the SDK writes `1.0.0+<HEAD sha>`
  into the informational version and SourceLink writes it into the PDB, whose
  id and checksum the DLL records (72 bytes differ, same size); the launcher
  also writes `git rev-parse` into its own version on purpose (`VERDITE2_BUILD`).
  Built with that held fixed
  (`-p:IncludeSourceRevisionInInformationalVersion=false
  -p:EnableSourceControlManagerQueries=false -p:VERDITE2_BUILD=pinned`), `ea10fb6`
  and the final HEAD give the same bytes: `RecompOne.Runtime.dll` `11acf1a5…`,
  `recompone.dll` `0bb5abaa…`, `KingsField2.dll` `8e83a91a…`, `Verdite2.dll`
  `a53e5769…`. Plain builds before, for the record: `91fe4123…`, `83581ff6…`,
  `3720d153…`, `a7fb546d…`.
- **Fresh clone.** Cloned with `origin` removed and `VERDITE_FORK_URL` pointed at
  an unresolvable host: `setup_tools.sh` and the launcher build, 0 errors.
- **Packaging.** `packaging/linux/build-appimage.sh` makes
  `Verdite2-0.3.3-x86_64.AppImage` (44 MB).
- **Acceptance.** `open → game → fdat02 → fdat05`, slot 2 at hp 46/86 in area 1,
  144.0 fps drawn at 19.9-20.8 ticks/s, `[present] wide 288, plain 0, vram
  fallback 0`, the vertex map at 18720 caught/s and 100.0% hit (standing, not
  moving: the scripted walk did not move the player), pacing at 15 hooks with the
  boundary 3/3 and 7/7 stages, no exceptions; `scripts/check_gate.py` 0
  violations.

**Needs your eyes.** The picture in a normal run, the widescreen margin's clear
in particular. The binaries are the same bytes, so this is only the plan's
belt-and-braces check.

**Pushed (2026-10-02, with your go-ahead).** The fork's `main` (`bbbf56b`) to
`Voicedrew11/verdite-recompone`, a standalone repo; then `bash
scripts/setup_tools.sh --pull-fork` here, from GitHub, reported the subtree
already at `bbbf56b`. This branch, `vendor-verditecore`, to Verdite2's origin. Not
merged to `main`.

**Open.**
- `patches/recompone/*.patch` and `docs/RECOMPONE_PATCHES.md` stayed here. They
  belong in the fork, in Phase 2: the fork's own source names these patch
  numbers, and points at this repo's `docs/` in 9 places, and a second game's
  copy would cite a record it does not have. Phase 1 is a move, so they did not
  go now.
- `git subtree` is a separate package on Fedora (`git-subtree`), so a new machine
  needs it before `--pull-fork`/`--push-fork`. The build does not need it.

### 2026-10-02: Phase 2 begun, Verdite3 laid out

**Decided (2026-10-02).** Phase 1 lands in `v0.4.0-staging`, which is
fast-forwarded to `vendor-verditecore` (locally; not pushed). Verdite3 targets
**`SLUS-00255`** (the US "King's Field II", the Japanese *King's Field III*).
Its names: assembly `KingsField3`, env prefix `KF3_`, MCP project
`KingsField3Mcp`, disc `disc/KingsField3.cue`. Its working clone is
`~/Desktop/verdite3`. Verdite Core is subtree'd at **`tools/verdite-core`** in
every game, beside `tools/RecompOne`. The patch record moves into the fork.

**The patch record is the fork's.** Fork `a617cf8` adds `patches/` (the 85
diffs and `assets/`) and `docs/RECOMPONE_PATCHES.md`, with a note that the other
`docs/` it names are Verdite2's. Here: `fd1d200` pulls it, `2852ed5` deletes
`patches/recompone/` and `docs/RECOMPONE_PATCHES.md` and repoints 43 files at
`tools/RecompOne/patches/` and `tools/RecompOne/docs/RECOMPONE_PATCHES.md`
(source comments, docs, and the AppImage and Windows scripts, which ship
`NotoSans-OFL.txt` from there). Sentences that describe the old replay keep the
old path, since they describe history.

**Verdite Core started.** `Voicedrew11/verdite-core` `536167a` (local clone
`~/Desktop/verdite-core`): `LICENSE` (Verdite2's MIT), a README, and the five
scripts a bring-up uses, `inspect_disc`, `extract_file`, `add_call_targets`,
`merge_branch_spans` and `merge_sdk_names`, plus `verdite_game.py`, which finds
the game's root (`VERDITE_GAME_ROOT`, else the first directory up holding
`config/verdite.json`) and reads it. What became game input: the disc path,
the recompiler config (`merge_branch_spans` now resolves each `funcMap`
relative to that config's directory, as the recompiler does, not `REPO/config`),
the funcmap directory, and `merge_sdk_names`' overlay list (`sdkOverlays`).
`inspect_disc` and `extract_file` need no config, so they run in a game that has
none yet. Here: `08447b9` adds the subtree (squash `2254b03`); `f803010` makes
`scripts/<name>.py` a wrapper of each that runs the shared one with
`VERDITE_GAME_ROOT` set, adds `config/verdite.json`, and points
`match_overlays.py` and `msg_glyphs.py` at the shared modules. `setup_tools.sh`
gained `--pull-core`/`--push-core` (`9931313`), the URL defined beside the
fork's.

**Verdite3 laid out** (`~/Desktop/verdite3`, branch `main`, local only):
`4f809d6` adds `tools/RecompOne` at fork `a617cf8` (its tree equals Verdite2's
copy exactly), `bb1290a` the skeleton (`.gitignore` with every protection
Verdite2's has, `README.md`, `AGENTS.md`, `NOTES.md`, `disc/README.md`, and
`docs/` with `DEVELOPMENT`, `RECOMPILATION`, `GAME_INTERNALS`, `ENV_VARS` and
`TODO`, which holds the bring-up order), `4a3f4af` adds `tools/verdite-core` at
`536167a`, and `d9c34bf` its `config/verdite.json` (`sdkOverlays` empty until
the disc says which executables link PSY-Q). Neither game ever had these
prefixes, so the subtree adds needed no `mainline` trailers: `git subtree split`
in Verdite3 gives exactly `a617cf8` and `536167a`, and in Verdite2 `536167a`.

**Pins.** Verdite2 and Verdite3: fork `a617cf8`, Verdite Core `536167a`.

**Pushed (2026-10-02, with your go-ahead).** The fork's `main` (`a617cf8`), Verdite
Core's first `main` (`536167a`), Verdite3's `main`, and here `vendor-verditecore`
and `v0.4.0-staging` (both `d03ac2c`, fast-forwards).

**Measured.**
- Old against new script, on this disc, from the repo root: `inspect_disc` (45
  lines), `extract_file --header-only GAME.EXE`, a full `OPEN.EXE` extract
  (`cmp`), `merge_branch_spans --dry-run` (all overlays, and `fdat17`; the same
  again from `docs/`), `add_call_targets` on a copy of `open.json` (the map
  written is identical; the one line differing is the scratch path it names),
  `merge_sdk_names` against copies of the maps with a synthetic autoconfigure
  directory (162 renames, the same maps), and `match_overlays --libgpu` (60
  lines): **identical**. `add_call_targets --help` differs only in its
  docstring's example disc name. `msg_glyphs` stops at the missing `tesseract`
  binary in both versions, so only its imports are shown to resolve. Nothing
  wrote to `config/funcmaps`.
- `--push-fork` against a scratch bare copy of the fork: "Everything up-to-date",
  so the split from this branch is `a617cf8` and carries no game commit.
- `dotnet build Deception.csproj -c Release`: 0 errors;
  `packaging/linux/build-appimage.sh`: exit 0. The game was not run: the only
  runtime change is comments and the patch record's location.

**Needs your eyes.** Nothing on screen. Verdite3's `README.md`, `AGENTS.md`
and `NOTES.md` are new prose.

**Open.**
- **Verdite3's disc is in place and read** (`disc/KingsField3.cue`, volume
  `SLUS-00255`; Verdite3 `6797f9f`, "What is on the disc" in its
  `docs/RECOMPILATION.md`). It has Verdite2's shape: a 4 KiB boot stub and
  `OPEN`/`GAME`/`END.EXE`, all at `0x80011000`; `GAME.EXE` is 0x8B800 bytes of
  text against 0x5E000 here. Next, in Verdite3: `config/kf3.json`, the sweep,
  the signature bank, recompile, boot. The launcher, packaging and CI move into
  Verdite Core only once Verdite3 needs them.
- `~/Desktop/verdite1`'s `kf1-port` branch was an experiment, not Verdite1's
  start; ignore it. Verdite1 still waits.
- `docs/RUNTIME.md` still opens by saying `tools/RecompOne/` is gitignored, which
  has been untrue since the vendoring. It predates this phase.

### 2026-10-02: Verdite3 boots, plays, saves and loads

**Done when, met** (the picture is yours to confirm). Verdite3 (`~/Desktop/verdite3`,
`main`, local) recompiles `SLUS-00255` and runs it from boot through
`OPEN.EXE`'s intro and title, the memory card screen, into `GAME.EXE` and an
area, between areas, and saves to and loads from card A, in game and from the
title. Its acceptance test is "The acceptance test" in its `docs/DEVELOPMENT.md`.
Commits there: `acf4873` (Verdite Core, below; `a6c2434` in Verdite Core), `9b137e8` (the recompile:
`config/kf3.json`, the maps, `KingsField3Recomp.csproj`, `Program.cs`,
`scripts/setup_tools.sh` ported from here), `a17c0fa` (docs).

**Pins.** Verdite3: fork `a617cf8` (unchanged, no fork commit was needed),
Verdite Core **`a6c2434`**, which is `536167a` plus one commit (`acf4873` in Verdite3).
Verdite2: fork `a617cf8`, Verdite Core `536167a`; nothing here changed but this
entry.

**What carried over from Verdite2, and what did not.**
- The disc has this disc's shape: three executables at `0x80011000` as overlays,
  per-area code in `CD/COM/FDAT.T` at entries `3n+2` (28 modules, all linked for
  `0x801E8308`, a module pointer at `0x8018FAE0` dispatched through slot 8),
  empty entry groups where areas were cut. The techniques (scoring a base by its
  slot targets, the overlay delta, data-side search, constants as evidence)
  all worked unchanged.
- **The PSY-Q libraries are a newer build.** Not one of the routines identified
  here matches there by `match_overlays`' normal form, not even the libcd thunks,
  so every address was found again: 516 names from the signature bank, the
  rest by hand. `CdControl`/`F`/`B` are full functions; `CdRead` is a retry
  wrapper; libapi is the 4.x interrupt manager, whose callback table is
  `intrEnv + 4` (the layout upstream's fallback assumes; `Program.cs` sets it per
  executable anyway). The same 21 entry points are bound, 63 in all, and the
  recompiler reports `applied 63 patches, 0 reimplementations`.
- **The executables' text runs on into data, and the sweep makes functions of
  it.** In `GAME.EXE` the data "functions" branch back into real code. The maps
  are cut at the end of code before the `jal` harvest.

**Verdite Core `a6c2434`: three fixes to `merge_branch_spans`**, all found on
Verdite3's `GAME.EXE`, where the old script would have merged half the
executable into one function: a switch table is bounded by the `sltiu` guarding
its index (a 17-entry table read on into a data pointer); `jal`s are counted
only from inside known functions (the rule `add_call_targets` already had); an
absorbed start reached by fallthrough is not reported as lost. **Measured on
this repo:** the old and new script, run from Verdite2's pre-merge maps
(`e41f53d^`) with `e41f53d`'s config, print the same output and write the same
maps, which equal the ones `e41f53d` committed; `--dry-run` on today's maps is
identical, all overlays and `fdat17` from `docs/`.

**Measured** (Verdite3): the overlay sequence `open` → `game` → `fdat02` →
`fdat14` in the log, each executable's callback table set as it loads, the title
load as `game` → `open` → `game`; `carda.sav` holding `BASLUS-002551`, 3 blocks,
titled `KING'S FIELD 2-1 EXP 0 LV 1`; no `unmapped call`.

**Needs your eyes.** You played it: intro, title, the memory card screen, the
short video, the first area and the next, a save and two loads. Nothing more is
outstanding for the done condition. The movies were seen only in passing, and
`END.EXE` has not been reached.

**Open.**
- **Pushed (2026-10-02, with your go-ahead):** Verdite Core `main` `536167a..a6c2434`
  (from Verdite3, `setup_tools.sh --push-core`; its tree equals Verdite3's
  `tools/verdite-core`), Verdite3's `main`, and here `v0.4.0-staging`. Verdite2
  pulls `a6c2434` only when you decide; it changes no output here.
- **The world runs at 60**, once per drawn frame. A world clock like
  `FramePacing` needs this game's frame gate and stages found first.
- **The fork reads seven `KF2_*` switches by name.** Verdite3 inherits them under
  those names. Taking the prefix from the game is the first fork change Verdite3
  will want; it must keep this repo's acceptance test passing.
- Verdite3's acceptance test still needs a person at every step: it has no
  scripted pad, auto start or state beacon yet. Those (`KF2_AUTOPAD`,
  `KF2_AUTOSTART`, `KF2_AGENT` here) are the Phase 3 candidates that would make
  it a program.
- Launcher, packaging and CI stay out of Verdite Core: Verdite3 does not need
  them yet.

### 2026-10-02: Phase 3 begun in Verdite3: the agent harness, then frame pacing

**The unit you picked**: the scripted acceptance harness, then `FramePacing`,
with your warning that this game's world clock is 15, not 20. **It is 15.**
Verdite3 (`~/Desktop/verdite3`, `main`) commit `4f1299b`, local, not pushed.
Nothing in Verdite2 changed but this file.

**Pins, unchanged.** Verdite3: fork `a617cf8`, Verdite Core `a6c2434`. Verdite2:
fork `a617cf8`, Verdite Core `536167a`. No shared-subtree commit was made.

**Found in Verdite3** (written up in its `docs/GAME_INTERNALS.md`, "The session
and the main loop", "The player", "Saves and the start menu"), all by behaviour,
not by matching Verdite2's code:
- The main loop at `0x80014F24`: fifteen stages; **only stage 15
  (`func_800422B8`) writes the ordering table** (measured: stages 1-14 change 0 of
  its 8192 words). Stage 15 ends in the swap `func_80035700` and **the frame gate
  `func_80019614`, which waits for 4 vblanks: 15 frames, and so 15 world steps, a
  second at most.** Its count comes from a vblank event handler
  (`func_80019570`, RCntCNT3/EvSpINT), the same arrangement as Verdite2's
  `func_80017850`/`func_80017880`, with 4 where Verdite2 has 2.
- **The fork delivers that event twice a vblank** (measured 120.0/s), the row
  already in this repo's `docs/TODO.md` ("RCNT3 is delivered twice per vblank").
  In Verdite3 it is not academic: the gate passed every two vblanks, so **the port
  had been running the world at 30, not the 60 its docs said** (1200 units of yaw
  a second holding Left, against 600 at 15).
- The player block at `0x801B24E4` (EXP, level, HP/MP and their maxima, position
  `0x801B25F0`, heading `0x801B260A`), the area byte `0x8018FAE4`, the slot
  `0x8009C2C0`; the card loader `func_8002860C(slot)` (returns 0/1/2, like
  Verdite2's); the start menu `func_8001FA60`, which loads only if OPEN.EXE's
  title left 1 in the stub's byte `0x800102FA`, through the slot chooser
  `func_8001FC5C`.

**Built in Verdite3** (its `docs/DEVELOPMENT.md`, "Driving the game without a
person", "Frame pacing"): `KF3_AGENT` (the beacon, with a `loop` field: the main
loop seen in the last second), `KF3_SHELL` (port 27903: `state`, `press`, `peek`,
`dump`, `help`), `KF3_AUTOSTART=<slot>|new` (the title's byte set, the chooser
replaced by the game's loader: no input needed in GAME.EXE), `KF3_AUTOPAD`, and
`FramePacing` under `KF3_FPS` (**off unless set**), plus two diagnostics,
`KF3_STAGEPROBE` and `KF3_RATECENSUS`.

**Measured** (slot 1, `fdat02`, standing; yaw for 1 s of Left, three times):

| `KF3_FPS` | drawn | ticks/s | yaw/s |
|---|---|---|---|
| unset | 30 | 30 | 1200 |
| 15 | 15.0 | 15.0 | 600 |
| 60 | 60.0 | 15.0 | 600 |
| 144 | 144.0 | 15.0 | 600 |
| off | 1225.5 | 15.0 | 600 |
| 144, boundary removed (`KF3_PACING_NOBOUNDARY=1`) | - | 14.7-14.8, watchdog | 600 |

Every hook reported installed; packets drawn per frame are the same on ticked and
idle frames (418/418 at 144), so no skipped stage feeds the picture; no
`unmapped call`. The scripted acceptance pass is `KF3_AUTOSTART=1 KF3_AGENT=1
KF3_FPS=144 KF3_FPS_PROBE=1`: `open → game → fdat02`, slot 1, HP 50/50, LV 1,
144.0 fps at 15.0 ticks/s.

**The comparison, and why nothing was extracted.**
- *Same mechanism, different data*: `HookAttach` (only the log prefix differs),
  `AgentServer`'s transport (line cap, queue, vblank drain, `PAD_dr` injection,
  JSON quoting; the verbs are the game's), the beacon's emitter, the autopad
  parser. These are the extraction candidates.
- *Different mechanism*: `AutoStart` (Verdite2 rides a New Game and loads over
  it from stage 3; Verdite3 answers the start menu's own question), and
  `FramePacing` (above, "Corrections"). They stay per game.
- **Extracting the first group is the first C# Verdite Core would hold**, and
  Verdite2 compiles its patches twice: in `Deception.csproj` and in the
  launcher's first-run `GameCompile`, whose payload and `BuildKey` would have to
  carry `tools/verdite-core` sources too. That is a launcher change, and
  `Kf2.AgentServer` is mod-visible. I stopped there to ask rather than make it.

**Needs your eyes** (Verdite3, `KF3_FPS=60` or `144`): the picture in an area
(it changes 15 times a second: nothing is carried between ticks yet), the
in-game menu and the opening movie under pacing, and whether the world's speed
at 15 looks like the console's.

**Open.**
- **My scripted Cross presses saved over card A slot 1** in Verdite3 (it held a
  new game's first save and still does, a few steps further on). Cross opens the
  in-game menu; this is now written in its `docs/DEVELOPMENT.md`.
- The double vblank delivery is a fork defect; fixing it changes Verdite2 when it
  pulls (the title music question in its `docs/TODO.md` row). Your call.
- Under pacing, what stage 15 advances runs at the render rate: billboard cels at
  `0x80182964` (`func_80040AE4`), the `SpriteAnim` shape, and eight unidentified
  words (`KF3_RATECENSUS`).
- Still by hand in Verdite3: changing areas, saving, the title-screen load;
  `load`/`warp` verbs; `KF3_PRESENT_PROBE` wiring.
- Nothing pushed: Verdite3 `main` is one commit ahead of `origin/main`, and this
  repo one ahead of `v0.4.0-staging`'s remote.

### 2026-10-02: the double vblank event, measured in both games and fixed in the fork

**Fork commit `2013e51`, made in Verdite3 as `0825391`** (its own commit,
amending `0021`; pushed 2026-10-02 with your go-ahead, `a617cf8..2013e51`). `LibEtc.TickVBlank` delivered the vblank root counter's event
(`0xF2000003`) and then raised IRQ 0, whose service delivered it again, so every
handler on it ran twice a vblank. IRQ 0 alone delivers it now, as on upstream's
blocking timeline.

**Measured**, before → after:

| | before | after |
|---|---|---|
| Verdite2, `func_80017850`'s clock `0x801B6CAC` in an area | 120.0/s | 60.0/s |
| Verdite2 acceptance (slot 2, `KF2_FPS=144`, presents, perspective) | fdat05, HP 46/86, 144.0 fps, 20.0 ticks/s, wide 288, 100% hit, no failed hook | the same |
| Verdite3, `func_80019570`'s count `0x801C12E8` | 120.1/s | 60.0/s |
| Verdite3 unpaced: yaw a second holding Left (world rate) | 1200 (30 Hz) | 600 (15 Hz) |
| Verdite3 at `KF3_FPS=144` | 144.0 fps, 15.0 ticks/s, 600 | 144.0 fps, 14.9 ticks/s, 600 |

During OPEN.EXE no handler on that event ran in Verdite2, so the title-music
question in its `docs/TODO.md` did not arise in that run.

**Verdite2 is unchanged**: the fix was applied to its working tree only for the
"after" run, then reverted and rebuilt. **What pulling it would change** is
`0x801B6CAC` at the console's 60/s, and so the interval at which per-object
ambient sounds retrigger (`vbl + 6 * (u16 at rec+0x3E)`, stage 13's object pass
and `ModelWalk`): about twice as long as now. An ear question, yours.

**Pins.** Verdite3: fork **`2013e51`** (its tree equals Verdite3's
`tools/RecompOne`), Verdite Core `a6c2434`. Verdite2: fork `a617cf8`, Verdite Core
`536167a`; it pulls `2013e51` only when you decide. Pushed with your go-ahead:
the fork, Verdite3's `main` and this branch.

### 2026-10-02: Phase 3 in Verdite3: the geometry path surveyed

**The unit you picked**: survey Verdite3's geometry path against what Verdite2
rewrote in C#, routine by routine, and build nothing yet. Done in Verdite3
(`~/Desktop/verdite3`, `main`), local commits only. The findings are its
`docs/GAME_INTERNALS.md`, "The geometry path"; this entry is the comparison, the
recommendation and the proposal for shared C#.

**Pins, unchanged.** Verdite3: fork `2013e51`, Verdite Core `a6c2434` plus one
local commit (`match_code.py`, below; not pushed). Verdite2: fork `a617cf8`,
Verdite Core `536167a`. Nothing in Verdite2 changed but this file.

**Built in Verdite3** (diagnostics and tooling, no game change):
- `KF3_GEOPROBE=1` (`patches/GeometryProbe.cs`): hooks each of stage 15's 22
  calls, walks the ordering table and the 8-entry front table before and after,
  and reports the packets each call added by GPU command, size, slot and address;
  `KF3_GEOPROBE_FUNCS=` adds any function, per call site. Stage 15's call table in
  Verdite3's doc is its reading, not a guess.
- `tools/verdite-core/scripts/match_code.py`, the structural matcher: the nearest
  counterparts of a function in *another game's* executable (opcode classes, GTE
  commands, field offsets loaded and stored through a pointer, small constants,
  size, calls; each scored on its own), `tree` (two routines' calls aligned in
  order, recursively), `pairs` (score and rank known pairs), `show`. It reads each
  game's `config/verdite.json`, so it is game-agnostic; a call to a named libgte
  routine counts as the GTE commands it runs, because Verdite3 inlines what
  Verdite2 calls. Its weak spot is small functions (a 30-instruction routine
  ranks its counterpart 111th-237th of 1155 on its own); `tree` is what finds
  those, by their place among their siblings. It found every pairing below that
  says "ranked first" or gives a score.

**The headline**: **Verdite3's engine is Verdite2's**, a year on. Stage 15 is
stage 13 in the same order (one call inserted, two more full-screen quads); the
map has the same 80x80 10-byte-tile format; the object walk has the same four
tables, the same liveness tests and the same record field offsets (two strides
differ); the MO pose blender's decoders match instruction for instruction or
nearly; and **the bulk map assembler and the lit model assembler write the same
packets Verdite2's `FillTriangle`/`FillQuad` write, offset for offset, from the
same face-record fields, with the same GTE operations, and link them at the same
`otz + 0xF0` slot.** What changed is mechanical and structural: the GTE is
inlined (`RTPS`, `NCLIP`, `NCCS`, `DPCS`, `NCDS`, `NCDT` as instructions) where
Verdite2 calls libgte; parameters pass through a block in the scratchpad
(`0x1F800000`) instead of arguments and the stack; the fog weight is computed by
the CPU from otz instead of read from `IR0`; and **there is no clipper**:
Verdite3 subdivides near map tiles and near models with libgte's polygon division
routines (library code that writes its own packets, `POLY_FT3`/`FT4` with one
colour a face), where Verdite2 clips (`Clip3FTP`/`Clip4FTP`) and subdivides with
its own routine before its own assembler.

**Routine by routine** (Verdite2 → Verdite3; scores from `match_code.py`):

| Verdite2 | Verdite3 | verdict | evidence |
|---|---|---|---|
| `func_800342D8` stage 13 | `func_800422B8` stage 15 | same shape, different data | 22 calls against 19; `tree` pairs 15 in order, two of them instruction for instruction, seven more at 0.79-0.91 and the rest at 0.46-0.79; inline HUD block in the same place (after the angle difference); probe: only #9 (HUD, 16 packets), #10 (overlays, 30), #12 (map, 60-358), #13 (models, 243-370) add packets, #20 splices the front table |
| `func_8002E22C` camera block | `func_800357E8` | same shape | same reads of `a0`/`a1`, same `>> 11` tile, 0.70; Verdite3 also precomposes four view-times-rotation matrices for the map's halves |
| `func_80031C94` 24x24 sweep | `func_8003BFD0` | same shape, different data | 25x25, grid in the scratchpad (`0x1F800120`), bit `0x02` gates both halves where Verdite2 has bits 0/1; same map format and bounds (0x50), same tile placement |
| `func_80031B1C` a cell's halves | inline in `func_8003BFD0` | merged | (`func_8003BE34`, 0.71 by shape, is a second caller of the half routine, not on this path) |
| `func_80031950` a half | `func_8003BB04` | same job, different data | light record by index (`0x6C` bytes against `0x68`, same layout to `+0x50`), LLM by rotation, LCM, BK, depth cue; same far-model gate; picks the assembler on grid bit `0x04` and the buffer's room (10 KB) |
| `func_8002E650` / `func_8002E7CC` vertex transforms | inline `RTPS` loops in each assembler and twice in the submitter | different | same 8-byte cache entry (screen word, otz `SZ3 >> 2`, fog weight); the weight is `((otz - near/4) << 14) / (far - near)`, clamped `0..0x1F0F`, not `IR0` on three curves (the same 32000 cut-off) |
| `func_8002FECC` far unclipped assembler | `func_80039D50` (the map's bulk, 147 calls, ~285 packets a frame) | **same packets**, different front end | identical `POLY_GT3`/`GT4` fill and slot (`otz + 0xF0`); a near reject (all corners' otz < 100) Verdite2 does not have; out-of-range otz clamped to `0x1F0F` where Verdite2 drops |
| `func_80030540` clipped assembler | `func_8003AB04` (near map, 8 calls, ~72 packets) | **different mechanism** | no clipper; libgte division (`func_80074D88`/`func_80075188`/`func_800756A8`/`func_80075B48`, `RTPT` inside) writes flat-coloured `POLY_FT3`/`FT4` |
| `Clip4FTP` / `Clip3FTP` | none | absent | not linked; nothing in `GAME.EXE` clips to the near plane |
| `func_8002F214` lit assembler | `func_80035CA4` | **same packets**, inline GTE | ranked first, 0.65: GTE identical (`NCLIP`/`NCDS`/`NCDT`), field sets 0.92 alike; also draws the HUD's models (#9) |
| `func_8002EAEC` forced blend | `func_80037BEC` | same shape | forces the blend bit and writes a blend rate into the page from the submit flag |
| (none) | `func_80038844`, `func_80039428` | variants | the lit assembler into the front table; the sky's, lit `NCCT`/`NCCS` without depth cue |
| `func_8002E910` HUD transform | inline orthographic `MVMVA` loops (`func_8003C35C`, the submitter's `fp` = 0 path) | same job, inline | same cache entry, fixed depth |
| `func_800331B4` object walk | `func_80040AE4` | same shape, different data | ranked first; creatures 200 x `0x88` (`0x7C`), objects 396 x `0x44` (same), effects 128 x `0x4C` (`0x48`), billboards 128 x `0x18` (same); the same liveness tests and record fields; helpers 0.92-1.00; extra object kinds `0xF2`/`0xE5`/`0xE9`; page bitmaps in the scratchpad |
| `func_80032588` submitter | `func_8003E34C` (and `func_8003F304` into the front table) | same job, different shape | ranked first once libgte calls count as GTE; matrices by inline `MVMVA`; three assembler paths on a flag (near/blend/lit) where Verdite2 has an assembler byte |
| `func_80032400` arm | `func_8003DF50` | same job | same position in the stage; lit from the player's tile's record; returns while `0x801B25A4` is -1 (so not seen drawing in this save) |
| `func_80032AC4` sky (kind `0xF0`) | `func_800400AC` | same job, same arguments | the same seven arguments from the same record fields; draws 45 packets into the front table, which the swap puts behind everything |
| `func_80034DA8` MO blender | `func_800431E8` | **same routine** | 0.77, identical field offsets; decoders 0.95, 0.98, 1.00, 0.98; three small Verdite2 helpers not called |

**Recommendation per routine.**
- **Port Verdite2's C# with a data table, then share**: the MO pose blender
  (`MoPose`), and inside the assemblers **the packet fill** (`FillTriangle`,
  `FillQuad`, `Place`, the allocator, and the depth/lighting records `0050`
  added). The fill is where 0050's per-packet depth, fractional corners and
  lighting are recorded, so sharing it is what makes Z-buffer, sub-pixel and
  per-pixel lighting one implementation in both games.
- **Rewrite for Verdite3 with Verdite2's as the template**: the face loops of
  `func_80039D50` and `func_80035CA4` (and its three variants as parameters of one
  loop), the vertex passes, `func_8003BB04`, the two walks, the submitter, stage
  15, the camera block. Same algorithms; the front ends differ (scratchpad block,
  inline GTE, fog formula, near reject, clamp versus drop).
- **New, no template**: the near path, `func_8003AB04` and `func_800366A8` with
  libgte's four division routines. Leaving them recompiled leaves the near
  geometry (where a Z-buffer matters most) on the weak address-matching path;
  rewriting them is the price of owning the near map.
- **Leave recompiled**: the overlays (`SPRT`, #10), the full-screen quads, the
  fade and texture steppers.

**Proposed build order** (yours, adjusted where the evidence says so):
1. `func_80039D50` and `func_80035CA4` in C# with a verify mode (RAM, registers,
   GTE, **and the scratchpad**, which every one of these routines reads and
   writes). Together they write about three quarters of the frame's packets here. Build them
   on a copy of Verdite2's fill, kept textually close so step 4 is a diff.
2. The near path: `func_8003AB04`, `func_800366A8` and the four division
   routines, verified the same way. Without it the near map has no depth record.
3. Z-buffer and sub-pixel on top (0050's records from the C# fill).
4. **Then compare the two games' fills** and extract the shared one (the plan's
   step 4), with Verdite2's acceptance test and `KF2_POLYASM=verify` as the proof
   that Verdite2 did not move.
5. The walks and stage 15 in C#. **One correction**: the billboard clock at
   `0x80182964` is bumped by the model walk `func_80040AE4`, not by stage 15's own
   body, exactly as Verdite2's `0x80195170` is by `func_800331B4`; Verdite2 fixed
   that with `SpriteAnim` (a hold keyed on the frame), which ports without stage 15
   in C#. The other render-rate words are not identified yet.

**A proposal for shared C#** (nothing built; Verdite Core holds only Python).
- **Where**: `tools/verdite-core/cs/`, namespace `Verdite.Core`, as **source
  files compiled into each game's own assembly**, not a library of its own. That
  keeps one assembly per game (the launcher, mods and `AutoStart`'s reflection
  all assume one), and lets core code bind recompiled functions by address the
  way patches do.
- **The csprojs**: both already `Remove` `tools/**` from the default globs (the
  CS0579 trap), so each adds one explicit `<Compile Include="tools/verdite-core/cs/**/*.cs" />`
  after the removes; that is not the NETSDK1022 case, which is a duplicate of a
  default glob.
- **Verdite2's launcher**: stage the same files as payload,
  `<Content Include="../tools/verdite-core/cs/**/*.cs" LinkBase="content/src/verdite-core" />`
  beside `content/src/patches`. `Sources.All()` already takes every `*.cs` under
  `content/src` recursively, so `GameCompile` compiles them in its one Roslyn
  pass and `BuildKey` hashes them with no code change; a core update therefore
  rebuilds the game at the next start, as a patch change does. The launcher's own
  `<Compile Remove="content/**" />` keeps them out of the launcher. The CI check
  that the launcher compiles neither `generated/` nor `patches/` gains the core
  path as a third assertion, because core code is game-side code (it binds
  recompiled functions) and would not link into the launcher anyway.
- **Mods keep `Kf2.*`**: `[TypeForwardedTo]` only redirects across assemblies,
  and here the core compiles into the same one, so forwarding is not the tool.
  A mod-visible type that moves keeps a thin `Kf2.` class delegating to the core
  (static members forwarded one by one; the public surface listed in "What mods
  can see" above stays as it is). Types no mod can see move without a wrapper.
- **Game data**: a per-game C# class in the game's `patches/` that fills the
  core's layout records (addresses, strides, field offsets, sentinels) and its
  prefixes (`KF2`/`KF3` for env vars and log lines), set once in `Program.cs`
  before any `Install()`. C# rather than JSON: the values are read in hot paths,
  are checked at compile time, and sit beside the patch that documents them.
  `config/verdite.json` stays the Python tools' input.
- **The first candidates**, low risk because they touch no picture: the four
  identical harness pieces from the last unit, `HookAttach` (only the log prefix
  differs), the command channel's transport (line cap, queue, vblank drain,
  `PAD_dr` injection, JSON quoting; each game registers its verbs), the beacon's
  emitter and the autopad parser. `Kf2.AgentServer` is mod-visible, so it keeps a
  wrapper. Verdite2's proof: its scripted acceptance run and the `[KF2]` lines of
  `KF2_AGENT`/`KF2_SHELL` unchanged before and after.

**Measured**: stage 15's per-call packets (Verdite3 slot 1, `fdat02`, 15.0
frames/s, four 5-second windows), the assembler per call site, the front table.
**Nothing to judge by eye this unit**: no picture changed.

**Open.**
- `func_80041D9C` (stage 15 #11), `func_8003D280` and `func_8003D79C` (the extra
  quads) have no counterpart and drew nothing here.
- The submit flag's source (which record field picks the near, blended or lit
  path in `func_8003E34C`) is not traced, nor what `func_8003F304` draws.
- Only one area was measured (the only save on the card); the near path's share
  will differ elsewhere.
- Verdite Core's `match_code.py` is a local commit in Verdite3's subtree; it goes
  to `Voicedrew11/verdite-core` with `--push-core` when you say so.

#### Handoff: the next unit

**State.** Verdite3 `main` is three commits ahead of `origin/main` (`498dbc9`
Verdite Core's `match_code.py`, a subtree-only commit; `7d65a03` the probe and
"The geometry path"; `0933ee2` entry registers, the scratchpad, the probe's
artifacts), none pushed. Verdite2 `v0.4.0-staging` carries this entry. Nothing
was built that changes a picture in either game. The survey is complete for what
the build order's first steps need; the open items above do not block them.

**The next piece of work (recommended; the plan says the user picks, so ask
first): Verdite3's two bulk assemblers in C#, with a verify mode.**
`func_80039D50` (the map's bulk) and `func_80035CA4` (the lit models and the
HUD's), as replace hooks in a new `patches/PolyAssembler.cs` in Verdite3, under
`KF3_POLYASM=0|1|verify`, **off by default** until verify reads zero mismatches
over a session. Done when: verify reports 0 RAM, 0 scratchpad, 0 register and
0 GTE mismatches across standing, turning and walking in `fdat02` (and a second
area if one can be reached); `KF3_GEOPROBE`'s per-call packet counts are the same
on and off; `KF3_FPS=144 KF3_FPS_PROBE=1` still reads 144.0 fps at 15.0 ticks/s.
No picture feature in the same unit: Z-buffer and sub-pixel come after.

The plan, with what the unit buys and does not, is Verdite3's
`docs/GEOMETRY.md`. How, in order:
1. Read Verdite2's `patches/PolyAssembler.cs` (the `Frame`, `Allocate`, `Bump`,
   `FillTriangle`, `FillQuad`, `Place`, `Visible` and the verify harness near
   `c.Snapshot()`) and `PolyAssemblerLit.cs`, and "The polygon assembler in C#"
   and "The lit model assembler" in `docs/PATCHES_AND_MODS.md`. Copy the fill
   **textually close** to Verdite2's, because the unit after next diffs the two to
   extract the shared one. Leave out what Verdite3 has no use for yet (the depth
   and lighting records, `RenderDistance`, `Remaster`, `GteVertexMap` hoisting).
2. The front ends from "How the assemblers are entered" and "The map" in
   Verdite3's `docs/GAME_INTERNALS.md`: parameters from the scratchpad (through
   `ReadU32`/`WriteU32`, never a direct `Ram` reference: the scratchpad is a
   separate array), the vertex pass with Verdite3's fog weight, the near reject,
   the clamp to `0x1F0F` (map) against the drop (models), the inline `addPrim`,
   the counters at `+0x68..+0x70`. GTE ops as `Gte` calls in the order the MIPS
   issues them, so the GTE state after the routine matches.
3. Verify as Verdite2's does, plus **the scratchpad's 1 KB** in each snapshot
   (`PSMemory`'s array is private: read it through the accessors, or add a fork
   accessor in its own commit). The disassembly is the spec: `match_code.py show`
   for the shape, and the instruction listings are quickly made with capstone
   (Python, installed); a GTE command word is COP2 with bit 25 set, which
   capstone does not decode.
4. Write it up in Verdite3's `docs/GAME_INTERNALS.md` or a new patches document,
   with the verify counts, and add `KF3_POLYASM` to `docs/ENV_VARS.md`.

**The alternative unit**, if you would rather prove the plumbing first: move the
four identical harness pieces into `tools/verdite-core/cs/` per the proposal
above. It is low risk, but it changes Verdite2's csproj and launcher payload and
`Kf2.AgentServer` is mod-visible, so it is yours to approve, and Verdite2's
acceptance test must read the same before and after.

**Don't**: push anything without asking; let an opencode agent share a checkout
being edited (its wrapper ran `git stash -u` in Verdite3 this session; opencode's
sandbox also could not read Verdite2 from a Verdite3 worktree, so cross-repo
tasks need their inputs copied in); hook the libgte division routines expecting
named symbols (they have none: `func_80074D88`, `func_80075188`,
`func_800756A8`, `func_80075B48`).

### 2026-10-02: Phase 3 in Verdite3: the bulk assemblers in C#

**The unit you picked**: Verdite3's two bulk polygon assemblers in C# with a
verify mode, as planned in Verdite3's `docs/GEOMETRY.md`. Done in Verdite3
(`~/Desktop/verdite3`, `main`, local commit `52ecf93`, not pushed). The numbers
are its `docs/GEOMETRY.md`, "The first unit: the bulk assemblers"; this entry is
the summary and what it means for the shared fill. Nothing in Verdite2 changed
but this file.

**Built**: `func_80039D50` (the map's bulk) and `func_80035CA4` (the lit models
and the HUD's) as replace hooks in `patches/PolyAssembler.cs`,
`PolyAssemblerFill.cs` and `PolyAssemblerLit.cs`, under `KF3_POLYASM` (on by
default now that verify read clean; `0` to compare, `verify`), `KF3_POLYASM_MAP=0`
and `KF3_POLYASM_LIT=0`. Verify is Verdite2's harness plus the scratchpad's 1 KB
(256 `ReadU32`s; no fork change) and `LO`/`HI`.

**Measured** (slot 1, `fdat02`):
- Verify: **0 mismatches** in RAM, scratchpad, registers and GTE over 153,278 map
  and 13,337 lit calls, standing, turning, walking, the menu opened and closed.
  The map's `0x34` kind (never met in `fdat02`) and the buffer-exhaustion return
  (never reached) were forced under verify by temporary edits: 0 mismatches.
- `KF3_GEOPROBE=1`: all 206 per-call lines identical with the C# on and off.
- `KF3_FPS=144 KF3_FPS_PROBE=1`: 144.0 fps at 15.0 ticks/s.
- Uncapped: **793 → 1,171 fps** where the map's bulk runs (147 calls a frame;
  1.26 → 0.85 ms), 1,235 → 1,324 at the start position. The plan expected
  10-20%: Verdite3's recompiled routines reach the scratchpad through `PSMemory`
  on almost every instruction, so locals buy more than in Verdite2.

**For the shared fill (the plan's step 4), the differences between the two
games' fills as they now stand**, each a parameter or a hook point:
- Verdite3 computes and clamps the otz **before** allocating; Verdite2's
  `FillTriangle`/`FillQuad` return the otz sum after filling. Verdite3's fills
  return nothing.
- Verdite3's `0x24` triangle runs `NCCS` twice on the same normal.
- The light colour: Verdite2 reads `LightColour` (`0x8006E604`); Verdite3 a
  scratchpad word (`+0x54` map, `+0x64` models), kept in `Frame.Colour`.
- The link: Verdite2's `Link` floors the slot at 16 and masks it; Verdite3's
  takes the clamped otz and drops a negative one. `Place` and `AddPrim` are the
  same text (Verdite3's `Place` without `RenderDistance`).
- The allocator: Verdite2 bumps the descriptor in RAM; Verdite3 the scratchpad's
  cursor (`Frame.Cursor`), and counts packets and links in the scratchpad.
- The models: Verdite3 adds the scratchpad's CLUT offset (`+0x84`) to every
  packet's `+0xE`, and takes the cull through `Visible` (corners read p0, p1, p2)
  where Verdite2's lit assembler uses `Facing` (p0, p2, p1).
- The map's third kind (`0x34`, an `NCDS` per corner on three normals) has no
  Verdite2 counterpart: `FillGouraudTriangle` is Verdite3's alone.
- Verdite3's `Frame` carries the scratchpad's working state and has no
  `Hoisted`, lighting or depth fields yet; those come with the records.

**A correction to the plan**: the three variants are not "the lit loop with other
parameters". `func_80037BEC` nearly is (blend rate into the page, forced code,
spills to `+0x2C..+0x3C`); `func_80038844` always shifts the fog weight by
`3 - a2` and links into the front table; `func_80039428`, the sky's, has two kinds
of its own and lights without the depth cue. Only the sky's runs in `fdat02`, so
all three stay recompiled. Read with an opencode agent (DeepSeek, read-only, on
copies of the routines) and checked by hand.

**Nothing to judge by eye**: the packets are identical by verify and by the
probe.

#### Handoff: the next unit

**State.** Verdite3 `main` is five commits ahead of `origin/main` (the four
before, plus `52ecf93`), none pushed; Verdite Core's `match_code.py` is still a
local subtree commit there. Verdite2 `v0.4.0-staging` carries this entry.

**Superseded again, 2026-10-02: smoothing is done and judged** (Verdite3's
`docs/SMOOTHING.md`, units 1-3, on under pacing, with a Testing tab in Settings).
**The next work is Verdite3's `docs/PICTURE.md`**: 24-bit colour and no dither,
perspective and sub-pixel on the shared fork's address map, then the Z-buffer from
the C# assemblers' packet records (`0050`), then the near path below, recorded.

**Superseded the same day: the user put smoothing first.** The next work is
stage 15 and the camera block in C# with a view override, then the camera carried
between ticks; planned, with its handoff, in Verdite3's `docs/SMOOTHING.md`. The
near path below is deferred behind it, and `SHARING.md` stays here (Verdite3's
`NOTES.md`, "Sharing with Verdite2", says why).

**The next piece of work on the geometry path: the near path** in C#,
`func_8003AB04` (the near map, about 72 packets a frame here) and
`func_800366A8` (the near models), with libgte's four division routines
(`func_80074D88`, `func_80075188`, `func_800756A8`, `func_80075B48`), verified
the same way. New code with no Verdite2 template; without it the near map, where
interpenetration is closest to the eye, has no depth record. **The alternative**
is the Z-buffer over what is C# now (records `0050` from the fills, about three
quarters of the frame), which can be tried before the near path but not finished
without it.

**Don't**: push anything without asking; let an opencode agent share a checkout
being edited (inputs for a read-only agent go into a scratch directory of their
own); drive menus without a copy of `carda.sav` (Cross saves over slot 1).

### 2026-10-02: Verdite3's keyboard and mouse, with the mouse lead

**Done in Verdite3** (`main`, local commit `afeb187`, not pushed): Verdite2's
`KeyLayout`, `Mouse`, `MouseIndicator` and `MouseLead` ported, the look hook
written for that game's routine. Written up in Verdite3's `docs/INPUT.md`.

- **The look routine is the same three branches.** Verdite3's `func_8002F5C0` is
  `func_80028DB8`'s shape on both axes, so `Analog.Drive`'s pre-load carries over
  unchanged; the one difference is that **L2 and R2 together recentre the pitch**
  there, so a mouse pitch masks both buttons. The pitch limit is `0x2BC`/`0xD44`
  at 12 bits in both games. The mouse drive is a replace hook of its own
  (`MouseLook`), not inside a ported `Analog`: twin-stick analog was not ported.
- **The lead fits Verdite3's view override better than Verdite2's stage-8
  pair**: it is a few lines in `ViewSmoothing.OnHanded` adding to the
  interpolated `Camera`, nothing written into the game's state. Measured with a
  synthetic hand at 144 fps: the view starts 6.9 ms after the hand against
  52.4 ms off, stops 4.0 ms after against 95.2 ms, and tracks it to 1-2 units.
- **Shared candidates for Verdite Core's first C#**: `KeyLayout`'s mechanism
  (`Configure` before the load, the once-only migration, `Superseded`),
  `Mouse`'s capture, poll and stale handling, and `MouseIndicator` are
  game-agnostic as ported; only the constants block, the layout table and the
  look hook are each game's.
- **Not ported**: the menu pointer (`MenuMouse`, 1.4k lines of this game's menu
  internals; Verdite3's menus read the pad through their own `PadRead` calls,
  per its survey) and `Analog`.

### 2026-10-02: Plan: Verdite Core's first C#, the five near-identical patches

**Not started.** A plan for the next agent. Verdite Core is Python scripts only;
this moves the first C# into it: the five patches whose Verdite2 and Verdite3
copies are nearly the same file. Measured by a line match of each pair, comments
dropped and the `KF2`/`KF3` prefixes normalised (`difflib`, 2026-10-02):

| file | match | what differs |
|---|---|---|
| `HookAttach.cs` | 100% | code identical; V2's doc comment is long, V3's points at V2's docs; the log prefix |
| `MouseIndicator.cs` | 97% | the picture rectangle: V2 `MapRender.Picture` (its map viewports), V3 the fork's `OutputView`; the panel id |
| `KeyLayout.cs` | 85% | the layout table, `Version`, `Superseded`; where "applied" is kept: V2 `Settings.PatchSettings.Get/Set`, V3 `Rt.View.GetInt/SetInt` + `Rt.SaveView()` |
| `Mouse.cs` | 82% | the constants block (V3 names its yaw/pitch addresses and default buttons there); the settings helpers (V2 borrows `Analog.Env/Saved`, V3 has its own copies); the text-input gate (V2 `HotkeyGate.Editing`, V3 ImGui's `WantTextInput`) |
| `Differential.cs` | 80% | V3's is a superset: it also compares the scratchpad and LO/HI, and takes an `extra` summary callback |

Everything else in either game's `patches/` matches below 65% or is the game's
own code (the next candidates, in order: the pacing core with `LoopPacing`, `VBlankPacing` and
the once-a-tick holds; the agent harness; the smoothing math).

#### Decisions already made

- **Source, not a DLL.** Core C# lives in `src/` of `Voicedrew11/verdite-core`
  (so `tools/verdite-core/src/` in each game) and is compiled **into each game's
  own assembly**, like `patches/`. No project reference, no second assembly, so
  `HookManager` detours, `AutoStart`'s reflection and the launcher's one Roslyn
  pass (`GameCompile`) see it as they see `patches/`.
- **Namespace `Verdite.Core`**, imported everywhere by a global using, so the
  callers do not change: `<Using Include="Verdite.Core" />` in each game's csproj,
  and in Verdite2 the same line in `GameCompile.GlobalUsings`, **which must stay in
  step with the csproj** (a difference exists only in the release).
- **The game's identity is set once**, first thing in `Program.cs`:
  `Verdite.Core.Game.Configure(tag: "KF2")` (`"KF3"`), giving the log prefix
  (`[KF2]`), the env prefix (`KF2_`) and the lowercase id for panel names
  (`kf2mouseind`). A new 10-line `src/Game.cs`. Nothing else in core may know a
  game: no address, no overlay name, no `KingsField` (Verdite Core's README rule).
- **The game's values are passed in, not read from a file.** A `config/verdite.json`
  is for the Python scripts; the C# takes small records from the game's own
  patch at install time.
- **Author in Verdite3, adopt in Verdite2.** Verdite3 (`main`) has fewer callers
  (about 30 files against 51) and no launcher, so the core version is written
  in its `tools/verdite-core`, pushed with `--push-core`, and pulled into
  Verdite2 (`dev`) with `--pull-core`. Each subtree commit touches only
  `tools/verdite-core/` (`--push-core` refuses a mixed one); each game's adoption
  is a separate commit.

#### Step 0: the core is out of step already

Verdite3's `tools/verdite-core` carries a commit Verdite Core does not have:
`498dbc9`, `scripts/match_code.py` (Verdite Core's `main` is `a6c2434`; Verdite2
is pinned at `536167a`). **Ask the user, then `bash scripts/setup_tools.sh
--push-core` from Verdite3** before adding anything, so the new work is not
stacked on an unpushed commit.

#### Step 1: the build wiring, with `HookAttach`

The wiring and the simplest file land together, so the wiring is proved by a file
that exercises it everywhere.

- Core: `src/Game.cs`; `src/HookAttach.cs`, V2's code, with V3's short doc
  comment pointing at "A registration is not a hook" in Verdite2's
  `docs/PATCHES_AND_MODS.md`, and `[{Game.Tag}]` in place of the literal prefix.
  README: a "C#" table beside the scripts, and the rule that it compiles into the
  game.
- Each game: `<Compile Include="tools/verdite-core/src/**/*.cs" />` **after** the
  `<Compile Remove="tools/**" />` (the remove stays: it keeps RecompOne's own
  sources out), the global using, `Game.Configure` in `Program.cs`, and its own
  `patches/HookAttach.cs` deleted.
- **Verdite2's launcher**: `<Content Include="../tools/verdite-core/src/**/*.cs"
  LinkBase="content/src/verdite-core" .../>` in `Verdite2.Launcher.csproj`, beside
  the `patches/**` line. `Sources.All()` walks `content/src` recursively, so this
  is what makes `GameCompile` compile it **and `BuildKey` hash it**: miss it and
  the release fails to compile, or a core change never triggers a rebuild. Check
  that `.github/workflows/ci.yml`'s payload assertion still holds (the launcher
  compiles neither `generated/` nor `patches/`; it must not compile core either).
- **Done when**: both games build; the startup log's attach lines are the same set
  before and after, prefix included (diff a boot's `[KF2]`/`[KF3]` lines); Verdite2's
  acceptance test (`open → game → fdat02 → fdat05`, 144.0 fps at 20.0 ticks/s,
  every hook attached, with `KF2_PRESENT_PROBE=1`); Verdite3's `KF3_FPS=144
  KF3_FPS_PROBE=1` at 144.0 fps and 15.0 ticks/s; and the launcher's first-run
  compile of a clean data folder succeeds (`packaging/linux/build-appimage.sh`,
  then run it once without a built game).

#### Step 2: `Differential`

- Core: V3's file. **Adopting it changes what Verdite2's verify modes compare**
  (the scratchpad and LO/HI were never compared there). Its Verdite2 users are
  `CameraBlock` and `MoPose` (an instance each) and `Stage13`'s replay (the RAM
  and register helpers), so run `KF2_CAMERABLOCK=verify`, `KF2_MOPOSE=verify` and
  `KF2_STAGE13=verify`. (`TileWalk`, `ModelWalk` and `PolyAssembler` carry copies
  of the same shape of their own; folding them in is a later unit, not this one.)
  A new mismatch is a finding
  about Verdite2's C#, not a reason to drop the check: write it down and ask. If
  the user wants it parked, a `comparePad: false` constructor argument is the
  stopgap, defaulting to true.
- Verdite3: `KF3_POLYASM=verify`, `KF3_NEARPATH=verify`, `KF3_MODELWALK=verify`,
  `KF3_MOPOSE=verify`, `KF3_STAGE15=verify`, `KF3_CAMERABLOCK=verify` still read 0.
- Verdite3's users: `CameraBlock`, `MoPose`, `ModelWalk`, `NearPath`,
  `PolyAssembler`, `PolyAssemblerLit` and `Stage15`.
- V2's doc comment names `TileWalk`, `ModelWalk`, `PolyAssembler` and `Stage13` by
  `cref`; core must not, so they become plain words ("the walks and assemblers").

#### Step 3: `MouseIndicator`

- Core: the file with `public static Func<(Vector2 Min, Vector2 Max)?> Picture`,
  defaulting to `OutputView` (Verdite3's), and the panel id from `Game.Id`.
  Verdite2 sets `Picture` to `MapRender.Picture` where it installs `Mouse`.
- **`mods/kf2debug/Noclip.cs` names `Kf2.MouseIndicator.Suppressed`** (lines 160
  and 576). A mod is compiled at run time against the game assembly, so a missed
  rename shows only when the mod is enabled in the Mods panel: change both to
  `Verdite.Core.MouseIndicator` and enable the mod once.
- **Done when**: both build, and the user has seen the glyph on Escape in both
  games (by eye; nothing measures it).

#### Step 4: `KeyLayout`

- Core: the mechanism as `Verdite.Core.KeyLayoutApply`: apply before the settings
  load, once per `Version`, leave a customised layout alone, correct a superseded
  one. It takes the layout, `Version`, `Superseded`, the applied key and two
  delegates, `Func<int> getApplied` and `Action<int> setApplied`. Each game keeps
  a short `patches/KeyLayout.cs` holding its table and its storage: Verdite2's
  `PatchSettings`, Verdite3's `Rt.View` with `Rt.SaveView()`.
- **The applied key's name and store must not change in either game**, or every
  existing `settings.json` reads as never applied (the layout is rewritten over
  the player's bindings) or as customised (never corrected): the rule about
  `KeyLayout.Version` in Verdite2's `AGENTS.md`. **Done when**, in each game: a
  copy of a real `settings.json` is unchanged after a boot (diff it), and a boot
  with no `settings.json` writes the game's layout and the current `Version`.

#### Step 5: `Mouse`, the one with real seams

- Core keeps the class name `Mouse` and its whole public API (`Enabled`, `Lead`,
  `Captured`, `SpentThisFrame`, `TakeLook`, `Poll`, the keys), so
  `ViewSmoothing`/`FrameSmoothing`, `MouseLook`/`Analog` and the settings pages
  compile unchanged through the global using. The game hands it one record at
  install: units per degree, degrees per pixel, the step cap, the pitch limit,
  the default left, right and middle buttons, and `Func<bool> TextEditing`
  (Verdite2 passes `HotkeyGate.Editing`, which also covers the remaster editor;
  Verdite3 passes ImGui's `WantTextInput`).
- The settings helpers move into core as `Verdite.Core.Kept.Env/Saved` (the env
  var wins over the saved key), and Verdite2's `Analog` calls those in place of
  its own copies, which removes `Mouse`'s only dependency on `Analog`.
- **What stays in each game**: the look hook that spends the motion (Verdite2's
  `Analog.BeforeLook`, Verdite3's `MouseLook` with its yaw and pitch addresses)
  and the settings page.
- **Done when**: both build; the mouse lead is measured as in "Verdite3's keyboard
  and mouse" above (a synthetic hand at 144 fps; the view starts within about a
  frame of the hand and tracks it to 1-2 units) in both games; and the user has
  turned and looked with the mouse in both (feel is by eye).

#### Rules for whoever does it

- **Ask before every push**: `--push-core`, and each game's branch (Verdite2 on
  `dev`, Verdite3 on `main`).
- One unit at a time, in the order above, each through both games before the
  next: a core commit, pushed, pulled into Verdite2, adopted in both.
- If opencode does the edits: one worktree per game, never the checkout being
  edited; gitignored inputs copied in (`generated/` symlinked, `disc/`, the save
  cards and `settings.json` copied); only the orchestrator runs a game, one at a
  time (one shell port per game: 27900, 27903).
- Findings go in the docs that own them: this log for the program, each game's
  `docs/` for what it changed in that game, Verdite Core's README for what core
  now holds.

### 2026-10-02: Verdite Core's first C#, step by step

Doing the plan above, in its order.

- **Step 0**: Verdite3's `498dbc9` (`match_code.py`) pushed to Verdite Core
  (`165d748`).
- **Step 1, the wiring and `HookAttach`**: Verdite Core `a238227` adds `src/Game.cs`
  and `src/HookAttach.cs`; both games compile `tools/verdite-core/src/**/*.cs`
  with `Verdite.Core` as a global using and set their tag first in `Program.cs`.
  Verdite2's launcher ships core under `content/src/verdite-core/`, and
  `GameCompile.GlobalUsings` names the namespace. Measured: each game's boot prints
  the same set of `[KF2]`/`[KF3]` lines as before (numbers and paths normalised);
  Verdite2's acceptance run (`open → game → fdat02 → fdat05`, hp 46/86 in area 1,
  144.0 fps at 20.0 ticks/s, `[present] wide 288`); Verdite3 at 144.0 fps and 15.0
  ticks/s; and an AppImage built from the change, run on an empty data folder,
  recompiled, compiled the game with core in its one Roslyn pass, and reached
  `fdat05` with the same lines. A comparison needs the main checkout's
  `interface.ini` and `settings.json` beside the build: without them a worktree
  boots with other enhancements and the lines differ for that reason alone, and a
  `settings.json` whose `CdPath` names another image stops a launcher run at the
  disc picker.
- **Step 2, `Differential`**: Verdite Core `19c86a3`, Verdite3's file (the
  scratchpad, LO/HI, the `extra` callback). Verdite3: every verify mode in the plan
  reads 0, as before. Verdite2: `KF2_CAMERABLOCK=verify` and `KF2_STAGE13=verify`
  read 0 and the scratchpad reads 0 everywhere; `KF2_MOPOSE=verify` (with
  `KF2_SMOOTH_ANIM=0`, which `MoPose`'s own write-up requires; without it the RAM
  differs before and after alike) now reports LO/HI mismatches on one model's blend,
  a finding about Verdite2's `MoPose` written up under "The blender in C#" in
  `docs/GPU_RENDERER.md`. The check stays on.
- **Step 3, `MouseIndicator`**: Verdite Core `7f62bc9`. `Picture` is a
  `Func<(Vector2 Min, Vector2 Max)?>`, `OutputView` by default (Verdite3); Verdite2
  sets it to `MapRender.Picture` in `Mouse.Install`, before registering the panel.
  The panel id is `{Game.Id}mouseind`, unchanged in both. `kf2debug`'s two
  `Kf2.MouseIndicator` names became `Verdite.Core.MouseIndicator`, and the mod
  builds and loads with it enabled. Both boots print the same lines as before. The
  glyph itself is for the user's eye (Escape, in both games).
- **Step 4, `KeyLayout`**: Verdite Core `cdb19ac`, `KeyLayoutApply`: the env switch
  (`{Tag}_KEYS`), the fresh-install default, the once-per-version migration, the
  arrows' second key in `PAD_dr`, `Apply`/`ApplyStock`/`IsApplied`, all of which
  were the same code in both games. Each game's `patches/KeyLayout.cs` keeps its
  table, `Version`, `Superseded`, the line announcing it and its store (Verdite2
  `PatchSettings`, Verdite3 `Rt.View` with `SaveView`), behind the same public
  API; the applied key (`kf2.keys.layout`, `kf3.keys.layout`) is unchanged.
  Measured in each game: a boot leaves a real `settings.json` and `interface.ini`
  byte-identical; a boot with neither writes the game's layout and its `Version`
  (2, 1); and an old layout with no marker is migrated with the same line
  (Verdite2's superseded version 1, Verdite3's stock keys).
- **Step 5, `Mouse`**: Verdite Core `0df259d`: `Mouse`, configured with one
  `MouseGame` record (units per degree, degrees per pixel, the step cap, the pitch
  limit, the base yaw and pitch addresses, the default buttons, `TextEditing`, the
  frame clock `Frames` and `LogicHz`, and an optional `Paused`, which only
  Verdite2's stale rule had), and `Kept`, the env-then-saved helpers. One real
  difference was kept per game: Verdite2 stores a saved bool as `True`/`False`,
  Verdite3 as 0/1, so `Kept.BoolsAsInts` is set by Verdite3. The settings keys are
  properties built from `Game.Id`, not fields, so no early type initialisation can
  fix them to another game's name. Verdite2's record is `Analog.MouseValues`,
  `Analog.Env/Saved` call `Kept`, and `MouseIndicator.Picture` moved to
  `Program.cs`; Verdite3's record is `MouseLook.Game`. Measured, with a synthetic
  hand (400 px/s in 400 ms bursts, fed through `Mouse.Poll`, the real mouse's
  motion discarded; a local hack, not committed) at 144 fps, 75 s a run, before
  and after: Verdite2 led 160.4 and 169.4 frames per 2 s window, 12.0 and 12.1
  units ahead, |applied − asked| 0.40 and 0.35; Verdite3 led 79.7 and 80.6 frames
  a second, |applied − asked| 0.00 in every window of both. Both boots print the
  same lines and leave `settings.json` and `interface.ini` unchanged; pacing held at
  144.0 fps, 20.0 and 15.0 ticks/s. How it feels to turn and look is for the
  user's hand, in both games.

### 2026-10-02: Plan: `core_check`, one command for "nothing regressed"

**Not started.** Moving five files into Verdite Core took about a dozen hand-run
commands per step and per game, the same ones each time: build, boot before, boot
after, diff the tagged lines, read the pacing line, check the overlays and the
beacon. This plan makes that one command, written once in Verdite Core and run in
either game. What it cannot replace is each move's own check (a verify mode, the
settings files, a synthetic input); it carries those as options so they are not
re-derived either.

#### What it is

`tools/verdite-core/scripts/core_check.py`, with a `scripts/core_check.py` wrapper
in each game like the other bring-up scripts. Python, reading the game through
`verdite_game.py`. **It knows no game**: everything game-specific is a new
`"check"` block in `config/verdite.json`.

```json
"check": {
  "tag": "KF2",
  "project": "Deception.csproj",
  "binary": "bin/Release/net10.0/Deception",
  "env": { "KF2_AUTOSTART": "2", "KF2_AGENT": "1", "KF2_FPS": "144",
           "KF2_FPS_PROBE": "1", "KF2_PRESENT_PROBE": "1" },
  "seconds": 70,
  "fps": 144.0, "ticks": 20.0,
  "overlays": ["open", "game", "fdat02", "fdat05"],
  "beacon": { "overlay": "fdat05", "hp": 46, "maxHp": 86, "area": 1 },
  "state": ["settings.json", "interface.ini", "carda.sav", "cardb.sav", "carda.fog"],
  "ignore": ["remaster: off, pack ", "icon: "],
  "package": { "script": "packaging/linux/build-appimage.sh",
               "image": "dist/Verdite2-*-x86_64.AppImage", "dataEnv": "VERDITE2_DATA" }
}
```

Verdite3's: tag `KF3`, `KingsField3Recomp.csproj`, `KF3_AUTOSTART=1`, 15.0
ticks, overlays `open, game, fdat02`, beacon `fdat02`, hp 50/50, area 0, no
`carda.fog`, no `package`.

#### What a run does

`core_check.py [--before REF] [--env K=V ...] [--verify] [--settings] [--fresh]
[--package] [--keep]`

1. **Two builds.** "After" is the working tree. "Before" is `REF` (default
   `HEAD`, or the last commit not touching the moved file with `--before auto`),
   checked out with `git worktree add --detach` under `scratch/corecheck/`, and
   removed afterwards unless `--keep`. Each worktree gets `generated/` symlinked,
   **each disc file symlinked into the tracked `disc/`** (a symlink of the
   directory lands inside it as `disc/disc`), and copies of `state` from the main
   checkout (without `interface.ini` and `settings.json`, a worktree boots with other
   enhancements and differs for that reason alone). It builds each with
   `dotnet build -c Release`.
2. **Two boots**, one at a time, never concurrently (one shell port, one
   window): `pkill -x` the binary's name (never `pkill -f`, which matches the
   calling shell), run under `timeout seconds` with `env` and any `--env`, log to
   `scratch/corecheck/{before,after}.log`, `pkill -x` again.
3. **Checks**, each PASS or FAIL on one line:
   - *lines*: the set of `[TAG]` lines, numbers replaced by `N`, `pacing:` and
     `ignore` lines dropped, is the same before and after; a difference prints the
     lines;
   - *pacing*: the last `pacing:` line of the after run is within 0.5 of `fps`
     drawn and 0.2 of `ticks`;
   - *overlays*: the `KF_AGENT` overlay sequence equals `overlays`;
   - *beacon*: the last beacon matches `beacon`;
   - *present*: a `[present]` line was printed (the black-window failure);
   - *faults*: no `Unhandled exception`, `could not hook`, `attach failed` or
     `giving up` in the after run that the before run did not have.
4. **Exit** 0 only if every check passed; the verdict block is at most about 15
   lines, so an agent can run it and read only that.

#### The options a move needs

- `--verify`: set every `*=verify` the game lists in a `"verify"` array in the
  config (Verdite2: `KF2_CAMERABLOCK`, `KF2_MOPOSE` with `KF2_SMOOTH_ANIM=0`,
  `KF2_STAGE13`; Verdite3: the six in the Differential step), and compare each
  routine's summed mismatch counts before and after, by kind (RAM, scratchpad,
  register, GTE). A count that rises is a FAIL with the first two samples
  printed; one already nonzero before is reported, not failed.
- `--settings`: hash every `state` file before and after each boot; any change is
  a FAIL (Step 4's check).
- `--fresh`: one more after-boot with no `settings.json` and no `interface.ini`,
  printing the keys it wrote that the config's `"fresh"` list names (Verdite2:
  `Keys`, `kf2.keys.layout`), for the reader to compare with what is expected.
- `--package` (only where `package` is set): run the script, point a
  `settings.json` copy's `CdPath` at the game's own disc (a stale path stops
  the launcher at its picker), run the image on an empty data folder under
  `dataEnv` long enough to build and boot, and apply the same *lines*, *pacing*,
  *overlays* and *beacon* checks to it. Slow (minutes); for a change to the
  wiring, not for every move.

#### Steps

1. The script with *lines*, *pacing*, *overlays*, *beacon*, *present*, *faults*,
   and Verdite2's `check` block. **Done when** `core_check.py --before HEAD` on a
   clean tree passes, and a deliberate regression fails it: `--env
   KF2_STAGE13=0` on the after run only (a hook line goes missing), and a
   `KF2_FPS=60` after run (pacing).
2. Verdite3's block. Done when the same two hold there.
3. `--verify`. Done when Verdite2 reproduces today's numbers: 0 for camerablock
   and stage13, and `mopose`'s LO/HI counts reported as pre-existing with
   `--before` at a commit after `Differential` moved.
4. `--settings` and `--fresh`. Done when they reproduce Step 4's results in both
   games.
5. `--package`. Done when it reproduces the Step 1 AppImage result.
6. Write it into Verdite Core's README, and point each game's `AGENTS.md`
   "Build and run" and `docs/DEVELOPMENT.md` at it; replace the hand-run steps in
   this log's next plan with a `core_check` line per unit.

#### Rules

- Each core commit touches only `tools/verdite-core/` and goes through
  `--push-core`/`--pull-core`; each game's block and wrapper is that game's
  commit.
- It never pushes, never commits, and never touches the main checkout's own
  files except to read the `state` it copies.
- It is a check for moves and wiring, not a replacement for a judgement by eye:
  it says so in its verdict when every check passed ("mechanism unchanged; nothing
  here looks at the picture").

### 2026-10-02: Verdite3's widescreen

**Done in Verdite3** (`main`, local commits `36adeb0`, `52f9047`, `e2c6711`, not
pushed), written up in its `docs/WIDESCREEN.md`. Three opencode agents (a survey,
the patch, a probe), then the cone from the survey; merged and measured by hand.

- **`Widescreen.cs` carried over almost unchanged**: the aspect, the latch clear
  on an executable load, the tint stretch by shape, the census. At 16:9 13-35% of
  an area's primitives reach the margin and the fade-in's tints were stretched,
  none in play. The HUD anchoring and the `DrawOTag` replacement were left behind
  (off in Verdite2; Verdite3's HUD is models and sprites anyway). A candidate for
  Verdite Core: only the overlay names, the tag and the store are the game's.
- **The cone is not Verdite2's.** Verdite3's `func_80034BF4` classifies every
  cell of a 25×25 grid by distance and two half-planes at yaw ± a half-angle of
  440/4096 (atan 160/200), so there is no table to scale and no dropped-row
  failure. The widening re-runs the classifier in C# at a wider angle with the
  stock radius and adds cells before the flood; the stock-angle C# matches the
  game's grid on every cell (after fixing two transcription bugs the agent made).
- **No clipper there**, so `ViewClip` has no counterpart; **the primitive buffer
  did not run out** (66% at 16:9 with the cone widened), so `PrimBuffer` was not
  needed in `fdat02`.

### 2026-10-03: Verdite3's near path had a screen test after all

**Done in Verdite3** (`main`, local commit `08df4c5`, not pushed), written up in
its `docs/WIDESCREEN.md`, "The near path's screen test". The user saw pop-in at
the bottom corners and on near walls at 16:9, untouched by a wider cone.

- **The "no clipper, nothing to port" entry above was wrong.** Verdite3 links no
  `Clip3FTP`/`Clip4FTP`, but its near faces go through libgte's polygon division,
  whose entry test drops a face wholly past `OFX ± pih/2`, and the near
  assemblers store `pih = 320` as an immediate per face. `NearScreen.cs` widens
  it at the four division entries (428 at 16:9, 560 at 21:9): 19% and 34% of the
  side rejects kept, verify clean, 4:3 unchanged. The counterpart of `ViewClip`
  is this, not nothing.
- **Verdite2's near-camera rescue does not carry over.** Verdite3's window is
  centred on the camera and its flood starts there; the wedge's apex sits about
  four cells behind it. A port of `RescueNearCamera` lit 33-35 flood-cleared
  cells a frame and changed nothing drawn, so it was dropped.
- Three opencode agents (the fix and probe, the cone, the doc); merged and
  measured by hand. Picture not judged.

### 2026-10-03: Verdite3's arm smoothing

**Done in Verdite3** (`main`, local commit `b9c89b2`, not pushed), written up in
its `docs/SMOOTHING.md`, "3d. The clip time carried". Verdite2's arm front-end in
`AnimSmoothing` (`func_80032400`, the swing clock `0x801994A4`) carried over as
one clip slot.

- **The same shape in both games.** Verdite3's arm, `func_8003DF50`, hands the MO
  blender a fixed slot (`0x801B259C`, the clock's address minus 8, as in
  Verdite2), model `0x20`, the clip byte `u8[0x801B25AE]` and the swing clock
  `s16[0x801B25A4]` as the clip time; its placement record does not move during a
  swing, so the clip is the whole motion. Square swings on the pad, not Triangle.
- **The "no arm in this save" reading was wrong**: the -1 is the idle clock.
- **Verdite2's idle latch was not needed.** Verdite3's carry already primes a slot
  that missed a tick, and the arm never reaches the blender while idle. The
  scope fence is `Enter`/`Leave` round stage 15's arm call in C#, not a pre/post
  pair on the arm and the blender.
- Measured at 144 fps: a swing drew 93 frames, 87 carried (the first tick
  primes), 0 re-seeks or backward steps over six swings. Picture not judged.
  Four opencode agents (the patch, two surveys, the doc); merged and measured by
  hand.

### 2026-10-03: Verdite3's twin-stick control and Input pane

**Done in Verdite3** (`main`, local commit `bf66ee1`, not pushed), written up in
its `docs/INPUT.md`, "Analog twin-stick control" and "The Input pane is the
port's". `Analog`, `AnalogProbe`, `InputSection`, `BindingTable`, `AnalogPage`,
`MousePage` and `KeyLayoutPage` ported; `MapButtonPage` has no counterpart.

- **The walk routine is Verdite2's shape with one asymmetry.** `func_8002F9BC`'s
  button branches add `max>>2` and clamp at `±max` on both axes, but with no
  button forward decays by `max>>3` and strafe by `max>>2`. Harmless: the stick's
  walk step is clamped to `max`, so it always takes a button branch.
- **Verdite3's base yaw is a quarter turn behind the heading**: forward moves along
  `yaw + 0x400`. `Analog` never does heading arithmetic, so it did not matter; a
  shared helper that does would.
- **The look hook is MouseLook's, not Analog's.** Verdite3's mouse already owned a
  replace on `func_8002F5C0`; the sticks were added inside it
  (`Analog.BeforeLook`), the inverse of Verdite2, where `Analog` owns the hook
  and the mouse rides it. Both put one step through one routine.
- **The pad word is put back after each call** (Verdite2 leaves it changed):
  stage 4 copies it to the previous-frame word after the calls.
- **Shared candidates for Verdite Core**: `Shape`, `RawMag`, `Step`,
  `Accelerate` and `Drive` are identical bar the masks' width (u32 in Verdite2,
  u16 in Verdite3); `BindingTable` and `InputSection` differ only in the action
  column's verbs and the pages they call.
- Measured with a synthesised stick (no pad attached): full right steps the yaw
  88 a tick (the overspeed path), the left stick drives the walk velocities to
  the expected fraction of 200, the D-pad turns as before with the sticks centred,
  144.0 fps at 15 ticks/s. Not judged: the pitch direction, the leak on a real
  pad, the feel. Five opencode agents (the patch, the probe, the pane, the pages,
  an independent read of the walk routine); merged and measured by hand.
- **Correction, the same day: Verdite3's face verbs.** The layout was built on the
  action-mask table as RAM holds it before a save loads (the game's preset 0),
  but a New Game sets preset 3 (`func_8002B64C`), and the verbs read from the
  code had attack and magic the wrong way round. Settled in play: Square swings,
  Triangle casts, Circle opens the menu, Cross examines, which is Verdite2's
  physical layout, so the keys and mouse now match Verdite2's. Read a game's
  action table from a loaded save, and settle verbs by pressing buttons
  (Verdite3 `8c84eb0`, superseded by the next commit there).

### 2026-10-05: Verdite3's menu world

**Done in Verdite3** (`main`, not committed), written up in its
`docs/WIDESCREEN.md`, "Menus and messages draw the world live". `MenuWorld`
ported, the presenter and the message fade both, on by default.

- **The same two shapes on new addresses.** Menus: enter `func_80027198`, frame
  head `func_80026FE4`, presenter `func_800270F8` (the paste), leave
  `func_80027310`, the buffers shrunk to `0x7400` each and the frame stored after
  them. Messages: `func_800441D4(file, entry)` and its fade `func_80043BB8`, the
  frame `MoveImage`d onto world texture space at (320, 0), and a wait between the
  fades that draws nothing; Verdite3's docs had misread the fade as an area-load
  bar.
- **No relocated buffer to borrow.** Verdite3 keeps 2 MB and the stock buffers,
  so the menu's pass lives in the frozen frame's store (120,784 bytes, more than
  a world buffer), and a message's pass borrows the same store, which then holds
  the saved texture space: the texels go back into VRAM first and the bytes are
  kept aside in C# for the fade. Verdite2's `KF2_PRIMBUF=1` case, where a message
  keeps the game's loop, has no counterpart.
- **The fade writes the examine bit into the pad word on a press**, set or
  cleared by which button it was; the replacement keeps it. Worth checking
  whether Verdite2's `func_800356F4` does the same.
- **The world is frozen by a flag, not by holds per patch**: a menu's frames are
  frame boundaries and move `Ticks`, so `FramePacing.Frozen` pins the tick
  fraction to the last frame's and makes no walk a tick's first. Verdite2 holds
  `SpriteAnim` alone. The pass also puts back the scratchpad as stage 15 left it,
  besides the GTE, and draws from the carried camera.
- **The pass must be a scene to the retained renderer**: Verdite3's `GpuWorld`
  begins a retained frame only inside stage 15, so under "Retained GPU" the pass
  fell back to the packet path, and with the Z-buffer its near faces in table
  order left slivers of the wall in front of a sign (seen in play). The pass now
  enters the scene and begins the frame after the cull grid. Verdite2's
  `MenuWorld` under `KF2_RETAINED` has the same question.
- Measured at 16:9: the in-game menu's margin 34.6% against 0.0%; a held message
  418 frames live with no VRAM fallback, against the game's loop falling back a
  few seconds in. Picture not judged. Done by hand.

### 2026-10-05: the subtrees reconciled, and the launcher and packaging in Verdite Core

Asked for by the user: "the launcher should be in verdite-core, in fact, get
everything reconciled including recompone."

**RecompOne.** Verdite3 carried seven fork commits nobody had pushed (its
retained-scene work, `0085`-`0089`). Split from Verdite3, they stacked on the
fork's `2013e51` as a fast-forward and were pushed (`2e766da`), then pulled here,
bringing `2013e51` with them: the vblank root counter's event once a vblank,
which this branch had held back as an ear question (`docs/TODO.md`; the ambient
retrigger clock `0x801B6CAC` now runs at 60/s). Acceptance before and after the
pull: `open → game → fdat02 → fdat05`, slot 2 at HP 46/86, 144.0 fps at
19.9-20.0 ticks/s, `wide 288`, the same `[KF2]` lines, `carda.sav` untouched. A
docs-only fork commit (`f02f484`, `RECOMPONE_PATCHES.md` naming the shared
launcher as the caller of `0030`, `0061` and `0081`) followed. **Both games now
hold fork `f02f484` and Verdite Core `91f4a4a`, each equal to the pushed commit.**

**The launcher and the packaging** (Verdite Core `91f4a4a`, authored in Verdite3
as `c2f617a`). `launcher/` is `Verdite2.Launcher` made game-agnostic, taking a
`LauncherGame` record: the inventory's "shell plus about twenty strings" held, and
the one piece of game logic (`UpdatePopup`'s overlay names) became the record's
`PlayAfter`. The disc check's file list and floors, and the build key's file
list, are now read from the game's recompiler config (every overlay's `file`, at
least `offset + skip + size`), which reproduces the list written here by hand.
`launcher/Launcher.targets` holds what the csproj held; `packaging/` holds the
AppImage, Windows, Inno and stub builds, read through a game's
`packaging/package.env`, and `scripts/release.sh`. `src/` gained `DesktopEntry`
(from `patches/`) and `WindowIcon` (the card icon's decode and scale, out of
`CardIcon`). The Windows script's font licence path, still the pre-subtree
`patches/recompone/assets/`, is right in core's copy.

Measured here: an AppImage built by core's script on an empty data folder
recompiled, compiled and reached `fdat05` (HP 46/86, 144.0 fps at 19.9 ticks/s,
`wide 288`, the developer build's `[KF2]` lines), announced `v0.4.0` from GitHub,
and refused `SLUS-00255` with the same message. In Verdite3, which gained
`Verdite3.Launcher`, `VERSION` 0.1.0, packaging and CI: the same from an empty
data folder (`fdat17`, 144.0 fps, 14.9 ticks/s), a 404 from the update check (no
release yet), and `SLUS-00158` refused. The Windows package was not run (no
`pwsh`); the stub builds under each name.

**Verdite3's window icon** is its fourth save slot's card icon, found in
`FDAT.T` entry 96 (Verdite3's `docs/PACKAGING.md`); unlike Verdite2's, no
palette copy exists in `GAME.EXE`, so it is fingerprinted rather than found by a
signature.

**Not pushed:** this branch and Verdite3's `main`. The fork and Verdite Core are.

### 2026-10-06: Verdite3's water

**Done in Verdite3** (not committed), written up in its `docs/WATER.md`. Murky water,
water waves and planar reflections ported onto its retained renderer, off until
judged. No runtime change: `0067`, `0068`, `0078` and `0085` carried everything.

- **Water is the scrolling texture's rect**, as the fluid slots are here: two records
  at `0x801AEB1C` (dest `+8`/`+0xA`, size `+0x14`/`+0x16`), one live in every area at
  (1016, 96) 8x32. Averaging faces on it are water; four areas also draw an additive
  liquid on it, left out as the fire is here. No model mesh in the area tables samples
  it, so Verdite3 has no `ModelWater`.
- **The swell is retained only**: `WaterSwell` there decides the free corners and flags
  them (`FlagSwell`); nothing is moved in guest RAM.
- **The mirror is not a second walk**: the retained frame's halves and instances drawn
  from `F R S` before the frame's `DrawOTag`, with a frustum cull of its own in place of
  `PlanarCull`. Models only that cull reaches are not mirrored.
- **The mip atlas's lookups are per view**: drawing the mirror through the atlas cost
  as much CPU as the main view (1.6 ms a frame over a pool), so Verdite3's mirror leaves
  it out by default. A per-frame memo in `UpdateWorldMips` would serve both games.

### 2026-10-06: Verdite3's `Prejit`

**Done in Verdite3**, written up in its `docs/DEVELOPMENT.md` ("The stutters",
"Compiling ahead"). `patches/Prejit.cs` copied into Verdite3's `patches/` under
`Kf3`/`KF3_PREJIT`, not moved into Verdite Core: the overlay ranking is the only
game-specific data, and the names (`fdat*`, `game`, `main`, `open`, `end`) happen
to be the same; Verdite2's copy is unchanged. Verdite3's copy differs in
three measured ways that would serve Verdite2 too, and are the case for making it
core's when it is next touched:

- **Several threads** (a quarter of the cores, at most four, `KF3_PREJIT_THREADS`)
  taking from one ordered list: one thread was 8.9 s there, still compiling `game`
  when the autostart reached its area; four are 2.7-2.9 s.
- **Constructors**: `GetMethods` returns none, so every `.cctor`/`.ctor` compiled on
  the game thread at its type's first use. Verdite2's pass has the same gap.
- **Value-type generic instantiations** named by fields (`Dictionary<(…), …>`,
  `List<Vertex>`): about 0.2 s more of the pass, and a third off the first area
  frame's remaining JIT.

The runtime's profiler now reports the game thread's JIT only (fork `0045`,
amended): with a background pass the process-wide figure charged its compiles to
whatever frame was running. Verdite2's own Prejit numbers were taken before this
and include that.

### 2026-10-09: Verdite3's work brought back

Verdite3's 97 commits since 2026-10-05 sorted against this port: most were already here,
KF3's own, or notes; both subtrees were already pulled. Brought back, each on `dev`:

- 0103's near clip for GPU-drawn models, on (`KF2_GPUWORLD_MODELCLIP`), with a probe; the
  surface probe's `ahead`; fog by distance from the eye as the default.
- The debug mod's film mode on V and the mute key, and `FrameSmoothing.LeadSuppressed`
  under its cinematic camera.
- 3D (tank) controls (`kf2.controls`, `KF2_TANK`).
- The rolling `preview` prerelease (`scripts/prerelease.sh`, `PREVIEW_PASSWORD`).
- The menu pointer on PORT SETTINGS (`MenuMouse.Rows`, the shell's `point`).
- The message fade's length, x1-x4 (`kf2.messagefade`).
- Escape opens the menu, a click captures the pointer (the shell's `key`).
- The shell's `scale`, and the menu bar hidden at startup.
- The crossing measured: no frame loses its models here (`c84d822`'s defect is absent),
  but every map change was built twice; now once (`46edd23`'s concern, fixed another way).
- The bow found in this game (`func_800271D0`, weapons 16 and 17), and Verdite3's rumble
  and gyro aim put on it.

Not brought back, being KF3's: its map viewers on M, the picked-up item's hold and turn,
the spell palette fix (cannot happen here), the front-table splice, its creature
streaming at the render distance, the full-card save, and taking the murk out of WATER.

### 2026-10-09: the settings model and `GpuFrames` moved into Verdite Core

Every patch both ports have was ranked by how far apart the two copies are, method by
method, after stripping the tag, the addresses and the numbers. Six files were the same
in both apart from the namespace, and they are core's now, in `src/Settings/`
(`PortSetting` with `Stored` and `Ui`, `SettingsStore`, `SettingsSession`,
`WindowMode`, `MenuFont`) and `src/GpuFrames.cs`. The only seams: the store's log prefix
and its panel name come from `Game.Tag` and `Game.Id`, which gives the same strings
as before, and `SettingsSession.Open` takes the port's list (`PortSettings.All`), which core
cannot name. Both ports boot to `0 default(s) wrong`.

Next in the ranking, the same code with only the data differing: `FrameProfiler`
(the address table), `ProfilerPanel`, `BindingTable`, `Prejit`, `GameState`'s
helpers, and inside files that differ, `Analog`'s stick maths, `MenuMouse`'s pointer
helpers and `AgentServer`'s plumbing. Where the two ports' attach code differs, take
KF3's form (`HookAttach.OnOverlayLoad`, `Differential`); some of KF2's files still
listen to `OverlayLoadedEvent` themselves.

#### The ranking: every patch both ports have, by tier

KF2 has 187 C# files under `patches/` and `mods/`, and KF3 has 110. They have 68 file
names in common. Each pair was scored twice:
- **Measured:** the changed-line count of the folded diff in the handoff below.
- **Judged:** each member read and scored 0-4: 0 identical; 1 data only; 2 a few game
  seams; 3 rewritten; 4 one port only. The file score is the line-weighted judgement.

Where the two disagree, the measured count decides the tier: a file scored 2 whose diff is
most of its length is treated as 3. The judged scores were made by small models, file by
file; a member called identical was checked where it mattered, but not every one.

**Tier 0, identical but for the namespace: moved** (above). `PortSetting`, `SettingsStore`,
`SettingsSession`, `WindowMode`, `MenuFont`, `GpuFrames`: 0 changed lines each.

**Tier 1, the same code with the data apart: next.** `ProfilerPanel` (8),
`FrameProfiler` (66), `HotkeyGate.Typing`, `settings/BindingTable` (18), `Prejit` (42),
`settings/KeyLayoutPage` (19). See the handoff below.

**Tier 2, the same algorithm with a few game seams: move with callbacks, by group.**

| group | files (changed lines) | identical already | the seams |
|---|---|---|---|
| sticks and pad | `Analog` (231), `settings/AnalogPage` (43), `AnalogProbe` (175), `settings/InputSection` (72), `settings/MousePage` (83), `KeyLayout` (69; KF1 too) | `Analog`'s `RawMag`, `Shape`, `Axis`, `Step`, `Accelerate`, `TankLeft`; `AnalogProbe`'s button table | the pad and mask-table addresses (KF2 reads the mask words as u32, KF3 as u16); the look and move hook sites; KF2's extra rows; KF3's `TurnSens`/`PitchSens` defaults (1.25) |
| bow | `Rumble` (118), `GyroAim` (37) | `Rumble`'s `Test`, `Twang`, `Creak`, `Strain`, `Ask`; `GyroAim.Take` | the bow's reads: KF2 has its own `Bow` class off `func_800271D0` (weapons 16, 17), KF3 inlines them (27, 28) |
| picture switches | `Perspective` (110), `Subpixel` (106), `NoDither` (130), `TrueColor` (23), `Widescreen` (203), `CameraBlock` (257); each has a KF1 copy | `Widescreen`'s `Parse`, `SetAspect`, `Presets`; `CameraBlock.Replace`; `Subpixel.Cull` | the per-overlay `DrawOTag`/`PutDrawEnv` address table, the same pattern in all six; KF1 and KF3 are one lineage and KF2's has moved on (PGXP seams, probe-gated attach) |
| boot and lifecycle | `BootExe` (59), `EndingHold` (73), `CardIcon` (89), `CrashReports` (36), `AutoReload` (157), `AgentBeacon` (76; KF1 too) | -- | addresses, the stub's index word, the reload delay (2.0 against 2.5), the player block |
| the settings page | `SettingsPage` (225) | `PortSettings`' `Named`, `Number`, `Validate` | the menu's addresses, masks, templates, row count and layout constants: all data, a large table |

**Tier 2 by score, tier 3 by measure.** `ZBuffer` (394; KF2 555 lines against KF3 161),
`PolyAssemblerLit` (274), `PolyAssemblerDepth` (168 of 182), `Waves` (149 of 235), `MoPose`
(653). The renderers have parted: KF3 draws from a retained mesh, so what KF2 does in guest
RAM KF3 does not do at all. Leave them in the ports.

**The debug mods** (`mods/kf2debug`, `mods/kf3debug`): `DebugMod` (17), `Cheats` (33),
`Hotkeys` (57), `Noclip` (112), `GameState` (116), `Attributes` (147), `Warp` (250), `Items`
(294), `DebugPanel` (300). The hooks are attributes, which take constants, so those classes
stay. Only the pure helpers can move; see the handoff. KF3's are ahead in features: the
equipment and spell editors, *Peaceful*, the warp's area field.

**Tier 3, rewritten for the same purpose: stay in the ports.** `FramePacing` (689),
`LoopPacing` (454), `SpriteAnim` (158), `MenuWorld` (677), `MenuMouse` (823), `ModelWalk`
(1924), `CullCone` (586), `GearCompare` (619), `RetainedMap` (943), `RetainedModels`
(1489), `GpuWorld` (714), `RenderDistance` (449), `PolyAssembler` (1135), `PolyAssemblerHud`
(408), `WaterSwell` (367), `Murk` (65), `AutoStart` (150), `RateCensus` (142),
`CrossProbe` (352), `AgentServer` (705), `PortSettings` (346),
`settings/GameplaySection` (63). Inside some of them are pieces identical in both, which
can come out without the rest:
- `MenuMouse`'s `Point`, `In`, `TakeWheel`, `HoverLive` and `Down`.
- `AgentServer`'s `AcceptLoop`, `Enqueue`, `Err`, `Q`, `Buttons` and `DoScale`; the verbs
  would need a registry.
- `MenuWorld`'s `SetFadeVBlanks`, `Quiet`, `RestoreClear`, `TPage`, `Xy` and `Link`.
- `WaterSwell`'s world-key packing.
- `PolyAssembler.Queue`.

Lineage: `FramePacing`, `AutoStart`, `RateCensus` and `AgentServer` are one design in KF1
and KF3, and KF2's has grown away from it. KF3's `SpriteAnim` has no 500 ms watchdog, which a
frame-keyed hold needs ("Rules that bite"), so that one is likely a KF3 defect.

**Tier 4, one port only.**
- 119 of KF2's files have no KF3 counterpart: the remaster editor, the map, smoothing,
  `Stage13`, the planar walk.
- 42 of KF3's have none in KF2: `NativeScene*`, `NearPath*`, `Stage15`, `ItemTurn`, `Magic`.
- Five files are KF1's and KF3's but not KF2's: `VBlankPacing` (34 changed lines),
  `MouseLook` (78), `ViewSmoothing` (162), `TestingSection` (173), `ModelSmoothing` (618).
  The first two are a KF1-KF3 tier 1 in their own right.

#### Handoff: tier 1, the same code with the data differing

What is left after tier 0 that is the same code in both ports with only the data apart:
addresses, keys, labels, help text. The "diff" column counts the changed lines after
`diff -w` of the two copies, with the tag folded (`KF2`/`KF3` → `KFN`, `Verdite2`/`3`) and
comment lines dropped; reproduce a row with

```bash
diff -w <(sed 's/[Kk][Ff]2/KFN/g;s/Verdite2/VerditeN/g' KFII-PC/<path> | grep -v '^\s*//') \
        <(sed 's/[Kk][Ff]3/KFN/g;s/Verdite3/VerditeN/g' verdite3/<path> | grep -v '^\s*//')
```

**How tier 0 went, and how to repeat it.** Write the core copy by hand in
`tools/verdite-core/src/` (namespace `Verdite.Core`, the tag from `Game.Tag`/`Game.Id`/
`Game.Env`). Core cannot name a port's type, so each seam becomes an argument, a
delegate the port sets in `Program.cs`, or a table handed in. Delete both ports' copies, build both, and
boot both. Then commit, in this order: a KF2 commit touching only `tools/verdite-core/`,
the KF2 port commit, `bash scripts/setup_tools.sh --push-core --no-build`, then in Verdite3 stash the port
changes, `--pull-core --no-build`, pop them, and commit; check `diff -r -x bin -x obj`
of the two cores is empty. Where the two copies differ in quality rather than data,
take the newer member and name in the commit which port it came from. KF3 is ahead on attach
(`HookAttach.OnOverlayLoad`, `Differential`); KF2 is ahead on most of the rest.

In order, easiest first:

| files | KF2 / KF3 lines | diff | the seams |
|---|---|---|---|
| `ProfilerPanel` + `FrameProfiler` + `HotkeyGate` | 830/831, 406/423, 17/— | 8, 66 | move together (they name each other; `GpuFrames` is core already) |
| `settings/BindingTable` | 267/250 | 18 | the per-button action text only |
| `Prejit` | 384/338 | 42 | not data: two versions to merge |
| `settings/KeyLayoutPage` | 78/67 | 19 | KF2 an `IPatchPage`, KF3 a static `Draw` from its `InputSection`; the tip text; calls the port's `KeyLayout` |

**The profiler.**
- `FrameProfiler.Known`, the address-and-label table (KF2's 24 rows, KF3's 36), becomes the port's, handed in.
- The wait sections differ: KF2 registers `MenuWait` and `LoadWait`, KF3 registers `VBlankWait`. Move each `Profiler.Register` into the pacing file that uses it (`MenuPacing.cs:373`, `LoadPacing.cs:383`, KF3's `VBlankPacing.cs:140`).
- The Shift+P gate: KF2 calls `HotkeyGate.Typing` (`patches/HotkeyGate.cs`), and KF3 inlines the same ImGui `WantTextInput` test. Move `Typing` to core. `HotkeyGate.Editing` names `Remaster.Editor`, so it stays in the port.
- `FramePacing` stays the port's. `FrameProfiler` reads `FramePacing.Floor`, and the panel reads `Enabled` and `TargetFps`, which KF3 also gates on `!Uncapped` (KF2 has no `Uncapped`). Make the panel's target one `Func<double>` (ms, 0 for none) that each port sets.
- The panel's default probe (`"game:80040348"` / `"game:8003BFD0"`) and the stage count on the button ("Time the 13 stages" / "15") become data.
- Check: Shift+P in both, the CSV written, the stage button timing every stage.

**`BindingTable`.**
- The table and its drawing go to core.
- The sixteen action descriptions are handed in by the port as button name → text. KF2's are "use, open; confirm", "look up", …; KF3's are "examine, open, talk", "tilt the view", ….
- KF2's sits in `Kf2.Settings`, KF3's in `Kf3`.
- Check: the Input pane's table in both.

**`Prejit`.**
- Take KF2's per-batch counters (`_labels`, `_remaining`, `_batchPrepared`): each batch is reported by the thread that finishes it, with its method count.
- Take KF3's `Configure(enabled, probe, threads)`, with the env read in `Program.cs` through `Game.Env("PREJIT_THREADS")`.
- The one piece of game data is `PortMethods()`'s `AssemblyMethods(typeof(Prejit).Assembly, "Kf2")`. The port's namespace has to be handed in, plus `"Verdite.Core"`: core compiles into the same assembly, and its methods, `Prejit`'s own among them, want warming too.
- Check: `KF2_PREJIT_PROBE=1` prints a "done by" line per batch, and an area arrives without the JIT stall ("The first frame of an area was the JIT" in `DEVELOPMENT.md`).

**`KeyLayoutPage`.**
- Its 70 lines pay only if `KeyLayout` itself is shaped for core first: the stock and port tables handed to core's `KeyLayoutApply`, `Version` and `Superseded` staying the port's. Leave it last, or for `Analog`'s move.

**Not tier 1 after all, though the agents' scoring put them there:**

- **`settings/AnalogPage`** (165/152, 43 changed lines) names about 40 of `Analog`'s keys and fields, so it moves with `Analog` (tier 2), not before.
  - KF2 has rows KF3 lacks: *Invert look Y*, the probe and its interval.
  - KF3 saves through a local `Set`; KF2 saves through `PatchSettings.Set`.
- **`Subpixel`** (206/151, 106) and **`CameraBlock`** (418/218, 257): the line counts say tier 2.
  - KF1 has copies of both. KF1's and KF3's share a lineage, and KF2's has moved away from it.
  - Measure all three before deciding.
- **The debug mods** (`mods/kf2debug`, `mods/kf3debug`: `Cheats` 33 changed lines, `DebugMod` 17, `Noclip` 112, `Attributes` 147, `GameState` 116) are a different question:
  - Mods are compiled at run time and hook with `[PreHook("game", Address = 0x…)]`. An attribute takes a constant, so no per-game table can feed it, and the hooked classes stay in the mods.
  - What can move is the pure helpers, identical in both: `GameState`'s `ReadS16`/`ReadS32`/`WriteS16`/`WriteS32`, `SetAngles`, `Position`/`SetPosition`/`Angles`, `AngleToRadians`; `Noclip`'s `Lag`, `SmoothLook` and `Format`. A mod reaches them with `using Verdite.Core;`; mods do not get the global using.
  - One behaviour differs, so decide it rather than copy it. KF2's speed cheat scales in `BeforeMove` (`0x80028DB8`) and stands down under noclip. KF3 split it into `BeforeMove` and `BeforeWalk` (`0x8002F9BC`), without the noclip test.

### 2026-10-11: the setup's core commits reconciled with the fork

The first-run setup (`src/Onboarding`, the launcher's hold on the build; `SETTINGS.md`,
"Onboarding") was three core commits here on the fork's `b74cab8`, while the fork had
moved on to `9d6bac8` (the hourly update check, and `content/manifest.txt`, so a release
unpacked over an older one compiles only what it shipped). Verdite3 was already at
`9d6bac8`.

- **`--push-core` refused** with "no subtree join found on the first-parent line". The
  join that took `b74cab8`, `87ae1c5`, reaches this branch only through the second parent
  of `412eaad` ("Merge dev into v0.5.0-staging"), and the script looks along first
  parents only.
- **The one-parent rejoin `447557c` did not break the pull.** It is the shape
  `RECOMPONE_FORK.md` warns of ("The pull that found a one-parent rejoin"), but the real
  join `87ae1c5` follows it, and `git subtree pull --squash` finds that join's squash
  (`192039d`) first. The rule there still stands.
- `--pull-core` then merged `9d6bac8` cleanly (`76671b4`; `Launcher.cs` and
  `Launcher.targets` overlap and auto-merged). That merge is the new first-parent join,
  so `--push-core`'s checks now pass, with no mixed commit, and its split is the three
  setup commits on `b74cab8` plus the merge to `9d6bac8`: a fast-forward of the fork by 4.
- Checked: the game and launcher build; the launcher's manifest lists
  `content/config/onboarding.json` and both `Onboarding` sources; the setup's boot check
  reads 38 keys, 0 problems; slot 2 restored in area 1 at hp 46/86, 144.0 fps drawn at
  15.0 ticks/s, `[present] wide 288, plain 0`, perspective 100.0% hit at 18720 caught/s.
- Not pushed: the setup's popup has not been judged by eye. After the push, Verdite3
  pulls core and builds and boots; it has no `onboarding.json`, so `Onboarding.Load`
  returns null and the launcher shows no setup.

**Verdite3's game commits since 2026-10-09, sorted 2026-10-11:**

- `f956c1c`, the manifest checked in CI, and `a09120d`, the hourly check's docs:
  taken (`1825088`).
- `6603075`/`e7f95f2`, no mark of the port's own: taken. The orb (`verdite2.png`,
  `verdite2.ico`, `make-icon.py`) is gone, the executables carry no icon resource,
  and the card icon is the only one (`PACKAGING.md`, "The icon comes off the disc").
- `2853787`, the arrival flag lost under pacing: the same shape here (`0x8019941E`,
  cleared inline between stages 5 and 6), but never lost, since the frame after a
  load always ticks. Measured, not fixed: `GAME_INTERNALS.md`, "The arrival flag
  `0x8019941E`, and why pacing does not lose it here".
- `5696cab`, the map at another BRIGHTNESS: does not apply. Verdite3 drew a half on
  the GPU only at `0x808080` and sent the rest to the packets; `RetainedMap` here
  bakes the assembler's live light word (`0x8006E604`) into every corner and hashes
  it each walk, so a change is a rebuild, and there is no fallback keyed on the
  colour. `KF2_GPUWORLD_RECORDCHECK=1` over areas 0-6: 2,932,975 corners, 0 coloured
  otherwise than the GTE.
- `4362046`, billboards turned to the eye: KF3's own.
- `b20242e`, spell effects' palette: cannot happen here (2026-10-09).

### 2026-10-13: the tag is the version, as Verdite3's

Verdite3's `de8a476`/`06c982d` brought over: `VERSION` is gone, and the version is
the newest `vMAJOR.MINOR.PATCH` tag `HEAD` contains, by the rule Verdite Core
already applied (a file, `VERDITE_VERSION`, the tags, `0.0.0`); nothing in the
core changed. CI checks out with `fetch-depth: 0` and refuses a `VERSION` file;
`release.yml` takes `VERDITE_VERSION` from the pushed tag in place of asserting
the two equal. `release.sh` now only tags. Measured: `version.sh` prints `0.4.0`
off `v0.4.0`, and the launcher builds as `0.4.0+dae2255b9`. See "Versioning" in
`PACKAGING.md`.

### 2026-10-13: Verdite Core pushed at `1301748`, and Verdite3 given it

The fork was at `9d6bac8`, Verdite3 exactly on it; this repo's `tools/verdite-core`
was that plus the setup's work (`src/Onboarding`, the launcher's hold on the build,
`MouseGame.OnIsSetting`/`LeadIsSetting`), 1,570 lines, nothing of the fork's undone.

- **`--push-core` would refuse again, as on 2026-10-11**: the join `76671b4` reaches
  `v0.5.0-staging` only through a merge's second parent (`53fcacc`, the onboarding
  merge), and the script looks along first parents only. Its checks were run by hand
  instead: no mixed commit since `76671b4` (`4f13c17`, `65159be`, core only), and
  `git subtree split` gives `1301748`, whose tree equals this repo's prefix and which
  contains `9d6bac8`. Pushed as that commit: a fast-forward of the fork by 6. A
  `--pull-core` after any staging merge would put a join back on the first-parent line.
- Verdite3 pulled it (`f5b9f0b`, PR #6 there, not merged); its launcher and game build
  with 0 errors, not booted. It has no `onboarding.json`, so it shows no setup, and its
  mouse look and lead stay settings (both flags default true).
- Not judged by eye: the setup's popup.

**Corrected from 2026-10-09:** `ItemTurn` is not KF3's own. Turning a held-up item with
the mouse, the right stick and the gyroscope, and the GYRO row that sets the item's gyro
and the bow's together, are wanted here; KF2's pickup has not been found yet.

### 2026-10-13: `ItemTurn` ported onto the menu's item preview

**Corrected later the same day:** KF2 does hold a picked-up item up, through the same
routine as KF3 (`func_8004831C`; GAME_INTERNALS, "The pickup holds the item up in a loop
of its own"), and `ItemTurn` now turns that too, with Verdite3's `ItemEye` ported beside
it and a correction for the item's own geometry that Verdite3 lacks (INPUT, "Turning a
picked-up item", "The item held up at the eye"). The centring is a candidate to carry
back to Verdite3, which had the same complaint. What follows was written before then.

The one model found showing an item was the inventory's and the shops' preview
(`func_8002156C`), so Verdite3's `ItemTurn` was ported onto that (`patches/ItemTurn.cs`; INPUT, "Turning the item preview"). The input half
(stick, gyro, the rest threshold, the hold and resume) is Verdite3's almost line for
line, and the rotation is PSY-Q's `RotMatrix` rather than KF3's own; the mouse is a drag
on the model, since the menu's pointer is free, where Verdite3's is the captured look.
The GYRO row now sets both gyro readers, as Verdite3's does. A candidate for Verdite Core
once both are settled: the input and the matrix maths are game-agnostic, the hooks are not.

