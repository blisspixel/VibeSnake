using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace RepositoryChecks;

public static class AgentKnowledgeCheck
{
    public const string OutputDirectory = "integrations/vibesnake-agent-knowledge";

    private const string BaselinePath = "integrations/agent-interop-baseline.json";
    private const string PluginPath = "integrations/vibesnake-agent-plugin/plugin.json";
    private const string RulesIdentityPath = "native/src/VibeSnake.Rules/RulesetIdentity.cs";
    private const string ContractsPath = "native/src/VibeSnake.AgentPlay/AgentContracts.cs";
    private const string ExperiencePath = "native/src/VibeSnake.AgentPlay/AgentExperience.cs";
    private const string LessonEvidencePath = "native/src/VibeSnake.AgentPlay/AgentLessonEvidence.cs";
    private const string ViewerPath = "native/src/VibeSnake.AgentPlay/AgentViewer.cs";
    private const string ToolsPath = "native/tools/VibeSnake.AgentHost/McpAgentTools.cs";
    private const string ResourcesPath = "native/tools/VibeSnake.AgentHost/AgentResources.cs";
    private const string ProgramPath = "native/tools/VibeSnake.AgentHost/Program.cs";
    private const string HostProjectPath =
        "native/tools/VibeSnake.AgentHost/VibeSnake.AgentHost.csproj";
    private const int ExpectedStyleCount = 5;
    private const int ExpectedLessonCount = 8;

    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    private static readonly string[] OutputNames =
    [
        "index.md",
        "rules.md",
        "protocol.md",
        "experience.md",
        "replays.md",
    ];

    private static readonly string[] ReferencedLocalSources =
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

    private static readonly Regex ToolNamePattern = new(
        "Name = \"([a-z_]+)\"",
        RegexOptions.CultureInvariant);

    private static readonly Regex ResourceUriPattern = new(
        "UriTemplate = \"([^\"]+)\"",
        RegexOptions.CultureInvariant);

    public static RepositoryCheckResult Inspect(string repositoryRoot) =>
        Inspect(
            repositoryRoot,
            DateOnly.FromDateTime(TimeProvider.System.GetUtcNow().UtcDateTime));

    public static RepositoryCheckResult Write(string repositoryRoot)
    {
        try
        {
            var rendered = RenderBundle(repositoryRoot);
            foreach (var name in OutputNames)
            {
                FixedCanonicalFixtureFile.Write(
                    repositoryRoot,
                    $"{OutputDirectory}/{name}",
                    $"Agent knowledge {name}",
                    rendered[name]);
            }

            var verification = Inspect(repositoryRoot);
            if (!verification.Passed)
            {
                return new RepositoryCheckResult(
                    "Agent knowledge",
                    false,
                    string.Empty,
                    verification.Failures
                        .Select(failure => "write verification failed: " + failure)
                        .ToArray());
            }

            return Passed("written", rendered);
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            return Failed(FixedCanonicalFixtureFile.SingleLine(exception.Message));
        }
    }

    internal static RepositoryCheckResult Inspect(
        string repositoryRoot,
        DateOnly asOf)
    {
        try
        {
            var rendered = RenderBundle(repositoryRoot);
            var failures = new List<string>();
            foreach (var name in OutputNames)
            {
                var actual = FixedCanonicalFixtureFile.Read(
                    repositoryRoot,
                    $"{OutputDirectory}/{name}",
                    $"Agent knowledge {name}");
                if (!actual.AsSpan().SequenceEqual(rendered[name]))
                {
                    failures.Add($"generated file is stale: {name}");
                }
            }

            failures.AddRange(UnexpectedOutputFailures(repositoryRoot));
            var staleAfter = ReadBaseline(repositoryRoot).StaleAfter;
            if (asOf >= staleAfter)
            {
                failures.Add(
                    "agent knowledge is stale: "
                    + $"as-of {asOf:yyyy-MM-dd} reached stale_after {staleAfter:yyyy-MM-dd}; "
                    + "review canonical sources and advance verification metadata");
            }

            return failures.Count == 0
                ? Passed("verified", rendered)
                : new RepositoryCheckResult(
                    "Agent knowledge",
                    false,
                    string.Empty,
                    failures);
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            return Failed(FixedCanonicalFixtureFile.SingleLine(exception.Message));
        }
    }

