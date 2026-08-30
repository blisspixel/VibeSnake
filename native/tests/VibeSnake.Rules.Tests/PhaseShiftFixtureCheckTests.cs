using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using RepositoryChecks;

namespace VibeSnake.Rules.Tests;

public sealed class PhaseShiftFixtureCheckTests
{
    private const string ExpectedSha256 =
        "1ebe90414c4556d5e6708792822771772c14f041d54fd0d8095feadbc4affd32";

    private static readonly JsonSerializerOptions IndentedJson = new()
    {
        WriteIndented = true,
    };

    [Fact]
    public void Renderer_reproduces_the_reviewed_python_origin_bytes_exactly()
    {
        var first = PhaseShiftFixtureCheck.BuildFixtureBytes();
        var second = PhaseShiftFixtureCheck.BuildFixtureBytes();
        var checkedIn = File.ReadAllBytes(Path.Combine(
            ResolveRepositoryRoot(),
            PhaseShiftFixtureCheck.FixtureRelativePath.Replace(
                '/',
                Path.DirectorySeparatorChar)));

        Assert.Equal(3_534, first.Length);
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
        using var document = JsonDocument.Parse(PhaseShiftFixtureCheck.BuildFixtureBytes());
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
        Assert.Equal("phase-shift-rules-targeted-v1", root.GetProperty("contract").GetString());
        Assert.Equal("python-production-phase-shift-v1", root.GetProperty("source_engine").GetString());
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
                "pickup_expiry",
                "effect_expiry",
                "self_collision_phasing",
                "body_overlap",
                "starvation_bypass",
                "ordered_power_events",
            ],
            Strings(root.GetProperty("comparison_scope")));
        Assert.Equal(
            [
                "random_spawn_position",
                "spawn_schedule",
                "presentation_feedback",
                "detached_obstacles",
                "other_power_types",
            ],
            Strings(root.GetProperty("excluded_scope")));

        var config = root.GetProperty("config");
        Assert.Equal(
            [
                "height",
                "phase_shift_duration_ticks",
                "power_visible_ticks",
                "starvation_ticks",
                "width",
            ],
            PropertyNames(config));
        Assert.Equal(33, config.GetProperty("height").GetInt32());
        Assert.Equal(100, config.GetProperty("phase_shift_duration_ticks").GetInt32());
        Assert.Equal(120, config.GetProperty("power_visible_ticks").GetInt32());
        Assert.Equal(600, config.GetProperty("starvation_ticks").GetInt32());
        Assert.Equal(64, config.GetProperty("width").GetInt32());

        var cases = root.GetProperty("cases").EnumerateArray().ToArray();
        Assert.Equal(6, root.GetProperty("case_count").GetInt32());
        Assert.Equal(
            [
                "phase-shift-collect-on-entry",
                "phase-shift-pickup-expiry",
                "phase-shift-active-countdown",
                "phase-shift-active-expiry-before-collision",
                "phase-shift-body-overlap",
                "phase-shift-does-not-block-starvation",
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
                    "phase_shift_ticks_remaining",
                    "pickup",
                    "starvation_ticks_elapsed",
                    "tick",
                ],
                PropertyNames(item.GetProperty("expected")));
            Assert.Equal(
                [
                    "body",
                    "direction",
                    "food",
                    "phase_shift_ticks_remaining",
                    "pickup",
                    "starvation_ticks_elapsed",
                ],
                PropertyNames(item.GetProperty("initial")));
            Assert.Equal(JsonValueKind.Null, item.GetProperty("expected").GetProperty("pickup").ValueKind);
            Assert.Equal(1, item.GetProperty("expected").GetProperty("tick").GetInt32());
        });

        var pickup = cases[0].GetProperty("initial").GetProperty("pickup");
        Assert.Equal(["kind", "position", "visibility_ticks_remaining"], PropertyNames(pickup));
        Assert.Equal("phase_shift", pickup.GetProperty("kind").GetString());
        Assert.Equal([6, 5], Ints(pickup.GetProperty("position")));
        Assert.Equal(10, pickup.GetProperty("visibility_ticks_remaining").GetInt32());
        Assert.Equal(
            ["moved", "power_collected", "power_activated"],
            EventKinds(cases[0]));
        Assert.Equal(100, PhaseShiftTicks(cases[0]));
        Assert.Equal(
            ["kind", "power", "value"],
            PropertyNames(cases[0].GetProperty("expected").GetProperty("events")[2]));

        Assert.Equal(["power_expired", "moved"], EventKinds(cases[1]));
        Assert.Equal(
            ["kind", "position", "power"],
            PropertyNames(cases[1].GetProperty("expected").GetProperty("events")[0]));
        Assert.Equal(0, PhaseShiftTicks(cases[1]));

        Assert.Equal(["moved"], EventKinds(cases[2]));
        Assert.Equal(1, PhaseShiftTicks(cases[2]));

        Assert.Equal(["power_expired", "died"], EventKinds(cases[3]));
        Assert.False(cases[3].GetProperty("expected").GetProperty("alive").GetBoolean());
        Assert.Equal("self_collision", DeathCause(cases[3]));
        Assert.Equal(
            [[1, 1], [1, 2], [2, 2], [2, 1]],
            Points(cases[3].GetProperty("expected").GetProperty("body")));
        Assert.Equal([2, 1], Ints(cases[3].GetProperty("expected").GetProperty("head")));
        Assert.Equal(0, PhaseShiftTicks(cases[3]));
        Assert.Equal(
            ["death_cause", "kind", "position"],
            PropertyNames(cases[3].GetProperty("expected").GetProperty("events")[1]));

        Assert.Equal(["moved"], EventKinds(cases[4]));
        Assert.True(cases[4].GetProperty("expected").GetProperty("alive").GetBoolean());
        Assert.Equal(
            [[1, 2], [2, 2], [2, 1], [2, 2]],
            Points(cases[4].GetProperty("expected").GetProperty("body")));
        Assert.Equal([2, 2], Ints(cases[4].GetProperty("expected").GetProperty("head")));
        Assert.Equal(1, PhaseShiftTicks(cases[4]));

        Assert.Equal(["moved", "died"], EventKinds(cases[5]));
        Assert.False(cases[5].GetProperty("expected").GetProperty("alive").GetBoolean());
        Assert.Equal("starvation", DeathCause(cases[5]));
        Assert.Equal(1, PhaseShiftTicks(cases[5]));
        Assert.Equal(
            600,
            cases[5].GetProperty("expected").GetProperty("starvation_ticks_elapsed").GetInt32());
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
                        "{\"case_count\":6",
                        "{\"case_count\":6,\"case_count\":6",
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

                var result = PhaseShiftFixtureCheck.Inspect(root);

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
            var first = PhaseShiftFixtureCheck.Write(root);
            Assert.True(first.Passed, string.Join(Environment.NewLine, first.Failures));
            Assert.Equal(
                "Shared Phase Shift fixture written: cases=6 bytes=3534.",
                first.SuccessMessage);
            Assert.Equal(CanonicalBytes(), File.ReadAllBytes(FixturePath(root)));

            File.WriteAllText(FixturePath(root), "stale\n", new UTF8Encoding(false));
            var second = PhaseShiftFixtureCheck.Write(root);
            var third = PhaseShiftFixtureCheck.Write(root);
            var inspection = PhaseShiftFixtureCheck.Inspect(root);

            Assert.True(second.Passed, string.Join(Environment.NewLine, second.Failures));
            Assert.True(third.Passed, string.Join(Environment.NewLine, third.Failures));
            Assert.True(inspection.Passed, string.Join(Environment.NewLine, inspection.Failures));
            Assert.Equal(
                "Shared Phase Shift fixture verified: cases=6 bytes=3534.",
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
            AssertFailure(PhaseShiftFixtureCheck.Inspect(root), "parent is missing");
            AssertFailure(
                PhaseShiftFixtureCheck.Inspect(Path.Combine(root, "missing")),
                "repository root");
            Assert.False(PhaseShiftFixtureCheck.Inspect(" ").Passed);

            File.WriteAllText(Path.Combine(root, "tests"), "blocked", new UTF8Encoding(false));
            AssertFailure(PhaseShiftFixtureCheck.Write(root), "parent is not a directory");
            File.Delete(Path.Combine(root, "tests"));

            Directory.CreateDirectory(FixturePath(root));
            AssertFailure(PhaseShiftFixtureCheck.Write(root), "path is a directory");
        });

        if (!OperatingSystem.IsWindows())
        {
            WithTemporaryDirectory(root =>
            {
                Directory.CreateDirectory(Path.Combine(root, "Tests"));
                AssertFailure(PhaseShiftFixtureCheck.Write(root), "portable case alias");
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

            AssertFailure(PhaseShiftFixtureCheck.Write(root), "must not be a link");
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
                RepositoryCheckCommand.Run(["phase-shift", root], output, error));
            Assert.Contains("verified", output.ToString(), StringComparison.Ordinal);
            Assert.Equal(string.Empty, error.ToString());

            File.WriteAllText(FixturePath(root), "stale\n", new UTF8Encoding(false));
            output = new StringWriter();
            error = new StringWriter();
            Assert.Equal(
                1,
                RepositoryCheckCommand.Run(["phase-shift", root], output, error));
            Assert.Contains("stale or noncanonical", error.ToString(), StringComparison.Ordinal);

            output = new StringWriter();
            error = new StringWriter();
            Assert.Equal(
                0,
                RepositoryCheckCommand.Run(["phase-shift-write", root], output, error));
            Assert.Contains("written", output.ToString(), StringComparison.Ordinal);
            Assert.Equal(CanonicalBytes(), File.ReadAllBytes(FixturePath(root)));

            output = new StringWriter();
            error = new StringWriter();
            Assert.Equal(
                2,
                RepositoryCheckCommand.Run(
                    ["phase-shift-write", root, "extra"],
                    output,
                    error));
            Assert.Contains("phase-shift-write", error.ToString(), StringComparison.Ordinal);
        });
    }

    private static byte[] CanonicalBytes() => PhaseShiftFixtureCheck.BuildFixtureBytes();

    private static string FixturePath(string root) => Path.Combine(
        root,
        PhaseShiftFixtureCheck.FixtureRelativePath.Replace(
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

    private static int PhaseShiftTicks(JsonElement traceCase) =>
        traceCase.GetProperty("expected").GetProperty("phase_shift_ticks_remaining").GetInt32();

    private static string? DeathCause(JsonElement traceCase) =>
        traceCase.GetProperty("expected").GetProperty("death_cause").GetString();

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
            "vibesnake-phase-shift-fixture-tests",
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
