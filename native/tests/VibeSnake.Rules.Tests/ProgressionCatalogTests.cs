namespace VibeSnake.Rules.Tests;

public sealed class ProgressionCatalogTests
{
    [Fact]
    public void Goal_catalog_exposes_exact_progress_in_all_three_lanes()
    {
        Assert.Equal(20, ProgressionGoalCatalog.Goals.Count);
        Assert.Equal(
            Enum.GetValues<ProgressionGoalLane>(),
            ProgressionGoalCatalog.Goals
                .Select(goal => goal.Lane)
                .Distinct()
                .Order()
                .ToArray());
        Assert.Equal(20, ProgressionGoalCatalog.Goals.Select(goal => goal.Id).Distinct().Count());
        Assert.Equal(20, ProgressionGoalCatalog.Goals.Select(goal => goal.Reward.Id).Distinct().Count());
        Assert.All(ProgressionGoalCatalog.Goals, goal =>
        {
            Assert.True(goal.Target > 0);
            Assert.False(string.IsNullOrWhiteSpace(goal.ExactRequirement));
            Assert.Equal(SnakeRun.RulesetId, goal.RulesetId);
            Assert.Equal(SnakeRun.RulesVersion, goal.RulesVersion);
            Assert.Equal(AchievementModeEligibility.Vibe, goal.ModeEligibility);
        });

        var metrics = new ProgressionMetrics(
            HighestScore: 250,
            HighestCombo: 5,
            SavedLoadouts: 1,
            CosmeticSetsUnlocked: 2);
        var progress = ProgressionGoalCatalog.BuildProgress(metrics, "identity_three_sets");
        var century = progress.Single(item => item.Definition.Id == "century");
        var identity = progress.Single(item => item.Definition.Id == "identity_three_sets");
        Assert.Equal("100/100", century.ExactProgress);
        Assert.True(century.Completed);
        Assert.Equal("2/3", identity.ExactProgress);
        Assert.True(identity.Highlighted);
        Assert.False(identity.Completed);
    }

