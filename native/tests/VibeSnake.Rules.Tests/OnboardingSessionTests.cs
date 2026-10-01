using VibeSnake.Rules;

namespace VibeSnake.Rules.Tests;

public sealed class OnboardingSessionTests
{
    [Fact]
    public void Current_lesson_previews_food_and_shield_before_the_requested_action()
    {
        var session = new OnboardingSession();
        session.SubmitDirection(Direction.Up);
        session.SubmitDirection(Direction.Down);
        session.SubmitDirection(Direction.Left);

        var wrappedHash = session.Snapshot.StateHash;
        var food = session.PresentationSnapshot;
        Assert.Equal(new GridPoint(1, 2), food.Head);
        Assert.Equal(Direction.Right, food.Direction);
        Assert.Equal(new GridPoint(2, 2), food.Food);
        Assert.Equal(0, food.Tick);
        Assert.Equal(wrappedHash, session.Snapshot.StateHash);
        Assert.False(session.SubmitDirection(Direction.Up).InputAccepted);
        Assert.Same(food, session.PresentationSnapshot);

        session.SubmitDirection(Direction.Right);
        session.SubmitDirection(Direction.Right);
        session.SubmitDirection(Direction.Right);
        var deadHash = session.Snapshot.StateHash;
        var shield = session.PresentationSnapshot;
        Assert.Equal(RunStatus.Running, shield.Status);
        Assert.Equal(new GridPoint(1, 2), shield.Head);
        Assert.Equal(Direction.Right, shield.Direction);
        Assert.NotNull(shield.PowerPickup);
        Assert.Equal(PowerKind.Shield, shield.PowerPickup.Kind);
        Assert.Equal(new GridPoint(2, 2), shield.PowerPickup.Position);
        Assert.Equal(deadHash, session.Snapshot.StateHash);
        Assert.False(session.SubmitDirection(Direction.Up).InputAccepted);
        Assert.Same(shield, session.PresentationSnapshot);

        session.SubmitDirection(Direction.Right);
        Assert.True(session.PresentationSnapshot.HasShield);
        Assert.False(shield.HasShield);
        Assert.Equal(0, shield.Tick);
        session.Reset();
        Assert.Equal(session.Snapshot.StateHash, session.PresentationSnapshot.StateHash);
    }

    [Fact]
    public void Presentation_reads_leave_every_first_action_and_rules_hash_unchanged()
    {
        var observed = new OnboardingSession();
        var unobserved = new OnboardingSession();
        var directions = new[]
        {
            Direction.Up, Direction.Down, Direction.Left, Direction.Right,
            Direction.Right, Direction.Right, Direction.Right,
        };
        foreach (var direction in directions)
        {
            var before = observed.Snapshot.StateHash;
            _ = observed.PresentationSnapshot;
            _ = observed.PresentationSnapshot;
            Assert.Equal(before, observed.Snapshot.StateHash);
            Assert.Equal(unobserved.SubmitDirection(direction), observed.SubmitDirection(direction));
            Assert.Equal(unobserved.Snapshot.StateHash, observed.Snapshot.StateHash);
        }

        Assert.Equal(unobserved.SubmitPause(), observed.SubmitPause());
        Assert.Equal(unobserved.SubmitRestart(), observed.SubmitRestart());
        Assert.Equal(unobserved.Snapshot.StateHash, observed.Snapshot.StateHash);
    }

    [Fact]
    public void Presentation_snapshots_expose_only_read_only_detached_collections()
    {
        var session = new OnboardingSession();
        var initial = session.PresentationSnapshot;
        AssertReadOnly(initial);
        session.SubmitDirection(Direction.Up);
        session.SubmitDirection(Direction.Down);
        session.SubmitDirection(Direction.Left);
        AssertReadOnly(session.PresentationSnapshot);
        session.SubmitDirection(Direction.Right);
        session.SubmitDirection(Direction.Right);
        session.SubmitDirection(Direction.Right);
        AssertReadOnly(session.PresentationSnapshot);
        Assert.Equal(new GridPoint(2, 2), initial.Head);
        Assert.Equal(0, initial.Tick);
    }

    private static void AssertReadOnly(RunSnapshot snapshot)
    {
        Assert.Throws<NotSupportedException>(() =>
            ((IList<GridPoint>)snapshot.Body)[0] = new GridPoint(99, 99));
        Assert.Throws<NotSupportedException>(() =>
            ((IList<Direction>)snapshot.PendingDirections).Add(Direction.Down));
        Assert.Throws<NotSupportedException>(() =>
            ((IList<GridPoint>)snapshot.DetachedObstacles).Add(new GridPoint(99, 99)));
    }

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
