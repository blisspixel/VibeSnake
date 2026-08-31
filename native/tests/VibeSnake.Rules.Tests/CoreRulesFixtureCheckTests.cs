using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using RepositoryChecks;

namespace VibeSnake.Rules.Tests;

public sealed class CoreRulesFixtureCheckTests
{
    private const string ExpectedSha256 =
        "a3ca486b827221a36fd86b97d62b5ddc9a409576a471f7793779e1b9e59f38af";

    private static readonly JsonSerializerOptions IndentedJson = new()
    {
        WriteIndented = true,
    };

    [Fact]
    public void Renderer_reproduces_the_reviewed_python_origin_bytes_exactly()
    {
        var first = CoreRulesFixtureCheck.BuildFixtureBytes();
        var second = CoreRulesFixtureCheck.BuildFixtureBytes();
        var checkedIn = File.ReadAllBytes(Path.Combine(
            ResolveRepositoryRoot(),
            CoreRulesFixtureCheck.FixtureRelativePath.Replace(
                '/',
                Path.DirectorySeparatorChar)));

        Assert.Equal(57_031, first.Length);
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
        using var document = JsonDocument.Parse(CoreRulesFixtureCheck.BuildFixtureBytes());
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
        Assert.Equal(4, root.GetProperty("schema_version").GetInt32());
        Assert.Equal("core-rules-targeted-v4", root.GetProperty("contract").GetString());
        Assert.Equal("python-core-reference-v3", root.GetProperty("source_engine").GetString());
        Assert.Equal(
            "positions-injected-or-random-output-normalized-v2",
            root.GetProperty("randomness_policy").GetString());
        Assert.Equal(["id", "version"], PropertyNames(root.GetProperty("ruleset")));
        Assert.Equal("vibesnake-core", root.GetProperty("ruleset").GetProperty("id").GetString());
        Assert.Equal(4, root.GetProperty("ruleset").GetProperty("version").GetInt32());
        Assert.Equal(
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
            ],
            Strings(root.GetProperty("comparison_scope")));
        Assert.Equal(
            ["food_respawn_coordinate", "risk_bonus", "power_effects"],
            Strings(root.GetProperty("excluded_scope")));

        var config = root.GetProperty("config");
        Assert.Equal(
            [
                "combo_window_ticks",
                "food_score",
                "height",
                "maximum_direction_queue",
                "maximum_score",
                "speed_bonus_ticks",
                "starvation_ticks",
                "width",
            ],
            PropertyNames(config));
        Assert.Equal(
            [60, 10, 33, 3, 2_000_000_000, 30, 600, 64],
            config.EnumerateObject().Select(property => property.Value.GetInt32()));

        var cases = root.GetProperty("cases").EnumerateArray().ToArray();
        var expectedIds = new[]
        {
            "food-entry",
            "food-buffered-turn",
            "queue-rejections-and-consumption",
            "queue-capacity",
            "combo-before-three",
            "combo-threshold-three",
            "combo-after-three",
            "combo-threshold-five",
            "combo-after-five",
            "combo-before-ten",
            "combo-threshold-ten",
            "combo-after-ten",
            "combo-before-twenty",
            "combo-threshold-twenty",
            "combo-after-twenty-cap",
            "speed-bonus-last-eligible-tick",
            "speed-bonus-exact-boundary",
            "speed-bonus-after-boundary",
            "combo-window-exact-no-food",
            "combo-window-expired-no-food",
            "combo-window-exact-food",
            "expired-combo-late-food-no-speed-bonus",
            "length-exact-ten",
            "length-first-bonus",
            "length-above-boundary",
            "score-saturation-near-cap",
            "score-at-cap",
            "self-collision",
            "departing-tail-is-safe",
            "horizontal-wrap",
            "starvation-predeadline",
            "starvation-deadline-food-rescue",
            "starvation-deadline-death",
            "starvation-collision-precedence",
            "full-grid-victory",
        };
        Assert.Equal(35, root.GetProperty("case_count").GetInt32());
        Assert.Equal(expectedIds, cases.Select(item => item.GetProperty("id").GetString()));
        Assert.Equal(expectedIds.Length, expectedIds.Distinct(StringComparer.Ordinal).Count());

        Assert.All(cases, item =>
        {
            Assert.Equal(
                ["command_acceptance", "commands", "expected", "id", "initial"],
                PropertyNames(item));
            Assert.Equal(
                [
                    "alive",
                    "ate_food",
                    "body",
                    "combo",
                    "death_cause",
                    "direction",
                    "events",
                    "food_unchanged",
                    "head",
                    "pending_directions",
                    "random_respawn",
                    "random_use",
                    "score",
                    "starvation_ticks_elapsed",
                    "tick",
                    "ticks_since_last_food",
                    "won",
                    "wrapped",
                ],
                PropertyNames(item.GetProperty("expected")));
            Assert.Equal(
                [
                    "body",
                    "combo",
                    "direction",
                    "food",
                    "score",
                    "starvation_ticks_elapsed",
                    "ticks_since_last_food",
                ],
                PropertyNames(item.GetProperty("initial")));
            Assert.Equal(1, item.GetProperty("expected").GetProperty("tick").GetInt32());
        });

