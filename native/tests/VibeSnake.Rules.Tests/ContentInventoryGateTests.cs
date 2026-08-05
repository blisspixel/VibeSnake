using VibeSnake.Persistence;

namespace VibeSnake.Rules.Tests;

/// <summary>
/// Regression gate for the published content inventory until pack export
/// approval sets <c>exportEligible</c> on a deliberate allowlist.
/// </summary>
public sealed class ContentInventoryGateTests
{
    [Fact]
    public void Public_inventory_has_zero_export_eligible_assets_until_pack_approval()
    {
        var inventoryPath = ResolveInventoryPath();
        Assert.True(File.Exists(inventoryPath), $"Missing inventory: {inventoryPath}");

        var inventory = ContentInventory.LoadFromFile(inventoryPath);
        Assert.Equal(1, inventory.SchemaVersion);
        Assert.True(inventory.FileCount > 0);
        Assert.Equal(0, inventory.ExportEligibleCount);
        Assert.All(inventory.Assets, asset =>
        {
            Assert.Equal("cleared", asset.RightsStatus);
            Assert.False(System.IO.Path.IsPathRooted(asset.RelativePath));
            Assert.DoesNotContain("..", asset.RelativePath, StringComparison.Ordinal);
        });

        var eligibility = ContentEligibilityReport.FromInventory(inventory);
        Assert.Equal(inventory.FileCount, eligibility.FileCount);
        Assert.Equal(0, eligibility.ExportEligibleCount);
        Assert.Equal(0, eligibility.ExportEligibleBytes);
        Assert.False(eligibility.HasAnyExportEligible);
        Assert.Equal(106, eligibility.BlockedCount);
        Assert.Equal(8, eligibility.ExcludedCount);
        Assert.Equal(114, eligibility.BlockedCount + eligibility.ExcludedCount);
        Assert.Equal(114, eligibility.CountsByRightsStatus["cleared"]);
        Assert.True(eligibility.CountsByMediaTypePrefix["audio"] >= 95);
        Assert.True(eligibility.CountsByShipStatus["blocked"] == 106);
    }

    private static string ResolveInventoryPath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(
                directory.FullName,
                "config",
                "content_inventory.json");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException(
            "Could not locate config/content_inventory.json from the test base directory.");
    }
}
