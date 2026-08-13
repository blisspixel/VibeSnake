namespace VibeSnake.Rules;

public enum PowerKind : byte
{
    Shield = 1,
    PhaseShift = 2,
    LastStand = 3,
    SlowMo = 4,
    Boost = 5,
    Magnet = 6,
    Bait = 7,
    Gluttony = 8,
    SegmentDetach = 9,
}

public sealed record PowerPickup
{
    public PowerPickup(
        PowerKind kind,
        GridPoint position,
        int visibilityTicksRemaining)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(visibilityTicksRemaining);

        Kind = kind;
        Position = position;
        VisibilityTicksRemaining = visibilityTicksRemaining;
    }

    public PowerKind Kind { get; }

    public GridPoint Position { get; }

    public int VisibilityTicksRemaining { get; }
}
