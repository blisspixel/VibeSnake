using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace RepositoryChecks;

public static class CandidateFreezeCheck
{
    private const string PolicyRelativePath = "config/candidate_freeze_policy_v1.json";
    private const string DefaultBaselineRelativePath = "config/candidate_freeze_baseline_v1.json";

    private static readonly string[] ExpectedContractIds =
    [
        "rules",
        "save-schemas",
        "replay-schema",
        "content-manifests",
        "input-defaults",
        "accessibility-defaults",
    ];

    private static readonly string[] ExpectedPrerequisites =
    [
        "0.8.0-acceptance",
        "clean-revision",
        "green-ci",
        "release-matrix-ready",
    ];

    private static readonly string[] ExpectedChangeKinds =
    [
        "defect",
        "compatibility",
        "performance",
        "documentation",
        "release-operation",
    ];

    private static readonly string[] ExpectedChangeEvidence =
    [
        "changeKind",
        "failedGate",
        "severity",
        "reproduction",
        "verification",
        "affectedFrozenContracts",
        "risk",
        "rollback",
    ];

    private static readonly KeyValuePair<string, string>[] ExpectedSeverityEffects =
    [
        new("P0", "always-blocks"),
        new("P1", "always-blocks"),
        new("P2", "decision-required"),
        new("P3", "known-issue-eligible"),
    ];

    private static readonly HashSet<string> GeneratedSurfaceParts = new(
        [".godot", "bin", "obj"],
        StringComparer.OrdinalIgnoreCase);

    private static readonly Regex RevisionPattern = new(
        "^[0-9a-f]{40}$",
        RegexOptions.CultureInvariant);

    private static readonly Regex Sha256Pattern = new(
        "^[0-9a-f]{64}$",
        RegexOptions.CultureInvariant);

