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
    ];

    public static bool IsKnown(RunEventKind kind) =>
        OrderedKinds.Contains(kind);

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
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown event kind."),
    };
}
