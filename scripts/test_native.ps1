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
    & (Join-Path $PSScriptRoot "test_native_coverage.ps1")

    $balanceEvidencePath = Join-Path $repositoryRoot "TestResults/native/balance_laboratory.json"
    if (-not (Test-Path -LiteralPath $balanceEvidencePath -PathType Leaf)) {
        throw "Native tests did not write balance_laboratory.json evidence."
    }

    $balanceEvidence = Get-Content -LiteralPath $balanceEvidencePath -Raw | ConvertFrom-Json
    if (($balanceEvidence.kind -ne "balance-laboratory-v1") -or
        ($balanceEvidence.schemaVersion -ne 1) -or
        (-not $balanceEvidence.passed) -or
        ($balanceEvidence.configHashAlgorithm -ne "sha256-canonical-runconfig-v3") -or
        ($balanceEvidence.stateHashAlgorithm -ne "fnv1a64-canonical-json-v4") -or
        ($balanceEvidence.seedCorpusKind -ne "vibesnake-qa-seed-corpora-v1") -or
        ($balanceEvidence.seedCorpusSchemaVersion -ne 1) -or
        ($balanceEvidence.seedCorpusSha256 -notmatch "^[0-9a-f]{64}$") -or
        ($balanceEvidence.maximumStepsPerRun -ne 384) -or
        ($balanceEvidence.runCount -ne 324) -or
        ($balanceEvidence.comparedStepCount -le 0)) {
        throw "Balance laboratory evidence failed its schema, provenance, or matrix contract."
    }
    if ((@($balanceEvidence.variants) -join ",") -ne
        "classic,vibe-dda-on,vibe-dda-off") {
        throw "Balance laboratory mode variants drifted from the qualified matrix."
    }
    if ((@($balanceEvidence.seedCorpora.classification) -join ",") -ne
        "reviewed-fixed,exploratory,previous-failure" -or
        (@($balanceEvidence.seedCorpora).Count -ne 3) -or
        @($balanceEvidence.seedCorpora | Where-Object {
            (-not $_.reviewed) -or (@($_.seeds).Count -ne 4)
        }).Count -ne 0) {
        throw "Balance laboratory seed corpora are incomplete or unreviewed."
    }
    if ((@($balanceEvidence.policies.id) -join ",") -ne
        "safe-survivor-v1,greedy-food-v1,risk-seeking-v1,power-hunting-v1,boundary-walker-v1,idle-v1,input-chaos-v1,personality-seeded-v1,replay-ghost-v1") {
        throw "Balance laboratory policy catalog drifted from the nine required policies."
    }
    if ((@($balanceEvidence.scenarios.id) -join ",") -ne
        "open-board-routing,long-body-trap,starvation-pressure,power-overlap,last-stand-recovery,detached-obstacle,near-miss-scoring,combo-escalation,full-grid-resolution,restart-leaks" -or
        @($balanceEvidence.scenarios | Where-Object { -not $_.passed }).Count -ne 0) {
        throw "Balance laboratory hostile-scenario matrix is incomplete or failed."
    }
    if ((@($balanceEvidence.distributions).Count -ne 27) -or
        @($balanceEvidence.distributions | Where-Object { $_.sampleCount -ne 12 }).Count -ne 0) {
        throw "Balance laboratory distributions do not cover all policy and mode pairs."
    }
    if ((@($balanceEvidence.runSummaries).Count -ne 324) -or
        @($balanceEvidence.runSummaries | Where-Object {
            $_.finalStateHash -notmatch "^[0-9a-f]{16}$"
        }).Count -ne 0) {
        throw "Balance laboratory run summaries lack complete deterministic state hashes."
    }
    if ((-not $balanceEvidence.divergence.passed) -or
        ($balanceEvidence.divergence.comparedRunCount -ne 324) -or
        ($balanceEvidence.divergence.comparedStepCount -ne $balanceEvidence.comparedStepCount) -or
        ($balanceEvidence.divergence.PSObject.Properties.Name -contains "firstDivergence")) {
        throw "Balance laboratory reported a deterministic first divergence."
    }

    $balanceOutliers = @($balanceEvidence.outlierReplays)
    if ($balanceOutliers.Count -lt 6 -or
        @($balanceOutliers | Where-Object {
            (-not $_.verified) -or
            ($_.finalStateHash -notmatch "^[0-9a-f]{16}$") -or
            ($_.sha256 -notmatch "^[0-9a-f]{64}$")
        }).Count -ne 0) {
        throw "Balance laboratory outlier replay metadata is incomplete or unverified."
    }
    $pathComparison = if ($IsWindows) {
        [StringComparison]::OrdinalIgnoreCase
    } else {
        [StringComparison]::Ordinal
    }
    $outlierRoot = [System.IO.Path]::GetFullPath(
        (Join-Path $repositoryRoot "TestResults/native/balance_lab/outliers"))
    $outlierPrefix = $outlierRoot.TrimEnd([System.IO.Path]::DirectorySeparatorChar) +
        [System.IO.Path]::DirectorySeparatorChar
    foreach ($outlier in $balanceOutliers) {
        $outlierPath = [System.IO.Path]::GetFullPath(
            (Join-Path $repositoryRoot $outlier.relativePath))
        if (-not $outlierPath.StartsWith($outlierPrefix, $pathComparison) -or
            -not (Test-Path -LiteralPath $outlierPath -PathType Leaf)) {
            throw "Balance laboratory outlier replay path is missing or outside its evidence root."
        }
        $actualOutlierHash = (Get-FileHash -LiteralPath $outlierPath -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($actualOutlierHash -ne $outlier.sha256) {
            throw "Balance laboratory outlier replay hash mismatch: $($outlier.relativePath)"
        }
    }

    $aiLeagueEvidencePath = Join-Path $repositoryRoot "TestResults/native/ai_league.json"
    if (-not (Test-Path -LiteralPath $aiLeagueEvidencePath -PathType Leaf)) {
        throw "Native tests did not write ai_league.json evidence."
    }

    $aiLeagueEvidence = Get-Content -LiteralPath $aiLeagueEvidencePath -Raw | ConvertFrom-Json
    if (($aiLeagueEvidence.kind -ne "native-ai-league-v1") -or
        ($aiLeagueEvidence.schemaVersion -ne 1) -or
        (-not $aiLeagueEvidence.passed) -or
        ($aiLeagueEvidence.controllerAlgorithm -ne "native-personality-controller-v2") -or
        ($aiLeagueEvidence.randomAlgorithm -ne "pcg-xsh-rr-32-v1") -or
        ($aiLeagueEvidence.seedCorpusKind -ne "vibesnake-qa-seed-corpora-v1") -or
        ($aiLeagueEvidence.seedCorpusSchemaVersion -ne 1) -or
        ($aiLeagueEvidence.seedCorpusSha256 -notmatch "^[0-9a-f]{64}$") -or
        ($aiLeagueEvidence.maximumStepsPerRun -ne 900) -or
        ($aiLeagueEvidence.personalityCount -ne 10) -or
        ($aiLeagueEvidence.runCount -ne 120) -or
        ($aiLeagueEvidence.comparedStepCount -le 0)) {
        throw "AI league evidence failed its schema, provenance, or complete-matrix contract."
    }
    if ((@($aiLeagueEvidence.seedCorpora.classification) -join ",") -ne
        "reviewed-fixed,exploratory,previous-failure" -or
        @($aiLeagueEvidence.seedCorpora | Where-Object { -not $_.reviewed }).Count -ne 0) {
        throw "AI league evidence must use the complete reviewed QA seed corpus."
    }
    if ((@($aiLeagueEvidence.metrics.id) -join ",") -ne
        "score,survival,food-efficiency,power-preference,risk-exposure,dead-end-rate,route-efficiency") {
        throw "AI league evidence lost a required roadmap metric."
    }
    if ((@($aiLeagueEvidence.personalities.id) -join ",") -ne
        "speed_demon,coward,greedy,power_hunter,drunk,optimal,yolo,balanced,wall_hugger,zen_master") {
        throw "AI league evidence lost or reordered a shipped built-in personality."
    }
    if ((@($aiLeagueEvidence.distributions).Count -ne 10) -or
        @($aiLeagueEvidence.distributions | Where-Object {
            ($_.sampleCount -ne 12) -or
            ($_.rulesVersion -ne $aiLeagueEvidence.rulesVersion) -or
            ($_.powerPreferenceBasisPoints -lt 0) -or
            ($_.powerPreferenceBasisPoints -gt 10000) -or
            ($_.riskExposureBasisPoints -lt 0) -or
            ($_.riskExposureBasisPoints -gt 10000) -or
            ($_.deadEndBasisPoints -lt 0) -or
            ($_.deadEndBasisPoints -gt 10000) -or
            ($_.routeEfficiencyBasisPoints -lt 0) -or
            ($_.routeEfficiencyBasisPoints -gt 10000)
        }).Count -ne 0) {
        throw "AI league distributions lost rules-version grouping, sample coverage, or bounded rates."
    }
    if ((@($aiLeagueEvidence.traitSensitivities).Count -ne 60) -or
        @($aiLeagueEvidence.traitSensitivities | Where-Object {
            ($_.observedDecisionCount -le 0) -or
            ($_.changedDecisionBasisPoints -lt 0) -or
            ($_.changedDecisionBasisPoints -gt 10000) -or
            ($_.interventionValue -notin @(0, 100))
        }).Count -ne 0 -or
        (@($aiLeagueEvidence.inertTraits).Count -ne 0) -or
        (@($aiLeagueEvidence.inertTraits).Count -ne
            @($aiLeagueEvidence.traitSensitivities | Where-Object {
                -not $_.materiallyAffectedDecisions
            }).Count)) {
        throw "AI league trait interventions did not account for every trait or inert result."
    }
    if (($aiLeagueEvidence.leaderboardIsolation.runKindId -ne "ai") -or
        ($aiLeagueEvidence.leaderboardIsolation.seedCategoryId -ne "ai-simulation") -or
        ($aiLeagueEvidence.leaderboardIsolation.displayCategoryId -ne "ai") -or
        $aiLeagueEvidence.leaderboardIsolation.competitiveEligible -or
        $aiLeagueEvidence.leaderboardIsolation.writesHumanScoreStorage) {
        throw "AI league evidence is not isolated from human competitive score storage."
    }
    if ((@($aiLeagueEvidence.runs).Count -ne 120) -or
        @($aiLeagueEvidence.runs | Where-Object {
            ($_.runKindId -ne "ai") -or
            ($_.seedCategoryId -ne "ai-simulation") -or
            ($_.displayCategoryId -ne "ai") -or
            $_.competitiveEligible -or
            ($_.rulesScoreCategoryId -ne "vibe-standard-v1-dda-on") -or
            ($_.decisionCount -ne $_.steps) -or
            ($_.decisionTraceSha256 -notmatch "^[0-9a-f]{64}$") -or
            ($_.finalStateHash -notmatch "^[0-9a-f]{16}$")
        }).Count -ne 0) {
        throw "AI league run evidence lost identity, determinism, or trace completeness."
    }

    $aiPersonalityEvidencePath =
        Join-Path $repositoryRoot "TestResults/native/ai_personalities.json"
    if (-not (Test-Path -LiteralPath $aiPersonalityEvidencePath -PathType Leaf)) {
        throw "Native tests did not write ai_personalities.json evidence."
    }

    $aiPersonalityEvidence =
        Get-Content -LiteralPath $aiPersonalityEvidencePath -Raw | ConvertFrom-Json
    if (($aiPersonalityEvidence.kind -ne "ai-personality-qualification-v1") -or
        ($aiPersonalityEvidence.schemaVersion -ne 1) -or
        (-not $aiPersonalityEvidence.passed) -or
        ($aiPersonalityEvidence.controllerAlgorithm -ne "native-personality-controller-v2") -or
        ($aiPersonalityEvidence.customSchemaVersion -ne 1) -or
        ($aiPersonalityEvidence.builtInCount -ne 10) -or
        ($aiPersonalityEvidence.behaviorClaimCount -ne 10) -or
        ($aiPersonalityEvidence.traitSensitivityCount -ne 60) -or
        ($aiPersonalityEvidence.inertTraitCount -ne 0) -or
        ($aiPersonalityEvidence.comparedStepCount -le 0) -or
        (-not $aiPersonalityEvidence.compatibilityIdsRetained) -or
        (-not $aiPersonalityEvidence.greedConsumed) -or
        (-not $aiPersonalityEvidence.allTraitsMaterial)) {
        throw "AI personality evidence failed its truthfulness or shared-schema contract."
    }
    if ((@($aiPersonalityEvidence.displayNames) -join ",") -ne
        "Redline,Shelter Coil,Crownchaser,Mutagenist,Noise Coil,The Proof,Edge Prophet,Meanline,Rimkeeper,Stillwater") {
        throw "AI personality display identities drifted from the reviewed truthfulness set."
    }
    if ((@($aiPersonalityEvidence.behaviorClaims).Count -ne 10) -or
        @($aiPersonalityEvidence.behaviorClaims | Where-Object {
            (-not $_.passed) -or
            ($_.observedValue -lt $_.inclusiveMinimum) -or
            ($_.observedValue -gt $_.inclusiveMaximum)
        }).Count -ne 0) {
        throw "An AI personality no longer satisfies its declared measured behavior."
    }
    if ((@($aiPersonalityEvidence.customValidation).Count -ne 6) -or
        @($aiPersonalityEvidence.customValidation | Where-Object {
            (-not $_.passed) -or
            (-not $_.filenameSpecific) -or
            ($_.actualCode -ne $_.expectedCode)
        }).Count -ne 0) {
        throw "Custom personality validation lost a strict or filename-specific case."
    }
    if (($aiPersonalityEvidence.overlay.policyId -ne
            "native-personality-controller-v2/balanced") -or
        ($aiPersonalityEvidence.overlay.recentDecisionCount -ne 5) -or
        ($aiPersonalityEvidence.overlay.builtInStatus -ne
            "BUILT-IN / LEAGUE-QUALIFIED") -or
        ($aiPersonalityEvidence.overlay.customStatus -ne "CUSTOM / UNOFFICIAL") -or
        $aiPersonalityEvidence.overlay.customOfficialLeagueQualified -or
        (-not $aiPersonalityEvidence.overlay.passed)) {
        throw "AI spectator overlay lost policy, decision history, or content-status truth."
    }

    $baselineEvidencePath = Join-Path $repositoryRoot "TestResults/native/balance_baselines.json"
    if (-not (Test-Path -LiteralPath $baselineEvidencePath -PathType Leaf)) {
        throw "Native tests did not write balance_baselines.json evidence."
    }

    $baselineEvidence = Get-Content -LiteralPath $baselineEvidencePath -Raw | ConvertFrom-Json
    if (($baselineEvidence.kind -ne "observed-balance-baseline-evidence-v1") -or
        ($baselineEvidence.schemaVersion -ne 1) -or
        (-not $baselineEvidence.passed) -or
        ($baselineEvidence.classification -ne "ai-simulation-observation") -or
        (-not $baselineEvidence.aiSimulationOnly) -or
        $baselineEvidence.humanTargetRangesEstablished -or
        (@($baselineEvidence.humanTargetRanges).Count -ne 0) -or
        ($baselineEvidence.configHashAlgorithm -ne "sha256-canonical-runconfig-v3") -or
        ($baselineEvidence.stateHashAlgorithm -ne "fnv1a64-canonical-json-v4") -or
        ($baselineEvidence.seedCorpusKind -ne "vibesnake-balance-baseline-seeds-v1") -or
        ($baselineEvidence.seedCorpusSha256 -notmatch "^[0-9a-f]{64}$") -or
        (-not $baselineEvidence.seedCorpusReviewed) -or
        ($baselineEvidence.seedCount -ne 100) -or
        ($baselineEvidence.maximumStepsPerRun -ne 900) -or
        ($baselineEvidence.variantCount -ne 3) -or
        ($baselineEvidence.policyCount -ne 9) -or
        ($baselineEvidence.referenceAiPolicyCount -ne 6) -or
        ($baselineEvidence.sampleCountPerPair -ne 100) -or
        ($baselineEvidence.runCount -ne 2700) -or
        (-not $baselineEvidence.baselineMatched) -or
        ($baselineEvidence.baselineDocumentSha256 -notmatch "^[0-9a-f]{64}$") -or
        ($baselineEvidence.observedDistributionSha256 -notmatch "^[0-9a-f]{64}$")) {
        throw "Observed balance baseline evidence failed its separation or matrix contract."
    }
    if ((@($baselineEvidence.variants.id) -join ",") -ne
        "classic,vibe-dda-on,vibe-dda-off" -or
        (@($baselineEvidence.variants.modeContractId) -join ",") -ne
        "classic@1,vibe@1,vibe@1" -or
        (@($baselineEvidence.variants.scoreCategoryId) -join ",") -ne
        "classic-standard-v1,vibe-standard-v1-dda-on,vibe-standard-v1-dda-off" -or
        @($baselineEvidence.variants | Where-Object {
            $_.configHash -notmatch "^[0-9a-f]{64}$"
        }).Count -ne 0) {
        throw "Observed balance baseline variants or fair-score identities drifted."
    }
    if ((@($baselineEvidence.distributions).Count -ne 27) -or
        @($baselineEvidence.distributions | Where-Object {
            ($_.sampleCount -ne 100) -or
            (($_.outcomes.'running-at-cap' + $_.outcomes.dead + $_.outcomes.won) -ne 100) -or
            ($_.starvationDeaths + $_.collisionDeaths -gt 100) -or
            ($_.scoreMinimum -lt 0) -or
            ($_.survivalMinimum -lt 1) -or
            ($_.finalLengthP50 -lt 1) -or
            ($_.maximumLengthMaximum -lt $_.finalLengthMaximum) -or
            ($_.foodPerThousandSteps -lt 0) -or
            ($_.powerEncounterTotal -lt 0) -or
            ($_.powerPickupTotal -lt 0) -or
            ($_.powerActivationTotal -lt 0)
        }).Count -ne 0) {
        throw "Observed balance distributions are incomplete or internally inconsistent."
    }
    if ((@($baselineEvidence.runSummaries).Count -ne 2700) -or
        @($baselineEvidence.runSummaries | Where-Object {
            ($_.finalStateHash -notmatch "^[0-9a-f]{16}$") -or
            ($_.survivalSteps -lt 1) -or
            ($_.survivalSteps -gt 900) -or
            ($_.score -lt 0) -or
            ($_.finalLength -lt 1) -or
            ($_.maximumLength -lt $_.finalLength) -or
            ($_.foodEaten -lt 0) -or
            ($_.comboPeak -lt 0) -or
            ($_.powerEncounters -lt 0) -or
            ($_.powerPickups -lt 0) -or
            ($_.powerActivations -lt 0) -or
            ($_.outcome -notin @("running-at-cap", "dead", "won")) -or
            ($_.deathCause -notin @("none", "self-collision", "starvation"))
        }).Count -ne 0) {
        throw "Observed balance per-run metrics are incomplete or invalid."
    }
    if (($baselineEvidence.distributions |
            Where-Object { $_.variantId -like "vibe-*" } |
            Measure-Object -Property powerEncounterTotal -Sum).Sum -le 0 -or
        ($baselineEvidence.distributions |
            Where-Object { $_.variantId -like "vibe-*" } |
            Measure-Object -Property powerPickupTotal -Sum).Sum -le 0 -or
        ($baselineEvidence.distributions |
            Where-Object { $_.variantId -like "vibe-*" } |
            Measure-Object -Property powerActivationTotal -Sum).Sum -le 0) {
        throw "Observed Vibe baselines did not exercise power encounters, pickups, and activations."
    }
    $baselineContractPath = Join-Path $repositoryRoot "config/balance_baseline_v1.json"
    $actualBaselineContractHash = (Get-FileHash -LiteralPath $baselineContractPath -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actualBaselineContractHash -ne $baselineEvidence.baselineDocumentSha256) {
        throw "Observed balance baseline evidence does not match the reviewed baseline document."
    }

    $balanceGuardEvidencePath = Join-Path $repositoryRoot "TestResults/native/balance_experiment_guard.json"
    if (-not (Test-Path -LiteralPath $balanceGuardEvidencePath -PathType Leaf)) {
        throw "Native tests did not write balance_experiment_guard.json evidence."
    }

    $balanceGuardEvidence = Get-Content -LiteralPath $balanceGuardEvidencePath -Raw | ConvertFrom-Json
    if (($balanceGuardEvidence.kind -ne "balance-experiment-guard-v1") -or
        (-not $balanceGuardEvidence.passed) -or
        ($balanceGuardEvidence.registrySha256 -notmatch "^[0-9a-f]{64}$") -or
        ($balanceGuardEvidence.balanceFamilyCount -ne 7) -or
        ($balanceGuardEvidence.experienceEffectCount -ne 4) -or
        ($balanceGuardEvidence.requiredExperimentFieldCount -ne 18) -or
        ($balanceGuardEvidence.humanTargetRangeCount -ne 0) -or
        ($balanceGuardEvidence.experimentCount -ne 0) -or
        $balanceGuardEvidence.tuningEligible -or
        (@($balanceGuardEvidence.notes).Count -ne 3)) {
        throw "Balance experiment guard authorized tuning without reviewed targets or lost its contract."
    }
    $balanceRegistryPath = Join-Path $repositoryRoot "config/balance_experiments_v1.json"
    $balanceRegistrySha256 = (Get-FileHash -LiteralPath $balanceRegistryPath -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($balanceGuardEvidence.registrySha256 -ne $balanceRegistrySha256) {
        throw "Balance experiment guard does not match the reviewed registry."
    }

    $scoreIdentityEvidencePath = Join-Path $repositoryRoot "TestResults/native/score_identity.json"
    if (-not (Test-Path -LiteralPath $scoreIdentityEvidencePath -PathType Leaf)) {
        throw "Native tests did not write score_identity.json evidence."
    }

    $scoreIdentityEvidence = Get-Content -LiteralPath $scoreIdentityEvidencePath -Raw | ConvertFrom-Json
    if (($scoreIdentityEvidence.kind -ne "score-identity-qualification-v1") -or
        (-not $scoreIdentityEvidence.passed) -or
        ($scoreIdentityEvidence.runContextCount -ne 8) -or
        ($scoreIdentityEvidence.competitiveContextCount -ne 2) -or
        (@($scoreIdentityEvidence.separatedRunKinds).Count -ne 8) -or
        (@($scoreIdentityEvidence.separatedSeedCategories).Count -ne 8) -or
        ($scoreIdentityEvidence.personalBestSchemaVersion -ne 2) -or
        ($scoreIdentityEvidence.scoreEntryFieldCount -ne 14) -or
        ($scoreIdentityEvidence.scoreHistorySchemaVersion -ne 1) -or
        ($scoreIdentityEvidence.scoreHistoryEntryFieldCount -ne 18) -or
        ($scoreIdentityEvidence.maximumScoresPerCategory -ne 10) -or
        ($scoreIdentityEvidence.personalBestHistoryMigrationCount -ne 2) -or
        (-not $scoreIdentityEvidence.explicitModeIdentity) -or
        (-not $scoreIdentityEvidence.explicitDifficultyPolicy) -or
        (-not $scoreIdentityEvidence.explicitAdaptivePolicy) -or
        (-not $scoreIdentityEvidence.legacyMigrationVisible) -or
        ($scoreIdentityEvidence.achievementAuditSha256 -notmatch "^[0-9a-f]{64}$") -or
        ($scoreIdentityEvidence.referenceAchievementCount -ne 25) -or
        ($scoreIdentityEvidence.nativeRulesLocalAchievementCount -ne 17) -or
        ($scoreIdentityEvidence.classicEligibleAchievementCount -ne 0) -or
        ($scoreIdentityEvidence.vibeEligibleAchievementCount -ne 17) -or
        ($scoreIdentityEvidence.referenceOnlyExcludedCount -ne 8) -or
        (@($scoreIdentityEvidence.notes).Count -ne 4)) {
        throw "Score identity evidence lost category metadata, migration, or achievement audit coverage."
    }
    $requiredRunKinds = @(
        "normal-human", "tutorial", "practice", "seeded-challenge",
        "ai", "replay", "modified", "legacy-0.2"
    )
    $requiredSeedCategories = @(
        "fresh-local", "tutorial-scripted", "practice-local", "fixed-challenge",
        "ai-simulation", "recorded-replay", "modified-local", "legacy-unknown"
    )
    if ((@($scoreIdentityEvidence.separatedRunKinds) -join ",") -ne
        ($requiredRunKinds -join ",") -or
        (@($scoreIdentityEvidence.separatedSeedCategories) -join ",") -ne
        ($requiredSeedCategories -join ",")) {
        throw "Score run-kind or seed-category taxonomy drifted."
    }
    $achievementAuditPath = Join-Path $repositoryRoot "config/achievement_mode_audit_v1.json"
    $achievementAuditSha256 = (Get-FileHash -LiteralPath $achievementAuditPath -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($scoreIdentityEvidence.achievementAuditSha256 -ne $achievementAuditSha256) {
        throw "Score identity evidence does not match the reviewed achievement audit."
    }

    $dependencyInventoryPath = Join-Path $repositoryRoot "TestResults/native/dependency_inventory.json"
    & (Join-Path $PSScriptRoot "write_dependency_inventory.ps1") `
        -OutputPath $dependencyInventoryPath
    if ($LASTEXITCODE -ne 0) {
        throw "Dependency inventory generation failed."
    }

    $dependencyInventory = Get-Content -LiteralPath $dependencyInventoryPath -Raw |
        ConvertFrom-Json
    if ($dependencyInventory.kind -ne "dependency-inventory-v1" -or
        -not $dependencyInventory.generatedFromLocksOnly -or
        $dependencyInventory.sourceRevision -notmatch "^[0-9a-f]{40}$" -or
        $dependencyInventory.lockSetSha256 -notmatch "^[0-9a-f]{64}$") {
        throw "Dependency inventory provenance fields are invalid."
    }
    if (@($dependencyInventory.sources).Count -ne 7 -or
        @($dependencyInventory.packages).Count -lt 60) {
        throw "Dependency inventory is missing lock sources or expected packages."
    }
    $dependencyNames = @($dependencyInventory.packages | ForEach-Object { $_.name })
    foreach ($requiredDependency in @("GodotSharp", "xunit", "pygame-ce", "ruff")) {
        if ($requiredDependency -notin $dependencyNames) {
            throw "Dependency inventory is missing required package: $requiredDependency"
        }
    }
    $sourcePaths = @($dependencyInventory.sources | ForEach-Object { $_.path })
    foreach ($package in $dependencyInventory.packages) {
        foreach ($sourceLock in @($package.sourceLocks)) {
            if ($sourceLock -notin $sourcePaths) {
                throw "Dependency package references unknown source lock: $sourceLock"
            }
        }
    }
    $packageKeys = @($dependencyInventory.packages | ForEach-Object {
        "$($_.ecosystem)|$($_.name.ToLowerInvariant())|$($_.version)"
    })
    if (@($packageKeys | Select-Object -Unique).Count -ne $packageKeys.Count) {
        throw "Dependency inventory contains duplicate ecosystem/name/version entries."
    }
    $dotnetTool = $dependencyInventory.tools | Where-Object { $_.name -eq "dotnet-sdk" }
    $godotTool = $dependencyInventory.tools | Where-Object { $_.name -eq "godot-dotnet" }
    if ($dotnetTool.version -ne "10.0.302" -or
        $godotTool.version -ne "4.7.1" -or
        $godotTool.commit -ne "a13da4feb") {
        throw "Dependency inventory toolchain does not match the pinned native toolchain."
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

    $agentViewerGodotVariable = "VIBESNAKE_AGENT_VIEWER_GODOT_EXECUTABLE"
    $previousAgentViewerGodot = [Environment]::GetEnvironmentVariable(
        $agentViewerGodotVariable,
        [EnvironmentVariableTarget]::Process)
    try {
        [Environment]::SetEnvironmentVariable(
            $agentViewerGodotVariable,
            $resolvedGodotExecutable,
            [EnvironmentVariableTarget]::Process)
        Invoke-Dotnet -CommandArguments @(
            "test",
            "native/tests/VibeSnake.Rules.Tests/VibeSnake.Rules.Tests.csproj",
            "--configuration",
            "Release",
            "--no-build",
            "--no-restore",
            "--filter",
            "FullyQualifiedName~Godot_watch_screen_receives_real_host_frame_when_qualified"
        )
    }
    finally {
        [Environment]::SetEnvironmentVariable(
            $agentViewerGodotVariable,
            $previousAgentViewerGodot,
            [EnvironmentVariableTarget]::Process)
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

    $viewportEvidencePath = Join-Path $repositoryRoot "TestResults/native/viewport_matrix.json"
    if (-not (Test-Path -LiteralPath $viewportEvidencePath -PathType Leaf)) {
        throw "Godot smoke did not write viewport_matrix.json evidence."
    }

    $viewportEvidence = Get-Content -LiteralPath $viewportEvidencePath -Raw | ConvertFrom-Json
    $requiredViewportCases = @(
        "minimum-clamp",
        "hd-16-9",
        "classic-4-3",
        "desktop-16-10",
        "ultrawide-21-9",
        "square-1-1",
        "high-density-4k",
        "high-density-5k"
    )
    $actualViewportCases = @($viewportEvidence.cases | ForEach-Object { $_.id })
    if ($viewportEvidence.kind -ne "virtual-viewport-matrix-v1" -or -not $viewportEvidence.passed) {
        throw "Viewport matrix evidence did not report the qualified schema and pass state."
    }
    if ($actualViewportCases.Count -ne $requiredViewportCases.Count -or
        @($actualViewportCases | Select-Object -Unique).Count -ne $requiredViewportCases.Count) {
        throw "Viewport matrix evidence must contain exactly the unique required cases."
    }
    foreach ($requiredCase in $requiredViewportCases) {
        if ($requiredCase -notin $actualViewportCases) {
            throw "Viewport matrix evidence is missing required case: $requiredCase"
        }
    }

    $shellEvidencePath = Join-Path $repositoryRoot "TestResults/native/shell_presentation.json"
    if (-not (Test-Path -LiteralPath $shellEvidencePath -PathType Leaf)) {
        throw "Godot smoke did not write shell_presentation.json evidence."
    }

    $shellEvidence = Get-Content -LiteralPath $shellEvidencePath -Raw | ConvertFrom-Json
    if ($shellEvidence.kind -ne "shell-presentation-v1" -or -not $shellEvidence.passed) {
        throw "Shell presentation evidence did not report the qualified schema and pass state."
    }
    if ((-not $shellEvidence.centralizedFontOwner) -or
        (-not $shellEvidence.textFallbackRetained) -or
        ($shellEvidence.paletteCount -ne 2) -or
        ($shellEvidence.promptFamilyCount -ne 5) -or
        ($shellEvidence.glyphShapeCount -ne 8) -or
        ($shellEvidence.maximumTextScale -ne 1.5) -or
        (-not $shellEvidence.maximumTextLayoutComplete) -or
        (-not $shellEvidence.nonColorStateMarkers) -or
        (-not $shellEvidence.longCatalogPagination) -or
        ($shellEvidence.standardPrimaryContrast -lt 4.5) -or
        ($shellEvidence.standardSecondaryContrast -lt 4.5) -or
        ($shellEvidence.highContrastPrimaryContrast -lt 4.5)) {
        throw "Shell presentation evidence failed font, palette, contrast, family, or glyph gates."
    }
    foreach ($requiredPromptFlow in @("menu", "run-end", "achievements", "scores", "bindings", "content-packs", "replays", "settings", "onboarding", "spectator", "lore", "comparisons")) {
        if ($requiredPromptFlow -notin @($shellEvidence.vectorBadgeFlows)) {
            throw "Shell presentation evidence is missing vector-badge flow: $requiredPromptFlow"
        }
    }

    $localizationEvidencePath = Join-Path $repositoryRoot "TestResults/native/localization.json"
    if (-not (Test-Path -LiteralPath $localizationEvidencePath -PathType Leaf)) {
        throw "Godot smoke did not write localization.json evidence."
    }

    $localizationEvidence = Get-Content -LiteralPath $localizationEvidencePath -Raw | ConvertFrom-Json
    if (($localizationEvidence.schemaVersion -ne 1) -or
        ($localizationEvidence.kind -ne "localization-qualification-v1") -or
        (-not $localizationEvidence.passed) -or
        ($localizationEvidence.catalogId -ne "shell-copy-v1") -or
        ($localizationEvidence.requiredLocale -ne "en") -or
        ($localizationEvidence.pseudoLocale -ne "qps-ploc") -or
        ($localizationEvidence.stringCount -ne 559) -or
        ($localizationEvidence.parameterizedStringCount -ne 79) -or
        ($localizationEvidence.migratedRequiredFlowCount -ne 13) -or
        ($localizationEvidence.minimumPseudoExpansionRatio -lt 1.3) -or
        ($localizationEvidence.missingGlyphCount -ne 0) -or
        (-not $localizationEvidence.exactParameterValidation) -or
        (-not $localizationEvidence.inputGlyphParameterPreserved) -or
        (-not $localizationEvidence.maximumTextScaleLayoutPassed) -or
        ($localizationEvidence.rulesCopyIdCount -ne 18) -or
        (-not $localizationEvidence.rulesCopyIdsResolved) -or
        ($localizationEvidence.feedbackCopyIdCount -ne 24) -or
        ($localizationEvidence.broadcastCopyIdCount -ne 24) -or
        (-not $localizationEvidence.broadcastCopyIdsResolved) -or
        (-not $localizationEvidence.sourceAuditPerformed) -or
        ($localizationEvidence.remainingDirectDrawLabelLiteralCount -ne 0) -or
        ($localizationEvidence.remainingDirectPromptLiteralCount -ne 0) -or
        ($localizationEvidence.remainingDirectStatusLiteralCount -ne 0) -or
        ($localizationEvidence.remainingComposedStatusLiteralCount -ne 0) -or
        ($localizationEvidence.remainingDomainStatusExpressionCount -ne 0) -or
        ($localizationEvidence.migrationStatus -ne "shell-and-audited-domain-presentation-copy-complete")) {
        throw "Localization evidence failed catalog, pseudo-locale, glyph, layout, or migration gates."
    }

    $captureEvidencePath = Join-Path $repositoryRoot "TestResults/native/capture_sharing.json"
    if (-not (Test-Path -LiteralPath $captureEvidencePath -PathType Leaf)) {
        throw "Godot smoke did not write capture_sharing.json evidence."
    }

    $captureEvidence = Get-Content -LiteralPath $captureEvidencePath -Raw | ConvertFrom-Json
    if (($captureEvidence.schemaVersion -ne 1) -or
        ($captureEvidence.kind -ne "capture-sharing-qualification-v1") -or
        (-not $captureEvidence.passed) -or
        (-not $captureEvidence.defaultCaptureModeOff) -or
        ($captureEvidence.hiddenOverlayFamilyCount -ne 6) -or
        (-not $captureEvidence.runHudHidden) -or
        (-not $captureEvidence.replayControlsHidden) -or
        (-not $captureEvidence.terminalOverlayHidden) -or
        (-not $captureEvidence.audioStatusHidden) -or
        (-not $captureEvidence.debugOverlayHidden) -or
        (-not $captureEvidence.spectatorOverlayHidden) -or
        (-not $captureEvidence.keyboardRouteComplete) -or
        (-not $captureEvidence.controllerRouteComplete) -or
        ($captureEvidence.replaySpeedCount -ne 4) -or
        (-not $captureEvidence.deterministicReplayCaptureComplete) -or
        (-not $captureEvidence.rulesStateUnchangedByCaptureMode) -or
        ($captureEvidence.runSummarySchemaVersion -ne 1) -or
        ($captureEvidence.runSummaryFieldCount -ne 24) -or
        (-not $captureEvidence.versionMetadataComplete) -or
        (-not $captureEvidence.rulesMetadataComplete) -or
        (-not $captureEvidence.replayVerificationMetadataComplete) -or
        (-not $captureEvidence.summaryExportComplete) -or
        (-not $captureEvidence.summaryAtomicAndIdempotent) -or
        (-not $captureEvidence.playerIdentityExcluded) -or
        (-not $captureEvidence.privatePathsExcluded) -or
        ($captureEvidence.humanReviewStatus -ne "pending-platform-capture-review") -or
        (@($captureEvidence.pendingHumanChecks).Count -ne 4)) {
        throw "Capture and sharing evidence failed clean-mode, deterministic replay, metadata, privacy, or handoff gates."
    }

    $spectatorEvidencePath = Join-Path $repositoryRoot "TestResults/native/spectator_experience.json"
    if (-not (Test-Path -LiteralPath $spectatorEvidencePath -PathType Leaf)) {
        throw "Godot smoke did not write spectator_experience.json evidence."
    }

    $spectatorEvidence = Get-Content -LiteralPath $spectatorEvidencePath -Raw | ConvertFrom-Json
    if (($spectatorEvidence.schemaVersion -ne 1) -or
        ($spectatorEvidence.kind -ne "spectator-experience-qualification-v1") -or
        (-not $spectatorEvidence.passed) -or
        ($spectatorEvidence.rivalCount -ne 10) -or
        ($spectatorEvidence.measuredPolicyClaimCount -ne 10) -or
        ($spectatorEvidence.authoredCommentaryCount -ne 50) -or
        ($spectatorEvidence.distinctShedCount -ne 10) -or
        ($spectatorEvidence.stationAffinityCount -ne 10) -or
        ($spectatorEvidence.seedClassCount -ne 3) -or
        ($spectatorEvidence.seedsPerClass -ne 4) -or
        ($spectatorEvidence.playbackSpeedCount -ne 4) -or
        ($spectatorEvidence.explanationLevelCount -ne 3) -or
        ($spectatorEvidence.predictionCount -ne 4) -or
        $spectatorEvidence.wageringAllowed -or
        ($spectatorEvidence.currencyAward -ne 0) -or
        ($spectatorEvidence.humanProgressionAwardCount -ne 0) -or
        (-not $spectatorEvidence.initialRulesEqual) -or
        (-not $spectatorEvidence.deterministicMatchComplete) -or
        (-not $spectatorEvidence.overlayContractComplete) -or
        (-not $spectatorEvidence.keyboardRouteComplete) -or
        (-not $spectatorEvidence.controllerRouteComplete) -or
        (-not $spectatorEvidence.channelSwitchRulesUnchanged) -or
        (-not $spectatorEvidence.stallRecoveryComplete) -or
        (-not $spectatorEvidence.invalidChannelFallbackComplete) -or
        (-not $spectatorEvidence.missingCommentaryFallbackComplete) -or
        (-not $spectatorEvidence.audioFallbackComplete) -or
        (-not $spectatorEvidence.presentationFallbackRulesUnchanged) -or
        ($spectatorEvidence.challengeSchemaVersion -ne 1) -or
        (-not $spectatorEvidence.challengeEqualRules) -or
        (-not $spectatorEvidence.challengeAiStateExcluded) -or
        ($spectatorEvidence.leagueSchemaVersion -ne 1) -or
        ($spectatorEvidence.standingCount -ne 10) -or
        ($spectatorEvidence.challengeRecordCount -ne 10) -or
        (-not $spectatorEvidence.rivalryRecordComplete) -or
        ($spectatorEvidence.milestoneContractCount -ne 7) -or
        (-not $spectatorEvidence.localPersistenceRoundTrip) -or
        (-not $spectatorEvidence.playerIdentityExcluded) -or
        ($spectatorEvidence.humanReviewStatus -ne "pending-platform-and-content-review") -or
        (@($spectatorEvidence.pendingHumanChecks).Count -ne 4)) {
        throw "Spectator experience evidence failed equal-rules, input, recovery, persistence, or isolation gates."
    }

    $reliabilityEvidencePath = Join-Path $repositoryRoot "TestResults/native/candidate_reliability.json"
    if (-not (Test-Path -LiteralPath $reliabilityEvidencePath -PathType Leaf)) {
        throw "Godot smoke did not write candidate_reliability.json evidence."
    }

    $reliabilityEvidence = Get-Content -LiteralPath $reliabilityEvidencePath -Raw | ConvertFrom-Json
    if (($reliabilityEvidence.schemaVersion -ne 1) -or
        ($reliabilityEvidence.kind -ne "candidate-reliability-qualification-v1") -or
        (-not $reliabilityEvidence.passed) -or
        ($reliabilityEvidence.requiredStepsPerRuleset -ne 100000) -or
        ($reliabilityEvidence.rulesetCount -ne 2) -or
        ($reliabilityEvidence.totalComparedSimulationSteps -ne 200000) -or
        ($reliabilityEvidence.referenceAiId -ne "balanced") -or
        ($reliabilityEvidence.aiAlgorithmId -ne "native-personality-controller-v2") -or
        ($reliabilityEvidence.randomAlgorithmId -ne "pcg-xsh-rr-32-v1") -or
        (@($reliabilityEvidence.simulations).Count -ne 2) -or
        (@($reliabilityEvidence.pendingGates).Count -ne 1) -or
        ($reliabilityEvidence.pendingGates[0] -ne
            "retained-release-execution-on-windows-macos-linux")) {
        throw "Candidate reliability evidence failed the campaign identity or step-count gate."
    }
    $expectedReliabilityModes = @{
        classic = "classic-standard-v1"
        vibe = "vibe-standard-v1-dda-on"
    }
    foreach ($simulation in @($reliabilityEvidence.simulations)) {
        if (-not $expectedReliabilityModes.ContainsKey([string]$simulation.modeId) -or
            ($simulation.modeVersion -ne 1) -or
            ($simulation.scoreCategoryId -ne $expectedReliabilityModes[[string]$simulation.modeId]) -or
            ($simulation.referenceAiId -ne "balanced") -or
            ($simulation.requiredComparedSteps -ne 100000) -or
            ($simulation.comparedSteps -ne 100000) -or
            ($simulation.runCount -le 0) -or
            ($simulation.restartCount -ne ([int]$simulation.runCount - 1)) -or
            ($simulation.stateHashCheckpointCount -lt 100) -or
            (-not $simulation.decisionsIdentical) -or
            (-not $simulation.queueOutcomesIdentical) -or
            (-not $simulation.stepResultsIdentical) -or
            ($null -ne $simulation.firstDivergence) -or
            ([string]$simulation.decisionAndStateTraceSha256 -notmatch '^[0-9a-f]{64}$')) {
            throw "Candidate reliability simulation row failed: $($simulation.modeId)"
        }
    }
    $spectatorReliability = $reliabilityEvidence.spectatorRestarts
    if (($spectatorReliability.requiredRestarts -ne 100) -or
        ($spectatorReliability.completedRestarts -ne 100) -or
        ($spectatorReliability.stepsPerRestart -ne 8) -or
        ($spectatorReliability.completedSteps -ne 800) -or
        ($spectatorReliability.stateResetCount -ne 100) -or
        (-not $spectatorReliability.everyFreshSessionStartedPaused) -or
        (-not $spectatorReliability.everyFreshSessionResetState) -or
        (-not $spectatorReliability.everySessionAdvanced) -or
        ($spectatorReliability.managedSessionReferencesRetained -ne 0) -or
        (-not $spectatorReliability.engineNodeCountStable) -or
        (-not $spectatorReliability.engineObjectCountDidNotGrow) -or
        (-not $spectatorReliability.engineResourceCountDidNotGrow) -or
        (-not $spectatorReliability.engineOrphanNodeCountDidNotGrow) -or
        (-not $spectatorReliability.noMonotonicStateOrResourceGrowth)) {
        throw "Candidate spectator restart reliability evidence failed."
    }
    $resourceSamples = @($spectatorReliability.resourceSamples)
    if ($resourceSamples.Count -ne 11 -or
        (@($resourceSamples | ForEach-Object { $_.completedRestarts }) -join ',') -ne
            '0,10,20,30,40,50,60,70,80,90,100') {
        throw "Candidate spectator resource sampling cadence drifted."
    }
    $resourceBaseline = $resourceSamples[0]
    foreach ($resourceSample in $resourceSamples) {
        if (($resourceSample.sceneNodeCount -ne $resourceBaseline.sceneNodeCount) -or
            ($resourceSample.objectCount -gt $resourceBaseline.objectCount) -or
            ($resourceSample.resourceCount -gt $resourceBaseline.resourceCount) -or
            ($resourceSample.orphanNodeCount -gt $resourceBaseline.orphanNodeCount)) {
            throw "Candidate spectator resources grew after $($resourceSample.completedRestarts) restarts."
        }
    }

    $faultEvidencePath = Join-Path $repositoryRoot "TestResults/native/candidate_fault_campaign.json"
    if (-not (Test-Path -LiteralPath $faultEvidencePath -PathType Leaf)) {
        throw "Godot smoke did not write candidate_fault_campaign.json evidence."
    }

    $faultEvidence = Get-Content -LiteralPath $faultEvidencePath -Raw | ConvertFrom-Json
    if (($faultEvidence.schemaVersion -ne 1) -or
        ($faultEvidence.kind -ne "candidate-fault-campaign-v1") -or
        (-not $faultEvidence.passed) -or
        ($faultEvidence.requiredFaultCount -ne 7) -or
        ($faultEvidence.completedFaultCount -ne 7) -or
        (-not $faultEvidence.everyFaultDetected) -or
        (-not $faultEvidence.everyExistingDataBoundaryPreserved) -or
        (-not $faultEvidence.everyRecoveryPathVerified) -or
        (-not $faultEvidence.rulesStateUnchangedAcrossCampaign) -or
        (@($faultEvidence.faults).Count -ne 7) -or
        (@($faultEvidence.pendingGates).Count -ne 1) -or
        ($faultEvidence.pendingGates[0] -ne
            "retained-release-execution-on-windows-macos-linux")) {
        throw "Candidate fault campaign failed its closed summary gate."
    }
    $expectedFaultIds = @(
        "interrupted-write",
        "corrupt-json",
        "full-disk",
        "read-only-data-directory",
        "missing-resource",
        "invalid-content-pack",
        "unavailable-audio"
    )
    if ((@($faultEvidence.faults | ForEach-Object { $_.faultId }) -join ',') -ne
        ($expectedFaultIds -join ',')) {
        throw "Candidate fault campaign did not cover the exact roadmap fault order."
    }
    foreach ($fault in @($faultEvidence.faults)) {
        if ([string]::IsNullOrWhiteSpace([string]$fault.injectionBoundary) -or
            (-not $fault.faultDetected) -or
            (-not $fault.existingDataPreserved) -or
            (-not $fault.recoveryVerified) -or
            (-not $fault.rulesStateUnchanged)) {
            throw "Candidate fault row failed: $($fault.faultId)"
        }
    }
    $triageKinds = @{
        crashTriage = "crash-report"
        divergenceTriage = "deterministic-divergence-report-v1"
    }
    foreach ($triageField in $triageKinds.Keys) {
        $triage = $faultEvidence.$triageField
        if (($triage.reportKind -ne $triageKinds[$triageField]) -or
            (-not $triage.reportRetained) -or
            (-not $triage.schemaValid) -or
            (-not $triage.privacySafe) -or
            (-not $triage.reproductionFieldsComplete) -or
            ([System.IO.Path]::GetFileName([string]$triage.fileName) -ne $triage.fileName) -or
            ([string]$triage.sha256 -notmatch '^[0-9a-f]{64}$')) {
            throw "Candidate triage probe failed: $triageField"
        }
    }

    $loreEvidencePath = Join-Path $repositoryRoot "TestResults/native/optional_lore.json"
    if (-not (Test-Path -LiteralPath $loreEvidencePath -PathType Leaf)) {
        throw "Godot smoke did not write optional_lore.json evidence."
    }

    $loreEvidence = Get-Content -LiteralPath $loreEvidencePath -Raw | ConvertFrom-Json
    if (($loreEvidence.schemaVersion -ne 1) -or
        ($loreEvidence.kind -ne "optional-lore-qualification-v1") -or
        (-not $loreEvidence.passed) -or
        ($loreEvidence.entryCount -ne 41) -or
        ($loreEvidence.surfaceCount -ne 19) -or
        ($loreEvidence.discoverableCount -ne 14) -or
        ($loreEvidence.archiveCount -ne 8) -or
        ($loreEvidence.surfaceStationCount -ne 8) -or
        ($loreEvidence.surfaceRivalCount -ne 10) -or
        ($loreEvidence.surfaceMutationCount -ne 9) -or
        ($loreEvidence.discoverableKindCount -ne 6) -or
        ($loreEvidence.archiveKindCount -ne 4) -or
        ($loreEvidence.initialUnlockedCount -ne 19) -or
        ($loreEvidence.fullyUnlockedCount -ne 41) -or
        ($loreEvidence.missingCopyIdCount -ne 0) -or
        ($loreEvidence.brokenContinuityCount -ne 0) -or
        ($loreEvidence.unsafeCriticalEntryCount -ne 0) -or
        (-not $loreEvidence.keyboardRouteComplete) -or
        (-not $loreEvidence.controllerRouteComplete) -or
        (-not $loreEvidence.criticalCopyNamespaceIsolated) -or
        (-not $loreEvidence.rulesStateUnchangedByBrowsing) -or
        (-not $loreEvidence.progressionAwardsExcluded) -or
        (-not $loreEvidence.optionalOfflineCatalogComplete) -or
        ($loreEvidence.humanReviewStatus -ne "pending-editorial-platform-and-pacing-review") -or
        (@($loreEvidence.pendingHumanChecks).Count -ne 3)) {
        throw "Optional lore evidence failed catalog, unlock, input, continuity, safety, or isolation gates."
    }

    $offlineComparisonEvidencePath = Join-Path $repositoryRoot "TestResults/native/offline_comparisons.json"
    if (-not (Test-Path -LiteralPath $offlineComparisonEvidencePath -PathType Leaf)) {
        throw "Godot smoke did not write offline_comparisons.json evidence."
    }

    $offlineComparisonEvidence = Get-Content -LiteralPath $offlineComparisonEvidencePath -Raw | ConvertFrom-Json
    if (($offlineComparisonEvidence.schemaVersion -ne 1) -or
        ($offlineComparisonEvidence.kind -ne "offline-comparison-qualification-v1") -or
        (-not $offlineComparisonEvidence.passed) -or
        ($offlineComparisonEvidence.seedCodeSchemaVersion -ne 1) -or
        ($offlineComparisonEvidence.allowedOptionCount -ne 3) -or
        ($offlineComparisonEvidence.householdSlotCount -ne 4) -or
        ($offlineComparisonEvidence.maximumImportBytes -ne 16777216) -or
        ($offlineComparisonEvidence.runCardSchemaVersion -ne 1) -or
        ($offlineComparisonEvidence.runCardFieldCount -ne 26) -or
        ($offlineComparisonEvidence.humanReviewStatus -ne "pending-household-platform-and-playability-review") -or
        (@($offlineComparisonEvidence.pendingHumanChecks).Count -ne 3)) {
        throw "Offline comparison evidence identity, bounds, or human handoff drifted."
    }
    foreach ($requiredOfflineComparisonCheck in @(
        "seedCodeStable",
        "seedCodeTamperDetected",
        "rulesIdentityComplete",
        "contentIdentityComplete",
        "configIdentityComplete",
        "exactSeedRoundTrip",
        "explicitSourcePreservingImport",
        "atomicNoOverwriteImport",
        "modifiedImportRejected",
        "incompatibleImportRejected",
        "keyboardRouteComplete",
        "controllerRouteComplete",
        "equalRulesGhostComplete",
        "actualGameGhostRouteComplete",
        "ghostStateIsolated",
        "runCardReadable",
        "runCardAtomicAndIdempotent",
        "playerIdentityExcluded",
        "privatePathsExcluded",
        "deletionRequiresExactConfirmation",
        "deleteCancelLossless",
        "confirmedDeleteExact",
        "progressionAwardsExcluded",
        "coreOffline"
    )) {
        if (-not $offlineComparisonEvidence.$requiredOfflineComparisonCheck) {
            throw "Offline comparison evidence failed required check: $requiredOfflineComparisonCheck"
        }
    }

    $settingsEvidencePath = Join-Path $repositoryRoot "TestResults/native/settings_screen.json"
    if (-not (Test-Path -LiteralPath $settingsEvidencePath -PathType Leaf)) {
        throw "Godot smoke did not write settings_screen.json evidence."
    }

    $settingsEvidence = Get-Content -LiteralPath $settingsEvidencePath -Raw | ConvertFrom-Json
    if (($settingsEvidence.kind -ne "settings-screen-qualification-v1") -or
        (-not $settingsEvidence.passed) -or
        ($settingsEvidence.preferenceSchemaVersion -ne 7) -or
        ($settingsEvidence.sectionCount -ne 6) -or
        ($settingsEvidence.itemCount -ne 34) -or
        (-not $settingsEvidence.everyItemDescribed)) {
        throw "Settings screen evidence did not report the complete schema-7 information architecture."
    }
    foreach ($requiredSettingsCheck in @(
        "keyboardRouteComplete",
        "controllerRouteComplete",
        "keyboardRemappingComplete",
        "controllerRemappingComplete",
        "conflictSwapAndCancelComplete",
        "oppositeDeviceBindingsRetained",
        "singleActionNavigationComplete",
        "sectionResetComplete",
        "fullResetCancelLossless",
        "fullResetComplete",
        "saveReloadComplete",
        "saveFailureVisible",
        "controllerDeadzoneApplied",
        "digitalFallbackRetained",
        "monoOutputApplied",
        "displayModesApplied",
        "vibeAdaptationOptOutApplied",
        "localPlaytestConsentApplied"
    )) {
        if (-not $settingsEvidence.$requiredSettingsCheck) {
            throw "Settings screen evidence failed required check: $requiredSettingsCheck"
        }
    }
    foreach ($requiredSettingsSection in @(
        "gameplay",
        "controls",
        "audio",
        "display",
        "accessibility",
        "data"
    )) {
        if ($requiredSettingsSection -notin @($settingsEvidence.sections)) {
            throw "Settings screen evidence is missing section: $requiredSettingsSection"
        }
    }

    $scoreBrowserEvidencePath = Join-Path $repositoryRoot "TestResults/native/score_browser.json"
    if (-not (Test-Path -LiteralPath $scoreBrowserEvidencePath -PathType Leaf)) {
        throw "Godot smoke did not write score_browser.json evidence."
    }

    $scoreBrowserEvidence = Get-Content -LiteralPath $scoreBrowserEvidencePath -Raw | ConvertFrom-Json
    if (($scoreBrowserEvidence.kind -ne "score-browser-qualification-v1") -or
        (-not $scoreBrowserEvidence.passed) -or
        ($scoreBrowserEvidence.schemaVersion -ne 1) -or
        ($scoreBrowserEvidence.scoreHistorySchemaVersion -ne 1) -or
        ($scoreBrowserEvidence.maximumScoresPerCategory -ne 10) -or
        ($scoreBrowserEvidence.persistedFieldsPerScore -ne 18) -or
        ($scoreBrowserEvidence.importedEntryCount -ne 2) -or
        ($scoreBrowserEvidence.importInboxRelativePath -ne "imports/high_scores.json") -or
        ($scoreBrowserEvidence.sourceSha256 -notmatch '^[0-9a-f]{64}$')) {
        throw "Score-browser evidence lost its schema, top-ten, import, or identity contract."
    }
    foreach ($requiredScoreBrowserCheck in @(
        "keyboardOpenComplete",
        "controllerOpenComplete",
        "keyboardCancelLossless",
        "controllerCategoryNavigationComplete",
        "explicitConfirmationRequired",
        "controllerImportComplete",
        "sourceUnchanged",
        "oneTimeImportComplete",
        "legacyCategoryVisible",
        "legacyCategoryNoncompetitive",
        "nativeCategoriesSeparated",
        "personalBestHistoryVisible",
        "resetCategoryOwnsScoreHistory"
    )) {
        if (-not $scoreBrowserEvidence.$requiredScoreBrowserCheck) {
            throw "Score-browser evidence failed required check: $requiredScoreBrowserCheck"
        }
    }

    $progressionEvidencePath =
        Join-Path $repositoryRoot "TestResults/native/progression_qualification.json"
    if (-not (Test-Path -LiteralPath $progressionEvidencePath -PathType Leaf)) {
        throw "Godot smoke did not write progression_qualification.json evidence."
    }

    $progressionEvidence =
        Get-Content -LiteralPath $progressionEvidencePath -Raw | ConvertFrom-Json
    if (($progressionEvidence.kind -ne "progression-qualification-v1") -or
        ($progressionEvidence.schemaVersion -ne 1) -or
        (-not $progressionEvidence.passed) -or
        ($progressionEvidence.progressionDocumentSchemaVersion -ne 1) -or
        ($progressionEvidence.goalCount -ne 20) -or
        ($progressionEvidence.goalLaneCount -ne 3) -or
        ($progressionEvidence.pacingTierCount -ne 3) -or
        ($progressionEvidence.exactRequirementCount -ne 20) -or
        ($progressionEvidence.highlightedGoalCount -ne 1) -or
        ($progressionEvidence.repetitionOnlyGoalCount -ne 0) -or
        ($progressionEvidence.cosmeticSetCount -ne 8) -or
        ($progressionEvidence.cosmeticProfileCaseCount -ne 16) -or
        ($progressionEvidence.tourSchemaVersion -ne 1) -or
        ($progressionEvidence.tourEventCount -ne 12) -or
        ($progressionEvidence.tourTierCount -ne 4) -or
        ($progressionEvidence.humanDistributionCount -ne 0) -or
        ($progressionEvidence.humanDistributionStatus -ne
            "pending-zero-reviewed-human-sessions") -or
        $progressionEvidence.aiEvidenceUsedAsHumanTarget) {
        throw "Progression evidence lost its exact-goal, cosmetic, tour, or human-evidence boundary."
    }
    foreach ($requiredProgressionCheck in @(
        "keyboardBrowseAndHighlightComplete",
        "controllerBrowseAndHighlightComplete",
        "highlightRoundTripComplete",
        "humanOnlyProgression",
        "notificationQueueBounded",
        "reducedMotionNotificationReadable",
        "cosmeticQualificationPassed",
        "cosmeticRulesIsolationPassed",
        "cosmeticKeyboardRouteComplete",
        "cosmeticControllerRouteComplete",
        "cosmeticSelectionRoundTripComplete",
        "tourValidationPassed",
        "practiceNoncompetitive",
        "immediateRematchAndReplayComplete",
        "tourKeyboardRouteComplete",
        "tourControllerRouteComplete",
        "tourPracticeIsolationComplete",
        "tourContextReferencesComplete"
    )) {
        if (-not $progressionEvidence.$requiredProgressionCheck) {
            throw "Progression evidence failed required check: $requiredProgressionCheck"
        }
    }

    $contentCurationEvidencePath =
        Join-Path $repositoryRoot "TestResults/native/content_curation.json"
    if (-not (Test-Path -LiteralPath $contentCurationEvidencePath -PathType Leaf)) {
        throw "Native tests did not write content_curation.json evidence."
    }

    $contentCurationEvidence =
        Get-Content -LiteralPath $contentCurationEvidencePath -Raw | ConvertFrom-Json
    if (($contentCurationEvidence.kind -ne "content-curation-qualification-v1") -or
        ($contentCurationEvidence.schemaVersion -ne 1) -or
        (-not $contentCurationEvidence.passed) -or
        (-not $contentCurationEvidence.automatedFoundationPassed) -or
        $contentCurationEvidence.releaseReady -or
        ($contentCurationEvidence.planId -ne "vibesnake-content-curation-v1") -or
        ($contentCurationEvidence.decisionStatus -ne "pending-human-listening-review") -or
        ($contentCurationEvidence.runtimeRadioAssetCount -ne 95) -or
        ($contentCurationEvidence.pendingRadioTrackCount -ne 95) -or
        ($contentCurationEvidence.approvedRadioTrackCount -ne 0) -or
        ($contentCurationEvidence.rejectedRadioTrackCount -ne 0) -or
        ($contentCurationEvidence.coreMusicCandidateCount -ne 0) -or
        ($contentCurationEvidence.stationCount -ne 8) -or
        ($contentCurationEvidence.minimumStationCandidateCount -ne 11) -or
        ($contentCurationEvidence.maximumStationCandidateCount -ne 13) -or
        ($contentCurationEvidence.duplicateRadioAssetCount -ne 0) -or
        ($contentCurationEvidence.suspiciousFilenameCount -ne 0) -or
        ($contentCurationEvidence.fullDecodeEvidenceCount -ne 0) -or
        ($contentCurationEvidence.loudnessEvidenceCount -ne 0) -or
        ($contentCurationEvidence.humanListeningReviewCount -ne 0) -or
        ($contentCurationEvidence.exportEligibleFileCount -ne 0) -or
        ($contentCurationEvidence.creditsContract -ne "content-credits-v1") -or
        (@($contentCurationEvidence.stations).Count -ne 8) -or
        (@($contentCurationEvidence.releaseBlockers).Count -ne 4)) {
        throw "Content-curation evidence lost its exact review queue or fail-closed gates."
    }

    $creatorContentEvidencePath =
        Join-Path $repositoryRoot "TestResults/native/creator_content.json"
    if (-not (Test-Path -LiteralPath $creatorContentEvidencePath -PathType Leaf)) {
        throw "Native tests did not write creator_content.json evidence."
    }

    $creatorContentEvidence =
        Get-Content -LiteralPath $creatorContentEvidencePath -Raw | ConvertFrom-Json
    if (($creatorContentEvidence.kind -ne "creator-content-qualification-v1") -or
        ($creatorContentEvidence.schemaVersion -ne 1) -or
        (-not $creatorContentEvidence.passed) -or
        ($creatorContentEvidence.contract -ne "creator-content-validation-v1") -or
        ($creatorContentEvidence.schemaCount -ne 2) -or
        ($creatorContentEvidence.exampleCount -ne 2) -or
        ($creatorContentEvidence.personalityCodeCount -ne 16) -or
        ($creatorContentEvidence.packAndCompatibilityCodeCount -ne 15) -or
        (-not $creatorContentEvidence.canonicalManifestRequired) -or
        ($creatorContentEvidence.collisionPolicy -ne "reject-all-duplicate-pack-ids") -or
        ($creatorContentEvidence.resolutionOrder -ne
            "core-then-ordinal-unique-optional-ids") -or
        $creatorContentEvidence.executesContent -or
        $creatorContentEvidence.arbitraryCodeSupported -or
        (-not $creatorContentEvidence.schemasPublished) -or
        (-not $creatorContentEvidence.examplesPublished) -or
        (-not $creatorContentEvidence.stableErrorCodesPublished) -or
        (-not $creatorContentEvidence.collisionRulesPublished) -or
        (-not $creatorContentEvidence.noEngineOrNetworkReferences) -or
        ((@($creatorContentEvidence.commands) -join ",") -ne "personality,pack-set")) {
        throw "Creator-content evidence lost its schema, command, collision, or no-code boundary."
    }

    $playtestEvidencePath = Join-Path $repositoryRoot "TestResults/native/local_playtest_summaries.json"
    if (-not (Test-Path -LiteralPath $playtestEvidencePath -PathType Leaf)) {
        throw "Godot smoke did not write local_playtest_summaries.json evidence."
    }

    $playtestEvidence = Get-Content -LiteralPath $playtestEvidencePath -Raw | ConvertFrom-Json
    if (($playtestEvidence.kind -ne "local-playtest-summary-qualification-v1") -or
        (-not $playtestEvidence.passed) -or
        ($playtestEvidence.preferenceSchemaVersion -ne 7) -or
        ($playtestEvidence.summarySchemaVersion -ne 2) -or
        ($playtestEvidence.collectionBasis -ne "explicit-local-opt-in") -or
        ($playtestEvidence.retentionLimit -ne 200) -or
        ($playtestEvidence.exportFileLimit -ne 20) -or
        ($playtestEvidence.maximumDocumentBytes -ne 524288) -or
        (@($playtestEvidence.allowedSummaryFields).Count -ne 26) -or
        (@($playtestEvidence.forbiddenFieldFamilies).Count -ne 10) -or
        (@($playtestEvidence.retentionRules).Count -ne 5)) {
        throw "Local playtest summary evidence did not retain its privacy and retention contract."
    }
    foreach ($requiredPlaytestCheck in @(
        "defaultConsentOff",
        "consentKeyboardRouteComplete",
        "consentRoundTrip",
        "terminalCaptureHonored",
        "disabledCaptureSkipped",
        "fieldAllowlistExact",
        "forbiddenFieldsAbsent",
        "exportKeyboardRouteComplete",
        "deleteControllerRouteComplete",
        "deleteCancelLossless",
        "storeAndExportsDeleted",
        "uploadSurfaceAbsent"
    )) {
        if (-not $playtestEvidence.$requiredPlaytestCheck) {
            throw "Local playtest summary evidence failed required check: $requiredPlaytestCheck"
        }
    }
    $requiredSummaryFields = @(
        "summaryId", "capturedAtUtc", "appVersion", "runKind", "rulesetId",
        "rulesVersion", "modeId", "modeVersion", "scoreCategoryId", "configHash",
        "adaptationEnabled", "adaptivePolicyId", "adaptiveFinalState", "seed", "outcome",
        "deathCause", "survivalSteps", "score", "finalLength", "foodEaten", "wraps",
        "nearMisses", "powerupsCollected", "comboPeak", "finalStateHash",
        "powerDecisions"
    )
    if ((@($playtestEvidence.allowedSummaryFields) -join ",") -ne
        ($requiredSummaryFields -join ",")) {
        throw "Local playtest summary field allowlist drifted."
    }

    $powerDecisionEvidencePath = Join-Path $repositoryRoot "TestResults/native/power_decisions.json"
    if (-not (Test-Path -LiteralPath $powerDecisionEvidencePath -PathType Leaf)) {
        throw "Godot smoke did not write power_decisions.json evidence."
    }

    $powerDecisionEvidence = Get-Content -LiteralPath $powerDecisionEvidencePath -Raw | ConvertFrom-Json
    if (($powerDecisionEvidence.kind -ne "power-decision-qualification-v1") -or
        (-not $powerDecisionEvidence.passed) -or
        ($powerDecisionEvidence.policyId -ne "power-decisions-v1") -or
        ($powerDecisionEvidence.powerCount -ne 9) -or
        ($powerDecisionEvidence.familyCount -ne 4) -or
        ($powerDecisionEvidence.lifecycleStageCount -ne 8) -or
        ($powerDecisionEvidence.synergyScenarioCount -ne 6) -or
        ($powerDecisionEvidence.localSummarySchemaVersion -ne 2) -or
        ($powerDecisionEvidence.deathAdjacencyWindowTicks -ne 20) -or
        ($powerDecisionEvidence.humanScenarioStatus -ne "pending-zero-sessions") -or
        ($powerDecisionEvidence.mutationForkStatus -ne
            "automated-prototype-human-unverified") -or
        $powerDecisionEvidence.mutationForkEnabled -or
        (@($powerDecisionEvidence.powers).Count -ne 9) -or
        (@($powerDecisionEvidence.scenarios).Count -ne 6)) {
        throw "Power-decision evidence lost its portfolio, lifecycle, or experiment gate."
    }
    foreach ($requiredPowerDecisionCheck in @(
        "catalogExact",
        "contractExact",
        "productVibeEnabled",
        "classicAndCompatibilityDisabled",
        "configIdentitySeparated",
        "allNineAutomaticOffersReachable",
        "automaticOffersDeterministic",
        "protectionRedundancySuppressed",
        "tempoRedundancySuppressed",
        "harvestSynergiesRetained",
        "geometryRedundancySuppressed",
        "offerPrecedesCollection",
        "typeFamilyAndVisibilityReadableBesideActiveState",
        "allHeldAndDurationStatesReadable",
        "lifecycleTraceComplete",
        "localSummaryAggregateOnly",
        "mutationForkPrototypeGated"
    )) {
        if (-not $powerDecisionEvidence.$requiredPowerDecisionCheck) {
            throw "Power-decision evidence failed required check: $requiredPowerDecisionCheck"
        }
    }
    $powerDecisionContractPath = Join-Path $repositoryRoot "config/power_decision_contract_v1.json"
    $powerDecisionContractSha256 =
        (Get-FileHash -LiteralPath $powerDecisionContractPath -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($powerDecisionEvidence.contractSha256 -ne $powerDecisionContractSha256) {
        throw "Power-decision evidence does not match the reviewed contract."
    }
    if ((@($powerDecisionEvidence.powers.id) -join ",") -ne
        "shield,phase-shift,last-stand,slow-mo,boost,magnet,bait,gluttony,segment-detach" -or
        (@($powerDecisionEvidence.powers.family | Select-Object -Unique).Count -ne 4) -or
        (@($powerDecisionEvidence.scenarios.id) -join ",") -ne
        "boost-phase-shift,slow-mo-magnet,bait-boost,gluttony-magnet,segment-detach-protection,last-stand-long-combo") {
        throw "Power-decision catalog or required synergy scenarios drifted."
    }

    $humanHandoffEvidencePath = Join-Path $repositoryRoot "TestResults/native/human_playtest_handoff.json"
    if (-not (Test-Path -LiteralPath $humanHandoffEvidencePath -PathType Leaf)) {
        throw "Native tests did not write human_playtest_handoff.json evidence."
    }

    $humanHandoffEvidence = Get-Content -LiteralPath $humanHandoffEvidencePath -Raw | ConvertFrom-Json
    if (($humanHandoffEvidence.kind -ne "human-playtest-handoff-v1") -or
        (-not $humanHandoffEvidence.passed) -or
        ($humanHandoffEvidence.status -ne "automated-qualified-experience-unverified") -or
        ($humanHandoffEvidence.protocolSha256 -notmatch "^[0-9a-f]{64}$") -or
        ($humanHandoffEvidence.cohortCount -ne 4) -or
        ($humanHandoffEvidence.stageCount -ne 3) -or
        ($humanHandoffEvidence.scenarioCount -ne 15) -or
        ($humanHandoffEvidence.recoveryProfileCount -ne 6) -or
        ($humanHandoffEvidence.requiredBuildFieldCount -ne 13) -or
        ($humanHandoffEvidence.requiredObservationFieldCount -ne 19) -or
        ($humanHandoffEvidence.severityCount -ne 4) -or
        ($humanHandoffEvidence.privacyForbiddenFieldFamilyCount -ne 10) -or
        ($humanHandoffEvidence.humanSessionCount -ne 0) -or
        $humanHandoffEvidence.experienceVerified -or
        $humanHandoffEvidence.humanTargetRangesEstablished -or
        (@($humanHandoffEvidence.requiredArtifactPaths).Count -ne 11) -or
        (@($humanHandoffEvidence.notes).Count -ne 3)) {
        throw "Human playtest handoff evidence overstated experience or lost its protocol contract."
    }
    $humanProtocolPath = Join-Path $repositoryRoot "config/qa_human_playtest_protocol.json"
    $humanProtocolSha256 = (Get-FileHash -LiteralPath $humanProtocolPath -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($humanHandoffEvidence.protocolSha256 -ne $humanProtocolSha256) {
        throw "Human playtest handoff protocol hash does not match the reviewed source."
    }
    $requiredHumanHandoffArtifacts = @(
        "TestResults/native/balance_laboratory.json",
        "TestResults/native/balance_baselines.json",
        "TestResults/native/input_cadence.json",
        "TestResults/native/bare_arcade_loop.json",
        "TestResults/native/settings_screen.json",
        "TestResults/native/local_playtest_summaries.json",
        "TestResults/native/power_decisions.json",
        "TestResults/native/multimodal_feedback.json",
        "TestResults/native/visual_hierarchy.json",
        "TestResults/native/performance.json",
        "TestResults/native/vibe_level.json"
    )
    if ((@($humanHandoffEvidence.requiredArtifactPaths) -join ",") -ne
        ($requiredHumanHandoffArtifacts -join ",")) {
        throw "Human playtest handoff artifact allowlist drifted."
    }
    foreach ($relativeArtifact in $requiredHumanHandoffArtifacts) {
        if ([System.IO.Path]::IsPathRooted($relativeArtifact)) {
            throw "Human playtest handoff artifact path must remain repository-relative."
        }
        $artifactPath = Join-Path $repositoryRoot $relativeArtifact
        if (-not (Test-Path -LiteralPath $artifactPath -PathType Leaf)) {
            throw "Human playtest handoff is missing required artifact: $relativeArtifact"
        }
    }

    $playerDataEvidencePath = Join-Path $repositoryRoot "TestResults/native/player_data_recovery.json"
    if (-not (Test-Path -LiteralPath $playerDataEvidencePath -PathType Leaf)) {
        throw "Godot smoke did not write player_data_recovery.json evidence."
    }

    $playerDataEvidence = Get-Content -LiteralPath $playerDataEvidencePath -Raw | ConvertFrom-Json
    if (($playerDataEvidence.kind -ne "player-data-recovery-qualification-v1") -or
        (-not $playerDataEvidence.passed) -or
        ($playerDataEvidence.categoryCount -ne 5) -or
        (@($playerDataEvidence.categories).Count -ne 5)) {
        throw "Player-data recovery evidence did not report all five separate categories."
    }
    foreach ($requiredPlayerDataCheck in @(
        "exactConfirmationComplete",
        "cancelWithoutWriteComplete",
        "backupBeforeResetComplete",
        "backupIntegrityComplete",
        "separateCategoryResetComplete",
        "corruptBackupDetected",
        "corruptRestoreRejected",
        "conflictWithoutOverwriteComplete",
        "restoreComplete",
        "keyboardRouteComplete",
        "controllerRouteComplete",
        "recoveryLocationVisible"
    )) {
        if (-not $playerDataEvidence.$requiredPlayerDataCheck) {
            throw "Player-data recovery evidence failed required check: $requiredPlayerDataCheck"
        }
    }

    $onboardingEvidencePath = Join-Path $repositoryRoot "TestResults/native/onboarding.json"
    if (-not (Test-Path -LiteralPath $onboardingEvidencePath -PathType Leaf)) {
        throw "Godot smoke did not write onboarding.json evidence."
    }

    $onboardingEvidence = Get-Content -LiteralPath $onboardingEvidencePath -Raw | ConvertFrom-Json
    if (($onboardingEvidence.kind -ne "onboarding-qualification-v2") -or
        (-not $onboardingEvidence.passed) -or
        ($onboardingEvidence.lessonCount -ne 8) -or
        (@($onboardingEvidence.lessons).Count -ne 8)) {
        throw "Onboarding evidence did not report the complete lesson contract."
    }
    foreach ($requiredOnboardingCheck in @(
        "titleFirstComplete",
        "optionalOfferComplete",
        "directPlayComplete",
        "keyboardRouteComplete",
        "controllerRouteComplete",
        "activeDevicePromptsComplete",
        "skipPersisted",
        "completionPersisted",
        "replayAvailable",
        "resetComplete",
        "competitiveScoreIsolated",
        "achievementsIsolated",
        "replaysIsolated"
    )) {
        if (-not $onboardingEvidence.$requiredOnboardingCheck) {
            throw "Onboarding evidence failed required check: $requiredOnboardingCheck"
        }
    }
    foreach ($requiredLesson in @(
        "turning",
        "invalid-reversal",
        "wrapping",
        "food-and-score",
        "starvation",
        "power-up",
        "pause",
        "restart"
    )) {
        $matches = @($onboardingEvidence.lessons | Where-Object { $_ -eq $requiredLesson })
        if ($matches.Count -ne 1) {
            throw "Onboarding evidence is missing unique lesson: $requiredLesson"
        }
    }

    $runEndEvidencePath = Join-Path $repositoryRoot "TestResults/native/run_end.json"
    if (-not (Test-Path -LiteralPath $runEndEvidencePath -PathType Leaf)) {
        throw "Godot smoke did not write run_end.json evidence."
    }

    $runEndEvidence = Get-Content -LiteralPath $runEndEvidencePath -Raw | ConvertFrom-Json
    if (($runEndEvidence.kind -ne "run-end-qualification-v1") -or
        (-not $runEndEvidence.passed)) {
        throw "Run-end evidence did not report the qualified schema and pass state."
    }
    foreach ($requiredRunEndCheck in @(
        "summaryOrderComplete",
        "collisionAttributionComplete",
        "starvationAttributionComplete",
        "recoveryHintComplete",
        "personalBestPersisted",
        "fairCategorySeparated",
        "sameInputRestartRejected",
        "laterIntentAccepted",
        "onlyConfirmRestarts",
        "keyboardRestartComplete",
        "controllerRestartComplete",
        "menuAccessRetained",
        "settingsAccessRetained",
        "replayAccessRetained",
        "unlockSummaryComplete"
    )) {
        if (-not $runEndEvidence.$requiredRunEndCheck) {
            throw "Run-end evidence failed required check: $requiredRunEndCheck"
        }
    }

    $accessibilityEvidencePath = Join-Path $repositoryRoot "TestResults/native/accessibility_presentation.json"
    if (-not (Test-Path -LiteralPath $accessibilityEvidencePath -PathType Leaf)) {
        throw "Godot smoke did not write accessibility_presentation.json evidence."
    }

    $accessibilityEvidence = Get-Content -LiteralPath $accessibilityEvidencePath -Raw | ConvertFrom-Json
    if (($accessibilityEvidence.kind -ne "accessibility-presentation-v1") -or
        (-not $accessibilityEvidence.passed) -or
        ($accessibilityEvidence.profileCount -ne 4) -or
        ($accessibilityEvidence.cueCount -ne 31) -or
        (-not $accessibilityEvidence.allFullScreenFlashDisabled) -or
        (-not $accessibilityEvidence.allCriticalTextRetained) -or
        (-not $accessibilityEvidence.allCuesRetained) -or
        (-not $accessibilityEvidence.rulesStateUnchanged) -or
        (@($accessibilityEvidence.profiles).Count -ne 4)) {
        throw "Accessibility presentation evidence did not report the complete safe profile matrix."
    }
    foreach ($requiredAccessibilityProfile in @(
        "default",
        "reduced-motion",
        "flash-free",
        "reduced-motion-flash-free"
    )) {
        $matchingProfiles = @($accessibilityEvidence.profiles | Where-Object {
            $_.id -eq $requiredAccessibilityProfile
        })
        if ($matchingProfiles.Count -ne 1) {
            throw "Accessibility presentation evidence is missing unique profile: $requiredAccessibilityProfile"
        }
    }

    $candidateAccessibilityPath = Join-Path `
        $repositoryRoot `
        "TestResults/native/candidate_accessibility_audit.json"
    if (-not (Test-Path -LiteralPath $candidateAccessibilityPath -PathType Leaf)) {
        throw "Godot smoke did not write candidate_accessibility_audit.json evidence."
    }

    $candidateAccessibility = Get-Content -LiteralPath $candidateAccessibilityPath -Raw |
        ConvertFrom-Json
    $expectedAccessibilityAreas = @(
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
    $expectedAccessibilityDisplays = @(
        "minimum-clamp",
        "hd-16-9",
        "classic-4-3",
        "desktop-16-10",
        "ultrawide-21-9",
        "square-1-1",
        "high-density-4k",
        "high-density-5k"
    )
    $expectedAccessibilitySources = @(
        "accessibility_presentation.json",
        "shell_presentation.json",
        "settings_screen.json",
        "input_cadence.json",
        "audio_fallback_stress.json",
        "multimodal_feedback.json",
        "viewport_matrix.json"
    )
    if (($candidateAccessibility.schemaVersion -ne 1) -or
        ($candidateAccessibility.kind -ne "candidate-accessibility-audit-v1") -or
        (-not $candidateAccessibility.passed) -or
        ($candidateAccessibility.requiredFlowDefectSeverity -ne "P1") -or
        ($candidateAccessibility.auditAreaCount -ne 12) -or
        (-not $candidateAccessibility.allAutomatedAuditAreasPassed) -or
        (-not $candidateAccessibility.keyboardOnlyRouteComplete) -or
        (-not $candidateAccessibility.controllerOnlyRouteComplete) -or
        (-not $candidateAccessibility.remappingComplete) -or
        (-not $candidateAccessibility.singleActionNavigationComplete) -or
        (-not $candidateAccessibility.independentAudioControlsComplete) -or
        (-not $candidateAccessibility.monoOutputComplete) -or
        (-not $candidateAccessibility.visualAlternativesComplete) -or
        (-not $candidateAccessibility.reducedMotionComplete) -or
        (-not $candidateAccessibility.flashSafetyComplete) -or
        (-not $candidateAccessibility.maximumTextScaleViewportMatrixComplete) -or
        ([math]::Abs($candidateAccessibility.maximumTextScale - 1.5) -gt 0.0001) -or
        ($candidateAccessibility.supportedDisplayClassCount -ne 8) -or
        ($candidateAccessibility.maximumTextScaleDisplayClassCount -ne 8) -or
        ($candidateAccessibility.accessibilityUserReviewStatus -ne
            "pending-accessibility-user-review") -or
        ($candidateAccessibility.featureGuidePath -ne "docs/guides/ACCESSIBILITY.md") -or
        ($candidateAccessibility.featurePublicationStatus -ne "published-in-repository") -or
        ((@($candidateAccessibility.auditAreas.id) -join ',') -ne
            ($expectedAccessibilityAreas -join ',')) -or
        ((@($candidateAccessibility.displayClasses.id) -join ',') -ne
            ($expectedAccessibilityDisplays -join ',')) -or
        ((@($candidateAccessibility.sources.fileName) -join ',') -ne
            ($expectedAccessibilitySources -join ',')) -or
        (@($candidateAccessibility.pendingHumanChecks).Count -ne 5)) {
        throw "Candidate accessibility evidence did not report the complete automated audit."
    }
    foreach ($source in @($candidateAccessibility.sources)) {
        if ([string]$source.sha256 -notmatch '^[0-9a-f]{64}$') {
            throw "Candidate accessibility source did not retain a SHA-256 digest: $($source.fileName)"
        }
    }

    $multimodalEvidencePath = Join-Path $repositoryRoot "TestResults/native/multimodal_feedback.json"
    if (-not (Test-Path -LiteralPath $multimodalEvidencePath -PathType Leaf)) {
        throw "Godot smoke did not write multimodal_feedback.json evidence."
    }

    $multimodalEvidence = Get-Content -LiteralPath $multimodalEvidencePath -Raw | ConvertFrom-Json
    if (($multimodalEvidence.kind -ne "multimodal-feedback-v1") -or
        (-not $multimodalEvidence.passed) -or
        ($multimodalEvidence.hungerPhaseCount -ne 4) -or
        ($multimodalEvidence.comboMilestoneCount -ne 4) -or
        ($multimodalEvidence.powerCount -ne 9) -or
        ($multimodalEvidence.deathCauseCount -ne 2) -or
        (@($multimodalEvidence.hungerStates).Count -ne 4) -or
        (@($multimodalEvidence.comboMilestones).Count -ne 4) -or
        (@($multimodalEvidence.powers).Count -ne 9) -or
        (@($multimodalEvidence.deaths).Count -ne 2) -or
        (@($multimodalEvidence.profiles).Count -ne 5)) {
        throw "Multimodal feedback evidence did not report the complete event and profile matrix."
    }
    foreach ($requiredMultimodalCheck in @(
        "timerShapeTextColorProgression",
        "scoreAndComboMoveTogether",
        "comboMotionHasStaticFallback",
        "powerIdentityOneToOne",
        "recoveryProtectionPreTelegraphed",
        "deathSignalsDistinct",
        "allProfilesDeathAttributionSurvives",
        "rulesStateUnchanged"
    )) {
        if (-not $multimodalEvidence.$requiredMultimodalCheck) {
            throw "Multimodal feedback evidence failed required check: $requiredMultimodalCheck"
        }
    }
    foreach ($requiredMultimodalProfile in @(
        "default",
        "muted",
        "reduced-motion",
        "flash-free",
        "minimum-effects-muted"
    )) {
        $matchingProfiles = @($multimodalEvidence.profiles | Where-Object {
            $_.id -eq $requiredMultimodalProfile
        })
        if ($matchingProfiles.Count -ne 1 -or
            ($matchingProfiles[0].collisionSurvivingChannels -lt 2) -or
            ($matchingProfiles[0].starvationSurvivingChannels -lt 2) -or
            (-not $matchingProfiles[0].hungerTextAndShapeRetained) -or
            (-not $matchingProfiles[0].comboMultiplierRetained) -or
            (-not $matchingProfiles[0].protectionTelegraphRetained)) {
            throw "Multimodal feedback evidence is missing a qualified profile: $requiredMultimodalProfile"
        }
    }
    if (@($multimodalEvidence.powers.stableIcon | Select-Object -Unique).Count -ne 9 -or
        @($multimodalEvidence.powers.activationCue | Select-Object -Unique).Count -ne 9) {
        throw "Multimodal power evidence contains a duplicate icon or activation cue."
    }

    $visualHierarchyEvidencePath = Join-Path $repositoryRoot "TestResults/native/visual_hierarchy.json"
    if (-not (Test-Path -LiteralPath $visualHierarchyEvidencePath -PathType Leaf)) {
        throw "Godot smoke did not write visual_hierarchy.json evidence."
    }

    $visualHierarchyEvidence = Get-Content -LiteralPath $visualHierarchyEvidencePath -Raw | ConvertFrom-Json
    if (($visualHierarchyEvidence.kind -ne "visual-hierarchy-qualification-v1") -or
        (-not $visualHierarchyEvidence.passed) -or
        ($visualHierarchyEvidence.humanPixelReviewStatus -ne "pending") -or
        (@($visualHierarchyEvidence.priorities).Count -ne 7) -or
        (@($visualHierarchyEvidence.scenarios).Count -ne 5) -or
        (@($visualHierarchyEvidence.pendingHumanChecks).Count -ne 4)) {
        throw "Visual hierarchy evidence did not retain the complete automation and human handoff."
    }
    foreach ($requiredVisualHierarchyCheck in @(
        "budgetsComplete",
        "peakFeedbackReserved",
        "gameplayChannelsRemainReadable",
        "backgroundContrastQualified",
        "productionPolicyConnected",
        "screenshotScenariosComplete",
        "rulesStateUnchanged"
    )) {
        if (-not $visualHierarchyEvidence.$requiredVisualHierarchyCheck) {
            throw "Visual hierarchy evidence failed required check: $requiredVisualHierarchyCheck"
        }
    }
    $visualBudget = $visualHierarchyEvidence.budget
    if (($visualBudget.maximumSimultaneousParticles -ne 160) -or
        ($visualBudget.maximumParticlesPerEvent -ne 64) -or
        ($visualBudget.maximumSimultaneousShakeSources -ne 1) -or
        ($visualBudget.maximumScreenShakeStrength -ne 0.35) -or
        ($visualBudget.maximumSimultaneousFullScreenFlashes -ne 0) -or
        ($visualBudget.maximumSimultaneousPopups -ne 3) -or
        ($visualBudget.maximumSimultaneousOverlays -ne 1) -or
        ($visualBudget.maximumHeadEffectOutlines -ne 3) -or
        ($visualBudget.minimumGraphicalContrast -ne 3.0) -or
        ($visualHierarchyEvidence.minimumObservedForegroundContrast -lt
            $visualBudget.minimumGraphicalContrast)) {
        throw "Visual hierarchy capacity limits drifted from the reviewed product budget."
    }
    $peakPriorityIds = @($visualHierarchyEvidence.priorities | Where-Object {
        $_.tier -eq "peak"
    } | ForEach-Object { $_.id })
    if (($peakPriorityIds -join ",") -ne
        "death-prevention,death,major-achievement,maximum-combo") {
        throw "Peak visual feedback is not reserved for the four approved event classes."
    }
    foreach ($requiredVisualScenario in @("quiet", "busy", "warning", "recovery", "game-over")) {
        $matchingScenarios = @($visualHierarchyEvidence.scenarios | Where-Object {
            $_.id -eq $requiredVisualScenario
        })
        if ($matchingScenarios.Count -ne 1) {
            throw "Visual hierarchy evidence is missing unique scenario: $requiredVisualScenario"
        }

        $scenario = $matchingScenarios[0]
        if ((-not $scenario.snakeHeadReadable) -or
            (-not $scenario.legalMovementSpaceReadable) -or
            (-not $scenario.foodReadable) -or
            (-not $scenario.obstaclesReadable) -or
            (-not $scenario.starvationStateReadable) -or
            (-not $scenario.activeEffectsReadable) -or
            (-not $scenario.contrastQualified) -or
            ($scenario.particleCount -gt $visualBudget.maximumSimultaneousParticles) -or
            ($scenario.shakeSourceCount -gt $visualBudget.maximumSimultaneousShakeSources) -or
            ($scenario.shakeStrength -gt $visualBudget.maximumScreenShakeStrength) -or
            ($scenario.fullScreenFlashCount -gt $visualBudget.maximumSimultaneousFullScreenFlashes) -or
            ($scenario.popupCount -gt $visualBudget.maximumSimultaneousPopups) -or
            ($scenario.overlayCount -gt $visualBudget.maximumSimultaneousOverlays) -or
            ($scenario.width -ne 640) -or
            ($scenario.height -ne 360)) {
            throw "Visual hierarchy scenario failed readability or capacity checks: $requiredVisualScenario"
        }

        $scenarioPath = Join-Path (Split-Path -Parent $visualHierarchyEvidencePath) $scenario.screenshot
        if ((-not (Test-Path -LiteralPath $scenarioPath -PathType Leaf)) -or
            ((Get-Item -LiteralPath $scenarioPath).Length -ne $scenario.pngBytes) -or
            ($scenario.pngBytes -le 1024)) {
            throw "Visual hierarchy screenshot is missing or incomplete: $requiredVisualScenario"
        }
        $pngSignature = [System.IO.File]::ReadAllBytes($scenarioPath)[0..7]
        if (($pngSignature -join ",") -ne "137,80,78,71,13,10,26,10") {
            throw "Visual hierarchy review frame is not a PNG: $requiredVisualScenario"
        }
        $observedScreenshotHash = (Get-FileHash -LiteralPath $scenarioPath -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($observedScreenshotHash -ne $scenario.pngSha256) {
            throw "Visual hierarchy screenshot hash mismatch: $requiredVisualScenario"
        }
    }

    $performanceEvidencePath = Join-Path $repositoryRoot "TestResults/native/performance.json"
    if (-not (Test-Path -LiteralPath $performanceEvidencePath -PathType Leaf)) {
        throw "Godot smoke did not write performance.json evidence."
    }

    $performanceEvidence = Get-Content -LiteralPath $performanceEvidencePath -Raw | ConvertFrom-Json
    if (($performanceEvidence.kind -ne "performance-qualification-v1") -or
        (-not $performanceEvidence.passed) -or
        ($performanceEvidence.minimumHardwareAcceptanceStatus -ne "pending-named-hardware") -or
        ($performanceEvidence.rulesStepsPerProfile -ne 256) -or
        ($performanceEvidence.finalRulesStateHash.Length -ne 16) -or
        (@($performanceEvidence.profiles).Count -ne 3) -or
        (@($performanceEvidence.measurements).Count -ne 3) -or
        (@($performanceEvidence.pendingHumanChecks).Count -ne 4)) {
        throw "Performance evidence did not retain the complete automation and named-hardware handoff."
    }
    foreach ($requiredPerformanceCheck in @(
        "threeEffectProfilesMeasured",
        "maximumMixedStressSceneComplete",
        "frameStatisticsComplete",
        "sharedHostRegressionCeilingMet",
        "particleBudgetConsistent",
        "audioChannelBudgetConsistent",
        "drawSubmissionBudgetMet",
        "feedbackCannotChangeSimulationSpeed",
        "rulesStateIdenticalAcrossProfiles"
    )) {
        if (-not $performanceEvidence.$requiredPerformanceCheck) {
            throw "Performance evidence failed required check: $requiredPerformanceCheck"
        }
    }
    $performanceBudget = $performanceEvidence.budget
    if (($performanceBudget.targetFramesPerSecond -ne 60) -or
        ([Math]::Abs($performanceBudget.targetFrameMilliseconds - (1000.0 / 60.0)) -gt 0.001) -or
        ($performanceBudget.sharedHostMaximumAverageMilliseconds -ne 25.0) -or
        ($performanceBudget.sharedHostMaximumP95Milliseconds -ne 60.0) -or
        ($performanceBudget.maximumLogicalDrawSubmissions -ne 2400) -or
        ($performanceBudget.maximumParticles -ne 160) -or
        ($performanceBudget.maximumAudioChannels -ne 12) -or
        ($performanceBudget.boardCellCapacity -ne 2112) -or
        ($performanceBudget.requiredWarmupFramesPerProfile -ne 30) -or
        ($performanceBudget.requiredSamplesPerProfile -ne 40)) {
        throw "Published performance budgets drifted from the reviewed contract."
    }
    $requiredPerformanceProfiles = @("minimum", "default", "maximum-safe")
    if ((@($performanceEvidence.profiles.id) -join ",") -ne
        ($requiredPerformanceProfiles -join ",") -or
        (@($performanceEvidence.measurements.id) -join ",") -ne
        ($requiredPerformanceProfiles -join ",")) {
        throw "Performance effect profiles are incomplete or out of order."
    }
    foreach ($requiredPerformanceProfile in $requiredPerformanceProfiles) {
        $profile = @($performanceEvidence.profiles | Where-Object {
            $_.id -eq $requiredPerformanceProfile
        })[0]
        $measurement = @($performanceEvidence.measurements | Where-Object {
            $_.id -eq $requiredPerformanceProfile
        })[0]
        if (($profile.logicalDrawSubmissionCount -gt $performanceBudget.maximumLogicalDrawSubmissions) -or
            ($profile.particleCount -gt $performanceBudget.maximumParticles) -or
            ($profile.fullScreenFlashCount -ne 0) -or
            ($measurement.sampleCount -lt $performanceBudget.requiredSamplesPerProfile) -or
            ($measurement.averageFrameMilliseconds -le 0.0) -or
            ($measurement.p50FrameMilliseconds -le 0.0) -or
            ($measurement.p95FrameMilliseconds -lt $measurement.p50FrameMilliseconds) -or
            ($measurement.p99FrameMilliseconds -lt $measurement.p95FrameMilliseconds) -or
            ($measurement.maximumFrameMilliseconds -lt $measurement.p99FrameMilliseconds) -or
            ($measurement.averageFrameMilliseconds -gt $performanceBudget.sharedHostMaximumAverageMilliseconds) -or
            ($measurement.p95FrameMilliseconds -gt $performanceBudget.sharedHostMaximumP95Milliseconds) -or
            ($measurement.driverDrawCallStatus -notin @("observed", "unavailable-headless-backend"))) {
            throw "Performance profile exceeded a capacity or shared-host regression budget: $requiredPerformanceProfile"
        }
    }
    $maximumPerformanceProfile = @($performanceEvidence.profiles | Where-Object {
        $_.id -eq "maximum-safe"
    })[0]
    if (($maximumPerformanceProfile.snakeCellCount -ne 2107) -or
        ($maximumPerformanceProfile.obstacleCount -ne 3) -or
        ($maximumPerformanceProfile.visibleCollectibleCount -ne 2) -or
        (($maximumPerformanceProfile.snakeCellCount +
            $maximumPerformanceProfile.obstacleCount +
            $maximumPerformanceProfile.visibleCollectibleCount) -ne
            $performanceBudget.boardCellCapacity) -or
        ($maximumPerformanceProfile.particleCount -ne $performanceBudget.maximumParticles) -or
        ($maximumPerformanceProfile.popupCount -ne 3) -or
        ($maximumPerformanceProfile.shakeSourceCount -ne 1) -or
        ($maximumPerformanceProfile.shakeStrength -ne 0.35)) {
        throw "Maximum-safe performance stress scene is not complete."
    }

    $vibeLevelEvidencePath = Join-Path $repositoryRoot "TestResults/native/vibe_level.json"
    if (-not (Test-Path -LiteralPath $vibeLevelEvidencePath -PathType Leaf)) {
        throw "Godot smoke did not write vibe_level.json evidence."
    }

    $vibeLevelEvidence = Get-Content -LiteralPath $vibeLevelEvidencePath -Raw | ConvertFrom-Json
    if (($vibeLevelEvidence.kind -ne "vibe-level-qualification-v1") -or
        (-not $vibeLevelEvidence.passed) -or
        ($vibeLevelEvidence.levelCount -ne 5) -or
        ($vibeLevelEvidence.transitionCount -ne 5) -or
        ($vibeLevelEvidence.sceneCount -ne 13) -or
        ($vibeLevelEvidence.accessibilityProfileCount -ne 7) -or
        ($vibeLevelEvidence.humanReviewStatus -ne "pending") -or
        (@($vibeLevelEvidence.levels).Count -ne 5) -or
        (@($vibeLevelEvidence.transitions).Count -ne 5) -or
        (@($vibeLevelEvidence.scenes).Count -ne 13) -or
        (@($vibeLevelEvidence.accessibilityProfiles).Count -ne 7) -or
        (@($vibeLevelEvidence.pendingHumanChecks).Count -ne 4)) {
        throw "Vibe Level evidence did not retain the complete automation and human handoff."
    }
    foreach ($requiredVibeLevelCheck in @(
        "milestonesExact",
        "everyTransitionFiresOnce",
        "singlePresentationAuthority",
        "everyLevelBudgetComplete",
        "criticalGameplayDominant",
        "backgroundContrastQualified",
        "accessibilityProfilesPreserveRulesAndCategory",
        "fixedScenesComplete"
    )) {
        if (-not $vibeLevelEvidence.$requiredVibeLevelCheck) {
            throw "Vibe Level evidence failed required check: $requiredVibeLevelCheck"
        }
    }
    if (($vibeLevelEvidence.minimumObservedForegroundContrast -lt 3.0) -or
        ((@($vibeLevelEvidence.levels.comboThreshold) -join ",") -ne "0,3,5,10,20") -or
        ((@($vibeLevelEvidence.levels.name) -join ",") -ne
            "GROUNDED,FLOW,HEAT,OVERDRIVE,TRANSCENDENT") -or
        (@($vibeLevelEvidence.levels.backgroundRole | Select-Object -Unique).Count -ne 5) -or
        (@($vibeLevelEvidence.levels.hudRole | Select-Object -Unique).Count -ne 5) -or
        ($vibeLevelEvidence.levels[-1].particleBudget -ne 160) -or
        ($vibeLevelEvidence.levels[-1].cameraShakeBudget -ne 0.35)) {
        throw "Vibe Level catalog drifted from the reviewed five-level contract."
    }
    if ((@($vibeLevelEvidence.transitions.sequence) -join ",") -ne "1,2,3,4,5" -or
        ((@($vibeLevelEvidence.transitions | Select-Object -First 4).to) -join ",") -ne
            "flow,heat,overdrive,transcendent" -or
        ($vibeLevelEvidence.transitions[-1].cause -ne "combo-break")) {
        throw "Vibe Level transitions repeated, skipped, or arrived out of order."
    }
    $requiredVibeSceneIds = @(
        "level-grounded",
        "level-flow",
        "level-heat",
        "level-overdrive",
        "level-transcendent",
        "transition-flow",
        "transition-heat",
        "transition-overdrive",
        "transition-transcendent",
        "combo-break",
        "recovery",
        "death-collision",
        "death-starvation"
    )
    if ((@($vibeLevelEvidence.scenes.id) -join ",") -ne ($requiredVibeSceneIds -join ",") -or
        @($vibeLevelEvidence.scenes | Where-Object {
            (-not $_.fatalCellsDominant) -or
            (-not $_.foodDominant) -or
            (-not $_.activePowersDominant) -or
            (-not $_.starvationDominant) -or
            (-not $_.staticSignalPresent)
        }).Count -ne 0) {
        throw "Vibe Level fixed presentation scenes are incomplete."
    }
    $requiredVibeProfiles = @(
        "default",
        "reduced-motion",
        "zero-shake",
        "flash-free",
        "high-contrast",
        "muted",
        "low-particle"
    )
    if ((@($vibeLevelEvidence.accessibilityProfiles.id) -join ",") -ne
        ($requiredVibeProfiles -join ",")) {
        throw "Vibe Level accessibility profiles are incomplete or out of order."
    }
    foreach ($profile in $vibeLevelEvidence.accessibilityProfiles) {
        if ((-not $profile.staticLevelSignalRetained) -or
            (-not $profile.rulesAndScoreCategoryUnchanged) -or
            $profile.fullScreenFlashAllowed -or
            ($profile.zeroShake -and $profile.effectiveCameraShake -ne 0.0) -or
            ($profile.lowParticle -and $profile.effectiveParticleBudget -gt 16)) {
            throw "Vibe Level accessibility profile lost identity or rules isolation: $($profile.id)"
        }
    }

    $radioEvidencePath = Join-Path $repositoryRoot "TestResults/native/radio_behavior.json"
    if (-not (Test-Path -LiteralPath $radioEvidencePath -PathType Leaf)) {
        throw "Godot smoke did not write radio_behavior.json evidence."
    }

    $radioEvidence = Get-Content -LiteralPath $radioEvidencePath -Raw | ConvertFrom-Json
    if (($radioEvidence.kind -ne "radio-behavior-qualification-v1") -or
        (-not $radioEvidence.passed) -or
        ($radioEvidence.validatedManifestStationCount -ne 1) -or
        ($radioEvidence.scenarioStationCount -ne 2) -or
        ($radioEvidence.scenarioTrackCount -ne 6) -or
        (@($radioEvidence.stationIds).Count -ne 2) -or
        (@($radioEvidence.stationIds | Select-Object -Unique).Count -ne 2) -or
        (-not $radioEvidence.missingPackHelp.Contains("core play remains available"))) {
        throw "Radio behavior evidence did not report its complete catalog and fallback contract."
    }
    foreach ($requiredRadioCheck in @(
        "catalogDrivenByValidatedManifests",
        "stationTrackMetadataComplete",
        "packMuteHelpStateComplete",
        "shuffleNoImmediateRepeat",
        "singleTrackEndBehaviorExplicit",
        "stationSwitchComplete",
        "perStationResumeComplete",
        "pauseResumeComplete",
        "endOfTrackAdvanceComplete",
        "missingTrackRecoveryComplete",
        "missingPackGraceful",
        "radioRandomSeparateFromGameplay",
        "keyboardCycleComplete",
        "controllerCycleComplete",
        "decoderAdapterPresent",
        "packagedInventoryAvailable",
        "rulesStateUnchanged"
    )) {
        if (-not $radioEvidence.$requiredRadioCheck) {
            throw "Radio behavior evidence failed required check: $requiredRadioCheck"
        }
    }

    $broadcastEvidencePath = Join-Path $repositoryRoot "TestResults/native/broadcast.json"
    if (-not (Test-Path -LiteralPath $broadcastEvidencePath -PathType Leaf)) {
        throw "Godot smoke did not write broadcast.json evidence."
    }

    $broadcastEvidence = Get-Content -LiteralPath $broadcastEvidencePath -Raw | ConvertFrom-Json
    if (($broadcastEvidence.kind -ne "broadcast-qualification-v1") -or
        (-not $broadcastEvidence.passed) -or
        ($broadcastEvidence.plannedStationCount -ne 8) -or
        ($broadcastEvidence.approvedStationCount -ne 0) -or
        ($broadcastEvidence.allowedBoundaryCount -ne 4) -or
        ($broadcastEvidence.maximumSegmentsPerRun -ne 8) -or
        ($broadcastEvidence.authoredContentReviewStatus -ne
            "pending-no-broadcast-audio-approved") -or
        (@($broadcastEvidence.stations).Count -ne 8) -or
        (@($broadcastEvidence.allowedBoundaries).Count -ne 4) -or
        (@($broadcastEvidence.pendingHumanChecks).Count -ne 4)) {
        throw "Broadcast evidence did not retain the complete policy and content-approval handoff."
    }
    foreach ($requiredBroadcastCheck in @(
        "everyStationIdentityComplete",
        "approvalStateExplicit",
        "radioShuffleBagComplete",
        "trackCooldownComplete",
        "resumeStateRetained",
        "hostBoundariesRestricted",
        "ordinaryComboKeepsTrackContinuous",
        "eventAwareDuckingComplete",
        "criticalCueIntelligibilityProtected",
        "missingFilesRetainCaptions",
        "longSessionFatigueBounded",
        "hostNoRepeatBagComplete",
        "adaptiveLayersRequireSupport",
        "radioRandomSeparateFromGameplay",
        "rulesStateUnchanged"
    )) {
        if (-not $broadcastEvidence.$requiredBroadcastCheck) {
            throw "Broadcast evidence failed required check: $requiredBroadcastCheck"
        }
    }
    $requiredBroadcastStations = @(
        "flow_signal",
        "chaos_theory",
        "global_coil",
        "ourotron",
        "the_pit",
        "the_bureau",
        "the_strike",
        "underground_scales"
    )
    if ((@($broadcastEvidence.stations.stationId) -join ",") -ne
        ($requiredBroadcastStations -join ",") -or
        (@($broadcastEvidence.stations.stationName | Select-Object -Unique).Count -ne 8) -or
        (@($broadcastEvidence.stations.hostName | Select-Object -Unique).Count -ne 8) -or
        (@($broadcastEvidence.stations.visualIdentity | Select-Object -Unique).Count -ne 8)) {
        throw "Broadcast station identity catalog is incomplete or duplicated."
    }
    foreach ($station in $broadcastEvidence.stations) {
        if ([string]::IsNullOrWhiteSpace($station.musicalInclusionRule) -or
            [string]::IsNullOrWhiteSpace($station.hostPerspective) -or
            [string]::IsNullOrWhiteSpace($station.coilRelationship) -or
            (@($station.shortIds).Count -ne 3) -or
            (@($station.shortIds | Select-Object -Unique).Count -ne 3) -or
            (@($station.transitionStingers).Count -ne 4) -or
            ($station.approval -ne "planned-unapproved") -or
            $station.supportsAdaptiveLayers) {
            throw "Broadcast station lacks complete unapproved identity metadata: $($station.stationId)"
        }
    }
    if ((@($broadcastEvidence.allowedBoundaries) -join ",") -ne
        "RunStart,MajorMilestone,Recovery,PostRun") {
        throw "Broadcast host/lore boundaries drifted from the reviewed contract."
    }

    $modeEvidencePath = Join-Path $repositoryRoot "TestResults/native/mode_contracts.json"
    if (-not (Test-Path -LiteralPath $modeEvidencePath -PathType Leaf)) {
        throw "Godot smoke did not write mode_contracts.json evidence."
    }

    $modeEvidence = Get-Content -LiteralPath $modeEvidencePath -Raw | ConvertFrom-Json
    if (($modeEvidence.kind -ne "mode-contract-qualification-v2") -or
        (-not $modeEvidence.passed) -or
        ($modeEvidence.modeCount -ne 2) -or
        ($modeEvidence.configHashAlgorithm -ne "sha256-canonical-runconfig-v3") -or
        ($modeEvidence.adaptiveImplementationStatus -ne
            "enabled-bounded-vibe-default-with-opt-out") -or
        (@($modeEvidence.modes).Count -ne 2)) {
        throw "Mode-contract evidence did not retain the complete Classic/Vibe boundary."
    }
    foreach ($requiredModeCheck in @(
        "stableIdentitiesComplete",
        "descriptionsComplete",
        "scoreCategoriesSeparated",
        "boardRulesExact",
        "pauseRulesExact",
        "seedRulesExact",
        "restartRulesExact",
        "classicFeatureBoundaryExact",
        "vibeFeatureBoundaryExact",
        "classicStarvationDisabled",
        "classicPowerSpawningDisabled",
        "classicMinimalScoreExact",
        "vibePressureAndScoringActive",
        "restartRetainsModeAndBoard",
        "keyboardAndControllerSelectionRoutesComplete",
        "deterministicPerMode",
        "crossModeScoreIsolation",
        "vibeAdaptationDefaultEnabled",
        "vibeOptOutScoreIsolation"
    )) {
        if (-not $modeEvidence.$requiredModeCheck) {
            throw "Mode-contract evidence failed required check: $requiredModeCheck"
        }
    }
    if ((@($modeEvidence.modes.id) -join ",") -ne "classic,vibe" -or
        (@($modeEvidence.modes.contractId) -join ",") -ne "classic@1,vibe@1" -or
        (@($modeEvidence.modes.scoreCategoryId) -join ",") -ne
            "classic-standard-v1,vibe-standard-v1-dda-on" -or
        (@($modeEvidence.modes.boardWidth | Select-Object -Unique) -join ",") -ne "64" -or
        (@($modeEvidence.modes.boardHeight | Select-Object -Unique) -join ",") -ne "33") {
        throw "Mode identities, score categories, or board contracts drifted."
    }

    $adaptiveEvidencePath = Join-Path $repositoryRoot "TestResults/native/adaptive_fairness.json"
    if (-not (Test-Path -LiteralPath $adaptiveEvidencePath -PathType Leaf)) {
        throw "Godot smoke did not write adaptive_fairness.json evidence."
    }

    $adaptiveEvidence = Get-Content -LiteralPath $adaptiveEvidencePath -Raw | ConvertFrom-Json
    if (($adaptiveEvidence.kind -ne "adaptive-fairness-qualification-v1") -or
        (-not $adaptiveEvidence.passed) -or
        ($adaptiveEvidence.policyId -ne "vibe-bounded-hunger-v1") -or
        ($adaptiveEvidence.minimumHungerDrainTicks -ne 0) -or
        ($adaptiveEvidence.maximumHungerDrainTicks -ne 2) -or
        ((@($adaptiveEvidence.scoreCategories) -join ",") -ne
            "classic-standard-v1,vibe-standard-v1-dda-on,vibe-standard-v1-dda-off")) {
        throw "Adaptive-fairness evidence did not retain its policy, bounds, or score categories."
    }
    foreach ($requiredAdaptiveCheck in @(
        "classicAlwaysDisabled",
        "vibeEnabledByDefault",
        "optOutPreferenceRoundTrips",
        "optOutSettingHasKeyboardAndControllerRoutes",
        "enabledAndDisabledScoresIsolated",
        "scoreMetadataExplicit",
        "stateInputsClosed",
        "hungerDrainBoundsExact",
        "supportStateExact",
        "standardStateExact",
        "pressureStateExact",
        "deterministicHashesExact",
        "achievementModeEligibilityExplicit"
    )) {
        if (-not $adaptiveEvidence.$requiredAdaptiveCheck) {
            throw "Adaptive-fairness evidence failed required check: $requiredAdaptiveCheck"
        }
    }

    $inputCadenceEvidencePath = Join-Path $repositoryRoot "TestResults/native/input_cadence.json"
    if (-not (Test-Path -LiteralPath $inputCadenceEvidencePath -PathType Leaf)) {
        throw "Godot smoke did not write input_cadence.json evidence."
    }

    $inputCadenceEvidence = Get-Content -LiteralPath $inputCadenceEvidencePath -Raw | ConvertFrom-Json
    if (($inputCadenceEvidence.kind -ne "input-cadence-qualification-v1") -or
        (-not $inputCadenceEvidence.passed) -or
        ($inputCadenceEvidence.deviceClassCount -ne 3) -or
        ($inputCadenceEvidence.cadenceProfileCount -ne 3) -or
        ($inputCadenceEvidence.inputCount -ne 5) -or
        (-not $inputCadenceEvidence.passiveStickDriftRejected) -or
        (@($inputCadenceEvidence.cases).Count -ne 9)) {
        throw "Input cadence evidence did not report the complete qualified matrix."
    }
    foreach ($deviceClass in @("keyboard", "dpad", "stick")) {
        foreach ($cadenceProfile in @("low-render-rate", "normal-render-rate", "stressed-render-rate")) {
            $matchingCases = @($inputCadenceEvidence.cases | Where-Object {
                ($_.deviceClass -eq $deviceClass) -and ($_.cadenceProfile -eq $cadenceProfile)
            })
            if ($matchingCases.Count -ne 1) {
                throw "Input cadence evidence is missing unique case: $deviceClass/$cadenceProfile"
            }

            $inputCase = $matchingCases[0]
            if (($inputCase.acceptedInputCount -ne 5) -or
                ($inputCase.rejectedInputCount -ne 0) -or
                ($inputCase.rulesStepCount -ne 5) -or
                ($inputCase.pendingDirectionCount -ne 0) -or
                (@($inputCase.consumedDirections).Count -ne 5) -or
                ($inputCase.finalStateHash -ne $inputCadenceEvidence.expectedFinalStateHash)) {
                throw "Input cadence evidence failed exact consumption: $deviceClass/$cadenceProfile"
            }
        }
    }

    $mouseEvidencePath = Join-Path $repositoryRoot "TestResults/native/mouse_input.json"
    if (-not (Test-Path -LiteralPath $mouseEvidencePath -PathType Leaf)) {
        throw "Godot smoke did not write mouse_input.json evidence."
    }
    $mouseEvidence = Get-Content -LiteralPath $mouseEvidencePath -Raw | ConvertFrom-Json
    $expectedMouseTargets = @(
        "start",
        "customize",
        "achievements",
        "scores",
        "spectator",
        "replays",
        "settings",
        "help",
        "quit"
    )
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
        ((@($mouseEvidence.menuTargets) -join ',') -ne ($expectedMouseTargets -join ',')) -or
        (@($mouseEvidence.pendingHumanChecks).Count -ne 2)) {
        throw "Mouse input evidence did not report the complete native pointer route."
    }

    $presentationFrameEvidencePath = Join-Path $repositoryRoot "TestResults/native/presentation_frames.json"
    if (-not (Test-Path -LiteralPath $presentationFrameEvidencePath -PathType Leaf)) {
        throw "Godot smoke did not write presentation_frames.json evidence."
    }

    $presentationFrameEvidence = Get-Content -LiteralPath $presentationFrameEvidencePath -Raw | ConvertFrom-Json
    if (($presentationFrameEvidence.kind -ne "presentation-frame-evidence-v1") -or
        ($presentationFrameEvidence.sampleCount -lt 40) -or
        ($presentationFrameEvidence.p95Milliseconds -gt 60.0) -or
        ($presentationFrameEvidence.maxMilliseconds -gt 100.0)) {
        throw "Host smoke presentation frames exceeded the bare-loop handoff budget."
    }

    $bareLoopEvidencePath = Join-Path $repositoryRoot "TestResults/native/bare_arcade_loop.json"
    if (-not (Test-Path -LiteralPath $bareLoopEvidencePath -PathType Leaf)) {
        throw "Godot smoke did not write bare_arcade_loop.json evidence."
    }

    $bareLoopEvidence = Get-Content -LiteralPath $bareLoopEvidencePath -Raw | ConvertFrom-Json
    if (($bareLoopEvidence.kind -ne "bare-arcade-loop-qualification-v1") -or
        (-not $bareLoopEvidence.passed) -or
        ($bareLoopEvidence.humanFeelReviewStatus -ne "pending") -or
        (@($bareLoopEvidence.frames).Count -ne 6) -or
        (@($bareLoopEvidence.pendingHumanChecks).Count -ne 4)) {
        throw "Bare arcade loop evidence did not retain the complete automation and human handoff."
    }
    foreach ($requiredBareLoopCheck in @(
        "optionalContentAbsent",
        "progressionPromptsAbsent",
        "minimumEffectsProfile",
        "inputResponseComplete",
        "bufferOrderingComplete",
        "fatalCellVisibilityComplete",
        "headFoodContrastComplete",
        "wrapContinuityComplete",
        "framePacingComplete",
        "deathAttributionComplete",
        "restartIntentComplete",
        "stateResetComplete",
        "crossAspectAccessibilityFramesComplete",
        "experienceHandoffComplete"
    )) {
        if (-not $bareLoopEvidence.$requiredBareLoopCheck) {
            throw "Bare arcade loop evidence failed required check: $requiredBareLoopCheck"
        }
    }
    foreach ($requiredFrame in @("quiet", "wrap", "long-body", "collision", "game-over", "restart")) {
        $matchingFrames = @($bareLoopEvidence.frames | Where-Object { $_.id -eq $requiredFrame })
        if ($matchingFrames.Count -ne 1 -or
            (-not $matchingFrames[0].visibilityQualified) -or
            (-not $matchingFrames[0].criticalTextPresent)) {
            throw "Bare arcade loop evidence is missing a qualified semantic frame: $requiredFrame"
        }
    }
    if (($bareLoopEvidence.budgets.observedInputResponseRulesSteps -gt 1) -or
        ($bareLoopEvidence.budgets.headFoodContrast -lt 3.0) -or
        ($bareLoopEvidence.budgets.observedDeathAttributionRulesSteps -gt 0) -or
        ($bareLoopEvidence.budgets.observedRestartInputSequenceDelta -lt 1) -or
        ($bareLoopEvidence.budgets.observedResetResidualTransientCount -ne 0)) {
        throw "Bare arcade loop evidence exceeded a response, contrast, death, restart, or reset budget."
    }

    $feedbackMatrixEvidencePath = Join-Path $repositoryRoot "TestResults/native/feedback_matrix.json"
    if (-not (Test-Path -LiteralPath $feedbackMatrixEvidencePath -PathType Leaf)) {
        throw "Godot smoke did not write feedback_matrix.json evidence."
    }

    $feedbackMatrixEvidence = Get-Content -LiteralPath $feedbackMatrixEvidencePath -Raw | ConvertFrom-Json
    if (($feedbackMatrixEvidence.kind -ne "feedback-matrix-qualification-v1") -or
        (-not $feedbackMatrixEvidence.passed) -or
        ($feedbackMatrixEvidence.runEventCount -ne 19) -or
        ($feedbackMatrixEvidence.uiActionCount -ne 15) -or
        ($feedbackMatrixEvidence.entryCount -ne 34) -or
        (@($feedbackMatrixEvidence.entries).Count -ne 34) -or
        ($feedbackMatrixEvidence.unusedShippedAssetCount -ne 0)) {
        throw "Feedback matrix evidence did not report its complete trigger and asset contract."
    }
    foreach ($requiredFeedbackCheck in @(
        "everyTriggerMapped",
        "everyDominantCueDeclared",
        "everyAccessibilityAlternativeDeclared",
        "everyAudioCueAccountedFor",
        "stackInterruptionPolicyComplete",
        "authoredAbsenceExplicit",
        "flashPolicySafe",
        "hapticMetadataComplete"
    )) {
        if (-not $feedbackMatrixEvidence.$requiredFeedbackCheck) {
            throw "Feedback matrix evidence failed required check: $requiredFeedbackCheck"
        }
    }
    $feedbackTriggerKeys = @($feedbackMatrixEvidence.entries | ForEach-Object {
        "$($_.domain)|$($_.triggerId)"
    })
    if (@($feedbackTriggerKeys | Select-Object -Unique).Count -ne 34) {
        throw "Feedback matrix evidence contains a duplicate trigger row."
    }

    $sfxEvidencePath = Join-Path $repositoryRoot "TestResults/native/sfx_catalog.json"
    if (-not (Test-Path -LiteralPath $sfxEvidencePath -PathType Leaf)) {
        throw "Godot smoke did not write sfx_catalog.json evidence."
    }

    $sfxEvidence = Get-Content -LiteralPath $sfxEvidencePath -Raw | ConvertFrom-Json
    if (($sfxEvidence.kind -ne "sfx-catalog-qualification-v1") -or
        (-not $sfxEvidence.passed) -or
        ($sfxEvidence.cueCount -ne 31) -or
        ($sfxEvidence.approvedAuthoredAssetCount -ne 0) -or
        (@($sfxEvidence.entries).Count -ne 31) -or
        ($sfxEvidence.authoredAssetReviewStatus -ne "pending-no-authored-sfx-approved")) {
        throw "SFX catalog evidence did not report its complete fallback and authored-asset contract."
    }
    foreach ($requiredSfxCheck in @(
        "catalogComplete",
        "everyCueConnected",
        "everyCueLicensed",
        "generationCandidatesExcluded",
        "peakPolicyComplete",
        "noClipping",
        "noDuplicateFingerprints",
        "menuNavigationDistinct",
        "comboTiersDistinct",
        "comboBreakDistinct",
        "powerActivationsDistinct",
        "achievementDistinct",
        "restartDistinct",
        "deathCausesDistinct",
        "rulesStateIndependent"
    )) {
        if (-not $sfxEvidence.$requiredSfxCheck) {
            throw "SFX catalog evidence failed required check: $requiredSfxCheck"
        }
    }
    if (@($sfxEvidence.entries.runtimeId | Select-Object -Unique).Count -ne 31 -or
        @($sfxEvidence.entries.measurement.pcmSha256 | Select-Object -Unique).Count -ne 31 -or
        @($sfxEvidence.entries | Where-Object {
            $_.measurement.peakDecibelsFullScale -lt -24.5 -or
            $_.measurement.peakDecibelsFullScale -gt -18.0
        }).Count -ne 0) {
        throw "SFX catalog evidence contains duplicate or out-of-policy procedural cues."
    }

    $audioEvidencePath = Join-Path $repositoryRoot "TestResults/native/audio_fallback_stress.json"
    if (-not (Test-Path -LiteralPath $audioEvidencePath -PathType Leaf)) {
        throw "Godot smoke did not write audio_fallback_stress.json evidence."
    }

    $audioEvidence = Get-Content -LiteralPath $audioEvidencePath -Raw | ConvertFrom-Json
    if (($audioEvidence.schemaVersion -ne 2) -or
        ($audioEvidence.kind -ne "audio-mixing-policy-v2") -or
        -not $audioEvidence.passed) {
        throw "Audio mixing evidence did not report the qualified schema and pass state."
    }
    if (($audioEvidence.cueCount -ne 31) -or
        ($audioEvidence.rapidRetriggerAttempts -lt 512) -or
        ($audioEvidence.mutedPathChecks -ne $audioEvidence.cueCount)) {
        throw "Audio fallback stress evidence did not exercise the complete cue catalog."
    }
    foreach ($requiredAudioCheck in @(
        "policyCatalogComplete",
        "busRoutingObserved",
        "cooldownPolicyObserved",
        "polyphonyPolicyObserved",
        "priorityPolicyObserved",
        "interruptionPolicyObserved",
        "musicDuckPolicyObserved",
        "musicDuckRestorationObserved",
        "busIsolationObserved",
        "unitTestableWithoutPlayback",
        "engineMusicDuckObserved",
        "engineMusicDuckRestored",
        "savedVolumesImmediateAndIsolated",
        "voiceCapacityBounded",
        "outputDevicePollingActive",
        "deviceChangeRecoveryObserved",
        "missingBusFailureObserved",
        "backoffObserved",
        "recoveryObserved",
        "cacheBounded",
        "cleanupObserved",
        "rulesStateUnchanged"
    )) {
        if (-not $audioEvidence.$requiredAudioCheck) {
            throw "Audio fallback stress evidence failed required check: $requiredAudioCheck"
        }
    }
    if (($audioEvidence.sfxBusCapacity -ne 8) -or
        ($audioEvidence.uiBusCapacity -ne 4) -or
        ($audioEvidence.peakVoiceCount -gt 12) -or
        ($audioEvidence.cooldownSuppressions -lt 1) -or
        ($audioEvidence.mutedSuppressions -ne $audioEvidence.cueCount)) {
        throw "Audio mixing evidence did not prove bounded production allocation behavior."
    }

    $coreOnlyEvidencePath = Join-Path $repositoryRoot "TestResults/native/core_only_offline.json"
    if (-not (Test-Path -LiteralPath $coreOnlyEvidencePath -PathType Leaf)) {
        throw "Godot smoke did not write core_only_offline.json evidence."
    }

    $coreOnlyEvidence = Get-Content -LiteralPath $coreOnlyEvidencePath -Raw | ConvertFrom-Json
    if ($coreOnlyEvidence.kind -ne "core-only-offline-v1" -or -not $coreOnlyEvidence.passed) {
        throw "Core-only offline evidence did not report the qualified schema and pass state."
    }
    foreach ($requiredCoreOnlyCheck in @(
        "coreOnlyReady",
        "optionalAbsenceNormal",
        "optionalRemovalIsolated",
        "tamperIsolated",
        "incompatibilityIsolated",
        "duplicateIsolated",
        "removalRequiresExplicitConfirmation",
        "removalCancelPreservesPack",
        "removalConfirmIsTargeted",
        "playerDataRemovalSeparated",
        "installedOptionalValidated",
        "installedAssetReadValidated",
        "removalQuarantinedRecoverably",
        "quarantineRediscovered",
        "restoreRevalidated",
        "playerDataPreservedByFilesystemLifecycle",
        "fullOfflineFlowExercised"
    )) {
        if (-not $coreOnlyEvidence.$requiredCoreOnlyCheck) {
            throw "Core-only offline evidence failed required check: $requiredCoreOnlyCheck"
        }
    }
    if (($coreOnlyEvidence.acceptedOptionalBeforeRemoval -ne 1) -or
        ($coreOnlyEvidence.acceptedOptionalAfterRemoval -ne 0)) {
        throw "Core-only offline evidence did not prove optional-pack add/removal isolation."
    }
    foreach ($requiredOfflineFlow in @(
        "launch",
        "menu",
        "run",
        "critical-feedback",
        "settings",
        "content-packs",
        "death",
        "restart",
        "recovery"
    )) {
        if ($requiredOfflineFlow -notin @($coreOnlyEvidence.exercisedFlows)) {
            throw "Core-only offline evidence is missing flow: $requiredOfflineFlow"
        }
    }

    $replayDirectory = Join-Path $smokeUserDataRoot "replays"
    $storedReplays = @(Get-ChildItem -LiteralPath $replayDirectory -File -Filter "*.vibesnake-replay.json")
    # Storage smoke plus the death-restart terminal path each save a replay.
    if ($storedReplays.Count -lt 1 -or $storedReplays.Count -gt 4) {
        throw "Godot smoke replay count out of range: $($storedReplays.Count) (expected 1-4)."
    }
    if (Get-ChildItem -LiteralPath $replayDirectory -File -Filter "*.tmp-*" | Select-Object -First 1) {
        throw "Godot smoke left an incomplete atomic replay file."
    }

    $replayBrowserEvidencePath = Join-Path $repositoryRoot "TestResults/native/replay_browser.json"
    if (-not (Test-Path -LiteralPath $replayBrowserEvidencePath -PathType Leaf)) {
        throw "Godot smoke did not write replay_browser.json evidence."
    }
    $replayBrowserEvidence = Get-Content -LiteralPath $replayBrowserEvidencePath -Raw | ConvertFrom-Json
    if (($replayBrowserEvidence.kind -ne "replay-browser-qualification-v2") -or
        (-not $replayBrowserEvidence.passed) -or
        ($replayBrowserEvidence.browserEntryFieldCount -ne 14) -or
        ((@($replayBrowserEvidence.playbackSpeeds) -join ",") -ne "0.5,1,2,4")) {
        throw "Replay-browser evidence identity, metadata shape, or speed set drifted."
    }
    foreach ($requiredReplayBrowserCheck in @(
        "metadataComplete",
        "explicitStateBadgesComplete",
        "rawKeyboardRouteComplete",
        "rawControllerRouteComplete",
        "speedControlsComplete",
        "hudToggleComplete",
        "pauseStepRestartReturnComplete",
        "atomicExportComplete",
        "deleteConsentComplete",
        "deleteCancelLossless",
        "confirmedDeleteExact",
        "exportsPreservedAfterDelete",
        "progressionIsolated"
    )) {
        if (-not $replayBrowserEvidence.$requiredReplayBrowserCheck) {
            throw "Replay-browser evidence failed required check: $requiredReplayBrowserCheck"
        }
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
