# Guarantee that the Godot import cache exists before the native game is launched.
#
# A clean clone and the published source archive both ship the committed `*.import`
# descriptors without the generated `.godot/imported/` payloads they name, because the
# cache is machine-generated and deliberately untracked. Godot only writes that cache
# from an editor pass, so launching the game first fails while loading an imported
# resource. Verify declared destinations and Godot's source MD5 metadata so asset
# edits and damaged caches also receive a headless editor import before launch.
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
        if ($descriptor.Length -gt 65536 -or $descriptor.Attributes -band [IO.FileAttributes]::ReparsePoint) { continue }
        foreach ($line in [System.IO.File]::ReadAllLines($descriptor.FullName)) {
            $trimmed = $line.Trim()
            if (-not $trimmed.StartsWith("dest_files=", [StringComparison]::Ordinal)) {
                continue
            }

            foreach ($match in [regex]::Matches($trimmed, '"res://([^"]+)"')) {
                $destination = Resolve-GodotResourcePath -ProjectRoot $ProjectRoot -RelativePath $match.Groups[1].Value
                if ($destination) { $destinations.Add($destination) }
            }
        }
    }

    return $destinations
}

function Resolve-GodotResourcePath {
    param([string]$ProjectRoot, [string]$RelativePath)
    $comparison = if ($IsWindows) { [StringComparison]::OrdinalIgnoreCase } else { [StringComparison]::Ordinal }
    $prefix = $ProjectRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    try { $path = [IO.Path]::GetFullPath((Join-Path $ProjectRoot $RelativePath)) } catch { return $null }
    if (-not $path.StartsWith($prefix, $comparison)) { return $null }
    $current = $path
    while ($current -and $current.StartsWith($prefix, $comparison)) {
        if (Test-Path -LiteralPath $current) {
            if ((Get-Item -LiteralPath $current -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { return $null }
        }
        $current = Split-Path -Parent $current
    }
    return $path
}

function Get-ImportCacheProblem {
    param([string]$ProjectRoot, [string]$ImportDescriptor)
    $descriptors = @(Get-ChildItem -LiteralPath $ProjectRoot -Recurse -File -Filter "*.import" |
        Where-Object { $_.FullName.Split([IO.Path]::DirectorySeparatorChar) -notcontains ".godot" })
    if ($ImportDescriptor) { $descriptors = @($descriptors | Where-Object { $_.FullName -eq $ImportDescriptor }) }
    foreach ($descriptor in $descriptors) {
        if ($descriptor.Length -gt 65536 -or $descriptor.Attributes -band [IO.FileAttributes]::ReparsePoint) {
            Write-Output "Malformed import descriptor: $($descriptor.Name)"
            continue
        }
        $lines = [IO.File]::ReadAllLines($descriptor.FullName)
        $sources = @($lines | Where-Object { $_ -match '^source_file="res://[^"]+"$' })
        $destinations = @($lines | Where-Object { $_.StartsWith("dest_files=", [StringComparison]::Ordinal) })
        if ($sources.Count -ne 1 -or $destinations.Count -ne 1) {
            Write-Output "Malformed import metadata: $($descriptor.Name)"
            continue
        }
        $sourceRelative = [regex]::Match($sources[0], '"res://([^"]+)"').Groups[1].Value
        $source = Resolve-GodotResourcePath $ProjectRoot $sourceRelative
        $payloads = @([regex]::Matches($destinations[0], '"res://([^"\r\n]+)"'))
        if (-not $source -or -not (Test-Path -LiteralPath $source -PathType Leaf) -or $payloads.Count -eq 0) {
            Write-Output "Missing or unsafe imported source: $($descriptor.Name)"
            continue
        }
        $metadataMatch = [regex]::Match($payloads[0].Groups[1].Value, '^(\.godot/imported/[^/]+-[0-9a-f]{32})(?:\.[^/]+)+$')
        $metadata = if ($metadataMatch.Success) { Resolve-GodotResourcePath $ProjectRoot ($metadataMatch.Groups[1].Value + ".md5") } else { $null }
        if (-not $metadata -or -not (Test-Path -LiteralPath $metadata -PathType Leaf) -or (Get-Item -LiteralPath $metadata).Length -gt 4096) {
            Write-Output "Missing or unsafe import digest: $($descriptor.Name)"
            continue
        }
        if ($descriptor.LastWriteTimeUtc -gt (Get-Item -LiteralPath $metadata).LastWriteTimeUtc) {
            Write-Output "Changed import settings: $($descriptor.Name)"
            continue
        }
        $digests = [IO.File]::ReadAllLines($metadata)
        $sourceDigests = @($digests | Where-Object { $_ -match '^source_md5="[0-9a-f]{32}"$' })
        $destDigests = @($digests | Where-Object { $_ -match '^dest_md5="[0-9a-f]{32}"$' })
        if ($sourceDigests.Count -ne 1 -or $destDigests.Count -ne 1) {
            Write-Output "Malformed import digest: $($descriptor.Name)"
            continue
        }
        $expectedSource = [regex]::Match($sourceDigests[0], '"([0-9a-f]{32})"').Groups[1].Value
        if ((Get-FileHash -LiteralPath $source -Algorithm MD5).Hash.ToLowerInvariant() -cne $expectedSource) {
            Write-Output "Changed imported source: $($descriptor.Name)"
            continue
        }
        foreach ($payload in $payloads) {
            $payloadPath = Resolve-GodotResourcePath $ProjectRoot $payload.Groups[1].Value
            if (-not $payloadPath -or -not (Test-Path -LiteralPath $payloadPath -PathType Leaf)) {
                Write-Output "Missing or unsafe import payload: $($descriptor.Name)"
            } elseif ($payloads.Count -eq 1) {
                $expectedDest = [regex]::Match($destDigests[0], '"([0-9a-f]{32})"').Groups[1].Value
                if ((Get-FileHash -LiteralPath $payloadPath -Algorithm MD5).Hash.ToLowerInvariant() -cne $expectedDest) {
                    Write-Output "Changed import payload: $($descriptor.Name)"
                }
            }
        }
    }
    # The product's imported bitmap assets must also be discovered on first add,
    # before Godot has generated their .import descriptor.
    if (-not $ImportDescriptor) { foreach ($image in Get-ChildItem -LiteralPath $ProjectRoot -Recurse -File -Filter "*.png" |
        Where-Object { $_.FullName.Split([IO.Path]::DirectorySeparatorChar) -notcontains ".godot" }) {
        if (-not (Test-Path -LiteralPath ($image.FullName + ".import") -PathType Leaf)) {
            Write-Output "New bitmap source: $($image.Name)"
        }
    } }
}

$declaredDestinations = @(Get-DeclaredImportDestination -ProjectRoot $resolvedProjectPath)
$missingDestinations = @(
    $declaredDestinations | Where-Object { -not (Test-Path -LiteralPath $_ -PathType Leaf) }
)

Write-Output "GodotImportDeclaredCount=$($declaredDestinations.Count)"
$cacheProblems = @(Get-ImportCacheProblem $resolvedProjectPath)
if ($missingDestinations.Count -eq 0 -and $cacheProblems.Count -eq 0 -and -not $Force) {
    Write-Output "GodotImportCache=Ready"
    exit 0
}

Write-Output "GodotImportMissingCount=$($missingDestinations.Count)"
Write-Output "GodotImportStaleCount=$($cacheProblems.Count)"
Write-Output "Importing Vibe Snake assets for this checkout or asset change."
# Godot compares source digests but does not rehash an existing texture payload.
# Drop only safely contained generated payloads and metadata for each stale entry
# so the editor regenerates it even when the original source bytes are unchanged.
if ($cacheProblems.Count -gt 0) {
    foreach ($descriptor in Get-ChildItem -LiteralPath $resolvedProjectPath -Recurse -File -Filter "*.import" |
        Where-Object { $_.FullName.Split([IO.Path]::DirectorySeparatorChar) -notcontains ".godot" }) {
        if ($descriptor.Length -gt 65536 -or $descriptor.Attributes -band [IO.FileAttributes]::ReparsePoint) { continue }
        if (@(Get-ImportCacheProblem $resolvedProjectPath $descriptor.FullName).Count -eq 0) { continue }
        foreach ($line in [IO.File]::ReadAllLines($descriptor.FullName) | Where-Object { $_.StartsWith("dest_files=", [StringComparison]::Ordinal) }) {
            foreach ($match in [regex]::Matches($line, '"res://(\.godot/imported/[^/]+-[0-9a-f]{32})(?:\.[^/"]+)+"')) {
                $payloadRelative = [regex]::Match($match.Value, '"res://([^"]+)"').Groups[1].Value
                $payloadPath = Resolve-GodotResourcePath $resolvedProjectPath $payloadRelative
                if ($payloadPath -and (Test-Path -LiteralPath $payloadPath -PathType Leaf)) { Remove-Item -LiteralPath $payloadPath -Force }
                $metadata = Resolve-GodotResourcePath $resolvedProjectPath ($match.Groups[1].Value + ".md5")
                if ($metadata -and (Test-Path -LiteralPath $metadata -PathType Leaf)) { Remove-Item -LiteralPath $metadata -Force }
            }
        }
    }
}
# Resource-completion mode plus bounded frame grace avoids the pinned editor's
# immediate C# layout shutdown crash. Cap headless FPS so 180 frames provide
# three seconds for initialization. Exact payload/digest checks below still fail
# an incomplete import, and every nonzero editor exit remains an error.
$importOutput = @(& $resolvedGodotExecutable --headless --editor --path $resolvedProjectPath --import --max-fps 60 --quit-after 180 2>&1)
$importExitCode = $LASTEXITCODE
$importOutput | Write-Output
if ($importExitCode -ne 0) {
    throw "The Godot headless asset import failed with exit code $importExitCode."
}
if ($importOutput | Where-Object { $_ -match "^(?:ERROR|WARNING):" -or $_ -match "ObjectDB instances? (?:was|were) leaked" }) {
    Write-Output "GodotImportFailure=EngineDiagnostics"
    throw "The Godot headless asset import reported an error, warning, or leaked object."
}

$stillMissing = @(
    @(Get-DeclaredImportDestination -ProjectRoot $resolvedProjectPath) |
        Where-Object { -not (Test-Path -LiteralPath $_ -PathType Leaf) }
)
if ($stillMissing.Count -gt 0) {
    throw "The Godot asset import did not produce $($stillMissing.Count) declared destination file(s): $($stillMissing -join ', ')"
}
$stillStale = @(Get-ImportCacheProblem $resolvedProjectPath)
if ($stillStale.Count -gt 0) {
    throw "The Godot asset import did not repair stale metadata or payloads: $($stillStale -join '; ')"
}

Write-Output "GodotImportCache=Rebuilt"
