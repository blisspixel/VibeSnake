using System.Text.Json;

namespace RepositoryChecks;

public static class CoreRulesFixtureCheck
{
    public const int SchemaVersion = 4;
    public const int CaseCount = 35;
    public const string Contract = "core-rules-targeted-v4";
    public const string FixtureRelativePath = "tests/fixtures/shared/core_rules_v4.json";

    private const int ComboWindowTicks = 60;
    private const int FoodScore = 10;
    private const int GridHeight = 33;
    private const int GridWidth = 64;
    private const int MaximumDirectionQueue = 3;
    private const int MaximumScore = 2_000_000_000;
    private const int SpeedBonusTicks = 30;
    private const int StarvationTicks = 600;
    private const string RandomnessPolicy = "positions-injected-or-random-output-normalized-v2";
    private const string SourceEngine = "python-core-reference-v3";

    public static RepositoryCheckResult Inspect(string repositoryRoot)
    {
        try
        {
            var expected = BuildFixtureBytes();
            var actual = FixedCanonicalFixtureFile.Read(
                repositoryRoot,
                FixtureRelativePath,
                "Core Rules fixture");
            if (!actual.AsSpan().SequenceEqual(expected))
            {
                return Failed(
                    "Core Rules fixture is stale or noncanonical; run "
                        + "dotnet run --project native/tools/RepositoryChecks/RepositoryChecks.csproj "
                        + "-- core-rules-write .");
            }

            return Passed("verified", expected.Length);
        }
        catch (Exception exception) when (
            FixedCanonicalFixtureFile.IsExpectedFailure(exception))
        {
            return Failed(FixedCanonicalFixtureFile.SingleLine(exception.Message));
        }
    }

    public static RepositoryCheckResult Write(string repositoryRoot)
    {
        try
        {
            var bytes = BuildFixtureBytes();
            FixedCanonicalFixtureFile.Write(
                repositoryRoot,
                FixtureRelativePath,
                "Core Rules fixture",
                bytes);

            var verification = Inspect(repositoryRoot);
            if (!verification.Passed)
            {
                return new RepositoryCheckResult(
                    "Core Rules fixture",
                    false,
                    string.Empty,
                    verification.Failures
                        .Select(failure => "write verification failed: " + failure)
                        .ToArray());
            }

            return Passed("written", bytes.Length);
        }
        catch (Exception exception) when (
            FixedCanonicalFixtureFile.IsExpectedFailure(exception))
        {
            return Failed(FixedCanonicalFixtureFile.SingleLine(exception.Message));
        }
    }

    internal static byte[] BuildFixtureBytes()
    {
        var cases = Cases();
        return CanonicalFixtureJson.Render("Core Rules fixture", writer =>
        {
            writer.WriteStartObject();
            writer.WriteNumber("case_count", cases.Length);
            writer.WritePropertyName("cases");
            writer.WriteStartArray();
            foreach (var traceCase in cases)
            {
                WriteCase(writer, traceCase);
            }

            writer.WriteEndArray();
            writer.WritePropertyName("comparison_scope");
            WriteStringArray(
                writer,
                [
                    "food_entry",
                    "growth",
                    "base_score",
                    "score_saturation",
                    "speed_bonus",
                    "speed_bonus_boundaries",
                    "combo_interpolation",
                    "combo_expiry",
                    "combo_clock_monotonicity",
                    "length_bonus",
                    "length_bonus_boundaries",
                    "command_acceptance",
                    "queue_capacity",
                    "queue_consumption",
                    "self_collision",
                    "departing_tail",
                    "edge_wrapping",
                    "starvation_progress",
                    "exact_starvation_deadline",
                    "collision_precedence",
                    "full_grid_completion",
                    "food_stability_without_collection",
                    "random_respawn_legality",
                    "random_stream_use",
                    "ordered_events",
                ]);
            writer.WritePropertyName("config");
            writer.WriteStartObject();
            writer.WriteNumber("combo_window_ticks", ComboWindowTicks);
            writer.WriteNumber("food_score", FoodScore);
            writer.WriteNumber("height", GridHeight);
            writer.WriteNumber("maximum_direction_queue", MaximumDirectionQueue);
            writer.WriteNumber("maximum_score", MaximumScore);
            writer.WriteNumber("speed_bonus_ticks", SpeedBonusTicks);
            writer.WriteNumber("starvation_ticks", StarvationTicks);
            writer.WriteNumber("width", GridWidth);
            writer.WriteEndObject();
            writer.WriteString("contract", Contract);
            writer.WritePropertyName("excluded_scope");
            WriteStringArray(writer, ["food_respawn_coordinate", "risk_bonus", "power_effects"]);
            writer.WriteString("randomness_policy", RandomnessPolicy);
            writer.WritePropertyName("ruleset");
            writer.WriteStartObject();
            writer.WriteString("id", "vibesnake-core");
            writer.WriteNumber("version", 4);
            writer.WriteEndObject();
            writer.WriteNumber("schema_version", SchemaVersion);
            writer.WriteString("source_engine", SourceEngine);
            writer.WriteEndObject();
        });
    }

