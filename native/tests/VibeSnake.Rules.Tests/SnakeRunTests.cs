using System.Text.Json;

namespace VibeSnake.Rules.Tests;

public sealed class SnakeRunTests
{
    [Fact]
    public void New_run_has_valid_deterministic_initial_state()
    {
        var first = SnakeRun.Create(123UL);
        var second = SnakeRun.Create(123UL);
        var snapshot = first.GetSnapshot();

        Assert.Equal(new GridPoint(32, 16), snapshot.Head);
        Assert.Equal(Direction.Right, snapshot.Direction);
        Assert.Equal(RunStatus.Running, snapshot.Status);
        Assert.Equal(DeathCause.None, snapshot.DeathCause);
        Assert.Equal(600, snapshot.HungerTicksRemaining);
        Assert.Equal(0, snapshot.Score);
        Assert.Equal(0, snapshot.ComboCount);
        Assert.Equal(1.0, snapshot.ComboMultiplier);
        Assert.Equal(0, snapshot.TicksSinceLastFood);
        Assert.NotNull(snapshot.Food);
        Assert.DoesNotContain(snapshot.Food!.Value, snapshot.Body);
        Assert.Equal(snapshot.StateHash, second.ComputeStateHash());
    }

    [Fact]
    public void Different_seeds_select_different_initial_food_in_reference_board()
    {
        Assert.NotEqual(SnakeRun.Create(1UL).Food, SnakeRun.Create(2UL).Food);
    }

    [Fact]
    public void Direction_queue_rejects_duplicates_reversals_and_overflow()
    {
        var run = SnakeRun.Create(10UL);

        Assert.False(run.QueueDirection(Direction.Right));
        Assert.False(run.QueueDirection(Direction.Left));
        Assert.True(run.QueueDirection(Direction.Up));
        Assert.False(run.QueueDirection(Direction.Down));
        Assert.True(run.QueueDirection(Direction.Left));
        Assert.True(run.QueueDirection(Direction.Down));
        Assert.False(run.QueueDirection(Direction.Right));
        Assert.Equal(3, run.PendingDirectionCount);
    }

    [Fact]
    public void Queued_turns_are_consumed_one_per_step()
    {
        var run = CreateRun(body: [new GridPoint(3, 3)], direction: Direction.Right, food: new GridPoint(0, 0));
        run.QueueDirection(Direction.Up);
        run.QueueDirection(Direction.Left);

        var firstResult = run.Step();
        Assert.Equal(Direction.Up, run.Direction);
        Assert.Equal(new GridPoint(3, 2), run.Head);
        Assert.Equal(1, run.PendingDirectionCount);
        Assert.Equal(
            [
                new RunEventDetail(RunEventKind.DirectionChanged, NewDirection: Direction.Up),
                new RunEventDetail(RunEventKind.Moved, Position: new GridPoint(3, 2)),
            ],
            firstResult.OrderedEvents);

        run.Step();
        Assert.Equal(Direction.Left, run.Direction);
        Assert.Equal(new GridPoint(2, 2), run.Head);
        Assert.Equal(0, run.PendingDirectionCount);
    }

    [Fact]
    public void Movement_wraps_at_board_edges()
    {
        var run = CreateRun(
            body: [new GridPoint(3, 1)],
            direction: Direction.Right,
            food: new GridPoint(2, 2),
            config: new RunConfig(Width: 4, Height: 3));

        var result = run.Step();

        Assert.Equal(new GridPoint(0, 1), run.Head);
        Assert.True(result.Events.HasFlag(RunEvent.Moved));
        Assert.True(result.Events.HasFlag(RunEvent.Wrapped));
        Assert.Equal(
            [RunEventKind.Moved, RunEventKind.Wrapped],
            result.OrderedEvents.Select(detail => detail.Kind));
    }

