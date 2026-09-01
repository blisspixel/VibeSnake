using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using RepositoryChecks;

namespace VibeSnake.Rules.Tests;

public sealed class MovementFixtureCheckTests
{
    private const string ExpectedSha256 =
        "43f3861f6a20c39ae5d2d439d0071b855f751478d34c30eafa4c7ae968f060d4";

    [Fact]
    public void Renderer_reproduces_the_reviewed_python_origin_bytes_exactly()
    {
        var first = MovementFixtureCheck.BuildFixtureBytes();
        var second = MovementFixtureCheck.BuildFixtureBytes();
        var checkedIn = File.ReadAllBytes(Path.Combine(
            ResolveRepositoryRoot(),
            MovementFixtureCheck.FixtureRelativePath.Replace(
                '/',
                Path.DirectorySeparatorChar)));

        Assert.Equal(999_087, first.Length);
        Assert.Equal(ExpectedSha256, Sha256(first));
        Assert.Equal(first, second);
        Assert.Equal(checkedIn, first);
        Assert.Equal((byte)'\n', first[^1]);
        Assert.DoesNotContain((byte)'\r', first);
        Assert.False(first[0] == 0xef && first[1] == 0xbb && first[2] == 0xbf);
    }

    [Fact]
    public void Frozen_contract_metadata_rng_queue_and_movement_are_closed()
    {
        using var document = JsonDocument.Parse(MovementFixtureCheck.BuildFixtureBytes());
        var root = document.RootElement;

        Assert.Equal(
            [
                "case_count",
                "cases",
                "comparison_scope",
                "contract",
                "direction_symbols",
                "excluded_scope",
                "grid",
                "randomness_policy",
                "ruleset",
                "schema_version",
                "source_engine",
                "step_encoding",
                "steps_per_case",
                "total_steps",
            ],
            PropertyNames(root));
        Assert.Equal(2, root.GetProperty("schema_version").GetInt32());
        Assert.Equal("movement-input-long-v2", root.GetProperty("contract").GetString());
        Assert.Equal("python-production-snake-v2", root.GetProperty("source_engine").GetString());
        Assert.Equal(
            "positions-injected-or-random-output-normalized-v2",
            root.GetProperty("randomness_policy").GetString());
        Assert.Equal(["id", "version"], PropertyNames(root.GetProperty("ruleset")));
        Assert.Equal("vibesnake-core", root.GetProperty("ruleset").GetProperty("id").GetString());
        Assert.Equal(4, root.GetProperty("ruleset").GetProperty("version").GetInt32());
        Assert.Equal(["height", "width"], PropertyNames(root.GetProperty("grid")));
        Assert.Equal(33, root.GetProperty("grid").GetProperty("height").GetInt32());
        Assert.Equal(64, root.GetProperty("grid").GetProperty("width").GetInt32());
        Assert.Equal(
            ["DOWN", "LEFT", "RIGHT", "UP"],
            PropertyNames(root.GetProperty("direction_symbols")));
        Assert.Equal(
            ["D", "L", "R", "U"],
            root.GetProperty("direction_symbols")
                .EnumerateObject()
                .Select(property => property.Value.GetString()));
        Assert.Equal(
            [
                "command_symbols",
                "command_acceptance_bits",
                "direction_symbol",
                "head_x",
                "head_y",
                "body_length",
                "pending_direction_symbols",
                "wrapped",
                "alive",
            ],
            Strings(root.GetProperty("step_encoding")));
        Assert.Equal(
            [
                "bounded_direction_queue",
                "command_acceptance",
                "duplicate_rejection",
                "reversal_rejection",
                "overflow_rejection",
                "direction_consumption",
                "head_position",
                "body_length",
                "edge_wrapping",
                "survival",
            ],
            Strings(root.GetProperty("comparison_scope")));
        Assert.Equal(
            ["food", "growth", "score", "combo", "starvation", "collision", "random_stream"],
            Strings(root.GetProperty("excluded_scope")));

        var cases = root.GetProperty("cases").EnumerateArray().ToArray();
        Assert.Equal(100, root.GetProperty("case_count").GetInt32());
        Assert.Equal(256, root.GetProperty("steps_per_case").GetInt32());
        Assert.Equal(25_600, root.GetProperty("total_steps").GetInt32());
        Assert.Equal(100, cases.Length);
        Assert.Equal(
            Enumerable.Range(0, 100).Select(seed => $"movement-seed-{seed:000}"),
            cases.Select(traceCase => traceCase.GetProperty("id").GetString()));
        Assert.Equal(
            Enumerable.Range(0, 100),
            cases.Select(traceCase => traceCase.GetProperty("seed").GetInt32()));

        var steps = new List<JsonElement>(25_600);
        foreach (var traceCase in cases)
        {
            Assert.Equal(["id", "initial", "seed", "steps"], PropertyNames(traceCase));
            var initial = traceCase.GetProperty("initial");
            Assert.Equal(["body", "direction"], PropertyNames(initial));
            Assert.Equal("RIGHT", initial.GetProperty("direction").GetString());
            Assert.Equal(
                [32, 16],
                initial.GetProperty("body")[0]
                    .EnumerateArray()
                    .Select(value => value.GetInt32()));

            var traceSteps = traceCase.GetProperty("steps").EnumerateArray().ToArray();
            Assert.Equal(256, traceSteps.Length);
            Assert.All(traceSteps.Take(40), step =>
            {
                Assert.Equal(string.Empty, step[0].GetString());
                Assert.Equal(string.Empty, step[1].GetString());
            });
            steps.AddRange(traceSteps);
        }

        Assert.All(steps, step =>
        {
            Assert.Equal(9, step.GetArrayLength());
            Assert.Equal(step[0].GetString()!.Length, step[1].GetString()!.Length);
            Assert.Equal(1, step[5].GetInt32());
            Assert.InRange(step[6].GetString()!.Length, 0, 2);
            Assert.True(step[8].GetBoolean());
        });
        Assert.True(steps.Count(step => step[7].GetBoolean()) >= 100);
        Assert.Equal(3, steps.Max(step => step[1].GetString()!.Count(value => value == '1')));
        Assert.True(steps.Count(step => step[1].GetString()!.Contains("1110", StringComparison.Ordinal)) > 100);
        Assert.True(steps.Sum(step => step[0].GetString()!.Length) > 10_000);
        Assert.True(steps.Sum(step => step[1].GetString()!.Count(value => value == '0')) > 5_000);

        var firstRandomStep = cases[0].GetProperty("steps")[40];
        Assert.Equal("RURRD", firstRandomStep[0].GetString());
        Assert.Equal("01101", firstRandomStep[1].GetString());
        Assert.Equal("U", firstRandomStep[2].GetString());
        Assert.Equal([8, 15], new[] { firstRandomStep[3].GetInt32(), firstRandomStep[4].GetInt32() });
        Assert.Equal("RD", firstRandomStep[6].GetString());
    }

