namespace VibeSnake.Rules.Tests;

public sealed class AchievementsBrowseReportTests
{
    [Fact]
    public void Empty_unlocks_report_full_catalog_locked()
    {
        var report = AchievementsBrowseReport.FromUnlocks(null);

        Assert.Equal(AchievementCatalog.Definitions.Count, report.CatalogCount);
        Assert.Equal(0, report.UnlockedCount);
        Assert.Equal(report.CatalogCount, report.LockedCount);
        Assert.False(report.HasAnyUnlock);
        Assert.False(report.IsComplete);
        Assert.Equal(report.CatalogCount, report.Entries.Count);
        Assert.All(report.Entries, entry => Assert.False(entry.Unlocked));
        Assert.Equal("RUN UNLOCKS 0/" + report.CatalogCount, report.FormatSummaryLine());
        Assert.Equal(string.Empty, report.FormatUnlockedPreview());
        Assert.True(report.CatalogCountsByRarity.ContainsKey("common"));
        Assert.Empty(report.UnlockedCountsByRarity);
    }

    [Fact]
    public void Known_unlocks_mark_rows_and_ignore_unknown_ids()
    {
        var report = AchievementsBrowseReport.FromUnlocks(
            ["first_bite", "not_a_real_id", "century", "first_bite"]);

        Assert.Equal(2, report.UnlockedCount);
        Assert.Equal(report.CatalogCount - 2, report.LockedCount);
        Assert.True(report.HasAnyUnlock);
        Assert.False(report.IsComplete);

        var first = report.Entries.Single(entry => entry.Id == "first_bite");
        Assert.True(first.Unlocked);
        Assert.Equal(0, first.CatalogIndex);
        Assert.Equal("First Bite", first.Name);

        var century = report.Entries.Single(entry => entry.Id == "century");
        Assert.True(century.Unlocked);

        // first_bite and century are both common in the catalog.
        Assert.Equal(2, report.UnlockedCountsByRarity["common"]);
    }

    [Fact]
    public void Complete_profile_marks_is_complete()
    {
        var allIds = AchievementCatalog.Definitions.Select(definition => definition.Id).ToArray();
        var report = AchievementsBrowseReport.FromUnlocks(allIds);

        Assert.Equal(report.CatalogCount, report.UnlockedCount);
        Assert.Equal(0, report.LockedCount);
        Assert.True(report.IsComplete);
        Assert.All(report.Entries, entry => Assert.True(entry.Unlocked));
    }

    [Fact]
    public void Preview_truncates_in_catalog_order()
    {
        var report = AchievementsBrowseReport.FromUnlocks(
            ["powered_up", "first_bite", "century", "legend"]);

        var preview = report.FormatUnlockedPreview(limit: 2);
        Assert.Equal("FIRST_BITE, CENTURY, ...", preview);

        Assert.Equal(string.Empty, report.FormatUnlockedPreview(limit: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => report.FormatUnlockedPreview(limit: -1));
    }

    [Fact]
    public void Rarity_progress_lines_cover_catalog_buckets()
    {
        var report = AchievementsBrowseReport.FromUnlocks(["legend"]);
        var lines = report.FormatRarityProgressLines();

        Assert.Contains(lines, line => line.StartsWith("common ", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.StartsWith("legendary ", StringComparison.Ordinal));
        Assert.Contains("legendary 1/1", lines);
    }

    [Fact]
    public void Filter_returns_unlocked_or_locked_only()
    {
        var report = AchievementsBrowseReport.FromUnlocks(["first_bite", "century"]);

        var unlocked = report.Filter(unlockedOnly: true);
        Assert.Equal(2, unlocked.Count);
        Assert.All(unlocked, entry => Assert.True(entry.Unlocked));

        var locked = report.Filter(unlockedOnly: false);
        Assert.Equal(report.CatalogCount - 2, locked.Count);
        Assert.All(locked, entry => Assert.False(entry.Unlocked));

        Assert.Equal(report.Entries.Count, report.Filter(unlockedOnly: null).Count);
    }

    [Fact]
    public void Empty_catalog_projection_is_not_complete_and_long_preview_scans_all_rows()
    {
        var empty = new AchievementsBrowseReport(
            0,
            0,
            0,
            new Dictionary<string, int>(),
            new Dictionary<string, int>(),
            []);
        Assert.False(empty.IsComplete);

        var oneUnlocked = AchievementsBrowseReport.FromUnlocks(["first_bite"]);
        Assert.Equal("FIRST_BITE", oneUnlocked.FormatUnlockedPreview(limit: int.MaxValue));
    }
}
