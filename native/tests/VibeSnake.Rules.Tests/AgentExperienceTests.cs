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
    public void Style_catalog_is_closed_unique_exactly_two_criteria_and_mode_aware()
    {
        var expected = new Dictionary<string, (string FirstId, int FirstTarget, AgentStyleCriterionUnit FirstUnit, string SecondId, int SecondTarget, AgentStyleCriterionUnit SecondUnit)>(StringComparer.Ordinal)
        {
            [AgentStyleContractCatalog.StillwaterId] = (
                "survival_steps",
                200,
                AgentStyleCriterionUnit.Count,
                "structural_open_exit_rate_bp",
                9_900,
                AgentStyleCriterionUnit.BasisPoints),
            [AgentStyleContractCatalog.CrownchaserId] = (
                "peak_combo",
                4,
                AgentStyleCriterionUnit.Count,
                "clean_pre_peak_continuity_bp",
                10_000,
                AgentStyleCriterionUnit.BasisPoints),
            [AgentStyleContractCatalog.EdgeProphetId] = (
                "rewarded_body_proximity_near_misses",
                3,
                AgentStyleCriterionUnit.Count,
                "wrapped_rewarded_body_proximity_near_misses",
                1,
                AgentStyleCriterionUnit.Count),
            [AgentStyleContractCatalog.MutagenistId] = (
                "distinct_power_kinds_activated",
                2,
                AgentStyleCriterionUnit.Count,
                "maximum_concurrent_active_power_kinds",
                2,
                AgentStyleCriterionUnit.Count),
            [AgentStyleContractCatalog.RedlineId] = (
                "food_eaten",
                6,
                AgentStyleCriterionUnit.Count,
                "safe_food_progress_rate_bp",
                6_500,
                AgentStyleCriterionUnit.BasisPoints),
        };

        Assert.Equal(5, AgentStyleContractCatalog.All.Count);
        Assert.Equal(expected.Keys, AgentStyleContractCatalog.All.Select(value => value.Id));
        Assert.Equal(
            AgentStyleContractCatalog.All.Count,
            AgentStyleContractCatalog.All.Select(value => value.Id).Distinct().Count());
        Assert.All(AgentStyleContractCatalog.All, definition =>
        {
            Assert.Same(definition, AgentStyleContractCatalog.Get(definition.Id));
            Assert.Equal(AgentStyleContractCatalog.EvaluationPolicyId, definition.EvaluationPolicyId);
            Assert.Equal(2, definition.Criteria.Count);
            Assert.Equal(2, definition.Criteria.Select(value => value.Id).Distinct().Count());
            Assert.NotEmpty(definition.SupportedModeIds);
            Assert.All(definition.Criteria, criterion =>
            {
                Assert.Equal(AgentStyleCriterionComparator.AtLeast, criterion.Comparator);
                Assert.True(criterion.Target > 0);
                Assert.False(string.IsNullOrWhiteSpace(criterion.Description));
            });

            var values = expected[definition.Id];
            Assert.Collection(
                definition.Criteria,
                first =>
                {
                    Assert.Equal(values.FirstId, first.Id);
                    Assert.Equal(values.FirstTarget, first.Target);
                    Assert.Equal(values.FirstUnit, first.Unit);
                },
                second =>
                {
                    Assert.Equal(values.SecondId, second.Id);
                    Assert.Equal(values.SecondTarget, second.Target);
                    Assert.Equal(values.SecondUnit, second.Unit);
                });

            AgentStyleContractCatalog.ValidateMode(definition.Id, definition.SupportedModeIds[0]);
        });

        Assert.Throws<ArgumentException>(() => AgentStyleContractCatalog.Get(" "));
        Assert.Throws<ArgumentException>(() => AgentStyleContractCatalog.Get("unknown"));
        Assert.Throws<ArgumentException>(() => AgentStyleContractCatalog.ValidateMode(
            AgentStyleContractCatalog.CrownchaserId,
            RunModeCatalog.ClassicId));
        Assert.Equal([AgentStyleCriterionComparator.AtLeast], Enum.GetValues<AgentStyleCriterionComparator>());
        Assert.Equal(
            [AgentStyleCriterionUnit.Count, AgentStyleCriterionUnit.BasisPoints],
            Enum.GetValues<AgentStyleCriterionUnit>());
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(198, 200, 9_900)]
    [InlineData(197, 200, 9_850)]
    [InlineData(13, 20, 6_500)]
    [InlineData(12, 20, 6_000)]
    [InlineData(1, 3, 3_333)]
    public void Style_evidence_basis_points_uses_full_denominator_and_integer_floor(
        long numerator,
        long denominator,
        int expected)
    {
        Assert.Equal(expected, AgentStyleEvidenceMath.BasisPoints(numerator, denominator));
    }

    [Fact]
    public void Shared_structural_evidence_counts_non_reversal_wrap_and_departing_tail_exits()
    {
        var config = new RunConfig(Width: 5, Height: 5, PowerSpawnIntervalTicks: 0);
        var initial = SnakeRun.Create(1UL, config).GetSnapshot();
        var openBoard = initial with
        {
            Direction = Direction.Left,
            Body = [new GridPoint(1, 2), new GridPoint(0, 2)],
            DetachedObstacles = [],
            DetachedObstacleTicksRemaining = 0,
        };

        Assert.Equal(3, AgentStyleEvidenceMath.StructuralOpenExitCount(config, openBoard));

        var tailAndWrapOnly = openBoard with
        {
            Direction = Direction.Down,
            Body =
            [
                new GridPoint(2, 2),
                new GridPoint(1, 3),
                new GridPoint(2, 3),
                new GridPoint(2, 1),
                new GridPoint(1, 1),
                new GridPoint(1, 2),
            ],
            DetachedObstacles = [new GridPoint(0, 2)],
            DetachedObstacleTicksRemaining = 2,
        };
        Assert.Equal(
            1,
            AgentStyleEvidenceMath.StructuralOpenExitCount(config, tailAndWrapOnly));
        Assert.Equal(
            1,
            AgentStyleEvidenceMath.WrappedManhattanDistance(
                new GridPoint(0, 2),
                new GridPoint(4, 2),
                config.Width,
                config.Height));
        Assert.Equal(
            4,
            AgentStyleEvidenceMath.WrappedManhattanDistance(
                new GridPoint(0, 0),
                new GridPoint(2, 2),
                config.Width,
                config.Height));

        Assert.Equal(
            0,
            AgentStyleEvidenceMath.StructuralOpenExitCount(
                config,
                openBoard with { Status = RunStatus.Dead, DeathCause = DeathCause.SelfCollision }));
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
        Assert.Equal(
            1,
            response.Observation.StyleContract!.Criteria
                .Single(value => value.CriterionId == "survival_steps")
                .Current);
        var result = Assert.IsType<AgentMatchResultV4>(response.MatchResult);
        Assert.Equal(response.Observation.EpisodeMetrics, result.EpisodeMetrics);
        var outcome = Assert.IsType<AgentStyleOutcomeV2>(result.StyleOutcome);
        Assert.Equal(AgentStyleOutcomeV2.Contract, outcome.Schema);
        Assert.Equal(response.Observation.StyleContract.ContractId, outcome.ContractId);
        Assert.Equal(response.Observation.StyleContract.Criteria, outcome.Criteria);
        Assert.False(outcome.AllCriteriaSatisfied);
        Assert.Equal(result.ReplayPayloadHash, outcome.ReplayPayloadHash);
        Assert.Equal(
            response.Observation.StyleContract,
            AgentStyleEvidenceReplayEvaluator.EvaluateProgress(
                AgentStyleContractCatalog.StillwaterId,
                RunModeCatalog.VibeId,
                result.VerifiedReplay));
        Assert.Equal(
            outcome,
            AgentStyleEvidenceReplayEvaluator.EvaluateOutcome(
                AgentStyleContractCatalog.StillwaterId,
                RunModeCatalog.VibeId,
                result.VerifiedReplay));
    }

    [Theory]
    [InlineData(AgentStyleContractCatalog.StillwaterId, RunModeCatalog.ClassicId)]
    [InlineData(AgentStyleContractCatalog.CrownchaserId, RunModeCatalog.VibeId)]
    [InlineData(AgentStyleContractCatalog.EdgeProphetId, RunModeCatalog.VibeId)]
    [InlineData(AgentStyleContractCatalog.MutagenistId, RunModeCatalog.VibeId)]
    [InlineData(AgentStyleContractCatalog.RedlineId, RunModeCatalog.ClassicId)]
    public void Style_selection_does_not_change_rules_state_or_verified_replay(
        string styleId,
        string modeId)
    {
        AgentMatchSession Create(string matchId, string? selectedStyle) => new(
            new AgentMatchOptions(
                matchId,
                modeId,
                RunModeCatalog.CurrentModeVersion,
                987UL,
                AgentSeedVisibility.Open,
                maximumSteps: 3,
                styleContractId: selectedStyle));

        var unstyled = Create("rules-identity-unstyled", null);
        var styled = Create("rules-identity-styled", styleId);
        Assert.Equal(unstyled.Observe().StateHash, styled.Observe().StateHash);
        Assert.Null(unstyled.Observe().StyleContract);
        Assert.Equal(styleId, styled.Observe().StyleContract!.ContractId);

        AgentAction[] actions = [AgentAction.Up, AgentAction.Right, AgentAction.Down];
        for (var index = 0; index < actions.Length; index++)
        {
            var left = unstyled.Observe();
            var right = styled.Observe();
            var leftResponse = unstyled.SubmitAction(new AgentActionRequest(
                $"unstyled-{index}",
                left.Tick,
                left.StateHash,
                actions[index]));
            var rightResponse = styled.SubmitAction(new AgentActionRequest(
                $"styled-{index}",
                right.Tick,
                right.StateHash,
                actions[index]));

            Assert.Equal(leftResponse.Accepted, rightResponse.Accepted);
            Assert.Equal(leftResponse.RulesAdvanced, rightResponse.RulesAdvanced);
            Assert.Equal(leftResponse.Observation.StateHash, rightResponse.Observation.StateHash);
            Assert.Equal(
                leftResponse.Observation.EpisodeMetrics,
                rightResponse.Observation.EpisodeMetrics);
        }

        var unstyledResult = unstyled.GetResult()!;
        var styledResult = styled.GetResult()!;
        Assert.Null(unstyledResult.StyleOutcome);
        Assert.NotNull(styledResult.StyleOutcome);
        Assert.Equal(unstyledResult.ReplayPayloadHash, styledResult.ReplayPayloadHash);
        Assert.Equal(
            unstyledResult.VerifiedReplay.Serialize(),
            styledResult.VerifiedReplay.Serialize());
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
            AgentMatchResultV4? result = null;
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

    private static AgentAction ChooseLessonAction(string lessonId, AgentObservationV4 observation)
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
        AgentObservationV4 observation,
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
        AgentObservationV4 observation,
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
