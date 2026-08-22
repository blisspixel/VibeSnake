param(
    [string]$OutputRoot,

    [switch]$Force
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$sourceRoot = Join-Path $repositoryRoot "integrations/vibesnake-agent-plugin"
$hostProject = Join-Path $repositoryRoot "native/tools/VibeSnake.AgentHost/VibeSnake.AgentHost.csproj"
$validatorProject = Join-Path $repositoryRoot "native/tools/RepositoryChecks/RepositoryChecks.csproj"
if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $repositoryRoot "dist/agent-plugins"
}

$resolvedOutputRoot = [System.IO.Path]::GetFullPath($OutputRoot)
$target = [System.IO.Path]::GetFullPath(
    (Join-Path $resolvedOutputRoot "portable/vibesnake-agent"))
$containedPrefix = $resolvedOutputRoot.TrimEnd(
    [System.IO.Path]::DirectorySeparatorChar,
    [System.IO.Path]::AltDirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
if (-not $target.StartsWith($containedPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "The package target must remain inside the selected output root."
}

if (Test-Path -LiteralPath $target) {
    if (-not $Force) {
        throw "The package target already exists. Pass -Force to replace that exact generated target."
    }
    Remove-Item -LiteralPath $target -Recurse -Force
}

New-Item -ItemType Directory -Path $target | Out-Null
$binaryDirectory = Join-Path $target "bin"

dotnet restore $hostProject --locked-mode
if ($LASTEXITCODE -ne 0) {
    throw "The locked AgentHost restore failed."
}

dotnet publish $hostProject `
    --configuration Release `
    --self-contained false `
    --output $binaryDirectory `
    --no-restore `
    -p:DebugType=None `
    -p:DebugSymbols=false
if ($LASTEXITCODE -ne 0) {
    throw "The framework-dependent AgentHost publish failed."
}

Copy-Item -LiteralPath (Join-Path $sourceRoot "plugin.json") -Destination $target
Copy-Item -LiteralPath (Join-Path $sourceRoot "skills") -Destination $target -Recurse
Copy-Item -LiteralPath (Join-Path $repositoryRoot "LICENSE") -Destination $target
Copy-Item -LiteralPath (Join-Path $repositoryRoot "NOTICE") -Destination $target

$mcpConfiguration = [ordered]@{
    '$schema' = "https://agent-plugins.org/schemas/1.0.0/mcp.schema.json"
    mcpServers = [ordered]@{
        "vibesnake-agent" = [ordered]@{
            type = "stdio"
            command = "dotnet"
            args = @('${PLUGIN_ROOT}/bin/VibeSnake.AgentHost.dll')
            cwd = '${PLUGIN_ROOT}'
        }
    }
}
$mcpJson = $mcpConfiguration | ConvertTo-Json -Depth 8
[System.IO.File]::WriteAllText(
    (Join-Path $target "mcp.json"),
    $mcpJson + [Environment]::NewLine,
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

dotnet run `
    --project $validatorProject `
    --configuration Release `
    -- plugin $target --require-mcp
if ($LASTEXITCODE -ne 0) {
    throw "The assembled Agent Plugin failed producer validation."
}

Write-Output "Agent Plugin package: $target"
