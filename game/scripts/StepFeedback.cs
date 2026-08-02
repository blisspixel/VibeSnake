using VibeSnake.Rules;

namespace VibeSnake.Game;

internal readonly record struct StepFeedback(AudioCue? Cue, string? Caption)
{
    public static StepFeedback Resolve(IReadOnlyList<RunEventDetail> events)
    {
        ArgumentNullException.ThrowIfNull(events);

        if (Contains(events, RunEventKind.CollisionPrevented, PowerKind.Shield))
        {
            return new StepFeedback(
                AudioCue.ShieldBreak,
                "SHIELD BROKE: COLLISION BLOCKED");
        }

        if (Contains(events, RunEventKind.PowerActivated, PowerKind.Shield))
        {
            return new StepFeedback(
                AudioCue.ShieldActivate,
                "SHIELD ONLINE: 1 COLLISION BLOCK");
        }

        if (Contains(events, RunEventKind.PowerExpired, PowerKind.Shield))
        {
            return new StepFeedback(
                AudioCue.ShieldExpire,
                "SHIELD SIGNAL EXPIRED");
        }

        if (Contains(events, RunEventKind.PowerDiscarded, PowerKind.Shield))
        {
            return new StepFeedback(
                AudioCue.ShieldExpire,
                "SHIELD SIGNAL CLEARED");
        }

        if (Contains(events, RunEventKind.PowerSpawned, PowerKind.Shield))
        {
            return new StepFeedback(
                AudioCue.ShieldSpawn,
                "SHIELD SIGNAL DETECTED");
        }

        return events.Any(detail => detail.Kind == RunEventKind.AteFood)
            ? new StepFeedback(AudioCue.Food, null)
            : default;
    }

    private static bool Contains(
        IEnumerable<RunEventDetail> events,
        RunEventKind kind,
        PowerKind power) =>
        events.Any(detail => detail.Kind == kind && detail.Power == power);
}
