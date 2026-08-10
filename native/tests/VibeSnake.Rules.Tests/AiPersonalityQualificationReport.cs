using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using VibeSnake.Persistence;

namespace VibeSnake.Rules.Tests;

internal sealed record AiBehaviorClaimEvidence(
    string PersonalityId,
    AiBehaviorMetric Metric,
    int ObservedValue,
    int InclusiveMinimum,
    int InclusiveMaximum,
    string PlayerFacingMeaning,
    bool Passed);

internal sealed record AiCustomValidationProbe(
    string Id,
    string SourceName,
    PersonalityLoadCode ActualCode,
    PersonalityLoadCode ExpectedCode,
    bool FilenameSpecific,
    bool Passed);

internal sealed record AiOverlayEvidence(
    string PolicyId,
    string Target,
    AiRiskBand Risk,
    AiDecisionReason CurrentDecision,
    int RecentDecisionCount,
    string BuiltInStatus,
    string CustomStatus,
    bool CustomOfficialLeagueQualified,
    bool Passed);

internal sealed record AiPersonalityQualificationEvidence(
    int SchemaVersion,
    string Kind,
    bool Passed,
    string ControllerAlgorithm,
    int CustomSchemaVersion,
    int BuiltInCount,
    int BehaviorClaimCount,
    int TraitSensitivityCount,
    int InertTraitCount,
    int ComparedStepCount,
    bool CompatibilityIdsRetained,
    bool GreedConsumed,
    bool AllTraitsMaterial,
    IReadOnlyList<string> DisplayNames,
    IReadOnlyList<AiBehaviorClaimEvidence> BehaviorClaims,
    IReadOnlyList<AiCustomValidationProbe> CustomValidation,
    AiOverlayEvidence Overlay,
    IReadOnlyList<string> Notes);

internal static class AiPersonalityQualificationReport
{
    public const int SchemaVersion = 1;
    public const string Kind = "ai-personality-qualification-v1";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.KebabCaseLower) },
    };

    public static string Write(string repositoryRoot, AiPersonalityQualificationEvidence evidence)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        ArgumentNullException.ThrowIfNull(evidence);
        var outputDirectory = Environment.GetEnvironmentVariable("VIBESNAKE_EVIDENCE_DIR");
        outputDirectory = string.IsNullOrWhiteSpace(outputDirectory)
            ? Path.Combine(repositoryRoot, "TestResults", "native")
            : Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(outputDirectory);
        var path = Path.Combine(outputDirectory, "ai_personalities.json");
        File.WriteAllText(
            path,
            JsonSerializer.Serialize(evidence, SerializerOptions) + "\n",
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return path;
    }
}
