using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using VibeSnake.AgentPlay;
using VibeSnake.Rules;

namespace VibeSnake.AgentViewer;

public enum AgentViewerClientState : byte
{
    Connecting = 0,
    Watching = 1,
    Completed = 2,
    Disconnected = 3,
    Rejected = 4,
    FailedClosed = 5,
}

public sealed class AgentViewerClient : IDisposable
{
    public const int MaximumFrameBytes = 262_144;
    public static readonly TimeSpan ConnectionTimeout = TimeSpan.FromSeconds(10);

    private static readonly JsonSerializerOptions SerializerOptions = CreateSerializerOptions();
    private readonly object _sync = new();
    private readonly string _pipeName;
    private readonly string _accessToken;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Task _readerTask;
    private AgentViewerFrameV7? _latestFrame;
    private long _lastPresentedSequence = -1;
    private AgentViewerClientState _state = AgentViewerClientState.Connecting;
    private string _status = "CONNECTING TO AGENT MATCH";
    private bool _disposed;

    public AgentViewerClient(string pipeName, string accessToken)
    {
        if (!AgentViewerTransport.IsValidPipeName(pipeName))
        {
            throw new ArgumentException(
                $"The viewer pipe name must be an ASCII token no longer than {AgentViewerTransport.MaximumPipeNameLength} characters.",
                nameof(pipeName));
        }
        ValidateAccessToken(accessToken);
        _pipeName = pipeName;
        _accessToken = accessToken;
        _readerTask = ReadFramesAsync(_shutdown.Token);
    }

    public AgentViewerClientState State
    {
        get
        {
            lock (_sync)
            {
                return _state;
            }
        }
    }

    public string Status
    {
        get
        {
            lock (_sync)
            {
                return _status;
            }
        }
    }

