using VibeSnake.AgentHost;
using VibeSnake.AgentPlay;
using VibeSnake.Persistence;
using VibeSnake.Rules;

namespace VibeSnake.Rules.Tests;

/// <summary>
/// AA-09b's machine half. An archived exhibition becomes a story only when
/// both named tapes are present and agree with the receipt. Presentation
/// reads the cursor; it does not invent a beat, a skip, or a pace.
/// </summary>
[Collection(AgentHostIntegrationGroup.Name)]
public sealed class AgentExhibitionStoryReportTests
{
    [Fact]
    public void An_archived_exhibition_becomes_a_story_from_its_named_tapes()
    {
        using var temporary = new StoryReportTemporaryDirectory();
        var exhibition = PlayAndArchive(temporary.Path, "match_storyrep", seed: 42UL);
        var archive = new AgentExhibitionArchiveStore(temporary.Path).Read();
        var store = new ReplayStore(temporary.Path);

        var report = AgentExhibitionStoryReportV1.FromArchive(
            archive.Entries[0],
            name => store.Load(name).Replay);

        Assert.True(report.IsAvailable);
        Assert.Equal(AgentExhibitionStoryReportV1.Contract, report.Schema);
        Assert.Equal(AgentExhibitionStoryRefuse.None, report.Refuse);
        Assert.Equal(exhibition.Receipt.ReceiptHash, report.ReceiptHash);
        Assert.Equal(exhibition.Receipt.RouteIdentityHash, report.RouteIdentityHash);
        var story = Assert.IsType<AgentExhibitionStoryV1>(report.Story);
        var cursor = Assert.IsType<AgentExhibitionStoryCursorV1>(report.Cursor);
        Assert.Equal(AgentExhibitionStoryCursorV1.Contract, cursor.Schema);
        Assert.Equal(0, cursor.Tick);
        Assert.Equal(AgentMontageRate.Selected, cursor.Rate);
        Assert.Equal(cursor.Tick, cursor.NextPlayableTick);
        Assert.Equal(0, story.Montage[0].StartTick);
        Assert.Equal(
            story.Montage[^1].EndTickInclusive + 1,
            story.Montage.Sum(window => window.EndTickInclusive - window.StartTick + 1));
    }

    [Fact]
    public void A_missing_archive_entry_is_not_a_story()
    {
        var report = AgentExhibitionStoryReportV1.FromArchive(null, _ => null);

        Assert.False(report.IsAvailable);
        Assert.Equal(AgentExhibitionStoryRefuse.NotArchived, report.Refuse);
        Assert.Null(report.Story);
        Assert.Null(report.Cursor);
    }

    [Fact]
    public void A_missing_agent_tape_is_refused_before_playback()
    {
        using var temporary = new StoryReportTemporaryDirectory();
        PlayAndArchive(temporary.Path, "match_storymiss", seed: 43UL);
        var archive = new AgentExhibitionArchiveStore(temporary.Path).Read();

        var report = AgentExhibitionStoryReportV1.FromArchive(
            archive.Entries[0],
            _ => null);

        Assert.False(report.IsAvailable);
        Assert.Equal(AgentExhibitionStoryRefuse.AgentReplayMissing, report.Refuse);
        Assert.Equal(archive.Entries[0].ReceiptHash, report.ReceiptHash);
        Assert.Equal(archive.Entries[0].AgentReplayFileName, report.AgentReplayFileName);
    }

    [Fact]
    public void A_rivalry_without_the_rival_tape_is_refused()
    {
        using var temporary = new StoryReportTemporaryDirectory();
        PlayAndArchive(
            temporary.Path,
            "match_storyrival",
            seed: 44UL,
            rivalPersonalityId: "optimal");
        var archive = new AgentExhibitionArchiveStore(temporary.Path).Read();
        var store = new ReplayStore(temporary.Path);
        var rivalName = Assert.IsType<string>(archive.Entries[0].RivalReplayFileName);

        var report = AgentExhibitionStoryReportV1.FromArchive(
            archive.Entries[0],
            name => string.Equals(name, rivalName, StringComparison.Ordinal)
                ? null
                : store.Load(name).Replay);

        Assert.False(report.IsAvailable);
        Assert.Equal(AgentExhibitionStoryRefuse.RivalReplayMissing, report.Refuse);
    }

