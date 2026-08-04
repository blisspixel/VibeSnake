namespace VibeSnake.Rules;

/// <summary>
/// Closed catalog of ordered step event kinds. Presentation and tooling must
/// treat unknown kinds as non-fatal display failures, not as rules mutations.
/// </summary>
public static class RulesEventCatalog
{
    public static readonly IReadOnlyList<RunEventKind> OrderedKinds =
    [
        RunEventKind.DirectionChanged,
        RunEventKind.Moved,
        RunEventKind.Wrapped,
        RunEventKind.AteFood,
        RunEventKind.ScoreChanged,
        RunEventKind.HungerReset,
        RunEventKind.Died,
        RunEventKind.Won,
        RunEventKind.PowerSpawned,
        RunEventKind.PowerCollected,
        RunEventKind.PowerActivated,
        RunEventKind.PowerExpired,
        RunEventKind.PowerConsumed,
        RunEventKind.PowerDiscarded,
        RunEventKind.CollisionPrevented,
        RunEventKind.NearMiss,
        RunEventKind.StarvationWarning,
        RunEventKind.ComboExpired,
        RunEventKind.AchievementCandidate,
    ];

    public static bool IsKnown(RunEventKind kind) =>
        OrderedKinds.Contains(kind);

    /// <summary>
    /// Returns the highest-priority event kind present in <paramref name="kinds"/>,
    /// or null when the sequence is empty.
    /// </summary>
    public static RunEventKind? SelectPrimaryKind(IEnumerable<RunEventKind> kinds)
    {
        ArgumentNullException.ThrowIfNull(kinds);
        RunEventKind? best = null;
        var bestPriority = int.MinValue;
        foreach (var kind in kinds)
        {
            var priority = PresentationPriority(kind);
            if (priority > bestPriority)
            {
                bestPriority = priority;
                best = kind;
            }
        }

        return best;
    }

    /// <summary>
    /// Declared relative priority for presentation caption selection when multiple
    /// events share a step. Higher values win. Not a substitute for ordered event lists.
    /// </summary>
    public static int PresentationPriority(RunEventKind kind) => kind switch
    {
        RunEventKind.Died => 100,
        RunEventKind.Won => 95,
        RunEventKind.AchievementCandidate => 92,
        RunEventKind.CollisionPrevented => 90,
        RunEventKind.PowerActivated => 80,
        RunEventKind.PowerConsumed => 75,
        RunEventKind.PowerCollected => 72,
        RunEventKind.PowerExpired => 70,
        RunEventKind.PowerDiscarded => 65,
        RunEventKind.PowerSpawned => 60,
        RunEventKind.StarvationWarning => 55,
        RunEventKind.NearMiss => 50,
        RunEventKind.ComboExpired => 45,
        RunEventKind.AteFood => 40,
        RunEventKind.ScoreChanged => 35,
        RunEventKind.HungerReset => 30,
        RunEventKind.Wrapped => 20,
        RunEventKind.Moved => 10,
        RunEventKind.DirectionChanged => 5,
        _ => 0,
    };

    public static string ToWireName(RunEventKind kind) => kind switch
    {
        RunEventKind.DirectionChanged => "direction_changed",
        RunEventKind.Moved => "moved",
        RunEventKind.Wrapped => "wrapped",
        RunEventKind.AteFood => "ate_food",
        RunEventKind.ScoreChanged => "score_changed",
        RunEventKind.HungerReset => "hunger_reset",
        RunEventKind.Died => "died",
        RunEventKind.Won => "won",
        RunEventKind.PowerSpawned => "power_spawned",
        RunEventKind.PowerCollected => "power_collected",
        RunEventKind.PowerActivated => "power_activated",
        RunEventKind.PowerExpired => "power_expired",
        RunEventKind.PowerConsumed => "power_consumed",
        RunEventKind.PowerDiscarded => "power_discarded",
        RunEventKind.CollisionPrevented => "collision_prevented",
        RunEventKind.NearMiss => "near_miss",
        RunEventKind.StarvationWarning => "starvation_warning",
        RunEventKind.ComboExpired => "combo_expired",
        RunEventKind.AchievementCandidate => "achievement_candidate",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown event kind."),
    };
}
