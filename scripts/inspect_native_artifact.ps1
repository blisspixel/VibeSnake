[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$ArtifactRoot,

    [Parameter(Mandatory)]
    [ValidateSet("windows-x64", "linux-x64", "macos-universal")]
    [string]$PlatformId,

    [Parameter(Mandatory)]
    [ValidateSet("Debug", "Release")]
    [string]$BuildMode,

    [Parameter(Mandatory)]
    [ValidatePattern("^[0-9a-f]{16}$")]
    [string]$SmokeStateHash,

    [Parameter(Mandatory)]
    [string]$GodotExecutable,

    [Parameter()]
    [string]$GodotArchivePath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$verificationArguments = @{ GodotExecutable = $GodotExecutable }
if ($GodotArchivePath) {
    $verificationArguments.GodotArchivePath = $GodotArchivePath
}
$verificationOutput = & (Join-Path $PSScriptRoot "assert_godot_toolchain.ps1") @verificationArguments
$verificationOutput | Write-Output
$verifiedArchiveLine = $verificationOutput | Where-Object { $_ -like "GodotArchiveSha512=*" } | Select-Object -Last 1
$verifiedExecutableLine = $verificationOutput | Where-Object { $_ -like "GodotExecutableSha256=*" } | Select-Object -Last 1
if (-not $verifiedArchiveLine -or -not $verifiedExecutableLine) {
    throw "Godot verification did not report checksum-bound provenance."
}
$verifiedArchiveHash = $verifiedArchiveLine.Substring("GodotArchiveSha512=".Length)
$verifiedExecutableHash = $verifiedExecutableLine.Substring("GodotExecutableSha256=".Length)
$resolvedArtifactRoot = [System.IO.Path]::GetFullPath($ArtifactRoot)
$comparison = if ($IsWindows) { [StringComparison]::OrdinalIgnoreCase } else { [StringComparison]::Ordinal }
$repositoryPrefix = $repositoryRoot.TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
if ($resolvedArtifactRoot.StartsWith($repositoryPrefix, $comparison)) {
    throw "Artifact inspection must run outside the source checkout: $resolvedArtifactRoot"
}

if (-not (Test-Path -LiteralPath $resolvedArtifactRoot -PathType Container)) {
    throw "Artifact root does not exist: $resolvedArtifactRoot"
}

$manifestPath = Join-Path $resolvedArtifactRoot "artifact-manifest.json"
if (Test-Path -LiteralPath $manifestPath) {
    throw "Refusing to overwrite an existing artifact manifest: $manifestPath"
}

$toolchain = Get-Content -LiteralPath (Join-Path $repositoryRoot "native/toolchain.json") -Raw | ConvertFrom-Json
. (Join-Path $PSScriptRoot "native_artifact_policy.ps1")

function Get-StreamSha256 {
    param([Parameter(Mandatory)][System.IO.Stream]$Stream)

    $hasher = [System.Security.Cryptography.SHA256]::Create()
    try {
        return [Convert]::ToHexString($hasher.ComputeHash($Stream)).ToLowerInvariant()
    } finally {
        $hasher.Dispose()
    }
}

function Assert-ProjectPayloadIsPortable {
    param(
        [Parameter(Mandatory)][byte[]]$Bytes,
        [Parameter(Mandatory)][string]$RelativePath
    )

    $text = [Text.Encoding]::UTF8.GetString($Bytes)
    $sourceNeedles = @(
        $repositoryRoot,
        $repositoryRoot.Replace("\", "/"),
        "packages.lock.json",
        "packages.ExportDebug.lock.json",
        "packages.ExportRelease.lock.json",
        "res://obj/",
        "/src/vibesnake/",
        "\\src\\vibesnake\\"
    ) | Select-Object -Unique

    foreach ($needle in $sourceNeedles) {
        if ($text.Contains($needle, $comparison)) {
            throw "Artifact payload contains a source-tree or development path in $RelativePath."
        }
    }
}

$files = Get-ChildItem -LiteralPath $resolvedArtifactRoot -Recurse -File | Sort-Object FullName
if (-not $files) {
    throw "Artifact root is empty: $resolvedArtifactRoot"
}

$fileEntries = @()
foreach ($file in $files) {
    $relativePath = Assert-NativeArtifactPath -RelativePath ([System.IO.Path]::GetRelativePath($resolvedArtifactRoot, $file.FullName))
    $fileEntries += [ordered]@{
        path = $relativePath
        bytes = $file.Length
        sha256 = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    }

    if ($relativePath -match "(?:^|/)(?:VibeSnake(?:\.Game|\.Persistence|\.Rules)?\.(?:dll|pdb)|VibeSnake\.pck)$") {
        Assert-ProjectPayloadIsPortable -Bytes ([System.IO.File]::ReadAllBytes($file.FullName)) -RelativePath $relativePath
    }
}

$relativePaths = @($fileEntries | ForEach-Object { [string]$_.path })
Assert-ArtifactRespectsContentInventory `
    -InventoryPath (Join-Path $repositoryRoot "config/content_inventory.json") `
    -ArtifactRelativePaths $relativePaths
switch ($PlatformId) {
    "windows-x64" {
        $requiredPatterns = @(
            "^VibeSnake\.exe$",
            "^VibeSnake\.pck$",
            "^data_VibeSnake\.Game_windows_x86_64/VibeSnake\.Game\.dll$",
            "^data_VibeSnake\.Game_windows_x86_64/VibeSnake\.Persistence\.dll$",
            "^data_VibeSnake\.Game_windows_x86_64/VibeSnake\.Rules\.dll$"
        )
    }
    "linux-x64" {
        $requiredPatterns = @(
            "^VibeSnake\.x86_64$",
            "^VibeSnake\.pck$",
            "^data_VibeSnake\.Game_linuxbsd_x86_64/VibeSnake\.Game\.dll$",
            "^data_VibeSnake\.Game_linuxbsd_x86_64/VibeSnake\.Persistence\.dll$",
            "^data_VibeSnake\.Game_linuxbsd_x86_64/VibeSnake\.Rules\.dll$"
        )
    }
    "macos-universal" {
        $requiredPatterns = @("^VibeSnake\.zip$")
    }
}

foreach ($pattern in $requiredPatterns) {
    if (-not ($relativePaths | Where-Object { $_ -match $pattern })) {
        throw "Artifact is missing a required $PlatformId path matching $pattern."
    }
}

$containerEntries = @()
if ($PlatformId -eq "macos-universal") {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archivePath = Join-Path $resolvedArtifactRoot "VibeSnake.zip"
    $archive = [System.IO.Compression.ZipFile]::OpenRead($archivePath)
    try {
        foreach ($entry in ($archive.Entries | Where-Object { $_.Name } | Sort-Object FullName)) {
            $entryPath = Assert-NativeArtifactPath -RelativePath $entry.FullName
            $entryStream = $entry.Open()
            try {
                if ($entryPath -match "(?:^|/)(?:VibeSnake(?:\.Game|\.Persistence|\.Rules)?\.(?:dll|pdb)|VibeSnake\.pck)$") {
                    $memory = [System.IO.MemoryStream]::new()
                    try {
                        $entryStream.CopyTo($memory)
                        $entryBytes = $memory.ToArray()
                        Assert-ProjectPayloadIsPortable -Bytes $entryBytes -RelativePath $entryPath
                        $entryHash = [Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData($entryBytes)).ToLowerInvariant()
                    } finally {
                        $memory.Dispose()
                    }
                } else {
                    $entryHash = Get-StreamSha256 -Stream $entryStream
                }
            } finally {
                $entryStream.Dispose()
            }

            $containerEntries += [ordered]@{
                path = $entryPath
                bytes = $entry.Length
                compressedBytes = $entry.CompressedLength
                sha256 = $entryHash
            }
        }
    } finally {
        $archive.Dispose()
    }

    $macPaths = @($containerEntries | ForEach-Object { [string]$_.path })
    # Godot names the .app from config/name ("Vibe Snake"), while assemblies keep
    # the VibeSnake.* project identifiers.
    $requiredMacPatterns = @(
        "\.app/Contents/MacOS/[^/]+$",
        "\.app/Contents/Resources/[^/]+\.pck$",
        "VibeSnake\.Game\.dll$",
        "VibeSnake\.Persistence\.dll$",
        "VibeSnake\.Rules\.dll$"
    )
    foreach ($pattern in $requiredMacPatterns) {
        if (-not ($macPaths | Where-Object { $_ -match $pattern })) {
            $preview = ($macPaths | Select-Object -First 40) -join "; "
            throw "macOS archive is missing a required path matching $pattern. Entries: $preview"
        }
    }
}

$sourceRevision = if ($env:GITHUB_SHA) { $env:GITHUB_SHA } else { "unavailable" }
$manifest = [ordered]@{
    schemaVersion = 2
    product = "Vibe Snake"
    platform = $PlatformId
    buildMode = $BuildMode
    sourceRevision = $sourceRevision
    godotVersion = [string]$toolchain.godot.version
    godotCommit = [string]$toolchain.godot.commit
    godotArchiveSha512 = $verifiedArchiveHash
    godotExecutableSha256 = $verifiedExecutableHash
    dotnetSdk = [string]$toolchain.dotnetSdk.version
    smokeStateHash = $SmokeStateHash
    fileCount = $fileEntries.Count
    totalBytes = ($files | Measure-Object Length -Sum).Sum
    files = $fileEntries
    containerEntries = $containerEntries
}

$json = $manifest | ConvertTo-Json -Depth 8
[System.IO.File]::WriteAllText($manifestPath, $json + [Environment]::NewLine, [Text.UTF8Encoding]::new($false))
$manifestHash = (Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash.ToLowerInvariant()

if ($env:GITHUB_OUTPUT) {
    "artifact-manifest=$manifestPath" | Out-File -LiteralPath $env:GITHUB_OUTPUT -Encoding utf8 -Append
    "artifact-manifest-sha256=$manifestHash" | Out-File -LiteralPath $env:GITHUB_OUTPUT -Encoding utf8 -Append
}

Write-Output "ArtifactManifest=$manifestPath"
Write-Output "ArtifactManifestSha256=$manifestHash"
Write-Output "ArtifactFileCount=$($fileEntries.Count)"
Write-Output "ArtifactTotalBytes=$($manifest.totalBytes)"
Write-Output "Native artifact inspection passed for $PlatformId."