    internal static IReadOnlyDictionary<string, byte[]> RenderBundle(
        string repositoryRoot)
    {
        var baseline = ReadBaseline(repositoryRoot);
        var plugin = ReadPlugin(repositoryRoot);
        var rulesIdentity = ReadText(repositoryRoot, RulesIdentityPath, "rules identity");
        var contracts = ReadText(repositoryRoot, ContractsPath, "agent contracts");
        var experience = ReadText(repositoryRoot, ExperiencePath, "agent experience");
        var lessonEvidence = ReadText(
            repositoryRoot,
            LessonEvidencePath,
            "lesson evidence");
        var viewer = ReadText(repositoryRoot, ViewerPath, "viewer contract");
        var tools = ReadText(repositoryRoot, ToolsPath, "MCP tools");
        var resources = ReadText(repositoryRoot, ResourcesPath, "MCP resources");
        var program = ReadText(repositoryRoot, ProgramPath, "Agent Host program");
        var hostProject = ReadText(repositoryRoot, HostProjectPath, "Agent Host project");
        foreach (var source in ReferencedLocalSources)
        {
            _ = FixedCanonicalFixtureFile.Read(repositoryRoot, source, "knowledge source");
        }

        var rulesetId = MatchSingle(
            rulesIdentity,
            "CurrentId = \"([^\"]+)\"",
            "ruleset ID");
        var rulesVersion = MatchSingle(
            rulesIdentity,
            "CurrentVersion = ([0-9]+)",
            "rules version");
        var observationSchema = MatchSingle(
            contracts,
            "record AgentObservationV5\\([\\s\\S]*?Contract = \"([^\"]+)\"",
            "observation schema");
        var resultSchema = MatchSingle(
            contracts,
            "record AgentMatchResultV5\\([\\s\\S]*?Contract = \"([^\"]+)\"",
            "result schema");
        var hostVersion = MatchSingle(
            program,
            "HostVersion = \"([^\"]+)\"",
            "host version");
        var sdkVersion = MatchSingle(
            hostProject,
            "<PackageReference Include=\"ModelContextProtocol\" Version=\"([^\"]+)\"",
            "MCP SDK version");
        var viewerContract = MatchSingle(
            viewer,
            "record AgentViewerFrameV[0-9]+\\([\\s\\S]*?Contract = \"([^\"]+)\"",
            "viewer frame contract");
        var survivalContract = MatchSingle(
            viewer,
            "record AgentSurvivalStateV1\\([\\s\\S]*?Contract = \"([^\"]+)\"",
            "survival contract");

        if (!string.Equals(sdkVersion, baseline.McpSdkVersion, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"MCP SDK source version {sdkVersion} does not match baseline {baseline.McpSdkVersion}");
        }

        if (!string.Equals(hostVersion, baseline.HostVersion, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"host source version {hostVersion} does not match baseline {baseline.HostVersion}");
        }

        var toolNames = DistinctSortedMatches(tools, ToolNamePattern, "MCP tool names");
        var resourceUris = DistinctSortedMatches(
            resources,
            ResourceUriPattern,
            "MCP resource URIs");
        var styleIds = ExtractStyleIds(experience);
        var lessonIds = ExtractLessonIds(lessonEvidence);

        var tokens = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["@@GENERATED_AT@@"] = baseline.GeneratedAt,
            ["@@VERIFIED_AT@@"] = baseline.VerifiedAt,
            ["@@STALE_AFTER@@"] = baseline.StaleAfter.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["@@OKF_VERSION@@"] = baseline.OkfVersion,
            ["@@RULESET_ID@@"] = rulesetId,
            ["@@RULES_VERSION@@"] = rulesVersion,
            ["@@OBSERVATION_SCHEMA@@"] = observationSchema,
            ["@@RESULT_SCHEMA@@"] = resultSchema,
            ["@@HOST_VERSION@@"] = hostVersion,
            ["@@PLUGIN_VERSION@@"] = plugin.Version,
            ["@@PLUGIN_SCHEMA@@"] = plugin.Schema,
            ["@@PLUGIN_SPEC_URL@@"] = baseline.PluginSpecUrl,
            ["@@PLUGIN_SPEC_VERSION@@"] = baseline.PluginSpecVersion,
            ["@@MCP_PROTOCOL@@"] = baseline.McpProtocolVersion,
            ["@@MCP_SDK_VERSION@@"] = sdkVersion,
            ["@@VIEWER_CONTRACT@@"] = viewerContract,
            ["@@SURVIVAL_CONTRACT@@"] = survivalContract,
            ["@@TOOLS@@"] = RenderBulletList(toolNames),
            ["@@RESOURCES@@"] = RenderBulletList(resourceUris),
            ["@@STYLE_IDS@@"] = RenderBulletList(styleIds),
            ["@@LESSON_IDS@@"] = RenderBulletList(lessonIds),
        };

