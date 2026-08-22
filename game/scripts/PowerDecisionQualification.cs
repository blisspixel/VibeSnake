using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using VibeSnake.Persistence;
using VibeSnake.Rules;

namespace VibeSnake.Game;

internal sealed record PowerDecisionQualificationEvidence(
    int SchemaVersion,
    string Kind,
    bool Passed,
    string ContractSha256,
    string PolicyId,
    int PowerCount,
    int FamilyCount,
    int LifecycleStageCount,
    int SynergyScenarioCount,
    int LocalSummarySchemaVersion,
    int DeathAdjacencyWindowTicks,
    bool CatalogExact,
    bool ContractExact,
    bool ProductVibeEnabled,
    bool ClassicAndCompatibilityDisabled,
    bool ConfigIdentitySeparated,
    bool AllNineAutomaticOffersReachable,
    bool AutomaticOffersDeterministic,
    bool ProtectionRedundancySuppressed,
    bool TempoRedundancySuppressed,
    bool HarvestSynergiesRetained,
    bool GeometryRedundancySuppressed,
    bool OfferPrecedesCollection,
    bool TypeFamilyAndVisibilityReadableBesideActiveState,
    bool AllHeldAndDurationStatesReadable,
    bool LifecycleTraceComplete,
    bool LocalSummaryAggregateOnly,
    string HumanScenarioStatus,
    string MutationForkStatus,
    bool MutationForkPrototypeGated,
    bool MutationForkEnabled,
    IReadOnlyList<PowerDecisionDefinition> Powers,
    IReadOnlyList<PowerSynergyScenario> Scenarios)
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.KebabCaseLower) },
    };

    public string Serialize() => JsonSerializer.Serialize(this, SerializerOptions) + "\n";
}

internal static class PowerDecisionQualification
{
    public static PowerDecisionQualificationEvidence Run(string contractPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contractPath);
        var contractBytes = File.ReadAllBytes(contractPath);
        var contractSha256 = Convert.ToHexString(SHA256.HashData(contractBytes))
            .ToLowerInvariant();
        using var contract = JsonDocument.Parse(contractBytes);
        var root = contract.RootElement;

        var catalogExact = PowerDecisionCatalog.All.Count == 9
            && PowerDecisionCatalog.All.Select(item => item.Kind)
                .SequenceEqual(Enum.GetValues<PowerKind>())
            && PowerDecisionCatalog.All.Select(item => item.Id)
                .Distinct(StringComparer.Ordinal).Count() == 9
            && PowerDecisionCatalog.All.Select(item => item.Family).Distinct().Count() == 4
            && PowerDecisionCatalog.All.All(item =>
                !string.IsNullOrWhiteSpace(item.IntendedQuestion)
                && !string.IsNullOrWhiteSpace(item.OfferTelegraph));
        var contractExact = ValidateContract(root);

        var vibe = RunModeCatalog.CreateConfig(RunModeCatalog.Vibe, enableAdaptation: false);
        var classic = RunModeCatalog.CreateConfig(RunModeCatalog.Classic);
        var compatibility = new RunConfig();
        var productVibeEnabled = vibe.EnablePowerDecisionOffers
            && vibe.AvoidFoodGeodesicPowerOffers;
        var classicAndCompatibilityDisabled = !classic.EnablePowerDecisionOffers
            && !compatibility.EnablePowerDecisionOffers
            && !classic.AvoidFoodGeodesicPowerOffers
            && !compatibility.AvoidFoodGeodesicPowerOffers;
        var configIdentitySeparated = vibe.ComputeConfigHash()
            != (vibe with { EnablePowerDecisionOffers = false }).ComputeConfigHash()
            && vibe.ComputeConfigHash()
            != (vibe with { AvoidFoodGeodesicPowerOffers = false }).ComputeConfigHash();

