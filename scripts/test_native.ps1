[CmdletBinding()]
param(
    [Parameter()]
    [string]$GodotExecutable,

    [Parameter()]
    [string]$GodotArchivePath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$localDotnetCandidates = @(
    (Join-Path $repositoryRoot ".dotnet/dotnet.exe"),
    (Join-Path $repositoryRoot ".dotnet/dotnet")
)
$localDotnet = $localDotnetCandidates | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1

if ($localDotnet) {
    $dotnetExecutable = $localDotnet
    $dotnetRoot = Split-Path -Parent $localDotnet
    $env:DOTNET_ROOT = $dotnetRoot
    $env:PATH = "$dotnetRoot$([System.IO.Path]::PathSeparator)$env:PATH"
} else {
    $dotnetCommand = Get-Command dotnet -ErrorAction Stop
    $dotnetExecutable = $dotnetCommand.Source
}

function Invoke-Dotnet {
    param(
        [Parameter(Mandatory)]
        [string[]]$CommandArguments
    )

    & $dotnetExecutable @CommandArguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet command failed: dotnet $($CommandArguments -join ' ')"
    }
}

$smokeUserDataRoot = $null
Push-Location $repositoryRoot
try {
    if (-not $GodotExecutable) {
        $installerOutput = & (Join-Path $PSScriptRoot "install_godot.ps1")
        if ($LASTEXITCODE -ne 0) {
            throw "Godot installation or verification failed."
        }

        $executableLine = $installerOutput | Where-Object { $_ -like "GodotExecutable=*" } | Select-Object -Last 1
        if (-not $executableLine) {
            throw "The Godot bootstrap did not report an executable path."
        }

        $GodotExecutable = $executableLine.Substring("GodotExecutable=".Length)
    }

    $resolvedGodotExecutable = [System.IO.Path]::GetFullPath($GodotExecutable)
    $verificationArguments = @{ GodotExecutable = $resolvedGodotExecutable }
    if ($GodotArchivePath) {
        $verificationArguments.GodotArchivePath = $GodotArchivePath
    }
    & (Join-Path $PSScriptRoot "assert_godot_toolchain.ps1") @verificationArguments
    & (Join-Path $PSScriptRoot "test_powershell_gates.ps1") @verificationArguments

    Invoke-Dotnet -CommandArguments @("--version")
    Invoke-Dotnet -CommandArguments @("restore", "native/VibeSnake.slnx", "--locked-mode")
    Invoke-Dotnet -CommandArguments @(
        "build",
        "native/VibeSnake.slnx",
        "--configuration",
        "Release",
        "--no-restore"
    )
    Invoke-Dotnet -CommandArguments @(
        "format",
        "native/VibeSnake.slnx",
        "--verify-no-changes",
        "--no-restore"
    )
    # Coverlet can fail on Windows runners with truncated hit-file streams even when
    # every test passed. Retry once after a clean rebuild of the test assembly.
    $testArguments = @(
        "test",
        "native/tests/VibeSnake.Rules.Tests/VibeSnake.Rules.Tests.csproj",
        "--configuration",
        "Release",
        "--no-build",
        "--no-restore",
        "-p:CollectCoverage=true",
        "-p:CoverletOutput=../../../TestResults/native/",
        "-p:CoverletOutputFormat=cobertura",
        "-p:Threshold=80",
        "-p:ThresholdType=line",
        "-p:ExcludeByFile=**/Properties/AssemblyInfo.cs"
    )
    $testSucceeded = $false
    for ($attempt = 1; $attempt -le 2; $attempt++) {
        try {
            Invoke-Dotnet -CommandArguments $testArguments
            $testSucceeded = $true
            break
        }
        catch {
            if ($attempt -ge 2) {
                throw
            }

            Write-Output "Native coverage attempt $attempt failed; rebuilding and retrying once. $_"
            if (Test-Path "TestResults/native") {
                Remove-Item -Recurse -Force "TestResults/native" -ErrorAction SilentlyContinue
            }
            Invoke-Dotnet -CommandArguments @(
                "build",
                "native/tests/VibeSnake.Rules.Tests/VibeSnake.Rules.Tests.csproj",
                "--configuration",
                "Release",
                "--no-restore"
            )
        }
    }
    if (-not $testSucceeded) {
        throw "Native tests with coverage failed after retry."
    }

    $importOutput = & $resolvedGodotExecutable --headless --editor --path game --quit 2>&1
    $importExitCode = $LASTEXITCODE
    $importOutput | Write-Output
    if ($importExitCode -ne 0) {
        throw "Godot headless import failed."
    }
    if ($importOutput | Where-Object { $_ -match "^(?:ERROR|WARNING):" -or $_ -match "ObjectDB instances? (?:was|were) leaked" }) {
        throw "Godot headless import reported an error, warning, or leaked object."
    }

    $smokeUserDataRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("vibesnake-godot-user-data-{0}" -f [Guid]::NewGuid())
    New-Item -ItemType Directory -Path $smokeUserDataRoot | Out-Null
    $smokeOutput = & $resolvedGodotExecutable --headless --path game -- --smoke-test "--smoke-user-data-root=$smokeUserDataRoot" 2>&1
    $smokeExitCode = $LASTEXITCODE
    $smokeOutput | Write-Output
    if ($smokeExitCode -ne 0) {
        throw "Godot deterministic smoke failed."
    }
    if ($smokeOutput | Where-Object { $_ -match "^(?:ERROR|WARNING):" -or $_ -match "ObjectDB instances? (?:was|were) leaked" }) {
        throw "Godot deterministic smoke reported an error, warning, or leaked object."
    }
    if (($smokeOutput -join "`n") -notmatch "VIBESNAKE_GODOT_SMOKE_OK hash=[0-9a-f]{16}") {
        throw "Godot deterministic smoke did not emit its success marker."
    }

    $replayDirectory = Join-Path $smokeUserDataRoot "replays"
    $storedReplays = @(Get-ChildItem -LiteralPath $replayDirectory -File -Filter "*.vibesnake-replay.json")
    if ($storedReplays.Count -ne 1) {
        throw "Godot smoke did not create exactly one isolated replay."
    }
    if (Get-ChildItem -LiteralPath $replayDirectory -File -Filter "*.tmp-*" | Select-Object -First 1) {
        throw "Godot smoke left an incomplete atomic replay file."
    }

    Write-Output "Native qualification checks passed."
} finally {
    if ($smokeUserDataRoot -and (Test-Path -LiteralPath $smokeUserDataRoot)) {
        $comparison = if ($IsWindows) { [StringComparison]::OrdinalIgnoreCase } else { [StringComparison]::Ordinal }
        $temporaryPrefix = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath()).TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
        $resolvedSmokeUserDataRoot = [System.IO.Path]::GetFullPath($smokeUserDataRoot)
        if (-not $resolvedSmokeUserDataRoot.StartsWith($temporaryPrefix, $comparison)) {
            throw "Refusing to clean an unexpected smoke user-data directory: $resolvedSmokeUserDataRoot"
        }

        [System.IO.Directory]::Delete($resolvedSmokeUserDataRoot, $true)
    }

    Pop-Location
}
