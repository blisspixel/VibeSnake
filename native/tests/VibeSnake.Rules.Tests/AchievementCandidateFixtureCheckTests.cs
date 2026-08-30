using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using RepositoryChecks;

namespace VibeSnake.Rules.Tests;

public sealed class AchievementCandidateFixtureCheckTests
{
    private const string ExpectedSha256 =
        "262701784c6736b04ff1d97d641d606bc1ac5de0d6cb51494c6476aca1b7fcd6";

    private static readonly JsonSerializerOptions IndentedJson = new()
    {
        WriteIndented = true,
    };

    [Fact]
    public void Renderer_reproduces_the_reviewed_python_origin_bytes_exactly()
    {
        var first = AchievementCandidateFixtureCheck.BuildFixtureBytes();
        var second = AchievementCandidateFixtureCheck.BuildFixtureBytes();
        var checkedIn = File.ReadAllBytes(Path.Combine(
            ResolveRepositoryRoot(),
            AchievementCandidateFixtureCheck.FixtureRelativePath.Replace(
                '/',
                Path.DirectorySeparatorChar)));

        Assert.Equal(2_682, first.Length);
        Assert.Equal(ExpectedSha256, Sha256(first));
        Assert.Equal(first, second);
        Assert.Equal(checkedIn, first);
        Assert.Equal((byte)'\n', first[^1]);
        Assert.DoesNotContain((byte)'\r', first);
        Assert.False(first[0] == 0xef && first[1] == 0xbb && first[2] == 0xbf);
    }

    [Fact]
    public void Frozen_contract_metadata_cases_and_event_order_are_closed()
    {
        using var document = JsonDocument.Parse(
            AchievementCandidateFixtureCheck.BuildFixtureBytes());
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
        Assert.Equal("achievement-candidates-targeted-v1", root.GetProperty("contract").GetString());
        Assert.Equal("python-core-reference-v3", root.GetProperty("source_engine").GetString());
        Assert.Equal(
            "positions-injected-or-random-output-normalized-v2",
            root.GetProperty("randomness_policy").GetString());
        Assert.Equal(
            ["id", "version"],
            PropertyNames(root.GetProperty("ruleset")));
        Assert.Equal("vibesnake-core", root.GetProperty("ruleset").GetProperty("id").GetString());
        Assert.Equal(4, root.GetProperty("ruleset").GetProperty("version").GetInt32());
        Assert.Equal(
            [
                "terminal_achievement_candidates",
                "already_unlocked_suppression",
                "ordered_events",
            ],
            Strings(root.GetProperty("comparison_scope")));
        Assert.Equal(
            ["default_flag_off_corpus", "profile_lifetime_achievements"],
            Strings(root.GetProperty("excluded_scope")));

        var config = root.GetProperty("config");
        Assert.Equal(
            [
                "combo_window_ticks",
                "enable_achievement_candidates",
                "food_score",
                "height",
                "maximum_direction_queue",
                "maximum_score",
                "speed_bonus_ticks",
                "starvation_ticks",
                "width",
            ],
            PropertyNames(config));
        Assert.Equal(60, config.GetProperty("combo_window_ticks").GetInt32());
        Assert.True(config.GetProperty("enable_achievement_candidates").GetBoolean());
        Assert.Equal(10, config.GetProperty("food_score").GetInt32());
        Assert.Equal(33, config.GetProperty("height").GetInt32());
        Assert.Equal(3, config.GetProperty("maximum_direction_queue").GetInt32());
        Assert.Equal(2_000_000_000, config.GetProperty("maximum_score").GetInt32());
        Assert.Equal(30, config.GetProperty("speed_bonus_ticks").GetInt32());
        Assert.Equal(600, config.GetProperty("starvation_ticks").GetInt32());
        Assert.Equal(64, config.GetProperty("width").GetInt32());

        var cases = root.GetProperty("cases").EnumerateArray().ToArray();
        Assert.Equal(4, root.GetProperty("case_count").GetInt32());
        Assert.Equal(
            [
                "starvation-score-candidates",
                "starvation-suppresses-already-unlocked",
                "starvation-zero-score-no-candidates",
                "self-collision-score-candidates",
            ],
            cases.Select(item => item.GetProperty("id").GetString()));
        Assert.All(cases, item =>
        {
            Assert.Equal(["commands", "expected", "id", "initial"], PropertyNames(item));
            Assert.Empty(item.GetProperty("commands").EnumerateArray());
            Assert.Equal(1, item.GetProperty("expected").GetProperty("tick").GetInt32());
            Assert.False(item.GetProperty("expected").GetProperty("alive").GetBoolean());
            Assert.False(item.GetProperty("expected").GetProperty("won").GetBoolean());
        });

        Assert.Equal(
            ["moved", "died", "achievement_candidate", "achievement_candidate"],
            EventKinds(cases[0]));
        Assert.Equal([0, 1], AchievementValues(cases[0]));
        Assert.Equal(["moved", "died"], EventKinds(cases[1]));
        Assert.Equal(["first_bite", "century"],
            Strings(cases[1].GetProperty("initial").GetProperty("already_unlocked")));
        Assert.Empty(AchievementValues(cases[1]));
        Assert.Equal(["moved", "died"], EventKinds(cases[2]));
        Assert.Equal(0, cases[2].GetProperty("expected").GetProperty("score").GetInt32());
        Assert.Equal(
            ["died", "achievement_candidate", "achievement_candidate"],
            EventKinds(cases[3]));
        Assert.Equal([0, 1], AchievementValues(cases[3]));
        Assert.Equal(
            "self_collision",
            cases[3].GetProperty("expected").GetProperty("death_cause").GetString());
        Assert.Equal(
            [2, 2],
            Ints(cases[3].GetProperty("expected").GetProperty("events")[0]
                .GetProperty("position")));
        Assert.Equal(
            [2, 1],
            Ints(cases[3].GetProperty("expected").GetProperty("head")));
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
                path => File.AppendAllText(path, "\n", new UTF8Encoding(false)),
                path => File.WriteAllText(
                    path,
                    JsonSerializer.Serialize(
                        JsonDocument.Parse(CanonicalBytes()).RootElement,
                        IndentedJson) + "\n",
                    new UTF8Encoding(false)),
                path => File.WriteAllBytes(path, [0xff, 0xfe, 0xfd]),
                path => File.WriteAllBytes(path, new byte[(64 * 1024) + 1]),
            };

