using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using RepositoryChecks;

namespace VibeSnake.Rules.Tests;

public sealed class RemainingPowersFixtureCheckTests
{
    private const string ExpectedSha256 =
        "92bec4caeca22c28d81ced7045168e93aeeb946df71e8c9a61872798210ba2b9";

    private static readonly JsonSerializerOptions IndentedJson = new()
    {
        WriteIndented = true,
    };

    [Fact]
    public void Renderer_reproduces_the_reviewed_python_origin_bytes_exactly()
    {
        var first = RemainingPowersFixtureCheck.BuildFixtureBytes();
        var second = RemainingPowersFixtureCheck.BuildFixtureBytes();
        var checkedIn = File.ReadAllBytes(Path.Combine(
            ResolveRepositoryRoot(),
            RemainingPowersFixtureCheck.FixtureRelativePath.Replace(
                '/',
                Path.DirectorySeparatorChar)));

        Assert.Equal(9_548, first.Length);
        Assert.Equal(ExpectedSha256, Sha256(first));
        Assert.Equal(first, second);
        Assert.Equal(checkedIn, first);
        Assert.Equal((byte)'\n', first[^1]);
        Assert.DoesNotContain((byte)'\r', first);
        Assert.False(first[0] == 0xef && first[1] == 0xbb && first[2] == 0xbf);
    }

    [Fact]
    public void Frozen_contract_metadata_cases_states_and_event_order_are_closed()
    {
        using var document = JsonDocument.Parse(
            RemainingPowersFixtureCheck.BuildFixtureBytes());
        var root = document.RootElement;

        Assert.Equal(
            [
                "case_count",
                "cases",
                "comparison_scope",
                "config",
                "contract",
                "excluded_scope",
                "randomness_policy",
                "ruleset",
                "schema_version",
                "source_engine",
            ],
            PropertyNames(root));
        Assert.Equal(1, root.GetProperty("schema_version").GetInt32());
        Assert.Equal(
            "remaining-powers-rules-targeted-v1",
            root.GetProperty("contract").GetString());
        Assert.Equal(
            "python-production-remaining-powers-v1",
            root.GetProperty("source_engine").GetString());
        Assert.Equal(
            "positions-and-power-state-injected-v1",
            root.GetProperty("randomness_policy").GetString());
        Assert.Equal(["id", "version"], PropertyNames(root.GetProperty("ruleset")));
        Assert.Equal("vibesnake-core", root.GetProperty("ruleset").GetProperty("id").GetString());
        Assert.Equal(4, root.GetProperty("ruleset").GetProperty("version").GetInt32());
        Assert.Equal(
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
            ],
            Strings(root.GetProperty("comparison_scope")));
        Assert.Equal(
            [
                "random_spawn_position",
                "spawn_schedule",
                "presentation_feedback",
                "food_respawn_position_after_eat",
                "shield_phase_last_stand",
            ],
            Strings(root.GetProperty("excluded_scope")));

        var config = root.GetProperty("config");
        Assert.Equal(
            [
                "boost_duration_ticks",
                "gluttony_duration_ticks",
                "height",
                "magnet_duration_ticks",
                "power_visible_ticks",
                "segment_detach_max_segments",
                "segment_detach_obstacle_ticks",
                "slow_mo_duration_ticks",
                "starvation_ticks",
                "width",
            ],
            PropertyNames(config));
        Assert.Equal(
            [80, 100, 33, 120, 120, 5, 200, 120, 600, 64],
            config.EnumerateObject().Select(property => property.Value.GetInt32()));

        var cases = root.GetProperty("cases").EnumerateArray().ToArray();
        Assert.Equal(9, root.GetProperty("case_count").GetInt32());
        var expectedIds = new[]
        {
            "slow-mo-collect-on-entry",
            "boost-collect-on-entry",
            "magnet-collect-on-entry",
            "magnet-pull-food-toward-head",
            "gluttony-collect-on-entry",
            "gluttony-eat-without-growth",
            "bait-collect-on-entry",
            "segment-detach-on-entry",
            "tempo-compose-active-countdown",
        };
        Assert.Equal(expectedIds, cases.Select(item => item.GetProperty("id").GetString()));
        Assert.Equal(expectedIds.Length, expectedIds.Distinct(StringComparer.Ordinal).Count());

