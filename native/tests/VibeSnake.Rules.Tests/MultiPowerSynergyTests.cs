namespace VibeSnake.Rules.Tests;

/// <summary>
/// V040-11 multi-power synergy and anti-synergy campaigns: coexistence, collision
/// precedence handoffs, cross-family composition, restore, and restart cleanup.
/// </summary>
public sealed class MultiPowerSynergyTests
{
    [Fact]
    public void Protection_stack_coexists_and_counts_down_together()
    {
        var run = CreateSynergyRun(
            body: LoopBody(),
            direction: Direction.Right,
            food: new GridPoint(4, 3),
            shieldTicksRemaining: 4,
            phaseShiftTicksRemaining: 3,
            lastStandHeld: true);

        Assert.True(run.HasShield);
        Assert.True(run.HasPhaseShift);
        Assert.True(run.LastStandHeld);

        // Safe step: loop body head is (2,1); Right into free cell (3,1).
        var result = run.Step();

        Assert.Equal(RunStatus.Running, run.Status);
        Assert.Equal(3, run.ShieldTicksRemaining);
        Assert.Equal(2, run.PhaseShiftTicksRemaining);
        Assert.True(run.LastStandHeld);
        Assert.Equal(new GridPoint(3, 1), run.Head);
        Assert.Equal(RunEvent.Moved, result.Events);
        Assert.DoesNotContain(
            result.OrderedEvents,
            value => value.Kind is RunEventKind.PowerConsumed or RunEventKind.CollisionPrevented);
    }

    [Fact]
    public void Phase_shift_expiry_hands_self_collision_to_shield()
    {
        // Phase expires before collision resolution on the same step (lifecycle first).
        // With Phase at 1, Shield at 3: Phase expires, then Shield consumes the collision.
        var run = CreateSynergyRun(
            body: LoopBody(),
            direction: Direction.Down,
            food: new GridPoint(4, 3),
            shieldTicksRemaining: 3,
            phaseShiftTicksRemaining: 1);

        var result = run.Step();

        Assert.Equal(RunStatus.Running, run.Status);
        Assert.False(run.HasPhaseShift);
        Assert.False(run.HasShield);
        Assert.Equal(LoopBody(), run.Body);
        Assert.Contains(
            result.OrderedEvents,
            value => value is { Kind: RunEventKind.PowerExpired, Power: PowerKind.PhaseShift });
        Assert.Contains(
            result.OrderedEvents,
            value => value is { Kind: RunEventKind.PowerConsumed, Power: PowerKind.Shield });
        Assert.Contains(
            result.OrderedEvents,
            value => value is { Kind: RunEventKind.CollisionPrevented, Power: PowerKind.Shield });
    }

    [Fact]
    public void Shield_consumes_before_held_last_stand_without_phase()
    {
        var run = CreateSynergyRun(
            body: LoopBody(),
            direction: Direction.Down,
            food: new GridPoint(4, 3),
            shieldTicksRemaining: 2,
            lastStandHeld: true);

        var result = run.Step();

        Assert.Equal(RunStatus.Running, run.Status);
        Assert.False(run.HasShield);
        Assert.True(run.LastStandHeld);
        Assert.Equal(0, run.LastStandRecoveryTicksRemaining);
        Assert.Equal(LoopBody(), run.Body);
        Assert.Equal(
            [
                RunEventKind.PowerConsumed,
                RunEventKind.CollisionPrevented,
            ],
            result.OrderedEvents.Select(value => value.Kind));
        Assert.Equal(PowerKind.Shield, result.OrderedEvents[0].Power);
        Assert.Equal(PowerKind.Shield, result.OrderedEvents[1].Power);
    }

    [Fact]
    public void Tempo_protection_and_harvest_compose_without_leaking_state()
    {
        var run = CreateSynergyRun(
            body: [new GridPoint(2, 2)],
            direction: Direction.Right,
            food: new GridPoint(6, 5),
            config: FullPortfolioConfig(width: 10, height: 10),
            shieldTicksRemaining: 4,
            phaseShiftTicksRemaining: 3,
            slowMoTicksRemaining: 4,
            boostTicksRemaining: 4,
            magnetTicksRemaining: 3,
            gluttonyTicksRemaining: 3);

        Assert.Equal(2, run.MovementCadenceNumerator);
        Assert.Equal(2, run.MovementCadenceDenominator);
        Assert.Equal(50, run.EffectiveRulesStepMilliseconds);

        var beforeFood = run.Food;
        run.Step();

        Assert.Equal(RunStatus.Running, run.Status);
        Assert.Equal(new GridPoint(3, 2), run.Head);
        Assert.Equal(3, run.ShieldTicksRemaining);
        Assert.Equal(2, run.PhaseShiftTicksRemaining);
        Assert.Equal(3, run.SlowMoTicksRemaining);
        Assert.Equal(3, run.BoostTicksRemaining);
        Assert.Equal(2, run.MagnetTicksRemaining);
        Assert.Equal(2, run.GluttonyTicksRemaining);
        // Magnet pulls food one Chebyshev step toward the head each rules tick.
        Assert.NotEqual(beforeFood, run.Food);
        Assert.Equal(new GridPoint(5, 4), run.Food);
    }

