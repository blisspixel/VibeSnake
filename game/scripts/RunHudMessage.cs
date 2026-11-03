using System.Globalization;
using System.Text.RegularExpressions;
using VibeSnake.Rules;

namespace VibeSnake.Game;

/// <summary>
/// Priority of the single second-row run message. A higher value replaces
/// the held message on the next presentation, including inside the hold.
/// Near-miss style awards stay flavour: they grant points, and treating
/// them as danger would hide the power timer during ordinary play.
/// </summary>
internal enum RunHudMessagePriority
{
    Flavour = 0,
    Offer = 1,
    Power = 2,
    Collision = 3,
    Starvation = 4,
}

internal readonly record struct RunHudMessage(
    string Text,
    RunHudMessagePriority Priority);

/// <summary>
/// Holds the current second-row message for at least 1.5 seconds of
/// readable time. A higher-priority message replaces it immediately.
/// The caller supplies the clock, so a paused run does not spend the hold.
/// </summary>
internal sealed class RunHudMessageDirector
{
    public const double MinimumHoldSeconds = 1.5;

    private RunHudMessage? _held;
    private double _heldAtSeconds;
    private bool _hasClock;

    public RunHudMessage? Current => _held;

    public void Reset()
    {
        _held = null;
        _heldAtSeconds = 0;
        _hasClock = false;
    }

    public RunHudMessage? Present(double nowSeconds, RunHudMessage? desired)
    {
        if (double.IsNaN(nowSeconds) || double.IsInfinity(nowSeconds))
        {
            throw new ArgumentOutOfRangeException(nameof(nowSeconds));
        }

        if (desired is { } candidate
            && (string.IsNullOrWhiteSpace(candidate.Text)
                || candidate.Text.Contains('\r')
                || candidate.Text.Contains('\n')
                || candidate.Text.Contains("  |  ", StringComparison.Ordinal)))
        {
            throw new ArgumentException("A run HUD message is one line and one fact.", nameof(desired));
        }

        if (_hasClock && nowSeconds < _heldAtSeconds)
        {
            throw new ArgumentOutOfRangeException(
                nameof(nowSeconds),
                nowSeconds,
                "Run HUD readable time cannot move backwards.");
        }

        if (desired is { } next && (_held is null || next.Priority > _held.Value.Priority))
        {
            Hold(next, nowSeconds);
            return _held;
        }

        if (desired is { } same && _held is { } current && same.Priority == current.Priority)
        {
            _held = same;
            _hasClock = true;
            return _held;
        }

        if (_held is not null && nowSeconds - _heldAtSeconds < MinimumHoldSeconds)
        {
            return _held;
        }

        if (desired is { } lower)
        {
            Hold(lower, nowSeconds);
            return _held;
        }

        _held = null;
        return null;
    }

    private void Hold(RunHudMessage message, double nowSeconds)
    {
        _held = message;
        _heldAtSeconds = nowSeconds;
        _hasClock = true;
    }
}

internal static class RunDifficultyBadge
{
    public static string CopyId(AdaptiveDifficultyState state) => state switch
    {
        AdaptiveDifficultyState.Support => "run.difficulty.easier",
        AdaptiveDifficultyState.Standard => "run.difficulty.standard",
        AdaptiveDifficultyState.Pressure => "run.difficulty.harder",
        AdaptiveDifficultyState.Disabled => "run.difficulty.fixed",
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, "Unknown adaptive difficulty state."),
    };
}

internal static class RadioStatusPresentation
{
    public const string NoAudioDevice = "NO AUDIO DEVICE";
    public const string Muted = "MUTED";

    public static bool IsDummyDriver(string? driverName) =>
        string.Equals(driverName?.Trim(), "Dummy", StringComparison.OrdinalIgnoreCase);

    public static string Present(string compactLine, bool dummyDriver, bool musicSilent)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(compactLine);
        if (!dummyDriver && !musicSilent)
        {
            return compactLine;
        }

        var status = dummyDriver ? NoAudioDevice : Muted;
        var separator = compactLine.LastIndexOf('|');
        if (separator < 0)
        {
            return status;
        }

