# Packaging

How the port becomes something a person can download, and why it is shaped the
way it is. `docs/DEVELOPMENT.md` is still the document for working on the port;
this one is about shipping it.

## The problem a release has to solve

`generated/` is a translation of FromSoftware's code. The compiled form of it is
no less derived than the source, so **there is no binary of this game that can be
distributed** — which is why `generated/` is gitignored and why the README's
claim that the project ships no game data has to keep being true of a release and
not only of the repository.

The way out is that the *inputs* to a build are all distributable. The addresses
in `config/` are metadata about the disc's code layout, not the code. The port's
own 22k lines under `patches/` and `Program.cs` are original work under the
repository's MIT licence. The recompiler and the runtime are MIT. Only the output
is encumbered — so the release ships the inputs and produces the output on the
machine of somebody who owns the disc.

That is also, and separately, a *correctness* win rather than only a legal one.
The generated dispatch tables bake absolute LBAs from one mastering
(`generated/fdat02.cs` reads `public int LbaStart => 457;`), and `Dispatcher`
arms an overlay swap on a CD read hitting that exact sector. A prebuilt binary
would silently fail to load area modules on a differently mastered dump. A
per-user recompile reads those LBAs off the player's own image.

## The two projects

| project | builds without the disc | what it is |
|---|---|---|
| `Deception.Launcher/Deception.Launcher.csproj` | **yes** | the shipped executable |
| `Deception.csproj` | no | the developer path, unchanged |

They are opposites on purpose. `Deception.csproj` compiles `generated/`
through the SDK's default globs, which is what makes iteration incremental.
The launcher compiles neither `generated/` nor `patches/`: King's Field's
`patches/` call `Recompiled.KingsField2` and are not this port. It carries
Deception's inputs as *payload* and compiles them at first run.

**The launcher's code is Verdite Core's** (`tools/verdite-core/launcher/`).
`Deception.Launcher/` holds a csproj that names the executable and imports
`Launcher.targets`, and a `Program.cs` that hands `Launcher.Run` this port's
record: `Deception`/`deception`, `SLUS-00340`, `Deception.json`, game assembly
`Deception`, `dannyowelch/deception-recomp` (notify only), and which overlays
are play. The launcher assembly itself is `DeceptionLauncher`, because the game
dll is `Deception.dll` and both cannot share a simple name in the default load
context. The player-facing Windows stub is still `Deception.exe`. Everything
below that names a launcher file (`Paths`, `DiscCheck`, `BuildKey`, `Recompile`,
`GameCompile`, `UpdateCheck`, the popups) means core's copy. The disc files the
check requires, and the bytes the build key hashes, are every overlay `file` in
`Deception.json`, each at least as long as its furthest `offset + skip + size`.
The key also hashes each of those files' LBA and size, so a remaster that moves
the same bytes is rebuilt instead of served the other mastering's dll. A
different serial is refused before that. The localisation keys are
`verdite.build.*` and `verdite.update.*` with the port's name filled in.