    [Fact]
    public void Magnet_and_gluttony_pull_then_eat_without_growth()
    {
        // Food one cell ahead of head: magnet would pull onto head path; gluttony suppresses growth.
        var run = CreateSynergyRun(
            body: [new GridPoint(1, 1), new GridPoint(2, 1)],
            direction: Direction.Right,
            food: new GridPoint(3, 1),
            config: FullPortfolioConfig(width: 8, height: 4),
            magnetTicksRemaining: 3,
            gluttonyTicksRemaining: 3);

        var lengthBefore = run.Body.Count;
        var result = run.Step();

        Assert.Equal(lengthBefore, run.Body.Count);
        Assert.Equal(new GridPoint(3, 1), run.Head);
        Assert.Contains(result.OrderedEvents, value => value.Kind == RunEventKind.AteFood);
        Assert.Equal(2, run.MagnetTicksRemaining);
        Assert.Equal(2, run.GluttonyTicksRemaining);
        Assert.True(run.HungerTicksRemaining > 50);
    }

    [Fact]
    public void Phase_shift_with_detached_obstacles_bypasses_without_consuming_shield()
    {
        var run = CreateSynergyRun(
            body: [new GridPoint(2, 1)],
            direction: Direction.Left,
            food: new GridPoint(8, 1),
            config: FullPortfolioConfig(width: 10, height: 4),
            shieldTicksRemaining: 4,
            phaseShiftTicksRemaining: 3,
            detachedObstacles: [new GridPoint(1, 1)],
            detachedObstacleTicksRemaining: 4);

        var result = run.Step();

        Assert.Equal(RunStatus.Running, run.Status);
        Assert.Equal(new GridPoint(1, 1), run.Head);
        Assert.True(run.HasShield);
        Assert.Equal(3, run.ShieldTicksRemaining);
        Assert.True(run.HasPhaseShift);
        Assert.Equal(2, run.PhaseShiftTicksRemaining);
        Assert.DoesNotContain(
            result.OrderedEvents,
            value => value.Kind is RunEventKind.PowerConsumed or RunEventKind.Died);
    }

    [Fact]
    public void Collecting_phase_shift_while_shield_active_is_legal_synergy()
    {
        var run = CreateSynergyRun(
            body: [new GridPoint(1, 1)],
            direction: Direction.Right,
            food: new GridPoint(4, 3),
            config: FullPortfolioConfig(),
            powerPickup: new PowerPickup(PowerKind.PhaseShift, new GridPoint(2, 1), 4),
            shieldTicksRemaining: 5);

        var result = run.Step();

        Assert.True(run.HasShield);
        Assert.True(run.HasPhaseShift);
        // Shield lifecycle ticks before collection; Phase activates at full duration this step.
        Assert.Equal(4, run.ShieldTicksRemaining);
        Assert.Equal(5, run.PhaseShiftTicksRemaining);
        Assert.Null(run.PowerPickup);
        Assert.Contains(
            result.OrderedEvents,
            value => value is { Kind: RunEventKind.PowerCollected, Power: PowerKind.PhaseShift });
        Assert.Contains(
            result.OrderedEvents,
            value => value is { Kind: RunEventKind.PowerActivated, Power: PowerKind.PhaseShift });
    }

    [Theory]
    [InlineData(PowerKind.Shield)]
    [InlineData(PowerKind.PhaseShift)]
    [InlineData(PowerKind.SlowMo)]
    [InlineData(PowerKind.Boost)]
    [InlineData(PowerKind.Magnet)]
    [InlineData(PowerKind.Gluttony)]
    public void Anti_synergy_rejects_same_kind_pickup_while_effect_active(PowerKind kind)
    {
        Assert.Throws<ArgumentException>(() =>
            CreateSynergyRun(
                body: [new GridPoint(1, 1)],
                direction: Direction.Right,
                food: new GridPoint(4, 3),
                config: FullPortfolioConfig(),
                powerPickup: new PowerPickup(kind, new GridPoint(2, 1), 4),
                shieldTicksRemaining: kind == PowerKind.Shield ? 2 : 0,
                phaseShiftTicksRemaining: kind == PowerKind.PhaseShift ? 2 : 0,
                slowMoTicksRemaining: kind == PowerKind.SlowMo ? 2 : 0,
                boostTicksRemaining: kind == PowerKind.Boost ? 2 : 0,
                magnetTicksRemaining: kind == PowerKind.Magnet ? 2 : 0,
                gluttonyTicksRemaining: kind == PowerKind.Gluttony ? 2 : 0));
    }

