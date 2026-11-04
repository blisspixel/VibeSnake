# Refuse unsupported SDKs before downloading or importing the editor.
param([Parameter(Mandatory)][string]$DotnetExecutable)
Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$pin = (Get-Content -LiteralPath (Join-Path $repositoryRoot "global.json") -Raw | ConvertFrom-Json).sdk.version
# A failing host often prints a multi-line CLI dump that repeats this pin.
# Quote a version only when the process succeeded and printed exactly one.
$versionOutput = & $DotnetExecutable --version 2>&1 | Out-String
$versionLines = @(
    ($versionOutput -split '\r?\n') |
        ForEach-Object { $_.Trim() } |
        Where-Object { $_ -match '^\d+\.\d+\.\d+$' })
$usable = $LASTEXITCODE -eq 0 -and $versionLines.Count -eq 1
$reported = if ($usable) { $versionLines[0] } else { "" }
if (-not $usable -or $reported -ne $pin) {
    $detail = if ($usable) {
        "The selected host reported $reported."
    } else {
        "The selected host did not print a usable SDK version."
    }
    throw @"
Vibe Snake requires the exact .NET $pin SDK. The selected host could not resolve it.
Linux or macOS with mise: mise use -g dotnet@$pin
Windows or macOS: https://dotnet.microsoft.com/download/dotnet/10.0
Alternatively, run the official dotnet-install script with -Version $pin -InstallDir .dotnet
(Unix: --version $pin --install-dir .dotnet), then rerun this launcher.
$detail
"@
}
