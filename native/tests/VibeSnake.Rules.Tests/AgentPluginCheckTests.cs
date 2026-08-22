using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using RepositoryChecks;

namespace VibeSnake.Rules.Tests;

public sealed class AgentPluginCheckTests
{
    private const string SourceManifest = """
        {
          "$schema": "https://agent-plugins.org/schemas/1.0.0/plugin.schema.json",
          "name": "vibesnake-agent",
          "version": "0.17.0",
          "description": "Play deterministic Vibe Snake matches through a local MCP host.",
          "author": {
            "name": "Vibe Snake",
            "email": "maintainers@example.invalid",
            "url": "https://github.com/blisspixel/VibeSnake"
          },
          "homepage": "https://github.com/blisspixel/VibeSnake",
          "repository": "https://github.com/blisspixel/VibeSnake.git",
          "license": "Apache-2.0",
          "keywords": ["agent-game", "mcp"],
          "extensions": {
            "vibesnake": {}
          }
        }
        """;

    private const string SourceSkill = """
        ---
        name: play-vibesnake
        description: Play deterministic Vibe Snake matches through the local MCP host.
        ---

        # Play Vibe Snake

        Use the discovered tool schema.
        """;

    private const string PackagedMcp = """
        {
          "$schema": "https://agent-plugins.org/schemas/1.0.0/mcp.schema.json",
          "mcpServers": {
            "vibesnake-agent": {
              "type": "stdio",
              "command": "dotnet",
              "args": ["${PLUGIN_ROOT}/bin/VibeSnake.AgentHost.dll"],
              "cwd": "${PLUGIN_ROOT}"
            }
          }
        }
        """;

    public static TheoryData<string, JsonNode?, string> InvalidRequiredManifestFields => new()
    {
        { "$schema", "https://example.invalid/plugin.json", "unsupported or missing" },
        { "name", "Invalid Name", "name violates" },
        { "name", "bad--name", "name violates" },
        { "name", "bad..name", "name violates" },
        { "version", "01.2.3", "canonical SemVer core" },
        { "description", "", "description must contain" },
        { "version", null, "canonical SemVer core" },
        { "description", null, "description must contain" },
    };

    private static readonly string[] RequiredPackagedFixturePaths =
    [
        "plugin.json",
        "mcp.json",
        "skills/play-vibesnake/SKILL.md",
        "LICENSE",
        "NOTICE",
        "bin/VibeSnake.AgentHost.dll",
    ];

    public static TheoryData<string, JsonNode, string> InvalidOptionalManifestFields => new()
    {
        { "homepage", 3, "homepage must be a string" },
        { "repository", false, "repository must be a string" },
        { "license", new JsonArray(), "license must be a string" },
        { "author", "someone", "author must be an object" },
        { "keywords", new JsonArray("mcp", 3), "keywords must be an array of strings" },
        { "extensions", new JsonObject { ["vibesnake"] = "bad" }, "extensions must map" },
    };

