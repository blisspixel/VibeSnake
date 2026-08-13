using VibeSnake.Rules;

namespace VibeSnake.AgentPlay;

public sealed class AgentMatchSession
{
    public const int MaximumUniqueMutations = 4_096;

    private readonly object _sync = new();
    private readonly AgentMatchOptions _options;
    private readonly RunConfig _config;
    private readonly SnakeRun _run;
    private readonly RunReplayRecorder _recorder;
    private readonly AgentEpisodeMetricsTracker _metrics = new();
    private readonly SnakeRun? _rivalRun;
    private readonly AiPersonalityController? _rivalController;
    private readonly RunReplayRecorder? _rivalRecorder;
    private readonly AgentEpisodeMetricsTracker? _rivalMetrics;
    private readonly IAgentViewerSink? _viewerSink;
    private readonly IAgentReplayFinalizer _replayFinalizer;
    private readonly Dictionary<string, ProcessedMutation> _processedMutations =
        new(StringComparer.Ordinal);
    private IReadOnlyList<RunEventDetail> _previousEvents =
        Array.Empty<RunEventDetail>();
    private AgentPreviousActionV1? _previousAction;
    private AgentMatchResult? _matchResult;
    private long _viewerSequence;

    public AgentMatchSession(
        AgentMatchOptions options,
        IAgentViewerSink? viewerSink = null)
        : this(options, viewerSink, AgentReplayFinalizer.Instance)
    {
    }

