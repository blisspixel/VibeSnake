using System.Security.Cryptography;
using System.Text;
using RepositoryChecks;

namespace VibeSnake.Rules.Tests;

public sealed class AgentKnowledgeCheckTests
{
    private static readonly IReadOnlyDictionary<string, string> ExpectedSha256 =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["experience.md"] = "c25ed13615eb700f3c9a426988dfb7100841c6cbe8b78a0acdd1989e6787aa74",
            ["index.md"] = "7c728d11b8c9d671659112bd5b696ddfbe6929e3ef6397e6999aaaf1d69dfded",
            ["protocol.md"] = "769ac56cdd053f0b92817fd61481d1ee321c67d53509ba2dae375f9b0d5c0cd4",
            ["replays.md"] = "66f09be8e8154e16464a6ebf6b9a774cf4c5fd2f39d5fb56a3c21a35245ba7e8",
            ["rules.md"] = "4beacf6fcec623819edd86a02acdabc666e0360c79fda2832dd467e16d83c25d",
        };

    [Fact]
    public void Renderer_reproduces_the_reviewed_bundle_and_current_live_contracts_exactly()
    {
        var root = AgentKnowledgeTestRepository.ResolveRepositoryRoot();
        var first = AgentKnowledgeCheck.RenderBundle(root);
        var second = AgentKnowledgeCheck.RenderBundle(root);

        Assert.Equal(ExpectedSha256.Keys.Order(StringComparer.Ordinal), first.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(14_687, first.Values.Sum(bytes => bytes.Length));
        foreach (var (name, expectedHash) in ExpectedSha256)
        {
            var bytes = first[name];
            var path = Path.Combine(
                root,
                AgentKnowledgeCheck.OutputDirectory.Replace('/', Path.DirectorySeparatorChar),
                name);
            Assert.Equal(expectedHash, Convert.ToHexStringLower(SHA256.HashData(bytes)));
            Assert.Equal(bytes, second[name]);
            Assert.Equal(File.ReadAllBytes(path), bytes);
            Assert.Equal((byte)'\n', bytes[^1]);
            Assert.DoesNotContain((byte)'\r', bytes);
            Assert.False(bytes.Length >= 3 && bytes[0] == 0xef && bytes[1] == 0xbb && bytes[2] == 0xbf);
        }

        var protocol = StrictText(first["protocol.md"]);
        Assert.Contains("`vibesnake-agent-viewer-frame-v9`", protocol, StringComparison.Ordinal);
        Assert.Contains("`vibesnake-agent-survival-state-v1`", protocol, StringComparison.Ordinal);
        Assert.DoesNotContain("viewer-frame-v7", protocol, StringComparison.Ordinal);
        Assert.Equal(17, SectionBulletCount(protocol, "# Tools", "# Resources"));
        Assert.Equal(8, SectionBulletCount(protocol, "# Resources", "# Live viewer"));

        var experience = StrictText(first["experience.md"]);
        Assert.Equal(5, SectionBulletCount(experience, "# Style Contracts", "Each style"));
        Assert.Equal(8, SectionBulletCount(experience, "# Signal School", "Call `start_lesson`"));
        Assert.Contains("The rules authority is `vibesnake-core@4`", StrictText(first["rules.md"]));
    }

    [Fact]
    public void Inspect_accepts_only_the_exact_closed_bundle_without_mutation()
    {
        AgentKnowledgeTestRepository.WithTemporaryRepository(root =>
        {
            WriteBundle(root);
            var before = SnapshotOutput(root);

            var result = AgentKnowledgeCheck.Inspect(root, new DateOnly(2026, 11, 13));

            Assert.True(result.Passed, string.Join(Environment.NewLine, result.Failures));
            Assert.Equal(
                "Agent knowledge verified: concepts=5 bytes=14687.",
                result.SuccessMessage);
            var after = SnapshotOutput(root);
            foreach (var name in ExpectedSha256.Keys)
            {
                Assert.Equal(before[name], after[name]);
            }
        });
    }

    [Fact]
    public void Inspect_reports_stale_missing_extra_and_case_aliased_outputs()
    {
        AgentKnowledgeTestRepository.WithTemporaryRepository(root =>
        {
            WriteBundle(root);
            File.WriteAllText(OutputPath(root, "rules.md"), "stale\n", new UTF8Encoding(false));
            var stale = AgentKnowledgeCheck.Inspect(root, new DateOnly(2026, 9, 1));
            AssertFailure(stale, "generated file is stale: rules.md");

            WriteBundle(root);
            File.Delete(OutputPath(root, "replays.md"));
            AssertFailure(
                AgentKnowledgeCheck.Inspect(root, new DateOnly(2026, 9, 1)),
                "required fixture is missing");

            WriteBundle(root);
            File.WriteAllText(OutputPath(root, "extra.md"), "extra\n", new UTF8Encoding(false));
            AssertFailure(
                AgentKnowledgeCheck.Inspect(root, new DateOnly(2026, 9, 1)),
                "unexpected generated concept: extra.md");

            File.Delete(OutputPath(root, "extra.md"));
            File.Move(OutputPath(root, "rules.md"), OutputPath(root, "RULES.md"));
            AssertFailure(
                AgentKnowledgeCheck.Inspect(root, new DateOnly(2026, 9, 1)),
                "portable case alias");
        });
    }

    [Fact]
    public void Freshness_fails_closed_on_the_declared_absolute_date()
    {
        AgentKnowledgeTestRepository.WithTemporaryRepository(root =>
        {
            WriteBundle(root);
            Assert.True(AgentKnowledgeCheck.Inspect(root, new DateOnly(2026, 11, 13)).Passed);

            var boundary = AgentKnowledgeCheck.Inspect(root, new DateOnly(2026, 11, 14));

            AssertFailure(
                boundary,
                "agent knowledge is stale: as-of 2026-11-14 reached stale_after 2026-11-14");
        });
    }

    [Fact]
    public void Baseline_and_plugin_inputs_are_strict_bounded_and_cross_checked()
    {
        AgentKnowledgeTestRepository.WithTemporaryRepository(root =>
        {
            WriteBundle(root);
            var baselinePath = InputPath(root, "integrations/agent-interop-baseline.json");
            var baseline = File.ReadAllText(baselinePath, Encoding.UTF8);

            File.WriteAllText(
                baselinePath,
                baseline.Replace(
                    "\"okf\": {",
                    "\"okf\": {},\n  \"okf\": {",
                    StringComparison.Ordinal),
                new UTF8Encoding(false));
            AssertFailure(
                AgentKnowledgeCheck.Inspect(root, new DateOnly(2026, 9, 1)),
                "duplicate property okf");

            File.WriteAllText(
                baselinePath,
                baseline.Replace("2026-11-14", "2026-11-14T00:00:00Z", StringComparison.Ordinal),
                new UTF8Encoding(false));
            AssertFailure(
                AgentKnowledgeCheck.Inspect(root, new DateOnly(2026, 9, 1)),
                "okf.stale_after must be an absolute YYYY-MM-DD date");

            File.WriteAllText(
                baselinePath,
                baseline.Replace("\"host_version\": \"0.18.0\"", "\"host_version\": \"9.9.9\"", StringComparison.Ordinal),
                new UTF8Encoding(false));
            AssertFailure(
                AgentKnowledgeCheck.Inspect(root, new DateOnly(2026, 9, 1)),
                "host source version 0.18.0 does not match baseline 9.9.9");

            File.WriteAllBytes(baselinePath, [0xff]);
            Assert.False(AgentKnowledgeCheck.Inspect(root, new DateOnly(2026, 9, 1)).Passed);

            File.WriteAllBytes(baselinePath, new byte[FixedCanonicalFixtureFile.MaximumBytes + 1]);
            AssertFailure(
                AgentKnowledgeCheck.Inspect(root, new DateOnly(2026, 9, 1)),
                "exceeds 65536 bytes");
        });
    }

    [Fact]
    public void Canonical_source_extraction_rejects_missing_duplicate_and_unknown_catalog_contracts()
    {
        AgentKnowledgeTestRepository.WithTemporaryRepository(root =>
        {
            WriteBundle(root);
            var viewerPath = InputPath(root, "native/src/VibeSnake.AgentPlay/AgentViewer.cs");
            File.Delete(viewerPath);
            AssertFailure(
                AgentKnowledgeCheck.Inspect(root, new DateOnly(2026, 9, 1)),
                "required fixture is missing");
        });

        AgentKnowledgeTestRepository.WithTemporaryRepository(root =>
        {
            WriteBundle(root);
            var toolsPath = InputPath(root, "native/tools/VibeSnake.AgentHost/McpAgentTools.cs");
            File.AppendAllText(toolsPath, "\nName = \"start_match\";\n", new UTF8Encoding(false));
            AssertFailure(
                AgentKnowledgeCheck.Inspect(root, new DateOnly(2026, 9, 1)),
                "MCP tool names must not contain duplicates");
        });

        AgentKnowledgeTestRepository.WithTemporaryRepository(root =>
        {
            WriteBundle(root);
            var experiencePath = InputPath(root, "native/src/VibeSnake.AgentPlay/AgentExperience.cs");
            var source = File.ReadAllText(experiencePath, Encoding.UTF8);
            File.WriteAllText(
                experiencePath,
                source.Replace(
                    "public const string RedlineId = \"redline\";",
                    string.Empty,
                    StringComparison.Ordinal),
                new UTF8Encoding(false));
            AssertFailure(
                AgentKnowledgeCheck.Inspect(root, new DateOnly(2026, 9, 1)),
                "style IDs must contain exactly 5 entries");
        });
    }

    [Fact]
    public void Writer_atomically_repairs_each_concept_repeats_and_self_verifies()
    {
        AgentKnowledgeTestRepository.WithTemporaryRepository(root =>
        {
            var first = AgentKnowledgeCheck.Write(root);
            Assert.True(first.Passed, string.Join(Environment.NewLine, first.Failures));
            Assert.Equal("Agent knowledge written: concepts=5 bytes=14687.", first.SuccessMessage);

            File.WriteAllText(OutputPath(root, "protocol.md"), "stale\n", new UTF8Encoding(false));
            var second = AgentKnowledgeCheck.Write(root);
            var third = AgentKnowledgeCheck.Write(root);

            Assert.True(second.Passed, string.Join(Environment.NewLine, second.Failures));
            Assert.True(third.Passed, string.Join(Environment.NewLine, third.Failures));
            Assert.Equal(
                ExpectedSha256["protocol.md"],
                Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(OutputPath(root, "protocol.md")))));
            Assert.Empty(Directory.EnumerateFiles(
                Path.GetDirectoryName(OutputPath(root, "protocol.md"))!,
                "*.tmp-*"));
        });
    }

    [Fact]
    public void Output_and_source_links_are_rejected_without_following_them()
    {
        AgentKnowledgeTestRepository.WithTemporaryRepository(root =>
        AgentKnowledgeTestRepository.WithTemporaryRepository(external =>
        {
            WriteBundle(root);
            var output = OutputPath(root, "protocol.md");
            var sentinel = Path.Combine(external, "sentinel.md");
            File.WriteAllText(sentinel, "preserve\n", new UTF8Encoding(false));
            File.Delete(output);
            try
            {
                File.CreateSymbolicLink(output, sentinel);
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
            {
                return;
            }

            AssertFailure(
                AgentKnowledgeCheck.Write(root),
                "must not be a link");
            Assert.Equal("preserve\n", File.ReadAllText(sentinel, Encoding.UTF8));

            File.Delete(output);
            WriteBundle(root);
            var source = InputPath(root, "native/src/VibeSnake.AgentPlay/AgentViewer.cs");
            File.Delete(source);
            File.CreateSymbolicLink(source, sentinel);

            AssertFailure(
                AgentKnowledgeCheck.Inspect(root, new DateOnly(2026, 9, 1)),
                "must not be a link");
            Assert.Equal("preserve\n", File.ReadAllText(sentinel, Encoding.UTF8));
        }));
    }

    [Fact]
    public void Commands_cover_check_write_failure_and_usage()
    {
        AgentKnowledgeTestRepository.WithTemporaryRepository(root =>
        {
            WriteBundle(root);
            var output = new StringWriter();
            var error = new StringWriter();
            Assert.Equal(0, RepositoryCheckCommand.Run(["knowledge", root], output, error));
            Assert.Contains("verified", output.ToString(), StringComparison.Ordinal);
            Assert.Equal(string.Empty, error.ToString());

            File.WriteAllText(OutputPath(root, "index.md"), "stale\n", new UTF8Encoding(false));
            output = new StringWriter();
            error = new StringWriter();
            Assert.Equal(1, RepositoryCheckCommand.Run(["knowledge", root], output, error));
            Assert.Contains("generated file is stale", error.ToString(), StringComparison.Ordinal);

            output = new StringWriter();
            error = new StringWriter();
            Assert.Equal(0, RepositoryCheckCommand.Run(["knowledge-write", root], output, error));
            Assert.Contains("written", output.ToString(), StringComparison.Ordinal);

            output = new StringWriter();
            error = new StringWriter();
            Assert.Equal(
                2,
                RepositoryCheckCommand.Run(["knowledge-write", root, "extra"], output, error));
            Assert.Contains("knowledge-write", error.ToString(), StringComparison.Ordinal);
        });
    }

    private static int SectionBulletCount(string text, string start, string end)
    {
        var startIndex = text.IndexOf(start, StringComparison.Ordinal);
        var endIndex = text.IndexOf(end, startIndex + start.Length, StringComparison.Ordinal);
        Assert.True(startIndex >= 0 && endIndex > startIndex);
        return text[startIndex..endIndex].Split('\n').Count(line => line.StartsWith("* `", StringComparison.Ordinal));
    }

    private static string StrictText(byte[] bytes) => new UTF8Encoding(false, true).GetString(bytes);

    private static string InputPath(string root, string relativePath) =>
        Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));

    private static string OutputPath(string root, string name) =>
        Path.Combine(
            root,
            AgentKnowledgeCheck.OutputDirectory.Replace('/', Path.DirectorySeparatorChar),
            name);

    private static Dictionary<string, byte[]> SnapshotOutput(string root) =>
        ExpectedSha256.Keys.ToDictionary(
            name => name,
            name => File.ReadAllBytes(OutputPath(root, name)),
            StringComparer.Ordinal);

    private static void WriteBundle(string root)
    {
        var result = AgentKnowledgeCheck.Write(root);
        Assert.True(result.Passed, string.Join(Environment.NewLine, result.Failures));
    }

    private static void AssertFailure(RepositoryCheckResult result, string expected)
    {
        Assert.False(result.Passed);
        Assert.Contains(
            result.Failures,
            failure => failure.Contains(expected, StringComparison.Ordinal));
    }
}

