using VibeSnake.AgentHost;
using VibeSnake.AgentPlay;
using VibeSnake.Persistence;
using VibeSnake.Rules;

namespace VibeSnake.Rules.Tests;

/// <summary>
/// AA-07's public-identity screen, machine half. Presentation never invents a
/// display name, a standing, or a receipt the store did not already keep.
/// </summary>
[Collection(AgentHostIntegrationGroup.Name)]
public sealed class AgentPassportBrowseTests
{
    [Fact]
    public void An_empty_store_browses_as_empty_rather_than_as_a_broken_selection()
    {
        var report = AgentPassportBrowseReportV1.Create(AgentPassportDocumentV1.Empty);

        Assert.Equal(AgentPassportBrowseReportV1.Contract, report.Schema);
        Assert.True(report.IsEmpty);
        Assert.Empty(report.Entries);
        Assert.Equal(-1, report.SelectedIndex);
        Assert.Null(report.Selected);
        Assert.Null(report.HandoffReceiptHash);
        Assert.Equal(AgentPassportDocumentV1.MaximumRecords, report.RemainingRecords);
        Assert.Equal(-1, report.WithSelection(4).SelectedIndex);
    }

    [Fact]
    public void The_browser_lists_records_in_store_order_without_a_display_name()
    {
        using var temporary = new BrowseTemporaryDirectory();
        var first = PlayVerified(temporary.Path, "match_passbrowse1", 111UL, agentId: "browse-one");
        var second = PlayVerified(temporary.Path, "match_passbrowse2", 112UL, agentId: "browse-two");
        var document = new AgentPassportDocumentV1(
            AgentPassportDocumentV1.Contract,
            AgentPassportDocumentV1.CurrentSchemaVersion,
            AgentPassportDocumentV1.MaximumRecords,
            [first.ReceiptHash, second.ReceiptHash],
            [
                AgentPassportRecordV1.FromReceipt(first),
                AgentPassportRecordV1.FromReceipt(second),
            ]);

        var report = AgentPassportBrowseReportV1.Create(document);

        Assert.Equal(2, report.RecordCount);
        Assert.Equal(2, report.ExhibitionTotal);
        Assert.Equal(AgentPassportDocumentV1.MaximumRecords - 2, report.RemainingRecords);
        Assert.Equal(
            ["browse-one", "browse-two"],
            report.Entries.Select(entry => entry.AgentId).ToArray());
        Assert.Equal([0, 1], report.Entries.Select(entry => entry.Position).ToArray());
        Assert.All(
            report.Entries,
            entry => Assert.Equal(AgentPassportBrowseEntryV1.Contract, entry.Schema));
        var properties = typeof(AgentPassportBrowseEntryV1)
            .GetProperties()
            .Select(property => property.Name)
            .ToArray();
        Assert.DoesNotContain(
            properties,
            name => name.Contains("Display", StringComparison.Ordinal));
        Assert.DoesNotContain(
            properties,
            name => name.Contains("Name", StringComparison.Ordinal)
                && !name.Contains("Schema", StringComparison.Ordinal));
        Assert.Equal(first.ReceiptHash, report.Selected!.LatestReceiptHash);
        Assert.Equal(first.ReceiptHash, report.HandoffReceiptHash);
    }

    [Fact]
    public void Selection_stops_at_both_ends_instead_of_wrapping()
    {
        using var temporary = new BrowseTemporaryDirectory();
        var first = PlayVerified(temporary.Path, "match_passsel1", 113UL, agentId: "sel-one");
        var second = PlayVerified(temporary.Path, "match_passsel2", 114UL, agentId: "sel-two");
        var report = AgentPassportBrowseReportV1.Create(
            new AgentPassportDocumentV1(
                AgentPassportDocumentV1.Contract,
                AgentPassportDocumentV1.CurrentSchemaVersion,
                AgentPassportDocumentV1.MaximumRecords,
                [first.ReceiptHash, second.ReceiptHash],
                [
                    AgentPassportRecordV1.FromReceipt(first),
                    AgentPassportRecordV1.FromReceipt(second),
                ]));

        Assert.Equal(0, report.SelectedIndex);
        Assert.Equal(0, report.WithSelection(-5).SelectedIndex);
        Assert.Equal(1, report.WithSelection(99).SelectedIndex);
        Assert.Equal(second.ReceiptHash, report.WithSelection(1).HandoffReceiptHash);
    }

