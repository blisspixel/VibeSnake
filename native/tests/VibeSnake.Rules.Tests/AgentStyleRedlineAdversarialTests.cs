using VibeSnake.AgentPlay;
using VibeSnake.Rules;

namespace VibeSnake.Rules.Tests;

public sealed class AgentStyleRedlineAdversarialTests
{
    [Fact]
    public void Redline_excludes_absent_targets_and_rejects_unsafe_or_nonprogressing_steps()
    {
        var tracker = CreateTracker(out var snapshot, initial => initial with { Food = null });

        snapshot = Record(
            tracker,
            snapshot,
            mutate: after => after with { Food = new GridPoint(5, 10) });
        snapshot = Record(
            tracker,
            snapshot,
            mutate: after => after with
            {
                Body = [new GridPoint(1, 10), new GridPoint(0, 10)],
                Food = new GridPoint(5, 10),
            });
        snapshot = Record(
            tracker,
            snapshot,
            mutate: after => after with
            {
                Body = [new GridPoint(1, 10), new GridPoint(0, 10)],
                Food = new GridPoint(6, 10),
            });
        snapshot = Record(
            tracker,
            snapshot,
            [new RunEventDetail(RunEventKind.AteFood)],
            after => after with
            {
                Direction = Direction.Right,
                Body = [new GridPoint(9, 10), new GridPoint(10, 10)],
                DetachedObstacles =
                [
                    new GridPoint(11, 10),
                    new GridPoint(10, 9),
                    new GridPoint(10, 11),
                ],
                DetachedObstacleTicksRemaining = 2,
                Food = new GridPoint(7, 10),
            });
        _ = Record(
            tracker,
            snapshot,
            [new RunEventDetail(RunEventKind.AteFood)],
            after => after with
            {
                Status = RunStatus.Dead,
                DeathCause = DeathCause.SelfCollision,
            });

        var progress = tracker.Snapshot();
        var food = progress.Criteria.Single(value => value.CriterionId == "food_eaten");
        var rate = progress.Criteria.Single(
            value => value.CriterionId == "safe_food_progress_rate_bp");
        Assert.Equal(2, food.Current);
        Assert.Equal(1, rate.Numerator);
        Assert.Equal(4, rate.Denominator);
        Assert.Equal(2_500, rate.Current);
    }

    [Fact]
    public void Redline_uses_the_exact_pre_step_target_and_accepts_a_winning_food_step()
    {
        var tracker = CreateTracker(
            out var snapshot,
            initial => initial with
            {
                Direction = Direction.Left,
                Body = [new GridPoint(2, 10), new GridPoint(1, 10)],
                Food = new GridPoint(0, 10),
            });

        snapshot = Record(
            tracker,
            snapshot,
            mutate: after => after with
            {
                Body = [new GridPoint(1, 10), new GridPoint(0, 10)],
                Food = new GridPoint(20, 10),
            });
        _ = Record(
            tracker,
            snapshot,
            [new RunEventDetail(RunEventKind.AteFood)],
            after => after with
            {
                Status = RunStatus.Won,
                Food = null,
            });

        var rate = tracker.Snapshot().Criteria.Single(
            value => value.CriterionId == "safe_food_progress_rate_bp");
        Assert.Equal(2, rate.Numerator);
        Assert.Equal(2, rate.Denominator);
        Assert.Equal(10_000, rate.Current);
    }

    private static AgentStyleEvidenceTracker CreateTracker(
        out RunSnapshot snapshot,
        Func<RunSnapshot, RunSnapshot> mutateInitial)
    {
        var config = RunModeCatalog.CreateConfig(RunModeCatalog.Classic);
        snapshot = mutateInitial(SnakeRun.Create(321UL, config).GetSnapshot());
        return new AgentStyleEvidenceTracker(
            AgentStyleContractCatalog.RedlineId,
            RunModeCatalog.ClassicId,
            config,
            snapshot);
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

        tracker.Record(
            before,
            new RunStepResult(
                after.Tick,
                RunEvent.None,
                events ?? [],
                after.Status,
                after.DeathCause,
                after.StateHash),
            after);
        return after;
    }
}
