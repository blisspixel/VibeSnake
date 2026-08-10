using VibeSnake.Persistence;

namespace VibeSnake.Rules.Tests;

public sealed class AudioOutputRecoveryTrackerTests
{
    [Fact]
    public void Starts_available_and_allows_immediate_playback()
    {
        var tracker = new AudioOutputRecoveryTracker();

        Assert.True(tracker.IsAvailable);
        Assert.True(tracker.ShouldAttemptPlayback(0));
        Assert.Equal(0, tracker.ConsecutiveFailures);
        Assert.Equal(string.Empty, tracker.LastFailureReason);
        Assert.Null(tracker.NoteSuccess());
    }

    [Fact]
    public void First_failure_emits_one_safe_transition()
    {
        var tracker = new AudioOutputRecoveryTracker();

        var transition = tracker.NoteFailure(500, "  output\nmissing\u0007  ");

        Assert.NotNull(transition);
        Assert.Equal(AudioOutputRecoveryKind.Unavailable, transition.Value.Kind);
        Assert.Equal("outputmissing", transition.Value.FailureReason);
        Assert.Equal("AUDIO UNAVAILABLE: VISUAL CUES ACTIVE", transition.Value.Caption);
        Assert.False(tracker.IsAvailable);
        Assert.Equal(1, tracker.ConsecutiveFailures);
        Assert.Equal(1_500UL, tracker.RetryAtMilliseconds);
    }

    [Fact]
    public void Backoff_blocks_attempts_until_its_boundary()
    {
        var tracker = new AudioOutputRecoveryTracker();
        tracker.NoteFailure(500, "missing");

        Assert.False(tracker.ShouldAttemptPlayback(1_499));
        Assert.True(tracker.ShouldAttemptPlayback(1_500));
    }

    [Fact]
    public void Repeated_failures_refresh_backoff_without_duplicate_events()
    {
        var tracker = new AudioOutputRecoveryTracker();
        tracker.NoteFailure(100, "first");

        Assert.Null(tracker.NoteFailure(1_100, "second"));
        Assert.Equal(2, tracker.ConsecutiveFailures);
        Assert.Equal("second", tracker.LastFailureReason);
        Assert.Equal(2_100UL, tracker.RetryAtMilliseconds);
    }

    [Fact]
    public void Success_after_failure_emits_recovery_and_resets_state()
    {
        var tracker = new AudioOutputRecoveryTracker();
        tracker.NoteFailure(100, "missing bus");
        tracker.NoteFailure(1_100, "missing bus");

        var transition = tracker.NoteSuccess();

        Assert.NotNull(transition);
        Assert.Equal(AudioOutputRecoveryKind.Recovered, transition.Value.Kind);
        Assert.Equal(2, transition.Value.ConsecutiveFailures);
        Assert.Equal("missing bus", transition.Value.FailureReason);
        Assert.Equal("AUDIO RESTORED", transition.Value.Caption);
        Assert.True(tracker.IsAvailable);
        Assert.Equal(0, tracker.ConsecutiveFailures);
        Assert.Equal(0UL, tracker.RetryAtMilliseconds);
        Assert.Null(tracker.NoteSuccess());
    }

    [Fact]
    public void Failure_reason_is_bounded_and_blank_falls_back()
    {
        var longReason = new string('x', AudioOutputRecoveryTracker.MaximumFailureReasonCharacters + 20);
        var tracker = new AudioOutputRecoveryTracker();
        tracker.NoteFailure(0, longReason);

        Assert.Equal(
            AudioOutputRecoveryTracker.MaximumFailureReasonCharacters,
            tracker.LastFailureReason.Length);

        var blankTracker = new AudioOutputRecoveryTracker();
        blankTracker.NoteFailure(0, "\r\n\u0007");
        Assert.Equal("Audio output unavailable.", blankTracker.LastFailureReason);
    }

    [Fact]
    public void Retry_deadline_saturates_at_ulong_max_value()
    {
        var tracker = new AudioOutputRecoveryTracker();
        tracker.NoteFailure(ulong.MaxValue - 10, "missing");

        Assert.Equal(ulong.MaxValue, tracker.RetryAtMilliseconds);
        Assert.False(tracker.ShouldAttemptPlayback(ulong.MaxValue - 1));
        Assert.True(tracker.ShouldAttemptPlayback(ulong.MaxValue));
    }
}
