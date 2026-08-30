using System.Text.Json;

namespace VibeSnake.Rules.Tests;

public sealed class SharedPhaseShiftTraceParityTests
{
    [Fact]
    public void Csharp_matches_reviewed_python_origin_phase_shift_traces()
    {
        var fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "phase_shift_rules_v1.json");
        var fixture = JsonSerializer.Deserialize<PhaseFixture>(
            File.ReadAllText(fixturePath),
            TestJsonSerializerOptions.SnakeCase);

        Assert.NotNull(fixture);
        Assert.Equal(1, fixture.SchemaVersion);
        Assert.Equal("phase-shift-rules-targeted-v1", fixture.Contract);
        Assert.Equal(SnakeRun.RulesetId, fixture.Ruleset.Id);
        Assert.Equal(SnakeRun.RulesVersion, fixture.Ruleset.Version);
        Assert.Equal("positions-and-power-state-injected-v1", fixture.RandomnessPolicy);
        Assert.Equal("python-production-phase-shift-v1", fixture.SourceEngine);
        Assert.Equal(
            [
                "pickup_identity",
                "collection_on_entry",
                "activation",
                "duration_countdown",
                "pickup_expiry",
                "effect_expiry",
                "self_collision_phasing",
                "body_overlap",
                "starvation_bypass",
                "ordered_power_events",
            ],
            fixture.ComparisonScope);
        Assert.Equal(
            [
                "random_spawn_position",
                "spawn_schedule",
                "presentation_feedback",
                "detached_obstacles",
                "other_power_types",
            ],
            fixture.ExcludedScope);
        Assert.Equal(64, fixture.Config.Width);
        Assert.Equal(33, fixture.Config.Height);
        Assert.Equal(600, fixture.Config.StarvationTicks);
        Assert.Equal(120, fixture.Config.PowerVisibleTicks);
        Assert.Equal(100, fixture.Config.PhaseShiftDurationTicks);
        Assert.Equal(6, fixture.CaseCount);
        Assert.Equal(fixture.CaseCount, fixture.Cases.Count);
        Assert.Equal(
            [
                "phase-shift-collect-on-entry",
                "phase-shift-pickup-expiry",
                "phase-shift-active-countdown",
                "phase-shift-active-expiry-before-collision",
                "phase-shift-body-overlap",
                "phase-shift-does-not-block-starvation",
            ],
            fixture.Cases.Select(traceCase => traceCase.Id));
        Assert.Equal(
            fixture.Cases.Count,
            fixture.Cases.Select(traceCase => traceCase.Id).Distinct(StringComparer.Ordinal).Count());

        foreach (var traceCase in fixture.Cases)
        {
            ExecuteCase(fixture.Config, traceCase);
        }
    }

    private static void ExecuteCase(PhaseConfig fixtureConfig, PhaseCase traceCase)
    {
        var config = new RunConfig(
            Width: fixtureConfig.Width,
            Height: fixtureConfig.Height,
            StarvationTicks: fixtureConfig.StarvationTicks,
            PowerSpawnIntervalTicks: 0,
            PowerVisibleTicks: fixtureConfig.PowerVisibleTicks,
            PhaseShiftDurationTicks: fixtureConfig.PhaseShiftDurationTicks);
        var initial = traceCase.Initial;
        if (initial.Pickup is not null)
        {
            Assert.Equal("phase_shift", initial.Pickup.Kind);
        }

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
            phaseShiftTicksRemaining: initial.PhaseShiftTicksRemaining);

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
            expected.PhaseShiftTicksRemaining,
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
            snapshot.PhaseShiftTicksRemaining,
        };
        var actualEvents = result.OrderedEvents.Select(NormalizeEvent).ToList();
        if (
            !ParityDivergence.AreEquivalent(expectedState, actualState)
            || !ParityDivergence.AreEquivalent(expected.Events, actualEvents))
        {
            ParityDivergence.ThrowWithBundle(
                new ParityDivergenceRequest(
                    Contract: "phase-shift-rules-targeted-v1",
                    Fixture: "phase_shift_rules_v1.json",
                    TestFilter:
                        "VibeSnake.Rules.Tests.SharedPhaseShiftTraceParityTests."
                        + "Csharp_matches_reviewed_python_origin_phase_shift_traces",
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

    private static PhaseEvent NormalizeEvent(RunEventDetail detail) => new(
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
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unexpected Phase Shift event kind."),
    };

    private static PhasePickup? NormalizePickup(PowerPickup? pickup) =>
        pickup is null
            ? null
            : new PhasePickup(
                NormalizePower(pickup.Kind),
                [pickup.Position.X, pickup.Position.Y],
                pickup.VisibilityTicksRemaining);

    private static PowerKind ParsePower(string value) => value switch
    {
        "phase_shift" => PowerKind.PhaseShift,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown fixture power kind."),
    };

    private static string NormalizePower(PowerKind power) => power switch
    {
        PowerKind.PhaseShift => "phase_shift",
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

    private sealed record PhaseFixture(
        int SchemaVersion,
        string Contract,
        PhaseRuleset Ruleset,
        string RandomnessPolicy,
        string SourceEngine,
        int CaseCount,
        PhaseConfig Config,
        List<string> ComparisonScope,
        List<string> ExcludedScope,
        List<PhaseCase> Cases);

    private sealed record PhaseRuleset(string Id, int Version);

    private sealed record PhaseConfig(
        int Width,
        int Height,
        int StarvationTicks,
        int PowerVisibleTicks,
        int PhaseShiftDurationTicks);

    private sealed record PhaseCase(
        string Id,
        PhaseInitial Initial,
        PhaseExpected Expected);

    private sealed record PhaseInitial(
        List<List<int>> Body,
        string Direction,
        List<int> Food,
        int StarvationTicksElapsed,
        PhasePickup? Pickup,
        int PhaseShiftTicksRemaining);

    private sealed record PhaseExpected(
        int Tick,
        List<int> Head,
        List<List<int>> Body,
        bool Alive,
        string? DeathCause,
        int StarvationTicksElapsed,
        PhasePickup? Pickup,
        int PhaseShiftTicksRemaining,
        List<PhaseEvent> Events);

    private sealed record PhasePickup(
        string Kind,
        List<int> Position,
        int VisibilityTicksRemaining);

    private sealed record PhaseEvent(
        string Kind,
        List<int>? Position,
        int? Value,
        string? DeathCause,
        string? Power);
}
