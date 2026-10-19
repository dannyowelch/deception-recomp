# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

A static recompilation of **King's Field (NTSC-U, `SLUS-00158`)** — the North
American release of the Japanese *King's Field II* (`SLPS-00069`) — using
[RecompOne](https://github.com/BlackLabelHQ/RecompOne). The series was renumbered
for the West: the US-boxed "King's Field II" (`SLUS-00255`) is a *different game*
and every address here is wrong for it.

There is no decompilation, no ELF and no `.map`. Function boundaries come from a
linear sweep and PSY-Q library functions are identified by hand, so most work in
this repo is *reverse engineering*, not application coding: find an SDK function's
address in the disc image, map it to the runtime's HLE implementation, re-run the
recompiler, run the game, read the logs.

**`NOTES.md` is the index, and `docs/` is the working log.** `NOTES.md` carries
what the project is and where it stands, plus a map of the documents under
`docs/` and the exact section titles in each. Read the index before starting
anything, then the one or two documents your task touches — they are split by what
you would be doing when you need them:

| file | when |
|---|---|
| `docs/DEVELOPMENT.md` | build, run, diagnose, measure |
| `docs/ENV_VARS.md` | every `KF2_*` switch, in one list |
| `docs/SETTINGS.md` | PORT SETTINGS in the game's menu: the list, the store, the session, the page |
| `docs/RECOMPILATION.md` | config, overlays, function maps, SDK addresses |
| `docs/RUNTIME.md` | interrupts, HLE, the `tools/RecompOne/patches/` stack |
| `docs/RECOMPONE_FORK.md` | the fork `tools/RecompOne` is a subtree of, and merging from upstream |
| `tools/RecompOne/docs/RECOMPONE_PATCHES.md` | every change the port made to RecompOne, `0001`-`0110` |
| `docs/RENDERING.md` | perspective correction, sub-pixel, Z-buffer, dither |
| `docs/WIDESCREEN.md` | aspect ratio, the HUD, the three culls |
| `docs/AUDIO.md` | SPU interpolation, reverb, XA resampling, the host output |
| `docs/GAME_INTERNALS.md` | the game's own addresses and routines |
| `docs/PATCHES_AND_MODS.md` | hooking, settings UI, frame pacing, smoothing, the map |
| `docs/INPUT.md` | pad, sticks, keyboard, mouse, the menu pointer |
| `docs/PACKAGING.md` | the redistributable: the launcher, the first-run build, CI |
| `docs/TODO.md` | next steps and open, undiagnosed questions |
| `docs/GPU_RENDERER.md` | the GPU (retained-mode) world renderer: the plan and its work |
| `docs/SHARING.md` | sharing with Verdite1 and Verdite3: the buckets and the progress log (plan in `SHARING_PLAN.md`, per-file detail in `SHARING_INVENTORY.md`) |

Update the right document when you learn something — that is where findings
belong, not in commit messages, and not in this file. Source comments still say
`See "X" in NOTES.md`, and **the text they name is not in `NOTES.md` any more** —
the titles are unchanged, so the index resolves X to a document, but it is a hop
rather than a direct hit. Grep `docs/` for the title, not `NOTES.md`.

## Build and run

Nothing here builds without the disc (gitignored, `disc/KingsField2.cue`).
`tools/RecompOne` is a **subtree** of the standalone fork
`Voicedrew11/verdite-recompone` — its sources are tracked here, so a fresh clone
already has them and nothing needs cloning or fetching.

```bash
bash scripts/setup_tools.sh          # build the recompiler (tools/RecompOne)

# recompile MIPS -> C# into generated/ (~2234 functions, ~182k lines)
dotnet run --project tools/RecompOne/RecompOne.Recompiler -c Release --no-build -- config/kf2.json

dotnet build Deception.csproj -c Release
dotnet run --project Deception.csproj -- disc/KingsField2.cue
```

`setup_tools.sh` builds; `--pull-fork [ref]` takes the fork's changes (ref defaults
to `main`), `--push-fork` sends this repo's `tools/RecompOne` commits to it, and `--signatures` fetches
the 15.7 MB PSY-Q bank from upstream at the `UPSTREAM` pin (gitignored, read only
by the standalone `--autoconfigure`). The cue path is needed at *play* time as
well as at recompile time. `--pull-core [ref]` and `--push-core` do the same for
`tools/verdite-core`.

There are no tests. Verification is empirical: run the game with log channels on
and check the trace against what the SDK sequence should look like (see the
steady-state `KF2_LOG=sdk` excerpt under "Status" in `NOTES.md`).

**Anything that has to be judged by eye is the user's job, not yours.** Do not
try to capture, screenshot or otherwise scrape the game window — it burns a lot
of context and produces nothing a person could not tell you in one sentence.
Measure what a counter can measure, then say plainly what still needs looking at
and ask. That distinction is already all over `docs/`, which repeatedly
separates "mechanism measured" from "picture never checked"; keep writing it down
that way.

### Diagnostics

The switches used most. The full list is `docs/ENV_VARS.md`; it is not imported
(it is ~9k tokens), so grep it for the switch you need.

```bash
KF2_LOG=bios,cd,gpu,dma,sdk,spu,mdec  # or KF2_LOG=all; wired up in Program.cs
KF2_CDTRACE=1                          # stack trace on first CD register access (patch 0002)
KF2_AUTOPAD=8:Start:400,20:Circle:200  # scripted pad input: seconds:button:holdMs
KF2_FPS=120                            # 60 (default), any number, or off
KF2_FPS_PROBE=1                        # a line a second: fps drawn, ticks taken, what each smoother is doing
KF2_TICKRATE=20                        # the world's tick rate (15, and not a setting); a comparison only
KF2_PRESENT_PROBE=1                    # proves frames reach the screen; prints nothing if they do not
KF2_AUTOSTART=2                        # boot straight into save slot 1..3, past the title menus
KF2_AGENT=1                            # [KF2-AGENT] state lines on stdout
KF2_SHELL=1                            # TCP 127.0.0.1:27900 command channel
KF2_BOOTEXE=end                        # boot straight into OPEN.EXE, GAME.EXE or END.EXE
```

Prefer the mods under `mods/` (**enable them in the game's Mods panel** — mods
default to off and load silently when disabled) to `KF2_LOG=sdk`, which is
gigabytes a minute. `KF2_LOG=bios` is very expensive during play — the game polls
`PAD_dr` hundreds of thousands of times a second.

`Program.cs` is hand-owned (RecompOne would otherwise generate one into
`generated/`); add new env-var-driven diagnostics there, and add them to
`docs/ENV_VARS.md`. The docs mention `KF2_TRACECALL` — that was an ad-hoc local
edit to the dispatcher and is *not* in any committed patch; re-add it by hand if
you need indirect-call tracing.

**For a hang, take the managed stack of the live process instead of adding
logging.** Recompiled functions carry their MIPS address in their name, so the
trace names the routine directly:

```bash
~/.dotnet/tools/dotnet-stack report -p $(pgrep -f net10.0/Deception)
```

Start the game from the same shell you run that in, or the diagnostic socket in
`TMPDIR` will not be found.

### Driving the game without a human

- **The title's attract footage is a movie, not a live scene**: `OPEN.EXE` plays a
  streamed MDEC video, so none of the port's enhancements reach it. Early sessions
  recorded a live demo walking into `fdat05` a minute after boot, but two measured
  idle runs (110 s and ~165 s) never left `overlay open`, so do not rely on it; get
  a live session with `KF2_AUTOSTART`. In an area, `AutoReload.Simulate()` kills on
  demand, and the death clock at `0x8019951A` can be pinned to hold any frame of the
  death sequence.
- **`KF2_AUTOSTART=<1..3>`** — an agent left at the title waits forever (the boot
  menus take no input by the usual routes, and `KF2_AUTOPAD` only arms once an area
  has loaded). This drives the pad through `PAD_dr`: Start, Cross into a New Game
  in `fdat02`, then loads the slot over it through `AutoReload.LoadSlot`.
  `KF2_AUTOSTART=new` stops in that New Game, which faces scrolling water.
- **`KF2_AGENT=1`** prints `{"overlay":…,"inGame":…,"hp":…,"area":…,"slot":…}` on
  each overlay change and about once a second; `inGame:false` is how a program
  tells "stuck at the title" from "in an area" without a screenshot.
