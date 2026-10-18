# Recompilation: config, function maps and SDK addresses

How MIPS becomes C# here, and everything that goes wrong on the way. Build and
run commands are in [DEVELOPMENT.md](DEVELOPMENT.md); the patches to the
recompiler and runtime checkout are in [RUNTIME.md](RUNTIME.md).

The disc holds a 4 KiB boot stub plus three real executables — `OPEN.EXE`,
`GAME.EXE`, `END.EXE` — which all load at `0x80011000` and are mutually
exclusive, so they are declared as **overlays** in `config/kf2.json`. Because
they share an address range, *every* address-based config entry must name its
overlay explicitly; prefer a named overlay over `"*"`.

Watch the namespace: generated code is `Recompiled.KingsField2`, a *class* named
after the project, which shadows any namespace called `KingsField2`.

## Two traps worth knowing

**1. Number bases are inconsistent between the JSON config and the CLI.**

| | `base` | `size` / `skip` / `offset` | `lba` |
|---|---|---|---|
| `config/kf2.json` | hex **string** `"0x80011000"` | decimal JSON numbers | decimal |
| `--generate-function-file` | hex | **hex** | decimal |

So the same value is `"skip": 2048` in the config but `-skip 800` on the command
line. `inspect_disc.py` prints both decimal and hex columns for this reason.

**2. Overlay files are read as raw bytes.** `ResolveOverlay` does *not* parse the
PS-X EXE header — it slices the file from `offset + skip`. Every overlay pointing
at a `.EXE` therefore needs `"skip": 2048` to step over the 0x800-byte header.
(The *boot* executable is different: it goes through `Psx/Parser.cs`, which
strips the header itself.)

## Two patches change the generated code, and only two

Most of `tools/RecompOne/patches/` is runtime: change it, rebuild the checkout, run.
**Two patches change what the recompiler *emits*, so a stale `generated/` is a
silent wrong answer rather than a build error.** Re-run the recompiler after
touching either, and after any `setup_tools.sh` that reports them newly applied:

- `0004-libapi-dma-callbacks.patch` — `SdkPatches` gains an entry, so which calls
  bind to the runtime's HLE changes.
- `0035-pgxp-cpu-hooks.patch` — `InstructionEmitter` appends
  `if (Pgxp.CpuTracking) PgxpCpu.X(...)` to every load, store, move, shift, add,
  multiply and divide. Measured: 68,188 sites in `game.cs`, no change in line
  count (they append to existing lines), and the release build 15 s → 37 s. The
  emitted gate is a static bool, so with PGXP off the game still measures 144.0 fps
  at 20.0 ticks/s — an unrecompiled tree does not run *slower*, it runs with PGXP
  permanently unable to answer, which the probe reports as a coverage collapse to
  the vertex cache rather than as an error.

```bash
dotnet run --project tools/RecompOne/RecompOne.Recompiler -c Release --no-build -- config/kf2.json
```

## Generating function maps

There is no King's Field decompilation producing an ELF + `.map`, so the
`-elf`/`-map` path is unavailable and we use a linear sweep. Upstream is blunt
about the tradeoff — a sweep "can and WILL get some data as code"
(`ConfigLoader.cs`) — so expect to correct these by hand as bad functions surface.

```bash
RC="dotnet run --project tools/RecompOne/RecompOne.Recompiler -c Release --no-build --"

$RC --generate-function-file -linear-sweep -disc disc/KingsField2.cue \
    -file SLUS_001.58 -base 80010000 -skip 800 -out config/funcmaps/main.json
$RC --generate-function-file -linear-sweep -disc disc/KingsField2.cue \
    -file OPEN.EXE     -base 80011000 -skip 800 -out config/funcmaps/open.json
$RC --generate-function-file -linear-sweep -disc disc/KingsField2.cue \
    -file GAME.EXE     -base 80011000 -skip 800 -out config/funcmaps/game.json
$RC --generate-function-file -linear-sweep -disc disc/KingsField2.cue \
    -file END.EXE      -base 80011000 -skip 800 -out config/funcmaps/end.json
```

Current yield: main 9, open 515, game 1103, end 471. The sweep also resolves jump
tables (1937 entries in `game` alone). Every function map entry requires a `size`
or the loader throws (`FunctionMapLoader.cs`).

## Fixing bad output

When a function misbehaves, turn on `debug`, `addressComments` and
`disasmComments` in the config to annotate the generated C# with source addresses
and disassembly. They inflate the output a lot, so keep them off otherwise.

Two escape hatches, both per-function and both overlay-aware:

- `stubs[]` — replace a function with a no-op. Good for hardware pokes and
  copy-protection paths with no native meaning.
