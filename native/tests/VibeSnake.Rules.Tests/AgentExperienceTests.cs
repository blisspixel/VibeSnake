using VibeSnake.AgentPlay;
using VibeSnake.Rules;

namespace VibeSnake.Rules.Tests;

public sealed class AgentExperienceTests
{
    [Fact]
    public void Burst_stop_policy_is_closed_and_ignores_routine_events()
    {
        Assert.Equal("decision-event-stop-v1", AgentBurstPolicy.Contract);
        Assert.Equal(15, AgentBurstPolicy.Stops.Count);
        Assert.DoesNotContain(RunEventKind.DirectionChanged, AgentBurstPolicy.Stops);
        Assert.DoesNotContain(RunEventKind.Moved, AgentBurstPolicy.Stops);
        Assert.DoesNotContain(RunEventKind.ScoreChanged, AgentBurstPolicy.Stops);
        Assert.DoesNotContain(RunEventKind.HungerReset, AgentBurstPolicy.Stops);
        Assert.True(AgentBurstPolicy.TryGetStopEvent(
            [
                new RunEventDetail(RunEventKind.Moved),
                new RunEventDetail(RunEventKind.AteFood),
                new RunEventDetail(RunEventKind.NearMiss),
            ],
            out var stopEvent));
        Assert.Equal(RunEventKind.AteFood, stopEvent);
        Assert.False(AgentBurstPolicy.TryGetStopEvent(
            [new RunEventDetail(RunEventKind.Moved)],
            out _));
        Assert.Throws<ArgumentNullException>(() =>
            AgentBurstPolicy.TryGetStopEvent(null!, out _));
    }

    [Fact]
    public void Style_catalog_is_closed_unique_and_mode_aware()
    {
        Assert.Equal(5, AgentStyleContractCatalog.All.Count);
        Assert.Equal(
            AgentStyleContractCatalog.All.Count,
            AgentStyleContractCatalog.All.Select(value => value.Id).Distinct().Count());
        Assert.All(AgentStyleContractCatalog.All, definition =>
        {
            Assert.Same(definition, AgentStyleContractCatalog.Get(definition.Id));
            Assert.True(definition.Target > 0);
            Assert.NotEmpty(definition.SupportedModeIds);
        });

        var metrics = Metrics(survival: 220, food: 7, combo: 5, wraps: 3, nearMisses: 4, powers: 2);
        foreach (var definition in AgentStyleContractCatalog.All)
        {
            var mode = definition.SupportedModeIds[0];
            var progress = AgentStyleContractCatalog.Evaluate(definition.Id, mode, metrics);
            Assert.Equal(definition.Id, progress.ContractId);
            Assert.Equal(definition.Metric, progress.Metric);
            Assert.Equal(definition.Target, progress.Target);
            Assert.Equal(metrics.ValueFor(definition.Metric), progress.Current);
            Assert.Equal(progress.Current >= progress.Target, progress.Completed);
        }

        Assert.Throws<ArgumentException>(() => AgentStyleContractCatalog.Get(" "));
        Assert.Throws<ArgumentException>(() => AgentStyleContractCatalog.Get("unknown"));
        Assert.Throws<ArgumentException>(() => AgentStyleContractCatalog.Evaluate(
            AgentStyleContractCatalog.CrownchaserId,
            RunModeCatalog.ClassicId,
            metrics));
        Assert.Throws<ArgumentNullException>(() => AgentStyleContractCatalog.Evaluate(
            AgentStyleContractCatalog.StillwaterId,
            RunModeCatalog.ClassicId,
            null!));
    }

    [Fact]
    public void Signal_school_defines_deterministic_evaluable_lessons()
    {
        Assert.Equal(6, AgentSignalSchoolCatalog.All.Count);
        Assert.Equal(
            AgentSignalSchoolCatalog.All.Count,
            AgentSignalSchoolCatalog.All.Select(value => value.Id).Distinct().Count());
        Assert.All(AgentSignalSchoolCatalog.All, lesson =>
        {
            Assert.Same(lesson, AgentSignalSchoolCatalog.Get(lesson.Id));
            Assert.Contains(lesson.ModeId, new[] { RunModeCatalog.ClassicId, RunModeCatalog.VibeId });
            Assert.InRange(lesson.MaximumSteps, 1, AgentMatchOptions.MaximumAllowedSteps);
            Assert.True(lesson.Target > 0);
        });

        var first = AgentSignalSchoolCatalog.Get("first-turn");
        Assert.False(AgentSignalSchoolCatalog.IsCompleted(first.Id, Metrics()));
        Assert.True(AgentSignalSchoolCatalog.IsCompleted(
            first.Id,
            Metrics(directionChanges: first.Target)));
        Assert.Throws<ArgumentException>(() => AgentSignalSchoolCatalog.Get("missing"));
        Assert.Throws<ArgumentException>(() => AgentSignalSchoolCatalog.Get(""));
        Assert.Throws<ArgumentNullException>(() =>
            AgentSignalSchoolCatalog.IsCompleted(first.Id, null!));
    }

