namespace VibeSnake.Rules.Tests;

public sealed class BroadcastTourSessionTests
{
    [Fact]
    public void Cards_are_finite_branchable_and_dependency_gated()
    {
        var initial = BroadcastTourSession.BuildCards([]);

        Assert.Equal(12, initial.Count);
        Assert.Single(initial, card => card.State == BroadcastTourEventState.Available);
        Assert.Equal(
            "local-first-signal",
            initial.Single(card => card.State == BroadcastTourEventState.Available).Event.Id);

        var afterFirst = BroadcastTourSession.BuildCards(["local-first-signal"]);
        Assert.Equal(2, afterFirst.Count(card => card.State == BroadcastTourEventState.Available));
        Assert.Equal(
            ["local-hold-line", "local-wrap-school"],
            afterFirst
                .Where(card => card.State == BroadcastTourEventState.Available)
                .Select(card => card.Event.Id)
                .Order(StringComparer.Ordinal)
                .ToArray());
        Assert.Throws<ArgumentException>(() =>
            BroadcastTourSession.BuildCards(["district-power-route"]));
        Assert.Throws<ArgumentException>(() =>
            BroadcastTourSession.BuildCards(["missing"]));
        Assert.Throws<ArgumentException>(() =>
            BroadcastTourSession.BuildCards(["local-first-signal", "local-first-signal"]));
    }

    [Fact]
    public void Every_event_constructs_the_exact_fixed_seed_and_rules_identity()
    {
        foreach (var item in BroadcastTourCatalog.Events)
        {
            var left = BroadcastTourSession.CreateRun(item);
            var right = BroadcastTourSession.CreateRun(item);

            Assert.Equal(item.FixedSeed, left.MasterSeed);
            Assert.Equal(item.ModeId, left.Configuration.ModeId);
            Assert.Equal(item.ModeVersion, left.Configuration.ModeVersion);
            Assert.Equal(item.ScoreCategoryId, left.ScoreCategoryId);
            Assert.Equal(left.ComputeStateHash(), right.ComputeStateHash());
        }

        Assert.Throws<ArgumentNullException>(() =>
            BroadcastTourSession.CreateRun(null!));
        Assert.Throws<ArgumentException>(() =>
            BroadcastTourSession.CreateRun(
                BroadcastTourCatalog.Events[0] with { RivalId = "forged" }));
    }

    [Fact]
    public void Outcome_uses_exact_terminal_run_metrics_and_rejects_wrong_identity()
    {
        var item = BroadcastTourCatalog.Events.Single(eventCard =>
            eventCard.Id == "local-first-signal");
        var run = BroadcastTourSession.CreateRun(item);
        while (run.Status == RunStatus.Running)
        {
            run.Step();
        }

        var outcome = BroadcastTourSession.Evaluate(item, run);

        Assert.Equal(item.Id, outcome.EventId);
        Assert.Equal(item.PrimaryGoal.Target, outcome.PrimaryTarget);
        Assert.Equal($"{Math.Min(run.Score, item.PrimaryGoal.Target)}/{item.PrimaryGoal.Target}", outcome.PrimaryProgress);
        Assert.Null(outcome.StyleCurrent);
        Assert.Null(outcome.StyleTarget);
        Assert.Null(outcome.StyleComplete);
        Assert.Throws<ArgumentException>(() =>
            BroadcastTourSession.Evaluate(item, SnakeRun.Create(99UL)));
        Assert.Throws<ArgumentException>(() =>
            BroadcastTourSession.Evaluate(item with { RivalId = "forged" }, run));
        Assert.Throws<ArgumentNullException>(() =>
            BroadcastTourSession.Evaluate(item, null!));

        var classic = CreateTerminalRun(RunModeCatalog.CreateConfig(RunModeCatalog.Classic));
        var fixedVibe = CreateTerminalRun(
            RunModeCatalog.CreateConfig(RunModeCatalog.Vibe, enableAdaptation: false));
        var wrongSeed = CreateTerminalRun(
            RunModeCatalog.CreateConfig(RunModeCatalog.Vibe, enableAdaptation: true));
        Assert.Throws<ArgumentException>(() => BroadcastTourSession.Evaluate(item, classic));
        Assert.Throws<ArgumentException>(() => BroadcastTourSession.Evaluate(item, fixedVibe));
        Assert.Throws<ArgumentException>(() => BroadcastTourSession.Evaluate(item, wrongSeed));
    }

    [Fact]
    public void Exact_metric_and_style_evaluation_covers_every_single_run_fact()
    {
        var metrics = new RunAchievementMetrics(
            Score: 101,
            MaxCombo: 7,
            Length: 13,
            FoodEaten: 9,
            WrapCount: 5,
            NearMisses: 11,
            PowerupsCollected: 3,
            SurvivalTicks: 777,
            IsTerminal: true);
        Dictionary<ProgressionMetric, int> expected = new()
        {
            [ProgressionMetric.HighestScore] = 101,
            [ProgressionMetric.HighestCombo] = 7,
            [ProgressionMetric.LongestLength] = 13,
            [ProgressionMetric.MostFoodInRun] = 9,
            [ProgressionMetric.MostWrapsInRun] = 5,
            [ProgressionMetric.MostNearMissesInRun] = 11,
            [ProgressionMetric.MostPowersInRun] = 3,
            [ProgressionMetric.LongestSurvivalTicks] = 777,
        };
        foreach (var pair in expected)
        {
            Assert.Equal(pair.Value, BroadcastTourSession.ValueForRun(metrics, pair.Key));
        }

        Assert.Throws<InvalidOperationException>(() =>
            BroadcastTourSession.ValueForRun(metrics, ProgressionMetric.CompletedHumanRuns));
        var styleEvent = BroadcastTourCatalog.Events.Single(item =>
            item.Id == "local-wrap-school");
        var styleOutcome = BroadcastTourSession.EvaluateMetrics(styleEvent, metrics);
        Assert.True(styleOutcome.PrimaryComplete);
        Assert.True(styleOutcome.StyleComplete);
        Assert.Equal("1/1", styleOutcome.StyleProgress);
        Assert.Throws<ArgumentException>(() =>
            BroadcastTourSession.EvaluateMetrics(
                styleEvent,
                metrics with { IsTerminal = false }));
    }

    private static SnakeRun CreateTerminalRun(RunConfig config)
    {
        var run = SnakeRun.CreateForTesting(
            config,
            [
                new GridPoint(0, 0),
                new GridPoint(1, 0),
                new GridPoint(1, 1),
                new GridPoint(0, 1),
            ],
            Direction.Right,
            new GridPoint(4, 3),
            hungerTicksRemaining: config.StarvationTicks);
        run.Step();
        Assert.NotEqual(RunStatus.Running, run.Status);

        return run;
    }
}