    public bool TryTakeLatest(
        out AgentViewerFrameV7? frame,
        out long coalescedFrames)
    {
        lock (_sync)
        {
            frame = _latestFrame;
            _latestFrame = null;
            if (frame is null)
            {
                coalescedFrames = 0;
                return false;
            }

            coalescedFrames = _lastPresentedSequence < 0
                ? frame.Sequence
                : frame.Sequence - _lastPresentedSequence - 1;
            _lastPresentedSequence = frame.Sequence;
            return true;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _shutdown.Cancel();
        try
        {
            _readerTask.Wait(TimeSpan.FromSeconds(1));
        }
        catch (AggregateException exception) when (
            exception.InnerExceptions.All(item => item is OperationCanceledException))
        {
        }
        finally
        {
            _shutdown.Dispose();
        }
    }

    private async Task ReadFramesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var pipe = new NamedPipeClientStream(
                ".",
                _pipeName,
                PipeDirection.InOut,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            using var connectionTimeout = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
            connectionTimeout.CancelAfter(ConnectionTimeout);
            try
            {
                await pipe.ConnectAsync(connectionTimeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                SetTerminalState(
                    AgentViewerClientState.Disconnected,
                    "AGENT VIEWER COULD NOT CONNECT; MATCH CONTROL REMAINS WITH HOST");
                return;
            }
            var token = Encoding.ASCII.GetBytes(_accessToken + "\n");
            await pipe.WriteAsync(token, cancellationToken).ConfigureAwait(false);
            await pipe.FlushAsync(cancellationToken).ConfigureAwait(false);

            long lastSequence = -1;
            AgentViewerFrameV7? lastFrame = null;
            while (!cancellationToken.IsCancellationRequested)
            {
                var line = await ReadLineBoundedAsync(pipe, cancellationToken).ConfigureAwait(false);
                if (line is null)
                {
                    SetTerminalState(
                        lastSequence < 0
                            ? AgentViewerClientState.Rejected
                            : AgentViewerClientState.Disconnected,
                        lastSequence < 0
                            ? "VIEW CAPABILITY REJECTED OR EXPIRED"
                            : "AGENT VIEWER DISCONNECTED; MATCH CONTROL REMAINS WITH HOST");
                    return;
                }

                var frame = JsonSerializer.Deserialize<AgentViewerFrameV7>(line, SerializerOptions);
                if (frame is null
                    || frame.Schema != AgentViewerFrameV7.Contract
                    || frame.Observation is null
                    || frame.Observation.Schema != AgentObservationV5.Contract
                    || !HasValidObservationShape(frame.Observation)
                    || frame.Sequence <= lastSequence
                    || !HasConsistentOperation(frame, lastFrame)
                    || lastFrame is not null
                        && !HasConsistentIdentity(lastFrame.Observation, frame.Observation)
                    || !HasConsistentOutcome(frame))
                {
                    SetTerminalState(
                        AgentViewerClientState.Rejected,
                        "AGENT VIEWER REJECTED AN INVALID FRAME",
                        clearPendingFrame: true);
                    return;
                }

                lastSequence = frame.Sequence;
                lastFrame = frame;
                lock (_sync)
                {
                    _latestFrame = frame;
                    (_state, _status) = DescribeFrame(frame);
                }
            }
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException)
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                SetTerminalState(
                    AgentViewerClientState.Rejected,
                    "AGENT VIEWER REJECTED AN INVALID FRAME",
                    clearPendingFrame: true);
            }
        }
        catch (Exception exception) when (
            exception is IOException
                or OperationCanceledException
                or ObjectDisposedException)
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                SetTerminalState(
                    AgentViewerClientState.Disconnected,
                    "AGENT VIEWER DISCONNECTED; MATCH CONTROL REMAINS WITH HOST");
            }
        }
    }

    private static bool HasConsistentOperation(
        AgentViewerFrameV7 frame,
        AgentViewerFrameV7? previousFrame)
    {
        if (frame.Sequence < 0
            || frame.StartTick < 0
            || !IsStateHash(frame.StartStateHash)
            || !Enum.IsDefined(frame.Operation)
            || frame.StepsAdvanced < 0
            || frame.Observation.Tick - frame.StartTick != frame.StepsAdvanced
            || frame.StepsAdvanced == 0
                && !string.Equals(
                    frame.StartStateHash,
                    frame.Observation.StateHash,
                    StringComparison.Ordinal)
            || previousFrame is not null
                && frame.Sequence == previousFrame.Sequence + 1
                && (frame.StartTick != previousFrame.Observation.Tick
                    || !string.Equals(
                        frame.StartStateHash,
                        previousFrame.Observation.StateHash,
                        StringComparison.Ordinal))
            || frame.BurstStopReason is { } stopReason && !Enum.IsDefined(stopReason)
            || frame.BurstStopEvent is { } stopEvent
                && !AgentBurstPolicy.Stops.Contains(stopEvent))
        {
            return false;
        }

        return frame.Operation switch
        {
            AgentViewerOperationKind.Initial =>
                frame.Sequence == 0
                && frame.StepsAdvanced == 0
                && frame.BurstStopReason is null
                && frame.BurstStopEvent is null
                && frame.StartTick == frame.Observation.Tick
                && string.Equals(
                    frame.StartStateHash,
                    frame.Observation.StateHash,
                    StringComparison.Ordinal)
                && frame.Observation.PreviousAction is null,
            AgentViewerOperationKind.Step =>
                frame.Sequence > 0
                && frame.StepsAdvanced is 0 or 1
                && frame.BurstStopReason is null
                && frame.BurstStopEvent is null
                && frame.Observation.PreviousAction is { } step
                && HasConsistentPreviousAction(step)
                && step.RulesAdvanced == (frame.StepsAdvanced == 1),
            AgentViewerOperationKind.Burst =>
                HasConsistentBurst(frame),
            AgentViewerOperationKind.Finish =>
                frame.Sequence > 0
                && frame.StepsAdvanced == 0
                && frame.BurstStopReason is null
                && frame.BurstStopEvent is null
                && frame.EndReason is AgentMatchEndReason.AgentFinished
                    or AgentMatchEndReason.ReplayFailure,
            _ => false,
        };
    }

    private static bool HasConsistentBurst(AgentViewerFrameV7 frame)
    {
        if (frame.Sequence <= 0
            || frame.StepsAdvanced > AgentBurstRequest.MaximumBurstSteps
            || frame.StepsAdvanced > 0 && frame.BurstStopReason is null
            || frame.BurstStopEvent is not null && frame.BurstStopReason is null
            || frame.Observation.PreviousAction is not { } previousAction
            || !HasConsistentPreviousAction(previousAction)
            || previousAction.RulesAdvanced != (frame.StepsAdvanced > 0))
        {
            return false;
        }

        if (frame.BurstStopEvent is not { } stopEvent)
        {
            return frame.BurstStopReason != AgentBurstStopReason.DecisionEvent;
        }

        if (frame.BurstStopReason is AgentBurstStopReason.RequestedLimit
            or AgentBurstStopReason.ReplayFailure)
        {
            return false;
        }

        var selectedEvent = frame.Observation.PreviousEvents
            .FirstOrDefault(item => AgentBurstPolicy.Stops.Contains(item.Kind));
        return selectedEvent?.Kind == stopEvent;
    }

    private static bool HasValidObservationShape(AgentObservationV5 observation) =>
        !string.IsNullOrWhiteSpace(observation.MatchId)
        && string.Equals(
            observation.RulesetId,
            RulesetIdentity.CurrentId,
            StringComparison.Ordinal)
        && observation.RulesVersion == RulesetIdentity.CurrentVersion
        && RunModeCatalog.All.Any(mode =>
            string.Equals(mode.Id, observation.ModeId, StringComparison.Ordinal)
            && mode.Version == observation.ModeVersion
            && mode.BoardWidth == observation.BoardWidth
            && mode.BoardHeight == observation.BoardHeight)
        && string.Equals(
            observation.ConfigHashAlgorithm,
            RunConfig.ConfigHashAlgorithmId,
            StringComparison.Ordinal)
        && IsLowerHex(observation.ConfigHash, 64)
        && HasCanonicalModeConfiguration(observation)
        && HasValidPassport(observation.Passport)
        && Enum.IsDefined(observation.SeedVisibility)
        && (observation.SeedVisibility == AgentSeedVisibility.Open
            ? observation.GameplaySeed is not null
            : observation.GameplaySeed is null)
        && observation.Tick >= 0
        && observation.MaximumSteps > 0
        && observation.MaximumSteps <= AgentMatchOptions.MaximumAllowedSteps
        && observation.Tick <= observation.MaximumSteps
        && observation.StepsRemaining >= 0
        && observation.StepsRemaining == observation.MaximumSteps - observation.Tick
        && IsStateHash(observation.StateHash)
        && observation.BoardWidth > 0
        && observation.BoardHeight > 0
        && observation.Body is { Count: > 0 }
        && observation.PendingDirections is not null
        && observation.PendingDirections.All(Enum.IsDefined)
        && observation.PreviousEvents is not null
        && observation.PreviousEvents.All(item =>
            item is not null
            && Enum.IsDefined(item.Kind)
            && (item.NewDirection is not { } direction || Enum.IsDefined(direction))
            && (item.Cause is not { } cause || Enum.IsDefined(cause))
            && (item.Power is not { } power || Enum.IsDefined(power)))
        && observation.DetachedObstacles is not null
        && Enum.IsDefined(observation.Status)
        && Enum.IsDefined(observation.DeathCause)
        && HasConsistentRunStatus(observation.Status, observation.DeathCause)
        && Enum.IsDefined(observation.Direction)
        && Enum.IsDefined(observation.AdaptiveDifficultyState)
        && !string.IsNullOrWhiteSpace(observation.AdaptivePolicyId)
        && Enum.IsDefined(observation.Lifecycle)
        && !(observation.StyleContract is not null && observation.LessonProgress is not null)
        && HasValidEpisodeMetrics(observation)
        && HasValidStyleProgress(observation)
        && HasValidLessonProgress(observation)
        && HasValidRivalObservation(observation);

    private static bool HasCanonicalModeConfiguration(AgentObservationV5 observation)
    {
        var mode = RunModeCatalog.All.First(item =>
            string.Equals(item.Id, observation.ModeId, StringComparison.Ordinal)
            && item.Version == observation.ModeVersion);
        var config = RunModeCatalog.CreateConfig(mode);
        return string.Equals(
                observation.ConfigHash,
                config.ComputeConfigHash(),
                StringComparison.Ordinal)
            && observation.WrapsAtEdges
            && observation.AdaptationEnabled == config.EnableAdaptation
            && string.Equals(
                observation.AdaptivePolicyId,
                config.AdaptivePolicyId,
                StringComparison.Ordinal);
    }

    private static bool HasValidPassport(AgentPassportV4? passport) =>
        passport is not null
        && string.Equals(passport.Schema, AgentPassportV4.Contract, StringComparison.Ordinal)
        && IsIdentityToken(passport.AgentId, 64)
        && IsIdentityToken(passport.PolicyVersion, 64)
        && !string.IsNullOrWhiteSpace(passport.DisplayName)
        && passport.DisplayName.Length <= AgentPassportV4.MaximumDisplayNameLength
        && passport.DisplayName == passport.DisplayName.Trim()
        && !passport.DisplayName.Any(char.IsControl)
        && CosmeticSetCatalog.Find(passport.AvatarId) is not null
        && AgentAccentCatalog.All.Any(accent =>
            string.Equals(accent.Id, passport.AccentId, StringComparison.Ordinal))
        && StationIdentityCatalog.All.Any(station =>
            string.Equals(station.Id, passport.StationId, StringComparison.Ordinal))
        && string.Equals(
            passport.ObservationProfile,
            AgentPassportV4.SymbolicStepObservationProfile,
            StringComparison.Ordinal)
        && AgentPassportV4.IsSupportedActionProfile(passport.ActionProfile);

    private static bool HasValidEpisodeMetrics(AgentObservationV5 observation)
    {
        var metrics = observation.EpisodeMetrics;
        return metrics is not null
            && string.Equals(
                metrics.Schema,
                AgentEpisodeMetricsV1.Contract,
                StringComparison.Ordinal)
            && metrics.SurvivalSteps == observation.Tick
            && metrics.FoodEaten is >= 0 && metrics.FoodEaten <= observation.Tick
            && metrics.PeakCombo is >= 0 && metrics.PeakCombo <= metrics.FoodEaten
            && observation.ComboCount is >= 0 && observation.ComboCount <= metrics.PeakCombo
            && metrics.Wraps is >= 0 && metrics.Wraps <= observation.Tick
            && metrics.NearMisses is >= 0 && metrics.NearMisses <= observation.Tick * 2
            && metrics.PowersCollected is >= 0 && metrics.PowersCollected <= observation.Tick
            && metrics.PowersActivated is >= 0
            && metrics.PowersActivated <= observation.Tick * 2
            && metrics.PowersActivated <= metrics.PowersCollected + metrics.Recoveries
            && metrics.Recoveries is >= 0 && metrics.Recoveries <= observation.Tick * 2
            && metrics.StarvationWarnings is >= 0
            && metrics.StarvationWarnings <= observation.Tick
            && metrics.DirectionChanges is >= 0
            && metrics.DirectionChanges <= observation.Tick;
    }

    private static bool HasValidStyleProgress(AgentObservationV5 observation)
    {
        var progress = observation.StyleContract;
        if (progress is null)
        {
            return true;
        }

        if (!AgentStyleContractCatalog.IsValidProgress(progress)
            || !SupportsStyleMode(progress.ContractId, observation.ModeId))
        {
            return false;
        }

        var first = progress.Criteria[0];
        var second = progress.Criteria[1];
        var metrics = observation.EpisodeMetrics;
        return progress.ContractId switch
        {
            AgentStyleContractCatalog.StillwaterId =>
                first.Current == observation.Tick
                && second.Denominator == observation.Tick
                && second.Numerator <= observation.Tick
                    - (observation.Status == RunStatus.Running ? 0 : 1),
            AgentStyleContractCatalog.CrownchaserId =>
                first.Current == metrics.PeakCombo
                && HasValidCrownchaserContinuity(second, observation),
            AgentStyleContractCatalog.EdgeProphetId =>
                first.Current <= metrics.NearMisses
                && second.Current <= first.Current
                && second.Current <= metrics.Wraps,
            AgentStyleContractCatalog.MutagenistId =>
                first.Current <= Math.Min(9, metrics.PowersActivated)
                && second.Current <= first.Current,
            AgentStyleContractCatalog.RedlineId =>
                first.Current == metrics.FoodEaten
                && second.Denominator == observation.Tick
                && second.Numerator <= observation.Tick
                    - (observation.Status == RunStatus.Dead ? 1 : 0),
            _ => false,
        };
    }

    private static bool HasValidCrownchaserContinuity(
        AgentStyleCriterionProgressV2 continuity,
        AgentObservationV5 observation)
    {
        var metrics = observation.EpisodeMetrics;
        if (metrics.PeakCombo < 4)
        {
            return continuity.Numerator == Math.Min(observation.ComboCount, metrics.FoodEaten)
                && continuity.Denominator == metrics.FoodEaten;
        }

        return continuity.Numerator == 4
            && continuity.Denominator is >= 4
            && continuity.Denominator <= metrics.FoodEaten;
    }

    private static bool HasValidLessonProgress(AgentObservationV5 observation)
    {
        var progress = observation.LessonProgress;
        if (progress is null)
        {
            return true;
        }

        AgentSignalLessonDefinitionV2 definition;
        try
        {
            definition = AgentSignalSchoolCatalog.Get(progress.LessonId);
        }
        catch (ArgumentException)
        {
            return false;
        }

        if (!AgentSignalSchoolCatalog.IsValidProgress(progress))
        {
            return false;
        }

        var expectedEvidenceState = observation.Lifecycle switch
        {
            AgentMatchLifecycle.AwaitingAction => AgentLessonEvidenceState.Live,
            AgentMatchLifecycle.Completed or AgentMatchLifecycle.Aborted =>
                AgentLessonEvidenceState.Verified,
            AgentMatchLifecycle.FailedClosed => AgentLessonEvidenceState.FailedClosed,
            _ => (AgentLessonEvidenceState)byte.MaxValue,
        };
        if (progress.EvidenceState != expectedEvidenceState
            || !string.Equals(observation.ModeId, definition.ModeId, StringComparison.Ordinal)
            || observation.SeedVisibility != AgentSeedVisibility.Open
            || observation.GameplaySeed != definition.PracticeSeed
            || observation.MaximumSteps != definition.MaximumSteps
            || observation.StyleContract is not null
            || observation.Rival is not null
            || progress.RetryDescriptor is { } retry
                && !string.Equals(
                    retry.ActionProfile,
                    observation.Passport.ActionProfile,
                    StringComparison.Ordinal))
        {
            return false;
        }

        var first = progress.Requirements[0];
        var second = progress.Requirements[1];
        var metrics = observation.EpisodeMetrics;
        if (definition.Id != AgentSignalSchoolCatalog.FirstTurnId
            && (progress.AttemptEvidenceCount != 0
                || !string.Equals(
                    progress.AttemptEvidenceHash,
                    AgentSignalSchoolCatalog.EmptyAttemptEvidenceHash,
                    StringComparison.Ordinal)))
        {
            return false;
        }

        return definition.Id switch
        {
            AgentSignalSchoolCatalog.FirstTurnId =>
                first.Current == Math.Min(1, progress.AttemptEvidenceCount)
                && second.Current <= first.Current
                && second.Current <= Math.Min(1, metrics.DirectionChanges),
            AgentSignalSchoolCatalog.WrapLineId =>
                first.Current <= Math.Min(1, metrics.Wraps)
                && second.Current <= first.Current,
            AgentSignalSchoolCatalog.HungerRouteId =>
                first.Current <= Math.Min(1, metrics.FoodEaten)
                && second.Current <= first.Current,
            AgentSignalSchoolCatalog.ExitRouteId =>
                first.Current <= Math.Min(1, metrics.FoodEaten)
                && second.Current <= first.Current,
            AgentSignalSchoolCatalog.PowerRouteId =>
                first.Current <= Math.Min(1, metrics.PowersCollected)
                && second.Current <= first.Current
                && second.Current <= Math.Min(1, metrics.PowersActivated),
            AgentSignalSchoolCatalog.RecoverRouteId =>
                first.Current <= Math.Min(1, metrics.Recoveries)
                && second.Current <= first.Current,
            AgentSignalSchoolCatalog.ComboRouteId =>
                first.Current == Math.Min(3, metrics.FoodEaten)
                && second.Current == Math.Min(3, metrics.PeakCombo),
            AgentSignalSchoolCatalog.DeathReadId =>
                HasValidDeathReadProgress(first, second, observation),
            _ => false,
        };
    }

    private static bool HasValidDeathReadProgress(
        AgentLessonRequirementProgressV2 terminalDeath,
        AgentLessonRequirementProgressV2 matchingDeathEvent,
        AgentObservationV5 observation)
    {
        var hasTerminalDeath = observation.Status == RunStatus.Dead
            && observation.DeathCause != DeathCause.None;
        var hasMatchingDeathEvent = observation.PreviousEvents.Any(item =>
            item.Kind == RunEventKind.Died
            && item.Cause == observation.DeathCause);
        var retainsVerifiedTerminalEvidence = hasTerminalDeath
            && observation.Lifecycle is AgentMatchLifecycle.Completed
                or AgentMatchLifecycle.FailedClosed
            && observation.PreviousAction is { Accepted: false, RulesAdvanced: false };
        return terminalDeath.Current == (hasTerminalDeath ? 1 : 0)
            && matchingDeathEvent.Current ==
                (hasMatchingDeathEvent || retainsVerifiedTerminalEvidence ? 1 : 0);
    }

    private static bool HasValidRivalObservation(AgentObservationV5 observation)
    {
        var rival = observation.Rival;
        if (rival is null)
        {
            return true;
        }

        var personality = AiPersonalityCatalog.BuiltIn.FirstOrDefault(item =>
            string.Equals(item.Id, rival.PersonalityId, StringComparison.Ordinal));
        return personality is not null
            && string.Equals(personality.Name, rival.DisplayName, StringComparison.Ordinal)
            && rival.Tick is >= 0
            && rival.Tick <= observation.Tick
            && Enum.IsDefined(rival.Status)
            && Enum.IsDefined(rival.DeathCause)
            && HasConsistentRunStatus(rival.Status, rival.DeathCause)
            && rival.Score is >= 0 and <= SnakeRun.MaximumScore;
    }

    private static bool HasConsistentRunStatus(RunStatus status, DeathCause deathCause) =>
        status switch
        {
            RunStatus.Running or RunStatus.Won => deathCause == DeathCause.None,
            RunStatus.Dead => deathCause is DeathCause.SelfCollision or DeathCause.Starvation,
            _ => false,
        };

    private static bool HasValidStyleOutcome(
        AgentStyleOutcomeV2 outcome,
        AgentStyleProgressV2 progress,
        string modeId) =>
        AgentStyleContractCatalog.IsValidOutcome(outcome)
        && SupportsStyleMode(outcome.ContractId, modeId)
        && HasSameStyleProgress(progress, outcome);

    private static bool SupportsStyleMode(string contractId, string modeId) =>
        AgentStyleContractCatalog.All.Any(definition =>
            string.Equals(definition.Id, contractId, StringComparison.Ordinal)
            && definition.SupportedModeIds.Contains(modeId, StringComparer.Ordinal));

    private static bool HasSameStyleIdentity(
        AgentStyleProgressV2? expected,
        AgentStyleProgressV2? actual)
    {
        if (expected is null || actual is null)
        {
            return expected is null && actual is null;
        }

        return string.Equals(expected.Schema, actual.Schema, StringComparison.Ordinal)
            && string.Equals(expected.ContractId, actual.ContractId, StringComparison.Ordinal)
            && string.Equals(expected.DisplayName, actual.DisplayName, StringComparison.Ordinal)
            && string.Equals(
                expected.EvaluationPolicyId,
                actual.EvaluationPolicyId,
                StringComparison.Ordinal)
            && HasSameCriterionIdentity(expected.Criteria, actual.Criteria);
    }

    private static bool HasSameCriterionIdentity(
        IReadOnlyList<AgentStyleCriterionProgressV2> expected,
        IReadOnlyList<AgentStyleCriterionProgressV2> actual)
    {
        if (expected.Count != 2 || actual.Count != 2)
        {
            return false;
        }

        for (var index = 0; index < expected.Count; index++)
        {
            if (!string.Equals(
                    expected[index].CriterionId,
                    actual[index].CriterionId,
                    StringComparison.Ordinal)
                || !string.Equals(
                    expected[index].DisplayName,
                    actual[index].DisplayName,
                    StringComparison.Ordinal)
                || expected[index].Comparator != actual[index].Comparator
                || expected[index].Unit != actual[index].Unit
                || expected[index].Target != actual[index].Target)
            {
                return false;
            }
        }

        return true;
    }

    private static bool HasSameStyleProgress(
        AgentStyleProgressV2 progress,
        AgentStyleOutcomeV2 outcome)
    {
        if (!string.Equals(progress.ContractId, outcome.ContractId, StringComparison.Ordinal)
            || !string.Equals(progress.DisplayName, outcome.DisplayName, StringComparison.Ordinal)
            || !string.Equals(
                progress.EvaluationPolicyId,
                outcome.EvaluationPolicyId,
                StringComparison.Ordinal)
            || progress.CriteriaSatisfied != outcome.CriteriaSatisfied
            || progress.AllCriteriaSatisfied != outcome.AllCriteriaSatisfied
            || progress.Criteria.Count != 2
            || outcome.Criteria.Count != 2)
        {
            return false;
        }

        for (var index = 0; index < progress.Criteria.Count; index++)
        {
            var expected = progress.Criteria[index];
            var actual = outcome.Criteria[index];
            if (!string.Equals(expected.CriterionId, actual.CriterionId, StringComparison.Ordinal)
                || !string.Equals(expected.DisplayName, actual.DisplayName, StringComparison.Ordinal)
                || expected.Comparator != actual.Comparator
                || expected.Unit != actual.Unit
                || expected.Current != actual.Current
                || expected.Target != actual.Target
                || expected.Numerator != actual.Numerator
                || expected.Denominator != actual.Denominator
                || expected.Satisfied != actual.Satisfied)
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsIdentityToken(string? value, int maximumLength) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Length <= maximumLength
        && value.All(character =>
            char.IsAsciiLetterOrDigit(character)
            || character is '-' or '_' or '.');

    private static bool IsStateHash(string? value) => IsLowerHex(value, 16);

    private static bool IsLowerHex(string? value, int length) =>
        value is not null
        && value.Length == length
        && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static bool HasConsistentPreviousAction(AgentPreviousActionV1 action) =>
        Enum.IsDefined(action.Action)
        && Enum.IsDefined(action.Rejection)
        && Enum.IsDefined(action.DeclaredIntent)
        && (action.Accepted
            ? action.Rejection == AgentActionRejection.None && action.RulesAdvanced
            : action.Rejection != AgentActionRejection.None
                && (!action.RulesAdvanced
                    || action.Rejection == AgentActionRejection.ReplayFailure));

    private static bool HasConsistentIdentity(
        AgentObservationV5 expected,
        AgentObservationV5 actual) =>
        string.Equals(expected.MatchId, actual.MatchId, StringComparison.Ordinal)
        && string.Equals(expected.RulesetId, actual.RulesetId, StringComparison.Ordinal)
        && expected.RulesVersion == actual.RulesVersion
        && string.Equals(expected.ModeId, actual.ModeId, StringComparison.Ordinal)
        && expected.ModeVersion == actual.ModeVersion
        && string.Equals(
            expected.ConfigHashAlgorithm,
            actual.ConfigHashAlgorithm,
            StringComparison.Ordinal)
        && string.Equals(expected.ConfigHash, actual.ConfigHash, StringComparison.Ordinal)
        && expected.SeedVisibility == actual.SeedVisibility
        && expected.GameplaySeed == actual.GameplaySeed
        && Equals(expected.Passport, actual.Passport)
        && expected.MaximumSteps == actual.MaximumSteps
        && expected.BoardWidth == actual.BoardWidth
        && expected.BoardHeight == actual.BoardHeight
        && expected.WrapsAtEdges == actual.WrapsAtEdges
        && HasSameStyleIdentity(expected.StyleContract, actual.StyleContract)
        && HasSameLessonIdentity(expected.LessonProgress, actual.LessonProgress)
        && HasSameRivalIdentity(expected.Rival, actual.Rival);

    private static bool HasSameLessonIdentity(
        AgentLessonProgressV2? expected,
        AgentLessonProgressV2? actual)
    {
        if (expected is null || actual is null)
        {
            return expected is null && actual is null;
        }

        return string.Equals(expected.LessonId, actual.LessonId, StringComparison.Ordinal)
            && string.Equals(expected.Title, actual.Title, StringComparison.Ordinal)
            && string.Equals(expected.Instruction, actual.Instruction, StringComparison.Ordinal)
            && string.Equals(
                expected.EvaluationPolicyId,
                actual.EvaluationPolicyId,
                StringComparison.Ordinal)
            && expected.Requirements.Select(item => new
            {
                item.RequirementId,
                item.DisplayName,
                item.EvidenceSource,
                item.Target,
            })
                .SequenceEqual(actual.Requirements.Select(item => new
                {
                    item.RequirementId,
                    item.DisplayName,
                    item.EvidenceSource,
                    item.Target,
                }))
            && actual.Requirements.Zip(expected.Requirements)
                .All(pair => pair.First.Current >= pair.Second.Current)
            && actual.AttemptEvidenceCount >= expected.AttemptEvidenceCount
            && (actual.AttemptEvidenceCount == expected.AttemptEvidenceCount)
                == string.Equals(
                    actual.AttemptEvidenceHash,
                    expected.AttemptEvidenceHash,
                    StringComparison.Ordinal);
    }

    private static bool HasSameRivalIdentity(
        AgentRivalObservationV1? expected,
        AgentRivalObservationV1? actual) =>
        expected is null || actual is null
            ? expected is null && actual is null
            : string.Equals(
                expected.PersonalityId,
                actual.PersonalityId,
                StringComparison.Ordinal)
            && string.Equals(expected.DisplayName, actual.DisplayName, StringComparison.Ordinal);

    private static bool HasConsistentOutcome(AgentViewerFrameV7 frame)
    {
        var observation = frame.Observation;
        if (!HasConsistentEndReason(frame)
            || !HasConsistentRunEnd(frame))
        {
            return false;
        }
        if (observation.IsActionAwaited)
        {
            return observation.Lifecycle == AgentMatchLifecycle.AwaitingAction
                && frame.EndReason == AgentMatchEndReason.None
                && !frame.VerifiedResultAvailable
                && frame.StyleOutcome is null
                && frame.LessonOutcome is null;
        }

        if (observation.Lifecycle == AgentMatchLifecycle.FailedClosed)
        {
            return frame.EndReason == AgentMatchEndReason.ReplayFailure
                && !frame.VerifiedResultAvailable
                && frame.StyleOutcome is null
                && frame.LessonOutcome is null;
        }

        var lessonRequirementsSatisfied =
            observation.LessonProgress?.AllRequirementsSatisfied == true;
        var successfulTerminal = frame.VerifiedResultAvailable
            && (observation.Lifecycle == AgentMatchLifecycle.Completed
                && (frame.EndReason is AgentMatchEndReason.RulesTerminal
                    or AgentMatchEndReason.StepLimit
                    || frame.EndReason == AgentMatchEndReason.AgentFinished
                    && lessonRequirementsSatisfied)
                || observation.Lifecycle == AgentMatchLifecycle.Aborted
                && frame.EndReason == AgentMatchEndReason.AgentFinished
                && !lessonRequirementsSatisfied);
        if (!successfulTerminal)
        {
            return false;
        }

        var hasValidStyleOutcome = observation.StyleContract is { } style
            ? frame.StyleOutcome is { } styleOutcome
                && HasValidStyleOutcome(styleOutcome, style, observation.ModeId)
            : frame.StyleOutcome is null;
        var hasValidLessonOutcome = observation.LessonProgress is { } lesson
            ? frame.LessonOutcome is { } lessonOutcome
                && HasValidLessonOutcome(
                    lessonOutcome,
                    lesson,
                    observation.Passport.ActionProfile,
                    frame.EndReason)
            : frame.LessonOutcome is null;
        return hasValidStyleOutcome && hasValidLessonOutcome;
    }

    private static bool HasValidLessonOutcome(
        AgentLessonOutcomeV2 outcome,
        AgentLessonProgressV2 progress,
        string actionProfile,
        AgentMatchEndReason endReason) =>
        AgentSignalSchoolCatalog.IsValidOutcome(outcome)
        && outcome.EndReason == endReason
        && string.Equals(outcome.LessonId, progress.LessonId, StringComparison.Ordinal)
        && string.Equals(
            outcome.EvaluationPolicyId,
            progress.EvaluationPolicyId,
            StringComparison.Ordinal)
        && outcome.Requirements.SequenceEqual(progress.Requirements)
        && outcome.RequirementsSatisfied == progress.RequirementsSatisfied
        && outcome.AllRequirementsSatisfied == progress.AllRequirementsSatisfied
        && string.Equals(
            outcome.FirstUnmetRequirementId,
            progress.FirstUnmetRequirementId,
            StringComparison.Ordinal)
        && outcome.AttemptEvidenceCount == progress.AttemptEvidenceCount
        && string.Equals(
            outcome.AttemptEvidenceHash,
            progress.AttemptEvidenceHash,
            StringComparison.Ordinal)
        && string.Equals(
            outcome.RetryDescriptor.ActionProfile,
            actionProfile,
            StringComparison.Ordinal);

    private static bool HasConsistentRunEnd(AgentViewerFrameV7 frame) =>
        frame.EndReason switch
        {
            AgentMatchEndReason.None => frame.Observation.Status == RunStatus.Running,
            AgentMatchEndReason.RulesTerminal => frame.Observation.Status is
                RunStatus.Dead or RunStatus.Won,
            AgentMatchEndReason.StepLimit =>
                frame.Observation.Status == RunStatus.Running
                && frame.Observation.Tick == frame.Observation.MaximumSteps,
            AgentMatchEndReason.AgentFinished => frame.Observation.Status == RunStatus.Running,
            AgentMatchEndReason.ReplayFailure => true,
            _ => false,
        };

    private static bool HasConsistentEndReason(AgentViewerFrameV7 frame)
    {
        if (frame.Operation == AgentViewerOperationKind.Finish)
        {
            return frame.EndReason is AgentMatchEndReason.AgentFinished
                or AgentMatchEndReason.ReplayFailure;
        }

        if (frame.StepsAdvanced > 0)
        {
            return frame.Operation switch
            {
                AgentViewerOperationKind.Initial => false,
                AgentViewerOperationKind.Step =>
                    frame.EndReason is not AgentMatchEndReason.AgentFinished,
                AgentViewerOperationKind.Burst => frame.EndReason switch
                {
                    AgentMatchEndReason.None => frame.BurstStopReason is
                        AgentBurstStopReason.RequestedLimit
                        or AgentBurstStopReason.DecisionEvent
                        or AgentBurstStopReason.LessonRequirementsReached,
                    AgentMatchEndReason.RulesTerminal =>
                        frame.BurstStopReason == AgentBurstStopReason.RulesTerminal,
                    AgentMatchEndReason.StepLimit =>
                        frame.BurstStopReason == AgentBurstStopReason.MatchStepLimit,
                    AgentMatchEndReason.ReplayFailure =>
                        frame.BurstStopReason == AgentBurstStopReason.ReplayFailure,
                    _ => false,
                },
                _ => false,
            };
        }

        if (frame.Operation == AgentViewerOperationKind.Initial)
        {
            return frame.EndReason == AgentMatchEndReason.None;
        }

        var previousAction = frame.Observation.PreviousAction;
        if (frame.Operation == AgentViewerOperationKind.Burst
            && frame.BurstStopReason is not null
            && !(frame.BurstStopReason == AgentBurstStopReason.ReplayFailure
                && frame.EndReason == AgentMatchEndReason.ReplayFailure))
        {
            return false;
        }

        return frame.EndReason == AgentMatchEndReason.None
            || previousAction is { Accepted: false, RulesAdvanced: false };
    }

    private static (AgentViewerClientState State, string Status) DescribeFrame(
        AgentViewerFrameV7 frame) => frame.EndReason switch
        {
            AgentMatchEndReason.None => (
                AgentViewerClientState.Watching,
                "AWAITING AGENT ACTION; RULES PAUSED"),
            AgentMatchEndReason.RulesTerminal => (
                AgentViewerClientState.Completed,
                "AGENT MATCH ENDED BY RULES; VERIFIED REPLAY READY"),
            AgentMatchEndReason.StepLimit => (
                AgentViewerClientState.Completed,
                "AGENT MATCH REACHED STEP LIMIT; VERIFIED REPLAY READY"),
            AgentMatchEndReason.AgentFinished => (
                AgentViewerClientState.Completed,
                "AGENT FINISHED MATCH; VERIFIED REPLAY READY"),
            AgentMatchEndReason.ReplayFailure => (
                AgentViewerClientState.FailedClosed,
                "AGENT MATCH FAILED CLOSED; NO VERIFIED REPLAY"),
            _ => throw new ArgumentOutOfRangeException(nameof(frame)),
        };

    private static async Task<string?> ReadLineBoundedAsync(
        Stream stream,
        CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var single = new byte[1];
        while (buffer.Length < MaximumFrameBytes)
        {
            var read = await stream.ReadAsync(single, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                return null;
            }

            if (single[0] == (byte)'\n')
            {
                return Encoding.UTF8.GetString(buffer.ToArray());
            }

            buffer.WriteByte(single[0]);
        }

        throw new JsonException("Agent viewer frame exceeded its byte limit.");
    }

    private void SetTerminalState(
        AgentViewerClientState state,
        string status,
        bool clearPendingFrame = false)
    {
        lock (_sync)
        {
            if (clearPendingFrame)
            {
                _latestFrame = null;
            }
            _state = state;
            _status = status;
        }
    }

    private static void ValidateAccessToken(string accessToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);
        if (accessToken.Length > 128
            || accessToken.Any(character =>
                !(char.IsAsciiLetterOrDigit(character)
                    || character is '-' or '_')))
        {
            throw new ArgumentException(
                "Agent viewer capabilities must be bounded ASCII tokens.",
                nameof(accessToken));
        }
    }

    private static JsonSerializerOptions CreateSerializerOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = false,
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            AllowDuplicateProperties = false,
            RespectRequiredConstructorParameters = true,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        };
        options.Converters.Add(
            new JsonStringEnumConverter(
                JsonNamingPolicy.SnakeCaseLower,
                allowIntegerValues: false));
        return options;
    }
}

