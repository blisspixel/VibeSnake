using VibeSnake.AgentPlay;
using VibeSnake.Rules;

namespace VibeSnake.Rules.Tests;

public sealed class AgentObservationProjectorTests
{
    [Fact]
    public void Projector_copies_the_complete_public_allowlist()
    {
        var options = new AgentMatchOptions(
            "projection",
            RunModeCatalog.VibeId,
            RunModeCatalog.CurrentModeVersion,
            77UL,
            AgentSeedVisibility.Open,
            maximumSteps: 10);
        var config = options.CreateRunConfig();
        var body = new List<GridPoint> { new(1, 2), new(2, 2) };
        var pending = new List<Direction> { Direction.Up };
        var detached = new List<GridPoint> { new(8, 9) };
        var snapshot = new RunSnapshot(
            Tick: 3,
            Status: RunStatus.Running,
            DeathCause: DeathCause.None,
            Direction: Direction.Right,
            Body: body,
            PendingDirections: pending,
            Food: new GridPoint(4, 5),
            Score: 120,
            ComboCount: 4,
            ComboMultiplier: 1.75,
            TicksSinceLastFood: 6,
            HungerTicksRemaining: 81,
            HungerMaximumTicks: 100,
            HungerWarningTicks: 20,
            PowerPickup: new PowerPickup(PowerKind.Magnet, new GridPoint(6, 7), 9),
            PowerSpawnTicksElapsed: 11,
            ShieldTicksRemaining: 12,
            PhaseShiftTicksRemaining: 13,
            LastStandHeld: true,
            LastStandRecoveryTicksRemaining: 14,
            SlowMoTicksRemaining: 15,
            BoostTicksRemaining: 16,
            MagnetTicksRemaining: 17,
            GluttonyTicksRemaining: 18,
            BaitPosition: new GridPoint(7, 8),
            DetachedObstacles: detached,
            DetachedObstacleTicksRemaining: 19,
            StateHash: "0123456789abcdef",
            AdaptiveDifficultyState: AdaptiveDifficultyState.Pressure,
            AdaptivePolicyId: AdaptiveDifficultyPolicy.CurrentPolicyId,
            AdaptationEnabled: true);
        RunEventDetail[] events =
        [
            new(
                RunEventKind.PowerActivated,
                Position: new GridPoint(3, 4),
                NewDirection: Direction.Down,
                Value: 22,
                Cause: DeathCause.SelfCollision,
                Power: PowerKind.Shield),
            new(RunEventKind.Moved),
        ];
        var previous = new AgentPreviousActionV1(
            AgentAction.Down,
            Accepted: true,
            AgentActionRejection.None,
            RulesAdvanced: true,
            AgentPublicIntent.Recover);
        var metrics = new AgentEpisodeMetricsV1(
            AgentEpisodeMetricsV1.Contract,
            3,
            2,
            4,
            1,
            1,
            1,
            1,
            0,
            0,
            1);

        var observation = AgentObservationProjector.Project(
            options,
            config,
            snapshot,
            events,
            previous,
            AgentMatchLifecycle.AwaitingAction,
            metrics,
            rival: null);

        Assert.Equal(AgentObservationV4.Contract, observation.Schema);
        Assert.Equal("projection", observation.MatchId);
        Assert.Equal(RulesetIdentity.CurrentId, observation.RulesetId);
        Assert.Equal(RulesetIdentity.CurrentVersion, observation.RulesVersion);
        Assert.Equal(RunModeCatalog.VibeId, observation.ModeId);
        Assert.Equal(RunModeCatalog.CurrentModeVersion, observation.ModeVersion);
        Assert.Equal(config.ComputeConfigHash(), observation.ConfigHash);
        Assert.Equal(RunConfig.ConfigHashAlgorithmId, observation.ConfigHashAlgorithm);
        Assert.Equal(AgentSeedVisibility.Open, observation.SeedVisibility);
        Assert.Equal(77UL, observation.GameplaySeed);
        Assert.Same(AgentPassportV3.Anonymous, observation.Passport);
        Assert.Equal(3, observation.Tick);
        Assert.Equal(10, observation.MaximumSteps);
        Assert.Equal(7, observation.StepsRemaining);
        Assert.Equal("0123456789abcdef", observation.StateHash);
        Assert.Equal(config.Width, observation.BoardWidth);
        Assert.Equal(config.Height, observation.BoardHeight);
        Assert.True(observation.WrapsAtEdges);
        Assert.Equal(RunStatus.Running, observation.Status);
        Assert.Equal(DeathCause.None, observation.DeathCause);
        Assert.Equal(Direction.Right, observation.Direction);
        Assert.Equal(new AgentPointV1(2, 2), observation.Head);
        Assert.Equal([new AgentPointV1(1, 2), new AgentPointV1(2, 2)], observation.Body);
        Assert.Equal([Direction.Up], observation.PendingDirections);
        Assert.Equal(new AgentPointV1(4, 5), observation.Food);
        Assert.Equal(120, observation.Score);
        Assert.Equal(4, observation.ComboCount);
        Assert.Equal(1.75, observation.ComboMultiplier);
        Assert.Equal(6, observation.TicksSinceLastFood);
        Assert.Equal(81, observation.HungerTicksRemaining);
        Assert.Equal(100, observation.HungerMaximumTicks);
        Assert.Equal(20, observation.HungerWarningTicks);
        Assert.Equal(new AgentPointV1(6, 7), observation.PowerPickup!.Position);
        Assert.Equal(PowerKind.Magnet, observation.PowerPickup.Kind);
        Assert.Equal(9, observation.PowerPickup.VisibilityTicksRemaining);
        Assert.Equal(11, observation.PowerSpawnTicksElapsed);
        Assert.Equal(12, observation.ShieldTicksRemaining);
        Assert.Equal(13, observation.PhaseShiftTicksRemaining);
        Assert.True(observation.LastStandHeld);
        Assert.Equal(14, observation.LastStandRecoveryTicksRemaining);
        Assert.Equal(15, observation.SlowMoTicksRemaining);
        Assert.Equal(16, observation.BoostTicksRemaining);
        Assert.Equal(17, observation.MagnetTicksRemaining);
        Assert.Equal(18, observation.GluttonyTicksRemaining);
        Assert.Equal(new AgentPointV1(7, 8), observation.BaitPosition);
        Assert.Equal([new AgentPointV1(8, 9)], observation.DetachedObstacles);
        Assert.Equal(19, observation.DetachedObstacleTicksRemaining);
        Assert.Equal(AdaptiveDifficultyState.Pressure, observation.AdaptiveDifficultyState);
        Assert.Equal(AdaptiveDifficultyPolicy.CurrentPolicyId, observation.AdaptivePolicyId);
        Assert.True(observation.AdaptationEnabled);
        Assert.Equal(AgentMatchLifecycle.AwaitingAction, observation.Lifecycle);
        Assert.True(observation.IsActionAwaited);
        Assert.Same(metrics, observation.EpisodeMetrics);
        Assert.Null(observation.StyleContract);
        Assert.Same(previous, observation.PreviousAction);
        var projectedPrevious = Assert.IsType<AgentPreviousActionV1>(
            observation.PreviousAction);
        Assert.True(projectedPrevious.Accepted);
        Assert.Equal(AgentActionRejection.None, projectedPrevious.Rejection);
        Assert.True(projectedPrevious.RulesAdvanced);
        Assert.Equal(AgentPublicIntent.Recover, projectedPrevious.DeclaredIntent);
        Assert.Equal(2, observation.PreviousEvents.Count);
        Assert.Equal(new AgentPointV1(3, 4), observation.PreviousEvents[0].Position);
        Assert.Equal(Direction.Down, observation.PreviousEvents[0].NewDirection);
        Assert.Equal(22, observation.PreviousEvents[0].Value);
        Assert.Equal(DeathCause.SelfCollision, observation.PreviousEvents[0].Cause);
        Assert.Equal(PowerKind.Shield, observation.PreviousEvents[0].Power);
        Assert.Null(observation.PreviousEvents[1].Position);

        body[0] = new GridPoint(40, 40);
        pending.Clear();
        detached.Clear();
        events[0] = new RunEventDetail(RunEventKind.Died);
        Assert.Equal(new AgentPointV1(1, 2), observation.Body[0]);
        Assert.Single(observation.PendingDirections);
        Assert.Single(observation.DetachedObstacles);
        Assert.Equal(RunEventKind.PowerActivated, observation.PreviousEvents[0].Kind);
    }

