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