        Assert.All(cases, item =>
        {
            Assert.Equal(
                ["expected", "id", "initial", "skip_food_after_eat"],
                PropertyNames(item));
            Assert.Equal(
                [
                    "alive",
                    "bait_position",
                    "body",
                    "boost_ticks_remaining",
                    "death_cause",
                    "detached_obstacle_ticks_remaining",
                    "detached_obstacles",
                    "events",
                    "food",
                    "gluttony_ticks_remaining",
                    "head",
                    "magnet_ticks_remaining",
                    "movement_cadence_denominator",
                    "movement_cadence_numerator",
                    "pickup",
                    "skip_food",
                    "slow_mo_ticks_remaining",
                    "starvation_ticks_elapsed",
                    "tick",
                ],
                PropertyNames(item.GetProperty("expected")));
            Assert.Equal(
                [
                    "bait_position",
                    "body",
                    "boost_ticks_remaining",
                    "detached_obstacle_ticks_remaining",
                    "detached_obstacles",
                    "direction",
                    "food",
                    "gluttony_ticks_remaining",
                    "magnet_ticks_remaining",
                    "pickup",
                    "slow_mo_ticks_remaining",
                    "starvation_ticks_elapsed",
                ],
                PropertyNames(item.GetProperty("initial")));
            var expected = item.GetProperty("expected");
            Assert.True(expected.GetProperty("alive").GetBoolean());
            Assert.Equal(JsonValueKind.Null, expected.GetProperty("death_cause").ValueKind);
            Assert.Equal(JsonValueKind.Null, expected.GetProperty("pickup").ValueKind);
            Assert.Equal(1, expected.GetProperty("tick").GetInt32());
            Assert.Equal(
                item.GetProperty("skip_food_after_eat").GetBoolean(),
                expected.GetProperty("skip_food").GetBoolean());
        });

        Assert.Collection(
            cases.Where(item => item.GetProperty("initial").GetProperty("pickup").ValueKind != JsonValueKind.Null),
            item => AssertPickup(item, "slow_mo", [6, 5]),
            item => AssertPickup(item, "boost", [6, 5]),
            item => AssertPickup(item, "magnet", [6, 5]),
            item => AssertPickup(item, "gluttony", [6, 5]),
            item => AssertPickup(item, "bait", [6, 5]),
            item => AssertPickup(item, "segment_detach", [6, 1]));

        Assert.Equal(["moved", "power_collected", "power_activated"], EventKinds(cases[0]));
        Assert.Equal(120, ExpectedInt(cases[0], "slow_mo_ticks_remaining"));
        Assert.Equal(2, ExpectedInt(cases[0], "movement_cadence_numerator"));
        Assert.Equal(80, ExpectedInt(cases[1], "boost_ticks_remaining"));
        Assert.Equal(2, ExpectedInt(cases[1], "movement_cadence_denominator"));
        Assert.Equal(120, ExpectedInt(cases[2], "magnet_ticks_remaining"));
        Assert.Equal([20, 20], ExpectedPoint(cases[2], "food"));

        Assert.Equal(["moved"], EventKinds(cases[3]));
        Assert.Equal([5, 4], ExpectedPoint(cases[3], "food"));
        Assert.Equal(2, ExpectedInt(cases[3], "magnet_ticks_remaining"));

        Assert.Equal(100, ExpectedInt(cases[4], "gluttony_ticks_remaining"));
        Assert.Equal(
            ["moved", "ate_food", "score_changed", "hunger_reset"],
            EventKinds(cases[5]));
        Assert.Equal([[2, 1], [3, 1]], ExpectedPoints(cases[5], "body"));
        Assert.Equal(JsonValueKind.Null, Expected(cases[5]).GetProperty("food").ValueKind);
        Assert.Equal(2, ExpectedInt(cases[5], "gluttony_ticks_remaining"));
        Assert.Equal(18, EventValue(cases[5], 2));
        Assert.Equal(600, EventValue(cases[5], 3));
        Assert.True(cases[5].GetProperty("skip_food_after_eat").GetBoolean());

