using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using RepositoryChecks;

namespace VibeSnake.Rules.Tests;

public sealed class LastStandFixtureCheckTests
{
    private const string ExpectedSha256 =
        "3c2cfec6ae10d7a3b0ba3e1dd753ee077d2297224a3f098c1c74addbd706e596";

    private static readonly JsonSerializerOptions IndentedJson = new()
    {
        WriteIndented = true,
    };

    [Fact]
    public void Renderer_reproduces_the_reviewed_python_origin_bytes_exactly()
    {
        var first = LastStandFixtureCheck.BuildFixtureBytes();
        var second = LastStandFixtureCheck.BuildFixtureBytes();
        var checkedIn = File.ReadAllBytes(Path.Combine(
            ResolveRepositoryRoot(),
            LastStandFixtureCheck.FixtureRelativePath.Replace(
                '/',
                Path.DirectorySeparatorChar)));

        Assert.Equal(3_596, first.Length);
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
        using var document = JsonDocument.Parse(LastStandFixtureCheck.BuildFixtureBytes());
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
        Assert.Equal("last-stand-rules-targeted-v1", root.GetProperty("contract").GetString());
        Assert.Equal("python-production-last-stand-v1", root.GetProperty("source_engine").GetString());
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
                "held_activation",
                "collision_revive",
                "body_shrink",
                "starvation_revive",
                "recovery_immunity",
                "recovery_expiry",
                "ordered_power_events",
            ],
            Strings(root.GetProperty("comparison_scope")));
        Assert.Equal(
            [
                "random_spawn_position",
                "spawn_schedule",
                "presentation_feedback",
                "other_power_types",
            ],
            Strings(root.GetProperty("excluded_scope")));

        var config = root.GetProperty("config");
        Assert.Equal(
            [
                "height",
                "last_stand_recovery_ticks",
                "power_visible_ticks",
                "starvation_ticks",
                "width",
            ],
            PropertyNames(config));
        Assert.Equal(33, config.GetProperty("height").GetInt32());
        Assert.Equal(60, config.GetProperty("last_stand_recovery_ticks").GetInt32());
        Assert.Equal(120, config.GetProperty("power_visible_ticks").GetInt32());
        Assert.Equal(600, config.GetProperty("starvation_ticks").GetInt32());
        Assert.Equal(64, config.GetProperty("width").GetInt32());

        var cases = root.GetProperty("cases").EnumerateArray().ToArray();
        Assert.Equal(5, root.GetProperty("case_count").GetInt32());
        Assert.Equal(
            [
                "last-stand-collect-on-entry",
                "last-stand-collision-revive",
                "last-stand-recovery-blocks-collision",
                "last-stand-starvation-revive",
                "last-stand-recovery-expiry",
            ],
            cases.Select(item => item.GetProperty("id").GetString()));
        Assert.All(cases, item =>
        {
            Assert.Equal(["expected", "id", "initial"], PropertyNames(item));
            Assert.Equal(
                [
                    "alive",
                    "body",
                    "death_cause",
                    "events",
                    "head",
                    "last_stand_held",
                    "pickup",
                    "recovery_ticks_remaining",
                    "starvation_ticks_elapsed",
                    "tick",
                ],
                PropertyNames(item.GetProperty("expected")));
            Assert.Equal(
                [
                    "body",
                    "direction",
                    "food",
                    "last_stand_held",
                    "pickup",
                    "recovery_ticks_remaining",
                    "starvation_ticks_elapsed",
                ],
                PropertyNames(item.GetProperty("initial")));
            Assert.True(item.GetProperty("expected").GetProperty("alive").GetBoolean());
            Assert.Equal(JsonValueKind.Null, item.GetProperty("expected").GetProperty("death_cause").ValueKind);
            Assert.Equal(JsonValueKind.Null, item.GetProperty("expected").GetProperty("pickup").ValueKind);
            Assert.Equal(1, item.GetProperty("expected").GetProperty("tick").GetInt32());
        });

        var pickup = cases[0].GetProperty("initial").GetProperty("pickup");
        Assert.Equal(["kind", "position", "visibility_ticks_remaining"], PropertyNames(pickup));
        Assert.Equal("last_stand", pickup.GetProperty("kind").GetString());
        Assert.Equal([6, 5], Ints(pickup.GetProperty("position")));
        Assert.Equal(10, pickup.GetProperty("visibility_ticks_remaining").GetInt32());
        Assert.Equal(
            ["moved", "power_collected", "power_activated"],
            EventKinds(cases[0]));
        Assert.True(cases[0].GetProperty("expected").GetProperty("last_stand_held").GetBoolean());

        Assert.Equal(
            ["power_consumed", "collision_prevented", "hunger_reset", "power_activated"],
            EventKinds(cases[1]));
        Assert.Equal(
            [[2, 2], [2, 1], [3, 1]],
            Points(cases[1].GetProperty("expected").GetProperty("body")));
        Assert.Equal(60, RecoveryTicks(cases[1]));
        Assert.Equal(
            ["death_cause", "kind", "position", "power"],
            PropertyNames(cases[1].GetProperty("expected").GetProperty("events")[1]));

        Assert.Equal(["collision_prevented"], EventKinds(cases[2]));
        Assert.Equal(1, RecoveryTicks(cases[2]));
        Assert.Equal(
            [[1, 1], [1, 2], [2, 2], [2, 1]],
            Points(cases[2].GetProperty("expected").GetProperty("body")));

        Assert.Equal(
            ["moved", "power_consumed", "collision_prevented", "hunger_reset", "power_activated"],
            EventKinds(cases[3]));
        Assert.Equal([[8, 5], [9, 5]], Points(cases[3].GetProperty("expected").GetProperty("body")));
        Assert.Equal(60, RecoveryTicks(cases[3]));
        Assert.Equal(0, cases[3].GetProperty("expected").GetProperty("starvation_ticks_elapsed").GetInt32());

        Assert.Equal(["power_expired", "moved"], EventKinds(cases[4]));
        Assert.Equal(0, RecoveryTicks(cases[4]));
        Assert.Equal([6, 5], Ints(cases[4].GetProperty("expected").GetProperty("head")));
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
                        "{\"case_count\":5",
                        "{\"case_count\":5,\"case_count\":5",
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

                var result = LastStandFixtureCheck.Inspect(root);

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
    public void Writer_creates_replaces_repeats_self_verifies_and_enforces_shared_bounds()
    {
        WithTemporaryDirectory(root =>
        {
            var first = LastStandFixtureCheck.Write(root);
            Assert.True(first.Passed, string.Join(Environment.NewLine, first.Failures));
            Assert.Equal(
                "Shared Last Stand fixture written: cases=5 bytes=3596.",
                first.SuccessMessage);
            Assert.Equal(CanonicalBytes(), File.ReadAllBytes(FixturePath(root)));

            File.WriteAllText(FixturePath(root), "stale\n", new UTF8Encoding(false));
            var second = LastStandFixtureCheck.Write(root);
            var inspection = LastStandFixtureCheck.Inspect(root);

            Assert.True(second.Passed, string.Join(Environment.NewLine, second.Failures));
            Assert.True(inspection.Passed, string.Join(Environment.NewLine, inspection.Failures));
            Assert.Equal(
                "Shared Last Stand fixture verified: cases=5 bytes=3596.",
                inspection.SuccessMessage);
            Assert.Equal(ExpectedSha256, Sha256(File.ReadAllBytes(FixturePath(root))));
            Assert.Empty(Directory.EnumerateFiles(
                Path.GetDirectoryName(FixturePath(root))!,
                "*.tmp-*"));

            var oversized = Assert.Throws<InvalidDataException>(() =>
                FixedCanonicalFixtureFile.Write(
                    root,
                    "tests/fixtures/shared/oversized.json",
                    "oversized fixture",
                    new byte[FixedCanonicalFixtureFile.MaximumBytes + 1]));
            Assert.Contains("exceeds 65536 bytes", oversized.Message, StringComparison.Ordinal);

            var oversizedRender = Assert.Throws<InvalidDataException>(() =>
                CanonicalFixtureJson.Render("oversized fixture", writer =>
                {
                    writer.WriteStartArray();
                    var chunk = new string('x', 1_024);
                    for (var index = 0; index < 70; index++)
                    {
                        writer.WriteStringValue(chunk);
                    }

                    writer.WriteEndArray();
                }));
            Assert.Contains(
                "exceeds 65536 bytes",
                oversizedRender.Message,
                StringComparison.Ordinal);

            var diagnostic = FixedCanonicalFixtureFile.SingleLine(
                new string('x', 511) + "\ud83d\ude00\r\nignored");
            Assert.True(diagnostic.Length <= 512);
            Assert.False(char.IsHighSurrogate(diagnostic[^1]));
            Assert.DoesNotContain('\r', diagnostic);
            Assert.DoesNotContain('\n', diagnostic);

            var cleanupInvoked = false;
            FixedCanonicalFixtureFile.RunCleanup(
                new IOException("primary"),
                () =>
                {
                    cleanupInvoked = true;
                    throw new UnauthorizedAccessException("secondary");
                });
            Assert.True(cleanupInvoked);
            var cleanupFailure = Assert.Throws<UnauthorizedAccessException>(() =>
                FixedCanonicalFixtureFile.RunCleanup(
                    null,
                    () => throw new UnauthorizedAccessException("secondary")));
            Assert.Equal("secondary", cleanupFailure.Message);
        });
    }

    [Fact]
    public void Fixed_path_rejects_invalid_roots_parent_files_directories_and_case_aliases()
    {
        WithTemporaryDirectory(root =>
        {
            AssertFailure(LastStandFixtureCheck.Inspect(root), "parent is missing");
            AssertFailure(
                LastStandFixtureCheck.Inspect(Path.Combine(root, "missing")),
                "repository root");
            Assert.False(LastStandFixtureCheck.Inspect(" ").Passed);

            File.WriteAllText(Path.Combine(root, "tests"), "blocked", new UTF8Encoding(false));
            AssertFailure(LastStandFixtureCheck.Write(root), "parent is not a directory");
            File.Delete(Path.Combine(root, "tests"));

            Directory.CreateDirectory(FixturePath(root));
            AssertFailure(LastStandFixtureCheck.Write(root), "path is a directory");
        });

        if (!OperatingSystem.IsWindows())
        {
            WithTemporaryDirectory(root =>
            {
                Directory.CreateDirectory(Path.Combine(root, "Tests"));
                AssertFailure(LastStandFixtureCheck.Write(root), "portable case alias");
            });
        }

        WithTemporaryDirectory(root =>
        {
            for (var index = 0; index <= FixedCanonicalFixtureFile.MaximumSiblingEntries; index++)
            {
                File.WriteAllText(
                    Path.Combine(root, $"entry-{index:D3}"),
                    string.Empty,
                    new UTF8Encoding(false));
            }

            AssertFailure(LastStandFixtureCheck.Write(root), "exceeds 256 entries");
        });

        WithTemporaryDirectory(root =>
        {
            for (var index = 0; index < FixedCanonicalFixtureFile.MaximumSiblingEntries; index++)
            {
                File.WriteAllText(
                    Path.Combine(root, $"entry-{index:D3}"),
                    string.Empty,
                    new UTF8Encoding(false));
            }

            var before = Directory.EnumerateFileSystemEntries(root)
                .Select(Path.GetFileName)
                .Order(StringComparer.Ordinal)
                .ToArray();
            AssertFailure(LastStandFixtureCheck.Write(root), "exceeds 256 entries");
            Assert.False(Directory.Exists(Path.Combine(root, "tests")));
            Assert.Equal(
                before,
                Directory.EnumerateFileSystemEntries(root)
                    .Select(Path.GetFileName)
                    .Order(StringComparer.Ordinal));
        });

        WithTemporaryDirectory(root =>
        {
            var parent = Path.GetDirectoryName(FixturePath(root))!;
            Directory.CreateDirectory(parent);
            for (var index = 0; index < FixedCanonicalFixtureFile.MaximumSiblingEntries; index++)
            {
                File.WriteAllText(
                    Path.Combine(parent, $"entry-{index:D3}"),
                    string.Empty,
                    new UTF8Encoding(false));
            }

            var before = Directory.EnumerateFileSystemEntries(parent)
                .Select(Path.GetFileName)
                .Order(StringComparer.Ordinal)
                .ToArray();
            AssertFailure(LastStandFixtureCheck.Write(root), "exceeds 256 entries");
            Assert.False(File.Exists(FixturePath(root)));
            Assert.Empty(Directory.EnumerateFiles(parent, "*.tmp-*"));
            Assert.Equal(
                before,
                Directory.EnumerateFileSystemEntries(parent)
                    .Select(Path.GetFileName)
                    .Order(StringComparer.Ordinal));
        });
    }

    [Fact]
    public void Failed_atomic_replacement_preserves_output_and_cleans_private_temporary_file()
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
                var result = LastStandFixtureCheck.Write(root);
                Assert.False(result.Passed);
                Assert.Single(result.Failures);
            }

            Assert.Empty(Directory.EnumerateFiles(
                Path.GetDirectoryName(FixturePath(root))!,
                "*.tmp-*"));
            Assert.Equal(CanonicalBytes(), File.ReadAllBytes(FixturePath(root)));
        });
    }

    [Fact]
    public void Symbolic_linked_root_parent_and_output_are_rejected_without_touching_external_files()
    {
        WithTemporaryDirectory(container =>
        WithTemporaryDirectory(external =>
        {
            var sentinel = Path.Combine(external, "sentinel.txt");
            File.WriteAllText(sentinel, "preserve\n", new UTF8Encoding(false));
            var linkedRoot = Path.Combine(container, "linked-root");
            try
            {
                Directory.CreateSymbolicLink(linkedRoot, external);
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
            {
                return;
            }

            AssertFailure(LastStandFixtureCheck.Write(linkedRoot), "must not be a link");
            Assert.Equal("preserve\n", File.ReadAllText(sentinel));
        }));

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

            AssertFailure(LastStandFixtureCheck.Write(root), "must not be a link");
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

            AssertFailure(LastStandFixtureCheck.Write(root), "must not be a link");
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
                RepositoryCheckCommand.Run(["last-stand", root], output, error));
            Assert.Contains("verified", output.ToString(), StringComparison.Ordinal);
            Assert.Equal(string.Empty, error.ToString());

            File.WriteAllText(FixturePath(root), "stale\n", new UTF8Encoding(false));
            output = new StringWriter();
            error = new StringWriter();
            Assert.Equal(
                1,
                RepositoryCheckCommand.Run(["last-stand", root], output, error));
            Assert.Contains("stale or noncanonical", error.ToString(), StringComparison.Ordinal);

            output = new StringWriter();
            error = new StringWriter();
            Assert.Equal(
                0,
                RepositoryCheckCommand.Run(["last-stand-write", root], output, error));
            Assert.Contains("written", output.ToString(), StringComparison.Ordinal);
            Assert.Equal(CanonicalBytes(), File.ReadAllBytes(FixturePath(root)));

            output = new StringWriter();
            error = new StringWriter();
            Assert.Equal(
                2,
                RepositoryCheckCommand.Run(
                    ["last-stand-write", root, "extra"],
                    output,
                    error));
            Assert.Contains("last-stand-write", error.ToString(), StringComparison.Ordinal);
        });
    }

    private static byte[] CanonicalBytes() => LastStandFixtureCheck.BuildFixtureBytes();

    private static string FixturePath(string root) => Path.Combine(
        root,
        LastStandFixtureCheck.FixtureRelativePath.Replace(
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

    private static string[] EventKinds(JsonElement traceCase) =>
        traceCase.GetProperty("expected").GetProperty("events")
            .EnumerateArray()
            .Select(item => item.GetProperty("kind").GetString()!)
            .ToArray();

    private static int RecoveryTicks(JsonElement traceCase) =>
        traceCase.GetProperty("expected").GetProperty("recovery_ticks_remaining").GetInt32();

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
            "vibesnake-last-stand-fixture-tests",
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
