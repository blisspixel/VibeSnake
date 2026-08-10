using VibeSnake.Rules;

namespace VibeSnake.Persistence;

public enum ReplaySaveCode : byte
{
    Saved = 0,
    AlreadyExists = 1,
    ReplayInvalid = 2,
    CapacityReached = 3,
    IoFailure = 4,
    Busy = 5,
}

public sealed record ReplaySaveResult(
    ReplaySaveCode Code,
    string Message,
    string? FileName = null,
    ReplayVerificationResult? Verification = null)
{
    public bool IsSuccess =>
        Code is ReplaySaveCode.Saved or ReplaySaveCode.AlreadyExists;
}

public enum ReplayLoadCode : byte
{
    Loaded = 0,
    NotFound = 1,
    InvalidName = 2,
    TooLarge = 3,
    InvalidEncoding = 4,
    Incompatible = 5,
    VerificationFailed = 6,
    CapacityExceeded = 7,
    IoFailure = 8,
}

public sealed record ReplayLoadResult(
    ReplayLoadCode Code,
    string Message,
    string? FileName = null,
    ReplayCompatibility? Compatibility = null,
    ReplayVerificationResult? Verification = null,
    RunReplay? Replay = null)
{
    public bool IsSuccess => Code == ReplayLoadCode.Loaded;
}

public enum ReplayListCode : byte
{
    Listed = 0,
    CapacityExceeded = 1,
    IoFailure = 2,
}

public sealed record StoredReplaySummary(
    string FileName,
    string StoredAtUtc,
    string PayloadHash,
    long FileBytes);

public sealed record ReplayListResult(
    ReplayListCode Code,
    string Message,
    IReadOnlyList<StoredReplaySummary> Replays)
{
    public bool IsSuccess => Code == ReplayListCode.Listed;
}

public enum ReplayBrowserState : byte
{
    Verified = 0,
    Incompatible = 1,
    Modified = 2,
    Unreadable = 3,
}

public sealed record ReplayBrowserEntry(
    string ReplayId,
    string StoredAtUtc,
    string DisplayedAtUtc,
    long FileBytes,
    ReplayBrowserState State,
    string StatusCode,
    string StatusMessage,
    string? ModeId = null,
    int? ModeVersion = null,
    string? RulesetId = null,
    int? RulesVersion = null,
    int? Score = null,
    ulong? GameplaySeed = null,
    int? StepCount = null)
{
    public bool IsPlayable => State == ReplayBrowserState.Verified;
}

public sealed record ReplayBrowserResult(
    ReplayListCode Code,
    string Message,
    IReadOnlyList<ReplayBrowserEntry> Replays)
{
    public bool IsSuccess => Code == ReplayListCode.Listed;
}

public enum ReplayExportCode : byte
{
    Exported = 0,
    AlreadyExists = 1,
    NotFound = 2,
    InvalidReplayId = 3,
    ReplayUnavailable = 4,
    CapacityReached = 5,
    Busy = 6,
    IoFailure = 7,
}

public sealed record ReplayExportResult(
    ReplayExportCode Code,
    string Message,
    string? FileName = null,
    string? PayloadHash = null)
{
    public bool IsSuccess =>
        Code is ReplayExportCode.Exported or ReplayExportCode.AlreadyExists;
}

public enum ReplayCaptureSummaryExportCode : byte
{
    Exported = 0,
    AlreadyExists = 1,
    NotFound = 2,
    InvalidReplayId = 3,
    ReplayUnavailable = 4,
    CapacityReached = 5,
    Busy = 6,
    IoFailure = 7,
}

public sealed record ReplayCaptureSummaryExportResult(
    ReplayCaptureSummaryExportCode Code,
    string Message,
    string? FileName = null,
    string? Sha256 = null)
{
    public bool IsSuccess =>
        Code is ReplayCaptureSummaryExportCode.Exported
            or ReplayCaptureSummaryExportCode.AlreadyExists;
}

public enum ReplayDeletionPlanCode : byte
{
    Ready = 0,
    NotFound = 1,
    InvalidReplayId = 2,
    IoFailure = 3,
}

public sealed record ReplayDeletionPlan(
    string ReplayId,
    string StoredAtUtc,
    long FileBytes,
    string ContentSha256,
    string ConfirmationText);

public sealed record ReplayDeletionPlanResult(
    ReplayDeletionPlanCode Code,
    string Message,
    ReplayDeletionPlan? Plan = null)
{
    public bool IsSuccess => Code == ReplayDeletionPlanCode.Ready && Plan is not null;
}

public enum ReplayDeleteCode : byte
{
    Deleted = 0,
    NotFound = 1,
    InvalidPlan = 2,
    ChangedSinceConsent = 3,
    Busy = 4,
    IoFailure = 5,
}

public sealed record ReplayDeleteResult(
    ReplayDeleteCode Code,
    string Message)
{
    public bool IsSuccess => Code == ReplayDeleteCode.Deleted;
}
