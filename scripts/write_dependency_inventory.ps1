[CmdletBinding()]
param(
    [Parameter()]
    [string]$OutputPath = "TestResults/native/dependency_inventory.json"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$resolvedOutputPath = if ([System.IO.Path]::IsPathFullyQualified($OutputPath)) {
    [System.IO.Path]::GetFullPath($OutputPath)
} else {
    [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot $OutputPath))
}

$nugetLockPaths = @(
    "game/packages.lock.json",
    "native/src/VibeSnake.AgentPlay/packages.lock.json",
    "native/src/VibeSnake.AgentViewer/packages.lock.json",
    "native/src/VibeSnake.Persistence/packages.lock.json",
    "native/src/VibeSnake.Rules/packages.lock.json",
    "native/tests/VibeSnake.Rules.Tests/packages.lock.json",
    "native/tools/RepositoryChecks/packages.lock.json",
    "native/tools/ValidateArtifactManifest/packages.lock.json",
    "native/tools/ValidateCreatorContent/packages.lock.json",
    "native/tools/VibeSnake.AgentHost/packages.lock.json"
)
$pythonLockPaths = @(
    "requirements-runtime.lock",
    "requirements-ci.lock"
)
$allLockPaths = @($nugetLockPaths) + @($pythonLockPaths)

foreach ($relativePath in $allLockPaths) {
    $absolutePath = Join-Path $repositoryRoot $relativePath
    if (-not (Test-Path -LiteralPath $absolutePath -PathType Leaf)) {
        throw "Dependency inventory source lock is missing: $relativePath"
    }
}

