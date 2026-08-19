using VibeSnake.AgentPlay;
using VibeSnake.Rules;

namespace VibeSnake.Rules.Tests;

public sealed class AgentExhibitionStoryTests
{
    [Fact]
    public void A_story_binds_the_receipt_and_ignores_display_time()
    {
        var (receipt, replay) = PlayFirstTurn();
        var shown = receipt.WithDisplayTime("2026-08-18T00:00:00Z");
        var shownLater = receipt.WithDisplayTime("2031-01-02T03:04:05Z");

        var first = Assert.IsType<AgentExhibitionStoryV1>(
            AgentExhibitionStory.TryCreate(shown, replay, null, out var refuse));
        var second = Assert.IsType<AgentExhibitionStoryV1>(
            AgentExhibitionStory.TryCreate(shownLater, replay, null, out var laterRefuse));

        Assert.Equal(AgentExhibitionStoryRefuse.None, refuse);
        Assert.Equal(AgentExhibitionStoryRefuse.None, laterRefuse);
        Assert.Equal(receipt.ReceiptHash, first.ReceiptHash);
        Assert.Equal(receipt.RouteIdentityHash, first.RouteIdentityHash);
        Assert.Equal(receipt.AgentReplayPayloadHash, first.AgentReplayPayloadHash);
        Assert.Equal(first.ReceiptHash, second.ReceiptHash);
        Assert.Equal(first.Highlights.Count, second.Highlights.Count);
        Assert.True(receipt.LessonOutcome is { AllRequirementsSatisfied: true });
        Assert.Contains(
            first.Highlights,
            highlight => highlight.Kind == AgentHighlightKind.LessonAllRequirements);
        Assert.True(first.TurningPointIndexes.Count <= AgentExhibitionStoryV1.MaximumTurningPoints);
        Assert.True(first.Highlights.Count <= AgentExhibitionStoryV1.MaximumHighlights);
        AssertMontageCoversEveryTick(first);
    }

    [Fact]
    public void The_montage_covers_every_tick_exactly_once()
    {
        var (receipt, replay) = PlayFirstTurn();
        var story = Assert.IsType<AgentExhibitionStoryV1>(
            AgentExhibitionStory.TryCreate(receipt, replay, null, out _));

        AssertMontageCoversEveryTick(story);
        Assert.Contains(story.Montage, window => window.Rate == AgentMontageRate.Linger);
    }

    [Fact]
    public void A_tampered_receipt_or_replay_is_not_a_story()
    {
        var (receipt, replay) = PlayFirstTurn();
        var tampered = receipt with { Score = receipt.Score + 1 };

        Assert.Null(AgentExhibitionStory.TryCreate(
            tampered,
            replay,
            null,
            out var invalid));
        Assert.Equal(AgentExhibitionStoryRefuse.InvalidReceipt, invalid);

        var other = PlayClassic();
        Assert.Null(AgentExhibitionStory.TryCreate(
            receipt,
            other.Replay,
            null,
            out var mismatch));
        Assert.Equal(AgentExhibitionStoryRefuse.AgentReplayHashMismatch, mismatch);
    }

    [Fact]
    public void A_rivalry_without_the_rival_tape_is_refused()
    {
        var (receipt, replay, _) = PlayClassic(rivalPersonalityId: "optimal");
        Assert.NotNull(receipt.RivalReplayPayloadHash);

        Assert.Null(AgentExhibitionStory.TryCreate(receipt, replay, null, out var refuse));
        Assert.Equal(AgentExhibitionStoryRefuse.RivalReplayMissing, refuse);
    }

    [Fact]
    public void A_rivalry_story_needs_both_tapes_and_ignores_a_wrong_rival_tape()
    {
        var (receipt, replay, rival) = PlayClassic(rivalPersonalityId: "optimal");
        var story = Assert.IsType<AgentExhibitionStoryV1>(
            AgentExhibitionStory.TryCreate(receipt, replay, rival, out var refuse));

        Assert.Equal(AgentExhibitionStoryRefuse.None, refuse);
        Assert.Equal(receipt.RivalReplayPayloadHash, story.RivalReplayPayloadHash);
        AssertMontageCoversEveryTick(story);

        var other = PlayClassic();
        Assert.Null(AgentExhibitionStory.TryCreate(
            receipt,
            replay,
            other.Replay,
            out var mismatch));
        Assert.Equal(AgentExhibitionStoryRefuse.RivalReplayHashMismatch, mismatch);
    }

