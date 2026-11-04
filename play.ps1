# Build and launch the native Godot game from a cloned checkout.
# Usage: ./play.ps1 [--help | -h | --version]
#        ./play.ps1 [--agent-watch-pipe=<name>] [--agent-watch-token=<token>]
#        ./play.ps1 [--] [<game arguments>]
# Example: ./play.ps1 --agent-watch-pipe=<pipe_name> --agent-watch-token=<access_token>

param()

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Test-KnownLauncherArgument([string]$Argument) {
    if ($Argument -eq "--agent-watch-exhibitions" -or
        $Argument -eq "--agent-watch-qualification" -or
        $Argument -eq "--agent-watch-passports" -or
        $Argument -eq "--agent-watch-accessibility") {
        return $true
    }
    if ($Argument.StartsWith("--agent-watch-pipe=", [StringComparison]::Ordinal) -and
        $Argument.Length -gt "--agent-watch-pipe=".Length) {
        return $true
    }
    if ($Argument.StartsWith("--agent-watch-token=", [StringComparison]::Ordinal) -and
        $Argument.Length -gt "--agent-watch-token=".Length) {
        return $true
    }
    return $false
}

function Write-LauncherUsage([string]$RepositoryRoot) {
    $version = "development"
    $versionPath = Join-Path $RepositoryRoot "VERSION"
    if (Test-Path -LiteralPath $versionPath -PathType Leaf) {
        $trimmed = (Get-Content -LiteralPath $versionPath -Raw).Trim()
        if ($trimmed) {
            $version = $trimmed
        }
    }
    Write-Output @"
Vibe Snake $version

Usage:
  ./play.sh
  ./play.ps1
  ./play.sh --help
  ./play.ps1 --help
  ./play.sh -h
  ./play.sh --version

With no arguments, the launcher checks the pinned .NET SDK, installs the pinned Godot editor on first use, builds the native game, imports assets, and opens the main menu. The first launch can take a few minutes. Later launches reuse verified downloads and imports.

Pass game arguments after --:
  ./play.sh -- <game arguments>

Post-1.0 agent watch preview (not part of the supported 1.0 release):
  --agent-watch-pipe=<name>
  --agent-watch-token=<token>
  --agent-watch-exhibitions
  --agent-watch-qualification
  --agent-watch-passports
  --agent-watch-accessibility

See docs/engineering/AGENT_PLAY.md. From this checkout, the local agent host is:
  dotnet run --project native/tools/VibeSnake.AgentHost/VibeSnake.AgentHost.csproj
"@
}

# Capture forwarded arguments before any other work. This script is deliberately a
# simple script rather than an advanced one: [CmdletBinding()] would leave $args
# undefined under StrictMode and would try to bind tokens such as
# --agent-watch-pipe=<name> as PowerShell parameters instead of forwarding them.
$forwardedArguments = @()
if ($null -ne $args) {
    $forwardedArguments = @($args)
}
# A leading -- means the remaining tokens are game arguments. The same watch
# flags may also be passed directly. Any other direct flag is reported here.
$gameArgumentsFollowSeparator = $false
if ($forwardedArguments.Count -gt 0 -and $forwardedArguments[0] -eq "--") {
    $gameArgumentsFollowSeparator = $true
    $forwardedArguments = @($forwardedArguments | Select-Object -Skip 1)
}

$repositoryRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
foreach ($argument in $forwardedArguments) {
    if ($argument -eq "--help" -or $argument -eq "-h" -or $argument -eq "--version") {
        Write-LauncherUsage -RepositoryRoot $repositoryRoot
        exit 0
    }
}
if (-not $gameArgumentsFollowSeparator) {
    $unknownArguments = @($forwardedArguments | Where-Object { -not (Test-KnownLauncherArgument $_) })
    if ($unknownArguments.Count -gt 0) {
        throw @"
Unrecognized launcher argument: $($unknownArguments -join ', ').
Pass game arguments after --. Run ./play.sh --help for usage.
"@
    }
}

Set-Location -LiteralPath $repositoryRoot

if ($PSVersionTable.PSVersion.Major -lt 7) {
    throw "Vibe Snake requires PowerShell 7 or newer. Run this script with pwsh."
}
# The first dotnet run on a machine prints a welcome banner and may install an
# ASP.NET development certificate. This process is launching a game.
$env:DOTNET_NOLOGO = "1"
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = "false"
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
    throw "The .NET 10.0.403 SDK is required. Install with mise use -g dotnet@10.0.403, or see https://dotnet.microsoft.com/download/dotnet/10.0, then rerun ./play.ps1."
}

& (Join-Path $repositoryRoot "scripts/assert_dotnet_sdk.ps1") -DotnetExecutable $dotnetExecutable
Write-Host "First launch can take a few minutes: verifying Godot, building the native game, then importing assets. Later launches reuse verified downloads and imports."
$installerOutput = & (Join-Path $repositoryRoot "scripts/install_godot.ps1")
$installerOutput | Write-Output
$executableLine = $installerOutput |
    Where-Object { $_ -like "GodotExecutable=*" } |
    Select-Object -Last 1
if (-not $executableLine) {
    throw "The verified Godot installer did not report an executable path."
}

$godotExecutable = $executableLine.Substring("GodotExecutable=".Length)
& $dotnetExecutable build (Join-Path $repositoryRoot "game/VibeSnake.Game.sln") --nologo
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
