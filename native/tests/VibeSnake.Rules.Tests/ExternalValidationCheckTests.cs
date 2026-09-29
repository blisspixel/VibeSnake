using System.Globalization;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using RepositoryChecks;

namespace VibeSnake.Rules.Tests;

public sealed class ExternalValidationCheckTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true,
    };

    private static readonly string[] Cohorts =
    [
        "clean-install-fresh-keyboard",
        "clean-install-fresh-controller",
        "accessibility-focused-fresh",
        "returning-regression",
    ];

    private static readonly string[] Platforms = ["windows-x64", "macos-universal", "linux-x64"];

    private static readonly string[] Checks =
    [
        "death-explanation",
        "recovery-identification",
        "power-route-decision-explanation",
        "escalation-recognition",
        "another-run-intent",
        "another-run-reason",
    ];

    private static readonly string[] Families = ["defect", "comprehension", "accessibility", "crash"];

    private static readonly Dictionary<string, string> ArtifactHashes = new(StringComparer.Ordinal)
    {
        ["windows-x64"] = new string('1', 64),
        ["macos-universal"] = new string('2', 64),
        ["linux-x64"] = new string('3', 64),
    };

    [Fact]
    public void Repository_handoff_is_pending_and_repeatable()
    {
        var root = ResolveRepositoryRoot();
        var evaluation = ExternalValidationCheck.Evaluate(root, null, null, null);
        using var evidence = JsonDocument.Parse(evaluation.Json);

        Assert.Empty(evaluation.Errors);
        Assert.True(evaluation.Passed);
        Assert.True(evidence.RootElement.GetProperty("passed").GetBoolean());
        Assert.True(evidence.RootElement.GetProperty("protocolQualified").GetBoolean());
        Assert.Equal(3, evidence.RootElement.GetProperty("artifactPlatformCount").GetInt32());
        Assert.Equal(4, evidence.RootElement.GetProperty("cohortCount").GetInt32());
        Assert.Equal(6, evidence.RootElement.GetProperty("comprehensionCheckCount").GetInt32());
        Assert.Equal(4, evidence.RootElement.GetProperty("reportFamilyCount").GetInt32());
        Assert.Equal(0, evidence.RootElement.GetProperty("candidateCount").GetInt32());
        Assert.Equal(0, evidence.RootElement.GetProperty("sessionCount").GetInt32());
        Assert.False(evidence.RootElement.GetProperty("externalValidationComplete").GetBoolean());
        Assert.False(evidence.RootElement.GetProperty("releaseAcceptance").GetBoolean());
        Assert.Equal(5, evidence.RootElement.GetProperty("pendingGates").GetArrayLength());
        Assert.Equal(
            Hash(Path.Combine(root, "config", "qa_external_validation_v1.json")),
            evidence.RootElement.GetProperty("contractSha256").GetString());

        var first = Path.Combine(Path.GetTempPath(), "vibesnake-external-handoff-" + Guid.NewGuid().ToString("N") + ".json");
        var second = Path.Combine(Path.GetTempPath(), "vibesnake-external-handoff-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            Assert.True(ExternalValidationCheck.WriteFoundationHandoff(root, first).Passed);
            Assert.True(ExternalValidationCheck.WriteFoundationHandoff(root, second).Passed);
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
    public void Contract_rejects_a_missing_comprehension_check()
    {
        var root = ResolveRepositoryRoot();
        var contract = JsonNode.Parse(File.ReadAllText(Path.Combine(root, "config", "qa_external_validation_v1.json")))!.AsObject();
        var checks = contract["comprehensionChecks"]!.AsArray();
        checks.Remove(checks.First(item => item!.GetValue<string>() == "another-run-reason")!);
        var path = Path.Combine(Path.GetTempPath(), "vibesnake-external-" + Guid.NewGuid().ToString("N") + ".json");
        WriteJson(path, contract);

        try
        {
            var evaluation = ExternalValidationCheck.Evaluate(root, null, null, null, path);
            Assert.Contains(
                evaluation.Errors,
                error => error.Contains("contract.comprehensionChecks must be", StringComparison.Ordinal));
            Assert.False(evaluation.Passed);
        }
        finally
        {
            DeleteIfExists(path);
        }
    }

    [Fact]
    public void Complete_external_sessions_close_the_gate()
    {
        var root = ResolveRepositoryRoot();
        using var directory = new TemporaryDirectory();
        var sessions = Path.Combine(directory.Path, "sessions");
        var ledger = Path.Combine(directory.Path, "candidate-ledger.json");
        var findings = Path.Combine(directory.Path, "findings.json");
        var revision = new string('a', 40);
        WriteCompleteFinalSessions(sessions, revision);
        WriteJson(ledger, Ledger(Candidate(revision)));
        WriteEmptyFindings(findings);

        var evaluation = ExternalValidationCheck.Evaluate(root, sessions, ledger, findings);
        using var evidence = JsonDocument.Parse(evaluation.Json);

        Assert.True(evaluation.Passed, string.Join(Environment.NewLine, evaluation.Errors));
        Assert.Equal(1, evidence.RootElement.GetProperty("candidateCount").GetInt32());
        Assert.Equal(4, evidence.RootElement.GetProperty("sessionCount").GetInt32());
        Assert.Equal(4, evidence.RootElement.GetProperty("finalCandidateSessionCount").GetInt32());
        Assert.Equal(Platforms.Order(StringComparer.Ordinal).ToArray(), Strings(evidence.RootElement.GetProperty("observedFinalCandidatePlatforms")));
        Assert.True(evidence.RootElement.GetProperty("externalValidationComplete").GetBoolean());
        Assert.True(evidence.RootElement.GetProperty("releaseAcceptance").GetBoolean());
        Assert.Equal(0, evidence.RootElement.GetProperty("pendingGates").GetArrayLength());
    }

    [Fact]
    public void Fixed_finding_requires_clean_replacement_and_gate_rerun()
    {
        var root = ResolveRepositoryRoot();
        using var directory = new TemporaryDirectory();
        var sessions = Path.Combine(directory.Path, "sessions");
        var ledger = Path.Combine(directory.Path, "candidate-ledger.json");
        var findings = Path.Combine(directory.Path, "findings.json");
        var firstRevision = new string('a', 40);
        var finalRevision = new string('b', 40);
        WriteSession(Path.Combine(sessions, "session-100.json"), Session(100, firstRevision, ["EXT-001"]));
        WriteCompleteFinalSessions(sessions, finalRevision);
        WriteEvidence(Path.Combine(directory.Path, "evidence", "native-smoke.json"));
        WriteEvidence(Path.Combine(directory.Path, "evidence", "finding-verification.json"));
        WriteJson(ledger, Ledger(Candidate(firstRevision), Candidate(finalRevision, firstRevision, "EXT-001")));
        WriteJson(findings, new JsonObject
        {
            ["schemaVersion"] = 1,
            ["kind"] = "vibesnake-external-finding-review-v1",
            ["findings"] = new JsonArray(
                new JsonObject
                {
                    ["findingId"] = "EXT-001",
                    ["sessionIds"] = StringArray("external-session-100"),
                    ["severity"] = "P1",
                    ["reportFamily"] = "defect",
                    ["affectedGateIds"] = StringArray("native-smoke"),
                    ["decision"] = "fix",
                    ["resolutionStatus"] = "closed",
                    ["workaround"] = null,
                    ["resolutionRevision"] = finalRevision,
                    ["verificationEvidencePaths"] = StringArray("evidence/finding-verification.json"),
                }),
        });

        var evaluation = ExternalValidationCheck.Evaluate(root, sessions, ledger, findings);
        using var evidence = JsonDocument.Parse(evaluation.Json);

        Assert.True(evaluation.Passed, string.Join(Environment.NewLine, evaluation.Errors));
        Assert.Equal(2, evidence.RootElement.GetProperty("candidateCount").GetInt32());
        Assert.Equal(1, evidence.RootElement.GetProperty("findingCount").GetInt32());
        Assert.True(evidence.RootElement.GetProperty("externalValidationComplete").GetBoolean());
    }

    [Fact]
    public void Missing_evidence_and_failed_fresh_comprehension_block_acceptance()
    {
        var root = ResolveRepositoryRoot();
        using var directory = new TemporaryDirectory();
        var sessions = Path.Combine(directory.Path, "sessions");
        var ledger = Path.Combine(directory.Path, "candidate-ledger.json");
        var findings = Path.Combine(directory.Path, "findings.json");
        var revision = new string('a', 40);
        WriteCompleteFinalSessions(sessions, revision);
        var firstSessionPath = Path.Combine(sessions, "session-0.json");
        var firstSession = JsonNode.Parse(File.ReadAllText(firstSessionPath))!.AsObject();
        firstSession["comprehensionResults"]!.AsArray()[0]!["result"] = "fail";
        WriteJson(firstSessionPath, firstSession);
        File.Delete(Path.Combine(sessions, "evidence", "session-000", "defect.json"));
        WriteJson(ledger, Ledger(Candidate(revision)));
        WriteEmptyFindings(findings);

        var evaluation = ExternalValidationCheck.Evaluate(root, sessions, ledger, findings);
        using var evidence = JsonDocument.Parse(evaluation.Json);

        Assert.False(evidence.RootElement.GetProperty("externalValidationComplete").GetBoolean());
        Assert.False(evidence.RootElement.GetProperty("releaseAcceptance").GetBoolean());
        Assert.Contains(evaluation.Errors, error => error.Contains("missing retained files", StringComparison.Ordinal));
        Assert.Contains(
            evaluation.Errors,
            error => error.Contains("fresh participant must pass every comprehension check", StringComparison.Ordinal));
    }

    [Fact]
    public void Malformed_session_fails_closed_without_an_exception()
    {
        var root = ResolveRepositoryRoot();
        using var directory = new TemporaryDirectory();
        var sessions = Path.Combine(directory.Path, "sessions");
        var ledger = Path.Combine(directory.Path, "candidate-ledger.json");
        var findings = Path.Combine(directory.Path, "findings.json");
        var revision = new string('a', 40);
        WriteCompleteFinalSessions(sessions, revision);
        var malformedPath = Path.Combine(sessions, "session-1.json");
        var malformed = JsonNode.Parse(File.ReadAllText(malformedPath))!.AsObject();
        var unexpected = new JsonObject { ["unexpected"] = "object" };
        var devices = new JsonArray();
        devices.Add(unexpected);
        malformed["inputDeviceIds"] = devices;
        WriteJson(malformedPath, malformed);
        WriteJson(ledger, Ledger(Candidate(revision)));
        WriteEmptyFindings(findings);

        var evaluation = ExternalValidationCheck.Evaluate(root, sessions, ledger, findings);
        using var evidence = JsonDocument.Parse(evaluation.Json);

        Assert.False(evidence.RootElement.GetProperty("externalValidationComplete").GetBoolean());
        Assert.Contains(
            evaluation.Errors,
            error => error.Contains("inputDeviceIds must contain unique supported devices", StringComparison.Ordinal));
    }

    [Fact]
    public void Commands_qualify_foundation_and_reject_partial_alias_and_bad_usage()
    {
        var root = ResolveRepositoryRoot();
        using var directory = new TemporaryDirectory();
        var output = new StringWriter();
        var error = new StringWriter();
        var code = RepositoryCheckCommand.Run(["external-validation", root], output, error);

        Assert.Equal(0, code);
        Assert.Contains("controlled participant execution remains pending", output.ToString(), StringComparison.Ordinal);
        Assert.Equal(string.Empty, error.ToString());

        var handoff = Path.Combine(directory.Path, "handoff.json");
        output = new StringWriter();
        error = new StringWriter();
        code = RepositoryCheckCommand.Run(["external-validation-write", handoff, root], output, error);
        Assert.Equal(0, code);
        Assert.True(File.Exists(handoff));

        var together = ExternalValidationCheck.Evaluate(root, directory.Path, null, null);
        Assert.Contains(
            together.Errors,
            item => item == "sessions, candidate ledger, and findings must be supplied together");

        var contract = Path.Combine(root, "config", "qa_external_validation_v1.json");
        var before = File.ReadAllBytes(contract);
        var alias = ExternalValidationCheck.Evaluate(root, null, null, null, outputPath: contract);
        Assert.Contains(alias.Errors, item => item.Contains("cannot alias", StringComparison.Ordinal));
        Assert.Equal(before, File.ReadAllBytes(contract));

        output = new StringWriter();
        error = new StringWriter();
        code = RepositoryCheckCommand.Run(
            [
                "external-validation-record",
                Path.Combine(directory.Path, "missing-sessions"),
                Path.Combine(directory.Path, "missing-ledger.json"),
                Path.Combine(directory.Path, "missing-findings.json"),
                Path.Combine(directory.Path, "decision.json"),
                root,
            ],
            output,
            error);
        Assert.Equal(1, code);
        Assert.StartsWith("External validation qualification failed:" + Environment.NewLine, error.ToString(), StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(directory.Path, "decision.json")));
    }

    [Fact]
    public void Closed_inputs_reject_mistyped_fields_and_incomplete_closure()
    {
        var root = ResolveRepositoryRoot();
        using var directory = new TemporaryDirectory();
        var contractPath = Path.Combine(directory.Path, "contract.json");
        var sourceContract = File.ReadAllText(Path.Combine(root, "config", "qa_external_validation_v1.json"));

        void RejectContract(Action<JsonObject> mutate, string fragment)
        {
            var document = JsonNode.Parse(sourceContract)!.AsObject();
            mutate(document);
            WriteJson(contractPath, document);
            var evaluation = ExternalValidationCheck.Evaluate(root, null, null, null, contractPath);
            Assert.Contains(evaluation.Errors, error => error.Contains(fragment, StringComparison.Ordinal));
        }

        RejectContract(document => document["schemaVersion"] = "1", "contract.schemaVersion");
        RejectContract(document => document["kind"] = true, "contract.kind");
        RejectContract(document => document["kind"] = JsonNode.Parse("null"), "contract.kind");
        RejectContract(document => document["status"] = 1, "contract.status");
        RejectContract(document => document["participantIdPattern"] = "external-[0-9]{4}", "contract participant pattern");
        RejectContract(document => document["artifactPlatforms"] = new JsonArray("windows-x64"), "contract artifact platforms");
        RejectContract(document => document["cohorts"] = new JsonArray(), "contract cohorts");
        RejectContract(document => document["cohorts"]![0]!["freshParticipantRequired"] = false, "contract cohorts");
        RejectContract(document => document["cohorts"]![0] = "nope", "contract cohorts");
        RejectContract(document => document["inputDevices"] = new JsonObject(), "contract.inputDevices");
        RejectContract(
            document =>
            {
                var profiles = new JsonArray();
                profiles.Add(1);
                document["accessibilityProfiles"] = profiles;
            },
            "contract.accessibilityProfiles");
        RejectContract(document => document["comprehensionChecks"] = new JsonArray("nope"), "contract.comprehensionChecks");
        RejectContract(document => document["reportFamilies"] = true, "contract.reportFamilies");
        RejectContract(document => document["severityValues"] = new JsonArray(), "contract.severityValues");
        RejectContract(document => document["findingDecisions"] = new JsonArray("fix"), "contract.findingDecisions");
        RejectContract(document => document["resolutionValues"] = false, "contract.resolutionValues");
        RejectContract(document => document["privacyRules"] = new JsonArray(), "contract.privacyRules");
        RejectContract(document => document.Remove("releaseRules"), "contract fields");
        RejectContract(document => document["extra"] = 1, "contract fields");

        File.WriteAllText(contractPath, "[1]\n");
        Assert.Contains(
            ExternalValidationCheck.Evaluate(root, null, null, null, contractPath).Errors,
            error => error.Contains("contract must be an object", StringComparison.Ordinal));
        File.WriteAllText(contractPath, "{\"schemaVersion\": 1,}\n");
        Assert.Contains(
            ExternalValidationCheck.Evaluate(root, null, null, null, contractPath).Errors,
            error => error.Contains("unreadable external validation contract", StringComparison.Ordinal));
        File.WriteAllBytes(contractPath, [0xFF]);
        Assert.Contains(
            ExternalValidationCheck.Evaluate(root, null, null, null, contractPath).Errors,
            error => error.Contains("unreadable external validation contract", StringComparison.Ordinal));

        var sessions = Path.Combine(directory.Path, "sessions");
        var ledgerPath = Path.Combine(directory.Path, "ledger.json");
        var findingsPath = Path.Combine(directory.Path, "findings.json");
        var revision = new string('a', 40);
        Directory.CreateDirectory(sessions);
        WriteEmptyFindings(findingsPath);

        void RejectLedger(Action<JsonObject> mutate, string fragment)
        {
            var document = Ledger(Candidate(revision));
            mutate(document);
            WriteJson(ledgerPath, document);
            var evaluation = ExternalValidationCheck.Evaluate(root, sessions, ledgerPath, findingsPath);
            Assert.Contains(evaluation.Errors, error => error.Contains(fragment, StringComparison.Ordinal));
        }

        RejectLedger(document => document["candidates"] = new JsonArray(), "candidates must be a nonempty array");
        RejectLedger(document => document["candidates"] = new JsonObject(), "candidates must be a nonempty array");
        RejectLedger(document => document["schemaVersion"] = "1", "candidate ledger.schemaVersion");
        RejectLedger(document => document["kind"] = 1, "candidate ledger.kind");
        RejectLedger(document => document.Remove("kind"), "candidate ledger fields");
        RejectLedger(document => document["candidates"]![0]!["revision"] = "nope", "revision must be a lowercase");
        RejectLedger(document => document["candidates"]![0]!["revision"] = 1, "revision must be a lowercase");
        RejectLedger(document => document["candidates"]![0]!["sourceTreeClean"] = false, "sourceTreeClean");
        RejectLedger(document => document["candidates"]![0]!["sourceTreeClean"] = "yes", "sourceTreeClean");
        RejectLedger(document => document["candidates"]![0]!["startedUtc"] = "tomorrow", "startedUtc must use");
        RejectLedger(document => document["candidates"]![0]!["startedUtc"] = 1, "startedUtc must use");
        RejectLedger(document => document["candidates"]![0]!["artifactSha256ByPlatform"]!["windows-x64"] = "zz", "must contain SHA-256");
        RejectLedger(document => document["candidates"]![0]!["artifactSha256ByPlatform"]!.AsObject().Remove("linux-x64"), "artifactSha256ByPlatform");
        RejectLedger(document => document["candidates"]![0]!["supersedesRevision"] = revision, "supersedesRevision");
        RejectLedger(document => document["candidates"]![0]!["triggerFindingIds"] = StringArray("EXT-001"), "triggerFindingIds");
        RejectLedger(document => document["candidates"]![0]!["affectedGateIds"] = StringArray("native-smoke"), "affectedGateIds");
        RejectLedger(
            document => document["candidates"]![0]!["gateRerunEvidencePaths"] = new JsonObject { ["native-smoke"] = StringArray("evidence/native-smoke.json") },
            "gateRerunEvidencePaths");

        var second = Candidate(new string('b', 40), revision, "EXT-001");
        second["startedUtc"] = "2026-08-09T11:00:00Z";
        WriteEvidence(Path.Combine(directory.Path, "evidence", "native-smoke.json"));
        WriteJson(ledgerPath, Ledger(Candidate(revision), second));
        Assert.Contains(
            ExternalValidationCheck.Evaluate(root, sessions, ledgerPath, findingsPath).Errors,
            error => error.Contains("startedUtc must be later", StringComparison.Ordinal));
        var duplicate = Candidate(revision);
        duplicate["startedUtc"] = "2026-08-09T13:00:00Z";
        WriteJson(ledgerPath, Ledger(Candidate(revision), duplicate));
        Assert.Contains(
            ExternalValidationCheck.Evaluate(root, sessions, ledgerPath, findingsPath).Errors,
            error => error.Contains("duplicate candidate revision", StringComparison.Ordinal));
        File.WriteAllText(ledgerPath, "[1]\n");
        Assert.Contains(
            ExternalValidationCheck.Evaluate(root, sessions, ledgerPath, findingsPath).Errors,
            error => error.Contains("candidate ledger must be an object", StringComparison.Ordinal));
        WriteJson(ledgerPath, Ledger(Candidate(revision)));

        JsonObject Finding(string findingId, string decision, string resolution, string severity, JsonNode? workaround, JsonNode? resolutionRevision)
        {
            return new JsonObject
            {
                ["findingId"] = findingId,
                ["sessionIds"] = StringArray("external-session-000"),
                ["severity"] = severity,
                ["reportFamily"] = "defect",
                ["affectedGateIds"] = StringArray("native-smoke"),
                ["decision"] = decision,
                ["resolutionStatus"] = resolution,
                ["workaround"] = workaround,
                ["resolutionRevision"] = resolutionRevision,
                ["verificationEvidencePaths"] = resolution == "closed" && decision == "fix"
                    ? StringArray("evidence/finding-verification.json")
                    : new JsonArray(),
            };
        }

        void RejectFinding(JsonObject finding, string fragment)
        {
            WriteJson(findingsPath, new JsonObject
            {
                ["schemaVersion"] = 1,
                ["kind"] = "vibesnake-external-finding-review-v1",
                ["findings"] = new JsonArray(finding),
            });
            var evaluation = ExternalValidationCheck.Evaluate(root, sessions, ledgerPath, findingsPath);
            Assert.Contains(evaluation.Errors, error => error.Contains(fragment, StringComparison.Ordinal));
        }

        WriteEvidence(Path.Combine(directory.Path, "evidence", "finding-verification.json"));
        RejectFinding(Finding("BAD", "fix", "open", "P3", null, null), "findingId must match");
        RejectFinding(Finding("EXT-001", "ship", "open", "P0", null, null), "cannot ship a P0 or P1");
        RejectFinding(Finding("EXT-001", "not-reproducible", "open", "P2", null, null), "P2 decision must be fix or ship");
        RejectFinding(Finding("EXT-001", "ship", "open", "P2", null, null), "player-facing workaround");
        RejectFinding(Finding("EXT-001", "ship", "open", "P2", " ", null), "player-facing workaround");
        RejectFinding(Finding("EXT-001", "fix", "closed", "P1", null, "nope"), "resolutionRevision must identify");
        RejectFinding(Finding("EXT-001", "ship", "closed", "P3", null, revision), "resolutionRevision must be None");
        RejectFinding(Finding("EXT-001", "fix", "open", "P1", null, revision), "resolutionRevision must be None");
        RejectFinding(Finding("EXT-001", "later", "open", "P3", null, null), "decision is unsupported");
        RejectFinding(Finding("EXT-001", "fix", "later", "P3", null, null), "resolutionStatus is unsupported");
        RejectFinding(Finding("EXT-001", "fix", "open", "P9", null, null), "severity is unsupported");
        var family = Finding("EXT-001", "fix", "open", "P3", null, null);
        family["reportFamily"] = "praise";
        RejectFinding(family, "reportFamily is unsupported");
        var gates = Finding("EXT-001", "fix", "open", "P3", null, null);
        gates["affectedGateIds"] = new JsonArray();
        RejectFinding(gates, "affectedGateIds must be nonempty");
        var sessionsIds = Finding("EXT-001", "fix", "open", "P3", null, null);
        sessionsIds["sessionIds"] = StringArray("session-1");
        RejectFinding(sessionsIds, "sessionIds must contain unique");
        var duplicateFinding = Finding("EXT-001", "fix", "open", "P3", null, null);
        WriteJson(findingsPath, new JsonObject
        {
            ["schemaVersion"] = 1,
            ["kind"] = "vibesnake-external-finding-review-v1",
            ["findings"] = new JsonArray(duplicateFinding, duplicateFinding.DeepClone()),
        });
        Assert.Contains(
            ExternalValidationCheck.Evaluate(root, sessions, ledgerPath, findingsPath).Errors,
            error => error.Contains("duplicate findingId", StringComparison.Ordinal));
        File.WriteAllText(findingsPath, "[1]\n");
        Assert.Contains(
            ExternalValidationCheck.Evaluate(root, sessions, ledgerPath, findingsPath).Errors,
            error => error.Contains("finding review must be an object", StringComparison.Ordinal));
        WriteJson(findingsPath, new JsonObject
        {
            ["schemaVersion"] = 1,
            ["kind"] = "vibesnake-external-finding-review-v1",
            ["findings"] = new JsonObject(),
        });
        Assert.Contains(
            ExternalValidationCheck.Evaluate(root, sessions, ledgerPath, findingsPath).Errors,
            error => error.Contains("findings must be an array", StringComparison.Ordinal));
        WriteEmptyFindings(findingsPath);

        void RejectSession(Action<JsonObject> mutate, string fragment)
        {
            var session = Session(0, revision);
            mutate(session);
            var path = Path.Combine(sessions, "session-extra.json");
            WriteJson(path, session);
            var evaluation = ExternalValidationCheck.Evaluate(root, sessions, ledgerPath, findingsPath);
            Assert.Contains(evaluation.Errors, error => error.Contains(fragment, StringComparison.Ordinal));
            File.Delete(path);
        }

        RejectSession(session => session["sessionId"] = "session-1", "sessionId must match");
        RejectSession(session => session["participantId"] = "person-1", "participantId must match");
        RejectSession(session => session["cohortId"] = "nope", "cohortId is unsupported");
        RejectSession(session => session["candidateRevision"] = "nope", "candidateRevision must be");
        RejectSession(session => session["artifactPlatform"] = "nope", "artifactPlatform is unsupported");
        RejectSession(session => session["artifactSha256"] = "nope", "artifactSha256 must be");
        RejectSession(session => session["appVersion"] = " ", "appVersion must be a nonempty string");
        RejectSession(session => session["cleanInstall"] = "yes", "must be booleans");
        RejectSession(session => session["neverSeenRepository"] = 1, "must be booleans");
        RejectSession(session => session["inputDeviceIds"] = new JsonArray(), "inputDeviceIds must contain unique");
        RejectSession(session => session["inputDeviceIds"] = StringArray("keyboard", "keyboard"), "inputDeviceIds must contain unique");
        RejectSession(session => session["inputDeviceIds"] = StringArray("mouse"), "keyboard cohort must use the keyboard");
        RejectSession(session => session["cohortId"] = "clean-install-fresh-controller", "controller cohort must use");
        RejectSession(session => session["accessibilityProfileIds"] = StringArray("nope"), "accessibilityProfileIds must contain unique");
        RejectSession(session => session["accessibilityProfileIds"] = StringArray("default", "default"), "accessibilityProfileIds must contain unique");
        RejectSession(
            session =>
            {
                session["cohortId"] = "accessibility-focused-fresh";
                session["accessibilityProfileIds"] = StringArray("default");
            },
            "accessibility cohort must use a non-default profile");
        RejectSession(session => session["executedUtc"] = "tomorrow", "executedUtc must use");
        RejectSession(session => session["distributionId"] = "", "distributionId must be a nonempty string");
        RejectSession(session => session["consentRecordedSeparately"] = false, "consentRecordedSeparately");
        RejectSession(session => session["reportFamilyPaths"]!.AsObject().Remove("crash"), "reportFamilyPaths");
        RejectSession(session => session["comprehensionResults"] = new JsonObject(), "comprehensionResults must be an array");
        RejectSession(session => session["comprehensionResults"]![0]!["checkId"] = "nope", "checkId must be unique");
        RejectSession(
            session => session["comprehensionResults"]!.AsArray().Add(session["comprehensionResults"]![0]!.DeepClone()),
            "checkId must be unique");
        RejectSession(session => session["comprehensionResults"]![0]!["result"] = "maybe", "result is unsupported");
        RejectSession(session => session["comprehensionResults"]!.AsArray().RemoveAt(0), "must cover every required check");
        RejectSession(session => session["crashObserved"] = "no", "crashObserved must be a boolean");
        RejectSession(session => session["findingIds"] = StringArray("BAD"), "findingIds must contain unique");
        RejectSession(session => session["findingIds"] = StringArray("EXT-001", "EXT-001"), "findingIds must contain unique");
        RejectSession(session => session["evidencePaths"] = new JsonArray(), "evidencePaths must contain safe relative paths");
        RejectSession(session => session["evidencePaths"] = StringArray("../secret.json"), "evidencePaths must contain safe relative paths");
        RejectSession(session => session["evidencePaths"] = StringArray("missing.json"), "missing retained files");
        RejectSession(session => session.Remove("kind"), "fields must be");

        WriteSession(Path.Combine(sessions, "session-0.json"), Session(0, revision));
        var copy = JsonNode.Parse(File.ReadAllText(Path.Combine(sessions, "session-0.json")))!.AsObject();
        copy["cohortId"] = "returning-regression";
        copy["sessionId"] = "external-session-050";
        copy["neverSeenRepository"] = false;
        WriteSession(Path.Combine(sessions, "session-cohort.json"), copy);
        Assert.Contains(
            ExternalValidationCheck.Evaluate(root, sessions, ledgerPath, findingsPath).Errors,
            error => error.Contains("cannot represent multiple cohorts", StringComparison.Ordinal));
        File.Delete(Path.Combine(sessions, "session-cohort.json"));

        var sameParticipant = JsonNode.Parse(File.ReadAllText(Path.Combine(sessions, "session-0.json")))!.AsObject();
        sameParticipant["sessionId"] = "external-session-051";
        WriteSession(Path.Combine(sessions, "session-same.json"), sameParticipant);
        Assert.Contains(
            ExternalValidationCheck.Evaluate(root, sessions, ledgerPath, findingsPath).Errors,
            error => error.Contains("distinct participants", StringComparison.Ordinal));
        File.Delete(Path.Combine(sessions, "session-same.json"));

        var unknownRevision = JsonNode.Parse(File.ReadAllText(Path.Combine(sessions, "session-0.json")))!.AsObject();
        unknownRevision["candidateRevision"] = new string('c', 40);
        unknownRevision["sessionId"] = "external-session-052";
        unknownRevision["participantId"] = "external-052";
        WriteSession(Path.Combine(sessions, "session-unknown.json"), unknownRevision);
        Assert.Contains(
            ExternalValidationCheck.Evaluate(root, sessions, ledgerPath, findingsPath).Errors,
            error => error.Contains("undeclared candidate revision", StringComparison.Ordinal));
        File.Delete(Path.Combine(sessions, "session-unknown.json"));

        var wrongHash = JsonNode.Parse(File.ReadAllText(Path.Combine(sessions, "session-0.json")))!.AsObject();
        wrongHash["artifactSha256"] = new string('9', 64);
        wrongHash["sessionId"] = "external-session-053";
        wrongHash["participantId"] = "external-053";
        WriteSession(Path.Combine(sessions, "session-hash.json"), wrongHash);
        Assert.Contains(
            ExternalValidationCheck.Evaluate(root, sessions, ledgerPath, findingsPath).Errors,
            error => error.Contains("artifact hash does not match", StringComparison.Ordinal));
        File.Delete(Path.Combine(sessions, "session-hash.json"));

        var unknownFinding = JsonNode.Parse(File.ReadAllText(Path.Combine(sessions, "session-0.json")))!.AsObject();
        unknownFinding["findingIds"] = StringArray("EXT-009");
        WriteJson(Path.Combine(sessions, "session-0.json"), unknownFinding);
        Assert.Contains(
            ExternalValidationCheck.Evaluate(root, sessions, ledgerPath, findingsPath).Errors,
            error => error.Contains("unknown finding", StringComparison.Ordinal));
        WriteSession(Path.Combine(sessions, "session-0.json"), Session(0, revision));

        var otherVersion = Session(1, revision);
        otherVersion["appVersion"] = "0.8.0";
        otherVersion["participantId"] = "external-054";
        WriteSession(Path.Combine(sessions, "session-version.json"), otherVersion);
        Assert.Contains(
            ExternalValidationCheck.Evaluate(root, sessions, ledgerPath, findingsPath).Errors,
            error => error.Contains("must use one application version", StringComparison.Ordinal));
        File.Delete(Path.Combine(sessions, "session-version.json"));

        WriteJson(findingsPath, new JsonObject
        {
            ["schemaVersion"] = 1,
            ["kind"] = "vibesnake-external-finding-review-v1",
            ["findings"] = new JsonArray(Finding("EXT-002", "fix", "open", "P2", null, null)),
        });
        Assert.Contains(
            ExternalValidationCheck.Evaluate(root, sessions, ledgerPath, findingsPath).Errors,
            error => error.Contains("unresolved blocking findings", StringComparison.Ordinal));
        var unknownSession = Finding("EXT-004", "fix", "open", "P3", null, null);
        unknownSession["sessionIds"] = StringArray("external-session-777");
        WriteJson(findingsPath, new JsonObject
        {
            ["schemaVersion"] = 1,
            ["kind"] = "vibesnake-external-finding-review-v1",
            ["findings"] = new JsonArray(unknownSession),
        });
        Assert.Contains(
            ExternalValidationCheck.Evaluate(root, sessions, ledgerPath, findingsPath).Errors,
            error => error.Contains("unknown session", StringComparison.Ordinal));

        var replacement = Candidate(new string('b', 40), revision, "EXT-009");
        WriteJson(ledgerPath, Ledger(Candidate(revision), replacement));
        WriteEmptyFindings(findingsPath);
        Assert.Contains(
            ExternalValidationCheck.Evaluate(root, sessions, ledgerPath, findingsPath).Errors,
            error => error.Contains("unknown trigger finding", StringComparison.Ordinal));
        WriteJson(findingsPath, new JsonObject
        {
            ["schemaVersion"] = 1,
            ["kind"] = "vibesnake-external-finding-review-v1",
            ["findings"] = new JsonArray(Finding("EXT-003", "ship", "open", "P3", "documented workaround", null)),
        });
        var shipTrigger = Candidate(new string('b', 40), revision, "EXT-003");
        WriteJson(ledgerPath, Ledger(Candidate(revision), shipTrigger));
        Assert.Contains(
            ExternalValidationCheck.Evaluate(root, sessions, ledgerPath, findingsPath).Errors,
            error => error.Contains("is not a fix decision", StringComparison.Ordinal));
        var gateDrift = Candidate(new string('b', 40), revision, "EXT-001");
        gateDrift["affectedGateIds"] = StringArray("other-gate");
        gateDrift["gateRerunEvidencePaths"] = new JsonObject { ["other-gate"] = StringArray("evidence/native-smoke.json") };
        WriteJson(ledgerPath, Ledger(Candidate(revision), gateDrift));
        WriteJson(findingsPath, new JsonObject
        {
            ["schemaVersion"] = 1,
            ["kind"] = "vibesnake-external-finding-review-v1",
            ["findings"] = new JsonArray(Finding("EXT-001", "fix", "closed", "P1", null, new string('b', 40))),
        });
        Assert.Contains(
            ExternalValidationCheck.Evaluate(root, sessions, ledgerPath, findingsPath).Errors,
            error => error.Contains("affected gates do not match", StringComparison.Ordinal));

        Directory.Delete(sessions, recursive: true);
        Assert.Contains(
            ExternalValidationCheck.Evaluate(root, sessions, ledgerPath, findingsPath).Errors,
            error => error.Contains("sessions directory does not exist", StringComparison.Ordinal));
        Directory.CreateDirectory(sessions);
        Assert.Contains(
            ExternalValidationCheck.Evaluate(root, sessions, ledgerPath, findingsPath).Errors,
            error => error.Contains("contains no JSON sessions", StringComparison.Ordinal));
        Assert.Contains(
            ExternalValidationCheck.Evaluate(root, sessions, null, findingsPath).Errors,
            error => error.Contains("must be supplied together", StringComparison.Ordinal));

        var outputDirectory = Path.Combine(directory.Path, "output-directory");
        Directory.CreateDirectory(outputDirectory);
        Assert.Contains(
            ExternalValidationCheck.Evaluate(root, null, null, null, outputPath: outputDirectory).Errors,
            error => error.Contains("must be a regular file", StringComparison.Ordinal));
        Assert.Contains(
            ExternalValidationCheck.Evaluate(root, null, null, null, outputPath: Path.GetPathRoot(directory.Path)!).Errors,
            error => error.Contains("no parent directory", StringComparison.Ordinal));
    }

    private static JsonObject Candidate(string revision, string? prior = null, string? trigger = null)
    {
        var replacement = prior is not null;
        var hashes = new JsonObject();
        foreach (var platform in Platforms)
        {
            hashes[platform] = ArtifactHashes[platform];
        }

        return new JsonObject
        {
            ["revision"] = revision,
            ["sourceTreeClean"] = true,
            ["artifactSha256ByPlatform"] = hashes,
            ["startedUtc"] = replacement ? "2026-08-09T13:00:00Z" : "2026-08-09T12:00:00Z",
            ["supersedesRevision"] = prior,
            ["triggerFindingIds"] = trigger is null ? new JsonArray() : StringArray(trigger),
            ["affectedGateIds"] = replacement ? StringArray("native-smoke") : new JsonArray(),
            ["gateRerunEvidencePaths"] = replacement
                ? new JsonObject { ["native-smoke"] = StringArray("evidence/native-smoke.json") }
                : new JsonObject(),
        };
    }

    private static JsonObject Ledger(params JsonObject[] candidates) =>
        new()
        {
            ["schemaVersion"] = 1,
            ["kind"] = "vibesnake-external-candidate-ledger-v1",
            ["candidates"] = new JsonArray(candidates),
        };

    private static JsonObject Session(int index, string revision, string[]? findingIds = null)
    {
        var cohort = Cohorts[index % Cohorts.Length];
        var platform = Platforms[index % Platforms.Length];
        var devices = (index % 4) switch
        {
            0 => StringArray("keyboard"),
            1 => StringArray("xbox-layout-controller"),
            2 => StringArray("mouse", "playstation-layout-controller"),
            _ => StringArray("keyboard"),
        };
        var profiles = cohort == "accessibility-focused-fresh" ? StringArray("high-contrast") : StringArray("default");
        var reports = new JsonObject();
        foreach (var family in Families)
        {
            reports[family] = StringArray("evidence/session-" + index.ToString("000", CultureInfo.InvariantCulture) + "/" + family + ".json");
        }

        var results = new JsonArray();
        foreach (var checkId in Checks)
        {
            results.Add(new JsonObject
            {
                ["checkId"] = checkId,
                ["result"] = "pass",
            });
        }

        return new JsonObject
        {
            ["schemaVersion"] = 1,
            ["kind"] = "vibesnake-external-validation-session-v1",
            ["sessionId"] = "external-session-" + index.ToString("000", CultureInfo.InvariantCulture),
            ["participantId"] = "external-" + index.ToString("000", CultureInfo.InvariantCulture),
            ["cohortId"] = cohort,
            ["candidateRevision"] = revision,
            ["artifactPlatform"] = platform,
            ["artifactSha256"] = ArtifactHashes[platform],
            ["appVersion"] = "0.9.0",
            ["cleanInstall"] = true,
            ["neverSeenRepository"] = cohort != "returning-regression",
            ["inputDeviceIds"] = devices,
            ["accessibilityProfileIds"] = profiles,
            ["executedUtc"] = "2026-08-09T12:00:" + (index % 60).ToString("00", CultureInfo.InvariantCulture) + "Z",
            ["distributionId"] = "controlled-candidate-group-001",
            ["consentRecordedSeparately"] = true,
            ["reportFamilyPaths"] = reports,
            ["comprehensionResults"] = results,
            ["crashObserved"] = false,
            ["findingIds"] = findingIds is null ? new JsonArray() : StringArray(findingIds),
            ["evidencePaths"] = StringArray("evidence/session-" + index.ToString("000", CultureInfo.InvariantCulture) + "/summary.json"),
        };
    }

    private static void WriteCompleteFinalSessions(string sessions, string revision)
    {
        for (var index = 0; index < 4; index++)
        {
            WriteSession(Path.Combine(sessions, "session-" + index.ToString(CultureInfo.InvariantCulture) + ".json"), Session(index, revision));
        }
    }

    private static void WriteEmptyFindings(string path) =>
        WriteJson(path, new JsonObject
        {
            ["schemaVersion"] = 1,
            ["kind"] = "vibesnake-external-finding-review-v1",
            ["findings"] = new JsonArray(),
        });

    private static void WriteSession(string path, JsonObject session)
    {
        WriteJson(path, session);
        foreach (var family in session["reportFamilyPaths"]!.AsObject())
        {
            foreach (var relative in family.Value!.AsArray())
            {
                WriteEvidence(Path.Combine(Path.GetDirectoryName(path)!, relative!.GetValue<string>().Replace('/', Path.DirectorySeparatorChar)));
            }
        }

        foreach (var relative in session["evidencePaths"]!.AsArray())
        {
            WriteEvidence(Path.Combine(Path.GetDirectoryName(path)!, relative!.GetValue<string>().Replace('/', Path.DirectorySeparatorChar)));
        }
    }

    private static void WriteEvidence(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, "retained de-identified external validation evidence"u8.ToArray());
    }

    private static void WriteJson(string path, JsonNode node)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, node.ToJsonString(JsonOptions).Replace("\r\n", "\n", StringComparison.Ordinal) + "\n");
    }

    private static JsonArray StringArray(params string[] values)
    {
        var array = new JsonArray();
        foreach (var value in values)
        {
            array.Add(value);
        }

        return array;
    }

    private static string[] Strings(JsonElement element) =>
        element.EnumerateArray().Select(item => item.GetString()!).ToArray();

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
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "vibesnake-external-" + Guid.NewGuid().ToString("N"));
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
