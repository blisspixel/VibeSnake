namespace VibeSnake.Rules;

public readonly record struct GridPoint(int X, int Y)
{
    public GridPoint Add(GridPoint offset) => new(X + offset.X, Y + offset.Y);

    public GridPoint Wrap(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        return new GridPoint(PositiveModulo(X, width), PositiveModulo(Y, height));
    }

    private static int PositiveModulo(int value, int modulus) => ((value % modulus) + modulus) % modulus;
}
