using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VibeSnake.Rules.Tests;

internal sealed record AiLeagueMetricDefinition(
    string Id,
    string Unit,
    string Definition);

internal sealed record AiLeagueRun(
    string CorpusId,
    string PersonalityId,
    ulong Seed,
    int RulesVersion,
    string RunKindId,
    string SeedCategoryId,
    string DisplayCategoryId,
    bool CompetitiveEligible,
    string RulesScoreCategoryId,
    int Steps,
    RunStatus Status,
    DeathCause DeathCause,
    int Score,
    int FoodEaten,
    int PowerCollections,
    int DecisionCount,
    int PowerOpportunityCount,
    int PowerTargetCount,
    int RiskExposureCount,
    int DeadEndCount,
    int RouteOpportunityCount,
    int EfficientRouteCount,
    string DecisionTraceSha256,
    string FinalStateHash);

internal sealed record AiLeagueDistribution(
    string PersonalityId,
    int RulesVersion,
    int SampleCount,
    int SurvivalMinimum,
    int SurvivalP50,
    int SurvivalP95,
    int SurvivalMaximum,
    int ScoreMinimum,
    int ScoreP50,
    int ScoreP95,
    int ScoreMaximum,
    int FoodEfficiencyPerThousandSteps,
    int PowerPreferenceBasisPoints,
    int RiskExposureBasisPoints,
    int DeadEndBasisPoints,
    int RouteEfficiencyBasisPoints,
    IReadOnlyDictionary<string, int> DeathCauses);

internal sealed record AiTraitSensitivity(
    string PersonalityId,
    AiPersonalityTrait Trait,
    int BaselineValue,
    int InterventionValue,
    int ObservedDecisionCount,
    int ChangedDecisionCount,
    int ChangedDecisionBasisPoints,
    bool MateriallyAffectedDecisions);

internal sealed record AiLeaderboardIsolation(
    string RunKindId,
    string SeedCategoryId,
    string DisplayCategoryId,
    bool CompetitiveEligible,
    bool WritesHumanScoreStorage,
    string Enforcement);

internal sealed record AiLeagueEvidence(
    int SchemaVersion,
    string Kind,
    bool Passed,
    string ControllerAlgorithm,
    string RandomAlgorithm,
    string RulesetId,
    int RulesVersion,
    string SeedCorpusKind,
    int SeedCorpusSchemaVersion,
    string SeedCorpusSha256,
    IReadOnlyList<BalanceSeedCorpus> SeedCorpora,
    int MaximumStepsPerRun,
    int PersonalityCount,
    int RunCount,
    int ComparedStepCount,
    IReadOnlyList<AiLeagueMetricDefinition> Metrics,
    IReadOnlyList<AiPersonality> Personalities,
    IReadOnlyList<AiLeagueDistribution> Distributions,
    IReadOnlyList<AiTraitSensitivity> TraitSensitivities,
    IReadOnlyList<AiTraitSensitivity> InertTraits,
    AiLeaderboardIsolation LeaderboardIsolation,
    IReadOnlyList<AiLeagueRun> Runs,
    IReadOnlyList<string> Notes);

internal static class AiLeagueReport
{
    public const int SchemaVersion = 1;
    public const string Kind = "native-ai-league-v1";
    public const int MaterialSensitivityBasisPoints = 100;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.KebabCaseLower) },
    };

    public static string Write(string repositoryRoot, AiLeagueEvidence evidence)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        ArgumentNullException.ThrowIfNull(evidence);
        var outputDirectory = Environment.GetEnvironmentVariable("VIBESNAKE_EVIDENCE_DIR");
        outputDirectory = string.IsNullOrWhiteSpace(outputDirectory)
            ? Path.Combine(repositoryRoot, "TestResults", "native")
            : Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(outputDirectory);
        var path = Path.Combine(outputDirectory, "ai_league.json");
        File.WriteAllText(
            path,
            JsonSerializer.Serialize(evidence, SerializerOptions) + "\n",
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return path;
    }
}
