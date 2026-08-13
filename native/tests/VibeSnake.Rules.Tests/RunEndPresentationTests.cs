using VibeSnake.Rules;

namespace VibeSnake.Rules.Tests;

public sealed class RunEndPresentationTests
{
    private static readonly string[] CenturyAndFirstBite = ["century", "first_bite"];

    [Fact]
    public void Self_collision_summary_has_ordered_metrics_recovery_and_unlocks()
    {
        var run = SelfCollisionRun();
        run.Step();
        var summary = RunEndSummary.Create(
            run,
            personalBest: 25,
            isNewPersonalBest: true,
            newlyUnlockedIds: ["first_bite", "first_bite", "century", " "]);

        Assert.Equal("RUN ENDED", summary.Outcome);
        Assert.Equal("SELF COLLISION", summary.Cause);
        Assert.Contains("Shield", summary.RecoveryHint, StringComparison.Ordinal);
        Assert.Equal(run.Score, summary.Score);
        Assert.Equal(25, summary.PersonalBest);
        Assert.True(summary.IsNewPersonalBest);
        Assert.Equal(run.Body.Count, summary.Length);
        Assert.Equal(run.Tick, summary.SurvivalSteps);
        Assert.Equal(CenturyAndFirstBite, summary.NewlyUnlockedIds);
    }

    [Fact]
    public void Starvation_and_victory_have_specific_attribution()
    {
        var starvation = SnakeRun.CreateForTesting(
            new RunConfig(Width: 5, Height: 4, StarvationTicks: 1, PowerSpawnIntervalTicks: 0),
            [new GridPoint(1, 1)],
            Direction.Right,
            new GridPoint(4, 3),
            hungerTicksRemaining: 1);
        starvation.Step();
        var starved = RunEndSummary.Create(starvation, 0, false);
        Assert.Equal("STARVATION", starved.Cause);
        Assert.Contains("hunger", starved.RecoveryHint, StringComparison.OrdinalIgnoreCase);

        var victory = SnakeRun.CreateForTesting(
            new RunConfig(Width: 2, Height: 2, StarvationTicks: 10, PowerSpawnIntervalTicks: 0),
            [
                new GridPoint(0, 0),
                new GridPoint(0, 1),
                new GridPoint(1, 1),
            ],
            Direction.Up,
            new GridPoint(1, 0),
            hungerTicksRemaining: 10);
        victory.Step();
        var won = RunEndSummary.Create(victory, victory.Score, true);
        Assert.Equal("GRID COMPLETE", won.Outcome);
        Assert.Equal("EVERY FREE CELL WAS CLAIMED", won.Cause);
    }

    [Fact]
    public void Summary_rejects_live_unknown_and_impossible_best_inputs()
    {
        var live = SnakeRun.Create(1UL);
        Assert.Throws<ArgumentException>(() => RunEndSummary.Create(live, 0, false));

        var dead = SelfCollisionRun();
        dead.Step();
        Assert.Throws<ArgumentOutOfRangeException>(
            () => RunEndSummary.Create(dead, dead.Score - 1, false));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => RunEndSummary.Create(dead, SnakeRun.MaximumScore + 1, false));

    }

    [Fact]
    public void Restart_gate_rejects_terminal_sequence_and_accepts_later_intent()
    {
        var gate = new RestartIntentGate();
        Assert.False(gate.CanRestart(0));
        gate.NoteTerminal(12);
        Assert.False(gate.CanRestart(12));
        Assert.False(gate.CanRestart(11));
        Assert.True(gate.CanRestart(13));
        gate.Reset();
        Assert.False(gate.CanRestart(99));
        Assert.Throws<ArgumentOutOfRangeException>(() => gate.NoteTerminal(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => gate.CanRestart(-1));
    }

    private static SnakeRun SelfCollisionRun() => SnakeRun.CreateForTesting(
        new RunConfig(Width: 8, Height: 6, StarvationTicks: 100, PowerSpawnIntervalTicks: 0),
        [
            new GridPoint(1, 1),
            new GridPoint(1, 2),
            new GridPoint(2, 2),
            new GridPoint(2, 1),
        ],
        Direction.Down,
        new GridPoint(6, 4),
        hungerTicksRemaining: 100);
}
