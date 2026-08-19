using VibeSnake.AgentHost;
using VibeSnake.AgentPlay;
using VibeSnake.Persistence;
using VibeSnake.Rules;

namespace VibeSnake.Rules.Tests;

[Collection(AgentHostIntegrationGroup.Name)]
public sealed class AgentQualificationTests
{
    [Fact]
    public void The_division_manifest_is_closed_and_self_identifying()
    {
        var manifest = AgentQualificationCatalog.Manifest;
        Assert.Equal(AgentDivisionManifestV1.Contract, manifest.Schema);
        Assert.Equal(8, manifest.Divisions.Count);
        Assert.Equal(8, manifest.Divisions.Select(entry => entry.DivisionId).Distinct().Count());
        Assert.All(
            manifest.Divisions,
            entry =>
            {
                Assert.Equal(AgentDivisionManifestEntryV1.Contract, entry.Schema);
                Assert.Equal(AgentPassportV4.SymbolicStepObservationProfile, entry.ObservationProfile);
                Assert.True(AgentQualificationCatalog.IsPublishedDivision(entry.DivisionId));
            });
        Assert.Equal(64, manifest.ManifestHash.Length);
        Assert.Equal(manifest.ManifestHash, AgentQualificationCatalog.Manifest.ManifestHash);
    }

    [Fact]
    public void Practice_seeds_are_not_qualification_time_seeds()
    {
        var decks = AgentQualificationCatalog.Decks;
        Assert.Equal(AgentSignalSchoolCatalog.All.Count, decks.Practice.Count);
        Assert.All(decks.Practice, seed => Assert.Equal(AgentDeckKind.Practice, seed.DeckKind));
        Assert.Equal(18, decks.QualificationTime.Count);
        foreach (var practice in decks.Practice)
        {
            Assert.DoesNotContain(
                decks.QualificationTime,
                other => other.GameplaySeed == practice.GameplaySeed
                    && string.Equals(other.LessonId, practice.LessonId, StringComparison.Ordinal));
        }
    }

    [Fact]
    public void Qualification_time_lesson_seeds_are_the_public_non_practice_boards()
    {
        Assert.Equal(
            new ulong[] { 1, 2 },
            AgentQualificationCatalog.QualificationTimeLessons(AgentSignalSchoolCatalog.WrapLineId)
                .Select(seed => seed.GameplaySeed)
                .ToArray());
        Assert.Contains(
            AgentQualificationCatalog.Decks.QualificationTime,
            seed => seed.StyleContractId == AgentStyleContractCatalog.StillwaterId
                && seed.GameplaySeed == 42UL);
        Assert.Contains(
            AgentQualificationCatalog.Decks.QualificationTime,
            seed => seed.RivalPersonalityId == "optimal" && seed.GameplaySeed == 91UL);
        Assert.Equal(10, AgentQualificationCatalog.RivalBreakerTerms.Count);
        Assert.Equal(
            AgentStyleContractCatalog.StillwaterId,
            AgentQualificationCatalog.TermsFor("zen_master").StyleContractId);
        Assert.Null(AgentQualificationCatalog.TermsFor("optimal").StyleContractId);
    }

    [Fact]
    public void A_completed_practice_is_not_a_qualifying_result()
    {
        var (receipt, _) = PlayLesson(AgentSignalSchoolCatalog.FirstTurnId);
        var eligibility = AgentQualificationReportV1.Classify(receipt);

        Assert.Equal(AgentQualificationClass.PracticeComplete, eligibility.Class);
        Assert.False(eligibility.Qualifying);
        Assert.Equal(AgentDeckKind.Practice, eligibility.DeckKind);
        Assert.Equal(receipt.EndReason, eligibility.EndReason);
    }

    [Fact]
    public void Voluntary_finish_of_a_running_exhibition_does_not_qualify()
    {
        var receipt = PlayClassic(maximumSteps: 8, finishEarly: true);
        var eligibility = AgentQualificationReportV1.Classify(receipt);

        Assert.Equal(AgentMatchEndReason.AgentFinished, receipt.EndReason);
        Assert.Equal(AgentQualificationClass.NonQualifyingFinish, eligibility.Class);
        Assert.False(eligibility.Qualifying);
    }

