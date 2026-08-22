namespace VibeSnake.Rules.Tests;

public sealed class PowerSpawnGeodesicTests
{
    [Fact]
    public void Compatibility_config_omits_geodesic_avoidance_and_keeps_legacy_occupancy()
    {
        var config = new RunConfig(
            Width: 9,
            Height: 5,
            StarvationTicks: 100,
            PowerSpawnIntervalTicks: 1,
            PowerVisibleTicks: 4);

        Assert.False(config.AvoidFoodGeodesicPowerOffers);
        Assert.DoesNotContain(
            "avoidFoodGeodesicPowerOffers",
            config.SerializeCanonicalConfig(),
            StringComparison.Ordinal);
        Assert.Equal(
            new RunConfig(Width: 9, Height: 5, StarvationTicks: 100).ComputeConfigHash(),
            (config with { PowerSpawnIntervalTicks = 300, PowerVisibleTicks = 120 })
                .ComputeConfigHash());
    }

    [Fact]
    public void Enabled_geodesic_avoidance_round_trips_state_and_changes_config_identity()
    {
        var baseline = new RunConfig();
        var enabled = baseline with { AvoidFoodGeodesicPowerOffers = true };
        var run = SnakeRun.Create(70_011UL, enabled);

        var restored = SnakeRun.RestoreCanonicalState(run.SerializeCanonicalState());

        Assert.NotEqual(baseline.ComputeConfigHash(), enabled.ComputeConfigHash());
        Assert.Contains(
            "\"avoidFoodGeodesicPowerOffers\":true",
            enabled.SerializeCanonicalConfig(),
            StringComparison.Ordinal);
        Assert.True(restored.Configuration.AvoidFoodGeodesicPowerOffers);
        Assert.Equal(run.ConfigHash, restored.ConfigHash);
        Assert.Equal(run.ComputeStateHash(), restored.ComputeStateHash());
    }

    [Fact]
    public void Product_vibe_factory_avoids_the_food_geodesic_and_classic_does_not()
    {
        var vibe = RunModeCatalog.CreateConfig(RunModeCatalog.Vibe, enableAdaptation: false);
        var classic = RunModeCatalog.CreateConfig(RunModeCatalog.Classic);
        var compatibility = new RunConfig();

        Assert.True(vibe.AvoidFoodGeodesicPowerOffers);
        Assert.False(classic.AvoidFoodGeodesicPowerOffers);
        Assert.False(compatibility.AvoidFoodGeodesicPowerOffers);
        Assert.NotEqual(
            vibe.ComputeConfigHash(),
            (vibe with { AvoidFoodGeodesicPowerOffers = false }).ComputeConfigHash());
        Assert.Throws<ArgumentException>(
            () => (classic with { AvoidFoodGeodesicPowerOffers = true }).Validate());
    }

    [Fact]
    public void Enabled_spawn_never_lands_on_the_remaining_food_geodesic()
    {
        var config = CorridorConfig(avoidFoodGeodesic: true);
        for (ulong seed = 1; seed <= 64; seed++)
        {
            var (origin, food, pickup) = SpawnOnce(config, seed);
            Assert.NotNull(pickup);
            Assert.False(
                GridPoint.LiesOnWrapManhattanGeodesic(
                    origin,
                    pickup.Position,
                    food,
                    config.Width,
                    config.Height),
                $"Seed {seed} spawned on the food geodesic at {pickup.Position}.");
        }
    }

    [Fact]
    public void Compatibility_spawn_can_land_on_the_food_geodesic()
    {
        var config = CorridorConfig(avoidFoodGeodesic: false);
        var geodesicHits = 0;
        for (ulong seed = 1; seed <= 64; seed++)
        {
            var (origin, food, pickup) = SpawnOnce(config, seed);
            Assert.NotNull(pickup);
            if (GridPoint.LiesOnWrapManhattanGeodesic(
                origin,
                pickup.Position,
                food,
                config.Width,
                config.Height))
            {
                geodesicHits++;
            }
        }

        Assert.True(
            geodesicHits > 0,
            "Legacy occupancy must still be able to place a pickup on the food geodesic.");
    }

    [Fact]
    public void Saturated_geodesic_fallback_still_spawns_on_the_food_path()
    {
        var config = new RunConfig(
            Width: 4,
            Height: 3,
            StarvationTicks: 100,
            PowerSpawnIntervalTicks: 1,
            PowerVisibleTicks: 4,
            AvoidFoodGeodesicPowerOffers: true);
        var run = SnakeRun.CreateForTesting(
            config,
            [new GridPoint(0, 1)],
            Direction.Down,
            new GridPoint(2, 1),
            hungerTicksRemaining: 100,
            detachedObstacles:
            [
                new GridPoint(0, 0),
                new GridPoint(1, 0),
                new GridPoint(2, 0),
                new GridPoint(3, 0),
                new GridPoint(1, 2),
                new GridPoint(2, 2),
                new GridPoint(3, 2),
            ],
            detachedObstacleTicksRemaining: 10);

        run.Step();

        var pickup = Assert.IsType<PowerPickup>(run.PowerPickup);
        Assert.Contains(pickup.Position, new[] { new GridPoint(1, 1), new GridPoint(3, 1) });
        Assert.True(
            GridPoint.LiesOnWrapManhattanGeodesic(
                run.Head,
                pickup.Position,
                Assert.IsType<GridPoint>(run.Food),
                config.Width,
                config.Height));
    }

    [Fact]
    public void Enabled_spawn_is_deterministic_for_equal_seeds()
    {
        var config = CorridorConfig(avoidFoodGeodesic: true);
        var first = SpawnOnce(config, 17);
        var second = SpawnOnce(config, 17);

        Assert.Equal(first.Pickup, second.Pickup);
        Assert.Equal(first.Origin, second.Origin);
        Assert.Equal(first.Food, second.Food);
    }

    private static RunConfig CorridorConfig(bool avoidFoodGeodesic) => new(
        Width: 9,
        Height: 5,
        StarvationTicks: 100,
        PowerSpawnIntervalTicks: 1,
        PowerVisibleTicks: 4,
        AvoidFoodGeodesicPowerOffers: avoidFoodGeodesic);

    private static (GridPoint Origin, GridPoint Food, PowerPickup Pickup) SpawnOnce(
        RunConfig config,
        ulong seed)
    {
        var run = SnakeRun.CreateForTesting(
            config,
            [new GridPoint(1, 2)],
            Direction.Left,
            new GridPoint(5, 2),
            hungerTicksRemaining: 100,
            randomState: seed,
            randomIncrement: 109UL);
        var origin = run.Head.Add(run.Direction.Offset()).Wrap(config.Width, config.Height);
        var food = Assert.IsType<GridPoint>(run.Food);

        run.Step();

        return (origin, food, Assert.IsType<PowerPickup>(run.PowerPickup));
    }
}