    [Fact]
    public void Eating_grows_scores_and_resets_hunger_before_starvation()
    {
        var config = new RunConfig(Width: 5, Height: 4, StarvationTicks: 7, FoodScore: 25);
        var run = CreateRun(
            body: [new GridPoint(1, 1)],
            direction: Direction.Right,
            food: new GridPoint(2, 1),
            hungerTicksRemaining: 1,
            config: config);

        var result = run.Step();

        Assert.Equal(2, run.Body.Count);
        Assert.Equal(45, run.Score);
        Assert.Equal(1, run.ComboCount);
        Assert.Equal(0, run.TicksSinceLastFood);
        Assert.Equal(7, run.HungerTicksRemaining);
        Assert.Equal(RunStatus.Running, run.Status);
        Assert.True(result.Events.HasFlag(RunEvent.AteFood));
        Assert.DoesNotContain(run.Food!.Value, run.Body);
        Assert.Equal(
            [
                new RunEventDetail(RunEventKind.Moved, Position: new GridPoint(2, 1)),
                new RunEventDetail(RunEventKind.AteFood, Position: new GridPoint(2, 1)),
                new RunEventDetail(RunEventKind.ScoreChanged, Value: 45),
                new RunEventDetail(RunEventKind.HungerReset, Value: 7),
            ],
            result.OrderedEvents);
    }

    [Fact]
    public void Food_respawn_has_a_golden_coordinate_and_random_state()
    {
        var run = SnakeRun.CreateForTesting(
            new RunConfig(Width: 5, Height: 4, StarvationTicks: 100),
            [new GridPoint(1, 1)],
            Direction.Right,
            new GridPoint(2, 1),
            hungerTicksRemaining: 100,
            randomState: 1UL,
            randomIncrement: 109UL);

        run.Step();

        Assert.Equal(new GridPoint(2, 2), run.Food);
        using var state = JsonDocument.Parse(run.SerializeCanonicalState());
        var random = state.RootElement.GetProperty("random");
        Assert.Equal("235471322647811199", random.GetProperty("state").GetString());
        Assert.Equal("109", random.GetProperty("increment").GetString());
    }

    [Fact]
    public void Score_saturates_and_reports_only_points_that_were_awarded()
    {
        var run = SnakeRun.CreateForTesting(
            new RunConfig(Width: 5, Height: 4, StarvationTicks: 100),
            [new GridPoint(1, 1)],
            Direction.Right,
            new GridPoint(2, 1),
            hungerTicksRemaining: 100,
            score: SnakeRun.MaximumScore - 1);

        var result = run.Step();

        Assert.Equal(SnakeRun.MaximumScore, run.Score);
        Assert.Contains(
            new RunEventDetail(RunEventKind.ScoreChanged, Value: 1),
            result.OrderedEvents);
    }

    [Fact]
    public void Hunger_expiry_ends_run_after_the_movement_step()
    {
        var run = CreateRun(
            body: [new GridPoint(1, 1)],
            direction: Direction.Right,
            food: new GridPoint(4, 3),
            hungerTicksRemaining: 1);

        var result = run.Step();

        Assert.Equal(new GridPoint(2, 1), run.Head);
        Assert.Equal(0, run.HungerTicksRemaining);
        Assert.Equal(RunStatus.Dead, run.Status);
        Assert.Equal(DeathCause.Starvation, run.DeathCause);
        Assert.True(result.Events.HasFlag(RunEvent.Died));
        Assert.Equal(
            [RunEventKind.Moved, RunEventKind.Died],
            result.OrderedEvents.Select(detail => detail.Kind));
        Assert.Equal(DeathCause.Starvation, result.OrderedEvents[^1].Cause);
    }

    [Fact]
    public void Moving_onto_departing_tail_is_legal()
    {
        var run = CreateRun(
            body:
            [
                new GridPoint(1, 1),
                new GridPoint(1, 2),
                new GridPoint(2, 2),
                new GridPoint(2, 1),
            ],
            direction: Direction.Left,
            food: new GridPoint(4, 3));

        run.Step();

        Assert.Equal(RunStatus.Running, run.Status);
        Assert.Equal(new GridPoint(1, 1), run.Head);
        Assert.Equal(4, run.Body.Count);
        Assert.Equal(run.Body.Count, run.Body.Distinct().Count());
    }

