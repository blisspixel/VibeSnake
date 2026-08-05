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
        var previousScore = original.Score;
        var fixedConfigHash = original.ConfigHash;

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
            Assert.True(
                original.Score >= previousScore,
                "Score must be monotonic non-decreasing within a run.");
            Assert.Equal(fixedConfigHash, original.ConfigHash);
            Assert.Equal(fixedConfigHash, restored.ConfigHash);
            previousScore = original.Score;

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
                previousScore = original.Score;
                fixedConfigHash = original.ConfigHash;
            }
        }
    }

    [Theory]
    [InlineData(7UL)]
    [InlineData(99UL)]
    public void Achievement_candidates_emit_once_across_terminal_and_restore(ulong seed)
    {
        var commandRandom = new Pcg32(seed, sequence: 4242UL);
        var config = new RunConfig(
            Width: 12,
            Height: 10,
            StarvationTicks: 80,
            MaximumDirectionQueue: 3,
            EnableAchievementCandidates: true);
        var run = SnakeRun.Create(NextUInt64(commandRandom), config);
        var sawCandidates = false;

        for (var operation = 0; operation < 256; operation++)
        {
            var commandCount = commandRandom.NextInt(4);
            for (var commandIndex = 0; commandIndex < commandCount; commandIndex++)
            {
                run.QueueDirection((Direction)commandRandom.NextInt(4));
            }

            var result = run.Step();
            var candidateCount = result.OrderedEvents.Count(
                detail => detail.Kind == RunEventKind.AchievementCandidate);
            if (candidateCount > 0)
            {
                Assert.False(sawCandidates);
                Assert.True(result.Events.HasFlag(RunEvent.AchievementCandidate));
                sawCandidates = true;
            }

            if (run.Status != RunStatus.Running)
            {
                var idle = run.Step();
                Assert.DoesNotContain(
                    idle.OrderedEvents,
                    detail => detail.Kind == RunEventKind.AchievementCandidate);

                var restored = SnakeRun.RestoreCanonicalState(run.SerializeCanonicalState());
                var afterRestore = restored.Step();
                Assert.DoesNotContain(
                    afterRestore.OrderedEvents,
                    detail => detail.Kind == RunEventKind.AchievementCandidate);

                run = run.Restart(NextUInt64(commandRandom));
                sawCandidates = false;
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
        Assert.Equal(expected.PhaseShiftTicksRemaining, actual.PhaseShiftTicksRemaining);
        Assert.Equal(expected.LastStandHeld, actual.LastStandHeld);
        Assert.Equal(
            expected.LastStandRecoveryTicksRemaining,
            actual.LastStandRecoveryTicksRemaining);
        Assert.Equal(expected.SlowMoTicksRemaining, actual.SlowMoTicksRemaining);
        Assert.Equal(expected.BoostTicksRemaining, actual.BoostTicksRemaining);
        Assert.Equal(expected.MagnetTicksRemaining, actual.MagnetTicksRemaining);
        Assert.Equal(expected.GluttonyTicksRemaining, actual.GluttonyTicksRemaining);
        Assert.Equal(expected.BaitPosition, actual.BaitPosition);
        Assert.Equal(expected.DetachedObstacles, actual.DetachedObstacles);
        Assert.Equal(
            expected.DetachedObstacleTicksRemaining,
            actual.DetachedObstacleTicksRemaining);
        Assert.Equal(expected.StateHash, actual.StateHash);
    }
}