    private static TraceCase[] Cases()
    {
        FixturePoint[] collisionBody =
        [
            new(1, 1),
            new(1, 2),
            new(2, 2),
            new(2, 1),
        ];

        return
        [
            FoodCase("food-entry", 0, 0, 0, 18, 1, 18),
            new(
                "food-buffered-turn",
                new InitialState([new(5, 5)], "RIGHT", new(5, 4)),
                new ExpectedState(
                    [new(5, 5), new(5, 4)],
                    [
                        new("direction_changed", Direction: "UP"),
                        new("moved", Position: new(5, 4)),
                        new("ate_food", Position: new(5, 4)),
                        new("score_changed", Value: 18),
                        new("hunger_reset", Value: StarvationTicks),
                    ],
                    new(5, 4),
                    Score: 18,
                    Combo: 1,
                    Direction: "UP",
                    TicksSinceLastFood: 0,
                    StarvationTicksElapsed: 0,
                    AteFood: true,
                    FoodUnchanged: false,
                    RandomRespawn: "legal_free_cell",
                    RandomUse: "advanced"),
                ["UP"],
                [true]),
            new(
                "queue-rejections-and-consumption",
                new InitialState([new(5, 5)], "RIGHT", null),
                new ExpectedState(
                    [new(5, 4)],
                    [
                        new("direction_changed", Direction: "UP"),
                        new("moved", Position: new(5, 4)),
                    ],
                    new(5, 4),
                    Direction: "UP",
                    PendingDirections: ["LEFT"]),
                ["RIGHT", "LEFT", "UP", "DOWN", "LEFT", "LEFT"],
                [false, false, true, false, true, false]),
            new(
                "queue-capacity",
                new InitialState([new(5, 5)], "RIGHT", null),
                new ExpectedState(
                    [new(5, 4)],
                    [
                        new("direction_changed", Direction: "UP"),
                        new("moved", Position: new(5, 4)),
                    ],
                    new(5, 4),
                    Direction: "UP",
                    PendingDirections: ["LEFT", "DOWN"]),
                ["UP", "LEFT", "DOWN", "RIGHT", "UP"],
                [true, true, true, false, false]),
            FoodCase("combo-before-three", 0, 1, 29, 16, 2, 16),
            FoodCase("combo-threshold-three", 0, 2, 29, 20, 3, 20),
            FoodCase("combo-after-three", 0, 3, 29, 25, 4, 25),
            FoodCase("combo-threshold-five", 0, 4, 29, 30, 5, 30),
            FoodCase("combo-after-five", 0, 5, 29, 34, 6, 34),
            FoodCase("combo-before-ten", 0, 8, 29, 46, 9, 46),
            FoodCase("combo-threshold-ten", 0, 9, 29, 50, 10, 50),
            FoodCase("combo-after-ten", 0, 10, 29, 55, 11, 55),
            FoodCase("combo-before-twenty", 0, 18, 29, 95, 19, 95),
            FoodCase("combo-threshold-twenty", 0, 19, 29, 100, 20, 100),
            FoodCase("combo-after-twenty-cap", 0, 20, 29, 100, 21, 100),
            FoodCase("speed-bonus-last-eligible-tick", 0, 0, 28, 18, 1, 18),
            FoodCase("speed-bonus-exact-boundary", 0, 0, 29, 13, 1, 13),
            FoodCase("speed-bonus-after-boundary", 0, 0, 30, 13, 1, 13),
            new(
                "combo-window-exact-no-food",
                new InitialState(
                    [new(5, 5)],
                    "RIGHT",
                    null,
                    Combo: 4,
                    TicksSinceLastFood: 59),
                new ExpectedState(
                    [new(6, 5)],
                    [new("moved", Position: new(6, 5))],
                    new(6, 5),
                    Combo: 4,
                    TicksSinceLastFood: 60)),
            new(
                "combo-window-expired-no-food",
                new InitialState(
                    [new(5, 5)],
                    "RIGHT",
                    null,
                    Combo: 4,
                    TicksSinceLastFood: 60),
                new ExpectedState(
                    [new(6, 5)],
                    [
                        new("combo_expired", Value: 0),
                        new("moved", Position: new(6, 5)),
                    ],
                    new(6, 5),
                    TicksSinceLastFood: 61)),
            FoodCase("combo-window-exact-food", 0, 4, 59, 30, 5, 30),
            FoodCase(
                "expired-combo-late-food-no-speed-bonus",
                0,
                4,
                60,
                13,
                1,
                13,
                comboExpired: true),
            LengthCase(
                "length-exact-ten",
                [
                    new(0, 5), new(1, 5), new(2, 5), new(3, 5), new(4, 5),
                    new(5, 5), new(6, 5), new(7, 5), new(8, 5),
                ],
                [
                    new(0, 5), new(1, 5), new(2, 5), new(3, 5), new(4, 5),
                    new(5, 5), new(6, 5), new(7, 5), new(8, 5), new(9, 5),
                ],
                new(9, 5),
                13,
                13),
            LengthCase(
                "length-first-bonus",
                [
                    new(0, 5), new(1, 5), new(2, 5), new(3, 5), new(4, 5),
                    new(5, 5), new(6, 5), new(7, 5), new(8, 5), new(9, 5),
                ],
                [
                    new(0, 5), new(1, 5), new(2, 5), new(3, 5), new(4, 5),
                    new(5, 5), new(6, 5), new(7, 5), new(8, 5), new(9, 5),
                    new(10, 5),
                ],
                new(10, 5),
                14,
                14),
            LengthCase(
                "length-above-boundary",
                [
                    new(0, 5), new(1, 5), new(2, 5), new(3, 5), new(4, 5),
                    new(5, 5), new(6, 5), new(7, 5), new(8, 5), new(9, 5),
                    new(10, 5),
                ],
                [
                    new(0, 5), new(1, 5), new(2, 5), new(3, 5), new(4, 5),
                    new(5, 5), new(6, 5), new(7, 5), new(8, 5), new(9, 5),
                    new(10, 5), new(11, 5),
                ],
                new(11, 5),
                15,
                15),
            FoodCase(
                "score-saturation-near-cap",
                MaximumScore - 1,
                0,
                29,
                MaximumScore,
                1,
                1),
            FoodCase(
                "score-at-cap",
                MaximumScore,
                0,
                29,
                MaximumScore,
                1,
                0),
            new(
                "self-collision",
                new InitialState(collisionBody, "DOWN", null),
                new ExpectedState(
                    collisionBody,
                    [new("died", Position: new(2, 2), DeathCause: "self_collision")],
                    new(2, 1),
                    Direction: "DOWN",
                    Alive: false,
                    DeathCause: "self_collision")),
            new(
                "departing-tail-is-safe",
                new InitialState(collisionBody, "LEFT", null),
                new ExpectedState(
                    [new(1, 2), new(2, 2), new(2, 1), new(1, 1)],
                    [new("moved", Position: new(1, 1))],
                    new(1, 1),
                    Direction: "LEFT")),
            new(
                "horizontal-wrap",
                new InitialState([new(63, 10)], "RIGHT", new(5, 5)),
                new ExpectedState(
                    [new(0, 10)],
                    [
                        new("moved", Position: new(0, 10)),
                        new("wrapped", Position: new(0, 10)),
                    ],
                    new(0, 10),
                    Wrapped: true)),
            new(
                "starvation-predeadline",
                new InitialState(
                    [new(5, 5)],
                    "RIGHT",
                    null,
                    StarvationTicksElapsed: 598),
                new ExpectedState(
                    [new(6, 5)],
                    [new("moved", Position: new(6, 5))],
                    new(6, 5),
                    StarvationTicksElapsed: 599)),
            new(
                "starvation-deadline-food-rescue",
                new InitialState(
                    [new(5, 5)],
                    "RIGHT",
                    new(6, 5),
                    StarvationTicksElapsed: 599),
                new ExpectedState(
                    [new(5, 5), new(6, 5)],
                    [
                        new("moved", Position: new(6, 5)),
                        new("ate_food", Position: new(6, 5)),
                        new("score_changed", Value: 18),
                        new("hunger_reset", Value: StarvationTicks),
                        new("score_changed", Value: 1),
                        new("near_miss", Value: 1),
                    ],
                    new(6, 5),
                    Score: 19,
                    Combo: 1,
                    TicksSinceLastFood: 0,
                    StarvationTicksElapsed: 0,
                    AteFood: true,
                    FoodUnchanged: false,
                    RandomRespawn: "legal_free_cell",
                    RandomUse: "advanced")),
            new(
                "starvation-deadline-death",
                new InitialState(
                    [new(5, 5)],
                    "RIGHT",
                    null,
                    StarvationTicksElapsed: 599),
                new ExpectedState(
                    [new(6, 5)],
                    [new("moved", Position: new(6, 5)), new("died", Position: new(6, 5), DeathCause: "starvation")],
                    new(6, 5),
                    StarvationTicksElapsed: 600,
                    Alive: false,
                    DeathCause: "starvation")),
            new(
                "starvation-collision-precedence",
                new InitialState(
                    collisionBody,
                    "DOWN",
                    null,
                    StarvationTicksElapsed: 599),
                new ExpectedState(
                    collisionBody,
                    [new("died", Position: new(2, 2), DeathCause: "self_collision")],
                    new(2, 1),
                    Direction: "DOWN",
                    StarvationTicksElapsed: 600,
                    Alive: false,
                    DeathCause: "self_collision")),
            FullGridVictoryCase(),
        ];
    }

