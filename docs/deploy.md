# Deploying computer-use

The release artifact is a zip of one directory:

```
computer-use/
    bin/computer-use.exe     self-contained native executable, x64
    SKILL.md                 what an agent reads first
    reference.md             full command/JSON/error reference
```

No installer is shipped. Unpack the zip wherever you keep agent skills and point your agent at
`SKILL.md`; the tool itself needs nothing but that directory. It writes nothing outside it except
screenshots, which land in `%TEMP%\computer-use\` unless `--out` names a path.

## Verifying a fresh copy

```
computer-use\bin\computer-use.exe screen info
```

`ok: true` with a `monitors` array means the binary runs on that machine. If instead you get
`{"ok":false,"error":{"code":"DpiAwarenessRequired"…}}`, the tool was started in a session where
per-monitor DPI awareness cannot be set (a service context, or a locked session): it refuses to emit
coordinates it cannot trust. Run it from a normal interactive desktop session.

## Uninstall

Delete the directory. There is no registry footprint, no service, no scheduled task, and no config
file.

## Requirements

Windows 10 1703 or newer, x64. The binary is Native AOT: no .NET runtime, no SDK, no administrator
rights.

## Building the package yourself

On a machine with the .NET 10 SDK:

```
dotnet publish src/ComputerUse/ComputerUse.csproj -c Release -r win-x64
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\package.ps1
```

`scripts/package.ps1` writes `dist\computer-use-skill.zip` containing exactly the three files above.
`scripts/install.ps1` is the developer convenience that publishes and copies straight into a local
skill folder; neither script is part of the shipped zip.
