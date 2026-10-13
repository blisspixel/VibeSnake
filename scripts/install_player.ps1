# Prepare a source checkout and launch the native Godot game.
# The frozen Python reference is retired; this script no longer installs it.
# Usage:
#   irm https://raw.githubusercontent.com/blisspixel/VibeSnake/main/scripts/install_player.ps1 | iex
#   or: ./scripts/install_player.ps1 [install_dir]

$ErrorActionPreference = "Stop"
$InstallDir = if ($args.Count -ge 1 -and $args[0]) { $args[0] } else { Join-Path $PWD "VibeSnake" }
$Branch = if ($env:VIBESNAKE_BRANCH) { $env:VIBESNAKE_BRANCH } else { "main" }
$RepoUrl = if ($env:VIBESNAKE_REPO) { $env:VIBESNAKE_REPO } else { "https://github.com/blisspixel/VibeSnake.git" }

Write-Host "The Python/Pygame reference is retired. Preparing the native Godot game in $InstallDir (branch $Branch)."

if (-not (Test-Path -LiteralPath (Join-Path $InstallDir ".git"))) {
    git clone --branch $Branch $RepoUrl $InstallDir
    if ($LASTEXITCODE -ne 0) { throw "git clone failed" }
} else {
    Write-Host "Directory exists; pulling latest $Branch"
    git -C $InstallDir fetch origin $Branch
    if ($LASTEXITCODE -ne 0) { throw "git fetch failed" }
    git -C $InstallDir checkout $Branch
    if ($LASTEXITCODE -ne 0) { throw "git checkout failed" }
    git -C $InstallDir pull --ff-only origin $Branch
    if ($LASTEXITCODE -ne 0) { throw "git pull failed" }
}

Write-Host "Launching ./play.ps1. It checks for the .NET SDK 10.0.303, then builds Release."
& (Join-Path $InstallDir "play.ps1")
exit $LASTEXITCODE
