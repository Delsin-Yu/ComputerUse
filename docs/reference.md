# computer-use — command reference

`computer-use` is one executable that observes and drives the Windows desktop.
Every invocation:

- writes exactly one JSON object to stdout (nothing else),
- resolves its own target and verifies its own effect,
- keeps **no state**: no config file, no cache, no memory of an earlier call.
  Coordinates and handles must never be reused from an earlier response.

Exit codes: `0` ok, `1` bad arguments, `2` the machine refused. A failure still
prints JSON (`ok: false` plus `error`).

## Syntax

```
computer-use <group> <command> [--option value] ...
```

- `--option value` and `--option=value` are equivalent; flags take no value.
- Coordinates are always a single comma token: `--at 1200,-40`. They are never
  split into two arguments, so an inline negative number is never ambiguous.
- `--mark` is repeatable; every other option may be given once.
- An unknown option, a missing value, a malformed pair or an out-of-range number
  is `InvalidArgument` (exit 1) and the response carries `error.help` with the
  command's grammar.

## Window selectors

Anywhere the surface says `--window <sel>` / `--hwnd <h>`:

| form | meaning |
| --- | --- |
| `hwnd:0x00120AB4` | exact window handle |
| `0x00120AB4`, `786612` | bare handle, hex or decimal |
| `title:*Notepad*` | window title glob |
| `process:notepad` | executable-name glob |
| `class:Notepad` | window-class glob |

Globs support `*` and `?` and match case-insensitively. A selector must resolve
to exactly one window: if several match, the call fails with `Ambiguous` and
`error.context` lists the candidates — narrow the glob, or run
`window find --all` and pass an explicit `hwnd:`.

## Coordinate spaces

| selection | numbers in `--at / --from / --to / --rect / --mark` |
| --- | --- |
| `--window <sel>` | **window pixels**: origin = the window's DWM extended-frame top-left, physical pixels, may be negative |
| no `--window` | **desktop physical pixels** |
| `--monitor ...` | **desktop physical pixels** (screenshots only) |

The space a command used is always reported as `target.space`
(`"window"` or `"desktop"`). A window pixel never means a desktop pixel; the
bridge is `window.rect` from `window list`:
`desktop = window.rect.{x,y} + windowPixel`.

The grid labels and marks drawn by `screenshot` are in the same space as the
command, so a number read off the image can be passed straight to `mouse click
--at`. The desktop is not a filled rectangle: monitors may sit at negative
coordinates and points in the gaps belong to no monitor (`PointNotOnMonitor`).

## `screenshot`

```
computer-use screenshot --window <sel> | --monitor [<idx|device>]
                        [--rect x,y,w,h] [--at x,y [--size N]] [--zoom 1..8]
                        [--grid [step]] [--mark x,y[@label] ...] [--cursor]
                        [--format png|jpg] [--quality 1..100] [--out <path>] [--settle <ms>]
```

| option | default | meaning |
| --- | --- | --- |
| `--window <sel>` | — | capture the window's **own** pixels via PrintWindow; works while occluded or behind other windows and never changes the foreground window |
| `--monitor [<idx\|device>]` | the monitor under the cursor | capture the composed screen pixels of one monitor |
| `--rect x,y,w,h` | the whole target | crop, in the target space |
| `--at x,y` | — | crop centred on this point |
| `--size N` | `1024 / zoom` coordinate points | side length of the centred crop; clamped to the target |
| `--zoom 1..8` | 1 | magnification applied before the 1024 px cap |
| `--grid [step]` | off; automatic step (about 80 image px apart) | draw a labelled grid every `step` coordinate points |
| `--mark x,y[@label]` | — | draw one marker, repeatable |
| `--cursor` | off | draw the current cursor position in the image |
| `--format png\|jpg` | `png` | output encoding |
| `--quality 1..100` | 90 | JPEG quality; ignored for png |
| `--out <path>` | `%TEMP%\computer-use-<random>.<ext>` | file to write; the absolute path comes back in `image.path` |
| `--settle <ms>` | 0 | wait this long after resolving the target before reading pixels |

