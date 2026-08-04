namespace VibeSnake.Rules;

public enum PowerKind : byte
{
    Shield = 1,
    PhaseShift = 2,
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

        if (visibilityTicksRemaining <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(visibilityTicksRemaining));
        }

        Kind = kind;
        Position = position;
        VisibilityTicksRemaining = visibilityTicksRemaining;
    }

    public PowerKind Kind { get; }

    public GridPoint Position { get; }

    public int VisibilityTicksRemaining { get; }
}
