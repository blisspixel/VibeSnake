namespace VibeSnake.Rules.Tests;

public sealed class RandomStreamBankTests
{
    [Fact]
    public void Master_seed_derives_independent_named_streams()
    {
        Assert.True(RandomStreamBank.NonGameplayStreamsAreIndependent(20260804UL));

        var bank = new RandomStreamBank(99UL);
        Assert.Equal(99UL, bank.MasterSeed);
        Assert.NotEqual(bank.Gameplay.NextUInt(), bank.Ai.NextUInt());
        Assert.Equal(bank.Get(RandomStreamKind.Gameplay).State, bank.Gameplay.State);
    }

    [Fact]
    public void Equal_master_seeds_reproduce_each_stream()
    {
        var left = new RandomStreamBank(123456789UL);
        var right = new RandomStreamBank(123456789UL);
        for (var index = 0; index < 32; index++)
        {
            Assert.Equal(left.Gameplay.NextUInt(), right.Gameplay.NextUInt());
            Assert.Equal(left.Ai.NextUInt(), right.Ai.NextUInt());
            Assert.Equal(left.Cosmetic.NextUInt(), right.Cosmetic.NextUInt());
            Assert.Equal(left.Radio.NextUInt(), right.Radio.NextUInt());
            Assert.Equal(left.Copy.NextUInt(), right.Copy.NextUInt());
        }
    }

    [Fact]
    public void Gameplay_stream_matches_legacy_snake_run_seed_construction()
    {
        // SnakeRun.Create currently uses Pcg32(seed) which defaults to sequence 54,
        // the same sequence owned by RandomStreamBank.Gameplay.
        var bank = new RandomStreamBank(42UL);
        var legacy = new Pcg32(42UL);
        for (var index = 0; index < 16; index++)
        {
            Assert.Equal(legacy.NextUInt(), bank.Gameplay.NextUInt());
        }
    }
}
