# Brief: sharing Verdite2's work across Verdite1, Verdite2 and Verdite3

This is a multi-session program, not a one-shot task. Each session: read this brief, read the progress log (see "Progress log"), do the next unit of work, update the log, stop. Don't start a phase whose gate isn't met.

## Read first
In Verdite2: `AGENTS.md`, `NOTES.md`, then `docs/RECOMPONE_FORK.md`, `docs/RECOMPONE_PATCHES.md`, `docs/PATCHES_AND_MODS.md`, `docs/PACKAGING.md`, `docs/GAME_INTERNALS.md`, `docs/ENV_VARS.md`, `scripts/setup_tools.sh`, `.gitignore`, `.github/workflows/*`. The conventions in `AGENTS.md` apply to all three repos unless this brief says otherwise. That includes findings going in `docs/` and not in commit messages, empirical verification, and visual judgement being my job, not yours.

## The situation
- **Verdite2** (`Voicedrew11/verdite2`): King's Field, NTSC-U `SLUS-00158`, the US release of the Japanese *King's Field II*. Playable start to finish. C#, .NET 10, built on a vendored fork of RecompOne at `tools/RecompOne`, plus about 81 hand-written patch files (~37k lines) in `patches/`, a settings UI in `patches/settings/`, a launcher that compiles the game at first run, an MCP server in `mcp/`, a runtime mod loader in `mods/`, and RE scripts in `scripts/`.
- **Verdite1** (`Voicedrew11/verdite1`) and **Verdite3** (`Voicedrew11/verdite3`): the first and third games in the Japanese numbering. Both repos currently contain only a LICENSE. Nothing has been recompiled yet.
- The goal: each game gets Verdite2's improvements where they make sense, without three copies of the same code drifting apart, and without any game's development blocking the others.

## Decisions already made — do not reopen
- **Three separate game repos.** Not a monorepo. Each game has its own README, releases, issues and identity.
- **Shared code is consumed through `git subtree`**, pinned per game. Each game upgrades shared code on its own schedule, and a fresh clone of any game repo builds with nothing extra fetched. Not NuGet, not submodules.
- **Two shared repos, kept apart:**
  1. **The RecompOne fork** (working name `Voicedrew11/RecompOne`): upstream-derived. It shares history with upstream `BlackLabelHQ/RecompOne` so upstream can be merged three-way. It's subtree'd at `tools/RecompOne` in every game.
  2. **Verdite Core** (working name `Voicedrew11/verdite-core`): my own game-agnostic code. That means tooling, launcher shell, packaging, settings framework, and patch mechanisms once they've proven shareable. It's subtree'd at a prefix you'll propose (e.g. `shared/`).

  Keep them separate because the fork's history has to stay mergeable with upstream, and my code mixed into it would make every upstream harvest harder and blur what's upstream's and what's mine.
- **Extract on evidence, not in advance.** Nothing moves into Verdite Core until a second game actually needs it. Details under Phase 3.

