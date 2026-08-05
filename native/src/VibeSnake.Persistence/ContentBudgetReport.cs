namespace VibeSnake.Persistence;

/// <summary>
/// Measured inventory totals compared against declared pack budgets.
/// Does not claim installed-artifact sizes; it reports inventory metadata only.
/// </summary>
public sealed record ContentBudgetReport(
    long InventoryBytes,
    long ExportEligibleBytes,
    int FileCount,
    int ExportEligibleCount,
    bool InventoryWithinCoreInstalledBudget,
    bool InventoryWithinCoreWorkingSetBudget,
    bool ExportEligibleWithinCoreCompressedBudget,
    bool ExportEligibleWithinCoreInstalledBudget)
{
    public static ContentBudgetReport FromInventory(ContentInventory inventory)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        long inventoryBytes = 0;
        long exportEligibleBytes = 0;
        var exportEligibleCount = 0;
        foreach (var asset in inventory.Assets)
        {
            if (asset.Bytes < 0)
            {
                throw new InvalidDataException(
                    "Inventory asset bytes must be non-negative: " + asset.RelativePath);
            }

            inventoryBytes = checked(inventoryBytes + asset.Bytes);
            if (asset.ExportEligible)
            {
                exportEligibleCount++;
                exportEligibleBytes = checked(exportEligibleBytes + asset.Bytes);
            }
        }

        return new ContentBudgetReport(
            InventoryBytes: inventoryBytes,
            ExportEligibleBytes: exportEligibleBytes,
            FileCount: inventory.FileCount,
            ExportEligibleCount: exportEligibleCount,
            InventoryWithinCoreInstalledBudget:
                ContentPackBudgets.IsWithinCoreInstalledBudget(inventoryBytes),
            InventoryWithinCoreWorkingSetBudget:
                ContentPackBudgets.IsWithinCoreWorkingSetBudget(inventoryBytes),
            ExportEligibleWithinCoreCompressedBudget:
                ContentPackBudgets.IsWithinCoreCompressedBudget(exportEligibleBytes),
            ExportEligibleWithinCoreInstalledBudget:
                ContentPackBudgets.IsWithinCoreInstalledBudget(exportEligibleBytes));
    }
}
