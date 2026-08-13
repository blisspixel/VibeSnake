namespace VibeSnake.Rules.Tests;

internal enum BalancePolicyKind : byte
{
    SafeSurvivor = 0,
    GreedyFood = 1,
    RiskSeeking = 2,
    PowerHunting = 3,
    BoundaryWalker = 4,
    Idle = 5,
    InputChaos = 6,
    Personality = 7,
    ReplayGhost = 8,
}

internal sealed record BalancePolicyDefinition(
    BalancePolicyKind Kind,
    string Id,
    string Classification,
    bool IsReferenceAi,
    string Purpose);

internal static class BalancePolicyCatalog
{
    public static IReadOnlyList<BalancePolicyDefinition> All { get; } =
    [
        new(BalancePolicyKind.SafeSurvivor, "safe-survivor-v1", "reference-ai", true, "Prefer open cells and long survival."),
        new(BalancePolicyKind.GreedyFood, "greedy-food-v1", "reference-ai", true, "Minimize wrapped distance to food."),
        new(BalancePolicyKind.RiskSeeking, "risk-seeking-v1", "reference-ai", true, "Prefer safe cells close to hazards."),
        new(BalancePolicyKind.PowerHunting, "power-hunting-v1", "reference-ai", true, "Route toward visible powers before food."),
        new(BalancePolicyKind.BoundaryWalker, "boundary-walker-v1", "reference-ai", true, "Reach edges, corners, and wraps."),
        new(BalancePolicyKind.Idle, "idle-v1", "lifecycle-stress", false, "Submit no turns and expose passive lifecycle behavior."),
        new(BalancePolicyKind.InputChaos, "input-chaos-v1", "input-stress", false, "Submit duplicate, reverse, and queue-pressure attempts."),
        new(BalancePolicyKind.Personality, "personality-seeded-v1", "reference-ai", true, "Blend food, safety, risk, power, and seeded variation."),
        new(BalancePolicyKind.ReplayGhost, "replay-ghost-v1", "replay-oracle", false, "Create a stable command trace for record and playback."),
    ];

    public static BalancePolicyDefinition Get(BalancePolicyKind kind) =>
        All.Single(definition => definition.Kind == kind);
}

internal sealed class BalancePolicyController
{
    private static readonly Direction[] Directions = Enum.GetValues<Direction>();
    private readonly BalancePolicyKind _kind;
    private readonly Pcg32 _random;
    private readonly int _personalityRisk;
    private readonly int _personalityPower;
    private readonly int _personalityChaos;

    public BalancePolicyController(BalancePolicyKind kind, ulong seed)
    {
        _kind = kind;
        _random = new Pcg32(seed, sequence: 70_003UL + (ulong)kind);
        _personalityRisk = _random.NextInt(101);
        _personalityPower = _random.NextInt(101);
        _personalityChaos = _random.NextInt(101);
    }

    public IReadOnlyList<Direction> SelectCommands(SnakeRun run)
    {
        ArgumentNullException.ThrowIfNull(run);
        if (run.Status != RunStatus.Running || _kind == BalancePolicyKind.Idle)
        {
            return Array.Empty<Direction>();
        }

        if (_kind == BalancePolicyKind.InputChaos)
        {
            var count = 1 + _random.NextInt(run.Configuration.MaximumDirectionQueue + 2);
            var commands = new Direction[count];
            for (var index = 0; index < commands.Length; index++)
            {
                commands[index] = (Direction)_random.NextInt(Directions.Length);
            }

            return commands;
        }

        var legal = Directions
            .Where(direction => direction != run.Direction.Opposite())
            .ToArray();
        var safe = legal.Where(direction => IsSafe(run, direction)).ToArray();
        var candidates = safe.Length > 0 ? safe : legal;
        var selected = _kind switch
        {
            BalancePolicyKind.SafeSurvivor => SelectMaximum(
                candidates,
                direction => (FreeNeighborCount(run, Next(run, direction)) * 100)
                    - DistanceToFood(run, direction)),
            BalancePolicyKind.GreedyFood or BalancePolicyKind.ReplayGhost => SelectMinimum(
                candidates,
                direction => DistanceToFood(run, direction)),
            BalancePolicyKind.RiskSeeking => SelectMaximum(
                candidates,
                direction => (HazardNeighborCount(run, Next(run, direction)) * 100)
                    - DistanceToFood(run, direction)),
            BalancePolicyKind.PowerHunting => SelectMinimum(
                candidates,
                direction => DistanceToTarget(
                    run,
                    direction,
                    run.PowerPickup?.Position ?? run.Food)),
            BalancePolicyKind.BoundaryWalker => SelectBoundaryDirection(run, candidates),
            BalancePolicyKind.Personality => SelectPersonalityDirection(run, candidates),
            _ => throw new InvalidOperationException("Unknown balance policy kind."),
        };

        return [selected];
    }

