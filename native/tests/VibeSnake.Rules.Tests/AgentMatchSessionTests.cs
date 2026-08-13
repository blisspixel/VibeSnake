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
        var matchResult = Assert.IsType<AgentMatchResult>(terminalResponse.MatchResult);
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
        Assert.Equal(AgentMatchResult.Contract, result.Schema);
        Assert.Equal("match", result.MatchId);
        Assert.Equal(RulesetIdentity.CurrentId, result.RulesetId);
        Assert.Equal(RulesetIdentity.CurrentVersion, result.RulesVersion);
        Assert.Equal(RunModeCatalog.VibeId, result.ModeId);
        Assert.Equal(RunModeCatalog.CurrentModeVersion, result.ModeVersion);
        Assert.Equal(RunConfig.ConfigHashAlgorithmId, result.ConfigHashAlgorithm);
        Assert.Equal(session.Observe().ConfigHash, result.ConfigHash);
        Assert.Equal(AgentSeedVisibility.Open, result.SeedVisibility);
        Assert.Equal(123UL, result.GameplaySeed);
        Assert.Same(AgentPassportV1.Anonymous, result.Passport);
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

        var result = Assert.IsType<AgentMatchResult>(response!.MatchResult);
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

        Assert.Equal(1, accepted.Observation.Tick);
        Assert.Equal(3, viewer.Attempts);
        Assert.Equal([0L, 1L], viewer.Frames.Select(frame => frame.Sequence).ToArray());
        Assert.Equal(initial.StateHash, viewer.Frames[0].Observation.StateHash);
        Assert.Equal(
            AgentActionRejection.IllegalDirection,
            viewer.Frames[1].Observation.PreviousAction!.Rejection);
        Assert.Single(session.Finish().VerifiedReplay.Steps);
    }

    private static AgentMatchSession CreateSession(
        AgentSeedVisibility visibility = AgentSeedVisibility.Open,
        int maximumSteps = AgentMatchOptions.DefaultMaximumSteps,
        string matchId = "match") =>
        new(new AgentMatchOptions(
            matchId,
            RunModeCatalog.VibeId,
            RunModeCatalog.CurrentModeVersion,
            123UL,
            visibility,
            maximumSteps));

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
        AgentObservationV1 observation,
        AgentAction action,
        AgentPublicIntent declaredIntent = AgentPublicIntent.Undeclared) =>
        new(key, observation.Tick, observation.StateHash, action, declaredIntent);

    private static AgentAction ChooseStarvationAction(AgentObservationV1 observation)
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
        public List<AgentViewerFrameV1> Frames { get; } = [];

        public int Attempts { get; private set; }

        public bool Throw { get; set; }

        public bool TryPublish(AgentViewerFrameV1 frame)
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
}
