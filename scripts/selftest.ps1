#Requires -Version 5.1
<#
.SYNOPSIS
    Acceptance self-test for computer-use.exe.

.DESCRIPTION
    Runs the published CLI and checks the contract an agent relies on: monitor
    geometry, DPI awareness, capture sizes, non-blank window capture, aiming
    accuracy, rejection of points in desktop gaps, the probe, jpg output, a safe
    desktop click and window hit-testing.

    Every check prints PASS, FAIL or SKIP with one line of detail. The script
    exits 1 if any check failed, 0 otherwise. It never asserts on stderr and it
    never clicks inside the terminal it was started from.

.PARAMETER Exe
    Path to computer-use.exe. Default: the Release publish output of this repo.

.PARAMETER Out
    Folder for images written by the test. Default: %TEMP%\computer-use-selftest

.PARAMETER Samples
    Grid points per axis for the aiming check. Default: 3 (25%%, 50%%, 75%%).

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\selftest.ps1
#>
[CmdletBinding()]
param(
    [string]$Exe = '',
    [string]$Out = (Join-Path $env:TEMP 'computer-use-selftest'),
    [int]$Samples = 3
)

$ErrorActionPreference = 'Stop'

# --- state -----------------------------------------------------------------

$script:Failures = 0
$script:Passes = 0
$script:Skips = 0
$script:DpiFailures = 0
$script:LastRaw = ''
$script:LastExit = 0

# --- helpers ---------------------------------------------------------------

function Write-Pass {
    param([string]$Name, [string]$Detail)
    $script:Passes++
    Write-Host ('PASS  {0,-22} {1}' -f $Name, $Detail) -ForegroundColor Green
}

function Write-Fail {
    param([string]$Name, [string]$Detail)
    $script:Failures++
    Write-Host ('FAIL  {0,-22} {1}' -f $Name, $Detail) -ForegroundColor Red
}

function Write-Skip {
    param([string]$Name, [string]$Detail)
    $script:Skips++
    Write-Host ('SKIP  {0,-22} {1}' -f $Name, $Detail) -ForegroundColor Yellow
}

# Runs the exe, parses stdout as one JSON object and returns it.
# Returns $null when there was no parsable JSON; never throws on ok:false.
function Invoke-Cu {
    param(
        [Parameter(ValueFromRemainingArguments = $true)]
        [string[]]$Arguments
    )

    $script:LastRaw = ''
    $script:LastExit = 0

    if ($null -eq $Arguments) { $Arguments = @() }

    $lines = & $Exe @Arguments 2>$null
    $script:LastExit = $LASTEXITCODE

    $raw = ($lines | Out-String).Trim()
    $script:LastRaw = $raw

    if ([string]::IsNullOrWhiteSpace($raw)) { return $null }

    $obj = $null
    try {
        $obj = $raw | ConvertFrom-Json
    }
    catch {
        return $null
    }

    if ($null -eq $obj) { return $null }

    $obj | Add-Member -NotePropertyName '__exit' -NotePropertyValue $script:LastExit -Force -ErrorAction SilentlyContinue

    if ($null -ne $obj.error) {
        if ([string]$obj.error.code -eq 'DpiAwarenessRequired') { $script:DpiFailures++ }
    }

    return $obj
}

function Get-Code {
    param($Obj)
    if ($null -eq $Obj) { return 'no-json' }
    if ($null -eq $Obj.error) { return 'none' }
    return [string]$Obj.error.code
}

function Get-Msg {
    param($Obj)
    if ($null -eq $Obj -or $null -eq $Obj.error) { return '' }
    return [string]$Obj.error.message
}

function Get-ApiDetail {
    param($Obj)
    if ($null -ne $Obj) { return ('error ' + (Get-Code $Obj) + ': ' + (Get-Msg $Obj)) }
    $raw = [string]$script:LastRaw
    if ([string]::IsNullOrWhiteSpace($raw)) { $raw = '<no stdout>' }
    if ($raw.Length -gt 160) { $raw = $raw.Substring(0, 160) + '...' }
    return ('exit ' + $script:LastExit + ', no JSON: ' + $raw)
}

$script:TypeTargetScript = Join-Path $PSScriptRoot 'typetarget.ps1'

