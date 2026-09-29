using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace RepositoryChecks;

public static class AgentHostPackageCheck
{
    public const string PackageSchema = "vibesnake-agent-host-package-v1";
    public const string InventorySchema = "vibesnake-agent-host-inventory-v1";
    public const string ProvenanceSchema = "vibesnake-agent-host-provenance-v1";
    public const string HostName = "vibesnake-agent-host";
    public const string Transport = "stdio";
    public const string UserDataPolicy = "godot-app-userdata";
    public const string Signing = "unsigned";

    private const int MaximumTreeEntries = 8192;
    private const long MaximumManifestBytes = 128 * 1024;
    private const long MaximumProvenanceBytes = 128 * 1024;
    private const long MaximumInventoryBytes = 1024 * 1024;
    private const long MaximumChecksumBytes = 8 * 1024 * 1024;
    private const long MaximumProgramBytes = 1024 * 1024;
    private const string ProgramRelativePath = "native/tools/VibeSnake.AgentHost/Program.cs";

    private static readonly string[] RequiredFiles =
    [
        "LICENSE",
        "NOTICE",
        "INSTALL.txt",
        "host-manifest.json",
        "host-inventory.json",
        "host-provenance.json",
    ];

    private static readonly string[] HostLockPaths =
    [
        "native/src/VibeSnake.AgentPlay/packages.lock.json",
        "native/src/VibeSnake.Persistence/packages.lock.json",
        "native/src/VibeSnake.Rules/packages.lock.json",
        "native/tools/VibeSnake.AgentHost/packages.lock.json",
    ];

    private static readonly string[] RequiredPackages =
    [
        "Microsoft.Extensions.Hosting",
        "ModelContextProtocol",
    ];

    private static readonly string[] ProvenanceCrossManifestFields = ["host_version", "runtime_identifier"];

    private static readonly string[] ProvenanceCrossInventoryFields =
    [
        "host_version",
        "runtime_identifier",
        "source_revision",
        "source_dirty",
        "lock_set_sha256",
        "dotnet_sdk",
    ];

    private static readonly string[] ProvenanceDigestFields =
    [
        "executable_sha256",
        "manifest_sha256",
        "inventory_sha256",
        "lock_set_sha256",
    ];

    private static readonly string[] PackageListFields =
    [
        "dependency_types",
        "source_locks",
        "content_hashes",
        "frameworks",
    ];

    private static readonly HashSet<string> ManifestFields = new(StringComparer.Ordinal)
    {
        "schema",
        "host_name",
        "host_version",
        "runtime_identifier",
        "self_contained",
        "framework_dependent",
        "publication_eligible",
        "executable",
        "protocol_version",
        "transport",
        "user_data_policy",
        "signing",
    };

    private static readonly HashSet<string> InventoryFields = new(StringComparer.Ordinal)
    {
        "schema",
        "generated_from_locks_only",
        "host_version",
        "runtime_identifier",
        "source_revision",
        "source_dirty",
        "lock_set_sha256",
        "dotnet_sdk",
        "sources",
        "packages",
    };

    private static readonly HashSet<string> SourceFields = new(StringComparer.Ordinal)
    {
        "path",
        "sha256",
    };

    private static readonly HashSet<string> PackageFields = new(StringComparer.Ordinal)
    {
        "ecosystem",
        "name",
        "version",
        "dependency_types",
        "source_locks",
        "content_hashes",
        "frameworks",
    };

    private static readonly HashSet<string> ProvenanceFields = new(StringComparer.Ordinal)
    {
        "schema",
        "host_name",
        "host_version",
        "runtime_identifier",
        "source_revision",
        "source_dirty",
        "self_contained",
        "signing",
        "publication_eligible",
        "executable_sha256",
        "manifest_sha256",
        "inventory_sha256",
        "lock_set_sha256",
        "dotnet_sdk",
    };

    private static readonly HashSet<string> ForbiddenNames = new(StringComparer.Ordinal)
    {
        "preferences.json",
        "agent_passports.json",
        "exhibition_archive.json",
        "mcp.json",
    };