- `patches[]` — point a function at hand-written C#. Takes `function` or
  `address`, a `target`, and `mode` (default `replace`). The `overlay` field
  accepts a single name, a list, or `"*"`. Since `open`/`game`/`end` overlap in
  address space, prefer naming the overlay explicitly over `"*"`.

Hand-written replacements live in `patches/`.

## GAME.EXE loads code

`GAME.EXE` is not the whole game. Per-area logic is **MIPS code linked to run at
`0x8019F07C` and loaded off the disc into RAM at run time**. The port died on it
with `unmapped call: 0x8019F1C8` the moment the first area came up.

`CD/COM/FDAT.T` is an archive: `u16` count, then a `u16` table of start sectors
in 2048-byte units. Entries run in groups of three per area — ~66 KB of data,
~28 KB of data, then a 4–8 KB code module — so the code lives at entries `3n+2`.
Each module is a table of function pointers (32 slots and up) followed by the
functions. `GAME.EXE` holds the current module at `0x8017E068` and dispatches
through it; the crash was slot 8, `(*mod)[8]`. With no module loaded the same
pointer holds `0x80064B64`, a static table of 32 identical pointers to a bare
`jr ra` — which is how the shape of the object was confirmed before any of the
loaded ones were read.

Two independent things pin the load address:

- The pointer table holds **absolute** addresses inside the module, so the file
  is linked for one fixed address. Searching the whole disc image for the crash
  address `0x8019F1C8` returns exactly one hit, inside `FDAT.T`, `0x20` bytes
  into entry 2 — the table slot the crash dispatched through.
- Scoring every candidate base by how many of a module's own `jal` targets land
  on a plausible function start (preceded by `jr ra` + delay slot, or opening
  `addiu sp,sp,-N`) peaks **unanimously** at `0x8019F07C` for all eight area
  modules — the address `GAME.EXE` itself writes into `0x8017E068` — and at
  `0x80193B38` for entry 32, which is a different module family.

| entry | file offset | size | base | LBA |
|---|---|---|---|---|
| 2 | `0x018000` | 4096 | `0x8019F07C` | 457 |
| 5 | `0x031000` | 8192 | `0x8019F07C` | 507 |
| 8 | `0x04B000` | 4096 | `0x8019F07C` | 559 |
| 11 | `0x064000` | 6144 | `0x8019F07C` | 609 |
| 14 | `0x07D000` | 8192 | `0x8019F07C` | 659 |
| 17 | `0x096800` | 6144 | `0x8019F07C` | 710 |
| 20 | `0x0B0000` | 6144 | `0x8019F07C` | 761 |
| 23 | `0x0C8800` | 8192 | `0x8019F07C` | 810 |
| 32 | `0x0E2000` | 6144 | `0x80193B38` | 861 |

Entry 32 is the odd one out and is **dead content** — a cut area the loader cannot
reach, since every code module this game loads goes to `0x8019F07C` and entry 32
is linked for `0x80193B38`. See "fdat32 is a cut area" below before spending any
time on it. (The area index the loader takes, and which areas exist at all, are
in [GAME_INTERNALS.md](GAME_INTERNALS.md).)

They are declared as overlays like any other. `base` is the address of the byte
at `offset + skip`, and `ResolveOverlay` derives the LBA as the archive's LBA
plus the entry's start sector. **That LBA is what arms the swap**: the CD read of
the module's first sector marks the overlay pending (`Dispatcher.LoadByLba`) and
the write that lands it in RAM activates it (`NotifyWrite`) — which only works
because the HLE `CdRead`/`CdGetSector` call `LoadByLba` per sector and write
through `PSMemory`.

The match is that start LBA and nothing else. `Register` stores `LbaStart`, and
`LoadByLba` returns on a miss, so the sector count, the destination and a file
offset never identify a load. `CdRead` also tries `lba+i` for every sector of
the transfer, which means a read that begins inside the file never includes the
key. `LbaStart` is the ISO 9660 extent (`DiscFs.Locate`, plus
`(Offset+Skip)/2048`; a headerless BIN uses skip 0). LibCd keeps the pending
Setloc in the drive position until `CdRead`, `ReadN`, `ReadS`, `Play` or `Seek`
takes it. The XA pacer otherwise advances that position while `_readActive` is
already false: a 203-sector read of `3DMD.BIN` (extent 41756, 416128 bytes) was
logged at 41762, six sectors into the same file, and the slot never went
pending.

Every module now takes `skip: 0` and its own load address as `base`, so the
overlay covers the header table as well as the code. The reason is in "The sweep
splits a switch" below: the words past the header's 32 dispatch slots are the
modules' **switch jump tables**, and the recompiler reads a jump table out of the
overlay's own bytes. Skipping the header hid them. Nothing disassembles the
header — only addresses named in the function map are emitted — so covering it
costs nothing.

