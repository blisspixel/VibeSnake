namespace VibeSnake.Rules.Tests;

public sealed class AchievementCatalogTests
{
    [Fact]
    public void Definitions_are_unique_and_match_condition_keys()
    {
        var ids = AchievementCatalog.Definitions.Select(definition => definition.Id).ToArray();
        Assert.Equal(ids.Length, ids.Distinct(StringComparer.Ordinal).Count());
        Assert.All(ids, id => Assert.NotNull(AchievementCatalog.Find(id)));
        Assert.Null(AchievementCatalog.Find("does_not_exist"));
    }

    [Fact]
    public void Non_terminal_metrics_yield_no_candidates_by_default()
    {
        var metrics = new RunAchievementMetrics(
            Score: 10_000,
            MaxCombo: 20,
            Length: 40,
            FoodEaten: 50,
            WrapCount: 10,
            NearMisses: 20,
            PowerupsCollected: 10,
            SurvivalTicks: 10_000,
            IsTerminal: false);

        Assert.Empty(AchievementCatalog.EvaluateCandidates(metrics));
    }

    [Fact]
    public void Terminal_metrics_earn_matching_score_and_length_candidates()
    {
        var metrics = new RunAchievementMetrics(
            Score: 150,
            MaxCombo: 6,
            Length: 12,
            FoodEaten: 8,
            WrapCount: 3,
            NearMisses: 0,
            PowerupsCollected: 0,
            SurvivalTicks: 100,
            IsTerminal: true);

        var earned = AchievementCatalog.EvaluateCandidates(metrics);
        Assert.Contains("first_bite", earned);
        Assert.Contains("century", earned);
        Assert.Contains("combo_starter", earned);
        Assert.Contains("getting_longer", earned);
        Assert.Contains("growing_strong", earned);
        Assert.Contains("just_a_taste", earned);
        Assert.Contains("wrap_around", earned);
        Assert.DoesNotContain("legend", earned);
        Assert.DoesNotContain("close_call", earned);
    }

    [Fact]
    public void Already_unlocked_ids_are_excluded()
    {
        var metrics = new RunAchievementMetrics(
            Score: 100,
            MaxCombo: 0,
            Length: 1,
            FoodEaten: 0,
            WrapCount: 0,
            NearMisses: 0,
            PowerupsCollected: 0,
            SurvivalTicks: 1,
            IsTerminal: true);

        var earned = AchievementCatalog.EvaluateCandidates(
            metrics,
            alreadyUnlocked: new HashSet<string>(StringComparer.Ordinal) { "first_bite", "century" });

        Assert.DoesNotContain("first_bite", earned);
        Assert.DoesNotContain("century", earned);
    }

    [Fact]
    public void Survival_thresholds_use_rules_tick_milliseconds()
    {
        // 30 seconds at 50 ms ticks is 600 ticks.
        var shortRun = new RunAchievementMetrics(
            Score: 0,
            MaxCombo: 0,
            Length: 1,
            FoodEaten: 0,
            WrapCount: 0,
            NearMisses: 0,
            PowerupsCollected: 0,
            SurvivalTicks: 599,
            IsTerminal: true);
        var longEnough = new RunAchievementMetrics(
            Score: 0,
            MaxCombo: 0,
            Length: 1,
            FoodEaten: 0,
            WrapCount: 0,
            NearMisses: 0,
            PowerupsCollected: 0,
            SurvivalTicks: 600,
            IsTerminal: true);

        Assert.DoesNotContain(
            "quick_reflexes",
            AchievementCatalog.EvaluateCandidates(shortRun));
        Assert.Contains(
            "quick_reflexes",
            AchievementCatalog.EvaluateCandidates(longEnough));
    }