## Hard rules for every phase
- **Upstream RecompOne rejects AI-authored pull requests.** Never open a PR or issue against `BlackLabelHQ/RecompOne`. Fixes go upstream as issues that I write myself.
- **No copyrighted game data in any repo:** disc images, extracted executables, recompiler output (`generated/`). Every new repo gets the same `.gitignore` protections Verdite2 has.
- **Shared-subtree edits go in their own commits**, never mixed with game code, and get pushed back to the shared repo soon after. A game's copy of a shared subtree must never quietly diverge from some commit of the shared repo.
- **No hardcoded game-specific values in shared code.** Addresses, struct layouts, record sizes, sentinels, units, overlay names, env var prefixes, settings keys, disc IDs, assembly names: all of these come from the game's own data. If shared code needs a number about a game, the game supplies it.
- **Verdite2 must not regress for existing users** at any point. In particular:
  - `KF2_*` environment variables (216 of them, documented in `docs/ENV_VARS.md`) keep their exact names in Verdite2. Shared code takes the prefix from the game.
  - `settings.json` / `interface.ini` keys stay identical, so existing users' settings survive an update.
  - Save files and `.fog` map files stay readable.
  - Mods (`mods/kf2debug` and third-party ones I don't control) are compiled at run time against the game's types. Moving or renaming a type a mod might use breaks them. Keep the names Verdite2 exposes, using type forwarding or thin wrappers if something moves.
  - The assembly name `KingsField2`, the MCP project `KingsField2Mcp` and the launcher's update check keep working.
- **Section titles in `docs/` are referenced from source comments by exact title.** Add sections freely; never rename or delete one without fixing every reference.
- **Don't rewrite history in any existing game repo.**

## Stop and ask me before
- Creating any GitHub repo, naming it, or choosing whether it's a network-linked GitHub fork.
- Any `git push` to anything public. Prepare locally and show me.
- Choosing a disc/region for Verdite1 or Verdite3 (see Phase 2).
- Choosing assembly names, env var prefixes and project names for Verdite1 and Verdite3.
- Any change to Verdite2's user-facing behaviour, settings, env vars, save format or mod-visible types.
- Any time an equivalence check fails and fixing it would mean changing behaviour rather than structure.
- Starting a phase, or starting a new feature port in Phase 3.

---

## Phase 0: inventory (no code changes)
**Gate:** none.

Write `docs/SHARING.md` in Verdite2 classifying everything in the repo into one of these buckets:

- **A. RecompOne fork:** `tools/RecompOne`. Generic PS1 runtime: GPU, GTE, SPU, PSY-Q HLE, perspective correction, subpixel, Z-buffer, AO, anisotropic, 24-bit, PGXP, CHD. Shared in Phase 1.
- **B. Game-agnostic Verdite infrastructure:** things with no knowledge of King's Field's RAM. Likely candidates: the launcher shell (but `DiscCheck`, `BuildKey`, `GameCompile` and `Recompile` reference KF2 specifics, so split those out), packaging scripts, CI workflows, `mcp/`, the settings UI framework (not the per-feature pages), `HookAttach`, the profiler/frame-viewer panels, `CrashDump`, `DesktopEntry`, `Prejit`, `release.sh`, and the RE scripts (`match_overlays.py`, `callgraph.py`, `find_writers.py`, `inspect_disc.py`, `extract_file.py`, `add_call_targets.py`, `merge_sdk_names.py`, `merge_branch_spans.py`, and others). Several of these scripts embed KF2 addresses or overlay names (`match_overlays.py`, `rate_matrix.py` and `rate_census.py` heavily), so record what each one hardcodes.
- **C. Game patches whose mechanism might generalize:** patches whose idea isn't KF-specific but whose implementation reads KF2 RAM. For each, record which kinds of coupling it has:
  1. **Function hook targets** (e.g. `LookRoutine`, `MoveRoutine` in `Analog.cs`; `ArmDraw`, `ModelSubmit` in `ZBuffer.cs`). Porting needs the counterpart function *and* that it does the same job with the same arguments at the same point in the frame.
  2. **Data addresses** (e.g. `Pad`, `FwdVel`, `TurnVel` in `Analog.cs`), plus their types, units and conventions (`StrafeVel` moving along `yaw-0x400`, walk speed `0xC8`).
  3. **Struct layouts** (e.g. `ModelWalk.cs`'s tables: creatures 200 × `0x7C` live when `u8[+0x9]==1`, objects 396 × `0x44` free when `u16[+0x6]==0xFF`, and so on).
  4. **Control-flow assumptions** (e.g. `FramePacing.cs`'s stage-by-stage model of KF2's main loop, the vblank credit spin, AI running one entity in four). These may not port as data at all.
  5. **Overlay names and bases** (`open`/`game`/`end` at `0x80016078`/`0x80060818`/`0x80013D80`, the `fdat` modules).
- **D. KF2-only:** things that only make sense for this game (e.g. `AreaWarp`, `EndingHold`, `MessageGlyphs`/`MessageText`, `CardIcon`, `config/kf2.json`, the funcmaps, `GAME_INTERNALS.md`).

Use grep as a starting signal (`0x80xxxxxx` literals, `KF2_`, `SLUS`, overlay names), but read the code. The grep counts include comments and miss offsets. This is a best guess, and the doc should say so. The real classification only comes out in Phase 3, and it gets updated as that happens.

Also record what's game-specific in the *infrastructure*: the `KF2_` prefix, `KingsField2` names, disc ID checks, paths like `disc/KingsField2.cue`, and anything the launcher's first-run compile or `BuildKey` assumes.

**Done when:** I've reviewed `docs/SHARING.md`.

---

## Phase 1: extract the RecompOne fork
**Gate:** Phase 0 reviewed, the current Verdite2 release has shipped and is tagged, and there's no unreleased staged work on `main`. If those don't hold, stop and ask.

### End state
- A standalone fork repo whose history shares ancestry with upstream at the sha in `tools/RecompOne/UPSTREAM`.
- Verdite2 consumes it via `git subtree` at the **same prefix, `tools/RecompOne`**, so no `ProjectReference` path in `Deception.csproj` or `Verdite2.Launcher/Verdite2.Launcher.csproj` changes. If you're editing one, stop.
- `git diff <branch-base> HEAD -- tools/RecompOne` is **empty**. This is a move, not a change: no upstream harvest, no reformat, no fixes.

### Building the fork's history
Right now the upstream ancestry only exists as the throwaway single commit `ensure_fork()` builds in `tools/RecompOne.git`. The real history of the vendored tree is in Verdite2's commits under `tools/RecompOne/`.

Preferred approach. Confirm it's feasible before committing to it:
- `git subtree split --prefix=tools/RecompOne` on a full clone to get the vendored tree's own history.
- Graft the first split commit (the vendoring import) onto the upstream commit it was vendored from, so the import becomes "the port's changes relative to upstream".
- Commits where `UPSTREAM` changed are harvests (`0409bc2`, `d81dec8`, and any others). Graft each with a second parent at the new upstream sha so they become real merges.
- Bake the grafts in (`git replace --graft` + `git filter-repo` or equivalent). Then check that `git merge-base HEAD <upstream>/master` equals the current `UPSTREAM` sha and that the tip tree equals Verdite2's `tools/RecompOne`.

Fallback if the split history is messy: what `ensure_fork()` already does, i.e. upstream up to `UPSTREAM` plus one commit with the vendored tree. Tell me which approach you used and why. The merge-base and tip checks must pass either way.

### Files upstream tracks but the fork shouldn't
Whatever is in the fork's tree gets copied into every game repo through the subtree.
- `RecompOne.Recompiler/AutoConfigure/signatures/psyq.json` (15.7 MB) is tracked upstream and gitignored in Verdite2. The fork must not track it; put it in the fork's own `.gitignore`.
- List any other file upstream tracks that the vendored tree lacks, and why.
- Give the fork its own `.gitignore` for `bin/`/`obj/`. Those are currently `tools/RecompOne/**/bin/` etc. in Verdite2's.

Fork README: say it's a fork and what it's for, state the no-AI-PR policy, and keep upstream's `LICENSE` and copyright untouched.

### Switching Verdite2 over
- `git rm -r tools/RecompOne`, commit, `git subtree add --prefix=tools/RecompOne <fork> <branch>`.
- **Round-trip test (required):** on a scratch branch of the fork, make a trivial edit in Verdite2's `tools/RecompOne` as its own commit, `git subtree push` it, confirm it arrives clean, then `git subtree pull` it back. Use this to choose `--squash` vs full history. Throw away the scratch branch and test commits afterward.
- Remove `tools/RecompOne.git/` and its comment block from `.gitignore`.

### `scripts/setup_tools.sh`
- Keep the build (default) and `--signatures`. Rework `--signatures` to fetch from upstream without `tools/RecompOne.git`.
- Move upstream harvesting into the fork repo, where it's now a plain `git fetch upstream && git merge upstream/master` or a `cherry-pick`. Carry over the acceptance-check text the script prints, word for word.
- Add `--pull-fork [ref]` and `--push-fork` wrapping `git subtree pull/push`. The fork URL and branch are defined in **one place**.
- Keep the header comment's reasoning about *why* the project vendors. It still holds; only the mechanism changed.

### CI, packaging, docs
- The CI workflows and packaging scripts say "vendored". Update the wording; add no clone or fetch steps.
- Check the launcher's `Build/` and `BuildKey` for anything tied to `tools/RecompOne.git` or the old layout.
- Rewrite `docs/RECOMPONE_FORK.md` for the new model, keeping every existing section title and the `0409bc2`/`d81dec8` merge write-ups. Those are still the checklist for the next merge. Update `NOTES.md`, `docs/DEVELOPMENT.md`, `docs/PACKAGING.md` and `AGENTS.md`. Add the shared-subtree commit rule to `AGENTS.md`.
- `patches/recompone/*.patch` and `docs/RECOMPONE_PATCHES.md` stay in Verdite2 for now. Tell me whether you think they belong in the fork.

### Verification
1. Before changes: build everything and record SHA-256 of `RecompOne.Runtime.dll`, `RecompOne.Recompiler.dll`, `KingsField2.dll` and the launcher assembly. After: same steps, same hashes. Builds are deterministic and paths are unchanged, so explain any mismatch before continuing.
2. The empty `git diff` on `tools/RecompOne`.
3. A fresh clone builds with the fork remote unreachable.
4. The packaging script for your platform produces a package.
5. If the disc is present, run the acceptance test from `docs/RECOMPONE_FORK.md` (`open → game → fdat02 → fdat05`, rate, `KF2_PRESENT_PROBE=1`, `KF2_PERSPECTIVE_PROBE=1`, `scripts/check_gate.py`). Those numbers all pass with a black window, so tell me what to look at.

---

## Phase 2: bootstrap Verdite1 and Verdite3
**Gate:** Phase 1 merged. Do one game at a time; ask me which first.

### Before starting a game, ask me
- Which disc and region. `AGENTS.md` already notes that the US-boxed "King's Field II" (`SLUS-00255`) is a different game from Verdite2's; whether Verdite3 targets that or the Japanese release is my call. Same for Verdite1.
- The assembly name, env var prefix, MCP project name and disc filename.

### Bootstrapping
- Subtree the fork at `tools/RecompOne`, pinned to the same fork commit Verdite2 uses.
- Set up the repo skeleton: `.gitignore` with the copyrighted-data protections, `AGENTS.md`, `NOTES.md`, a `docs/` layout mirroring Verdite2's where it applies, and a README.
- Create Verdite Core now, because this is the first point where something is genuinely needed twice. The RE scripts are needed to bring up a new game. Ask me before creating the repo. Move into it only what this bootstrap actually uses. Each script's hardcoded KF2 values (addresses, overlay names, file names) become inputs from the game's config, not new hardcodes. Switch Verdite2 to the shared versions in the same piece of work, and confirm they produce identical output on Verdite2's data.
- Recompiler config and funcmaps: discover overlays, entry points and bases (`inspect_disc.py`, `extract_file.py`), and identify PSY-Q functions using the signature bank. Expect the same traps Verdite2 hit, which are documented in `docs/RECOMPILATION.md`: overlays sharing an address range, entry points the linear sweep misses, `ScanCrossImage` splitting functions across overlays.
- **Fork changes a new game needs** (HLE gaps, SDK functions KF2 never called) are made in that game's `tools/RecompOne` as separate commits, pushed to the fork, and then pulled into the other games *only when I decide*. Every fork change must keep Verdite2's acceptance test passing. Verdite2 isn't allowed to break because Verdite3 needed something.
- Launcher, packaging and CI: if the new game needs them now, this is when they move into Verdite Core, with the game-specific parts (`DiscCheck`, `BuildKey` inputs, names, icons, disc IDs) supplied by each game. The launcher's first-run compile has to include shared source in its payload, and `BuildKey` has to cover shared files so an update to them triggers a rebuild. Verdite2's launcher must behave identically afterward; verify the first-run build and update check end to end.

### Done when
The game boots, reaches gameplay, and saves and loads through the memory card. The fork's runtime improvements should apply for free. Record the game's own acceptance test in its `docs/`, the way Verdite2 has one, and I'll confirm the picture.

---

## Phase 3: porting features, one at a time
**Gate:** at least one other game bootstrapped. Each feature is its own unit of work, and I pick which one.

### Suggested order, cheapest first
1. Bucket B infrastructure the game needs that isn't shared yet (settings framework, hook attach, panels, mouse).
2. Features with little or no RAM coupling (e.g. `Mouse`, `TrueColor`, `Anisotropic`, `MouseIndicator`).
3. Thin hooks: overlay bases plus a few addresses (e.g. `Widescreen`, `Perspective`, `Subpixel`, `ZBuffer`).
4. Deep ones last: `FramePacing`, `ModelWalk`, `Analog`, `AnimSmoothing`, `ObjectSmoothing`, the map. Expect these to need real reverse engineering in the other game, not just address lookups.

### Per-feature procedure
1. **Find the counterparts with function matching first, not by hand.** Fingerprint the KF2 functions the feature hooks and find their nearest matches in the target game (build on `match_overlays.py` and `callgraph.py`). Use `find_writers.py`-style tracing for data addresses.
2. **Check the behaviour matches, not just that an address exists.** For each hook: same job, same arguments, same point in the frame. For each data address: same type, units and conventions. For each struct: same record size, field offsets and sentinels. For loop-shaped features: draw out the target game's main loop and compare it stage by stage with Verdite2's documented one. Write the findings in the target game's `docs/GAME_INTERNALS.md`.
3. **Build the target game's version in the target game's repo first**, adapting from Verdite2's code. Get it working there. Don't build a shared abstraction yet.
4. **Then compare the two working versions.**
   - **Same mechanism, different data:** extract the mechanism into Verdite Core. The game-specific parts (addresses, layouts, conventions, overlay names, settings keys, env var names) become a per-game data structure each game supplies. Verdite2's copy carries exactly the values it had. Switch both games over in the same unit of work.
   - **Different mechanism** (likely for `FramePacing`-type features): leave them as two game-specific patches. Only extract sub-parts that are genuinely identical. Don't force them into one abstraction, and record in `docs/SHARING.md` why they stayed separate.
5. **Verify Verdite2 is unchanged.** Since the code moved, the binary hashes will differ, so verify behaviour instead: run Verdite2's acceptance test and the probes/counters relevant to the feature, and compare them with readings taken before the change. Then tell me what to look at on screen.
6. **Settings pages**: the framework can be shared; each game owns its pages and keys. Verdite2's existing keys and page layout stay the same.
7. **Mod-visible types**: if anything a mod could touch moves, keep Verdite2's public names working.
8. Update `docs/SHARING.md`: move the feature to its real bucket and note anything that changes the Phase 0 guesses.

---

## Progress log
Keep it in `docs/SHARING.md` in Verdite2 (later in Verdite Core, once it exists, with a pointer left behind). For each unit of work, record: what was done, which shared-repo commit each game is pinned to, what's verified by measurement, what still needs my eyes, and what's open. Write it so the next session can pick up from the log alone.

## Report at the end of every session
- What you did, and which phase or feature it belongs to.
- Every shared-repo commit you created, and which games now pin which commit.
- What was measured, the numbers, and what I need to check by eye.
- Anything you weren't sure about, and anything that changed the plan.
