# Build and launch the native Godot game from a cloned checkout.
# Usage: ./play.ps1

[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repositoryRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location -LiteralPath $repositoryRoot

if ($PSVersionTable.PSVersion.Major -lt 7) {
    throw "Vibe Snake requires PowerShell 7 or newer. Run this script with pwsh."
}
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw "The .NET 10.0.302 SDK is required. Install it, then run ./play.ps1 again."
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

& $godotExecutable --path (Join-Path $repositoryRoot "game") -- @args
exit $LASTEXITCODE
