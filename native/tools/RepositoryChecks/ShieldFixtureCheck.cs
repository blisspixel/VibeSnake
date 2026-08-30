using System.Text.Json;

namespace RepositoryChecks;

public static class ShieldFixtureCheck
{
    public const int SchemaVersion = 1;
    public const int CaseCount = 8;
    public const string Contract = "shield-rules-targeted-v1";
    public const string FixtureRelativePath =
        "tests/fixtures/shared/shield_rules_v1.json";

    private const int PowerVisibleTicks = 120;
    private const int ShieldDurationTicks = 100;
    private const int StarvationTicks = 600;
    private const string RandomnessPolicy = "positions-and-power-state-injected-v1";
    private const string SourceEngine = "python-production-shield-v1";

    public static RepositoryCheckResult Inspect(string repositoryRoot)
    {
        try
        {
            var expected = BuildFixtureBytes();
            var actual = FixedCanonicalFixtureFile.Read(
                repositoryRoot,
                FixtureRelativePath,
                "Shield fixture");
            if (!actual.AsSpan().SequenceEqual(expected))
            {
                return Failed(
                    "Shield fixture is stale or noncanonical; run "
                        + "dotnet run --project native/tools/RepositoryChecks/RepositoryChecks.csproj "
                        + "-- shield-write .");
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
                "Shield fixture",
                bytes);

            var verification = Inspect(repositoryRoot);
            if (!verification.Passed)
            {
                return new RepositoryCheckResult(
                    "Shield fixture",
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
        return CanonicalFixtureJson.Render("Shield fixture", writer =>
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
                    "pickup_identity",
                    "collection_on_entry",
                    "activation",
                    "duration_countdown",
                    "pickup_expiry",
                    "effect_expiry",
                    "self_collision_consumption",
                    "collision_prevention",
                    "starvation_bypass",
                    "ordered_power_events",
                ]);
            writer.WritePropertyName("config");
            writer.WriteStartObject();
            writer.WriteNumber("height", 33);
            writer.WriteNumber("power_visible_ticks", PowerVisibleTicks);
            writer.WriteNumber("shield_duration_ticks", ShieldDurationTicks);
            writer.WriteNumber("starvation_ticks", StarvationTicks);
            writer.WriteNumber("width", 64);
            writer.WriteEndObject();
            writer.WriteString("contract", Contract);
            writer.WritePropertyName("excluded_scope");
            WriteStringArray(
                writer,
                [
                    "random_spawn_position",
                    "spawn_schedule",
                    "presentation_feedback",
                    "other_power_types",
                ]);
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
            new(
                "shield-collect-on-entry",
                new InitialState(
                    [new FixturePoint(5, 5)],
                    "RIGHT",
                    new FixturePoint(20, 20),
                    new FixturePickup(new FixturePoint(6, 5), 10),
                    0,
                    0),
                new ExpectedState(
                    true,
                    [new FixturePoint(6, 5)],
                    null,
                    [
                        new("moved", Position: new FixturePoint(6, 5)),
                        new(
                            "power_collected",
                            Position: new FixturePoint(6, 5),
                            Power: "shield"),
                        new(
                            "power_activated",
                            Power: "shield",
                            Value: ShieldDurationTicks),
                    ],
                    new FixturePoint(6, 5),
                    ShieldDurationTicks,
                    1)),
            new(
                "shield-pickup-expiry",
                new InitialState(
                    [new FixturePoint(5, 5)],
                    "RIGHT",
                    new FixturePoint(20, 20),
                    new FixturePickup(new FixturePoint(6, 5), 1),
                    0,
                    0),
                new ExpectedState(
                    true,
                    [new FixturePoint(6, 5)],
                    null,
                    [
                        new(
                            "power_expired",
                            Position: new FixturePoint(6, 5),
                            Power: "shield"),
                        new("moved", Position: new FixturePoint(6, 5)),
                    ],
                    new FixturePoint(6, 5),
                    0,
                    1)),
            new(
                "shield-active-countdown",
                new InitialState(
                    [new FixturePoint(5, 5)],
                    "RIGHT",
                    new FixturePoint(20, 20),
                    null,
                    2,
                    0),
                new ExpectedState(
                    true,
                    [new FixturePoint(6, 5)],
                    null,
                    [new("moved", Position: new FixturePoint(6, 5))],
                    new FixturePoint(6, 5),
                    1,
                    1)),
            new(
                "shield-active-expiry",
                new InitialState(
                    [new FixturePoint(5, 5)],
                    "RIGHT",
                    new FixturePoint(20, 20),
                    null,
                    1,
                    0),
                new ExpectedState(
                    true,
                    [new FixturePoint(6, 5)],
                    null,
                    [
                        new("power_expired", Power: "shield"),
                        new("moved", Position: new FixturePoint(6, 5)),
                    ],
                    new FixturePoint(6, 5),
                    0,
                    1)),
            new(
                "shield-collision-consumption",
                new InitialState(
                    collisionBody,
                    "DOWN",
                    new FixturePoint(20, 20),
                    null,
                    2,
                    0),
                new ExpectedState(
                    true,
                    collisionBody,
                    null,
                    [
                        new("power_consumed", Power: "shield"),
                        new(
                            "collision_prevented",
                            Position: new FixturePoint(2, 2),
                            Power: "shield",
                            DeathCause: "self_collision"),
                    ],
                    new FixturePoint(2, 1),
                    0,
                    1)),
            new(
                "shield-collision-at-starvation-deadline",
                new InitialState(
                    collisionBody,
                    "DOWN",
                    new FixturePoint(20, 20),
                    null,
                    2,
                    StarvationTicks - 1),
                new ExpectedState(
                    false,
                    collisionBody,
                    "starvation",
                    [
                        new("power_consumed", Power: "shield"),
                        new(
                            "collision_prevented",
                            Position: new FixturePoint(2, 2),
                            Power: "shield",
                            DeathCause: "self_collision"),
                        new(
                            "died",
                            Position: new FixturePoint(2, 1),
                            DeathCause: "starvation"),
                    ],
                    new FixturePoint(2, 1),
                    0,
                    StarvationTicks)),
            new(
                "shield-expiry-before-collision",
                new InitialState(
                    collisionBody,
                    "DOWN",
                    new FixturePoint(20, 20),
                    null,
                    1,
                    0),
                new ExpectedState(
                    false,
                    collisionBody,
                    "self_collision",
                    [
                        new("power_expired", Power: "shield"),
                        new(
                            "died",
                            Position: new FixturePoint(2, 2),
                            DeathCause: "self_collision"),
                    ],
                    new FixturePoint(2, 1),
                    0,
                    1)),
            new(
                "shield-does-not-block-starvation",
                new InitialState(
                    [new FixturePoint(5, 5)],
                    "RIGHT",
                    new FixturePoint(20, 20),
                    null,
                    2,
                    StarvationTicks - 1),
                new ExpectedState(
                    false,
                    [new FixturePoint(6, 5)],
                    "starvation",
                    [
                        new("moved", Position: new FixturePoint(6, 5)),
                        new(
                            "died",
                            Position: new FixturePoint(6, 5),
                            DeathCause: "starvation"),
                    ],
                    new FixturePoint(6, 5),
                    1,
                    StarvationTicks)),
        ];
    }

    private static void WriteCase(Utf8JsonWriter writer, TraceCase traceCase)
    {
        writer.WriteStartObject();
        writer.WritePropertyName("expected");
        writer.WriteStartObject();
        writer.WriteBoolean("alive", traceCase.Expected.Alive);
        writer.WritePropertyName("body");
        WritePoints(writer, traceCase.Expected.Body);
        if (traceCase.Expected.DeathCause is { } deathCause)
        {
            writer.WriteString("death_cause", deathCause);
        }
        else
        {
            writer.WriteNull("death_cause");
        }

        writer.WritePropertyName("events");
        writer.WriteStartArray();
        foreach (var detail in traceCase.Expected.Events)
        {
            WriteEvent(writer, detail);
        }

        writer.WriteEndArray();
        writer.WritePropertyName("head");
        WritePoint(writer, traceCase.Expected.Head);
        writer.WriteNull("pickup");
        writer.WriteNumber(
            "shield_ticks_remaining",
            traceCase.Expected.ShieldTicksRemaining);
        writer.WriteNumber(
            "starvation_ticks_elapsed",
            traceCase.Expected.StarvationTicksElapsed);
        writer.WriteNumber("tick", 1);
        writer.WriteEndObject();
        writer.WriteString("id", traceCase.Id);
        writer.WritePropertyName("initial");
        writer.WriteStartObject();
        writer.WritePropertyName("body");
        WritePoints(writer, traceCase.Initial.Body);
        writer.WriteString("direction", traceCase.Initial.Direction);
        writer.WritePropertyName("food");
        WritePoint(writer, traceCase.Initial.Food);
        writer.WritePropertyName("pickup");
        if (traceCase.Initial.Pickup is { } pickup)
        {
            WritePickup(writer, pickup);
        }
        else
        {
            writer.WriteNullValue();
        }

        writer.WriteNumber(
            "shield_ticks_remaining",
            traceCase.Initial.ShieldTicksRemaining);
        writer.WriteNumber(
            "starvation_ticks_elapsed",
            traceCase.Initial.StarvationTicksElapsed);
        writer.WriteEndObject();
        writer.WriteEndObject();
    }

    private static void WriteEvent(Utf8JsonWriter writer, ExpectedEvent detail)
    {
        writer.WriteStartObject();
        if (detail.DeathCause is { } deathCause)
        {
            writer.WriteString("death_cause", deathCause);
        }

        writer.WriteString("kind", detail.Kind);
        if (detail.Position is { } position)
        {
            writer.WritePropertyName("position");
            WritePoint(writer, position);
        }

        if (detail.Power is { } power)
        {
            writer.WriteString("power", power);
        }

        if (detail.Value is { } value)
        {
            writer.WriteNumber("value", value);
        }

        writer.WriteEndObject();
    }

    private static void WritePickup(Utf8JsonWriter writer, FixturePickup pickup)
    {
        writer.WriteStartObject();
        writer.WriteString("kind", "shield");
        writer.WritePropertyName("position");
        WritePoint(writer, pickup.Position);
        writer.WriteNumber(
            "visibility_ticks_remaining",
            pickup.VisibilityTicksRemaining);
        writer.WriteEndObject();
    }

    private static void WritePoints(
        Utf8JsonWriter writer,
        IEnumerable<FixturePoint> points)
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

    private static void WriteStringArray(
        Utf8JsonWriter writer,
        IEnumerable<string> values)
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
            "Shield fixture",
            true,
            $"Shared Shield fixture {operation}: cases={CaseCount} bytes={bytes}.",
            []);

    private static RepositoryCheckResult Failed(string failure) =>
        new("Shield fixture", false, string.Empty, [failure]);

    private sealed record TraceCase(
        string Id,
        InitialState Initial,
        ExpectedState Expected);

    private sealed record InitialState(
        IReadOnlyList<FixturePoint> Body,
        string Direction,
        FixturePoint Food,
        FixturePickup? Pickup,
        int ShieldTicksRemaining,
        int StarvationTicksElapsed);

    private sealed record ExpectedState(
        bool Alive,
        IReadOnlyList<FixturePoint> Body,
        string? DeathCause,
        IReadOnlyList<ExpectedEvent> Events,
        FixturePoint Head,
        int ShieldTicksRemaining,
        int StarvationTicksElapsed);

    private sealed record FixturePickup(
        FixturePoint Position,
        int VisibilityTicksRemaining);

    private sealed record ExpectedEvent(
        string Kind,
        FixturePoint? Position = null,
        string? Power = null,
        int? Value = null,
        string? DeathCause = null);

    private readonly record struct FixturePoint(int X, int Y);
}
