namespace VibeSnake.Rules.Tests;

public sealed class ShieldPowerTests
{
    [Fact]
    public void Pickup_contract_rejects_unknown_kind_and_nonpositive_visibility()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new PowerPickup((PowerKind)255, new GridPoint(1, 1), 1));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new PowerPickup(PowerKind.Shield, new GridPoint(1, 1), 0));
    }

    [Fact]
    public void Shield_pickup_collects_on_entry_and_activates_once()
    {
        var run = CreateRun(
            body: [new GridPoint(1, 1)],
            direction: Direction.Right,
            food: new GridPoint(4, 3),
            pickup: ShieldPickup(new GridPoint(2, 1), visibleTicks: 4));

        var result = run.Step();
        var snapshot = run.GetSnapshot();

        Assert.Equal(new GridPoint(2, 1), snapshot.Head);
        Assert.Null(snapshot.PowerPickup);
        Assert.True(snapshot.HasShield);
        Assert.Equal(3, snapshot.ShieldTicksRemaining);
        Assert.Equal(
            RunEvent.Moved | RunEvent.PowerCollected | RunEvent.PowerActivated,
            result.Events);
        Assert.Equal(
            [
                new RunEventDetail(RunEventKind.Moved, Position: new GridPoint(2, 1)),
                new RunEventDetail(
                    RunEventKind.PowerCollected,
                    Position: new GridPoint(2, 1),
                    Power: PowerKind.Shield),
                new RunEventDetail(
                    RunEventKind.PowerActivated,
                    Value: 3,
                    Power: PowerKind.Shield),
            ],
            result.OrderedEvents);
    }

    [Fact]
    public void Active_shield_counts_down_and_expires_before_collision_resolution()
    {
        var run = CreateRun(
            body: [new GridPoint(1, 1)],
            direction: Direction.Right,
            food: new GridPoint(4, 3),
            shieldTicksRemaining: 2);

        var activeStep = run.Step();
        Assert.Equal(1, run.ShieldTicksRemaining);
        Assert.True(run.HasShield);
        Assert.Equal([RunEventKind.Moved], activeStep.OrderedEvents.Select(value => value.Kind));

        var expiryStep = run.Step();

        Assert.False(run.HasShield);
        Assert.Equal(0, run.ShieldTicksRemaining);
        Assert.Equal(
            [RunEventKind.PowerExpired, RunEventKind.Moved],
            expiryStep.OrderedEvents.Select(value => value.Kind));
        Assert.Equal(PowerKind.Shield, expiryStep.OrderedEvents[0].Power);
    }

    [Fact]
    public void Shield_consumes_on_one_self_collision_without_moving_the_snake()
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
            shieldTicksRemaining: 2);

        var blocked = run.Step();

        Assert.Equal(RunStatus.Running, run.Status);
        Assert.Equal(DeathCause.None, run.DeathCause);
        Assert.Equal(body, run.Body);
        Assert.False(run.HasShield);
        Assert.Equal(99, run.HungerTicksRemaining);
        Assert.Equal(
            RunEvent.PowerConsumed | RunEvent.CollisionPrevented,
            blocked.Events);
        Assert.Equal(
            [
                new RunEventDetail(
                    RunEventKind.PowerConsumed,
                    Power: PowerKind.Shield),
                new RunEventDetail(
                    RunEventKind.CollisionPrevented,
                    Position: new GridPoint(2, 2),
                    Cause: DeathCause.SelfCollision,
                    Power: PowerKind.Shield),
            ],
            blocked.OrderedEvents);

        var fatal = run.Step();

        Assert.Equal(RunStatus.Dead, run.Status);
        Assert.Equal(DeathCause.SelfCollision, run.DeathCause);
        Assert.Equal(RunEvent.Died, fatal.Events);
    }

    [Fact]
    public void Shield_collision_does_not_prevent_simultaneous_starvation()
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
            hungerTicksRemaining: 1,
            shieldTicksRemaining: 2);

        var result = run.Step();

        Assert.Equal(RunStatus.Dead, run.Status);
        Assert.Equal(DeathCause.Starvation, run.DeathCause);
        Assert.Equal(0, run.HungerTicksRemaining);
        Assert.False(run.HasShield);
        Assert.Equal(body, run.Body);
        Assert.Equal(
            RunEvent.PowerConsumed | RunEvent.CollisionPrevented | RunEvent.Died,
            result.Events);
        Assert.Equal(
            [
                RunEventKind.PowerConsumed,
                RunEventKind.CollisionPrevented,
                RunEventKind.Died,
            ],
            result.OrderedEvents.Select(value => value.Kind));
        Assert.Equal(DeathCause.Starvation, result.OrderedEvents[^1].Cause);
        Assert.Equal(run.Head, result.OrderedEvents[^1].Position);
    }

    [Fact]
    public void Minimum_duration_shield_protects_the_first_post_collection_step()
    {
        var body = new[]
        {
            new GridPoint(4, 2),
            new GridPoint(3, 2),
            new GridPoint(2, 2),
            new GridPoint(1, 2),
            new GridPoint(1, 1),
            new GridPoint(1, 0),
            new GridPoint(2, 0),
        };
        var run = CreateRun(
            body,
            Direction.Down,
            new GridPoint(4, 3),
            pickup: ShieldPickup(new GridPoint(2, 1), visibleTicks: 4),
            config: new RunConfig(
                Width: 5,
                Height: 4,
                StarvationTicks: 100,
                PowerSpawnIntervalTicks: 0,
                PowerVisibleTicks: 4,
                ShieldDurationTicks: RunConfig.MinimumShieldDurationTicks));

        var collection = run.Step();
        var collision = run.Step();

        Assert.Contains(
            collection.OrderedEvents,
            value => value.Kind == RunEventKind.PowerActivated
                && value.Value == RunConfig.MinimumShieldDurationTicks);
        Assert.Equal(RunStatus.Running, run.Status);
        Assert.False(run.HasShield);
        Assert.Equal(
            [RunEventKind.PowerConsumed, RunEventKind.CollisionPrevented],
            collision.OrderedEvents.Select(value => value.Kind));
    }

    [Fact]
    public void Starvation_bypasses_shield_and_preserves_its_remaining_state()
    {
        var run = CreateRun(
            body: [new GridPoint(1, 1)],
            direction: Direction.Right,
            food: new GridPoint(4, 3),
            hungerTicksRemaining: 1,
            shieldTicksRemaining: 2);

        var result = run.Step();

        Assert.Equal(RunStatus.Dead, run.Status);
        Assert.Equal(DeathCause.Starvation, run.DeathCause);
        Assert.True(run.HasShield);
        Assert.Equal(1, run.ShieldTicksRemaining);
        Assert.Equal(
            [RunEventKind.Moved, RunEventKind.Died],
            result.OrderedEvents.Select(value => value.Kind));
        Assert.DoesNotContain(
            result.OrderedEvents,
            value => value.Kind is RunEventKind.PowerConsumed or RunEventKind.CollisionPrevented);
    }

    [Fact]
    public void Uncollected_shield_expires_before_the_final_destination_check()
    {
        var run = CreateRun(
            body: [new GridPoint(1, 1)],
            direction: Direction.Right,
            food: new GridPoint(4, 3),
            pickup: ShieldPickup(new GridPoint(2, 1), visibleTicks: 1));

        var result = run.Step();

        Assert.Equal(new GridPoint(2, 1), run.Head);
        Assert.Null(run.PowerPickup);
        Assert.False(run.HasShield);
        Assert.Equal(
            [RunEventKind.PowerExpired, RunEventKind.Moved],
            result.OrderedEvents.Select(value => value.Kind));
        Assert.Equal(new GridPoint(2, 1), result.OrderedEvents[0].Position);
    }

    [Fact]
    public void Spawned_shield_is_deterministic_legal_and_never_on_the_immediate_destination()
    {
        var config = new RunConfig(
            Width: 5,
            Height: 4,
            StarvationTicks: 100,
            PowerSpawnIntervalTicks: 1,
            PowerVisibleTicks: 4,
            ShieldDurationTicks: 3);
        var first = SnakeRun.CreateForTesting(
            config,
            [new GridPoint(1, 1)],
            Direction.Right,
            new GridPoint(4, 3),
            hungerTicksRemaining: 100,
            randomState: 1UL,
            randomIncrement: 109UL);
        var second = SnakeRun.RestoreCanonicalState(first.SerializeCanonicalState());

        var firstResult = first.Step();
        var secondResult = second.Step();

        Assert.Equal(firstResult, secondResult);
        var pickup = Assert.IsType<PowerPickup>(first.PowerPickup);
        Assert.Equal(PowerKind.Shield, pickup.Kind);
        Assert.Equal(4, pickup.VisibilityTicksRemaining);
        Assert.DoesNotContain(pickup.Position, first.Body);
        Assert.NotEqual(first.Food, pickup.Position);
        Assert.NotEqual(new GridPoint(2, 1), pickup.Position);
        Assert.Contains(
            firstResult.OrderedEvents,
            value => value.Kind == RunEventKind.PowerSpawned
                && value.Power == PowerKind.Shield
                && value.Position == pickup.Position
                && value.Value == 4);
    }

    [Fact]
    public void Food_respawn_discards_a_pickup_when_it_is_the_only_free_cell()
    {
        var run = CreateRun(
            body: [new GridPoint(0, 0), new GridPoint(0, 1)],
            direction: Direction.Right,
            food: new GridPoint(1, 1),
            pickup: ShieldPickup(new GridPoint(1, 0), visibleTicks: 4),
            config: new RunConfig(
                Width: 2,
                Height: 2,
                StarvationTicks: 100,
                PowerSpawnIntervalTicks: 0,
                PowerVisibleTicks: 4,
                ShieldDurationTicks: 3));

        var result = run.Step();

        Assert.Equal(RunStatus.Running, run.Status);
        Assert.Null(run.PowerPickup);
        Assert.Equal(new GridPoint(1, 0), run.Food);
        Assert.Equal(3, run.Body.Count);
        Assert.Equal(RunEventKind.PowerDiscarded, result.OrderedEvents[^1].Kind);
        Assert.Equal(PowerKind.Shield, result.OrderedEvents[^1].Power);
    }

    [Fact]
    public void Shield_state_round_trips_replays_and_restart_clears_it()
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
            powerSpawnTicksElapsed: 7,
            shieldTicksRemaining: 2,
            config: new RunConfig(
                Width: 5,
                Height: 4,
                StarvationTicks: 100,
                PowerSpawnIntervalTicks: 10,
                PowerVisibleTicks: 4,
                ShieldDurationTicks: 3));
        var canonical = initial.SerializeCanonicalState();
        var restored = SnakeRun.RestoreCanonicalState(canonical);

        Assert.Equal(initial.ComputeStateHash(), restored.ComputeStateHash());
        Assert.Equal(initial.PowerSpawnTicksElapsed, restored.PowerSpawnTicksElapsed);
        Assert.Equal(initial.ShieldTicksRemaining, restored.ShieldTicksRemaining);
        Assert.Equal(canonical, restored.SerializeCanonicalState());

        var replay = RunReplay.Capture(initial, [Array.Empty<Direction>(), Array.Empty<Direction>()], 1);
        var verification = RunReplay.Read(replay.Serialize()).Replay!.Verify();

        Assert.True(verification.IsValid, verification.Message);
        Assert.Equal(RunStatus.Dead, replay.Outcome.Status);
        Assert.Equal(DeathCause.SelfCollision, replay.Outcome.DeathCause);

        initial.Step();
        initial.Step();
        var restarted = initial.Restart(91UL).GetSnapshot();
        Assert.Null(restarted.PowerPickup);
        Assert.False(restarted.HasShield);
        Assert.Equal(0, restarted.ShieldTicksRemaining);
        Assert.Equal(0, restarted.PowerSpawnTicksElapsed);
    }

    private static PowerPickup ShieldPickup(GridPoint position, int visibleTicks) =>
        new(PowerKind.Shield, position, visibleTicks);

    private static SnakeRun CreateRun(
        IEnumerable<GridPoint> body,
        Direction direction,
        GridPoint? food,
        int hungerTicksRemaining = 100,
        PowerPickup? pickup = null,
        int powerSpawnTicksElapsed = 0,
        int shieldTicksRemaining = 0,
        RunConfig? config = null) =>
        SnakeRun.CreateForTesting(
            config
                ?? new RunConfig(
                    Width: 5,
                    Height: 4,
                    StarvationTicks: 100,
                    PowerSpawnIntervalTicks: 0,
                    PowerVisibleTicks: 4,
                    ShieldDurationTicks: 3),
            body,
            direction,
            food,
            hungerTicksRemaining,
            powerPickup: pickup,
            powerSpawnTicksElapsed: powerSpawnTicksElapsed,
            shieldTicksRemaining: shieldTicksRemaining);
}
