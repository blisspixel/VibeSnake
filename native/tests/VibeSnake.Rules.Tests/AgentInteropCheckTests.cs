using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using RepositoryChecks;

namespace VibeSnake.Rules.Tests;

public sealed class AgentInteropCheckTests
{
    private const string ExpectedHostDigest =
        "d8c279812413bde5b47bc2387765274b4207963c4d88fd0db2e9026cfcd23b1e";
    private const string ExpectedPluginDigest =
        "4a3d8f444d67ca5847e23a6ddfc18dd49f75c4e279e3221ce367ba043478210d";

    [Fact]
    public void Checked_in_baseline_and_public_contract_digests_are_exact_and_fresh()
    {
        var root = AgentInteropTestRepository.ResolveRepositoryRoot();
        var digests = AgentInteropCheck.CalculateContractDigests(root);

        Assert.Equal(ExpectedHostDigest, digests.Host);
        Assert.Equal(ExpectedPluginDigest, digests.Plugin);
        var result = AgentInteropCheck.Inspect(root, new DateOnly(2026, 11, 13));
        Assert.True(result.Passed, string.Join(Environment.NewLine, result.Failures));
        Assert.Equal(
            $"Agent interoperability baseline verified: host={ExpectedHostDigest} "
            + $"plugin={ExpectedPluginDigest}.",
            result.SuccessMessage);
    }

    [Fact]
    public void Freshness_source_alignment_and_lifecycle_metadata_fail_closed()
    {
        AgentInteropTestRepository.WithTemporaryRepository(root =>
        {
            MutateBaseline(root, baseline =>
            {
                baseline["mcp"]!["host_version"] = "0.1.0";
                baseline["okf"]!["stale_after"] = "2026-11-14T00:00:00Z";
                baseline["okf"]!["generated_at"] = "2026-08-13Z";
            });

            var result = AgentInteropCheck.Inspect(root, new DateOnly(2026, 11, 14));

            AssertFailure(result, "interoperability baseline is stale");
            AssertFailure(result, "okf.stale_after must be an absolute YYYY-MM-DD date");
            AssertFailure(result, "okf.generated_at must be a canonical RFC 3339 UTC datetime");
            AssertFailure(result, "mcp.host_version='0.1.0'");
        });
    }

    [Fact]
    public void Reviewed_closed_values_dates_skill_fields_and_plugin_pins_are_enforced()
    {
        AgentInteropTestRepository.WithTemporaryRepository(root =>
        {
            MutateBaseline(root, baseline =>
            {
                baseline["reviewed_on"] = "2026-11-14";
                baseline["next_review_on"] = "2026-11-14";
                baseline["mcp"]!["transport"] = "http";
                baseline["mcp"]!["session_model"] = "sessionful";
                baseline["agent_plugins"]!["normative_status"] = "draft";
                baseline["agent_plugins"]!["website_status"] = "published";
                baseline["agent_plugins"]!["spec_source_commit"] = "main";
                baseline["agent_plugins"]!["spec_source_sha256"] = "ABC";
                baseline["agent_skill"]!["fields"] = new JsonArray("name");
                baseline["mcp_apps"]!["status"] = "supported";
            });

            var result = AgentInteropCheck.Inspect(root, new DateOnly(2026, 9, 1));

            AssertFailure(result, "next_review_on must be after reviewed_on");
            AssertFailure(result, "mcp.transport must be 'stdio'");
            AssertFailure(result, "mcp.session_model must be 'stateless'");
            AssertFailure(result, "agent_plugins.normative_status must be 'published'");
            AssertFailure(result, "agent_plugins.website_status must be 'working-draft'");
            AssertFailure(result, "agent_plugins.spec_source_commit must be a full lowercase Git commit SHA");
            AssertFailure(result, "agent_plugins.spec_source_url must bind the reviewed version");
            AssertFailure(result, "agent_plugins.spec_source_sha256 must be a lowercase SHA-256 digest");
            AssertFailure(result, "agent_skill.fields must be the reviewed minimal ordered field set");
            AssertFailure(result, "mcp_apps.status must be 'tracked-only'");
        });
    }

