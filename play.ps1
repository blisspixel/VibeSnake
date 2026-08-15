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
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw "The .NET 10.0.303 SDK is required. Install it, then run ./play.ps1 again."
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
& dotnet build (Join-Path $repositoryRoot "game/VibeSnake.Game.sln") --nologo
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
