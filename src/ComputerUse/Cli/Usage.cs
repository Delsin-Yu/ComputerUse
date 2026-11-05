namespace ComputerUse.Cli;

/// <summary>Usage text. Short on purpose: these strings are read by agents, not by people browsing a manual.</summary>
internal static class Usage
{
    public const string Root = """
computer-use - observation-first Windows control for agents.

  computer-use screenshot --window <sel> | --monitor [<idx|device>] [options]
  computer-use screen info
  computer-use screen run-probe [--samples <n>]
  computer-use window list | find | move-to-foreground | wait | point-info
  computer-use mouse move | click | drag | scroll | down | up
  computer-use keyboard type | press

Coordinates: with --window every number is a WINDOW PIXEL (origin = the window frame's
top-left). Without --window, and always with --monitor, numbers are DESKTOP physical pixels.
Every action returns requested/landed/delta and fails when the pointer did not land.
""";

    public const string Screenshot = """
screenshot --window <sel> | --monitor [<idx|device>]
           [--rect x,y,w,h] [--at x,y [--size N]] [--zoom 1..8]
           [--grid [step]] [--cursor] [--mark x,y[@label] ...]
           [--format png|jpg] [--quality 1..100] [--out <path>] [--settle <ms>]

  --window   the window's OWN pixels (PrintWindow); never changes the foreground window
  --monitor  composed screen pixels of one monitor; no value = monitor under the cursor
  --at/--size  square crop centred on a point in the current space (--size defaults
               to 1024/zoom, so --zoom alone fills the image budget)
  --grid     draw ruler lines labelled with the coordinate of that line
  --mark     draw a crosshair (repeatable)
  --cursor    also mark the current pointer position
  --zoom     nearest-neighbour magnification (for small targets)
  --settle   re-capture until two frames match (animations, first paint)

Selector: hwnd:0x00120AB4 | title:*Notepad* | process:notepad | class:Notepad | 0x... 
""";

    public const string Screen = """
screen info
screen run-probe [--samples <n>]

  info       monitors, per-monitor DPI/scale, virtual desktop, pointer position
  run-probe  live diagnosis; moves the pointer ~40-80 times. Reports the mapping error
             table per monitor, cross-seam crossings, dead zones and capture sanity.
""";

    public const string Window = """
window list [--all] [--empty-title]
window find (--title <glob> | --process <glob> | --class <glob> | --hwnd <h>) [--index N] [--all]
window move-to-foreground (--hwnd <h> | --window <sel>)
window wait (--hwnd <h> | --window <sel> | --title <glob> | --process <glob> | --class <glob>)
            (--appear | --disappear) [--timeout <sec>] [--poll-ms <n>] [--all] [--empty-title]
window point-info --at x,y [--window <sel>]
""";

    public const string Mouse = """
mouse move  --at x,y [--window <sel>] [--tolerance 2] [--shot] [--zoom 4]
mouse click --at x,y [--window <sel>] [--button left|right|middle] [--clicks 1] [--tolerance 2] [--shot]
mouse drag  --from x,y --to x,y [--window <sel>] [--steps 16] [--tolerance 2] [--shot]
mouse scroll --delta <n> [--at x,y] [--window <sel>]
mouse down  --button left|right|middle [--at x,y] [--window <sel>]
mouse up    --button left|right|middle
""";

    public const string Keyboard = """
keyboard type (--text <s> | --text-file <path> | --text-b64 <b64>) [--window <sel>] [--delay-ms 0] [--method unicode|scan]
keyboard press <combo> [--window <sel>] [--repeat 1] [--delay-ms 0]
keyboard press --hold <key> | --release <key> [--window <sel>]
""";
}
