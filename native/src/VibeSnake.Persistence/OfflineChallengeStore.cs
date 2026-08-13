using System.Diagnostics;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using VibeSnake.Rules;

namespace VibeSnake.Persistence;

public enum GhostImportCode : byte
{
    Imported = 0,
    InvalidSlot = 1,
    SlotOccupied = 2,
    SourceNotFound = 3,
    InvalidSource = 4,
    SourceTooLarge = 5,
    Incompatible = 6,
    Modified = 7,
    ChallengeUnavailable = 8,
    SourceChanged = 9,
    Busy = 10,
    IoFailure = 11,
}

public sealed record GhostImportResult(
    GhostImportCode Code,
    string Message,
    int? Slot = null,
    string? ReplayId = null,
    string? SeedCode = null)
{
    public bool IsSuccess => Code == GhostImportCode.Imported;
}

public enum GhostSlotState : byte
{
    Empty = 0,
    Verified = 1,
    Incompatible = 2,
    Modified = 3,
    Unreadable = 4,
}

public sealed record GhostSlotEntry(
    int Slot,
    string DisplayName,
    GhostSlotState State,
    string StatusCode,
    string StatusMessage,
    string? ReplayId = null,
    string? SeedCode = null,
    string? ModeId = null,
    int? ModeVersion = null,
    ulong? GameplaySeed = null,
    int? Score = null,
    int? StepCount = null)
{
    public bool IsPlayable => State == GhostSlotState.Verified;
}

public sealed record GhostSlotListResult(
    bool IsSuccess,
    string Message,
    IReadOnlyList<GhostSlotEntry> Slots);

public enum GhostDeletionPlanCode : byte
{
    Ready = 0,
    InvalidSlot = 1,
    Empty = 2,
    IoFailure = 3,
}

public sealed record GhostDeletionPlan(
    int Slot,
    long FileBytes,
    string ContentSha256,
    string ConfirmationText);

public sealed record GhostDeletionPlanResult(
    GhostDeletionPlanCode Code,
    string Message,
    GhostDeletionPlan? Plan = null)
{
    public bool IsSuccess => Code == GhostDeletionPlanCode.Ready && Plan is not null;
}

public enum GhostDeleteCode : byte
{
    Deleted = 0,
    InvalidPlan = 1,
    Empty = 2,
    ChangedSinceConsent = 3,
    Busy = 4,
    IoFailure = 5,
}

public sealed record GhostDeleteResult(GhostDeleteCode Code, string Message)
{
    public bool IsSuccess => Code == GhostDeleteCode.Deleted;
}

public enum RunCardExportCode : byte
{
    Exported = 0,
    AlreadyExists = 1,
    InvalidSlot = 2,
    GhostUnavailable = 3,
    CapacityReached = 4,
    Busy = 5,
    IoFailure = 6,
}

public sealed record RunCardExportResult(
    RunCardExportCode Code,
    string Message,
    string? FileName = null,
    string? Sha256 = null,
    OfflineRunCard? Card = null)
{
    public bool IsSuccess => Code is RunCardExportCode.Exported or RunCardExportCode.AlreadyExists;
}

/// <summary>
/// Four fixed household rival slots backed by verified replay copies. Imports
/// are explicit, bounded, source-preserving, atomic, and never overwrite a slot.
/// </summary>
public sealed class OfflineChallengeStore
{
    public const string DirectoryName = "offline-challenges";
    public const string GhostDirectoryName = "ghosts";
    public const string RunCardDirectoryName = "run-cards";
    public const string GhostFileExtension = ".vibesnake-ghost.json";
    public const string RunCardFileExtension = ".vibesnake-run-card.json";
    public const string StoreLockFileName = ".vibesnake-offline-challenge.lock";
    public const int MaximumHouseholdRivalSlots = 4;
    public const int MaximumRunCards = 64;
    public const long MaximumRunCardBytes = 4L * 1024 * 1024;

