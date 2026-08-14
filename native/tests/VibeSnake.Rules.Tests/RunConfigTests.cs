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
        new RunConfig(StarvationWarningTicks: -1),
        new RunConfig(StarvationWarningTicks: RunConfig.MaximumConfiguredTicks + 1),
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
        new RunConfig(PhaseShiftDurationTicks: RunConfig.MinimumPhaseShiftDurationTicks - 1),
        new RunConfig(PhaseShiftDurationTicks: RunConfig.MaximumConfiguredTicks + 1),
        new RunConfig(LastStandRecoveryTicks: RunConfig.MinimumLastStandRecoveryTicks - 1),
        new RunConfig(LastStandRecoveryTicks: RunConfig.MaximumConfiguredTicks + 1),
        new RunConfig(SlowMoDurationTicks: RunConfig.MinimumSlowMoDurationTicks - 1),
        new RunConfig(SlowMoDurationTicks: RunConfig.MaximumConfiguredTicks + 1),
        new RunConfig(BoostDurationTicks: RunConfig.MinimumBoostDurationTicks - 1),
        new RunConfig(BoostDurationTicks: RunConfig.MaximumConfiguredTicks + 1),
        new RunConfig(MagnetDurationTicks: RunConfig.MinimumMagnetDurationTicks - 1),
        new RunConfig(MagnetDurationTicks: RunConfig.MaximumConfiguredTicks + 1),
        new RunConfig(GluttonyDurationTicks: RunConfig.MinimumGluttonyDurationTicks - 1),
        new RunConfig(GluttonyDurationTicks: RunConfig.MaximumConfiguredTicks + 1),
        new RunConfig(
            SegmentDetachObstacleTicks: RunConfig.MinimumSegmentDetachObstacleTicks - 1),
        new RunConfig(SegmentDetachObstacleTicks: RunConfig.MaximumConfiguredTicks + 1),
        new RunConfig(
            SegmentDetachMaxSegments: RunConfig.MinimumSegmentDetachMaxSegments - 1),
        new RunConfig(
            SegmentDetachMaxSegments: RunConfig.MaximumSegmentDetachMaxSegments + 1),
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
        Assert.Equal(RunConfig.ConfigHashAlgorithmId, "sha256-canonical-runconfig-v3");
    }

    [Fact]
    public void Config_hash_changes_when_a_scoring_field_changes()
    {
        var baseline = new RunConfig().ComputeConfigHash();
        var wider = new RunConfig(Width: 32).ComputeConfigHash();
        var nearMissOff = new RunConfig(EnableNearMiss: false).ComputeConfigHash();
        var foodScore = new RunConfig(FoodScore: 11).ComputeConfigHash();

        Assert.NotEqual(baseline, wider);
        Assert.NotEqual(baseline, nearMissOff);
        Assert.NotEqual(baseline, foodScore);
        Assert.NotEqual(wider, nearMissOff);
    }

    [Fact]
    public void Config_hash_includes_every_field_in_canonical_json()
    {
        var json = new RunConfig(
            EnableNearMiss: true,
            EnableComboExpiredEvent: true).SerializeCanonicalConfig();

        Assert.Contains("\"algorithm\":\"sha256-canonical-runconfig-v3\"", json);
        Assert.Contains("\"rulesetId\":\"vibesnake-core\"", json);
        Assert.Contains("\"rulesVersion\":4", json);
        Assert.Contains("\"modeId\":\"vibe\"", json);
        Assert.Contains("\"modeVersion\":1", json);
        Assert.Contains("\"enableNearMiss\":true", json);
        Assert.Contains("\"enableComboExpiredEvent\":true", json);
        Assert.Contains("\"starvationWarningTicks\":200", json);
        Assert.Contains("\"rulesTickMilliseconds\":50", json);
        Assert.Contains("\"enableStarvation\":true", json);
        Assert.Contains("\"enableComboScoring\":true", json);
        Assert.Contains("\"enableSpeedScoreBonus\":true", json);
        Assert.Contains("\"enableLengthScoreBonus\":true", json);
        Assert.Contains("\"enableAdaptation\":false", json);
        Assert.Contains("\"adaptivePolicyId\":\"none\"", json);
        Assert.DoesNotContain("enablePowerDecisionOffers", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Config_hash_changes_when_combo_expired_flag_changes()
    {
        var off = new RunConfig(EnableComboExpiredEvent: false).ComputeConfigHash();
        var on = new RunConfig(EnableComboExpiredEvent: true).ComputeConfigHash();
        Assert.NotEqual(off, on);
    }

    [Fact]
    public void Config_hash_changes_when_achievement_candidate_flag_changes()
    {
        var off = new RunConfig(EnableAchievementCandidates: false).ComputeConfigHash();
        var on = new RunConfig(EnableAchievementCandidates: true).ComputeConfigHash();
        Assert.NotEqual(off, on);
        Assert.Contains(
            "\"enableAchievementCandidates\":true",
            new RunConfig(EnableAchievementCandidates: true).SerializeCanonicalConfig(),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Config_hash_changes_for_mode_and_score_model_fields()
    {
        var vibe = RunModeCatalog.CreateConfig(
            RunModeCatalog.Vibe,
            enableAdaptation: false);
        var classic = RunModeCatalog.CreateConfig(RunModeCatalog.Classic);

        Assert.NotEqual(vibe.ComputeConfigHash(), classic.ComputeConfigHash());
        Assert.NotEqual(
            vibe.ComputeConfigHash(),
            (vibe with { EnableStarvation = false }).ComputeConfigHash());
        Assert.NotEqual(
            vibe.ComputeConfigHash(),
            (vibe with { EnableComboScoring = false }).ComputeConfigHash());
        Assert.NotEqual(
            vibe.ComputeConfigHash(),
            (vibe with { EnableSpeedScoreBonus = false }).ComputeConfigHash());
        Assert.NotEqual(
            vibe.ComputeConfigHash(),
            (vibe with { EnableLengthScoreBonus = false }).ComputeConfigHash());
        var adaptive = RunModeCatalog.CreateConfig(RunModeCatalog.Vibe);
        Assert.NotEqual(vibe.ComputeConfigHash(), adaptive.ComputeConfigHash());
        Assert.NotEqual(
            vibe.ComputeConfigHash(),
            (vibe with { EnablePowerDecisionOffers = false }).ComputeConfigHash());
    }

    [Theory]
    [InlineData("unknown", 1)]
    [InlineData("classic", 0)]
    [InlineData("vibe", 2)]
    public void Config_rejects_unknown_mode_identity(string modeId, int modeVersion)
    {
        var invalid = new RunConfig(ModeId: modeId, ModeVersion: modeVersion);
        Assert.Throws<ArgumentException>(() => invalid.ComputeConfigHash());
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
    public void Classic_rejects_vibe_scoring_and_power_flags()
    {
        var classic = RunModeCatalog.CreateConfig(RunModeCatalog.Classic);
        classic.Validate();

        Assert.Throws<ArgumentException>(
            () => (classic with { EnableStarvation = true }).Validate());
        Assert.Throws<ArgumentException>(
            () => (classic with { EnableComboScoring = true }).Validate());
        Assert.Throws<ArgumentException>(
            () => (classic with { EnableNearMiss = true }).Validate());
        Assert.Throws<ArgumentException>(
            () => (classic with { PowerSpawnIntervalTicks = 300 }).Validate());
        Assert.Throws<ArgumentException>(
            () => new RunConfig(ModeId: RunModeCatalog.ClassicId).Validate());
    }

    [Fact]
    public void Config_hash_rejects_invalid_configuration_before_hashing()
    {
        var invalid = new RunConfig(Width: 1);
        Assert.Throws<ArgumentOutOfRangeException>(() => invalid.ComputeConfigHash());
        Assert.Throws<ArgumentOutOfRangeException>(() => invalid.SerializeCanonicalConfig());
    }
}
