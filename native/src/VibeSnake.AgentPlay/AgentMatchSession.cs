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
    private readonly AgentStyleEvidenceTracker? _styleEvidence;
    private readonly AgentLessonEvidenceTracker? _lessonEvidence;
    private readonly SnakeRun? _rivalRun;
    private readonly AiPersonalityController? _rivalController;
    private readonly RunReplayRecorder? _rivalRecorder;
    private readonly AgentEpisodeMetricsTracker? _rivalMetrics;
    private readonly IAgentViewerSink? _viewerSink;
    private readonly IAgentReplayFinalizer _replayFinalizer;
    private readonly Dictionary<string, ProcessedMutation> _processedMutations =
        new(StringComparer.Ordinal);
    // Ordered public presentation facts for accepted rules steps. They feed the
    // AA-06 exhibition receipt and never influence rules, score, or verification.
    private readonly List<AgentAcceptedPresentationEventV1> _acceptedPresentationEvents = [];
    private IReadOnlyList<RunEventDetail> _previousEvents =
        Array.Empty<RunEventDetail>();
    private AgentPreviousActionV1? _previousAction;
    private AgentMatchResultV5? _matchResult;
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
        _styleEvidence = options.StyleContractId is null
            ? null
            : new AgentStyleEvidenceTracker(
                options.StyleContractId,
                options.ModeId,
                _config,
                _run.GetSnapshot());
        _lessonEvidence = options.LessonId is null
            ? null
            : new AgentLessonEvidenceTracker(options.LessonId, _config);
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
        var initialObservation = CreateObservation();
        PublishViewerFrame(
            initialObservation,
            AgentViewerOperationKind.Initial,
            initialObservation.Tick,
            initialObservation.StateHash,
            stepsAdvanced: 0);
    }

    public AgentMatchLifecycle Lifecycle { get; private set; }

    public AgentObservationV5 Observe()
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

            if (_options.ActionProfile != AgentPassportV4.FourDirectionActionProfile)
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

            var lessonBefore = CreateLessonProgress();
            var response = ExecuteSingleStep(
                request.Action,
                request.DeclaredIntent,
                publishViewer: true,
                request.IdempotencyKey,
                AgentLessonAttemptOperation.Step);
            response = response with
            {
                LessonDelta = CreateLessonDelta(
                    lessonBefore,
                    response.Observation.LessonProgress),
            };
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

            if (_options.ActionProfile != AgentPassportV4.FourDirectionBurstActionProfile)
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

            var lessonBefore = CreateLessonProgress();
            var stepsAdvanced = 0;
            AgentBurstStopReason stopReason = AgentBurstStopReason.RequestedLimit;
            RunEventKind? stopEvent = null;
            AgentActionResponse lastStep = null!;
            for (var index = 0; index < request.MaximumSteps; index++)
            {
                lastStep = ExecuteSingleStep(
                    index == 0 ? request.InitialAction : AgentAction.Continue,
                    request.DeclaredIntent,
                    publishViewer: false,
                    index == 0 ? request.IdempotencyKey : null,
                    index == 0 ? AgentLessonAttemptOperation.Burst : null);
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
                            CreateLessonDelta(
                                lessonBefore,
                                lastStep.Observation.LessonProgress),
                            lastStep.Observation,
                            MatchResult: null);
                        PublishViewerFrame(
                            rejected.Observation,
                            AgentViewerOperationKind.Burst,
                            snapshot.Tick,
                            snapshot.StateHash,
                            rejected.StepsAdvanced,
                            rejected.StopReason,
                            rejected.StopEvent);
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

                var lessonAfterStep = lastStep.Observation.LessonProgress;
                if (lessonBefore is { AllRequirementsSatisfied: false }
                    && lessonAfterStep is { AllRequirementsSatisfied: true })
                {
                    stopReason = AgentBurstStopReason.LessonRequirementsReached;
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
                CreateLessonDelta(lessonBefore, observation.LessonProgress),
                observation,
                _matchResult);
            PublishViewerFrame(
                observation,
                AgentViewerOperationKind.Burst,
                snapshot.Tick,
                snapshot.StateHash,
                response.StepsAdvanced,
                response.StopReason,
                response.StopEvent);
            return Remember(
                request.IdempotencyKey,
                AgentMutationKind.Burst,
                request,
                response);
        }
    }

    public AgentMatchResultV5 Finish()
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

            var start = _run.GetSnapshot();
            var lifecycle = CreateLessonProgress()?.AllRequirementsSatisfied == true
                ? AgentMatchLifecycle.Completed
                : AgentMatchLifecycle.Aborted;

            if (!TryComplete(
                AgentMatchEndReason.AgentFinished,
                lifecycle,
                out var result))
            {
                PublishViewerFrame(
                    CreateObservation(),
                    AgentViewerOperationKind.Finish,
                    start.Tick,
                    start.StateHash,
                    stepsAdvanced: 0);
                throw new InvalidOperationException(
                    "Agent match replay finalization failed closed.");
            }
            PublishViewerFrame(
                CreateObservation(),
                AgentViewerOperationKind.Finish,
                start.Tick,
                start.StateHash,
                stepsAdvanced: 0);
            return result!;
        }
    }

    public AgentMatchResultV5? GetResult()
    {
        lock (_sync)
        {
            return _matchResult;
        }
    }

    /// <summary>
    /// Returns the canonical exhibition receipt for a successfully finalized,
    /// verified match. A live, aborted-without-verification, or failed-closed match
    /// has no exhibition identity and returns null.
    /// </summary>
    public AgentExhibitionReceiptV2? TryCreateExhibitionReceipt()
    {
        lock (_sync)
        {
            return _matchResult is null
                ? null
                : AgentExhibitionReceipt.TryCreate(
                    _matchResult,
                    Array.AsReadOnly(_acceptedPresentationEvents.ToArray()));
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
            LessonDelta: null,
            CreateObservation(),
            MatchResult: null);
        return response;
    }

    private AgentActionResponse ExecuteSingleStep(
        AgentAction action,
        AgentPublicIntent declaredIntent,
        bool publishViewer,
        string? idempotencyKey,
        AgentLessonAttemptOperation? lessonOperation)
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
                if (direction == effectiveDirection.Opposite()
                    && idempotencyKey is not null
                    && lessonOperation is { } operation)
                {
                    _ = _lessonEvidence?.TryRecordOppositeReversal(
                        operation,
                        idempotencyKey,
                        snapshot,
                        action);
                }
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
                    PublishViewerFrame(
                        failed.Observation,
                        AgentViewerOperationKind.Step,
                        snapshot.Tick,
                        snapshot.StateHash,
                        failed.RulesAdvanced ? 1 : 0);
                }
                return failed;
            }
        }

        var result = _run.Step();
        _previousEvents = Array.AsReadOnly(result.OrderedEvents.ToArray());
        var steppedSnapshot = _run.GetSnapshot();
        _metrics.Record(result, steppedSnapshot);
        try
        {
            _styleEvidence?.Record(snapshot, result, steppedSnapshot);
            _lessonEvidence?.RecordStep(snapshot, result, steppedSnapshot);
        }
        catch (Exception exception) when (
            exception is ArgumentException
                or InvalidOperationException
                or OverflowException)
        {
            var failed = FailClosedAfterRecorderError(
                action,
                declaredIntent,
                rulesAdvanced: true);
            if (publishViewer)
            {
                PublishViewerFrame(
                    failed.Observation,
                    AgentViewerOperationKind.Step,
                    snapshot.Tick,
                    snapshot.StateHash,
                    failed.RulesAdvanced ? 1 : 0);
            }
            return failed;
        }

        if (!_recorder.TryCompleteStep(result, _run))
        {
            var failed = FailClosedAfterRecorderError(
                action,
                declaredIntent,
                rulesAdvanced: true);
            if (publishViewer)
            {
                PublishViewerFrame(
                    failed.Observation,
                    AgentViewerOperationKind.Step,
                    snapshot.Tick,
                    snapshot.StateHash,
                    failed.RulesAdvanced ? 1 : 0);
            }
            return failed;
        }

        if (!AdvanceRival())
        {
            var failed = FailClosedAfterRecorderError(
                action,
                declaredIntent,
                rulesAdvanced: true);
            if (publishViewer)
            {
                PublishViewerFrame(
                    failed.Observation,
                    AgentViewerOperationKind.Step,
                    snapshot.Tick,
                    snapshot.StateHash,
                    failed.RulesAdvanced ? 1 : 0);
            }
            return failed;
        }

        _previousAction = new AgentPreviousActionV1(
            action,
            Accepted: true,
            AgentActionRejection.None,
            RulesAdvanced: true,
            declaredIntent);
        _acceptedPresentationEvents.Add(new AgentAcceptedPresentationEventV1(
            _acceptedPresentationEvents.Count + 1,
            steppedSnapshot.Tick,
            action,
            declaredIntent));

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
                    PublishViewerFrame(
                        failed.Observation,
                        AgentViewerOperationKind.Step,
                        snapshot.Tick,
                        snapshot.StateHash,
                        failed.RulesAdvanced ? 1 : 0);
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
                    PublishViewerFrame(
                        failed.Observation,
                        AgentViewerOperationKind.Step,
                        snapshot.Tick,
                        snapshot.StateHash,
                        failed.RulesAdvanced ? 1 : 0);
                }
                return failed;
            }
        }

        var response = new AgentActionResponse(
            Accepted: true,
            RulesAdvanced: true,
            AgentActionRejection.None,
            LessonDelta: null,
            CreateObservation(),
            _matchResult);
        if (publishViewer)
        {
            PublishViewerFrame(
                response.Observation,
                AgentViewerOperationKind.Step,
                snapshot.Tick,
                snapshot.StateHash,
                stepsAdvanced: 1);
        }
        return response;
    }

    private bool TryComplete(
        AgentMatchEndReason endReason,
        AgentMatchLifecycle lifecycle,
        out AgentMatchResultV5? result)
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

        var snapshot = _run.GetSnapshot();
        var episodeMetrics = _metrics.Snapshot(snapshot.Tick);
        AgentEpisodeMetricsV1 replayMetrics;
        AgentStyleOutcomeV3? styleOutcome = null;
        AgentLessonOutcomeV3? lessonOutcome = null;
        try
        {
            replayMetrics = AgentEpisodeMetricsReplayEvaluator.Evaluate(agent.Replay);
            if (_options.StyleContractId is { } styleContractId)
            {
                if (_styleEvidence is null)
                {
                    throw new InvalidOperationException(
                        "The selected style contract had no live evidence tracker.");
                }

                var replayStyleEvidence = AgentStyleEvidenceReplayEvaluator.Evaluate(
                    styleContractId,
                    _options.ModeId,
                    agent.Replay);
                if (_styleEvidence.Facts != replayStyleEvidence.Facts
                    || !AgentStyleEvidenceReplayEvaluator.Equivalent(
                    _styleEvidence.Snapshot(),
                    replayStyleEvidence.Progress))
                {
                    throw new InvalidOperationException(
                        "Live style evidence diverged from the verified replay.");
                }

                styleOutcome = AgentStyleEvidenceReplayEvaluator.CreateOutcome(
                    replayStyleEvidence.Progress,
                    agent.Replay.PayloadHash);
            }

            if (_options.LessonId is { } lessonId)
            {
                if (_lessonEvidence is null)
                {
                    throw new InvalidOperationException(
                        "The selected lesson had no live evidence tracker.");
                }

                var replayLessonProgress = AgentLessonEvidenceReplayEvaluator.Evaluate(
                    lessonId,
                    _options.ActionProfile,
                    agent.Replay,
                    _lessonEvidence.AttemptWitnesses);
                var liveLessonProgress = _lessonEvidence.Snapshot(
                    AgentLessonEvidenceState.Live,
                    _options.ActionProfile);
                if (!AgentLessonEvidenceReplayEvaluator.Equivalent(
                    liveLessonProgress,
                    replayLessonProgress))
                {
                    throw new InvalidOperationException(
                        "Live lesson evidence diverged from the verified replay and attempt witnesses.");
                }

                lessonOutcome = AgentLessonEvidenceReplayEvaluator.CreateOutcome(
                    replayLessonProgress,
                    endReason,
                    agent.Replay.PayloadHash);
            }
        }
        catch (Exception exception) when (
            exception is ArgumentException
                or InvalidOperationException
                or OverflowException)
        {
            Lifecycle = AgentMatchLifecycle.FailedClosed;
            return false;
        }
        if (episodeMetrics != replayMetrics)
        {
            Lifecycle = AgentMatchLifecycle.FailedClosed;
            return false;
        }

        Lifecycle = lifecycle;
        var rivalResult = CreateRivalResult(rivalReplay, rivalVerification);
        _matchResult = new AgentMatchResultV5(
            AgentMatchResultV5.Contract,
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
            replayMetrics,
            styleOutcome,
            lessonOutcome,
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
            CreateLessonDelta(CreateLessonProgress(), CreateLessonProgress()),
            CreateObservation(),
            _matchResult);
        if (publishViewer)
        {
            PublishViewerFrame(
                response.Observation,
                AgentViewerOperationKind.Step,
                response.Observation.Tick,
                response.Observation.StateHash,
                stepsAdvanced: 0);
        }
        return response;
    }

    private AgentBurstResponse RejectBurst(
        AgentAction action,
        AgentActionRejection rejection,
        AgentPublicIntent declaredIntent)
    {
        var rejected = Reject(action, rejection, declaredIntent, publishViewer: false);
        var response = new AgentBurstResponse(
            Accepted: false,
            rejected.RulesAdvanced,
            rejected.Rejection,
            StepsAdvanced: 0,
            StopReason: null,
            StopEvent: null,
            rejected.LessonDelta,
            rejected.Observation,
            MatchResult: null);
        PublishViewerFrame(
            response.Observation,
            AgentViewerOperationKind.Burst,
            response.Observation.Tick,
            response.Observation.StateHash,
            stepsAdvanced: 0);
        return response;
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

    private AgentObservationV5 CreateObservation() =>
        AgentObservationProjector.Project(
            _options,
            _config,
            _run.GetSnapshot(),
            _previousEvents,
            _previousAction,
            Lifecycle,
            _metrics.Snapshot(_run.GetSnapshot().Tick),
            CreateRivalObservation(),
            _styleEvidence?.Snapshot(),
            CreateLessonProgress());

    // Observed danger and recovery facts for the frame being published. They are
    // derived from the authoritative run snapshot, while a viewer independently
    // recomputes the same values from the public observation it also received,
    // so a disagreeing frame is rejected instead of presented.
    private AgentSurvivalStateV1 CreateSurvivalState()
    {
        var snapshot = _run.GetSnapshot();
        return AgentSurvivalStateV1.Create(
            snapshot.Status == RunStatus.Running,
            AgentStyleEvidenceMath.StructuralOpenExitCount(_config, snapshot),
            snapshot.ShieldTicksRemaining,
            snapshot.PhaseShiftTicksRemaining,
            snapshot.LastStandHeld,
            snapshot.LastStandRecoveryTicksRemaining,
            snapshot.SlowMoTicksRemaining);
    }

    private AgentLessonProgressV3? CreateLessonProgress()
    {
        if (_options.LessonId is null)
        {
            return null;
        }

        var evidenceState = Lifecycle == AgentMatchLifecycle.FailedClosed
            ? AgentLessonEvidenceState.FailedClosed
            : _matchResult is null
                ? AgentLessonEvidenceState.Live
                : AgentLessonEvidenceState.Verified;
        return _lessonEvidence?.Snapshot(evidenceState, _options.ActionProfile);
    }

    private static AgentLessonProgressDeltaV2? CreateLessonDelta(
        AgentLessonProgressV3? previous,
        AgentLessonProgressV3? current) =>
        previous is null || current is null
            ? null
            : AgentSignalSchoolCatalog.Delta(previous, current);

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

    private void PublishViewerFrame(
        AgentObservationV5 observation,
        AgentViewerOperationKind operation,
        int startTick,
        string startStateHash,
        int stepsAdvanced,
        AgentBurstStopReason? burstStopReason = null,
        RunEventKind? burstStopEvent = null)
    {
        if (_viewerSink is null)
        {
            return;
        }

        try
        {
            var verifiedResultAvailable =
                _matchResult?.ReplayVerificationCode == ReplayVerificationCode.Verified;
            _ = _viewerSink.TryPublish(new AgentViewerFrameV9(
                AgentViewerFrameV9.Contract,
                _viewerSequence++,
                operation,
                startTick,
                startStateHash,
                stepsAdvanced,
                burstStopReason,
                burstStopEvent,
                observation,
                CreateSurvivalState(),
                _matchResult?.EndReason
                    ?? (Lifecycle == AgentMatchLifecycle.FailedClosed
                        ? AgentMatchEndReason.ReplayFailure
                        : AgentMatchEndReason.None),
                verifiedResultAvailable,
                verifiedResultAvailable ? _matchResult?.ReplayPayloadHash : null,
                verifiedResultAvailable ? _matchResult?.StyleOutcome : null,
                verifiedResultAvailable ? _matchResult?.LessonOutcome : null));
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
