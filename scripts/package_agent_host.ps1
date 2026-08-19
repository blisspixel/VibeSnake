param(
    [string]$OutputRoot,

    [string]$RuntimeIdentifier,

    [switch]$Force
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$hostProject = Join-Path $repositoryRoot "native/tools/VibeSnake.AgentHost/VibeSnake.AgentHost.csproj"
$pluginManifestPath = Join-Path $repositoryRoot "integrations/vibesnake-agent-plugin/plugin.json"
$validator = Join-Path $repositoryRoot "scripts/validate_agent_host_package.py"

function Get-CurrentRuntimeIdentifier {
    if ($IsWindows) {
        return "win-x64"
    }
    if ($IsMacOS) {
        $architecture = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture
        if ($architecture -eq [System.Runtime.InteropServices.Architecture]::Arm64) {
            return "osx-arm64"
        }
        return "osx-x64"
    }
    return "linux-x64"
}

if ([string]::IsNullOrWhiteSpace($RuntimeIdentifier)) {
    $RuntimeIdentifier = Get-CurrentRuntimeIdentifier
}
if ($RuntimeIdentifier -notmatch '^(win|osx|linux)-(x64|arm64)$') {
    throw "Unsupported runtime identifier: $RuntimeIdentifier"
}

if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $repositoryRoot "dist/agent-host"
}

$resolvedOutputRoot = [System.IO.Path]::GetFullPath($OutputRoot)
$target = [System.IO.Path]::GetFullPath((Join-Path $resolvedOutputRoot $RuntimeIdentifier))
$containedPrefix = $resolvedOutputRoot.TrimEnd(
    [System.IO.Path]::DirectorySeparatorChar,
    [System.IO.Path]::AltDirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
if (-not $target.StartsWith($containedPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw "The package target must remain inside the selected output root."
}

if (Test-Path -LiteralPath $target) {
    if (-not $Force) {
        throw "The package target already exists. Pass -Force to replace that exact generated target."
    }
    Remove-Item -LiteralPath $target -Recurse -Force
}

New-Item -ItemType Directory -Path $target | Out-Null

$pluginManifest = Get-Content -LiteralPath $pluginManifestPath -Raw | ConvertFrom-Json
$hostVersion = [string]$pluginManifest.version
if ([string]::IsNullOrWhiteSpace($hostVersion)) {
    throw "The Agent Plugin manifest did not publish a host version."
}

dotnet restore $hostProject --locked-mode
if ($LASTEXITCODE -ne 0) {
    throw "The locked AgentHost restore failed."
}

dotnet publish $hostProject `
    --configuration Release `
    --self-contained true `
    --runtime $RuntimeIdentifier `
    --output $target `
    --no-restore `
    -p:DebugType=None `
    -p:DebugSymbols=false
if ($LASTEXITCODE -ne 0) {
    throw "The self-contained AgentHost publish failed for $RuntimeIdentifier."
}

Copy-Item -LiteralPath (Join-Path $repositoryRoot "LICENSE") -Destination $target
Copy-Item -LiteralPath (Join-Path $repositoryRoot "NOTICE") -Destination $target

$executableName = if ($RuntimeIdentifier.StartsWith("win-")) {
    "VibeSnake.AgentHost.exe"
} else {
    "VibeSnake.AgentHost"
}
$executablePath = Join-Path $target $executableName
if (-not (Test-Path -LiteralPath $executablePath -PathType Leaf)) {
    throw "Self-contained publish did not write $executableName."
}

$installText = @(
    "Vibe Snake Agent Host self-contained preview"
    "Version $hostVersion"
    "Runtime $RuntimeIdentifier"
    "Unsigned. publicationEligible is false."
    ""
    "Run:"
    "  ./$executableName"
    ""
    "This process speaks MCP 2026-07-28 over stdio. It writes player-adjacent"
    "preview data under the Godot user-data root for Vibe Snake, not beside"
    "this folder. Delete this folder to remove the host. That does not delete"
    "player data."
) -join [Environment]::NewLine
[System.IO.File]::WriteAllText(
    (Join-Path $target "INSTALL.txt"),
    $installText + [Environment]::NewLine,
    [System.Text.UTF8Encoding]::new($false))

$manifest = [ordered]@{
    schema = "vibesnake-agent-host-package-v1"
    host_name = "vibesnake-agent-host"
    host_version = $hostVersion
    runtime_identifier = $RuntimeIdentifier
    self_contained = $true
    framework_dependent = $false
    publication_eligible = $false
    executable = $executableName
    protocol_version = "2026-07-28"
    transport = "stdio"
    user_data_policy = "godot-app-userdata"
    signing = "unsigned"
}
$manifestJson = ($manifest | ConvertTo-Json -Depth 8) + [Environment]::NewLine
[System.IO.File]::WriteAllText(
    (Join-Path $target "host-manifest.json"),
    $manifestJson,
    [System.Text.UTF8Encoding]::new($false))

$checksumLines = Get-ChildItem -LiteralPath $target -File -Recurse |
    Where-Object { $_.Name -ne "SHA256SUMS" } |
    Sort-Object FullName |
    ForEach-Object {
        $relative = [System.IO.Path]::GetRelativePath($target, $_.FullName).Replace("\", "/")
        $hash = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        "$hash  $relative"
    }
[System.IO.File]::WriteAllLines(
    (Join-Path $target "SHA256SUMS"),
    $checksumLines,
    [System.Text.UTF8Encoding]::new($false))

python $validator $target
if ($LASTEXITCODE -ne 0) {
    throw "The assembled Agent Host package failed validation."
}

$startInfo = New-Object System.Diagnostics.ProcessStartInfo
$startInfo.FileName = $executablePath
$startInfo.WorkingDirectory = $target
$startInfo.UseShellExecute = $false
$startInfo.RedirectStandardInput = $true
$startInfo.RedirectStandardOutput = $true
$startInfo.RedirectStandardError = $true
$startInfo.CreateNoWindow = $true
$process = [System.Diagnostics.Process]::Start($startInfo)
Start-Sleep -Seconds 2
if ($process.HasExited) {
    $errorOutput = $process.StandardError.ReadToEnd()
    throw "The self-contained host exited $($process.ExitCode) during smoke: $errorOutput"
}
$process.Kill($true)
$process.WaitForExit(5000) | Out-Null
$process.Dispose()

if (Test-Path -LiteralPath (Join-Path $target "agent_arena")) {
    throw "Host smoke wrote preview data inside the package directory."
}

Write-Output "Agent Host package: $target"