        Assert.Equal([6, 5], ExpectedPoint(cases[6], "bait_position"));
        Assert.Equal(
            ["kind", "position", "power", "value"],
            PropertyNames(Expected(cases[6]).GetProperty("events")[2]));
        Assert.Equal(0, EventValue(cases[6], 2));

        Assert.Equal([[6, 1]], ExpectedPoints(cases[7], "body"));
        Assert.Equal(
            [[1, 1], [2, 1], [3, 1], [4, 1], [5, 1]],
            ExpectedPoints(cases[7], "detached_obstacles"));
        Assert.Equal(200, ExpectedInt(cases[7], "detached_obstacle_ticks_remaining"));
        Assert.Equal(5, EventValue(cases[7], 2));

        Assert.Equal(["moved"], EventKinds(cases[8]));
        Assert.Equal(2, ExpectedInt(cases[8], "slow_mo_ticks_remaining"));
        Assert.Equal(1, ExpectedInt(cases[8], "boost_ticks_remaining"));
        Assert.Equal(2, ExpectedInt(cases[8], "movement_cadence_numerator"));
        Assert.Equal(2, ExpectedInt(cases[8], "movement_cadence_denominator"));
    }

    [Fact]
    public void Inspect_accepts_only_the_exact_bounded_fixture_bytes()
    {
        WithTemporaryDirectory(root =>
        {
            var variants = new Action<string>[]
            {
                path => File.Delete(path),
                path =>
                {
                    var bytes = File.ReadAllBytes(path);
                    bytes[20] ^= 1;
                    File.WriteAllBytes(path, bytes);
                },
                path => File.WriteAllBytes(path, [0xef, 0xbb, 0xbf, .. CanonicalBytes()]),
                path => File.WriteAllText(
                    path,
                    Encoding.UTF8.GetString(CanonicalBytes()).Replace("\n", "\r\n", StringComparison.Ordinal),
                    new UTF8Encoding(false)),
                path => File.WriteAllBytes(path, CanonicalBytes()[..^1]),
                path => File.AppendAllText(path, "\n", new UTF8Encoding(false)),
                path => File.WriteAllText(
                    path,
                    JsonSerializer.Serialize(
                        JsonDocument.Parse(CanonicalBytes()).RootElement,
                        IndentedJson) + "\n",
                    new UTF8Encoding(false)),
                path => File.WriteAllText(
                    path,
                    Encoding.UTF8.GetString(CanonicalBytes()).Replace(
                        "{\"case_count\":9",
                        "{\"case_count\":9,\"case_count\":9",
                        StringComparison.Ordinal),
                    new UTF8Encoding(false)),
                path => File.AppendAllText(path, "{}", new UTF8Encoding(false)),
                path => File.WriteAllBytes(path, [0xff, 0xfe, 0xfd]),
                path => File.WriteAllBytes(path, new byte[FixedCanonicalFixtureFile.MaximumBytes + 1]),
            };

            foreach (var mutate in variants)
            {
                WriteCanonicalFixture(root);
                var path = FixturePath(root);
                mutate(path);
                var before = File.Exists(path) ? File.ReadAllBytes(path) : null;

                var result = RemainingPowersFixtureCheck.Inspect(root);

                Assert.False(result.Passed);
                Assert.Single(result.Failures);
                Assert.True(result.Failures[0].Length <= 512);
                Assert.DoesNotContain('\r', result.Failures[0]);
                Assert.DoesNotContain('\n', result.Failures[0]);
                if (before is not null)
                {
                    Assert.Equal(before, File.ReadAllBytes(path));
                }
            }
        });
    }

    [Fact]
    public void Writer_creates_replaces_repeats_and_self_verifies()
    {
        WithTemporaryDirectory(root =>
        {
            var first = RemainingPowersFixtureCheck.Write(root);
            Assert.True(first.Passed, string.Join(Environment.NewLine, first.Failures));
            Assert.Equal(
                "Shared Remaining Powers fixture written: cases=9 bytes=9548.",
                first.SuccessMessage);
            Assert.Equal(CanonicalBytes(), File.ReadAllBytes(FixturePath(root)));

            File.WriteAllText(FixturePath(root), "stale\n", new UTF8Encoding(false));
            var second = RemainingPowersFixtureCheck.Write(root);
            var third = RemainingPowersFixtureCheck.Write(root);
            var inspection = RemainingPowersFixtureCheck.Inspect(root);

            Assert.True(second.Passed, string.Join(Environment.NewLine, second.Failures));
            Assert.True(third.Passed, string.Join(Environment.NewLine, third.Failures));
            Assert.True(inspection.Passed, string.Join(Environment.NewLine, inspection.Failures));
            Assert.Equal(
                "Shared Remaining Powers fixture verified: cases=9 bytes=9548.",
                inspection.SuccessMessage);
            Assert.Equal(ExpectedSha256, Sha256(File.ReadAllBytes(FixturePath(root))));
            Assert.Empty(Directory.EnumerateFiles(
                Path.GetDirectoryName(FixturePath(root))!,
                "*.tmp-*"));
        });
    }

    [Fact]
    public void Fixed_path_rejects_invalid_roots_parent_files_directories_and_symbolic_links()
    {
        WithTemporaryDirectory(root =>
        {
            AssertFailure(RemainingPowersFixtureCheck.Inspect(root), "parent is missing");
            AssertFailure(
                RemainingPowersFixtureCheck.Inspect(Path.Combine(root, "missing")),
                "repository root");
            Assert.False(RemainingPowersFixtureCheck.Inspect(" ").Passed);

            File.WriteAllText(Path.Combine(root, "tests"), "blocked", new UTF8Encoding(false));
            AssertFailure(RemainingPowersFixtureCheck.Write(root), "parent is not a directory");
            File.Delete(Path.Combine(root, "tests"));

            Directory.CreateDirectory(FixturePath(root));
            AssertFailure(RemainingPowersFixtureCheck.Write(root), "path is a directory");
        });

        if (!OperatingSystem.IsWindows())
        {
            WithTemporaryDirectory(root =>
            {
                Directory.CreateDirectory(Path.Combine(root, "Tests"));
                AssertFailure(RemainingPowersFixtureCheck.Write(root), "portable case alias");
            });
        }

        WithTemporaryDirectory(root =>
        WithTemporaryDirectory(external =>
        {
            var sentinel = Path.Combine(external, "sentinel.json");
            File.WriteAllText(sentinel, "preserve\n", new UTF8Encoding(false));
            Directory.CreateDirectory(Path.GetDirectoryName(FixturePath(root))!);
            try
            {
                File.CreateSymbolicLink(FixturePath(root), sentinel);
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
            {
                return;
            }

            AssertFailure(RemainingPowersFixtureCheck.Write(root), "must not be a link");
            Assert.Equal("preserve\n", File.ReadAllText(sentinel));
        }));
    }

    [Fact]
    public void Commands_cover_check_write_stale_failure_and_usage()
    {
        WithTemporaryDirectory(root =>
        {
            WriteCanonicalFixture(root);
            var output = new StringWriter();
            var error = new StringWriter();
            Assert.Equal(
                0,
                RepositoryCheckCommand.Run(["remaining-powers", root], output, error));
            Assert.Contains("verified", output.ToString(), StringComparison.Ordinal);
            Assert.Equal(string.Empty, error.ToString());

            File.WriteAllText(FixturePath(root), "stale\n", new UTF8Encoding(false));
            output = new StringWriter();
            error = new StringWriter();
            Assert.Equal(
                1,
                RepositoryCheckCommand.Run(["remaining-powers", root], output, error));
            Assert.Contains("stale or noncanonical", error.ToString(), StringComparison.Ordinal);

            output = new StringWriter();
            error = new StringWriter();
            Assert.Equal(
                0,
                RepositoryCheckCommand.Run(["remaining-powers-write", root], output, error));
            Assert.Contains("written", output.ToString(), StringComparison.Ordinal);
            Assert.Equal(CanonicalBytes(), File.ReadAllBytes(FixturePath(root)));

            output = new StringWriter();
            error = new StringWriter();
            Assert.Equal(
                2,
                RepositoryCheckCommand.Run(
                    ["remaining-powers-write", root, "extra"],
                    output,
                    error));
            Assert.Contains("remaining-powers-write", error.ToString(), StringComparison.Ordinal);
        });
    }

    private static void AssertPickup(
        JsonElement traceCase,
        string expectedKind,
        int[] expectedPosition)
    {
        var pickup = traceCase.GetProperty("initial").GetProperty("pickup");
        Assert.Equal(
            ["kind", "position", "visibility_ticks_remaining"],
            PropertyNames(pickup));
        Assert.Equal(expectedKind, pickup.GetProperty("kind").GetString());
        Assert.Equal(expectedPosition, Ints(pickup.GetProperty("position")));
        Assert.Equal(10, pickup.GetProperty("visibility_ticks_remaining").GetInt32());
    }

    private static byte[] CanonicalBytes() =>
        RemainingPowersFixtureCheck.BuildFixtureBytes();

    private static string FixturePath(string root) => Path.Combine(
        root,
        RemainingPowersFixtureCheck.FixtureRelativePath.Replace(
            '/',
            Path.DirectorySeparatorChar));

    private static void WriteCanonicalFixture(string root)
    {
        var path = FixturePath(root);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, CanonicalBytes());
    }

    private static string[] PropertyNames(JsonElement element) =>
        element.EnumerateObject().Select(property => property.Name).ToArray();

    private static string[] Strings(JsonElement element) =>
        element.EnumerateArray().Select(item => item.GetString()!).ToArray();

    private static int[] Ints(JsonElement element) =>
        element.EnumerateArray().Select(item => item.GetInt32()).ToArray();

    private static int[][] Points(JsonElement element) =>
        element.EnumerateArray().Select(Ints).ToArray();

    private static JsonElement Expected(JsonElement traceCase) =>
        traceCase.GetProperty("expected");

    private static int ExpectedInt(JsonElement traceCase, string property) =>
        Expected(traceCase).GetProperty(property).GetInt32();

    private static int[] ExpectedPoint(JsonElement traceCase, string property) =>
        Ints(Expected(traceCase).GetProperty(property));

    private static int[][] ExpectedPoints(JsonElement traceCase, string property) =>
        Points(Expected(traceCase).GetProperty(property));

    private static string[] EventKinds(JsonElement traceCase) =>
        Expected(traceCase).GetProperty("events")
            .EnumerateArray()
            .Select(item => item.GetProperty("kind").GetString()!)
            .ToArray();

    private static int EventValue(JsonElement traceCase, int eventIndex) =>
        Expected(traceCase).GetProperty("events")[eventIndex]
            .GetProperty("value").GetInt32();

    private static string Sha256(byte[] bytes) =>
        Convert.ToHexStringLower(SHA256.HashData(bytes));

    private static void AssertFailure(RepositoryCheckResult result, string expected)
    {
        Assert.False(result.Passed);
        Assert.Contains(result.Failures, failure => failure.Contains(expected, StringComparison.Ordinal));
    }

    private static string ResolveRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "VERSION")))
        {
            current = current.Parent;
        }

        return current?.FullName
            ?? throw new DirectoryNotFoundException("Could not resolve repository root.");
    }

    private static void WithTemporaryDirectory(Action<string> action)
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "vibesnake-remaining-powers-fixture-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Exception? primaryFailure = null;
        try
        {
            action(root);
        }
        catch (Exception exception)
        {
            primaryFailure = exception;
            throw;
        }
        finally
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch when (primaryFailure is not null)
            {
                // Preserve the test-body failure rather than masking it with cleanup.
            }
        }
    }
}
