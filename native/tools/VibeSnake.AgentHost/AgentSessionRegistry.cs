using System.Globalization;
using System.Security.Cryptography;
using VibeSnake.AgentPlay;
using VibeSnake.Persistence;
using VibeSnake.Rules;

namespace VibeSnake.AgentHost;

public sealed class AgentSessionRegistry : IDisposable
{
    public const int MaximumRetainedMatches = 8;
    public const int MaximumHandleGenerationAttempts = 16;
    public const string RetentionPolicy =
        "Up to eight matches are retained in this host process. Completed matches may be evicted first when capacity is needed, and all handles expire when the process exits.";

    private readonly object _sync = new();
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly ReplayStore _replayStore;
    private readonly Func<string> _handleGenerator;
    private readonly Func<ulong> _seedGenerator;
    private long _nextOrder;
    private bool _disposed;

    public AgentSessionRegistry(
        ReplayStore replayStore,
        Func<string>? handleGenerator = null,
        Func<ulong>? seedGenerator = null)
    {
        ArgumentNullException.ThrowIfNull(replayStore);
        _replayStore = replayStore;
        _handleGenerator = handleGenerator ?? GenerateHandle;
        _seedGenerator = seedGenerator ?? GenerateSeed;
    }

    public StartAgentMatchV1 StartMatch(
        string modeId,
        AgentSeedVisibility seedVisibility,
        string? gameplaySeed,
        int? maximumSteps,
        string? styleContractId = null,
        string? rivalPersonalityId = null,
        bool watchEnabled = false,
        AgentPassportV1? passport = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modeId);
        if (!RunModeCatalog.IsSupportedIdentity(
            modeId,
            RunModeCatalog.CurrentModeVersion))
        {
            throw new ArgumentException(
                "modeId must be classic or vibe.",
                nameof(modeId));
        }

        if (!Enum.IsDefined(seedVisibility))
        {
            throw new ArgumentOutOfRangeException(nameof(seedVisibility));
        }

        if (seedVisibility == AgentSeedVisibility.Blind && gameplaySeed is not null)
        {
            throw new ArgumentException(
                "A blind-seed match cannot accept a caller-selected seed.",
                nameof(gameplaySeed));
        }

        var seed = gameplaySeed is null
            ? _seedGenerator()
            : ParseSeed(gameplaySeed);
        var stepLimit = maximumSteps ?? AgentMatchOptions.DefaultMaximumSteps;
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            MakeCapacity();
            var handle = MintUniqueHandle();
            AgentViewerServer? viewer = watchEnabled ? AgentViewerServer.Create() : null;
            AgentMatchSession session;
            try
            {
                session = new AgentMatchSession(
                    new AgentMatchOptions(
                        handle,
                        modeId,
                        RunModeCatalog.CurrentModeVersion,
                        seed,
                        seedVisibility,
                        stepLimit,
                        styleContractId,
                        rivalPersonalityId,
                        passport),
                    viewer);
            }
            catch
            {
                viewer?.Dispose();
                throw;
            }

