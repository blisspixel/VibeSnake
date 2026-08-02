namespace VibeSnake.Rules.Tests;

public sealed class RunReplayRecorderTests
{
    [Fact]
    public void Recorder_preserves_every_logical_attempt_and_matches_offline_capture()
    {
        var live = SnakeRun.Create(
            601UL,
            new RunConfig(Width: 12, Height: 8, StarvationTicks: 100));
        var initial = SnakeRun.RestoreCanonicalState(live.SerializeCanonicalState());
        var recorder = new RunReplayRecorder(live, checkpointInterval: 2);
        IReadOnlyList<Direction>[] commandsByStep =
        [
            [Direction.Left, Direction.Up, Direction.Down],
            [],
            [Direction.Left, Direction.Right],
        ];

        foreach (var commands in commandsByStep)
        {
            foreach (var command in commands)
            {
                Assert.True(recorder.TryRecordCommand(command));
                live.QueueDirection(command);
            }

            var result = live.Step();
            Assert.True(recorder.TryCompleteStep(result, live));
        }

        var finalized = recorder.Finish(live);

        Assert.True(finalized.IsSuccessful, finalized.Message);
        Assert.NotNull(finalized.Replay);
        Assert.Equal(ReplayRecordingState.Finalized, recorder.State);
        Assert.Equal(3, recorder.RecordedStepCount);
        Assert.Equal(0, recorder.PendingCommandCount);
        Assert.Equal(commandsByStep[0], finalized.Replay.Steps[0].Commands);
        Assert.Equal(commandsByStep[1], finalized.Replay.Steps[1].Commands);
        Assert.Equal(commandsByStep[2], finalized.Replay.Steps[2].Commands);
        Assert.Equal(
            RunReplay.Capture(initial, commandsByStep, checkpointInterval: 2).Serialize(),
            finalized.Replay.Serialize());
        Assert.True(finalized.Replay.Verify().IsValid);
        Assert.Same(finalized, recorder.Finish(live));
    }

    [Fact]
    public void Recorder_captures_and_verifies_a_terminal_run()
    {
        var live = SnakeRun.Create(
            602UL,
            new RunConfig(StarvationTicks: 1));
        var recorder = new RunReplayRecorder(live);

        var result = live.Step();

        Assert.True(recorder.TryCompleteStep(result, live));
        var finalized = recorder.Finish(live);
        Assert.True(finalized.IsSuccessful, finalized.Message);
        Assert.NotNull(finalized.Replay);
        Assert.True(finalized.Replay.Outcome.IsTerminal);
        Assert.Equal(DeathCause.Starvation, finalized.Replay.Outcome.DeathCause);
        Assert.Equal([0, 1], finalized.Replay.Checkpoints.Select(value => value.StepIndex));
    }

    [Fact]
    public void Recorder_fails_closed_when_the_reported_step_diverges()
    {
        var live = SnakeRun.Create(603UL);
        var recorder = new RunReplayRecorder(live);
        Assert.True(recorder.TryRecordCommand(Direction.Up));
        live.QueueDirection(Direction.Up);
        var actual = live.Step();
        var falseResult = new RunStepResult(
            actual.Tick,
            actual.Events,
            actual.OrderedEvents,
            actual.Status,
            actual.DeathCause,
            AlternateHash(actual.StateHash));

        Assert.False(recorder.TryCompleteStep(falseResult, live));
        Assert.Equal(ReplayRecordingState.Failed, recorder.State);
        Assert.Equal(ReplayRecordingFailureCode.Diverged, recorder.FailureCode);
        Assert.Equal(1, recorder.FirstDivergentStep);
        Assert.Contains("step result", recorder.FailureMessage, StringComparison.OrdinalIgnoreCase);

        var finalized = recorder.Finish(live);
        Assert.False(finalized.IsSuccessful);
        Assert.Null(finalized.Replay);
        Assert.Equal(ReplayRecordingFailureCode.Diverged, finalized.FailureCode);
    }

    [Fact]
    public void Recorder_fails_closed_when_the_live_state_does_not_match()
    {
        var live = SnakeRun.Create(604UL);
        var recorder = new RunReplayRecorder(live);
        var result = live.Step();
        var differentLive = SnakeRun.Create(605UL);

        Assert.False(recorder.TryCompleteStep(result, differentLive));
        Assert.Equal(ReplayRecordingFailureCode.Diverged, recorder.FailureCode);
        Assert.Contains("live state", recorder.FailureMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Recorder_bounds_commands_without_interrupting_the_live_run()
    {
        var live = SnakeRun.Create(606UL);
        var recorder = new RunReplayRecorder(live);

        for (var index = 0; index < ReplayStep.MaximumCommands; index++)
        {
            Assert.True(recorder.TryRecordCommand(Direction.Up));
        }

        Assert.False(recorder.TryRecordCommand(Direction.Left));
        Assert.Equal(ReplayRecordingState.Failed, recorder.State);
        Assert.Equal(
            ReplayRecordingFailureCode.CommandLimitExceeded,
            recorder.FailureCode);
        Assert.False(recorder.TryCompleteStep(live.Step(), live));
        Assert.False(recorder.Finish(live).IsSuccessful);
    }

    [Fact]
    public void Recorder_rejects_terminal_origins_invalid_intervals_and_pending_finish()
    {
        var terminal = SnakeRun.Create(
            607UL,
            new RunConfig(StarvationTicks: 1));
        terminal.Step();

        Assert.Throws<ArgumentException>(() => new RunReplayRecorder(terminal));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new RunReplayRecorder(SnakeRun.Create(608UL), 0));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new RunReplayRecorder(
                SnakeRun.Create(609UL),
                RunReplay.MaximumSteps + 1));

        var live = SnakeRun.Create(610UL);
        var recorder = new RunReplayRecorder(live);
        Assert.True(recorder.TryRecordCommand(Direction.Up));

        var finalized = recorder.Finish(live);
        Assert.False(finalized.IsSuccessful);
        Assert.Equal(
            ReplayRecordingFailureCode.PendingCommands,
            finalized.FailureCode);
        Assert.False(recorder.TryRecordCommand(Direction.Left));
    }

    [Fact]
    public void Recorder_rejects_invalid_commands_and_null_live_runs()
    {
        var live = SnakeRun.Create(611UL);
        var recorder = new RunReplayRecorder(live);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => recorder.TryRecordCommand((Direction)255));
        Assert.Throws<ArgumentNullException>(
            () => recorder.TryCompleteStep(default, null!));
        Assert.Throws<ArgumentNullException>(() => recorder.Finish(null!));
    }

    private static string AlternateHash(string hash) =>
        (hash[0] == '0' ? "1" : "0") + hash[1..];
}