    [Fact]
    public void SnakeRun_metrics_track_food_combo_and_wraps()
    {
        var run = SnakeRun.CreateForTesting(
            new RunConfig(
                Width: 8,
                Height: 6,
                StarvationTicks: 100,
                PowerSpawnIntervalTicks: 0),
            [new GridPoint(1, 1)],
            Direction.Right,
            food: new GridPoint(2, 1),
            hungerTicksRemaining: 100);

        run.Step();
        Assert.Equal(1, run.SessionFoodEaten);
        Assert.Equal(1, run.SessionMaxCombo);

        // Wrap horizontally from the right edge.
        var wrapRun = SnakeRun.CreateForTesting(
            new RunConfig(Width: 5, Height: 4, StarvationTicks: 100, PowerSpawnIntervalTicks: 0),
            [new GridPoint(4, 1)],
            Direction.Right,
            food: new GridPoint(0, 0),
            hungerTicksRemaining: 100);
        wrapRun.Step();
        Assert.Equal(1, wrapRun.SessionWraps);

        var metrics = wrapRun.ToAchievementMetrics();
        Assert.False(metrics.IsTerminal);
        Assert.Equal(wrapRun.SessionWraps, metrics.WrapCount);
    }

    [Fact]
    public void Terminal_death_emits_achievement_candidate_events()
    {
        var run = SnakeRun.CreateForTesting(
            new RunConfig(
                Width: 8,
                Height: 6,
                StarvationTicks: 100,
                PowerSpawnIntervalTicks: 0,
                EnableAchievementCandidates: true),
            [new GridPoint(1, 1)],
            Direction.Right,
            food: new GridPoint(7, 5),
            hungerTicksRemaining: 1,
            score: 150,
            comboCount: 6);

        var result = run.Step();

        Assert.Equal(RunStatus.Dead, run.Status);
        Assert.Equal(DeathCause.Starvation, run.DeathCause);
        Assert.Contains(
            result.OrderedEvents,
            detail => detail.Kind == RunEventKind.AchievementCandidate);
        Assert.True(result.Events.HasFlag(RunEvent.AchievementCandidate));

        var firstBiteIndex = AchievementCatalog.IndexOf("first_bite");
        Assert.True(firstBiteIndex >= 0);
        Assert.Contains(
            result.OrderedEvents,
            detail =>
                detail.Kind == RunEventKind.AchievementCandidate
                && detail.Value == firstBiteIndex);
        Assert.Contains(
            result.OrderedEvents,
            detail =>
                detail.Kind == RunEventKind.AchievementCandidate
                && detail.Value == AchievementCatalog.IndexOf("century"));
    }

    [Fact]
    public void Terminal_step_emits_achievement_candidates_only_once()
    {
        var run = SnakeRun.CreateForTesting(
            new RunConfig(
                Width: 8,
                Height: 6,
                StarvationTicks: 100,
                PowerSpawnIntervalTicks: 0,
                EnableAchievementCandidates: true),
            [new GridPoint(1, 1)],
            Direction.Right,
            food: new GridPoint(7, 5),
            hungerTicksRemaining: 1,
            score: 150,
            comboCount: 6);

        var first = run.Step();
        var second = run.Step();

        Assert.Equal(RunStatus.Dead, run.Status);
        Assert.Contains(
            first.OrderedEvents,
            detail => detail.Kind == RunEventKind.AchievementCandidate);
        Assert.DoesNotContain(
            second.OrderedEvents,
            detail => detail.Kind == RunEventKind.AchievementCandidate);
        Assert.False(second.Events.HasFlag(RunEvent.AchievementCandidate));
    }

    [Fact]
    public void Terminal_death_skips_achievement_candidates_when_flag_default_off()
    {
        // Default EnableAchievementCandidates:false keeps dual-runtime parity
        // fixtures stable until Python also emits achievement_candidate.
        var run = SnakeRun.CreateForTesting(
            new RunConfig(
                Width: 8,
                Height: 6,
                StarvationTicks: 100,
                PowerSpawnIntervalTicks: 0),
            [new GridPoint(1, 1)],
            Direction.Right,
            food: new GridPoint(7, 5),
            hungerTicksRemaining: 1,
            score: 150,
            comboCount: 6);

        var result = run.Step();

        Assert.Equal(RunStatus.Dead, run.Status);
        Assert.DoesNotContain(
            result.OrderedEvents,
            detail => detail.Kind == RunEventKind.AchievementCandidate);
        Assert.False(result.Events.HasFlag(RunEvent.AchievementCandidate));
    }

    [Fact]
    public void IndexOf_and_DefinitionAt_round_trip()
    {
        var index = AchievementCatalog.IndexOf("century");
        Assert.True(index >= 0);
        Assert.Equal("century", AchievementCatalog.DefinitionAt(index)!.Id);
        Assert.Equal(-1, AchievementCatalog.IndexOf("missing_id"));
        Assert.Null(AchievementCatalog.DefinitionAt(-1));
        Assert.Null(AchievementCatalog.DefinitionAt(10_000));
    }

