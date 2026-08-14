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
    private AgentViewerFrameV5? _latestFrame;
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
        out AgentViewerFrameV5? frame,
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
            AgentViewerFrameV5? lastFrame = null;
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

                var frame = JsonSerializer.Deserialize<AgentViewerFrameV5>(line, SerializerOptions);
                if (frame is null
                    || frame.Schema != AgentViewerFrameV5.Contract
                    || frame.Observation is null
                    || frame.Observation.Schema != AgentObservationV3.Contract
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
        AgentViewerFrameV5 frame,
        AgentViewerFrameV5? previousFrame)
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

    private static bool HasConsistentBurst(AgentViewerFrameV5 frame)
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

    private static bool HasValidObservationShape(AgentObservationV3 observation) =>
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
        && HasValidPassport(observation.Passport)
        && Enum.IsDefined(observation.SeedVisibility)
        && (observation.SeedVisibility == AgentSeedVisibility.Open
            ? observation.GameplaySeed is not null
            : observation.GameplaySeed is null)
        && observation.Tick >= 0
        && observation.MaximumSteps > 0
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
        && Enum.IsDefined(observation.Direction)
        && Enum.IsDefined(observation.AdaptiveDifficultyState)
        && !string.IsNullOrWhiteSpace(observation.AdaptivePolicyId)
        && Enum.IsDefined(observation.Lifecycle);

    private static bool HasValidPassport(AgentPassportV2? passport) =>
        passport is not null
        && string.Equals(passport.Schema, AgentPassportV2.Contract, StringComparison.Ordinal)
        && IsIdentityToken(passport.AgentId, 64)
        && IsIdentityToken(passport.PolicyVersion, 64)
        && !string.IsNullOrWhiteSpace(passport.DisplayName)
        && passport.DisplayName.Length <= AgentPassportV2.MaximumDisplayNameLength
        && passport.DisplayName == passport.DisplayName.Trim()
        && !passport.DisplayName.Any(char.IsControl)
        && CosmeticSetCatalog.Find(passport.AvatarId) is not null
        && AgentAccentCatalog.All.Any(accent =>
            string.Equals(accent.Id, passport.AccentId, StringComparison.Ordinal))
        && StationIdentityCatalog.All.Any(station =>
            string.Equals(station.Id, passport.StationId, StringComparison.Ordinal))
        && string.Equals(
            passport.ObservationProfile,
            AgentPassportV2.SymbolicStepObservationProfile,
            StringComparison.Ordinal)
        && AgentPassportV2.IsSupportedActionProfile(passport.ActionProfile);

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
        AgentObservationV3 expected,
        AgentObservationV3 actual) =>
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
        && expected.WrapsAtEdges == actual.WrapsAtEdges;

    private static bool HasConsistentOutcome(AgentViewerFrameV5 frame)
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
                && !frame.VerifiedResultAvailable;
        }

        if (observation.Lifecycle == AgentMatchLifecycle.FailedClosed)
        {
            return frame.EndReason == AgentMatchEndReason.ReplayFailure
                && !frame.VerifiedResultAvailable;
        }

        return frame.VerifiedResultAvailable
            && (observation.Lifecycle == AgentMatchLifecycle.Completed
                && frame.EndReason is AgentMatchEndReason.RulesTerminal
                    or AgentMatchEndReason.StepLimit
                || observation.Lifecycle == AgentMatchLifecycle.Aborted
                && frame.EndReason == AgentMatchEndReason.AgentFinished);
    }

    private static bool HasConsistentRunEnd(AgentViewerFrameV5 frame) =>
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

    private static bool HasConsistentEndReason(AgentViewerFrameV5 frame)
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
                        or AgentBurstStopReason.LessonTargetReached,
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
            || previousAction is { Accepted: false } rejection
            && rejection.Rejection is AgentActionRejection.MatchNotAwaitingAction
                or AgentActionRejection.ReplayFailure;
    }

    private static (AgentViewerClientState State, string Status) DescribeFrame(
        AgentViewerFrameV5 frame) => frame.EndReason switch
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
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower));
        return options;
    }
}

public static class AgentViewerPresentation
{
    public static RunSnapshot ProjectSnapshot(AgentObservationV3 observation)
    {
        ArgumentNullException.ThrowIfNull(observation);
        if (observation.Schema != AgentObservationV3.Contract
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
