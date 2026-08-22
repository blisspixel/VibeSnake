using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace RepositoryChecks;

public static class AgentPluginCheck
{
    public const string PluginSchema =
        "https://agent-plugins.org/schemas/1.0.0/plugin.schema.json";
    public const string McpSchema =
        "https://agent-plugins.org/schemas/1.0.0/mcp.schema.json";
    public const string PackagedServerName = "vibesnake-agent";
    public const string PackagedHostArgument =
        "${PLUGIN_ROOT}/bin/VibeSnake.AgentHost.dll";

    private const int MaximumTreeEntries = 4096;
    private const long MaximumManifestBytes = 128 * 1024;
    private const long MaximumMcpBytes = 1024 * 1024;
    private const long MaximumSkillBytes = 1024 * 1024;
    private const long MaximumChecksumBytes = 8 * 1024 * 1024;

    private static readonly HashSet<string> PluginFields = new(
        [
            "$schema",
            "name",
            "version",
            "description",
            "author",
            "homepage",
            "repository",
            "license",
            "keywords",
            "extensions",
        ],
        StringComparer.Ordinal);

    private static readonly HashSet<string> SkillFields = new(
        ["name", "description"],
        StringComparer.Ordinal);

    private static readonly HashSet<string> StdioFields = new(
        ["type", "command", "args", "env", "cwd"],
        StringComparer.Ordinal);

    private static readonly string[] PackagedRequiredFiles =
    [
        "plugin.json",
        "mcp.json",
        "skills/play-vibesnake/SKILL.md",
        "LICENSE",
        "NOTICE",
        "bin/VibeSnake.AgentHost.dll",
    ];