        var automaticKinds = new HashSet<PowerKind>();
        var automaticOffersDeterministic = true;
        var geodesicOffersOffPath = true;
        var spawnConfig = vibe with
        {
            // Avoid double-antipode layouts where every cell can lie on a
            // shortest wrap path and the documented fallback is required.
            Width = 9,
            Height = 7,
            StarvationTicks = 100,
            StarvationWarningTicks = 0,
            PowerSpawnIntervalTicks = 1,
            PowerVisibleTicks = 4,
        };
        for (ulong seed = 1; seed <= 512 && automaticKinds.Count < 9; seed++)
        {
            var first = SnakeRun.Create(seed, spawnConfig);
            var second = SnakeRun.Create(seed, spawnConfig);
            var reservedDestination = first.Head
                .Add(first.Direction.Offset())
                .Wrap(spawnConfig.Width, spawnConfig.Height);
            var foodBeforeStep = first.Food;
            var firstResult = first.Step();
            var secondResult = second.Step();
            automaticOffersDeterministic &= firstResult == secondResult
                && first.PowerPickup == second.PowerPickup;
            if (first.PowerPickup is { } pickup)
            {
                automaticKinds.Add(pickup.Kind);
                if (
                    foodBeforeStep is { } food
                    && GridPoint.LiesOnWrapManhattanGeodesic(
                        reservedDestination,
                        pickup.Position,
                        food,
                        spawnConfig.Width,
                        spawnConfig.Height))
                {
                    geodesicOffersOffPath = false;
                }
            }
        }

        var allNineAutomaticOffersReachable = automaticKinds.SetEquals(
            Enum.GetValues<PowerKind>());
        var protectionEligible = PowerDecisionCatalog.EligibleOffers([PowerKind.Shield]);
        var protectionRedundancySuppressed = protectionEligible.All(kind =>
            PowerDecisionCatalog.Get(kind).Family != PowerTacticalFamily.Protection);
        var tempoEligible = PowerDecisionCatalog.EligibleOffers([PowerKind.SlowMo]);
        var tempoRedundancySuppressed = !tempoEligible.Contains(PowerKind.SlowMo)
            && !tempoEligible.Contains(PowerKind.Boost);
        var harvestEligible = PowerDecisionCatalog.EligibleOffers([PowerKind.Magnet]);
        var harvestSynergiesRetained = !harvestEligible.Contains(PowerKind.Magnet)
            && harvestEligible.Contains(PowerKind.Bait)
            && harvestEligible.Contains(PowerKind.Gluttony);
        var geometryEligible = PowerDecisionCatalog.EligibleOffers([PowerKind.SegmentDetach]);
        var geometryRedundancySuppressed = !geometryEligible.Contains(PowerKind.SegmentDetach)
            && geometryEligible.Contains(PowerKind.Shield);

        var preCollection = SnakeRun.CreateForTesting(
            spawnConfig,
            [new GridPoint(1, 1)],
            Direction.Right,
            new GridPoint(7, 5),
            hungerTicksRemaining: 100,
            randomState: 1UL,
            randomIncrement: 109UL);
        var preCollectionResult = preCollection.Step();
        var offerPrecedesCollection = preCollection.PowerPickup is { } preCollectionPickup
            && preCollectionPickup.Position != preCollection.Head
            && preCollectionResult.OrderedEvents.Any(item =>
                item.Kind == RunEventKind.PowerSpawned
                && item.Power == preCollectionPickup.Kind)
            && preCollectionResult.OrderedEvents.All(item =>
                item.Kind != RunEventKind.PowerCollected);