Function maps are a linear sweep of each module with the pointer-table targets
and every internal `jal` merged in. Do not skip the merge: the sweep alone
missed real entry points in **five of the nine** modules, and each of those is a
future `unmapped call`. `scripts/add_call_targets.py`'s `merge()` does the
splicing, and re-running the sweep is:

```bash
$RC --generate-function-file -linear-sweep -disc disc/KingsField2.cue \
    -file "CD/COM/FDAT.T" -base 8019F0FC -offset 18000 -skip 80 -size F80 \
    -out config/funcmaps/fdat02.json
```

**Nine is all of them, and the disc has no other code.** Every one of `FDAT.T`'s
70 entries was tested for the shape that identifies a module — a table of
absolute pointers into itself — and exactly the nine above match, at 16 words out
of 16. The group pattern does not break down after entry 23 as previously
suspected; entries 24–29 and 33–46 are simply **empty** (zero length), and 30/31/32
is one more group of the same 66 KB / 28 KB / 6 KB shape. From 47 on the entries
are large asset blobs with no pointer table.

The same test run over every other file on the disc — `RTIM.T`, `RTMD.T`, `MO.T`,
`VAB.T`, `ITEM.T`, `TALK.T`, `FN.D`, `OP.D`, `OPU.D` — finds **zero** module
tables at any sector boundary. `FDAT.T` is the only file that carries code.

The other static source of `unmapped call` is closed too. Scanning all three
executables for words that point into their own text at a plausible function
start (preceded by `jr ra` + delay slot, or opening `addiu sp,sp,-N`) and are not
already a known start returns **0 in all three** — so every indirect dispatch
target that exists statically is mapped, on top of every `jal` target. What can
still surface is an address computed at run time, which nothing static can
predict — and, as the next section covers, an address that is not a function
start at all.

### The sweep splits a switch, and the split crashes

A room off the main hall died with `unmapped call: 0x8019F578`, six frames deep
in `fdat17`. The address is not missing from the map: it is *inside*
`func_8019F564`, 20 bytes past its start. Nothing calls it. `func_8019F374`
**branches** to it — `beq s0, a1, 0x8019F578` — and the recompiler turns a branch
that leaves its own function into a `Dispatcher.Call`, which only knows entry
points.

The real function is `0x8019F374`–`0x8019F5A4`: prologue `addiu sp,sp,-48`,
epilogue `jr ra` + `addiu sp,sp,48`, and in between a 14-case switch whose case
bodies and shared epilogue the sweep chopped into twelve "functions". **The sweep
ends a function at any `jr` or `j` plus its delay slot.** That is right for
`jr ra` and for a tail call, and wrong for the two forms PSY-Q emits *inside* a
function — a `jr` through a jump table, and a `j` to a shared epilogue — so every
switch in the game is a candidate.

The proof that a split is wrong is cheap and total: **a MIPS conditional branch
never leaves the function that issues it.** There is no conditional tail call and
the range is only ±128 KB. So a conditional branch crossing a boundary in the map
means the map is wrong, and the two functions plus everything laid out between
them are one function. `scripts/merge_branch_spans.py` applies exactly that rule,
and jump-table entries as a second source of the same proof:

```bash
python3 scripts/merge_branch_spans.py --dry-run    # every overlay
python3 scripts/merge_branch_spans.py fdat17
```

It found the bug in **five of the nine modules** — `fdat05`, `fdat11`, `fdat14`,
`fdat17`, `fdat20`, `fdat32` — i.e. four more areas were carrying the identical
crash. The three executables are clean of *this* class of split, which is why
it never showed up before the area modules went in. Merging drops fdat17 from
29 functions to 13. They were not clean of the next class: a `.data` word that
decodes as `jal` can still cut a GAME.EXE function, and `merge_branch_spans.py`
will not see it. See "add_call_targets can split a function" below.

Two things have to hold for a merge to be safe, and the script checks both:

- Nothing may `jal` a start the merge swallowed. A label is not callable, so that
  would trade one unmapped call for another. (Nothing does; every swallowed start
  is a switch case or an epilogue.)
- The `jr` has to resolve, or the switch dispatches to nothing at run time.
  `JumpTableAnalyzer` reads the table out of the overlay's bytes and only accepts
  entries inside the function — which is why **the merge and the `skip: 0` change
  above are one fix, not two.** The tables live in the module header, past the 32
  dispatch slots, so with the header skipped the recompiler could not read a
  single one.

Finding the table needs the same dataflow the recompiler does, not a peephole:
`fdat14` hoists its `lui`/`addiu` pair to the top of the function, **33
instructions** above the `jr` that uses it.

After the fix every switch in every module resolves — `fdat17` alone goes from
zero resolved tables to two, 19 entries — and the only `Dispatcher.Call` left on
a register in any module is the unreachable `default:` arm of a resolved switch.

