using System.Globalization;
using VibeSnake.AgentHost;
using VibeSnake.AgentPlay;
using VibeSnake.Persistence;
using VibeSnake.Rules;

namespace VibeSnake.Rules.Tests;

/// <summary>
/// AA-06's browser, machine half. Everything a person can decide from the
/// archive is decided here so presentation never invents a rule, and so the
/// isolation promise can be proven without a running game.
/// </summary>
[Collection(AgentHostIntegrationGroup.Name)]
public sealed class AgentExhibitionBrowseTests
{
    [Fact]
    public void The_browser_lists_the_archive_in_eviction_order()
    {
        using var temporary = new BrowseTemporaryDirectory();
        var receipts = ArchiveThree(temporary.Path);
        var archive = new AgentExhibitionArchiveStore(temporary.Path).Read();

        var report = AgentExhibitionBrowseReportV1.Create(archive, _ => true);

        Assert.Equal(AgentExhibitionBrowseReportV1.Contract, report.Schema);
        Assert.Equal(3, report.EntryCount);
        Assert.False(report.IsEmpty);
        // Oldest first, because that is the order eviction removes them in. A
        // browser that reordered would hide which one is about to be lost.
        Assert.Equal(receipts, report.Entries.Select(entry => entry.ReceiptHash).ToArray());
        Assert.Equal([0, 1, 2], report.Entries.Select(entry => entry.Position).ToArray());
        Assert.All(report.Entries, entry =>
        {
            Assert.Equal(AgentExhibitionBrowseEntryV1.Contract, entry.Schema);
            Assert.True(entry.WatchAvailable);
            Assert.True(entry.RematchAvailable);
            Assert.False(entry.IsRivalry);
        });
        Assert.Equal(3, report.WatchableCount);
        Assert.Equal(3, report.RematchableCount);
        Assert.Equal(0, report.MissingReplayCount);
        Assert.Equal(0, report.RivalryCount);
    }

    [Fact]
    public void An_empty_archive_browses_as_empty_rather_than_as_a_broken_selection()
    {
        var report = AgentExhibitionBrowseReportV1.Create(
            AgentExhibitionArchiveV2.Empty,
            _ => true);

        Assert.True(report.IsEmpty);
        Assert.Empty(report.Entries);
        Assert.Equal(-1, report.SelectedIndex);
        Assert.Null(report.Selected);
        Assert.Null(report.SelectedChallenge());
        Assert.Equal(-1, report.WithSelection(4).SelectedIndex);
    }

    [Fact]
    public void Selection_stops_at_both_ends_instead_of_wrapping()
    {
        using var temporary = new BrowseTemporaryDirectory();
        ArchiveThree(temporary.Path);
        var report = AgentExhibitionBrowseReportV1.Create(
            new AgentExhibitionArchiveStore(temporary.Path).Read(),
            _ => true);

        Assert.Equal(0, report.SelectedIndex);
        Assert.Equal(0, report.WithSelection(-5).SelectedIndex);
        Assert.Equal(2, report.WithSelection(99).SelectedIndex);
        Assert.Equal(1, report.WithSelection(1).SelectedIndex);
        Assert.Equal(
            report.Entries[1].ReceiptHash,
            report.WithSelection(1).Selected!.ReceiptHash);
    }

    [Fact]
    public void A_missing_lane_replay_blocks_watching_and_says_which_lane()
    {
        using var temporary = new BrowseTemporaryDirectory();
        var receipts = ArchiveThree(temporary.Path);
        var archive = new AgentExhibitionArchiveStore(temporary.Path).Read();
        var absent = archive.Entries[1].AgentReplayFileName;

        var report = AgentExhibitionBrowseReportV1.Create(
            archive,
            name => !string.Equals(name, absent, StringComparison.Ordinal));

        var blocked = report.Entries[1];
        Assert.False(blocked.WatchAvailable);
        Assert.Equal(AgentExhibitionWatchBlock.AgentReplayMissing, blocked.WatchBlock);
        Assert.Equal(1, report.MissingReplayCount);
        Assert.Equal(2, report.WatchableCount);

        // The exhibition is still a true record of what happened, so it stays
        // listed and stays rematchable. Only watching the recording is gone.
        Assert.Equal(receipts[1], blocked.ReceiptHash);
        Assert.True(blocked.RematchAvailable);
        Assert.Equal(3, report.RematchableCount);
    }