    [Fact]
    public void A_disagreeing_agent_tape_is_refused()
    {
        using var temporary = new StoryReportTemporaryDirectory();
        PlayAndArchive(temporary.Path, "match_storyhash", seed: 45UL);
        PlayAndArchive(temporary.Path, "match_storyother", seed: 46UL);
        var archive = new AgentExhibitionArchiveStore(temporary.Path).Read();
        var store = new ReplayStore(temporary.Path);
        var otherReplay = store.Load(archive.Entries[1].AgentReplayFileName).Replay;

        var report = AgentExhibitionStoryReportV1.FromArchive(
            archive.Entries[0],
            _ => otherReplay);

        Assert.False(report.IsAvailable);
        Assert.Equal(AgentExhibitionStoryRefuse.AgentReplayHashMismatch, report.Refuse);
    }

    [Fact]
    public void Skip_windows_jump_to_the_next_beat_instead_of_playing()
    {
        var story = SyntheticStory();
        var skip = story.Montage.Single(window => window.Rate == AgentMontageRate.Skip);
        var cursor = AgentExhibitionStoryReportV1.At(story, skip.StartTick);

        Assert.Equal(AgentMontageRate.Skip, cursor.Rate);
        Assert.Equal(34, cursor.NextPlayableTick);
        Assert.Equal(
            AgentMontageRate.Selected,
            AgentExhibitionStoryReportV1.At(story, cursor.NextPlayableTick).Rate);
    }

    [Fact]
    public void Turning_point_seek_stops_at_both_ends()
    {
        var story = SyntheticStory();
        var report = new AgentExhibitionStoryReportV1(
            AgentExhibitionStoryReportV1.Contract,
            AgentExhibitionStoryRefuse.None,
            "hash",
            "route",
            "agent.json",
            null,
            story,
            AgentExhibitionStoryReportV1.At(story, 8));

        Assert.Equal(8, report.SeekTurningPoint(-1).Cursor!.Tick);
        Assert.Equal(40, report.AtTick(40).SeekTurningPoint(1).Cursor!.Tick);
        Assert.Equal(40, report.SeekTurningPoint(1).Cursor!.Tick);
        Assert.Equal(8, report.AtTick(40).SeekTurningPoint(-1).Cursor!.Tick);
    }

    [Fact]
    public void An_unavailable_report_does_not_seek()
    {
        var empty = AgentExhibitionStoryReportV1.FromArchive(null, _ => null);
        Assert.Same(empty, empty.SeekTurningPoint(1));
        Assert.Same(empty, empty.AtTick(4));
    }

    [Fact]
    public void Linger_is_half_the_viewer_speed_and_skip_is_not_a_speed()
    {
        Assert.Equal(5_000, AgentExhibitionStoryReportV1.LingerSpeedBasisPoints);
        Assert.Equal(10_000, AgentExhibitionStoryReportV1.SelectedSpeedBasisPoints);
        Assert.Equal(
            5_000,
            AgentExhibitionStoryReportV1.SpeedBasisPoints(AgentMontageRate.Linger));
        Assert.Equal(
            10_000,
            AgentExhibitionStoryReportV1.SpeedBasisPoints(AgentMontageRate.Selected));
        Assert.Equal(
            10_000,
            AgentExhibitionStoryReportV1.SpeedBasisPoints(AgentMontageRate.Skip));
    }

    [Fact]
    public void Building_a_report_does_not_write_player_data()
    {
        using var temporary = new StoryReportTemporaryDirectory();
        PlayAndArchive(temporary.Path, "match_storyiso", seed: 47UL);
        var archive = new AgentExhibitionArchiveStore(temporary.Path).Read();
        var store = new ReplayStore(temporary.Path);

        Assert.True(AgentExhibitionStoryReportV1.FromArchive(
            archive.Entries[0],
            name => store.Load(name).Replay).IsAvailable);
        Assert.False(File.Exists(Path.Combine(temporary.Path, "agent_arena", "agent_passports.json")));
        Assert.False(File.Exists(Path.Combine(temporary.Path, "preferences.json")));
        Assert.False(File.Exists(Path.Combine(temporary.Path, "achievements.json")));
    }