        return string.Concat(compactLine.AsSpan(0, separator + 1), "  ", status);
    }

    public static string MuteLine(string policyMuteLine, bool dummyDriver, bool musicSilent)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(policyMuteLine);
        if (dummyDriver)
        {
            return "MUTE: " + NoAudioDevice;
        }

        if (musicSilent)
        {
            return "MUTE: MUSIC MUTED";
        }

        return policyMuteLine;
    }
}

/// <summary>
/// Chooses the one player-facing run message. Coordinates stay in the
/// engineering status used by replays and qualification, not here.
/// </summary>
internal static class RunHudMessages
{
    public const string BaitSet = "[T] BAIT SET: EAT THE NEXT FOOD TO TRIGGER";
    public const string ClassicRules = "CLASSIC RULES: ROUTE, EAT, GROW, WRAP, AVOID YOUR BODY";
    public static readonly float MessageLeft = 18.0f;
    public static readonly float MessageWidth = 864.0f;
    public static readonly float MessageTop = 53.0f;
    public static readonly float RadioLeft = 900.0f;
    public const int MessageBaseFontSize = 15;
    public const int MessageMinimumFontSize = 11;

    private static readonly Regex CoordinatePattern = new(
        @"\d+\s*,\s*\d+",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static RunHudMessagePriority PriorityForEvents(IReadOnlyList<RunEventDetail> events)
    {
        ArgumentNullException.ThrowIfNull(events);
        if (events.Any(detail => detail.Kind == RunEventKind.StarvationWarning))
        {
            return RunHudMessagePriority.Starvation;
        }

        if (events.Any(detail => detail.Kind is RunEventKind.CollisionPrevented or RunEventKind.Died))
        {
            return RunHudMessagePriority.Collision;
        }

        return RunHudMessagePriority.Flavour;
    }

    /// <summary>
    /// Priority of the sentence StepFeedback actually chose. A starvation or
    /// collision event can share a step with a power activation, and that
    /// activation keeps the caption. The words on screen decide the tier.
    /// </summary>
    public static RunHudMessagePriority PriorityForCopy(string copyId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(copyId);
        return copyId switch
        {
            "feedback.starvation-warning" => RunHudMessagePriority.Starvation,
            "feedback.power.shield-broke"
                or "feedback.power.last-stand-reversed"
                or "feedback.power.last-stand-window" => RunHudMessagePriority.Collision,
            _ => RunHudMessagePriority.Flavour,
        };
    }

    public static RunHudMessage? Desired(
        RunSnapshot snapshot,
        string? feedbackText,
        RunHudMessagePriority feedbackPriority,
        string? broadcastText,
        string? ghostText)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (feedbackPriority == RunHudMessagePriority.Starvation
            && !string.IsNullOrWhiteSpace(feedbackText))
        {
            return new RunHudMessage(feedbackText, RunHudMessagePriority.Starvation);
        }

        if (feedbackPriority == RunHudMessagePriority.Collision
            && !string.IsNullOrWhiteSpace(feedbackText))
        {
            return new RunHudMessage(feedbackText, RunHudMessagePriority.Collision);
        }

        if (TryPower(snapshot, out var power))
        {
            return power;
        }

        if (snapshot.PowerPickup is { } pickup)
        {
            return new RunHudMessage(DescribeOffer(snapshot, pickup), RunHudMessagePriority.Offer);
        }

        if (!string.IsNullOrWhiteSpace(feedbackText))
        {
            return new RunHudMessage(feedbackText, RunHudMessagePriority.Flavour);
        }

        if (!string.IsNullOrWhiteSpace(broadcastText))
        {
            return new RunHudMessage(broadcastText, RunHudMessagePriority.Flavour);
        }

        if (!string.IsNullOrWhiteSpace(ghostText))
        {
            return new RunHudMessage(ghostText, RunHudMessagePriority.Flavour);
        }

        return null;
    }