- **`KF2_SHELL=1`** — one request per line on TCP 127.0.0.1:27900, one
  single-line JSON response back: `state`, `nearby`, `load <slot>`,
  `warp <area>`, `press <button> [ms]`, `key <name> [ms]` (a host key down and up, e.g. Escape), `kill`, `ending [boss|kill]`,
  `map [on|off|toggle]` (the map mod's, with it enabled), `setup [open|apply <tier> [<tier> <tier>]]` (the first-run setup's state, opened, or tiers applied live), `waves [on|off|<setting> <value>]`, `savecheck`, `prop`, `goto <x> <y> <z> [yaw [pitch]]`, `view [<x> <y> <z> <pitch> <yaw> <roll> | off]` (the frame's camera and a cull-grid digest; with a camera, draw from it), and the remaster editor's `edit`, `select`, `set`, `pack`, `remaster`, `textures`, `level`, `camera`, plus `snap [hash|PATH.png]`, which reads the presented picture back and hashes it, `pause [on|off]`, which holds the world on the stage gate so two snaps compare one frame, `gpuworld [on|off|surfaces on|off|water on|off|models on|off|hide|show|arm on|off|sky on|off|hide|show|mirror on|off|hide|show|scene|at X Y|perpixel on|off]` (see `help`), `capture` (arms the frame capture; `KF2_FRAMEVIEW_OUT` writes it), `murk [on|off|tilt X]`, `aspect [4:3|16:9|<ratio>]` and `scale [1..8]` (the render scale at the next present, unsaved; alone, the current one), `point [<x> <y>|left|right|off]` (a menu pointer at game pixels in place of the host's), `aim [on|off|head on|off|hold on|off|ease on|off|<dyaw> <dpitch> [ms]]` (mouse aim's state, or synthetic reticle motion in angle units). The `kf2` MCP server in `mcp/` exposes the same channel.
  `ending kill` is the form that reproduces the final-boss crash; reaching it
  needs `KF2_DEBUG_GODMODE=1`, or `warp 7` kills the player on the way in.
  `press` reaches Cross but not the in-game menu's Up/Down.
- **`KF2_AUTOPAD`**'s clock starts when the first area module loads, the only
  point in the boot sequence that reliably means "in game".

See "Auto start and the agent beacon" and "The command channel" in
`docs/PATCHES_AND_MODS.md`.

## The port's own patches

Everything under `patches/*.cs` attaches at run time through `HookManager`; each
has a write-up, and the specifics (addresses, measurements, why each default is
what it is) live there, not here.

| patch | what | default | doc, section |
|---|---|---|---|
| `FramePacing` | skips the game's frame gate `func_80017880`, paces frames itself, runs the gated stages on a 15 Hz world clock | 60 fps drawn, 15 ticks/s | PATCHES_AND_MODS, "Any frame rate" |
| `FrameSmoothing`, `ObjectSmoothing`, `AnimSmoothing`, `FluidSmoothing` | carry the camera (and the compass needle and the HP/MP gauges with it, bracketing the HUD builder), the four world tables, MO pose and the scrolling textures between ticks | on; one checkbox | PATCHES_AND_MODS, "One switch for all of the smoothing", "The compass is carried with the view", "The gauges are carried like the needle" |
| `LoopPacing` | modal loops (fades, cutscenes, item/spell animations) run once per tick, gaps filled with stage-13 redraws | on | PATCHES_AND_MODS, "Loops that render their own frames" |
| `MenuPacing` | menu cursor repeat and blink held to the 60 Hz grid | on | PATCHES_AND_MODS, "The menu's cursor repeat" |
| `MenuWorld` | replaces the menu presenter `func_800226A8` and the message fade `func_800356F4`: the world is redrawn live behind menus, shops, signs and dialogue (full width, AO, Z) instead of the frozen 320-wide copy; each step of a message's fade held for 1-4 vblanks (`KF2_MESSAGE_FADE`, Gameplay ▸ *Message fade length*) | on; fade x1, the game's own | PATCHES_AND_MODS, "Menus draw the world live", "Messages draw the world live", "The fade's length" |
| `MessageText` | sign and dialogue text decoded from the message TIM in RAM (`patches/MessageGlyphs.cs`, built by `scripts/msg_glyphs.py`) and drawn as text on an opaque box; the TIM's palette is zeroed so the game's text quads draw nothing | off (judged: the mixed look is unwanted) | PATCHES_AND_MODS, "Drawing message text" |
| `LoadPacing` | loading screen's walking figure held to the vblank grid | on | PATCHES_AND_MODS, "The loading screen's walking figure" |
| `SpriteAnim` | billboard cel animation held to the tick | on | PATCHES_AND_MODS, "The flames run at the render rate" |
| `TintHold` | stage 1 `func_8002C944` in C#: it clears the screen tint every frame while the stages that set it are gated, so the reset now runs only on a tick and the death fade and damage flash stop strobing; `KF2_TINTHOLD=verify` diffs it against the recompiled routine | on | PATCHES_AND_MODS, "The tints strobed between ticks" |
| `FullRateLogic` | `KF2_FPS_LOGIC=full`; comparison only, **not shippable** | off | PATCHES_AND_MODS, "Any frame rate" |
| `Prejit` | compiles the recompiled code, the patches and the runtime on a background thread at boot, so walking into an area does not stop to JIT it (QuickJit is off, so a first call is a full JIT: 234 methods and 297.87 ms in one frame without it) | on | DEVELOPMENT, "The first frame of an area was the JIT" |
| `FrameProfiler` | per-frame time by section: every hook, the present path, the waits (`0045`); Shift+P | records while its panel is open | DEVELOPMENT, "Profiling a frame" |
| `FrameCapture`, `FrameViewerPanel` | capture one run of stage 13 and scrub it GP0 command by command on a detached software GPU: owner routine, send cost, fragments, GL batch submits and why, GPU time per batch and for AO, every runtime section, vertex-map work per routine (`0046`); Shift+F | idle until a capture; routines hooked from the first | DEVELOPMENT, "Watching a frame being built" |
| `PolyAssembler` | `func_80030540` in C# as a replace hook, rejecting polygons the view-space clipper would clip to nothing; also `func_8002FECC` (the far map tiles' unclipped assembler), the vertex transforms `func_8002E650`/`func_8002E7CC` and the HUD's `func_8002E910` (orthographic, so it publishes each vertex's fraction to the vertex map itself, and only for pieces the matrix turns), `func_8002F214`/`func_8002EAEC` (the models' lit assembler) and the clipper `Clip4FTP`/`Clip3FTP`; the GTE ops they call have a fast path in the runtime (`0047`); `KF2_POLYASM=verify` diffs each against the recompiled routine, GTE included; a clipped polygon or quad is culled on its whole area at the fractional corners, not its first three corners, through a hook on `NormalClip` (`KF2_POLYASM_FACING=0` to compare); Video ▸ Fast geometry switches them all, with the GTE fast path | on | PATCHES_AND_MODS, "The polygon assembler in C#", "The lit model assembler", "The HUD's transform in C#", "The clipper in C#", "The GTE fast path"; RENDERING, "A floor quarter missing at a short edge", "An edge-on wall lost its strips" |
| `TileWalk` | the map tile walk in C#: `func_80031C94` (the 24×24 cell sweep), `func_80031B1C` (a cell's two halves) and `func_80031950` (a half, set up and assembled). Taken for the scene it enumerates, not for time (0.013 ms a frame); `KF2_TILEWALK=verify` diffs each against the recompiled routine | on | PATCHES_AND_MODS, "The map tile walk in C#" |
| `ModelWalk` | the object and creature walk in C#: `func_800331B4` (the creature, object, effect and billboard tables) and `func_80032588` (the model submitter); and `func_80032400`, the first-person arm (`KF2_MODELWALK_ARM=0`), and `func_80032AC4`, an object of kind `0xF0`: the sky (`KF2_MODELWALK_SPECIAL=0`). Taken for the scene it enumerates, not for time (3 us a frame); `ModelWalk.Scene` publishes each submit's record, model, position and assembler; `KF2_MODELWALK=verify` diffs both against the recompiled routines | on | PATCHES_AND_MODS, "The object and creature walk in C#" |
| `MoPose` | the MO blender `func_80034DA8` in C#: the keyframe per (clip, segment) and the per-frame copy and delta decode into `0x80190AD8`; for a model the GPU world renderer draws from its mesh the copy and decode are left undone (`MoPose.Defer`) and the vertex shader blends the pose from the pose store; `KF2_MOPOSE=verify` diffs it against the recompiled routine | on | GPU_RENDERER, "Step 3, the third slice" |
| `Stage13`, `CameraBlock` | stage 13 `func_800342D8` and its camera block `func_8002E22C` in C#: nineteen calls, each through its hooks, and the HUD block, with the compass needle's spring at `0x8006E608` stepped on the tick (`KF2_STAGE13_NEEDLE=0` to compare); `Stage13.HookOrder` orders the hooks on it (`0070`); `Stage13.ViewOverride` draws the frame from a `Camera` of the port's, the cull grid following (`HideArmOnOverride` leaves the arm out; `Handed` is the player's camera); `Stage13.DrawScene` is the drawing half (`MenuWorld`); `CameraBlock.Build` (`PlanarWalk`); `ScenePass` points the frame at a table and arena of the port's and puts everything back (both); `KF2_STAGE13=verify` records the recompiled routine's calls and replays ours against them, `KF2_CAMERABLOCK=verify` diffs both | on; no override | PATCHES_AND_MODS, "Stage 13 in C#", "Drawing the frame from another camera", "The compass needle is held to the tick", "The hooks on stage 13 are ordered by what they need", "A pass of the port's own"; GAME_INTERNALS, "Stage 13's HUD block, and the compass needle" |
| `Perspective` | perspective-correct textures (`0009`, `0012`) | on | RENDERING, "Perspective correction" |
| `Subpixel` | sub-pixel vertex positions (`0010`); under it, the C# assemblers' backface cull is taken at the fractional corners (`0052`, `KF2_SUBPIXEL_CULL=0` to compare) | on | RENDERING, "Sub-pixel vertex positioning", "A thin face was culled on whole pixels" |
| `ZBuffer` | per-pixel occlusion; depth from the C# assemblers' packet records (`0050`), coplanar tolerance on the test (`0051`), the address map without Fast geometry (`0014`, `0036`); blended surfaces drawn after the opaque ones the table put behind them, so a fish or the floor under the water no longer paints over it (`0079`, `KF2_BLENDORDER=0` to compare); Video ▸ Enhancements, a checkbox (the tolerance has no sliders) | on | RENDERING, "Z-buffer", "The assemblers write the depth", "Water was painted over by what lay under it" |
| `Pgxp` | upstream's PGXP as the vertex source (`0034`-`0036`); env only | off | RENDERING, "PGXP has no control in the window" |
| `AmbientOcclusion` | SSAO from painter's-order depth (`0040`), normals from the frame's own geometry redrawn into a G-buffer (`0058`); optional world-space term marching the area's tile grid, so off-screen geometry occludes (`0059`, off); the *SSAO* slider is Off/Low/Medium/High, and the three qualities cap the pass at 1x/2x the game's pixels or runs it at the render scale | on, Medium | RENDERING, "Ambient occlusion", "The normal was the guess", "Occluders the camera cannot see", "What the pass costs" |
| `Reflections` | screen-space reflections on water (`0067`): the normal pass gains a surface buffer (normal, depth, material per pixel) that keeps the translucent water the depth buffer cannot; water found by the fluid slots' VRAM rects, translucent in an averaging blend; the material id is there for lighting later; the pass runs for any of the march, `Murk`, `PlanarWalk` or `RetainedMap`, each on its own switch; the march is no longer a setting (`KF2_SSR=1`, a comparison) | off (not judged) | RENDERING, "Screen-space reflections", "The reflection pass runs for each term on its own" |
| `Murk` | murky water: the reflection pass darkens water by the view ray's run through it to the floor (`WaterMurk`, `0067` amended); needs no reflection on; only a level surface is murked (`KF2_MURK_TILT`), and a model is water only if it is a rigid, flat object (`ModelWater`), so a slime or a crystal in the water's texture is not | off; depth 1886, no sliders (tuning judged) | RENDERING, "Murky water", "Only level water is murked", "A model is water only if it is a sheet of it" |
| `Waves`, `WaterSwell` | water waves: a swell moves the water's interior vertices (the tile walk points each water mesh's bank header at a moved copy in `PrimBuffer.WaveScratch`, so the transforms, clipper and subdivider all read it; rims and vertices shared with other geometry held), and ripples push and shade the water's texture per pixel from a world-space wave field (`0078`, `WaterWaves`); one world clock; six sliders and the `waves` shell verb | on; swell 338/6114, ripples 139/700, shade 0.51, no sliders (tuning judged) | RENDERING, "Water waves" |
| `PlanarWalk` | planar reflections (`0068`): after the object walk, the tile walk runs again and the walk's model submits are replayed from a camera mirrored in the water, into an arena and ordering table `PrimBuffer` keeps past its buffers; drawn at the frame's `DrawOTag` into a planar texture per target, fragments under the water discarded; the reflection pass takes it where a surface lies on the plane, and there its answer is final (an empty texel is the background, never a march); the plane is binned from the water the backend classifies; **it is the reflection**: the world reflections stand down for it, and the mirror walks a cull of its own (`PlanarCull`: the cone without the eye's occlusion flood, and the models there drawn only in the mirror; `KF2_PLANAR_CULL=0`); a *Reflection strength* slider | on; Video ▸ Enhancements, no slider (strength 0.6) | RENDERING, "Planar reflections", "The planar walk is the reflection, with a cull of its own" |
| `RetainedMap`, `RetainedPlanes`, `RetainedModels` | the retained scene (`0072`): the map built into world-space triangles on the GPU from the map data (each corner within 1 px of the GTE's own), the object walk's models captured each frame before culling, drawn at present for the presented frame through `WorldVs` in front of the unchanged `PrimFs`: up to four planes (water, authored reflective floors, ranked on the frame's own camera) mirrored straight into one planar texture, and a camera cubemap with depth marched in world space in place of the screen march; chunk-culled per view; `KF2_RETAINED_PROBE=1` the check, the planes, GPU time and the planar-vs-cubemap agreement | off (not judged); reflections no longer a setting (`KF2_RETAINED=1`), and stand down for the planar walk | RENDERING, "The retained scene: the world kept on the GPU, so a reflection can draw it again" |
| `Remaster.*` (`patches/remaster/`) | authored data from a pack: area identity and fingerprint, the working pack (JSON, watched, undo), a material per tile half written into the packet's depth record for the reflection pass, the editor (Shift+E, pauses the world, picks the faces under a click from the frame's own triangles, or a whole half from the docked map); materials per face of a half or of a mesh area-wide, gated on a hash of the mesh; authored point and spot lights added to the lit colour in `shade8` before the depth cue (`0071`, `RemasterUniforms`; needs per-pixel lighting and Fast geometry; `KF2_REMASTER_LIGHTS=0`), placed at the eye or on a click, dragged over the picture; materials on a model wherever the area draws it (kind and id, picked from the frame), with roughness (a blur of the reflection, from a mip chain), metalness, a highlight, an occlusion strength, and a glow, added over the texture by default, pulsing if asked, and giving off a light of its own that leaves its material alone, from a 256-id table on the GPU (`0067`, `0071` amended); the `edit`/`select`/`set`/`pack`/`remaster`/`light` shell verbs; upstream's texture packs on the port's path (`0073`): a key per uploaded image, filtered by the port's slider, and the `textures` census verb (`KF2_TEXCENSUS=1`); materials by texture, in every area (`TextureKeys`, `remaster/textures.json`, the least specific rule; a scrolling texture keyed on its source image), set from the editor's pick or `set texture material NAME`; `snap` hashes the presented picture (`0069`); the game's own light records (back colour, the three lights, the fog word) overridden after stage 1's copy, gated on each record's hash (`Atmosphere`, `atmosphere.json`, the `atmos` verb, `KF2_REMASTER_ATMOS=0`), once a census (`KF2_LIGHTCENSUS=1`) showed only the renderer reads them, and the area's *Darkness* slider over them (`"record": "all"`, `"darkness"`: a scale on records 0-63's back colour and light colours, computed from the source every pass; `atmos darkness`); shadows from the authored lights (`0077`): a depth cubemap per light, up to four, drawn from the retained map with the light at its centre, again only when the light or the map changes, sampled in `authored()` along the receiving surface (`KF2_REMASTER_SHADOWS=0`, `light shadows tune`); creatures and objects cast too, the frame's captured models drawn over a copy of the map's cubemap, again only while one in reach moves, a blended face with its opaque texels, effects not at all (`KF2_REMASTER_SHADOW_MODELS=0`, `light shadows models off`); the area's fog colour and curve (`fogColour`, `fogPower`, `fogMax` on the `"all"` entry), added past the texture by the packet's own depth-cue weight so a surface fades into the colour instead of black (`0074`; needs per-pixel lighting and Fast geometry; a face fogged to black keeps its record while one is set), and a `sky` the game's own background clear draws, the fog's colour by default (a pre and post on `PutDrawEnv`; `atmos fog|curve|sky`); tile edits (`Level`, `level.json`: a half's mesh, height, collision bits, shape, light record and flood bit, each field owning only its bits; applied whole per area and per half behind the fingerprint, never over a half the game rewrote, every write put back only if it still reads as written; the `level` verb), behind a switch of their own because they change gameplay (`KF2_REMASTER_LEVEL=1`); the rewrite census of halves the game rewrites itself (`TileRewrites`, `dump/GAME/census/rewrites.json`); `savecheck`, which proves the tile block never reaches a save; the editor's free camera (`EditorCamera`: `Stage13.ViewOverride` from the player's eye, right mouse to look, WASD/Q/E to fly, the arm left out; the `camera` verb); the compatibility report (`Compat`: per area, each document matches, differs or is unseen against a census of fingerprints this disc has loaded, `dump/GAME/census/areas.json`, with what resolved there last; `pack report`) and the export (`pack export`, upstream's zip layout, into `exports/`); props (`Props`, `props.json`: an object model the area already has, placed, turned and scaled, as an object record of the port's own above 2 MB that the C# object walk submits after the game's, so it is culled, lit and drawn by the game's own path; no collision, never saved; the `prop` verb, `KF2_REMASTER_PROPS=0`); Video ▸ Enhancements ▸ Remaster packs, with the packs listed under their own heading | off; nothing authored; level edits off | REMASTER, "Phase 1, the first slice", "Phase 1, the second slice", "Faces, picked from the frame", "Phase 2, the first slice", "Phase 3, the first slice", "The glow is a light source", "Phase 3, the second slice", "Phase 4, the first slice", "Phase 4, the second slice", "Metal is a tinted mirror", "The light records are read only by the renderer", "Phase 5, the first slice", "The area's darkness", "Shadows, the first slice", "Shadows, the second slice", "Phase 5, the second slice", "Phase 6, the first slice", "Phase 7, the first slice", "Phase 8, the first slice" |
| `Anisotropic` | post-CLUT footprint supersampling (`0041`), every tap held inside the polygon's texture rectangle; one *Texture filtering* slider, Off / Trilinear / 2x-16x, every position past Off with mipmaps: minified textures decoded into an atlas with a mip chain each (`0060`, `KF2_MIPMAPS`) | 16x, mipmaps on | RENDERING, "Anisotropic filtering", "Mipmaps where the texture is decoded" |
| `PerPixelLighting` | the depth cue and the models' light evaluated per pixel from what `PolyAssembler` recorded per packet (`0048`) | on | RENDERING, "Per-pixel lighting" |
| `EvenFog` | clipped map tiles refogged on the tiles' curve instead of `func_800302E8`'s `IR0 >> 1`, and each tile vertex's fog blended between the light records of the tiles around it (hooks `func_80031950`; `0049`); and the records' colour matrix and back colour the same way; one *Even fog and lighting* checkbox, dimmed without Fast geometry (`KF2_EVENFOG_BLEND=0`, `KF2_EVENLIGHT=0` drop a part); stands down under verify | on | RENDERING, "A clipped tile is fogged at half, and that is the block on the floor", "Fog changes at a tile edge" |
| `Fog` | the fog, one choice of three: at the corners (the game's), by pixel depth (the GPU world's map, `RetainedScene.MainFogFromZ`), or by distance from the eye (`0100`, `GteDepth.RadialFog`: the GTE cues by SZ, so a wide picture's sides were fogged as if nearer and the fog moved as the view turned); `KF2_FOG`, `KF2_GPUWORLD_FOGZ=0` the old spelling of corners; Video ▸ Enhancements ▸ *Fog*, PORT SETTINGS ▸ WORLD ▸ FOG | by distance (not judged) | RENDERING, "Fog by distance from the eye" |
| `NoDither`, `TrueColor` | one *Shading* slider: Dither / None / Smooth (24-bit, `0021`) | Smooth | PATCHES_AND_MODS, "Two shading checkboxes were one question asked twice" |
| `Widescreen`, `CullCone` | aspect ratio; widened view cone and screen tints follow it; *HUD at the screen edges* moves the HUD records' X around the builder `func_80031D5C` | 16:9; HUD off (not judged) | WIDESCREEN, "Widescreen", "The cull the margin runs into", "The HUD is moved by its records", "The HUD at the edges is a setting again" |
| `RenderDistance` | cells added past the game's 24x24 visibility window, out to a slider's distance (at most 15 tiles, the s16 a tile is placed with): the game's flood continued outward on the eye's level from what it lit, walked after the game's own through the far assembler, answered by the object walk's queries, and a far packet placed at the table's last slot but one instead of dropped; the fog is the game's | off (not judged) | WIDESCREEN, "Render distance: the game's flood carried past its window" |
| `ReflectionReach` | what the retained scene's reflections may show is the frame's halves grown by `KF2_REFLECT_REACH` cells on their own level, held 0.75 s and dither-faded (`0072` amended), so what the mirror sees and the eye's flood culled (a cavern round a cliff, a creature) stops popping into the water; the object walk's queries let models in them through; no longer a setting, and not read by the planar walk | off (not judged) | RENDERING, "The reflections see past the camera's cull" |
| `GpuWorld` | the map's opaque faces drawn on the GPU from the retained scene's static mesh into the frame, at the table walk's slot 1, gated to the halves the tile walk visited; a half is then not assembled (`0085`); the map lit and fogged in the vertex shader from the area's 64 light records, EvenFog's blends and all, so a record the game rewrites is an upload and not a rebuild (`KF2_GPUWORLD_RECORDS=0` to compare, `KF2_GPUWORLD_RECORDCHECK=1` the check); its water is drawn by the backend too, whole faces sorted by the table's key and put among 0079's held packets where their packets would have gone, with the swell and the ripples (a half with a subtractive face stays on the packets; `KF2_GPUWORLD_WATER=0` puts the water back on them, `PolyAssembler.BlendedOnly`); follows sub-pixel, per-pixel lighting and the crosshatch, stands down without Fast geometry, the Z-buffer or perspective correction; the map drawn first into the occlusion's normals and the surface buffer, the rest tested there against the frame's depth (`KF2_GPUWORLD_SURFACES=0` to compare); fogged at each pixel's depth (`KF2_GPUWORLD_FOGZ=0`); stands down while a texture pack is loaded; the sky (the objects of kind `0xF0`) drawn from its meshes before the map, in painter's order (`KF2_GPUWORLD_SKY=0`); every blend mode, subtractive too, in one key order with the water; the object walk's opaque models drawn after the map from their posed corners, taken off the lit and clipped assemblers by those assemblers' own tests, lit per pixel from light dots with each run's BK and LCM (`RetainedModels.CaptureMain`; blended faces, effects, billboards and the arm stay on the packets; per-pixel lighting needed; `KF2_GPUWORLD_MODELS=0` to compare); the lit models placed in the world drawn from meshes kept on the GPU, an instance a frame (posed vertices, placement, light), the lit assembler's cull in the vertex shader, and a model with no blended face running neither the transform nor the assembler, nor the mirror's replay (`RetainedModels.TryInstance`; `KF2_GPUWORLD_MESHES=0` to compare, `KF2_GPUWORLD_MESHCHECK=1` the check); their vertices kept on the GPU too, a rigid model's as they are and an animated one's keyframe and deltas, blended in the vertex shader by the instance's weight, so no instance uploads vertices a frame (`MoPose`; `KF2_GPUWORLD_POSES=0` to compare, `KF2_GPUWORLD_POSECHECK=1` the check); the first-person arm drawn from its mesh in the game's painter's order, a run of faces per key where the table walk reached its packets, placed by the GTE's integer transform, the far plane left under it (`RetainedModels.TryArm`; `KF2_GPUWORLD_ARM=0` to compare); a model corner nearer than H/2 placed where the GTE's saturated divide puts it; the planar walk's mirror drawn the same way into the planar texture, from the mirrored camera with its own cull: its opaque map, its water and the models its replay takes off the packets, clipped and fogged level as a capture's packets are (`KF2_GPUWORLD_MIRROR=0` to compare); the objects near the camera (the clipped map assembler's) drawn from their meshes too, with that assembler's tests in the vertex shader, so stage 13's object walk draws 0 packets in every area at arrival (`KF2_GPUWORLD_TILE=0` to compare); the mirror's blended model faces keyed from the mirrored camera and merged with its water in the table's order (`KF2_GPUWORLD_MIRRORBLEND=0`); a model's blended faces in the normal and surface buffers as their packets were: a solid door as opaque, an authored material or the water's texture as a blended surface (`KF2_GPUWORLD_BLENDSURFACES=0`); the cell walk notes a half the GPU draws whole without calling the half routine (`KF2_GPUWORLD_CELL=0`); `0051`'s slope term capped at one game pixel's world width (`0087`, `KF2_GPUWORLD_DEPTHCAP=0` to compare, `KF2_GPUWORLD_TOLERANCE_PROBE=1` and `gpuworld tolerance` the probe), so a face seen edge-on no longer draws over what stands in front of it; `KF2_GPUWORLD_CENSUS=1` counts what 3D the game's code still builds, by context (only the menu's item preview); Video ▸ Frame pacing ▸ *GPU geometry*, `KF2_GPUWORLD`, the `gpuworld` verb | on (measured; not all judged by eye) | GPU_RENDERER, "Step 1, the first slice", "Step 1, the second slice", "Step 2, the first slice", "Step 2, the second slice", "Step 2, the third slice", "Step 3, the first slice", "Step 3, the second slice", "Step 3, the third slice", "Step 3, the fourth slice", "Step 3, the fifth slice", "Step 3, the sixth slice", "Step 3, the seventh slice", "Step 3, the eighth slice", "Step 3, the ninth slice", "Step 4, the fallback census", "Step 5, the first slice", "Walls that wobbled up close at hard angles" |
| `ProjectileLights` | magic projectiles give off light in their own colour: an effect whose faces the game adds to the picture (blend 1 or 3), coloured by the mean of its texels through the CLUT; opaque lit effects (arrows, rocks) never; a trail merged into one, at most six a frame; through `Remaster.Lights`, with or without the remaster (`Lights.Bare`); a light whose effect is gone fades out over 0.35 s; Video ▸ Enhancements ▸ *Glowing spells*, setup's *Lighting and fog* (Modern), `KF2_PROJECTILE_LIGHTS`, the `projectiles` verb | on (judged; the fade not) | RENDERING, "Magic projectiles give off light" |
| `EnhancementDistance` | past a view depth, the game's own look: corner colours, no authored light, one texel, no ripple, occlusion or reflection, faded over a tile (`0083`) | off (not judged) | RENDERING, "The enhancement distance: past it, the game's own look" |
| `PrimBuffer` | the frame's primitive buffers moved above 2 MB into 4 MB of guest RAM, 4× as large, so a wide view no longer runs out and drops geometry (`0056`); `KF2_PRIMBUF=1` is the comparison, `KF2_PRIMBUF_PROBE=1` the measurement | on | WIDESCREEN, "The primitive buffer ran out" |
| `SettingsPage`, `PortSettings` | PORT SETTINGS in place of RETURN at the bottom of the in-game menu: six pages (DISPLAY, GRAPHICS, WORLD, GAMEPLAY, CONTROLS, SOUND; what earns a row is SETTINGS, "What the page is for") drawn with the menu's own routines, changes staged until SAVE CHANGES, written only through `SettingsStore` on the host thread; `KF2_SETTINGSPAGE_PROBE=1`, the `settings` shell verb | on (measured, not judged) | SETTINGS, "The page"; GAME_INTERNALS, "RETURN is the chooser's last item, and the port settings page takes it" |
| `Setup` | the first-run setup (Verdite Core's `Onboarding`, `config/onboarding.json`): the launcher shows it while the game builds, and the game starts when both are done; a display step (window, render resolution, aspect, frame rate), then a page per category (Controls, Presentation, Gameplay), Original / Balanced / Modern for each group, then a turning page (tank or twin stick, and the enemy turn speed slider, `pages` in the spec); RUN SETUP, the DISPLAY page's last row, runs it again and applies live where a setter exists; due while `verdite.setup` is below the spec's version (`VERDITE2_SETUP=1` forces it); the `setup` shell verb | on (measured, not judged) | SETTINGS, "Onboarding" |
| `FocusPause` | Verdite Core's: the window losing focus holds the stage gate shut (`FramePacing.PauseWhen`) and mutes the SPU; off for a run `KF2_SHELL`/`KF2_AUTOPAD`/`KF2_AUTOSTART`/`KF2_AGENT` drives; Gameplay ▸ *Pause in the background*, `KF2_FOCUS_PAUSE` | on (measured focused; focus loss not seen) | PATCHES_AND_MODS, "Pausing when the window loses focus" |
| `AutoReload` | reload the last save on death, fixed 2 s delay | on | PATCHES_AND_MODS, "Auto reload" |
| `GearCompare`, `MenuDraw` | the equip and buy prompts show every stat the item would change, now and after (`func_800244CC` run on the candidate and put back), drawn with the status screen's menu primitives rewritten in C# (`KF2_GEARCOMPARE=verify` diffs them against the recompiled routines); was `mods/gearcompare` | on | PATCHES_AND_MODS, "Comparing gear on the equip prompt"; GAME_INTERNALS, "The menu's primitives are `POLY_FT4`s out of a cursor, and the cursor is mirrored" |
| `EnemyTurn` | a multiplier on a creature's turn at the player (pre-hook on `func_8003C8D8`: speed x k, acceleration x k²; a wander keeps the game's pace), up to x8; an engaged creature standing still is turned at the player each tick through the same routine (a post on stage 4; *Track you while standing*); Gameplay ▸ *Enemy turn speed*, PORT SETTINGS ▸ GAMEPLAY ▸ ENEMY TURN, `KF2_ENEMYTURN`, `KF2_ENEMYTURN_TRACK` | x1, tracking on (measured, not judged) | PATCHES_AND_MODS, "Enemies turn faster"; GAME_INTERNALS, "The creature turn is one routine" |
| `mods/map` (`kf2.map`, a mod, not a patch) | the dynamic map, off until enabled in the Mods panel: full-screen map (`G`, or the pad's R3), docked readout (`Shift+G`), minimap (`N`), fog of war, markers; full map pauses the world; the shell's `map` verb (through `AgentServer.FastVerbs`) | off until enabled; fog on; minimap, markers off | PATCHES_AND_MODS, "A dynamic map", "What the Map page is down to" |
| `MapKey` | M or the touchpad opens the game's own map item (PIRATE'S, MINER'S or NECRON'S MAP) through the ITEM page's viewer `func_80019B54`, wrapped in the menu's enter and leave; a post on stage 3 on a tick; refused with no character, dead, an overlay load in the last second, or a message box | on (measured; picture not judged) | GAME_INTERNALS, "The maps" |
| `Analog` | twin-stick control; *Controls* 3D is one stick, as tank controls (`KF2_TANK`, `kf2.controls`) | on; 2D | INPUT, "Analog twin-stick control", "2D and 3D controls" |
| `Mouse` | mouse look (Verdite Core's, this game's values in `Analog.MouseValues`), spent inside `Analog.BeforeLook`; the view shows motion the tick has not spent yet (`FrameSmoothing.MouseLead`); mouse look and the lead always on, not settings (`KF2_MOUSE=0`, `KF2_MOUSE_LEAD=0` the comparisons); *Mouse aim* (`MouseAim`, `MouseAimOverlay`): the mouse moves a reticle and the player turns toward it at the D-pad's rate and ease (`KF2_MOUSE_AIM`); a small aim held still is carried out (settle), another device's axis is yielded, the turn is measured, with aim sensitivity (`KF2_MOUSE_AIM_SENS`), marks only while aiming (`KF2_MOUSE_AIM_MARKS`) and a *View* of turns with you, follows the mouse (`KF2_MOUSE_AIM_HEAD`) or holds still (`KF2_MOUSE_AIM_HOLD`), both off and not judged; the ease optional (`KF2_MOUSE_AIM_EASE`) | on; lead always on (judged); aim no longer a setting (env only) | INPUT, "Mouse look", "The mouse leads the tick", "Mouse aim: the hand points, the player turns", "Seven changes after the first play" |
| `MenuMouse` | point-and-click in the in-game menus; always on, not a setting (`KF2_MENUMOUSE=0` the comparison) | on | INPUT, "The menu pointer" |
| `Rumble`, `GyroAim` | the bow read off `func_800271D0` once a tick (`Bow`): the pad rumbles as it is drawn and loosed (`0104`, `0105`), and a drawn bow aims with the pad's gyroscope (`0102`); PORT SETTINGS > CONTROLS > GYRO, RUMBLE | on (not felt) | INPUT, "Rumble and gyro aim with a drawn bow"; GAME_INTERNALS, "The bow and the arbalest" |
| `ItemTurn` | a held item turned by hand: the one the pickup `func_8004831C` holds up (mouse look, the right stick, the gyro; the turn added at draw time in `ModelWalk.Ordinary`, the yaw on the record's and the tilt about the camera's right; the spin's step taken back on a pre at `Stage13.HookOrder.World`) and the menu's item preview (a left drag on the model, MenuMouse asking `Grab` first, the stick, the gyro; a pre/post on `func_8002156C`); the spin holds while turning and resumes 1.5 s after; Gameplay ▸ *Turn a held item*, the GYRO row with the bow's, `KF2_ITEMTURN`, `KF2_ITEMTURN_GYRO` | on (stick measured on both; mouse, drag and gyro not tried, not judged) | INPUT, "Turning a picked-up item", "Turning the item preview" |
| `ItemEye` | a picked-up item held at the centre of the view: the pickup's targets moved by the camera's eye less the one `func_80046C64` assumes (the head bob, as Verdite3's), and at draw time the record moved for the submit so the middle of the mesh's box is on the view's axis at the game's distance, blended in over the fly-in and out over the fly-back | on (measured, not judged) | INPUT, "The item held up at the eye" |
| `KeyLayout` | the port's WASD layout; the table and store here, the mechanism Verdite Core's `KeyLayoutApply` | on | INPUT, "The keyboard layout" |
| `CardIcon`, `DesktopEntry` | the window icon is the game's own memory-card icon, read off the player's disc at boot; the shipped orb is the fallback. On Linux the same pixels go into the icon theme under the app id, which is the only way a Wayland compositor can show one (`0061`) | on | PACKAGING, "The icon comes off the disc", "Wayland takes the icon from the desktop entry" |
| `EndingHold`, `BootExe` | hold "The End", any button returns to the title | on | RUNTIME, "The ending screen" |
| `HitGuard` | fences the final-boss hit-path fault | on | TODO, "The crash on the final boss's last hit" |
| `WallGuard` | replaces the wall slide `func_80028080`: if the player already stands in map collision (bit `0x2`) its retries end in a loop with no call that never exits, and stage 3 calls it every tick, so the game froze; the guard tests the spot first and nudges the player to the nearest free one | on | GAME_INTERNALS, "The wall slide never leaves a wall it starts in" |
| `AudioQuality`, `AudioProbe` | voice interpolation and reverb (`0043`); probe and WAV dump | sinc, enhanced reverb | AUDIO, "Voice interpolation", "An enhanced reverb sized from the game's registers" |
| `PositionalAudio` | 3D sound effects re-aimed every frame, for speakers or headphones (`0044`) | headphones | AUDIO, "Positional audio" |

Features ship **off** when the mechanism is measured but the picture has never been
judged by eye; say which of the two a change has when you write it up.

### Rules that bite when you change a patch

- **Attach through `HookAttach` (`tools/verdite-core/src/HookAttach.cs`), and read back what committed.**
  `AddPre`/`AddPost` only queue a delegate; the detour is made later in
  `HookManager.Commit`, which fails per function without throwing (`0027`), so
  counting `Add*` returns claims what was *queued*. Use `HookManager.IsCommitted`
  (`0028`), retry until it holds, and never latch `attached = true` before
  `Attach()` — an exception thrown inside an `OverlayLoadedEvent` listener is
  swallowed by `Event.Dispatch` into one stderr line. See "A registration is not
  a hook" in `docs/PATCHES_AND_MODS.md`.
- **The frame boundary is a single point of failure and it fails open.** It is a
  `DrawOTag` that follows a `VSync` call; `_tickThisFrame` starts `true`, so a lost
  boundary uncaps the picture *and* runs the whole world at the render rate,
  silently. The stage gate's 500 ms watchdog (`FallbackTick`) is what saves it; a
  hold keyed on the frame (like `SpriteAnim`) fails *closed* and needs the same
  watchdog. Check any pacing change with `KF2_FPS=144 KF2_FPS_PROBE=1`: 144.0 fps
  drawn at 15.0 ticks/s.
- **A gated stage must not draw** (`scripts/check_gate.py`; its `KNOWN` holds the
  two recorded exceptions). Something stepped inside a drawing function's own body
  cannot be gated and needs a hold/restore pair instead.
- **A tick is a frame identity, not a flag**: use `FramePacing.FirstWalkOfTick`,
  not `TickedThisFrame`, when something must happen once per tick inside stage 13.
- **`Widescreen` owns the one `Replace` of `DrawOTag`**, so every other `DrawOTag`
  hook must be a pre or a post, and a replacement must pass the source address to
  `WriteGp0` or perspective correction silently turns off.
- **A hook whose place among the others matters declares it** (`order` on
  `AddPre`/`AddPost`, `0070`), never by where its `Install()` sits in `Program.cs`.
  On stage 13 the orders are `Stage13.HookOrder`: `LoopPacing`'s redraw post is
  `Redraw`, after every smoother's restore.
- **A pass that draws into a table of the port's own goes through `ScenePass`**, so
  it puts back everything any pass moves.
- **A liveness test is the renderer's, not the owning stage's**: an object is drawn
  when `u16[+0x6] != 0xFF`, a creature when `u8[+0x9] == 1`.
- **Settings**: a page registers against a runtime section with
  `PatchSettings.Register("display", ...)` (or `RegisterSlot`, `0013`); pages
  sharing a `Title` share a heading and need adjacent `IPatchPage.Order`s or the
  heading is drawn twice. `Extend` has **no un-extend**, so nothing may register
  against `"input"` — `patches/settings/InputSection.cs` owns that pane outright
  and `Register` refuses the id. `gameplay` is the port's own section
  (`GameplaySection.cs`); **a new localisation key must supply all three of the
  runtime's languages** (en, pt-BR, es-419), and **a new sidebar entry must not
  collide with an existing one in any of them** (`Controles` already exists in two).
  See "Patch settings" in `docs/PATCHES_AND_MODS.md` and "The Input pane is the
  port's" in `docs/INPUT.md`.
- **A control that stops being a setting stops reading its saved key**, so nobody
  is stranded by a value with no control to show it; the env var is the
  comparison.
- **Changing the shipped keyboard layout** means bumping `KeyLayout.Version` and
  recording the old layout in `Superseded`, or an existing config reads as
  customised and is never corrected.
- **Anything the runtime refreshes only at `VSync` is invisible to a game that
  stops calling `VSync`**, and **a handler that never runs loses only the work
  inside it** — both failure modes are silent. See "Two general shapes worth
  keeping" in `docs/RUNTIME.md`.

### Regenerating function maps

Only needed if the sweep is wrong or a new executable is added:

```bash
RC="dotnet run --project tools/RecompOne/RecompOne.Recompiler -c Release --no-build --"
$RC --generate-function-file -linear-sweep -disc disc/KingsField2.cue \
    -file OPEN.EXE -base 80011000 -skip 800 -out config/funcmaps/open.json
```

An `unmapped call: 0x…` has three causes, so check which it is before reaching
for a script. **Is the address a function start the map is missing, an address
inside a function that already exists, or a few instructions past a mapped start
that has no prologue?**

- Missing start — `scripts/add_call_targets.py` recovers it by harvesting `jal`
  targets and splicing them into the map. Only sites already inside a known
  function are harvested; a `.data` word that decodes as `jal` is not a call.
- Interior address — the sweep split one real function in two, because it ends a
  function at any `jr`/`j` plus delay slot and PSY-Q emits both *inside* a
  function (a `jr` through a switch table, a `j` to a shared epilogue). A
  conditional branch that crosses a boundary proves it, since MIPS branches never
  leave their function. `scripts/merge_branch_spans.py` merges on that proof (and
  on jump-table entries), checks nothing `jal`s a start it swallowed, and is
  idempotent — run it after any sweep. See "The sweep splits a switch" in
  `docs/RECOMPILATION.md`.
- False split from data — `add_call_targets.py` once treated a table word as
  `jal` and cut a real function. The crash address sits just past a mapped start
  that has no prologue and that nothing in code `jal`s; the previous function
  falls through into it. `merge_branch_spans.py` cannot see a fallthrough, so
  rejoin by hand (the previous start's size should reach the next real function).
  See "add_call_targets can split a function" in `docs/RECOMPILATION.md`. The script no longer
  harvests sites outside a known function, so re-running it will not re-cut.

```bash
python3 scripts/merge_branch_spans.py --dry-run   # all overlays, writes nothing
python3 scripts/merge_branch_spans.py fdat17
```

The FDAT overlays deliberately use `"skip": 0` so the overlay covers the module
header: the modules' switch jump tables live in it, past the 32 dispatch slots,
and the recompiler reads a jump table out of the overlay's own bytes. Raising
`skip` past them makes every `jr` through a table dispatch to nothing at run time.

## Architecture

```
config/kf2.json          recompiler config: overlays, funcMaps, stubs[], patches[]
config/funcmaps/*.json   swept function maps (address/name/size; size is mandatory)
patches/                 hand-written C# replacing recompiled functions
mods/<id>/               runtime-loaded mods (mod.json + C#, Roslyn-compiled)
mcp/                     stdio MCP server exposing the KF2_SHELL command channel as tools to MCP hosts
Verdite2.Launcher/       the SHIPPED executable: Verdite Core's launcher under this
                         port's names (one Program.cs and a csproj importing
                         tools/verdite-core/launcher/Launcher.targets); builds with
                         no disc, and makes the game at first run from the player's
                         own image. See docs/PACKAGING.md
packaging/               package.env (this port's names for Verdite Core's packaging),
                         the icons and desktop entry, and wrappers of the core scripts
tools/RecompOne/patches/*.patch  the record of the port's changes to RecompOne
tools/verdite-core/       Verdite Core, the game-agnostic code shared with Verdite3 (a subtree)
config/verdite.json      this game's values for Verdite Core's scripts
generated/               recompiler output (gitignored — derived from copyrighted disc data)
scripts/*.py             disc inspection, address-hunting, and the rate tooling:
                         merge_sdk_names (write the PSY-Q names a signature
                         match found into config/funcmaps/, refusing the ones
                         SdkPatches would bind -- see "Merging the SDK names" in
                         docs/RECOMPILATION.md),
                         shader_probe.c (run a real prim fragment shader
                         headless over a known texture and read the pixels back:
                         the only thing here that can say a shader change moved a
                         pixel, since the port is CPU-bound and frame rate cannot),
                         rate_census (which words move at the render rate),
                         find_writers (which code moves them), rate_matrix (did
                         the fix work), check_gate (does the gate obey its rule).
                         kf2run/callgraph/kf2model are their shared halves.
                         See "Finding the rate defects" in docs/DEVELOPMENT.md
Program.cs               hand-owned entry point; calls Entry.Run(PSMemory, cuePath)
```

The disc holds a 4 KiB boot stub (`SLUS_001.58`, named by SYSTEM.CNF) plus three
real executables — `OPEN.EXE` (title/intro), `GAME.EXE`, `END.EXE` — which **all
load at `0x80011000`** and are mutually exclusive. They are declared as
**overlays** in `config/kf2.json`. Left alone the recompiler would only find the
2 KiB stub. Per-area logic is more MIPS loaded at run time: the `fdat` modules.

Because the three overlays share an address range, *every* address-based config
entry must name its overlay explicitly. Prefer a named overlay over `"*"`.

### The core problem: SDK functions must be mapped by address

The recompiler's `SdkPatches.cs` binds PSY-Q calls to the runtime's HLE by exact
**function name**. A linear sweep names everything `func_800xxxxx`, so it always
reports `applied 0 reimplementations`. Every SDK entry point therefore has to be
mapped by address in `patches[]`:

```json
{ "overlay": "open", "address": "0x80016078",
  "target": "RecompOne.Runtime.Sdk.LibGpu.DrawOTag", "mode": "replace" }
```

63 such patches exist today (`libetc`, `libcd`, four `libgpu` entry points, six
`libcdstream`, libapi's `DMACallback`). `libpad` is the notable gap — and a
signature match confirms it is not a gap at all: the game links no `libpad`, which
is consistent with it reading `PAD_dr` through `BiosB` instead.

**997 non-binding functions now carry their real PSY-Q names** (`rsin`, `rcos`,
`SsSetMVol`, `RotTransPers`...), from upstream's signature bank via
`scripts/merge_sdk_names.py`. That is legibility only: the script **refuses every
name `SdkPatches` binds**, reading that list out of the patched checkout rather
than copying it, so all 63 entries above still do the binding and the recompiler
still reports `applied 63 patches, 0 reimplementations`. See "Merging the SDK
names" in `docs/RECOMPILATION.md`. Only map a function the runtime actually
implements — check `tools/RecompOne/RecompOne.Runtime/sdk/Lib*.cs` first; unmapped
library routines run fine as recompiled MIPS because `PSMemory` traps their
register writes.

`mode` is `"replace"`, `"pre"` or `"post"`. **Use config patches for `replace`
only** — binding an SDK entry point, which has to happen before any mod could
load. For anything else, do not add a config entry: RecompOne's `HookManager`
detours a recompiled function by address at run time, so a hook needs neither a
config entry nor a recompile. See "Mods" in `docs/PATCHES_AND_MODS.md`; `patches/FramePacing.cs`
is the in-project example and `mods/` holds the loadable ones.

**Generated code is one class per overlay** — `Recompiled.KingsField2_game`,
`_open`, `_end`, `_fdat`, `_main` — because CoreCLR caps a class at 65535 methods.
Code that calls a recompiled function directly carries a one-line
`using KingsField2 = Recompiled.KingsField2_game;` alias (sixteen call sites, in
`patches/AreaWarp.cs`, `AutoReload.cs`, `CullGrid.cs`, `MenuMouse.cs` and
`mods/kf2debug/Noclip.cs`, `Attributes.cs`), so do not also name a namespace
`KingsField2`. A direct call is *not* the same call as `Dispatcher.Call`, which
goes through `HookManager`.

### Identifying an address: the techniques that work

In rough order of payoff (full reasoning and worked examples in
`docs/RECOMPILATION.md`):

1. **The overlay delta.** The three executables are three links of the same
   libraries, laid out at a constant offset *per translation unit* (not per
   library — `libcd`'s `cdio` and `stream` differ from each other). Identify once,
   derive the other two. `scripts/match_overlays.py` does this mechanically
   against a relocation-insensitive normal form; validate any new delta by
   re-deriving already-known addresses with it.
2. **Data-side search for hardware addresses.** PSY-Q reaches I/O through pointer
   tables in `.data`, never through literals in code — there are five
   `lui …, 0x1F80` instructions in all of `OPEN.EXE` and none is the GPU. Search
   the *data* for `0x1F801810` (GPU) or `0x1F801800` (CD) to find the table; the
   functions loading through it are the library.
3. **Diagnostic strings.** `func_80014C0C` is the `printf` thunk (BIOS A(3Fh), 69
   call sites). PSY-Q error text (`VSync: timeout`, `CdInit: Init failed`,
   `GPU timeout:QUE=…`) names the calling function for free.
4. **Struct offsets as evidence.** Two independent offsets agreeing (a `DR_ENV`
   packet at `env+0x1C` *and* a `0x5C`-byte copy) turns a guess into an ID.
5. **Indirect-call tracing.** Public entry points can have zero `jal` references
   because PSY-Q dispatches through driver tables filled in at init (`libgpu`'s
   15-slot table, libapi's `DMACallback`). Log the address in `Dispatcher.Call`
   rather than searching statically.

### Two traps

**Number bases differ between the config and the CLI.** In `config/kf2.json`,
`base` is a hex *string* while `size`/`skip`/`offset`/`lba` are decimal numbers;
on the `--generate-function-file` CLI, `-size`/`-skip`/`-offset` are hex.
`"skip": 2048` in the config is `-skip 800` on the command line.

**Overlays are read as raw bytes.** `ResolveOverlay` does not parse the PS-X EXE
header, so every `.EXE` overlay needs `"skip": 2048` to step past the 0x800-byte
header, and `base` must be the header's real text address (read it with
`scripts/extract_file.py --header-only`). The *boot* executable is different — it
goes through `Psx/Parser.cs`, which strips the header itself.

### csproj

`generated/` and `patches/` are picked up by the SDK's default globs — adding an
explicit `Compile Include` causes NETSDK1022. `tools/**` must stay explicitly
removed, or the build compiles RecompOne's own sources and its `obj/`
AssemblyInfo files (CS0579); `mods/**`, `mcp/**` and `Verdite2.Launcher/` are
removed for the same reason.

## The RecompOne checkout

**`tools/RecompOne/` is a `git subtree` (taken with `--squash`) of the
standalone fork `Voicedrew11/verdite-recompone` (`main`): its sources are tracked
here, so a fresh clone builds with nothing fetched, and an edit inside it is a
change to this repository like any other.** `tools/RecompOne/patches/*.patch` (the fork's own, since Phase 2) are kept
as the record of what the port changed and why, and the numbers (`0001`-`0110`)
are how the source refers to each change, but they are **no longer replayed**.
The merge base is `tools/RecompOne/UPSTREAM` (currently `d81dec8`); the fork's
history descends from upstream, so a harvest is an ordinary merge, made in a
working clone of the fork.

- **Shared-subtree edits go in their own commits.** A commit that touches
  `tools/RecompOne/` touches nothing else (`--push-fork` refuses a mixed one), it
  is pushed to the fork soon after with `--push-fork`, and this repo's copy must
  always equal some commit of the fork. Upstream harvests happen in the fork, not
  here; see `docs/RECOMPONE_FORK.md`.
- **`tools/verdite-core/` is the second subtree**, of `Voicedrew11/verdite-core`:
  the game-agnostic code the Verdite games share, under the same rules
  (`--pull-core`, `--push-core`). Nothing in it may know this game; it reads
  this game's values from `config/verdite.json`. The bring-up scripts
  (`inspect_disc`, `extract_file`, `add_call_targets`, `merge_branch_spans`,
  `merge_sdk_names`) live there, and `scripts/` keeps a wrapper of each name, so
  the commands in this file are unchanged. Its C# (`src/`, namespace
  `Verdite.Core`) compiles into this assembly as source, through a `Compile
  Include` after the `tools/**` remove and a global using (stated twice: the
  csproj and core's `launcher/Build/GameCompile.cs`), and the launcher ships it
  under `content/src/verdite-core/`; `Program.cs` sets the game's tag first
  (`Game.Configure(tag: "KF2")`). See `docs/SHARING.md`.
- **Three changes force a recompile**: `0004`, `0035` and `0037`. Everything else
  is runtime-only.
- **The acceptance test for a merge** is `open → game → fdat02 → fdat05`, slot 2
  restored at hp 46/86 in area 1, 144.0 fps drawn at 15.0 ticks/s, every hook
  attached — **paired with `KF2_PRESENT_PROBE=1`**, because every number in that
  run was once still true with a completely black window. The last merge also
  silently broke the vertex map (`KF2_PERSPECTIVE_PROBE=1` reading `0 caught/s`)
  and the widescreen margin's clear; check both.

**Nothing goes upstream. Not a pull request, and not an issue either.** Upstream
rejects AI-authored pull requests outright, and this project does not file
issues against it: a defect found here is recorded in `docs/` and fixed in the
vendored tree, which is the whole point of vendoring it. If the user wants
something reported upstream they will write it themselves.

Read `docs/RECOMPONE_FORK.md` before merging from upstream, and grep
`tools/RecompOne/docs/RECOMPONE_PATCHES.md` for a patch number (`0047`) before changing that
patch's code or amending it. Neither is imported, for size.

## Shipping it

**The port cannot ship a playable binary.** `generated/` is a translation of
FromSoftware's code, so the assembly that plays the game has to be built on the
machine of somebody who owns the disc. The release ships every **input** —
`config/`, `patches/**`, `Program.cs`, the RecompOne subtree — and makes the
output at first run. That is also a correctness win: the generated dispatch tables
bake **absolute LBAs from one mastering**, so a prebuilt binary would silently
fail to load area modules on a differently mastered dump.

- **`Verdite2.Launcher/` is the shipped executable and compiles neither
  `generated/` nor `patches/`** — it carries them as payload under `content/` and
  compiles them at first run. Its code is Verdite Core's `launcher/`, shared with
  Verdite3; `Verdite2.Launcher/Program.cs` is the one record of this port's names,
  serial, wrong discs and update repository, and a change to the shell is a
  `tools/verdite-core` commit like any other. Anything added there must keep that property;
  `.github/workflows/ci.yml` asserts it on every push, because it is invisible
  locally where `generated/` exists.
- **The recompiled output and the port's sources compile in ONE Roslyn pass**
  (core's `GameCompile`). Its reference set comes from `TRUSTED_PLATFORM_ASSEMBLIES`, not
  loaded assemblies, and it supplies `GlobalUsings.g.cs` itself, since
  `ImplicitUsings` is an SDK feature.
- **`GameCompile`'s options and `Deception.csproj`'s properties are two
  statements of one thing and must stay in step** — a difference is a bug that
  exists only in the release. Check the packaged binary with
  `KF2_FPS=144 KF2_FPS_PROBE=1`: 144.0 fps drawn at 15.0 ticks/s.
- **The version is the newest `vMAJOR.MINOR.PATCH` tag `HEAD` contains**, as
  Verdite3's is; there is no `VERSION` file and CI refuses one. `bash
  scripts/release.sh 0.5.0` tags `HEAD`, commits nothing, and deliberately does
  not push; `release.yml` names the artifacts by the pushed tag.
- Packaging is `packaging/linux/build-appimage.sh` and
  `packaging/windows/build-windows.ps1`, neither of which needs the disc.
  **Trimming is off and must stay off** (MonoMod detours, Roslyn, `AutoStart`'s
  reflection).
- **QuickJit is off and must stay off, in both `Deception.csproj` and
  `Verdite2.Launcher.csproj`** (`TieredCompilationQuickJit`). Tier-up recompiles a
  hooked method and MonoMod's detour does not reliably follow, so a committed hook
  stops firing for the session with `IsCommitted` still true — that was the boot
  that ran at twice the chosen rate. `FramePacing`'s sentinel prints
  `[KF2] pacing sentinel:` if a pacing hook is ever lost again. `DiscCheck.Validate` fills `Runtime.DiscValidator` and refuses
  `SLUS-00255` by name.

See `docs/PACKAGING.md`.

## Repository conventions

Never commit disc data or recompiler output — `disc/`, `generated/`, `*.sav` and
`settings.json` (written by the runtime at play time) are gitignored for
copyright and cleanliness reasons.

Commit messages in this repo state the *finding*, in the imperative, with the
observable consequence: "Map the PSY-Q CD library; boot now reaches the main
loop".
