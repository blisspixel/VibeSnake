# Guarantee that the Godot import cache exists before the native game is launched.
#
# A clean clone and the published source archive both ship the committed `*.import`
# descriptors without the generated `.godot/imported/` payloads they name, because the
# cache is machine-generated and deliberately untracked. Godot only writes that cache
# from an editor pass, so launching the game first fails while loading an imported
# resource. This script verifies every declared destination and runs one bounded
# headless import when any of them is missing.
#
# Usage: ./scripts/assert_godot_import.ps1 -GodotExecutable <path> [-ProjectPath <path>] [-Force]

[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$GodotExecutable,

    [Parameter()]
    [string]$ProjectPath,

    [Parameter()]
    [switch]$Force
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repositoryRoot = Split-Path -Parent $PSScriptRoot
if (-not $ProjectPath) {
    $ProjectPath = Join-Path $repositoryRoot "game"
}

$resolvedProjectPath = [System.IO.Path]::GetFullPath($ProjectPath)
if (-not (Test-Path -LiteralPath (Join-Path $resolvedProjectPath "project.godot") -PathType Leaf)) {
    throw "The Godot project path does not contain project.godot: $resolvedProjectPath"
}

$resolvedGodotExecutable = [System.IO.Path]::GetFullPath($GodotExecutable)
if (-not (Test-Path -LiteralPath $resolvedGodotExecutable -PathType Leaf)) {
    throw "The pinned Godot executable was not found: $resolvedGodotExecutable"
}

function Get-DeclaredImportDestination {
    param(
        [Parameter(Mandatory)]
        [string]$ProjectRoot
    )

    $destinations = [System.Collections.Generic.List[string]]::new()
    $descriptors = @(
        Get-ChildItem -LiteralPath $ProjectRoot -Recurse -File -Filter "*.import" |
            Where-Object { $_.FullName.Split([System.IO.Path]::DirectorySeparatorChar) -notcontains ".godot" }
    )
    foreach ($descriptor in $descriptors) {
        foreach ($line in [System.IO.File]::ReadAllLines($descriptor.FullName)) {
            $trimmed = $line.Trim()
            if (-not $trimmed.StartsWith("dest_files=", [StringComparison]::Ordinal)) {
                continue
            }

            foreach ($match in [regex]::Matches($trimmed, '"res://([^"]+)"')) {
                $relative = $match.Groups[1].Value -replace "/", [System.IO.Path]::DirectorySeparatorChar
                $destinations.Add((Join-Path $ProjectRoot $relative))
            }
        }
    }

    return $destinations
}

$declaredDestinations = @(Get-DeclaredImportDestination -ProjectRoot $resolvedProjectPath)
$missingDestinations = @(
    $declaredDestinations | Where-Object { -not (Test-Path -LiteralPath $_ -PathType Leaf) }
)

Write-Output "GodotImportDeclaredCount=$($declaredDestinations.Count)"
if ($missingDestinations.Count -eq 0 -and -not $Force) {
    Write-Output "GodotImportCache=Ready"
    exit 0
}

Write-Output "GodotImportMissingCount=$($missingDestinations.Count)"
Write-Output "Importing Vibe Snake assets. This runs once for a new checkout."
& $resolvedGodotExecutable --headless --editor --path $resolvedProjectPath --quit | Write-Output
if ($LASTEXITCODE -ne 0) {
    throw "The Godot headless asset import failed with exit code $LASTEXITCODE."
}

$stillMissing = @(
    @(Get-DeclaredImportDestination -ProjectRoot $resolvedProjectPath) |
        Where-Object { -not (Test-Path -LiteralPath $_ -PathType Leaf) }
)
if ($stillMissing.Count -gt 0) {
    throw "The Godot asset import did not produce $($stillMissing.Count) declared destination file(s): $($stillMissing -join ', ')"
}

Write-Output "GodotImportCache=Rebuilt"
