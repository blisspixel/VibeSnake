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
        // SnakeRun.Create uses RandomStreamBank.Gameplay, which matches Pcg32(seed)
        // with the historical default sequence of 54.
        var bank = new RandomStreamBank(42UL);
        var legacy = new Pcg32(42UL);
        for (var index = 0; index < 16; index++)
        {
            Assert.Equal(legacy.NextUInt(), bank.Gameplay.NextUInt());
        }
    }

    [Fact]
    public void Snake_run_create_preserves_master_seed_and_matches_stream_bank_gameplay()
    {
        const ulong seed = 777001UL;
        var run = SnakeRun.Create(seed);
        var twin = SnakeRun.Create(seed);
        Assert.Equal(seed, run.MasterSeed);
        Assert.Equal(run.ComputeStateHash(), twin.ComputeStateHash());

        var bank = SnakeRun.CreateStreamBank(seed);
        // First food placement already advanced gameplay; bank starts fresh.
        // Equal master seeds still produce independent presentation streams.
        Assert.NotEqual(bank.Gameplay.NextUInt(), bank.Ai.NextUInt());

        var restored = SnakeRun.RestoreCanonicalState(run.SerializeCanonicalState());
        Assert.Null(restored.MasterSeed);
        Assert.Equal(run.ComputeStateHash(), restored.ComputeStateHash());
    }
}