        return new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["index.md"] = Encode(RenderTemplate(IndexTemplate, tokens)),
            ["rules.md"] = Encode(RenderTemplate(RulesTemplate, tokens)),
            ["protocol.md"] = Encode(RenderTemplate(ProtocolTemplate, tokens)),
            ["experience.md"] = Encode(RenderTemplate(ExperienceTemplate, tokens)),
            ["replays.md"] = Encode(RenderTemplate(ReplaysTemplate, tokens)),
        };
    }

    private static KnowledgeBaseline ReadBaseline(string repositoryRoot)
    {
        using var document = ParseJson(repositoryRoot, BaselinePath, "interoperability baseline");
        var root = RequireObject(document.RootElement, "baseline");
        var okf = RequireObjectProperty(root, "okf", "baseline");
        var mcp = RequireObjectProperty(root, "mcp", "baseline");
        var plugins = RequireObjectProperty(root, "agent_plugins", "baseline");
        var generatedAt = RequireString(okf, "generated_at", "okf");
        var verifiedAt = RequireString(okf, "verified_at", "okf");
        ValidateUtcTimestamp(generatedAt, "okf.generated_at");
        ValidateUtcTimestamp(verifiedAt, "okf.verified_at");
        var staleText = RequireString(okf, "stale_after", "okf");
        if (!DateOnly.TryParseExact(
                staleText,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var staleAfter)
            || !string.Equals(
                staleAfter.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                staleText,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException("okf.stale_after must be an absolute YYYY-MM-DD date");
        }

        return new KnowledgeBaseline(
            RequireString(okf, "spec_version", "okf"),
            generatedAt,
            verifiedAt,
            staleAfter,
            RequireString(mcp, "protocol_version", "mcp"),
            RequireString(mcp, "sdk_version", "mcp"),
            RequireString(mcp, "host_version", "mcp"),
            RequireString(plugins, "spec_source_url", "agent_plugins"),
            RequireString(plugins, "spec_version", "agent_plugins"));
    }

    private static PluginIdentity ReadPlugin(string repositoryRoot)
    {
        using var document = ParseJson(repositoryRoot, PluginPath, "plugin manifest");
        var root = RequireObject(document.RootElement, "plugin manifest");
        return new PluginIdentity(
            RequireString(root, "$schema", "plugin manifest"),
            RequireString(root, "version", "plugin manifest"));
    }

    private static JsonDocument ParseJson(
        string repositoryRoot,
        string relativePath,
        string label)
    {
        var bytes = FixedCanonicalFixtureFile.Read(repositoryRoot, relativePath, label);
        var document = JsonDocument.Parse(
            bytes,
            new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 32,
            });
        RejectDuplicateProperties(document.RootElement, label);
        return document;
    }

    private static void RejectDuplicateProperties(JsonElement element, string label)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name))
                {
                    throw new InvalidDataException(
                        $"{label} contains duplicate property {property.Name}");
                }

                RejectDuplicateProperties(property.Value, label);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                RejectDuplicateProperties(item, label);
            }
        }
    }

    private static JsonElement RequireObject(JsonElement element, string label)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException($"{label} must be a JSON object");
        }

        return element;
    }

    private static JsonElement RequireObjectProperty(
        JsonElement element,
        string propertyName,
        string label)
    {
        if (!element.TryGetProperty(propertyName, out var property)
            || property.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException($"{label}.{propertyName} must be an object");
        }

        return property;
    }

    private static string RequireString(
        JsonElement element,
        string propertyName,
        string label)
    {
        if (!element.TryGetProperty(propertyName, out var property)
            || property.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(property.GetString()))
        {
            throw new InvalidDataException($"{label}.{propertyName} must be a nonempty string");
        }

        return property.GetString()!;
    }

    private static void ValidateUtcTimestamp(string value, string label)
    {
        if (!DateTimeOffset.TryParseExact(
                value,
                "yyyy-MM-dd'T'HH:mm:ss'Z'",
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsed)
            || !string.Equals(
                parsed.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
                value,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException($"{label} must be a canonical RFC 3339 UTC datetime");
        }
    }

    private static string[] DistinctSortedMatches(
        string source,
        Regex pattern,
        string label)
    {
        var values = pattern.Matches(source)
            .Select(match => match.Groups[1].Value)
            .ToArray();
        if (values.Length == 0)
        {
            throw new InvalidDataException($"could not extract {label} from its canonical source");
        }

        if (values.Distinct(StringComparer.Ordinal).Count() != values.Length)
        {
            throw new InvalidDataException($"{label} must not contain duplicates");
        }

        return values.Order(StringComparer.Ordinal).ToArray();
    }

    private static string[] ExtractStyleIds(string source)
    {
        var catalog = MatchSingle(
            source,
            "public static class AgentStyleContractCatalog\\s*\\{([\\s\\S]*?)\\n\\}",
            "style catalog");
        var ids = Regex.Matches(
                catalog,
                "public const string \\w+Id = \"([a-z-]+)\";",
                RegexOptions.CultureInvariant)
            .Select(match => match.Groups[1].Value)
            .ToArray();
        RequireCatalog(ids, ExpectedStyleCount, "style IDs");
        return ids;
    }

    private static List<string> ExtractLessonIds(string source)
    {
        var catalog = MatchSingle(
            source,
            "public static class AgentSignalSchoolCatalog\\s*\\{([\\s\\S]*?)\\n\\}",
            "Signal School catalog");
        var constants = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match match in Regex.Matches(
            catalog,
            "public const string (\\w+Id) = \"([a-z-]+)\";",
            RegexOptions.CultureInvariant))
        {
            if (!constants.TryAdd(match.Groups[1].Value, match.Groups[2].Value))
            {
                throw new InvalidDataException("lesson ID constants must not contain duplicates");
            }
        }

        var ids = new List<string>();
        foreach (Match match in Regex.Matches(
            catalog,
            "\\bLesson\\(\\s*(\\w+Id)",
            RegexOptions.CultureInvariant))
        {
            if (!constants.TryGetValue(match.Groups[1].Value, out var id))
            {
                throw new InvalidDataException(
                    $"lesson catalog references unknown ID constant {match.Groups[1].Value}");
            }

            ids.Add(id);
        }

        RequireCatalog(ids, ExpectedLessonCount, "lesson IDs");
        return ids;
    }

    private static void RequireCatalog(
        IReadOnlyList<string> values,
        int expectedCount,
        string label)
    {
        if (values.Count != expectedCount)
        {
            throw new InvalidDataException(
                $"{label} must contain exactly {expectedCount} entries; found {values.Count}");
        }

        if (values.Distinct(StringComparer.Ordinal).Count() != values.Count)
        {
            throw new InvalidDataException($"{label} must not contain duplicates");
        }
    }

    private static string MatchSingle(string source, string pattern, string label)
    {
        var matches = Regex.Matches(
            source,
            pattern,
            RegexOptions.CultureInvariant);
        if (matches.Count != 1)
        {
            throw new InvalidDataException(
                $"could not extract exactly one {label} from its canonical source; found {matches.Count}");
        }

        return matches[0].Groups[1].Value;
    }

    private static List<string> UnexpectedOutputFailures(string repositoryRoot)
    {
        var root = Path.GetFullPath(repositoryRoot);
        var outputRoot = Path.Combine(
            root,
            OutputDirectory.Replace('/', Path.DirectorySeparatorChar));
        var failures = new List<string>();
        var count = 0;
        foreach (var entry in Directory.EnumerateFileSystemEntries(outputRoot))
        {
            count++;
            if (count > FixedCanonicalFixtureFile.MaximumSiblingEntries)
            {
                throw new InvalidDataException(
                    $"Agent knowledge output exceeds {FixedCanonicalFixtureFile.MaximumSiblingEntries} entries");
            }

            var name = Path.GetFileName(entry);
            if (!OutputNames.Contains(name, StringComparer.Ordinal))
            {
                failures.Add($"unexpected generated concept: {name}");
            }
        }

        return failures;
    }

    private static string ReadText(
        string repositoryRoot,
        string relativePath,
        string label) =>
        StrictUtf8.GetString(
            FixedCanonicalFixtureFile.Read(repositoryRoot, relativePath, label));

    private static string RenderBulletList(IEnumerable<string> values) =>
        string.Join('\n', values.Select(value => $"* `{value}`"));

    private static string RenderTemplate(
        string template,
        IReadOnlyDictionary<string, string> tokens)
    {
        var rendered = template.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
        foreach (var (token, value) in tokens)
        {
            rendered = rendered.Replace(token, value, StringComparison.Ordinal);
        }

        if (rendered.Contains("@@", StringComparison.Ordinal))
        {
            throw new InvalidDataException("Agent knowledge template contains an unresolved token");
        }

        return rendered + "\n";
    }

    private static byte[] Encode(string value)
    {
        var bytes = StrictUtf8.GetBytes(value);
        FixedCanonicalFixtureFile.EnsureBounded(bytes.Length, "Agent knowledge concept");
        return bytes;
    }

    private static bool IsExpectedFailure(Exception exception) =>
        FixedCanonicalFixtureFile.IsExpectedFailure(exception)
        || exception is JsonException
        || exception is DecoderFallbackException
        || exception is EncoderFallbackException
        || exception is OverflowException;

    private static RepositoryCheckResult Passed(
        string action,
        IReadOnlyDictionary<string, byte[]> rendered) =>
        new(
            "Agent knowledge",
            true,
            $"Agent knowledge {action}: concepts={rendered.Count} "
                + $"bytes={rendered.Values.Sum(bytes => bytes.Length)}.",
            []);

    private static RepositoryCheckResult Failed(string failure) =>
        new("Agent knowledge", false, string.Empty, [failure]);

    private sealed record KnowledgeBaseline(
        string OkfVersion,
        string GeneratedAt,
        string VerifiedAt,
        DateOnly StaleAfter,
        string McpProtocolVersion,
        string McpSdkVersion,
        string HostVersion,
        string PluginSpecUrl,
        string PluginSpecVersion);

    private sealed record PluginIdentity(string Schema, string Version);

    private const string IndexTemplate = """
---
okf_version: "@@OKF_VERSION@@"
---

# Vibe Snake Agent Knowledge

* [Rules and observations](rules.md) - Public state, actions, modes, and authority boundaries.
* [MCP protocol](protocol.md) - Local host tools, resources, versions, and transport limits.
* [Agent experience](experience.md) - Signal School lessons and Style Contracts.
* [Verified replay handoff](replays.md) - Verified results, explicit saving, and human viewing.
""";

    private const string RulesTemplate = """
---
type: "Game Rules"
title: "Vibe Snake agent rules and observations"
description: "The public, deterministic rules boundary available to an external agent."
tags: [vibesnake, rules, observation, agents]
generated: { by: process:vibesnake-okf-generator, at: @@GENERATED_AT@@ }
verified: { by: process:vibesnake-quality-gate, at: @@VERIFIED_AT@@ }
stale_after: "@@STALE_AFTER@@"
status: draft
sources:
  - id: rules-identity
    resource: ../../native/src/VibeSnake.Rules/RulesetIdentity.cs
    title: "Ruleset identity"
  - id: agent-contracts
    resource: ../../native/src/VibeSnake.AgentPlay/AgentContracts.cs
    title: "Agent contracts"
  - id: agent-identity
    resource: ../../native/src/VibeSnake.AgentPlay/AgentIdentity.cs
    title: "Agent identity catalogs"
  - id: station-identity
    resource: ../../native/src/VibeSnake.Rules/StationIdentityCatalog.cs
    title: "Station identity catalog"
  - id: mode-catalog
    resource: ../../native/src/VibeSnake.Rules/RunModeCatalog.cs
    title: "Official mode catalog"
---
# Authority

The rules authority is `@@RULESET_ID@@@@@RULES_VERSION@@`. The public observation schema is `@@OBSERVATION_SCHEMA@@`.
This knowledge bundle is descriptive. The rules assembly, tool schemas, and verified replay remain authoritative.

# Actions

An agent may choose `continue`, `up`, `right`, `down`, or `left`. In `four-direction-step-v1`, one accepted action advances exactly one clock-free rules step. In the separate `four-direction-burst-v1` division, one initial action is followed by at most 15 straight continuations and stops under fixed `decision-event-stop-v1` public events, a selected lesson's transition to all requirements reached, or a closed terminal, cap, replay-failure, or requested-bound reason.
Each mutation is bound to the observed tick, state hash, and one shared idempotency-key namespace capped at 4,096 unique records per match. Exact retries return cached typed responses; known keys are never evicted, and changed, cross-operation, or post-cap unseen keys advance no additional state.

# Public observation

The observation includes the catalog-validated public Agent Passport v4, board, ordered body, direction queue, food, visible powers and obstacles, score, combo, hunger, active effects, adaptive policy, previous public events, episode metrics, optional two-criterion live style progress, and optional ordered Signal School requirement progress.
Passport identity is caller-declared and ephemeral. Avatar, accent, and station IDs must resolve through the host's closed identity resource; they affect presentation only and remain independent of human progression and cosmetics.
It excludes random state, future outcomes, controller internals, profiles, progression, paths, prompts, credentials, diagnostics, and hidden reasoning.

# Seed divisions

Open matches expose the gameplay seed. Blind matches withhold it until the verified result. Classic and Vibe results remain separate identities.
""";

    private const string ProtocolTemplate = """
---
type: "Protocol"
title: "Vibe Snake MCP agent host"
description: "The local stdio MCP surface and its portable Agent Plugin packaging."
tags: [vibesnake, mcp, agent-plugins, stdio]
generated: { by: process:vibesnake-okf-generator, at: @@GENERATED_AT@@ }
verified: { by: process:vibesnake-quality-gate, at: @@VERIFIED_AT@@ }
stale_after: "@@STALE_AFTER@@"
status: draft
sources:
  - id: mcp-tools
    resource: ../../native/tools/VibeSnake.AgentHost/McpAgentTools.cs
    title: "MCP tool adapter"
  - id: mcp-resources
    resource: ../../native/tools/VibeSnake.AgentHost/AgentResources.cs
    title: "MCP resources"
  - id: viewer-contract
    resource: ../../native/src/VibeSnake.AgentPlay/AgentViewer.cs
    title: "Live viewer wire contract"
  - id: viewer-client
    resource: ../../native/src/VibeSnake.AgentViewer/AgentViewerClient.cs
    title: "Live viewer client"
  - id: plugin-manifest
    resource: ../vibesnake-agent-plugin/plugin.json
    title: "Agent Plugin manifest"
  - id: agent-plugins-normative-spec
    resource: @@PLUGIN_SPEC_URL@@
    title: "Immutable Agent Plugins @@PLUGIN_SPEC_VERSION@@ normative specification"
  - id: agent-plugins-website
    resource: https://agent-plugins.org/specification
    title: "Agent Plugins public specification website"
  - id: mcp-specification
    resource: https://modelcontextprotocol.io/specification/@@MCP_PROTOCOL@@
    title: "Model Context Protocol @@MCP_PROTOCOL@@ specification"
  - id: mcp-csharp-sdk
    resource: https://github.com/modelcontextprotocol/csharp-sdk/releases/tag/v@@MCP_SDK_VERSION@@
    title: "Official C# SDK @@MCP_SDK_VERSION@@ release"
---
# Versions

The host version is `@@HOST_VERSION@@`. The Agent Plugin version is `@@PLUGIN_VERSION@@` and targets `@@PLUGIN_SCHEMA@@`.
The MCP server targets stable protocol `@@MCP_PROTOCOL@@` through the official C# SDK `@@MCP_SDK_VERSION@@`.
Clients must speak the stateless MCP `@@MCP_PROTOCOL@@` era: every request carries protocol metadata, optional discovery uses `server/discover`, and there is no protocol session. Legacy `initialize` handshakes are rejected and this preview provides no downlevel fallback.

# Tools

@@TOOLS@@

# Resources

@@RESOURCES@@

# Live viewer

The optional same-user pipe uses `@@VIEWER_CONTRACT@@`. Every frame declares initial, step, burst, or finish origin and binds exact steps advanced to the pre-mutation tick and state hash. Burst frames carry closed stop reason and final-step event, while terminal truth, immutable match identity, catalog-bound Passport v4, action facts, contiguous state anchors, two ordered live style criteria, ordered lesson progress, optional replay-bound terminal style outcomes, optional combined-evidence lesson outcomes, and the verified replay payload hash are cross-validated before presentation. Its `@@SURVIVAL_CONTRACT@@` block reports structural open exits, the closed pressure tier, and fixed-order held recovery resources derived from the same public board state; disagreement rejects the frame, and the block never recommends a direction. Malformed, oversized, contradictory, unknown-catalog, identity-drifting, criterion-drifting, requirement-drifting, or mixed-version input clears pending content and rejects the stream. The host keeps only the latest unsent frame, the client reports sequence gaps as coalesced earlier updates, and the packaged-host transcript exercises rejection-aware lesson recovery as well as terminal burst delivery. The verified replay remains the canonical accepted-step history, and viewer timing never advances rules or score.

# Trust boundary

The first transport is local stdio. It opens no network listener, accepts no executable, arbitrary path, action list, or custom stop predicate, and keeps opaque bearer handles in one bounded process without a separate client-authentication layer. Finalized matches are evicted first at capacity; otherwise only a live handle with no valid handle-bearing operation for 30 minutes may be reclaimed without a result or replay. Replacement construction precedes eviction, and viewer activity is never match control. The normative Agent Plugins repository labels @@PLUGIN_SPEC_VERSION@@ Published while the public specification website still labels it Working Draft, so Vibe Snake retains preview-quality packaging and drift review.
""";

    private const string ExperienceTemplate = """
---
type: "Curriculum"
title: "Vibe Snake Signal School and Style Contracts"
description: "Deterministic lessons and self-selected public goals for agent-native play."
tags: [vibesnake, curriculum, styles, evaluation]
generated: { by: process:vibesnake-okf-generator, at: @@GENERATED_AT@@ }
verified: { by: process:vibesnake-quality-gate, at: @@VERIFIED_AT@@ }
stale_after: "@@STALE_AFTER@@"
status: draft
sources:
  - id: agent-experience
    resource: ../../native/src/VibeSnake.AgentPlay/AgentExperience.cs
    title: "Agent experience catalog"
  - id: lesson-evidence
    resource: ../../native/src/VibeSnake.AgentPlay/AgentLessonEvidence.cs
    title: "Signal School requirement and evidence evaluator"
  - id: style-evidence
    resource: ../../native/src/VibeSnake.AgentPlay/AgentStyleEvidence.cs
    title: "Replay-derived style evidence evaluator"
  - id: experience-design
    resource: ../../docs/design/AGENT_ARENA.md
    title: "Agent Arena experience contract"
---
# Style Contracts

@@STYLE_IDS@@

Each style publishes exactly two ordered, factual criteria under `replay-composite-core4-v1`. Stillwater combines rules-advanced-step survival with structural-open-exit rate. Crownchaser combines peak combo with uninterrupted food continuity through the first combo of four. Edge Prophet combines rewarded body-proximity near misses with a same-step wrap fact under the pinned `vibesnake-core@4` evaluator. Mutagenist combines distinct activated power kinds with concurrent active power kinds. Redline combines food count with safe progress toward the exact pre-step visible food.
Live style values are rules-advanced-step observations and may rise or fall. Rate criteria expose integer numerators and denominators and use floor basis points. Successful finalization independently reconstructs the same facts from the verified replay, requires agreement with live evidence, and binds the terminal style outcome to the replay payload hash. These facts do not prove intent, planning, mastery, personality, or spectator appeal. A style never changes rules, scoring, spawn order, or replay verification.

# Signal School

@@LESSON_IDS@@

Call `start_lesson` with one of eight published lesson IDs to create its canonical open-seed practice session. Every definition publishes ordered closed requirements under `ordered-replay-attempt-evidence-v2`; observations return live requirement progress and the first unmet requirement, accepted moves and bursts return exact progress deltas, and verified finalization returns a factual outcome. A completed practice is not mastery or qualification.
Accepted-step facts are independently reconstructed from the verified replay. The rejection-aware first-turn lesson additionally uses a maximum-32 canonical attempt-witness sequence: exact idempotent retries do not add evidence, and stale, conflicting, capacity, or wrong-profile requests cannot qualify. The outcome binds the replay payload hash and distinct attempt-evidence hash into one evidence hash. An ordinary saved replay contains only accepted-step history, so it cannot later prove the rejected reversal without a future receipt that carries the attempt evidence.
A verified miss names the first unmet requirement and a closed review code. Failed-closed evidence produces no verified lesson outcome and directs the client to a fresh same-lesson `start_lesson` session without inherited rules state, mutation keys, or practice history. The resource also publishes exact action-call and UTF-8 byte measurements from checked-in canonical routes; these are evidence, not product-wide limits. Byte accounting covers each exact camelCase MCP tool arguments object and snake_case structured response only; it excludes MCP framing, logs, viewer traffic, and token estimates. Bounded straight-line burst fixtures choose an observation-derived bound from 1 through 16, never exceed the paired step route's action-call count, and reduce calls for at least six of eight lessons. Checked-in non-practice seeds are the public qualification-time lesson deck; they are not secret and they are not mastery.
""";

    private const string ReplaysTemplate = """
---
type: "Replay Contract"
title: "Verified agent replay handoff"
description: "How successfully finalized agent play becomes a verified result and human-watchable replay."
tags: [vibesnake, replay, verification, spectator]
generated: { by: process:vibesnake-okf-generator, at: @@GENERATED_AT@@ }
verified: { by: process:vibesnake-quality-gate, at: @@VERIFIED_AT@@ }
stale_after: "@@STALE_AFTER@@"
status: draft
sources:
  - id: agent-session
    resource: ../../native/src/VibeSnake.AgentPlay/AgentMatchSession.cs
    title: "Agent match owner"
  - id: replay-store
    resource: ../../native/src/VibeSnake.Persistence/ReplayStore.cs
    title: "Bounded replay store"
  - id: replay-doc
    resource: ../../docs/engineering/REPLAYS.md
    title: "Replay engineering contract"
---
# Verified result

A successfully finalized completed, capped, or explicitly finished match returns `@@RESULT_SCHEMA@@` with final state hash, replay payload hash, rules and mode identity, outcome, metrics, and verification code. A styled result carries exactly two criterion outcomes independently reconstructed from and bound to that verified replay. A Signal School result carries ordered requirement outcomes, a factual review, the replay payload hash, a distinct bounded attempt-evidence hash, and their aggregate evidence hash. Failed-closed finalization returns neither a verified result, a style or lesson outcome, nor a verified replay.

# Persistence

Replay saving is an explicit call into the bounded application-owned replay store. The agent supplies no path. The saved file is reloaded and verified before the existing replay presentation consumes it. Replay schema 1 stores accepted rules steps only; the bounded Signal School attempt witnesses remain ephemeral host result evidence until a future exhibition receipt explicitly persists both evidence domains.

# Human viewing

The same replay browser and clock-free playback used for human runs can play the agent action trace at a human-selected pace. Playback presentation cannot alter the canonical final hash.
""";
}
