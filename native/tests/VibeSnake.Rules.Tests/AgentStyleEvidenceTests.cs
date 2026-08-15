using VibeSnake.AgentPlay;
using VibeSnake.Rules;

namespace VibeSnake.Rules.Tests;

public sealed class AgentStyleEvidenceTests
{
    [Theory]
    [InlineData(198, 200, 9_900, true)]
    [InlineData(197, 200, 9_850, false)]
    public void Stillwater_uses_the_full_running_post_step_boundary(
        int openSteps,
        int totalSteps,
        int expectedBasisPoints,
        bool expectedSatisfied)
    {
        var tracker = CreateTracker(
            AgentStyleContractCatalog.StillwaterId,
            RunModeCatalog.ClassicId,
            out _,
            out var snapshot);
        for (var index = 0; index < totalSteps; index++)
        {
            var open = index < openSteps;
            snapshot = Record(
                tracker,
                snapshot,
                mutate: after => after with
                {
                    Direction = Direction.Right,
                    PendingDirections = [],
                    Body = [new GridPoint(9, 10), new GridPoint(10, 10)],
                    DetachedObstacles = open
                        ? []
                        :
                        [
                            new GridPoint(11, 10),
                            new GridPoint(10, 9),
                            new GridPoint(10, 11),
                        ],
                    DetachedObstacleTicksRemaining = open ? 0 : 2,
                });
        }

        var progress = tracker.Snapshot();
        AssertCriterion(progress, "survival_steps", totalSteps, totalSteps >= 200);
        var rate = AssertCriterion(
            progress,
            "structural_open_exit_rate_bp",
            expectedBasisPoints,
            expectedSatisfied);
        Assert.Equal(openSteps, rate.Numerator);
        Assert.Equal(totalSteps, rate.Denominator);
    }

    [Fact]
    public void Stillwater_includes_a_terminal_post_step_in_its_rate_denominator()
    {
        var tracker = CreateTracker(
            AgentStyleContractCatalog.StillwaterId,
            RunModeCatalog.ClassicId,
            out _,
            out var snapshot);
        for (var index = 0; index < 199; index++)
        {
            snapshot = Record(tracker, snapshot);
        }

        _ = Record(
            tracker,
            snapshot,
            mutate: after => after with
            {
                Status = RunStatus.Dead,
                DeathCause = DeathCause.SelfCollision,
            });

        var rate = AssertCriterion(
            tracker.Snapshot(),
            "structural_open_exit_rate_bp",
            9_950,
            expectedSatisfied: true);
        Assert.Equal(199, rate.Numerator);
        Assert.Equal(200, rate.Denominator);
    }

    [Fact]
    public void Crownchaser_freezes_clean_continuity_at_the_first_combo_four()
    {
        var clean = CreateTracker(
            AgentStyleContractCatalog.CrownchaserId,
            RunModeCatalog.VibeId,
            out _,
            out var cleanSnapshot);
        for (var combo = 1; combo <= 4; combo++)
        {
            cleanSnapshot = Record(
                clean,
                cleanSnapshot,
                [new RunEventDetail(RunEventKind.AteFood)],
                after => after with { ComboCount = combo });
        }

        cleanSnapshot = Record(
            clean,
            cleanSnapshot,
            [new RunEventDetail(RunEventKind.ComboExpired)],
            after => after with { ComboCount = 0 });
        _ = Record(
            clean,
            cleanSnapshot,
            [new RunEventDetail(RunEventKind.AteFood)],
            after => after with { ComboCount = 1 });
        var frozen = clean.Snapshot();
        AssertCriterion(frozen, "peak_combo", 4, expectedSatisfied: true);
        var cleanRate = AssertCriterion(
            frozen,
            "clean_pre_peak_continuity_bp",
            10_000,
            expectedSatisfied: true);
        Assert.Equal(4, cleanRate.Numerator);
        Assert.Equal(4, cleanRate.Denominator);

        var broken = CreateTracker(
            AgentStyleContractCatalog.CrownchaserId,
            RunModeCatalog.VibeId,
            out _,
            out var brokenSnapshot);
        for (var combo = 1; combo <= 3; combo++)
        {
            brokenSnapshot = Record(
                broken,
                brokenSnapshot,
                [new RunEventDetail(RunEventKind.AteFood)],
                after => after with { ComboCount = combo });
        }

        brokenSnapshot = Record(
            broken,
            brokenSnapshot,
            [new RunEventDetail(RunEventKind.ComboExpired)],
            after => after with { ComboCount = 0 });
        for (var combo = 1; combo <= 4; combo++)
        {
            brokenSnapshot = Record(
                broken,
                brokenSnapshot,
                [new RunEventDetail(RunEventKind.AteFood)],
                after => after with { ComboCount = combo });
        }

        var brokenRate = AssertCriterion(
            broken.Snapshot(),
            "clean_pre_peak_continuity_bp",
            5_714,
            expectedSatisfied: false);
        Assert.Equal(4, brokenRate.Numerator);
        Assert.Equal(7, brokenRate.Denominator);
    }