            foreach (var mutate in variants)
            {
                WriteCanonicalFixture(root);
                var path = FixturePath(root);
                mutate(path);
                var before = File.Exists(path) ? File.ReadAllBytes(path) : null;

                var result = AchievementCandidateFixtureCheck.Inspect(root);

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
    public void Writer_creates_replaces_repeats_and_self_verifies_atomically()
    {
        WithTemporaryDirectory(root =>
        {
            var first = AchievementCandidateFixtureCheck.Write(root);
            Assert.True(first.Passed, string.Join(Environment.NewLine, first.Failures));
            Assert.Equal(
                "Shared achievement-candidate fixture written: cases=4 bytes=2682.",
                first.SuccessMessage);
            Assert.Equal(CanonicalBytes(), File.ReadAllBytes(FixturePath(root)));

            File.WriteAllText(FixturePath(root), "stale\n", new UTF8Encoding(false));
            var second = AchievementCandidateFixtureCheck.Write(root);
            var inspection = AchievementCandidateFixtureCheck.Inspect(root);

            Assert.True(second.Passed, string.Join(Environment.NewLine, second.Failures));
            Assert.True(inspection.Passed, string.Join(Environment.NewLine, inspection.Failures));
            Assert.Equal(
                "Shared achievement-candidate fixture verified: cases=4 bytes=2682.",
                inspection.SuccessMessage);
            Assert.Equal(ExpectedSha256, Sha256(File.ReadAllBytes(FixturePath(root))));
            Assert.Empty(Directory.EnumerateFiles(
                Path.GetDirectoryName(FixturePath(root))!,
                "*.tmp-*"));
        });
    }

    [Fact]
    public void Fixed_path_rejects_invalid_roots_parent_files_directories_and_case_aliases()
    {
        WithTemporaryDirectory(root =>
        {
            var missingParent = AchievementCandidateFixtureCheck.Inspect(root);
            AssertFailure(missingParent, "parent is missing");

            var missing = AchievementCandidateFixtureCheck.Inspect(Path.Combine(root, "missing"));
            AssertFailure(missing, "repository root");

            var invalid = AchievementCandidateFixtureCheck.Inspect(" ");
            Assert.False(invalid.Passed);

            File.WriteAllText(Path.Combine(root, "tests"), "blocked", new UTF8Encoding(false));
            var parentFile = AchievementCandidateFixtureCheck.Write(root);
            AssertFailure(parentFile, "parent is not a directory");
            File.Delete(Path.Combine(root, "tests"));

            Directory.CreateDirectory(FixturePath(root));
            var outputDirectory = AchievementCandidateFixtureCheck.Write(root);
            AssertFailure(outputDirectory, "path is a directory");
        });

        if (!OperatingSystem.IsWindows())
        {
            WithTemporaryDirectory(root =>
            {
                Directory.CreateDirectory(Path.Combine(root, "Tests"));
                var alias = AchievementCandidateFixtureCheck.Write(root);
                AssertFailure(alias, "portable case alias");
            });
        }

        WithTemporaryDirectory(root =>
        {
            for (var index = 0; index <= 256; index++)
            {
                File.WriteAllText(
                    Path.Combine(root, $"entry-{index:D3}"),
                    string.Empty,
                    new UTF8Encoding(false));
            }

            var bounded = AchievementCandidateFixtureCheck.Write(root);
            AssertFailure(bounded, "exceeds 256 entries");
        });
    }

    [Fact]
    public void Failed_atomic_replacement_cleans_up_its_private_temporary_file()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        WithTemporaryDirectory(root =>
        {
            WriteCanonicalFixture(root);
            using (new FileStream(
                FixturePath(root),
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read))
            {
                var result = AchievementCandidateFixtureCheck.Write(root);
                Assert.False(result.Passed);
            }

            Assert.Empty(Directory.EnumerateFiles(
                Path.GetDirectoryName(FixturePath(root))!,
                "*.tmp-*"));
            Assert.Equal(CanonicalBytes(), File.ReadAllBytes(FixturePath(root)));
        });
    }

