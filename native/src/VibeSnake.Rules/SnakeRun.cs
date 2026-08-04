using System.Buffers;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace VibeSnake.Rules;

public sealed partial class SnakeRun
{
    public const string RulesetId = RulesetIdentity.CurrentId;
    public const int RulesVersion = RulesetIdentity.CurrentVersion;
    public const int CanonicalStateSchemaVersion = 2;
    public const string StateHashAlgorithmId = "fnv1a64-canonical-json-v3";
    public const int MaximumScore = 2_000_000_000;
    public const int MaximumRestorableTick = int.MaxValue - RunReplay.MaximumSteps;

    private const ulong FnvOffsetBasis = 14695981039346656037UL;
    private const ulong FnvPrime = 1099511628211UL;

    private readonly RunConfig _config;
    private readonly List<GridPoint> _body;
    private readonly ReadOnlyCollection<GridPoint> _bodyView;
    private readonly HashSet<GridPoint> _occupied;
    private readonly Queue<Direction> _pendingDirections;
    private readonly Pcg32 _random;

    private SnakeRun(
        RunConfig config,
        IEnumerable<GridPoint> body,
        Direction direction,
        GridPoint? food,
        int hungerTicksRemaining,
        int score,
        int comboCount,
        int ticksSinceLastFood,
        int tick,
        RunStatus status,
        DeathCause deathCause,
        Pcg32 random,
        PowerPickup? powerPickup = null,
        int powerSpawnTicksElapsed = 0,
        int shieldTicksRemaining = 0,
        int phaseShiftTicksRemaining = 0,
        bool lastStandHeld = false,
        int lastStandRecoveryTicksRemaining = 0,
        int slowMoTicksRemaining = 0,
        int boostTicksRemaining = 0,
        IEnumerable<Direction>? pendingDirections = null)
    {
        config.Validate();
        _config = config;
        _body = body.ToList();
        ValidateBody(_body, config);
        _bodyView = _body.AsReadOnly();
        _occupied = _body.ToHashSet();
        _pendingDirections = new Queue<Direction>(config.MaximumDirectionQueue);
        _random = random;
        Direction = direction;
        Food = food;
        HungerTicksRemaining = hungerTicksRemaining;
        Score = score;
        ComboCount = comboCount;
        TicksSinceLastFood = ticksSinceLastFood;
        Tick = tick;
        Status = status;
        DeathCause = deathCause;
        PowerPickup = powerPickup;
        PowerSpawnTicksElapsed = powerSpawnTicksElapsed;
        ShieldTicksRemaining = shieldTicksRemaining;
        PhaseShiftTicksRemaining = phaseShiftTicksRemaining;
        LastStandHeld = lastStandHeld;
        LastStandRecoveryTicksRemaining = lastStandRecoveryTicksRemaining;
        SlowMoTicksRemaining = slowMoTicksRemaining;
        BoostTicksRemaining = boostTicksRemaining;

        if (!Enum.IsDefined(direction))
        {
            throw new ArgumentOutOfRangeException(nameof(direction));
        }

        RestorePendingDirections(pendingDirections ?? [], direction);

        if (food is { } foodPoint && (!IsInBounds(foodPoint) || _occupied.Contains(foodPoint)))
        {
            throw new ArgumentException("Food must be in bounds and outside the snake.", nameof(food));
        }

        ValidatePowerState();

        if (hungerTicksRemaining < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(hungerTicksRemaining));
        }

        if (hungerTicksRemaining > config.StarvationTicks)
        {
            throw new ArgumentOutOfRangeException(
                nameof(hungerTicksRemaining),
                "Hunger cannot exceed the configured maximum.");
        }

        if (score < 0 || score > MaximumScore)
        {
            throw new ArgumentOutOfRangeException(nameof(score));
        }

