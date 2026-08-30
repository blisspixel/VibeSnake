using System.Text.Json;

namespace RepositoryChecks;

public static class AchievementCandidateFixtureCheck
{
    public const int SchemaVersion = 1;
    public const int CaseCount = 4;
    public const string Contract = "achievement-candidates-targeted-v1";
    public const string FixtureRelativePath =
        "tests/fixtures/shared/achievement_candidates_rules_v1.json";

    private const int StarvationTicks = 600;
    private const string RandomnessPolicy =
        "positions-injected-or-random-output-normalized-v2";
    private const string SourceEngine = "python-core-reference-v3";

    public static RepositoryCheckResult Inspect(string repositoryRoot)
    {
        try
        {
            var expected = BuildFixtureBytes();
            var actual = FixedCanonicalFixtureFile.Read(
                repositoryRoot,
                FixtureRelativePath,
                "achievement-candidate fixture");
            if (!actual.AsSpan().SequenceEqual(expected))
            {
                return Failed(
                    "achievement-candidate fixture is stale or noncanonical; run "
                        + "dotnet run --project native/tools/RepositoryChecks/RepositoryChecks.csproj "
                        + "-- achievement-candidates-write .");
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
                "achievement-candidate fixture",
                bytes);

            var verification = Inspect(repositoryRoot);
            if (!verification.Passed)
            {
                return new RepositoryCheckResult(
                    "Achievement-candidate fixture",
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
        return CanonicalFixtureJson.Render("achievement-candidate fixture", writer =>
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
                    "terminal_achievement_candidates",
                    "already_unlocked_suppression",
                    "ordered_events",
                ]);
            writer.WritePropertyName("config");
            WriteConfig(writer);
            writer.WriteString("contract", Contract);
            writer.WritePropertyName("excluded_scope");
            WriteStringArray(
                writer,
                [
                    "default_flag_off_corpus",
                    "profile_lifetime_achievements",
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
            new CaseSpecification(
                "starvation-score-candidates",
                [new FixturePoint(5, 5)],
                "RIGHT",
                new FixturePoint(10, 10),
                150,
                0,
                0,
                StarvationTicks - 1,
                []),
            new ExpectedTrace(
                [new FixturePoint(6, 5)],
                "starvation",
                "RIGHT",
                [
                    new("moved", new FixturePoint(6, 5)),
                    new("died", new FixturePoint(6, 5), "starvation"),
                    new("achievement_candidate", Value: 0),
                    new("achievement_candidate", Value: 1),
                ],
                new FixturePoint(6, 5),
                150)),
        new(
            new CaseSpecification(
                "starvation-suppresses-already-unlocked",
                [new FixturePoint(5, 5)],
                "RIGHT",
                new FixturePoint(10, 10),
                150,
                0,
                0,
                StarvationTicks - 1,
                ["first_bite", "century"]),
            new ExpectedTrace(
                [new FixturePoint(6, 5)],
                "starvation",
                "RIGHT",
                [
                    new("moved", new FixturePoint(6, 5)),
                    new("died", new FixturePoint(6, 5), "starvation"),
                ],
                new FixturePoint(6, 5),
                150)),
        new(
            new CaseSpecification(
                "starvation-zero-score-no-candidates",
                [new FixturePoint(5, 5)],
                "RIGHT",
                new FixturePoint(10, 10),
                0,
                0,
                0,
                StarvationTicks - 1,
                []),
            new ExpectedTrace(
                [new FixturePoint(6, 5)],
                "starvation",
                "RIGHT",
                [
                    new("moved", new FixturePoint(6, 5)),
                    new("died", new FixturePoint(6, 5), "starvation"),
                ],
                new FixturePoint(6, 5),
                0)),
        new(
            new CaseSpecification(
                "self-collision-score-candidates",
                [
                    new FixturePoint(1, 1),
                    new FixturePoint(1, 2),
                    new FixturePoint(2, 2),
                    new FixturePoint(2, 1),
                ],
                "DOWN",
                new FixturePoint(10, 10),
                120,
                0,
                0,
                0,
                []),
            new ExpectedTrace(
                [
                    new FixturePoint(1, 1),
                    new FixturePoint(1, 2),
                    new FixturePoint(2, 2),
                    new FixturePoint(2, 1),
                ],
                "self_collision",
                "DOWN",
                [
                    new("died", new FixturePoint(2, 2), "self_collision"),
                    new("achievement_candidate", Value: 0),
                    new("achievement_candidate", Value: 1),
                ],
                new FixturePoint(2, 1),
                120)),
    ];

    private static void WriteCase(Utf8JsonWriter writer, TraceCase traceCase)
    {
        var specification = traceCase.Specification;
        var expected = traceCase.Expected;
        writer.WriteStartObject();
        writer.WritePropertyName("commands");
        writer.WriteStartArray();
        writer.WriteEndArray();
        writer.WritePropertyName("expected");
        writer.WriteStartObject();
        writer.WriteBoolean("alive", false);
        writer.WritePropertyName("body");
        WritePoints(writer, expected.Body);
        writer.WriteString("death_cause", expected.DeathCause);
        writer.WriteString("direction", expected.Direction);
        writer.WritePropertyName("events");
        writer.WriteStartArray();
        foreach (var detail in expected.Events)
        {
            WriteEvent(writer, detail);
        }

        writer.WriteEndArray();
        writer.WritePropertyName("head");
        WritePoint(writer, expected.Head);
        writer.WriteNumber("score", expected.Score);
        writer.WriteNumber("tick", 1);
        writer.WriteBoolean("won", false);
        writer.WriteEndObject();
        writer.WriteString("id", specification.Id);
        writer.WritePropertyName("initial");
        writer.WriteStartObject();
        writer.WritePropertyName("already_unlocked");
        WriteStringArray(writer, specification.AlreadyUnlocked);
        writer.WritePropertyName("body");
        WritePoints(writer, specification.Body);
        writer.WriteNumber("combo", specification.Combo);
        writer.WriteString("direction", specification.Direction);
        writer.WritePropertyName("food");
        WritePoint(writer, specification.Food);

        writer.WriteNumber("score", specification.Score);
        writer.WriteNumber(
            "starvation_ticks_elapsed",
            specification.StarvationTicksElapsed);
        writer.WriteNumber("ticks_since_last_food", specification.TicksSinceLastFood);
        writer.WriteEndObject();
        writer.WriteEndObject();
    }

    private static void WriteConfig(Utf8JsonWriter writer)
    {
        writer.WriteStartObject();
        writer.WriteNumber("combo_window_ticks", 60);
        writer.WriteBoolean("enable_achievement_candidates", true);
        writer.WriteNumber("food_score", 10);
        writer.WriteNumber("height", 33);
        writer.WriteNumber("maximum_direction_queue", 3);
        writer.WriteNumber("maximum_score", 2_000_000_000);
        writer.WriteNumber("speed_bonus_ticks", 30);
        writer.WriteNumber("starvation_ticks", StarvationTicks);
        writer.WriteNumber("width", 64);
        writer.WriteEndObject();
    }

    private static void WriteEvent(Utf8JsonWriter writer, ExpectedEvent detail)
    {
        writer.WriteStartObject();
        if (detail.DeathCause is { } cause)
        {
            writer.WriteString("death_cause", cause);
        }

        writer.WriteString("kind", detail.Kind);
        if (detail.Position is { } position)
        {
            writer.WritePropertyName("position");
            WritePoint(writer, position);
        }

        if (detail.Value is { } value)
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
            "Achievement-candidate fixture",
            true,
            $"Shared achievement-candidate fixture {operation}: cases={CaseCount} bytes={bytes}.",
            []);

    private static RepositoryCheckResult Failed(string failure) =>
        new("Achievement-candidate fixture", false, string.Empty, [failure]);

    private sealed record CaseSpecification(
        string Id,
        IReadOnlyList<FixturePoint> Body,
        string Direction,
        FixturePoint Food,
        int Score,
        int Combo,
        int TicksSinceLastFood,
        int StarvationTicksElapsed,
        IReadOnlyList<string> AlreadyUnlocked);

    private sealed record TraceCase(
        CaseSpecification Specification,
        ExpectedTrace Expected);

    private sealed record ExpectedTrace(
        IReadOnlyList<FixturePoint> Body,
        string DeathCause,
        string Direction,
        IReadOnlyList<ExpectedEvent> Events,
        FixturePoint Head,
        int Score);

    private sealed record ExpectedEvent(
        string Kind,
        FixturePoint? Position = null,
        string? DeathCause = null,
        int? Value = null);

    private readonly record struct FixturePoint(int X, int Y);
}
