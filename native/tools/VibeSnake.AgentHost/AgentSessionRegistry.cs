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
    public const int LiveMatchIdleLeaseMinutes = 30;
    public const string RetentionPolicy =
        "Up to eight matches are retained in this host process. Completed matches are evicted first when capacity is needed. At capacity, a live match idle for at least 30 minutes may be reclaimed without a result or replay. Viewer activity never refreshes this lease, and all handles expire when the process exits.";

    private readonly object _sync = new();
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly ReplayStore _replayStore;
    private readonly AgentExhibitionArchiveStore? _archiveStore;
    private readonly Func<string> _handleGenerator;
    private readonly Func<ulong> _seedGenerator;
    private readonly TimeProvider _timeProvider;
    private long _nextOrder;
    private bool _disposed;

    public AgentSessionRegistry(
        ReplayStore replayStore,
        Func<string>? handleGenerator = null,
        Func<ulong>? seedGenerator = null,
        TimeProvider? timeProvider = null,
        AgentExhibitionArchiveStore? archiveStore = null)
    {
        ArgumentNullException.ThrowIfNull(replayStore);
        _replayStore = replayStore;
        _archiveStore = archiveStore;
        _handleGenerator = handleGenerator ?? GenerateHandle;
        _seedGenerator = seedGenerator ?? GenerateSeed;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public StartAgentMatchV5 StartMatch(
        string modeId,
        AgentSeedVisibility seedVisibility,
        string? gameplaySeed,
        int? maximumSteps,
        string? styleContractId = null,
        string? rivalPersonalityId = null,
        bool watchEnabled = false,
        AgentPassportV4? passport = null,
        string actionProfile = AgentPassportV4.FourDirectionActionProfile) =>
        StartMatchCore(
            modeId,
            seedVisibility,
            gameplaySeed,
            maximumSteps,
            styleContractId,
            rivalPersonalityId,
            watchEnabled,
            passport,
            actionProfile,
            lessonId: null);

    public StartAgentMatchV5 StartLesson(
        string lessonId,
        bool watchEnabled = false,
        AgentPassportV4? passport = null,
        string actionProfile = AgentPassportV4.FourDirectionActionProfile)
    {
        var lesson = AgentSignalSchoolCatalog.Get(lessonId);
        return StartMatchCore(
            lesson.ModeId,
            AgentSeedVisibility.Open,
            lesson.PracticeSeed.ToString(CultureInfo.InvariantCulture),
            lesson.MaximumSteps,
            styleContractId: null,
            rivalPersonalityId: null,
            watchEnabled,
            passport,
            actionProfile,
            lesson.Id);
    }

    private StartAgentMatchV5 StartMatchCore(
        string modeId,
        AgentSeedVisibility seedVisibility,
        string? gameplaySeed,
        int? maximumSteps,
        string? styleContractId,
        string? rivalPersonalityId,
        bool watchEnabled,
        AgentPassportV4? passport,
        string actionProfile,
        string? lessonId)
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
            EnsureCapacityAvailable();
            var handle = MintUniqueHandle();
            var options = new AgentMatchOptions(
                handle,
                modeId,
                RunModeCatalog.CurrentModeVersion,
                seed,
                seedVisibility,
                stepLimit,
                styleContractId,
                rivalPersonalityId,
                passport,
                actionProfile,
                lessonId);
            AgentViewerServer? viewer = null;
            try
            {
                viewer = watchEnabled ? AgentViewerServer.Create() : null;
                var session = new AgentMatchSession(options, viewer);
                MakeCapacity();
                _entries.Add(
                    handle,
                    new Entry(
                        session,
                        _nextOrder++,
                        _timeProvider.GetTimestamp(),
                        viewer));
                return new StartAgentMatchV5(
                    StartAgentMatchV5.Contract,
                    handle,
                    RetentionPolicy,
                    session.Observe(),
                    viewer?.Connection);
            }
            catch
            {
                viewer?.Dispose();
                throw;
            }
        }
    }

    public AgentObservationV5 Observe(string matchHandle) =>
        GetSession(matchHandle).Observe();

    public AgentActionResponseV5 PlayMove(
        string matchHandle,
        string idempotencyKey,
        int expectedTick,
        string expectedStateHash,
        AgentAction action,
        AgentPublicIntent declaredIntent = AgentPublicIntent.Undeclared) =>
        AgentActionResponseV5.FromResponse(
            GetSession(matchHandle).SubmitAction(new AgentActionRequest(
                idempotencyKey,
                expectedTick,
                expectedStateHash,
                action,
                declaredIntent)));

    public AgentBurstResponseV5 PlayBurst(
        string matchHandle,
        string idempotencyKey,
        int expectedTick,
        string expectedStateHash,
        AgentAction initialAction,
        int maximumSteps,
        AgentPublicIntent declaredIntent = AgentPublicIntent.Undeclared) =>
        AgentBurstResponseV5.FromResponse(
            GetSession(matchHandle).SubmitBurst(new AgentBurstRequest(
                idempotencyKey,
                expectedTick,
                expectedStateHash,
                initialAction,
                maximumSteps,
                declaredIntent)));

    public AgentMatchSummaryV5 Finish(string matchHandle) =>
        AgentMatchSummaryV5.FromResult(GetSession(matchHandle).Finish());

    public AgentMatchResultStatusV5 GetResult(string matchHandle)
    {
        var result = GetSession(matchHandle).GetResult();
        return new AgentMatchResultStatusV5(
            AgentMatchResultStatusV5.Contract,
            matchHandle,
            result is not null,
            result is null ? null : AgentMatchSummaryV5.FromResult(result));
    }

    public AgentExhibitionReceiptStatusV1 GetExhibitionReceipt(string matchHandle)
    {
        var receipt = GetSession(matchHandle).TryCreateExhibitionReceipt();
        return new AgentExhibitionReceiptStatusV1(
            AgentExhibitionReceiptStatusV1.Contract,
            matchHandle,
            receipt is not null,
            receipt);
    }

    /// <summary>
    /// Archives one verified exhibition beside the lane replays this host
    /// already saved. Archiving is explicit and separate from playing: a match
    /// is ephemeral until a caller asks for it to be kept, and an exhibition
    /// cannot be kept until both of its lanes exist as saved replay files.
    /// </summary>
    public AgentExhibitionArchiveStatusV1 ArchiveExhibition(string matchHandle)
    {
        if (_archiveStore is null)
        {
            throw new InvalidOperationException(
                "This host was started without an exhibition archive.");
        }

        var receipt = GetSession(matchHandle).TryCreateExhibitionReceipt();
        if (receipt is null)
        {
            return ArchiveStatus(
                matchHandle,
                new AgentExhibitionArchiveWriteV1(
                    AgentExhibitionArchiveCode.NoVerifiedReceipt,
                    "A live, unverified, or failed-closed match has no exhibition receipt to archive.",
                    Archived: false,
                    EvictedCount: 0,
                    RecoveredFromCorruption: false,
                    _archiveStore.Read()),
                receipt);
        }

        var saved = ReadSavedReplayNames(matchHandle);
        if (saved.AgentFileName is null
            || (receipt.RivalReplayPayloadHash is not null && saved.RivalFileName is null))
        {
            return ArchiveStatus(
                matchHandle,
                new AgentExhibitionArchiveWriteV1(
                    AgentExhibitionArchiveCode.ReplayNotSaved,
                    "Call save_verified_replay first. An archived exhibition names the saved replay file for every lane it contains.",
                    Archived: false,
                    EvictedCount: 0,
                    RecoveredFromCorruption: false,
                    _archiveStore.Read()),
                receipt);
        }

        return ArchiveStatus(
            matchHandle,
            _archiveStore.Archive(
                receipt,
                saved.AgentFileName,
                receipt.RivalReplayPayloadHash is null ? null : saved.RivalFileName),
            receipt);
    }

    private static AgentExhibitionArchiveStatusV1 ArchiveStatus(
        string matchHandle,
        AgentExhibitionArchiveWriteV1 write,
        AgentExhibitionReceiptV2? receipt) =>
        new(
            AgentExhibitionArchiveStatusV1.Contract,
            matchHandle,
            write.Archived,
            write.Code,
            write.Message,
            receipt?.ReceiptHash,
            receipt?.RouteIdentityHash,
            write.Archive.Entries.Count,
            write.Archive.Capacity,
            write.EvictedCount,
            write.RecoveredFromCorruption,
            write.Archive.Entries
                .Select(AgentArchivedExhibitionIndexEntryV1.FromEntry)
                .ToArray());

    private (string? AgentFileName, string? RivalFileName) ReadSavedReplayNames(
        string matchHandle)
    {
        ValidateHandle(matchHandle);
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _entries.TryGetValue(matchHandle, out var entry)
                ? (entry.SavedAgentReplayFileName, entry.SavedRivalReplayFileName)
                : (null, null);
        }
    }

    private void RecordSavedReplayNames(
        string matchHandle,
        string? agentFileName,
        string? rivalFileName)
    {
        lock (_sync)
        {
            if (_disposed || !_entries.TryGetValue(matchHandle, out var entry))
            {
                return;
            }

            entry.SavedAgentReplayFileName = agentFileName ?? entry.SavedAgentReplayFileName;
            entry.SavedRivalReplayFileName = rivalFileName ?? entry.SavedRivalReplayFileName;
        }
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
        RecordSavedReplayNames(
            matchHandle,
            saved.IsSuccess ? saved.FileName : null,
            rivalSaved is { IsSuccess: true } ? rivalSaved.FileName : null);
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
            if (!_entries.TryGetValue(matchHandle, out var entry))
            {
                throw new KeyNotFoundException("The match handle is unknown or expired.");
            }

            entry.LastActivityTimestamp = _timeProvider.GetTimestamp();
            return entry.Session;
        }
    }

    private void MakeCapacity()
    {
        if (_entries.Count < MaximumRetainedMatches)
        {
            return;
        }

        var evictableKey = FindEvictableKey();
        if (evictableKey is null)
        {
            throw new InvalidOperationException(
                "The host reached its live-match capacity. Finish a match or wait for an inactive live-match lease to expire before starting another.");
        }

        var evictable = _entries[evictableKey];
        _entries.Remove(evictableKey);
        evictable.Viewer?.Dispose();
    }

    private void EnsureCapacityAvailable()
    {
        if (_entries.Count >= MaximumRetainedMatches && FindEvictableKey() is null)
        {
            throw new InvalidOperationException(
                "The host reached its live-match capacity. Finish a match or wait for an inactive live-match lease to expire before starting another.");
        }
    }

    private string? FindEvictableKey()
    {
        var finalized = _entries
            .Where(pair => pair.Value.Session.Lifecycle != AgentMatchLifecycle.AwaitingAction)
            .OrderBy(pair => pair.Value.Order)
            .FirstOrDefault();
        if (finalized.Key is not null)
        {
            return finalized.Key;
        }

        var now = _timeProvider.GetTimestamp();
        return _entries
            .Where(pair =>
                pair.Value.Session.Lifecycle == AgentMatchLifecycle.AwaitingAction
                && _timeProvider.GetElapsedTime(
                    pair.Value.LastActivityTimestamp,
                    now) >= TimeSpan.FromMinutes(LiveMatchIdleLeaseMinutes))
            .OrderBy(pair => pair.Value.LastActivityTimestamp)
            .ThenBy(pair => pair.Value.Order)
            .Select(pair => pair.Key)
            .FirstOrDefault();
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

    private sealed class Entry
    {
        public Entry(
            AgentMatchSession session,
            long order,
            long lastActivityTimestamp,
            AgentViewerServer? viewer)
        {
            Session = session;
            Order = order;
            LastActivityTimestamp = lastActivityTimestamp;
            Viewer = viewer;
        }

        public AgentMatchSession Session { get; }

        public long Order { get; }

        public long LastActivityTimestamp { get; set; }

        /// <summary>
        /// The replay file names this host actually wrote for the match, so an
        /// archived exhibition names a file that exists instead of a hash it
        /// hopes someone kept.
        /// </summary>
        public string? SavedAgentReplayFileName { get; set; }

        public string? SavedRivalReplayFileName { get; set; }

        public AgentViewerServer? Viewer { get; }
    }
}
