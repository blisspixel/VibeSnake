namespace VibeSnake.Rules.Tests;

public sealed class RunStepResultTests
{
    [Fact]
    public void Equal_results_compare_equal_including_operators()
    {
        var left = new RunStepResult(
            tick: 3,
            events: RunEvent.Moved | RunEvent.ComboExpired,
            orderedEvents:
            [
                new RunEventDetail(RunEventKind.ComboExpired, Value: 0),
                new RunEventDetail(RunEventKind.Moved, Position: new GridPoint(1, 2)),
            ],
            status: RunStatus.Running,
            deathCause: DeathCause.None,
            stateHash: "aabbccddeeff0011");
        var right = new RunStepResult(
            tick: 3,
            events: RunEvent.Moved | RunEvent.ComboExpired,
            orderedEvents:
            [
                new RunEventDetail(RunEventKind.ComboExpired, Value: 0),
                new RunEventDetail(RunEventKind.Moved, Position: new GridPoint(1, 2)),
            ],
            status: RunStatus.Running,
            deathCause: DeathCause.None,
            stateHash: "aabbccddeeff0011");

        Assert.True(left.Equals(right));
        Assert.True(left == right);
        Assert.False(left != right);
        Assert.Equal(left.GetHashCode(), right.GetHashCode());
        Assert.True(left.Equals((object)right));
    }

    [Fact]
    public void Different_ordered_events_are_not_equal()
    {
        var left = new RunStepResult(
            tick: 1,
            events: RunEvent.Moved,
            orderedEvents: [new RunEventDetail(RunEventKind.Moved, Position: new GridPoint(0, 0))],
            status: RunStatus.Running,
            deathCause: DeathCause.None,
            stateHash: "1122334455667788");
        var right = new RunStepResult(
            tick: 1,
            events: RunEvent.Moved,
            orderedEvents: [new RunEventDetail(RunEventKind.Moved, Position: new GridPoint(1, 0))],
            status: RunStatus.Running,
            deathCause: DeathCause.None,
            stateHash: "1122334455667788");

        Assert.False(left.Equals(right));
        Assert.True(left != right);
        Assert.False(left.Equals(null));
        Assert.False(left.Equals("not-a-result"));
    }
}
