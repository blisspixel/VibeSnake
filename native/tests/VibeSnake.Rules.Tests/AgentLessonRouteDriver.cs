using VibeSnake.AgentPlay;
using VibeSnake.Rules;

namespace VibeSnake.Rules.Tests;

internal sealed record AgentLessonRouteCall(
    string IdempotencyKey,
    AgentAction Action,
    bool Accepted,
    bool RulesAdvanced,
    AgentActionRejection Rejection,
    int StepsAdvanced);

internal sealed record AgentLessonRouteRun(
    AgentSignalLessonDefinitionV2 Definition,
    string ActionProfile,
    IReadOnlyList<AgentLessonRouteCall> Calls,
    AgentMatchResultV5 Result);

internal static class AgentLessonRouteDriver
{
    public static AgentLessonRouteRun DriveSession(
        AgentSignalLessonDefinitionV2 definition,
        string actionProfile = AgentPassportV4.FourDirectionActionProfile)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var keyPrefix = $"route-{definition.Id}";
        var session = new AgentMatchSession(new AgentMatchOptions(
            keyPrefix,
            definition.ModeId,
            RunModeCatalog.CurrentModeVersion,
            definition.PracticeSeed,
            AgentSeedVisibility.Open,
            definition.MaximumSteps,
            actionProfile: actionProfile,
            lessonId: definition.Id));
        var calls = new List<AgentLessonRouteCall>();
        if (definition.Id == AgentSignalSchoolCatalog.FirstTurnId)
        {
            var initial = session.Observe();
            Submit(
                session,
                actionProfile,
                $"{keyPrefix}-reversal",
                initial,
                OppositeAction(initial),
                calls);
        }

        AgentMatchResultV5? result = null;
        for (var step = 0; step < definition.MaximumSteps && result is null; step++)
        {
            var observation = session.Observe();
            if (observation.LessonProgress!.AllRequirementsSatisfied)
            {
                break;
            }

            result = Submit(
                session,
                actionProfile,
                $"{keyPrefix}-{step}",
                observation,
                ChooseAction(definition.Id, observation),
                calls);
        }