function Start-CuTypeTarget {
    if (-not (Test-Path -LiteralPath $script:TypeTargetScript -PathType Leaf)) { return $null }

    try {
        return Start-Process -FilePath 'powershell.exe' -PassThru -ArgumentList @(
            '-NoProfile', '-ExecutionPolicy', 'Bypass', '-WindowStyle', 'Hidden',
            '-File', $script:TypeTargetScript)
    }
    catch {
        return $null
    }
}

function Wait-CuWindow {
    param([string]$TitleGlob, [int]$TimeoutMs = 8000)

    $deadline = [Environment]::TickCount + $TimeoutMs
    while ([Environment]::TickCount -lt $deadline) {
        $found = Invoke-Cu window find --title $TitleGlob
        if ($null -ne $found -and $found.ok -and $null -ne $found.windows -and @($found.windows).Count -ge 1) {
            return @($found.windows)[0]
        }

        Start-Sleep -Milliseconds 200
    }

    return $null
}

function Format-Rect {
    param($Rect)
    if ($null -eq $Rect) { return 'none' }
    return ('{0},{1} {2}x{3}' -f $Rect.x, $Rect.y, $Rect.width, $Rect.height)
}

function Test-InRect {
    param($Rect, [int]$X, [int]$Y)
    if ($null -eq $Rect) { return $false }
    return (($X -ge [int]$Rect.x) -and
            ($X -lt ([int]$Rect.x + [int]$Rect.width)) -and
            ($Y -ge [int]$Rect.y) -and
            ($Y -lt ([int]$Rect.y + [int]$Rect.height)))
}

# --- locate the exe --------------------------------------------------------

$scriptDir = $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($scriptDir)) {
    $scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
}

# The test has to work from three places: inside the repo, inside an unpacked
# package (bin\computer-use.exe next to this script), and from an installed skill
# folder. Search in that order instead of assuming a build tree exists.
$looked = @()

if ([string]::IsNullOrWhiteSpace($Exe)) {
    $candidates = @(
        (Join-Path $scriptDir 'bin\computer-use.exe'),
        (Join-Path $scriptDir '..\bin\computer-use.exe'),
        (Join-Path $env:USERPROFILE '.cursor\skills\computer-use\bin\computer-use.exe'),
        (Join-Path $scriptDir '..\src\ComputerUse\bin\Release\net10.0-windows\win-x64\publish\computer-use.exe')
    )

    foreach ($candidate in $candidates) {
        $looked += $candidate
        if (Test-Path -LiteralPath $candidate -PathType Leaf) {
            $Exe = $candidate
            break
        }
    }

    if ([string]::IsNullOrWhiteSpace($Exe)) {
        $onPath = Get-Command 'computer-use' -ErrorAction SilentlyContinue
        if ($null -ne $onPath) {
            $Exe = $onPath.Source
            $looked += ('PATH: ' + $Exe)
        }
    }
}

if ([string]::IsNullOrWhiteSpace($Exe) -or -not (Test-Path -LiteralPath $Exe -PathType Leaf)) {
    Write-Host 'FAIL  exe                    no computer-use.exe found' -ForegroundColor Red
    Write-Host '      looked at:'
    foreach ($candidate in $looked) {
        Write-Host ('        ' + $candidate)
    }
    Write-Host '      pass -Exe <path>, or build it with:'
    Write-Host '        dotnet publish src/ComputerUse/ComputerUse.csproj -c Release -r win-x64'
    exit 1
}

$Exe = (Resolve-Path -LiteralPath $Exe).Path
if ($Samples -lt 1) { $Samples = 1 }

if (-not (Test-Path -LiteralPath $Out)) {
    New-Item -ItemType Directory -Force -Path $Out | Out-Null
}
$Out = (Resolve-Path -LiteralPath $Out).Path

Write-Host 'computer-use self-test'
Write-Host ('  exe     : ' + $Exe)
Write-Host ('  out     : ' + $Out)
Write-Host ('  samples : ' + $Samples)
Write-Host ''

# --- 1. screen info --------------------------------------------------------

$info = Invoke-Cu screen info
$monitors = @()
$vb = $null