            _entries.Add(handle, new Entry(session, _nextOrder++, viewer));
            return new StartAgentMatchV1(
                StartAgentMatchV1.Contract,
                handle,
                RetentionPolicy,
                session.Observe(),
                viewer?.Connection);
        }
    }

    public AgentObservationV1 Observe(string matchHandle) =>
        GetSession(matchHandle).Observe();

    public AgentActionResponseV1 PlayMove(
        string matchHandle,
        string idempotencyKey,
        int expectedTick,
        string expectedStateHash,
        AgentAction action,
        AgentPublicIntent declaredIntent = AgentPublicIntent.Undeclared) =>
        AgentActionResponseV1.FromResponse(
            GetSession(matchHandle).SubmitAction(new AgentActionRequest(
                idempotencyKey,
                expectedTick,
                expectedStateHash,
                action,
                declaredIntent)));

    public AgentMatchSummaryV1 Finish(string matchHandle) =>
        AgentMatchSummaryV1.FromResult(GetSession(matchHandle).Finish());

    public AgentMatchResultStatusV1 GetResult(string matchHandle)
    {
        var result = GetSession(matchHandle).GetResult();
        return new AgentMatchResultStatusV1(
            AgentMatchResultStatusV1.Contract,
            matchHandle,
            result is not null,
            result is null ? null : AgentMatchSummaryV1.FromResult(result));
    }

    public AgentReplaySaveV1 SaveVerifiedReplay(string matchHandle)
    {
        var result = GetSession(matchHandle).GetResult()
            ?? throw new InvalidOperationException(
                "The match must be completed or explicitly finished before its replay can be saved.");
        var saved = _replayStore.Save(result.VerifiedReplay);
        var rivalSaved = result.VerifiedRivalReplay is null
            ? null
            : _replayStore.Save(result.VerifiedRivalReplay);
        var allSucceeded = saved.IsSuccess && (rivalSaved?.IsSuccess ?? true);
        var effectiveCode = saved.IsSuccess && rivalSaved is { IsSuccess: false }
            ? rivalSaved.Code
            : saved.Code;
        var message = rivalSaved is null
            ? saved.Message
            : $"Agent replay: {saved.Message} Rival replay: {rivalSaved.Message}";
        return new AgentReplaySaveV1(
            AgentReplaySaveV1.Contract,
            matchHandle,
            allSucceeded,
            effectiveCode,
            message,
            saved.FileName,
            saved.Verification?.Code,
            rivalSaved?.Code,
            rivalSaved?.Message,
            rivalSaved?.FileName,
            rivalSaved?.Verification?.Code);
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            foreach (var entry in _entries.Values)
            {
                entry.Viewer?.Dispose();
            }

            _entries.Clear();
        }
    }

    private AgentMatchSession GetSession(string matchHandle)
    {
        ValidateHandle(matchHandle);
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _entries.TryGetValue(matchHandle, out var entry)
                ? entry.Session
                : throw new KeyNotFoundException("The match handle is unknown or expired.");
        }
    }

    private void MakeCapacity()
    {
        if (_entries.Count < MaximumRetainedMatches)
        {
            return;
        }

        var evictable = _entries
            .Where(pair => pair.Value.Session.Lifecycle != AgentMatchLifecycle.AwaitingAction)
            .OrderBy(pair => pair.Value.Order)
            .FirstOrDefault();
        if (evictable.Key is null)
        {
            throw new InvalidOperationException(
                "The host reached its live-match capacity. Finish an existing match before starting another.");
        }

        _entries.Remove(evictable.Key);
        evictable.Value.Viewer?.Dispose();
    }

    private string MintUniqueHandle()
    {
        for (var attempt = 0; attempt < MaximumHandleGenerationAttempts; attempt++)
        {
            var candidate = _handleGenerator();
            ValidateHandle(candidate);
            if (!_entries.ContainsKey(candidate))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException(
            "The host could not mint a unique match handle.");
    }

    private static ulong ParseSeed(string value)
    {
        if (value.Length == 0 || value.Length > 20
            || !ulong.TryParse(
                value,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var seed))
        {
            throw new ArgumentException(
                "gameplaySeed must be an unsigned 64-bit decimal string.",
                nameof(value));
        }

        return seed;
    }

    private static void ValidateHandle(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (value.Length > AgentMatchOptions.MaximumMatchIdLength
            || !value.StartsWith("match_", StringComparison.Ordinal)
            || value.Any(character =>
                !(char.IsAsciiLetterOrDigit(character)
                    || character is '-' or '_')))
        {
            throw new ArgumentException("The match handle is invalid.", nameof(value));
        }
    }

    private static string GenerateHandle()
    {
        Span<byte> bytes = stackalloc byte[18];
        RandomNumberGenerator.Fill(bytes);
        return "match_" + Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static ulong GenerateSeed()
    {
        Span<byte> bytes = stackalloc byte[sizeof(ulong)];
        RandomNumberGenerator.Fill(bytes);
        return BitConverter.ToUInt64(bytes);
    }

    private sealed record Entry(
        AgentMatchSession Session,
        long Order,
        AgentViewerServer? Viewer = null);
}