Exactly one of `--window` / `--monitor` is required, and `--rect` / `--at` are
alternatives (`InvalidArgument` otherwise).

Returns: `target`, `image`, `geometry`, `pointer`, `warnings`.

- `--window` renders the window whether or not it is visible; a minimised
  window usually yields `CaptureBlank`.
- `--monitor` includes everything composed on that monitor: other windows, the
  taskbar, the cursor when asked.

## `window list`

```
computer-use window list [--all] [--empty-title]
```

| option | default | meaning |
| --- | --- | --- |
| `--all` | off | also list minimised, hidden and cloaked windows |
| `--empty-title` | off | also list windows whose title is empty |

Returns `windows`: array of window objects, top-most first. A window object is:

| field | meaning |
| --- | --- |
| `hwnd` | handle as a `0x`-prefixed hex string |
| `title` | window title (`""` when the window has none) |
| `class` | Win32 class name |
| `processId` | owning process id |
| `process` | owning executable name |
| `rect` | desktop pixels, DWM extended frame |
| `foreground` | `true` for the current foreground window |
| `minimized` | `true` when iconic |
| `visible` | `true` when `IsWindowVisible` |

## `window find`

```
computer-use window find (--title <glob>|--process <glob>|--class <glob>|--hwnd <h>)
                        [--index N] [--all] [--empty-title]
```

Exactly one selector option is required. `--all` and `--empty-title` widen the
search the same way as in `window list`.

| option | default | meaning |
| --- | --- | --- |
| `--index N` | none; several matches is `Ambiguous` | 0-based pick among the matches |
| `--all` | off | return every match instead of one |

Returns `matches` (how many windows matched) and `window` (the selected window
object). With `--all`, `windows` (array) replaces `window`.

Errors: `NotFound` when nothing matches. Matching several windows is not an
error here because `--index` decides; `Ambiguous` is reserved for the
single-valued `--window` selector.

## `window move-to-foreground`

```
computer-use window move-to-foreground (--hwnd <h>|--window <sel>)
```

Restores the window if minimised and brings it to the foreground with focus.
Returns `window`: the window object afterwards (`foreground` is `true` on
success). Errors: `NotFound`, `Ambiguous`, `Win32Error` if the OS refuses the
foreground change (foreground lock, different integrity level).

## `window wait`

```
computer-use window wait (--hwnd <h>|--window <sel>) (--appear|--disappear)
                        [--timeout <sec>] [--poll-ms <n>]
```

| option | default | meaning |
| --- | --- | --- |
| `--appear` / `--disappear` | required (exactly one) | condition to wait for |
| `--timeout <sec>` | 30 | give up with `Timeout` after this long |
| `--poll-ms <n>` | 100 | how often to re-check |

Returns `condition` (`"appear"` / `"disappear"`), `elapsedMs`, and `window` —
the window object for `--appear`, `null` after a successful `--disappear`.
A `--window`/`--hwnd` selector that matches nothing yet is not an error here:
that is the normal starting state for `--appear`.

## `window point-info`

```
computer-use window point-info --at x,y [--window <sel>]
```

Which window owns a point. Without `--window` the point is a desktop pixel; with
it, a window pixel of that window (so the answer is independent of where the
window sits on screen).

Returns `point`:

| field | meaning |
| --- | --- |
| `x`, `y` | the point, in the command's space |
| `hwnd` | `0x`-prefixed handle of the window at that point, `null` when the OS reports none |
| `class` | class name of that window |
| `processId`, `process` | owning process |
| `window` | full window object at that point (`null` with `hwnd`) |

Use it before a click to learn whether the point you are about to hit belongs to
the window you think it does. Errors: `PointNotOnMonitor` when the point is in a
gap between monitors.

## `screen info`

```
computer-use screen info
```

No options. Returns:

| field | meaning |
| --- | --- |
| `monitors` | array of monitors (see below) |
| `virtualBounds` | rect enclosing every monitor, desktop pixels |
| `awareness` | the process awareness actually in force, e.g. `per-monitor-v2` |
| `pointer` | the current cursor block |

