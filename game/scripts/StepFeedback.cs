using VibeSnake.Rules;

namespace VibeSnake.Game;

internal readonly record struct StepFeedback(AudioCue? Cue, ShellTextReference? Text)
{
    public static StepFeedback Resolve(
        IReadOnlyList<RunEventDetail> events,
        int comboCount = 0,
        VibeLevelTransition? vibeTransition = null)
    {
        ArgumentNullException.ThrowIfNull(events);
        ArgumentOutOfRangeException.ThrowIfNegative(comboCount);

        if (Contains(events, RunEventKind.CollisionPrevented, PowerKind.LastStand))
        {
            return Localized(
                AudioCue.PowerRecovery,
                "feedback.power.last-stand-reversed");
        }

        if (Contains(events, RunEventKind.CollisionPrevented, PowerKind.Shield))
        {
            return Localized(
                AudioCue.ShieldBreak,
                "feedback.power.shield-broke");
        }

        if (Contains(events, RunEventKind.PowerActivated, PowerKind.LastStand)
            && Contains(events, RunEventKind.PowerConsumed, PowerKind.LastStand))
        {
            return Localized(
                AudioCue.PowerRecovery,
                "feedback.power.last-stand-window");
        }

        if (TryPowerEvent(events, RunEventKind.PowerActivated, out var activated))
        {
            return Localized(
                ActivationCue(activated),
                ActivationCopyId(activated));
        }

        if (TryPowerEvent(events, RunEventKind.PowerExpired, out var expired))
        {
            return Localized(
                expired == PowerKind.Shield
                    ? AudioCue.ShieldExpire
                    : AudioCue.PowerExpire,
                "feedback.power.expired",
                ShellTextArgument.From("power", PowerPresentation.ShortName(expired)));
        }

        if (TryPowerEvent(events, RunEventKind.PowerDiscarded, out var discarded))
        {
            return Localized(
                discarded == PowerKind.Shield
                    ? AudioCue.ShieldExpire
                    : AudioCue.PowerExpire,
                "feedback.power.cleared",
                ShellTextArgument.From("power", PowerPresentation.ShortName(discarded)));
        }

        if (TryPowerEvent(events, RunEventKind.PowerSpawned, out var spawned))
        {
            return Localized(
                spawned == PowerKind.Shield
                    ? AudioCue.ShieldSpawn
                    : AudioCue.PowerSpawn,
                "feedback.power.detected",
                ShellTextArgument.From("power", PowerPresentation.ShortName(spawned)));
        }

        // Achievement candidates outrank pressure/style cues (catalog priority 92).
        if (TryAchievementName(events, out var achievementName))
        {
            return Localized(
                AudioCue.Achievement,
                "feedback.achievement",
                ShellTextArgument.From("achievement", achievementName.ToUpperInvariant()));
        }

        // Starvation pressure outranks near-miss style captions (catalog priority).
        if (events.Any(detail => detail.Kind == RunEventKind.StarvationWarning))
        {
            return Localized(AudioCue.Starvation, "feedback.starvation-warning");
        }

        if (events.Any(detail => detail.Kind == RunEventKind.ComboExpired)
            && vibeTransition is { Cause: VibeTransitionCause.ComboBreak })
        {
            return Localized(vibeTransition.Stinger, "feedback.combo-expired");
        }

        if (events.Any(detail => detail.Kind == RunEventKind.AteFood)
            && vibeTransition is { Cause: VibeTransitionCause.Escalation })
        {
            var definition = VibeLevelDirector.Find(vibeTransition.To);
            return Localized(
                vibeTransition.Stinger,
                "feedback.combo-level",
                ShellTextArgument.From("count", definition.ComboThreshold),
                ShellTextArgument.From("level", definition.Name));
        }

        if (TryNearMissText(events, out var nearMissText))
        {
            return new StepFeedback(AudioCue.Food, nearMissText);
        }

        return events.Any(detail => detail.Kind == RunEventKind.AteFood)
            ? new StepFeedback(AudioCue.Food, null)
            : default;
    }

    public static AudioCue ActivationCue(PowerKind power) =>
        PowerFeedbackCatalog.Find(power).ActivationCue;

    private static bool TryAchievementName(
        IReadOnlyList<RunEventDetail> events,
        out string name)
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

            name = definition.Name;
            return true;
        }

        name = string.Empty;
        return false;
    }

    private static bool TryNearMissText(
        IReadOnlyList<RunEventDetail> events,
        out ShellTextReference text)
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
                text = ShellTextReference.Create(
                    detail.Value >= 2
                        ? "feedback.near-miss.style-streak"
                        : "feedback.near-miss.clutch",
                    ShellTextArgument.From("points", detail.Value));
            }
            else if (detail.Value >= 2)
            {
                text = ShellTextReference.Create(
                    "feedback.near-miss.threading",
                    ShellTextArgument.From("points", detail.Value));
            }
            else
            {
                text = ShellTextReference.Create(
                    "feedback.near-miss.close-call",
                    ShellTextArgument.From("points", detail.Value));
            }

            return true;
        }

        text = default;
        return false;
    }

    private static string ActivationCopyId(PowerKind power) => power switch
    {
        PowerKind.Shield => "feedback.power.activation.shield",
        PowerKind.PhaseShift => "feedback.power.activation.phase-shift",
        PowerKind.LastStand => "feedback.power.activation.last-stand",
        PowerKind.SlowMo => "feedback.power.activation.slow-mo",
        PowerKind.Boost => "feedback.power.activation.boost",
        PowerKind.Magnet => "feedback.power.activation.magnet",
        PowerKind.Bait => "feedback.power.activation.bait",
        PowerKind.Gluttony => "feedback.power.activation.gluttony",
        PowerKind.SegmentDetach => "feedback.power.activation.segment-detach",
        _ => throw new ArgumentOutOfRangeException(nameof(power), power, "Unknown power kind."),
    };

    private static StepFeedback Localized(
        AudioCue cue,
        string copyId,
        params ShellTextArgument[] arguments) =>
        new(cue, ShellTextReference.Create(copyId, arguments));

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
