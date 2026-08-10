namespace VibeSnake.Rules;

public enum AiTargetKind : byte
{
    None = 0,
    Food = 1,
    Power = 2,
}

public enum AiRiskBand : byte
{
    Open = 0,
    Guarded = 1,
    Exposed = 2,
    DeadEnd = 3,
}

public enum AiDecisionReason : byte
{
    AdvanceFood = 0,
    AdvancePower = 1,
    PreserveOptions = 2,
    ContinueCourse = 3,
    EscapeHazard = 4,
    BoundedChaos = 5,
    RecoverStalledTarget = 6,
}

/// <summary>Measured facts for one AI decision, suitable for league reporting.</summary>
public sealed record AiDecision(
    Direction Direction,
    AiTargetKind TargetKind,
    GridPoint? Target,
    int? TargetDistanceBefore,
    int? TargetDistanceAfter,
    int HazardNeighborCount,
    int OnwardChoiceCount,
    int LegalChoiceCount,
    int SafeChoiceCount,
    bool UsedChaos,
    bool RecoveredStalledTarget = false)
{
    public bool ReducedTargetDistance =>
        TargetDistanceBefore is { } before
        && TargetDistanceAfter is { } after
        && after < before;

    public bool EnteredDeadEnd => OnwardChoiceCount <= 1;

    public AiRiskBand RiskBand => OnwardChoiceCount <= 1
        ? AiRiskBand.DeadEnd
        : HazardNeighborCount switch
        {
            0 => AiRiskBand.Open,
            1 => AiRiskBand.Guarded,
            _ => AiRiskBand.Exposed,
        };

    public AiDecisionReason Reason => RecoveredStalledTarget
        ? AiDecisionReason.RecoverStalledTarget
        : UsedChaos
        ? AiDecisionReason.BoundedChaos
        : ReducedTargetDistance
            ? TargetKind == AiTargetKind.Power
                ? AiDecisionReason.AdvancePower
                : AiDecisionReason.AdvanceFood
            : HazardNeighborCount > 0
                ? AiDecisionReason.EscapeHazard
                : OnwardChoiceCount >= 2
                    ? AiDecisionReason.PreserveOptions
                    : AiDecisionReason.ContinueCourse;
}

public sealed record AiSpectatorOverlaySnapshot(
    string PersonalityId,
    string PersonalityName,
    string PolicyId,
    string ContentStatus,
    bool OfficialLeagueQualified,
    string Target,
    AiRiskBand Risk,
    AiDecisionReason CurrentDecision,
    IReadOnlyList<AiDecisionReason> RecentDecisions);

public static class AiSpectatorOverlay
{
    public const int MaximumRecentDecisions = 5;

    public static AiSpectatorOverlaySnapshot Create(
        AiPersonalityProfile profile,
        AiDecision current,
        IEnumerable<AiDecision> recent)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(recent);
        profile.Personality.Validate();
        var target = current.Target is { } point
            ? $"{current.TargetKind.ToString().ToUpperInvariant()} {point.X},{point.Y}"
            : "NONE";
        var history = recent
            .TakeLast(MaximumRecentDecisions)
            .Select(decision => decision.Reason)
            .ToArray();
        return new AiSpectatorOverlaySnapshot(
            profile.Personality.Id,
            profile.Personality.Name,
            $"{AiPersonalityController.AlgorithmId}/{profile.Personality.Id}",
            profile.StatusLabel,
            profile.OfficialLeagueQualified,
            target,
            current.RiskBand,
            current.Reason,
            history);
    }
}

/// <summary>
/// Deterministic personality-weighted controller shared by simulations and the
/// future spectator flow. It reads rules state and never writes score storage.
/// </summary>
public sealed class AiPersonalityController
{
    public const string AlgorithmId = "native-personality-controller-v2";

    private const ulong RandomSequence = 80_002UL;
    private static readonly Direction[] Directions = Enum.GetValues<Direction>();
    private readonly AiPersonality _personality;
    private readonly Pcg32 _random;

    public AiPersonalityController(AiPersonality personality, ulong seed)
    {
        ArgumentNullException.ThrowIfNull(personality);
        personality.Validate();
        _personality = personality;
        _random = new Pcg32(seed, RandomSequence);
    }

