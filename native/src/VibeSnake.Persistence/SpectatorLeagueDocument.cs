using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using VibeSnake.Rules;

namespace VibeSnake.Persistence;

public enum SpectatorLeagueLoadCode : byte
{
    Success = 0,
    InvalidJson = 1,
    UnsupportedSchema = 2,
    InvalidField = 3,
    TooLarge = 4,
    IoError = 5,
}

public sealed record SpectatorLeagueLoadResult(
    SpectatorLeagueLoadCode Code,
    string Message,
    SpectatorLeagueDocument? Document = null)
{
    public bool IsSuccess => Code == SpectatorLeagueLoadCode.Success && Document is not null;
}

public sealed record SpectatorStandingEntry(
    string PersonalityId,
    int Matches,
    int Wins,
    int Losses,
    int Ties,
    int BestScore,
    long TotalScore,
    int BestSurvivalTicks,
    IReadOnlyList<string> MilestoneIds)
{
    public int AverageScore => Matches == 0 ? 0 : (int)(TotalScore / Matches);
}

public sealed record SpectatorRivalryRecord(
    string Id,
    string LeftPersonalityId,
    string RightPersonalityId,
    int Matches,
    int LeftWins,
    int RightWins,
    int Ties,
    int LeftBestScore,
    int RightBestScore);

public sealed record SpectatorChallengeRecord(
    string PersonalityId,
    int Attempts,
    int HumanWins,
    int AiWins,
    int Ties,
    int HumanBestScore,
    int AiBestScore);

