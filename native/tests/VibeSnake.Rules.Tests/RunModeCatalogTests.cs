namespace VibeSnake.Rules.Tests;

public sealed class RunModeCatalogTests
{
    [Fact]
    public void Catalog_freezes_two_unique_product_mode_contracts()
    {
        Assert.Equal(2, RunModeCatalog.All.Count);
        Assert.Equal(["classic", "vibe"], RunModeCatalog.All.Select(mode => mode.Id));
        Assert.Equal(2, RunModeCatalog.All.Select(mode => mode.ContractId).Distinct().Count());
        Assert.Equal(2, RunModeCatalog.All.Select(mode => mode.ScoreCategoryId).Distinct().Count());

        foreach (var mode in RunModeCatalog.All)
        {
            Assert.Equal(1, mode.Version);
            Assert.Equal(64, mode.BoardWidth);
            Assert.Equal(33, mode.BoardHeight);
            Assert.False(string.IsNullOrWhiteSpace(mode.DisplayName));
            Assert.False(string.IsNullOrWhiteSpace(mode.Description));
            Assert.False(string.IsNullOrWhiteSpace(mode.ScoreCategoryId));
            Assert.False(string.IsNullOrWhiteSpace(mode.ScoreModelDescription));
            Assert.False(string.IsNullOrWhiteSpace(mode.DifficultyPolicyId));
            Assert.Equal(RunPauseRule.FreezeRulesAndBufferedInput, mode.PauseRule);
            Assert.Equal(RunSeedRule.FreshLocalSeedPerRun, mode.SeedRule);
            Assert.Equal(RunRestartRule.FreshSeedSameModeAndBoard, mode.RestartRule);
        }
    }

    [Fact]
    public void Classic_contract_contains_only_the_minimal_arcade_feature_set()
    {
        var expected = RunModeFeatures.Movement
            | RunModeFeatures.Wrapping
            | RunModeFeatures.FoodAndGrowth
            | RunModeFeatures.FixedSpeed
            | RunModeFeatures.SelfCollision
            | RunModeFeatures.Pause;

        Assert.Equal(expected, RunModeCatalog.Classic.Features);
        Assert.Equal(RunAdaptiveState.Disabled, RunModeCatalog.Classic.AdaptiveState);
        Assert.Equal("none", RunModeCatalog.Classic.AdaptivePolicyId);
        Assert.Equal("classic-standard-v1", RunModeCatalog.Classic.ScoreCategoryId);
    }

    [Fact]
    public void Vibe_contract_adds_pressure_powers_progression_and_disclosed_adaptation()
    {
        var vibe = RunModeCatalog.Vibe;

        Assert.True(vibe.Includes(RunModeFeatures.Starvation));
        Assert.True(vibe.Includes(RunModeFeatures.ComboScoring));
        Assert.True(vibe.Includes(RunModeFeatures.NearMisses));
        Assert.True(vibe.Includes(RunModeFeatures.PowerUps));
        Assert.True(vibe.Includes(RunModeFeatures.Progression));
        Assert.True(vibe.Includes(RunModeFeatures.FullFeedback));
        Assert.True(vibe.Includes(RunModeFeatures.AdaptivePolicy));
        Assert.Equal(RunAdaptiveState.EnabledByDefault, vibe.AdaptiveState);
        Assert.Equal(AdaptiveDifficultyPolicy.CurrentPolicyId, vibe.AdaptivePolicyId);
        Assert.Equal(RunModeCatalog.VibeAdaptiveScoreCategoryId, vibe.ScoreCategoryId);
    }

    [Fact]
    public void Factory_maps_mode_contracts_to_distinct_exact_rule_configs()
    {
        var classic = RunModeCatalog.CreateConfig(RunModeCatalog.Classic);
        var vibe = RunModeCatalog.CreateConfig(RunModeCatalog.Vibe);

        Assert.Equal("classic", classic.ModeId);
        Assert.Equal(1, classic.ModeVersion);
        Assert.False(classic.EnableStarvation);
        Assert.False(classic.EnableComboScoring);
        Assert.False(classic.EnableSpeedScoreBonus);
        Assert.False(classic.EnableLengthScoreBonus);
        Assert.False(classic.EnableNearMiss);
        Assert.False(classic.EnableComboExpiredEvent);
        Assert.False(classic.EnableAchievementCandidates);
        Assert.False(classic.EnableAdaptation);
        Assert.Equal(AdaptiveDifficultyPolicy.DisabledPolicyId, classic.AdaptivePolicyId);
        Assert.Equal(0, classic.PowerSpawnIntervalTicks);

        Assert.Equal("vibe", vibe.ModeId);
        Assert.True(vibe.EnableStarvation);
        Assert.True(vibe.EnableComboScoring);
        Assert.True(vibe.EnableSpeedScoreBonus);
        Assert.True(vibe.EnableLengthScoreBonus);
        Assert.True(vibe.EnableNearMiss);
        Assert.True(vibe.EnableComboExpiredEvent);
        Assert.True(vibe.EnableAchievementCandidates);
        Assert.True(vibe.EnableAdaptation);
        Assert.Equal(AdaptiveDifficultyPolicy.CurrentPolicyId, vibe.AdaptivePolicyId);
        Assert.True(vibe.PowerSpawnIntervalTicks > 0);
        Assert.NotEqual(classic.ComputeConfigHash(), vibe.ComputeConfigHash());
    }

