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

    public static int WrapManhattanDistance(GridPoint left, GridPoint right, int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        var deltaX = Math.Abs(left.X - right.X);
        var deltaY = Math.Abs(left.Y - right.Y);
        if (deltaX >= width || deltaY >= height)
        {
            throw new ArgumentOutOfRangeException(
                nameof(right),
                "Distance points must be inside the wrapped board.");
        }

        return Math.Min(deltaX, width - deltaX) + Math.Min(deltaY, height - deltaY);
    }

    public static bool LiesOnWrapManhattanGeodesic(
        GridPoint start,
        GridPoint via,
        GridPoint end,
        int width,
        int height) =>
        WrapManhattanDistance(start, via, width, height)
            + WrapManhattanDistance(via, end, width, height)
            == WrapManhattanDistance(start, end, width, height);

    private static int PositiveModulo(int value, int modulus) => ((value % modulus) + modulus) % modulus;
}
