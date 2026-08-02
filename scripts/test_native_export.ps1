[CmdletBinding()]
param(
    [Parameter()]
    [string]$GodotExecutable,

    [Parameter()]
    [string]$GodotArchivePath,

    [Parameter()]
    [string]$OutputDirectory,

    [Parameter()]
    [ValidateSet("Debug", "Release")]
    [string]$BuildMode = "Debug",

    [Parameter()]
    [switch]$SkipTemplateInstall
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$projectDirectory = Join-Path $repositoryRoot "game"

if ($IsWindows) {
    $platformId = "windows-x64"
    $preset = "Windows x64"
    $artifactName = "VibeSnake.exe"
} elseif ($IsMacOS) {
    $platformId = "macos-universal"
    $preset = "macOS Universal"
    $artifactName = "VibeSnake.zip"
} elseif ($IsLinux) {
    $platformId = "linux-x64"
    $preset = "Linux x64"
    $artifactName = "VibeSnake.x86_64"
} else {
    throw "Native export qualification does not support this operating system."
}

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
if (-not (Test-Path -LiteralPath $resolvedGodotExecutable -PathType Leaf)) {
    throw "Godot executable does not exist: $resolvedGodotExecutable"
}
$verificationArguments = @{ GodotExecutable = $resolvedGodotExecutable }
if ($GodotArchivePath) {
    $verificationArguments.GodotArchivePath = $GodotArchivePath
}
& (Join-Path $PSScriptRoot "assert_godot_toolchain.ps1") @verificationArguments

if (-not $SkipTemplateInstall) {
    & (Join-Path $PSScriptRoot "install_godot_templates.ps1") -PlatformId $platformId
    if ($LASTEXITCODE -ne 0) {
        throw "Godot export template installation or verification failed."
    }
}

if ($OutputDirectory) {
    $resolvedOutputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)
} else {
    $resolvedOutputDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ("vibesnake-native-export-{0}" -f [Guid]::NewGuid())
}

$comparison = if ($IsWindows) { [StringComparison]::OrdinalIgnoreCase } else { [StringComparison]::Ordinal }
$repositoryPrefix = $repositoryRoot.TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
if ($resolvedOutputDirectory.StartsWith($repositoryPrefix, $comparison)) {
    throw "Export qualification must run outside the source checkout: $resolvedOutputDirectory"
}

if (Test-Path -LiteralPath $resolvedOutputDirectory) {
    if (Get-ChildItem -LiteralPath $resolvedOutputDirectory -Force | Select-Object -First 1) {
        throw "Refusing to overwrite a non-empty export directory: $resolvedOutputDirectory"
    }
} else {
    New-Item -ItemType Directory -Path $resolvedOutputDirectory | Out-Null
}

$artifactPath = Join-Path $resolvedOutputDirectory $artifactName
$exportArgument = if ($BuildMode -eq "Release") { "--export-release" } else { "--export-debug" }

$canonicalLockPaths = @(
    (Join-Path $repositoryRoot "game/packages.lock.json"),
    (Join-Path $repositoryRoot "native/src/VibeSnake.Persistence/packages.lock.json"),
    (Join-Path $repositoryRoot "native/src/VibeSnake.Rules/packages.lock.json"),
    (Join-Path $repositoryRoot "native/tests/VibeSnake.Rules.Tests/packages.lock.json")
)
$canonicalLockHashes = @{}
foreach ($lockPath in $canonicalLockPaths) {
    if (-not (Test-Path -LiteralPath $lockPath -PathType Leaf)) {
        throw "Canonical NuGet lock file does not exist: $lockPath"
    }

    $canonicalLockHashes[$lockPath] = (Get-FileHash -LiteralPath $lockPath -Algorithm SHA256).Hash
}

try {
    $exportOutput = & $resolvedGodotExecutable --headless --path $projectDirectory $exportArgument $preset $artifactPath 2>&1
    $exportExitCode = $LASTEXITCODE
} finally {
    foreach ($lockPath in $canonicalLockPaths) {
        $currentHash = (Get-FileHash -LiteralPath $lockPath -Algorithm SHA256).Hash
        if ($currentHash -ne $canonicalLockHashes[$lockPath]) {
            throw "Godot export changed canonical dependency lock: $lockPath"
        }
    }
}

$exportOutput | Write-Output
if ($exportExitCode -ne 0) {
    throw "Godot $BuildMode export failed for $preset."
}

$exportErrors = $exportOutput | Where-Object {
    $_ -match "^ERROR:" -or $_ -match "completed with warnings"
}
if ($exportErrors) {
    throw "Godot $BuildMode export reported errors or warnings for $preset."
}

if (-not (Test-Path -LiteralPath $artifactPath -PathType Leaf)) {
    throw "Godot reported success but did not create the expected artifact: $artifactPath"
}

