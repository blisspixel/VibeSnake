namespace VibeSnake.Persistence;

/// <summary>
/// Describes a player-visible transition in optional audio availability.
/// Audio failures never affect deterministic rules or block shell input.
/// </summary>
public enum AudioOutputRecoveryKind : byte
{
    Unavailable = 0,
    Recovered = 1,
}

public readonly record struct AudioOutputRecoveryEvent(
    AudioOutputRecoveryKind Kind,
    int ConsecutiveFailures,
    string FailureReason,
    string Caption);

/// <summary>
/// Pure, monotonic-time recovery policy for an optional audio output.
/// Presentation layers own device probing and playback, while this type owns
/// bounded retry timing, transition dedupe, and safe player-facing captions.
/// </summary>
public sealed class AudioOutputRecoveryTracker
{
    public const ulong RetryDelayMilliseconds = 1_000UL;
    public const int MaximumFailureReasonCharacters = 160;

    public bool IsAvailable { get; private set; } = true;

    public int ConsecutiveFailures { get; private set; }

    public ulong RetryAtMilliseconds { get; private set; }

    public string LastFailureReason { get; private set; } = string.Empty;

    public bool ShouldAttemptPlayback(ulong nowMilliseconds) =>
        IsAvailable || nowMilliseconds >= RetryAtMilliseconds;

    /// <summary>
    /// Records a failed playback attempt. Only the transition from available
    /// to unavailable emits an event so repeated failures cannot flood UI or logs.
    /// </summary>
    public AudioOutputRecoveryEvent? NoteFailure(
        ulong nowMilliseconds,
        string? failureReason)
    {
        var wasAvailable = IsAvailable;
        IsAvailable = false;
        ConsecutiveFailures = ConsecutiveFailures == int.MaxValue
            ? int.MaxValue
            : ConsecutiveFailures + 1;
        RetryAtMilliseconds = nowMilliseconds > ulong.MaxValue - RetryDelayMilliseconds
            ? ulong.MaxValue
            : nowMilliseconds + RetryDelayMilliseconds;
        LastFailureReason = NormalizeFailureReason(failureReason);

        return wasAvailable
            ? new AudioOutputRecoveryEvent(
                AudioOutputRecoveryKind.Unavailable,
                ConsecutiveFailures,
                LastFailureReason,
                "AUDIO UNAVAILABLE: VISUAL CUES ACTIVE")
            : null;
    }

    /// <summary>
    /// Records a successful playback attempt after an outage. Returns null
    /// when audio was already available.
    /// </summary>
    public AudioOutputRecoveryEvent? NoteSuccess()
    {
        if (IsAvailable)
        {
            return null;
        }

        var previousFailures = ConsecutiveFailures;
        var previousReason = LastFailureReason;
        IsAvailable = true;
        ConsecutiveFailures = 0;
        RetryAtMilliseconds = 0;
        LastFailureReason = string.Empty;
        return new AudioOutputRecoveryEvent(
            AudioOutputRecoveryKind.Recovered,
            previousFailures,
            previousReason,
            "AUDIO RESTORED");
    }

    private static string NormalizeFailureReason(string? failureReason)
    {
        if (string.IsNullOrWhiteSpace(failureReason))
        {
            return "Audio output unavailable.";
        }

        var trimmed = failureReason.Trim();
        var buffer = new char[Math.Min(trimmed.Length, MaximumFailureReasonCharacters)];
        var length = 0;
        foreach (var character in trimmed)
        {
            if (length >= buffer.Length)
            {
                break;
            }

            if (!char.IsControl(character))
            {
                buffer[length++] = character;
            }
        }

        return length == 0
            ? "Audio output unavailable."
            : new string(buffer, 0, length);
    }
}
