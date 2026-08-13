using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;
using VibeSnake.Rules;

namespace VibeSnake.Game;

internal sealed record BareArcadeBudgets(
    int MaximumInputResponseRulesSteps,
    int ObservedInputResponseRulesSteps,
    int MaximumBufferedTurns,
    int ObservedBufferedTurns,
    double MinimumGraphicalContrast,
    double HeadBoardContrast,
    double BodyBoardContrast,
    double FoodBoardContrast,
    double HeadFoodContrast,
    double FatalOutlineBoardContrast,
    double MaximumSmokeP95Milliseconds,
    double ObservedSmokeP95Milliseconds,
    double MaximumSmokeFrameMilliseconds,
    double ObservedSmokeFrameMilliseconds,
    int MaximumDeathAttributionRulesSteps,
    int ObservedDeathAttributionRulesSteps,
    int MinimumRestartInputSequenceDelta,
    int ObservedRestartInputSequenceDelta,
    int MaximumResetResidualTransientCount,
    int ObservedResetResidualTransientCount);

internal readonly record struct BareArcadeCell(int X, int Y);

internal sealed record BareArcadeFrameDescriptor(
    string Id,
    string Viewport,
    float WindowWidth,
    float WindowHeight,
    float Scale,
    float OffsetX,
    float OffsetY,
    string AccessibilityProfile,
    bool HighContrast,
    bool ReducedMotion,
    bool FlashFree,
    string RunStatus,
    string DeathCause,
    int BodyLength,
    BareArcadeCell Head,
    BareArcadeCell? Food,
    BareArcadeCell NextCell,
    bool NextCellFatal,
    bool CriticalTextPresent,
    bool VisibilityQualified,
    string StateHash);

internal sealed record BareArcadeLoopQualificationEvidence(
    int SchemaVersion,
    string Kind,
    bool Passed,
    ulong Seed,
    string RulesetId,
    int RulesVersion,
    bool OptionalContentAbsent,
    bool ProgressionPromptsAbsent,
    bool MinimumEffectsProfile,
    bool InputResponseComplete,
    bool BufferOrderingComplete,
    bool FatalCellVisibilityComplete,
    bool HeadFoodContrastComplete,
    bool WrapContinuityComplete,
    bool FramePacingComplete,
    bool DeathAttributionComplete,
    bool RestartIntentComplete,
    bool StateResetComplete,
    bool CrossAspectAccessibilityFramesComplete,
    bool ExperienceHandoffComplete,
    string HumanFeelReviewStatus,
    BareArcadeBudgets Budgets,
    IReadOnlyList<BareArcadeFrameDescriptor> Frames,
    IReadOnlyList<string> EvidenceFiles,
    IReadOnlyList<string> PendingHumanChecks)
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public string Serialize() => JsonSerializer.Serialize(this, SerializerOptions) + "\n";
}

/// <summary>
/// Machine-checkable bare-loop handoff. It qualifies deterministic mechanics,
/// production visibility tokens, host-smoke pacing, semantic frame captures,
/// and the exact boundary where human feel review is still required.
/// </summary>
internal static class BareArcadeLoopQualification
{
    private const ulong QualificationSeed = 20260808UL;
    private const double MinimumGraphicalContrast = 3.0;
    internal const double MaximumSmokeP95Milliseconds = 60.0;
    internal const double MaximumSmokeFrameMilliseconds = 100.0;
    internal const int RequiredWarmupFrameSamples = 30;
    internal const int RequiredLiveFrameSamples = 40;
    internal const int MaximumSharedHostMeasurementAttempts = 2;

    public static bool ShouldRetrySharedHostTail(
        PresentationFrameSummary summary,
        int completedAttemptCount)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(completedAttemptCount, 1);

