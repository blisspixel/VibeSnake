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
    private const int MaximumFixtureBytes = 64 * 1024;
    private const int MaximumSiblingEntries = 256;
    private const string RandomnessPolicy =
        "positions-injected-or-random-output-normalized-v2";
    private const string SourceEngine = "python-core-reference-v3";

    private static readonly Type[] ExpectedFailureTypes =
    [
        typeof(ArgumentException),
        typeof(IOException),
        typeof(UnauthorizedAccessException),
        typeof(InvalidDataException),
        typeof(NotSupportedException),
    ];

    public static RepositoryCheckResult Inspect(string repositoryRoot)
    {
        try
        {
            var root = ResolveRepositoryRoot(repositoryRoot);
            var expected = BuildFixtureBytes();
            var fixturePath = ResolveExistingFixturePath(root);
            var actual = ReadBounded(fixturePath);
            if (!actual.AsSpan().SequenceEqual(expected))
            {
                return Failed(
                    "achievement-candidate fixture is stale or noncanonical; run "
                        + "dotnet run --project native/tools/RepositoryChecks/RepositoryChecks.csproj "
                        + "-- achievement-candidates-write .");
            }

            return Passed("verified", expected.Length);
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            return Failed(SingleLine(exception.Message));
        }
    }

    public static RepositoryCheckResult Write(string repositoryRoot)
    {
        try
        {
            var root = ResolveRepositoryRoot(repositoryRoot);
            var bytes = BuildFixtureBytes();
            var fixturePath = ResolveWritableFixturePath(root);
            WriteAtomic(fixturePath, bytes);

            var verification = Inspect(root);
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
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            return Failed(SingleLine(exception.Message));
        }
    }

    internal static byte[] BuildFixtureBytes()
    {
        var cases = Cases();

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
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
            writer.Flush();
        }

        stream.WriteByte((byte)'\n');
        return stream.ToArray();
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

    private static string ResolveRepositoryRoot(string repositoryRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        var root = Path.GetFullPath(repositoryRoot);
        if (!Directory.Exists(root))
        {
            throw new InvalidDataException("repository root is missing or is not a directory");
        }

        RejectLink(root, "repository root");
        return root;
    }

    private static string ResolveExistingFixturePath(string root)
    {
        var parts = FixtureRelativePath.Split('/');
        var current = root;
        foreach (var part in parts[..^1])
        {
            RejectPortableAlias(current, part);
            current = Path.Combine(current, part);
            if (!Directory.Exists(current))
            {
                throw new InvalidDataException(
                    "achievement-candidate fixture parent is missing or is not a directory");
            }

            RejectLink(current, "achievement-candidate fixture parent");
        }

        RejectPortableAlias(current, parts[^1]);
        var path = Path.Combine(current, parts[^1]);
        if (!File.Exists(path))
        {
            throw new InvalidDataException(
                $"required fixture is missing: {FixtureRelativePath}");
        }

        RejectLink(path, "achievement-candidate fixture");
        return path;
    }

    private static string ResolveWritableFixturePath(string root)
    {
        var parts = FixtureRelativePath.Split('/');
        var current = root;
        foreach (var part in parts[..^1])
        {
            RejectPortableAlias(current, part);
            current = Path.Combine(current, part);
            if (TryGetAttributes(current, out var attributes))
            {
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw new InvalidDataException(
                        "achievement-candidate fixture parent must not be a link");
                }

                if ((attributes & FileAttributes.Directory) == 0)
                {
                    throw new InvalidDataException(
                        "achievement-candidate fixture parent is not a directory");
                }
            }
            else
            {
                Directory.CreateDirectory(current);
                RejectLink(current, "achievement-candidate fixture parent");
            }
        }

        RejectPortableAlias(current, parts[^1]);
        var path = Path.Combine(current, parts[^1]);
        if (TryGetAttributes(path, out var fixtureAttributes))
        {
            if ((fixtureAttributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidDataException(
                    "achievement-candidate fixture must not be a link");
            }

            if ((fixtureAttributes & FileAttributes.Directory) != 0)
            {
                throw new InvalidDataException(
                    "achievement-candidate fixture path is a directory");
            }
        }

        return path;
    }

    private static void RejectPortableAlias(string parent, string expectedName)
    {
        var count = 0;
        foreach (var entry in Directory.EnumerateFileSystemEntries(parent))
        {
            count++;
            if (count > MaximumSiblingEntries)
            {
                throw new InvalidDataException(
                    $"achievement-candidate fixture parent exceeds {MaximumSiblingEntries} entries");
            }

            var name = Path.GetFileName(entry);
            if (string.Equals(name, expectedName, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(name, expectedName, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "achievement-candidate fixture path has a portable case alias");
            }
        }
    }

    private static void RejectLink(string path, string label)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException($"{label} must not be a link");
        }
    }

    private static bool TryGetAttributes(string path, out FileAttributes attributes)
    {
        try
        {
            attributes = File.GetAttributes(path);
            return true;
        }
        catch (Exception exception) when (
            exception is FileNotFoundException or DirectoryNotFoundException)
        {
            attributes = default;
            return false;
        }
    }

    private static byte[] ReadBounded(string path)
    {
        using var input = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            16 * 1024,
            FileOptions.SequentialScan);
        if (input.Length > MaximumFixtureBytes)
        {
            throw new InvalidDataException(
                $"achievement-candidate fixture exceeds {MaximumFixtureBytes} bytes");
        }

        var expectedLength = checked((int)input.Length);
        var bytes = new byte[expectedLength];
        input.ReadExactly(bytes);
        return bytes;
    }

    private static void WriteAtomic(string path, ReadOnlySpan<byte> bytes)
    {
        var temporary = path + $".tmp-{Guid.NewGuid():N}";
        try
        {
            using (var output = new FileStream(
                temporary,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                16 * 1024,
                FileOptions.WriteThrough))
            {
                output.Write(bytes);
                output.Flush(flushToDisk: true);
            }

            RejectLink(Path.GetDirectoryName(path)!, "achievement-candidate fixture parent");
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    private static RepositoryCheckResult Passed(string operation, int bytes) =>
        new(
            "Achievement-candidate fixture",
            true,
            $"Shared achievement-candidate fixture {operation}: cases={CaseCount} bytes={bytes}.",
            []);

    private static RepositoryCheckResult Failed(string failure) =>
        new("Achievement-candidate fixture", false, string.Empty, [failure]);

    private static bool IsExpectedFailure(Exception exception) =>
        ExpectedFailureTypes.Any(type => type.IsAssignableFrom(exception.GetType()));

    private static string SingleLine(string value)
    {
        var singleLine = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return singleLine[..Math.Min(singleLine.Length, 512)];
    }

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
