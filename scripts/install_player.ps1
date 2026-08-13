# Bootstrap the frozen Python reference from GitHub main.
# Usage:
#   irm https://raw.githubusercontent.com/blisspixel/VibeSnake/main/scripts/install_player.ps1 | iex
#   or: ./scripts/install_player.ps1 [-InstallDir path]

[CmdletBinding()]
param(
    [string]$InstallDir = (Join-Path $PWD "VibeSnake"),
    [string]$Branch = "main",
    [string]$RepoUrl = "https://github.com/blisspixel/VibeSnake.git",
    [string]$Python = "py -3.14"
)

$ErrorActionPreference = "Stop"

function Invoke-Python {
    param([Parameter(Mandatory)][string]$Expression)
    if ($Python -like "py *") {
        $parts = $Python.Split(" ", 2)
        & $parts[0] $parts[1].Split(" ") @("-c", $Expression)
    } else {
        & $Python -c $Expression
    }
    if ($LASTEXITCODE -ne 0) {
        throw "Python command failed: $Expression"
    }
}

Write-Host "Installing the frozen Vibe Snake Python reference into $InstallDir (branch $Branch)"

if (-not (Test-Path -LiteralPath $InstallDir)) {
    git clone --branch $Branch $RepoUrl $InstallDir
} else {
    Write-Host "Directory exists; pulling latest $Branch"
    git -C $InstallDir fetch origin $Branch
    git -C $InstallDir checkout $Branch
    git -C $InstallDir pull --ff-only origin $Branch
}

Set-Location -LiteralPath $InstallDir
if (-not (Test-Path ".venv")) {
    if ($Python -like "py *") {
        $parts = $Python.Split(" ", 2)
        & $parts[0] $parts[1].Split(" ") -m venv .venv
    } else {
        & $Python -m venv .venv
    }
}

$venvPython = Join-Path $InstallDir ".venv\Scripts\python.exe"
& $venvPython -m pip install --upgrade pip
& $venvPython -m pip install --require-hashes --only-binary=:all: -r requirements-runtime.lock
& $venvPython -m pip install --no-deps --no-build-isolation -e .

Write-Host ""
Write-Host "Frozen reference installed. Run it with:"
Write-Host "  cd `"$InstallDir`""
Write-Host "  .\.venv\Scripts\Activate.ps1"
Write-Host "  vibesnake"
Write-Host "Update later with:"
Write-Host "  vibesnake update"