    [Fact]
    public void Moving_into_body_causes_collision_without_mutating_body()
    {
        var originalBody = new[]
        {
            new GridPoint(1, 1),
            new GridPoint(1, 2),
            new GridPoint(2, 2),
            new GridPoint(2, 1),
        };
        var run = CreateRun(
            originalBody,
            Direction.Down,
            new GridPoint(4, 3),
            hungerTicksRemaining: 1);

        var result = run.Step();

        Assert.Equal(RunStatus.Dead, run.Status);
        Assert.Equal(DeathCause.SelfCollision, run.DeathCause);
        Assert.Equal(originalBody, run.Body);
        Assert.Equal(0, run.HungerTicksRemaining);
        Assert.Equal(RunEvent.Died, result.Events);
        Assert.Equal(
            [
                new RunEventDetail(
                    RunEventKind.Died,
                    Position: new GridPoint(2, 2),
                    Cause: DeathCause.SelfCollision),
            ],
            result.OrderedEvents);
    }

    [Fact]
    public void Filling_the_last_cell_wins_and_removes_food()
    {
        var run = CreateRun(
            body:
            [
                new GridPoint(0, 0),
                new GridPoint(0, 1),
                new GridPoint(1, 1),
            ],
            direction: Direction.Up,
            food: new GridPoint(1, 0),
            config: new RunConfig(Width: 2, Height: 2));

        var result = run.Step();

        Assert.Equal(RunStatus.Won, run.Status);
        Assert.Null(run.Food);
        Assert.Equal(4, run.Body.Count);
        Assert.True(result.Events.HasFlag(RunEvent.Won));
        Assert.Equal(
            [
                RunEventKind.Moved,
                RunEventKind.AteFood,
                RunEventKind.ScoreChanged,
                RunEventKind.HungerReset,
                RunEventKind.Won,
            ],
            result.OrderedEvents.Select(detail => detail.Kind));
    }

    [Fact]
    public void Terminal_run_ignores_later_commands_and_steps()
    {
        var run = CreateRun(
            body:
            [
                new GridPoint(1, 1),
                new GridPoint(1, 2),
                new GridPoint(2, 2),
                new GridPoint(2, 1),
            ],
            direction: Direction.Down,
            food: new GridPoint(4, 3));
        run.Step();
        var terminalHash = run.ComputeStateHash();

        Assert.False(run.QueueDirection(Direction.Left));
        var repeatedStep = run.Step();

        Assert.Equal(RunEvent.None, repeatedStep.Events);
        Assert.Empty(repeatedStep.OrderedEvents);
        Assert.Equal(terminalHash, repeatedStep.StateHash);
    }

    [Fact]
    public void Restart_requires_a_terminal_run_and_preserves_configuration()
    {
        var running = SnakeRun.Create(70UL);
        Assert.Throws<InvalidOperationException>(() => running.Restart(71UL));

        var terminal = CreateRun(
            body: [new GridPoint(1, 1)],
            direction: Direction.Right,
            food: new GridPoint(4, 3),
            hungerTicksRemaining: 1);
        terminal.Step();
        var terminalHash = terminal.ComputeStateHash();

        var restarted = terminal.Restart(72UL);

        Assert.Equal(RunStatus.Dead, terminal.Status);
        Assert.Equal(terminalHash, terminal.ComputeStateHash());
        Assert.Equal(RunStatus.Running, restarted.Status);
        Assert.Equal(0, restarted.Tick);
        Assert.Equal(new GridPoint(2, 2), restarted.Head);
        Assert.Equal(100, restarted.HungerTicksRemaining);
        Assert.NotEqual(terminalHash, restarted.ComputeStateHash());
    }

    [Fact]
    public void Snapshot_is_a_detached_copy_of_mutable_sequences()
    {
        var run = SnakeRun.Create(22UL);
        var snapshot = run.GetSnapshot();
        var copiedBody = Assert.IsType<GridPoint[]>(snapshot.Body);
        copiedBody[0] = new GridPoint(0, 0);

        Assert.NotEqual(copiedBody[0], run.Head);
    }

    [Fact]
    public void Hash_changes_when_pending_player_intent_changes()
    {
        var run = SnakeRun.Create(22UL);
        var before = run.ComputeStateHash();

        run.QueueDirection(Direction.Up);

        Assert.NotEqual(before, run.ComputeStateHash());
    }

