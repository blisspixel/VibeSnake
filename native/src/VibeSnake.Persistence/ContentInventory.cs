using System.Text.Json;
using System.Text;

namespace VibeSnake.Persistence;

/// <summary>
/// Read-only view of the published content inventory used to gate native
/// pack and export allowlists. Domain rules never load this type.
/// </summary>
public sealed class ContentInventory
{
    private const int MaximumDocumentBytes = 8 * 1024 * 1024;
    private const int MaximumAssets = 4096;
    private const long MaximumTotalBytes = 4L * 1024 * 1024 * 1024;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private readonly Dictionary<string, ContentInventoryAsset> _assetsByPath;
    private readonly Dictionary<string, ContentInventoryAsset> _assetsById;

    private ContentInventory(
        int schemaVersion,
        string assetRoot,
        string policySha256,
        int fileCount,
        IReadOnlyList<ContentInventoryAsset> assets,
        Dictionary<string, ContentInventoryAsset> assetsByPath,
        Dictionary<string, ContentInventoryAsset> assetsById)
    {
        SchemaVersion = schemaVersion;
        AssetRoot = assetRoot;
        PolicySha256 = policySha256;
        FileCount = fileCount;
        Assets = assets;
        _assetsByPath = assetsByPath;
        _assetsById = assetsById;
    }

    public int SchemaVersion { get; }

    public string AssetRoot { get; }

    public string PolicySha256 { get; }

    public int FileCount { get; }

    public IReadOnlyList<ContentInventoryAsset> Assets { get; }

    public int ExportEligibleCount => Assets.Count(asset => asset.ExportEligible);

    public long TotalBytes => Assets.Sum(asset => asset.Bytes);

    public long ExportEligibleBytes =>
        Assets.Where(asset => asset.ExportEligible).Sum(asset => asset.Bytes);

    public ContentBudgetReport MeasureBudgets() => ContentBudgetReport.FromInventory(this);