    [Fact]
    public void Public_host_and_plugin_contract_changes_require_versioned_digest_updates()
    {
        AgentInteropTestRepository.WithTemporaryRepository(root =>
        {
            var resources = InputPath(root, "native/tools/VibeSnake.AgentHost/AgentResources.cs");
            File.AppendAllText(resources, "\n// public resource drift\n", new UTF8Encoding(false));

            var result = AgentInteropCheck.Inspect(root, new DateOnly(2026, 9, 1));
            var digest = AgentInteropCheck.CalculateContractDigests(root);

            AssertFailure(result, "public host contract changed without a matching versioned digest entry");
            Assert.NotEqual(ExpectedHostDigest, digest.Host);
            Assert.Equal(ExpectedPluginDigest, digest.Plugin);
        });

        AgentInteropTestRepository.WithTemporaryRepository(root =>
        {
            var skill = InputPath(
                root,
                "integrations/vibesnake-agent-plugin/skills/play-vibesnake/SKILL.md");
            File.AppendAllText(skill, "\nContract drift.\n", new UTF8Encoding(false));

            var result = AgentInteropCheck.Inspect(root, new DateOnly(2026, 9, 1));
            var digest = AgentInteropCheck.CalculateContractDigests(root);

            AssertFailure(result, "public plugin contract changed without a matching versioned digest entry");
            Assert.Equal(ExpectedHostDigest, digest.Host);
            Assert.NotEqual(ExpectedPluginDigest, digest.Plugin);
        });
    }

    [Fact]
    public void Plugin_digest_excludes_only_the_package_version_and_canonicalizes_properties()
    {
        AgentInteropTestRepository.WithTemporaryRepository(root =>
        {
            var path = InputPath(root, "integrations/vibesnake-agent-plugin/plugin.json");
            var plugin = JsonNode.Parse(File.ReadAllBytes(path))!.AsObject();
            plugin["version"] = "9.9.9";
            WriteJson(path, plugin);
            Assert.Equal(
                ExpectedPluginDigest,
                AgentInteropCheck.CalculateContractDigests(root).Plugin);

            var reordered = new JsonObject();
            foreach (var property in plugin.Reverse())
            {
                reordered[property.Key] = property.Value?.DeepClone();
            }

            WriteJson(path, reordered);
            Assert.Equal(
                ExpectedPluginDigest,
                AgentInteropCheck.CalculateContractDigests(root).Plugin);

            reordered["name"] = "different-agent";
            WriteJson(path, reordered);
            Assert.NotEqual(
                ExpectedPluginDigest,
                AgentInteropCheck.CalculateContractDigests(root).Plugin);
        });
    }

    [Fact]
    public void Contract_history_requires_objects_exact_keys_semver_order_and_lowercase_digests()
    {
        AgentInteropTestRepository.WithTemporaryRepository(root =>
        {
            MutateBaseline(root, baseline =>
            {
                var host = baseline["public_contract_history"]!["host"]!.AsArray();
                host[0]!["extra"] = true;
                host[1]!["version"] = "not-semver";
                host[2]!["version"] = host[0]!["version"]!.GetValue<string>();
                host[3]!["version"] = "0.1.0";
                host[4]!["sha256"] = "ABC";
            });

            var result = AgentInteropCheck.Inspect(root, new DateOnly(2026, 9, 1));

            AssertFailure(result, "public_contract_history.host[0] keys must be exactly");
            AssertFailure(result, "public_contract_history.host[1].version must be SemVer core");
            AssertFailure(result, "public_contract_history.host[2].version must be unique");
            AssertFailure(result, "must be greater than the preceding history version");
            AssertFailure(result, "public_contract_history.host[4].sha256 must be a lowercase SHA-256 digest");
        });
    }

    [Fact]
    public void Missing_empty_and_nonobject_history_shapes_are_rejected_without_mutation()
    {
        AgentInteropTestRepository.WithTemporaryRepository(root =>
        {
            MutateBaseline(root, baseline =>
            {
                baseline["public_contract_history"]!["host"] = new JsonArray();
                baseline["public_contract_history"]!["plugin"] = new JsonArray("bad");
            });
            var path = InputPath(root, AgentInteropCheck.BaselinePath);
            var before = File.ReadAllBytes(path);

            var result = AgentInteropCheck.Inspect(root, new DateOnly(2026, 9, 1));

            AssertFailure(result, "public_contract_history.host must be a nonempty array");
            AssertFailure(result, "public_contract_history.plugin[0] must be an object");
            Assert.Equal(before, File.ReadAllBytes(path));
        });
    }

