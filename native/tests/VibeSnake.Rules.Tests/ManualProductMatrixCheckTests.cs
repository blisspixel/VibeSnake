using System.Globalization;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using RepositoryChecks;

namespace VibeSnake.Rules.Tests;

public sealed class ManualProductMatrixCheckTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true,
    };

    private static readonly (string Id, string Artifact, string Architecture)[] Platforms =
    [
        ("windows-x64", "windows-x64", "x86_64"),
        ("macos-universal-apple-silicon", "macos-universal", "arm64"),
        ("macos-universal-intel", "macos-universal", "x86_64"),
        ("linux-x64", "linux-x64", "x86_64"),
    ];

    private static readonly string[] CompleteFlowDevices =
    [
        "keyboard",
        "xbox-layout-controller",
        "playstation-layout-controller",
    ];

    private static readonly string[] InputDeviceIds =
    [
        "keyboard",
        "mouse",
        "xbox-layout-controller",
        "playstation-layout-controller",
    ];

    private static readonly string[] MouseCapabilities =
    [
        "menu-targeting",
        "settings-navigation",
        "gameplay-direction",
        "back",
    ];

    private static readonly string[] SettingsProfiles =
    [
        "sound-device-absent",
        "sound-muted",
        "zero-shake",
        "reduced-motion",
        "flash-free",
        "high-contrast",
        "maximum-text-scale",
        "missing-optional-content",
    ];

    [Fact]
    public void Repository_handoff_is_pending_and_repeatable()
    {
        var root = ResolveRepositoryRoot();
        var evaluation = ManualProductMatrixCheck.Evaluate(root, null, null);
        using var evidence = JsonDocument.Parse(evaluation.Json);

        Assert.Empty(evaluation.Errors);
        Assert.True(evaluation.Passed);
        Assert.Equal(2, evidence.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("manual-product-matrix-handoff-v2", evidence.RootElement.GetProperty("kind").GetString());
        Assert.True(evidence.RootElement.GetProperty("passed").GetBoolean());
        Assert.True(evidence.RootElement.GetProperty("protocolQualified").GetBoolean());
        Assert.Equal(4, evidence.RootElement.GetProperty("platformRowCount").GetInt32());
        Assert.Equal(36, evidence.RootElement.GetProperty("requiredFlowCount").GetInt32());
        Assert.Equal(144, evidence.RootElement.GetProperty("requiredPlatformFlowCellCount").GetInt32());
        Assert.Equal(432, evidence.RootElement.GetProperty("requiredDeviceFlowCellCount").GetInt32());
        Assert.Equal(16, evidence.RootElement.GetProperty("requiredMouseCapabilityCellCount").GetInt32());
        Assert.Equal(32, evidence.RootElement.GetProperty("requiredPlatformProfileCellCount").GetInt32());
        Assert.Equal(4, evidence.RootElement.GetProperty("inputDeviceCount").GetInt32());
        Assert.Equal(8, evidence.RootElement.GetProperty("settingsProfileCount").GetInt32());
        Assert.Equal(0, evidence.RootElement.GetProperty("manualSessionCount").GetInt32());
        Assert.False(evidence.RootElement.GetProperty("manualExecutionComplete").GetBoolean());
        Assert.False(evidence.RootElement.GetProperty("releaseAcceptance").GetBoolean());
        Assert.Equal(5, evidence.RootElement.GetProperty("pendingGates").GetArrayLength());
        Assert.Equal(
            Hash(Path.Combine(root, "config", "qa_manual_product_matrix_v2.json")),
            evidence.RootElement.GetProperty("contractSha256").GetString());

        var first = Path.Combine(Path.GetTempPath(), "vibesnake-manual-handoff-" + Guid.NewGuid().ToString("N") + ".json");
        var second = Path.Combine(Path.GetTempPath(), "vibesnake-manual-handoff-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            Assert.True(ManualProductMatrixCheck.WriteFoundationHandoff(root, first).Passed);
            Assert.True(ManualProductMatrixCheck.WriteFoundationHandoff(root, second).Passed);
            Assert.Equal(File.ReadAllBytes(first), File.ReadAllBytes(second));
            Assert.Equal(evaluation.Json, File.ReadAllText(first));
        }
        finally
        {
            DeleteIfExists(first);
            DeleteIfExists(second);
        }
    }

    [Fact]
    public void Contract_rejects_a_missing_required_flow()
    {
        var root = ResolveRepositoryRoot();
        var contract = JsonNode.Parse(File.ReadAllText(Path.Combine(root, "config", "qa_manual_product_matrix_v2.json")))!.AsObject();
        var flows = contract["requiredFlows"]!.AsArray();
        flows.Remove(flows.First(item => item!.GetValue<string>() == "quit")!);
        var path = WriteTemp("contract.json", contract);

        try
        {
            var evaluation = ManualProductMatrixCheck.Evaluate(root, null, null, path);
            Assert.Contains(evaluation.Errors, error => error.Contains("contract.requiredFlows must be", StringComparison.Ordinal));
            Assert.False(evaluation.Passed);
        }
        finally
        {
            DeleteIfExists(path);
        }
    }

    [Fact]
    public void Complete_retained_sessions_close_every_matrix_dimension()
    {
        var root = ResolveRepositoryRoot();
        using var directory = new TemporaryDirectory();
        var sessions = Path.Combine(directory.Path, "sessions");
        var candidate = WriteCandidate(Path.Combine(directory.Path, "candidate.json"));
        WriteCompleteMatrix(sessions);

        var evaluation = ManualProductMatrixCheck.Evaluate(root, sessions, candidate);
        using var evidence = JsonDocument.Parse(evaluation.Json);

        Assert.True(evaluation.Passed, string.Join(Environment.NewLine, evaluation.Errors));
        Assert.Equal(16, evidence.RootElement.GetProperty("manualSessionCount").GetInt32());
        Assert.Equal(144, evidence.RootElement.GetProperty("completedPlatformFlowCellCount").GetInt32());
        Assert.Equal(432, evidence.RootElement.GetProperty("completedDeviceFlowCellCount").GetInt32());
        Assert.Equal(16, evidence.RootElement.GetProperty("completedMouseCapabilityCellCount").GetInt32());
        Assert.Equal(32, evidence.RootElement.GetProperty("completedPlatformProfileCellCount").GetInt32());
        Assert.Equal(InputDeviceIds.Order(StringComparer.Ordinal).ToArray(), Strings(evidence.RootElement.GetProperty("observedInputDevices")));
        Assert.Equal(SettingsProfiles.Order(StringComparer.Ordinal).ToArray(), Strings(evidence.RootElement.GetProperty("observedSettingsProfiles")));
        Assert.Equal(0, evidence.RootElement.GetProperty("failedOrBlockedResultCount").GetInt32());
        Assert.True(evidence.RootElement.GetProperty("manualExecutionComplete").GetBoolean());
        Assert.True(evidence.RootElement.GetProperty("releaseAcceptance").GetBoolean());
        Assert.True(evidence.RootElement.GetProperty("candidateQualified").GetBoolean());
        Assert.Equal(new string('a', 40), evidence.RootElement.GetProperty("candidateRevision").GetString());
        Assert.Equal(0, evidence.RootElement.GetProperty("pendingGates").GetArrayLength());
    }

    [Fact]
    public void One_device_per_platform_cannot_claim_complete_device_coverage()
    {
        var root = ResolveRepositoryRoot();
        using var directory = new TemporaryDirectory();
        var sessions = Path.Combine(directory.Path, "sessions");
        var candidate = WriteCandidate(Path.Combine(directory.Path, "candidate.json"));
        for (var index = 0; index < Platforms.Length; index++)
        {
            var device = InputDeviceIds[index];
            var session = Session(Platforms[index].Id, index, index, device, RequiredFlows());
            if (device == "mouse")
            {
                session["results"]!.AsArray()[0]!["inputCapabilityIds"] = StringArray(MouseCapabilities);
            }

            WriteSession(Path.Combine(sessions, "session-" + index.ToString(CultureInfo.InvariantCulture) + ".json"), session);
        }

        var evaluation = ManualProductMatrixCheck.Evaluate(root, sessions, candidate);
        using var evidence = JsonDocument.Parse(evaluation.Json);

        Assert.Equal(144, evidence.RootElement.GetProperty("completedPlatformFlowCellCount").GetInt32());
        Assert.False(evidence.RootElement.GetProperty("manualExecutionComplete").GetBoolean());
        Assert.False(evidence.RootElement.GetProperty("releaseAcceptance").GetBoolean());
        Assert.Contains(
            evaluation.Errors,
            error => error.Contains("windows-x64 xbox-layout-controller is missing passing flows", StringComparison.Ordinal));
    }

    [Fact]
    public void Complete_device_and_mouse_capability_gaps_fail_closed()
    {
        var root = ResolveRepositoryRoot();
        using var directory = new TemporaryDirectory();
        var sessions = Path.Combine(directory.Path, "sessions");
        var candidate = WriteCandidate(Path.Combine(directory.Path, "candidate.json"));
        var paths = WriteCompleteMatrix(sessions);
        var keyboard = JsonNode.Parse(File.ReadAllText(paths[0]))!.AsObject();
        var keyboardResults = keyboard["results"]!.AsArray();
        keyboardResults.RemoveAt(keyboardResults.Count - 1);
        WriteJson(paths[0], keyboard);
        var mouse = JsonNode.Parse(File.ReadAllText(paths[3]))!.AsObject();
        var mouseResults = mouse["results"]!.AsArray();
        mouseResults[mouseResults.Count - 1]!["inputCapabilityIds"] = new JsonArray();
        WriteJson(paths[3], mouse);

        var evaluation = ManualProductMatrixCheck.Evaluate(root, sessions, candidate);
        using var evidence = JsonDocument.Parse(evaluation.Json);

        Assert.False(evidence.RootElement.GetProperty("manualExecutionComplete").GetBoolean());
        Assert.Contains(
            evaluation.Errors,
            error => error.Contains("windows-x64 keyboard is missing passing flows: quit", StringComparison.Ordinal));
        Assert.Contains(
            evaluation.Errors,
            error => error.Contains("windows-x64 mouse is missing passing capabilities: back", StringComparison.Ordinal));
    }

    [Fact]
    public void Failed_profile_observation_earns_no_coverage_and_remains_fatal()
    {
        var root = ResolveRepositoryRoot();
        using var directory = new TemporaryDirectory();
        var sessions = Path.Combine(directory.Path, "sessions");
        var candidate = WriteCandidate(Path.Combine(directory.Path, "candidate.json"));
        var paths = WriteCompleteMatrix(sessions);
        var document = JsonNode.Parse(File.ReadAllText(paths[0]))!.AsObject();
        document["results"]!.AsArray()[0]!["result"] = "fail";
        WriteJson(paths[0], document);

        var evaluation = ManualProductMatrixCheck.Evaluate(root, sessions, candidate);
        using var evidence = JsonDocument.Parse(evaluation.Json);

        Assert.False(evidence.RootElement.GetProperty("manualExecutionComplete").GetBoolean());
        Assert.Contains(
            evaluation.Errors,
            error => error.Contains("windows-x64 is missing passing settings profiles: sound-device-absent", StringComparison.Ordinal));
        Assert.Contains(evaluation.Errors, error => error == "manual matrix contains failed or blocked required flows");
    }

    [Fact]
    public void Retained_sessions_require_and_match_an_exact_candidate()
    {
        var root = ResolveRepositoryRoot();
        using var directory = new TemporaryDirectory();
        var sessions = Path.Combine(directory.Path, "sessions");
        var sessionPath = Path.Combine(sessions, "session.json");
        WriteSession(sessionPath, Session("windows-x64", 0, 0, "keyboard", RequiredFlows()));

        var missingCandidate = ManualProductMatrixCheck.Evaluate(root, sessions, null);
        using var missingEvidence = JsonDocument.Parse(missingCandidate.Json);
        Assert.Contains(missingCandidate.Errors, error => error == "retained manual sessions require an exact candidate record");
        Assert.False(missingEvidence.RootElement.GetProperty("releaseAcceptance").GetBoolean());

        var candidate = WriteCandidate(Path.Combine(directory.Path, "candidate.json"));
        var mismatched = JsonNode.Parse(File.ReadAllText(sessionPath))!.AsObject();
        mismatched["artifactSha256"] = new string('9', 64);
        WriteJson(sessionPath, mismatched);
        var mismatchedEvaluation = ManualProductMatrixCheck.Evaluate(root, sessions, candidate);
        using var mismatchedEvidence = JsonDocument.Parse(mismatchedEvaluation.Json);
        Assert.Contains(
            mismatchedEvaluation.Errors,
            error => error.Contains("artifact SHA-256 does not match the exact candidate", StringComparison.Ordinal));
        Assert.False(mismatchedEvidence.RootElement.GetProperty("releaseAcceptance").GetBoolean());
    }

    [Fact]
    public void Candidate_rejects_duplicate_fields_and_split_universal_identity()
    {
        var root = ResolveRepositoryRoot();
        var duplicate = WriteTemp("duplicate.json", null);
        File.WriteAllText(duplicate, "{\"schemaVersion\": 1, \"schemaVersion\": 1}\n");
        try
        {
            var duplicateEvaluation = ManualProductMatrixCheck.Evaluate(root, null, duplicate);
            using var duplicateEvidence = JsonDocument.Parse(duplicateEvaluation.Json);
            Assert.Contains(
                duplicateEvaluation.Errors,
                error => error.Contains("duplicate JSON field: schemaVersion", StringComparison.Ordinal));
            Assert.False(duplicateEvidence.RootElement.GetProperty("candidateQualified").GetBoolean());
        }
        finally
        {
            DeleteIfExists(duplicate);
        }

        using var directory = new TemporaryDirectory();
        var candidate = WriteCandidate(Path.Combine(directory.Path, "candidate.json"));
        var document = JsonNode.Parse(File.ReadAllText(candidate))!.AsObject();
        document["artifactRows"]!.AsArray()[2]!["sha256"] = new string('9', 64);
        WriteJson(candidate, document);
        var evaluation = ManualProductMatrixCheck.Evaluate(root, null, candidate);
        using var evidence = JsonDocument.Parse(evaluation.Json);
        Assert.Contains(
            evaluation.Errors,
            error => error == "candidate macOS architecture rows must identify one identical Universal artifact");
        Assert.False(evidence.RootElement.GetProperty("candidateQualified").GetBoolean());
    }

    [Fact]
    public void Malformed_result_dimensions_and_invalid_calendar_time_fail_closed()
    {
        var root = ResolveRepositoryRoot();
        using var directory = new TemporaryDirectory();
        var sessions = Path.Combine(directory.Path, "sessions");
        var candidate = WriteCandidate(Path.Combine(directory.Path, "candidate.json"));
        var session = Session("windows-x64", 0, 0, "keyboard", RequiredFlows());
        session["executedUtc"] = "2026-99-99T99:99:99Z";
        var results = session["results"]!.AsArray();
        results[0]!["inputDeviceId"] = StringArray("unknown-controller");
        results[1]!["inputCapabilityIds"] = StringArray("back");
        var nestedProfile = new JsonArray();
        nestedProfile.Add("sound-muted");
        var nestedProfiles = new JsonArray();
        nestedProfiles.Add(nestedProfile);
        results[2]!["settingsProfileIds"] = nestedProfiles;
        WriteSession(Path.Combine(sessions, "session.json"), session);

        var evaluation = ManualProductMatrixCheck.Evaluate(root, sessions, candidate);
        using var evidence = JsonDocument.Parse(evaluation.Json);

        Assert.Contains(evaluation.Errors, error => error.Contains("inputDeviceId is unsupported", StringComparison.Ordinal));
        Assert.Contains(evaluation.Errors, error => error.Contains("inputCapabilityIds must be empty for keyboard", StringComparison.Ordinal));
        Assert.Contains(evaluation.Errors, error => error.Contains("settingsProfileIds must be unique supported profiles", StringComparison.Ordinal));
        Assert.Contains(evaluation.Errors, error => error.Contains("executedUtc must use", StringComparison.Ordinal));
        Assert.False(evidence.RootElement.GetProperty("releaseAcceptance").GetBoolean());
    }

    [Fact]
    public void Commands_qualify_foundation_and_reject_alias_oversize_and_bad_usage()
    {
        var root = ResolveRepositoryRoot();
        using var directory = new TemporaryDirectory();
        var output = new StringWriter();
        var error = new StringWriter();
        var code = RepositoryCheckCommand.Run(["manual-matrix", root], output, error);

        Assert.Equal(0, code);
        Assert.Contains("retained physical execution remains pending", output.ToString(), StringComparison.Ordinal);
        Assert.Equal(string.Empty, error.ToString());

        var handoff = Path.Combine(directory.Path, "handoff.json");
        output = new StringWriter();
        error = new StringWriter();
        code = RepositoryCheckCommand.Run(["manual-matrix-write", handoff, root], output, error);
        Assert.Equal(0, code);
        Assert.True(File.Exists(handoff));
        Assert.Equal(string.Empty, error.ToString());

        var contract = Path.Combine(root, "config", "qa_manual_product_matrix_v2.json");
        var before = File.ReadAllBytes(contract);
        var alias = ManualProductMatrixCheck.Evaluate(root, null, null, outputPath: contract);
        Assert.Contains(alias.Errors, item => item.Contains("cannot alias", StringComparison.Ordinal));
        Assert.Equal(before, File.ReadAllBytes(contract));

        var oversize = Path.Combine(directory.Path, "oversize.json");
        File.WriteAllBytes(oversize, new byte[(4 * 1024 * 1024) + 1]);
        var oversizeEvaluation = ManualProductMatrixCheck.Evaluate(root, null, null, oversize);
        Assert.Contains(oversizeEvaluation.Errors, item => item.Contains("exceeds the", StringComparison.Ordinal));

        output = new StringWriter();
        error = new StringWriter();
        code = RepositoryCheckCommand.Run(
            ["manual-matrix-record", Path.Combine(directory.Path, "missing-sessions"), Path.Combine(directory.Path, "missing.json"), Path.Combine(directory.Path, "decision.json"), root],
            output,
            error);
        Assert.Equal(1, code);
        Assert.StartsWith("Manual product matrix qualification failed:" + Environment.NewLine, error.ToString(), StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(directory.Path, "decision.json")));
    }

    [Fact]
    public void Contract_and_candidate_reject_closed_field_drift()
    {
        var root = ResolveRepositoryRoot();
        using var directory = new TemporaryDirectory();
        var contractPath = Path.Combine(directory.Path, "contract.json");
        var sourceContract = File.ReadAllText(Path.Combine(root, "config", "qa_manual_product_matrix_v2.json"));

        void RejectContract(Action<JsonObject> mutate, string fragment)
        {
            var document = JsonNode.Parse(sourceContract)!.AsObject();
            mutate(document);
            WriteJson(contractPath, document);
            var evaluation = ManualProductMatrixCheck.Evaluate(root, null, null, contractPath);
            Assert.Contains(evaluation.Errors, error => error.Contains(fragment, StringComparison.Ordinal));
        }

        RejectContract(document => document["schemaVersion"] = "2", "contract.schemaVersion");
        RejectContract(document => document["kind"] = JsonNode.Parse("null"), "contract.kind");
        RejectContract(document => document["kind"] = true, "contract.kind");
        RejectContract(document => document["kind"] = false, "contract.kind");
        RejectContract(document => document["status"] = 2, "contract.status");
        RejectContract(document => document["status"] = "bad'quote\\path", "contract.status");
        RejectContract(document => document["requiredFlowDefectSeverity"] = "P0", "contract severity");
        RejectContract(document => document["platformRows"] = new JsonArray(), "contract.platformRows");
        RejectContract(document => document["platformRows"] = new JsonObject(), "contract.platformRows");
        RejectContract(document => document["platformRows"]!.AsArray()[0] = "nope", "contract.platformRows");
        RejectContract(document => document["platformRows"]![0]!["id"] = "nope", "contract.platformRows");
        RejectContract(document => document["platformRows"]![0]!.AsObject()["extra"] = true, "contract.platformRows");
        RejectContract(
            document =>
            {
                var flows = new JsonArray();
                flows.Add(true);
                document["requiredFlows"] = flows;
            },
            "contract.requiredFlows");
        RejectContract(document => document["requiredFlows"] = new JsonArray("nope"), "contract.requiredFlows");
        RejectContract(document => document["inputDevices"] = new JsonArray(), "contract.inputDevices");
        RejectContract(document => document["inputDevices"]![0]!["requiredCoverage"] = "optional", "contract.inputDevices");
        RejectContract(document => document["inputDevices"]![0] = new JsonArray(), "contract.inputDevices");
        RejectContract(document => document["mouseInputCapabilities"] = new JsonObject(), "contract.mouseInputCapabilities");
        RejectContract(document => document.Remove("releaseRules"), "contract fields");
        RejectContract(document => document["extra"] = 1, "contract fields");

        File.WriteAllText(contractPath, "[1]\n");
        Assert.Contains(
            ManualProductMatrixCheck.Evaluate(root, null, null, contractPath).Errors,
            error => error.Contains("contract must be an object", StringComparison.Ordinal));
        File.WriteAllText(contractPath, "{\"schemaVersion\": 2,}\n");
        Assert.Contains(
            ManualProductMatrixCheck.Evaluate(root, null, null, contractPath).Errors,
            error => error.Contains("unreadable manual product matrix contract", StringComparison.Ordinal));
        File.WriteAllText(contractPath, "{\"schemaVersion\": 2 /*no*/}\n");
        Assert.Contains(
            ManualProductMatrixCheck.Evaluate(root, null, null, contractPath).Errors,
            error => error.Contains("unreadable manual product matrix contract", StringComparison.Ordinal));
        File.WriteAllBytes(contractPath, [0xFF, 0x00]);
        Assert.Contains(
            ManualProductMatrixCheck.Evaluate(root, null, null, contractPath).Errors,
            error => error.Contains("unreadable manual product matrix contract", StringComparison.Ordinal));
        File.WriteAllText(contractPath, new string('[', 70) + new string(']', 70));
        Assert.Contains(
            ManualProductMatrixCheck.Evaluate(root, null, null, contractPath).Errors,
            error => error.Contains("unreadable manual product matrix contract", StringComparison.Ordinal));
        Assert.Contains(
            ManualProductMatrixCheck.Evaluate(root, null, null, Path.Combine(directory.Path, "missing-contract.json")).Errors,
            error => error.Contains("missing manual product matrix contract", StringComparison.Ordinal));

        var candidatePath = WriteCandidate(Path.Combine(directory.Path, "candidate.json"));

        void RejectCandidate(Action<JsonObject> mutate, string fragment)
        {
            var document = JsonNode.Parse(File.ReadAllText(candidatePath))!.AsObject();
            mutate(document);
            var path = Path.Combine(directory.Path, "mutated-candidate.json");
            WriteJson(path, document);
            var evaluation = ManualProductMatrixCheck.Evaluate(root, null, path);
            Assert.Contains(evaluation.Errors, error => error.Contains(fragment, StringComparison.Ordinal));
            Assert.False(JsonDocument.Parse(evaluation.Json).RootElement.GetProperty("candidateQualified").GetBoolean());
        }

        RejectCandidate(document => document["schemaVersion"] = "1", "candidate.schemaVersion");
        RejectCandidate(document => document["kind"] = 1, "candidate.kind");
        RejectCandidate(document => document["releaseRunId"] = 0, "candidate.releaseRunId");
        RejectCandidate(document => document["releaseRunId"] = "123456", "candidate.releaseRunId");
        RejectCandidate(document => document["releaseRunId"] = 1.5, "candidate.releaseRunId");
        RejectCandidate(document => document["releaseRunUrl"] = 1, "candidate.releaseRunUrl");
        RejectCandidate(document => document["releaseRunUrl"] = "https://example.invalid/runs/123456", "candidate.releaseRunUrl");
        RejectCandidate(
            document => document["releaseRunUrl"] = "https://github.com/blisspixel/VibeSnake/actions/runs/1",
            "candidate.releaseRunUrl");
        RejectCandidate(document => document["releaseMatrixSha256"] = "abc", "candidate.releaseMatrixSha256");
        RejectCandidate(document => document["candidateRevision"] = "GG", "candidate.candidateRevision");
        RejectCandidate(document => document["appVersion"] = " ", "candidate.appVersion");
        RejectCandidate(document => document["appVersion"] = 1, "candidate.appVersion");
        RejectCandidate(document => document["buildMode"] = "Debug", "candidate.buildMode");
        RejectCandidate(document => document["humanReviewStatus"] = "accepted", "candidate.humanReviewStatus");
        RejectCandidate(document => document["releaseAcceptance"] = true, "candidate.releaseAcceptance");
        RejectCandidate(document => document["publicationEligible"] = true, "candidate.publicationEligible");
        RejectCandidate(document => document["artifactRows"] = new JsonArray(), "candidate.artifactRows");
        RejectCandidate(document => document["artifactRows"] = new JsonObject(), "candidate.artifactRows");
        RejectCandidate(document => document["artifactRows"]!.AsArray()[0] = "nope", "candidate.artifactRows[0]");
        RejectCandidate(document => document["artifactRows"]![0]!["fileName"] = "", "fileName must be a safe file name");
        RejectCandidate(document => document["artifactRows"]![0]!["fileName"] = ".", "fileName must be a safe file name");
        RejectCandidate(document => document["artifactRows"]![0]!["fileName"] = "..", "fileName must be a safe file name");
        RejectCandidate(document => document["artifactRows"]![0]!["fileName"] = "a/b", "fileName must be a safe file name");
        RejectCandidate(document => document["artifactRows"]![0]!["fileName"] = "a\\b", "fileName must be a safe file name");
        RejectCandidate(document => document["artifactRows"]![0]!["fileName"] = 1, "fileName must be a safe file name");
        RejectCandidate(document => document["artifactRows"]![0]!["sha256"] = "zz", "sha256 must be a SHA-256 digest");
        RejectCandidate(document => document["artifactRows"]![0]!["bytes"] = 0, "bytes must be a positive integer");
        RejectCandidate(document => document["artifactRows"]![0]!["bytes"] = -2, "bytes must be a positive integer");
        RejectCandidate(document => document["artifactRows"]![0]!["bytes"] = 1.5, "bytes must be a positive integer");
        RejectCandidate(document => document["artifactRows"]![1]!["bytes"] = 1002, "one identical Universal artifact");
        RejectCandidate(
            document =>
            {
                document["artifactRows"]![1]!["fileName"] = true;
                document["artifactRows"]![2]!["fileName"] = true;
            },
            "fileName must be a safe file name");
        RejectCandidate(
            document =>
            {
                document["artifactRows"]![1]!["fileName"] = JsonNode.Parse("null");
                document["artifactRows"]![2]!["fileName"] = JsonNode.Parse("null");
            },
            "fileName must be a safe file name");
        RejectCandidate(
            document =>
            {
                document["artifactRows"]![1]!["fileName"] = new JsonArray();
                document["artifactRows"]![2]!["fileName"] = new JsonArray();
            },
            "fileName must be a safe file name");
        RejectCandidate(document => document["artifactRows"]![1]!.AsObject().Remove("sha256"), "one identical Universal artifact");
        RejectCandidate(document => document.Remove("releaseRunId"), "candidate fields");
        RejectCandidate(document => document["extra"] = true, "candidate fields");

        File.WriteAllText(Path.Combine(directory.Path, "array-candidate.json"), "[1]\n");
        Assert.Contains(
            ManualProductMatrixCheck.Evaluate(root, null, Path.Combine(directory.Path, "array-candidate.json")).Errors,
            error => error.Contains("candidate must be an object", StringComparison.Ordinal));
        File.WriteAllText(Path.Combine(directory.Path, "empty-candidate.json"), string.Empty);
        Assert.Contains(
            ManualProductMatrixCheck.Evaluate(root, null, Path.Combine(directory.Path, "empty-candidate.json")).Errors,
            error => error.Contains("unreadable manual product matrix candidate", StringComparison.Ordinal));
    }

    [Fact]
    public void Sessions_reject_unsafe_identity_evidence_and_output()
    {
        var root = ResolveRepositoryRoot();
        using var directory = new TemporaryDirectory();
        var sessions = Path.Combine(directory.Path, "sessions");
        var candidate = WriteCandidate(Path.Combine(directory.Path, "candidate.json"));
        var flows = RequiredFlows();

        void RejectSession(Action<JsonObject> mutate, string fragment)
        {
            var session = Session("windows-x64", 0, 0, "keyboard", flows.Take(1).ToArray());
            mutate(session);
            var path = Path.Combine(sessions, "mutated.json");
            if (fragment.Contains("duplicate flow", StringComparison.Ordinal))
            {
                WriteSession(path, session);
            }
            else
            {
                WriteJson(path, session);
            }

            var evaluation = ManualProductMatrixCheck.Evaluate(root, sessions, candidate);
            Assert.Contains(evaluation.Errors, error => error.Contains(fragment, StringComparison.Ordinal));
            Assert.False(JsonDocument.Parse(evaluation.Json).RootElement.GetProperty("releaseAcceptance").GetBoolean());
            File.Delete(path);
        }

        RejectSession(session => session["sessionId"] = "manual-1", "sessionId must match");
        RejectSession(session => session["sessionId"] = 1, "sessionId must match");
        RejectSession(session => session["candidateRevision"] = "nope", "candidateRevision must be");
        RejectSession(session => session["candidateRevision"] = 1, "candidateRevision must be");
        RejectSession(session => session["artifactSha256"] = "nope", "artifactSha256 must be");
        RejectSession(session => session["artifactSha256"] = 1, "artifactSha256 must be");
        RejectSession(session => session["platformRowId"] = "nope", "platformRowId is unsupported");
        RejectSession(session => session["platformRowId"] = 1, "platformRowId is unsupported");
        RejectSession(session => session["appVersion"] = " ", "appVersion must be a nonempty string");
        RejectSession(session => session["operatingSystemVersion"] = 1, "operatingSystemVersion must be a nonempty string");
        RejectSession(session => session["hardwareClass"] = "", "hardwareClass must be a nonempty string");
        RejectSession(session => session["renderer"] = JsonNode.Parse("null"), "renderer must be a nonempty string");
        RejectSession(session => session["executedUtc"] = "not-a-date", "executedUtc must use");
        RejectSession(session => session["executedUtc"] = 1, "executedUtc must use");
        RejectSession(session => session["results"] = new JsonArray(), "results must be a nonempty array");
        RejectSession(session => session["results"] = new JsonObject(), "results must be a nonempty array");
        RejectSession(session => session["results"]![0]!["flowId"] = "not-a-flow", "flowId is unsupported");
        RejectSession(session => session["results"]![0]!["flowId"] = 1, "flowId is unsupported");
        RejectSession(
            session => session["results"]!.AsArray().Add(session["results"]![0]!.DeepClone()),
            "duplicate flow result");
        RejectSession(session => session["results"]![0]!["result"] = "maybe", "result is unsupported");
        RejectSession(session => session["results"]![0]!["result"] = 1, "result is unsupported");
        RejectSession(session => session["results"]![0]!["inputDeviceId"] = "dance-pad", "inputDeviceId is unsupported");
        RejectSession(session => session["results"]![0]!["inputCapabilityIds"] = "back", "inputCapabilityIds must be unique");
        RejectSession(session => session["results"]![0]!["inputCapabilityIds"] = StringArray("nope"), "inputCapabilityIds must be unique");
        RejectSession(session => session["results"]![0]!["inputCapabilityIds"] = StringArray("back", "back"), "inputCapabilityIds must be unique");
        RejectSession(session => session["results"]![0]!["settingsProfileIds"] = StringArray("nope"), "settingsProfileIds must be unique");
        RejectSession(session => session["results"]![0]!["settingsProfileIds"] = StringArray("sound-muted", "sound-muted"), "settingsProfileIds must be unique");
        RejectSession(session => session["results"]![0]!["evidencePaths"] = new JsonArray(), "evidencePaths must contain safe relative paths");
        RejectSession(session => session["results"]![0]!["evidencePaths"] = StringArray(""), "evidencePaths must contain safe relative paths");
        RejectSession(session => session["results"]![0]!["evidencePaths"] = StringArray("a\\b.png"), "evidencePaths must contain safe relative paths");
        RejectSession(session => session["results"]![0]!["evidencePaths"] = StringArray("/tmp/a.png"), "evidencePaths must contain safe relative paths");
        RejectSession(session => session["results"]![0]!["evidencePaths"] = StringArray("C:/a.png"), "evidencePaths must contain safe relative paths");
        RejectSession(session => session["results"]![0]!["evidencePaths"] = StringArray("a/../b.png"), "evidencePaths must contain safe relative paths");
        RejectSession(session => session["results"]![0]!["evidencePaths"] = StringArray("missing.png"), "missing retained files");
        RejectSession(session => session.Remove("sessionId"), "fields must be");
        RejectSession(session => session["results"]![0] = "nope", "must be an object");

        File.WriteAllText(Path.Combine(sessions, "array.json"), "[1]\n");
        Assert.Contains(
            ManualProductMatrixCheck.Evaluate(root, sessions, candidate).Errors,
            error => error.Contains("must be an object", StringComparison.Ordinal));
        File.Delete(Path.Combine(sessions, "array.json"));
        File.WriteAllText(Path.Combine(sessions, "empty.json"), string.Empty);
        Assert.Contains(
            ManualProductMatrixCheck.Evaluate(root, sessions, candidate).Errors,
            error => error.Contains("must be an object", StringComparison.Ordinal));
        File.Delete(Path.Combine(sessions, "empty.json"));

        var first = Session("windows-x64", 0, 0, "keyboard", flows.Take(1).ToArray());
        var second = Session("windows-x64", 0, 1, "keyboard", flows.Take(1).ToArray());
        second["sessionId"] = first["sessionId"]!.DeepClone();
        WriteSession(Path.Combine(sessions, "duplicate-a.json"), first);
        WriteSession(Path.Combine(sessions, "duplicate-b.json"), second);
        Assert.Contains(
            ManualProductMatrixCheck.Evaluate(root, sessions, candidate).Errors,
            error => error.Contains("duplicate manual product matrix sessionId", StringComparison.Ordinal));
        File.Delete(Path.Combine(sessions, "duplicate-a.json"));
        File.Delete(Path.Combine(sessions, "duplicate-b.json"));

        var revised = Session("windows-x64", 0, 2, "keyboard", flows.Take(1).ToArray());
        revised["candidateRevision"] = new string('b', 40);
        WriteSession(Path.Combine(sessions, "revised.json"), revised);
        Assert.Contains(
            ManualProductMatrixCheck.Evaluate(root, sessions, candidate).Errors,
            error => error.Contains("revision does not match the exact candidate", StringComparison.Ordinal));
        File.Delete(Path.Combine(sessions, "revised.json"));

        var version = Session("windows-x64", 0, 3, "keyboard", flows.Take(1).ToArray());
        version["appVersion"] = "0.8.0";
        WriteSession(Path.Combine(sessions, "version.json"), version);
        Assert.Contains(
            ManualProductMatrixCheck.Evaluate(root, sessions, candidate).Errors,
            error => error.Contains("application version does not match the exact candidate", StringComparison.Ordinal));
        File.Delete(Path.Combine(sessions, "version.json"));

        var windowsA = Session("windows-x64", 0, 4, "keyboard", flows.Take(1).ToArray());
        var windowsB = Session("windows-x64", 0, 5, "keyboard", flows.Take(1).ToArray());
        windowsB["artifactSha256"] = new string('9', 64);
        var driftedCandidate = JsonNode.Parse(File.ReadAllText(candidate))!.AsObject();
        driftedCandidate["artifactRows"]![0]!["sha256"] = new string('9', 64);
        var driftedPath = Path.Combine(directory.Path, "drifted-candidate.json");
        WriteJson(driftedPath, driftedCandidate);
        WriteSession(Path.Combine(sessions, "windows-a.json"), windowsA);
        WriteSession(Path.Combine(sessions, "windows-b.json"), windowsB);
        Assert.Contains(
            ManualProductMatrixCheck.Evaluate(root, sessions, driftedPath).Errors,
            error => error.Contains("must use exactly one candidate artifact SHA-256", StringComparison.Ordinal));
        File.Delete(Path.Combine(sessions, "windows-a.json"));
        File.Delete(Path.Combine(sessions, "windows-b.json"));

        var apple = Session("macos-universal-apple-silicon", 1, 6, "keyboard", flows.Take(1).ToArray());
        var intel = Session("macos-universal-intel", 2, 7, "keyboard", flows.Take(1).ToArray());
        intel["artifactSha256"] = new string('8', 64);
        WriteSession(Path.Combine(sessions, "apple.json"), apple);
        WriteSession(Path.Combine(sessions, "intel.json"), intel);
        Assert.Contains(
            ManualProductMatrixCheck.Evaluate(root, sessions, candidate).Errors,
            error => error.Contains("must use the same Universal artifact SHA-256", StringComparison.Ordinal));
        File.Delete(Path.Combine(sessions, "apple.json"));
        File.Delete(Path.Combine(sessions, "intel.json"));

        var evidenceDirectory = Session("windows-x64", 0, 8, "keyboard", flows.Take(1).ToArray());
        evidenceDirectory["results"]![0]!["evidencePaths"] = StringArray("evidence-directory");
        WriteJson(Path.Combine(sessions, "directory-evidence.json"), evidenceDirectory);
        Directory.CreateDirectory(Path.Combine(sessions, "evidence-directory"));
        Assert.Contains(
            ManualProductMatrixCheck.Evaluate(root, sessions, candidate).Errors,
            error => error.Contains("missing retained files", StringComparison.Ordinal));
        File.Delete(Path.Combine(sessions, "directory-evidence.json"));

        Assert.Contains(
            ManualProductMatrixCheck.Evaluate(root, Path.Combine(directory.Path, "missing-sessions"), candidate).Errors,
            error => error.Contains("sessions directory does not exist", StringComparison.Ordinal));

        var outputDirectory = Path.Combine(directory.Path, "output-directory");
        Directory.CreateDirectory(outputDirectory);
        Assert.Contains(
            ManualProductMatrixCheck.Evaluate(root, null, null, outputPath: outputDirectory).Errors,
            error => error.Contains("must be a regular file", StringComparison.Ordinal));
        var parentFile = Path.Combine(directory.Path, "parent-file");
        File.WriteAllText(parentFile, "x");
        var parentEvaluation = ManualProductMatrixCheck.Evaluate(root, null, null, outputPath: Path.Combine(parentFile, "handoff.json"));
        Assert.Contains(parentEvaluation.Errors, error => error.Contains("Cannot create", StringComparison.Ordinal));
        Assert.Contains(
            ManualProductMatrixCheck.Evaluate(root, null, null, outputPath: Path.GetPathRoot(directory.Path)!).Errors,
            error => error.Contains("no parent directory", StringComparison.Ordinal));
    }

    private static string[] RequiredFlows()
    {
        var root = ResolveRepositoryRoot();
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "config", "qa_manual_product_matrix_v2.json")));
        return document.RootElement.GetProperty("requiredFlows").EnumerateArray().Select(item => item.GetString()!).ToArray();
    }

    private static List<string> WriteCompleteMatrix(string sessions)
    {
        var paths = new List<string>();
        var sessionIndex = 0;
        var flows = RequiredFlows();
        foreach (var (platform, platformIndex) in Platforms.Select((platform, index) => (platform, index)))
        {
            foreach (var device in CompleteFlowDevices)
            {
                var path = Path.Combine(sessions, "session-" + sessionIndex.ToString("000", CultureInfo.InvariantCulture) + ".json");
                WriteSession(path, Session(platform.Id, platformIndex, sessionIndex, device, flows, device == "keyboard"));
                paths.Add(path);
                sessionIndex++;
            }

            var mousePath = Path.Combine(sessions, "session-" + sessionIndex.ToString("000", CultureInfo.InvariantCulture) + ".json");
            WriteSession(mousePath, Session(platform.Id, platformIndex, sessionIndex, "mouse", flows.Take(MouseCapabilities.Length).ToArray()));
            paths.Add(mousePath);
            sessionIndex++;
        }

        return paths;
    }

    private static JsonObject Session(
        string platform,
        int platformIndex,
        int sessionIndex,
        string inputDevice,
        string[] flows,
        bool includeProfiles = false)
    {
        var artifactDigit = platform.StartsWith("macos-universal", StringComparison.Ordinal) ? 2 : platformIndex + 1;
        var capabilityByFlow = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 0; index < Math.Min(flows.Length, MouseCapabilities.Length); index++)
        {
            capabilityByFlow[RequiredFlows()[index]] = MouseCapabilities[index];
        }

        var results = new JsonArray();
        for (var index = 0; index < flows.Length; index++)
        {
            var flowId = flows[index];
            var capabilities = new JsonArray();
            if (inputDevice == "mouse" && capabilityByFlow.TryGetValue(flowId, out var capability))
            {
                capabilities.Add(capability);
            }

            var profiles = new JsonArray();
            if (includeProfiles && index < SettingsProfiles.Length)
            {
                profiles.Add(SettingsProfiles[index]);
            }

            results.Add(new JsonObject
            {
                ["flowId"] = flowId,
                ["inputDeviceId"] = inputDevice,
                ["inputCapabilityIds"] = capabilities,
                ["settingsProfileIds"] = profiles,
                ["result"] = "pass",
                ["evidencePaths"] = new JsonArray("evidence/" + platform + "/" + sessionIndex.ToString("000", CultureInfo.InvariantCulture) + "-" + flowId + ".png"),
            });
        }

        return new JsonObject
        {
            ["schemaVersion"] = 2,
            ["kind"] = "vibesnake-manual-product-matrix-session-v2",
            ["sessionId"] = "product-matrix-" + sessionIndex.ToString("000", CultureInfo.InvariantCulture),
            ["candidateRevision"] = new string('a', 40),
            ["artifactSha256"] = new string((char)('0' + artifactDigit), 64),
            ["appVersion"] = "0.9.0",
            ["platformRowId"] = platform,
            ["operatingSystemVersion"] = "qualified-os-version",
            ["hardwareClass"] = "declared-hardware-class",
            ["renderer"] = "gl-compatibility",
            ["executedUtc"] = "2026-08-" + (platformIndex + 1).ToString("00", CultureInfo.InvariantCulture) + "T12:00:00Z",
            ["results"] = results,
        };
    }

    private static string WriteCandidate(string path)
    {
        var rows = new JsonArray();
        for (var index = 0; index < Platforms.Length; index++)
        {
            var platform = Platforms[index];
            var macos = platform.Id.StartsWith("macos-universal", StringComparison.Ordinal);
            var digit = macos ? 2 : index + 1;
            rows.Add(new JsonObject
            {
                ["platformRowId"] = platform.Id,
                ["artifactPlatform"] = platform.Artifact,
                ["architecture"] = platform.Architecture,
                ["fileName"] = "VibeSnake-" + platform.Artifact + ".package",
                ["sha256"] = new string((char)('0' + digit), 64),
                ["bytes"] = macos ? 1001 : 1000 + index,
                ["artifactManifestSha256"] = new string((char)('0' + (macos ? 6 : index + 5)), 64),
            });
        }

        WriteJson(path, new JsonObject
        {
            ["schemaVersion"] = 1,
            ["kind"] = "vibesnake-manual-product-matrix-candidate-v1",
            ["releaseRunId"] = 123456,
            ["releaseRunUrl"] = "https://github.com/blisspixel/VibeSnake/actions/runs/123456",
            ["releaseMatrixSha256"] = new string('f', 64),
            ["candidateRevision"] = new string('a', 40),
            ["appVersion"] = "0.9.0",
            ["buildMode"] = "Release",
            ["artifactRows"] = rows,
            ["humanReviewStatus"] = "pending",
            ["releaseAcceptance"] = false,
            ["publicationEligible"] = false,
        });
        return path;
    }

    private static void WriteSession(string path, JsonObject session)
    {
        WriteJson(path, session);
        foreach (var result in session["results"]!.AsArray())
        {
            foreach (var relative in result!["evidencePaths"]!.AsArray())
            {
                var evidencePath = Path.Combine(
                    Path.GetDirectoryName(path)!,
                    relative!.GetValue<string>().Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(evidencePath)!);
                File.WriteAllBytes(evidencePath, "retained manual evidence"u8.ToArray());
            }
        }
    }

    private static void WriteJson(string path, JsonNode node)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, node.ToJsonString(JsonOptions).Replace("\r\n", "\n", StringComparison.Ordinal) + "\n");
    }

    private static string WriteTemp(string name, JsonNode? node)
    {
        var path = Path.Combine(Path.GetTempPath(), "vibesnake-manual-" + Guid.NewGuid().ToString("N"), name);
        if (node is not null)
        {
            WriteJson(path, node);
        }
        else
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        }

        return path;
    }

    private static string[] Strings(JsonElement element) =>
        element.EnumerateArray().Select(item => item.GetString()!).ToArray();

    private static JsonArray StringArray(params string[] values)
    {
        var array = new JsonArray();
        foreach (var value in values)
        {
            array.Add(value);
        }

        return array;
    }

    private static string Hash(string path) =>
        Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static string ResolveRepositoryRoot() => AgentKnowledgeTestRepository.ResolveRepositoryRoot();

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "vibesnake-manual-matrix-" + Guid.NewGuid().ToString("N"));
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