    private static readonly Regex PluginNamePattern = new(
        @"^[a-z0-9](?:[a-z0-9.-]{0,62}[a-z0-9])?$",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    private static readonly Regex SkillNamePattern = new(
        @"^[a-z0-9](?:[a-z0-9-]{0,62}[a-z0-9])?$",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    private static readonly Regex CoreSemVerPattern = new(
        @"^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    private static readonly Regex Sha256Pattern = new(
        "^[0-9a-f]{64}$",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    private static readonly Regex NumericYamlScalarPattern = new(
        @"^[+-]?(?:0b[0-1_]+|0x[0-9a-fA-F_]+|0[0-7_]+|[0-9][0-9_]*(?::[0-5]?[0-9])+|[0-9][0-9_]*(?:\.[0-9_]*)?(?:[eE][+-]?[0-9]+)?|\.[0-9_]+(?:[eE][+-]?[0-9]+)?|\.(?:inf|nan))$",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.NonBacktracking);

    private static readonly Regex TimestampYamlScalarPattern = new(
        @"^[0-9]{4}-[0-9]{1,2}-[0-9]{1,2}(?:[Tt]|[ \t]+)[0-9]{1,2}:[0-9]{2}:[0-9]{2}(?:\.[0-9_]*)?(?:[ \t]*(?:Z|[-+][0-9]{1,2}(?::[0-9]{2})?))?$|^[0-9]{4}-[0-9]{1,2}-[0-9]{1,2}$",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    private static readonly HashSet<string> ImplicitYamlScalars = new(
        ["y", "yes", "n", "no", "true", "false", "on", "off", "null", "~"],
        StringComparer.OrdinalIgnoreCase);

    public static RepositoryCheckResult Inspect(string pluginRoot, bool requireMcp = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginRoot);

        string root;
        try
        {
            root = Path.GetFullPath(pluginRoot);
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return Failed(["plugin root is invalid"]);
        }

        if (!Directory.Exists(root))
        {
            return Failed(["plugin root must be an existing directory"]);
        }

        var problems = new List<string>();
        var inventory = BuildInventory(root, problems);
        ValidateManifest(root, inventory.Files, problems);
        ValidateSkills(root, inventory.Files, problems);
        ValidateMcp(root, inventory.Files, requireMcp, problems);
        if (requireMcp)
        {
            ValidatePackagedComponents(inventory.Files, problems);
        }

        ValidateChecksums(root, inventory.Files, requireMcp, problems);

        var failures = problems
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        return failures.Length == 0
            ? new RepositoryCheckResult(
                "Agent Plugin",
                true,
                requireMcp
                    ? $"Agent Plugin packaged profile passed for {inventory.Files.Count} files."
                    : $"Agent Plugin source profile passed for {inventory.Files.Count} files.",
                [])
            : Failed(failures);
    }

    private static PluginInventory BuildInventory(string root, List<string> problems)
    {
        var files = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<string>();
        pending.Push(root);
        var entryCount = 0;

        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            string[] entries;
            try
            {
                entries = Directory.GetFileSystemEntries(directory);
                Array.Sort(entries, StringComparer.Ordinal);
            }
            catch (Exception exception) when (
                exception is IOException
                    or UnauthorizedAccessException
                    or NotSupportedException)
            {
                problems.Add(
                    $"{RelativePath(root, directory)}: could not enumerate plugin content: "
                    + SingleLine(exception.Message));
                continue;
            }

            for (var index = entries.Length - 1; index >= 0; index--)
            {
                var entry = entries[index];
                entryCount++;
                if (entryCount > MaximumTreeEntries)
                {
                    problems.Add(
                        $"plugin tree exceeds the {MaximumTreeEntries}-entry validation limit");
                    return new PluginInventory(files);
                }

                var relative = RelativePath(root, entry);
                FileAttributes attributes;
                try
                {
                    attributes = File.GetAttributes(entry);
                }
                catch (Exception exception) when (
                    exception is IOException
                        or UnauthorizedAccessException
                        or NotSupportedException)
                {
                    problems.Add(
                        $"{relative}: could not inspect plugin content: "
                        + SingleLine(exception.Message));
                    continue;
                }

                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    problems.Add($"{relative}: links are not allowed");
                    continue;
                }

                if ((attributes & FileAttributes.Directory) != 0)
                {
                    pending.Push(entry);
                    continue;
                }

                if (!File.Exists(entry) || !IsContained(root, entry))
                {
                    problems.Add($"{relative}: unsupported or escaping file entry");
                    continue;
                }

                files.Add(relative);
            }
        }

        return new PluginInventory(files);
    }

    private static void ValidateManifest(
        string root,
        IReadOnlySet<string> files,
        List<string> problems)
    {
        const string relativePath = "plugin.json";
        if (!files.Contains(relativePath))
        {
            problems.Add("plugin.json: required regular file is missing");
            return;
        }

        using var document = LoadJsonObject(
            root,
            relativePath,
            MaximumManifestBytes,
            problems);
        if (document is null)
        {
            return;
        }

        var value = document.RootElement;
        RejectUnknown(value, PluginFields, "plugin.json", problems);
        RequireExactString(
            value,
            "$schema",
            PluginSchema,
            "plugin.json: unsupported or missing Agent Plugins schema",
            problems);

        if (!TryGetString(value, "name", out var name)
            || !PluginNamePattern.IsMatch(name)
            || name.Contains("--", StringComparison.Ordinal)
            || name.Contains("..", StringComparison.Ordinal))
        {
            problems.Add("plugin.json: name violates Agent Plugins 1.0.0 constraints");
        }

        if (!TryGetString(value, "version", out var version)
            || !CoreSemVerPattern.IsMatch(version))
        {
            problems.Add("plugin.json: version must be a canonical SemVer core");
        }

        if (!TryGetString(value, "description", out var description)
            || description.EnumerateRunes().Count() is < 1 or > 1024)
        {
            problems.Add("plugin.json: description must contain 1 through 1024 characters");
        }

        foreach (var field in new[] { "homepage", "repository", "license" })
        {
            if (value.TryGetProperty(field, out var item)
                && item.ValueKind != JsonValueKind.String)
            {
                problems.Add($"plugin.json: {field} must be a string");
            }
        }

        if (value.TryGetProperty("author", out var author))
        {
            ValidateAuthor(author, problems);
        }

        if (value.TryGetProperty("keywords", out var keywords)
            && (keywords.ValueKind != JsonValueKind.Array
                || keywords.EnumerateArray().Any(item => item.ValueKind != JsonValueKind.String)))
        {
            problems.Add("plugin.json: keywords must be an array of strings");
        }

        if (value.TryGetProperty("extensions", out var extensions)
            && (extensions.ValueKind != JsonValueKind.Object
                || extensions.EnumerateObject().Any(
                    property => property.Value.ValueKind != JsonValueKind.Object)))
        {
            problems.Add("plugin.json: extensions must map namespaces to objects");
        }
    }

    private static void ValidateAuthor(JsonElement author, List<string> problems)
    {
        if (author.ValueKind != JsonValueKind.Object)
        {
            problems.Add("plugin.json: author must be an object");
            return;
        }

        RejectUnknown(
            author,
            new HashSet<string>(["name", "email", "url"], StringComparer.Ordinal),
            "plugin.json author",
            problems);
        if (author.EnumerateObject().Any(
            property => property.Value.ValueKind != JsonValueKind.String))
        {
            problems.Add("plugin.json: author values must be strings");
        }
    }

    private static void ValidateSkills(
        string root,
        IReadOnlySet<string> files,
        List<string> problems)
    {
        var skillsPath = Path.Combine(root, "skills");
        if (!Path.Exists(skillsPath))
        {
            return;
        }

        if (!Directory.Exists(skillsPath))
        {
            problems.Add("skills: fixed component location must be a directory");
            return;
        }

        string[] children;
        try
        {
            children = Directory.GetDirectories(skillsPath);
            Array.Sort(children, StringComparer.Ordinal);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            problems.Add("skills: could not enumerate skill directories: " + SingleLine(exception.Message));
            return;
        }

        foreach (var child in children)
        {
            var relative = $"skills/{Path.GetFileName(child)}/SKILL.md";
            if (!files.Contains(relative))
            {
                continue;
            }

            var fields = ParseSkillFrontmatter(root, relative, problems);
            if (fields is null)
            {
                continue;
            }

            fields.TryGetValue("name", out var name);
            fields.TryGetValue("description", out var description);
            if (name is null
                || !SkillNamePattern.IsMatch(name)
                || name.Contains("--", StringComparison.Ordinal)
                || !string.Equals(name, Path.GetFileName(child), StringComparison.Ordinal))
            {
                problems.Add(
                    $"{relative}: name must be valid and match its parent directory");
            }

            if (description is null
                || description.EnumerateRunes().Count() is < 1 or > 1024)
            {
                problems.Add(
                    $"{relative}: description must contain 1 through 1024 characters");
            }
        }
    }

    private static Dictionary<string, string>? ParseSkillFrontmatter(
        string root,
        string relativePath,
        List<string> problems)
    {
        var text = ReadBoundedUtf8Text(
            Path.Combine(root, ToPlatformPath(relativePath)),
            relativePath,
            MaximumSkillBytes,
            "skill",
            problems);
        if (text is null)
        {
            return null;
        }

        var lines = SplitLines(text);
        if (lines.Length == 0 || lines[0] != "---")
        {
            problems.Add($"{relativePath}: YAML frontmatter must start on the first line");
            return null;
        }

        var end = -1;
        for (var index = 1; index < lines.Length; index++)
        {
            if (lines[index] == "---")
            {
                end = index;
                break;
            }
        }

        if (end < 0)
        {
            problems.Add($"{relativePath}: YAML frontmatter is not closed");
            return null;
        }

        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 1; index < end; index++)
        {
            var line = lines[index];
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var separator = line.IndexOf(':');
            var key = separator > 0 ? line[..separator] : string.Empty;
            var rawValue = separator > 0 ? line[(separator + 1)..].TrimStart() : string.Empty;
            if (separator <= 0
                || !string.Equals(key, key.Trim(), StringComparison.Ordinal)
                || key.Any(character => char.IsWhiteSpace(character)))
            {
                problems.Add($"{relativePath}: invalid YAML frontmatter syntax on line {index + 1}");
                continue;
            }

            if (fields.ContainsKey(key))
            {
                problems.Add($"{relativePath}: duplicate frontmatter field {key}");
                continue;
            }

            if (!SkillFields.Contains(key))
            {
                problems.Add($"{relativePath}: unknown frontmatter field {key}");
            }

            if (!TryParseYamlString(rawValue, out var parsed))
            {
                problems.Add($"{relativePath}: frontmatter field {key} must be a string");
                continue;
            }

            fields[key] = parsed;
        }

        if (!lines.Skip(end + 1).Any(line => !string.IsNullOrWhiteSpace(line)))
        {
            problems.Add($"{relativePath}: Markdown instructions are required");
        }

        return fields;
    }

