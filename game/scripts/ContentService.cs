using VibeSnake.Persistence;

namespace VibeSnake.Game;

/// <summary>
/// Presentation-side content boundary for inventory-backed allowlists.
/// Does not load media yet; it only answers export-eligibility queries so
/// the shell cannot claim pack assets before approval.
/// </summary>
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

    public static ContentService LoadInventoryFile(string inventoryPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inventoryPath);
        return new ContentService(ContentInventory.LoadFromFile(inventoryPath));
    }

    public bool MayPackage(string relativePath) => _inventory.IsExportEligible(relativePath);

    public bool TryDescribe(string relativePath, out ContentInventoryAsset asset) =>
        _inventory.TryGetAsset(relativePath, out asset!);
}
