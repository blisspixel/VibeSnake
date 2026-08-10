[CmdletBinding()]
param(
    [Parameter()]
    [string]$GodotExecutable,

    [Parameter()]
    [string]$GodotArchivePath,

    [Parameter()]
    [string]$OutputDirectory,

    [Parameter()]
    [string]$EvidenceDirectory,

    [Parameter()]
    [ValidateSet("Debug", "Release")]
    [string]$BuildMode = "Debug",

    [Parameter()]
    [ValidateRange(0, 1000)]
    [int]$CandidateLaunchCount = 0,

    [Parameter()]
    [switch]$CandidateLifecycle,

    [Parameter()]
    [switch]$SkipTemplateInstall
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$projectDirectory = Join-Path $repositoryRoot "game"
$pyprojectText = Get-Content -LiteralPath (Join-Path $repositoryRoot "pyproject.toml") -Raw
$productVersionMatch = [regex]::Match(
    $pyprojectText,
    '(?m)^version\s*=\s*"([0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z.-]+)?)"\s*$')
if (-not $productVersionMatch.Success) {
    throw "Could not resolve the canonical product version from pyproject.toml."
}
$productVersion = $productVersionMatch.Groups[1].Value
$resolvedEvidenceDirectory = if ($EvidenceDirectory) {
    [System.IO.Path]::GetFullPath($EvidenceDirectory)
} else {
    [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot "TestResults/native"))
}

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
$qualificationPackageDirectory = $resolvedOutputDirectory + "-qualification-package"
if (Test-Path -LiteralPath $qualificationPackageDirectory) {
    throw "Refusing to overwrite an existing qualification package path: $qualificationPackageDirectory"
}

if (Test-Path -LiteralPath $resolvedOutputDirectory) {
    if (Get-ChildItem -LiteralPath $resolvedOutputDirectory -Force | Select-Object -First 1) {
        throw "Refusing to overwrite a non-empty export directory: $resolvedOutputDirectory"
    }
} else {
    New-Item -ItemType Directory -Path $resolvedOutputDirectory | Out-Null
}

