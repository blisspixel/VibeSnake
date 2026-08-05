using VibeSnake.Persistence;

namespace VibeSnake.Rules.Tests;

public sealed class ContentPackBudgetsTests
{
    [Fact]
    public void Core_and_radio_budget_helpers_are_strict()
    {
        Assert.True(ContentPackBudgets.IsWithinCoreCompressedBudget(0));
        Assert.True(ContentPackBudgets.IsWithinCoreCompressedBudget(
            ContentPackBudgets.CoreCompressedBytesMaximum));
        Assert.False(ContentPackBudgets.IsWithinCoreCompressedBudget(
            ContentPackBudgets.CoreCompressedBytesMaximum + 1));
        Assert.True(ContentPackBudgets.IsWithinCoreInstalledBudget(1));
        Assert.True(ContentPackBudgets.IsWithinCoreWorkingSetBudget(
            ContentPackBudgets.CoreWorkingSetBytesMaximum));
        Assert.False(ContentPackBudgets.IsWithinCoreWorkingSetBudget(
            ContentPackBudgets.CoreWorkingSetBytesMaximum + 1));
        Assert.True(ContentPackBudgets.IsWithinRadioStationCompressedBudget(1));
        Assert.False(ContentPackBudgets.IsWithinRadioStationCompressedBudget(
            ContentPackBudgets.RadioStationCompressedBytesMaximum + 1));
        Assert.True(ContentPackBudgets.IsWithinRadioStationInstalledBudget(0));
        Assert.False(ContentPackBudgets.IsWithinRadioStationInstalledBudget(-1));
        Assert.True(ContentPackBudgets.IsRadioPackId("vibesnake.radio.ambient"));
        Assert.False(ContentPackBudgets.IsRadioPackId("vibesnake.core"));
        Assert.False(ContentPackBudgets.IsRadioPackId("vibesnake.radio."));
        Assert.Equal("vibesnake.core", ContentPackBudgets.CorePackId);
    }

    [Fact]
    public void Budget_report_measures_inventory_totals_without_eligibility_claims()
    {
        var inventory = ContentInventory.Parse(
            """
            {
              "schemaVersion": 1,
              "fileCount": 2,
              "assets": [
                {
                  "id": "asset:a.json",
                  "path": "a.json",
                  "mediaType": "application/json",
                  "bytes": 100,
                  "sha256": "aa",
                  "exportEligible": false,
                  "shipStatus": "blocked",
                  "rights": { "status": "cleared" }
                },
                {
                  "id": "asset:b.json",
                  "path": "b.json",
                  "mediaType": "application/json",
                  "bytes": 50,
                  "sha256": "bb",
                  "exportEligible": false,
                  "shipStatus": "blocked",
                  "rights": { "status": "cleared" }
                }
              ]
            }
            """);

        var report = inventory.MeasureBudgets();
        Assert.Equal(150, report.InventoryBytes);
        Assert.Equal(0, report.ExportEligibleBytes);
        Assert.Equal(2, report.FileCount);
        Assert.Equal(0, report.ExportEligibleCount);
        Assert.True(report.InventoryWithinCoreInstalledBudget);
        Assert.True(report.ExportEligibleWithinCoreCompressedBudget);
        Assert.True(report.ExportEligibleWithinCoreInstalledBudget);
        Assert.Equal(150, inventory.TotalBytes);
        Assert.Equal(0, inventory.ExportEligibleBytes);
        Assert.Equal(2, inventory.CountByMediaTypePrefix("application/"));
        Assert.Equal(0, inventory.CountByMediaTypePrefix("audio/"));
        Assert.Throws<ArgumentException>(() => inventory.CountByMediaTypePrefix(" "));
    }

    [Fact]
    public void Public_inventory_budget_report_stays_within_declared_core_ceilings()
    {
        var path = ResolveInventoryPath();
        var inventory = ContentInventory.LoadFromFile(path);
        var report = ContentBudgetReport.FromInventory(inventory);

        Assert.Equal(inventory.FileCount, report.FileCount);
        Assert.Equal(0, report.ExportEligibleCount);
        Assert.Equal(0, report.ExportEligibleBytes);
        Assert.True(report.ExportEligibleWithinCoreCompressedBudget);
        Assert.True(report.ExportEligibleWithinCoreInstalledBudget);
        // Full inventory may exceed the core installed ceiling while radio remains optional.
        Assert.Equal(inventory.TotalBytes, report.InventoryBytes);
        Assert.True(report.InventoryBytes > 0);
    }

    private static string ResolveInventoryPath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "config", "content_inventory.json");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("Could not locate config/content_inventory.json.");
    }
}