    [Fact]
    public void A_longer_practice_still_covers_every_tick_and_can_skip()
    {
        var (receipt, replay) = PlayWrapLine();
        var story = Assert.IsType<AgentExhibitionStoryV1>(
            AgentExhibitionStory.TryCreate(receipt, replay, null, out _));

        AssertMontageCoversEveryTick(story);
        Assert.True(story.Highlights.Count > 0);
        var cursor = AgentExhibitionStoryReportV1.At(story, story.Montage[^1].EndTickInclusive + 40);
        Assert.Equal(story.Montage[^1].EndTickInclusive, cursor.Tick);
        Assert.Equal(cursor.Tick, cursor.NextTurningPointTick);
    }

    [Fact]
    public void Death_and_combo_practices_publish_their_terminal_highlights()
    {
        foreach (var lessonId in new[]
        {
            AgentSignalSchoolCatalog.DeathReadId,
            AgentSignalSchoolCatalog.ComboRouteId,
        })
        {
            var (receipt, replay) = PlayLesson(lessonId);
            var story = Assert.IsType<AgentExhibitionStoryV1>(
                AgentExhibitionStory.TryCreate(receipt, replay, null, out _));
            AssertMontageCoversEveryTick(story);
            Assert.Contains(
                story.Highlights,
                highlight => highlight.Kind is AgentHighlightKind.TerminalDied
                    or AgentHighlightKind.TerminalWon
                    or AgentHighlightKind.ComboMilestone
                    or AgentHighlightKind.LessonAllRequirements);
        }
    }

    [Fact]
    public void A_longer_rivalry_can_record_a_lead_change()
    {
        var session = new AgentMatchSession(new AgentMatchOptions(
            "story-rivalry",
            RunModeCatalog.ClassicId,
            RunModeCatalog.CurrentModeVersion,
            91UL,
            AgentSeedVisibility.Open,
            24,
            rivalPersonalityId: "optimal"));
        var observation = session.Observe();
        AgentMatchResultV5? result = null;
        for (var step = 0; step < 24 && result is null; step++)
        {
            var moved = session.SubmitAction(new AgentActionRequest(
                "story-rivalry-" + step,
                observation.Tick,
                observation.StateHash,
                AgentAction.Continue));
            observation = moved.Observation;
            result = moved.MatchResult;
        }

        result ??= session.Finish();
        var receipt = Assert.IsType<AgentExhibitionReceiptV2>(session.TryCreateExhibitionReceipt());
        var story = Assert.IsType<AgentExhibitionStoryV1>(
            AgentExhibitionStory.TryCreate(
                receipt,
                result.VerifiedReplay,
                result.VerifiedRivalReplay,
                out _));
        AssertMontageCoversEveryTick(story);
        Assert.NotNull(story.RivalReplayPayloadHash);
    }

    [Fact]
    public void Declared_intent_changes_are_highlights()
    {
        var session = new AgentMatchSession(new AgentMatchOptions(
            "story-intent",
            RunModeCatalog.ClassicId,
            RunModeCatalog.CurrentModeVersion,
            7UL,
            AgentSeedVisibility.Open,
            3));
        var observation = session.Observe();
        observation = session.SubmitAction(new AgentActionRequest(
            "story-intent-0",
            observation.Tick,
            observation.StateHash,
            AgentAction.Continue,
            AgentPublicIntent.SeekFood)).Observation;
        observation = session.SubmitAction(new AgentActionRequest(
            "story-intent-1",
            observation.Tick,
            observation.StateHash,
            AgentAction.Continue,
            AgentPublicIntent.Recover)).Observation;
        var finished = session.Finish();
        var receipt = Assert.IsType<AgentExhibitionReceiptV2>(session.TryCreateExhibitionReceipt());
        var story = Assert.IsType<AgentExhibitionStoryV1>(
            AgentExhibitionStory.TryCreate(receipt, finished.VerifiedReplay, null, out _));

        Assert.Contains(
            story.Highlights,
            highlight => highlight.Kind == AgentHighlightKind.IntentChanged);
        AssertMontageCoversEveryTick(story);
    }

    [Fact]
    public void Building_a_story_does_not_write_player_data()
    {
        using var temporary = new StoryTemporaryDirectory();
        var (receipt, replay) = PlayFirstTurn();

        Assert.NotNull(AgentExhibitionStory.TryCreate(receipt, replay, null, out _));
        Assert.False(File.Exists(Path.Combine(temporary.Path, "agent_arena", "agent_passports.json")));
        Assert.False(File.Exists(Path.Combine(temporary.Path, "agent_arena", "exhibition_archive.json")));
        Assert.False(File.Exists(Path.Combine(temporary.Path, "preferences.json")));
        Assert.False(File.Exists(Path.Combine(temporary.Path, "achievements.json")));
    }