    [Fact]
    public void A_capped_non_practice_run_qualifies_in_its_own_division()
    {
        var receipt = PlayClassic(maximumSteps: 1, finishEarly: false);
        var eligibility = AgentQualificationReportV1.Classify(receipt);

        Assert.Equal(AgentMatchEndReason.StepLimit, receipt.EndReason);
        Assert.Equal(AgentQualificationClass.QualifyingCapped, eligibility.Class);
        Assert.True(eligibility.Qualifying);
        Assert.True(AgentQualificationCatalog.IsPublishedDivision(eligibility.DivisionId));
    }

    [Fact]
    public void Standings_never_mix_divisions_or_count_practice()
    {
        var practice = PlayLesson(AgentSignalSchoolCatalog.WrapLineId).Receipt;
        var capped = PlayClassic(maximumSteps: 1, finishEarly: false);
        var finished = PlayClassic(maximumSteps: 8, finishEarly: true);
        var qualificationBoard = PlayClassic(maximumSteps: 1, finishEarly: false, seed: 1UL);
        var report = AgentQualificationReportV1.FromArchive(
            [
                Archive(practice),
                Archive(capped),
                Archive(finished),
                Archive(qualificationBoard),
            ]);

        Assert.DoesNotContain(
            report.Standings,
            row => row.BestReceiptHash == practice.ReceiptHash);
        Assert.DoesNotContain(
            report.Standings,
            row => row.BestReceiptHash == finished.ReceiptHash);
        var standing = Assert.Single(report.Standings);
        Assert.Equal(2, standing.QualifyingCount);
        Assert.Contains(
            standing.BestReceiptHash,
            new[] { capped.ReceiptHash, qualificationBoard.ReceiptHash });
        Assert.Equal(capped.Division.DivisionId, standing.DivisionId);
        Assert.Equal(
            1,
            report.Generalization.Single(row =>
                row.LessonId == AgentSignalSchoolCatalog.WrapLineId).PracticeComplete);
        Assert.All(
            report.Generalization,
            row => Assert.Equal(row.QualificationTimeComplete - row.PracticeComplete, row.Gap));
    }

    [Fact]
    public void A_qualifying_rivalry_against_the_proof_is_broken_on_score()
    {
        var receipt = PlayClassic(
            maximumSteps: 1,
            finishEarly: false,
            rivalPersonalityId: "optimal");
        Assert.NotNull(receipt.RivalPersonalityId);
        Assert.NotNull(receipt.RivalScore);
        var kind = AgentQualificationReportV1.RivalBreakerKind(receipt);
        Assert.Equal(AgentQualificationClass.QualifyingCapped, AgentQualificationReportV1.Classify(receipt).Class);
        Assert.Equal(
            receipt.Score > receipt.RivalScore
                ? AgentRivalBreakerKind.Broken
                : receipt.Score == receipt.RivalScore
                    ? AgentRivalBreakerKind.Level
                    : AgentRivalBreakerKind.Behind,
            kind);
        var finished = PlayClassic(
            maximumSteps: 8,
            finishEarly: true,
            rivalPersonalityId: "optimal");
        Assert.Equal(
            AgentRivalBreakerKind.NonQualifying,
            AgentQualificationReportV1.RivalBreakerKind(finished));
    }

    [Fact]
    public void An_unknown_rival_has_no_breaker_terms()
    {
        Assert.Throws<ArgumentException>(() => AgentQualificationCatalog.TermsFor("not-a-rival"));
        Assert.NotNull(AgentQualificationCatalog.FindPracticeLesson(AgentSignalSchoolCatalog.WrapLineId));
        Assert.False(AgentQualificationCatalog.IsPublishedDivision("not-a-division"));
    }

