namespace VibeSnake.Rules.Tests;

public sealed class SnakeRunStateMachineTests
{
    [Theory]
    [InlineData(0UL)]
    [InlineData(1UL)]
    [InlineData(2UL)]
    [InlineData(17UL)]
    [InlineData(42UL)]
    [InlineData(255UL)]
    [InlineData(65535UL)]
    [InlineData(ulong.MaxValue)]
    public void Generated_operations_survive_repeated_restore_and_restart(ulong seed)
    {
        var commandRandom = new Pcg32(seed, sequence: 9001UL);
        var config = new RunConfig(
            Width: 16,
            Height: 12,
            StarvationTicks: 400,
            MaximumDirectionQueue: 3);
        var runSeed = NextUInt64(commandRandom);
        var original = SnakeRun.Create(runSeed, config);
        var restored = SnakeRun.RestoreCanonicalState(
            original.SerializeCanonicalState());

        for (var operation = 0; operation < 512; operation++)
        {
            var commandCount = commandRandom.NextInt(6);
            for (var commandIndex = 0; commandIndex < commandCount; commandIndex++)
            {
                var direction = (Direction)commandRandom.NextInt(4);
                Assert.Equal(
                    original.QueueDirection(direction),
                    restored.QueueDirection(direction));
            }

            Assert.Equal(original.Step(), restored.Step());
            AssertEquivalent(original.GetSnapshot(), restored.GetSnapshot());

            if (operation % 11 == 0)
            {
                restored = SnakeRun.RestoreCanonicalState(
                    restored.SerializeCanonicalState());
                AssertEquivalent(original.GetSnapshot(), restored.GetSnapshot());
            }

            if (operation % 29 == 0)
            {
                original = SnakeRun.RestoreCanonicalState(
                    original.SerializeCanonicalState());
                AssertEquivalent(original.GetSnapshot(), restored.GetSnapshot());
            }

            if (original.Status != RunStatus.Running)
            {
                restored = SnakeRun.RestoreCanonicalState(
                    restored.SerializeCanonicalState());
                var restartSeed = NextUInt64(commandRandom);
                original = original.Restart(restartSeed);
                restored = restored.Restart(restartSeed);
                AssertEquivalent(original.GetSnapshot(), restored.GetSnapshot());
            }
        }
    }

    private static ulong NextUInt64(Pcg32 random) =>
        ((ulong)random.NextUInt() << 32) | random.NextUInt();

    private static void AssertEquivalent(
        RunSnapshot expected,
        RunSnapshot actual)
    {
        Assert.Equal(expected.Tick, actual.Tick);
        Assert.Equal(expected.Status, actual.Status);
        Assert.Equal(expected.DeathCause, actual.DeathCause);
        Assert.Equal(expected.Direction, actual.Direction);
        Assert.Equal(expected.Body, actual.Body);
        Assert.Equal(expected.PendingDirections, actual.PendingDirections);
        Assert.Equal(expected.Food, actual.Food);
        Assert.Equal(expected.Score, actual.Score);
        Assert.Equal(expected.ComboCount, actual.ComboCount);
        Assert.Equal(expected.TicksSinceLastFood, actual.TicksSinceLastFood);
        Assert.Equal(expected.HungerTicksRemaining, actual.HungerTicksRemaining);
        Assert.Equal(expected.PowerPickup, actual.PowerPickup);
        Assert.Equal(expected.PowerSpawnTicksElapsed, actual.PowerSpawnTicksElapsed);
        Assert.Equal(expected.ShieldTicksRemaining, actual.ShieldTicksRemaining);
        Assert.Equal(expected.StateHash, actual.StateHash);
    }
}
