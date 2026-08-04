namespace VibeSnake.Rules.Tests;

public sealed class SnakeRunNearMissTests
{
    [Fact]
    public void Default_create_disables_near_miss_scoring()
    {
        // Create uses RunConfig defaults (EnableNearMiss false). A critical clutch
        // eat must not emit near-miss events until the flag is opted in.
        var gated = SnakeRun.CreateForTesting(
            new RunConfig(
                Width: 16,
                Height: 10,
                StarvationTicks: 100,
                PowerSpawnIntervalTicks: 0,
                PowerVisibleTicks: 4),
            [new GridPoint(5, 5), new GridPoint(6, 5), new GridPoint(7, 5)],
            Direction.Right,
            food: new GridPoint(8, 5),
            hungerTicksRemaining: 5);
        var result = gated.Step();
        Assert.Contains(result.OrderedEvents, detail => detail.Kind == RunEventKind.AteFood);
        Assert.DoesNotContain(result.OrderedEvents, detail => detail.Kind == RunEventKind.NearMiss);
        Assert.Equal(0, gated.SessionNearMisses);
        Assert.False(new RunConfig().EnableNearMiss);
    }

    [Fact]
    public void Clutch_eat_awards_near_miss_when_hunger_is_critical()
    {
        var run = SnakeRun.CreateForTesting(
            new RunConfig(
                Width: 16,
                Height: 10,
                StarvationTicks: 100,
                PowerSpawnIntervalTicks: 0,
                PowerVisibleTicks: 4,
                EnableNearMiss: true),
            [new GridPoint(5, 5), new GridPoint(6, 5), new GridPoint(7, 5)],
            Direction.Right,
            food: new GridPoint(8, 5),
            hungerTicksRemaining: 10);

        var before = run.Score;
        var result = run.Step();

        Assert.Contains(result.OrderedEvents, detail => detail.Kind == RunEventKind.AteFood);
        Assert.Contains(
            result.OrderedEvents,
            detail => detail.Kind == RunEventKind.NearMiss && detail.Value == 1);
        Assert.True(result.Events.HasFlag(RunEvent.NearMiss));
        Assert.True(run.Score > before);
        Assert.Equal(1, run.SessionNearMisses);
    }

    [Fact]
    public void Style_points_award_when_eating_with_boost()
    {
        var run = SnakeRun.CreateForTesting(
            new RunConfig(
                Width: 16,
                Height: 10,
                StarvationTicks: 100,
                PowerSpawnIntervalTicks: 0,
                PowerVisibleTicks: 4,
                BoostDurationTicks: 40,
                EnableNearMiss: true),
            [new GridPoint(5, 5), new GridPoint(6, 5), new GridPoint(7, 5)],
            Direction.Right,
            food: new GridPoint(8, 5),
            hungerTicksRemaining: 80,
            boostTicksRemaining: 40);

        var result = run.Step();

        Assert.Contains(result.OrderedEvents, detail => detail.Kind == RunEventKind.AteFood);
        Assert.Contains(
            result.OrderedEvents,
            detail => detail.Kind == RunEventKind.NearMiss && detail.Value == 1);
        Assert.Equal(1, run.SessionNearMisses);
    }

    [Fact]
    public void Short_snakes_do_not_emit_body_proximity_near_misses()
    {
        var run = SnakeRun.CreateForTesting(
            new RunConfig(
                Width: 16,
                Height: 10,
                StarvationTicks: 100,
                PowerSpawnIntervalTicks: 0,
                PowerVisibleTicks: 4,
                EnableNearMiss: true),
            [new GridPoint(5, 5), new GridPoint(6, 5), new GridPoint(7, 5)],
            Direction.Right,
            food: new GridPoint(14, 8),
            hungerTicksRemaining: 80);

        var result = run.Step();
        Assert.DoesNotContain(
            result.OrderedEvents,
            detail => detail.Kind == RunEventKind.NearMiss);
        Assert.Equal(0, run.SessionNearMisses);
    }

    [Fact]
    public void Clutch_and_style_can_stack_on_one_eat()
    {
        var run = SnakeRun.CreateForTesting(
            new RunConfig(
                Width: 16,
                Height: 10,
                StarvationTicks: 100,
                PowerSpawnIntervalTicks: 0,
                PowerVisibleTicks: 4,
                BoostDurationTicks: 40,
                EnableNearMiss: true),
            [new GridPoint(5, 5), new GridPoint(6, 5), new GridPoint(7, 5)],
            Direction.Right,
            food: new GridPoint(8, 5),
            hungerTicksRemaining: 5,
            boostTicksRemaining: 40);

        var result = run.Step();
        var nearMisses = result.OrderedEvents.Count(detail => detail.Kind == RunEventKind.NearMiss);
        Assert.Equal(2, nearMisses);
        Assert.Equal(2, run.SessionNearMisses);
    }
}
