using System.Text.Json;

namespace RepositoryChecks;

public static class LastStandFixtureCheck
{
    public const int SchemaVersion = 1;
    public const int CaseCount = 5;
    public const string Contract = "last-stand-rules-targeted-v1";
    public const string FixtureRelativePath =
        "tests/fixtures/shared/last_stand_rules_v1.json";

    private const int LastStandRecoveryTicks = 60;
    private const int PowerVisibleTicks = 120;
    private const int StarvationTicks = 600;
    private const string RandomnessPolicy = "positions-and-power-state-injected-v1";
    private const string SourceEngine = "python-production-last-stand-v1";

    public static RepositoryCheckResult Inspect(string repositoryRoot)
    {
        try
        {
            var expected = BuildFixtureBytes();
            var actual = FixedCanonicalFixtureFile.Read(
                repositoryRoot,
                FixtureRelativePath,
                "Last Stand fixture");
            if (!actual.AsSpan().SequenceEqual(expected))
            {
                return Failed(
                    "Last Stand fixture is stale or noncanonical; run "
                        + "dotnet run --project native/tools/RepositoryChecks/RepositoryChecks.csproj "
                        + "-- last-stand-write .");
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
                "Last Stand fixture",
                bytes);

            var verification = Inspect(repositoryRoot);
            if (!verification.Passed)
            {
                return new RepositoryCheckResult(
                    "Last Stand fixture",
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
        return CanonicalFixtureJson.Render("Last Stand fixture", writer =>
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
                    "held_activation",
                    "collision_revive",
                    "body_shrink",
                    "starvation_revive",
                    "recovery_immunity",
                    "recovery_expiry",
                    "ordered_power_events",
                ]);
            writer.WritePropertyName("config");
            writer.WriteStartObject();
            writer.WriteNumber("height", 33);
            writer.WriteNumber("last_stand_recovery_ticks", LastStandRecoveryTicks);
            writer.WriteNumber("power_visible_ticks", PowerVisibleTicks);
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

    private static TraceCase[] Cases() =>
    [
        new(
            "last-stand-collect-on-entry",
            new InitialState(
                [new FixturePoint(5, 5)],
                "RIGHT",
                new FixturePoint(20, 20),
                false,
                new FixturePickup(new FixturePoint(6, 5), 10),
                0,
                0),
            new ExpectedState(
                [new FixturePoint(6, 5)],
                [
                    new("moved", Position: new FixturePoint(6, 5)),
                    new(
                        "power_collected",
                        Position: new FixturePoint(6, 5),
                        Power: "last_stand"),
                    new("power_activated", Power: "last_stand", Value: 0),
                ],
                new FixturePoint(6, 5),
                true,
                0,
                1)),
        new(
            "last-stand-collision-revive",
            new InitialState(
                [
                    new FixturePoint(1, 1),
                    new FixturePoint(1, 2),
                    new FixturePoint(2, 2),
                    new FixturePoint(2, 1),
                    new FixturePoint(3, 1),
                ],
                "LEFT",
                new FixturePoint(20, 20),
                true,
                null,
                0,
                0),
            new ExpectedState(
                [
                    new FixturePoint(2, 2),
                    new FixturePoint(2, 1),
                    new FixturePoint(3, 1),
                ],
                [
                    new("power_consumed", Power: "last_stand"),
                    new(
                        "collision_prevented",
                        Position: new FixturePoint(2, 1),
                        Power: "last_stand",
                        DeathCause: "self_collision"),
                    new("hunger_reset", Value: StarvationTicks),
                    new(
                        "power_activated",
                        Power: "last_stand",
                        Value: LastStandRecoveryTicks),
                ],
                new FixturePoint(3, 1),
                false,
                LastStandRecoveryTicks,
                0)),
        new(
            "last-stand-recovery-blocks-collision",
            new InitialState(
                [
                    new FixturePoint(1, 1),
                    new FixturePoint(1, 2),
                    new FixturePoint(2, 2),
                    new FixturePoint(2, 1),
                ],
                "DOWN",
                new FixturePoint(20, 20),
                false,
                null,
                2,
                0),
            new ExpectedState(
                [
                    new FixturePoint(1, 1),
                    new FixturePoint(1, 2),
                    new FixturePoint(2, 2),
                    new FixturePoint(2, 1),
                ],
                [
                    new(
                        "collision_prevented",
                        Position: new FixturePoint(2, 2),
                        Power: "last_stand",
                        DeathCause: "self_collision"),
                ],
                new FixturePoint(2, 1),
                false,
                1,
                1)),
        new(
            "last-stand-starvation-revive",
            new InitialState(
                [
                    new FixturePoint(5, 5),
                    new FixturePoint(6, 5),
                    new FixturePoint(7, 5),
                    new FixturePoint(8, 5),
                ],
                "RIGHT",
                new FixturePoint(20, 20),
                true,
                null,
                0,
                StarvationTicks - 1),
            new ExpectedState(
                [new FixturePoint(8, 5), new FixturePoint(9, 5)],
                [
                    new("moved", Position: new FixturePoint(9, 5)),
                    new("power_consumed", Power: "last_stand"),
                    new(
                        "collision_prevented",
                        Position: new FixturePoint(9, 5),
                        Power: "last_stand",
                        DeathCause: "starvation"),
                    new("hunger_reset", Value: StarvationTicks),
                    new(
                        "power_activated",
                        Power: "last_stand",
                        Value: LastStandRecoveryTicks),
                ],
                new FixturePoint(9, 5),
                false,
                LastStandRecoveryTicks,
                0)),
        new(
            "last-stand-recovery-expiry",
            new InitialState(
                [new FixturePoint(5, 5)],
                "RIGHT",
                new FixturePoint(20, 20),
                false,
                null,
                1,
                0),
            new ExpectedState(
                [new FixturePoint(6, 5)],
                [
                    new("power_expired", Power: "last_stand"),
                    new("moved", Position: new FixturePoint(6, 5)),
                ],
                new FixturePoint(6, 5),
                false,
                0,
                1)),
    ];

    private static void WriteCase(Utf8JsonWriter writer, TraceCase traceCase)
    {
        writer.WriteStartObject();
        writer.WritePropertyName("expected");
        writer.WriteStartObject();
        writer.WriteBoolean("alive", true);
        writer.WritePropertyName("body");
        WritePoints(writer, traceCase.Expected.Body);
        writer.WriteNull("death_cause");
        writer.WritePropertyName("events");
        writer.WriteStartArray();
        foreach (var detail in traceCase.Expected.Events)
        {
            WriteEvent(writer, detail);
        }

        writer.WriteEndArray();
        writer.WritePropertyName("head");
        WritePoint(writer, traceCase.Expected.Head);
        writer.WriteBoolean("last_stand_held", traceCase.Expected.LastStandHeld);
        writer.WriteNull("pickup");
        writer.WriteNumber(
            "recovery_ticks_remaining",
            traceCase.Expected.RecoveryTicksRemaining);
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
        writer.WriteBoolean("last_stand_held", traceCase.Initial.LastStandHeld);
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
            "recovery_ticks_remaining",
            traceCase.Initial.RecoveryTicksRemaining);
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
        writer.WriteString("kind", "last_stand");
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
            "Last Stand fixture",
            true,
            $"Shared Last Stand fixture {operation}: cases={CaseCount} bytes={bytes}.",
            []);

    private static RepositoryCheckResult Failed(string failure) =>
        new("Last Stand fixture", false, string.Empty, [failure]);

    private sealed record TraceCase(
        string Id,
        InitialState Initial,
        ExpectedState Expected);

    private sealed record InitialState(
        IReadOnlyList<FixturePoint> Body,
        string Direction,
        FixturePoint Food,
        bool LastStandHeld,
        FixturePickup? Pickup,
        int RecoveryTicksRemaining,
        int StarvationTicksElapsed);

    private sealed record ExpectedState(
        IReadOnlyList<FixturePoint> Body,
        IReadOnlyList<ExpectedEvent> Events,
        FixturePoint Head,
        bool LastStandHeld,
        int RecoveryTicksRemaining,
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
