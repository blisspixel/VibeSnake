using System.Text.Json;

namespace VibeSnake.Rules.Tests;

public sealed class SharedRemainingPowerTraceParityTests
{
    [Fact]
    public void Csharp_matches_reviewed_python_origin_remaining_power_traces()
    {
        var fixturePath = Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "remaining_powers_rules_v1.json");
        var fixture = JsonSerializer.Deserialize<RemainingFixture>(
            File.ReadAllText(fixturePath),
            TestJsonSerializerOptions.SnakeCase);

        Assert.NotNull(fixture);
        Assert.Equal(1, fixture.SchemaVersion);
        Assert.Equal("remaining-powers-rules-targeted-v1", fixture.Contract);
        Assert.Equal(SnakeRun.RulesetId, fixture.Ruleset.Id);
        Assert.Equal(SnakeRun.RulesVersion, fixture.Ruleset.Version);
        Assert.Equal("positions-and-power-state-injected-v1", fixture.RandomnessPolicy);
        Assert.Equal("python-production-remaining-powers-v1", fixture.SourceEngine);
        Assert.Equal(
            [
                "pickup_identity",
                "collection_on_entry",
                "activation",
                "duration_countdown",
                "magnet_pull",
                "gluttony_no_growth",
                "bait_mark",
                "segment_detach_obstacles",
                "tempo_compose",
                "ordered_power_events",
            ],
            fixture.ComparisonScope);
        Assert.Equal(
            [
                "random_spawn_position",
                "spawn_schedule",
                "presentation_feedback",
                "food_respawn_position_after_eat",
                "shield_phase_last_stand",
            ],
            fixture.ExcludedScope);
        Assert.Equal(64, fixture.Config.Width);
        Assert.Equal(33, fixture.Config.Height);
        Assert.Equal(600, fixture.Config.StarvationTicks);
        Assert.Equal(120, fixture.Config.PowerVisibleTicks);
        Assert.Equal(120, fixture.Config.SlowMoDurationTicks);
        Assert.Equal(80, fixture.Config.BoostDurationTicks);
        Assert.Equal(120, fixture.Config.MagnetDurationTicks);
        Assert.Equal(100, fixture.Config.GluttonyDurationTicks);
        Assert.Equal(200, fixture.Config.SegmentDetachObstacleTicks);
        Assert.Equal(5, fixture.Config.SegmentDetachMaxSegments);
        Assert.Equal(9, fixture.CaseCount);
        Assert.Equal(fixture.CaseCount, fixture.Cases.Count);
        Assert.Equal(
            [
                "slow-mo-collect-on-entry",
                "boost-collect-on-entry",
                "magnet-collect-on-entry",
                "magnet-pull-food-toward-head",
                "gluttony-collect-on-entry",
                "gluttony-eat-without-growth",
                "bait-collect-on-entry",
                "segment-detach-on-entry",
                "tempo-compose-active-countdown",
            ],
            fixture.Cases.Select(traceCase => traceCase.Id));
        Assert.Equal(
            fixture.Cases.Count,
            fixture.Cases.Select(traceCase => traceCase.Id).Distinct(StringComparer.Ordinal).Count());
        string?[] expectedPickupKinds =
        [
            "slow_mo",
            "boost",
            "magnet",
            null,
            "gluttony",
            null,
            "bait",
            "segment_detach",
            null,
        ];
        Assert.Equal(expectedPickupKinds, fixture.Cases.Select(traceCase => traceCase.Initial.Pickup?.Kind));

        AssertFrozenSemantics(fixture.Cases.ToDictionary(traceCase => traceCase.Id, StringComparer.Ordinal));