        if (comboCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(comboCount));
        }

        if (ticksSinceLastFood < 0 || ticksSinceLastFood > MaximumRestorableTick)
        {
            throw new ArgumentOutOfRangeException(nameof(ticksSinceLastFood));
        }

        if (tick < 0 || tick > MaximumRestorableTick)
        {
            throw new ArgumentOutOfRangeException(nameof(tick));
        }

        ValidateRunState();
    }

    public int Tick { get; private set; }

    public RunStatus Status { get; private set; }

    public DeathCause DeathCause { get; private set; }

    public Direction Direction { get; private set; }

    public IReadOnlyList<GridPoint> Body => _bodyView;

    public GridPoint Head => _body[^1];

    public GridPoint? Food { get; private set; }

    public int Score { get; private set; }

    public int ComboCount { get; private set; }

    public double ComboMultiplier => CalculateComboMultiplier(ComboCount);

    public int TicksSinceLastFood { get; private set; }

    public int HungerTicksRemaining { get; private set; }

    public int PendingDirectionCount => _pendingDirections.Count;

    public PowerPickup? PowerPickup { get; private set; }

    public int PowerSpawnTicksElapsed { get; private set; }

    public int ShieldTicksRemaining { get; private set; }

    public int PhaseShiftTicksRemaining { get; private set; }

    public bool LastStandHeld { get; private set; }

    public int LastStandRecoveryTicksRemaining { get; private set; }

    public int SlowMoTicksRemaining { get; private set; }

    public int BoostTicksRemaining { get; private set; }

    public bool HasShield => ShieldTicksRemaining > 0;

    public bool HasPhaseShift => PhaseShiftTicksRemaining > 0;

    public bool HasLastStandRecovery => LastStandRecoveryTicksRemaining > 0;

    public bool HasSlowMo => SlowMoTicksRemaining > 0;

    public bool HasBoost => BoostTicksRemaining > 0;

    public int MovementCadenceNumerator => HasSlowMo ? 2 : 1;

    public int MovementCadenceDenominator => HasBoost ? 2 : 1;

    internal long GetNextStepVerificationWorkUnits()
    {
        if (Status != RunStatus.Running)
        {
            return _body.Count;
        }

        var gridCells = (long)_config.Width * _config.Height;
        var workUnits = _body.Count + 1L;

        var pickupRemainsAfterLifecycle = PowerPickup is
        {
            VisibilityTicksRemaining: > 1,
        };
        var shieldRemainsAfterLifecycle = ShieldTicksRemaining > 1;
        var spawnClockReachesInterval =
            _config.PowerSpawnIntervalTicks > 0
            && PowerSpawnTicksElapsed + 1 >= _config.PowerSpawnIntervalTicks;
        if (
            spawnClockReachesInterval
            && !pickupRemainsAfterLifecycle
            && !shieldRemainsAfterLifecycle)
        {
            // SpawnPower makes one complete occupancy pass, then at most one
            // complete selection pass. Charge both before either scan begins.
            workUnits += 2L * gridCells;
        }

        var nextDirection = Direction;
        if (
            _pendingDirections.TryPeek(out var queuedDirection)
            && queuedDirection != Direction.Opposite())
        {
            nextDirection = queuedDirection;
        }

        var nextHead = Head
            .Add(nextDirection.Offset())
            .Wrap(_config.Width, _config.Height);
        if (Food == nextHead && _body.Count + 1L < gridCells)
        {
            // Eating performs at most one complete free-cell selection pass.
            workUnits += gridCells;
        }

        return workUnits;
    }

    public static SnakeRun Create(ulong seed, RunConfig? config = null)
    {
        config ??= new RunConfig();
        config.Validate();
        var random = new Pcg32(seed);
        var start = new GridPoint(config.Width / 2, config.Height / 2);
        var run = new SnakeRun(
            config,
            [start],
            Direction.Right,
            null,
            config.StarvationTicks,
            0,
            0,
            0,
            0,
            RunStatus.Running,
            DeathCause.None,
            random);
        run.Food = run.SpawnFood(out _);
        return run;
    }

    internal static SnakeRun CreateForTesting(
        RunConfig config,
        IEnumerable<GridPoint> body,
        Direction direction,
        GridPoint? food,
        int hungerTicksRemaining,
        int score = 0,
        int comboCount = 0,
        int ticksSinceLastFood = 0,
        int tick = 0,
        ulong randomState = 1UL,
        ulong randomIncrement = 109UL,
        PowerPickup? powerPickup = null,
        int powerSpawnTicksElapsed = 0,
        int shieldTicksRemaining = 0,
        int phaseShiftTicksRemaining = 0,
        bool lastStandHeld = false,
        int lastStandRecoveryTicksRemaining = 0,
        int slowMoTicksRemaining = 0,
        int boostTicksRemaining = 0)
    {
        return new SnakeRun(
            config,
            body,
            direction,
            food,
            hungerTicksRemaining,
            score,
            comboCount,
            ticksSinceLastFood,
            tick,
            RunStatus.Running,
            DeathCause.None,
            new Pcg32(randomState, randomIncrement, restoreState: true),
            powerPickup,
            powerSpawnTicksElapsed,
            shieldTicksRemaining,
            phaseShiftTicksRemaining,
            lastStandHeld,
            lastStandRecoveryTicksRemaining,
            slowMoTicksRemaining,
            boostTicksRemaining);
    }

    public bool QueueDirection(Direction direction)
    {
        if (Status != RunStatus.Running || !Enum.IsDefined(direction))
        {
            return false;
        }

        if (_pendingDirections.Count >= _config.MaximumDirectionQueue)
        {
            return false;
        }

        var effectiveDirection = _pendingDirections.Count > 0 ? _pendingDirections.Last() : Direction;
        if (direction == effectiveDirection || direction == effectiveDirection.Opposite())
        {
            return false;
        }

        _pendingDirections.Enqueue(direction);
        return true;
    }

    public SnakeRun Restart(ulong seed)
    {
        if (Status == RunStatus.Running)
        {
            throw new InvalidOperationException("A running game cannot be restarted.");
        }

        return Create(seed, _config);
    }

    public RunStepResult Step()
    {
        if (Status != RunStatus.Running)
        {
            return Result(RunEvent.None, []);
        }

        var orderedEvents = new List<RunEventDetail>(12);
        if (_pendingDirections.TryDequeue(out var queuedDirection) && queuedDirection != Direction.Opposite())
        {
            Direction = queuedDirection;
            orderedEvents.Add(
                new RunEventDetail(
                    RunEventKind.DirectionChanged,
                    NewDirection: Direction));
        }

        Tick = checked(Tick + 1);
        AdvanceComboClock();
        var events = RunEvent.None;
        AdvancePowerLifecycle(ref events, orderedEvents);
        var unwrappedHead = Head.Add(Direction.Offset());
        var nextHead = unwrappedHead.Wrap(_config.Width, _config.Height);
        var wrapped = nextHead != unwrappedHead;
        AdvancePowerSpawnClock(nextHead, ref events, orderedEvents);
        var grows = Food == nextHead;
        var movesOntoDepartingTail = !grows && nextHead == _body[0];
        if (!grows)
        {
            HungerTicksRemaining = Math.Max(0, HungerTicksRemaining - 1);
        }

        var bodyCollision = _occupied.Contains(nextHead) && !movesOntoDepartingTail;
        if (bodyCollision && !HasPhaseShift)
        {
            if (wrapped)
            {
                events |= RunEvent.Wrapped;
                orderedEvents.Add(new RunEventDetail(RunEventKind.Wrapped, Position: nextHead));
            }

            // Precedence: recovery immunity, then Shield, then held Last Stand, then death.
            if (HasLastStandRecovery)
            {
                events |= RunEvent.CollisionPrevented;
                orderedEvents.Add(
                    new RunEventDetail(
                        RunEventKind.CollisionPrevented,
                        Position: nextHead,
                        Cause: DeathCause.SelfCollision,
                        Power: PowerKind.LastStand));
                ResolveStarvation(Head, ref events, orderedEvents);
                return Result(events, orderedEvents);
            }

            if (HasShield)
            {
                ShieldTicksRemaining = 0;
                events |= RunEvent.PowerConsumed | RunEvent.CollisionPrevented;
                orderedEvents.Add(
                    new RunEventDetail(
                        RunEventKind.PowerConsumed,
                        Power: PowerKind.Shield));
                orderedEvents.Add(
                    new RunEventDetail(
                        RunEventKind.CollisionPrevented,
                        Position: nextHead,
                        Cause: DeathCause.SelfCollision,
                        Power: PowerKind.Shield));
                ResolveStarvation(Head, ref events, orderedEvents);
                return Result(events, orderedEvents);
            }

            if (LastStandHeld)
            {
                ApplyLastStandRevive(
                    nextHead,
                    DeathCause.SelfCollision,
                    ref events,
                    orderedEvents);
                return Result(events, orderedEvents);
            }

            Status = RunStatus.Dead;
            DeathCause = DeathCause.SelfCollision;
            events |= RunEvent.Died;
            orderedEvents.Add(
                new RunEventDetail(
                    RunEventKind.Died,
                    Position: nextHead,
                    Cause: DeathCause.SelfCollision));
            return Result(events, orderedEvents);
        }

        _body.Add(nextHead);
        _occupied.Add(nextHead);

        events |= RunEvent.Moved | (wrapped ? RunEvent.Wrapped : RunEvent.None);
        orderedEvents.Add(new RunEventDetail(RunEventKind.Moved, Position: nextHead));
        if (wrapped)
        {
            orderedEvents.Add(new RunEventDetail(RunEventKind.Wrapped, Position: nextHead));
        }

        CollectPowerAtHead(nextHead, ref events, orderedEvents);

        if (grows)
        {
            var points = CalculateFoodPoints(_body.Count);
            var awardedPoints = (int)Math.Min((long)points, MaximumScore - (long)Score);
            Score += awardedPoints;
            ComboCount++;
            TicksSinceLastFood = 0;
            HungerTicksRemaining = _config.StarvationTicks;
            events |= RunEvent.AteFood;
            orderedEvents.Add(new RunEventDetail(RunEventKind.AteFood, Position: nextHead));
            orderedEvents.Add(
                new RunEventDetail(
                    RunEventKind.ScoreChanged,
                    Value: awardedPoints));
            orderedEvents.Add(
                new RunEventDetail(
                    RunEventKind.HungerReset,
                    Value: _config.StarvationTicks));

            if (_occupied.Count == _config.Width * _config.Height)
            {
                Food = null;
                Status = RunStatus.Won;
                events |= RunEvent.Won;
                orderedEvents.Add(new RunEventDetail(RunEventKind.Won, Position: nextHead));
                return Result(events, orderedEvents);
            }

            Food = SpawnFood(out var discardedPickup);
            if (discardedPickup is not null)
            {
                events |= RunEvent.PowerDiscarded;
                orderedEvents.Add(
                    new RunEventDetail(
                        RunEventKind.PowerDiscarded,
                        Position: discardedPickup.Position,
                        Power: discardedPickup.Kind));
            }
        }
        else
        {
            var tail = _body[0];
            _body.RemoveAt(0);
            // Phase Shift may leave duplicate coordinates; keep occupancy until
            // the final occurrence of a cell leaves the body.
            if (tail != nextHead && !_body.Contains(tail))
            {
                _occupied.Remove(tail);
            }

            ResolveStarvation(nextHead, ref events, orderedEvents);
        }

        return Result(events, orderedEvents);
    }

    public RunSnapshot GetSnapshot()
    {
        return new RunSnapshot(
            Tick,
            Status,
            DeathCause,
            Direction,
            _body.ToArray(),
            _pendingDirections.ToArray(),
            Food,
            Score,
            ComboCount,
            ComboMultiplier,
            TicksSinceLastFood,
            HungerTicksRemaining,
            PowerPickup,
            PowerSpawnTicksElapsed,
            ShieldTicksRemaining,
            PhaseShiftTicksRemaining,
            LastStandHeld,
            LastStandRecoveryTicksRemaining,
            SlowMoTicksRemaining,
            BoostTicksRemaining,
            ComputeStateHash());
    }

    public string ComputeStateHash()
    {
        var canonicalState = SerializeCanonicalStateBytes();
        var hash = FnvOffsetBasis;
        foreach (var value in canonicalState)
        {
            AddByte(ref hash, value);
        }

        return hash.ToString("x16", CultureInfo.InvariantCulture);
    }

    public string SerializeCanonicalState() =>
        Encoding.UTF8.GetString(SerializeCanonicalStateBytes());

    private RunStepResult Result(
        RunEvent events,
        IReadOnlyList<RunEventDetail> orderedEvents) =>
        new(Tick, events, orderedEvents, Status, DeathCause, ComputeStateHash());

    private byte[] SerializeCanonicalStateBytes()
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", CanonicalStateSchemaVersion);
            writer.WriteNumber("rulesVersion", RulesVersion);
            writer.WriteString("hashAlgorithm", StateHashAlgorithmId);
            writer.WriteString("rngAlgorithm", Pcg32.AlgorithmId);

            writer.WriteStartObject("config");
            writer.WriteNumber("width", _config.Width);
            writer.WriteNumber("height", _config.Height);
            writer.WriteNumber("rulesTickMilliseconds", RunConfig.RulesTickMilliseconds);
            writer.WriteNumber("starvationTicks", _config.StarvationTicks);
            writer.WriteNumber("maximumDirectionQueue", _config.MaximumDirectionQueue);
            writer.WriteNumber("foodScore", _config.FoodScore);
            writer.WriteNumber("comboWindowTicks", _config.ComboWindowTicks);
            writer.WriteNumber("speedBonusTicks", _config.SpeedBonusTicks);
            writer.WriteNumber("powerSpawnIntervalTicks", _config.PowerSpawnIntervalTicks);
            writer.WriteNumber("powerVisibleTicks", _config.PowerVisibleTicks);
            writer.WriteNumber("shieldDurationTicks", _config.ShieldDurationTicks);
            writer.WriteNumber("phaseShiftDurationTicks", _config.PhaseShiftDurationTicks);
            writer.WriteNumber("lastStandRecoveryTicks", _config.LastStandRecoveryTicks);
            writer.WriteNumber("slowMoDurationTicks", _config.SlowMoDurationTicks);
            writer.WriteNumber("boostDurationTicks", _config.BoostDurationTicks);
            writer.WriteEndObject();

            writer.WriteNumber("tick", Tick);
            writer.WriteNumber("status", (byte)Status);
            writer.WriteNumber("deathCause", (byte)DeathCause);
            writer.WriteNumber("direction", (byte)Direction);
            writer.WriteNumber("score", Score);
            writer.WriteNumber("comboCount", ComboCount);
            writer.WriteNumber("ticksSinceLastFood", TicksSinceLastFood);
            writer.WriteNumber("hungerTicksRemaining", HungerTicksRemaining);
            writer.WriteNumber("powerSpawnTicksElapsed", PowerSpawnTicksElapsed);
            writer.WriteNumber("shieldTicksRemaining", ShieldTicksRemaining);
            writer.WriteNumber("phaseShiftTicksRemaining", PhaseShiftTicksRemaining);
            writer.WriteBoolean("lastStandHeld", LastStandHeld);
            writer.WriteNumber(
                "lastStandRecoveryTicksRemaining",
                LastStandRecoveryTicksRemaining);
            writer.WriteNumber("slowMoTicksRemaining", SlowMoTicksRemaining);
            writer.WriteNumber("boostTicksRemaining", BoostTicksRemaining);

            if (PowerPickup is { } powerPickup)
            {
                writer.WriteStartObject("powerPickup");
                writer.WriteNumber("kind", (byte)powerPickup.Kind);
                WritePoint(writer, "position", powerPickup.Position);
                writer.WriteNumber(
                    "visibilityTicksRemaining",
                    powerPickup.VisibilityTicksRemaining);
                writer.WriteEndObject();
            }
            else
            {
                writer.WriteNull("powerPickup");
            }

            writer.WriteStartObject("random");
            writer.WriteString("state", _random.State.ToString(CultureInfo.InvariantCulture));
            writer.WriteString("increment", _random.Increment.ToString(CultureInfo.InvariantCulture));
            writer.WriteEndObject();

            if (Food is { } food)
            {
                WritePoint(writer, "food", food);
            }
            else
            {
                writer.WriteNull("food");
            }

            writer.WriteStartArray("body");
            foreach (var segment in _body)
            {
                WritePoint(writer, segment);
            }

            writer.WriteEndArray();
            writer.WriteStartArray("pendingDirections");
            foreach (var pendingDirection in _pendingDirections)
            {
                writer.WriteNumberValue((byte)pendingDirection);
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return buffer.WrittenSpan.ToArray();
    }

    private static void WritePoint(Utf8JsonWriter writer, string propertyName, GridPoint point)
    {
        writer.WriteStartObject(propertyName);
        writer.WriteNumber("x", point.X);
        writer.WriteNumber("y", point.Y);
        writer.WriteEndObject();
    }

    private static void WritePoint(Utf8JsonWriter writer, GridPoint point)
    {
        writer.WriteStartObject();
        writer.WriteNumber("x", point.X);
        writer.WriteNumber("y", point.Y);
        writer.WriteEndObject();
    }

    private void AdvancePowerLifecycle(
        ref RunEvent events,
        ICollection<RunEventDetail> orderedEvents)
    {
        if (PowerPickup is { } pickup)
        {
            var visibilityTicksRemaining = pickup.VisibilityTicksRemaining - 1;
            if (visibilityTicksRemaining == 0)
            {
                PowerPickup = null;
                events |= RunEvent.PowerExpired;
                orderedEvents.Add(
                    new RunEventDetail(
                        RunEventKind.PowerExpired,
                        Position: pickup.Position,
                        Power: pickup.Kind));
            }
            else
            {
                PowerPickup = new PowerPickup(
                    pickup.Kind,
                    pickup.Position,
                    visibilityTicksRemaining);
            }
        }

        if (ShieldTicksRemaining > 0)
        {
            ShieldTicksRemaining--;
            if (ShieldTicksRemaining == 0)
            {
                events |= RunEvent.PowerExpired;
                orderedEvents.Add(
                    new RunEventDetail(
                        RunEventKind.PowerExpired,
                        Power: PowerKind.Shield));
            }
        }

        if (PhaseShiftTicksRemaining > 0)
        {
            PhaseShiftTicksRemaining--;
            if (PhaseShiftTicksRemaining == 0)
            {
                events |= RunEvent.PowerExpired;
                orderedEvents.Add(
                    new RunEventDetail(
                        RunEventKind.PowerExpired,
                        Power: PowerKind.PhaseShift));
            }
        }

        if (LastStandRecoveryTicksRemaining > 0)
        {
            LastStandRecoveryTicksRemaining--;
            if (LastStandRecoveryTicksRemaining == 0)
            {
                events |= RunEvent.PowerExpired;
                orderedEvents.Add(
                    new RunEventDetail(
                        RunEventKind.PowerExpired,
                        Power: PowerKind.LastStand));
            }
        }

        if (SlowMoTicksRemaining > 0)
        {
            SlowMoTicksRemaining--;
            if (SlowMoTicksRemaining == 0)
            {
                events |= RunEvent.PowerExpired;
                orderedEvents.Add(
                    new RunEventDetail(
                        RunEventKind.PowerExpired,
                        Power: PowerKind.SlowMo));
            }
        }

        if (BoostTicksRemaining <= 0)
        {
            return;
        }

        BoostTicksRemaining--;
        if (BoostTicksRemaining == 0)
        {
            events |= RunEvent.PowerExpired;
            orderedEvents.Add(
                new RunEventDetail(
                    RunEventKind.PowerExpired,
                    Power: PowerKind.Boost));
        }
    }

    private void ResolveStarvation(
        GridPoint position,
        ref RunEvent events,
        ICollection<RunEventDetail> orderedEvents)
    {
        if (HungerTicksRemaining > 0)
        {
            return;
        }

        if (LastStandHeld)
        {
            ApplyLastStandRevive(
                position,
                DeathCause.Starvation,
                ref events,
                orderedEvents);
            return;
        }

        Status = RunStatus.Dead;
        DeathCause = DeathCause.Starvation;
        events |= RunEvent.Died;
        orderedEvents.Add(
            new RunEventDetail(
                RunEventKind.Died,
                Position: position,
                Cause: DeathCause.Starvation));
    }

    private void ApplyLastStandRevive(
        GridPoint triggerPosition,
        DeathCause preventedCause,
        ref RunEvent events,
        ICollection<RunEventDetail> orderedEvents)
    {
        LastStandHeld = false;
        ShrinkBodyToHalfRoundedUp();
        HungerTicksRemaining = _config.StarvationTicks;
        LastStandRecoveryTicksRemaining = _config.LastStandRecoveryTicks;

        events |= RunEvent.PowerConsumed | RunEvent.CollisionPrevented;
        orderedEvents.Add(
            new RunEventDetail(
                RunEventKind.PowerConsumed,
                Power: PowerKind.LastStand));
        orderedEvents.Add(
            new RunEventDetail(
                RunEventKind.CollisionPrevented,
                Position: triggerPosition,
                Cause: preventedCause,
                Power: PowerKind.LastStand));
        orderedEvents.Add(
            new RunEventDetail(
                RunEventKind.HungerReset,
                Value: _config.StarvationTicks));
        orderedEvents.Add(
            new RunEventDetail(
                RunEventKind.PowerActivated,
                Value: LastStandRecoveryTicksRemaining,
                Power: PowerKind.LastStand));
    }

    private void ShrinkBodyToHalfRoundedUp()
    {
        var targetLength = Math.Max(1, (_body.Count + 1) / 2);
        while (_body.Count > targetLength)
        {
            var tail = _body[0];
            _body.RemoveAt(0);
            if (!_body.Contains(tail))
            {
                _occupied.Remove(tail);
            }
        }
    }

    private void AdvancePowerSpawnClock(
        GridPoint reservedDestination,
        ref RunEvent events,
        ICollection<RunEventDetail> orderedEvents)
    {
        if (_config.PowerSpawnIntervalTicks == 0)
        {
            PowerSpawnTicksElapsed = 0;
            return;
        }

        PowerSpawnTicksElapsed = Math.Min(
            _config.PowerSpawnIntervalTicks,
            checked(PowerSpawnTicksElapsed + 1));
        if (
            PowerSpawnTicksElapsed < _config.PowerSpawnIntervalTicks
            || PowerPickup is not null
            || HasShield)
        {
            return;
        }

        PowerSpawnTicksElapsed = 0;
        var pickup = SpawnPower(reservedDestination);
        if (pickup is null)
        {
            return;
        }

        PowerPickup = pickup;
        events |= RunEvent.PowerSpawned;
        orderedEvents.Add(
            new RunEventDetail(
                RunEventKind.PowerSpawned,
                Position: pickup.Position,
                Value: pickup.VisibilityTicksRemaining,
                Power: pickup.Kind));
    }

    private void CollectPowerAtHead(
        GridPoint head,
        ref RunEvent events,
        ICollection<RunEventDetail> orderedEvents)
    {
        if (PowerPickup is not { } pickup || pickup.Position != head)
        {
            return;
        }

        PowerPickup = null;
        events |= RunEvent.PowerCollected;
        orderedEvents.Add(
            new RunEventDetail(
                RunEventKind.PowerCollected,
                Position: head,
                Power: pickup.Kind));

        switch (pickup.Kind)
        {
            case PowerKind.Shield:
                ShieldTicksRemaining = _config.ShieldDurationTicks;
                events |= RunEvent.PowerActivated;
                orderedEvents.Add(
                    new RunEventDetail(
                        RunEventKind.PowerActivated,
                        Value: ShieldTicksRemaining,
                        Power: pickup.Kind));
                break;
            case PowerKind.PhaseShift:
                PhaseShiftTicksRemaining = _config.PhaseShiftDurationTicks;
                events |= RunEvent.PowerActivated;
                orderedEvents.Add(
                    new RunEventDetail(
                        RunEventKind.PowerActivated,
                        Value: PhaseShiftTicksRemaining,
                        Power: pickup.Kind));
                break;
            case PowerKind.LastStand:
                LastStandHeld = true;
                events |= RunEvent.PowerActivated;
                orderedEvents.Add(
                    new RunEventDetail(
                        RunEventKind.PowerActivated,
                        Value: 0,
                        Power: pickup.Kind));
                break;
            case PowerKind.SlowMo:
                SlowMoTicksRemaining = _config.SlowMoDurationTicks;
                events |= RunEvent.PowerActivated;
                orderedEvents.Add(
                    new RunEventDetail(
                        RunEventKind.PowerActivated,
                        Value: SlowMoTicksRemaining,
                        Power: pickup.Kind));
                break;
            case PowerKind.Boost:
                BoostTicksRemaining = _config.BoostDurationTicks;
                events |= RunEvent.PowerActivated;
                orderedEvents.Add(
                    new RunEventDetail(
                        RunEventKind.PowerActivated,
                        Value: BoostTicksRemaining,
                        Power: pickup.Kind));
                break;
            default:
                throw new InvalidOperationException($"Unsupported power kind {pickup.Kind}.");
        }
    }

    private void AdvanceComboClock()
    {
        TicksSinceLastFood = checked(TicksSinceLastFood + 1);
        if (TicksSinceLastFood > _config.ComboWindowTicks && ComboCount > 0)
        {
            ComboCount = 0;
        }
    }

    private int CalculateFoodPoints(int snakeLength)
    {
        var nextComboCount = ComboCount + 1;
        var points = (int)(_config.FoodScore * CalculateComboMultiplier(nextComboCount));

        if (TicksSinceLastFood < _config.SpeedBonusTicks)
        {
            points = checked(points + (int)(_config.FoodScore * 0.5));
        }

        if (snakeLength > 10)
        {
            points = checked(points + (int)((snakeLength - 10) * Math.Log(snakeLength) / 2.0));
        }

        return points;
    }

    private static double CalculateComboMultiplier(int comboCount)
    {
        ReadOnlySpan<(int Threshold, double Multiplier)> thresholds =
        [
            (0, 1.0),
            (3, 2.0),
            (5, 3.0),
            (10, 5.0),
            (20, 10.0),
        ];

        for (var index = 0; index < thresholds.Length - 1; index++)
        {
            var lower = thresholds[index];
            var upper = thresholds[index + 1];
            if (comboCount >= lower.Threshold && comboCount < upper.Threshold)
            {
                var progress = (double)(comboCount - lower.Threshold) / (upper.Threshold - lower.Threshold);
                return lower.Multiplier + ((upper.Multiplier - lower.Multiplier) * progress);
            }
        }

        return thresholds[^1].Multiplier;
    }

    private GridPoint SpawnFood(out PowerPickup? discardedPickup)
    {
        discardedPickup = null;
        var freeCellCount = (_config.Width * _config.Height)
            - _occupied.Count
            - (PowerPickup is null ? 0 : 1);
        if (freeCellCount <= 0 && PowerPickup is { } pickup)
        {
            discardedPickup = pickup;
            PowerPickup = null;
            freeCellCount = (_config.Width * _config.Height) - _occupied.Count;
        }

        if (freeCellCount <= 0)
        {
            throw new InvalidOperationException("Cannot spawn food on a full grid.");
        }

        var targetFreeCell = _random.NextInt(freeCellCount);
        var freeCellIndex = 0;
        for (var y = 0; y < _config.Height; y++)
        {
            for (var x = 0; x < _config.Width; x++)
            {
                var candidate = new GridPoint(x, y);
                if (
                    _occupied.Contains(candidate)
                    || PowerPickup?.Position == candidate)
                {
                    continue;
                }

                if (freeCellIndex == targetFreeCell)
                {
                    return candidate;
                }

                freeCellIndex++;
            }
        }

        throw new InvalidOperationException("The free-cell count did not match board occupancy.");
    }

    private PowerPickup? SpawnPower(GridPoint reservedDestination)
    {
        var freeCellCount = 0;
        for (var y = 0; y < _config.Height; y++)
        {
            for (var x = 0; x < _config.Width; x++)
            {
                var candidate = new GridPoint(x, y);
                if (!IsPowerSpawnCellBlocked(candidate, reservedDestination))
                {
                    freeCellCount++;
                }
            }
        }

        if (freeCellCount == 0)
        {
            return null;
        }

        var targetFreeCell = _random.NextInt(freeCellCount);
        var freeCellIndex = 0;
        for (var y = 0; y < _config.Height; y++)
        {
            for (var x = 0; x < _config.Width; x++)
            {
                var candidate = new GridPoint(x, y);
                if (IsPowerSpawnCellBlocked(candidate, reservedDestination))
                {
                    continue;
                }

                if (freeCellIndex == targetFreeCell)
                {
                    return new PowerPickup(
                        PowerKind.Shield,
                        candidate,
                        _config.PowerVisibleTicks);
                }

                freeCellIndex++;
            }
        }

        throw new InvalidOperationException("The free-cell count did not match power spawn occupancy.");
    }

    private bool IsPowerSpawnCellBlocked(
        GridPoint candidate,
        GridPoint reservedDestination) =>
        _occupied.Contains(candidate)
        || Food == candidate
        || reservedDestination == candidate;

    private bool IsInBounds(GridPoint point) =>
        point.X >= 0 && point.X < _config.Width && point.Y >= 0 && point.Y < _config.Height;

    private void ValidatePowerState()
    {
        if (PowerPickup is { } pickup)
        {
            if (!IsInBounds(pickup.Position) || _occupied.Contains(pickup.Position))
            {
                throw new ArgumentException(
                    "A power pickup must be in bounds and outside the snake.",
                    nameof(PowerPickup));
            }

            if (Food == pickup.Position)
            {
                throw new ArgumentException(
                    "Food and a power pickup cannot occupy the same cell.",
                    nameof(PowerPickup));
            }

            if (pickup.VisibilityTicksRemaining > _config.PowerVisibleTicks)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(PowerPickup),
                    "Pickup visibility cannot exceed the configured window.");
            }
        }

        if (
            PowerSpawnTicksElapsed < 0
            || PowerSpawnTicksElapsed > _config.PowerSpawnIntervalTicks
            || (_config.PowerSpawnIntervalTicks == 0 && PowerSpawnTicksElapsed != 0))
        {
            throw new ArgumentOutOfRangeException(nameof(PowerSpawnTicksElapsed));
        }

        if (ShieldTicksRemaining < 0 || ShieldTicksRemaining > _config.ShieldDurationTicks)
        {
            throw new ArgumentOutOfRangeException(nameof(ShieldTicksRemaining));
        }

        if (
            PhaseShiftTicksRemaining < 0
            || PhaseShiftTicksRemaining > _config.PhaseShiftDurationTicks)
        {
            throw new ArgumentOutOfRangeException(nameof(PhaseShiftTicksRemaining));
        }

        if (PowerPickup?.Kind == PowerKind.Shield && HasShield)
        {
            throw new ArgumentException(
                "A second Shield pickup cannot coexist with an active Shield.",
                nameof(PowerPickup));
        }

        if (PowerPickup?.Kind == PowerKind.PhaseShift && HasPhaseShift)
        {
            throw new ArgumentException(
                "A second Phase Shift pickup cannot coexist with an active Phase Shift.",
                nameof(PowerPickup));
        }

        if (PowerPickup?.Kind == PowerKind.LastStand && LastStandHeld)
        {
            throw new ArgumentException(
                "A second Last Stand pickup cannot coexist with a held Last Stand.",
                nameof(PowerPickup));
        }

        if (
            LastStandRecoveryTicksRemaining < 0
            || LastStandRecoveryTicksRemaining > _config.LastStandRecoveryTicks)
        {
            throw new ArgumentOutOfRangeException(nameof(LastStandRecoveryTicksRemaining));
        }

        if (SlowMoTicksRemaining < 0 || SlowMoTicksRemaining > _config.SlowMoDurationTicks)
        {
            throw new ArgumentOutOfRangeException(nameof(SlowMoTicksRemaining));
        }

        if (BoostTicksRemaining < 0 || BoostTicksRemaining > _config.BoostDurationTicks)
        {
            throw new ArgumentOutOfRangeException(nameof(BoostTicksRemaining));
        }

        if (PowerPickup?.Kind == PowerKind.SlowMo && HasSlowMo)
        {
            throw new ArgumentException(
                "A second Slow-Mo pickup cannot coexist with an active Slow-Mo.",
                nameof(PowerPickup));
        }

        if (PowerPickup?.Kind == PowerKind.Boost && HasBoost)
        {
            throw new ArgumentException(
                "A second Boost pickup cannot coexist with an active Boost.",
                nameof(PowerPickup));
        }
    }

    private static void ValidateBody(IReadOnlyCollection<GridPoint> body, RunConfig config)
    {
        if (body.Count == 0)
        {
            throw new ArgumentException("A snake must contain at least one segment.", nameof(body));
        }

        // Phase Shift permits temporary coordinate duplicates along the body.
        // Cap length so restore remains resource-bounded.
        if (body.Count > RunConfig.MaximumGridCells)
        {
            throw new ArgumentException(
                $"Snake length cannot exceed {RunConfig.MaximumGridCells} segments.",
                nameof(body));
        }

        if (body.Any(point => point.X < 0 || point.X >= config.Width || point.Y < 0 || point.Y >= config.Height))
        {
            throw new ArgumentException("Snake segments must be inside the grid.", nameof(body));
        }

        var segments = body as IReadOnlyList<GridPoint> ?? body.ToArray();
        for (var index = 1; index < segments.Count; index++)
        {
            var previous = segments[index - 1];
            var current = segments[index];
            var horizontalDistance = Math.Abs(previous.X - current.X);
            var verticalDistance = Math.Abs(previous.Y - current.Y);
            horizontalDistance = Math.Min(horizontalDistance, config.Width - horizontalDistance);
            verticalDistance = Math.Min(verticalDistance, config.Height - verticalDistance);
            if (horizontalDistance + verticalDistance != 1)
            {
                throw new ArgumentException(
                    "Consecutive snake segments must occupy adjacent wrapped cells.",
                    nameof(body));
            }
        }
    }

    private void RestorePendingDirections(
        IEnumerable<Direction> pendingDirections,
        Direction startingDirection)
    {
        var effectiveDirection = startingDirection;
        foreach (var pendingDirection in pendingDirections)
        {
            if (!Enum.IsDefined(pendingDirection))
            {
                throw new ArgumentOutOfRangeException(nameof(pendingDirections));
            }

            if (_pendingDirections.Count >= _config.MaximumDirectionQueue)
            {
                throw new ArgumentException(
                    "Pending directions exceed the configured queue capacity.",
                    nameof(pendingDirections));
            }

            if (
                pendingDirection == effectiveDirection
                || pendingDirection == effectiveDirection.Opposite())
            {
                throw new ArgumentException(
                    "Pending directions must form a legal turn sequence.",
                    nameof(pendingDirections));
            }

            _pendingDirections.Enqueue(pendingDirection);
            effectiveDirection = pendingDirection;
        }
    }

    private void ValidateRunState()
    {
        if (!Enum.IsDefined(Status))
        {
            throw new ArgumentOutOfRangeException(nameof(Status));
        }

        if (!Enum.IsDefined(DeathCause))
        {
            throw new ArgumentOutOfRangeException(nameof(DeathCause));
        }

        var gridIsFull = _occupied.Count == _config.Width * _config.Height;
        switch (Status)
        {
            case RunStatus.Running when DeathCause != DeathCause.None:
                throw new ArgumentException("A running game cannot have a death cause.");
            case RunStatus.Running when HungerTicksRemaining <= 0:
                throw new ArgumentException("A running game must have hunger remaining.");
            case RunStatus.Dead when DeathCause == DeathCause.None:
                throw new ArgumentException("A dead game must have a death cause.");
            case RunStatus.Dead when DeathCause == DeathCause.Starvation && HungerTicksRemaining != 0:
                throw new ArgumentException("A starvation death must end at zero hunger.");
            case RunStatus.Dead when DeathCause == DeathCause.SelfCollision && HasShield:
                throw new ArgumentException("A self-collision death cannot retain an active Shield.");
            case RunStatus.Dead when DeathCause == DeathCause.SelfCollision && HasPhaseShift:
                throw new ArgumentException(
                    "A self-collision death cannot retain an active Phase Shift.");
            case RunStatus.Dead when DeathCause == DeathCause.SelfCollision && LastStandHeld:
                throw new ArgumentException(
                    "A self-collision death cannot retain a held Last Stand.");
            case RunStatus.Dead when DeathCause == DeathCause.SelfCollision && HasLastStandRecovery:
                throw new ArgumentException(
                    "A self-collision death cannot retain Last Stand recovery.");
            case RunStatus.Dead when DeathCause == DeathCause.Starvation && LastStandHeld:
                throw new ArgumentException(
                    "A starvation death cannot retain a held Last Stand.");
            case RunStatus.Won when DeathCause != DeathCause.None:
                throw new ArgumentException("A won game cannot have a death cause.");
            case RunStatus.Won when !gridIsFull || Food is not null:
                throw new ArgumentException("A won game must contain a full grid and no food.");
            case not RunStatus.Won when gridIsFull:
                throw new ArgumentException("A full grid must be represented as a won game.");
        }
    }

    private static void AddByte(ref ulong hash, byte value)
    {
        hash ^= value;
        hash = unchecked(hash * FnvPrime);
    }

}