    [Fact]
    public void Metrics_tracker_projects_each_public_event_family()
    {
        var run = SnakeRun.Create(
            1UL,
            RunModeCatalog.CreateConfig(RunModeCatalog.Vibe));
        var snapshot = run.GetSnapshot() with { ComboCount = 5 };
        RunEventDetail[] events =
        [
            new(RunEventKind.AteFood),
            new(RunEventKind.Wrapped),
            new(RunEventKind.NearMiss),
            new(RunEventKind.PowerCollected),
            new(RunEventKind.PowerActivated),
            new(RunEventKind.CollisionPrevented),
            new(RunEventKind.StarvationWarning),
            new(RunEventKind.DirectionChanged),
            new(RunEventKind.Moved),
        ];
        var result = new RunStepResult(
            1,
            RunEvent.Moved,
            events,
            RunStatus.Running,
            DeathCause.None,
            snapshot.StateHash);
        var tracker = new AgentEpisodeMetricsTracker();

        tracker.Record(result, snapshot);
        tracker.Record(result, snapshot with { ComboCount = 3 });
        var metrics = tracker.Snapshot(2);

        Assert.Equal(AgentEpisodeMetricsV1.Contract, metrics.Schema);
        Assert.Equal(2, metrics.SurvivalSteps);
        Assert.Equal(2, metrics.FoodEaten);
        Assert.Equal(5, metrics.PeakCombo);
        Assert.Equal(2, metrics.Wraps);
        Assert.Equal(2, metrics.NearMisses);
        Assert.Equal(2, metrics.PowersCollected);
        Assert.Equal(2, metrics.PowersActivated);
        Assert.Equal(2, metrics.Recoveries);
        Assert.Equal(2, metrics.StarvationWarnings);
        Assert.Equal(2, metrics.DirectionChanges);
        Assert.Throws<ArgumentNullException>(() => tracker.Record(result, null!));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            metrics.ValueFor((AgentExperienceMetric)255));
    }

    [Fact]
    public void Styled_session_returns_live_progress_and_verified_terminal_metrics()
    {
        var session = new AgentMatchSession(new AgentMatchOptions(
            "styled",
            RunModeCatalog.VibeId,
            RunModeCatalog.CurrentModeVersion,
            123UL,
            AgentSeedVisibility.Open,
            maximumSteps: 1,
            styleContractId: AgentStyleContractCatalog.StillwaterId));
        var initial = session.Observe();
        var response = session.SubmitAction(new AgentActionRequest(
            "move",
            initial.Tick,
            initial.StateHash,
            AgentAction.Continue));

        Assert.Equal(0, initial.EpisodeMetrics.SurvivalSteps);
        Assert.Equal(AgentStyleContractCatalog.StillwaterId, initial.StyleContract!.ContractId);
        Assert.Equal(1, response.Observation.EpisodeMetrics.SurvivalSteps);
        Assert.Equal(1, response.Observation.StyleContract!.Current);
        var result = Assert.IsType<AgentMatchResult>(response.MatchResult);
        Assert.Equal(response.Observation.EpisodeMetrics, result.EpisodeMetrics);
        Assert.Equal(response.Observation.StyleContract, result.StyleContract);
        Assert.False(result.StyleContract!.Completed);
    }

    private static AgentEpisodeMetricsV1 Metrics(
        int survival = 0,
        int food = 0,
        int combo = 0,
        int wraps = 0,
        int nearMisses = 0,
        int powers = 0,
        int directionChanges = 0) =>
        new(
            AgentEpisodeMetricsV1.Contract,
            survival,
            food,
            combo,
            wraps,
            nearMisses,
            powers,
            powers,
            powers,
            0,
            directionChanges);
}