    public AiPersonality Personality => _personality;

    public AiDecision SelectDecision(SnakeRun run)
    {
        ArgumentNullException.ThrowIfNull(run);
        if (run.Status != RunStatus.Running)
        {
            throw new InvalidOperationException("AI decisions require a running rules state.");
        }

        var legal = Directions
            .Where(direction => direction != run.Direction.Opposite())
            .ToArray();
        var safe = legal.Where(direction => IsSafe(run, direction)).ToArray();
        var candidates = safe.Length > 0 ? safe : legal;

        // Draw a fixed random budget every decision. Counterfactual personality
        // runs therefore compare the same random samples even when traits differ.
        var targetRoll = _random.NextInt(100);
        var chaosRoll = _random.NextInt(100);
        var chaosChoice = _random.NextInt(Directions.Length);
        var tieBreakers = Directions.ToDictionary(
            direction => direction,
            _ => _random.NextInt(17));

        var (targetKind, target) = SelectTarget(run, targetRoll);
        var selected = chaosRoll < _personality.Chaos
            ? candidates[chaosChoice % candidates.Length]
            : candidates
                .Select(direction => new
                {
                    Direction = direction,
                    Score = ScoreDirection(run, direction, targetKind, target)
                        + tieBreakers[direction],
                })
                .OrderByDescending(candidate => candidate.Score)
                .ThenBy(candidate => candidate.Direction)
                .First()
                .Direction;
        var next = Next(run, selected);
        int? before = target is { } targetPoint
            ? WrappedDistance(run.Head, targetPoint, run.Configuration)
            : null;
        int? after = target is { } selectedTarget
            ? WrappedDistance(next, selectedTarget, run.Configuration)
            : null;

        return new AiDecision(
            selected,
            targetKind,
            target,
            before,
            after,
            HazardNeighborCount(run, next),
            OnwardChoiceCount(run, next, selected),
            legal.Length,
            safe.Length,
            chaosRoll < _personality.Chaos);
    }

    /// <summary>
    /// Deterministic visible-state recovery used only after the spectator
    /// session observes a bounded target stall. It favors open exits, then low
    /// hazard exposure, without reading hidden state or consuming random data.
    /// </summary>
    public AiDecision SelectStallRecoveryDecision(SnakeRun run)
    {
        ArgumentNullException.ThrowIfNull(run);
        if (run.Status != RunStatus.Running)
        {
            throw new InvalidOperationException("AI decisions require a running rules state.");
        }

        var legal = Directions
            .Where(direction => direction != run.Direction.Opposite())
            .ToArray();
        var safe = legal.Where(direction => IsSafe(run, direction)).ToArray();
        var candidates = safe.Length > 0 ? safe : legal;
        var selected = candidates
            .Select(direction =>
            {
                var next = Next(run, direction);
                return new
                {
                    Direction = direction,
                    Onward = OnwardChoiceCount(run, next, direction),
                    Hazards = HazardNeighborCount(run, next),
                };
            })
            .OrderByDescending(candidate => candidate.Onward)
            .ThenBy(candidate => candidate.Hazards)
            .ThenBy(candidate => candidate.Direction == run.Direction ? 0 : 1)
            .ThenBy(candidate => candidate.Direction)
            .First();
        var (targetKind, target) = VisibleTarget(run);
        var nextPoint = Next(run, selected.Direction);
        int? before = target is { } targetPoint
            ? WrappedDistance(run.Head, targetPoint, run.Configuration)
            : null;
        int? after = target is { } selectedTarget
            ? WrappedDistance(nextPoint, selectedTarget, run.Configuration)
            : null;
        return new AiDecision(
            selected.Direction,
            targetKind,
            target,
            before,
            after,
            selected.Hazards,
            selected.Onward,
            legal.Length,
            safe.Length,
            UsedChaos: false,
            RecoveredStalledTarget: true);
    }

    private static (AiTargetKind Kind, GridPoint? Target) VisibleTarget(SnakeRun run) =>
        run.PowerPickup is { } power
            ? (AiTargetKind.Power, power.Position)
            : run.Food is { } food
                ? (AiTargetKind.Food, food)
                : (AiTargetKind.None, null);

