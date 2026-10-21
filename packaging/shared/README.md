# Packaging assets

`deception.desktop` is the entry the AppImage carries; `package.env` one level up
names the port to Verdite Core's packaging scripts. `verdite2.desktop` is leftover
from the King's Field packaging and is not what this port ships.

`deception.png` (256×256) and `deception.ico` (16, 32, 48, 64, 128, 256) are the
port's icon, made from `Icon.jpg` at the repo root. The `.ico` is the executable's
icon: `Deception.csproj`, the launcher, and the Windows stub. The `.png` is the
window icon, and the picture the AppImage puts under the `deception` app id.
