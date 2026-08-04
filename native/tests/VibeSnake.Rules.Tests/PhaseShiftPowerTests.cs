namespace VibeSnake.Rules.Tests;

public sealed class PhaseShiftPowerTests
{
    [Fact]
    public void Phase_shift_pickup_collects_on_entry_and_activates_once()
    {
        var run = CreateRun(
            body: [new GridPoint(1, 1)],
            direction: Direction.Right,
            food: new GridPoint(4, 3),
            pickup: PhasePickup(new GridPoint(2, 1), visibleTicks: 4));

        var result = run.Step();
        var snapshot = run.GetSnapshot();

        Assert.Equal(new GridPoint(2, 1), snapshot.Head);
        Assert.Null(snapshot.PowerPickup);
        Assert.True(snapshot.HasPhaseShift);
        Assert.Equal(3, snapshot.PhaseShiftTicksRemaining);
        Assert.False(snapshot.HasShield);
        Assert.Equal(
            RunEvent.Moved | RunEvent.PowerCollected | RunEvent.PowerActivated,
            result.Events);
        Assert.Equal(
            [
                new RunEventDetail(RunEventKind.Moved, Position: new GridPoint(2, 1)),
                new RunEventDetail(
                    RunEventKind.PowerCollected,
                    Position: new GridPoint(2, 1),
                    Power: PowerKind.PhaseShift),
                new RunEventDetail(
                    RunEventKind.PowerActivated,
                    Value: 3,
                    Power: PowerKind.PhaseShift),
            ],
            result.OrderedEvents);
    }

    [Fact]
    public void Active_phase_shift_counts_down_and_expires_before_collision_resolution()
    {
        var body = new[]
        {
            new GridPoint(1, 1),
            new GridPoint(1, 2),
            new GridPoint(2, 2),
            new GridPoint(2, 1),
        };
        var run = CreateRun(
            body,
            Direction.Down,
            new GridPoint(4, 3),
            phaseShiftTicksRemaining: 1);

        var result = run.Step();

        Assert.False(run.HasPhaseShift);
        Assert.Equal(RunStatus.Dead, run.Status);
        Assert.Equal(DeathCause.SelfCollision, run.DeathCause);
        Assert.Equal(body, run.Body);
        Assert.Equal(
            [RunEventKind.PowerExpired, RunEventKind.Died],
            result.OrderedEvents.Select(value => value.Kind));
        Assert.Equal(PowerKind.PhaseShift, result.OrderedEvents[0].Power);
    }

    [Fact]
    public void Phase_shift_allows_body_overlap_and_preserves_duplicate_occupancy()
    {
        var body = new[]
        {
            new GridPoint(1, 1),
            new GridPoint(1, 2),
            new GridPoint(2, 2),
            new GridPoint(2, 1),
        };
        var run = CreateRun(
            body,
            Direction.Down,
            new GridPoint(4, 3),
            phaseShiftTicksRemaining: 3);

        var result = run.Step();
        var snapshot = run.GetSnapshot();

        Assert.Equal(RunStatus.Running, run.Status);
        Assert.Equal(new GridPoint(2, 2), snapshot.Head);
        Assert.Equal(4, snapshot.Body.Count);
        Assert.Equal(
            [
                new GridPoint(1, 2),
                new GridPoint(2, 2),
                new GridPoint(2, 1),
                new GridPoint(2, 2),
            ],
            snapshot.Body);
        Assert.Equal(2, run.PhaseShiftTicksRemaining);
        Assert.Equal(RunEvent.Moved, result.Events);
        Assert.Equal(
            [new RunEventDetail(RunEventKind.Moved, Position: new GridPoint(2, 2))],
            result.OrderedEvents);
    }

    [Fact]
    public void Phase_shift_does_not_block_starvation()
    {
        var run = CreateRun(
            body: [new GridPoint(1, 1)],
            direction: Direction.Right,
            food: new GridPoint(4, 3),
            hungerTicksRemaining: 1,
            phaseShiftTicksRemaining: 2);

        var result = run.Step();

        Assert.Equal(RunStatus.Dead, run.Status);
        Assert.Equal(DeathCause.Starvation, run.DeathCause);
        Assert.True(run.HasPhaseShift);
        Assert.Equal(1, run.PhaseShiftTicksRemaining);
        Assert.Equal(
            [RunEventKind.Moved, RunEventKind.Died],
            result.OrderedEvents.Select(value => value.Kind));
    }

