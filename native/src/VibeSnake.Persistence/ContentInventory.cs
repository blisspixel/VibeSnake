using System.Text.Json;

namespace VibeSnake.Persistence;

/// <summary>
/// Read-only view of the published content inventory used to gate native
/// pack and export allowlists. Domain rules never load this type.
/// </summary>
public sealed class ContentInventory
{
    private readonly Dictionary<string, ContentInventoryAsset> _assetsByPath;

    private ContentInventory(
        int schemaVersion,
        int fileCount,
        IReadOnlyList<ContentInventoryAsset> assets,
        Dictionary<string, ContentInventoryAsset> assetsByPath)
    {
        SchemaVersion = schemaVersion;
        FileCount = fileCount;
        Assets = assets;
        _assetsByPath = assetsByPath;
    }

    public int SchemaVersion { get; }

    public int FileCount { get; }

    public IReadOnlyList<ContentInventoryAsset> Assets { get; }

    public int ExportEligibleCount => Assets.Count(asset => asset.ExportEligible);

    public static ContentInventory Parse(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (!root.TryGetProperty("schemaVersion", out var schemaElement)
            || schemaElement.GetInt32() != 1)
        {
            throw new InvalidDataException("Content inventory schemaVersion must be 1.");
        }

        if (!root.TryGetProperty("fileCount", out var fileCountElement))
        {
            throw new InvalidDataException("Content inventory is missing fileCount.");
        }

        var fileCount = fileCountElement.GetInt32();
        if (fileCount <= 0)
        {
            throw new InvalidDataException("Content inventory fileCount must be positive.");
        }

        if (!root.TryGetProperty("assets", out var assetsElement)
            || assetsElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("Content inventory is missing the assets array.");
        }

        var assets = new List<ContentInventoryAsset>(fileCount);
        var byPath = new Dictionary<string, ContentInventoryAsset>(
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

            assets.Add(asset);
        }

        if (assets.Count != fileCount)
        {
            throw new InvalidDataException(
                $"Content inventory fileCount {fileCount} does not match assets length {assets.Count}.");
        }

        return new ContentInventory(1, fileCount, assets, byPath);
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
    string RightsStatus)
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
        return new ContentInventoryAsset(
            Id: element.GetProperty("id").GetString()
                ?? throw new InvalidDataException("Inventory asset is missing id."),
            RelativePath: relativePath,
            MediaType: element.GetProperty("mediaType").GetString() ?? string.Empty,
            Bytes: element.GetProperty("bytes").GetInt64(),
            Sha256: element.GetProperty("sha256").GetString() ?? string.Empty,
            ExportEligible: element.GetProperty("exportEligible").GetBoolean(),
            ShipStatus: element.GetProperty("shipStatus").GetString() ?? string.Empty,
            RightsStatus: rights.GetProperty("status").GetString() ?? string.Empty);
    }
}
