namespace VibeSnake.Rules.Tests;

public sealed class SnakeRunRestoreTests
{
    [Fact]
    public void Running_state_round_trips_with_pending_intent_and_continues_identically()
    {
        var original = SnakeRun.Create(
            800UL,
            new RunConfig(
                Width: 12,
                Height: 8,
                StarvationTicks: 200,
                MaximumDirectionQueue: 4,
                FoodScore: 25,
                ComboWindowTicks: 40,
                SpeedBonusTicks: 20));
        Assert.True(original.QueueDirection(Direction.Up));
        Assert.True(original.QueueDirection(Direction.Left));
        Assert.True(original.QueueDirection(Direction.Down));
        original.Step();

        var canonicalState = original.SerializeCanonicalState();
        var restored = SnakeRun.RestoreCanonicalState(canonicalState);

        AssertEquivalent(original, restored);
        Assert.Equal(canonicalState, restored.SerializeCanonicalState());
        for (var step = 0; step < 20 && original.Status == RunStatus.Running; step++)
        {
            Assert.Equal(original.Step(), restored.Step());
            AssertEquivalent(original, restored);
        }
    }

    [Fact]
    public void Restored_random_state_selects_the_same_food_after_collection()
    {
        var original = SnakeRun.CreateForTesting(
            new RunConfig(Width: 5, Height: 4, StarvationTicks: 100),
            [new GridPoint(1, 1)],
            Direction.Right,
            new GridPoint(2, 1),
            hungerTicksRemaining: 1,
            randomState: 12345UL,
            randomIncrement: 109UL);
        var restored = SnakeRun.RestoreCanonicalState(
            original.SerializeCanonicalState());

        Assert.Equal(original.Step(), restored.Step());
        Assert.NotNull(original.Food);
        Assert.Equal(original.Food, restored.Food);
        AssertEquivalent(original, restored);
    }

    [Fact]
    public void Dead_and_won_states_round_trip_without_becoming_mutable()
    {
        var dead = SnakeRun.CreateForTesting(
            new RunConfig(Width: 5, Height: 4, StarvationTicks: 1),
            [new GridPoint(1, 1)],
            Direction.Right,
            new GridPoint(4, 3),
            hungerTicksRemaining: 1);
        dead.Step();
        var restoredDead = SnakeRun.RestoreCanonicalState(
            dead.SerializeCanonicalState());

        AssertEquivalent(dead, restoredDead);
        Assert.Equal(RunEvent.None, restoredDead.Step().Events);

        var won = SnakeRun.CreateForTesting(
            new RunConfig(Width: 2, Height: 2),
            [
                new GridPoint(0, 0),
                new GridPoint(0, 1),
                new GridPoint(1, 1),
            ],
            Direction.Up,
            new GridPoint(1, 0),
            hungerTicksRemaining: 1);
        won.Step();
        var restoredWon = SnakeRun.RestoreCanonicalState(
            won.SerializeCanonicalState());

        AssertEquivalent(won, restoredWon);
        Assert.Equal(RunStatus.Won, restoredWon.Status);
        Assert.Equal(RunEvent.None, restoredWon.Step().Events);
    }

    [Theory]
    [InlineData("\n")]
    [InlineData(" ")]
    public void Noncanonical_whitespace_is_rejected(string suffix)
    {
        var canonical = SnakeRun.Create(801UL).SerializeCanonicalState();

        Assert.Throws<InvalidDataException>(
            () => SnakeRun.RestoreCanonicalState(canonical + suffix));
    }

    [Fact]
    public void Unknown_properties_are_rejected_as_noncanonical()
    {
        var canonical = SnakeRun.Create(802UL).SerializeCanonicalState();
        var withUnknownProperty = canonical.Insert(
            canonical.Length - 1,
            ",\"unexpected\":true");

        Assert.Throws<InvalidDataException>(
            () => SnakeRun.RestoreCanonicalState(withUnknownProperty));
    }