    public int CountByMediaTypePrefix(string mediaTypePrefix)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaTypePrefix);
        return Assets.Count(asset =>
            asset.MediaType.StartsWith(mediaTypePrefix, StringComparison.OrdinalIgnoreCase));
    }

    public static ContentInventory Parse(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        try
        {
            return ParseCore(json);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException
            or KeyNotFoundException or FormatException or OverflowException)
        {
            throw new InvalidDataException("Content inventory JSON is malformed or has invalid field types.", exception);
        }
    }

    private static ContentInventory ParseCore(string json)
    {
        if (json.Length > MaximumDocumentBytes || Encoding.UTF8.GetByteCount(json) > MaximumDocumentBytes)
        {
            throw new InvalidDataException("Content inventory exceeds the 8388608-byte document limit.");
        }

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("Content inventory root must be an object.");
        }

        RejectDuplicateFields(root);

        if (!root.TryGetProperty("schemaVersion", out var schemaElement)
            || schemaElement.ValueKind != JsonValueKind.Number
            || !schemaElement.TryGetInt32(out var schemaVersion)
            || schemaVersion != 1)
        {
            throw new InvalidDataException("Content inventory schemaVersion must be 1.");
        }

        if (!root.TryGetProperty("fileCount", out var fileCountElement)
            || fileCountElement.ValueKind != JsonValueKind.Number
            || !fileCountElement.TryGetInt32(out var fileCount))
        {
            throw new InvalidDataException("Content inventory fileCount must be an integer.");
        }

        if (fileCount <= 0)
        {
            throw new InvalidDataException("Content inventory fileCount must be positive.");
        }

        if (fileCount > MaximumAssets)
        {
            throw new InvalidDataException("Content inventory fileCount exceeds the 4096-asset limit.");
        }

        if (!root.TryGetProperty("assets", out var assetsElement)
            || assetsElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("Content inventory is missing the assets array.");
        }

        if (assetsElement.GetArrayLength() != fileCount)
        {
            throw new InvalidDataException(
                $"Content inventory fileCount {fileCount} does not match assets length {assetsElement.GetArrayLength()}.");
        }

        var assetRoot = root.TryGetProperty("assetRoot", out var assetRootElement)
            ? assetRootElement.GetString() ?? string.Empty
            : string.Empty;
        var policySha256 = root.TryGetProperty("policySha256", out var policyElement)
            ? policyElement.GetString() ?? string.Empty
            : string.Empty;

        var assets = new List<ContentInventoryAsset>(fileCount);
        var byPath = new Dictionary<string, ContentInventoryAsset>(
            fileCount,
            StringComparer.Ordinal);
        var byId = new Dictionary<string, ContentInventoryAsset>(
            fileCount,
            StringComparer.Ordinal);
        long totalBytes = 0;
        foreach (var element in assetsElement.EnumerateArray())
        {
            var asset = ContentInventoryAsset.FromJson(element);
            totalBytes = checked(totalBytes + asset.Bytes);
            if (totalBytes > MaximumTotalBytes)
            {
                throw new InvalidDataException("Content inventory exceeds the 4294967296-byte content limit.");
            }
            if (!byPath.TryAdd(asset.RelativePath, asset))
            {
                throw new InvalidDataException(
                    $"Content inventory contains a duplicate path: {asset.RelativePath}");
            }
            if (!byId.TryAdd(asset.Id, asset))
            {
                throw new InvalidDataException(
                    $"Content inventory contains a duplicate id: {asset.Id}");
            }

            assets.Add(asset);
        }

        return new ContentInventory(
            1,
            assetRoot,
            policySha256,
            fileCount,
            assets.AsReadOnly(),
            byPath,
            byId);
    }

    public static ContentInventory LoadFromFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.GetFullPath(path);
        using var source = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (source.Length > MaximumDocumentBytes)
        {
            throw new InvalidDataException("Content inventory exceeds the 8388608-byte document limit.");
        }

        using var bytes = new MemoryStream();
        var buffer = new byte[64 * 1024];
        int count;
        while ((count = source.Read(buffer)) > 0)
        {
            if (bytes.Length + count > MaximumDocumentBytes)
            {
                throw new InvalidDataException("Content inventory exceeds the 8388608-byte document limit.");
            }

            bytes.Write(buffer, 0, count);
        }

        var data = bytes.ToArray();
        var offset = data.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf }) ? 3 : 0;
        try
        {
            var json = StrictUtf8.GetString(data.AsSpan(offset));
            if (string.IsNullOrWhiteSpace(json))
            {
                throw new InvalidDataException("Content inventory file must contain a JSON object.");
            }

            return Parse(json);
        }
        catch (DecoderFallbackException exception)
        {
            throw new InvalidDataException("Content inventory must contain valid UTF-8.", exception);
        }
    }

    public bool TryGetAsset(string relativePath, out ContentInventoryAsset asset)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        var normalized = NormalizeRelativePath(relativePath);
        return _assetsByPath.TryGetValue(normalized, out asset!);
    }

    public bool IsExportEligible(string relativePath) =>
        TryGetAsset(relativePath, out var asset) && asset.ExportEligible;

    public bool TryGetAssetById(string assetId, out ContentInventoryAsset asset)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetId);
        return _assetsById.TryGetValue(assetId, out asset!);
    }

    public IReadOnlyList<ContentInventoryAsset> GetExportEligibleForPack(string packId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packId);
        return Assets
            .Where(asset => asset.ExportEligible && asset.PackId == packId)
            .OrderBy(asset => asset.Id, StringComparer.Ordinal)
            .ToArray();
    }

    private static string NormalizeRelativePath(string relativePath)
    {
        var normalized = relativePath.Replace('\\', '/').Trim();
        if (normalized.StartsWith("./", StringComparison.Ordinal))
        {
            normalized = normalized[2..];
        }

        if (!ContentInventoryAsset.IsSafeRelativePath(normalized))
        {
            throw new ArgumentException(
                "Inventory asset paths must be relative without traversal.",
                nameof(relativePath));
        }

        return normalized;
    }

    private static void RejectDuplicateFields(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name))
                {
                    throw new InvalidDataException("Content inventory repeats JSON field: " + property.Name);
                }

                RejectDuplicateFields(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var value in element.EnumerateArray())
            {
                RejectDuplicateFields(value);
            }
        }
    }
}