    [Fact]
    public void Classic_disables_starvation_and_power_spawning_during_long_straight_play()
    {
        var config = RunModeCatalog.CreateConfig(RunModeCatalog.Classic) with
        {
            Width = 8,
            Height = 6,
        };
        var run = SnakeRun.CreateForTesting(
            config,
            [new GridPoint(2, 2)],
            Direction.Right,
            food: new GridPoint(7, 5),
            hungerTicksRemaining: 1,
            powerSpawnTicksElapsed: 0);

        for (var step = 0; step < 2_000; step++)
        {
            var result = run.Step();
            Assert.Equal(RunStatus.Running, result.Status);
            Assert.DoesNotContain(
                result.OrderedEvents,
                item => item.Kind is RunEventKind.StarvationWarning
                    or RunEventKind.PowerSpawned);
        }

        Assert.Equal(1, run.HungerTicksRemaining);
        Assert.Null(run.PowerPickup);
        Assert.Equal(0, run.ComboCount);
    }

    [Fact]
    public void Classic_food_awards_only_fixed_points_and_emits_no_hunger_reset()
    {
        var config = RunModeCatalog.CreateConfig(RunModeCatalog.Classic) with
        {
            Width = 20,
            Height = 4,
        };
        var body = Enumerable.Range(0, 11).Select(x => new GridPoint(x, 1)).ToArray();
        var run = SnakeRun.CreateForTesting(
            config,
            body,
            Direction.Right,
            food: new GridPoint(11, 1),
            hungerTicksRemaining: 1,
            comboCount: 20,
            ticksSinceLastFood: 0);

        var result = run.Step();

        Assert.Equal(10, run.Score);
        Assert.Equal(0, run.ComboCount);
        Assert.Contains(
            result.OrderedEvents,
            item => item.Kind == RunEventKind.ScoreChanged && item.Value == 10);
        Assert.DoesNotContain(
            result.OrderedEvents,
            item => item.Kind == RunEventKind.HungerReset);
    }

    [Fact]
    public void Restart_retains_mode_board_and_score_category_with_a_fresh_seed()
    {
        var config = RunModeCatalog.CreateConfig(RunModeCatalog.Classic);
        var original = SnakeRun.CreateForTesting(
            config,
            [
                new GridPoint(1, 1),
                new GridPoint(1, 2),
                new GridPoint(2, 2),
                new GridPoint(2, 1),
            ],
            Direction.Down,
            food: new GridPoint(10, 10),
            hungerTicksRemaining: config.StarvationTicks);
        original.Step();
        Assert.Equal(RunStatus.Dead, original.Status);
        var restarted = original.Restart(42UL);

        Assert.Equal(RunModeCatalog.Classic, original.Mode);
        Assert.Equal(RunModeCatalog.Classic, restarted.Mode);
        Assert.Equal(original.ConfigHash, restarted.ConfigHash);
        Assert.Equal(42UL, restarted.MasterSeed);
        Assert.Equal(64, restarted.Configuration.Width);
        Assert.Equal(33, restarted.Configuration.Height);
        Assert.True(
            RunScoreIdentity.FromRun(original).IsSameScoreCategory(
                RunScoreIdentity.FromRun(restarted)));
    }

    [Fact]
    public void Classic_and_vibe_never_share_a_score_category()
    {
        var classic = RunScoreIdentity.FromRun(
            SnakeRun.Create(1UL, RunModeCatalog.CreateConfig(RunModeCatalog.Classic)));
        var vibe = RunScoreIdentity.FromRun(
            SnakeRun.Create(1UL, RunModeCatalog.CreateConfig(RunModeCatalog.Vibe)));

        Assert.False(classic.IsSameScoreCategory(vibe));
    }

    [Fact]
    public void Vibe_adaptation_opt_out_has_a_separate_score_category()
    {
        var enabled = RunModeCatalog.CreateConfig(RunModeCatalog.Vibe);
        var disabled = RunModeCatalog.CreateConfig(
            RunModeCatalog.Vibe,
            enableAdaptation: false);

        Assert.Equal(
            RunModeCatalog.VibeAdaptiveScoreCategoryId,
            RunModeCatalog.GetScoreCategoryId(enabled));
        Assert.Equal(
            RunModeCatalog.VibeFixedScoreCategoryId,
            RunModeCatalog.GetScoreCategoryId(disabled));
        Assert.NotEqual(enabled.ComputeConfigHash(), disabled.ComputeConfigHash());
        Assert.False(
            RunScoreIdentity.FromRun(SnakeRun.Create(1UL, enabled)).IsSameScoreCategory(
                RunScoreIdentity.FromRun(SnakeRun.Create(1UL, disabled))));
    }

    [Fact]
    public void Classic_rejects_any_attempt_to_enable_adaptation()
    {
        Assert.Throws<ArgumentException>(() =>
            RunModeCatalog.CreateConfig(RunModeCatalog.Classic, enableAdaptation: true));
        Assert.Throws<ArgumentException>(() =>
            SnakeRun.Create(
                1UL,
                RunModeCatalog.CreateConfig(RunModeCatalog.Classic) with
                {
                    EnableAdaptation = true,
                    AdaptivePolicyId = AdaptiveDifficultyPolicy.CurrentPolicyId,
                }));
    }

    [Fact]
    public void Catalog_rejects_unknown_and_drifted_definitions()
    {
        Assert.Throws<ArgumentException>(() => RunModeCatalog.Get("unknown", 1));
        Assert.Throws<ArgumentException>(() => RunModeCatalog.Get("classic", 2));
        Assert.Throws<ArgumentException>(
            () => RunModeCatalog.CreateConfig(
                RunModeCatalog.Classic with { ScoreCategoryId = "drifted" }));
    }
}
