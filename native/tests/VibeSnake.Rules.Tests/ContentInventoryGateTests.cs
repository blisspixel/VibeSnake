using System.Text.Json;

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

        using var document = JsonDocument.Parse(File.ReadAllText(inventoryPath));
        var root = document.RootElement;
        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        var fileCount = root.GetProperty("fileCount").GetInt32();
        Assert.True(fileCount > 0, "Inventory must list at least one classified asset.");

        var assets = root.GetProperty("assets");
        Assert.Equal(fileCount, assets.GetArrayLength());

        var exportEligible = 0;
        var unclearedRights = 0;
        foreach (var asset in assets.EnumerateArray())
        {
            if (asset.GetProperty("exportEligible").GetBoolean())
            {
                exportEligible++;
            }

            var rights = asset.GetProperty("rights");
            if (!string.Equals(
                    rights.GetProperty("status").GetString(),
                    "cleared",
                    StringComparison.Ordinal))
            {
                unclearedRights++;
            }

            var path = asset.GetProperty("path").GetString() ?? string.Empty;
            Assert.False(
                path.Contains("..", StringComparison.Ordinal),
                $"Inventory path must not traverse: {path}");
            Assert.False(
                Path.IsPathRooted(path),
                $"Inventory path must be relative: {path}");
        }

        Assert.Equal(0, exportEligible);
        Assert.Equal(0, unclearedRights);
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