/// <summary>
/// Local AI league fiction and statistics. The closed document contains no
/// player identity and never updates human progression or achievement state.
/// </summary>
public sealed record SpectatorLeagueDocument(
    int SchemaVersion,
    IReadOnlyList<SpectatorStandingEntry> Standings,
    IReadOnlyList<SpectatorRivalryRecord> Rivalries,
    IReadOnlyList<SpectatorChallengeRecord> Challenges)
{
    public const int CurrentSchemaVersion = 1;
    public const string FileName = "spectator-league.json";
    public const int MaximumDocumentBytes = 262_144;
    public const int MaximumRivalries = 45;
    public const int MaximumCounter = 1_000_000;
    public const int MaximumMilestonesPerPersonality = 7;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        WriteIndented = true,
    };

    public static SpectatorLeagueDocument CreateDefaults() => new(
        CurrentSchemaVersion,
        AiPersonalityCatalog.BuiltIn
            .Select(item => EmptyStanding(item.Id))
            .OrderBy(item => item.PersonalityId, StringComparer.Ordinal)
            .ToArray(),
        Array.Empty<SpectatorRivalryRecord>(),
        AiPersonalityCatalog.BuiltIn
            .Select(item => EmptyChallenge(item.Id))
            .OrderBy(item => item.PersonalityId, StringComparer.Ordinal)
            .ToArray());

    public SpectatorStandingEntry StandingFor(string personalityId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(personalityId);
        return Standings.SingleOrDefault(item => item.PersonalityId == personalityId)
            ?? throw new ArgumentException("The spectator personality is unknown.", nameof(personalityId));
    }

    public IReadOnlyList<SpectatorStandingEntry> RankedStandings() => Standings
        .OrderByDescending(item => item.Wins)
        .ThenByDescending(item => item.AverageScore)
        .ThenByDescending(item => item.BestSurvivalTicks)
        .ThenBy(item => item.PersonalityId, StringComparer.Ordinal)
        .ToArray();

    public SpectatorLeagueDocument WithMatch(SpectatorMatchResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        ValidateMatch(result);
        var comparison = Compare(
            result.Featured.Score,
            result.Featured.FinalTick,
            result.Rival.Score,
            result.Rival.FinalTick);
        var standings = Standings.ToDictionary(item => item.PersonalityId, StringComparer.Ordinal);
        standings[result.Featured.PersonalityId] = ApplyLane(
            standings[result.Featured.PersonalityId],
            result.Featured,
            comparison);
        standings[result.Rival.PersonalityId] = ApplyLane(
            standings[result.Rival.PersonalityId],
            result.Rival,
            -comparison);

        var orderedIds = new[] { result.Featured.PersonalityId, result.Rival.PersonalityId }
            .Order(StringComparer.Ordinal)
            .ToArray();
        var rivalryId = RivalryId(orderedIds[0], orderedIds[1]);
        var rivalries = Rivalries.ToDictionary(item => item.Id, StringComparer.Ordinal);
        rivalries.TryGetValue(rivalryId, out var existing);
        existing ??= new SpectatorRivalryRecord(
            rivalryId,
            orderedIds[0],
            orderedIds[1],
            0,
            0,
            0,
            0,
            0,
            0);
        var featuredIsLeft = result.Featured.PersonalityId == existing.LeftPersonalityId;
        var leftScore = featuredIsLeft ? result.Featured.Score : result.Rival.Score;
        var rightScore = featuredIsLeft ? result.Rival.Score : result.Featured.Score;
        var leftComparison = featuredIsLeft ? comparison : -comparison;
        rivalries[rivalryId] = existing with
        {
            Matches = Increment(existing.Matches),
            LeftWins = existing.LeftWins + (leftComparison > 0 ? 1 : 0),
            RightWins = existing.RightWins + (leftComparison < 0 ? 1 : 0),
            Ties = existing.Ties + (leftComparison == 0 ? 1 : 0),
            LeftBestScore = Math.Max(existing.LeftBestScore, leftScore),
            RightBestScore = Math.Max(existing.RightBestScore, rightScore),
        };

        return NormalizeAndValidate(this with
        {
            Standings = standings.Values.ToArray(),
            Rivalries = rivalries.Values.ToArray(),
        });
    }

    public SpectatorLeagueDocument WithHumanChallenge(
        string personalityId,
        int aiScore,
        SpectatorChallengeDescriptor challenge,
        SnakeRun humanRun,
        ScoreRunContext context)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(personalityId);
        ArgumentNullException.ThrowIfNull(challenge);
        ArgumentNullException.ThrowIfNull(humanRun);
        ArgumentNullException.ThrowIfNull(context);
        _ = SpectatorRivalCatalog.Get(personalityId);
        challenge.Validate();
        if (context != ScoreRunContextCatalog.SeededChallenge
            || humanRun.Status == RunStatus.Running
            || humanRun.ConfigHash != challenge.ConfigHash
            || humanRun.Configuration.ModeId != challenge.ModeId
            || humanRun.Configuration.ModeVersion != challenge.ModeVersion
            || aiScore is < 0 or > SnakeRun.MaximumScore)
        {
            throw new ArgumentException(
                "A human challenge must be terminal, equal-rules, and use seeded-challenge identity.");
        }

        var records = Challenges.ToDictionary(item => item.PersonalityId, StringComparer.Ordinal);
        var existing = records[personalityId];
        var comparison = humanRun.Score.CompareTo(aiScore);
        records[personalityId] = existing with
        {
            Attempts = Increment(existing.Attempts),
            HumanWins = existing.HumanWins + (comparison > 0 ? 1 : 0),
            AiWins = existing.AiWins + (comparison < 0 ? 1 : 0),
            Ties = existing.Ties + (comparison == 0 ? 1 : 0),
            HumanBestScore = Math.Max(existing.HumanBestScore, humanRun.Score),
            AiBestScore = Math.Max(existing.AiBestScore, aiScore),
        };
        return NormalizeAndValidate(this with { Challenges = records.Values.ToArray() });
    }

    public string SerializeCanonical()
    {
        var normalized = NormalizeAndValidate(this with { SchemaVersion = CurrentSchemaVersion });
        return JsonSerializer.Serialize(normalized, SerializerOptions) + "\n";
    }

    public static SpectatorLeagueLoadResult Read(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new SpectatorLeagueLoadResult(
                SpectatorLeagueLoadCode.InvalidJson,
                "Spectator league document is empty.");
        }

        if (Encoding.UTF8.GetByteCount(json) > MaximumDocumentBytes)
        {
            return new SpectatorLeagueLoadResult(
                SpectatorLeagueLoadCode.TooLarge,
                "Spectator league document exceeds its byte limit.");
        }

        JsonDocument parsed;
        try
        {
            parsed = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return new SpectatorLeagueLoadResult(
                SpectatorLeagueLoadCode.InvalidJson,
                "Spectator league JSON is invalid.");
        }

        using (parsed)
        {
            try
            {
                RejectDuplicateProperties(parsed.RootElement, "spectatorLeague");
                if (!parsed.RootElement.TryGetProperty("schemaVersion", out var schema)
                    || !schema.TryGetInt32(out var schemaVersion))
                {
                    throw new InvalidDataException("Spectator league schemaVersion is required.");
                }

                if (schemaVersion != CurrentSchemaVersion)
                {
                    return new SpectatorLeagueLoadResult(
                        SpectatorLeagueLoadCode.UnsupportedSchema,
                        $"Spectator league schema {schemaVersion} is unsupported.");
                }

                var document = JsonSerializer.Deserialize<SpectatorLeagueDocument>(
                    json,
                    SerializerOptions)
                    ?? throw new JsonException("Spectator league payload was null.");
                document = NormalizeAndValidate(document);
                return new SpectatorLeagueLoadResult(
                    SpectatorLeagueLoadCode.Success,
                    "Spectator league document loaded.",
                    document);
            }
            catch (Exception exception) when (
                exception is JsonException
                    or InvalidDataException
                    or ArgumentException
                    or InvalidOperationException
                    or OverflowException)
            {
                return new SpectatorLeagueLoadResult(
                    SpectatorLeagueLoadCode.InvalidField,
                    "Spectator league document is invalid: " + exception.Message);
            }
        }
    }

    private static SpectatorLeagueDocument NormalizeAndValidate(
        SpectatorLeagueDocument document)
    {
        ArgumentNullException.ThrowIfNull(document.Standings);
        ArgumentNullException.ThrowIfNull(document.Rivalries);
        ArgumentNullException.ThrowIfNull(document.Challenges);
        if (document.SchemaVersion != CurrentSchemaVersion
            || document.Standings.Count != AiPersonalityCatalog.BuiltIn.Count
            || document.Challenges.Count != AiPersonalityCatalog.BuiltIn.Count
            || document.Rivalries.Count > MaximumRivalries)
        {
            throw new InvalidDataException("Spectator league collection bounds are invalid.");
        }

        var knownIds = AiPersonalityCatalog.BuiltIn
            .Select(item => item.Id)
            .ToHashSet(StringComparer.Ordinal);
        var standings = document.Standings
            .OrderBy(item => item.PersonalityId, StringComparer.Ordinal)
            .ToArray();
        var challenges = document.Challenges
            .OrderBy(item => item.PersonalityId, StringComparer.Ordinal)
            .ToArray();
        if (!standings.Select(item => item.PersonalityId).ToHashSet(StringComparer.Ordinal)
                .SetEquals(knownIds)
            || !challenges.Select(item => item.PersonalityId).ToHashSet(StringComparer.Ordinal)
                .SetEquals(knownIds))
        {
            throw new InvalidDataException("Spectator league personality coverage is incomplete.");
        }

        foreach (var standing in standings)
        {
            ValidateStanding(standing);
        }

        foreach (var challenge in challenges)
        {
            ValidateChallenge(challenge);
        }

        var rivalryIds = new HashSet<string>(StringComparer.Ordinal);
        var rivalries = document.Rivalries
            .OrderBy(item => item.Id, StringComparer.Ordinal)
            .ToArray();
        foreach (var rivalry in rivalries)
        {
            if (!knownIds.Contains(rivalry.LeftPersonalityId)
                || !knownIds.Contains(rivalry.RightPersonalityId)
                || string.CompareOrdinal(rivalry.LeftPersonalityId, rivalry.RightPersonalityId) >= 0
                || rivalry.Id != RivalryId(
                    rivalry.LeftPersonalityId,
                    rivalry.RightPersonalityId)
                || !rivalryIds.Add(rivalry.Id)
                || rivalry.Matches is < 0 or > MaximumCounter
                || rivalry.LeftWins < 0
                || rivalry.RightWins < 0
                || rivalry.Ties < 0
                || rivalry.LeftWins + rivalry.RightWins + rivalry.Ties != rivalry.Matches
                || rivalry.LeftBestScore is < 0 or > SnakeRun.MaximumScore
                || rivalry.RightBestScore is < 0 or > SnakeRun.MaximumScore)
            {
                throw new InvalidDataException("Spectator rivalry record is invalid.");
            }
        }

        return document with
        {
            Standings = standings,
            Rivalries = rivalries,
            Challenges = challenges,
        };
    }

    private static void ValidateStanding(SpectatorStandingEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry.MilestoneIds);
        var milestones = entry.MilestoneIds.ToHashSet(StringComparer.Ordinal);
        if (entry.Matches is < 0 or > MaximumCounter
            || entry.Wins < 0
            || entry.Losses < 0
            || entry.Ties < 0
            || entry.Wins + entry.Losses + entry.Ties != entry.Matches
            || entry.BestScore is < 0 or > SnakeRun.MaximumScore
            || entry.TotalScore is < 0 or > ((long)SnakeRun.MaximumScore * MaximumCounter)
            || entry.BestSurvivalTicks < 0
            || entry.MilestoneIds.Count > MaximumMilestonesPerPersonality
            || milestones.Count != entry.MilestoneIds.Count
            || milestones.Any(id => !MilestoneIds.Contains(id, StringComparer.Ordinal)))
        {
            throw new InvalidDataException("Spectator standing is invalid.");
        }
    }

    private static void ValidateChallenge(SpectatorChallengeRecord entry)
    {
        if (entry.Attempts is < 0 or > MaximumCounter
            || entry.HumanWins < 0
            || entry.AiWins < 0
            || entry.Ties < 0
            || entry.HumanWins + entry.AiWins + entry.Ties != entry.Attempts
            || entry.HumanBestScore is < 0 or > SnakeRun.MaximumScore
            || entry.AiBestScore is < 0 or > SnakeRun.MaximumScore)
        {
            throw new InvalidDataException("Spectator challenge record is invalid.");
        }
    }

    private static void ValidateMatch(SpectatorMatchResult result)
    {
        _ = SpectatorRivalCatalog.Get(result.Featured.PersonalityId);
        _ = SpectatorRivalCatalog.Get(result.Rival.PersonalityId);
        var mode = RunModeCatalog.Get(result.ModeId, result.ModeVersion);
        var config = RunModeCatalog.CreateConfig(mode);
        ValidateOutcome(result.Featured);
        ValidateOutcome(result.Rival);
        if (result.Featured.PersonalityId == result.Rival.PersonalityId
            || result.ConfigHash.Length != 64
            || result.ConfigHash.Any(character =>
                !char.IsAsciiHexDigit(character) || char.IsUpper(character))
            || result.ConfigHash != config.ComputeConfigHash()
            || !result.EqualRules
            || result.AiProgressionAwarded
            || (result.Featured.Status == RunStatus.Running
                && !result.Featured.EndedByBroadcastLimit)
            || (result.Rival.Status == RunStatus.Running
                && !result.Rival.EndedByBroadcastLimit)
            || result.PredictionCorrect != SpectatorPredictionContract.Evaluate(
                result.Prediction,
                result.Featured))
        {
            throw new ArgumentException("The spectator match result is invalid.", nameof(result));
        }
    }

    private static void ValidateOutcome(SpectatorLaneOutcome outcome)
    {
        if (outcome.Score is < 0 or > SnakeRun.MaximumScore
            || outcome.FinalTick < 0
            || outcome.MaximumCombo < 0
            || outcome.FoodEaten < 0
            || outcome.PowerCollections < 0
            || outcome.CollisionRecoveries < 0
            || (outcome.EndedByBroadcastLimit && outcome.Status != RunStatus.Running)
            || outcome.FinalStateHash.Length != 16
            || outcome.FinalStateHash.Any(character =>
                !char.IsAsciiHexDigit(character) || char.IsUpper(character)))
        {
            throw new ArgumentException("The spectator lane outcome is invalid.", nameof(outcome));
        }
    }

    private static SpectatorStandingEntry ApplyLane(
        SpectatorStandingEntry entry,
        SpectatorLaneOutcome outcome,
        int comparison)
    {
        var milestones = entry.MilestoneIds.ToHashSet(StringComparer.Ordinal);
        milestones.Add("first-broadcast");
        if (comparison > 0)
        {
            milestones.Add("match-win");
        }

        if (outcome.Score >= 100)
        {
            milestones.Add("score-100");
        }

        if (outcome.FinalTick >= 500)
        {
            milestones.Add("survive-500");
        }

        if (outcome.MaximumCombo >= 5)
        {
            milestones.Add("combo-5");
        }

        if (outcome.PowerCollections > 0)
        {
            milestones.Add("power-route");
        }

        if (outcome.CollisionRecoveries > 0)
        {
            milestones.Add("collision-save");
        }

        return entry with
        {
            Matches = Increment(entry.Matches),
            Wins = entry.Wins + (comparison > 0 ? 1 : 0),
            Losses = entry.Losses + (comparison < 0 ? 1 : 0),
            Ties = entry.Ties + (comparison == 0 ? 1 : 0),
            BestScore = Math.Max(entry.BestScore, outcome.Score),
            TotalScore = checked(entry.TotalScore + outcome.Score),
            BestSurvivalTicks = Math.Max(entry.BestSurvivalTicks, outcome.FinalTick),
            MilestoneIds = milestones.Order(StringComparer.Ordinal).ToArray(),
        };
    }

    private static int Compare(int leftScore, int leftTicks, int rightScore, int rightTicks)
    {
        var score = leftScore.CompareTo(rightScore);
        return score != 0 ? score : leftTicks.CompareTo(rightTicks);
    }

    private static int Increment(int value) => value >= MaximumCounter
        ? throw new InvalidOperationException("Spectator league counter reached its bound.")
        : value + 1;

    private static string RivalryId(string leftId, string rightId) =>
        $"{leftId}__vs__{rightId}";

    private static SpectatorStandingEntry EmptyStanding(string personalityId) => new(
        personalityId,
        0,
        0,
        0,
        0,
        0,
        0,
        0,
        Array.Empty<string>());

    private static SpectatorChallengeRecord EmptyChallenge(string personalityId) => new(
        personalityId,
        0,
        0,
        0,
        0,
        0,
        0);

    private static readonly IReadOnlyList<string> MilestoneIds =
    [
        "first-broadcast",
        "match-win",
        "score-100",
        "survive-500",
        "combo-5",
        "power-route",
        "collision-save",
    ];

    private static void RejectDuplicateProperties(JsonElement element, string location)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name))
                {
                    throw new InvalidDataException($"Duplicate field at {location}.{property.Name}.");
                }

                RejectDuplicateProperties(property.Value, $"{location}.{property.Name}");
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var item in element.EnumerateArray())
            {
                RejectDuplicateProperties(item, $"{location}[{index++}]");
            }
        }
    }
}