    [Fact]
    public void Exact_keys_object_shapes_scalar_types_and_dates_are_strict()
    {
        AgentInteropTestRepository.WithTemporaryRepository(root =>
        {
            MutateBaseline(root, baseline =>
            {
                baseline["extra"] = true;
                baseline["mcp"]!["extra"] = true;
                baseline["mcp"]!["sdk_version"] = 2;
                baseline["agent_plugins"]!["plugin_version"] = "1.0";
                baseline["agent_skill"] = "bad";
                baseline["okf"]!["verified_at"] = "2026-09-01T14:25:13+00:00";
                baseline["reviewed_on"] = "2026-9-1";
            });

            var result = AgentInteropCheck.Inspect(root, new DateOnly(2026, 9, 1));

            AssertFailure(result, "baseline keys must be exactly");
            AssertFailure(result, "mcp keys must be exactly");
            AssertFailure(result, "mcp.sdk_version must be SemVer core");
            AssertFailure(result, "agent_plugins.plugin_version must be SemVer core");
            AssertFailure(result, "agent_skill must be an object");
            AssertFailure(result, "okf.verified_at must be a canonical RFC 3339 UTC datetime");
            AssertFailure(result, "reviewed_on must be an absolute YYYY-MM-DD date");
        });
    }

    [Fact]
    public void Strict_json_utf8_bounds_missing_files_and_links_fail_closed()
    {
        AgentInteropTestRepository.WithTemporaryRepository(root =>
        {
            var path = InputPath(root, AgentInteropCheck.BaselinePath);
            var text = File.ReadAllText(path, Encoding.UTF8);
            File.WriteAllText(
                path,
                text.Replace(
                    "\"schema\":",
                    "\"schema\": \"duplicate\",\n  \"schema\":",
                    StringComparison.Ordinal),
                new UTF8Encoding(false));
            AssertFailure(
                AgentInteropCheck.Inspect(root, new DateOnly(2026, 9, 1)),
                "duplicate property schema");
        });

        AgentInteropTestRepository.WithTemporaryRepository(root =>
        {
            File.WriteAllBytes(InputPath(root, AgentInteropCheck.BaselinePath), [0xff]);
            Assert.False(AgentInteropCheck.Inspect(root, new DateOnly(2026, 9, 1)).Passed);
        });

        AgentInteropTestRepository.WithTemporaryRepository(root =>
        {
            File.WriteAllBytes(
                InputPath(root, AgentInteropCheck.BaselinePath),
                new byte[FixedCanonicalFixtureFile.MaximumBytes + 1]);
            AssertFailure(
                AgentInteropCheck.Inspect(root, new DateOnly(2026, 9, 1)),
                "exceeds 65536 bytes");
        });

        AgentInteropTestRepository.WithTemporaryRepository(root =>
        {
            File.Delete(InputPath(root, "native/tools/VibeSnake.AgentHost/Program.cs"));
            AssertFailure(
                AgentInteropCheck.Inspect(root, new DateOnly(2026, 9, 1)),
                "required fixture is missing");
        });

        AgentInteropTestRepository.WithTemporaryRepository(root =>
        {
            var path = InputPath(root, AgentInteropCheck.BaselinePath);
            var target = InputPath(root, "integrations/baseline-target.json");
            File.Move(path, target);
            try
            {
                File.CreateSymbolicLink(path, target);
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
            {
                return;
            }

            AssertFailure(
                AgentInteropCheck.Inspect(root, new DateOnly(2026, 9, 1)),
                "must not be a link");
        });
    }

    [Fact]
    public void Writer_is_idempotent_canonical_atomic_and_self_verified()
    {
        AgentInteropTestRepository.WithTemporaryRepository(root =>
        {
            var baselinePath = InputPath(root, AgentInteropCheck.BaselinePath);
            var original = File.ReadAllBytes(baselinePath);
            MutateBaseline(root, baseline =>
            {
                baseline["public_contract_history"]!["host"]!.AsArray()[^1]!["sha256"] = new string('0', 64);
                baseline["public_contract_history"]!["plugin"]!.AsArray()[^1]!["sha256"] = new string('1', 64);
            });

            var first = AgentInteropCheck.WriteDigests(root);
            Assert.True(first.Passed, string.Join(Environment.NewLine, first.Failures));
            Assert.Contains(ExpectedHostDigest, first.SuccessMessage, StringComparison.Ordinal);
            Assert.Contains(ExpectedPluginDigest, first.SuccessMessage, StringComparison.Ordinal);
            var repaired = File.ReadAllBytes(baselinePath);
            Assert.Equal(original, repaired);
            Assert.DoesNotContain((byte)'\r', repaired);
            Assert.Equal((byte)'\n', repaired[^1]);
            Assert.Equal(6852, repaired.Length);

            var second = AgentInteropCheck.WriteDigests(root);
            Assert.True(second.Passed, string.Join(Environment.NewLine, second.Failures));
            Assert.Equal(repaired, File.ReadAllBytes(baselinePath));
        });
    }

    [Fact]
    public void Writer_refuses_version_mismatch_and_link_destinations()
    {
        AgentInteropTestRepository.WithTemporaryRepository(root =>
        {
            MutateBaseline(root, baseline =>
                baseline["public_contract_history"]!["host"]!.AsArray()[^1]!["version"] = "0.17.0");
            AssertFailure(
                AgentInteropCheck.WriteDigests(root),
                "latest version is '0.17.0', expected '0.18.0'");
        });

        AgentInteropTestRepository.WithTemporaryRepository(root =>
        {
            var path = InputPath(root, AgentInteropCheck.BaselinePath);
            var target = InputPath(root, "integrations/baseline-target.json");
            File.Move(path, target);
            try
            {
                File.CreateSymbolicLink(path, target);
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
            {
                return;
            }

            AssertFailure(AgentInteropCheck.WriteDigests(root), "must not be a link");
        });
    }

    [Fact]
    public void Documentation_pins_and_obsolete_protocol_wording_are_enforced()
    {
        AgentInteropTestRepository.WithTemporaryRepository(root =>
        {
            var path = InputPath(root, "docs/engineering/AGENT_PLAY.md");
            var text = File.ReadAllText(path, Encoding.UTF8)
                .Replace("2026-07-28", "removed-protocol", StringComparison.Ordinal)
                + "\ninitialize with exactly one stable initialize revision\n";
            File.WriteAllText(path, text, new UTF8Encoding(false));

            var result = AgentInteropCheck.Inspect(root, new DateOnly(2026, 9, 1));

            AssertFailure(result, "AGENT_PLAY.md does not publish baseline value 2026-07-28");
            AssertFailure(result, "AGENT_PLAY.md contains obsolete MCP wording: initialize with exactly");
            AssertFailure(result, "AGENT_PLAY.md contains obsolete MCP wording: stable initialize revision");
        });
    }

    [Fact]
    public void Command_routes_cover_check_write_failure_and_usage()
    {
        AgentInteropTestRepository.WithTemporaryRepository(root =>
        {
            var output = new StringWriter();
            var error = new StringWriter();
            Assert.Equal(0, RepositoryCheckCommand.Run(["interop", root], output, error));
            Assert.Contains("baseline verified", output.ToString(), StringComparison.Ordinal);
            Assert.Equal(string.Empty, error.ToString());

            MutateBaseline(root, baseline =>
                baseline["public_contract_history"]!["host"]!.AsArray()[^1]!["sha256"] = new string('0', 64));
            output = new StringWriter();
            error = new StringWriter();
            Assert.Equal(1, RepositoryCheckCommand.Run(["interop", root], output, error));
            Assert.Contains("public host contract changed", error.ToString(), StringComparison.Ordinal);

            output = new StringWriter();
            error = new StringWriter();
            Assert.Equal(0, RepositoryCheckCommand.Run(["interop-write", root], output, error));
            Assert.Contains("baseline written", output.ToString(), StringComparison.Ordinal);

            output = new StringWriter();
            error = new StringWriter();
            Assert.Equal(
                2,
                RepositoryCheckCommand.Run(["interop-write", root, "extra"], output, error));
            Assert.Contains("interop-write", error.ToString(), StringComparison.Ordinal);
        });
    }

    private static void MutateBaseline(string root, Action<JsonObject> mutate)
    {
        var path = InputPath(root, AgentInteropCheck.BaselinePath);
        var baseline = JsonNode.Parse(File.ReadAllBytes(path))!.AsObject();
        mutate(baseline);
        WriteJson(path, baseline);
    }

    private static void WriteJson(string path, JsonObject value)
    {
        var text = value.ToJsonString(new JsonSerializerOptions { WriteIndented = true })
            .Replace("\r\n", "\n", StringComparison.Ordinal);
        File.WriteAllText(path, text + "\n", new UTF8Encoding(false));
    }

    private static string InputPath(string root, string relativePath) =>
        Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));