    private static readonly Regex RidPattern = new(
        "^(win|osx|linux)-(x64|arm64)$",
        RegexOptions.CultureInvariant);

    private static readonly Regex VersionPattern = new(
        "^[0-9]+\\.[0-9]+\\.[0-9]+$",
        RegexOptions.CultureInvariant);

    private static readonly Regex Sha256Pattern = new(
        "^[0-9a-f]{64}$",
        RegexOptions.CultureInvariant);

    private static readonly Regex RevisionPattern = new(
        "^[0-9a-f]{40}$",
        RegexOptions.CultureInvariant);

    private static readonly Regex ProtocolPattern = new(
        "McpProtocolVersion = \"([^\"]+)\"",
        RegexOptions.CultureInvariant);

    public static RepositoryCheckResult Inspect(string packageRoot, string repositoryRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);

        string root;
        try
        {
            root = Path.GetFullPath(packageRoot);
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return Failed(["host package root is invalid"]);
        }

        if (!Directory.Exists(root))
        {
            return Failed(["host package root must be an existing directory"]);
        }

        var problems = new List<string>();
        var files = BuildInventory(root, problems);
        foreach (var relative in RequiredFiles)
        {
            if (!files.Contains(relative))
            {
                problems.Add($"{relative}: required packaged regular file is missing");
            }
        }

        JsonElement manifest = default;
        var hasManifest = false;
        if (files.Contains("host-manifest.json"))
        {
            using var document = LoadJsonObject(
                root,
                "host-manifest.json",
                MaximumManifestBytes,
                problems);
            if (document is not null)
            {
                manifest = document.RootElement.Clone();
                hasManifest = true;
            }
        }

        JsonElement inventory = default;
        var hasInventory = false;
        if (files.Contains("host-inventory.json"))
        {
            using var document = LoadJsonObject(
                root,
                "host-inventory.json",
                MaximumInventoryBytes,
                problems);
            if (document is not null)
            {
                inventory = document.RootElement.Clone();
                hasInventory = true;
            }
        }

        JsonElement provenance = default;
        var hasProvenance = false;
        if (files.Contains("host-provenance.json"))
        {
            using var document = LoadJsonObject(
                root,
                "host-provenance.json",
                MaximumProvenanceBytes,
                problems);
            if (document is not null)
            {
                provenance = document.RootElement.Clone();
                hasProvenance = true;
            }
        }

        string? protocol = null;
        if (hasManifest)
        {
            protocol = ReadProtocol(repositoryRoot, problems);
            ValidateManifest(manifest, files, protocol, problems);
        }

        if (hasInventory)
        {
            ValidateInventory(inventory, problems);
        }

        if (hasProvenance)
        {
            ValidateProvenance(
                root,
                provenance,
                hasManifest ? manifest : null,
                hasInventory ? inventory : null,
                files,
                problems);
        }

        ValidateChecksums(root, files, problems);

