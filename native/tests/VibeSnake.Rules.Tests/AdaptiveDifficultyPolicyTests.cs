namespace VibeSnake.Rules.Tests;

public sealed class AdaptiveDifficultyPolicyTests
{
    [Fact]
    public void Enabled_vibe_policy_has_exact_support_standard_and_pressure_bounds()
    {
        var config = RunModeCatalog.CreateConfig(RunModeCatalog.Vibe);

        var supportOdd = AdaptiveDifficultyPolicy.Evaluate(config, 1, 0, 100);
        var supportEven = AdaptiveDifficultyPolicy.Evaluate(config, 2, 2, 100);
        var standard = AdaptiveDifficultyPolicy.Evaluate(config, 3, 3, 300);
        var pressureNormal = AdaptiveDifficultyPolicy.Evaluate(config, 3, 10, 300);
        var pressureExtra = AdaptiveDifficultyPolicy.Evaluate(config, 4, 10, 300);

        Assert.Equal(
            new AdaptiveDifficultyDecision(
                AdaptiveDifficultyState.Support,
                0,
                "Low hunger and combo below 3 slow hunger drain to every other step."),
            supportOdd);
        Assert.Equal(AdaptiveDifficultyState.Support, supportEven.State);
        Assert.Equal(1, supportEven.HungerDrainTicks);
        Assert.Equal(AdaptiveDifficultyState.Standard, standard.State);
        Assert.Equal(1, standard.HungerDrainTicks);
        Assert.Equal(AdaptiveDifficultyState.Pressure, pressureNormal.State);
        Assert.Equal(1, pressureNormal.HungerDrainTicks);
        Assert.Equal(AdaptiveDifficultyState.Pressure, pressureExtra.State);
        Assert.Equal(2, pressureExtra.HungerDrainTicks);
    }

    [Fact]
    public void Opted_out_vibe_uses_fixed_hunger_drain()
    {
        var config = RunModeCatalog.CreateConfig(
            RunModeCatalog.Vibe,
            enableAdaptation: false);

        foreach (var tick in Enumerable.Range(0, 16))
        {
            var decision = AdaptiveDifficultyPolicy.Evaluate(config, tick, 20, 1);
            Assert.Equal(AdaptiveDifficultyState.Disabled, decision.State);
            Assert.Equal(1, decision.HungerDrainTicks);
        }
    }

    [Fact]
    public void Classic_has_no_hunger_drain_or_adaptive_state()
    {
        var config = RunModeCatalog.CreateConfig(RunModeCatalog.Classic);
        var decision = AdaptiveDifficultyPolicy.Evaluate(config, 4, 20, 1);

        Assert.Equal(AdaptiveDifficultyState.Disabled, decision.State);
        Assert.Equal(0, decision.HungerDrainTicks);
    }

    [Fact]
    public void Support_and_pressure_change_only_the_declared_hunger_drain()
    {
        var config = RunModeCatalog.CreateConfig(RunModeCatalog.Vibe) with
        {
            Width = 20,
            Height = 4,
            PowerSpawnIntervalTicks = 0,
        };
        var support = SnakeRun.CreateForTesting(
            config,
            [new GridPoint(2, 1)],
            Direction.Right,
            food: new GridPoint(19, 3),
            hungerTicksRemaining: 100,
            comboCount: 0);
        var pressure = SnakeRun.CreateForTesting(
            config,
            [new GridPoint(2, 1)],
            Direction.Right,
            food: new GridPoint(19, 3),
            hungerTicksRemaining: 300,
            comboCount: 10,
            tick: 3);

        support.Step();
        pressure.Step();

        Assert.Equal(100, support.HungerTicksRemaining);
        Assert.Equal(298, pressure.HungerTicksRemaining);
        Assert.Equal(0, support.Score);
        Assert.Equal(0, pressure.Score);
        Assert.Equal(AdaptiveDifficultyState.Support, support.GetSnapshot().AdaptiveDifficultyState);
        Assert.Equal(AdaptiveDifficultyState.Pressure, pressure.GetSnapshot().AdaptiveDifficultyState);
    }

    [Fact]
    public void Enabled_policy_round_trips_canonical_state_and_replay()
    {
        var config = RunModeCatalog.CreateConfig(RunModeCatalog.Vibe) with
        {
            PowerSpawnIntervalTicks = 0,
        };
        var run = SnakeRun.Create(808UL, config);
        var state = run.SerializeCanonicalState();
        var restored = SnakeRun.RestoreCanonicalState(state);

        Assert.Equal(state, restored.SerializeCanonicalState());
        Assert.Equal(run.ConfigHash, restored.ConfigHash);
        Assert.True(restored.Configuration.EnableAdaptation);
        Assert.Equal(AdaptiveDifficultyPolicy.CurrentPolicyId, restored.Configuration.AdaptivePolicyId);

        var replay = RunReplay.Capture(run, [Array.Empty<Direction>()]);
        Assert.True(replay.Verify().IsValid);
        Assert.Equal(RunModeCatalog.Vibe, new RunReplayPlayback(replay).Mode);
    }

    [Fact]
    public void Policy_rejects_invalid_inputs_and_mismatched_contracts()
    {
        var config = RunModeCatalog.CreateConfig(RunModeCatalog.Vibe);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            AdaptiveDifficultyPolicy.Evaluate(config, -1, 0, 100));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            AdaptiveDifficultyPolicy.Evaluate(config, 0, -1, 100));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            AdaptiveDifficultyPolicy.Evaluate(config, 0, 0, -1));
        Assert.Throws<ArgumentException>(() =>
            SnakeRun.Create(1UL, config with { AdaptivePolicyId = "unknown" }));
        Assert.Throws<ArgumentException>(() =>
            SnakeRun.Create(
                1UL,
                config with
                {
                    EnableAdaptation = false,
                    AdaptivePolicyId = AdaptiveDifficultyPolicy.CurrentPolicyId,
                }));
    }
}
