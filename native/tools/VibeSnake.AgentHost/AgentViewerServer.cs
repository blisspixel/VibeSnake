using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using VibeSnake.AgentPlay;

namespace VibeSnake.AgentHost;

public sealed record AgentViewerConnectionV1(
    string Schema,
    string Transport,
    string PipeName,
    string AccessToken,
    string RetentionPolicy)
{
    public const string Contract = "vibesnake-agent-viewer-connection-v1";
}

internal sealed class AgentViewerServer : IAgentViewerSink, IDisposable
{
    public const int MaximumTokenBytes = 128;
    public const string ViewerRetentionPolicy =
        "One local same-user viewer may attach with the one-time capability while this host retains the match. Only the newest unsent frame is retained; sequence gaps expose coalesced earlier updates, and the verified replay produced by successful finalization remains canonical.";

    private static readonly JsonSerializerOptions SerializerOptions = CreateSerializerOptions();
    private readonly Channel<AgentViewerFrameV4> _frames = Channel.CreateBounded<AgentViewerFrameV4>(
        new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false,
        });
    private readonly byte[] _accessTokenBytes;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Task _serverTask;
    private bool _disposed;

    public AgentViewerServer(string pipeName, byte[] accessTokenBytes)
    {
        if (!AgentViewerTransport.IsValidPipeName(pipeName))
        {
            throw new ArgumentException(
                $"The viewer pipe name must be an ASCII token no longer than {AgentViewerTransport.MaximumPipeNameLength} characters.",
                nameof(pipeName));
        }
        ArgumentNullException.ThrowIfNull(accessTokenBytes);
        if (accessTokenBytes.Length == 0 || accessTokenBytes.Length > 64)
        {
            throw new ArgumentOutOfRangeException(nameof(accessTokenBytes));
        }

        PipeName = pipeName;
        _accessTokenBytes = accessTokenBytes.ToArray();
        AccessToken = Convert.ToBase64String(_accessTokenBytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
        Connection = new AgentViewerConnectionV1(
            AgentViewerConnectionV1.Contract,
            "named-pipe",
            PipeName,
            AccessToken,
            ViewerRetentionPolicy);
        _serverTask = RunAsync(_shutdown.Token);
    }

    public string PipeName { get; }

    public string AccessToken { get; }

    public AgentViewerConnectionV1 Connection { get; }

    public bool TryPublish(AgentViewerFrameV4 frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        return !_disposed && _frames.Writer.TryWrite(frame);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _frames.Writer.TryComplete();
        _shutdown.Cancel();
        try
        {
            _serverTask.Wait(TimeSpan.FromSeconds(1));
        }
        catch (AggregateException exception) when (
            exception.InnerExceptions.All(item => item is OperationCanceledException))
        {
        }
        finally
        {
            CryptographicOperations.ZeroMemory(_accessTokenBytes);
            _shutdown.Dispose();
        }
    }

    internal static AgentViewerServer Create()
    {
        Span<byte> nameBytes = stackalloc byte[10];
        RandomNumberGenerator.Fill(nameBytes);
        var pipeName = "vs_" + Convert.ToHexString(nameBytes).ToLowerInvariant();
        var token = RandomNumberGenerator.GetBytes(32);
        return new AgentViewerServer(pipeName, token);
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var pipe = new NamedPipeServerStream(
                PipeName,
                PipeDirection.InOut,
                maxNumberOfServerInstances: 1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            await pipe.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
            var suppliedToken = await ReadTokenAsync(pipe, cancellationToken).ConfigureAwait(false);
            if (suppliedToken is null
                || !CryptographicOperations.FixedTimeEquals(suppliedToken, _accessTokenBytes))
            {
                return;
            }

            await foreach (var frame in _frames.Reader.ReadAllAsync(cancellationToken))
            {
                var json = JsonSerializer.Serialize(frame, SerializerOptions);
                var payload = Encoding.UTF8.GetBytes(json + "\n");
                await pipe.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
                await pipe.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception exception) when (
            exception is IOException
                or OperationCanceledException
                or ObjectDisposedException)
        {
        }
    }

    private static async Task<byte[]?> ReadTokenAsync(
        Stream stream,
        CancellationToken cancellationToken)
    {
        var encoded = new byte[MaximumTokenBytes];
        var length = 0;
        var single = new byte[1];
        while (length < encoded.Length)
        {
            var read = await stream.ReadAsync(single, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                return null;
            }

            if (single[0] == (byte)'\n')
            {
                break;
            }

            if (!char.IsAsciiLetterOrDigit((char)single[0])
                && single[0] is not (byte)'-' and not (byte)'_')
            {
                return null;
            }

            encoded[length++] = single[0];
        }

        if (length == 0 || length == encoded.Length)
        {
            return null;
        }

        try
        {
            var text = Encoding.ASCII.GetString(encoded, 0, length);
            var padded = text.Replace('-', '+').Replace('_', '/');
            padded += new string('=', (4 - (padded.Length % 4)) % 4);
            return Convert.FromBase64String(padded);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static JsonSerializerOptions CreateSerializerOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower));
        return options;
    }
}
