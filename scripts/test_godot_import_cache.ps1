# Import-cache regressions use a fake editor and isolated project, with no builds.
[CmdletBinding()]
param([string]$GodotExecutable)
Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$temporaryBase = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$fixtureRoot = Join-Path $temporaryBase ("vibesnake-import-cache-{0}" -f [Guid]::NewGuid())
$previousFailure = $env:VIBESNAKE_IMPORT_FIXTURE_FAILURE
$previousDotnetRoot = $env:DOTNET_ROOT
$previousPath = $env:PATH
try {
    $project = Join-Path $fixtureRoot "project with spaces"
    New-Item -ItemType Directory -Path $project | Out-Null
    [IO.File]::WriteAllText((Join-Path $project "project.godot"), "config_version=5")
    $source = Join-Path $project "image.png"
    [IO.File]::WriteAllText($source, "bitmap fixture original")
    $editor = Join-Path $fixtureRoot "editor.ps1"
    [IO.File]::WriteAllText($editor, @'
param()
$project = $args[[Array]::IndexOf($args, "--path") + 1]
if ($args -notcontains "--import" -or $args -contains "--quit" -or
    $args -notcontains "--quit-after" -or $args[[Array]::IndexOf($args, "--quit-after") + 1] -ne "180" -or
    $args -notcontains "--max-fps" -or $args[[Array]::IndexOf($args, "--max-fps") + 1] -ne "60") {
    throw "Import must use resource completion with bounded editor-frame grace."
}
[IO.File]::AppendAllText((Join-Path $project "editor-invocations.txt"), "import`n")
if ($env:VIBESNAKE_IMPORT_FIXTURE_FAILURE -eq "exit") { exit 9 }
if ($env:VIBESNAKE_IMPORT_FIXTURE_FAILURE -eq "no-repair") { exit 0 }
$cache = Join-Path $project ".godot/imported"
New-Item -ItemType Directory -Path $cache -Force | Out-Null
foreach ($image in Get-ChildItem -LiteralPath $project -File -Filter "*.png") {
    $resource = "res://" + $image.Name
    $key = [Convert]::ToHexString([Security.Cryptography.MD5]::HashData([Text.Encoding]::UTF8.GetBytes($resource))).ToLowerInvariant()
    $stem = $image.Name + "-" + $key
    $payload = Join-Path $cache ($stem + ".ctex")
    $sourceHash = (Get-FileHash -LiteralPath $image.FullName -Algorithm MD5).Hash.ToLowerInvariant()
    [IO.File]::WriteAllText($payload, "imported:" + $sourceHash)
    $payloadHash = (Get-FileHash -LiteralPath $payload -Algorithm MD5).Hash.ToLowerInvariant()
    [IO.File]::WriteAllText(($image.FullName + ".import"), ('[deps]' + "`n" + 'source_file="' + $resource + '"' + "`n" + 'dest_files=["res://.godot/imported/' + $stem + '.ctex"]' + "`n"))
    [IO.File]::WriteAllText((Join-Path $cache ($stem + ".md5")), ('source_md5="' + $sourceHash + '"' + "`n" + 'dest_md5="' + $payloadHash + '"' + "`n"))
}
if ($env:VIBESNAKE_IMPORT_FIXTURE_FAILURE -eq "reported-error") { Write-Output "ERROR: fixture reported an import error" }
exit 0
'@)
    $shell = (Get-Process -Id $PID).Path
    $guard = Join-Path $PSScriptRoot "assert_godot_import.ps1"
    function Invoke-ImportFixture {
        param([string]$Failure = "", [switch]$Force)
        $env:VIBESNAKE_IMPORT_FIXTURE_FAILURE = $Failure
        $arguments = @("-NoProfile", "-File", $guard, "-GodotExecutable", $editor, "-ProjectPath", $project)
        if ($Force) { $arguments += "-Force" }
        $output = & $shell @arguments 2>&1 | Out-String
        return @{ code = $LASTEXITCODE; output = $output }
    }
    function Assert-ImportRebuilt {
        param($Result)
        if ($Result.code -ne 0 -or $Result.output -notmatch "GodotImportCache=Rebuilt") { throw "Import fixture failed: $($Result.output)" }
    }
    Assert-ImportRebuilt (Invoke-ImportFixture)
    $marker = Join-Path $project "editor-invocations.txt"
    $before = [IO.File]::ReadAllText($marker)
    $ready = Invoke-ImportFixture
    if ($ready.code -ne 0 -or $ready.output -notmatch "GodotImportCache=Ready" -or [IO.File]::ReadAllText($marker) -ne $before) { throw "Ready imports unnecessarily ran the editor." }
    [IO.File]::WriteAllText($source, "bitmap fixture changed")
    Assert-ImportRebuilt (Invoke-ImportFixture)
    $metadata = Get-ChildItem -LiteralPath (Join-Path $project ".godot/imported") -Filter "*.md5" | Select-Object -First 1
    [IO.File]::WriteAllText($metadata.FullName, 'source_md5="malformed"')
    Assert-ImportRebuilt (Invoke-ImportFixture)
    Remove-Item -LiteralPath $metadata.FullName
    Assert-ImportRebuilt (Invoke-ImportFixture)
    $payload = Get-ChildItem -LiteralPath (Join-Path $project ".godot/imported") -Filter "*.ctex" | Select-Object -First 1
    [IO.File]::WriteAllText($payload.FullName, "damaged cached bitmap")
    Assert-ImportRebuilt (Invoke-ImportFixture)
    Remove-Item -LiteralPath $payload.FullName
    Assert-ImportRebuilt (Invoke-ImportFixture)
    $descriptor = $source + ".import"
    [IO.File]::WriteAllText($descriptor, '[deps]' + "`n" + 'source_file="res://../../outside.png"' + "`n" + 'dest_files=["res://../../outside.ctex"]')
    Assert-ImportRebuilt (Invoke-ImportFixture)
    Assert-ImportRebuilt (Invoke-ImportFixture -Force)
    [IO.File]::AppendAllText($descriptor, "[params]`nprocess/size_limit=64`n")
    Assert-ImportRebuilt (Invoke-ImportFixture)
    [IO.File]::WriteAllText($source, "bitmap fixture changed again")
    $noRepair = Invoke-ImportFixture -Failure "no-repair"
    if ($noRepair.code -eq 0 -or $noRepair.output -notmatch "did not repair stale|did not produce") { throw "Import accepted an editor that left stale resources." }
    $failure = Invoke-ImportFixture -Failure "exit"
    if ($failure.code -eq 0 -or $failure.output -notmatch "exit code 9") { throw "Import accepted an editor failure." }
    $reportedError = Invoke-ImportFixture -Failure "reported-error"
    if ($reportedError.code -eq 0 -or $reportedError.output -notmatch "reported an error, warning, or leaked object") { throw "Import accepted zero-exit reported engine errors." }
    Write-Output "VIBESNAKE_GODOT_IMPORT_CACHE_OK cases=13"
    if ($GodotExecutable) {
        $localDotnetRoot = Join-Path (Split-Path -Parent $PSScriptRoot) ".dotnet"
        if (Test-Path -LiteralPath (Join-Path $localDotnetRoot $(if ($IsWindows) { "dotnet.exe" } else { "dotnet" })) -PathType Leaf) {
            $env:DOTNET_ROOT = $localDotnetRoot
            $env:PATH = "$localDotnetRoot$([IO.Path]::PathSeparator)$env:PATH"
        }
        $project = Join-Path $fixtureRoot "real editor project"
        New-Item -ItemType Directory -Path $project | Out-Null
        [IO.File]::WriteAllText((Join-Path $project "project.godot"), "config_version=5`n[application]`nconfig/name=`"Import Fixture`"`n[rendering]`nrenderer/rendering_method=`"gl_compatibility`"`n")
        Copy-Item -LiteralPath (Join-Path (Split-Path -Parent $PSScriptRoot) "game/assets/branding/vibe-snake.png") -Destination (Join-Path $project "image.png")
        $editor = [IO.Path]::GetFullPath($GodotExecutable)
        Assert-ImportRebuilt (Invoke-ImportFixture)
        $payload = Get-ChildItem -LiteralPath (Join-Path $project ".godot/imported") -Filter "*.ctex" | Select-Object -First 1
        $originalPayloadHash = (Get-FileHash -LiteralPath $payload.FullName -Algorithm SHA256).Hash
        [IO.File]::WriteAllText($payload.FullName, "corrupt real cache payload")
        Assert-ImportRebuilt (Invoke-ImportFixture)
        if ((Get-FileHash -LiteralPath $payload.FullName -Algorithm SHA256).Hash -ne $originalPayloadHash) { throw "Real editor did not reproduce the original imported bitmap." }
        $metadata = Get-ChildItem -LiteralPath (Join-Path $project ".godot/imported") -Filter "*.md5" | Select-Object -First 1
        [IO.File]::WriteAllText($metadata.FullName, 'source_md5="malformed"')
        Assert-ImportRebuilt (Invoke-ImportFixture)
        $descriptor = Join-Path $project "image.png.import"
        $originalDescriptor = [IO.File]::ReadAllText($descriptor)
        [IO.File]::WriteAllText($descriptor, $originalDescriptor.Replace("process/size_limit=0", "process/size_limit=64"))
        Assert-ImportRebuilt (Invoke-ImportFixture)
        if ((Get-FileHash -LiteralPath $payload.FullName -Algorithm SHA256).Hash -eq $originalPayloadHash) { throw "Real editor did not apply changed import settings." }
        Write-Output "VIBESNAKE_GODOT_IMPORT_REAL_EDITOR_OK cases=4"
    }
} finally {
    $env:VIBESNAKE_IMPORT_FIXTURE_FAILURE = $previousFailure
    $env:DOTNET_ROOT = $previousDotnetRoot
    $env:PATH = $previousPath
    $resolvedFixture = [IO.Path]::GetFullPath($fixtureRoot)
    $safePrefix = $temporaryBase.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if ($resolvedFixture.StartsWith($safePrefix, [StringComparison]::OrdinalIgnoreCase)) {
        Remove-Item -LiteralPath $resolvedFixture -Recurse -Force -ErrorAction SilentlyContinue
    }
}
