using System.Text.Json;

namespace VibeSnake.Rules.Tests;

public sealed class SharedLastStandTraceParityTests
{
    [Fact]
    public void Csharp_matches_targeted_python_last_stand_traces()
    {
        var fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "last_stand_rules_v1.json");
        var fixture = JsonSerializer.Deserialize<LastStandFixture>(
            File.ReadAllText(fixturePath),
            TestJsonSerializerOptions.SnakeCase);

        Assert.NotNull(fixture);
        Assert.Equal(1, fixture.SchemaVersion);
        Assert.Equal("last-stand-rules-targeted-v1", fixture.Contract);
        Assert.Equal(SnakeRun.RulesetId, fixture.Ruleset.Id);
        Assert.Equal(SnakeRun.RulesVersion, fixture.Ruleset.Version);
        Assert.Equal(5, fixture.CaseCount);
        Assert.Equal(fixture.CaseCount, fixture.Cases.Count);

        foreach (var traceCase in fixture.Cases)
        {
            ExecuteCase(fixture.Config, traceCase);
        }
    }

    private static void ExecuteCase(LastStandConfig fixtureConfig, LastStandCase traceCase)
    {
        var config = new RunConfig(
            Width: fixtureConfig.Width,
            Height: fixtureConfig.Height,
            StarvationTicks: fixtureConfig.StarvationTicks,
            PowerSpawnIntervalTicks: 0,
            PowerVisibleTicks: fixtureConfig.PowerVisibleTicks,
            LastStandRecoveryTicks: fixtureConfig.LastStandRecoveryTicks);
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
                    PowerKind.LastStand,
                    ToGridPoint(initial.Pickup.Position),
                    initial.Pickup.VisibilityTicksRemaining),
            lastStandHeld: initial.LastStandHeld,
            lastStandRecoveryTicksRemaining: initial.RecoveryTicksRemaining);

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
            expected.LastStandHeld,
            expected.RecoveryTicksRemaining,
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
            snapshot.LastStandHeld,
            RecoveryTicksRemaining = snapshot.LastStandRecoveryTicksRemaining,
        };
        var actualEvents = result.OrderedEvents.Select(NormalizeEvent).ToList();
        if (
            !ParityDivergence.AreEquivalent(expectedState, actualState)
            || !ParityDivergence.AreEquivalent(expected.Events, actualEvents))
        {
            ParityDivergence.ThrowWithBundle(
                new ParityDivergenceRequest(
                    Contract: "last-stand-rules-targeted-v1",
                    Fixture: "last_stand_rules_v1.json",
                    TestFilter:
                        "VibeSnake.Rules.Tests.SharedLastStandTraceParityTests."
                        + "Csharp_matches_targeted_python_last_stand_traces",
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

    private static LastStandEvent NormalizeEvent(RunEventDetail detail) => new(
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
        RunEventKind.HungerReset => "hunger_reset",
        RunEventKind.PowerSpawned => "power_spawned",
        RunEventKind.PowerCollected => "power_collected",
        RunEventKind.PowerActivated => "power_activated",
        RunEventKind.PowerExpired => "power_expired",
        RunEventKind.PowerConsumed => "power_consumed",
        RunEventKind.PowerDiscarded => "power_discarded",
        RunEventKind.CollisionPrevented => "collision_prevented",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unexpected Last Stand event kind."),
    };

    private static LastStandPickup? NormalizePickup(PowerPickup? pickup) =>
        pickup is null
            ? null
            : new LastStandPickup(
                "last_stand",
                [pickup.Position.X, pickup.Position.Y],
                pickup.VisibilityTicksRemaining);

    private static string NormalizePower(PowerKind power) => power switch
    {
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

    private sealed record LastStandFixture(
        int SchemaVersion,
        string Contract,
        LastStandRuleset Ruleset,
        int CaseCount,
        LastStandConfig Config,
        List<LastStandCase> Cases);

    private sealed record LastStandRuleset(string Id, int Version);

    private sealed record LastStandConfig(
        int Width,
        int Height,
        int StarvationTicks,
        int PowerVisibleTicks,
        int LastStandRecoveryTicks);

    private sealed record LastStandCase(
        string Id,
        LastStandInitial Initial,
        LastStandExpected Expected);

    private sealed record LastStandInitial(
        List<List<int>> Body,
        string Direction,
        List<int> Food,
        int StarvationTicksElapsed,
        LastStandPickup? Pickup,
        bool LastStandHeld,
        int RecoveryTicksRemaining);

    private sealed record LastStandExpected(
        int Tick,
        List<int> Head,
        List<List<int>> Body,
        bool Alive,
        string? DeathCause,
        int StarvationTicksElapsed,
        LastStandPickup? Pickup,
        bool LastStandHeld,
        int RecoveryTicksRemaining,
        List<LastStandEvent> Events);

    private sealed record LastStandPickup(
        string Kind,
        List<int> Position,
        int VisibilityTicksRemaining);

    private sealed record LastStandEvent(
        string Kind,
        List<int>? Position,
        int? Value,
        string? DeathCause,
        string? Power);
}
