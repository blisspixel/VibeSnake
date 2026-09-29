using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace RepositoryChecks;

internal static class ReleaseMatrixCheck
{
    internal const int DefaultMaximumJsonBytes = 4 * 1024 * 1024;

    private const int MaximumDepth = 64;
    private const double TargetFrameMilliseconds = 1000d / 60d;
    private const double FrameTolerance = 0.001d;

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private static readonly JsonSerializerOptions RenderOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true,
        IndentCharacter = ' ',
        IndentSize = 2,
    };
    private static readonly JsonDocumentOptions DocumentOptions = new()
    {
        AllowTrailingCommas = false,
        CommentHandling = JsonCommentHandling.Disallow,
        MaxDepth = MaximumDepth,
    };
    private static readonly JsonReaderOptions ReaderOptions = new()
    {
        AllowTrailingCommas = false,
        CommentHandling = JsonCommentHandling.Disallow,
        MaxDepth = MaximumDepth,
    };
    private static readonly Regex RevisionPattern = new(
        "^[0-9a-f]{40}$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex Sha256Pattern = new(
        "^[0-9a-f]{64}$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex StateHashPattern = new(
        "^[0-9a-f]{16}$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly JsonElement None = ParseElement("null");
    private static readonly JsonElement Zero = ParseElement("0");
    private static readonly JsonElement False = ParseElement("false");
    private static readonly JsonElement ExpectedPreferenceMigrations = ParseElement(
        """
        [
          {"inputSchema":1,"effectiveSchema":7,"loadCode":"Success","sourcePreserved":true},
          {"inputSchema":2,"effectiveSchema":7,"loadCode":"Success","sourcePreserved":true},
          {"inputSchema":3,"effectiveSchema":7,"loadCode":"Success","sourcePreserved":true},
          {"inputSchema":4,"effectiveSchema":7,"loadCode":"Success","sourcePreserved":true},
          {"inputSchema":5,"effectiveSchema":7,"loadCode":"Success","sourcePreserved":true},
          {"inputSchema":6,"effectiveSchema":7,"loadCode":"Success","sourcePreserved":true}
        ]
        """);
    private static readonly JsonElement ExpectedAdditionalMigrations = ParseElement(
        """
        [
          {"fixture":"personal-best-schema-1","effectiveSchema":2,"loadCode":"Success","sourcePreserved":true},
          {"fixture":"local-playtest-summary-schema-1","effectiveSchema":2,"loadCode":"Success","sourcePreserved":true}
        ]
        """);

    private static readonly string[] Platforms =
    [
        "windows-x64",
        "macos-universal",
        "linux-x64",
    ];
    private static readonly EvidenceFile[] EvidenceFiles =
    [
        new("readOnly", "artifact_read_only_install.json"),
        new("dependencies", "dependency_inventory.json"),
        new("signing", "release_signing_readiness.json"),
        new("output", "release_output_plan.json"),
        new("reliability", "candidate_reliability.json"),
        new("faults", "candidate_fault_campaign.json"),
        new("performance", "performance.json"),
        new("accessibility", "candidate_accessibility_audit.json"),
    ];
    private static readonly string[] ReadOnlyTrueFields =
    [
        "passed",
        "writeProbeRejected",
        "installUnchanged",
        "userDataOutsideInstall",
        "logOutsideInstall",
        "evidenceOutsideInstall",
        "installPathQualified",
        "userDataPathQualified",
        "logPathQualified",
        "freshProfile",
    ];
    private static readonly string[] OutputTrueFields =
    [
        "passed",
        "qualificationOnly",
        "assemblyEligible",
        "optionalPackOutputSeparate",
        "playerDataExcluded",
        "uninstallPreservesPlayerData",
        "deterministicRepeatMatched",
    ];
    private static readonly string[] SimulationTrueFields =
    [
        "decisionsIdentical",
        "queueOutcomesIdentical",
        "stepResultsIdentical",
    ];
    private static readonly string[] SpectatorTrueFields =
    [
        "everyFreshSessionStartedPaused",
        "everyFreshSessionResetState",
        "everySessionAdvanced",
        "engineNodeCountStable",
        "engineObjectCountDidNotGrow",
        "engineResourceCountDidNotGrow",
        "engineOrphanNodeCountDidNotGrow",
        "noMonotonicStateOrResourceGrowth",
    ];
    private static readonly string[] ResourceCountFields =
    [
        "sceneNodeCount",
        "objectCount",
        "resourceCount",
        "orphanNodeCount",
    ];
    private static readonly string[] RetainedReleaseGate =
    [
        "retained-release-execution-on-windows-macos-linux",
    ];
    private static readonly string[] FaultIds =
    [
        "interrupted-write",
        "corrupt-json",
        "full-disk",
        "read-only-data-directory",
        "missing-resource",
        "invalid-content-pack",
        "unavailable-audio",
    ];
    private static readonly string[] FaultCampaignTrueFields =
    [
        "everyFaultDetected",
        "everyExistingDataBoundaryPreserved",
        "everyRecoveryPathVerified",
        "rulesStateUnchangedAcrossCampaign",
    ];
    private static readonly string[] FaultRowTrueFields =
    [
        "faultDetected",
        "existingDataPreserved",
        "recoveryVerified",
        "rulesStateUnchanged",
    ];
    private static readonly string[] TriageTrueFields =
    [
        "reportRetained",
        "schemaValid",
        "privacySafe",
        "reproductionFieldsComplete",
    ];
    private static readonly string[] PerformanceTrueFields =
    [
        "threeEffectProfilesMeasured",
        "maximumMixedStressSceneComplete",
        "frameStatisticsComplete",
        "sharedHostRegressionCeilingMet",
        "particleBudgetConsistent",
        "audioChannelBudgetConsistent",
        "drawSubmissionBudgetMet",
        "feedbackCannotChangeSimulationSpeed",
        "rulesStateIdenticalAcrossProfiles",
    ];
    private static readonly string[] ProfileIds = ["minimum", "default", "maximum-safe"];
    private static readonly string[] ProfileShapeFields =
    [
        "snakeCellCount",
        "obstacleCount",
        "visibleCollectibleCount",
        "particleCount",
        "popupCount",
        "fullScreenFlashCount",
        "logicalDrawSubmissionCount",
    ];
    private static readonly Dictionary<string, int[]> ProfileShapes = new(StringComparer.Ordinal)
    {
        ["minimum"] = [64, 0, 2, 0, 0, 0, 88],
        ["default"] = [512, 3, 2, 64, 2, 0, 610],
        ["maximum-safe"] = [2107, 3, 2, 160, 3, 0, 2303],
    };
    private static readonly string[] TimingFields =
    [
        "averageFrameMilliseconds",
        "p50FrameMilliseconds",
        "p95FrameMilliseconds",
        "p99FrameMilliseconds",
        "maximumFrameMilliseconds",
    ];
    private static readonly string[] DriverStatuses = ["observed", "unavailable-headless-backend"];
    private static readonly string[] AccessibilityTrueFields =
    [
        "allAutomatedAuditAreasPassed",
        "keyboardOnlyRouteComplete",
        "controllerOnlyRouteComplete",
        "remappingComplete",
        "singleActionNavigationComplete",
        "independentAudioControlsComplete",
        "monoOutputComplete",
        "visualAlternativesComplete",
        "reducedMotionComplete",
        "flashSafetyComplete",
        "maximumTextScaleViewportMatrixComplete",
    ];
    private static readonly string[] AccessibilityAreas =
    [
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
        "documentation",
    ];
    private static readonly DisplayRow[] DisplayRows =
    [
        new("minimum-clamp", 320, 180, 640, 360),
        new("hd-16-9", 1920, 1080, 1920, 1080),
        new("classic-4-3", 1024, 768, 1024, 768),
        new("desktop-16-10", 1920, 1200, 1920, 1200),
        new("ultrawide-21-9", 3440, 1440, 3440, 1440),
        new("square-1-1", 1024, 1024, 1024, 1024),
        new("high-density-4k", 3840, 2160, 3840, 2160),
        new("high-density-5k", 5120, 2880, 5120, 2880),
    ];
    private static readonly SourceFile[] AccessibilitySources =
    [
        new("accessibility_presentation.json", "accessibility-presentation-v1"),
        new("shell_presentation.json", "shell-presentation-v1"),
        new("settings_screen.json", "settings-screen-qualification-v1"),
        new("input_cadence.json", "input-cadence-qualification-v1"),
        new("audio_fallback_stress.json", "audio-mixing-policy-v2"),
        new("multimodal_feedback.json", "multimodal-feedback-v1"),
        new("viewport_matrix.json", "virtual-viewport-matrix-v1"),
    ];
    private static readonly string[] AccessibilityPendingChecks =
    [
        "retained-visible-audit-windows-macos-linux",
        "maximum-text-scale-platform-captures",
        "physical-keyboard-and-controller-only-flow-review",
        "players-using-relevant-accessibility-settings",
        "human-focus-contrast-readability-photosensitivity-review",
    ];
    private static readonly string[] LifecycleTrueFields =
    [
        "passed",
        "firstInstallPassed",
        "readOnlyInstallPassed",
        "noElevationRequested",
        "nonAsciiInstallAndUserPathsPassed",
        "repairSnapshotMatched",
        "repairLaunchPassed",
        "futureSchemaRejectedAndPreserved",
        "rollbackNeverOverwritesNewerPreferences",
        "optionalPackAddRemovalRestorePassed",
        "dataResetBackupRestorePassed",
        "applicationRemovalPreservedPlayerData",
        "completeSupportedSaveFixtureMatrix",
    ];
    private static readonly string[] LifecycleRemainingGates =
    [
        "selected-channel-installer-lifecycle",
        "cross-version-binary-rollback",
    ];
    private static readonly string[] ExpectedModeIds = ["classic", "vibe"];
    private static readonly Dictionary<string, string> ExpectedModes = new(StringComparer.Ordinal)
    {
        ["classic"] = "classic-standard-v1",
        ["vibe"] = "vibe-standard-v1-dda-on",
    };
    private static readonly string[] RemainingProtectedOperations =
    [
        "windows-signing-verification",
        "macos-signing-notarization-stapling-verification",
        "linux-runtime-baseline-and-desktop-integration",
        "selected-channel-installer-lifecycle",
        "cross-version-binary-rollback",
        "named-minimum-hardware-performance-acceptance",
        "retained-accessibility-audit-and-user-review",
        "final-provenance",
        "channel-approval",
    ];

    internal readonly record struct Qualification(bool Passed, IReadOnlyList<string> Errors, string Json);

    internal static Qualification Qualify(string downloadRoot, string expectedRevision, string expectedBuildMode) =>
        Qualify(downloadRoot, expectedRevision, expectedBuildMode, DefaultMaximumJsonBytes);

    internal static Qualification Qualify(
        string downloadRoot,
        string expectedRevision,
        string expectedBuildMode,
        int maximumJsonBytes)
    {
        var errors = new List<string>();
        var owned = new List<JsonDocument>();
        try
        {
            var json = Build(downloadRoot, expectedRevision, expectedBuildMode, maximumJsonBytes, errors, owned);
            return new Qualification(errors.Count == 0, errors, json);
        }
        finally
        {
            foreach (var document in owned)
            {
                document.Dispose();
            }
        }
    }

    private static string Build(
        string downloadRoot,
        string expectedRevision,
        string expectedBuildMode,
        int maximumJsonBytes,
        List<string> errors,
        List<JsonDocument> owned)
    {
        downloadRoot ??= string.Empty;
        expectedRevision ??= string.Empty;
        expectedBuildMode ??= string.Empty;
        if (!RevisionPattern.IsMatch(expectedRevision))
        {
            errors.Add("expected revision must be a lowercase 40-character Git revision");
        }

        if (expectedBuildMode is not ("Debug" or "Release"))
        {
            errors.Add("expected build mode must be Debug or Release");
        }

        var context = new MatrixContext(downloadRoot, expectedRevision, expectedBuildMode, maximumJsonBytes, errors, owned);
        foreach (var platform in Platforms)
        {
            ValidatePlatform(context, platform);
        }

        if (context.Rows.Count != Platforms.Length)
        {
            errors.Add(
                "release matrix must contain exactly "
                + Platforms.Length.ToString(CultureInfo.InvariantCulture)
                + " complete platform rows");
        }

        if (context.SmokeHashes.Count != 1)
        {
            errors.Add("all platform artifacts must report one identical smoke state hash");
        }

        if (context.LockHashes.Count != 1)
        {
            errors.Add("all platform dependency inventories must report one lock-set SHA-256");
        }

        if (context.ProductVersions.Count != 1)
        {
            errors.Add("all platform output plans must report one product version");
        }

        foreach (var (mode, hashes) in context.TraceHashes)
        {
            if (hashes.Count != 1)
            {
                errors.Add($"all platform reliability rows must report one {mode} trace SHA-256");
            }
        }

        if (context.RulesHashes.Count != 1)
        {
            errors.Add("all platform performance rows must report one rules state hash");
        }

        var traces = new JsonObject();
        foreach (var (mode, hashes) in context.TraceHashes)
        {
            traces[mode] = OnlyNode(hashes);
        }

        var platforms = new JsonArray();
        foreach (var row in context.Rows)
        {
            platforms.Add(row.Json);
        }

        var root = new JsonObject
        {
            ["schemaVersion"] = 1,
            ["kind"] = "release-matrix-qualification-v1",
            ["passed"] = errors.Count == 0,
            ["sourceRevision"] = expectedRevision,
            ["buildMode"] = expectedBuildMode,
            ["platforms"] = platforms,
            ["sharedSmokeStateHash"] = OnlyNode(context.SmokeHashes),
            ["sharedLockSetSha256"] = OnlyNode(context.LockHashes),
            ["productVersion"] = OnlyNode(context.ProductVersions),
            ["sharedReliabilityTraceSha256ByMode"] = traces,
            ["sharedPerformanceRulesStateHash"] = OnlyNode(context.RulesHashes),
            ["totalCleanLaunches"] = Sum(context.Rows.Select(row => row.CleanLaunches)),
            ["installLifecyclePreflightPlatforms"] = CountTruthy(context.Rows.Select(row => row.LifecyclePassed)),
            ["totalSupportedSaveMigrationFixtures"] = Sum(context.Rows.Select(row => row.SaveFixtures)),
            ["totalReliabilityComparedSteps"] = Sum(context.Rows.Select(row => row.ComparedSteps)),
            ["totalSpectatorRestarts"] = Sum(context.Rows.Select(row => row.SpectatorRestarts)),
            ["totalInjectedFaults"] = Sum(context.Rows.Select(row => row.CompletedFaults)),
            ["crashTriagePlatforms"] = CountTrue(context.Rows.Select(row => row.CrashTriageRetained)),
            ["divergenceTriagePlatforms"] = CountTrue(context.Rows.Select(row => row.DivergenceTriageRetained)),
            ["totalPerformanceSamples"] = SumLongs(context.Rows.Select(row => row.PerformanceSamples)),
            ["maximumSharedHostP99Milliseconds"] = ParseNumber(FormatPythonFloat(MaximumP99(context.Rows))),
            ["accessibilityAuditPlatforms"] = CountTrue(context.Rows.Select(row => row.AccessibilityPassed)),
            ["totalMaximumTextScaleDisplayClasses"] = Sum(context.Rows.Select(row => row.TextScaleClasses)),
            ["publicationEligible"] = false,
            ["remainingProtectedOperations"] = StringArray(RemainingProtectedOperations),
            ["errors"] = StringArray(errors),
        };
        return Render(root);
    }

    private static void ValidatePlatform(MatrixContext context, string platform)
    {
        var evidenceRoot = Path.Combine(context.DownloadRoot, $"vibesnake-{platform}-qualification-evidence");
        var manifestPath = Path.Combine(context.DownloadRoot, $"vibesnake-{platform}-manifest", "artifact-manifest.json");
        var manifestDocument = ReadJson(manifestPath, $"{platform} artifact manifest", context);
        var documents = new Dictionary<string, JsonDocument?>(StringComparer.Ordinal);
        foreach (var file in EvidenceFiles)
        {
            documents[file.Name] = ReadJson(
                Path.Combine(evidenceRoot, file.FileName),
                $"{platform} {file.Name} evidence",
                context);
        }

        JsonDocument? launchesDocument = null;
        JsonDocument? lifecycleDocument = null;
        var release = context.ExpectedBuildMode == "Release";
        if (release)
        {
            launchesDocument = ReadJson(
                Path.Combine(evidenceRoot, "candidate_launch_reliability.json"),
                $"{platform} candidate launch evidence",
                context);
            lifecycleDocument = ReadJson(
                Path.Combine(evidenceRoot, "candidate_install_lifecycle.json"),
                $"{platform} candidate lifecycle evidence",
                context);
        }

        if (!IsObject(manifestDocument)
            || EvidenceFiles.Any(file => !IsObject(documents[file.Name]))
            || (release && (!IsObject(launchesDocument) || !IsObject(lifecycleDocument))))
        {
            return;
        }

        var manifest = manifestDocument!.RootElement;
        var readOnly = documents["readOnly"]!.RootElement;
        var dependencies = documents["dependencies"]!.RootElement;
        var signing = documents["signing"]!.RootElement;
        var output = documents["output"]!.RootElement;
        var reliability = documents["reliability"]!.RootElement;
        var faults = documents["faults"]!.RootElement;
        var performance = documents["performance"]!.RootElement;
        var accessibility = documents["accessibility"]!.RootElement;
        var launches = release ? launchesDocument!.RootElement : None;
        var lifecycle = release ? lifecycleDocument!.RootElement : None;
        var manifestSha = Sha256(manifestPath);
        var performanceTotals = new PerformanceTotals(0, 0d);

        ValidateManifest(manifest, platform, context);
        ValidateReadOnly(readOnly, manifest, platform, context);
        ValidateDependencies(dependencies, platform, context);
        ValidateSigning(signing, platform, manifestSha, context);
        ValidateOutput(output, platform, context);
        ValidateReliability(reliability, platform, context);
        ValidateFaults(faults, platform, context.Errors);
        performanceTotals = ValidatePerformance(performance, platform, context);
        ValidateAccessibility(accessibility, evidenceRoot, platform, context.Errors);
        if (release)
        {
            ValidateLaunches(launches, platform, context);
            ValidateLifecycle(lifecycle, platform, context.ExpectedRevision, context.Errors);
        }

        var spectator = Field(reliability, "spectatorRestarts");
        var row = new JsonObject
        {
            ["platform"] = platform,
            ["artifactManifestSha256"] = manifestSha,
            ["packageSha256"] = CloneNode(Field(output, "packageSha256")),
            ["packageBytes"] = CloneNode(Field(output, "packageBytes")),
            ["directDownloadFileName"] = CloneNode(Field(output, "directDownloadFileName")),
            ["fileCount"] = CloneNode(Field(manifest, "fileCount")),
            ["totalBytes"] = CloneNode(Field(manifest, "totalBytes")),
            ["runtimeIdentifier"] = CloneNode(Field(dependencies, "runtimeIdentifier")),
            ["signingState"] = CloneNode(Field(signing, "signingState")),
            ["cleanLaunches"] = CloneNode(release ? Field(launches, "completedLaunches") : Zero),
            ["installLifecyclePreflight"] = CloneNode(release ? Field(lifecycle, "passed") : False),
            ["supportedSaveMigrationFixtures"] = CloneNode(
                release ? FieldOrDefault(lifecycle, "supportedSaveMigrationFixtureCount", Zero) : Zero),
            ["reliabilityComparedSteps"] = CloneNode(FieldOrDefault(reliability, "totalComparedSimulationSteps", Zero)),
            ["spectatorRestarts"] = CloneNode(
                spectator.ValueKind == JsonValueKind.Object
                    ? FieldOrDefault(spectator, "completedRestarts", Zero)
                    : Zero),
            ["completedFaults"] = CloneNode(FieldOrDefault(faults, "completedFaultCount", Zero)),
            ["crashTriageRetained"] = IsReportRetained(faults, "crashTriage"),
            ["divergenceTriageRetained"] = IsReportRetained(faults, "divergenceTriage"),
            ["performanceSamples"] = performanceTotals.Samples,
            ["maximumPerformanceP99Milliseconds"] = ParseNumber(FormatPythonFloat(performanceTotals.MaximumP99)),
            ["accessibilityAuditPassed"] = Field(accessibility, "passed").ValueKind == JsonValueKind.True,
            ["maximumTextScaleDisplayClasses"] = CloneNode(
                FieldOrDefault(accessibility, "maximumTextScaleDisplayClassCount", Zero)),
        };
        context.Rows.Add(new PlatformRow(
            row,
            release ? Field(launches, "completedLaunches") : Zero,
            release ? Field(lifecycle, "passed") : False,
            release ? FieldOrDefault(lifecycle, "supportedSaveMigrationFixtureCount", Zero) : Zero,
            FieldOrDefault(reliability, "totalComparedSimulationSteps", Zero),
            spectator.ValueKind == JsonValueKind.Object
                ? FieldOrDefault(spectator, "completedRestarts", Zero)
                : Zero,
            FieldOrDefault(faults, "completedFaultCount", Zero),
            IsReportRetained(faults, "crashTriage"),
            IsReportRetained(faults, "divergenceTriage"),
            performanceTotals.Samples,
            performanceTotals.MaximumP99,
            Field(accessibility, "passed").ValueKind == JsonValueKind.True,
            FieldOrDefault(accessibility, "maximumTextScaleDisplayClassCount", Zero)));
    }

    private static void ValidateManifest(JsonElement manifest, string platform, MatrixContext context)
    {
        var label = $"{platform} manifest";
        ExpectInteger(manifest, "schemaVersion", 3, label, context.Errors);
        ExpectString(manifest, "product", "Vibe Snake", label, context.Errors);
        ExpectString(manifest, "platform", platform, label, context.Errors);
        ExpectString(manifest, "buildMode", context.ExpectedBuildMode, label, context.Errors);
        ExpectString(manifest, "sourceRevision", context.ExpectedRevision, label, context.Errors);
        ExpectBool(manifest, "agentArenaPreviewExcluded", context.ExpectedBuildMode == "Release", label, context.Errors);
        var smoke = Field(manifest, "smokeStateHash");
        if (smoke.ValueKind != JsonValueKind.String || !StateHashPattern.IsMatch(smoke.GetString() ?? string.Empty))
        {
            context.Errors.Add($"{platform} manifest.smokeStateHash must be 16 lowercase hex characters");
        }
        else
        {
            context.SmokeHashes.Add(smoke.GetString() ?? string.Empty);
        }

        var files = Field(manifest, "files");
        if (files.ValueKind != JsonValueKind.Array || files.GetArrayLength() == 0)
        {
            context.Errors.Add($"{platform} manifest.files must be a nonempty array");
        }

        var length = files.ValueKind == JsonValueKind.Array ? files.GetArrayLength() : files.ValueKind == JsonValueKind.Null ? 0 : -1;
        if (!TryJsonInt64(Field(manifest, "fileCount"), out var fileCount) || length < 0 || fileCount != length)
        {
            context.Errors.Add($"{platform} manifest.fileCount must match files");
        }
    }

    private static void ValidateReadOnly(
        JsonElement readOnly,
        JsonElement manifest,
        string platform,
        MatrixContext context)
    {
        var label = $"{platform} readOnly";
        ExpectInteger(readOnly, "schemaVersion", 1, label, context.Errors);
        ExpectString(readOnly, "kind", "artifact-read-only-install-v1", label, context.Errors);
        ExpectString(readOnly, "platformId", platform, label, context.Errors);
        ExpectString(readOnly, "sourceRevision", context.ExpectedRevision, label, context.Errors);
        ExpectEquivalent(readOnly, "smokeStateHash", Field(manifest, "smokeStateHash"), label, context.Errors);
        ExpectTrue(readOnly, ReadOnlyTrueFields, label, context.Errors);
        if (!PythonEqual(Field(readOnly, "beforeSha256"), Field(readOnly, "afterSha256")))
        {
            context.Errors.Add($"{platform} readOnly install hashes must match");
        }
    }

    private static void ValidateDependencies(JsonElement dependencies, string platform, MatrixContext context)
    {
        var label = $"{platform} dependencies";
        ExpectInteger(dependencies, "schemaVersion", 1, label, context.Errors);
        ExpectString(dependencies, "kind", "dependency-inventory-v1", label, context.Errors);
        ExpectBool(dependencies, "generatedFromLocksOnly", true, label, context.Errors);
        ExpectString(dependencies, "sourceRevision", context.ExpectedRevision, label, context.Errors);
        ExpectBool(dependencies, "sourceDirty", false, label, context.Errors);
        var lockHash = Field(dependencies, "lockSetSha256");
        if (lockHash.ValueKind != JsonValueKind.String || !Sha256Pattern.IsMatch(lockHash.GetString() ?? string.Empty))
        {
            context.Errors.Add($"{platform} dependencies.lockSetSha256 must be a SHA-256 digest");
        }
        else
        {
            context.LockHashes.Add(lockHash.GetString() ?? string.Empty);
        }

        var packages = Field(dependencies, "packages");
        if (packages.ValueKind != JsonValueKind.Array || packages.GetArrayLength() == 0)
        {
            context.Errors.Add($"{platform} dependencies.packages must be a nonempty array");
        }
    }

    private static void ValidateSigning(JsonElement signing, string platform, string manifestSha, MatrixContext context)
    {
        var label = $"{platform} signing";
        ExpectInteger(signing, "schemaVersion", 1, label, context.Errors);
        ExpectString(signing, "kind", "release-signing-readiness-v1", label, context.Errors);
        ExpectString(signing, "product", "Vibe Snake", label, context.Errors);
        ExpectString(signing, "platform", platform, label, context.Errors);
        ExpectString(signing, "sourceRevision", context.ExpectedRevision, label, context.Errors);
        ExpectString(signing, "buildMode", context.ExpectedBuildMode, label, context.Errors);
        ExpectString(signing, "artifactManifestSha256", manifestSha, label, context.Errors);
        ExpectString(signing, "signingState", "unsigned-input", label, context.Errors);
        ExpectBool(signing, "passed", true, label, context.Errors);
        ExpectBool(signing, "ordinaryCiCredentialAccess", false, label, context.Errors);
        ExpectBool(signing, "signingMaterialAllowedInRepository", false, label, context.Errors);
        ExpectBool(signing, "signingMaterialAllowedInArtifacts", false, label, context.Errors);
    }

    private static void ValidateOutput(JsonElement output, string platform, MatrixContext context)
    {
        var label = $"{platform} output";
        ExpectInteger(output, "schemaVersion", 1, label, context.Errors);
        ExpectString(output, "kind", "release-output-plan-v1", label, context.Errors);
        ExpectString(output, "product", "Vibe Snake", label, context.Errors);
        ExpectString(output, "platform", platform, label, context.Errors);
        ExpectTrue(output, OutputTrueFields, label, context.Errors);
        ExpectBool(output, "publicationEligible", false, label, context.Errors);
        ExpectBool(output, "baseGameIncludesOptionalPacks", false, label, context.Errors);
        var packageSha = Field(output, "packageSha256");
        if (packageSha.ValueKind != JsonValueKind.String || !Sha256Pattern.IsMatch(packageSha.GetString() ?? string.Empty))
        {
            context.Errors.Add($"{platform} output.packageSha256 must be a SHA-256 digest");
        }

        if (!IsPositiveInteger(Field(output, "packageBytes")))
        {
            context.Errors.Add($"{platform} output.packageBytes must be a positive integer");
        }

        if (!IsQualificationPackage(Field(output, "directDownloadFileName"), platform))
        {
            context.Errors.Add($"{platform} output.directDownloadFileName is not a qualification package");
        }

        var version = Field(output, "productVersion");
        if (!IsNonEmptyString(version))
        {
            context.Errors.Add($"{platform} output.productVersion must be nonempty");
        }
        else
        {
            context.ProductVersions.Add(version.GetString() ?? string.Empty);
        }
    }

    private static void ValidateReliability(JsonElement reliability, string platform, MatrixContext context)
    {
        var label = $"{platform} reliability";
        ExpectInteger(reliability, "schemaVersion", 1, label, context.Errors);
        ExpectString(reliability, "kind", "candidate-reliability-qualification-v1", label, context.Errors);
        ExpectBool(reliability, "passed", true, label, context.Errors);
        ExpectInteger(reliability, "requiredStepsPerRuleset", 100_000, label, context.Errors);
        ExpectInteger(reliability, "rulesetCount", 2, label, context.Errors);
        ExpectInteger(reliability, "totalComparedSimulationSteps", 200_000, label, context.Errors);
        ExpectString(reliability, "referenceAiId", "balanced", label, context.Errors);
        ExpectString(reliability, "aiAlgorithmId", "native-personality-controller-v2", label, context.Errors);
        ExpectString(reliability, "randomAlgorithmId", "pcg-xsh-rr-32-v1", label, context.Errors);
        var simulations = Field(reliability, "simulations");
        if (simulations.ValueKind != JsonValueKind.Array || simulations.GetArrayLength() != 2)
        {
            context.Errors.Add($"{platform} reliability.simulations must contain two ruleset rows");
        }
        else
        {
            var observedModes = new HashSet<string>(StringComparer.Ordinal);
            var index = 0;
            foreach (var simulation in simulations.EnumerateArray())
            {
                var simulationLabel = $"{platform} reliability.simulations[{index.ToString(CultureInfo.InvariantCulture)}]";
                if (simulation.ValueKind != JsonValueKind.Object)
                {
                    context.Errors.Add($"{simulationLabel} must be an object");
                    index++;
                    continue;
                }

                var modeId = PythonScalarString(Field(simulation, "modeId"));
                observedModes.Add(modeId);
                ExpectInteger(simulation, "modeVersion", 1, simulationLabel, context.Errors);
                if (ExpectedModes.TryGetValue(modeId, out var category))
                {
                    ExpectString(simulation, "scoreCategoryId", category, simulationLabel, context.Errors);
                }
                else
                {
                    ExpectNull(simulation, "scoreCategoryId", simulationLabel, context.Errors);
                }

                ExpectString(simulation, "referenceAiId", "balanced", simulationLabel, context.Errors);
                ExpectInteger(simulation, "requiredComparedSteps", 100_000, simulationLabel, context.Errors);
                ExpectInteger(simulation, "comparedSteps", 100_000, simulationLabel, context.Errors);
                ExpectTrue(simulation, SimulationTrueFields, simulationLabel, context.Errors);
                ExpectNull(simulation, "firstDivergence", simulationLabel, context.Errors);
                var runCount = Field(simulation, "runCount");
                if (!IsPositiveInteger(runCount))
                {
                    context.Errors.Add($"{simulationLabel}.runCount must be a positive integer");
                }
                else if (!TryJsonInt64(Field(simulation, "restartCount"), out var restarts)
                    || !TryJsonInt64(runCount, out var runs)
                    || restarts != runs - 1)
                {
                    context.Errors.Add($"{simulationLabel}.restartCount must equal runCount minus one");
                }

                if (!TryJsonInt64(Field(simulation, "stateHashCheckpointCount"), out var checkpoints) || checkpoints < 100)
                {
                    context.Errors.Add($"{simulationLabel}.stateHashCheckpointCount must be at least 100");
                }

                var trace = Field(simulation, "decisionAndStateTraceSha256");
                if (trace.ValueKind != JsonValueKind.String || !Sha256Pattern.IsMatch(trace.GetString() ?? string.Empty))
                {
                    context.Errors.Add($"{simulationLabel}.decisionAndStateTraceSha256 must be a SHA-256 digest");
                }
                else if (context.TraceHashes.TryGetValue(modeId, out var hashes))
                {
                    hashes.Add(trace.GetString() ?? string.Empty);
                }

                index++;
            }

            if (!observedModes.SetEquals(ExpectedModeIds))
            {
                context.Errors.Add($"{platform} reliability simulations must cover classic and vibe");
            }
        }

        var spectator = Field(reliability, "spectatorRestarts");
        if (spectator.ValueKind != JsonValueKind.Object)
        {
            context.Errors.Add($"{platform} reliability.spectatorRestarts must be an object");
        }
        else
        {
            ValidateSpectator(spectator, $"{platform} reliability.spectatorRestarts", context.Errors);
        }

        ExpectStrings(reliability, "pendingGates", RetainedReleaseGate, label, context.Errors);
    }

    private static void ValidateSpectator(JsonElement spectator, string label, List<string> errors)
    {
        ExpectInteger(spectator, "requiredRestarts", 100, label, errors);
        ExpectInteger(spectator, "completedRestarts", 100, label, errors);
        ExpectInteger(spectator, "stepsPerRestart", 8, label, errors);
        ExpectInteger(spectator, "completedSteps", 800, label, errors);
        ExpectInteger(spectator, "stateResetCount", 100, label, errors);
        ExpectInteger(spectator, "managedSessionReferencesRetained", 0, label, errors);
        ExpectTrue(spectator, SpectatorTrueFields, label, errors);
        var samples = Field(spectator, "resourceSamples");
        if (samples.ValueKind != JsonValueKind.Array || samples.GetArrayLength() != 11)
        {
            errors.Add($"{label}.resourceSamples must contain eleven samples");
            return;
        }

        if (samples.EnumerateArray().Any(sample => sample.ValueKind != JsonValueKind.Object))
        {
            errors.Add($"{label}.resourceSamples must contain objects");
            return;
        }

        if (!RestartCadenceMatches(samples))
        {
            errors.Add($"{label}.resourceSamples cadence must be 0 through 100 by ten");
            return;
        }

        var countsValid = true;
        var sampleIndex = 0;
        foreach (var sample in samples.EnumerateArray())
        {
            foreach (var field in ResourceCountFields)
            {
                if (!TryJsonInt64(Field(sample, field), out var count) || count < 0)
                {
                    errors.Add(
                        $"{label}.resourceSamples[{sampleIndex.ToString(CultureInfo.InvariantCulture)}].{field} must be a nonnegative integer");
                    countsValid = false;
                }
            }

            sampleIndex++;
        }

        if (!countsValid)
        {
            return;
        }

        var baseline = samples.EnumerateArray().First();
        foreach (var sample in samples.EnumerateArray())
        {
            if (Integer(sample, "sceneNodeCount") != Integer(baseline, "sceneNodeCount")
                || Integer(sample, "objectCount") > Integer(baseline, "objectCount")
                || Integer(sample, "resourceCount") > Integer(baseline, "resourceCount")
                || Integer(sample, "orphanNodeCount") > Integer(baseline, "orphanNodeCount"))
            {
                errors.Add($"{label} resources grew across restart samples");
                break;
            }
        }
    }

    private static void ValidateFaults(JsonElement faults, string platform, List<string> errors)
    {
        var label = $"{platform} faults";
        ExpectInteger(faults, "schemaVersion", 1, label, errors);
        ExpectString(faults, "kind", "candidate-fault-campaign-v1", label, errors);
        ExpectBool(faults, "passed", true, label, errors);
        ExpectInteger(faults, "requiredFaultCount", 7, label, errors);
        ExpectInteger(faults, "completedFaultCount", 7, label, errors);
        ExpectTrue(faults, FaultCampaignTrueFields, label, errors);
        var rows = Field(faults, "faults");
        if (rows.ValueKind != JsonValueKind.Array || rows.GetArrayLength() != 7)
        {
            errors.Add($"{label}.faults must contain seven rows");
        }
        else if (rows.EnumerateArray().Any(row => row.ValueKind != JsonValueKind.Object))
        {
            errors.Add($"{label}.faults must contain objects");
        }
        else
        {
            if (!PropertyStringsEqual(rows, "faultId", FaultIds))
            {
                errors.Add($"{label}.faults must cover the exact roadmap fault order");
            }

            var index = 0;
            foreach (var row in rows.EnumerateArray())
            {
                var rowLabel = $"{label}.faults[{index.ToString(CultureInfo.InvariantCulture)}]";
                ExpectTrue(row, FaultRowTrueFields, rowLabel, errors);
                if (!IsNonEmptyString(Field(row, "injectionBoundary")))
                {
                    errors.Add($"{rowLabel}.injectionBoundary must be nonempty");
                }

                index++;
            }
        }

        ValidateTriage(faults, "crashTriage", "crash-report", label, errors);
        ValidateTriage(faults, "divergenceTriage", "deterministic-divergence-report-v1", label, errors);
        ExpectStrings(faults, "pendingGates", RetainedReleaseGate, label, errors);
    }

    private static void ValidateTriage(
        JsonElement faults,
        string field,
        string expectedKind,
        string faultLabel,
        List<string> errors)
    {
        var triage = Field(faults, field);
        var label = $"{faultLabel}.{field}";
        if (triage.ValueKind != JsonValueKind.Object)
        {
            errors.Add($"{label} must be an object");
            return;
        }

        ExpectString(triage, "reportKind", expectedKind, label, errors);
        ExpectTrue(triage, TriageTrueFields, label, errors);
        if (!IsLocalBaseName(Field(triage, "fileName")))
        {
            errors.Add($"{label}.fileName must be a local base name");
        }

        var sha = Field(triage, "sha256");
        if (sha.ValueKind != JsonValueKind.String || !Sha256Pattern.IsMatch(sha.GetString() ?? string.Empty))
        {
            errors.Add($"{label}.sha256 must be a SHA-256 digest");
        }
    }

    private static PerformanceTotals ValidatePerformance(JsonElement performance, string platform, MatrixContext context)
    {
        var label = $"{platform} performance";
        ExpectInteger(performance, "schemaVersion", 1, label, context.Errors);
        ExpectString(performance, "kind", "performance-qualification-v1", label, context.Errors);
        ExpectBool(performance, "passed", true, label, context.Errors);
        ExpectTrue(performance, PerformanceTrueFields, label, context.Errors);
        ExpectInteger(performance, "rulesStepsPerProfile", 256, label, context.Errors);
        ExpectString(performance, "minimumHardwareAcceptanceStatus", "pending-named-hardware", label, context.Errors);
        var rulesHash = Field(performance, "finalRulesStateHash");
        if (rulesHash.ValueKind != JsonValueKind.String || !StateHashPattern.IsMatch(rulesHash.GetString() ?? string.Empty))
        {
            context.Errors.Add($"{label}.finalRulesStateHash must be 16 lowercase hex");
        }
        else
        {
            context.RulesHashes.Add(rulesHash.GetString() ?? string.Empty);
        }

        var budget = Field(performance, "budget");
        if (budget.ValueKind != JsonValueKind.Object)
        {
            context.Errors.Add($"{label}.budget must be an object");
        }
        else
        {
            var budgetLabel = $"{label}.budget";
            ExpectInteger(budget, "targetFramesPerSecond", 60, budgetLabel, context.Errors);
            ExpectInteger(budget, "sharedHostMaximumAverageMilliseconds", 25, budgetLabel, context.Errors);
            ExpectInteger(budget, "sharedHostMaximumP95Milliseconds", 70, budgetLabel, context.Errors);
            ExpectInteger(budget, "maximumLogicalDrawSubmissions", 2400, budgetLabel, context.Errors);
            ExpectInteger(budget, "maximumParticles", 160, budgetLabel, context.Errors);
            ExpectInteger(budget, "maximumAudioChannels", 12, budgetLabel, context.Errors);
            ExpectInteger(budget, "boardCellCapacity", 2112, budgetLabel, context.Errors);
            ExpectInteger(budget, "requiredWarmupFramesPerProfile", 30, budgetLabel, context.Errors);
            ExpectInteger(budget, "requiredSamplesPerProfile", 40, budgetLabel, context.Errors);
            var target = Field(budget, "targetFrameMilliseconds");
            if (target.ValueKind != JsonValueKind.Number
                || !target.TryGetDouble(out var milliseconds)
                || Math.Abs(milliseconds - TargetFrameMilliseconds) > FrameTolerance)
            {
                context.Errors.Add($"{label}.budget.targetFrameMilliseconds must match 60 FPS");
            }
        }

        var profiles = Field(performance, "profiles");
        if (profiles.ValueKind != JsonValueKind.Array || profiles.GetArrayLength() != 3)
        {
            context.Errors.Add($"{label}.profiles must contain three rows");
        }
        else if (profiles.EnumerateArray().Any(profile => profile.ValueKind != JsonValueKind.Object))
        {
            context.Errors.Add($"{label}.profiles must contain objects");
        }
        else
        {
            if (!PropertyStringsEqual(profiles, "id", ProfileIds))
            {
                context.Errors.Add($"{label}.profiles must use the exact effect order");
            }

            var index = 0;
            foreach (var profile in profiles.EnumerateArray())
            {
                var profileId = PythonScalarString(Field(profile, "id"));
                if (!ProfileShapes.TryGetValue(profileId, out var shape) || !ShapeMatches(profile, shape))
                {
                    context.Errors.Add(
                        $"{label}.profiles[{index.ToString(CultureInfo.InvariantCulture)}] stress shape drifted");
                }

                index++;
            }
        }

        long samples = 0;
        var maximumP99 = 0d;
        var measurements = Field(performance, "measurements");
        if (measurements.ValueKind != JsonValueKind.Array || measurements.GetArrayLength() != 3)
        {
            context.Errors.Add($"{label}.measurements must contain three rows");
        }
        else if (measurements.EnumerateArray().Any(measurement => measurement.ValueKind != JsonValueKind.Object))
        {
            context.Errors.Add($"{label}.measurements must contain objects");
        }
        else
        {
            if (!PropertyStringsEqual(measurements, "id", ProfileIds))
            {
                context.Errors.Add($"{label}.measurements must use the exact effect order");
            }

            var index = 0;
            foreach (var measurement in measurements.EnumerateArray())
            {
                var measurementLabel = $"{label}.measurements[{index.ToString(CultureInfo.InvariantCulture)}]";
                if (!TryJsonInt64(Field(measurement, "sampleCount"), out var sampleCount) || sampleCount < 40)
                {
                    context.Errors.Add($"{measurementLabel}.sampleCount must be at least 40");
                    index++;
                    continue;
                }

                samples = unchecked(samples + sampleCount);
                if (!TryTimings(measurement, out var average, out var p50, out var p95, out var p99, out var maximum))
                {
                    context.Errors.Add($"{measurementLabel} frame timings must be positive numbers");
                    index++;
                    continue;
                }

                maximumP99 = Math.Max(maximumP99, p99);
                if (!(p50 <= p95 && p95 <= p99 && p99 <= maximum))
                {
                    context.Errors.Add($"{measurementLabel} percentile ordering is invalid");
                }

                if (average > 25d || p95 > 70d)
                {
                    context.Errors.Add($"{measurementLabel} exceeded the shared-host ceiling");
                }

                var driver = Field(measurement, "driverDrawCallStatus");
                if (driver.ValueKind != JsonValueKind.String
                    || !DriverStatuses.Contains(driver.GetString(), StringComparer.Ordinal))
                {
                    context.Errors.Add($"{measurementLabel}.driverDrawCallStatus is invalid");
                }

                index++;
            }
        }

        if (!HasExactNonEmptyStrings(Field(performance, "pendingHumanChecks"), 4))
        {
            context.Errors.Add($"{label}.pendingHumanChecks must contain four checks");
        }

        return new PerformanceTotals(samples, maximumP99);
    }

    private static void ValidateAccessibility(
        JsonElement accessibility,
        string evidenceRoot,
        string platform,
        List<string> errors)
    {
        var label = $"{platform} accessibility";
        ExpectInteger(accessibility, "schemaVersion", 1, label, errors);
        ExpectString(accessibility, "kind", "candidate-accessibility-audit-v1", label, errors);
        ExpectBool(accessibility, "passed", true, label, errors);
        ExpectString(accessibility, "requiredFlowDefectSeverity", "P1", label, errors);
        ExpectInteger(accessibility, "auditAreaCount", 12, label, errors);
        ExpectTrue(accessibility, AccessibilityTrueFields, label, errors);
        ExpectFloat(accessibility, "maximumTextScale", 1.5d, label, errors);
        ExpectInteger(accessibility, "supportedDisplayClassCount", 8, label, errors);
        ExpectInteger(accessibility, "maximumTextScaleDisplayClassCount", 8, label, errors);
        ExpectString(accessibility, "accessibilityUserReviewStatus", "pending-accessibility-user-review", label, errors);
        ExpectString(accessibility, "featureGuidePath", "docs/guides/ACCESSIBILITY.md", label, errors);
        ExpectString(accessibility, "featurePublicationStatus", "published-in-repository", label, errors);
        var areas = Field(accessibility, "auditAreas");
        if (areas.ValueKind != JsonValueKind.Array || areas.GetArrayLength() != 12)
        {
            errors.Add($"{label}.auditAreas must contain twelve rows");
        }
        else if (areas.EnumerateArray().Any(area => area.ValueKind != JsonValueKind.Object))
        {
            errors.Add($"{label}.auditAreas must contain objects");
        }
        else
        {
            if (!PropertyStringsEqual(areas, "id", AccessibilityAreas))
            {
                errors.Add($"{label}.auditAreas must use the exact roadmap order");
            }

            var index = 0;
            foreach (var area in areas.EnumerateArray())
            {
                var areaLabel = $"{label}.auditAreas[{index.ToString(CultureInfo.InvariantCulture)}]";
                ExpectBool(area, "automatedPassed", true, areaLabel, errors);
                if (!IsNonEmptyStringArray(Field(area, "evidenceFiles")))
                {
                    errors.Add($"{areaLabel}.evidenceFiles must be a nonempty string array");
                }

                index++;
            }
        }

        var displays = Field(accessibility, "displayClasses");
        if (displays.ValueKind != JsonValueKind.Array || displays.GetArrayLength() != 8)
        {
            errors.Add($"{label}.displayClasses must contain eight rows");
        }
        else if (displays.EnumerateArray().Any(row => row.ValueKind != JsonValueKind.Object))
        {
            errors.Add($"{label}.displayClasses must contain objects");
        }
        else
        {
            var index = 0;
            foreach (var row in displays.EnumerateArray())
            {
                var rowLabel = $"{label}.displayClasses[{index.ToString(CultureInfo.InvariantCulture)}]";
                if (!DisplayMatches(row, DisplayRows[index]))
                {
                    errors.Add($"{rowLabel} display shape drifted");
                }

                ExpectFloat(row, "textScale", 1.5d, rowLabel, errors);
                ExpectBool(row, "logicalLayoutComplete", true, rowLabel, errors);
                var scale = Field(row, "viewportScale");
                if (scale.ValueKind != JsonValueKind.Number
                    || !scale.TryGetDouble(out var viewportScale)
                    || !double.IsFinite(viewportScale)
                    || viewportScale <= 0d)
                {
                    errors.Add($"{rowLabel}.viewportScale must be positive");
                }

                index++;
            }
        }

        var sources = Field(accessibility, "sources");
        if (sources.ValueKind != JsonValueKind.Array || sources.GetArrayLength() != AccessibilitySources.Length)
        {
            errors.Add($"{label}.sources must contain seven rows");
        }
        else if (sources.EnumerateArray().Any(source => source.ValueKind != JsonValueKind.Object))
        {
            errors.Add($"{label}.sources must contain objects");
        }
        else
        {
            if (!SourcesMatch(sources))
            {
                errors.Add($"{label}.sources must use the exact evidence order");
            }

            var index = 0;
            foreach (var source in sources.EnumerateArray())
            {
                var sourceLabel = $"{label}.sources[{index.ToString(CultureInfo.InvariantCulture)}]";
                var sha = Field(source, "sha256");
                if (sha.ValueKind != JsonValueKind.String || !Sha256Pattern.IsMatch(sha.GetString() ?? string.Empty))
                {
                    errors.Add($"{sourceLabel}.sha256 must be a SHA-256 digest");
                    index++;
                    continue;
                }

                var fileName = Field(source, "fileName");
                if (!IsLocalBaseName(fileName))
                {
                    errors.Add($"{sourceLabel}.fileName must be a local base name");
                    index++;
                    continue;
                }

                var name = fileName.GetString() ?? string.Empty;
                var sourcePath = Path.Combine(evidenceRoot, name);
                if (!IsRegularFile(sourcePath))
                {
                    errors.Add($"missing {sourceLabel} bound evidence: {StrictJsonFile.Display(sourcePath)}");
                }
                else if (!string.Equals(Sha256(sourcePath), sha.GetString(), StringComparison.Ordinal))
                {
                    errors.Add($"{sourceLabel}.sha256 does not match {name}");
                }

                index++;
            }
        }

        ExpectStrings(accessibility, "pendingHumanChecks", AccessibilityPendingChecks, label, errors);
    }

    private static void ValidateLaunches(JsonElement launches, string platform, MatrixContext context)
    {
        var label = $"{platform} launches";
        ExpectInteger(launches, "schemaVersion", 1, label, context.Errors);
        ExpectString(launches, "kind", "candidate-launch-reliability-v1", label, context.Errors);
        ExpectBool(launches, "passed", true, label, context.Errors);
        ExpectString(launches, "platformId", platform, label, context.Errors);
        ExpectString(launches, "buildMode", "Release", label, context.Errors);
        ExpectString(launches, "sourceRevision", context.ExpectedRevision, label, context.Errors);
        ExpectInteger(launches, "requestedLaunches", 100, label, context.Errors);
        ExpectInteger(launches, "completedLaunches", 100, label, context.Errors);
        ExpectInteger(launches, "freshProfileLaunches", 100, label, context.Errors);
        ExpectBool(launches, "readOnlyInstall", true, label, context.Errors);
        ExpectBool(launches, "headless", true, label, context.Errors);
        ExpectEmptyArray(launches, "failures", label, context.Errors);
    }

    private static void ValidateLifecycle(JsonElement lifecycle, string platform, string expectedRevision, List<string> errors)
    {
        var label = $"{platform} lifecycle";
        ExpectInteger(lifecycle, "schemaVersion", 1, label, errors);
        ExpectString(lifecycle, "kind", "candidate-install-lifecycle-preflight-v1", label, errors);
        ExpectString(lifecycle, "platformId", platform, label, errors);
        ExpectString(lifecycle, "buildMode", "Release", label, errors);
        ExpectString(lifecycle, "sourceRevision", expectedRevision, label, errors);
        ExpectTrue(lifecycle, LifecycleTrueFields, label, errors);
        ExpectInteger(lifecycle, "preferenceMigrationFixtureCount", 6, label, errors);
        if (!PythonEqual(Field(lifecycle, "preferenceMigrations"), ExpectedPreferenceMigrations))
        {
            errors.Add($"{platform} lifecycle.preferenceMigrations must cover schemas 1 through 6");
        }

        ExpectInteger(lifecycle, "additionalSaveMigrationFixtureCount", 2, label, errors);
        if (!PythonEqual(Field(lifecycle, "additionalSaveMigrations"), ExpectedAdditionalMigrations))
        {
            errors.Add($"{platform} lifecycle.additionalSaveMigrations must cover both schema-1 stores");
        }

        ExpectInteger(lifecycle, "supportedSaveMigrationFixtureCount", 8, label, errors);
        ExpectStrings(lifecycle, "remainingGates", LifecycleRemainingGates, label, errors);
    }

    private static JsonDocument? ReadJson(string path, string label, MatrixContext context)
    {
        var display = StrictJsonFile.Display(path);
        if (!IsRegularFile(path))
        {
            context.Errors.Add($"missing {label}: {display}");
            return null;
        }

        long length;
        try
        {
            length = new FileInfo(path).Length;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            context.Errors.Add($"unreadable {label}: {display}: {StrictJsonFile.SingleLine(exception.Message)}");
            return null;
        }

        if (length > context.MaximumJsonBytes)
        {
            context.Errors.Add(LimitMessage(label, context.MaximumJsonBytes));
            return null;
        }

        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            context.Errors.Add($"unreadable {label}: {display}: {StrictJsonFile.SingleLine(exception.Message)}");
            return null;
        }

        string source;
        try
        {
            source = StrictUtf8.GetString(bytes);
        }
        catch (Exception exception) when (exception is DecoderFallbackException or ArgumentException)
        {
            context.Errors.Add($"unreadable {label}: {display}: {StrictJsonFile.SingleLine(exception.Message)}");
            return null;
        }

        if (StrictUtf8.GetByteCount(source) > context.MaximumJsonBytes)
        {
            context.Errors.Add(LimitMessage(label, context.MaximumJsonBytes));
            return null;
        }

        var nonFinite = FindNonFiniteToken(source);
        if (nonFinite is not null)
        {
            context.Errors.Add($"unreadable {label}: {display}: non-finite JSON number: {nonFinite}");
            return null;
        }

        try
        {
            RejectDuplicateProperties(bytes);
            var document = JsonDocument.Parse(bytes, DocumentOptions);
            context.Owned.Add(document);
            return document;
        }
        catch (Exception exception) when (exception is InvalidDataException or JsonException or DecoderFallbackException)
        {
            context.Errors.Add($"unreadable {label}: {display}: {StrictJsonFile.SingleLine(exception.Message)}");
            return null;
        }
    }

    private static string LimitMessage(string label, int maximumJsonBytes) =>
        $"{label} exceeds the {maximumJsonBytes.ToString(CultureInfo.InvariantCulture)}-byte limit";

    private static void RejectDuplicateProperties(ReadOnlySpan<byte> json)
    {
        var reader = new Utf8JsonReader(json, ReaderOptions);
        var objects = new Stack<HashSet<string>>();
        while (reader.Read())
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.StartObject:
                    objects.Push(new HashSet<string>(StringComparer.Ordinal));
                    break;
                case JsonTokenType.EndObject:
                    if (objects.Count > 0)
                    {
                        objects.Pop();
                    }

                    break;
                case JsonTokenType.PropertyName:
                    if (objects.Count == 0)
                    {
                        throw new InvalidDataException("JSON property is outside an object");
                    }

                    var name = reader.GetString() ?? string.Empty;
                    if (!objects.Peek().Add(name))
                    {
                        throw new InvalidDataException($"duplicate JSON field: {name}");
                    }

                    break;
            }
        }
    }

    private static string? FindNonFiniteToken(string source)
    {
        var index = 0;
        while (index < source.Length)
        {
            var character = source[index];
            if (character == '"')
            {
                index = SkipString(source, index);
                continue;
            }

            if (MatchToken(source, index, "-Infinity"))
            {
                return "-Infinity";
            }

            if (MatchToken(source, index, "Infinity"))
            {
                return "Infinity";
            }

            if (MatchToken(source, index, "NaN"))
            {
                return "NaN";
            }

            index++;
        }

        return null;
    }

    private static bool MatchToken(string source, int index, string token) =>
        index + token.Length <= source.Length
        && string.CompareOrdinal(source, index, token, 0, token.Length) == 0
        && HasTokenBoundary(source, index, token.Length);

    private static bool HasTokenBoundary(string source, int index, int length)
    {
        if (index > 0 && IsTokenCharacter(source[index - 1]))
        {
            return false;
        }

        var end = index + length;
        return end >= source.Length || !IsTokenCharacter(source[end]);
    }

    private static bool IsTokenCharacter(char character) =>
        character is (>= '0' and <= '9') or (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or '_' or '+' or '-' or '.';

    private static int SkipString(string source, int index)
    {
        index++;
        while (index < source.Length)
        {
            var character = source[index++];
            if (character == '\\')
            {
                if (index < source.Length)
                {
                    index++;
                }

                continue;
            }

            if (character == '"')
            {
                break;
            }
        }

        return index;
    }

    private static void ExpectTrue(JsonElement document, string[] fields, string label, List<string> errors)
    {
        foreach (var field in fields)
        {
            ExpectBool(document, field, true, label, errors);
        }
    }

    private static void ExpectBool(JsonElement document, string field, bool expected, string label, List<string> errors)
    {
        var actual = Field(document, field);
        var matches = expected ? actual.ValueKind == JsonValueKind.True : actual.ValueKind == JsonValueKind.False;
        if (!matches)
        {
            errors.Add($"{label}.{field} must be {(expected ? "True" : "False")}; got {StrictJsonFile.Format(actual)}");
        }
    }

    private static void ExpectInteger(JsonElement document, string field, int expected, string label, List<string> errors)
    {
        var actual = Field(document, field);
        if (!TryJsonInt64(actual, out var actualValue) || actualValue != expected)
        {
            errors.Add(
                $"{label}.{field} must be {expected.ToString(CultureInfo.InvariantCulture)}; got {StrictJsonFile.Format(actual)}");
        }
    }

    private static void ExpectFloat(JsonElement document, string field, double expected, string label, List<string> errors)
    {
        var actual = Field(document, field);
        if (actual.ValueKind != JsonValueKind.Number || !actual.TryGetDouble(out var number) || number != expected)
        {
            errors.Add($"{label}.{field} must be {FormatPythonFloat(expected)}; got {StrictJsonFile.Format(actual)}");
        }
    }

    private static void ExpectString(JsonElement document, string field, string expected, string label, List<string> errors)
    {
        var actual = Field(document, field);
        if (actual.ValueKind != JsonValueKind.String || !string.Equals(actual.GetString(), expected, StringComparison.Ordinal))
        {
            errors.Add($"{label}.{field} must be {StrictJsonFile.Quote(expected)}; got {StrictJsonFile.Format(actual)}");
        }
    }

    private static void ExpectNull(JsonElement document, string field, string label, List<string> errors)
    {
        var actual = Field(document, field);
        if (actual.ValueKind != JsonValueKind.Null)
        {
            errors.Add($"{label}.{field} must be None; got {StrictJsonFile.Format(actual)}");
        }
    }

    private static void ExpectStrings(JsonElement document, string field, string[] expected, string label, List<string> errors)
    {
        var actual = Field(document, field);
        if (!StringArrayEquals(actual, expected))
        {
            errors.Add($"{label}.{field} must be {StrictJsonFile.FormatStrings(expected)}; got {StrictJsonFile.Format(actual)}");
        }
    }

    private static void ExpectEmptyArray(JsonElement document, string field, string label, List<string> errors)
    {
        var actual = Field(document, field);
        if (actual.ValueKind != JsonValueKind.Array || actual.GetArrayLength() != 0)
        {
            errors.Add($"{label}.{field} must be []; got {StrictJsonFile.Format(actual)}");
        }
    }

    private static void ExpectEquivalent(
        JsonElement document,
        string field,
        JsonElement expected,
        string label,
        List<string> errors)
    {
        var actual = Field(document, field);
        if (!ValuesMatch(actual, expected))
        {
            errors.Add($"{label}.{field} must be {StrictJsonFile.Format(expected)}; got {StrictJsonFile.Format(actual)}");
        }
    }

    private static bool ValuesMatch(JsonElement actual, JsonElement expected)
    {
        if (expected.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            return actual.ValueKind == expected.ValueKind;
        }

        if (IsJsonInteger(expected))
        {
            return TryJsonInt64(expected, out var expectedNumber)
                && TryJsonInt64(actual, out var actualNumber)
                && actualNumber == expectedNumber;
        }

        if (expected.ValueKind == JsonValueKind.Number)
        {
            return actual.ValueKind == JsonValueKind.Number
                && actual.TryGetDouble(out var actualNumber)
                && expected.TryGetDouble(out var expectedNumber)
                && actualNumber == expectedNumber;
        }

        if (expected.ValueKind == JsonValueKind.Array)
        {
            if (actual.ValueKind != JsonValueKind.Array || actual.GetArrayLength() != expected.GetArrayLength())
            {
                return false;
            }

            var expectedItems = expected.EnumerateArray();
            foreach (var item in actual.EnumerateArray())
            {
                if (!expectedItems.MoveNext() || !ValuesMatch(item, expectedItems.Current))
                {
                    return false;
                }
            }

            return true;
        }

        if (expected.ValueKind == JsonValueKind.String)
        {
            return actual.ValueKind == JsonValueKind.String
                && string.Equals(actual.GetString(), expected.GetString(), StringComparison.Ordinal);
        }

        if (expected.ValueKind == JsonValueKind.Null)
        {
            return actual.ValueKind == JsonValueKind.Null;
        }

        if (expected.ValueKind == JsonValueKind.Object)
        {
            return PythonEqual(actual, expected);
        }

        return false;
    }

    private static bool StringArrayEquals(JsonElement actual, string[] expected)
    {
        if (actual.ValueKind != JsonValueKind.Array || actual.GetArrayLength() != expected.Length)
        {
            return false;
        }

        var index = 0;
        foreach (var item in actual.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String || !string.Equals(item.GetString(), expected[index], StringComparison.Ordinal))
            {
                return false;
            }

            index++;
        }

        return true;
    }

    private static bool PythonEqual(JsonElement left, JsonElement right)
    {
        var leftIsNumber = TryPythonNumber(left, out var leftNumber);
        var rightIsNumber = TryPythonNumber(right, out var rightNumber);
        if (leftIsNumber || rightIsNumber)
        {
            return leftIsNumber && rightIsNumber && leftNumber == rightNumber;
        }

        if (left.ValueKind != right.ValueKind)
        {
            return false;
        }

        return left.ValueKind switch
        {
            JsonValueKind.Null => true,
            JsonValueKind.String => string.Equals(left.GetString(), right.GetString(), StringComparison.Ordinal),
            JsonValueKind.Array => ArraysEqual(left, right),
            JsonValueKind.Object => ObjectsEqual(left, right),
            _ => false,
        };
    }

    private static bool ArraysEqual(JsonElement left, JsonElement right)
    {
        if (left.GetArrayLength() != right.GetArrayLength())
        {
            return false;
        }

        var rightItems = right.EnumerateArray();
        foreach (var item in left.EnumerateArray())
        {
            if (!rightItems.MoveNext() || !PythonEqual(item, rightItems.Current))
            {
                return false;
            }
        }

        return true;
    }

    private static bool ObjectsEqual(JsonElement left, JsonElement right)
    {
        var leftCount = left.EnumerateObject().Count();
        var rightCount = right.EnumerateObject().Count();
        if (leftCount != rightCount)
        {
            return false;
        }

        foreach (var property in left.EnumerateObject())
        {
            if (!right.TryGetProperty(property.Name, out var other) || !PythonEqual(property.Value, other))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryPythonNumber(JsonElement value, out double number)
    {
        if (value.ValueKind == JsonValueKind.True)
        {
            number = 1d;
            return true;
        }

        if (value.ValueKind == JsonValueKind.False)
        {
            number = 0d;
            return true;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out number) && double.IsFinite(number))
        {
            return true;
        }

        number = 0d;
        return false;
    }

    private static bool PropertyStringsEqual(JsonElement array, string property, string[] expected)
    {
        if (array.ValueKind != JsonValueKind.Array || array.GetArrayLength() != expected.Length)
        {
            return false;
        }

        var index = 0;
        foreach (var item in array.EnumerateArray())
        {
            var value = Field(item, property);
            if (value.ValueKind != JsonValueKind.String || !string.Equals(value.GetString(), expected[index], StringComparison.Ordinal))
            {
                return false;
            }

            index++;
        }

        return true;
    }

    private static bool SourcesMatch(JsonElement sources)
    {
        var index = 0;
        foreach (var source in sources.EnumerateArray())
        {
            var fileName = Field(source, "fileName");
            var kind = Field(source, "kind");
            if (fileName.ValueKind != JsonValueKind.String
                || kind.ValueKind != JsonValueKind.String
                || !string.Equals(fileName.GetString(), AccessibilitySources[index].FileName, StringComparison.Ordinal)
                || !string.Equals(kind.GetString(), AccessibilitySources[index].Kind, StringComparison.Ordinal))
            {
                return false;
            }

            index++;
        }

        return true;
    }

    private static bool DisplayMatches(JsonElement row, DisplayRow expected)
    {
        var id = Field(row, "id");
        return id.ValueKind == JsonValueKind.String
            && string.Equals(id.GetString(), expected.Id, StringComparison.Ordinal)
            && TryJsonInt64(Field(row, "requestedWidth"), out var requestedWidth)
            && requestedWidth == expected.RequestedWidth
            && TryJsonInt64(Field(row, "requestedHeight"), out var requestedHeight)
            && requestedHeight == expected.RequestedHeight
            && TryJsonInt64(Field(row, "effectiveWidth"), out var effectiveWidth)
            && effectiveWidth == expected.EffectiveWidth
            && TryJsonInt64(Field(row, "effectiveHeight"), out var effectiveHeight)
            && effectiveHeight == expected.EffectiveHeight;
    }

    private static bool ShapeMatches(JsonElement profile, int[] expected)
    {
        for (var index = 0; index < ProfileShapeFields.Length; index++)
        {
            if (!TryJsonInt64(Field(profile, ProfileShapeFields[index]), out var actual) || actual != expected[index])
            {
                return false;
            }
        }

        return true;
    }

    private static bool RestartCadenceMatches(JsonElement samples)
    {
        var expected = 0;
        foreach (var sample in samples.EnumerateArray())
        {
            if (!TryJsonInt64(Field(sample, "completedRestarts"), out var actual) || actual != expected)
            {
                return false;
            }

            expected += 10;
        }

        return expected == 110;
    }

    private static bool TryTimings(
        JsonElement measurement,
        out double average,
        out double p50,
        out double p95,
        out double p99,
        out double maximum)
    {
        average = 0d;
        p50 = 0d;
        p95 = 0d;
        p99 = 0d;
        maximum = 0d;
        var values = new double[TimingFields.Length];
        for (var index = 0; index < TimingFields.Length; index++)
        {
            if (!TryPositiveDouble(Field(measurement, TimingFields[index]), out values[index]))
            {
                return false;
            }
        }

        average = values[0];
        p50 = values[1];
        p95 = values[2];
        p99 = values[3];
        maximum = values[4];
        return true;
    }

    private static bool HasExactNonEmptyStrings(JsonElement value, int count)
    {
        if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() != count)
        {
            return false;
        }

        foreach (var item in value.EnumerateArray())
        {
            if (!IsNonEmptyString(item))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsNonEmptyStringArray(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() == 0)
        {
            return false;
        }

        foreach (var item in value.EnumerateArray())
        {
            if (!IsNonEmptyString(item))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsNonEmptyString(JsonElement value) =>
        value.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(value.GetString());

    private static bool IsLocalBaseName(JsonElement value)
    {
        if (!IsNonEmptyString(value))
        {
            return false;
        }

        var name = value.GetString() ?? string.Empty;
        return name != "." && Path.GetFileName(name) == name;
    }

    private static bool IsQualificationPackage(JsonElement value, string platform)
    {
        if (value.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var name = value.GetString() ?? string.Empty;
        var extension = platform == "linux-x64" ? ".tar.gz" : ".zip";
        return name.StartsWith("VibeSnake-", StringComparison.Ordinal)
            && name.EndsWith($"-{platform}-qualification{extension}", StringComparison.Ordinal);
    }

    private static bool IsReportRetained(JsonElement faults, string name)
    {
        var triage = Field(faults, name);
        return triage.ValueKind == JsonValueKind.Object && Field(triage, "reportRetained").ValueKind == JsonValueKind.True;
    }

    private static bool IsObject(JsonDocument? document) =>
        document is not null && document.RootElement.ValueKind == JsonValueKind.Object;

    private static bool IsRegularFile(string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            var attributes = File.GetAttributes(path);
            return (attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) == 0;
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or NotSupportedException)
        {
            return false;
        }
    }

    private static JsonElement Field(JsonElement document, string name)
    {
        if (document.ValueKind == JsonValueKind.Object && document.TryGetProperty(name, out var value))
        {
            return value;
        }

        return None;
    }

    private static JsonElement FieldOrDefault(JsonElement document, string name, JsonElement fallback)
    {
        if (document.ValueKind == JsonValueKind.Object && document.TryGetProperty(name, out var value))
        {
            return value;
        }

        return fallback;
    }

    private static bool IsJsonInteger(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Number)
        {
            return false;
        }

        var raw = value.GetRawText();
        var index = 0;
        if (raw.Length > 0 && raw[0] == '-')
        {
            index = 1;
        }

        if (index >= raw.Length)
        {
            return false;
        }

        for (; index < raw.Length; index++)
        {
            if (raw[index] is < '0' or > '9')
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryJsonInt64(JsonElement value, out long number)
    {
        number = 0;
        return IsJsonInteger(value)
            && long.TryParse(value.GetRawText(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out number);
    }

    private static bool IsPositiveInteger(JsonElement value) => TryJsonInt64(value, out var number) && number > 0;

    private static bool TryPositiveDouble(JsonElement value, out double number)
    {
        if (value.ValueKind == JsonValueKind.Number
            && value.TryGetDouble(out number)
            && double.IsFinite(number)
            && number > 0d)
        {
            return true;
        }

        number = 0d;
        return false;
    }

    private static long Integer(JsonElement document, string name) =>
        TryJsonInt64(Field(document, name), out var number) ? number : 0L;

    private static string PythonScalarString(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString() ?? string.Empty,
        JsonValueKind.True => "True",
        JsonValueKind.False => "False",
        JsonValueKind.Number => IsJsonInteger(value) ? value.GetRawText() : FormatPythonFloat(value.GetDouble()),
        JsonValueKind.Null or JsonValueKind.Undefined => "None",
        _ => StrictJsonFile.Format(value),
    };

    private static bool IsTruthy(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.True => true,
        JsonValueKind.False or JsonValueKind.Null or JsonValueKind.Undefined => false,
        JsonValueKind.Number => value.TryGetDouble(out var number) && number != 0d,
        JsonValueKind.String => !string.IsNullOrEmpty(value.GetString()),
        JsonValueKind.Array => value.GetArrayLength() > 0,
        JsonValueKind.Object => value.EnumerateObject().Any(),
        _ => false,
    };

    private static JsonNode Sum(IEnumerable<JsonElement> values)
    {
        long integer = 0;
        double floating = 0d;
        var seenFloat = false;
        foreach (var value in values)
        {
            if (value.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                var bit = value.ValueKind == JsonValueKind.True ? 1 : 0;
                if (seenFloat)
                {
                    floating += bit;
                }
                else
                {
                    integer += bit;
                }

                continue;
            }

            if (value.ValueKind != JsonValueKind.Number)
            {
                continue;
            }

            if (!seenFloat && TryJsonInt64(value, out var whole))
            {
                integer = unchecked(integer + whole);
                continue;
            }

            if (!value.TryGetDouble(out var number) || !double.IsFinite(number))
            {
                continue;
            }

            if (!seenFloat)
            {
                floating = integer;
                seenFloat = true;
            }

            floating += number;
        }

        if (seenFloat)
        {
            return ParseNumber(FormatPythonFloat(floating));
        }

        return JsonValue.Create(integer)!;
    }

    private static long SumLongs(IEnumerable<long> values)
    {
        long total = 0;
        foreach (var value in values)
        {
            total = unchecked(total + value);
        }

        return total;
    }

    private static int CountTrue(IEnumerable<bool> values)
    {
        var total = 0;
        foreach (var value in values)
        {
            if (value)
            {
                total++;
            }
        }

        return total;
    }

    private static int CountTruthy(IEnumerable<JsonElement> values)
    {
        var total = 0;
        foreach (var value in values)
        {
            if (IsTruthy(value))
            {
                total++;
            }
        }

        return total;
    }

    private static double MaximumP99(IReadOnlyList<PlatformRow> rows)
    {
        if (rows.Count == 0)
        {
            return 0d;
        }

        var maximum = rows[0].MaximumP99;
        for (var index = 1; index < rows.Count; index++)
        {
            maximum = Math.Max(maximum, rows[index].MaximumP99);
        }

        return maximum;
    }

    private static JsonValue? OnlyNode(HashSet<string> values)
    {
        if (values.Count != 1)
        {
            return null;
        }

        foreach (var value in values)
        {
            return JsonValue.Create(value);
        }

        return null;
    }

    private static JsonNode? CloneNode(JsonElement value)
    {
        if (value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        return JsonNode.Parse(value.GetRawText());
    }

    private static JsonNode ParseNumber(string literal) => JsonNode.Parse(literal)!;

    private static JsonArray StringArray(IEnumerable<string> values)
    {
        var array = new JsonArray();
        foreach (var value in values)
        {
            array.Add(value);
        }

        return array;
    }

    private static string Render(JsonObject root)
    {
        var json = root.ToJsonString(RenderOptions).Replace("\r\n", "\n", StringComparison.Ordinal);
        if (!json.EndsWith('\n'))
        {
            json += "\n";
        }

        return json;
    }

    private static string FormatPythonFloat(double value)
    {
        var text = JsonValue.Create(value)!.ToJsonString();
        if (text.Contains('E'))
        {
            text = text.Replace("E", "e", StringComparison.Ordinal);
        }

        if (!text.Contains('.') && !text.Contains('e'))
        {
            text += ".0";
        }

        return text;
    }

    private static string Sha256(string path)
    {
        using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    private static JsonElement ParseElement(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private sealed class MatrixContext(
        string downloadRoot,
        string expectedRevision,
        string expectedBuildMode,
        int maximumJsonBytes,
        List<string> errors,
        List<JsonDocument> owned)
    {
        public string DownloadRoot { get; } = downloadRoot;
        public string ExpectedRevision { get; } = expectedRevision;
        public string ExpectedBuildMode { get; } = expectedBuildMode;
        public int MaximumJsonBytes { get; } = maximumJsonBytes;
        public List<string> Errors { get; } = errors;
        public List<JsonDocument> Owned { get; } = owned;
        public List<PlatformRow> Rows { get; } = [];
        public HashSet<string> SmokeHashes { get; } = new(StringComparer.Ordinal);
        public HashSet<string> LockHashes { get; } = new(StringComparer.Ordinal);
        public HashSet<string> ProductVersions { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, HashSet<string>> TraceHashes { get; } = new(StringComparer.Ordinal)
        {
            ["classic"] = new HashSet<string>(StringComparer.Ordinal),
            ["vibe"] = new HashSet<string>(StringComparer.Ordinal),
        };
        public HashSet<string> RulesHashes { get; } = new(StringComparer.Ordinal);
    }

    private sealed class PlatformRow(
        JsonObject json,
        JsonElement cleanLaunches,
        JsonElement lifecyclePassed,
        JsonElement saveFixtures,
        JsonElement comparedSteps,
        JsonElement spectatorRestarts,
        JsonElement completedFaults,
        bool crashTriageRetained,
        bool divergenceTriageRetained,
        long performanceSamples,
        double maximumP99,
        bool accessibilityPassed,
        JsonElement textScaleClasses)
    {
        public JsonObject Json { get; } = json;
        public JsonElement CleanLaunches { get; } = cleanLaunches;
        public JsonElement LifecyclePassed { get; } = lifecyclePassed;
        public JsonElement SaveFixtures { get; } = saveFixtures;
        public JsonElement ComparedSteps { get; } = comparedSteps;
        public JsonElement SpectatorRestarts { get; } = spectatorRestarts;
        public JsonElement CompletedFaults { get; } = completedFaults;
        public bool CrashTriageRetained { get; } = crashTriageRetained;
        public bool DivergenceTriageRetained { get; } = divergenceTriageRetained;
        public long PerformanceSamples { get; } = performanceSamples;
        public double MaximumP99 { get; } = maximumP99;
        public bool AccessibilityPassed { get; } = accessibilityPassed;
        public JsonElement TextScaleClasses { get; } = textScaleClasses;
    }

    private readonly record struct EvidenceFile(string Name, string FileName);

    private readonly record struct DisplayRow(
        string Id,
        int RequestedWidth,
        int RequestedHeight,
        int EffectiveWidth,
        int EffectiveHeight);

    private readonly record struct SourceFile(string FileName, string Kind);

    private readonly record struct PerformanceTotals(long Samples, double MaximumP99);
}