    [Fact]
    public void Edge_prophet_counts_only_rewarded_body_proximity_and_tracks_wrap_coincidence()
    {
        var tracker = CreateTracker(
            AgentStyleContractCatalog.EdgeProphetId,
            RunModeCatalog.VibeId,
            out _,
            out var snapshot);
        GridPoint[] body =
        [
            new(0, 0),
            new(0, 1),
            new(0, 2),
            new(9, 9),
            new(9, 10),
            new(10, 9),
            new(11, 9),
            new(10, 10),
        ];
        for (var index = 0; index < 3; index++)
        {
            var events = index == 0
                ? new[]
                {
                    new RunEventDetail(RunEventKind.Wrapped),
                    new RunEventDetail(
                        RunEventKind.NearMiss,
                        Position: body[^1],
                        Value: 1),
                }
                :
                [
                    new RunEventDetail(
                        RunEventKind.NearMiss,
                        Position: body[^1],
                        Value: 1),
                ];
            snapshot = Record(
                tracker,
                snapshot,
                events,
                after => after with { Body = body });
        }

        snapshot = Record(
            tracker,
            snapshot,
            [new RunEventDetail(RunEventKind.NearMiss, Position: body[^1], Value: 0)],
            after => after with { Body = body });
        snapshot = Record(
            tracker,
            snapshot,
            [new RunEventDetail(RunEventKind.NearMiss, Position: null, Value: 2)],
            after => after with { Body = body });
        _ = Record(
            tracker,
            snapshot,
            [new RunEventDetail(RunEventKind.NearMiss, Position: new GridPoint(20, 20), Value: 2)],
            after => after with { Body = body });

        var progress = tracker.Snapshot();
        AssertCriterion(
            progress,
            "rewarded_body_proximity_near_misses",
            3,
            expectedSatisfied: true);
        AssertCriterion(
            progress,
            "wrapped_rewarded_body_proximity_near_misses",
            1,
            expectedSatisfied: true);
    }

    [Fact]
    public void Mutagenist_distinguishes_overlapping_from_sequential_active_power_kinds()
    {
        var overlapping = CreateTracker(
            AgentStyleContractCatalog.MutagenistId,
            RunModeCatalog.VibeId,
            out _,
            out var overlappingSnapshot);
        overlappingSnapshot = Record(
            overlapping,
            overlappingSnapshot,
            [new RunEventDetail(RunEventKind.PowerActivated, Power: PowerKind.Shield)],
            after => after with { ShieldTicksRemaining = 2 });
        _ = Record(
            overlapping,
            overlappingSnapshot,
            [new RunEventDetail(RunEventKind.PowerActivated, Power: PowerKind.Boost)],
            after => after with { ShieldTicksRemaining = 1, BoostTicksRemaining = 2 });
        var overlappingProgress = overlapping.Snapshot();
        AssertCriterion(
            overlappingProgress,
            "distinct_power_kinds_activated",
            2,
            expectedSatisfied: true);
        AssertCriterion(
            overlappingProgress,
            "maximum_concurrent_active_power_kinds",
            2,
            expectedSatisfied: true);

        var sequential = CreateTracker(
            AgentStyleContractCatalog.MutagenistId,
            RunModeCatalog.VibeId,
            out _,
            out var sequentialSnapshot);
        sequentialSnapshot = Record(
            sequential,
            sequentialSnapshot,
            [new RunEventDetail(RunEventKind.PowerActivated, Power: PowerKind.Shield)],
            after => after with { ShieldTicksRemaining = 1 });
        _ = Record(
            sequential,
            sequentialSnapshot,
            [new RunEventDetail(RunEventKind.PowerActivated, Power: PowerKind.Boost)],
            after => after with { ShieldTicksRemaining = 0, BoostTicksRemaining = 1 });
        var sequentialProgress = sequential.Snapshot();
        AssertCriterion(
            sequentialProgress,
            "distinct_power_kinds_activated",
            2,
            expectedSatisfied: true);
        AssertCriterion(
            sequentialProgress,
            "maximum_concurrent_active_power_kinds",
            1,
            expectedSatisfied: false);
    }

