using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using VibeSnake.Rules;

namespace VibeSnake.Persistence;

public enum ProgressionLoadCode : byte
{
    Success = 0,
    InvalidJson = 1,
    UnsupportedSchema = 2,
    InvalidField = 3,
    TooLarge = 4,
    IoError = 5,
}

public sealed record ProgressionLoadResult(
    ProgressionLoadCode Code,
    string Message,
    ProgressionDocument? Document = null)
{
    public bool IsSuccess => Code == ProgressionLoadCode.Success && Document is not null;
}

/// <summary>
/// Monotonic human-only goal and finite-tour progress. Rewards are identifiers
/// for expression/content only and never feed back into rules configuration.
/// </summary>
public sealed record ProgressionDocument(
    int SchemaVersion,
    ProgressionMetrics Metrics,
    string? HighlightedGoalId,
    string SelectedCosmeticSetId,
    IReadOnlyList<string> SavedCosmeticSetIds,
    IReadOnlyList<string> CompletedTourEventIds,
    IReadOnlyList<string> UnlockedRewardIds)
{
    public const int CurrentSchemaVersion = 1;
    public const string FileName = "progression.json";
    public const int MaximumDocumentBytes = 131_072;
    public const int MaximumCompletedTourEvents = 64;
    public const int MaximumUnlockedRewards = 256;
    public const int MaximumSavedCosmeticSets = 5;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        WriteIndented = true,
    };

    public static ProgressionDocument CreateDefaults() =>
        new(
            CurrentSchemaVersion,
            new ProgressionMetrics(),
            HighlightedGoalId: null,
            SelectedCosmeticSetId: "classic-signal",
            SavedCosmeticSetIds: Array.Empty<string>(),
            CompletedTourEventIds: Array.Empty<string>(),
            UnlockedRewardIds: Array.Empty<string>());

    public IReadOnlyList<ProgressionGoalProgress> BuildGoalProgress() =>
        ProgressionGoalCatalog.BuildProgress(Metrics, HighlightedGoalId);

    public ProgressionDocument WithHumanRun(
        RunAchievementMetrics run,
        ScoreRunContext context) =>
        ReconcileRewards(this with { Metrics = Metrics.MergeHumanRun(run, context) });

    public ProgressionDocument WithHighlightedGoal(string? goalId)
    {
        if (goalId is not null && ProgressionGoalCatalog.Find(goalId) is null)
        {
            throw new ArgumentException("The highlighted progression goal is unknown.", nameof(goalId));
        }

        return this with { HighlightedGoalId = goalId };
    }

    public bool IsCosmeticSetUnlocked(string cosmeticSetId)
    {
        var cosmetic = CosmeticSetCatalog.Find(cosmeticSetId)
            ?? throw new ArgumentException("The cosmetic set is unknown.", nameof(cosmeticSetId));
        return cosmetic.AvailableFromStart
            || UnlockedRewardIds.Contains(cosmetic.UnlockRewardId, StringComparer.Ordinal);
    }

    public ProgressionDocument WithSelectedCosmeticSet(string cosmeticSetId)
    {
        if (!IsCosmeticSetUnlocked(cosmeticSetId))
        {
            throw new InvalidOperationException("The cosmetic set is still locked.");
        }

        return this with { SelectedCosmeticSetId = cosmeticSetId };
    }

    public ProgressionDocument WithSavedCosmeticSet(string cosmeticSetId)
    {
        if (!IsCosmeticSetUnlocked(cosmeticSetId))
        {
            throw new InvalidOperationException("A locked cosmetic set cannot be saved.");
        }

        var saved = SavedCosmeticSetIds.ToHashSet(StringComparer.Ordinal);
        if (saved.Contains(cosmeticSetId))
        {
            return this;
        }

        if (saved.Count >= MaximumSavedCosmeticSets)
        {
            throw new InvalidOperationException("All five cosmetic loadout slots are occupied.");
        }

        saved.Add(cosmeticSetId);
        return ReconcileRewards(this with
        {
            SavedCosmeticSetIds = saved.Order(StringComparer.Ordinal).ToArray(),
            Metrics = Metrics.WithPresentationProgress(
                saved.Count,
                Metrics.CosmeticSetsUnlocked,
                Metrics.TourEventsCompleted),
        });
    }

    public ProgressionDocument CompleteTourEvent(string eventId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventId);
        var item = BroadcastTourCatalog.Events.SingleOrDefault(candidate =>
            string.Equals(candidate.Id, eventId, StringComparison.Ordinal))
            ?? throw new ArgumentException("The Broadcast Tour event is unknown.", nameof(eventId));
        var completed = CompletedTourEventIds.ToHashSet(StringComparer.Ordinal);
        if (completed.Contains(item.Id))
        {
            return this;
        }

        if (item.PrerequisiteEventIds.Any(id => !completed.Contains(id)))
        {
            throw new InvalidOperationException("Broadcast Tour prerequisites are incomplete.");
        }

        completed.Add(item.Id);
        var rewards = UnlockedRewardIds.ToHashSet(StringComparer.Ordinal);
        rewards.Add(item.Reward.Id);
        var unlockedCosmeticSets = CosmeticSetCatalog.Sets.Count(set =>
            !set.AvailableFromStart && rewards.Contains(set.UnlockRewardId));
        var updated = this with
        {
            CompletedTourEventIds = completed.Order(StringComparer.Ordinal).ToArray(),
            UnlockedRewardIds = rewards.Order(StringComparer.Ordinal).ToArray(),
            Metrics = Metrics.WithPresentationProgress(
                Metrics.SavedLoadouts,
                unlockedCosmeticSets,
                completed.Count),
        };
        return ReconcileRewards(updated);
    }

    public string SerializeCanonical()
    {
        var normalized = NormalizeAndValidate(this with { SchemaVersion = CurrentSchemaVersion });
        return JsonSerializer.Serialize(normalized, SerializerOptions) + "\n";
    }

    public static ProgressionLoadResult Read(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new ProgressionLoadResult(
                ProgressionLoadCode.InvalidJson,
                "Progression document is empty.");
        }

        if (Encoding.UTF8.GetByteCount(json) > MaximumDocumentBytes)
        {
            return new ProgressionLoadResult(
                ProgressionLoadCode.TooLarge,
                "Progression document exceeds its byte limit.");
        }

        try
        {
            using var parsed = JsonDocument.Parse(json);
            RejectDuplicateProperties(parsed.RootElement, "progression");
            var schema = parsed.RootElement.GetProperty("schemaVersion").GetInt32();
            if (schema != CurrentSchemaVersion)
            {
                return new ProgressionLoadResult(
                    ProgressionLoadCode.UnsupportedSchema,
                    $"Progression schema {schema} is unsupported.");
            }

            var document = JsonSerializer.Deserialize<ProgressionDocument>(json, SerializerOptions)
                ?? throw new JsonException("Progression payload was null.");
            document = NormalizeAndValidate(document);
            return new ProgressionLoadResult(
                ProgressionLoadCode.Success,
                "Progression document loaded.",
                document);
        }
        catch (Exception exception) when (
            exception is JsonException
                or InvalidDataException
                or ArgumentException
                or InvalidOperationException
                or OverflowException)
        {
            return new ProgressionLoadResult(
                ProgressionLoadCode.InvalidField,
                "Progression document is invalid: " + exception.Message);
        }
    }

    private static ProgressionDocument ReconcileRewards(ProgressionDocument document)
    {
        var rewards = document.UnlockedRewardIds.ToHashSet(StringComparer.Ordinal);
        rewards.UnionWith(ExpectedRewardIds(document.Metrics, document.CompletedTourEventIds));

        return document with { UnlockedRewardIds = rewards.Order(StringComparer.Ordinal).ToArray() };
    }

    private static ProgressionDocument NormalizeAndValidate(ProgressionDocument document)
    {
        ArgumentNullException.ThrowIfNull(document.Metrics);
        if (document.SchemaVersion != CurrentSchemaVersion)
        {
            throw new InvalidDataException("Progression schema is unsupported.");
        }

        foreach (var metric in Enum.GetValues<ProgressionMetric>())
        {
            if (document.Metrics.ValueFor(metric) is < 0 or > 1_000_000_000)
            {
                throw new InvalidDataException("Progression metrics must be bounded and nonnegative.");
            }
        }

        if (document.Metrics.SavedLoadouts > 5)
        {
            throw new InvalidDataException("Saved loadouts exceed the five-slot limit.");
        }

        if (document.HighlightedGoalId is not null
            && ProgressionGoalCatalog.Find(document.HighlightedGoalId) is null)
        {
            throw new InvalidDataException("Highlighted progression goal is unknown.");
        }

        var completed = NormalizeKnownIds(
            document.CompletedTourEventIds,
            BroadcastTourCatalog.Events.Select(item => item.Id),
            MaximumCompletedTourEvents,
            "tour event");
        var knownRewards = ProgressionGoalCatalog.Goals.Select(goal => goal.Reward.Id)
            .Concat(BroadcastTourCatalog.Events.Select(item => item.Reward.Id));
        var rewards = NormalizeKnownIds(
            document.UnlockedRewardIds,
            knownRewards,
            MaximumUnlockedRewards,
            "progression reward");
        var savedCosmetics = NormalizeKnownIds(
            document.SavedCosmeticSetIds,
            CosmeticSetCatalog.Sets.Select(item => item.Id),
            MaximumSavedCosmeticSets,
            "saved cosmetic set");
        var selectedCosmetic = CosmeticSetCatalog.Find(document.SelectedCosmeticSetId)
            ?? throw new InvalidDataException("Selected cosmetic set is unknown.");
        bool IsUnlocked(CosmeticSetDefinition cosmetic) => cosmetic.AvailableFromStart
            || rewards.Contains(cosmetic.UnlockRewardId, StringComparer.Ordinal);
        if (!IsUnlocked(selectedCosmetic)
            || savedCosmetics.Any(id => !IsUnlocked(CosmeticSetCatalog.Find(id)!)))
        {
            throw new InvalidDataException("Selected and saved cosmetic sets must be unlocked.");
        }

        if (document.Metrics.SavedLoadouts != savedCosmetics.Count)
        {
            throw new InvalidDataException("Saved loadout count does not match saved cosmetic IDs.");
        }

        var unlockedCosmeticCount = CosmeticSetCatalog.Sets.Count(item =>
            !item.AvailableFromStart && rewards.Contains(item.UnlockRewardId, StringComparer.Ordinal));
        if (document.Metrics.CosmeticSetsUnlocked != unlockedCosmeticCount)
        {
            throw new InvalidDataException(
                "Unlocked cosmetic count does not match earned cosmetic rewards.");
        }
        if (document.Metrics.TourEventsCompleted != completed.Count)
        {
            throw new InvalidDataException("Tour completion count does not match completed event IDs.");
        }

        var completedSet = completed.ToHashSet(StringComparer.Ordinal);
        foreach (var eventId in completed)
        {
            var tourEvent = BroadcastTourCatalog.Events.Single(item => item.Id == eventId);
            if (tourEvent.PrerequisiteEventIds.Any(id => !completedSet.Contains(id)))
            {
                throw new InvalidDataException(
                    "Completed Broadcast Tour events must include every prerequisite.");
            }
        }

        var expectedRewards = ExpectedRewardIds(document.Metrics, completed);
        if (!rewards.ToHashSet(StringComparer.Ordinal).SetEquals(expectedRewards))
        {
            throw new InvalidDataException(
                "Unlocked progression rewards must exactly match earned goals and tour events.");
        }

        return document with
        {
            CompletedTourEventIds = completed,
            SavedCosmeticSetIds = savedCosmetics,
            UnlockedRewardIds = rewards,
        };
    }

    private static IReadOnlySet<string> ExpectedRewardIds(
        ProgressionMetrics metrics,
        IReadOnlyList<string> completedTourEventIds)
    {
        var rewards = ProgressionGoalCatalog.BuildProgress(metrics)
            .Where(progress => progress.Completed)
            .Select(progress => progress.Definition.Reward.Id)
            .ToHashSet(StringComparer.Ordinal);
        var completed = completedTourEventIds.ToHashSet(StringComparer.Ordinal);
        rewards.UnionWith(BroadcastTourCatalog.Events
            .Where(item => completed.Contains(item.Id))
            .Select(item => item.Reward.Id));
        return rewards;
    }

    private static IReadOnlyList<string> NormalizeKnownIds(
        IReadOnlyList<string> ids,
        IEnumerable<string> knownIds,
        int maximum,
        string label)
    {
        ArgumentNullException.ThrowIfNull(ids);
        if (ids.Count > maximum)
        {
            throw new InvalidDataException($"{label} count exceeds its limit.");
        }

        var known = knownIds.ToHashSet(StringComparer.Ordinal);
        var normalized = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var id in ids)
        {
            if (string.IsNullOrWhiteSpace(id) || !known.Contains(id) || !normalized.Add(id))
            {
                throw new InvalidDataException($"Unknown or duplicate {label} ID.");
            }
        }

        return normalized.ToArray();
    }

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

