using VibeSnake.AgentPlay;
using VibeSnake.Rules;

namespace VibeSnake.Rules.Tests;

/// <summary>
/// AA-08's ranking screen, machine half. Every decision the Godot standings
/// page can show is decided here so presentation never invents a grade, a
/// mixed ranking, or a standing from practice or voluntary finish.
/// </summary>
public sealed class AgentQualificationBrowseTests
{
    [Fact]
    public void An_empty_archive_still_lists_every_published_division()
    {
        var report = AgentQualificationBrowseReportV1.Create([]);

        Assert.Equal(AgentQualificationBrowseReportV1.Contract, report.Schema);
        Assert.True(report.ArchiveIsEmpty);
        Assert.Equal(8, report.Divisions.Count);
        Assert.Equal(
            AgentQualificationCatalog.Manifest.Divisions.Select(entry => entry.DivisionId),
            report.Divisions.Select(page => page.Division.DivisionId));
        Assert.All(report.Divisions, page =>
        {
            Assert.Equal(AgentQualificationDivisionViewV1.Contract, page.Schema);
            Assert.True(page.IsEmpty);
            Assert.Empty(page.Standings);
        });
        Assert.Equal(0, report.QualifyingCount);
        Assert.Equal(-1, report.SelectedStandingIndex);
        Assert.Null(report.SelectedStanding);
        Assert.Null(report.HandoffReceiptHash);
    }

    [Fact]
    public void Practice_and_voluntary_finish_stay_off_the_standings()
    {
        var practice = PlayLesson(AgentSignalSchoolCatalog.WrapLineId);
        var finished = PlayClassic(maximumSteps: 8, finishEarly: true);
        var capped = PlayClassic(maximumSteps: 1, finishEarly: false);
        var report = AgentQualificationBrowseReportV1.Create(
            [Archive(practice), Archive(finished), Archive(capped)]);

        Assert.Equal(1, report.QualifyingCount);
        Assert.Equal(1, report.PracticeCompleteCount);
        Assert.Equal(1, report.NonQualifyingFinishCount);
        var occupied = Assert.Single(report.Divisions, page => !page.IsEmpty);
        var standing = Assert.Single(occupied.Standings);
        Assert.Equal(capped.ReceiptHash, standing.BestReceiptHash);
        Assert.Equal(1, standing.Rank);
        Assert.Equal(AgentQualificationClass.QualifyingCapped, standing.BestClass);
        Assert.Equal(1, standing.PracticeCompleteCount);
        Assert.Equal(0, standing.QualificationTimeCompleteCount);
        Assert.Equal(-1, standing.GeneralizationGap);
        Assert.DoesNotContain(
            occupied.Standings,
            row => row.BestReceiptHash == practice.ReceiptHash);
        Assert.DoesNotContain(
            occupied.Standings,
            row => row.BestReceiptHash == finished.ReceiptHash);
        Assert.Equal(capped.ReceiptHash, report.WithDivision(occupied.Position).HandoffReceiptHash);
    }

    [Fact]
    public void Rank_is_score_order_inside_one_division_and_policy()
    {
        var later = PlayClassic(maximumSteps: 1, finishEarly: false, agentId: "browse-later");
        var earlier = PlayClassic(maximumSteps: 2, finishEarly: false, agentId: "browse-earlier");
        var report = AgentQualificationBrowseReportV1.Create(
            [Archive(later), Archive(earlier)]);

        var occupied = Assert.Single(report.Divisions, page => !page.IsEmpty);
        Assert.Equal(2, occupied.StandingCount);
        Assert.All(
            occupied.Standings,
            row => Assert.Equal(later.Division.DivisionId, row.DivisionId));
        Assert.Equal(
            occupied.Standings.OrderByDescending(row => row.BestScore)
                .ThenByDescending(row => row.BestFinalTick)
                .ThenBy(row => row.AgentId, StringComparer.Ordinal)
                .Select(row => row.BestReceiptHash),
            occupied.Standings.Select(row => row.BestReceiptHash));
        Assert.Equal([1, 2], occupied.Standings.Select(row => row.Rank).ToArray());
        Assert.Equal(
            occupied.Standings[0].PolicyVersion,
            occupied.Standings[1].PolicyVersion);
    }

