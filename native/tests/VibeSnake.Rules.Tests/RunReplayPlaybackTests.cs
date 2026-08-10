namespace VibeSnake.Rules.Tests;

public sealed class RunReplayPlaybackTests
{
    [Fact]
    public void Playback_advances_recorded_commands_to_the_verified_outcome()
    {
        IReadOnlyList<Direction>[] commands =
        [
            [Direction.Up],
            [],
            [Direction.Left, Direction.Down],
            [Direction.Right],
        ];
        var replay = RunReplay.Capture(
            SnakeRun.Create(7_001UL),
            commands,
            checkpointInterval: 1,
            capturedAtUtc: "2026-08-08T00:00:00.000Z");
        var playback = new RunReplayPlayback(replay);

        Assert.True(playback.Verification.IsValid);
        Assert.Equal(0, playback.StepIndex);
        Assert.Equal(commands.Length, playback.StepCount);
        Assert.False(playback.IsComplete);
        Assert.Equal(0.0, playback.Progress);
        Assert.Equal(replay.Checkpoints[0].StateHash, playback.CurrentSnapshot.StateHash);

        var frames = new List<ReplayPlaybackFrame>();
        while (playback.TryAdvance(out var frame))
        {
            Assert.NotNull(frame);
            frames.Add(frame);
        }

        Assert.Equal(commands.Length, frames.Count);
        Assert.Equal(commands[0], frames[0].Commands);
        Assert.Equal(commands[^1], frames[^1].Commands);
        Assert.Equal(commands.Length, playback.StepIndex);
        Assert.True(playback.IsComplete);
        Assert.Equal(1.0, playback.Progress);
        Assert.Equal(replay.Outcome.StateHash, playback.CurrentSnapshot.StateHash);
        Assert.Equal(commands.Length, frames[^1].StepIndex);
        Assert.Equal(replay.Outcome.StateHash, frames[^1].Result.StateHash);
        Assert.Equal(replay.Outcome.StateHash, frames[^1].Snapshot.StateHash);
        Assert.False(playback.TryAdvance(out var completedFrame));
        Assert.Null(completedFrame);
    }

    [Fact]
    public void Playback_seek_and_reset_are_exact_and_repeatable()
    {
        var replay = RunReplay.Capture(
            SnakeRun.Create(7_002UL),
            [[Direction.Up], [], [Direction.Left], []],
            checkpointInterval: 1);
        var playback = new RunReplayPlayback(replay);

        playback.Seek(3);
        var thirdHash = playback.CurrentSnapshot.StateHash;
        Assert.Equal(3, playback.StepIndex);

        playback.Seek(1);
        Assert.Equal(replay.Checkpoints[1].StateHash, playback.CurrentSnapshot.StateHash);
        playback.Seek(3);
        Assert.Equal(thirdHash, playback.CurrentSnapshot.StateHash);

        playback.Reset();
        Assert.Equal(0, playback.StepIndex);
        Assert.Equal(replay.Checkpoints[0].StateHash, playback.CurrentSnapshot.StateHash);
        playback.Seek(playback.StepCount);
        Assert.True(playback.IsComplete);
        Assert.Equal(replay.Outcome.StateHash, playback.CurrentSnapshot.StateHash);
    }

    [Fact]
    public void Empty_playback_is_complete_and_seek_bounds_fail_closed()
    {
        var replay = RunReplay.Capture(
            SnakeRun.Create(7_003UL),
            Array.Empty<IReadOnlyList<Direction>>());
        var playback = new RunReplayPlayback(replay);

        Assert.True(playback.IsComplete);
        Assert.Equal(1.0, playback.Progress);
        playback.Seek(0);
        Assert.Throws<ArgumentOutOfRangeException>(() => playback.Seek(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => playback.Seek(1));
    }

    [Fact]
    public void Playback_rejects_null_and_deterministically_invalid_replays()
    {
        Assert.Throws<ArgumentNullException>(() => new RunReplayPlayback(null!));

        var valid = RunReplay.Capture(
            SnakeRun.Create(7_004UL),
            [[Direction.Up]],
            checkpointInterval: 1);
        var wrongInitialCheckpoint = new ReplayCheckpoint(
            0,
            AlternateHash(valid.Checkpoints[0].StateHash));
        var invalid = RunReplay.CreateForTesting(
            valid.InitialCanonicalState,
            valid.Steps,
            valid.CheckpointInterval,
            [wrongInitialCheckpoint, valid.Checkpoints[1]],
            valid.Outcome);

        Assert.Throws<ArgumentException>(() => new RunReplayPlayback(invalid));
    }

    private static string AlternateHash(string hash) =>
        (hash[0] == '0' ? "1" : "0") + hash[1..];
}