    [Fact]
    public void Open_exhibitions_outside_the_decks_still_classify()
    {
        var receipt = PlayClassic(maximumSteps: 1, finishEarly: false);
        Assert.Null(AgentQualificationCatalog.DeckKindOf(receipt));
        Assert.Equal(
            AgentRivalBreakerKind.NotARivalry,
            AgentQualificationReportV1.RivalBreakerKind(receipt));
        var failed = receipt with { EndReason = AgentMatchEndReason.ReplayFailure };
        Assert.Equal(
            AgentQualificationClass.Ineligible,
            AgentQualificationReportV1.Classify(failed).Class);
        var unknownDivision = receipt with
        {
            Division = receipt.Division with { DivisionId = "not-published" },
        };
        Assert.Equal(
            AgentQualificationClass.Ineligible,
            AgentQualificationReportV1.Classify(unknownDivision).Class);
        var unreadableSeed = receipt with { GameplaySeed = "not-a-seed" };
        Assert.Null(AgentQualificationCatalog.DeckKindOf(unreadableSeed));
    }

    [Fact]
    public void An_incomplete_practice_is_not_qualifying()
    {
        var definition = AgentSignalSchoolCatalog.Get(AgentSignalSchoolCatalog.FirstTurnId);
        var session = new AgentMatchSession(new AgentMatchOptions(
            "qualify-incomplete",
            definition.ModeId,
            RunModeCatalog.CurrentModeVersion,
            definition.PracticeSeed,
            AgentSeedVisibility.Open,
            definition.MaximumSteps,
            lessonId: definition.Id));
        _ = session.Finish();
        var receipt = Assert.IsType<AgentExhibitionReceiptV2>(session.TryCreateExhibitionReceipt());
        Assert.Equal(
            AgentQualificationClass.PracticeIncomplete,
            AgentQualificationReportV1.Classify(receipt).Class);
    }

    [Fact]
    public void A_styled_qualification_seed_is_on_the_published_deck()
    {
        var session = new AgentMatchSession(new AgentMatchOptions(
            "qualify-stillwater-deck",
            RunModeCatalog.ClassicId,
            RunModeCatalog.CurrentModeVersion,
            42UL,
            AgentSeedVisibility.Open,
            1,
            styleContractId: AgentStyleContractCatalog.StillwaterId));
        var observation = session.Observe();
        _ = session.SubmitAction(new AgentActionRequest(
            "qualify-stillwater-0",
            observation.Tick,
            observation.StateHash,
            AgentAction.Continue));
        var receipt = Assert.IsType<AgentExhibitionReceiptV2>(session.TryCreateExhibitionReceipt());
        Assert.Equal(AgentDeckKind.QualificationTime, AgentQualificationCatalog.DeckKindOf(receipt));
    }

    [Fact]
    public void A_mapped_rival_without_its_style_is_score_not_breaker()
    {
        var receipt = PlayClassic(
            maximumSteps: 1,
            finishEarly: false,
            rivalPersonalityId: "zen_master");
        var kind = AgentQualificationReportV1.RivalBreakerKind(receipt);
        Assert.Contains(
            kind,
            new[]
            {
                AgentRivalBreakerKind.BeatScore,
                AgentRivalBreakerKind.Level,
                AgentRivalBreakerKind.Behind,
            });
        var proof = PlayClassic(
            maximumSteps: 1,
            finishEarly: false,
            rivalPersonalityId: "optimal",
            seed: 91UL);
        Assert.Equal(AgentDeckKind.QualificationTime, AgentQualificationCatalog.DeckKindOf(proof));
    }

    [Fact]
    public void Standings_filter_by_agent_and_keep_policy_versions_apart()
    {
        var first = PlayClassic(maximumSteps: 1, finishEarly: false);
        var second = PlayClassic(maximumSteps: 1, finishEarly: false);
        var report = AgentQualificationReportV1.FromArchive(
            [Archive(first), Archive(second)],
            first.Passport.AgentId);
        Assert.All(
            report.Eligibility,
            row => Assert.Equal(first.Passport.AgentId, row.AgentId));
    }

    [Fact]
    public void The_host_report_reads_an_empty_archive_without_writing()
    {
        using var temporary = new QualificationTemporaryDirectory();
        var replay = new ReplayStore(temporary.Path);
        var archive = new AgentExhibitionArchiveStore(temporary.Path);
        using var registry = new AgentSessionRegistry(
            replay,
            archiveStore: archive);
        var report = registry.GetQualificationReport(null);
        Assert.Equal(AgentQualificationReportV1.Contract, report.Schema);
        Assert.Empty(report.Eligibility);
        Assert.Empty(report.Standings);
        Assert.Equal(8, report.Manifest.Divisions.Count);
        Assert.False(File.Exists(System.IO.Path.Combine(temporary.Path, "agent_arena", "exhibition_archive.json")));
    }