    private static void AssertFailure(RepositoryCheckResult result, string expected)
    {
        Assert.False(result.Passed);
        Assert.Contains(
            result.Failures,
            failure => failure.Contains(expected, StringComparison.Ordinal));
    }
}

internal static class AgentInteropTestRepository
{
    private static readonly string[] InputFiles =
    [
        AgentInteropCheck.BaselinePath,
        "native/src/VibeSnake.AgentPlay/AgentBurstPolicy.cs",
        "native/src/VibeSnake.AgentPlay/AgentContracts.cs",
        "native/src/VibeSnake.AgentPlay/AgentIdentity.cs",
        "native/src/VibeSnake.AgentPlay/AgentLessonEvidence.cs",
        "native/src/VibeSnake.AgentPlay/AgentExperience.cs",
        "native/src/VibeSnake.AgentPlay/AgentMatchSession.cs",
        "native/src/VibeSnake.AgentPlay/AgentObservationProjector.cs",
        "native/src/VibeSnake.AgentPlay/AgentStyleEvidence.cs",
        "native/src/VibeSnake.AgentPlay/AgentViewer.cs",
        "native/src/VibeSnake.AgentPlay/AgentPassportRecord.cs",
        "native/src/VibeSnake.AgentPlay/AgentPassportStore.cs",
        "native/src/VibeSnake.AgentPlay/AgentExhibitionStory.cs",
        "native/src/VibeSnake.AgentPlay/AgentExhibitionStoryReport.cs",
        "native/src/VibeSnake.AgentPlay/AgentQualificationCatalog.cs",
        "native/src/VibeSnake.AgentPlay/AgentQualificationReport.cs",
        "native/src/VibeSnake.Rules/CosmeticSetCatalog.cs",
        "native/src/VibeSnake.Rules/StationIdentityCatalog.cs",
        "native/tools/VibeSnake.AgentHost/AgentViewerServer.cs",
        "native/tools/VibeSnake.AgentHost/AgentToolArgumentFilter.cs",
        "native/tools/VibeSnake.AgentHost/AgentHostContracts.cs",
        "native/tools/VibeSnake.AgentHost/AgentHostDataPaths.cs",
        "native/tools/VibeSnake.AgentHost/AgentResources.cs",
        "native/tools/VibeSnake.AgentHost/AgentSessionRegistry.cs",
        "native/tools/VibeSnake.AgentHost/McpAgentTools.cs",
        "native/tools/VibeSnake.AgentHost/Program.cs",
        "native/tools/VibeSnake.AgentHost/VibeSnake.AgentHost.csproj",
        "integrations/vibesnake-agent-plugin/plugin.json",
        "integrations/vibesnake-agent-plugin/skills/play-vibesnake/SKILL.md",
        "scripts/package_agent_plugin.ps1",
        "docs/engineering/AGENT_PLAY.md",
    ];

    internal static void WithTemporaryRepository(Action<string> action)
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "vibesnake-agent-interop-tests",
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

    internal static string ResolveRepositoryRoot() =>
        AgentKnowledgeTestRepository.ResolveRepositoryRoot();

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
    }
}