    [Theory]
    [InlineData("\"schemaVersion\":3", "\"schemaVersion\":2")]
    [InlineData("\"schemaVersion\":3", "\"schemaVersion\":1")]
    [InlineData("\"rulesVersion\":4", "\"rulesVersion\":3")]
    [InlineData(
        "\"fnv1a64-canonical-json-v4\"",
        "\"unsupported-hash\"")]
    [InlineData(
        "\"fnv1a64-canonical-json-v4\"",
        "\"fnv1a64-canonical-json-v3\"")]
    [InlineData("\"pcg-xsh-rr-32-v1\"", "\"unsupported-rng\"")]
    public void Unsupported_contract_identifiers_are_rejected(
        string current,
        string replacement)
    {
        var canonical = SnakeRun.Create(803UL).SerializeCanonicalState();
        var incompatible = ReplaceOnce(canonical, current, replacement);

        Assert.Throws<InvalidDataException>(
            () => SnakeRun.RestoreCanonicalState(incompatible));
    }

    [Fact]
    public void Invalid_random_body_food_queue_and_terminal_state_are_rejected()
    {
        var simple = SnakeRun.CreateForTesting(
            new RunConfig(Width: 5, Height: 4, StarvationTicks: 100),
            [new GridPoint(1, 1)],
            Direction.Right,
            new GridPoint(4, 3),
            hungerTicksRemaining: 100);
        var canonical = simple.SerializeCanonicalState();

        AssertInvalid(ReplaceOnce(canonical, "\"increment\":\"109\"", "\"increment\":\"108\""));
        AssertInvalid(ReplaceOnce(canonical, "\"food\":{\"x\":4,\"y\":3}", "\"food\":{\"x\":1,\"y\":1}"));
        AssertInvalid(ReplaceOnce(canonical, "\"body\":[{\"x\":1,\"y\":1}]", "\"body\":[{\"x\":5,\"y\":1}]"));
        AssertInvalid(ReplaceOnce(canonical, "\"status\":0", "\"status\":2"));
        AssertInvalid(
            ReplaceOnce(
                canonical,
                "\"food\":{\"x\":4,\"y\":3}",
                "\"food\":null"));
        AssertInvalid(
            ReplaceOnce(canonical, "\"comboCount\":0", "\"comboCount\":2"));
        AssertInvalid(
            ReplaceOnce(
                canonical,
                "\"powerPickup\":null",
                "\"powerPickup\":{\"kind\":1,\"position\":{\"x\":1,\"y\":1},\"visibilityTicksRemaining\":10}"));
        AssertInvalid(
            ReplaceOnce(
                canonical,
                "\"powerPickup\":null",
                "\"powerPickup\":{\"kind\":1,\"position\":{\"x\":4,\"y\":3},\"visibilityTicksRemaining\":10}"));
        AssertInvalid(
            ReplaceOnce(
                canonical,
                "\"powerPickup\":null",
                "\"powerPickup\":{\"kind\":255,\"position\":{\"x\":3,\"y\":3},\"visibilityTicksRemaining\":10}"));
        AssertInvalid(
            ReplaceOnce(canonical, "\"phaseShiftTicksRemaining\":0", "\"phaseShiftTicksRemaining\":101"));
        AssertInvalid(
            ReplaceOnce(
                canonical,
                "\"powerPickup\":null",
                "\"powerPickup\":{\"kind\":1,\"position\":{\"x\":3,\"y\":3},\"visibilityTicksRemaining\":121}"));
        AssertInvalid(
            ReplaceOnce(canonical, "\"shieldTicksRemaining\":0", "\"shieldTicksRemaining\":101"));
        AssertInvalid(
            ReplaceOnce(canonical, "\"powerSpawnTicksElapsed\":0", "\"powerSpawnTicksElapsed\":301"));
        AssertInvalid(
            ReplaceOnce(
                ReplaceOnce(canonical, "\"shieldTicksRemaining\":0", "\"shieldTicksRemaining\":1"),
                "\"powerPickup\":null",
                "\"powerPickup\":{\"kind\":1,\"position\":{\"x\":3,\"y\":3},\"visibilityTicksRemaining\":10}"));

        var twoSegments = SnakeRun.CreateForTesting(
            new RunConfig(Width: 5, Height: 4, StarvationTicks: 100),
            [new GridPoint(1, 1), new GridPoint(2, 1)],
            Direction.Right,
            new GridPoint(4, 3),
            hungerTicksRemaining: 100);
        AssertInvalid(
            ReplaceOnce(
                twoSegments.SerializeCanonicalState(),
                "\"body\":[{\"x\":1,\"y\":1},{\"x\":2,\"y\":1}]",
                "\"body\":[{\"x\":1,\"y\":1},{\"x\":4,\"y\":1}]"));

        Assert.True(simple.QueueDirection(Direction.Up));
        Assert.True(simple.QueueDirection(Direction.Left));
        AssertInvalid(
            ReplaceOnce(
                simple.SerializeCanonicalState(),
                "\"pendingDirections\":[0,3]",
                "\"pendingDirections\":[0,2]"));
    }