Monitor object: `index` (integer, used by `screenshot --monitor <i>`), `device`
(device name, e.g. `\\.\DISPLAY1`), `primary`, `rect` (desktop pixels), `workArea`
(the part not covered by the taskbar), `dpi` (96 = 100 %), `scale` (percent:
100, 125, 150, ...).

## `screen run-probe`

```
computer-use screen run-probe [--samples <n>]
```

| option | default | meaning |
| --- | --- | --- |
| `--samples <n>` | 3 | capture/measure samples per monitor |

Returns `report`:

| field | meaning |
| --- | --- |
| `monitors` | the monitors as probed, with their measured DPI and scale |
| `mapping` | the per-monitor result of checking that the reported rectangle matches what a capture actually contains (origin, size, `pxPerPoint`) |
| `verdict` | one human-readable line summarising the probe; print it, do not parse it: `exact`, `approximate-within-tolerance`, `remapped`, `blocked-at-seam`, `clipped-by-hook`, `pointer-frozen` |

A mapping point carries `strategy` and, when it applies, `clipWaitMs`, `clipped` and
`frozen`. `frozen` means the pointer had already refused three different targets and
the command stopped paying the clip budget for every further point — the pointer is
being held, not mis-aimed.

Run it when aiming feels wrong on a mixed-DPI or multi-monitor machine.

## `mouse move`

```
computer-use mouse move --at x,y [--window <sel>] [--tolerance <px>] [--shot] [--zoom <n>]
```

| option | default | meaning |
| --- | --- | --- |
| `--at x,y` | required | destination, in the command's space |
| `--window <sel>` | desktop | use window pixels of that window |
| `--tolerance <px>` | 2 | allowed landing error; beyond it the call fails |
| `--shot` | off | attach a post-move capture (`image` + `geometry`) centred on the point |
| `--zoom <n>` | 1 | magnification of that crop |

Returns `target`, `input`, `pointer`, plus `image` and `geometry` with `--shot`.
`move` never clicks.

## `mouse click`

```
computer-use mouse click --at x,y [--window <sel>] [--button left|right|middle]
                        [--clicks 1..10] [--tolerance <px>] [--shot] [--zoom <n>]
```

As `mouse move`, plus:

| option | default | meaning |
| --- | --- | --- |
| `--button` | `left` | which button |
| `--clicks` | 1 | click count (2 = double click) |

Returns `target`, `input`, `pointer`, plus `image`/`geometry` with `--shot`.
The click happens only after the pointer is verified to be within `--tolerance`
of `--at`; a click at the wrong place is never executed and reported as ok.
`--window` does not raise the window: the click lands on whatever is topmost at
that screen point, so check with `window point-info` or raise the window with
`window move-to-foreground` first.

## `mouse drag`

```
computer-use mouse drag --from x,y --to x,y [--window <sel>] [--steps <n>]
                        [--tolerance <px>] [--shot]
```

| option | default | meaning |
| --- | --- | --- |
| `--from x,y` | required | press point |
| `--to x,y` | required | release point |
| `--steps <n>` | 20 | intermediate positions between press and release |
| `--tolerance <px>` | 2 | allowed error at both ends |

Left button press, `steps` interpolated moves, release. `input.requested` /
`input.landed` describe the release point; `input.landedScreen` the same in
desktop pixels. Returns `target`, `input`, `pointer`, plus `image`/`geometry`
with `--shot`.

## `mouse scroll`

```
computer-use mouse scroll --delta <n> [--at x,y] [--window <sel>]
```

| option | default | meaning |
| --- | --- | --- |
| `--delta <n>` | required | wheel notches (-60..60): positive scrolls up (away from you), negative down; one notch is one wheel click (120 wheel units), echoed as `input.wheelUnits` |
| `--at x,y` | current cursor | move the pointer there first (verified like `mouse move`) |

Returns `target`, `input`, `pointer`. Without `--at` there is no aim point, so
`input` carries no `landed`/`delta`/`attempts`/`strategy`.