    public static IReadOnlyList<string> LayoutSamples()
    {
        var samples = new List<string> { BaitSet, ClassicRules, "STARVATION WARNING" };
        foreach (var kind in Enum.GetValues<PowerKind>())
        {
            var definition = PowerFeedbackCatalog.Find(kind);
            samples.Add($"[{definition.StableIcon}] {definition.Name} 99.0s");
            samples.Add(
                $"OFFER {PowerDecisionCatalog.Get(kind).Family.ToString().ToUpperInvariant()} "
                + $"[{definition.StableIcon}] {definition.Name} 99.0s "
                + definition.PickupTelegraph);
        }

        samples.Add("[L] RECOVERY IMMUNITY 99.0s");
        samples.Add("[L] LAST STAND HELD");
        // The live detach line carries the obstacle count, which the generic
        // power sample above does not.
        samples.Add("[D] DETACH x99 99.0s");
        samples.Add("GHOST SLOT 4  SCORE 999999  DELTA -999999  LENGTH 999");
        return samples;
    }

    public static bool ContainsBoardCoordinate(string text) =>
        CoordinatePattern.IsMatch(text);

    private static bool TryPower(RunSnapshot snapshot, out RunHudMessage message)
    {
        var interval = snapshot.EffectiveRulesStepMilliseconds;
        if (snapshot.HasLastStandRecovery)
        {
            message = Timed(
                PowerKind.LastStand,
                "RECOVERY IMMUNITY",
                snapshot.LastStandRecoveryTicksRemaining,
                interval);
            return true;
        }

        if (snapshot.LastStandHeld)
        {
            message = new RunHudMessage("[L] LAST STAND HELD", RunHudMessagePriority.Power);
            return true;
        }

        if (snapshot.HasShield)
        {
            message = Timed(PowerKind.Shield, null, snapshot.ShieldTicksRemaining, interval);
            return true;
        }

        if (snapshot.HasPhaseShift)
        {
            message = Timed(PowerKind.PhaseShift, null, snapshot.PhaseShiftTicksRemaining, interval);
            return true;
        }

        if (snapshot.HasSlowMo)
        {
            message = Timed(PowerKind.SlowMo, null, snapshot.SlowMoTicksRemaining, interval);
            return true;
        }

        if (snapshot.HasBoost)
        {
            message = Timed(PowerKind.Boost, null, snapshot.BoostTicksRemaining, interval);
            return true;
        }

        if (snapshot.HasMagnet)
        {
            message = Timed(PowerKind.Magnet, null, snapshot.MagnetTicksRemaining, interval);
            return true;
        }

        if (snapshot.HasGluttony)
        {
            message = Timed(PowerKind.Gluttony, null, snapshot.GluttonyTicksRemaining, interval);
            return true;
        }

        if (snapshot.HasBait)
        {
            message = new RunHudMessage(BaitSet, RunHudMessagePriority.Power);
            return true;
        }

        if (snapshot.HasDetachedObstacles)
        {
            message = Timed(
                PowerKind.SegmentDetach,
                $"DETACH x{snapshot.DetachedObstacles.Count}",
                snapshot.DetachedObstacleTicksRemaining,
                interval);
            return true;
        }

        message = default;
        return false;
    }

    private static RunHudMessage Timed(
        PowerKind kind,
        string? label,
        int ticksRemaining,
        int stepIntervalMilliseconds) =>
        new(
            $"[{PowerPresentation.Marker(kind)}] {label ?? PowerPresentation.ShortName(kind)} "
                + $"{Seconds(ticksRemaining, stepIntervalMilliseconds)}s",
            RunHudMessagePriority.Power);

    private static string DescribeOffer(RunSnapshot snapshot, PowerPickup pickup)
    {
        var definition = PowerFeedbackCatalog.Find(pickup.Kind);
        var decision = PowerDecisionCatalog.Get(pickup.Kind);
        var seconds = Seconds(pickup.VisibilityTicksRemaining, snapshot.EffectiveRulesStepMilliseconds);
        return $"OFFER {decision.Family.ToString().ToUpperInvariant()} "
            + $"[{definition.StableIcon}] {definition.Name} {seconds}s "
            + definition.PickupTelegraph;
    }

    private static string Seconds(int ticksRemaining, int stepIntervalMilliseconds) =>
        RulesCadenceClock.RemainingWallClockSeconds(ticksRemaining, stepIntervalMilliseconds)
            .ToString("0.0", CultureInfo.InvariantCulture);
}