    [Fact]
    public void The_host_reads_a_story_from_an_archived_receipt_without_writing()
    {
        using var temporary = new StoryReportTemporaryDirectory();
        var exhibition = PlayAndArchive(temporary.Path, "match_storyhost", seed: 48UL);
        using var registry = new AgentSessionRegistry(
            new ReplayStore(temporary.Path),
            archiveStore: new AgentExhibitionArchiveStore(temporary.Path));

        var report = registry.GetExhibitionStory(exhibition.Receipt.ReceiptHash);

        Assert.True(report.IsAvailable);
        Assert.Equal(exhibition.Receipt.ReceiptHash, report.ReceiptHash);
        Assert.Equal(AgentExhibitionStoryRefuse.None, report.Refuse);
        Assert.False(File.Exists(Path.Combine(temporary.Path, "agent_arena", "agent_passports.json")));
        Assert.Equal(
            AgentExhibitionStoryRefuse.NotArchived,
            registry.GetExhibitionStory(new string('0', 64)).Refuse);
    }

    [Fact]
    public void The_report_refuses_a_null_loader()
    {
        Assert.Throws<ArgumentNullException>(() =>
            AgentExhibitionStoryReportV1.FromArchive(null, null!));
    }

    private static AgentExhibitionStoryV1 SyntheticStory() =>
        new(
            AgentExhibitionStoryV1.Contract,
            "receipt",
            "route",
            "agent",
            null,
            AgentExhibitionStoryV1.CatalogIdValue,
            AgentExhibitionStoryV1.SelectorIdValue,
            AgentExhibitionStoryV1.PaceIdValue,
            [
                new AgentHighlightV1(
                    AgentHighlightV1.Contract,
                    AgentHighlightLane.Agent,
                    8,
                    AgentHighlightKind.LeadChange,
                    0,
                    (int)AgentScoreRelation.Ahead),
                new AgentHighlightV1(
                    AgentHighlightV1.Contract,
                    AgentHighlightLane.Agent,
                    40,
                    AgentHighlightKind.TerminalWon,
                    0,
                    null),
            ],
            [0, 1],
            [
                new AgentMontageWindowV1(
                    AgentMontageWindowV1.Contract,
                    AgentHighlightLane.Agent,
                    0,
                    7,
                    AgentMontageRate.Selected),
                new AgentMontageWindowV1(
                    AgentMontageWindowV1.Contract,
                    AgentHighlightLane.Agent,
                    8,
                    11,
                    AgentMontageRate.Linger),
                new AgentMontageWindowV1(
                    AgentMontageWindowV1.Contract,
                    AgentHighlightLane.Agent,
                    12,
                    15,
                    AgentMontageRate.Selected),
                new AgentMontageWindowV1(
                    AgentMontageWindowV1.Contract,
                    AgentHighlightLane.Agent,
                    16,
                    33,
                    AgentMontageRate.Skip),
                new AgentMontageWindowV1(
                    AgentMontageWindowV1.Contract,
                    AgentHighlightLane.Agent,
                    34,
                    39,
                    AgentMontageRate.Selected),
                new AgentMontageWindowV1(
                    AgentMontageWindowV1.Contract,
                    AgentHighlightLane.Agent,
                    40,
                    43,
                    AgentMontageRate.Linger),
            ]);

    private static StoryReportFixture PlayAndArchive(
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
        return new StoryReportFixture(
            Assert.IsType<AgentExhibitionReceiptV2>(
                registry.GetExhibitionReceipt(started.MatchHandle).Receipt));
    }

    private sealed record StoryReportFixture(AgentExhibitionReceiptV2 Receipt);

    private sealed class StoryReportTemporaryDirectory : IDisposable
    {
        public StoryReportTemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "VibeSnakeAgentStoryReportTests",
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
