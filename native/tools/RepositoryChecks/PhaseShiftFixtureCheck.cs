using System.Text.Json;

namespace RepositoryChecks;

public static class PhaseShiftFixtureCheck
{
    public const int SchemaVersion = 1;
    public const int CaseCount = 6;
    public const string Contract = "phase-shift-rules-targeted-v1";
    public const string FixtureRelativePath =
        "tests/fixtures/shared/phase_shift_rules_v1.json";

    private const int PhaseShiftDurationTicks = 100;
    private const int PowerVisibleTicks = 120;
    private const int StarvationTicks = 600;
    private const string RandomnessPolicy = "positions-and-power-state-injected-v1";
    private const string SourceEngine = "python-production-phase-shift-v1";

    public static RepositoryCheckResult Inspect(string repositoryRoot)
    {
        try
        {
            var expected = BuildFixtureBytes();
            var actual = FixedCanonicalFixtureFile.Read(
                repositoryRoot,
                FixtureRelativePath,
                "Phase Shift fixture");
            if (!actual.AsSpan().SequenceEqual(expected))
            {
                return Failed(
                    "Phase Shift fixture is stale or noncanonical; run "
                        + "dotnet run --project native/tools/RepositoryChecks/RepositoryChecks.csproj "
                        + "-- phase-shift-write .");
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
                "Phase Shift fixture",
                bytes);

            var verification = Inspect(repositoryRoot);
            if (!verification.Passed)
            {
                return new RepositoryCheckResult(
                    "Phase Shift fixture",
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
        return CanonicalFixtureJson.Render("Phase Shift fixture", writer =>
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
                    "self_collision_phasing",
                    "body_overlap",
                    "starvation_bypass",
                    "ordered_power_events",
                ]);
            writer.WritePropertyName("config");
            writer.WriteStartObject();
            writer.WriteNumber("height", 33);
            writer.WriteNumber("phase_shift_duration_ticks", PhaseShiftDurationTicks);
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
                    "detached_obstacles",
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
            "phase-shift-collect-on-entry",
            new InitialState(
                [new FixturePoint(5, 5)],
                "RIGHT",
                new FixturePoint(20, 20),
                0,
                new FixturePickup(new FixturePoint(6, 5), 10),
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
                        Power: "phase_shift"),
                    new(
                        "power_activated",
                        Power: "phase_shift",
                        Value: PhaseShiftDurationTicks),
                ],
                new FixturePoint(6, 5),
                PhaseShiftDurationTicks,
                1)),
        new(
            "phase-shift-pickup-expiry",
            new InitialState(
                [new FixturePoint(5, 5)],
                "RIGHT",
                new FixturePoint(20, 20),
                0,
                new FixturePickup(new FixturePoint(6, 5), 1),
                0),
            new ExpectedState(
                true,
                [new FixturePoint(6, 5)],
                null,
                [
                    new(
                        "power_expired",
                        Position: new FixturePoint(6, 5),
                        Power: "phase_shift"),
                    new("moved", Position: new FixturePoint(6, 5)),
                ],
                new FixturePoint(6, 5),
                0,
                1)),
        new(
            "phase-shift-active-countdown",
            new InitialState(
                [new FixturePoint(5, 5)],
                "RIGHT",
                new FixturePoint(20, 20),
                2,
                null,
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
            "phase-shift-active-expiry-before-collision",
            new InitialState(
                [
                    new FixturePoint(1, 1),
                    new FixturePoint(1, 2),
                    new FixturePoint(2, 2),
                    new FixturePoint(2, 1),
                ],
                "DOWN",
                new FixturePoint(20, 20),
                1,
                null,
                0),
            new ExpectedState(
                false,
                [
                    new FixturePoint(1, 1),
                    new FixturePoint(1, 2),
                    new FixturePoint(2, 2),
                    new FixturePoint(2, 1),
                ],
                "self_collision",
                [
                    new("power_expired", Power: "phase_shift"),
                    new(
                        "died",
                        Position: new FixturePoint(2, 2),
                        DeathCause: "self_collision"),
                ],
                new FixturePoint(2, 1),
                0,
                1)),
        new(
            "phase-shift-body-overlap",
            new InitialState(
                [
                    new FixturePoint(1, 1),
                    new FixturePoint(1, 2),
                    new FixturePoint(2, 2),
                    new FixturePoint(2, 1),
                ],
                "DOWN",
                new FixturePoint(20, 20),
                2,
                null,
                0),
            new ExpectedState(
                true,
                [
                    new FixturePoint(1, 2),
                    new FixturePoint(2, 2),
                    new FixturePoint(2, 1),
                    new FixturePoint(2, 2),
                ],
                null,
                [new("moved", Position: new FixturePoint(2, 2))],
                new FixturePoint(2, 2),
                1,
                1)),
        new(
            "phase-shift-does-not-block-starvation",
            new InitialState(
                [new FixturePoint(5, 5)],
                "RIGHT",
                new FixturePoint(20, 20),
                2,
                null,
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
        writer.WriteNumber(
            "phase_shift_ticks_remaining",
            traceCase.Expected.PhaseShiftTicksRemaining);
        writer.WriteNull("pickup");
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
        writer.WriteNumber(
            "phase_shift_ticks_remaining",
            traceCase.Initial.PhaseShiftTicksRemaining);
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
        writer.WriteString("kind", "phase_shift");
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
            "Phase Shift fixture",
            true,
            $"Shared Phase Shift fixture {operation}: cases={CaseCount} bytes={bytes}.",
            []);

    private static RepositoryCheckResult Failed(string failure) =>
        new("Phase Shift fixture", false, string.Empty, [failure]);

    private sealed record TraceCase(
        string Id,
        InitialState Initial,
        ExpectedState Expected);

    private sealed record InitialState(
        IReadOnlyList<FixturePoint> Body,
        string Direction,
        FixturePoint Food,
        int PhaseShiftTicksRemaining,
        FixturePickup? Pickup,
        int StarvationTicksElapsed);

    private sealed record ExpectedState(
        bool Alive,
        IReadOnlyList<FixturePoint> Body,
        string? DeathCause,
        IReadOnlyList<ExpectedEvent> Events,
        FixturePoint Head,
        int PhaseShiftTicksRemaining,
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
