namespace VibeSnake.Rules;

public sealed record RunSnapshot(
    int Tick,
    RunStatus Status,
    DeathCause DeathCause,
    Direction Direction,
    IReadOnlyList<GridPoint> Body,
    IReadOnlyList<Direction> PendingDirections,
    GridPoint? Food,
    int Score,
    int ComboCount,
    double ComboMultiplier,
    int TicksSinceLastFood,
    int HungerTicksRemaining,
    PowerPickup? PowerPickup,
    int PowerSpawnTicksElapsed,
    int ShieldTicksRemaining,
    int PhaseShiftTicksRemaining,
    bool LastStandHeld,
    int LastStandRecoveryTicksRemaining,
    int SlowMoTicksRemaining,
    int BoostTicksRemaining,
    int MagnetTicksRemaining,
    string StateHash)
{
    public GridPoint Head => Body[^1];

    public bool HasShield => ShieldTicksRemaining > 0;

    public bool HasPhaseShift => PhaseShiftTicksRemaining > 0;

    public bool HasLastStandRecovery => LastStandRecoveryTicksRemaining > 0;

    public bool HasSlowMo => SlowMoTicksRemaining > 0;

    public bool HasBoost => BoostTicksRemaining > 0;

    public bool HasMagnet => MagnetTicksRemaining > 0;

    /// <summary>
    /// Effective rules-tick scale as numerator/denominator: Slow-Mo multiplies by 2,
    /// Boost multiplies by 1/2, and both compose. Shells advance rules at
    /// <c>RulesTickMilliseconds * Numerator / Denominator</c> without changing step semantics.
    /// </summary>
    public int MovementCadenceNumerator => HasSlowMo ? 2 : 1;

    public int MovementCadenceDenominator => HasBoost ? 2 : 1;
}

public readonly struct RunStepResult : IEquatable<RunStepResult>
{
    private static readonly IReadOnlyList<RunEventDetail> EmptyEvents =
        Array.Empty<RunEventDetail>();

    private readonly IReadOnlyList<RunEventDetail>? _orderedEvents;

    public RunStepResult(
        int tick,
        RunEvent events,
        IReadOnlyList<RunEventDetail> orderedEvents,
        RunStatus status,
        DeathCause deathCause,
        string stateHash)
    {
        ArgumentNullException.ThrowIfNull(orderedEvents);
        ArgumentNullException.ThrowIfNull(stateHash);
        Tick = tick;
        Events = events;
        _orderedEvents = Array.AsReadOnly(orderedEvents.ToArray());
        Status = status;
        DeathCause = deathCause;
        StateHash = stateHash;
    }

    public int Tick { get; }

    public RunEvent Events { get; }

    public IReadOnlyList<RunEventDetail> OrderedEvents => _orderedEvents ?? EmptyEvents;

    public RunStatus Status { get; }

    public DeathCause DeathCause { get; }

    public string StateHash { get; }

    public bool Equals(RunStepResult other) =>
        Tick == other.Tick
        && Events == other.Events
        && Status == other.Status
        && DeathCause == other.DeathCause
        && StateHash == other.StateHash
        && OrderedEvents.SequenceEqual(other.OrderedEvents);

    public override bool Equals(object? obj) => obj is RunStepResult other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Tick);
        hash.Add(Events);
        hash.Add(Status);
        hash.Add(DeathCause);
        hash.Add(StateHash, StringComparer.Ordinal);
        foreach (var detail in OrderedEvents)
        {
            hash.Add(detail);
        }

        return hash.ToHashCode();
    }

    public static bool operator ==(RunStepResult left, RunStepResult right) => left.Equals(right);

    public static bool operator !=(RunStepResult left, RunStepResult right) => !left.Equals(right);
}