    [Fact]
    public void Anti_synergy_rejects_last_stand_pickup_while_held()
    {
        Assert.Throws<ArgumentException>(() =>
            CreateSynergyRun(
                body: [new GridPoint(1, 1)],
                direction: Direction.Right,
                food: new GridPoint(4, 3),
                config: FullPortfolioConfig(),
                powerPickup: new PowerPickup(PowerKind.LastStand, new GridPoint(2, 1), 4),
                lastStandHeld: true));
    }

    [Fact]
    public void Full_portfolio_round_trips_canonical_state()
    {
        var run = CreateSynergyRun(
            body: [new GridPoint(1, 1), new GridPoint(2, 1), new GridPoint(3, 1)],
            direction: Direction.Right,
            food: new GridPoint(6, 1),
            config: FullPortfolioConfig(width: 10, height: 4),
            shieldTicksRemaining: 5,
            phaseShiftTicksRemaining: 4,
            lastStandHeld: true,
            lastStandRecoveryTicksRemaining: 2,
            slowMoTicksRemaining: 3,
            boostTicksRemaining: 3,
            magnetTicksRemaining: 4,
            gluttonyTicksRemaining: 2,
            baitPosition: new GridPoint(5, 2),
            detachedObstacles: [new GridPoint(0, 0), new GridPoint(0, 1)],
            detachedObstacleTicksRemaining: 6);

        var restored = SnakeRun.RestoreCanonicalState(run.SerializeCanonicalState());

        Assert.Equal(run.ComputeStateHash(), restored.ComputeStateHash());
        Assert.Equal(run.ShieldTicksRemaining, restored.ShieldTicksRemaining);
        Assert.Equal(run.PhaseShiftTicksRemaining, restored.PhaseShiftTicksRemaining);
        Assert.Equal(run.LastStandHeld, restored.LastStandHeld);
        Assert.Equal(run.LastStandRecoveryTicksRemaining, restored.LastStandRecoveryTicksRemaining);
        Assert.Equal(run.SlowMoTicksRemaining, restored.SlowMoTicksRemaining);
        Assert.Equal(run.BoostTicksRemaining, restored.BoostTicksRemaining);
        Assert.Equal(run.MagnetTicksRemaining, restored.MagnetTicksRemaining);
        Assert.Equal(run.GluttonyTicksRemaining, restored.GluttonyTicksRemaining);
        Assert.Equal(run.BaitPosition, restored.BaitPosition);
        Assert.Equal(run.DetachedObstacles, restored.DetachedObstacles);
        Assert.Equal(run.DetachedObstacleTicksRemaining, restored.DetachedObstacleTicksRemaining);
        Assert.Equal(run.MovementCadenceNumerator, restored.MovementCadenceNumerator);
        Assert.Equal(run.MovementCadenceDenominator, restored.MovementCadenceDenominator);
    }

    [Fact]
    public void Restart_clears_full_power_portfolio()
    {
        var run = CreateSynergyRun(
            body: [new GridPoint(1, 1)],
            direction: Direction.Right,
            food: new GridPoint(4, 3),
            config: FullPortfolioConfig(),
            hungerTicksRemaining: 1,
            shieldTicksRemaining: 5,
            phaseShiftTicksRemaining: 4,
            lastStandHeld: false,
            slowMoTicksRemaining: 3,
            boostTicksRemaining: 3,
            magnetTicksRemaining: 2,
            gluttonyTicksRemaining: 2,
            baitPosition: new GridPoint(0, 0),
            detachedObstacles: [new GridPoint(0, 1)],
            detachedObstacleTicksRemaining: 3);

        // Starvation ends the run while other powers remain non-zero in the terminal snapshot path.
        run.Step();
        Assert.Equal(RunStatus.Dead, run.Status);
        Assert.Equal(DeathCause.Starvation, run.DeathCause);

        var restarted = run.Restart(91UL).GetSnapshot();

        Assert.False(restarted.HasShield);
        Assert.False(restarted.HasPhaseShift);
        Assert.False(restarted.LastStandHeld);
        Assert.Equal(0, restarted.LastStandRecoveryTicksRemaining);
        Assert.False(restarted.HasSlowMo);
        Assert.False(restarted.HasBoost);
        Assert.False(restarted.HasMagnet);
        Assert.False(restarted.HasGluttony);
        Assert.Null(restarted.BaitPosition);
        Assert.Empty(restarted.DetachedObstacles);
        Assert.Equal(0, restarted.DetachedObstacleTicksRemaining);
        Assert.Null(restarted.PowerPickup);
        Assert.Equal(1, restarted.MovementCadenceNumerator);
        Assert.Equal(1, restarted.MovementCadenceDenominator);
    }

