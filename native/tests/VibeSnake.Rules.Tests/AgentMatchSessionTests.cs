using VibeSnake.AgentPlay;
using VibeSnake.Rules;

namespace VibeSnake.Rules.Tests;

public sealed class AgentMatchSessionTests
{
    [Fact]
    public void Open_match_exposes_public_state_and_accepts_one_exact_step()
    {
        var session = CreateSession(AgentSeedVisibility.Open);
        var initial = session.Observe();

        var response = session.SubmitAction(Request(
            "move-1",
            initial,
            AgentAction.Up,
            AgentPublicIntent.PreserveSpace));

        Assert.True(response.Accepted);
        Assert.True(response.RulesAdvanced);
        Assert.Equal(AgentActionRejection.None, response.Rejection);
        Assert.Equal(1, response.Observation.Tick);
        Assert.Equal(123UL, response.Observation.GameplaySeed);
        Assert.Equal(Direction.Up, response.Observation.Direction);
        Assert.Contains(
            response.Observation.PreviousEvents,
            value => value.Kind == RunEventKind.DirectionChanged);
        Assert.Equal(AgentAction.Up, response.Observation.PreviousAction!.Action);
        Assert.True(response.Observation.PreviousAction.Accepted);
        Assert.Equal(
            AgentActionRejection.None,
            response.Observation.PreviousAction.Rejection);
        Assert.True(response.Observation.PreviousAction.RulesAdvanced);
        Assert.Equal(
            AgentPublicIntent.PreserveSpace,
            response.Observation.PreviousAction.DeclaredIntent);
        Assert.Null(response.MatchResult);
        Assert.Equal(AgentMatchLifecycle.AwaitingAction, session.Lifecycle);
    }

    [Fact]
    public void Blind_match_hides_seed_until_verified_result()
    {
        var session = CreateSession(AgentSeedVisibility.Blind, maximumSteps: 1);
        var initial = session.Observe();

        var response = session.SubmitAction(Request("move-1", initial, AgentAction.Continue));

        Assert.Null(initial.GameplaySeed);
        Assert.Null(response.Observation.GameplaySeed);
        Assert.NotNull(response.MatchResult);
        Assert.Equal(123UL, response.MatchResult.GameplaySeed);
        Assert.Equal(AgentMatchEndReason.StepLimit, response.MatchResult.EndReason);
        Assert.Equal(ReplayVerificationCode.Verified, response.MatchResult.ReplayVerificationCode);
        Assert.True(response.MatchResult.VerifiedReplay.Verify().IsValid);
        Assert.Equal(response.MatchResult, session.GetResult());
        Assert.Same(response.MatchResult, session.Finish());
        Assert.False(response.Observation.IsActionAwaited);
    }

    [Fact]
    public void Legal_directions_and_continue_map_to_replay_commands()
    {
        var up = CreateSession();
        var upResult = Act(up, "up", AgentAction.Up);
        var rightResult = Act(up, "right", AgentAction.Right);
        var downResult = Act(up, "down", AgentAction.Down);
        var finished = up.Finish();
        var left = CreateSession();
        Act(left, "left-setup", AgentAction.Up);
        var leftResult = Act(left, "left", AgentAction.Left);
        var continued = CreateSession();
        var continueResult = Act(continued, "continue", AgentAction.Continue);
        var continuedReplay = continued.Finish().VerifiedReplay;

        Assert.Equal(Direction.Up, upResult.Observation.Direction);
        Assert.Equal(Direction.Right, rightResult.Observation.Direction);
        Assert.Equal(Direction.Down, downResult.Observation.Direction);
        Assert.Equal(Direction.Left, leftResult.Observation.Direction);
        Assert.Equal(1, continueResult.Observation.Tick);
        Assert.Equal([Direction.Up], finished.VerifiedReplay.Steps[0].Commands);
        Assert.Equal([Direction.Right], finished.VerifiedReplay.Steps[1].Commands);
        Assert.Equal([Direction.Down], finished.VerifiedReplay.Steps[2].Commands);
        Assert.Empty(continuedReplay.Steps[0].Commands);
    }

    [Fact]
    public void Illegal_stale_and_invalid_actions_advance_no_rules_state()
    {
        var session = CreateSession();
        var initial = session.Observe();

        var same = session.SubmitAction(Request("same", initial, AgentAction.Right));
        var opposite = session.SubmitAction(Request("opposite", initial, AgentAction.Left));
        var staleTick = session.SubmitAction(new AgentActionRequest(
            "stale-tick",
            initial.Tick + 1,
            initial.StateHash,
            AgentAction.Continue));
        var staleHash = session.SubmitAction(new AgentActionRequest(
            "stale-hash",
            initial.Tick,
            "not-the-state-hash",
            AgentAction.Continue));
        var invalid = session.SubmitAction(Request("invalid", initial, (AgentAction)255));

        Assert.Equal(AgentActionRejection.IllegalDirection, same.Rejection);
        Assert.Equal(AgentActionRejection.IllegalDirection, opposite.Rejection);
        Assert.Equal(AgentActionRejection.StaleTick, staleTick.Rejection);
        Assert.Equal(AgentActionRejection.StaleStateHash, staleHash.Rejection);
        Assert.Equal(AgentActionRejection.InvalidAction, invalid.Rejection);
        Assert.All(
            new[] { same, opposite, staleTick, staleHash, invalid },
            value => Assert.False(value.RulesAdvanced));
        Assert.Equal(initial.Tick, session.Observe().Tick);
        Assert.Equal(initial.StateHash, session.Observe().StateHash);
        Assert.Empty(session.Finish().VerifiedReplay.Steps);
    }

    [Fact]
    public void Exact_retry_returns_original_response_and_conflict_does_not_step()
    {
        var session = CreateSession();
        var initial = session.Observe();
        var request = Request("retry", initial, AgentAction.Up);

        var accepted = session.SubmitAction(request);
        var retry = session.SubmitAction(request);
        var conflict = session.SubmitAction(new AgentActionRequest(
            request.IdempotencyKey,
            initial.Tick,
            initial.StateHash,
            AgentAction.Up,
            AgentPublicIntent.TakeRisk));

        Assert.Same(accepted, retry);
        Assert.Equal(AgentActionRejection.IdempotencyConflict, conflict.Rejection);
        Assert.False(conflict.RulesAdvanced);
        Assert.Equal(1, session.Observe().Tick);
        Assert.Single(session.Finish().VerifiedReplay.Steps);
    }

    [Fact]
    public void Styled_rejections_and_idempotent_retries_do_not_mutate_evidence()
    {
        var session = new AgentMatchSession(new AgentMatchOptions(
            "styled-idempotency",
            RunModeCatalog.ClassicId,
            RunModeCatalog.CurrentModeVersion,
            123UL,
            AgentSeedVisibility.Open,
            maximumSteps: 10,
            styleContractId: AgentStyleContractCatalog.StillwaterId));
        var initial = session.Observe();
        var rejected = session.SubmitAction(Request("styled-rejected", initial, AgentAction.Left));
        var request = Request("styled-accepted", initial, AgentAction.Up);

        var accepted = session.SubmitAction(request);
        var retry = session.SubmitAction(request);
        var conflict = session.SubmitAction(new AgentActionRequest(
            request.IdempotencyKey,
            request.ExpectedTick,
            request.ExpectedStateHash,
            request.Action,
            AgentPublicIntent.TakeRisk));

        Assert.Equal(AgentActionRejection.IllegalDirection, rejected.Rejection);
        Assert.False(rejected.RulesAdvanced);
        Assert.Equal(initial.StyleContract, rejected.Observation.StyleContract);
        Assert.Same(accepted, retry);
        Assert.Equal(AgentActionRejection.IdempotencyConflict, conflict.Rejection);
        Assert.False(conflict.RulesAdvanced);
        Assert.Equal(accepted.Observation.StyleContract, conflict.Observation.StyleContract);
        Assert.Equal(1, session.Observe().Tick);

        var result = session.Finish();
        Assert.Single(result.VerifiedReplay.Steps);
        Assert.Equal(accepted.Observation.StyleContract!.Criteria, result.StyleOutcome!.Criteria);
        Assert.Equal(result.ReplayPayloadHash, result.StyleOutcome.ReplayPayloadHash);
    }