        var readableSnapshot = Snapshot(
            tick: 1,
            direction: Direction.Right,
            head: new GridPoint(1, 1),
            pickup: new PowerPickup(PowerKind.Boost, new GridPoint(4, 2), 4),
            shieldTicks: 8,
            phaseTicks: 7,
            lastStandHeld: true,
            recoveryTicks: 6,
            slowMoTicks: 5,
            boostTicks: 4,
            magnetTicks: 3,
            gluttonyTicks: 2,
            bait: new GridPoint(5, 4),
            obstacles: [new GridPoint(0, 0)],
            obstacleTicks: 9);
        var readableStatus = PowerPresentation.DescribeStatus(readableSnapshot);
        var typeFamilyAndVisibilityReadableBesideActiveState =
            readableStatus.Contains("OFFER TEMPO [B] BOOST 0.2s", StringComparison.Ordinal)
            && readableStatus.Contains("[S] SHIELD 0.4s", StringComparison.Ordinal);
        string[] stateTokens =
        [
            "[S] SHIELD", "[P] PHASE", "[L] LAST STAND HELD",
            "RECOVERY IMMUNITY", "[W] SLOW-MO", "[B] BOOST", "[M] MAGNET",
            "[G] GLUTTONY", "[T] BAIT ARMED", "[D] DETACH x1", "CADENCE 2/2",
        ];
        var allHeldAndDurationStatesReadable = stateTokens.All(token =>
            readableStatus.Contains(token, StringComparison.Ordinal))
            && PowerFeedbackCatalog.Find(PowerKind.LastStand).StatePresentation == "held coil"
            && PowerFeedbackCatalog.Find(PowerKind.LastStand).PickupTelegraph == "HELD COIL READY";
        var boostOnlyStatus = PowerPresentation.DescribeStatus(
            Snapshot(
                tick: 1,
                direction: Direction.Right,
                head: new GridPoint(1, 1),
                shieldTicks: 8,
                boostTicks: 4));
        var slowOnlyStatus = PowerPresentation.DescribeStatus(
            Snapshot(
                tick: 1,
                direction: Direction.Right,
                head: new GridPoint(1, 1),
                shieldTicks: 8,
                slowMoTicks: 5));
        var cadenceAwareDurations =
            boostOnlyStatus.Contains("[S] SHIELD 0.2s", StringComparison.Ordinal)
            && boostOnlyStatus.Contains("[B] BOOST 0.1s", StringComparison.Ordinal)
            && boostOnlyStatus.Contains("CADENCE 1/2", StringComparison.Ordinal)
            && slowOnlyStatus.Contains("[S] SHIELD 0.8s", StringComparison.Ordinal)
            && slowOnlyStatus.Contains("[W] SLOW-MO 0.5s", StringComparison.Ordinal)
            && slowOnlyStatus.Contains("CADENCE 2/1", StringComparison.Ordinal);

        var trace = BuildCompleteTrace();
        var traceCounts = trace.Snapshot();
        var lifecycleTraceComplete = new[]
        {
            traceCounts.Sum(item => item.Offered),
            traceCounts.Sum(item => item.DetoursObserved),
            traceCounts.Sum(item => item.Collected),
            traceCounts.Sum(item => item.Activated),
            traceCounts.Sum(item => item.Expired),
            traceCounts.Sum(item => item.Consumed),
            traceCounts.Sum(item => item.Saved),
            traceCounts.Sum(item => item.DeathAdjacent),
        }.All(count => count > 0);
        var localRows = traceCounts.Select(counts => new LocalPowerDecisionSummary(
            PowerDecisionCatalog.Get(counts.Kind).Id,
            counts.Offered,
            counts.DetoursObserved,
            counts.Collected,
            counts.Activated,
            counts.Expired,
            counts.Consumed,
            counts.Saved,
            counts.DeathAdjacent)).ToArray();
        var localSummaryAggregateOnly = localRows.Length == 9
            && LocalPlaytestSummaryDocument.CurrentSchemaVersion == 2
            && localRows.All(row =>
            {
                row.Validate();
                return true;
            });

        var mutationForkEnabled = PowerDecisionCatalog.MutationFork.EnabledByDefault;
        var mutationForkOff = PowerDecisionCatalog.CreateMutationFork(false, [], 0, 0);
        var mutationForkOn = PowerDecisionCatalog.CreateMutationFork(true, [], 3, 5);
        var mutationForkPrototypeGated = mutationForkOff is null
            && mutationForkOn is { } fork
            && fork.First != fork.Second
            && fork.Resolve(fork.First).Withdrawn == fork.Second;
        var passed = catalogExact
            && contractExact
            && productVibeEnabled
            && classicAndCompatibilityDisabled
            && configIdentitySeparated
            && allNineAutomaticOffersReachable
            && automaticOffersDeterministic
            && geodesicOffersOffPath
            && protectionRedundancySuppressed
            && tempoRedundancySuppressed
            && harvestSynergiesRetained
            && geometryRedundancySuppressed
            && offerPrecedesCollection
            && typeFamilyAndVisibilityReadableBesideActiveState
            && allHeldAndDurationStatesReadable
            && cadenceAwareDurations
            && lifecycleTraceComplete
            && localSummaryAggregateOnly
            && mutationForkPrototypeGated
            && !mutationForkEnabled;
        if (!passed)
        {
            throw new InvalidOperationException("Power-decision qualification failed.");
        }