internal static class AgentKnowledgeTestRepository
{
    private static readonly string[] InputFiles =
    [
        "integrations/agent-interop-baseline.json",
        "integrations/vibesnake-agent-plugin/plugin.json",
        "native/src/VibeSnake.Rules/RulesetIdentity.cs",
        "native/src/VibeSnake.AgentPlay/AgentContracts.cs",
        "native/src/VibeSnake.AgentPlay/AgentExperience.cs",
        "native/src/VibeSnake.AgentPlay/AgentLessonEvidence.cs",
        "native/src/VibeSnake.AgentPlay/AgentViewer.cs",
        "native/tools/VibeSnake.AgentHost/McpAgentTools.cs",
        "native/tools/VibeSnake.AgentHost/AgentResources.cs",
        "native/tools/VibeSnake.AgentHost/Program.cs",
        "native/tools/VibeSnake.AgentHost/VibeSnake.AgentHost.csproj",
    ];

    private static readonly string[] ReferencedOnlyFiles =
    [
        "native/src/VibeSnake.AgentPlay/AgentIdentity.cs",
        "native/src/VibeSnake.AgentPlay/AgentMatchSession.cs",
        "native/src/VibeSnake.AgentPlay/AgentStyleEvidence.cs",
        "native/src/VibeSnake.AgentViewer/AgentViewerClient.cs",
        "native/src/VibeSnake.Persistence/ReplayStore.cs",
        "native/src/VibeSnake.Rules/RunModeCatalog.cs",
        "native/src/VibeSnake.Rules/StationIdentityCatalog.cs",
        "docs/design/AGENT_ARENA.md",
        "docs/engineering/REPLAYS.md",
    ];

    internal static void CopyInputs(string targetRoot)
    {
        var sourceRoot = ResolveRepositoryRoot();
        foreach (var relativePath in InputFiles)
        {
            var source = Path.Combine(
                sourceRoot,
                relativePath.Replace('/', Path.DirectorySeparatorChar));
            var target = Path.Combine(
                targetRoot,
                relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(source, target, overwrite: true);
        }

        foreach (var relativePath in ReferencedOnlyFiles)
        {
            var target = Path.Combine(
                targetRoot,
                relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllText(target, "# Knowledge source\n", new UTF8Encoding(false));
        }
    }

    internal static void Write(string targetRoot)
    {
        CopyInputs(targetRoot);
        var result = AgentKnowledgeCheck.Write(targetRoot);
        Assert.True(result.Passed, string.Join(Environment.NewLine, result.Failures));
    }

    internal static void WithTemporaryRepository(Action<string> action)
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "vibesnake-agent-knowledge-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Exception? primaryFailure = null;
        try
        {
            CopyInputs(root);
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

    internal static string ResolveRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "VERSION")))
        {
            current = current.Parent;
        }

        return current?.FullName
            ?? throw new DirectoryNotFoundException("Could not resolve repository root.");
    }
}
