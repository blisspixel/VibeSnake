# Qualify the Agent Arena developer preview. Optional --full runs native quality;
# optional --commit commits only changes the caller has already staged. Never pushes.
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
if ($PSVersionTable.PSVersion.Major -lt 7) {
    [Console]::Error.WriteLine("PowerShell 7 or newer is required.")
    exit 1
}
$full = $false
$commit = $false
foreach ($argument in $args) {
    switch -CaseSensitive ($argument) {
        "--full" { $full = $true }
        "--commit" { $commit = $true }
        default {
            [Console]::Error.WriteLine("Unknown argument: $argument. Usage: close-agent-preview.cmd [--full] [--commit]")
            exit 2
        }
    }
}

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$previousLocation = Get-Location
$previousEnvironment = @{}
foreach ($name in @("DOTNET_ROOT", "DOTNET_ROOT_X64", "PATH", "GIT_AUTHOR_NAME", "GIT_AUTHOR_EMAIL", "GIT_COMMITTER_NAME", "GIT_COMMITTER_EMAIL")) {
    $previousEnvironment[$name] = [Environment]::GetEnvironmentVariable($name, "Process")
}
function Invoke-PreviewCommand {
    param([string]$Executable, [string[]]$CommandArguments)
    Write-Output ("+ " + $Executable + " " + ($CommandArguments -join " "))
    & $Executable @CommandArguments
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

try {
    Set-Location -LiteralPath $repositoryRoot
    $localDotnet = Join-Path $repositoryRoot $(if ($IsWindows) { ".dotnet/dotnet.exe" } else { ".dotnet/dotnet" })
    if (Test-Path -LiteralPath $localDotnet -PathType Leaf) {
        $dotnet = $localDotnet
        $env:DOTNET_ROOT = Split-Path -Parent $localDotnet
        $env:DOTNET_ROOT_X64 = $env:DOTNET_ROOT
        $env:PATH = "$env:DOTNET_ROOT$([IO.Path]::PathSeparator)$env:PATH"
    } else {
        $dotnet = (Get-Command dotnet -ErrorAction Stop).Source
    }
    $checks = Join-Path $repositoryRoot "native/tools/RepositoryChecks/RepositoryChecks.csproj"
    foreach ($route in @("interop-write", "knowledge-write", "interop", "docs")) {
        Invoke-PreviewCommand $dotnet @("run", "--project", $checks, "--configuration", "Release", "--", $route, $repositoryRoot)
    }
    $testProject = Join-Path $repositoryRoot "native/tests/VibeSnake.Rules.Tests/VibeSnake.Rules.Tests.csproj"
    $filter = "FullyQualifiedName~AgentPassport|FullyQualifiedName~AgentExhibitionStory|FullyQualifiedName~AgentQualification|FullyQualifiedName~AgentHostTests"
    Invoke-PreviewCommand $dotnet @("test", $testProject, "--filter", $filter, "--nologo")
    if ($full) {
        $pwsh = (Get-Command pwsh -ErrorAction Stop).Source
        Invoke-PreviewCommand $pwsh @("-NoProfile", "-File", (Join-Path $PSScriptRoot "test_native.ps1"))
    }
    Write-Output "Agent Arena preview close-out passed."
    if ($commit) {
        $git = (Get-Command git -ErrorAction Stop).Source
        & $git diff --cached --quiet
        if ($LASTEXITCODE -eq 0) {
            Write-Output "No staged changes to commit."
        } elseif ($LASTEXITCODE -eq 1) {
            $env:GIT_AUTHOR_NAME = "Nick Seal"
            $env:GIT_AUTHOR_EMAIL = "32712898+blisspixel@users.noreply.github.com"
            $env:GIT_COMMITTER_NAME = $env:GIT_AUTHOR_NAME
            $env:GIT_COMMITTER_EMAIL = $env:GIT_AUTHOR_EMAIL
            Invoke-PreviewCommand $git @("-c", "user.name=Nick Seal", "-c", "user.email=$env:GIT_AUTHOR_EMAIL", "commit", "-m", "Qualify the Agent Arena developer preview")
        } else {
            exit $LASTEXITCODE
        }
    } else {
        Write-Output "Review and stage the intended changes before committing."
    }
} catch {
    [Console]::Error.WriteLine($_.Exception.Message)
    exit 1
} finally {
    Set-Location -LiteralPath $previousLocation.Path
    foreach ($name in $previousEnvironment.Keys) {
        [Environment]::SetEnvironmentVariable($name, $previousEnvironment[$name], "Process")
    }
}