        return new PowerDecisionQualificationEvidence(
            SchemaVersion: 1,
            Kind: "power-decision-qualification-v1",
            Passed: true,
            ContractSha256: contractSha256,
            PolicyId: PowerDecisionCatalog.PolicyId,
            PowerCount: PowerDecisionCatalog.All.Count,
            FamilyCount: 4,
            LifecycleStageCount: PowerDecisionCatalog.RequiredLifecycleStages.Count,
            SynergyScenarioCount: PowerDecisionCatalog.RequiredSynergyScenarios.Count,
            LocalSummarySchemaVersion: LocalPlaytestSummaryDocument.CurrentSchemaVersion,
            DeathAdjacencyWindowTicks: PowerDecisionRunTrace.DeathAdjacencyWindowTicks,
            CatalogExact: catalogExact,
            ContractExact: contractExact,
            ProductVibeEnabled: productVibeEnabled,
            ClassicAndCompatibilityDisabled: classicAndCompatibilityDisabled,
            ConfigIdentitySeparated: configIdentitySeparated,
            AllNineAutomaticOffersReachable: allNineAutomaticOffersReachable,
            AutomaticOffersDeterministic: automaticOffersDeterministic,
            ProtectionRedundancySuppressed: protectionRedundancySuppressed,
            TempoRedundancySuppressed: tempoRedundancySuppressed,
            HarvestSynergiesRetained: harvestSynergiesRetained,
            GeometryRedundancySuppressed: geometryRedundancySuppressed,
            OfferPrecedesCollection: offerPrecedesCollection,
            TypeFamilyAndVisibilityReadableBesideActiveState:
                typeFamilyAndVisibilityReadableBesideActiveState,
            AllHeldAndDurationStatesReadable: allHeldAndDurationStatesReadable,
            LifecycleTraceComplete: lifecycleTraceComplete,
            LocalSummaryAggregateOnly: localSummaryAggregateOnly,
            HumanScenarioStatus: "pending-zero-sessions",
            MutationForkStatus: PowerDecisionCatalog.MutationFork.EvidenceStatus,
            MutationForkPrototypeGated: mutationForkPrototypeGated,
            MutationForkEnabled: mutationForkEnabled,
            Powers: PowerDecisionCatalog.All,
            Scenarios: PowerDecisionCatalog.RequiredSynergyScenarios);
    }

    private static bool ValidateContract(JsonElement root)
    {
        var familyIds = root.GetProperty("families").EnumerateArray()
            .Select(item => item.GetProperty("id").GetString())
            .ToArray();
        var lifecycle = root.GetProperty("lifecycleStages").EnumerateArray()
            .Select(item => item.GetString())
            .ToArray();
        var scenarios = root.GetProperty("requiredScenarios").EnumerateArray().ToArray();
        var mutation = root.GetProperty("mutationFork");
        var localSummary = root.GetProperty("localSummary");
        return root.GetProperty("schemaVersion").GetInt32() == 1
            && root.GetProperty("kind").GetString() == "vibesnake-power-decision-contract-v1"
            && root.GetProperty("policyId").GetString() == PowerDecisionCatalog.PolicyId
            && !root.GetProperty("compatibilityDefaultEnabled").GetBoolean()
            && root.GetProperty("productVibeEnabled").GetBoolean()
            && familyIds.SequenceEqual(["protection", "tempo", "harvest", "geometry"])
            && lifecycle.SequenceEqual(
            [
                "offered", "detour-observed", "collected", "activated",
                "expired", "consumed", "saved", "death-adjacent",
            ])
            && scenarios.Length == 6
            && scenarios.Select(item => item.GetProperty("id").GetString())
                .SequenceEqual(PowerDecisionCatalog.RequiredSynergyScenarios.Select(item => item.Id))
            && scenarios.All(item =>
                item.GetProperty("automatedStatus").GetString() == "required"
                && item.GetProperty("humanStatus").GetString() == "pending")
            && localSummary.GetProperty("schemaVersion").GetInt32() == 2
            && localSummary.GetProperty("aggregateOnly").GetBoolean()
            && !localSummary.GetProperty("rawInputRetained").GetBoolean()
            && !localSummary.GetProperty("wallClockInputTimingRetained").GetBoolean()
            && localSummary.GetProperty("deathAdjacencyWindowTicks").GetInt32()
                == PowerDecisionRunTrace.DeathAdjacencyWindowTicks
            && mutation.GetProperty("id").GetString() == PowerDecisionCatalog.MutationFork.Id
            && mutation.GetProperty("choiceCount").GetInt32()
                == PowerDecisionCatalog.MutationFork.ChoiceCount
            && mutation.GetProperty("withdrawUnchosenOffer").GetBoolean()
            && !mutation.GetProperty("enabledByDefault").GetBoolean()
            && mutation.GetProperty("decision").GetString() == "unapproved";
    }

    private static PowerDecisionRunTrace BuildCompleteTrace()
    {
        var trace = new PowerDecisionRunTrace();
        var before = Snapshot(0, Direction.Right, new GridPoint(1, 1));
        var shieldPickup = new PowerPickup(PowerKind.Shield, new GridPoint(2, 2), 4);
        var after = Snapshot(1, Direction.Right, new GridPoint(2, 1), shieldPickup);
        trace.Observe(before, after,
        [
            new RunEventDetail(
                RunEventKind.PowerSpawned,
                shieldPickup.Position,
                Value: 4,
                Power: PowerKind.Shield),
        ]);

        before = after;
        after = Snapshot(2, Direction.Down, new GridPoint(2, 2), shieldPickup);
        trace.Observe(before, after, [new RunEventDetail(RunEventKind.DirectionChanged)]);
        before = after;
        after = Snapshot(3, Direction.Right, new GridPoint(3, 2));
        trace.Observe(before, after,
        [
            new RunEventDetail(RunEventKind.PowerCollected, Power: PowerKind.Shield),
            new RunEventDetail(RunEventKind.PowerActivated, Power: PowerKind.Shield),
        ]);
        before = after;
        after = Snapshot(4, Direction.Right, new GridPoint(4, 2));
        trace.Observe(before, after,
        [
            new RunEventDetail(RunEventKind.PowerConsumed, Power: PowerKind.Shield),
            new RunEventDetail(RunEventKind.CollisionPrevented, Power: PowerKind.Shield),
        ]);
        before = after;
        var boostPickup = new PowerPickup(PowerKind.Boost, new GridPoint(6, 3), 2);
        after = Snapshot(5, Direction.Right, new GridPoint(5, 2), boostPickup);
        trace.Observe(before, after,
        [
            new RunEventDetail(
                RunEventKind.PowerSpawned,
                boostPickup.Position,
                Value: 2,
                Power: PowerKind.Boost),
        ]);
        before = after;
        after = Snapshot(6, Direction.Right, new GridPoint(6, 2));
        trace.Observe(before, after,
        [
            new RunEventDetail(RunEventKind.PowerExpired, Power: PowerKind.Boost),
        ]);
        before = after;
        after = Snapshot(7, Direction.Right, new GridPoint(7, 2), status: RunStatus.Dead);
        trace.Observe(before, after, [new RunEventDetail(RunEventKind.Died)]);
        return trace;
    }

    private static RunSnapshot Snapshot(
        int tick,
        Direction direction,
        GridPoint head,
        PowerPickup? pickup = null,
        int shieldTicks = 0,
        int phaseTicks = 0,
        bool lastStandHeld = false,
        int recoveryTicks = 0,
        int slowMoTicks = 0,
        int boostTicks = 0,
        int magnetTicks = 0,
        int gluttonyTicks = 0,
        GridPoint? bait = null,
        IReadOnlyList<GridPoint>? obstacles = null,
        int obstacleTicks = 0,
        RunStatus status = RunStatus.Running) =>
        new(
            Tick: tick,
            Status: status,
            DeathCause: status == RunStatus.Dead
                ? DeathCause.SelfCollision
                : DeathCause.None,
            Direction: direction,
            Body: [head],
            PendingDirections: [],
            Food: new GridPoint(7, 5),
            Score: 0,
            ComboCount: 0,
            ComboMultiplier: 1.0,
            TicksSinceLastFood: tick,
            HungerTicksRemaining: 100,
            HungerMaximumTicks: 100,
            HungerWarningTicks: 20,
            PowerPickup: pickup,
            PowerSpawnTicksElapsed: 0,
            ShieldTicksRemaining: shieldTicks,
            PhaseShiftTicksRemaining: phaseTicks,
            LastStandHeld: lastStandHeld,
            LastStandRecoveryTicksRemaining: recoveryTicks,
            SlowMoTicksRemaining: slowMoTicks,
            BoostTicksRemaining: boostTicks,
            MagnetTicksRemaining: magnetTicks,
            GluttonyTicksRemaining: gluttonyTicks,
            BaitPosition: bait,
            DetachedObstacles: obstacles ?? [],
            DetachedObstacleTicksRemaining: obstacleTicks,
            StateHash: "0000000000000000");
}