    private static bool TryParseYamlString(string source, out string value)
    {
        value = string.Empty;
        source = StripYamlComment(source);
        if (source.Length == 0)
        {
            return false;
        }

        if (source[0] == '"')
        {
            try
            {
                var parsed = JsonSerializer.Deserialize<string>(source);
                if (parsed is null)
                {
                    return false;
                }

                value = parsed;
                return true;
            }
            catch (JsonException)
            {
                return false;
            }
        }

        if (source[0] == '\'')
        {
            if (source.Length < 2 || source[^1] != '\'')
            {
                return false;
            }

            var parsed = new StringBuilder(source.Length - 2);
            for (var index = 1; index < source.Length - 1; index++)
            {
                if (source[index] != '\'')
                {
                    parsed.Append(source[index]);
                    continue;
                }

                if (index + 1 >= source.Length - 1 || source[index + 1] != '\'')
                {
                    return false;
                }

                parsed.Append('\'');
                index++;
            }

            value = parsed.ToString();
            return true;
        }

        var comment = source.IndexOf(" #", StringComparison.Ordinal);
        var plain = (comment >= 0 ? source[..comment] : source).TrimEnd();
        if (plain.Length == 0
            || "[{|>&*!".Contains(plain[0])
            || ((plain[0] is '-' or '?' or ':')
                && (plain.Length == 1 || char.IsWhiteSpace(plain[1])))
            || plain.Contains(": ", StringComparison.Ordinal)
            || plain.EndsWith(':')
            || NumericYamlScalarPattern.IsMatch(plain)
            || TimestampYamlScalarPattern.IsMatch(plain)
            || ImplicitYamlScalars.Contains(plain))
        {
            return false;
        }

        value = plain;
        return true;
    }