internal static class RunHudMessageQualification
{
    public static void Verify()
    {
        var failures = new List<string>();
        VerifyDirector(failures);
        VerifySelection(failures);
        VerifyPlayerSentences(failures);
        VerifyRadio(failures);
        VerifyBadgeCopy(failures);
        if (RunHudMessages.MessageLeft + RunHudMessages.MessageWidth + 8.0f > RunHudMessages.RadioLeft)
        {
            failures.Add("message row overlaps the radio column");
        }

        if (failures.Count > 0)
        {
            throw new InvalidOperationException(
                "Run HUD message qualification failed: " + string.Join("; ", failures));
        }
    }

    private static void VerifyDirector(List<string> failures)
    {
        var director = new RunHudMessageDirector();
        var power = new RunHudMessage("[S] SHIELD 2.0s", RunHudMessagePriority.Power);
        var starvation = new RunHudMessage("STARVATION WARNING", RunHudMessagePriority.Starvation);
        var offer = new RunHudMessage("OFFER TEMPO [B] BOOST 1.0s", RunHudMessagePriority.Offer);
        if (director.Present(0, power) is not { Text: "[S] SHIELD 2.0s" })
        {
            failures.Add("director did not show the first message");
        }

        var replaced = director.Present(0, starvation);
        if (replaced is not { Text: "STARVATION WARNING" })
        {
            failures.Add("starvation did not replace a lower message within one frame");
        }

        if (director.Present(1, offer) is not { Text: "STARVATION WARNING" })
        {
            failures.Add("starvation hold ended before 1.5 seconds");
        }

        if (director.Present(RunHudMessageDirector.MinimumHoldSeconds, offer)
            is not { Priority: RunHudMessagePriority.Offer })
        {
            failures.Add("a lower message could not replace starvation after the hold");
        }

        director.Reset();
        director.Present(0, power);
        director.Present(1.0, new RunHudMessage("[S] SHIELD 1.0s", RunHudMessagePriority.Power));
        if (director.Present(1.2, offer) is not { Text: "[S] SHIELD 1.0s" })
        {
            failures.Add("a power timer update reset the hold");
        }

        if (director.Present(RunHudMessageDirector.MinimumHoldSeconds, null) is not null)
        {
            failures.Add("an expired message stayed after its source cleared");
        }

        var backwards = new RunHudMessageDirector();
        backwards.Present(2, power);
        if (!Throws<ArgumentOutOfRangeException>(() => backwards.Present(1, offer)))
        {
            failures.Add("backwards readable time was accepted");
        }
    }