    [Fact]
    public async Task Concurrent_retry_is_serialized_and_steps_once()
    {
        var session = CreateSession();
        var initial = session.Observe();
        var request = Request("concurrent", initial, AgentAction.Up);
        using var ready = new CountdownEvent(8);
        using var start = new ManualResetEventSlim();
        var tasks = Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
        {
            ready.Signal();
            start.Wait();
            return session.SubmitAction(request);
        })).ToArray();
        ready.Wait();
        start.Set();

        var responses = await Task.WhenAll(tasks);

        Assert.All(responses, value => Assert.Same(responses[0], value));
        Assert.Equal(1, session.Observe().Tick);
        Assert.Single(session.Finish().VerifiedReplay.Steps);
    }

    [Fact]
    public void Mutation_ledger_is_bounded_without_evicting_authoritative_keys()
    {
        var viewer = new LatestViewerSink();
        var session = CreateSession(viewerSink: viewer);
        var initial = session.Observe();
        AgentActionResponse? first = null;
        for (var index = 0; index < AgentMatchSession.MaximumUniqueMutations; index++)
        {
            var request = new AgentActionRequest(
                $"bounded-{index}",
                initial.Tick + 1,
                initial.StateHash,
                AgentAction.Continue);
            var response = session.SubmitAction(request);
            first ??= response;
            Assert.Equal(AgentActionRejection.StaleTick, response.Rejection);
        }

        var knownRetry = session.SubmitAction(new AgentActionRequest(
            "bounded-0",
            initial.Tick + 1,
            initial.StateHash,
            AgentAction.Continue));
        var knownCrossOperation = session.SubmitBurst(new AgentBurstRequest(
            "bounded-0",
            initial.Tick,
            initial.StateHash,
            AgentAction.Continue,
            1));
        Assert.Equal(AgentViewerOperationKind.Burst, viewer.Latest!.Operation);
        Assert.Equal(0, viewer.Latest.StepsAdvanced);
        var unseenStep = session.SubmitAction(Request(
            "unseen-step",
            initial,
            AgentAction.Up));
        Assert.Equal(AgentViewerOperationKind.Step, viewer.Latest!.Operation);
        Assert.Equal(0, viewer.Latest.StepsAdvanced);
        var unseenBurst = session.SubmitBurst(new AgentBurstRequest(
            "unseen-burst",
            initial.Tick,
            initial.StateHash,
            AgentAction.Up,
            1));
        Assert.Equal(AgentViewerOperationKind.Burst, viewer.Latest!.Operation);
        Assert.Equal(0, viewer.Latest.StepsAdvanced);

        Assert.Same(first, knownRetry);
        Assert.Equal(AgentActionRejection.IdempotencyConflict, knownCrossOperation.Rejection);
        Assert.Equal(AgentActionRejection.MutationCapacityExceeded, unseenStep.Rejection);
        Assert.Equal(AgentActionRejection.MutationCapacityExceeded, unseenBurst.Rejection);
        Assert.False(unseenStep.RulesAdvanced);
        Assert.False(unseenBurst.RulesAdvanced);
        Assert.Equal(initial.Tick, session.Observe().Tick);
        Assert.Equal(initial.StateHash, session.Observe().StateHash);
    }

    [Fact]
    public void Burst_matches_equivalent_step_state_replay_metrics_and_rival()
    {
        var burst = CreateBurstSession(maximumSteps: 3, rivalPersonalityId: "optimal");
        var step = new AgentMatchSession(new AgentMatchOptions(
            "step-equivalent",
            RunModeCatalog.ClassicId,
            RunModeCatalog.CurrentModeVersion,
            123UL,
            AgentSeedVisibility.Open,
            maximumSteps: 3,
            rivalPersonalityId: "optimal"));
        var initial = burst.Observe();

        var burstResponse = burst.SubmitBurst(new AgentBurstRequest(
            "burst-equivalent",
            initial.Tick,
            initial.StateHash,
            AgentAction.Up,
            maximumSteps: 3,
            AgentPublicIntent.PreserveSpace));
        _ = Act(step, "step-0", AgentAction.Up);
        _ = Act(step, "step-1", AgentAction.Continue);
        var stepResponse = Act(step, "step-2", AgentAction.Continue);

        Assert.True(burstResponse.Accepted);
        Assert.True(burstResponse.RulesAdvanced);
        Assert.Equal(3, burstResponse.StepsAdvanced);
        Assert.Equal(AgentBurstStopReason.MatchStepLimit, burstResponse.StopReason);
        Assert.Equal(stepResponse.Observation.StateHash, burstResponse.Observation.StateHash);
        Assert.Equal(stepResponse.Observation.EpisodeMetrics, burstResponse.Observation.EpisodeMetrics);
        Assert.Equal(stepResponse.Observation.Rival, burstResponse.Observation.Rival);
        Assert.Equal(
            stepResponse.MatchResult!.ReplayPayloadHash,
            burstResponse.MatchResult!.ReplayPayloadHash);
        Assert.Equal(
            stepResponse.MatchResult.Rival!.ReplayPayloadHash,
            burstResponse.MatchResult.Rival!.ReplayPayloadHash);
        Assert.Equal(
            AgentPublicIntent.PreserveSpace,
            burstResponse.Observation.PreviousAction!.DeclaredIntent);
    }

    [Fact]
    public void Lesson_step_and_burst_report_equivalent_idempotent_progress()
    {
        var lesson = AgentSignalSchoolCatalog.Get("first-turn");
        var step = new AgentMatchSession(new AgentMatchOptions(
            "lesson-step",
            lesson.ModeId,
            RunModeCatalog.CurrentModeVersion,
            lesson.PracticeSeed,
            AgentSeedVisibility.Open,
            lesson.MaximumSteps,
            lessonId: lesson.Id));
        var burst = new AgentMatchSession(new AgentMatchOptions(
            "lesson-burst",
            lesson.ModeId,
            RunModeCatalog.CurrentModeVersion,
            lesson.PracticeSeed,
            AgentSeedVisibility.Open,
            lesson.MaximumSteps,
            actionProfile: AgentPassportV4.FourDirectionBurstActionProfile,
            lessonId: lesson.Id));
        var stepInitial = step.Observe();
        var burstInitial = burst.Observe();
        var stepReversalRequest = Request("lesson-reversal", stepInitial, AgentAction.Left);
        var burstReversalRequest = new AgentBurstRequest(
            "lesson-reversal",
            burstInitial.Tick,
            burstInitial.StateHash,
            AgentAction.Left,
            maximumSteps: AgentBurstRequest.MaximumBurstSteps);
        var stepRejection = step.SubmitAction(stepReversalRequest);
        var burstRejection = burst.SubmitBurst(burstReversalRequest);
        var stepRequest = Request("lesson-turn", stepRejection.Observation, AgentAction.Up);
        var burstRequest = new AgentBurstRequest(
            "lesson-turn",
            burstRejection.Observation.Tick,
            burstRejection.Observation.StateHash,
            AgentAction.Up,
            maximumSteps: AgentBurstRequest.MaximumBurstSteps);

        var stepResponse = step.SubmitAction(stepRequest);
        var burstResponse = burst.SubmitBurst(burstRequest);
        var stepRejectionRetry = step.SubmitAction(stepReversalRequest);
        var burstRejectionRetry = burst.SubmitBurst(burstReversalRequest);
        var stepRetry = step.SubmitAction(stepRequest);
        var burstRetry = burst.SubmitBurst(burstRequest);
        var stepResult = step.Finish();
        var burstResult = burst.Finish();

        Assert.Same(stepRejection, stepRejectionRetry);
        Assert.Same(burstRejection, burstRejectionRetry);
        Assert.Same(stepResponse, stepRetry);
        Assert.Same(burstResponse, burstRetry);
        Assert.Equal(AgentActionRejection.IllegalDirection, stepRejection.Rejection);
        Assert.Equal(AgentActionRejection.IllegalDirection, burstRejection.Rejection);
        Assert.Equal(
            ["opposite_reversal_rejected"],
            stepRejection.LessonDelta!.NewlySatisfiedRequirementIds);
        Assert.Equal(
            ["opposite_reversal_rejected"],
            burstRejection.LessonDelta!.NewlySatisfiedRequirementIds);
        Assert.Equal(1, stepRejection.Observation.LessonProgress!.AttemptEvidenceCount);
        Assert.Equal(1, burstRejection.Observation.LessonProgress!.AttemptEvidenceCount);
        Assert.Equal(stepResponse.Observation.StateHash, burstResponse.Observation.StateHash);
        Assert.Equal(1, burstResponse.StepsAdvanced);
        Assert.Equal(AgentBurstStopReason.LessonRequirementsReached, burstResponse.StopReason);
        Assert.Null(burstResponse.MatchResult);
        Assert.Equal(
            stepResponse.Observation.LessonProgress!.Requirements,
            burstResponse.Observation.LessonProgress!.Requirements);
        Assert.Equal(
            ["legal_turn_after_rejection"],
            stepResponse.LessonDelta!.NewlySatisfiedRequirementIds);
        Assert.Equal(
            ["legal_turn_after_rejection"],
            burstResponse.LessonDelta!.NewlySatisfiedRequirementIds);
        Assert.True(stepResponse.LessonDelta.AllRequirementsReachedThisMutation);
        Assert.True(burstResponse.LessonDelta.AllRequirementsReachedThisMutation);
        Assert.Equal(stepResult.ReplayPayloadHash, burstResult.ReplayPayloadHash);
        Assert.Equal(
            stepResult.LessonOutcome!.Requirements,
            burstResult.LessonOutcome!.Requirements);
        Assert.NotEqual(
            stepResult.LessonOutcome.AttemptEvidenceHash,
            burstResult.LessonOutcome.AttemptEvidenceHash);
    }

    [Fact]
    public void Burst_stops_at_first_public_decision_event_and_remains_bounded()
    {
        var session = CreateBurstSession(maximumSteps: 100);
        AgentBurstResponse? stopped = null;
        for (var index = 0; index < 8 && stopped is null; index++)
        {
            var observation = session.Observe();
            var response = session.SubmitBurst(new AgentBurstRequest(
                $"burst-{index}",
                observation.Tick,
                observation.StateHash,
                index == 0 ? AgentAction.Up : AgentAction.Continue,
                AgentBurstRequest.MaximumBurstSteps));
            Assert.InRange(response.StepsAdvanced, 1, AgentBurstRequest.MaximumBurstSteps);
            if (response.StopReason == AgentBurstStopReason.DecisionEvent)
            {
                stopped = response;
            }
        }

        var decision = Assert.IsType<AgentBurstResponse>(stopped);
        Assert.NotNull(decision.StopEvent);
        Assert.Contains(decision.StopEvent!.Value, AgentBurstPolicy.Stops);
        Assert.Contains(
            decision.Observation.PreviousEvents,
            item => item.Kind == decision.StopEvent);
    }

    [Fact]
    public void Burst_retry_is_exact_and_cross_operation_keys_never_advance_twice()
    {
        var session = CreateBurstSession(maximumSteps: 20);
        var initial = session.Observe();
        var request = new AgentBurstRequest(
            "shared-key",
            initial.Tick,
            initial.StateHash,
            AgentAction.Up,
            maximumSteps: 2);

        var accepted = session.SubmitBurst(request);
        var retry = session.SubmitBurst(request);
        var changedBurst = session.SubmitBurst(new AgentBurstRequest(
            request.IdempotencyKey,
            initial.Tick,
            initial.StateHash,
            AgentAction.Up,
            maximumSteps: 3));
        var conflict = session.SubmitAction(new AgentActionRequest(
            request.IdempotencyKey,
            initial.Tick,
            initial.StateHash,
            AgentAction.Up));

        Assert.Same(accepted, retry);
        Assert.Equal(AgentActionRejection.IdempotencyConflict, changedBurst.Rejection);
        Assert.False(changedBurst.RulesAdvanced);
        Assert.Equal(2, accepted.StepsAdvanced);
        Assert.Equal(AgentActionRejection.IdempotencyConflict, conflict.Rejection);
        Assert.False(conflict.RulesAdvanced);
        Assert.Equal(2, session.Observe().Tick);
        Assert.Equal(2, session.Finish().VerifiedReplay.Steps.Count);
    }

    [Fact]
    public void Burst_rejects_null_invalid_stale_and_post_terminal_requests_without_stepping()
    {
        var viewer = new RecordingViewerSink();
        var session = CreateBurstSession(maximumSteps: 1, viewerSink: viewer);
        var initial = session.Observe();

        Assert.Throws<ArgumentNullException>(() => session.SubmitBurst(null!));
        var staleTick = session.SubmitBurst(new AgentBurstRequest(
            "stale-tick-burst",
            initial.Tick + 1,
            initial.StateHash,
            AgentAction.Continue,
            1));
        var staleHash = session.SubmitBurst(new AgentBurstRequest(
            "stale-hash-burst",
            initial.Tick,
            "not-the-state-hash",
            AgentAction.Continue,
            1));
        var invalid = session.SubmitBurst(new AgentBurstRequest(
            "invalid-burst",
            initial.Tick,
            initial.StateHash,
            (AgentAction)255,
            1));

        Assert.Equal(AgentActionRejection.StaleTick, staleTick.Rejection);
        Assert.Equal(AgentActionRejection.StaleStateHash, staleHash.Rejection);
        Assert.Equal(AgentActionRejection.InvalidAction, invalid.Rejection);
        Assert.All(
            new[] { staleTick, staleHash, invalid },
            response =>
            {
                Assert.False(response.Accepted);
                Assert.False(response.RulesAdvanced);
                Assert.Equal(0, response.StepsAdvanced);
                Assert.Null(response.StopReason);
            });
        Assert.Equal(0, session.Observe().Tick);

        var completed = session.SubmitBurst(new AgentBurstRequest(
            "complete-burst",
            initial.Tick,
            initial.StateHash,
            AgentAction.Up,
            1));
        var after = session.SubmitBurst(new AgentBurstRequest(
            "after-burst",
            completed.Observation.Tick,
            completed.Observation.StateHash,
            AgentAction.Continue,
            1));

        Assert.Equal(AgentBurstStopReason.MatchStepLimit, completed.StopReason);
        Assert.Equal(AgentActionRejection.MatchNotAwaitingAction, after.Rejection);
        Assert.False(after.RulesAdvanced);
        Assert.Equal(6, viewer.Frames.Count);
        Assert.Equal(AgentViewerOperationKind.Initial, viewer.Frames[0].Operation);
        Assert.All(
            viewer.Frames.Skip(1),
            frame => Assert.Equal(AgentViewerOperationKind.Burst, frame.Operation));
        Assert.Equal([0, 0, 0, 1, 0], viewer.Frames.Skip(1)
            .Select(frame => frame.StepsAdvanced)
            .ToArray());
        Assert.All(
            viewer.Frames.Where(frame => frame.StepsAdvanced == 0),
            frame =>
            {
                Assert.Equal(frame.StartTick, frame.Observation.Tick);
                Assert.Equal(frame.StartStateHash, frame.Observation.StateHash);
            });
    }

    [Fact]
    public void Burst_rules_terminal_takes_precedence_and_returns_verified_result()
    {
        var session = CreateBurstSession(
            maximumSteps: 1_000,
            modeId: RunModeCatalog.VibeId);
        AgentBurstResponse? terminal = null;
        for (var index = 0; index < 1_000 && terminal?.MatchResult is null; index++)
        {
            var observation = session.Observe();
            terminal = session.SubmitBurst(new AgentBurstRequest(
                $"terminal-burst-{index}",
                observation.Tick,
                observation.StateHash,
                ChooseStarvationAction(observation),
                AgentBurstRequest.MaximumBurstSteps));
        }

        var result = Assert.IsType<AgentMatchResultV5>(terminal!.MatchResult);
        Assert.Equal(AgentBurstStopReason.RulesTerminal, terminal.StopReason);
        Assert.Equal(AgentMatchEndReason.RulesTerminal, result.EndReason);
        Assert.True(result.VerifiedReplay.Verify().IsValid);
        Assert.InRange(terminal.StepsAdvanced, 1, AgentBurstRequest.MaximumBurstSteps);
    }

    [Theory]
    [InlineData(false, 0, 1)]
    [InlineData(false, 0, 2)]
    [InlineData(false, 1, 1)]
    [InlineData(false, 1, 2)]
    [InlineData(true, 0, 1)]
    [InlineData(true, 0, 2)]
    [InlineData(true, 1, 1)]
    [InlineData(true, 1, 2)]
    public void Terminal_replay_failure_is_typed_cached_and_visible(
        bool burst,
        int failedLaneValue,
        int failureValue)
    {
        var failedLane = (AgentReplayLane)failedLaneValue;
        var failure = (AgentReplayFinalizationFailure)failureValue;
        var viewer = new RecordingViewerSink();
        var options = new AgentMatchOptions(
            "failed-finalization",
            RunModeCatalog.ClassicId,
            RunModeCatalog.CurrentModeVersion,
            123UL,
            AgentSeedVisibility.Open,
            maximumSteps: 1,
            styleContractId: AgentStyleContractCatalog.StillwaterId,
            rivalPersonalityId: failedLane == AgentReplayLane.Rival ? "optimal" : null,
            actionProfile: burst
                ? AgentPassportV4.FourDirectionBurstActionProfile
                : AgentPassportV4.FourDirectionActionProfile);
        var session = new AgentMatchSession(
            options,
            viewer,
            new FaultingReplayFinalizer(failedLane, failure));
        var initial = session.Observe();

        object response;
        object retry;
        if (burst)
        {
            var request = new AgentBurstRequest(
                "failed-terminal",
                initial.Tick,
                initial.StateHash,
                AgentAction.Up,
                1);
            response = session.SubmitBurst(request);
            retry = session.SubmitBurst(request);
            var typed = Assert.IsType<AgentBurstResponse>(response);
            Assert.False(typed.Accepted);
            Assert.True(typed.RulesAdvanced);
            Assert.Equal(1, typed.StepsAdvanced);
            Assert.Equal(AgentActionRejection.ReplayFailure, typed.Rejection);
            Assert.Equal(AgentBurstStopReason.ReplayFailure, typed.StopReason);
            Assert.Null(typed.MatchResult);
        }
        else
        {
            var request = Request("failed-terminal", initial, AgentAction.Up);
            response = session.SubmitAction(request);
            retry = session.SubmitAction(request);
            var typed = Assert.IsType<AgentActionResponse>(response);
            Assert.False(typed.Accepted);
            Assert.True(typed.RulesAdvanced);
            Assert.Equal(AgentActionRejection.ReplayFailure, typed.Rejection);
            Assert.Null(typed.MatchResult);
        }

        Assert.Same(response, retry);
        Assert.Equal(AgentMatchLifecycle.FailedClosed, session.Lifecycle);
        Assert.Null(session.GetResult());
        Assert.Equal(2, viewer.Frames.Count);
        Assert.Equal(
            burst ? AgentViewerOperationKind.Burst : AgentViewerOperationKind.Step,
            viewer.Frames[^1].Operation);
        Assert.Equal(1, viewer.Frames[^1].StepsAdvanced);
        Assert.Equal(AgentMatchEndReason.ReplayFailure, viewer.Frames[^1].EndReason);
        Assert.False(viewer.Frames[^1].VerifiedResultAvailable);
        Assert.NotNull(viewer.Frames[^1].Observation.StyleContract);
        Assert.Null(viewer.Frames[^1].StyleOutcome);
        Assert.Equal(
            AgentActionRejection.ReplayFailure,
            viewer.Frames[^1].Observation.PreviousAction!.Rejection);
        Assert.Throws<InvalidOperationException>(() => session.Finish());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void Lesson_finalizer_failure_exposes_only_failed_closed_live_truth(
        int failureValue)
    {
        var failure = (AgentReplayFinalizationFailure)failureValue;
        var lesson = AgentSignalSchoolCatalog.Get(AgentSignalSchoolCatalog.FirstTurnId);
        var viewer = new RecordingViewerSink();
        var session = new AgentMatchSession(
            new AgentMatchOptions(
                "failed-lesson-finalization",
                lesson.ModeId,
                RunModeCatalog.CurrentModeVersion,
                lesson.PracticeSeed,
                AgentSeedVisibility.Open,
                lesson.MaximumSteps,
                lessonId: lesson.Id),
            viewer,
            new FaultingReplayFinalizer(AgentReplayLane.Agent, failure));

        Assert.Throws<InvalidOperationException>(() => session.Finish());

        var observation = session.Observe();
        Assert.Equal(AgentMatchLifecycle.FailedClosed, observation.Lifecycle);
        Assert.Equal(AgentLessonEvidenceState.FailedClosed, observation.LessonProgress!.EvidenceState);
        Assert.False(observation.LessonProgress.AllRequirementsSatisfied);
        Assert.NotNull(observation.LessonProgress.RetryDescriptor);
        Assert.Null(session.GetResult());
        Assert.False(viewer.Frames[^1].VerifiedResultAvailable);
        Assert.Null(viewer.Frames[^1].LessonOutcome);
    }

    [Fact]
    public void Lesson_recorder_failure_preserves_accepted_step_truth_without_an_outcome()
    {
        var lesson = AgentSignalSchoolCatalog.Get(AgentSignalSchoolCatalog.FirstTurnId);
        var session = new AgentMatchSession(new AgentMatchOptions(
            "failed-lesson-recorder",
            lesson.ModeId,
            RunModeCatalog.CurrentModeVersion,
            lesson.PracticeSeed,
            AgentSeedVisibility.Open,
            lesson.MaximumSteps,
            lessonId: lesson.Id));
        var initial = session.Observe();
        var rejected = session.SubmitAction(Request(
            "failed-lesson-reversal",
            initial,
            AgentAction.Left));
        var accepted = session.SubmitAction(Request(
            "accepted-lesson-turn",
            rejected.Observation,
            AgentAction.Up));
        Assert.True(accepted.Accepted);
        Assert.True(accepted.Observation.LessonProgress!.AllRequirementsSatisfied);
        var recorderField = typeof(AgentMatchSession).GetField(
            "_recorder",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        var recorder = Assert.IsType<RunReplayRecorder>(recorderField!.GetValue(session));
        Assert.True(recorder.TryRecordCommand(Direction.Right));

        var response = session.SubmitAction(Request(
            "failed-lesson-recorder-step",
            accepted.Observation,
            AgentAction.Continue));

        Assert.False(response.Accepted);
        Assert.True(response.RulesAdvanced);
        Assert.Equal(AgentActionRejection.ReplayFailure, response.Rejection);
        Assert.Equal(2, response.Observation.Tick);
        Assert.Equal(AgentMatchLifecycle.FailedClosed, response.Observation.Lifecycle);
        var progress = Assert.IsType<AgentLessonProgressV3>(response.Observation.LessonProgress);
        Assert.Equal(AgentLessonEvidenceState.FailedClosed, progress.EvidenceState);
        Assert.True(progress.AllRequirementsSatisfied);
        Assert.Equal(2, progress.RequirementsSatisfied);
        Assert.Equal(1, progress.AttemptEvidenceCount);
        Assert.NotNull(progress.RetryDescriptor);
        Assert.Null(response.MatchResult);
        Assert.Null(session.GetResult());
    }

    [Fact]
    public void Recorder_step_failure_preserves_advanced_metrics_and_style_progress()
    {
        var viewer = new RecordingViewerSink();
        var session = new AgentMatchSession(
            new AgentMatchOptions(
                "failed-recorder-step",
                RunModeCatalog.ClassicId,
                RunModeCatalog.CurrentModeVersion,
                123UL,
                AgentSeedVisibility.Open,
                maximumSteps: 10,
                styleContractId: AgentStyleContractCatalog.StillwaterId),
            viewer);
        var initial = session.Observe();
        var recorderField = typeof(AgentMatchSession).GetField(
            "_recorder",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        var recorder = Assert.IsType<RunReplayRecorder>(recorderField!.GetValue(session));
        Assert.True(recorder.TryRecordCommand(Direction.Up));

        var request = Request("failed-recorder-step", initial, AgentAction.Continue);
        var response = session.SubmitAction(request);

        Assert.False(response.Accepted);
        Assert.True(response.RulesAdvanced);
        Assert.Equal(AgentActionRejection.ReplayFailure, response.Rejection);
        Assert.Null(response.MatchResult);
        Assert.Equal(AgentMatchLifecycle.FailedClosed, response.Observation.Lifecycle);
        Assert.Equal(1, response.Observation.Tick);
        Assert.Equal(1, response.Observation.EpisodeMetrics.SurvivalSteps);
        var progress = Assert.IsType<AgentStyleProgressV3>(response.Observation.StyleContract);
        Assert.Equal(1, progress.Criteria[0].Current);
        Assert.Equal(1, progress.Criteria[1].Denominator);
        Assert.Null(session.GetResult());
        var failedFrame = Assert.Single(viewer.Frames, frame => frame.Sequence == 1);
        Assert.Equal(AgentMatchEndReason.ReplayFailure, failedFrame.EndReason);
        Assert.False(failedFrame.VerifiedResultAvailable);
        Assert.Null(failedFrame.StyleOutcome);
        Assert.Equal(progress, failedFrame.Observation.StyleContract);
        Assert.Same(response, session.SubmitAction(request));
    }

    [Fact]
    public async Task Concurrent_burst_retry_advances_once_and_publishes_one_final_frame()
    {
        var viewer = new RecordingViewerSink();
        var session = CreateBurstSession(maximumSteps: 20, viewerSink: viewer);
        var initial = session.Observe();
        var request = new AgentBurstRequest(
            "concurrent-burst",
            initial.Tick,
            initial.StateHash,
            AgentAction.Up,
            maximumSteps: 3);
        using var ready = new CountdownEvent(8);
        using var start = new ManualResetEventSlim();
        var tasks = Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
        {
            ready.Signal();
            start.Wait();
            return session.SubmitBurst(request);
        })).ToArray();
        ready.Wait();
        start.Set();

        var responses = await Task.WhenAll(tasks);

        Assert.All(responses, response => Assert.Same(responses[0], response));
        Assert.Equal(3, session.Observe().Tick);
        Assert.Equal(2, viewer.Frames.Count);
        Assert.Equal(0, viewer.Frames[0].Observation.Tick);
        Assert.Equal(3, viewer.Frames[1].Observation.Tick);
        Assert.Equal(AgentViewerOperationKind.Initial, viewer.Frames[0].Operation);
        Assert.Equal(AgentViewerOperationKind.Burst, viewer.Frames[1].Operation);
        Assert.Equal(3, viewer.Frames[1].StepsAdvanced);
        Assert.Equal(AgentBurstStopReason.RequestedLimit, viewer.Frames[1].BurstStopReason);
    }

    [Fact]
    public void Step_and_burst_are_separate_control_divisions()
    {
        var stepViewer = new RecordingViewerSink();
        var step = CreateSession(viewerSink: stepViewer);
        var stepObservation = step.Observe();
        var rejectedBurst = step.SubmitBurst(new AgentBurstRequest(
            "wrong-burst",
            stepObservation.Tick,
            stepObservation.StateHash,
            AgentAction.Up,
            1));
        var burst = CreateBurstSession();
        var burstObservation = burst.Observe();
        var rejectedStep = burst.SubmitAction(Request(
            "wrong-step",
            burstObservation,
            AgentAction.Up));

        Assert.Equal(AgentActionRejection.WrongActionProfile, rejectedBurst.Rejection);
        Assert.Equal(AgentActionRejection.WrongActionProfile, rejectedStep.Rejection);
        Assert.False(rejectedBurst.RulesAdvanced);
        Assert.False(rejectedStep.RulesAdvanced);
        Assert.Equal(
            AgentPassportV4.FourDirectionActionProfile,
            stepObservation.Passport.ActionProfile);
        Assert.Equal(
            AgentPassportV4.FourDirectionBurstActionProfile,
            burstObservation.Passport.ActionProfile);
        Assert.Equal(AgentViewerOperationKind.Burst, stepViewer.Frames[^1].Operation);
        Assert.Equal(0, stepViewer.Frames[^1].StepsAdvanced);
    }

    [Fact]
    public void Public_state_route_reaches_rules_terminal_and_closes_match()
    {
        var session = CreateSession(maximumSteps: 1_000);
        AgentActionResponse? terminal = null;
        for (var index = 0; index < 1_000 && terminal?.MatchResult is null; index++)
        {
            terminal = Act(
                session,
                $"survive-{index}",
                ChooseStarvationAction(session.Observe()));
        }

        var terminalResponse = Assert.IsType<AgentActionResponse>(terminal);
        var matchResult = Assert.IsType<AgentMatchResultV5>(terminalResponse.MatchResult);
        Assert.Equal(AgentMatchEndReason.RulesTerminal, matchResult.EndReason);
        Assert.Equal(AgentMatchLifecycle.Completed, matchResult.Lifecycle);
        Assert.Equal(RunStatus.Dead, matchResult.RunStatus);
        Assert.Equal(DeathCause.Starvation, matchResult.DeathCause);
        Assert.True(matchResult.VerifiedReplay.Verify().IsValid);
        var after = session.SubmitAction(Request(
            "after",
            terminalResponse.Observation,
            AgentAction.Continue));
        Assert.Equal(AgentActionRejection.MatchNotAwaitingAction, after.Rejection);
        Assert.False(after.RulesAdvanced);
    }

    [Fact]
    public void Finish_aborts_nonterminal_match_with_verified_replay_and_is_idempotent()
    {
        var session = CreateSession();
        Act(session, "up", AgentAction.Up);

        var result = session.Finish();

        Assert.Equal(AgentMatchLifecycle.Aborted, result.Lifecycle);
        Assert.Equal(AgentMatchEndReason.AgentFinished, result.EndReason);
        Assert.Equal(AgentMatchResultV5.Contract, result.Schema);
        Assert.Equal("match", result.MatchId);
        Assert.Equal(RulesetIdentity.CurrentId, result.RulesetId);
        Assert.Equal(RulesetIdentity.CurrentVersion, result.RulesVersion);
        Assert.Equal(RunModeCatalog.VibeId, result.ModeId);
        Assert.Equal(RunModeCatalog.CurrentModeVersion, result.ModeVersion);
        Assert.Equal(RunConfig.ConfigHashAlgorithmId, result.ConfigHashAlgorithm);
        Assert.Equal(session.Observe().ConfigHash, result.ConfigHash);
        Assert.Equal(AgentSeedVisibility.Open, result.SeedVisibility);
        Assert.Equal(123UL, result.GameplaySeed);
        Assert.Same(AgentPassportV4.Anonymous, result.Passport);
        Assert.Equal(1, result.FinalTick);
        Assert.Equal(RunStatus.Running, result.RunStatus);
        Assert.Equal(DeathCause.None, result.DeathCause);
        Assert.Equal(0, result.Score);
        Assert.Equal(result.FinalStateHash, result.VerifiedReplay.Outcome.StateHash);
        Assert.Equal(result.ReplayPayloadHash, result.VerifiedReplay.PayloadHash);
        Assert.Equal(ReplayVerificationCode.Verified, result.ReplayVerificationCode);
        Assert.True(result.VerifiedReplay.Verify().IsValid);
        Assert.Null(result.Rival);
        Assert.Null(result.VerifiedRivalReplay);
        Assert.Same(result, session.Finish());
    }

    [Fact]
    public void Equal_seed_rival_advances_only_with_accepted_agent_steps_and_verifies_independently()
    {
        var session = new AgentMatchSession(new AgentMatchOptions(
            "rivalry",
            RunModeCatalog.VibeId,
            RunModeCatalog.CurrentModeVersion,
            123UL,
            AgentSeedVisibility.Open,
            maximumSteps: 4,
            rivalPersonalityId: "optimal"));
        var initial = session.Observe();
        var rejected = session.SubmitAction(Request("rejected", initial, AgentAction.Left));

        Assert.Equal(0, initial.Rival!.Tick);
        Assert.Equal("The Proof", initial.Rival.DisplayName);
        Assert.Equal(AgentActionRejection.IllegalDirection, rejected.Rejection);
        Assert.Equal(0, rejected.Observation.Rival!.Tick);

        AgentAction[] actions = [AgentAction.Up, AgentAction.Right, AgentAction.Continue, AgentAction.Down];
        AgentActionResponse? response = null;
        for (var index = 0; index < actions.Length; index++)
        {
            response = Act(session, $"rival-{index}", actions[index]);
            Assert.Equal(response.Observation.Tick, response.Observation.Rival!.Tick);
        }

        var result = Assert.IsType<AgentMatchResultV5>(response!.MatchResult);
        var rival = Assert.IsType<AgentRivalResultV1>(result.Rival);
        var rivalReplay = Assert.IsType<RunReplay>(result.VerifiedRivalReplay);
        Assert.Equal("optimal", rival.PersonalityId);
        Assert.Equal(result.FinalTick, rival.FinalTick);
        Assert.Equal(ReplayVerificationCode.Verified, rival.ReplayVerificationCode);
        Assert.True(rivalReplay.Verify().IsValid);
        Assert.Equal(rival.ReplayPayloadHash, rivalReplay.PayloadHash);
        Assert.Equal(rival.FinalStateHash, rivalReplay.Outcome.StateHash);
        Assert.Equal(
            result.VerifiedReplay.InitialCanonicalState,
            rivalReplay.InitialCanonicalState);
        Assert.NotEqual(
            result.VerifiedReplay.Serialize(),
            rivalReplay.Serialize());
    }

    [Fact]
    public void Rival_controller_and_replay_are_deterministic_for_same_seed_and_actions()
    {
        static AgentMatchSession RivalSession(string id) => new(new AgentMatchOptions(
            id,
            RunModeCatalog.VibeId,
            RunModeCatalog.CurrentModeVersion,
            789UL,
            AgentSeedVisibility.Blind,
            maximumSteps: 3,
            rivalPersonalityId: "balanced"));

        var first = RivalSession("first-rival");
        var second = RivalSession("second-rival");
        AgentAction[] actions = [AgentAction.Up, AgentAction.Right, AgentAction.Down];
        for (var index = 0; index < actions.Length; index++)
        {
            _ = Act(first, $"first-{index}", actions[index]);
            _ = Act(second, $"second-{index}", actions[index]);
        }

        var firstResult = first.GetResult()!;
        var secondResult = second.GetResult()!;
        Assert.Equal(firstResult.Rival, secondResult.Rival);
        Assert.Equal(
            firstResult.VerifiedRivalReplay!.Serialize(),
            secondResult.VerifiedRivalReplay!.Serialize());
    }

    [Fact]
    public void Same_seed_and_actions_produce_identical_states_events_and_replay()
    {
        var first = CreateSession(matchId: "first");
        var second = CreateSession(matchId: "second");
        AgentAction[] actions =
        [
            AgentAction.Up,
            AgentAction.Right,
            AgentAction.Continue,
            AgentAction.Down,
        ];

        for (var index = 0; index < actions.Length; index++)
        {
            var firstResponse = Act(first, $"first-{index}", actions[index]);
            var secondResponse = Act(second, $"second-{index}", actions[index]);
            Assert.Equal(firstResponse.Observation.StateHash, secondResponse.Observation.StateHash);
            Assert.Equal(firstResponse.Observation.PreviousEvents, secondResponse.Observation.PreviousEvents);
        }

        var firstResult = first.Finish();
        var secondResult = second.Finish();
        Assert.Equal(firstResult.FinalStateHash, secondResult.FinalStateHash);
        Assert.Equal(firstResult.ReplayPayloadHash, secondResult.ReplayPayloadHash);
        Assert.Equal(
            firstResult.VerifiedReplay.Serialize(),
            secondResult.VerifiedReplay.Serialize());
    }

    [Fact]
    public void Session_rejects_null_inputs_and_agentplay_references_no_shell_or_persistence()
    {
        Assert.Throws<ArgumentNullException>(() => new AgentMatchSession(null!));
        var session = CreateSession();
        Assert.Throws<ArgumentNullException>(() => session.SubmitAction(null!));

        var references = typeof(AgentMatchSession).Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .ToArray();
        Assert.Contains("VibeSnake.Rules", references);
        Assert.DoesNotContain("VibeSnake.Persistence", references);
        Assert.DoesNotContain("VibeSnake.Game", references);
        Assert.DoesNotContain("GodotSharp", references);
    }

    [Fact]
    public void Viewer_receives_full_frames_and_cannot_change_rules_on_failure()
    {
        var viewer = new RecordingViewerSink();
        var session = new AgentMatchSession(
            new AgentMatchOptions(
                "viewed",
                RunModeCatalog.VibeId,
                RunModeCatalog.CurrentModeVersion,
                123UL,
                AgentSeedVisibility.Open,
                maximumSteps: 2),
            viewer);
        var initial = session.Observe();

        var rejected = session.SubmitAction(Request("bad", initial, AgentAction.Left));
        viewer.Throw = true;
        var accepted = session.SubmitAction(Request(
            "good",
            rejected.Observation,
            AgentAction.Up));
        viewer.Throw = false;
        var result = session.Finish();

        Assert.Equal(1, accepted.Observation.Tick);
        Assert.Equal(4, viewer.Attempts);
        Assert.Equal([0L, 1L, 3L], viewer.Frames.Select(frame => frame.Sequence).ToArray());
        Assert.Equal(
            [
                AgentViewerOperationKind.Initial,
                AgentViewerOperationKind.Step,
                AgentViewerOperationKind.Finish,
            ],
            viewer.Frames.Select(frame => frame.Operation).ToArray());
        Assert.Equal([0, 0, 0], viewer.Frames.Select(frame => frame.StepsAdvanced).ToArray());
        Assert.Equal(initial.StateHash, viewer.Frames[0].Observation.StateHash);
        Assert.Equal(AgentMatchEndReason.None, viewer.Frames[0].EndReason);
        Assert.False(viewer.Frames[0].VerifiedResultAvailable);
        Assert.Equal(
            AgentActionRejection.IllegalDirection,
            viewer.Frames[1].Observation.PreviousAction!.Rejection);
        Assert.Equal(AgentMatchEndReason.None, viewer.Frames[1].EndReason);
        Assert.False(viewer.Frames[1].VerifiedResultAvailable);
        Assert.Equal(AgentMatchEndReason.AgentFinished, viewer.Frames[2].EndReason);
        Assert.True(viewer.Frames[2].VerifiedResultAvailable);
        Assert.Single(result.VerifiedReplay.Steps);
    }

    [Fact]
    public void Exhibition_receipt_hash_links_verified_identity_and_excludes_display_time()
    {
        var session = CreateSession(AgentSeedVisibility.Open, matchId: "receipt-match");
        Assert.Null(session.TryCreateExhibitionReceipt());

        var initial = session.Observe();
        var first = session.SubmitAction(Request(
            "receipt-1",
            initial,
            AgentAction.Up,
            AgentPublicIntent.PreserveSpace));
        Assert.True(first.Accepted);
        var second = session.SubmitAction(Request(
            "receipt-2",
            first.Observation,
            AgentAction.Continue,
            AgentPublicIntent.SeekFood));
        Assert.True(second.Accepted);

        // A live match has a verified identity only after it is finalized.
        Assert.Null(session.TryCreateExhibitionReceipt());
        var result = session.Finish();
        var receipt = Assert.IsType<AgentExhibitionReceiptV2>(session.TryCreateExhibitionReceipt());

        Assert.Equal(AgentExhibitionReceiptV2.Contract, receipt.Schema);
        Assert.Equal("receipt-match", receipt.MatchId);
        Assert.True(AgentExhibitionReceipt.HasCanonicalHash(receipt));
        Assert.Equal(64, receipt.ReceiptHash.Length);
        Assert.Equal(result.ReplayPayloadHash, receipt.AgentReplayPayloadHash);
        Assert.Equal(result.FinalStateHash, receipt.FinalStateHash);
        Assert.Equal(ReplayVerificationCode.Verified, receipt.AgentReplayVerificationCode);
        Assert.Equal("123", receipt.GameplaySeed);
        Assert.Null(receipt.RivalPersonalityId);
        Assert.Null(receipt.DisplayTimeUtc);

        Assert.Equal(
            AgentDivisionIdentityV1.Contract,
            receipt.Division.Schema);
        Assert.Equal(
            $"{RunModeCatalog.VibeId}@{RunModeCatalog.CurrentModeVersion}|open|"
                + $"{AgentPassportV4.SymbolicStepObservationProfile}|"
                + AgentPassportV4.FourDirectionActionProfile,
            receipt.Division.DivisionId);

        // Accepted presentation events keep their order, tick, action, and label.
        Assert.Collection(
            receipt.AcceptedPresentationEvents,
            item =>
            {
                Assert.Equal(1, item.Ordinal);
                Assert.Equal(1, item.Tick);
                Assert.Equal(AgentAction.Up, item.Action);
                Assert.Equal(AgentPublicIntent.PreserveSpace, item.DeclaredIntent);
            },
            item =>
            {
                Assert.Equal(2, item.Ordinal);
                Assert.Equal(2, item.Tick);
                Assert.Equal(AgentAction.Continue, item.Action);
                Assert.Equal(AgentPublicIntent.SeekFood, item.DeclaredIntent);
            });

        // Display time rides beside the canonical hash and never inside it.
        var shown = receipt.WithDisplayTime("2026-08-15T00:00:00Z");
        var shownLater = receipt.WithDisplayTime("2031-01-02T03:04:05Z");
        Assert.Equal("2026-08-15T00:00:00Z", shown.DisplayTimeUtc);
        Assert.Equal(receipt.ReceiptHash, shown.ReceiptHash);
        Assert.Equal(receipt.ReceiptHash, shownLater.ReceiptHash);
        Assert.True(AgentExhibitionReceipt.HasCanonicalHash(shownLater));

        // Repeating the identical exhibition reproduces the identical identity.
        var replayed = CreateSession(AgentSeedVisibility.Open, matchId: "receipt-match");
        var replayedInitial = replayed.Observe();
        var replayedFirst = replayed.SubmitAction(Request(
            "receipt-1",
            replayedInitial,
            AgentAction.Up,
            AgentPublicIntent.PreserveSpace));
        _ = replayed.SubmitAction(Request(
            "receipt-2",
            replayedFirst.Observation,
            AgentAction.Continue,
            AgentPublicIntent.SeekFood));
        _ = replayed.Finish();
        Assert.Equal(
            receipt.ReceiptHash,
            replayed.TryCreateExhibitionReceipt()!.ReceiptHash);

        // A rematch under a new handle is a new visit but the same line. The
        // instance identity changes; the route identity does not.
        var rematch = CreateSession(AgentSeedVisibility.Open, matchId: "receipt-rematch");
        var rematchFirst = rematch.SubmitAction(Request(
            "rematch-1",
            rematch.Observe(),
            AgentAction.Up,
            AgentPublicIntent.TakeRisk));
        _ = rematch.SubmitAction(Request(
            "rematch-2",
            rematchFirst.Observation,
            AgentAction.Continue,
            AgentPublicIntent.Recover));
        _ = rematch.Finish();
        var rematchReceipt = Assert.IsType<AgentExhibitionReceiptV2>(
            rematch.TryCreateExhibitionReceipt());
        Assert.True(AgentExhibitionReceipt.HasCanonicalHash(rematchReceipt));
        Assert.NotEqual(receipt.ReceiptHash, rematchReceipt.ReceiptHash);
        Assert.Equal(receipt.RouteIdentityHash, rematchReceipt.RouteIdentityHash);
        Assert.Equal(receipt.AgentReplayPayloadHash, rematchReceipt.AgentReplayPayloadHash);

        // Any tampered fact breaks the canonical hash.
        Assert.False(AgentExhibitionReceipt.HasCanonicalHash(receipt with { Score = 9_999 }));
        Assert.False(AgentExhibitionReceipt.HasCanonicalHash(
            receipt with { AgentReplayPayloadHash = new string('a', 64) }));
        Assert.False(AgentExhibitionReceipt.HasCanonicalHash(
            receipt with { AcceptedPresentationEvents = [] }));
    }

    [Fact]
    public void Exhibition_receipt_records_blind_styled_rival_and_lesson_divisions()
    {
        // A blind exhibition keeps its own division and reveals the seed only here.
        var blind = CreateSession(AgentSeedVisibility.Blind, maximumSteps: 1, matchId: "blind-receipt");
        _ = blind.SubmitAction(Request("blind-1", blind.Observe(), AgentAction.Continue));
        var blindReceipt = Assert.IsType<AgentExhibitionReceiptV2>(
            blind.TryCreateExhibitionReceipt());
        Assert.True(AgentExhibitionReceipt.HasCanonicalHash(blindReceipt));
        Assert.Equal(AgentSeedVisibility.Blind, blindReceipt.Division.SeedVisibility);
        Assert.Contains("|blind|", blindReceipt.Division.DivisionId, StringComparison.Ordinal);
        Assert.Equal("123", blindReceipt.GameplaySeed);
        Assert.Null(blindReceipt.StyleOutcome);
        Assert.Null(blindReceipt.LessonOutcome);
        Assert.Null(blindReceipt.RivalPersonalityId);

        // A styled rivalry links both verified lanes and the replay-bound style facts.
        var styled = new AgentMatchSession(new AgentMatchOptions(
            "styled-rival-receipt",
            RunModeCatalog.ClassicId,
            RunModeCatalog.CurrentModeVersion,
            123UL,
            AgentSeedVisibility.Open,
            maximumSteps: 2,
            styleContractId: AgentStyleContractCatalog.StillwaterId,
            rivalPersonalityId: "optimal"));
        var styledFirst = styled.SubmitAction(Request(
            "styled-1",
            styled.Observe(),
            AgentAction.Up,
            AgentPublicIntent.TakeRisk));
        var styledResult = styled.SubmitAction(Request(
            "styled-2",
            styledFirst.Observation,
            AgentAction.Continue)).MatchResult;
        Assert.NotNull(styledResult);
        var styledReceipt = Assert.IsType<AgentExhibitionReceiptV2>(
            styled.TryCreateExhibitionReceipt());
        Assert.True(AgentExhibitionReceipt.HasCanonicalHash(styledReceipt));
        Assert.Equal("optimal", styledReceipt.RivalPersonalityId);
        Assert.Equal(styledResult.Rival!.ReplayPayloadHash, styledReceipt.RivalReplayPayloadHash);
        Assert.Equal(
            ReplayVerificationCode.Verified,
            styledReceipt.RivalReplayVerificationCode);
        Assert.Equal(styledResult.Rival.Score, styledReceipt.RivalScore);
        Assert.Equal(styledResult.StyleOutcome, styledReceipt.StyleOutcome);
        Assert.False(styledReceipt.StyleOutcome!.AllThresholdsReached);
        Assert.Equal(2, styledReceipt.AcceptedPresentationEvents.Count);

        // A satisfied lesson links its combined evidence hash into the receipt.
        var lesson = AgentSignalSchoolCatalog.Get(AgentSignalSchoolCatalog.WrapLineId);
        var practice = new AgentMatchSession(new AgentMatchOptions(
            "lesson-receipt",
            lesson.ModeId,
            RunModeCatalog.CurrentModeVersion,
            lesson.PracticeSeed,
            AgentSeedVisibility.Open,
            lesson.MaximumSteps,
            lessonId: lesson.Id));
        var observation = practice.Observe();
        for (var step = 0; step < lesson.MaximumSteps; step++)
        {
            var response = practice.SubmitAction(Request(
                $"lesson-{step}",
                observation,
                AgentAction.Continue));
            observation = response.Observation;
            if (observation.LessonProgress?.AllRequirementsSatisfied == true)
            {
                break;
            }
        }

        Assert.True(observation.LessonProgress!.AllRequirementsSatisfied);
        var practiceResult = practice.Finish();
        var lessonReceipt = Assert.IsType<AgentExhibitionReceiptV2>(
            practice.TryCreateExhibitionReceipt());
        Assert.True(AgentExhibitionReceipt.HasCanonicalHash(lessonReceipt));
        Assert.Equal(practiceResult.LessonOutcome, lessonReceipt.LessonOutcome);
        Assert.True(lessonReceipt.LessonOutcome!.AllRequirementsSatisfied);
        Assert.Equal(AgentMatchLifecycle.Completed, lessonReceipt.Lifecycle);
        Assert.Equal(RunStatus.Running, lessonReceipt.RunStatus);
    }

    [Fact]
    public void Exhibition_route_identity_survives_a_first_turn_rematch_with_new_keys()
    {
        // first-turn folds a hash of the idempotency key into its attempt evidence,
        // so two legal runs of the same lesson differ in evidence and in receipt
        // identity. The route identity must still recognise the same walked line.
        static AgentExhibitionReceiptV2 RunFirstTurn(string matchId, string keyPrefix)
        {
            var lesson = AgentSignalSchoolCatalog.Get(AgentSignalSchoolCatalog.FirstTurnId);
            var session = new AgentMatchSession(new AgentMatchOptions(
                matchId,
                lesson.ModeId,
                RunModeCatalog.CurrentModeVersion,
                lesson.PracticeSeed,
                AgentSeedVisibility.Open,
                lesson.MaximumSteps,
                lessonId: lesson.Id));
            var initial = session.Observe();
            var reversed = session.SubmitAction(new AgentActionRequest(
                $"{keyPrefix}-reversal",
                initial.Tick,
                initial.StateHash,
                OppositeAction(initial.Direction)));
            Assert.Equal(AgentActionRejection.IllegalDirection, reversed.Rejection);
            var turned = session.SubmitAction(new AgentActionRequest(
                $"{keyPrefix}-turn",
                reversed.Observation.Tick,
                reversed.Observation.StateHash,
                LegalTurnAction(initial.Direction)));
            Assert.True(turned.Accepted);
            Assert.True(turned.Observation.LessonProgress!.AllRequirementsSatisfied);
            _ = session.Finish();
            return Assert.IsType<AgentExhibitionReceiptV2>(
                session.TryCreateExhibitionReceipt());
        }

        var first = RunFirstTurn("first-turn-a", "alpha");
        var second = RunFirstTurn("first-turn-b", "bravo");

        Assert.True(AgentExhibitionReceipt.HasCanonicalHash(first));
        Assert.True(AgentExhibitionReceipt.HasCanonicalHash(second));
        Assert.NotEqual(
            first.LessonOutcome!.AttemptEvidenceHash,
            second.LessonOutcome!.AttemptEvidenceHash);
        Assert.NotEqual(first.ReceiptHash, second.ReceiptHash);
        Assert.Equal(first.RouteIdentityHash, second.RouteIdentityHash);
    }

    private static AgentAction OppositeAction(Direction direction) => direction switch
    {
        Direction.Up => AgentAction.Down,
        Direction.Down => AgentAction.Up,
        Direction.Left => AgentAction.Right,
        _ => AgentAction.Left,
    };

    private static AgentAction LegalTurnAction(Direction direction) => direction switch
    {
        Direction.Up or Direction.Down => AgentAction.Left,
        _ => AgentAction.Up,
    };

    [Fact]
    public void Exhibition_receipt_fails_closed_for_unverified_lifecycles_and_lanes()
    {
        var session = new AgentMatchSession(new AgentMatchOptions(
            "receipt-guard",
            RunModeCatalog.ClassicId,
            RunModeCatalog.CurrentModeVersion,
            123UL,
            AgentSeedVisibility.Open,
            maximumSteps: 1,
            rivalPersonalityId: "optimal"));
        var result = session.SubmitAction(Request(
            "guard-1",
            session.Observe(),
            AgentAction.Continue)).MatchResult;
        Assert.NotNull(result);
        Assert.NotNull(AgentExhibitionReceipt.TryCreate(result, []));

        // Only a verified agent lane and a settled lifecycle earn an identity.
        Assert.Null(AgentExhibitionReceipt.TryCreate(
            result with { ReplayVerificationCode = ReplayVerificationCode.CheckpointDiverged },
            []));
        Assert.Null(AgentExhibitionReceipt.TryCreate(
            result with { Lifecycle = AgentMatchLifecycle.AwaitingAction },
            []));
        Assert.Null(AgentExhibitionReceipt.TryCreate(
            result with { Lifecycle = AgentMatchLifecycle.FailedClosed },
            []));

        // A rivalry is receipted only when both lanes verified independently.
        Assert.Null(AgentExhibitionReceipt.TryCreate(
            result with
            {
                Rival = result.Rival! with
                {
                    ReplayVerificationCode = ReplayVerificationCode.CheckpointDiverged,
                },
            },
            []));
    }

    [Fact]
    public void Exhibition_receipt_is_unavailable_for_a_failed_closed_match()
    {
        var session = new AgentMatchSession(
            new AgentMatchOptions(
                "failed-receipt",
                RunModeCatalog.ClassicId,
                RunModeCatalog.CurrentModeVersion,
                42UL,
                AgentSeedVisibility.Open,
                maximumSteps: 1),
            viewerSink: null,
            new FaultingReplayFinalizer(
                AgentReplayLane.Agent,
                AgentReplayFinalizationFailure.Verification));
        var initial = session.Observe();

        var response = session.SubmitAction(Request("failed-1", initial, AgentAction.Up));

        Assert.Equal(AgentActionRejection.ReplayFailure, response.Rejection);
        Assert.Equal(AgentMatchLifecycle.FailedClosed, session.Lifecycle);
        Assert.Null(session.TryCreateExhibitionReceipt());
    }

    [Fact]
    public void Published_survival_state_matches_the_board_on_every_frame()
    {
        var sink = new RecordingViewerSink();
        var session = CreateSession(viewerSink: sink);
        var directions = new[]
        {
            AgentAction.Up,
            AgentAction.Left,
            AgentAction.Down,
            AgentAction.Right,
            AgentAction.Up,
            AgentAction.Left,
        };
        for (var index = 0; index < directions.Length; index++)
        {
            var current = session.Observe();
            session.SubmitAction(Request($"survival-{index}", current, directions[index]));
        }

        Assert.Equal(directions.Length + 1, sink.Frames.Count);
        foreach (var frame in sink.Frames)
        {
            AgentSurvivalTestFacts.AssertSurvivalEquivalent(
                AgentSurvivalTestFacts.SurvivalFor(frame.Observation),
                frame.SurvivalState);
            Assert.Equal(AgentSurvivalStateV1.Contract, frame.SurvivalState.Schema);
            Assert.Equal(
                AgentSurvivalStateV1.RecoveryOrder,
                frame.SurvivalState.RecoveryResources.Select(item => item.Kind));
        }
    }

    [Fact]
    public void Published_survival_state_reports_an_open_start_and_a_closed_terminal()
    {
        var sink = new RecordingViewerSink();
        var session = CreateSession(viewerSink: sink, maximumSteps: 1);
        var initial = sink.Frames[0].SurvivalState;

        Assert.Equal(AgentSurvivalStateV1.RunningCandidateExits, initial.CandidateExits);
        Assert.Equal(AgentSurvivalStateV1.RunningCandidateExits, initial.StructuralOpenExits);
        Assert.Equal(AgentExitPressureV1.Open, initial.ExitPressure);
        Assert.Equal(0, initial.HeldRecoveryCount);
        Assert.All(
            initial.RecoveryResources,
            resource =>
            {
                Assert.False(resource.Held);
                Assert.Equal(0, resource.TicksRemaining);
            });

        var observation = session.Observe();
        session.SubmitAction(Request("survival-cap", observation, AgentAction.Up));
        var last = sink.Frames[^1].SurvivalState;

        // A capped match keeps a living snake, so the structural facts stay real.
        AgentSurvivalTestFacts.AssertSurvivalEquivalent(
            AgentSurvivalTestFacts.SurvivalFor(sink.Frames[^1].Observation),
            last);
    }

    [Theory]
    [InlineData(3, AgentExitPressureV1.Open)]
    [InlineData(2, AgentExitPressureV1.Narrow)]
    [InlineData(1, AgentExitPressureV1.Pinned)]
    [InlineData(0, AgentExitPressureV1.Trapped)]
    public void Exit_pressure_is_a_threshold_crossing_of_the_open_exit_count(
        int structuralOpenExits,
        AgentExitPressureV1 expected)
    {
        Assert.Equal(expected, AgentSurvivalStateV1.Pressure(true, structuralOpenExits));
        Assert.Equal(
            AgentExitPressureV1.NotRunning,
            AgentSurvivalStateV1.Pressure(false, structuralOpenExits));
    }

    [Fact]
    public void Survival_state_reports_held_recovery_resources_without_naming_a_route()
    {
        var survival = AgentSurvivalStateV1.Create(
            running: true,
            structuralOpenExits: 2,
            shieldTicksRemaining: 40,
            phaseShiftTicksRemaining: 0,
            lastStandHeld: true,
            lastStandRecoveryTicksRemaining: 0,
            slowMoTicksRemaining: 7);

        Assert.Equal(3, survival.HeldRecoveryCount);
        Assert.Equal(AgentExitPressureV1.Narrow, survival.ExitPressure);
        var byKind = survival.RecoveryResources.ToDictionary(item => item.Kind);
        Assert.Equal(40, byKind[AgentRecoveryResourceKind.Shield].TicksRemaining);
        Assert.False(byKind[AgentRecoveryResourceKind.PhaseShift].Held);
        Assert.True(byKind[AgentRecoveryResourceKind.LastStand].Held);
        Assert.Equal(0, byKind[AgentRecoveryResourceKind.LastStand].TicksRemaining);
        Assert.Equal(7, byKind[AgentRecoveryResourceKind.SlowMo].TicksRemaining);
    }

    private static AgentMatchSession CreateSession(
        AgentSeedVisibility visibility = AgentSeedVisibility.Open,
        int maximumSteps = AgentMatchOptions.DefaultMaximumSteps,
        string matchId = "match",
        IAgentViewerSink? viewerSink = null) =>
        new(
            new AgentMatchOptions(
                matchId,
                RunModeCatalog.VibeId,
                RunModeCatalog.CurrentModeVersion,
                123UL,
                visibility,
                maximumSteps),
            viewerSink);

    private static AgentMatchSession CreateBurstSession(
        int maximumSteps = AgentMatchOptions.DefaultMaximumSteps,
        string? rivalPersonalityId = null,
        IAgentViewerSink? viewerSink = null,
        string modeId = RunModeCatalog.ClassicId) =>
        new(
            new AgentMatchOptions(
                "burst-match",
                modeId,
                RunModeCatalog.CurrentModeVersion,
                123UL,
                AgentSeedVisibility.Open,
                maximumSteps,
                rivalPersonalityId: rivalPersonalityId,
                actionProfile: AgentPassportV4.FourDirectionBurstActionProfile),
            viewerSink);

    private static AgentActionResponse Act(
        AgentMatchSession session,
        string key,
        AgentAction action)
    {
        var observation = session.Observe();
        return session.SubmitAction(Request(key, observation, action));
    }

    private static AgentActionRequest Request(
        string key,
        AgentObservationV5 observation,
        AgentAction action,
        AgentPublicIntent declaredIntent = AgentPublicIntent.Undeclared) =>
        new(key, observation.Tick, observation.StateHash, action, declaredIntent);

    private static AgentAction ChooseStarvationAction(AgentObservationV5 observation)
    {
        Direction[] candidates =
        [
            observation.Direction,
            TurnLeft(observation.Direction),
            TurnRight(observation.Direction),
        ];
        foreach (var direction in candidates)
        {
            var next = Advance(observation.Head, direction, observation.BoardWidth, observation.BoardHeight);
            var wouldEat = observation.Food == next;
            var wouldCollectPower = observation.PowerPickup?.Position == next;
            var wouldHitObstacle = observation.DetachedObstacles.Contains(next);
            var wouldHitBody = observation.Body.Skip(1).Contains(next);
            if (!wouldEat && !wouldCollectPower && !wouldHitObstacle && !wouldHitBody)
            {
                return ToAction(direction, observation.Direction);
            }
        }

        return AgentAction.Continue;
    }

    private static AgentPointV1 Advance(
        AgentPointV1 point,
        Direction direction,
        int width,
        int height)
    {
        var offset = direction.Offset();
        return new AgentPointV1(
            (point.X + offset.X + width) % width,
            (point.Y + offset.Y + height) % height);
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

    private sealed class RecordingViewerSink : IAgentViewerSink
    {
        public List<AgentViewerFrameV9> Frames { get; } = [];

        public int Attempts { get; private set; }

        public bool Throw { get; set; }

        public bool TryPublish(AgentViewerFrameV9 frame)
        {
            Attempts++;
            if (Throw)
            {
                throw new IOException("viewer unavailable");
            }

            Frames.Add(frame);
            return true;
        }
    }

    private sealed class LatestViewerSink : IAgentViewerSink
    {
        public AgentViewerFrameV9? Latest { get; private set; }

        public bool TryPublish(AgentViewerFrameV9 frame)
        {
            Latest = frame;
            return true;
        }
    }

    private sealed class FaultingReplayFinalizer(
        AgentReplayLane failedLane,
        AgentReplayFinalizationFailure failure) : IAgentReplayFinalizer
    {
        public AgentReplayFinalization Finalize(
            AgentReplayLane lane,
            RunReplayRecorder recorder,
            SnakeRun run) =>
            lane == failedLane
                ? AgentReplayFinalization.Failed(failure)
                : AgentReplayFinalizer.Instance.Finalize(lane, recorder, run);
    }
}
