# computer-use

Stateless Windows CLI for agent computer control. Each invocation runs one action, prints a single JSON object to stdout, and exits. No daemon or session state.

## Build / publish (Native AOT)

```powershell
dotnet publish src/ComputerUse.Cli/ComputerUse.Cli.csproj -c Release -r win-x64
```

Native binary: `src/ComputerUse.Cli/bin/Release/net10.0-windows/win-x64/publish/computer-use.exe`

## Commands

| Command | Purpose |
|---|---|
| `cursor get` | Cursor position + virtual screen bounds |
| `cursor move --x <n> --y <n>` | Absolute cursor move (SendInput, same path as clicks) |
| `mouse click [--button] [--x --y \| --hwnd --ref] [--clicks] [--click-delay-ms]` | Click by coords or snapshot ref |
| `mouse hover --x --y` | Move without clicking |
| `mouse down` / `mouse up` | Button press/release |
| `mouse scroll --delta <n> [--dx <n>] [--x --y]` | Wheel scroll (optional move first) |
| `mouse drag --from-x --from-y --to-x --to-y [--steps 12] [--step-delay-ms 8]` | Stepped drag |
| `key type --text "..." [--delay-ms 0]` | Type Unicode text (batched when delay is 0) |
| `key tap --key <name> [--modifiers ctrl,shift]` | Key down+up |
| `key down` / `key up` `--key <name>` | Hold/release |
| `screenshot --path <file-or-dir> [--x --y --width --height --monitor]` | Absolute / monitor / full desktop PNG |
| `screenshot --path <dir> --hwnd <hwnd>` | Crop to window outer bounds |
| `screenshot --path <dir> --hwnd <hwnd> --ref e14 [--pad 8]` | Crop to snapshot element (+ pad) |
| `screenshot --path <dir> --around-x --around-y --size` | Square POI crop around a screen point |
| `screenshot --path <dir> --hwnd <hwnd> --annotate` | Full-window capture with Set-of-Mark refs |
| `screen info` | Virtual desktop + monitors |
| `window list [--include-empty-title] [--all]` | Top-level windows |
| `window focus --hwnd <0xHEX\|dec>` | Restore + foreground |
| `window snapshot --hwnd <hwnd> [options]` | Agent-friendly ARIA snapshot + ref map |
| `uia dump --hwnd …` | Verbose UIA JSON tree (debug) |
| `uia from-point --x --y` | Element under a screen point |
| `uia set-value --hwnd --ref --value` | ValuePattern set (preferred for edits) |
| `uia invoke\|toggle\|select\|expand\|collapse\|focus --hwnd --ref` | Pattern / focus actions by ref |

### Preferred agent loop

1. `window list` → pick `hwnd`
2. `window focus --hwnd 0x…`
3. `window snapshot --hwnd 0x…` → read YAML `snapshot`, note refs
4. Prefer pattern actions when listed in `elements[ref].actions`:
   - `uia set-value --hwnd 0x… --ref e3 --value "…"`
   - `uia invoke|toggle|select --hwnd 0x… --ref e12`
5. Otherwise click: `mouse click --hwnd 0x… --ref e12`
6. For vision on a POI (esp. Electron/custom UI), prefer a crop over a full desktop shot:
   - `screenshot --path … --hwnd 0x… --ref e14 --pad 12`
   - or `uia from-point` then `screenshot --path … --around-x … --around-y … --size 600`
7. Re-snapshot to verify

### Screenshot path

- Directory (exists, or ends with `\` / `/`): writes `screenshot-yyyyMMddHHmmssfff.png`
- File path: writes exactly that path
- Every response includes `screenshot.region` (screen-space source rect) so image pixels map back to desktop coords
- Do not mix absolute `--x/--y/--width/--height/--monitor` with `--hwnd/--ref/--around-*`

## JSON contract

Success includes `ok: true`, `command`, usually `cursor`, plus payload fields (`snapshot`, `elements`, `element`, `meta`, `screenshot`, `windows`, …).

Failure:

```json
{
  "ok": false,
  "command": "uia set-value",
  "error": { "code": "InvalidArgument", "message": "..." }
}
```

Exit codes: `0` success, `1` argument/usage error, `2` Win32/runtime failure.

## Agent examples

```powershell
computer-use window list
computer-use window focus --hwnd 0x00040C1A
computer-use window snapshot --hwnd 0x00040C1A
computer-use uia set-value --hwnd 0x00040C1A --ref e3 --value "hello"
computer-use mouse click --hwnd 0x00040C1A --ref e20
computer-use uia invoke --hwnd 0x00040C1A --ref e15
computer-use uia from-point --x 400 --y 300
computer-use mouse hover --x 400 --y 300
computer-use screenshot --path C:\Temp\agent\ --hwnd 0x00040C1A --ref e3 --pad 12
computer-use screenshot --path C:\Temp\agent\ --around-x 1280 --around-y 700 --size 600
```

## Notes

- Windows only (`net10.0-windows`)
- Uses `SendInput` for mouse/keyboard, GDI `BitBlt` for screenshots, UI Automation for snapshots and pattern actions
- Refs are assigned in tree-walk order for a given snapshot; re-snapshot after UI changes before reusing refs
- Parse stdout JSON; ignore stderr diagnostics