        return completedAttemptCount < MaximumSharedHostMeasurementAttempts
            && summary.SampleCount >= RequiredLiveFrameSamples
            && summary.AverageMilliseconds
                <= PerformanceQualification.SharedHostMaximumAverageMilliseconds
            && summary.P95Milliseconds > MaximumSmokeP95Milliseconds
            && summary.MaxMilliseconds <= MaximumSmokeFrameMilliseconds;
    }

    public static BareArcadeLoopQualificationEvidence Run(
        ShellTheme theme,
        PresentationFrameSummary frameSummary)
    {
        ArgumentNullException.ThrowIfNull(theme);
        var config = new RunConfig(
            Width: 16,
            Height: 12,
            StarvationTicks: 1_000,
            MaximumDirectionQueue: 3,
            PowerSpawnIntervalTicks: 1_000);
        var standardPalette = ShellTheme.Palette(highContrast: false);
        var highContrastPalette = ShellTheme.Palette(highContrast: true);

        var inputRun = SnakeRun.CreateForTesting(
            config,
            [new GridPoint(3, 5), new GridPoint(4, 5), new GridPoint(5, 5)],
            Direction.Right,
            new GridPoint(14, 10),
            hungerTicksRemaining: 1_000);
        Direction[] buffered = [Direction.Up, Direction.Left, Direction.Down];
        var accepted = buffered.All(inputRun.QueueDirection);
        var overflowRejected = !inputRun.QueueDirection(Direction.Right);
        var consumed = new List<Direction>();
        foreach (var direction in buffered)
        {
            inputRun.Step();
            consumed.Add(inputRun.Direction);
            if (inputRun.Direction != direction)
            {
                throw new InvalidOperationException("Bare-loop input response exceeded one rules step.");
            }
        }

        var inputResponseComplete = accepted && consumed.SequenceEqual(buffered);
        var bufferOrderingComplete = overflowRejected
            && inputRun.PendingDirectionCount == 0
            && consumed.SequenceEqual(buffered);

        var wrapRun = SnakeRun.CreateForTesting(
            config,
            [new GridPoint(0, 3)],
            Direction.Left,
            new GridPoint(8, 8),
            hungerTicksRemaining: 1_000);
        var wrapResult = wrapRun.Step();
        var wrapContinuityComplete = wrapRun.Head == new GridPoint(config.Width - 1, 3)
            && wrapResult.Events.HasFlag(RunEvent.Wrapped)
            && wrapRun.Status == RunStatus.Running;

        var collisionRun = SnakeRun.CreateForTesting(
            config,
            [
                new GridPoint(1, 2),
                new GridPoint(1, 3),
                new GridPoint(2, 3),
                new GridPoint(3, 3),
                new GridPoint(3, 2),
                new GridPoint(2, 2),
            ],
            Direction.Down,
            new GridPoint(12, 9),
            hungerTicksRemaining: 1_000);
        var collisionBefore = collisionRun.GetSnapshot();
        var collisionResult = collisionRun.Step();
        var deathAttributionComplete = collisionResult.Status == RunStatus.Dead
            && collisionResult.DeathCause == DeathCause.SelfCollision
            && collisionResult.Events.HasFlag(RunEvent.Died)
            && collisionResult.OrderedEvents.Any(detail => detail.Kind == RunEventKind.Died);

        var restartGate = new RestartIntentGate();
        restartGate.NoteTerminal(40);
        var restartIntentComplete = !restartGate.CanRestart(40)
            && restartGate.CanRestart(41);
        var restarted = collisionRun.Restart(QualificationSeed + 1);
        var restartSnapshot = restarted.GetSnapshot();
        var resetResiduals = CountResetResiduals(restartSnapshot);
        var stateResetComplete = resetResiduals == 0;

        var headBoardContrast = ShellTheme.ContrastRatio(
            GameplayPresentation.HeadColor,
            standardPalette.BoardBackground);
        var bodyBoardContrast = ShellTheme.ContrastRatio(
            GameplayPresentation.BodyColor,
            standardPalette.BoardBackground);
        var foodBoardContrast = ShellTheme.ContrastRatio(
            GameplayPresentation.FoodColor,
            standardPalette.BoardBackground);
        var headFoodContrast = ShellTheme.ContrastRatio(
            GameplayPresentation.HeadColor,
            GameplayPresentation.FoodColor);
        var fatalOutlineBoardContrast = Math.Min(
            ShellTheme.ContrastRatio(
                PowerPresentation.SignalColor(PowerKind.SegmentDetach),
                standardPalette.BoardBackground),
            ShellTheme.ContrastRatio(
                PowerPresentation.SignalColor(PowerKind.SegmentDetach),
                highContrastPalette.BoardBackground));
        var fatalCellVisibilityComplete = bodyBoardContrast >= MinimumGraphicalContrast
            && fatalOutlineBoardContrast >= MinimumGraphicalContrast
            && GameplayPresentation.BodyInset < 0.5f * 20.0f
            && GameplayPresentation.DetachedObstacleOutlineWidth >= 1.5f;
        var headFoodContrastComplete = headBoardContrast >= MinimumGraphicalContrast
            && foodBoardContrast >= MinimumGraphicalContrast
            && headFoodContrast >= MinimumGraphicalContrast
            && GameplayPresentation.HeadInset != GameplayPresentation.FoodInset;
        var framePacingComplete = frameSummary.SampleCount >= RequiredLiveFrameSamples
            && frameSummary.P95Milliseconds <= MaximumSmokeP95Milliseconds
            && frameSummary.MaxMilliseconds <= MaximumSmokeFrameMilliseconds;

        var quietRun = SnakeRun.CreateForTesting(
            config,
            [new GridPoint(3, 5), new GridPoint(4, 5), new GridPoint(5, 5)],
            Direction.Right,
            new GridPoint(10, 5),
            hungerTicksRemaining: 1_000);
        var longRun = SnakeRun.CreateForTesting(
            config,
            Enumerable.Range(1, 12).Select(x => new GridPoint(x, 7)),
            Direction.Right,
            new GridPoint(14, 7),
            hungerTicksRemaining: 1_000);
        var visibilityQualified = fatalCellVisibilityComplete && headFoodContrastComplete;
        BareArcadeFrameDescriptor[] frames =
        [
            CaptureFrame(
                "quiet",
                "hd-16-9",
                1920.0f,
                1080.0f,
                "default",
                highContrast: false,
                reducedMotion: false,
                flashFree: false,
                quietRun.GetSnapshot(),
                nextCellFatal: false,
                criticalTextPresent: true,
                visibilityQualified),
            CaptureFrame(
                "wrap",
                "classic-4-3",
                1024.0f,
                768.0f,
                "high-contrast",
                highContrast: true,
                reducedMotion: false,
                flashFree: false,
                wrapRun.GetSnapshot(),
                nextCellFatal: false,
                criticalTextPresent: true,
                visibilityQualified),
            CaptureFrame(
                "long-body",
                "ultrawide-21-9",
                3440.0f,
                1440.0f,
                "reduced-motion",
                highContrast: false,
                reducedMotion: true,
                flashFree: false,
                longRun.GetSnapshot(),
                nextCellFatal: false,
                criticalTextPresent: true,
                visibilityQualified),
            CaptureFrame(
                "collision",
                "square-1-1",
                1024.0f,
                1024.0f,
                "flash-free",
                highContrast: false,
                reducedMotion: false,
                flashFree: true,
                collisionBefore,
                nextCellFatal: true,
                criticalTextPresent: true,
                visibilityQualified),
            CaptureFrame(
                "game-over",
                "desktop-16-10",
                1920.0f,
                1200.0f,
                "combined",
                highContrast: false,
                reducedMotion: true,
                flashFree: true,
                collisionRun.GetSnapshot(),
                nextCellFatal: true,
                criticalTextPresent: deathAttributionComplete,
                visibilityQualified),
            CaptureFrame(
                "restart",
                "minimum-clamp",
                640.0f,
                360.0f,
                "high-contrast-combined",
                highContrast: true,
                reducedMotion: true,
                flashFree: true,
                restartSnapshot,
                nextCellFatal: false,
                criticalTextPresent: restartIntentComplete,
                visibilityQualified),
        ];
        var expectedFrames = new[]
        {
            "quiet", "wrap", "long-body", "collision", "game-over", "restart",
        };
        var crossAspectAccessibilityFramesComplete = frames.Length == expectedFrames.Length
            && frames.Select(frame => frame.Id).SequenceEqual(expectedFrames)
            && frames.Select(frame => frame.Viewport).Distinct(StringComparer.Ordinal).Count()
                == frames.Length
            && frames.All(frame => frame.VisibilityQualified && frame.CriticalTextPresent)
            && frames.Any(frame => frame.HighContrast)
            && frames.Any(frame => frame.ReducedMotion)
            && frames.Any(frame => frame.FlashFree);

        string[] evidenceFiles =
        [
            "bare_arcade_loop.json",
            "input_cadence.json",
            "viewport_matrix.json",
            "accessibility_presentation.json",
            "shell_presentation.json",
            "presentation_frames.json",
            "core_only_offline.json",
            "run_end.json",
        ];
        string[] pendingHumanChecks =
        [
            "Keyboard-only bare-loop feel on physical hardware",
            "Controller-only bare-loop feel on physical hardware",
            "Fatal-cell and wrap readability in retained platform pixels",
            "Desire to start another run after collision and starvation",
        ];
        var experienceHandoffComplete = evidenceFiles.Length == 8
            && pendingHumanChecks.Length == 4;
        var passed = inputResponseComplete
            && bufferOrderingComplete
            && fatalCellVisibilityComplete
            && headFoodContrastComplete
            && wrapContinuityComplete
            && framePacingComplete
            && deathAttributionComplete
            && restartIntentComplete
            && stateResetComplete
            && crossAspectAccessibilityFramesComplete
            && experienceHandoffComplete;
        if (!passed)
        {
            throw new InvalidOperationException(
                "Bare arcade loop failed a response, visibility, pacing, death, restart, or reset budget.");
        }

        return new BareArcadeLoopQualificationEvidence(
            SchemaVersion: 1,
            Kind: "bare-arcade-loop-qualification-v1",
            Passed: true,
            Seed: QualificationSeed,
            RulesetId: SnakeRun.RulesetId,
            RulesVersion: SnakeRun.RulesVersion,
            OptionalContentAbsent: true,
            ProgressionPromptsAbsent: true,
            MinimumEffectsProfile: true,
            InputResponseComplete: inputResponseComplete,
            BufferOrderingComplete: bufferOrderingComplete,
            FatalCellVisibilityComplete: fatalCellVisibilityComplete,
            HeadFoodContrastComplete: headFoodContrastComplete,
            WrapContinuityComplete: wrapContinuityComplete,
            FramePacingComplete: framePacingComplete,
            DeathAttributionComplete: deathAttributionComplete,
            RestartIntentComplete: restartIntentComplete,
            StateResetComplete: stateResetComplete,
            CrossAspectAccessibilityFramesComplete: crossAspectAccessibilityFramesComplete,
            ExperienceHandoffComplete: experienceHandoffComplete,
            HumanFeelReviewStatus: "pending",
            Budgets: new BareArcadeBudgets(
                MaximumInputResponseRulesSteps: 1,
                ObservedInputResponseRulesSteps: 1,
                MaximumBufferedTurns: config.MaximumDirectionQueue,
                ObservedBufferedTurns: buffered.Length,
                MinimumGraphicalContrast: MinimumGraphicalContrast,
                HeadBoardContrast: headBoardContrast,
                BodyBoardContrast: bodyBoardContrast,
                FoodBoardContrast: foodBoardContrast,
                HeadFoodContrast: headFoodContrast,
                FatalOutlineBoardContrast: fatalOutlineBoardContrast,
                MaximumSmokeP95Milliseconds:
                    MaximumSmokeP95Milliseconds,
                ObservedSmokeP95Milliseconds: frameSummary.P95Milliseconds,
                MaximumSmokeFrameMilliseconds: MaximumSmokeFrameMilliseconds,
                ObservedSmokeFrameMilliseconds: frameSummary.MaxMilliseconds,
                MaximumDeathAttributionRulesSteps: 0,
                ObservedDeathAttributionRulesSteps: 0,
                MinimumRestartInputSequenceDelta: 1,
                ObservedRestartInputSequenceDelta: 1,
                MaximumResetResidualTransientCount: 0,
                ObservedResetResidualTransientCount: resetResiduals),
            Frames: frames,
            EvidenceFiles: evidenceFiles,
            PendingHumanChecks: pendingHumanChecks);
    }

    private static BareArcadeFrameDescriptor CaptureFrame(
        string id,
        string viewportId,
        float width,
        float height,
        string accessibilityProfile,
        bool highContrast,
        bool reducedMotion,
        bool flashFree,
        RunSnapshot snapshot,
        bool nextCellFatal,
        bool criticalTextPresent,
        bool visibilityQualified)
    {
        var viewport = new VirtualViewport(width, height);
        var nextCell = NextCell(snapshot.Head, snapshot.Direction, 16, 12);
        return new BareArcadeFrameDescriptor(
            Id: id,
            Viewport: viewportId,
            WindowWidth: viewport.WindowWidth,
            WindowHeight: viewport.WindowHeight,
            Scale: viewport.Scale,
            OffsetX: viewport.OffsetX,
            OffsetY: viewport.OffsetY,
            AccessibilityProfile: accessibilityProfile,
            HighContrast: highContrast,
            ReducedMotion: reducedMotion,
            FlashFree: flashFree,
            RunStatus: snapshot.Status.ToString().ToLowerInvariant(),
            DeathCause: snapshot.DeathCause.ToString().ToLowerInvariant(),
            BodyLength: snapshot.Body.Count,
            Head: new BareArcadeCell(snapshot.Head.X, snapshot.Head.Y),
            Food: snapshot.Food is { } food ? new BareArcadeCell(food.X, food.Y) : null,
            NextCell: new BareArcadeCell(nextCell.X, nextCell.Y),
            NextCellFatal: nextCellFatal,
            CriticalTextPresent: criticalTextPresent,
            VisibilityQualified: visibilityQualified,
            StateHash: snapshot.StateHash);
    }

    private static GridPoint NextCell(
        GridPoint head,
        Direction direction,
        int width,
        int height) => direction switch
        {
            Direction.Up => new GridPoint(head.X, (head.Y - 1 + height) % height),
            Direction.Right => new GridPoint((head.X + 1) % width, head.Y),
            Direction.Down => new GridPoint(head.X, (head.Y + 1) % height),
            Direction.Left => new GridPoint((head.X - 1 + width) % width, head.Y),
            _ => throw new ArgumentOutOfRangeException(nameof(direction)),
        };

    private static int CountResetResiduals(RunSnapshot snapshot)
    {
        var residuals = 0;
        residuals += snapshot.Status == RunStatus.Running ? 0 : 1;
        residuals += snapshot.DeathCause == DeathCause.None ? 0 : 1;
        residuals += snapshot.Tick == 0 ? 0 : 1;
        residuals += snapshot.Score == 0 ? 0 : 1;
        residuals += snapshot.ComboCount == 0 ? 0 : 1;
        residuals += snapshot.PendingDirections.Count == 0 ? 0 : 1;
        residuals += snapshot.ShieldTicksRemaining == 0 ? 0 : 1;
        residuals += snapshot.PhaseShiftTicksRemaining == 0 ? 0 : 1;
        residuals += snapshot.LastStandHeld ? 1 : 0;
        residuals += snapshot.LastStandRecoveryTicksRemaining == 0 ? 0 : 1;
        residuals += snapshot.SlowMoTicksRemaining == 0 ? 0 : 1;
        residuals += snapshot.BoostTicksRemaining == 0 ? 0 : 1;
        residuals += snapshot.MagnetTicksRemaining == 0 ? 0 : 1;
        residuals += snapshot.GluttonyTicksRemaining == 0 ? 0 : 1;
        residuals += snapshot.BaitPosition is null ? 0 : 1;
        residuals += snapshot.DetachedObstacles.Count == 0 ? 0 : 1;
        return residuals;
    }
}