    [Fact]
    public void A_rivalry_needs_both_lanes_before_it_can_be_watched()
    {
        using var temporary = new BrowseTemporaryDirectory();
        var exhibition = PlayAndArchive(
            temporary.Path,
            "match_browserival",
            seed: 91UL,
            rivalPersonalityId: "optimal");
        var archive = new AgentExhibitionArchiveStore(temporary.Path).Read();
        var rivalFile = Assert.IsType<string>(archive.Entries[0].RivalReplayFileName);

        var whole = AgentExhibitionBrowseReportV1.Create(archive, _ => true);
        var halved = AgentExhibitionBrowseReportV1.Create(
            archive,
            name => !string.Equals(name, rivalFile, StringComparison.Ordinal));

        Assert.True(whole.Entries[0].IsRivalry);
        Assert.True(whole.Entries[0].WatchAvailable);
        Assert.Equal(1, whole.RivalryCount);
        Assert.Equal(exhibition.Receipt.RivalPersonalityId, whole.Entries[0].RivalPersonalityId);
        Assert.Equal(exhibition.Receipt.RivalScore, whole.Entries[0].RivalScore);

        Assert.False(halved.Entries[0].WatchAvailable);
        Assert.Equal(
            AgentExhibitionWatchBlock.RivalReplayMissing,
            halved.Entries[0].WatchBlock);
    }

    [Fact]
    public void A_same_seed_challenge_carries_the_line_and_nothing_else()
    {
        using var temporary = new BrowseTemporaryDirectory();
        var exhibition = PlayAndArchive(temporary.Path, "match_browsechal", seed: 92UL);
        var report = AgentExhibitionBrowseReportV1.Create(
            new AgentExhibitionArchiveStore(temporary.Path).Read(),
            _ => true);

        var challenge = Assert.IsType<AgentExhibitionChallengeV1>(report.SelectedChallenge());

        Assert.Equal(AgentExhibitionChallengeV1.Contract, challenge.Schema);
        Assert.Equal(exhibition.Receipt.ReceiptHash, challenge.ReceiptHash);
        Assert.Equal(exhibition.Receipt.RouteIdentityHash, challenge.RouteIdentityHash);
        Assert.Equal(exhibition.Receipt.Division.ModeId, challenge.ModeId);
        Assert.Equal(
            ulong.Parse(exhibition.Receipt.GameplaySeed, CultureInfo.InvariantCulture),
            challenge.GameplaySeed);
        Assert.Equal(exhibition.Receipt.Score, challenge.AgentScore);
    }

    [Fact]
    public void A_challenge_is_a_human_run_that_never_joins_ordinary_scores()
    {
        using var temporary = new BrowseTemporaryDirectory();
        PlayAndArchive(temporary.Path, "match_browseiso", seed: 93UL);
        var report = AgentExhibitionBrowseReportV1.Create(
            new AgentExhibitionArchiveStore(temporary.Path).Read(),
            _ => true);

        var challenge = Assert.IsType<AgentExhibitionChallengeV1>(report.SelectedChallenge());

        // The whole point of the handoff: a person plays the agent's line, and
        // the result is neither an ordinary fresh-seed score nor an agent one.
        Assert.True(challenge.IsIsolatedFromOrdinaryScores);
        Assert.Equal(ScoreRunContextCatalog.SeededChallengeRunKind, challenge.RunKindId);
        Assert.Equal(ScoreRunContextCatalog.FixedChallengeSeedCategory, challenge.SeedCategoryId);
        Assert.NotEqual(ScoreRunContextCatalog.NormalHumanRunKind, challenge.RunKindId);
        Assert.NotEqual(ScoreRunContextCatalog.AiRunKind, challenge.RunKindId);
        Assert.NotEqual(
            ScoreRunContextCatalog.FreshLocalSeedCategory,
            challenge.SeedCategoryId);

        var context = AgentExhibitionBrowseReportV1.ChallengeRunContext;
        Assert.Equal(ScoreRunContextCatalog.SeededChallenge, context);
        Assert.True(context.CompetitiveEligible);
        Assert.NotEqual(
            ScoreRunContextCatalog.NormalHuman.DisplayCategoryId,
            context.DisplayCategoryId);
    }

