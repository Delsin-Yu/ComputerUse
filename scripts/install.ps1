#Requires -Version 5.1
<#
.SYNOPSIS
    Publishes computer-use and installs it as an agent skill folder.

.DESCRIPTION
    Resolves the repository root from this script's folder, runs

        dotnet publish src/ComputerUse/ComputerUse.csproj -c <Configuration> -r win-x64

    from that root, then lays out:

        <SkillDir>\SKILL.md
        <SkillDir>\reference.md
        <SkillDir>\bin\computer-use.exe

    Everything is written under the user profile, so no administrator rights are
    needed. Any failure throws, prints one line and exits non-zero.

.PARAMETER Configuration
    Build configuration passed to dotnet publish. Default: Release.

.PARAMETER NoPublish
    Skip dotnet publish and install whatever is already under bin\.

.PARAMETER SkillDir
    Destination skill folder. Default: $env:USERPROFILE\.cursor\skills\computer-use

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\install.ps1

.EXAMPLE
    .\scripts\install.ps1 -Configuration Debug -SkillDir D:\skills\computer-use
#>
[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [switch]$NoPublish,
    [string]$SkillDir = (Join-Path $env:USERPROFILE '.cursor\skills\computer-use')
)

$ErrorActionPreference = 'Stop'

# --- helpers ---------------------------------------------------------------

function Copy-Artifact {
    param(
        [Parameter(Mandatory = $true)][string]$Source,
        [Parameter(Mandatory = $true)][string]$Destination
    )

    if (-not (Test-Path -LiteralPath $Source -PathType Leaf)) {
        throw "Missing artifact: $Source"
    }

    $parent = Split-Path -Parent $Destination
    if ($parent -and -not (Test-Path -LiteralPath $parent)) {
        New-Item -ItemType Directory -Force -Path $parent | Out-Null
    }

    Copy-Item -LiteralPath $Source -Destination $Destination -Force

    $item = Get-Item -LiteralPath $Destination
    if ($item.Length -ge 1MB) {
        $size = '{0:N1} MB' -f ($item.Length / 1MB)
    }
    elseif ($item.Length -ge 1KB) {
        $size = '{0:N1} KB' -f ($item.Length / 1KB)
    }
    else {
        $size = '{0:N0} bytes' -f $item.Length
    }

    Write-Host ('  {0}  ({1}, {2:N0} bytes on disk)' -f $item.FullName, $size, $item.Length)
}

# --- main ------------------------------------------------------------------

try {
    $scriptDir = $PSScriptRoot
    if ([string]::IsNullOrWhiteSpace($scriptDir)) {
        $scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
    }

    $repoRoot = (Resolve-Path -LiteralPath (Join-Path $scriptDir '..')).Path
    $project = Join-Path $repoRoot 'src\ComputerUse\ComputerUse.csproj'
    $exeName = 'computer-use.exe'
    $publishDir = Join-Path $repoRoot ('src\ComputerUse\bin\{0}\net10.0-windows\win-x64\publish' -f $Configuration)
    $exePath = Join-Path $publishDir $exeName

    Write-Host 'computer-use install'
    Write-Host ('  repo root : {0}' -f $repoRoot)
    Write-Host ('  config    : {0}' -f $Configuration)
    Write-Host ('  skill dir : {0}' -f $SkillDir)

    if (-not (Test-Path -LiteralPath $project -PathType Leaf)) {
        throw "Project not found: $project"
    }

    if (-not $NoPublish) {
        if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
            throw 'dotnet was not found on PATH. Install the .NET SDK, or re-run with -NoPublish.'
        }

        Write-Host ''
        Write-Host ('dotnet publish src/ComputerUse/ComputerUse.csproj -c {0} -r win-x64' -f $Configuration)

        Push-Location -LiteralPath $repoRoot
        try {
            & dotnet publish 'src/ComputerUse/ComputerUse.csproj' -c $Configuration -r win-x64
            if ($LASTEXITCODE -ne 0) {
                throw ("dotnet publish failed with exit code {0}." -f $LASTEXITCODE)
            }
        }
        finally {
            Pop-Location
        }
    }

    # A previous Release publish is a harmless fallback when another
    # configuration was requested but never built.
    if (-not (Test-Path -LiteralPath $exePath -PathType Leaf) -and $Configuration -ne 'Release') {
        $releaseExe = Join-Path $repoRoot 'src\ComputerUse\bin\Release\net10.0-windows\win-x64\publish\computer-use.exe'
        if (Test-Path -LiteralPath $releaseExe -PathType Leaf) {
            Write-Host ('  using existing Release build: {0}' -f $releaseExe)
            $exePath = $releaseExe
        }
    }

    if (-not (Test-Path -LiteralPath $exePath -PathType Leaf)) {
        throw "Publish output not found: $exePath"
    }

    $skillMd = Join-Path $repoRoot 'docs\SKILL.md'
    $referenceMd = Join-Path $repoRoot 'docs\reference.md'

    Write-Host ''
    Write-Host 'installing:'

    Copy-Artifact -Source $exePath -Destination (Join-Path $SkillDir ('bin\' + $exeName))
    Copy-Artifact -Source $skillMd -Destination (Join-Path $SkillDir 'SKILL.md')
    Copy-Artifact -Source $referenceMd -Destination (Join-Path $SkillDir 'reference.md')

    Write-Host ''
    Write-Host ('done. {0}' -f $SkillDir)
    Write-Host ('try  : "{0}" screen info' -f (Join-Path $SkillDir ('bin\' + $exeName)))
    exit 0
}
catch {
    Write-Host ''
    Write-Host ('install failed: {0}' -f $_.Exception.Message) -ForegroundColor Red
    exit 1
}