        result ??= session.Finish();
        return new AgentLessonRouteRun(
            definition,
            actionProfile,
            calls.AsReadOnly(),
            result);
    }

    public static AgentAction ChooseAction(
        string lessonId,
        AgentObservationV5 observation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(lessonId);
        ArgumentNullException.ThrowIfNull(observation);
        if (lessonId == AgentSignalSchoolCatalog.FirstTurnId)
        {
            return ToAction(TurnLeft(observation.Direction), observation.Direction);
        }

        if (lessonId == AgentSignalSchoolCatalog.WrapLineId)
        {
            return AgentAction.Continue;
        }

        if (lessonId == AgentSignalSchoolCatalog.DeathReadId)
        {
            return observation.Body.Count < 5 && observation.Food is { } food
                ? FindPathAction(observation, food)
                : ToAction(TurnLeft(observation.Direction), observation.Direction);
        }

        if (lessonId == AgentSignalSchoolCatalog.RecoverRouteId
            && (observation.ShieldTicksRemaining > 0
                || observation.PhaseShiftTicksRemaining > 0
                || observation.LastStandHeld))
        {
            return ToAction(TurnLeft(observation.Direction), observation.Direction);
        }

        var target = ResolveTarget(lessonId, observation);
        return target is null
            ? AgentAction.Continue
            : FindPathAction(observation, target.Value);
    }

    public static int ChooseBurstMaximumSteps(
        string lessonId,
        AgentObservationV5 observation,
        AgentAction action)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(lessonId);
        ArgumentNullException.ThrowIfNull(observation);
        if (lessonId == AgentSignalSchoolCatalog.FirstTurnId
            || lessonId == AgentSignalSchoolCatalog.WrapLineId
            || lessonId == AgentSignalSchoolCatalog.DeathReadId && observation.Body.Count >= 5
            || lessonId == AgentSignalSchoolCatalog.RecoverRouteId
                && (observation.ShieldTicksRemaining > 0
                    || observation.PhaseShiftTicksRemaining > 0
                    || observation.LastStandHeld))
        {
            return AgentBurstRequest.MaximumBurstSteps;
        }

        var target = ResolveTarget(lessonId, observation);
        if (target is null)
        {
            return AgentBurstRequest.MaximumBurstSteps;
        }

        var path = FindPathDirections(observation, target.Value);
        if (path.Length == 0
            || ToAction(path[0], observation.Direction) != action)
        {
            return 1;
        }

        return Math.Min(
            AgentBurstRequest.MaximumBurstSteps,
            path.TakeWhile(direction => direction == path[0]).Count());
    }

    public static AgentAction OppositeAction(AgentObservationV5 observation)
    {
        ArgumentNullException.ThrowIfNull(observation);
        return ToAction(observation.Direction.Opposite(), observation.Direction);
    }

    private static AgentMatchResultV5? Submit(
        AgentMatchSession session,
        string actionProfile,
        string key,
        AgentObservationV5 observation,
        AgentAction action,
        List<AgentLessonRouteCall> calls)
    {
        if (actionProfile == AgentPassportV4.FourDirectionActionProfile)
        {
            var response = session.SubmitAction(new AgentActionRequest(
                key,
                observation.Tick,
                observation.StateHash,
                action));
            calls.Add(new AgentLessonRouteCall(
                key,
                action,
                response.Accepted,
                response.RulesAdvanced,
                response.Rejection,
                response.RulesAdvanced ? 1 : 0));
            return response.MatchResult;
        }

        if (actionProfile == AgentPassportV4.FourDirectionBurstActionProfile)
        {
            var maximumSteps = ChooseBurstMaximumSteps(
                observation.LessonProgress?.LessonId
                    ?? throw new InvalidOperationException("Lesson route lost its lesson identity."),
                observation,
                action);
            var response = session.SubmitBurst(new AgentBurstRequest(
                key,
                observation.Tick,
                observation.StateHash,
                action,
                maximumSteps));
            calls.Add(new AgentLessonRouteCall(
                key,
                action,
                response.Accepted,
                response.RulesAdvanced,
                response.Rejection,
                response.StepsAdvanced));
            return response.MatchResult;
        }

        throw new ArgumentException("Unsupported lesson route action profile.", nameof(actionProfile));
    }

    private static AgentAction FindPathAction(
        AgentObservationV5 observation,
        AgentPointV1 target)
    {
        var path = FindPathDirections(observation, target);
        return path.Length == 0
            ? AgentAction.Continue
            : ToAction(path[0], observation.Direction);
    }

    private static Direction[] FindPathDirections(
        AgentObservationV5 observation,
        AgentPointV1 target)
    {
        var blocked = observation.Body.Skip(1)
            .Concat(observation.DetachedObstacles)
            .ToHashSet();
        var queue = new Queue<(AgentPointV1 Point, Direction[] Path)>();
        var visited = new HashSet<AgentPointV1> { observation.Head };
        foreach (var direction in CandidateDirections(observation.Direction))
        {
            var next = Advance(observation, observation.Head, direction);
            if (next is null || blocked.Contains(next.Value) || !visited.Add(next.Value))
            {
                continue;
            }

            if (next.Value == target)
            {
                return [direction];
            }
            queue.Enqueue((next.Value, [direction]));
        }

        while (queue.TryDequeue(out var current))
        {
            foreach (var direction in Enum.GetValues<Direction>())
            {
                var next = Advance(observation, current.Point, direction);
                if (next is null || blocked.Contains(next.Value) || !visited.Add(next.Value))
                {
                    continue;
                }

                var path = current.Path.Append(direction).ToArray();
                if (next.Value == target)
                {
                    return path;
                }
                queue.Enqueue((next.Value, path));
            }
        }

        return [];
    }

    private static AgentPointV1? ResolveTarget(
        string lessonId,
        AgentObservationV5 observation) =>
        lessonId is AgentSignalSchoolCatalog.PowerRouteId
            or AgentSignalSchoolCatalog.RecoverRouteId
            ? observation.PowerPickup?.Position ?? observation.Food
            : observation.Food;

    private static Direction[] CandidateDirections(Direction current) =>
        [current, TurnLeft(current), TurnRight(current)];

    private static AgentPointV1? Advance(
        AgentObservationV5 observation,
        AgentPointV1 point,
        Direction direction)
    {
        var offset = direction.Offset();
        var x = point.X + offset.X;
        var y = point.Y + offset.Y;
        if (observation.WrapsAtEdges)
        {
            x = (x + observation.BoardWidth) % observation.BoardWidth;
            y = (y + observation.BoardHeight) % observation.BoardHeight;
        }
        else if (x < 0 || x >= observation.BoardWidth || y < 0 || y >= observation.BoardHeight)
        {
            return null;
        }

        return new AgentPointV1(x, y);
    }

    private static Direction TurnLeft(Direction direction) => direction switch
    {
        Direction.Up => Direction.Left,
        Direction.Right => Direction.Up,
        Direction.Down => Direction.Right,
        Direction.Left => Direction.Down,
        _ => throw new ArgumentOutOfRangeException(nameof(direction)),
    };

    private static Direction TurnRight(Direction direction) => direction switch
    {
        Direction.Up => Direction.Right,
        Direction.Right => Direction.Down,
        Direction.Down => Direction.Left,
        Direction.Left => Direction.Up,
        _ => throw new ArgumentOutOfRangeException(nameof(direction)),
    };

    private static AgentAction ToAction(Direction direction, Direction current) =>
        direction == current
            ? AgentAction.Continue
            : direction switch
            {
                Direction.Up => AgentAction.Up,
                Direction.Right => AgentAction.Right,
                Direction.Down => AgentAction.Down,
                Direction.Left => AgentAction.Left,
                _ => throw new ArgumentOutOfRangeException(nameof(direction)),
            };
}
