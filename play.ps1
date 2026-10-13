# Build and launch the native Godot game from a cloned checkout.
# Usage: ./play.ps1 [-- ] [<godot user arguments>]
# Example: ./play.ps1 --agent-watch-pipe=<pipe_name> --agent-watch-token=<access_token>

param()

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# Capture forwarded arguments before any other work. This script is deliberately a
# simple script rather than an advanced one: [CmdletBinding()] would leave $args
# undefined under StrictMode and would try to bind tokens such as
# --agent-watch-pipe=<name> as PowerShell parameters instead of forwarding them.
$forwardedArguments = @()
if ($null -ne $args) {
    $forwardedArguments = @($args)
}
# Accept the POSIX separator so `./play.sh -- --agent-watch-pipe=<name>` and
# `./play.sh --agent-watch-pipe=<name>` forward the same Godot user arguments.
if ($forwardedArguments.Count -gt 0 -and $forwardedArguments[0] -eq "--") {
    $forwardedArguments = @($forwardedArguments | Select-Object -Skip 1)
}

$repositoryRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location -LiteralPath $repositoryRoot

if ($PSVersionTable.PSVersion.Major -lt 7) {
    throw "Vibe Snake requires PowerShell 7 or newer. Run this script with pwsh."
}
$localDotnet = @(
    (Join-Path $repositoryRoot ".dotnet/dotnet.exe"),
    (Join-Path $repositoryRoot ".dotnet/dotnet")
) | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
if ($localDotnet) {
    $dotnetExecutable = $localDotnet
    $env:DOTNET_ROOT = Split-Path -Parent $localDotnet
    $env:PATH = "$env:DOTNET_ROOT$([System.IO.Path]::PathSeparator)$env:PATH"
} elseif (Get-Command dotnet -ErrorAction SilentlyContinue) {
    $dotnetExecutable = (Get-Command dotnet).Source
} else {
    throw @"
The .NET SDK 10.0.303 is required. Install it, then run ./play.ps1 again.
  mise:      mise use -g dotnet@10.0.303
  Homebrew:  brew install dotnet@10
  winget:    winget install --id Microsoft.DotNet.SDK.10 -e
  script:    curl -fsSL https://dot.net/v1/dotnet-install.sh | bash /dev/stdin --version 10.0.303
"@
}

$installedSdks = & $dotnetExecutable --list-sdks
if (($installedSdks -join "`n") -notmatch '(?m)^10\.0\.303\b') {
    throw @"
The .NET SDK 10.0.303 is required (global.json sets rollForward to disable). Found:
$($installedSdks -join "`n")
  mise:      mise use -g dotnet@10.0.303
  Homebrew:  brew install dotnet@10
  winget:    winget install --id Microsoft.DotNet.SDK.10 -e
  script:    curl -fsSL https://dot.net/v1/dotnet-install.sh | bash /dev/stdin --version 10.0.303
"@
}

$godotCache = Join-Path $repositoryRoot ".tools/godot"
$cachedGodot = $null
if (Test-Path -LiteralPath $godotCache -PathType Container) {
    $cachedGodot = Get-ChildItem -LiteralPath $godotCache -Recurse -File -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -like "Godot*" } |
        Select-Object -First 1
}
if (-not $cachedGodot) {
    Write-Host "First run: downloading the pinned Godot editor, building the native game, and importing assets."
    Write-Host "This usually takes a few minutes and only happens once."
}

$installerOutput = & (Join-Path $repositoryRoot "scripts/install_godot.ps1")
$installerOutput | Write-Output
$executableLine = $installerOutput |
    Where-Object { $_ -like "GodotExecutable=*" } |
    Select-Object -Last 1
if (-not $executableLine) {
    throw "The verified Godot installer did not report an executable path."
}

$godotExecutable = $executableLine.Substring("GodotExecutable=".Length)
# Release is the player build. Debug stays available for development via an explicit -c Debug.
& $dotnetExecutable build (Join-Path $repositoryRoot "game/VibeSnake.Game.sln") --nologo -c Release
if ($LASTEXITCODE -ne 0) {
    throw "The native game build failed."
}

# A fresh clone or extracted source archive has no generated import cache, and Godot
# cannot write one while running the game. Import first so the first launch renders.
& (Join-Path $repositoryRoot "scripts/assert_godot_import.ps1") -GodotExecutable $godotExecutable

$gamePath = Join-Path $repositoryRoot "game"
if ($forwardedArguments.Count -eq 0) {
    & $godotExecutable --path $gamePath
} else {
    & $godotExecutable --path $gamePath -- @forwardedArguments
}
exit $LASTEXITCODE
