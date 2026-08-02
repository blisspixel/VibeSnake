using System.Text.Json;

namespace VibeSnake.Rules.Tests;

public sealed class SharedShieldTraceParityTests
{
    [Fact]
    public void Csharp_matches_targeted_python_shield_traces()
    {
        var fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "shield_rules_v1.json");
        var fixture = JsonSerializer.Deserialize<ShieldFixture>(
            File.ReadAllText(fixturePath),
            new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            });

        Assert.NotNull(fixture);
        Assert.Equal(1, fixture.SchemaVersion);
        Assert.Equal("shield-rules-targeted-v1", fixture.Contract);
        Assert.Equal(SnakeRun.RulesetId, fixture.Ruleset.Id);
        Assert.Equal(SnakeRun.RulesVersion, fixture.Ruleset.Version);
        Assert.Equal("positions-and-power-state-injected-v1", fixture.RandomnessPolicy);
        Assert.Equal(8, fixture.CaseCount);
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

    private static void ExecuteCase(ShieldConfig fixtureConfig, ShieldCase traceCase)
    {
        var config = new RunConfig(
            Width: fixtureConfig.Width,
            Height: fixtureConfig.Height,
            StarvationTicks: fixtureConfig.StarvationTicks,
            PowerSpawnIntervalTicks: 0,
            PowerVisibleTicks: fixtureConfig.PowerVisibleTicks,
            ShieldDurationTicks: fixtureConfig.ShieldDurationTicks);
        var initial = traceCase.Initial;
        var run = SnakeRun.CreateForTesting(
            config,
            initial.Body.Select(ToGridPoint),
            Enum.Parse<Direction>(initial.Direction, ignoreCase: true),
            ToGridPoint(initial.Food),
            fixtureConfig.StarvationTicks - initial.StarvationTicksElapsed,
            powerPickup: initial.Pickup is null
                ? null
                : new PowerPickup(
                    ParsePower(initial.Pickup.Kind),
                    ToGridPoint(initial.Pickup.Position),
                    initial.Pickup.VisibilityTicksRemaining),
            shieldTicksRemaining: initial.ShieldTicksRemaining);

        var result = run.Step();
        var snapshot = run.GetSnapshot();
        var expected = traceCase.Expected;
        var expectedState = new
        {
            expected.Tick,
            expected.Head,
            expected.Body,
            expected.Alive,
            expected.DeathCause,
            expected.StarvationTicksElapsed,
            expected.Pickup,
            expected.ShieldTicksRemaining,
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
            snapshot.ShieldTicksRemaining,
        };
        var actualEvents = result.OrderedEvents.Select(NormalizeEvent).ToList();
        if (
            !ParityDivergence.AreEquivalent(expectedState, actualState)
            || !ParityDivergence.AreEquivalent(expected.Events, actualEvents))
        {
            ParityDivergence.ThrowWithBundle(
                new ParityDivergenceRequest(
                    Contract: "shield-rules-targeted-v1",
                    Fixture: "shield_rules_v1.json",
                    TestFilter:
                        "VibeSnake.Rules.Tests.SharedShieldTraceParityTests."
                        + "Csharp_matches_targeted_python_shield_traces",
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

    private static ShieldEvent NormalizeEvent(RunEventDetail detail) => new(
        NormalizeEventKind(detail.Kind),
        detail.Position is { } position ? [position.X, position.Y] : null,
        detail.Value,
        detail.Cause is { } cause ? NormalizeDeathCause(cause) : null,
        detail.Power is { } power ? NormalizePower(power) : null);

    private static string NormalizeEventKind(RunEventKind kind) => kind switch
    {
        RunEventKind.Moved => "moved",
        RunEventKind.Wrapped => "wrapped",
        RunEventKind.Died => "died",
        RunEventKind.PowerSpawned => "power_spawned",
        RunEventKind.PowerCollected => "power_collected",
        RunEventKind.PowerActivated => "power_activated",
        RunEventKind.PowerExpired => "power_expired",
        RunEventKind.PowerConsumed => "power_consumed",
        RunEventKind.PowerDiscarded => "power_discarded",
        RunEventKind.CollisionPrevented => "collision_prevented",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unexpected Shield event kind."),
    };

    private static ShieldPickup? NormalizePickup(PowerPickup? pickup) =>
        pickup is null
            ? null
            : new ShieldPickup(
                NormalizePower(pickup.Kind),
                [pickup.Position.X, pickup.Position.Y],
                pickup.VisibilityTicksRemaining);

    private static PowerKind ParsePower(string value) => value switch
    {
        "shield" => PowerKind.Shield,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown fixture power kind."),
    };

    private static string NormalizePower(PowerKind power) => power switch
    {
        PowerKind.Shield => "shield",
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

    private sealed record ShieldFixture(
        int SchemaVersion,
        string Contract,
        ShieldRuleset Ruleset,
        string RandomnessPolicy,
        int CaseCount,
        ShieldConfig Config,
        List<ShieldCase> Cases);

    private sealed record ShieldRuleset(string Id, int Version);

    private sealed record ShieldConfig(
        int Width,
        int Height,
        int StarvationTicks,
        int PowerVisibleTicks,
        int ShieldDurationTicks);

    private sealed record ShieldCase(
        string Id,
        ShieldInitial Initial,
        ShieldExpected Expected);

    private sealed record ShieldInitial(
        List<List<int>> Body,
        string Direction,
        List<int> Food,
        int StarvationTicksElapsed,
        ShieldPickup? Pickup,
        int ShieldTicksRemaining);

    private sealed record ShieldExpected(
        int Tick,
        List<int> Head,
        List<List<int>> Body,
        bool Alive,
        string? DeathCause,
        int StarvationTicksElapsed,
        ShieldPickup? Pickup,
        int ShieldTicksRemaining,
        List<ShieldEvent> Events);

    private sealed record ShieldPickup(
        string Kind,
        List<int> Position,
        int VisibilityTicksRemaining);

    private sealed record ShieldEvent(
        string Kind,
        List<int>? Position,
        int? Value,
        string? DeathCause,
        string? Power);
}
