# The sharing inventory, file by file

The per-file detail behind `docs/SHARING.md`: for each file its bucket, what it
does, the functions it hooks, the addresses it reads, the struct layouts, its
assumptions about the game's loop, the overlays it names, the env vars and
settings keys it reads, the RecompOne changes it depends on, what a mod could
see of it, and where a generic mechanism meets this game's data.

**Made by reading, not by porting, and the buckets are best guesses.**
It was written on 2026-10-02 by agents reading each file against a common brief,
in parallel groups, then spot-checked against the source and grep. Each group's
own summary is kept, so some observations repeat across sections. Where an
entry and `docs/SHARING.md` disagree, `docs/SHARING.md` is the corrected one; its
"Corrections to the plan's guesses" lists what moved. Line counts and addresses
are as of `b7b16cf`.

Entries use these fields: **Bucket** (A fork, B game-agnostic, C mechanism that
might generalize, D this game only), **Hooks**, **Data** (RAM addresses),
**Structs**, **Control flow**, **Overlays**, **Env / settings keys**, **RecompOne
deps** (patch numbers and runtime types), **Mod-visible**, **Split note**.


## patches/, Agent* to Cull*
Read-only classification per `scratch/sharing/BRIEF.md`. Buckets: A RecompOne fork,
B game-agnostic infra, C generalizable mechanism with KF2 implementation, D KF2-only.

#### patches/AgentBeacon.cs  (123 lines)
- Bucket: C (high) -- generic "machine-readable agent state on stdout"; reads KF2 RAM directly.
- Does: prints `[KF2-AGENT]` overlay transitions and a ~1 Hz JSON state snapshot so an external driver can tell title vs in-game without a screenshot.
- Hooks: none via HookManager; `Event.AddListener` on `OverlayLoadedEvent` and `VSyncEvent`.
- Data: MaxHp 0x80199426 u16 (in-game gate !=0), Hp 0x80199428, MaxMp 0x8019942A, Mp 0x8019942C, Exp 0x80199414 u32, Level 0x8019941C, State 0x801994E1 (`dead==0x11`), DeathFrames 0x8019951A u16, Area 0x8017E060, CurrentSlot 0x8006E5D4, Yaw 0x80199506 s16 (0x1000/turn), Pitch 0x8019950C s16, PosX/Y/Z 0x801994EC/F0/F4 s32.
- Structs: none.
- Control flow: assumes VSync event fires on the game thread; same RAM map as `mods/kf2debug/GameState.cs`.
- Overlays: reads `OverlayLoadedEvent.Name` (open/game/end/fdat*).
- Env / settings keys: `KF2_AGENT` (via Configure).
- RecompOne deps: `RecompOne.Runtime.Events` (OverlayLoadedEvent, VSyncEvent), `Runtime.Mem`, `ReadU8/16/32`.
- Mod-visible: `public static string Snapshot()` (bare `{...}` JSON), `Configure`, `Install`.
- Split note: JSON shape and the beacon timing are generic; the address map is KF2.

#### patches/AnalogProbe.cs  (229 lines)
- Bucket: C (high) -- generic stick-vs-state measurement; all addresses/masks KF2.
- Does: reports control-state (velocities, yaw/pitch steps, walk speed) against stick deflection each window, and dumps the action-mask table once.
- Hooks: `post` on `game`/0x8002A550 (end of main-loop stage 3, after the input stage) via `HookManager.AddPost`.
- Data: Analog.Yaw 0x80199506, Analog.Pitch 0x8019950C; turnVel 0x80199544, pitchVel 0x80199546, fwdVel 0x80199540, strafeVel 0x8019953E, walkMag 0x80199542, pos 0x801994EC/F0/F4; action->button mask table 0x8006E568..0x8006E5D0 word-spaced (Analog.Mask* consts).
- Structs: 24-word action->button-mask table; bit i of a mask is Controller bit `(i+8)&15` (pad halves swapped).
- Control flow: must run after the whole input stage so angles are final for the frame.
- Overlays: game.
- Env / settings keys: `KF2_ANALOG_PROBE`, settings `kf2.analog.probe`, `kf2.analog.probeinterval` (via `Analog.Env`/`Analog.Saved`).
- RecompOne deps: `HookManager`, `SymbolRegistry`, `ModInfo`, `CpuContext`, `IMemory`.
- Mod-visible: internal class; `AfterInputStage` public, `Configure`/`Attach`/`Note*` internal.
- Split note: the probe mechanism is generic; address/mask layout is KF2.

#### patches/AudioQuality.cs  (69 lines)
- Bucket: B (high) -- only switches runtime SPU settings; no game knowledge.
- Does: maps env/saved settings to `Spu.Interpolation` and `Spu.ReverbMode`.
- Hooks: none; `RuntimeReadyEvent` listener re-reads saved keys.
- Data: none.
- Structs: none.
- Control flow: saved choice read at RuntimeReady, after ConfigManager loads (later than Program.cs).
- Overlays: none.
- Env / settings keys: `KF2_SPU_INTERP` (gauss|cubic|sinc), `KF2_REVERB` (legacy|hardware|enhanced); settings `kf2.audio.interp`, `kf2.audio.reverb`.
- RecompOne deps: patch `0043`; `Spu`, `SpuInterpolation`, `SpuReverbMode`, `Runtime.View`, `RuntimeReadyEvent`.
- Mod-visible: `Configure`, `Install`, `SetInterpolation`, `SetEnhancedReverb`, consts `InterpKey`/`ReverbKey`/`OriginalReverb`.
- Split note: none.

#### patches/BootExe.cs  (107 lines)
- Bucket: D (high) -- writes this disc's boot-stub selector; no generic content.
- Does: `KF2_BOOTEXE` boots straight into OPEN.EXE / GAME.EXE / END.EXE for diagnostics.
- Hooks: `pre` on `main`/0x80010038 (stub loader loop) via `HookManager.AddPre`; attached in the first `OverlayLoadedEvent`.
- Data: Selector 0x80010268 written with 0/1/2; filename table 0x80010254 (0=OPEN,1=GAME,2=END) and the re-read byte 0x800102F0 noted, not written.
- Structs: 3-entry file-name table.
- Control flow: writes the index once, on the loop's first pass only -- re-entry (EndingHold to title) must not re-trigger.
- Overlays: `main` (boot stub SLUS_001.58).
- Env / settings keys: `KF2_BOOTEXE=open|game|end`.
- RecompOne deps: `HookManager`, `SymbolRegistry`, `ModInfo`, `CpuContext`, `IMemory`, `OverlayLoadedEvent`.
- Mod-visible: `Configure`, `Install`, `BeforeLoaderLoop`.
- Split note: none.

#### patches/CardIcon.cs  (173 lines)
- Bucket: D (high) -- fixed offsets and file names of this disc's card icon.
- Does: decodes the game's memory-card icon off the player's own disc into window + desktop icons.
- Hooks: none.
- Data (disc): CD/COM/FDAT.T CLUT at 0x14D210 (3 frames 0x30 past it, 16x16 4bpp), GAME.EXE palette at 0x56DB4 used as the signature; scan fallback.
- Structs: none.
- Control flow: called at boot from Program.cs with disc path.
- Overlays: disc files GAME.EXE, CD/COM/FDAT.T.
- Env / settings keys: `KF2_ICON=orb|png|off|<frame digit>`, `KF2_ICON_INSTALL` (read inside `DesktopEntry.Publish`).
- RecompOne deps: `RecompOne.Runtime.Cdrom.DiscFs`, `Runtime.SetIcons`/`ClearIcon`/`CdPath`, `DesktopEntry`.
- Mod-visible: `public static void Install(string? discPath)`.
- Split note: decode/scale is generic; the offsets and disc signature are KF2.

#### patches/AreaWarp.cs  (214 lines)
- Bucket: D (high) -- FDAT area structure, KF2 routines and tables throughout.
- Does: re-enters an area through the game's own loader, parking/snapping the player into loaded geometry.
- Hooks: none; direct calls into `Recompiled.KingsField2_game`: func_80024154, func_80025D38, func_80025DA8, func_80029E5C.
- Data: MaxHp 0x80199426, State 0x801994E1 (dead 0x11), Area 0x8017E060, PosX/Y/Z 0x801994EC/F0/F4, Strafe/Fwd/WalkMag 0x8019953E/40/42; SafeX/Y/Z 0x11800/0xFFFFCE00/0x18000.
- Structs: object table 0x80177714 stride 0x44 count 0x18C (empty u8@+4==0xFF, VECTOR@+0x14); entity table 0x8016C544 stride 0x7C count 0xC8 (empty@+0, VECTOR@+0x2C); tile map 0x50 x 0x50, tile shift 11.
- Control flow: caller must be on the game thread at main-loop stage 3; func_80024154 loops the CD via func_80017818/VSync so must never run from the VSync event.
- Overlays: game; areas 0..7 valid, area 10 = fdat32 cut (entry 32, linked 0x80193B38 vs 0x8019F07C).
- Env / settings keys: none (`warp` shell command calls it).
- RecompOne deps: `CpuContext.Snapshot/Restore`; direct generated-class calls; `mods/kf2debug/Warp.cs` delegates here.
- Mod-visible: `public static readonly int[] Areas`, `CutArea`, `public static string? TryRun(...)`.
- Split note: "warp via the loader" is generic; area indices, tables and routines are KF2.

#### patches/AudioProbe.cs  (183 lines)
- Bucket: B (high) -- reads only runtime Spu/AudioStats; no game data.
- Does: 1 Hz audio stat line (voices, clamps, mixer ms, underruns, levels, reverb) and optional WAV dump of final mix + reverb return.
- Hooks: none; subscribes `Spu.Mixed`; background reporter/drain threads.
- Data: none.
- Structs: none.
- Control flow: assumes `FramesPerBuffer = 256` for per-buffer cost.
- Overlays: none.
- Env / settings keys: `KF2_AUDIO_PROBE`, `KF2_AUDIO_DUMP=dir`.
- RecompOne deps: `Spu`/`Spu.Mixed`, `ReverbPreset`/`ReverbPath`, `XaAudio`, `AudioStats`, `WavWriter`, `RecompOne.Runtime.Host`.
- Mod-visible: `Configure`, `Install`.
- Split note: none.

#### patches/CameraBlock.cs  (198 lines)
- Bucket: C (high) -- transcribes a generic camera routine; KF2 address and RAM block.
- Does: func_8002E22C in C#: copies a VECTOR position and SVECTOR angles into the camera block, derives the eye tile, rebuilds view + pitch matrices.
- Hooks: `replace` on `game`/0x8002E22C via `HookManager.AddReplace` (`KF2_CAMERABLOCK=0` recompiled, `=verify` diffs).
- Data: ViewMatrix 0x80192E18, PitchMatrix 0x80192E38, Position 0x80192E78 (VECTOR+pad), Angles 0x80192E88 (SVECTOR+pad), TileX 0x80192E90 / TileZ 0x80192E94 (`X>>11`, `Z>>11`); block Start/Bytes for ScenePass.
- Structs: camera block layout VECTOR/SVECTOR; 0x400-byte differential compare region.
- Control flow: cull grid func_8002D3A8 reads its eye from this block; camera is the previous frame's view.
- Overlays: game.
- Env / settings keys: `KF2_CAMERABLOCK=0|verify`.
- RecompOne deps: `HookManager`, `HookAttach`, `Differential`, `Pgxp.Pgxp.CpuTracking`, `PSMemory`; direct calls func_80015048/func_80014E90.
- Mod-visible: `public readonly record struct Camera` + `Camera.Read`, public consts, `Store`, `Build`, `Read`.
- Split note: routine transcription and Camera value are generic; block addresses and callees are KF2.

#### patches/CrashDump.cs  (267 lines)
- Bucket: C (high) -- generic post-fault RAM dump; every table is KF2.
- Does: after an unhandled exception, prints equipped weapon, entity table, descriptor block and the fdat23 boss record to explain the fault.
- Hooks: none (a `catch` around `Entry.Run` in Program.cs; deliberately perturbs nothing while running).
- Data: EntityBase 0x8016C544 stride 0x7C count 0xC8; DescBase 0x80172624 stride 120, ptr@+0x38, 15 ptrs, end `+0xCB0*4`; WeaponPtr 0x80199494 -> `0x801C7FBC + id*0x44`, AltMode 0x801994AE, EquipId 0x801994AF, SwingClock 0x801994A4; BossRecPtr 0x801A0598; EffectsBase 0x8019CC6C stride 0x48 count 128.
- Structs: entity kind@+0, drawn@+9, type@+2, pos@+0x2C; descriptor pointer-table layout; weapon parameters first/second sets.
- Control flow: reads only after the fault, assumes RAM still intact and nothing runs again; must never throw.
- Overlays: fdat23 record referenced; nothing hooked.
- Env / settings keys: `KF2_CRASHDUMP=0`.
- RecompOne deps: `IMemory`, `PSMemory`; no hooks.
- Mod-visible: internal class; `public static bool On`, `Configure`, `Dump(Exception, IMemory?)`.
- Split note: catch-and-dump mechanism is generic; all addresses/layouts are KF2.

#### patches/AmbientOcclusion.cs  (368 lines)
- Bucket: B (high) -- switch/report over the runtime's SSAO (`0040`); only touches SDK entry addresses and runtime types.
- Does: on/off/quality switch, tuning, and the coverage/normal/census report for the runtime's screen-space ambient occlusion from recovered GTE depth.
- Hooks: `post` on libgpu `DrawOTag` per overlay only while probing -- open 0x80016078, game 0x80060818, end 0x80013D80 (the config's SDK mapping), to count frames.
- Data: none in guest RAM; writes `GteDepth.Ao*` (radius/strength/bias/samples/maxdepth/normals/probe) and reads its counters.
- Structs: none.
- Control flow: pass runs at present between the render target and the window blit; painter's-order depth via `GL_ALWAYS`.
- Overlays: SDK entry addresses for open/game/end (no game data).
- Env / settings keys: `KF2_AO`, `KF2_AO_RADIUS`, `KF2_AO_STRENGTH`, `KF2_AO_BIAS`, `KF2_AO_SAMPLES`, `KF2_AO_QUALITY`, `KF2_AO_MAXDEPTH`, `KF2_AO_NORMALS`, `KF2_AO_PROBE`; keys `kf2.ao.on`, `kf2.ao.quality`.
- RecompOne deps: patches `0040`, `0058`, `0048`; `GteDepth`, `AoGeometry`, `RuntimeReadyEvent`, `OverlayLoadedEvent`, `HookManager`.
- Mod-visible: `Configure`, `Install`, `SetEnabled`, `SetQuality`, `Enabled`, `CurrentQuality`, `Quality` enum, consts `OnKey`/`QualityKey`.
- Split note: mechanism wholly in the runtime; this file is a generic switch -- B, with only the `KF2_` prefix game-named.

#### patches/AoWorld.cs  (395 lines)
- Bucket: C (high) -- world-space occluder mechanism (generic) fed by KF2's tile grid and camera globals.
- Does: publishes the camera rotation/world position and builds a floor-plan heightfield from the 80x80 tile grid so the occlusion pass can use off-screen occluders.
- Hooks: `pre` on `DrawOTag` open/game/end (same three addresses).
- Data: ViewMatrix 0x80192E18 (9 s16 at 4096/turn-ish, rows unit-checked); CamWorldX/Y/Z 0x80192E78/7C/80 u16; PlayerX/Y/Z 0x801994EC/F0/F4 s32; TileBase 0x801C8484, stride 10; height low +1, upper +6; drawn if model byte < 240; block bit `+4`/`+9` & 0x80; height -> `h*128` world (GteDepth.AoHeightSpan).
- Structs: 80x80 tile grid of 10-byte records (model/height `+0/+1` lower, `+5/+6` upper, block `+4/+9`).
- Control flow: grid re-read every 30 frames; a change must be read twice before accepted; last good transform held rather than dropping.
- Overlays: open/game/end.
- Env / settings keys: `KF2_AO_WORLD`, `KF2_AO_WORLD_STRENGTH`, `KF2_AO_WORLD_RADIUS`, `KF2_AO_WORLD_PROBE`; `GteDepth.AoWorld*`.
- RecompOne deps: patch `0059`; `GteDepth`, `TileWalk`, `PSMemory`, `HookManager`, `SymbolRegistry`.
- Mod-visible: `Configure`, `Install`, `BeforeDrawOTag`.
- Split note: the off-screen-occluder idea is generic; grid layout, height decode and camera addresses are KF2.

#### patches/Analog.cs  (634 lines)
- Bucket: C (high) -- generic twin-stick-by-velocity mechanism; KF2 control state and mask table.
- Does: drives the game's own turn/pitch/walk/strafe velocities from the sticks by pre-loading the velocity word and asserting the matching action-mask button, so the game's own collision/animation runs on a chosen amount.
- Hooks: `pre` on `game`/0x80028DB8 (LookRoutine) and `game`/0x800290D4 (MoveRoutine); `AnalogProbe.Attach` adds a post on 0x8002A550. All attached on the first overlay load.
- Data: Pad 0x80199554 u16 (active high); StrafeVel 0x8019953E, FwdVel 0x80199540, TurnVel 0x80199544, PitchVel 0x80199546, MoveSpeed 0x80199558 (0xC8=200), TurnRate 0x8019955C (0x1C, 0x23 running); Pitch 0x8019950C, Yaw 0x8019950E; action-mask table 0x8006E568..0x8006E5D0 (Mask* consts at 0x8006E580..0x8006E59C).
- Structs: 24-word action->button mask table; per-axis velocity/carry state.
- Control flow: assumes the three-branch `vel += rate>>2; clamp; else decay; angle += vel` form; runs inside the look routine shared with `Mouse`; left-stick-leak fix zeroes the turn bits.
- Overlays: game.
- Env / settings keys: `KF2_ANALOG`, `_LOOK`, `_MOVE_ENABLE`, `_TURN/_PITCH/_MOVE`, `_DEADZONE/_MOVEDEADZONE`, `_CURVE/_MOVECURVE`, `_ACCEL/_ACCELMAX/_ACCELTIME`, `_INSTANTSTOP`, `_INVERTY/_INVERTTURN/_INVERTSTRAFE/_INVERTFWD`, `KF2_ANALOG_PROBE`; keys `kf2.analog.*` (18).
- RecompOne deps: `Controller`, `HookManager`, `SymbolRegistry`, `RuntimeReadyEvent`, `OverlayLoadedEvent`, `ModInfo`; sibling `Mouse`, `AnalogProbe`.
- Mod-visible: public fields (`Enabled`, sensitivities, deadzones, `Sticks`), `Configure`, `Install`, `BeforeLook`, `BeforeMove`, `Env`/`Saved` internal, keys.
- Split note: the velocity-preload + mask-assertion mechanism generalizes to any velocity-based control scheme; addresses and mask layout are KF2.

#### patches/AnimSmoothing.cs  (1588 lines)
- Bucket: C (high) -- generic clip-timeline pose interpolation; KF2 MO clip tables, routines and stack argument layout.
- Does: drives `func_8003486C`'s clip clock between logic ticks, interpolating creature/arm MO pose on the clip's own circle, with legacy Weight/Time comparison modes.
- Hooks: `pre` on `game`/0x800342D8 (Renderer, frame bracket); pre/post pairs on 0x80032588 (ModelSubmit), 0x8003486C (ClipClock), 0x80032400 (ArmDraw), 0x80034DA8 (MoApply); all-or-nothing after `HookManager.Commit`.
- Data: SwingClock 0x801994A4 s16 (-1 idle); ArmSlot = SwingClock-8; clip byte at caller `SP+0x1C`, clip time at `SP+0x20`, weight pointer at `SP+0x10`; clip table `bank + u32[bank+0x10]`, clip record, segment records.
- Structs: clip record `u16 count`@+0 then `u32 bank-relative offsets`@+4; segment record `u16 reversedFlag`@+0, `u16 duration`@+2; 12.12 blend weight; per-slot playback state.
- Control flow: one sample per tick via `FramePacing.FirstWalkOfTick`; `FramePacing.LogicPhase` interpolation `(t-1+frac)`; coupling to `ObjectSmoothing.PositionHeld` on the same position address; stands down while `ModelWalk.Verifying`.
- Overlays: game.
- Env / settings keys: `KF2_SMOOTH_ANIM=0|timeline|weight|time`, `KF2_SMOOTH_ANIM_PROBE`; keys `kf2.smoothing.anim`, `kf2.smoothing.anim.mode`.
- RecompOne deps: `FramePacing`, `ObjectSmoothing`, `ModelWalk`, `HookAttach`, `HookManager`, `SymbolRegistry`; ModInfo `kf2.animsmoothing`.
- Mod-visible: `Mode` enum, `Enabled`, `Carry`, `Configure`, `Install`, `SetEnabled`, `SetCarry`, `TakeHealth`, public hooks.
- Split note: clip-circle interpolation is generic; segment-table layout, routines and stack offsets are KF2.

#### patches/AgentServer.cs  (890 lines)
- Bucket: C (high) -- generic TCP/JSON command channel; nearly every verb's body is KF2 state.
- Does: line protocol on 127.0.0.1 exposing `state`, `load`, `warp`, `press`, `kill`, `nearby`, `ending`, `map`, `goto`, `view`, `waves`, `gpuworld`, `pause`, `aspect`, `capture`, `murk` and the remaster verbs.
- Hooks: `post` on `game`/0x8002A550 (heavy drain); `VSyncEvent` (fast drain); `PadReadEvent` (press injection); `FramePacing.PauseWhen`.
- Data: PlayerMaxHp 0x80199426, PosX/Y/Z 0x801994EC/F0/F4; object table 0x80177714 (+0x14), entity 0x8016C544 (+0x2C), effect 0x8019CC6C (+0x14), sprite 0x80195174 (+0x8); QuitWord 0x80199574 (1 ending, 9 title); fdat23 boss 0x8019F474/0x8019F688/0x8019FA2C/0x801B30A2/0x801B30A6; death 0x8003A490; descriptor base 0x80172624; cull grid 0x80192EAC, offsets 0x80192E98/9C; BaseYaw 0x8019950E; Analog.Pitch.
- Structs: object 0x44 x 0x18C; entity 0x7C x 0xC8; effect 0x48 x 128; sprite 0x18 x 128; descriptor 120-byte records with 15 pointers at +0x38.
- Control flow: two marshal points -- fast (state/press/kill/help/map/...) at VSync, heavy (load/warp/goto/ending/savecheck) at stage 3 because `func_80024154` nests VSync; `map` deliberately on the fast queue.
- Overlays: game, fdat23.
- Env / settings keys: `KF2_SHELL` (=1 default port 27900, or a port); ModInfo `kf2.agentserver`.
- RecompOne deps: `FramePacing.PauseWhen`, `SymbolRegistry.Resolve`/`CreateDelegate`, `CpuContext.Snapshot/Restore`, `Interrupts`; siblings `AreaWarp`, `AutoReload`, `AgentBeacon`, `Stage13`, `Camera`, `Widescreen`, `Waves`, `GpuWorld`, `Murk`, `MapFullscreen`, `FrameCapture`, `Remaster.Shell`.
- Mod-visible: `DefaultPort`, `Port`, `Configure`, `Install`, `AfterPlayerStage`; the `mcp/` server proxies it.
- Split note: socket/JSON routing and the two-queue marshal shape are generic; every command body is KF2.

#### patches/AutoReload.cs  (440 lines)
- Bucket: C (high) -- generic reload-last-save-on-death mechanism; KF2 loader, state block and post-load arm.
- Does: holds the game's death counter at the end of the animation and reloads the last-used (or pinned) save through the game's own loader after 2 s.
- Hooks: `post` on `game`/0x8002A550 (PlayerStage); `OverlayLoadedEvent`/`RuntimeReadyEvent`; direct calls func_80023638, func_800240B8, func_80024154, func_80025D38, func_80029E5C, func_8002A264.
- Data: Level 0x8019941C, MaxHp 0x80199426, Hp 0x80199428, State 0x801994E1 (dead 0x11), Area 0x8017E060, DeathFrames 0x8019951A (held at 31; game's own respawn at 65), CurrentSlot 0x8006E5D4; buf2 stat block at 0x80199414 (0x58 bytes).
- Structs: buf2 0x58-byte per-area stat block; death-state jump table at 0x80011300 + state*4.
- Control flow: the game's own post-load arm at 0x80029E0C transcribed; hook runs every frame dead or alive; never arms from a state not watched alive; delay is fixed 2 s, not a setting.
- Overlays: game.
- Env / settings keys: `KF2_AUTORELOAD`, `KF2_AUTORELOAD_DELAY` (comparison; key `kf2.autoreload.delay` deliberately unread), `KF2_AUTORELOAD_SLOT`; keys `kf2.autoreload.enabled`, `kf2.autoreload.slot`.
- RecompOne deps: `HookManager`, `SymbolRegistry`, `CpuContext.Snapshot/Restore`, `RuntimeReadyEvent`, `OverlayLoadedEvent`, `ModInfo`; `LoadSlot` shared with `AutoStart`.
- Mod-visible: `Enabled`, `Delay`, `Slot`, `Status`, `Deaths`, `Reloads`, `Configure`, `Install`, `SetEnabled`, `SetDelay`, `SetSlot`, `Simulate`, `AfterPlayerStage`; internal `LoadSlot`.
- Split note: reload-on-death is generic; the loader routines and buffers are KF2.

#### patches/AutoStart.cs  (225 lines)
- Bucket: C (high) -- generic "drive the pad through the boot menus" idea; KF2 overlay names and menu structure.
- Does: injects Start/Cross through `PAD_dr` to skip OPEN.EXE title and GAME.EXE start menu, then loads a save over the New Game (or stops in it).
- Hooks: `post` on `game`/0x8002A550; `PadReadEvent` injection; `OverlayLoadedEvent`.
- Data: MaxHp 0x80199426, Hp 0x80199428, Level 0x8019941C (live-character test).
- Structs: none.
- Control flow: pad buffer is active-low with halves swapped; 90-frame settle; expects overlays `open` -> `game` -> `fdat*`; one shot per GAME.EXE load.
- Overlays: open, game, fdat*.
- Env / settings keys: `KF2_AUTOSTART=1..3|new`; ModInfo `kf2.autostart`.
- RecompOne deps: `Controller`, `PadReadEvent`, `HookManager`, `SymbolRegistry`, `AutoReload.LoadSlot`.
- Mod-visible: `Slot`, `NewGame`, `Status`, `Configure`, `Install`, `AfterPlayerStage`.
- Split note: boot-skip by pad injection generalizes; the overlay names, menu timings and addresses are KF2.

#### patches/BlackProbe.cs  (734 lines)
- Bucket: C (high) -- generic per-drawn-frame display readback diagnostic; KF2 globals and overlays.
- Does: samples the presented display rectangle each drawn frame (luminance + pixel signature) and dumps the window around every fdat load, optionally as PNGs.
- Hooks: `post` on `DrawOTag` open/game/end; `CrossProbe.InstallRenderer`; `OverlayLoadedEvent`.
- Data: TintMode 0x80192D45, TintRed 0x80192D46; PosX 0x801994EC, PosZ 0x801994F4, ComposedYaw 0x80199506.
- Structs: none (delegates `TileWalk.CellsDrawn`, `CrossProbe` camera/built counters, `FrameSmoothing.LastVerdict`).
- Control flow: per drawn frame, not per vblank; skips `0039`'s restore copies; reads backend VRAM via `GpuJobs`.
- Overlays: open/game/end, fdat*.
- Env / settings keys: `KF2_BLACKPROBE`, `KF2_BLACKPROBE_OUT`; ModInfo `kf2.blackprobe`.
- RecompOne deps: patch `0039`; `GpuHle.Backend`, `GlCore.ReadVram`, `PngWriter`, `GpuJobs`, `LibEtc.VSyncCalls`, `LibGpu.AutoPresents`; siblings `CrossProbe`, `TileWalk`, `FrameSmoothing`.
- Mod-visible: `Enabled`, `Configure`, `Install`, `AfterDrawOTag`.
- Split note: the readback/sampling harness is generic; the tint and view addresses and fdat trigger are KF2.

#### patches/CrossProbe.cs  (379 lines)
- Bucket: C (high) -- generic per-frame "what did the order table get" census; KF2 addresses and tables.
- Does: records cells walked/drawn, models submitted, tick identity, area and view per presented frame, and dumps a window around every fdat load to name a bad crossing frame.
- Hooks: `pre`+`post` on `game`/0x800342D8 (Renderer) and `post` on `DrawOTag` open/game/end.
- Data: AreaByte 0x8017E060, PosX 0x801994EC, PosZ 0x801994F4, Yaw 0x80199506 s16 (0x1000/turn); camera 0x80192E78/0x80192E80.
- Structs: none (uses `TileWalk.CellsWalked/CellsDrawn`, `ModelWalk.Scene`).
- Control flow: `FramePacing.TickedThisFrame` for tick vs redraw; `built` from `RendererRuns`; per-area cell peak; view-jump test.
- Overlays: game, open/end.
- Env / settings keys: `KF2_CROSSPROBE=1|2`; ModInfo `kf2.crossprobe`.
- RecompOne deps: `TileWalk`, `ModelWalk`, `FramePacing`, `Interrupts.ClockMs`, `HookAttach`; `RendererRuns` read by `BlackProbe`.
- Mod-visible: `Enabled`, `Verbose`, `RendererRuns`, `Install`, `InstallRenderer`, `AfterDrawOTag`, `EnterRenderer`, `LeaveRenderer`, `CamX/CamZ/TrueX/TrueZ/Stage8Ran`.
- Split note: the census mechanism (hook DrawOTag/renderer, count cells/models, detect jumped view) generalizes; addresses and fdat trigger are KF2.

#### patches/CullCone.cs  (728 lines)
- Bucket: C (high) -- generic "widen the game's view cone with the aspect" mechanism; KF2 tile grid and cone table.
- Does: scales the game's floor-projected view trapezoid to the widescreen aspect, fixes the scanline fill so widened rows are not lost, and force-lights a disc around the camera to undo the flood's blind spot.
- Hooks: `pre` on `game`/0x8002CD0C (LineRoutine), `post` on 0x8002CF0C (FillRoutine), `post` on 0x8002D3A8 (BuildRoutine); direct call func_8002D3A8 for the superset check.
- Data: cone table 0x80068760 (7 s16 pairs, 1/256 tile, lerped by `0x1000-rcos(pitch)`); Grid 0x80192EAC (24x24 row-major on Z); OffsetX 0x80192E98, OffsetZ 0x80192E9C (read as u16); EyeTileX/Y 0x80192E90/94; CamWorldX/Z 0x80192E78/80; cell index `world>>12 + offset` (Bresenham) or `world>>11` (queries).
- Structs: 7-pair cone table (forward push, far/near edges L/R+depths); 24x24 byte grid; cell bits (bit0/1 halves, 0xC0 rescue/block).
- Control flow: grid rebuilt every frame at the top of the renderer, before the object walks; occlusion flood runs from the window middle (5 tiles ahead of eye); a widened trapezoid must be OR-ed on top, not written into the table.
- Overlays: game.
- Env / settings keys: `KF2_WIDESCREEN_CULL`, `KF2_WIDESCREEN_CULL_PROBE`, `KF2_CULL_RESCUE_RADIUS`; `Widescreen.On`, `Display.WideMargin(320)`; old key `kf2.widescreen.widencull` deliberately unread.
- RecompOne deps: `Widescreen`, `CullGrid`, `RenderDistance`, `Display`; `RuntimeReadyEvent`, `OverlayLoadedEvent`, `HookManager`.
- Mod-visible: `Table`, `Enabled`, `StockCorners`, `StockBuilds`, `Factor`, `Configure`, `Install`, `Apply`, public hooks, internal `Widen`/`RescueRadius`/`RescueActive`/`RescueDisc`.
- Split note: aspect-following cull widening is generic; the cone table, tile grid and offsets are KF2.

#### patches/Anisotropic.cs  (259 lines)
- Bucket: B (high) -- switch/probe over the runtime's texture-filter kernels (`0041`/`0060`); PS1 texture-page/CLUT scheme is generic.
- Does: level + mipmap switch and the "does the uniform reach the shader / is the atlas built" probe for anisotropic filtering after the CLUT.
- Hooks: none for the feature; a `VSyncEvent` listener only for the probe report.
- Data: none in guest RAM; writes `GteDepth.Anisotropy`, `GteDepth.Mipmaps`, `GteDepth.MipVerify`.
- Structs: none.
- Control flow: the prim program is built on the first present, so the uniform can only be asked about after frames have drawn (`ReportAfter = 120`).
- Overlays: none.
- Env / settings keys: `KF2_ANISO`, `KF2_ANISO_PROBE=1|2`, `KF2_MIPMAPS`; keys `kf2.aniso.level`, `kf2.mipmaps.on`.
- RecompOne deps: patches `0041`, `0060`; `GteDepth`, `GteTexRect`, `RuntimeReadyEvent`.
- Mod-visible: `Configure`, `Install`, `SetLevel`, `SetMipmaps`, `Level`, `Enabled`, `Mipmaps`, `Max`, consts `LevelKey`/`MipKey`.
- Split note: mechanism is in the runtime shaders; this file is a generic switch -- B.

### Summary

Bucket counts (20 files):
- **A (RecompOne fork): 0** -- none of these files live under `tools/RecompOne`.
- **B (game-agnostic infra): 4** -- `AudioQuality`, `AudioProbe`, `AmbientOcclusion`, `Anisotropic`. Each is a switch/report over a runtime feature (SPU settings, SPU stats, SSAO, aniso/mipmaps), reads no guest RAM and names only the `KF2_` env prefix.
- **C (generalizable mechanism, KF2 implementation): 14** -- `AgentBeacon`, `AgentServer`, `Analog`, `AnalogProbe`, `AnimSmoothing`, `AoWorld`, `AreaWarp`*, `AutoReload`, `AutoStart`, `BlackProbe`, `CameraBlock`, `CrossProbe`, `CullCone`, `CrashDump`. (*`AreaWarp` is borderline D; it is a loader-driven warp but the concept generalizes.)
- **D (KF2-only): 2** -- `BootExe` (writes this disc's boot-stub selector), `CardIcon` (fixed disc offsets for this game's memory-card icon).

Most important observations:
1. **The generic seam is consistent: mechanism in `patches/`, data in addresses.** Nearly every file's hook attachment, env/settings precedence (`Env` > `Loaded`), overlay-load timing (`HookAttach.OnOverlayLoad` resolving through `SymbolRegistry`) and "FPS/rate" plumbing is reusable verbatim; only the address tables and struct layouts differ.
2. **`AudioProbe`, `AmbientOcclusion`, `Anisotropic` and `AudioQuality` are already portable**: they read `Spu`/`GteDepth`/backend state and hook only SDK entry addresses, none of the game's RAM.
3. **A small set of RAM addresses recurs across files** and is the de-facto KF2 state map: player stat block 0x80199414.. (`MaxHp` 0x80199426, `State` 0x801994E1, pos 0x801994EC/F0/F4, yaw 0x80199506), area byte 0x8017E060, current slot 0x8006E5D4, camera block 0x80192E18..94. A shared `Kf2State` type would be the obvious extraction.
4. **The game's per-frame entry points are reused heavily**: main-loop stage 3 `0x8002A550`, stage 13 `0x800342D8`, libgpu `DrawOTag` (0x80016078/0x80060818/0x80013D80) and the cull build `0x8002D3A8`. Any sibling port needs its own equivalents before these patches can attach.
5. **Several files depend on other patches' runtime types** -- `FramePacing` (phase/tick/gating/PauseWhen), `ObjectSmoothing.PositionHeld`, `TileWalk`, `ModelWalk`, `CrossProbe.RendererRuns`, `Widescreen`, `CullGrid` -- so sharing is not per-file but along a small dependency graph.
6. **Diagnostics dominate**: `AgentBeacon`, `AnalogProbe`, `AudioProbe`, `BlackProbe`, `CrossProbe`, `CrashDump`, `BootExe`, `AutoStart` exist to drive or observe; the actual gameplay-changing ports are `Analog`, `AutoReload`, `CullCone`, `AreaWarp`, `AmbientOcclusion`, `Anisotropic`.
7. **`AgentServer` is the API surface a sibling port would most want to share**, but it embeds the whole KF2 vocabulary (`warp`, `ending`, remaster verbs); the transport and two-queue marshal are the reusable half.
8. **`AnimSmoothing` is the deepest coupling**: it parses the MO clip table format (`bank+0x10` -> clip table -> segment durations), reads caller stack words (`SP+0x1C/0x20/0x10`) and assumes `func_80034DA8` as the clock's caller. This is genuinely engine-specific reverse-engineering, not portable without an equivalently documented animation system.

## patches/, CullGrid to FrameViewerPanel
Read-only classification per `scratch/sharing/BRIEF.md`. Buckets: A RecompOne fork,
B game-agnostic infra, C generalizable mechanism with KF2 implementation, D KF2-only.
Group: `CullGrid DesktopEntry Differential DrawCensus EndingHold EnhancementDistance
EvenFog FluidSmoothing FrameCapture FramePacing FrameProfiler FrameSmoothing
FrameViewerPanel`.

#### patches/CullGrid.cs  (666 lines)
- Bucket: C (high) -- a tile-visibility cull grid is a general idea, but every address, record stride and trig call is GAME.EXE's.
- Does: rebuilds the game's 24x24 tile-visibility pipeline at 32x32 (build + point/box queries) so the widescreen-widened cone is not truncated by the shipped window.
- Hooks: `replace` on `game`/0x8002D3A8 (build `func_8002D3A8`), `game`/0x80032D78 (point query), `game`/0x80032DE8 (box query) in `on` mode; `post` on the build in `shadow` mode (`HookManager.AddReplace`/`AddPost`, `Commit`).
- Data: PitchAddr 0x80192E88, YawAddr 0x80192E8A; CamWorldX/Y/Z 0x80192E78/7C/80; OffsetX/Z 0x80192E98/9C, MirrorX/Z 0x80192EA0/A4; Legacy array 0x80192EAC (24x24, cropped into every on-frame); EyeTileX/Z 0x80192E90/94; MapBase 0x801C8484; AreaNum 0x801D9C8E u16 (gate: 0 = marker 1/alive 2, else 2/1). Angles are 12-bit (`>>12` for tile fractions, 0x1000/turn), camera world in 1/4096-tile units (`<<1`, `>>11` for tiles). Calls `KingsField2.rcos`, `KingsField2.rsin`, `KingsField2.func_8002B6B4` (direct, not hooks). `CullCone.Table`, `CullCone.Widen`, `CullCone.RescueActive`, `CullCone.RescueRadius`.
- Structs: private 32x32 byte grid (span 32, centre 16); legacy 24x24 array (mapping ours (i+4,j+4)); map record = `MapBase + area + 800*z + 10*x`, `+4` with 0x80 = alive, `0xFF` = empty; flood is 14 rings x 8 directions over a stride-32 index, parent offsets in `Dir[]` (e.g. -32/-31/-33).
- Control flow: shadow runs our build after the stock one each frame (stock grid is the oracle); on replaces the build and both queries and crops the central 24x24 into the legacy array so `func_80031C94` keeps rendering the truth. Force-lights the 3x3 around the window middle; near-camera rescue discs shared with `CullCone`. Differential compare over 576 central cells, rescue-class cells counted separately.
- Overlays: game (all three routines resolved as `game`).
- Env / settings keys: `KF2_CULLGRID=shadow|on`, `KF2_CULLGRID_COMPARE=1`; no settings key.
- RecompOne deps: `HookManager`, `SymbolRegistry`, `Event`/`OverlayLoadedEvent`, `ModInfo`, `CpuContext`, `IMemory`, `GCHandle` pin. Direct generated-class calls (`Recompiled.KingsField2_game`). No numbered patch.
- Mod-visible: `public static int LegacyBias`, `public static void Configure/Install`.
- Split note: the tile-visibility build/flood shape is reusable; the map-record layout, camera block addresses, fixed-point conventions and the nine stride-24 routines are KF2.

#### patches/DesktopEntry.cs  (188 lines)
- Bucket: B (high) -- a Wayland desktop-entry/icon writer; only the app id and a couple of literal strings are game-specific.
- Does: writes an RGBA->PNG icon set under `$XDG_DATA_HOME/icons/hicolor/NxN/apps/<appid>.png` and, when no `<appid>.desktop` exists anywhere, a launcher entry, so a Wayland compositor can show the window icon.
- Hooks: none.
- Data: none.
- Structs: none.
- Control flow: called at boot with the decoded icon images (see `CardIcon`); Linux only; no X11/Windows path.
- Overlays: none.
- Env / settings keys: `KF2_ICON_INSTALL` (=`0`/`off` disables); reads `XDG_DATA_HOME`, `XDG_DATA_DIRS`, `APPIMAGE`. No settings key.
- RecompOne deps: `RecompOne.Runtime.Runtime.AppId`.
- Mod-visible: `public static void Publish(IReadOnlyList<(byte[] Rgba,int W,int H)> images)`.
- Split note: the icon/entry writing and PNG encoder are generic; the literal `Name=Verdite2`, `GenericName=King's Field`, the SLUS-00158 comment and `Icon=<appid>` are the only KF2 strings.

#### patches/Differential.cs  (114 lines)
- Bucket: B (high) -- a generic "run recompiled routine and C# twin from one state, compare" harness; no addresses.
- Does: executes the original recompiled function, snapshots RAM/callee-saved registers/GTE, restores entry state, runs the C# replacement, and reports byte/register/GTE mismatches (the recompiled result is kept).
- Hooks: none itself (called from `TileWalk`, `ModelWalk`, `PolyAssembler`, `Stage13` verify modes).
- Data: `stackWindow` is a caller parameter, not a game address; excludes guest RAM outside `[SP-stackWindow, SP)` and above `SP`.
- Structs: guest RAM as a `Span<byte>` via `PSMemory.Ram`; `CpuSnapshot` (SP/RA/S0-S7/FP); `Gte.State`.
- Control flow: one call deep; restore-theirs after the comparison so the recompiled result stands.
- Overlays: none.
- Env / settings keys: none.
- RecompOne deps: `RecompOne.Runtime.Context.CpuSnapshot`, `Memory.PSMemory`, `Gte`/`Gte.State`; `System.Runtime.InteropServices.MemoryMarshal`.
- Mod-visible: `sealed class Differential` is internal (not mod-visible); `public static Span<byte> Ram(PSMemory)`, `bool CalleeSavedEqual(in CpuSnapshot,in CpuSnapshot)`, `string? Describe(ReadOnlySpan<byte>,ReadOnlySpan<byte>,int,int)`, `void Run(...)`.
- Split note: none -- this is pure infrastructure.

#### patches/DrawCensus.cs  (392 lines)
- Bucket: C (high) -- the "charge primitives to the routine that bumped the arena" method generalizes; the routine table and every address are GAME.EXE's.
- Does: attributes the frame's primitive bytes to the stage-13 callees that drew them (exclusive + inclusive, per 2 s window), optionally naming what `func_80032588` was asked to draw.
- Hooks: `pre`+`post` on each of 22 `game` routines in `Routines[]` (`AddPre`/`AddPost`, `Commit`); optional `pre` on `game`/0x80032588 (`ModelSubmit`) when `KF2_DRAWCENSUS=2`.
- Data: ActiveDescriptor 0x8017E0A4 -> arena `{start,end,current}`; `ModelSubmit` args A0=model, A1=rot, A2=position (three u16 at +0/+4/+8); EyeX/Y/Z 0x80192E78/7C/80; PlayerX/Y/Z 0x801994EC/F0/F4. Routines table: 0x800342D8 renderer, 0x8002E22C/0x8002DC78/0x800353AC/0x80015374 head a-d, 0x8002D3A8 cull build, 0x8002E064 OT swap+clear, 0x80032400 first-person arm, 0x80031D5C HUD, 0x80033E78 overlay a, 0x80031C94 map tiles, 0x800331B4 world+object walks, 0x80032588 geometry submit, 0x80032AC4 object submit, 0x8003309C/0x80032FAC walk callees, 0x8003202C/0x800320BC/0x8003214C/0x80032234/0x8002E0FC/0x8003549C remaining stage-13 callees. Primitive conversion uses POLY_GT4 size 0x34.
- Structs: arena descriptor `{start,end,current}` (two 0x19000-byte buffers 0x800FC99C/0x8011599C, swapped by 0x8002E064); call stack of 32 frames `(slot, desc, entry, children)`; model record at A2 as 3 u16.
- Control flow: bytes = bump in `current` across a routine; exclusive vs inclusive by nesting; frame boundary on slot 0, not depth 0 (0x80015374 is called from outside stage 13); arena-swap handled by comparing descriptors. Measurement only, never writes.
- Overlays: game.
- Env / settings keys: `KF2_DRAWCENSUS=1|2`; no settings key.
- RecompOne deps: `HookManager`, `SymbolRegistry`, `Event`/`OverlayLoadedEvent`, `ModInfo`, `CpuContext`, `IMemory`.
- Mod-visible: `internal static readonly (uint Addr,string What)[] Routines`; `public static void Configure/Install`, `public static void BeforeModel/PreXX/PostXX`.
- Split note: the arena-bump attribution and the hook-pair boilerplate are generic; the 22-address table, the two arena buffers and the model/eye/player addresses are KF2.

#### patches/EndingHold.cs  (136 lines)
- Bucket: D (high) -- END.EXE's own ending routine and the SLUS_001.58 boot-stub loader addresses.
- Does: keeps presenting "The End" after END.EXE's main hits its `while(1);` spin, and on any button re-enters the boot stub to reload OPEN.EXE.
- Hooks: `post` on `end`/0x80011CC4 (movie player `func_80011CC4`), attached on the first `OverlayLoadedEvent`; resets `_played` whenever overlay `end` loads.
- Data: `MoviePlayer` 0x80011CC4; boot stub `StubMain` 0x80010038, `StubIndex` 0x80010268 u32, `StubNext` 0x800102F0 u8; `TitleIndex` 0 (OPEN.EXE, filename table at 0x80010254). Reuses `func_800119A4`'s real spin at 0x80011A50.
- Structs: 3-entry boot-stub filename table (0 OPEN, 1 GAME, 2 END).
- Control flow: after the second movie return it never returns; loops `LibEtc.VSync(0)`; a button down after a release breaks and `Dispatcher.Call(StubMain)` restarts the stub, which re-reads the index.
- Overlays: end, main (boot stub SLUS_001.58).
- Env / settings keys: `KF2_ENDINGEXIT` (=`0` keeps the pure hang); no settings key.
- RecompOne deps: `HookManager`, `SymbolRegistry`, `Event`/`OverlayLoadedEvent`, `ModInfo`, `LibEtc.VSync`, `Controller.State`, `Dispatcher.Call`, `CpuContext`, `IMemory`.
- Mod-visible: `public static bool ExitToTitle`, `public static void Configure/Install`, `public static void AfterMovie`.
- Split note: "keep presenting a hung frame and offer an exit" generalizes; every address, the file-name table and the END.EXE spin are this disc.

#### patches/EnhancementDistance.cs  (52 lines)
- Bucket: B (high) -- maps a slider/env value to one runtime renderer field; nothing game-specific but the key names.
- Does: sets `GteDepth.PlainDepth` so past N tiles of view depth surfaces draw the game's own way (affects per-pixel lighting, authored light, filtering, ripples, occlusion, reflections; perspective/sub-pixel/Z stay on).
- Hooks: none; `RuntimeReadyEvent` reads the saved slider.
- Data: none.
- Structs: none.
- Control flow: `Set` clamps (<Min or >=Max => 0 = everywhere), writes `PlainDepth = Tiles * 2048f`.
- Overlays: none.
- Env / settings keys: `KF2_ENHANCEDIST=<tiles>`; settings `kf2.enhancedistance` (`Key`). Slider range Min 2 / Max 16.
- RecompOne deps: patch `0083` (`GteDepth.PlainDepth`); `Runtime.View`, `RuntimeReadyEvent`.
- Mod-visible: `public const string Key`, `public const float Max/Min`, `public static float Tiles`, `Set/Configure/Install`.
- Split note: the "distance at which enhancements fade to native" is generic; the env prefix and key are the only KF2 bits.

#### patches/EvenFog.cs  (87 lines)
- Bucket: C (high) -- "blend fog and lighting between map records at tile edges" generalizes; it hooks a KF2 tile routine and relies on the port's `PolyAssembler`.
- Does: three parts under one switch -- clipped map tiles fogged on the tiles' curve (not the half IR0), each tile vertex's fog blended between surrounding light records, and the records' colour matrix/back colour blended the same way.
- Hooks: `pre`+`post` on `game`/0x80031950 (`TileSubmit`, `func_80031950`, one tile half) via `HookAttach.OnOverlayLoad`; delegates to `PolyAssembler.BeginTile`/`EndTile`.
- Data: `TileSubmit` 0x80031950; no RAM addresses directly.
- Structs: none directly (tile/record layout lives in `PolyAssembler`).
- Control flow: only runs from the C# assembler, so Fast geometry must be on; stands down under `KF2_POLYASM=verify`; both halves or nothing.
- Overlays: game.
- Env / settings keys: `KF2_EVENFOG`, `KF2_EVENFOG_BLEND`, `KF2_EVENLIGHT`; settings `kf2.evenfog.on` (`OnKey`).
- RecompOne deps: patch `0049`; `HookManager`, `HookAttach`, `SymbolRegistry`, `ModInfo`, `CpuContext`, `IMemory`, port `PolyAssembler`.
- Mod-visible: `public static bool Enabled/Blend/Light`, `public const string OnKey`, `Configure/Install`, `public static void BeforeTile/AfterTile`.
- Split note: the edge-blending idea is generic; the tile routine address, the records it reads and the assembler being hooked are KF2.

#### patches/FluidSmoothing.cs  (234 lines)
- Bucket: C (high) -- "interpolate a scrolling-texture phase between ticks" generalizes; slots, stride and the DrawOTag address are KF2.
- Does: publishes the animated textures' dest rects and a fractional V shift just before the ordering-table walk, so water/fire/creature skins scroll at the render rate instead of the tick rate.
- Hooks: `pre` on `game`/0x80060818 (`DrawOTag`) via `HookAttach.OnOverlayLoad`; cleared / disabled on failure.
- Data: `Slots` 0x80192D58, `Count` 8, `Stride` 0x18; per slot: +0 type==1 live, +4 s16 scroll phase, +6 destX, +8 destY, +A destW, +C destH (all s16). `DrawOTag` 0x80060818. Shift fed to `GteDepth.Fluid[n]` `{X,Y,W,H,Off}` with `GteDepth.FluidN`/`FluidLive`.
- Structs: 8 slots x 0x18.
- Control flow: only when `FramePacing.Extrapolating`; samples on `FramePacing.FirstWalkOfTick` (prev/cur phase, delta modulo dest height); `Off = (1 - LogicPhase) * delta`; interpolate-not-extrapolate.
- Overlays: game.
- Env / settings keys: `KF2_SMOOTH_FLUID`, `KF2_SMOOTH_FLUID_PROBE`; settings `kf2.smoothing.fluid` (`OnKey`).
- RecompOne deps: patch `0054` (scaled-atlas blit removed); `GteDepth` (Fluid/FluidN/FluidLive), `HookManager`, `HookAttach`, `SymbolRegistry`, `Event`, `ModInfo`, `CpuContext`, `IMemory`.
- Mod-visible: `public const string OnKey`, `public static bool Enabled`, `Configure/Install/SetEnabled`, `public static void BeforeDrawOTag`.
- Split note: matching the blend to the dest RECT (so every assembler is covered) is the generic seam; the slot table address/stride and the game DrawOTag hook are KF2.

#### patches/FrameCapture.cs  (1619 lines)
- Bucket: C (high) -- capturing a renderer's GP0 stream and replaying it on a detached software GPU is generic; the routine set, stage-13 tree and arena are GAME.EXE's.
- Does: captures one whole run of stage 13 -- every GP0/GP1 word, every routine enter/leave, every GL batch submit and GPU timer query -- then analyses it with a full software-GPU replay (fragments, overdraw, per-routine attribution, batch costs, vertex-map recovery) and writes it as CSV.
- Hooks: `pre`+`post` on up to 32 `game` routines (slots seeded from `DrawCensus.Routines`, plus `game`/0x80060818 `DrawOTag` and `game`/0x8005FCC8 `VSync thunk`); `AddFunction` hooks any `game:` address. Also installs `GpuTrace.Sink` (`IGpuTrace`) and `Profiler.Trace` (`IProfileTrace`) for the captured frame.
- Data: ActiveDescriptor 0x8017E0A4 -> arena `{start,end,current}`. `GteVertexMap` trace counters (`Stores`, `TraceScans/Bound/Published/Republished`), `GteDepth.OtEntry`/`OtSlot`, `GteVertexMap.Active`, `Runtime.RamSize`. `GteDepth.Enabled/Subpixel/ZBuffer/AmbientOcclusion/AoSamples/Anisotropy/TrueColor`, `Pgxp.Pgxp.Enabled`, `GlVram.Scale` are read for the state line.
- Structs: `Ev`/`SecEv`; `CapturedCmd` (arena `First/Count`, `Src` guest address of packet header, `OtEntry/OtSlot`, `Owner`+`OwnerByTime`+`RunIn`, `Batch`/`Flushes`/`Reason`/`Frags`, `VtxAsked/VtxHits/LookupTicks`, `GpuNs`, draw clip/offset, decoded `Kind/Op/Tex/Semi/Gouraud/Quad`); `CapturedCall` (slot, parent, depth, enter/leave/self, arena `RangeLo/Hi`, packets, walk ticks, fragments, `Vtx`); `CapturedSection`; `CapturedWork`; `CapturedFlush`; `SectionStat`; `SlotStat`; `VtxCounts`; `Capture` (Start `PsxGpu`, words/cmds/calls/flushes, `ViewL/T/W/H`, overdraw histogram, per-slot stats). Arena descriptor is `{start,end,current}`; packet header is at `Src-4`, RAM masked by `Runtime.RamSize`; POLY decode uses 11-bit x/y words, sprites size codes, fill sizes.
- Control flow: `Enter(0)` arms on the next stage-13 run (`_armed`, optional 1-frame skip so new hook JIT doesn't land in the capture); `Begin` copies GPU state, reads the GL VRAM back, starts the clock and trace sinks; `End` at `Leave(0)` marks, builds the `Capture` (call stack, by-time and by-arena-place ownership, batch boundaries), then resolves GPU timers over the following presents (`TailSink`, `TryResolve`, `FinishResolve`). One run of stage 13 per capture; the panel and `KF2_FRAMEVIEW_OUT` consume it.
- Overlays: game (`DrawCensus.Routines`, stage 13, DrawOTag, VSync thunk all GAME.EXE).
- Env / settings keys: `KF2_FRAMEVIEW=panel|1`, `KF2_FRAMEVIEW_CAPTURE=<seconds,...>`, `KF2_FRAMEVIEW_OUT=<dir>`; no settings key. Shift+F toggles the panel.
- RecompOne deps: patches `0039` (detached-instance VRAM readback / restore copies), `0042` (present counters), `0045` (Profiler), `0084` (GPU frame timers). Runtime types: `RecompOne.Runtime.Gpu`/`PsxGpu` (`WriteGp0/WriteGp1`, `CopyStateFrom`, `Vram`, `Coverage`, `Owner`, `Fragments`, `CoverTag`), `GteDepth`, `GteVertexMap`, `GpuHle`/`GlCore` (`ReadVram`, `GpuTimeNs`, `DeleteGpuTimer`, `TimerQueries`), `GpuTrace`/`IGpuTrace`/`FlushReason`/`GpuWork`, `Profiler`/`IProfileTrace`/`ProfileGroup`, `GpuJobs`, `Runtime.Gpu`/`Runtime.RamSize`, `Host.Window.PanelManager`/`Localization`/`PopupManager`/`HostWindow`, `Config.ConfigManager`, `ModInfo`, `CpuContext`, `IMemory`.
- Mod-visible: `public static class FrameCapture` (`MaxSlots`, `SlotName/SlotAddr/SlotCount`, `Armed`, `HoldMapOff`, `Latest`, `Count`, `Arm`, `AddFunction`, `SlotLabel`, `PreXX/PostXX`), and the public value types `CmdKind`, `CapturedCmd`, `CapturedCall`, `CapturedSection`, `CapturedWork`, `SectionStat`, `VtxCounts`, `CapturedFlush`, `SlotStat`, `Capture`, `Replayer`.
- Split note: the capture/replay/attribution engine and all `Captured*` structs are generic; `DrawCensus.Routines`, the stage-13/arena addresses and `GteDepth`/`GteVertexMap` being the port's renderer state are KF2.

#### patches/FramePacing.cs  (1433 lines)
- Bucket: C (high) -- "fixed timestep + a render-rate floor + a frame boundary at present" is a general mechanism; every stage address, the gate and the tick rate are GAME.EXE's.
- Does: chooses the render rate, removes the game's own 2-vblank frame gate, holds the world to a fixed 20 Hz logic clock by gating the per-tick main-loop stages, and paces the picture itself at a frame boundary.
- Hooks: `post` on per-overlay libgpu `DrawOTag` (`open` 0x80016078, `game` 0x80060818, `end` 0x80013D80) = the frame boundary; `pre` on per-overlay libetc `VSync` thunks (`open` 0x8001EB88, `game` 0x8005FCC8, `end` 0x8001B154) = counts presents per frame; `pre` on `game`/0x80017880 (`FrameGate`) = always skips the gate (returns false, zeroes VBlankCredit); `pre` on each gated stage (`BeforeStage`, returns false to skip a non-tick frame); `FullRateLogic.Attach`. All via `HookAttach.OnOverlayLoad("pacing", ...)`, `AddPre/AddPost/AddReplace`, then `HookManager.Commit` and `HookAttach.Installed` verification with retry on later overlay loads.
- Data: FrameGate 0x80017880; VBlankCredit 0x801B6CA8 u32 (zeroed when skipped); per-overlay `DrawOTag`/`VSync` address tables above. Gated stages: 0x80037C0C, 0x8002A550, 0x80040348, 0x80046A60, 0x8004910C, 0x80033FBC, 0x8002DC78. `KF2_FPS_GATE` accepts `overlay:hex+hex`? No -- addresses as bare hex joined by `+` in `game`.
- Structs: none of the game's; internal accumulator/credit state (`_logicCredit`, `_logicClockMs`, `_tickThisFrame`, `_due`, `_vsyncCalls`, `_frames`, `_lastBoundaryMs`). Stage data layouts are documented in comments only (object table 0x80177714 stride 0x44 x396; entity table 0x8016C544 x200; effects 0x8019CC6C x128; area module dispatch `*(u32*)(*(u32*)0x8017E068 + 4)` slot 1; fade bytes 0x80192D42/43/44; texture slots 0x80192D58 stride 0x18).
- Control flow: **the main loop and the frame boundary, in full** -- see the dedicated note below the entry fields.
- Overlays: open, game, end (VSync thunk + DrawOTag per overlay); stage gating and the frame gate are `game` only.
- Env / settings keys: `KF2_FPS` (number or `off`), `KF2_TICKRATE`, `KF2_FPS_LOGIC=full`, `KF2_FPS_GATE=<hex+...>`, `KF2_FPS_PROBE=1`; settings `kf2.framepacing.fps` (`FpsKey`) and legacy `kf2.framepacing.vblanks` (`VBlankKey`, migrated once). `kf2.framepacing.logichz` is deliberately no longer read.
- RecompOne deps: patches `0021` (emulated vblank on a wall-clock 60 Hz grid), `0025` (settable `FrameClock`/`Runtime.TargetFps`), `0007` (host input polled inside `VSync`), `0042` (`LibEtc.VSyncCalls`), `0045` (`Profiler` for the floor wait). Runtime types: `RecompOne.Runtime.Runtime.TargetFps`/`View`, `LibEtc.VSync/VSyncCalls/CapturedStack/CaptureNextStack`, `LibGpu.AutoPresents`, `Controller`/`Pads` state, `HookManager`/`HookAttach`/`SymbolRegistry`, `Event`/`RuntimeReadyEvent`/`VSyncEvent`/`OverlayLoadedEvent`, `ModInfo`, `CpuContext`, `IMemory`, `Diagnostics.Profiler`, `LoopPacing`, `FrameSmoothing`/`ObjectSmoothing`/`AnimSmoothing`, `FullRateLogic`, `LoadPacing`, `SpriteAnim`.
- Mod-visible: `public static double LogicHz/TargetFps`, `Measured`, `Enabled`, `LogicPhase`, `TickedThisFrame`, `StagesWillRun`, `Frames`, `Paused`, `Gating`, `Extrapolating`, `Cap`, `Logic` enum + `LogicMode`, `FirstWalkOfTick(ref long)`, `PauseWhen(Func<bool>)`, `ExcuseBoundaryGap`, `Configure/Install/SetTargetFps/SetLogicHz/SetCap`, `BeforeFrameGate/BeforeVSync/AfterDrawOTag/BeforeStage`, consts `DefaultFps`/`FpsKey`/`VBlankKey`.
- Split note: the pacing scheme (logic clock, per-stage gate, boundary-after-present, watchdog) is generic and is the part a sibling game would reuse; the specific stage addresses, the 2-vblank gate and the 20 Hz judgement are KF2.

**FramePacing's model of the game's main loop (Control flow, in full).** The port models `GAME.EXE` as a loop of numbered stages, each a function run once per loop iteration, with stage 13 the renderer:
1. `func_8002C944` stage 1 (screen tint clear); 2. `func_80037C0C` object state machine (396 slots x 0x44 at 0x80177714, type byte at rec+0x4, 224-entry jump table at 0x8001191C; doors/drawbridge/minecart/crystals, writes position vectors); 3. `func_8002A550` pad read, turn, walk, angle fold, death counter 0x8019951A, poison tick, buff timers 0x80199472..82, global frame counter 0x80199488; 4. `func_80040348` 200-record entity table 0x8016C544 (AI one in four off 0x80175908&3); 5. `func_80046A60` 128 effect/projectile slots 0x8019CC6C (lifetime at +0x0E); 6. `func_8004910C` area module per-frame entry, module header slot 1 via `*(u32*)(*(u32*)0x8017E068+4)` (fdat11/14/20 do proximity/trigger work); 7. `func_8001689C` area loader; 8. `func_80025A1C` build render camera from player state (the hook `FrameSmoothing` uses); 9. `func_800140AC` 3D sound listener; 10. `func_8002CA74`; 11. `func_80016FC8`; 12. `func_80014534`; 13. `func_800342D8` renderer, which calls the two gated sub-steps `func_80033FBC` (fade state machine, bytes 0x80192D42/43/44) and `func_8002DC78` (animated-texture updater, 8 slots at 0x80192D58), and ends by calling the game's frame gate `func_80017880` and drawing one ordering table after a `VSync(0)`.
- **Which stages are gated** (run only on a tick, default set `DefaultGate`): 2, 3, 4, 5, 6 (the per-tick world/game state) plus 13's `func_80033FBC` fade and `func_8002DC78` texture updater. Never gated: stage 13 itself and every drawing stage. `KF2_FPS_GATE` replaces the whole set (any `game` address). The rule for including a stage: its entire subtree must contain no `DrawOTag`/`VSync`/`PutDispEnv`/`PutDrawEnv`; the two recorded exceptions are stages 2 and 3, which reach stage 13 only as *extra* renders inside their own modal loops (`func_80037B5C` transition fade, `func_80018E80` menu, `func_80022DC4` blip), so skipping them defers the loop entry by at most one tick and never cuts a frame.
- **The tick clock**: `AdvanceLogicClock` runs at every frame boundary. `dt = now - _logicClockMs`; `_logicCredit += dt*LogicHz/1000` clamped at 2; `_tickThisFrame = _logicCredit >= 1` and one tick is spent. A first sample or `dt > 250 ms` (the game stopped drawing -- disc read, module swap) restarts the credit at 1 instead of running the world forward. When paused the credit is zeroed and the clock carried to now. In `Logic.Full` (`KF2_FPS_LOGIC=full`) there is no gating: `_tickThisFrame` is always true. `LogicPhase = clamp(credit,0,1)` (0 when paused or not gating) is the fraction `FrameSmoothing`/`FluidSmoothing` draw at; `TickedThisFrame` is stable for the whole frame.
- **The frame boundary**: `AfterDrawOTag` fires after a libgpu `DrawOTag`. A frame ends only where a `DrawOTag` follows one or more `VSync` calls since the last boundary (`_vsyncCalls`, set by the `BeforeVSync` pre-hook, the presenter's `func_8002E0FC` VSync(0) being the one per frame once the gate is gone); a second OT with no VSync between is charged to the frame already in flight. If no VSync thunk could be hooked (`_boundaryNeedsVSync` false) every `DrawOTag` is a frame. At the boundary: `_frames++`, `_lastBoundaryMs = now`, fps window update, `AdvanceLogicClock`, then `Floor(min)` sleeps/spins until `_due` (`min = LoopPacing.FrameMinMs(Enabled, TargetFps)`: 1000/TargetFps for an ordinary frame, 0 when nothing should wait). `_due` is absolute so overruns are paid from the next frame instead of drifting.
- **The game's own gate** is skipped at every rate (`SkipFrameGate` is always true): `BeforeFrameGate` returns false and zeroes `VBlankCredit` so the spin is bypassed. Stage 13's call is also the signal `LoopPacing.WorldDrawn()` (a real world picture vs. an interface one). The title/ending are left alone -- they are CD-bound and have no world to tick.
- **Watchdog**: if no boundary has arrived for `BoundaryDeadMs = 500 ms`, `BeforeStage` calls `FallbackTick`, which runs its own absolute `LogicHz` grid off the wall clock, latches pause, sets `_tickThisFrame`, and `Floor`s the loop; one decision is held for `FallbackHoldMs` (~a quarter tick) within a main-loop iteration, keyed on `LibEtc.VSyncCalls` so it does not depend on the lost hook. `ExcuseBoundaryGap` lets `LoadPacing` excuse the disc-wait window that legitimately draws nothing.
- **Pause** is the stage gate held shut (`PauseWhen` predicates OR together, latched once a frame); stage 13 still draws, so overlays sit on a live frame. Rates: `LogicHz` 20 default (KF2_TICKRATE 5..60), `TargetFps` 60 default (`KF2_FPS`, 0/off uncapped); `Extrapolating = Gating && (!Enabled || TargetFps > LogicHz)`. RecompOne's own `Runtime.TargetFps` is set as a permissive host ceiling (`max(60, TargetFps*2)`, or 0 uncapped), not as the pacer.

#### patches/FrameProfiler.cs  (406 lines)
- Bucket: C (medium) -- the profiler frontend/CSV/report is generic, but `Known[]` is a table of KF2 addresses and `stages` means the game's stages.
- Does: the port's half of the runtime frame profiler -- console summary every 5 s, per-frame CSV, spike lines, empty-pre-hook probes that turn any recompiled function into a section, and the Shift+P panel.
- Hooks: no game hooks directly; `AddProbe` installs an empty `pre` on a recompiled function so `HookManager.Invoke` times it (`HookAttach.OnOverlayLoad("profile", ...)` + `AddProbes`, `stages` = every `Known` row labelled `stage *`). Reads `KeyboardEvent` for Shift+P.
- Data: `Known[]` labels `game` 0x8002C944 stage 1, 0x80037C0C stage 2, 0x8002A550 stage 3, 0x80040348 stage 4, 0x80046A60 stage 5, 0x8004910C stage 6, 0x8001689C stage 7, 0x80025A1C stage 8, 0x800140AC stage 9, 0x8002CA74 stage 10, 0x80016FC8 stage 11, 0x80014534 stage 12, 0x800342D8 stage 13, 0x80033FBC fade stepper, 0x8002DC78 animated textures, 0x80017880 frame gate, 0x80018E80 in-game menu loop, 0x80037B5C transition fade loop, 0x80060818 DrawOTag, 0x8005FCC8 VSync thunk; `open` 0x80016078 DrawOTag / 0x8001EB88 VSync; `end` 0x80013D80 DrawOTag / 0x8001B154 VSync.
- Structs: none; CSV columns `frame,time_ms,section,group,self_ms,incl_ms,calls` plus frame pseudo-sections (total, gc_pause, alloc_kb, jit).
- Control flow: recording follows the panel/env/CSV/spike; `UpdateEnabled` at `RuntimeReadyEvent`; frame completion via `Profiler.FrameCompleted`; GPU times via `GpuFrames` (0084). Must run on the game thread (panel draws there).
- Overlays: labels name open/game/end addresses, but no execution depends on the overlay.
- Env / settings keys: `KF2_PROFILE=1|panel|on`, `KF2_PROFILE_OUT=<csv>`, `KF2_PROFILE_SPIKE=<ms>`, `KF2_PROFILE_FUNCS=stages|overlay:hex+...`; no settings key.
- RecompOne deps: patch `0045` (frame profiler), `0084` (GPU timers). Runtime types: `Diagnostics.Profiler` (`Register`, `FrameCompleted`, `SetLabel`, `FunctionName`, `Group`, `DisplayName`, `Name`, `Enabled`, `TicksToMs`, `Trace`, `MaxSections`, `SectionCount`, `Frame`/`Sample`, `Ao`/`Ssr`/`Composite`/`Display`/`GlFlush` ids), `GpuTimes`/`GpuFrames`, `Config.ConfigManager`, `Localization`, `Host.Window.PanelManager`/`PopupManager`/`HotkeyGate`/`HostWindow`, `Silk.NET.Input.Key`, `HookManager`/`HookAttach`/`SymbolRegistry`, `ModInfo`, `CpuContext`, `IMemory`.
- Mod-visible: `public static int FloorWait/MenuWait/LoadWait`, `public const string CsvHeader`, `public static bool KeepRecording/Paused`, `Configure/Install/UpdateEnabled/AddProbe/AddProbes/WriteCsv/Probe/Top`.
- Split note: the profiling frontend and `AddProbe`/CSV machinery are generic; the `Known[]` address/label table (and hence the `stages` shortcut) is KF2.

#### patches/FrameSmoothing.cs  (899 lines)
- Bucket: C (high) -- "interpolate the view and HUD readings between logic ticks" generalizes; stage 8, the player-state addresses and the 12-bit conventions are KF2.
- Does: draws the camera (and optionally position, head bob/landing) as `lerp(prev tick, cur tick, LogicPhase)` by writing the composed view globals before stage 8 and restoring them after, and carries the compass needle and HP/MP gauge lengths the same way around the HUD builder; also lets the mouse lead the tick.
- Hooks: `pre`+`post` on `game`/0x80025A1C (`CameraCopy`, stage 8) -- both or neither (`_paired`); `pre`+`post` on `game`/0x80031D5C (`HudBuilder`) for the needle/gauges. `HookAttach.OnOverlayLoad("smoothing", ...)`, `Commit`, `Installed`.
- Data: CameraCopy 0x80025A1C; ComposedPitch 0x80199504, ComposedYaw 0x80199506 (u16, 12-bit wrapped), PosX/Y/Z 0x801994EC/F0/F4 (u32, signed read), Bob 0x80199548, Landing 0x8019954C (s16); `TeleportUnits` 1024 (walk ~45 u/tick), `PitchLimit` 0x2BC (12-bit signed pitch). Mouse: `Mouse.Lead`, `Mouse.Poll`, `Mouse.Pending`, `Mouse.SpentThisFrame`, `Analog.Pitch`. HUD addresses come from `Stage13.NeedleYaw`, `Stage13.GaugeLengths[0..1]`, with `Stage13.NeedleOnTick`/`NeedleSteps`/`HudOnTick`/`HudTicks`, `Stage13.InFrame`.
- Structs: `TickPair` (Prev/Cur, 12-bit wrap via `Delta12` taking the short way); `HudReading` (per-record prev/cur pair, primed/carriable); three HUD readings (compass + two gauges). Angle convention 0x1000 per turn; pitch stored zero-extended/sign-extended in a u16.
- Control flow: stage 3 (`func_80028DB8` angles / `func_80028080` position) runs before stage 8, so on a `TickedThisFrame` frame the sampled pair is the new tick; on a non-tick frame a placement is detected and the pair shifted; carry only when `FramePacing.Extrapolating`; the carry exists for exactly one stage-8 call and is restored in `After`. Position carry is knowingly inconsistent with `func_80032400`/`func_800331B4`, which read the triple after stage 8.
- Overlays: game.
- Env / settings keys: `KF2_SMOOTH`, `KF2_SMOOTH_POS`, `KF2_SMOOTH_COMPASS`, `KF2_SMOOTH_GAUGES`, `KF2_SMOOTH_PROBE` (=1 or 2); settings `kf2.smoothing.on` (`OnKey`), `kf2.smoothing.pos` (`PosKey`).
- RecompOne deps: `FramePacing` (`Extrapolating`/`TickedThisFrame`/`LogicPhase`/`FirstWalkOfTick`/`TakeHealth`/`Frames`), `Stage13`, `Mouse`, `Analog`, `HookManager`/`HookAttach`/`SymbolRegistry`, `Event`/`RuntimeReadyEvent`/`OverlayLoadedEvent`, `ModInfo`, `CpuContext`, `IMemory`.
- Mod-visible: `public static bool Enabled/Position/Compass/Gauges`, consts `OnKey`/`PosKey`, `public static long Frames/Carries/OffTickMoves`, `public static int LastVerdict`, `Configure/Install/SetEnabled/SetPosition/Before/After/BeforeHud/AfterHud/TakeHealth`.
- Split note: the interpolation/hud-carry mechanism and `TickPair` are generic; stage 8 being the only camera copy, the player-state addresses, the HUD record layout and the 12-bit angle domain are KF2.

#### patches/FrameViewerPanel.cs  (999 lines)
- Bucket: C (high) -- an ImGui scrubber over a `Capture`; generic UI, but it names `FrameCapture` slots and the GAME.EXE-derived capture.
- Does: the frame viewer window (Shift+F): loads the latest `Capture`, draws a stage-13 timeline (calls, commands, GL submits, runtime sections), a scrubbable soft-GPU replay of the frame with six overlay modes, and tables for routines, sections, commands and batch submits.
- Hooks: none of its own (the capture hooks are `FrameCapture`'s); registers an `IPanel` and a `KeyboardEvent` Shift+F toggle.
- Data: reads `FrameCapture.Latest`, `FrameCapture.SlotName/SlotAddr/SlotLabel`, `Capture` fields, `Profiler.*`, `GteVertexMap`/GteDepth indirectly through the capture only; VRAM coordinates via `RecompOne.Runtime.Gpu.VramWidth/VramHeight`.
- Structs: consumes `Capture`, `CapturedCmd`, `CapturedCall`, `CapturedSection`, `CapturedFlush`, `SlotStat`, `SectionStat`, `VtxCounts`; `Replayer` (own `Coverage`/`Owner` arrays).
- Control flow: `Draw` on the ImGui thread; `Replayer.Seek` replays up to the cursor (reset and re-step when moving backwards, checker option); `UpdateTexture` uploads the replay's VRAM view to a GL texture; mode switch picks Picture/Overdraw/Batches/Routines/Vertex-recovery/GPU-cost. It just displays; holds no game state.
- Overlays: none directly (the captures it shows are GAME.EXE's stage 13).
- Env / settings keys: none (panel enabled via `KF2_FRAMEVIEW`/Shift+F).
- RecompOne deps: `Diagnostics.Profiler`/`ProfileGroup`, `Hle.GpuGlAccess`/`GpuWork`, `Host.Window.IPanel`, `Silk.NET.OpenGL`/`Silk.NET.Input`, `Gpu` VRAM constants, plus all `FrameCapture` public types. Patch `0045`/`0084` supply the sections/GPU columns.
- Mod-visible: `public sealed class FrameViewerPanel : IPanel` with `public static readonly Instance`, `Name`, `TitleKey`, `IsOpen`, `Draw`.
- Split note: the scrubber UI and replay-picture modes are generic; the slot table, stage-13 timeline and the `Capture` it renders are KF2.

### Summary

Counts (13 files): **A 0, B 3, C 9, D 1.**
- B: `Differential.cs` (verify harness), `EnhancementDistance.cs` (slider -> one runtime field), `DesktopEntry.cs` (Wayland icon/entry writer).
- C: `CullGrid`, `DrawCensus`, `EvenFog`, `FluidSmoothing`, `FrameCapture`, `FramePacing`, `FrameProfiler`, `FrameSmoothing`, `FrameViewerPanel`.
- D: `EndingHold.cs`.

Most important observations:

1. **One cluster, one engine.** `FramePacing`, `FrameSmoothing`, `FluidSmoothing`, `FrameProfiler`, `FrameCapture`/`FrameViewerPanel`, `DrawCensus` and `EvenFog` all hang off the same idea: a 20 Hz logic clock in `FramePacing` whose stages are gated per function, with a frame boundary defined as "a `DrawOTag` that follows a `VSync` call" on three per-overlay address pairs. That clock (`LogicPhase`/`TickedThisFrame`/`FirstWalkOfTick`/`Extrapolating`) is the single reusable mechanism; a sibling port would rename `FramePacing` and supply its own stage list, `DrawOTag`/`VSync` addresses and (for the game's own gate) one skip hook.
2. **The gate list is the game's, and it is documented inline.** `DefaultGate` (7 addresses) plus the "cannot draw" rule and the two recorded exceptions (stages 2 and 3 reach stage 13 through modal loops) is the KF2-specific part of `FramePacing`; everything else (accumulator, watchdog, floor, sentinel, pause) is generic.
3. **Two generic `Differential`-style oracles carry all the verify modes.** `Differential.cs` (`TileWalk`/`ModelWalk`/`PolyAssembler`/`Stage13` verify) is pure infrastructure; `DrawCensus` and `FrameCapture` independently re-derive the same arena-descriptor bookkeeping (`0x8017E0A4` -> `{start,end,current}`, two `0x19000` buffers swapped by `func_8002E064`). That arena-attribution method is the most reusable KF2-side trick.
4. **`FrameCapture` is the most mod-visible surface by far.** `Capture`, `Replayer`, `CapturedCmd/Call/Section/Work/Flush`, `CmdKind`, `SlotStat`, `VtxCounts` are all `public`, and the routine/pair boilerplate (`Pre00..Pre31`) is deliberately public. A sibling project could lift the capture+replay+reporting with a different `SlotName`/`SlotAddr` seed.
5. **`FrameProfiler` and `FrameCapture` both key off the same `Known`/`Routines` KF2 address tables**, toward different ends (labels/probes vs. hooked slots). Both would need new tables but no new mechanism.
6. **`DesktopEntry` and `Differential` are the only clean-B files** that touch neither RAM nor code addresses; `EnhancementDistance` is B only because its one runtime field lives in the renderer -- it is the seam between "generic slider" and "KF2 renderer".
7. **`EndingHold` is the lone D**: it is welded to `END.EXE`'s `func_80011CC4`, the `SLUS_001.58` boot-stub loader loop and its filename/selector bytes, and the game's own quit-to-title path. The only portable idea ("keep presenting a spin, offer an exit") is already general enough to be re-derived rather than shared.
8. **Address conventions to carry across if anything is shared:** camera/player block is 0x80192E78..0x80192EA4 (pitch/yaw at +0x10/+0x12); player state 0x801994EC..0x8019954C; the two arena descriptors and `0x19000` buffer size; tile grid legacy 0x80192EAC; map records `0x801C8484 + area + 800*z + 10*x` with `0xFF` empty / `0x80` alive. All would differ in a sibling game.
9. **The KF2 patch numbers these files lean on** are `0021` (60 Hz vblank grid) and `0025` (settable frame clock) for pacing; `0042` (present counters); `0045` (profiler); `0084` (GPU timers) for the two diagnostics; `0054` (1x VRAM sample) for fluid smoothing; `0083` (`PlainDepth`) for enhancement distance; `0049` for even fog. No file is A (a RecompOne fork change) -- they are all consumers of the vendored runtime.

## patches/, FullRateLogic to MapRender
Files: FullRateLogic.cs, GearCompare.cs, GpuFrames.cs, GpuWorld.cs, GpuWorldCensus.cs,
HitGuard.cs, HookAttach.cs, HotkeyGate.cs, KeyLayout.cs, LoadPacing.cs, LoopPacing.cs,
Map.cs, MapFog.cs, MapFullscreen.cs, MapMarkers.cs, MapOverlay.cs, MapPanel.cs, MapRender.cs.

#### patches/FullRateLogic.cs  (114 lines)
- Bucket: C (high) -- "run every stage at render rate and scale movement deltas" is a general frame-rate mechanism, but the addresses and per-tick counters it names are KF2's.
- Does: comparison mode (`KF2_FPS_LOGIC=full`) that multiplies the frame's walk speed and turn rate by `LogicHz/TargetFps` so per-frame movement is scaled down instead of ticking the world.
- Hooks: `0x80028DB8` (stage 3 turn/look; `SymbolRegistry.Resolve("game", …)`), mode `pre` (`HookManager.AddPre`); implementation `Before(CpuContext, IMemory)`.
- Data: `0x80199558` u32 this-frame walk speed (base 0xC8, run ramp at 0x80199422, halved by 0x801994B4); `0x8019955C` u32 this-frame turn rate (0x1C moving / 0x23 standing); both written early in stage 3 body at 0x8002A6E4/0x8002A724, modified to 0x8002A8CC, then consumed by `func_80028DB8` then `func_800290D4`.
- Structs: none.
- Control flow: assumes stage 3 overwrites the two words every frame before the turn/walk consumers; scaling must sit after the writes and before the turn.
- Overlays: `game` only.
- Env / settings keys: `KF2_FPS_LOGIC=full`; reads `FramePacing.LogicMode`, `FramePacing.Enabled`, `FramePacing.LogicHz`, `FramePacing.TargetFps`.
- RecompOne deps: `SymbolRegistry`, `HookManager`, `CpuContext`, `IMemory`, `ModInfo`. Mechanism shared with `mods/kf2debug` speed multiplier.
- Mod-visible: `public static class FullRateLogic`, `Scale`, `Before`.
- Split note: generic seam is `Before`/`Apply` (scale a rate word); KF2-specific are the two addresses and the hook target.

#### tools/verdite-core/src/GpuFrames.cs  (114 lines)
- Bucket: A/B (high) -- pure host-side bookkeeping of GPU timer queries per profiler frame; no game RAM, addresses or assets.
- Does: attributes async GPU timer-query results (a few frames late) to the profiler frame that issued them, keeping per-pass GPU ms and streaming CSV rows.
- Hooks: none (subscribes `GpuTimes.Resolved`).
- Data: none (runtime `GpuTimes.Issued`/`Complete`/`Enabled`/`Names`, `Profiler.Frame`).
- Structs: ring of `Profiler.HistoryFrames` slots indexed by frame index.
- Control flow: assumes everything runs on the presenting/game thread; completion when `_last <= GpuTimes.Complete`.
- Overlays: none.
- Env / settings keys: none directly.
- RecompOne deps: runtime patch/runtime type `GpuTimes` (`0084`), `Profiler`, `Profiler.Frame`, `Profiler.HistoryFrames`, `HistoryCount`, `GetFrame`, `TicksToMs`.
- Mod-visible: `public static class GpuFrames`, `Install`, `Close`, `TryGet`, `Total`, `WriteRows`, `WriteCompleted`.
- Split note: entirely generic; only the `gpu.` CSV row naming is project convention.

#### patches/HookAttach.cs  (90 lines)
- Bucket: B (high) -- attaching/retry/read-back helper; no KF2 knowledge, only a `[KF2]` log prefix.
- Does: runs a patch's attach pass on `OverlayLoadedEvent` with retry and latches on actual success (not on the queue call).
- Hooks: none (calls `HookManager.IsCommitted`).
- Data: none.
- Structs: none.
- Control flow: assumes `SymbolRegistry` is readable only after an overlay load; `MaxTries = 3`; `Event.Dispatch` swallows listener exceptions to one stderr line.
- Overlays: none (reacts to any).
- Env / settings keys: none. Log prefix `[KF2]`.
- RecompOne deps: `OverlayLoadedEvent`, `Event.AddListener`, `HookManager.IsCommitted` (`0027`, `0028`), `MethodInfo`.
- Mod-visible: `static class HookAttach` (internal), `MaxTries`, `OnOverlayLoad`, `Installed`.
- Split note: fully generic; could move to the RecompOne fork or a shared library as-is.

#### patches/HotkeyGate.cs  (17 lines)
- Bucket: B (medium) -- hotkey-suppression policy; generic except the `Remaster.Editor.Open` reference which is port-remaster-specific.
- Does: reports when port hotkeys should stand down because ImGui has a text field focused, or the remaster editor is open.
- Hooks: none.
- Data: none.
- Structs: none.
- Control flow: none.
- Overlays: none.
- Env / settings keys: none.
- RecompOne deps: `ImGuiNET.ImGui`.
- Mod-visible: `static class HotkeyGate`, `Typing`, `Editing`.
- Split note: `Typing` is fully generic; `Editing`'s dependency on `Remaster.Editor.Open` is the remaster-specific half.

#### patches/GearCompare.cs  (480 lines)
- Bucket: C (medium) -- "show a stat panel for item X before/after" is a general RPG-UI idea, but every address, stat table and menu layout is KF2's.
- Does: favours the equip/buy prompt with the status stats the candidate item would change (and computed totals), drawn in the game's font or via recompiled routines in `verify`.
- Hooks (all `game`, `SymbolRegistry.Resolve` by absolute address): `0x8001A6E8` equip page (pre `EnterEquipPage` / post `LeaveEquipPage`); `0x800206E0` equip prompt loop (pre `PromptOpens` / post `PromptCloses`); `0x80021478` prompt draw (post `AfterPromptDrawn`); shop buy pages `0x8001D6BC`, `0x8001DF5C`, `0x8001E45C` (pre `EnterShop` / post `LeaveShop`). Direct call: `Recompiled.KingsField2_game.func_800244CC(c, m)` inside `Compute`.
- Data: stat block `0x8019943C`..`0x80199468` (19 u16 stats: STR/MAG power, offense types, defense types); equip slot bytes `0x801994AF`, `0x801994D4`..`0x801994DA`; ring slot rule reads `0x801994D9`/`0x801994DA` for 0xFF empty. Item id range → slot mapping.
- Structs: stat block is a contiguous u16 array of 22 bytes; slot bytes are u8 equipment slots.
- Control flow: `func_8001A6E8(kind)` A0 picks slot; `func_800206E0(desc,5,5,id)` id in A3 (0xFF = remove); "after" computed by running `func_800244CC` with the candidate in the slot then restoring the slot and the whole 22-byte stat block; panel drawn after the prompt's own boxes. Uses `CpuContext.Snapshot`/`Restore`.
- Overlays: `game` only.
- Env / settings keys: `KF2_GEARCOMPARE` (0 / verify, on by default); settings key `kf2.gearcompare.enabled` (`Runtime.View.GetBool`); `ModInfo Id="kf2.gearcompare"`.
- RecompOne deps: `SymbolRegistry`, `HookManager`, `CpuContext`, `IMemory`, `RuntimeReadyEvent`, `Runtime.View`, `ModInfo`, `HookAttach`.
- Mod-visible: `public static class GearCompare`, `OnKey`, `Enabled`, `Configure`, `Install`, and the public hook bodies (pre/post), `EnterEquipPage`, `LeaveEquipPage`, `EnterShop`, `LeaveShop`, `PromptOpens`, `PromptCloses`, `AfterPromptDrawn`.
- Split note: the panel layout/draw (`IPen`, `NativePen`, `DrawPanel`, `DrawCompact`) and the idea generalize; the stat table, addresses, slot mapping, shop pages and `func_800244CC` are KF2. Depends on `MenuDraw` for primitives.

#### patches/GpuWorldCensus.cs  (219 lines)
- Bucket: C/D (medium) -- a measurement harness for "what 3D does the CPU still build"; the method generalizes but it hard-codes KF2 routine addresses and contexts.
- Does: counts (every 2 s / on VSync) calls into each KF2 transform/assembler routine by context (walk/mirror/arm/stage13/outside), plus CPU projections and 3D packet counts; writes nothing.
- Hooks (all `game`): stage 13 `0x800342D8` (pre/post), tile sweep `0x80031C94` (pre/post), and pre/post on `0x8002E650` transform, `0x8002E7CC` near transform, `0x8002E9B8` view-space transform, `0x8002F214` lit assembler, `0x8002EAEC` twin assembler, `0x80030540` clipped assembler, `0x8002FECC` far map assembler, `0x8002F918` sky assembler, `0x80031950` map half.
- Data: `0x8017E0A4` this frame's primitive arena descriptor; reads `desc+8` cursor (`ActiveDescriptor`). Counts from `GteDepth.Recorded`, `GtePacketDepth.Hits`, `LibEtc.VSyncCalls`.
- Structs: none of its own; tracks `_calls[9,5]`, `_bytes[9,5]` arrays.
- Control flow: context inferred from `PlanarWalk.Mirroring/Replaying`, `ModelWalk.InArm/InWalk`, `_inStage13`, `_inSweep`; report on `DispEnvEvent` and every 2 s.
- Overlays: `game`.
- Env / settings keys: `KF2_GPUWORLD_CENSUS=1`; `ModInfo Id="kf2.gpuworldcensus"`.
- RecompOne deps: `SymbolRegistry`, `HookManager`, `CpuContext`, `IMemory`, `ModInfo`, `DispEnvEvent`, `LibEtc.VSyncCalls`, `GteDepth`, `GtePacketDepth`, `HookAttach`. Cross-patch runtime deps: `PlanarWalk`, `ModelWalk`, `GpuWorld`.
- Mod-visible: `public static class GpuWorldCensus`, `Configure`, `Install`, `BeforeStage13`/`AfterStage13`/`BeforeSweep`/`AfterSweep`, all `PreN`/`PostN`.
- Split note: reporting/context plumbing generalizes; the routine address table and the "stage 13 / walk / mirror / arm" context model are KF2 renderer internals.

#### patches/GpuWorld.cs  (528 lines)
- Bucket: C/D (high) -- the GPU (retained-mode) world renderer is a mechanism that could apply to any PS1 scene, but this file is the KF2-specific switchboard, model-table reader and probe.
- Does: top-level controller for the retained-scene GPU renderer: on/off and all sub-switches, decides per frame whether the map/models/water/mirror are drawn by the backend, exposes the `gpuworld` shell verb and the 2 s probe.
- Hooks: none directly (the drawing is in `RetainedMap`/`RetainedScene`/`RetainedModels`); reads `0x8018E19C` (model bank table) in `KindOf`, and `0x8017E0A4`-style frame descriptor indirectly via retained scene. Depends on the C# tile walk / object walk / assemblers being the active path.
- Data: `0x8018E19C` model bank base (model header stride 28, +0x0C face-data pointer at +0x10, face count at +0x14); face words decode GP0 type in bits 24-31, blend-mode bits 5-6 of the u16 at face+4+6. Half id from `st[i].Flags >> HalfShift` (RetainedScene). Listens for `GteLightMap.Generations`.
- Structs: model bank records stride 28; retained static vertex/flags layout via `RetainedScene.Static`, `StaticStart/Count[5]`, `ModelInstance`.
- Control flow: map is drawn at slot 1 of the ordering table's walk; the tile walk's visited halves are noted into the frame gate, so a half is not assembled; object-walk models captured before culling; mirror is the planar walk's replay. Requires tile walk + C# assemblers + Z-buffer + perspective + Fast geometry, and `Pgxp.CpuTracking == false`.
- Overlays: `game` (model table is game RAM).
- Env / settings keys: `OnKey="kf2.gpuworld.on"` (Video ▸ Frame pacing ▸ GPU geometry); `KF2_GPUWORLD` (main), `_PROBE`, `_WATER`, `_MODELS`, `_MIRROR`, `_MESHES`, `_MESHCHECK`, `_POSES`, `_ARM`, `_BLEND`, `_SKY`, `_BLENDSURFACES`, `_TILE`, `_MIRRORBLEND`, `_CELL`, `_RECORDS`, `_RECORDCHECK`, `_POSECHECK`, `_NEAR`, `_FOGZ`; `KF2_GPUWORLD_SURFACES` (arg). Shell verb `gpuworld`.
- RecompOne deps: `PSMemory`, `Runtime.View`, `RuntimeReadyEvent`, `OverlayLoadedEvent`, `RetainedScene`/`RetainedMap`/`RetainedModels` (all port runtime), `PolyAssembler`, `Perspective`, `GteDepth`, `GteLightMap`, `ModelWalk`, `TileWalk`, `MoPose`, `PlanarWalk`, `PlanarReflections`, `BlendOrder`, `Pgxp`. Runtime type `TextureResolver`/`AssetReplacerManager` (asset replacement).
- Mod-visible: `public static class GpuWorld`, `OnKey`, `Enabled`, `Wanted`, `Active`, `MirrorActive`, `MirrorModelsActive`, `MirrorWaterActive`, `WaterActive`, `ModelsActive`, `Configure`, `Install`, `SetEnabled`, `AtFrame`, `KindOf`, `Faces`, `ModelsReady`, `Shell`, `Skipped/Kept`, `MirrorSkipped/MirrorKept`.
- Split note: the mesh extraction/decoding (`KindOf`, model-table offsets) and every switch map to a specific KF2 renderer; only the general idea "draw retained map on GPU" generalizes.

#### patches/KeyLayout.cs  (284 lines)
- Bucket: C (medium) -- ships a keyboard default/migration; the *need* is KF2's tank-control pad map, but the machinery (defaults + one-time migration + secondary arrows) is generic input plumbing.
- Does: sets the port's WASD/FPS keyboard layout as the shipped default, migrates an existing stock settings.json once, and adds Up/Down arrows as second bindings by ORing into `PAD_dr`.
- Hooks: none by address; listens `PadReadEvent` (`_secondary`), reads/writes `PAD_dr` button word via `e.Buttons` on port 0.
- Data: `PAD_dr` button word (one bit per pad button, set by any device); `Controller.Up/Down` bit constants; `ConfigManager.Game.Keys` (`KeyBindings`).
- Structs: `KeyBindings` = one `Key` string per pad button (16 buttons: Cross/Circle/Square/Triangle/L1/R1/L2/R2/L3/R3/Start/Select/Up/Down/Left/Right); second keys held in `Extras`.
- Control flow: `Configure()` must run before `ConfigManager.Load` (Program.cs); migration at `RuntimeReadyEvent`; secondary arrows refreshed at most 1 ms and ANDed in reverse byte order.
- Overlays: none (input only); action map references game's action-mask table.
- Env / settings keys: `KF2_KEYS` (`fps`/`wasd`/`1`/`on` vs `stock`/`recompone`/`0`/`off`); interface.ini key `kf2.keys.layout`; settings page `Kf2.Settings.KeyLayoutPage`; `Settings.PatchSettings.Get/Set`.
- RecompOne deps: `KeyBindings`, `ConfigManager.Game.Keys`, `ConfigManager.SaveGame`, `PadReadEvent`, `HostWindow.IsKeyDown`, `Controller`, `Port 0`; `Silk.NET.Input.Key`.
- Mod-visible: `public static class KeyLayout`, `AppliedKey`, `Layout`, `Configure`, `Install`, `Apply`, `ApplyStock`, `IsApplied`.
- Split note: layout/migration logic is generic; the specific key→button assignment and the arrow-second-binding reason are KF2 control-scheme specific.
#### patches/HitGuard.cs  (613 lines)
- Bucket: D (high) -- a workaround for a specific KF2 final-boss crash (TODO #14); the record layouts, the 0x0FFF0000 read and the fdat23 ending are all this game.
- Does: fences the hit-resolution walk that would read a non-pointer as a pointer (answers "no reaction" instead of faulting) and reports impossible hit records; on by default; a pure diagnostic census behind `KF2_HITPROBE`.
- Hooks (all `game`): `0x8003A9CC` `func_8003A9CC` hit resolution (pre `BeforeHit`, report-only); `0x8003A448` `func_8003A448` the fifteen-pointer descriptor walk (pre `BeforeDescLookup`, the fence; sets V0=0 to skip body); `0x8002A550` `func_8002A550` player/main-loop stage 3 (post `AfterPlayerStage`, probe only, resolves the first malformed record).
- Data: entity table `0x8016C544` stride 0x7C count 200 (kind at +0x0 = 0xFF free, type at +0x2, redirect s16 at +0x22, drawn flag +0x9); descriptor table `0x80172624` stride 120, 15 pointers at +0x38, block end `0x80172624 + 0xCB0*4 = 0x801758E4` (copied by `func_80017244(0x80172624,src,0xCB0)`); RAM range fence `[0x80010000,0x80200000)`. Crash read `ReadU8(0x0FFF0000)`.
- Structs: entity record 0x7C bytes; creature descriptor 120 bytes with a 15-entry pointer block at +0x38; per-area descriptor block 0xCB0 words.
- Control flow: `func_800271D0` (weapon reach) → `func_8003A9CC` → (`fdat23` dispatch slot 0x48 = `func_8019FA2C` damage hook, which blanks type bytes) → `func_8003A490` → `func_8003A448`; the guard must sit on the walk, not the entry, because the bad state is created mid-call. `func_8003B72C` query selects on `u8[rec+9]==1`. Counts `FramePacing.Frames`.
- Overlays: `game`; references `fdat23` (ending/damage hook, module+0x48). `fdat*` modules share bases.
- Env / settings keys: `KF2_HITGUARD=0` (comparison, a hard crash); `KF2_HITPROBE=1|2`; `ModInfo Id="kf2.hitguard"`.
- RecompOne deps: `SymbolRegistry`, `HookManager` (`Invoke` returning false skips body), `CpuContext` (A0/A1/RA/V0/Snapshot), `IMemory`, `OverlayLoadedEvent`, `ModInfo`, `HookAttach`, `PSMemory` unmapped-address throwing behavior. Cross-patch: `FramePacing.Frames`.
- Mod-visible: `internal static class HitGuard` (not public); `Guard`, `Probe`, `Verbose`, `Configure`, `Install`, `BeforeHit`, `BeforeDescLookup`, `AfterPlayerStage`, `ForgetArea`.
- Split note: none -- the whole file is tied to KF2's hit path and fdat23 ending. The general shape ("fence a pointer walk that hardware tolerated") could be a runtime facility but would need the game's table layout.

#### patches/LoadPacing.cs  (419 lines)
- Bucket: C (high) -- "hold blocking VSync(0) in a disc wait to the 60 Hz grid" is a general PS1 timing mechanism; hook addresses are KF2's.
- Does: paces `VSync(0)` calls made inside the disc wait so the loading screen's walking figure steps at the console's vblank rate at any render rate; probes steps/sec.
- Hooks (all `game`): `0x80017CA8` `func_80017CA8` CD job drain wait (pre `BeforeWait` / post `AfterWait`); `0x800181B0` `func_800181B0` sector wait (pre/post); `0x8005FCC8` libetc `VSync` thunk (pre `BeforeVSync`); `0x8001883C` `func_8001883C` loader animator (pre `CountStep`, probe only).
- Data: animator globals `0x8006E5A4` frame counter (`&3`/`&7` gates), `0x8006E5A8` sequence state, `0x8006E5AC` figure x (+=3, or 5 past the middle band, under 288); CD job queue `0x801B6F44`; `VSync` A0 mode. `PSX` `VSync(-1)`/mode 1 must not be charged.
- Structs: none.
- Control flow: discs reads are blocking spins that call the animator and end in `DrawSync(0); VSync(0)` -- one call = one vblank on hardware; the loader draws no `DrawOTag`, so FramePacing/LoopPacing cannot see it; nested waits counted by `_depth`; watchdog `WaitDeadMs = 30000` excuses a leaked window via `FramePacing.ExcuseBoundaryGap()`.
- Overlays: `game` (GAME.EXE's libetc copy).
- Env / settings keys: `KF2_LOADPACING=0`; `KF2_LOADPACING_PROBE=1`; `ModInfo Id="kf2.loadpacing"`; `KF2_TICKRATE` deliberately not used (fixed 60 Hz).
- RecompOne deps: `SymbolRegistry`, `HookManager`, `CpuContext`, `IMemory`, `VSyncEvent`, `ModInfo`, `HookAttach`, `Profiler.Begin/End(FrameProfiler.LoadWait)`. Cross-patch: `FramePacing.ExcuseBoundaryGap`, `FramePacing.ApplyHostCeiling` (host ceiling), `MenuPacing` pattern.
- Mod-visible: `public static class LoadPacing`, `Enabled`, `Configure`, `Install`, `BeforeWait`, `AfterWait`, `BeforeVSync`, `CountStep`.
- Split note: the pacing/grid/watchdog mechanism generalizes; the wait/animator/thunk addresses and the "one call = one vblank" identity are KF2/PSY-Q specific.
#### patches/LoopPacing.cs  (930 lines)
- Bucket: C (high) -- "hold a modal loop that renders its own frames to the world tick and fill the gap with redraws" is a general frame-pacing mechanism; addresses, stage-13 argument semantics and the loops are KF2's.
- Does: classifies each frame as main-loop / modal-world / modal-interface via hooks, holds a modal world loop's body to the logic tick while redrawing/lerping its own panned camera; paces interface-only modal frames (menu) at 60 Hz.
- Hooks (all `game`, attached by `SymbolRegistry.Resolve`): stage 9 `0x800140AC` main-loop marker (pre `MainLoopStage`); stage 13 `0x800342D8` renderer (pre `BeforeRenderer`, post `AfterRenderer` at `Stage13.HookOrder.Redraw`). Cross-patch contributions: `WorldDrawn()` called from `FramePacing.BeforeFrameGate` (`0x80017880`), `FrameMinMs` called from `FramePacing.AfterDrawOTag`.
- Data: stage 13's view blocks consumed by `func_8002E22C`: `0x80192E78` position (3 u32 at +0/+4/+8), `0x80192E88` rotation (3 u16 at +0/+2/+4). `a0` = VECTOR pos, `a1` = SVECTOR rot (or both 0 = reuse stored view). Frame gate `func_80017880`. Sound-slot table near `0x8018EAA4` (noted only as a wrong-a0 hazard). `0x80199504` player view (a1 a loop passes). Angle wrap 0xFFF, `CutUnits=1024`.
- Structs: VECTOR 3×s32; SVECTOR 3×s16; stage-13 view cache 0x80192E78/0x80192E88.
- Control flow: modal loop entered from a gated stage; stage 13 gate `func_80017880`; stage 9 is main-loop-only marker; `KF2_TICKRATE`=LogicHz; holds body to tick and redraws at `FramePacing.LogicPhase`, redraw passes the frame boundary so `FramePacing.Floor` paces it; pause is filled uncapped. OPEN.EXE title treated as interface at 60 Hz.
- Overlays: `game` (all addresses); reacts to `open`/`game`/`end`/`main` via `OverlayLoadedEvent.Name`; stands down outside GAME.EXE.
- Env / settings keys: `KF2_LOOPPACING` (0 / pace / nocarry); `KF2_LOOPPACING_PROBE` (1|2); `ModInfo Id="kf2.looppacing"`. Reads `KF2_FPS`, `KF2_TICKRATE`, `KF2_FPS_LOGIC` through FramePacing.
- RecompOne deps: `SymbolRegistry`, `HookManager` (with order), `CpuContext.Snapshot/Restore`, `IMemory`, `OverlayLoadedEvent`, `ModInfo`, `HookAttach`; `HookManager.CreateDelegate<T>`. Cross-patch: `FramePacing` (`Gating`, `Extrapolating`, `Paused`, `TickedThisFrame`, `LogicPhase`, `LogicHz`, `TargetFps`, `Measured`, `Floor`, `BeforeFrameGate`, `AfterDrawOTag`, `BoundaryDeadMs`), `Stage13.HookOrder`, `ObjectSmoothing`/`AnimSmoothing` bracketing, `FrameSmoothing`.
- Mod-visible: `public static class LoopPacing`, `Enabled`, `Configure`, `Install`, `BeforeRenderer`, `AfterRenderer`, `MainLoopStage`, `WorldDrawn`, `FrameMinMs`.
- Split note: the classify/pace/redraw/lerp machinery is general; the marker stage addresses, stage-13 `(pos,rot)` contract, `func_8002E22C` cache and the named loops (fade/menu/spell/fdat05/fdat14/fdat23) are KF2.
#### patches/Map.cs  (908 lines)
- Bucket: C (high) -- "automap from the game's own floor plan" is a general idea; every address, the 80x80 tile layout and the area/slot conventions are KF2.
- Does: reads the area's 80x80 tile grid and the player's position/heading/floor from RAM, exposes it to the three map viewports; owns the tile-record constants, extents, room-centre search, full-screen pause predicate and the `KF2_MAP_PROBE` ASCII dump.
- Hooks: none (deliberately pure reads, no game function hooked). Listens `RuntimeReadyEvent`, `OverlayLoadedEvent`, `KeyboardEvent` (M/Shift+M/N), `ControllerEvent` (pad open). Calls `FramePacing.PauseWhen`.
- Data: tile grid `0x801C8484` (80*80*10 bytes, `tile=base+800*z+10*x`); player X/Y/Z `0x801994EC/F0/F4` (s32); yaw `0x80199506` (s16, composed; `0x1000` per turn, + = left); area `0x8017E060` (u8 0..7); max HP `0x80199426` (u16, 0=boot/load); half selector `0x801D9C8E` (u16, 0/5). Tile pitch 2048 world units; tile Y = -(height<<7).
- Structs: tile record = two 5-byte halves (lower +0, upper +5); per half: +0 model (<240 drawn, 0xFF empty), +1 height byte, +2 collision flags (&0xFC tested by func_8002C700), +3 shape index into `0x801D8484` (func_8002B7D0), +4 flags (bit 0x80 stops the visibility flood); `Extent{X0,Z0,X1,Z1}` per half.
- Control flow: caller must `Refresh()` from a panel's `Draw`, which runs inside the game's own `VSync` -> `PresentFrame` -> `HostWindow.Present` -> `PanelManager.DrawPanels`; grid copied 4x/s (250 ms) and on area change, not hooked on `func_8001689C` (called every frame). Pause via `FramePacing.PauseWhen` only while `InGame` and full map open. Screen rows run along -Z (`RowF = Span - TileF`).
- Overlays: `game` (all addresses); invalidated on any `OverlayLoadedEvent`; area id also reaches the cut area 10.
- Env / settings keys: `OnKey="kf2.map.on"`, `PadButtonKey="kf2.map.pad.button"`; `KF2_MAP` (on), `KF2_MAP_MINIMAP`, `KF2_MAP_PROBE`, comparisons `KF2_MAP_PAUSE`, `KF2_MAP_STYLE=blueprint`, `KF2_MAP_SHADE=1`, `KF2_MAP_WALLS=1`, `KF2_MAP_FLOOR`, `KF2_MAP_ARROW`; localisation keys `menu.game.map`, `menu.game.mapfs`.
- RecompOne deps: `Runtime.Mem`, `Runtime.View`, `RuntimeReadyEvent`, `OverlayLoadedEvent`, `KeyboardEvent`, `ControllerEvent`, `PanelManager`, `PopupManager`, `HostWindow.IsKeyDown`, `Localization.Merge`, `ConfigManager.ApplyViewToPanels`, `FramePacing.PauseWhen`, `PatchSettings`, `Silk.NET.Input.Key`. Cross-patch: `MapMarkers`, `MapPanel`, `MapOverlay`, `MapFullscreen`, `HotkeyGate`, `Remaster.Editor`.
- Mod-visible: `public static class Map`, constants `Span/Stride/RowBytes/TileUnits/HalfBytes/Model/HeightByte/Collide/Shape/Flags/NotDrawn/StopsFlood/Turn/StyleNative/StyleBlueprint/PadNone/PadTouchpad/PadL3/PadR3/PadSelect`, `Enabled/Pause/Minimap/...`, `Tiles`, `InGame`, `PlayerX/Y/Z/Yaw/Half/Area`, `MinHeight/MaxHeight/Occupied`, `Extents`, `Ready`, `Refresh`, `HalfOffset`, `TileOf`, `TileF`, `RowF`, `RowOf`, `RoomCentre`, `Drawn`, `Byte`, `Dump`, `Toggle*`, `SetMinimap`.
- Split note: the panel/ImGui shell and the map geometry seam is clear -- `Map.cs` is the KF2 reader; `MapRender.cs` the KF2 drawing. The general mechanism (a grid + a position -> an automap) would need a per-game adapter.

#### patches/MapFog.cs  (883 lines)
- Bucket: C/D (high) -- fog-of-war from the engine's visibility grid is a general idea; the grid addresses, tile format, slot/area identity and `.fog` persistence are KF2.
- Does: accumulates the game's 24x24 per-frame visibility grid into an 80x80 bitset per (save slot, area), verifies each lit cell with symmetric shadowcasting against the floor plan, exposes tri-state state (unexplored/remembered/in view), and persists to a `.fog` file.
- Hooks: none (uses `VSyncEvent` as the 60 Hz sampling clock; deliberately not a post-hook on `func_8002D3A8`).
- Data: visibility grid `0x80192EAC` (24x24 bytes, row-major on Z, `z*24+x`); mirror/origin offsets `0x80192EA0` (X) / `0x80192EA4` (Z) (as words, negated tile of cell 0,0); camera world pos `0x80192E78`/`0x80192E80`; current slot byte `0x8006E5D4` (u8; 0 until a load/save); area `0x8017E060`; max HP `0x80199426`; player X/Z `0x801994EC/F4`; tile grid base `0x801C8484`. Reads `CullGrid.LegacyBias`, `Stage13.ViewOverride`. Lit cells are the low two bits only (bit0=lower half, bit1=upper half drawn by func_80031B1C); other bits are flood working state.
- Structs: 24x24 grid cell; 80x80 bits `bit=z*80+x`, `Bytes = 80*80/8 = 800`; store keyed `slot<<8 | area`; line-of-sight window 53x53 (`LosR=26`) per half.
- Control flow: sample on `VSyncEvent` (wall-clock 60 Hz, `0021`); staleness guard -- camera within 2 tiles of player, 250 ms hold after overlay load, skip when `Stage13.ViewOverride != null`, reject slot>3 / area>10; the cone's flood ORs two parents (45° per ring) so shadowcasting intersects it; cast per stacked half (no "player floor" assumption, because the selector is measured failing in area 5); `drawn==0` means no view. Fails open (passes) when a half has no floor plan. Sample gated on `Map.Enabled`, not the fog switch.
- Overlays: `game` (all addresses); `OverlayLoadedEvent` flushes and re-holds.
- Env / settings keys: `OnKey="kf2.map.fog"`; `KF2_MAP_FOG=0`, `KF2_MAP_FOG_PROBE=1|2`, comparison `KF2_MAP_FOG_LOS=0`.
- RecompOne deps: `Runtime.Mem`, `Runtime.View`, `RuntimeReadyEvent`, `OverlayLoadedEvent`, `VSyncEvent`, `ConfigManager.Game.CardAPath`, `IMemory`, `BitOperations`. Cross-patch: `Map`, `MapRender` (predicate consumer), `CullGrid.LegacyBias`, `Stage13.ViewOverride`, `CullCone`.
- Mod-visible: `public static class MapFog`, `OnKey`, `Bytes`, `Enabled`, `LineOfSight`, `Predicate`, `State`, `Configure`, `Install`, `SetEnabled`, `SetLineOfSight`, `ForgetArea`, `RevealArea`, `Flush`.
- **.fog file format** (write `Flush`, read `Load`): path `Path.ChangeExtension(ConfigManager.Game.CardAPath, ".fog")` (default `carda.fog`, fallback card `carda.sav`) -- beside the memory card. Header 10 bytes: `"KF2FOG\0"` (7 bytes incl. NUL) + version byte `1` + u16 LE record count. Each record `2 + Bytes = 802` bytes: slot u8, area u8, then the 800-byte bitset (`bit = z*80 + x`, LSB-first inside each byte). Whole store rewritten atomically via `path + ".tmp"` then `File.Move(overwrite: true)`; loaded once at `RuntimeReadyEvent`, flushed on overlay load, every 10 s while dirty, and on `ProcessExit`. Rejected on bad magic/version/truncation.
- Split note: the `.fog` format is game-specific because its payload is KF2's 80x80 bit grid and its keys are KF2's slot/area bytes; a sibling game would need its own grid size and identity scheme, though the header/atomic-write/record container could be a shared codec.

#### patches/MapMarkers.cs  (484 lines)
- Bucket: D (high) -- reads four KF2 world tables by address, stride and liveness sentinel, and claims KF2's save-point definition kind.
- Does: samples creatures, props, effects and billboards from the four renderer world tables into a per-frame marker list, plus save points, with per-half assignment and fog-aware visibility.
- Hooks: none. Called from `Map.Refresh`.
- Data / structs (four tables): creatures `0x8016C544` stride `0x7C` count 200, drawn `u8[+0x9]==1`, pos +0x2C, rot +0x40, type +0x2; objects `0x80177714` stride `0x44` count 396, drawn `u16[+0x6]!=0xFF`, pos +0x14, rot +0x24, type +0x4, def +0x6; effects `0x8019CC6C` stride `0x48` count 128, live `u8[+0x0]!=0xFF`, pos +0x14, rot +0x24; billboards `0x80195174` stride `0x18` count 128, live `u16[+0x0]!=0xFFFF`, pos +0x8, no rot. Save-point kinds `0x80175914 + def*0x18`, kind byte `SaveKind=0x0E`, count `0x1E00/0x18`. Rot yaw at +2 biased `+0x800`.
- Control flow: liveness is the renderer's (`u8[+0x9]==1` creatures, `u16[+0x6]!=0xFF` objects), not the owning stage's; object table outlives its area (loader clears `+0x4`, leaves `+0x6`/positions) so the sample is held until some slot has `+0x4 != 0xFF` (`ObjectTableSettled`); records at 0,0,0 skipped; sample every 50 ms (one tick). Fog: creatures/effects need tile lit this sample (state 2), objects/sprites need remembered (state 1).
- Overlays: `game`.
- Env / settings keys: `KF2_MAP_MARKERS=0` (off, no longer a setting); the four class toggles, `Facing`, and `Saves` are session-only/retired keys; `KF2_MAP_PROBE=1` dumps the census.
- RecompOne deps: `IMemory`. Cross-patch: `Map` (`TileOf`, `Byte`, `Drawn`, `HalfOffset`, `Span`, `TileUnits`, `HalfBytes`), `MapRender.DrawMarkers`, `ObjectSmoothing` (same four tables), `AgentServer`.
- Mod-visible: `public static class MapMarkers`, `Kind`, `Marker`, `SaveKind`, `Enabled/Creatures/Objects/Effects/Sprites/Facing/Saves`, `Live`, `Counts`, `SaveCount`, `Refresh`, `Configure`, `Visible`, `Noun`, `Dump`.
- Split note: purely KF2 -- the four-table schema is the game's object/creature/effect/billboard layout.

#### patches/MapFullscreen.cs  (269 lines)
- Bucket: C (medium) -- the "full-area map over the picture" viewport; the ImGui floating-panel shell is generic but the content is KF2's map model.
- Does: the player's M/touchpad map; a `NoInputs` scrim over the game picture fitting the whole occupied extent, drawn via `MapRender`; session-only.
- Hooks: none; `Map.Refresh` in Draw; consumes `FramePacing.PauseWhen` set up by `Map.cs`.
- Data: none directly (through `Map`: `Extents`, `HalfOffset`, `Area`, `PlayerDot`, `Style`, etc.). `MapRender.Picture` rectangle.
- Structs: `Map.Extent` per half.
- Control flow: rectangle is the game picture (`OutputView`, patch `0029`) not the viewport; scale fitted to the occupied extent with a `MaxCell` ceiling and no floor; native style forces a square board; `NoBringToFrontOnFocus` absent (see MapOverlay).
- Overlays: `game` indirectly via Map.
- Env / settings keys: none own; draws per `Map.Style`/`Map.PlayerDot`.
- RecompOne deps: `ImGuiNET`, `IFloatingPanel`, `Theme.Scale`, `OutputView` (`0029`). Cross-patch: `Map`, `MapRender`.
- Mod-visible: `public sealed class MapFullscreen : IFloatingPanel`, `Instance`, `Name`, `TitleKey`, `IsOpen`, `Draw`.
- Split note: shell (panel, scrim, fit-to-rect) is generic; the KF2 half is `Map.Extents`/`HalfOffset` and the native board styling.

#### patches/MapOverlay.cs  (191 lines)
- Bucket: C (medium) -- the corner minimap `IFloatingPanel`; shell generic, content KF2.
- Does: north-up minimap of the tiles around the player over the game picture; on only when `Map.Enabled && Map.Minimap` and no full map; `IsOpen` setter deliberately a no-op.
- Hooks: none; `Map.Refresh` in Draw.
- Data: none directly (Map fields: `MinimapSize/Radius/Corner/Pad/Shape/Opacity`, `PlayerX/Z`, `HalfOffset`, `Style`); `MapRender` palette.
- Structs: none.
- Control flow: window flags `NoInputs` + `NoBackground`, `NoBringToFrontOnFocus` deliberately absent (else it lands under the opaque output panel); round vs square; markers/player exempt from opacity.
- Overlays: `game` indirectly.
- Env / settings keys: none own; `KF2_MAP_MINIMAP` via Map; N toggles session.
- RecompOne deps: `ImGuiNET`, `IFloatingPanel`, `Theme.Scale`, `OutputView`. Cross-patch: `Map`, `MapRender`.
- Mod-visible: `public sealed class MapOverlay : IFloatingPanel`, `Instance`, `Name`, `IsOpen`, `Draw`.
- Split note: shell generic; KF2 content via Map/MapRender.

#### patches/MapPanel.cs  (281 lines)
- Bucket: C (medium) -- the docked instrument panel; ImGui window/drag/zoom generic, readout and tile semantics KF2.
- Does: the debugger map: pan/zoom/hover readout of the ten tile bytes, plus session toggles for markers/height/walls and fog Forget/Reveal; the second remaster picker (right-click a half).
- Hooks: none; `Map.Refresh` in Draw.
- Data: all through `Map` (`Tiles`, `Byte`, `TileOf`, `RowF`, `Area`, `Occupied`, `HalfOffset`, `Span`, `Stride`, `HalfBytes`, `Model/HeightByte/Collide/Shape/Flags`, `NotDrawn`, `StopsFlood`); `MapMarkers.Live/Counts/SaveCount`; `MapFog.LineOfSight/Enabled`.
- Structs: marker records; `Remaster.TileKey`.
- Control flow: reads game memory inside Draw (same VSync-thread guarantee); invisible button owns canvas for drag/zoom.
- Overlays: `game` indirectly.
- Env / settings keys: none own (the retired map keys are deliberately unread); `MapFog` session toggles.
- RecompOne deps: `ImGuiNET`, `IPanel`, `Theme.Scale`. Cross-patch: `Map`, `MapRender`, `MapMarkers`, `MapFog`, `Remaster.Editor`, `Remaster.Pack`, `Remaster.TileKey`.
- Mod-visible: `public sealed class MapPanel : IPanel`, `Instance`, `Name`, `TitleKey`, `IsOpen`, `Draw`.
- Split note: the panel widgetry is generic; the ten-byte tile readout and remaster picker are KF2.

#### patches/MapRender.cs  (986 lines)
- Bucket: C (high) -- the shared map drawing routine; a "draw an automap grid with markers" routine generalizes but the palette, tile semantics, marker shapes and player mapping are KF2.
- Does: the one draw routine all three viewports call -- tiles (blueprint fill or native outline style), grid, wall tint, fog tints, native bevel frame, markers, save-point letter, and the player pointer/dot.
- Hooks: none. Reads `Map.Tiles`/`Map.*` only.
- Data: `Map.Tiles`, `Map.MinHeight/MaxHeight`, `Map.NotDrawn`, `Map.StopsFlood`, `Map.HalfBytes`, `Map.Turn`, `Map.Span/Stride/RowBytes`; `MapMarkers.Live`, `MapMarkers.Kind`, `MapMarkers.Saves`, `MapMarkers.Facing`, `MapMarkers.Visible`; `MapFog` predicate. Palette sampled from a capture of KF2's own map screen (`#3A523A` field, `#0E200E` ink, `#7B8C7F` bevel, player `#E7C4C5`/`#F78272`).
- Structs: `Map.Extent`; `MapMarkers.Marker`.
- Control flow: caller passes origin+cell and tiles x0..x1 / screen rows z0..z1; screen Y runs along -Z (`Map.RowOf`); native style draws outlines in a second pass, "other stacked half" wash; player heading derived from `func_80028080` (heading `(-sin yaw, cos yaw)`, screen angle decreases as yaw increases); dot/pointer default.
- Overlays: `game` indirectly.
- Env / settings keys: none own (reads `Map.Style`, `Map.Shade`, `Map.Walls`, `Map.PlayerDot`, `Map.Probe`).
- RecompOne deps: `ImGuiNET` (`ImDrawListPtr`, fonts, `ImGuiCol`), `OutputView` (`0029`), `Theme.Scale`. Cross-patch: `Map`, `MapMarkers`, `MapFog`.
- Mod-visible: `public static class MapRender`, `Ground`, `Text`, `TextDim`, `Cardinals`, `Picture`, `Fade`, `Draw`, `Frame`, `FrameWidth`, `DrawMarkers`, `DrawPlayer`, `DrawPlayerDot`, `DrawPlayerPointer`.
- Split note: the drawing primitives and fit logic are generic; the palette, tile-record interpretation, marker kinds and heading derivation are KF2.

### Summary

Counts (18 files):
- A. RecompOne fork: 0
- B. Game-agnostic infrastructure: 3 -- `HookAttach.cs`, `HotkeyGate.cs` (B with a remaster-specific `Editing` half), `GpuFrames.cs` (B, host-side only).
- C. Game patch whose mechanism might generalize: 13 -- `FullRateLogic`, `GearCompare`, `GpuWorld`, `GpuWorldCensus`, `KeyLayout`, `LoadPacing`, `LoopPacing`, `Map`, `MapFog` (C/D), `MapFullscreen`, `MapOverlay`, `MapPanel`, `MapRender`.
- D. KF2-only: 2 -- `HitGuard.cs`, `MapMarkers.cs`.

Most important observations:
1. **The map feature is one seam split across seven files.** `Map.cs` is the only reader of the 80x80 grid + player fix; `MapFog.cs` accumulates and persists; `MapMarkers.cs` is the only reader of the four world tables; `MapRender.cs` draws; `MapPanel`/`MapFullscreen`/`MapOverlay` are three viewports over the same API. A sibling game reuses the ImGui panel/viewport/container machinery but must supply its own grid reader, table schema and palette.
2. **The `.fog` format is a self-describing container holding KF2's 80x80 bitset.** Header `"KF2FOG\0"` + version 1 + u16 count; records `(slot u8, area u8, 800-byte bitset)`; path derives from the memory-card path (`carda.fog`); atomic `.tmp`+rename; loaded at RuntimeReady, flushed at overlay load/10 s/exit. Its size and keys are KF2-specific even though the container is not.
3. **Map and MapMarkers never hook and never write game memory** -- they read inside the game's own VSync call on the game thread (LibEtc.VSync → PresentFrame → HostWindow.Present → PanelManager.DrawPanels). This is the cleanest, most portable architectural pattern in the batch.
4. **Several patches are KF2 timing workarounds with a general shape**: the game counts time in `VSync(0)` calls and in modal-loop iterations, so `LoadPacing` (disc-wait VSync held to 60 Hz) and `LoopPacing` (modal loops held to the tick, gap filled with stage-13 redraws) are the same class as `FramePacing`/`MenuPacing`. The mechanism generalizes; the addresses and stage-13 `(pos,rot)` contract do not.
5. **`GpuWorld.cs` is a switchboard over a large port-runtime renderer** (`RetainedScene`/`RetainedMap`/`RetainedModels`/`RetainedScene` static mesh); the actual shared engine is in the runtime, while this file encodes the KF2 model-table decode, per-switch defaults and the probe. Its dependencies are deep and KF2-specific (`0x8018E19C` model bank, per-pixel lighting, tile/object walks).
6. **`HitGuard.cs` is the purest D**: a fence for `fdat23`'s ending blanking a creature type byte mid-hit-resolution (`func_8003A9CC` → `func_8019FA2C` → blank `+0x2=0xFF` → `func_8003A448` reads `0x0FFF0000`). It hard-codes the entity/descriptor tables and is the port compensating for `PSMemory` throwing where the console returned open-bus.
7. **`GearCompare.cs` and `MapRender.cs` both run the game's own routines or copy its own pixels** (`func_800244CC` for after-equip stats; palette sampled from the map screen), the pattern this port uses to stay visually faithful while reimplementing.
8. **`KeyLayout.cs` is the model for a shareable input default/migration**: default-before-ConfigManager.Load, one-time migration from known stock/previous layouts, `Superseded` + version bump, second bindings ORed into the pad word. Only the key→button map is KF2.
9. **`HookAttach.cs` is a self-contained, game-agnostic attach/retry/read-back helper** that every other patch depends on; a strong candidate to move into the RecompOne fork or a shared runtime. Its only KF2 trace is the `[KF2]` log prefix.
10. **`FullRateLogic.cs` and `GpuWorldCensus.cs` are comparison/probe modes**, not shippable features; they document the per-tick counters that break under render-rate logic and are useful mainly as the "before" half of frame-pacing measurements.

## patches/, MenuDraw to ObjectSmoothing
#### patches/MenuDraw.cs  (300 lines)
- Bucket: C (confidence: high) -- C# rewrite of the game's menu primitive drawing: a generic mechanism (emit POLY_FT4 text/number/box quads from template structs into an OT) whose data tables and glyph layout are KF2's.
- Does: emits the game's menu text, digits and nine-slice window quads as POLY_FT4 packets, byte for byte.
- Hooks: none (a direct-call library: `DrawText`/`DrawDigits`/`DrawWindow`/`DrawNumber` called by MenuWorld/GearCompare; `Reference.*` calls recompiled `KingsField2.func_80021E10`, `func_80022B20`, `func_80021FCC`, `func_800222B8` directly for `KF2_GEARCOMPARE=verify`).
- Data: `Cursor` 0x8006E914 (u32 primitive cursor; bumped by 0x28 per quad); `ActiveDescriptor` 0x8017E0A4 (its +8 mirrors the cursor); `OrderingTable` 0x8018E0A8 (base of OT slot array, slot*4 entries); `TextTemplate` 0x80064BF0, `NumberTemplate` 0x80064BE4, `WindowTemplates` 0x80064C68 (DR/packet templates: u16 w@+8, u16 h@+A, u8 u/v/uw/vh at +4/+6/+8/+A, colour u16s at +0/+2).
- Structs: POLY_FT4 = 0x28 bytes/quad (length byte +3=9, code +7=0x2C; XY at +8/+A/+10/+12/+18/+1A/+20/+22; UV at +C/+D/+14/+15/+1C/+1D/+24/+25; colour +4/+5/+6). Glyph map: `(g&15)*8` u, `(g>>4)*15` v, 7 px step, 24-quad cap (step>=168); space = cell 0x7F. Window = 3x3 nine-slice, fixed 33/28/94 geometry, middle row/col stretch by dw/dh+pad.
- Control flow: none beyond the OT slot ordering (slot 10 for text/digits, 20 for window) and cursor mirror via `ActiveDescriptor+8`.
- Overlays: none named in file; the `Reference` calls are `KingsField2_game` (GAME.EXE).
- Env / settings keys: `KF2_GEARCOMPARE` (=verify) drives the Reference path (read in GearCompare).
- RecompOne deps: none of the port patches; uses runtime types `IMemory`, `CpuContext`.
- Mod-visible: `public static class MenuDraw` with constants `Cursor`, `ActiveDescriptor`, `OrderingTable`, `TextTemplate`, `NumberTemplate`, `WindowTemplates`, and public `DrawText`, `FormatNumber`, `DrawDigits`, `DrawNumber`, `DrawWindow`, nested `MenuDraw.Reference` (`Text`, `Number`, `Box`).
- Split note: the emit-buffer mechanics (cursor/OT/POLY_FT4) are generic; the template addresses, glyph grid and nine-slice constants are KF2 data. Seam is the six `const uint` addresses and the glyph/window geometry.

#### patches/MenuPacing.cs  (443 lines)
- Bucket: C (confidence: high) -- holds two vblank-counted UI behaviours (cursor auto-repeat, blink) to the console's 60 Hz; mechanism generalizes, implementation hooks KF2 menu functions.
- Does: makes the six `VSync(0)` calls in the menu's repeat spin cost a vblank again, and caps the cursor-blink counter to one step per 60 Hz slot.
- Hooks: pre/post on repeat gate `func_80022E90` (0x80022E90, GAME.EXE, auto-repeat delay: spins up to six VSyncs while a direction is held); pre on libetc `VSync` thunk 0x8005FCC8 (same one FramePacing counts frames on); pre/post on menu frame head `func_80022530` (0x80022530: buffer swap, OT pointer, ClearOTag, blink ping-pong). All attached by address via `SymbolRegistry.Resolve("game", …)` + `HookManager.AddPre/AddPost`, committed, read back with `HookAttach.Installed`.
- Data: `BlinkCount` 0x8006E5CC (u32, 0..7 ping-pong counter), `BlinkDir` 0x8006E5D0 (u32 direction: 0 up, 1 down, else frozen); `0x8006E5C4` (repeat latch: gate returns early unless ==1); `func_80021A84` reads the count as `(v+0x1F4)<<6` for +0xE (comment only).
- Structs: none.
- Control flow: the in-game menu is outside FramePacing's stage gate by construction: stage 3 `func_80029CBC` `jal`s `func_80018E80` on a just-pressed Circle and it blocks for the whole session, presenting frames through `func_800226A8` (VSync then DrawOTag). Hooks mark the repeat window with a pre/post pair; six menu frames still presented. Frame head runs twice per menu-loop iteration. 60 Hz deliberately, not `FramePacing.LogicHz`.
- Overlays: `game` (GAME.EXE); function addresses valid there.
- Env / settings keys: `KF2_MENUPACING` (0 = off), `KF2_MENUPACING_PROBE` (1 = probe). No settings page (correctness fix).
- RecompOne deps: recompone patches `0021` (vblank on wall-clock grid) and `0025` (permissive FrameClock ceiling) named in comments; runtime types `HookManager`, `SymbolRegistry`, `HookAttach`, `ModInfo`, `CpuContext`, `IMemory`, `Profiler` (`FrameProfiler.MenuWait`); depends on `FramePacing`/`FrameProfiler` classes.
- Mod-visible: `public static class MenuPacing` with `Enabled`, `Configure`, `Install`, `BeforeRepeat`, `AfterRepeat`, `BeforeVSync`, `BeforeFrameHead`, `AfterFrameHead` (hook methods are public, as required by reflection lookup).
- Split note: generic seam is "pace vblank-counted UI to 60 Hz" and the VSync pre-hook; KF2-specific are the five addresses, the `0x8006E5C4` latch test and the blink-word semantics.

#### patches/MenuMouse.cs  (1418 lines)
- Bucket: C (confidence: high) -- host pointer -> the game's own menu cursor (a generic "point-and-click a pad UI" mechanism) implemented entirely against KF2 menu code, descriptors and layout tables.
- Does: turns the mouse into hover/click/back-out/wheel for the in-game menu's three widget kinds (fixed option list, scrolling list, two-line prompt).
- Hooks (all GAME.EXE, resolved by address with `SymbolRegistry.Resolve("game", null, …)`, pre/post via `HookManager.AddPre/AddPost`, committed and read back with `HookAttach.Installed`):
  - `MenuLoop` 0x80018E80 pre/post (`BeforeMenu`/`AfterMenu`) -- the modal loop that blocks for the session; owns pointer capture release/return.
  - `OptionDraw` 0x800208D8 pre (`BeforeOptionDraw`) -- fixed list drawer `(group,count,cursor,confirmed)`.
  - `FixedCursor` 0x8001EA14 pre/post (`BeforeCursor`/`AfterCursor`) -- fixed list stepper `(cursor,max,*sel,*confirmed,*cancel)->cursor`; patch writes `V0`.
  - `ScrollCursor` 0x8001EB70 pre/post (`BeforeScroll`/`AfterScroll`) -- scrolling list stepper `(desc,items,*confirmed,*cancel)->padWord`.
  - `MenuPadRead` 0x80022E58 post (`AfterPadRead`) -- the menu's `PadRead(1)`, one place where back-out is spent and prompt injections are made.
  - `PromptLoop` 0x800206E0 pre/post (`BeforePrompt`/`AfterPrompt`), `PromptDraw` 0x80021478 pre (`BeforePromptDraw`).
  - Direct calls into recompiled GAME: `func_80022DC4` (blip, via `Blip`), `func_80022CAC` (load item preview, via `Preview`).
- Data: `LayoutBase` 0x80064CD4 (fixed-list layout table, `GroupStride` 0x134, `RecordStride` 0x1C, record 0 = header, `MaxFixedRows` 10); `ItemTemplate` 0x80064C20 (w@+8, h@+A, drawn with `TemplateInset` 6 up-left); `BlinkDir` 0x8006E5D0 (u32, zeroed on accepted move); pad masks `MaskUp` 0x8006E590, `MaskCross` 0x8006E568, `MaskCancel` 0x8006E56C; `RowSprite` 0x80064C44 (scrolling row width @+8); `PromptTemplate` 0x80064C08 (54x24); repeat latch `0x8006E5C4` (comment).
- Structs: scrolling-list descriptor fields `DescX` 0x1C, `DescY` 0x1D, `DescCount` 0x1E, `DescVisible` 0x1F, `DescScroll` 0x20 (page / row 0), `DescCursor` 0x21 (absolute selection), `DescRow` 0x22 (cursor - scroll); row geometry `RowInset` 5, `RowPitch` 14, 236 wide; fixed-list rows each own u16 X/Y at record+0/+2.
- Control flow: one menu-loop iteration == one pad read; injected buttons OR into `V0` (a synthetic Up while hovered row != drawn flag, then Cross; a cancel latch spent on the delivering read); session scope `SessionGapMs` 500 ms, widget scope `WidgetGapMs` 250 ms; "whichever device moved last owns the cursor" (`IdleMs` 2000 ms); pointer sampled in the post because the stepper can spin ~100 ms; `EdgeSlack` 8 for off-widget back-out; `WheelRows` 1.
- Overlays: `game` (GAME.EXE) only; some covered screens (save slots, shops) are outside `func_80018E80`.
- Env / settings keys: `KF2_MENUMOUSE` (0=off), `KF2_MENUMOUSE_PROBE`; settings key `kf2.menumouse.on`; Input pane Mouse tab.
- RecompOne deps: recompone patch `0038` (`HostWindow.TakeMouseWheel`) and `0029` (GameW) referenced in comments; runtime types `HostWindow`, `OutputView`, `Display`, `Analog`, `PopupManager`, `Event`/`RuntimeReadyEvent`, `HookManager`, `SymbolRegistry`, `HookAttach`, `ModInfo`, `ImGuiNET`, `CpuContext`, `IMemory`, Silk `MouseButton`; port types `Mouse`, `MouseIndicator` (indirect), `MenuPacing`.
- Mod-visible: `public static class MenuMouse` -- `Enabled`, `OnKey`, `Configure`, `Install`, `BeforeMenu`, `AfterMenu`, `BeforeOptionDraw`, `BeforeCursor`, `AfterCursor`, `BeforeScroll`, `AfterScroll`, `BeforePrompt`, `AfterPrompt`, `BeforePromptDraw`, `AfterPadRead`.
- Split note: pointer sampling, hit-rect union/edge-slack, session/edge scoping and the last-device-owned-cursor rule are generic; every address, the descriptor layout, template insets and mask words are KF2 data.

#### patches/MenuWorld.cs  (519 lines)
- Bucket: C (confidence: high) -- draws the world live behind a menu/message by re-running stage 13's drawing into a private ordering table; generic idea, KF2 renderer + buffer layout implementation.
- Does: replaces the menu presenter and the message fade so the real world (full width, depth, AO, Z) is drawn instead of the game's frozen 320-wide `LoadImage` copy.
- Hooks (GAME.EXE):
  - `Presenter` 0x800226A8 **replace** (`Present`) -- `func_800226A8` DrawSync/VSync/PutDrawEnv/PutDispEnv/paste/DrawOTag.
  - `Enter` 0x80022754 post (`AfterEnter`), `Leave` 0x800228C8 post (`AfterLeave`) -- menu framework shrink/restore.
  - `Renderer` 0x800342D8 post (`AfterRenderer`) -- stage 13, saves world GTE/model/vertex state + draw-env clear.
  - `MainLoopMarker` 0x800140AC pre (`MainLoop`) -- stage 9, main loop only, ends a stale session.
  - `SoundCentred` 0x80014158 pre (`Quiet`), `SoundPlaced` 0x80013D08 pre (`Quiet`) -- suppress sound players during a pass (`!_drawing`).
  - `Fade` 0x800356F4 **replace** (`MessageFade`) -- message-box fade loop.
  - Direct calls: `func_80060624` LoadImage, `func_800605A4` DrawSync, `func_8002E064` stage-13 head, `func_8002E0FC` stage-13 presenter, `PadRead_game`, `func_8005FCC8` VSync, `func_80060870` PutDrawEnv, `func_80060990` PutDispEnv, `func_80060818` DrawOTag; `Stage13.DrawScene`.
- Data: `BufferIndex` 0x8017E084 (u8); `Descriptor0` 0x8017E08C / `Descriptor1` 0x8017E098 (start/end pairs); `ActiveDescriptor`, `OtPointer` (=ScenePass); `DrawEnvs` 0x8018E0AC stride 0x5C, `DispEnvs` 0x8018E164 stride 0x14; `SavedDescriptor1` 0x8006EB30; `FrameCounters` [0x801DA554, 0x80192D54, 0x80192D50]; `MenuPrimBytes` 0xC800; `MessageVramSave` 0x25800; `MessageRect` 0x8006E610 (RECT); `MessageSave` 0x8017E09C; `EnvClear` 0x18 (DRAWENV isbg+colour); `TableOffset` 0x10.
- Structs: menu buffers as (start,end) descriptors checked as start, start+0x6400, +0xC800; `ScenePass` (`ModelTable`, `VertexBase`, `ActiveDescriptor`, `OtPointer`, `OtEntries`, `OtBytes`); `PrimBuffer.TrySecondBuffer` for a relocated world-sized buffer; `Gte.State`; layout is the shrunk one the game builds (`func_80022754`), never assumed.
- Control flow: menu framework `func_80022754` enter -> `func_80022530` head -> `func_800226A8` present -> `func_800228C8` leave; message box `func_80035B48` with fade `func_800356F4`; a pass runs stage 13's *drawing* half only, never the world advance; `_drawing` gates the sound players and the renderer post. Frame head `func_8002E064` is deliberately not called in `Present`.
- Overlays: `game` (GAME.EXE); stage 13 is game's.
- Env / settings keys: `KF2_MENUWORLD` (0 = game's frozen copy), `KF2_MENUWORLD_PROBE`.
- RecompOne deps: port types `ScenePass`, `PrimBuffer`, `SpriteAnim`, `Stage13`, `MessageText`; runtime `HookManager`, `SymbolRegistry`, `HookAttach`, `ModInfo`, `Event`/`OverlayLoadedEvent`, `PSMemory`, `CpuContext`, `IMemory`, `Runtime.RamSize`, GTE.
- Mod-visible: `public static class MenuWorld` -- `Enabled`, `MessageLive`, `Configure`, `Install`, `Quiet`, `MainLoop`, `AfterRenderer`, `AfterEnter`, `AfterLeave`, `MessageFade`, `Present`.
- Split note: "render into a private OT and splice ahead of the menu's" is generic; the menu framework addresses, stage 13, the buffer/descriptor arithmetic and the message-quad byte encoding are KF2.

#### patches/MessageGlyphs.cs  (121 lines)
- Bucket: D (confidence: high) -- a generated KF2 font lookup: FNV-1a 64 of a cell's 14 row bytes -> character; meaningless for another game's font.
- Does: maps hashes of glyph cells (from the disc's message TIM) to characters.
- Hooks: none. Data: none in the sense of RAM addresses (the 110-entry `Dictionary<ulong,char>` is the data; generated by `scripts/msg_glyphs.py`).
- Structs: none. Control flow: none. Overlays: none.
- Env / settings keys: none.
- RecompOne deps: none (port data table read by `MessageText`).
- Mod-visible: `static class MessageGlyphs` (internal) with `public static readonly Dictionary<ulong,char> Table`; a mod can read it (it is `static` but the class is internal, so effectively port-internal).
- Split note: the glyph set and hashes are pure KF2 disc data; a sibling game regenerates the table. Seam is the whole file.

#### patches/MessageText.cs  (378 lines)
- Bucket: C (confidence: medium) -- "replace the game's 1:1 bitmap text with a real text panel" is a general idea; the decode is entirely KF2's 4-bit TIM message format and glyph table.
- Does: decodes a message TIM in RAM cell-by-cell against `MessageGlyphs`, zeroes its palette so the game's text quads draw nothing, and draws the decoded lines in an ImGui floating panel; anything undecodable is left to the game.
- Hooks (GAME.EXE):
  - `ShowMessage` 0x80035B48 pre/post (`BeforeShow`/`AfterShow`) -- `func_80035B48(file, entry)`, the message modal loop.
  - `LoadTims` 0x80035684 pre (`BeforeLoad`) -- uploads the message TIM; the decode happens here.
  - `Fade` 0x800356F4 pre/post (`BeforeFade`/`AfterFade`) -- brightness in `A0`, held in `S2`.
  - `Present` 0x8002E0FC pre (`BeforePresent`) -- stage 13's presenter; reads `c.S2` as the fade brightness.
  - `MainLoopMarker` 0x800140AC pre (`MainLoop`) -- stage 9; only when `KF2_MESSAGETEXT_TEST` queued a message, opens `func_80035B48` directly under a `c.Snapshot()`/`Restore`.
- Data: the TIM is read from RAM at `c.A0` on `func_80035684`: magic u32@+0 == 0x10, u32@+4 == 8; CLUT 16×u16 at +20; image at `buf+8+u32@+8`; palette zeroed at `buf+8+12` (colour 0 transparent); geometry `ScreenX` 32, `ScreenY` 10, `CellW` 8, `CellH` 14, `OriginX` 4, `Phases` [3,10], `FullBright` 0x6C.
- Structs: 4-bit TIM (header, CLUT 16 entries, image block w@+8/ h@+A in 16-bit units `*4`); message cells are 8x14; each cell's 14 row bytes hashed FNV-1a 64 -> `MessageGlyphs.Table`.
- Control flow: `func_800356F4` runs the fade and presents through stage 13's presenter, so `BeforePresent` reads the live brightness from `S2`; the panel draws from what `BeforeLoad` saved and the per-message state is on the game thread (`volatile _active`).
- Overlays: `game` (GAME.EXE).
- Env / settings keys: `KF2_MESSAGETEXT`, `KF2_MESSAGETEXT_PROBE`, `KF2_MESSAGETEXT_TEST=f:e,...`. No settings page (off by default; judged unwanted).
- RecompOne deps: runtime `IFloatingPanel`, `PanelManager`, `Localization.Merge`, `MapRender.Picture`, `Event`/`RuntimeReadyEvent`, `HookManager`, `SymbolRegistry`, `HookAttach`, `ModInfo`, `PSMemory`, `IMemory`, `CpuContext`, `ImGuiNET`; port types `MessageGlyphs`, `MenuWorld` (`MessageLive`).
- Mod-visible: `public sealed class MessageText : IFloatingPanel` -- `Instance`, `Enabled`, `Covering`, `Name`, `TitleKey`, `IsOpen`, `Configure`, `Install`, `MainLoop`, `BeforeShow`, `AfterShow`, `BeforeFade`, `AfterFade`, `BeforePresent`, `BeforeLoad`, `Draw`; localisation key `panel.kf2.message` (en/pt-BR/es-419).
- Split note: the ImGui panel, hash decode and brightness ramp generalize; the TIM addresses/magic, cell geometry and glyph table are KF2.

#### patches/Murk.cs  (78 lines)
- Bucket: C (confidence: high) -- "darken water with the view ray's run through it" generalizes; the file is settings/glue over `WaterMurk` and the KF2 reflection pass.
- Does: configures murky water (on/off, distance, colour sliders, max tilt) and delegates to `WaterMurk`; exposes the `murk` shell verb.
- Hooks: none.
- Data: none directly (writes `WaterMurk.Enabled/Distance/MaxTilt`, `SurfaceMaterial.RectN`).
- Structs: none. Control flow: none. Overlays: none.
- Env / settings keys: `KF2_MURK`, `KF2_MURK_DISTANCE`, `KF2_MURK_TILT`; settings `kf2.murk.on`, `kf2.murk.distance`, `kf2.murk.r/g/b`.
- RecompOne deps: runtime `Event`/`RuntimeReadyEvent`, `Runtime.View.GetBool`; port types `WaterMurk`, `GteDepth`, `SurfaceMaterial`, `Reflections`.
- Mod-visible: `public static class Murk` -- `OnKey`, `DistanceKey`, `RKey`, `GKey`, `BKey`, `DefaultDistance` (1886f), `DefaultOn` (true), `DefaultR/G/B`, `Enabled`, `Configure`, `Shell`, `Install`, `SetEnabled`.
- Split note: the settings/env glue and shell verb are generic; `WaterMurk`, the distance semantics and the setting keys are KF2.

#### patches/MouseIndicator.cs  (192 lines)
- Bucket: B (confidence: high) -- host-side pointer-capture indicator; no KF2 RAM, addresses or data layout at all. Trivial game strings: panel name `kf2mouseind`, window id `##kf2mouseind`.
- Does: draws a small white pixel-art mouse (with a diagonal cut when released) in the top-right of the game picture, faded in on capture change.
- Hooks: none. Data: none. Structs: none. Control flow: none. Overlays: none.
- Env / settings keys: none. (`Suppressed` is set by the debug mod's cinematic camera; `MouseIndicator.Show` is called from `Mouse.SetCaptured`/`Mouse.TakeLook`.)
- RecompOne deps: runtime `IFloatingPanel`, `ImGuiNET`, `MapRender.Picture`, `HostWindow`; port types `Mouse`.
- Mod-visible: `public sealed class MouseIndicator : IFloatingPanel` -- `Instance`, `Name`, `IsOpen`, `Suppressed` (settable), `Show(bool)`, `Draw`.
- Split note: none; the whole file is game-agnostic host UI. Only the panel name carries a KF2 string.

#### patches/MoPose.cs  (465 lines)
- Bucket: C (confidence: high) -- the MO keyframe blender in C#, plus a "defer the CPU pose decode and blend in the vertex shader" mechanism; the format/addresses are KF2's.
- Does: replaces `func_80034DA8` (the MO pose blender: keyframe copy + per-vertex delta decode at the clock's weight), optionally deferring the decode to a GPU pose store the shader blends.
- Hooks: **replace** `func_80034DA8` 0x80034DA8, GAME.EXE (`KingsField2`), attached via `SymbolRegistry.Resolve("game",null,Routine)` + `HookManager.AddReplace`; `KF2_MOPOSE=verify` runs both through `Differential`. Direct calls into recompiled GAME: `func_800353E8`, `func_80034834`, `func_8002E1F0`, `func_80035508`, `func_80017798`, `func_80035430`, `func_8003486C` (the clock), `func_80034934`, `func_800349F8`, `func_80034A74` (original decode), plus `Interrupts.Poll`.
- Data: `Routine` 0x80034DA8; `BankTable` 0x8018E1A0 (u32 bank-record pointers, indexed `bank<<2`); `VertexBase` 0x8018EAA0; `Posed` 0x80190AD8.
- Structs: slot record `slot`: u16@+0 state (set 2), u16@+2 bank, u16@+4 clip, u16@+6 segment index, u32@+8 keyframe pointer into the bank table, u32@+C keyframe buffer, u32@+0x10 back-pointer to slot. Bank record `s3`: u32@+4 MO-bank flag (0 = rigid), u32@+C table offset. Segment `seg`: u16@+4 keyframe index, u16@+6 key count, u16@+8 target-key index, key list at `seg+0xA`. Stream: s16 count, then per entry (x,y,z) s16s or `0x8000` + skip; vertex record 8 bytes. Blender stack frame `entry-0x48`; clock writes seg at `SP+0x18` and weight at `SP+0x1C`.
- Control flow: `Differential` verify compares RAM/regs/GTE; `Defer`/`Pending`/`Store` feed `RetainedScene.AddPose`; `Materialize` re-decodes into `Posed` for anything needing RAM; `func_80034A74` (original) used when a count is negative; `Scale` = low word of `(short delta * weight) >> 12`.
- Overlays: `game` (GAME.EXE).
- Env / settings keys: `KF2_MOPOSE` (0/verify); interacts with `KF2_GPUWORLD_POSES`, `KF2_GPUWORLD_POSECHECK`, and `Pgxp.CpuTracking` (honours off when PGXP CPU tracking).
- RecompOne deps: recompone `0009`? no; runtime `Differential`, `Pgxp`, `Interrupts`, `Runtime.RamSize`, `HookManager`, `SymbolRegistry`, `HookAttach`, `ModInfo`, `PSMemory`, `IMemory`, `CpuContext`; port GPU types `RetainedScene`, `RetainedModels`.
- Mod-visible: `public static class MoPose` -- `Configure`, `Install`, `Active`, `Defer`, `Pending`, `Calls`, `Deferred`, `Materialized`, `Rigid`, `Inits`, `Rebuilds`, `Materialize`, `Store`, `StoreRigid`, `PoseBuilds`, `PoseHits`, `PoseRefused`, `RigidBuilds`, `RigidHits`, `Checking`, `CheckVertices`, `CheckDiffer`, `Check`.
- Split note: the C# blender and the defer-to-shader seam are the portable idea; the MO bank/segment/stream layout, the ten routine addresses and the RAM addresses are KF2.

#### patches/NoDither.cs  (353 lines)
- Bucket: C (confidence: high) -- clear the PS1 GPU's dither bit; the GP0/DRAWENV knowledge is generic PS1, but it hardcodes the three KF2 overlays' `PutDrawEnv`/`DrawOTag` addresses.
- Does: makes sure no GP0(E1) draw-mode word with bit 9 set reaches the GPU, by clearing the DRAWENV `dtd` byte and any ordering-table E1 word around the call and restoring them after.
- Hooks: pre/post per overlay, resolved by address and attached with `HookManager.AddPre/AddPost`:
  - `PutDrawEnv`: open 0x800160D0, game 0x80060870, end 0x80013DD8.
  - `DrawOTag`: open 0x80016078, game 0x80060818, end 0x80013D80.
- Data: `DRAWENV+0x16` = `dtd` byte (becomes bit 9 of the E1 word); OT words; `GPUSTAT` bit 9 sampled under the probe. `Runtime.RamWordMask` used to mask OT links.
- Structs: none beyond DRAWENV; OT walk follows each header's `next` field and steps packets by GP0 command length (`Length`), stopping at variable-length commands (polyline 0x40/1<<27, image load 0xA0-0xBF).
- Control flow: borrowed-not-rewritten (pre clears, post restores) so a re-sent packet buffer is not permanently altered; hooks compose with Widescreen's `DrawOTag` Replace and FramePacing's post. `Enabled` live; hooks always attached.
- Overlays: `open`, `game`, `end`.
- Env / settings keys: `KF2_NODITHER_PROBE`; settings key `kf2.nodither.on`; Video ▸ Shading combo (Dither/None/Smooth, shared with `TrueColor`).
- RecompOne deps: runtime `Runtime.View.GetBool`, `Runtime.RamWordMask`, `Runtime.Gpu.ReadStat`, `SymbolRegistry`, `HookManager`, `ModInfo`, `Event`/`RuntimeReadyEvent`/`OverlayLoadedEvent`, `CpuContext`, `IMemory`.
- Mod-visible: `public static class NoDither` -- `OnKey`, `Enabled`, `Configure`, `Install`, `SetEnabled`, `BeforePutDrawEnv`, `AfterPutDrawEnv`, `BeforeDrawOTag`, `AfterDrawOTag`.
- Split note: the GP0-length table, DRAWENV `dtd` and GPUSTAT bit are generic PS1; the six overlay/address pairs are KF2.

#### patches/Mouse.cs  (530 lines)
- Bucket: C (confidence: high) -- mouse look and mouse buttons as another input source; generic mechanism, but it speaks KF2's angle units and rides `Analog`'s hook and the BIOS pad bus.
- Does: turns mouse motion into the per-frame turn/pitch step `Analog.BeforeLook` spends, and ORs mouse buttons into the pad word via `PadReadEvent`.
- Hooks: none of its own -- look is spent inside `Analog.BeforeLook` (already attached); buttons are an `Event.AddListener<PadReadEvent>` attached only while captured; capture key comes off `KeyboardEvent`. Direct references: `Analog.Yaw`, `Analog.Pitch`; `func_8002957C` (the action routine) in comments.
- Data: `Analog.Yaw`/`Analog.Pitch` (12-bit angles, `& 0xFFF`); pad button bit masks from `Controller` (`Cross/Circle/Square/Triangle/L1/R1/L2/R2/Start/Select`); game mask table (stored swapped, active-low `~buffer`) referenced. Angle conventions: yaw 12 bits/turn, D-pad turn rate 0x1C/tick, pitch clamped ±0x2BC.
- Structs: none.
- Control flow: motion drained every drawn frame from stage 8 (`Poll`) and again by `TakeLook`; spent once per tick by `Analog.BeforeLook` (`NoteSpent`/`SpentThisFrame`); `StaleMs` 250 drops motion across a menu/load; `StepCap` 1024/frame; `Lead` decides whether `FrameSmoothing` shows the motion before the tick spends it; `Live` ties liveness to `FramePacing.Paused`/`LogicHz`.
- Overlays: none directly (no game addresses; all through `Analog`).
- Env / settings keys: `KF2_MOUSE`, `KF2_MOUSE_TURN`, `KF2_MOUSE_LOOK`, `KF2_MOUSE_INVERTY`, `KF2_MOUSE_BUTTONS`, `KF2_MOUSE_KEY`, `KF2_MOUSE_LEAD`; settings keys `kf2.mouse.on/turn/look/inverty/left/right/middle/capturekey/lead`; Input pane `MousePage`.
- RecompOne deps: runtime `Event`, `KeyboardEvent`, `PadReadEvent`, `HostWindow` (`PumpInput`, `TakeMouseMotion`, `MouseCaptured`, `IsMouseButtonDown`), `PopupManager`, `HotkeyGate`, `ToastNotifications`, `Controller`, `FramePacing`; port types `Analog`, `MouseIndicator`.
- Mod-visible: `public static class Mouse` -- all `*Key` consts, `Enabled`, `Lead`, `TurnSens`, `LookSens`, `InvertY`, `LeftButton`, `RightButton`, `MiddleButton`, `CaptureKey`, `Captured`, `Configure`, `Install`, `SetCaptured`; internal `PadButtons`, `CaptureKeys`, `Poll`, `Pending`, `TakeLook`, `NoteSpent`, `SpentThisFrame`.
- Split note: host pointer/button plumbing and the "OR a synthetic button into the pad word" idea are generic; the angle units, mask table, sensitivity semantics and `Analog` coupling are KF2.

#### patches/ObjectSmoothing.cs  (996 lines)
- Bucket: C (confidence: high) -- carry non-player world tables between logic ticks; the interpolation mechanism generalizes, the tables / tests / addresses are KF2's.
- Does: pre-hook on stage 13 samples the renderer's four world tables, lerps each drawn record's position and rotation to the tick fraction, and a post-hook puts every table back exactly as the game wrote it.
- Hooks: pre+post (`Before`/`After`) on stage 13 `func_800342D8` 0x800342D8, GAME.EXE; both must install or it disables itself (`_paired`). Attached by `SymbolRegistry.Resolve("game",null,Renderer)` + `HookManager.AddPre/AddPost`.
- Data: `TeleportUnits` 1024, `RaisedUnits` 8192, `GlidingFactor` 4, `AngleMod` 0x1000 (yaw bias 0x800 = half a turn, so a turn is 4096); stage-13 marker `0x800342D8`; positions are `VECTOR` (3×u32) and rotation 3×s16 with a `0x800` yaw bias applied by the renderer.
- Structs -- **every table, in full** (`TableSpec(Label, Noun, Base, Stride, Count, TestOff, TestWidth, TestValue, PosOff, RotOff, Fast, DrawnWhenEqual)`; liveness is the *renderer's drawn test*: `(value == TestValue) == DrawnWhenEqual`):
  | label | noun | base | stride | count | liveness test | fields used | flags |
  |---|---|---|---|---|---|---|---|
  | entities | creature | 0x8016C544 | 0x7C | 0xC8 (200) | `u8[+0x9] == 1` (TestOff 9, width 1, value 1, equal) | position `VECTOR` at +0x2C (PosOff), rotation 3×s16 at +0x40 (RotOff) | `Fast: true` |
  | objects | object | 0x80177714 | 0x44 | 0x18C (396) | `u16[+0x6] != 0xFF` (TestOff 6, width 2, value 0xFF, not equal) | position at +0x14, rotation 3×s16 at +0x24 | -- |
  | effects | effect | 0x8019CC6C | 0x48 | 0x80 (128) | `u8[+0x0] != 0xFF` (TestOff 0, width 1, value 0xFF, not equal) | position at +0x14, rotation at +0x24 | -- |
  | sprites | sprite | 0x80195174 | 0x18 | 0x80 (128) | `u16[+0x0] != 0xFFFF` (TestOff 0, width 2, value 0xFFFF, not equal) | position at +0x8, no rotation (RotOff -1; renderer passes a zeroed triple) | -- |
  (Notes: the entity table is drawn by the renderer's first loop with `S0=base+3`, `ReadU8(S0+6)==1`, stride 0x7C; the object table's rotation triple is built at rec+0x24/+0x26+0x800/+0x28; `func_800331B4`'s two loops feed `func_80032588`; `AgentServer` `nearby` reports these same constants.)
- Control flow: bracket is stage 13 (`func_800342D8`), the only display-list filler; the world tick is identified by `FramePacing.FirstWalkOfTick`; `_rebase` on `OverlayLoadedEvent` (executable swaps + fdat area modules) rebases `Prev=Cur`; a slot is live only once drawn for two samples (`WasFree`); placement guard (1024/8192×4) decided once per tick and reused; `_held` publishes refused position addresses for `AnimSmoothing` (`PositionHeld`).
- Overlays: `game` (GAME.EXE); listeners reset on any overlay load (fdat modules too).
- Env / settings keys: `KF2_SMOOTH_OBJECTS` (0=off), `KF2_SMOOTH_OBJECTS_PROBE`; settings keys `kf2.smoothing.objects`, `kf2.smoothing.objects.guard`; Video plus `Guard` setting via `SetPlacement`.
- RecompOne deps: runtime `Event`/`RuntimeReadyEvent`/`OverlayLoadedEvent`, `HookManager`, `SymbolRegistry`, `HookAttach`, `ModInfo`, `Runtime.View.GetBool/GetInt`, `CpuContext`, `IMemory`; port types `FramePacing`, `AnimSmoothing`, `SpriteAnim`, `AgentServer`.
- Mod-visible: `public static class ObjectSmoothing` -- `Guard` enum, `Placement`, `OnKey`, `GuardKey`, `Enabled`, `PositionHeld`, `Configure`, `Install`, `SetEnabled`, `SetPlacement`, `Before`, `After`, `TakeHealth`; internal table specs/state.
- Split note: the sample/lerp/restore bracket and the placement guard generalize to any fixed-stride record table; the four base/stride/test/offset rows and the stage-13 bracket are KF2.

#### patches/ModelWalk.cs  (1608 lines)
- Bucket: C (confidence: high) -- the moving-world table walk and model submitter in C#; the mechanism (walk record tables, resolve matrix/light/assembler) generalizes, every table/address is KF2's.
- Does: replaces `func_800331B4` (the four table walks), `func_80032588` (one model: matrices, light, assembler), `func_80032400` (first-person arm) and `func_80032AC4` (object kind `0xF0`), which is how the port learns *which model, where, which pose, which assembler*.
- Hooks (GAME.EXE): **replace** `Walk` 0x800331B4, `Submit` 0x80032588, `Arm` 0x80032400, `Special` 0x80032AC4 (each `SymbolRegistry.Resolve("game",null,addr)` + `HookManager.AddReplace`); `KF2_MODELWALK=verify` diffs each against the recompiled routine (`Verify`/`Check`); each half can be left recompiled by its own switch. Direct calls into recompiled GAME: `func_8003CE44`, `func_80032DE8`, `func_80032D78`, `func_80032CD8`, `func_80032EAC`, `func_80037810`, `func_80014158`, `func_8003309C`, `func_80032FAC`, `func_800172A4`, `func_80034834`, `func_8002E1BC`, `func_8002E1F0`, `func_80014FE0`, `func_80034DA8`, `func_8002EA60`, `func_8002F918`, `func_8002E650`, `func_8002F214`, `func_8002E9B8`, `func_80030540`, `func_8002EAEC`, `func_8002DDDC`, `func_80015930`, `func_800158C8`, `MulMatrix0/2`, `RotMatrix`, `ScaleMatrix`, `SetRotMatrix`, `SetTransMatrix`, `SetColorMatrix`, `SetLightMatrix`, `SetBackColor_game`, `RotTrans`, plus `Interrupts.Poll`.
- Data: `CreatureDefs` 0x80172624 (120 B/creature, +7/+8 = two wanted pages); `ObjectDefs` 0x80175914 (24 B/object, +2 page, +0xC second-query mask, +0 = kind); `ViewMatrix` 0x80192E18, `FlatMatrix` 0x80192E38, `RomMatrix` 0x80064B30; camera world `CamWorldX/Y/Z` 0x80192E78/7C/80; `MapBase` 0x801C8484; `LightBase` 0x801930F0 (0x68/104 B per light record); `HalfSelect` 0x8019953C; `PlayerPos` 0x801994EC, `PlayerY` 0x801994F0, `PlayerZ` 0x801994F4; `SoundClock` 0x801B6CAC; `EffectSounds` 0x801989D8 (10 B/effect sound id); `ObjectMask` 0x801B69BC; `SpriteClock` 0x80195170; arm: `SwingClock` 0x801994A4, `SwingClip` 0x801994AE, `ArmSlot` 0x8019949C, `Weapon` 0x80199494. Angle units 12-bit (yaw `+0x800` bias), position `VECTOR` 3×u32, map tile = `(z*800 + x*10)` on an 80-wide map, light stride 104.
- Structs -- **the four walk tables, in full** (all confirmed by the strides/counts read out of the routine; liveness is the walk's own skip test):
  | table | base | count | stride | liveness | fields used |
  |---|---|---|---|---|---|
  | creatures | 0x8016C544 | 200 (0xC8) | 0x7C | `u8[+0x9] == 1` | +1 model id (u8, submit `+0x80`); +2 def index (`*15<<3` = ×120 into CreatureDefs); +3 visibility mask (u8, `+0x20` if flag 0x2000); +0xC MO record; +0x13 u8, +0x14 u8, +0x15 s8, +0x16 s16, +0x18 u16 (submit stack args); +0x28 flags u32 (0x80000 volume query, 0x2000 mask+0x20, 0x20 place-at-record/no-rotation/fixed matrix); +0x2C position VECTOR (query/submit); +0x40/+0x42/+0x44 rotation x/y/z s16 (y `+0x800`); +0x48 scale ptr, +0x5C second ptr |
  | objects | 0x80177714 | 396 (0x18C) | 0x44 | `u16[+0x6] != 0xFF` | +0 visibility mask (u8); +1 MO record; +2 assembler byte; +3 kind byte (bit1 volume query, bit0 force assembler; bit 0x80 set after draw); +4 kind (u8: 0x1F ambient, 0xF0 special); +5 u8, +0x6 model id u16 (submit `+0x100`), +0xA u16, +0xE s16, +0x10 u16 (submit args); +0x14 position VECTOR; +0x24/+0x26/+0x28 rotation x/y/z s16 (y `+0x800`); +0x2C/+0x34 stack args; special `0xF0`: +0x38/+0x39 half extents, +0x3A slot, +0x3B light byte (`&0x7F` light, `&0x80` negated light matrix), +0x3C rate, +0x3D u8, +0x3E u16 interval, +0x40 due clock |
  | effects | 0x8019CC6C | 128 (0x80) | 0x48 | `u8[+0x0] != 0xFF` | +3 clip/half byte (submit `+0x28`); +4 MO record; +8 gate (low2 live: 0 skip, 2 skip visibility query; bits2-3 placement: 0 view/rotated, 4 RomMatrix, 8 FlatMatrix, 0xC no matrix bias 20); +9 weight; +0xA visibility mask; +0xC u8, +0x10 s16, +0x12 u16 (submit args); +0x14 position VECTOR; +0x24/+0x26/+0x28 rotation; +0x2C/+0x3C stack args (stage 5 lifetime at +0x0E) |
  | sprites | 0x80195174 | 128 (0x80) | 0x18 | `u16[+0x0] != 0xFFFF` | +0 model id u16 (submit `+0x28`); +2 visibility mask; +3 strip length (cel wrap); +4 cel interval; +5 cel index; +8 position VECTOR; always FlatMatrix, no rotation |
  Also scratch: two frame bitmaps (texture pages at `sp+0x58`, CLUT pages at `sp+0x198`) are memset and spent through `func_8003309C`/`func_80032FAC`.
- Control flow: walk order creatures → (pages/cluts) → objects → (pages/cluts) → effects → sprites; `SpriteClock` bumped once per walk at the end; `Interrupts.Poll` kept at recompiled loop heads; `func_80032588`'s nine stack args read from the caller's frame (`scale`, MO rec, matrix, clip byte, MO clip time, second light, blend weight, assembler, OT depth); a matrix of 0 is view-space placement (position raw, light from the camera's tile); `Placed` set when matrix == ViewMatrix; `_walkOwns` records the scene entry (`WalkObjects`/`Ordinary`/etc.); the walk extends visibility through `RenderDistance`, `ReflectionReach`, `PlanarCull` (`_mirrorOnly` -> `PlanarWalk.Record`, not drawn); assembler byte 0xFF lit, 0xFE flat, else semi-transparent blend; `func_80032AC4` special and `func_80032400` arm each save registers through a manual stack frame.
- Overlays: `game` (GAME.EXE).
- Env / settings keys: `KF2_MODELWALK`, `KF2_MODELWALK_WALK`, `KF2_MODELWALK_SUBMIT`, `KF2_MODELWALK_ARM`, `KF2_MODELWALK_SPECIAL`, `KF2_MODELWALK_PROBE`, `KF2_GPUWORLD_SUBTEST`; Video ▸ Fast geometry (via PolyAssembler/RetainedModels).
- RecompOne deps: recompone `0085` (GPU world renderer's models/meshes/poses/arm/special) referenced throughout; runtime `Pgxp.CpuTracking`, `Interrupts`, `Gte`, `HookManager`, `SymbolRegistry`, `HookAttach`, `ModInfo`, `PSMemory`, `IMemory`, `CpuContext`; port types `PolyAssembler`, `MoPose`, `RetainedModels`, `PlanarWalk`, `PlanarCull`, `RenderDistance`, `ReflectionReach`, `Remaster.Props`, `Remaster.Surfaces`, `SpriteAnim`.
- Mod-visible: `public enum ModelKind : byte { Creature, Object, Effect, Sprite }`; `public readonly struct ModelDraw` (Kind, Slot, Record, Model, X, Y, Z, Assembler, Light); `public static class ModelWalk` -- `Enabled`, `WalkEnabled`, `SubmitEnabled`, `ArmEnabled`, `SpecialEnabled`, `SubtractTest`, `Verifying`, `WalkCalls`, `SubmitCalls`, `ViewSpaceSubmits`, `SubmitKind`, `SubmitRecord`, `SubmitSlot`, `MirrorOnlySubmits`, `SubmitModel`, `SetSubmit`, `ObjectKind`, `SolidKind`, `InWalk`, `Placed`, `PlacedRot`, `PlacedX/Y/Z`, `Scene`, `InSpecial`, `InArm`, `Configure`, `Install`, `Ordinary`.
- Split note: the "walk N record tables, resolve each into matrix+light+assembler, publish the scene" pattern generalizes; the four base/stride/count/liveness rows, the submit stack-arg layout, the light/map strides and every routine address are KF2.

### Summary

Counts over the 13 files (Batch 4: Menu* / Message* / Mo* / Mouse* / Murk / NoDither / ObjectSmoothing):

- **A. RecompOne fork: 0**
- **B. Game-agnostic infrastructure: 1** -- `MouseIndicator.cs`
- **C. Game patch whose mechanism might generalize: 11** -- `MenuDraw`, `MenuMouse`, `MenuPacing`, `MenuWorld`, `MessageText`, `MoPose`, `ModelWalk`, `Mouse`, `Murk`, `NoDither`, `ObjectSmoothing`
- **D. KF2-only: 1** -- `MessageGlyphs.cs`

Important observations:

1. **The C bucket is defined by hardcoded GAME.EXE/overlay addresses**, not by KF2 knowledge in the abstract. Every C file here hooks or calls named routine addresses resolved through `SymbolRegistry.Resolve("game"|"open"|"end", null, addr)` and reads RAM by literal address; the *idea* of each (menu pointer, live menu world, pose deferral, dither clear, mouse look, object interpolation) would port, the addresses would not.
2. **A shared "table map" is the single highest-value seam.** `ObjectSmoothing` and `ModelWalk` describe the *same four world tables* (`entities` 0x8016C544/0x7C/200, `objects` 0x80177714/0x44/396, `effects` 0x8019CC6C/0x48/128, `sprites` 0x80195174/0x18/128) with the *same* liveness polarity and field offsets, but each restates base/count/stride/test separately. A sibling game needs to supply exactly this map, and the tests published here are the groundwork for it.
3. **Stage 13 (`func_800342D8`) is the hub of the world patches**: `ObjectSmoothing` brackets it, `MenuWorld` replays its drawing half against a private OT, and `MoPose`/`ModelWalk` are called from within it. Any port would want an equivalent "renderer entry" identity.
4. **The menu framework is a second cluster with a fixed shape**: enter `func_80022754`, frame head `func_80022530`, presenter `func_800226A8`, leave `func_800228C8`, modal loop `func_80018E80`, pad read `func_80022E58`; `MenuWorld`, `MenuPacing` and `MenuMouse` each hook a different part. This is a game-specific API a sibling game would have to re-identify, not a generic runtime concept.
5. **The 12-bit angle convention recurs and should be a shared constant**: `0x1000` per turn, yaw `+0x800` bias applied by the renderer, pitch clamped `±0x2BC`; used by `Mouse`, `ObjectSmoothing` (`AngleMod`), and `ModelWalk` (creature/object/effect rotation).
6. **Generated data is the only D**: `MessageGlyphs` is a disc-derived font hash table (`scripts/msg_glyphs.py`); a sibling game regenerates it, so the file is a build artifact of the target, not shared code.
7. **The port's own runtime layer is the actual reusable substrate** and is used by almost every file: `HookAttach`, `SymbolRegistry`, `HookManager`, `ModInfo`, `Event`/`OverlayLoadedEvent`, `ScenePass`, `PrimBuffer`, `FramePacing`, `Gte`, `Differential`. That layer (plus `FrameSmoothing`/`ObjectSmoothing`-style interpolation scaffolding) is the B material hiding inside C files.
8. **Comparison/verify modes are pervasive and are the migration tool**: `KF2_*_PROBE`, `KF2_*_verify` and explicit `Differential`/`Check` implementations (`MenuDraw.Reference`, `MoPose`, `ModelWalk`, `NoDither`) let a sibling port diff C# against recompiled MIPS. Preserve these.
9. **`KF2_` prefix + settings keys are the only cross-cutting game strings** in otherwise generic code; the mod-visible public surface (`ModelKind`, `ModelDraw`, `Mouse`, `Murk`, `MenuPacing`, `MenuWorld`, `ObjectSmoothing`, `MoPose`, `NoDither`) is what runtime mods compile against and must stay stable or be renamed together.
10. **Three of these patches default off / are judged-by-eye pending**: `MessageText` (off, judged unwanted), `Murk` (on but no sliders, tuning judged), `MenuMouse`/`Mouse`/`ModelWalk` have never-judged-by-eye caveats. None of the C classification implies shipped quality.

## patches/, PacketMatch to ReflectionReach
Read-only classification per `scratch/sharing/BRIEF.md`. Files classified, in order:
PacketMatch, PerPixelLighting, Perspective, Pgxp, PlanarCull, PlanarWalk,
PolyAssembler*, PositionalAudio, Prejit, PrimBuffer, ProfilerPanel, RateCensus,
ReflectionReach.

#### patches/PacketMatch.cs  (815 lines)
- Bucket: C (medium) -- the question ("can a primitive be recognised in the previous tick's list, and does it carry pose") is game-agnostic; every conclusion is drawn against KF2 addresses and the KF2 submitters.
- Does: a read-only probe that walks the OT at `DrawOTag`, attributes each packet to the model/map submitter that built it, and reports match rates (ordinal K1 / lerpable K2 / intrinsic K3), collisions, displacement and pose.
- Hooks: none committed to game logic; probe only, and only when `KF2_PACKETMATCH`. Pre `DrawOTag` at `open 0x80016078`/`game 0x80060818`/`end 0x80013D80`; pre/post pairs on `game` `func_80032588` (model submitter, context id = `c.A2` position pointer) and `func_80031950` (tile submitter, context id = `c.A0` = tile record + half offset). Attached via `HookManager.AddPre/AddPost` + `Commit`, `SymbolRegistry.Resolve`.
- Data: `ActiveDescriptor=0x8017E0A4` (arena `{start,end,cur}`), `ComposedYaw=0x80199506` (u16 wrapped to 12 bits, 0x1000/turn), `PosX=0x801994EC`, `PosZ=0x801994F4`; same as `FrameSmoothing`. Reads OT via `Runtime.RamWordMask`.
- Structs: OT node header `n>>24` count + 24-bit `next` ptr; polygon flags bit28 gouraud /27 quad /26 textured /25 semi /24 raw; per-vertex pos word (11-bit signed x/y), optional UV word (low16 UV, high16 CLUT/texpage on verts 0-1); arena span `{Lo,Hi,Kind,Id}`; context `(Kind,Id)`.
- Control flow: samples only tick frames (`FramePacing.TickedThisFrame`); arena rewound at head of stage 13, so spans dropped each frame; area change (`OverlayLoadedEvent`) forgets all.
- Overlays: `game` (and `open`/`end` DrawOTag sites); function addresses are GAME.EXE.
- Env / settings keys: `KF2_PACKETMATCH` (1/2).
- RecompOne deps: `HookManager`, `SymbolRegistry`, `Runtime.RamWordMask`, `FramePacing.TickedThisFrame`; references `FrameSmoothing`, `ObjectSmoothing`, `DrawCensus`.
- Mod-visible: `PacketMatch.Configure/Install`; public `BeforeDrawOTag`, `PreModel/PostModel/PreTile/PostTile`; nothing meant for mods.
- Split note: the scheme (OT decode, three keys, pose split, surviving-context split) is generic; the KF2 seam is the two submitter addresses and the yaw/pos reads. A sibling game reparameterises the contexts.

#### patches/PerPixelLighting.cs  (181 lines)
- Bucket: C (high) -- "evaluate the GTE's lighting and depth cue per pixel instead of interpolating corner colours" is generic; the curve constants and who is excluded (HUD) are KF2/SDK facts.
- Does: the switch and probe for the runtime's `GteLightMap`; enables per-pixel lighting and reports recorded/lit/fallback counts.
- Hooks: probe only. `HookAttach.OnOverlayLoad` builds a pre/post pair on the HUD builder `game 0x80031D5C` to set/clear `PolyAssembler.InHud`; DrawOTag `open 0x80016078`/`game 0x80060818`/`end 0x80013D80` post-hooks only when probing.
- Data: none directly; `PolyAssembler.InHud` flag; reads `GteLightMap` counters. Curve constants referenced in docs (IR0 2800 knee, 3x slope).
- Structs: `GteLightMap.Rec` (runtime).
- Control flow: excludes the HUD (icons drawn through the lit assembler); GL core backend only (`GteLightMap.Supported`).
- Overlays: `game` (HudBuilder); DrawOTag sites in all three.
- Env / settings keys: `KF2_PERPIXEL`, `KF2_PERPIXEL_PROBE` (1/2); setting `kf2.perpixel.on`; `RuntimeReadyEvent`, `OverlayLoadedEvent`.
- RecompOne deps: patch `0048`; runtime types `GteLightMap` (`Enabled`, `Supported`, `Active`, `Directional`, `Hits/Misses/Recorded`), `HookAttach`, `SymbolRegistry`, `HookManager`.
- Mod-visible: `PerPixelLighting.Enabled`, `SetEnabled`, `Configure/Install`, `NoteCorner`, `Check`, `CheckFlat`, `Fallback`, `BeforeHud/AfterHud`.
- Split note: mechanism in `GteLightMap`/`0048`; KF2 seam is the HudBuilder address and the curve constants baked in the shader/assembler.

#### patches/Perspective.cs  (215 lines)
- Bucket: B/A (high) -- the perspective-correction mechanism is entirely in the runtime; this file is a switch + probe with KF2 hook addresses. The DrawOTag addresses are the only game coupling.
- Does: switch for `GteDepth` perspective-correct textures and a probe of the vertex map's hit rate (vertices projected, caught, copied, lookups, hit %, saturated).
- Hooks: probe only, post `DrawOTag` at `open 0x80016078`/`game 0x80060818`/`end 0x80013D80` for a frame boundary to count against. Composes with Widescreen's `Replace` (post, not replace).
- Data: none directly; reads `GteVertexMap`/`GteDepth` counters.
- Structs: none in file; `GteDepth`, `GteVertexMap` runtime.
- Control flow: on from first projected vertex before config read; `RuntimeReadyEvent` applies saved setting; fallback-by-position is a comparison only.
- Overlays: `open`/`game`/`end` DrawOTag sites.
- Env / settings keys: `KF2_PERSPECTIVE`, `KF2_PERSPECTIVE_PROBE`, `KF2_PERSPECTIVE_FALLBACK`; setting `kf2.perspective.on`; `RuntimeReadyEvent`, `OverlayLoadedEvent`.
- RecompOne deps: patches `0009`, `0012`; runtime types `GteDepth` (`Enabled`, `PositionFallback`, `Recorded`, `Saturated`, `Subpixel`), `GteVertexMap` (`Hits`, `Misses`, `Roots`, `Propagated`), `HookManager`, `SymbolRegistry`.
- Mod-visible: `Perspective.Enabled`, `SetEnabled`, `Configure/Install`, `AfterDrawOTag`, `OnKey`; interacts with `Pgxp.Reload`.
- Split note: mechanism wholly runtime (`GteVertexMap`/`GteDepth`); only the three DrawOTag addresses and the KF2 naming keep it here. Nearly pure B with trivial game strings.

#### patches/Pgxp.cs  (318 lines)
- Bucket: B (high) -- upstream RecompOne's PGXP, re-exposed; the only KF2 coupling is the three DrawOTag probe addresses.
- Does: the switch between the port's address-map/GTE depth source and upstream PGXP; reloads PGXP settings from the view store with env overrides winning, reports coverage and disagreement.
- Hooks: probe only, post `DrawOTag` at the same three addresses; `Configure` arms `Rp.Pgxp.CpuHooksArmed` before recompiled code is compiled.
- Data: none directly; reads `Rp.PgxpStats`.
- Structs: upstream PGXP's own; `GteDepth` for the texture-correction link.
- Control-flow: off until config read; `Reload` writes forced values into the view store, loads, restores; frees the 64 MB vertex cache when off.
- Overlays: `open`/`game`/`end` DrawOTag sites.
- Env / settings keys: `KF2_PGXP`, `KF2_PGXP_TEXTURE`, `KF2_PGXP_CULLING`, `KF2_PGXP_CPU`, `KF2_PGXP_MEMORY`, `KF2_PGXP_VERTEXCACHE`, `KF2_PGXP_CACHEW`, `KF2_PGXP_TOLERANCE`, `KF2_PGXP_PROBE`; upstream PGXP keys via `Rp.Pgxp.Key*`. No port setting key (env-only, no window control).
- RecompOne deps: patches `0034`-`0036`; runtime types `RecompOne.Runtime.Pgxp.Pgxp` (KeyEnable, KeyTextureCorrection, KeyCulling, KeyCpu, KeyMemory, KeyVertexCache, KeyCacheW, KeyTolerance, Load, Enabled, Tolerance...), `PgxpGate`, `PgxpGte`, `PgxpStats`, `PgxpGpu`, `Runtime.View`.
- Mod-visible: `Pgxp.Enabled`, `Status`, `Configure/Install`, `Reload`, `AfterDrawOTag`, `OnKey`.
- Split note: mechanism wholly in the vendored runtime; only the probe hook addresses are KF2. Nearly pure B.

#### patches/PlanarCull.cs  (162 lines)
- Bucket: C (high) -- "the mirror walks the cone without the eye's occlusion flood" generalises; every address is KF2 map/grid/camera RAM and it calls the KF2 half routine.
- Does: computes, per frame, every half on the eye's level inside the view cone out to the render distance, admitting mirror-only cells/models for the planar walk.
- Hooks: none; driven by `PlanarWalk`/`ModelWalk`/`TileWalk` and `RenderDistance`. `Walk` direct-calls `Recompiled.KingsField2_game.func_80031B1C` with `c.RA=0x80031D14`.
- Data: `Grid=0x80192EAC`, `GridOriginX=0x80192EA0`, `GridOriginZ=0x80192EA4`, `CamWorldX=0x80192E78`, `CamWorldZ=0x80192E80`, `MapBase=0x801C8484`; `CameraBlock.ViewMatrix`. Conventions: world>>11 = tile; map half byte at `MapBase + tz*800 + tx*10 + halfOff` (0xFF empty); grid byte bit0 lower / bit1 upper; marker = whichever level the flood lit most.
- Structs: 80x80x2 halves (`_bits`), map cells 10-byte stride, 800-byte row, per-level half offset 0/5; 24x24 grid.
- Control flow: `Build` from end of frame's tile walk before object walk queries; pitched cone widened by pitch; `NearTiles=3` marked for the clipper.
- Overlays: `game` (globals + `func_80031B1C`).
- Env / settings keys: `KF2_PLANAR_CULL`.
- RecompOne deps: `CullCone.Factor`, `GteDepth.ProjH`, `Interrupts`; references `RenderDistance`, `ModelWalk`, `CameraBlock`, `PlanarWalk`.
- Mod-visible: `PlanarCull.On`, `Any`, `Frames/Added/Models`, `Pitched`, `Configure/Build/Walk/Point/Box`.
- Split note: generic "cone without flood" idea; seam is all the KF2 addresses and the `func_80031B1C` direct call.

#### patches/PlanarWalk.cs  (492 lines)
- Bucket: C (high) -- planar reflections by walking the world twice is a general technique; the walk/submit/camera addresses and scene-pass state are KF2.
- Does: after the game's object walk, saves the camera block, rebuilds it mirrored in the water plane, re-walks the tile grid and replays every recorded model submit into the port's own OT/arena, then hands it to `DrawOTag` with `PlanarReflections.Capturing` at the frame's DrawOTag.
- Hooks: pre/post on `game` `func_800331B4` (object walk) at `0x800331B4`; pre on `func_80032588` (submitter) `0x80032588`; pre on `DrawOTag` `0x80060818`. Attached via `HookAttach.OnOverlayLoad`, `AddPre/AddPost`, `Commit`, `HookAttach.Installed`.
- Data: `CameraBlock.ViewMatrix`/`Position`; `ViewMatrix` row reads; `ModelTable=0x8018E1A0`; `func_80032CD8` residency test; `WalkFrame=0x300`; `Walk`/`Submit`/`DrawOTag` addresses. Calls `KingsField2.func_80031C94`, `func_80032588`, `CameraBlock.Build`, `Sdk.LibGpu.DrawOTag`.
- Structs: recorded submit (4 regs, 9 stack words at SP+0x10, position 12 B @A2, rotation 8 B @A3, ModelKind/slot/record, taken flag); camera block; `Camera.Read`; mirrored planes `_clip`/`_view`/`_level`; `ModelKind` enum.
- Control flow: inside stage 13 after object walk; records only main walk (`_replaying` guard); runs at frame DrawOTag before game table; plane comes from backend's water classification (`TakePlane`); sky/arm/matrix-0/kind 0xF0 not reflected; evicted models skipped.
- Overlays: `game`.
- Env / settings keys: `KF2_PLANAR`, `KF2_PLANAR_TOLERANCE`, `KF2_PLANAR_RIPPLE`, `KF2_PLANAR_BIAS`, `KF2_PLANAR_PROBE`, `KF2_PLANAR_FOG`; setting `kf2.ssr.planar`; `RuntimeReadyEvent`, `OverlayLoadedEvent`.
- RecompOne deps: patch `0067`/`0068`; runtime `PlanarReflections` (`Enabled`, `Supported`, `Tolerance`, `Ripple`, `LevelFog`, `Probe`, `Capturing`, `SetCamera`, `TakePlane`, `ClipPlane`, `ViewPlane`, `LevelAxis`, `Captures`...), `ScreenReflections`, `SurfaceMaterial`, `ScenePass`, `PrimBuffer`, `RetainedMap/Scene`, `Recompiled.KingsField2_game`, `Sdk.LibGpu.DrawOTag`, `Hle.GpuTrace`.
- Mod-visible: `PlanarWalk.Enabled`, `SetEnabled`, `Configure/Install`, `Strength`, `SetStrength`, `Mirroring`, `Replaying`, `BeforeWalk/AfterWalk`, `BeforeSubmit/Record/TakenLast`, `BeforeDrawOTag`, `DefaultStrength`.
- Split note: mechanism is generic ("mirror the camera, walk again, replay submits, ScenePass"); the KF2 seam is the camera block, tile walk, submitter and model table. A sibling game reparameterises those.

#### patches/PolyAssembler.cs  (1181 lines)
- Bucket: C (high) -- the idea "reimplement the game's geometry assemblers in C# with hooks, verify mode and fast paths" generalises; the routines, addresses, GTE sequences and packet layouts are KF2/PSY-Q.
- Does: `func_80030540` (polygon assembler) and `func_8002FECC` (unclipped far tiles), the transforms `func_8002E650`/`func_8002E7CC`, plus the `Check`/`Verify` harness and the `Frame` RAM-access fast path; clipper rejection; whole-polygon facing; scratch helpers.
- Hooks (replace): `func_80030540` (Assembler), `func_8002FECC` (Unclipped), `func_8002E650` (Transform), `func_8002E7CC` (NearTransform), `func_8002F214` (Lit, in Lit.cs), `func_8002EAEC` (LitBlend, Lit.cs), `Clip4FTP`/`Clip3FTP` (Clip.cs), `func_8002E910` (Hud, Hud.cs), `NormalClip 0x8005DC6C` (in Clip.cs). `HookManager.AddReplace` + `Commit` + `HookAttach.Installed`.
- Data: `Assembler=0x80030540`, `Unclipped=0x8002FECC`, `Transform=0x8002E650`, `NearTransform=0x8002E7CC`; `FogMode=0x80192EA8`; `VertexCache=0x8018EB94` (sxy,otz,fog 8 B/vertex); `VertexBase=0x8018EAA0`; `ModelTable=0x8018E19C`; `OtBase=0x8018E0A8`; `PrimDescriptor=0x8017E0A4`; `LightColour=0x8006E604`; `ClipOut=0x80192A18`; clip state `ClipRecords=0x8006E7B0`, `ClipXScale=0x800FC97C`, `ClipYScale=0x800FC98C`, `ClipFar=0x8012E99C`, `ClipNear=0x8017E07C`. Packet sizes `0x34` GT4 / `0x28` GT3; OT slots `0x2000`, otz clamp min 16, max `0x1FFF`.
- Structs: model header 28 B records at `ModelTable`; packet POLY_GT3/GT4 layouts (writes at +3,+7,+8,+0xC,+0x10,+0x18,+0x1C,+0x20,+0x24,+0x2C,+0x30); `Saved` register set; `Frame` fast-path hoist state; clip records `0x2C`.
- Control flow: per-face loop polls interrupts; bump allocator abandons the call on overflow; near transform otz 0xFFFF unless flag==0x1000; fog curves by FogMode (>=32000 none, bit15 knee else). Verify records/replays both versions and restores the recompiled result.
- Overlays: `game` (all routines).
- Env / settings keys: `KF2_POLYASM` (0/verify), `KF2_POLYASM_REJECT` (0/replay), `KF2_POLYASM_UNCLIPPED`, `KF2_POLYASM_TRANSFORM`, `KF2_POLYASM_LIT`, `KF2_POLYASM_CLIPPER`, `KF2_POLYASM_FACING` (in Clip.cs), `KF2_GTE_FAST`; setting `kf2.fastgeometry.on`; `RuntimeReadyEvent`.
- RecompOne deps: patch `0047`; runtime `Gte` (Rtps, Nclip, MvmvaOp, NccsOp/NcdsOp/NcdtOp, Dpcs, Gpf/Gpl, Read/WriteControl, FastLighting, LightCache, Save/Load/Diff, State), `GteVertexMap` (`Active`, `MaybeBound`, `Peek`, `CurveWord`...), `PSMemory.DirectRam`, `HookManager`, `SymbolRegistry`, `Interrupts.SlowPolls`, `Recompiled.KingsField2_game`, `KingsField2.Clip4FTP/Clip3FTP`, `KingsField2.func_800302E8`, `KingsField2.func_8002E7CC`, `KingsField2.func_8002E650`, `CpuContext.Snapshot/Restore`, `MemoryMap`.
- Mod-visible: `PolyAssembler.Enabled`, `FastGeometry`, `SetFastGeometry`, `UnclippedEnabled`, `TransformEnabled`, `RejectEnabled`, `ReplayRejection`, `Verifying`, counters `AssemblerCalls/ClipperCalls/Rejections`; `FaceKept`, `TileFaceClips`, `TileFaceKept`, `CullKept/CullDropped`, `ScreenVertices/...` (Hud.cs); `InArm`, `InModel`, `TileMaterial`, `BlendedByKind`, `SolidPackets`, `KeepUnfogged`, `InHud`, `LitEnabled`, `LitCalls`, `FastGeometryKey`.
- Split note: the harness (hooks, verify, Frame fast path, Saved) is B-flavoured; the routine bodies are the game's exact code and packet layout. Seam is per-routine: swap `Assembler`/`Unclipped`/`Transform` bodies for a sibling game but keep `Frame`/`Check`/verify.

#### patches/PolyAssemblerClip.cs  (502 lines)
- Bucket: C (high) -- view-space clipper in C#; PSY-Q `ClipFT` algorithm but KF2 addresses/records.
- Does: `Clip4FTP`/`Clip3FTP` in C# (copy vertices into `0x2C` records, `RotTrans`/bound, six `ClipFT` passes, `RotTransPers` survivors) plus `NormalClip` replacement and whole-polygon facing (`ClippedFacing`, `QuadFaces`, `Area`, `Nc`).
- Hooks (replace): `Clip4=0x8005CAC8`, `Clip3=0x8005CA48`, `NormalClipAddress=0x8005DC6C`.
- Data: `ClipYEdge=0x800FC994`, `ClipXEdge=0x800FC984`; `ClipRecords`, `ClipXScale`, `ClipYScale`, `ClipFar`, `ClipNear`, `ClipOut` (from main file). Record layout: SVECTOR at +0, UV at +0x20, bounds +0x24/+0x28, RotTransPers sxy +0x18 / IR0 +0x14, view Z +0x10.
- Structs: `0x2C` clip record; two `ClipOut` lists `0x28` apart; 10-corner max for facing.
- Control flow: six fixed Sutherland-Hodgman passes (far, near, Y low/high, X low/high); `WholeFacing` (KF2_POLYASM_FACING) decides quad/fan culling whole-loop at fractional corners; verify leaves the game's first-three answer.
- Overlays: `game`.
- Env / settings keys: `KF2_POLYASM_FACING`; inherits `KF2_POLYASM`/`KF2_POLYASM_CLIPPER`.
- RecompOne deps: `Gte.MvmvaOp/Rtps/Gpf/Gpl/Write/Read/ReadControl`, `GteDepth.Subpixel`, `GteVertexMap.Peek`, `Interrupts`.
- Mod-visible: `PolyAssembler.ClipperEnabled`, `WholeFacing`, `ClippedChanged`, `QuadsChanged`.
- Split note: clipper algorithm is PSY-Q-generic but record layout/list addresses are KF2's; facing helpers generalise.

#### patches/PolyAssemblerDepth.cs  (174 lines)
- Bucket: C (high) -- "record each packet's corner depths from the assembler for a depth buffer" generalises; addresses/vertex cache are KF2.
- Does: records per-corner view Z (SZ3, unrounded) and the packet's command/XY for `GtePacketDepth`/`ZBuffer`; handles clipped fans; classifies blended door models as solid.
- Hooks: none; called from the assemblers.
- Data: `VertexCache`, `VertexBase`, `ModelTable`, `ClipRecords`, `ClipOut`, `PrimDescriptor`. Corner Z computed from GTE control 3/4/7 (rotation row + translation); depth unrounded vs whole within 1.5 units.
- Structs: `CacheDepth` (W0, W1, Z) per cache slot (1<<13); `GtePacketDepth.Rec` (Cmd, Xy0, XyLast, Material, Model, Solid, Z0-3); `ModelKind`.
- Control flow: HUD and arm not recorded (`DepthOn` excludes `InHud`/`InArm`) so they keep painter's order; blended model packets by kind; packet at/below camera saturates to depth 1.
- Overlays: `game`.
- Env / settings keys: none direct (gated by `GtePacketDepth.Active` via `ZBuffer`); `KF2_ZBUFFER`/`KF2_GPUWORLD` etc. elsewhere.
- RecompOne deps: `GtePacketDepth` (`Active`, `SetRange`, `Slot`, `Recorded`, `Rec`), `Gte.ReadControl`, `Remaster`; patch `0050`.
- Mod-visible: `PolyAssembler.InArm`, `InModel`, `TileMaterial`, `BlendedByKind`, `SolidPackets`, `DepthClipMismatches`, `DepthUnrounded`, `DepthWhole`.
- Split note: mechanism generic; seam is the vertex cache/records layout and depth-cue formula.

#### patches/PolyAssemblerFog.cs  (368 lines)
- Bucket: C (high) -- "blend fog/light between neighbouring tiles / the records' colour matrix" generalises as a lighting idea; addresses and record strides are KF2.
- Does: `EvenFog`'s blending of fog words and light matrix/back colour between a tile half and its eight neighbours, plus `0085` record-terms version for the vertex shader, and clipped-fan colour/fog rewrite.
- Hooks: none; called by `TileWalk` (`BeginTile`/`EndTile`), retained map (`RetainedTile`...), and the assemblers (`TileColours`, `RewriteClipped`).
- Data: `TileBase=0x801C8484`, `LightRecords=0x801930F0` (record stride `0x68`; fog word +0x66; colour matrix +0x50 nine shorts; back colour +0x62 three bytes; light matrix per rotation `+rot*20`); `FogMode`, `LightColour`, `VertexBase`, `VertexCache`, `PrimDescriptor`, `ClipOut`. Fog curves: 2800 knee, `0xAF0`, `0x320` offset; word>=32000 none.
- Structs: light record `0x68`; tile half `off/10%80`, `off/800`; neighbour index `(dz+1)*3+dx+1`; bilinear weights at ±1024.
- Control flow: `BeginTile` sets `_tileFog`/`_tileLight` only when a neighbour record differs; verify stands `EvenFog` down; refog vs emitter IR0/2.
- Overlays: `game`.
- Env / settings keys: `KF2_EVENFOG_BLEND`, `KF2_EVENLIGHT`; setting for Even fog and lighting (Enhancements); gated on Fast geometry.
- RecompOne deps: `Gte.DepthQuotient`, `Gte.ReadControl`, `Gte.LightProducts`, `RetainedScene.PackLight`, `EvenFog` (`Enabled`, `Blend`, `Light`).
- Mod-visible: `RetainedLight/RetainedFog/RetainedRecords/RetainedLightWord`, `EmptyWord`, `RecordFogWeight`.
- Split note: blending maths generalisable; KF2 seam is the light-record layout and tile grid.

#### patches/PolyAssemblerHud.cs  (117 lines)
- Bucket: C (high) -- "the HUD transform in C#, publishing each screen vertex's sub-pixel fraction to the vertex map" generalises; `func_8002E910` is KF2.
- Does: `func_8002E910` (HUD orthographic `RotTrans`) in C#, writing X/Y as one word and publishing a fraction to `GteVertexMap` only for pieces the matrix turns.
- Hooks (replace): `HudTransform=0x8002E910`.
- Data: `VertexBase`, `VertexCache`; GTE control 0/1/2 (R rows) and 5/6 (TR); cache entry xy, `Z>>2`, Z.
- Structs: vertex cache 8 B; `GteVertexMap.Publish(xy, depth0, fx, fy, projected:false)`.
- Control flow: a scale-only matrix offers no fraction (whole-pixel snap preserved for gauges); `Turned()` = off-diagonal R element.
- Overlays: `game`.
- Env / settings keys: `KF2_POLYASM_TRANSFORM`; `KF2_SUBPIXEL_PROBE` reads counters.
- RecompOne deps: `Gte.MvmvaOp/Read/ReadControl`, `GteVertexMap.Active/Publish`, `Subpixel`; patch `0052`.
- Mod-visible: `PolyAssembler.ScreenVertices`, `ScreenFractional`, `ScreenAligned`.
- Split note: concept generic; address and orthographic layout KF2.

#### patches/PolyAssemblerLight.cs  (369 lines)
- Bucket: C (high) -- "record each packet's lighting inputs for a per-pixel shader, with a CPU reference of the shader" generalises; record layout and light records KF2.
- Does: records per-packet raw depth cue, curve, light dots/colours and BK/LCM generation for `GteLightMap`; supplies `ShadeCorner` as the shader's reference for `KF2_PERPIXEL_PROBE=2`; keeps unfogged/black-fogged faces when authored lights/fog need the record.
- Hooks: none; called from the assemblers and `PolyAssemblerClip`.
- Data: `VertexCache`, `ClipRecords`, `LightColour`, `VertexBase`, `FogMode`, `PrimDescriptor`. Depot caps `CurveNone/Offset(2848)/Knee(3232)/Word(4096)`, `Directional` bit, `Present` bit; MAC0/4096 as raw cue.
- Structs: `CacheLight` (Sxy, Serial, Fog, Curve, Raw) 1<<13; `RecordLight` (Sxy, Ir0, Raw) 64; `GteLightMap.Rec` (F0-3, L0-3 xyz, Gen, Light, Cmd, Xy0, Light word); light-record reuse via `_cacheSerial`.
- Control flow: `Uniform` drops flat/unfogged/black-fogged faces from the buffer; direct tile path vs word fallback; `KeepUnfogged`/`KeepFogged` (remaster); `InHud` excluded.
- Overlays: `game`.
- Env / settings keys: `KF2_PERPIXEL_CHECK`? no -- `PerPixelLighting.Check`; `KF2_REMASTER_LIGHTS`/`KF2_REMASTER_ATMOS` influence keep flags elsewhere.
- RecompOne deps: patch `0048`; `GteLightMap` (`Active`, `Slot`, `NoteConstants`, `Present`, `Directional`, `Bk`, `LcmAt`, `CurveWord/None/Knee/Offset`), `Gte.LightProducts/LightDots/Read/ReadControl`, `Remaster`.
- Mod-visible: `PolyAssembler.KeepUnfogged`, `KeepForLights`, `KeepForGlow`, `Keep`, `KeepFogged`, `InHud`.
- Split note: shader contract generic; KF2 seam is the light records and the GTE formulas.

#### patches/PolyAssemblerLit.cs  (328 lines)
- Bucket: C (high) -- the lit model assembler in C#; general technique, KF2 routines/packets.
- Does: `func_8002F214` and `func_8002EAEC` (forced semi-transparent) in C#: unclipped loop with per-face GTE lighting (`NCDS`/`NCDT`), caller slot bias, blend-rate tpage; captures faces for retained models.
- Hooks (replace): `Lit=0x8002F214`, `LitBlend=0x8002EAEC`.
- Data: `ModelTable`, `VertexCache`, `LightColour`, `PrimDescriptor`, `OtBase`, `FogMode`. Packet GT3 `0x28`/GT4 `0x34`; tpage blend bits 5-6 (`& 0xFF9F | abr`).
- Structs: model header 28 B; flat vs gouraud faces `0x24/0x2C/0x34/0x3C`; `IBlend`/`Opaque`/`Blended` type switch for code sharing.
- Control flow: mean fog weight for flat faces, first-vertex fog for gouraud; `Insert` drops mean depth <=0 then `Place` range test; captures every face before culling for retained scene; blended leaves GPU.
- Overlays: `game`.
- Env / settings keys: `KF2_POLYASM_LIT`; inherits `KF2_POLYASM`.
- RecompOne deps: `Gte.NcdsOp/NcdtOp/Write/Read`, `GteLightMap`, `RetainedModels`, `ModelWalk`.
- Mod-visible: `PolyAssembler.LitEnabled`, `LitCalls`.
- Split note: generic assembler structure; KF2 seam routine addresses and GTE lighting recipes.

#### patches/PolyAssemblerTexRect.cs  (42 lines)
- Bucket: C (medium) -- "a clipped fan's texture rect is its face's" generalises to anisotropic filtering; small KF2-specific.
- Does: after `func_800302E8`, gives every packet of a clipped fan the face's UV bounds (from the face record corners) instead of the surviving UV subset, so filters and mip atlas key on the whole face.
- Hooks: none; called by `PolyAssembler.Clipped`.
- Data: `ClipOut`, `PrimDescriptor`, `GteTexRect`; face UV at `f+4*i` low/high bytes (u,v).
- Structs: `GteTexRect.Slot(pkt)` (Cmd, Xy0, XyLast, Rect = u0|v0<<8|u1<<16|v1<<24).
- Control flow: runs only when `GteTexRect.Active`; packet k records at pkt+4/8/0x20.
- Overlays: `game`.
- Env / settings keys: none; gated by anisotropic filtering.
- RecompOne deps: `GteTexRect` (`Active`, `Slot`, `SetRange`, `Recorded`).
- Mod-visible: none public.
- Split note: geometry generic; seam is face record layout and VRAM UV packing.

#### patches/PositionalAudio.cs  (343 lines)
- Bucket: C (high) -- "re-aim a 3D sound effect every frame from the listener, render binaural/spatial" generalises; sound routine, listener and SPU calls are KF2/PSY-Q.
- Does: pre/post `func_80013D08` (game's 3D sound) keeps the source; pre/post `SsUtKeyOn` tags the voice's next key-on; post stage 9 `func_800140AC` re-aims every live tag from this frame's listener with the game's distance law, for speakers or headphones.
- Hooks: pre/post `game` `Sound3D=0x80013D08`, `SsUtKeyOn=0x8005520C`, post `ListenerStage=0x800140AC`; `HookAttach.OnOverlayLoad`, `AddPre/AddPost`, `Commit`.
- Data: `ListenerPos=0x80198584` (xyz), `ListenerYaw=0x80198598`, `HalfNow=0x801E9C8E`, `ListenerHalf=0x80198594` (stacked-map half), `SsUtKeyOn` V0 = voice. Pan scale 4096/3400, centre sin(pi/4)*scale; `func_80015394` atan2 in 4096ths/turn.
- Structs: `Tag[24]` (Live, Serial, X/Y/Z, Vol, Fade, Halve, Sum); SPU key-on serials `_before`/`_serials`; `Mode` enum.
- Control flow: non-nesting call guard `_inSound`/`_inKeyOn`; deferred key-on (written at next sequencer flush) handled by serial+1; a voice re-keyed by anything else loses its tag (music untouched); level scaled by mixer registers.
- Overlays: `game` (`func_80013D08`, `SsUtKeyOn`, `func_800140AC`).
- Env / settings keys: `KF2_POSAUDIO` (off/speakers/headphones), `KF2_POSAUDIO_PROBE`; setting `kf2.audio.positional`; `RuntimeReadyEvent`.
- RecompOne deps: runtime `Spu` (`CopyKeyOnSerials`, `SetSpatial`, `Stats.SpatialVoicesPeak`), `HookManager`, `SymbolRegistry`; patch `0044`.
- Mod-visible: `PositionalAudio.Mode`, `ModeKey`, `Current`, `Probe`, `Configure/Install/Set`, `BeforeSound/AfterSound/BeforeKeyOn/AfterKeyOn/AfterListener`.
- Split note: binaural maths and voice-tagging generalise; KF2 seam is the sound routine, listener addresses and stage 9.

#### patches/Prejit.cs  (274 lines)
- Bucket: B (high) -- compiles the assembly's methods ahead of time; the only game coupling is overlay-name ranking (`fdat*`, `game`, `main`).
- Does: on a background lowest-priority thread at first overlay load, `RuntimeHelpers.PrepareMethod` every recompiled function, every port method and the runtime, area modules first, skipping hooked methods.
- Hooks: none; enumerates `Dispatcher.Overlays`, `HookManager.IsCommitted`.
- Data: none.
- Structs: none.
- Control flow: starts at first `OverlayLoadedEvent` (dispatcher populated), installed last in Program.cs; yields every 32 methods; order `fdat*` < `game` < `main` < others, then patches, runtime.
- Overlays: all (enumerated; `fdat*`/`game`/`main`/`open`/`end` ranked).
- Env / settings keys: `KF2_PREJIT`, `KF2_PREJIT_PROBE`; no setting.
- RecompOne deps: `Dispatcher.Overlays`, `IOverlay.Functions`, `HookManager.IsCommitted`, `OverlayLoadedEvent`.
- Mod-visible: `Prejit.Enabled`, `Prepared`, `Done`, `Configure/Install`.
- Split note: pure infra; the `Rank` overlay names are the trivial game-specific strings.

#### patches/PrimBuffer.cs  (270 lines)
- Bucket: C (high) -- "move the primitive buffers above the console's 2 MB and enlarge them" generalises; the layout routine, descriptor addresses and stock sizes are KF2.
- Does: relocates the two per-frame primitive buffers to `0x80200000` at 4x (or KF2_PRIMBUF) and lays out the mirror arena/OT/scratch, wave copy and prop scratch after them; probes peak usage and overflow; auto-sizes guest RAM.
- Hooks: post `game` `Init=0x8002DF80`, pre `game` `FrameHead=0x8002E064` (both through the same `Relocate`), post `Renderer=0x800342D8` (probe); `HookAttach.OnOverlayLoad`, `AddPre/AddPost`, `Commit`, `HookAttach.Installed`.
- Data: `ActiveDescriptor=0x8017E0A4`, `Descriptor0=0x8017E08C`, `Descriptor1=0x8017E098`, `StockBase=0x800FC99C`, `StockBytes=0x19000`, `Base=0x80200000`; `PacketBytes=0x34`; `OtBytes=0x2000*4`, `ScratchBytes=0x8000`, `WaveBytes=0x10000`, `PropBytes=0x2000`. Descriptor `{start,end,cur}` 12 B.
- Structs: buffer descriptor 12 B; two stock buffers back-to-back into `0x8012E99C`; shrunk menu layout `0x6400` with frozen frame at `start+0xC800`.
- Control flow: writes full layout only when the game's full layout is found; leaves a shrunk menu layout alone (menus hold a frozen frame in the tail); `PublishRange` re-points depth/light/texrect record ranges when relocated.
- Overlays: `game`.
- Env / settings keys: `KF2_PRIMBUF` (scale, default 4), `KF2_PRIMBUF_PROBE`, `KF2_RAMSIZE` (MB), `KF2_RAM_PROBE`; via `Runtime.View`? no. `RamProbe`.
- RecompOne deps: patch `0056`; `MemoryMap.RetailRamSize`, `GtePacketDepth.SetRange`, `GteLightMap.SetRange`, `GteTexRect.SetRange`, `Runtime.RamSize`, `RamProbe`, `HookManager`, `SymbolRegistry`.
- Mod-visible: `PrimBuffer.Scale` (private), `RamSize`, `Relocated`, `MirrorArena/ArenaBytes/Ot/Scratch`, `WaveBytes/WaveScratch`, `PropBytes/PropScratch`, `PublishRange`, `TrySecondBuffer`, `Configure/Install/Relocate/AfterFrame`.
- Split note: relocation idea generic; KF2 seam is `func_8002DF80`/`func_8002E064`, the descriptors and stock sizes.

#### patches/ProfilerPanel.cs  (830 lines)
- Bucket: B (high) -- an ImGui frame-profiler panel over the runtime's `Profiler`/`GpuTimes`/`FrameProfiler`; the only KF2 coupling is the `ProfileGroup`/section names the runtime defines and the optional `FramePacing` target line.
- Does: Shift+P window: stacked self-time bars per frame by group, a GPU-time strip by pass, spike list, sortable/filterable section table (self/max/incl/calls/share, CPU+GPU), CSV export, probe-adding buttons.
- Hooks: none.
- Data: none game.
- Structs: runtime `Profiler.Frame`/`Span`, `GpuFrames`, `GpuTimes.Passes`, `ProfileGroup` (game/hook/runtime/swap/wait/gpu), `IPanel`.
- Control flow: aggregates over a window or one selected frame; reads `FramePacing.Enabled`/`TargetFps` only for the target line.
- Overlays: none.
- Env / settings keys: `KF2_PROFILE=panel` (OpenPanel via runtime); no setting.
- RecompOne deps: runtime `RecompOne.Runtime.Diagnostics.Profiler`, `FrameProfiler`, `GpuFrames`, `GpuTimes`, `ProfileGroup`, `RecompOne.Runtime.Host.Window.IPanel`; patch `0045`.
- Mod-visible: `ProfilerPanel.Instance`, `IPanel` members.
- Split note: pure B; the profile section/group taxonomy is the runtime's, so it travels with the runtime.

#### patches/RateCensus.cs  (205 lines)
- Bucket: B (high) -- a rate instrument that snapshots a RAM range on the vblank grid and ranks changed words; the default range is the KF2 data region, the only game-specific part.
- Does: samples words between two addresses at each `VSyncEvent` (60 Hz wall-clock grid) and periodically dumps `addr changes` so a 20 fps and a 144 fps run can be compared to separate tick-clocked from render-clocked words.
- Hooks: none; `Event.AddListener<VSyncEvent>`.
- Data: default range `0x80060000`-`0x801C0000` (KF2 data region); overridable. No struct interpretation.
- Structs: none.
- Control flow: first sample seeds, not counted; dump rewritten in full each period; process-exit dump only for clean exit.
- Overlays: none (range is raw addresses).
- Env / settings keys: `KF2_RATECENSUS`, `KF2_RATECENSUS_RANGE` (hex lo:hi), `KF2_RATECENSUS_OUT`, `KF2_RATECENSUS_PERIOD`; no setting.
- RecompOne deps: `VSyncEvent`, `Runtime.Mem`; companion `scripts/find_writers.py`.
- Mod-visible: `RateCensus.Enabled`, `Configure/Install`.
- Split note: instrument generic; seam is the default address range (trivially reparameterised).

#### patches/ReflectionReach.cs  (276 lines)
- Bucket: C (high) -- "grow and time-hold the set of halves a reflection may show" generalises; map base, tile grid and query addresses are KF2.
- Does: each frame grows the drawn halves by `Reach` cells on their level, holds them `Hold` seconds with a fade, publishes weights to `RetainedScene.CurrentHalves`, and answers the object walk's visibility queries for the grown set.
- Hooks: none; called from `TileWalk` (`NoteDrawn`, `Build`), `ModelWalk` queries, and the retained scene.
- Data: `MapBase=0x801C8484`; halves 80*80*2; map half byte `< 240` = drawable; world>>11 = tile; `RenderDistance` cone (`ViewCone`, slope, apex, reach). Queries at `pos+0` x / `pos+8` z.
- Structs: half index `(tz*80+tx)*2 + upper`; per-half stamps/weights/dist arrays; `_tileBits` per tile.
- Control flow: `Wanted` requires `Reach>0 && RetainedMap.ReflectionsReady`; planar walk no longer reads it; `OverlayLoadedEvent` forgets; BFS growth bounded by `RenderDistance.Reach`.
- Overlays: `game` (map globals).
- Env / settings keys: `KF2_REFLECT_REACH`, `KF2_REFLECT_REACH_PROBE`; setting `kf2.reflectreach` (no longer read -- env only); `RuntimeReadyEvent`, `OverlayLoadedEvent`.
- RecompOne deps: `RetainedMap.ReflectionsReady`, `RetainedScene.CurrentHalves`, `RenderDistance.ViewCone`, `CullCone.Factor`.
- Mod-visible: `ReflectionReach.Key`, `Max`, `DefaultReach`, `Hold`, `FadeSeconds`, `Reach`, `Any`, `Configure/Install/Set`, `NoteDrawn/Build/Point/Box`.
- Split note: set-growth/hold idea generic; seam is the KF2 map base/tile grid and the retained-scene gate.

### Summary

Bucket counts (14 entries; the `PolyAssembler*.cs` treated as one feature with a
per-file line, but counted here per file for the mechanism assessment):

- **A**: 0
- **B**: 4 -- `Perspective` (B/A), `Pgxp` (near-pure B), `Prejit`, `ProfilerPanel`, `RateCensus` (5 files, one rated B/A)
- **C**: 14 of the 20 files (all renderer/audio/reflection/geometry work + `PacketMatch`, `PlanarCull`, `ReflectionReach`, `PrimBuffer`, `PositionalAudio`)
- **D**: 0 outright KF2-only

Every file in this batch is either generic infrastructure (B) or a generic
mechanism implemented against KF2 addresses (C). Nothing was KF2-only by idea.

Most important observations:

1. **The rendering stack is one big C seam centred on the GTE/packet contract.**
   `PolyAssembler*`, `PerPixelLighting`, `Perspective`, `Pgxp`, `PlanarWalk`,
   `PlanarCull`, `ReflectionReach`, `PrimBuffer` all read or write the same KF2
   structures: the vertex cache at `0x8018EB94` (8 B/vertex), the packet/arena
   descriptor at `0x8017E0A4`, the OT at `0x8018E0A8`, the model table at
   `0x8018E19C`, the light records at `0x801930F0` (stride `0x68`) and the map at
   `0x801C8484`. A sibling game shares the *mechanism* but must re-derive these
   constants; a shared runtime layer could parameterise them.
2. **`Perspective` and `Pgxp` are effectively runtime patches wearing a KF2
   filename.** Their mechanism lives entirely in `tools/RecompOne`; the file is a
   switch + probe with three `DrawOTag` addresses. These are the strongest B
   candidates for moving into the fork.
3. **`Prejit`, `ProfilerPanel`, `RateCensus` are pure B** with only trivial game
   strings (overlay name ranking; a default RAM range). They would drop into a
   sibling repo nearly verbatim.
4. **`PolyAssembler`'s harness is the reusable half.** The `HookManager`
   replace + `Verify` + `Frame` (direct-RAM fast path with interrupt-epoch
   refresh) + `Saved` register restore discipline is game-agnostic; only the
   routine bodies and packet layouts are KF2. If shared, the seam is per-routine.
5. **`PositionalAudio` is C but its binaural mixing is B.** The head model,
   distance law and voice-tagging are generic; only `func_80013D08`,
   `SsUtKeyOn`, the listener at `0x80198584` and stage 9 are KF2.
6. **`PacketMatch` is a probe, not a feature** -- it measures whether a
   packet-level smoother is viable (pose recovery), and its conclusion depends on
   KF2 assembling packets with whole-word `lw`/`sw`. The OT decode and three-key
   scheme generalise; the finding may not.
7. **`PlanarWalk`/`PlanarCull`/`ReflectionReach` are a coupled trio** sharing the
   tile-grid addresses and the `ScenePass`/`PrimBuffer` arena machinery; they
   should probably move as a unit or not at all.
8. **`PrimBuffer` is a prerequisite for `PlanarWalk`** (the mirror arena lives
   past the relocated buffers) and its `RamSize`/`PublishRange` surface is read by
   the depth/light/texrect records; a shared version must keep that contract.
9. **`PolyAssemblerDepth`/`Light`/`TexRect` are record-producers for runtime
   consumers** (`GtePacketDepth`, `GteLightMap`, `GteTexRect`): the record structs
   are the interface, the KF2 part is only how they are filled from the packet.
   This is the cleanest extraction boundary in the batch.
10. **Env surface is large and stable**: each C file exposes `KF2_<feature>` and
    `KF2_<feature>_PROBE`, reading a `kf2.<feature>.on`-style setting, with the
    env winning over the saved key. That convention is itself B and worth keeping
    consistent across sibling repos.

## patches/, Reflections to ZBuffer
#### patches/Reflections.cs  (243 lines)
- Bucket: C (high) -- screen-space-reflection *mechanism* is generic, but which VRAM rects are water and the stub slots are KF2 RAM.
- Does: switch/config and the water material for the runtime's SSR pass; publishes water dest rects as surface materials.
- Hooks: pre on `DrawOTag` at `0x80060818` (overlay `game`), via `SymbolRegistry.Resolve("game", null, DrawOTag)` / `HookManager.AddPre`. Job: before the OT walk, read current fluid slot rects.
- Data: fluid slots base `0x80192D58` (8 slots x stride `0x18`): byte +0 liveness==1, rect X +6, Y +8, W +0xA, H +0xC (s16). Writes `SurfaceMaterial.RectN`/`Rects`.
- Structs: 8 fluid slot records at `0x18` stride; liveness sentinel `u8[+0]==1`; w/h signed 16-bit.
- Control flow: pass runs for any of SSR/Murk/PlanarWalk/RetainedMap; rects refreshed every frame before the OT walk regardless of smoothing.
- Overlays: `game` (DrawOTag).
- Env / settings keys: `KF2_SSR`, `KF2_SSR_STRENGTH`, `KF2_SSR_F0`, `KF2_SSR_DISTANCE`, `KF2_SSR_STEPS`, `KF2_SSR_THICKNESS`, `KF2_SSR_SKY`, `KF2_SSR_RESOLUTION`, `KF2_SSR_FOGCURVE`, `KF2_SSR_PROBE`; setting `kf2.ssr.on`; `RuntimeReadyEvent`, `OverlayLoadedEvent`.
- RecompOne deps: patch `0067`; runtime types `ScreenReflections`, `PlanarReflections`, `RetainedScene`, `SurfaceMaterial`, `GteDepth`, `GtePacketDepth.Rec.Material`, `AoGeometry`, `HookAttach`, `SymbolRegistry`, `HookManager`, `Interrupts`(no).
- Mod-visible: `Reflections.OnKey`, `Reflections.Enabled`, `AnySource`, `StrengthForced`, `Probing`, `Configure`, `Install`, `SetEnabled`; `ModInfo _self`.
- Split note: mechanism in `ScreenReflections`/runtime `0067`; KF2 seam is `Slots=0x80192D58` and the DrawOTag address. A sibling game needs only new slot base/stride.

#### patches/RenderDistance.cs  (258 lines)
- Bucket: C (high) -- "draw the map past the game's visibility window" generalizes, but everything reads KF2 grid/map RAM and calls KF2 funcs.
- Does: adds cells outside the game's 24x24 flood cone out to a slider distance, walks them through the far assembler, answers model visibility queries.
- Hooks: none directly; called from `TileWalk`/`ModelWalk`/`CullGrid` and `KingsField2.func_80031B1C` direct call (`c.RA=0x80031D14`). Direct-call alias `Recompiled.KingsField2_game`.
- Data: `Grid=0x80192EAC`, `GridOriginX=0x80192EA0`, `GridOriginZ=0x80192EA4`, `CamWorldX=0x80192E78`, `CamWorldZ=0x80192E80`, `MapBase=0x801C8484`. Conventions: world position >>11 = tile index; map half byte at `MapBase + tz*800 + tx*10 + halfOff` (halfOff 0 or 5, `0xFF` empty); grid byte bit0=lower, bit1=upper level; camera world /2048 = tiles.
- Structs: 24x24 grid bytes (`Grid`), map cells 10 bytes stride at `MapBase` with 800-byte row (80x80 tiles), per-level half offset 0/5.
- Control flow: `Build` from start of frame's tile walk after game built grid; `Walk` after game's cells; view cone from `CullCone.StockCorners`.
- Overlays: `game` (module globals), direct call into `fdat`? No, `func_80031B1C` is game.
- Env / settings keys: `KF2_RENDERDIST`, `KF2_RENDERDIST_PROBE` (1/2); setting `kf2.renderdistance`; `RuntimeReadyEvent`.
- RecompOne deps: runtime types `CullCone`, `Interrupts`, `Runtime.View`, `Program` hooks in `TileWalk`/`ModelWalk`. Patch refs: `0056`? no.
- Mod-visible: `Key`, `Stock`, `Reach`, `Max`, `Slope`, `Apex`, `Tiles`, `On`, `Any`, `Configure`, `Install`, `Set`, `Build`, `Walk`, `Point`, `Box`, `Clamped`, `Report`, `ViewCone`.
- Split note: generic "extend the visibility flood" vs KF2 addresses/among constants (`Stock=10.5`, slope from the game's table).

#### patches/RetainedPlanes.cs  (185 lines)
- Bucket: C (high) -- ranking level reflective faces from a retained scene is a general idea; water detection is KF2 fluid-slot/texture specific.
- Does: finds level faces that are water or authored-reflective in the retained map, ranks them by screen area from the frame camera, picks up to MaxPlanes.
- Hooks: none; called by `RetainedMap`/`RetainedScene` build and `Choose` each frame.
- Data: `SurfaceMaterial.RectN`/`Rects` (fluid VRAM rects), authored material reflectivity table `SurfaceMaterial.Reflectivity`.
- Structs: `Face` (4 xz corners + Y, Corners, Semi, Mat, TPage, Rect, Key=Y/16), `Group` keyed `(key, chunk, water? -1 : mat)`; chunk = (minX/16384)*16 + minZ/16384.
- Control flow: `Note` during retained-map build; `Finish` groups; `Choose` per frame from `RetainedScene.Find(Serial).View`; Y is down, only camera above plane.
- Overlays: none.
- Env / settings keys: none direct.
- RecompOne deps: `RetainedScene` (`Enabled`, `Planar`, `MaxPlanes`, `View`, `Find`, `SetPlanes`), `SurfaceMaterial`, `IMemory`.
- Mod-visible: none public (internal `static class`).
- Split note: generic plane ranking; KF2 seam is `Water()` TPage/Rect VRAM math and fluid slots, and `RetainedScene.Serial` (area serial).

#### patches/ScenePass.cs  (138 lines)
- Bucket: C (high) -- "run a game draw pass into the port's own OT/arena and restore state" is generic; the exact saved state set is KF2 addresses.
- Does: opens a drawing pass against the port's ordering table/primitive arena, saving all state the game's draws may move, and restores it on End.
- Hooks: none; used by `MenuWorld`, `PlanarWalk`, `Stage13`, editor camera, shadows.
- Data: `ActiveDescriptor=0x8017E0A4`, `OtPointer=0x8018E0A8`, `ModelTable=0x8018E19C`, `VertexBase=0x8018EAA0`, `FogWord=0x80192EA8`, `CameraBlock.Start`/`Bytes`.
- Structs: primitive descriptor 3 words (`0xC`), OT of `0x2000` entries (`OtBytes`); `CameraBlock.Bytes` blob.
- Control flow: Begin/End bracket; one instance per caller, not reentrant; LinkBefore chains pass table in front of game table; ClearTable is `ClearOTagR`.
- Overlays: `game` (globals).
- Env / settings keys: none.
- RecompOne deps: `Gte.State`, `Gte.Save/Load`, `CpuSnapshot`, `PSMemory`, `CameraBlock`; patch `0070`? no (Stage13 orders).
- Mod-visible: `OtEntries`, `OtBytes`, `DescriptorBytes`, `ActiveDescriptor`, `OtPointer`, `ModelTable`, `VertexBase`, `FogWord`, `Descriptor`, `Table`, `Arena`, `ArenaEnd`, `Head`, `Used`, `Overflowed`, `Begin`, `End`, `LinkBefore`, `ClearTable`.
- Split note: the generic part is the save/restore discipline; the constants are all KF2 game globals. A sibling game reparameterizes the constants.

#### patches/TrueColor.cs  (72 lines)
- Bucket: B (high) -- a picture switch; the precision decision lives in the runtime, no game knowledge.
- Does: the switch for 24-bit display output (Smooth entry of the Shading combo); sets `GteDepth.TrueColor`.
- Hooks: none.
- Data: none.
- Structs: none.
- Control flow: `Enabled` read at `RuntimeReadyEvent` (saved setting); `KF2_TRUECOLOR` forces for the run.
- Overlays: none.
- Env / settings keys: `KF2_TRUECOLOR`; setting key `kf2.truecolor.on`; `Settings.ShadingPage` (KF2 settings UI); `RuntimeReadyEvent`.
- RecompOne deps: patch `0021`; runtime types `GteDepth`, `Runtime.View`.
- Mod-visible: `TrueColor.OnKey`, `Enabled`, `Configure`, `Install`, `SetEnabled`.
- Split note: mechanism wholly in the runtime; nothing to split.

#### patches/UiScale.cs  (80 lines)
- Bucket: B (high) -- generic "recover from an unusable interface scale"; only the `KF2_` prefix and namespace are game-flavoured.
- Does: `KF2_UISCALE` forces/saves the ImGui interface scale so an oversized scale can be recovered.
- Hooks: none.
- Data: none.
- Structs: none.
- Control flow: applies on `RuntimeReadyEvent` (must be after ConfigManager load, before the window loop); writes to `interface.ini` via `Runtime.SaveView()`.
- Overlays: none.
- Env / settings keys: `KF2_UISCALE`; runtime `ViewConfig.UiScale` (0.5-3), `interface.ini`; `RuntimeReadyEvent`.
- RecompOne deps: patch `0019` (popup clamp); runtime types `Runtime.View`, `Theme`, `ImGuiNET`.
- Mod-visible: `UiScale.Configure`, `Install`.
- Split note: fully generic; no seam.

#### patches/Subpixel.cs  (206 lines)
- Bucket: C (medium) -- generic sub-pixel mechanism (runtime `0010`/`0012`); switch generic, but the probe hooks the SDK `DrawOTag` by per-overlay address.
- Does: the switch for sub-pixel vertex positioning, plus a probe that counts recovered fractions; the work is in the runtime.
- Hooks: post `DrawOTag` at `open 0x80016078`, `game 0x80060818`, `end 0x80013D80` (only under `KF2_SUBPIXEL_PROBE`); via `SymbolRegistry.Resolve`/`HookManager.AddPost`.
- Data: none.
- Structs: none.
- Control flow: frame boundary for the probe is `DrawOTag`; setting read at `RuntimeReadyEvent`.
- Overlays: `open`, `game`, `end` (SDK libgpu addresses only).
- Env / settings keys: `KF2_SUBPIXEL`, `KF2_SUBPIXEL_PROBE`, `KF2_SUBPIXEL_CULL`; setting `kf2.subpixel.on`; `RuntimeReadyEvent`, `OverlayLoadedEvent`.
- RecompOne deps: patches `0010`, `0012`, `0052`; runtime types `GteDepth`, `GteVertexMap`, `PolyAssembler` (`CullKept`/`CullDropped`/`ScreenVertices`/...), `HookAttach`, `SymbolRegistry`, `HookManager`.
- Mod-visible: `Subpixel.OnKey`, `Enabled`, `Cull`, `Configure`, `Install`, `SetEnabled`; `ModInfo _self`.
- Split note: switch/mechanism generic; KF2 seam is only the DrawOTag address table and the probe's counters, both re-mappable.

#### patches/TexProbe.cs  (161 lines)
- Bucket: B (high) -- generic renderer/VRAM diagnostic; no game addresses or data.
- Does: census of textured/flat/raw prims and per-page distinct VRAM words, once a second, to console and `texprobe.log`.
- Hooks: none; listens to `RenderPrimEvent` and `VSyncEvent`.
- Data: none (reads backend VRAM via `be.ReadVram`).
- Structs: 32 texture pages (16x2) of 64x256 texels; `_vram` 1024x512 ushorts.
- Control flow: once a second from `VSyncEvent`, on the backend's thread.
- Overlays: none.
- Env / settings keys: `KF2_TEXPROBE`; writes `texprobe.log`.
- RecompOne deps: runtime types `RenderPrimEvent`, `VSyncEvent`, `GpuHle`, `GpuHle.Backend.ReadVram`, `GpuGlAccess`, `GpuBackendFactory`, `GlVram.Scale`, `GteDepth`.
- Mod-visible: `TexProbe.On`.
- Split note: generic; only the `[KF2-TEXPROBE]` tag and log filename are game-flavoured.

#### patches/SpriteAnim.cs  (356 lines)
- Bucket: C (medium) -- "hold a frame counter inside a drawing function to run animation at the tick" generalizes; the whole implementation is KF2's billboard table.
- Does: hold/restore pair around the world+object walk so billboard sprite cels advance once per world tick, not per rendered frame.
- Hooks: pre+post `func_800331B4` at `0x800331B4` (overlay `game`), `HookManager.AddPre`/`AddPost`; job: stage 13's world/object walk (draws billboards, last act `++counter`).
- Data: counter `0x80195170` (u32, render-frame count; also zeroed by `func_8002DF80`); table `0x80195174` (u16 id `+0x0`, 0xFFFF free; `+0x2` vis mask; `+0x3` frame count; `+0x4` interval; `+0x5` current cel; `+0x8` VECTOR position).
- Structs: 128 billboard records of `0x18`; liveness sentinel `u16[+0]==0xFFFF`.
- Control flow: `func_800331B4` called once, from stage 13; one walk per frame boundary; keyed on `FramePacing.Frames`+`TickedThisFrame`; `MenuWorld.Hold` freezes; 500 ms watchdog falls back to a wall-clock grid at `FramePacing.LogicHz`.
- Overlays: `game`.
- Env / settings keys: `KF2_SPRITEANIM`, `KF2_SPRITEANIM_PROBE`; no settings page, no saved key; `RuntimeReadyEvent`/`OverlayLoadedEvent` via `HookAttach`.
- RecompOne deps: patch `0027` (per-function detour failure); runtime types `FramePacing` (`Frames`, `TickedThisFrame`, `LogicHz`), `HookAttach`, `SymbolRegistry`, `HookManager`, `PSMemory`.
- Mod-visible: `SpriteAnim.Enabled`, `Hold`, `Configure`, `Install`, `SetEnabled`, `Before`, `After`; `ModInfo _self`.
- Split note: generic seam is the hold/restore + tick identity + watchdog; KF2 seam is the counter/table addresses and the `0x18` record layout.

#### patches/TintHold.cs  (222 lines)
- Bucket: C (medium) -- "run a per-frame reset only on the tick" generalizes; the routine, all addresses and the record layout are KF2's.
- Does: stage 1 `func_8002C944` in C#; holds the light-record copy and tint/wash reset to the tick so the death fade and damage flash stop strobing.
- Hooks: replace `func_8002C944` at `0x8002C944` (overlay `game`), `HookManager.AddReplace`; job: stage 1 per-frame copy of 80 light records and tint-block reset.
- Data: src `0x800679A0` (80 x `0x2C`), dst `0x801930F0` (80 x `0x68`; copy src `+0x00..0x14` -> dst `+0x00`, src `+0x14..0x2C` -> dst `+0x3C`); load flag `0x801930EC`; tint mode `0x80192D45`, wash `0x80192D49` (`u8` + three `u16` at +1/+3/+5); main-loop return site `0x80013920`.
- Structs: 80 records, stride src `0x2C` / dst `0x68`, split copy at `+0x14`.
- Control flow: only the main loop's call held (compare `c.RA` to `0x80013920`); reset gated on `FramePacing.StagesWillRun`; other 8 callers always reset; load flag cleared every frame (stage 10 reads it).
- Overlays: `game`.
- Env / settings keys: `KF2_TINTHOLD` (0/off/verify/on); no saved key.
- RecompOne deps: runtime types `FramePacing.StagesWillRun`, `Pgxp.CpuTracking` (stand down under PGXP), `PSMemory`, `Interrupts.Poll`, `CpuContext.Snapshot/Restore`, `HookAttach`, `SymbolRegistry`, `HookManager`.
- Mod-visible: `TintHold.Enabled`, `Configure`, `Install`; `ModInfo _self`.
- Split note: generic idea = hold a reset to a gated tick; KF2 seam = addresses, record layout, and the assumption stages 2/3/10 exist.

#### patches/ViewClip.cs  (243 lines)
- Bucket: C (high) -- "open a guard-band view-space clipper to the widescreen aspect" generalizes; the four words and the setup routine are KF2 addresses.
- Does: raises the horizontal tangent of the game's view-space clip volume so a wide picture is not cut with a straight edge.
- Hooks: post `func_8005D7CC` at `0x8005D7CC` (overlay `game`), `HookManager.AddPost`; job: computes the clip volume's four words at init. `Apply` also called by `Widescreen` when the aspect moves.
- Data: `TanX=0x800FC97C`, `RecipX=0x800FC984` written; vertical pair `0x800FC98C`/`0x800FC994` left alone. Stock tan 6553 / recip 2560 (`160<<12/100`, `100<<12/160`); GTE projection `H=200` (`SetGeomScreen(0xC8)`); `ScreenWidth=320`; `Slack=24`; `MaxSpan=1000`.
- Structs: none (four words).
- Control flow: post-hook at init is the only moment the words are the stock values; refuses if they differ (`_refused`); no-op below ~2.67:1; `Apply` follows aspect changes.
- Overlays: `game`.
- Env / settings keys: `KF2_VIEWCLIP`, `KF2_VIEWCLIP_PROBE`; settings key `kf2.widescreen.widenclip`; `RuntimeReadyEvent`, `OverlayLoadedEvent`.
- RecompOne deps: runtime types `Widescreen.On`, `Display.WideMargin`, `GpuRaster` span drop (documented only), `HookAttach`, `SymbolRegistry`, `HookManager`.
- Mod-visible: `ViewClip.Key`, `Enabled`, `Tan`, `Configure`, `Install`, `SetEnabled`, `AfterSetup`, `Apply`; `ModInfo _self`.
- Split note: generic mechanism = guard-band clipper widened by aspect; KF2 seam = addresses, `H=200`, 320 screen, stock tangent constants.

#### patches/Waves.cs  (223 lines)
- Bucket: C (high) -- a world-clock water-wave field generalizes; the water slots and "what is water" are KF2.
- Does: master switch/clock/settings for water waves: a swell on the water's vertices (`WaterSwell`) and per-pixel ripples (`RecompOne.Runtime.WaterWaves`, 0078).
- Hooks: none; `AtWalk` is called from `TileWalk`'s sweep start.
- Data: fluid slots `0x80192D58`, 8 x `0x18` (liveness `u8[+0]==1`; rect X `+6`, Y `+8`, W `+0xA`, H `+0xC` s16). Clock from `FramePacing.FirstWalkOfTick`/`LogicPhase`/`LogicHz`. Tile = 2048 world units, height step 128.
- Structs: 8 fluid slot rects (same as `Reflections`); `WaterWaves.Rects` not more than `MaxRects`.
- Control flow: once per frame's tile walk (`AtWalk`); clock advances with ticks + phase; resets on overlay load (`WaterSwell.Forget`).
- Overlays: none directly (driven from `TileWalk`, `game`).
- Env / settings keys: `KF2_WAVES`, `KF2_WAVES_PROBE`; settings keys `kf2.waves.on/swell/swellsize/ripple/ripplesize/shade/speed`; defaults swell 338/6114, ripple 139/700, shade 0.51, speed 1; `waves` shell verb.
- RecompOne deps: patch `0078`; runtime types `WaterWaves` (`Rects`, `RectN`, `R`, `Cam*`, `Tx/Y/Z`, `Time`, `Distort`, `Scale`, `Shade`, `Generation`, `Batches`, `Enabled`, `Supported`, `MaxRects`), `PlanarReflections.RestHeight/Tolerance`, `RetainedMap.ReadView`, `FramePacing`, `PSMemory`.
- Mod-visible: `Waves.OnKey`/`SwellKey`/`SwellSizeKey`/`RippleKey`/`RippleSizeKey`/`ShadeKey`/`SpeedKey`, `Default*`, `Enabled`, `Swell`, `SwellSize`, `Speed`, `Time`, `Configure`, `Install`, `SetEnabled`, `Changed`, `AtWalk`, `InRect`, `Shell`.
- Split note: mechanism = wave field on a world clock + vertex swell; KF2 seam = fluid slot base and `InRect`'s water-rect test.

#### patches/Widescreen.cs  (627 lines)
- Bucket: C (high) -- "present at a wider aspect with margin, anchor HUD, stretch full-screen tints" generalizes; the HUD geometry and 320 width are KF2's.
- Does: sets the runtime's wide aspect/margin, replaces `DrawOTag` to number entries for the probe's listing, anchors the HUD by moving the HUD records' X around the builder `func_80031D5C`, stretches full-screen tints across the margin, and drives `CullCone`/`ViewClip`.
- Hooks: replace `DrawOTag` at `open 0x80016078`, `game 0x80060818`, `end 0x80013D80` (the single owner of the `Replace`; dither/perspective/subpixel are pre/post). Job: libgpu OT walk with entries numbered.
- Data: display width `320`; HUD clusters x 5..91 and x 269..310, y 11..60, last `HudTailEntries=128` OT entries; tint request block `0x80192D45` (comment; read by `func_8003214C`, drawn by `func_80031EE8(0,0,320,240)`).
- Structs: OT linked list (24-bit `next` pointers), `RenderPrimEvent` vertex arrays; HUD boxes clip-relative.
- Control flow: two-pass OT walk (`LibGpu.WalkOTag`); `RenderPrimEvent` listener gated on margin non-zero; margin latch cleared on `open`/`game`/`end` only, not fdat; `Apply` calls `CullCone.Apply` + `ViewClip.Apply`.
- Overlays: `open`, `game`, `end`.
- Env / settings keys: `KF2_WIDESCREEN`, `KF2_WIDESCREEN_PROBE` (1/2), `KF2_WIDESCREEN_EFFECTS`, `KF2_WIDESCREEN_HUD`; key `kf2.widescreen.aspect`; `aspect` shell verb; `Kf2.Settings.WidescreenPage`.
- RecompOne deps: runtime types `Display.WideAspect`/`WideMargin`, `GpuHle.PortWidenedPrim`, `GpuHle.Backend as GlCore.ClearMarginLatches`, `LibGpu.WalkOTag`, `RenderPrimEvent`, `HookAttach`, `SymbolRegistry`, `HookManager`; patches `0019`? no; coordinates with `ViewClip`, `CullCone`.
- Mod-visible: `Widescreen.AspectKey`, `FourThree`, `SixteenNine`, `Widest`, `Presets`, `Aspect`, `AnchorHud`, `StretchEffects`, `On`, `Margin`, `Configure`, `Install`, `SetAspect`, `Shell`, `DrawOTag`; `ModInfo _self`.
- Split note: generic mechanism = render-target margin + OT-walk replacement + full-screen-quad widening; KF2 seam = 320 width, HUD clusters/tail, DrawOTag address table, tint request block.

#### patches/WaterSwell.cs  (391 lines)
- Bucket: C (medium) -- "displace water vertices into a scratch bank before the game's own readers see them" generalizes; the map/bank/model layout is entirely KF2's.
- Does: builds each area's water mesh/vertex set, then points a half's mesh at a moved copy (swell) in `PrimBuffer.WaveScratch` for the length of the half so transforms, clipper, subdivider, depth and reflections all draw the moved surface.
- Hooks: none; `Enter`/`Leave` are called from `TileWalk` around `func_8002E1F0` setting the vertex base.
- Data: `MapBase=0x801C8484`; `Banks=0x8018E18C`; `ModelTable=0x8018E19C`; `VertexBase=0x8018EAA0`; model header at `table + model*28 + 0xC` (vert offset `+0`, byte length `+4`*8, face offset `+0x10`, count `+0x14`); map 80x80 cells, 10-byte stride, half offset 0/5; tile 2048 units, height `u8[rec+1]*-128`, turn `u8[rec+2]&3`.
- Structs: 12800 halves (`i>>1` cell, `i&1` half; `_rest[12800]` float); per-model `Mesh { WaterVerts, WaterFaces, OtherVerts }`; model header 28 bytes; s16 xyz vertices; faces parsed by PSX packet type (`0x2C` quad, `0x24` tri).
- Control flow: `AtWalk` once per frame, gated on a hash of banks/rects/map; `Enter` after `func_8002E1F0`, `Leave` after the half; stands down while `TileWalk.Verifying`, `PlanarWalk.Mirroring`, swell off, or `!PrimBuffer.Relocated`.
- Overlays: `game` (globals).
- Env / settings keys: none directly (driven by `Waves`).
- RecompOne deps: patch `0085` (retained map carries free corners); runtime types `PrimBuffer` (`WaveScratch`, `WaveBytes`, `Relocated`), `PlanarReflections.RestHeight`, `RetainedScene.SetSwell`, `WaterWaves.Rects/RectN`, `PlanarWalk.Mirroring`.
- Mod-visible: `WaterSwell.Forget`, `Generation`, `IsFree`, `Publish`, `RestAt`, `AtWalk`, `Enter`, `Leave`, `Report`.
- Split note: generic = displaced scratch copy + rim/shared/free vertex classification; KF2 seam = map/bank addresses and the model header layout.

#### patches/ZBuffer.cs  (530 lines)
- Bucket: C (high) -- per-pixel depth testing is generic (runtime `0014`); the switch/probe and the arm/model brackets name KF2 functions.
- Does: console switch + probes for the runtime's Z-buffer (depth from GTE SZ), and brackets the first-person arm and model submitter so the occlusion pass knows what is a solid/blended surface.
- Hooks: post `DrawOTag` open `0x80016078`/game `0x80060818`/end `0x80013D80` (probe only); pre+post `func_80032400` (`ArmDraw`) and `func_80032588` (`ModelSubmit`), overlay `game`, to set `PolyAssembler.InArm`/`InModel`.
- Data: none (depth is recovered by the runtime); the three function addresses above.
- Structs: none.
- Control flow: frame boundary for the probe is `DrawOTag`; `SyncSource` keeps packet depth tied to Fast geometry; arm/models bracketed every frame.
- Overlays: `open`, `game`, `end`.
- Env / settings keys: `KF2_ZBUFFER`, `KF2_ZBUFFER_PROBE` (1/2), `KF2_ZBUFFER_THRESHOLD`, `KF2_ZBUFFER_SOURCE`, `KF2_ZBUFFER_BIAS`, `KF2_ZBUFFER_SLOPE`, `KF2_BLENDORDER`, `KF2_BLENDORDER_PROBE`, `KF2_AO_SOLID`; setting `kf2.zbuffer.on`; Video > Enhancements; `RuntimeReadyEvent`.
- RecompOne deps: patches `0014`, `0036`, `0050`, `0051`, `0079`; runtime types `GteDepth`, `GtePacketDepth`, `PolyAssembler` (`InArm`, `InModel`, `BlendedByKind`, ...), `BlendOrder`, `HookAttach`, `SymbolRegistry`, `HookManager`.
- Mod-visible: `ZBuffer.OnKey`, `DefaultBias`, `DefaultSlope`, `Enabled`, `Configure`, `SyncSource`, `Install`, `SetEnabled`, `BeforeModel`/`AfterModel`, `BeforeArm`/`AfterArm`, `AfterDrawOTag`; `ModInfo _self`.
- Split note: switch/probe generic; KF2 seam = `ArmDraw`/`ModelSubmit`/`DrawOTag` addresses and the `PolyAssembler` packet-depth integration.

#### patches/TileWalk.cs  (690 lines)
- Bucket: C (high) -- "reimplement the map walk in C# to learn which tile, where, by which assembler" generalizes; every address and layout is KF2's.
- Does: `func_80031C94` (24x24 cell sweep), `func_80031B1C` (a cell's two stacked halves) and `func_80031950` (a half: matrices, light, assembler) in C#, with a `verify` mode and `KF2_GPUWORLD_CELL` fallback.
- Hooks: replace `func_80031C94` `0x80031C94`, `func_80031B1C` `0x80031B1C`, `func_80031950` `0x80031950` (overlay `game`), `HookManager.AddReplace`. Direct calls into `Recompiled.KingsField2_game`: `func_8002E190`, `func_80031B1C`, `func_80031950`, `SetRotMatrix`, `SetTransMatrix`, `RotTrans`, `func_80014B88`, `SetLightMatrix`, `SetColorMatrix`, `func_8002DDDC`, `SetBackColor_game`, `func_8002E1F0`, `func_8002FECC`, `func_8002E1BC`, `func_80030C94`, `func_80030540`.
- Data: `Grid=0x80192EAC`; `GridOriginX=0x80192EA0`, `GridOriginZ=0x80192EA4`; `CamWorldX=0x80192E78`, `CamWorldY=0x80192E7C`, `CamWorldZ=0x80192E80`; `ViewMatrix=0x80192E18`; `MapBase=0x801C8484`; `LightBase=0x801930F0`; `FarFlag=0x8017E05C`, `FarEnable=0x8017E072`; `ModelTable=0x8018E19C`; `VertexBase=0x8018EAA0`; `MapSpan=0x50`, `GridSpan=0x18`. Conventions: model >=240 not drawn; flags bit0 lower/bit1 upper, 0x80/0x40 pick assembler; tile 10 bytes (lower +0/+1, upper +5/+6); light record 104 bytes (4x20 light matrices, colour +0x50, back +0x62, depth cue +0x66); world position tile-centred `(tx<<11) - CamWorld + 0x400`.
- Structs: 24x24 grid bytes; 80x80 map of 10-byte tiles; 12800 halves; 104-byte light records; subdivider scratch `sp+0x38`; stack frames 0x30/0x28/0x1050.
- Control flow: called once from stage 13; grid written by `CullGrid` (`func_8002D3A8`); mirror walk routes to `PlanarCull`/`RenderDistance`; subdivide a <16-face mesh before `func_80030540`; `RetainedMap.AtWalk`/`Waves.AtWalk`/`RenderDistance.Build` at walk start.
- Overlays: `game`.
- Env / settings keys: `KF2_TILEWALK`, `KF2_TILEWALK_CELL`, `KF2_TILEWALK_TILE`, `KF2_TILEWALK_PROBE`, `KF2_GPUWORLD_CELL` (`TakeInCell`); PGXP stands down.
- RecompOne deps: patches `0049` (EvenFog reads the stack) / `0050` / `0085`; runtime types `Gte` (`State`/`Save`/`Load`/`Diff`), `Interrupts.Poll`, `HookAttach`, `SymbolRegistry`, `HookManager`, `PolyAssembler`, `GpuWorld`, `RetainedMap`, `RetainedScene`, `ReflectionReach`, `PlanarWalk`, `PlanarCull`, `RenderDistance`, `Remaster.Faces/FaceProbe/Surfaces`, `WaterSwell`, `Waves`, `Pgxp.CpuTracking`.
- Mod-visible: `TileWalk.Enabled`, `CellEnabled`, `TileEnabled`, `Verifying`, `WalkCalls`/`CellCalls`/`TileCalls`, `CellsWalked`, `CellsDrawn`, `CurrentRecord`, `TakeInCell`, `Configure`, `Install`.
- Split note: generic = a map walk in C# for per-tile/per-assembler scene facts; KF2 seam = every address, the map/grid layout, light records and the assembler dispatch.

#### patches/Stage13.cs  (651 lines)
- Bucket: C (medium) -- taking a game's whole frame routine over in C# (per-call delegates, view override, replay-verify) is a general technique; this file is KF2's exact 19-call stage.
- Does: stage 13 `func_800342D8`, the renderer, in C#: nineteen calls in fixed order plus the HUD arithmetic; enables `ViewOverride` (draw from another camera), `DrawScene` (the drawing half) and holding the compass needle to the world tick.
- Hooks: replace `func_800342D8` `0x800342D8` (overlay `game`); under `verify`, pre+post on all 19 callees to record/replay. Each site is called through a delegate to the recompiled function so its detours/hooks fire.
- Hooks (callees, in order): `View 0x8002E22C`, `AnimatedTextures 0x8002DC78`, `Fade 0x80033FBC`, `CullGrid 0x8002D3A8`, `FrameHead 0x8002E064`, `SoundMark 0x800353AC`, `Arm 0x80032400`, `CompassError 0x80015374`, `Hud 0x80031D5C`, `Overlays 0x80033E78`, `Tiles 0x80031C94`, `Models 0x800331B4`, `Tint1 0x8003202C`, `Tint2 0x800320BC`, `Tint3 0x8003214C`, `Tint4 0x80032234`, `Present 0x8002E0FC`, `FrameGate 0x80017880`, `SoundService 0x8003549C`.
- Data: `HudRecords=0x80067774`, 14 x `0x24` (Shown `+0`, Model `+4`, Length `+8`, Pitch `+0x18`, Yaw `+0x1A`); `CompassShown=0x801994DE`, `OthersShown=0x801994DD`; `Hp=0x80199428`, `Mp=0x8019942C`, `GaugeA=0x8019942E`, `GaugeB=0x80199432`; `NeedleSpeed=0x8006E608`; `DigitModel=3`; gauge length 204 at 5000; needle yaw step `speed>>6`, damping `(v±7)>>3`. Camera block `CameraBlock` (view matrix, angles).
- Structs: 14 HUD records of `0x24`; call table of `(callee, returnAddress)`; needle spring at `0x8006E608` (angular velocity, angle units).
- Control flow: stage 13 is the frame; needle/HUD derivations gated on `FramePacing.FirstWalkOfTick`; nested redraw via `LoopPacing` (`InFrame`); verify records RAM at `View`/`CompassError`/`Hud` entry and `Arm`/`CompassError` exit.
- Overlays: `game`.
- Env / settings keys: `KF2_STAGE13`, `KF2_STAGE13_NEEDLE`, `KF2_STAGE13_PROBE`; no saved key.
- RecompOne deps: patch `0070` (hook order); runtime types `Camera`, `CameraBlock`, `FramePacing`, `Gte` (`State`/`Save`/`Load`), `CpuSnapshot`, `Differential`, `HookAttach`, `SymbolRegistry`, `HookManager`; KF2 systems it drives `MenuWorld`, `LoopPacing`, `FrameSmoothing`, `WaterMurk`, `PlanarWalk`, `HookOrder`.
- Mod-visible: `Stage13.NeedleSpeed`, `NeedleYaw`, `NeedleOnTick`, `InFrame`, `NeedleSteps`, `GaugeLengths`, `HudOnTick`, `HudTicks`, `HookOrder`, `ViewOverride`, `HideArmOnOverride`, `Handed`, `DrawScene`, `Configure`, `Install`, `Site`; `ModInfo _self`.
- Split note: generic = frame-routine takeover + per-call delegates + replay verify + `ViewOverride`; KF2 seam = the 19 addresses, the HUD record block and the needle spring.

#### patches/RetainedMap.cs  (797 lines)
- Bucket: C (high) -- "the static map as a world-space GPU mesh, so a reflection redraws the world" generalizes; the map/bank/light layouts are KF2's.
- Does: builds the area's map into world-space triangles on the GPU (`RetainedScene`), lit and fogged from the light records; rebuilds on a hash of map/bank/materials; also proves the mesh against the game's own GTE vertices.
- Hooks: none; `AtWalk`/`CheckHalf` called from `TileWalk`; direct calls `Recompiled.KingsField2_game.SetFogNear` (`func_8002DDDC`).
- Data: `MapBase=0x801C8484`, `MapBytes=80*80*10`; `LightBase=0x801930F0`, `LightBytes=64*104`; `Banks=0x8018E18C`; `LightColour=0x8006E604`; `VertexCache=0x8018EB94`; `ViewMatrix=CameraBlock.ViewMatrix`; model header `table + model*28 + 0xC` (verts `+0`, normals `+8`, faces `+0x10`, count `+0x14`, bytes `+4`); tile 2048 units, height `u8[rec+1]*-128`, turn `u8[rec+2]&3`; light record 104 bytes (4x20 matrices, LCM `+0x50`, BK `+0x62`, fog word `+0x66`); fog DQA/DQB via `SetFogNear(word&0x7FFF>>1, 200)`.
- Structs: half record 10 bytes (model, height, turn, light index, upper at +5); model bank header 28 bytes; `RetainedScene.Vertex`; 64 light records indexed by low 6 bits; `RetainedPlanes.Note` faces.
- Control flow: `AtWalk` from the frame's tile walk start (not mirrored); hash over map/bank/materials/rects/swell and the used light records; GTE state saved/restored around Build and PackRecords; `BeginFrame`/`GpuWorld.AtFrame` then `RetainedPlanes.Choose`.
- Overlays: `game` (globals).
- Env / settings keys: `KF2_RETAINED`, `KF2_RETAINED_PLANAR`, `KF2_RETAINED_CUBE`, `KF2_RETAINED_CUBESIZE`, `KF2_RETAINED_CULL`, `KF2_RETAINED_GATE`, `KF2_RETAINED_PROBE`, `KF2_RETAINED_LIT`, `KF2_RETAINED_MIPS`, `KF2_GPUWORLD_RECORDS`, `KF2_GPUWORLD_RECORDCHECK`; key `kf2.ssr.retained`; `RuntimeReadyEvent`, `OverlayLoadedEvent`.
- RecompOne deps: patches `0072`, `0077`, `0085`; runtime types `RetainedScene`, `RetainedPlanes`, `SurfaceMaterial` (`Rects`, `RectN`, `Water`), `PolyAssembler` (`RetainedTile`/`RetainedRecords`/`RetainedLight`/...), `Gte`, `EvenFog`, `CameraBlock`, `Camera`, `GteDepth`, `GpuWorld`, `Remaster.Surfaces`/`TextureKeys`, `PlanarReflections`, `ScreenReflections`, `RetainedModels`, `RemasterUniforms`.
- Mod-visible: `RetainedMap.OnKey`, `Enabled`, `RecordsOn`, `RecordCheck`, `Packs`, `LastPackMs`, `LastPackWhy`, `CheckCorners`/`CheckColour`/`CheckCue`/`CheckCueWorst`, `ReflectionsReady`, `Ready`, `Builds`, `LastWhy`, `LastBuildMs`, `Checking`, `Configure`, `Install`, `SetEnabled`, `AtWalk`, `ReadView`, `CheckHalf`, `CheckCorner`.
- Split note: generic = a static map kept as GPU triangles with a hash rebuild and a GTE-vertex check; KF2 seam = addresses, model bank, light records and GTE lighting/fog.

#### patches/RetainedModels.cs  (1535 lines)
- Bucket: C (high) -- "capture the frame's models to world space, cache their meshes and instance them on the GPU" generalizes; the packet format, addresses and GTE records are KF2's.
- Does: captures every model face the lit assembler is handed `func_8002F214` into the retained scene (world space, for reflections and authored-light shadows), and for the GPU world (0085) builds per-mesh caches, instances them, and draws blends/arm/sky.
- Hooks: none; called from `ModelWalk`/`PolyAssembler` (`Capture`, `CaptureMain`, `CaptureFlat`, `TryInstance`, `TryArm`, `TrySpecial`); reads the GTE directly.
- Data: `VertexBase=0x8018EAA0`, `VertexCache=0x8018EB94`, `LightColour=0x8006E604`, `FogMode=0x80192EA8`, `PolyModelTable=0x8018E19C`, `PosedBuffer=0x80190AD8`; model header `sub*28+0xC` (count `+0x14`, normals `+8`, face `+0x10`); face commands `0x24`/`0x2C`/`0x34`/`0x3C` (lit) and `0x30`/`0x38` (sky), corner offsets `0x0E..0x1E`, normal offsets `0x04..0x1C`; GTE control 0-28 (rotation/translation, light matrix 8-12, BK 13-15, LCM 16-20, proj 24-26, DQA/DQB 27-28); table far gate `+4`.
- Structs: cached `Mesh` (Start/Count/SemiCount, FaceAt/FaceN/FaceMode/FaceSemi, face/normal hashes); `RetainedScene.Vertex` corners (Dqa/Dqb/Curve/Rect/Flags/Rgbc/Light); `RetainedScene.ModelInstance` (mesh range, pose/morph/weight, Near/Far, matrices, lights, TwinMode/ViewSpace/Sky/Solid); 8192-entry vertex/normal caches.
- Control flow: `Capturing` gates on `ModelWalk.InWalk`/`PolyAssembler.Verifying`; main/mirror capture depend on `GpuWorld.ModelsActive`/`MirrorModelsActive` and GTE far colour == 0; mesh caches invalidated by hash and on `RetainedScene.MeshGeneration`/water rect changes; arm instance held in view space until `GpuWorld.AtFrame`.
- Overlays: `game` (globals; all via `ModelWalk`).
- Env / settings keys: `KF2_GPUWORLD_MESHES`, `KF2_GPUWORLD_MESHCHECK`, `KF2_GPUWORLD_POSES`, `KF2_GPUWORLD_POSECHECK`, `KF2_GPUWORLD_ARM`, `KF2_GPUWORLD_SKY`, `KF2_GPUWORLD_BLEND`, `KF2_GPUWORLD_BLENDSURFACES`, `KF2_GPUWORLD_TILE`, `KF2_GPUWORLD_MIRRORBLEND`; no saved keys.
- RecompOne deps: patches `0085`, `0077`; runtime types `RetainedScene` (`Vertex`, `ModelInstance`, `AddMainModel`/`AddDynamic`/`AddMesh`/`AddModelVertices`/`AddInstance`/`AddSky`/`AddBlendFace`, `PoseStore`, `MeshCorners`, `MeshGeneration`, `Find`, `Serial`, `View`/`MirrorView`), `PolyAssembler` (`FaceKept`/`TileFaceClips`/`TileFaceKept`/`TileMaterial`/`Verifying`), `ModelWalk` (`InWalk`, `SubmitKind`, `SubmitRecord`, `Placed`/`PlacedRot`/`PlacedX/Y/Z`, `SolidKind`/`ObjectKind`), `MoPose`, `Gte`, `GteDepth`, `RetainedMap`, `Remaster`, `RenderDistance`, `SurfaceMaterial`, `GpuWorld`.
- Mod-visible: `RetainedModels.Capturing`, `MainCapturing`, `MirrorCapturing`, `Models`/`Faces`/`Props`, `ByKind`, `Unread`, `MirrorModels`, `Main*` counters, `MeshesOn`, `InstanceWanted`, `Instanced`, mesh/instance counters, `TryInstance`, `LastMirrored`, `BlendOn`, `BlendSurfacesOn`, `TileOn`, `MirrorBlendOn`, `BlendRoutes`, `Subtractive`, `TryArm`, `ArmWanted`, `ArmOn`, `ArmPending`, `AtFrame`, `TrySpecial`, `SkyOn`, `PosesOn`, `InstancesPosed/Rigid`, `NoteSkippedTransform`, `Checking`/`CheckFaces`/..., `Check`, `Placed`, `Capture`, `CaptureMain`, `CaptureFlat`, `ForgetMeshes`, `Hash`.
- Split note: generic = model-face capture to world space + a hash-checked mesh cache + GPU instancing; KF2 seam = addresses, the PSX packet format, GTE light records and the object-walk integration.

### Summary

Counts: **A 0, B 3, C 12, D 0.**
- B: `TrueColor.cs`, `UiScale.cs`, `TexProbe.cs` (switches/diagnostics with no game data; only `KF2_*`/`kf2.*` strings and the `Kf2` namespace).
- C: `Subpixel.cs`, `SpriteAnim.cs`, `TintHold.cs`, `ViewClip.cs`, `Waves.cs`, `Widescreen.cs`, `WaterSwell.cs`, `ZBuffer.cs`, `TileWalk.cs`, `Stage13.cs`, `RetainedMap.cs`, `RetainedModels.cs`.
- No file is class A (all of these are `patches/`, not the vendored runtime) and none is strictly D; every C separates a mechanism that could be re-pointed at a sibling game from a small constant/struct seam.

Most important observations:
1. **The picture work lives in the runtime, not these files.** `TrueColor`, `Subpixel`, `ZBuffer` and `TexProbe` are switches/probes/census only (`patches/recompone/0010`, `0012`, `0014`, `0021`); only the game-side integration is here. Sharing a sibling game mostly means re-pointing addresses, not porting pixels.
2. **One SDK seam repeats: `DrawOTag` at `open 0x80016078`, `game 0x80060818`, `end 0x80013D80`.** Used by `Subpixel`, `Widescreen` (the single `Replace`), `ZBuffer` (and, per docs, `NoDither`/`Perspective`). A sibling game only needs this three-address table re-swept.
3. **The deepest KF2 coupling is the map/bank/record layout**, shared by `TileWalk`, `WaterSwell`, `RetainedMap`, `RetainedModels`, `Waves`, `RenderDistance` and `Reflections`: `MapBase=0x801C8484` (80x80x10), `Banks=0x8018E18C`, `ModelTable=0x8018E19C` (28-byte headers), `VertexBase=0x8018EAA0`, `VertexCache=0x8018EB94`, light records `0x801930F0` (64x104), fluid slots `0x80192D58` (8x0x18). This is the natural shared "KF map" abstraction a sibling game would reimplement.
4. **A common frame/tick contract runs through the C files**: `FramePacing.FirstWalkOfTick`, `TickedThisFrame`, `LogicHz`, `BoundaryDeadMs`, `StagesWillRun` — used by `SpriteAnim`, `Waves`, `WaterSwell`, `TileWalk`, `Stage13`. It lives in `patches/FramePacing.cs` (bucket C), not in the runtime, so it shares only once `FramePacing` does.
5. **A common GTE save/restore discipline**: `Gte.Save/Load`, `c.Snapshot/Restore`, `Gte.State`, `CpuSnapshot` bracket work in `TintHold`, `TileWalk`, `Stage13`, `RetainedMap`, `RetainedModels`. Generic.
6. **`verify` modes are a repeated three-way pattern** (`TintHold`, `TileWalk`, `Stage13`, and the assemblers per docs): run both, diff RAM/registers/GTE, let the recompiled result stand. A shared diff harness is a candidate B extraction.
7. **Mod-visible surface is uniform**: nearly every patch exposes public `Enabled`/`Configure`/`Install`/`SetEnabled` plus a `ModInfo _self`, and `KF2_*` env vars mirror `kf2.*` saved keys. That convention is game-agnostic; the key names and addresses are not.
8. **`Stage13` is the closest thing to D**: the 19 callee addresses, the 14-entry HUD record block and the needle spring are the least portable code in this batch. Its *technique* (frame-routine takeover, per-call delegates, replay verify, `ViewOverride`) is generic, but nothing about it survives a sibling game's re-sweep unchanged.
9. **`Subpixel` and `ZBuffer` are B-like in spirit but classified C** because each carries a hardcoded `DrawOTag` overlay/address table and (for `ZBuffer`) hooks KF2 `func_80032400`/`func_80032588`; their mechanism is entirely runtime.
10. **`WaterSwell`/`RetainedMap`/`RetainedModels` are the split-note archetype**: the general mechanism (displace a scratch copy; retain static geometry; instance models) is cleanly separable from the KF2 constant/layout seam, which is where a sibling port would do all its work.

## patches/remaster/
Read-only classification per `scratch/sharing/BRIEF.md`. Buckets: A RecompOne fork,
B game-agnostic infra, C generalizable mechanism with KF2 implementation, D KF2-only.
This is the remaster editor + pack system (`docs/REMASTER.md`). The `Editor.*`,
`Pack.*` and `Shell.*` families are grouped into one entry each with a line per file.

### Pack file formats (read from `Pack*.cs`, `docs/REMASTER.md`)

A remaster pack is an upstream asset pack (folder or zip under `packs/`) with
upstream's `pack.json` at the root (`id`, `name`, `author`, `version`, `priority`,
`game{id,strict}`) plus a `remaster/` directory. Layout:

```
packs/<pack>/
  pack.json
  textures/...
  remaster/
    materials.json                 # named material library
    textures.json                  # art -> material (content-keyed, every area)
    areas/<n>/surfaces.json        # tile halves, mesh rules, model rules -> material
    areas/<n>/lights.json          # authored point/spot lights
    areas/<n>/atmosphere.json      # light-record overrides + whole-area darkness/fog/sky
    areas/<n>/level.json           # tile byte edits (gameplay-changing)
    areas/<n>/props.json           # placed object-model props
```

Every area document carries `formatVersion`, `area`, `fingerprint`. Unknown fields
survive a round trip (documents held as `JsonNode` trees). `formatVersion` is 1.

- **materials.json**: `materials` object keyed by name. Per material keys:
  `reflectivity`, `f0`, `roughness`, `emissive` [3], `emissiveStrength`,
  `glowMode` (`additive`|`lit`), `glowFog`, `light`, `lightColour`, `glowLight`,
  `glowRadius`, `pulseAmount`, `pulseHz`, `pulseStyle` (`flicker`|sine),
  `metalness`, `specular`, `occlusion`.
- **textures.json**: `textures` array; each `index` (FNV/TextureTile key hex),
  optional `clut`, `material`, optional `note`. Content-keyed, needs no fingerprint.
- **areas/<n>/surfaces.json**: `fingerprint`; `tiles` array of
  `{x,z,half:"lower"|"upper"[,mesh,meshHash,faces:{faceIndex:materialName}],material}`;
  `meshes` array of `{mesh,meshHash[,material][,faces{]}`; `models` array of
  `{kind:"object"|...,model,material}`. Most specific wins: half's face, half,
  mesh's face, mesh, then the packet's texture rule.
- **areas/<n>/lights.json**: `lights` array; `name`, `type` (`point`|`spot`),
  `position` [3] world units (up is -Y, tile 2048), `colour` [3] linear 0..1,
  `intensity`, `radius`, optional `direction`, `cone` [inner,outer half-angles deg],
  `flicker{amount,hz}`, `enabled` (default true), `shadows` (default true).
- **areas/<n>/atmosphere.json**: `records` array; each
  `{record: N|"all"[,recordHash], back[3] bytes, lights:[{direction,colour}...3],
  fog, darkness, fogColour[3], fogPower, fogMax, sky[3]}`. `record":"all"` carries
  the area darkness/fog/sky.
- **areas/<n>/level.json**: `fingerprint`; `halves` array of
  `{x,z,half, mesh, height, collision, shape, light, stopsFlood}` (only authored
  fields present).
- **areas/<n>/props.json**: `fingerprint`; `props` array of
  `{name, model (0x100+def), position[3], rotation[3] deg, scale (number or [3]),
  half:"upper", enabled}`.

**KF2-specific parts of a pack** (the seam a sibling game must replace): the game id
`SLUS-00158` (`Pack.GameId`); the area number (`0x8017E060`) and its fingerprint
(FNV-64 of the 64,000-byte tile block less each half's +2, plus the first 0x600 bytes
of `0x801D8484`); tile-half keys `(area,x,z,lower|upper)` and mesh hashes; model keys
`(area,kind,model)`; light-record numbers and `recordHash`; the tile byte field layout
(`TileField`: mesh/height/collision/shape/light/stopsFlood at half +0..+4); prop model
ids (object def base `0x100`); texture keys from VRAM content (area-independent). Only
identifiers/hashes/the author's values — never disc payload.

---

#### patches/remaster/Host.cs  (129 lines)
- Bucket: C (high) -- the remaster's lifecycle/switch and feature registry is generic; the feature list and env defaults are KF2's.
- Does: owns the remaster switch, registers the five `IRemasterFeature`s, marshals `Pack.Poll`/`Identity.Poll`/`TileRewrites.Poll` at `VSyncEvent`, prints the probe.
- Hooks: none via HookManager; `Event.AddListener` on `RuntimeReadyEvent`, `OverlayLoadedEvent`, `VSyncEvent`. Installs `TextureResolver.Scroll = TextureKeys.ScrollLookup`.
- Data: none directly.
- Structs: `IRemasterFeature` interface (`Id`, `OnFrame`, `Detach`, `Probe`); `Features = [Surfaces, Lights, Atmosphere, Level, Props]`.
- Control flow: every feature's `OnFrame` runs once per frame on the game thread at `VSyncEvent`, after `Identity.Poll` and `Pack.Poll`, before `Compat.Poll`; `Editor.Install` runs once.
- Overlays: none itself; passes `OverlayLoadedEvent.Name` to `Identity.Invalidate`.
- Env / settings keys: `KF2_REMASTER` (on), `KF2_REMASTER_PACK`, `KF2_REMASTER_PROBE`, plus per-feature `KF2_REMASTER_LIGHTS/ATMOS/LEVEL/PROPS`; setting `kf2.remaster.on`.
- RecompOne deps: `Runtime.Mem`, `Event`, `PatchSettings`, `AssetPack`/`TextureResolver`; runtime remaster types via features.
- Mod-visible: `public static class Host`; `Enabled`, `Features`, `Configure`, `Install`, `SetEnabled`, `OnKey`.
- Split note: generic registry/probe plumbing; the concrete `Features` array and env names are the KF2 seam.

#### patches/remaster/Identity.cs  (347 lines)
- Bucket: C (high) -- "detect an area settled and fingerprint its data block" generalizes, but every address/layout is KF2.
- Does: reads guest RAM to detect when an area has settled (player stands on a drawn half at its floor height, twice 200 ms apart), takes an FNV-1a 64 fingerprint of the tile block + collision shapes, and defines the key types (`TileKey`, `ModelKey`).
- Hooks: `pre` on `game`/`0x80017244` (the loader's word-copy `func_80017244`) via `HookAttach.OnOverlayLoad` / `HookManager.AddPre`, to hash the source buffer as the loader copies it in.
- Data: `TileBase=0x801C8484` (80*80*10), `ShapeBase=0x801D8484` (0x600), `AreaAddr=0x8017E060`, `MaxHpAddr=0x80199426`, player pos `0x801994EC/F0/F4`, half-select `0x801D9C8E`, `CopyAddr=0x80017244`. Conventions: tile 2048 units, floor `-(h<<7)`, half +2 (collision flags footprint) excluded from the hash.
- Structs: tile half = 5 bytes at `TileBase + (z*80+x)*10 + half*5`; `TileKey(Area,X,Z,Half)` names `tile:A:X:Z:lower|upper`; `ModelKey(Area,ModelKind,Model)` names `model:A:kind:id`.
- Control flow: `Invalidate` on every overlay load (`fdat*` sets `_module`); `Poll` once a frame; settles only after an `fdat` module has loaded and the fingerprint is stable ~200 ms; `Baseline` = loader's copy (disc data kept in port memory only, never written to a document).
- Overlays: `game` (loader `func_80017244` and globals); reads area byte regardless of overlay.
- Env / settings keys: `KF2_REMASTER_PROBE` (via `Host`), `Identity.Probe`.
- RecompOne deps: `HookManager`, `SymbolRegistry`, `ModInfo`, `CpuContext`, `IMemory`, `HookAttach`, `OverlayLoadedEvent`; patch numbers none directly (relies on loader hooks).
- Mod-visible: `public static class Identity`; `Span/Stride/HalfBytes/TileUnits/TileBase/ShapeBase`, `Area`, `Fingerprint(Ulong)`, `FingerprintText`, `Settled`, `Baseline`, `BlockLoads`, `AreaSettled` event, `Poll/Invalidate/Install`, `HalfRecord`, `FromRecord`, `PlayerTile`; `TileKey`, `ModelKey` records.
- Split note: the FNV fingerprint and settle idea are generic; addresses, block layout and hash exclusions are KF2.

#### patches/remaster/Pack.cs + Pack.Layers.cs + Pack.Level.cs + Pack.Props.cs + Pack.Share.cs  (1244+414+95+135+66 lines)
- Bucket: C (high) -- a generic layered JSON-document pack system, but its manifest id, area/fingerprint gating and every document key are KF2.
- Does: reads/writes/watches the working pack and any pack under `packs/`, merges layers per key, holds one undo/redo stack, and exposes typed accessors for materials, surfaces, textures, models, lights, atmosphere records, level halves and props.
- Hooks: none; parsing is off-thread, swapped in at `Pack.Poll` (called from `Host.Frame` at VSync).
- Data: none by itself (Identity/features read RAM).
- Structs: `Set` (Materials, Textures, `Surfaces/Lights/Atmosphere/Level/Props` per-area `JsonObject`s); `Material`, `Uses`, `TileFaces`, `MeshRule`, `TextureRule`, `ModelRule`, `Light`, `RecordOverride`, `Prop`, `HalfEdit`, `AreaDocument`, `Layer`. Records are materialised from JSON; unknown JSON fields survive.
- Control flow: `Load` builds `_base` from enabled layers then merges the working pack; `Watch` uses a `FileSystemWatcher` parsed on a `Task.Delay(250)` continuation; `Poll` swaps it if not dirty; `Save` writes only the working pack's diff over `_base` with `"removed":true` entries. Area fingerprint mismatch sets a layer aside whole.
- Overlays: none.
- Env / settings keys: `KF2_REMASTER_PACK` (root); settings `kf2.remaster.layer.<id>` per layer; `Pack.GameId = "SLUS-00158"`.
- RecompOne deps: `AssetPack`/`PackManifest` (upstream `RecompOne.Runtime.Assets`) to open layers; `PatchSettings`.
- Mod-visible: `public static partial class Pack` with `Root`, `Version`, `Load/Save/Watch/Poll`, `Materials()`, `Tiles/TileMaterial/SetTile`, `TileFaceLists/MeshRules/FaceMaterial/SetFaces/SetMeshMaterial`, `TextureRules/SetTextureMaterial`, `ModelRules/SetModelMaterial`, `Lights/AddLight/SetLight/...`, `Records/SetRecord/CopyRecord/...`, `LevelEdits/SetLevelField`, `Props/AddProp/SetProp`, `Export/AreaDocs`, `Undo/Redo`, `Layers/SetLayerEnabled`, plus all the record structs.
- Split note: the JSON tree/merge/watcher/undo machinery and upstream manifest use are portable; `GameId`, the per-area fingerprint gate, the layer key scheme and every document schema are KF2.

#### patches/remaster/Pack.cs  (1244 lines) -- family detail
- The working pack and its documents: paths, `Load/Save/Watch/Poll`, the materials library, surfaces (tiles/meshes/models/faces), texture rules, model rules, lights, atmosphere records, undo. Writes upstream `pack.json` once. Keeps `JsonNode` trees for unknown-field survival.

#### patches/remaster/Pack.Layers.cs  (414 lines) -- family detail
- Layering and removal: finds packs beside the working pack, opens folder/zip via `AssetPack.Open`, checks `TargetsGame(GameId)`, merges by `EntryKey` per collection, writes diffs, and owns the per-key `"removed":true`/`null` semantics and fingerprint set-aside.

#### patches/remaster/Pack.Level.cs  (95 lines) -- family detail
- The `level.json` document: `halves` array of author field values, parsed through `TileField` with range checks; set/clear one field or reset a half as undo entries.

#### patches/remaster/Pack.Props.cs  (135 lines) -- family detail
- The `props.json` document: named placed props with model/position/rotation/scale/half, one undo entry per edit; `rotation` in degrees, scale can be a scalar.

#### patches/remaster/Pack.Share.cs  (66 lines) -- family detail
- Compatibility report inputs (`AreaDocs()`: area/kind/fingerprint/entry count) and `Export`, which zips the pack in upstream layout, refusing while dirty.

#### patches/remaster/TileField.cs  (64 lines)
- Bucket: D (high) -- the exact byte/bit layout of this game's tile-half records.
- Does: describes one editable part of a tile half (byte offset + bit mask) so level edits name fields, never raw bytes.
- Hooks: none. Data: none of its own (offset/mask constants describe the block in `Identity`).
- Structs: `TileField` with `Offset`, `Mask`, `Shift`, `Max`, `IsFlag`, `IsBits`, `Read/Bits/Valid`; the six fields `Mesh +0 0xFF`, `Height +1 0xFF`, `Collision +2 0xF8`, `Shape +3 0xFF`, `Light +4 0x3F`, `StopsFlood +4 0x80`.
- Control flow: none.
- Overlays: none.
- Env / settings keys: none.
- RecompOne deps: none.
- Mod-visible: `public sealed class TileField` and `All`, `Find`, `Valid`, `Takes`, `Read`, `Bits`.
- Split note: pure mechanism with KF2's exact bit layout; a sibling game supplies its own fields/masks.

#### patches/remaster/Editor.cs + Editor.Header.cs + Editor.Layout.cs + Editor.Material.cs + Editor.Lights.cs + Editor.Atmosphere.cs + Editor.Level.cs + Editor.Props.cs + Editor.Share.cs  (602+107+153+417+487+440+116+136+95 lines)
- Bucket: C (high) -- an ImGui document editor over the pack; the UI mechanism is generic, every address/key/record it names is KF2.
- Does: `Editor` (static partial, `Panel`) draws the Shift+E editor: a header, six tabs (Material, Lights, Atmos, Level, Props, Pack), picking faces/models/textures from the frame's own triangles, live gizmos over `OutputView`, and document changes through `Pack` only (never a side table or uniform).
- Hooks: `FramePacing.PauseWhen(() => Open && Identity.Area >= 0)`; `Event.AddListener<KeyboardEvent>` for Shift+E, Ctrl+Z/Y/S; panel registered with `PanelManager.Register`; `Stage13.ViewOverride` via `EditorCamera`. No HookManager detours of its own.
- Data: player eye/feet `0x801994EC/F0/F4` (`PlayerFeet`, `PlayerLightPosition`, `Add here`); half record read through `Identity.HalfRecord`; `Editor.MeshOf` reads the half's model byte; `OutputView.GameW/H/Min/Size/Max`, `OutputView.DockId`, `Display.WideMargin` for window<->game pixels; `TileField` offsets; `Faces.Last`/`Faces.Nearest` for picking; `GteDepth`/`CameraBlock.ViewMatrix` via `Lights.ReadView`.
- Structs: `Editor.Tab` (Material/Lights/Atmos/Level/Props/Pack), `Editor.Grow` (Connected/Texture/Mesh); selection state `Selected`, `SelectedFaces`, `SelectedModel`, `SelectedTexture`, `AnyPalette`, `MeshScope`, `SelectedLight`, `SelectedProp`; `with`-record use of `FaceRef`/`TileKey`/`ModelKey`.
- Control flow: `Host.Frame` sets `Faces.Recording = Editor.Open || FaceProbe.On` and `Faces.Wanted`; panel `Draw` runs inside the ImGui frame, pauses the world (so stage gating holds but the renderer keeps drawing), and edges edits through `Pack.Preview*`/`Commit*` so a held slider/drag is one undo entry. A click picks from `Faces.PickAt` (the depth buffer's answer); Place arms the next click.
- Overlays: none directly; reads KF2 RAM through `Runtime.Mem` regardless of overlay.
- Env / settings keys: no env var of its own; settings `kf2.remaster.on` (via `Host`), writes `RetainedMap.OnKey` ("Turn on world reflections"); localisation key `kf2.remaster.editor` (en/pt-BR/es-419).
- RecompOne deps: `FramePacing`, `Stage13` (`ViewOverride`, `Handed`, `HideArmOnOverride`), `Camera`, `Camera.Read/ViewMatrix`, `GteDepth.ProjH/Cx/Cy`, `OutputView`, `PanelManager`, `IPanel`, `Localization`, `FontSet`, `HotkeyGate`, `Mouse`, `PatchSettings`, `Event`/`KeyboardEvent`, `Display`; runtime `SurfaceMaterial`, `RemasterUniforms`, `RetainedScene`, `Reflections`, `RetainedMap`, `PerPixelLighting`, `Lights` (Kf2). Patch numbers: reads through `Faces`/`Surfaces` which depend on `0048`, `0050`, `0067`, `0071`, `0072`.
- Mod-visible: `public static partial class Editor` -- `Open`, `Selected`, `SelectedFaces`, `SelectedModel`, `SelectedTexture`, `AnyPalette`, `MeshScope`, `SelectedLight`, `SelectedProp`, `Tab`, `Grow`, `SetOpen`, `ShowTab`, `ActiveTab`, `Select`, `SelectFaces`, `SelectModel`, `SelectTexture`, `GrowSelection`, `Effective`, `Assign`, `AssignTexture`, `MeshOf`, `TextureOf`, `PlaceAt`, `SurfaceAt`, `PropPlaceAt`, `PlayerFeet`, `PlayerLightPosition`, `Blocked`, `Install`, `Register`, `Docked`, `PanelMin/Max`.
- Split note: the ImGui panel, grid/undo/place machinery and docks are portable; the tabs, keys, record numbers, tile-byte semantics and world-unit conventions are KF2. `Patches/settings/RemasterPage.cs` (outside this dir) provides the settings pages and shares `kf2.remaster.on`.

Editor family, per file:
- `Editor.cs` -- panel shell, yolks picking/gizmos/selection, window<->game pixel conversion, highlight overlays, hotkeys and panel registration.
- `Editor.Header.cs` -- header: remaster switch, save/undo/redo, area+selection+fingerprint, selection tint/player-tile/free-camera toggles, warnings.
- `Editor.Layout.cs` -- two-column grid, icon buttons/toggles, reset buttons, wrapped/right text, fitting, Font Awesome icons.
- `Editor.Material.cs` -- Material tab: selection card (Assign to: face/half/mesh-face/mesh/texture, Any palette), Result + rule, material library editor (finish/reflection/shading/glow/gives-light/pulse).
- `Editor.Lights.cs` -- Lights tab: add at eye/Place, list and inspector (type/pos/colour/intensity/radius/cone/flicker/on/shadows); world-space gizmos (reach rings, floor line, spot cone, axis arrows) and dragging.
- `Editor.Atmosphere.cs` -- Atmos tab: record picker (follow player / by use / edited), area darkness, area fog colour/curve/max/sky, record back colour + 3 lights (bearing/elevation/strength) + fog start/shape; copy-to popup; overlay arrows and half tint.
- `Editor.Level.cs` -- Level tab: `Apply level edits` switch, the selected half's `TileField`s as game's/loaded/edited, floor/neighbour readout, delete/reset.
- `Editor.Props.cs` -- Props tab: add from a selected object model (Here/Place), list and inspector (position/rotation/scale/upper/enabled/model/delete).
- `Editor.Share.cs` -- Pack tab: save/reload/export, layers with enable checkboxes and set-aside, per-area compatibility report tree.

#### patches/remaster/Shell.cs + Shell.Camera.cs + Shell.Level.cs + Shell.Props.cs  (750+90+150+120 lines)
- Bucket: C (high) -- a JSON command surface for the editor/pack; the verb set is KF2's, the transport is `KF2_SHELL`/`AgentServer`.
- Does: exposes `edit`, `select`, `set`, `pack`, `remaster`, `light`, `textures`, `atmos`, `level`, `camera`, `prop` over the MCP/agent command channel, all through the same `Pack`/`Editor` calls the panel makes, so the undo stack sees them.
- Hooks: none; runs on the game thread from `AgentServer`'s VSync queue. `Verbs`/`Help` are registered by `AgentServer.cs` (line ~321/444/622); `SaveCheck` and `Snap` are separate commands on the same channel.
- Data: none of its own; reads via `Identity`/`Faces`/`Lights.ReadView`/`ModelWalk.Scene`/`Camera.Read`/`Atmosphere`/`TileRewrites`/`Props.LiveObjects`.
- Structs: none new; serialises `Pack.Light`, `Pack.Prop`, `Pack.Material`, `Pack.RecordOverride`, `TileField`, `FaceRef`, `TileKey`/`ModelKey` as JSON.
- Control flow: one request per line on TCP 127.0.0.1:27900, one JSON reply; `snap`'s reply is deferred to the presenting thread. Document mutations go through `Pack.*` exactly as the panel does.
- Overlays: none distinct.
- Env / settings keys: reached through `KF2_SHELL`; verbs may set saved settings (`RetainedMap.OnKey`, `kf2.remaster.level`, layer keys) indirectly.
- RecompOne deps: via `Editor`/`Pack`/`Lights`/`Atmosphere`/`Props` -- `Runtime.Mem`, `Camera`, `Stage13`, `ModelWalk.Scene`, `RemasterUniforms`, `RetainedScene`, `SurfaceMaterial`, `AgentServer`.
- Mod-visible: `public static partial class Shell` -- `Verbs`, `Help`, `Run(string,string)`; the per-family verbs are private but public entry points are used by `AgentServer`.
- Split note: the command/JSON shape is portable; the keys, field names, record numbers, world units and gameplay warnings are KF2. `Shell.Camera.cs` drives `EditorCamera`; `Shell.Level.cs` drives `TileField`/`Level`; `Shell.Props.cs` drives `Props`/`Pack.Props`.

#### patches/remaster/Faces.cs  (376 lines)
- Bucket: C (high) -- face identity, subdivider counting and a pick from the frame's triangles generalize; the mesh-table layout and faces-per-polygon are KF2.
- Does: names a polygon by `(tile half, mesh, face index)`; maps the subdivider's output back to source faces by counting; records the frame's triangles during `SealDepth`; picks faces/models under a game pixel; hashes a mesh and grows selections.
- Hooks: none by HookManager directly; called from `PolyAssembler`'s face loops (`Faces.Enter`), the subdivider (`Faces.Subdivided`), `PolyAssembler.SealDepth` (`Faces.Seal`), and `TileWalk`/`Surfaces`.
- Data: tile mesh table pointer `0x8018E19C` (`ModelTable`), noted per half because the object walk repoints it (`NoteTable`); reads mesh headers `table + 0xC + model*28` with `+0x10` face offset, `+0x14` face count; packet corner words; `GtePacketDepth.Rec` depths/`Material`.
- Structs: `FaceRef(TileKey Tile, int Mesh, int Face)`; `Tri` (3 screen corners + depths, `Rec`=half record or 0 for a model, `Mesh`, `Face`, `Label`, `Tex`, `Model`, `Kind`, `Prop`); `MeshFace(Cmd, Clut, Tpage, Verts, Rect)`; mesh entry stride 28, command `>>24` (0x2C/0x2E/0x24/0x26 split 4, else 1), UV rect.
- Control flow: `Host.Frame` sets `Wanted`/`Recording`; `FrameStart` swaps `_last`/`_cur` at the tile walk from the real camera (skipped for the planar mirror); `Seal` only while recording and not mirroring.
- Overlays: `game` globals (`0x8018E19C`).
- Env / settings keys: driven by `KF2_FACE_PROBE` (via `FaceProbe`), no key of its own.
- RecompOne deps: `PSMemory`, `IMemory`, `GtePacketDepth`, `PolyAssembler.InModel`/`TileMaterial`, `ModelWalk.SubmitModel/SubmitKind/SubmitRecord`, `PlanarWalk.Mirroring`, `TileWalk.CurrentRecord`; KF2 `Props`, `Surfaces`.
- Mod-visible: `public static class Faces`; `Wanted`, `Recording`, `Current`, `MapRefused`, `Last`, `TileTable`, `TableSerial`, `PickedProp`, `Enter`, `Subdivided`, `FrameStart`, `Seal`, `Nearest`, `PickAt`, `Mesh`, `MeshHash`, `Connected`, `SameTexture`, `MeshFace`, `Tri`, `FaceRef`; `Coplanar`, `DepthAt`, `NoteTable`, `LeaveHalf`, `SubdividedDone`.
- Split note: generic "key a polygon by index, count the subdivider, pick by depth"; KF2 seam is the mesh-table address/layout, command codes and per-half record addressing.

#### patches/remaster/Surfaces.cs  (448 lines)
- Bucket: C (high) -- "resolve authored materials to ids and stamp them into packet records" generalizes; the tile/face/model keying and the mesh hash are KF2.
- Does: the `surfaces` feature: names to ids from `SurfaceMaterial.FirstAuthored`, an 80x80x2 id table plus per-half face lists, area-wide mesh rules, model rules and content-keyed texture rules; publishes glow/pulse light tables and `PolyAssembler.KeepForGlow`.
- Hooks: no HookManager detours; called from `TileWalk` (`EnterHalf`), `Faces` (`Face`), `ModelWalk`/`PolyAssembler` (`EnterModel`, `PacketMaterial`, `MeshFaceMaterial`), and `Lights` (`GlowLight`, `Pulse`).
- Data: none directly; reads `SurfaceMaterial` runtime arrays and `Faces.MeshHash` (thence `0x8018E19C`).
- Structs: `_table` byte[80*80*2] one id per half (index `(z*80+x)*2+half`); `_tileFaces` mesh+`byte[] Ids` by half index; `_meshWhole`/`_meshFaces` by mesh; `_models` by `(ModelKind,int)`; `_textures` by `TexKey` (with/without CLUT); per-id `GlowLight`/`GlowRadius`/`Pulse`/`IdNames`.
- Control flow: `OnFrame` after `Host.Enabled && Identity.Settled`, re-applies when `Pack.Version`, `Identity.Settles` or `Faces.TableSerial` moves; fingerprint mismatch refuses the area's tile keys but keeps texture rules; pulse steps once per world tick (`Lights.Ticks`).
- Overlays: none.
- Env / settings keys: none of its own.
- RecompOne deps: `SurfaceMaterial` (`FirstAuthored`, `Count`, `Reflectivity/F0/Roughness/Emissive/EmissiveAdditive/EmissiveUnfogged/Metalness/Specular/Occlusion`, `Changed`, `Keep*`), `PolyAssembler`, `Runtime.Mem`, `IMemory`; patch `0067` (table) and `0071` (glow light) at the runtime end.
- Mod-visible: `public sealed class Surfaces : IRemasterFeature`; `ByTexture`, `TexturesApplied`, `TexturePackets`, `PerFace`, `MeshRefused`, `NoMaterial`, `IdNames`, `GlowLight`, `GlowRadius`, `Pulse`, `Serial`, `ModelsGiveLight`, `Refused`, `Unallocated`, `TilesApplied`, `Packets`, `GivesLight`, `AnyGivesLight`, `TextureId`, `EnterHalf`, `Face`, `FaceOf`, `Authored`, `EnterModel`, `IdOf`, `OnFrame`, `Detach`, `Probe`.
- Split note: layer name/scope precedence and id allocation are portable; KF2 is the keying: half record addressing, face index, mesh hash, model kinds, texture VRAM rects.

#### patches/remaster/Lights.cs  (575 lines)
- Bucket: C (high) -- the light-list/cull/flicker/upload mechanism is generic; the DrawOTag addresses, view-matrix layout and tile/mesh glow derivation are KF2.
- Does: the `lights` feature: takes the pack's authored lights into GTE view space before each `DrawOTag`, culls and publishes the nearest 16 to `RemasterUniforms` for the prim shader, steps flicker on the world tick, derives a light per glowing tile face/material and per glowing model draw, and assigns shadow slots.
- Hooks: `pre` on `open`/`0x80016078`, `game`/`0x80060818`, `end`/`0x80013D80` (each overlay's `DrawOTag`) via `HookAttach.OnOverlayLoad` + `HookManager.AddPre`/`Commit`; requires the `game` one. `Lights.Install`; also sets `RetainedScene.ShadowsWanted`, `PolyAssembler.KeepForLights`.
- Data: view matrix `CameraBlock.ViewMatrix` (fixed 4.12 at `E(off)`), translation `+0x14/18/1C`, `Camera.Read`, `GteDepth.ProjH/Cx/Cy`; tile mesh table `Faces.TileTable`; half +1 height, +2 quarter-turn bits, half model byte; model positions from `ModelWalk.Scene`; `FramePacing.FirstWalkOfTick` for the tick.
- Structs: `Lights.View` (R matrix, T, Cam, H, Cx, Cy; `ToView`/`ToWorld`/`Project`/`Unproject`); candidates `_frame` + `_frameSrc` (material id); `Pack.Light`; `RemasterUniforms` arrays `LightPos/LightWorldPos/LightCol/LightDir/LightWorldDir`; shadow slots `_slotName` + `RemasterUniforms.LightShadow`.
- Control flow: `OnFrame` re-resolves when `Pack.Version`/`Identity.Settles`/`Surfaces.Serial`/shadow switches move; `BeforeDrawOTag` runs each `DrawOTag` and gathers candidates, sorts authored before derived, culls behind the eye or past 12 tiles, sends 16; pulse/flicker evaluated at `Ticks` (20/s) so they hold while paused.
- Overlays: `open`, `game`, `end` (DrawOTag entry points).
- Env / settings keys: `KF2_REMASTER_LIGHTS`, `KF2_REMASTER_SHADOWS`, `KF2_REMASTER_SHADOW_MODELS`, `KF2_REMASTER_SHADOW_SIZE/BIAS/OFFSET/SOFT`; `Lights.SetShadows` from shell.
- RecompOne deps: `RemasterUniforms` (`MaxLights`, `MaxShadows`, `Publish`, `Enabled`, `Shadow*`, `Uploads`, `LitBatches`), `RetainedScene` (`ShadowsWanted`, `ShadowModels`, `Serial`, `Find`), `PolyAssembler.KeepForLights/Keep`, `CameraBlock`, `Camera`, `GteDepth`, `FramePacing`, `HookAttach`, `SymbolRegistry`, `HookManager`, `ModInfo`, `CpuContext`, `IMemory`; KF2 `Faces`, `Surfaces`, `ModelWalk`; patch `0071` (light term), `0077` (shadow cubemaps), `0072` (retained scene).
- Mod-visible: `public sealed class Lights : IRemasterFeature`; `Refused`, `Authored`, `Derived`, `DerivedLights`, `ModelLights`, `Sent`, `Culled`, `Frames`, `Ticks`, `ShadowsOn`, `View`, `ReadView`, `Configure`, `ConfigureShadows`, `SetShadows`, `Install`, `Noise`, `OnFrame`, `Detach`, `Probe`.
- Split note: the pass shape (gather/cull/sort/upload/flicker/shadow slots) is portable; KF2 seam is the three DrawOTag addresses, the camera-block/view-matrix layout, and deriving glows from KF2 tile meshes/records.

#### patches/remaster/Atmosphere.cs  (411 lines)
- Bucket: C (high) -- "override the game's per-area light/fog records after the copy" generalizes; the source/destination addresses, 80x0x68 layout and record field offsets are KF2.
- Does: the `atmosphere` feature: after stage 1 copies the 80 light records, writes the pack's record overrides over the copy; applies the area's darkness scale and fog colour/curve; overrides the game's background clear with the `sky`.
- Hooks: `post` on `game`/`0x8002C944` (stage 1 record copy) and `pre`+`post` on `game`/`0x80060870` (`PutDrawEnv`), via `HookAttach.OnOverlayLoad` + `HookManager.AddPost/AddPre`/`Commit`.
- Data: source records `Src=0x800679A0` stride `0x2C`; destination `Dst=0x801930F0` stride `0x68`; field offsets `SrcLight 0x00/SrcColour 0x14/SrcBack 0x26/SrcFog 0x2A` and `DstLight 0x00/DstColour 0x50/DstBack 0x62/DstFog 0x66`; DRAWENV `+0x18` is-bg, `+0x19` bg colour; GTE 4.12 units (1.0 = 4096); `Fixed` writes.
- Structs: `Record(Vector3[] Direction, Colour, int[] Back, int Fog)`; `Pack.RecordOverride`; per-record `_own` override index; fog word `(low15)/2` view units, bit `0x8000` linear, >=32000 none; `Darkened = 64` records a tile half can name.
- Control flow: `OnFrame` resolves on `Pack.Version`/`Identity.Settles`; `AfterCopy` runs every stage 1 pass and re-derives from source so it never compounds; `BeforeDrawEnv`/`AfterDrawEnv` bracket the game's own clear.
- Overlays: `game` (stage 1 and PutDrawEnv).
- Env / settings keys: `KF2_REMASTER_ATMOS=0` (via `Host`).
- RecompOne deps: `RemasterUniforms` (`FogColour`, `SkyColour`, `FogPower`, `FogMax`, `PublishFog`, `FogOn`, `FogActive`, `FogBatches`), `PolyAssembler.KeepFogged`, `HookAttach`, `SymbolRegistry`, `HookManager`, `ModInfo`, `CpuContext`, `IMemory`, `Identity` (via Host); patch `0074` (fog/sky), `0048` (per-pixel chain).
- Mod-visible: `public sealed class Atmosphere : IRemasterFeature`; `Src`, `Dst`, `SrcStride`, `DstStride`, `Records`, `Darkened`, `Darkness`, `FogColour`, `FogPower`, `FogMax`, `Sky`, `Refused`, `Applied`, `Stale`, `Passes`, `Calls`, `SkyClears`, `NoClear`, `Active`, `Configure`, `Install`, `SourceHash`, `Game`, `Effective`, `Usage`, `UnderPlayer`, `DescribeFog`, `Json`, `Record`, `OnFrame`, `Detach`, `Probe`.
- Split note: the "override the game's own record via the game's renderer" idea is portable; addresses, stride, field offsets and the tile-record count are KF2.

#### patches/remaster/Level.cs  (291 lines)
- Bucket: D (high) -- writes this game's tile bytes and collides with its run-time tile rewrites; no portable content.
- Does: the `level` feature: applies the pack's `level.json` tile edits to the settled area's block, holding each written byte and putting it back only while the bits it owns still read as written.
- Hooks: none; runs from `Host.Frame` on the game thread and writes `Runtime.Mem`.
- Data: `Identity.TileBase`/`ShapeBase`, `TileField` offsets/masks, `Identity.Baseline`, `Identity.BlockLoads`; conventions: heights in 128 steps, floor `-(h<<7)`, `+2` footprint excluded, `+4 & 0x3F` light record.
- Structs: `FieldState(Live, Loaded, Edit)`; `ByteWrite(Addr, Mask, Value)`; `_holding`/`_before` maps address->(mask,value)/byte; `_why` per-half refusal reasons; `_meshes`/`_shapes` sets of the area's own block.
- Control flow: apply only when `Level.Enabled && Host.Enabled && Identity.Settled` and the fingerprint and loader baseline match; per half refuse if any field unusable or the game rewrote a byte but `+2`; `Revert` before re-apply; `Forget` when `Identity.BlockLoads` moves.
- Overlays: none.
- Env / settings keys: `KF2_REMASTER_LEVEL`; setting `kf2.remaster.level`; `Label = "changes gameplay"`.
- RecompOne deps: `Runtime.Mem`, `IMemory`, `RuntimeReadyEvent`, `PatchSettings`; KF2 `Identity`, `Pack`, `TileField`, `TileRewrites`.
- Mod-visible: `public sealed class Level : IRemasterFeature`; `OnKey`, `Label`, `Enabled`, `Configure`, `Install`, `SetEnabled`, `Holding`, `Generation`, `Refused`, `Authored`, `Applied`, `RefusedHalves`, `KeptGames`, `Status`, `State`, `NeighbourHeights`, `Unusable`, `CannotEdit`, `OnFrame`, `Detach`, `Probe`.
- Split note: none -- a sibling game gets its own tile-field definition and block addresses; only the hold/revert discipline is reusable.

#### patches/remaster/Props.cs  (249 lines)
- Bucket: D (high) -- places copies of this game's object records; the object table, stride, definition base and model numbering are KF2.
- Does: the `props` feature: copies a live object of a chosen model into a port-owned scratch object record above 2 MB (`PrimBuffer.PropScratch`), sets position/rotation/scale/half, and has the C# object walk submit each after the game's own objects.
- Hooks: none directly; `ModelWalk` calls `Props.Walk` after the table (`ModelWalk.Ordinary` per prop).
- Data: object table `0x80177714` stride `0x44`, count 396; object definitions `0x80175914` (stride 24); model id = def + `ModelBase 0x100`; record fields `+0x6` def, `+4` kind, `+0x14/18/1C` position, `+0x24/26/28` rotation (0x1000/turn, 4096=360), `+0x2C/2E/30` scale, `+0` half, `+3` flags; liveness kind sentinels 0x1F/0xF0/0xFF skipped.
- Structs: `Pack.Prop(Name, Model, Position, Rotation, Scale, Upper, Off)`; `ObjectInfo(Slot, Model, Kind, DefKind, Half, Clip, Assembler, Flags, Position, Rotation, Scale)`.
- Control flow: `OnFrame` rebuilds when `Pack.Version`/`Identity.Settles`/want moves, and retries every second for models the area loads later; `Walk` submits only while enabled, settled and the C# walk is active.
- Overlays: none.
- Env / settings keys: `KF2_REMASTER_PROPS=0` (via `Host`).
- RecompOne deps: `PrimBuffer` (`PropScratch`, `PropBytes`, `Relocated`), `ModelWalk` (`Ordinary`, `Enabled`, `WalkEnabled`, `Verifying`, `SubmitCalls`), `Runtime.Mem`, `CpuContext`/`PSMemory`, `IMemory`.
- Mod-visible: `public sealed class Props : IRemasterFeature`; `ModelBase`, `Max`, `Refused`, `Authored`, `Resolved`, `Submitted`, `Walks`, `Submits`, `Configure`, `Status`, `Unusable`, `LiveObjects`, `ObjectInfo`, `Walk`, `NameOf`, `Angle`, `OnFrame`, `Detach`, `Probe`.
- Split note: none -- the "submit a port-owned object record through the game's walk" mechanism is generic but every table address, stride and model offset is KF2.

#### patches/remaster/TextureKeys.cs  (308 lines)
- Bucket: C (high) -- "key a face on the content of the art it draws, normalised to the upload" generalizes; the fluid-slot address and KF2 tpage/CLUT formats are the game's.
- Does: turns a face's `(tpage, clut, UV rect)` into a `TexKey` (upstream `TextureTile.Hash` of the art), widening the rect to the image the game uploaded; special-cases the eight scrolling-texture fluid slots by hashing their source image in RAM; supplies `TextureResolver.Scroll`.
- Hooks: none; static helpers called by `Surfaces`, `Faces`, `TextureCensus`, `TextureResolver.Scroll`.
- Data: fluid slots `0x80192D58` stride `0x18` (byte +0 live, +4 phase, +6 dest rect, +0x10 source image); reads `Runtime.Gpu.Vram`, `VramTracker.Clock/Generation`; tpage bits for bpp/page; CLUT addressing.
- Structs: `TexKey(ulong Index, ulong Clut)` with `AnyClut`, `ToString` `texture:INDEX[:CLUT]`; `_memo` by face-rect place + VRAM clock; `_slots`/`_fluid` caches; `Entry`.
- Control flow: memoised until VRAM under the rect changes (clock/generation); fluid keys reset per area settle; `CheckSlots` verifies the scroll phase/shift assumption.
- Overlays: none.
- Env / settings keys: none; `TextureKeys.Census` toggled by `TextureCensus`.
- RecompOne deps: `TextureTile` (`Hash`, `Describe`), `TextureResolver` (`ToUpload`, `Scroll`, `KeyOnUpload/KeyOnFaceRect`), `TextureDumper`, `VramTracker`, `GteDepth.Fluid/FluidN`, `Runtime.Gpu.Vram`, `IMemory`; patch `0073` (replacement textures on the port's path), `0053`/`FluidSmoothing` for scroll.
- Mod-visible: `public static class TextureKeys`; `Lookups`, `Hashed`, `NoUpload`, `Census`, `FluidLookups`, `ScrollLookups`, `Of`, `ScrollLookup`, `CheckSlots`, `OfPacket`; `TexKey.TryParse`/`AnyClut`.
- Split note: `TexKey` and the upload-normalisation idea are portable; the fluid-slot address, tpage/CLUT decoding and `GteDepth.Fluid` coupling are KF2.

#### patches/remaster/TextureCensus.cs  (261 lines)
- Bucket: C (high) -- "census which texture keys an area draws and which a pack covers" generalizes; bucketing by KF2 area and the fluid slots are the game's.
- Does: measurement (`KF2_TEXCENSUS=1`): every replacement-resolver lookup bucketed by settled area (keys, art, places, overlap, uploads, dirty), a line every 5 s, and the `textures` shell verb to dump/save (`dump/GAME/census/area-N.json`).
- Hooks: `Event.AddListener<VSyncEvent>`; assigns `TextureResolver.Observer`, `VramTracker.Uploaded`, `TextureKeys.Census`.
- Data: none direct; observes resolver/`VramTracker`; area from `Identity`.
- Structs: `Seen` (tpage/clut/bpp/rect/hits/dirty/replaced), `AreaCensus` (keys by `(index,clut)`, places by tpage+clut+rect, uploads); summary JSON.
- Control flow: on from boot; `Frame` polls `Identity` only while the remaster/editor are off and reports every 5 s; `textures` verb toggles `TextureResolver.Enabled`, `TextureDumper`.
- Overlays: none.
- Env / settings keys: `KF2_TEXCENSUS`, `KF2_TEXKEY=face|triangle`.
- RecompOne deps: `TextureResolver` (`Observer`, `KeyOnUpload`, `KeyOnFaceRect`, `ScrollHits/Misses`, `Invalidate`, `Enabled`), `VramTracker`, `TextureDumper`, `TextureTile`, `TileRect`, `AssetReplacerManager.GameId`, `VSyncEvent`, `GteDepth.RepFilterSets`.
- Mod-visible: `public static class TextureCensus`; `On`, `Configure`, `Install`, `SetOn`, `Verb`.
- Split note: the census mechanism is portable; the KF2 seam is texture detection (fluid slots via `TextureKeys`) and the area bucketing.

#### patches/remaster/LightCensus.cs  (251 lines)
- Bucket: C (high) -- a RAM read/write attribution census generalizes; the owner addresses and record ranges are KF2.
- Does: measurement (`KF2_LIGHTCENSUS=1`): hooks the 13 stages and helper routines to attribute every read/write of the 80 light records (source and destination) and the load flag to the innermost owner, reporting every 5 s.
- Hooks: `pre`/`post` pairs on 24 `game` addresses (stages 1-13, `func_8002CAF4`, `func_8002CBD4`, `func_80043388`, `func_80015DD4`, `func_8002DC78`, `func_80033FBC`, `func_80031C94`, `func_800331B4`, `func_80032400`, `func_80031D5C`, `func_8002E0FC`), via `HookAttach.OnOverlayLoad` + `HookManager.AddPre/AddPost` and `Commit`; each has a closure-free nested `O{i}` class pair.
- Data: `Flag=0x801930EC`, `Dst=0x801930F0` stride `0x68`, `Src=0x800679A0` stride `0x2C`, 80 records; uses `Runtime.RamLog` read/write stamps, sets `RamLogger.TrackReads/TrackWrites`.
- Structs: `Tally` (source/dest/flag read+written counts and per-record/field bitsets), `Owners` table.
- Control flow: owner entry/exit nests on a stack; `Harvest` credits bytes stamped since the last event to the innermost owner; `Report` every 5 s.
- Overlays: `game`.
- Env / settings keys: `KF2_LIGHTCENSUS=1`.
- RecompOne deps: `RamLogger`/`Runtime.RamLog`, `HookAttach`, `SymbolRegistry`, `HookManager`, `ModInfo`, `CpuContext`, `IMemory`.
- Mod-visible: `public static class LightCensus`; `Configure`, `Install`.
- Split note: the attribution mechanism is generic; owner addresses, record ranges/strides and the "only stage 1 writes them" question are KF2.

#### patches/remaster/TileRewrites.cs  (207 lines)
- Bucket: C (high) -- "census which tile halves the game rewrites" generalizes; the block address/layout and `+2` exception are KF2.
- Does: measurement: four times a second compares the live tile block against the block as loaded, adjusted by the level edits' held bits, and records which halves the game itself rewrote (all bytes but `+2`); persists per area fingerprint to `dump/GAME/census/rewrites.json`.
- Hooks: none; called from `Host.Frame` (`TileRewrites.Poll`).
- Data: `Identity.TileBase`/`TileBytes`/`HalfBytes`, `TileField.Collision.Offset` (the `+2` exception), `Identity.Baseline`, `Level.Holding`/`Generation`.
- Structs: `_census` fingerprint -> (area, halves {(x,z,half) -> bitmask of moved offsets}); `Compared` per-word mask excluding each half's `+2`.
- Control flow: only while settled with a loader baseline; expected block recomputed when settle or held-generation moves; file saved 5 s after a change.
- Overlays: none.
- Env / settings keys: none.
- RecompOne deps: `Runtime.Mem`/`IMemory`, `Identity`, `TileField`, `Level`, `AssetReplacerManager.GameId`.
- Mod-visible: `public static class TileRewrites`; `Polls`, `Found`, `LastError`, `FilePath`, `Count`, `Poll`, `Rewritten`, `List`, `Reset`, `DescribeBytes`.
- Split note: generic census; addresses and the `+2` footprint exception are KF2.

#### patches/remaster/Compat.cs  (204 lines)
- Bucket: C (high) -- a per-disc area-fingerprint compatibility report generalizes; the fingerprint/keys are KF2.
- Does: records which area fingerprints this install has settled on as loaded (with the last resolution counts per pack) in `dump/GAME/census/areas.json`, and reports per area whether the pack's documents match, differ or are unseen.
- Hooks: none; called from `Host.Frame` (`Compat.Poll`) and the `pack report`/Pack tab.
- Data: none direct; `Identity.FingerprintText`/`FromLoad`/`Settles`/`Area`, `Pack.AreaDocs()`, feature counters.
- Structs: `Seen` (area + `Resolved` JsonObject).
- Control flow: only for a settled, loader-seen area; resolution counts taken 1 s after a settle then every 2 s; file keyed by fingerprint.
- Overlays: none.
- Env / settings keys: none.
- RecompOne deps: `AssetReplacerManager.Instance.GameId`; KF2 `Identity`, `Pack`, features.
- Mod-visible: `public static class Compat`; `LastError`, `FilePath`, `Poll`, `Report`.
- Split note: portable report logic; the fingerprint meaning and document kinds are KF2.

#### patches/remaster/EditorCamera.cs  (150 lines)
- Bucket: C (high) -- a free fly camera over `Stage13.ViewOverride` generalizes; the angle units (0x1000/turn), -Y up and view-matrix read are KF2.
- Does: the editor's free camera: right-mouse look, WASD/QE fly, Shift/Ctrl speed, wheel speed; drives `Stage13.ViewOverride` (and `HideArmOnOverride`) each frame; returns to the player on close.
- Hooks: none; `Poll` from `Host.Frame`, `Input` from the editor panel; feeds `Stage13.ViewOverride` which is a runtime field.
- Data: `Stage13.Handed`/`ViewOverride`/`HideArmOnOverride`, `Camera.Read`, `Lights.ReadView` matrix for axes, `HotkeyGate.Typing`; angle limits `PitchLimit 1000`; speeds 256..65536, `LookRate 4` (angle units/pixel).
- Structs: `Camera` (`X,Y,Z,Pitch,Yaw,Roll`), `(Right, Down, Forward)` tuple.
- Control flow: `Poll` closes it with the editor; `SetOn` seeds from `Stage13.Handed` or `Camera.Read`; `Turn`/`Move` publish immediately.
- Overlays: none.
- Env / settings keys: none.
- RecompOne deps: `Stage13`, `Camera`, `HostWindow`, `HotkeyGate`, `Mouse`, `ImGui`; KF2 `Lights.ReadView`.
- Mod-visible: `public static class EditorCamera`; `On`, `Looking`, `Speed`, `MinSpeed`, `MaxSpeed`, `LookRate`, `Current`, `SetOn`, `Place`, `ToPlayer`, `Axes`, `Move`, `Turn`, `Poll`, `Input`.
- Split note: the camera mechanism is portable; view-matrix layout, angle units and -Y up are KF2.

#### patches/remaster/FaceProbe.cs  (201 lines)
- Bucket: C (high) -- a measurement of subdivider mapping and pick-vs-GPU generalizes; the mesh-table addresses, command codes and readback sizes are KF2.
- Does: measurement (`KF2_FACE_PROBE=1`): gives every tile face one of ids 4-7 by a hash of its key; checks the subdivider's output against its source face (count, command, texture, vertex containment, corner coverage); on each new SSR readback, compares the pick's nearest face with the GPU's id per pixel.
- Hooks: none; called from `Faces`/`PolyAssembler` and `TextureCensus`/`ScreenReflections` readbacks.
- Data: mesh table `0x8018E19C`, subdivider output mesh headers; `ScreenReflections.MapSerial/LastInfo/LastW/LastH`; `Faces.Last`; game pixel mapping 320x240 and widescreen margin.
- Structs: counters only; `Faces.Tri` read.
- Control flow: `Enter` labels each face when recording; `Subdivided` walks source/output meshes; `FrameDone` analyses the latest readback once per `MapSerial` and prints a summary line.
- Overlays: `game`.
- Env / settings keys: `KF2_FACE_PROBE=1`.
- RecompOne deps: `ScreenReflections`, `PSMemory`, `PolyAssembler.TileMaterial`, `TileWalk.CurrentRecord`; KF2 `Faces`.
- Mod-visible: public `On`, `Enter`, `Subdivided`, `FrameDone`.
- Split note: portable measurement shape; all addresses/stride/commands are KF2.

#### patches/remaster/SaveCheck.cs  (104 lines)
- Bucket: D (high) -- runs this game's save packer with its exact addresses and buffer.
- Does: measurement (`savecheck` shell verb): runs `func_80049A88` three times on the same card buffer -- as-is, tile+shape blocks inverted, and player X inverted as the control -- and reports whether the save carries any of the tile block.
- Hooks: none; called from `AgentServer.DoSaveCheck`.
- Data: card buffer pointer `0x8006E98C`, `CardBytes 0x4000`, `Packed 0x400`; tiles `Identity.TileBase`/`TileBytes`, shapes `ShapeBase`/`ShapeBytes`; player X `0x801994EC`.
- Structs: none.
- Control flow: snapshots CPU + buffer, runs the packer in each variant, restores everything; `verdict` distinguishes block-moved vs control-moved.
- Overlays: `game` (`func_80049A88` direct call via alias `Recompiled.KingsField2_game`).
- Env / settings keys: none.
- RecompOne deps: `CpuContext.Snapshot/Restore`, `IMemory`, direct `KingsField2.func_80049A88`; KF2 `Identity`.
- Mod-visible: `public static class SaveCheck`; `Usage`, `Run`.
- Split note: none -- addresses and the packer are this disc's.

#### patches/remaster/Snap.cs  (97 lines)
- Bucket: B (high) -- reads back the presented picture, hashes, diffs and writes a PNG; no KF2 knowledge beyond using the runtime present hook.
- Does: `snap` verb: reads the presented target (runtime `0069`) after N presents, SHA-256-hashes the first 16 hex digits, counts changed pixels and bounding rect against the last snap, writes a PNG.
- Hooks: none of its own; uses `PresentSnap.Request` (runtime).
- Data: none (RGBA frame bytes only); `_lastBuffer` to pick the same display buffer a paused frame took.
- Structs: none.
- Control flow: reply is sent from the presenting thread (deferred), waits 2 presents by default; `buffer Y|any` selects.
- Overlays: none.
- Env / settings keys: none.
- RecompOne deps: `PresentSnap`, `PngWriter`, `SHA256`, `AssetReplacerManager` (unused import).
- Mod-visible: `public static class Snap`; `Run`, `Usage`.
- Split note: none -- fully generic (only the "two display buffers differ a few pixels" note is port-specific behaviour).

#### patches/remaster/Snap wiring + shell registration (outside this dir, for context)
- `Program.cs` (lines 777-804) reads `KF2_REMASTER`, `KF2_REMASTER_PACK`, `KF2_REMASTER_PROBE`, `KF2_REMASTER_LIGHTS`, `KF2_REMASTER_ATMOS`, `KF2_REMASTER_LEVEL`, `KF2_REMASTER_PROPS`, the seven `KF2_REMASTER_SHADOW*`, `KF2_TEXCENSUS`, `KF2_TEXKEY`, `KF2_LIGHTCENSUS`, and calls `Host.Install()`, `TextureCensus.Install()`, `LightCensus.Install()`.
- `patches/AgentServer.cs` routes `edit/select/set/pack/remaster/light/textures/atmos/level/camera/prop` to `Remaster.Shell.Run`, and `savecheck`/`snap` to `SaveCheck`/`Snap`; `Remaster.Shell.Verbs`/`Help`/`Snap.Usage`/`SaveCheck.Usage` are merged into `help`.
- `patches/settings/RemasterPage.cs` owns settings `kf2.remaster.on` (Video ▸ Enhancements ▸ Remaster packs) and the Remaster packs page (working pack, layers, per-area apply/refuse reasons). Not under `patches/remaster/`, listed only because the dir's code depends on it.

### Summary

Counts (36 `.cs` files under `patches/remaster/`, grouped into 20 entries -- 15
single files plus 3 families and the two already-covered lines):
- Bucket A (RecompOne fork): 0 files.
- Bucket B (game-agnostic infra): 1 file -- `Snap.cs`. (The generic halves of
  `Pack`/`Shell`/`Editor` plumbing are noted, but each file as a whole is C.)
- Bucket C (generalizable mechanism, KF2 implementation): 31 files -- `Host`,
  `Identity`, `Pack` family (5), `Editor` family (9), `Shell` family (4), `Faces`,
  `Surfaces`, `Lights`, `Atmosphere`, `TextureKeys`, `TextureCensus`, `LightCensus`,
  `TileRewrites`, `Compat`, `EditorCamera`, `FaceProbe`.
- Bucket D (KF2-only): 4 files -- `TileField`, `Level`, `Props`, `SaveCheck` (plus
  `Pack.GameId`/the key schemas embedded in the C entries).

Most important observations:
1. **The pack format is upstream's asset pack plus a `remaster/` directory**, and only `pack.json`'s `game.id` and the area fingerprint gate make it KF2. All document schemas, path layouts and merge/undo logic are portable.
2. **The single hardest KF2 tie is the area fingerprint**: an FNV-1a 64 of the 64,000-byte tile block (`0x801C8484`, less each half's `+2`) plus the first `0x600` bytes of `0x801D8484`, taken from `func_80017244`'s source at `0x80017244` so the game's own rewrites do not move it. A sibling game needs its own block layout and loader hook.
3. **Identity keys are all KF2 coordinates**: `TileKey(area,x,z,lower|upper)` over an 80x80x10 block, `ModelKey(area,kind,model)`, light records 0-79 at `0x801930F0` stride `0x68`, object models def+`0x100`. The seam is clean: `Identity`/`TileField` are the only files that must be rewritten for another game.
4. **The remaster is layered on upstream's texture pack system** (`AssetPack`, `TextureTile.Hash`, `TextureResolver`, `TextureDumper`), and Phase 4's `TextureKeys` wires scrolling textures into it by hashing the fluid slots' source RAM images (`0x80192D58`, stride `0x18`).
5. **Only three files hook KF2 code by address**: `Identity` (`0x80017244`), `Lights` (the three overlay `DrawOTag` entries `0x80016078`/`0x80060818`/`0x80013D80`), `Atmosphere` (`0x8002C944` stage 1, `0x80060870` `PutDrawEnv`). Everything else hangs off `Host.Frame` at `VSyncEvent` or off those passes.
6. **`TileField` is the abstraction that makes level editing safe**: edits name fields/bitmasks, never bytes, so `+2`'s moving footprint bit and the game's run-time rewrites are structurally protected; `Level` additionally holds each written byte and reverts only if the owned bits still read as written.
7. **Props are the only added geometry**, and they are KF2-only: a port-owned object record above 2 MB (`PrimBuffer.PropScratch`, requires `KF2_PRIMBUF=1`) copied from a live object of the same model and submitted through `ModelWalk.Ordinary`, so they inherit culling/light/fog/reflections with no collision and no save.
8. **`SaveCheck` proves the tile block never reaches a save**, which is what lets level edits ship at all; it is pure KF2 (card buffer `0x8006E98C`, packer `func_80049A88`).
9. **The measurement features** (`FaceProbe`, `TextureCensus`, `LightCensus`, `TileRewrites`, `Compat`) are the reusable part: each is a generic census/attribution shape with all KF2 addresses in its constants. `Snap` is the only fully game-agnostic tool in the directory.
10. **`Host`'s feature list is the extension point**: `[Surfaces, Lights, Atmosphere, Level, Props]`; a sibling game reimplements the five features + `Identity`/`TileField` and keeps `Pack`, `Editor`, `Shell`, `Faces`, `Surfaces`' resolution logic and all the measurement tooling.

## patches/settings/, mods/, config/
Scope: every `.cs` under `patches/settings/`; everything under `mods/` except
`mods/.cache`; `config/kf2.json`; and a summary of `config/funcmaps/`.
Read-only analysis per `scratch/sharing/BRIEF.md`.

---

### 1. Settings framework (shared machinery)

#### patches/settings/PatchSettings.cs  (385 lines)
- Bucket: B (low) — the settings plumbing (`IPatchPage`, `PatchSettings`,
  `Localization.Merge` of section names) is game-agnostic, but it hard-wires this
  game's page list and the `kf2.*` key prefix.
- Does: registers/orders/draws per-patch settings pages inside the runtime's own
  settings sections, persists them through `Runtime.View` + `SaveView`.
- Hooks: none (UI only; listens for `RuntimeReadyEvent`).
- Data: none.
- Structs: none.
- Control flow: waits for `RuntimeReadyEvent` before `RegisterUi`, because
  `ConfigManager.Load()` runs in `HostWindow.Initialize` *after* `Program.cs`.
- Overlays: none.
- Env / settings keys: owns the store; keys are `kf2.<patch>.<name>` in
  `interface.ini`. Env var used to gate/comparison only if pages read one.
- RecompOne deps: `0031` (settings child padding shape referenced in InputSection),
  `0013` (`SettingsRegistry.DrawSlot` — slot extension), `0027` (commit-fail
  passive). Runtime types: `RecompOne.Runtime.Host.Window` (`ISettingsSection`,
  `SettingsRegistry`, `Localization`, `HostWindow`), `RecompOne.Runtime.Runtime`
  (`View`, `SaveView` as `Rt`), `RecompOne.Runtime.Events.Event`.
- Mod-visible: `public static class PatchSettings` with `Register(string,
  IPatchPage)`, `RegisterSlot(string, IPatchPage)`, `Get(string,bool|int|float)`,
  `Set(string,bool|int|float)`, `Note(string)`, `Install()`;
  `public interface IPatchPage { string Id; string Title; int Order; void Draw(); }`.
  `GameplaySection` and `InputSection` are `public sealed`.
- Split note: the seam between generic plumbing and KF2 is exactly
  `PatchSettings.Install`'s hard-coded page list + `SectionNames` (Video/Gameplay
  translations). Move the page list to the game repo; keep `PatchSettings`,
  `IPatchPage`, `Note`, Get/Set generic.

#### patches/settings/GameplaySection.cs  (41 lines)
- Bucket: B (medium) — its only game-tied content is the localisation key it
  names; structurally it is just "an empty section of the port's own".
- Does: adds the **Gameplay** settings tab (id `gameplay`, order 7) with no body.
- Hooks: none.
- Data / Structs / Control flow / Overlays: none.
- Env / settings keys: localisation key `settings.gameplay` (en "Gameplay",
  pt-BR "Jogabilidade", es-419 "Jugabilidad", supplied by PatchSettings).
- RecompOne deps: none beyond `ISettingsSection` / `SettingsRegistry.Register`.
- Mod-visible: `public sealed class GameplaySection : ISettingsSection`.
- Split note: fully reusable as "a game-defined section"; the title key is the
  only thing to re-key.

#### patches/settings/InputSection.cs  (213 lines)
- Bucket: C (medium) — the mechanism (replace the runtime's Input pane so port
  pages fit above the fold) generalizes; the deployed instance is tied to this
  game's keyboard/pad/mouse pages and its own button semantics.
- Does: **replaces** the runtime's `input` section with three tabs (Keyboard /
  Gamepad / Mouse) holding the port's input pages and a copied binding table.
- Hooks: none. Reads `HostWindow.IsPadConnected(0)`.
- Data: references the game's action-mask table `0x8006E568`-`0x8006E5D0` and
  `func_8002957C` only in comments (the binding table is UI, not addresses).
- Structs: none.
- Control flow: registered on `RuntimeReadyEvent`, after `HostWindow.Load` has
  registered the runtime's five sections; must not `Unregister("input")` first.
- Overlays: none.
- Env / settings keys: localisation keys `settings.input.keyboard`,
  `settings.input.gamepad`, `settings.input.no_gamepad`,
  `settings.input.reset_defaults` (runtime's own); `"Mouse"` tab is English only.
- RecompOne deps: `0031` (child padding shape), `0032` (`HostWindow` reach for
  internal `InputManager`). Runtime types: `RecompOne.Runtime.Config.ConfigManager`
  (`Game.Keys`, `Game.Pad`, `SaveGame`), `RecompOne.Runtime.Host.Window`
  (`ISettingsSection`, `Localization`, `HostWindow`), `RecompOne.Runtime.Host`
  (`GamepadBindings`).
- Mod-visible: `public sealed class InputSection : ISettingsSection`.
- Split note: the wrapper is generic; the page list (`KeyLayoutPage`, `AnalogPage`,
  `MapButtonPage`, `MousePage`) and the binding table's action strings are KF2.

#### patches/settings/BindingTable.cs  (267 lines)
- Bucket: C (high) — copied generic table, but the middle "action" column is this
  game's measured button defaults and the pad-1-only choice is this game's.
- Does: draws the 16-row pad/key binding table with a King's Field action column.
- Hooks: none.
- Data: action strings derived from action-mask table `0x8006E568`-`0x8006E5D0`
  and `func_8002957C`; pad-2 unused because `BiosB.PadRead` packs pad 2 in the
  high half at `0x80199554`.
- Structs: `(Label, GetKey, SetKey, GetPad, SetPad, Action)` row tuples, 16 rows.
- Control flow: none.
- Overlays: none.
- Env / settings keys: localisation keys `settings.input.button`,
  `settings.input.keyboard`, `settings.input.gamepad`,
  `settings.input.press_key`, `settings.input.press_button`,
  `settings.input.press_button_add`, `settings.input.unbound`. The middle column
  header "In King's Field" is hard-coded English.
- RecompOne deps: `0032` (`HostWindow.IsKeyDown`, `GetFirstPressedPadButton`).
  Runtime types: `RecompOne.Runtime.Config` (`KeyBindings`, `GamepadBindings`),
  `HostWindow`, `Silk.NET.Input.Key`.
- Mod-visible: `static class BindingTable` is **internal** (no access modifier →
  internal); only `Draw` is reachable inside the assembly.
- Split note: copy `BindingTable.Draw`/`PadLabel`/capture generically; the `_rows`
  action column and the pad-2 removal are the KF2 seam.

---

### 2. Per-feature settings pages (`patches/settings/*Page.cs`)

Each entry: section it registers under, `Id`, `Title` (heading), `Order`, exact
settings keys read/written, localisation keys merged, env comparison.

#### AmbientOcclusionPage.cs (44)
- Bucket: C (high) — generalizes (a quality slider), reads `AmbientOcclusion` KF2 state.
- Does: SSAO quality slider Off/Low/Medium/High under Video ▸ Enhancements.
- Hooks/Data/Structs/Control flow/Overlays: none (UI only).
- Env/settings: registers `display`; reads `kf2.ao.on`, `kf2.ao.quality` via `AmbientOcclusion.OnKey`/`QualityKey`; writes both. Env comparisons `KF2_AO_*`.
- RecompOne deps: none extra. Mod-visible: `AmbientOcclusion` static (public keys).
- Split note: the slider is generic; `AmbientOcclusion` is the KF2 patch.

#### AnisotropicPage.cs (51)
- Bucket: C/B (medium) — generic texture filter slider over KF2 `Anisotropic`.
- Does: Texture filtering slider Off/Trilinear/2x/4x/8x/16x under Enhancements.
- Env/settings: `display`; reads/writes `kf2.aniso.level` (`Anisotropic.LevelKey`) and `kf2.mipmaps.on` (`MipKey`); `KF2_MIPMAPS`.
- Mod-visible: `Anisotropic.Level`, `Mipmaps`, `SetLevel`, `SetMipmaps`.

#### AudioPage.cs (48)
- Bucket: C (high) — reverb/interp/positional are this SPU patch's knobs.
- Does: Interpolation, Reverb, Positional audio combos under Audio.
- Env/settings: `audio`; writes `kf2.audio.interp`, `kf2.audio.reverb` (`AudioQuality`), `kf2.audio.positional` (`PositionalAudio.ModeKey`).
- Runtime types: `RecompOne.Runtime.Spu`, `SpuInterpolation`, `SpuReverbMode`.
- Mod-visible: `AudioQuality`, `PositionalAudio` statics.

#### AutoReloadPage.cs (75)
- Bucket: D (high) — reload-the-last-save-on-death is a KF2 port rule.
- Does: checkbox + save-slot combo under Gameplay (empty title).
- Env/settings: `gameplay`; reads/writes `kf2.autoreload.enabled`, `kf2.autoreload.slot`. Env `KF2_AUTORELOAD_DELAY`.
- Mod-visible: `AutoReload.Enabled/Slot/SetEnabled/SetSlot`.

#### DistancePage.cs (81)
- Bucket: C (high) — render distance is a KF2 cull extension.
- Does: Render distance + Enhancement distance sliders under Video ▸ Experimental.
- Localisation keys added (all 3 langs): `kf2.renderdistance.label/.tooltip/.game`,
  `kf2.enhancedistance.label/.tooltip/.all`.
- Env/settings: `display`; `kf2.renderdistance` (`RenderDistance.Key`), `kf2.enhancedistance` (`EnhancementDistance.Key`).

#### EvenFogPage.cs (58)
- Bucket: C (high) — EvenFog is a KF2 tile-walk patch.
- Does: Even fog and lighting checkbox, dimmed without Fast geometry.
- Localisation keys (3 langs): `kf2.evenfog.label/.tooltip/.needsfast`.
- Env/settings: `display`; `kf2.evenfog.on` (`EvenFog.OnKey`); reads `PolyAssembler.FastGeometry`.

#### FastGeometryPage.cs (74)
- Bucket: C (high) — Fast geometry and GpuWorld are KF2 renderer patches.
- Does: "Fast geometry" checkbox + indented "GPU geometry" (`GpuWorld`) checkbox under Video ▸ Frame pacing.
- Localisation keys (3 langs): `kf2.gpuworld.label/.tooltip/.blocked`.
- Env/settings: `display`; `kf2.fastgeometry.on` (`PolyAssembler.FastGeometryKey`), `kf2.gpuworld.on` (`GpuWorld.OnKey`). Env `KF2_POLYASM_*`, `KF2_GTE_*`, `KF2_GPUWORLD`.

#### FramePacingPage.cs (122)
- Bucket: C (high) — frame pacing is generic in idea but implemented against KF2's frame gate.
- Does: frame-rate slider (fixed pins 20..240 + Unlimited) under Video ▸ Frame pacing, with measured/live notes.
- Env/settings: `display`; reads `FramePacing.TargetFps`, writes `kf2.framepacing.fps` (`FpsKey`). Env `KF2_FPS`, `KF2_TICKRATE`.
- Mod-visible: `FramePacing.TargetFps/Enabled/Measured/Extrapolating/LogicHz/SetTargetFps`.

#### FrameSmoothingPage.cs (101)
- Bucket: C (high) — one switch harmonising five KF2 smoothing patches.
- Does: "Smooth motion between game ticks" checkbox, dimmed unless `FramePacing.Extrapolating`; writes all five patches+keys.
- Env/settings: `display`; writes `kf2.smoothing.on`/`.pos` (`FrameSmoothing.OnKey`/`PosKey`), `kf2.smoothing.objects` (`ObjectSmoothing.OnKey`), `kf2.smoothing.anim` (`AnimSmoothing.OnKey`), `kf2.smoothing.fluid` (`FluidSmoothing.OnKey`). Env `KF2_SMOOTH*`.

#### GearComparePage.cs (30)
- Bucket: D (high) — comparing gear on the KF2 equip prompt.
- Does: "Compare gear" checkbox under Gameplay (empty title).
- Env/settings: `gameplay`; `kf2.gearcompare.enabled` (`GearCompare.OnKey`).

#### KeyLayoutPage.cs (78)
- Bucket: D (high) — the port's WASD King's Field layout.
- Does: two buttons ("King's Field layout" / "RecompOne layout") at head of Input ▸ Keyboard.
- Env/settings: held by `InputSection` (not registered separately); uses `KeyLayout.IsApplied/Apply/ApplyStock`; `KeyLayout.AppliedKey` = `kf2.keys.layout`.
- Mod-visible: `KeyLayout`.

#### MapButtonPage.cs (56)
- Bucket: D (high) — the map's pad button is a KF2 feature; SDL indices are `Map.Pad*`.
- Does: combo choosing the pad button that opens the full-screen map, under Input ▸ Gamepad.
- Env/settings: held by `InputSection`; `kf2.map.pad.button` (`Map.PadButtonKey`); reads `Map.Enabled`.

#### MapPage.cs (100)
- Bucket: D (high) — the map/fog feature is KF2-only.
- Does: one "Map" combo Off / Whole area / Fill in as you go under Gameplay (empty title); harmonises `Map` + `MapFog`.
- Env/settings: `gameplay`; writes `kf2.map.on` (`Map.OnKey`), `kf2.map.fog` (`MapFog.OnKey`). Env `KF2_MAP`, `KF2_MAP_FOG`, `KF2_MAP_FOG_LOS`.

#### MouseLeadPage.cs (36)
- Bucket: C (high) — mouse look leading the tick is a port input mechanism tied to KF2's 20 Hz tick.
- Does: "Instant mouse look" checkbox under Gameplay (empty title), dimmed when `!Mouse.Enabled`.
- Env/settings: `gameplay`; `kf2.mouse.lead` (`Mouse.LeadKey`).

#### MousePage.cs (143)
- Bucket: C/D (high) — mouse look is a port input mechanism; buttons map to PSX pad buttons.
- Does: Mouse look checkbox, sensitivities, invert, button→pad combos, capture-key combo, menu-pointer checkbox, live notes, under Input ▸ Mouse.
- Env/settings: held by `InputSection`; writes `kf2.mouse.on`, `.turn`, `.look`, `.inverty`, `.left`, `.right`, `.middle`, `.capturekey`, `.lead` (all `Mouse.*Key`); reads/writes `kf2.menumouse.on` (`MenuMouse.OnKey`).
- Runtime types: `RecompOne.Runtime.Host.HostWindow` (`MouseAvailable`).

#### PerPixelLightingPage.cs (27)
- Bucket: C (high) — per-pixel lighting is a KF2 packet-record feature.
- Does: Per-pixel lighting checkbox under Enhancements.
- Env/settings: `display`; `kf2.perpixel.on` (`PerPixelLighting.OnKey`).

#### PerspectivePage.cs (36)
- Bucket: C (high) — perspective correction is a KF2 vertex-map mechanism (RecompOne `0009`,`0012`).
- Does: Perspective-correct textures checkbox under Enhancements.
- Env/settings: `display`; `kf2.perspective.on` (`Perspective.OnKey`); probe `KF2_PERSPECTIVE_PROBE`.

#### ReflectionsPage.cs (86)
- Bucket: C (high) — water reflections/murk/waves read KF2 water geometry.
- Does: Planar reflections, Murky water, Water waves checkboxes under Enhancements.
- Localisation keys (3 langs): `kf2.ssr.planar.label/.tooltip`, `kf2.murk.label/.tooltip`, `kf2.waves.label/.tooltip`.
- Env/settings: `display`; `kf2.ssr.planar` (`PlanarWalk.OnKey`), `kf2.murk.on` (`Murk.OnKey`), `kf2.waves.on` (`Waves.OnKey`). Env `KF2_SSR`, `KF2_RETAINED`, `KF2_REFLECT_REACH`, `KF2_MURK_TILT`.

#### RemasterPage.cs (114) — two pages
- Bucket: C/D (high) — remaster packs / authored data, but the editor/pack mechanism could generalize.
- Does: `RemasterPage` (id `remaster`) "Remaster packs" checkbox; `RemasterPacksPage`
  (id `remaster.packs`, title "Remaster packs") lists pack layers, working pack,
  level edits checkbox, per-area fingerprints, Reload/Open editor.
- Localisation keys (3 langs): `kf2.remaster.label/.tooltip`.
- Env/settings: `display` (both); `kf2.remaster.on` (`Host.OnKey`), `kf2.remaster.level` (`Level.OnKey`), dynamic `kf2.remaster.layer.<id>` (`Pack.LayerKey`).
- Runtime types: `Localization`; reads `Kf2.Remaster` `Pack`, `Surfaces`, `Level`, `Identity`, `Editor`.
- Mod-visible: none new (settings only).

#### ShadingPage.cs (85)
- Bucket: C (high) — dither/true-colour is a KF2 15-bit framebuffer concern.
- Does: one "Shading" slider Dither / None / Smooth (24-bit), harmonising `NoDither`+`TrueColor`.
- Env/settings: `display`; writes `kf2.nodither.on` (`NoDither.OnKey`), `kf2.truecolor.on` (`TrueColor.OnKey`). Env `KF2_NODITHER`, `KF2_TRUECOLOR`. RecompOne `0021`.

#### SubpixelPage.cs (35)
- Bucket: C (high) — sub-pixel positioning is a KF2 GTE/assembler mechanism (RecompOne `0010`).
- Does: Sub-pixel vertex positioning checkbox under Enhancements.
- Env/settings: `display`; `kf2.subpixel.on` (`Subpixel.OnKey`); probe `KF2_SUBPIXEL_PROBE`.

#### WidescreenPage.cs (73)
- Bucket: C (high) — widescreen widens KF2's cull cone and tints.
- Does: "Aspect" combo registered as a **slot** `display.render_scale`, drawn bare (RecompOne `0013`).
- Env/settings: slot `display.render_scale`; `kf2.widescreen.aspect` (`Widescreen.AspectKey`); env `KF2_WIDESCREEN`, `KF2_WIDESCREEN_CULL`, `KF2_WIDESCREEN_EFFECTS`, `KF2_WIDESCREEN_HUD`, `KF2_WIDESCREEN_PROBE`.
- Runtime types: `SettingsRegistry.DrawSlot` via `PatchSettings.RegisterSlot`.

#### ZBufferPage.cs (53)
- Bucket: C (high) — Z-buffer depth comes from KF2 assembler packet records (RecompOne `0050`,`0051`).
- Does: Z-buffer checkbox under Enhancements.
- Localisation keys (3 langs): `kf2.zbuffer.label/.tooltip`.
- Env/settings: `display`; `kf2.zbuffer.on` (`ZBuffer.OnKey`); env `KF2_ZBUFFER_BIAS`, `KF2_ZBUFFER_SLOPE`.

#### AnalogPage.cs (139)
- Bucket: C (high) — twin-stick control writes KF2 per-frame velocities.
- Does: twin-stick checkboxes, sensitivities, fine-tuning tree (deadzones, curves,
  acceleration, inversions), probe controls, live stick readout, under Input ▸ Gamepad.
- Env/settings: held by `InputSection`; writes `kf2.analog.on`, `.look`, `.move`,
  `.instantstop`, `.accel`, `.accelmax`, `.acceltime`, `.turn`, `.pitch`, `.movesens`,
  `.lookdeadzone`, `.movedeadzone`, `.lookcurve`, `.movecurve`, `.invertpitch`,
  `.invertturn`, `.invertstrafe`, `.invertforward`, and `kf2.analog.probe`,
  `kf2.analog.probeinterval` (`AnalogProbe`).
- Mod-visible: `Analog` static (`Enabled`, `AnalogLook`, `AnalogMove`, sensitivities, `Sticks`, `SetEnabled`...).

---

### 3. Mods (`mods/`, excluding `.cache`)

`ModCompiler` compiles mods with no implicit usings; they reference the main game
assembly (`patches/`, `Program.cs`) and `tools/RecompOne` at load time. The
**mod-visible surface** below is what a runtime mod can name and must not break.

#### mods/framestats (removed)
- Removed from the repo; it reported fps, vblanks/frame and VSync-calls/frame
  (`KF2_FRAMESTATS`). Frame rate and tick rate are `KF2_FPS_PROBE=1`'s.

#### mods/kf2debug/mod.json (7) + DebugMod.cs (205)
- Bucket: D (high) — the whole debug mod is KF2 addresses and routines.
- Does: `public sealed class DebugMod : IMod` — loads persisted settings and env,
  registers the dockable `DebugPanel`, polls hotkeys at stage-3 post.
- Hooks: `[PostHook("game", Address = 0x8002A550)]` (end of main-loop stage 3).
- Data: none directly (delegates to GameState/Noclip/Cheats/...).
- Structs: none directly.
- Control flow: assumes GAME.EXE main loop and that stage 3 = `func_8002A550` is
  the safe point to run recompiled routines; panel registered on UI thread from
  first `VSyncEvent`.
- Overlays: `game`.
- Env / settings keys: env `KF2_DEBUG_NOCLIP`, `KF2_DEBUG_GODMODE`,
  `KF2_DEBUG_INFINITEMP`, `KF2_DEBUG_HOTKEYS`, `KF2_DEBUG_NOCLIP_SPEED`,
  `KF2_DEBUG_SPEED`; settings via `Runtime.View`/`SaveView`:
  `kf2.debug.noclip.speed`, `kf2.debug.noclip.fast`, `kf2.debug.noclip.inverty`,
  `kf2.debug.noclip.invertstrafe`, `kf2.debug.speed.multiplier`, `kf2.debug.hotkeys`.
- RecompOne deps: `IMod`, `Event`/`VSyncEvent`, `CpuContext`, `IMemory`,
  `HostWindow`, `PanelManager`, `PanelManager.Panels`, `IPanel`, `MenuRegistry.Menu("menu.debug").Panel<DebugPanel>().End()`, `ToastNotifications`, `Host`/`Host.Window`.
- Mod-visible (references it uses): `RecompOne.Runtime.Runtime.View/Mem/Cpu/SaveView`; `Recompiled.KingsField2_game` (`func_80024CAC`, `func_800244CC`, `func_80048178`, `func_8002C3A8`); `Kf2.AreaWarp` (`TryRun`, `Areas`, `CutArea`), `Kf2.MouseIndicator.Suppressed`.
- Split note: every address and routine is KF2; the modding API it consumes is RecompOne.

#### mods/kf2debug/GameState.cs (232)
- Bucket: D (high) — a by-address map of GAME.EXE's player state.
- Does: `internal static class GameState` — typed signed accessors over player
  position/view/stats with 12-bit angle conventions.
- Data (all GAME.EXE, in code; addr: name/type/units):
  - `0x801994EC PosX` s32, `0x801994F0 PosY` s32 (height), `0x801994F4 PosZ` s32.
  - `0x8019950C Pitch` s16 (12-bit? base), `0x8019950E Yaw` s16 12-bit
    (0x1000/turn, increases left), `0x80199510 Roll` s16.
  - `0x80199504/06/08 ViewPitch/ViewYaw/ViewRoll` s16 (composed = base+A+B+C).
  - `0x8019953E StrafeVel`, `0x80199540 FwdVel`, `0x80199542 WalkMag`,
    `0x80199544 TurnVel`, `0x80199546 PitchVel` (s16 velocities).
  - `0x80199558 MoveSpeed` u32 (walk 0xC8), `0x8019955C TurnRate` u32
    (0x1C moving / 0x23 still).
  - `0x8019954E FallVel` s16, `0x8019953C SurfaceId` u16, `0x80199554 Pad` u16 (active HIGH).
  - buf2 @ `0x80199414`: `Exp` u32 (cap 999999), `ExpNext` u32, `Level` u8,
    `MaxHp` 0x80199426, `Hp` 0x80199428, `MaxMp` 0x8019942A, `Mp` 0x8019942C (u16),
    `BaseStr` 0x80199438, `BaseMag` 0x8019943A, `StrPower` 0x8019943C,
    `MagPower` 0x8019943E, `Gold` 0x80199440 u32, 8 offense 0x80199444-0x52,
    9 defense 0x80199456-0x66, 5 conditions 0x80199468-0x74 (s16).
  - `0x801994E1 State` u8 (0x11 dead), `0x8019951A DeathFrames` u16,
    `0x8017E060 Area` u8, `0x8006E5D4 CurrentSlot` u8.
  - Constants: `AngleMask 0xFFF`, `AngleFull 0x1000`, `PitchLimit 0x2BC`.
- Control flow: `IsInGame` = MaxHp != 0; `IsDead` = State == 0x11.
- Overlays: `game`.
- RecompOne deps: `IMemory`.
- Mod-visible: internal only (mod-private).
- Split note: KF2-only; the signed-accessor/12-bit-angle helpers are the reusable idea.

#### mods/kf2debug/Attributes.cs (336)
- Bucket: D (high) — buf2 character/ratings editor.
- Does: `internal static class Attributes` — memory editor for character stats and
  the 19-word derived ratings, plus buttons queuing the game's own level-up/recalc.
- Hooks: `[PostHook("game", 0x8002A550)]`, `[PostHook("game", 0x800244CC)]`.
- Data: GameState addresses above; routine `func_80024CAC` (level-up, EXP clamp),
  `func_800244CC` (rebuild ratings); `func_80049A88` packs the stats into the save.
- Structs: `Field { Name, Address, Width(1/2/4), Signed, Tip }`.
- Control flow: queued work runs at stage-3 post; recompiled routines reached via
  `KingsField2.func_80024CAC(c, m)` with `CpuContext.Snapshot/Restore`; A0 set manually.
- Overlays: `game`.
- Env / settings keys: none.
- RecompOne deps: `CpuContext`, `IMemory`, `Modding`; `Recompiled.KingsField2_game`.
- Mod-visible: internal only.
- Split note: every address KF2; `Kf2.AreaWarp`-style direct-call pattern reusable.

#### mods/kf2debug/Cheats.cs (284)
- Bucket: D (high) — invincibility/infinite MP/speed/peaceful all KF2 routines.
- Does: three HP routines clamped, death latch refused, behaviour picker handed a
  fake far distance, speed words scaled.
- Hooks: `[PreHook("game", 0x80024FE0)]` (take damage), `[PreHook("game", 0x80024F90)]`
  and `[PreHook("game", 0x8002A3DC)]` (HP add/adjust), `[PreHook("game", 0x8003A300)]`
  (behaviour picker a0), `[PreHook("game", 0x8002A264)]` (death latch, returns false),
  `[PostHook("game", 0x8002A550)]` (HP/MP restore), `[PreHook("game", 0x80028DB8)]`
  (scale MoveSpeed/TurnRate before look/walk).
- Data: `func_80024FE0`, `func_80024F90`, `func_8002A3DC`, `func_8002A264` (latch,
  state 0x11), `func_8003A300`/`func_8003A574`/`func_80039E40` (creature AI at
  `desc+0x38`, ranges `rule+0x10/+0x12`), `func_800154E4` distance, `func_80028DB8`
  look; `FarAway = 0x20000`.
- Structs: creature record `rec+0x2C/+0x34` position, `rec+0x9` activation byte;
  rule range u16 at `+0x10/+0x12`.
- Control flow: stage 3 ordering; comment claims scale must sit between stage-3
  write and `func_80028DB8` read.
- Overlays: `game`.
- Env / settings keys: none (flag fields set by panel/env via DebugMod).
- RecompOne deps: `CpuContext`, `IMemory`, `Modding`.
- Mod-visible: internal only (and `Cheats` fields read by Noclip/DebugPanel).
- Split note: KF2-only.

#### mods/kf2debug/Hotkeys.cs (155)
- Bucket: C (low)/D — input polling is generic; key choices and SDL indices are KF2-ish.
- Does: `internal static class Hotkeys` — edge-detected keyboard/pad hotkeys.
- Hooks: none of its own (polled from DebugMod's stage-3 post).
- Data/Structs/Control flow/Overlays: none.
- Env / settings keys: none.
- RecompOne deps: `HostWindow.IsKeyDown`, `HostWindow.IsPadButtonDown`,
  `ToastNotifications.ShowText`; `Silk.NET.Input.Key`.
- Mod-visible surface used: `HostWindow` (public), `ToastNotifications`.
- Split note: generic; keybind/indices KF2.

#### mods/kf2debug/Items.cs (435)
- Bucket: D (high) — inventory/name table/ give routine all KF2.
- Does: `internal static class Items` — reads/edits 120-byte inventory, decodes item
  names live from the font-index table, queues the game's give routine.
- Hooks: `[PostHook("game", 0x8002A550)]`.
- Data: `InvBase 0x8009B52C` (120 bytes), `NameTable 0x80065B24` stride `0x18`,
  `ModulePtr 0x8017E068` (slot `+0x18` = give hook), `MaxHeld 99`,
  `SavedCount 0x70`; routines `func_80019444` (list builder), `func_80048178`
  (give), `func_80048124` (consume), `func_80049A88`/`func_8004A040` (save pack),
  `func_80033F08` (full chime), shop arrays `0x80066844/0x80066A24/0x80066A9C`.
- Structs: item name records (24-byte, `0x00`=A, `0x7F`=space, `0xFF` terminator);
  category runs between placeholder `00 FF` records.
- Control flow: queued gives run at stage-3 post; module-hook presence checked.
- Overlays: `game`.
- Env / settings keys: env `KF2_DEBUG_ITEMS_PROBE` (0/1/2).
- RecompOne deps: `CpuContext`, `IMemory`, `Modding`; `Recompiled.KingsField2_game`
  (`func_80048178`).
- Mod-visible: internal only.
- Split note: KF2-only; the "decode names live from the running image" idea reusable.

#### mods/kf2debug/Noclip.cs (578)
- Bucket: D (high) — flight integrates KF2 position/angle state and game routines.
- Does: `internal static class Noclip` — free-flight, cinematic lag, snap-to-floor,
  return-to-entry.
- Hooks: `[PreHook("game", 0x8002A264)]` (death latch refused), `[PostHook("game",
  0x8002A550)]` (integrate and write position).
- Data: GameState position/angles; collision `func_8002C330`/`func_8002C700`
  (radius `0x320`), floor query `func_8002C3A8`, heading `func_8005EB08` (rsin,
  1.12 fixed) / `func_8005EC10` (rcos), walk `func_800290D4`, gravity
  `func_80028560`, `func_800284BC`/`func_80023ECC` death checks, spawn warps
  `0x8002AFBC` (-9344), `0x80025C44` (-12800); `PlayerRadius 0x320`,
  `PlayerHeight 0x6A4`.
- Structs: stage map (stage 2 `func_80037C0C`, stage 3 `func_8002A550`,
  stage 4 `func_80040348`, stage 7 `func_8001689C`, stage 13 `func_800342D8`).
- Control flow: assumes stage-3 post is last writer of the position triple; area 7
  (`fdat23`) scripted sequences suspend stage 3.
- Overlays: `game`; mentions `fdat23` (area 7).
- Env / settings keys: none.
- RecompOne deps: `CpuContext`, `IMemory`, `Hardware.Controller`
  (`LeftX/LeftY/State/Up/Down/L1/R1`), `Modding`; `Recompiled.KingsField2_game`
  (`func_8002C3A8`).
- Mod-visible surface used: `RecompOne.Runtime.Runtime.Cpu/Mem`; `Kf2.MouseIndicator.Suppressed`.
- Split note: KF2-only; "integrate own position, write after stage 3" idea reusable.

#### mods/kf2debug/Warp.cs (240)
- Bucket: D (high) — area warp calls the KF2 area-entry routine.
- Does: `internal static class Warp` — 4 position bookmarks (persisted) and area warp.
- Hooks: `[PostHook("game", 0x8002A550)]` (run queued area warp).
- Data: `func_80024154` (area-entry wrapper), `func_8001689C` (area loader),
  `func_80017818` (CD wait/VSync); GameState Area/position/angles.
- Structs: `Bookmark { Set, X,Y,Z,Pitch,Yaw,Roll }`, `SlotCount 4`.
- Control flow: queued from panel (inside VSync/Present), run at stage-3 post.
- Overlays: `game`; loads fdat modules via CD read arming the overlay.
- Env / settings keys: `kf2.debug.bookmark{0..3}.{set,x,y,z,pitch,yaw,roll}`.
- RecompOne deps: `CpuContext`, `IMemory`, `Modding`; `Runtime.View/SaveView`.
- Mod-visible surface used: `Kf2.AreaWarp` (`TryRun`, `Areas`, `CutArea`), `Noclip`.
- Split note: KF2-only; bookmark persistence pattern generic.

#### mods/kf2debug/DebugPanel.cs (633)
- Bucket: D (high) — the panel reads/writes KF2 state throughout.
- Does: `internal sealed class DebugPanel : IPanel` — dockable tabs Cheats,
  Attributes, Items, Warp, State, Keys.
- Hooks: none of its own; draws all state.
- Data: reads all GameState addresses; `func_800244CC`, `func_80048178`.
- Structs: uses `Attributes.Field`.
- Control flow: assumes draw inside VSync/Present; `Kf2.AreaWarp` area range 0-7.
- Overlays: `game` (mentions `fdat32` cut area).
- Env / settings keys: none direct.
- RecompOne deps: `IPanel` (`Name`, `TitleKey`, `IsOpen`, `Draw`), `Host.Window`
  (`IPanel.Title()` extension, `PanelManager`, `MenuRegistry`), `IMemory`,
  `Runtime.Mem`.
- Mod-visible surface used: `IPanel`, `PanelManager`, `MenuRegistry.Menu`,
  `RecompOne.Runtime.Runtime.Mem`.
- Split note: KF2-only.

---

### 4. The mod-visible surface a runtime mod references

Derived from the `using`s and symbols the two mods name; this is the API that must
not break.

**From `tools/RecompOne` (`RecompOne.Runtime`):**
- `Context.CpuContext` — `A0..A3`, `V0`, `SP`, `At`, `Snapshot()`, `Restore()`.
- `Memory.IMemory` — `ReadU8/ReadU16/ReadU32`, `WriteU8/WriteU16/WriteU32`.
- `Modding.IMod` — `OnLoad()`, `OnUnload()`, `DrawSettings()`.
- `Modding` hook attributes — `[PreHook(overlay, Address = n)]`, `[PostHook(...)]`
  (`PreHook` method returning `bool` skips the original).
- `Events.Event` — `AddListener<T>`, `RemoveListener`; `VSyncEvent`,
  `RuntimeReadyEvent`.
- `Host.Window` — `HostWindow` (`IsKeyDown`, `IsPadButtonDown`, `IsPadConnected`,
  `GetFirstPressedPadButton`, `MouseAvailable`), `IPanel`, `PanelManager`,
  `MenuRegistry`, `ToastNotifications`, `ISettingsSection`, `SettingsRegistry`,
  `SettingsRegistry.DrawSlot`, `SettingsRegistry.Sections`, `Localization`.
- `Host.GamepadBindings`, `Host.KeyBindings`, `Host.DrawSlot`.
- `Config.ConfigManager` (`Game.Keys/Pad`, `SaveGame`), `Config.KeyBindings`.
- `RecompOne.Runtime.Runtime` — `View`, `SaveView()`, `Mem`, `Cpu`.
- `Spu`, `SpuInterpolation`, `SpuReverbMode`.
- `Hardware.Controller` — `LeftX/LeftY/State/Up/Down/L1/R1`.

**From the game assembly (`patches/`, `Program.cs`):**
- `Recompiled.KingsField2_game` — recompiled GAME.EXE functions called directly:
  `func_80024CAC`, `func_800244CC`, `func_80048178`, `func_8002C3A8`.
- `Kf2.AreaWarp` — `TryRun`, `Areas`, `CutArea`.
- `Kf2.MouseIndicator.Suppressed`.
- `Kf2.Settings.PatchSettings` / `IPatchPage` / `GameplaySection` / `InputSection`.
- Feature statics referenced by settings pages and mods: `FramePacing`,
  `FrameSmoothing`, `ObjectSmoothing`, `AnimSmoothing`, `FluidSmoothing`,
  `Perspective`, `Subpixel`, `NoDither`, `TrueColor`, `AmbientOcclusion`,
  `Anisotropic`, `PerPixelLighting`, `EvenFog`, `ZBuffer`, `RenderDistance`,
  `EnhancementDistance`, `PlanarWalk`, `Murk`, `Waves`, `Reflections`,
  `ZBuffer`, `GpuWorld`, `PolyAssembler`, `Widescreen`, `AudioQuality`,
  `PositionalAudio`, `Analog`, `AnalogProbe`, `Mouse`, `MenuMouse`, `KeyLayout`,
  `Map`, `MapFog`, `MapButton`, `GearCompare`, `AutoReload`, `Host` (remaster),
  `Level`, `Pack`, `Surfaces`, `Identity`, `Editor`, `Kf2.Mods.Debug.*`.

---

### 5. `config/kf2.json` (512 lines)

- Bucket: D (high) — bound to SLUS-00158's disc layout and executables.
- Does: the recompiler config: game id, overlays, function maps, SDK `patches[]`,
  debug flags.
- Overlays (`base` all `0x80011000` for the three exes; `skip` 2048 for `.EXE`):
  - `open` = `OPEN.EXE`, funcmap `open.json`, entry `0x80013DB8`.
  - `game` = `GAME.EXE`, funcmap `game.json`, entry `0x8004A628`.
  - `end` = `END.EXE`, funcmap `end.json`, entry `0x80011CB4`.
  - `fdat02/05/08/11/14/17/20/23` = `CD/COM/FDAT.T` at `base 0x8019F07C`,
    `skip 0`, offsets 98304, 200704, 307200, 409600, 512000, 616448, 720896,
    821248; sizes 4096/8192/4096/6144/8192/6144/6144/8192.
  - `fdat32` = `base 0x80193B38`, offset 925696, size 6144 (cut area, never loaded).
- `funcMap` (main) = `funcmaps/main.json`; boot stub at `0x80010000`, 2 KiB.
- `patches[]` (63 `replace` entries): SDK binding by address per overlay —
  LibEtc.VSync ×3, LibCd.CdInit/CdControl/CdControlF/CdControlB/CdSync/CdRead/
  CdReady/CdGetSector/CdReadSync, LibGpu.DrawOTag/DrawSync/PutDrawEnv/PutDispEnv,
  LibCdStream.StSetRing/StClearRing/StUnSetRing/StSetStream/StFreeRing/StGetNext,
  LibApi.DMACallback.
- Diagnostics: `debug`, `addressComments`, `disasmComments` all false.
- `stubs[]` and `ignored[]` empty.
- Env/settings keys: none (build-time).
- Split note: the overlay/funcmap/patch structure is game-agnostic; every address
  in it is KF2.

---

### 6. `config/funcmaps/` summary (names, counts, overlays)

Each file is `{ "functions": [ {address,name,size} ], "labels": [] }`. All entries
are `func_800xxxxx` / `func_8019xxxx` (except named SDK merges); `size` mandatory.
`labels` empty in every file.

| file | functions | overlay / base |
|---|---|---|
| `main.json` | 10 | boot stub `0x80010000` |
| `open.json` | 517 | `OPEN.EXE` @ `0x80011000` |
| `game.json` | 1102 | `GAME.EXE` @ `0x80011000` |
| `end.json` | 473 | `END.EXE` @ `0x80011000` |
| `fdat02.json` | 10 | FDAT code module @ `0x8019F07C` |
| `fdat05.json` | 13 | FDAT code module @ `0x8019F07C` |
| `fdat08.json` | 12 | FDAT code module @ `0x8019F07C` |
| `fdat11.json` | 16 | FDAT code module @ `0x8019F07C` |
| `fdat14.json` | 19 | FDAT code module @ `0x8019F07C` |
| `fdat17.json` | 13 | FDAT code module @ `0x8019F07C` |
| `fdat20.json` | 13 | FDAT code module @ `0x8019F07C` |
| `fdat23.json` | 14 | FDAT code module @ `0x8019F07C` |
| `fdat32.json` | 13 | FDAT code module @ `0x80193B38` (cut) |
**Total functions: 2225.** Overlays covered: boot stub + the three executables +
nine FDAT modules. All names are address-derived; no per-area SDK renames.

---

### Summary

**Counts (by entry):**
- A (RecompOne fork): 0 — this inventory covered no `tools/RecompOne` files.
- B (game-agnostic infra): ~5 — `PatchSettings.cs`, `GameplaySection.cs`, the
  settings plumbing, `FrameStats` mechanism, parts of `InputSection`/`BindingTable`.
- C (mechanism generalizes, KF2 implementation): ~20 — every `ReflectionsPage`,
  `ShadingPage`, `FramePacingPage`, `MousePage`, `AnalogPage`, `Noclip`, etc.
- D (KF2-only): ~15 — `AutoReloadPage`, `MapPage`, `GearComparePage`,
  `KeyLayoutPage`, all of `mods/kf2debug/`, `config/kf2.json`.

**Most important observations:**
1. **Settings keys are a two-file contract.** Each page reads/writes keys owned by
   a patch (`kf2.<feature>.*` in `patches/*.cs`); pages never hard-code addresses.
   A sibling game gets the framework for free if it re-owns those constants.
2. **`PatchSettings` is the only generic settings plumbing and it hard-codes this
   game's page list** in `Install` and `SectionNames`; that list is the seam.
3. **`InputSection` *replaces* the runtime's `input` section by id**; the port
   owns a runtime-registered pane in the vendored checkout, paid for by a
   one-line abandon. Siblings must decide this per game.
4. **`BindingTable` is a verbatim copy of an internal RecompOne type**, with one
   duplicated constant (`PadLabel` SDL indices) that is silent on a pin bump.
5. **All of `mods/kf2debug` is a by-address overlay on GAME.EXE**: ~40 RAM
   addresses and ~25 recompiled routines, all in `GameState`/`Cheats`/`Items`/
   `Noclip`/`Attributes`/`Warp`.
6. **The mod-visible API surface is RecompOne's, not the port's**, except four
   `Recompiled.KingsField2_game.func_*` direct calls, `Kf2.AreaWarp`,
   `Kf2.MouseIndicator`, and `Kf2.Settings.PatchSettings`.
7. **`config/kf2.json`'s 63 SDK `patches[]` are all absolute KF2 addresses**, one
   set per overlay; the overlay machinery itself is generic.
8. **`fdat32` is declared but unreachable** (based at `0x80193B38`); it is dead
   evidence, and any sibling should not copy the pattern unless it has the same.
9. **Localisation keys the port adds are KF2-specific and all three languages are
   required**: `settings.gameplay`, `settings.display`, plus the `kf2.*` keys in
   `DistancePage`, `EvenFogPage`, `FastGeometryPage`, `ReflectionsPage`,
   `RemasterPage`, `ZBufferPage`.
10. **The `mods/.cache` directory is compiled output and was excluded**; the two
    real mods were `framestats` (since removed) and `kf2debug` (entirely KF2).

## scripts/
Scope: every source file under `scripts/` except `__pycache__`. Read-only analysis.
Buckets per `scratch/sharing/BRIEF.md`: **B** game-agnostic once parameterised,
**C** generalising mechanism with KF2 implementation, **D** KF2-only.

Line counts from the checked-out files. Addresses in the code's own naming.

---

#### scripts/add_call_targets.py  (151 lines)
- Bucket: **B** (confidence: high) -- all game identity is CLI arguments; only docstring examples name KF2 files.
- Does: harvests `jal`/tail-`j` targets from a PS-X EXE on a PS1 disc and merges them into a funcmap JSON, recomputing sizes.
- Hooks: none.
- Data: none hardcoded; addresses come from the executable header and the funcmap.
- Structs: funcmap entry `{address, name, size}` with size mandatory.
- Control flow: sweeps code for opcodes `J`=2, `JAL`=3; only harvests a site inside a known function.
- Overlays: none in code; takes an EXE name.
- Env / settings keys: none.
- RecompOne deps: none; writes the funcmaps the recompiler consumes.
- Mod-visible: none.
- Imports from scripts: `inspect_disc` (`open_disc`, `resolve_image`), `extract_file` (`find_entry`, `EXE_HEADER_SIZE`, `EXE_MAGIC`).
- Hardcodes specific to this game/repo: none in code. Docstring examples only: `disc/KingsField2.cue`, `OPEN.EXE`, `config/funcmaps/open.json`. Generic PS-X constants: `PS-X EXE` magic, `0x800` header size.
- How to parameterise: already parameterised (image, exe, funcmap). Only the doc examples need no change; could optionally read the overlay→EXE→funcmap pairing from `config/kf2.json`.

---

#### scripts/audio_spectrum.py  (76 lines)
- Bucket: **B** (confidence: high) -- pure WAV DSP; the only KF2 mention is a docstring reference to `KF2_AUDIO_DUMP`.
- Does: per WAV, prints RMS in dBFS and the share of energy above 11.025 kHz and 16 kHz in dB.
- Hooks: none.
- Data: none.
- Structs: none.
- Control flow: none.
- Overlays: none.
- Env / settings keys: none read; docstring names `KF2_AUDIO_DUMP` as a producer of its inputs.
- RecompOne deps: none.
- Mod-visible: none.
- Imports from scripts: none (stdlib only: `wave`, `array`, `math`).
- Hardcodes specific to this game/repo: `KF2_AUDIO_DUMP` (docstring only). Audio constants 11025 Hz and 16000 Hz are generic (PS1 SPU reverb runs at 22.05 kHz).
- How to parameterise: already CLI (`files`, `--skip`, `--seconds`). No change needed; could take the two crossover frequencies as flags if reused beyond KF2.

---

#### scripts/callgraph.py  (268 lines)
- Bucket: **B** (confidence: high) -- generic "parse generated PS1 C# into a call graph"; KF2 identity is only the class prefix and paths.
- Does: parses `generated/*.cs` into a `Graph` of functions, call edges, indirect-call marks, and per-function global read/write/ref address tables via a tiny `lui`/`addiu` dataflow.
- Hooks: none (static text analysis).
- Data: no fixed addresses; recovers addresses from emitted `lui`/`addiu`; only records the PS1 RAM window `0x80000000..0x80800000`.
- Structs: `Func` dataclass (`name`, `overlay`, line `start`/`end`, `calls`, `indirect`, `writes`/`reads`/`refs` address→width, `has_backedge`, `address`). Width map `U8=1,U16=2,U32=4`.
- Control flow: assumes the recompiler's emitted shape: `public static void NAME(CpuContext c, IMemory m)`, `KingsField2_*.NAME(c, m)`, `Dispatcher.Call(c, m, ...)`, PGXP hook suffix stripped.
- Overlays: derived from filename; skips `Entry`, `Stubs`; a redefinition keeps the first.
- Env / settings keys: none.
- RecompOne deps: patch `0035` (PGXP hook suffix `RecompOne.Runtime.Pgxp.*`); the generated code shape.
- Mod-visible: `Graph`, `Func`, `Graph.by_addr/subtree/subtree_blocked/reaches_indirect/writers/readers/touching/writes_in_subtree` (Python, not the C# runtime).
- Imports from scripts: none.
- Hardcodes specific to this game/repo: `REPO/generated` (`generated/`); class prefix regex `KingsField2(?:_\w+)?` (assembly/class name `KingsField2_game`, `_open`, `_end`); `func_XXXXXXXX` name scheme; `config/funcmaps/<overlay>.json`; PS1 RAM range `0x80000000`–`0x80800000`; `Dispatcher.Call(c, m,`; `RecompOne.Runtime.Pgxp.`; skip set `Entry`/`Stubs`.
- How to parameterise: generated dir, class prefix, and funcmap dir from `config/kf2.json` or a per-game JSON; RAM window is generic PS1. Under a sibling game only the class prefix/paths change.

---

#### scripts/check_gate.py  (216 lines)
- Bucket: **C** (confidence: high) -- the rule "a gated stage must not draw" generalises, but it hardcodes KF2 SDK entry addresses and three recorded exceptions.
- Does: static check that every address in `patches/FramePacing.cs`'s `DefaultGate` has a call subtree reaching no submit/present SDK call, printing the shortest path and known exceptions; `--stages` prints the inverse.
- Hooks: none; reads FramePacing's gate list and `generated/game.cs`.
- Data (hardcoded): `SUBMIT` = `0x80060818` DrawOTag, `0x80060870` PutDrawEnv, `0x80060990` PutDispEnv; `PRESENT` = `0x8005FCC8` VSync (`SDK_NAMES` = union). `KNOWN` exceptions: `0x80037C0C` (stage 2, reason names `func_80037B5C` transition fade and area modules `func_80047000`/`func_80048208`/`func_8004831C`), `0x8002A550` (stage 3, names `func_80029CBC` item use, `func_80037B5C`), `0x80046A60` (stage 5, names `func_80043388` modal loop, shops `func_8001D544`, message box `func_80035B48`, stage 13).
- Structs: none direct; relies on the call graph.
- Control flow: assumes "stage" subtrees, modal loops, and the stage-13 self-call structure of the KF2 main loop.
- Overlays: `game` (via `kf2model`).
- Env / settings keys: none.
- RecompOne deps: none direct; `patches/FramePacing.cs` `DefaultGate`.
- Mod-visible: none.
- Imports from scripts: `kf2model` as `model`.
- Split note: the generic rule and BFS path reporting vs the KF2 address tables. The `SUBMIT`/`PRESENT` addresses already exist in `config/kf2.json` `patches[]` and could come from `kf2model.draw_addresses()` rather than being restated; `KNOWN` could be per-game JSON keyed by stage.
- How to parameterise: read draw/present addresses from `config/kf2.json`; move the exception list to per-game data.

---

#### scripts/extract_file.py  (100 lines)
- Bucket: **B** (confidence: high) -- PS-X EXE/ISO extraction, all inputs CLI.
- Does: extracts a named file from a PS1 disc image and, for a PS-X EXE, prints the header fields a linear sweep needs (`-base` etc.).
- Hooks: none.
- Data: none.
- Structs: PS-X EXE header fields at offsets `0x10` (pc, gp, t_addr, t_size), `0x20` (d_addr/d_size/b_addr/b_size), `0x30` (s_addr/s_size).
- Control flow: none.
- Overlays: none.
- Env / settings keys: none.
- RecompOne deps: none.
- Mod-visible: none.
- Imports from scripts: `inspect_disc` (`open_disc`, `parse_dir_record`, `resolve_image`, `walk`, `PVD_LBA`).
- Hardcodes specific to this game/repo: `EXE_MAGIC = b"PS-X EXE"`, `EXE_HEADER_SIZE = 0x800` (generic PS-X). Docstring examples only: `disc/KingsField2.cue`, `GAME.EXE`. Also exports `find_entry` used by others.
- How to parameterise: already CLI (image, name, `-o`, `--header-only`). No change needed.

---

#### scripts/find_writers.py  (154 lines)
- Bucket: **C** (confidence: medium) -- generic "which code writes this word, at what clock" reporter, but it is built entirely on the KF2-specific `kf2model`.
- Does: given addresses (or `--stage N`, `--modal`, `--audit`), names the functions that write them and classifies each writer's rate (tick vs render vs modal loop).
- Hooks: none; static over the call graph.
- Data: no hardcoded addresses; CLI addresses and `kf2model` classifications. Docstring examples `8006E5CC`, `80199554`, `--stage 2`.
- Structs: none direct.
- Control flow: relies on `kf2model`'s thirteen-stage list, `FramePacing.DefaultGate`, draw-address set and computed modal loops.
- Overlays: `game` (via model).
- Env / settings keys: none.
- RecompOne deps: none direct.
- Mod-visible: none.
- Imports from scripts: `kf2model` as `model`.
- Split note: the reporter/classifier half is B; the KF2 stage/gate/modal model it imports is D. If `kf2model` becomes per-game config, this becomes B.
- How to parameterise: parameterise `kf2model` (main-loop function, loop label, gate source, overlay); then this needs no changes.

---

#### scripts/inspect_disc.py  (174 lines)
- Bucket: **B** (confidence: high) -- generic PS1/ISO9660 inspection; no game data.
- Does: detects a PS1 disc layout, prints `SYSTEM.CNF` and walks the ISO9660 filesystem with LBAs and sizes (the data that drives `config/kf2.json`).
- Hooks: none.
- Data: none.
- Structs: ISO9660 directory record (`lba` at +2, `size` at +10, flags at +25, name-len at +32, name at +33); PVD root record at PVD offset 156.
- Control flow: none.
- Overlays: none.
- Env / settings keys: none.
- RecompOne deps: none.
- Mod-visible: none.
- Imports from scripts: none.
- Hardcodes specific to this game/repo: none in code. Docstring examples only: `disc/KingsField2.cue`, `disc/KingsField2.bin`. Generic constants: `USER_DATA=2048`, layouts `(2352,24),(2048,0),(2336,8)`, `PVD_LBA=16`. Exports `Disc`, `open_disc`, `resolve_image`, `parse_dir_record`, `walk`, `PVD_LBA`.
- How to parameterise: already CLI (image path). No change needed.

---

#### scripts/kf2model.py  (193 lines)
- Bucket: **D** (confidence: high) -- hardwires the KF2 main loop, its label, the KF2 patch file and the `game` overlay.
- Does: derives the port's model of a frame from the tree -- the thirteen main-loop stages, which are gated, what counts as drawing, and the computed modal loops -- and classifies a function as tick- or render-clocked.
- Hooks: none (reads source text); parses `patches/FramePacing.cs` `DefaultGate`.
- Data: `MAIN_LOOP = "func_8001369C"`; `LOOP_LABEL = "L80013918"`; draw targets read from `config/kf2.json` patches (`LibGpu.DrawOTag`, `LibEtc.VSync`, `LibGpu.PutDrawEnv`, `LibGpu.PutDispEnv`).
- Structs: none.
- Control flow: assumes the frame is a flat call list after `L80013918` in `func_8001369C`, ends at the back-branch, excludes main loop + stages from modal-loop detection.
- Overlays: `game` (`generated/game.cs`).
- Env / settings keys: none.
- RecompOne deps: `patches/FramePacing.cs`; generated `game.cs`; `config/kf2.json`.
- Mod-visible: none (Python).
- Imports from scripts: `callgraph`.
- Hardcodes specific to this game/repo: `func_8001369C` (main loop), `L80013918` (loop label), `generated/game.cs`, `patches/FramePacing.cs`, `config/kf2.json`, overlay `"game"`, `func_XXXXXXXX`/`LXXXXXXXX` naming, SDK target names above.
- How to parameterise: a per-game JSON with `{mainLoop, loopLabel, overlay, generatedClass, gateSource, drawTargets, patchConfig}`; the rest of `classify()` is general.

---

#### scripts/kf2run.py  (165 lines)
- Bucket: **B** (confidence: high) -- generic process launcher + KF2_SHELL client; KF2 identity is the executable name, project path, cue path and env prefix.
- Does: launches the port detached, speaks one-line JSON to the `KF2_SHELL` command channel, and harvests its stdout; helpers `state`/`press`/`hold`/`wait_in_game`/`stop`.
- Hooks: none.
- Data: none.
- Structs: `Run` dataclass (`log`, `env`); reads `state` fields `inGame`, `MaxHp!=0` convention noted.
- Control flow: assumes `KF2_SHELL` is listening on connect; `wait_in_game` polls `state` then settles.
- Overlays: none.
- Env / settings keys (read/set): `KF2_SHELL=1`, `KF2_AUTOSTART=2` (defaults set in `launch`); caller-supplied `KF2_*`.
- RecompOne deps: none.
- Mod-visible: none (Python client); the `KF2_SHELL` protocol is the runtime module's.
- Imports from scripts: none.
- Hardcodes specific to this game/repo: `REPO`; `EXE_MATCH = "bin/Release/net10.0/Deception"`; `PORT = 27900`; project `Deception.csproj`; cue `disc/KingsField2.cue`; env `KF2_SHELL`, `KF2_AUTOSTART`; shell timeout 6.0 s; `pgrep -f`.
- How to parameterise: executable match, port, project path, cue path and env prefix as constructor/CLI/module constants from a per-game JSON; `KF2_SHELL` prefix already a convention.

---

#### scripts/light_probe.c  (415 lines)
- Bucket: **C** (confidence: medium) -- a reusable headless shader-vs-formula harness, but it encodes the KF2 remaster shader's uniforms, constants and formulas.
- Does: runs the port's real `PrimFs.frag` over untextured/true-colour and textured strips, sweeping known lighting/fog/material varyings, and diffs every pixel against the same formula reimplemented in C; exit code = number of failing cases.
- Hooks: none (drives the shipped shader binary).
- Data (hardcoded): `BK[3]={1920,1920,1920}` back colour, `LCM[9]` (2662,2662,3328 ×3) light colour matrix, `H=200`, `CX=128`, `CY=1`, `DEPTH=2000`, `NL=3` lights; material table layout `256*3*4` floats (rows: base, emissive, specular); texel `{66,165,214}`; VRAM 1024×512.
- Structs: material table 256 ids × 3 rows × vec4; light list `uLightPos/uLightCol/uLightDir` each `NL*4` (dir .w carries radius or `-2-id` outer cosine); shadow cubemap 64×64 depth.
- Control flow: assumes pass numbering (0-23) matching the shader's features; assumes GlCore sampler-unit assignment (shadow maps on units 12-15; material table unit 6; VRAM unit 7).
- Overlays: none (stands alone over a shader file).
- Env / settings keys: none; takes the frag shader path `argv[1]`. (Docstring names `KF2_PERPIXEL_PROBE=2` as the in-play counterpart.)
- RecompOne deps: patches `0048` (per-pixel lighting), `0071` (materials/authored lights), `0074` (fog), `0077` (shadows), `0083` (enhancement distance).
- Mod-visible: none.
- Imports from scripts: none (EGL/GL only).
- Hardcodes specific to this game/repo: the entire uniform contract of `PrimFs.frag` — `uTrueColor`, `uSetMask`, `uCheckMask`, `uOpaqueDepth`, `uShadow0..3`, `uLightShadow`, `uLightBk`, `uLcmR/G/B`, `uVram`, `uTexWindow`, `uScale`, `uLightCentre`, `uLightH`, `uLightPos/Col/Dir`, `uLightN`, `uEmitOn`, `uMatTable`, `uTestLight/uLit0/uLit1/uFog0/uFog1/uTestDepth/uTestMat/uTestTex`, `uPlainZ`, `uAtmosOn/uAtmosColour/uAtmosShape/uAtmosSkip`, `uShadowToWorld/uShadowSize/uShadowOffset/uShadowBias/uShadowSoft`; and the remaster formulas/constants above.
- How to parameterise: the EGL/FBO/readback harness is B; the formula and uniform block should be a per-game/per-shader C file or generated from the shader's uniform list. Split note: harness (top ~110 lines) vs KF2 formula/uniform/constant block.

---

#### scripts/match_overlays.py  (271 lines)
- Bucket: **C** (confidence: medium) -- the overlay-delta mechanism is generic to any multi-overlay PSY-Q game, but it hardcodes the three KF2 executables and 17 `open`-link libgpu addresses.
- Does: finds the same function in the other two executables by a relocation-insensitive instruction normal form (masking `j`/`jal`/`lui`/immediates), and can re-derive the libgpu map as config patches.
- Hooks: none.
- Data (hardcoded): `OVERLAYS = [("open","OPEN.EXE"),("game","GAME.EXE"),("end","END.EXE")]`; `LIBGPU` 17 addresses in `open` (ResetGraph `0x80015A8C`, SetGraphDebug `0x80015D28`, GetGraphType? `0x80015D8C`, GetGraphDebug `0x80015D9C`, SetDispMask `0x80015DC4`, DrawSync `0x80015E04`, ClearImage `0x80015E34`, LoadImage `0x80015E84`, StoreImage `0x80015EC0`, MoveImage `0x80015EFC`, ClearOTag `0x80015F68`, ClearOTagR `0x80015FBC`, DrawPrim `0x80015FF4`, DrawOTag `0x80016078`, PutDrawEnv `0x800160D0`, GetDrawEnv `0x80016190`, PutDispEnv `0x800161F0`); `HLE = {DrawOTag, DrawSync, PutDrawEnv, PutDispEnv}`.
- Structs: none; function lengths from funcmap or first `jr $ra`.
- Control flow: assumes a constant per-translation-unit delta between links; masks immediates by opcode set.
- Overlays: `open`/`game`/`end`, bases read from the EXE header (`text_addr - 0x800`).
- Env / settings keys: none.
- RecompOne deps: `config/kf2.json` `patches[]` output shape (`RecompOne.Runtime.Sdk.LibGpu.*`, `mode: replace`).
- Mod-visible: none.
- Imports from scripts: `extract_file.find_entry`, `inspect_disc` (`open_disc`, `resolve_image`).
- Hardcodes specific to this game/repo: overlay names/files `open/OPEN.EXE`, `game/GAME.EXE`, `end/END.EXE`; the `LIBGPU` address table (KF2's `open` link); `ROOT/config/funcmaps/<name>.json`; `0x800` header size; `JR_RA = 0x03E00008`.
- How to parameterise: overlay list (name, disc file) and the seed address/name table into a per-game JSON; the `--libgpu` seed is specific to this game's libgpu layout.

---

#### scripts/merge_branch_spans.py  (359 lines)
- Bucket: **B** (confidence: high) -- generic MIPS sweep repair driven by `config/kf2.json`; only the hardcoded disc path/config default are repo-specific.
- Does: rejoins functions a linear sweep split, using conditional branches (which never leave a function) and switch jump tables as proof, rewriting each overlay's funcmap.
- Hooks: none.
- Data: overlay base/size/file/FuncMap come from `config/kf2.json`; no fixed addresses.
- Structs: funcmap entry `{start,end,name}`; `FuncIndex` over sorted non-overlapping functions.
- Control flow: MIPS opcodes `J=2`, `JAL=3`, `REGIMM=1`, branches `{4,5,6,7}`, regimm branches `{0,1,16,17}`; `lui=15`, `addiu=9`, `lw=35`, `jr` func 8, `add/addu` funcs 32/33; 2048-byte sectors.
- Overlays: whatever `config["overlays"]` names (`open`, `game`, `end`, `fdat*`); `base`/`file`/`funcMap` from config.
- Env / settings keys: none.
- RecompOne deps: the funcmap JSON shape and overlay config.
- Mod-visible: none.
- Imports from scripts: `inspect_disc` (`open_disc`, `resolve_image`), `extract_file.find_entry`.
- Hardcodes specific to this game/repo: `REPO/config/kf2.json` (default, overridable via `--config`) and `REPO/disc/KingsField2.cue` (hardcoded in `main`, **not** a flag); funcmap directory under `config/`; `0x800` header assumption; overlay key names `name`/`file`/`base`/`funcMap`/`size`/`offset`/`skip`. No KF2 code addresses.
- How to parameterise: make the disc path a `--disc` flag (it is the one real hardcode); everything else already comes from `config/kf2.json`.

---

#### scripts/merge_sdk_names.py  (162 lines)
- Bucket: **B** (confidence: medium) -- generic "rename unmatched functions from a signature match" tool; the PSY-Q name list is library-wide, overlay names are the only KF2 strings.
- Does: takes `--autoconfigure`'s funcmaps and writes the matched PSY-Q names into `config/funcmaps/*.json`, deliberately refusing any name `SdkPatches` binds so binding stays by address.
- Hooks: none.
- Data: none; reads funcmaps.
- Structs: funcmap entry `{address, name}`; either a list or `{functions:[...]}`.
- Control flow: none.
- Overlays: `OVERLAYS = ("open", "game", "end")` (KF2's three executables, hardcoded).
- Env / settings keys: none.
- RecompOne deps: reads the bind table `tools/RecompOne/RecompOne.Recompiler/CodeGen/SdkPatches.cs`; the `patches[]`/`SdkPatches` name-binding mechanism.
- Mod-visible: none.
- Imports from scripts: none.
- Hardcodes specific to this game/repo: `OVERLAYS = ("open","game","end")`; `SDK_PATCHES = "tools/RecompOne/RecompOne.Recompiler/CodeGen/SdkPatches.cs"`; default `--maps config/funcmaps`; a copied `HLE_NAMES` set (generic PSY-Q names: libcd/libmdec/libetc/libgpu/libpad/libmcrd/libapi).
- How to parameterise: overlay list from `config/kf2.json`; the funcmap dir and SdkPatches path are already flags/derivable. `HLE_NAMES` is generic PSY-Q, not KF2.

---

#### scripts/msg_glyphs.py  (186 lines)
- Bucket: **D** (confidence: high) -- built around KF2's specific disc archives, font grid and message container.
- Does: OCRs the 4-bit TIM fonts in the disc's message archives, cuts them on the game's fixed cell grid, and emits `patches/MessageGlyphs.cs` mapping FNV-1a hashes of cell bitmaps to characters.
- Hooks: none.
- Data (hardcoded): disc paths `CD/COM/TALK.T` (archive 3) and `CD/COM/ITEM.T` (archive 6); `CELL_W,CELL_H,ORIGIN_X = 8,14,4`; `PHASES = (3,10)`; TIM tag `0x10`/flags `8`; 2048-byte archive units; `OVERRIDES = {"¥":"y"}`; FNV-1a 64 constants `0xCBF29CE484222325`/`0x100000001B3`.
- Structs: TIM archive offset table (`u16 count` + `count+1` offsets at 2048-unit granularity); TIM header; 14-row cell masks.
- Control flow: assumes text starts at x=4 on an 8×14 grid, lines centred at phases 3/10 mod 14; majority vote across images.
- Overlays: none.
- Env / settings keys: none.
- RecompOne deps: none.
- Mod-visible: none.
- Imports from scripts: `extract_file.find_entry`, `inspect_disc` (`open_disc`, `resolve_image`).
- Hardcodes specific to this game/repo: `CD/COM/TALK.T`, `CD/COM/ITEM.T`, archive ids 3 and 6, `8×14` cell at origin `x=4`, phases `(3,10)`, output `patches/MessageGlyphs.cs`, `namespace Kf2`, `static class MessageGlyphs`, `Dictionary<ulong,char> Table` contract with `MessageText.Decode`.
- How to parameterise: disc paths, archive ids, cell geometry, phases, output path and namespace/class into a per-game JSON; the OCR+hash pipeline itself could reuse, but the grid/format is game data. Emitted class/namespace are consumed by `patches/MessageText.cs`.

---

#### scripts/profile_report.py  (135 lines)
- Bucket: **B** (confidence: high) -- generic CSV profiler summariser; KF2 touched only via the producer env var name in the docstring.
- Does: reads a `KF2_PROFILE_OUT` CSV (one row per section per frame plus pseudo-rows) and prints where frame time goes and the worst frames by work.
- Hooks: none.
- Data: none.
- Structs: CSV columns `frame`, `time_ms`, `section`, `group`, `self_ms`, `incl_ms`, `calls`; pseudo sections `frame.total`, `frame.gc_pause`, `frame.alloc_kb`, `frame.jit`, `gpu.*`.
- Control flow: groups `Frame`/`Wait`/`Gpu`; `work = total - wait - gpu`.
- Overlays: none.
- Env / settings keys: reads no env; docstring names `KF2_PROFILE_OUT` as the producer.
- RecompOne deps: runtime `0084` (the `gpu.*` rows); the profiler CSV schema.
- Mod-visible: none.
- Imports from scripts: none.
- Hardcodes specific to this game/repo: `KF2_PROFILE_OUT` (docstring); the pseudo-row names above are the port profiler's schema; example `--match func_800342D8` in the docstring.
- How to parameterise: already CLI (csv); the CSV schema names are the port's contract and would be shared with a sibling game only if the same profiler is reused.

---

#### scripts/rate_census.py  (231 lines)
- Bucket: **C** (confidence: high) -- the differential render-rate census mechanism generalises, but the struct-fold table, env vars and writer model are KF2's.
- Does: runs an identical scene at two render rates, ranks every RAM word by how much more often it changed at the higher rate, folds struct-slot findings into one row per field, and annotates each with `find_writers`.
- Hooks: none (uses the runtime's `KF2_RATECENSUS` sampler).
- Data (hardcoded): `STRUCTS = [("object table", 0x80177714, 0x44, 396)]` -- object table base `0x80177714`, stride `0x44` (68 B), 396 records.
- Structs: the object table above; its shape is stated to match `AgentServer`'s `nearby`.
- Control flow: samples on the emulated vblank (wall-clock 60 Hz), ratio 20→144 tops near 3.0, candidate threshold ratio >1.5.
- Overlays: none directly (via `kf2run`).
- Env / settings keys (set): `KF2_FPS`, `KF2_RATECENSUS=1`, `KF2_RATECENSUS_OUT`, `KF2_RATECENSUS_PERIOD=5`, `KF2_SMOOTH=0`, `KF2_SMOOTH_OBJECTS=0`, `KF2_SMOOTH_ANIM=0`.
- RecompOne deps: none; depends on the port's `RateCensus` probe.
- Mod-visible: none.
- Imports from scripts: `kf2run` as `kf2`, `kf2model` as `model`, `find_writers`.
- Hardcodes specific to this game/repo: object table `0x80177714` / `0x44` / `396`; all `KF2_*` env vars; `SCRATCH=/tmp/kf2-rate-census`.
- How to parameterise: struct table into a per-game JSON (or read from the same source `AgentServer` uses); env prefix and scratch root as constants. The comparison algorithm is generic.

---

#### scripts/rate_matrix.py  (340 lines)
- Bucket: **C** (confidence: medium) -- the matrix runner and markdown table are generic, but every scenario hardcodes KF2 addresses, shell verbs, probe-log regexes and env vars.
- Does: runs a named scenario (menu scroll, death clock, walk, modal rate, sprite anim, compass needle, idle) across a matrix of `--fps`/`--tickrate`/`--env` settings and prints a paste-ready markdown table (optional `--json`).
- Hooks: none (drives via `kf2run`/`KF2_SHELL`).
- Data (hardcoded): death counter `0x8019951A` ("65-tick"; `AutoReload.HoldAt = 31` per docstring); billboard counter `0x80195170` and walk `func_800331B4` (sprite scenario); compass needle spring `0x8006E608` (compass scenario, stepped in stage 13).
- Structs: none direct; reads `state` fields `deathFrames`, `dead`, `pos`.
- Control flow: scenarios assume an area is up; the death-clock slope is the tick rate; modal-loop probes distinguish loop body vs picture.
- Overlays: none directly.
- Env / settings keys (set/read): `KF2_MENUPACING_PROBE=1`, `KF2_AUTORELOAD=0`, `KF2_LOOPPACING_PROBE=1`, `KF2_SPRITEANIM_PROBE=1`, `KF2_STAGE13_PROBE=1`, `KF2_FPS`, `KF2_TICKRATE`; `--env KEY=VALUE` passthrough (`KF2_MENUPACING=0`, `KF2_LOOPPACING=0`, `KF2_SPRITEANIM=0`, `KF2_STAGE13_NEEDLE=0` in docstrings).
- RecompOne deps: none direct; depends on port probe logs and `LoopPacing`/`Stage13`/`SpriteAnim`/`MenuPacing`.
- Mod-visible: none.
- Imports from scripts: `kf2run` as `kf2`.
- Hardcodes specific to this game/repo: shell verbs `kill` and `warp 5`; buttons `Circle`/`Down`/`Up`; probe-line regexes (`menu pacing:`, `blink stepped`, `loop pacing:`, `world iteration(s) a second`, `sprite anim:`, `stage 13:`); addresses and `func_800331B4` above; `SCRATCH=/tmp/kf2-rate-matrix`; the `KF2_AUTORELOAD`/`AutoReload.HoldAt` interaction.
- Split note: the matrix/table/report half is B; the `SCENARIOS` dict (drivers, env, addresses, regexes) is D. Seam is at the scenario definitions.
- How to parameterise: scenarios into a per-game module/JSON (actions, env, log patterns, addresses); runner stays.

---

#### scripts/release.sh  (44 lines)
- Bucket: **B** (confidence: high) -- generic semver bump/commit/tag; only the product string is this repo's.
- Does: validates a `MAJOR.MINOR.PATCH` argument, requires a clean tree and no existing tag, writes `VERSION`, commits and creates annotated tag `v<version>` (deliberately does not push).
- Hooks: none.
- Data: none.
- Structs: none.
- Control flow: refuses dirty tree, duplicate tag, non-semver, unchanged version.
- Overlays: none.
- Env / settings keys: none.
- RecompOne deps: none.
- Mod-visible: none.
- Imports from scripts: none.
- Hardcodes specific to this game/repo: `VERSION` file; tag message `"Verdite2 v$NEW"`; tag prefix `v`; `ROOT` derived from script path; assumes `release.yml` publishes on tag.
- How to parameterise: product name/tag message as a variable; already takes the version. Repo-structure (`VERSION`) is a convention.

---

#### scripts/setup_tools.sh  (144 lines)
- Bucket: **B** (confidence: high) -- vendored-RecompOne build/merge tooling; RecompOne-specific, not KF2-specific.
- Does: builds the vendored RecompOne, or (flags) fetches the PSY-Q signature bank or starts a three-way merge of upstream into `tools/RecompOne`.
- Hooks: none.
- Data: none.
- Structs: `tools/RecompOne/UPSTREAM` holds the merge-base commit.
- Control flow: creates a bare fork repo `tools/RecompOne.git` on demand; `origin/master` is the upstream branch; shepherding instructions printed for conflicts.
- Overlays: none.
- Env / settings keys: none.
- RecompOne deps: the whole vendored tree and its gitdir; `RecompOne.Recompiler` project; signature path `RecompOne.Recompiler/AutoConfigure/signatures/psyq.json`.
- Mod-visible: none.
- Imports from scripts: none.
- Hardcodes specific to this game/repo: `UPSTREAM_URL = "https://github.com/BlackLabelHQ/RecompOne.git"`; `tools/RecompOne`; `tools/RecompOne.git`; `UPSTREAM` file; signature path; build `dotnet build "$TOOLS/RecompOne.Recompiler" -c Release`; merge branch name `vendored`; upstream branch `origin/master`.
- How to parameterise: upstream URL/paths are constants of the vendoring model; would be identical for a sibling repo using RecompOne.

---

#### scripts/shader_probe.c  (250 lines)
- Bucket: **C** (confidence: medium) -- a reusable headless fragment-shader pixel probe, but the varyings/uniforms and texture-rectangle packing are the KF2 renderer's contract.
- Does: runs one of the port's real prim fragment shaders over a known value-noise texture with a known footprint, reads pixels back, and reports the colour spread across `uAniso` values (and mip/rect/leak/plain-depth modes).
- Hooks: none (drives the shipped shader).
- Data (hardcoded): `VW=64`, `FOOT=16` (overridable by env), VRAM sheet 1024×512, texture rectangle `RECT=u0,u1`, `uCentre`; plain-depth base `2000.f/65536.f`; sample rows at `y=2*VW` readback.
- Structs: texture handle `uTex` = rectangle word (`u0|u1<<16|15<<24`) plus mip entry flag `0x80000000|0x40000000|lg<<16`; atlas at unit 5 with 9 mip levels, block size `1<<lg`.
- Control flow: substitute vertex shader supplies varyings; `PLAIN` maps enhancement distance in texels; `MIP=1` builds the atlas the way `GlTexCache` does; `UC`/`LEAK`/`RECT` env modes.
- Overlays: none.
- Env (read): `FOOT`, `MIP`, `LEAK`, `RECT`, `UC`, `PLAIN` (program's own, not `KF2_*`).
- RecompOne deps: patches `0060` (mipmaps where the texture is decoded), `0083` (enhancement distance/`uPlainZ`).
- Mod-visible: none.
- Imports from scripts: none (EGL/GL only).
- Hardcodes specific to this game/repo: the shader interface — varyings `vColor/vUV/vDepth/clutBase/pageBase/texMode/vDither/vRepClut/vFade/vLit/vFog/vLight/vTex`, uniforms `uVram/uDest/uExtTex/uRepTex/uRepClut`, `uTexWindow=255,255,0,0`, `uScale=1`, `uSetMask`, `uCheckMask`, `uTrueColor`, `uPosBias`, `uBlend`, `uRepClutCount=16`, `uRepRect`, `uAniso`, `uMipOn`, `uMip`, `uTestDepth`, `uPlainZ`; 1024×512 VRAM; PS1 15-bit channel quantisation.
- How to parameterise: the EGL/FBO/readback/statistics harness is B; the varying/uniform names and texture-rectangle packing belong to the shader/renderer and should come from the shader or a per-game header. Split note: harness vs shader contract.

---

### Summary

Counts (20 files): **B = 11, C = 7, D = 2**.

- B: `add_call_targets.py`, `audio_spectrum.py`, `callgraph.py`, `extract_file.py`, `inspect_disc.py`, `kf2run.py`, `merge_branch_spans.py`, `merge_sdk_names.py`, `profile_report.py`, `release.sh`, `setup_tools.sh`
- C: `check_gate.py`, `find_writers.py`, `light_probe.c`, `match_overlays.py`, `rate_census.py`, `rate_matrix.py`, `shader_probe.c`
- D: `kf2model.py`, `msg_glyphs.py`

Key observations:

1. **The whole `scripts/` tree is nearly clean.** Almost every KF2 coupling is a *path/name/env-prefix* constant, not a code address. The scripts that pass addresses on the command line (`add_call_targets`, `find_writers`) are already parameterised.
2. **`kf2model.py` is the keystone D dependency.** `check_gate.py`, `find_writers.py` and `rate_census.py` all inherit its KF2 knowledge (`func_8001369C`, `L80013918`, `patches/FramePacing.cs`, `generated/game.cs`, overlay `game`). Parameterising `kf2model` (a per-game JSON of main loop/label/overlay/gate source) converts those three to B for free and leaves `rate_census` with only its struct table to externalise.
3. **The parse layer is game-agnostic but for three strings.** `callgraph.py` only knows `generated/`, the class prefix `KingsField2(_*)`, and `config/funcmaps/<overlay>.json`; the rest (instruction idioms, RAM window, dispatch) is generic PS1. `kf2model`'s `generated/game.cs` assumption sits on top.
4. **Disc-side tooling is fully generic.** `inspect_disc.py`, `extract_file.py`, `add_call_targets.py`, `merge_branch_spans.py` carry no KF2 data at all; only their docstrings name the game. `merge_branch_spans.py`'s one real hardcode is the disc path `disc/KingsField2.cue` in `main()`—the config is already a `--config` flag.
5. **`match_overlays.py` hardcodes the port's libgpu address table** (17 entries in `open`) plus the `open/game/end` executable names. The delta-matching machinery (normal form, masking, consensus) is the reusable part and would carry to a sibling game with a different seed table.
6. **The two C probes encode a shader/uniform contract, not game memory.** `light_probe.c` embeds the remaster formula constants (`BK=1920`, LCM `2662/3328`, material table `256*3*4`, sampler units 6/7/12-15) and `shader_probe.c` the frag-shader varyings/`uTex` rectangle packing. Both harnesses (EGL, FBO, pixel readback, statistics) are B; the shader interface block is the seam.
7. **`msg_glyphs.py` is the clearest D:** disc paths `CD/COM/TALK.T`/`ITEM.T`, archive ids 3/6, the `8×14` grid at `x=4`, phases `(3,10)`, output `patches/MessageGlyphs.cs`, and the emitted class contract with `MessageText.Decode`.
8. **`rate_census.py`'s object table (`0x80177714`, stride `0x44`, 396 records) duplicates a layout that also lives in `AgentServer`**, so the per-game struct table should be one shared definition rather than restated in Python.
9. **`rate_matrix.py` splits cleanly at `SCENARIOS`:** the matrix runner/table printer is B; each scenario (death counter `0x8019951A`, needle `0x8006E608`, billboard counter `0x80195170`, `warp 5`, probe-log regexes) is D.
10. **`KF2_*` env vars are set, not just read, by the harnesses:** `kf2run` defaults `KF2_SHELL`/`KF2_AUTOSTART`; `rate_census` sets `KF2_RATECENSUS*`/`KF2_SMOOTH*`; `rate_matrix` sets the per-scenario `*_PROBE` switches. A sibling repo either keeps the `KF2_` prefix or needs a shared env-prefix constant—the prefix is the only cross-cutting naming choice.

## The launcher, packaging, CI, mcp/, Program.cs, setup and release scripts
Scope: everything under `Verdite2.Launcher/` (except its `bin/`, `obj/`),
`packaging/` (linux/, windows/, shared/), `.github/workflows/*.yml`, `mcp/`,
root `Program.cs`, `Deception.csproj`, `scripts/setup_tools.sh`,
`scripts/release.sh`, `VERSION`, `.gitignore`. Read-only; nothing else touched.

Overall: this whole layer is **bucket B** (game-agnostic infrastructure). What
varies between King's Field 1 / 2 / 3 is a small, identifiable per-game datum set
embeddable in a shell (Serial, boot EXE name, list of disc files the recompiler
reads, config file name, assembly/project/app-id/repo/icon strings). The only
place a *mechanism* touches game RAM is root `Program.cs` (bucket C/D, the patch
install list), which is deliberately excluded from the shipped launcher payload.

### Summary of the per-game datum set (the seam)

A reusable shell + a per-game data record would need to supply exactly:

| datum | current value | where |
|---|---|---|
| disc serial (validation) | `SLUS-00158` | DiscCheck.cs:30 |
| boot EXE inside SYSTEM.CNF | `SLUS_001.58` | DiscCheck.cs:31, BuildKey.cs:50 |
| disc files the recompile reads | `SYSTEM.CNF`, `SLUS_001.58`, `OPEN.EXE`, `GAME.EXE`, `END.EXE`, `CD/COM/FDAT.T` | BuildKey.cs:50, DiscCheck.cs:34-42 |
| known-wrong serial + message | `SLUS-00255` (different game) | DiscCheck.cs:176-179, release.yml:128 |
| recompiler config file | `config/kf2.json` | Recompile.cs:38-39, DiscCheck.cs:135 |
| config dir payload glob | `../config/**/*.json` | Launcher.csproj:124 |
| output dir | data `generated/` | Launcher Program.cs:110 |
| game assembly name | `KingsField2` | GameCompile.cs:32, csproj:9 |
| built game DLL | `KingsField2.dll` | Launcher Program.cs:81 |
| generated class alias referenced | `Recompiled.KingsField2` | GameCompile.cs comment |
| launcher assembly / exe | `Verdite2` / `Verdite2.exe` / `Verdite2.dll` | csproj:33, stub:16 |
| root namespace | `Verdite2.Launcher` / `Kf2` (patches) / `Kf2.Mcp` | csproj:34, mcp csproj |
| app id (Wayland/desktop) | `verdite2` | Launcher Program.cs:45, root Program.cs:1449 |
| per-user data dir | `Verdite2` / `verdite2` | Paths.cs:78,83,87 |
| env prefix | `VERDITE2_` (launcher), `KF2_` (game) | Paths.cs, UpdateCheck.cs |
| update repo | `Voicedrew11/verdite2` | UpdateCheck.cs:19 |
| icons | `verdite2.png`, `verdite2.ico` | csproj:97,129; Stub csproj:25 |
| desktop Name/Generic/Comment | `Verdite2` / `King's Field` / SLUS-00158 | verdite2.desktop |
| window title | `Verdite2 {ver}` | Launcher Program.cs:46 |
| AppImage / zip / installer names | `Verdite2-$VERSION-x86_64.AppImage`, `Verdite2-$version-win-x64.zip`, `Verdite2-$AppVersion-win-x64-setup` | build scripts, .iss |
| Inno AppId GUID | `{9F1F0C1E-6A3E-4C69-9C2A-9E5F2B8D4A11}` | verdite2.iss:22 |

Everything else in this layer is pure mechanism.

---

### The reusable launcher shell (bucket B, high)

#### Verdite2.Launcher/Program.cs (189 lines)
- Bucket: B (high) -- generic first-run orchestrator; per-game values are 4 string literals.
- Does: settles paths, installs DiscValidator, initializes window+localization, starts update check, ensures/loads the built game assembly, builds it if absent, plays it.
- Hooks: none.
- Data: none.
- Structs: none.
- Control flow: assumes game is a built .NET assembly with an `EntryPoint` accepting `argv[0] = disc path`; build runs on worker thread while main thread pumps (`Runtime.Pump`), because main thread owns the GL context.
- Overlays: none directly.
- Game-specific: `Runtime.AppId = "verdite2"` (:45); window title `Verdite2 {Ver.Number}` (:46); `"KingsField2.dll"` (:81); console tag `[Verdite2]`.
- Env / settings keys: none read here (uses `Runtime.CdPath`, `Paths`, `BuildKey`).
- RecompOne deps: `RecompOne.Runtime` (`Runtime.Initialize/Pump/WaitForValidDisc/CdPath/SetIcon/DiscValidator/AppId`, `Localization.Merge`, `PopupManager`, `ConfigManager` via patches). The window/GL owned by runtime.
- Mod-visible: nothing (launcher never compiles against game).
- Split note: all per-game strings are the 4 literals above + the `content/` payload layout.

#### Verdite2.Launcher/Ver.cs (28 lines)
- Bucket: B (high) -- reads assembly version + InformationalVersion; no game knowledge.
- Does: exposes `Number` (MAJOR.MINOR.PATCH from assembly) and `Full` (release+commit).
- Hooks/Data/Structs/Control flow/Overlays: none.
- Env / settings keys: none.
- RecompOne deps: none.
- Mod-visible: none.

#### Verdite2.Launcher/Console.cs (48 lines)
- Bucket: B (high) -- Windows WinExe console reattachment.
- Does: `AttachConsole(ATTACH_PARENT_PROCESS)` + reopen stdout/stderr so KF2_LOG etc. reach a launching terminal; no-op off Windows.
- Hooks/Data/Structs/Control flow/Overlays: none.
- Env / settings keys: none (comment mentions `KF2_LOG`, `KF2_AGENT`).
- RecompOne deps: none.
- Mod-visible: none.

#### Verdite2.Launcher/BuildProgressPopup.cs (111 lines)
- Bucket: B (high) -- first-run progress UI; only the localized strings name the game.
- Does: modal, non-closable popup with 3 steps (reading disc / translating / compiling) and an animated ellipsis; shows compiler error + points at `build.log`.
- Hooks/Data/Structs/Control flow: none.
- Overlays: none.
- Game-specific: `Strings` JSON (en/pt-BR/es-419): `"Preparing King's Field"`, `"Verdite2 is building the game from your disc…"`, keys prefixed `verdite2.build.*`.
- RecompOne deps: `RecompOne.Runtime.Host.Window.Popup`, `Localization`. Patch 0030 provided `Runtime.Pump`.
- Mod-visible: `const string Strings`.

#### Verdite2.Launcher/UpdateCheck.cs (204 lines)
- Bucket: B (high) -- GitHub release notifier, generic but hard-wired to one repo.
- Does: once/launch background check; `GET https://api.github.com/repos/Voicedrew11/verdite2/releases/latest`; caches tag+url in `<data>/update.json`; daily throttle; semver compare; skip/announce.
- Hooks/Data/Structs/Control flow/Overlays: none.
- Game-specific: repo `Voicedrew11/verdite2` (:19); UserAgent `"Verdite2"` (:105); settings key `Verdite2.UpdateCheck` (:22); state file `update.json`; console tag `[Verdite2]`.
- Env / settings keys: `VERDITE2_UPDATE_CHECK` (`0`/`force`); `Verdite2.UpdateCheck` (ConfigManager.View bool, default true).
- RecompOne deps: `RecompOne.Runtime.Config.ConfigManager`.
- Mod-visible: `public sealed record Release`, `Available`, `Enabled`, `IsNewer`, `Skip`, `SettingKey`, `Start`.

#### Verdite2.Launcher/UpdateBadge.cs (171 lines)
- Bucket: B (high) -- menu-bar update badge + popup menu (open page / skip / hide).
- Does: registers `UpdatePopup`, a right menu-bar item, and an Interface settings checkbox.
- Hooks/Data/Structs/Control flow: none.
- Game-specific: localized `verdite2.update.*` strings (en/pt-BR/es-419); gold palette.
- Env / settings keys: `Verdite2.UpdateCheck` via UpdateCheck.
- RecompOne deps: `PopupManager`, `MainMenuBar.AddRightItem`, `SettingsRegistry.Extend("interface", …)`, `Localization`, `ConfigManager.SaveView`, `PanelManager.Panels`.
- Mod-visible: `Install`, `PushGold`, `PopGold`, `Skip`, `OpenUrl`, `const Strings`.

#### Verdite2.Launcher/UpdatePopup.cs (87 lines)
- Bucket: B (high) -- startup notice popup (download / skip / later).
- Does: one-shot per launch, only outside play; listens `OverlayLoadedEvent`.
- Game-specific coupling: treats overlay names specially -- `"open"`/`"game"` = not in play, `"end"` or name starting `"fdat"` = in play (:27-28). **This is bucket C/D leakage: the overlay naming convention of this game is baked into a generic-looking UI component.**
- Env / settings keys: none direct.
- RecompOne deps: `RecompOne.Runtime.Events.Event`/`OverlayLoadedEvent`, `Popup`.
- Mod-visible: none (internal class).
- Split note: seam is the overlay-name predicate; parameterize as "is this an in-play overlay?".

#### Verdite2.Launcher/Verdite2.Launcher.csproj (172 lines)
- Bucket: B (high) -- shipped executable project; builds with no disc.
- Does: WinExe net10.0 self-contained publish; assembly `Verdite2`, root ns `Verdite2.Launcher`; version from `../VERSION`; QuickJit off; trimming/single-file off; icon from `packaging/shared/verdite2.ico`; references RecompOne.Runtime + RecompOne.Recompiler; stages payload as `content/`; stamps `InformationalVersion = Version+GitSha`.
- Game-specific: `<AssemblyName>Verdite2</AssemblyName>`, `<RootNamespace>Verdite2.Launcher`, `ApplicationIcon` = verdite2.ico, payload globs `../config/**/*.json` → `content/config`, `../Program.cs` → `content/src/Program.cs`, `../patches/**/*.cs` → `content/src/patches`, `../mods/**/*` → `content/mods`, `../packaging/shared/verdite2.png` → `verdite2.png`.
- Env / settings keys: `VERDITE2_BUILD` (overrides commit stamp).
- RecompOne deps: `RecompOne.Runtime.csproj`, `RecompOne.Recompiler.csproj`. `ValidateExecutableReferencesMatchSelfContained=false` (Exe-to-Exe).
- Mod-visible: none.
- Split note: payload directory names (`config/`, `src/`, `mods/`) are the contract GameCompile/BuildKey/Paths rely on.

#### Verdite2.Launcher/Build/BuildKey.cs (96 lines)
- Bucket: B (high) -- cache key for a built assembly.
- Does: SHA-256 over, in order:
  1. literal `"verdite2"` (:46) and `BuildKey` assembly `Version` (:47);
  2. per disc file in `{SYSTEM.CNF, SLUS_001.58, OPEN.EXE, GAME.EXE, END.EXE, CD/COM/FDAT.T}` (:50): name + SHA-256 of bytes, or `"<missing>"`;
  3. every `content/src/**/*.cs` (`Sources.All()`) and `content/config/**/*.json` (`Sources.Config()`), each as relative path + SHA-256, sorted ordinal.
  Returns first 16 hex chars. Length-prefixed `Add`.
- Explicitly NOT in key: absolute LBAs (read during recompile), the git commit, the `InformationalVersion`.
- Game-specific: literal `"verdite2"`; the disc file list; directory names.
- Env / settings keys: none.
- RecompOne deps: `RecompOne.Runtime.Cdrom.DiscFs`.
- Mod-visible: none (`internal static class`).
- What *would* need per-game editing: the file list (a game with no `CD/COM/FDAT.T`, different boot EXE, more/fewer executables).

#### Verdite2.Launcher/Build/DiscCheck.cs (202 lines)
- Bucket: B/C (high) -- disc validator; the *mechanism* is generic, every literal is this game.
- Does: fills `Runtime.DiscValidator`; opens image via `DiscFs` (cue/bin/chd; chd codecs cdzl/cdlz/cdfl/zlib/lzma only); requires `SYSTEM.CNF` contain `SLUS_001.58` case-insensitive; requires OPEN/GAME/END.EXE ≥ 0x800 and `CD/COM/FDAT.T` ≥ `FdatFloor()`; memoised on path+size+mtime.
- `FdatFloor()`: parses `content/config/kf2.json` overlays, takes `max(offset+size)` for entries whose `file == "CD/COM/FDAT.T"`; unparseable → 0 (so a broken install is blamed on the install, not the disc).
- `Wrong()`: names the inserted disc from SYSTEM.CNF `cdrom:\…` (11-char `SLUS_001.58` → `SLUS-00158`); special message for `SLUS-00255`.
- Game-specific: `Serial="SLUS-00158"`, `BootExe="SLUS_001.58"`, `SLUS-00255` message, `CD/COM/FDAT.T`, `config/kf2.json`, `0x800` EXE floor.
- Env / settings keys: none.
- RecompOne deps: `RecompOne.Runtime.Cdrom.DiscFs` (`Open`, `ReadFile`, `Locate`); sets `Runtime.DiscValidator`.
- Mod-visible: `public const string Serial`.
- Split note: exactly the per-game record (serial, boot exe, required-file list + floors, wrong-serial message, config name). Generic logic below it.

#### Verdite2.Launcher/Build/GameCompile.cs (193 lines)
- Bucket: B (high) -- Roslyn compile of generated code + port sources; one per-game string.
- Does: parse `GlobalUsings.g.cs` (literal 7-global list, :111-119) + all `generatedDir/**/*.cs` ordinal + `Sources.All()` (`content/src/**/*.cs`); `CSharpCompilationOptions(ConsoleApplication)` with AllowUnsafe, Nullable enable, Optimization Release, Platform.AnyCpu, ConcurrentBuild, **all warnings suppressed except CS5001 = Error**; emit embedded debug info to `<dll>.tmp` then rename.
- Assumes: `AssemblyName = "Deception"` (:32); references = host `TRUSTED_PLATFORM_ASSEMBLIES` plus loaded non-dynamic assemblies (raw metadata fallback), skipping `"recompone"` and `"Verdite2"` (:192).
- Payload source dirs: `content/src` (Program.cs + patches/**), `content/config` is **not** compiled here (it is the recompiler's input).
- Game-specific: `AssemblyName = "Deception"`; comment names 15 static calls `Recompiled.KingsField2.func_XXXXXXXX` from AutoReload/AreaWarp/CullGrid; skip set names `Verdite2`.
- Env / settings keys: none.
- RecompOne deps: `RecompOne.Runtime` must be in TPA/loaded so `using RecompOne.*` resolves; Roslyn kept because RecompOne.Runtime references it.
- Mod-visible: none.
- Split note: seam is the assembly name + the fact the port's `Program.cs` directly calls `Recompiled.<GameName>`. Must stay in step with `Deception.csproj` options.

#### Verdite2.Launcher/Build/Recompile.cs (133 lines)
- Bucket: B (high) -- drives the recompiler in-process; one per-game config name.
- Does: `Stage()` mirrors `content/config/**/*.json` into `<data>/config/` (copy-if-length+mtime differ); writes `kf2.build.json` = `kf2.json` with `cue` and `output` string-edited (preserving comments/trailing commas); invokes `RecompOne.Recompiler.Config.ConfigLoader`'s assembly `EntryPoint` via reflection, passing `[cfgPath]`; deletes temp config; throws on nonzero exit / TargetInvocationException.
- Assumes: recompiler is an Exe with top-level statements (`Program.<Main>`); config relative funcMap paths resolve against staged dir.
- Game-specific: `kf2.json` / `kf2.build.json`; keys `"cue"`, `"output"`.
- Env / settings keys: none.
- RecompOne deps: `RecompOne.Recompiler.Config.ConfigLoader`.
- Mod-visible: none.
- Split note: config filename is the per-game datum; staging/rewrite mechanism generic.

#### Verdite2.Launcher/Build/Paths.cs (134 lines)
- Bucket: B (high) -- install/data layout; per-game names only.
- Does: resolves
  - `Install` = dir containing `content/`, else its parent (Windows `bin/` shape), else base dir;
  - `Content` = `Install/content`; `ContentConfig` = `content/config`; `ContentSrc` = `content/src`; `ContentMods` = `content/mods`;
  - `Data` = `$VERDITE2_DATA` → `%LOCALAPPDATA%\Verdite2` → `$XDG_DATA_HOME/verdite2` → `~/.local/share/verdite2`;
  - `Builds` = `<Data>/builds`; `BuildLog` = `<Data>/build.log`.
  - `Prepare()`: mkdir Data+Builds, `SetCurrentDirectory(Data)` (so runtime-relative `settings.json`, `interface.ini`, `carda.sav`, `carda.fog`, `mods/` land there), seed `content/mods` into `<Data>/mods` tracking a `.mods-seeded` manifest (deleted mods stay deleted; new ones still seed).
- Game-specific: `verdite2` / `Verdite2` directory and app names.
- Env / settings keys: `VERDITE2_DATA`, `XDG_DATA_HOME`.
- RecompOne deps: none (named-relative-path contract with runtime).
- Mod-visible: `public static` Install/Content/ContentConfig/ContentSrc/ContentMods/Data/Builds/BuildLog/Prepare.

---

### Packaging (bucket B, high, except icon art)

#### packaging/linux/build-appimage.sh (71 lines)
- Bucket: B (high) -- no disc needed; generic AppImage build.
- Does: `dotnet publish` launcher self-contained linux-x64 → `AppDir/usr/bin`; writes `AppRun` exec'ing `usr/bin/Verdite2`; appends `X-AppImage-Version=$VERSION` to the `.desktop`; copies icon + `LICENSE` + `NotoSans-OFL.txt`; fetches `appimagetool` if absent; builds `Verdite2-$VERSION-x86_64.AppImage`.
- Game-specific: `Verdite2.AppDir`, `usr/bin/Verdite2`, `verdite2.desktop`, `verdite2.png`, `dist/Verdite2-$VERSION-x86_64.AppImage`, doc dir `verdite2`, `VERSION` file.
- Env / settings keys: `OUT`, `VERSION`, `APPIMAGETOOL`.
- RecompOne deps: none at runtime; comment cites patch 0033 / upstream FontSet (Noto Sans licence).
- Mod-visible: none.

#### packaging/windows/build-windows.ps1 (86 lines)
- Bucket: B (high).
- Does: reads `VERSION`; `dotnet publish` launcher self-contained win-x64 → `stage/bin`; moves `bin/content` → `stage/content`; removes duplicate `LICENSE`; publishes net48 stub → copies `Verdite2.exe`(+`.config`) to stage root; copies `LICENSE` + `NotoSans-OFL.txt` into `stage/licenses`; zips `Verdite2-$version-win-x64.zip`; sets `VERDITE2_VERSION`; runs `iscc` if present.
- Game-specific: names above (Verdite2.exe, win-x64, content/), `VERDITE2_VERSION`.
- Env / settings keys: `VERDITE2_VERSION`.
- RecompOne deps: none (licence comment).
- Mod-visible: none.

#### packaging/windows/verdite2.iss (61 lines)
- Bucket: B (high) -- Inno Setup installer.
- Does: installs an already-published `dist/win-x64/*`; install dir `{autopf}\Verdite2`; shortcuts point at root `Verdite2.exe` stub; `VERDITE2_VERSION` is required (errors if unset); does not remove saves on uninstall.
- Game-specific: `AppName "Verdite2"`, `AppId={{9F1F0C1E-6A3E-4C69-9C2A-9E5F2B8D4A11}}`, `AppPublisher=Voicedrew11`, `Verdite2-{#AppVersion}-win-x64-setup`, `Verdite2.exe`, `LICENSE`.
- Env / settings keys: `VERDITE2_VERSION`.
- Mod-visible: none.

#### packaging/windows/Stub/Program.cs (73 lines)
- Bucket: B (high) -- net48 root launcher that execs `bin\Verdite2.exe`, waits, forwards argv (quoted), attaches parent console, MessageBox on missing.
- Game-specific: `bin\Verdite2.exe`, `Verdite2` strings, namespace `Verdite2.Stub`; comment mentions `KF2_LOG`.
- Env / settings: none.
- Mod-visible: none.

#### packaging/windows/Stub/Verdite2.Stub.csproj (32 lines)
- Bucket: B (high). net48 WinExe x64, assembly `Verdite2`, root ns `Verdite2.Stub`, icon `../../shared/verdite2.ico`, PackageReference `Microsoft.NETFramework.ReferenceAssemblies 1.0.3`.
- Game-specific: assembly/namespace/icon names.
- Mod-visible: none.
- Note: `bin/` + `obj/` under the stub are committed build artefacts (net48 publish output incl. `Verdite2.exe`); they are tracked but are generated.

#### packaging/shared/README.md (16 lines)
- Bucket: B/D (high) -- describes the shipping icon (green verdite orb), PNG 256², ICO 16/32/48/256; `make-icon.py` made the old "V" placeholder; in play the window uses the game's own memory-card icon off the disc (`patches/CardIcon.cs`), `KF2_ICON=orb` keeps this.
- Game-specific: orb is game-branded; disc icon reference.
- Mod-visible: none.

#### packaging/shared/make-icon.py (79 lines)
- Bucket: B/D (high) -- dependency-free PNG+ICO generator; palette = the game's map palette (`MapRender.DrawNative`); draws a "V" placeholder; refuses overwrite without `--force`.
- Game-specific: `FIELD=(0x3A,0x52,0x3A)`, `INK=(0x0E,0x20,0x0E)`, `EDGE=(0x7B,0x8C,0x7F)`, filename `verdite2.png`/`.ico`, the "V".
- Mod-visible: none.

#### packaging/shared/verdite2.desktop (10 lines)
- Bucket: B/C (high) -- desktop entry.
- Does: `Exec=Verdite2 %f`, `Icon=verdite2`, `Name=Verdite2`, `GenericName=King's Field`, `Comment=A PC port of King's Field (SLUS-00158). Requires your own disc image.`, `Keywords=kings field;fromsoftware;playstation`.
- Game-specific: everything above; the SLUS-00158 disc requirement is per-game.
- Mod-visible: none.

#### packaging/shared/verdite2.png, verdite2.ico
- Bucket: D (high) -- shipped brand mark (binary art).

---

### CI (bucket B, high)

#### .github/workflows/ci.yml (58 lines)
- Bucket: B (high) -- asserts the shipped project builds with no disc.
- Does: checkout; setup-dotnet 10.0.x; `bash scripts/setup_tools.sh` (vendored, clones nothing); assert VERSION is `MAJOR.MINOR.PATCH` and no literal `<Version>` in launcher csproj; `dotnet build Verdite2.Launcher/... -c Release`; assert assembly carries `$v+[0-9a-f]{9}` (grep strings of `Verdite2.dll`); build Windows stub. **Deliberately does not build `Deception.csproj`** (needs disc).
- Game-specific: launcher project path, `Verdite2.dll`, stub path.
- Mod-visible: none.

#### .github/workflows/release.yml (129 lines)
- Bucket: B/C (high) -- tag-triggered release build for linux+windows.
- Does: builds vendored RecompOne; asserts `generated/` absent and `disc/` empty; asserts tag == VERSION and semver; stamps `VERDITE2_BUILD`; runs AppImage / Windows packaging; uploads artifacts; creates a **draft** GitHub release with generated notes and a body stating "Verdite2 ships no game data… you need your own dump of King's Field (NTSC-U, `SLUS-00158`)… US-boxed *King's Field II* (`SLUS-00255`) is a different game and will be refused."
- Game-specific: repo body text, SLUS ids, artifact dirs, `Verdite2` release name.
- Env: `VERDITE2_BUILD`.

---

### MCP server (bucket B/C, high)

#### mcp/Program.cs (492 lines)
- Bucket: B/C (high) -- plain stdio JSON-RPC 2.0 MCP server, no RecompOne reference; tool schemas are thin wrappers over KF2_SHELL.
- Does: `initialize` (echoes protocol version; serverInfo `kf2-mcp`), `ping`, `tools/list`, `tools/call`. Serial stdin loop; one request in flight over TCP to shell; never kills the process on error. `ShellClient` connects 127.0.0.1:27900 default, 5 s connect / 10 s reply timeouts, no resend after send.
- Tools exposed / shell verbs: `kf2_state`→`state`; `kf2_kill`→`kill`; `kf2_nearby`→`nearby [radius]`; `kf2_load_save`→`load <slot>`; `kf2_warp`→`warp <area>`; `kf2_press_button`→`press <button> [holdMs]`.
- Game-specific: `instructions` string names "King's Field II port", `KF2_SHELL=1 KF2_AUTOSTART=2 dotnet run --project Deception.csproj -c Release -- disc/KingsField2.cue`; tool descriptions use 8192≈four tiles, area 0..7 ("beacon's area numbering"), save slots 1..3; namespace `Kf2.Mcp`; env var `KF2_MCP_ENDPOINT`.
- Env / settings keys: `KF2_MCP_ENDPOINT` (default `127.0.0.1:27900`).
- RecompOne deps: none.
- Mod-visible: none (separate process).
- Split note: the shell verb vocabulary + units + area/slot ranges are this game's protocol; the JSON-RPC framing is generic.

#### mcp/KingsField2Mcp.csproj (20 lines)
- Bucket: B (high). net10.0 Exe, assembly `KingsField2Mcp`, root ns `Kf2.Mcp`; no PackageReference (hand-rolled JSON, offline restore).
- Game-specific: assembly/namespace names.

---

### Root build / entry (bucket C/D heavily)

#### Program.cs (root, 1488 lines)
- Bucket: C/D (high) -- the port's hand-owned entry point. Mechanically it is a run-time patch installer; every `Configure`/`Install` binds KF2 RAM addresses, KF2 routines and KF2 overlay names. It is **not** in the launcher payload as executable code path? It *is* shipped as `content/src/Program.cs` and is the game's Main. **This is the principal C/D file in scope.**
- Does: configures `KF2_LOG` channels; per-overlay IRQ callback table; KF2_AUTOPAD scripted pad; UI scale; key layout; runtime defaults; then ~70 patch `Configure()`+`Install()` calls; sets AppId/icon; constructs `PSMemory(Kf2.PrimBuffer.RamSize)`; `Entry.Run(memory, argv[0])`; crash dump + MapFog flush.
- Hooks: the `Install()` calls attach through `HookManager` to recompiled KF2 functions. Full `Install()` list below.
- Data: per-overlay IRQ callback tables -- `open` 0x8003DD48, `game` 0x8006E3D4, `end` 0x80038D90, with `InterruptCallback` addresses 0x8001E75C / 0x8005F8CC / 0x8001AD28; `CallbackTable`. Also `0x801B6CA8` (vblank credit, in comment), `0x8019951A` death clock (AGENTS).
- Structs: IRQ callback table = 11 slots × 4 bytes; `table + 11*4`.
- Control flow: main loop is stages 1..13 (several comments); stage 8 = func_80025A1C, stage 9, stage 13 = func_800342D8, stage 3 = func_80029CBC; modal sub-loop architecture; frame ends at DrawOTag after a VSync call.
- Overlays: `open`, `game`, `end`, `fdat*`; unloading fdat on `open`/`end`; fdat at 0x8019F07C.
- Game-specific: floats/addresses throughout comments; `Recompiled` using; `Entry.Run`; console tag `[KF2]`; `"verdite2"` AppId; `verdite2.png` icon; `Kf2.*` patch types; `Kf2.Settings.PatchSettings`, `Kf2.PrimBuffer.RamSize`, `Kf2.CardIcon.Install`, `Kf2.CrashDump`.
- **Env vars read directly via `Environment.GetEnvironmentVariable` (205):**
`KF2_AGENT, KF2_ANISO, KF2_ANISO_PROBE, KF2_AO, KF2_AO_BIAS, KF2_AO_MAXDEPTH, KF2_AO_NORMALS, KF2_AO_PROBE, KF2_AO_QUALITY, KF2_AO_RADIUS, KF2_AO_SAMPLES, KF2_AO_STRENGTH, KF2_AO_WORLD, KF2_AO_WORLD_PROBE, KF2_AO_WORLD_RADIUS, KF2_AO_WORLD_STRENGTH, KF2_AUDIO_DUMP, KF2_AUDIO_PROBE, KF2_AUTOPAD, KF2_AUTORELOAD, KF2_AUTORELOAD_DELAY, KF2_AUTORELOAD_SLOT, KF2_AUTOSTART, KF2_BLACKPROBE, KF2_BLACKPROBE_OUT, KF2_BLENDORDER, KF2_BLENDORDER_PROBE, KF2_BOOTEXE, KF2_CAMERABLOCK, KF2_CRASHDUMP, KF2_CROSSPROBE, KF2_CULLGRID, KF2_CULLGRID_COMPARE, KF2_CULL_RESCUE_RADIUS, KF2_DRAWCENSUS, KF2_ENDINGEXIT, KF2_ENHANCEDIST, KF2_EVENFOG, KF2_EVENFOG_BLEND, KF2_EVENLIGHT, KF2_FPS, KF2_FPS_GATE, KF2_FPS_LOGIC, KF2_FPS_PROBE, KF2_FRAMEVIEW, KF2_FRAMEVIEW_CAPTURE, KF2_FRAMEVIEW_OUT, KF2_GEARCOMPARE, KF2_GPUWORLD, KF2_GPUWORLD_CENSUS, KF2_GPUWORLD_PROBE, KF2_GPUWORLD_SURFACES, KF2_HITGUARD, KF2_HITPROBE, KF2_LIGHTCENSUS, KF2_LOADPACING, KF2_LOADPACING_PROBE, KF2_LOG, KF2_LOOPPACING, KF2_LOOPPACING_PROBE, KF2_MAP, KF2_MAP_ARROW, KF2_MAP_FLOOR, KF2_MAP_FOG, KF2_MAP_FOG_LOS, KF2_MAP_FOG_PROBE, KF2_MAP_MARKERS, KF2_MAP_MINIMAP, KF2_MAP_PAUSE, KF2_MAP_PROBE, KF2_MAP_SHADE, KF2_MAP_STYLE, KF2_MAP_WALLS, KF2_MENUMOUSE, KF2_MENUMOUSE_PROBE, KF2_MENUPACING, KF2_MENUPACING_PROBE, KF2_MENUWORLD, KF2_MENUWORLD_PROBE, KF2_MESSAGETEXT, KF2_MESSAGETEXT_PROBE, KF2_MESSAGETEXT_TEST, KF2_MIPMAPS, KF2_MODELWALK, KF2_MODELWALK_PROBE, KF2_MODELWALK_SUBMIT, KF2_MODELWALK_WALK, KF2_MOPOSE, KF2_MURK, KF2_MURK_DISTANCE, KF2_NODITHER_PROBE, KF2_PACKETMATCH, KF2_PERPIXEL, KF2_PERPIXEL_PROBE, KF2_PERSPECTIVE, KF2_PERSPECTIVE_FALLBACK, KF2_PERSPECTIVE_PROBE, KF2_PGXP, KF2_PGXP_CACHEW, KF2_PGXP_CPU, KF2_PGXP_CULLING, KF2_PGXP_MEMORY, KF2_PGXP_PROBE, KF2_PGXP_TEXTURE, KF2_PGXP_TOLERANCE, KF2_PGXP_VERTEXCACHE, KF2_PLANAR, KF2_PLANAR_BIAS, KF2_PLANAR_CULL, KF2_PLANAR_FOG, KF2_PLANAR_PROBE, KF2_PLANAR_RIPPLE, KF2_PLANAR_TOLERANCE, KF2_POLYASM, KF2_POLYASM_CLIPPER, KF2_POLYASM_FACING, KF2_POLYASM_LIT, KF2_POLYASM_REJECT, KF2_POLYASM_TRANSFORM, KF2_POLYASM_UNCLIPPED, KF2_POSAUDIO, KF2_POSAUDIO_PROBE, KF2_PREJIT, KF2_PREJIT_PROBE, KF2_PRESENT_PROBE, KF2_PRIMBUF_PROBE, KF2_PROFILE, KF2_PROFILE_FUNCS, KF2_PROFILE_OUT, KF2_PROFILE_SPIKE, KF2_RATECENSUS, KF2_RATECENSUS_OUT, KF2_RATECENSUS_PERIOD, KF2_RATECENSUS_RANGE, KF2_REFLECT_REACH, KF2_REFLECT_REACH_PROBE, KF2_REMASTER, KF2_REMASTER_ATMOS, KF2_REMASTER_LEVEL, KF2_REMASTER_LIGHTS, KF2_REMASTER_PACK, KF2_REMASTER_PROBE, KF2_REMASTER_PROPS, KF2_REMASTER_SHADOWS, KF2_REMASTER_SHADOW_BIAS, KF2_REMASTER_SHADOW_MODELS, KF2_REMASTER_SHADOW_OFFSET, KF2_REMASTER_SHADOW_SIZE, KF2_REMASTER_SHADOW_SOFT, KF2_RENDERDIST, KF2_RENDERDIST_PROBE, KF2_RETAINED, KF2_RETAINED_CUBE, KF2_RETAINED_CUBESIZE, KF2_RETAINED_CULL, KF2_RETAINED_GATE, KF2_RETAINED_LIT, KF2_RETAINED_MIPS, KF2_RETAINED_PLANAR, KF2_RETAINED_PROBE, KF2_REVERB, KF2_SHELL, KF2_SMOOTH, KF2_SMOOTH_ANIM, KF2_SMOOTH_ANIM_PROBE, KF2_SMOOTH_COMPASS, KF2_SMOOTH_FLUID, KF2_SMOOTH_FLUID_PROBE, KF2_SMOOTH_GAUGES, KF2_SMOOTH_OBJECTS, KF2_SMOOTH_OBJECTS_GUARD, KF2_SMOOTH_OBJECTS_PROBE, KF2_SMOOTH_POS, KF2_SMOOTH_PROBE, KF2_SPRITEANIM, KF2_SPRITEANIM_PROBE, KF2_SPU_INTERP, KF2_SSR, KF2_SSR_DISTANCE, KF2_SSR_F0, KF2_SSR_FOGCURVE, KF2_SSR_PROBE, KF2_SSR_RESOLUTION, KF2_SSR_SKY, KF2_SSR_STEPS, KF2_SSR_STRENGTH, KF2_SSR_THICKNESS, KF2_STAGE13, KF2_STAGE13_NEEDLE, KF2_STAGE13_PROBE, KF2_SUBPIXEL, KF2_SUBPIXEL_CULL, KF2_SUBPIXEL_PROBE, KF2_TEXCENSUS, KF2_TEXKEY, KF2_TEXPROBE, KF2_TICKRATE, KF2_TILEWALK, KF2_TILEWALK_CELL, KF2_TILEWALK_PROBE, KF2_TILEWALK_TILE, KF2_TINTHOLD, KF2_TRUECOLOR, KF2_UISCALE, KF2_VIEWCLIP, KF2_VIEWCLIP_PROBE, KF2_VRAMSNAP, KF2_VRAMSNAP_PROBE, KF2_VSYNC, KF2_WAVES, KF2_WAVES_PROBE, KF2_WIDESCREEN, KF2_WIDESCREEN_CULL, KF2_WIDESCREEN_CULL_PROBE, KF2_WIDESCREEN_EFFECTS, KF2_WIDESCREEN_HUD, KF2_WIDESCREEN_PROBE, KF2_ZBUFFER, KF2_ZBUFFER_BIAS, KF2_ZBUFFER_PROBE, KF2_ZBUFFER_SLOPE, KF2_ZBUFFER_SOURCE, KF2_ZBUFFER_THRESHOLD`
- **Env vars named in comments / read inside a patch's own `Configure()` (not a direct read here):** `KF2_ANALOG`, `KF2_ANALOG_TURN`, `KF2_ANALOG_PITCH`, `KF2_ANALOG_MOVE`, `KF2_ANALOG_DEADZONE`, `KF2_ANALOG_CURVE`, `KF2_ANALOG_ACCEL*`, `KF2_ANALOG_INVERTY`, `KF2_ANALOG_INVERTTURN`, `KF2_ANALOG_INVERTSTRAFE`, `KF2_ANALOG_INVERTFWD`, `KF2_ANALOG_PROBE` (Analog.Configure()); `KF2_MOUSE`, `KF2_MOUSE_TURN`, `KF2_MOUSE_LOOK`, `KF2_MOUSE_INVERTY`, `KF2_MOUSE_BUTTONS`, `KF2_MOUSE_KEY` (Mouse.Configure()); `KF2_KEYS` (KeyLayout.Configure); `KF2_PRIMBUF`, `KF2_RAMSIZE`, `KF2_RAM_PROBE` (PrimBuffer.RamSize/Configure); `KF2_GPUWORLD_FOGZ`, `KF2_GPUWORLD_NEAR` (GpuWorld); `KF2_ICON` (CardIcon.Install).
- **Every patch `Install()` call, in Program.cs order:**
  1. `Kf2.UiScale.Install` (:168)
  2. `Kf2.KeyLayout.Install` (:184)
  3. `Kf2.FramePacing.Install` (:238)
  4. `Kf2.TintHold.Install` (:248)
  5. `Kf2.FrameProfiler.Install` (:266)
  6. `Kf2.FrameCapture.Install` (:279)
  7. `Kf2.MenuPacing.Install` (:297)
  8. `Kf2.MenuWorld.Install` (:309)
  9. `Kf2.MessageText.Install` (:320)
  10. `Kf2.LoadPacing.Install` (:343)
  11. `Kf2.SpriteAnim.Install` (:366)
  12. `Kf2.RateCensus.Install` (:383)
  13. `Kf2.FrameSmoothing.Install` (:403)
  14. `Kf2.ObjectSmoothing.Install` (:423)
  15. `Kf2.AnimSmoothing.Install` (:447)
  16. `Kf2.FluidSmoothing.Install` (:462)
  17. `Kf2.LoopPacing.Install` (:513)
  18. `Kf2.EndingHold.Install` (:514)
  19. `Kf2.DrawCensus.Install` (:525)
  20. `Kf2.PacketMatch.Install` (:539)
  21. `Kf2.HitGuard.Install` (:558)
  22. `Kf2.NoDither.Install` (:567)
  23. `Kf2.Perspective.Install` (:597)
  24. `Kf2.Subpixel.Install` (:616)
  25. `Kf2.ZBuffer.Install` (:647)
  26. `Kf2.AmbientOcclusion.Install` (:690)
  27. `Kf2.AoWorld.Install` (:695)
  28. `Kf2.Reflections.Install` (:712)
  29. `Kf2.Murk.Install` (:719)
  30. `Kf2.Waves.Install` (:726)
  31. `Kf2.PlanarWalk.Install` (:742)
  32. `Kf2.RetainedMap.Install` (:758)
  33. `Kf2.GpuWorld.Install` (:772)
  34. `Kf2.GpuWorldCensus.Install` (:775)
  35. `Kf2.Remaster.Host.Install` (:796)
  36. `Kf2.Remaster.TextureCensus.Install` (:801)
  37. `Kf2.Remaster.LightCensus.Install` (:804)
  38. `Kf2.Pgxp.Install` (:834)
  39. `Kf2.TrueColor.Install` (:846)
  40. `Kf2.Anisotropic.Install` (:867)
  41. `Kf2.PerPixelLighting.Install` (:877)
  42. `Kf2.EvenFog.Install` (:889)
  43. `Kf2.AudioQuality.Install` (:897)
  44. `Kf2.AudioProbe.Install` (:900)
  45. `Kf2.PositionalAudio.Install` (:909)
  46. `Kf2.AutoReload.Install` (:967)
  47. `Kf2.Map.Install` (:1033)
  48. `Kf2.MapFog.Install` (:1058)
  49. `Kf2.AutoStart.Install` (:1076)
  50. `Kf2.AgentBeacon.Install` (:1078)
  51. `Kf2.AgentServer.Install` (:1094)
  52. `Kf2.Analog.Install` (:1112)
  53. `Kf2.Mouse.Install` (:1130)
  54. `Kf2.MenuMouse.Install` (:1148)
  55. `Kf2.GearCompare.Install` (:1159)
  56. `Kf2.Widescreen.Install` (:1187)
  57. `Kf2.CullCone.Install` (:1224)
  58. `Kf2.CullGrid.Install` (:1239)
  59. `Kf2.RenderDistance.Install` (:1249)
  60. `Kf2.EnhancementDistance.Install` (:1255)
  61. `Kf2.ReflectionReach.Install` (:1265)
  62. `Kf2.PrimBuffer.Install` (:1278)
  63. `Kf2.PolyAssembler.Install` (:1301)
  64. `Kf2.TileWalk.Install` (:1319)
  65. `Kf2.ModelWalk.Install` (:1339)
  66. `Kf2.MoPose.Install` (:1348)
  67. `Kf2.CameraBlock.Install` (:1363)
  68. `Kf2.Stage13.Install` (:1367)
  69. `Kf2.ViewClip.Install` (:1381)
  70. `Kf2.BootExe.Install` (:1397)
  71. `Kf2.Settings.PatchSettings.Install` (:1404)
  72. `Kf2.Prejit.Install` (:1417)
  73. `Kf2.CrossProbe.Install` (:1425)
  74. `Kf2.BlackProbe.Install` (:1435)
  75. `Kf2.CardIcon.Install` (:1467)
  - `Configure()`-only (no `Install`): `Kf2.TexProbe` (:576), `Kf2.MapMarkers` (:1032), `Kf2.PlanarCull` (:741), `Kf2.CrashDump` (:1444), plus `Kf2.Remaster.Lights.ConfigureShadows` (:790).
- Mod-visible: `Kf2.*` patch classes are in the game assembly; mods compile against it. Root Program.cs itself is not a type.
- Split note: **This file is the seam in the other direction.** Everything from line 1 is KF2-specific. A second game needs its own `Program.cs` with its own `Kf2.*`/`GameName.*` patch set; the launcher does not care what is in `content/src` beyond compiling it. Only `Recompiled.<Assembly>` name and `Entry.Run` are structural.

#### Deception.csproj (117 lines)
- Bucket: B/D (high) -- developer-path equivalent of GameCompile; compiles generated/ + patches/ via SDK globs; can only build with the disc.
- Does: Exe net10.0, AllowUnsafe, Nullable, assembly `KingsField2`, root ns `KingsField2`, icon `packaging/shared/verdite2.ico`, `TieredCompilationQuickJit=false`, ProjectReference `RecompOne.Runtime`. Removes `tools/**`, `mods/**`, `mcp/**`, `Verdite2.Launcher/**`, `dist/**`, `packaging/**` from Compile/EmbeddedResource/None (CS0579/NETSDK1022 traps).
- Game-specific: `AssemblyName=Deception`, RootNamespace, icon.
- Mod-visible: none.
- Split note: must stay in lockstep with GameCompile options (AssemblyName, AllowUnsafe, Nullable, Release, QuickJit).

---

### Scripts / VERSION / .gitignore

#### scripts/setup_tools.sh (144 lines)
- Bucket: B (high) -- builds the vendored RecompOne; fork management is repo-generic.
- Does: `dotnet build tools/RecompOne/RecompOne.Recompiler -c Release` by default; `--signatures` fetches `AutoConfigure/signatures/psyq.json` (15.7 MB, gitignored) via bare fork `origin/master`; `--sync-upstream` creates/uses `tools/RecompOne.git` bare fork, fetches upstream `BlackLabelHQ/RecompOne`, merges `origin/master` into branch `vendored` (three-way), leaves conflicts; `--no-build`.
- Game-specific: none (path `tools/RecompOne`, upstream URL generic).
- Env: none.
- RecompOne deps: builds `RecompOne.Recompiler`. `tools/RecompOne/UPSTREAM` holds merge base commit.

#### scripts/release.sh (44 lines)
- Bucket: B (high) -- bumps `VERSION`, commits `Release v$NEW`, tags annotated `v$NEW -m "Verdite2 v$NEW"`; refuses dirty tree / existing tag / non-semver; does **not** push.
- Game-specific: commit/tag message literal `Verdite2`.
- Env: none.

#### VERSION (1 line)
- Bucket: B (high) -- single version number `0.3.3`. Consumed by: launcher csproj, AppImage script, Windows script, Inno, release.yml tag check, ci.yml.

#### .gitignore (60 lines) -- copyrighted-data protections a new game repo must copy
- `disc/*` with `!disc/README.md` -- never commit disc image/cue.
- `generated/` -- recompiler output derived from disc (also copyrighted).
- `bin/`, `obj/` -- .NET build output.
- `tools/RecompOne.git/` -- fork history, rebuildable.
- `tools/RecompOne/**/bin/`, `tools/RecompOne/**/obj/`.
- `tools/RecompOne/RecompOne.Recompiler/AutoConfigure/signatures/psyq.json` -- PSY-Q signature bank (15.7 MB).
- `*.sav` -- memory card saves (runtime-written).
- `settings.json`, `interface.ini` -- runtime host settings (may embed disc paths).
- `*.fog`, `*.fog.tmp` -- MapFog explored-tile records.
- `packs/`, `exports/` -- runtime asset-replacement packs / editor exports (may hold derived art).
- `dump/` -- upstream texture dumper output / censuses (derived from disc).
- `mods/.cache/` -- ModLoader compiled mod assemblies.
- `ratecensus.txt`, `*.log` -- session diagnostics.
- `dist/`, `dump/`.
- `scratch/`, `__pycache__/`, `*.pyc`.
- **Note:** `packs/` and `exports/` are both ignored (authored/derived assets, not source). A new game repo must copy the disc/, generated/, signature-bank, save/fog, dump/, packs/ and exports/ lines at minimum.

---

### Bucket counts (this slice)

| bucket | files |
|---|---|
| A | 0 |
| B (mechanism, with per-game strings) | launcher 12 sources + csproj + 4 build/packaging scripts + 3 packaging assets/csproj + 2 CI + mcp (2) + setup_tools + release.sh + VERSION ≈ 28 |
| C | 0 standalone here; C/D coupling lives inside root `Program.cs` and inside `Verdite2.Launcher/UpdatePopup.cs` (overlay-name predicate) and launcher Program.cs (assembly/disc names) |
| D | `packaging/shared/verdite2.png`, `verdite2.ico`; root `Program.cs` as a whole |

### Top observations

1. **The launcher is already a reusable shell.** Good seam: `Build/{Paths,BuildKey,DiscCheck,GameCompile,Recompile}.cs` + `Program.cs` contain no KF2 addresses or RAM layouts; per-game differences are ~20 literals (the table at top). A second game = new `content/` + a data record.
2. **Root `Program.cs` is the opposite: wholly D/C.** 75 `Install()` calls, ~205 `KF2_*` env reads, per-overlay IRQ tables and addresses. It is shipped as `content/src/Program.cs` but is not part of the shell; each game needs its own.
3. **`GameCompile` and `Deception.csproj` must move together.** Same AssemblyName + options; a drift is a release-only bug (fast game, no crash).
4. **`BuildKey` deliberately excludes LBAs and commit**, but DOES include the game-specific disc-file list; a new game with different executable names / no `FDAT.T` must edit it.
5. **`DiscCheck` is pure per-game data** behind a generic validator (serial, boot EXE, required files + floors, wrong-serial message, config name `kf2.json`).
6. **`UpdatePopup` leaks the overlay naming convention** (`open`/`game`/`end`/`fdat*`) into a generic UI component -- the one C-coupling hidden in the launcher.
7. **`Paths.Prepare()` chdir is the whole file-placement strategy**; it depends on the runtime addressing `settings.json`, `interface.ini`, `carda.sav`, `carda.fog`, `mods/` relatively. Any shared shell must keep that contract.
8. **Packaging/CI are disc-agnostic by construction** (that is why release can run in CI); they carry only naming strings and the release body's SLUS warning.
9. **MCP server is a pure protocol adapter**; the only game-specific parts are the shell verb vocabulary and unit comments.
10. **`.gitignore`'s copyright protections** are the reusable checklist (disc/, generated/, signatures, saves, fog, dumps, packs/, exports/); `packs/` and `exports/` are easy to miss and can contain derived assets.

## tools/RecompOne: what the fork knows about this game
Scope: the vendored `tools/RecompOne` tree (`bin/`, `obj/` excluded). Read-only.
Question: what is KF2-specific that a sibling King's Field would trip over.

Method: `Environment.GetEnvironmentVariable` grep; `KF2_`/King's Field/SLUS greps
split into code vs comments; `0x80xxxxxx` grep; read of every class the brief named;
`git --git-dir=tools/RecompOne.git diff d81dec8` (the merge base) to separate the
fork's additions from upstream.

The header: the whole fork is generic. There is **almost no KF2 data in executable
runtime code** — no KF2 RAM address, no KF2 overlay name, no game-id literal. The
KF2 coupling in bucket A is concentrated in three places: (a) a hardcoded `KF2_`
env-var prefix, (b) diagnostic print labels, and (c) *defaults and layout constants*
in the retained/reflection/ordering-table mechanisms that assume this game's renderer
shape (slot 1 = map, slot 0 = sky, 0x2000-entry table, 0xF0 tile bias, 2048-unit tile,
80×80 tile grid, 64 light records, blend 0/3 = water). Those constants are wired to
values the port sets at run time, but the runtime's own defaults/decoders hardcode them.

---

### 1. Every environment variable the RecompOne tree reads

All seven are direct `Environment.GetEnvironmentVariable("…")` calls, evaluated once at
static init, with the `KF2_` prefix **hardcoded as a string literal**. There is no
helper and no game-supplied prefix.

| file:line | name | default / semantics |
|---|---|---|
| `RecompOne.Runtime/Gpu/Backends/Common/VramCheck.cs:12` | `KF2_VRAMCHECK` | `== "1"` arms the CPU VRAM mirror |
| `RecompOne.Runtime/Hardware/Gte.cs:447` | `KF2_GTE_FAST` | `!= "0"` (on) selects the specialized GTE lighting path |
| `RecompOne.Runtime/Hardware/Gte.cs:448` | `KF2_GTE_LIGHTCACHE` | `!= "0"` (on) caches the per-normal light product |
| `RecompOne.Runtime/Host/Window/GlDebug.cs:16` | `KF2_GLDEBUG` | int level |
| `RecompOne.Runtime/Host/Window/HostWindow.cs:418` | `KF2_SWAP` | `interval`/`immediate`/`vblank` |
| `RecompOne.Runtime/Memory/PSMemory.cs:161` | `KF2_CDTRACE` | `== "1"` stack-trace first CD access |
| `RecompOne.Runtime/Memory/RamProbe.cs:13` | `KF2_RAM_PROBE` | non-`"0"` arms a RAM access monitor |

Suggestion: **parameterise the prefix** (game supplies it, e.g. an assembly-level
`GameEnv.Prefix`) or leave as is but document that bucket A's switches are namespaced
`KF2_` and a sibling game's port would read `KF3_`/whatever. Every one of the seven is
a diagnostic or comparison switch, so a sibling can simply not set them.

**Comment-only `KF2_` reads (executed by the port, not by the runtime).** These appear
in runtime doc comments and would mislead a sibling reader into thinking the runtime
reads them: `KF2_BLENDORDER_PROBE` (`Gpu/BlendOrder.cs:95`), `KF2_AO_PROBE`
(`Gpu/GteDepth.cs:405,413`), `KF2_ANISO_PROBE` (`Gpu/GteDepth.cs:649`),
`KF2_PERSPECTIVE_FALLBACK` (`Gpu/GteDepth.cs:581`, `Gpu/GpuRaster.cs:129`),
`KF2_PLANAR_FOG` (`Gpu/PlanarReflections.cs:74`), `KF2_AUDIO_DUMP`
(`Hardware/SpuReverb.cs:13`), `KF2_POLYASM` (`Hardware/Gte.cs:92`),
`KF2_VRAMSNAP` (`Gpu/Backends/Common/GlVram.cs:12,15`), `KF2_ZBUFFER_PROBE`
(`Gpu/Backends/Common/GlDisplayRt.cs:14`), `KF2_PRESENT_PROBE`
(`Gpu/Hle/GpuHle.cs:16,20`, `Gpu/Backends/Common/GlCore.cs:2312`),
`KF2_PGXP*` (`Host/Window/Settings/Sections/DisplaySettingsSection.cs:94`),
`KF2_VSYNC` (`sdk/LibEtc.cs:29`), `KF2_FPS` (`sdk/LibEtc.cs:47`,
`sdk/LibCdStream.cs:262`), `KF2_MURK_TILT` (`Gpu/WaterMurk.cs` via docs),
`KF2_LIGHTCENSUS`/`KF2_TEXCENSUS`/`KF2_REFLECT_REACH`/`KF2_RETAINED` (remaster files
are in `patches/`, not bucket A). Suggest rewriting these to a neutral "the port's
switch" wording or moving the concrete name into `docs/`.

---

### 2. `KingsField` / `KF2` / `Verdite` / `SLUS` references — code vs comments

#### Executable code
- The seven env literals in §1 (all literal `KF2_`).
- `RecompOne.Runtime/Gpu/Backends/Common/GlTexCache.cs:260` — `Console.WriteLine("[KF2] mip verify: …")` diagnostic label.
- `RecompOne.Runtime/Gpu/ScreenReflections.cs:165` — `sb.Append("[KF2] reflections:  ")` diagnostic label.
- `RecompOne.Runtime/sdk/LibGpu.cs:334-337` — inspects `AssetApi.GameId` for the
  European region prefixes `SCES`/`SLES`/`SCED`/`SLED`. **Not** a KF2 reference; it is
  generic PS1 region detection and correctly does not name `SLUS`. Leave as is.
- No executable reference to `KingsField`, `Verdite`, `SLUS`, `SLPS`, or `SLES-…`
  anywhere else in bucket A. (The game name/overlays/addresses live in
  `config/kf2.json` and `patches/`, outside bucket A.)

#### Comments / doc-comments (would mislead a sibling reader)
King's Field named as the motivating game:
- `RecompOne.Runtime/Bios/BiosA.cs:708` — "King's Field's boot stub compares the result against 1".
- `RecompOne.Runtime/Bios/BiosB.cs:326` — "King's Field does exactly that: every screen change begins with …".
- `RecompOne.Runtime/Hardware/Interrupts.cs:282` — "for King's Field it lands in game data".
- `RecompOne.Runtime/sdk/LibCd.cs:36` — "King's Field's is one".
- `RecompOne.Runtime/Gpu/GteDepth.cs:167` — "King's Field draws one world and a 2D HUD".
- `RecompOne.Runtime/Gpu/GteVertexMap.cs:28` — "King's Field projects a whole vertex list into an 8-byte-per-vertex scratch array".

KF2 function addresses named in doc comments (all `func_800xxxxx`, no code use):
- `RecompOne.Runtime/Diagnostics/Profiler.cs:124` — `func_80060818@game` (example section name).
- `RecompOne.Runtime/Gpu/Backends/Common/GlShaders.cs:685,688` — `func_8002F918` (sky), `func_80030540` (near-camera object).
- `RecompOne.Runtime/Gpu/Backends/Common/GlShaders.cs:1931` — `func_8002DC78` (scrolling-texture upload).
- `RecompOne.Runtime/Gpu/GteDepth.cs:654` — `func_8002DC78`.
- `RecompOne.Runtime/Gpu/RetainedScene.cs:805,810,912,936` — `func_8002F918`, `func_80030540`, `func_80032AC4` (kind 0xF0 sky), `func_80032400` (first-person arm).

Port-specific `Kf2`-type/API name in a comment:
- `RecompOne.Runtime/Gpu/GteDepth.cs:601` — "Clamped to 1..16 by `Kf2.Anisotropic`" (the actual clamp lives in the port).

Recompiler comment naming a KF2 address:
- `RecompOne.Recompiler/CodeGen/OverlayWriter.cs:49-54` — the `config.PointerScan`
  gate is explained with "collides 0x80025D38 across all three" (open/game/end share a
  base). The *behaviour* is generic (don't scan pointers across mutually-exclusive
  overlays); only the example address is KF2's.

Suggestion: leave the mechanism comments but replace "King's Field"/`func_…`/`Kf2.`
with neutral wording ("a sibling port", "the game's X routine") so a sibling reader
does not read this game's addresses as contract. The `GlTexCache`/`ScreenReflections`
`[KF2]` print labels: **move the literal behind the same parameterised prefix** as §1
or drop it.

---

### 3. Hardcoded PS1 RAM addresses and other game data constants

The range the brief asked about, `0x8001_0000`–`0x801F_FFFF`: **nothing hardcoded in
executable code.** Only:
- `RecompOne.Runtime/Dispatch/Dispatcher.cs:233` — `0x80000000` (KSEG0 base, hardware).
- `RecompOne.Recompiler/Psx/SystemCfg.cs:11` — `Stack = 0x801FFF00` default (BIOS
  convention; overwritten from SYSTEM.CNF). Leave as is.
- `RecompOne.Runtime/Memory/MemoryMap.cs:25` — `Kseg0Base = 0x80000000` (hardware).
- KSEG bit masks `0x80000000`/`0x1FFFFFFF` throughout the GTE/GPU maps (hardware).
- `RecompOne.Recompiler/CodeGen/OverlayWriter.cs:53` — `0x80025D38` **in a comment only**.

Guest addresses that are hardcoded below `0x8001_0000` (corrected on review: these
are **not** this game's link layout):
- `RecompOne.Runtime/sdk/LibDs.cs:37-39` -- `ParamAddr = 0x8000F810`,
  `CallAddr = 0x8000F818`, `TextAddr = 0x8000F830`; and
  `RecompOne.Runtime/sdk/LibCd.cs:531` -- `ResultAddr = 0x8000F800`. These are the
  HLE's own scratch slots in kernel RAM, below where any executable loads, and they
  are upstream's: the same constants are in upstream `d81dec8`. Leave as is, unless
  a sibling game turns out to use that kernel area itself.

No other game data constant (no table base, stride or record count from KF2 RAM) is
hardcoded in runtime code; those live in `patches/`. Leave as is.

---

### 4. Game-renderer assumptions baked into the port's runtime classes

The mechanism is generic in each case; what is *not* generic is a default value, a
decoder layout, or an ordering-table position that only holds for this game. These are
the sibling-game trip hazards.

#### `RecompOne.Runtime/Gpu/GteDepth.cs` (1206 lines) — Bucket A, mostly generic
- Does: perspective/sub-pixel/G-buffer depth bookkeeping and probes.
- `GteDepth.cs:279` `AoRadius = 512f` — a quarter of this game's 2048-unit tile.
- `GteDepth.cs:304` `AoMaxDepth = 24000f` — this game's fog range.
- `GteDepth.cs:318` `AoWorldStrength / AoWorldRadius = 0.6f / 3072f`.
- `GteDepth.cs:341` `AoHeightSpan = 80, AoTileUnits = 2048` — **the game's 80×80 tile
  grid and 2048-unit tile are compile-time constants of the runtime**, even though the
  grid is filled by the port. A sibling with a different grid must edit this.
- `GteDepth.cs:358` `AoWallHeight = 2048f`.
- `GteDepth.cs:375` `ProjH = 320f, ProjCx = 160f, ProjCy = 120f` — defaults only; the
  real values are published from `Gte.Rtp` (`Gte.cs:776`, `NoteProjection`). Leave.
- `GteDepth.cs:662` `FluidSlots = 8` — this game's scrolling-texture slot count
  (water/slime/fire). A sibling with more/fewer slots would need this changed.
- `GteDepth.cs:828` `CensusBigArea = 1500` — tuned "in pixels of the 320×240 the game
  draws".
- `GteDepth.cs:180` `DepthClearThreshold` default `0`, explicitly because "King's Field
  draws one world and a 2D HUD" (`:167`). Deliberate game-specific default.
- Suggestion: `parameterise` the grid/tile/slot constants (they are already port-fed
  data, just stored as `const`); leave the tuning numbers as defaults.

#### `RecompOne.Runtime/Gpu/GteVertexMap.cs` (310 lines) — Bucket A, generic
- Does: exact vertex→packet attribute association by RAM word address.
- No game constant. Only the doc comment names King's Field (`:28`). Leave as is.

#### `RecompOne.Runtime/Gpu/GteLightMap.cs` (138 lines) — Bucket A, generic
- Does: per-pixel lighting inputs keyed by packet address. GTE hardware constants
  (`2848`/`3232`, BK/LCM regs), `GenRing = 64`. No game data. Leave as is.

#### `RecompOne.Runtime/Gpu/GtePacketDepth.cs` (97 lines) — Bucket A, generic
- Does: per-packet corner depths for the depth buffer. Address-range indexed
  (`& 0x1FFFFFFF`, hardware). Comment names "map tiles / models" (`:12-14`). Leave.

#### `RecompOne.Runtime/Gpu/RetainedScene.cs` (1048 lines) — Bucket A, generic with KF2 layout defaults
- Does: the world kept in world space for reflections/shadows/main view.
- `RetainedScene.cs:72` `RecordInts = 52, RecordCount = 64` — **the game's 64 light
  records, 52 ints each** (0085). Compile-time.
- `RetainedScene.cs:139` `HalvesW = 160, HalvesH = 80` — **the game's halves grid**
  (`(tile Z*80+tile X)*2+upper`), also at `:153`, `:600`, `:622`.
- `RetainedScene.cs:299` `ChunkTiles = 8, ChunkSide = 80 / ChunkTiles, Chunks = …` and
  `:300` `ChunkUnits = ChunkTiles * 2048f` — **80-tile grid and 2048 tile baked in**.
- `RetainedScene.cs:402-414` `BlackQuotient`/`FogKeep` use the GTE depth-cue curve
  constants (`2848`/`3232`/`800`/`2800`/`5600`) — hardware, generic.
- `RetainedScene.cs:480` `Swell = new float[12]` — assumes the port's 12-float swell
  encoding (port's own convention, not the game's).
- `RetainedScene.cs:963` `ArmSlot = 0x1FFF - runKey[0]` — **assumes a 0x2000-entry
  ordering table** (the game's).
- Comments naming `func_8002F918`/`func_80030540`/`func_80032AC4`/`func_80032400`
  (`:805,810,912,936`).
- Suggestion: **parameterise** the grid (`80`, `160`, `HalvesW/H`), the light-record
  shape (`64×52`), the tile unit (`2048`) and the table length (`0x2000`); the rest
  (flags, ring, arrays) is the port's own and can stay.

#### `RecompOne.Runtime/Gpu/PlanarReflections.cs` (232 lines) — Bucket A, generic
- Does: mirrored-camera planar reflections; port drives the capture.
- `PlanarReflections.cs:79` `Tolerance = 48f`, `:83` `Ripple = 4f`,
  `:121`/`:167` `Bins = 16` and world-Y band `y / 16f` — tuning in this game's world
  units (a tile 2048). Defaults only.
- `PlanarReflections.cs:74` comment names `KF2_PLANAR_FOG`.
- Suggestion: leave as defaults, or surface them as game-supplied settings.

#### `RecompOne.Runtime/Gpu/WaterWaves.cs` (56 lines) — Bucket A, generic
- Does: uniform values for the ripple shader term; the port drives the field.
- `WaterWaves.cs:21` `MaxRects = 8` — must match `GteDepth.FluidSlots` (this game's 8).
- `WaterWaves.cs:48` `Distort = 139f, Scale = 700f, Shade = 0.51f` — tuned to "a water
  texel is 32 / a tile is 2048" (`:46-47`). Defaults.
- Suggestion: leave (tuning), but `parameterise` `MaxRects` off the fluid slot count.

#### `RecompOne.Runtime/Gpu/BlendOrder.cs` (167 lines) — Bucket A, generic
- Does: opaque-before-translucent reordering for the depth buffer.
- `BlendOrder.cs:47` queue cap `256`; probe grid `:100`
  `GridX0=-256,GridY0=-64,GridW=256,GridH=96` (a 320×240-picture probe). Mechanical.
- Comment `:12-14` describes the game's table biases ("map tiles 240 entries back", a
  creature's small bias) as the motivation. Leave mechanism; neutralise comment.

#### `RecompOne.Runtime/Gpu/SurfaceMaterial.cs` (197 lines) — Bucket A, generic with water encoding
- Does: per-pixel material id for reflection/lighting passes.
- `SurfaceMaterial.cs:180` `bool averaging = semi && (blend == 0 || blend == 3);` — **the
  blend modes assumed to be water's** (PSX average). Comment `:131-133` says the same
  slots also hold "the main-hall fire (additive) and the creatures' skins" — game-specific
  semantics of the port's own texture rects.
- `SurfaceMaterial.cs:136` `RectSlots = 16`, `:35` `Count = 256`, veil constants — port
  encoding, generic.
- Suggestion: the id/veil encoding is generic (leave); the `blend == 0 || blend == 3`
  water heuristic should be **game-supplied** (the port already publishes the rects and
  could publish the averaging blend modes too).

#### `RecompOne.Runtime/Gpu/RemasterUniforms.cs` (140 lines) — Bucket A, generic
- Does: uniform block for authored lights/fog/shadows. `MaxLights=16`, `MaxShadows=4`,
  `ShadowSize=1024`, `ShadowOffset/Bias/Soft` — defaults. No game data. Leave as is.

#### `RecompOne.Runtime/sdk/LibGpu.cs` — `WalkOTag` (637 lines) — Bucket A, **encodes the game's ordering table**
- `LibGpu.cs:79-92` — the retained map is drawn when the walk reaches **`slot == 1`**,
  i.e. the map is assumed to be linked one past the skybox. This is the game's table
  layout, not a hardware fact.
- `LibGpu.cs:107` — comment and code treat **slot 0 as the skybox** and never test it.
- `LibGpu.cs:196` `private static float WaterCut(int slot) => 4f * (0x1FFF - slot - 0xF0);`
  with doc `:193-195` — **0x2000-entry table, a tile linked at its mean SZ over four
  plus 0xF0**. Straight out of this game's `func_80031C94`/`PolyAssembler`.
- Suggestion: **parameterise** (the port supplies table length, map slot, sky slot,
  tile bias and the /4 SZ divisor). These are the single clearest "constant that
  encodes this game's renderer" in bucket A.

#### `RecompOne.Runtime/Gpu/Backends/Common/GlMainView.cs` (Bucket A) — same encoding, backend side
- `GlMainView.cs:706` `const int TileBias = 0xF0;` — duplicates `WaterCut`'s bias.
- `GlMainView.cs:843` `const int SortBuckets = 0x2000;` — the game's table length.
- `GlMainView.cs:910` `_wKey = Math.Clamp((int)(z * 0.25f), …)` — "mean view depth over
  four", i.e. the game's OTZ.
- Suggestion: **parameterise** together with `LibGpu.WaterCut`.

#### `RecompOne.Runtime/Gpu/Backends/Common/GlModelMeshes.cs` / `GlRetained.cs`
- `GlModelMeshes.cs:531` `if (last < f.ArmRuns) RetainedScene.ArmSlot = 0x1FFF - f.ArmRunKey[last];`
  — 0x2000 table again.
- `GlRetained.cs:464` `float oldReach = Math.Max(ScreenReflections.March(), 4096f) + 2048f;`
  — 2048-unit tile again.
- Suggestion: **parameterise** with the table/tile constants above.

#### `RecompOne.Runtime/sdk/LibEtc.cs` — vblank timeline (234 lines) — Bucket A, generic
- Does: wall-clock 60 Hz vblank timeline for a port that presents from the game's VSync.
- `LibEtc.cs:55` `VBlankMs = 1000.0 / 60.0`; `:61` `HblankHz` 15625/15734; `:59`
  `MaxCatchUpVBlanks = 120` — PS1 hardware. `:29` comment names `KF2_VSYNC`.
- Control-flow assumption: the game calls `VSync` as its frame boundary; generic to
  any port that uses the same override shape.
- Suggestion: leave as is.

#### `Hardware/Gte.cs` — GTE fast path (1494 lines) — Bucket A, generic
- `Gte.cs:440-448` comment "The forms **every caller in this game uses** —
  `NormalColorDpq(3)`… `NormalColorCol`… `DpqColor`… `RotTrans`" plus
  `FastLighting`/`LightCache` env switches. The specialization is chosen by the port
  through the params, not by a KF2 address, but the switch defaults (`!= "0"`, on)
  assume this game's call mix. Suggestion: leave; neutralise comment.

#### Assets — port-added texture keys (Bucket A, generic)
- `RecompOne.Runtime/Assets/Textures/TextureResolver.cs`:
  - `KeyOnFaceRect = true` and `KeyOnUpload = true` defaults;
  - `UploadSlop = 2`, comment "this game's faces read a texel past their texture's
    edge, so one 64x64 tile had three keys";
  - `Scroll`/`ScrollLookup` delegate (port supplies the scrolling-texture policy).
- `RecompOne.Runtime/Assets/Textures/VramTracker.cs` `NoteUpload`, comment "this game
  splits a 128x128 sheet into 100 rows and 28".
- Suggestion: leave the mechanism; `parameterise` the defaults (`UploadSlop`,
  `KeyOnUpload`) so a sibling can switch them off without editing the runtime.

#### Settings sections (see §5) — port-added, generic
- `RecompOne.Runtime/Host/Window/Settings/Sections/DisplaySettingsSection.cs:82-94`
  removes upstream's frame-rate slider and PGXP block on purpose and points at
  `patches/settings/FramePacingPage.cs` and `KF2_PGXP*`. This is a port decision, not a
  KF2 data assumption, but a sibling that *does* want upstream's controls must
  re-add them. Suggestion: leave (document it) or gate the removal.

---

### 5. Settings keys / localisation keys the port added to the runtime's own sections

All additions to the runtime's own settings UI are **generic** — none name KF2.

- `RecompOne.Runtime/Config/ViewConfig.cs:114-118` — new persisted key `"Borderless"`
  (`ViewConfig.Borderless`), consumed by `HostWindow.SetFullscreen`.
- `RecompOne.Runtime/Host/Window/Settings/SettingsRegistry.cs:49-52` —
  `DrawSlot(string slotId)` extension point (lets a port draw a control *inside* a
  section, e.g. aspect ratio beside render scale). Generic seam.
- `RecompOne.Runtime/Host/Window/Settings/Sections/DisplaySettingsSection.cs:67` —
  `SettingsRegistry.DrawSlot("display.render_scale")` call; `:18-31` the windowed /
  fullscreen / borderless mode combo.
- `RecompOne.Runtime/Host/Window/MainMenuBar.cs:69-83,101-118` —
  `AddRightItem(width, draw)` extension point and `DrawRight`; generic.
- `RecompOne.Runtime/Host/Window/Assets/languages.json` — keys added by the port:
  - `settings.display.mode`
  - `settings.display.mode.windowed`
  - `settings.display.mode.borderless`
  - `settings.display.mode_hint`
  (all six languages each; no KF2 key added to the runtime's own file).
- The port also **removed** upstream's CJK font asset
  (`Host/Window/Assets/NotoSansCJK-Regular.otf`, 16 MB) — an asset change, generic.

The port's own KF2 settings keys/pages (`Video`, `Gameplay`, `Controles`, remaster,
`patch.*` keys) are registered from `patches/settings/*` against the runtime — that is
**bucket B/C**, not bucket A. From bucket A's side the only contract a sibling needs is
that `SettingsRegistry.Extend` appends and `DrawSlot` inserts; both are generic.

---

### Per-file bucket labels (the brief's format, condensed)

Non-obvious buckets only; `GteVertexMap`, `GteLightMap`, `GtePacketDepth`,
`RemasterUniforms`, `FrameClock`, `Profiler`, `InputManager` additions are **A (high)**.

```
Gpu/GteDepth.cs          A (high)  generic mechanism; KF2 grid/tile/slot + tuning defaults
Gpu/RetainedScene.cs     A (high)  generic store; KF2 80x80 grid, 64x52 records, 0x2000 table
Gpu/PlanarReflections.cs A (high)  generic, port-driven; KF2-scale tuning defaults
Gpu/WaterWaves.cs        A (high)  generic; 8 must match FluidSlots; tuning defaults
Gpu/BlendOrder.cs        A (high)  generic; probe grid sized to the 320x240 picture
Gpu/SurfaceMaterial.cs   A (high)  generic; blend 0/3 assumed to be water
Gpu/ScreenReflections.cs A (high)  generic; [KF2] print label
Gpu/Backends/.../Gl*.cs  A (high)  generic renderer; TileBias 0xF0 / SortBuckets 0x2000 / ArmSlot 0x1FFF
sdk/LibGpu.cs (WalkOTag) A (high)  generic walk; slot1=map, slot0=sky, WaterCut table layout
sdk/LibEtc.cs            A (high)  generic 60 Hz vblank timeline
sdk/LibDs.cs             A (med)   HLE libds; 0x8000F8xx are upstream's kernel-RAM scratch slots
sdk/LibCd.cs             A (med)   HLE libcd; 0x8000F800 is upstream's kernel-RAM scratch slot
Memory/PSMemory.cs       A (high)  generic fast path; KF2_CDTRACE literal
Memory/RamProbe.cs       A (high)  generic; KF2_RAM_PROBE literal
Hardware/Gte.cs          A (high)  generic GTE; KF2_GTE_FAST/LIGHTCACHE literals; comment names KF2 call mix
Assets/Textures/*        A (high)  generic texture keys; KF2-tuned UploadSlop/defaults
Recompiler/OverlayWriter A (high)  generic cross-overlay fix; comment names 0x80025D38
Recompiler/SdkPatches    A (high)  generic libapi DMACallback binding
Host/Window/...          A (high)  generic settings/menu; KF2_SWAP/KF2_GLDEBUG literals; borderless keys
```

---

### Summary

- **Buckets in bucket A:** ~all A (generic runtime) at high confidence. **No C/D
  code** in `tools/RecompOne`: every KF2 RAM address, overlay name and game routine
  lives in `config/` or `patches/`. A handful of **A/B seams** (the ordering-table and
  tile-grid constants a sibling must re-supply).
- **Env vars:** exactly **7** read by the runtime, all literal `KF2_`-prefixed strings,
  no helper/prefix indirection (§1). Plus ~15 more `KF2_` names that appear only in
  comments while the *port* reads them — a documentation hazard, not behaviour.
- **Names in code:** no `KingsField`/`Verdite`/`SLUS` in executable code. `KF2` in
  executable code is limited to the 7 env literals and 2 diagnostic print labels. All
  King's Field / `func_800…` / `Kf2.` references are in comments (§2).
- **RAM addresses:** nothing in `0x8001_0000`–`0x801F_FFFF` is hardcoded in runtime
  code (only KSEG masks and the SYSTEM.CNF stack default). `LibDs` `0x8000F810/18/30`
  and `LibCd` `0x8000F800` are upstream's HLE scratch slots in kernel RAM, not the
  game's (§3, corrected on review).
- **Game-renderer assumptions (§4), in priority order for a sibling:**
  1. `LibGpu.WalkOTag`: **slot 1 = map, slot 0 = sky, `WaterCut = 4*(0x1FFF-slot-0xF0)`**
     (`LibGpu.cs:79,107,196`) — parameterise.
  2. `RetainedScene`: **`HalvesW/H=160/80`, `ChunkSide=80/8`, `2048` tile,
     `RecordCount=64`×`RecordInts=52`, `ArmSlot=0x1FFF-…`** (`:139,299,300,72,963`) —
     parameterise.
  3. `GlMainView`: **`TileBias=0xF0`, `SortBuckets=0x2000`, `z*0.25f`** (`:706,843,910`)
     and `GlModelMeshes:531` `0x1FFF` — parameterise with (1).
  4. `GteDepth`: **`AoHeightSpan=80`, `AoTileUnits=2048`, `FluidSlots=8`,
     `DepthClearThreshold=0`** (`:341,662,180`) — parameterise the first three, keep the
     last as a documented default.
  5. `SurfaceMaterial`: **blend `0 || 3` = water** (`:180`) — game-supplied.
  6. `WaterWaves.MaxRects=8` must track `FluidSlots` (`:21`) — one parameter.
- **Settings/localisation:** the port added only generic keys to the runtime's own
  sections — `ViewConfig.Borderless`, the `DrawSlot`/`AddRightItem` extension points,
  and four `settings.display.mode*` strings. **No KF2 key entered the runtime's
  `languages.json`**; the KF2 pages are bucket B/C.
- **Overall:** bucket A is close to shareable as-is. The work to make it share is
  mechanical and small: (a) one parameterised env prefix, (b) two `[KF2]` print
  labels, and (c) one bundle of
  ordering-table/tile-grid constants (items 1–6 above). Everything else is generic
  mechanism with tuning defaults.
