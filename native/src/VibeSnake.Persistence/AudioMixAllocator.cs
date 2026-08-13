namespace VibeSnake.Persistence;

public enum AudioMixDecisionCode : byte
{
    Granted = 0,
    GrantedWithInterruption = 1,
    SuppressedByCooldown = 2,
    SuppressedByPolyphony = 3,
    SuppressedByPriority = 4,
    InvalidRequest = 5,
}

public sealed record AudioMixRequest(
    string CueId,
    string Bus,
    int Priority,
    long RequestedAtMilliseconds,
    int CooldownMilliseconds,
    int MaximumPolyphony,
    int ExpectedDurationMilliseconds,
    bool MayInterruptLowerPriority,
    float MusicDuckDecibels,
    string? CooldownGroup = null);

public sealed record AudioVoiceLease(
    long LeaseId,
    string CueId,
    string Bus,
    int Priority,
    long GrantedAtMilliseconds,
    long ExpiresAtMilliseconds,
    float MusicDuckDecibels);

public sealed record AudioMixDecision(
    AudioMixDecisionCode Code,
    string Message,
    AudioVoiceLease? Lease = null,
    IReadOnlyList<long>? InterruptedLeaseIds = null)
{
    public bool IsGranted => Code is AudioMixDecisionCode.Granted
        or AudioMixDecisionCode.GrantedWithInterruption;

    public IReadOnlyList<long> Interrupted => InterruptedLeaseIds ?? Array.Empty<long>();
}

public sealed record AudioMixAdvanceResult(
    IReadOnlyList<long> ExpiredLeaseIds,
    float EffectiveMusicDuckDecibels);

/// <summary>
/// Clock-injected allocation policy for optional audio voices. It never plays
/// audio and is deterministic for a request sequence, so cooldown, capacity,
/// priority, interruption, expiry, and ducking can be tested without a mixer.
/// </summary>
public sealed class AudioMixAllocator
{
    public const int MaximumBusCount = 32;
    public const int MaximumSupportedBusVoices = 64;
    public const int MaximumIdentifierCharacters = 128;
    public const int MaximumCooldownGroupCount = 256;
    public const int MaximumCooldownMilliseconds = 10_000;
    public const int MaximumExpectedDurationMilliseconds = 60_000;
    public const float MinimumMusicDuckDecibels = -12.0f;

    private readonly Dictionary<string, int> _busCapacities;
    private readonly Dictionary<long, AudioVoiceLease> _active = [];
    private readonly Dictionary<string, long> _lastGrantByCooldownGroup =
        new(StringComparer.Ordinal);
    private long _nextLeaseId = 1;
    private long _lastObservedMilliseconds = -1;

    public AudioMixAllocator(IReadOnlyDictionary<string, int> busCapacities)
    {
        ArgumentNullException.ThrowIfNull(busCapacities);
        if (busCapacities.Count is <= 0 or > MaximumBusCount
            || busCapacities.Any(pair =>
                string.IsNullOrWhiteSpace(pair.Key)
                || pair.Key.Length > MaximumIdentifierCharacters
                || pair.Value <= 0
                || pair.Value > MaximumSupportedBusVoices))
        {
            throw new ArgumentException(
                "Audio bus capacities must contain bounded named buses.",
                nameof(busCapacities));
        }

        _busCapacities = new Dictionary<string, int>(busCapacities, StringComparer.Ordinal);
    }

    public int ActiveVoiceCount => _active.Count;

    public float EffectiveMusicDuckDecibels => _active.Count == 0
        ? 0.0f
        : _active.Values.Min(lease => lease.MusicDuckDecibels);

    public IReadOnlyList<AudioVoiceLease> ActiveLeases => _active.Values
        .OrderBy(lease => lease.LeaseId)
        .ToArray();

    public AudioMixDecision Request(AudioMixRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!IsValid(request, out var failure))
        {
            return new AudioMixDecision(AudioMixDecisionCode.InvalidRequest, failure);
        }

        _ = Advance(request.RequestedAtMilliseconds);
        var cooldownGroup = request.CooldownGroup ?? request.CueId;
        if (!_lastGrantByCooldownGroup.ContainsKey(cooldownGroup)
            && _lastGrantByCooldownGroup.Count >= MaximumCooldownGroupCount)
        {
            return new AudioMixDecision(
                AudioMixDecisionCode.InvalidRequest,
                "Audio cooldown-group capacity was reached.");
        }

        if (_lastGrantByCooldownGroup.TryGetValue(cooldownGroup, out var lastGrant)
            && request.RequestedAtMilliseconds - lastGrant < request.CooldownMilliseconds)
        {
            return new AudioMixDecision(
                AudioMixDecisionCode.SuppressedByCooldown,
                "Cue was suppressed by its cooldown group.");
        }

