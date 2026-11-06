# computer-use

A stateless Windows CLI that lets an agent **look at a screen it can measure** and **act on it in a way
that verifies itself**. One command per call, one JSON object on stdout, nothing remembered between
calls.

It exists because the usual approach — an accessibility tree plus blind clicks — falls apart exactly
where agents are needed most: custom-drawn UIs, games, Electron shells, and desktops with several
monitors at different DPI scales. This tool has no element tree. It has pixels with coordinates you can
trust back, and a pointer that is read back from the OS after every move.

```
computer-use screenshot --window "title:*Notepad*" --grid      # a picture whose ruler is in click coordinates
computer-use screenshot --window "title:*Notepad*" --at 640,480 --size 160 --zoom 4
computer-use mouse move --window "title:*Notepad*" --at 640,480 --shot   # aim, and see where you landed
computer-use mouse click --window "title:*Notepad*" --at 640,480        # press only after the landing check
computer-use screen run-probe --samples 3                      # diagnose a desk: accuracy, seams, gaps
```

The loop is *look → zoom → aim → click → look again*, and every action reports `requested`, `landed`,
`delta` and the strategy that got it there. When the pointer cannot reach the target the command fails
with a cause (`cursor-clipped`, `edge-crossing`, `dead-zone`, `dpi-scale`, `input-hook`,
`PointerFrozen`) instead of reporting success.

Features that matter in practice:

- **Window-pixel coordinates.** With `--window`, every number is a pixel of that window, and the ruler
  drawn by `screenshot --grid` is labelled in the same space, so a number read off the image is a number
  you can click.
- **Capture without stealing focus.** `screenshot --window` asks the window to render itself; it works
  while covered and never changes the foreground window.
- **Closed-loop input.** Absolute move, read the cursor back, escalate strategies, and refuse to press
  when the pointer did not land. Survives pointer-hooking utilities (multi-monitor mouse managers,
  remote-desktop hosts) by nudging their confinements away and routing across monitors one shared edge
  at a time.
- **Honest failures.** A blank capture, a point in a gap between monitors, and a cursor confined to the
  wrong panel are all errors with a named cause, never a plausible-looking success.
- **No state.** No configuration, no cache, no handles or coordinates reused between invocations.

## Requirements

Windows 10 1703 or newer, x64. The shipped binary is a self-contained native executable — no .NET
runtime, no SDK, no installer, no administrator rights.

## Install

The release artifact is a zip of the skill directory. Unpack it wherever you keep agent skills, then
point your agent at `SKILL.md`:

```
computer-use/
    bin/computer-use.exe
    SKILL.md          what an agent reads first
    reference.md      every command, option, JSON field and error code
```

The tool writes nothing outside that folder except screenshots, which go to
`%TEMP%\computer-use\` unless `--out` says otherwise.

## Build from source

Needs the .NET 10 SDK.

```
dotnet publish src/ComputerUse/ComputerUse.csproj -c Release -r win-x64
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\install.ps1      # publish + copy into the skill dir
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\package.ps1      # build the release zip
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\selftest.ps1     # acceptance on this machine
```

`scripts/` is development tooling and is not part of the shipped zip.

## Documentation

- `docs/SKILL.md` — the agent-facing guide (start here)
- `docs/reference.md` — full command/JSON/error reference
- `docs/deploy.md` — what the shipped artifact contains and where to put it
- `docs/notes.md` — engineering notes: what was measured, and the traps worth knowing

## Licence

MIT, see `LICENSE`.
