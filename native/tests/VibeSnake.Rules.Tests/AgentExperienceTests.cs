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
            Assert.Equal(
                AgentSignalSchoolCatalog.PrimaryMetricEvaluationPolicy,
                lesson.EvaluationPolicyId);
        });

        var first = AgentSignalSchoolCatalog.Get("first-turn");
        Assert.False(AgentSignalSchoolCatalog.IsCompleted(first.Id, Metrics()));
        Assert.True(AgentSignalSchoolCatalog.IsCompleted(
            first.Id,
            Metrics(directionChanges: first.Target)));
        var before = AgentSignalSchoolCatalog.Evaluate(first.Id, Metrics());
        var after = AgentSignalSchoolCatalog.Evaluate(
            first.Id,
            Metrics(directionChanges: first.Target));
        var delta = AgentSignalSchoolCatalog.Delta(before, after);
        Assert.Equal(AgentLessonProgressV1.Contract, before.Schema);
        Assert.Equal(first.Id, before.LessonId);
        Assert.Equal(first.Target, before.Remaining);
        Assert.Equal(AgentLessonProgressDeltaV1.Contract, delta.Schema);
        Assert.Equal(first.Target, delta.Delta);
        Assert.True(delta.TargetReachedThisMutation);
        Assert.Throws<ArgumentException>(() => AgentSignalSchoolCatalog.Get("missing"));
        Assert.Throws<ArgumentException>(() => AgentSignalSchoolCatalog.Get(""));
        Assert.Throws<ArgumentNullException>(() =>
            AgentSignalSchoolCatalog.IsCompleted(first.Id, null!));
        Assert.Throws<ArgumentException>(() => AgentSignalSchoolCatalog.Delta(
            before,
            after with { LessonId = "different" }));
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

    [Fact]
    public void Lesson_session_returns_live_delta_and_replay_bound_outcome()
    {
        var lesson = AgentSignalSchoolCatalog.Get("first-turn");
        var session = new AgentMatchSession(new AgentMatchOptions(
            "lesson",
            lesson.ModeId,
            RunModeCatalog.CurrentModeVersion,
            lesson.PracticeSeed,
            AgentSeedVisibility.Open,
            lesson.MaximumSteps,
            lessonId: lesson.Id));
        var initial = session.Observe();
        var response = session.SubmitAction(new AgentActionRequest(
            "lesson-turn",
            initial.Tick,
            initial.StateHash,
            AgentAction.Up));
        var result = session.Finish();

        Assert.Equal(lesson.Id, initial.LessonProgress!.LessonId);
        Assert.Equal(0, initial.LessonProgress.Current);
        Assert.Equal(1, response.LessonDelta!.Delta);
        Assert.True(response.LessonDelta.TargetReachedThisMutation);
        Assert.True(response.Observation.LessonProgress!.TargetReached);
        Assert.Equal(AgentLessonOutcomeV1.Contract, result.LessonOutcome!.Schema);
        Assert.True(result.LessonOutcome.TargetReached);
        Assert.Equal(result.ReplayPayloadHash, result.LessonOutcome.ReplayPayloadHash);
        Assert.Equal(
            result.EpisodeMetrics,
            AgentEpisodeMetricsReplayEvaluator.Evaluate(result.VerifiedReplay));
    }

    [Fact]
    public void Every_signal_school_practice_has_a_deterministic_verified_route()
    {
        var evidence = new List<string>();
        foreach (var lesson in AgentSignalSchoolCatalog.All)
        {
            var incomplete = new AgentMatchSession(new AgentMatchOptions(
                $"incomplete-{lesson.Id}",
                lesson.ModeId,
                RunModeCatalog.CurrentModeVersion,
                lesson.PracticeSeed,
                AgentSeedVisibility.Open,
                lesson.MaximumSteps,
                lessonId: lesson.Id)).Finish();
            Assert.False(incomplete.LessonOutcome!.TargetReached);
            Assert.Equal(lesson.Target, incomplete.LessonOutcome.Shortfall);

            var session = new AgentMatchSession(new AgentMatchOptions(
                $"route-{lesson.Id}",
                lesson.ModeId,
                RunModeCatalog.CurrentModeVersion,
                lesson.PracticeSeed,
                AgentSeedVisibility.Open,
                lesson.MaximumSteps,
                lessonId: lesson.Id));
            AgentMatchResult? result = null;
            for (var step = 0; step < lesson.MaximumSteps && result is null; step++)
            {
                var observation = session.Observe();
                if (observation.LessonProgress!.TargetReached)
                {
                    break;
                }

                var response = session.SubmitAction(new AgentActionRequest(
                    $"route-{lesson.Id}-{step}",
                    observation.Tick,
                    observation.StateHash,
                    ChooseLessonAction(lesson.Id, observation)));
                Assert.True(response.Accepted, $"{lesson.Id}: {response.Rejection}");
                result = response.MatchResult;
            }

            result ??= session.Finish();
            var outcome = Assert.IsType<AgentLessonOutcomeV1>(result.LessonOutcome);
            Assert.True(
                outcome.TargetReached,
                $"{lesson.Id}: shortfall {outcome.Shortfall}; metrics={result.EpisodeMetrics}");
            Assert.Equal(result.ReplayPayloadHash, outcome.ReplayPayloadHash);
            Assert.Equal(
                result.EpisodeMetrics,
                AgentEpisodeMetricsReplayEvaluator.Evaluate(result.VerifiedReplay));
            evidence.Add($"{lesson.Id}={result.ReplayPayloadHash}");
        }

        Assert.Equal(
            [
                "first-turn=9cf44d81c732b02d1dfbf63b12a83fa7e7e6fffdd5b2ff58fd16c53ae0a226bc",
                "wrap-line=1444c8c7fe3776e83e368f3448496f810e618d58756c2fae6e2720e0efeddc86",
                "hunger-route=d9478460a57181c802fafefc619925b62b25adf0fe22b11315e4007d44bcd02a",
                "power-route=8740bd9b61f1666701457645caea85f9d68206bbfe214df3e31de6ace721bfab",
                "combo-route=6ccb498f6b5a824c55bc5f92ad577ea42747c4f2b2b69428ec03d94992daa7b3",
                "recover-route=2996836bd7db0538aa626f050db64afda925069bab0aa66bea8e85c0c26bf66e",
            ],
            evidence);
        AssertLessonReplayDivergenceFailsClosed();
    }

    private static void AssertLessonReplayDivergenceFailsClosed()
    {
        var lesson = AgentSignalSchoolCatalog.Get("first-turn");
        var session = new AgentMatchSession(
            new AgentMatchOptions(
                "lesson-divergence",
                lesson.ModeId,
                RunModeCatalog.CurrentModeVersion,
                lesson.PracticeSeed,
                AgentSeedVisibility.Open,
                maximumSteps: 1),
            viewerSink: null,
            new DivergentReplayFinalizer());
        var initial = session.Observe();
        var request = new AgentActionRequest(
            "lesson-divergence-step",
            initial.Tick,
            initial.StateHash,
            AgentAction.Up);

        var response = session.SubmitAction(request);
        var retry = session.SubmitAction(request);

        Assert.Same(response, retry);
        Assert.False(response.Accepted);
        Assert.True(response.RulesAdvanced);
        Assert.Equal(AgentActionRejection.ReplayFailure, response.Rejection);
        Assert.Equal(AgentMatchLifecycle.FailedClosed, session.Lifecycle);
        Assert.Null(response.MatchResult);
        Assert.Null(session.GetResult());
    }

    private static AgentAction ChooseLessonAction(string lessonId, AgentObservationV3 observation)
    {
        if (lessonId == "first-turn")
        {
            return ToAction(TurnLeft(observation.Direction), observation.Direction);
        }

        if (lessonId == "wrap-line")
        {
            return AgentAction.Continue;
        }

        if (lessonId == "recover-route"
            && (observation.ShieldTicksRemaining > 0
                || observation.PhaseShiftTicksRemaining > 0
                || observation.LastStandHeld))
        {
            return ToAction(TurnLeft(observation.Direction), observation.Direction);
        }

        var target = lessonId is "power-route" or "recover-route"
            ? observation.PowerPickup?.Position ?? observation.Food
            : observation.Food;
        return target is null
            ? AgentAction.Continue
            : FindPathAction(observation, target.Value);
    }

    private static AgentAction FindPathAction(
        AgentObservationV3 observation,
        AgentPointV1 target)
    {
        var blocked = observation.Body.Skip(1)
            .Concat(observation.DetachedObstacles)
            .ToHashSet();
        var queue = new Queue<(AgentPointV1 Point, Direction First)>();
        var visited = new HashSet<AgentPointV1> { observation.Head };
        foreach (var direction in CandidateDirections(observation.Direction))
        {
            var next = Advance(observation, observation.Head, direction);
            if (next is null || blocked.Contains(next.Value) || !visited.Add(next.Value))
            {
                continue;
            }

            if (next.Value == target)
            {
                return ToAction(direction, observation.Direction);
            }
            queue.Enqueue((next.Value, direction));
        }

        while (queue.TryDequeue(out var current))
        {
            foreach (var direction in Enum.GetValues<Direction>())
            {
                var next = Advance(observation, current.Point, direction);
                if (next is null || blocked.Contains(next.Value) || !visited.Add(next.Value))
                {
                    continue;
                }

                if (next.Value == target)
                {
                    return ToAction(current.First, observation.Direction);
                }
                queue.Enqueue((next.Value, current.First));
            }
        }

        return AgentAction.Continue;
    }

    private static Direction[] CandidateDirections(Direction current) =>
        [current, TurnLeft(current), TurnRight(current)];

    private static AgentPointV1? Advance(
        AgentObservationV3 observation,
        AgentPointV1 point,
        Direction direction)
    {
        var offset = direction.Offset();
        var x = point.X + offset.X;
        var y = point.Y + offset.Y;
        if (observation.WrapsAtEdges)
        {
            x = (x + observation.BoardWidth) % observation.BoardWidth;
            y = (y + observation.BoardHeight) % observation.BoardHeight;
        }
        else if (x < 0 || x >= observation.BoardWidth || y < 0 || y >= observation.BoardHeight)
        {
            return null;
        }

        return new AgentPointV1(x, y);
    }

    private static Direction TurnLeft(Direction direction) => direction switch
    {
        Direction.Up => Direction.Left,
        Direction.Right => Direction.Up,
        Direction.Down => Direction.Right,
        Direction.Left => Direction.Down,
        _ => throw new ArgumentOutOfRangeException(nameof(direction)),
    };

    private static Direction TurnRight(Direction direction) => direction switch
    {
        Direction.Up => Direction.Right,
        Direction.Right => Direction.Down,
        Direction.Down => Direction.Left,
        Direction.Left => Direction.Up,
        _ => throw new ArgumentOutOfRangeException(nameof(direction)),
    };

    private static AgentAction ToAction(Direction direction, Direction current) =>
        direction == current
            ? AgentAction.Continue
            : direction switch
            {
                Direction.Up => AgentAction.Up,
                Direction.Right => AgentAction.Right,
                Direction.Down => AgentAction.Down,
                Direction.Left => AgentAction.Left,
                _ => throw new ArgumentOutOfRangeException(nameof(direction)),
            };

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

    private sealed class DivergentReplayFinalizer : IAgentReplayFinalizer
    {
        public AgentReplayFinalization Finalize(
            AgentReplayLane lane,
            RunReplayRecorder recorder,
            SnakeRun run)
        {
            _ = recorder;
            _ = run;
            var alternative = SnakeRun.Create(
                999UL,
                RunModeCatalog.CreateConfig(RunModeCatalog.Classic));
            var alternativeRecorder = new RunReplayRecorder(alternative);
            return AgentReplayFinalizer.Instance.Finalize(
                lane,
                alternativeRecorder,
                alternative);
        }
    }
}
