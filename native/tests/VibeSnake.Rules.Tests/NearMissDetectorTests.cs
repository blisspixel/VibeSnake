namespace VibeSnake.Rules.Tests;

public sealed class NearMissDetectorTests
{
    [Theory]
    [InlineData(0, null, 0, null, false)]
    [InlineData(1, null, 0, null, false)]
    [InlineData(2, NearMissKind.DangerWarning, 0, "", true)]
    [InlineData(3, NearMissKind.BodyProximity, 1, "CLOSE CALL!", false)]
    [InlineData(4, NearMissKind.BodyProximity, 2, "THREADING THE NEEDLE!", false)]
    public void Body_proximity_has_distinct_warning_and_reward_tiers(
        int adjacentCount,
        NearMissKind? expectedKind,
        int expectedBonus,
        string? expectedMessage,
        bool expectedWarning)
    {
        var detector = new NearMissDetector();
        var head = new GridPoint(10, 10);
        GridPoint[] neighbors =
        [
            new(11, 10),
            new(10, 11),
            new(9, 10),
            new(10, 9),
        ];
        var body = neighbors.Take(adjacentCount).ToHashSet();

        var result = detector.CheckBodyProximity(head, body, snakeLength: 10);

        if (expectedKind is null)
        {
            Assert.Null(result);
            return;
        }

        Assert.NotNull(result);
        Assert.Equal(expectedKind, result.Value.Kind);
        Assert.Equal(expectedBonus, result.Value.ScoreBonus);
        Assert.Equal(expectedMessage, result.Value.Message);
        Assert.Equal(expectedWarning, result.Value.IsWarning);
        Assert.Equal(head, result.Value.Position);
    }

    [Fact]
    public void Body_proximity_requires_a_long_enough_snake()
    {
        var detector = new NearMissDetector();
        var body = new HashSet<GridPoint>
        {
            new(11, 10),
            new(10, 11),
            new(9, 10),
            new(10, 9),
        };

        Assert.Null(detector.CheckBodyProximity(new GridPoint(10, 10), body, snakeLength: 7));
    }

    [Fact]
    public void Reward_cooldown_blocks_events_but_not_warnings()
    {
        var detector = new NearMissDetector();
        var rewardBody = new HashSet<GridPoint>
        {
            new(11, 10),
            new(10, 11),
            new(9, 10),
        };
        var warningBody = new HashSet<GridPoint>
        {
            new(11, 10),
            new(10, 11),
        };
        var head = new GridPoint(10, 10);

        Assert.NotNull(detector.CheckBodyProximity(head, rewardBody, 10));
        Assert.Null(detector.CheckBodyProximity(head, rewardBody, 10));
        Assert.True(detector.CheckBodyProximity(head, warningBody, 10)!.Value.IsWarning);

        detector.AdvanceTicks(detector.CooldownTicks);
        Assert.NotNull(detector.CheckBodyProximity(head, rewardBody, 10));
    }

    [Theory]
    [InlineData(1, 1, "EDGE RIDE")]
    [InlineData(20, 2, "EDGE RIDE")]
    [InlineData(50, 5, "EDGE LORD!")]
    [InlineData(80, 8, "EDGE MASTERY!")]
    [InlineData(100, 10, "EDGE MASTERY!")]
    [InlineData(200, 10, "EDGE MASTERY!")]
    public void Edge_ride_reward_scales_and_caps(
        int snakeLength,
        int expectedBonus,
        string expectedMessage)
    {
        var eventResult = new NearMissDetector().CheckEdgeRide(
            new GridPoint(0, 15),
            Direction.Down,
            snakeLength,
            gridWidth: 64,
            gridHeight: 33);

        Assert.NotNull(eventResult);
        Assert.Equal(NearMissKind.EdgeRide, eventResult.Value.Kind);
        Assert.Equal(expectedBonus, eventResult.Value.ScoreBonus);
        Assert.Equal(expectedMessage, eventResult.Value.Message);
    }