    [Fact]
    public void Human_progress_is_monotonic_and_nonhuman_contexts_are_isolated()
    {
        var metrics = new ProgressionMetrics();
        var run = new RunAchievementMetrics(
            Score: 123,
            MaxCombo: 6,
            Length: 9,
            FoodEaten: 7,
            WrapCount: 4,
            NearMisses: 3,
            PowerupsCollected: 2,
            SurvivalTicks: 700,
            IsTerminal: true);
        var human = metrics.MergeHumanRun(run, ScoreRunContextCatalog.NormalHuman);
        var lower = human.MergeHumanRun(run with { Score = 1 }, ScoreRunContextCatalog.NormalHuman);

        Assert.Equal(2, lower.CompletedHumanRuns);
        Assert.Equal(123, lower.HighestScore);
        Assert.Equal(7, lower.MostFoodInRun);
        Assert.Equal(metrics, metrics.MergeHumanRun(run, ScoreRunContextCatalog.Ai));
        Assert.Equal(metrics, metrics.MergeHumanRun(run, ScoreRunContextCatalog.Replay));
        Assert.Equal(metrics, metrics.MergeHumanRun(
            run,
            ScoreRunContextCatalog.NormalHuman with { CompetitiveEligible = false }));
        Assert.Throws<ArgumentException>(() => metrics.MergeHumanRun(
            run with { IsTerminal = false },
            ScoreRunContextCatalog.NormalHuman));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            metrics.WithPresentationProgress(-1, 0, 0));
    }

    [Fact]
    public void Broadcast_tour_is_finite_reachable_and_expression_only()
    {
        var validation = BroadcastTourCatalog.Validate();

        Assert.True(validation.Passed);
        Assert.Equal(12, validation.EventCount);
        Assert.Equal(4, validation.TierCount);
        Assert.Equal(12, validation.ReachableEventCount);
        Assert.Equal(0, validation.DependencyCycleCount);
        Assert.Equal(0, validation.DuplicateRewardCount);
        Assert.Equal(0, validation.ImpossibleGoalCount);
        Assert.Equal(0, validation.RulesContaminationCount);
        Assert.Equal(0, validation.MechanicalRewardCount);
        Assert.Equal(0, validation.UnknownContextCount);
        Assert.All(BroadcastTourCatalog.Events, item =>
        {
            Assert.True(item.ImmediateRematch);
            Assert.True(item.ReplayAvailable);
            Assert.True(item.PracticeNoncompetitive);
            Assert.NotNull(item.FixedSeed);
            Assert.Equal(TourSeedPolicyKind.Fixed, item.SeedPolicy);
            Assert.All(
                ["speed", "life", "shield", "multiplier", "immunity"],
                forbidden => Assert.DoesNotContain(
                    forbidden,
                    item.Reward.DisplayName,
                    StringComparison.OrdinalIgnoreCase));
        });
    }

    [Fact]
    public void Catalog_lookup_and_unknown_metric_paths_fail_closed()
    {
        Assert.Equal("century", ProgressionGoalCatalog.Find("century")!.Id);
        Assert.Null(ProgressionGoalCatalog.Find("missing"));
        Assert.Throws<ArgumentException>(() => ProgressionGoalCatalog.Find(" "));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ProgressionMetrics().ValueFor((ProgressionMetric)byte.MaxValue));
    }

    [Fact]
    public void Curated_cosmetic_sets_pass_both_profiles_and_cannot_change_rules()
    {
        var validation = CosmeticSetCatalog.Validate();
        Assert.True(validation.Passed);
        Assert.Equal(8, validation.SetCount);
        Assert.Equal(8, validation.QuietProfileCount);
        Assert.Equal(8, validation.MaximumVibeProfileCount);
        Assert.Equal(0, validation.MechanicalFieldCount);
        Assert.Single(CosmeticSetCatalog.Sets, item => item.AvailableFromStart);
        Assert.Equal("redline", CosmeticSetCatalog.Find("redline")!.Id);
        Assert.Null(CosmeticSetCatalog.Find("missing"));

        foreach (var cosmetic in CosmeticSetCatalog.Sets)
        {
            _ = cosmetic;
            var left = SnakeRun.Create(42UL);
            var right = SnakeRun.Create(42UL);
            for (var step = 0; step < 64 && left.Status == RunStatus.Running; step++)
            {
                var direction = step % 17 == 0 ? Direction.Down : Direction.Right;
                left.QueueDirection(direction);
                right.QueueDirection(direction);
                Assert.Equal(left.Step().StateHash, right.Step().StateHash);
            }
        }
    }

    [Fact]
    public void Progression_notifications_are_bounded_ordered_and_reduced_motion_safe()
    {
        var queue = new ProgressionNotificationQueue();
        Assert.True(queue.Enqueue("first", "FIRST REWARD", reducedMotion: true));
        Assert.False(queue.Enqueue("first", "DUPLICATE", reducedMotion: false));
        for (var index = 1; index < ProgressionNotificationQueue.MaximumPending; index++)
        {
            Assert.True(queue.Enqueue(
                "reward-" + index,
                "REWARD " + index,
                reducedMotion: false));
        }

        Assert.False(queue.Enqueue("overflow", "OVERFLOW", reducedMotion: false));
        Assert.Equal(ProgressionNotificationQueue.MaximumPending, queue.Count);
        Assert.True(queue.TryDequeue(out var first));
        Assert.Equal("first", first!.Id);
        Assert.False(first.MotionEnabled);
        Assert.Equal(ProgressionNotificationQueue.MinimumReadableMilliseconds, first.MinimumVisibleMilliseconds);
        Assert.True(queue.Enqueue("first", "FIRST AGAIN", reducedMotion: false));
        queue.Clear();
        Assert.False(queue.TryDequeue(out _));
    }
}
