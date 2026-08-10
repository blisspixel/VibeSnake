namespace VibeSnake.Rules;

public enum BroadcastTourEventState : byte
{
    Locked = 0,
    Available = 1,
    Completed = 2,
}

public sealed record BroadcastTourCard(
    BroadcastTourEvent Event,
    BroadcastTourEventState State);

public sealed record BroadcastTourOutcome(
    string EventId,
    int PrimaryCurrent,
    int PrimaryTarget,
    bool PrimaryComplete,
    int? StyleCurrent,
    int? StyleTarget,
    bool? StyleComplete)
{
    public string PrimaryProgress => $"{Math.Min(PrimaryCurrent, PrimaryTarget)}/{PrimaryTarget}";

    public string? StyleProgress => StyleCurrent is { } current && StyleTarget is { } target
        ? $"{Math.Min(current, target)}/{target}"
        : null;
}

/// <summary>
/// Pure availability, run-construction, and outcome rules for the finite
/// Broadcast Tour. Product presentation may browse these cards, but cannot
/// alter event seeds, rules identity, completion, or reward eligibility.
/// </summary>
public static class BroadcastTourSession
{
    public static IReadOnlyList<BroadcastTourCard> BuildCards(
        IReadOnlyCollection<string> completedEventIds)
    {
        ArgumentNullException.ThrowIfNull(completedEventIds);
        var completed = completedEventIds.ToHashSet(StringComparer.Ordinal);
        if (completed.Count != completedEventIds.Count
            || completed.Any(id => BroadcastTourCatalog.Events.All(item => item.Id != id)))
        {
            throw new ArgumentException(
                "Completed Broadcast Tour event IDs must be known and unique.",
                nameof(completedEventIds));
        }

        foreach (var eventId in completed)
        {
            var item = BroadcastTourCatalog.Events.Single(candidate => candidate.Id == eventId);
            if (item.PrerequisiteEventIds.Any(id => !completed.Contains(id)))
            {
                throw new ArgumentException(
                    "Completed Broadcast Tour events must include their prerequisites.",
                    nameof(completedEventIds));
            }
        }

        return BroadcastTourCatalog.Events
            .Select(item => new BroadcastTourCard(
                item,
                completed.Contains(item.Id)
                    ? BroadcastTourEventState.Completed
                    : item.PrerequisiteEventIds.All(completed.Contains)
                        ? BroadcastTourEventState.Available
                        : BroadcastTourEventState.Locked))
            .ToArray();
    }

    public static SnakeRun CreateRun(BroadcastTourEvent item)
    {
        var canonical = RequireCanonicalEvent(item);
        var seed = canonical.FixedSeed!.Value;
        var mode = RunModeCatalog.Get(canonical.ModeId, canonical.ModeVersion);
        var config = RunModeCatalog.CreateConfig(mode, enableAdaptation: true);
        return SnakeRun.Create(seed, config);
    }

    public static BroadcastTourOutcome Evaluate(
        BroadcastTourEvent item,
        SnakeRun run)
    {
        var canonical = RequireCanonicalEvent(item);
        ArgumentNullException.ThrowIfNull(run);
        if (run.Status == RunStatus.Running)
        {
            throw new ArgumentException(
                "Broadcast Tour outcomes require a terminal run.",
                nameof(run));
        }

        if (run.Configuration.ModeId != canonical.ModeId
            || run.Configuration.ModeVersion != canonical.ModeVersion
            || run.ScoreCategoryId != canonical.ScoreCategoryId
            || run.MasterSeed != canonical.FixedSeed)
        {
            throw new ArgumentException(
                "The terminal run does not match the Broadcast Tour event identity.",
                nameof(run));
        }

        return EvaluateMetrics(canonical, run.ToAchievementMetrics());
    }

    internal static BroadcastTourOutcome EvaluateMetrics(
        BroadcastTourEvent item,
        RunAchievementMetrics metrics)
    {
        var canonical = RequireCanonicalEvent(item);
        if (!metrics.IsTerminal)
        {
            throw new ArgumentException(
                "Broadcast Tour outcome metrics must be terminal.",
                nameof(metrics));
        }

        var primaryCurrent = ValueForRun(metrics, canonical.PrimaryGoal.Metric);
        int? styleCurrent = canonical.StyleGoal is { } style
            ? ValueForRun(metrics, style.Metric)
            : null;
        return new BroadcastTourOutcome(
            canonical.Id,
            primaryCurrent,
            canonical.PrimaryGoal.Target,
            primaryCurrent >= canonical.PrimaryGoal.Target,
            styleCurrent,
            canonical.StyleGoal?.Target,
            canonical.StyleGoal is null
                ? null
                : styleCurrent >= canonical.StyleGoal.Target);
    }

    private static BroadcastTourEvent RequireCanonicalEvent(BroadcastTourEvent item)
    {
        ArgumentNullException.ThrowIfNull(item);
        var canonical = BroadcastTourCatalog.Events.SingleOrDefault(candidate =>
            string.Equals(candidate.Id, item.Id, StringComparison.Ordinal));
        if (canonical is null || canonical != item)
        {
            throw new ArgumentException(
                "The Broadcast Tour event does not match the canonical catalog.",
                nameof(item));
        }

        return canonical;
    }

    internal static int ValueForRun(
        RunAchievementMetrics metrics,
        ProgressionMetric metric) => metric switch
        {
            ProgressionMetric.HighestScore => metrics.Score,
            ProgressionMetric.HighestCombo => metrics.MaxCombo,
            ProgressionMetric.LongestLength => metrics.Length,
            ProgressionMetric.MostFoodInRun => metrics.FoodEaten,
            ProgressionMetric.MostWrapsInRun => metrics.WrapCount,
            ProgressionMetric.MostNearMissesInRun => metrics.NearMisses,
            ProgressionMetric.MostPowersInRun => metrics.PowerupsCollected,
            ProgressionMetric.LongestSurvivalTicks => metrics.SurvivalTicks,
            _ => throw new InvalidOperationException(
                "Broadcast Tour goals must use a single-run metric."),
        };
}