    [Fact]
    public void Self_collision_terminal_state_cannot_retain_an_active_shield()
    {
        var starvation = SnakeRun.CreateForTesting(
            new RunConfig(Width: 5, Height: 4, StarvationTicks: 1),
            [new GridPoint(1, 1)],
            Direction.Right,
            new GridPoint(4, 3),
            hungerTicksRemaining: 1,
            shieldTicksRemaining: 2);
        starvation.Step();
        var impossible = ReplaceOnce(
            starvation.SerializeCanonicalState(),
            "\"deathCause\":2",
            "\"deathCause\":1");

        AssertInvalid(impossible);
    }

    [Fact]
    public void Simultaneous_collision_and_hunger_round_trip_with_collision_precedence()
    {
        var collision = SnakeRun.CreateForTesting(
            new RunConfig(Width: 5, Height: 4, StarvationTicks: 100),
            [
                new GridPoint(1, 1),
                new GridPoint(1, 2),
                new GridPoint(2, 2),
                new GridPoint(2, 1),
            ],
            Direction.Down,
            new GridPoint(4, 3),
            hungerTicksRemaining: 1);

        collision.Step();
        var restored = SnakeRun.RestoreCanonicalState(
            collision.SerializeCanonicalState());

        Assert.Equal(RunStatus.Dead, restored.Status);
        Assert.Equal(DeathCause.SelfCollision, restored.DeathCause);
        Assert.Equal(0, restored.HungerTicksRemaining);
        AssertEquivalent(collision, restored);
    }

    [Fact]
    public void Empty_malformed_and_missing_state_are_rejected()
    {
        Assert.Throws<ArgumentNullException>(
            () => SnakeRun.RestoreCanonicalState(null!));
        Assert.Throws<ArgumentException>(
            () => SnakeRun.RestoreCanonicalState(""));
        Assert.Throws<InvalidDataException>(
            () => SnakeRun.RestoreCanonicalState("{"));
        Assert.Throws<InvalidDataException>(
            () => SnakeRun.RestoreCanonicalState("{}"));
    }

    [Theory]
    [InlineData("sessionFoodEaten")]
    [InlineData("sessionWraps")]
    [InlineData("sessionNearMisses")]
    [InlineData("sessionPowerupsCollected")]
    [InlineData("sessionMaxCombo")]
    public void Negative_session_counters_are_rejected(string fieldName)
    {
        var canonical = SnakeRun.Create(805UL).SerializeCanonicalState();
        var invalid = ReplaceOnce(
            canonical,
            $"\"{fieldName}\":0",
            $"\"{fieldName}\":-1");

        Assert.Throws<InvalidDataException>(
            () => SnakeRun.RestoreCanonicalState(invalid));
    }