    internal AgentMatchSession(
        AgentMatchOptions options,
        IAgentViewerSink? viewerSink,
        IAgentReplayFinalizer replayFinalizer)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(replayFinalizer);
        _options = options;
        _viewerSink = viewerSink;
        _replayFinalizer = replayFinalizer;
        _config = options.CreateRunConfig();
        _run = SnakeRun.Create(options.GameplaySeed, _config);
        _recorder = new RunReplayRecorder(_run);
        if (options.RivalPersonalityId is { } rivalId)
        {
            _rivalRun = SnakeRun.Create(options.GameplaySeed, _config);
            _rivalController = new AiPersonalityController(
                AiPersonalityCatalog.GetBuiltIn(rivalId),
                RivalControllerSeed(options.GameplaySeed, rivalId));
            _rivalRecorder = new RunReplayRecorder(_rivalRun);
            _rivalMetrics = new AgentEpisodeMetricsTracker();
        }
        Lifecycle = AgentMatchLifecycle.AwaitingAction;
        PublishViewerFrame(CreateObservation());
    }

    public AgentMatchLifecycle Lifecycle { get; private set; }

    public AgentObservationV1 Observe()
    {
        lock (_sync)
        {
            return CreateObservation();
        }
    }

    public AgentActionResponse SubmitAction(AgentActionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (_sync)
        {
            if (_processedMutations.TryGetValue(request.IdempotencyKey, out var processed))
            {
                return processed.Kind == AgentMutationKind.Step
                    && processed.Request.Equals(request)
                    ? (AgentActionResponse)processed.Response
                    : Reject(
                        request.Action,
                        AgentActionRejection.IdempotencyConflict,
                        request.DeclaredIntent);
            }

            if (_processedMutations.Count >= MaximumUniqueMutations)
            {
                return Reject(
                    request.Action,
                    AgentActionRejection.MutationCapacityExceeded,
                    request.DeclaredIntent);
            }

            if (_options.ActionProfile != AgentPassportV1.FourDirectionActionProfile)
            {
                return Remember(
                    request.IdempotencyKey,
                    AgentMutationKind.Step,
                    request,
                    Reject(
                        request.Action,
                        AgentActionRejection.WrongActionProfile,
                        request.DeclaredIntent));
            }

            if (Lifecycle != AgentMatchLifecycle.AwaitingAction)
            {
                return Remember(
                    request.IdempotencyKey,
                    AgentMutationKind.Step,
                    request,
                    Reject(
                        request.Action,
                        AgentActionRejection.MatchNotAwaitingAction,
                        request.DeclaredIntent));
            }

            var snapshot = _run.GetSnapshot();
            if (request.ExpectedTick != snapshot.Tick)
            {
                return Remember(
                    request.IdempotencyKey,
                    AgentMutationKind.Step,
                    request,
                    Reject(
                        request.Action,
                        AgentActionRejection.StaleTick,
                        request.DeclaredIntent));
            }

            if (!string.Equals(
                request.ExpectedStateHash,
                snapshot.StateHash,
                StringComparison.Ordinal))
            {
                return Remember(
                    request.IdempotencyKey,
                    AgentMutationKind.Step,
                    request,
                    Reject(
                        request.Action,
                        AgentActionRejection.StaleStateHash,
                        request.DeclaredIntent));
            }

            var response = ExecuteSingleStep(
                request.Action,
                request.DeclaredIntent,
                publishViewer: true);
            return Remember(
                request.IdempotencyKey,
                AgentMutationKind.Step,
                request,
                response);
        }
    }

    public AgentBurstResponse SubmitBurst(AgentBurstRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (_sync)
        {
            if (_processedMutations.TryGetValue(request.IdempotencyKey, out var processed))
            {
                return processed.Kind == AgentMutationKind.Burst
                    && processed.Request.Equals(request)
                    ? (AgentBurstResponse)processed.Response
                    : RejectBurst(
                        request.InitialAction,
                        AgentActionRejection.IdempotencyConflict,
                        request.DeclaredIntent);
            }

            if (_processedMutations.Count >= MaximumUniqueMutations)
            {
                return RejectBurst(
                    request.InitialAction,
                    AgentActionRejection.MutationCapacityExceeded,
                    request.DeclaredIntent);
            }

            if (_options.ActionProfile != AgentPassportV1.FourDirectionBurstActionProfile)
            {
                return Remember(
                    request.IdempotencyKey,
                    AgentMutationKind.Burst,
                    request,
                    RejectBurst(
                        request.InitialAction,
                        AgentActionRejection.WrongActionProfile,
                        request.DeclaredIntent));
            }

            if (Lifecycle != AgentMatchLifecycle.AwaitingAction)
            {
                return Remember(
                    request.IdempotencyKey,
                    AgentMutationKind.Burst,
                    request,
                    RejectBurst(
                        request.InitialAction,
                        AgentActionRejection.MatchNotAwaitingAction,
                        request.DeclaredIntent));
            }

            var snapshot = _run.GetSnapshot();
            if (request.ExpectedTick != snapshot.Tick)
            {
                return Remember(
                    request.IdempotencyKey,
                    AgentMutationKind.Burst,
                    request,
                    RejectBurst(
                        request.InitialAction,
                        AgentActionRejection.StaleTick,
                        request.DeclaredIntent));
            }

            if (!string.Equals(
                request.ExpectedStateHash,
                snapshot.StateHash,
                StringComparison.Ordinal))
            {
                return Remember(
                    request.IdempotencyKey,
                    AgentMutationKind.Burst,
                    request,
                    RejectBurst(
                        request.InitialAction,
                        AgentActionRejection.StaleStateHash,
                        request.DeclaredIntent));
            }

            var stepsAdvanced = 0;
            AgentBurstStopReason stopReason = AgentBurstStopReason.RequestedLimit;
            RunEventKind? stopEvent = null;
            AgentActionResponse lastStep = null!;
            for (var index = 0; index < request.MaximumSteps; index++)
            {
                lastStep = ExecuteSingleStep(
                    index == 0 ? request.InitialAction : AgentAction.Continue,
                    request.DeclaredIntent,
                    publishViewer: false);
                if (lastStep.RulesAdvanced)
                {
                    stepsAdvanced++;
                }

                if (!lastStep.Accepted)
                {
                    if (stepsAdvanced == 0)
                    {
                        var rejected = new AgentBurstResponse(
                            Accepted: false,
                            lastStep.RulesAdvanced,
                            lastStep.Rejection,
                            stepsAdvanced,
                            lastStep.Rejection == AgentActionRejection.ReplayFailure
                                ? AgentBurstStopReason.ReplayFailure
                                : null,
                            StopEvent: null,
                            lastStep.Observation,
                            MatchResult: null);
                        PublishViewerFrame(rejected.Observation);
                        return Remember(
                            request.IdempotencyKey,
                            AgentMutationKind.Burst,
                            request,
                            rejected);
                    }

                    stopReason = AgentBurstStopReason.ReplayFailure;
                    break;
                }

                var hasDecisionEvent = AgentBurstPolicy.TryGetStopEvent(
                    _previousEvents,
                    out var decisionEvent);
                if (hasDecisionEvent)
                {
                    stopEvent = decisionEvent;
                }

                if (_matchResult?.EndReason == AgentMatchEndReason.RulesTerminal)
                {
                    stopReason = AgentBurstStopReason.RulesTerminal;
                    break;
                }

                if (_matchResult?.EndReason == AgentMatchEndReason.StepLimit)
                {
                    stopReason = AgentBurstStopReason.MatchStepLimit;
                    break;
                }

                if (hasDecisionEvent)
                {
                    stopReason = AgentBurstStopReason.DecisionEvent;
                    break;
                }
            }

            if (stepsAdvanced > 0 && Lifecycle != AgentMatchLifecycle.FailedClosed)
            {
                _previousAction = new AgentPreviousActionV1(
                    request.InitialAction,
                    Accepted: true,
                    AgentActionRejection.None,
                    RulesAdvanced: true,
                    request.DeclaredIntent);
            }

            var observation = CreateObservation();
            var response = new AgentBurstResponse(
                Accepted: lastStep.Accepted,
                RulesAdvanced: stepsAdvanced > 0,
                lastStep.Rejection,
                stepsAdvanced,
                stopReason,
                stopEvent,
                observation,
                _matchResult);
            PublishViewerFrame(observation);
            return Remember(
                request.IdempotencyKey,
                AgentMutationKind.Burst,
                request,
                response);
        }
    }

    public AgentMatchResult Finish()
    {
        lock (_sync)
        {
            if (_matchResult is not null)
            {
                return _matchResult;
            }

            if (Lifecycle == AgentMatchLifecycle.FailedClosed)
            {
                throw new InvalidOperationException(
                    "A failed-closed agent match has no verified replay result.");
            }

            if (!TryComplete(
                AgentMatchEndReason.AgentFinished,
                AgentMatchLifecycle.Aborted,
                out var result))
            {
                PublishViewerFrame(CreateObservation());
                throw new InvalidOperationException(
                    "Agent match replay finalization failed closed.");
            }
            PublishViewerFrame(CreateObservation());
            return result!;
        }
    }

    public AgentMatchResult? GetResult()
    {
        lock (_sync)
        {
            return _matchResult;
        }
    }

    private AgentActionResponse FailClosedAfterRecorderError(
        AgentAction action,
        AgentPublicIntent declaredIntent,
        bool rulesAdvanced)
    {
        Lifecycle = AgentMatchLifecycle.FailedClosed;
        if (!rulesAdvanced)
        {
            _previousEvents = Array.Empty<RunEventDetail>();
        }
        _previousAction = new AgentPreviousActionV1(
            action,
            Accepted: false,
            AgentActionRejection.ReplayFailure,
            rulesAdvanced,
            declaredIntent);
        var response = new AgentActionResponse(
            Accepted: false,
            rulesAdvanced,
            AgentActionRejection.ReplayFailure,
            CreateObservation(),
            MatchResult: null);
        return response;
    }

    private AgentActionResponse ExecuteSingleStep(
        AgentAction action,
        AgentPublicIntent declaredIntent,
        bool publishViewer)
    {
        var snapshot = _run.GetSnapshot();
        if (!Enum.IsDefined(action))
        {
            return Reject(action, AgentActionRejection.InvalidAction, declaredIntent, publishViewer);
        }

        if (TryMapDirection(action, out var direction))
        {
            var effectiveDirection = snapshot.PendingDirections.Count > 0
                ? snapshot.PendingDirections[^1]
                : snapshot.Direction;
            if (snapshot.PendingDirections.Count >= _config.MaximumDirectionQueue
                || direction == effectiveDirection
                || direction == effectiveDirection.Opposite())
            {
                return Reject(
                    action,
                    AgentActionRejection.IllegalDirection,
                    declaredIntent,
                    publishViewer);
            }

            if (!_recorder.TryRecordCommand(direction) || !_run.QueueDirection(direction))
            {
                var failed = FailClosedAfterRecorderError(
                    action,
                    declaredIntent,
                    rulesAdvanced: false);
                if (publishViewer)
                {
                    PublishViewerFrame(failed.Observation);
                }
                return failed;
            }
        }

        var result = _run.Step();
        _previousEvents = Array.AsReadOnly(result.OrderedEvents.ToArray());
        if (!_recorder.TryCompleteStep(result, _run))
        {
            var failed = FailClosedAfterRecorderError(
                action,
                declaredIntent,
                rulesAdvanced: true);
            if (publishViewer)
            {
                PublishViewerFrame(failed.Observation);
            }
            return failed;
        }

        var steppedSnapshot = _run.GetSnapshot();
        _metrics.Record(result, steppedSnapshot);
        if (!AdvanceRival())
        {
            var failed = FailClosedAfterRecorderError(
                action,
                declaredIntent,
                rulesAdvanced: true);
            if (publishViewer)
            {
                PublishViewerFrame(failed.Observation);
            }
            return failed;
        }

        _previousAction = new AgentPreviousActionV1(
            action,
            Accepted: true,
            AgentActionRejection.None,
            RulesAdvanced: true,
            declaredIntent);

        if (result.Status != RunStatus.Running)
        {
            if (!TryComplete(
                AgentMatchEndReason.RulesTerminal,
                AgentMatchLifecycle.Completed,
                out _))
            {
                var failed = FailClosedAfterRecorderError(
                    action,
                    declaredIntent,
                    rulesAdvanced: true);
                if (publishViewer)
                {
                    PublishViewerFrame(failed.Observation);
                }
                return failed;
            }
        }
        else if (result.Tick >= _options.MaximumSteps)
        {
            if (!TryComplete(
                AgentMatchEndReason.StepLimit,
                AgentMatchLifecycle.Completed,
                out _))
            {
                var failed = FailClosedAfterRecorderError(
                    action,
                    declaredIntent,
                    rulesAdvanced: true);
                if (publishViewer)
                {
                    PublishViewerFrame(failed.Observation);
                }
                return failed;
            }
        }

        var response = new AgentActionResponse(
            Accepted: true,
            RulesAdvanced: true,
            AgentActionRejection.None,
            CreateObservation(),
            _matchResult);
        if (publishViewer)
        {
            PublishViewerFrame(response.Observation);
        }
        return response;
    }

    private bool TryComplete(
        AgentMatchEndReason endReason,
        AgentMatchLifecycle lifecycle,
        out AgentMatchResult? result)
    {
        result = null;
        var agent = _replayFinalizer.Finalize(
            AgentReplayLane.Agent,
            _recorder,
            _run);
        if (agent.Failure != AgentReplayFinalizationFailure.None
            || agent.Replay is null
            || agent.Verification is null)
        {
            Lifecycle = AgentMatchLifecycle.FailedClosed;
            return false;
        }

        RunReplay? rivalReplay = null;
        ReplayVerificationResult? rivalVerification = null;
        if (_rivalRecorder is not null && _rivalRun is not null)
        {
            var rival = _replayFinalizer.Finalize(
                AgentReplayLane.Rival,
                _rivalRecorder,
                _rivalRun);
            if (rival.Failure != AgentReplayFinalizationFailure.None
                || rival.Replay is null
                || rival.Verification is null)
            {
                Lifecycle = AgentMatchLifecycle.FailedClosed;
                return false;
            }

            rivalReplay = rival.Replay;
            rivalVerification = rival.Verification;
        }

        Lifecycle = lifecycle;
        var snapshot = _run.GetSnapshot();
        var episodeMetrics = _metrics.Snapshot(snapshot.Tick);
        var rivalResult = CreateRivalResult(rivalReplay, rivalVerification);
        _matchResult = new AgentMatchResult(
            AgentMatchResult.Contract,
            _options.MatchId,
            Lifecycle,
            endReason,
            RulesetIdentity.CurrentId,
            RulesetIdentity.CurrentVersion,
            _options.ModeId,
            _options.ModeVersion,
            RunConfig.ConfigHashAlgorithmId,
            _config.ComputeConfigHash(),
            _options.SeedVisibility,
            _options.GameplaySeed,
            _options.Passport,
            snapshot.Tick,
            snapshot.Status,
            snapshot.DeathCause,
            snapshot.Score,
            snapshot.StateHash,
            agent.Replay.PayloadHash,
            agent.Verification.Code,
            episodeMetrics,
            _options.StyleContractId is null
                ? null
                : AgentStyleContractCatalog.Evaluate(
                    _options.StyleContractId,
                    _options.ModeId,
                    episodeMetrics),
            rivalResult,
            agent.Replay,
            rivalReplay);
        result = _matchResult;
        return true;
    }

    private AgentActionResponse Reject(
        AgentAction action,
        AgentActionRejection rejection,
        AgentPublicIntent declaredIntent = AgentPublicIntent.Undeclared,
        bool publishViewer = true)
    {
        _previousEvents = Array.Empty<RunEventDetail>();
        _previousAction = new AgentPreviousActionV1(
            action,
            Accepted: false,
            rejection,
            RulesAdvanced: false,
            declaredIntent);
        var response = new AgentActionResponse(
            Accepted: false,
            RulesAdvanced: false,
            rejection,
            CreateObservation(),
            _matchResult);
        if (publishViewer)
        {
            PublishViewerFrame(response.Observation);
        }
        return response;
    }

    private AgentBurstResponse RejectBurst(
        AgentAction action,
        AgentActionRejection rejection,
        AgentPublicIntent declaredIntent)
    {
        var rejected = Reject(action, rejection, declaredIntent);
        return new AgentBurstResponse(
            Accepted: false,
            rejected.RulesAdvanced,
            rejected.Rejection,
            StepsAdvanced: 0,
            StopReason: null,
            StopEvent: null,
            rejected.Observation,
            MatchResult: null);
    }

    private TResponse Remember<TResponse>(
        string idempotencyKey,
        AgentMutationKind kind,
        object request,
        TResponse response)
        where TResponse : class
    {
        _processedMutations.Add(
            idempotencyKey,
            new ProcessedMutation(kind, request, response));
        return response;
    }

    private AgentObservationV1 CreateObservation() =>
        AgentObservationProjector.Project(
            _options,
            _config,
            _run.GetSnapshot(),
            _previousEvents,
            _previousAction,
            Lifecycle,
            _metrics.Snapshot(_run.GetSnapshot().Tick),
            CreateRivalObservation());

    private bool AdvanceRival()
    {
        if (_rivalRun is null || _rivalController is null || _rivalRecorder is null)
        {
            return true;
        }

        if (_rivalRun.Status != RunStatus.Running)
        {
            return true;
        }

        var decision = _rivalController.SelectDecision(_rivalRun);
        if (!_rivalRecorder.TryRecordCommand(decision.Direction))
        {
            return false;
        }

        _ = _rivalRun.QueueDirection(decision.Direction);
        var result = _rivalRun.Step();
        if (!_rivalRecorder.TryCompleteStep(result, _rivalRun))
        {
            return false;
        }

        _rivalMetrics!.Record(result, _rivalRun.GetSnapshot());
        return true;
    }

    private AgentRivalObservationV1? CreateRivalObservation()
    {
        if (_rivalRun is null || _options.RivalPersonalityId is null)
        {
            return null;
        }

        var personality = AiPersonalityCatalog.GetBuiltIn(_options.RivalPersonalityId);
        var snapshot = _rivalRun.GetSnapshot();
        return new AgentRivalObservationV1(
            personality.Id,
            personality.Name,
            snapshot.Tick,
            snapshot.Status,
            snapshot.DeathCause,
            snapshot.Score);
    }

    private AgentRivalResultV1? CreateRivalResult(
        RunReplay? replay,
        ReplayVerificationResult? verification)
    {
        if (_rivalRun is null
            || _rivalMetrics is null
            || _options.RivalPersonalityId is null
            || replay is null
            || verification is null)
        {
            return null;
        }

        var personality = AiPersonalityCatalog.GetBuiltIn(_options.RivalPersonalityId);
        var snapshot = _rivalRun.GetSnapshot();
        return new AgentRivalResultV1(
            personality.Id,
            personality.Name,
            snapshot.Tick,
            snapshot.Status,
            snapshot.DeathCause,
            snapshot.Score,
            snapshot.StateHash,
            replay.PayloadHash,
            verification.Code,
            _rivalMetrics.Snapshot(snapshot.Tick));
    }

    private static ulong RivalControllerSeed(ulong gameplaySeed, string personalityId)
    {
        var personalityIndex = AiPersonalityCatalog.BuiltIn
            .Select((item, index) => new { item.Id, Index = index + 1 })
            .Single(item => string.Equals(item.Id, personalityId, StringComparison.Ordinal))
            .Index;
        return unchecked(gameplaySeed ^ (0x9E3779B97F4A7C15UL * (ulong)personalityIndex));
    }

    private void PublishViewerFrame(AgentObservationV1 observation)
    {
        if (_viewerSink is null)
        {
            return;
        }

        try
        {
            _ = _viewerSink.TryPublish(new AgentViewerFrameV2(
                AgentViewerFrameV2.Contract,
                _viewerSequence++,
                observation,
                _matchResult?.EndReason
                    ?? (Lifecycle == AgentMatchLifecycle.FailedClosed
                        ? AgentMatchEndReason.ReplayFailure
                        : AgentMatchEndReason.None),
                _matchResult?.ReplayVerificationCode == ReplayVerificationCode.Verified));
        }
        catch (Exception)
        {
            // The viewer is presentation-only. Its failure never changes rules.
        }
    }

    private static bool TryMapDirection(AgentAction action, out Direction direction)
    {
        switch (action)
        {
            case AgentAction.Up:
                direction = Direction.Up;
                return true;
            case AgentAction.Right:
                direction = Direction.Right;
                return true;
            case AgentAction.Down:
                direction = Direction.Down;
                return true;
            case AgentAction.Left:
                direction = Direction.Left;
                return true;
            case AgentAction.Continue:
            default:
                direction = default;
                return false;
        }
    }

    private enum AgentMutationKind : byte
    {
        Step = 0,
        Burst = 1,
    }

    private sealed record ProcessedMutation(
        AgentMutationKind Kind,
        object Request,
        object Response);
}
