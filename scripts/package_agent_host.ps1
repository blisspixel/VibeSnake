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

function Get-Sha256Lower {
    param([string]$Path)
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Write-Utf8Json {
    param(
        [string]$Path,
        [object]$Value
    )
    $json = ($Value | ConvertTo-Json -Depth 10) + [Environment]::NewLine
    [System.IO.File]::WriteAllText(
        $Path,
        $json,
        [System.Text.UTF8Encoding]::new($false))
}

function Get-HostPackageSourceState {
    param([string]$RepositoryRoot)

    $toolchain = Get-Content -LiteralPath (Join-Path $RepositoryRoot "native/toolchain.json") -Raw |
        ConvertFrom-Json
    $dotnetSdk = (& dotnet --version).Trim()
    if ($LASTEXITCODE -ne 0 -or $dotnetSdk -ne [string]$toolchain.dotnetSdk.version) {
        throw "Selected .NET SDK $dotnetSdk does not match pinned SDK $($toolchain.dotnetSdk.version)."
    }
    $sourceRevision = (& git -C $RepositoryRoot rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0 -or $sourceRevision -notmatch '^[0-9a-f]{40}$') {
        throw "Host package provenance could not resolve a full Git source revision."
    }
    $sourceDirty = [bool](& git -C $RepositoryRoot status --porcelain)
    if ($LASTEXITCODE -ne 0) {
        throw "Host package provenance could not inspect Git worktree state."
    }
    return [ordered]@{
        source_revision = $sourceRevision
        source_dirty = $sourceDirty
        dotnet_sdk = $dotnetSdk
    }
}

function New-HostPackageInventory {
    param(
        [string]$RepositoryRoot,
        [string]$RuntimeIdentifier,
        [string]$HostVersion,
        $SourceState
    )

    $lockPaths = @(
        "native/src/VibeSnake.AgentPlay/packages.lock.json",
        "native/src/VibeSnake.Persistence/packages.lock.json",
        "native/src/VibeSnake.Rules/packages.lock.json",
        "native/tools/VibeSnake.AgentHost/packages.lock.json"
    )
    $sourceLocks = @(
        foreach ($relativePath in $lockPaths) {
            $absolutePath = Join-Path $RepositoryRoot $relativePath
            if (-not (Test-Path -LiteralPath $absolutePath -PathType Leaf)) {
                throw "Host inventory source lock is missing: $relativePath"
            }
            [ordered]@{
                path = $relativePath
                sha256 = Get-Sha256Lower $absolutePath
            }
        }
    )
    $lockSetText = ($sourceLocks | ForEach-Object { "$($_.path)=$($_.sha256)" }) -join "`n"
    $lockSetBytes = [Text.Encoding]::UTF8.GetBytes($lockSetText)
    $lockSetHasher = [Security.Cryptography.SHA256]::Create()
    try {
        $lockSetSha256 = [Convert]::ToHexString(
            $lockSetHasher.ComputeHash($lockSetBytes)).ToLowerInvariant()
    }
    finally {
        $lockSetHasher.Dispose()
    }

    $nugetPackages = @{}
    $ridFramework = "net10.0/$RuntimeIdentifier"
    foreach ($sourceLock in $sourceLocks) {
        $relativePath = [string]$sourceLock.path
        $lock = Get-Content -LiteralPath (Join-Path $RepositoryRoot $relativePath) -Raw |
            ConvertFrom-Json
        $frameworkNames = @($lock.dependencies.PSObject.Properties.Name)
        if ($relativePath -eq "native/tools/VibeSnake.AgentHost/packages.lock.json" -and
            $ridFramework -notin $frameworkNames) {
            throw "Agent Host lock is missing the $RuntimeIdentifier graph."
        }
        foreach ($framework in $lock.dependencies.PSObject.Properties) {
            if ($framework.Name -ne "net10.0" -and $framework.Name -ne $ridFramework) {
                continue
            }
            foreach ($packageProperty in $framework.Value.PSObject.Properties) {
                $package = $packageProperty.Value
                if ($package.type -eq "Project") {
                    continue
                }
                $name = [string]$packageProperty.Name
                $version = [string]$package.resolved
                if (-not $name -or -not $version) {
                    throw "Host inventory lock entry is missing name or resolved version: $relativePath"
                }
                $key = "$($name.ToLowerInvariant())|$version"
                if (-not $nugetPackages.ContainsKey($key)) {
                    $nugetPackages[$key] = [ordered]@{
                        ecosystem = "nuget"
                        name = $name
                        version = $version
                        dependencyTypes = [Collections.Generic.HashSet[string]]::new(
                            [StringComparer]::OrdinalIgnoreCase)
                        sourceLocks = [Collections.Generic.HashSet[string]]::new(
                            [StringComparer]::Ordinal)
                        contentHashes = [Collections.Generic.HashSet[string]]::new(
                            [StringComparer]::Ordinal)
                        frameworks = [Collections.Generic.HashSet[string]]::new(
                            [StringComparer]::Ordinal)
                    }
                }
                $entry = $nugetPackages[$key]
                [void]$entry.dependencyTypes.Add(([string]$package.type).ToLowerInvariant())
                [void]$entry.sourceLocks.Add($relativePath)
                if ($package.PSObject.Properties.Name -contains "contentHash" -and
                    -not [string]::IsNullOrWhiteSpace([string]$package.contentHash)) {
                    [void]$entry.contentHashes.Add([string]$package.contentHash)
                }
                [void]$entry.frameworks.Add([string]$framework.Name)
            }
        }
    }

    $packages = @(
        foreach ($entry in $nugetPackages.Values) {
            [pscustomobject][ordered]@{
                ecosystem = $entry.ecosystem
                name = $entry.name
                version = $entry.version
                dependency_types = @($entry.dependencyTypes | Sort-Object)
                source_locks = @($entry.sourceLocks | Sort-Object)
                content_hashes = @($entry.contentHashes | Sort-Object)
                frameworks = @($entry.frameworks | Sort-Object)
            }
        }
    ) | Sort-Object name, version
    $packageNames = @($packages | ForEach-Object { $_.name })
    foreach ($required in @("ModelContextProtocol", "Microsoft.Extensions.Hosting")) {
        if ($required -notin $packageNames) {
            throw "Host inventory is missing required package: $required"
        }
    }

    return [ordered]@{
        schema = "vibesnake-agent-host-inventory-v1"
        generated_from_locks_only = $true
        host_version = $HostVersion
        runtime_identifier = $RuntimeIdentifier
        source_revision = $SourceState.source_revision
        source_dirty = [bool]$SourceState.source_dirty
        lock_set_sha256 = $lockSetSha256
        dotnet_sdk = $SourceState.dotnet_sdk
        sources = $sourceLocks
        packages = @($packages)
    }
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
    "this folder. Set VIBESNAKE_AGENT_USER_DATA_ROOT to an existing fully"
    "qualified directory to isolate that data. That directory cannot be this"
    "package. Delete this folder to remove the host. That does not delete"
    "player data."
    ""
    "host-inventory.json is the lock-derived NuGet inventory for this host."
    "host-provenance.json binds the executable, manifest, and inventory to the"
    "source revision. Neither file is a platform signature."
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
$manifestPath = Join-Path $target "host-manifest.json"
Write-Utf8Json -Path $manifestPath -Value $manifest

$sourceState = Get-HostPackageSourceState -RepositoryRoot $repositoryRoot
$inventory = New-HostPackageInventory `
    -RepositoryRoot $repositoryRoot `
    -RuntimeIdentifier $RuntimeIdentifier `
    -HostVersion $hostVersion `
    -SourceState $sourceState
$inventoryPath = Join-Path $target "host-inventory.json"
Write-Utf8Json -Path $inventoryPath -Value $inventory

$provenance = [ordered]@{
    schema = "vibesnake-agent-host-provenance-v1"
    host_name = "vibesnake-agent-host"
    host_version = $hostVersion
    runtime_identifier = $RuntimeIdentifier
    source_revision = $sourceState.source_revision
    source_dirty = [bool]$sourceState.source_dirty
    self_contained = $true
    signing = "unsigned"
    publication_eligible = $false
    executable_sha256 = Get-Sha256Lower $executablePath
    manifest_sha256 = Get-Sha256Lower $manifestPath
    inventory_sha256 = Get-Sha256Lower $inventoryPath
    lock_set_sha256 = [string]$inventory.lock_set_sha256
    dotnet_sdk = $sourceState.dotnet_sdk
}
Write-Utf8Json -Path (Join-Path $target "host-provenance.json") -Value $provenance

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
