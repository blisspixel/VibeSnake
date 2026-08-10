using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VibeSnake.Rules.Tests;

internal sealed record BalanceBaselineVariantEvidence(
    string Id,
    string ModeContractId,
    string ScoreCategoryId,
    string DifficultyPolicyId,
    bool AdaptationEnabled,
    string ConfigHash);

internal sealed record BalanceBaselineRunSummary(
    string VariantId,
    string PolicyId,
    ulong Seed,
    int SurvivalSteps,
    string Outcome,
    string DeathCause,
    int Score,
    int FinalLength,
    int MaximumLength,
    int FoodEaten,
    int ComboPeak,
    int PowerEncounters,
    int PowerPickups,
    int PowerActivations,
    string FinalStateHash);

internal sealed record ObservedBalanceDistribution(
    string VariantId,
    string PolicyId,
    int SampleCount,
    int ScoreMinimum,
    int ScoreP50,
    int ScoreP95,
    int ScoreP99,
    int ScoreMaximum,
    int SurvivalMinimum,
    int SurvivalP50,
    int SurvivalP95,
    int SurvivalP99,
    int SurvivalMaximum,
    int FinalLengthP50,
    int FinalLengthP95,
    int FinalLengthMaximum,
    int MaximumLengthP50,
    int MaximumLengthP95,
    int MaximumLengthMaximum,
    int FoodTotal,
    decimal FoodPerThousandSteps,
    int FoodP50,
    int FoodP95,
    int FoodMaximum,
    int ComboPeakP50,
    int ComboPeakP95,
    int ComboPeakMaximum,
    int PowerEncounterTotal,
    int PowerPickupTotal,
    int PowerActivationTotal,
    int StarvationDeaths,
    int CollisionDeaths,
    IReadOnlyDictionary<string, int> Outcomes);

internal sealed record BalanceBaselineEvidence(
    int SchemaVersion,
    string Kind,
    bool Passed,
    string Classification,
    bool AiSimulationOnly,
    bool HumanTargetRangesEstablished,
    IReadOnlyList<object> HumanTargetRanges,
    string RulesetId,
    int RulesVersion,
    string ConfigHashAlgorithm,
    string StateHashAlgorithm,
    string SeedCorpusKind,
    string SeedCorpusSha256,
    bool SeedCorpusReviewed,
    int SeedCount,
    int MaximumStepsPerRun,
    int VariantCount,
    int PolicyCount,
    int ReferenceAiPolicyCount,
    int SampleCountPerPair,
    int RunCount,
    string BaselineDocumentSha256,
    string ObservedDistributionSha256,
    bool BaselineMatched,
    IReadOnlyList<BalanceBaselineVariantEvidence> Variants,
    IReadOnlyList<BalancePolicyDefinition> Policies,
    IReadOnlyList<ObservedBalanceDistribution> Distributions,
    IReadOnlyList<BalanceBaselineRunSummary> RunSummaries,
    IReadOnlyList<string> Notes);

internal sealed record ExpectedBalanceBaseline(
    string DocumentSha256,
    string ObservedDistributionSha256);

internal static class BalanceBaselineReport
{
    public const int SchemaVersion = 1;
    public const string Kind = "observed-balance-baseline-evidence-v1";
    public const string Classification = "ai-simulation-observation";
    public const string SeedCorpusKind = "vibesnake-balance-baseline-seeds-v1";
    public const string BaselineKind = "vibesnake-observed-balance-baseline-v1";

    private static readonly JsonSerializerOptions EvidenceOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.KebabCaseLower) },
    };

    private static readonly JsonSerializerOptions CanonicalOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.KebabCaseLower) },
    };

    public static (IReadOnlyList<ulong> Seeds, string Sha256) ReadSeeds(
        string repositoryRoot)
    {
        var path = Path.Combine(repositoryRoot, "config", "qa_balance_baseline_seeds.json");
        var bytes = File.ReadAllBytes(path);
        using var document = JsonDocument.Parse(bytes);
        var root = document.RootElement;
        var seeds = root.GetProperty("seeds")
            .EnumerateArray()
            .Select(item => item.GetUInt64())
            .ToArray();
        if (root.GetProperty("schemaVersion").GetInt32() != SchemaVersion
            || root.GetProperty("kind").GetString() != SeedCorpusKind
            || !root.GetProperty("reviewed").GetBoolean()
            || seeds.Length != 100
            || seeds.Distinct().Count() != seeds.Length)
        {
            throw new InvalidDataException("The observed-balance seed corpus is invalid.");
        }

        return (seeds, Sha256(bytes));
    }

    public static ExpectedBalanceBaseline ReadExpectedBaseline(string repositoryRoot)
    {
        var path = Path.Combine(repositoryRoot, "config", "balance_baseline_v1.json");
        var bytes = File.ReadAllBytes(path);
        using var document = JsonDocument.Parse(bytes);
        var root = document.RootElement;
        if (root.GetProperty("schemaVersion").GetInt32() != SchemaVersion
            || root.GetProperty("kind").GetString() != BaselineKind
            || root.GetProperty("classification").GetString() != Classification
            || !root.GetProperty("reviewed").GetBoolean()
            || root.GetProperty("maximumStepsPerRun").GetInt32() != 900
            || root.GetProperty("runCount").GetInt32() != 2_700
            || root.GetProperty("humanTargetRanges").GetArrayLength() != 0)
        {
            throw new InvalidDataException("The observed-balance baseline contract is invalid.");
        }

        var observedDistributionSha256 = root.GetProperty(
            "observedDistributionSha256").GetString()!;
        if (observedDistributionSha256.Length != 64
            || observedDistributionSha256.Any(character =>
                !char.IsAsciiHexDigitLower(character)))
        {
            throw new InvalidDataException("The observed-balance baseline hash is invalid.");
        }

        return new ExpectedBalanceBaseline(
            Sha256(bytes),
            observedDistributionSha256);
    }

    public static string SerializeDistributions(
        IReadOnlyList<ObservedBalanceDistribution> distributions) =>
        JsonSerializer.Serialize(distributions, CanonicalOptions);

    public static string ComputeSha256(string value) =>
        Sha256(Encoding.UTF8.GetBytes(value));

    public static string Write(string repositoryRoot, BalanceBaselineEvidence evidence)
    {
        var outputDirectory = Environment.GetEnvironmentVariable("VIBESNAKE_EVIDENCE_DIR");
        outputDirectory = string.IsNullOrWhiteSpace(outputDirectory)
            ? Path.Combine(repositoryRoot, "TestResults", "native")
            : Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(outputDirectory);
        var path = Path.Combine(outputDirectory, "balance_baselines.json");
        File.WriteAllText(
            path,
            JsonSerializer.Serialize(evidence, EvidenceOptions) + "\n",
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return path;
    }

    private static string Sha256(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
