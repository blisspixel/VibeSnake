using System.Text.Json;

namespace RepositoryChecks;

public static class RemainingPowersFixtureCheck
{
    public const int SchemaVersion = 1;
    public const int CaseCount = 9;
    public const string Contract = "remaining-powers-rules-targeted-v1";
    public const string FixtureRelativePath =
        "tests/fixtures/shared/remaining_powers_rules_v1.json";

    private const int BoostDurationTicks = 80;
    private const int GluttonyDurationTicks = 100;
    private const int MagnetDurationTicks = 120;
    private const int PowerVisibleTicks = 120;
    private const int SegmentDetachMaxSegments = 5;
    private const int SegmentDetachObstacleTicks = 200;
    private const int SlowMoDurationTicks = 120;
    private const int StarvationTicks = 600;
    private const string RandomnessPolicy = "positions-and-power-state-injected-v1";
    private const string SourceEngine = "python-production-remaining-powers-v1";

    public static RepositoryCheckResult Inspect(string repositoryRoot)
    {
        try
        {
            var expected = BuildFixtureBytes();
            var actual = FixedCanonicalFixtureFile.Read(
                repositoryRoot,
                FixtureRelativePath,
                "Remaining Powers fixture");
            if (!actual.AsSpan().SequenceEqual(expected))
            {
                return Failed(
                    "Remaining Powers fixture is stale or noncanonical; run "
                        + "dotnet run --project native/tools/RepositoryChecks/RepositoryChecks.csproj "
                        + "-- remaining-powers-write .");
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
                "Remaining Powers fixture",
                bytes);

            var verification = Inspect(repositoryRoot);
            if (!verification.Passed)
            {
                return new RepositoryCheckResult(
                    "Remaining Powers fixture",
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
        return CanonicalFixtureJson.Render("Remaining Powers fixture", writer =>
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
                    "magnet_pull",
                    "gluttony_no_growth",
                    "bait_mark",
                    "segment_detach_obstacles",
                    "tempo_compose",
                    "ordered_power_events",
                ]);
            writer.WritePropertyName("config");
            writer.WriteStartObject();
            writer.WriteNumber("boost_duration_ticks", BoostDurationTicks);
            writer.WriteNumber("gluttony_duration_ticks", GluttonyDurationTicks);
            writer.WriteNumber("height", 33);
            writer.WriteNumber("magnet_duration_ticks", MagnetDurationTicks);
            writer.WriteNumber("power_visible_ticks", PowerVisibleTicks);
            writer.WriteNumber("segment_detach_max_segments", SegmentDetachMaxSegments);
            writer.WriteNumber("segment_detach_obstacle_ticks", SegmentDetachObstacleTicks);
            writer.WriteNumber("slow_mo_duration_ticks", SlowMoDurationTicks);
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
                    "food_respawn_position_after_eat",
                    "shield_phase_last_stand",
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
        FixturePoint[] detachedInitialBody =
        [
            new(0, 1),
            new(1, 1),
            new(2, 1),
            new(3, 1),
            new(4, 1),
            new(5, 1),
        ];
        FixturePoint[] detachedObstacles =
        [
            new(1, 1),
            new(2, 1),
            new(3, 1),
            new(4, 1),
            new(5, 1),
        ];

        return
        [
            CollectionCase(
                "slow-mo-collect-on-entry",
                "slow_mo",
                SlowMoDurationTicks,
                slowMoTicksRemaining: SlowMoDurationTicks,
                movementCadenceNumerator: 2),
            CollectionCase(
                "boost-collect-on-entry",
                "boost",
                BoostDurationTicks,
                boostTicksRemaining: BoostDurationTicks,
                movementCadenceDenominator: 2),
            CollectionCase(
                "magnet-collect-on-entry",
                "magnet",
                MagnetDurationTicks,
                magnetTicksRemaining: MagnetDurationTicks),
            new(
                "magnet-pull-food-toward-head",
                new InitialState(
                    [new FixturePoint(2, 2)],
                    "RIGHT",
                    new FixturePoint(6, 5),
                    MagnetTicksRemaining: 3),
                new ExpectedState(
                    [new FixturePoint(3, 2)],
                    [new("moved", Position: new FixturePoint(3, 2))],
                    new FixturePoint(5, 4),
                    new FixturePoint(3, 2),
                    MagnetTicksRemaining: 2),
                false),
            CollectionCase(
                "gluttony-collect-on-entry",
                "gluttony",
                GluttonyDurationTicks,
                gluttonyTicksRemaining: GluttonyDurationTicks),
            new(
                "gluttony-eat-without-growth",
                new InitialState(
                    [new FixturePoint(1, 1), new FixturePoint(2, 1)],
                    "RIGHT",
                    new FixturePoint(3, 1),
                    GluttonyTicksRemaining: 3),
                new ExpectedState(
                    [new FixturePoint(2, 1), new FixturePoint(3, 1)],
                    [
                        new("moved", Position: new FixturePoint(3, 1)),
                        new("ate_food", Position: new FixturePoint(3, 1)),
                        new("score_changed", Value: 18),
                        new("hunger_reset", Value: StarvationTicks),
                    ],
                    null,
                    new FixturePoint(3, 1),
                    GluttonyTicksRemaining: 2,
                    SkipFood: true,
                    StarvationTicksElapsed: 0),
                true),
            CollectionCase(
                "bait-collect-on-entry",
                "bait",
                0,
                baitPosition: new FixturePoint(6, 5)),
            new(
                "segment-detach-on-entry",
                new InitialState(
                    detachedInitialBody,
                    "RIGHT",
                    new FixturePoint(20, 20),
                    new FixturePickup(
                        "segment_detach",
                        new FixturePoint(6, 1),
                        10)),
                new ExpectedState(
                    [new FixturePoint(6, 1)],
                    [
                        new("moved", Position: new FixturePoint(6, 1)),
                        new(
                            "power_collected",
                            Position: new FixturePoint(6, 1),
                            Power: "segment_detach"),
                        new("power_activated", Power: "segment_detach", Value: 5),
                    ],
                    new FixturePoint(20, 20),
                    new FixturePoint(6, 1),
                    DetachedObstacleTicksRemaining: SegmentDetachObstacleTicks,
                    DetachedObstacles: detachedObstacles),
                false),
            new(
                "tempo-compose-active-countdown",
                new InitialState(
                    [new FixturePoint(5, 5)],
                    "RIGHT",
                    new FixturePoint(20, 20),
                    BoostTicksRemaining: 2,
                    SlowMoTicksRemaining: 3),
                new ExpectedState(
                    [new FixturePoint(6, 5)],
                    [new("moved", Position: new FixturePoint(6, 5))],
                    new FixturePoint(20, 20),
                    new FixturePoint(6, 5),
                    BoostTicksRemaining: 1,
                    MovementCadenceDenominator: 2,
                    MovementCadenceNumerator: 2,
                    SlowMoTicksRemaining: 2),
                false),
        ];
    }

    private static TraceCase CollectionCase(
        string id,
        string power,
        int activationValue,
        int boostTicksRemaining = 0,
        int gluttonyTicksRemaining = 0,
        int magnetTicksRemaining = 0,
        int movementCadenceDenominator = 1,
        int movementCadenceNumerator = 1,
        int slowMoTicksRemaining = 0,
        FixturePoint? baitPosition = null)
    {
        var pickupPosition = new FixturePoint(6, 5);
        return new TraceCase(
            id,
            new InitialState(
                [new FixturePoint(5, 5)],
                "RIGHT",
                new FixturePoint(20, 20),
                new FixturePickup(power, pickupPosition, 10)),
            new ExpectedState(
                [pickupPosition],
                [
                    new("moved", Position: pickupPosition),
                    new("power_collected", Position: pickupPosition, Power: power),
                    new(
                        "power_activated",
                        Position: baitPosition,
                        Power: power,
                        Value: activationValue),
                ],
                new FixturePoint(20, 20),
                pickupPosition,
                BaitPosition: baitPosition,
                BoostTicksRemaining: boostTicksRemaining,
                GluttonyTicksRemaining: gluttonyTicksRemaining,
                MagnetTicksRemaining: magnetTicksRemaining,
                MovementCadenceDenominator: movementCadenceDenominator,
                MovementCadenceNumerator: movementCadenceNumerator,
                SlowMoTicksRemaining: slowMoTicksRemaining),
            false);
    }

    private static void WriteCase(Utf8JsonWriter writer, TraceCase traceCase)
    {
        writer.WriteStartObject();
        writer.WritePropertyName("expected");
        WriteExpected(writer, traceCase.Expected);
        writer.WriteString("id", traceCase.Id);
        writer.WritePropertyName("initial");
        WriteInitial(writer, traceCase.Initial);
        writer.WriteBoolean("skip_food_after_eat", traceCase.SkipFoodAfterEat);
        writer.WriteEndObject();
    }

    private static void WriteExpected(Utf8JsonWriter writer, ExpectedState expected)
    {
        writer.WriteStartObject();
        writer.WriteBoolean("alive", true);
        writer.WritePropertyName("bait_position");
        WriteNullablePoint(writer, expected.BaitPosition);
        writer.WritePropertyName("body");
        WritePoints(writer, expected.Body);
        writer.WriteNumber("boost_ticks_remaining", expected.BoostTicksRemaining);
        writer.WriteNull("death_cause");
        writer.WriteNumber(
            "detached_obstacle_ticks_remaining",
            expected.DetachedObstacleTicksRemaining);
        writer.WritePropertyName("detached_obstacles");
        WritePoints(writer, expected.DetachedObstacles);
        writer.WritePropertyName("events");
        writer.WriteStartArray();
        foreach (var detail in expected.Events)
        {
            WriteEvent(writer, detail);
        }

        writer.WriteEndArray();
        writer.WritePropertyName("food");
        WriteNullablePoint(writer, expected.Food);
        writer.WriteNumber(
            "gluttony_ticks_remaining",
            expected.GluttonyTicksRemaining);
        writer.WritePropertyName("head");
        WritePoint(writer, expected.Head);
        writer.WriteNumber("magnet_ticks_remaining", expected.MagnetTicksRemaining);
        writer.WriteNumber(
            "movement_cadence_denominator",
            expected.MovementCadenceDenominator);
        writer.WriteNumber(
            "movement_cadence_numerator",
            expected.MovementCadenceNumerator);
        writer.WriteNull("pickup");
        writer.WriteBoolean("skip_food", expected.SkipFood);
        writer.WriteNumber("slow_mo_ticks_remaining", expected.SlowMoTicksRemaining);
        writer.WriteNumber(
            "starvation_ticks_elapsed",
            expected.StarvationTicksElapsed);
        writer.WriteNumber("tick", 1);
        writer.WriteEndObject();
    }

    private static void WriteInitial(Utf8JsonWriter writer, InitialState initial)
    {
        writer.WriteStartObject();
        writer.WritePropertyName("bait_position");
        WriteNullablePoint(writer, initial.BaitPosition);
        writer.WritePropertyName("body");
        WritePoints(writer, initial.Body);
        writer.WriteNumber("boost_ticks_remaining", initial.BoostTicksRemaining);
        writer.WriteNumber(
            "detached_obstacle_ticks_remaining",
            initial.DetachedObstacleTicksRemaining);
        writer.WritePropertyName("detached_obstacles");
        WritePoints(writer, initial.DetachedObstacles);
        writer.WriteString("direction", initial.Direction);
        writer.WritePropertyName("food");
        WriteNullablePoint(writer, initial.Food);
        writer.WriteNumber(
            "gluttony_ticks_remaining",
            initial.GluttonyTicksRemaining);
        writer.WriteNumber("magnet_ticks_remaining", initial.MagnetTicksRemaining);
        writer.WritePropertyName("pickup");
        if (initial.Pickup is { } pickup)
        {
            WritePickup(writer, pickup);
        }
        else
        {
            writer.WriteNullValue();
        }

        writer.WriteNumber("slow_mo_ticks_remaining", initial.SlowMoTicksRemaining);
        writer.WriteNumber(
            "starvation_ticks_elapsed",
            initial.StarvationTicksElapsed);
        writer.WriteEndObject();
    }

    private static void WriteEvent(Utf8JsonWriter writer, ExpectedEvent detail)
    {
        writer.WriteStartObject();
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
        writer.WriteString("kind", pickup.Kind);
        writer.WritePropertyName("position");
        WritePoint(writer, pickup.Position);
        writer.WriteNumber(
            "visibility_ticks_remaining",
            pickup.VisibilityTicksRemaining);
        writer.WriteEndObject();
    }

    private static void WriteNullablePoint(
        Utf8JsonWriter writer,
        FixturePoint? point)
    {
        if (point is { } value)
        {
            WritePoint(writer, value);
        }
        else
        {
            writer.WriteNullValue();
        }
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
            "Remaining Powers fixture",
            true,
            $"Shared Remaining Powers fixture {operation}: cases={CaseCount} bytes={bytes}.",
            []);

    private static RepositoryCheckResult Failed(string failure) =>
        new("Remaining Powers fixture", false, string.Empty, [failure]);

    private sealed record TraceCase(
        string Id,
        InitialState Initial,
        ExpectedState Expected,
        bool SkipFoodAfterEat);

    private sealed record InitialState(
        IReadOnlyList<FixturePoint> Body,
        string Direction,
        FixturePoint? Food,
        FixturePickup? Pickup = null,
        FixturePoint? BaitPosition = null,
        int BoostTicksRemaining = 0,
        int DetachedObstacleTicksRemaining = 0,
        IReadOnlyList<FixturePoint>? DetachedObstacles = null,
        int GluttonyTicksRemaining = 0,
        int MagnetTicksRemaining = 0,
        int SlowMoTicksRemaining = 0,
        int StarvationTicksElapsed = 0)
    {
        public IReadOnlyList<FixturePoint> DetachedObstacles { get; } =
            DetachedObstacles ?? [];
    }

    private sealed record ExpectedState(
        IReadOnlyList<FixturePoint> Body,
        IReadOnlyList<ExpectedEvent> Events,
        FixturePoint? Food,
        FixturePoint Head,
        FixturePoint? BaitPosition = null,
        int BoostTicksRemaining = 0,
        int DetachedObstacleTicksRemaining = 0,
        IReadOnlyList<FixturePoint>? DetachedObstacles = null,
        int GluttonyTicksRemaining = 0,
        int MagnetTicksRemaining = 0,
        int MovementCadenceDenominator = 1,
        int MovementCadenceNumerator = 1,
        bool SkipFood = false,
        int SlowMoTicksRemaining = 0,
        int StarvationTicksElapsed = 1)
    {
        public IReadOnlyList<FixturePoint> DetachedObstacles { get; } =
            DetachedObstacles ?? [];
    }

    private sealed record FixturePickup(
        string Kind,
        FixturePoint Position,
        int VisibilityTicksRemaining);

    private sealed record ExpectedEvent(
        string Kind,
        FixturePoint? Position = null,
        string? Power = null,
        int? Value = null);

    private readonly record struct FixturePoint(int X, int Y);
}
