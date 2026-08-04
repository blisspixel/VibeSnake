namespace VibeSnake.Rules.Tests;

public sealed class RemainingPowerTests
{
    [Fact]
    public void Bait_records_head_and_biases_next_food_spawn()
    {
        var run = SnakeRun.CreateForTesting(
            new RunConfig(
                Width: 6,
                Height: 4,
                StarvationTicks: 100,
                PowerSpawnIntervalTicks: 0,
                PowerVisibleTicks: 4),
            [new GridPoint(1, 1)],
            Direction.Right,
            new GridPoint(4, 1),
            hungerTicksRemaining: 100,
            powerPickup: new PowerPickup(PowerKind.Bait, new GridPoint(2, 1), 3));

        var collected = run.Step();
        Assert.Equal(new GridPoint(2, 1), run.BaitPosition);
        Assert.Contains(
            collected.OrderedEvents,
            value => value.Kind == RunEventKind.PowerActivated && value.Power == PowerKind.Bait);
        Assert.Equal(new GridPoint(4, 1), run.Food);

        // Eating with bait set clears the marker on respawn.
        run.Step();
        run.Step();
        Assert.Null(run.BaitPosition);
    }

    [Fact]
    public void Gluttony_eats_food_without_growing()
    {
        var run = SnakeRun.CreateForTesting(
            new RunConfig(
                Width: 6,
                Height: 4,
                StarvationTicks: 100,
                PowerSpawnIntervalTicks: 0,
                PowerVisibleTicks: 4,
                GluttonyDurationTicks: 4),
            [new GridPoint(1, 1), new GridPoint(2, 1)],
            Direction.Right,
            new GridPoint(3, 1),
            hungerTicksRemaining: 100,
            gluttonyTicksRemaining: 3);

        var result = run.Step();
        Assert.Equal(2, run.Body.Count);
        Assert.Equal(new GridPoint(3, 1), run.Head);
        Assert.Contains(result.OrderedEvents, value => value.Kind == RunEventKind.AteFood);
        Assert.Equal(2, run.GluttonyTicksRemaining);
    }

    [Fact]
    public void Segment_detach_creates_obstacles_and_phase_shift_bypasses_them()
    {
        var body = new[]
        {
            new GridPoint(0, 1),
            new GridPoint(1, 1),
            new GridPoint(2, 1),
            new GridPoint(3, 1),
            new GridPoint(4, 1),
            new GridPoint(5, 1),
        };
        var run = SnakeRun.CreateForTesting(
            new RunConfig(
                Width: 10,
                Height: 4,
                StarvationTicks: 100,
                PowerSpawnIntervalTicks: 0,
                PowerVisibleTicks: 4,
                SegmentDetachObstacleTicks: 4,
                SegmentDetachMaxSegments: 5),
            body,
            Direction.Right,
            new GridPoint(8, 1),
            hungerTicksRemaining: 100,
            powerPickup: new PowerPickup(PowerKind.SegmentDetach, new GridPoint(6, 1), 3));

        var detached = run.Step();
        // Move without food: add head then remove tail (length stays 6), then detach 5 -> length 1.
        Assert.Single(run.Body);
        Assert.Equal(5, run.DetachedObstacles.Count);
        Assert.Equal(4, run.DetachedObstacleTicksRemaining);
        Assert.Contains(
            detached.OrderedEvents,
            value => value is { Kind: RunEventKind.PowerActivated, Power: PowerKind.SegmentDetach, Value: 5 });

        // Without phase shift, stepping into an obstacle kills.
        var lethal = SnakeRun.CreateForTesting(
            new RunConfig(
                Width: 10,
                Height: 4,
                StarvationTicks: 100,
                PowerSpawnIntervalTicks: 0,
                PowerVisibleTicks: 4,
                SegmentDetachObstacleTicks: 4),
            [new GridPoint(2, 1)],
            Direction.Left,
            new GridPoint(8, 1),
            hungerTicksRemaining: 100,
            detachedObstacles: [new GridPoint(1, 1)],
            detachedObstacleTicksRemaining: 3);
        var death = lethal.Step();
        Assert.Equal(RunStatus.Dead, lethal.Status);
        Assert.Equal(DeathCause.SelfCollision, lethal.DeathCause);
        Assert.Equal(RunEvent.Died, death.Events);

        // Phase shift allows moving through obstacles.
        var phased = SnakeRun.CreateForTesting(
            new RunConfig(
                Width: 10,
                Height: 4,
                StarvationTicks: 100,
                PowerSpawnIntervalTicks: 0,
                PowerVisibleTicks: 4,
                SegmentDetachObstacleTicks: 4),
            [new GridPoint(2, 1)],
            Direction.Left,
            new GridPoint(8, 1),
            hungerTicksRemaining: 100,
            phaseShiftTicksRemaining: 2,
            detachedObstacles: [new GridPoint(1, 1)],
            detachedObstacleTicksRemaining: 3);
        phased.Step();
        Assert.Equal(RunStatus.Running, phased.Status);
        Assert.Equal(new GridPoint(1, 1), phased.Head);
    }

    [Fact]
    public void Remaining_powers_round_trip_canonical_state()
    {
        var run = SnakeRun.CreateForTesting(
            new RunConfig(
                Width: 8,
                Height: 4,
                StarvationTicks: 100,
                PowerSpawnIntervalTicks: 0,
                PowerVisibleTicks: 4,
                GluttonyDurationTicks: 5,
                SegmentDetachObstacleTicks: 6),
            [new GridPoint(1, 1), new GridPoint(2, 1)],
            Direction.Right,
            new GridPoint(6, 1),
            hungerTicksRemaining: 100,
            gluttonyTicksRemaining: 3,
            baitPosition: new GridPoint(2, 1),
            detachedObstacles: [new GridPoint(0, 0), new GridPoint(0, 1)],
            detachedObstacleTicksRemaining: 4);

        var restored = SnakeRun.RestoreCanonicalState(run.SerializeCanonicalState());
        Assert.Equal(run.ComputeStateHash(), restored.ComputeStateHash());
        Assert.Equal(run.GluttonyTicksRemaining, restored.GluttonyTicksRemaining);
        Assert.Equal(run.BaitPosition, restored.BaitPosition);
        Assert.Equal(run.DetachedObstacles, restored.DetachedObstacles);
        Assert.Equal(run.DetachedObstacleTicksRemaining, restored.DetachedObstacleTicksRemaining);
    }
}
