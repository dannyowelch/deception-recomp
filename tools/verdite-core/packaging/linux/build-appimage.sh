#!/usr/bin/env bash
# Build a port's Linux AppImage.
#
# Run through the game's packaging/linux/build-appimage.sh, or directly; the game
# is the checkout this subtree sits in (tools/verdite-core), or $VERDITE_GAME_ROOT.
# Its packaging/package.env names it (NAME, APP_ID), and packaging/shared/ holds
# its $APP_ID.desktop and, if it ships a mark, $APP_ID.png.
#
# Needs the RecompOne subtree built (the game's scripts/setup_tools.sh) and the
# .NET 10 SDK. It does NOT need the disc: the launcher carries the inputs to a
# build and makes the game on the player's machine, which is what lets this run
# in CI at all.
set -euo pipefail

ROOT="${VERDITE_GAME_ROOT:-$(cd "$(dirname "${BASH_SOURCE[0]}")/../../../.." && pwd)}"
[ -f "$ROOT/packaging/package.env" ] || { echo "no packaging/package.env under $ROOT" >&2; exit 1; }
# shellcheck disable=SC1091
. "$ROOT/packaging/package.env"
: "${NAME:?package.env sets no NAME}" "${APP_ID:?package.env sets no APP_ID}"
# The apphost under usr/bin. NAME when the launcher assembly uses that name.
LAUNCHER="${LAUNCHER_ASSEMBLY:-$NAME}"

OUT="${OUT:-$ROOT/dist}"
APPDIR="$OUT/$NAME.AppDir"
# One rule for the number, for everything that names a build: the launcher's
# csproj resolves it the same way (VERSION, else $VERDITE_VERSION, else the newest
# version tag), so a package can never be named something other than what is
# inside it.
VERSION="${VERSION:-$(VERDITE_GAME_ROOT="$ROOT" bash "$(dirname "${BASH_SOURCE[0]}")/../../scripts/version.sh")}"

rm -rf "$APPDIR"
mkdir -p "$APPDIR/usr/bin" "$APPDIR/usr/share/applications" "$APPDIR/usr/share/icons/hicolor/256x256/apps"

echo "==> publishing linux-x64"
dotnet publish "$ROOT/$NAME.Launcher/$NAME.Launcher.csproj" \
    -c Release -r linux-x64 --self-contained \
    -p:DebugType=none -p:DebugSymbols=false \
    -o "$APPDIR/usr/bin"

# AppRun, not a symlink to the binary: the working directory an AppImage starts
# in is wherever the user invoked it, and $APPDIR is a read-only mount, so the
# launcher's own data-directory handling has to be the thing that decides where
# files go. It does -- this just execs it.
cat > "$APPDIR/AppRun" <<RUN
#!/bin/sh
HERE="\$(dirname "\$(readlink -f "\$0")")"
exec "\$HERE/usr/bin/$LAUNCHER" "\$@"
RUN
chmod +x "$APPDIR/AppRun"

# X-AppImage-Version is what an AppImage reports about itself to the desktop and
# to update tooling; without it the number lives only in the file name, which a
# player renames. Added here rather than kept in the .desktop, so there is still
# one place the version is written.
for d in "$APPDIR/usr/share/applications/$APP_ID.desktop" "$APPDIR/$APP_ID.desktop"; do
    mkdir -p "$(dirname "$d")"
    { cat "$ROOT/packaging/shared/$APP_ID.desktop"; echo "X-AppImage-Version=$VERSION"; } > "$d"
done
# appimagetool refuses an AppDir without the icon its entry names. A port that
# ships no mark (its icon is the game's own, read off the player's disc and put
# into the icon theme by the game, which outranks this copy) gets a transparent
# square, so the build passes and nothing is drawn in its place.
ICON="$ROOT/packaging/shared/$APP_ID.png"
if [ ! -f "$ICON" ]; then
    ICON="$OUT/$APP_ID-blank.png"
    base64 -d > "$ICON" <<'PNG'
iVBORw0KGgoAAAANSUhEUgAAAQAAAAEACAYAAABccqhmAAABFUlEQVR42u3BMQEAAADCoPVP7WsIoAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAeAMBPAAB2ClDBAAAAABJRU5ErkJggg==
PNG
fi
cp "$ICON" "$APPDIR/usr/share/icons/hicolor/256x256/apps/$APP_ID.png"
cp "$ICON" "$APPDIR/$APP_ID.png"

# Licences the artifact is obliged to carry. The port's MIT, the two vendored
# trees, and Noto Sans: tools/RecompOne/patches/0033 embeds that font in
# RecompOne.Runtime.dll, and SIL OFL 1.1 requires its licence to travel with it.
# licenses/ is the tree a player looks for; usr/share/doc keeps the same files
# where a desktop package expects them.
mkdir -p "$APPDIR/licenses" "$APPDIR/usr/share/doc/$APP_ID"
for dest in "$APPDIR/licenses" "$APPDIR/usr/share/doc/$APP_ID"; do
    cp "$ROOT/LICENSE" "$dest/LICENSE"
    cp "$ROOT/tools/RecompOne/LICENSE" "$dest/RecompOne-LICENSE"
    cp "$ROOT/tools/verdite-core/LICENSE" "$dest/verdite-core-LICENSE"
    cp "$ROOT/tools/RecompOne/patches/assets/NotoSans-OFL.txt" "$dest/NotoSans-OFL.txt"
done

# The tag this package was built from, by the same rule that named it.
printf '%s\n' "$VERSION" > "$APPDIR/version.txt"
printf '%s\n' "$VERSION" > "$APPDIR/usr/share/doc/$APP_ID/version.txt"

# A release must not carry a player's cards or settings. Unpacking one over an
# older folder would replace them.
found=$(find "$APPDIR" -type f \( \
    -name 'settings.json' -o -name 'interface.ini' -o \
    -name '*.sav' -o -name '*.sav.bak' -o -name '*.fog' \) || true)
if [ -n "$found" ]; then
    echo "release would ship player files:" >&2
    echo "$found" >&2
    exit 1
fi

echo "==> appimagetool"
TOOL="${APPIMAGETOOL:-}"
if [ -z "$TOOL" ]; then
    TOOL="$OUT/appimagetool"
    if [ ! -x "$TOOL" ]; then
        curl -fsSL -o "$TOOL" \
            https://github.com/AppImage/appimagetool/releases/download/continuous/appimagetool-x86_64.AppImage
        chmod +x "$TOOL"
    fi
fi

# ARCH is read from the environment by appimagetool and is not inferred.
ARCH=x86_64 "$TOOL" --no-appstream "$APPDIR" "$OUT/$NAME-$VERSION-x86_64.AppImage"

echo "done: $OUT/$NAME-$VERSION-x86_64.AppImage"
