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

    [Fact]
    public void Config_hash_is_stable_for_identical_defaults()
    {
        var left = new RunConfig().ComputeConfigHash();
        var right = new RunConfig().ComputeConfigHash();

        Assert.Equal(64, left.Length);
        Assert.Equal(left, right);
        Assert.Matches("^[0-9a-f]{64}$", left);
        Assert.Equal(RunConfig.ConfigHashAlgorithmId, "sha256-canonical-runconfig-v1");
    }

    [Fact]
    public void Config_hash_changes_when_a_scoring_field_changes()
    {
        var baseline = new RunConfig().ComputeConfigHash();
        var wider = new RunConfig(Width: 32).ComputeConfigHash();
        var nearMiss = new RunConfig(EnableNearMiss: true).ComputeConfigHash();
        var foodScore = new RunConfig(FoodScore: 11).ComputeConfigHash();

        Assert.NotEqual(baseline, wider);
        Assert.NotEqual(baseline, nearMiss);
        Assert.NotEqual(baseline, foodScore);
        Assert.NotEqual(wider, nearMiss);
    }

    [Fact]
    public void Config_hash_includes_every_field_in_canonical_json()
    {
        var json = new RunConfig(EnableNearMiss: true).SerializeCanonicalConfig();

        Assert.Contains("\"algorithm\":\"sha256-canonical-runconfig-v1\"", json);
        Assert.Contains("\"rulesetId\":\"vibesnake-core\"", json);
        Assert.Contains("\"rulesVersion\":4", json);
        Assert.Contains("\"enableNearMiss\":true", json);
        Assert.Contains("\"starvationWarningTicks\":200", json);
        Assert.Contains("\"rulesTickMilliseconds\":50", json);
    }

    [Fact]
    public void SnakeRun_exposes_config_hash_matching_the_run_configuration()
    {
        var configuration = new RunConfig(Width: 16, Height: 12, EnableNearMiss: true);
        var run = SnakeRun.Create(1UL, configuration);

        Assert.Equal(configuration.ComputeConfigHash(), run.ConfigHash);
        Assert.Equal(RunConfig.ConfigHashAlgorithmId, run.ConfigHashAlgorithm);
    }

    [Fact]
    public void Config_hash_rejects_invalid_configuration_before_hashing()
    {
        var invalid = new RunConfig(Width: 1);
        Assert.Throws<ArgumentOutOfRangeException>(() => invalid.ComputeConfigHash());
        Assert.Throws<ArgumentOutOfRangeException>(() => invalid.SerializeCanonicalConfig());
    }
}
