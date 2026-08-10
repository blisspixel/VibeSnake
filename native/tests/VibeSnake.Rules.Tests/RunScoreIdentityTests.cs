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
        Assert.Equal(RunModeCatalog.VibeId, identity.ModeId);
        Assert.Equal(1, identity.ModeVersion);
        Assert.Equal(RunModeCatalog.VibeFixedScoreCategoryId, identity.ScoreCategoryId);
        Assert.Equal("vibe-fixed-cadence-v1", identity.DifficultyPolicyId);
        Assert.False(identity.AdaptationEnabled);
        Assert.Equal(AdaptiveDifficultyPolicy.DisabledPolicyId, identity.AdaptivePolicyId);
        Assert.Equal(AdaptiveDifficultyState.Disabled, identity.AdaptiveStateAtCapture);
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
    public void Achievement_candidate_flag_separates_product_score_category()
    {
        // Product runs enable candidates; default fixtures leave them off.
        // Fair-score categories must not mix those configurations.
        var fixture = RunScoreIdentity.FromRun(
            SnakeRun.Create(1UL, new RunConfig(EnableAchievementCandidates: false)));
        var product = RunScoreIdentity.FromRun(
            SnakeRun.Create(1UL, new RunConfig(EnableAchievementCandidates: true)));

        Assert.False(fixture.IsSameScoreCategory(product));
        Assert.NotEqual(fixture.ConfigHash, product.ConfigHash);
    }

    [Fact]
    public void FromRun_rejects_null()
    {
        Assert.Throws<ArgumentNullException>(() => RunScoreIdentity.FromRun(null!));
    }

    [Fact]
    public void Dda_enabled_and_disabled_vibe_metadata_never_share_a_category()
    {
        var enabled = RunScoreIdentity.FromRun(
            SnakeRun.Create(1UL, RunModeCatalog.CreateConfig(RunModeCatalog.Vibe)));
        var disabled = RunScoreIdentity.FromRun(
            SnakeRun.Create(
                1UL,
                RunModeCatalog.CreateConfig(
                    RunModeCatalog.Vibe,
                    enableAdaptation: false)));

        Assert.True(enabled.AdaptationEnabled);
        Assert.Equal(AdaptiveDifficultyPolicy.CurrentPolicyId, enabled.AdaptivePolicyId);
        Assert.Equal(RunModeCatalog.VibeAdaptiveScoreCategoryId, enabled.ScoreCategoryId);
        Assert.False(disabled.AdaptationEnabled);
        Assert.Equal(RunModeCatalog.VibeFixedScoreCategoryId, disabled.ScoreCategoryId);
        Assert.False(enabled.IsSameScoreCategory(disabled));
    }

    [Fact]
    public void Score_identity_caption_fields_are_stable_for_support_display()
    {
        var identity = RunScoreIdentity.FromRun(SnakeRun.Create(3UL));
        Assert.StartsWith("vibesnake-core@4", identity.RulesetContractId, StringComparison.Ordinal);
        Assert.Equal(64, identity.ConfigHash.Length);
        Assert.True(identity.ConfigHash.Length >= 12);
    }

    [Fact]
    public void Run_kind_and_seed_category_catalog_is_closed_and_separates_scores()
    {
        Assert.Equal(8, ScoreRunContextCatalog.All.Count);
        Assert.Equal(
            8,
            ScoreRunContextCatalog.All
                .Select(context => $"{context.RunKindId}|{context.SeedCategoryId}")
                .Distinct(StringComparer.Ordinal)
                .Count());
        Assert.Equal(
            2,
            ScoreRunContextCatalog.All.Count(context => context.CompetitiveEligible));

        var run = SnakeRun.Create(4UL);
        var normal = RunScoreIdentity.FromRun(run, ScoreRunContextCatalog.NormalHuman);
        var challenge = RunScoreIdentity.FromRun(run, ScoreRunContextCatalog.SeededChallenge);
        var tutorial = RunScoreIdentity.FromRun(run, ScoreRunContextCatalog.Tutorial);

        Assert.Equal(ScoreRunContextCatalog.NormalHumanRunKind, normal.RunKindId);
        Assert.Equal(ScoreRunContextCatalog.FreshLocalSeedCategory, normal.SeedCategoryId);
        Assert.True(normal.CompetitiveEligible);
        Assert.True(challenge.CompetitiveEligible);
        Assert.False(tutorial.CompetitiveEligible);
        Assert.False(normal.IsSameScoreCategory(challenge));
        Assert.False(normal.IsSameScoreCategory(tutorial));
        Assert.Throws<ArgumentException>(() => ScoreRunContextCatalog.Get("future", "future"));
        Assert.Throws<ArgumentException>(() => RunScoreIdentity.FromRun(
            run,
            ScoreRunContextCatalog.NormalHuman with { CompetitiveEligible = false }));
    }
}