    private static string StripYamlComment(string source)
    {
        var singleQuoted = false;
        var doubleQuoted = false;
        var escaped = false;
        for (var index = 0; index < source.Length; index++)
        {
            var character = source[index];
            if (doubleQuoted)
            {
                if (escaped)
                {
                    escaped = false;
                }
                else if (character == '\\')
                {
                    escaped = true;
                }
                else if (character == '"')
                {
                    doubleQuoted = false;
                }

                continue;
            }

            if (singleQuoted)
            {
                if (character == '\''
                    && index + 1 < source.Length
                    && source[index + 1] == '\'')
                {
                    index++;
                }
                else if (character == '\'')
                {
                    singleQuoted = false;
                }

                continue;
            }

            if (character == '"')
            {
                doubleQuoted = true;
            }
            else if (character == '\'')
            {
                singleQuoted = true;
            }
            else if (character == '#'
                && (index == 0 || char.IsWhiteSpace(source[index - 1])))
            {
                return source[..index].TrimEnd();
            }
        }

        return source.TrimEnd();
    }

    private static void ValidateMcp(
        string root,
        IReadOnlySet<string> files,
        bool requireMcp,
        List<string> problems)
    {
        const string relativePath = "mcp.json";
        if (!files.Contains(relativePath))
        {
            if (requireMcp)
            {
                problems.Add("mcp.json: packaged plugin requires an MCP configuration");
            }

            return;
        }

        using var document = LoadJsonObject(
            root,
            relativePath,
            MaximumMcpBytes,
            problems);
        if (document is null)
        {
            return;
        }

        var value = document.RootElement;
        RejectUnknown(
            value,
            new HashSet<string>(["$schema", "mcpServers"], StringComparer.Ordinal),
            "mcp.json",
            problems);
        RequireExactString(
            value,
            "$schema",
            McpSchema,
            "mcp.json: schema must match Agent Plugins 1.0.0",
            problems);

        if (!value.TryGetProperty("mcpServers", out var servers)
            || servers.ValueKind != JsonValueKind.Object)
        {
            problems.Add("mcp.json: mcpServers must be an object");
            return;
        }

        var serverProperties = servers.EnumerateObject().ToArray();
        if (requireMcp
            && (serverProperties.Length != 1
                || serverProperties[0].Name != PackagedServerName))
        {
            problems.Add(
                "mcp.json: packaged plugin must declare exactly the vibesnake-agent server");
        }

        foreach (var serverProperty in serverProperties)
        {
            var label = $"mcp.json server {serverProperty.Name}";
            if (serverProperty.Value.ValueKind != JsonValueKind.Object)
            {
                problems.Add($"{label}: configuration must be an object");
                continue;
            }

            var server = serverProperty.Value;
            if (!TryGetString(server, "type", out var serverType)
                || serverType != "stdio")
            {
                problems.Add($"{label}: Vibe Snake's producer profile supports only stdio");
                continue;
            }

            ValidateStdio(root, label, server, problems);
            if (requireMcp && serverProperty.Name == PackagedServerName)
            {
                ValidatePackagedLaunch(label, server, problems);
            }
        }
    }

