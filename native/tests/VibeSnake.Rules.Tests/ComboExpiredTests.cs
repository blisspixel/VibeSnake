namespace VibeSnake.Rules.Tests;

public sealed class ComboExpiredTests
{
    [Fact]
    public void Combo_window_elapse_emits_combo_expired_by_default()
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
        Assert.Contains(
            result.OrderedEvents,
            detail => detail.Kind == RunEventKind.ComboExpired && detail.Value == 0);
        Assert.True((result.Events & RunEvent.ComboExpired) != 0);
        Assert.True(RulesEventCatalog.IsKnown(RunEventKind.ComboExpired));
        Assert.Equal("combo_expired", RulesEventCatalog.ToWireName(RunEventKind.ComboExpired));
    }

    [Fact]
    public void Combo_window_elapse_can_stay_silent_when_disabled()
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
                FoodScore: 10,
                EnableComboExpiredEvent: false),
            [new GridPoint(1, 1)],
            Direction.Right,
            food: new GridPoint(7, 5),
            hungerTicksRemaining: 100,
            score: 10,
            comboCount: 3,
            ticksSinceLastFood: 2);

        var result = run.Step();
        Assert.Equal(0, run.ComboCount);
        Assert.DoesNotContain(
            result.OrderedEvents,
            detail => detail.Kind == RunEventKind.ComboExpired);
    }

    [Fact]
    public void Combo_window_elapse_emits_when_enabled()
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
                FoodScore: 10,
                EnableComboExpiredEvent: true),
            [new GridPoint(1, 1)],
            Direction.Right,
            food: new GridPoint(7, 5),
            hungerTicksRemaining: 100,
            score: 10,
            comboCount: 3,
            ticksSinceLastFood: 2);

        var result = run.Step();
        Assert.Equal(0, run.ComboCount);
        Assert.Contains(
            result.OrderedEvents,
            detail => detail.Kind == RunEventKind.ComboExpired && detail.Value == 0);
        Assert.True((result.Events & RunEvent.ComboExpired) != 0);
    }
}