public sealed record ContentInventoryAsset(
    string Id,
    string RelativePath,
    string MediaType,
    long Bytes,
    string Sha256,
    bool ExportEligible,
    string ShipStatus,
    string RightsStatus,
    string PackId,
    string Role,
    string RuntimeUse,
    string IntegrityStatus,
    string? DuplicateOf,
    ContentInventoryRights Rights)
{
    internal static ContentInventoryAsset FromJson(JsonElement element)
    {
        var relativePath = element.GetProperty("path").GetString()
            ?? throw new InvalidDataException("Inventory asset is missing path.");
        relativePath = relativePath.Replace('\\', '/');
        if (!IsSafeRelativePath(relativePath))
        {
            throw new InvalidDataException($"Inventory asset path is unsafe: {relativePath}");
        }

        var rights = element.GetProperty("rights");
        var rightsRecord = new ContentInventoryRights(
            Status: rights.GetProperty("status").GetString() ?? string.Empty,
            Source: GetOptionalString(rights, "source"),
            License: GetOptionalString(rights, "license"),
            Attribution: GetOptionalString(rights, "attribution"),
            ReviewEvidence: GetOptionalString(rights, "reviewNote"));
        var asset = new ContentInventoryAsset(
            Id: element.GetProperty("id").GetString()
                ?? throw new InvalidDataException("Inventory asset is missing id."),
            RelativePath: relativePath,
            MediaType: element.GetProperty("mediaType").GetString() ?? string.Empty,
            Bytes: element.GetProperty("bytes").GetInt64(),
            Sha256: element.GetProperty("sha256").GetString() ?? string.Empty,
            ExportEligible: element.GetProperty("exportEligible").GetBoolean(),
            ShipStatus: element.GetProperty("shipStatus").GetString() ?? string.Empty,
            RightsStatus: rightsRecord.Status,
            PackId: GetOptionalString(element, "packId"),
            Role: GetOptionalString(element, "role"),
            RuntimeUse: GetOptionalString(element, "runtimeUse"),
            IntegrityStatus: GetOptionalString(element, "integrityStatus"),
            DuplicateOf: GetOptionalNullableString(element, "duplicateOf"),
            Rights: rightsRecord);
        if (asset.Bytes < 0 || asset.Bytes > 256L * 1024 * 1024)
        {
            throw new InvalidDataException("Inventory asset bytes must be between zero and 268435456.");
        }

        if (asset.ExportEligible && (asset.ShipStatus != "approved"
            || asset.RightsStatus != "cleared"
            || (element.TryGetProperty("integrityStatus", out _) && asset.IntegrityStatus != "valid")))
        {
            throw new InvalidDataException("Inventory export eligibility contradicts shipping, rights, or integrity status.");
        }

        return asset;
    }

    internal static bool IsSafeRelativePath(string value) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Length <= 512
        && !value.StartsWith('/')
        && !value.Contains(':')
        && !value.Any(char.IsControl)
        && !value.Contains("..", StringComparison.Ordinal)
        && value.Split('/').All(segment => segment.Length > 0 && segment != ".");

    private static string GetOptionalString(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var property))
        {
            return string.Empty;
        }

        if (property.ValueKind != JsonValueKind.String)
        {
            throw new InvalidDataException("Inventory asset optional field must be a string: " + name);
        }

        return property.GetString() ?? string.Empty;
    }

    private static string? GetOptionalNullableString(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var property) || property.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (property.ValueKind != JsonValueKind.String)
        {
            throw new InvalidDataException("Inventory asset optional field must be a string or null: " + name);
        }

        return property.GetString();
    }
}

public sealed record ContentInventoryRights(
    string Status,
    string Source,
    string License,
    string Attribution,
    string ReviewEvidence);