    private static void ValidateStdio(
        string root,
        string label,
        JsonElement value,
        List<string> problems)
    {
        RejectUnknown(value, StdioFields, label, problems);
        if (!TryGetString(value, "command", out var command)
            || !IsCommandToken(command))
        {
            problems.Add($"{label}: command must be one nonempty executable token");
        }
        else if (command.StartsWith("./", StringComparison.Ordinal))
        {
            var executable = Path.Combine(root, ToPlatformPath(command[2..]));
            if (!IsContained(root, executable))
            {
                problems.Add($"{label}: command escapes the plugin root");
            }
            else if (!File.Exists(executable))
            {
                problems.Add($"{label}: packaged command does not exist");
            }
        }
        else if (command.Contains('/') || command.Contains('\\'))
        {
            problems.Add($"{label}: command must be a bare token or start with ./");
        }

        if (value.TryGetProperty("args", out var arguments)
            && (arguments.ValueKind != JsonValueKind.Array
                || arguments.EnumerateArray().Any(item => item.ValueKind != JsonValueKind.String)))
        {
            problems.Add($"{label}: args must be an array of strings");
        }

        if (value.TryGetProperty("env", out var environment))
        {
            if (environment.ValueKind != JsonValueKind.Object
                || environment.EnumerateObject().Any(
                    property => property.Value.ValueKind != JsonValueKind.String))
            {
                problems.Add($"{label}: env must map strings to strings");
            }
            else if (environment.EnumerateObject().Any(
                property => property.Name.Equals("PLUGIN_ROOT", StringComparison.OrdinalIgnoreCase)
                    || property.Name.Equals("PLUGIN_DATA", StringComparison.OrdinalIgnoreCase)))
            {
                problems.Add($"{label}: env cannot override PLUGIN_ROOT or PLUGIN_DATA");
            }
        }

        if (value.TryGetProperty("cwd", out var workingDirectory))
        {
            if (workingDirectory.ValueKind != JsonValueKind.String)
            {
                problems.Add($"{label}: cwd must be a string");
            }
            else
            {
                var cwd = workingDirectory.GetString()!;
                if (cwd.StartsWith("./", StringComparison.Ordinal))
                {
                    var path = Path.Combine(root, ToPlatformPath(cwd[2..]));
                    if (!IsContained(root, path))
                    {
                        problems.Add($"{label}: cwd escapes the plugin root");
                    }
                }
                else if (!IsSafePlaceholderPath(cwd))
                {
                    problems.Add($"{label}: cwd has an unsupported form");
                }
            }
        }
    }

