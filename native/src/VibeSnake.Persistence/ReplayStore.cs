using System.Diagnostics;
using System.Globalization;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using VibeSnake.Rules;

namespace VibeSnake.Persistence;

public sealed partial class ReplayStore
{
    public const string ReplayDirectoryName = "replays";
    public const string ReplayFileExtension = ".vibesnake-replay.json";
    public const int MaximumStoredReplays = 256;
    public const long MaximumStoredReplayBytes = 256L * 1024 * 1024;
    public const string StoreLockFileName = ".vibesnake-replay-store.lock";
    public const string ReplayExportDirectoryName = "replay-exports";
    public const int MaximumReplayExports = 256;
    public const long MaximumReplayExportBytes = 256L * 1024 * 1024;

    private static readonly TimeSpan DefaultStoreLockWait = TimeSpan.FromSeconds(2);
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _storeLockWait;

    public ReplayStore(
        string userDataRoot,
        TimeProvider? timeProvider = null,
        TimeSpan? storeLockWait = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userDataRoot);
        if (!Path.IsPathFullyQualified(userDataRoot))
        {
            throw new ArgumentException(
                "The user-data root must be an absolute path.",
                nameof(userDataRoot));
        }

        UserDataRoot = Path.GetFullPath(userDataRoot);
        ReplayDirectory = Path.Combine(UserDataRoot, ReplayDirectoryName);
        ReplayExportDirectory = Path.Combine(UserDataRoot, ReplayExportDirectoryName);
        _timeProvider = timeProvider ?? TimeProvider.System;
        _storeLockWait = storeLockWait ?? DefaultStoreLockWait;
        if (_storeLockWait <= TimeSpan.Zero || _storeLockWait > TimeSpan.FromSeconds(30))
        {
            throw new ArgumentOutOfRangeException(
                nameof(storeLockWait),
                "The replay-store lock wait must be greater than zero and no more than 30 seconds.");
        }
    }

    public string UserDataRoot { get; }

    public string ReplayDirectory { get; }

    public string ReplayExportDirectory { get; }

    public ReplaySaveResult Save(RunReplay replay)
    {
        ArgumentNullException.ThrowIfNull(replay);
        var verification = replay.Verify();
        if (!verification.IsValid)
        {
            return new ReplaySaveResult(
                ReplaySaveCode.ReplayInvalid,
                "The replay was not saved because deterministic verification failed: "
                    + verification.Message,
                Verification: verification);
        }

        var serialized = replay.Serialize();
        var bytes = StrictUtf8.GetBytes(serialized);
        var timestamp = _timeProvider.GetUtcNow().ToUniversalTime().ToString(
            "yyyyMMdd'T'HHmmssfff'Z'",
            CultureInfo.InvariantCulture);
        var fileName = $"{timestamp}_{replay.PayloadHash}{ReplayFileExtension}";
        var destination = Path.Combine(ReplayDirectory, fileName);
        string? temporaryPath = null;
        try
        {
            Directory.CreateDirectory(ReplayDirectory);
            using var storeLock = TryAcquireStoreLock();
            if (storeLock is null)
            {
                return new ReplaySaveResult(
                    ReplaySaveCode.Busy,
                    "The replay store is busy in another process; retry the save.",
                    fileName,
                    verification);
            }

            var storedFiles = Directory
                .EnumerateFiles(
                    ReplayDirectory,
                    $"*{ReplayFileExtension}",
                    SearchOption.TopDirectoryOnly)
                .Take(MaximumStoredReplays + 1)
                .Select(path => new FileInfo(path))
                .ToArray();
            var matchingReplay = storedFiles.FirstOrDefault(file =>
                IsGeneratedFileName(file.Name)
                && file.Name.EndsWith(
                    $"_{replay.PayloadHash}{ReplayFileExtension}",
                    StringComparison.Ordinal));
            if (matchingReplay is not null)
            {
                return ExistingResult(
                    matchingReplay.FullName,
                    bytes,
                    matchingReplay.Name,
                    verification);
            }

            if (storedFiles.Length >= MaximumStoredReplays)
            {
                return CapacityResult(
                    "The replay was not saved because the store reached its file-count limit.",
                    fileName,
                    verification);
            }

            var storedBytes = 0L;
            foreach (var file in storedFiles)
            {
                if (file.Length > MaximumStoredReplayBytes - storedBytes)
                {
                    return CapacityResult(
                        "The replay was not saved because the store reached its byte limit.",
                        fileName,
                        verification);
                }

                storedBytes += file.Length;
            }

            if (bytes.Length > MaximumStoredReplayBytes - storedBytes)
            {
                return CapacityResult(
                    "The replay was not saved because the store reached its byte limit.",
                    fileName,
                    verification);
            }

            if (File.Exists(destination))
            {
                return ExistingResult(destination, bytes, fileName, verification);
            }

            temporaryPath = destination + $".tmp-{Guid.NewGuid():N}";
            using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 64 * 1024,
                FileOptions.WriteThrough))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }

            try
            {
                File.Move(temporaryPath, destination, overwrite: false);
                temporaryPath = null;
            }
            catch (IOException) when (File.Exists(destination))
            {
                var result = ExistingResult(destination, bytes, fileName, verification);
                if (result.IsSuccess && temporaryPath is not null)
                {
                    File.Delete(temporaryPath);
                    temporaryPath = null;
                }

                return result;
            }

            return new ReplaySaveResult(
                ReplaySaveCode.Saved,
                "The replay was saved atomically and is ready for verification.",
                fileName,
                verification);
        }
        catch (Exception exception) when (IsFileSystemFailure(exception))
        {
            return new ReplaySaveResult(
                ReplaySaveCode.IoFailure,
                "The replay could not be saved because its storage directory is unavailable.",
                fileName,
                verification);
        }
        finally
        {
            if (temporaryPath is not null)
            {
                TryDeleteTemporaryFile(temporaryPath);
            }
        }
    }

    public ReplayLoadResult LoadLatest()
    {
        var listed = ListStored();
        if (!listed.IsSuccess)
        {
            return new ReplayLoadResult(
                listed.Code == ReplayListCode.CapacityExceeded
                    ? ReplayLoadCode.CapacityExceeded
                    : ReplayLoadCode.IoFailure,
                listed.Message);
        }

        return listed.Replays.Count == 0
            ? NotFound("No saved replays are available.")
            : Load(listed.Replays[0].FileName);
    }

    public ReplayListResult ListStored()
    {
        try
        {
            if (File.Exists(UserDataRoot))
            {
                return ListFailure(
                    ReplayListCode.IoFailure,
                    "Saved replays could not be listed because the user-data root is not a directory.");
            }

            if (!Directory.Exists(ReplayDirectory))
            {
                return new ReplayListResult(
                    ReplayListCode.Listed,
                    "No saved replays are available.",
                    []);
            }

            var files = Directory
                .EnumerateFiles(
                    ReplayDirectory,
                    $"*{ReplayFileExtension}",
                    SearchOption.TopDirectoryOnly)
                .Take(MaximumStoredReplays + 1)
                .Select(path => new FileInfo(path))
                .ToArray();
            if (files.Length > MaximumStoredReplays)
            {
                return ListFailure(
                    ReplayListCode.CapacityExceeded,
                    "Saved replays exceed the supported file-count limit; archive files before browsing.");
            }

            var totalBytes = 0L;
            var summaries = new List<StoredReplaySummary>(files.Length);
            foreach (var file in files)
            {
                if (file.Length > MaximumStoredReplayBytes - totalBytes)
                {
                    return ListFailure(
                        ReplayListCode.CapacityExceeded,
                        "Saved replays exceed the supported byte limit; archive files before browsing.");
                }

                totalBytes += file.Length;
                if (!TryParseGeneratedFileName(file.Name, out var storedAtUtc, out var payloadHash))
                {
                    continue;
                }

                summaries.Add(
                    new StoredReplaySummary(
                        file.Name,
                        storedAtUtc,
                        payloadHash,
                        file.Length));
            }

            return new ReplayListResult(
                ReplayListCode.Listed,
                summaries.Count == 0
                    ? "No saved replays are available."
                    : $"Listed {summaries.Count} saved replay(s).",
                summaries
                    .OrderByDescending(value => value.FileName, StringComparer.Ordinal)
                    .ToArray());
        }
        catch (Exception exception) when (IsFileSystemFailure(exception))
        {
            return ListFailure(
                ReplayListCode.IoFailure,
                "Saved replays could not be listed because their storage directory is unavailable.");
        }
    }

    public ReplayBrowserResult BrowseStored()
    {
        var listed = ListStored();
        if (!listed.IsSuccess)
        {
            return new ReplayBrowserResult(listed.Code, listed.Message, []);
        }

        var entries = new List<ReplayBrowserEntry>(listed.Replays.Count);
        foreach (var summary in listed.Replays)
        {
            var loaded = Load(summary.FileName);
            if (loaded.IsSuccess && loaded.Replay is { } replay)
            {
                var initialRun = SnakeRun.RestoreCanonicalState(replay.InitialCanonicalState);
                entries.Add(
                    new ReplayBrowserEntry(
                        summary.PayloadHash,
                        summary.StoredAtUtc,
                        replay.CapturedAtUtc ?? summary.StoredAtUtc,
                        summary.FileBytes,
                        ReplayBrowserState.Verified,
                        ReplayVerificationCode.Verified.ToString(),
                        loaded.Message,
                        initialRun.Mode.Id,
                        initialRun.Mode.Version,
                        replay.Ruleset.Id,
                        replay.Ruleset.Version,
                        replay.Outcome.Score,
                        replay.GameplaySeed ?? initialRun.MasterSeed,
                        replay.Outcome.StepCount));
                continue;
            }

            var state = loaded.Code switch
            {
                ReplayLoadCode.VerificationFailed => ReplayBrowserState.Modified,
                ReplayLoadCode.Incompatible
                    when loaded.Compatibility?.Code == ReplayCompatibilityCode.IntegrityMismatch =>
                    ReplayBrowserState.Modified,
                ReplayLoadCode.Incompatible
                    when loaded.Compatibility?.Code == ReplayCompatibilityCode.InvalidPayload =>
                    ReplayBrowserState.Unreadable,
                ReplayLoadCode.Incompatible => ReplayBrowserState.Incompatible,
                _ => ReplayBrowserState.Unreadable,
            };
            var statusCode = loaded.Verification?.Code.ToString()
                ?? loaded.Compatibility?.Code.ToString()
                ?? loaded.Code.ToString();
            entries.Add(
                new ReplayBrowserEntry(
                    summary.PayloadHash,
                    summary.StoredAtUtc,
                    summary.StoredAtUtc,
                    summary.FileBytes,
                    state,
                    statusCode,
                    loaded.Message));
        }

        return new ReplayBrowserResult(
            ReplayListCode.Listed,
            entries.Count == 0
                ? "No saved replays are available."
                : $"Inspected {entries.Count} saved replay(s); metadata and status are current.",
            entries);
    }

    public ReplayLoadResult LoadByReplayId(string replayId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(replayId);
        if (!IsReplayId(replayId))
        {
            return new ReplayLoadResult(
                ReplayLoadCode.InvalidName,
                "A replay id must be a lowercase SHA-256 digest.");
        }

        var listed = ListStored();
        if (!listed.IsSuccess)
        {
            return new ReplayLoadResult(
                listed.Code == ReplayListCode.CapacityExceeded
                    ? ReplayLoadCode.CapacityExceeded
                    : ReplayLoadCode.IoFailure,
                listed.Message);
        }

        var summary = listed.Replays.FirstOrDefault(value =>
            string.Equals(value.PayloadHash, replayId, StringComparison.Ordinal));
        return summary is null
            ? NotFound("The requested replay is no longer stored.")
            : Load(summary.FileName);
    }

    public ReplayExportResult Export(string replayId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(replayId);
        if (!IsReplayId(replayId))
        {
            return new ReplayExportResult(
                ReplayExportCode.InvalidReplayId,
                "A replay id must be a lowercase SHA-256 digest.");
        }

        var loaded = LoadByReplayId(replayId);
        if (!loaded.IsSuccess || loaded.Replay is null)
        {
            return new ReplayExportResult(
                loaded.Code == ReplayLoadCode.NotFound
                    ? ReplayExportCode.NotFound
                    : ReplayExportCode.ReplayUnavailable,
                "The replay was not exported because it is not verified: " + loaded.Message);
        }

        var listed = ListStored();
        var summary = listed.Replays.FirstOrDefault(value =>
            string.Equals(value.PayloadHash, replayId, StringComparison.Ordinal));
        if (!listed.IsSuccess || summary is null)
        {
            return new ReplayExportResult(
                ReplayExportCode.NotFound,
                "The selected replay is no longer stored; nothing was exported.",
                PayloadHash: replayId);
        }

        var bytes = StrictUtf8.GetBytes(loaded.Replay.Serialize());
        var timestamp = DateTimeOffset.ParseExact(
                summary.StoredAtUtc,
                RunReplay.CaptureTimestampFormat,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal)
            .ToString("yyyyMMdd'T'HHmmssfff'Z'", CultureInfo.InvariantCulture);
        var fileName = $"replay_{timestamp}_{replayId}{ReplayFileExtension}";
        var destination = Path.Combine(ReplayExportDirectory, fileName);
        string? temporaryPath = null;
        try
        {
            Directory.CreateDirectory(ReplayDirectory);
            Directory.CreateDirectory(ReplayExportDirectory);
            using var storeLock = TryAcquireStoreLock();
            if (storeLock is null)
            {
                return new ReplayExportResult(
                    ReplayExportCode.Busy,
                    "The replay library is busy; retry the export.",
                    fileName,
                    replayId);
            }

            var exports = Directory
                .EnumerateFiles(
                    ReplayExportDirectory,
                    $"replay_*{ReplayFileExtension}",
                    SearchOption.TopDirectoryOnly)
                .Take(MaximumReplayExports + 1)
                .Select(path => new FileInfo(path))
                .ToArray();
            var exportBytes = 0L;
            foreach (var export in exports)
            {
                if (export.Length > MaximumReplayExportBytes - exportBytes)
                {
                    return ReplayExportCapacity(fileName, replayId);
                }

                exportBytes += export.Length;
            }

            if (File.Exists(destination))
            {
                return FileContentEquals(destination, bytes)
                    ? new ReplayExportResult(
                        ReplayExportCode.AlreadyExists,
                        "The identical verified replay is already exported.",
                        fileName,
                        replayId)
                    : new ReplayExportResult(
                        ReplayExportCode.IoFailure,
                        "The export destination exists with different content; nothing was overwritten.",
                        fileName,
                        replayId);
            }

            if (exports.Length >= MaximumReplayExports
                || bytes.Length > MaximumReplayExportBytes - exportBytes)
            {
                return ReplayExportCapacity(fileName, replayId);
            }

            temporaryPath = destination + $".tmp-{Guid.NewGuid():N}";
            using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 64 * 1024,
                FileOptions.WriteThrough))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, destination, overwrite: false);
            temporaryPath = null;
            return new ReplayExportResult(
                ReplayExportCode.Exported,
                "The verified replay was exported atomically to user://replay-exports/.",
                fileName,
                replayId);
        }
        catch (Exception exception) when (IsFileSystemFailure(exception))
        {
            return new ReplayExportResult(
                ReplayExportCode.IoFailure,
                "The replay export directory is unavailable; no stored replay was changed.",
                fileName,
                replayId);
        }
        finally
        {
            if (temporaryPath is not null)
            {
                TryDeleteTemporaryFile(temporaryPath);
            }
        }
    }

    public ReplayDeletionPlanResult PlanDeletion(string replayId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(replayId);
        if (!IsReplayId(replayId))
        {
            return new ReplayDeletionPlanResult(
                ReplayDeletionPlanCode.InvalidReplayId,
                "A replay id must be a lowercase SHA-256 digest.");
        }

        var listed = ListStored();
        if (!listed.IsSuccess)
        {
            return new ReplayDeletionPlanResult(
                ReplayDeletionPlanCode.IoFailure,
                listed.Message);
        }

        var summary = listed.Replays.FirstOrDefault(value =>
            string.Equals(value.PayloadHash, replayId, StringComparison.Ordinal));
        if (summary is null)
        {
            return new ReplayDeletionPlanResult(
                ReplayDeletionPlanCode.NotFound,
                "The selected replay is no longer stored.");
        }

        try
        {
            var contentHash = ComputeFileSha256(
                Path.Combine(ReplayDirectory, summary.FileName),
                MaximumStoredReplayBytes);
            var sizeKiB = Math.Max(1L, (summary.FileBytes + 1023L) / 1024L);
            var plan = new ReplayDeletionPlan(
                replayId,
                summary.StoredAtUtc,
                summary.FileBytes,
                contentHash,
                $"Permanently delete the {sizeKiB} KiB local replay from {summary.StoredAtUtc}?");
            return new ReplayDeletionPlanResult(
                ReplayDeletionPlanCode.Ready,
                "Replay deletion requires a separate confirmation.",
                plan);
        }
        catch (Exception exception) when (IsFileSystemFailure(exception))
        {
            return new ReplayDeletionPlanResult(
                ReplayDeletionPlanCode.IoFailure,
                "The selected replay could not be inspected; nothing was deleted.");
        }
    }

    public ReplayDeleteResult Delete(ReplayDeletionPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (!IsReplayId(plan.ReplayId)
            || !IsReplayId(plan.ContentSha256)
            || plan.FileBytes < 0
            || !IsCanonicalStoredAtUtc(plan.StoredAtUtc))
        {
            return new ReplayDeleteResult(
                ReplayDeleteCode.InvalidPlan,
                "The replay deletion plan is invalid; nothing was deleted.");
        }

        try
        {
            if (!Directory.Exists(ReplayDirectory))
            {
                return new ReplayDeleteResult(
                    ReplayDeleteCode.NotFound,
                    "The selected replay is no longer stored.");
            }

            using var storeLock = TryAcquireStoreLock();
            if (storeLock is null)
            {
                return new ReplayDeleteResult(
                    ReplayDeleteCode.Busy,
                    "The replay library is busy; retry deletion after reviewing the confirmation again.");
            }

            var listed = ListStored();
            if (!listed.IsSuccess)
            {
                return new ReplayDeleteResult(
                    ReplayDeleteCode.IoFailure,
                    "The replay library could not be inspected; nothing was deleted.");
            }

            var summary = listed.Replays.FirstOrDefault(value =>
                string.Equals(value.PayloadHash, plan.ReplayId, StringComparison.Ordinal));
            if (summary is null)
            {
                return new ReplayDeleteResult(
                    ReplayDeleteCode.NotFound,
                    "The selected replay is no longer stored.");
            }

            var path = Path.Combine(ReplayDirectory, summary.FileName);
            if (summary.FileBytes != plan.FileBytes
                || !string.Equals(summary.StoredAtUtc, plan.StoredAtUtc, StringComparison.Ordinal)
                || !string.Equals(
                    ComputeFileSha256(path, MaximumStoredReplayBytes),
                    plan.ContentSha256,
                    StringComparison.Ordinal))
            {
                return new ReplayDeleteResult(
                    ReplayDeleteCode.ChangedSinceConsent,
                    "The selected replay changed after confirmation was prepared; nothing was deleted.");
            }

            File.Delete(path);
            return new ReplayDeleteResult(
                ReplayDeleteCode.Deleted,
                "The selected local replay was permanently deleted. Existing exports were preserved.");
        }
        catch (Exception exception) when (IsFileSystemFailure(exception))
        {
            return new ReplayDeleteResult(
                ReplayDeleteCode.IoFailure,
                "The selected replay could not be deleted; its current file was preserved.");
        }
    }

    public ReplayLoadResult Load(string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        if (!IsValidStoredFileName(fileName))
        {
            return new ReplayLoadResult(
                ReplayLoadCode.InvalidName,
                "A saved replay name must be a single file ending in "
                    + ReplayFileExtension + ".",
                fileName);
        }

        if (File.Exists(UserDataRoot))
        {
            return new ReplayLoadResult(
                ReplayLoadCode.IoFailure,
                "The replay could not be read because the user-data root is not a directory.",
                fileName);
        }

        return ReadFromPath(Path.Combine(ReplayDirectory, fileName), fileName);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Performance",
        "CA1822:Mark members as static",
        Justification = "The established instance member is retained for public API compatibility.")]
    public ReplayLoadResult InspectExternal(string absolutePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(absolutePath);
        if (!Path.IsPathFullyQualified(absolutePath))
        {
            return new ReplayLoadResult(
                ReplayLoadCode.InvalidName,
                "An imported replay path must be absolute.");
        }

        string resolved;
        try
        {
            resolved = Path.GetFullPath(absolutePath);
        }
        catch (Exception exception) when (
            exception is ArgumentException
                or NotSupportedException
                or PathTooLongException)
        {
            return new ReplayLoadResult(
                ReplayLoadCode.InvalidName,
                "The imported replay path is invalid.");
        }

        return ReadFromPath(resolved, Path.GetFileName(resolved));
    }

    private static ReplaySaveResult ExistingResult(
        string destination,
        ReadOnlySpan<byte> expected,
        string fileName,
        ReplayVerificationResult verification)
    {
        return FileContentEquals(destination, expected)
            ? new ReplaySaveResult(
                ReplaySaveCode.AlreadyExists,
                "The identical replay is already stored.",
                fileName,
                verification)
            : new ReplaySaveResult(
                ReplaySaveCode.IoFailure,
                "The replay destination already exists with different content; no file was overwritten.",
                fileName,
                verification);
    }

    private static ReplaySaveResult CapacityResult(
        string message,
        string fileName,
        ReplayVerificationResult verification) =>
        new(
            ReplaySaveCode.CapacityReached,
            message + " Existing replays were preserved; archive or remove reviewed files before retrying.",
            fileName,
            verification);

    private static ReplayExportResult ReplayExportCapacity(
        string fileName,
        string replayId) =>
        new(
            ReplayExportCode.CapacityReached,
            "The replay export library reached its bounded capacity; existing exports and stored replays were preserved.",
            fileName,
            replayId);

    private static bool FileContentEquals(
        string path,
        ReadOnlySpan<byte> expected)
    {
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            FileOptions.SequentialScan);
        if (stream.Length != expected.Length)
        {
            return false;
        }

        var buffer = new byte[64 * 1024];
        var offset = 0;
        while (offset < expected.Length)
        {
            var count = stream.Read(
                buffer,
                0,
                Math.Min(buffer.Length, expected.Length - offset));
            if (count == 0 || !buffer.AsSpan(0, count).SequenceEqual(expected.Slice(offset, count)))
            {
                return false;
            }

            offset += count;
        }

        return true;
    }

    private static ReplayLoadResult ReadFromPath(string path, string? fileName)
    {
        try
        {
            if (!File.Exists(path))
            {
                return NotFound("The requested replay file does not exist.", fileName);
            }

            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 64 * 1024,
                FileOptions.SequentialScan);
            if (stream.Length > RunReplay.MaximumSerializedCharacters)
            {
                return new ReplayLoadResult(
                    ReplayLoadCode.TooLarge,
                    $"The replay exceeds the {RunReplay.MaximumSerializedCharacters}-byte import limit.",
                    fileName);
            }

            using var buffer = new MemoryStream(
                capacity: checked((int)Math.Min(
                    stream.Length + 1,
                    RunReplay.MaximumSerializedCharacters + 1L)));
            var chunk = new byte[64 * 1024];
            while (true)
            {
                var count = stream.Read(chunk, 0, chunk.Length);
                if (count == 0)
                {
                    break;
                }

                if (buffer.Length + count > RunReplay.MaximumSerializedCharacters)
                {
                    return new ReplayLoadResult(
                        ReplayLoadCode.TooLarge,
                        $"The replay exceeds the {RunReplay.MaximumSerializedCharacters}-byte import limit.",
                        fileName);
                }

                buffer.Write(chunk, 0, count);
            }

            var bytes = buffer.GetBuffer().AsSpan(0, checked((int)buffer.Length));
            if (bytes.StartsWith(Encoding.UTF8.Preamble))
            {
                return new ReplayLoadResult(
                    ReplayLoadCode.InvalidEncoding,
                    "The replay must use UTF-8 without a byte-order mark.",
                    fileName);
            }

            string serialized;
            try
            {
                serialized = StrictUtf8.GetString(bytes);
            }
            catch (DecoderFallbackException)
            {
                return new ReplayLoadResult(
                    ReplayLoadCode.InvalidEncoding,
                    "The replay is not valid UTF-8.",
                    fileName);
            }

            var read = RunReplay.Read(serialized);
            if (!read.Compatibility.IsCompatible || read.Replay is null)
            {
                return new ReplayLoadResult(
                    ReplayLoadCode.Incompatible,
                    read.Compatibility.Message,
                    fileName,
                    read.Compatibility);
            }

            var verification = read.Replay.Verify();
            if (!verification.IsValid)
            {
                return new ReplayLoadResult(
                    ReplayLoadCode.VerificationFailed,
                    verification.Message,
                    fileName,
                    read.Compatibility,
                    verification);
            }

            return new ReplayLoadResult(
                ReplayLoadCode.Loaded,
                "The replay is compatible and passed deterministic verification.",
                fileName,
                read.Compatibility,
                verification,
                read.Replay);
        }
        catch (Exception exception) when (IsFileSystemFailure(exception))
        {
            return new ReplayLoadResult(
                ReplayLoadCode.IoFailure,
                "The replay could not be read because the file is unavailable.",
                fileName);
        }
    }

    private FileStream? TryAcquireStoreLock()
    {
        var lockPath = Path.Combine(ReplayDirectory, StoreLockFileName);
        var stopwatch = Stopwatch.StartNew();
        while (true)
        {
            try
            {
                return new FileStream(
                    lockPath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    bufferSize: 1,
                    FileOptions.None);
            }
            catch (IOException) when (stopwatch.Elapsed < _storeLockWait)
            {
                Thread.Sleep(10);
            }
            catch (IOException)
            {
                return null;
            }
        }
    }

    private static bool IsValidStoredFileName(string fileName) =>
        fileName.Length > ReplayFileExtension.Length
        && !fileName.Any(character =>
            character <= 31
            || character is ':' or '/' or '\\')
        && string.Equals(Path.GetFileName(fileName), fileName, StringComparison.Ordinal)
        && fileName.EndsWith(ReplayFileExtension, StringComparison.Ordinal);

    private static bool IsReplayId(string value)
    {
        if (value.Length != 64)
        {
            return false;
        }

        foreach (var character in value)
        {
            if ((character < '0' || character > '9')
                && (character < 'a' || character > 'f'))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsCanonicalStoredAtUtc(string value) =>
        DateTimeOffset.TryParseExact(
            value,
            RunReplay.CaptureTimestampFormat,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var parsed)
        && string.Equals(
            parsed.ToString(RunReplay.CaptureTimestampFormat, CultureInfo.InvariantCulture),
            value,
            StringComparison.Ordinal);

    private static bool IsGeneratedFileName(string fileName) =>
        TryParseGeneratedFileName(fileName, out _, out _);

    private static bool TryParseGeneratedFileName(
        string fileName,
        out string storedAtUtc,
        out string payloadHash)
    {
        storedAtUtc = string.Empty;
        payloadHash = string.Empty;
        const int timestampLength = 19;
        var expectedLength = timestampLength
            + 1
            + 64
            + ReplayFileExtension.Length;
        if (
            fileName.Length != expectedLength
            || fileName[timestampLength] != '_'
            || !fileName.EndsWith(ReplayFileExtension, StringComparison.Ordinal))
        {
            return false;
        }

        if (!DateTimeOffset.TryParseExact(
            fileName.AsSpan(0, timestampLength),
            "yyyyMMdd'T'HHmmssfff'Z'",
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var parsedTimestamp))
        {
            return false;
        }

        var hash = fileName.AsSpan(timestampLength + 1, 64);
        foreach (var character in hash)
        {
            if (
                (character < '0' || character > '9')
                && (character < 'a' || character > 'f'))
            {
                return false;
            }
        }

        storedAtUtc = parsedTimestamp.ToString(
            RunReplay.CaptureTimestampFormat,
            CultureInfo.InvariantCulture);
        payloadHash = hash.ToString();
        return true;
    }

    private static ReplayListResult ListFailure(
        ReplayListCode code,
        string message) =>
        new(code, message, []);

    private static ReplayLoadResult NotFound(
        string message,
        string? fileName = null) =>
        new(ReplayLoadCode.NotFound, message, fileName);

    private static bool IsFileSystemFailure(Exception exception) =>
        exception is IOException
            or UnauthorizedAccessException
            or SecurityException
            or NotSupportedException;

    private static void TryDeleteTemporaryFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (IsFileSystemFailure(exception))
        {
            // Cleanup is best-effort after the primary result has already been determined.
        }
    }

    private static string ComputeFileSha256(string path, long maximumBytes)
    {
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            FileOptions.SequentialScan);
        if (stream.Length > maximumBytes)
        {
            throw new IOException("The file exceeds its bounded hashing limit.");
        }

        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
}
