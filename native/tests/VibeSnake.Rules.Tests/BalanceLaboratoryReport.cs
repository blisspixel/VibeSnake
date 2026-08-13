using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VibeSnake.Rules.Tests;

internal sealed record BalanceSeedCorpus(
    string Id,
    string Classification,
    bool Reviewed,
    IReadOnlyList<ulong> Seeds,
    IReadOnlyList<string> Sources);

internal sealed record BalanceScenarioEvidence(
    string Id,
    bool Passed,
    string FinalStateHash,
    string Detail);

internal sealed record BalanceDistribution(
    string VariantId,
    string PolicyId,
    int SampleCount,
    int SurvivalMinimum,
    int SurvivalP50,
    int SurvivalP95,
    int SurvivalP99,
    int SurvivalMaximum,
    int ScoreMinimum,
    int ScoreP50,
    int ScoreP95,
    int ScoreP99,
    int ScoreMaximum,
    int FoodMaximum,
    int ComboMaximum,
    int NearMissMaximum,
    int PowerCollectionMaximum,
    int WrapMaximum,
    int RouteCellMaximum,
    IReadOnlyDictionary<string, int> DeathCauses);

internal sealed record BalanceOutlierReplay(
    string Reason,
    string VariantId,
    string PolicyId,
    ulong Seed,
    int Steps,
    int Score,
    string FinalStateHash,
    string RelativePath,
    string Sha256,
    bool Verified);

internal sealed record BalanceFirstDivergence(
    string VariantId,
    string PolicyId,
    ulong Seed,
    int Step,
    string ExpectedStateHash,
    string ActualStateHash,
    IReadOnlyList<IReadOnlyList<Direction>> CommandPrefix);

internal sealed record BalanceDivergenceEvidence(
    int ComparedRunCount,
    int ComparedStepCount,
    bool Passed,
    BalanceFirstDivergence? FirstDivergence,
    string ReproductionCommand);

internal sealed record BalanceRunTrace(
    string VariantId,
    RunConfig Configuration,
    string PolicyId,
    BalancePolicyKind PolicyKind,
    ulong Seed,
    int Steps,
    RunStatus Status,
    DeathCause DeathCause,
    int Score,
    int FoodEaten,
    int MaximumCombo,
    int NearMisses,
    int PowerCollections,
    int Wraps,
    int UniqueRouteCells,
    int DirectionTransitions,
    string FinalStateHash,
    IReadOnlyDictionary<string, int> AdaptiveStateSteps,
    IReadOnlyList<IReadOnlyList<Direction>> Commands);

internal sealed record BalanceLaboratoryEvidence(
    int SchemaVersion,
    string Kind,
    bool Passed,
    string RulesetId,
    int RulesVersion,
    string ConfigHashAlgorithm,
    string StateHashAlgorithm,
    string SeedCorpusKind,
    int SeedCorpusSchemaVersion,
    string SeedCorpusSha256,
    IReadOnlyList<BalanceSeedCorpus> SeedCorpora,
    IReadOnlyList<BalancePolicyDefinition> Policies,
    IReadOnlyList<string> Variants,
    int MaximumStepsPerRun,
    int RunCount,
    int ComparedStepCount,
    IReadOnlyList<BalanceScenarioEvidence> Scenarios,
    IReadOnlyList<BalanceDistribution> Distributions,
    IReadOnlyList<BalanceOutlierReplay> OutlierReplays,
    BalanceDivergenceEvidence Divergence,
    IReadOnlyList<object> RunSummaries,
    IReadOnlyList<string> Notes);

internal static class BalanceLaboratoryReport
{
    public const int SchemaVersion = 1;
    public const string Kind = "balance-laboratory-v1";
    public const string SeedCorpusKind = "vibesnake-qa-seed-corpora-v1";
    public const int SeedCorpusSchemaVersion = 1;