if ($null -eq $info -or -not $info.ok) {
    Write-Fail '1 screen-info' (Get-ApiDetail $info)
}
else {
    if ($null -ne $info.virtualBounds) { $vb = $info.virtualBounds }
    if ($null -ne $info.monitors) { $monitors = @($info.monitors | Where-Object { $null -ne $_ }) }

    $vbOk = ($null -ne $vb -and [int]$vb.width -gt 0 -and [int]$vb.height -gt 0)
    if ($monitors.Count -ge 1 -and $vbOk) {
        $dpiText = 'unknown'
        if ($null -ne $info.awareness) { $dpiText = [string]$info.awareness }
        Write-Pass '1 screen-info' ('monitors=' + $monitors.Count + ' virtualBounds=' + (Format-Rect $vb) + ' dpi=' + $dpiText)
    }
    elseif ($monitors.Count -lt 1) {
        Write-Fail '1 screen-info' 'no monitors reported'
    }
    else {
        Write-Fail '1 screen-info' ('virtualBounds is empty: ' + (Format-Rect $vb))
    }
}

# --- 2. dpi awareness ------------------------------------------------------

$dpiAtCheck2 = $script:DpiFailures
$dpiValue = 'unknown'
if ($null -ne $info -and $null -ne $info.awareness) { $dpiValue = [string]$info.awareness }

$dpiOk = ($script:DpiFailures -eq 0)
if ($dpiValue -ne 'unknown' -and $dpiValue -ne 'per-monitor-v2') { $dpiOk = $false }

if ($dpiOk) {
    Write-Pass '2 dpi-awareness' ('no DpiAwarenessRequired; reported ' + $dpiValue + ' (probe in 7 is authoritative)')
}
else {
    Write-Fail '2 dpi-awareness' ('reported ' + $dpiValue + ', DpiAwarenessRequired failures=' + $script:DpiFailures)
}

# --- 3. per-monitor capture size -------------------------------------------

if ($monitors.Count -eq 0) {
    Write-Fail '3 capture-monitor' 'no monitors to capture'
}
else {
    foreach ($m in $monitors) {
        $label = '3 capture-monitor[' + [string]$m.index + ']'
        $path = Join-Path $Out ('monitor-' + [string]$m.index + '.png')
        $shot = Invoke-Cu screenshot --monitor ([string]$m.index) --out $path

        if ($null -eq $shot -or -not $shot.ok) {
            Write-Fail $label (Get-ApiDetail $shot)
            continue
        }

        $ppp = [double]$shot.geometry.pxPerPoint
        $w = [int]$shot.image.width
        $h = [int]$shot.image.height
        $expW = [int][math]::Round(([double]$m.rect.width) * $ppp)
        $expH = [int][math]::Round(([double]$m.rect.height) * $ppp)

        $sizeOk = (([math]::Abs($w - $expW)) -le 1) -and (([math]::Abs($h - $expH)) -le 1)
        $capOk = ($w -le 1024) -and ($h -le 1024)

        if ($sizeOk -and $capOk) {
            Write-Pass $label ('image ' + $w + 'x' + $h + ' pxPerPoint=' + $ppp.ToString('0.###') + ' expected ' + $expW + 'x' + $expH)
        }
        else {
            Write-Fail $label ('image ' + $w + 'x' + $h + ', expected ' + $expW + 'x' + $expH + ', cap1024=' + $capOk)
        }
    }
}

# --- 4. non-blank foreground window capture --------------------------------

$list = Invoke-Cu window list
$windows = @()
if ($null -ne $list -and $list.ok -and $null -ne $list.windows) {
    $windows = @($list.windows | Where-Object { $null -ne $_ })
}

$fg = $null
foreach ($w in $windows) {
    if ($w.foreground -eq $true) { $fg = $w; break }
}