    [Fact]
    public void Current_source_plugin_passes_native_validation()
    {
        var root = ResolveRepositoryRoot();
        var plugin = Path.Combine(root, "integrations", "vibesnake-agent-plugin");

        var result = AgentPluginCheck.Inspect(plugin);

        Assert.True(result.Passed, string.Join(Environment.NewLine, result.Failures));
        Assert.Contains("source profile passed", result.SuccessMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void Complete_packaged_plugin_passes_with_exact_launch_and_checksums()
    {
        WithTemporaryPlugin(plugin =>
        {
            WriteSourcePlugin(plugin);
            CompletePackagedPlugin(plugin);

            var result = AgentPluginCheck.Inspect(plugin, requireMcp: true);

            Assert.True(result.Passed, string.Join(Environment.NewLine, result.Failures));
            Assert.Contains("packaged profile passed", result.SuccessMessage, StringComparison.Ordinal);
            Assert.Equal(7, Directory.EnumerateFiles(plugin, "*", SearchOption.AllDirectories).Count());
        });
    }

    [Fact]
    public void Missing_or_invalid_plugin_root_is_rejected()
    {
        WithTemporaryPlugin(plugin =>
        {
            var missing = AgentPluginCheck.Inspect(Path.Combine(plugin, "missing"));
            var invalid = AgentPluginCheck.Inspect("bad\0root");

            Assert.False(missing.Passed);
            Assert.Contains("existing directory", Assert.Single(missing.Failures), StringComparison.Ordinal);
            Assert.False(invalid.Passed);
            Assert.Contains("root is invalid", Assert.Single(invalid.Failures), StringComparison.Ordinal);
        });
    }

    [Theory]
    [MemberData(nameof(InvalidRequiredManifestFields))]
    public void Required_manifest_contract_is_closed(
        string field,
        JsonNode? replacement,
        string expectedFailure)
    {
        WithTemporaryPlugin(plugin =>
        {
            WriteSourcePlugin(plugin);
            var manifest = JsonNode.Parse(SourceManifest)!.AsObject();
            if (replacement is null)
            {
                manifest.Remove(field);
            }
            else
            {
                manifest[field] = replacement.DeepClone();
            }

            WriteText(plugin, "plugin.json", manifest.ToJsonString());

            var result = AgentPluginCheck.Inspect(plugin);

            Assert.False(result.Passed);
            Assert.Contains(result.Failures, failure => failure.Contains(expectedFailure, StringComparison.Ordinal));
        });
    }

    [Theory]
    [MemberData(nameof(InvalidOptionalManifestFields))]
    public void Optional_manifest_fields_retain_strict_types(
        string field,
        JsonNode replacement,
        string expectedFailure)
    {
        WithTemporaryPlugin(plugin =>
        {
            WriteSourcePlugin(plugin);
            var manifest = JsonNode.Parse(SourceManifest)!.AsObject();
            manifest[field] = replacement.DeepClone();
            WriteText(plugin, "plugin.json", manifest.ToJsonString());

            var result = AgentPluginCheck.Inspect(plugin);

            Assert.False(result.Passed);
            Assert.Contains(result.Failures, failure => failure.Contains(expectedFailure, StringComparison.Ordinal));
        });
    }

    [Fact]
    public void Manifest_rejects_unknown_nested_and_duplicate_fields()
    {
        WithTemporaryPlugin(plugin =>
        {
            WriteSourcePlugin(plugin);
            var unknown = SourceManifest
                .Replace(
                    "\"extensions\": {",
                    "\"unexpected\": true,\n  \"extensions\": {",
                    StringComparison.Ordinal)
                .Replace(
                    "\"name\": \"Vibe Snake\"",
                    "\"name\": \"Vibe Snake\", \"handle\": \"bad\"",
                    StringComparison.Ordinal);
            WriteText(plugin, "plugin.json", unknown);

            var unknownResult = AgentPluginCheck.Inspect(plugin);

            Assert.Contains("plugin.json: unknown field unexpected", unknownResult.Failures);
            Assert.Contains("plugin.json author: unknown field handle", unknownResult.Failures);

            WriteText(
                plugin,
                "plugin.json",
                SourceManifest.Replace(
                    "\"name\": \"vibesnake-agent\"",
                    "\"name\": \"first\", \"name\": \"second\"",
                    StringComparison.Ordinal));
            var duplicateResult = AgentPluginCheck.Inspect(plugin);

            Assert.Contains(
                duplicateResult.Failures,
                failure => failure.Contains("duplicate JSON key: name", StringComparison.Ordinal));
        });
    }

    [Fact]
    public void Manifest_rejects_non_object_invalid_utf8_and_oversize_input()
    {
        WithTemporaryPlugin(plugin =>
        {
            WriteSourcePlugin(plugin);
            WriteText(plugin, "plugin.json", "[]");
            Assert.Contains("plugin.json: root must be an object", AgentPluginCheck.Inspect(plugin).Failures);

            File.WriteAllBytes(Path.Combine(plugin, "plugin.json"), [0xff, 0xfe]);
            Assert.Contains(
                AgentPluginCheck.Inspect(plugin).Failures,
                failure => failure.Contains("unreadable JSON", StringComparison.Ordinal));

            File.WriteAllBytes(Path.Combine(plugin, "plugin.json"), new byte[(128 * 1024) + 1]);
            Assert.Contains(
                AgentPluginCheck.Inspect(plugin).Failures,
                failure => failure.Contains("validation limit", StringComparison.Ordinal));
        });
    }

    [Fact]
    public void Skill_frontmatter_accepts_safe_quoted_scalars_and_comments()
    {
        WithTemporaryPlugin(plugin =>
        {
            WriteSourcePlugin(plugin);
            WriteText(
                plugin,
                "skills/play-vibesnake/SKILL.md",
                "---\nname: 'play-vibesnake'\ndescription: \"Play safely\" # profile\n---\n\n# Body\n");

            var result = AgentPluginCheck.Inspect(plugin);

            Assert.True(result.Passed, string.Join(Environment.NewLine, result.Failures));
        });
    }

    [Theory]
    [InlineData("name: play-vibesnake\ndescription: safe\n---\nbody\n", "must start on the first line")]
    [InlineData("---\nname: play-vibesnake\ndescription: safe\n", "is not closed")]
    [InlineData("---\nname play-vibesnake\ndescription: safe\n---\nbody\n", "invalid YAML")]
    [InlineData("---\nname: play-vibesnake\nname: duplicate\ndescription: safe\n---\nbody\n", "duplicate frontmatter")]
    [InlineData("---\nname: play-vibesnake\nmetadata: preview\ndescription: safe\n---\nbody\n", "unknown frontmatter")]
    [InlineData("---\nname: play-vibesnake\ndescription: [not, scalar]\n---\nbody\n", "must be a string")]
    [InlineData("---\nname: play-vibesnake\ndescription: 42\n---\nbody\n", "must be a string")]
    [InlineData("---\nname: play-vibesnake\ndescription: true\n---\nbody\n", "must be a string")]
    [InlineData("---\nname: play-vibesnake\ndescription: yes\n---\nbody\n", "must be a string")]
    [InlineData("---\nname: play-vibesnake\ndescription: 2026-08-22\n---\nbody\n", "must be a string")]
    [InlineData("---\nname: play-vibesnake\ndescription: safe: nested\n---\nbody\n", "must be a string")]
    [InlineData("---\nname: play-vibesnake\ndescription: 'safe' trailing'\n---\nbody\n", "must be a string")]
    [InlineData("---\nname: play-vibesnake\ndescription: # missing\n---\nbody\n", "must be a string")]
    [InlineData("---\nname: play-vibesnake\ndescription: - item\n---\nbody\n", "must be a string")]
    [InlineData("---\nname: wrong\ndescription: safe\n---\nbody\n", "match its parent")]
    [InlineData("---\nname: play-vibesnake\ndescription: safe\n---\n", "Markdown instructions are required")]
    public void Skill_frontmatter_rejects_unsafe_or_incomplete_input(
        string source,
        string expectedFailure)
    {
        WithTemporaryPlugin(plugin =>
        {
            WriteSourcePlugin(plugin);
            WriteText(plugin, "skills/play-vibesnake/SKILL.md", source);

            var result = AgentPluginCheck.Inspect(plugin);

            Assert.False(result.Passed);
            Assert.Contains(result.Failures, failure => failure.Contains(expectedFailure, StringComparison.Ordinal));
        });
    }

    [Fact]
    public void Skills_component_must_be_a_directory_when_present()
    {
        WithTemporaryPlugin(plugin =>
        {
            WriteSourcePlugin(plugin);
            Directory.Delete(Path.Combine(plugin, "skills"), recursive: true);
            WriteText(plugin, "skills", "not a directory");

            var result = AgentPluginCheck.Inspect(plugin);

            Assert.Contains("skills: fixed component location must be a directory", result.Failures);
        });
    }

    [Fact]
    public void Packaged_profile_requires_mcp_and_exact_named_server()
    {
        WithTemporaryPlugin(plugin =>
        {
            WriteSourcePlugin(plugin);
            var missing = AgentPluginCheck.Inspect(plugin, requireMcp: true);
            Assert.Contains(
                "mcp.json: packaged plugin requires an MCP configuration",
                missing.Failures);

            WriteText(
                plugin,
                "mcp.json",
                PackagedMcp.Replace("vibesnake-agent", "other-agent", StringComparison.Ordinal));
            var wrongServer = AgentPluginCheck.Inspect(plugin, requireMcp: true);
            Assert.Contains(
                wrongServer.Failures,
                failure => failure.Contains("exactly the vibesnake-agent server", StringComparison.Ordinal));
        });
    }

    [Theory]
    [InlineData("streamable-http", "Vibe Snake's producer profile supports only stdio")]
    [InlineData("", "Vibe Snake's producer profile supports only stdio")]
    public void Mcp_profile_rejects_unsupported_transport(string transport, string expectedFailure)
    {
        WithTemporaryPlugin(plugin =>
        {
            WriteSourcePlugin(plugin);
            var mcp = JsonNode.Parse(PackagedMcp)!.AsObject();
            mcp["mcpServers"]!["vibesnake-agent"]!["type"] = transport;
            WriteText(plugin, "mcp.json", mcp.ToJsonString());

            var result = AgentPluginCheck.Inspect(plugin);

            Assert.Contains(result.Failures, failure => failure.Contains(expectedFailure, StringComparison.Ordinal));
        });
    }

    [Fact]
    public void Mcp_document_rejects_schema_shape_unknown_and_duplicate_fields()
    {
        WithTemporaryPlugin(plugin =>
        {
            WriteSourcePlugin(plugin);
            WriteText(plugin, "mcp.json", "[]");
            Assert.Contains("mcp.json: root must be an object", AgentPluginCheck.Inspect(plugin).Failures);

            WriteText(plugin, "mcp.json", "{\"$schema\":\"bad\",\"mcpServers\":[],\"extra\":true}");
            var shape = AgentPluginCheck.Inspect(plugin);
            Assert.Contains("mcp.json: schema must match Agent Plugins 1.0.0", shape.Failures);
            Assert.Contains("mcp.json: mcpServers must be an object", shape.Failures);
            Assert.Contains("mcp.json: unknown field extra", shape.Failures);

            WriteText(
                plugin,
                "mcp.json",
                PackagedMcp.Replace(
                    "\"command\": \"dotnet\"",
                    "\"command\": \"dotnet\", \"command\": \"other\"",
                    StringComparison.Ordinal));
            Assert.Contains(
                AgentPluginCheck.Inspect(plugin).Failures,
                failure => failure.Contains("duplicate JSON key: command", StringComparison.Ordinal));
        });
    }

    [Fact]
    public void Mcp_server_must_be_an_object()
    {
        WithTemporaryPlugin(plugin =>
        {
            WriteSourcePlugin(plugin);
            WriteText(
                plugin,
                "mcp.json",
                "{\"$schema\":\"https://agent-plugins.org/schemas/1.0.0/mcp.schema.json\",\"mcpServers\":{\"bad\":3}}");

            var result = AgentPluginCheck.Inspect(plugin);

            Assert.Contains(
                result.Failures,
                failure => failure.Contains("configuration must be an object", StringComparison.Ordinal));
        });
    }

    [Theory]
    [InlineData("command", "dotnet --info", "one nonempty executable token")]
    [InlineData("command", "../outside/host", "bare token or start with ./")]
    [InlineData("command", "./bin/missing", "packaged command does not exist")]
    [InlineData("cwd", "${PLUGIN_ROOT}/../escape", "cwd has an unsupported form")]
    [InlineData("cwd", "C:/outside", "cwd has an unsupported form")]
    public void Stdio_launch_rejects_unsafe_command_or_working_directory(
        string field,
        string value,
        string expectedFailure)
    {
        WithTemporaryPlugin(plugin =>
        {
            WriteSourcePlugin(plugin);
            var mcp = JsonNode.Parse(PackagedMcp)!.AsObject();
            mcp["mcpServers"]!["vibesnake-agent"]![field] = value;
            WriteText(plugin, "mcp.json", mcp.ToJsonString());

            var result = AgentPluginCheck.Inspect(plugin);

            Assert.Contains(result.Failures, failure => failure.Contains(expectedFailure, StringComparison.Ordinal));
        });
    }

    [Fact]
    public void Stdio_launch_rejects_wrong_types_reserved_environment_and_unknown_fields()
    {
        WithTemporaryPlugin(plugin =>
        {
            WriteSourcePlugin(plugin);
            var mcp = JsonNode.Parse(PackagedMcp)!.AsObject();
            var server = mcp["mcpServers"]!["vibesnake-agent"]!.AsObject();
            server["args"] = new JsonArray("good", 3);
            server["env"] = new JsonObject { ["plugin_root"] = "stolen" };
            server["cwd"] = 3;
            server["url"] = "https://example.invalid";
            WriteText(plugin, "mcp.json", mcp.ToJsonString());

            var result = AgentPluginCheck.Inspect(plugin);

            Assert.Contains(result.Failures, failure => failure.Contains("args must be an array", StringComparison.Ordinal));
            Assert.Contains(result.Failures, failure => failure.Contains("env cannot override", StringComparison.Ordinal));
            Assert.Contains(result.Failures, failure => failure.Contains("cwd must be a string", StringComparison.Ordinal));
            Assert.Contains(result.Failures, failure => failure.Contains("unknown field url", StringComparison.Ordinal));

            server["env"] = new JsonArray("bad");
            WriteText(plugin, "mcp.json", mcp.ToJsonString());
            Assert.Contains(
                AgentPluginCheck.Inspect(plugin).Failures,
                failure => failure.Contains("env must map strings to strings", StringComparison.Ordinal));
        });
    }

    [Theory]
    [InlineData("command", "other", "packaged command must be dotnet")]
    [InlineData("cwd", "${PLUGIN_DATA}", "packaged cwd must be ${PLUGIN_ROOT}")]
    public void Packaged_launch_declaration_is_exact(
        string field,
        string value,
        string expectedFailure)
    {
        WithTemporaryPlugin(plugin =>
        {
            WriteSourcePlugin(plugin);
            CompletePackagedPlugin(plugin);
            var mcp = JsonNode.Parse(File.ReadAllText(Path.Combine(plugin, "mcp.json")))!.AsObject();
            mcp["mcpServers"]!["vibesnake-agent"]![field] = value;
            WriteText(plugin, "mcp.json", mcp.ToJsonString());
            WriteChecksums(plugin);

            var result = AgentPluginCheck.Inspect(plugin, requireMcp: true);

            Assert.Contains(result.Failures, failure => failure.Contains(expectedFailure, StringComparison.Ordinal));
        });
    }

    [Fact]
    public void Packaged_launch_requires_only_the_declared_host_argument()
    {
        WithTemporaryPlugin(plugin =>
        {
            WriteSourcePlugin(plugin);
            CompletePackagedPlugin(plugin);
            var mcp = JsonNode.Parse(File.ReadAllText(Path.Combine(plugin, "mcp.json")))!.AsObject();
            mcp["mcpServers"]!["vibesnake-agent"]!["args"] = new JsonArray("other.dll");
            WriteText(plugin, "mcp.json", mcp.ToJsonString());
            WriteChecksums(plugin);

            var result = AgentPluginCheck.Inspect(plugin, requireMcp: true);

            Assert.Contains(
                result.Failures,
                failure => failure.Contains("only the declared Agent Host assembly", StringComparison.Ordinal));
        });
    }

    [Fact]
    public void Packaged_profile_requires_every_component()
    {
        WithTemporaryPlugin(plugin =>
        {
            foreach (var relative in RequiredPackagedFixturePaths)
            {
                ResetPackagedPlugin(plugin);
                File.Delete(Path.Combine(plugin, relative.Replace('/', Path.DirectorySeparatorChar)));

                var result = AgentPluginCheck.Inspect(plugin, requireMcp: true);

                Assert.Contains(
                    result.Failures,
                    failure => failure.Contains(
                        $"{relative}: required packaged regular file is missing",
                        StringComparison.Ordinal));
            }
        });
    }

    [Fact]
    public void Checksum_manifest_rejects_tampering_missing_duplicate_and_unsafe_entries()
    {
        WithTemporaryPlugin(plugin =>
        {
            WriteSourcePlugin(plugin);
            CompletePackagedPlugin(plugin);
            File.AppendAllText(Path.Combine(plugin, "NOTICE"), "tampered", new UTF8Encoding(false));
            var tampered = AgentPluginCheck.Inspect(plugin, requireMcp: true);
            Assert.Contains(
                tampered.Failures,
                failure => failure.Contains("digest mismatch for NOTICE", StringComparison.Ordinal));

            WriteChecksums(plugin);
            var checksum = Path.Combine(plugin, "SHA256SUMS");
            var lines = File.ReadAllLines(checksum);
            File.WriteAllLines(checksum, lines.Skip(1), new UTF8Encoding(false));
            var missing = AgentPluginCheck.Inspect(plugin, requireMcp: true);
            Assert.Contains(
                "SHA256SUMS: entries must match every packaged regular file exactly once",
                missing.Failures);

            File.WriteAllLines(checksum, [.. lines, lines[0]], new UTF8Encoding(false));
            var duplicate = AgentPluginCheck.Inspect(plugin, requireMcp: true);
            Assert.Contains(
                duplicate.Failures,
                failure => failure.Contains("duplicate path", StringComparison.Ordinal));

            File.WriteAllText(checksum, $"{'0'.ToString().PadLeft(64, '0')}  ../outside\n", new UTF8Encoding(false));
            var unsafeEntry = AgentPluginCheck.Inspect(plugin, requireMcp: true);
            Assert.Contains(
                unsafeEntry.Failures,
                failure => failure.Contains("invalid checksum entry", StringComparison.Ordinal));
        });
    }

    [Fact]
    public void Existing_checksum_is_validated_even_for_source_profile()
    {
        WithTemporaryPlugin(plugin =>
        {
            WriteSourcePlugin(plugin);
            WriteChecksums(plugin);
            File.AppendAllText(Path.Combine(plugin, "plugin.json"), " ", new UTF8Encoding(false));

            var result = AgentPluginCheck.Inspect(plugin);

            Assert.Contains(
                result.Failures,
                failure => failure.Contains("digest mismatch for plugin.json", StringComparison.Ordinal));
        });
    }

    [Fact]
    public void Fixed_component_paths_cannot_be_directories()
    {
        WithTemporaryPlugin(plugin =>
        {
            WriteSourcePlugin(plugin);
            File.Delete(Path.Combine(plugin, "plugin.json"));
            Directory.CreateDirectory(Path.Combine(plugin, "plugin.json"));
            Directory.CreateDirectory(Path.Combine(plugin, "mcp.json"));
            Directory.CreateDirectory(Path.Combine(plugin, "SHA256SUMS"));

            var result = AgentPluginCheck.Inspect(plugin, requireMcp: true);

            Assert.Contains("plugin.json: required regular file is missing", result.Failures);
            Assert.Contains("mcp.json: packaged plugin requires an MCP configuration", result.Failures);
            Assert.Contains(
                "SHA256SUMS: packaged plugin requires a complete checksum manifest",
                result.Failures);
        });
    }

    [Fact]
    public void Plugin_command_has_source_packaged_failure_and_usage_routes()
    {
        WithTemporaryPlugin(plugin =>
        {
            WriteSourcePlugin(plugin);
            var sourceOutput = new StringWriter();
            var sourceError = new StringWriter();
            Assert.Equal(
                0,
                RepositoryCheckCommand.Run(
                    ["plugin", plugin],
                    sourceOutput,
                    sourceError));
            Assert.Contains("source profile passed", sourceOutput.ToString(), StringComparison.Ordinal);
            Assert.Equal(string.Empty, sourceError.ToString());

            CompletePackagedPlugin(plugin);
            var packagedOutput = new StringWriter();
            Assert.Equal(
                0,
                RepositoryCheckCommand.Run(
                    ["plugin", plugin, "--require-mcp"],
                    packagedOutput,
                    new StringWriter()));
            Assert.Contains("packaged profile passed", packagedOutput.ToString(), StringComparison.Ordinal);

            File.Delete(Path.Combine(plugin, "plugin.json"));
            var failedError = new StringWriter();
            Assert.Equal(
                1,
                RepositoryCheckCommand.Run(
                    ["plugin", plugin],
                    new StringWriter(),
                    failedError));
            Assert.Contains("Agent Plugin check failed:", failedError.ToString(), StringComparison.Ordinal);

            var usageError = new StringWriter();
            Assert.Equal(
                2,
                RepositoryCheckCommand.Run(
                    ["plugin", plugin, "--wrong"],
                    new StringWriter(),
                    usageError));
            Assert.Contains("plugin <plugin-root>", usageError.ToString(), StringComparison.Ordinal);

            var invalidRootError = new StringWriter();
            Assert.Equal(
                2,
                RepositoryCheckCommand.Run(
                    ["plugin", "bad\0root"],
                    new StringWriter(),
                    invalidRootError));
            Assert.Contains("root is invalid", invalidRootError.ToString(), StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Link_entries_are_rejected_when_the_platform_can_create_them()
    {
        WithTemporaryPlugin(plugin =>
        {
            WriteSourcePlugin(plugin);
            var target = Path.Combine(plugin, "plugin.json");
            var link = Path.Combine(plugin, "linked.json");
            try
            {
                File.CreateSymbolicLink(link, target);
            }
            catch (Exception exception) when (
                exception is IOException
                    or UnauthorizedAccessException
                    or PlatformNotSupportedException)
            {
                return;
            }

            var result = AgentPluginCheck.Inspect(plugin);

            Assert.Contains("linked.json: links are not allowed", result.Failures);
        });
    }

    private static void ResetPackagedPlugin(string plugin)
    {
        foreach (var child in Directory.GetFileSystemEntries(plugin))
        {
            if (Directory.Exists(child))
            {
                Directory.Delete(child, recursive: true);
            }
            else
            {
                File.Delete(child);
            }
        }

        WriteSourcePlugin(plugin);
        CompletePackagedPlugin(plugin);
    }

    private static void WriteSourcePlugin(string plugin)
    {
        WriteText(plugin, "plugin.json", SourceManifest + "\n");
        WriteText(plugin, "skills/play-vibesnake/SKILL.md", SourceSkill + "\n");
    }

    private static void CompletePackagedPlugin(string plugin)
    {
        WriteText(plugin, "mcp.json", PackagedMcp + "\n");
        WriteText(plugin, "LICENSE", "license\n");
        WriteText(plugin, "NOTICE", "notice\n");
        WriteText(plugin, "bin/VibeSnake.AgentHost.dll", "host\n");
        WriteChecksums(plugin);
    }

    private static void WriteChecksums(string plugin)
    {
        var checksumPath = Path.Combine(plugin, "SHA256SUMS");
        if (File.Exists(checksumPath))
        {
            File.Delete(checksumPath);
        }

        var lines = Directory
            .EnumerateFiles(plugin, "*", SearchOption.AllDirectories)
            .Select(path => new
            {
                Path = path,
                Relative = Path.GetRelativePath(plugin, path).Replace('\\', '/'),
            })
            .OrderBy(item => item.Relative, StringComparer.Ordinal)
            .Select(item =>
                $"{Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(item.Path)))}  {item.Relative}")
            .ToArray();
        File.WriteAllText(
            checksumPath,
            string.Join('\n', lines) + "\n",
            new UTF8Encoding(false));
    }

    private static void WriteText(string root, string relativePath, string source)
    {
        var path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, source, new UTF8Encoding(false));
    }

    private static void WithTemporaryPlugin(Action<string> action)
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "vibesnake-agent-plugin-check-" + Guid.NewGuid().ToString("N"));
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

    private static string ResolveRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "VERSION"))
                && Directory.Exists(Path.Combine(current.FullName, "integrations")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not resolve repository root.");
    }
}