    private const string TestProject =
        "native/tests/VibeSnake.Rules.Tests/VibeSnake.Rules.Tests.csproj";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.KebabCaseLower) },
    };

    public static string ResolveRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "ROADMAP.md"))
                && File.Exists(Path.Combine(directory.FullName, "native", "VibeSnake.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }

    public static (
        IReadOnlyList<BalanceSeedCorpus> Corpora,
        string Sha256) ReadSeedCorpora(string repositoryRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        var path = Path.Combine(repositoryRoot, "config", "qa_seed_corpora.json");
        var bytes = File.ReadAllBytes(path);
        using var document = JsonDocument.Parse(bytes);
        var root = document.RootElement;
        if (root.GetProperty("schemaVersion").GetInt32() != SeedCorpusSchemaVersion
            || root.GetProperty("kind").GetString() != SeedCorpusKind)
        {
            throw new InvalidDataException("The QA seed corpus contract is unsupported.");
        }

        var corpora = new List<BalanceSeedCorpus>();
        foreach (var element in root.GetProperty("corpora").EnumerateArray())
        {
            var sources = element.TryGetProperty("sources", out var sourceElement)
                ? sourceElement.EnumerateArray().Select(item => item.GetString()!).ToArray()
                : Array.Empty<string>();
            corpora.Add(
                new BalanceSeedCorpus(
                    element.GetProperty("id").GetString()!,
                    element.GetProperty("classification").GetString()!,
                    element.GetProperty("reviewed").GetBoolean(),
                    element.GetProperty("seeds")
                        .EnumerateArray()
                        .Select(item => item.GetUInt64())
                        .ToArray(),
                    sources));
        }

        ValidateCorpora(corpora);
        return (corpora, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
    }

    public static string Write(
        string repositoryRoot,
        BalanceLaboratoryEvidence evidence)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        ArgumentNullException.ThrowIfNull(evidence);
        var outputDirectory = Environment.GetEnvironmentVariable("VIBESNAKE_EVIDENCE_DIR");
        outputDirectory = string.IsNullOrWhiteSpace(outputDirectory)
            ? Path.Combine(repositoryRoot, "TestResults", "native")
            : Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(outputDirectory);
        var path = Path.Combine(outputDirectory, "balance_laboratory.json");
        File.WriteAllText(
            path,
            JsonSerializer.Serialize(evidence, SerializerOptions) + "\n",
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return path;
    }

    public static BalanceOutlierReplay WriteOutlierReplay(
        string repositoryRoot,
        BalanceRunTrace trace,
        string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        ArgumentNullException.ThrowIfNull(trace);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        var evidenceDirectory = Environment.GetEnvironmentVariable("VIBESNAKE_EVIDENCE_DIR");
        evidenceDirectory = string.IsNullOrWhiteSpace(evidenceDirectory)
            ? Path.Combine(repositoryRoot, "TestResults", "native")
            : Path.GetFullPath(evidenceDirectory);
        var outlierDirectory = Path.Combine(evidenceDirectory, "balance_lab", "outliers");
        Directory.CreateDirectory(outlierDirectory);
        var safeReason = string.Concat(reason.Select(character =>
            char.IsLetterOrDigit(character) ? char.ToLowerInvariant(character) : '_'));
        var fileName = $"{safeReason}_{trace.VariantId}_{trace.PolicyId}_{trace.Seed}.vibesnake-replay.json";
        var path = Path.Combine(outlierDirectory, fileName);

        var replay = RunReplay.Capture(
            SnakeRun.Create(trace.Seed, trace.Configuration),
            trace.Commands,
            checkpointInterval: 64);
        var serialized = replay.Serialize();
        var read = RunReplay.Read(serialized);
        var verified = read.Compatibility.IsCompatible
            && read.Replay is not null
            && read.Replay.Verify().IsValid
            && replay.Outcome.StateHash == trace.FinalStateHash;
        File.WriteAllText(
            path,
            serialized,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        var bytes = File.ReadAllBytes(path);
        var relativePath = Path.GetRelativePath(repositoryRoot, path).Replace('\\', '/');
        return new BalanceOutlierReplay(
            reason,
            trace.VariantId,
            trace.PolicyId,
            trace.Seed,
            trace.Steps,
            trace.Score,
            trace.FinalStateHash,
            relativePath,
            Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
            verified);
    }

    public static string ReproductionCommand =>
        $"dotnet test {TestProject} --filter FullyQualifiedName~Balance_laboratory";

    private static void ValidateCorpora(List<BalanceSeedCorpus> corpora)
    {
        string[] classifications = ["reviewed-fixed", "exploratory", "previous-failure"];
        if (corpora.Count != classifications.Length
            || corpora.Select(corpus => corpus.Id).Distinct(StringComparer.Ordinal).Count()
                != corpora.Count)
        {
            throw new InvalidDataException("The QA seed corpora are incomplete or duplicated.");
        }

        foreach (var classification in classifications)
        {
            var corpus = corpora.SingleOrDefault(item => item.Classification == classification);
            if (corpus is null
                || !corpus.Reviewed
                || corpus.Seeds.Count < 4
                || corpus.Seeds.Distinct().Count() != corpus.Seeds.Count)
            {
                throw new InvalidDataException(
                    $"The {classification} seed corpus is incomplete or unreviewed.");
            }
        }

        var previous = corpora.Single(item => item.Classification == "previous-failure");
        if (previous.Sources.Count == 0)
        {
            throw new InvalidDataException("Previous-failure seeds require source notes.");
        }
    }
}