    [Fact]
    public void Canonical_state_has_a_golden_serialization_and_hash()
    {
        var run = CreateRun(
            body: [new GridPoint(1, 1)],
            direction: Direction.Right,
            food: new GridPoint(4, 3));
        const string expected = """{"schemaVersion":2,"rulesVersion":4,"hashAlgorithm":"fnv1a64-canonical-json-v3","rngAlgorithm":"pcg-xsh-rr-32-v1","config":{"width":5,"height":4,"rulesTickMilliseconds":50,"starvationTicks":100,"maximumDirectionQueue":3,"foodScore":10,"comboWindowTicks":60,"speedBonusTicks":30,"powerSpawnIntervalTicks":300,"powerVisibleTicks":120,"shieldDurationTicks":100,"phaseShiftDurationTicks":100,"lastStandRecoveryTicks":60,"slowMoDurationTicks":120,"boostDurationTicks":80,"magnetDurationTicks":120,"gluttonyDurationTicks":100,"segmentDetachObstacleTicks":200,"segmentDetachMaxSegments":5},"tick":0,"status":0,"deathCause":0,"direction":1,"score":0,"comboCount":0,"ticksSinceLastFood":0,"hungerTicksRemaining":100,"powerSpawnTicksElapsed":0,"shieldTicksRemaining":0,"phaseShiftTicksRemaining":0,"lastStandHeld":false,"lastStandRecoveryTicksRemaining":0,"slowMoTicksRemaining":0,"boostTicksRemaining":0,"magnetTicksRemaining":0,"gluttonyTicksRemaining":0,"detachedObstacleTicksRemaining":0,"baitPosition":null,"detachedObstacles":[],"powerPickup":null,"random":{"state":"1","increment":"109"},"food":{"x":4,"y":3},"body":[{"x":1,"y":1}],"pendingDirections":[]}""";

        Assert.Equal(expected, run.SerializeCanonicalState());
        Assert.Equal("70fedaec76d81bce", run.ComputeStateHash());
    }

    [Fact]
    public void State_hash_includes_every_configuration_field()
    {
        var first = SnakeRun.CreateForTesting(
            new RunConfig(Width: 5, Height: 4, MaximumDirectionQueue: 3),
            [new GridPoint(1, 1)],
            Direction.Right,
            new GridPoint(4, 3),
            hungerTicksRemaining: 100);
        var second = SnakeRun.CreateForTesting(
            new RunConfig(Width: 5, Height: 4, MaximumDirectionQueue: 2),
            [new GridPoint(1, 1)],
            Direction.Right,
            new GridPoint(4, 3),
            hungerTicksRemaining: 100);

        Assert.NotEqual(first.ComputeStateHash(), second.ComputeStateHash());
    }

    [Theory]
    [InlineData(0, 1.0)]
    [InlineData(1, 1.3333333333333333)]
    [InlineData(2, 1.6666666666666665)]
    [InlineData(3, 2.0)]
    [InlineData(4, 2.5)]
    [InlineData(5, 3.0)]
    [InlineData(7, 3.8)]
    [InlineData(10, 5.0)]
    [InlineData(15, 7.5)]
    [InlineData(20, 10.0)]
    [InlineData(30, 10.0)]
    public void Combo_curve_matches_python_scoring_contract(int comboCount, double expectedMultiplier)
    {
        var run = SnakeRun.CreateForTesting(
            new RunConfig(Width: 5, Height: 4),
            [new GridPoint(1, 1)],
            Direction.Right,
            new GridPoint(2, 1),
            hungerTicksRemaining: 100,
            comboCount: comboCount);

        Assert.Equal(expectedMultiplier, run.ComboMultiplier, precision: 12);
    }

    [Fact]
    public void Food_scoring_uses_next_combo_speed_bonus_and_length_bonus()
    {
        var body = new[]
        {
            new GridPoint(0, 0),
            new GridPoint(1, 0),
            new GridPoint(2, 0),
            new GridPoint(3, 0),
            new GridPoint(4, 0),
            new GridPoint(4, 1),
            new GridPoint(3, 1),
            new GridPoint(2, 1),
            new GridPoint(1, 1),
            new GridPoint(0, 1),
            new GridPoint(0, 2),
        };
        var run = SnakeRun.CreateForTesting(
            new RunConfig(Width: 6, Height: 4),
            body,
            Direction.Right,
            new GridPoint(1, 2),
            hungerTicksRemaining: 100,
            score: 100,
            comboCount: 4,
            ticksSinceLastFood: 5);

        run.Step();

        Assert.Equal(137, run.Score);
        Assert.Equal(5, run.ComboCount);
        Assert.Equal(3.0, run.ComboMultiplier);
    }