    [Fact]
    public void Renderer_rejects_empty_or_unbounded_corpora()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            MovementFixtureCheck.BuildFixtureBytes(0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            MovementFixtureCheck.BuildFixtureBytes(1, 0));
        Assert.Throws<OverflowException>(() =>
            MovementFixtureCheck.BuildFixtureBytes(int.MaxValue, 2));
        var oversized = Assert.Throws<InvalidDataException>(() =>
            MovementFixtureCheck.BuildFixtureBytes(101, 256));
        Assert.Contains("exceeds 1000000 bytes", oversized.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Inspect_accepts_only_the_exact_large_fixture_bytes()
    {
        WithTemporaryDirectory(root =>
        {
            var variants = new Action<string>[]
            {
                path => File.Delete(path),
                path =>
                {
                    var bytes = File.ReadAllBytes(path);
                    bytes[500_000] ^= 1;
                    File.WriteAllBytes(path, bytes);
                },
                path => File.WriteAllBytes(path, [0xef, 0xbb, 0xbf, .. CanonicalBytes()]),
                path => File.WriteAllBytes(path, CanonicalBytes()[..^1]),
                path => File.AppendAllText(path, "\n", new UTF8Encoding(false)),
                path => File.WriteAllBytes(path, new byte[LargeCanonicalFixtureFile.MaximumBytes + 1]),
            };

            foreach (var mutate in variants)
            {
                WriteCanonicalFixture(root);
                var path = FixturePath(root);
                mutate(path);
                var before = File.Exists(path) ? File.ReadAllBytes(path) : null;

                var result = MovementFixtureCheck.Inspect(root);

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
            var first = MovementFixtureCheck.Write(root);
            Assert.True(first.Passed, string.Join(Environment.NewLine, first.Failures));
            Assert.Equal(
                "Shared Movement fixture written: cases=100 steps=25600 bytes=999087.",
                first.SuccessMessage);
            Assert.Equal(CanonicalBytes(), File.ReadAllBytes(FixturePath(root)));

            File.WriteAllText(FixturePath(root), "stale\n", new UTF8Encoding(false));
            var second = MovementFixtureCheck.Write(root);
            var third = MovementFixtureCheck.Write(root);
            var inspection = MovementFixtureCheck.Inspect(root);

            Assert.True(second.Passed, string.Join(Environment.NewLine, second.Failures));
            Assert.True(third.Passed, string.Join(Environment.NewLine, third.Failures));
            Assert.True(inspection.Passed, string.Join(Environment.NewLine, inspection.Failures));
            Assert.Equal(
                "Shared Movement fixture verified: cases=100 steps=25600 bytes=999087.",
                inspection.SuccessMessage);
            Assert.Equal(ExpectedSha256, Sha256(File.ReadAllBytes(FixturePath(root))));
            Assert.Empty(Directory.EnumerateFiles(
                Path.GetDirectoryName(FixturePath(root))!,
                "*.tmp-*"));
        });
    }

    [Fact]
    public void Large_file_lifecycle_has_an_independent_exact_capacity_contract()
    {
        WithTemporaryDirectory(root =>
        {
            const string relativePath = "fixtures/large.json";
            var exactLimit = CanonicalFixtureJson.Render(
                "large fixture",
                LargeCanonicalFixtureFile.MaximumBytes,
                writer => writer.WriteStringValue(new string(
                    'x',
                    LargeCanonicalFixtureFile.MaximumBytes - 3)));
            Assert.Equal(LargeCanonicalFixtureFile.MaximumBytes, exactLimit.Length);

            LargeCanonicalFixtureFile.Write(
                root,
                relativePath,
                "large fixture",
                exactLimit);
            Assert.Equal(
                exactLimit,
                LargeCanonicalFixtureFile.Read(root, relativePath, "large fixture"));

            Assert.Throws<InvalidDataException>(() =>
                LargeCanonicalFixtureFile.Write(
                    root,
                    relativePath,
                    "large fixture",
                    new byte[LargeCanonicalFixtureFile.MaximumBytes + 1]));
            Assert.Throws<InvalidDataException>(() =>
                CanonicalFixtureJson.Render(
                    "large fixture",
                    LargeCanonicalFixtureFile.MaximumBytes,
                    writer => writer.WriteStringValue(new string(
                        'x',
                        LargeCanonicalFixtureFile.MaximumBytes - 2))));

            File.WriteAllBytes(
                Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)),
                new byte[LargeCanonicalFixtureFile.MaximumBytes + 1]);
            Assert.Throws<InvalidDataException>(() =>
                LargeCanonicalFixtureFile.Read(root, relativePath, "large fixture"));

            Assert.Throws<ArgumentOutOfRangeException>(() =>
                CanonicalFixtureJson.Render("invalid fixture", 0, _ => { }));
        });
    }

