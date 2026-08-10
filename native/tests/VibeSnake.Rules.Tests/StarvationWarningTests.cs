namespace VibeSnake.Rules.Tests;

public sealed class StarvationWarningTests
{
    [Fact]
    public void Emits_once_when_hunger_crosses_warning_threshold()
    {
        var run = SnakeRun.CreateForTesting(
            new RunConfig(
                Width: 8,
                Height: 6,
                StarvationTicks: 10,
                StarvationWarningTicks: 3,
                PowerSpawnIntervalTicks: 0,
                PowerVisibleTicks: 4),
            [new GridPoint(2, 2)],
            Direction.Right,
            food: new GridPoint(7, 5),
            hungerTicksRemaining: 4);

        var first = run.Step();
        Assert.Equal(3, run.HungerTicksRemaining);
        Assert.Equal(10, run.GetSnapshot().HungerMaximumTicks);
        Assert.Equal(3, run.GetSnapshot().HungerWarningTicks);
        Assert.True(first.Events.HasFlag(RunEvent.StarvationWarning));
        Assert.Contains(
            first.OrderedEvents,
            detail => detail.Kind == RunEventKind.StarvationWarning && detail.Value == 3);

        var second = run.Step();
        Assert.Equal(2, run.HungerTicksRemaining);
        Assert.False(second.Events.HasFlag(RunEvent.StarvationWarning));
        Assert.DoesNotContain(
            second.OrderedEvents,
            detail => detail.Kind == RunEventKind.StarvationWarning);
    }

    [Fact]
    public void Eating_rearms_starvation_warning_for_a_later_crossing()
    {
        // Cross into warning without food, then eat to rearm, then cross again.
        var run = SnakeRun.CreateForTesting(
            new RunConfig(
                Width: 8,
                Height: 6,
                StarvationTicks: 8,
                StarvationWarningTicks: 3,
                PowerSpawnIntervalTicks: 0,
                PowerVisibleTicks: 4),
            [new GridPoint(1, 1)],
            Direction.Right,
            food: new GridPoint(3, 1),
            hungerTicksRemaining: 4);

        Assert.True(run.Step().Events.HasFlag(RunEvent.StarvationWarning)); // 4 -> 3, head (2,1)
        Assert.Equal(3, run.HungerTicksRemaining);
        Assert.True(run.Step().Events.HasFlag(RunEvent.AteFood)); // eat at (3,1), hunger -> 8
        Assert.Equal(8, run.HungerTicksRemaining);

        // Move away from any immediate food and walk hunger down to the threshold again.
        run = SnakeRun.CreateForTesting(
            new RunConfig(
                Width: 8,
                Height: 6,
                StarvationTicks: 8,
                StarvationWarningTicks: 3,
                PowerSpawnIntervalTicks: 0,
                PowerVisibleTicks: 4),
            [new GridPoint(1, 1)],
            Direction.Right,
            food: new GridPoint(7, 5),
            hungerTicksRemaining: 8);

        var sawWarning = false;
        for (var step = 0; step < 10 && run.Status == RunStatus.Running; step++)
        {
            var result = run.Step();
            if (result.Events.HasFlag(RunEvent.StarvationWarning))
            {
                sawWarning = true;
                Assert.Equal(3, run.HungerTicksRemaining);
                break;
            }
        }

        Assert.True(sawWarning);
    }

    [Fact]
    public void Zero_threshold_disables_starvation_warning_events()
    {
        var run = SnakeRun.CreateForTesting(
            new RunConfig(
                Width: 8,
                Height: 6,
                StarvationTicks: 5,
                StarvationWarningTicks: 0,
                PowerSpawnIntervalTicks: 0,
                PowerVisibleTicks: 4),
            [new GridPoint(2, 2)],
            Direction.Right,
            food: new GridPoint(7, 5),
            hungerTicksRemaining: 3);

        for (var i = 0; i < 3; i++)
        {
            var result = run.Step();
            Assert.False(result.Events.HasFlag(RunEvent.StarvationWarning));
        }
    }
}
