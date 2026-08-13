using VibeSnake.Rules;

namespace VibeSnake.AgentPlay;

public static class AgentBurstPolicy
{
    public const string Contract = "decision-event-stop-v1";

    private static readonly HashSet<RunEventKind> DecisionEvents =
    [
        RunEventKind.Wrapped,
        RunEventKind.AteFood,
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

    public static IReadOnlyList<RunEventKind> Stops { get; } =
        Array.AsReadOnly(DecisionEvents.Order().ToArray());

    public static bool TryGetStopEvent(
        IReadOnlyList<RunEventDetail> events,
        out RunEventKind stopEvent)
    {
        ArgumentNullException.ThrowIfNull(events);
        foreach (var item in events)
        {
            if (DecisionEvents.Contains(item.Kind))
            {
                stopEvent = item.Kind;
                return true;
            }
        }

        stopEvent = default;
        return false;
    }
}
