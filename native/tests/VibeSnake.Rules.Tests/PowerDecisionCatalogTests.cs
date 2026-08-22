namespace VibeSnake.Rules.Tests;

public sealed class PowerDecisionCatalogTests
{
    [Fact]
    public void Catalog_classifies_every_power_once_with_complete_decision_copy()
    {
        Assert.Equal(Enum.GetValues<PowerKind>(), PowerDecisionCatalog.All.Select(item => item.Kind));
        Assert.Equal(9, PowerDecisionCatalog.All.Count);
        Assert.Equal(4, PowerDecisionCatalog.All.Select(item => item.Family).Distinct().Count());
        Assert.Equal(9, PowerDecisionCatalog.All.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.All(PowerDecisionCatalog.All, item =>
        {
            Assert.Matches("^[a-z]+(?:-[a-z]+)*$", item.Id);
            Assert.False(string.IsNullOrWhiteSpace(item.IntendedQuestion));
            Assert.False(string.IsNullOrWhiteSpace(item.OfferTelegraph));
            Assert.True(Enum.IsDefined(item.StatePresentation));
        });

        Assert.Equal(
            [PowerKind.Shield, PowerKind.PhaseShift, PowerKind.LastStand],
            Family(PowerTacticalFamily.Protection));
        Assert.Equal(
            [PowerKind.SlowMo, PowerKind.Boost],
            Family(PowerTacticalFamily.Tempo));
        Assert.Equal(
            [PowerKind.Magnet, PowerKind.Bait, PowerKind.Gluttony],
            Family(PowerTacticalFamily.Harvest));
        Assert.Equal(
            [PowerKind.SegmentDetach],
            Family(PowerTacticalFamily.Geometry));
    }

    [Fact]
    public void Empty_state_makes_all_nine_offers_eligible_in_stable_order()
    {
        var eligible = PowerDecisionCatalog.EligibleOffers([]);

        Assert.Equal(Enum.GetValues<PowerKind>(), eligible);
    }

    [Theory]
    [InlineData(PowerKind.Shield)]
    [InlineData(PowerKind.PhaseShift)]
    [InlineData(PowerKind.LastStand)]
    public void Active_protection_suppresses_every_redundant_protection_offer(
        PowerKind activeProtection)
    {
        var eligible = PowerDecisionCatalog.EligibleOffers([activeProtection]);

        Assert.DoesNotContain(PowerKind.Shield, eligible);
        Assert.DoesNotContain(PowerKind.PhaseShift, eligible);
        Assert.DoesNotContain(PowerKind.LastStand, eligible);
        Assert.Contains(PowerKind.Boost, eligible);
        Assert.Contains(PowerKind.Magnet, eligible);
        Assert.Contains(PowerKind.SegmentDetach, eligible);
    }

    [Theory]
    [InlineData(PowerKind.SlowMo)]
    [InlineData(PowerKind.Boost)]
    public void Active_tempo_suppresses_same_and_opposing_tempo_offers(PowerKind activeTempo)
    {
        var eligible = PowerDecisionCatalog.EligibleOffers([activeTempo]);

        Assert.DoesNotContain(PowerKind.SlowMo, eligible);
        Assert.DoesNotContain(PowerKind.Boost, eligible);
        Assert.Contains(PowerKind.PhaseShift, eligible);
        Assert.Contains(PowerKind.Magnet, eligible);
    }

    [Fact]
    public void Harvest_synergies_remain_eligible_while_exact_duplicates_do_not()
    {
        var eligible = PowerDecisionCatalog.EligibleOffers(
            [PowerKind.Magnet, PowerKind.Gluttony]);

        Assert.DoesNotContain(PowerKind.Magnet, eligible);
        Assert.DoesNotContain(PowerKind.Gluttony, eligible);
        Assert.Contains(PowerKind.Bait, eligible);
        Assert.Contains(PowerKind.Boost, eligible);
        Assert.Contains(PowerKind.PhaseShift, eligible);
    }

    [Fact]
    public void Geometry_duplicate_is_suppressed_without_blocking_other_families()
    {
        var eligible = PowerDecisionCatalog.EligibleOffers([PowerKind.SegmentDetach]);

        Assert.DoesNotContain(PowerKind.SegmentDetach, eligible);
        Assert.Contains(PowerKind.Shield, eligible);
        Assert.Contains(PowerKind.SlowMo, eligible);
        Assert.Contains(PowerKind.Bait, eligible);
    }

    [Fact]
    public void Eligibility_rejects_unknown_kinds_and_null_inputs()
    {
        Assert.Throws<ArgumentNullException>(() => PowerDecisionCatalog.EligibleOffers(null!));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PowerDecisionCatalog.EligibleOffers([(PowerKind)byte.MaxValue]));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PowerDecisionCatalog.Get((PowerKind)byte.MaxValue));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PowerDecisionCatalog.IsEligibleOffer((PowerKind)byte.MaxValue, []));
    }

    [Fact]
    public void Lifecycle_synergy_and_mutation_fork_contracts_are_complete_and_gated()
    {
        Assert.Equal(Enum.GetValues<PowerLifecycleStage>(), PowerDecisionCatalog.RequiredLifecycleStages);
        Assert.Equal(8, PowerDecisionCatalog.RequiredLifecycleStages.Count);
        Assert.Equal(6, PowerDecisionCatalog.RequiredSynergyScenarios.Count);
        Assert.Equal(
            6,
            PowerDecisionCatalog.RequiredSynergyScenarios
                .Select(item => item.Id)
                .Distinct(StringComparer.Ordinal)
                .Count());
        Assert.All(PowerDecisionCatalog.RequiredSynergyScenarios, item =>
        {
            Assert.NotEmpty(item.Powers);
            Assert.False(string.IsNullOrWhiteSpace(item.Setup));
            Assert.False(string.IsNullOrWhiteSpace(item.ExpectedInteraction));
        });

        var experiment = PowerDecisionCatalog.MutationFork;
        Assert.Equal("mutation-fork-v1", experiment.Id);
        Assert.False(experiment.EnabledByDefault);
        Assert.Equal(2, experiment.ChoiceCount);
        Assert.True(experiment.WithdrawUnchosenOffer);
        Assert.Equal("automated-prototype-human-unverified", experiment.EvidenceStatus);
    }

    [Fact]
    public void Mutation_fork_prototype_is_default_off_deterministic_and_withdraws_the_other_choice()
    {
        Assert.Null(PowerDecisionCatalog.CreateMutationFork(
            experimentEnabled: false,
            activePowers: [],
            firstRoll: 0,
            secondRoll: 0));

        var first = Assert.IsType<MutationForkOffer>(PowerDecisionCatalog.CreateMutationFork(
            experimentEnabled: true,
            activePowers: [PowerKind.Shield, PowerKind.SlowMo],
            firstRoll: 7,
            secondRoll: 11));
        var second = Assert.IsType<MutationForkOffer>(PowerDecisionCatalog.CreateMutationFork(
            experimentEnabled: true,
            activePowers: [PowerKind.Shield, PowerKind.SlowMo],
            firstRoll: 7,
            secondRoll: 11));
        Assert.Equal(first, second);
        Assert.NotEqual(first.First, first.Second);
        Assert.DoesNotContain(first.First, new[]
        {
            PowerKind.Shield,
            PowerKind.PhaseShift,
            PowerKind.LastStand,
            PowerKind.SlowMo,
            PowerKind.Boost,
        });
        Assert.Equal(
            new MutationForkResolution(first.First, first.Second),
            first.Resolve(first.First));
        Assert.Equal(
            new MutationForkResolution(first.Second, first.First),
            first.Resolve(first.Second));
        Assert.Throws<ArgumentOutOfRangeException>(() => first.Resolve(PowerKind.Shield));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PowerDecisionCatalog.CreateMutationFork(true, [], -1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PowerDecisionCatalog.CreateMutationFork(true, [], 0, -1));

        var noPair = PowerDecisionCatalog.CreateMutationFork(
            experimentEnabled: true,
            activePowers:
            [
                PowerKind.Shield,
                PowerKind.SlowMo,
                PowerKind.Magnet,
                PowerKind.Bait,
                PowerKind.Gluttony,
                PowerKind.SegmentDetach,
            ],
            firstRoll: 0,
            secondRoll: 0);
        Assert.Null(noPair);
    }

    [Fact]
    public void Product_vibe_mode_uses_diversified_deterministic_offers()
    {
        var config = RunModeCatalog.CreateConfig(
            RunModeCatalog.Vibe,
            enableAdaptation: false) with
        {
            Width = 8,
            Height = 6,
            StarvationTicks = 100,
            StarvationWarningTicks = 0,
            PowerSpawnIntervalTicks = 1,
            PowerVisibleTicks = 4,
        };
        var observed = new HashSet<PowerKind>();

        for (ulong seed = 1; seed <= 512 && observed.Count < 9; seed++)
        {
            var first = SnakeRun.Create(seed, config);
            var second = SnakeRun.Create(seed, config);

            var firstResult = first.Step();
            var secondResult = second.Step();
            Assert.Equal(firstResult, secondResult);
            Assert.Equal(first.PowerPickup, second.PowerPickup);
            var offer = Assert.IsType<PowerPickup>(first.PowerPickup);
            observed.Add(offer.Kind);
        }

        Assert.Equal(Enum.GetValues<PowerKind>(), observed.OrderBy(kind => kind));
    }

    [Fact]
    public void Compatibility_config_retains_shield_only_offer_and_random_path()
    {
        var config = new RunConfig(
            Width: 5,
            Height: 4,
            StarvationTicks: 100,
            PowerSpawnIntervalTicks: 1,
            PowerVisibleTicks: 4);
        var run = SnakeRun.CreateForTesting(
            config,
            [new GridPoint(1, 1)],
            Direction.Right,
            new GridPoint(4, 3),
            hungerTicksRemaining: 100,
            randomState: 1UL,
            randomIncrement: 109UL);

        run.Step();

        Assert.False(config.EnablePowerDecisionOffers);
        Assert.False(config.AvoidFoodGeodesicPowerOffers);
        Assert.Equal(PowerKind.Shield, Assert.IsType<PowerPickup>(run.PowerPickup).Kind);
    }

    [Fact]
    public void Enabled_offer_policy_round_trips_state_and_changes_config_identity()
    {
        var baseline = new RunConfig();
        var enabled = baseline with { EnablePowerDecisionOffers = true };
        var run = SnakeRun.Create(70_009UL, enabled);

        var restored = SnakeRun.RestoreCanonicalState(run.SerializeCanonicalState());

        Assert.NotEqual(baseline.ComputeConfigHash(), enabled.ComputeConfigHash());
        Assert.Contains(
            "\"enablePowerDecisionOffers\":true",
            enabled.SerializeCanonicalConfig(),
            StringComparison.Ordinal);
        Assert.True(restored.Configuration.EnablePowerDecisionOffers);
        Assert.Equal(run.ConfigHash, restored.ConfigHash);
        Assert.Equal(run.ComputeStateHash(), restored.ComputeStateHash());
    }

    [Fact]
    public void Enabled_product_policy_rejects_redundant_restored_offers_but_keeps_synergies()
    {
        var config = new RunConfig(
            Width: 6,
            Height: 4,
            StarvationTicks: 100,
            PowerSpawnIntervalTicks: 0,
            PowerVisibleTicks: 4,
            EnablePowerDecisionOffers: true);

        Assert.Throws<ArgumentException>(() => SnakeRun.CreateForTesting(
            config,
            [new GridPoint(1, 1)],
            Direction.Right,
            new GridPoint(5, 3),
            hungerTicksRemaining: 100,
            powerPickup: new PowerPickup(PowerKind.PhaseShift, new GridPoint(3, 2), 4),
            shieldTicksRemaining: 2));
        Assert.Throws<ArgumentException>(() => SnakeRun.CreateForTesting(
            config,
            [new GridPoint(1, 1)],
            Direction.Right,
            new GridPoint(5, 3),
            hungerTicksRemaining: 100,
            powerPickup: new PowerPickup(PowerKind.SegmentDetach, new GridPoint(3, 2), 4),
            detachedObstacles: [new GridPoint(4, 2)],
            detachedObstacleTicksRemaining: 2));

        var synergy = SnakeRun.CreateForTesting(
            config,
            [new GridPoint(1, 1)],
            Direction.Right,
            new GridPoint(5, 3),
            hungerTicksRemaining: 100,
            powerPickup: new PowerPickup(PowerKind.Gluttony, new GridPoint(3, 2), 4),
            magnetTicksRemaining: 2);
        Assert.Equal(PowerKind.Gluttony, synergy.PowerPickup!.Kind);
    }

    private static PowerKind[] Family(PowerTacticalFamily family) =>
        PowerDecisionCatalog.All
            .Where(item => item.Family == family)
            .Select(item => item.Kind)
            .ToArray();
}