    private Direction SelectPersonalityDirection(SnakeRun run, Direction[] candidates)
    {
        if (_personalityChaos > 65 && _random.NextInt(8) == 0)
        {
            return candidates[_random.NextInt(candidates.Length)];
        }

        return SelectMaximum(
            candidates,
            direction =>
            {
                var next = Next(run, direction);
                var safety = FreeNeighborCount(run, next) * (100 - _personalityRisk);
                var risk = HazardNeighborCount(run, next) * _personalityRisk;
                var food = -DistanceToTarget(run, direction, run.Food) * 4;
                var power = run.PowerPickup is null
                    ? 0
                    : -DistanceToTarget(run, direction, run.PowerPickup.Position)
                        * _personalityPower;
                return safety + risk + food + power + _random.NextInt(3);
            });
    }

    private static Direction SelectBoundaryDirection(SnakeRun run, Direction[] candidates)
    {
        var head = run.Head;
        var config = run.Configuration;
        Direction preferred;
        if (head.Y > 0 && head.X > 0 && head.X < config.Width - 1)
        {
            preferred = Direction.Up;
        }
        else if (head.Y == 0 && head.X < config.Width - 1)
        {
            preferred = Direction.Right;
        }
        else if (head.X == config.Width - 1 && head.Y < config.Height - 1)
        {
            preferred = Direction.Down;
        }
        else if (head.Y == config.Height - 1 && head.X > 0)
        {
            preferred = Direction.Left;
        }
        else
        {
            preferred = Direction.Up;
        }

        return candidates.Contains(preferred)
            ? preferred
            : SelectMaximum(
                candidates,
                direction => FreeNeighborCount(run, Next(run, direction)));
    }

    private static bool IsSafe(SnakeRun run, Direction direction)
    {
        if (run.HasPhaseShift)
        {
            return true;
        }

        var next = Next(run, direction);
        var bodyCollision = run.Body.Contains(next) && next != run.Body[0];
        var obstacleCollision = run.DetachedObstacles.Contains(next);
        return !bodyCollision && !obstacleCollision;
    }

    private static int FreeNeighborCount(SnakeRun run, GridPoint point)
    {
        var count = 0;
        foreach (var direction in Directions)
        {
            var neighbor = point
                .Add(direction.Offset())
                .Wrap(run.Configuration.Width, run.Configuration.Height);
            if (!run.Body.Contains(neighbor) && !run.DetachedObstacles.Contains(neighbor))
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
            if (run.Body.Contains(neighbor) || run.DetachedObstacles.Contains(neighbor))
            {
                count++;
            }
        }

        return count;
    }

    private static int DistanceToFood(SnakeRun run, Direction direction) =>
        DistanceToTarget(run, direction, run.Food);

    private static int DistanceToTarget(
        SnakeRun run,
        Direction direction,
        GridPoint? target)
    {
        if (target is not { } point)
        {
            return 0;
        }

        var next = Next(run, direction);
        return WrappedDistance(next.X, point.X, run.Configuration.Width)
            + WrappedDistance(next.Y, point.Y, run.Configuration.Height);
    }

    private static int WrappedDistance(int left, int right, int size)
    {
        var direct = Math.Abs(left - right);
        return Math.Min(direct, size - direct);
    }

    private static GridPoint Next(SnakeRun run, Direction direction) =>
        run.Head
            .Add(direction.Offset())
            .Wrap(run.Configuration.Width, run.Configuration.Height);

    private static Direction SelectMinimum(
        IEnumerable<Direction> candidates,
        Func<Direction, int> score) =>
        candidates
            .Select(direction => (Direction: direction, Score: score(direction)))
            .OrderBy(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Direction)
            .First()
            .Direction;

    private static Direction SelectMaximum(
        IEnumerable<Direction> candidates,
        Func<Direction, int> score) =>
        candidates
            .Select(direction => (Direction: direction, Score: score(direction)))
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Direction)
            .First()
            .Direction;
}
