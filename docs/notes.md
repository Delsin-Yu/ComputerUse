# Engineering notes

What was measured while building this, and the traps that cost the most time. No machine names, no
paths, no personal data — just the facts that are useful to whoever touches the code next.

## Why there is no element tree

The first version of this tool walked UI Automation and handed the agent `ref`-addressed elements. It
was deleted: for custom-drawn UIs, games, Electron shells and canvas apps the tree is either empty or
wrong, refs go stale the moment the UI moves, and it cost ~1.8k lines of COM vtable code to be wrong.
What replaced it is a picture you can measure and a pointer you can verify.

## Coordinate truth

- **Per-monitor DPI awareness is enforced at startup** and proved by reading it back; if it cannot be
  set, the process refuses to work (`DpiAwarenessRequired`). A DPI-unaware process on a 150% panel
  reports a 3840x2160 screen as 2560x1440 and invents a 1280 px gap next to it.
- `GetAwarenessFromDpiAwarenessContext` reports the same value for both per-monitor contexts, so the
  reported name is `per-monitor`.
- A capture can be smaller than the rectangle it covers (a DPI-unaware window rendering into a scaled
  bitmap), so `geometry.pxPerPoint` is measured, not assumed, and `pxPerPoint != 1` is normal.
- The desktop is not a filled rectangle. Points in gaps are rejected up front (`PointNotOnMonitor`)
  rather than "clicked" into nothing.

## Input

- **A single-jump absolute move is not reliable.** With a multi-monitor mouse management utility
  running, only a small fraction of first attempts land; the rest need a retry, a different mechanism,
  or a short walk. The strategy ladder (`sendinput` → retry → `setcursorpos` → waypoints → planned
  routing) exists because of measurements like "3 of 40 first attempts landed".
- **Pointer confinement is real.** Such utilities can install a monitor-scoped `ClipCursor` when the
  pointer reaches a display edge. Waiting does not lift it (measured: tens of seconds of input
  silence); a small relative move does (measured: ~120 ms), and it must be aimed *into* the confined
  rectangle — a nudge along the clipped axis moves nothing.
- **`ClipCursor` is DPI-aware.** Called from a DPI-unaware process, a clip rectangle is scaled by the
  primary monitor's factor and intersected with the desktop, so the "clip" silently equals the whole
  screen. Both the setter and the reader must be per-monitor aware; otherwise the numbers look sane and
  mean nothing.
- **A confinement can outlive the process that set it.** Any process can clear it with
  `ClipCursor(NULL)`; do that after any experiment that confines the pointer.
- **A wall between two monitors can be a physical-layout artefact.** A mouse manager models panels by
  physical size and position (millimetres), independently of the pixel layout. Where its physical model
  has a gap, its crossing engine refuses to enter — even though the pixel rectangles touch. The pairs
  it does model as touching are the ones that never fail. Nothing in the pixel geometry explains it;
  edited physical entries and unresolved display identity do.
- **Remote-desktop forwarders filter injected input.** One tested host dropped virtual-key injected
  keystrokes while passing scan-code ones: text could be typed into the remote session, but Enter,
  Backspace and Escape never arrived. `keyboard press --method scan` (and `type --method scan`) exist
  for that case. Scan mode is printable ASCII only.
- Relative moves are subject to Windows pointer acceleration, so a "40 px" nudge is approximate; they
  are only used to make motion happen, never to aim.

## Capture

- `PrintWindow` returns a window's own pixels and works while it is covered; some GPU-composed and
  Avalonia/Avalonia-like toolkits return a blank or stale surface, in which case the capture falls back
  to screen pixels and says so in `geometry.capture.source`.
- A blank capture is an error, never a picture of the UI. Nothing is worse for an agent than a
  plausible empty screenshot.
- Windows titles are full of non-ASCII. Output JSON is ASCII-only (`\uXXXX` escapes) because a
  `PowerShell` consumer decodes child-process stdout with the OEM code page and raw UTF-8 CJK breaks
  `ConvertFrom-Json` outright.

## Behavioural notes that are not obvious from a screenshot

- **Selection is not focus.** A list item can look highlighted while the window's internal focus sits
  in an address bar; `F2` then does nothing. Click the item first.
- **Inline rename keeps the extension.** Typing `beta.txt` into a rename box over `alpha.txt` yields
  `beta.txt.txt`.
- A key that was silently dropped and one that worked both look like `ok: true`. Look again after any
  keyboard step.

## Testing approach

Everything the tool claims is measured, not asserted:

- `scripts/selftest.ps1` — geometry, capture sizes, aiming accuracy, gap rejection, a click and a
  typing round-trip into a window it opens itself (never a real document).
- `screen run-probe` — the pointer-accuracy table per monitor, cross-seam crossings, dead-zone
  rejection, capture sanity, environment (pointer settings and any running pointer hook), and with
  `--routes` the planned crossing for every ordered monitor pair.
- Real tasks remain the only way to catch some bugs: a confirmation-image offset that no automated
  check could see was found by renaming a file in Explorer.
