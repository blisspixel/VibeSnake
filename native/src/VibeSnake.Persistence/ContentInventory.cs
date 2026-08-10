using System.Text.Json;

namespace VibeSnake.Persistence;

/// <summary>
/// Read-only view of the published content inventory used to gate native
/// pack and export allowlists. Domain rules never load this type.
/// </summary>
public sealed class ContentInventory
{
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
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("Content inventory root must be an object.");
        }

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

        if (!root.TryGetProperty("assets", out var assetsElement)
            || assetsElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("Content inventory is missing the assets array.");
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
        foreach (var element in assetsElement.EnumerateArray())
        {
            var asset = ContentInventoryAsset.FromJson(element);
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

        if (assets.Count != fileCount)
        {
            throw new InvalidDataException(
                $"Content inventory fileCount {fileCount} does not match assets length {assets.Count}.");
        }

        return new ContentInventory(
            1,
            assetRoot,
            policySha256,
            fileCount,
            assets,
            byPath,
            byId);
    }

    public static ContentInventory LoadFromFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.GetFullPath(path);
        return Parse(File.ReadAllText(fullPath));
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

        if (string.IsNullOrWhiteSpace(normalized)
            || Path.IsPathRooted(normalized)
            || normalized.Contains("..", StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Inventory asset paths must be relative without traversal.",
                nameof(relativePath));
        }

        return normalized;
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
        if (System.IO.Path.IsPathRooted(relativePath)
            || relativePath.Contains("..", StringComparison.Ordinal))
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
        return new ContentInventoryAsset(
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
    }

    private static string GetOptionalString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString() ?? string.Empty
            : string.Empty;

    private static string? GetOptionalNullableString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
}

public sealed record ContentInventoryRights(
    string Status,
    string Source,
    string License,
    string Attribution,
    string ReviewEvidence);