    private static void ValidatePackagedLaunch(
        string label,
        JsonElement server,
        List<string> problems)
    {
        if (!TryGetString(server, "command", out var command) || command != "dotnet")
        {
            problems.Add($"{label}: packaged command must be dotnet");
        }

        if (!server.TryGetProperty("args", out var arguments)
            || arguments.ValueKind != JsonValueKind.Array
            || arguments.GetArrayLength() != 1
            || arguments[0].ValueKind != JsonValueKind.String
            || arguments[0].GetString() != PackagedHostArgument)
        {
            problems.Add(
                $"{label}: packaged args must contain only the declared Agent Host assembly");
        }

        if (!TryGetString(server, "cwd", out var cwd) || cwd != "${PLUGIN_ROOT}")
        {
            problems.Add($"{label}: packaged cwd must be ${{PLUGIN_ROOT}}");
        }
    }

    private static void ValidatePackagedComponents(
        IReadOnlySet<string> files,
        List<string> problems)
    {
        foreach (var relativePath in PackagedRequiredFiles)
        {
            if (!files.Contains(relativePath))
            {
                problems.Add($"{relativePath}: required packaged regular file is missing");
            }
        }
    }

    private static void ValidateChecksums(
        string root,
        IReadOnlySet<string> files,
        bool required,
        List<string> problems)
    {
        const string relativePath = "SHA256SUMS";
        if (!files.Contains(relativePath))
        {
            if (required)
            {
                problems.Add(
                    "SHA256SUMS: packaged plugin requires a complete checksum manifest");
            }

            return;
        }

        var text = ReadBoundedUtf8Text(
            Path.Combine(root, relativePath),
            relativePath,
            MaximumChecksumBytes,
            "checksum list",
            problems);
        if (text is null)
        {
            return;
        }

        var expected = new Dictionary<string, string>(StringComparer.Ordinal);
        var lines = SplitLines(text);
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            var separator = line.IndexOf("  ", StringComparison.Ordinal);
            var digest = separator > 0 ? line[..separator] : string.Empty;
            var candidatePath = separator > 0 ? line[(separator + 2)..] : string.Empty;
            if (!Sha256Pattern.IsMatch(digest)
                || !IsSafeRelativePackagePath(candidatePath)
                || candidatePath == relativePath
                || !files.Contains(candidatePath))
            {
                problems.Add($"SHA256SUMS:{index + 1}: invalid checksum entry");
                continue;
            }

            if (!expected.TryAdd(candidatePath, digest))
            {
                problems.Add($"SHA256SUMS:{index + 1}: duplicate path {candidatePath}");
            }
        }

        var actual = files
            .Where(path => path != relativePath)
            .ToHashSet(StringComparer.Ordinal);
        if (!actual.SetEquals(expected.Keys))
        {
            problems.Add(
                "SHA256SUMS: entries must match every packaged regular file exactly once");
        }