    [Fact]
    public void Two_policy_versions_never_share_a_rank()
    {
        var first = PlayClassic(maximumSteps: 1, finishEarly: false);
        var secondReceipt = PlayClassic(maximumSteps: 1, finishEarly: false);
        var original = secondReceipt.Passport;
        var second = secondReceipt with
        {
            Passport = new AgentPassportV4(
                original.Schema,
                original.AgentId,
                "other-policy",
                original.DisplayName,
                original.AvatarId,
                original.AccentId,
                original.StationId,
                original.ObservationProfile,
                original.ActionProfile),
        };
        var report = AgentQualificationBrowseReportV1.Create(
            [Archive(first), Archive(second)]);

        var occupied = Assert.Single(report.Divisions, page => !page.IsEmpty);
        Assert.Equal(2, occupied.StandingCount);
        Assert.All(occupied.Standings, row => Assert.Equal(1, row.Rank));
        Assert.Equal(
            new[] { first.Passport.PolicyVersion, "other-policy" }.Order(StringComparer.Ordinal),
            occupied.Standings.Select(row => row.PolicyVersion).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Division_and_standing_selection_clamp_instead_of_wrapping()
    {
        var capped = PlayClassic(maximumSteps: 1, finishEarly: false);
        var report = AgentQualificationBrowseReportV1.Create([Archive(capped)]);
        var occupied = Assert.Single(report.Divisions, page => !page.IsEmpty);

        Assert.Equal(occupied.Position, report.SelectedDivisionIndex);
        Assert.Equal(0, report.WithDivision(-4).SelectedDivisionIndex);
        Assert.Equal(7, report.WithDivision(99).SelectedDivisionIndex);
        var onOccupied = report.WithDivision(occupied.Position);
        Assert.Equal(0, onOccupied.SelectedStandingIndex);
        Assert.Equal(0, onOccupied.WithStanding(-4).SelectedStandingIndex);
        Assert.Equal(0, onOccupied.WithStanding(99).SelectedStandingIndex);
        var emptyPage = report.WithDivision(occupied.Position == 0 ? 1 : 0);
        Assert.True(emptyPage.SelectedDivision.IsEmpty);
        Assert.Equal(-1, emptyPage.SelectedStandingIndex);
        Assert.Equal(-1, emptyPage.WithStanding(3).SelectedStandingIndex);
    }

    [Fact]
    public void A_qualifying_proof_rivalry_can_break_on_published_terms()
    {
        var receipt = PlayClassic(
            maximumSteps: 1,
            finishEarly: false,
            rivalPersonalityId: "optimal");
        var report = AgentQualificationBrowseReportV1.Create([Archive(receipt)]);
        var occupied = Assert.Single(report.Divisions, page => !page.IsEmpty);
        var standing = Assert.Single(occupied.Standings);

        Assert.Equal("optimal", standing.RivalPersonalityId);
        Assert.Equal(
            AgentQualificationReportV1.RivalBreakerKind(receipt),
            standing.RivalBreakerKind);
        Assert.Contains(
            standing.RivalBreakerKind,
            new[]
            {
                AgentRivalBreakerKind.Broken,
                AgentRivalBreakerKind.Level,
                AgentRivalBreakerKind.Behind,
            });
    }

    [Fact]
    public void Building_the_browse_view_does_not_write()
    {
        using var temporary = new BrowseTemporaryDirectory();
        var receipt = PlayClassic(maximumSteps: 1, finishEarly: false);
        _ = AgentQualificationBrowseReportV1.Create([Archive(receipt)]);
        Assert.False(File.Exists(Path.Combine(temporary.Path, "agent_arena", "exhibition_archive.json")));
        Assert.False(File.Exists(Path.Combine(temporary.Path, "agent_arena", "agent_passports.json")));
        Assert.False(File.Exists(Path.Combine(temporary.Path, "preferences.json")));
    }

    [Fact]
    public void The_browse_view_refuses_a_null_archive()
    {
        Assert.Throws<ArgumentNullException>(() =>
            AgentQualificationBrowseReportV1.Create(null!));
    }

    private static AgentArchivedExhibitionV2 Archive(AgentExhibitionReceiptV2 receipt) =>
        AgentArchivedExhibitionV2.Create(
            receipt,
            "agent.replay",
            receipt.RivalReplayPayloadHash is null ? null : "rival.replay");

    private static AgentExhibitionReceiptV2 PlayLesson(string lessonId)
    {
        var definition = AgentSignalSchoolCatalog.Get(lessonId);
        var session = new AgentMatchSession(new AgentMatchOptions(
            "qualify-browse-" + lessonId,
            definition.ModeId,
            RunModeCatalog.CurrentModeVersion,
            definition.PracticeSeed,
            AgentSeedVisibility.Open,
            definition.MaximumSteps,
            lessonId: definition.Id));
        var observation = session.Observe();
        AgentMatchResultV5? result = null;
        for (var step = 0; step < definition.MaximumSteps && result is null; step++)
        {
            if (observation.LessonProgress!.AllRequirementsSatisfied)
            {
                break;
            }

            var moved = session.SubmitAction(new AgentActionRequest(
                "qualify-browse-" + step,
                observation.Tick,
                observation.StateHash,
                AgentLessonRouteDriver.ChooseAction(lessonId, observation)));
            observation = moved.Observation;
            result = moved.MatchResult;
        }

        result ??= session.Finish();
        return Assert.IsType<AgentExhibitionReceiptV2>(session.TryCreateExhibitionReceipt());
    }

    private static AgentExhibitionReceiptV2 PlayClassic(
        int maximumSteps,
        bool finishEarly,
        string? rivalPersonalityId = null,
        string? agentId = null)
    {
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
        var session = new AgentMatchSession(new AgentMatchOptions(
            "qualify-browse-classic-" + Guid.NewGuid().ToString("N"),
            RunModeCatalog.ClassicId,
            RunModeCatalog.CurrentModeVersion,
            123_456_789UL,
            AgentSeedVisibility.Open,
            maximumSteps,
            rivalPersonalityId: rivalPersonalityId,
            passport: passport));
        var observation = session.Observe();
        AgentMatchResultV5? result = null;
        var limit = finishEarly ? Math.Max(1, maximumSteps - 1) : maximumSteps;
        for (var step = 0; step < limit && result is null; step++)
        {
            var moved = session.SubmitAction(new AgentActionRequest(
                "qualify-browse-classic-" + step,
                observation.Tick,
                observation.StateHash,
                AgentAction.Continue));
            observation = moved.Observation;
            result = moved.MatchResult;
        }

        result ??= session.Finish();
        return Assert.IsType<AgentExhibitionReceiptV2>(session.TryCreateExhibitionReceipt());
    }

    private sealed class BrowseTemporaryDirectory : IDisposable
    {
        public BrowseTemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "VibeSnakeAgentQualificationBrowseTests",
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
