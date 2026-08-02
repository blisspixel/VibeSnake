[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$GodotExecutable,

    [Parameter()]
    [string]$GodotArchivePath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$toolchainPath = Join-Path $repositoryRoot "native/toolchain.json"
$toolchain = Get-Content -LiteralPath $toolchainPath -Raw | ConvertFrom-Json
$expectedVersion = [string]$toolchain.godot.version
$expectedCommit = [string]$toolchain.godot.commit
$expectedIdentity = "$expectedVersion.stable.mono.official.$expectedCommit"

if ($expectedVersion -notmatch "^\d+\.\d+\.\d+$") {
    throw "The pinned Godot version is invalid."
}
if ($expectedCommit -notmatch "^[0-9a-f]{9}$") {
    throw "The pinned Godot commit identity is invalid."
}
if ([string]$toolchain.godot.flavor -ne "dotnet") {
    throw "The pinned Godot flavor must be dotnet."
}

if ($IsWindows) {
    $platformId = "windows-x64"
} elseif ($IsMacOS) {
    $platformId = "macos-universal"
} elseif ($IsLinux) {
    $platformId = "linux-x64"
} else {
    throw "Godot toolchain verification does not support this operating system."
}

$archiveConfig = $toolchain.godot.archives.$platformId
if ($null -eq $archiveConfig) {
    throw "The pinned Godot archive is missing for $platformId."
}
$expectedArchiveHash = ([string]$archiveConfig.sha512).ToLowerInvariant()
if ($expectedArchiveHash -notmatch "^[0-9a-f]{128}$") {
    throw "The pinned Godot archive checksum is invalid for $platformId."
}

$resolvedArchive = if ($GodotArchivePath) {
    [System.IO.Path]::GetFullPath($GodotArchivePath)
} else {
    [System.IO.Path]::GetFullPath(
        (Join-Path $repositoryRoot ".tools/godot/downloads/$([string]$archiveConfig.file)")
    )
}
if (-not (Test-Path -LiteralPath $resolvedArchive -PathType Leaf)) {
    throw "The checksum-pinned Godot archive is unavailable: $resolvedArchive"
}
$actualArchiveHash = (Get-FileHash -LiteralPath $resolvedArchive -Algorithm SHA512).Hash.ToLowerInvariant()
if ($actualArchiveHash -cne $expectedArchiveHash) {
    throw "Godot archive checksum mismatch for $platformId."
}

$resolvedExecutable = [System.IO.Path]::GetFullPath($GodotExecutable)
if (-not (Test-Path -LiteralPath $resolvedExecutable -PathType Leaf)) {
    throw "Godot executable does not exist: $resolvedExecutable"
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
$zipArchive = [System.IO.Compression.ZipFile]::OpenRead($resolvedArchive)
try {
    $executableEntries = @(
        $zipArchive.Entries | Where-Object {
            if ($platformId -eq "windows-x64") {
                return $_.Name -like "Godot*_console.exe"
            }
            if ($platformId -eq "macos-universal") {
                return $_.Name -eq "Godot" -and $_.FullName -match "(?:^|/)Contents/MacOS/Godot$"
            }
            return $_.Name -match "^Godot.*mono_linux.*x86_64$"
        }
    )
    if ($executableEntries.Count -ne 1) {
        throw "The pinned Godot archive must contain exactly one platform editor executable."
    }

    $entryStream = $executableEntries[0].Open()
    $hasher = [System.Security.Cryptography.SHA256]::Create()
    try {
        $expectedExecutableHash = [Convert]::ToHexString($hasher.ComputeHash($entryStream)).ToLowerInvariant()
    } finally {
        $hasher.Dispose()
        $entryStream.Dispose()
    }
} finally {
    $zipArchive.Dispose()
}

$actualExecutableHash = (Get-FileHash -LiteralPath $resolvedExecutable -Algorithm SHA256).Hash.ToLowerInvariant()
if ($actualExecutableHash -cne $expectedExecutableHash) {
    throw "Godot executable bytes do not match the checksum-pinned official archive."
}

$reportedVersionLines = @(& $resolvedExecutable --version 2>&1)
$versionExitCode = $LASTEXITCODE
$reportedVersions = @(
    $reportedVersionLines |
        ForEach-Object { ([string]$_).Trim() } |
        Where-Object { $_ }
)
if ($versionExitCode -ne 0) {
    throw "Godot executable version query failed."
}
if ($reportedVersions.Count -ne 1 -or $reportedVersions[0] -cne $expectedIdentity) {
    $actualIdentity = if ($reportedVersions.Count -eq 1) { $reportedVersions[0] } else { "invalid output" }
    throw "Godot toolchain mismatch. Expected '$expectedIdentity', received '$actualIdentity'."
}

Write-Output "GodotArchiveSha512=$actualArchiveHash"
Write-Output "GodotExecutableSha256=$actualExecutableHash"
Write-Output "GodotVerifiedVersion=$expectedIdentity"