        Assert.Equal(
            [false, false, true, false, true, false],
            Booleans(cases[2].GetProperty("command_acceptance")));
        Assert.Equal(
            ["RIGHT", "LEFT", "UP", "DOWN", "LEFT", "LEFT"],
            Strings(cases[2].GetProperty("commands")));
        Assert.Equal(["LEFT", "DOWN"], Strings(Expected(cases[3]).GetProperty("pending_directions")));
        Assert.Equal(18, ExpectedInt(cases[0], "score"));
        Assert.Equal(50, ExpectedInt(cases[10], "score"));
        Assert.Equal(100, ExpectedInt(cases[13], "score"));
        Assert.Equal(100, ExpectedInt(cases[14], "score"));
        Assert.Equal(2_000_000_000, ExpectedInt(cases[26], "score"));
        Assert.Equal(0, ExpectedInt(cases[19], "combo"));
        Assert.Equal(5, ExpectedInt(cases[20], "combo"));
        Assert.Equal("advanced", Expected(cases[20]).GetProperty("random_use").GetString());
        Assert.Equal(599, ExpectedInt(cases[30], "starvation_ticks_elapsed"));
        Assert.True(Expected(cases[31]).GetProperty("alive").GetBoolean());
        Assert.Equal("starvation", Expected(cases[32]).GetProperty("death_cause").GetString());
        Assert.Equal("self_collision", Expected(cases[33]).GetProperty("death_cause").GetString());
        Assert.Equal(
            ["moved", "ate_food", "score_changed", "hunger_reset", "won"],
            EventKinds(cases[34]));
        Assert.Equal(2_112, Expected(cases[34]).GetProperty("body").GetArrayLength());
        Assert.True(Expected(cases[34]).GetProperty("won").GetBoolean());
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
                        "{\"case_count\":35",
                        "{\"case_count\":35,\"case_count\":35",
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

                var result = CoreRulesFixtureCheck.Inspect(root);

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
            var first = CoreRulesFixtureCheck.Write(root);
            Assert.True(first.Passed, string.Join(Environment.NewLine, first.Failures));
            Assert.Equal(
                "Shared Core Rules fixture written: cases=35 bytes=57031.",
                first.SuccessMessage);
            Assert.Equal(CanonicalBytes(), File.ReadAllBytes(FixturePath(root)));

            File.WriteAllText(FixturePath(root), "stale\n", new UTF8Encoding(false));
            var second = CoreRulesFixtureCheck.Write(root);
            var third = CoreRulesFixtureCheck.Write(root);
            var inspection = CoreRulesFixtureCheck.Inspect(root);

            Assert.True(second.Passed, string.Join(Environment.NewLine, second.Failures));
            Assert.True(third.Passed, string.Join(Environment.NewLine, third.Failures));
            Assert.True(inspection.Passed, string.Join(Environment.NewLine, inspection.Failures));
            Assert.Equal(
                "Shared Core Rules fixture verified: cases=35 bytes=57031.",
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
            AssertFailure(CoreRulesFixtureCheck.Inspect(root), "parent is missing");
            AssertFailure(
                CoreRulesFixtureCheck.Inspect(Path.Combine(root, "missing")),
                "repository root");
            Assert.False(CoreRulesFixtureCheck.Inspect(" ").Passed);

            File.WriteAllText(Path.Combine(root, "tests"), "blocked", new UTF8Encoding(false));
            AssertFailure(CoreRulesFixtureCheck.Write(root), "parent is not a directory");
            File.Delete(Path.Combine(root, "tests"));

            Directory.CreateDirectory(FixturePath(root));
            AssertFailure(CoreRulesFixtureCheck.Write(root), "path is a directory");
        });

        if (!OperatingSystem.IsWindows())
        {
            WithTemporaryDirectory(root =>
            {
                Directory.CreateDirectory(Path.Combine(root, "Tests"));
                AssertFailure(CoreRulesFixtureCheck.Write(root), "portable case alias");
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

            AssertFailure(CoreRulesFixtureCheck.Write(root), "must not be a link");
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
            Assert.Equal(0, RepositoryCheckCommand.Run(["core-rules", root], output, error));
            Assert.Contains("verified", output.ToString(), StringComparison.Ordinal);
            Assert.Equal(string.Empty, error.ToString());

            File.WriteAllText(FixturePath(root), "stale\n", new UTF8Encoding(false));
            output = new StringWriter();
            error = new StringWriter();
            Assert.Equal(1, RepositoryCheckCommand.Run(["core-rules", root], output, error));
            Assert.Contains("stale or noncanonical", error.ToString(), StringComparison.Ordinal);

            output = new StringWriter();
            error = new StringWriter();
            Assert.Equal(
                0,
                RepositoryCheckCommand.Run(["core-rules-write", root], output, error));
            Assert.Contains("written", output.ToString(), StringComparison.Ordinal);
            Assert.Equal(CanonicalBytes(), File.ReadAllBytes(FixturePath(root)));

            output = new StringWriter();
            error = new StringWriter();
            Assert.Equal(
                2,
                RepositoryCheckCommand.Run(["core-rules-write", root, "extra"], output, error));
            Assert.Contains("core-rules-write", error.ToString(), StringComparison.Ordinal);
        });
    }

    private static byte[] CanonicalBytes() => CoreRulesFixtureCheck.BuildFixtureBytes();

    private static string FixturePath(string root) => Path.Combine(
        root,
        CoreRulesFixtureCheck.FixtureRelativePath.Replace(
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

    private static bool[] Booleans(JsonElement element) =>
        element.EnumerateArray().Select(item => item.GetBoolean()).ToArray();

    private static JsonElement Expected(JsonElement traceCase) =>
        traceCase.GetProperty("expected");

    private static int ExpectedInt(JsonElement traceCase, string property) =>
        Expected(traceCase).GetProperty(property).GetInt32();

    private static string[] EventKinds(JsonElement traceCase) =>
        Expected(traceCase).GetProperty("events")
            .EnumerateArray()
            .Select(item => item.GetProperty("kind").GetString()!)
            .ToArray();

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
            "vibesnake-core-rules-fixture-tests",
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
