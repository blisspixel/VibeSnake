# Refuse unsupported SDKs before downloading or importing the editor.
param([Parameter(Mandatory)][string]$DotnetExecutable)
Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$pin = (Get-Content -LiteralPath (Join-Path $repositoryRoot "global.json") -Raw | ConvertFrom-Json).sdk.version
$version = & $DotnetExecutable --version 2>&1 | Out-String
if ($LASTEXITCODE -ne 0 -or $version.Trim() -ne $pin) {
    throw @"
Vibe Snake requires the exact .NET $pin SDK. The selected host could not resolve it.
Linux or macOS with mise: mise use -g dotnet@$pin
Windows or macOS: https://dotnet.microsoft.com/download/dotnet/10.0
Alternatively, run the official dotnet-install script with -Version $pin -InstallDir .dotnet
(Unix: --version $pin --install-dir .dotnet), then rerun this launcher.
$version
"@
}
