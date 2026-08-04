using System.Text;
using System.Text.Json;

namespace VibeSnake.Rules.Tests;

public sealed class SharedTraceParityTests
{
    private static readonly string[] ExpectedStepEncoding =
    [
        "command_symbols",
        "command_acceptance_bits",
        "direction_symbol",
        "head_x",
        "head_y",
        "body_length",
        "pending_direction_symbols",
        "wrapped",
        "alive",
    ];

    [Fact]
    public void Csharp_matches_all_long_python_movement_and_input_traces()
    {
        var fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "core_movement_v2.json");
        var fixture = JsonSerializer.Deserialize<TraceFixture>(
            File.ReadAllText(fixturePath),
            new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            });

        Assert.NotNull(fixture);
        Assert.Equal(2, fixture.SchemaVersion);
        Assert.Equal("movement-input-long-v2", fixture.Contract);
        Assert.Equal(SnakeRun.RulesetId, fixture.Ruleset.Id);
        Assert.Equal(SnakeRun.RulesVersion, fixture.Ruleset.Version);
        Assert.Equal(
            "positions-injected-or-random-output-normalized-v2",
            fixture.RandomnessPolicy);
        Assert.Equal(100, fixture.CaseCount);
        Assert.Equal(256, fixture.StepsPerCase);
        Assert.Equal(25_600, fixture.TotalSteps);
        Assert.Equal(ExpectedStepEncoding, fixture.StepEncoding);
        Assert.Equal(
            new Dictionary<string, string>
            {
                ["UP"] = "U",
                ["RIGHT"] = "R",
                ["DOWN"] = "D",
                ["LEFT"] = "L",
            },
            fixture.DirectionSymbols);
        Assert.Equal(fixture.CaseCount, fixture.Cases.Count);
        Assert.Equal(
            fixture.Cases.Count,
            fixture.Cases.Select(traceCase => traceCase.Seed).Distinct().Count());
        Assert.Equal(
            fixture.Cases.Count,
            fixture.Cases.Select(traceCase => traceCase.Id).Distinct(StringComparer.Ordinal).Count());

        foreach (var traceCase in fixture.Cases)
        {
            Assert.Equal(fixture.StepsPerCase, traceCase.Steps.Count);
            ExecuteCase(fixture, traceCase);
        }
    }

    private static void ExecuteCase(TraceFixture fixture, TraceCase traceCase)
    {
        var config = new RunConfig(
            Width: fixture.Grid.Width,
            Height: fixture.Grid.Height,
            StarvationTicks: fixture.StepsPerCase + 1);
        var body = traceCase.Initial.Body.Select(ToGridPoint);
        var run = SnakeRun.CreateForTesting(
            config,
            body,
            ParseDirection(traceCase.Initial.Direction),
            food: null,
            hungerTicksRemaining: fixture.StepsPerCase + 1);

        for (var stepIndex = 0; stepIndex < traceCase.Steps.Count; stepIndex++)
        {
            var traceStep = DecodeStep(traceCase.Steps[stepIndex]);
            var actualAcceptance = new StringBuilder(traceStep.CommandSymbols.Length);
            foreach (var commandSymbol in traceStep.CommandSymbols)
            {
                var accepted = run.QueueDirection(ParseDirectionSymbol(commandSymbol));
                actualAcceptance.Append(accepted ? '1' : '0');
            }

            var result = run.Step();
            var snapshot = run.GetSnapshot();
            var expectedState = new
            {
                Tick = stepIndex + 1,
                traceStep.DirectionSymbol,
                Head = new[] { traceStep.HeadX, traceStep.HeadY },
                traceStep.BodyLength,
                traceStep.PendingDirectionSymbols,
                traceStep.CommandAcceptanceBits,
                traceStep.Wrapped,
                traceStep.Alive,
            };
            var actualState = new
            {
                snapshot.Tick,
                DirectionSymbol = DirectionSymbol(snapshot.Direction),
                Head = new[] { snapshot.Head.X, snapshot.Head.Y },
                BodyLength = snapshot.Body.Count,
                PendingDirectionSymbols = string.Concat(
                    snapshot.PendingDirections.Select(DirectionSymbol)),
                CommandAcceptanceBits = actualAcceptance.ToString(),
                Wrapped = result.Events.HasFlag(RunEvent.Wrapped),
                Alive = snapshot.Status == RunStatus.Running,
            };
            if (!ParityDivergence.AreEquivalent(expectedState, actualState))
            {
                var failingPrefix = traceCase.Steps
                    .Take(stepIndex + 1)
                    .Select((step, index) => new
                    {
                        Step = index + 1,
                        CommandSymbols = DecodeStep(step).CommandSymbols,
                    })
                    .ToList();
                var minimized = ParityDeltaReducer.MinimizePrefix(
                    failingPrefix,
                    stillFails: prefix =>
                        MovementPrefixDiverges(fixture, traceCase, prefix.Count));
                ParityDivergence.ThrowWithBundle(
                    new ParityDivergenceRequest(
                        Contract: fixture.Contract,
                        Fixture: "core_movement_v2.json",
                        TestFilter:
                            "VibeSnake.Rules.Tests.SharedTraceParityTests."
                            + "Csharp_matches_all_long_python_movement_and_input_traces",
                        CaseId: traceCase.Id,
                        Seed: traceCase.Seed,
                        FirstDivergentStep: stepIndex + 1,
                        InitialState: traceCase.Initial,
                        CommandPrefix: failingPrefix,
                        ExpectedState: expectedState,
                        ExpectedEvents: Array.Empty<object>(),
                        ActualState: actualState,
                        ActualEvents: result.OrderedEvents,
                        ActualCanonicalState: JsonSerializer.Deserialize<JsonElement>(
                            run.SerializeCanonicalState()),
                        ActualStateHash: result.StateHash,
                        MinimizedCommandPrefix: minimized,
                        MinimizedStepCount: minimized.Count));
            }
        }
    }

    private static bool MovementPrefixDiverges(
        TraceFixture fixture,
        TraceCase traceCase,
        int prefixLength)
    {
        if (prefixLength <= 0 || prefixLength > traceCase.Steps.Count)
        {
            return false;
        }

        var config = new RunConfig(
            Width: fixture.Grid.Width,
            Height: fixture.Grid.Height,
            StarvationTicks: fixture.StepsPerCase + 1);
        var run = SnakeRun.CreateForTesting(
            config,
            traceCase.Initial.Body.Select(ToGridPoint),
            ParseDirection(traceCase.Initial.Direction),
            food: null,
            hungerTicksRemaining: fixture.StepsPerCase + 1);

        for (var stepIndex = 0; stepIndex < prefixLength; stepIndex++)
        {
            var traceStep = DecodeStep(traceCase.Steps[stepIndex]);
            var actualAcceptance = new StringBuilder(traceStep.CommandSymbols.Length);
            foreach (var commandSymbol in traceStep.CommandSymbols)
            {
                var accepted = run.QueueDirection(ParseDirectionSymbol(commandSymbol));
                actualAcceptance.Append(accepted ? '1' : '0');
            }

            var result = run.Step();
            var snapshot = run.GetSnapshot();
            var expectedState = new
            {
                Tick = stepIndex + 1,
                traceStep.DirectionSymbol,
                Head = new[] { traceStep.HeadX, traceStep.HeadY },
                traceStep.BodyLength,
                traceStep.PendingDirectionSymbols,
                traceStep.CommandAcceptanceBits,
                traceStep.Wrapped,
                traceStep.Alive,
            };
            var actualState = new
            {
                snapshot.Tick,
                DirectionSymbol = DirectionSymbol(snapshot.Direction),
                Head = new[] { snapshot.Head.X, snapshot.Head.Y },
                BodyLength = snapshot.Body.Count,
                PendingDirectionSymbols = string.Concat(
                    snapshot.PendingDirections.Select(DirectionSymbol)),
                CommandAcceptanceBits = actualAcceptance.ToString(),
                Wrapped = result.Events.HasFlag(RunEvent.Wrapped),
                Alive = snapshot.Status == RunStatus.Running,
            };
            if (!ParityDivergence.AreEquivalent(expectedState, actualState))
            {
                return stepIndex + 1 == prefixLength;
            }
        }

        return false;
    }

    private static CompactTraceStep DecodeStep(IReadOnlyList<JsonElement> values)
    {
        Assert.Equal(ExpectedStepEncoding.Length, values.Count);
        var commandSymbols = RequireString(values[0]);
        var commandAcceptanceBits = RequireString(values[1]);
        Assert.Equal(commandSymbols.Length, commandAcceptanceBits.Length);
        Assert.All(commandAcceptanceBits, value => Assert.Contains(value, "01"));

        return new CompactTraceStep(
            commandSymbols,
            commandAcceptanceBits,
            RequireString(values[2]),
            values[3].GetInt32(),
            values[4].GetInt32(),
            values[5].GetInt32(),
            RequireString(values[6]),
            values[7].GetBoolean(),
            values[8].GetBoolean());
    }

    private static string RequireString(JsonElement value) =>
        value.GetString() ?? throw new InvalidDataException("A compact trace string cannot be null.");

    private static Direction ParseDirectionSymbol(char symbol) => symbol switch
    {
        'U' => Direction.Up,
        'R' => Direction.Right,
        'D' => Direction.Down,
        'L' => Direction.Left,
        _ => throw new InvalidDataException($"Unknown direction symbol '{symbol}'."),
    };

    private static string DirectionSymbol(Direction direction) => direction switch
    {
        Direction.Up => "U",
        Direction.Right => "R",
        Direction.Down => "D",
        Direction.Left => "L",
        _ => throw new ArgumentOutOfRangeException(nameof(direction), direction, "Unknown direction."),
    };

    private static Direction ParseDirection(string value) =>
        Enum.Parse<Direction>(value, ignoreCase: true);

    private static GridPoint ToGridPoint(IReadOnlyList<int> coordinates)
    {
        Assert.Equal(2, coordinates.Count);
        return new GridPoint(coordinates[0], coordinates[1]);
    }

    private sealed record TraceFixture(
        int SchemaVersion,
        string Contract,
        TraceRuleset Ruleset,
        string RandomnessPolicy,
        int CaseCount,
        int StepsPerCase,
        int TotalSteps,
        TraceGrid Grid,
        Dictionary<string, string> DirectionSymbols,
        List<string> StepEncoding,
        List<TraceCase> Cases);

    private sealed record TraceRuleset(string Id, int Version);

    private sealed record TraceGrid(int Width, int Height);

    private sealed record TraceCase(long Seed, string Id, TraceInitial Initial, List<List<JsonElement>> Steps);

    private sealed record TraceInitial(List<List<int>> Body, string Direction);

    private sealed record CompactTraceStep(
        string CommandSymbols,
        string CommandAcceptanceBits,
        string DirectionSymbol,
        int HeadX,
        int HeadY,
        int BodyLength,
        string PendingDirectionSymbols,
        bool Wrapped,
        bool Alive);
}