    [Theory]
    [InlineData(13, 20, 6_500, true)]
    [InlineData(12, 20, 6_000, false)]
    public void Redline_uses_all_eligible_pre_food_steps_and_ate_food_qualifies(
        int safeSteps,
        int totalSteps,
        int expectedBasisPoints,
        bool expectedSatisfied)
    {
        var target = new GridPoint(20, 10);
        var tracker = CreateTracker(
            AgentStyleContractCatalog.RedlineId,
            RunModeCatalog.ClassicId,
            out _,
            out var snapshot,
            initial => initial with { Food = target });
        for (var index = 0; index < totalSteps; index++)
        {
            snapshot = Record(
                tracker,
                snapshot,
                index < safeSteps
                    ? [new RunEventDetail(RunEventKind.AteFood)]
                    : [],
                after => after with
                {
                    Food = index == 0 ? new GridPoint(21, 10) : after.Food,
                });
        }

        var progress = tracker.Snapshot();
        AssertCriterion(progress, "food_eaten", safeSteps, expectedSatisfied: safeSteps >= 6);
        var rate = AssertCriterion(
            progress,
            "safe_food_progress_rate_bp",
            expectedBasisPoints,
            expectedSatisfied);
        Assert.Equal(safeSteps, rate.Numerator);
        Assert.Equal(totalSteps, rate.Denominator);
    }

    [Fact]
    public void Style_evidence_rejects_invalid_arithmetic_steps_events_and_hashes()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => AgentStyleEvidenceMath.BasisPoints(-1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => AgentStyleEvidenceMath.BasisPoints(0, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => AgentStyleEvidenceMath.BasisPoints(2, 1));
        Assert.Throws<OverflowException>(() =>
            AgentStyleEvidenceMath.BasisPoints(long.MaxValue, long.MaxValue));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            AgentStyleEvidenceMath.WrappedManhattanDistance(
                new GridPoint(0, 0),
                new GridPoint(0, 0),
                0,
                1));

        var tracker = CreateTracker(
            AgentStyleContractCatalog.MutagenistId,
            RunModeCatalog.VibeId,
            out _,
            out var snapshot);
        var next = snapshot with { Tick = 1, StateHash = "0000000000000001" };
        Assert.Throws<InvalidOperationException>(() => tracker.Record(
            snapshot with { Tick = 1 },
            Result(next),
            next));
        Assert.Throws<InvalidOperationException>(() => tracker.Record(
            snapshot,
            Result(next, [new RunEventDetail((RunEventKind)byte.MaxValue)]),
            next));
        Assert.Throws<InvalidOperationException>(() => tracker.Record(
            snapshot,
            Result(next, [new RunEventDetail(RunEventKind.PowerActivated)]),
            next));
        Assert.Throws<ArgumentException>(() => tracker.CreateOutcome("not-a-replay-hash"));
        Assert.Throws<ArgumentNullException>(() =>
            AgentStyleEvidenceReplayEvaluator.EvaluateProgress(
                AgentStyleContractCatalog.MutagenistId,
                RunModeCatalog.VibeId,
                null!));
    }

    private static AgentStyleEvidenceTracker CreateTracker(
        string styleId,
        string modeId,
        out RunConfig config,
        out RunSnapshot initialSnapshot,
        Func<RunSnapshot, RunSnapshot>? mutateInitial = null)
    {
        var mode = RunModeCatalog.Get(modeId, RunModeCatalog.CurrentModeVersion);
        config = RunModeCatalog.CreateConfig(mode);
        initialSnapshot = SnakeRun.Create(123UL, config).GetSnapshot();
        if (mutateInitial is not null)
        {
            initialSnapshot = mutateInitial(initialSnapshot);
        }

        return new AgentStyleEvidenceTracker(styleId, modeId, config, initialSnapshot);
    }

    private static RunSnapshot Record(
        AgentStyleEvidenceTracker tracker,
        RunSnapshot before,
        IReadOnlyList<RunEventDetail>? events = null,
        Func<RunSnapshot, RunSnapshot>? mutate = null)
    {
        var after = before with
        {
            Tick = checked(before.Tick + 1),
            StateHash = checked(before.Tick + 1).ToString(
                "x16",
                System.Globalization.CultureInfo.InvariantCulture),
        };
        if (mutate is not null)
        {
            after = mutate(after);
        }

        tracker.Record(before, Result(after, events), after);
        return after;
    }

    private static RunStepResult Result(
        RunSnapshot after,
        IReadOnlyList<RunEventDetail>? events = null) =>
        new(
            after.Tick,
            RunEvent.None,
            events ?? [],
            after.Status,
            after.DeathCause,
            after.StateHash);

    private static AgentStyleCriterionProgressV3 AssertCriterion(
        AgentStyleProgressV3 progress,
        string criterionId,
        int expectedCurrent,
        bool expectedSatisfied)
    {
        var criterion = progress.Criteria.Single(value => value.CriterionId == criterionId);
        Assert.Equal(expectedCurrent, criterion.Current);
        Assert.Equal(expectedSatisfied, criterion.ThresholdReached);
        Assert.Equal(
            progress.Criteria.Count(value => value.ThresholdReached),
            progress.ThresholdsReached);
        Assert.Equal(progress.ThresholdsReached == 2, progress.AllThresholdsReached);
        return criterion;
    }
}
