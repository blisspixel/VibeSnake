using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace RepositoryChecks;

public static class ExternalValidationCheck
{
    private const string ContractRelativePath = "config/qa_external_validation_v1.json";
    private const string PendingMessage =
        "External validation handoff qualified; controlled participant execution remains pending.";

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private static readonly JsonSerializerOptions RenderOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true,
        IndentCharacter = ' ',
        IndentSize = 2,
    };
    private static readonly Regex RevisionPattern = new("^[0-9a-f]{40}$", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex Sha256Pattern = new("^[0-9a-f]{64}$", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private const string ParticipantPatternText = "external-[0-9]{3}";
    private static readonly Regex ParticipantPattern = new(
        "^" + ParticipantPatternText + "$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex SessionIdPattern = new("^external-session-[0-9]{3}$", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex FindingIdPattern = new("^EXT-[0-9]{3}$", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex UtcPattern = new(
        @"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}Z$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly string[] ArtifactPlatforms = ["windows-x64", "macos-universal", "linux-x64"];
    private static readonly Cohort[] Cohorts =
    [
        new("clean-install-fresh-keyboard", true, true, true),
        new("clean-install-fresh-controller", true, true, true),
        new("accessibility-focused-fresh", true, true, true),
        new("returning-regression", false, true, false),
    ];
    private static readonly string[] InputDevices =
    [
        "keyboard",
        "mouse",
        "xbox-layout-controller",
        "playstation-layout-controller",
    ];
    private static readonly string[] AccessibilityProfiles =
    [
        "default",
        "sound-muted",
        "zero-shake",
        "reduced-motion",
        "flash-free",
        "high-contrast",
        "maximum-text-scale",
    ];
    private static readonly string[] ComprehensionChecks =
    [
        "death-explanation",
        "recovery-identification",
        "power-route-decision-explanation",
        "escalation-recognition",
        "another-run-intent",
        "another-run-reason",
    ];
    private static readonly string[] ReportFamilies = ["defect", "comprehension", "accessibility", "crash"];
    private static readonly string[] SeverityValues = ["P0", "P1", "P2", "P3"];
    private static readonly string[] FindingDecisions = ["fix", "ship", "not-reproducible"];
    private static readonly string[] ResolutionValues = ["open", "closed"];
    private static readonly string[] SessionFields =
    [
        "schemaVersion", "kind", "sessionId", "participantId", "cohortId", "candidateRevision",
        "artifactPlatform", "artifactSha256", "appVersion", "cleanInstall", "neverSeenRepository",
        "inputDeviceIds", "accessibilityProfileIds", "executedUtc", "distributionId",
        "consentRecordedSeparately", "reportFamilyPaths", "comprehensionResults", "crashObserved",
        "findingIds", "evidencePaths",
    ];
    private static readonly string[] ComprehensionResultFields = ["checkId", "result"];
    private static readonly string[] ComprehensionResultValues = ["pass", "fail", "blocked"];
    private static readonly string[] CandidateFields =
    [
        "revision", "sourceTreeClean", "artifactSha256ByPlatform", "startedUtc", "supersedesRevision",
        "triggerFindingIds", "affectedGateIds", "gateRerunEvidencePaths",
    ];
    private static readonly string[] FindingFields =
    [
        "findingId", "sessionIds", "severity", "reportFamily", "affectedGateIds", "decision",
        "resolutionStatus", "workaround", "resolutionRevision", "verificationEvidencePaths",
    ];
    private static readonly string[] PrerequisitePaths =
    [
        "config/qa_human_playtest_protocol.json",
        "config/qa_manual_product_matrix_v2.json",
        "docs/guides/ACCESSIBILITY.md",
        "docs/release/MANUAL_PRODUCT_MATRIX.md",
    ];
    private static readonly string[] PrivacyRules =
    [
        "Consent records stay outside the repository and separate from observations.",
        "Session JSON contains pseudonymous participant IDs and no identifying free text.",
        "Names, accounts, contact details, device serials, system paths, raw input, raw timing, and unrelated device data are forbidden.",
        "Retained reports are reviewed, de-identified files referenced by safe relative paths.",
    ];
    private static readonly string[] ReleaseRules =
    [
        "All sessions use candidates declared in the clean candidate ledger and exact platform artifact hashes.",
        "Every required cohort and artifact platform is represented on the final candidate.",
        "Fresh cohorts use clean installs, have never seen the repository, and pass every comprehension check.",
        "Keyboard, mouse, Xbox-layout controller, and PlayStation-layout controller coverage is retained.",
        "Defect, comprehension, accessibility, and crash outcomes are retained for every session.",
        "Every fix starts a new clean candidate and reruns every declared affected gate.",
        "No P0 or P1 finding remains open, and every P2 is closed by a fix or an explicit ship decision with a player-facing workaround.",
    ];
    private static readonly string[] ContractFields =
    [
        "schemaVersion", "kind", "status", "participantIdPattern", "artifactPlatforms", "cohorts",
        "inputDevices", "accessibilityProfiles", "comprehensionChecks", "reportFamilies", "severityValues",
        "findingDecisions", "resolutionValues", "requiredSessionFields", "requiredComprehensionResultFields",
        "comprehensionResultValues", "requiredCandidateFields", "requiredFindingFields", "prerequisitePaths",
        "privacyRules", "releaseRules",
    ];
    private static readonly string[] PendingGates =
    [
        "controlled-real-artifact-distribution",
        "clean-install-fresh-participants",
        "structured-defect-comprehension-accessibility-crash-reports",
        "fresh-participant-comprehension-and-replay-intent",
        "clean-candidate-fix-and-gate-rerun-loop",
    ];
    private static readonly string[] LedgerFields = ["schemaVersion", "kind", "candidates"];
    private static readonly string[] FindingReviewFields = ["schemaVersion", "kind", "findings"];
    private static readonly HashSet<string> PlatformSet = new(ArtifactPlatforms, StringComparer.Ordinal);
    private static readonly HashSet<string> InputDeviceSet = new(InputDevices, StringComparer.Ordinal);
    private static readonly HashSet<string> ProfileSet = new(AccessibilityProfiles, StringComparer.Ordinal);
    private static readonly HashSet<string> ComprehensionSet = new(ComprehensionChecks, StringComparer.Ordinal);
    private static readonly HashSet<string> ReportFamilySet = new(ReportFamilies, StringComparer.Ordinal);
    private static readonly HashSet<string> SeveritySet = new(SeverityValues, StringComparer.Ordinal);
    private static readonly HashSet<string> DecisionSet = new(FindingDecisions, StringComparer.Ordinal);
    private static readonly HashSet<string> ResolutionSet = new(ResolutionValues, StringComparer.Ordinal);
    private static readonly HashSet<string> ResultValueSet = new(ComprehensionResultValues, StringComparer.Ordinal);
    private static readonly Dictionary<string, Cohort> CohortById = Cohorts.ToDictionary(cohort => cohort.Id, StringComparer.Ordinal);

    public static RepositoryCheckResult Inspect(string repositoryRoot) =>
        ToResult(Evaluate(repositoryRoot, null, null, null));

    public static RepositoryCheckResult WriteFoundationHandoff(string repositoryRoot, string outputPath) =>
        ToResult(Evaluate(repositoryRoot, null, null, null, outputPath: outputPath));

    public static RepositoryCheckResult Record(
        string repositoryRoot,
        string sessionsDirectory,
        string candidateLedgerPath,
        string findingsPath,
        string outputPath) =>
        ToResult(Evaluate(repositoryRoot, sessionsDirectory, candidateLedgerPath, findingsPath, outputPath: outputPath));

    internal static HandoffEvaluation Evaluate(
        string repositoryRoot,
        string? sessionsDirectory,
        string? candidateLedgerPath,
        string? findingsPath,
        string? contractPath = null,
        string? outputPath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        var errors = new List<string>();
        var contractLocation = contractPath ?? Path.Combine(repositoryRoot, ContractRelativePath.Replace('/', Path.DirectorySeparatorChar));
        var contractLoad = StrictJsonFile.Load(contractLocation, "external validation contract", errors);
        using var contractDocument = contractLoad.Document;
        var contractIsObject = false;
        if (contractDocument is null)
        {
            errors.Add("contract must be an object");
        }
        else if (StrictKeys(contractDocument.RootElement, ContractFields, "contract", errors))
        {
            contractIsObject = true;
            ValidateContract(contractDocument.RootElement, errors);
        }
        else
        {
            contractIsObject = contractDocument.RootElement.ValueKind == JsonValueKind.Object;
        }

        var contractFailed = errors.Count > 0;
        var prerequisites = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var relative in PrerequisitePaths)
        {
            var path = Path.Combine(repositoryRoot, relative.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path) || Directory.Exists(path) || (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            {
                errors.Add($"missing external validation prerequisite: {relative}");
                continue;
            }

            prerequisites[relative] = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
        }

        var protocolQualified = !contractFailed && prerequisites.Count == PrerequisitePaths.Length;
        var executionRequested = sessionsDirectory is not null && candidateLedgerPath is not null && findingsPath is not null;
        if ((sessionsDirectory is not null || candidateLedgerPath is not null || findingsPath is not null) && !executionRequested)
        {
            errors.Add("sessions, candidate ledger, and findings must be supplied together");
        }

        var sessions = new List<SessionRecord>();
        var sessionPaths = new List<string>();
        var candidates = new List<CandidateRecord>();
        var findings = new List<FindingRecord>();
        if (executionRequested)
        {
            if (!Directory.Exists(sessionsDirectory))
            {
                errors.Add($"sessions directory does not exist: {StrictJsonFile.Display(sessionsDirectory!)}");
            }
            else
            {
                sessionPaths.AddRange(Directory.EnumerateFiles(sessionsDirectory!, "*.json", SearchOption.TopDirectoryOnly));
                sessionPaths.Sort(StringComparer.Ordinal);
                if (sessionPaths.Count == 0)
                {
                    errors.Add("sessions directory contains no JSON sessions");
                }

                foreach (var path in sessionPaths)
                {
                    var session = ReadSession(path, errors);
                    if (session is not null)
                    {
                        sessions.Add(session);
                    }
                }
            }

            candidates.AddRange(ReadLedger(candidateLedgerPath!, errors));
            findings.AddRange(ReadFindings(findingsPath!, errors));
        }

        var finalRevision = candidates.Count == 0 ? null : candidates[^1].Revision;
        var finalSessions = sessions.Where(session => session.CandidateRevision == finalRevision).ToArray();
        var observedCohorts = finalSessions.Select(session => session.CohortId).ToHashSet(StringComparer.Ordinal);
        var observedPlatforms = finalSessions.Select(session => session.ArtifactPlatform).ToHashSet(StringComparer.Ordinal);
        var observedDevices = finalSessions.SelectMany(session => session.InputDevices).ToHashSet(StringComparer.Ordinal);
        var observedProfiles = finalSessions.SelectMany(session => session.Profiles).ToHashSet(StringComparer.Ordinal);
        var crashCount = sessions.Count(session => session.CrashObserved);
        if (executionRequested && candidates.Count > 0)
        {
            CrossCheck(sessions, candidates, findings, observedCohorts, observedPlatforms, observedDevices, errors);
        }

        var complete = executionRequested && finalSessions.Length > 0 && errors.Count == 0;
        var passed = executionRequested
            ? errors.Count == 0
            : errors.Count == 0 && contractIsObject;
        var json = Render(
            passed,
            protocolQualified,
            contractLoad.Bytes,
            prerequisites,
            candidates.Count,
            sessions.Count,
            finalSessions.Length,
            findings.Count,
            crashCount,
            observedCohorts,
            observedPlatforms,
            observedDevices,
            observedProfiles,
            complete,
            errors);
        if (outputPath is not null)
        {
            try
            {
                var protectedPaths = new List<string> { contractLocation };
                protectedPaths.AddRange(PrerequisitePaths.Select(relative =>
                    Path.Combine(repositoryRoot, relative.Replace('/', Path.DirectorySeparatorChar))));
                if (candidateLedgerPath is not null)
                {
                    protectedPaths.Add(candidateLedgerPath);
                }

                if (findingsPath is not null)
                {
                    protectedPaths.Add(findingsPath);
                }

                protectedPaths.AddRange(sessionPaths);
                WriteAtomic(outputPath, json, protectedPaths);
            }
            catch (Exception exception) when (IsExpected(exception))
            {
                errors.Add(StrictJsonFile.SingleLine(exception.Message));
                passed = false;
            }
        }

        var message = passed
            ? executionRequested
                ? "External validation accepted " + sessions.Count.ToString(CultureInfo.InvariantCulture) + " retained sessions."
                : PendingMessage
            : string.Empty;
        return new HandoffEvaluation(passed, message, errors, json);
    }

    private static void ValidateContract(JsonElement contract, List<string> errors)
    {
        ExactInteger(contract.GetProperty("schemaVersion"), 1, "contract.schemaVersion", errors);
        ExactString(contract.GetProperty("kind"), "vibesnake-external-validation-v1", "contract.kind", errors);
        ExactString(contract.GetProperty("status"), "qualified-handoff-execution-pending", "contract.status", errors);
        ExactString(contract.GetProperty("participantIdPattern"), ParticipantPatternText, "contract participant pattern", errors);
        ExactStrings(contract.GetProperty("artifactPlatforms"), ArtifactPlatforms, "contract artifact platforms", errors);
        if (!CohortsMatch(contract.GetProperty("cohorts")))
        {
            errors.Add("contract cohorts must be " + CohortLiteral() + "; got " + StrictJsonFile.Format(contract.GetProperty("cohorts")));
        }

        ExactStrings(contract.GetProperty("inputDevices"), InputDevices, "contract.inputDevices", errors);
        ExactStrings(contract.GetProperty("accessibilityProfiles"), AccessibilityProfiles, "contract.accessibilityProfiles", errors);
        ExactStrings(contract.GetProperty("comprehensionChecks"), ComprehensionChecks, "contract.comprehensionChecks", errors);
        ExactStrings(contract.GetProperty("reportFamilies"), ReportFamilies, "contract.reportFamilies", errors);
        ExactStrings(contract.GetProperty("severityValues"), SeverityValues, "contract.severityValues", errors);
        ExactStrings(contract.GetProperty("findingDecisions"), FindingDecisions, "contract.findingDecisions", errors);
        ExactStrings(contract.GetProperty("resolutionValues"), ResolutionValues, "contract.resolutionValues", errors);
        ExactStrings(contract.GetProperty("requiredSessionFields"), SessionFields, "contract.requiredSessionFields", errors);
        ExactStrings(contract.GetProperty("requiredComprehensionResultFields"), ComprehensionResultFields, "contract.requiredComprehensionResultFields", errors);
        ExactStrings(contract.GetProperty("comprehensionResultValues"), ComprehensionResultValues, "contract.comprehensionResultValues", errors);
        ExactStrings(contract.GetProperty("requiredCandidateFields"), CandidateFields, "contract.requiredCandidateFields", errors);
        ExactStrings(contract.GetProperty("requiredFindingFields"), FindingFields, "contract.requiredFindingFields", errors);
        ExactStrings(contract.GetProperty("prerequisitePaths"), PrerequisitePaths, "contract.prerequisitePaths", errors);
        ExactStrings(contract.GetProperty("privacyRules"), PrivacyRules, "contract.privacyRules", errors);
        ExactStrings(contract.GetProperty("releaseRules"), ReleaseRules, "contract.releaseRules", errors);
    }

    private static List<CandidateRecord> ReadLedger(string path, List<string> errors)
    {
        var loaded = StrictJsonFile.Load(path, "candidate ledger", errors);
        using var document = loaded.Document;
        if (document is null || !StrictKeys(document.RootElement, LedgerFields, "candidate ledger", errors))
        {
            if (document is null)
            {
                errors.Add("candidate ledger must be an object");
            }

            return [];
        }

        var ledger = document.RootElement;
        ExactInteger(ledger.GetProperty("schemaVersion"), 1, "candidate ledger.schemaVersion", errors);
        ExactString(ledger.GetProperty("kind"), "vibesnake-external-candidate-ledger-v1", "candidate ledger.kind", errors);
        var rows = ledger.GetProperty("candidates");
        if (rows.ValueKind != JsonValueKind.Array || rows.GetArrayLength() == 0)
        {
            errors.Add("candidate ledger.candidates must be a nonempty array");
            return [];
        }

        var validated = new List<CandidateRecord>();
        var revisions = new HashSet<string>(StringComparer.Ordinal);
        string? priorRevision = null;
        string? priorStarted = null;
        var index = 0;
        foreach (var candidate in rows.EnumerateArray())
        {
            var before = errors.Count;
            var label = $"candidate ledger.candidates[{index.ToString(CultureInfo.InvariantCulture)}]";
            index++;
            if (!StrictKeys(candidate, CandidateFields, label, errors))
            {
                continue;
            }

            var revisionElement = candidate.GetProperty("revision");
            var revision = revisionElement.ValueKind == JsonValueKind.String ? revisionElement.GetString() ?? string.Empty : string.Empty;
            if (!RevisionPattern.IsMatch(revision))
            {
                errors.Add($"{label}.revision must be a lowercase 40-character revision");
            }

            if (!revisions.Add(revision))
            {
                errors.Add($"duplicate candidate revision: {revision}");
            }

            ExactBool(candidate.GetProperty("sourceTreeClean"), true, $"{label}.sourceTreeClean", errors);
            var startedElement = candidate.GetProperty("startedUtc");
            var started = startedElement.ValueKind == JsonValueKind.String ? startedElement.GetString() ?? string.Empty : StrictJsonFile.Format(startedElement);
            if (!UtcPattern.IsMatch(startedElement.ValueKind == JsonValueKind.String ? started : string.Empty))
            {
                errors.Add($"{label}.startedUtc must use YYYY-MM-DDTHH:MM:SSZ");
            }
            else if (priorStarted is not null && string.CompareOrdinal(started, priorStarted) <= 0)
            {
                errors.Add($"{label}.startedUtc must be later than the previous candidate");
            }

            var hashes = candidate.GetProperty("artifactSha256ByPlatform");
            Dictionary<string, string>? artifactHashes = null;
            if (!StrictKeys(hashes, ArtifactPlatforms, $"{label}.artifactSha256ByPlatform", errors))
            {
                artifactHashes = null;
            }
            else if (!ArtifactPlatforms.All(platform => Sha256Pattern.IsMatch(StringField(hashes, platform) ?? string.Empty)))
            {
                errors.Add($"{label}.artifactSha256ByPlatform must contain SHA-256 digests");
            }
            else
            {
                artifactHashes = ArtifactPlatforms.ToDictionary(platform => platform, platform => StringField(hashes, platform)!, StringComparer.Ordinal);
            }

            var triggers = candidate.GetProperty("triggerFindingIds");
            var gates = candidate.GetProperty("affectedGateIds");
            var reruns = candidate.GetProperty("gateRerunEvidencePaths");
            string[] triggerIds = [];
            string[] gateIds = [];
            if (index == 1)
            {
                ExactNull(candidate.GetProperty("supersedesRevision"), $"{label}.supersedesRevision", errors);
                ExactEmptyArray(triggers, $"{label}.triggerFindingIds", errors);
                ExactEmptyArray(gates, $"{label}.affectedGateIds", errors);
                ExactEmptyObject(reruns, $"{label}.gateRerunEvidencePaths", errors);
            }
            else
            {
                ExactString(candidate.GetProperty("supersedesRevision"), priorRevision ?? string.Empty, $"{label}.supersedesRevision", errors);
                if (!UniqueFindingIds(triggers, out triggerIds))
                {
                    errors.Add($"{label}.triggerFindingIds must contain unique finding IDs");
                }

                if (!UniqueNonempty(gates, $"{label}.affectedGateIds", errors, out gateIds))
                {
                    errors.Add($"{label}.affectedGateIds must contain unique gate IDs");
                }

                if (gates.ValueKind == JsonValueKind.Array
                    && gates.EnumerateArray().All(item => item.ValueKind == JsonValueKind.String)
                    && StrictKeys(reruns, gateIds, $"{label}.gateRerunEvidencePaths", errors))
                {
                    foreach (var gateId in gateIds)
                    {
                        ExistingPaths(reruns.GetProperty(gateId), Path.GetDirectoryName(path) ?? string.Empty, $"{label}.gateRerunEvidencePaths.{gateId}", errors);
                    }
                }
            }

            priorRevision = revision;
            priorStarted = started;
            if (errors.Count == before && artifactHashes is not null)
            {
                validated.Add(new CandidateRecord(revision, artifactHashes, triggerIds, gateIds));
            }
        }

        return validated;
    }

    private static List<FindingRecord> ReadFindings(string path, List<string> errors)
    {
        var loaded = StrictJsonFile.Load(path, "finding review", errors);
        using var document = loaded.Document;
        if (document is null || !StrictKeys(document.RootElement, FindingReviewFields, "finding review", errors))
        {
            if (document is null)
            {
                errors.Add("finding review must be an object");
            }

            return [];
        }

        var review = document.RootElement;
        ExactInteger(review.GetProperty("schemaVersion"), 1, "finding review.schemaVersion", errors);
        ExactString(review.GetProperty("kind"), "vibesnake-external-finding-review-v1", "finding review.kind", errors);
        var rows = review.GetProperty("findings");
        if (rows.ValueKind != JsonValueKind.Array)
        {
            errors.Add("finding review.findings must be an array");
            return [];
        }

        var validated = new List<FindingRecord>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var index = 0;
        foreach (var finding in rows.EnumerateArray())
        {
            var before = errors.Count;
            var label = $"finding review.findings[{index.ToString(CultureInfo.InvariantCulture)}]";
            index++;
            if (!StrictKeys(finding, FindingFields, label, errors))
            {
                continue;
            }

            var findingId = StringField(finding, "findingId") ?? string.Empty;
            if (!FindingIdPattern.IsMatch(findingId))
            {
                errors.Add($"{label}.findingId must match EXT-[0-9]{{3}}");
            }

            if (!seen.Add(findingId))
            {
                errors.Add($"duplicate findingId: {findingId}");
            }

            if (!UniqueSessionIds(finding.GetProperty("sessionIds"), out var sessionIds))
            {
                errors.Add($"{label}.sessionIds must contain unique external session IDs");
            }

            var severity = StringField(finding, "severity") ?? string.Empty;
            var reportFamily = StringField(finding, "reportFamily") ?? string.Empty;
            if (!SeveritySet.Contains(severity))
            {
                errors.Add($"{label}.severity is unsupported");
            }

            if (!ReportFamilySet.Contains(reportFamily))
            {
                errors.Add($"{label}.reportFamily is unsupported");
            }

            if (!UniqueNonempty(finding.GetProperty("affectedGateIds"), $"{label}.affectedGateIds", errors, out var gates))
            {
                errors.Add($"{label}.affectedGateIds must be nonempty");
            }

            var decision = StringField(finding, "decision") ?? string.Empty;
            var resolution = StringField(finding, "resolutionStatus") ?? string.Empty;
            if (!DecisionSet.Contains(decision))
            {
                errors.Add($"{label}.decision is unsupported");
            }

            if (!ResolutionSet.Contains(resolution))
            {
                errors.Add($"{label}.resolutionStatus is unsupported");
            }

            if ((severity is "P0" or "P1") && decision == "ship")
            {
                errors.Add($"{label} cannot ship a P0 or P1 finding");
            }

            if (severity == "P2" && decision is not ("fix" or "ship"))
            {
                errors.Add($"{label} P2 decision must be fix or ship");
            }

            if (severity == "P2" && decision == "ship" && !Nonempty(finding.GetProperty("workaround"), $"{label}.workaround", errors))
            {
                errors.Add($"{label} requires a player-facing workaround");
            }

            string? resolutionRevision = null;
            if (resolution == "closed")
            {
                var revisionElement = finding.GetProperty("resolutionRevision");
                if (decision == "fix" && (revisionElement.ValueKind != JsonValueKind.String || !RevisionPattern.IsMatch(revisionElement.GetString() ?? string.Empty)))
                {
                    errors.Add($"{label}.resolutionRevision must identify the fixed candidate");
                }

                if (revisionElement.ValueKind == JsonValueKind.String)
                {
                    resolutionRevision = revisionElement.GetString();
                }

                ExistingPaths(finding.GetProperty("verificationEvidencePaths"), Path.GetDirectoryName(path) ?? string.Empty, $"{label}.verificationEvidencePaths", errors);
            }
            else
            {
                ExactNull(finding.GetProperty("resolutionRevision"), $"{label}.resolutionRevision", errors);
                ExactEmptyArray(finding.GetProperty("verificationEvidencePaths"), $"{label}.verificationEvidencePaths", errors);
            }

            if (decision != "fix")
            {
                ExactNull(finding.GetProperty("resolutionRevision"), $"{label}.resolutionRevision", errors);
            }

            if (errors.Count == before)
            {
                validated.Add(new FindingRecord(findingId, sessionIds, severity, decision, resolution, resolutionRevision, gates));
            }
        }

        return validated;
    }

    private static SessionRecord? ReadSession(string path, List<string> errors)
    {
        var before = errors.Count;
        var label = $"session {Path.GetFileName(path)}";
        var loaded = StrictJsonFile.Load(path, "external validation session", errors);
        using var document = loaded.Document;
        if (document is null || !StrictKeys(document.RootElement, SessionFields, label, errors))
        {
            if (document is null)
            {
                errors.Add($"{label} must be an object");
            }

            return null;
        }

        var session = document.RootElement;
        ExactInteger(session.GetProperty("schemaVersion"), 1, $"{label}.schemaVersion", errors);
        ExactString(session.GetProperty("kind"), "vibesnake-external-validation-session-v1", $"{label}.kind", errors);
        var sessionId = StringField(session, "sessionId") ?? string.Empty;
        var participantId = StringField(session, "participantId") ?? string.Empty;
        if (!SessionIdPattern.IsMatch(sessionId))
        {
            errors.Add($"{label}.sessionId must match external-session-[0-9]{{3}}");
        }

        if (!ParticipantPattern.IsMatch(participantId))
        {
            errors.Add($"{label}.participantId must match {ParticipantPatternText}");
        }

        var cohortId = StringField(session, "cohortId") ?? string.Empty;
        CohortById.TryGetValue(cohortId, out var cohort);
        var cohortKnown = CohortById.ContainsKey(cohortId);
        if (!cohortKnown)
        {
            errors.Add($"{label}.cohortId is unsupported");
        }

        var revision = StringField(session, "candidateRevision") ?? string.Empty;
        if (!RevisionPattern.IsMatch(revision))
        {
            errors.Add($"{label}.candidateRevision must be a lowercase 40-character revision");
        }

        var platform = StringField(session, "artifactPlatform") ?? string.Empty;
        if (!PlatformSet.Contains(platform))
        {
            errors.Add($"{label}.artifactPlatform is unsupported");
        }

        var artifactSha = StringField(session, "artifactSha256") ?? string.Empty;
        if (!Sha256Pattern.IsMatch(artifactSha))
        {
            errors.Add($"{label}.artifactSha256 must be a SHA-256 digest");
        }

        Nonempty(session.GetProperty("appVersion"), $"{label}.appVersion", errors);
        var clean = session.GetProperty("cleanInstall");
        var unseen = session.GetProperty("neverSeenRepository");
        if (clean.ValueKind is not (JsonValueKind.True or JsonValueKind.False)
            || unseen.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            errors.Add($"{label} cleanInstall and neverSeenRepository must be booleans");
        }

        if (cohortKnown && cohort.CleanInstall)
        {
            ExactBool(clean, true, $"{label}.cleanInstall", errors);
        }

        if (cohortKnown && cohort.NeverSeen)
        {
            ExactBool(unseen, true, $"{label}.neverSeenRepository", errors);
        }

        var devicesElement = session.GetProperty("inputDeviceIds");
        var devicesValid = TryStringSet(devicesElement, InputDeviceSet, requireNonempty: true, out var devices);
        if (!devicesValid)
        {
            errors.Add($"{label}.inputDeviceIds must contain unique supported devices");
        }

        if (cohortId == "clean-install-fresh-keyboard" && !ContainsString(devicesElement, "keyboard"))
        {
            errors.Add($"{label} keyboard cohort must use the keyboard");
        }

        if (cohortId == "clean-install-fresh-controller" && !ContainsAny(devicesElement, "xbox-layout-controller", "playstation-layout-controller"))
        {
            errors.Add($"{label} controller cohort must use a supported controller");
        }

        var profilesElement = session.GetProperty("accessibilityProfileIds");
        if (!TryStringSet(profilesElement, ProfileSet, requireNonempty: true, out var profiles))
        {
            errors.Add($"{label}.accessibilityProfileIds must contain unique supported profiles");
        }

        if (cohortId == "accessibility-focused-fresh" && ProfilesAreOnlyDefault(profilesElement))
        {
            errors.Add($"{label} accessibility cohort must use a non-default profile");
        }

        var executed = StringField(session, "executedUtc") ?? string.Empty;
        if (!UtcPattern.IsMatch(executed))
        {
            errors.Add($"{label}.executedUtc must use YYYY-MM-DDTHH:MM:SSZ");
        }

        Nonempty(session.GetProperty("distributionId"), $"{label}.distributionId", errors);
        ExactBool(session.GetProperty("consentRecordedSeparately"), true, $"{label}.consentRecordedSeparately", errors);
        var reportPaths = session.GetProperty("reportFamilyPaths");
        var parent = Path.GetDirectoryName(path) ?? string.Empty;
        if (StrictKeys(reportPaths, ReportFamilies, $"{label}.reportFamilyPaths", errors))
        {
            foreach (var family in ReportFamilies)
            {
                ExistingPaths(reportPaths.GetProperty(family), parent, $"{label}.reportFamilyPaths.{family}", errors);
            }
        }

        var comprehension = new Dictionary<string, string>(StringComparer.Ordinal);
        var results = session.GetProperty("comprehensionResults");
        if (results.ValueKind != JsonValueKind.Array)
        {
            errors.Add($"{label}.comprehensionResults must be an array");
        }
        else
        {
            var index = 0;
            foreach (var result in results.EnumerateArray())
            {
                var resultLabel = $"{label}.comprehensionResults[{index.ToString(CultureInfo.InvariantCulture)}]";
                index++;
                if (!StrictKeys(result, ComprehensionResultFields, resultLabel, errors))
                {
                    continue;
                }

                var checkId = StringField(result, "checkId") ?? string.Empty;
                if (!ComprehensionSet.Contains(checkId) || comprehension.ContainsKey(checkId))
                {
                    errors.Add($"{resultLabel}.checkId must be unique and supported");
                    continue;
                }

                var resultValue = StringField(result, "result") ?? string.Empty;
                if (!ResultValueSet.Contains(resultValue))
                {
                    errors.Add($"{resultLabel}.result is unsupported");
                    continue;
                }

                comprehension[checkId] = resultValue;
            }
        }

        if (!comprehension.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(ComprehensionSet))
        {
            errors.Add($"{label}.comprehensionResults must cover every required check");
        }

        if (cohortKnown && cohort.Fresh && comprehension.Values.Any(value => value != "pass"))
        {
            errors.Add($"{label} fresh participant must pass every comprehension check");
        }

        if (session.GetProperty("crashObserved").ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            errors.Add($"{label}.crashObserved must be a boolean");
        }

        if (!UniqueFindingIds(session.GetProperty("findingIds"), out var findingIds, allowEmpty: true))
        {
            errors.Add($"{label}.findingIds must contain unique finding IDs");
        }

        ExistingPaths(session.GetProperty("evidencePaths"), parent, $"{label}.evidencePaths", errors);
        if (errors.Count != before)
        {
            return null;
        }

        return new SessionRecord(
            sessionId,
            participantId,
            cohortId,
            revision,
            platform,
            artifactSha,
            StringField(session, "appVersion") ?? string.Empty,
            devices,
            profiles,
            session.GetProperty("crashObserved").ValueKind == JsonValueKind.True,
            findingIds);
    }

    private static void CrossCheck(
        List<SessionRecord> sessions,
        List<CandidateRecord> candidates,
        List<FindingRecord> findings,
        HashSet<string> observedCohorts,
        HashSet<string> observedPlatforms,
        HashSet<string> observedDevices,
        List<string> errors)
    {
        var candidateByRevision = candidates.ToDictionary(candidate => candidate.Revision, StringComparer.Ordinal);
        var candidateIndex = candidates.Select((candidate, index) => (candidate.Revision, index)).ToDictionary(item => item.Revision, item => item.index, StringComparer.Ordinal);
        var sessionIds = sessions.Select(session => session.SessionId).ToArray();
        if (sessionIds.Length != sessionIds.Distinct(StringComparer.Ordinal).Count())
        {
            errors.Add("external validation session IDs must be unique");
        }

        var findingById = findings.ToDictionary(finding => finding.FindingId, StringComparer.Ordinal);
        var sessionById = sessions.ToDictionary(session => session.SessionId, StringComparer.Ordinal);
        var cohortByParticipant = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var session in sessions)
        {
            if (cohortByParticipant.TryGetValue(session.ParticipantId, out var previous) && previous != session.CohortId)
            {
                errors.Add($"participant {session.ParticipantId} cannot represent multiple cohorts");
            }
            else
            {
                cohortByParticipant.TryAdd(session.ParticipantId, session.CohortId);
            }
        }

        foreach (var session in sessions)
        {
            if (!candidateByRevision.TryGetValue(session.CandidateRevision, out var candidate))
            {
                errors.Add($"session {session.SessionId} uses an undeclared candidate revision");
                continue;
            }

            if (!candidate.Artifacts.TryGetValue(session.ArtifactPlatform, out var expected) || session.ArtifactSha256 != expected)
            {
                errors.Add($"session {session.SessionId} artifact hash does not match its candidate ledger");
            }

            foreach (var findingId in session.FindingIds)
            {
                if (!findingById.ContainsKey(findingId))
                {
                    errors.Add($"session {session.SessionId} references unknown finding {findingId}");
                }
            }
        }

        var sessionIdSet = sessionIds.ToHashSet(StringComparer.Ordinal);
        foreach (var finding in findings)
        {
            foreach (var sessionId in finding.SessionIds)
            {
                if (!sessionIdSet.Contains(sessionId))
                {
                    errors.Add($"finding {finding.FindingId} references unknown session {sessionId}");
                }
            }

            if (finding.ResolutionStatus == "closed" && finding.Decision == "fix")
            {
                var resolutionRevision = finding.ResolutionRevision ?? string.Empty;
                if (!candidateByRevision.TryGetValue(resolutionRevision, out var resolutionCandidate))
                {
                    errors.Add($"finding {finding.FindingId} resolution revision is not in the candidate ledger");
                }
                else if (!resolutionCandidate.TriggerFindingIds.Contains(finding.FindingId, StringComparer.Ordinal))
                {
                    errors.Add($"fixed finding {finding.FindingId} is not linked to its replacement candidate");
                }

                if (candidateIndex.TryGetValue(resolutionRevision, out var resolutionIndex))
                {
                    foreach (var sessionId in finding.SessionIds)
                    {
                        if (!sessionById.TryGetValue(sessionId, out var observed))
                        {
                            continue;
                        }

                        if (candidateIndex.TryGetValue(observed.CandidateRevision, out var observedIndex) && observedIndex >= resolutionIndex)
                        {
                            errors.Add($"finding {finding.FindingId} must be observed before its resolution candidate");
                        }
                    }
                }
            }
        }

        foreach (var candidate in candidates.Skip(1))
        {
            foreach (var findingId in candidate.TriggerFindingIds)
            {
                if (!findingById.TryGetValue(findingId, out var finding))
                {
                    errors.Add($"candidate {candidate.Revision} references unknown trigger finding {findingId}");
                }
                else if (finding.Decision != "fix")
                {
                    errors.Add($"candidate {candidate.Revision} trigger {findingId} is not a fix decision");
                }
            }

            var expectedGates = candidate.TriggerFindingIds
                .Where(findingById.ContainsKey)
                .SelectMany(findingId => findingById[findingId].AffectedGateIds)
                .ToHashSet(StringComparer.Ordinal);
            if (!expectedGates.SetEquals(candidate.AffectedGateIds))
            {
                errors.Add($"candidate {candidate.Revision} affected gates do not match its trigger findings");
            }
        }

        var missingCohorts = Cohorts.Select(cohort => cohort.Id).Where(id => !observedCohorts.Contains(id)).Order(StringComparer.Ordinal).ToArray();
        if (missingCohorts.Length > 0)
        {
            errors.Add($"final candidate is missing cohorts: {string.Join(", ", missingCohorts)}");
        }

        var missingPlatforms = ArtifactPlatforms.Where(platform => !observedPlatforms.Contains(platform)).Order(StringComparer.Ordinal).ToArray();
        if (missingPlatforms.Length > 0)
        {
            errors.Add($"final candidate is missing artifact platforms: {string.Join(", ", missingPlatforms)}");
        }

        var missingDevices = InputDevices.Where(device => !observedDevices.Contains(device)).Order(StringComparer.Ordinal).ToArray();
        if (missingDevices.Length > 0)
        {
            errors.Add($"final candidate is missing input devices: {string.Join(", ", missingDevices)}");
        }

        var freshParticipants = new List<string>();
        var finalRevision = candidates[^1].Revision;
        foreach (var session in sessions.Where(session => session.CandidateRevision == finalRevision))
        {
            if (CohortById.TryGetValue(session.CohortId, out var cohort) && cohort.Fresh)
            {
                freshParticipants.Add(session.ParticipantId);
            }
        }

        if (freshParticipants.Count != freshParticipants.Distinct(StringComparer.Ordinal).Count())
        {
            errors.Add("fresh final-candidate sessions must use distinct participants");
        }

        foreach (var revision in candidateByRevision.Keys)
        {
            var versions = sessions
                .Where(session => session.CandidateRevision == revision)
                .Select(session => session.AppVersion)
                .ToHashSet(StringComparer.Ordinal);
            if (versions.Count > 1)
            {
                errors.Add($"candidate {revision} sessions must use one application version");
            }
        }

        var unresolved = findings
            .Where(finding => (finding.Severity is "P0" or "P1" or "P2") && finding.ResolutionStatus != "closed")
            .Select(finding => finding.FindingId)
            .ToArray();
        if (unresolved.Length > 0)
        {
            errors.Add($"external validation has unresolved blocking findings: {string.Join(", ", unresolved)}");
        }
    }

    private static string Render(
        bool passed,
        bool protocolQualified,
        byte[]? contractBytes,
        Dictionary<string, string> prerequisites,
        int candidateCount,
        int sessionCount,
        int finalSessionCount,
        int findingCount,
        int crashCount,
        HashSet<string> cohorts,
        HashSet<string> platforms,
        HashSet<string> devices,
        HashSet<string> profiles,
        bool complete,
        List<string> errors)
    {
        var prerequisiteJson = new JsonObject();
        foreach (var relative in PrerequisitePaths)
        {
            if (prerequisites.TryGetValue(relative, out var digest))
            {
                prerequisiteJson[relative] = digest;
            }
        }

        var root = new JsonObject
        {
            ["schemaVersion"] = 1,
            ["kind"] = "external-validation-handoff-v1",
            ["passed"] = passed,
            ["protocolQualified"] = protocolQualified,
            ["contractSha256"] = contractBytes is null ? null : Convert.ToHexStringLower(SHA256.HashData(contractBytes)),
            ["prerequisiteSha256"] = prerequisiteJson,
            ["artifactPlatformCount"] = ArtifactPlatforms.Length,
            ["cohortCount"] = Cohorts.Length,
            ["comprehensionCheckCount"] = ComprehensionChecks.Length,
            ["reportFamilyCount"] = ReportFamilies.Length,
            ["inputDeviceCount"] = InputDevices.Length,
            ["accessibilityProfileCount"] = AccessibilityProfiles.Length,
            ["requiredSessionFieldCount"] = SessionFields.Length,
            ["requiredCandidateFieldCount"] = CandidateFields.Length,
            ["requiredFindingFieldCount"] = FindingFields.Length,
            ["candidateCount"] = candidateCount,
            ["sessionCount"] = sessionCount,
            ["finalCandidateSessionCount"] = finalSessionCount,
            ["findingCount"] = findingCount,
            ["crashObservedCount"] = crashCount,
            ["observedFinalCandidateCohorts"] = StringArray(cohorts.Order(StringComparer.Ordinal)),
            ["observedFinalCandidatePlatforms"] = StringArray(platforms.Order(StringComparer.Ordinal)),
            ["observedFinalCandidateInputDevices"] = StringArray(devices.Order(StringComparer.Ordinal)),
            ["observedFinalCandidateAccessibilityProfiles"] = StringArray(profiles.Order(StringComparer.Ordinal)),
            ["externalValidationComplete"] = complete,
            ["releaseAcceptance"] = complete,
            ["pendingGates"] = StringArray(complete ? [] : PendingGates),
            ["errors"] = StringArray(errors),
        };
        return root.ToJsonString(RenderOptions).Replace("\r\n", "\n", StringComparison.Ordinal) + "\n";
    }

    private static bool CohortsMatch(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() != Cohorts.Length)
        {
            return false;
        }

        var index = 0;
        foreach (var item in value.EnumerateArray())
        {
            var expected = Cohorts[index++];
            if (item.ValueKind != JsonValueKind.Object
                || item.EnumerateObject().Count() != 4
                || !FieldEquals(item, "id", expected.Id)
                || !BoolEquals(item, "freshParticipantRequired", expected.Fresh)
                || !BoolEquals(item, "cleanInstallRequired", expected.CleanInstall)
                || !BoolEquals(item, "neverSeenRepositoryRequired", expected.NeverSeen))
            {
                return false;
            }
        }

        return true;
    }

    private static string CohortLiteral()
    {
        var rows = Cohorts.Select(cohort =>
            "{'id': " + StrictJsonFile.Quote(cohort.Id)
            + ", 'freshParticipantRequired': " + (cohort.Fresh ? "True" : "False")
            + ", 'cleanInstallRequired': " + (cohort.CleanInstall ? "True" : "False")
            + ", 'neverSeenRepositoryRequired': " + (cohort.NeverSeen ? "True" : "False") + "}");
        return "[" + string.Join(", ", rows) + "]";
    }

    private static bool ExistingPaths(JsonElement value, string parent, string label, List<string> errors)
    {
        if (!TryRelativePaths(value, out var paths))
        {
            errors.Add($"{label} must contain safe relative paths");
            return false;
        }

        var missing = paths.Where(relative => !IsFile(parent, relative)).ToArray();
        if (missing.Length > 0)
        {
            errors.Add($"{label} reference missing retained files: {string.Join(", ", missing)}");
            return false;
        }

        return true;
    }

    private static bool TryRelativePaths(JsonElement value, out List<string> paths)
    {
        paths = [];
        if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() == 0)
        {
            return false;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            var text = item.GetString() ?? string.Empty;
            if (!IsSafeRelative(text) || !seen.Add(text))
            {
                return false;
            }

            paths.Add(text);
        }

        return true;
    }

    private static bool IsFile(string parent, string relative)
    {
        var combined = Path.Combine(parent, relative.Replace('/', Path.DirectorySeparatorChar));
        return File.Exists(combined) && !Directory.Exists(combined);
    }

    private static bool IsSafeRelative(string value)
    {
        if (value.Length == 0 || value.Contains('\\', StringComparison.Ordinal) || value.StartsWith('/'))
        {
            return false;
        }

        if (value.Length >= 2 && char.IsAsciiLetter(value[0]) && value[1] == ':')
        {
            return false;
        }

        return !value.Split('/').Contains("..", StringComparer.Ordinal);
    }

    private static bool TryStringSet(JsonElement value, HashSet<string> allowed, bool requireNonempty, out string[] items)
    {
        items = [];
        if (value.ValueKind != JsonValueKind.Array || (requireNonempty && value.GetArrayLength() == 0))
        {
            return false;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var list = new List<string>();
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            var text = item.GetString() ?? string.Empty;
            if (!allowed.Contains(text) || !seen.Add(text))
            {
                return false;
            }

            list.Add(text);
        }

        items = list.ToArray();
        return true;
    }

    private static bool UniqueFindingIds(JsonElement value, out string[] items, bool allowEmpty = false)
    {
        items = [];
        if (value.ValueKind != JsonValueKind.Array || (!allowEmpty && value.GetArrayLength() == 0))
        {
            return false;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var list = new List<string>();
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String || !FindingIdPattern.IsMatch(item.GetString() ?? string.Empty) || !seen.Add(item.GetString()!))
            {
                return false;
            }

            list.Add(item.GetString()!);
        }

        items = list.ToArray();
        return true;
    }

    private static bool UniqueSessionIds(JsonElement value, out string[] items)
    {
        items = [];
        if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() == 0)
        {
            return false;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var list = new List<string>();
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String || !SessionIdPattern.IsMatch(item.GetString() ?? string.Empty) || !seen.Add(item.GetString()!))
            {
                return false;
            }

            list.Add(item.GetString()!);
        }

        items = list.ToArray();
        return true;
    }

    private static bool UniqueNonempty(JsonElement value, string label, List<string> errors, out string[] items)
    {
        items = [];
        if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() == 0)
        {
            return false;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var list = new List<string>();
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String || !seen.Add(item.GetString() ?? string.Empty))
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(item.GetString()))
            {
                errors.Add($"{label} must be a nonempty string");
                return false;
            }

            list.Add(item.GetString()!);
        }

        items = list.ToArray();
        return true;
    }

    private static bool ContainsString(JsonElement value, string expected) =>
        value.ValueKind == JsonValueKind.Array
        && value.EnumerateArray().Any(item => item.ValueKind == JsonValueKind.String && item.GetString() == expected);

    private static bool ContainsAny(JsonElement value, params string[] expected)
    {
        if (value.ValueKind != JsonValueKind.Array || value.EnumerateArray().Any(item => item.ValueKind != JsonValueKind.String))
        {
            return false;
        }

        var actual = value.EnumerateArray().Select(item => item.GetString()).ToHashSet(StringComparer.Ordinal);
        return expected.Any(actual.Contains);
    }

    private static bool ProfilesAreOnlyDefault(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Array || value.EnumerateArray().Any(item => item.ValueKind != JsonValueKind.String))
        {
            return true;
        }

        return value.EnumerateArray().All(item => item.GetString() == "default");
    }

    private static bool StrictKeys(JsonElement value, string[] expected, string label, List<string> errors)
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            errors.Add($"{label} must be an object");
            return false;
        }

        var actual = value.EnumerateObject().Select(property => property.Name).ToArray();
        if (!new HashSet<string>(actual, StringComparer.Ordinal).SetEquals(expected))
        {
            errors.Add($"{label} fields must be {StrictJsonFile.FormatFields(expected)}; got {StrictJsonFile.FormatFields(actual)}");
            return false;
        }

        return true;
    }

    private static void ExactStrings(JsonElement value, string[] expected, string label, List<string> errors)
    {
        if (!SameStrings(value, expected))
        {
            errors.Add($"{label} must be {StrictJsonFile.FormatStrings(expected)}; got {StrictJsonFile.Format(value)}");
        }
    }

    private static bool SameStrings(JsonElement value, string[] expected)
    {
        if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() != expected.Length)
        {
            return false;
        }

        var index = 0;
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String || !string.Equals(item.GetString(), expected[index], StringComparison.Ordinal))
            {
                return false;
            }

            index++;
        }

        return true;
    }

    private static void ExactString(JsonElement value, string expected, string label, List<string> errors)
    {
        if (value.ValueKind != JsonValueKind.String || !string.Equals(value.GetString(), expected, StringComparison.Ordinal))
        {
            errors.Add($"{label} must be {StrictJsonFile.Quote(expected)}; got {StrictJsonFile.Format(value)}");
        }
    }

    private static void ExactBool(JsonElement value, bool expected, string label, List<string> errors)
    {
        var matches = expected ? value.ValueKind == JsonValueKind.True : value.ValueKind == JsonValueKind.False;
        if (!matches)
        {
            errors.Add($"{label} must be {(expected ? "True" : "False")}; got {StrictJsonFile.Format(value)}");
        }
    }

    private static void ExactInteger(JsonElement value, int expected, string label, List<string> errors)
    {
        if (value.ValueKind != JsonValueKind.Number || value.GetRawText() != expected.ToString(CultureInfo.InvariantCulture))
        {
            errors.Add($"{label} must be {expected.ToString(CultureInfo.InvariantCulture)}; got {StrictJsonFile.Format(value)}");
        }
    }

    private static void ExactNull(JsonElement value, string label, List<string> errors)
    {
        if (value.ValueKind != JsonValueKind.Null)
        {
            errors.Add($"{label} must be None; got {StrictJsonFile.Format(value)}");
        }
    }

    private static void ExactEmptyArray(JsonElement value, string label, List<string> errors)
    {
        if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() != 0)
        {
            errors.Add($"{label} must be []; got {StrictJsonFile.Format(value)}");
        }
    }

    private static void ExactEmptyObject(JsonElement value, string label, List<string> errors)
    {
        if (value.ValueKind != JsonValueKind.Object || value.EnumerateObject().Any())
        {
            errors.Add($"{label} must be {{}}; got {StrictJsonFile.Format(value)}");
        }
    }

    private static bool Nonempty(JsonElement value, string label, List<string> errors)
    {
        if (value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString()))
        {
            return true;
        }

        errors.Add($"{label} must be a nonempty string");
        return false;
    }

    private static bool FieldEquals(JsonElement value, string name, string expected) =>
        value.TryGetProperty(name, out var property)
        && property.ValueKind == JsonValueKind.String
        && string.Equals(property.GetString(), expected, StringComparison.Ordinal);

    private static bool BoolEquals(JsonElement value, string name, bool expected) =>
        value.TryGetProperty(name, out var property)
        && (expected ? property.ValueKind == JsonValueKind.True : property.ValueKind == JsonValueKind.False);

    private static string? StringField(JsonElement value, string name) =>
        value.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static JsonArray StringArray(IEnumerable<string> values)
    {
        var array = new JsonArray();
        foreach (var value in values)
        {
            array.Add(value);
        }

        return array;
    }

    private static void WriteAtomic(string outputPath, string json, IReadOnlyList<string> protectedPaths)
    {
        var path = Path.GetFullPath(outputPath);
        foreach (var protectedPath in protectedPaths)
        {
            if (string.Equals(Path.GetFullPath(protectedPath), path, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("external validation evidence output cannot alias a qualification input");
            }
        }

        var parent = Path.GetDirectoryName(path)
            ?? throw new InvalidDataException("external validation evidence output has no parent directory");
        if (File.Exists(parent))
        {
            throw new InvalidDataException("external validation evidence output parent is not a directory");
        }

        Directory.CreateDirectory(parent);
        if (Path.Exists(path) && (File.GetAttributes(path) & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
        {
            throw new InvalidDataException("external validation evidence output must be a regular file");
        }

        var bytes = StrictUtf8.GetBytes(json);
        var temporary = Path.Combine(parent, "." + Path.GetFileName(path) + "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, FileOptions.WriteThrough))
            {
                output.Write(bytes);
                output.Flush(flushToDisk: true);
            }

            File.Move(temporary, path, overwrite: true);
            if (!File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes))
            {
                throw new InvalidDataException("external validation evidence write verification failed");
            }
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    private static RepositoryCheckResult ToResult(HandoffEvaluation evaluation) =>
        new("External validation", evaluation.Passed, evaluation.Message, evaluation.Passed ? [] : evaluation.Errors);

    private static bool IsExpected(Exception exception) =>
        exception is ArgumentException
            or IOException
            or UnauthorizedAccessException
            or InvalidDataException
            or JsonException
            or NotSupportedException
            or PathTooLongException
            or DecoderFallbackException;

    internal readonly record struct HandoffEvaluation(bool Passed, string Message, IReadOnlyList<string> Errors, string Json);

    private readonly record struct Cohort(string Id, bool Fresh, bool CleanInstall, bool NeverSeen);

    private sealed record CandidateRecord(string Revision, Dictionary<string, string> Artifacts, string[] TriggerFindingIds, string[] AffectedGateIds);

    private sealed record FindingRecord(
        string FindingId,
        string[] SessionIds,
        string Severity,
        string Decision,
        string ResolutionStatus,
        string? ResolutionRevision,
        string[] AffectedGateIds);

    private sealed record SessionRecord(
        string SessionId,
        string ParticipantId,
        string CohortId,
        string CandidateRevision,
        string ArtifactPlatform,
        string ArtifactSha256,
        string AppVersion,
        string[] InputDevices,
        string[] Profiles,
        bool CrashObserved,
        string[] FindingIds);
}