`fdat32` was the exception, and it turned out not to be a bug at all. It is
covered in "fdat32 is a cut area" below.

### add_call_targets can split a function when `.data` looks like `jal`

Loading area 7 died with `unmapped call: 0x80042A48` from `func_80042A08`. The
address is not missing from the map and it is not a switch case: it is eight
bytes into `func_80042A40`, which `scripts/add_call_targets.py` had carved out of
`func_80042A08` because a word at `0x8006AAE0` decodes as `jal 0x80042A40`.

That word is not a call. `GAME.EXE`'s text segment runs `0x80011000`–`0x8006F000`,
but the last real function ends at `0x80064B30`; everything after that is data.
A packed signed-16 table in that tail happens to look like a run of `j`/`jal`
instructions. Almost every decoded target lands outside the executable
(`0x81882874` and friends). The two that do not — `0x80042A40` and `0x80042FF0`
— both landed in the middle of a real function.

The split is fatal for a small extra reason. `func_80042A08` falls through two
nops into `mult a0, a2` at `0x80042A48`. The recompiler skips leading nops, so
the fallthrough became `Dispatcher.Call(0x80042A48)` rather than a call to the
mapped start at `0x80042A40`. `merge_branch_spans.py` cannot see it: there is no
conditional branch across the cut, only fallthrough, and the start it would have
to swallow is not a `jal` target from any instruction that is actually code.

The sibling split `func_80042FD0` / `func_80042FF0` is the same table
(`0x80069FD8` = `jal 0x80042FF0`) and the same script. That one happens to fall
through onto the mapped start, so it had not crashed yet; nothing in code `jal`s
`0x80042FF0` either, and the second half has no prologue of its own.

Fix: rejoin both pairs in `config/funcmaps/game.json` (`0x80042A08` size 172,
`0x80042FD0` size 396) and stop `add_call_targets.py` harvesting a `jal` or `j`
whose site is not already inside a known function. Re-running the script after
that change adds nothing.

The four real `jal`s to `0x80042A08` and the twenty-odd to `0x80042FD0` all live
in `func_80043388`, which is the area-7 entity update — that is why this waited
for the final boss.

## fdat32 is a cut area, and nothing can load it

Scoring each module's *external* `jal` targets the way the load address was
originally pinned gives 17/17, 19/19, 22/22, 38/38 … for the eight area modules
against `GAME.EXE` — and **0/15 for `fdat32` against all three executables**. Its
fifteen outbound calls land mid-function on words like `bltz v0`, `sw s1,0x6C(sp)`
and `lw v0,72(sp)`. No constant delta fixes them either: the best offset over
±32 KB puts 4 of 15 on a function start, which is what chance gives.

The base is not the problem. `fdat32`'s **internal** calls are 4/4 on a prologue,
its header pointer table holds `0x80193BCC`–`0x80194954`, and `0x80193BD4`
disassembles as a clean function. `0x80193B38` is right. The module is real,
correctly based code whose calls into the host resolve to nothing.

**The loader settles it.** `CD/COM/FDAT.T` is registered as archive 5 by
`func_800185A0` at `0x80015E7C`, and the area loader at `0x8001689C` reads a
group of three with the area index taken from the byte at `0x8017E06C`:

| entry | destination | site |
|---|---|---|
| `area*3` | `0x801B6FA8` | `0x80016968` |
| `area*3 + 1` | `0x801B6FA8` | `0x80016A88` |
| `area*3 + 2` (the code module) | **`0x8019F07C`** | `0x800169C0` |

The code module's destination is a **literal argument**, built by
`lui a2,0x801A` / `addiu a2,a2,-3972` right before the call. So every code module
this game can load, loads at `0x8019F07C` — and `fdat32` is linked for
`0x80193B38`. Reading entry 32 through this path would drop it 46 KB from where
its own pointer table says it is, and `(*mod)[8]` would dispatch to `0x80194624`
with nothing there. It would break on hardware exactly as it breaks here.

And nothing names it. Scanning all three executables for `lui`/`addiu`/`ori`
pairs and for raw data words:

| address | OPEN.EXE | GAME.EXE | END.EXE |
|---|---|---|---|
| `0x8019F07C` (live module base) | 0 | **2 code refs** | 0 |
| `0x80193B38` (fdat32 base) | 0 | **0** | 0 |
| `0x80193BCC` (fdat32 first function) | 0 | **0** | 0 |

`OPEN.EXE` and `END.EXE` do not contain the string `COM\FDAT.T` at all, so
`GAME.EXE` is the only thing that ever reads the archive.

So `fdat32` is **leftover content**: an area whose data (entries 30 and 31, 66 KB
and 28 KB, the normal group shape) and code were left on the disc after the host
it was linked against moved on. The two empty groups before it — entries 24–29,
zero length — are two more areas cut without leaving anything behind. `fdat32`
only survives because a cut area's files were not stripped.

