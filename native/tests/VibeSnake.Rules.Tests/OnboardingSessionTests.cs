using VibeSnake.Rules;

namespace VibeSnake.Rules.Tests;

public sealed class OnboardingSessionTests
{
    [Fact]
    public void Complete_action_path_teaches_every_required_lesson_without_score_eligibility()
    {
        var session = new OnboardingSession();

        Assert.Equal(OnboardingLesson.Turning, session.Lesson);
        Assert.Equal(OnboardingSession.Identity, "vibesnake-onboarding@1-unscored");
        Assert.False(session.CompetitiveScoreEligible);
        Assert.False(session.PersistsAchievements);
        Assert.False(session.RecordsReplay);

        var turn = session.SubmitDirection(Direction.Up);
        AssertAdvance(turn, OnboardingLesson.Turning, OnboardingLesson.InvalidReversal);
        Assert.True(turn.Events.HasFlag(RunEvent.Moved));

        var reversal = session.SubmitDirection(Direction.Down);
        AssertAdvance(reversal, OnboardingLesson.InvalidReversal, OnboardingLesson.Wrapping);
        Assert.Equal(0, session.Snapshot.Head.X);

        var wrap = session.SubmitDirection(Direction.Left);
        AssertAdvance(wrap, OnboardingLesson.Wrapping, OnboardingLesson.FoodAndScore);
        Assert.True(wrap.Events.HasFlag(RunEvent.Wrapped));
        Assert.Equal(OnboardingSession.ScenarioWidth - 1, session.Snapshot.Head.X);

        var food = session.SubmitDirection(Direction.Right);
        AssertAdvance(food, OnboardingLesson.FoodAndScore, OnboardingLesson.Starvation);
        Assert.True(food.Events.HasFlag(RunEvent.AteFood));

        var warning = session.SubmitDirection(Direction.Right);
        Assert.True(warning.InputAccepted);
        Assert.False(warning.LessonAdvanced);
        Assert.Equal(OnboardingLesson.Starvation, warning.CurrentLesson);
        Assert.True(warning.Events.HasFlag(RunEvent.StarvationWarning));

        var starvation = session.SubmitDirection(Direction.Right);
        AssertAdvance(starvation, OnboardingLesson.Starvation, OnboardingLesson.PowerUp);
        Assert.True(starvation.Events.HasFlag(RunEvent.Died));
        Assert.Equal(RunStatus.Dead, session.Snapshot.Status);
        Assert.Equal(DeathCause.Starvation, session.Snapshot.DeathCause);

        var power = session.SubmitDirection(Direction.Right);
        AssertAdvance(power, OnboardingLesson.PowerUp, OnboardingLesson.Pause);
        Assert.True(power.Events.HasFlag(RunEvent.PowerCollected));
        Assert.True(session.Snapshot.HasShield);

        var pause = session.SubmitPause();
        AssertAdvance(pause, OnboardingLesson.Pause, OnboardingLesson.Restart);

        var restart = session.SubmitRestart();
        AssertAdvance(restart, OnboardingLesson.Restart, OnboardingLesson.Complete);
        Assert.True(session.IsComplete);
        Assert.Equal(OnboardingCopyIds.Complete, restart.CopyId);
    }

    [Fact]
    public void Wrong_actions_are_rejected_without_advancing_and_reset_is_exact()
    {
        var session = new OnboardingSession();
        var initialHash = session.Snapshot.StateHash;

        foreach (var direction in new[] { Direction.Right, Direction.Down, Direction.Left })
        {
            var rejected = session.SubmitDirection(direction);
            Assert.False(rejected.InputAccepted);
            Assert.False(rejected.LessonAdvanced);
            Assert.Equal(OnboardingLesson.Turning, session.Lesson);
            Assert.Equal(initialHash, session.Snapshot.StateHash);
        }

        Assert.False(session.SubmitPause().InputAccepted);
        Assert.False(session.SubmitRestart().InputAccepted);
        Assert.Throws<ArgumentOutOfRangeException>(
            () => session.SubmitDirection((Direction)byte.MaxValue));

        session.SubmitDirection(Direction.Up);
        session.Reset();
        Assert.Equal(OnboardingLesson.Turning, session.Lesson);
        Assert.Equal(initialHash, session.Snapshot.StateHash);
        Assert.False(session.IsComplete);
    }

    [Fact]
    public void Lesson_specific_wrong_directions_retain_each_scenario()
    {
        var session = new OnboardingSession();
        session.SubmitDirection(Direction.Up);
        Assert.False(session.SubmitDirection(Direction.Left).InputAccepted);
        session.SubmitDirection(Direction.Down);
        Assert.False(session.SubmitDirection(Direction.Right).InputAccepted);
        session.SubmitDirection(Direction.Left);
        Assert.False(session.SubmitDirection(Direction.Up).InputAccepted);
        session.SubmitDirection(Direction.Right);
        Assert.False(session.SubmitDirection(Direction.Left).InputAccepted);
        session.SubmitDirection(Direction.Right);
        session.SubmitDirection(Direction.Right);
        Assert.False(session.SubmitDirection(Direction.Up).InputAccepted);
        session.SubmitDirection(Direction.Right);
        Assert.False(session.SubmitDirection(Direction.Right).InputAccepted);
    }

    private static void AssertAdvance(
        OnboardingAdvance advance,
        OnboardingLesson previous,
        OnboardingLesson current)
    {
        Assert.True(advance.InputAccepted);
        Assert.True(advance.LessonAdvanced);
        Assert.Equal(previous, advance.PreviousLesson);
        Assert.Equal(current, advance.CurrentLesson);
        Assert.Contains(advance.CopyId, OnboardingCopyIds.All);
    }

    [Fact]
    public void Copy_ids_are_unique_stable_and_complete()
    {
        Assert.Equal(18, OnboardingCopyIds.All.Count);
        Assert.Equal(
            OnboardingCopyIds.All.Count,
            OnboardingCopyIds.All.Distinct(StringComparer.Ordinal).Count());
        Assert.All(
            OnboardingCopyIds.All,
            copyId => Assert.StartsWith("onboarding.lesson.", copyId, StringComparison.Ordinal));
    }
}
