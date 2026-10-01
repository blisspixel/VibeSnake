# Fast bootstrap regressions use only isolated fixtures and never launch the game.
[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$repositoryRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot "godot_cache_policy.ps1")
$fixtureRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("vibesnake-bootstrap-{0}" -f [Guid]::NewGuid())
$originalGitHubOutput = $env:GITHUB_OUTPUT
try {
    $env:GITHUB_OUTPUT = $null
    New-Item -ItemType Directory -Path $fixtureRoot | Out-Null
    $source = Join-Path $fixtureRoot "source"
    $extraction = Join-Path $fixtureRoot "editor"
    New-Item -ItemType Directory -Path (Join-Path $source "support") | Out-Null
    [IO.File]::WriteAllText((Join-Path $source "editor"), "verified executable fixture")
    [IO.File]::WriteAllText((Join-Path $source "support/runtime.dll"), "verified runtime fixture")
    $archive = Join-Path $fixtureRoot "editor.zip"
    [IO.Compression.ZipFile]::CreateFromDirectory($source, $archive)
    [IO.Compression.ZipFile]::ExtractToDirectory($archive, $extraction)
    if (-not (Test-GodotArchiveExtraction -ArchivePath $archive -ExtractionRoot $extraction)) {
        throw "Bootstrap cache rejected an exact extraction."
    }
    $runtime = Join-Path $extraction "support/runtime.dll"
    $originalRuntime = [IO.File]::ReadAllBytes($runtime)
    [IO.File]::WriteAllText($runtime, "modified runtime fixture")
    if (Test-GodotArchiveExtraction -ArchivePath $archive -ExtractionRoot $extraction) {
        throw "Bootstrap cache accepted modified supporting assembly bytes."
    }
    [IO.File]::WriteAllBytes($runtime, $originalRuntime)
    $extra = Join-Path $extraction "unexpected.dll"
    [IO.File]::WriteAllText($extra, "unexpected payload")
    if (Test-GodotArchiveExtraction -ArchivePath $archive -ExtractionRoot $extraction) {
        throw "Bootstrap cache accepted an unexpected file."
    }
    Remove-Item -LiteralPath $extra
    Remove-Item -LiteralPath $runtime
    if (Test-GodotArchiveExtraction -ArchivePath $archive -ExtractionRoot $extraction) {
        throw "Bootstrap cache accepted a missing supporting assembly."
    }
    $linkedRoot = Join-Path $fixtureRoot "linked editor"
    $linkKind = if ($IsWindows) { "Junction" } else { "SymbolicLink" }
    New-Item -ItemType $linkKind -Path $linkedRoot -Target $source | Out-Null
    if (Test-GodotArchiveExtraction -ArchivePath $archive -ExtractionRoot $linkedRoot) {
        throw "Bootstrap cache accepted a linked extraction root."
    }
    Remove-Item -LiteralPath $linkedRoot -Force

    # Exercise orchestration in a tiny repository with a synthetic checksum pin.
    # The copied verifier reports a fixed fixture identity and can inject a staged
    # verification failure. The real archive verifier is exercised by the parent gate.
    $bootstrapRoot = Join-Path $fixtureRoot "bootstrap"
    $bootstrapScripts = Join-Path $bootstrapRoot "scripts"
    New-Item -ItemType Directory -Path $bootstrapScripts | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $bootstrapRoot "native") | Out-Null
    foreach ($script in @("install_godot.ps1", "godot_cache_policy.ps1")) {
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot $script) -Destination $bootstrapScripts
    }
    [IO.File]::WriteAllText((Join-Path $bootstrapScripts "assert_godot_toolchain.ps1"), @'
param($GodotExecutable, $GodotArchivePath)
if (Test-Path -LiteralPath (Join-Path $PSScriptRoot "fail-verification")) { throw "Injected staged verification failure" }
Write-Output "GodotVerifiedVersion=fixture"
'@)
    $fixtureExecutable = if ($IsWindows) { "Godot_fixture_console.exe" } elseif ($IsMacOS) { "Godot.app/Contents/MacOS/Godot" } else { "Godot_fixture_mono_linux_x86_64" }
    $fixtureExecutablePath = Join-Path $source $fixtureExecutable
    New-Item -ItemType Directory -Path (Split-Path -Parent $fixtureExecutablePath) -Force | Out-Null
    [IO.File]::WriteAllText($fixtureExecutablePath, "editor fixture")
    $bootstrapArchive = Join-Path $fixtureRoot "bootstrap.zip"
    [IO.Compression.ZipFile]::CreateFromDirectory($source, $bootstrapArchive)
    $platformId = if ($IsWindows) { "windows-x64" } elseif ($IsMacOS) { "macos-universal" } else { "linux-x64" }
    $pin = @{ godot = @{ version = "1.2.3"; archives = @{ $platformId = @{ file = "bootstrap.zip"; sha512 = (Get-FileHash -LiteralPath $bootstrapArchive -Algorithm SHA512).Hash } } } }
    [IO.File]::WriteAllText((Join-Path $bootstrapRoot "native/toolchain.json"), ($pin | ConvertTo-Json -Depth 6))
    $toolsRoot = Join-Path $bootstrapRoot "tools"
    New-Item -ItemType Directory -Path (Join-Path $toolsRoot "downloads") | Out-Null
    Copy-Item -LiteralPath $bootstrapArchive -Destination (Join-Path $toolsRoot "downloads/bootstrap.zip")
    $installer = Join-Path $bootstrapScripts "install_godot.ps1"
    $first = @(& $installer -OutputDirectory $toolsRoot)
    if ($first -notcontains "GodotEditorCache=Rebuilt") { throw "Bootstrap did not install a fresh fixture." }
    $installedRuntime = Join-Path $toolsRoot "1.2.3/support/runtime.dll"
    $stamp = (Get-Item -LiteralPath $installedRuntime).LastWriteTimeUtc
    $second = @(& $installer -OutputDirectory $toolsRoot)
    if ($second -notcontains "GodotEditorCache=Verified" -or
        (Get-Item -LiteralPath $installedRuntime).LastWriteTimeUtc -ne $stamp) { throw "Bootstrap did not reuse verified extraction." }
    [IO.File]::WriteAllText($installedRuntime, "damaged runtime fixture")
    [IO.File]::WriteAllText((Join-Path $bootstrapScripts "fail-verification"), "inject")
    try {
        & $installer -OutputDirectory $toolsRoot | Out-Null
        throw "Bootstrap ignored staged verification failure."
    } catch {
        if ($_.Exception.Message -ne "Injected staged verification failure") { throw }
    }
    if ([IO.File]::ReadAllText($installedRuntime) -ne "damaged runtime fixture") { throw "Failed staging replaced the existing editor." }
    Remove-Item -LiteralPath (Join-Path $bootstrapScripts "fail-verification")
    $repair = @(& $installer -OutputDirectory $toolsRoot)
    if ($repair -notcontains "GodotEditorCache=Rebuilt" -or
        [IO.File]::ReadAllText($installedRuntime) -ne "verified runtime fixture") { throw "Bootstrap failed to repair supporting assembly corruption." }

    $launcherRoot = Join-Path $fixtureRoot "launcher with spaces"
    New-Item -ItemType Directory -Path (Join-Path $launcherRoot ".dotnet") | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $launcherRoot "scripts") | Out-Null
    $launcher = Join-Path $launcherRoot "play.ps1"
    Copy-Item -LiteralPath (Join-Path $repositoryRoot "play.ps1") -Destination $launcher
    # Selection must reach the installer with local DOTNET_ROOT even with PATH empty.
    # This placeholder is never executed: the fixture installer stops first.
    $localSdkName = if ($IsWindows) { "dotnet.exe" } else { "dotnet" }
    [IO.File]::WriteAllText((Join-Path $launcherRoot ".dotnet/$localSdkName"), "selection fixture")
    [IO.File]::WriteAllText((Join-Path $launcherRoot "scripts/install_godot.ps1"),
        'Write-Host "LOCAL_SDK_SELECTED=$env:DOTNET_ROOT"; throw "Fixture stops before build"')
    $shell = (Get-Process -Id $PID).Path
    $originalPath = $env:PATH
    try {
        $env:PATH = $fixtureRoot
        $probe = & $shell -NoProfile -File $launcher -- --seed=42 2>&1 | Out-String
        $expectedSdkRoot = Join-Path $launcherRoot ".dotnet"
        if (-not $probe.Contains("LOCAL_SDK_SELECTED=$expectedSdkRoot", [StringComparison]::Ordinal)) {
            throw "Launcher did not discover the repository-local SDK: $probe"
        }
    } finally { $env:PATH = $originalPath }
    Write-Output "VIBESNAKE_NATIVE_BOOTSTRAP_OK"
} finally {
    $env:GITHUB_OUTPUT = $originalGitHubOutput
    if ([IO.Path]::GetFullPath($fixtureRoot).StartsWith(
        [IO.Path]::GetFullPath([IO.Path]::GetTempPath()), [StringComparison]::OrdinalIgnoreCase)) {
        Remove-Item -LiteralPath $fixtureRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}
