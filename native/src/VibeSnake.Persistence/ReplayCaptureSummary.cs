using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using VibeSnake.Rules;

namespace VibeSnake.Persistence;

/// <summary>
/// Closed, player-shareable metadata for one deterministically verified replay.
/// It contains no player identity, machine path, or arbitrary free text.
/// </summary>
public sealed record ReplayCaptureSummary(
    int SchemaVersion,
    string Kind,
    string ExportingAppVersion,
    string? ReplayAppVersion,
    string RulesetId,
    int RulesVersion,
    string ModeId,
    int ModeVersion,
    string ScoreCategoryId,
    string ConfigHashAlgorithm,
    string ConfigHash,
    string StateHashAlgorithm,
    string ReplayIntegrityAlgorithm,
    string ReplayPayloadHash,
    string? CapturedAtUtc,
    ulong? GameplaySeed,
    int StepCount,
    int FinalTick,
    RunStatus Status,
    DeathCause DeathCause,
    int Score,
    string FinalStateHash,
    bool ContainsPlayerIdentity,
    bool ContainsPrivatePaths)
{
    public const int CurrentSchemaVersion = 1;
    public const string KindId = "vibesnake-run-capture-summary-v1";
    public const int MaximumSerializedCharacters = 16 * 1024;

    private static readonly Regex AppVersionPattern = new(
        "^[0-9A-Za-z][0-9A-Za-z.+-]{0,63}$",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.KebabCaseLower) },
    };

    public static ReplayCaptureSummary Create(
        RunReplay replay,
        string exportingAppVersion)
    {
        ArgumentNullException.ThrowIfNull(replay);
        ArgumentException.ThrowIfNullOrWhiteSpace(exportingAppVersion);
        var normalizedVersion = exportingAppVersion.Trim();
        if (!AppVersionPattern.IsMatch(normalizedVersion))
        {
            throw new ArgumentException(
                "The exporting application version is invalid.",
                nameof(exportingAppVersion));
        }

        var verification = replay.Verify();
        if (!verification.IsValid)
        {
            throw new ArgumentException(
                "A capture summary requires a deterministically verified replay.",
                nameof(replay));
        }

        var initial = SnakeRun.RestoreCanonicalState(replay.InitialCanonicalState);
        return new ReplayCaptureSummary(
            SchemaVersion: CurrentSchemaVersion,
            Kind: KindId,
            ExportingAppVersion: normalizedVersion,
            ReplayAppVersion: replay.AppVersion,
            RulesetId: replay.Ruleset.Id,
            RulesVersion: replay.Ruleset.Version,
            ModeId: initial.Configuration.ModeId,
            ModeVersion: initial.Configuration.ModeVersion,
            ScoreCategoryId: initial.ScoreCategoryId,
            ConfigHashAlgorithm: replay.ConfigHashAlgorithm,
            ConfigHash: replay.ConfigHash,
            StateHashAlgorithm: replay.StateHashAlgorithmId,
            ReplayIntegrityAlgorithm: RunReplay.IntegrityAlgorithmId,
            ReplayPayloadHash: replay.PayloadHash,
            CapturedAtUtc: replay.CapturedAtUtc,
            GameplaySeed: replay.GameplaySeed,
            StepCount: replay.Outcome.StepCount,
            FinalTick: replay.Outcome.FinalTick,
            Status: replay.Outcome.Status,
            DeathCause: replay.Outcome.DeathCause,
            Score: replay.Outcome.Score,
            FinalStateHash: replay.Outcome.StateHash,
            ContainsPlayerIdentity: false,
            ContainsPrivatePaths: false);
    }

    public string Serialize()
    {
        var serialized = JsonSerializer.Serialize(this, SerializerOptions) + "\n";
        if (serialized.Length > MaximumSerializedCharacters)
        {
            throw new InvalidOperationException("The capture summary exceeds its size bound.");
        }

        return serialized;
    }
}