$smokeRoot = $null
$smokeUserDataRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("vibesnake-player-user-data-{0}" -f [Guid]::NewGuid())
$playerProcess = $null
$smokeLog = Join-Path ([System.IO.Path]::GetTempPath()) ("vibesnake-player-smoke-{0}.log" -f [Guid]::NewGuid())
try {
    New-Item -ItemType Directory -Path $smokeUserDataRoot | Out-Null
    if ($IsMacOS) {
        $smokeRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("vibesnake-macos-smoke-{0}" -f [Guid]::NewGuid())
        New-Item -ItemType Directory -Path $smokeRoot | Out-Null
        Expand-Archive -LiteralPath $artifactPath -DestinationPath $smokeRoot
        $playerExecutable = Get-ChildItem -LiteralPath $smokeRoot -Recurse -File |
            Where-Object { $_.FullName -match "[\\/]Contents[\\/]MacOS[\\/][^\\/]+$" } |
            Select-Object -First 1 -ExpandProperty FullName
        if (-not $playerExecutable) {
            throw "The macOS archive does not contain an application executable."
        }

        & chmod +x $playerExecutable
        if ($LASTEXITCODE -ne 0) {
            throw "Could not mark the macOS player as executable."
        }
    } else {
        $playerExecutable = $artifactPath
        if ($IsLinux) {
            & chmod +x $playerExecutable
            if ($LASTEXITCODE -ne 0) {
                throw "Could not mark the Linux player as executable."
            }
        }
    }

    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $playerExecutable
    $startInfo.WorkingDirectory = $resolvedOutputDirectory
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    foreach ($argument in @("--headless", "--log-file", $smokeLog, "--", "--smoke-test", "--smoke-user-data-root=$smokeUserDataRoot")) {
        $startInfo.ArgumentList.Add($argument)
    }

    $playerProcess = [System.Diagnostics.Process]::Start($startInfo)
    if (-not $playerProcess) {
        throw "The exported $platformId player process could not be started."
    }

    $deadline = [DateTime]::UtcNow.AddSeconds(30)
    $smokeText = ""
    do {
        if (Test-Path -LiteralPath $smokeLog -PathType Leaf) {
            try {
                $smokeText = Get-Content -LiteralPath $smokeLog -Raw -ErrorAction Stop
            } catch {
                $smokeText = ""
            }

            if ($smokeText -match "VIBESNAKE_GODOT_SMOKE_(?:OK|FAILED)") {
                break
            }
        }

        if ($playerProcess.HasExited) {
            break
        }

        Start-Sleep -Milliseconds 100
    } while ([DateTime]::UtcNow -lt $deadline)

    if (-not $playerProcess.HasExited -and -not $playerProcess.WaitForExit(10000)) {
        throw "The exported $platformId player did not exit after its smoke marker."
    }

    $smokeExitCode = $playerProcess.ExitCode
    if (Test-Path -LiteralPath $smokeLog -PathType Leaf) {
        $smokeText = Get-Content -LiteralPath $smokeLog -Raw
    }

    if ($smokeText) {
        $smokeText.TrimEnd() | Write-Output
    }

    if ($smokeExitCode -ne 0) {
        throw "The exported $platformId player exited with code $smokeExitCode."
    }

    if ($smokeText -notmatch "VIBESNAKE_GODOT_SMOKE_OK hash=([0-9a-f]{16})") {
        throw "The exported player did not emit the deterministic smoke marker."
    }
    $smokeStateHash = $Matches[1]

    if (
        ($smokeText -match "(?m)^(?:ERROR|WARNING):") -or
        ($smokeText -match "ObjectDB instances? (?:was|were) leaked") -or
        ($smokeText -match "Leaked instance:")
    ) {
        throw "The exported player reported an error, warning, or leaked object."
    }

    $replayDirectory = Join-Path $smokeUserDataRoot "replays"
    $storedReplays = @(Get-ChildItem -LiteralPath $replayDirectory -File -Filter "*.vibesnake-replay.json")
    if ($storedReplays.Count -ne 1) {
        throw "The exported player did not create exactly one isolated replay."
    }
    if (Get-ChildItem -LiteralPath $replayDirectory -File -Filter "*.tmp-*" | Select-Object -First 1) {
        throw "The exported player left an incomplete atomic replay file."
    }
} finally {
    if ($playerProcess) {
        if (-not $playerProcess.HasExited) {
            $playerProcess.Kill($true)
            if (-not $playerProcess.WaitForExit(5000)) {
                throw "The exported $platformId player could not be stopped."
            }
        }

        $playerProcess.Dispose()
    }

    foreach ($temporaryDirectory in @($smokeRoot, $smokeUserDataRoot)) {
        if ($temporaryDirectory -and (Test-Path -LiteralPath $temporaryDirectory)) {
            $temporaryPrefix = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath()).TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
            $resolvedTemporaryDirectory = [System.IO.Path]::GetFullPath($temporaryDirectory)
            if (-not $resolvedTemporaryDirectory.StartsWith($temporaryPrefix, $comparison)) {
                throw "Refusing to clean an unexpected smoke directory: $resolvedTemporaryDirectory"
            }

            [System.IO.Directory]::Delete($resolvedTemporaryDirectory, $true)
        }
    }

    if (Test-Path -LiteralPath $smokeLog -PathType Leaf) {
        $temporaryPrefix = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath()).TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
        $resolvedSmokeLog = [System.IO.Path]::GetFullPath($smokeLog)
        if (-not $resolvedSmokeLog.StartsWith($temporaryPrefix, $comparison)) {
            throw "Refusing to clean an unexpected smoke log: $resolvedSmokeLog"
        }

        Remove-Item -LiteralPath $resolvedSmokeLog -Force
    }
}

& (Join-Path $PSScriptRoot "inspect_native_artifact.ps1") `
    -ArtifactRoot $resolvedOutputDirectory `
    -PlatformId $platformId `
    -BuildMode $BuildMode `
    -SmokeStateHash $smokeStateHash `
    -GodotExecutable $resolvedGodotExecutable `
    -GodotArchivePath $GodotArchivePath
if ($LASTEXITCODE -ne 0) {
    throw "Native artifact inspection failed for $platformId."
}

if ($env:GITHUB_OUTPUT) {
    "artifact-path=$artifactPath" | Out-File -LiteralPath $env:GITHUB_OUTPUT -Encoding utf8 -Append
    "artifact-root=$resolvedOutputDirectory" | Out-File -LiteralPath $env:GITHUB_OUTPUT -Encoding utf8 -Append
}

Write-Output "NativeArtifact=$artifactPath"
Write-Output "NativeArtifactRoot=$resolvedOutputDirectory"
Write-Output "Native export qualification passed for $platformId."
