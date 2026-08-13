namespace VibeSnake.Rules;

/// <summary>
/// Classification of a bounded risk-reward proximity or style event.
/// Thresholds and bonuses are provisional balance parameters.
/// </summary>
public enum NearMissKind : byte
{
    DangerWarning = 0,
    BodyProximity = 1,
    EdgeRide = 2,
    ClutchEat = 3,
    StylePoints = 4,
}

/// <summary>
/// One immutable near-miss classification. Warnings carry no score and do not
/// start the reward cooldown. A null position marks a non-spatial event.
/// </summary>
public readonly record struct NearMissEvent(
    NearMissKind Kind,
    GridPoint? Position,
    int ScoreBonus,
    string Message,
    bool IsWarning);

/// <summary>
/// Pure fixed-step near-miss detector. Cooldown and recent-event windows use
/// rules ticks so outcomes stay deterministic and independent of wall clocks.
/// </summary>
public sealed class NearMissDetector
{
    public const int MinimumSnakeLength = 8;
    public const int DefaultCooldownTicks = 30;
    public const int DefaultEventTimeoutTicks = 60;
    public const int ClutchRemainingTicks = 30;
    public const int MaximumEdgeRideBonus = 10;

    private readonly List<(NearMissEvent Event, int RemainingTicks)> _recentEvents = [];
    private int _rewardCooldownTicksRemaining;

    public NearMissDetector(
        int cooldownTicks = DefaultCooldownTicks,
        int eventTimeoutTicks = DefaultEventTimeoutTicks)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(cooldownTicks);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(eventTimeoutTicks);

