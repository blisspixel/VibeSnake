using VibeSnake.Rules;

namespace VibeSnake.Game;

internal sealed record PowerDecisionCounts(
    PowerKind Kind,
    int Offered,
    int DetoursObserved,
    int Collected,
    int Activated,
    int Expired,
    int Consumed,
    int Saved,
    int DeathAdjacent);

/// <summary>
/// Bounded, aggregate-only run observation for power decisions. It stores no
/// raw input or wall-clock timing and is safe to copy into an opted-in local
/// playtest summary at terminal state.
/// </summary>
internal sealed class PowerDecisionRunTrace
{
    public const int DeathAdjacencyWindowTicks = 20;

    private readonly Dictionary<PowerKind, MutableCounts> _counts =
        Enum.GetValues<PowerKind>().ToDictionary(kind => kind, _ => new MutableCounts());
    private OfferObservation? _offer;
    private PowerKind? _lastPowerKind;
    private int _lastPowerTick = -1;

    public void Reset()
    {
        foreach (var counts in _counts.Values)
        {
            counts.Reset();
        }

        _offer = null;
        _lastPowerKind = null;
        _lastPowerTick = -1;
    }

    public void Observe(
        RunSnapshot before,
        RunSnapshot after,
        IReadOnlyList<RunEventDetail> events)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        ArgumentNullException.ThrowIfNull(events);
        if (after.Tick != before.Tick + 1)
        {
            throw new ArgumentException("Power trace snapshots must describe one rules step.");
        }

        ObserveDetour(before, after);
        foreach (var detail in events)
        {
            switch (detail.Kind)
            {
                case RunEventKind.PowerSpawned:
                    Increment(detail, after.Tick, counts => counts.Offered++);
                    if (detail.Power is { } offeredKind && detail.Position is { } offeredPosition)
                    {
                        _offer = new OfferObservation(offeredKind, offeredPosition);
                    }

                    break;
                case RunEventKind.PowerCollected:
                    Increment(detail, after.Tick, counts => counts.Collected++);
                    _offer = null;
                    break;
                case RunEventKind.PowerActivated:
                    Increment(detail, after.Tick, counts => counts.Activated++);
                    break;
                case RunEventKind.PowerExpired:
                    Increment(detail, after.Tick, counts => counts.Expired++);
                    if (_offer is { } offer && detail.Power == offer.Kind)
                    {
                        _offer = null;
                    }

                    break;
                case RunEventKind.PowerConsumed:
                    Increment(detail, after.Tick, counts => counts.Consumed++);
                    break;
                case RunEventKind.CollisionPrevented:
                    Increment(detail, after.Tick, counts => counts.Saved++);
                    break;
                case RunEventKind.Died:
                    if (_lastPowerKind is { } adjacentKind
                        && after.Tick - _lastPowerTick <= DeathAdjacencyWindowTicks)
                    {
                        _counts[adjacentKind].DeathAdjacent++;
                    }

                    break;
            }
        }
    }

    public IReadOnlyList<PowerDecisionCounts> Snapshot() =>
        PowerDecisionCatalog.All.Select(definition =>
        {
            var counts = _counts[definition.Kind];
            return new PowerDecisionCounts(
                definition.Kind,
                counts.Offered,
                counts.DetoursObserved,
                counts.Collected,
                counts.Activated,
                counts.Expired,
                counts.Consumed,
                counts.Saved,
                counts.DeathAdjacent);
        }).ToArray();

    private void ObserveDetour(RunSnapshot before, RunSnapshot after)
    {
        if (_offer is not { DetourRecorded: false } offer
            || before.PowerPickup is not { } beforePickup
            || beforePickup.Kind != offer.Kind
            || beforePickup.Position != offer.Position)
        {
            return;
        }

        var beforeDistance = ManhattanDistance(before.Head, offer.Position);
        var afterDistance = ManhattanDistance(after.Head, offer.Position);
        if (afterDistance < beforeDistance && after.Direction != before.Direction)
        {
            _counts[offer.Kind].DetoursObserved++;
            _offer = offer with { DetourRecorded = true };
        }
    }

    private void Increment(
        RunEventDetail detail,
        int tick,
        Action<MutableCounts> increment)
    {
        if (detail.Power is not { } kind || !_counts.TryGetValue(kind, out var counts))
        {
            throw new ArgumentException(
                $"{detail.Kind} requires a defined power kind.",
                nameof(detail));
        }

        increment(counts);
        _lastPowerKind = kind;
        _lastPowerTick = tick;
    }

    private static int ManhattanDistance(GridPoint left, GridPoint right) =>
        Math.Abs(left.X - right.X) + Math.Abs(left.Y - right.Y);

    private sealed class MutableCounts
    {
        public int Offered { get; set; }

        public int DetoursObserved { get; set; }

        public int Collected { get; set; }

        public int Activated { get; set; }

        public int Expired { get; set; }

        public int Consumed { get; set; }

        public int Saved { get; set; }

        public int DeathAdjacent { get; set; }

        public void Reset()
        {
            Offered = 0;
            DetoursObserved = 0;
            Collected = 0;
            Activated = 0;
            Expired = 0;
            Consumed = 0;
            Saved = 0;
            DeathAdjacent = 0;
        }
    }

    private sealed record OfferObservation(
        PowerKind Kind,
        GridPoint Position,
        bool DetourRecorded = false);
}