    [Fact]
    public void Linked_parent_and_output_are_rejected_without_touching_external_files()
    {
        WithTemporaryDirectory(root =>
        WithTemporaryDirectory(external =>
        {
            var sentinel = Path.Combine(external, "sentinel.txt");
            File.WriteAllText(sentinel, "preserve\n", new UTF8Encoding(false));
            try
            {
                Directory.CreateSymbolicLink(Path.Combine(root, "tests"), external);
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
            {
                return;
            }

            var parentResult = AchievementCandidateFixtureCheck.Write(root);
            AssertFailure(parentResult, "must not be a link");
            Assert.Equal("preserve\n", File.ReadAllText(sentinel));
        }));

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

            var outputResult = AchievementCandidateFixtureCheck.Write(root);
            AssertFailure(outputResult, "must not be a link");
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
                RepositoryCheckCommand.Run(
                    ["achievement-candidates", root],
                    output,
                    error));
            Assert.Contains("verified", output.ToString(), StringComparison.Ordinal);
            Assert.Equal(string.Empty, error.ToString());

            File.WriteAllText(FixturePath(root), "stale\n", new UTF8Encoding(false));
            output = new StringWriter();
            error = new StringWriter();
            Assert.Equal(
                1,
                RepositoryCheckCommand.Run(
                    ["achievement-candidates", root],
                    output,
                    error));
            Assert.Contains("stale or noncanonical", error.ToString(), StringComparison.Ordinal);

            output = new StringWriter();
            error = new StringWriter();
            Assert.Equal(
                0,
                RepositoryCheckCommand.Run(
                    ["achievement-candidates-write", root],
                    output,
                    error));
            Assert.Contains("written", output.ToString(), StringComparison.Ordinal);
            Assert.Equal(CanonicalBytes(), File.ReadAllBytes(FixturePath(root)));

            output = new StringWriter();
            error = new StringWriter();
            Assert.Equal(
                2,
                RepositoryCheckCommand.Run(
                    ["achievement-candidates-write", root, "extra"],
                    output,
                    error));
            Assert.Contains("achievement-candidates-write", error.ToString(), StringComparison.Ordinal);
        });
    }

    private static byte[] CanonicalBytes() =>
        AchievementCandidateFixtureCheck.BuildFixtureBytes();

    private static string FixturePath(string root) => Path.Combine(
        root,
        AchievementCandidateFixtureCheck.FixtureRelativePath.Replace(
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

    private static string[] EventKinds(JsonElement traceCase) =>
        traceCase.GetProperty("expected").GetProperty("events")
            .EnumerateArray()
            .Select(item => item.GetProperty("kind").GetString()!)
            .ToArray();

    private static int[] AchievementValues(JsonElement traceCase) =>
        traceCase.GetProperty("expected").GetProperty("events")
            .EnumerateArray()
            .Where(item => item.GetProperty("kind").GetString() == "achievement_candidate")
            .Select(item => item.GetProperty("value").GetInt32())
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
            "vibesnake-achievement-fixture-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            action(root);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
