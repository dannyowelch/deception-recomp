# Configuration

`settings.json` and `interface.ini` are read from the working directory. `dotnet run` uses the shell's directory. Starting `Deception.exe` uses the folder that contains the executable. A missing field keeps the default below.

## Disc

The recompiler reads `cue` in `config/deception.json/Deception.json`. A rooted path is used as written. A relative path is resolved from `config/deception.json/`.

`CdPath` in `settings.json` is the image the game checks at startup. When it is empty or the file is missing, the window asks you to choose one and saves the path. A first program argument is the image opened for that run after the check accepts `CdPath`.

Cue/bin and CHD version 5 are the formats that open. No PlayStation BIOS is used.

## settings.json

The first controller uses `Pad` in menus and `PadWalk` while walking, and the keyboard uses `KeysMenu` and `Keys` the same way. `PadKind` does not change that.

| Field | Default |
|---|---|
| `CdPath` | `""` |
| `CardAPath`, `CardBPath` | `carda.sav`, `cardb.sav` |
| `CardAEnabled`, `CardBEnabled` | `true` |
| `MasterVolume` | `0.5` |
| `SpuVolume`, `XaVolume` | `1.0` |
| `Muted` | `false` |
| `PadDevice`, `PadDevice2` | `""` |
| `PadKind`, `PadKind2` | `Digital` |
| `Analog` | `true` |
| `MouseLook` | `true` |
| `MouseSensitivityX`, `MouseSensitivityY` | `1` |
| `InvertLookY` | `false` |
| `MouseLeft`, `MouseRight`, `MouseMiddle` | `1` (Cross), `2` (Circle), `4` (Triangle). `0` is none |
| `StickDeadzone` | `0.15` (clamped to 0..0.99 when read) |
| `StickCurve` | `1` |
| `ActiveMods` | `[]` |
| `Keys2`, `Pad2`, `PadAnalog2` | empty second-controller tables |

A key field accepts one string or a list of strings. An empty list clears that control. Mouse numbers run through None, Cross, Circle, Square, Triangle, L1, R1, L2, R2, L3, R3, Start, Select, Up, Down, Left, Right.

Gamepad numbers:

| Code | Control |
|---|---|
| 0-3 | Cross, Circle, Square, Triangle |
| 4, 6 | Select, Start |
| 7, 8 | L3, R3 |
| 9, 10 | L1, R1 |
| 11-14 | D-pad up, down, left, right |
| 100, 101 | L2, R2 |
| 102-105 | Left stick left, right, up, down |
| 106-109 | Right stick left, right, up, down |

`LeftStickX`, `LeftStickY`, `RightStickX`, and `RightStickY` default to axes 0, 1, 2, and 3. Face, Start, Select, L3, and R3 match on the menu pad and the walk pad: Cross 0, Circle 1, Square 2, Triangle 3, Select 4, Start 6, L3 7, R3 8.

Menu pad (`Pad`). The left stick is the D-pad. The right stick is unbound.

| Action | Codes |
|---|---|
| Up, Down | 11 and 104, 12 and 105 |
| Left, Right | 13 and 102, 14 and 103 |
| L1, R1 | 9, 10 |
| L2, R2 | 100, 101 |

Walk pad (`PadWalk`). The D-pad and the shoulder buttons stay, and the sticks add strafe, turn, and look.

| Action | Codes |
|---|---|
| Up, Down | 11 and 104, 12 and 105 |
| Left, Right | 13 and 106, 14 and 107 |
| L1, R1 | 9 and 102, 10 and 103 |
| L2, R2 | 100 and 108, 101 and 109 |

`PadAnalog` keeps those face buttons and binds the D-pad alone (11, 12, 13, 14). The first controller does not use it.

Walk keyboard (`Keys`). Up and Down move, L1 and R1 strafe, Left and Right turn, and L2 and R2 look.

| Action | Keys |
|---|---|
| Cross, Circle, Square, Triangle | Space, C, Z, Tab |
| L1, R1, L2, R2 | A, D, R, F |
| L3, R3 | V, B |
| Start, Select | Enter, ShiftRight |
| Up, Down | W and Up, S and Down |
| Left, Right | Q and Left, E and Right |

Menu keyboard (`KeysMenu`). Face buttons match the walk set. Strafe becomes Left and Right, and the turn keys become L1 and R1.

| Action | Keys |
|---|---|
| L1, R1 | Q, E |
| Left, Right | A and Left, D and Right |

`Analog` set to false leaves the sticks digital. Mouse look has its own switch. Escape captures and releases the pointer.

## interface.ini

The `[RecompOne]` section is the display. A missing key uses the fallback.

| Key | Fallback |
|---|---|
| `RenderScale` | `4`, clamped to 1..8 |
| `Anisotropy` | `16`. One of `1`, `2`, `4`, `8`, `16`. `1` is the console's single texel |
| `Mipmaps` | `True` |
| `Frame` | `16:9`. `4:3` leaves the side columns black |
| `HideBorders` | `False`. With `Frame` at `16:9`, the side columns stay black |
| `GpuBackend` | `auto`. Also `gl45`, `gl33`, `gl21`. Applied on the next launch |
| `VSync` | `false` |
| `Fullscreen`, `Borderless` | `false`. Fullscreen covers the screen. Borderless chooses a borderless window for that |
| `UiScale` | `1`, clamped to 0.5..3 |
| `ShowFps`, `HideTopBar`, `AutoHideMenuBar` | `false` |
| `Language`, `Accent`, `Background` | empty |

The window opens at 1280x720.

`Anisotropy` and `Mipmaps` apply to the GL picture. A glyph or menu drawn at about one texel per pixel stays a single sample. The game picture stays the console's 4:3 frame, fitted the same way in either mode. `Frame` `16:9` draws `border/left-border.jpg` and `border/right-border.jpg` in the columns a wide window leaves beside that picture. A window already near 4:3 has no column, so nothing is drawn. `HideBorders` leaves the columns black. `Frame` `4:3` does not draw them.
