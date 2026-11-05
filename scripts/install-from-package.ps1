#Requires -Version 5.1
<#
.SYNOPSIS
    Installs computer-use from an already-built package (no .NET SDK required).

.DESCRIPTION
    The AOT build is a single self-contained native executable, so deploying is three
    files: the exe plus its two documents. This script finds them next to itself (the
    layout of dist\computer-use-win-x64.zip) or one level up, and copies them into the
    agent skill folder:

        <SkillDir>\bin\computer-use.exe
        <SkillDir>\SKILL.md
        <SkillDir>\reference.md

    It then prints the version of the tool it just installed. Per-user, no admin.

.PARAMETER SkillDir
    Destination skill folder. Default: $env:USERPROFILE\.cursor\skills\computer-use

.PARAMETER Path
    Add <SkillDir>\bin to the user PATH when it is missing (default: on).

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File .\install-from-package.ps1
#>
[CmdletBinding()]
param(
    [string]$SkillDir = (Join-Path $env:USERPROFILE '.cursor\skills\computer-use'),
    [switch]$Path
)

$ErrorActionPreference = 'Stop'

function Find-Artifact {
    param([string]$Name)

    foreach ($dir in @($PSScriptRoot, (Split-Path -Parent $PSScriptRoot))) {
        $candidate = Join-Path $dir $Name
        if (Test-Path -LiteralPath $candidate -PathType Leaf) {
            return (Resolve-Path -LiteralPath $candidate).Path
        }

        $nested = Join-Path $dir ('bin\' + $Name)
        if (Test-Path -LiteralPath $nested -PathType Leaf) {
            return (Resolve-Path -LiteralPath $nested).Path
        }
    }

    return $null
}

try {
    Write-Host 'computer-use package install'
    Write-Host ('  skill dir : ' + $SkillDir)

    $exe = Find-Artifact -Name 'computer-use.exe'
    $skill = Find-Artifact -Name 'SKILL.md'
    $reference = Find-Artifact -Name 'reference.md'

    if ($null -eq $exe) { throw 'computer-use.exe not found next to this script (or in .\bin).' }
    if ($null -eq $skill) { throw 'SKILL.md not found next to this script.' }
    if ($null -eq $reference) { throw 'reference.md not found next to this script.' }

    $binDir = Join-Path $SkillDir 'bin'
    if (-not (Test-Path -LiteralPath $binDir)) {
        New-Item -ItemType Directory -Force -Path $binDir | Out-Null
    }

    Copy-Item -LiteralPath $exe -Destination (Join-Path $binDir 'computer-use.exe') -Force
    Copy-Item -LiteralPath $skill -Destination (Join-Path $SkillDir 'SKILL.md') -Force
    Copy-Item -LiteralPath $reference -Destination (Join-Path $SkillDir 'reference.md') -Force

    # A file that travelled over a share or a chat channel carries a mark-of-the-web
    # zone identifier; without this the first launch can be blocked by policy.
    foreach ($name in @('computer-use.exe', 'SKILL.md', 'reference.md')) {
        $target = Join-Path $SkillDir $(if ($name -eq 'computer-use.exe') { 'bin\' + $name } else { $name })
        try { Unblock-File -LiteralPath $target -ErrorAction Stop } catch { }
        Write-Host ('  ' + $target)
    }

    if ($Path) {
        $userPath = [Environment]::GetEnvironmentVariable('Path', 'User')
        $parts = @()
        if (-not [string]::IsNullOrWhiteSpace($userPath)) { $parts = $userPath.Split(';') }
        if (-not ($parts | Where-Object { $_.TrimEnd('\') -ieq $binDir.TrimEnd('\') })) {
            $newPath = (($parts + $binDir) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }) -join ';'
            [Environment]::SetEnvironmentVariable('Path', $newPath, 'User')
            Write-Host ('  added to user PATH: ' + $binDir + '  (open a new shell to pick it up)')
        }
        else {
            Write-Host ('  already on user PATH: ' + $binDir)
        }
    }

    $installed = Join-Path $binDir 'computer-use.exe'
    Write-Host ''
    Write-Host 'installed; asking the tool about this desktop:'
    & $installed screen info
    if ($LASTEXITCODE -ne 0) {
        throw ('the installed tool did not answer (exit ' + $LASTEXITCODE + ')')
    }

    Write-Host ''
    Write-Host 'next:'
    Write-Host ('  & "' + $installed + '" screen run-probe --samples 3')
    Write-Host '  powershell -NoProfile -ExecutionPolicy Bypass -File .\selftest.ps1 -Samples 3'
    exit 0
}
catch {
    Write-Host ''
    Write-Host ('install failed: ' + $_.Exception.Message) -ForegroundColor Red
    exit 1
}
