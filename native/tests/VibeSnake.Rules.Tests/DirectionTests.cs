namespace VibeSnake.Rules.Tests;

public sealed class DirectionTests
{
    [Theory]
    [InlineData(Direction.Up, Direction.Down, 0, -1)]
    [InlineData(Direction.Right, Direction.Left, 1, 0)]
    [InlineData(Direction.Down, Direction.Up, 0, 1)]
    [InlineData(Direction.Left, Direction.Right, -1, 0)]
    public void Direction_contract_is_complete(
        Direction direction,
        Direction expectedOpposite,
        int expectedX,
        int expectedY)
    {
        Assert.Equal(expectedOpposite, direction.Opposite());
        Assert.Equal(new GridPoint(expectedX, expectedY), direction.Offset());
    }

    [Fact]
    public void Unknown_direction_is_rejected()
    {
        var unknown = (Direction)byte.MaxValue;

        Assert.Throws<ArgumentOutOfRangeException>(() => unknown.Opposite());
        Assert.Throws<ArgumentOutOfRangeException>(() => unknown.Offset());
    }
}