    [Fact]
    public void Ahead_level_and_behind_are_counts_not_a_standing()
    {
        using var temporary = new BrowseTemporaryDirectory();
        var receipt = PlayVerified(
            temporary.Path,
            "match_passrivalbrowse",
            115UL,
            rivalPersonalityId: "optimal",
            agentId: "rival-agent");
        var report = AgentPassportBrowseReportV1.Create(
            new AgentPassportDocumentV1(
                AgentPassportDocumentV1.Contract,
                AgentPassportDocumentV1.CurrentSchemaVersion,
                AgentPassportDocumentV1.MaximumRecords,
                [receipt.ReceiptHash],
                [AgentPassportRecordV1.FromReceipt(receipt)]));

        var entry = Assert.Single(report.Entries);
        Assert.Equal(1, entry.RivalryCount);
        Assert.Equal(
            1,
            entry.AheadCount + entry.LevelCount + entry.BehindCount);
        Assert.True(entry.MilestoneCount >= 1);
    }

    [Fact]
    public void Building_the_browse_view_does_not_write()
    {
        using var temporary = new BrowseTemporaryDirectory();
        var receipt = PlayVerified(temporary.Path, "match_passnowrite", 116UL);
        _ = AgentPassportBrowseReportV1.Create(
            new AgentPassportDocumentV1(
                AgentPassportDocumentV1.Contract,
                AgentPassportDocumentV1.CurrentSchemaVersion,
                AgentPassportDocumentV1.MaximumRecords,
                [receipt.ReceiptHash],
                [AgentPassportRecordV1.FromReceipt(receipt)]));
        Assert.False(File.Exists(Path.Combine(temporary.Path, "agent_arena", "agent_passports.json")));
        Assert.False(File.Exists(Path.Combine(temporary.Path, "agent_arena", "exhibition_archive.json")));
        Assert.False(File.Exists(Path.Combine(temporary.Path, "preferences.json")));
    }

    [Fact]
    public void The_browse_view_refuses_a_null_document()
    {
        Assert.Throws<ArgumentNullException>(() =>
            AgentPassportBrowseReportV1.Create(null!));
    }

    private static AgentExhibitionReceiptV2 PlayVerified(
        string root,
        string handle,
        ulong seed,
        string? rivalPersonalityId = null,
        string? agentId = null)
    {
        using var registry = new AgentSessionRegistry(
            new ReplayStore(root),
            () => handle,
            () => seed);
        var anonymous = AgentPassportV4.Anonymous;
        var passport = agentId is null
            ? null
            : new AgentPassportV4(
                AgentPassportV4.Contract,
                agentId,
                anonymous.PolicyVersion,
                anonymous.DisplayName,
                anonymous.AvatarId,
                anonymous.AccentId,
                anonymous.StationId);
        var started = registry.StartMatch(
            RunModeCatalog.ClassicId,
            AgentSeedVisibility.Open,
            null,
            maximumSteps: 1,
            rivalPersonalityId: rivalPersonalityId,
            passport: passport);
        var moved = registry.PlayMove(
            started.MatchHandle,
            handle + "-move",
            started.Observation.Tick,
            started.Observation.StateHash,
            AgentAction.Continue);
        Assert.True(moved.Accepted);
        var status = registry.GetExhibitionReceipt(started.MatchHandle);
        return Assert.IsType<AgentExhibitionReceiptV2>(status.Receipt);
    }

    private sealed class BrowseTemporaryDirectory : IDisposable
    {
        public BrowseTemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "VibeSnakeAgentPassportBrowseTests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