    [Theory]
    [InlineData(0, 15, Direction.Down)]
    [InlineData(63, 15, Direction.Up)]
    [InlineData(32, 0, Direction.Right)]
    [InlineData(32, 32, Direction.Left)]
    public void Edge_ride_detects_parallel_motion_on_every_edge(
        int x,
        int y,
        Direction direction)
    {
        Assert.NotNull(
            new NearMissDetector().CheckEdgeRide(
                new GridPoint(x, y),
                direction,
                snakeLength: 50,
                gridWidth: 64,
                gridHeight: 33));
    }

    [Theory]
    [InlineData(32, 16, Direction.Down)]
    [InlineData(1, 16, Direction.Down)]
    [InlineData(0, 15, Direction.Right)]
    [InlineData(32, 0, Direction.Down)]
    public void Edge_ride_ignores_non_edges_and_perpendicular_motion(
        int x,
        int y,
        Direction direction)
    {
        Assert.Null(
            new NearMissDetector().CheckEdgeRide(
                new GridPoint(x, y),
                direction,
                snakeLength: 50,
                gridWidth: 64,
                gridHeight: 33));
    }

    [Theory]
    [InlineData(30, false)]
    [InlineData(29, true)]
    [InlineData(1, true)]
    [InlineData(0, true)]
    public void Clutch_eat_uses_strict_remaining_hunger_boundary(
        int hungerRemaining,
        bool expected)
    {
        var result = new NearMissDetector().CheckClutchEat(hungerRemaining);
        Assert.Equal(expected, result is not null);
        if (result is not null)
        {
            Assert.Equal(NearMissKind.ClutchEat, result.Value.Kind);
            Assert.Equal(1, result.Value.ScoreBonus);
            Assert.Equal("CLUTCH!", result.Value.Message);
            Assert.Null(result.Value.Position);
        }
    }

    [Fact]
    public void Style_points_only_reward_active_boost()
    {
        var detector = new NearMissDetector();
        Assert.Null(detector.CheckStylePoints(false));
        Assert.Equal(NearMissKind.StylePoints, detector.CheckStylePoints(true)!.Value.Kind);
    }

    [Fact]
    public void Recent_events_drive_bounded_combo_and_expire()
    {
        var detector = new NearMissDetector();
        Assert.Equal(1.0, detector.GetComboMultiplier());

        detector.TrackEvent(new NearMissEvent(NearMissKind.BodyProximity, new GridPoint(1, 1), 1, "CLOSE CALL!", false));
        Assert.Equal(1.0, detector.GetComboMultiplier());

        detector.TrackEvent(new NearMissEvent(NearMissKind.BodyProximity, new GridPoint(2, 2), 1, "CLOSE CALL!", false));
        Assert.Equal(1.5, detector.GetComboMultiplier());

        detector.TrackEvent(new NearMissEvent(NearMissKind.BodyProximity, new GridPoint(3, 3), 1, "CLOSE CALL!", false));
        detector.TrackEvent(new NearMissEvent(NearMissKind.BodyProximity, new GridPoint(4, 4), 1, "CLOSE CALL!", false));
        Assert.Equal(2.0, detector.GetComboMultiplier());

        detector.AdvanceTicks(detector.EventTimeoutTicks);
        Assert.Empty(detector.RecentEvents);
        Assert.Equal(1.0, detector.GetComboMultiplier());
    }

    [Fact]
    public void Warnings_are_not_tracked_for_combo()
    {
        var detector = new NearMissDetector();
        detector.TrackEvent(
            new NearMissEvent(
                NearMissKind.DangerWarning,
                new GridPoint(0, 0),
                0,
                string.Empty,
                IsWarning: true));
        Assert.Empty(detector.RecentEvents);
        Assert.Equal(1.0, detector.GetComboMultiplier());
    }

    [Fact]
    public void Reset_clears_cooldown_and_recent_events()
    {
        var detector = new NearMissDetector();
        var body = new HashSet<GridPoint>
        {
            new(11, 10),
            new(10, 11),
            new(9, 10),
        };
        var reward = detector.CheckBodyProximity(new GridPoint(10, 10), body, 10);
        Assert.NotNull(reward);
        detector.TrackEvent(reward.Value);
        detector.Reset();
        Assert.Equal(0, detector.RewardCooldownTicksRemaining);
        Assert.Empty(detector.RecentEvents);
        Assert.NotNull(detector.CheckBodyProximity(new GridPoint(10, 10), body, 10));
    }
}