public sealed class SpectatorLeagueStore
{
    public SpectatorLeagueStore(string userDataRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userDataRoot);
        if (!Path.IsPathFullyQualified(userDataRoot))
        {
            throw new ArgumentException("The user-data root must be absolute.", nameof(userDataRoot));
        }

        UserDataRoot = Path.GetFullPath(userDataRoot);
        LeaguePath = Path.Combine(UserDataRoot, SpectatorLeagueDocument.FileName);
    }

    public string UserDataRoot { get; }

    public string LeaguePath { get; }

    public SpectatorLeagueLoadResult Load()
    {
        if (!File.Exists(LeaguePath))
        {
            return new SpectatorLeagueLoadResult(
                SpectatorLeagueLoadCode.Success,
                "Spectator league defaults applied.",
                SpectatorLeagueDocument.CreateDefaults());
        }

        try
        {
            return SpectatorLeagueDocument.Read(File.ReadAllText(LeaguePath));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new SpectatorLeagueLoadResult(
                SpectatorLeagueLoadCode.IoError,
                "Spectator league file could not be read.");
        }
    }

    public void Save(SpectatorLeagueDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        Directory.CreateDirectory(UserDataRoot);
        var temporaryPath = LeaguePath + $".tmp-{Guid.NewGuid():N}";
        try
        {
            using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                16 * 1024,
                FileOptions.WriteThrough))
            using (var writer = new StreamWriter(
                stream,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
            {
                writer.Write(document.SerializeCanonical());
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, LeaguePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                try
                {
                    File.Delete(temporaryPath);
                }
                catch (Exception exception) when (
                    exception is IOException or UnauthorizedAccessException)
                {
                    // The primary save result remains authoritative.
                }
            }
        }
    }
}