    private static void AssertMontageCoversEveryTick(AgentExhibitionStoryV1 story)
    {
        Assert.NotEmpty(story.Montage);
        Assert.Equal(0, story.Montage[0].StartTick);
        var expected = 0;
        foreach (var window in story.Montage)
        {
            Assert.Equal(AgentMontageWindowV1.Contract, window.Schema);
            Assert.True(window.EndTickInclusive >= window.StartTick);
            Assert.Equal(expected, window.StartTick);
            expected = window.EndTickInclusive + 1;
        }
    }

    private static (AgentExhibitionReceiptV2 Receipt, RunReplay Replay) PlayFirstTurn()
    {
        var definition = AgentSignalSchoolCatalog.Get(AgentSignalSchoolCatalog.FirstTurnId);
        var session = new AgentMatchSession(new AgentMatchOptions(
            "story-first-turn",
            definition.ModeId,
            RunModeCatalog.CurrentModeVersion,
            definition.PracticeSeed,
            AgentSeedVisibility.Open,
            definition.MaximumSteps,
            lessonId: definition.Id));
        var observation = session.Observe();
        var opposite = observation.Direction switch
        {
            Direction.Up => AgentAction.Down,
            Direction.Down => AgentAction.Up,
            Direction.Left => AgentAction.Right,
            _ => AgentAction.Left,
        };
        var rejected = session.SubmitAction(new AgentActionRequest(
            "story-reversal",
            observation.Tick,
            observation.StateHash,
            opposite));
        observation = rejected.Observation;
        AgentMatchResultV5? result = null;
        for (var step = 0; step < definition.MaximumSteps && result is null; step++)
        {
            if (observation.LessonProgress!.AllRequirementsSatisfied)
            {
                break;
            }

            var moved = session.SubmitAction(new AgentActionRequest(
                "story-" + step,
                observation.Tick,
                observation.StateHash,
                AgentLessonRouteDriver.ChooseAction(definition.Id, observation)));
            observation = moved.Observation;
            result = moved.MatchResult;
        }

        result ??= session.Finish();
        var receipt = Assert.IsType<AgentExhibitionReceiptV2>(session.TryCreateExhibitionReceipt());
        return (receipt, result.VerifiedReplay);
    }

    private static (AgentExhibitionReceiptV2 Receipt, RunReplay Replay) PlayWrapLine() =>
        PlayLesson(AgentSignalSchoolCatalog.WrapLineId);

    private static (AgentExhibitionReceiptV2 Receipt, RunReplay Replay) PlayLesson(string lessonId)
    {
        var definition = AgentSignalSchoolCatalog.Get(lessonId);
        var session = new AgentMatchSession(new AgentMatchOptions(
            "story-wrap-line",
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
                "story-wrap-" + step,
                observation.Tick,
                observation.StateHash,
                AgentLessonRouteDriver.ChooseAction(definition.Id, observation)));
            observation = moved.Observation;
            result = moved.MatchResult;
        }

        result ??= session.Finish();
        return (
            Assert.IsType<AgentExhibitionReceiptV2>(session.TryCreateExhibitionReceipt()),
            result.VerifiedReplay);
    }

    private static (AgentExhibitionReceiptV2 Receipt, RunReplay Replay, RunReplay? Rival) PlayClassic(
        string? rivalPersonalityId = null)
    {
        var session = new AgentMatchSession(new AgentMatchOptions(
            "story-classic",
            RunModeCatalog.ClassicId,
            RunModeCatalog.CurrentModeVersion,
            42UL,
            AgentSeedVisibility.Open,
            1,
            rivalPersonalityId: rivalPersonalityId));
        var observation = session.Observe();
        var moved = session.SubmitAction(new AgentActionRequest(
            "story-classic-move",
            observation.Tick,
            observation.StateHash,
            AgentAction.Continue));
        Assert.True(moved.Accepted);
        var receipt = Assert.IsType<AgentExhibitionReceiptV2>(session.TryCreateExhibitionReceipt());
        return (receipt, moved.MatchResult!.VerifiedReplay, moved.MatchResult.VerifiedRivalReplay);
    }

    private sealed class StoryTemporaryDirectory : IDisposable
    {
        public StoryTemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "VibeSnakeAgentStoryTests",
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