public sealed class ProgressionStore
{
    public ProgressionStore(string userDataRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userDataRoot);
        if (!Path.IsPathFullyQualified(userDataRoot))
        {
            throw new ArgumentException("The user-data root must be absolute.", nameof(userDataRoot));
        }

        UserDataRoot = Path.GetFullPath(userDataRoot);
        ProgressionPath = Path.Combine(UserDataRoot, ProgressionDocument.FileName);
    }

    public string UserDataRoot { get; }

    public string ProgressionPath { get; }

    public ProgressionLoadResult Load()
    {
        if (!File.Exists(ProgressionPath))
        {
            return new ProgressionLoadResult(
                ProgressionLoadCode.Success,
                "Progression defaults applied.",
                ProgressionDocument.CreateDefaults());
        }

        try
        {
            return ProgressionDocument.Read(File.ReadAllText(ProgressionPath));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new ProgressionLoadResult(
                ProgressionLoadCode.IoError,
                "Progression file could not be read: " + exception.Message);
        }
    }

    public void Save(ProgressionDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        Directory.CreateDirectory(UserDataRoot);
        var temporaryPath = ProgressionPath + ".tmp";
        File.WriteAllText(
            temporaryPath,
            document.SerializeCanonical(),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.Move(temporaryPath, ProgressionPath, overwrite: true);
    }
}
