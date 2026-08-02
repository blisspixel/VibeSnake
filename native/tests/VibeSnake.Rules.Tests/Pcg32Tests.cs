namespace VibeSnake.Rules.Tests;

public sealed class Pcg32Tests
{
    [Fact]
    public void Reference_seed_matches_published_pcg32_vector()
    {
        var random = new Pcg32(seed: 42UL, sequence: 54UL);
        uint[] expected =
        [
            0xa15c02b7U,
            0x7b47f409U,
            0xba1d3330U,
            0x83d2f293U,
            0xbfa4784bU,
            0xcbed606eU,
        ];

        Assert.Equal(expected, expected.Select(_ => random.NextUInt()).ToArray());
    }

    [Fact]
    public void Same_seed_and_sequence_replay_exactly()
    {
        var first = new Pcg32(1234UL, 99UL);
        var second = new Pcg32(1234UL, 99UL);

        Assert.Equal(
            Enumerable.Range(0, 100).Select(_ => first.NextUInt()),
            Enumerable.Range(0, 100).Select(_ => second.NextUInt()));
    }

    [Fact]
    public void Bounded_values_stay_inside_requested_range()
    {
        var random = new Pcg32(999UL);
        var values = Enumerable.Range(0, 10_000).Select(_ => random.NextInt(7)).ToArray();

        Assert.All(values, value => Assert.InRange(value, 0, 6));
        Assert.Equal(Enumerable.Range(0, 7), values.Distinct().Order());
    }

    [Fact]
    public void Bounded_generation_rejects_empty_range()
    {
        var random = new Pcg32(1UL);

        Assert.Throws<ArgumentOutOfRangeException>(() => random.NextInt(0));
    }

    [Fact]
    public void Restore_constructor_rejects_even_increment()
    {
        Assert.Throws<ArgumentException>(() => new Pcg32(1UL, 2UL, restoreState: true));
    }

    [Fact]
    public void Restore_constructor_requires_explicit_restore_flag()
    {
        Assert.Throws<ArgumentException>(() => new Pcg32(1UL, 3UL, restoreState: false));
    }
}
