using VibeSnake.Persistence;

namespace VibeSnake.Game;

/// <summary>
/// Presentation-side content boundary for inventory-backed allowlists and
/// packaging resolution. Does not decode or stream media; it only answers
/// eligibility, metadata, and inventory budget questions so the shell cannot
/// claim pack assets before approval.
/// </summary>
internal enum ContentResolveCode : byte
{
    Ready = 0,
    NotFound = 1,
    NotExportEligible = 2,
    InvalidPath = 3,
}

internal sealed record ContentResolveResult(
    ContentResolveCode Code,
    string Message,
    ContentInventoryAsset? Asset = null)
{
    public bool IsReady => Code == ContentResolveCode.Ready && Asset is not null;
}

internal sealed class ContentService
{
    private readonly ContentInventory _inventory;

    public ContentService(ContentInventory inventory)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        _inventory = inventory;
    }

    public int FileCount => _inventory.FileCount;

    public int ExportEligibleCount => _inventory.ExportEligibleCount;

    public long TotalBytes => _inventory.TotalBytes;

    public static ContentService LoadInventoryFile(string inventoryPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inventoryPath);
        return new ContentService(ContentInventory.LoadFromFile(inventoryPath));
    }

    public bool MayPackage(string relativePath) => _inventory.IsExportEligible(relativePath);

    public bool TryDescribe(string relativePath, out ContentInventoryAsset asset) =>
        _inventory.TryGetAsset(relativePath, out asset!);

    /// <summary>
    /// Resolves a relative inventory path for packaging. Ready only when the
    /// asset exists and is export-eligible.
    /// </summary>
    public ContentResolveResult ResolveForPackaging(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return new ContentResolveResult(
                ContentResolveCode.InvalidPath,
                "Content path is empty.");
        }

        try
        {
            if (!_inventory.TryGetAsset(relativePath, out var asset))
            {
                return new ContentResolveResult(
                    ContentResolveCode.NotFound,
                    "Asset is not present in the content inventory.");
            }

            if (!asset.ExportEligible)
            {
                return new ContentResolveResult(
                    ContentResolveCode.NotExportEligible,
                    "Asset is not export-eligible until pack approval.",
                    asset);
            }

            return new ContentResolveResult(
                ContentResolveCode.Ready,
                "Asset is approved for packaging.",
                asset);
        }
        catch (ArgumentException exception)
        {
            return new ContentResolveResult(
                ContentResolveCode.InvalidPath,
                exception.Message);
        }
    }

    public ContentBudgetReport MeasureBudgets() => _inventory.MeasureBudgets();

    public int CountByMediaTypePrefix(string mediaTypePrefix) =>
        _inventory.CountByMediaTypePrefix(mediaTypePrefix);

    public IReadOnlyList<ContentInventoryAsset> ListByMediaTypePrefix(string mediaTypePrefix)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaTypePrefix);
        return _inventory.Assets
            .Where(asset => asset.MediaType.StartsWith(mediaTypePrefix, StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }
}
