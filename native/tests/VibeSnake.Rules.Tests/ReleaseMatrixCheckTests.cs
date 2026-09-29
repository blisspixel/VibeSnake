using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using RepositoryChecks;

namespace VibeSnake.Rules.Tests;

public sealed class ReleaseMatrixCheckTests
{
    private const string Revision = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string SmokeHash = "bbbbbbbbbbbbbbbb";
    private const string LockHash = "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true,
        IndentCharacter = ' ',
        IndentSize = 2,
    };

    private static readonly string[] Platforms = ["windows-x64", "macos-universal", "linux-x64"];
    private static readonly string[] ShapeKinds = ["bool", "string", "empty", "null", "float", "negative"];

    private static readonly string[] EvidencePropertyOrder =
    [
        "schemaVersion",
        "kind",
        "passed",
        "sourceRevision",
        "buildMode",
        "platforms",
        "sharedSmokeStateHash",
        "sharedLockSetSha256",
        "productVersion",
        "sharedReliabilityTraceSha256ByMode",
        "sharedPerformanceRulesStateHash",
        "totalCleanLaunches",
        "installLifecyclePreflightPlatforms",
        "totalSupportedSaveMigrationFixtures",
        "totalReliabilityComparedSteps",
        "totalSpectatorRestarts",
        "totalInjectedFaults",
        "crashTriagePlatforms",
        "divergenceTriagePlatforms",
        "totalPerformanceSamples",
        "maximumSharedHostP99Milliseconds",
        "accessibilityAuditPlatforms",
        "totalMaximumTextScaleDisplayClasses",
        "publicationEligible",
        "remainingProtectedOperations",
        "errors",
    ];

    private static readonly string[] RowPropertyOrder =
    [
        "platform",
        "artifactManifestSha256",
        "packageSha256",
        "packageBytes",
        "directDownloadFileName",
        "fileCount",
        "totalBytes",
        "runtimeIdentifier",
        "signingState",
        "cleanLaunches",
        "installLifecyclePreflight",
        "supportedSaveMigrationFixtures",
        "reliabilityComparedSteps",
        "spectatorRestarts",
        "completedFaults",
        "crashTriageRetained",
        "divergenceTriageRetained",
        "performanceSamples",
        "maximumPerformanceP99Milliseconds",
        "accessibilityAuditPassed",
        "maximumTextScaleDisplayClasses",
    ];

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

    private static readonly (string FileName, string Kind)[] AccessibilitySourceKinds =
    [
        ("accessibility_presentation.json", "accessibility-presentation-v1"),
        ("shell_presentation.json", "shell-presentation-v1"),
        ("settings_screen.json", "settings-screen-qualification-v1"),
        ("input_cadence.json", "input-cadence-qualification-v1"),
        ("audio_fallback_stress.json", "audio-mixing-policy-v2"),
        ("multimodal_feedback.json", "multimodal-feedback-v1"),
        ("viewport_matrix.json", "virtual-viewport-matrix-v1"),
    ];

    private static readonly string[] AccessibilityAreaIds =
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

    private static readonly (string Id, int RequestedWidth, int RequestedHeight, int EffectiveWidth, int EffectiveHeight, double ViewportScale)[] DisplayRows =
    [
        ("minimum-clamp", 320, 180, 640, 360, 0.5),
        ("hd-16-9", 1920, 1080, 1920, 1080, 1.5),
        ("classic-4-3", 1024, 768, 1024, 768, 0.8),
        ("desktop-16-10", 1920, 1200, 1920, 1200, 1.5),
        ("ultrawide-21-9", 3440, 1440, 3440, 1440, 2.0),
        ("square-1-1", 1024, 1024, 1024, 1024, 0.8),
        ("high-density-4k", 3840, 2160, 3840, 2160, 3.0),
        ("high-density-5k", 5120, 2880, 5120, 2880, 4.0),
    ];

    [Fact]
    public void Complete_release_matrix_cross_binds_all_platform_evidence()
    {
        using var directory = new TemporaryDirectory();
        WriteMatrix(directory.Path, "Release");
        var before = Files(directory.Path);

        var qualification = ReleaseMatrixCheck.Qualify(directory.Path, Revision, "Release");

        Assert.Equal(before, Files(directory.Path));
        Assert.True(qualification.Passed, string.Join("; ", qualification.Errors));
        Assert.Empty(qualification.Errors);
        Assert.False(qualification.Json.Contains('\r'));
        Assert.EndsWith("\n", qualification.Json);
        Assert.False(qualification.Json.EndsWith("\n\n", StringComparison.Ordinal));
        Assert.Equal((byte)'{', Encoding.UTF8.GetBytes(qualification.Json)[0]);

        using var evidence = JsonDocument.Parse(qualification.Json);
        var root = evidence.RootElement;
        Assert.Equal(EvidencePropertyOrder, Names(root));
        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("release-matrix-qualification-v1", root.GetProperty("kind").GetString());
        Assert.True(root.GetProperty("passed").GetBoolean());
        Assert.Equal(Platforms, Strings(root.GetProperty("platforms"), "platform"));
        Assert.Equal(RowPropertyOrder, Names(root.GetProperty("platforms")[0]));
        Assert.Equal(SmokeHash, root.GetProperty("sharedSmokeStateHash").GetString());
        Assert.Equal(LockHash, root.GetProperty("sharedLockSetSha256").GetString());
        Assert.Equal("9.0", root.GetProperty("maximumSharedHostP99Milliseconds").GetRawText());
        Assert.Equal("9.0", root.GetProperty("platforms")[0].GetProperty("maximumPerformanceP99Milliseconds").GetRawText());
        Assert.Equal(300, root.GetProperty("totalCleanLaunches").GetInt32());
        Assert.Equal("300", root.GetProperty("totalCleanLaunches").GetRawText());
        Assert.Equal(3, root.GetProperty("installLifecyclePreflightPlatforms").GetInt32());
        Assert.Equal(24, root.GetProperty("totalSupportedSaveMigrationFixtures").GetInt32());
        Assert.Equal(600_000, root.GetProperty("totalReliabilityComparedSteps").GetInt32());
        Assert.Equal(300, root.GetProperty("totalSpectatorRestarts").GetInt32());
        Assert.Equal(21, root.GetProperty("totalInjectedFaults").GetInt32());
        Assert.Equal(3, root.GetProperty("crashTriagePlatforms").GetInt32());
        Assert.Equal(3, root.GetProperty("divergenceTriagePlatforms").GetInt32());
        Assert.Equal(360, root.GetProperty("totalPerformanceSamples").GetInt32());
        Assert.Equal(new string('4', 16), root.GetProperty("sharedPerformanceRulesStateHash").GetString());
        Assert.Equal(3, root.GetProperty("accessibilityAuditPlatforms").GetInt32());
        Assert.Equal(24, root.GetProperty("totalMaximumTextScaleDisplayClasses").GetInt32());
        Assert.Equal(new string('1', 64), root.GetProperty("sharedReliabilityTraceSha256ByMode").GetProperty("classic").GetString());
        Assert.Equal(new string('1', 64), root.GetProperty("sharedReliabilityTraceSha256ByMode").GetProperty("vibe").GetString());
        Assert.Equal(["classic", "vibe"], Names(root.GetProperty("sharedReliabilityTraceSha256ByMode")));
        Assert.Equal(RemainingProtectedOperations, Strings(root.GetProperty("remainingProtectedOperations")));
        Assert.DoesNotContain("remaining-fault-injection-and-triage", Strings(root.GetProperty("remainingProtectedOperations")));
        Assert.Contains("retained-accessibility-audit-and-user-review", Strings(root.GetProperty("remainingProtectedOperations")));
        Assert.False(root.GetProperty("publicationEligible").GetBoolean());
        Assert.Equal(0, root.GetProperty("errors").GetArrayLength());
    }

    [Fact]
    public void Debug_matrix_does_not_claim_or_require_candidate_launches()
    {
        using var directory = new TemporaryDirectory();
        WriteMatrix(directory.Path, "Debug");

        var qualification = ReleaseMatrixCheck.Qualify(directory.Path, Revision, "Debug");

        Assert.True(qualification.Passed, string.Join("; ", qualification.Errors));
        Assert.Empty(qualification.Errors);
        using var evidence = JsonDocument.Parse(qualification.Json);
        Assert.True(evidence.RootElement.GetProperty("passed").GetBoolean());
        Assert.Equal(0, evidence.RootElement.GetProperty("totalCleanLaunches").GetInt32());
        Assert.False(evidence.RootElement.GetProperty("publicationEligible").GetBoolean());
        Assert.False(File.Exists(Path.Combine(
            directory.Path,
            "vibesnake-windows-x64-qualification-evidence",
            "candidate_launch_reliability.json")));
    }

    [Fact]
    public void Release_matrix_rejects_missing_platform_evidence()
    {
        using var directory = new TemporaryDirectory();
        WriteMatrix(directory.Path, "Release");
        File.Delete(Path.Combine(
            directory.Path,
            "vibesnake-linux-x64-qualification-evidence",
            "artifact_read_only_install.json"));

        var qualification = ReleaseMatrixCheck.Qualify(directory.Path, Revision, "Release");

        Assert.False(qualification.Passed);
        Assert.Contains(qualification.Errors, error => error.Contains("missing linux-x64 readOnly evidence", StringComparison.Ordinal));
        Assert.Contains(qualification.Errors, error => error.Contains("exactly 3 complete platform rows", StringComparison.Ordinal));
        using var evidence = JsonDocument.Parse(qualification.Json);
        Assert.False(evidence.RootElement.GetProperty("passed").GetBoolean());
    }

    [Fact]
    public void Release_matrix_rejects_dirty_or_mismatched_source_identity()
    {
        using var directory = new TemporaryDirectory();
        WriteMatrix(directory.Path, "Release");
        var path = Path.Combine(directory.Path, "vibesnake-windows-x64-qualification-evidence", "dependency_inventory.json");
        var document = ReadObject(path);
        document["sourceDirty"] = true;
        WriteJson(path, document);

        var qualification = ReleaseMatrixCheck.Qualify(directory.Path, new string('1', 40), "Release");

        Assert.False(qualification.Passed);
        Assert.Contains(qualification.Errors, error => error.Contains("sourceDirty must be False", StringComparison.Ordinal));
        Assert.Contains(qualification.Errors, error => error.Contains("sourceRevision must be", StringComparison.Ordinal));
        using var evidence = JsonDocument.Parse(qualification.Json);
        Assert.False(evidence.RootElement.GetProperty("passed").GetBoolean());
    }

    [Fact]
    public void Release_matrix_rejects_cross_platform_hash_drift()
    {
        using var directory = new TemporaryDirectory();
        WriteMatrix(directory.Path, "Release");
        var path = Path.Combine(directory.Path, "vibesnake-macos-universal-qualification-evidence", "dependency_inventory.json");
        var document = ReadObject(path);
        document["lockSetSha256"] = new string('1', 64);
        WriteJson(path, document);

        var qualification = ReleaseMatrixCheck.Qualify(directory.Path, Revision, "Release");

        Assert.False(qualification.Passed);
        Assert.Contains("all platform dependency inventories must report one lock-set SHA-256", qualification.Errors);
        using var evidence = JsonDocument.Parse(qualification.Json);
        Assert.Equal(JsonValueKind.Null, evidence.RootElement.GetProperty("sharedLockSetSha256").ValueKind);
    }

    [Fact]
    public void Release_matrix_rejects_missing_candidate_lifecycle_evidence()
    {
        using var directory = new TemporaryDirectory();
        WriteMatrix(directory.Path, "Release");
        File.Delete(Path.Combine(
            directory.Path,
            "vibesnake-linux-x64-qualification-evidence",
            "candidate_install_lifecycle.json"));

        var qualification = ReleaseMatrixCheck.Qualify(directory.Path, Revision, "Release");

        Assert.False(qualification.Passed);
        Assert.Contains(qualification.Errors, error => error.Contains("missing linux-x64 candidate lifecycle evidence", StringComparison.Ordinal));
        Assert.Contains(qualification.Errors, error => error.Contains("exactly 3 complete platform rows", StringComparison.Ordinal));
    }

    [Fact]
    public void Release_matrix_rejects_incomplete_save_migration_matrix()
    {
        using var directory = new TemporaryDirectory();
        WriteMatrix(directory.Path, "Release");
        var path = Path.Combine(directory.Path, "vibesnake-windows-x64-qualification-evidence", "candidate_install_lifecycle.json");
        var document = ReadObject(path);
        document["completeSupportedSaveFixtureMatrix"] = false;
        var migrations = document["preferenceMigrations"]!.AsArray();
        migrations.RemoveAt(migrations.Count - 1);
        WriteJson(path, document);

        var qualification = ReleaseMatrixCheck.Qualify(directory.Path, Revision, "Release");

        Assert.False(qualification.Passed);
        Assert.Contains(qualification.Errors, error => error.Contains("completeSupportedSaveFixtureMatrix must be True", StringComparison.Ordinal));
        Assert.Contains(qualification.Errors, error => error.Contains("must cover schemas 1 through 6", StringComparison.Ordinal));
    }

    [Fact]
    public void Release_matrix_rejects_reliability_drift_or_resource_growth()
    {
        using var directory = new TemporaryDirectory();
        WriteMatrix(directory.Path, "Release");
        var path = Path.Combine(directory.Path, "vibesnake-macos-universal-qualification-evidence", "candidate_reliability.json");
        var document = ReadObject(path);
        document["simulations"]![1]!["stepResultsIdentical"] = false;
        document["simulations"]![1]!["decisionAndStateTraceSha256"] = new string('2', 64);
        var samples = document["spectatorRestarts"]!["resourceSamples"]!.AsArray();
        var last = samples[samples.Count - 1]!.AsObject();
        last["objectCount"] = last["objectCount"]!.GetValue<int>() + 1;
        WriteJson(path, document);

        var qualification = ReleaseMatrixCheck.Qualify(directory.Path, Revision, "Release");

        Assert.False(qualification.Passed);
        Assert.Contains(qualification.Errors, error => error.Contains("stepResultsIdentical must be True", StringComparison.Ordinal));
        Assert.Contains(qualification.Errors, error => error.Contains("resources grew across restart samples", StringComparison.Ordinal));
        Assert.Contains(qualification.Errors, error => error.Contains("one vibe trace SHA-256", StringComparison.Ordinal));
    }

    [Fact]
    public void Release_matrix_rejects_incomplete_fault_or_triage_evidence()
    {
        using var directory = new TemporaryDirectory();
        WriteMatrix(directory.Path, "Release");
        var path = Path.Combine(directory.Path, "vibesnake-linux-x64-qualification-evidence", "candidate_fault_campaign.json");
        var document = ReadObject(path);
        document["faults"]![2]!["faultDetected"] = false;
        document["divergenceTriage"]!["privacySafe"] = false;
        document["crashTriage"]!["sha256"] = "bad";
        WriteJson(path, document);

        var qualification = ReleaseMatrixCheck.Qualify(directory.Path, Revision, "Release");

        Assert.False(qualification.Passed);
        Assert.Contains(qualification.Errors, error => error.Contains("faultDetected must be True", StringComparison.Ordinal));
        Assert.Contains(qualification.Errors, error => error.Contains("privacySafe must be True", StringComparison.Ordinal));
        Assert.Contains(qualification.Errors, error => error.Contains("crashTriage.sha256 must be a SHA-256 digest", StringComparison.Ordinal));
    }

    [Fact]
    public void Release_matrix_rejects_performance_drift_or_shared_host_regression()
    {
        using var directory = new TemporaryDirectory();
        WriteMatrix(directory.Path, "Release");
        var path = Path.Combine(directory.Path, "vibesnake-windows-x64-qualification-evidence", "performance.json");
        var document = ReadObject(path);
        document["budget"]!["requiredWarmupFramesPerProfile"] = 29;
        document["profiles"]![2]!["particleCount"] = 161;
        document["measurements"]![2]!["averageFrameMilliseconds"] = Float(25.1);
        document["measurements"]![2]!["p95FrameMilliseconds"] = Float(70.1);
        document["measurements"]![2]!["p99FrameMilliseconds"] = Float(101.0);
        document["measurements"]![2]!["maximumFrameMilliseconds"] = Float(101.0);
        document["finalRulesStateHash"] = new string('5', 16);
        WriteJson(path, document);

        var qualification = ReleaseMatrixCheck.Qualify(directory.Path, Revision, "Release");

        Assert.False(qualification.Passed);
        Assert.Contains(qualification.Errors, error => error.Contains("requiredWarmupFramesPerProfile must be 30", StringComparison.Ordinal));
        Assert.Contains(qualification.Errors, error => error.Contains("stress shape drifted", StringComparison.Ordinal));
        Assert.Contains(qualification.Errors, error => error.Contains("exceeded the shared-host ceiling", StringComparison.Ordinal));
        Assert.Contains(qualification.Errors, error => error.Contains("one rules state hash", StringComparison.Ordinal));
    }

    [Fact]
    public void Release_matrix_rejects_accessibility_drift_or_unbound_source()
    {
        using var directory = new TemporaryDirectory();
        WriteMatrix(directory.Path, "Release");
        var path = Path.Combine(
            directory.Path,
            "vibesnake-macos-universal-qualification-evidence",
            "candidate_accessibility_audit.json");
        var document = ReadObject(path);
        document["auditAreas"]![3]!["automatedPassed"] = false;
        document["displayClasses"]![2]!["textScale"] = Float(1.0);
        document["sources"]![0]!["sha256"] = new string('0', 64);
        WriteJson(path, document);

        var qualification = ReleaseMatrixCheck.Qualify(directory.Path, Revision, "Release");

        Assert.False(qualification.Passed);
        Assert.Contains(qualification.Errors, error => error.Contains("automatedPassed must be True", StringComparison.Ordinal));
        Assert.Contains(qualification.Errors, error => error.Contains("textScale must be 1.5", StringComparison.Ordinal));
        Assert.Contains(
            qualification.Errors,
            error => error.Contains("sha256 does not match accessibility_presentation.json", StringComparison.Ordinal));
    }

    [Fact]
    public void Release_matrix_rejects_booleans_substituted_for_integer_evidence()
    {
        using var directory = new TemporaryDirectory();
        WriteMatrix(directory.Path, "Release");
        var manifestPath = Path.Combine(directory.Path, "vibesnake-windows-x64-manifest", "artifact-manifest.json");
        var manifest = ReadObject(manifestPath);
        manifest["fileCount"] = true;
        WriteJson(manifestPath, manifest);

        var evidenceRoot = Path.Combine(directory.Path, "vibesnake-windows-x64-qualification-evidence");
        var outputPath = Path.Combine(evidenceRoot, "release_output_plan.json");
        var output = ReadObject(outputPath);
        output["schemaVersion"] = true;
        output["packageBytes"] = true;
        WriteJson(outputPath, output);
        var reliabilityPath = Path.Combine(evidenceRoot, "candidate_reliability.json");
        var reliability = ReadObject(reliabilityPath);
        reliability["simulations"]![0]!["runCount"] = true;
        reliability["simulations"]![0]!["restartCount"] = 0;
        WriteJson(reliabilityPath, reliability);

        var qualification = ReleaseMatrixCheck.Qualify(directory.Path, Revision, "Release");

        Assert.False(qualification.Passed);
        Assert.Contains(qualification.Errors, error => error.Contains("manifest.fileCount must match files", StringComparison.Ordinal));
        Assert.Contains(qualification.Errors, error => error.Contains("output.schemaVersion must be 1", StringComparison.Ordinal));
        Assert.Contains(qualification.Errors, error => error.Contains("output.packageBytes must be a positive integer", StringComparison.Ordinal));
        Assert.Contains(qualification.Errors, error => error.Contains("runCount must be a positive integer", StringComparison.Ordinal));
    }

    [Fact]
    public void Release_matrix_rejects_missing_supported_preview_exclusion()
    {
        using var directory = new TemporaryDirectory();
        WriteMatrix(directory.Path, "Release");
        var manifestPath = Path.Combine(directory.Path, "vibesnake-linux-x64-manifest", "artifact-manifest.json");
        var manifest = ReadObject(manifestPath);
        manifest["agentArenaPreviewExcluded"] = false;
        WriteJson(manifestPath, manifest);

        var qualification = ReleaseMatrixCheck.Qualify(directory.Path, Revision, "Release");

        Assert.False(qualification.Passed);
        Assert.Contains(
            qualification.Errors,
            error => error.Contains("manifest.agentArenaPreviewExcluded must be True", StringComparison.Ordinal));
    }

    [Fact]
    public void Release_matrix_rejects_duplicate_fields_and_nonfinite_numbers()
    {
        using var directory = new TemporaryDirectory();
        WriteMatrix(directory.Path, "Release");
        var evidenceRoot = Path.Combine(directory.Path, "vibesnake-linux-x64-qualification-evidence");
        var outputPath = Path.Combine(evidenceRoot, "release_output_plan.json");
        var source = File.ReadAllText(outputPath);
        Assert.Contains("\"schemaVersion\": 1,", source, StringComparison.Ordinal);
        WriteBytes(
            outputPath,
            source.Replace(
                "\"schemaVersion\": 1,",
                "\"schemaVersion\": 1, \"schemaVersion\": 1,",
                StringComparison.Ordinal));
        var performancePath = Path.Combine(evidenceRoot, "performance.json");
        var performanceSource = File.ReadAllText(performancePath);
        const string needle = "\"averageFrameMilliseconds\": 7.0";
        var index = performanceSource.IndexOf(needle, StringComparison.Ordinal);
        Assert.True(index >= 0);
        WriteBytes(
            performancePath,
            performanceSource.Remove(index, needle.Length).Insert(index, "\"averageFrameMilliseconds\": NaN"));

        var qualification = ReleaseMatrixCheck.Qualify(directory.Path, Revision, "Release");

        Assert.False(qualification.Passed);
        Assert.Contains(qualification.Errors, error => error.Contains("duplicate JSON field: schemaVersion", StringComparison.Ordinal));
        Assert.Contains(qualification.Errors, error => error.Contains("non-finite JSON number: NaN", StringComparison.Ordinal));
    }

    [Fact]
    public void Release_matrix_bounds_every_downloaded_json_document()
    {
        using var directory = new TemporaryDirectory();
        WriteMatrix(directory.Path, "Release");

        var qualification = ReleaseMatrixCheck.Qualify(directory.Path, Revision, "Release", 32);

        Assert.False(qualification.Passed);
        Assert.Contains(qualification.Errors, error => error.Contains("exceeds the 32-byte limit", StringComparison.Ordinal));
        using var evidence = JsonDocument.Parse(qualification.Json);
        Assert.False(evidence.RootElement.GetProperty("passed").GetBoolean());
        Assert.False(evidence.RootElement.GetProperty("publicationEligible").GetBoolean());
    }

    [Fact]
    public void Release_matrix_rejects_null_arguments_and_unknown_build_mode()
    {
        var nullQualification = ReleaseMatrixCheck.Qualify(null!, null!, null!);
        Assert.False(nullQualification.Passed);
        Assert.Contains(
            nullQualification.Errors,
            error => error.Contains("lowercase 40-character", StringComparison.Ordinal));
        Assert.False(nullQualification.Json.Contains("\"publicationEligible\": true", StringComparison.Ordinal));

        using var directory = new TemporaryDirectory();
        WriteMatrix(directory.Path, "Release");
        var qualification = ReleaseMatrixCheck.Qualify(directory.Path, Revision, "Profile");
        Assert.False(qualification.Passed);
        Assert.Contains(
            qualification.Errors,
            error => error.Contains("Debug or Release", StringComparison.Ordinal));
    }

    [Fact]
    public void Release_matrix_rejects_shape_drift_across_windows_evidence()
    {
        using var directory = new TemporaryDirectory();
        WriteMatrix(directory.Path, "Release");
        var files = Directory.GetFiles(directory.Path, "*.json", SearchOption.AllDirectories)
            .Where(path => path.Contains("windows-x64", StringComparison.Ordinal))
            .ToArray();
        var failures = 0;
        var attempts = 0;
        foreach (var file in files)
        {
            if (AccessibilitySourceKinds.Any(item => file.EndsWith(item.FileName, StringComparison.Ordinal)))
            {
                continue;
            }

            var original = File.ReadAllBytes(file);
            try
            {
                foreach (var jsonPath in JsonPaths(JsonNode.Parse(original)!))
                {
                    foreach (var kind in ShapeKinds)
                    {
                        var copy = JsonNode.Parse(original)!;
                        SetJsonPath(copy, jsonPath, JsonReplacement(kind));
                        WriteJson(file, copy);
                        var qualification = ReleaseMatrixCheck.Qualify(directory.Path, Revision, "Release");
                        attempts++;
                        Assert.False(qualification.Json.Contains("\"publicationEligible\": true", StringComparison.Ordinal));
                        if (!qualification.Passed)
                        {
                            failures++;
                        }
                    }
                }
            }
            finally
            {
                File.WriteAllBytes(file, original);
            }
        }

        Assert.True(attempts > 50, attempts.ToString(CultureInfo.InvariantCulture));
        Assert.True(failures > 50, failures.ToString(CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Release_matrix_compares_smoke_shapes_mixed_totals_and_nonfinite_tokens()
    {
        (string Expected, string Actual)[] shapes =
        [
            ("true", "true"),
            ("true", "false"),
            ("false", "0"),
            ("1", "1"),
            ("1", "2"),
            ("1", "\"1\""),
            ("-3", "-3"),
            ("-3", "-4"),
            ("1.5", "1.5"),
            ("1.5", "1.25"),
            ("1.5", "\"x\""),
            ("[1, true]", "[1, true]"),
            ("[1, true]", "[1, false]"),
            ("[1, 2]", "[1]"),
            ("\"ab\"", "\"ab\""),
            ("\"ab\"", "\"cd\""),
            ("null", "null"),
            ("null", "1"),
            ("{\"a\":1}", "{\"a\":1}"),
            ("{\"a\":1}", "{\"a\":2}"),
            ("{\"a\":1}", "{\"b\":1}"),
            ("{\"a\":{\"c\":true}}", "{\"a\":{\"c\":false}}"),
            ("9223372036854775807", "1"),
        ];
        foreach (var (expected, actual) in shapes)
        {
            using var directory = new TemporaryDirectory();
            WriteMatrix(directory.Path, "Release");
            SetRawField(SmokePath(directory.Path, manifest: true), "smokeStateHash", expected);
            SetRawField(SmokePath(directory.Path, manifest: false), "smokeStateHash", actual);
            var qualification = ReleaseMatrixCheck.Qualify(directory.Path, Revision, "Release");
            Assert.False(qualification.Json.Contains("\"publicationEligible\": true", StringComparison.Ordinal));
        }

        using (var directory = new TemporaryDirectory())
        {
            WriteMatrix(directory.Path, "Release");
            ReplaceLiteral(SmokePath(directory.Path, manifest: true), "\"" + SmokeHash + "\"", "9223372036854775808");
            ReplaceLiteral(SmokePath(directory.Path, manifest: false), "\"" + SmokeHash + "\"", "9223372036854775808");
            var qualification = ReleaseMatrixCheck.Qualify(directory.Path, Revision, "Release");
            Assert.False(qualification.Passed);
            Assert.False(qualification.Json.Contains("\"publicationEligible\": true", StringComparison.Ordinal));
        }

        using (var directory = new TemporaryDirectory())
        {
            WriteMatrix(directory.Path, "Release");
            SetFaultCount(directory.Path, "windows-x64", "1.5");
            SetFaultCount(directory.Path, "macos-universal", "true");
            SetFaultCount(directory.Path, "linux-x64", "false");
            SetLaunchCount(directory.Path, "windows-x64", "true");
            SetLaunchCount(directory.Path, "macos-universal", "1.5");
            SetLaunchCount(directory.Path, "linux-x64", "\"x\"");
            SetLifecyclePassed(directory.Path, "windows-x64", "false");
            SetLifecyclePassed(directory.Path, "macos-universal", "0");
            SetLifecyclePassed(directory.Path, "linux-x64", "\"\"");
            SetModeId(directory.Path, "windows-x64", "false");
            SetProfileId(directory.Path, "macos-universal", "{}");
            var qualification = ReleaseMatrixCheck.Qualify(directory.Path, Revision, "Release");
            Assert.False(qualification.Json.Contains("\"publicationEligible\": true", StringComparison.Ordinal));
        }

        using (var directory = new TemporaryDirectory())
        {
            WriteMatrix(directory.Path, "Release");
            SetLifecyclePassed(directory.Path, "windows-x64", "\"x\"");
            SetLifecyclePassed(directory.Path, "macos-universal", "[1]");
            SetLifecyclePassed(directory.Path, "linux-x64", "{\"a\":1}");
            SetModeId(directory.Path, "windows-x64", "[]");
            SetModeId(directory.Path, "macos-universal", "1.5");
            SetModeId(directory.Path, "linux-x64", "null");
            SetProfileId(directory.Path, "windows-x64", "true");
            var qualification = ReleaseMatrixCheck.Qualify(directory.Path, Revision, "Release");
            Assert.False(qualification.Json.Contains("\"publicationEligible\": true", StringComparison.Ordinal));
        }

        using (var directory = new TemporaryDirectory())
        {
            WriteMatrix(directory.Path, "Release");
            SetLifecyclePassed(directory.Path, "windows-x64", "[]");
            SetLifecyclePassed(directory.Path, "macos-universal", "{}");
            SetLifecyclePassed(directory.Path, "linux-x64", "null");
            var manifest = Path.Combine(directory.Path, "vibesnake-windows-x64-manifest", "artifact-manifest.json");
            WriteBytes(
                manifest,
                """
                {"quote":"say \"hi\" \\","a":xNaN,"b":ANaN,"c":_NaN,"d":+NaN,"e":.NaN,"f":0NaN,"g":NaN1,"h":NaN}
                """);
            var qualification = ReleaseMatrixCheck.Qualify(directory.Path, Revision, "Release");
            Assert.Contains(qualification.Errors, error => error.Contains("non-finite JSON number: NaN", StringComparison.Ordinal));
            Assert.False(qualification.Json.Contains("\"publicationEligible\": true", StringComparison.Ordinal));
        }

        foreach (var token in new[] { "-Infinity", "Infinity" })
        {
            using var directory = new TemporaryDirectory();
            WriteMatrix(directory.Path, "Release");
            var manifest = Path.Combine(directory.Path, "vibesnake-linux-x64-manifest", "artifact-manifest.json");
            WriteBytes(manifest, "{\"value\": " + token + "}");
            var qualification = ReleaseMatrixCheck.Qualify(directory.Path, Revision, "Release");
            Assert.Contains(
                qualification.Errors,
                error => error.Contains("non-finite JSON number: " + token, StringComparison.Ordinal));
        }
    }

    [Fact]
    public void Command_writes_release_matrix_evidence_for_success_and_failure()
    {
        using var directory = new TemporaryDirectory();
        var downloadRoot = Path.Combine(directory.Path, "download");
        var outputPath = Path.Combine(directory.Path, "nested", "release_matrix.json");
        Directory.CreateDirectory(downloadRoot);
        WriteMatrix(downloadRoot, "Release");
        var output = new StringWriter();
        var error = new StringWriter();

        var code = RepositoryCheckCommand.Run(
            ["release-matrix", downloadRoot, Revision, "Release", outputPath],
            output,
            error);

        Assert.Equal(0, code);
        Assert.Equal(string.Empty, error.ToString());
        Assert.Contains("passed for 3 platforms at " + Revision, output.ToString(), StringComparison.Ordinal);
        Assert.True(File.Exists(outputPath));
        using var written = JsonDocument.Parse(File.ReadAllBytes(outputPath));
        Assert.True(written.RootElement.GetProperty("passed").GetBoolean());
        Assert.False(written.RootElement.GetProperty("publicationEligible").GetBoolean());
        Assert.Equal("release-matrix-qualification-v1", written.RootElement.GetProperty("kind").GetString());

        var failedOutput = Path.Combine(directory.Path, "failed.json");
        var failedStdout = new StringWriter();
        var failedStderr = new StringWriter();
        var failedCode = RepositoryCheckCommand.Run(
            ["release-matrix", downloadRoot, new string('1', 40), "Release", failedOutput],
            failedStdout,
            failedStderr);
        Assert.Equal(1, failedCode);
        Assert.Equal(string.Empty, failedStdout.ToString());
        Assert.Contains("Release matrix qualification failed:", failedStderr.ToString(), StringComparison.Ordinal);
        Assert.True(File.Exists(failedOutput));
        using var failed = JsonDocument.Parse(File.ReadAllBytes(failedOutput));
        Assert.False(failed.RootElement.GetProperty("passed").GetBoolean());
        Assert.False(failed.RootElement.GetProperty("publicationEligible").GetBoolean());
    }

    private static void WriteMatrix(string root, string buildMode)
    {
        foreach (var platform in Platforms)
        {
            WritePlatform(root, platform, buildMode);
        }
    }

    private static void WritePlatform(string root, string platform, string buildMode)
    {
        var evidenceRoot = Path.Combine(root, $"vibesnake-{platform}-qualification-evidence");
        var manifestPath = Path.Combine(root, $"vibesnake-{platform}-manifest", "artifact-manifest.json");
        WriteJson(manifestPath, Manifest(platform, buildMode));
        var manifestSha = Hash(manifestPath);
        const string fileHash = "eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee";
        WriteJson(
            Path.Combine(evidenceRoot, "artifact_read_only_install.json"),
            new JsonObject
            {
                ["schemaVersion"] = 1,
                ["kind"] = "artifact-read-only-install-v1",
                ["passed"] = true,
                ["platformId"] = platform,
                ["sourceRevision"] = Revision,
                ["smokeStateHash"] = SmokeHash,
                ["writeProbeRejected"] = true,
                ["installUnchanged"] = true,
                ["userDataOutsideInstall"] = true,
                ["logOutsideInstall"] = true,
                ["evidenceOutsideInstall"] = true,
                ["installPathQualified"] = true,
                ["userDataPathQualified"] = true,
                ["logPathQualified"] = true,
                ["freshProfile"] = true,
                ["beforeSha256"] = fileHash,
                ["afterSha256"] = fileHash,
            });
        WriteJson(
            Path.Combine(evidenceRoot, "dependency_inventory.json"),
            new JsonObject
            {
                ["schemaVersion"] = 1,
                ["kind"] = "dependency-inventory-v1",
                ["generatedFromLocksOnly"] = true,
                ["sourceRevision"] = Revision,
                ["sourceDirty"] = false,
                ["runtimeIdentifier"] = platform,
                ["lockSetSha256"] = LockHash,
                ["packages"] = new JsonArray(new JsonObject { ["name"] = "example" }),
            });
        WriteJson(
            Path.Combine(evidenceRoot, "release_signing_readiness.json"),
            new JsonObject
            {
                ["schemaVersion"] = 1,
                ["kind"] = "release-signing-readiness-v1",
                ["product"] = "Vibe Snake",
                ["platform"] = platform,
                ["sourceRevision"] = Revision,
                ["buildMode"] = buildMode,
                ["artifactManifestSha256"] = manifestSha,
                ["signingState"] = "unsigned-input",
                ["passed"] = true,
                ["ordinaryCiCredentialAccess"] = false,
                ["signingMaterialAllowedInRepository"] = false,
                ["signingMaterialAllowedInArtifacts"] = false,
            });
        var extension = platform == "linux-x64" ? ".tar.gz" : ".zip";
        WriteJson(
            Path.Combine(evidenceRoot, "release_output_plan.json"),
            new JsonObject
            {
                ["schemaVersion"] = 1,
                ["kind"] = "release-output-plan-v1",
                ["product"] = "Vibe Snake",
                ["productVersion"] = "0.3.0-alpha.1",
                ["platform"] = platform,
                ["passed"] = true,
                ["qualificationOnly"] = true,
                ["assemblyEligible"] = true,
                ["optionalPackOutputSeparate"] = true,
                ["playerDataExcluded"] = true,
                ["uninstallPreservesPlayerData"] = true,
                ["deterministicRepeatMatched"] = true,
                ["publicationEligible"] = false,
                ["baseGameIncludesOptionalPacks"] = false,
                ["packageSha256"] = new string('f', 64),
                ["packageBytes"] = 100,
                ["directDownloadFileName"] = $"VibeSnake-0.3.0-alpha.1-{platform}-qualification{extension}",
            });
        WriteJson(Path.Combine(evidenceRoot, "candidate_reliability.json"), Reliability());
        WriteJson(Path.Combine(evidenceRoot, "candidate_fault_campaign.json"), FaultCampaign());
        WriteJson(Path.Combine(evidenceRoot, "performance.json"), Performance());
        WriteJson(Path.Combine(evidenceRoot, "candidate_accessibility_audit.json"), AccessibilityAudit(evidenceRoot));
        if (buildMode == "Release")
        {
            WriteJson(Path.Combine(evidenceRoot, "candidate_launch_reliability.json"), Launches(platform));
            WriteJson(Path.Combine(evidenceRoot, "candidate_install_lifecycle.json"), Lifecycle(platform));
        }
    }

    private static JsonObject Manifest(string platform, string buildMode) => new()
    {
        ["schemaVersion"] = 3,
        ["product"] = "Vibe Snake",
        ["platform"] = platform,
        ["buildMode"] = buildMode,
        ["sourceRevision"] = Revision,
        ["smokeStateHash"] = SmokeHash,
        ["agentArenaPreviewExcluded"] = buildMode == "Release",
        ["fileCount"] = 1,
        ["totalBytes"] = 10,
        ["files"] = new JsonArray(new JsonObject
        {
            ["path"] = "player",
            ["bytes"] = 10,
            ["sha256"] = new string('d', 64),
        }),
    };

    private static JsonObject Reliability()
    {
        var simulations = new JsonArray
        {
            Simulation("classic", "classic-standard-v1", 84),
            Simulation("vibe", "vibe-standard-v1-dda-on", 82),
        };
        return new JsonObject
        {
            ["schemaVersion"] = 1,
            ["kind"] = "candidate-reliability-qualification-v1",
            ["passed"] = true,
            ["requiredStepsPerRuleset"] = 100_000,
            ["rulesetCount"] = 2,
            ["totalComparedSimulationSteps"] = 200_000,
            ["referenceAiId"] = "balanced",
            ["aiAlgorithmId"] = "native-personality-controller-v2",
            ["randomAlgorithmId"] = "pcg-xsh-rr-32-v1",
            ["simulations"] = simulations,
            ["spectatorRestarts"] = new JsonObject
            {
                ["requiredRestarts"] = 100,
                ["completedRestarts"] = 100,
                ["stepsPerRestart"] = 8,
                ["completedSteps"] = 800,
                ["stateResetCount"] = 100,
                ["everyFreshSessionStartedPaused"] = true,
                ["everyFreshSessionResetState"] = true,
                ["everySessionAdvanced"] = true,
                ["managedSessionReferencesRetained"] = 0,
                ["engineNodeCountStable"] = true,
                ["engineObjectCountDidNotGrow"] = true,
                ["engineResourceCountDidNotGrow"] = true,
                ["engineOrphanNodeCountDidNotGrow"] = true,
                ["noMonotonicStateOrResourceGrowth"] = true,
                ["resourceSamples"] = ResourceSamples(),
            },
            ["pendingGates"] = new JsonArray("retained-release-execution-on-windows-macos-linux"),
        };
    }

    private static JsonObject Simulation(string modeId, string scoreCategory, int runCount) => new()
    {
        ["modeId"] = modeId,
        ["modeVersion"] = 1,
        ["scoreCategoryId"] = scoreCategory,
        ["referenceAiId"] = "balanced",
        ["requiredComparedSteps"] = 100_000,
        ["comparedSteps"] = 100_000,
        ["runCount"] = runCount,
        ["restartCount"] = runCount - 1,
        ["stateHashCheckpointCount"] = 180,
        ["decisionsIdentical"] = true,
        ["queueOutcomesIdentical"] = true,
        ["stepResultsIdentical"] = true,
        ["decisionAndStateTraceSha256"] = new string('1', 64),
        ["firstDivergence"] = null,
    };

    private static JsonArray ResourceSamples()
    {
        var samples = new JsonArray();
        for (var restart = 0; restart <= 100; restart += 10)
        {
            samples.Add(new JsonObject
            {
                ["completedRestarts"] = restart,
                ["sceneNodeCount"] = 9,
                ["objectCount"] = 1683,
                ["resourceCount"] = 2,
                ["orphanNodeCount"] = 0,
            });
        }

        return samples;
    }

    private static JsonObject FaultCampaign()
    {
        var faults = new JsonArray();
        foreach (var faultId in new[]
        {
            "interrupted-write",
            "corrupt-json",
            "full-disk",
            "read-only-data-directory",
            "missing-resource",
            "invalid-content-pack",
            "unavailable-audio",
        })
        {
            faults.Add(new JsonObject
            {
                ["faultId"] = faultId,
                ["injectionBoundary"] = "production-boundary",
                ["faultDetected"] = true,
                ["existingDataPreserved"] = true,
                ["recoveryVerified"] = true,
                ["rulesStateUnchanged"] = true,
            });
        }

        return new JsonObject
        {
            ["schemaVersion"] = 1,
            ["kind"] = "candidate-fault-campaign-v1",
            ["passed"] = true,
            ["requiredFaultCount"] = 7,
            ["completedFaultCount"] = 7,
            ["everyFaultDetected"] = true,
            ["everyExistingDataBoundaryPreserved"] = true,
            ["everyRecoveryPathVerified"] = true,
            ["rulesStateUnchangedAcrossCampaign"] = true,
            ["faults"] = faults,
            ["crashTriage"] = Triage("crash-report", "crash.vibesnake-diagnostic.json", new string('2', 64)),
            ["divergenceTriage"] = Triage("deterministic-divergence-report-v1", "divergence.vibesnake-divergence.json", new string('3', 64)),
            ["pendingGates"] = new JsonArray("retained-release-execution-on-windows-macos-linux"),
        };
    }

    private static JsonObject Triage(string kind, string fileName, string sha) => new()
    {
        ["reportKind"] = kind,
        ["reportRetained"] = true,
        ["schemaValid"] = true,
        ["privacySafe"] = true,
        ["reproductionFieldsComplete"] = true,
        ["fileName"] = fileName,
        ["sha256"] = sha,
    };

    private static JsonObject Performance()
    {
        var profiles = new JsonArray
        {
            Profile("minimum", 64, 0, 2, 0, 0, 88),
            Profile("default", 512, 3, 2, 64, 2, 610),
            Profile("maximum-safe", 2107, 3, 2, 160, 3, 2303),
        };
        var measurements = new JsonArray
        {
            Measurement("minimum"),
            Measurement("default"),
            Measurement("maximum-safe"),
        };
        return new JsonObject
        {
            ["schemaVersion"] = 1,
            ["kind"] = "performance-qualification-v1",
            ["passed"] = true,
            ["threeEffectProfilesMeasured"] = true,
            ["maximumMixedStressSceneComplete"] = true,
            ["frameStatisticsComplete"] = true,
            ["sharedHostRegressionCeilingMet"] = true,
            ["particleBudgetConsistent"] = true,
            ["audioChannelBudgetConsistent"] = true,
            ["drawSubmissionBudgetMet"] = true,
            ["feedbackCannotChangeSimulationSpeed"] = true,
            ["rulesStateIdenticalAcrossProfiles"] = true,
            ["finalRulesStateHash"] = new string('4', 16),
            ["rulesStepsPerProfile"] = 256,
            ["minimumHardwareAcceptanceStatus"] = "pending-named-hardware",
            ["budget"] = new JsonObject
            {
                ["targetFramesPerSecond"] = 60,
                ["targetFrameMilliseconds"] = Float(1000d / 60d),
                ["sharedHostMaximumAverageMilliseconds"] = 25,
                ["sharedHostMaximumP95Milliseconds"] = 70,
                ["maximumLogicalDrawSubmissions"] = 2400,
                ["maximumParticles"] = 160,
                ["maximumAudioChannels"] = 12,
                ["boardCellCapacity"] = 2112,
                ["requiredWarmupFramesPerProfile"] = 30,
                ["requiredSamplesPerProfile"] = 40,
            },
            ["profiles"] = profiles,
            ["measurements"] = measurements,
            ["pendingHumanChecks"] = new JsonArray(
                "Windows named minimum hardware",
                "macOS named minimum hardware",
                "Linux named minimum hardware",
                "Long-session resource and thermal review"),
        };
    }

    private static JsonObject Profile(
        string id,
        int snakeCells,
        int obstacles,
        int collectibles,
        int particles,
        int popups,
        int drawSubmissions) => new()
        {
            ["id"] = id,
            ["snakeCellCount"] = snakeCells,
            ["obstacleCount"] = obstacles,
            ["visibleCollectibleCount"] = collectibles,
            ["particleCount"] = particles,
            ["popupCount"] = popups,
            ["fullScreenFlashCount"] = 0,
            ["logicalDrawSubmissionCount"] = drawSubmissions,
        };

    private static JsonObject Measurement(string id) => new()
    {
        ["id"] = id,
        ["sampleCount"] = 40,
        ["averageFrameMilliseconds"] = Float(7.0),
        ["p50FrameMilliseconds"] = Float(7.0),
        ["p95FrameMilliseconds"] = Float(8.0),
        ["p99FrameMilliseconds"] = Float(9.0),
        ["maximumFrameMilliseconds"] = Float(10.0),
        ["driverDrawCallStatus"] = "unavailable-headless-backend",
        ["averageObservedDriverDrawCalls"] = 0,
        ["maximumObservedDriverDrawCalls"] = 0,
    };

    private static JsonObject AccessibilityAudit(string evidenceRoot)
    {
        var sources = new JsonArray();
        foreach (var (fileName, kind) in AccessibilitySourceKinds)
        {
            var path = Path.Combine(evidenceRoot, fileName);
            WriteJson(path, new JsonObject { ["kind"] = kind, ["passed"] = true });
            sources.Add(new JsonObject
            {
                ["fileName"] = fileName,
                ["kind"] = kind,
                ["sha256"] = Hash(path),
            });
        }

        var areas = new JsonArray();
        foreach (var areaId in AccessibilityAreaIds)
        {
            areas.Add(new JsonObject
            {
                ["id"] = areaId,
                ["automatedPassed"] = true,
                ["evidenceFiles"] = new JsonArray("qualified-source.json"),
            });
        }

        var displays = new JsonArray();
        foreach (var row in DisplayRows)
        {
            displays.Add(new JsonObject
            {
                ["id"] = row.Id,
                ["requestedWidth"] = row.RequestedWidth,
                ["requestedHeight"] = row.RequestedHeight,
                ["effectiveWidth"] = row.EffectiveWidth,
                ["effectiveHeight"] = row.EffectiveHeight,
                ["viewportScale"] = Float(row.ViewportScale),
                ["textScale"] = Float(1.5),
                ["logicalLayoutComplete"] = true,
            });
        }

        return new JsonObject
        {
            ["schemaVersion"] = 1,
            ["kind"] = "candidate-accessibility-audit-v1",
            ["passed"] = true,
            ["requiredFlowDefectSeverity"] = "P1",
            ["auditAreaCount"] = 12,
            ["allAutomatedAuditAreasPassed"] = true,
            ["keyboardOnlyRouteComplete"] = true,
            ["controllerOnlyRouteComplete"] = true,
            ["remappingComplete"] = true,
            ["singleActionNavigationComplete"] = true,
            ["independentAudioControlsComplete"] = true,
            ["monoOutputComplete"] = true,
            ["visualAlternativesComplete"] = true,
            ["reducedMotionComplete"] = true,
            ["flashSafetyComplete"] = true,
            ["maximumTextScaleViewportMatrixComplete"] = true,
            ["maximumTextScale"] = Float(1.5),
            ["supportedDisplayClassCount"] = 8,
            ["maximumTextScaleDisplayClassCount"] = 8,
            ["accessibilityUserReviewStatus"] = "pending-accessibility-user-review",
            ["featureGuidePath"] = "docs/guides/ACCESSIBILITY.md",
            ["featurePublicationStatus"] = "published-in-repository",
            ["auditAreas"] = areas,
            ["displayClasses"] = displays,
            ["sources"] = sources,
            ["pendingHumanChecks"] = new JsonArray(
                "retained-visible-audit-windows-macos-linux",
                "maximum-text-scale-platform-captures",
                "physical-keyboard-and-controller-only-flow-review",
                "players-using-relevant-accessibility-settings",
                "human-focus-contrast-readability-photosensitivity-review"),
        };
    }

    private static JsonObject Launches(string platform) => new()
    {
        ["schemaVersion"] = 1,
        ["kind"] = "candidate-launch-reliability-v1",
        ["passed"] = true,
        ["platformId"] = platform,
        ["buildMode"] = "Release",
        ["sourceRevision"] = Revision,
        ["requestedLaunches"] = 100,
        ["completedLaunches"] = 100,
        ["freshProfileLaunches"] = 100,
        ["readOnlyInstall"] = true,
        ["headless"] = true,
        ["failures"] = new JsonArray(),
    };

    private static JsonObject Lifecycle(string platform)
    {
        var preferences = new JsonArray();
        for (var schema = 1; schema <= 6; schema++)
        {
            preferences.Add(new JsonObject
            {
                ["inputSchema"] = schema,
                ["effectiveSchema"] = 7,
                ["loadCode"] = "Success",
                ["sourcePreserved"] = true,
            });
        }

        return new JsonObject
        {
            ["schemaVersion"] = 1,
            ["kind"] = "candidate-install-lifecycle-preflight-v1",
            ["passed"] = true,
            ["platformId"] = platform,
            ["buildMode"] = "Release",
            ["sourceRevision"] = Revision,
            ["firstInstallPassed"] = true,
            ["readOnlyInstallPassed"] = true,
            ["noElevationRequested"] = true,
            ["nonAsciiInstallAndUserPathsPassed"] = true,
            ["repairSnapshotMatched"] = true,
            ["repairLaunchPassed"] = true,
            ["preferenceMigrationFixtureCount"] = 6,
            ["preferenceMigrations"] = preferences,
            ["additionalSaveMigrationFixtureCount"] = 2,
            ["additionalSaveMigrations"] = new JsonArray(
                new JsonObject
                {
                    ["fixture"] = "personal-best-schema-1",
                    ["effectiveSchema"] = 2,
                    ["loadCode"] = "Success",
                    ["sourcePreserved"] = true,
                },
                new JsonObject
                {
                    ["fixture"] = "local-playtest-summary-schema-1",
                    ["effectiveSchema"] = 2,
                    ["loadCode"] = "Success",
                    ["sourcePreserved"] = true,
                }),
            ["supportedSaveMigrationFixtureCount"] = 8,
            ["futureSchemaRejectedAndPreserved"] = true,
            ["rollbackNeverOverwritesNewerPreferences"] = true,
            ["optionalPackAddRemovalRestorePassed"] = true,
            ["dataResetBackupRestorePassed"] = true,
            ["applicationRemovalPreservedPlayerData"] = true,
            ["completeSupportedSaveFixtureMatrix"] = true,
            ["remainingGates"] = new JsonArray(
                "selected-channel-installer-lifecycle",
                "cross-version-binary-rollback"),
        };
    }

    private static JsonNode Float(double value)
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

        return JsonNode.Parse(text)!;
    }

    private static void WriteJson(string path, JsonNode node)
    {
        var parent = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(parent))
        {
            Directory.CreateDirectory(parent);
        }

        var json = node.ToJsonString(JsonOptions).Replace("\r\n", "\n", StringComparison.Ordinal);
        if (!json.EndsWith('\n'))
        {
            json += "\n";
        }

        WriteBytes(path, json);
    }

    private static void WriteBytes(string path, string json) =>
        File.WriteAllBytes(path, Encoding.UTF8.GetBytes(json));

    private static JsonObject ReadObject(string path) => JsonNode.Parse(File.ReadAllText(path))!.AsObject();

    private static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));

    private static string SmokePath(string root, bool manifest) => manifest
        ? Path.Combine(root, "vibesnake-windows-x64-manifest", "artifact-manifest.json")
        : Path.Combine(root, "vibesnake-windows-x64-qualification-evidence", "artifact_read_only_install.json");

    private static void SetRawField(string path, string field, string jsonLiteral)
    {
        var document = ReadObject(path);
        document[field] = JsonNode.Parse(jsonLiteral);
        WriteJson(path, document);
    }

    private static void ReplaceLiteral(string path, string needle, string replacement)
    {
        var source = File.ReadAllText(path);
        var index = source.IndexOf(needle, StringComparison.Ordinal);
        Assert.True(index >= 0, needle);
        WriteBytes(path, source.Remove(index, needle.Length).Insert(index, replacement));
    }

    private static void SetFaultCount(string root, string platform, string jsonLiteral) =>
        SetRawField(
            Path.Combine(root, $"vibesnake-{platform}-qualification-evidence", "candidate_fault_campaign.json"),
            "completedFaultCount",
            jsonLiteral);

    private static void SetLaunchCount(string root, string platform, string jsonLiteral) =>
        SetRawField(
            Path.Combine(root, $"vibesnake-{platform}-qualification-evidence", "candidate_launch_reliability.json"),
            "completedLaunches",
            jsonLiteral);

    private static void SetLifecyclePassed(string root, string platform, string jsonLiteral) =>
        SetRawField(
            Path.Combine(root, $"vibesnake-{platform}-qualification-evidence", "candidate_install_lifecycle.json"),
            "passed",
            jsonLiteral);

    private static void SetModeId(string root, string platform, string jsonLiteral)
    {
        var path = Path.Combine(root, $"vibesnake-{platform}-qualification-evidence", "candidate_reliability.json");
        var document = ReadObject(path);
        document["simulations"]![0]!["modeId"] = JsonNode.Parse(jsonLiteral);
        WriteJson(path, document);
    }

    private static void SetProfileId(string root, string platform, string jsonLiteral)
    {
        var path = Path.Combine(root, $"vibesnake-{platform}-qualification-evidence", "performance.json");
        var document = ReadObject(path);
        document["profiles"]![0]!["id"] = JsonNode.Parse(jsonLiteral);
        WriteJson(path, document);
    }

    private static List<string[]> JsonPaths(JsonNode node)
    {
        var paths = new List<string[]>();
        CollectJsonPaths(node, [], paths);
        return paths;
    }

    private static void CollectJsonPaths(JsonNode? node, List<string> prefix, List<string[]> paths)
    {
        if (node is JsonObject obj)
        {
            foreach (var property in obj)
            {
                prefix.Add(property.Key);
                paths.Add([.. prefix]);
                CollectJsonPaths(property.Value, prefix, paths);
                prefix.RemoveAt(prefix.Count - 1);
            }

            return;
        }

        if (node is not JsonArray array || array.Count == 0)
        {
            return;
        }

        prefix.Add("0");
        paths.Add([.. prefix]);
        CollectJsonPaths(array[0], prefix, paths);
        prefix.RemoveAt(prefix.Count - 1);
    }

    private static void SetJsonPath(JsonNode root, string[] path, JsonNode value)
    {
        JsonNode current = root;
        for (var index = 0; index < path.Length - 1; index++)
        {
            current = NextJson(current, path[index]);
        }

        if (current is JsonArray array)
        {
            array[int.Parse(path[^1], CultureInfo.InvariantCulture)] = value;
            return;
        }

        current.AsObject()[path[^1]] = value;
    }

    private static JsonNode NextJson(JsonNode current, string segment) => current is JsonArray array
        ? array[int.Parse(segment, CultureInfo.InvariantCulture)]!
        : current[segment]!;

    private static JsonNode JsonReplacement(string kind) => kind switch
    {
        "bool" => JsonValue.Create(true)!,
        "string" => JsonValue.Create("bad")!,
        "empty" => new JsonArray(),
        "null" => JsonNode.Parse("null")!,
        "float" => JsonValue.Create(1.5d)!,
        "negative" => JsonValue.Create(-1)!,
        _ => throw new InvalidOperationException(kind),
    };

    private static string[] Files(string root) =>
        Directory.GetFiles(root, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal).ToArray();

    private static string[] Names(JsonElement element) =>
        element.EnumerateObject().Select(property => property.Name).ToArray();

    private static string[] Strings(JsonElement element, string property) =>
        element.EnumerateArray().Select(item => item.GetProperty(property).GetString()!).ToArray();

    private static string[] Strings(JsonElement element) =>
        element.EnumerateArray().Select(item => item.GetString()!).ToArray();

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "vibesnake-release-matrix-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
