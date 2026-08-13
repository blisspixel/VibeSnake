using System.Text.Json;

namespace VibeSnake.Rules.Tests;

public sealed class SharedRemainingPowerTraceParityTests
{
    [Fact]
    public void Csharp_matches_targeted_python_remaining_power_traces()
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
        Assert.Equal(9, fixture.CaseCount);
        Assert.Equal(fixture.CaseCount, fixture.Cases.Count);
        Assert.All(fixture.Cases, traceCase => Assert.False(string.IsNullOrWhiteSpace(traceCase.Id)));
        Assert.Equal(
            fixture.Cases.Count,
            fixture.Cases.Select(traceCase => traceCase.Id).Distinct(StringComparer.Ordinal).Count());

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
        var skipFood = expected.SkipFood || traceCase.SkipFoodAfterEat;
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
                        + "Csharp_matches_targeted_python_remaining_power_traces",
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
        "shield" => PowerKind.Shield,
        "phase_shift" => PowerKind.PhaseShift,
        "last_stand" => PowerKind.LastStand,
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
        PowerKind.Shield => "shield",
        PowerKind.PhaseShift => "phase_shift",
        PowerKind.LastStand => "last_stand",
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
        int CaseCount,
        RemainingConfig Config,
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
