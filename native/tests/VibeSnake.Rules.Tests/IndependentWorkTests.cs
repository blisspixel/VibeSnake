namespace VibeSnake.Rules.Tests;

public sealed class IndependentWorkTests
{
    [Fact]
    public void Independent_work_maps_items_in_input_order()
    {
        IReadOnlyList<int> items = Enumerable.Range(0, 32).ToArray();

        var results = IndependentWork.Map(items, value => value * value);

        Assert.Equal(items.Select(value => value * value), results);
        Assert.True(IndependentWork.WorkerCount >= 1);
    }

    [Fact]
    public void Independent_work_returns_empty_for_empty_input()
    {
        Assert.Empty(IndependentWork.Map(Array.Empty<int>(), value => value));
    }
}