    private static TraceCase FoodCase(
        string id,
        int initialScore,
        int initialCombo,
        int ticksSinceLastFood,
        int expectedScore,
        int expectedCombo,
        int scoreChange,
        bool comboExpired = false)
    {
        var events = new List<FixtureEvent>();
        if (comboExpired)
        {
            events.Add(new FixtureEvent("combo_expired", Value: 0));
        }

        events.Add(new FixtureEvent("moved", Position: new FixturePoint(6, 5)));
        events.Add(new FixtureEvent("ate_food", Position: new FixturePoint(6, 5)));
        events.Add(new FixtureEvent("score_changed", Value: scoreChange));
        events.Add(new FixtureEvent("hunger_reset", Value: StarvationTicks));
        return new TraceCase(
            id,
            new InitialState(
                [new FixturePoint(5, 5)],
                "RIGHT",
                new FixturePoint(6, 5),
                initialScore,
                initialCombo,
                ticksSinceLastFood),
            new ExpectedState(
                [new FixturePoint(5, 5), new FixturePoint(6, 5)],
                events.ToArray(),
                new FixturePoint(6, 5),
                Score: expectedScore,
                Combo: expectedCombo,
                TicksSinceLastFood: 0,
                StarvationTicksElapsed: 0,
                AteFood: true,
                FoodUnchanged: false,
                RandomRespawn: "legal_free_cell",
                RandomUse: "advanced"));
    }