Consequences for the port, all of them "nothing to do":

- **The fifteen mid-function calls can never fire.** Nothing reads LBA 861 and
  nothing writes `0x80193B38`, so the overlay is never armed and never activated.
- Entry 31 ends at LBA 860, one sector short of 861, so no neighbouring read
  overruns into it.
- The overlay stays declared. It is correctly based, it costs 13 dead functions,
  and it is the evidence for this entry — removing it would only make the next
  person redo the work.

Worth remembering as a general point: **a module's internal `jal` targets confirm
its base, and its external ones confirm its host.** `fdat32` passes the first and
fails the second, and only the second test distinguishes live content from
abandoned content.


## The SDK naming problem

`SdkPatches.cs` routes PSY-Q library calls to the runtime's HLE implementations
by matching **exact function names** — `VSync`, `DrawOTag`, `DrawSync`,
`PutDrawEnv`, `PutDispEnv`, `CdInit`, `CdRead`, `PadInitDirect` and so on. A
linear sweep names everything `func_800xxxxx`, so nothing matches and the
recompiler always reports `applied 0 reimplementations`. Every SDK function has
to be mapped **by address** in `patches[]` instead, which is upstream's own
advice for this case: "you need to map each SDK address to its runtime
counterpart yourself, using `patches` with a `replace` that targets the runtime
function."

```json
{ "overlay": "open", "address": "0x80016078",
  "target": "RecompOne.Runtime.Sdk.LibGpu.DrawOTag", "mode": "replace" }
```

Done so far, across all three overlays — 63 patches: `libetc` (VSync), `libcd`
(CdInit, CdControl/F/B, CdSync, CdRead, CdReady, CdReadSync, CdGetSector), all
four HLE'd `libgpu` entry points, six of `libcdstream`, and libapi's
`DMACallback`. What is still unmapped is at the end of this file.

What worked for identifying them, roughly in order of payoff:

- **The overlay delta.** Identify once, get the other two for free — see "The
  overlay delta" below. This is worth doing first because it makes every other
  technique 3× cheaper.
- **Data-side search for hardware addresses.** PSY-Q reaches I/O through pointer
  tables in `.data`, not through literals in code — there are only five
  `lui …, 0x1F80` instructions in all of `OPEN.EXE`. Searching the *data* for a
  register address (`0x1F801810` for the GPU, `0x1F801800` for the CD) finds the
  table, and the functions that load through it are the library.
- **Diagnostic strings.** `func_80014C0C` is the `printf` thunk (BIOS A(3Fh), 69
  call sites) and the PSY-Q libraries are full of `$Id:` tags and error text —
  `VSync: timeout`, `CdInit: Init failed`, `GPU timeout:QUE=…`,
  `SetGraphDebug:level:%d,…`. Following an argument names the caller for free.
- **Struct offsets as evidence.** A function that writes a packet at `env+0x1C`
  and copies `0x5C` bytes is handling a `DRAWENV`; one that clamps to
  `[0x1F4, 0xCDA]` is doing PSY-Q's horizontal-range arithmetic. Two independent
  offsets agreeing is what turns a guess into an identification.
- **Indirect-call tracing.** PSY-Q reaches whole modules through driver tables
  filled in at init, so a public entry point can have *zero* `jal` references in
  the image. `libgpu`'s 15-slot table and libapi's `DMACallback` are both like
  this. When a function you expect to be called has no callers, log the address
  in `Dispatcher.Call` and watch for it instead of searching statically.
- **PSY-Q signature matching** against the official SDK objects would identify
  everything wholesale (`ghidra_psx_ldr` and FLIRT-style matchers do this) and is
  the only approach that really scales — still worth setting up if the remaining
  libraries fight back.
- **The managed stack of the running process** names the MIPS routine a hung
  game is spinning in. See "For a hang, take the managed stack of the live
  process" in [DEVELOPMENT.md](DEVELOPMENT.md).

## Merging the SDK names: 997 functions stop being addresses

The sweep names everything positionally, which is what "The SDK naming problem"
above is about. Upstream RecompOne grew a **PSY-Q signature bank** (2,949
functions, 9,674 variants, matched on exact word sequences with an object-layout
anchor to break ties), and it runs as a standalone command — `--autoconfigure` —
so it needs **no pin move** and no change to the recompiler this port builds
against. `scripts/merge_sdk_names.py` writes what it found into
`config/funcmaps/*.json`.