        foreach (var traceCase in fixture.Cases)
        {
            ExecuteCase(fixture.Config, traceCase);
        }
    }

    private static void ExecuteCase(RemainingConfig fixtureConfig, RemainingCase traceCase)
    {
        var config = new RunConfig(
            Width: fixtureConfig.Width,
            Height: fixtureConfig.Height,
            StarvationTicks: fixtureConfig.StarvationTicks,
            PowerSpawnIntervalTicks: 0,
            PowerVisibleTicks: fixtureConfig.PowerVisibleTicks,
            SlowMoDurationTicks: fixtureConfig.SlowMoDurationTicks,
            BoostDurationTicks: fixtureConfig.BoostDurationTicks,
            MagnetDurationTicks: fixtureConfig.MagnetDurationTicks,
            GluttonyDurationTicks: fixtureConfig.GluttonyDurationTicks,
            SegmentDetachObstacleTicks: fixtureConfig.SegmentDetachObstacleTicks,
            SegmentDetachMaxSegments: fixtureConfig.SegmentDetachMaxSegments);
        var initial = traceCase.Initial;
        var run = SnakeRun.CreateForTesting(
            config,
            initial.Body.Select(ToGridPoint),
            Enum.Parse<Direction>(initial.Direction, ignoreCase: true),
            initial.Food is null ? null : ToGridPoint(initial.Food),
            fixtureConfig.StarvationTicks - initial.StarvationTicksElapsed,
            powerPickup: initial.Pickup is null
                ? null
                : new PowerPickup(
                    ParsePower(initial.Pickup.Kind),
                    ToGridPoint(initial.Pickup.Position),
                    initial.Pickup.VisibilityTicksRemaining),
            slowMoTicksRemaining: initial.SlowMoTicksRemaining,
            boostTicksRemaining: initial.BoostTicksRemaining,
            magnetTicksRemaining: initial.MagnetTicksRemaining,
            gluttonyTicksRemaining: initial.GluttonyTicksRemaining,
            baitPosition: initial.BaitPosition is null
                ? null
                : ToGridPoint(initial.BaitPosition),
            detachedObstacles: initial.DetachedObstacles.Select(ToGridPoint),
            detachedObstacleTicksRemaining: initial.DetachedObstacleTicksRemaining);

        var result = run.Step();
        var snapshot = run.GetSnapshot();
        var expected = traceCase.Expected;
        Assert.Equal(traceCase.SkipFoodAfterEat, expected.SkipFood);
        var skipFood = expected.SkipFood;
        var expectedState = new
        {
            expected.Tick,
            expected.Head,
            expected.Body,
            expected.Alive,
            expected.DeathCause,
            expected.StarvationTicksElapsed,
            expected.Pickup,
            Food = skipFood ? null : expected.Food,
            expected.SlowMoTicksRemaining,
            expected.BoostTicksRemaining,
            expected.MagnetTicksRemaining,
            expected.GluttonyTicksRemaining,
            expected.BaitPosition,
            expected.DetachedObstacles,
            expected.DetachedObstacleTicksRemaining,
            expected.MovementCadenceNumerator,
            expected.MovementCadenceDenominator,
        };
        var actualState = new
        {
            snapshot.Tick,
            Head = new[] { snapshot.Head.X, snapshot.Head.Y },
            Body = snapshot.Body.Select(point => new[] { point.X, point.Y }).ToList(),
            Alive = snapshot.Status == RunStatus.Running,
            DeathCause = NormalizeDeathCause(snapshot.DeathCause),
            StarvationTicksElapsed = fixtureConfig.StarvationTicks - snapshot.HungerTicksRemaining,
            Pickup = NormalizePickup(snapshot.PowerPickup),
            Food = skipFood
                ? null
                : snapshot.Food is { } food
                    ? new[] { food.X, food.Y }
                    : null,
            snapshot.SlowMoTicksRemaining,
            snapshot.BoostTicksRemaining,
            snapshot.MagnetTicksRemaining,
            snapshot.GluttonyTicksRemaining,
            BaitPosition = snapshot.BaitPosition is { } bait
                ? new[] { bait.X, bait.Y }
                : null,
            DetachedObstacles = snapshot.DetachedObstacles
                .Select(point => new[] { point.X, point.Y })
                .ToList(),
            snapshot.DetachedObstacleTicksRemaining,
            snapshot.MovementCadenceNumerator,
            snapshot.MovementCadenceDenominator,
        };
        var actualEvents = result.OrderedEvents.Select(NormalizeEvent).ToList();
        if (
            !ParityDivergence.AreEquivalent(expectedState, actualState)
            || !ParityDivergence.AreEquivalent(expected.Events, actualEvents))
        {
            ParityDivergence.ThrowWithBundle(
                new ParityDivergenceRequest(
                    Contract: "remaining-powers-rules-targeted-v1",
                    Fixture: "remaining_powers_rules_v1.json",
                    TestFilter:
                        "VibeSnake.Rules.Tests.SharedRemainingPowerTraceParityTests."
                        + "Csharp_matches_reviewed_python_origin_remaining_power_traces",
                    CaseId: traceCase.Id,
                    Seed: null,
                    FirstDivergentStep: expected.Tick,
                    InitialState: traceCase.Initial,
                    CommandPrefix: Array.Empty<string>(),
                    ExpectedState: expectedState,
                    ExpectedEvents: expected.Events,
                    ActualState: actualState,
                    ActualEvents: actualEvents,
                    ActualCanonicalState: JsonSerializer.Deserialize<JsonElement>(
                        run.SerializeCanonicalState()),
                    ActualStateHash: result.StateHash));
        }
    }

    private static void AssertFrozenSemantics(
        IReadOnlyDictionary<string, RemainingCase> cases)
    {
        Assert.All(cases, pair =>
        {
            var traceCase = pair.Value;
            var skipsRespawn = pair.Key == "gluttony-eat-without-growth";
            Assert.Equal(skipsRespawn, traceCase.SkipFoodAfterEat);
            Assert.Equal(traceCase.SkipFoodAfterEat, traceCase.Expected.SkipFood);
            Assert.True(traceCase.Expected.Alive);
            Assert.Null(traceCase.Expected.DeathCause);
            Assert.Equal(1, traceCase.Expected.Tick);
            Assert.Null(traceCase.Expected.Pickup);
        });

        Assert.All(
            cases.Values.Where(traceCase => traceCase.Initial.Pickup is not null),
            traceCase => Assert.Equal(10, traceCase.Initial.Pickup!.VisibilityTicksRemaining));

        var slowMo = cases["slow-mo-collect-on-entry"].Expected;
        Assert.Equal(120, slowMo.SlowMoTicksRemaining);
        Assert.Equal(2, slowMo.MovementCadenceNumerator);
        Assert.Equal(1, slowMo.MovementCadenceDenominator);
        Assert.Equal(
            ["moved", "power_collected", "power_activated"],
            EventKinds(slowMo));
        Assert.Equal(120, slowMo.Events[^1].Value);

        var boost = cases["boost-collect-on-entry"].Expected;
        Assert.Equal(80, boost.BoostTicksRemaining);
        Assert.Equal(1, boost.MovementCadenceNumerator);
        Assert.Equal(2, boost.MovementCadenceDenominator);
        Assert.Equal(
            ["moved", "power_collected", "power_activated"],
            EventKinds(boost));
        Assert.Equal(80, boost.Events[^1].Value);

        var magnetCollection = cases["magnet-collect-on-entry"].Expected;
        Assert.Equal(120, magnetCollection.MagnetTicksRemaining);
        Assert.Equal<int>([20, 20], magnetCollection.Food!);
        Assert.Equal(120, magnetCollection.Events[^1].Value);

        var magnetPull = cases["magnet-pull-food-toward-head"].Expected;
        Assert.Equal<int>([3, 2], magnetPull.Head);
        Assert.Equal<int>([5, 4], magnetPull.Food!);
        Assert.Equal(2, magnetPull.MagnetTicksRemaining);
        Assert.Equal(["moved"], EventKinds(magnetPull));

        var gluttonyCollection = cases["gluttony-collect-on-entry"].Expected;
        Assert.Equal(100, gluttonyCollection.GluttonyTicksRemaining);
        Assert.Equal(100, gluttonyCollection.Events[^1].Value);

        var gluttonyEat = cases["gluttony-eat-without-growth"].Expected;
        Assert.Equal(["2,1", "3,1"], PointStrings(gluttonyEat.Body));
        Assert.Null(gluttonyEat.Food);
        Assert.Equal(2, gluttonyEat.GluttonyTicksRemaining);
        Assert.Equal(0, gluttonyEat.StarvationTicksElapsed);
        Assert.Equal(
            ["moved", "ate_food", "score_changed", "hunger_reset"],
            EventKinds(gluttonyEat));
        Assert.Equal(18, gluttonyEat.Events[2].Value);
        Assert.Equal(600, gluttonyEat.Events[3].Value);

        var bait = cases["bait-collect-on-entry"].Expected;
        Assert.Equal<int>([6, 5], bait.BaitPosition!);
        Assert.Equal(
            ["moved", "power_collected", "power_activated"],
            EventKinds(bait));
        Assert.Equal<int>([6, 5], bait.Events[^1].Position!);
        Assert.Equal(0, bait.Events[^1].Value);

        var detach = cases["segment-detach-on-entry"].Expected;
        Assert.Equal(["6,1"], PointStrings(detach.Body));
        Assert.Equal(
            ["1,1", "2,1", "3,1", "4,1", "5,1"],
            PointStrings(detach.DetachedObstacles));
        Assert.Equal(200, detach.DetachedObstacleTicksRemaining);
        Assert.Equal(
            ["moved", "power_collected", "power_activated"],
            EventKinds(detach));
        Assert.Equal(5, detach.Events[^1].Value);

        var tempo = cases["tempo-compose-active-countdown"].Expected;
        Assert.Equal(2, tempo.SlowMoTicksRemaining);
        Assert.Equal(1, tempo.BoostTicksRemaining);
        Assert.Equal(2, tempo.MovementCadenceNumerator);
        Assert.Equal(2, tempo.MovementCadenceDenominator);
        Assert.Equal(["moved"], EventKinds(tempo));
    }

    private static string[] EventKinds(RemainingExpected expected) =>
        expected.Events.Select(detail => detail.Kind).ToArray();

    private static string[] PointStrings(IEnumerable<List<int>> points) =>
        points.Select(point => string.Join(',', point)).ToArray();

    private static RemainingEvent NormalizeEvent(RunEventDetail detail) => new(
        NormalizeEventKind(detail.Kind),
        detail.Position is { } position ? [position.X, position.Y] : null,
        detail.Value,
        detail.Cause is { } cause ? NormalizeDeathCause(cause) : null,
        detail.Power is { } power ? NormalizePower(power) : null);

    private static string NormalizeEventKind(RunEventKind kind) => kind switch
    {
        RunEventKind.Moved => "moved",
        RunEventKind.Wrapped => "wrapped",
        RunEventKind.AteFood => "ate_food",
        RunEventKind.ScoreChanged => "score_changed",
        RunEventKind.HungerReset => "hunger_reset",
        RunEventKind.Died => "died",
        RunEventKind.Won => "won",
        RunEventKind.PowerSpawned => "power_spawned",
        RunEventKind.PowerCollected => "power_collected",
        RunEventKind.PowerActivated => "power_activated",
        RunEventKind.PowerExpired => "power_expired",
        RunEventKind.PowerConsumed => "power_consumed",
        RunEventKind.PowerDiscarded => "power_discarded",
        RunEventKind.CollisionPrevented => "collision_prevented",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unexpected event kind."),
    };

    private static RemainingPickup? NormalizePickup(PowerPickup? pickup) =>
        pickup is null
            ? null
            : new RemainingPickup(
                NormalizePower(pickup.Kind),
                [pickup.Position.X, pickup.Position.Y],
                pickup.VisibilityTicksRemaining);

    private static PowerKind ParsePower(string kind) => kind switch
    {
        "slow_mo" => PowerKind.SlowMo,
        "boost" => PowerKind.Boost,
        "magnet" => PowerKind.Magnet,
        "bait" => PowerKind.Bait,
        "gluttony" => PowerKind.Gluttony,
        "segment_detach" => PowerKind.SegmentDetach,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown power kind."),
    };

    private static string NormalizePower(PowerKind power) => power switch
    {
        PowerKind.SlowMo => "slow_mo",
        PowerKind.Boost => "boost",
        PowerKind.Magnet => "magnet",
        PowerKind.Bait => "bait",
        PowerKind.Gluttony => "gluttony",
        PowerKind.SegmentDetach => "segment_detach",
        _ => throw new ArgumentOutOfRangeException(nameof(power), power, "Unknown power kind."),
    };

    private static string? NormalizeDeathCause(DeathCause cause) => cause switch
    {
        DeathCause.None => null,
        DeathCause.SelfCollision => "self_collision",
        DeathCause.Starvation => "starvation",
        _ => throw new ArgumentOutOfRangeException(nameof(cause), cause, "Unknown death cause."),
    };

    private static GridPoint ToGridPoint(IReadOnlyList<int> coordinates)
    {
        Assert.Equal(2, coordinates.Count);
        return new GridPoint(coordinates[0], coordinates[1]);
    }

    private sealed record RemainingFixture(
        int SchemaVersion,
        string Contract,
        RemainingRuleset Ruleset,
        string RandomnessPolicy,
        string SourceEngine,
        int CaseCount,
        RemainingConfig Config,
        List<string> ComparisonScope,
        List<string> ExcludedScope,
        List<RemainingCase> Cases);

    private sealed record RemainingRuleset(string Id, int Version);

    private sealed record RemainingConfig(
        int Width,
        int Height,
        int StarvationTicks,
        int PowerVisibleTicks,
        int SlowMoDurationTicks,
        int BoostDurationTicks,
        int MagnetDurationTicks,
        int GluttonyDurationTicks,
        int SegmentDetachObstacleTicks,
        int SegmentDetachMaxSegments);

    private sealed record RemainingCase(
        string Id,
        bool SkipFoodAfterEat,
        RemainingInitial Initial,
        RemainingExpected Expected);

    private sealed record RemainingInitial(
        List<List<int>> Body,
        string Direction,
        List<int>? Food,
        int StarvationTicksElapsed,
        RemainingPickup? Pickup,
        int SlowMoTicksRemaining,
        int BoostTicksRemaining,
        int MagnetTicksRemaining,
        int GluttonyTicksRemaining,
        List<int>? BaitPosition,
        List<List<int>> DetachedObstacles,
        int DetachedObstacleTicksRemaining);

    private sealed record RemainingExpected(
        int Tick,
        List<int> Head,
        List<List<int>> Body,
        bool Alive,
        string? DeathCause,
        int StarvationTicksElapsed,
        RemainingPickup? Pickup,
        List<int>? Food,
        int SlowMoTicksRemaining,
        int BoostTicksRemaining,
        int MagnetTicksRemaining,
        int GluttonyTicksRemaining,
        List<int>? BaitPosition,
        List<List<int>> DetachedObstacles,
        int DetachedObstacleTicksRemaining,
        int MovementCadenceNumerator,
        int MovementCadenceDenominator,
        List<RemainingEvent> Events,
        bool SkipFood);

    private sealed record RemainingPickup(
        string Kind,
        List<int> Position,
        int VisibilityTicksRemaining);

    private sealed record RemainingEvent(
        string Kind,
        List<int>? Position,
        int? Value,
        string? DeathCause,
        string? Power);
}
