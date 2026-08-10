using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using VibeSnake.Rules;

namespace VibeSnake.Persistence;

/// <summary>
/// Closed, privacy-safe card derived from one verified replay. Presentation
/// selections use catalog IDs and cannot contain player-authored text.
/// </summary>
public sealed record OfflineRunCard(
    int SchemaVersion,
    string Kind,
    string ExportingAppVersion,
    string RulesetId,
    int RulesVersion,
    string ContentContractId,
    string ModeId,
    int ModeVersion,
    string ConfigHashAlgorithm,
    string ConfigHash,
    string SeedCode,
    ulong GameplaySeed,
    int Score,
    int PeakCombo,
    int Length,
    int StepCount,
    RunStatus Status,
    DeathCause DeathCause,
    string StationId,
    IReadOnlyList<string> PowerIds,
    string SelectedLookId,
    string VerificationState,
    string ReplayIntegrityAlgorithm,
    string ReplayPayloadHash,
    bool ContainsPlayerIdentity,
    bool ContainsPrivatePaths)
{
    public const int CurrentSchemaVersion = 1;
    public const string KindId = "vibesnake-offline-run-card-v1";
    public const string VerifiedState = "verified";
    public const int FieldCount = 26;
    public const int MaximumSerializedCharacters = 32 * 1024;

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

    public static OfflineRunCard Create(
        RunReplay replay,
        SeedChallengeDescriptor challenge,
        string exportingAppVersion,
        string stationId,
        string selectedLookId)
    {
        ArgumentNullException.ThrowIfNull(replay);
        ArgumentNullException.ThrowIfNull(challenge);
        ArgumentException.ThrowIfNullOrWhiteSpace(exportingAppVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(stationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(selectedLookId);
        var normalizedVersion = exportingAppVersion.Trim();
        if (!AppVersionPattern.IsMatch(normalizedVersion))
        {
            throw new ArgumentException(
                "The exporting application version is invalid.",
                nameof(exportingAppVersion));
        }

        challenge.Validate();
        var verification = replay.Verify();
        if (!verification.IsValid
            || replay.GameplaySeed != challenge.GameplaySeed
            || !string.Equals(replay.ConfigHash, challenge.ConfigHash, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "A run card requires a verified replay matching its seed challenge.",
                nameof(replay));
        }

        if (BroadcastStationCatalog.Find(stationId) is null)
        {
            throw new ArgumentException("The run-card station is unknown.", nameof(stationId));
        }

        if (CosmeticSetCatalog.Find(selectedLookId) is null)
        {
            throw new ArgumentException("The run-card look is unknown.", nameof(selectedLookId));
        }

        var playback = new RunReplayPlayback(replay);
        var peakCombo = playback.CurrentSnapshot.ComboCount;
        var powers = new HashSet<PowerKind>();
        while (playback.TryAdvance(out var frame))
        {
            peakCombo = Math.Max(peakCombo, frame!.Snapshot.ComboCount);
            foreach (var detail in frame.Result.OrderedEvents)
            {
                if (detail.Kind == RunEventKind.PowerCollected && detail.Power is { } power)
                {
                    powers.Add(power);
                }
            }
        }

        var final = playback.CurrentSnapshot;
        return new OfflineRunCard(
            CurrentSchemaVersion,
            KindId,
            normalizedVersion,
            replay.Ruleset.Id,
            replay.Ruleset.Version,
            challenge.ContentContractId,
            challenge.ModeId,
            challenge.ModeVersion,
            challenge.ConfigHashAlgorithm,
            challenge.ConfigHash,
            challenge.Encode(),
            challenge.GameplaySeed,
            final.Score,
            peakCombo,
            final.Body.Count,
            replay.Outcome.StepCount,
            final.Status,
            final.DeathCause,
            stationId,
            powers.Order().Select(power => PowerDecisionCatalog.Get(power).Id).ToArray(),
            selectedLookId,
            VerifiedState,
            RunReplay.IntegrityAlgorithmId,
            replay.PayloadHash,
            ContainsPlayerIdentity: false,
            ContainsPrivatePaths: false);
    }

    public IReadOnlyList<string> ToDisplayLines() =>
    [
        "VERIFIED OFFLINE RUN",
        $"{ModeId.ToUpperInvariant()}@{ModeVersion}  SCORE {Score:D6}  STEPS {StepCount}",
        $"PEAK COMBO {PeakCombo}  LENGTH {Length}  SEED {GameplaySeed}",
        $"STATION {StationIdForDisplay(StationId)}  LOOK {SelectedLookId.ToUpperInvariant()}",
        $"POWERS {(PowerIds.Count == 0 ? "NONE" : string.Join(", ", PowerIds).ToUpperInvariant())}",
        $"RULES {RulesetId}@{RulesVersion}  CONTENT {ContentContractId}",
    ];

    public string Serialize()
    {
        var serialized = JsonSerializer.Serialize(this, SerializerOptions) + "\n";
        if (serialized.Length > MaximumSerializedCharacters)
        {
            throw new InvalidOperationException("The offline run card exceeds its size bound.");
        }

        return serialized;
    }

    private static string StationIdForDisplay(string stationId) =>
        stationId.Replace('_', ' ').ToUpperInvariant();
}
