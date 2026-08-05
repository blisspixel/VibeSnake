namespace VibeSnake.Persistence;

/// <summary>
/// Measured inventory-scan and cold-start timings against declared ceilings.
/// Does not claim declared-hardware qualification by itself.
/// </summary>
public sealed record ContentTimingReport(
    int InventoryScanMilliseconds,
    int ColdStartMilliseconds,
    bool WithinInventoryScanBudget,
    bool WithinColdStartBudget)
{
    public static ContentTimingReport FromMeasurements(
        int inventoryScanMilliseconds,
        int coldStartMilliseconds)
    {
        if (inventoryScanMilliseconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(inventoryScanMilliseconds));
        }

        if (coldStartMilliseconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(coldStartMilliseconds));
        }

        return new ContentTimingReport(
            InventoryScanMilliseconds: inventoryScanMilliseconds,
            ColdStartMilliseconds: coldStartMilliseconds,
            WithinInventoryScanBudget:
                ContentPackBudgets.IsWithinCoreInventoryScanBudget(inventoryScanMilliseconds),
            WithinColdStartBudget:
                ContentPackBudgets.IsWithinCoreColdStartBudget(coldStartMilliseconds));
    }
}
