namespace VibeSnake.Rules.Tests;

public sealed class TempoPowerTests
{
    [Fact]
    public void Slow_mo_and_boost_compose_cadence_without_changing_step_distance()
    {
        var run = SnakeRun.CreateForTesting(
            new RunConfig(
                Width: 8,
                Height: 4,
                StarvationTicks: 100,
                PowerSpawnIntervalTicks: 0,
                PowerVisibleTicks: 4,
                SlowMoDurationTicks: 4,
                BoostDurationTicks: 4),
            [new GridPoint(1, 1)],
            Direction.Right,
            new GridPoint(6, 1),
            hungerTicksRemaining: 100,
            slowMoTicksRemaining: 3,
            boostTicksRemaining: 3);

        Assert.Equal(2, run.MovementCadenceNumerator);
        Assert.Equal(2, run.MovementCadenceDenominator);

        var result = run.Step();
        Assert.Equal(new GridPoint(2, 1), run.Head);
        Assert.Equal(2, run.SlowMoTicksRemaining);
        Assert.Equal(2, run.BoostTicksRemaining);
        Assert.Equal(RunEvent.Moved, result.Events);
    }

    [Fact]
    public void Slow_mo_pickup_activates_and_expires_cleanly()
    {
        var run = SnakeRun.CreateForTesting(
            new RunConfig(
                Width: 5,
                Height: 4,
                StarvationTicks: 100,
                PowerSpawnIntervalTicks: 0,
                PowerVisibleTicks: 4,
                SlowMoDurationTicks: 2),
            [new GridPoint(1, 1)],
            Direction.Right,
            new GridPoint(4, 3),
            hungerTicksRemaining: 100,
            powerPickup: new PowerPickup(PowerKind.SlowMo, new GridPoint(2, 1), 3));

        var collected = run.Step();
        Assert.True(run.HasSlowMo);
        Assert.Equal(2, run.MovementCadenceNumerator);
        Assert.Equal(1, run.MovementCadenceDenominator);
        Assert.Contains(
            collected.OrderedEvents,
            value => value.Kind == RunEventKind.PowerActivated && value.Power == PowerKind.SlowMo);

        run.Step();
        var expired = run.Step();
        Assert.False(run.HasSlowMo);
        Assert.Equal(1, run.MovementCadenceNumerator);
        Assert.Contains(
            expired.OrderedEvents,
            value => value.Kind == RunEventKind.PowerExpired && value.Power == PowerKind.SlowMo);
    }

    [Fact]
    public void Boost_pickup_activates_and_round_trips()
    {
        var run = SnakeRun.CreateForTesting(
            new RunConfig(
                Width: 5,
                Height: 4,
                StarvationTicks: 100,
                PowerSpawnIntervalTicks: 0,
                PowerVisibleTicks: 4,
                BoostDurationTicks: 3),
            [new GridPoint(1, 1)],
            Direction.Right,
            new GridPoint(4, 3),
            hungerTicksRemaining: 100,
            powerPickup: new PowerPickup(PowerKind.Boost, new GridPoint(2, 1), 3));

        run.Step();
        Assert.True(run.HasBoost);
        Assert.Equal(1, run.MovementCadenceNumerator);
        Assert.Equal(2, run.MovementCadenceDenominator);

        var restored = SnakeRun.RestoreCanonicalState(run.SerializeCanonicalState());
        Assert.Equal(run.ComputeStateHash(), restored.ComputeStateHash());
        Assert.Equal(run.BoostTicksRemaining, restored.BoostTicksRemaining);
    }

    [Fact]
    public void Cadence_clock_maps_tempo_scales_to_wall_clock_intervals()
    {
        Assert.Equal(50, RulesCadenceClock.StepIntervalMilliseconds(1, 1));
        Assert.Equal(100, RulesCadenceClock.StepIntervalMilliseconds(2, 1));
        Assert.Equal(25, RulesCadenceClock.StepIntervalMilliseconds(1, 2));
        Assert.Equal(50, RulesCadenceClock.StepIntervalMilliseconds(2, 2));
    }

    [Fact]
    public void Cadence_clock_drains_boost_and_slow_mo_steps_from_accumulator()
    {
        var accumulated = 0.0;
        var interval = 25;
        var boostSteps = RulesCadenceClock.DrainSteps(
            ref accumulated,
            deltaSeconds: 0.05,
            () => interval);
        Assert.Equal(2, boostSteps);
        Assert.Equal(0.0, accumulated);

        interval = 100;
        var slowSteps = RulesCadenceClock.DrainSteps(
            ref accumulated,
            deltaSeconds: 0.05,
            () => interval);
        Assert.Equal(0, slowSteps);
        Assert.Equal(50.0, accumulated);

        slowSteps = RulesCadenceClock.DrainSteps(
            ref accumulated,
            deltaSeconds: 0.05,
            () => interval);
        Assert.Equal(1, slowSteps);
        Assert.Equal(0.0, accumulated);
    }

    [Fact]
    public void Cadence_clock_re_reads_interval_after_each_drained_step()
    {
        var accumulated = 0.0;
        var remainingBoostSteps = 1;
        var steps = RulesCadenceClock.DrainSteps(
            ref accumulated,
            deltaSeconds: 0.075,
            () => remainingBoostSteps-- > 0 ? 25 : 50);
        Assert.Equal(2, steps);
        Assert.Equal(0.0, accumulated);
    }

    [Fact]
    public void Live_run_exposes_effective_rules_step_milliseconds()
    {
        var run = SnakeRun.CreateForTesting(
            new RunConfig(
                Width: 8,
                Height: 4,
                StarvationTicks: 100,
                PowerSpawnIntervalTicks: 0,
                PowerVisibleTicks: 4,
                SlowMoDurationTicks: 4,
                BoostDurationTicks: 4),
            [new GridPoint(1, 1)],
            Direction.Right,
            new GridPoint(6, 1),
            hungerTicksRemaining: 100,
            slowMoTicksRemaining: 2,
            boostTicksRemaining: 2);

        Assert.Equal(50, run.EffectiveRulesStepMilliseconds);
        Assert.Equal(50, run.GetSnapshot().EffectiveRulesStepMilliseconds);
    }
}
