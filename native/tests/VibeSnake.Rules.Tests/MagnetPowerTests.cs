namespace VibeSnake.Rules.Tests;

public sealed class MagnetPowerTests
{
    [Fact]
    public void Magnet_pulls_food_one_cell_toward_the_head_each_step()
    {
        var run = SnakeRun.CreateForTesting(
            new RunConfig(
                Width: 10,
                Height: 10,
                StarvationTicks: 100,
                PowerSpawnIntervalTicks: 0,
                PowerVisibleTicks: 4,
                MagnetDurationTicks: 4),
            [new GridPoint(2, 2)],
            Direction.Right,
            new GridPoint(6, 5),
            hungerTicksRemaining: 100,
            magnetTicksRemaining: 3);

        run.Step();

        Assert.Equal(new GridPoint(5, 4), run.Food);
        Assert.Equal(2, run.MagnetTicksRemaining);
        Assert.Equal(new GridPoint(3, 2), run.Head);
    }

    [Fact]
    public void Magnet_does_not_pull_food_onto_occupied_cells()
    {
        // Head at (2,1), food at (4,1): pull wants (3,1) which is body.
        var run = SnakeRun.CreateForTesting(
            new RunConfig(
                Width: 8,
                Height: 4,
                StarvationTicks: 100,
                PowerSpawnIntervalTicks: 0,
                PowerVisibleTicks: 4,
                MagnetDurationTicks: 3),
            [new GridPoint(1, 1), new GridPoint(2, 1), new GridPoint(3, 1)],
            Direction.Up,
            new GridPoint(4, 1),
            hungerTicksRemaining: 100,
            magnetTicksRemaining: 2);

        run.Step();
        Assert.Equal(new GridPoint(4, 1), run.Food);
    }

    [Fact]
    public void Magnet_pickup_activates_and_round_trips()
    {
        var run = SnakeRun.CreateForTesting(
            new RunConfig(
                Width: 5,
                Height: 4,
                StarvationTicks: 100,
                PowerSpawnIntervalTicks: 0,
                PowerVisibleTicks: 4,
                MagnetDurationTicks: 3),
            [new GridPoint(1, 1)],
            Direction.Right,
            new GridPoint(4, 3),
            hungerTicksRemaining: 100,
            powerPickup: new PowerPickup(PowerKind.Magnet, new GridPoint(2, 1), 3));

        run.Step();
        Assert.True(run.HasMagnet);
        var restored = SnakeRun.RestoreCanonicalState(run.SerializeCanonicalState());
        Assert.Equal(run.ComputeStateHash(), restored.ComputeStateHash());
        Assert.Equal(run.MagnetTicksRemaining, restored.MagnetTicksRemaining);
    }
}
