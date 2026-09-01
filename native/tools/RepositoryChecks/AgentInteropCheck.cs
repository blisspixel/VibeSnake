using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace RepositoryChecks;

public sealed record AgentContractDigests(string Host, string Plugin);

public static class AgentInteropCheck
{
    public const string BaselinePath = "integrations/agent-interop-baseline.json";

    private const string ProgramPath = "native/tools/VibeSnake.AgentHost/Program.cs";
    private const string HostProjectPath =
        "native/tools/VibeSnake.AgentHost/VibeSnake.AgentHost.csproj";
    private const string PluginPath = "integrations/vibesnake-agent-plugin/plugin.json";
    private const string PackageScriptPath = "scripts/package_agent_plugin.ps1";
    private const string EngineeringDocumentPath = "docs/engineering/AGENT_PLAY.md";
    private const string ExpectedSchema = "vibesnake-agent-interop-baseline-v1";

    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    private static readonly JsonSerializerOptions IndentedJsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true,
    };

    private static readonly Regex Sha256Pattern = new(
        "^[0-9a-f]{64}$",
        RegexOptions.CultureInvariant);

    private static readonly Regex SemVerPattern = new(
        "^[0-9]+\\.[0-9]+\\.[0-9]+$",
        RegexOptions.CultureInvariant);

    private static readonly string[] RootKeys =
    [
        "schema",
        "reviewed_on",
        "next_review_on",
        "mcp",
        "agent_plugins",
        "agent_skill",
        "okf",
        "mcp_apps",
        "public_contract_history",
    ];

    private static readonly string[] McpKeys =
    [
        "protocol_version",
        "sdk_package",
        "sdk_version",
        "host_version",
        "transport",
        "session_model",
    ];

    private static readonly string[] PluginKeys =
    [
        "spec_version",
        "normative_status",
        "website_status",
        "spec_source_commit",
        "spec_source_url",
        "spec_source_sha256",
        "plugin_version",
        "plugin_schema_url",
        "plugin_schema_sha256",
        "mcp_schema_url",
        "mcp_schema_sha256",
    ];

    private static readonly string[] HostContractPaths =
    [
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
        ProgramPath,
    ];

    private static readonly string[] PluginContractPaths =
    [
        "integrations/vibesnake-agent-plugin/skills/play-vibesnake/SKILL.md",
        PackageScriptPath,
    ];

    public static RepositoryCheckResult Inspect(string repositoryRoot) =>
        Inspect(
            repositoryRoot,
            DateOnly.FromDateTime(TimeProvider.System.GetUtcNow().UtcDateTime));

    public static RepositoryCheckResult WriteDigests(string repositoryRoot)
    {
        try
        {
            var digests = CalculateContractDigests(repositoryRoot);
            var bytes = FixedCanonicalFixtureFile.Read(
                repositoryRoot,
                BaselinePath,
                "interoperability baseline");
            using (var strictDocument = ParseJson(bytes, "interoperability baseline"))
            {
                _ = RequireObject(strictDocument.RootElement, "baseline");
            }

            var baseline = JsonNode.Parse(
                bytes,
                nodeOptions: null,
                documentOptions: StrictDocumentOptions()) as JsonObject
                ?? throw new InvalidDataException("baseline must be a JSON object");
            var mcp = RequireObjectNode(baseline, "mcp", "baseline");
            var plugins = RequireObjectNode(baseline, "agent_plugins", "baseline");
            var history = RequireObjectNode(baseline, "public_contract_history", "baseline");
            PatchLatestDigest(
                history,
                "host",
                RequireStringNode(mcp, "host_version", "mcp"),
                digests.Host);
            PatchLatestDigest(
                history,
                "plugin",
                RequireStringNode(plugins, "plugin_version", "agent_plugins"),
                digests.Plugin);

            var rendered = Encoding.UTF8.GetBytes(
                StrictUtf8.GetString(
                    JsonSerializer.SerializeToUtf8Bytes(
                        baseline,
                        IndentedJsonOptions))
                    .Replace("\r\n", "\n", StringComparison.Ordinal));
            var canonical = new byte[rendered.Length + 1];
            rendered.CopyTo(canonical, 0);
            canonical[^1] = (byte)'\n';

            var rechecked = CalculateContractDigests(repositoryRoot);
            if (rechecked != digests)
            {
                throw new InvalidDataException(
                    "public contract sources changed while digests were being updated");
            }

            FixedCanonicalFixtureFile.Write(
                repositoryRoot,
                BaselinePath,
                "interoperability baseline",
                canonical);
            var verification = Inspect(repositoryRoot);
            if (!verification.Passed)
            {
                return new RepositoryCheckResult(
                    "Agent interoperability",
                    false,
                    string.Empty,
                    verification.Failures
                        .Select(failure => "write verification failed: " + failure)
                        .ToArray());
            }

            return Passed("written", digests);
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            return Failed(FixedCanonicalFixtureFile.SingleLine(exception.Message));
        }
    }

    public static AgentContractDigests CalculateContractDigests(string repositoryRoot)
    {
        var program = ReadText(repositoryRoot, ProgramPath, "Agent Host program");
        var protocol = MatchSingle(
            program,
            "McpProtocolVersion = \\\"([^\\\"]+)\\\"",
            "MCP protocol");

        using var hostHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendUtf8(hostHash, $"protocol={protocol}\n");
        foreach (var path in HostContractPaths)
        {
            AppendUtf8(hostHash, path + "\n");
            AppendUtf8(hostHash, NormalizeSource(ReadText(repositoryRoot, path, "host contract source")));
        }

        using var pluginDocument = ParseJson(repositoryRoot, PluginPath, "plugin manifest");
        var pluginRoot = RequireObject(pluginDocument.RootElement, "plugin manifest");
        using var pluginHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        pluginHash.AppendData(RenderCanonicalJson(pluginRoot, excludedRootProperty: "version"));
        foreach (var path in PluginContractPaths)
        {
            AppendUtf8(pluginHash, path + "\n");
            AppendUtf8(pluginHash, NormalizeSource(ReadText(repositoryRoot, path, "plugin contract source")));
        }

        return new AgentContractDigests(
            Convert.ToHexStringLower(hostHash.GetHashAndReset()),
            Convert.ToHexStringLower(pluginHash.GetHashAndReset()));
    }

    internal static RepositoryCheckResult Inspect(
        string repositoryRoot,
        DateOnly asOf)
    {
        try
        {
            var failures = new List<string>();
            using var document = ParseJson(
                repositoryRoot,
                BaselinePath,
                "interoperability baseline");
            var baseline = RequireObject(document.RootElement, "baseline");
            RequireExactKeys(baseline, RootKeys, "baseline", failures);
            RequireValue(baseline, "schema", ExpectedSchema, "schema", failures);

            var reviewed = ReadDate(baseline, "reviewed_on", "reviewed_on", failures);
            var nextReview = ReadDate(baseline, "next_review_on", "next_review_on", failures);
            if (reviewed is not null && nextReview is not null && reviewed >= nextReview)
            {
                failures.Add("next_review_on must be after reviewed_on");
            }

            if (nextReview is not null && asOf >= nextReview)
            {
                failures.Add(
                    $"interoperability baseline is stale: as-of {asOf:yyyy-MM-dd} reached "
                    + $"next_review_on {nextReview:yyyy-MM-dd}");
            }

            var mcp = ReadObject(baseline, "mcp", "mcp", failures);
            var plugins = ReadObject(
                baseline,
                "agent_plugins",
                "agent_plugins",
                failures);
            var skill = ReadObject(baseline, "agent_skill", "agent_skill", failures);
            var okf = ReadObject(baseline, "okf", "okf", failures);
            var apps = ReadObject(baseline, "mcp_apps", "mcp_apps", failures);
            var history = ReadObject(
                baseline,
                "public_contract_history",
                "public_contract_history",
                failures);

            RequireExactKeys(mcp, McpKeys, "mcp", failures);
            RequireExactKeys(plugins, PluginKeys, "agent_plugins", failures);
            RequireExactKeys(skill, ["profile", "fields"], "agent_skill", failures);
            RequireExactKeys(
                okf,
                ["spec_version", "generated_at", "verified_at", "stale_after"],
                "okf",
                failures);
            RequireExactKeys(apps, ["tracked_version", "status"], "mcp_apps", failures);
            RequireExactKeys(
                history,
                ["host", "plugin"],
                "public_contract_history",
                failures);

            RequireValue(mcp, "sdk_package", "ModelContextProtocol", "mcp.sdk_package", failures);
            RequireValue(mcp, "transport", "stdio", "mcp.transport", failures);
            RequireValue(mcp, "session_model", "stateless", "mcp.session_model", failures);
            RequireValue(
                plugins,
                "spec_version",
                "1.0.0",
                "agent_plugins.spec_version",
                failures);
            RequireValue(
                plugins,
                "normative_status",
                "published",
                "agent_plugins.normative_status",
                failures);
            RequireValue(
                plugins,
                "website_status",
                "working-draft",
                "agent_plugins.website_status",
                failures);
            RequireValue(
                skill,
                "profile",
                "minimal-non-experimental",
                "agent_skill.profile",
                failures);
            RequireValue(okf, "spec_version", "0.2", "okf.spec_version", failures);
            RequireValue(apps, "status", "tracked-only", "mcp_apps.status", failures);
            RequireStringArray(
                skill,
                "fields",
                ["name", "description", "markdown-body"],
                "agent_skill.fields",
                failures);

            ValidateSemVer(mcp, "host_version", "mcp.host_version", failures);
            ValidateSemVer(mcp, "sdk_version", "mcp.sdk_version", failures);
            ValidateSemVer(
                plugins,
                "plugin_version",
                "agent_plugins.plugin_version",
                failures);

            var staleAfter = ReadDate(okf, "stale_after", "okf.stale_after", failures);
            if (staleAfter is not null
                && nextReview is not null
                && staleAfter != nextReview)
            {
                failures.Add("okf.stale_after must equal next_review_on");
            }

            ValidateUtcTimestamp(okf, "generated_at", "okf.generated_at", failures);
            ValidateUtcTimestamp(okf, "verified_at", "okf.verified_at", failures);
            ValidatePluginPins(plugins, failures);
            ValidateSourceAlignment(repositoryRoot, mcp, plugins, failures);

            var digests = CalculateContractDigests(repositoryRoot);
            CheckHistory(
                history,
                "host",
                GetString(mcp, "host_version"),
                digests.Host,
                failures);
            CheckHistory(
                history,
                "plugin",
                GetString(plugins, "plugin_version"),
                digests.Plugin,
                failures);
            ValidateDocumentation(repositoryRoot, baseline, mcp, plugins, skill, okf, apps, failures);

            return failures.Count == 0
                ? Passed("verified", digests)
                : new RepositoryCheckResult(
                    "Agent interoperability",
                    false,
                    string.Empty,
                    failures);
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            return Failed(FixedCanonicalFixtureFile.SingleLine(exception.Message));
        }
    }

    private static void ValidatePluginPins(JsonElement plugins, List<string> failures)
    {
        var sourceCommit = GetString(plugins, "spec_source_commit");
        if (sourceCommit is null
            || !Regex.IsMatch(
                sourceCommit,
                "^[0-9a-f]{40}$",
                RegexOptions.CultureInvariant))
        {
            failures.Add(
                "agent_plugins.spec_source_commit must be a full lowercase Git commit SHA");
        }

        var expectedUrl =
            "https://raw.githubusercontent.com/agentplugins/agent-plugins-spec/"
            + $"{sourceCommit}/spec/{GetString(plugins, "spec_version")}.md";
        if (!string.Equals(
            GetString(plugins, "spec_source_url"),
            expectedUrl,
            StringComparison.Ordinal))
        {
            failures.Add(
                "agent_plugins.spec_source_url must bind the reviewed version to its immutable commit");
        }

        foreach (var name in new[]
        {
            "spec_source_sha256",
            "plugin_schema_sha256",
            "mcp_schema_sha256",
        })
        {
            var value = GetString(plugins, name);
            if (value is null || !Sha256Pattern.IsMatch(value))
            {
                failures.Add(
                    $"agent_plugins.{name} must be a lowercase SHA-256 digest");
            }
        }
    }

    private static void ValidateSourceAlignment(
        string repositoryRoot,
        JsonElement mcp,
        JsonElement plugins,
        List<string> failures)
    {
        var program = ReadText(repositoryRoot, ProgramPath, "Agent Host program");
        var project = ReadText(repositoryRoot, HostProjectPath, "Agent Host project");
        var packageScript = ReadText(
            repositoryRoot,
            PackageScriptPath,
            "Agent Plugin package script");
        using var pluginDocument = ParseJson(repositoryRoot, PluginPath, "plugin manifest");
        var plugin = RequireObject(pluginDocument.RootElement, "plugin manifest");

        CompareSource(
            "mcp.protocol_version",
            GetString(mcp, "protocol_version"),
            MatchSingle(program, "McpProtocolVersion = \\\"([^\\\"]+)\\\"", "MCP protocol"),
            failures);
        CompareSource(
            "mcp.host_version",
            GetString(mcp, "host_version"),
            MatchSingle(program, "HostVersion = \\\"([^\\\"]+)\\\"", "host version"),
            failures);
        CompareSource(
            "mcp.sdk_version",
            GetString(mcp, "sdk_version"),
            MatchSingle(
                project,
                "<PackageReference Include=\\\"ModelContextProtocol\\\" Version=\\\"([^\\\"]+)\\\"",
                "MCP SDK version"),
            failures);
        CompareSource(
            "agent_plugins.plugin_version",
            GetString(plugins, "plugin_version"),
            GetString(plugin, "version"),
            failures);
        CompareSource(
            "agent_plugins.plugin_schema_url",
            GetString(plugins, "plugin_schema_url"),
            GetString(plugin, "$schema"),
            failures);
        CompareSource(
            "agent_plugins.mcp_schema_url",
            GetString(plugins, "mcp_schema_url"),
            MatchSingle(
                packageScript,
                "'\\$schema'\\s*=\\s*\\\"(https://agent-plugins\\.org/schemas/[^\\\"]+/mcp\\.schema\\.json)\\\"",
                "assembled MCP schema URL"),
            failures);
    }

    private static void ValidateDocumentation(
        string repositoryRoot,
        JsonElement baseline,
        JsonElement mcp,
        JsonElement plugins,
        JsonElement skill,
        JsonElement okf,
        JsonElement apps,
        List<string> failures)
    {
        var document = ReadText(
            repositoryRoot,
            EngineeringDocumentPath,
            "Agent Play engineering document");
        var values = new[]
        {
            GetString(mcp, "protocol_version"),
            GetString(mcp, "sdk_version"),
            GetString(mcp, "host_version"),
            GetString(plugins, "spec_version"),
            GetString(plugins, "plugin_version"),
            GetString(plugins, "plugin_schema_sha256"),
            GetString(plugins, "mcp_schema_sha256"),
            GetString(okf, "spec_version"),
            GetString(baseline, "reviewed_on"),
            GetString(plugins, "normative_status"),
            GetString(plugins, "website_status"),
            GetString(plugins, "spec_source_commit"),
            GetString(plugins, "spec_source_sha256"),
            GetString(skill, "profile"),
            GetString(apps, "tracked_version"),
        };
        foreach (var value in values)
        {
            if (value is null || !document.Contains(value, StringComparison.Ordinal))
            {
                failures.Add(
                    $"AGENT_PLAY.md does not publish baseline value {value ?? "<non-string>"}");
            }
        }

        foreach (var forbidden in new[] { "initialize with exactly", "stable initialize revision" })
        {
            if (document.Contains(forbidden, StringComparison.Ordinal))
            {
                failures.Add($"AGENT_PLAY.md contains obsolete MCP wording: {forbidden}");
            }
        }
    }

    private static void CheckHistory(
        JsonElement history,
        string kind,
        string? currentVersion,
        string currentDigest,
        List<string> failures)
    {
        if (!history.TryGetProperty(kind, out var entries)
            || entries.ValueKind != JsonValueKind.Array
            || entries.GetArrayLength() == 0)
        {
            failures.Add($"public_contract_history.{kind} must be a nonempty array");
            return;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        SemVerCore? previous = null;
        var index = 0;
        foreach (var entry in entries.EnumerateArray())
        {
            var field = $"public_contract_history.{kind}[{index}]";
            if (entry.ValueKind != JsonValueKind.Object)
            {
                failures.Add($"{field} must be an object");
                index++;
                continue;
            }

            RequireExactKeys(entry, ["version", "sha256"], field, failures);
            var version = GetString(entry, "version");
            var parsed = ParseSemVer(version);
            if (parsed is null)
            {
                failures.Add($"{field}.version must be SemVer core");
            }
            else if (!seen.Add(version!))
            {
                failures.Add($"{field}.version must be unique");
            }
            else
            {
                if (previous is not null && parsed.Value.CompareTo(previous.Value) <= 0)
                {
                    failures.Add(
                        $"{field}.version must be greater than the preceding history version");
                }

                previous = parsed;
            }

            var digest = GetString(entry, "sha256");
            if (digest is null || !Sha256Pattern.IsMatch(digest))
            {
                failures.Add($"{field}.sha256 must be a lowercase SHA-256 digest");
            }

            index++;
        }

        var latest = entries[entries.GetArrayLength() - 1];
        if (latest.ValueKind == JsonValueKind.Object)
        {
            if (!string.Equals(
                GetString(latest, "version"),
                currentVersion,
                StringComparison.Ordinal))
            {
                failures.Add(
                    $"public_contract_history.{kind} latest version must match '{currentVersion}'");
            }

            if (!string.Equals(
                GetString(latest, "sha256"),
                currentDigest,
                StringComparison.Ordinal))
            {
                failures.Add(
                    $"public {kind} contract changed without a matching versioned digest entry");
            }
        }
    }

    private static byte[] RenderCanonicalJson(
        JsonElement root,
        string excludedRootProperty)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(
            stream,
            new JsonWriterOptions
            {
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                Indented = false,
            }))
        {
            WriteCanonical(root, writer, excludedRootProperty, isRoot: true);
            writer.Flush();
        }

        return stream.ToArray();
    }

    private static void WriteCanonical(
        JsonElement element,
        Utf8JsonWriter writer,
        string excludedRootProperty,
        bool isRoot)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject()
                    .Where(property => !isRoot || property.Name != excludedRootProperty)
                    .OrderBy(property => property.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonical(
                        property.Value,
                        writer,
                        excludedRootProperty,
                        isRoot: false);
                }

                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                {
                    WriteCanonical(item, writer, excludedRootProperty, isRoot: false);
                }

                writer.WriteEndArray();
                break;
            case JsonValueKind.String:
                writer.WriteStringValue(element.GetString());
                break;
            case JsonValueKind.Number:
                writer.WriteRawValue(element.GetRawText(), skipInputValidation: false);
                break;
            case JsonValueKind.True:
                writer.WriteBooleanValue(true);
                break;
            case JsonValueKind.False:
                writer.WriteBooleanValue(false);
                break;
            case JsonValueKind.Null:
                writer.WriteNullValue();
                break;
            default:
                throw new InvalidDataException("plugin manifest contains an unsupported JSON value");
        }
    }

    private static void PatchLatestDigest(
        JsonObject history,
        string kind,
        string currentVersion,
        string digest)
    {
        if (history[kind] is not JsonArray entries || entries.Count == 0)
        {
            throw new InvalidDataException(
                $"public_contract_history.{kind} must be a nonempty array");
        }

        if (entries[^1] is not JsonObject latest)
        {
            throw new InvalidDataException(
                $"public_contract_history.{kind} latest entry must be an object");
        }

        var version = latest["version"] is JsonValue value
            && value.TryGetValue<string>(out var text)
                ? text
                : null;
        if (!string.Equals(version, currentVersion, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"public_contract_history.{kind} latest version is '{version}', "
                + $"expected '{currentVersion}'");
        }

        latest["sha256"] = digest;
    }

    private static JsonObject RequireObjectNode(
        JsonObject parent,
        string propertyName,
        string label) =>
        parent[propertyName] as JsonObject
        ?? throw new InvalidDataException($"{label}.{propertyName} must be an object");

    private static string RequireStringNode(
        JsonObject parent,
        string propertyName,
        string label)
    {
        if (parent[propertyName] is JsonValue value
            && value.TryGetValue<string>(out var text))
        {
            return text;
        }

        throw new InvalidDataException($"{label}.{propertyName} must be a string");
    }

    private static void CompareSource(
        string field,
        string? baselineValue,
        string? sourceValue,
        List<string> failures)
    {
        if (!string.Equals(baselineValue, sourceValue, StringComparison.Ordinal))
        {
            failures.Add(
                $"{field}='{baselineValue}' does not match canonical source '{sourceValue}'");
        }
    }

    private static void RequireValue(
        JsonElement parent,
        string propertyName,
        string expected,
        string field,
        List<string> failures)
    {
        if (!string.Equals(
            GetString(parent, propertyName),
            expected,
            StringComparison.Ordinal))
        {
            failures.Add($"{field} must be '{expected}'");
        }
    }

    private static void RequireStringArray(
        JsonElement parent,
        string propertyName,
        IReadOnlyList<string> expected,
        string field,
        List<string> failures)
    {
        if (!parent.TryGetProperty(propertyName, out var value)
            || value.ValueKind != JsonValueKind.Array
            || !value.EnumerateArray().Select(item => item.ValueKind == JsonValueKind.String
                ? item.GetString()
                : null).SequenceEqual(expected))
        {
            failures.Add($"{field} must be the reviewed minimal ordered field set");
        }
    }

    private static void ValidateSemVer(
        JsonElement parent,
        string propertyName,
        string field,
        List<string> failures)
    {
        if (ParseSemVer(GetString(parent, propertyName)) is null)
        {
            failures.Add($"{field} must be SemVer core");
        }
    }

    private static SemVerCore? ParseSemVer(string? value)
    {
        if (value is null || !SemVerPattern.IsMatch(value))
        {
            return null;
        }

        var parts = value.Split('.');
        return new SemVerCore(
            BigInteger.Parse(parts[0], CultureInfo.InvariantCulture),
            BigInteger.Parse(parts[1], CultureInfo.InvariantCulture),
            BigInteger.Parse(parts[2], CultureInfo.InvariantCulture));
    }

    private static DateOnly? ReadDate(
        JsonElement parent,
        string propertyName,
        string field,
        List<string> failures)
    {
        var value = GetString(parent, propertyName);
        if (value is null
            || !DateOnly.TryParseExact(
                value,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var parsed)
            || !string.Equals(
                parsed.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                value,
                StringComparison.Ordinal))
        {
            failures.Add($"{field} must be an absolute YYYY-MM-DD date");
            return null;
        }

        return parsed;
    }

    private static void ValidateUtcTimestamp(
        JsonElement parent,
        string propertyName,
        string field,
        List<string> failures)
    {
        var value = GetString(parent, propertyName);
        if (value is null
            || !DateTimeOffset.TryParseExact(
                value,
                "yyyy-MM-dd'T'HH:mm:ss'Z'",
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsed)
            || !string.Equals(
                parsed.ToString(
                    "yyyy-MM-dd'T'HH:mm:ss'Z'",
                    CultureInfo.InvariantCulture),
                value,
                StringComparison.Ordinal))
        {
            failures.Add($"{field} must be a canonical RFC 3339 UTC datetime");
        }
    }

    private static JsonElement ReadObject(
        JsonElement parent,
        string propertyName,
        string field,
        List<string> failures)
    {
        if (parent.TryGetProperty(propertyName, out var value)
            && value.ValueKind == JsonValueKind.Object)
        {
            return value;
        }

        failures.Add($"{field} must be an object");
        using var empty = JsonDocument.Parse("{}");
        return empty.RootElement.Clone();
    }

    private static void RequireExactKeys(
        JsonElement value,
        IReadOnlyCollection<string> expected,
        string field,
        List<string> failures)
    {
        var actual = value.ValueKind == JsonValueKind.Object
            ? value.EnumerateObject().Select(property => property.Name).ToHashSet(StringComparer.Ordinal)
            : [];
        if (!actual.SetEquals(expected))
        {
            failures.Add(
                $"{field} keys must be exactly [{string.Join(", ", expected.Order(StringComparer.Ordinal))}]; "
                + $"got [{string.Join(", ", actual.Order(StringComparer.Ordinal))}]");
        }
    }

    private static string? GetString(JsonElement parent, string propertyName) =>
        parent.ValueKind == JsonValueKind.Object
        && parent.TryGetProperty(propertyName, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static JsonElement RequireObject(JsonElement element, string label)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException($"{label} must be a JSON object");
        }

        return element;
    }

    private static string ReadText(
        string repositoryRoot,
        string relativePath,
        string label) =>
        StrictUtf8.GetString(
            FixedCanonicalFixtureFile.Read(repositoryRoot, relativePath, label));

    private static string NormalizeSource(string source) =>
        source.Replace("\r\n", "\n", StringComparison.Ordinal);

    private static void AppendUtf8(IncrementalHash hash, string value) =>
        hash.AppendData(Encoding.UTF8.GetBytes(value));

    private static string MatchSingle(string source, string pattern, string label)
    {
        var matches = Regex.Matches(source, pattern, RegexOptions.CultureInvariant);
        if (matches.Count != 1)
        {
            throw new InvalidDataException(
                $"could not extract exactly one {label} from its canonical source; found {matches.Count}");
        }

        return matches[0].Groups[1].Value;
    }

    private static JsonDocument ParseJson(
        string repositoryRoot,
        string relativePath,
        string label) =>
        ParseJson(
            FixedCanonicalFixtureFile.Read(repositoryRoot, relativePath, label),
            label);

    private static JsonDocument ParseJson(ReadOnlyMemory<byte> bytes, string label)
    {
        var document = JsonDocument.Parse(bytes, StrictDocumentOptions());
        RejectDuplicateProperties(document.RootElement, label);
        return document;
    }

    private static JsonDocumentOptions StrictDocumentOptions() =>
        new()
        {
            AllowTrailingCommas = false,
            CommentHandling = JsonCommentHandling.Disallow,
            MaxDepth = 32,
        };

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

    private static RepositoryCheckResult Passed(
        string action,
        AgentContractDigests digests) =>
        new(
            "Agent interoperability",
            true,
            $"Agent interoperability baseline {action}: host={digests.Host} plugin={digests.Plugin}.",
            []);

    private static RepositoryCheckResult Failed(string failure) =>
        new("Agent interoperability", false, string.Empty, [failure]);

    private static bool IsExpectedFailure(Exception exception) =>
        FixedCanonicalFixtureFile.IsExpectedFailure(exception)
        || exception is JsonException
        || exception is DecoderFallbackException
        || exception is FormatException;

    private readonly record struct SemVerCore(
        BigInteger Major,
        BigInteger Minor,
        BigInteger Patch) : IComparable<SemVerCore>
    {
        public int CompareTo(SemVerCore other)
        {
            var major = Major.CompareTo(other.Major);
            if (major != 0)
            {
                return major;
            }

            var minor = Minor.CompareTo(other.Minor);
            return minor != 0 ? minor : Patch.CompareTo(other.Patch);
        }
    }
}