**A name merged in as legibility can start binding later.** `bound_names()` reads
the list out of the checkout for exactly that reason, but it only guards the
*next* run: the merge to `d81dec8` added `libpress` to `SdkPatches`, and five
`DecDCT*` names already sitting in `open`, `game` and `end` turned into `applied
63 patches, **11** reimplementations` — the intro's MDEC path silently rerouted to
an HLE nobody has watched. They are back to `func_`-named and in `HLE_NAMES`. So
**read that counter after every merge**: `63, 0` is the invariant, and anything
else is a rename that became a behaviour change.

Run against `SLUS-00158` it named 347 of 515 functions in OPEN.EXE, 425 of 1103
in GAME.EXE and 324 of 471 in END.EXE. Its function boundaries agree with this
project's swept-and-merged maps almost exactly — 1,096 shared addresses, at most
two on either side unshared — so the merge is a join on address, and the script
asserts that: addresses and sizes are untouched, only `name` changes.

**It independently reproduced the hand-mapped SDK addresses.** Of the 63 entries
in `patches[]`, it agrees with 54 exactly, differs on 3 only by naming `CdSync`
through its alias `CD_sync`, and leaves 6 unnamed. **Zero contradictions.** Two
of the port's own hand deductions came back confirmed by name: `func_8005EB08`
and `func_8005EC10`, which `MapRender` had argued were sine and cosine from
parity and from one of them taking `|a0|`, are libgte's `rsin` and `rcos`.
`patches/CullGrid.cs` calls them by those names now.

**Naming a function is binding it, so the merge refuses the names that would.**
`SdkPatches` matches by name, so writing `CdSearchFile` into a funcmap does not
label that function — it reroutes it to the runtime's HLE. That is a behaviour
change, and three of the four `libgpu` image routines it would newly bind
(`ClearImage`, `MoveImage`, `LoadImage`) are the ones `func_8001883C` draws the
loading screen with, which is the measured fact `patches/LoadPacing.cs` is built
on. So the script skips every name the recompiler binds — 97 of them — and they
stay `func_`-named. `patches[]` keeps all 63 entries and the recompiler still
reports `applied 63 patches, 0 reimplementations`, exactly as before.

**The exclusion list is read out of the patched checkout, not copied.** It was
first written from the pristine pin's `SdkPatches.cs` and so missed
`DMACallback`, which `0004-libapi-dma-callbacks` adds — and the matcher's name
for it bound a *second* address alongside the one `patches[]` binds by hand,
turning up as `applied 3 reimplementations` where there had been 0. Reading the
table the recompiler will actually use cannot drift that way. That near-miss also
surfaced a real disagreement about where `DMACallback` is; see `docs/TODO.md`.

**A name that matched twice is refused at both addresses.** `SpuVmDamperOn`
matched two places in GAME.EXE, so one of them is wrong and nothing here says
which. The whole value of the merge is that a stack trace can be read at face
value, so an ambiguous name is worth less than none.

What this buys is legibility, not behaviour: `dotnet-stack report` on a hang —
the documented technique for one — now names `rsin`, `SsSetMVol`, `RotTransPers`
rather than three hex addresses to look up, and any future address hunt starts
from 45 named `libcd`, 134 `libgte`, 113 `libsnd` and 75 `libgpu` routines in
GAME.EXE instead of from nothing. Verified rename-only: the generated C# is
byte-identical once the renames are undone, allowing for the overlay suffix the
recompiler drops when a name stops colliding.

## The overlay delta: identify once, get all three

The three executables are three separate links of the *same* PSY-Q libraries, so
every library routine exists in all three at a different address. They are laid
out at a **constant offset per object file**:

| object | `game` − `open` | `end` − `open` |
|---|---|---|
| `libgpu` (both layers) | `+0x4A7A0` | `-0x22F8` |
| `libcd` (`cdinit`) | `+0x315B0` | `-0x3A34` |
| `libcd` (`cdio`, the thunk block) | `+0x2FDBC` | `-0x3A34` |
| `libcd` (`stream`) | `+0x30AB4` | `-0x3A34` |
| `libetc` (`vsync`) | `+0x41140` | — |

The granularity is the translation unit, not the library: `libcd`'s `cdio` and
`stream` modules are at different offsets from each other. So one identification
plus one subtraction gives the other two, but only for functions in the *same*
object — do not extrapolate a delta across a module boundary.

`scripts/match_overlays.py` does this mechanically. It matches on a
relocation-insensitive normal form — `j`/`jal` targets, `lui` immediates and the
16-bit displacement of loads, stores and `addiu` masked out, leaving opcodes and
register numbers. Absolute addresses are exactly what differs between the links,
so masking them is the point:

```bash
# where is open's DrawOTag in the other two?
python3 scripts/match_overlays.py disc/KingsField2.cue 0x80016078

# re-derive the whole libgpu map and emit the config patches
python3 scripts/match_overlays.py disc/KingsField2.cue --libgpu
```