if ($null -eq $fg) {
    Write-Fail '4 capture-window' 'window list reported no foreground window'
}
else {
    $selector = 'hwnd:' + [string]$fg.hwnd
    $path = Join-Path $Out 'foreground.png'
    $shot = Invoke-Cu screenshot --window $selector --out $path

    if ($null -eq $shot -or -not $shot.ok) {
        Write-Fail '4 capture-window' (Get-ApiDetail $shot)
    }
    else {
        $blank = $false
        if ($null -ne $shot.warnings) {
            foreach ($warning in @($shot.warnings)) {
                if ($null -ne $warning -and ([string]$warning) -match 'blank') { $blank = $true }
            }
        }

        $imgPath = $path
        if ($null -ne $shot.image -and -not [string]::IsNullOrWhiteSpace([string]$shot.image.path)) {
            $imgPath = [string]$shot.image.path
            if (-not [System.IO.Path]::IsPathRooted($imgPath)) {
                $imgPath = Join-Path (Get-Location).Path $imgPath
            }
        }

        $exists = Test-Path -LiteralPath $imgPath -PathType Leaf
        $length = 0
        if ($exists) { $length = (Get-Item -LiteralPath $imgPath).Length }

        if ($blank) {
            Write-Fail '4 capture-window' ('capture warned about blank content for ' + $selector)
        }
        elseif (-not $exists) {
            Write-Fail '4 capture-window' ('image not on disk: ' + $imgPath)
        }
        elseif ($length -le 0) {
            Write-Fail '4 capture-window' ('image is empty: ' + $imgPath)
        }
        else {
            Write-Pass '4 capture-window' ($selector + ' ' + [string]$shot.image.width + 'x' + [string]$shot.image.height + ', ' + $length + ' bytes')
        }
    }
}

# --- 5. aiming accuracy ----------------------------------------------------

$fractions = @()
for ($i = 0; $i -lt $Samples; $i++) {
    $fractions += (($i + 1.0) / ($Samples + 1.0))
}

if ($monitors.Count -eq 0) {
    Write-Fail '5 aim' 'no monitors to aim at'
}
else {
    foreach ($m in $monitors) {
        $label = '5 aim[' + [string]$m.index + ']'
        $maxDelta = 0.0
        $bad = 0
        $count = 0

        foreach ($fx in $fractions) {
            foreach ($fy in $fractions) {
                $count++
                $x = [int]$m.rect.x + [int][math]::Round(([double]$m.rect.width) * $fx)
                $y = [int]$m.rect.y + [int][math]::Round(([double]$m.rect.height) * $fy)
                if ($x -ge ([int]$m.rect.x + [int]$m.rect.width)) { $x = [int]$m.rect.x + [int]$m.rect.width - 1 }
                if ($y -ge ([int]$m.rect.y + [int]$m.rect.height)) { $y = [int]$m.rect.y + [int]$m.rect.height - 1 }

                $at = '{0},{1}' -f $x, $y
                $move = Invoke-Cu mouse move --at $at

                if ($null -eq $move -or -not $move.ok) {
                    $bad++
                    Write-Host ('      move ' + $at + ' -> ' + (Get-ApiDetail $move))
                }
                else {
                    $delta = [double]$move.input.delta
                    if ($delta -gt $maxDelta) { $maxDelta = $delta }
                    if ($delta -gt 3) {
                        $bad++
                        Write-Host ('      move ' + $at + ' -> delta ' + $delta.ToString('0.0') + ' px')
                    }
                }
            }
        }

        $detail = 'max delta ' + $maxDelta.ToString('0.0') + ' px over ' + $count + ' points'
        if ($bad -eq 0) {
            Write-Pass $label $detail
        }
        else {
            Write-Fail $label ($bad.ToString() + '/' + $count.ToString() + ' points missed; ' + $detail)
        }
    }
}

# --- 6. points in desktop gaps must be rejected ----------------------------

if ($null -eq $vb -or [int]$vb.width -lt 2 -or [int]$vb.height -lt 2) {
    Write-Fail '6 dead-zone' 'no usable virtualBounds from screen info'
}
else {
    $need = 50
    $gapPoints = New-Object System.Collections.ArrayList
    $draws = 0
    $vx = [int]$vb.x
    $vy = [int]$vb.y
    $vw = [int]$vb.width
    $vh = [int]$vb.height

    while ($gapPoints.Count -lt $need -and $draws -lt 2000) {
        $draws++
        $px = Get-Random -Minimum $vx -Maximum ($vx + $vw)
        $py = Get-Random -Minimum $vy -Maximum ($vy + $vh)

        $inside = $false
        foreach ($m in $monitors) {
            if (Test-InRect $m.rect $px $py) { $inside = $true; break }
        }
        if (-not $inside) { [void]$gapPoints.Add(('{0},{1}' -f $px, $py)) }
    }

    if ($gapPoints.Count -eq 0) {
        Write-Skip '6 dead-zone' ('no gap between monitors after ' + $draws + ' draws; nothing to reject')
    }
    else {
        $notRejected = 0
        foreach ($at in $gapPoints) {
            $move = Invoke-Cu mouse move --at $at
            if ($null -eq $move) {
                $notRejected++
                Write-Host ('      gap point ' + $at + ' -> ' + (Get-ApiDetail $move))
            }
            elseif ($move.ok) {
                $notRejected++
                Write-Host ('      gap point ' + $at + ' -> ok (should be PointNotOnMonitor)')
            }
            elseif ((Get-Code $move) -ne 'PointNotOnMonitor') {
                $notRejected++
                Write-Host ('      gap point ' + $at + ' -> ' + (Get-Code $move) + ' (expected PointNotOnMonitor)')
            }
        }

        if ($notRejected -eq 0) {
            Write-Pass '6 dead-zone' ($gapPoints.Count.ToString() + ' gap points rejected with PointNotOnMonitor (' + $draws + ' draws)')
        }
        else {
            Write-Fail '6 dead-zone' ($notRejected.ToString() + '/' + $gapPoints.Count.ToString() + ' gap points were not rejected')
        }
    }
}