    private static TraceCase LengthCase(
        string id,
        FixturePoint[] initialBody,
        FixturePoint[] expectedBody,
        FixturePoint food,
        int expectedScore,
        int scoreChange) =>
        new(
            id,
            new InitialState(
                initialBody,
                "RIGHT",
                food,
                TicksSinceLastFood: 29),
            new ExpectedState(
                expectedBody,
                [
                    new("moved", Position: food),
                    new("ate_food", Position: food),
                    new("score_changed", Value: scoreChange),
                    new("hunger_reset", Value: StarvationTicks),
                ],
                food,
                Score: expectedScore,
                Combo: 1,
                TicksSinceLastFood: 0,
                StarvationTicksElapsed: 0,
                AteFood: true,
                FoodUnchanged: false,
                RandomRespawn: "legal_free_cell",
                RandomUse: "advanced"));

    private static TraceCase FullGridVictoryCase()
    {
        var initialBody = SerpentineBody(includeFinalCell: false);
        var expectedBody = SerpentineBody(includeFinalCell: true);
        var finalCell = new FixturePoint(GridWidth - 1, GridHeight - 1);
        return new TraceCase(
            "full-grid-victory",
            new InitialState(initialBody, "RIGHT", finalCell),
            new ExpectedState(
                expectedBody,
                [
                    new("moved", Position: finalCell),
                    new("ate_food", Position: finalCell),
                    new("score_changed", Value: 8_063),
                    new("hunger_reset", Value: StarvationTicks),
                    new("won", Position: finalCell),
                ],
                finalCell,
                Score: 8_063,
                Combo: 1,
                TicksSinceLastFood: 0,
                StarvationTicksElapsed: 0,
                AteFood: true,
                Alive: false,
                Won: true,
                FoodUnchanged: false,
                RandomRespawn: "full_grid_no_cell"));
    }