    [Fact]
    public void Phase_shift_takes_precedence_over_shield_on_self_collision()
    {
        var body = new[]
        {
            new GridPoint(1, 1),
            new GridPoint(1, 2),
            new GridPoint(2, 2),
            new GridPoint(2, 1),
        };
        var run = CreateRun(
            body,
            Direction.Down,
            new GridPoint(4, 3),
            shieldTicksRemaining: 3,
            phaseShiftTicksRemaining: 3);

        var result = run.Step();

        Assert.Equal(RunStatus.Running, run.Status);
        Assert.True(run.HasShield);
        Assert.Equal(2, run.ShieldTicksRemaining);
        Assert.True(run.HasPhaseShift);
        Assert.Equal(2, run.PhaseShiftTicksRemaining);
        Assert.Equal(new GridPoint(2, 2), run.Head);
        Assert.DoesNotContain(
            result.OrderedEvents,
            value => value.Kind is RunEventKind.PowerConsumed or RunEventKind.CollisionPrevented);
    }

    [Fact]
    public void Phase_shift_state_round_trips_and_restart_clears_it()
    {
        var body = new[]
        {
            new GridPoint(1, 1),
            new GridPoint(1, 2),
            new GridPoint(2, 2),
            new GridPoint(2, 1),
        };
        var initial = CreateRun(
            body,
            Direction.Down,
            new GridPoint(4, 3),
            phaseShiftTicksRemaining: 2,
            config: new RunConfig(
                Width: 5,
                Height: 4,
                StarvationTicks: 100,
                PowerSpawnIntervalTicks: 10,
                PowerVisibleTicks: 4,
                ShieldDurationTicks: 3,
                PhaseShiftDurationTicks: 3));
        var restored = SnakeRun.RestoreCanonicalState(initial.SerializeCanonicalState());

        Assert.Equal(initial.ComputeStateHash(), restored.ComputeStateHash());
        Assert.Equal(initial.PhaseShiftTicksRemaining, restored.PhaseShiftTicksRemaining);

        var phased = initial.Step();
        Assert.Equal(RunStatus.Running, initial.Status);
        Assert.Equal(RunEvent.Moved, phased.Events);
        Assert.Equal(1, initial.PhaseShiftTicksRemaining);
        Assert.Equal(
            SnakeRun.RestoreCanonicalState(initial.SerializeCanonicalState()).ComputeStateHash(),
            initial.ComputeStateHash());

        // Force a terminal state so Restart is legal, then prove Phase Shift clears.
        initial = CreateRun(
            body,
            Direction.Down,
            new GridPoint(4, 3),
            phaseShiftTicksRemaining: 0,
            config: new RunConfig(
                Width: 5,
                Height: 4,
                StarvationTicks: 100,
                PowerSpawnIntervalTicks: 0,
                PowerVisibleTicks: 4,
                ShieldDurationTicks: 3,
                PhaseShiftDurationTicks: 3));
        initial.Step();
        Assert.Equal(RunStatus.Dead, initial.Status);

        var restarted = initial.Restart(91UL).GetSnapshot();
        Assert.False(restarted.HasPhaseShift);
        Assert.Equal(0, restarted.PhaseShiftTicksRemaining);
    }

    [Fact]
    public void Rejects_phase_shift_pickup_while_effect_is_active()
    {
        Assert.Throws<ArgumentException>(() =>
            CreateRun(
                body: [new GridPoint(1, 1)],
                direction: Direction.Right,
                food: new GridPoint(4, 3),
                pickup: PhasePickup(new GridPoint(2, 1), visibleTicks: 4),
                phaseShiftTicksRemaining: 2));
    }

    private static PowerPickup PhasePickup(GridPoint position, int visibleTicks) =>
        new(PowerKind.PhaseShift, position, visibleTicks);

    private static SnakeRun CreateRun(
        IEnumerable<GridPoint> body,
        Direction direction,
        GridPoint? food,
        int hungerTicksRemaining = 100,
        PowerPickup? pickup = null,
        int powerSpawnTicksElapsed = 0,
        int shieldTicksRemaining = 0,
        int phaseShiftTicksRemaining = 0,
        RunConfig? config = null) =>
        SnakeRun.CreateForTesting(
            config
                ?? new RunConfig(
                    Width: 5,
                    Height: 4,
                    StarvationTicks: 100,
                    PowerSpawnIntervalTicks: 0,
                    PowerVisibleTicks: 4,
                    ShieldDurationTicks: 3,
                    PhaseShiftDurationTicks: 3),
            body,
            direction,
            food,
            hungerTicksRemaining,
            powerPickup: pickup,
            powerSpawnTicksElapsed: powerSpawnTicksElapsed,
            shieldTicksRemaining: shieldTicksRemaining,
            phaseShiftTicksRemaining: phaseShiftTicksRemaining);
}
