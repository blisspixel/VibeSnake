namespace VibeSnake.Rules.Tests;

public sealed class ComboExpiredTests
{
    [Fact]
    public void Combo_window_elapse_clears_combo_count()
    {
        var run = SnakeRun.CreateForTesting(
            new RunConfig(
                Width: 8,
                Height: 6,
                StarvationTicks: 100,
                ComboWindowTicks: 2,
                SpeedBonusTicks: 1,
                PowerSpawnIntervalTicks: 0,
                PowerVisibleTicks: 4,
                FoodScore: 10),
            [new GridPoint(1, 1)],
            Direction.Right,
            food: new GridPoint(7, 5),
            hungerTicksRemaining: 100,
            score: 10,
            comboCount: 3,
            ticksSinceLastFood: 2);

        var result = run.Step();
        Assert.Equal(0, run.ComboCount);
        // Event emission is reserved until core_rules fixtures regenerate.
        Assert.DoesNotContain(
            result.OrderedEvents,
            detail => detail.Kind == RunEventKind.ComboExpired);
        Assert.True(RulesEventCatalog.IsKnown(RunEventKind.ComboExpired));
        Assert.Equal("combo_expired", RulesEventCatalog.ToWireName(RunEventKind.ComboExpired));
    }
}