    [Fact]
    public void Building_the_report_does_not_write_player_data()
    {
        using var temporary = new QualificationTemporaryDirectory();
        var receipt = PlayClassic(maximumSteps: 1, finishEarly: false);
        _ = AgentQualificationReportV1.FromArchive([Archive(receipt)]);
        Assert.False(File.Exists(Path.Combine(temporary.Path, "agent_arena", "agent_passports.json")));
        Assert.False(File.Exists(Path.Combine(temporary.Path, "agent_arena", "exhibition_archive.json")));
        Assert.False(File.Exists(Path.Combine(temporary.Path, "preferences.json")));
    }

    private static AgentArchivedExhibitionV2 Archive(AgentExhibitionReceiptV2 receipt) =>
        AgentArchivedExhibitionV2.Create(
            receipt,
            "agent.replay",
            receipt.RivalReplayPayloadHash is null ? null : "rival.replay");

    private static (AgentExhibitionReceiptV2 Receipt, RunReplay Replay) PlayLesson(string lessonId)
    {
        var definition = AgentSignalSchoolCatalog.Get(lessonId);
        var session = new AgentMatchSession(new AgentMatchOptions(
            "qualify-" + lessonId,
            definition.ModeId,
            RunModeCatalog.CurrentModeVersion,
            definition.PracticeSeed,
            AgentSeedVisibility.Open,
            definition.MaximumSteps,
            lessonId: definition.Id));
        var observation = session.Observe();
        if (lessonId == AgentSignalSchoolCatalog.FirstTurnId)
        {
            var opposite = observation.Direction switch
            {
                Direction.Up => AgentAction.Down,
                Direction.Down => AgentAction.Up,
                Direction.Left => AgentAction.Right,
                _ => AgentAction.Left,
            };
            observation = session.SubmitAction(new AgentActionRequest(
                "qualify-reversal",
                observation.Tick,
                observation.StateHash,
                opposite)).Observation;
        }

        AgentMatchResultV5? result = null;
        for (var step = 0; step < definition.MaximumSteps && result is null; step++)
        {
            if (observation.LessonProgress!.AllRequirementsSatisfied)
            {
                break;
            }

            var moved = session.SubmitAction(new AgentActionRequest(
                "qualify-" + step,
                observation.Tick,
                observation.StateHash,
                AgentLessonRouteDriver.ChooseAction(lessonId, observation)));
            observation = moved.Observation;
            result = moved.MatchResult;
        }

        result ??= session.Finish();
        return (
            Assert.IsType<AgentExhibitionReceiptV2>(session.TryCreateExhibitionReceipt()),
            result.VerifiedReplay);
    }

    private static AgentExhibitionReceiptV2 PlayClassic(
        int maximumSteps,
        bool finishEarly,
        string? rivalPersonalityId = null,
        ulong seed = 123_456_789UL)
    {
        var session = new AgentMatchSession(new AgentMatchOptions(
            "qualify-classic-" + Guid.NewGuid().ToString("N"),
            RunModeCatalog.ClassicId,
            RunModeCatalog.CurrentModeVersion,
            seed,
            AgentSeedVisibility.Open,
            maximumSteps,
            rivalPersonalityId: rivalPersonalityId));
        var observation = session.Observe();
        AgentMatchResultV5? result = null;
        var limit = finishEarly ? Math.Max(1, maximumSteps - 1) : maximumSteps;
        for (var step = 0; step < limit && result is null; step++)
        {
            var moved = session.SubmitAction(new AgentActionRequest(
                "qualify-classic-" + step,
                observation.Tick,
                observation.StateHash,
                AgentAction.Continue));
            observation = moved.Observation;
            result = moved.MatchResult;
        }

        result ??= session.Finish();
        return Assert.IsType<AgentExhibitionReceiptV2>(session.TryCreateExhibitionReceipt());
    }

    private sealed class QualificationTemporaryDirectory : IDisposable
    {
        public QualificationTemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "VibeSnakeAgentQualificationTests",
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
