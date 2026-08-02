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
$toolchain = Get-Content -LiteralPath (Join-Path $repositoryRoot "native/toolchain.json") -Raw | ConvertFrom-Json
$spoofedIdentity = "$([string]$toolchain.godot.version).stable.mono.official.$([string]$toolchain.godot.commit)"

$verificationArguments = @{ GodotExecutable = $GodotExecutable }
if ($GodotArchivePath) {
    $verificationArguments.GodotArchivePath = $GodotArchivePath
}
& (Join-Path $PSScriptRoot "assert_godot_toolchain.ps1") @verificationArguments | Out-Null

$temporaryRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("vibesnake-powershell-gates-{0}" -f [Guid]::NewGuid())
$comparison = if ($IsWindows) { [StringComparison]::OrdinalIgnoreCase } else { [StringComparison]::Ordinal }
try {
    New-Item -ItemType Directory -Path $temporaryRoot | Out-Null
    if ($IsWindows) {
        $fakeExecutable = Join-Path $temporaryRoot "fake-godot.cmd"
        [System.IO.File]::WriteAllText(
            $fakeExecutable,
            "@echo off`r`necho $spoofedIdentity`r`n",
            [Text.UTF8Encoding]::new($false)
        )
    } else {
        $fakeExecutable = Join-Path $temporaryRoot "fake-godot"
        [System.IO.File]::WriteAllText(
            $fakeExecutable,
            "#!/bin/sh`nprintf '%s\n' '$spoofedIdentity'`n",
            [Text.UTF8Encoding]::new($false)
        )
        & chmod +x $fakeExecutable
        if ($LASTEXITCODE -ne 0) {
            throw "Could not make the fake Godot regression fixture executable."
        }
    }

    $fakeArguments = @{ GodotExecutable = $fakeExecutable }
    if ($GodotArchivePath) {
        $fakeArguments.GodotArchivePath = $GodotArchivePath
    }
    try {
        & (Join-Path $PSScriptRoot "assert_godot_toolchain.ps1") @fakeArguments | Out-Null
        throw "Godot verification accepted an executable that only spoofed the pinned version text."
    } catch {
        if ($_.Exception.Message -notlike "Godot executable bytes do not match*") {
            throw
        }
    }

    . (Join-Path $PSScriptRoot "native_artifact_policy.ps1")
    $allowedLauncher = Assert-NativeArtifactPath -RelativePath "VibeSnake.sh"
    if ($allowedLauncher -cne "VibeSnake.sh") {
        throw "Linux product launcher shell must remain allowlisted: $allowedLauncher"
    }
    try {
        Assert-NativeArtifactPath -RelativePath "tools/setup.sh" | Out-Null
        throw "Artifact policy accepted a non-product shell script: tools/setup.sh"
    } catch {
        if ($_.Exception.Message -notlike "Artifact contains prohibited content:*") {
            throw
        }
    }

    $prohibitedPaths = @(
        "python314.dll",
        "libpython3.14.so.1.0",
        "Frameworks/libpython3.14.dylib",
        "Python.framework/Versions/3.14/Python",
        ".env.local",
        "config/.env.production"
    )
    foreach ($prohibitedPath in $prohibitedPaths) {
        try {
            Assert-NativeArtifactPath -RelativePath $prohibitedPath | Out-Null
            throw "Artifact policy accepted prohibited content: $prohibitedPath"
        } catch {
            if ($_.Exception.Message -notlike "Artifact contains prohibited content:*") {
                throw
            }
        }
    }

    $invalidPaths = @(
        "/etc/passwd",
        "C:\secrets.txt",
        "C:secrets.txt",
        "\\server\share\secret.txt",
        ".",
        "bundle/..",
        "bundle/./file.txt",
        "bundle//file.txt"
    )
    foreach ($invalidPath in $invalidPaths) {
        try {
            Assert-NativeArtifactPath -RelativePath $invalidPath | Out-Null
            throw "Artifact policy accepted an invalid path: $invalidPath"
        } catch {
            if ($_.Exception.Message -notlike "Artifact contains an invalid path:*") {
                throw
            }
        }
    }

    . (Join-Path $PSScriptRoot "platform_path_policy.ps1")
    $absoluteFallback = Join-Path $temporaryRoot "data-fallback"
    $resolvedRelativeXdg = Get-AbsoluteEnvironmentPathOrDefault `
        -ConfiguredPath "relative/data" `
        -DefaultPath $absoluteFallback
    if ($resolvedRelativeXdg -cne [System.IO.Path]::GetFullPath($absoluteFallback)) {
        throw "Relative XDG data paths must resolve to the absolute platform fallback."
    }

    $caseCount = 4 + $prohibitedPaths.Count + $invalidPaths.Count
    Write-Output "PowerShell qualification regression checks passed: cases=$caseCount."
} finally {
    if (Test-Path -LiteralPath $temporaryRoot) {
        $temporaryPrefix = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath()).TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
        $resolvedTemporaryRoot = [System.IO.Path]::GetFullPath($temporaryRoot)
        if (-not $resolvedTemporaryRoot.StartsWith($temporaryPrefix, $comparison)) {
            throw "Refusing to clean an unexpected PowerShell gate fixture directory."
        }
        [System.IO.Directory]::Delete($resolvedTemporaryRoot, $true)
    }
}