    private static FixturePoint[] SerpentineBody(bool includeFinalCell)
    {
        var points = new List<FixturePoint>(GridWidth * GridHeight);
        for (var y = 0; y < GridHeight; y++)
        {
            if (y % 2 == 0)
            {
                for (var x = 0; x < GridWidth; x++)
                {
                    points.Add(new FixturePoint(x, y));
                }
            }
            else
            {
                for (var x = GridWidth - 1; x >= 0; x--)
                {
                    points.Add(new FixturePoint(x, y));
                }
            }
        }

        if (!includeFinalCell)
        {
            points.RemoveAt(points.Count - 1);
        }

        return points.ToArray();
    }

    private static void WriteCase(Utf8JsonWriter writer, TraceCase traceCase)
    {
        writer.WriteStartObject();
        writer.WritePropertyName("command_acceptance");
        writer.WriteStartArray();
        foreach (var accepted in traceCase.CommandAcceptance ?? [])
        {
            writer.WriteBooleanValue(accepted);
        }

        writer.WriteEndArray();
        writer.WritePropertyName("commands");
        WriteStringArray(writer, traceCase.Commands ?? []);
        writer.WritePropertyName("expected");
        WriteExpected(writer, traceCase.Expected);
        writer.WriteString("id", traceCase.Id);
        writer.WritePropertyName("initial");
        WriteInitial(writer, traceCase.Initial);
        writer.WriteEndObject();
    }

    private static void WriteExpected(Utf8JsonWriter writer, ExpectedState expected)
    {
        writer.WriteStartObject();
        writer.WriteBoolean("alive", expected.Alive);
        writer.WriteBoolean("ate_food", expected.AteFood);
        writer.WritePropertyName("body");
        WritePoints(writer, expected.Body);
        writer.WriteNumber("combo", expected.Combo);
        if (expected.DeathCause is null)
        {
            writer.WriteNull("death_cause");
        }
        else
        {
            writer.WriteString("death_cause", expected.DeathCause);
        }

        writer.WriteString("direction", expected.Direction);
        writer.WritePropertyName("events");
        writer.WriteStartArray();
        foreach (var item in expected.Events)
        {
            WriteEvent(writer, item);
        }

        writer.WriteEndArray();
        writer.WriteBoolean("food_unchanged", expected.FoodUnchanged);
        writer.WritePropertyName("head");
        WritePoint(writer, expected.Head);
        writer.WritePropertyName("pending_directions");
        WriteStringArray(writer, expected.PendingDirections ?? []);
        writer.WriteString("random_respawn", expected.RandomRespawn);
        writer.WriteString("random_use", expected.RandomUse);
        writer.WriteNumber("score", expected.Score);
        writer.WriteNumber("starvation_ticks_elapsed", expected.StarvationTicksElapsed);
        writer.WriteNumber("tick", 1);
        writer.WriteNumber("ticks_since_last_food", expected.TicksSinceLastFood);
        writer.WriteBoolean("won", expected.Won);
        writer.WriteBoolean("wrapped", expected.Wrapped);
        writer.WriteEndObject();
    }