Short functions (a 12-instruction table dispatch, say) match in several places;
the tool reports that rather than guessing, and settles them against the delta
the unambiguous rows agree on. Run against the already-known `libcd`/`libetc`
addresses it reproduces every one of them, which is what makes it trustworthy
for the ones that are not known yet.

## libgpu: found and mapped

libgpu is **two layers**, and only the outer one is worth patching.

The public API never touches hardware. It dispatches through a 15-slot driver
table (`0x8003D610` in `open`, reached via a pointer at `0x8003E790`) whose
entries are the routines that do: `_ctl` writes GP1 and shadows the value,
`_cwc` pushes words to GP0, `_dma` programs DMA channel 2 in linked-list mode,
`_otc` uses channel 6, `_load`/`_store` issue GP0 `0xA0`/`0xC0`. Patching a
public function replaces the whole path below it, and the runtime already
emulates every register the driver layer would have written.

This indirection is also why the library is invisible to the obvious searches:
**nothing in the image ever forms a GPU register address**. There are five
`lui …, 0x1F80` instructions in all of `OPEN.EXE` and none of them is the GPU.
The register addresses live in a data table (`0x8003E7A8`: GP0, GP1, D2_MADR,
D2_BCR, D2_CHCR, D6_MADR, D6_BCR, D6_CHCR, DPCR) that the driver layer loads
through. Finding that table by searching the *data* for the dword `0x1F801810`
is what unpicked the whole library, and the same trick found `libcd` earlier.

| function | `open` | `game` | `end` | HLE |
|---|---|---|---|---|
| `ResetGraph` | `0x80015A8C` | `0x8006022C` | `0x80013794` | |
| `SetGraphDebug` | `0x80015D28` | `0x800604C8` | `0x80013A30` | |
| `GetGraphDebug` | `0x80015D9C` | `0x8006053C` | `0x80013AA4` | |
| `SetDispMask` | `0x80015DC4` | `0x80060564` | `0x80013ACC` | |
| `DrawSync` | `0x80015E04` | `0x800605A4` | `0x80013B0C` | ✔ |
| `ClearImage` | `0x80015E34` | `0x800605D4` | `0x80013B3C` | |
| `LoadImage` | `0x80015E84` | `0x80060624` | `0x80013B8C` | |
| `StoreImage` | `0x80015EC0` | `0x80060660` | `0x80013BC8` | |
| `MoveImage` | `0x80015EFC` | `0x8006069C` | `0x80013C04` | |
| `ClearOTag` | `0x80015F68` | `0x80060708` | `0x80013C70` | |
| `ClearOTagR` | `0x80015FBC` | `0x8006075C` | `0x80013CC4` | |
| `DrawPrim` | `0x80015FF4` | `0x80060794` | `0x80013CFC` | |
| `DrawOTag` | `0x80016078` | `0x80060818` | `0x80013D80` | ✔ |
| `PutDrawEnv` | `0x800160D0` | `0x80060870` | `0x80013DD8` | ✔ |
| `GetDrawEnv` | `0x80016190` | `0x80060930` | `0x80013E98` | |
| `PutDispEnv` | `0x800161F0` | `0x80060990` | `0x80013EF8` | ✔ |

Only the four marked `HLE` are patched, because those are the only four
`RecompOne.Runtime.Sdk.LibGpu` implements. The rest run as recompiled MIPS and
work: their register writes are trapped by `PSMemory` and their DMA (channels 2
and 6, including OTC) is emulated.

How each of the four was pinned down:

- **`PutDispEnv`** computes `hStart = scrX*10 + 0x260` and clamps it to
  `[0x1F4, 0xCDA]` — the same two magic numbers as the runtime's own
  `LibGpu.PutDispEnv` (`Math.Clamp(hStart, 500, 3290)`). It finishes with
  `GP1(0x08)` and a 20-byte copy of the DISPENV to the current-env global.
- **`PutDrawEnv`** builds the `DR_ENV` packet at `env+0x1C`, which is precisely
  where that packet sits inside a 92-byte `DRAWENV`; it then sends the packet
  through the driver's `_dma` slot, copies `0x5C` bytes to the current draw env
  and returns its argument. Two independent struct offsets agreeing is what
  makes this one certain rather than plausible.
- **`DrawOTag`** calls the `_dma` slot — the routine that writes `D2_MADR`,
  `D2_BCR = 0` and `D2_CHCR = 0x01000401` — with the ordering table.
- **`DrawSync`** tail-calls the slot that polls `D2_CHCR` and GPUSTAT and, on
  expiry, prints `GPU timeout:QUE=(%2d,%2d),CODE=(%d,%d,%08X)`.

`SetGraphDebug` is confirmed the same way: it is the only caller of
`SetGraphDebug:level:%d,type:%d reverse:%d`, and `DrawPrim` passes the literal
string `"DrawPrim"` as the `%s` of `%s: bad prim:addr=%08X,type=%s,len=%d`.