    [Fact]
    public void A_browse_row_publishes_no_display_time_and_no_passport()
    {
        using var temporary = new BrowseTemporaryDirectory();
        PlayAndArchive(temporary.Path, "match_browseid", seed: 94UL);
        var report = AgentExhibitionBrowseReportV1.Create(
            new AgentExhibitionArchiveStore(temporary.Path).Read(),
            _ => true);

        // Exhibition identity excludes display time, so a browser sorted by it
        // would present one exhibition differently on every visit. The caller
        // declared passport is equally absent: it is ephemeral, not history.
        var properties = typeof(AgentExhibitionBrowseEntryV1)
            .GetProperties()
            .Select(property => property.Name)
            .ToArray();
        Assert.DoesNotContain(properties, name => name.Contains("Time", StringComparison.Ordinal));
        Assert.DoesNotContain(
            properties,
            name => name.Contains("Passport", StringComparison.Ordinal));
        Assert.DoesNotContain(
            properties,
            name => name.Contains("Agent", StringComparison.Ordinal)
                && !name.Contains("Replay", StringComparison.Ordinal));
        Assert.Single(report.Entries);
    }

    [Fact]
    public void The_browser_refuses_arguments_it_cannot_stand_behind()
    {
        Assert.Throws<ArgumentNullException>(() => AgentExhibitionBrowseReportV1.Create(
            null!,
            _ => true));
        Assert.Throws<ArgumentNullException>(() => AgentExhibitionBrowseReportV1.Create(
            AgentExhibitionArchiveV2.Empty,
            null!));
    }

    private static string[] ArchiveThree(string root)
    {
        var receipts = new List<string>();
        for (var index = 0; index < 3; index++)
        {
            var exhibition = PlayAndArchive(root, $"match_browse{index}", 80UL + (ulong)index);
            receipts.Add(exhibition.Receipt.ReceiptHash);
        }

        return receipts.ToArray();
    }

    private static BrowseFixture PlayAndArchive(
        string root,
        string handle,
        ulong seed,
        string? rivalPersonalityId = null)
    {
        using var registry = new AgentSessionRegistry(
            new ReplayStore(root),
            () => handle,
            () => seed,
            archiveStore: new AgentExhibitionArchiveStore(root));
        var started = registry.StartMatch(
            RunModeCatalog.ClassicId,
            AgentSeedVisibility.Open,
            null,
            maximumSteps: 1,
            rivalPersonalityId: rivalPersonalityId);
        var moved = registry.PlayMove(
            started.MatchHandle,
            handle + "-move",
            started.Observation.Tick,
            started.Observation.StateHash,
            AgentAction.Continue);
        Assert.True(moved.Accepted);
        Assert.True(registry.SaveVerifiedReplay(started.MatchHandle).IsSuccess);
        var archived = registry.ArchiveExhibition(started.MatchHandle);
        Assert.True(archived.Archived, archived.Message);
        var receipt = registry.GetExhibitionReceipt(started.MatchHandle);
        return new BrowseFixture(
            Assert.IsType<AgentExhibitionReceiptV2>(receipt.Receipt));
    }

    private sealed record BrowseFixture(AgentExhibitionReceiptV2 Receipt);

    private sealed class BrowseTemporaryDirectory : IDisposable
    {
        public BrowseTemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "VibeSnakeAgentBrowseTests",
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
