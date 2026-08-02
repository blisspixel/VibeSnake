using System.Text.Json;

namespace VibeSnake.Rules.Tests;

public sealed class SharedRuleTraceParityTests
{
    [Fact]
    public void Csharp_matches_targeted_python_core_rule_traces()
    {
        var fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "core_rules_v4.json");
        var fixture = JsonSerializer.Deserialize<RuleFixture>(
            File.ReadAllText(fixturePath),
            new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            });

        Assert.NotNull(fixture);
        Assert.Equal(4, fixture.SchemaVersion);
        Assert.Equal("core-rules-targeted-v4", fixture.Contract);
        Assert.Equal(SnakeRun.RulesetId, fixture.Ruleset.Id);
        Assert.Equal(SnakeRun.RulesVersion, fixture.Ruleset.Version);
        Assert.Equal(
            "positions-injected-or-random-output-normalized-v2",
            fixture.RandomnessPolicy);
        Assert.Equal(35, fixture.CaseCount);
        Assert.Equal(fixture.CaseCount, fixture.Cases.Count);
        Assert.All(fixture.Cases, traceCase => Assert.False(string.IsNullOrWhiteSpace(traceCase.Id)));
        Assert.Equal(
            fixture.Cases.Count,
            fixture.Cases.Select(traceCase => traceCase.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(SnakeRun.MaximumScore, fixture.Config.MaximumScore);

        foreach (var traceCase in fixture.Cases)
        {
            ExecuteCase(fixture.Config, traceCase);
        }
    }

    private static void ExecuteCase(RuleConfig fixtureConfig, RuleCase traceCase)
    {
        var config = new RunConfig(
            Width: fixtureConfig.Width,
            Height: fixtureConfig.Height,
            StarvationTicks: fixtureConfig.StarvationTicks,
            MaximumDirectionQueue: fixtureConfig.MaximumDirectionQueue,
            FoodScore: fixtureConfig.FoodScore,
            ComboWindowTicks: fixtureConfig.ComboWindowTicks,
            SpeedBonusTicks: fixtureConfig.SpeedBonusTicks);
        var initial = traceCase.Initial;
        var run = SnakeRun.CreateForTesting(
            config,
            initial.Body.Select(ToGridPoint),
            ParseDirection(initial.Direction),
            initial.Food is null ? null : ToGridPoint(initial.Food),
            fixtureConfig.StarvationTicks - initial.StarvationTicksElapsed,
            score: initial.Score,
            comboCount: initial.Combo,
            ticksSinceLastFood: initial.TicksSinceLastFood);

        var actualCommandAcceptance = new List<bool>(traceCase.Commands.Count);
        foreach (var command in traceCase.Commands)
        {
            actualCommandAcceptance.Add(run.QueueDirection(ParseDirection(command)));
        }

        var randomStateBefore = ReadRandomState(run);
        var result = run.Step();
        var snapshot = run.GetSnapshot();
        var expected = traceCase.Expected;
        var expectedState = new
        {
            expected.Tick,
            CommandAcceptance = traceCase.CommandAcceptance,
            Direction = expected.Direction.ToUpperInvariant(),
            Head = expected.Head,
            expected.Body,
            PendingDirections = expected.PendingDirections.Select(value => value.ToUpperInvariant()).ToList(),
            expected.Score,
            expected.Combo,
            expected.TicksSinceLastFood,
            expected.StarvationTicksElapsed,
            expected.Wrapped,
            expected.AteFood,
            expected.Alive,
            expected.Won,
            expected.DeathCause,
            expected.FoodUnchanged,
            expected.RandomRespawn,
            expected.RandomUse,
        };
        var actualState = new
        {
            snapshot.Tick,
            CommandAcceptance = actualCommandAcceptance,
            Direction = snapshot.Direction.ToString().ToUpperInvariant(),
            Head = new[] { snapshot.Head.X, snapshot.Head.Y },
            Body = snapshot.Body.Select(point => new[] { point.X, point.Y }).ToList(),
            PendingDirections = snapshot.PendingDirections
                .Select(value => value.ToString().ToUpperInvariant())
                .ToList(),
            snapshot.Score,
            Combo = snapshot.ComboCount,
            snapshot.TicksSinceLastFood,
            StarvationTicksElapsed = fixtureConfig.StarvationTicks - snapshot.HungerTicksRemaining,
            Wrapped = result.Events.HasFlag(RunEvent.Wrapped),
            AteFood = result.Events.HasFlag(RunEvent.AteFood),
            Alive = snapshot.Status == RunStatus.Running,
            Won = snapshot.Status == RunStatus.Won,
            DeathCause = NormalizeDeathCause(snapshot.DeathCause),
            FoodUnchanged = snapshot.Food == (initial.Food is null ? null : ToGridPoint(initial.Food)),
            RandomRespawn = NormalizeRandomRespawn(config, result, snapshot),
            RandomUse = randomStateBefore == ReadRandomState(run) ? "unchanged" : "advanced",
        };
        var actualEvents = result.OrderedEvents.Select(NormalizeEvent).ToList();
        if (
            !ParityDivergence.AreEquivalent(expectedState, actualState)
            || !ParityDivergence.AreEquivalent(expected.Events, actualEvents))
        {
            ParityDivergence.ThrowWithBundle(
                new ParityDivergenceRequest(
                    Contract: "core-rules-targeted-v4",
                    Fixture: "core_rules_v4.json",
                    TestFilter:
                        "VibeSnake.Rules.Tests.SharedRuleTraceParityTests."
                        + "Csharp_matches_targeted_python_core_rule_traces",
                    CaseId: traceCase.Id,
                    Seed: null,
                    FirstDivergentStep: expected.Tick,
                    InitialState: traceCase.Initial,
                    CommandPrefix: traceCase.Commands,
                    ExpectedState: expectedState,
                    ExpectedEvents: expected.Events,
                    ActualState: actualState,
                    ActualEvents: actualEvents,
                    ActualCanonicalState: JsonSerializer.Deserialize<JsonElement>(
                        run.SerializeCanonicalState()),
                    ActualStateHash: result.StateHash));
        }
    }

    private static RuleEvent NormalizeEvent(RunEventDetail detail) => new(
        NormalizeEventKind(detail.Kind),
        detail.Position is { } position ? [position.X, position.Y] : null,
        detail.NewDirection?.ToString().ToUpperInvariant(),
        detail.Value,
        detail.Cause is { } cause ? NormalizeDeathCause(cause) : null);

    private static string NormalizeEventKind(RunEventKind kind) => kind switch
    {
        RunEventKind.DirectionChanged => "direction_changed",
        RunEventKind.Moved => "moved",
        RunEventKind.Wrapped => "wrapped",
        RunEventKind.AteFood => "ate_food",
        RunEventKind.ScoreChanged => "score_changed",
        RunEventKind.HungerReset => "hunger_reset",
        RunEventKind.Died => "died",
        RunEventKind.Won => "won",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown event kind."),
    };

    private static string? NormalizeDeathCause(DeathCause cause) => cause switch
    {
        DeathCause.None => null,
        DeathCause.SelfCollision => "self_collision",
        DeathCause.Starvation => "starvation",
        _ => throw new ArgumentOutOfRangeException(nameof(cause), cause, "Unknown death cause."),
    };

    private static string NormalizeRandomRespawn(
        RunConfig config,
        RunStepResult result,
        RunSnapshot snapshot)
    {
        if (!result.Events.HasFlag(RunEvent.AteFood))
        {
            return "not_used";
        }

        if (snapshot.Status == RunStatus.Won)
        {
            return snapshot.Food is null && snapshot.Body.Count == config.Width * config.Height
                ? "full_grid_no_cell"
                : "invalid";
        }

        if (snapshot.Food is not { } food)
        {
            return "invalid";
        }

        var inBounds = food.X >= 0 && food.X < config.Width && food.Y >= 0 && food.Y < config.Height;
        return inBounds && !snapshot.Body.Contains(food) ? "legal_free_cell" : "invalid";
    }

    private static string ReadRandomState(SnakeRun run)
    {
        using var document = JsonDocument.Parse(run.SerializeCanonicalState());
        return document.RootElement
            .GetProperty("random")
            .GetProperty("state")
            .GetString()!;
    }

    private static Direction ParseDirection(string value) => Enum.Parse<Direction>(value, ignoreCase: true);

    private static GridPoint ToGridPoint(IReadOnlyList<int> coordinates)
    {
        Assert.Equal(2, coordinates.Count);
        return new GridPoint(coordinates[0], coordinates[1]);
    }

    private sealed record RuleFixture(
        int SchemaVersion,
        string Contract,
        RuleRuleset Ruleset,
        string RandomnessPolicy,
        int CaseCount,
        RuleConfig Config,
        List<RuleCase> Cases);

    private sealed record RuleRuleset(string Id, int Version);

    private sealed record RuleConfig(
        int Width,
        int Height,
        int StarvationTicks,
        int MaximumDirectionQueue,
        int MaximumScore,
        int ComboWindowTicks,
        int SpeedBonusTicks,
        int FoodScore);

    private sealed record RuleCase(
        string Id,
        RuleInitial Initial,
        List<string> Commands,
        List<bool> CommandAcceptance,
        RuleExpected Expected);

    private sealed record RuleInitial(
        List<List<int>> Body,
        string Direction,
        List<int>? Food,
        int Score,
        int Combo,
        int TicksSinceLastFood,
        int StarvationTicksElapsed);

    private sealed record RuleExpected(
        int Tick,
        string Direction,
        List<int> Head,
        List<List<int>> Body,
        List<string> PendingDirections,
        int Score,
        int Combo,
        int TicksSinceLastFood,
        int StarvationTicksElapsed,
        bool Wrapped,
        bool AteFood,
        bool Alive,
        bool Won,
        string? DeathCause,
        bool FoodUnchanged,
        string RandomRespawn,
        string RandomUse,
        List<RuleEvent> Events);

    private sealed record RuleEvent(
        string Kind,
        List<int>? Position,
        string? Direction,
        int? Value,
        string? DeathCause);
}
