namespace VibeSnake.Rules.Tests;

public sealed class LastStandPowerTests
{
    [Fact]
    public void Last_stand_pickup_collects_and_holds_without_timer()
    {
        var run = CreateRun(
            body: [new GridPoint(1, 1)],
            direction: Direction.Right,
            food: new GridPoint(4, 3),
            pickup: LastStandPickup(new GridPoint(2, 1), visibleTicks: 4));

        var result = run.Step();

        Assert.True(run.LastStandHeld);
        Assert.Equal(0, run.LastStandRecoveryTicksRemaining);
        Assert.Null(run.PowerPickup);
        Assert.Equal(
            [
                RunEventKind.Moved,
                RunEventKind.PowerCollected,
                RunEventKind.PowerActivated,
            ],
            result.OrderedEvents.Select(value => value.Kind));
        Assert.Equal(0, result.OrderedEvents[^1].Value);
    }

    [Fact]
    public void Last_stand_revives_self_collision_shrinks_body_and_grants_recovery()
    {
        var body = new[]
        {
            new GridPoint(1, 1),
            new GridPoint(1, 2),
            new GridPoint(2, 2),
            new GridPoint(2, 1),
            new GridPoint(3, 1),
        };
        var run = CreateRun(
            body,
            Direction.Left,
            new GridPoint(4, 3),
            lastStandHeld: true);

        var result = run.Step();

        Assert.Equal(RunStatus.Running, run.Status);
        Assert.False(run.LastStandHeld);
        Assert.Equal(3, run.Body.Count);
        Assert.Equal(new GridPoint(3, 1), run.Head);
        Assert.Equal(
            [
                new GridPoint(2, 2),
                new GridPoint(2, 1),
                new GridPoint(3, 1),
            ],
            run.Body);
        Assert.Equal(100, run.HungerTicksRemaining);
        Assert.Equal(3, run.LastStandRecoveryTicksRemaining);
        Assert.Equal(
            [
                RunEventKind.PowerConsumed,
                RunEventKind.CollisionPrevented,
                RunEventKind.HungerReset,
                RunEventKind.PowerActivated,
            ],
            result.OrderedEvents.Select(value => value.Kind));
        Assert.Equal(DeathCause.SelfCollision, result.OrderedEvents[1].Cause);
        Assert.Equal(PowerKind.LastStand, result.OrderedEvents[0].Power);
    }

    [Fact]
    public void Recovery_immunity_blocks_collision_without_moving()
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
            lastStandRecoveryTicksRemaining: 2);

        var result = run.Step();

        Assert.Equal(RunStatus.Running, run.Status);
        Assert.Equal(body, run.Body);
        Assert.Equal(1, run.LastStandRecoveryTicksRemaining);
        Assert.Equal(
            [RunEventKind.CollisionPrevented],
            result.OrderedEvents.Select(value => value.Kind));
        Assert.Equal(PowerKind.LastStand, result.OrderedEvents[0].Power);
    }

    [Fact]
    public void Last_stand_intercepts_starvation_and_resets_hunger()
    {
        var run = CreateRun(
            body: [new GridPoint(1, 1), new GridPoint(2, 1), new GridPoint(3, 1), new GridPoint(4, 1)],
            direction: Direction.Right,
            food: new GridPoint(0, 0),
            hungerTicksRemaining: 1,
            lastStandHeld: true,
            config: new RunConfig(
                Width: 8,
                Height: 4,
                StarvationTicks: 100,
                PowerSpawnIntervalTicks: 0,
                PowerVisibleTicks: 4,
                ShieldDurationTicks: 3,
                PhaseShiftDurationTicks: 3,
                LastStandRecoveryTicks: 3));

        var result = run.Step();

        Assert.Equal(RunStatus.Running, run.Status);
        Assert.False(run.LastStandHeld);
        Assert.Equal(2, run.Body.Count);
        Assert.Equal(100, run.HungerTicksRemaining);
        Assert.Equal(3, run.LastStandRecoveryTicksRemaining);
        Assert.Contains(
            result.OrderedEvents,
            value => value.Kind == RunEventKind.PowerConsumed && value.Power == PowerKind.LastStand);
        Assert.Contains(
            result.OrderedEvents,
            value => value.Kind == RunEventKind.HungerReset && value.Value == 100);
        Assert.DoesNotContain(
            result.OrderedEvents,
            value => value.Kind == RunEventKind.Died);
    }

    [Fact]
    public void Phase_shift_outranks_last_stand_and_shield_on_collision()
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
            phaseShiftTicksRemaining: 3,
            lastStandHeld: true);

        var result = run.Step();

        Assert.Equal(RunStatus.Running, run.Status);
        Assert.True(run.LastStandHeld);
        Assert.True(run.HasShield);
        Assert.Equal(new GridPoint(2, 2), run.Head);
        Assert.Equal(RunEvent.Moved, result.Events);
    }

    [Fact]
    public void Recovery_outranks_shield_on_collision()
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
            lastStandRecoveryTicksRemaining: 2);

        var result = run.Step();

        Assert.Equal(RunStatus.Running, run.Status);
        Assert.True(run.HasShield);
        // Lifecycle advances Shield even when recovery blocks the collision move.
        Assert.Equal(2, run.ShieldTicksRemaining);
        Assert.Equal(1, run.LastStandRecoveryTicksRemaining);
        Assert.Equal(body, run.Body);
        Assert.Equal(PowerKind.LastStand, result.OrderedEvents[0].Power);
    }

    [Fact]
    public void Last_stand_state_round_trips()
    {
        var run = CreateRun(
            body: [new GridPoint(1, 1), new GridPoint(2, 1), new GridPoint(3, 1)],
            direction: Direction.Right,
            food: new GridPoint(4, 3),
            lastStandHeld: true,
            lastStandRecoveryTicksRemaining: 2);
        var restored = SnakeRun.RestoreCanonicalState(run.SerializeCanonicalState());

        Assert.Equal(run.ComputeStateHash(), restored.ComputeStateHash());
        Assert.True(restored.LastStandHeld);
        Assert.Equal(2, restored.LastStandRecoveryTicksRemaining);
    }

    private static PowerPickup LastStandPickup(GridPoint position, int visibleTicks) =>
        new(PowerKind.LastStand, position, visibleTicks);

    private static SnakeRun CreateRun(
        IEnumerable<GridPoint> body,
        Direction direction,
        GridPoint? food,
        int hungerTicksRemaining = 100,
        PowerPickup? pickup = null,
        int powerSpawnTicksElapsed = 0,
        int shieldTicksRemaining = 0,
        int phaseShiftTicksRemaining = 0,
        bool lastStandHeld = false,
        int lastStandRecoveryTicksRemaining = 0,
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
                    PhaseShiftDurationTicks: 3,
                    LastStandRecoveryTicks: 3),
            body,
            direction,
            food,
            hungerTicksRemaining,
            powerPickup: pickup,
            powerSpawnTicksElapsed: powerSpawnTicksElapsed,
            shieldTicksRemaining: shieldTicksRemaining,
            phaseShiftTicksRemaining: phaseShiftTicksRemaining,
            lastStandHeld: lastStandHeld,
            lastStandRecoveryTicksRemaining: lastStandRecoveryTicksRemaining);
}
