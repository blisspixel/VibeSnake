namespace VibeSnake.Rules.Tests;

public sealed class RunConfigTests
{
    public static TheoryData<RunConfig> InvalidConfigurations => new()
    {
        new RunConfig(Width: 1),
        new RunConfig(Height: 1),
        new RunConfig(Width: RunConfig.MaximumGridDimension + 1),
        new RunConfig(Height: RunConfig.MaximumGridDimension + 1),
        new RunConfig(
            Width: RunConfig.MaximumGridDimension,
            Height: (RunConfig.MaximumGridCells / RunConfig.MaximumGridDimension) + 1),
        new RunConfig(StarvationTicks: 0),
        new RunConfig(StarvationTicks: RunConfig.MaximumConfiguredTicks + 1),
        new RunConfig(MaximumDirectionQueue: 0),
        new RunConfig(
            MaximumDirectionQueue: RunConfig.MaximumDirectionQueueCapacity + 1),
        new RunConfig(FoodScore: 0),
        new RunConfig(FoodScore: RunConfig.MaximumFoodScore + 1),
        new RunConfig(ComboWindowTicks: 0),
        new RunConfig(ComboWindowTicks: RunConfig.MaximumConfiguredTicks + 1),
        new RunConfig(SpeedBonusTicks: 0),
        new RunConfig(ComboWindowTicks: 10, SpeedBonusTicks: 11),
        new RunConfig(PowerSpawnIntervalTicks: -1),
        new RunConfig(PowerSpawnIntervalTicks: RunConfig.MaximumConfiguredTicks + 1),
        new RunConfig(PowerVisibleTicks: RunConfig.MinimumPowerVisibleTicks - 1),
        new RunConfig(PowerVisibleTicks: RunConfig.MaximumConfiguredTicks + 1),
        new RunConfig(ShieldDurationTicks: RunConfig.MinimumShieldDurationTicks - 1),
        new RunConfig(ShieldDurationTicks: RunConfig.MaximumConfiguredTicks + 1),
    };

    [Theory]
    [MemberData(nameof(InvalidConfigurations))]
    public void Create_rejects_invalid_configuration(RunConfig configuration)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SnakeRun.Create(0UL, configuration));
    }

    [Fact]
    public void Create_accepts_the_maximum_supported_grid_work_budget()
    {
        var configuration = new RunConfig(
            Width: RunConfig.MaximumGridDimension,
            Height: RunConfig.MaximumGridCells / RunConfig.MaximumGridDimension);

        var run = SnakeRun.Create(0UL, configuration);

        Assert.Equal(RunStatus.Running, run.Status);
        Assert.NotNull(run.Food);
    }
}