## `mouse down` / `mouse up`

```
computer-use mouse down --button left|right|middle [--at x,y] [--window <sel>]
computer-use mouse up --button left|right|middle
```

`down` optionally moves (and verifies) first; `up` takes no `--at` and releases
wherever the cursor is, which is how a drag is expressed as
`down --at A` -> `mouse move --at B` -> `up`. Returns `target`, `input`,
`pointer`.

A held button is the one piece of state that outlives the process: the tool does
not track it and cannot release it for you. Always pair `down` with `up`.

## `keyboard type`

```
computer-use keyboard type (--text <s>|--text-file <path>|--text-b64 <b64>)
                        [--window <sel>] [--delay-ms <n>] [--method unicode|scan]
```

| option | default | meaning |
| --- | --- | --- |
| `--text <s>` | one of the three is required | literal text from the command line |
| `--text-file <path>` | | read UTF-8 text from a file |
| `--text-b64 <b64>` | | base64 of the UTF-8 text |
| `--window <sel>` | current focus | the window the text is for (see below) |
| `--delay-ms <n>` | 0 | delay between characters |
| `--method unicode\|scan` | `unicode` | `unicode` injects characters; `scan` injects scancodes for apps that ignore unicode input |

`--text-file` and `--text-b64` exist because shells eat quotes, backticks and
`$`: any text that is not a plain literal should travel through a file or base64.

Returns `target`, `input` (`requested` = the text, `sent` = characters injected,
`method`, `pointerAfter`), `pointer`.

`--window` names and reports the intended receiver but does not raise it: the
text goes to whatever window holds focus. Confirm with `target.window.foreground`
or call `window move-to-foreground` first.

## `keyboard press`

```
computer-use keyboard press <combo> [--window <sel>] [--repeat <n>] [--delay-ms <n>]
computer-use keyboard press --hold <key> | --release <key> [--window <sel>]
```

`<combo>` is `+`-joined: `ctrl+shift+s`, `alt+f4`, `enter`, `f5`. Modifiers are
pressed in order and released in reverse.

| option | default | meaning |
| --- | --- | --- |
| `--repeat <n>` | 1 | send the combo this many times |
| `--delay-ms <n>` | 0 | delay between repeats (and between key down/up) |
| `--hold <key>` | — | press one key and leave it down |
| `--release <key>` | — | release one key |

`--hold` / `--release` exist for key state a single combo cannot express; pair
them yourself. Key names are case-insensitive and include the character keys,
`enter`, `tab`, `esc`/`escape`, `space`, `backspace`, `delete`, `insert`, `home`,
`end`, `pageup`, `pagedown`, `up`/`down`/`left`/`right`, `f1`..`f24`, and the
modifiers `ctrl`/`control`, `shift`, `alt`, `win`/`lwin`/`rwin`. An unrecognised
name fails with `UnknownKey` (exit 1).

Returns `target`, `input` (`requested` = the combo, `sent` = key events injected,
`pointerAfter`), `pointer`.

## The JSON envelope

Success:

```json
{
  "ok": true,
  "command": "mouse click",
  "target": { },
  "image": { },
  "geometry": { },
  "input": { },
  "pointer": { },
  "warnings": [ ],
  "ms": 34
}
```

`ok`, `command` and `ms` (wall time of the whole invocation) are always present;
the other blocks appear only when the command produces them.

Failure:

```json
{
  "ok": false,
  "command": "mouse click",
  "error": {
    "code": "PointerNotLanded",
    "message": "cursor did not land within 2 px of 640,480",
    "context": { },
    "causes": [ "input-hook" ],
    "hints": [ "..." ]
  }
}
```

`error.context` is structured, command-specific data (requested and landed
points, candidate windows, the failing Win32 call and its number). `error.hints`
are short, actionable strings — read them. `error.help` appears only on usage
errors and shows the grammar.

### `target`

