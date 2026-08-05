using System.Text.Json;
using System.Text.RegularExpressions;

namespace VibeSnake.Persistence;

/// <summary>
/// Load outcome for a release/CI artifact-manifest.json document.
/// </summary>
public enum ReleaseArtifactManifestLoadCode : byte
{
    Success = 0,
    Empty = 1,
    InvalidJson = 2,
    UnsupportedSchema = 3,
    InvalidField = 4,
    MissingRequiredPayload = 5,
}

/// <summary>
/// Result of reading an artifact manifest produced by export inspection.
/// </summary>
public sealed record ReleaseArtifactManifestLoadResult(
    ReleaseArtifactManifestLoadCode Code,
    string Message,
    ReleaseArtifactManifest? Manifest = null)
{
    public bool IsSuccess =>
        Code == ReleaseArtifactManifestLoadCode.Success && Manifest is not null;
}

/// <summary>
/// One file entry inside a release artifact manifest.
/// </summary>
public sealed record ReleaseArtifactFileEntry(
    string Path,
    long Bytes,
    string Sha256,
    long? CompressedBytes = null);

/// <summary>
/// Schema 2 release artifact manifest: platform identity, toolchain provenance,
/// smoke hash, and per-file SHA-256 inventory. Matches the document written by
/// <c>scripts/inspect_native_artifact.ps1</c> so packaging gates can validate
/// without PowerShell. Does not claim signing or store-channel approval.
/// </summary>
public sealed record ReleaseArtifactManifest(
    int SchemaVersion,
    string Product,
    string Platform,
    string BuildMode,
    string SourceRevision,
    string GodotVersion,
    string GodotCommit,
    string GodotArchiveSha512,
    string GodotExecutableSha256,
    string DotnetSdk,
    string SmokeStateHash,
    int FileCount,
    long TotalBytes,
    IReadOnlyList<ReleaseArtifactFileEntry> Files,
    IReadOnlyList<ReleaseArtifactFileEntry> ContainerEntries)
{
    public const int CurrentSchemaVersion = 2;
    public const string FileName = "artifact-manifest.json";
    public const string ProductName = "Vibe Snake";

    public static readonly string[] SupportedPlatforms =
    [
        "windows-x64",
        "linux-x64",
        "macos-universal",
    ];

    public static readonly string[] SupportedBuildModes = ["Debug", "Release"];

    /// <summary>
    /// True when the platform is a first-class desktop ship target.
    /// </summary>
    public bool IsSupportedPlatform =>
        SupportedPlatforms.Contains(Platform, StringComparer.Ordinal);

    /// <summary>
    /// Reads and validates a manifest file path used by export inspection.
    /// </summary>
    public static ReleaseArtifactManifestLoadResult LoadFromFile(
        string path,
        bool enforceRequiredPayload = true)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return new ReleaseArtifactManifestLoadResult(
                ReleaseArtifactManifestLoadCode.Empty,
                "Artifact manifest path is empty.");
        }

        try
        {
            var json = File.ReadAllText(path);
            return Parse(json, enforceRequiredPayload);
        }
        catch (IOException exception)
        {
            return new ReleaseArtifactManifestLoadResult(
                ReleaseArtifactManifestLoadCode.InvalidField,
                "Could not read artifact manifest: " + exception.Message);
        }
        catch (UnauthorizedAccessException exception)
        {
            return new ReleaseArtifactManifestLoadResult(
                ReleaseArtifactManifestLoadCode.InvalidField,
                "Could not read artifact manifest: " + exception.Message);
        }
    }

    /// <summary>
    /// Pure parse + structural validation. Platform payload patterns are checked
    /// when <paramref name="enforceRequiredPayload"/> is true (default).
    /// </summary>
    public static ReleaseArtifactManifestLoadResult Parse(
        string json,
        bool enforceRequiredPayload = true)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new ReleaseArtifactManifestLoadResult(
                ReleaseArtifactManifestLoadCode.Empty,
                "Artifact manifest document is empty.");
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException exception)
        {
            return new ReleaseArtifactManifestLoadResult(
                ReleaseArtifactManifestLoadCode.InvalidJson,
                "Artifact manifest JSON is invalid: " + exception.Message);
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return FailField("Artifact manifest root must be an object.");
            }

            var root = document.RootElement;
            if (!TryGetInt(root, "schemaVersion", out var schemaVersion))
            {
                return FailField("schemaVersion must be an integer.");
            }

            if (schemaVersion != CurrentSchemaVersion)
            {
                return new ReleaseArtifactManifestLoadResult(
                    ReleaseArtifactManifestLoadCode.UnsupportedSchema,
                    "Unsupported artifact manifest schemaVersion: " + schemaVersion);
            }

            if (!TryGetString(root, "product", out var product)
                || !string.Equals(product, ProductName, StringComparison.Ordinal))
            {
                return FailField("product must be \"" + ProductName + "\".");
            }

            if (!TryGetString(root, "platform", out var platform)
                || !SupportedPlatforms.Contains(platform, StringComparer.Ordinal))
            {
                return FailField(
                    "platform must be one of: " + string.Join(", ", SupportedPlatforms));
            }

            if (!TryGetString(root, "buildMode", out var buildMode)
                || !SupportedBuildModes.Contains(buildMode, StringComparer.Ordinal))
            {
                return FailField("buildMode must be Debug or Release.");
            }

            if (!TryGetString(root, "sourceRevision", out var sourceRevision)
                || string.IsNullOrWhiteSpace(sourceRevision))
            {
                return FailField("sourceRevision is required.");
            }

            if (!TryGetString(root, "godotVersion", out var godotVersion)
                || string.IsNullOrWhiteSpace(godotVersion))
            {
                return FailField("godotVersion is required.");
            }

            if (!TryGetString(root, "godotCommit", out var godotCommit)
                || string.IsNullOrWhiteSpace(godotCommit))
            {
                return FailField("godotCommit is required.");
            }

            if (!TryGetString(root, "godotArchiveSha512", out var archiveSha)
                || !IsHex(archiveSha, 128))
            {
                return FailField("godotArchiveSha512 must be 128 lowercase hex characters.");
            }

            if (!TryGetString(root, "godotExecutableSha256", out var exeSha)
                || !IsHex(exeSha, 64))
            {
                return FailField("godotExecutableSha256 must be 64 lowercase hex characters.");
            }

            if (!TryGetString(root, "dotnetSdk", out var dotnetSdk)
                || string.IsNullOrWhiteSpace(dotnetSdk))
            {
                return FailField("dotnetSdk is required.");
            }

            if (!TryGetString(root, "smokeStateHash", out var smokeHash)
                || !IsHex(smokeHash, 16))
            {
                return FailField("smokeStateHash must be 16 lowercase hex characters.");
            }

            if (!TryGetInt(root, "fileCount", out var fileCount) || fileCount < 0)
            {
                return FailField("fileCount must be a non-negative integer.");
            }

            if (!TryGetLong(root, "totalBytes", out var totalBytes) || totalBytes < 0)
            {
                return FailField("totalBytes must be a non-negative integer.");
            }

            if (!TryReadEntries(root, "files", out var files, out var filesError))
            {
                return FailField(filesError!);
            }

            if (files.Count != fileCount)
            {
                return FailField(
                    "fileCount does not match files array length ("
                    + fileCount
                    + " vs "
                    + files.Count
                    + ").");
            }

            var summed = files.Sum(entry => entry.Bytes);
            if (summed != totalBytes)
            {
                return FailField(
                    "totalBytes does not equal the sum of file bytes ("
                    + totalBytes
                    + " vs "
                    + summed
                    + ").");
            }

            if (!TryReadEntries(
                    root,
                    "containerEntries",
                    out var containers,
                    out var containersError,
                    required: false))
            {
                return FailField(containersError!);
            }

            var manifest = new ReleaseArtifactManifest(
                SchemaVersion: schemaVersion,
                Product: product,
                Platform: platform,
                BuildMode: buildMode,
                SourceRevision: sourceRevision,
                GodotVersion: godotVersion,
                GodotCommit: godotCommit,
                GodotArchiveSha512: archiveSha,
                GodotExecutableSha256: exeSha,
                DotnetSdk: dotnetSdk,
                SmokeStateHash: smokeHash,
                FileCount: fileCount,
                TotalBytes: totalBytes,
                Files: files,
                ContainerEntries: containers);

            if (enforceRequiredPayload)
            {
                var payloadError = ValidateRequiredPayload(manifest);
                if (payloadError is not null)
                {
                    return new ReleaseArtifactManifestLoadResult(
                        ReleaseArtifactManifestLoadCode.MissingRequiredPayload,
                        payloadError);
                }
            }

            return new ReleaseArtifactManifestLoadResult(
                ReleaseArtifactManifestLoadCode.Success,
                "ok",
                manifest);
        }
    }

    /// <summary>
    /// Returns null when required platform payloads are present; otherwise an error.
    /// </summary>
    public static string? ValidateRequiredPayload(ReleaseArtifactManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        var paths = manifest.Files.Select(entry => entry.Path.Replace('\\', '/')).ToArray();
        var patterns = RequiredPathPatterns(manifest.Platform);
        foreach (var pattern in patterns)
        {
            if (!paths.Any(path => Regex.IsMatch(path, pattern, RegexOptions.CultureInvariant)))
            {
                return "Artifact is missing a required "
                    + manifest.Platform
                    + " path matching "
                    + pattern
                    + ".";
            }
        }

        if (string.Equals(manifest.Platform, "macos-universal", StringComparison.Ordinal))
        {
            var containerPaths = manifest.ContainerEntries
                .Select(entry => entry.Path.Replace('\\', '/'))
                .ToArray();
            foreach (var pattern in RequiredMacContainerPatterns)
            {
                if (!containerPaths.Any(path =>
                        Regex.IsMatch(path, pattern, RegexOptions.CultureInvariant)))
                {
                    return "macOS archive is missing a required path matching " + pattern + ".";
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Declared release packaging shape for a platform (folder, app, zip).
    /// </summary>
    public static string DeclaredInstallerArchiveShape(string platform) =>
        platform switch
        {
            "windows-x64" => "portable-folder",
            "linux-x64" => "portable-folder",
            "macos-universal" => "app-bundle-zip",
            _ => "unknown",
        };

    private static readonly string[] RequiredMacContainerPatterns =
    [
        @"\.app/Contents/MacOS/[^/]+$",
        @"\.app/Contents/Resources/[^/]+\.pck$",
        @"VibeSnake\.Game\.dll$",
        @"VibeSnake\.Persistence\.dll$",
        @"VibeSnake\.Rules\.dll$",
    ];

    private static string[] RequiredPathPatterns(string platform) =>
        platform switch
        {
            "windows-x64" =>
            [
                @"^VibeSnake\.exe$",
                @"^VibeSnake\.pck$",
                @"^data_VibeSnake\.Game_windows_x86_64/VibeSnake\.Game\.dll$",
                @"^data_VibeSnake\.Game_windows_x86_64/VibeSnake\.Persistence\.dll$",
                @"^data_VibeSnake\.Game_windows_x86_64/VibeSnake\.Rules\.dll$",
            ],
            "linux-x64" =>
            [
                @"^VibeSnake\.x86_64$",
                @"^VibeSnake\.pck$",
                @"^data_VibeSnake\.Game_linuxbsd_x86_64/VibeSnake\.Game\.dll$",
                @"^data_VibeSnake\.Game_linuxbsd_x86_64/VibeSnake\.Persistence\.dll$",
                @"^data_VibeSnake\.Game_linuxbsd_x86_64/VibeSnake\.Rules\.dll$",
            ],
            "macos-universal" => [@"^VibeSnake\.zip$"],
            _ => [],
        };

    private static ReleaseArtifactManifestLoadResult FailField(string message) =>
        new(ReleaseArtifactManifestLoadCode.InvalidField, message);

    private static bool TryGetInt(JsonElement root, string name, out int value)
    {
        value = 0;
        return root.TryGetProperty(name, out var element)
            && element.ValueKind == JsonValueKind.Number
            && element.TryGetInt32(out value);
    }

    private static bool TryGetLong(JsonElement root, string name, out long value)
    {
        value = 0;
        return root.TryGetProperty(name, out var element)
            && element.ValueKind == JsonValueKind.Number
            && element.TryGetInt64(out value);
    }

    private static bool TryGetString(JsonElement root, string name, out string value)
    {
        value = string.Empty;
        if (!root.TryGetProperty(name, out var element)
            || element.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = element.GetString() ?? string.Empty;
        return true;
    }

    private static bool TryReadEntries(
        JsonElement root,
        string propertyName,
        out List<ReleaseArtifactFileEntry> entries,
        out string? error,
        bool required = true)
    {
        entries = [];
        error = null;
        if (!root.TryGetProperty(propertyName, out var arrayElement))
        {
            if (required)
            {
                error = propertyName + " array is required.";
                return false;
            }

            return true;
        }

        if (arrayElement.ValueKind != JsonValueKind.Array)
        {
            error = propertyName + " must be an array.";
            return false;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var element in arrayElement.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                error = propertyName + " entries must be objects.";
                return false;
            }

            if (!TryGetString(element, "path", out var path) || string.IsNullOrWhiteSpace(path))
            {
                error = propertyName + " entry path is required.";
                return false;
            }

            path = path.Replace('\\', '/');
            if (path.StartsWith('/')
                || path.Contains("..", StringComparison.Ordinal)
                || path.Contains(':', StringComparison.Ordinal))
            {
                error = "Artifact path is unsafe: " + path;
                return false;
            }

            if (!seen.Add(path))
            {
                error = "Duplicate artifact path: " + path;
                return false;
            }

            if (!TryGetLong(element, "bytes", out var bytes) || bytes < 0)
            {
                error = "Entry bytes must be a non-negative integer for " + path;
                return false;
            }

            if (!TryGetString(element, "sha256", out var sha) || !IsHex(sha, 64))
            {
                error = "Entry sha256 must be 64 lowercase hex characters for " + path;
                return false;
            }

            long? compressed = null;
            if (element.TryGetProperty("compressedBytes", out var compressedElement))
            {
                if (compressedElement.ValueKind != JsonValueKind.Number
                    || !compressedElement.TryGetInt64(out var compressedValue)
                    || compressedValue < 0)
                {
                    error = "compressedBytes must be a non-negative integer for " + path;
                    return false;
                }

                compressed = compressedValue;
            }

            entries.Add(new ReleaseArtifactFileEntry(path, bytes, sha, compressed));
        }

        return true;
    }

    private static bool IsHex(string value, int expectedLength)
    {
        if (value.Length != expectedLength)
        {
            return false;
        }

        foreach (var c in value)
        {
            var isDigit = c is >= '0' and <= '9';
            var isLower = c is >= 'a' and <= 'f';
            if (!isDigit && !isLower)
            {
                return false;
            }
        }

        return true;
    }
}