public static class AgentViewerPresentation
{
    public static RunSnapshot ProjectSnapshot(AgentObservationV5 observation)
    {
        ArgumentNullException.ThrowIfNull(observation);
        if (observation.Schema != AgentObservationV5.Contract
            || observation.Body is null
            || observation.Body.Count == 0
            || observation.PendingDirections is null
            || observation.DetachedObstacles is null
            || observation.BoardWidth <= 0
            || observation.BoardHeight <= 0
            || string.IsNullOrWhiteSpace(observation.StateHash))
        {
            throw new ArgumentException("Agent viewer observation is invalid.", nameof(observation));
        }

        return new RunSnapshot(
            observation.Tick,
            observation.Status,
            observation.DeathCause,
            observation.Direction,
            Array.AsReadOnly(observation.Body.Select(ProjectPoint).ToArray()),
            Array.AsReadOnly(observation.PendingDirections.ToArray()),
            observation.Food is { } food ? ProjectPoint(food) : null,
            observation.Score,
            observation.ComboCount,
            observation.ComboMultiplier,
            observation.TicksSinceLastFood,
            observation.HungerTicksRemaining,
            observation.HungerMaximumTicks,
            observation.HungerWarningTicks,
            observation.PowerPickup is { } pickup
                ? new PowerPickup(
                    pickup.Kind,
                    ProjectPoint(pickup.Position),
                    pickup.VisibilityTicksRemaining)
                : null,
            observation.PowerSpawnTicksElapsed,
            observation.ShieldTicksRemaining,
            observation.PhaseShiftTicksRemaining,
            observation.LastStandHeld,
            observation.LastStandRecoveryTicksRemaining,
            observation.SlowMoTicksRemaining,
            observation.BoostTicksRemaining,
            observation.MagnetTicksRemaining,
            observation.GluttonyTicksRemaining,
            observation.BaitPosition is { } bait ? ProjectPoint(bait) : null,
            Array.AsReadOnly(observation.DetachedObstacles.Select(ProjectPoint).ToArray()),
            observation.DetachedObstacleTicksRemaining,
            observation.StateHash,
            observation.AdaptiveDifficultyState,
            observation.AdaptivePolicyId,
            observation.AdaptationEnabled);
    }

    private static GridPoint ProjectPoint(AgentPointV1 point) => new(point.X, point.Y);
}