    private static void VerifySelection(List<string> failures)
    {
        var bait = Snapshot(bait: new GridPoint(46, 25), pickup: new PowerPickup(
            PowerKind.Gluttony,
            new GridPoint(44, 10),
            24));
        var baitMessage = RunHudMessages.Desired(bait, null, RunHudMessagePriority.Flavour, null, null);
        if (baitMessage is not { Text: RunHudMessages.BaitSet, Priority: RunHudMessagePriority.Power }
            || RunHudMessages.ContainsBoardCoordinate(baitMessage.Value.Text)
            || baitMessage.Value.Text.Contains("DDA", StringComparison.Ordinal))
        {
            failures.Add("armed bait did not use the coordinate-free player sentence");
        }

        var shielded = Snapshot(shieldTicks: 40, bait: new GridPoint(46, 25));
        var shieldMessage = RunHudMessages.Desired(shielded, null, RunHudMessagePriority.Flavour, null, null);
        if (shieldMessage is not { Text: "[S] SHIELD 2.0s" })
        {
            failures.Add($"shield did not outrank bait: {shieldMessage?.Text}");
        }

        var boosted = Snapshot(shieldTicks: 40, boostTicks: 10);
        var boostedMessage = RunHudMessages.Desired(boosted, null, RunHudMessagePriority.Flavour, null, null);
        if (boostedMessage is not { Text: "[S] SHIELD 1.0s" })
        {
            failures.Add($"boost did not halve the shown shield time: {boostedMessage?.Text}");
        }

        var warning = RunHudMessages.Desired(
            shielded,
            "STARVATION WARNING",
            RunHudMessagePriority.Starvation,
            "broadcast",
            "ghost");
        if (warning is not { Text: "STARVATION WARNING", Priority: RunHudMessagePriority.Starvation })
        {
            failures.Add("starvation did not outrank the power, broadcast, and ghost lines");
        }

        var nearMiss = RunHudMessages.Desired(
            shielded,
            "+2 CLOSE CALL!",
            RunHudMessagePriority.Flavour,
            null,
            null);
        if (nearMiss is not { Priority: RunHudMessagePriority.Power })
        {
            failures.Add("a style near-miss hid the active power timer");
        }

        var collision = RunHudMessages.Desired(
            shielded,
            "SHIELD BROKE: COLLISION BLOCKED",
            RunHudMessagePriority.Collision,
            null,
            null);
        if (collision is not { Priority: RunHudMessagePriority.Collision })
        {
            failures.Add("a blocked collision did not outrank the power timer");
        }

        var quiet = RunHudMessages.Desired(
            Snapshot(),
            null,
            RunHudMessagePriority.Flavour,
            null,
            null);
        if (quiet is not null)
        {
            failures.Add("a quiet board invented a status line");
        }

        foreach (var sample in RunHudMessages.LayoutSamples())
        {
            if (sample.Contains("DDA", StringComparison.Ordinal)
                || RunHudMessages.ContainsBoardCoordinate(sample)
                || sample.Contains("  |  ", StringComparison.Ordinal))
            {
                failures.Add("layout sample is not one player message: " + sample);
            }
        }

        if (RunHudMessages.PriorityForEvents(
            [new RunEventDetail(RunEventKind.StarvationWarning, Value: 200)])
            != RunHudMessagePriority.Starvation)
        {
            failures.Add("starvation event was not classified as danger");
        }

        if (RunHudMessages.PriorityForEvents(
            [new RunEventDetail(RunEventKind.NearMiss, Value: 1)])
            != RunHudMessagePriority.Flavour)
        {
            failures.Add("near-miss style was classified as danger");
        }

        if (RunHudMessages.PriorityForCopy("feedback.starvation-warning")
            != RunHudMessagePriority.Starvation)
        {
            failures.Add("the starvation sentence was not danger");
        }

        foreach (var collisionCopyId in new[]
        {
            "feedback.power.shield-broke",
            "feedback.power.last-stand-reversed",
            "feedback.power.last-stand-window",
        })
        {
            if (RunHudMessages.PriorityForCopy(collisionCopyId) != RunHudMessagePriority.Collision)
            {
                failures.Add("a blocked collision sentence was not danger: " + collisionCopyId);
            }
        }

        if (RunHudMessages.PriorityForCopy("feedback.power.activation.shield")
            != RunHudMessagePriority.Flavour)
        {
            failures.Add("a power activation sentence was treated as danger");
        }

        var activation = RunHudMessages.Desired(
            Snapshot(shieldTicks: 40),
            "SHIELD ONLINE: 1 COLLISION BLOCK",
            RunHudMessages.PriorityForCopy("feedback.power.activation.shield"),
            null,
            null);
        if (activation is not { Text: "[S] SHIELD 2.0s" })
        {
            failures.Add("a power activation hid its own timer: " + activation?.Text);
        }
    }

    private static void VerifyPlayerSentences(List<string> failures)
    {
        foreach (var entry in ShellLocalization.All)
        {
            if (!entry.Id.StartsWith("feedback.", StringComparison.Ordinal)
                && !entry.Id.StartsWith("broadcast.", StringComparison.Ordinal)
                && entry.Id is not ("comparisons.ghost-hud"
                    or "status.unlock.saved"
                    or "status.unlock.saved-many"))
            {
                continue;
            }

            var arguments = entry.Parameters
                .Select(parameter => ShellTextArgument.From(parameter, "99"))
                .ToArray();
            RejectJoinedSentence(
                failures,
                entry.Id,
                ShellLocalization.Format(entry.Id, ShellLocale.English, arguments));
        }

        foreach (var definition in AchievementCatalog.Definitions)
        {
            RejectJoinedSentence(
                failures,
                definition.Id,
                ShellLocalization.Format(
                    "feedback.achievement",
                    ShellLocale.English,
                    ShellTextArgument.From("achievement", definition.Name.ToUpperInvariant())));
            RejectJoinedSentence(
                failures,
                definition.Id,
                ShellLocalization.Format(
                    "status.unlock.saved",
                    ShellLocale.English,
                    ShellTextArgument.From("unlock", definition.Id.ToUpperInvariant())));
        }
    }

