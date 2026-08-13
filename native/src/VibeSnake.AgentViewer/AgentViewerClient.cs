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
}

public sealed class AgentViewerClient : IDisposable
{
    public const int MaximumFrameBytes = 262_144;

    private static readonly JsonSerializerOptions SerializerOptions = CreateSerializerOptions();
    private readonly object _sync = new();
    private readonly string _pipeName;
    private readonly string _accessToken;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Task _readerTask;
    private AgentViewerFrameV1? _latestFrame;
    private AgentViewerClientState _state = AgentViewerClientState.Connecting;
    private string _status = "CONNECTING TO AGENT MATCH";
    private bool _disposed;

    public AgentViewerClient(string pipeName, string accessToken)
    {
        ValidateToken(pipeName, nameof(pipeName));
        ValidateToken(accessToken, nameof(accessToken));
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

    public bool TryTakeLatest(out AgentViewerFrameV1? frame)
    {
        lock (_sync)
        {
            frame = _latestFrame;
            _latestFrame = null;
            return frame is not null;
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
            await pipe.ConnectAsync(cancellationToken).ConfigureAwait(false);
            var token = Encoding.ASCII.GetBytes(_accessToken + "\n");
            await pipe.WriteAsync(token, cancellationToken).ConfigureAwait(false);
            await pipe.FlushAsync(cancellationToken).ConfigureAwait(false);

            long lastSequence = -1;
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
                            : "AGENT VIEWER DISCONNECTED; VERIFIED REPLAY REMAINS AVAILABLE");
                    return;
                }

                var frame = JsonSerializer.Deserialize<AgentViewerFrameV1>(line, SerializerOptions);
                if (frame is null
                    || frame.Schema != AgentViewerFrameV1.Contract
                    || frame.Observation is null
                    || frame.Observation.Schema != AgentObservationV1.Contract
                    || frame.Sequence <= lastSequence)
                {
                    SetTerminalState(
                        AgentViewerClientState.Rejected,
                        "AGENT VIEWER REJECTED AN INVALID FRAME");
                    return;
                }

                lastSequence = frame.Sequence;
                lock (_sync)
                {
                    _latestFrame = frame;
                    _state = frame.Observation.IsActionAwaited
                        ? AgentViewerClientState.Watching
                        : AgentViewerClientState.Completed;
                    _status = frame.Observation.IsActionAwaited
                        ? "WATCHING AGENT LIVE"
                        : "AGENT MATCH COMPLETE; VERIFIED REPLAY READY";
                }
            }
        }
        catch (Exception exception) when (
            exception is IOException
                or OperationCanceledException
                or ObjectDisposedException
                or JsonException)
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                SetTerminalState(
                    AgentViewerClientState.Disconnected,
                    "AGENT VIEWER DISCONNECTED; VERIFIED REPLAY REMAINS AVAILABLE");
            }
        }
    }

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

    private void SetTerminalState(AgentViewerClientState state, string status)
    {
        lock (_sync)
        {
            _state = state;
            _status = status;
        }
    }

    private static void ValidateToken(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (value.Length > 128
            || value.Any(character =>
                !(char.IsAsciiLetterOrDigit(character)
                    || character is '-' or '_')))
        {
            throw new ArgumentException(
                "Agent viewer capabilities must be bounded ASCII tokens.",
                parameterName);
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
    public static RunSnapshot ProjectSnapshot(AgentObservationV1 observation)
    {
        ArgumentNullException.ThrowIfNull(observation);
        if (observation.Schema != AgentObservationV1.Contract
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