| field | meaning |
| --- | --- |
| `kind` | `"window"`, `"monitor"` or `"desktop"` — what the command aimed at |
| `space` | `"window"` or `"desktop"` — the space every number in this response's `input` belongs to |
| `hwnd` | `0x`-prefixed handle (window targets only) |
| `title`, `class`, `process` | identification of a window target |
| `rect` | the target's rectangle in desktop pixels |
| `monitor` | index of the monitor containing the target |
| `foreground`, `minimized` | window state at the moment of the call |

### `image`

| field | meaning |
| --- | --- |
| `path` | absolute path of the written file (readable as soon as the response is printed) |
| `format` | `"png"` or `"jpg"` |
| `width`, `height` | image pixels; the longest side is at most 1024 |
| `bytes` | file size on disk |

### `geometry`

| field | meaning |
| --- | --- |
| `space` | `"window"` or `"desktop"`, as in `target` |
| `origin` | `{x,y}`: the coordinate point that image pixel `0,0` corresponds to (equals the top-left of `target.rect`) |
| `pxPerPoint` | image pixels per coordinate point; `< 1` means the image is downscaled |
| `width`, `height` | image size in image pixels (mirrors `image`) |

Conversion: `point = origin + imagePixel / pxPerPoint`. The result is in
`target.space`; for desktop targets it is already a screen point, for window
targets add `window.rect` to reach the screen.

### `input`

Pointer actions:

| field | meaning |
| --- | --- |
| `requested` | `{x,y}` the point the command was asked to hit, in `target.space` |
| `landed` | `{x,y}` where the cursor actually is, read back from the OS, in `target.space` |
| `landedScreen` | `{x,y}` that same position in desktop physical pixels |
| `delta` | distance in pixels between `requested` and `landed`, as the larger of the two axis differences; `0` when exact |
| `attempts` | how many placement attempts were made (at least 1) |
| `strategy` | identifier of the mechanism that produced the landing (`sendinput`, `sendinput-retry`, `setcursorpos`, `waypoints`, `route`, `sendinput+after-clip`, `sendinput+after-freeze`, `clipped`, `frozen`); diagnostics only — never branch on it |
| `clipWaitMs` | present when the cursor was confined to a rectangle that excluded the target and the move waited for that confinement to lift; the move then succeeded and this is how long it waited |
| `pointerAfter` | `{x,y}` cursor position after the whole action, desktop pixels |

Keyboard actions: `keyboard type` returns `text` (echoed, truncated past 200
characters), `length`, `method` and `delayMs`; `keyboard press` returns `combo`,
`repeat` and `delayMs`, or `hold` / `release` for a single key held down or let
go. There is no `landed`/`delta` because nothing was aimed.

### `pointer`

`{ "x": .., "y": .., "monitor": .. }` — the cursor in desktop physical pixels and
the index of the monitor containing it (`null` if it is on none).

### `warnings`

Array of strings, absent when empty. A warning means the call succeeded but the
result needs care:

- an entry stating that the image is too coarse for aiming (`pxPerPoint` below
  `0.5`): one image pixel covers more than two coordinate points, so recapture a
  crop with `--at`/`--size`/`--zoom` instead of estimating.
- an entry containing `blank`: the captured surface is uniform, i.e. the window
  is still painting, minimised, or protected. Recapture with `--settle`, never
  click into a blank capture.

## Error codes