    [Fact]
    public void Multi_power_campaign_survives_restore_continue_and_restart()
    {
        var initial = CreateSynergyRun(
            body: [new GridPoint(1, 1)],
            direction: Direction.Right,
            food: new GridPoint(6, 1),
            config: FullPortfolioConfig(width: 10, height: 4),
            shieldTicksRemaining: 6,
            phaseShiftTicksRemaining: 5,
            lastStandHeld: true,
            slowMoTicksRemaining: 4,
            boostTicksRemaining: 4,
            magnetTicksRemaining: 4,
            gluttonyTicksRemaining: 4);

        initial.Step();
        var midHash = initial.ComputeStateHash();
        var midState = initial.SerializeCanonicalState();

        var continued = SnakeRun.RestoreCanonicalState(midState);
        Assert.Equal(midHash, continued.ComputeStateHash());

        continued.Step();
        Assert.Equal(RunStatus.Running, continued.Status);
        Assert.True(continued.HasShield);
        Assert.True(continued.HasPhaseShift);
        Assert.True(continued.LastStandHeld);
        Assert.True(continued.HasSlowMo);
        Assert.True(continued.HasBoost);

        // Force terminal via collision without protection (expire protection first by reconstruction).
        var terminal = CreateSynergyRun(
            body: LoopBody(),
            direction: Direction.Down,
            food: new GridPoint(4, 3),
            config: FullPortfolioConfig());
        terminal.Step();
        Assert.Equal(RunStatus.Dead, terminal.Status);

        var restarted = terminal.Restart(77UL);
        Assert.Equal(RunStatus.Running, restarted.Status);
        Assert.False(restarted.HasShield);
        Assert.False(restarted.HasPhaseShift);
        Assert.False(restarted.LastStandHeld);
        Assert.False(restarted.HasSlowMo);
        Assert.False(restarted.HasBoost);
        Assert.False(restarted.HasMagnet);
        Assert.False(restarted.HasGluttony);
    }

    private static GridPoint[] LoopBody() =>
    [
        new GridPoint(1, 1),
        new GridPoint(1, 2),
        new GridPoint(2, 2),
        new GridPoint(2, 1),
    ];

    private static RunConfig FullPortfolioConfig(int width = 5, int height = 4) =>
        new(
            Width: width,
            Height: height,
            StarvationTicks: 100,
            PowerSpawnIntervalTicks: 0,
            PowerVisibleTicks: 4,
            ShieldDurationTicks: 6,
            PhaseShiftDurationTicks: 5,
            LastStandRecoveryTicks: 3,
            SlowMoDurationTicks: 6,
            BoostDurationTicks: 6,
            MagnetDurationTicks: 6,
            GluttonyDurationTicks: 6,
            SegmentDetachObstacleTicks: 8,
            SegmentDetachMaxSegments: 5);

    private static SnakeRun CreateSynergyRun(
        IEnumerable<GridPoint> body,
        Direction direction,
        GridPoint? food,
        RunConfig? config = null,
        int hungerTicksRemaining = 100,
        PowerPickup? powerPickup = null,
        int shieldTicksRemaining = 0,
        int phaseShiftTicksRemaining = 0,
        bool lastStandHeld = false,
        int lastStandRecoveryTicksRemaining = 0,
        int slowMoTicksRemaining = 0,
        int boostTicksRemaining = 0,
        int magnetTicksRemaining = 0,
        int gluttonyTicksRemaining = 0,
        GridPoint? baitPosition = null,
        IEnumerable<GridPoint>? detachedObstacles = null,
        int detachedObstacleTicksRemaining = 0) =>
        SnakeRun.CreateForTesting(
            config ?? FullPortfolioConfig(),
            body,
            direction,
            food,
            hungerTicksRemaining,
            powerPickup: powerPickup,
            shieldTicksRemaining: shieldTicksRemaining,
            phaseShiftTicksRemaining: phaseShiftTicksRemaining,
            lastStandHeld: lastStandHeld,
            lastStandRecoveryTicksRemaining: lastStandRecoveryTicksRemaining,
            slowMoTicksRemaining: slowMoTicksRemaining,
            boostTicksRemaining: boostTicksRemaining,
            magnetTicksRemaining: magnetTicksRemaining,
            gluttonyTicksRemaining: gluttonyTicksRemaining,
            baitPosition: baitPosition,
            detachedObstacles: detachedObstacles,
            detachedObstacleTicksRemaining: detachedObstacleTicksRemaining);
}