Measured on the move, an AppImage built by core's script and run on an empty data
folder (`VERDITE2_DATA`) with `VERDITE2_UPDATE_CHECK=force`: `[Verdite2]
0.3.3+8debecd66`, `update available: v0.4.0 (running 0.3.3)` and `update.json`
written, the game recompiled and compiled into `builds/306e49d40b6983fc/`, then
`open → game → fdat02 → fdat05`, slot 2 at HP 46/86, 144.0 fps at 19.9 ticks/s,
`wide 288`, and the same `[KF2]` lines as the developer build. Given
`SLUS-00255`, it refuses with the same message. **Not run:** the Windows package
(no `pwsh` here; the stub builds as `Verdite2.exe` from core's project).

`.github/workflows/ci.yml` asserts the difference on every push, because it is
easy to break by accident and impossible to notice locally, where `generated/`
exists.

## What the release contains

Beside the executable, `content/`:

- `content/config/` — `Deception.json` and its funcmaps (`main`, `open`, `enty`,
  `plan`, `staff`, `title`, `kokumei`, `bin_3dmd`), as siblings, because the
  config's `funcMap` paths are relative to the config file. Addresses and names;
  no disc bytes. Not `kf2.json`, not the King's Field funcmaps, not
  `config/generated`.
- `content/border/` — `left-border.jpg` and `right-border.jpg`, drawn beside a 16:9 frame.
- `content/src/` — `Program.cs`, `InputSupport.cs`, `GraphicsOptions.cs`, `LegacyData.cs`, `WallLod.cs`, `PrimHeap.cs` and Verdite
  Core's C# (`tools/verdite-core/src/**`, under `content/src/verdite-core/`), as
  source text. `patches/` and `mods/` are not in the payload. `Sources.All()`
  walks `content/src` recursively, so `GameCompile` compiles core and `BuildKey`
  hashes it. (Core's `launcher/` is the launcher itself, not payload.)

Plus `RecompOne.Runtime.dll`, `recompone.dll`, Roslyn and the self-contained .NET
runtime. About 109 MB laid out, 41 MB as an AppImage.

On Windows those DLLs live in `bin/`, not next to the thing the player
double-clicks. A self-contained apphost will not start unless its managed
assembly and `hostfxr` sit beside it, so the published tree goes in `bin/` and
a few-KB .NET Framework 4.8 executable at the install root
(`tools/verdite-core/packaging/windows/Stub/`, built under the launcher's name)
launches `bin\Verdite2.exe`. Shortcuts, Inno's icon and `UninstallDisplayIcon`
all point at the stub. It is a nested project, so building it leaves `obj/`
AssemblyInfo under the game project's tree, inside `tools/**`, which
`Deception.csproj` removes for that CS0579 reason. `Paths.Install` looks one directory up when `content/` is
not next to the apphost, which is how a developer run and the AppImage (payload
still beside the executable) keep working. The zip and the install directory look like:

```
Deception.exe          the stub; it starts bin\DeceptionLauncher.exe
bin/                   the self-contained runtime and the real apphost
content/               Deception.json, funcmaps, sources
licenses/              this repo's MIT, RecompOne, verdite-core, NotoSans-OFL
version.txt            the tag, MAJOR.MINOR.PATCH (0.0.0 with no version tag)
```

**The licences the artifact carries:** this repository's MIT (`LICENSE`),
`tools/RecompOne/LICENSE`, `tools/verdite-core/LICENSE`, and `NotoSans-OFL.txt`.
`tools/RecompOne/patches/0033` embeds Noto Sans in `RecompOne.Runtime.dll`, and
SIL OFL 1.1 requires that licence to travel with the font. Both packaging
scripts stage the four files under `licenses/` (and, in the AppImage, the same
four under `usr/share/doc/deception/`). `version.txt` is the same number the
zip and the AppImage are named with. Adding a third-party asset to the build
means adding its licence here.

`UpdateCheck` reads `https://api.github.com/repos/dannyowelch/deception-recomp/releases/latest`
and only notifies. It does not download a release.

## First run

1. `Paths.Prepare()` resolves the data directory and **chdirs into it**. That one
   line is the whole of the packaging fix for file locations: the runtime
   addresses everything it owns with a bare relative path — `settings.json`,
   `interface.ini`, `carda.sav`, `cardb.sav`, `carda.fog`, `mods/.cache` — so they
   all resolve there and none of them needed a patch.

   `%LOCALAPPDATA%\Deception`, or `$XDG_DATA_HOME/deception` else
   `~/.local/share/deception`. `DECEPTION_DATA` overrides it.

   A later release does not replace `settings.json`, `interface.ini`, `carda.sav`,
   or `cardb.sav`. On startup, settings this version adds are written into the
   existing files; a value already there stays. When the Deception folder is
   missing one of those files, it is copied from `%LOCALAPPDATA%\Verdite2` (and
   the older King's Field folder names, or `~/.local/share/verdite2`) and not
   replaced if it is already there. `DECEPTION_DATA` skips that copy. The package
   build fails if a player file is staged into the zip or the AppImage, so
   unpacking a release cannot overwrite one.

2. `Runtime.DiscValidator = DiscCheck.Validate`. It checks `SYSTEM.CNF` boots
   `SLUS_003.40` and that every file `Deception.json` slices is present and at
   least as long as its furthest slice. King's Field `SLUS-00158` and
   `SLUS-00255` are named; any other serial is refused as not this disc.

3. `Runtime.WaitForValidDisc()` — the runtime's own picker, which already opens a
   native file dialog and saves the accepted path. It needed nothing but the
   validator.

   The launcher overrides the picker's title and text. This is the first thing a
   new player sees, so it says "Choose your disc", that the game is built from the
   player's own copy, and names the disc (a .cue or .chd, the serial); the runtime's
   wording is for a disc that has gone missing. Cancel on the picker during the boot
   wait quits the launcher, rather than leaving an empty window waiting forever.

4. A cue on the command line — the developer form, `Verdite2 other.cue` — is
   settled **here**, before the key, and is validated like any other disc. It has
   to be: the generated dispatch tables bake absolute LBAs from one mastering, so
   an assembly keyed on the saved disc and then handed a different image would
   silently fail to load its area modules, which is the exact failure the per-user
   recompile exists to prevent. Whatever is played is what is keyed and built.

5. If `builds/<key>/Deception.dll` is absent, build it. `BuildKey` hashes
   `SYSTEM.CNF`, the boot file and every overlay file (not the whole image: an
   image can differ in padding or streamed media and still recompile
   identically), each of those files' LBA and size, the shipped sources **and
   the shipped config**, and the launcher's version. A remaster whose files
   moved misses the cache. `content/config` is in the key because the function
   maps decide where each function starts and the SDK map decides which are
   bound to the runtime's HLE.

6. `Recompile.Run` drives the recompiler **in process** through
   `Assembly.EntryPoint` — its `Program.cs` is top-level statements, so its entry
   point is an ordinary invocable method. A second process was not an option: a
   self-contained publish has no `dotnet` to launch one with.

7. `GameCompile.Run` compiles the recompiler's output *and* the port's sources in
   one Roslyn pass. Together, because the port reaches into the recompiled code
   directly — `Program.cs` calls `Recompiled.Entry.Run`, and `AutoReload`,
   `AreaWarp` and `CullGrid` make fifteen static calls to
   `Recompiled.KingsField2.func_XXXXXXXX`. Splitting them would mean an interface
   boundary for each, or routing through `Dispatcher.Call`, which goes through
   `HookManager` and is therefore not the same call.

8. `AssemblyLoadContext.Default.LoadFromAssemblyPath`, then the assembly's entry
   point with the cue as `argv[0]`.

While steps 5-7 run, a player who has not done the first-run setup gets that
instead of the build popup, with the build as a strip at its foot. Step 8 waits
for both. See "Onboarding" in `docs/SETTINGS.md`.

**An install unpacked over an older one builds only what it shipped** (Verdite Core's
`9d6bac8`, taken 2026-10-11). Verdite3 0.1.1 unpacked over 0.1.0 failed to compile:
0.1.1 had moved fourteen patches into Verdite Core, the old copies stayed in
`content/src/patches`, and step 5's key and step 7's compile took every `.cs` under
`content/`. The launcher's build now writes `content/manifest.txt`, every payload file
it staged, and the launcher hashes and compiles only what it lists, naming anything
else at startup (`ignoring N file(s) under content/ this release did not ship`); an
install with no manifest builds from everything, as before. The Windows installer
(core's `verdite.iss`) also clears `content\` and `bin\` before an upgrade. Measured
here on a publish: the manifest's 227 lines are exactly the files under `content/`
(`content/config/onboarding.json` among them), and an `#error` file planted in
`content/src/patches/` was set aside. CI checks both on every push, as Verdite3's
does. **Not run:** an upgrade over a real older Verdite2 release, on either platform.

Measured on a 16-thread machine: **12.7 s** from launching the AppImage to a
built `KingsField2.dll` and a running game, of which the recompile is 0.85 s and
the rest is Roslyn. Warm launches skip to step 8.

## Two things that were nearly wrong

**The reference set must come from the host, not from what is loaded.**
`GameCompile.References()` reads `TRUSTED_PLATFORM_ASSEMBLIES`. The first version
walked `AppDomain.CurrentDomain.GetAssemblies()`, the way `ModCompiler` does — and
`ModCompiler` is right to, because a mod compiles against what the game has and by
then the game has loaded it. The launcher has loaded almost nothing. The build
failed on `patches/AgentServer.cs` with six errors about `System.Net.Sockets`,
purely because the launcher does not open a socket. Every framework assembly the
port uses and the launcher does not was missing for the same reason; sockets was
just the first one a patch happened to name.

**`ImplicitUsings` is an SDK feature, not a compiler one.** Both csprojs enable it
and the SDK answers by generating `GlobalUsings.g.cs`. Roslyn generates nothing,
so `GameCompile` supplies the `Microsoft.NET.Sdk` set itself. Without it the
port's 22k lines lose `System`, `System.Linq` and the rest, and fail in hundreds
of places that read as the port being broken rather than as a missing file.

More generally: **the compilation options in `GameCompile` and the properties in
`Deception.csproj` are two statements of one thing and must stay in step.**
A difference between them is a class of bug that only exists in the release. The
port's own notes record what losing the frame boundary looks like, and it is not a
crash — it is the whole game running fast from the title onward, silently. The
check for it is `KF2_FPS=144 KF2_FPS_PROBE=1` against the packaged binary, which
must read 20.0 ticks/s. Measured: 144.0 fps drawn, 19.9-20.0 ticks/s.

## Versioning

**The version is the newest `vMAJOR.MINOR.PATCH` tag that `HEAD` contains, and a
release is that tag and nothing else.** Five things have to agree about the
number: the launcher's assembly version, the AppImage's file name, the Windows
zip's, the Inno installer's, and the tag the release is cut on. Verdite Core
resolves it by one rule, in `scripts/version.sh`, `Launcher.targets` and
`build-windows.ps1` alike (its README, "Packaging"): a `VERSION` file at the
root first, which is why CI refuses one here; then `VERDITE_VERSION`, which
`release.yml` sets from the tag it was pushed for; then the tags; then `0.0.0`.

| what | how it gets the number |
|---|---|
| the assembly | core's `Launcher.targets`, by the rule above, into `<Version>` |
| `Verdite2-<v>-x86_64.AppImage` | `build-appimage.sh`, through `version.sh` |
| `Verdite2-<v>-win-x64.zip` | `build-windows.ps1`, by the same rule |
| `…-win-x64-setup.exe` | the script exports `VERDITE_VERSION`; core's `verdite.iss` **errors** if it is unset |
| the git tag | it *is* the number; `release.yml` refuses one that is not `vMAJOR.MINOR.PATCH` |

Until 0.5.0 the number was a `VERSION` file, bumped by a commit that the tag then
went on, with `release.yml` asserting the two equal, because a `v0.2.0` tag on a
tree that still said `0.1.0` would have published `0.1.0` files under a `0.2.0`
release. With the tag as the only source there is nothing for it to disagree
with. Verdite3 has worked this way since its first release ("The tag is the
version" in its `PACKAGING.md`).

- A build between releases carries the last release's number and the commit
  after it (`0.4.0+<sha>`); it is never announced as an update to itself.
- How far to move the number is the person tagging's call. `release.sh` refuses
  one that is not above the newest tag, because the update check never announces
  a lower one.
- The clone needs its tags: CI checks out with `fetch-depth: 0`, and a shallow or
  tagless checkout builds as `0.0.0`. `bash tools/verdite-core/scripts/version.sh`
  prints what a build here would call itself.

### The number is not the build

A release is many commits wide, so `0.1.0` does not identify a binary. The
assembly's `InformationalVersion` is therefore `0.1.0+<9-char sha>`, stamped by
the csproj's `StampBuild` target off `git rev-parse` (or `VERDITE2_BUILD`, for a
build made outside a checkout — a source tarball, a distro package — falling back
to `local`). `Ver.Full` reads it back, and it is printed at startup, written at
the head of the build log, and quoted on an unhandled exception; `Ver.Number`
alone is in the window title. **What a bug report should quote is the full
string.**

The sha is deliberately **not** part of `BuildKey`. Hashing it would make every
commit — a docs-only one included — throw away the player's built game and
recompile it. What goes into that assembly is the shipped sources and the shipped
config, and `BuildKey` hashes those directly.

### Cutting one

```bash
bash scripts/release.sh 0.5.0        # tags HEAD. Commits nothing, does not push.
git push origin HEAD && git push origin v0.5.0
```

The script refuses a malformed number, a dirty tree, a tag that already exists
and a number not above the newest tag. It does not push, because pushing
the tag is what publishes the draft release and that is a decision rather than a
step. The workflow builds both platforms, named by the tag, and
opens a **draft** with the commits since the previous tag appended to the body.

### Previews

A preview is a build handed to testers: a commit, not a version, and no promise
of which release it becomes. When one proves a release, `release.sh` tags that
commit, so the release is exactly what was tested. There is one preview at a time, the **rolling `preview`
prerelease**, replaced whole each time:

```bash
git push origin main
bash scripts/prerelease.sh      # builds HEAD as GitHub has it; replaces `preview`
bash scripts/prerelease.sh --no-wait
```

The script refuses a detached `HEAD`, a dirty tree, a `HEAD` that is not what
`origin` has for the branch, and a repository without the secret (listing
secrets needs admin access; without it, the script warns and goes on). It starts
`release.yml` by hand (`gh workflow run release.yml -f preview=true`); the build
job runs as for a release, and its tag check is skipped, since that step runs on
a `v*` tag alone and a run started by hand has none. The `preview` job then zips
each package with the `PREVIEW_PASSWORD` repository secret (AES-256, so 7-Zip or
WinRAR opens them and Windows Explorer does not) and deletes and recreates the
`preview` prerelease at that commit, with fixed notes: the password is had on the
Verdite Project Discord, and back up `carda.sav` and `cardb.sav` first, from
`%LOCALAPPDATA%\Verdite2` on Windows and `~/.local/share/verdite2` on Linux
(`$XDG_DATA_HOME/verdite2` if that is set; see "First run"). The assets are always
`Verdite2-preview-linux-x86_64.zip`, `Verdite2-preview-win-x64.zip` and
`Verdite2-preview-win-x64-setup.zip`, so
`…/releases/download/preview/<name>` never changes; the files inside, and the
title, carry the commit. The build calls itself the last release's number and the
commit (`0.4.0+<sha>`, `Ver.Full`): a preview makes no tag.

- **The `PREVIEW_PASSWORD` secret has to be set by hand, once, before the first
  preview**: `gh secret set PREVIEW_PASSWORD` (or the repository's Settings ▸
  Secrets ▸ Actions). The script checks for it, and the job fails before zipping
  anything without it. The password is the secret, never the repository, which is
  public; setting it again changes it.
- **It is never GitHub's "Latest".** A prerelease cannot be: the badge, and
  `releases/latest`, which the update check reads, name only a published full
  release, so no player is told about a preview.
- **`preview` is no version.** `release.yml` starts on `v*` tags alone, so the tag
  moving starts nothing, and `version.sh` reads `vMAJOR.MINOR.PATCH` alone. A clone that fetched the tag
  keeps a stale copy when it moves; `git tag -d preview` drops it.
- **It goes when the release does.** `preview-cleanup.yml` deletes the prerelease
  and its tag when a full release is *published* (not when its draft is opened),
  so a preview stays up until the release is live. A `release` event runs the
  workflow as the release's tagged commit has it, so it acts only for a release
  tagged on a commit that contains `preview-cleanup.yml`. It deletes the preview
  whatever commit it was built from, a newer one too; that is the design (one
  preview, gone with the release), not an oversight.
- **The old preview is deleted before the new one is made**, so its links 404
  while the new zips upload, and stay that way if the upload fails, until the job
  is rerun. A failure before that (a missing installer, an unset secret) leaves
  the old preview up. The job also deletes a `preview` tag left without its
  release, which `gh release create` would otherwise reuse at its old commit,
  and a `concurrency` group queues a second preview behind the first.
- Ported from Verdite3, where it was called `rc` until 2026-10-08 and renamed
  because it is handed out for any fix worth testing, not only a commit meant to
  ship. **Never run here yet**: the workflow files parse and the script passes
  `bash -n`, and that is all.

## Telling the player about a new release

The shipped launcher checks GitHub for a newer release at startup and says so;
**it downloads nothing and replaces nothing.** The player takes the new build
from the release page as before. The check is the same for the AppImage, the
installer and the zip, which is why it stops there: installing in place would
mean a separate path for each (the AppImage replacing `$APPIMAGE`, the installer
running a new `setup.exe` silently), and none of those can be tested until a
release exists to update to.

- `UpdateCheck.Start()` runs on a worker thread after `Runtime.Initialize`, so
  the disc picker and the build never wait on the network. It asks
  `api.github.com/repos/Voicedrew11/verdite2/releases/latest`, which leaves out
  drafts and prereleases, so the draft `release.yml` opens is announced only
  once it is published.
- **It reaches the network at most once an hour** (once a day until 2026-10-10, Verdite Core's `26aabe4`: a release published after a player's launch went unannounced for up to 24 hours). `update.json` in the data
  directory keeps the last answer (`checked`, `tag`, `url`) and any version the
  player skipped (`skipped`). Between checks the cached answer is used, so a
  player who launches twice in an hour still sees the notice. Offline or
  rate-limited, it prints one `[Verdite2] update check failed:` line, leaves
  `checked` alone so the next launch tries again, and **still announces the cached
  answer**: a release already known is not forgotten because the network is down.
  The file is a cache anyone can edit, so a field that is missing or of the wrong
  type reads as absent rather than throwing, and a `checked` in the future (a
  clock that was wrong) counts as stale rather than holding off checks until real
  time catches up. Every read-modify-write of it holds one lock, and *Skip* writes
  it on a worker, not the interface thread.
- **The notice is a popup at startup, and a badge that stays.** Once the check
  finds a newer tag (compared as `MAJOR.MINOR.PATCH` against `Ver.Number`; a
  suffix after the numbers, `v0.4.0-hotfix`, is ignored, and a tag with no number
  in it prints `update check: cannot read a version in the tag …` rather than
  vanishing), `UpdatePopup` opens once per launch, only while no other popup is
  open, so it never lands on the picker or the build, and **only outside play**:
  it is modal, so once an area module (`fdat…`) or `END.EXE` has loaded it waits
  until `OPEN.EXE` or a fresh `GAME.EXE` (the title) loads, and the badge alone
  carries the news meanwhile (the attract demo loads an area, so it counts as
  play). It says only the new version and the running one,
  with *Download page* (the release's `html_url`, in gold), *Skip this version*
  (never announce that tag again; a later one is announced) and *Later* — one row
  of equal buttons when the widest label fits a third of the popup, a column of
  full-width ones when it does not, which the pt-BR and es-419 labels need. `UpdateBadge` puts a square gold *Update available!* button at the right
  of the menu bar, left of the FPS counter or where the counter would be when that
  is off (`MainMenuBar.AddRightItem`, `0081`). Clicking it opens a small menu with
  the same choices, *Later* being *Hide until next launch*. Release notes are left
  out on purpose, because they can run long; the page has them. The badge shares
  the counter's limit: with the menu bar hidden (F1) the badge is hidden too.
- It is on by default. *Check for updates at launch* is a checkbox at the foot of
  Settings ▸ Interface (`SettingsRegistry.Extend("interface", …)`, saved under
  `Verdite2.UpdateCheck` in the view config). `VERDITE2_UPDATE_CHECK=0` turns it
  off from the environment. `=force` ignores the hourly limit; a skipped version
  stays skipped. To see a skipped release again, delete `skipped` from
  `update.json`.

Only the launcher checks. The developer path (`Deception.csproj`) never
reaches GitHub.

Measured with a launcher stamped `0.3.0` (`-p:Version=0.3.0`) against the
published `v0.3.2`: `[Verdite2] update available: v0.3.2 (running 0.3.0)` and
`update.json` written. A second launch reused the cache, with `checked`
unchanged. Nothing was announced at `0.3.2`, with `v0.3.2` skipped, or with
`VERDITE2_UPDATE_CHECK=0`. **Seen by eye:** the popup, the badge, and *Skip this
version* taking both away (`[Verdite2] skipped v0.3.2`, then `v0.3.2 is available
and was skipped` on the next launch). **Never looked at by eye:** the Interface
checkbox, and *Open download page* opening a browser from inside the AppImage
and on Windows.

Measured again after the review fixes, with crafted `update.json` files: a
`checked` in 2030 fetched and announced `v0.3.2`; `checked` and `tag` as numbers
fetched and announced it without an exception; a stale cache with the network
refused (`HTTPS_PROXY=http://127.0.0.1:9`) printed the failure, left the file
untouched and still announced the cached `v0.3.2`; a cached `v0.4.0-hotfix` was
announced; a cached `latest` printed the cannot-read line. **Never looked at by
eye:** the popup's buttons stacked in pt-BR and es-419, the popup waiting out
play until the title, and the badge holding still as the counter changes width.

## Building a release

```bash
bash scripts/setup_tools.sh          # builds the RecompOne subtree, tracked here

bash packaging/linux/build-appimage.sh      # dist/Verdite2-<v>-x86_64.AppImage
pwsh packaging/windows/build-windows.ps1    # dist/…-win-x64.zip and the installer
```

Both are one-line wrappers of Verdite Core's scripts
(`tools/verdite-core/packaging/`), which read this port's names from
`packaging/package.env` (`NAME`, `APP_ID`, and `INNO_APP_ID`, the installer's
`{9F1F0C1E-…}`, which must never change, and `INNO_PRIVILEGES=lowest`, the
per-user install "The shortcuts wear it too" needs); `scripts/release.sh` is the same.
The Windows script used to copy the font licence from `patches/recompone/assets/`,
a path from before the subtree; core's reads `tools/RecompOne/patches/assets/`.

Neither needs the disc. `.github/workflows/release.yml` runs both on a `v*` tag
and opens a draft release; it asserts that `generated/` and `disc/` are absent,
and that the tag is `vMAJOR.MINOR.PATCH`, before it packages anything.

**The release workflow has never run.** The tag pushed to test it was
`test-win-0.1.0`, which does not match the `tags: ['v*']` trigger, so only `ci`
fired; and `workflow_dispatch` is not offered for a workflow absent from the
**default branch**, which `release.yml` still is. So the Windows leg — the
installer, and the launcher on real Windows — is unexercised. Merging `dist` and
tagging `v*` is what settles it.

Trimming is off and must stay off: `MonoMod.RuntimeDetour` builds detours at run
time, `ModCompiler` hands Roslyn the loaded assemblies, and
`patches/AutoStart.cs` reflects over runtime internals.

The port's icon is `Icon.jpg` at the repo root, shipped as
`packaging/shared/deception.png` and `deception.ico`. See `packaging/shared/README.md`.

## The icon

The window and the executables wear that picture. `deception.ico` is
`ApplicationIcon` on `Deception.csproj` and on the launcher, and the Windows
stub reads the same file when `build-windows.ps1` finds it. `deception.png` is
copied beside both executables; `Program.cs` sets it as the window icon before
`Entry.Run`, and the launcher does the same for its own window from
`<app id>.png`. The AppImage script copies the png into the icon theme under
`deception`, which is what a Wayland compositor reads (`deception.desktop`
already says `Icon=deception`). `Program.cs` sets `Runtime.AppId` to that id
before the window exists.

The memory-card reader below is not installed. `patches/CardIconSource.cs` can
still find an icon on a disc, and Verdite Core's `CardIcon` would replace the
window with it, but nothing calls `CardIcon.Install`. A saved `icon.png` in the
data directory is what the launcher prefers for its own window until the game
starts and sets `deception.png`.

The notes that follow are the King's Field card icon this tree used to wear.

**Where it is.** 16×16 at 4bpp with a 16-entry BGR555 CLUT, in `CD/COM/FDAT.T` at
file offset `0x14D210` — the CLUT, then at `0x14D240` the three animation frames
stored **a row at a time**, frame 0's row *y*, then frame 1's, then frame 2's,
which is the order a loop filling all three of a card header's frames reads them
in. Reconstructed that way it is byte-identical to the icon in a `carda.sav`
this port wrote, all three frames, which is what proves the layout. Index 0 is
the background; the card header zeroes that entry (the archive's copy has a dark
teal there), so it is the icon's transparency.

**How it is found, and why not by offset alone.** The same sixteen colours are in
`GAME.EXE` at `0x800675B4`, beside the `BASLUS-00158` filename and title
templates the save routine builds a header from — so the palette is the
*signature* rather than a constant this repo has to carry: `CardIcon` reads it
out of `GAME.EXE`, checks it against the fixed offset in `FDAT.T`, and only scans
the archive if that fails. Nothing of the disc is in the port's source, and a
differently mastered dump is a scan rather than a miss. The pixels are in
`FDAT.T` only; `GAME.EXE` has the palette and not the picture.

**When it is read.** With a disc on the command line (the launcher always passes
one, and so does a run from the checkout) the icon is read at once, before
`Entry.Run`. With none — `KingsField2.exe` opened on its own — the disc is not
known yet: `settings.json` is loaded by the window, which `Entry.Run` makes, and
a first run's disc only once the picker answers. `CardIcon` read an empty
`CdPath` there and returned without a word, so Windows showed the shipped mark. It now
waits for the first overlay to load and sets the icon on the window's thread
(`GpuJobs.Run`). Not yet measured in Verdite2: the Verdite3 fix was measured
under Wine, and the no-argument case here is unverified until someone runs it
with no argument and sees the icon.

**The sizes are exact multiples** (the decode and the scale are Verdite Core's
`WindowIcon`, since 2026-10-05; `CardIcon` keeps where the icon is). 16, 32, 48,
64, 128 and 256, each a
nearest-neighbour scale of the same 16 pixels, handed to GLFW together
(`tools/RecompOne/patches/0061`, which is what made `SetWindowIcon` take more than
one). A desktop asking for any of those gets pixel art it does not resample;
before that, one image meant the window manager smoothing a 16×16 up or a 256×256
down, which is exactly what makes this kind of icon look like a photograph of
itself.

`KF2_ICON=off` clears it, `KF2_ICON=1` or `=2` picks another of the three frames
(they differ by a one-pixel bob). `KF2_ICON=orb` is still read by core, and with no
mark shipped it now means no icon.

## The shortcuts wear it too

Explorer draws a shortcut with the icon embedded in its target, and the target is
the release's `Verdite2.exe`, which carries no icon (Windows draws its plain program icon). Verdite Core's
`ShortcutIcon` (ported from Verdite3 `dc4a6d1`, `6603075`) is already called by
`WindowIcon.Apply`: once the disc is read it writes the same sizes into `icon.ico`
in the data directory and points every Desktop, Start menu and pinned taskbar
shortcut whose target is this process, or the stub above `bin\`, at it, and keeps
`icon.png` for the next run, which `CardIcon` sets at boot
(`WindowIcon.ApplySaved`) until the disc is read again.

That reached only a shortcut the player pinned. The installer was per-machine, so
its Start menu and desktop shortcuts were in the all-users folders, which the game
cannot write. Since 2026-10-09 it is **per-user** (`INNO_PRIVILEGES=lowest`): it
installs under `%LOCALAPPDATA%\Programs\Verdite2` with the shortcuts in the
player's own folders. **An upgrade from a per-machine install (v0.4.0 and before)
is a second install**, since Inno looks for a previous one in HKCU only: the old
copy in `Program Files` and its orb shortcuts stay until it is uninstalled, which
the release notes have to say. Since 2026-10-11 the orb is gone from Verdite2
too, so a shortcut the game has not reached shows Windows's plain program icon,
as in Verdite3.

Not measured in Verdite2: Verdite3's run under Wine rewrote two shortcuts
(`icon: 2 shortcut(s) now wear icon.ico`); here nothing has run on Windows, and
the installer has not been built per-user.

## Wayland takes the icon from the desktop entry

**A window icon set the ordinary way is invisible on Wayland, and silently so.**
`glfwSetWindowIcon` is a documented no-op there — the string
`Wayland: The platform does not support setting the window icon` is in the GLFW
Silk bundles — and nothing is thrown, logged or returned. There is a protocol for
it now (`xdg_toplevel_icon_manager_v1`, which KWin advertises), but GLFW neither
speaks it nor exposes the `xdg_toplevel` a caller would need to, so that door is
shut from inside the process.

What a compositor does instead is match the toplevel's **app id** to a desktop
entry and read that entry's `Icon=`. So the icon is a *file lookup*, not pixels
on the wire, and two things have to be true.

**The window has to say what it is.** Measured with `WAYLAND_DEBUG=1`, GLFW sent
`set_title("KingsField2")` and **no `set_app_id` at all** — the default is the
empty string. KWin therefore had nothing to match, and no icon could have appeared
whatever the port did: not the card icon, and not the orb the AppImage then installed
either, which is a packaging bug that was invisible for as long as nobody looked.
`tools/RecompOne/patches/0061` hints `GLFW_WAYLAND_APP_ID` (and the X11 class beside it)
from `Runtime.AppId`, which `Program.cs` and the launcher both set to `verdite2`
before the window is made. After: `xdg_toplevel#45.set_app_id("verdite2")`.

**And something has to be there to match.** `DesktopEntry` (Verdite Core's
`src/`, since 2026-10-05; `Program.cs` gives it the entry's name and comment) writes the
six sizes to `$XDG_DATA_HOME/icons/hicolor/NxN/apps/verdite2.png`. The theme is
the right home for them rather than one big PNG in the data directory, for two
reasons: the lookup picks the exact size the desktop asks for, so 32 and 48 px are
the real pixels rather than a toolkit's downscale of 256; and `XDG_DATA_HOME`
outranks `/usr/share`, so an installed build's own `Icon=verdite2` resolves to
these without the packager's entry being touched. An entry is written — to
`$XDG_DATA_HOME/applications/verdite2.desktop` — **only when none exists in any
XDG data directory**, which is the source-tree case; an installed or
appimaged-integrated build already has one. `Exec` prefers `$APPIMAGE` over
`Environment.ProcessPath`, because an AppImage's apphost lives in a mount point
that stops existing when it exits. A file already byte-identical is not rewritten,
so an ordinary boot touches nothing — **per runtime**: the system `dotnet` and the
AppImage's bundled runtime compress the same pixels to different PNG bytes
(measured 2026-10-05, pixels identical), so alternating a developer run and the
AppImage rewrites the six files each time. `KF2_ICON_INSTALL=0` writes none of it.

**This is how every AppImage does it, too** — it is just usually somebody else
doing the writing. An AppImage carries its `.desktop` and its hicolor icons
*inside* the image, where the desktop cannot see them; `appimaged` or
AppImageLauncher is what copies them into `~/.local/share` on integration.

A compositor may cache what it has; a new entry generally needs `kbuildsycoca6`
on Plasma, or a relog.

**Never looked at by eye:** the icon at the sizes a taskbar actually draws it, on
either platform.

## The one patch this needed

`tools/RecompOne/patches/0030-expose-host-pump.patch` makes `HostWindow.Pump` public as
`Runtime.Pump`. The build blocks for seconds and a window that stops pumping for
seconds is one the desktop offers to force-quit; `WaitForValidDisc` already runs
exactly that loop but only ever for its own condition. Everything else the
progress UI needs was already public — `Popup` and `PopupManager.Register` — so
`BuildProgressPopup` lives in the launcher.

## Not done

macOS (`.app` for `osx-arm64` and `osx-x64`) and Flatpak. The data-directory work
both need is done; what is missing is the packaging and, for macOS, signing and
notarization.

`.chd` **is** supported now — see "CHD disc images" in [RUNTIME.md](RUNTIME.md).
Two things about it belong here rather than there. The disc the player picks is
what `BuildKey.Compute` opens, and the key hashes the **six files** the recompile
reads rather than the container, so the same dump as a cue and as a CHD produces
the same key, shares one build directory, and switching between them costs no
rebuild. And the format is chosen by `DiscImage.Open` off the extension (falling
back to the CHD magic), so nothing in the launcher branches on it: `DiscCheck`
validates and `Recompile` rewrites the config's `cue` key exactly as before — that
key is the recompiler's schema and keeps its name.

**Never looked at by eye:** the progress popup itself, and the verdite orb at
the sizes a desktop actually draws it.
