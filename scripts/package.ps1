#Requires -Version 5.1
<#
.SYNOPSIS
    Builds the release artifact: a zip containing only the skill directory.

.DESCRIPTION
    The shipped zip is self-contained and has no installer:

        computer-use/
            bin/computer-use.exe
            SKILL.md
            reference.md

    Publish happens first (Native AOT, win-x64) unless -NoPublish is given. The output lands in
    dist\computer-use-skill.zip. Nothing in scripts\ or docs\dev-only material is included.

.PARAMETER Configuration
    Publish configuration, default Release.

.PARAMETER NoPublish
    Package whatever is already published under bin\.

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\package.ps1
#>
[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [switch]$NoPublish
)

$ErrorActionPreference = 'Stop'

try {
    $scriptDir = $PSScriptRoot
    $repoRoot = (Resolve-Path -LiteralPath (Join-Path $scriptDir '..')).Path
    $publish = Join-Path $repoRoot ('src\ComputerUse\bin\{0}\net10.0-windows\win-x64\publish' -f $Configuration)
    $exe = Join-Path $publish 'computer-use.exe'
    $dist = Join-Path $repoRoot 'dist'
    $stage = Join-Path $dist 'computer-use'
    $zip = Join-Path $dist 'computer-use-skill.zip'

    if (-not $NoPublish) {
        Push-Location -LiteralPath $repoRoot
        try {
            & dotnet publish 'src/ComputerUse/ComputerUse.csproj' -c $Configuration -r win-x64
            if ($LASTEXITCODE -ne 0) { throw ("dotnet publish failed with exit code {0}." -f $LASTEXITCODE) }
        }
        finally {
            Pop-Location
        }
    }

    foreach ($required in @($exe, (Join-Path $repoRoot 'docs\SKILL.md'), (Join-Path $repoRoot 'docs\reference.md'))) {
        if (-not (Test-Path -LiteralPath $required -PathType Leaf)) { throw "Missing artifact: $required" }
    }

    if (Test-Path -LiteralPath $stage) { Remove-Item -Recurse -Force -LiteralPath $stage }
    New-Item -ItemType Directory -Force -Path (Join-Path $stage 'bin') | Out-Null

    Copy-Item -LiteralPath $exe -Destination (Join-Path $stage 'bin\computer-use.exe')
    Copy-Item -LiteralPath (Join-Path $repoRoot 'docs\SKILL.md') -Destination (Join-Path $stage 'SKILL.md')
    Copy-Item -LiteralPath (Join-Path $repoRoot 'docs\reference.md') -Destination (Join-Path $stage 'reference.md')

    if (Test-Path -LiteralPath $zip) { Remove-Item -Force -LiteralPath $zip }
    # Compress the folder itself, so the zip unpacks into computer-use\ rather than scattering files.
    Compress-Archive -Path $stage -DestinationPath $zip -Force
    Remove-Item -Recurse -Force -LiteralPath $stage

    $item = Get-Item -LiteralPath $zip
    $hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash
    Write-Host ''
    Write-Host ('package : {0}' -f $item.FullName)
    Write-Host ('size    : {0:N0} bytes' -f $item.Length)
    Write-Host ('sha256  : {0}' -f $hash)
    Write-Host 'contents: computer-use\bin\computer-use.exe, SKILL.md, reference.md'
    exit 0
}
catch {
    Write-Host ''
    Write-Host ('package failed: ' + $_.Exception.Message) -ForegroundColor Red
    exit 1
}