function Get-InstallSnapshot {
    param([Parameter(Mandatory)][string]$InstallRoot)

    $files = @(Get-ChildItem -LiteralPath $InstallRoot -Recurse -File | Sort-Object FullName)
    if (-not $files) {
        throw "Install snapshot root is empty: $InstallRoot"
    }

    $lines = @(
        foreach ($file in $files) {
            $relativePath = [System.IO.Path]::GetRelativePath($InstallRoot, $file.FullName).Replace("\", "/")
            $sha256 = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
            "$relativePath|$($file.Length)|$sha256"
        }
    )
    $bytes = [Text.Encoding]::UTF8.GetBytes(($lines -join "`n"))
    $hasher = [Security.Cryptography.SHA256]::Create()
    try {
        $aggregate = [Convert]::ToHexString($hasher.ComputeHash($bytes)).ToLowerInvariant()
    } finally {
        $hasher.Dispose()
    }

    return [pscustomobject]@{
        FileCount = $files.Count
        AggregateSha256 = $aggregate
    }
}

function Set-InstallReadOnly {
    param([Parameter(Mandatory)][string]$InstallRoot)

    if ($IsWindows) {
        $identity = [Security.Principal.WindowsIdentity]::GetCurrent().Name
        $output = & icacls $InstallRoot /deny "${identity}:(W)" /T /C /Q 2>&1
        if ($LASTEXITCODE -ne 0) {
            $output | Write-Output
            throw "Could not apply temporary read-only install ACL: $InstallRoot"
        }

        return $identity
    }

    & chmod -R a-w $InstallRoot
    if ($LASTEXITCODE -ne 0) {
        throw "Could not remove write permission from temporary install root: $InstallRoot"
    }

    return "posix"
}

function Restore-InstallPermissions {
    param(
        [Parameter(Mandatory)][string]$InstallRoot,
        [Parameter(Mandatory)][string]$PermissionToken
    )

    if ($IsWindows) {
        $output = & icacls $InstallRoot /remove:d $PermissionToken /T /C /Q 2>&1
        if ($LASTEXITCODE -ne 0) {
            $output | Write-Output
            throw "Could not remove temporary read-only install ACL: $InstallRoot"
        }

        return
    }

    & chmod -R u+w $InstallRoot
    if ($LASTEXITCODE -ne 0) {
        throw "Could not restore write permission to temporary install root: $InstallRoot"
    }
}

function Invoke-PlayerLaunchProbe {
    param(
        [Parameter(Mandatory)][string]$Executable,
        [Parameter(Mandatory)][string]$WorkingDirectory,
        [Parameter(Mandatory)][string]$UserDataRoot,
        [Parameter(Mandatory)][string]$LogPath,
        [Parameter()][string[]]$ProbeArguments = @()
    )

    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $Executable
    $startInfo.WorkingDirectory = $WorkingDirectory
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $arguments = @(
        "--headless",
        "--log-file",
        $LogPath,
        "--",
        "--launch-probe",
        "--smoke-user-data-root=$UserDataRoot"
    ) + $ProbeArguments
    foreach ($argument in $arguments) {
        $startInfo.ArgumentList.Add($argument)
    }

    $process = [System.Diagnostics.Process]::Start($startInfo)
    if (-not $process) {
        throw "Player launch probe could not start."
    }
    try {
        if (-not $process.WaitForExit(30000)) {
            $process.Kill($true)
            [void]$process.WaitForExit(5000)
            return [pscustomobject]@{
                Passed = $false
                Failure = "timeout"
                Text = ""
            }
        }
        $text = if (Test-Path -LiteralPath $LogPath -PathType Leaf) {
            Get-Content -LiteralPath $LogPath -Raw
        } else {
            ""
        }
        $passed = $process.ExitCode -eq 0 -and
            $text -match "VIBESNAKE_LAUNCH_PROBE_OK" -and
            $text -notmatch "(?m)^(?:ERROR|WARNING):" -and
            $text -notmatch "ObjectDB instances? (?:was|were) leaked" -and
            $text -notmatch "Leaked instance:"
        return [pscustomobject]@{
            Passed = $passed
            Failure = if ($passed) { "" } else { "exit-marker-or-log" }
            Text = $text
        }
    } finally {
        $process.Dispose()
    }
}

function Get-LegacyPreferencesJson {
    param(
        [Parameter(Mandatory)]
        [ValidateRange(1, 6)]
        [int]$SchemaVersion
    )

    if ($SchemaVersion -eq 1) {
        return ([ordered]@{
            schema_version = 1
            sound_on = $true
            volume = 0.65
            fullscreen = $false
        } | ConvertTo-Json -Compress) + "`n"
    }

    $document = [ordered]@{
        schemaVersion = $SchemaVersion
        masterVolume = 0.65
        musicVolume = 0.55
        sfxVolume = 0.75
        uiVolume = 0.70
        masterMuted = $false
        musicMuted = $false
        sfxMuted = $false
        uiMuted = $false
        fullscreen = $false
        reducedMotion = $true
        highContrast = $true
        textScale = 1.25
        screenShakeIntensity = 0.25
        flashFree = $true
        controllerDeadzone = 0.60
        monoOutput = $true
        vibeAdaptationEnabled = $false
        localPlaytestSummariesEnabled = $true
    }
    if ($SchemaVersion -lt 3) {
        [void]$document.Remove("controllerDeadzone")
    }
    if ($SchemaVersion -lt 4) {
        [void]$document.Remove("monoOutput")
    }
    if ($SchemaVersion -lt 5) {
        [void]$document.Remove("vibeAdaptationEnabled")
    }
    if ($SchemaVersion -lt 6) {
        [void]$document.Remove("localPlaytestSummariesEnabled")
    }
    return ($document | ConvertTo-Json -Compress) + "`n"
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
$smokeUserDataRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("VibeSnake player profile café {0}" -f [Guid]::NewGuid())
$launchProbeRoot = $null
$lifecycleRoot = $null
$playerProcess = $null
$smokeLog = Join-Path ([System.IO.Path]::GetTempPath()) ("VibeSnake player log café {0}.log" -f [Guid]::NewGuid())
$installRoot = $null
$installPermissionToken = $null
$installSnapshotBefore = $null
$readOnlyWriteRejected = $false
$writeProbePath = $null
try {
    if (Test-Path -LiteralPath $smokeUserDataRoot) {
        throw "Fresh-profile path unexpectedly exists: $smokeUserDataRoot"
    }
    New-Item -ItemType Directory -Path $smokeUserDataRoot | Out-Null
    $smokeRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("VibeSnake install café {0}" -f [Guid]::NewGuid())
    New-Item -ItemType Directory -Path $smokeRoot | Out-Null
    if ($IsMacOS) {
        Expand-Archive -LiteralPath $artifactPath -DestinationPath $smokeRoot
        $appBundle = Get-ChildItem -LiteralPath $smokeRoot -Directory -Filter "*.app" |
            Select-Object -First 1 -ExpandProperty FullName
        if (-not $appBundle) {
            throw "The macOS archive does not contain an application bundle."
        }
        $installRoot = $appBundle
        $playerExecutable = Get-ChildItem -LiteralPath $installRoot -Recurse -File |
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
        Get-ChildItem -LiteralPath $resolvedOutputDirectory -Force | ForEach-Object {
            Copy-Item -LiteralPath $_.FullName -Destination $smokeRoot -Recurse -Force
        }
        $installRoot = $smokeRoot
        $playerExecutable = Join-Path $installRoot $artifactName
        if (-not (Test-Path -LiteralPath $playerExecutable -PathType Leaf)) {
            throw "The staged install does not contain the expected player: $playerExecutable"
        }
        if ($IsLinux) {
            & chmod +x $playerExecutable
            if ($LASTEXITCODE -ne 0) {
                throw "Could not mark the Linux player as executable."
            }
        }
    }

    $installSnapshotBefore = Get-InstallSnapshot -InstallRoot $installRoot
    $installPermissionToken = Set-InstallReadOnly -InstallRoot $installRoot
    $writeProbePath = Join-Path $installRoot ".vibesnake-read-only-probe"
    try {
        [System.IO.File]::WriteAllText($writeProbePath, "write must fail")
    } catch {
        $readOnlyWriteRejected = $true
    }

    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $playerExecutable
    $startInfo.WorkingDirectory = $installRoot
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.Environment["VIBESNAKE_EVIDENCE_DIR"] = $resolvedEvidenceDirectory
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

    $reliabilityEvidencePath = Join-Path $resolvedEvidenceDirectory "candidate_reliability.json"
    if (-not (Test-Path -LiteralPath $reliabilityEvidencePath -PathType Leaf)) {
        throw "The exported player did not retain candidate reliability evidence."
    }
    $reliabilityEvidence = Get-Content -LiteralPath $reliabilityEvidencePath -Raw | ConvertFrom-Json
    if (($reliabilityEvidence.schemaVersion -ne 1) -or
        ($reliabilityEvidence.kind -ne "candidate-reliability-qualification-v1") -or
        (-not $reliabilityEvidence.passed) -or
        ($reliabilityEvidence.requiredStepsPerRuleset -ne 100000) -or
        ($reliabilityEvidence.rulesetCount -ne 2) -or
        ($reliabilityEvidence.totalComparedSimulationSteps -ne 200000) -or
        (@($reliabilityEvidence.simulations).Count -ne 2) -or
        (@($reliabilityEvidence.simulations | Where-Object {
            $null -ne $_.firstDivergence
        }).Count -ne 0) -or
        ($reliabilityEvidence.spectatorRestarts.completedRestarts -ne 100) -or
        ($reliabilityEvidence.spectatorRestarts.completedSteps -ne 800) -or
        ($reliabilityEvidence.spectatorRestarts.managedSessionReferencesRetained -ne 0) -or
        (-not $reliabilityEvidence.spectatorRestarts.noMonotonicStateOrResourceGrowth) -or
        (@($reliabilityEvidence.spectatorRestarts.resourceSamples).Count -ne 11)) {
        throw "The exported player candidate reliability evidence failed its closed summary gate."
    }

    $faultEvidencePath = Join-Path $resolvedEvidenceDirectory "candidate_fault_campaign.json"
    if (-not (Test-Path -LiteralPath $faultEvidencePath -PathType Leaf)) {
        throw "The exported player did not retain candidate fault campaign evidence."
    }
    $faultEvidence = Get-Content -LiteralPath $faultEvidencePath -Raw | ConvertFrom-Json
    $expectedFaultIds = @(
        "interrupted-write",
        "corrupt-json",
        "full-disk",
        "read-only-data-directory",
        "missing-resource",
        "invalid-content-pack",
        "unavailable-audio"
    )
    if (($faultEvidence.schemaVersion -ne 1) -or
        ($faultEvidence.kind -ne "candidate-fault-campaign-v1") -or
        (-not $faultEvidence.passed) -or
        ($faultEvidence.requiredFaultCount -ne 7) -or
        ($faultEvidence.completedFaultCount -ne 7) -or
        (-not $faultEvidence.everyFaultDetected) -or
        (-not $faultEvidence.everyExistingDataBoundaryPreserved) -or
        (-not $faultEvidence.everyRecoveryPathVerified) -or
        (-not $faultEvidence.rulesStateUnchangedAcrossCampaign) -or
        ((@($faultEvidence.faults | ForEach-Object { $_.faultId }) -join ',') -ne
            ($expectedFaultIds -join ',')) -or
        (-not $faultEvidence.crashTriage.reportRetained) -or
        (-not $faultEvidence.crashTriage.privacySafe) -or
        (-not $faultEvidence.divergenceTriage.reportRetained) -or
        (-not $faultEvidence.divergenceTriage.privacySafe)) {
        throw "The exported player candidate fault campaign failed its closed summary gate."
    }

    $accessibilityAuditPath = Join-Path `
        $resolvedEvidenceDirectory `
        "candidate_accessibility_audit.json"
    if (-not (Test-Path -LiteralPath $accessibilityAuditPath -PathType Leaf)) {
        throw "The exported player did not retain candidate accessibility evidence."
    }
    $accessibilityAudit = Get-Content -LiteralPath $accessibilityAuditPath -Raw |
        ConvertFrom-Json
    $accessibilityAreaIds = @(
        "text",
        "contrast",
        "focus",
        "remapping",
        "single-action-navigation",
        "controller-only-use",
        "keyboard-only-use",
        "audio-separation",
        "visual-alternatives",
        "reduced-motion",
        "flash-safety",
        "documentation"
    )
    if (($accessibilityAudit.schemaVersion -ne 1) -or
        ($accessibilityAudit.kind -ne "candidate-accessibility-audit-v1") -or
        (-not $accessibilityAudit.passed) -or
        ($accessibilityAudit.requiredFlowDefectSeverity -ne "P1") -or
        (-not $accessibilityAudit.allAutomatedAuditAreasPassed) -or
        (-not $accessibilityAudit.keyboardOnlyRouteComplete) -or
        (-not $accessibilityAudit.controllerOnlyRouteComplete) -or
        (-not $accessibilityAudit.remappingComplete) -or
        (-not $accessibilityAudit.singleActionNavigationComplete) -or
        (-not $accessibilityAudit.independentAudioControlsComplete) -or
        (-not $accessibilityAudit.monoOutputComplete) -or
        (-not $accessibilityAudit.visualAlternativesComplete) -or
        (-not $accessibilityAudit.reducedMotionComplete) -or
        (-not $accessibilityAudit.flashSafetyComplete) -or
        (-not $accessibilityAudit.maximumTextScaleViewportMatrixComplete) -or
        ($accessibilityAudit.maximumTextScaleDisplayClassCount -ne 8) -or
        ((@($accessibilityAudit.auditAreas.id) -join ',') -ne
            ($accessibilityAreaIds -join ',')) -or
        ($accessibilityAudit.accessibilityUserReviewStatus -ne
            "pending-accessibility-user-review") -or
        ($accessibilityAudit.featurePublicationStatus -ne "published-in-repository") -or
        (@($accessibilityAudit.pendingHumanChecks).Count -ne 5)) {
        throw "The exported player candidate accessibility audit failed its closed summary gate."
    }

    $mouseEvidencePath = Join-Path $resolvedEvidenceDirectory "mouse_input.json"
    if (-not (Test-Path -LiteralPath $mouseEvidencePath -PathType Leaf)) {
        throw "The exported player did not retain mouse input evidence."
    }
    $mouseEvidence = Get-Content -LiteralPath $mouseEvidencePath -Raw | ConvertFrom-Json
    if (($mouseEvidence.schemaVersion -ne 1) -or
        ($mouseEvidence.kind -ne "mouse-input-qualification-v1") -or
        (-not $mouseEvidence.passed) -or
        ($mouseEvidence.deviceClass -ne "mouse") -or
        ($mouseEvidence.menuTargetCount -ne 9) -or
        (-not $mouseEvidence.menuHitTestingComplete) -or
        (-not $mouseEvidence.leftClickConfirmComplete) -or
        (-not $mouseEvidence.rightClickBackComplete) -or
        (-not $mouseEvidence.verticalWheelNavigationComplete) -or
        (-not $mouseEvidence.horizontalWheelNavigationComplete) -or
        (-not $mouseEvidence.gameplayDirectionComplete) -or
        (-not $mouseEvidence.windowScalingApplied) -or
        (-not $mouseEvidence.letterboxInputRejected) -or
        (-not $mouseEvidence.keyboardBindingsUnchanged) -or
        (-not $mouseEvidence.controllerBindingsUnchanged) -or
        (@($mouseEvidence.pendingHumanChecks).Count -ne 2)) {
        throw "The exported player mouse input evidence failed its closed summary gate."
    }

    $performanceEvidencePath = Join-Path $resolvedEvidenceDirectory "performance.json"
    if (-not (Test-Path -LiteralPath $performanceEvidencePath -PathType Leaf)) {
        throw "The exported player did not retain candidate performance evidence."
    }
    $performanceEvidence = Get-Content -LiteralPath $performanceEvidencePath -Raw |
        ConvertFrom-Json
    $performanceIds = @("minimum", "default", "maximum-safe")
    if (($performanceEvidence.schemaVersion -ne 1) -or
        ($performanceEvidence.kind -ne "performance-qualification-v1") -or
        (-not $performanceEvidence.passed) -or
        (-not $performanceEvidence.sharedHostRegressionCeilingMet) -or
        (-not $performanceEvidence.feedbackCannotChangeSimulationSpeed) -or
        (-not $performanceEvidence.rulesStateIdenticalAcrossProfiles) -or
        ($performanceEvidence.minimumHardwareAcceptanceStatus -ne "pending-named-hardware") -or
        ($performanceEvidence.rulesStepsPerProfile -ne 256) -or
        ([string]$performanceEvidence.finalRulesStateHash -notmatch '^[0-9a-f]{16}$') -or
        ((@($performanceEvidence.profiles.id) -join ',') -ne ($performanceIds -join ',')) -or
        ((@($performanceEvidence.measurements.id) -join ',') -ne ($performanceIds -join ',')) -or
        ((($performanceEvidence.measurements |
            Measure-Object -Property sampleCount -Sum).Sum) -lt 120)) {
        throw "The exported player candidate performance evidence failed its closed summary gate."
    }
    foreach ($measurement in @($performanceEvidence.measurements)) {
        if (($measurement.sampleCount -lt 40) -or
            ($measurement.p50FrameMilliseconds -gt $measurement.p95FrameMilliseconds) -or
            ($measurement.p95FrameMilliseconds -gt $measurement.p99FrameMilliseconds) -or
            ($measurement.p99FrameMilliseconds -gt $measurement.maximumFrameMilliseconds) -or
            ($measurement.averageFrameMilliseconds -gt 25.0) -or
            ($measurement.p95FrameMilliseconds -gt 60.0)) {
            throw "The exported player performance row drifted: $($measurement.id)"
        }
    }

    $replayDirectory = Join-Path $smokeUserDataRoot "replays"
    $storedReplays = @(Get-ChildItem -LiteralPath $replayDirectory -File -Filter "*.vibesnake-replay.json")
    # Storage smoke plus the death-restart terminal path each save a replay.
    if ($storedReplays.Count -lt 1 -or $storedReplays.Count -gt 4) {
        throw "The exported player replay count out of range: $($storedReplays.Count) (expected 1-4)."
    }
    if (Get-ChildItem -LiteralPath $replayDirectory -File -Filter "*.tmp-*" | Select-Object -First 1) {
        throw "The exported player left an incomplete atomic replay file."
    }

    $launchProbeFailures = @()
    if ($CandidateLaunchCount -gt 0) {
        if ($BuildMode -ne "Release") {
            throw "Candidate launch reliability is only valid for Release artifacts."
        }
        $launchProbeRoot = Join-Path `
            ([System.IO.Path]::GetTempPath()) `
            ("VibeSnake launch campaign café {0}" -f [Guid]::NewGuid())
        New-Item -ItemType Directory -Path $launchProbeRoot | Out-Null
        for ($launchIndex = 0; $launchIndex -lt $CandidateLaunchCount; $launchIndex++) {
            $probeUserDataRoot = Join-Path $launchProbeRoot ("profile-{0:D3}" -f $launchIndex)
            $probeLog = Join-Path $launchProbeRoot ("launch-{0:D3}.log" -f $launchIndex)
            if (Test-Path -LiteralPath $probeUserDataRoot) {
                throw "Candidate launch profile was not fresh: $probeUserDataRoot"
            }
            New-Item -ItemType Directory -Path $probeUserDataRoot | Out-Null
            $probe = Invoke-PlayerLaunchProbe `
                -Executable $playerExecutable `
                -WorkingDirectory $installRoot `
                -UserDataRoot $probeUserDataRoot `
                -LogPath $probeLog
            if (-not $probe.Passed) {
                $launchProbeFailures += "launch-$launchIndex-$($probe.Failure)"
            }
        }

        $launchSourceRevision = (& git -C $repositoryRoot rev-parse HEAD).Trim()
        if ($LASTEXITCODE -ne 0 -or $launchSourceRevision -notmatch '^[0-9a-f]{40}$') {
            throw "Candidate launch reliability could not resolve the source revision."
        }
        [System.IO.Directory]::CreateDirectory($resolvedEvidenceDirectory) | Out-Null
        $launchEvidence = [ordered]@{
            schemaVersion = 1
            kind = "candidate-launch-reliability-v1"
            passed = $launchProbeFailures.Count -eq 0
            platformId = $platformId
            buildMode = $BuildMode
            sourceRevision = $launchSourceRevision
            requestedLaunches = $CandidateLaunchCount
            completedLaunches = $CandidateLaunchCount - $launchProbeFailures.Count
            freshProfileLaunches = $CandidateLaunchCount
            readOnlyInstall = $true
            headless = $true
            timeoutSecondsPerLaunch = 30
            failures = $launchProbeFailures
        }
        $launchEvidencePath = Join-Path `
            $resolvedEvidenceDirectory `
            "candidate_launch_reliability.json"
        [System.IO.File]::WriteAllText(
            $launchEvidencePath,
            ($launchEvidence | ConvertTo-Json -Depth 6) + "`n",
            [Text.UTF8Encoding]::new($false))
        if ($launchProbeFailures.Count -ne 0) {
            throw "Candidate launch reliability failed: $($launchProbeFailures -join ', ')"
        }
    }

    $lifecycleMigrationRows = @()
    $additionalSaveMigrationRows = @()
    $futureSchemaPreserved = $false
    $optionalPackLifecyclePassed = $false
    $dataResetRecoveryPassed = $false
    $lifecycleRetainedProfile = $null
    if ($CandidateLifecycle) {
        if ($BuildMode -ne "Release") {
            throw "Candidate lifecycle qualification is only valid for Release artifacts."
        }
        $lifecycleRoot = Join-Path `
            ([System.IO.Path]::GetTempPath()) `
            ("VibeSnake lifecycle campaign café {0}" -f [Guid]::NewGuid())
        New-Item -ItemType Directory -Path $lifecycleRoot | Out-Null
        for ($schemaVersion = 1; $schemaVersion -le 6; $schemaVersion++) {
            $profileRoot = Join-Path $lifecycleRoot ("legacy-profile-schema-{0}" -f $schemaVersion)
            New-Item -ItemType Directory -Path $profileRoot | Out-Null
            $preferencesPath = Join-Path $profileRoot "preferences.json"
            [System.IO.File]::WriteAllText(
                $preferencesPath,
                (Get-LegacyPreferencesJson -SchemaVersion $schemaVersion),
                [Text.UTF8Encoding]::new($false))
            $beforeHash = (Get-FileHash -LiteralPath $preferencesPath -Algorithm SHA256).Hash.ToLowerInvariant()
            $probe = Invoke-PlayerLaunchProbe `
                -Executable $playerExecutable `
                -WorkingDirectory $installRoot `
                -UserDataRoot $profileRoot `
                -LogPath (Join-Path $lifecycleRoot ("migration-{0}.log" -f $schemaVersion)) `
                -ProbeArguments @("--launch-probe-preferences-schema=$schemaVersion")
            $expectedMarker = "input_schema=$schemaVersion effective_schema=7 code=Success"
            $afterHash = (Get-FileHash -LiteralPath $preferencesPath -Algorithm SHA256).Hash.ToLowerInvariant()
            if (-not $probe.Passed -or -not $probe.Text.Contains($expectedMarker, [StringComparison]::Ordinal)) {
                throw "Candidate lifecycle could not migrate preferences schema $schemaVersion."
            }
            if ($beforeHash -ne $afterHash) {
                throw "Candidate lifecycle rewrote a legacy preferences fixture during read-only migration."
            }
            $lifecycleMigrationRows += [ordered]@{
                inputSchema = $schemaVersion
                effectiveSchema = 7
                loadCode = "Success"
                sourcePreserved = $true
            }
            if ($schemaVersion -eq 6) {
                $lifecycleRetainedProfile = $profileRoot
            }
        }

        $personalBestProfile = Join-Path $lifecycleRoot "personal-best-schema-1"
        New-Item -ItemType Directory -Path $personalBestProfile | Out-Null
        $personalBestPath = Join-Path $personalBestProfile "personal_bests.json"
        $personalBestPayload = [ordered]@{
            schemaVersion = 1
            entries = @(
                [ordered]@{
                    rulesetId = "vibesnake-core"
                    rulesVersion = 4
                    configHash = "a" * 64
                    configHashAlgorithm = "sha256-canonical-runconfig-v1"
                    bestScore = 250
                }
            )
        } | ConvertTo-Json -Compress -Depth 4
        [System.IO.File]::WriteAllText(
            $personalBestPath,
            $personalBestPayload + "`n",
            [Text.UTF8Encoding]::new($false))
        $personalBestBeforeHash = (Get-FileHash -LiteralPath $personalBestPath -Algorithm SHA256).Hash.ToLowerInvariant()
        $personalBestProbe = Invoke-PlayerLaunchProbe `
            -Executable $playerExecutable `
            -WorkingDirectory $installRoot `
            -UserDataRoot $personalBestProfile `
            -LogPath (Join-Path $lifecycleRoot "personal-best-schema-1.log") `
            -ProbeArguments @("--launch-probe-fixture=personal-best-schema-1")
        $personalBestAfterHash = (Get-FileHash -LiteralPath $personalBestPath -Algorithm SHA256).Hash.ToLowerInvariant()
        if (-not $personalBestProbe.Passed -or
            -not $personalBestProbe.Text.Contains(
                "fixture=personal-best-schema-1 effective_schema=2 code=Success",
                [StringComparison]::Ordinal) -or
            $personalBestBeforeHash -ne $personalBestAfterHash) {
            throw "Candidate lifecycle could not migrate and preserve personal-best schema 1."
        }
        $additionalSaveMigrationRows += [ordered]@{
            fixture = "personal-best-schema-1"
            effectiveSchema = 2
            loadCode = "Success"
            sourcePreserved = $true
        }

        $playtestProfile = Join-Path $lifecycleRoot "local-playtest-summary-schema-1"
        $playtestStoreDirectory = Join-Path $playtestProfile "playtest-summaries"
        New-Item -ItemType Directory -Path $playtestStoreDirectory -Force | Out-Null
        $playtestPath = Join-Path $playtestStoreDirectory "summaries.json"
        $playtestPayload = [ordered]@{
            schemaVersion = 1
            kind = "vibesnake-local-playtest-summaries-v1"
            collectionBasis = "explicit-local-opt-in"
            retentionLimit = 200
            summaries = @()
        } | ConvertTo-Json -Compress -Depth 3
        [System.IO.File]::WriteAllText(
            $playtestPath,
            $playtestPayload + "`n",
            [Text.UTF8Encoding]::new($false))
        $playtestBeforeHash = (Get-FileHash -LiteralPath $playtestPath -Algorithm SHA256).Hash.ToLowerInvariant()
        $playtestProbe = Invoke-PlayerLaunchProbe `
            -Executable $playerExecutable `
            -WorkingDirectory $installRoot `
            -UserDataRoot $playtestProfile `
            -LogPath (Join-Path $lifecycleRoot "local-playtest-summary-schema-1.log") `
            -ProbeArguments @("--launch-probe-fixture=local-playtest-summary-schema-1")
        $playtestAfterHash = (Get-FileHash -LiteralPath $playtestPath -Algorithm SHA256).Hash.ToLowerInvariant()
        if (-not $playtestProbe.Passed -or
            -not $playtestProbe.Text.Contains(
                "fixture=local-playtest-summary-schema-1 effective_schema=2 code=Success",
                [StringComparison]::Ordinal) -or
            $playtestBeforeHash -ne $playtestAfterHash) {
            throw "Candidate lifecycle could not migrate and preserve local playtest summary schema 1."
        }
        $additionalSaveMigrationRows += [ordered]@{
            fixture = "local-playtest-summary-schema-1"
            effectiveSchema = 2
            loadCode = "Success"
            sourcePreserved = $true
        }

        $futureProfile = Join-Path $lifecycleRoot "future-profile-schema-99"
        New-Item -ItemType Directory -Path $futureProfile | Out-Null
        $futurePreferencesPath = Join-Path $futureProfile "preferences.json"
        [System.IO.File]::WriteAllText(
            $futurePreferencesPath,
            '{"schemaVersion":99,"masterVolume":0.5}' + "`n",
            [Text.UTF8Encoding]::new($false))
        $futureBeforeHash = (Get-FileHash -LiteralPath $futurePreferencesPath -Algorithm SHA256).Hash.ToLowerInvariant()
        $futureProbe = Invoke-PlayerLaunchProbe `
            -Executable $playerExecutable `
            -WorkingDirectory $installRoot `
            -UserDataRoot $futureProfile `
            -LogPath (Join-Path $lifecycleRoot "future-schema.log") `
            -ProbeArguments @("--launch-probe-expect-future-preferences")
        $futureAfterHash = (Get-FileHash -LiteralPath $futurePreferencesPath -Algorithm SHA256).Hash.ToLowerInvariant()
        $futureSchemaPreserved = $futureProbe.Passed -and
            $futureProbe.Text.Contains(
                "future_schema_rejected=true code=UnsupportedSchema",
                [StringComparison]::Ordinal) -and
            $futureBeforeHash -eq $futureAfterHash
        if (-not $futureSchemaPreserved) {
            throw "Candidate lifecycle did not preserve and reject future preferences."
        }

        $coreOnlyPath = Join-Path $resolvedEvidenceDirectory "core_only_offline.json"
        $recoveryPath = Join-Path $resolvedEvidenceDirectory "player_data_recovery.json"
        $coreOnly = Get-Content -LiteralPath $coreOnlyPath -Raw | ConvertFrom-Json
        $recovery = Get-Content -LiteralPath $recoveryPath -Raw | ConvertFrom-Json
        $optionalPackLifecyclePassed = [bool]$coreOnly.passed -and
            [bool]$coreOnly.removalRequiresExplicitConfirmation -and
            [bool]$coreOnly.removalCancelPreservesPack -and
            [bool]$coreOnly.removalConfirmIsTargeted -and
            [bool]$coreOnly.removalQuarantinedRecoverably -and
            [bool]$coreOnly.restoreRevalidated -and
            [bool]$coreOnly.playerDataPreservedByFilesystemLifecycle
        $dataResetRecoveryPassed = [bool]$recovery.passed -and
            [bool]$recovery.cancelWithoutWriteComplete -and
            [bool]$recovery.backupBeforeResetComplete -and
            [bool]$recovery.backupIntegrityComplete -and
            [bool]$recovery.separateCategoryResetComplete -and
            [bool]$recovery.conflictWithoutOverwriteComplete -and
            [bool]$recovery.restoreComplete
        if (-not $optionalPackLifecyclePassed -or -not $dataResetRecoveryPassed) {
            throw "Candidate lifecycle could not bind optional-pack or data-reset evidence."
        }
    }

    Restore-InstallPermissions `
        -InstallRoot $installRoot `
        -PermissionToken $installPermissionToken
    $installPermissionToken = $null
    if ($writeProbePath -and (Test-Path -LiteralPath $writeProbePath -PathType Leaf)) {
        Remove-Item -LiteralPath $writeProbePath -Force
    }

    $installSnapshotAfter = Get-InstallSnapshot -InstallRoot $installRoot
    $installUnchanged = $installSnapshotBefore.FileCount -eq $installSnapshotAfter.FileCount -and
        $installSnapshotBefore.AggregateSha256 -eq $installSnapshotAfter.AggregateSha256
    $installPrefix = [System.IO.Path]::GetFullPath($installRoot).TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
    $userDataOutsideInstall = -not [System.IO.Path]::GetFullPath($smokeUserDataRoot).StartsWith(
        $installPrefix,
        $comparison)
    $logOutsideInstall = -not [System.IO.Path]::GetFullPath($smokeLog).StartsWith(
        $installPrefix,
        $comparison)
    $evidenceOutsideInstall = -not [System.IO.Path]::GetFullPath(
        $resolvedEvidenceDirectory).StartsWith(
            $installPrefix,
            $comparison)
    $installPathQualified = $installRoot.Contains(" ") -and $installRoot -match '[^\x00-\x7f]'
    $userDataPathQualified = $smokeUserDataRoot.Contains(" ") -and
        $smokeUserDataRoot -match '[^\x00-\x7f]'
    $logPathQualified = $smokeLog.Contains(" ") -and $smokeLog -match '[^\x00-\x7f]'
    $evidencePassed = $readOnlyWriteRejected -and
        $installUnchanged -and
        $userDataOutsideInstall -and
        $logOutsideInstall -and
        $evidenceOutsideInstall -and
        $installPathQualified -and
        $userDataPathQualified -and
        $logPathQualified

    [System.IO.Directory]::CreateDirectory($resolvedEvidenceDirectory) | Out-Null
    $sourceRevision = (& git -C $repositoryRoot rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0 -or $sourceRevision -notmatch '^[0-9a-f]{40}$') {
        throw "Read-only install evidence could not resolve the source revision."
    }

    if ($CandidateLifecycle) {
        $replicaInstall = Join-Path $lifecycleRoot "repaired-install"
        Copy-Item -LiteralPath $installRoot -Destination $replicaInstall -Recurse
        $replicaSnapshot = Get-InstallSnapshot -InstallRoot $replicaInstall
        $repairSnapshotMatched = $replicaSnapshot.FileCount -eq $installSnapshotAfter.FileCount -and
            $replicaSnapshot.AggregateSha256 -eq $installSnapshotAfter.AggregateSha256
        $relativeExecutable = [System.IO.Path]::GetRelativePath($installRoot, $playerExecutable)
        $replicaExecutable = Join-Path $replicaInstall $relativeExecutable
        $repairProfile = Join-Path $lifecycleRoot "repair-profile"
        New-Item -ItemType Directory -Path $repairProfile | Out-Null
        $repairProbe = Invoke-PlayerLaunchProbe `
            -Executable $replicaExecutable `
            -WorkingDirectory $replicaInstall `
            -UserDataRoot $repairProfile `
            -LogPath (Join-Path $lifecycleRoot "repair-launch.log")
        $repairLaunchPassed = $repairProbe.Passed -and
            $repairProbe.Text.Contains(
                "effective_schema=6 code=Success",
                [StringComparison]::Ordinal)
        if (-not $repairSnapshotMatched -or -not $repairLaunchPassed) {
            throw "Candidate repair or reinstall copy did not match and launch."
        }

        $retainedPreferencesPath = Join-Path $lifecycleRetainedProfile "preferences.json"
        $retainedBeforeHash = (Get-FileHash -LiteralPath $retainedPreferencesPath -Algorithm SHA256).Hash.ToLowerInvariant()
        $lifecyclePrefix = [System.IO.Path]::GetFullPath($lifecycleRoot).TrimEnd(
            [System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
        $resolvedReplica = [System.IO.Path]::GetFullPath($replicaInstall)
        if (-not $resolvedReplica.StartsWith($lifecyclePrefix, $comparison)) {
            throw "Refusing to remove an unexpected lifecycle replica path."
        }
        [System.IO.Directory]::Delete($resolvedReplica, $true)
        $applicationRemovalPreservedPlayerData = -not (Test-Path -LiteralPath $resolvedReplica) -and
            (Test-Path -LiteralPath $retainedPreferencesPath -PathType Leaf) -and
            (Get-FileHash -LiteralPath $retainedPreferencesPath -Algorithm SHA256).Hash.ToLowerInvariant() -eq
                $retainedBeforeHash
        if (-not $applicationRemovalPreservedPlayerData) {
            throw "Candidate application removal did not preserve external player data."
        }

        $lifecycleEvidence = [ordered]@{
            schemaVersion = 1
            kind = "candidate-install-lifecycle-preflight-v1"
            passed = $true
            platformId = $platformId
            buildMode = $BuildMode
            sourceRevision = $sourceRevision
            installShape = if ($IsMacOS) { "expanded-app-bundle" } else { "portable-folder" }
            firstInstallPassed = $true
            readOnlyInstallPassed = $evidencePassed
            noElevationRequested = $true
            nonAsciiInstallAndUserPathsPassed = $installPathQualified -and $userDataPathQualified
            repairSnapshotMatched = $repairSnapshotMatched
            repairLaunchPassed = $repairLaunchPassed
            preferenceMigrationFixtureCount = $lifecycleMigrationRows.Count
            preferenceMigrations = $lifecycleMigrationRows
            additionalSaveMigrationFixtureCount = $additionalSaveMigrationRows.Count
            additionalSaveMigrations = $additionalSaveMigrationRows
            supportedSaveMigrationFixtureCount = $lifecycleMigrationRows.Count +
                $additionalSaveMigrationRows.Count
            futureSchemaRejectedAndPreserved = $futureSchemaPreserved
            rollbackNeverOverwritesNewerPreferences = $futureSchemaPreserved
            optionalPackAddRemovalRestorePassed = $optionalPackLifecyclePassed
            dataResetBackupRestorePassed = $dataResetRecoveryPassed
            applicationRemovalPreservedPlayerData = $applicationRemovalPreservedPlayerData
            completeSupportedSaveFixtureMatrix = $true
            remainingGates = @(
                "selected-channel-installer-lifecycle",
                "cross-version-binary-rollback"
            )
        }
        $lifecycleEvidencePath = Join-Path `
            $resolvedEvidenceDirectory `
            "candidate_install_lifecycle.json"
        [System.IO.File]::WriteAllText(
            $lifecycleEvidencePath,
            ($lifecycleEvidence | ConvertTo-Json -Depth 8) + "`n",
            [Text.UTF8Encoding]::new($false))
    }

    $readOnlyEvidence = [ordered]@{
        schemaVersion = 1
        kind = "artifact-read-only-install-v1"
        passed = $evidencePassed
        platformId = $platformId
        installShape = if ($IsMacOS) { "expanded-app-bundle" } else { "portable-folder" }
        sourceRevision = $sourceRevision
        smokeStateHash = $smokeStateHash
        writeProbeRejected = $readOnlyWriteRejected
        installFileCount = $installSnapshotAfter.FileCount
        beforeSha256 = $installSnapshotBefore.AggregateSha256
        afterSha256 = $installSnapshotAfter.AggregateSha256
        installUnchanged = $installUnchanged
        userDataOutsideInstall = $userDataOutsideInstall
        logOutsideInstall = $logOutsideInstall
        evidenceOutsideInstall = $evidenceOutsideInstall
        installPathQualified = $installPathQualified
        userDataPathQualified = $userDataPathQualified
        logPathQualified = $logPathQualified
        freshProfile = $true
    }
    $readOnlyEvidencePath = Join-Path `
        $resolvedEvidenceDirectory `
        "artifact_read_only_install.json"
    [System.IO.File]::WriteAllText(
        $readOnlyEvidencePath,
        ($readOnlyEvidence | ConvertTo-Json -Depth 6) + "`n",
        [Text.UTF8Encoding]::new($false))

    if (-not $evidencePassed) {
        throw "Exported player did not satisfy the read-only install contract."
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

    if ($installPermissionToken -and $installRoot -and (Test-Path -LiteralPath $installRoot)) {
        Restore-InstallPermissions `
            -InstallRoot $installRoot `
            -PermissionToken $installPermissionToken
        $installPermissionToken = $null
    }

    if ($writeProbePath -and (Test-Path -LiteralPath $writeProbePath -PathType Leaf)) {
        Remove-Item -LiteralPath $writeProbePath -Force
    }

    foreach ($temporaryDirectory in @(
        $smokeRoot,
        $smokeUserDataRoot,
        $launchProbeRoot,
        $lifecycleRoot
    )) {
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

$artifactManifestPath = Join-Path $resolvedOutputDirectory "artifact-manifest.json"
$signingPolicyPath = Join-Path $repositoryRoot "config/release_signing_policy.json"
$signingReadinessPath = Join-Path $resolvedEvidenceDirectory "release_signing_readiness.json"
$localDotnetCandidates = @(
    (Join-Path $repositoryRoot ".dotnet/dotnet.exe"),
    (Join-Path $repositoryRoot ".dotnet/dotnet")
)
$dotnetExecutable = $localDotnetCandidates |
    Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } |
    Select-Object -First 1
if (-not $dotnetExecutable) {
    $dotnetExecutable = (Get-Command dotnet -ErrorAction Stop).Source
}
$validatorProject = Join-Path `
    $repositoryRoot `
    "native/tools/ValidateArtifactManifest/ValidateArtifactManifest.csproj"
& $dotnetExecutable @(
    "run",
    "--project", $validatorProject,
    "--configuration", "Release",
    "--no-restore",
    "--",
    $artifactManifestPath,
    "--signing-policy", $signingPolicyPath,
    "--readiness-output", $signingReadinessPath,
    "--package-qualification", $qualificationPackageDirectory,
    "--product-version", $productVersion
)
if ($LASTEXITCODE -ne 0) {
    throw "Signing-readiness generation failed for $platformId."
}
$signingReadiness = Get-Content -LiteralPath $signingReadinessPath -Raw | ConvertFrom-Json
if (
    $signingReadiness.kind -ne "release-signing-readiness-v1" -or
    -not $signingReadiness.passed -or
    $signingReadiness.signingState -ne "unsigned-input" -or
    [bool]$signingReadiness.ordinaryCiCredentialAccess -or
    [bool]$signingReadiness.signingMaterialAllowedInRepository -or
    [bool]$signingReadiness.signingMaterialAllowedInArtifacts
) {
    throw "Signing-readiness evidence weakened the unsigned qualification boundary."
}
if ($BuildMode -eq "Debug" -and [bool]$signingReadiness.promotionEligible) {
    throw "Debug artifact was incorrectly marked eligible for release promotion."
}
$releaseOutputPlanPath = Join-Path $qualificationPackageDirectory "release_output_plan.json"
$releaseOutputChecksumsPath = Join-Path $qualificationPackageDirectory "SHA256SUMS"
$releaseOutputPlan = Get-Content -LiteralPath $releaseOutputPlanPath -Raw | ConvertFrom-Json
if (
    $releaseOutputPlan.kind -ne "release-output-plan-v1" -or
    -not $releaseOutputPlan.passed -or
    -not $releaseOutputPlan.qualificationOnly -or
    [bool]$releaseOutputPlan.publicationEligible -or
    -not $releaseOutputPlan.optionalPackOutputSeparate -or
    [bool]$releaseOutputPlan.baseGameIncludesOptionalPacks -or
    -not $releaseOutputPlan.playerDataExcluded -or
    -not $releaseOutputPlan.uninstallPreservesPlayerData -or
    -not $releaseOutputPlan.deterministicRepeatMatched -or
    [long]$releaseOutputPlan.packageBytes -le 0 -or
    [string]$releaseOutputPlan.packageSha256 -notmatch '^[0-9a-f]{64}$' -or
    -not (Test-Path -LiteralPath $releaseOutputChecksumsPath -PathType Leaf)
) {
    throw "Release output plan did not preserve the qualification-only channel boundary."
}
$retainedReleaseOutputPlanPath = Join-Path `
    $resolvedEvidenceDirectory `
    "release_output_plan.json"
Copy-Item `
    -LiteralPath $releaseOutputPlanPath `
    -Destination $retainedReleaseOutputPlanPath `
    -Force

if ($env:GITHUB_OUTPUT) {
    "artifact-path=$artifactPath" | Out-File -LiteralPath $env:GITHUB_OUTPUT -Encoding utf8 -Append
    "artifact-root=$resolvedOutputDirectory" | Out-File -LiteralPath $env:GITHUB_OUTPUT -Encoding utf8 -Append
    "read-only-evidence=$readOnlyEvidencePath" | Out-File -LiteralPath $env:GITHUB_OUTPUT -Encoding utf8 -Append
    "signing-readiness=$signingReadinessPath" | Out-File -LiteralPath $env:GITHUB_OUTPUT -Encoding utf8 -Append
    "package-root=$qualificationPackageDirectory" | Out-File -LiteralPath $env:GITHUB_OUTPUT -Encoding utf8 -Append
    "release-output-plan=$retainedReleaseOutputPlanPath" | Out-File -LiteralPath $env:GITHUB_OUTPUT -Encoding utf8 -Append
}

Write-Output "NativeArtifact=$artifactPath"
Write-Output "NativeArtifactRoot=$resolvedOutputDirectory"
Write-Output "ReadOnlyInstallEvidence=$readOnlyEvidencePath"
Write-Output "ReleaseSigningReadiness=$signingReadinessPath"
Write-Output "QualificationPackageRoot=$qualificationPackageDirectory"
Write-Output "ReleaseOutputPlan=$retainedReleaseOutputPlanPath"
Write-Output "Native export qualification passed for $platformId."
