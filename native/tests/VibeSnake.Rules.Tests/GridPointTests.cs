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
}
