# Deploying computer-use

The AOT build is **one self-contained native executable** (about 3 MB). The target
machine needs no .NET runtime, no SDK, no admin rights — just the exe and its two
documents in the agent skill folder.

## What gets installed where

```
%USERPROFILE%\.cursor\skills\computer-use\
    bin\computer-use.exe     the tool
    SKILL.md                 what the agent reads every session
    reference.md             full option/JSON/error reference
```

## Path A — copy a package (no toolchain on the target)

1. On the build machine:

   ```powershell
   dotnet publish src/ComputerUse/ComputerUse.csproj -c Release -r win-x64
   ```

   That produces `src/ComputerUse/bin/Release/net10.0-windows/win-x64/publish/computer-use.exe`
   (the `.pdb` is optional and only useful for debugging crashes).

2. Assemble a package (or reuse `dist/computer-use-win-x64.zip` from the build machine):

   ```
   computer-use-win-x64\
       bin\computer-use.exe
       SKILL.md
       reference.md
       install-from-package.ps1
       selftest.ps1
       typetarget.ps1
   ```

3. Move the folder or the zip to the target over whatever channel is at hand
   (remote-desktop file transfer, a share, a USB stick, a cloud drive).

4. On the target, in the extracted folder:

   ```powershell
   powershell -NoProfile -ExecutionPolicy Bypass -File .\install-from-package.ps1
   ```

   The script copies the three files, clears the mark-of-the-web that a file moved
   over a share usually carries (otherwise the first launch can be blocked), prints
   the installed path, and then runs `screen info` — if that fails, nothing was
   installed correctly and it says so.

   `-SkillDir` overrides the destination, `-Path` adds `bin` to the user `PATH`.

## Path B — build on the target

Needed only when that machine also has the source and the **.NET 10 SDK**:

```powershell
git clone <repo> computer-use
cd computer-use
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\install.ps1
```

`install.ps1` publishes and copies in one step. It expects `dotnet` on `PATH`.

## After installing (on the target)

```powershell
computer-use screen info                 # monitors, DPI, virtual desktop, pointer
computer-use screen run-probe --samples 3   # mapping error table, seams, dead zones, capture sanity
powershell -NoProfile -ExecutionPolicy Bypass -File .\selftest.ps1 -Samples 3
```

`run-probe` moves the pointer 40–80 times; do not run it while somebody is typing.
`selftest.ps1` clicks inside a throwaway window it opens itself and types into it,
so it never touches a real document; it skips (never fails) checks the current
session cannot support.

## Notes

- x64 only (`-r win-x64`). An ARM64 machine needs `-r win-arm64`.
- The agent picks up `SKILL.md`/`reference.md` from the skill folder on its next
  session; there is no service to restart.
- Nothing is written outside the skill folder and `%TEMP%\computer-use\` (images).
- If the tool prints `{"ok":false,"error":{"code":"DpiAwarenessRequired"…}}`, it is
  running in a session where per-monitor-v2 awareness cannot be set (a service or a
  locked session). Run it from a normal interactive desktop session.
