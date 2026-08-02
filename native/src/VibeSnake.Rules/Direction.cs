namespace VibeSnake.Rules;

public enum Direction : byte
{
    Up = 0,
    Right = 1,
    Down = 2,
    Left = 3,
}

public static class DirectionExtensions
{
    public static Direction Opposite(this Direction direction) => direction switch
    {
        Direction.Up => Direction.Down,
        Direction.Right => Direction.Left,
        Direction.Down => Direction.Up,
        Direction.Left => Direction.Right,
        _ => throw new ArgumentOutOfRangeException(nameof(direction), direction, "Unknown direction."),
    };

    public static GridPoint Offset(this Direction direction) => direction switch
    {
        Direction.Up => new GridPoint(0, -1),
        Direction.Right => new GridPoint(1, 0),
        Direction.Down => new GridPoint(0, 1),
        Direction.Left => new GridPoint(-1, 0),
        _ => throw new ArgumentOutOfRangeException(nameof(direction), direction, "Unknown direction."),
    };
}
