using VibeSnake.Rules;

namespace VibeSnake.AgentPlay;

public sealed class AgentMatchSession
{
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
    private readonly Dictionary<string, ProcessedAction> _processedActions =
        new(StringComparer.Ordinal);
    private IReadOnlyList<RunEventDetail> _previousEvents =
        Array.Empty<RunEventDetail>();
    private AgentPreviousActionV1? _previousAction;
    private AgentMatchResult? _matchResult;
    private long _viewerSequence;

    public AgentMatchSession(
        AgentMatchOptions options,
        IAgentViewerSink? viewerSink = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
        _viewerSink = viewerSink;
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
            if (_processedActions.TryGetValue(request.IdempotencyKey, out var processed))
            {
                return processed.Request == request
                    ? processed.Response
                    : Reject(
                        request.Action,
                        AgentActionRejection.IdempotencyConflict,
                        request.DeclaredIntent);
            }

            if (Lifecycle != AgentMatchLifecycle.AwaitingAction)
            {
                return Remember(
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
                    request,
                    Reject(
                        request.Action,
                        AgentActionRejection.StaleStateHash,
                        request.DeclaredIntent));
            }

            if (!Enum.IsDefined(request.Action))
            {
                return Remember(
                    request,
                    Reject(
                        request.Action,
                        AgentActionRejection.InvalidAction,
                        request.DeclaredIntent));
            }

            if (TryMapDirection(request.Action, out var direction))
            {
                var effectiveDirection = snapshot.PendingDirections.Count > 0
                    ? snapshot.PendingDirections[^1]
                    : snapshot.Direction;
                if (snapshot.PendingDirections.Count >= _config.MaximumDirectionQueue
                    || direction == effectiveDirection
                    || direction == effectiveDirection.Opposite())
                {
                    return Remember(
                        request,
                        Reject(
                            request.Action,
                            AgentActionRejection.IllegalDirection,
                            request.DeclaredIntent));
                }

                if (!_recorder.TryRecordCommand(direction) || !_run.QueueDirection(direction))
                {
                    return Remember(
                        request,
                        FailClosedAfterRecorderError(
                            request.Action,
                            request.DeclaredIntent,
                            rulesAdvanced: false));
                }
            }

            var result = _run.Step();
            _previousEvents = Array.AsReadOnly(result.OrderedEvents.ToArray());
            if (!_recorder.TryCompleteStep(result, _run))
            {
                return Remember(
                    request,
                    FailClosedAfterRecorderError(
                        request.Action,
                        request.DeclaredIntent,
                        rulesAdvanced: true));
            }

            var steppedSnapshot = _run.GetSnapshot();
            _metrics.Record(result, steppedSnapshot);
            if (!AdvanceRival())
            {
                return Remember(
                    request,
                    FailClosedAfterRecorderError(
                        request.Action,
                        request.DeclaredIntent,
                        rulesAdvanced: true));
            }

            _previousAction = new AgentPreviousActionV1(
                request.Action,
                Accepted: true,
                AgentActionRejection.None,
                RulesAdvanced: true,
                request.DeclaredIntent);

            if (result.Status != RunStatus.Running)
            {
                Complete(AgentMatchEndReason.RulesTerminal, AgentMatchLifecycle.Completed);
            }
            else if (result.Tick >= _options.MaximumSteps)
            {
                Complete(AgentMatchEndReason.StepLimit, AgentMatchLifecycle.Completed);
            }

            var response = new AgentActionResponse(
                Accepted: true,
                RulesAdvanced: true,
                AgentActionRejection.None,
                CreateObservation(),
                _matchResult);
            PublishViewerFrame(response.Observation);
            return Remember(request, response);
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

            var result = Complete(
                AgentMatchEndReason.AgentFinished,
                AgentMatchLifecycle.Aborted);
            PublishViewerFrame(CreateObservation());
            return result;
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
        PublishViewerFrame(response.Observation);
        return response;
    }

    private AgentMatchResult Complete(
        AgentMatchEndReason endReason,
        AgentMatchLifecycle lifecycle)
    {
        var recording = _recorder.Finish(_run);
        if (!recording.IsSuccessful || recording.Replay is null)
        {
            Lifecycle = AgentMatchLifecycle.FailedClosed;
            throw new InvalidOperationException(
                "Agent match replay finalization failed closed.");
        }

        var verification = recording.Replay.Verify();
        if (!verification.IsValid)
        {
            Lifecycle = AgentMatchLifecycle.FailedClosed;
            throw new InvalidOperationException(
                "Agent match replay verification failed closed.");
        }

        RunReplay? rivalReplay = null;
        ReplayVerificationResult? rivalVerification = null;
        if (_rivalRecorder is not null && _rivalRun is not null)
        {
            var rivalRecording = _rivalRecorder.Finish(_rivalRun);
            if (!rivalRecording.IsSuccessful || rivalRecording.Replay is null)
            {
                Lifecycle = AgentMatchLifecycle.FailedClosed;
                throw new InvalidOperationException(
                    "Agent rival replay finalization failed closed.");
            }

            rivalVerification = rivalRecording.Replay.Verify();
            if (!rivalVerification.IsValid)
            {
                Lifecycle = AgentMatchLifecycle.FailedClosed;
                throw new InvalidOperationException(
                    "Agent rival replay verification failed closed.");
            }

            rivalReplay = rivalRecording.Replay;
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
            recording.Replay.PayloadHash,
            verification.Code,
            episodeMetrics,
            _options.StyleContractId is null
                ? null
                : AgentStyleContractCatalog.Evaluate(
                    _options.StyleContractId,
                    _options.ModeId,
                    episodeMetrics),
            rivalResult,
            recording.Replay,
            rivalReplay);
        return _matchResult;
    }

    private AgentActionResponse Reject(
        AgentAction action,
        AgentActionRejection rejection,
        AgentPublicIntent declaredIntent = AgentPublicIntent.Undeclared)
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
        PublishViewerFrame(response.Observation);
        return response;
    }

    private AgentActionResponse Remember(
        AgentActionRequest request,
        AgentActionResponse response)
    {
        _processedActions.Add(
            request.IdempotencyKey,
            new ProcessedAction(request, response));
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

    private sealed record ProcessedAction(
        AgentActionRequest Request,
        AgentActionResponse Response);
}