    [Fact]
    public void Projector_hides_blind_seed_and_handles_absent_optional_state()
    {
        var options = new AgentMatchOptions(
            "blind",
            RunModeCatalog.ClassicId,
            RunModeCatalog.CurrentModeVersion,
            ulong.MaxValue,
            AgentSeedVisibility.Blind,
            maximumSteps: 1);
        var run = SnakeRun.Create(options.GameplaySeed, options.CreateRunConfig());

        var observation = AgentObservationProjector.Project(
            options,
            options.CreateRunConfig(),
            run.GetSnapshot(),
            Array.Empty<RunEventDetail>(),
            previousAction: null,
            AgentMatchLifecycle.Completed,
            new AgentEpisodeMetricsTracker().Snapshot(0),
            rival: null);

        Assert.Null(observation.GameplaySeed);
        Assert.Null(observation.PowerPickup);
        Assert.Null(observation.BaitPosition);
        Assert.Null(observation.PreviousAction);
        Assert.Empty(observation.PreviousEvents);
        Assert.False(observation.IsActionAwaited);
    }

    [Fact]
    public void Projector_rejects_null_inputs()
    {
        var options = new AgentMatchOptions(
            "nulls",
            RunModeCatalog.ClassicId,
            RunModeCatalog.CurrentModeVersion,
            1UL,
            AgentSeedVisibility.Blind);
        var config = options.CreateRunConfig();
        var snapshot = SnakeRun.Create(1UL, config).GetSnapshot();
        var metrics = new AgentEpisodeMetricsTracker().Snapshot(0);

        Assert.Throws<ArgumentNullException>(() => AgentObservationProjector.Project(
            null!, config, snapshot, [], null, AgentMatchLifecycle.AwaitingAction, metrics, null));
        Assert.Throws<ArgumentNullException>(() => AgentObservationProjector.Project(
            options, null!, snapshot, [], null, AgentMatchLifecycle.AwaitingAction, metrics, null));
        Assert.Throws<ArgumentNullException>(() => AgentObservationProjector.Project(
            options, config, null!, [], null, AgentMatchLifecycle.AwaitingAction, metrics, null));
        Assert.Throws<ArgumentNullException>(() => AgentObservationProjector.Project(
            options, config, snapshot, null!, null, AgentMatchLifecycle.AwaitingAction, metrics, null));
        Assert.Throws<ArgumentNullException>(() => AgentObservationProjector.Project(
            options, config, snapshot, [], null, AgentMatchLifecycle.AwaitingAction, null!, null));
    }

    [Fact]
    public void Public_observation_contract_exposes_no_private_state_categories()
    {
        string[] forbiddenTerms =
        [
            "Random",
            "Rng",
            "Profile",
            "Achievement",
            "Progression",
            "Prompt",
            "Reasoning",
            "Credential",
            "Path",
            "Diagnostic",
            "Future",
        ];
        var propertyNames = typeof(AgentObservationV4)
            .GetProperties()
            .Select(property => property.Name)
            .ToArray();

        foreach (var forbiddenTerm in forbiddenTerms)
        {
            Assert.DoesNotContain(
                propertyNames,
                name => name.Contains(forbiddenTerm, StringComparison.OrdinalIgnoreCase));
        }
    }
}
