---
name: computer-use
description: >-
  Control the Windows desktop: observe with screenshots and geometry, act with
  self-verifying mouse and keyboard commands. Use when a task needs a real GUI
  (a native window, a dialog, a control with no API) and you must see the screen
  before and after acting on it. Every response is one JSON object, no state is
  kept between calls, and no action is reported as ok unless it was verified.
---

# computer-use

A Windows CLI for agents. One JSON object on stdout per call, exit 0 ok /
1 bad arguments / 2 the machine refused. Nothing is remembered between calls.

## When to use it

- The task needs a GUI: click a dialog, type into a native app, read a window
  that has no API.
- You must prove the effect: every action reads the cursor back from the OS, so
  "it probably worked" is never the answer.
- Not for files, HTTP or text. Those have real APIs; use them.

## The canonical loop

1. **Orient** — whole window plus a grid:
   `computer-use screenshot --window "title:*Notepad*" --grid`
   Read `geometry.pxPerPoint` and the grid labels.
2. **Zoom** — crop and magnify the target:
   `computer-use screenshot --window "title:*Notepad*" --at 640,480 --size 160 --zoom 4`
   At zoom 4 one coordinate point is 4 image pixels.
3. **Aim** — move without clicking, and look:
   `computer-use mouse move --window "title:*Notepad*" --at 640,480 --shot --zoom 4`
   `input.delta` must be 0 and the shot must show the cursor on the target.
   A move never clicks, so a bad aim costs nothing.
4. **Act** — click the point you just confirmed:
   `computer-use mouse click --window "title:*Notepad*" --at 640,480`
5. **Confirm** — look again, with a fresh capture:
   `computer-use screenshot --window "title:*Notepad*" --at 640,480 --size 200 --zoom 2`

One decision, one action, one capture. Never chain two clicks without looking
between them.

### Two things a picture does not tell you

- **Selection is not focus.** A list item can look highlighted while the window's
  internal focus sits in the address bar, and then `F2` / arrows do nothing. Click
  the item, *then* send the shortcut.
- **Inline rename keeps the extension.** Explorer preselects only the base name, so
  typing `beta.txt` into `alpha.txt` gives `beta.txt.txt`. Type `beta` and keep the
  extension the UI already left there.
- After any keyboard step, look again: a shortcut that silently did nothing and one
  that worked look identical from `ok: true`.

## Coordinate spaces

| how the target is selected | what `--at / --from / --to / --rect / --mark` mean |
| --- | --- |
| `--window <sel>` | **window pixels**: origin = the window's DWM extended-frame top-left, physical pixels, may be negative |
| no `--window` | **desktop physical pixels** |
| `--monitor [idx\|device]` | **desktop physical pixels** (screenshots only) |

- Numbers never mix spaces.
- Grid labels and marks are drawn in the space of the command that made them, so
  a number read off the image is a number you can pass straight back.
- A window pixel maps to the desktop as `window.rect + windowPixel`
  (`window.rect` comes from `window list`); prefer `--window` because window
  pixels survive the window moving.

## Reading the response

`geometry` — `origin` (the coordinate point at image pixel 0,0), `pxPerPoint`
(image pixels per coordinate point). Convert with
`point = origin + imagePixel / pxPerPoint`. The image is capped at 1024 px on
its longest side; when `pxPerPoint` < 0.5 a warning says the image is too coarse
to aim at, so crop with `--at/--size/--zoom` instead of squinting.

`input` — `requested` (what you asked for), `landed` (where the cursor was read
back), `landedScreen` (the same point in desktop pixels), `delta` (distance in
px, 0 = exact), `attempts`/`strategy` (diagnostics), `pointerAfter` (cursor after
the action). `ok: true` with `delta` above `--tolerance` never happens: the tool
fails instead.

`warnings` — succeeded, but read the result carefully. A warning containing
"blank" means the capture is empty; the "too coarse" warning means the image is
too small to aim at.

## When it fails

- `PointerNotLanded` — the cursor would not come within `--tolerance` (default
  2 px). Read `error.context.causes`: `dpi-scale` (something rescales the
  coordinate), `edge-crossing` (the cursor stopped at a monitor edge), `dead-zone`
  (the cursor landed in a gap between monitors), `input-hook` (something swallows
  or moves injected input). Recapture, re-read the coordinate, retry once, then stop and
  report.
- `PointerFrozen` — the pointer is pinned: three *different* requested points were all
  refused by the same cursor confinement (or it stopped answering with no confinement at
  all), so the call stopped instead of repeating the wait. `error.context.clip` shows the
  rectangle holding it and `pinnedAt` where it sits. A mouse manager or a remote-desktop
  host is the usual owner. Nudging the pointer by hand once (or restarting the mouse
  manager) releases it; retrying in a loop does not. Any command raises it as soon as a
  confinement outlives the wait and the nudges — a single `mouse click` included — so
  seeing it once means later attempts will fail the same way until the pointer is freed.
- `PointNotOnMonitor` — the point is in a gap between monitors. Re-read
  `screen info`; never interpolate across a gap.
- `CaptureBlank` / a warning containing "blank" — the window returned no pixels:
  minimized, still painting, or a protected surface. Restore it with
  `window move-to-foreground`, recapture with `--settle 200`, or capture the
  `--monitor` instead. Never click into a blank capture.
- `Ambiguous` — the selector matched several windows; use `window find --all`
  and an explicit `hwnd:`.
- `DpiAwarenessRequired` — the process could not become per-monitor-v2 aware;
  the tool refuses to emit coordinates it cannot trust. Stop.
- Exit 1 means your command line is wrong, 2 means the machine refused. Both
  still print JSON: read `error.code`.

## Multi-monitor rules

- The desktop is **not** a filled rectangle: monitors sit at negative
  coordinates, at different sizes and different DPI scales, and gaps exist.
- Never reuse a coordinate or a handle from an earlier response. The tool keeps
  no state, so you must not either.
- One logical step per decision: act, then re-observe.
- Never run two desktop-mutating calls in parallel (no concurrent clicks, drags,
  scrolls, typing, or foreground changes). Serialise them.
- Prefer `--window` over desktop coordinates when the target is a window.

## Typing text safely

Shell quoting damages quotes, backticks and `$`. Pass text through a file:
`--text-file .\prompt.txt`, or base64: `--text-b64 <b64>`. Use `--text` only for
simple literals. `--method scan` sends scancodes for apps that ignore unicode
injection.

## Cheat sheet

```
computer-use screen info
computer-use screenshot --window "hwnd:0x00120AB4" --grid
computer-use screenshot --window "title:*Notepad*" --at 640,480 --size 160 --zoom 4
computer-use screenshot --monitor --cursor
computer-use window list
computer-use window find --process notepad --all
computer-use window move-to-foreground --hwnd 0x00120AB4
computer-use window wait --window "title:*Save*" --appear --timeout 10
computer-use window point-info --at 1200,400
computer-use mouse move --window "hwnd:0x00120AB4" --at 640,480 --shot --zoom 4
computer-use mouse click --window "hwnd:0x00120AB4" --at 640,480
computer-use mouse drag --from 100,100 --to 400,100 --window "hwnd:0x00120AB4"
computer-use mouse scroll --delta -3 --at 640,480
computer-use keyboard type --text-file .\prompt.txt --window "hwnd:0x00120AB4"
computer-use keyboard press "ctrl+s" --window "hwnd:0x00120AB4"
computer-use keyboard press --hold shift
computer-use keyboard press --release shift
```