    private static readonly Regex UtcPattern = new(
        "^[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}Z$",
        RegexOptions.CultureInvariant);

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
    };

    public static RepositoryCheckResult Inspect(string repositoryRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        var root = Path.GetFullPath(repositoryRoot);
        if (!Directory.Exists(root))
        {
            return Failed(["repository root does not exist"]);
        }

        var failures = new List<string>();
        var policy = ReadJson<FreezePolicy>(
            Path.Combine(root, PolicyRelativePath.Replace('/', Path.DirectorySeparatorChar)),
            "candidate freeze policy",
            failures);
        if (policy is null)
        {
            return Failed(failures);
        }

        var surfaces = ValidatePolicy(root, policy, failures);
        if (failures.Count == 0 && policy.State == "frozen")
        {
            ValidateBaseline(root, policy, surfaces, failures);
        }

        return failures.Count == 0
            ? new RepositoryCheckResult(
                "Candidate freeze policy",
                true,
                $"Candidate freeze policy check passed for {surfaces.Count} frozen-surface files ({policy.State}).",
                [])
            : Failed(failures);
    }

    public static string BuildBaselineJson(
        string repositoryRoot,
        string revision,
        string generatedUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        ArgumentNullException.ThrowIfNull(revision);
        ArgumentNullException.ThrowIfNull(generatedUtc);
        if (!RevisionPattern.IsMatch(revision))
        {
            throw new InvalidDataException(
                "revision must be a lowercase 40-character Git revision");
        }

        if (!IsValidUtc(generatedUtc))
        {
            throw new InvalidDataException(
                "generated UTC must use a valid YYYY-MM-DDTHH:MM:SSZ timestamp");
        }

        var root = Path.GetFullPath(repositoryRoot);
        var failures = new List<string>();
        var policy = ReadJson<FreezePolicy>(
            Path.Combine(root, PolicyRelativePath.Replace('/', Path.DirectorySeparatorChar)),
            "candidate freeze policy",
            failures);
        if (policy is null)
        {
            throw new InvalidDataException(string.Join("; ", failures));
        }

        var surfaces = ValidatePolicy(root, policy, failures);
        if (policy.State != "pre-freeze")
        {
            failures.Add("a baseline can only be prepared while the policy is pre-freeze");
        }

        if (policy.PrerequisiteGates is null
            || policy.PrerequisiteGates.Any(gate => gate?.State != "passed"))
        {
            failures.Add("every prerequisite gate must pass before preparing a baseline");
        }

        if (failures.Count != 0)
        {
            throw new InvalidDataException(string.Join("; ", failures.Distinct(StringComparer.Ordinal)));
        }

        var files = BuildFileEntries(root, surfaces);
        var baseline = new FreezeBaseline
        {
            SchemaVersion = 1,
            Kind = "candidate-freeze-baseline-v1",
            PolicyId = policy.PolicyId!,
            CandidateVersion = policy.CandidateVersion!,
            CandidateRevision = revision,
            GeneratedUtc = generatedUtc,
            Files = files.Cast<FreezeFile?>().ToList(),
            CombinedSha256 = CombinedDigest(files),
        };
        return JsonSerializer.Serialize(baseline, WriteOptions) + "\n";
    }

    public static int WriteBaseline(
        string repositoryRoot,
        string revision,
        string generatedUtc,
        string? outputPath = null)
    {
        var root = Path.GetFullPath(repositoryRoot);
        var rendered = BuildBaselineJson(root, revision, generatedUtc);
        var destination = outputPath is null
            ? Path.Combine(root, DefaultBaselineRelativePath.Replace('/', Path.DirectorySeparatorChar))
            : Path.GetFullPath(outputPath);
        var destinationDirectory = Path.GetDirectoryName(destination)
            ?? throw new InvalidDataException("baseline output must have a parent directory");
        if (!Directory.Exists(destinationDirectory))
        {
            throw new DirectoryNotFoundException(
                $"baseline output directory does not exist: {destinationDirectory}");
        }

        var temporary = Path.Combine(
            destinationDirectory,
            $".{Path.GetFileName(destination)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temporary, rendered, new UTF8Encoding(false));
            File.Move(temporary, destination, true);
        }
        finally
        {
            File.Delete(temporary);
        }

        return JsonSerializer.Deserialize<FreezeBaseline>(rendered, ReadOptions)!.Files!.Count;
    }

    private static RepositoryCheckResult Failed(IReadOnlyList<string> failures) =>
        new("Candidate freeze policy", false, string.Empty, failures);

    private static SortedDictionary<string, string[]> ValidatePolicy(
        string root,
        FreezePolicy policy,
        List<string> failures)
    {
        if (policy.SchemaVersion != 1)
        {
            failures.Add($"schemaVersion must be 1; got {policy.SchemaVersion}");
        }

        RequireEqual(policy.PolicyId, "candidate-freeze-policy-v1", "policyId", failures);
        RequireEqual(policy.CandidateVersion, "0.9.0", "candidateVersion", failures);
        RequireEqual(policy.PromotionVersion, "1.0.0", "promotionVersion", failures);
        if (policy.State is not ("pre-freeze" or "frozen"))
        {
            failures.Add("state must be 'pre-freeze' or 'frozen'");
        }

        ValidatePrerequisites(policy.PrerequisiteGates, failures);
        RequireSequence(
            policy.AllowedChangeKinds,
            ExpectedChangeKinds,
            "allowedChangeKinds",
            failures);
        RequireSequence(
            policy.RequiredChangeEvidence,
            ExpectedChangeEvidence,
            "requiredChangeEvidence",
            failures);
        ValidateSeverities(policy.SeverityPolicy, failures);
        var surfaces = ResolveSurfaces(root, policy.FrozenContracts, failures);
        ValidateActivation(policy, failures);
        return surfaces;
    }

    private static void ValidatePrerequisites(
        List<FreezeGate?>? gates,
        List<string> failures)
    {
        if (gates is null)
        {
            failures.Add("prerequisiteGates must be an array");
            return;
        }

        var ids = new List<string?>();
        for (var index = 0; index < gates.Count; index++)
        {
            var gate = gates[index];
            if (gate is null)
            {
                failures.Add($"prerequisiteGates[{index}] must be an object");
                continue;
            }

            ids.Add(gate.Id);
            if (gate.State is not ("open" or "passed"))
            {
                failures.Add(
                    $"prerequisiteGates[{index}].state must be 'open' or 'passed'");
            }
        }

        if (!ids.SequenceEqual(ExpectedPrerequisites, StringComparer.Ordinal))
        {
            failures.Add(
                "prerequisite gate IDs must be "
                + string.Join(", ", ExpectedPrerequisites));
        }
    }

    private static void ValidateSeverities(
        List<FreezeSeverity?>? severities,
        List<string> failures)
    {
        if (severities is null)
        {
            failures.Add("severityPolicy must be an array");
            return;
        }

        var actual = new List<KeyValuePair<string, string>>();
        for (var index = 0; index < severities.Count; index++)
        {
            var severity = severities[index];
            if (severity?.Id is null || severity.ReleaseEffect is null)
            {
                failures.Add($"severityPolicy[{index}] must contain string id and releaseEffect");
                continue;
            }

            actual.Add(new KeyValuePair<string, string>(severity.Id, severity.ReleaseEffect));
        }

        if (!actual.SequenceEqual(ExpectedSeverityEffects))
        {
            failures.Add("severityPolicy does not match the required P0 through P3 effects");
        }
    }

    private static void ValidateActivation(FreezePolicy policy, List<string> failures)
    {
        if (policy.Activation is null)
        {
            failures.Add("activation must be an object");
            return;
        }

        var activation = policy.Activation;
        if (policy.State == "pre-freeze")
        {
            if (activation.CandidateRevision is not null
                || activation.ActivatedUtc is not null
                || activation.BaselineManifest is not null
                || activation.BaselineSha256 is not null)
            {
                failures.Add("pre-freeze activation fields must all be null");
            }

            return;
        }

        if (policy.State != "frozen")
        {
            return;
        }

        if (policy.PrerequisiteGates is null
            || policy.PrerequisiteGates.Count != ExpectedPrerequisites.Length
            || policy.PrerequisiteGates.Any(gate => gate?.State != "passed"))
        {
            failures.Add("every prerequisite gate must pass before the policy is frozen");
        }

        if (activation.CandidateRevision is null
            || !RevisionPattern.IsMatch(activation.CandidateRevision))
        {
            failures.Add("candidateRevision must be a lowercase 40-character Git revision");
        }

        if (activation.ActivatedUtc is null || !IsValidUtc(activation.ActivatedUtc))
        {
            failures.Add("activatedUtc must be a valid second-precision UTC timestamp");
        }

        if (!IsSafePattern(activation.BaselineManifest)
            || !activation.BaselineManifest!.StartsWith("config/", StringComparison.Ordinal))
        {
            failures.Add("baselineManifest must be a safe repository-relative config path");
        }

        if (activation.BaselineSha256 is null
            || !Sha256Pattern.IsMatch(activation.BaselineSha256))
        {
            failures.Add("baselineSha256 must be a lowercase SHA-256 digest");
        }
    }

    private static SortedDictionary<string, string[]> ResolveSurfaces(
        string root,
        List<FreezeContract?>? contracts,
        List<string> failures)
    {
        var owners = new SortedDictionary<string, HashSet<string>>(StringComparer.Ordinal);
        if (contracts is null)
        {
            failures.Add("frozenContracts must be an array");
            return new SortedDictionary<string, string[]>(StringComparer.Ordinal);
        }

        var contractIds = new List<string?>();
        for (var index = 0; index < contracts.Count; index++)
        {
            var contract = contracts[index];
            if (contract is null)
            {
                failures.Add($"frozenContracts[{index}] must be an object");
                continue;
            }

            contractIds.Add(contract.Id);
            if (contract.Id is null)
            {
                failures.Add($"frozenContracts[{index}].id must be a string");
                continue;
            }

            if (contract.PathPatterns is null || contract.PathPatterns.Count == 0)
            {
                failures.Add($"frozenContracts[{index}].pathPatterns must be a nonempty array");
                continue;
            }

            var matched = new HashSet<string>(StringComparer.Ordinal);
            foreach (var pattern in contract.PathPatterns)
            {
                if (!IsSafePattern(pattern))
                {
                    failures.Add(
                        $"frozenContracts[{index}] contains an unsafe path pattern: {pattern ?? "null"}");
                    continue;
                }

                string[] matches;
                try
                {
                    matches = GlobFiles(root, pattern!);
                }
                catch (Exception exception) when (
                    exception is IOException
                        or UnauthorizedAccessException
                        or DirectoryNotFoundException)
                {
                    failures.Add(
                        $"frozenContracts[{index}] could not enumerate path pattern {pattern}: "
                        + SingleLine(exception.Message));
                    continue;
                }

                if (matches.Length == 0)
                {
                    failures.Add(
                        $"frozenContracts[{index}] path pattern matched no files: {pattern}");
                }

                foreach (var relative in matches)
                {
                    matched.Add(relative);
                    if (!owners.TryGetValue(relative, out var contractOwners))
                    {
                        contractOwners = new HashSet<string>(StringComparer.Ordinal);
                        owners.Add(relative, contractOwners);
                    }

                    contractOwners.Add(contract.Id);
                }
            }

            if (matched.Count == 0)
            {
                failures.Add($"frozenContracts[{index}] resolved to no files");
            }
        }

        if (!contractIds.SequenceEqual(ExpectedContractIds, StringComparer.Ordinal))
        {
            failures.Add(
                "frozenContracts IDs must be " + string.Join(", ", ExpectedContractIds));
        }

        return new SortedDictionary<string, string[]>(
            owners.ToDictionary(
                pair => pair.Key,
                pair => pair.Value.Order(StringComparer.Ordinal).ToArray(),
                StringComparer.Ordinal),
            StringComparer.Ordinal);
    }

    private static string[] GlobFiles(string root, string pattern)
    {
        var matcher = CompileGlob(pattern);
        var wildcard = pattern.IndexOfAny(['*', '?']);
        if (wildcard < 0)
        {
            var exact = Path.Combine(root, pattern.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(exact))
            {
                return [];
            }

            var relative = RelativePath(root, exact);
            return HasGeneratedPart(relative) ? [] : [relative];
        }

        var slash = pattern.LastIndexOf('/', wildcard);
        var prefix = slash < 0 ? string.Empty : pattern[..slash];
        var searchRoot = prefix.Length == 0
            ? root
            : Path.Combine(root, prefix.Replace('/', Path.DirectorySeparatorChar));
        if (!Directory.Exists(searchRoot))
        {
            return [];
        }

        return Directory
            .EnumerateFiles(searchRoot, "*", SearchOption.AllDirectories)
            .Select(path => RelativePath(root, path))
            .Where(path => !HasGeneratedPart(path) && matcher.IsMatch(path))
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    private static Regex CompileGlob(string pattern)
    {
        var expression = new StringBuilder("^");
        for (var index = 0; index < pattern.Length; index++)
        {
            var character = pattern[index];
            if (character == '*')
            {
                if (index + 1 < pattern.Length && pattern[index + 1] == '*')
                {
                    index++;
                    if (index + 1 < pattern.Length && pattern[index + 1] == '/')
                    {
                        index++;
                        expression.Append("(?:[^/]+/)*");
                    }
                    else
                    {
                        expression.Append(".*");
                    }
                }
                else
                {
                    expression.Append("[^/]*");
                }
            }
            else if (character == '?')
            {
                expression.Append("[^/]");
            }
            else
            {
                expression.Append(Regex.Escape(character.ToString()));
            }
        }

        expression.Append('$');
        return new Regex(expression.ToString(), RegexOptions.CultureInvariant);
    }

    private static bool HasGeneratedPart(string relativePath) =>
        relativePath.Split('/').Any(GeneratedSurfaceParts.Contains);

    private static bool IsSafePattern(string? pattern)
    {
        if (string.IsNullOrEmpty(pattern)
            || pattern.Contains('\\')
            || pattern[0] == '/')
        {
            return false;
        }

        return !pattern.Split('/').Contains("..", StringComparer.Ordinal);
    }

    private static bool IsValidUtc(string value) =>
        UtcPattern.IsMatch(value)
        && DateTimeOffset.TryParseExact(
            value,
            "yyyy-MM-dd'T'HH:mm:ss'Z'",
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal
                | System.Globalization.DateTimeStyles.AdjustToUniversal,
            out _);

    private static List<FreezeFile> BuildFileEntries(
        string root,
        SortedDictionary<string, string[]> surfaces) =>
        surfaces.Select(pair => new FreezeFile
        {
            Path = pair.Key,
            Sha256 = FileSha256(Path.Combine(
                root,
                pair.Key.Replace('/', Path.DirectorySeparatorChar))),
            ContractIds = pair.Value.Select(value => (string?)value).ToList(),
        }).ToList();

    private static string FileSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    private static string CombinedDigest(IReadOnlyList<FreezeFile> files)
    {
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var file in files)
        {
            var line = $"{file.Path}\0{file.Sha256}\0{string.Join(',', file.ContractIds!)}\n";
            digest.AppendData(Encoding.UTF8.GetBytes(line));
        }

        return Convert.ToHexStringLower(digest.GetHashAndReset());
    }

    private static void ValidateBaseline(
        string root,
        FreezePolicy policy,
        SortedDictionary<string, string[]> surfaces,
        List<string> failures)
    {
        var activation = policy.Activation!;
        var relativeManifest = activation.BaselineManifest!;
        var manifestPath = Path.Combine(
            root,
            relativeManifest.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(manifestPath))
        {
            failures.Add($"baseline manifest is missing: {relativeManifest}");
            return;
        }

        string manifestHash;
        try
        {
            manifestHash = FileSha256(manifestPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            failures.Add("baseline manifest is unreadable: " + SingleLine(exception.Message));
            return;
        }

        if (manifestHash != activation.BaselineSha256)
        {
            failures.Add("baseline manifest SHA-256 does not match the activation record");
            return;
        }

        var baseline = ReadJson<FreezeBaseline>(
            manifestPath,
            "baseline manifest",
            failures);
        if (baseline is null)
        {
            return;
        }

        if (baseline.SchemaVersion != 1)
        {
            failures.Add("baseline manifest schemaVersion must be 1");
        }

        RequireEqual(
            baseline.Kind,
            "candidate-freeze-baseline-v1",
            "baseline manifest kind",
            failures);
        RequireEqual(
            baseline.PolicyId,
            policy.PolicyId,
            "baseline manifest policyId",
            failures);
        RequireEqual(
            baseline.CandidateVersion,
            policy.CandidateVersion,
            "baseline manifest candidateVersion",
            failures);
        RequireEqual(
            baseline.CandidateRevision,
            activation.CandidateRevision,
            "baseline manifest candidateRevision",
            failures);
        RequireEqual(
            baseline.GeneratedUtc,
            activation.ActivatedUtc,
            "baseline manifest generatedUtc",
            failures);

        List<FreezeFile> expectedFiles;
        try
        {
            expectedFiles = BuildFileEntries(root, surfaces);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            failures.Add("frozen contract files are unreadable: " + SingleLine(exception.Message));
            return;
        }

        if (baseline.Files is null || !FileEntriesEqual(baseline.Files, expectedFiles))
        {
            failures.Add("frozen contract files differ from the baseline manifest");
        }

        var expectedCombined = CombinedDigest(expectedFiles);
        if (baseline.CombinedSha256 != expectedCombined)
        {
            failures.Add(
                "baseline combined SHA-256 does not match current frozen contracts");
        }
    }

    private static bool FileEntriesEqual(
        IReadOnlyList<FreezeFile?> actual,
        IReadOnlyList<FreezeFile> expected)
    {
        if (actual.Count != expected.Count)
        {
            return false;
        }

        for (var index = 0; index < actual.Count; index++)
        {
            var left = actual[index];
            var right = expected[index];
            if (left is null
                || left.Path != right.Path
                || left.Sha256 != right.Sha256
                || left.ContractIds is null
                || !left.ContractIds.SequenceEqual(right.ContractIds!, StringComparer.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static T? ReadJson<T>(
        string path,
        string context,
        List<string> failures)
        where T : class
    {
        string source;
        try
        {
            source = new UTF8Encoding(false, true).GetString(File.ReadAllBytes(path));
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or DecoderFallbackException)
        {
            failures.Add($"{context} is unreadable: {SingleLine(exception.Message)}");
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(source);
            if (HasDuplicateProperty(document.RootElement))
            {
                failures.Add($"{context} contains a duplicate object field");
                return null;
            }

            return JsonSerializer.Deserialize<T>(source, ReadOptions);
        }
        catch (JsonException exception)
        {
            failures.Add($"{context} is unreadable: {SingleLine(exception.Message)}");
            return null;
        }
    }

    private static bool HasDuplicateProperty(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name) || HasDuplicateProperty(property.Value))
                {
                    return true;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            return element.EnumerateArray().Any(HasDuplicateProperty);
        }

        return false;
    }

    private static void RequireEqual(
        string? actual,
        string? expected,
        string name,
        List<string> failures)
    {
        if (actual != expected)
        {
            failures.Add($"{name} must be '{expected ?? "null"}'; got '{actual ?? "null"}'");
        }
    }

    private static void RequireSequence(
        List<string?>? actual,
        IReadOnlyList<string> expected,
        string name,
        List<string> failures)
    {
        if (actual is null || !actual.SequenceEqual(expected, StringComparer.Ordinal))
        {
            failures.Add($"{name} must be {string.Join(", ", expected)}");
        }
    }

    private static string RelativePath(string root, string path) =>
        Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');

    private static string SingleLine(string value) =>
        value.Replace('\r', ' ').Replace('\n', ' ').Trim();

    private sealed class FreezePolicy
    {
        [JsonPropertyName("schemaVersion")]
        public required int SchemaVersion { get; init; }

        [JsonPropertyName("policyId")]
        public required string? PolicyId { get; init; }

        [JsonPropertyName("candidateVersion")]
        public required string? CandidateVersion { get; init; }

        [JsonPropertyName("promotionVersion")]
        public required string? PromotionVersion { get; init; }

        [JsonPropertyName("state")]
        public required string? State { get; init; }

        [JsonPropertyName("activation")]
        public required FreezeActivation? Activation { get; init; }

        [JsonPropertyName("prerequisiteGates")]
        public required List<FreezeGate?>? PrerequisiteGates { get; init; }

        [JsonPropertyName("frozenContracts")]
        public required List<FreezeContract?>? FrozenContracts { get; init; }

        [JsonPropertyName("allowedChangeKinds")]
        public required List<string?>? AllowedChangeKinds { get; init; }

        [JsonPropertyName("requiredChangeEvidence")]
        public required List<string?>? RequiredChangeEvidence { get; init; }

        [JsonPropertyName("severityPolicy")]
        public required List<FreezeSeverity?>? SeverityPolicy { get; init; }
    }

    private sealed class FreezeActivation
    {
        [JsonPropertyName("candidateRevision")]
        public required string? CandidateRevision { get; init; }

        [JsonPropertyName("activatedUtc")]
        public required string? ActivatedUtc { get; init; }

        [JsonPropertyName("baselineManifest")]
        public required string? BaselineManifest { get; init; }

        [JsonPropertyName("baselineSha256")]
        public required string? BaselineSha256 { get; init; }
    }

    private sealed class FreezeGate
    {
        [JsonPropertyName("id")]
        public required string? Id { get; init; }

        [JsonPropertyName("state")]
        public required string? State { get; init; }
    }

    private sealed class FreezeContract
    {
        [JsonPropertyName("id")]
        public required string? Id { get; init; }

        [JsonPropertyName("pathPatterns")]
        public required List<string?>? PathPatterns { get; init; }
    }

    private sealed class FreezeSeverity
    {
        [JsonPropertyName("id")]
        public required string? Id { get; init; }

        [JsonPropertyName("releaseEffect")]
        public required string? ReleaseEffect { get; init; }
    }

    private sealed class FreezeBaseline
    {
        [JsonPropertyName("schemaVersion")]
        public required int SchemaVersion { get; init; }

        [JsonPropertyName("kind")]
        public required string? Kind { get; init; }

        [JsonPropertyName("policyId")]
        public required string? PolicyId { get; init; }

        [JsonPropertyName("candidateVersion")]
        public required string? CandidateVersion { get; init; }

        [JsonPropertyName("candidateRevision")]
        public required string? CandidateRevision { get; init; }

        [JsonPropertyName("generatedUtc")]
        public required string? GeneratedUtc { get; init; }

        [JsonPropertyName("files")]
        public required List<FreezeFile?>? Files { get; init; }

        [JsonPropertyName("combinedSha256")]
        public required string? CombinedSha256 { get; init; }
    }

    private sealed class FreezeFile
    {
        [JsonPropertyName("path")]
        public required string? Path { get; init; }

        [JsonPropertyName("sha256")]
        public required string? Sha256 { get; init; }

        [JsonPropertyName("contractIds")]
        public required List<string?>? ContractIds { get; init; }
    }
}