    private static readonly TimeSpan DefaultLockWait = TimeSpan.FromSeconds(2);
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);
    private readonly ReplayStore _replayReader;
    private readonly TimeSpan _lockWait;

    public OfflineChallengeStore(string userDataRoot, TimeSpan? lockWait = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userDataRoot);
        if (!Path.IsPathFullyQualified(userDataRoot))
        {
            throw new ArgumentException("The user-data root must be absolute.", nameof(userDataRoot));
        }

        UserDataRoot = Path.GetFullPath(userDataRoot);
        ChallengeDirectory = Path.Combine(UserDataRoot, DirectoryName);
        GhostDirectory = Path.Combine(ChallengeDirectory, GhostDirectoryName);
        RunCardDirectory = Path.Combine(ChallengeDirectory, RunCardDirectoryName);
        _replayReader = new ReplayStore(UserDataRoot);
        _lockWait = lockWait ?? DefaultLockWait;
        if (_lockWait <= TimeSpan.Zero || _lockWait > TimeSpan.FromSeconds(30))
        {
            throw new ArgumentOutOfRangeException(nameof(lockWait));
        }
    }

    public string UserDataRoot { get; }

    public string ChallengeDirectory { get; }

    public string GhostDirectory { get; }

    public string RunCardDirectory { get; }

    public GhostImportResult ImportGhost(string absoluteSourcePath, int slot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(absoluteSourcePath);
        if (!IsValidSlot(slot))
        {
            return new GhostImportResult(
                GhostImportCode.InvalidSlot,
                "A household rival slot must be between 1 and 4.");
        }

        if (!Path.IsPathFullyQualified(absoluteSourcePath))
        {
            return new GhostImportResult(
                GhostImportCode.InvalidSource,
                "A ghost import source must be an absolute file path.");
        }

        string source;
        try
        {
            source = Path.GetFullPath(absoluteSourcePath);
            if (!File.Exists(source))
            {
                return new GhostImportResult(
                    GhostImportCode.SourceNotFound,
                    "The selected ghost source does not exist.");
            }

            var sourceInfo = new FileInfo(source);
            if (sourceInfo.Length > RunReplay.MaximumSerializedCharacters)
            {
                return new GhostImportResult(
                    GhostImportCode.SourceTooLarge,
                    "The selected ghost exceeds the replay import size limit.");
            }

            var sourceHashBefore = ComputeFileSha256(source, RunReplay.MaximumSerializedCharacters);
            var loaded = _replayReader.InspectExternal(source);
            if (!loaded.IsSuccess || loaded.Replay is null)
            {
                return ImportFailure(loaded);
            }

            SeedChallengeDescriptor challenge;
            try
            {
                challenge = SeedChallengeDescriptor.Create(loaded.Replay);
            }
            catch (ArgumentException)
            {
                return new GhostImportResult(
                    GhostImportCode.ChallengeUnavailable,
                    "The replay is verified but lacks a supported stable seed challenge.");
            }

            var bytes = StrictUtf8.GetBytes(loaded.Replay.Serialize());
            var sourceHashAfter = ComputeFileSha256(source, RunReplay.MaximumSerializedCharacters);
            if (!string.Equals(sourceHashBefore, sourceHashAfter, StringComparison.Ordinal))
            {
                return new GhostImportResult(
                    GhostImportCode.SourceChanged,
                    "The source changed during validation; no household slot was written.");
            }

            Directory.CreateDirectory(ChallengeDirectory);
            Directory.CreateDirectory(GhostDirectory);
            using var storeLock = TryAcquireLock();
            if (storeLock is null)
            {
                return new GhostImportResult(
                    GhostImportCode.Busy,
                    "The offline challenge library is busy; retry the explicit import.");
            }

            var destination = SlotPath(slot);
            if (File.Exists(destination))
            {
                return new GhostImportResult(
                    GhostImportCode.SlotOccupied,
                    "The selected household rival slot is occupied; delete it explicitly before importing.",
                    slot);
            }

            var temporary = destination + $".tmp-{Guid.NewGuid():N}";
            try
            {
                using (var stream = new FileStream(
                    temporary,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    bufferSize: 64 * 1024,
                    FileOptions.WriteThrough))
                {
                    stream.Write(bytes);
                    stream.Flush(flushToDisk: true);
                }

                File.Move(temporary, destination, overwrite: false);
            }
            finally
            {
                TryDeleteTemporary(temporary);
            }

            return new GhostImportResult(
                GhostImportCode.Imported,
                "The verified replay was copied into a household rival slot; the source was preserved.",
                slot,
                loaded.Replay.PayloadHash,
                challenge.Encode());
        }
        catch (Exception exception) when (IsFileSystemFailure(exception))
        {
            return new GhostImportResult(
                GhostImportCode.IoFailure,
                "The ghost import could not be completed; source and existing slots were preserved.");
        }
    }

    public GhostSlotListResult ListSlots()
    {
        var slots = new List<GhostSlotEntry>(MaximumHouseholdRivalSlots);
        try
        {
            for (var slot = 1; slot <= MaximumHouseholdRivalSlots; slot++)
            {
                var path = SlotPath(slot);
                if (!File.Exists(path))
                {
                    slots.Add(EmptySlot(slot));
                    continue;
                }

                var loaded = _replayReader.InspectExternal(path);
                slots.Add(ProjectSlot(slot, loaded));
            }

            return new GhostSlotListResult(true, "Household rival slots were inspected.", slots);
        }
        catch (Exception exception) when (IsFileSystemFailure(exception))
        {
            return new GhostSlotListResult(
                false,
                "Household rival slots could not be inspected.",
                Enumerable.Range(1, MaximumHouseholdRivalSlots).Select(EmptySlot).ToArray());
        }
    }

    public ReplayLoadResult LoadGhost(int slot)
    {
        if (!IsValidSlot(slot))
        {
            return new ReplayLoadResult(
                ReplayLoadCode.InvalidName,
                "A household rival slot must be between 1 and 4.");
        }

        return _replayReader.InspectExternal(SlotPath(slot));
    }

    public GhostDeletionPlanResult PlanDeletion(int slot)
    {
        if (!IsValidSlot(slot))
        {
            return new GhostDeletionPlanResult(
                GhostDeletionPlanCode.InvalidSlot,
                "A household rival slot must be between 1 and 4.");
        }

        var path = SlotPath(slot);
        try
        {
            if (!File.Exists(path))
            {
                return new GhostDeletionPlanResult(
                    GhostDeletionPlanCode.Empty,
                    "The household rival slot is already empty.");
            }

            var info = new FileInfo(path);
            var plan = new GhostDeletionPlan(
                slot,
                info.Length,
                ComputeFileSha256(path, RunReplay.MaximumSerializedCharacters),
                $"Permanently delete household rival slot {slot}? The imported source is not affected.");
            return new GhostDeletionPlanResult(
                GhostDeletionPlanCode.Ready,
                "Ghost deletion requires a separate confirmation.",
                plan);
        }
        catch (Exception exception) when (IsFileSystemFailure(exception))
        {
            return new GhostDeletionPlanResult(
                GhostDeletionPlanCode.IoFailure,
                "The household rival slot could not be inspected; nothing was deleted.");
        }
    }

    public GhostDeleteResult Delete(GhostDeletionPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (!IsValidSlot(plan.Slot)
            || plan.FileBytes < 0
            || !IsLowerHex(plan.ContentSha256, 64))
        {
            return new GhostDeleteResult(
                GhostDeleteCode.InvalidPlan,
                "The ghost deletion plan is invalid; nothing was deleted.");
        }

        var path = SlotPath(plan.Slot);
        try
        {
            Directory.CreateDirectory(ChallengeDirectory);
            using var storeLock = TryAcquireLock();
            if (storeLock is null)
            {
                return new GhostDeleteResult(
                    GhostDeleteCode.Busy,
                    "The offline challenge library is busy; review deletion again before retrying.");
            }

            if (!File.Exists(path))
            {
                return new GhostDeleteResult(
                    GhostDeleteCode.Empty,
                    "The household rival slot is already empty.");
            }

            var info = new FileInfo(path);
            if (info.Length != plan.FileBytes
                || !string.Equals(
                    ComputeFileSha256(path, RunReplay.MaximumSerializedCharacters),
                    plan.ContentSha256,
                    StringComparison.Ordinal))
            {
                return new GhostDeleteResult(
                    GhostDeleteCode.ChangedSinceConsent,
                    "The household rival changed after confirmation was prepared; nothing was deleted.");
            }

            File.Delete(path);
            return new GhostDeleteResult(
                GhostDeleteCode.Deleted,
                "The selected household rival was deleted. Its original import source was not affected.");
        }
        catch (Exception exception) when (IsFileSystemFailure(exception))
        {
            return new GhostDeleteResult(
                GhostDeleteCode.IoFailure,
                "The household rival could not be deleted; its current slot was preserved.");
        }
    }

    public RunCardExportResult ExportRunCard(
        int slot,
        string exportingAppVersion,
        string stationId,
        string selectedLookId)
    {
        if (!IsValidSlot(slot))
        {
            return new RunCardExportResult(
                RunCardExportCode.InvalidSlot,
                "A household rival slot must be between 1 and 4.");
        }

        var loaded = LoadGhost(slot);
        if (!loaded.IsSuccess || loaded.Replay is null)
        {
            return new RunCardExportResult(
                RunCardExportCode.GhostUnavailable,
                "A verified household rival is required for run-card export.");
        }

        try
        {
            var challenge = SeedChallengeDescriptor.Create(loaded.Replay);
            var card = OfflineRunCard.Create(
                loaded.Replay,
                challenge,
                exportingAppVersion,
                stationId,
                selectedLookId);
            var bytes = StrictUtf8.GetBytes(card.Serialize());
            var fileName = $"run-card_{loaded.Replay.PayloadHash}{RunCardFileExtension}";
            var destination = Path.Combine(RunCardDirectory, fileName);
            Directory.CreateDirectory(ChallengeDirectory);
            Directory.CreateDirectory(RunCardDirectory);
            using var storeLock = TryAcquireLock();
            if (storeLock is null)
            {
                return new RunCardExportResult(
                    RunCardExportCode.Busy,
                    "The offline challenge library is busy; retry run-card export.",
                    fileName);
            }

            var cards = Directory
                .EnumerateFiles(RunCardDirectory, $"*{RunCardFileExtension}", SearchOption.TopDirectoryOnly)
                .Take(MaximumRunCards + 1)
                .Select(path => new FileInfo(path))
                .ToArray();
            var totalBytes = cards.Sum(cardFile => cardFile.Length);
            if (cards.Length > MaximumRunCards
                || totalBytes > MaximumRunCardBytes
                || bytes.Length > MaximumRunCardBytes - totalBytes)
            {
                return new RunCardExportResult(
                    RunCardExportCode.CapacityReached,
                    "The bounded run-card library is full; existing files were preserved.",
                    fileName);
            }

            var sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            if (File.Exists(destination))
            {
                return FileContentEquals(destination, bytes)
                    ? new RunCardExportResult(
                        RunCardExportCode.AlreadyExists,
                        "The identical privacy-safe run card is already exported.",
                        fileName,
                        sha256,
                        card)
                    : new RunCardExportResult(
                        RunCardExportCode.IoFailure,
                        "The run-card destination contains different data; nothing was overwritten.",
                        fileName);
            }

            if (cards.Length >= MaximumRunCards)
            {
                return new RunCardExportResult(
                    RunCardExportCode.CapacityReached,
                    "The bounded run-card library is full; existing files were preserved.",
                    fileName);
            }

            var temporary = destination + $".tmp-{Guid.NewGuid():N}";
            try
            {
                using (var stream = new FileStream(
                    temporary,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    bufferSize: 16 * 1024,
                    FileOptions.WriteThrough))
                {
                    stream.Write(bytes);
                    stream.Flush(flushToDisk: true);
                }

                File.Move(temporary, destination, overwrite: false);
            }
            finally
            {
                TryDeleteTemporary(temporary);
            }

            return new RunCardExportResult(
                RunCardExportCode.Exported,
                "A privacy-safe offline run card was exported atomically.",
                fileName,
                sha256,
                card);
        }
        catch (Exception exception) when (
            IsFileSystemFailure(exception)
                || exception is ArgumentException
                or InvalidOperationException)
        {
            return new RunCardExportResult(
                RunCardExportCode.IoFailure,
                "The run card could not be exported; existing files were preserved.");
        }
    }

    private static GhostSlotEntry ProjectSlot(int slot, ReplayLoadResult loaded)
    {
        if (loaded.IsSuccess && loaded.Replay is { } replay)
        {
            try
            {
                var challenge = SeedChallengeDescriptor.Create(replay);
                var initial = SnakeRun.RestoreCanonicalState(replay.InitialCanonicalState);
                return new GhostSlotEntry(
                    slot,
                    DisplayName(slot),
                    GhostSlotState.Verified,
                    "verified",
                    "Verified equal-rules household rival.",
                    replay.PayloadHash,
                    challenge.Encode(),
                    initial.Configuration.ModeId,
                    initial.Configuration.ModeVersion,
                    replay.GameplaySeed,
                    replay.Outcome.Score,
                    replay.Outcome.StepCount);
            }
            catch (ArgumentException)
            {
                return new GhostSlotEntry(
                    slot,
                    DisplayName(slot),
                    GhostSlotState.Incompatible,
                    "challenge-unavailable",
                    "The replay cannot produce a supported stable seed challenge.");
            }
        }

        var modified = loaded.Compatibility?.Code == ReplayCompatibilityCode.IntegrityMismatch
            || loaded.Code == ReplayLoadCode.VerificationFailed;
        return new GhostSlotEntry(
            slot,
            DisplayName(slot),
            modified
                ? GhostSlotState.Modified
                : loaded.Code == ReplayLoadCode.Incompatible
                    ? GhostSlotState.Incompatible
                    : GhostSlotState.Unreadable,
            loaded.Code.ToString().ToLowerInvariant(),
            loaded.Message);
    }

    private static GhostImportResult ImportFailure(ReplayLoadResult loaded)
    {
        var integrityMismatch = loaded.Compatibility?.Code == ReplayCompatibilityCode.IntegrityMismatch;
        return loaded.Code switch
        {
            ReplayLoadCode.NotFound => new GhostImportResult(
                GhostImportCode.SourceNotFound,
                "The selected ghost source does not exist."),
            ReplayLoadCode.TooLarge => new GhostImportResult(
                GhostImportCode.SourceTooLarge,
                "The selected ghost exceeds the replay import size limit."),
            ReplayLoadCode.VerificationFailed => new GhostImportResult(
                GhostImportCode.Modified,
                "The selected ghost failed deterministic verification; the source was preserved."),
            ReplayLoadCode.Incompatible when integrityMismatch => new GhostImportResult(
                GhostImportCode.Modified,
                "The selected ghost failed integrity verification; the source was preserved."),
            ReplayLoadCode.Incompatible => new GhostImportResult(
                GhostImportCode.Incompatible,
                "The selected ghost uses unsupported rules or schema; the source was preserved."),
            _ => new GhostImportResult(
                GhostImportCode.InvalidSource,
                "The selected ghost is unreadable; the source was preserved."),
        };
    }

    private FileStream? TryAcquireLock()
    {
        var path = Path.Combine(ChallengeDirectory, StoreLockFileName);
        var stopwatch = Stopwatch.StartNew();
        while (true)
        {
            try
            {
                return new FileStream(
                    path,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    bufferSize: 1,
                    FileOptions.None);
            }
            catch (IOException) when (stopwatch.Elapsed < _lockWait)
            {
                Thread.Sleep(10);
            }
            catch (IOException)
            {
                return null;
            }
        }
    }

    private string SlotPath(int slot) =>
        Path.Combine(GhostDirectory, $"household-rival-{slot}{GhostFileExtension}");

    private static bool IsValidSlot(int slot) => slot is >= 1 and <= MaximumHouseholdRivalSlots;

    private static GhostSlotEntry EmptySlot(int slot) => new(
        slot,
        DisplayName(slot),
        GhostSlotState.Empty,
        "empty",
        "No household rival is stored in this slot.");

    private static string DisplayName(int slot) => $"HOUSEHOLD RIVAL {slot}";

    private static bool IsLowerHex(string value, int length) =>
        value.Length == length
        && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

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

    private static bool FileContentEquals(string path, ReadOnlySpan<byte> expected)
    {
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 16 * 1024,
            FileOptions.SequentialScan);
        if (stream.Length != expected.Length)
        {
            return false;
        }

        var buffer = new byte[16 * 1024];
        var offset = 0;
        while (offset < expected.Length)
        {
            var count = stream.Read(buffer, 0, Math.Min(buffer.Length, expected.Length - offset));
            if (count == 0 || !buffer.AsSpan(0, count).SequenceEqual(expected[offset..(offset + count)]))
            {
                return false;
            }

            offset += count;
        }

        return true;
    }

    private static bool IsFileSystemFailure(Exception exception) =>
        exception is IOException
            or UnauthorizedAccessException
            or SecurityException
            or NotSupportedException;

    private static void TryDeleteTemporary(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (IsFileSystemFailure(exception))
        {
            // Best effort after the primary operation result is known.
        }
    }
}