| code | exit | meaning | what to do |
| --- | --- | --- | --- |
| `InvalidArgument` | 1 | unknown option, missing value, malformed `x,y`, out-of-range number; `error.help` holds the grammar | fix the command line |
| `UnknownKey` | 1 | a key name in `keyboard press` is not recognised | use one of the names in `error.message` |
| `NotFound` | 2 | the selector or handle matched no window, or the monitor index does not exist | re-list with `window list` / `screen info` |
| `Ambiguous` | 2 | a single-window selector matched several windows; `error.context` lists them | narrow the glob, or use `window find --index` and an explicit `hwnd:` |
| `PointNotOnMonitor` | 2 | the point is not inside any monitor rectangle | re-read `screen info`; never interpolate across a gap |
| `PointerNotLanded` | 2 | after every strategy the cursor was still outside `--tolerance` | read `error.context.causes` (`cursor-clipped`, `dpi-scale`, `edge-crossing`, `dead-zone`, `input-hook`) and `error.hints`, recapture, retry once, then report. `cursor-clipped` means the OS confined the cursor to a rectangle that excludes the target (a game, a remote-desktop client or a mouse manager set it) — retry later, the confinement is usually temporary |
| `PointerFrozen` | 2 | three different requested points were refused by the same confinement — or the pointer stopped answering with no confinement at all, which looks the same from here — so the command stopped retrying instead of paying the wait again for each one | `error.context.pinnedAt` is where the pointer sits and `clip` the rectangle holding it (`null` when no confinement explains it). A mouse manager or remote-desktop host usually owns it: move the pointer by hand once (or restart that manager) and retry; retrying in a loop will not. Nothing was clicked. Any command raises it once a confinement outlives the wait and the nudges (a single `mouse click` included), so one occurrence predicts the next attempt will fail the same way until the pointer is freed |
| `CaptureBlank` | 2 | the capture contained no usable pixels | raise the window, add `--settle`, or capture the `--monitor` instead |
| `CaptureFailed` | 2 | the OS refused the capture (invalid target, protected window, resource failure) | see `error.message`; try a crop or the `--monitor` |
| `DpiAwarenessRequired` | 2 | per-monitor-v2 DPI awareness could not be enabled | the tool refuses to emit coordinates it cannot trust; run it from a normal interactive desktop session |
| `InputNotAccepted` | 2 | the OS accepted fewer input events than were injected (UIPI, elevated target, secured desktop) | run the tool at the same integrity level as the target window |
| `Timeout` | 2 | `window wait` never saw the condition within `--timeout` | widen the timeout, or re-check the selector |
| `CaptureRegionOutsideImage` | 2 | the requested crop does not overlap the captured pixels | re-read `geometry.capture`; crop inside the window |
| `InvalidWindow` | 2 | the handle exists but is no longer a window, or was never one | re-list with `window list`; handles are not reusable |
| `NoMonitors` | 2 | the session reported no monitors at all | not a usable desktop session |
| `FocusRefused` | 2 | Windows refused to hand the foreground to the target | the target may be elevated or modal; try again or click it directly |
| `UnsupportedPlatform` | 2 | not running on Windows | use the Windows build |
| `RuntimeError` | 2 | an unexpected exception escaped the command | report it with the command line |
| `Win32Error` | 2 | a Win32 call failed; `error.context` names it with its numeric code | usually a transient window state; retry once |
| `RuntimeError` | 2 | an unexpected internal failure | report `error.message`; the tool gave up rather than guessing |

## How pixels map to desktop coordinates

The image is capped at 1024 px on its longest side, so a large target is
shrunk and one image pixel can cover several coordinate points.
`geometry.pxPerPoint` is the ratio **image pixels per coordinate point** and
`geometry.origin` is the coordinate point under image pixel `0,0`:

```
point = origin + imagePixel / pxPerPoint
```

Worked example — `screenshot --monitor 1` of a monitor at `3840,-1920` with a
1920x1080 rectangle. The image is scaled to `pxPerPoint = 0.4`, so
`origin = (3840, -1920)`. A feature you measure at image pixel `(100, 80)`:

```
x = 3840 + 100 / 0.4 = 3840 + 250 = 4090
y = -1920 + 80 / 0.4 = -1920 + 200 = -1720
```

The desktop point is `4090,-1720`, ready for `mouse click --at 4090,-1720` with
no `--window`. Because `pxPerPoint` is `0.4`, below `0.5`, the response also
carries the "too coarse for aiming" warning: one image pixel is 2.5 coordinate
points, which is why the next capture should be a crop —
`screenshot --monitor 1 --at 4090,-1720 --size 200 --zoom 4` — where one image
pixel is a quarter of a coordinate point.

For a window capture the same arithmetic yields a **window pixel**, because
`geometry.origin` is expressed in `target.space`; `window.rect + windowPixel`
converts it to the desktop, and `input.landedScreen` gives you that conversion
for free after any mouse action.