    private static void RejectJoinedSentence(List<string> failures, string id, string text)
    {
        if (string.IsNullOrWhiteSpace(text)
            || text.Contains('\r')
            || text.Contains('\n')
            || text.Contains("  |  ", StringComparison.Ordinal))
        {
            failures.Add("player message is not one line: " + id);
        }
    }

    private static void VerifyRadio(List<string> failures)
    {
        const string playing = "RADIO: The Flow Signal  |  CHILL GRACEFUL MOLT  |  PLAYING";
        var dummy = RadioStatusPresentation.Present(playing, dummyDriver: true, musicSilent: false);
        if (!dummy.Contains(RadioStatusPresentation.NoAudioDevice, StringComparison.Ordinal)
            || dummy.Contains("PLAYING", StringComparison.Ordinal))
        {
            failures.Add("dummy driver still reads PLAYING: " + dummy);
        }

        var muted = RadioStatusPresentation.Present(playing, dummyDriver: false, musicSilent: true);
        if (!muted.EndsWith("MUTED", StringComparison.Ordinal) || muted.Contains("PLAYING", StringComparison.Ordinal))
        {
            failures.Add("silent music still reads PLAYING: " + muted);
        }

        var dummyWins = RadioStatusPresentation.Present(playing, dummyDriver: true, musicSilent: true);
        if (!dummyWins.Contains(RadioStatusPresentation.NoAudioDevice, StringComparison.Ordinal))
        {
            failures.Add("mute hid a missing audio device");
        }

        if (!string.Equals(playing, RadioStatusPresentation.Present(playing, false, false), StringComparison.Ordinal))
        {
            failures.Add("an audible radio line was rewritten");
        }

        if (RadioStatusPresentation.MuteLine("MUTE: MUSIC AUDIBLE", true, false)
            != "MUTE: NO AUDIO DEVICE")
        {
            failures.Add("the radio panel still calls a dummy driver audible");
        }
    }

    private static void VerifyBadgeCopy(List<string> failures)
    {
        foreach (var state in Enum.GetValues<AdaptiveDifficultyState>())
        {
            var english = ShellLocalization.Format(
                RunDifficultyBadge.CopyId(state),
                ShellLocale.English);
            if (english.Contains("DDA", StringComparison.Ordinal)
                || english.Contains(',', StringComparison.Ordinal))
            {
                failures.Add("difficulty badge is not player copy: " + english);
            }
        }

        var help = ShellLocalization.Format("onboarding.offer.difficulty", ShellLocale.English);
        foreach (var word in new[] { "EASIER", "STANDARD", "HARDER", "FIXED" })
        {
            if (!help.Contains(word, StringComparison.Ordinal))
            {
                failures.Add("help does not explain " + word);
            }
        }
    }

    private static RunSnapshot Snapshot(
        int shieldTicks = 0,
        int boostTicks = 0,
        GridPoint? bait = null,
        PowerPickup? pickup = null) =>
        new(
            Tick: 1,
            Status: RunStatus.Running,
            DeathCause: DeathCause.None,
            Direction: Direction.Right,
            Body: [new GridPoint(1, 1)],
            PendingDirections: [],
            Food: new GridPoint(2, 2),
            Score: 0,
            ComboCount: 0,
            ComboMultiplier: 1.0,
            TicksSinceLastFood: 0,
            HungerTicksRemaining: 100,
            HungerMaximumTicks: 600,
            HungerWarningTicks: 200,
            PowerPickup: pickup,
            PowerSpawnTicksElapsed: 0,
            ShieldTicksRemaining: shieldTicks,
            PhaseShiftTicksRemaining: 0,
            LastStandHeld: false,
            LastStandRecoveryTicksRemaining: 0,
            SlowMoTicksRemaining: 0,
            BoostTicksRemaining: boostTicks,
            MagnetTicksRemaining: 0,
            GluttonyTicksRemaining: 0,
            BaitPosition: bait,
            DetachedObstacles: [],
            DetachedObstacleTicksRemaining: 0,
            StateHash: "hud");

    private static bool Throws<TException>(Action action)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return true;
        }

        return false;
    }
}