    private static void WriteInitial(Utf8JsonWriter writer, InitialState initial)
    {
        writer.WriteStartObject();
        writer.WritePropertyName("body");
        WritePoints(writer, initial.Body);
        writer.WriteNumber("combo", initial.Combo);
        writer.WriteString("direction", initial.Direction);
        writer.WritePropertyName("food");
        if (initial.Food is { } food)
        {
            WritePoint(writer, food);
        }
        else
        {
            writer.WriteNullValue();
        }

        writer.WriteNumber("score", initial.Score);
        writer.WriteNumber("starvation_ticks_elapsed", initial.StarvationTicksElapsed);
        writer.WriteNumber("ticks_since_last_food", initial.TicksSinceLastFood);
        writer.WriteEndObject();
    }

    private static void WriteEvent(Utf8JsonWriter writer, FixtureEvent item)
    {
        writer.WriteStartObject();
        if (item.DeathCause is not null)
        {
            writer.WriteString("death_cause", item.DeathCause);
        }

        if (item.Direction is not null)
        {
            writer.WriteString("direction", item.Direction);
        }

        writer.WriteString("kind", item.Kind);
        if (item.Position is { } position)
        {
            writer.WritePropertyName("position");
            WritePoint(writer, position);
        }

        if (item.Value is { } value)
        {
            writer.WriteNumber("value", value);
        }

        writer.WriteEndObject();
    }

    private static void WritePoints(Utf8JsonWriter writer, IEnumerable<FixturePoint> points)
    {
        writer.WriteStartArray();
        foreach (var point in points)
        {
            WritePoint(writer, point);
        }

        writer.WriteEndArray();
    }

    private static void WritePoint(Utf8JsonWriter writer, FixturePoint point)
    {
        writer.WriteStartArray();
        writer.WriteNumberValue(point.X);
        writer.WriteNumberValue(point.Y);
        writer.WriteEndArray();
    }

    private static void WriteStringArray(Utf8JsonWriter writer, IEnumerable<string> values)
    {
        writer.WriteStartArray();
        foreach (var value in values)
        {
            writer.WriteStringValue(value);
        }

        writer.WriteEndArray();
    }

    private static RepositoryCheckResult Passed(string operation, int bytes) =>
        new(
            "Core Rules fixture",
            true,
            $"Shared Core Rules fixture {operation}: cases={CaseCount} bytes={bytes}.",
            []);

    private static RepositoryCheckResult Failed(string failure) =>
        new("Core Rules fixture", false, string.Empty, [failure]);

    private readonly record struct FixturePoint(int X, int Y);

    private sealed record FixtureEvent(
        string Kind,
        FixturePoint? Position = null,
        string? Direction = null,
        int? Value = null,
        string? DeathCause = null);

    private sealed record InitialState(
        FixturePoint[] Body,
        string Direction,
        FixturePoint? Food,
        int Score = 0,
        int Combo = 0,
        int TicksSinceLastFood = 0,
        int StarvationTicksElapsed = 0);

    private sealed record ExpectedState(
        FixturePoint[] Body,
        FixtureEvent[] Events,
        FixturePoint Head,
        int Score = 0,
        int Combo = 0,
        string Direction = "RIGHT",
        string[]? PendingDirections = null,
        int TicksSinceLastFood = 1,
        int StarvationTicksElapsed = 1,
        bool Wrapped = false,
        bool AteFood = false,
        bool Alive = true,
        bool Won = false,
        string? DeathCause = null,
        bool FoodUnchanged = true,
        string RandomRespawn = "not_used",
        string RandomUse = "unchanged");

    private sealed record TraceCase(
        string Id,
        InitialState Initial,
        ExpectedState Expected,
        string[]? Commands = null,
        bool[]? CommandAcceptance = null);
}
