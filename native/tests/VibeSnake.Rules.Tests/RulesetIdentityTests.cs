namespace VibeSnake.Rules.Tests;

public sealed class RulesetIdentityTests
{
    [Fact]
    public void Current_identity_is_explicit_and_stable()
    {
        Assert.Equal("vibesnake-core", RulesetIdentity.Current.Id);
        Assert.Equal(4, RulesetIdentity.Current.Version);
        Assert.Equal("vibesnake-core@4", RulesetIdentity.Current.ContractId);
        Assert.True(RulesetIdentity.Current.IsCurrent);
        Assert.Equal(RulesetIdentity.CurrentId, SnakeRun.RulesetId);
        Assert.Equal(RulesetIdentity.CurrentVersion, SnakeRun.RulesVersion);
    }

    [Fact]
    public void Custom_identity_reports_compatibility_without_mutating_current()
    {
        var identity = new RulesetIdentity("vibesnake-challenge", 3);

        Assert.Equal("vibesnake-challenge@3", identity.ContractId);
        Assert.False(identity.IsCurrent);
        Assert.True(RulesetIdentity.Current.IsCurrent);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Identity_rejects_blank_ids(string id)
    {
        Assert.Throws<ArgumentException>(() => new RulesetIdentity(id, 1));
    }

    [Fact]
    public void Identity_rejects_null_id_and_nonpositive_version()
    {
        Assert.Throws<ArgumentNullException>(
            () => new RulesetIdentity(null!, 1));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new RulesetIdentity("valid", 0));
    }
}