# --- 7. probe report -------------------------------------------------------

$probe = Invoke-Cu screen run-probe --samples 2
if ($null -eq $probe -or -not $probe.ok) {
    Write-Fail '7 run-probe' (Get-ApiDetail $probe)
}
else {
    $report = $probe.report
    $names = @()
    if ($null -ne $report) { $names = @($report.PSObject.Properties.Name) }

    $hasMonitors = $names -contains 'monitors'
    $hasMapping = $names -contains 'mapping'
    $hasVerdict = $names -contains 'verdict'

    $verdict = ''
    if ($hasVerdict) { $verdict = [string]$report.verdict }

    if ($hasMonitors -and $hasMapping -and $hasVerdict) {
        Write-Pass '7 run-probe' ('report.ok; verdict: ' + $verdict)
    }
    else {
        Write-Fail '7 run-probe' ('report missing ' + (($names -join ',') + '; has monitors=' + $hasMonitors + ' mapping=' + $hasMapping + ' verdict=' + $hasVerdict))
    }
}

# --- 8. jpg output ---------------------------------------------------------

$targetWindow = $fg
if ($null -eq $targetWindow -and $windows.Count -gt 0) { $targetWindow = $windows[0] }

if ($null -eq $targetWindow) {
    Write-Fail '8 jpg' 'no window to capture'
}
else {
    $selector = 'hwnd:' + [string]$targetWindow.hwnd
    $path = Join-Path $Out 'window.jpg'
    $shot = Invoke-Cu screenshot --window $selector --format jpg --out $path

    if ($null -eq $shot -or -not $shot.ok) {
        Write-Fail '8 jpg' (Get-ApiDetail $shot)
    }
    else {
        $reported = $path
        if ($null -ne $shot.image -and -not [string]::IsNullOrWhiteSpace([string]$shot.image.path)) {
            $reported = [string]$shot.image.path
        }
        if ($reported.ToLowerInvariant().EndsWith('.jpg')) {
            Write-Pass '8 jpg' ('image.path ' + $reported)
        }
        else {
            Write-Fail '8 jpg' ('image.path does not end with .jpg: ' + $reported)
        }
    }
}

# --- 9. click an empty desktop point ---------------------------------------

$clickedLabel = $null
$consoleHit = $false

