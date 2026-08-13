using System.Text.Json;
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
        Assert.Equal(
            ContentEligibilityReport.DefaultSampleBlockedPathLimit,
            eligibility.SampleBlockedPaths.Count);
        Assert.All(
            eligibility.SampleBlockedPaths,
            path =>
            {
                Assert.False(string.IsNullOrWhiteSpace(path));
                Assert.False(System.IO.Path.IsPathRooted(path));
            });

        var evidencePath = WriteEligibilityEvidence(eligibility);
        Assert.True(File.Exists(evidencePath));
        using var document = JsonDocument.Parse(File.ReadAllText(evidencePath));
        var root = document.RootElement;
        Assert.Equal(1, root.GetProperty("schema_version").GetInt32());
        Assert.Equal("content-eligibility-evidence-v1", root.GetProperty("kind").GetString());
        Assert.Equal(0, root.GetProperty("export_eligible_count").GetInt32());
        Assert.Equal(106, root.GetProperty("blocked_count").GetInt32());
    }

    private static string WriteEligibilityEvidence(ContentEligibilityReport report)
    {
        var directory = ResolveEvidenceDirectory();
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "content_eligibility.json");
        var payload = new
        {
            schema_version = 1,
            kind = "content-eligibility-evidence-v1",
            report.FileCount,
            report.ExportEligibleCount,
            report.ExportEligibleBytes,
            report.BlockedCount,
            report.ExcludedCount,
            report.HasAnyExportEligible,
            report.CountsByShipStatus,
            report.CountsByRightsStatus,
            report.CountsByMediaTypePrefix,
            report.SampleBlockedPaths,
            notes = new[]
            {
                "Published inventory classification only.",
                "exportEligible remains zero until human pack approval.",
            },
        };
        File.WriteAllText(
            path,
            JsonSerializer.Serialize(
                payload,
                TestJsonSerializerOptions.SnakeCaseIndented) + "\n");
        return path;
    }

    private static string ResolveEvidenceDirectory()
    {
        var configured = Environment.GetEnvironmentVariable("VIBESNAKE_EVIDENCE_DIR");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return Path.GetFullPath(configured);
        }

        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var roadmap = Path.Combine(directory.FullName, "ROADMAP.md");
            var solution = Path.Combine(directory.FullName, "native", "VibeSnake.slnx");
            if (File.Exists(roadmap) && File.Exists(solution))
            {
                return Path.Combine(directory.FullName, "TestResults", "native");
            }

            directory = directory.Parent;
        }

        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "TestResults", "native"));
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