    private (AiTargetKind Kind, GridPoint? Target) SelectTarget(
        SnakeRun run,
        int targetRoll)
    {
        if (run.PowerPickup is { } power)
        {
            var powerWeight = Math.Clamp(
                ((_personality.PowerUpPriority * 2)
                    + _personality.Aggression
                    + (100 - _personality.Greed)) / 4,
                0,
                100);
            if (targetRoll < powerWeight)
            {
                return (AiTargetKind.Power, power.Position);
            }
        }

        return run.Food is { } food
            ? (AiTargetKind.Food, food)
            : (AiTargetKind.None, null);
    }

    private int ScoreDirection(
        SnakeRun run,
        Direction direction,
        AiTargetKind targetKind,
        GridPoint? target)
    {
        var next = Next(run, direction);
        var hazards = HazardNeighborCount(run, next);
        var onward = OnwardChoiceCount(run, next, direction);
        var continuing = direction == run.Direction ? 1 : 0;
        var targetProgress = Progress(run, direction, target);
        var foodProgress = Progress(run, direction, run.Food);
        var powerProgress = Progress(run, direction, run.PowerPickup?.Position);

        var score = targetProgress * (200 + (_personality.Aggression * 8));
        score += foodProgress * _personality.Greed * 10;
        score += powerProgress * _personality.PowerUpPriority * 7;
        score += onward * _personality.Patience * 10;
        score += onward * (100 - _personality.RiskTolerance) * 5;
        score += continuing * _personality.Patience * 8;
        score -= hazards * (100 - _personality.RiskTolerance) * 40;
        score += hazards * _personality.RiskTolerance * 20;

        if (targetKind == AiTargetKind.Power)
        {
            score += powerProgress * _personality.Aggression * 3;
        }

        return score;
    }

    private static bool IsSafe(SnakeRun run, Direction direction)
    {
        if (run.HasPhaseShift)
        {
            return true;
        }

        var next = Next(run, direction);
        var bodyCollision = run.Body.Contains(next) && next != run.Body[0];
        return !bodyCollision && !run.DetachedObstacles.Contains(next);
    }

    private static int OnwardChoiceCount(
        SnakeRun run,
        GridPoint point,
        Direction incoming)
    {
        var count = 0;
        foreach (var direction in Directions)
        {
            if (direction == incoming.Opposite())
            {
                continue;
            }

            var neighbor = point
                .Add(direction.Offset())
                .Wrap(run.Configuration.Width, run.Configuration.Height);
            var bodyCollision = run.Body.Contains(neighbor)
                && neighbor != run.Body[0]
                && neighbor != run.Head;
            if (run.HasPhaseShift
                || (!bodyCollision && !run.DetachedObstacles.Contains(neighbor)))
            {
                count++;
            }
        }

        return count;
    }

    private static int HazardNeighborCount(SnakeRun run, GridPoint point)
    {
        var count = 0;
        foreach (var direction in Directions)
        {
            var neighbor = point
                .Add(direction.Offset())
                .Wrap(run.Configuration.Width, run.Configuration.Height);
            if ((run.Body.Contains(neighbor) && neighbor != run.Head)
                || run.DetachedObstacles.Contains(neighbor))
            {
                count++;
            }
        }

        return count;
    }

    private static int Progress(
        SnakeRun run,
        Direction direction,
        GridPoint? target)
    {
        if (target is not { } targetPoint)
        {
            return 0;
        }

        return WrappedDistance(run.Head, targetPoint, run.Configuration)
            - WrappedDistance(Next(run, direction), targetPoint, run.Configuration);
    }

    private static int WrappedDistance(
        GridPoint left,
        GridPoint right,
        RunConfig config) =>
        WrappedAxisDistance(left.X, right.X, config.Width)
        + WrappedAxisDistance(left.Y, right.Y, config.Height);

    private static int WrappedAxisDistance(int left, int right, int size)
    {
        var direct = Math.Abs(left - right);
        return Math.Min(direct, size - direct);
    }

    private static GridPoint Next(SnakeRun run, Direction direction) =>
        run.Head
            .Add(direction.Offset())
            .Wrap(run.Configuration.Width, run.Configuration.Height);
}