    [Fact]
    public void Large_fixed_path_rejects_invalid_roots_parent_files_directories_and_links()
    {
        WithTemporaryDirectory(root =>
        {
            AssertFailure(MovementFixtureCheck.Inspect(root), "parent is missing");
            AssertFailure(
                MovementFixtureCheck.Inspect(Path.Combine(root, "missing")),
                "repository root");
            Assert.False(MovementFixtureCheck.Inspect(" ").Passed);

            File.WriteAllText(Path.Combine(root, "tests"), "blocked", new UTF8Encoding(false));
            AssertFailure(MovementFixtureCheck.Write(root), "parent is not a directory");
            File.Delete(Path.Combine(root, "tests"));

            Directory.CreateDirectory(FixturePath(root));
            AssertFailure(MovementFixtureCheck.Write(root), "path is a directory");
        });

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

            AssertFailure(MovementFixtureCheck.Write(root), "must not be a link");
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
            Assert.Equal(0, RepositoryCheckCommand.Run(["movement", root], output, error));
            Assert.Contains("verified", output.ToString(), StringComparison.Ordinal);
            Assert.Equal(string.Empty, error.ToString());

            File.WriteAllText(FixturePath(root), "stale\n", new UTF8Encoding(false));
            output = new StringWriter();
            error = new StringWriter();
            Assert.Equal(1, RepositoryCheckCommand.Run(["movement", root], output, error));
            Assert.Contains("stale or noncanonical", error.ToString(), StringComparison.Ordinal);

            output = new StringWriter();
            error = new StringWriter();
            Assert.Equal(
                0,
                RepositoryCheckCommand.Run(["movement-write", root], output, error));
            Assert.Contains("written", output.ToString(), StringComparison.Ordinal);
            Assert.Equal(CanonicalBytes(), File.ReadAllBytes(FixturePath(root)));

            output = new StringWriter();
            error = new StringWriter();
            Assert.Equal(
                2,
                RepositoryCheckCommand.Run(["movement-write", root, "extra"], output, error));
            Assert.Contains("movement-write", error.ToString(), StringComparison.Ordinal);
        });
    }

    private static byte[] CanonicalBytes() => MovementFixtureCheck.BuildFixtureBytes();

    private static string FixturePath(string root) => Path.Combine(
        root,
        MovementFixtureCheck.FixtureRelativePath.Replace(
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

    private static string Sha256(byte[] bytes) =>
        Convert.ToHexStringLower(SHA256.HashData(bytes));

    private static void AssertFailure(RepositoryCheckResult result, string expected)
    {
        Assert.False(result.Passed);
        Assert.Contains(
            result.Failures,
            failure => failure.Contains(expected, StringComparison.Ordinal));
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
            "vibesnake-movement-fixture-tests",
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