    [Fact]
    public void Oversized_state_is_rejected_before_json_materialization()
    {
        var oversized = "{" + new string(
            ' ',
            SnakeRun.MaximumCanonicalStateCharacters);

        var exception = Assert.Throws<InvalidDataException>(
            () => SnakeRun.RestoreCanonicalState(oversized));

        Assert.Contains("size limit", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Allocation_and_counter_amplifying_values_are_rejected_before_use()
    {
        var canonical = SnakeRun.Create(804UL).SerializeCanonicalState();

        AssertInvalid(
            ReplaceOnce(
                canonical,
                "\"maximumDirectionQueue\":3",
                $"\"maximumDirectionQueue\":{int.MaxValue}"));
        AssertInvalid(
            ReplaceOnce(
                canonical,
                "\"tick\":0",
                $"\"tick\":{SnakeRun.MaximumRestorableTick + 1}"));
        AssertInvalid(
            ReplaceOnce(
                canonical,
                "\"score\":0",
                $"\"score\":{SnakeRun.MaximumScore + 1L}"));
        AssertInvalid(
            ReplaceOnce(
                canonical,
                "\"ticksSinceLastFood\":0",
                "\"ticksSinceLastFood\":1"));
    }

    private static void AssertEquivalent(SnakeRun expected, SnakeRun actual)
    {
        var expectedSnapshot = expected.GetSnapshot();
        var actualSnapshot = actual.GetSnapshot();
        Assert.Equal(expectedSnapshot.Tick, actualSnapshot.Tick);
        Assert.Equal(expectedSnapshot.Status, actualSnapshot.Status);
        Assert.Equal(expectedSnapshot.DeathCause, actualSnapshot.DeathCause);
        Assert.Equal(expectedSnapshot.Direction, actualSnapshot.Direction);
        Assert.Equal(expectedSnapshot.Body, actualSnapshot.Body);
        Assert.Equal(
            expectedSnapshot.PendingDirections,
            actualSnapshot.PendingDirections);
        Assert.Equal(expectedSnapshot.Food, actualSnapshot.Food);
        Assert.Equal(expectedSnapshot.Score, actualSnapshot.Score);
        Assert.Equal(expectedSnapshot.ComboCount, actualSnapshot.ComboCount);
        Assert.Equal(
            expectedSnapshot.TicksSinceLastFood,
            actualSnapshot.TicksSinceLastFood);
        Assert.Equal(
            expectedSnapshot.HungerTicksRemaining,
            actualSnapshot.HungerTicksRemaining);
        Assert.Equal(expectedSnapshot.PowerPickup, actualSnapshot.PowerPickup);
        Assert.Equal(
            expectedSnapshot.PowerSpawnTicksElapsed,
            actualSnapshot.PowerSpawnTicksElapsed);
        Assert.Equal(
            expectedSnapshot.ShieldTicksRemaining,
            actualSnapshot.ShieldTicksRemaining);
        Assert.Equal(
            expectedSnapshot.PhaseShiftTicksRemaining,
            actualSnapshot.PhaseShiftTicksRemaining);
        Assert.Equal(expectedSnapshot.LastStandHeld, actualSnapshot.LastStandHeld);
        Assert.Equal(
            expectedSnapshot.LastStandRecoveryTicksRemaining,
            actualSnapshot.LastStandRecoveryTicksRemaining);
        Assert.Equal(expectedSnapshot.SlowMoTicksRemaining, actualSnapshot.SlowMoTicksRemaining);
        Assert.Equal(expectedSnapshot.BoostTicksRemaining, actualSnapshot.BoostTicksRemaining);
        Assert.Equal(expectedSnapshot.MagnetTicksRemaining, actualSnapshot.MagnetTicksRemaining);
        Assert.Equal(expectedSnapshot.GluttonyTicksRemaining, actualSnapshot.GluttonyTicksRemaining);
        Assert.Equal(expectedSnapshot.BaitPosition, actualSnapshot.BaitPosition);
        Assert.Equal(expectedSnapshot.DetachedObstacles, actualSnapshot.DetachedObstacles);
        Assert.Equal(
            expectedSnapshot.DetachedObstacleTicksRemaining,
            actualSnapshot.DetachedObstacleTicksRemaining);
        Assert.Equal(expectedSnapshot.StateHash, actualSnapshot.StateHash);
    }

    private static string ReplaceOnce(
        string value,
        string current,
        string replacement)
    {
        Assert.Equal(1, value.Split(current).Length - 1);
        return value.Replace(current, replacement, StringComparison.Ordinal);
    }

    private static void AssertInvalid(string state) =>
        Assert.Throws<InvalidDataException>(
            () => SnakeRun.RestoreCanonicalState(state));
}
