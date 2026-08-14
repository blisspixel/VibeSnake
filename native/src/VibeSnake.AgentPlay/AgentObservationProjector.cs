using VibeSnake.Rules;

namespace VibeSnake.AgentPlay;

internal static class AgentObservationProjector
{
    public static AgentObservationV3 Project(
        AgentMatchOptions options,
        RunConfig config,
        RunSnapshot snapshot,
        IReadOnlyList<RunEventDetail> previousEvents,
        AgentPreviousActionV1? previousAction,
        AgentMatchLifecycle lifecycle,
        AgentEpisodeMetricsV1 metrics,
        AgentRivalObservationV1? rival)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(previousEvents);
        ArgumentNullException.ThrowIfNull(metrics);

        var body = Array.AsReadOnly(snapshot.Body.Select(ProjectPoint).ToArray());
        var pendingDirections = Array.AsReadOnly(snapshot.PendingDirections.ToArray());
        var detachedObstacles = Array.AsReadOnly(
            snapshot.DetachedObstacles.Select(ProjectPoint).ToArray());
        var events = Array.AsReadOnly(previousEvents.Select(ProjectEvent).ToArray());
        var powerPickup = snapshot.PowerPickup is { } pickup
            ? new AgentPowerPickupV1(
                pickup.Kind,
                ProjectPoint(pickup.Position),
                pickup.VisibilityTicksRemaining)
            : null;

        return new AgentObservationV3(
            AgentObservationV3.Contract,
            options.MatchId,
            RulesetIdentity.CurrentId,
            RulesetIdentity.CurrentVersion,
            options.ModeId,
            options.ModeVersion,
            RunConfig.ConfigHashAlgorithmId,
            config.ComputeConfigHash(),
            options.SeedVisibility,
            options.SeedVisibility == AgentSeedVisibility.Open
                ? options.GameplaySeed
                : null,
            options.Passport,
            snapshot.Tick,
            options.MaximumSteps,
            Math.Max(0, options.MaximumSteps - snapshot.Tick),
            snapshot.StateHash,
            config.Width,
            config.Height,
            WrapsAtEdges: true,
            snapshot.Status,
            snapshot.DeathCause,
            snapshot.Direction,
            ProjectPoint(snapshot.Head),
            body,
            pendingDirections,
            snapshot.Food is { } food ? ProjectPoint(food) : null,
            snapshot.Score,
            snapshot.ComboCount,
            snapshot.ComboMultiplier,
            snapshot.TicksSinceLastFood,
            snapshot.HungerTicksRemaining,
            snapshot.HungerMaximumTicks,
            snapshot.HungerWarningTicks,
            powerPickup,
            snapshot.PowerSpawnTicksElapsed,
            snapshot.ShieldTicksRemaining,
            snapshot.PhaseShiftTicksRemaining,
            snapshot.LastStandHeld,
            snapshot.LastStandRecoveryTicksRemaining,
            snapshot.SlowMoTicksRemaining,
            snapshot.BoostTicksRemaining,
            snapshot.MagnetTicksRemaining,
            snapshot.GluttonyTicksRemaining,
            snapshot.BaitPosition is { } bait ? ProjectPoint(bait) : null,
            detachedObstacles,
            snapshot.DetachedObstacleTicksRemaining,
            snapshot.AdaptiveDifficultyState,
            snapshot.AdaptivePolicyId,
            snapshot.AdaptationEnabled,
            events,
            previousAction,
            lifecycle,
            lifecycle == AgentMatchLifecycle.AwaitingAction
                && snapshot.Status == RunStatus.Running
                && snapshot.Tick < options.MaximumSteps,
            metrics,
            options.StyleContractId is null
                ? null
                : AgentStyleContractCatalog.Evaluate(
                    options.StyleContractId,
                    options.ModeId,
                    metrics),
            options.LessonId is null
                ? null
                : AgentSignalSchoolCatalog.Evaluate(options.LessonId, metrics),
            rival);
    }

    private static AgentPointV1 ProjectPoint(GridPoint point) => new(point.X, point.Y);

    private static AgentPublicEventV1 ProjectEvent(RunEventDetail detail) =>
        new(
            detail.Kind,
            detail.Position is { } position ? ProjectPoint(position) : null,
            detail.NewDirection,
            detail.Value,
            detail.Cause,
            detail.Power);
}