if ($null -eq $fg) {
    Write-Fail '9 click-desktop' 'no foreground window to stay out of'
}
elseif ($monitors.Count -eq 0) {
    Write-Fail '9 click-desktop' 'no monitors'
}
else {
    $fgCx = [int]$fg.rect.x + [int][math]::Floor(([double]$fg.rect.width) / 2.0)
    $fgCy = [int]$fg.rect.y + [int][math]::Floor(([double]$fg.rect.height) / 2.0)

    $mon = $monitors[0]
    foreach ($m in $monitors) {
        if (Test-InRect $m.rect $fgCx $fgCy) { $mon = $m; break }
    }

    $wa = $mon.workArea
    if ($null -eq $wa) { $wa = $mon.rect }

    $inset = 8
    $wx = [int]$wa.x
    $wy = [int]$wa.y
    $ww = [int]$wa.width
    $wh = [int]$wa.height

    $candidates = @(
        @(($wx + $inset), ($wy + $wh - $inset)),
        @(($wx + $ww - $inset), ($wy + $wh - $inset)),
        @(($wx + $inset), ($wy + $inset)),
        @(($wx + $ww - $inset), ($wy + $inset))
    )

    $chosen = $null
    $chosenClass = ''

    foreach ($candidate in $candidates) {
        $cx = [int]$candidate[0]
        $cy = [int]$candidate[1]

        # Never click the foreground window.
        if (Test-InRect $fg.rect $cx $cy) { continue }

        # Never click any other listed window either.
        $blocked = $false
        foreach ($w in $windows) {
            if (Test-InRect $w.rect $cx $cy) { $blocked = $true; break }
        }
        if ($blocked) { continue }

        $at = '{0},{1}' -f $cx, $cy
        $pointInfo = Invoke-Cu window point-info --at $at
        if ($null -eq $pointInfo -or -not $pointInfo.ok) { continue }

        $pointClass = ''
        $pointProcess = ''
        if ($null -ne $pointInfo.point) {
            if ($null -ne $pointInfo.point.class) { $pointClass = [string]$pointInfo.point.class }
            if ($null -ne $pointInfo.point.process) { $pointProcess = ([string]$pointInfo.point.process).ToLowerInvariant() }
        }

        $isConsole = ($pointClass -eq 'ConsoleWindowClass') -or
                     ($pointProcess -eq 'powershell') -or
                     ($pointProcess -eq 'powershell_ise') -or
                     ($pointProcess -eq 'cmd') -or
                     ($pointProcess -eq 'conhost') -or
                     ($pointProcess -eq 'windowsterminal') -or
                     ($pointProcess -eq 'openconsole')
        if ($isConsole) { $consoleHit = $true; continue }

        $chosen = $candidate
        $chosenClass = $pointClass
        break
    }

    if ($null -eq $chosen) {
        # Nothing free on the desktop (a maximised window covers it, or the only
        # candidates are consoles). Click inside a window we own instead: same
        # closed-loop click path, window-space coordinates.
        $self = $null
        try {
            $self = Start-CuTypeTarget
            if ($null -eq $self) {
                Write-Skip '9 click-window' 'no free desktop point and no typing helper to click inside'
            }
            else {
                $selfWindow = Wait-CuWindow -TitleGlob 'CU-TYPE-TARGET*'
                if ($null -eq $selfWindow) {
                    Write-Skip '9 click-window' 'the typing helper window never appeared'
                }
                else {
                    $selfAt = '60,40'
                    $selfClick = Invoke-Cu mouse click --window ('hwnd:' + [string]$selfWindow.hwnd) --at $selfAt

                    if ($null -eq $selfClick -or -not $selfClick.ok) {
                        Write-Fail '9 click-window' (Get-ApiDetail $selfClick)
                    }
                    else {
                        $selfDelta = 'n/a'
                        if ($null -ne $selfClick.input -and $null -ne $selfClick.input.delta) {
                            $selfDelta = [string]$selfClick.input.delta
                        }

                        Write-Pass '9 click-window' ('clicked ' + $selfAt + ' inside our own window ' + [string]$selfWindow.hwnd + ' (delta ' + $selfDelta + ')')
                        $clickedLabel = $selfAt + ' (window-space, in the typing helper)'
                    }
                }
            }
        }
        finally {
            if ($null -ne $self) {
                Stop-Process -Id $self.Id -Force -ErrorAction SilentlyContinue
            }
        }
    }
    else {
        $at = '{0},{1}' -f [int]$chosen[0], [int]$chosen[1]
        $click = Invoke-Cu mouse click --at $at

        if ($null -eq $click -or -not $click.ok) {
            Write-Fail '9 click-desktop' ('click ' + $at + ' -> ' + (Get-ApiDetail $click))
        }
        else {
            $after = Invoke-Cu window list
            if ($null -ne $after -and $after.ok) {
                Write-Pass '9 click-desktop' ('clicked ' + $at + ' (class ' + $chosenClass + '); window list still answers')
            }
            else {
                Write-Fail '9 click-desktop' ('window list failed after click: ' + (Get-ApiDetail $after))
            }
            $clickedLabel = $at
        }
    }
}

# --- 10. point-info inside the foreground window ---------------------------