        CooldownTicks = cooldownTicks;
        EventTimeoutTicks = eventTimeoutTicks;
    }

    public int CooldownTicks { get; }

    public int EventTimeoutTicks { get; }

    public int RewardCooldownTicksRemaining => _rewardCooldownTicksRemaining;

    public IReadOnlyList<NearMissEvent> RecentEvents
    {
        get
        {
            if (_recentEvents.Count == 0)
            {
                return Array.Empty<NearMissEvent>();
            }

            var copy = new NearMissEvent[_recentEvents.Count];
            for (var index = 0; index < _recentEvents.Count; index++)
            {
                copy[index] = _recentEvents[index].Event;
            }

            return copy;
        }
    }

    /// <summary>
    /// Advances cooldown and expires tracked combo events by one or more rules ticks.
    /// </summary>
    public void AdvanceTicks(int ticks = 1)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(ticks);

        if (ticks == 0)
        {
            return;
        }

        if (_rewardCooldownTicksRemaining > 0)
        {
            _rewardCooldownTicksRemaining = Math.Max(0, _rewardCooldownTicksRemaining - ticks);
        }

        if (_recentEvents.Count == 0)
        {
            return;
        }

        for (var index = _recentEvents.Count - 1; index >= 0; index--)
        {
            var remaining = _recentEvents[index].RemainingTicks - ticks;
            if (remaining <= 0)
            {
                _recentEvents.RemoveAt(index);
            }
            else
            {
                _recentEvents[index] = (_recentEvents[index].Event, remaining);
            }
        }
    }

    /// <summary>
    /// Classifies body occupancy in the eight non-wrapping cells around the head.
    /// Snakes shorter than <see cref="MinimumSnakeLength"/> are ignored.
    /// </summary>
    public NearMissEvent? CheckBodyProximity(
        GridPoint head,
        IReadOnlySet<GridPoint> bodyPositions,
        int snakeLength)
    {
        ArgumentNullException.ThrowIfNull(bodyPositions);
        if (snakeLength < MinimumSnakeLength)
        {
            return null;
        }

        var dangerCount = 0;
        for (var dx = -1; dx <= 1; dx++)
        {
            for (var dy = -1; dy <= 1; dy++)
            {
                if (dx == 0 && dy == 0)
                {
                    continue;
                }

                if (bodyPositions.Contains(new GridPoint(head.X + dx, head.Y + dy)))
                {
                    dangerCount++;
                }
            }
        }

        if (dangerCount >= 4)
        {
            if (_rewardCooldownTicksRemaining > 0)
            {
                return null;
            }

            _rewardCooldownTicksRemaining = CooldownTicks;
            return new NearMissEvent(
                NearMissKind.BodyProximity,
                head,
                ScoreBonus: 2,
                Message: "THREADING THE NEEDLE!",
                IsWarning: false);
        }

        if (dangerCount >= 3)
        {
            if (_rewardCooldownTicksRemaining > 0)
            {
                return null;
            }

            _rewardCooldownTicksRemaining = CooldownTicks;
            return new NearMissEvent(
                NearMissKind.BodyProximity,
                head,
                ScoreBonus: 1,
                Message: "CLOSE CALL!",
                IsWarning: false);
        }

        if (dangerCount == 2)
        {
            return new NearMissEvent(
                NearMissKind.DangerWarning,
                head,
                ScoreBonus: 0,
                Message: string.Empty,
                IsWarning: true);
        }

        return null;
    }

    /// <summary>
    /// Rewards motion parallel to a wrapping boundary. Bonus is
    /// <c>clamp(snakeLength / 10, 1, 10)</c>.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Performance",
        "CA1822:Mark members as static",
        Justification = "The established instance member is retained for public API compatibility.")]
    public NearMissEvent? CheckEdgeRide(
        GridPoint head,
        Direction direction,
        int snakeLength,
        int gridWidth,
        int gridHeight)
    {
        if (gridWidth < 2 || gridHeight < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(gridWidth), "Grid dimensions must be at least 2.");
        }

        if (snakeLength < 1)
        {
            return null;
        }

        var offset = direction.Offset();
        var atLeft = head.X == 0 && offset.Y != 0;
        var atRight = head.X == gridWidth - 1 && offset.Y != 0;
        var atTop = head.Y == 0 && offset.X != 0;
        var atBottom = head.Y == gridHeight - 1 && offset.X != 0;
        if (!(atLeft || atRight || atTop || atBottom))
        {
            return null;
        }

        var bonus = Math.Clamp(snakeLength / 10, 1, MaximumEdgeRideBonus);
        var message = bonus switch
        {
            >= 8 => "EDGE MASTERY!",
            >= 5 => "EDGE LORD!",
            _ => "EDGE RIDE",
        };

        return new NearMissEvent(
            NearMissKind.EdgeRide,
            head,
            ScoreBonus: bonus,
            Message: message,
            IsWarning: false);
    }

    /// <summary>
    /// Rewards food collection when fewer than <see cref="ClutchRemainingTicks"/>
    /// hunger ticks remain.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Performance",
        "CA1822:Mark members as static",
        Justification = "The established instance member is retained for public API compatibility.")]
    public NearMissEvent? CheckClutchEat(int hungerTicksRemaining)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(hungerTicksRemaining);

        if (hungerTicksRemaining >= ClutchRemainingTicks)
        {
            return null;
        }

        return new NearMissEvent(
            NearMissKind.ClutchEat,
            Position: null,
            ScoreBonus: 1,
            Message: "CLUTCH!",
            IsWarning: false);
    }

    /// <summary>
    /// Rewards food collection while Boost is active.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Performance",
        "CA1822:Mark members as static",
        Justification = "The established instance member is retained for public API compatibility.")]
    public NearMissEvent? CheckStylePoints(bool hasBoost)
    {
        if (!hasBoost)
        {
            return null;
        }

        return new NearMissEvent(
            NearMissKind.StylePoints,
            Position: null,
            ScoreBonus: 1,
            Message: "ZOOMING!",
            IsWarning: false);
    }

    public void TrackEvent(NearMissEvent nearMissEvent)
    {
        if (nearMissEvent.IsWarning)
        {
            return;
        }

        _recentEvents.Add((nearMissEvent, EventTimeoutTicks));
    }

    /// <summary>
    /// Bounded multiplier from recent rewarded near-miss events.
    /// Two events yield 1.5x; three or more yield 2.0x.
    /// </summary>
    public double GetComboMultiplier()
    {
        var count = _recentEvents.Count;
        if (count >= 3)
        {
            return 2.0;
        }

        if (count >= 2)
        {
            return 1.5;
        }

        return 1.0;
    }

    public void Reset()
    {
        _recentEvents.Clear();
        _rewardCooldownTicksRemaining = 0;
    }
}