$sourceLocks = @(
    foreach ($relativePath in ($allLockPaths | Sort-Object)) {
        $absolutePath = Join-Path $repositoryRoot $relativePath
        [ordered]@{
            path = $relativePath.Replace("\", "/")
            sha256 = (Get-FileHash -LiteralPath $absolutePath -Algorithm SHA256).Hash.ToLowerInvariant()
        }
    }
)

$lockSetText = ($sourceLocks | ForEach-Object { "$($_.path)=$($_.sha256)" }) -join "`n"
$lockSetBytes = [Text.Encoding]::UTF8.GetBytes($lockSetText)
$lockSetHasher = [Security.Cryptography.SHA256]::Create()
try {
    $lockSetSha256 = [Convert]::ToHexString(
        $lockSetHasher.ComputeHash($lockSetBytes)).ToLowerInvariant()
} finally {
    $lockSetHasher.Dispose()
}

$nugetPackages = @{}
foreach ($relativePath in $nugetLockPaths) {
    $absolutePath = Join-Path $repositoryRoot $relativePath
    $lock = Get-Content -LiteralPath $absolutePath -Raw | ConvertFrom-Json
    foreach ($framework in $lock.dependencies.PSObject.Properties) {
        foreach ($packageProperty in $framework.Value.PSObject.Properties) {
            $package = $packageProperty.Value
            if ($package.type -eq "Project") {
                continue
            }

            $name = [string]$packageProperty.Name
            $version = [string]$package.resolved
            if (-not $name -or -not $version) {
                throw "NuGet lock entry is missing name or resolved version: $relativePath"
            }

            $key = "$($name.ToLowerInvariant())|$version"
            if (-not $nugetPackages.ContainsKey($key)) {
                $nugetPackages[$key] = [ordered]@{
                    ecosystem = "nuget"
                    name = $name
                    version = $version
                    dependencyTypes = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
                    sourceLocks = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
                    contentHashes = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
                }
            }

            $entry = $nugetPackages[$key]
            [void]$entry.dependencyTypes.Add(([string]$package.type).ToLowerInvariant())
            [void]$entry.sourceLocks.Add($relativePath.Replace("\", "/"))
            if ($package.PSObject.Properties.Name -contains "contentHash") {
                [void]$entry.contentHashes.Add([string]$package.contentHash)
            }
        }
    }
}

$pythonPackages = @{}
foreach ($relativePath in $pythonLockPaths) {
    $profile = if ($relativePath -eq "requirements-runtime.lock") { "runtime" } else { "ci" }
    foreach ($line in Get-Content -LiteralPath (Join-Path $repositoryRoot $relativePath)) {
        if ($line -notmatch '^([A-Za-z0-9][A-Za-z0-9._-]*)==([^\s\\]+)') {
            continue
        }

        $name = $Matches[1]
        $version = $Matches[2]
        $key = "$($name.ToLowerInvariant())|$version"
        if (-not $pythonPackages.ContainsKey($key)) {
            $pythonPackages[$key] = [ordered]@{
                ecosystem = "python"
                name = $name
                version = $version
                profiles = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
                sourceLocks = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
            }
        }

        $entry = $pythonPackages[$key]
        [void]$entry.profiles.Add($profile)
        [void]$entry.sourceLocks.Add($relativePath)
    }
}

$packages = @(
    foreach ($entry in $nugetPackages.Values) {
        [pscustomobject][ordered]@{
            ecosystem = $entry.ecosystem
            name = $entry.name
            version = $entry.version
            dependencyTypes = @($entry.dependencyTypes | Sort-Object)
            sourceLocks = @($entry.sourceLocks | Sort-Object)
            contentHashes = @($entry.contentHashes | Sort-Object)
        }
    }
    foreach ($entry in $pythonPackages.Values) {
        [pscustomobject][ordered]@{
            ecosystem = $entry.ecosystem
            name = $entry.name
            version = $entry.version
            profiles = @($entry.profiles | Sort-Object)
            sourceLocks = @($entry.sourceLocks | Sort-Object)
        }
    }
) | Sort-Object ecosystem, name, version

if (-not $packages) {
    throw "Dependency inventory did not discover any locked packages."
}

$toolchain = Get-Content -LiteralPath (Join-Path $repositoryRoot "native/toolchain.json") -Raw |
    ConvertFrom-Json
$selectedDotnetSdk = (& dotnet --version).Trim()
if ($LASTEXITCODE -ne 0 -or $selectedDotnetSdk -ne [string]$toolchain.dotnetSdk.version) {
    throw "Selected .NET SDK $selectedDotnetSdk does not match pinned SDK $($toolchain.dotnetSdk.version)."
}
$sourceRevision = (& git -C $repositoryRoot rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $sourceRevision -notmatch '^[0-9a-f]{40}$') {
    throw "Dependency inventory could not resolve a full Git source revision."
}
$sourceDirty = [bool](& git -C $repositoryRoot status --porcelain)
if ($LASTEXITCODE -ne 0) {
    throw "Dependency inventory could not inspect Git worktree state."
}

$inventory = [ordered]@{
    schemaVersion = 1
    kind = "dependency-inventory-v1"
    generatedFromLocksOnly = $true
    sourceRevision = $sourceRevision
    sourceDirty = $sourceDirty
    runtimeIdentifier = [Runtime.InteropServices.RuntimeInformation]::RuntimeIdentifier
    lockSetSha256 = $lockSetSha256
    tools = @(
        [ordered]@{ name = "dotnet-sdk"; version = $selectedDotnetSdk },
        [ordered]@{ name = "godot-dotnet"; version = [string]$toolchain.godot.version; commit = [string]$toolchain.godot.commit },
        [ordered]@{ name = "powershell"; version = [string]$PSVersionTable.PSVersion }
    )
    sources = $sourceLocks
    packages = $packages
}

$parent = Split-Path -Parent $resolvedOutputPath
if (-not $parent) {
    throw "Dependency inventory output must have a parent directory."
}
[System.IO.Directory]::CreateDirectory($parent) | Out-Null
$json = $inventory | ConvertTo-Json -Depth 10
[System.IO.File]::WriteAllText(
    $resolvedOutputPath,
    $json + "`n",
    [Text.UTF8Encoding]::new($false))

Write-Output "DependencyInventory=$resolvedOutputPath"
Write-Output "DependencyInventoryPackages=$($packages.Count)"
Write-Output "DependencyInventoryLockSetSha256=$lockSetSha256"