        var failures = problems
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        return failures.Length == 0
            ? new RepositoryCheckResult(
                "Agent Host package",
                true,
                $"Agent Host package validation passed: {root}",
                [])
            : Failed(failures);
    }

    private static HashSet<string> BuildInventory(string root, List<string> problems)
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
                exception is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                problems.Add(
                    $"{RelativePath(root, directory)}: could not enumerate host package content: "
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
                        $"host package tree exceeds the {MaximumTreeEntries}-entry validation limit");
                    return files;
                }

                var relative = RelativePath(root, entry);
                FileAttributes attributes;
                try
                {
                    attributes = File.GetAttributes(entry);
                }
                catch (Exception exception) when (
                    exception is IOException or UnauthorizedAccessException or NotSupportedException)
                {
                    problems.Add(
                        $"{relative}: could not inspect host package content: "
                        + SingleLine(exception.Message));
                    continue;
                }

                if ((attributes & FileAttributes.ReparsePoint) != 0 || !IsContained(root, entry))
                {
                    problems.Add($"{relative}: link or path escapes are not allowed");
                    continue;
                }

                var name = Path.GetFileName(entry);
                if (ForbiddenNames.Contains(name))
                {
                    problems.Add(
                        $"{relative}: player or plugin files do not belong in a host package");
                }

                if ((attributes & FileAttributes.Directory) != 0)
                {
                    pending.Push(entry);
                    continue;
                }

                if (Path.GetExtension(entry).Equals(".pdb", StringComparison.OrdinalIgnoreCase))
                {
                    problems.Add($"{relative}: debug symbols are not part of this package");
                }

                if (!File.Exists(entry))
                {
                    problems.Add($"{relative}: link or path escapes are not allowed");
                    continue;
                }

                files.Add(relative);
            }
        }

        return files;
    }

    private static void ValidateManifest(
        JsonElement manifest,
        HashSet<string> files,
        string? protocol,
        List<string> problems)
    {
        const string label = "host-manifest.json";
        RequireFields(manifest, ManifestFields, label, problems);
        RequireExactString(manifest, "schema", PackageSchema, $"{label}: schema must be {PackageSchema}", problems);
        RequireExactString(manifest, "host_name", HostName, $"{label}: host_name must be {HostName}", problems);
        RequirePattern(manifest, "host_version", VersionPattern, $"{label}: host_version must be dotted numeric SemVer", problems);
        RequirePattern(
            manifest,
            "runtime_identifier",
            RidPattern,
            $"{label}: runtime_identifier must be a closed desktop RID",
            problems);
        if (!IsTrue(manifest, "self_contained"))
        {
            problems.Add($"{label}: self_contained must be true");
        }

        if (!IsFalse(manifest, "framework_dependent"))
        {
            problems.Add($"{label}: framework_dependent must be false");
        }

        if (!IsFalse(manifest, "publication_eligible"))
        {
            problems.Add($"{label}: publication_eligible must stay false until signing exists");
        }

        if (protocol is null
            || !TryGetString(manifest, "protocol_version", out var actualProtocol)
            || actualProtocol != protocol)
        {
            problems.Add(
                protocol is null
                    ? $"{label}: protocol_version must match the Agent Host MCP protocol constant"
                    : $"{label}: protocol_version must be {protocol}");
        }

        RequireExactString(manifest, "transport", Transport, $"{label}: transport must be {Transport}", problems);
        RequireExactString(
            manifest,
            "user_data_policy",
            UserDataPolicy,
            $"{label}: user_data_policy must be {UserDataPolicy}",
            problems);
        RequireExactString(manifest, "signing", Signing, $"{label}: signing must be {Signing}", problems);

        if (!TryGetString(manifest, "executable", out var executable)
            || executable.Length == 0
            || executable.Contains('\\')
            || executable.Contains('/'))
        {
            problems.Add($"{label}: executable must be a file name in the package root");
            return;
        }

        if (!files.Contains(executable))
        {
            problems.Add($"{executable}: declared host executable is missing");
            return;
        }

        if (!TryGetString(manifest, "runtime_identifier", out var runtimeIdentifier))
        {
            return;
        }

        var expectsExe = runtimeIdentifier.StartsWith("win-", StringComparison.Ordinal);
        if (expectsExe && !executable.EndsWith(".exe", StringComparison.Ordinal))
        {
            problems.Add($"{label}: Windows packages must declare a .exe host");
        }

        if (!expectsExe && executable.EndsWith(".exe", StringComparison.Ordinal))
        {
            problems.Add($"{label}: non-Windows packages must not declare a .exe host");
        }
    }

    private static void ValidateInventory(JsonElement inventory, List<string> problems)
    {
        const string label = "host-inventory.json";
        RequireFields(inventory, InventoryFields, label, problems);
        RequireExactString(
            inventory,
            "schema",
            InventorySchema,
            $"{label}: schema must be {InventorySchema}",
            problems);
        if (!IsTrue(inventory, "generated_from_locks_only"))
        {
            problems.Add($"{label}: generated_from_locks_only must be true");
        }

        RequirePattern(inventory, "host_version", VersionPattern, $"{label}: host_version must be dotted numeric SemVer", problems);
        RequirePattern(
            inventory,
            "runtime_identifier",
            RidPattern,
            $"{label}: runtime_identifier must be a closed desktop RID",
            problems);
        RequirePattern(
            inventory,
            "source_revision",
            RevisionPattern,
            $"{label}: source_revision must be a 40-character lowercase SHA-1",
            problems);
        if (!IsBoolean(inventory, "source_dirty"))
        {
            problems.Add($"{label}: source_dirty must be a boolean");
        }

        var hasLockSet = TryGetString(inventory, "lock_set_sha256", out var lockSetSha256)
            && Sha256Pattern.IsMatch(lockSetSha256);
        if (!hasLockSet)
        {
            problems.Add($"{label}: lock_set_sha256 must be a lowercase SHA-256");
        }

        RequirePattern(inventory, "dotnet_sdk", VersionPattern, $"{label}: dotnet_sdk must be dotted numeric SemVer", problems);

        var sourcePaths = new List<string>();
        var lockSetLines = new List<string>();
        if (!inventory.TryGetProperty("sources", out var sources) || sources.ValueKind != JsonValueKind.Array || sources.GetArrayLength() == 0)
        {
            problems.Add($"{label}: sources must list the host lock closure");
        }
        else
        {
            var seenPaths = new HashSet<string>(StringComparer.Ordinal);
            var index = 0;
            foreach (var source in sources.EnumerateArray())
            {
                var sourceLabel = $"{label} sources[{index}]";
                index++;
                if (source.ValueKind != JsonValueKind.Object)
                {
                    problems.Add($"{sourceLabel} must be an object");
                    continue;
                }

                RequireFields(source, SourceFields, sourceLabel, problems);
                if (!TryGetString(source, "path", out var path) || !IsSafeRelativePackagePath(path))
                {
                    problems.Add($"{sourceLabel}: path must be a repository-relative POSIX lock path");
                    continue;
                }

                if (!seenPaths.Add(path))
                {
                    problems.Add($"{sourceLabel}: duplicate path {path}");
                    continue;
                }

                if (!TryGetString(source, "sha256", out var digest) || !Sha256Pattern.IsMatch(digest))
                {
                    problems.Add($"{sourceLabel}: sha256 must be a lowercase SHA-256");
                    continue;
                }

                sourcePaths.Add(path);
                lockSetLines.Add($"{path}={digest}");
            }

            if (!sourcePaths.SequenceEqual(HostLockPaths, StringComparer.Ordinal))
            {
                problems.Add($"{label}: sources must be the exact host lock closure in path order");
            }

            if (hasLockSet)
            {
                var recomputed = Convert.ToHexStringLower(
                    SHA256.HashData(new UTF8Encoding(false).GetBytes(string.Join('\n', lockSetLines))));
                if (!string.Equals(recomputed, lockSetSha256, StringComparison.Ordinal))
                {
                    problems.Add($"{label}: lock_set_sha256 must match the ordered source list");
                }
            }
        }

        if (!inventory.TryGetProperty("packages", out var packages)
            || packages.ValueKind != JsonValueKind.Array
            || packages.GetArrayLength() == 0)
        {
            problems.Add($"{label}: packages must list locked NuGet packages");
            return;
        }

        var names = new List<string>();
        var seenKeys = new HashSet<string>(StringComparer.Ordinal);
        string? previousName = null;
        string? previousVersion = null;
        var packageIndex = 0;
        foreach (var package in packages.EnumerateArray())
        {
            var packageLabel = $"{label} packages[{packageIndex}]";
            packageIndex++;
            if (package.ValueKind != JsonValueKind.Object)
            {
                problems.Add($"{packageLabel} must be an object");
                continue;
            }

            RequireFields(package, PackageFields, packageLabel, problems);
            if (!TryGetString(package, "ecosystem", out var ecosystem) || ecosystem != "nuget")
            {
                problems.Add($"{packageLabel}: ecosystem must be nuget");
            }

            if (!TryGetString(package, "name", out var name) || name.Length == 0)
            {
                problems.Add($"{packageLabel}: name must be a non-empty string");
                continue;
            }

            if (!TryGetString(package, "version", out var version) || version.Length == 0)
            {
                problems.Add($"{packageLabel}: version must be a non-empty string");
                continue;
            }

            var key = name.ToLowerInvariant() + "|" + version;
            if (!seenKeys.Add(key))
            {
                problems.Add($"{packageLabel}: duplicate package {name} {version}");
                continue;
            }

            names.Add(name);
            var lowered = name.ToLowerInvariant();
            if (previousName is not null && previousVersion is not null)
            {
                var nameOrder = string.Compare(lowered, previousName, StringComparison.Ordinal);
                if (nameOrder < 0
                    || (nameOrder == 0
                        && string.Compare(version, previousVersion, StringComparison.Ordinal) < 0))
                {
                    problems.Add($"{label}: packages must be sorted by name then version");
                }
            }

            previousName = lowered;
            previousVersion = version;
            var runtimeIdentifier = TryGetString(inventory, "runtime_identifier", out var rid) ? rid : null;
            foreach (var field in PackageListFields)
            {
                var values = ReadStringList(package, field, packageLabel, problems);
                if (field == "source_locks" && sourcePaths.Count > 0)
                {
                    foreach (var sourceLock in values)
                    {
                        if (!sourcePaths.Contains(sourceLock))
                        {
                            problems.Add($"{packageLabel}: source lock is outside the host lock closure");
                        }
                    }
                }

                if (field != "frameworks")
                {
                    continue;
                }

                var allowed = new HashSet<string>(StringComparer.Ordinal) { "net10.0" };
                if (runtimeIdentifier is not null)
                {
                    allowed.Add("net10.0/" + runtimeIdentifier);
                }

                foreach (var framework in values)
                {
                    if (!allowed.Contains(framework))
                    {
                        problems.Add($"{packageLabel}: framework {framework} is outside this package RID");
                    }
                }
            }
        }

        foreach (var required in RequiredPackages)
        {
            if (!names.Contains(required))
            {
                problems.Add($"{label}: missing required package {required}");
            }
        }
    }

    private static void ValidateProvenance(
        string root,
        JsonElement provenance,
        JsonElement? manifest,
        JsonElement? inventory,
        HashSet<string> files,
        List<string> problems)
    {
        const string label = "host-provenance.json";
        RequireFields(provenance, ProvenanceFields, label, problems);
        RequireExactString(
            provenance,
            "schema",
            ProvenanceSchema,
            $"{label}: schema must be {ProvenanceSchema}",
            problems);
        RequireExactString(provenance, "host_name", HostName, $"{label}: host_name must be {HostName}", problems);
        if (!IsTrue(provenance, "self_contained"))
        {
            problems.Add($"{label}: self_contained must be true");
        }

        RequireExactString(provenance, "signing", Signing, $"{label}: signing must be {Signing}", problems);
        if (!IsFalse(provenance, "publication_eligible"))
        {
            problems.Add($"{label}: publication_eligible must stay false until signing exists");
        }

        RequirePattern(provenance, "host_version", VersionPattern, $"{label}: host_version must be dotted numeric SemVer", problems);
        RequirePattern(
            provenance,
            "runtime_identifier",
            RidPattern,
            $"{label}: runtime_identifier must be a closed desktop RID",
            problems);
        RequirePattern(
            provenance,
            "source_revision",
            RevisionPattern,
            $"{label}: source_revision must be a 40-character lowercase SHA-1",
            problems);
        if (!IsBoolean(provenance, "source_dirty"))
        {
            problems.Add($"{label}: source_dirty must be a boolean");
        }

        RequirePattern(provenance, "dotnet_sdk", VersionPattern, $"{label}: dotnet_sdk must be dotted numeric SemVer", problems);
        foreach (var field in ProvenanceDigestFields)
        {
            RequirePattern(provenance, field, Sha256Pattern, $"{label}: {field} must be a lowercase SHA-256", problems);
        }

        if (manifest is JsonElement manifestElement)
        {
            foreach (var field in ProvenanceCrossManifestFields)
            {
                if (!SameProperty(provenance, manifestElement, field))
                {
                    problems.Add($"{label}: {field} must match host-manifest.json");
                }
            }
        }

        if (inventory is JsonElement inventoryElement)
        {
            foreach (var field in ProvenanceCrossInventoryFields)
            {
                if (!SameProperty(provenance, inventoryElement, field))
                {
                    problems.Add($"{label}: {field} must match host-inventory.json");
                }
            }
        }

        if (manifest is JsonElement executableManifest
            && TryGetString(executableManifest, "executable", out var executable)
            && files.Contains(executable))
        {
            CompareFileHash(
                root,
                executable,
                provenance,
                "executable_sha256",
                $"{label}: executable_sha256 must match the declared host",
                problems);
        }

        if (files.Contains("host-manifest.json"))
        {
            CompareFileHash(
                root,
                "host-manifest.json",
                provenance,
                "manifest_sha256",
                $"{label}: manifest_sha256 must match host-manifest.json",
                problems);
        }

        if (files.Contains("host-inventory.json"))
        {
            CompareFileHash(
                root,
                "host-inventory.json",
                provenance,
                "inventory_sha256",
                $"{label}: inventory_sha256 must match host-inventory.json",
                problems);
        }
    }

    private static void ValidateChecksums(string root, HashSet<string> files, List<string> problems)
    {
        const string relativePath = "SHA256SUMS";
        if (!files.Contains(relativePath))
        {
            problems.Add("SHA256SUMS: packaged host requires a complete checksum manifest");
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
            problems.Add("SHA256SUMS: entries must match every packaged regular file exactly once");
        }

        foreach (var (path, digest) in expected)
        {
            try
            {
                using var stream = File.OpenRead(Path.Combine(root, ToPlatformPath(path)));
                var actualDigest = Convert.ToHexStringLower(SHA256.HashData(stream));
                if (!string.Equals(actualDigest, digest, StringComparison.Ordinal))
                {
                    problems.Add($"SHA256SUMS: digest mismatch for {path}");
                }
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                problems.Add($"SHA256SUMS: could not hash {path}: {SingleLine(exception.Message)}");
            }
        }
    }

    private static string? ReadProtocol(string repositoryRoot, List<string> problems)
    {
        string repositoryFull;
        try
        {
            repositoryFull = Path.GetFullPath(repositoryRoot);
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            problems.Add("Agent Host program: MCP protocol constant is missing or ambiguous");
            return null;
        }

        var path = Path.Combine(
            repositoryFull,
            "native",
            "tools",
            "VibeSnake.AgentHost",
            "Program.cs");
        var source = ReadBoundedUtf8Text(
            path,
            ProgramRelativePath,
            MaximumProgramBytes,
            "Agent Host program",
            problems);
        if (source is null)
        {
            problems.Add("Agent Host program: MCP protocol constant is missing or ambiguous");
            return null;
        }

        var matches = ProtocolPattern.Matches(source);
        if (matches.Count != 1)
        {
            problems.Add("Agent Host program: MCP protocol constant is missing or ambiguous");
            return null;
        }

        return matches[0].Groups[1].Value;
    }

    private static void CompareFileHash(
        string root,
        string relativePath,
        JsonElement provenance,
        string field,
        string failure,
        List<string> problems)
    {
        try
        {
            using var stream = File.OpenRead(Path.Combine(root, ToPlatformPath(relativePath)));
            var actual = Convert.ToHexStringLower(SHA256.HashData(stream));
            if (!TryGetString(provenance, field, out var declared) || declared != actual)
            {
                problems.Add(failure);
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            problems.Add($"{failure}: {SingleLine(exception.Message)}");
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
            if (!info.Exists)
            {
                problems.Add($"{relativePath}: unreadable {kind}: file is missing");
                return null;
            }

            if (info.Length > maximumBytes)
            {
                problems.Add($"{relativePath}: {kind} exceeds the {maximumBytes}-byte validation limit");
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

    private static void RequireFields(
        JsonElement value,
        HashSet<string> expected,
        string label,
        List<string> problems)
    {
        var present = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
        {
            present.Add(property.Name);
        }

        var extra = present.Where(name => !expected.Contains(name)).Order(StringComparer.Ordinal).ToArray();
        var missing = expected.Where(name => !present.Contains(name)).Order(StringComparer.Ordinal).ToArray();
        if (extra.Length > 0)
        {
            problems.Add($"{label}: unknown fields: {string.Join(", ", extra)}");
        }

        if (missing.Length > 0)
        {
            problems.Add($"{label}: missing fields: {string.Join(", ", missing)}");
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

    private static void RequirePattern(
        JsonElement value,
        string propertyName,
        Regex pattern,
        string failure,
        List<string> problems)
    {
        if (!TryGetString(value, propertyName, out var actual) || !pattern.IsMatch(actual))
        {
            problems.Add(failure);
        }
    }

    private static List<string> ReadStringList(
        JsonElement parent,
        string field,
        string label,
        List<string> problems)
    {
        if (!parent.TryGetProperty(field, out var value) || value.ValueKind != JsonValueKind.Array)
        {
            problems.Add($"{label}.{field} must be an array of non-empty strings");
            return [];
        }

        var result = new List<string>();
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String || string.IsNullOrEmpty(item.GetString()))
            {
                problems.Add($"{label}.{field} must be an array of non-empty strings");
                return [];
            }

            result.Add(item.GetString()!);
        }

        return result;
    }

    private static bool SameProperty(JsonElement left, JsonElement right, string name)
    {
        var leftHas = left.TryGetProperty(name, out var leftValue);
        var rightHas = right.TryGetProperty(name, out var rightValue);
        if (!leftHas || !rightHas || leftValue.ValueKind != rightValue.ValueKind)
        {
            return false;
        }

        return leftValue.ValueKind switch
        {
            JsonValueKind.String => leftValue.GetString() == rightValue.GetString(),
            JsonValueKind.True or JsonValueKind.False or JsonValueKind.Null => true,
            JsonValueKind.Number => leftValue.GetRawText() == rightValue.GetRawText(),
            _ => leftValue.GetRawText() == rightValue.GetRawText(),
        };
    }

    private static bool IsTrue(JsonElement value, string name) =>
        value.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.True;

    private static bool IsFalse(JsonElement value, string name) =>
        value.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.False;

    private static bool IsBoolean(JsonElement value, string name) =>
        value.TryGetProperty(name, out var property)
        && property.ValueKind is JsonValueKind.True or JsonValueKind.False;

    private static bool TryGetString(JsonElement value, string propertyName, out string result)
    {
        result = string.Empty;
        if (!value.TryGetProperty(propertyName, out var property)
            || property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        result = property.GetString() ?? string.Empty;
        return true;
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
        var normalized = value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        if (normalized.Length == 0)
        {
            return [];
        }

        var lines = normalized.Split('\n');
        return lines[^1].Length == 0 ? lines[..^1] : lines;
    }

    private static string RelativePath(string root, string path)
    {
        var relative = Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');
        return relative == "." ? "host package root" : relative;
    }

    private static string ToPlatformPath(string relativePath) =>
        relativePath.Replace('/', Path.DirectorySeparatorChar);

    private static string SingleLine(string value) =>
        value.Replace('\r', ' ').Replace('\n', ' ').Trim();

    private static RepositoryCheckResult Failed(IReadOnlyList<string> failures) =>
        new("Agent Host package", false, string.Empty, failures);
}