        var interrupted = new List<long>(1);
        var sameCue = _active.Values
            .Where(lease => string.Equals(lease.CueId, request.CueId, StringComparison.Ordinal))
            .OrderBy(lease => lease.Priority)
            .ThenBy(lease => lease.GrantedAtMilliseconds)
            .ThenBy(lease => lease.LeaseId)
            .ToArray();
        if (sameCue.Length >= request.MaximumPolyphony)
        {
            if (!TryInterrupt(request, sameCue[0], interrupted))
            {
                return new AudioMixDecision(
                    AudioMixDecisionCode.SuppressedByPolyphony,
                    "Cue was suppressed by its per-cue polyphony limit.");
            }
        }

        var busVoices = _active.Values
            .Where(lease => string.Equals(lease.Bus, request.Bus, StringComparison.Ordinal))
            .OrderBy(lease => lease.Priority)
            .ThenBy(lease => lease.GrantedAtMilliseconds)
            .ThenBy(lease => lease.LeaseId)
            .ToArray();
        if (busVoices.Length >= _busCapacities[request.Bus])
        {
            if (!TryInterrupt(request, busVoices[0], interrupted))
            {
                return new AudioMixDecision(
                    AudioMixDecisionCode.SuppressedByPriority,
                    "Cue was suppressed because its bus is full of equal or higher priority voices.");
            }
        }

        var lease = new AudioVoiceLease(
            _nextLeaseId++,
            request.CueId,
            request.Bus,
            request.Priority,
            request.RequestedAtMilliseconds,
            checked(request.RequestedAtMilliseconds + request.ExpectedDurationMilliseconds),
            request.MusicDuckDecibels);
        _active.Add(lease.LeaseId, lease);
        _lastGrantByCooldownGroup[cooldownGroup] = request.RequestedAtMilliseconds;
        return new AudioMixDecision(
            interrupted.Count == 0
                ? AudioMixDecisionCode.Granted
                : AudioMixDecisionCode.GrantedWithInterruption,
            interrupted.Count == 0
                ? "Cue voice granted."
                : "Cue voice granted after interrupting a lower priority voice.",
            lease,
            interrupted.ToArray());
    }

    public AudioMixAdvanceResult Advance(long nowMilliseconds)
    {
        if (nowMilliseconds < 0 || nowMilliseconds < _lastObservedMilliseconds)
        {
            throw new ArgumentOutOfRangeException(
                nameof(nowMilliseconds),
                "Audio mix time must be nonnegative and monotonic.");
        }

        _lastObservedMilliseconds = nowMilliseconds;
        var expired = _active.Values
            .Where(lease => lease.ExpiresAtMilliseconds <= nowMilliseconds)
            .Select(lease => lease.LeaseId)
            .OrderBy(id => id)
            .ToArray();
        foreach (var leaseId in expired)
        {
            _active.Remove(leaseId);
        }

        return new AudioMixAdvanceResult(expired, EffectiveMusicDuckDecibels);
    }

    public bool Release(long leaseId) => _active.Remove(leaseId);

    public void Reset()
    {
        _active.Clear();
        _lastGrantByCooldownGroup.Clear();
        _lastObservedMilliseconds = -1;
        _nextLeaseId = 1;
    }

    private bool TryInterrupt(
        AudioMixRequest request,
        AudioVoiceLease candidate,
        List<long> interrupted)
    {
        if (!request.MayInterruptLowerPriority || request.Priority <= candidate.Priority)
        {
            return false;
        }

        if (_active.Remove(candidate.LeaseId))
        {
            interrupted.Add(candidate.LeaseId);
        }

        return true;
    }

    private bool IsValid(AudioMixRequest request, out string failure)
    {
        if (string.IsNullOrWhiteSpace(request.CueId)
            || request.CueId.Length > MaximumIdentifierCharacters
            || string.IsNullOrWhiteSpace(request.Bus)
            || request.Bus.Length > MaximumIdentifierCharacters
            || !_busCapacities.ContainsKey(request.Bus)
            || request.Priority is < 0 or > 100
            || request.RequestedAtMilliseconds < 0
            || request.RequestedAtMilliseconds < _lastObservedMilliseconds
            || request.CooldownMilliseconds is < 0 or > MaximumCooldownMilliseconds
            || request.MaximumPolyphony is <= 0 or > MaximumSupportedBusVoices
            || request.ExpectedDurationMilliseconds is <= 0
                or > MaximumExpectedDurationMilliseconds
            || request.RequestedAtMilliseconds
                > long.MaxValue - request.ExpectedDurationMilliseconds
            || request.MusicDuckDecibels is < MinimumMusicDuckDecibels or > 0.0f
            || request.CooldownGroup is not null
                && (string.IsNullOrWhiteSpace(request.CooldownGroup)
                    || request.CooldownGroup.Length > MaximumIdentifierCharacters))
        {
            failure = "Audio mix request contains an invalid cue, bus, time, range, or group.";
            return false;
        }

        failure = string.Empty;
        return true;
    }
}
