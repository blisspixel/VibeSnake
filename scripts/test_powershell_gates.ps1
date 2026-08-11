[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$GodotExecutable,

    [Parameter()]
    [string]$GodotArchivePath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$toolchain = Get-Content -LiteralPath (Join-Path $repositoryRoot "native/toolchain.json") -Raw | ConvertFrom-Json
$spoofedIdentity = "$([string]$toolchain.godot.version).stable.mono.official.$([string]$toolchain.godot.commit)"

$verificationArguments = @{ GodotExecutable = $GodotExecutable }
if ($GodotArchivePath) {
    $verificationArguments.GodotArchivePath = $GodotArchivePath
}
& (Join-Path $PSScriptRoot "assert_godot_toolchain.ps1") @verificationArguments | Out-Null

$temporaryRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("vibesnake-powershell-gates-{0}" -f [Guid]::NewGuid())
$comparison = if ($IsWindows) { [StringComparison]::OrdinalIgnoreCase } else { [StringComparison]::Ordinal }
try {
    New-Item -ItemType Directory -Path $temporaryRoot | Out-Null
    if ($IsWindows) {
        $fakeExecutable = Join-Path $temporaryRoot "fake-godot.cmd"
        [System.IO.File]::WriteAllText(
            $fakeExecutable,
            "@echo off`r`necho $spoofedIdentity`r`n",
            [Text.UTF8Encoding]::new($false)
        )
    } else {
        $fakeExecutable = Join-Path $temporaryRoot "fake-godot"
        [System.IO.File]::WriteAllText(
            $fakeExecutable,
            "#!/bin/sh`nprintf '%s\n' '$spoofedIdentity'`n",
            [Text.UTF8Encoding]::new($false)
        )
        & chmod +x $fakeExecutable
        if ($LASTEXITCODE -ne 0) {
            throw "Could not make the fake Godot regression fixture executable."
        }
    }

    $fakeArguments = @{ GodotExecutable = $fakeExecutable }
    if ($GodotArchivePath) {
        $fakeArguments.GodotArchivePath = $GodotArchivePath
    }
    try {
        & (Join-Path $PSScriptRoot "assert_godot_toolchain.ps1") @fakeArguments | Out-Null
        throw "Godot verification accepted an executable that only spoofed the pinned version text."
    } catch {
        if ($_.Exception.Message -notlike "Godot executable bytes do not match*") {
            throw
        }
    }

    . (Join-Path $PSScriptRoot "native_artifact_policy.ps1")
    $allowedLauncher = Assert-NativeArtifactPath -RelativePath "VibeSnake.sh"
    if ($allowedLauncher -cne "VibeSnake.sh") {
        throw "Linux product launcher shell must remain allowlisted: $allowedLauncher"
    }
    try {
        Assert-NativeArtifactPath -RelativePath "tools/setup.sh" | Out-Null
        throw "Artifact policy accepted a non-product shell script: tools/setup.sh"
    } catch {
        if ($_.Exception.Message -notlike "Artifact contains prohibited content:*") {
            throw
        }
    }

    $prohibitedPaths = @(
        "python314.dll",
        "libpython3.14.so.1.0",
        "Frameworks/libpython3.14.dylib",
        "Python.framework/Versions/3.14/Python",
        ".env.local",
        "config/.env.production",
        "signing/windows-certificate.p12",
        "signing/AuthKey_RELEASE.p8",
        "signing/release.keystore"
    )
    foreach ($prohibitedPath in $prohibitedPaths) {
        try {
            Assert-NativeArtifactPath -RelativePath $prohibitedPath | Out-Null
            throw "Artifact policy accepted prohibited content: $prohibitedPath"
        } catch {
            if ($_.Exception.Message -notlike "Artifact contains prohibited content:*") {
                throw
            }
        }
    }

    $invalidPaths = @(
        "/etc/passwd",
        "C:\secrets.txt",
        "C:secrets.txt",
        "\\server\share\secret.txt",
        ".",
        "bundle/..",
        "bundle/./file.txt",
        "bundle//file.txt"
    )
    foreach ($invalidPath in $invalidPaths) {
        try {
            Assert-NativeArtifactPath -RelativePath $invalidPath | Out-Null
            throw "Artifact policy accepted an invalid path: $invalidPath"
        } catch {
            if ($_.Exception.Message -notlike "Artifact contains an invalid path:*") {
                throw
            }
        }
    }

    . (Join-Path $PSScriptRoot "platform_path_policy.ps1")
    $absoluteFallback = Join-Path $temporaryRoot "data-fallback"
    $resolvedRelativeXdg = Get-AbsoluteEnvironmentPathOrDefault `
        -ConfiguredPath "relative/data" `
        -DefaultPath $absoluteFallback
    if ($resolvedRelativeXdg -cne [System.IO.Path]::GetFullPath($absoluteFallback)) {
        throw "Relative XDG data paths must resolve to the absolute platform fallback."
    }

    $inventoryPath = Join-Path $repositoryRoot "config/content_inventory.json"
    Assert-ArtifactRespectsContentInventory `
        -InventoryPath $inventoryPath `
        -ArtifactRelativePaths @("VibeSnake.exe", "VibeSnake.pck", "data_VibeSnake.Game_windows_x86_64/VibeSnake.Rules.dll")
    try {
        Assert-ArtifactRespectsContentInventory `
            -InventoryPath $inventoryPath `
            -ArtifactRelativePaths @("audio/radio/ambient_graceful_laminar.mp3")
        throw "Inventory gate accepted a non-exportEligible packaged asset path."
    } catch {
        if ($_.Exception.Message -notlike "Artifact contains inventory asset that is not exportEligible:*") {
            throw
        }
    }

    $ciWorkflowPath = Join-Path $repositoryRoot ".github/workflows/ci.yml"
    $ciWorkflow = Get-Content -LiteralPath $ciWorkflowPath -Raw
    $nativeTestScript = Get-Content -LiteralPath (Join-Path $repositoryRoot "scripts/test_native.ps1") -Raw
    $nativeCoverageScript = Get-Content -LiteralPath (Join-Path $repositoryRoot "scripts/test_native_coverage.ps1") -Raw
    $nativeExportScript = Get-Content -LiteralPath (Join-Path $repositoryRoot "scripts/test_native_export.ps1") -Raw
    $gameMainScript = Get-Content -LiteralPath (Join-Path $repositoryRoot "game/scripts/Main.cs") -Raw
    foreach ($coverageConsumer in @($ciWorkflow, $nativeTestScript)) {
        if (-not $coverageConsumer.Contains("test_native_coverage.ps1", [StringComparison]::Ordinal)) {
            throw "Native coverage consumer does not invoke the shared coverage gate."
        }
    }
    foreach ($requiredCoverageFragment in @(
        "-p:Threshold=90%2c85",
        "-p:ThresholdType=line%2cbranch",
        "-p:ThresholdStat=minimum",
        'for ($attempt = 1; $attempt -le 2; $attempt++)',
        "Native tests failed; a coverage-report retry cannot hide a test failure.",
        "build-server",
        "Assert-NativeCoverageReport"
    )) {
        if (-not $nativeCoverageScript.Contains($requiredCoverageFragment, [StringComparison]::Ordinal)) {
            throw "Native coverage gate is missing: $requiredCoverageFragment"
        }
    }
    foreach ($requiredLocalizationFragment in @(
        "ShellLocalization.All.Count == 516",
        "entry.Parameters.Count > 0) == 73"
    )) {
        if (-not $gameMainScript.Contains($requiredLocalizationFragment, [StringComparison]::Ordinal)) {
            throw "Godot localization evidence is missing catalog count: $requiredLocalizationFragment"
        }
    }
    foreach ($requiredLocalizationFragment in @(
        '($localizationEvidence.stringCount -ne 516)',
        '($localizationEvidence.parameterizedStringCount -ne 73)'
    )) {
        if (-not $nativeTestScript.Contains($requiredLocalizationFragment, [StringComparison]::Ordinal)) {
            throw "Native localization gate is missing catalog count: $requiredLocalizationFragment"
        }
    }
    foreach ($requiredPresentationBudgetFragment in @(
        '($presentationFrameEvidence.sampleCount -lt 40)',
        '($presentationFrameEvidence.p95Milliseconds -gt 60.0)',
        '($presentationFrameEvidence.maxMilliseconds -gt 100.0)'
    )) {
        if (-not $nativeTestScript.Contains(
            $requiredPresentationBudgetFragment,
            [StringComparison]::Ordinal)) {
            throw "Native presentation gate drifted from the Godot bare-loop budget: $requiredPresentationBudgetFragment"
        }
    }
    if ($nativeTestScript.Contains(
        '($presentationFrameEvidence.p95Milliseconds -gt 50.0)',
        [StringComparison]::Ordinal)) {
        throw "Native presentation gate retained the obsolete 50 ms p95 ceiling."
    }
    if (-not $nativeExportScript.Contains(
        '"effective_schema=7 code=Success"',
        [StringComparison]::Ordinal)) {
        throw "Candidate repair launch must require the current preferences schema."
    }
    if ($ciWorkflow -match '\$\{\{\s*secrets\.') {
        throw "Ordinary CI must not reference signing or release secrets."
    }
    $godotJobStart = $ciWorkflow.IndexOf("  godot-smoke:", [StringComparison]::Ordinal)
    $releaseMatrixJobStart = $ciWorkflow.IndexOf(
        "  release-matrix:",
        [StringComparison]::Ordinal)
    $attestationJobStart = $ciWorkflow.IndexOf(
        "  attest-qualified-manifests:",
        [StringComparison]::Ordinal)
    $radioPackJobStart = $ciWorkflow.IndexOf(
        "  package-approved-radio-content:",
        [StringComparison]::Ordinal)
    $alphaPublishJobStart = $ciWorkflow.IndexOf(
        "  publish-native-alpha:",
        [StringComparison]::Ordinal)
    if (
        $godotJobStart -lt 0 -or
        $releaseMatrixJobStart -le $godotJobStart -or
        $attestationJobStart -le $releaseMatrixJobStart -or
        $radioPackJobStart -le $attestationJobStart -or
        $alphaPublishJobStart -le $radioPackJobStart
    ) {
        throw "CI must keep artifact smoke, aggregate matrix, and provenance in ordered separate jobs."
    }
    $godotJob = $ciWorkflow.Substring(
        $godotJobStart,
        $releaseMatrixJobStart - $godotJobStart)
    if ($godotJob.Contains("id-token: write", [StringComparison]::Ordinal)) {
        throw "Ordinary Godot smoke must not receive provenance identity permissions."
    }
    foreach ($requiredGodotFragment in @(
        '$candidateLaunchCount = if ($env:VIBESNAKE_QUALIFICATION_BUILD_MODE -eq "Release") { 100 } else { 0 }',
        "CandidateLaunchCount = `$candidateLaunchCount",
        '$exportArguments["CandidateLifecycle"] = $true',
        "./scripts/test_native_export.ps1 @exportArguments"
    )) {
        if (-not $godotJob.Contains($requiredGodotFragment, [StringComparison]::Ordinal)) {
            throw "Native candidate launch campaign is missing: $requiredGodotFragment"
        }
    }
    $releaseMatrixJob = $ciWorkflow.Substring(
        $releaseMatrixJobStart,
        $attestationJobStart - $releaseMatrixJobStart)
    foreach ($requiredMatrixFragment in @(
        "needs: godot-smoke",
        "pattern: vibesnake-*-qualification-evidence",
        "pattern: vibesnake-*-manifest",
        "python scripts/check_release_matrix.py release-matrix",
        '--expected-revision "${{ github.sha }}"',
        "name: vibesnake-release-matrix"
    )) {
        if (-not $releaseMatrixJob.Contains($requiredMatrixFragment, [StringComparison]::Ordinal)) {
            throw "Aggregate release matrix job is missing: $requiredMatrixFragment"
        }
    }
    if ($releaseMatrixJob.Contains("id-token: write", [StringComparison]::Ordinal)) {
        throw "Aggregate release matrix must not receive provenance identity permissions."
    }
    $attestationJob = $ciWorkflow.Substring(
        $attestationJobStart,
        $radioPackJobStart - $attestationJobStart)
    foreach ($requiredAttestationFragment in @(
        "needs: [godot-smoke, release-matrix]",
        "id-token: write",
        "attestations: write",
        "artifact-metadata: write",
        "actions/attest@1e69f48acb82d1966a394da916b4c1698aa569d6",
        "github.event_name == 'workflow_dispatch' || startsWith(github.ref, 'refs/tags/')"
    )) {
        if (-not $attestationJob.Contains($requiredAttestationFragment, [StringComparison]::Ordinal)) {
            throw "Separated provenance job is missing: $requiredAttestationFragment"
        }
    }
    $radioPackJob = $ciWorkflow.Substring(
        $radioPackJobStart,
        $alphaPublishJobStart - $radioPackJobStart)
    foreach ($requiredRadioPackFragment in @(
        "startsWith(github.ref, 'refs/tags/v') && contains(github.ref_name, '-alpha.')",
        "needs: quality",
        "Expected exactly one approved alpha radio manifest",
        "python scripts/assemble_radio_pack.py",
        "name: vibesnake-approved-radio-pack"
    )) {
        if (-not $radioPackJob.Contains($requiredRadioPackFragment, [StringComparison]::Ordinal)) {
            throw "Approved alpha radio-pack job is missing: $requiredRadioPackFragment"
        }
    }
    $alphaPublishJob = $ciWorkflow.Substring($alphaPublishJobStart)
    foreach ($requiredAlphaPublishFragment in @(
        "startsWith(github.ref, 'refs/tags/v') && contains(github.ref_name, '-alpha.')",
        "needs: [release-matrix, attest-qualified-manifests, package-approved-radio-content]",
        "contents: write",
        "python scripts/content_inventory.py --check --release-ready",
        "python scripts/assemble_unsigned_preview.py preview-channel",
        "name: vibesnake-approved-radio-pack",
        "--radio-pack-root preview-radio",
        '--tag "${{ github.ref_name }}"',
        '--expected-revision "${{ github.sha }}"',
        "gh release create",
        "--verify-tag",
        "--prerelease",
        "--latest=false"
    )) {
        if (-not $alphaPublishJob.Contains($requiredAlphaPublishFragment, [StringComparison]::Ordinal)) {
            throw "Native alpha publication job is missing: $requiredAlphaPublishFragment"
        }
    }

    $playerBuildWorkflow = Get-Content -LiteralPath (
        Join-Path $repositoryRoot ".github/workflows/player-build.yml") -Raw
    if ($playerBuildWorkflow.Contains('tags: ["v*"]', [StringComparison]::Ordinal)) {
        throw "Source player workflow must not own versioned tag publication."
    }
    if ($playerBuildWorkflow.Contains(
        "Publish GitHub Release for tags",
        [StringComparison]::Ordinal)) {
        throw "Source player workflow must not publish versioned releases."
    }

    $caseCount = 24 + $prohibitedPaths.Count + $invalidPaths.Count
    Write-Output "PowerShell qualification regression checks passed: cases=$caseCount."
} finally {
    if (Test-Path -LiteralPath $temporaryRoot) {
        $temporaryPrefix = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath()).TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
        $resolvedTemporaryRoot = [System.IO.Path]::GetFullPath($temporaryRoot)
        if (-not $resolvedTemporaryRoot.StartsWith($temporaryPrefix, $comparison)) {
            throw "Refusing to clean an unexpected PowerShell gate fixture directory."
        }
        [System.IO.Directory]::Delete($resolvedTemporaryRoot, $true)
    }
}