    [Fact]
    public void Catalog_size_and_order_match_dual_runtime_contract()
    {
        // Keep aligned with vibesnake.qa.achievement_candidates.DEFINITIONS.
        string[] expectedIds =
        [
            "first_bite",
            "century",
            "high_roller",
            "legend",
            "just_a_taste",
            "getting_longer",
            "growing_strong",
            "serpent",
            "combo_starter",
            "combo_king",
            "wrap_around",
            "close_call",
            "powered_up",
            "power_hungry",
            "quick_reflexes",
            "endurance",
            "marathon",
        ];
        Assert.Equal(expectedIds, AchievementCatalog.Definitions.Select(definition => definition.Id));
        Assert.Equal(0, AchievementCatalog.IndexOf("first_bite"));
        Assert.Equal(16, AchievementCatalog.IndexOf("marathon"));
    }

    [Fact]
    public void Restart_clears_session_achievement_counters()
    {
        var run = SnakeRun.CreateForTesting(
            new RunConfig(
                Width: 8,
                Height: 6,
                StarvationTicks: 200,
                PowerSpawnIntervalTicks: 0),
            [new GridPoint(1, 1)],
            Direction.Right,
            food: new GridPoint(2, 1),
            hungerTicksRemaining: 100);

        run.Step();
        Assert.True(run.SessionFoodEaten >= 1);

        // Force terminal, then restart into a fresh run.
        while (run.Status == RunStatus.Running)
        {
            run.Step();
        }

        var restarted = run.Restart(99UL);
        Assert.Equal(0, restarted.SessionFoodEaten);
        Assert.Equal(0, restarted.SessionWraps);
        Assert.Equal(0, restarted.SessionNearMisses);
        Assert.Equal(0, restarted.SessionPowerupsCollected);
        Assert.Equal(0, restarted.SessionMaxCombo);
        Assert.Equal(RunStatus.Running, restarted.Status);
    }

    [Fact]
    public void Mid_run_restore_preserves_session_achievement_counters()
    {
        // Schema 3 carries session counters so mid-run restore and checkpoint
        // continuation keep unlock eligibility without under-awarding.
        var run = SnakeRun.CreateForTesting(
            new RunConfig(
                Width: 8,
                Height: 6,
                StarvationTicks: 200,
                PowerSpawnIntervalTicks: 0),
            [new GridPoint(1, 1)],
            Direction.Right,
            food: new GridPoint(2, 1),
            hungerTicksRemaining: 100);

        run.Step();
        Assert.True(run.SessionFoodEaten >= 1);

        var restored = SnakeRun.RestoreCanonicalState(run.SerializeCanonicalState());
        Assert.Equal(run.SessionFoodEaten, restored.SessionFoodEaten);
        Assert.Equal(run.SessionWraps, restored.SessionWraps);
        Assert.Equal(run.SessionNearMisses, restored.SessionNearMisses);
        Assert.Equal(run.SessionPowerupsCollected, restored.SessionPowerupsCollected);
        Assert.Equal(run.SessionMaxCombo, restored.SessionMaxCombo);
        Assert.Equal(run.SerializeCanonicalState(), restored.SerializeCanonicalState());
    }

    [Fact]
    public void Terminal_win_emits_achievement_candidates_once()
    {
        var run = SnakeRun.CreateForTesting(
            new RunConfig(
                Width: 2,
                Height: 2,
                PowerSpawnIntervalTicks: 0,
                EnableAchievementCandidates: true),
            [
                new GridPoint(0, 0),
                new GridPoint(0, 1),
                new GridPoint(1, 1),
            ],
            Direction.Up,
            food: new GridPoint(1, 0),
            hungerTicksRemaining: 100,
            score: 50,
            comboCount: 0);

        var first = run.Step();
        Assert.Equal(RunStatus.Won, run.Status);
        Assert.Contains(
            first.OrderedEvents,
            detail => detail.Kind == RunEventKind.AchievementCandidate);
        var second = run.Step();
        Assert.DoesNotContain(
            second.OrderedEvents,
            detail => detail.Kind == RunEventKind.AchievementCandidate);
    }
}
