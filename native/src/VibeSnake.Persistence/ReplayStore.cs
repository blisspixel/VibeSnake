using System.Diagnostics;
using System.Globalization;
using System.Security;
using System.Text;
using VibeSnake.Rules;

namespace VibeSnake.Persistence;

public sealed class ReplayStore
{
    public const string ReplayDirectoryName = "replays";
    public const string ReplayFileExtension = ".vibesnake-replay.json";
    public const int MaximumStoredReplays = 256;
    public const long MaximumStoredReplayBytes = 256L * 1024 * 1024;
    public const string StoreLockFileName = ".vibesnake-replay-store.lock";

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
        try
        {
            if (File.Exists(UserDataRoot))
            {
                return new ReplayLoadResult(
                    ReplayLoadCode.IoFailure,
                    "Saved replays could not be listed because the user-data root is not a directory.");
            }

            if (!Directory.Exists(ReplayDirectory))
            {
                return NotFound("No saved replays are available.");
            }

            var fileNames = Directory
                .EnumerateFiles(
                    ReplayDirectory,
                    $"*{ReplayFileExtension}",
                    SearchOption.TopDirectoryOnly)
                .Select(Path.GetFileName)
                .OfType<string>()
                .Take(MaximumStoredReplays + 1)
                .ToArray();
            if (fileNames.Length > MaximumStoredReplays)
            {
                return new ReplayLoadResult(
                    ReplayLoadCode.CapacityExceeded,
                    "Saved replays exceed the supported file-count limit; archive files before loading the latest replay.");
            }

            var fileName = fileNames
                .Where(IsGeneratedFileName)
                .OrderByDescending(value => value, StringComparer.Ordinal)
                .FirstOrDefault();
            return fileName is null
                ? NotFound("No saved replays are available.")
                : Load(fileName);
        }
        catch (Exception exception) when (IsFileSystemFailure(exception))
        {
            return new ReplayLoadResult(
                ReplayLoadCode.IoFailure,
                "Saved replays could not be listed because their storage directory is unavailable.");
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

    private ReplayLoadResult ReadFromPath(string path, string? fileName)
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

    private static bool IsGeneratedFileName(string fileName)
    {
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
            out _))
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

        return true;
    }

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
}
