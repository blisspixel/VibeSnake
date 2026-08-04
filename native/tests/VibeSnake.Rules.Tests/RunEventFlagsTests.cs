namespace VibeSnake.Rules.Tests;

public sealed class RunEventFlagsTests
{
    [Fact]
    public void Flag_values_are_unique_powers_of_two()
    {
        var flags = Enum.GetValues<RunEvent>()
            .Where(value => value != RunEvent.None)
            .Select(value => (ushort)value)
            .ToArray();
        Assert.Equal(flags.Length, flags.Distinct().Count());
        foreach (var flag in flags)
        {
            Assert.True((flag & (flag - 1)) == 0, $"RunEvent value {flag} is not a single bit.");
        }
    }
}
