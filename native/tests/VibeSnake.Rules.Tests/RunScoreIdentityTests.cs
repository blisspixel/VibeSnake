namespace VibeSnake.Rules.Tests;

public sealed class RunScoreIdentityTests
{
    [Fact]
    public void FromRun_captures_ruleset_config_and_score()
    {
        var run = SnakeRun.Create(7UL, new RunConfig(Width: 16, Height: 12));
        var identity = RunScoreIdentity.FromRun(run);

        Assert.Equal(SnakeRun.RulesetId, identity.RulesetId);
        Assert.Equal(SnakeRun.RulesVersion, identity.RulesVersion);
        Assert.Equal("vibesnake-core@4", identity.RulesetContractId);
        Assert.Equal(run.ConfigHash, identity.ConfigHash);
        Assert.Equal(RunConfig.ConfigHashAlgorithmId, identity.ConfigHashAlgorithm);
        Assert.Equal(run.Score, identity.Score);
        Assert.Equal(RunStatus.Running, identity.Status);
        Assert.Equal(DeathCause.None, identity.DeathCause);
    }

    [Fact]
    public void Same_config_shares_score_category_even_when_scores_differ()
    {
        var configuration = new RunConfig(Width: 20, Height: 10);
        var left = RunScoreIdentity.FromRun(SnakeRun.Create(1UL, configuration));
        var rightRun = SnakeRun.Create(2UL, configuration);
        // Advance a few steps so scores may diverge later; category uses config only.
        rightRun.Step();
        var right = RunScoreIdentity.FromRun(rightRun) with { Score = left.Score + 50 };

        Assert.True(left.IsSameScoreCategory(right));
        Assert.NotEqual(left.Score, right.Score);
    }

    [Fact]
    public void Different_config_is_not_the_same_score_category()
    {
        var left = RunScoreIdentity.FromRun(
            SnakeRun.Create(1UL, new RunConfig(EnableNearMiss: false)));
        var right = RunScoreIdentity.FromRun(
            SnakeRun.Create(1UL, new RunConfig(EnableNearMiss: true)));

        Assert.False(left.IsSameScoreCategory(right));
        Assert.NotEqual(left.ConfigHash, right.ConfigHash);
    }

    [Fact]
    public void FromRun_rejects_null()
    {
        Assert.Throws<ArgumentNullException>(() => RunScoreIdentity.FromRun(null!));
    }
}
