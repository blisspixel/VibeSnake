using VibeSnake.Persistence;
using Xunit;

namespace VibeSnake.Rules.Tests;

public sealed class ContentEligibilityReportTests
{
    [Fact]
    public void Synthetic_inventory_counts_eligible_blocked_and_samples()
    {
        const string json = """
        {
          "schemaVersion": 1,
          "fileCount": 3,
          "assets": [
            {
              "id": "a",
              "path": "a.png",
              "mediaType": "image/png",
              "bytes": 10,
              "sha256": "00",
              "exportEligible": true,
              "shipStatus": "approved",
              "rights": { "status": "cleared" }
            },
            {
              "id": "b",
              "path": "b.mp3",
              "mediaType": "audio/mpeg",
              "bytes": 20,
              "sha256": "11",
              "exportEligible": false,
              "shipStatus": "blocked",
              "rights": { "status": "cleared" }
            },
            {
              "id": "c",
              "path": "c.json",
              "mediaType": "application/json",
              "bytes": 5,
              "sha256": "22",
              "exportEligible": false,
              "shipStatus": "excluded",
              "rights": { "status": "unknown" }
            }
          ]
        }
        """;
        var inventory = ContentInventory.Parse(json);
        var report = ContentEligibilityReport.FromInventory(inventory, sampleBlockedPathLimit: 1);
        Assert.Equal(3, report.FileCount);
        Assert.Equal(1, report.ExportEligibleCount);
        Assert.Equal(10, report.ExportEligibleBytes);
        Assert.True(report.HasAnyExportEligible);
        Assert.Equal(1, report.BlockedCount);
        Assert.Equal(1, report.ExcludedCount);
        Assert.Equal(new[] { "b.mp3" }, report.SampleBlockedPaths);
        Assert.Equal(1, report.CountsByMediaTypePrefix["image"]);
        Assert.Equal(1, report.CountsByMediaTypePrefix["audio"]);
        Assert.Equal(1, report.CountsByRightsStatus["unknown"]);
    }
}
