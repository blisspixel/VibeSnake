using VibeSnake.Persistence;

namespace VibeSnake.Rules.Tests;

public sealed class ContentInventoryTests
{
    [Fact]
    public void Parses_public_inventory_and_rejects_export_until_approval()
    {
        var path = ResolveInventoryPath();
        var inventory = ContentInventory.LoadFromFile(path);

        Assert.Equal(1, inventory.SchemaVersion);
        Assert.Equal(inventory.FileCount, inventory.Assets.Count);
        Assert.Equal(0, inventory.ExportEligibleCount);
        Assert.All(inventory.Assets, asset => Assert.Equal("cleared", asset.RightsStatus));
        Assert.False(inventory.IsExportEligible("ai/custom/military_tactician.json"));
        Assert.Contains(
            inventory.Assets,
            asset => asset.RelativePath.EndsWith("logo.png", StringComparison.Ordinal));
    }

    [Fact]
    public void Rejects_path_traversal_queries()
    {
        var inventory = ContentInventory.Parse(
            """
            {
              "schemaVersion": 1,
              "fileCount": 1,
              "assets": [
                {
                  "id": "asset:demo.json",
                  "path": "demo.json",
                  "mediaType": "application/json",
                  "bytes": 1,
                  "sha256": "00",
                  "exportEligible": false,
                  "shipStatus": "blocked",
                  "rights": { "status": "cleared" }
                }
              ]
            }
            """);

        Assert.Throws<ArgumentException>(() => inventory.IsExportEligible("../demo.json"));
        Assert.Throws<ArgumentException>(() => inventory.IsExportEligible("/demo.json"));
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