`GetDrawEnv` and `GetDispEnv` have **zero** call sites in all three overlays, so
the current-env copies that the HLE versions skip are never read back.

Verification is in the log rather than in the reasoning: with the patches in
place `KF2_LOG=gpu,sdk` shows `PutDrawEnv`/`PutDispEnv` reporting a coherent
640×240 double buffer (garbage struct offsets would give nonsense), the
`[DMA] ch2` linked-list transfers disappear (the HLE `DrawOTag` writes GP0
directly instead of programming the DMA controller), and `GP1(03) 0x000000`
confirms the display is switched on.

## libcdstream: found and mapped

The intro and ending are STR movies streamed off the disc. KF2 drives them
through `CdRead2(mode | 0x100)`, which is `stream.c`'s own entry point: it sends
`CdlSetmode`, installs the stream module's DMA-3 and ready callbacks, and issues
`CdlReadS`. Six public functions matter.

| function | `open` | `game` | `end` | call sites in `open` |
|---|---|---|---|---|
| `StSetRing` | `0x8001C584` | `0x8004D038` | `0x80018B50` | 1 |
| `StClearRing` | `0x8001C5DC` | `0x8004D090` | `0x80018BA8` | 0 |
| `StUnSetRing` | `0x8001C62C` | `0x8004D0E0` | `0x80018BF8` | 0 |
| `StSetStream` | `0x8001C6CC` | `0x8004D180` | `0x80018C98` | 1 |
| `StFreeRing` | `0x8001C7A0` | `0x8004D254` | `0x80018D6C` | 2 |
| `StGetNext` | `0x8001C8A8` | `0x8004D35C` | `0x80018E74` | 1 |

The ring layout identifies them, and it is exactly the layout the runtime's
`LibCdStream` already assumed: `slots` 32-byte headers first, then `slots`
2016-byte data blocks. `StGetNext` computes its data pointer as
`ring + slots*32 + idx*2016` (in the disassembly, `idx*63 << 5`) and its header
pointer as `ring + idx*32`; `StFreeRing` divides back by `0x1F8` words to
recover the index, requires the slot's status word to be 4, and clears the `n`
headers whose count it reads from `header+6` — the STR frame header's sector
count. The call sites settle the argument shapes: `StSetRing(0x800927F0, 0x20)`
(a 64 KiB ring, 32 slots) and `StGetNext(&addr, &header)` polled until it
returns 0.

Mapping these was necessary but **not sufficient** — see the next section.

## VSync: found and mapped

| Overlay | `VSync` |
|---|---|
| `open` | `0x8001EB88` |
| `game` | `0x8005FCC8` |
| `end`  | `0x8001B154` |

Found by fingerprinting the function's *contract* rather than its name, since a
linear sweep provides no names. `VSync(mode)` returns a counter when `mode < 0`,
returns early when `mode == 1`, and otherwise waits — which shows up in the
generated C# as a register copied from `A0`, sign-tested, then compared against 1.
Exactly one function per overlay matches, each 79 lines with identical structure:
the same statically linked routine, linked at a different address in each
executable.

**Confirmed, not guessed:** the helper immediately after it counts down and, on
expiry, calls the `printf` thunk with the string at `0x800117B4`, which reads
`VSync: timeout` — PSY-Q's own timeout message.

Mapped via `patches[]` in `config/kf2.json`; the recompiler reports `applied 3
patches` and emits the body as a forwarding call:

```csharp
public static void func_8001EB88(CpuContext c, IMemory m) => RecompOne.Runtime.Sdk.LibEtc.VSync(c, m);
```

Useful technique for the remaining SDK functions: `func_80014C0C` is the `printf`
thunk (BIOS A(3Fh), 69 call sites). Library routines pass diagnostic strings to
it, so following its arguments names the surrounding function for free — that is
how `VSync` was confirmed.


## Where the rest of the mapped addresses are written up

Three more identifications live with the runtime bug that motivated each, in
[RUNTIME.md](RUNTIME.md), rather than being repeated here:

| routine | overlays | why it is there |
|---|---|---|
| `CdReady`, `CdReadSync`, `CdGetSector` (and `CdInit`'s different delta) | all three | "The three ways a CD read can hang" |
| `DMACallback` | all three | "DMA callbacks: the thing that was actually missing" |
| `InterruptCallback` and the callback table it indexes | all three | "The interrupt-callback table cannot be guessed" |

**Still unmapped:** `libpad` — and it may never be needed, since the game reads
the pad through the BIOS (`B(16) PAD_dr` in the trace, not `PadInitDirect`; see
[INPUT.md](INPUT.md)) — and `libcdstream`'s `StSetMask`/`StGetBackloc`, which
this game never calls and which are therefore not identified either.
