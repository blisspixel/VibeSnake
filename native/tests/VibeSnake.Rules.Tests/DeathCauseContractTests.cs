namespace VibeSnake.Rules.Tests;

/// <summary>
/// Exact regression fixtures for every published death cause (V040-10).
/// </summary>
public sealed class DeathCauseContractTests
{
    [Fact]
    public void Self_collision_death_attributes_cause_and_terminal_status()
    {
        var run = SnakeRun.CreateForTesting(
            new RunConfig(
                Width: 5,
                Height: 4,
                StarvationTicks: 100,
                PowerSpawnIntervalTicks: 0),
            [
                new GridPoint(1, 1),
                new GridPoint(2, 1),
                new GridPoint(3, 1),
            ],
            Direction.Left,
            food: new GridPoint(0, 0),
            hungerTicksRemaining: 50);

        // Body is tail(1,1)-mid(2,1)-head(3,1) facing left into itself.
        var result = run.Step();

        Assert.Equal(RunStatus.Dead, run.Status);
        Assert.Equal(DeathCause.SelfCollision, run.DeathCause);
        Assert.True(result.Events.HasFlag(RunEvent.Died));
        Assert.Contains(
            result.OrderedEvents,
            detail => detail.Kind == RunEventKind.Died && detail.Cause == DeathCause.SelfCollision);
    }

    [Fact]
    public void Starvation_death_attributes_cause_and_terminal_status()
    {
        var run = SnakeRun.CreateForTesting(
            new RunConfig(
                Width: 5,
                Height: 4,
                StarvationTicks: 1,
                PowerSpawnIntervalTicks: 0),
            [new GridPoint(1, 1)],
            Direction.Right,
            food: new GridPoint(4, 3),
            hungerTicksRemaining: 1);

        var result = run.Step();

        Assert.Equal(RunStatus.Dead, run.Status);
        Assert.Equal(DeathCause.Starvation, run.DeathCause);
        Assert.True(result.Events.HasFlag(RunEvent.Died));
        Assert.Contains(
            result.OrderedEvents,
            detail => detail.Kind == RunEventKind.Died && detail.Cause == DeathCause.Starvation);
    }

    [Fact]
    public void Running_and_won_states_report_none_death_cause()
    {
        var running = SnakeRun.Create(11UL);
        Assert.Equal(RunStatus.Running, running.Status);
        Assert.Equal(DeathCause.None, running.DeathCause);

        var won = SnakeRun.CreateForTesting(
            new RunConfig(
                Width: 2,
                Height: 2,
                PowerSpawnIntervalTicks: 0),
            [
                new GridPoint(0, 0),
                new GridPoint(1, 0),
                new GridPoint(1, 1),
            ],
            Direction.Left,
            food: new GridPoint(0, 1),
            hungerTicksRemaining: 20);

        var result = won.Step();
        Assert.Equal(RunStatus.Won, won.Status);
        Assert.Equal(DeathCause.None, won.DeathCause);
        Assert.True(result.Events.HasFlag(RunEvent.Won));
        Assert.DoesNotContain(
            result.OrderedEvents,
            detail => detail.Kind == RunEventKind.Died);
    }

    [Fact]
    public void Published_death_cause_set_is_closed()
    {
        var values = Enum.GetValues<DeathCause>();
        Assert.Equal(
            new[]
            {
                DeathCause.None,
                DeathCause.SelfCollision,
                DeathCause.Starvation,
            },
            values);
    }
}
