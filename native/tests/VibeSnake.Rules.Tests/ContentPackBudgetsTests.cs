using VibeSnake.Persistence;

namespace VibeSnake.Rules.Tests;

public sealed class ContentPackBudgetsTests
{
    [Fact]
    public void Core_and_radio_budget_helpers_are_strict()
    {
        Assert.True(ContentPackBudgets.IsWithinCoreCompressedBudget(0));
        Assert.True(ContentPackBudgets.IsWithinCoreCompressedBudget(
            ContentPackBudgets.CoreCompressedBytesMaximum));
        Assert.False(ContentPackBudgets.IsWithinCoreCompressedBudget(
            ContentPackBudgets.CoreCompressedBytesMaximum + 1));
        Assert.True(ContentPackBudgets.IsWithinCoreInstalledBudget(1));
        Assert.True(ContentPackBudgets.IsRadioPackId("vibesnake.radio.ambient"));
        Assert.False(ContentPackBudgets.IsRadioPackId("vibesnake.core"));
        Assert.False(ContentPackBudgets.IsRadioPackId("vibesnake.radio."));
        Assert.Equal("vibesnake.core", ContentPackBudgets.CorePackId);
    }
}