if ($null -eq $fg) {
    Write-Fail '10 point-info' 'no foreground window'
}
else {
    $px = [int]$fg.rect.x + [int][math]::Floor(([double]$fg.rect.width) / 2.0)
    $py = [int]$fg.rect.y + [int][math]::Floor(([double]$fg.rect.height) / 2.0)
    $at = '{0},{1}' -f $px, $py

    $pointInfo = Invoke-Cu window point-info --at $at
    if ($null -eq $pointInfo -or -not $pointInfo.ok) {
        Write-Fail '10 point-info' (Get-ApiDetail $pointInfo)
    }
    else {
        $hwnd = ''
        if ($null -ne $pointInfo.point -and $null -ne $pointInfo.point.hwnd) {
            $hwnd = [string]$pointInfo.point.hwnd
        }

        if ($hwnd -match '^0x[0-9A-Fa-f]+$') {
            Write-Pass '10 point-info' ('point ' + $at + ' -> ' + $hwnd)
        }
        else {
            Write-Fail '10 point-info' ('point ' + $at + ' -> hwnd "' + $hwnd + '" is not a 0x handle')
        }
    }
}

# --- 11. typing round-trip through a throwaway window ----------------------

# The only honest way to test typing is to type into something that reports back
# and is not a real document: typetarget.ps1 mirrors its TextBox into its title.
$typingText = 'quote" backtick` dollar$ amp& lt< CJK:' + [char]0x4E2D + [char]0x6587 + ' hello'
$targetScript = $script:TypeTargetScript

if (-not (Test-Path -LiteralPath $targetScript -PathType Leaf)) {
    Write-Skip '11 typing' ('helper not found: ' + $targetScript)
}
else {
    $typer = $null
    try {
        $typer = Start-CuTypeTarget
        Start-Sleep -Milliseconds 1200

        $targetWindow = Wait-CuWindow -TitleGlob 'CU-TYPE-TARGET*'
        $targetList = @()
        if ($null -ne $targetWindow) {
            $targetList = @($targetWindow)
        }

        if ($targetList.Count -lt 1) {
            Write-Skip '11 typing' 'the typing target window never appeared (session may not allow windows)'
        }
        else {
            $hwnd = [string]$targetList[0].hwnd

            # PowerShell 5.1 does not re-quote native arguments, so a literal quote inside
            # --text can be eaten before the exe ever sees it. That is exactly why the CLI
            # offers --text-b64: the test drives the encoding-safe path.
            $b64 = [System.Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes($typingText))
            $typed = Invoke-Cu keyboard type --window ('hwnd:' + $hwnd) --text-b64 $b64

            if ($null -eq $typed -or -not $typed.ok) {
                Write-Fail '11 typing' (Get-ApiDetail $typed)
            }
            else {
                Start-Sleep -Milliseconds 400
                $again = Invoke-Cu window find --title 'CU-TYPE-TARGET*'
                $title = ''
                if ($null -ne $again -and $again.ok -and $null -ne $again.windows -and @($again.windows).Count -ge 1) {
                    $title = [string]@($again.windows)[0].title
                }

                $expected = 'CU-TYPE-TARGET|' + $typingText
                if ($title -eq $expected) {
                    Write-Pass '11 typing' ('round-tripped ' + $typingText.Length + ' chars incl. quote, backtick, $, &, < and CJK')
                }
                else {
                    Write-Fail '11 typing' ('echo mismatch: expected "' + $expected + '" got "' + $title + '"')
                }
            }
        }
    }
    finally {
        if ($null -ne $typer) {
            Stop-Process -Id $typer.Id -Force -ErrorAction SilentlyContinue
        }
    }
}

# --- summary ---------------------------------------------------------------

if ($script:DpiFailures -gt $dpiAtCheck2) {
    Write-Fail '2 dpi-awareness' ($script:DpiFailures.ToString() + ' call(s) returned DpiAwarenessRequired during the run')
}

Write-Host ''
if ($null -ne $clickedLabel) {
    Write-Host ('  click point used: ' + $clickedLabel)
}
Write-Host ('  passed=' + $script:Passes + ' failed=' + $script:Failures + ' skipped=' + $script:Skips + ' dpi-failures=' + $script:DpiFailures)

if ($script:Failures -gt 0) {
    Write-Host 'RESULT: FAIL' -ForegroundColor Red
    exit 1
}

Write-Host 'RESULT: PASS' -ForegroundColor Green
exit 0
