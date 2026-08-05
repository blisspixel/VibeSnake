using VibeSnake.Rules;

namespace VibeSnake.Game;

internal readonly record struct StepFeedback(AudioCue? Cue, string? Caption)
{
    public static StepFeedback Resolve(IReadOnlyList<RunEventDetail> events)
    {
        ArgumentNullException.ThrowIfNull(events);

        if (Contains(events, RunEventKind.CollisionPrevented, PowerKind.LastStand))
        {
            return new StepFeedback(
                AudioCue.PowerRecovery,
                "LAST STAND: DEATH REVERSED");
        }

        if (Contains(events, RunEventKind.CollisionPrevented, PowerKind.Shield))
        {
            return new StepFeedback(
                AudioCue.ShieldBreak,
                "SHIELD BROKE: COLLISION BLOCKED");
        }

        if (Contains(events, RunEventKind.PowerActivated, PowerKind.LastStand)
            && Contains(events, RunEventKind.PowerConsumed, PowerKind.LastStand))
        {
            return new StepFeedback(
                AudioCue.PowerRecovery,
                "LAST STAND RECOVERY WINDOW");
        }

        if (TryPowerEvent(events, RunEventKind.PowerActivated, out var activated))
        {
            return new StepFeedback(
                activated == PowerKind.Shield
                    ? AudioCue.ShieldActivate
                    : AudioCue.PowerActivate,
                ActivationCaption(activated));
        }

        if (TryPowerEvent(events, RunEventKind.PowerExpired, out var expired))
        {
            return new StepFeedback(
                expired == PowerKind.Shield
                    ? AudioCue.ShieldExpire
                    : AudioCue.PowerExpire,
                $"{PowerPresentation.ShortName(expired)} SIGNAL EXPIRED");
        }

        if (TryPowerEvent(events, RunEventKind.PowerDiscarded, out var discarded))
        {
            return new StepFeedback(
                discarded == PowerKind.Shield
                    ? AudioCue.ShieldExpire
                    : AudioCue.PowerExpire,
                $"{PowerPresentation.ShortName(discarded)} SIGNAL CLEARED");
        }

        if (TryPowerEvent(events, RunEventKind.PowerSpawned, out var spawned))
        {
            return new StepFeedback(
                spawned == PowerKind.Shield
                    ? AudioCue.ShieldSpawn
                    : AudioCue.PowerSpawn,
                $"{PowerPresentation.ShortName(spawned)} SIGNAL DETECTED");
        }

        // Achievement candidates outrank pressure/style cues (catalog priority 92).
        if (TryAchievementCaption(events, out var achievementCaption))
        {
            return new StepFeedback(AudioCue.Confirm, achievementCaption);
        }

        // Starvation pressure outranks near-miss style captions (catalog priority).
        if (events.Any(detail => detail.Kind == RunEventKind.StarvationWarning))
        {
            return new StepFeedback(AudioCue.Pause, "STARVATION WARNING");
        }

        if (TryNearMissCaption(events, out var nearMissCaption))
        {
            return new StepFeedback(AudioCue.Food, nearMissCaption);
        }

        if (events.Any(detail => detail.Kind == RunEventKind.ComboExpired))
        {
            return new StepFeedback(AudioCue.Pause, "COMBO EXPIRED");
        }

        return events.Any(detail => detail.Kind == RunEventKind.AteFood)
            ? new StepFeedback(AudioCue.Food, null)
            : default;
    }

    private static bool TryAchievementCaption(
        IReadOnlyList<RunEventDetail> events,
        out string caption)
    {
        foreach (var detail in events)
        {
            if (detail.Kind != RunEventKind.AchievementCandidate
                || detail.Value is not int index)
            {
                continue;
            }

            var definition = AchievementCatalog.DefinitionAt(index);
            if (definition is null)
            {
                continue;
            }

            caption = "ACHIEVEMENT: " + definition.Name.ToUpperInvariant();
            return true;
        }

        caption = string.Empty;
        return false;
    }

    private static bool TryNearMissCaption(
        IReadOnlyList<RunEventDetail> events,
        out string caption)
    {
        foreach (var detail in events)
        {
            if (detail.Kind != RunEventKind.NearMiss || detail.Value is null or <= 0)
            {
                continue;
            }

            // Spatial body-proximity uses a grid position; food style/clutch does not.
            if (detail.Position is null)
            {
                caption = detail.Value >= 2
                    ? $"+{detail.Value} STYLE STREAK!"
                    : $"+{detail.Value} CLUTCH!";
            }
            else if (detail.Value >= 2)
            {
                caption = $"+{detail.Value} THREADING THE NEEDLE!";
            }
            else
            {
                caption = $"+{detail.Value} CLOSE CALL!";
            }

            return true;
        }

        caption = string.Empty;
        return false;
    }

    private static string ActivationCaption(PowerKind power) => power switch
    {
        PowerKind.Shield => "SHIELD ONLINE: 1 COLLISION BLOCK",
        PowerKind.PhaseShift => "PHASE SHIFT ONLINE: BODY PASS",
        PowerKind.LastStand => "LAST STAND ARMED",
        PowerKind.SlowMo => "SLOW-MO ONLINE: HALF STEP RATE",
        PowerKind.Boost => "BOOST ONLINE: DOUBLE STEP RATE",
        PowerKind.Magnet => "MAGNET ONLINE: FOOD PULL",
        PowerKind.Bait => "BAIT MARKED: NEXT FOOD PULL",
        PowerKind.Gluttony => "GLUTTONY ONLINE: EAT WITHOUT GROWTH",
        PowerKind.SegmentDetach => "SEGMENTS DETACHED: TIMED HAZARDS",
        _ => throw new ArgumentOutOfRangeException(nameof(power), power, "Unknown power kind."),
    };

    private static bool Contains(
        IEnumerable<RunEventDetail> events,
        RunEventKind kind,
        PowerKind power) =>
        events.Any(detail => detail.Kind == kind && detail.Power == power);

    private static bool TryPowerEvent(
        IReadOnlyList<RunEventDetail> events,
        RunEventKind kind,
        out PowerKind power)
    {
        foreach (var detail in events)
        {
            if (detail.Kind == kind && detail.Power is { } matched)
            {
                power = matched;
                return true;
            }
        }

        power = default;
        return false;
    }
}