        foreach (var (path, digest) in expected.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            try
            {
                using var stream = File.OpenRead(Path.Combine(root, ToPlatformPath(path)));
                var actualDigest = Convert.ToHexStringLower(SHA256.HashData(stream));
                if (actualDigest != digest)
                {
                    problems.Add($"SHA256SUMS: digest mismatch for {path}");
                }
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                problems.Add(
                    $"SHA256SUMS: could not hash {path}: {SingleLine(exception.Message)}");
            }
        }
    }

    private static JsonDocument? LoadJsonObject(
        string root,
        string relativePath,
        long maximumBytes,
        List<string> problems)
    {
        var source = ReadBoundedUtf8Text(
            Path.Combine(root, ToPlatformPath(relativePath)),
            relativePath,
            maximumBytes,
            "JSON",
            problems);
        if (source is null)
        {
            return null;
        }

        try
        {
            var document = JsonDocument.Parse(
                source,
                new JsonDocumentOptions
                {
                    AllowTrailingCommas = false,
                    CommentHandling = JsonCommentHandling.Disallow,
                    MaxDepth = 64,
                });
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                problems.Add($"{relativePath}: root must be an object");
                document.Dispose();
                return null;
            }

            RejectDuplicateJsonKeys(document.RootElement);
            return document;
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException)
        {
            problems.Add($"{relativePath}: unreadable JSON: {SingleLine(exception.Message)}");
            return null;
        }
    }

    private static void RejectDuplicateJsonKeys(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in value.EnumerateArray())
            {
                RejectDuplicateJsonKeys(item);
            }

            return;
        }

        if (value.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
        {
            if (!names.Add(property.Name))
            {
                throw new InvalidDataException($"duplicate JSON key: {property.Name}");
            }

            RejectDuplicateJsonKeys(property.Value);
        }
    }

    private static string? ReadBoundedUtf8Text(
        string path,
        string relativePath,
        long maximumBytes,
        string kind,
        List<string> problems)
    {
        try
        {
            var info = new FileInfo(path);
            if (info.Length > maximumBytes)
            {
                problems.Add(
                    $"{relativePath}: {kind} exceeds the {maximumBytes}-byte validation limit");
                return null;
            }

            var bytes = File.ReadAllBytes(path);
            return new UTF8Encoding(false, true).GetString(bytes);
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or DecoderFallbackException
                or NotSupportedException)
        {
            problems.Add($"{relativePath}: unreadable {kind}: {SingleLine(exception.Message)}");
            return null;
        }
    }

    private static void RejectUnknown(
        JsonElement value,
        HashSet<string> allowed,
        string label,
        List<string> problems)
    {
        foreach (var field in value
            .EnumerateObject()
            .Select(property => property.Name)
            .Where(field => !allowed.Contains(field))
            .Order(StringComparer.Ordinal))
        {
            problems.Add($"{label}: unknown field {field}");
        }
    }

    private static void RequireExactString(
        JsonElement value,
        string propertyName,
        string expected,
        string failure,
        List<string> problems)
    {
        if (!TryGetString(value, propertyName, out var actual) || actual != expected)
        {
            problems.Add(failure);
        }
    }

    private static bool TryGetString(
        JsonElement value,
        string propertyName,
        out string result)
    {
        result = string.Empty;
        if (!value.TryGetProperty(propertyName, out var property)
            || property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        result = property.GetString()!;
        return true;
    }

    private static bool IsCommandToken(string value) =>
        value.Length > 0
        && value.All(character =>
            !char.IsWhiteSpace(character)
            && !char.IsControl(character)
            && character != '\u007f');

    private static bool IsSafePlaceholderPath(string value)
    {
        foreach (var placeholder in new[] { "${PLUGIN_ROOT}", "${PLUGIN_DATA}" })
        {
            if (value == placeholder)
            {
                return true;
            }

            var prefix = placeholder + "/";
            if (!value.StartsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }

            var suffix = value[prefix.Length..];
            return IsSafeRelativePackagePath(suffix);
        }

        return false;
    }

    private static bool IsSafeRelativePackagePath(string value) =>
        value.Length > 0
        && value[0] != '/'
        && !value.Contains('\\')
        && !value.Contains(':')
        && value.Split('/').All(part => part is not ("" or "." or ".."));

    private static bool IsContained(string root, string candidate)
    {
        try
        {
            var relative = Path.GetRelativePath(root, Path.GetFullPath(candidate));
            return relative != ".."
                && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                && !Path.IsPathRooted(relative);
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private static string[] SplitLines(string value)
    {
        var normalized = value.Replace("\r\n", "\n", StringComparison.Ordinal);
        if (normalized.Contains('\r'))
        {
            normalized = normalized.Replace('\r', '\n');
        }

        if (normalized.Length == 0)
        {
            return [];
        }

        var lines = normalized.Split('\n');
        return lines[^1].Length == 0 ? lines[..^1] : lines;
    }

    private static string RelativePath(string root, string path)
    {
        var relative = Path.GetRelativePath(root, path)
            .Replace(Path.DirectorySeparatorChar, '/');
        return relative == "." ? "plugin root" : relative;
    }

    private static string ToPlatformPath(string relativePath) =>
        relativePath.Replace('/', Path.DirectorySeparatorChar);

    private static string SingleLine(string value) =>
        value.Replace('\r', ' ').Replace('\n', ' ').Trim();

    private static RepositoryCheckResult Failed(IReadOnlyList<string> failures) =>
        new("Agent Plugin", false, string.Empty, failures);

    private sealed record PluginInventory(IReadOnlySet<string> Files);
}
