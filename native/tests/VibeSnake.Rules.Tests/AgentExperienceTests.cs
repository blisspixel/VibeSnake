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
    public void Signal_school_defines_eight_ordered_two_requirement_lessons()
    {
        string[] expectedIds =
        [
            "first-turn",
            "wrap-line",
            "hunger-route",
            "exit-route",
            "power-route",
            "recover-route",
            "combo-route",
            "death-read",
        ];

        Assert.Equal(expectedIds, AgentSignalSchoolCatalog.All.Select(value => value.Id));
        Assert.Equal(
            AgentSignalSchoolCatalog.All.Count,
            AgentSignalSchoolCatalog.All.Select(value => value.Id).Distinct().Count());
        Assert.All(AgentSignalSchoolCatalog.All, lesson =>
        {
            Assert.Same(lesson, AgentSignalSchoolCatalog.Get(lesson.Id));
            Assert.Contains(lesson.ModeId, new[] { RunModeCatalog.ClassicId, RunModeCatalog.VibeId });
            Assert.InRange(lesson.MaximumSteps, 1, AgentMatchOptions.MaximumAllowedSteps);
            Assert.Equal(2, lesson.Requirements.Count);
            Assert.Equal(
                lesson.Requirements.Count,
                lesson.Requirements.Select(value => value.Id).Distinct().Count());
            Assert.All(lesson.Requirements, requirement => Assert.True(requirement.Target > 0));
            Assert.Equal(
                AgentSignalSchoolCatalog.EvaluationPolicyId,
                lesson.EvaluationPolicyId);
        });
        Assert.Equal(
            ["First Signal", "Open Circuit", "Feed the Signal", "Keep Two Doors",
                "Tune the Current", "Return from Static", "Hold the Chorus", "Read the End"],
            AgentSignalSchoolCatalog.All.Select(value => value.Title));
        Assert.Equal(
            [
                RunModeCatalog.ClassicId,
                RunModeCatalog.ClassicId,
                RunModeCatalog.VibeId,
                RunModeCatalog.VibeId,
                RunModeCatalog.VibeId,
                RunModeCatalog.VibeId,
                RunModeCatalog.VibeId,
                RunModeCatalog.VibeId,
            ],
            AgentSignalSchoolCatalog.All.Select(value => value.ModeId));
        Assert.Equal(
            [7UL, 65_535UL, 4_294_967_291UL, 20_260_814UL, 32_452_843UL, 0UL,
                49_979_687UL, 20_260_815UL],
            AgentSignalSchoolCatalog.All.Select(value => value.PracticeSeed));
        Assert.Equal(
            [16, 160, 180, 240, 320, 600, 480, 600],
            AgentSignalSchoolCatalog.All.Select(value => value.MaximumSteps));
        Assert.Equal(
            [
                "opposite_reversal_rejected/legal_turn_after_rejection",
                "wrapped_event/running_after_wrap",
                "food_eaten/food_before_starvation",
                "food_growth/two_structural_exits_after_growth",
                "power_collected/same_power_activated",
                "collision_prevented/running_after_recovery",
                "three_food/peak_combo_three",
                "terminal_death/matching_death_event",
            ],
            AgentSignalSchoolCatalog.All.Select(value =>
                string.Join('/', value.Requirements.Select(requirement => requirement.Id))));
        Assert.Equal(
            [
                "AttemptWitness:1/ReplayTrace:1",
                "ReplayTrace:1/ReplayTrace:1",
                "ReplayTrace:1/ReplayTrace:1",
                "ReplayTrace:1/ReplayTrace:1",
                "ReplayTrace:1/ReplayTrace:1",
                "ReplayTrace:1/ReplayTrace:1",
                "ReplayTrace:3/ReplayTrace:3",
                "ReplayTrace:1/ReplayTrace:1",
            ],
            AgentSignalSchoolCatalog.All.Select(value => string.Join(
                '/',
                value.Requirements.Select(requirement =>
                    $"{requirement.EvidenceSource}:{requirement.Target}"))));

        Assert.Throws<ArgumentException>(() => AgentSignalSchoolCatalog.Get("missing"));
        Assert.Throws<ArgumentException>(() => AgentSignalSchoolCatalog.Get(""));
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
        var result = Assert.IsType<AgentMatchResultV5>(response.MatchResult);
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
    public void Lesson_session_returns_attempt_aware_delta_and_replay_bound_outcome()
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
        var rejection = session.SubmitAction(new AgentActionRequest(
            "lesson-reversal",
            initial.Tick,
            initial.StateHash,
            AgentAction.Left));
        var response = session.SubmitAction(new AgentActionRequest(
            "lesson-turn",
            rejection.Observation.Tick,
            rejection.Observation.StateHash,
            AgentAction.Up));
        var result = session.Finish();

        Assert.Equal(lesson.Id, initial.LessonProgress!.LessonId);
        Assert.Equal("opposite_reversal_rejected", initial.LessonProgress.FirstUnmetRequirementId);
        Assert.False(rejection.Accepted);
        Assert.False(rejection.RulesAdvanced);
        Assert.Equal(AgentActionRejection.IllegalDirection, rejection.Rejection);
        Assert.Equal(["opposite_reversal_rejected"], rejection.LessonDelta!.NewlySatisfiedRequirementIds);
        Assert.Equal(["legal_turn_after_rejection"], response.LessonDelta!.NewlySatisfiedRequirementIds);
        Assert.True(response.LessonDelta.AllRequirementsReachedThisMutation);
        Assert.True(response.Observation.LessonProgress!.AllRequirementsSatisfied);
        Assert.Equal(AgentLessonOutcomeV2.Contract, result.LessonOutcome!.Schema);
        Assert.True(result.LessonOutcome.AllRequirementsSatisfied);
        Assert.Equal(AgentLessonReviewCode.TargetReached, result.LessonOutcome.ReviewCode);
        Assert.Equal(result.ReplayPayloadHash, result.LessonOutcome.ReplayPayloadHash);
        Assert.Equal(
            AgentLessonEvidenceReplayEvaluator.ComputeEvidenceHash(
                result.ReplayPayloadHash,
                result.LessonOutcome.AttemptEvidenceHash),
            result.LessonOutcome.EvidenceHash);
        Assert.Equal(
            result.EpisodeMetrics,
            AgentEpisodeMetricsReplayEvaluator.Evaluate(result.VerifiedReplay));
    }

    [Fact]
    public void Every_signal_school_lesson_has_a_locked_verified_route()
    {
        var evidence = new List<string>();
        foreach (var lesson in AgentSignalSchoolCatalog.All)
        {
            var route = AgentLessonRouteDriver.DriveSession(lesson);
            var result = route.Result;
            var outcome = Assert.IsType<AgentLessonOutcomeV2>(result.LessonOutcome);
            Assert.True(
                outcome.AllRequirementsSatisfied,
                $"{lesson.Id}: first unmet {outcome.FirstUnmetRequirementId}; metrics={result.EpisodeMetrics}");
            Assert.All(route.Calls.Skip(lesson.Id == AgentSignalSchoolCatalog.FirstTurnId ? 1 : 0),
                call => Assert.True(call.Accepted, $"{lesson.Id}: {call.Rejection}"));
            if (lesson.Id == AgentSignalSchoolCatalog.FirstTurnId)
            {
                Assert.Equal(AgentActionRejection.IllegalDirection, route.Calls[0].Rejection);
            }
            Assert.Equal(AgentLessonReviewCode.TargetReached, outcome.ReviewCode);
            Assert.Equal(result.ReplayPayloadHash, outcome.ReplayPayloadHash);
            Assert.Equal(
                result.EpisodeMetrics,
                AgentEpisodeMetricsReplayEvaluator.Evaluate(result.VerifiedReplay));
            evidence.Add(
                $"{lesson.Id}={result.ReplayPayloadHash}/{outcome.AttemptEvidenceHash}/{outcome.EvidenceHash}");
        }

        Assert.Equal(
            [
                "first-turn=9cf44d81c732b02d1dfbf63b12a83fa7e7e6fffdd5b2ff58fd16c53ae0a226bc/7ae32dae873da1104b366c9d6b7d921ffbbfeb58e5ef8f66b2367ef5a831b35a/15d900a8e54100d2dba7b2d0dd26a242edda6a57198f684d9b94a9191b89c1a6",
                "wrap-line=1444c8c7fe3776e83e368f3448496f810e618d58756c2fae6e2720e0efeddc86/e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855/17cf001ef78e3039c27b9b56f9ceb0c68b43e21760c9092d9f64d74a192ec21e",
                "hunger-route=d9478460a57181c802fafefc619925b62b25adf0fe22b11315e4007d44bcd02a/e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855/176adae0c93f05454357d63f7d1b605c4f3593b5f2122b0b7245ec4eecda8581",
                "exit-route=0aecacfed7c285529584392450e9d7e458234897441eb8532d10920f0a6f49fd/e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855/c1cfbf072d7d828b58955e246f275430d9523b159fe80746a0bbbd23959844c9",
                "power-route=8740bd9b61f1666701457645caea85f9d68206bbfe214df3e31de6ace721bfab/e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855/d7434d533b99595390e8bc200c812d953f38a59f975ec5b74e1b7ef3b97a9198",
                "recover-route=2996836bd7db0538aa626f050db64afda925069bab0aa66bea8e85c0c26bf66e/e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855/cb71c4d2fc89c86fd35046bcde78a0e8b444503f8d813160159b1d8c236dc059",
                "combo-route=6ccb498f6b5a824c55bc5f92ad577ea42747c4f2b2b69428ec03d94992daa7b3/e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855/b42808afb8a7ddff17761c73d3d5146d21bbf3cd02416f0a493ff0e3af17be75",
                "death-read=65368b64b0e8dd941c578445a1053408c3bc2d34a02ba1d12b5ece0d1e08d061/e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855/e2415ba272f6bac7398d91ae982570c03c3b49568679009c8d6c6d9e5d454e4c",
            ],
            evidence);
    }

    [Fact]
    public void Lesson_replay_divergence_fails_closed_without_an_outcome()
    {
        var lesson = AgentSignalSchoolCatalog.Get(AgentSignalSchoolCatalog.FirstTurnId);
        var session = new AgentMatchSession(
            new AgentMatchOptions(
                "lesson-divergence",
                lesson.ModeId,
                RunModeCatalog.CurrentModeVersion,
                lesson.PracticeSeed,
                AgentSeedVisibility.Open,
                lesson.MaximumSteps,
                lessonId: lesson.Id),
            viewerSink: null,
            new DivergentReplayFinalizer());
        var initial = session.Observe();
        var rejected = session.SubmitAction(new AgentActionRequest(
            "lesson-divergence-reversal",
            initial.Tick,
            initial.StateHash,
            AgentAction.Left));
        var advanced = session.SubmitAction(new AgentActionRequest(
            "lesson-divergence-turn",
            rejected.Observation.Tick,
            rejected.Observation.StateHash,
            AgentAction.Up));

        Assert.True(advanced.Accepted);
        Assert.True(advanced.Observation.LessonProgress!.AllRequirementsSatisfied);
        Assert.Throws<InvalidOperationException>(() => session.Finish());
        var failed = session.Observe();
        Assert.Equal(AgentMatchLifecycle.FailedClosed, failed.Lifecycle);
        Assert.Equal(AgentLessonEvidenceState.FailedClosed, failed.LessonProgress!.EvidenceState);
        Assert.True(failed.LessonProgress.AllRequirementsSatisfied);
        Assert.NotNull(failed.LessonProgress.RetryDescriptor);
        Assert.Null(session.GetResult());
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