    [Fact]
    public void Expired_combo_clears_streak_without_rewriting_elapsed_ticks()
    {
        var run = SnakeRun.CreateForTesting(
            new RunConfig(Width: 5, Height: 4, ComboWindowTicks: 3, SpeedBonusTicks: 2),
            [new GridPoint(1, 1)],
            Direction.Right,
            new GridPoint(4, 3),
            hungerTicksRemaining: 100,
            comboCount: 4,
            ticksSinceLastFood: 3);

        run.Step();

        Assert.Equal(0, run.ComboCount);
        Assert.Equal(4, run.TicksSinceLastFood);
        Assert.Equal(1.0, run.ComboMultiplier);
    }

    [Theory]
    [InlineData(0UL)]
    [InlineData(1UL)]
    [InlineData(42UL)]
    [InlineData(ulong.MaxValue)]
    public void Long_seeded_runs_replay_step_for_step(ulong seed)
    {
        var first = SnakeRun.Create(seed, new RunConfig(StarvationTicks: 1_000));
        var second = SnakeRun.Create(seed, new RunConfig(StarvationTicks: 1_000));

        for (var step = 0; step < 250; step++)
        {
            if (step % 17 == 0)
            {
                var direction = (Direction)((step / 17) % 4);
                Assert.Equal(first.QueueDirection(direction), second.QueueDirection(direction));
            }

            Assert.Equal(first.Step(), second.Step());
            var firstSnapshot = first.GetSnapshot();
            var secondSnapshot = second.GetSnapshot();
            Assert.Equal(firstSnapshot.StateHash, secondSnapshot.StateHash);
            Assert.Equal(firstSnapshot.Body, secondSnapshot.Body);
            Assert.Equal(firstSnapshot.PendingDirections, secondSnapshot.PendingDirections);

            if (first.Status != RunStatus.Running)
            {
                break;
            }
        }
    }

    [Fact]
    public void Test_state_validation_rejects_invalid_body_and_food()
    {
        var config = new RunConfig(Width: 5, Height: 4);

        Assert.Throws<ArgumentException>(() => CreateRun([], Direction.Right, new GridPoint(1, 1), config: config));
        Assert.Throws<ArgumentException>(() => CreateRun(
            [new GridPoint(1, 1), new GridPoint(1, 1)],
            Direction.Right,
            new GridPoint(2, 1),
            config: config));
        Assert.Throws<ArgumentException>(() => CreateRun(
            [new GridPoint(5, 1)],
            Direction.Right,
            new GridPoint(2, 1),
            config: config));
        Assert.Throws<ArgumentException>(() => CreateRun(
            [new GridPoint(1, 1)],
            Direction.Right,
            new GridPoint(1, 1),
            config: config));
    }

    [Fact]
    public void Test_state_validation_rejects_invalid_counters_and_direction()
    {
        var config = new RunConfig(Width: 5, Height: 4);
        var body = new[] { new GridPoint(1, 1) };
        var food = new GridPoint(2, 1);

        Assert.Throws<ArgumentOutOfRangeException>(() => SnakeRun.CreateForTesting(
            config, body, (Direction)99, food, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => SnakeRun.CreateForTesting(
            config, body, Direction.Right, food, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => SnakeRun.CreateForTesting(
            config, body, Direction.Right, food, config.StarvationTicks + 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => SnakeRun.CreateForTesting(
            config, body, Direction.Right, food, 1, score: -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => SnakeRun.CreateForTesting(
            config, body, Direction.Right, food, 1, comboCount: -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => SnakeRun.CreateForTesting(
            config, body, Direction.Right, food, 1, ticksSinceLastFood: -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => SnakeRun.CreateForTesting(
            config, body, Direction.Right, food, 1, tick: -1));
    }

    private static SnakeRun CreateRun(
        IEnumerable<GridPoint> body,
        Direction direction,
        GridPoint? food,
        int hungerTicksRemaining = 100,
        RunConfig? config = null)
    {
        return SnakeRun.CreateForTesting(
            config ?? new RunConfig(Width: 5, Height: 4, StarvationTicks: 100),
            body,
            direction,
            food,
            hungerTicksRemaining);
    }
}
