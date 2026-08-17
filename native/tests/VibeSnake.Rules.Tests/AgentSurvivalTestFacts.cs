using VibeSnake.AgentPlay;
using VibeSnake.Rules;

namespace VibeSnake.Rules.Tests;

/// <summary>
/// Builds the survival block a viewer frame must carry for a given observation.
/// Tests that are not about the survival contract use this so they keep exercising
/// what they were written to exercise; the survival contract has its own tests that
/// tamper with the block and require rejection.
/// </summary>
internal static class AgentSurvivalTestFacts
{
    // Records compare their recovery list by reference, so equivalence is asserted
    // field by field instead of with a single Assert.Equal on the record.
    public static void AssertSurvivalEquivalent(
        AgentSurvivalStateV1 expected,
        AgentSurvivalStateV1 actual)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(actual);
        Assert.Equal(expected.Schema, actual.Schema);
        Assert.Equal(expected.CandidateExits, actual.CandidateExits);
        Assert.Equal(expected.StructuralOpenExits, actual.StructuralOpenExits);
        Assert.Equal(expected.ExitPressure, actual.ExitPressure);
        Assert.Equal(expected.HeldRecoveryCount, actual.HeldRecoveryCount);
        Assert.Equal(expected.RecoveryResources, actual.RecoveryResources);
    }

    public static AgentSurvivalStateV1 SurvivalFor(AgentObservationV5 observation)
    {
        ArgumentNullException.ThrowIfNull(observation);
        return AgentSurvivalStateV1.Create(
            observation.Status == RunStatus.Running,
            StructuralOpenExits(observation),
            observation.ShieldTicksRemaining,
            observation.PhaseShiftTicksRemaining,
            observation.LastStandHeld,
            observation.LastStandRecoveryTicksRemaining,
            observation.SlowMoTicksRemaining);
    }

    private static int StructuralOpenExits(AgentObservationV5 observation)
    {
        if (observation.Status != RunStatus.Running || observation.Body.Count == 0)
        {
            return 0;
        }

        var effectiveDirection = observation.PendingDirections.Count == 0
            ? observation.Direction
            : observation.PendingDirections[^1];
        var openExits = 0;
        foreach (var candidateDirection in Enum.GetValues<Direction>())
        {
            if (candidateDirection == effectiveDirection.Opposite())
            {
                continue;
            }

            var wrapped = new GridPoint(observation.Head.X, observation.Head.Y)
                .Add(candidateDirection.Offset())
                .Wrap(observation.BoardWidth, observation.BoardHeight);
            var candidate = new AgentPointV1(wrapped.X, wrapped.Y);
            var grows = observation.Food == candidate
                && observation.GluttonyTicksRemaining <= 0;
            var movesOntoDepartingTail = !grows
                && candidate == observation.Body[0]
                && !observation.Body.Skip(1).Contains(candidate);
            var structurallyBlocked =
                observation.Body.Contains(candidate) && !movesOntoDepartingTail
                || observation.DetachedObstacles.Contains(candidate);
            if (!structurallyBlocked)
            {
                openExits++;
            }
        }

        return openExits;
    }
}
