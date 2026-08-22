namespace VibeSnake.Rules.Tests;

public sealed class GridPointTests
{
    [Theory]
    [InlineData(-1, -1, 3, 4, 2, 3)]
    [InlineData(3, 4, 3, 4, 0, 0)]
    [InlineData(7, 9, 3, 4, 1, 1)]
    [InlineData(1, 2, 3, 4, 1, 2)]
    public void Wrap_uses_positive_modulo(
        int x,
        int y,
        int width,
        int height,
        int expectedX,
        int expectedY)
    {
        Assert.Equal(new GridPoint(expectedX, expectedY), new GridPoint(x, y).Wrap(width, height));
    }

    [Fact]
    public void Add_combines_offsets()
    {
        Assert.Equal(new GridPoint(4, 2), new GridPoint(3, 4).Add(new GridPoint(1, -2)));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    public void Wrap_rejects_non_positive_dimensions(int width, int height)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new GridPoint(0, 0).Wrap(width, height));
    }

    [Theory]
    [InlineData(0, 0, 3, 0, 8, 5, 3)]
    [InlineData(0, 0, 7, 0, 8, 5, 1)]
    [InlineData(1, 2, 5, 2, 9, 5, 4)]
    [InlineData(0, 0, 0, 2, 8, 5, 2)]
    public void WrapManhattanDistance_uses_the_shorter_axis_wrap(
        int leftX,
        int leftY,
        int rightX,
        int rightY,
        int width,
        int height,
        int expected)
    {
        Assert.Equal(
            expected,
            GridPoint.WrapManhattanDistance(
                new GridPoint(leftX, leftY),
                new GridPoint(rightX, rightY),
                width,
                height));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    public void WrapManhattanDistance_rejects_non_positive_dimensions(int width, int height)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            GridPoint.WrapManhattanDistance(new GridPoint(0, 0), new GridPoint(0, 0), width, height));
    }

    [Fact]
    public void WrapManhattanDistance_rejects_points_outside_the_board()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            GridPoint.WrapManhattanDistance(new GridPoint(0, 0), new GridPoint(8, 0), 8, 5));
    }

    [Theory]
    [InlineData(1, 2, 3, 2, 5, 2, 9, 5, true)]
    [InlineData(1, 2, 4, 2, 5, 2, 9, 5, true)]
    [InlineData(1, 2, 1, 3, 5, 2, 9, 5, false)]
    [InlineData(0, 0, 5, 0, 3, 0, 6, 4, true)]
    [InlineData(0, 0, 1, 0, 3, 0, 6, 4, true)]
    [InlineData(0, 0, 0, 1, 3, 0, 6, 4, false)]
    public void LiesOnWrapManhattanGeodesic_matches_every_shortest_wrap_path(
        int startX,
        int startY,
        int viaX,
        int viaY,
        int endX,
        int endY,
        int width,
        int height,
        bool expected)
    {
        Assert.Equal(
            expected,
            GridPoint.LiesOnWrapManhattanGeodesic(
                new GridPoint(startX, startY),
                new GridPoint(viaX, viaY),
                new GridPoint(endX, endY),
                width,
                height));
    }
}
