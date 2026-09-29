using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace RepositoryChecks;

public static class ManualProductMatrixCheck
{
    private const string ContractRelativePath = "config/qa_manual_product_matrix_v2.json";
    private const string PendingMessage =
        "Manual product matrix handoff qualified; retained physical execution remains pending.";

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private static readonly JsonSerializerOptions RenderOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true,
        IndentCharacter = ' ',
        IndentSize = 2,
    };
    private static readonly Regex RevisionPattern = new(
        "^[0-9a-f]{40}$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex Sha256Pattern = new(
        "^[0-9a-f]{64}$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex UtcPattern = new(
        @"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}Z$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex SessionIdPattern = new(
        "^product-matrix-[0-9]{3}$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex ReleaseRunUrlPattern = new(
        @"^https://github\.com/[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+/actions/runs/([1-9][0-9]*)$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly PlatformRow[] PlatformRows =
    [
        new("windows-x64", "windows-x64", "x86_64"),
        new("macos-universal-apple-silicon", "macos-universal", "arm64"),
        new("macos-universal-intel", "macos-universal", "x86_64"),
        new("linux-x64", "linux-x64", "x86_64"),
    ];

    private static readonly string[] RequiredFlows =
    [
        "first-launch",
        "tutorial",
        "classic-mode",
        "vibe-mode",
        "death-self-collision",
        "death-starvation",
        "power-shield",
        "power-phase-shift",
        "power-last-stand",
        "power-slow-mo",
        "power-boost",
        "power-magnet",
        "power-bait",
        "power-gluttony",
        "power-segment-detach",
        "settings-gameplay",
        "settings-controls",
        "settings-audio",
        "settings-display",
        "settings-accessibility",
        "settings-data",
        "achievements",
        "customization",
        "scores",
        "radio",
        "optional-pack-absent",
        "optional-pack-valid",
        "optional-pack-removed",
        "optional-pack-invalid",
        "optional-pack-recovered",
        "ai-channels",
        "replays",
        "reset",
        "recovery",
        "focus-loss",
        "quit",
    ];

    private static readonly InputDevice[] InputDevices =
    [
        new("keyboard", "complete-required-flow-per-platform"),
        new("mouse", "complete-capability-set-per-platform"),
        new("xbox-layout-controller", "complete-required-flow-per-platform"),
        new("playstation-layout-controller", "complete-required-flow-per-platform"),
    ];

    private static readonly string[] CompleteFlowDevices =
    [
        "keyboard",
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

    private static readonly string[] SessionFields =
    [
        "schemaVersion",
        "kind",
        "sessionId",
        "candidateRevision",
        "artifactSha256",
        "appVersion",
        "platformRowId",
        "operatingSystemVersion",
        "hardwareClass",
        "renderer",
        "executedUtc",
        "results",
    ];

    private static readonly string[] ResultFields =
    [
        "flowId",
        "inputDeviceId",
        "inputCapabilityIds",
        "settingsProfileIds",
        "result",
        "evidencePaths",
    ];

    private static readonly string[] ResultValues = ["pass", "fail", "blocked"];

    private static readonly string[] CandidateFields =
    [
        "schemaVersion",
        "kind",
        "releaseRunId",
        "releaseRunUrl",
        "releaseMatrixSha256",
        "candidateRevision",
        "appVersion",
        "buildMode",
        "artifactRows",
        "humanReviewStatus",
        "releaseAcceptance",
        "publicationEligible",
    ];

    private static readonly string[] CandidateArtifactFields =
    [
        "platformRowId",
        "artifactPlatform",
        "architecture",
        "fileName",
        "sha256",
        "bytes",
        "artifactManifestSha256",
    ];

    private static readonly string[] UniversalIdentityFields =
    [
        "artifactPlatform",
        "fileName",
        "sha256",
        "bytes",
        "artifactManifestSha256",
    ];

    private static readonly string[] ReleaseRules =
    [
        "Every retained session must match an exact candidate record projected from independently verified Release matrix evidence.",
        "Every required flow must pass on every platform row using the exact candidate artifact.",
        "Keyboard, Xbox-layout controller, and PlayStation-layout controller must each pass every required flow on every platform row.",
        "Mouse menu targeting, settings navigation, gameplay direction, and Back must each pass on every platform row.",
        "Every required settings profile must appear on at least one passing observation on every platform row.",
        "Only a passing result earns flow, device, capability, or settings-profile coverage.",
        "A failed or blocked required flow prevents release acceptance.",
        "An inaccessible required flow is a P1 defect and prevents release acceptance.",
    ];

    private static readonly string[] ContractFields =
    [
        "schemaVersion",
        "kind",
        "status",
        "requiredFlowDefectSeverity",
        "platformRows",
        "requiredFlows",
        "inputDevices",
        "mouseInputCapabilities",
        "settingsProfiles",
        "requiredCandidateFields",
        "requiredCandidateArtifactFields",
        "requiredSessionFields",
        "requiredResultFields",
        "resultValues",
        "releaseRules",
    ];

    private static readonly string[] PendingGates =
    [
        "retained-windows-x64-full-flow",
        "retained-macos-universal-apple-silicon-full-flow",
        "retained-macos-universal-intel-full-flow",
        "retained-linux-x64-full-flow",
        "physical-input-audio-accessibility-profile-coverage",
    ];

    private static readonly HashSet<string> RequiredFlowSet = new(RequiredFlows, StringComparer.Ordinal);
    private static readonly HashSet<string> InputDeviceSet = new(InputDevices.Select(device => device.Id), StringComparer.Ordinal);
    private static readonly HashSet<string> CompleteFlowDeviceSet = new(CompleteFlowDevices, StringComparer.Ordinal);
    private static readonly HashSet<string> MouseCapabilitySet = new(MouseCapabilities, StringComparer.Ordinal);
    private static readonly HashSet<string> SettingsProfileSet = new(SettingsProfiles, StringComparer.Ordinal);
    private static readonly HashSet<string> ResultValueSet = new(ResultValues, StringComparer.Ordinal);
    private static readonly HashSet<string> PlatformRowSet = new(PlatformRows.Select(row => row.Id), StringComparer.Ordinal);

    public static RepositoryCheckResult Inspect(string repositoryRoot) =>
        ToResult(Evaluate(repositoryRoot, sessionsDirectory: null, candidatePath: null));

    public static RepositoryCheckResult WriteFoundationHandoff(string repositoryRoot, string outputPath) =>
        ToResult(Evaluate(repositoryRoot, sessionsDirectory: null, candidatePath: null, outputPath: outputPath));

    public static RepositoryCheckResult RecordSessions(
        string repositoryRoot,
        string sessionsDirectory,
        string candidatePath,
        string outputPath) =>
        ToResult(Evaluate(repositoryRoot, sessionsDirectory, candidatePath, outputPath: outputPath));

    internal static HandoffEvaluation Evaluate(
        string repositoryRoot,
        string? sessionsDirectory,
        string? candidatePath,
        string? contractPath = null,
        string? outputPath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        var errors = new List<string>();
        var contractLocation = contractPath ?? Path.Combine(repositoryRoot, ContractRelativePath.Replace('/', Path.DirectorySeparatorChar));
        var contractLoad = StrictJsonFile.Load(contractLocation, "manual product matrix contract", errors);
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

        var protocolQualified = errors.Count == 0;

        string? candidateSha = null;
        JsonNode? candidateRevisionNode = null;
        JsonNode? candidateRunNode = null;
        var candidateQualified = false;
        var candidateRows = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        string? candidateRevision = null;
        string? candidateAppVersion = null;
        var candidateIsObject = false;
        using var candidateDocument = candidatePath is null
            ? null
            : LoadCandidate(
                candidatePath,
                errors,
                out candidateSha,
                out candidateQualified,
                out candidateIsObject,
                candidateRows,
                out candidateRevision,
                out candidateAppVersion,
                out candidateRevisionNode,
                out candidateRunNode);
        if (sessionsDirectory is not null && candidatePath is null)
        {
            errors.Add("retained manual sessions require an exact candidate record");
        }

        var sessionPaths = SessionFiles(sessionsDirectory, errors);
        var platformPasses = PlatformRows.ToDictionary(
            row => row.Id,
            _ => new HashSet<string>(StringComparer.Ordinal),
            StringComparer.Ordinal);
        var devicePasses = new Dictionary<(string Platform, string Device), HashSet<string>>();
        var mousePasses = PlatformRows.ToDictionary(
            row => row.Id,
            _ => new HashSet<string>(StringComparer.Ordinal),
            StringComparer.Ordinal);
        var profilePasses = PlatformRows.ToDictionary(
            row => row.Id,
            _ => new HashSet<string>(StringComparer.Ordinal),
            StringComparer.Ordinal);
        foreach (var row in PlatformRows)
        {
            foreach (var device in CompleteFlowDevices)
            {
                devicePasses[(row.Id, device)] = new HashSet<string>(StringComparer.Ordinal);
            }
        }

        var observedDevices = new HashSet<string>(StringComparer.Ordinal);
        var observedProfiles = new HashSet<string>(StringComparer.Ordinal);
        var sessionIds = new HashSet<string>(StringComparer.Ordinal);
        var revisions = new HashSet<string>(StringComparer.Ordinal);
        var artifactHashes = PlatformRows.ToDictionary(
            row => row.Id,
            _ => new HashSet<string>(StringComparer.Ordinal),
            StringComparer.Ordinal);
        var failedOrBlocked = 0;
        foreach (var path in sessionPaths)
        {
            var sessionLoad = StrictJsonFile.Load(path, "manual product matrix session", errors);
            using var sessionDocument = sessionLoad.Document;
            if (sessionDocument is null || sessionDocument.RootElement.ValueKind != JsonValueKind.Object)
            {
                errors.Add($"session {Path.GetFileName(path)} must be an object");
                continue;
            }

            if (!TryReadSession(sessionDocument.RootElement, path, errors, out var session))
            {
                continue;
            }

            if (!sessionIds.Add(session.SessionId))
            {
                errors.Add($"duplicate manual product matrix sessionId: {session.SessionId}");
            }

            revisions.Add(session.Revision);
            if (!platformPasses.ContainsKey(session.PlatformRowId))
            {
                continue;
            }

            artifactHashes[session.PlatformRowId].Add(session.ArtifactSha256);
            if (candidateIsObject && candidateRows.TryGetValue(session.PlatformRowId, out var candidateRow))
            {
                if (!string.Equals(session.Revision, candidateRevision, StringComparison.Ordinal))
                {
                    errors.Add($"session {session.SessionId} revision does not match the exact candidate");
                }

                var expectedSha = StringField(candidateRow, "sha256");
                if (!string.Equals(session.ArtifactSha256, expectedSha, StringComparison.Ordinal))
                {
                    errors.Add($"session {session.SessionId} artifact SHA-256 does not match the exact candidate");
                }

                if (!string.Equals(session.AppVersion, candidateAppVersion, StringComparison.Ordinal))
                {
                    errors.Add($"session {session.SessionId} application version does not match the exact candidate");
                }
            }

            foreach (var (flowId, result) in session.Results)
            {
                observedDevices.Add(result.InputDevice);
                observedProfiles.UnionWith(result.Profiles);
                if (result.Result == "pass")
                {
                    platformPasses[session.PlatformRowId].Add(flowId);
                    profilePasses[session.PlatformRowId].UnionWith(result.Profiles);
                    if (CompleteFlowDeviceSet.Contains(result.InputDevice))
                    {
                        devicePasses[(session.PlatformRowId, result.InputDevice)].Add(flowId);
                    }
                    else if (result.InputDevice == "mouse")
                    {
                        mousePasses[session.PlatformRowId].UnionWith(result.Capabilities);
                    }
                }
                else
                {
                    failedOrBlocked++;
                }
            }
        }

        var manualExecutionComplete = false;
        if (sessionsDirectory is not null)
        {
            foreach (var row in PlatformRows)
            {
                var missingFlows = RequiredFlows.Where(flow => !platformPasses[row.Id].Contains(flow)).Order(StringComparer.Ordinal);
                var missingFlowList = missingFlows.ToArray();
                if (missingFlowList.Length > 0)
                {
                    errors.Add($"{row.Id} is missing passing flows: {string.Join(", ", missingFlowList)}");
                }

                if (artifactHashes[row.Id].Count != 1)
                {
                    errors.Add($"{row.Id} must use exactly one candidate artifact SHA-256");
                }

                foreach (var device in CompleteFlowDevices)
                {
                    var missingDeviceFlows = RequiredFlows
                        .Where(flow => !devicePasses[(row.Id, device)].Contains(flow))
                        .Order(StringComparer.Ordinal)
                        .ToArray();
                    if (missingDeviceFlows.Length > 0)
                    {
                        errors.Add($"{row.Id} {device} is missing passing flows: {string.Join(", ", missingDeviceFlows)}");
                    }
                }

                var missingMouse = MouseCapabilities.Where(item => !mousePasses[row.Id].Contains(item)).Order(StringComparer.Ordinal).ToArray();
                if (missingMouse.Length > 0)
                {
                    errors.Add($"{row.Id} mouse is missing passing capabilities: {string.Join(", ", missingMouse)}");
                }

                var missingProfiles = SettingsProfiles.Where(item => !profilePasses[row.Id].Contains(item)).Order(StringComparer.Ordinal).ToArray();
                if (missingProfiles.Length > 0)
                {
                    errors.Add($"{row.Id} is missing passing settings profiles: {string.Join(", ", missingProfiles)}");
                }
            }

            var apple = artifactHashes["macos-universal-apple-silicon"];
            var intel = artifactHashes["macos-universal-intel"];
            if (apple.Count == 1 && intel.Count == 1 && !apple.SetEquals(intel))
            {
                errors.Add("macOS Apple Silicon and Intel sessions must use the same Universal artifact SHA-256");
            }

            var missingDevices = InputDevices
                .Select(device => device.Id)
                .Where(device => !observedDevices.Contains(device))
                .Order(StringComparer.Ordinal)
                .ToArray();
            if (missingDevices.Length > 0)
            {
                errors.Add($"manual matrix is missing input devices: {string.Join(", ", missingDevices)}");
            }

            if (revisions.Count != 1)
            {
                errors.Add("manual matrix sessions must use one candidate revision");
            }

            if (failedOrBlocked != 0)
            {
                errors.Add("manual matrix contains failed or blocked required flows");
            }

            manualExecutionComplete = errors.Count == 0;
        }

        var passed = sessionsDirectory is not null
            ? errors.Count == 0
            : errors.Count == 0 && contractIsObject;
        var json = Render(
            passed,
            protocolQualified,
            contractLoad.Bytes,
            candidateQualified,
            candidateSha,
            candidateRevisionNode,
            candidateRunNode,
            sessionPaths.Count,
            platformPasses.Values.Sum(set => set.Count),
            devicePasses.Values.Sum(set => set.Count),
            mousePasses.Values.Sum(set => set.Count),
            profilePasses.Values.Sum(set => set.Count),
            observedDevices,
            observedProfiles,
            failedOrBlocked,
            manualExecutionComplete,
            errors);
        if (outputPath is not null)
        {
            try
            {
                var protectedPaths = new List<string> { contractLocation };
                if (candidatePath is not null)
                {
                    protectedPaths.Add(candidatePath);
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
            ? sessionsDirectory is null
                ? PendingMessage
                : "Manual product matrix accepted "
                    + sessionPaths.Count.ToString(CultureInfo.InvariantCulture)
                    + " retained sessions."
            : string.Empty;
        return new HandoffEvaluation(passed, message, errors, json);
    }

    private static JsonDocument? LoadCandidate(
        string candidatePath,
        List<string> errors,
        out string? candidateSha,
        out bool candidateQualified,
        out bool candidateIsObject,
        Dictionary<string, JsonElement> candidateRows,
        out string? candidateRevision,
        out string? candidateAppVersion,
        out JsonNode? candidateRevisionNode,
        out JsonNode? candidateRunNode)
    {
        var before = errors.Count;
        var loaded = StrictJsonFile.Load(candidatePath, "manual product matrix candidate", errors);
        candidateSha = loaded.Bytes is null ? null : Convert.ToHexStringLower(SHA256.HashData(loaded.Bytes));
        candidateRevision = null;
        candidateAppVersion = null;
        candidateRevisionNode = null;
        candidateRunNode = null;
        candidateIsObject = false;
        if (loaded.Document is null)
        {
            errors.Add("candidate must be an object");
            candidateQualified = false;
            return null;
        }

        var root = loaded.Document.RootElement;
        if (StrictKeys(root, CandidateFields, "candidate", errors))
        {
            candidateIsObject = true;
            ValidateCandidate(root, errors);
        }
        else
        {
            candidateIsObject = root.ValueKind == JsonValueKind.Object;
        }

        if (candidateIsObject)
        {
            if (root.TryGetProperty("candidateRevision", out var revision))
            {
                candidateRevisionNode = JsonNode.Parse(revision.GetRawText());
                if (revision.ValueKind == JsonValueKind.String)
                {
                    candidateRevision = revision.GetString();
                }
            }

            if (root.TryGetProperty("releaseRunId", out var runId))
            {
                candidateRunNode = JsonNode.Parse(runId.GetRawText());
            }

            if (root.TryGetProperty("appVersion", out var appVersion) && appVersion.ValueKind == JsonValueKind.String)
            {
                candidateAppVersion = appVersion.GetString();
            }

            if (root.TryGetProperty("artifactRows", out var rows)
                && rows.ValueKind == JsonValueKind.Array
                && rows.EnumerateArray().All(row =>
                    row.ValueKind == JsonValueKind.Object
                    && row.TryGetProperty("platformRowId", out var platform)
                    && platform.ValueKind == JsonValueKind.String))
            {
                foreach (var row in rows.EnumerateArray())
                {
                    candidateRows[row.GetProperty("platformRowId").GetString()!] = row;
                }
            }
        }

        candidateQualified = errors.Count == before && candidateIsObject;
        return loaded.Document;
    }

    private static void ValidateContract(JsonElement contract, List<string> errors)
    {
        ExactInteger(contract.GetProperty("schemaVersion"), 2, "contract.schemaVersion", errors);
        ExactString(contract.GetProperty("kind"), "vibesnake-manual-product-matrix-v2", "contract.kind", errors);
        ExactString(contract.GetProperty("status"), "qualified-handoff-execution-pending", "contract.status", errors);
        ExactString(contract.GetProperty("requiredFlowDefectSeverity"), "P1", "contract severity", errors);
        if (!PlatformRowsMatch(contract.GetProperty("platformRows")))
        {
            errors.Add("contract.platformRows must be " + PlatformRowLiteral() + "; got " + StrictJsonFile.Format(contract.GetProperty("platformRows")));
        }

        ExactStrings(contract.GetProperty("requiredFlows"), RequiredFlows, "contract.requiredFlows", errors);
        if (!InputDevicesMatch(contract.GetProperty("inputDevices")))
        {
            errors.Add("contract.inputDevices must be " + InputDeviceLiteral() + "; got " + StrictJsonFile.Format(contract.GetProperty("inputDevices")));
        }

        ExactStrings(contract.GetProperty("mouseInputCapabilities"), MouseCapabilities, "contract.mouseInputCapabilities", errors);
        ExactStrings(contract.GetProperty("settingsProfiles"), SettingsProfiles, "contract.settingsProfiles", errors);
        ExactStrings(contract.GetProperty("requiredCandidateFields"), CandidateFields, "contract.requiredCandidateFields", errors);
        ExactStrings(contract.GetProperty("requiredCandidateArtifactFields"), CandidateArtifactFields, "contract.requiredCandidateArtifactFields", errors);
        ExactStrings(contract.GetProperty("requiredSessionFields"), SessionFields, "contract.requiredSessionFields", errors);
        ExactStrings(contract.GetProperty("requiredResultFields"), ResultFields, "contract.requiredResultFields", errors);
        ExactStrings(contract.GetProperty("resultValues"), ResultValues, "contract.resultValues", errors);
        ExactStrings(contract.GetProperty("releaseRules"), ReleaseRules, "contract.releaseRules", errors);
    }

    private static void ValidateCandidate(JsonElement candidate, List<string> errors)
    {
        ExactInteger(candidate.GetProperty("schemaVersion"), 1, "candidate.schemaVersion", errors);
        ExactString(candidate.GetProperty("kind"), "vibesnake-manual-product-matrix-candidate-v1", "candidate.kind", errors);
        var runId = candidate.GetProperty("releaseRunId");
        var runIdIsPositive = TryPositiveInteger(runId, out var runIdValue);
        if (!runIdIsPositive)
        {
            errors.Add("candidate.releaseRunId must be a positive integer");
        }

        var runUrl = candidate.GetProperty("releaseRunUrl");
        var urlMatch = runUrl.ValueKind == JsonValueKind.String
            ? ReleaseRunUrlPattern.Match(runUrl.GetString() ?? string.Empty)
            : Match.Empty;
        if (!urlMatch.Success
            || !runIdIsPositive
            || !string.Equals(urlMatch.Groups[1].Value, runIdValue.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal))
        {
            errors.Add("candidate.releaseRunUrl must be a GitHub Actions URL for releaseRunId");
        }

        RequireDigest(candidate.GetProperty("releaseMatrixSha256"), "candidate.releaseMatrixSha256", errors);
        RequireRevision(candidate.GetProperty("candidateRevision"), "candidate.candidateRevision", errors);
        Nonempty(candidate.GetProperty("appVersion"), "candidate.appVersion", errors);
        ExactString(candidate.GetProperty("buildMode"), "Release", "candidate.buildMode", errors);
        ExactString(candidate.GetProperty("humanReviewStatus"), "pending", "candidate.humanReviewStatus", errors);
        ExactBool(candidate.GetProperty("releaseAcceptance"), false, "candidate.releaseAcceptance", errors);
        ExactBool(candidate.GetProperty("publicationEligible"), false, "candidate.publicationEligible", errors);
        var rows = candidate.GetProperty("artifactRows");
        if (rows.ValueKind != JsonValueKind.Array || rows.GetArrayLength() != PlatformRows.Length)
        {
            errors.Add($"candidate.artifactRows must contain exactly {PlatformRows.Length.ToString(CultureInfo.InvariantCulture)} rows");
            return;
        }

        var index = 0;
        foreach (var row in rows.EnumerateArray())
        {
            var label = $"candidate.artifactRows[{index.ToString(CultureInfo.InvariantCulture)}]";
            var expected = PlatformRows[index];
            index++;
            if (!StrictKeys(row, CandidateArtifactFields, label, errors))
            {
                continue;
            }

            ExactString(row.GetProperty("platformRowId"), expected.Id, $"{label}.platformRowId", errors);
            ExactString(row.GetProperty("artifactPlatform"), expected.ArtifactPlatform, $"{label}.artifactPlatform", errors);
            ExactString(row.GetProperty("architecture"), expected.Architecture, $"{label}.architecture", errors);
            var fileName = row.GetProperty("fileName");
            if (fileName.ValueKind != JsonValueKind.String || !IsSafeFileName(fileName.GetString() ?? string.Empty))
            {
                errors.Add($"{label}.fileName must be a safe file name");
            }

            RequireDigest(row.GetProperty("sha256"), $"{label}.sha256", errors);
            RequireDigest(row.GetProperty("artifactManifestSha256"), $"{label}.artifactManifestSha256", errors);
            if (!TryPositiveInteger(row.GetProperty("bytes"), out _))
            {
                errors.Add($"{label}.bytes must be a positive integer");
            }
        }

        var materialized = rows.EnumerateArray().ToArray();
        if (materialized[1].ValueKind == JsonValueKind.Object && materialized[2].ValueKind == JsonValueKind.Object)
        {
            var apple = materialized[1];
            var intel = materialized[2];
            var mismatch = false;
            foreach (var field in UniversalIdentityFields)
            {
                var appleHas = apple.TryGetProperty(field, out var appleValue);
                var intelHas = intel.TryGetProperty(field, out var intelValue);
                if (appleHas == intelHas && (!appleHas || JsonEquals(appleValue, intelValue)))
                {
                    continue;
                }

                mismatch = true;
                break;
            }

            if (mismatch)
            {
                errors.Add("candidate macOS architecture rows must identify one identical Universal artifact");
            }
        }
    }

    private static bool TryReadSession(
        JsonElement session,
        string path,
        List<string> errors,
        out SessionObservation observation)
    {
        observation = default;
        var label = $"session {Path.GetFileName(path)}";
        if (!StrictKeys(session, SessionFields, label, errors))
        {
            return false;
        }

        ExactInteger(session.GetProperty("schemaVersion"), 2, $"{label}.schemaVersion", errors);
        ExactString(session.GetProperty("kind"), "vibesnake-manual-product-matrix-session-v2", $"{label}.kind", errors);
        var sessionIdElement = session.GetProperty("sessionId");
        var revisionElement = session.GetProperty("candidateRevision");
        var artifactElement = session.GetProperty("artifactSha256");
        var platformElement = session.GetProperty("platformRowId");
        var sessionId = sessionIdElement.ValueKind == JsonValueKind.String
            ? sessionIdElement.GetString() ?? string.Empty
            : StrictJsonFile.Format(sessionIdElement);
        var revision = revisionElement.ValueKind == JsonValueKind.String
            ? revisionElement.GetString() ?? string.Empty
            : StrictJsonFile.Format(revisionElement);
        var artifactSha = artifactElement.ValueKind == JsonValueKind.String
            ? artifactElement.GetString() ?? string.Empty
            : StrictJsonFile.Format(artifactElement);
        var platform = platformElement.ValueKind == JsonValueKind.String
            ? platformElement.GetString() ?? string.Empty
            : string.Empty;
        if (!SessionIdPattern.IsMatch(sessionIdElement.ValueKind == JsonValueKind.String ? sessionId : string.Empty))
        {
            errors.Add($"{label}.sessionId must match product-matrix-[0-9]{{3}}");
        }

        if (!RevisionPattern.IsMatch(revisionElement.ValueKind == JsonValueKind.String ? revision : string.Empty))
        {
            errors.Add($"{label}.candidateRevision must be a lowercase 40-character revision");
        }

        if (!Sha256Pattern.IsMatch(artifactElement.ValueKind == JsonValueKind.String ? artifactSha : string.Empty))
        {
            errors.Add($"{label}.artifactSha256 must be a SHA-256 digest");
        }

        if (platformElement.ValueKind != JsonValueKind.String || !PlatformRowSet.Contains(platform))
        {
            errors.Add($"{label}.platformRowId is unsupported: {StrictJsonFile.Format(platformElement)}");
        }

        foreach (var field in new[] { "appVersion", "operatingSystemVersion", "hardwareClass", "renderer" })
        {
            Nonempty(session.GetProperty(field), $"{label}.{field}", errors);
        }

        var executed = session.GetProperty("executedUtc");
        if (executed.ValueKind != JsonValueKind.String || !IsCalendarUtc(executed.GetString() ?? string.Empty))
        {
            errors.Add($"{label}.executedUtc must use YYYY-MM-DDTHH:MM:SSZ");
        }

        var results = new Dictionary<string, FlowObservation>(StringComparer.Ordinal);
        var resultElement = session.GetProperty("results");
        if (resultElement.ValueKind != JsonValueKind.Array || resultElement.GetArrayLength() == 0)
        {
            errors.Add($"{label}.results must be a nonempty array");
        }
        else
        {
            var index = 0;
            foreach (var result in resultElement.EnumerateArray())
            {
                var resultLabel = $"{label}.results[{index.ToString(CultureInfo.InvariantCulture)}]";
                index++;
                if (!StrictKeys(result, ResultFields, resultLabel, errors))
                {
                    continue;
                }

                var flowElement = result.GetProperty("flowId");
                var flowId = flowElement.ValueKind == JsonValueKind.String ? flowElement.GetString() ?? string.Empty : string.Empty;
                if (!RequiredFlowSet.Contains(flowId))
                {
                    errors.Add($"{resultLabel}.flowId is unsupported: {StrictJsonFile.Format(flowElement)}");
                    continue;
                }

                if (results.ContainsKey(flowId))
                {
                    errors.Add($"{label} contains duplicate flow result: {flowId}");
                    continue;
                }

                var valueElement = result.GetProperty("result");
                var value = valueElement.ValueKind == JsonValueKind.String ? valueElement.GetString() ?? string.Empty : string.Empty;
                if (!ResultValueSet.Contains(value))
                {
                    errors.Add($"{resultLabel}.result is unsupported: {StrictJsonFile.Format(valueElement)}");
                    continue;
                }

                var deviceElement = result.GetProperty("inputDeviceId");
                var device = deviceElement.ValueKind == JsonValueKind.String ? deviceElement.GetString() ?? string.Empty : string.Empty;
                if (deviceElement.ValueKind != JsonValueKind.String || !InputDeviceSet.Contains(device))
                {
                    errors.Add($"{resultLabel}.inputDeviceId is unsupported: {StrictJsonFile.Format(deviceElement)}");
                    continue;
                }

                if (!TryUniqueSubset(result.GetProperty("inputCapabilityIds"), MouseCapabilitySet, out var capabilities))
                {
                    errors.Add($"{resultLabel}.inputCapabilityIds must be unique supported capabilities");
                    continue;
                }

                if (device != "mouse" && capabilities.Count > 0)
                {
                    errors.Add($"{resultLabel}.inputCapabilityIds must be empty for {device}");
                    continue;
                }

                if (!TryUniqueSubset(result.GetProperty("settingsProfileIds"), SettingsProfileSet, out var profiles))
                {
                    errors.Add($"{resultLabel}.settingsProfileIds must be unique supported profiles");
                    continue;
                }

                if (!TryEvidence(result.GetProperty("evidencePaths"), path, resultLabel, errors, out _))
                {
                    continue;
                }

                results[flowId] = new FlowObservation(value, device, capabilities, profiles);
            }
        }

        observation = new SessionObservation(
            sessionId,
            revision,
            artifactSha,
            platform,
            StringField(session, "appVersion") ?? string.Empty,
            results);
        return true;
    }

    private static string Render(
        bool passed,
        bool protocolQualified,
        byte[]? contractBytes,
        bool candidateQualified,
        string? candidateSha,
        JsonNode? candidateRevision,
        JsonNode? candidateRun,
        int sessionCount,
        int completedPlatformFlows,
        int completedDeviceFlows,
        int completedMouseCapabilities,
        int completedProfiles,
        HashSet<string> observedDevices,
        HashSet<string> observedProfiles,
        int failedOrBlocked,
        bool manualExecutionComplete,
        List<string> errors)
    {
        var root = new JsonObject
        {
            ["schemaVersion"] = 2,
            ["kind"] = "manual-product-matrix-handoff-v2",
            ["passed"] = passed,
            ["protocolQualified"] = protocolQualified,
            ["contractSha256"] = contractBytes is null ? null : Convert.ToHexStringLower(SHA256.HashData(contractBytes)),
            ["candidateQualified"] = candidateQualified,
            ["candidateSha256"] = candidateSha,
            ["candidateRevision"] = candidateRevision,
            ["candidateReleaseRunId"] = candidateRun,
            ["platformRowCount"] = PlatformRows.Length,
            ["requiredFlowCount"] = RequiredFlows.Length,
            ["requiredPlatformFlowCellCount"] = PlatformRows.Length * RequiredFlows.Length,
            ["requiredDeviceFlowCellCount"] = PlatformRows.Length * CompleteFlowDevices.Length * RequiredFlows.Length,
            ["requiredMouseCapabilityCellCount"] = PlatformRows.Length * MouseCapabilities.Length,
            ["requiredPlatformProfileCellCount"] = PlatformRows.Length * SettingsProfiles.Length,
            ["inputDeviceCount"] = InputDevices.Length,
            ["settingsProfileCount"] = SettingsProfiles.Length,
            ["requiredSessionFieldCount"] = SessionFields.Length,
            ["requiredResultFieldCount"] = ResultFields.Length,
            ["requiredCandidateFieldCount"] = CandidateFields.Length,
            ["requiredCandidateArtifactFieldCount"] = CandidateArtifactFields.Length,
            ["manualSessionCount"] = sessionCount,
            ["completedPlatformFlowCellCount"] = completedPlatformFlows,
            ["completedDeviceFlowCellCount"] = completedDeviceFlows,
            ["completedMouseCapabilityCellCount"] = completedMouseCapabilities,
            ["completedPlatformProfileCellCount"] = completedProfiles,
            ["observedInputDevices"] = StringArray(observedDevices.OrderBy(item => item, StringComparer.Ordinal)),
            ["observedSettingsProfiles"] = StringArray(observedProfiles.OrderBy(item => item, StringComparer.Ordinal)),
            ["failedOrBlockedResultCount"] = failedOrBlocked,
            ["manualExecutionComplete"] = manualExecutionComplete,
            ["releaseAcceptance"] = manualExecutionComplete && errors.Count == 0,
            ["pendingGates"] = StringArray(manualExecutionComplete ? [] : PendingGates),
            ["errors"] = StringArray(errors),
        };
        return root.ToJsonString(RenderOptions).Replace("\r\n", "\n", StringComparison.Ordinal) + "\n";
    }

    private static List<string> SessionFiles(string? sessionsDirectory, List<string> errors)
    {
        var paths = new List<string>();
        if (sessionsDirectory is null)
        {
            return paths;
        }

        if (!Directory.Exists(sessionsDirectory))
        {
            errors.Add($"sessions directory does not exist: {StrictJsonFile.Display(sessionsDirectory)}");
            return paths;
        }

        paths.AddRange(Directory.EnumerateFiles(sessionsDirectory, "*.json", SearchOption.TopDirectoryOnly));
        paths.Sort(StringComparer.Ordinal);
        return paths;
    }

    private static bool TryEvidence(
        JsonElement value,
        string sessionFile,
        string label,
        List<string> errors,
        out List<string> paths)
    {
        paths = [];
        if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() == 0)
        {
            errors.Add($"{label}.evidencePaths must contain safe relative paths");
            return false;
        }

        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String || !IsSafeRelative(item.GetString() ?? string.Empty))
            {
                errors.Add($"{label}.evidencePaths must contain safe relative paths");
                return false;
            }

            paths.Add(item.GetString()!);
        }

        var parent = Path.GetDirectoryName(sessionFile) ?? string.Empty;
        var missing = paths.Where(relative => !IsExistingFile(parent, relative)).ToArray();
        if (missing.Length > 0)
        {
            errors.Add($"{label}.evidencePaths reference missing retained files: {string.Join(", ", missing)}");
            return false;
        }

        return true;
    }

    private static bool IsExistingFile(string parent, string relative)
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

    private static bool IsSafeFileName(string value) =>
        value.Length > 0
        && value is not "." and not ".."
        && !value.Contains('/', StringComparison.Ordinal)
        && !value.Contains('\\', StringComparison.Ordinal);

    private static bool TryUniqueSubset(JsonElement value, HashSet<string> allowed, out HashSet<string> items)
    {
        items = new HashSet<string>(StringComparer.Ordinal);
        if (value.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            var text = item.GetString() ?? string.Empty;
            if (!allowed.Contains(text) || !items.Add(text))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsCalendarUtc(string value)
    {
        if (!UtcPattern.IsMatch(value))
        {
            return false;
        }

        return DateTime.TryParseExact(
            value,
            "yyyy-MM-dd'T'HH:mm:ss'Z'",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out _);
    }

    private static bool PlatformRowsMatch(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() != PlatformRows.Length)
        {
            return false;
        }

        var index = 0;
        foreach (var row in value.EnumerateArray())
        {
            var expected = PlatformRows[index++];
            if (row.ValueKind != JsonValueKind.Object
                || row.EnumerateObject().Count() != 3
                || !FieldEquals(row, "id", expected.Id)
                || !FieldEquals(row, "artifactPlatform", expected.ArtifactPlatform)
                || !FieldEquals(row, "architecture", expected.Architecture))
            {
                return false;
            }
        }

        return true;
    }

    private static bool InputDevicesMatch(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() != InputDevices.Length)
        {
            return false;
        }

        var index = 0;
        foreach (var item in value.EnumerateArray())
        {
            var expected = InputDevices[index++];
            if (item.ValueKind != JsonValueKind.Object
                || item.EnumerateObject().Count() != 2
                || !FieldEquals(item, "id", expected.Id)
                || !FieldEquals(item, "requiredCoverage", expected.Coverage))
            {
                return false;
            }
        }

        return true;
    }

    private static string PlatformRowLiteral()
    {
        var rows = PlatformRows.Select(row =>
            "{'id': " + StrictJsonFile.Quote(row.Id)
            + ", 'artifactPlatform': " + StrictJsonFile.Quote(row.ArtifactPlatform)
            + ", 'architecture': " + StrictJsonFile.Quote(row.Architecture) + "}");
        return "[" + string.Join(", ", rows) + "]";
    }

    private static string InputDeviceLiteral()
    {
        var rows = InputDevices.Select(device =>
            "{'id': " + StrictJsonFile.Quote(device.Id)
            + ", 'requiredCoverage': " + StrictJsonFile.Quote(device.Coverage) + "}");
        return "[" + string.Join(", ", rows) + "]";
    }

    private static bool FieldEquals(JsonElement value, string name, string expected) =>
        value.TryGetProperty(name, out var property)
        && property.ValueKind == JsonValueKind.String
        && string.Equals(property.GetString(), expected, StringComparison.Ordinal);

    private static string? StringField(JsonElement value, string name) =>
        value.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

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
        if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() != expected.Length)
        {
            errors.Add($"{label} must be {StrictJsonFile.FormatStrings(expected)}; got {StrictJsonFile.Format(value)}");
            return;
        }

        var index = 0;
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String
                || !string.Equals(item.GetString(), expected[index], StringComparison.Ordinal))
            {
                errors.Add($"{label} must be {StrictJsonFile.FormatStrings(expected)}; got {StrictJsonFile.Format(value)}");
                return;
            }

            index++;
        }
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
        if (!TryInteger(value, out var actual) || actual != expected)
        {
            errors.Add($"{label} must be {expected.ToString(CultureInfo.InvariantCulture)}; got {StrictJsonFile.Format(value)}");
        }
    }

    private static bool TryPositiveInteger(JsonElement value, out long number)
    {
        if (!TryInteger(value, out number) || number <= 0)
        {
            number = 0;
            return false;
        }

        return true;
    }

    private static bool TryInteger(JsonElement value, out long number)
    {
        number = 0;
        if (value.ValueKind != JsonValueKind.Number)
        {
            return false;
        }

        var raw = value.GetRawText();
        foreach (var character in raw)
        {
            if (character is < '0' or > '9')
            {
                return false;
            }
        }

        return long.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out number);
    }

    private static void RequireDigest(JsonElement value, string label, List<string> errors)
    {
        if (value.ValueKind != JsonValueKind.String || !Sha256Pattern.IsMatch(value.GetString() ?? string.Empty))
        {
            errors.Add($"{label} must be a SHA-256 digest");
        }
    }

    private static void RequireRevision(JsonElement value, string label, List<string> errors)
    {
        if (value.ValueKind != JsonValueKind.String || !RevisionPattern.IsMatch(value.GetString() ?? string.Empty))
        {
            errors.Add($"{label} must be a lowercase 40-character revision");
        }
    }

    private static void Nonempty(JsonElement value, string label, List<string> errors)
    {
        if (value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
        {
            errors.Add($"{label} must be a nonempty string");
        }
    }

    private static bool JsonEquals(JsonElement left, JsonElement right)
    {
        if (left.ValueKind != right.ValueKind)
        {
            return false;
        }

        return left.ValueKind switch
        {
            JsonValueKind.String => string.Equals(left.GetString(), right.GetString(), StringComparison.Ordinal),
            JsonValueKind.Number => string.Equals(left.GetRawText(), right.GetRawText(), StringComparison.Ordinal),
            JsonValueKind.True or JsonValueKind.False or JsonValueKind.Null => true,
            _ => string.Equals(left.GetRawText(), right.GetRawText(), StringComparison.Ordinal),
        };
    }

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
                throw new InvalidDataException("manual product matrix evidence output cannot alias a qualification input");
            }
        }

        var parent = Path.GetDirectoryName(path)
            ?? throw new InvalidDataException("manual product matrix evidence output has no parent directory");
        Directory.CreateDirectory(parent);
        if (Path.Exists(path) && (File.GetAttributes(path) & FileAttributes.Directory) != 0)
        {
            throw new InvalidDataException("manual product matrix evidence output must be a regular file");
        }

        if (Path.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException("manual product matrix evidence output must be a regular file");
        }

        var bytes = StrictUtf8.GetBytes(json);
        var temporary = Path.Combine(parent, "." + Path.GetFileName(path) + "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var output = new FileStream(
                temporary,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                64 * 1024,
                FileOptions.WriteThrough))
            {
                output.Write(bytes);
                output.Flush(flushToDisk: true);
            }

            File.Move(temporary, path, overwrite: true);
            if (!File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes))
            {
                throw new InvalidDataException("manual product matrix evidence write verification failed");
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
        new(
            "Manual product matrix",
            evaluation.Passed,
            evaluation.Message,
            evaluation.Passed ? [] : evaluation.Errors);

    private static bool IsExpected(Exception exception) =>
        exception is ArgumentException
            or IOException
            or UnauthorizedAccessException
            or InvalidDataException
            or JsonException
            or NotSupportedException
            or PathTooLongException
            or DecoderFallbackException;

    internal readonly record struct HandoffEvaluation(
        bool Passed,
        string Message,
        IReadOnlyList<string> Errors,
        string Json);

    private readonly record struct PlatformRow(string Id, string ArtifactPlatform, string Architecture);

    private readonly record struct InputDevice(string Id, string Coverage);

    private readonly record struct FlowObservation(
        string Result,
        string InputDevice,
        HashSet<string> Capabilities,
        HashSet<string> Profiles);

    private readonly record struct SessionObservation(
        string SessionId,
        string Revision,
        string ArtifactSha256,
        string PlatformRowId,
        string AppVersion,
        Dictionary<string, FlowObservation> Results);
}
