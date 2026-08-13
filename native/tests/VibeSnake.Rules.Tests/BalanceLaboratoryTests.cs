using System.Text.Json;

namespace VibeSnake.Rules.Tests;

public sealed class BalanceLaboratoryTests
{
    private const int MaximumStepsPerRun = 384;

    [Fact]
    public void Balance_laboratory_runs_complete_policy_scenario_and_seed_matrix()
    {
        var repositoryRoot = BalanceLaboratoryReport.ResolveRepositoryRoot();
        var (corpora, corpusHash) = BalanceLaboratoryReport.ReadSeedCorpora(repositoryRoot);
        var seeds = corpora.SelectMany(corpus => corpus.Seeds).ToArray();
        var variants = CreateVariants();
        var traces = new List<BalanceRunTrace>();
        BalanceFirstDivergence? firstDivergence = null;
        var comparedSteps = 0;

        foreach (var variant in variants)
        {
            foreach (var policy in BalancePolicyCatalog.All)
            {
                foreach (var seed in seeds)
                {
                    var execution = RunOne(
                        variant.Id,
                        variant.Configuration,
                        policy,
                        seed);
                    traces.Add(execution.Trace);
                    comparedSteps += execution.ComparedSteps;
                    firstDivergence ??= execution.FirstDivergence;
                }
            }
        }

        var scenarios = RunScenarioMatrix();
        var distributions = BuildDistributions(traces);
        var outliers = WriteOutliers(repositoryRoot, traces);
        var divergence = new BalanceDivergenceEvidence(
            ComparedRunCount: traces.Count,
            ComparedStepCount: comparedSteps,
            Passed: firstDivergence is null,
            FirstDivergence: firstDivergence,
            ReproductionCommand: BalanceLaboratoryReport.ReproductionCommand);
        var expectedRunCount = variants.Count * BalancePolicyCatalog.All.Count * seeds.Length;
        var passed = firstDivergence is null
            && traces.Count == expectedRunCount
            && scenarios.Count == 10
            && scenarios.All(scenario => scenario.Passed)
            && distributions.Length == variants.Count * BalancePolicyCatalog.All.Count
            && outliers.Length >= 6
            && outliers.All(outlier => outlier.Verified)
            && traces.All(trace => trace.FinalStateHash.Length == 16);

        var evidence = new BalanceLaboratoryEvidence(
            SchemaVersion: BalanceLaboratoryReport.SchemaVersion,
            Kind: BalanceLaboratoryReport.Kind,
            Passed: passed,
            RulesetId: SnakeRun.RulesetId,
            RulesVersion: SnakeRun.RulesVersion,
            ConfigHashAlgorithm: RunConfig.ConfigHashAlgorithmId,
            StateHashAlgorithm: SnakeRun.StateHashAlgorithmId,
            SeedCorpusKind: BalanceLaboratoryReport.SeedCorpusKind,
            SeedCorpusSchemaVersion: BalanceLaboratoryReport.SeedCorpusSchemaVersion,
            SeedCorpusSha256: corpusHash,
            SeedCorpora: corpora,
            Policies: BalancePolicyCatalog.All,
            Variants: variants.Select(variant => variant.Id).ToArray(),
            MaximumStepsPerRun: MaximumStepsPerRun,
            RunCount: traces.Count,
            ComparedStepCount: comparedSteps,
            Scenarios: scenarios,
            Distributions: distributions,
            OutlierReplays: outliers,
            Divergence: divergence,
            RunSummaries: traces.Select(ToRunSummary).ToArray(),
            Notes:
            [
                "Policies are deterministic test instruments, not models of human behavior.",
                "Distribution evidence is descriptive until V070-04 establishes reviewed baselines.",
                "No human fairness, fun, tension, or fatigue claim is inferred.",
            ]);
        var path = BalanceLaboratoryReport.Write(repositoryRoot, evidence);

        Assert.True(File.Exists(path));
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        Assert.Equal("balance-laboratory-v1", root.GetProperty("kind").GetString());
        Assert.True(root.GetProperty("passed").GetBoolean());
        Assert.Equal(expectedRunCount, root.GetProperty("runCount").GetInt32());
        Assert.Equal(9, root.GetProperty("policies").GetArrayLength());
        Assert.Equal(10, root.GetProperty("scenarios").GetArrayLength());
        Assert.Equal(27, root.GetProperty("distributions").GetArrayLength());
        Assert.Null(firstDivergence);
        Assert.True(passed);
    }

    [Fact]
    public void Balance_laboratory_retains_a_synthetic_first_divergence_shape()
    {
        IReadOnlyList<IReadOnlyList<Direction>> prefix =
        [
            new[] { Direction.Up },
            new[] { Direction.Left, Direction.Down },
        ];
        var divergence = new BalanceFirstDivergence(
            VariantId: "vibe-dda-on",
            PolicyId: "input-chaos-v1",
            Seed: 42UL,
            Step: 2,
            ExpectedStateHash: "0123456789abcdef",
            ActualStateHash: "fedcba9876543210",
            CommandPrefix: prefix);

        Assert.Equal(2, divergence.Step);
        Assert.Equal(2, divergence.CommandPrefix.Count);
        Assert.NotEqual(divergence.ExpectedStateHash, divergence.ActualStateHash);
    }

    private static BalanceRunExecution RunOne(
        string variantId,
        RunConfig config,
        BalancePolicyDefinition policy,
        ulong seed)
    {
        var left = SnakeRun.Create(seed, config);
        var right = SnakeRun.Create(seed, config);
        var controllerSeed = seed
            ^ ((ulong)policy.Kind << 48)
            ^ (config.EnableAdaptation ? 0xDDA0DDA0UL : 0xF17ED0FFUL);
        var controller = new BalancePolicyController(policy.Kind, controllerSeed);
        var commands = new List<IReadOnlyList<Direction>>(MaximumStepsPerRun);
        var visited = new HashSet<GridPoint> { left.Head };
        var adaptiveStates = Enum.GetValues<AdaptiveDifficultyState>()
            .ToDictionary(state => state.ToString().ToLowerInvariant(), _ => 0);
        var directionTransitions = 0;
        var previousDirection = left.Direction;
        BalanceFirstDivergence? divergence = null;
        var comparedSteps = 0;

        for (var step = 0; step < MaximumStepsPerRun && left.Status == RunStatus.Running; step++)
        {
            var selected = controller.SelectCommands(left).ToArray();
            commands.Add(selected);
            foreach (var command in selected)
            {
                left.QueueDirection(command);
                right.QueueDirection(command);
            }

            var leftResult = left.Step();
            var rightResult = right.Step();
            comparedSteps++;
            if (leftResult.StateHash != rightResult.StateHash)
            {
                divergence = new BalanceFirstDivergence(
                    variantId,
                    policy.Id,
                    seed,
                    step + 1,
                    leftResult.StateHash,
                    rightResult.StateHash,
                    commands.Select(item => (IReadOnlyList<Direction>)item.ToArray()).ToArray());
                break;
            }

            if (left.Direction != previousDirection)
            {
                directionTransitions++;
                previousDirection = left.Direction;
            }

            visited.Add(left.Head);
            var adaptiveKey = left.AdaptiveDifficulty.State.ToString().ToLowerInvariant();
            adaptiveStates[adaptiveKey]++;
        }

        var trace = new BalanceRunTrace(
            VariantId: variantId,
            Configuration: config,
            PolicyId: policy.Id,
            PolicyKind: policy.Kind,
            Seed: seed,
            Steps: commands.Count,
            Status: left.Status,
            DeathCause: left.DeathCause,
            Score: left.Score,
            FoodEaten: left.SessionFoodEaten,
            MaximumCombo: left.SessionMaxCombo,
            NearMisses: left.SessionNearMisses,
            PowerCollections: left.SessionPowerupsCollected,
            Wraps: left.SessionWraps,
            UniqueRouteCells: visited.Count,
            DirectionTransitions: directionTransitions,
            FinalStateHash: left.ComputeStateHash(),
            AdaptiveStateSteps: adaptiveStates,
            Commands: commands);
        return new BalanceRunExecution(trace, comparedSteps, divergence);
    }

    private static BalanceDistribution[] BuildDistributions(
        IReadOnlyList<BalanceRunTrace> traces) =>
        traces
            .GroupBy(trace => (trace.VariantId, trace.PolicyId))
            .OrderBy(group => group.Key.VariantId, StringComparer.Ordinal)
            .ThenBy(group => group.Key.PolicyId, StringComparer.Ordinal)
            .Select(group =>
            {
                var runs = group.ToArray();
                return new BalanceDistribution(
                    group.Key.VariantId,
                    group.Key.PolicyId,
                    runs.Length,
                    Minimum(runs, run => run.Steps),
                    Percentile(runs, run => run.Steps, 0.50),
                    Percentile(runs, run => run.Steps, 0.95),
                    Percentile(runs, run => run.Steps, 0.99),
                    Maximum(runs, run => run.Steps),
                    Minimum(runs, run => run.Score),
                    Percentile(runs, run => run.Score, 0.50),
                    Percentile(runs, run => run.Score, 0.95),
                    Percentile(runs, run => run.Score, 0.99),
                    Maximum(runs, run => run.Score),
                    Maximum(runs, run => run.FoodEaten),
                    Maximum(runs, run => run.MaximumCombo),
                    Maximum(runs, run => run.NearMisses),
                    Maximum(runs, run => run.PowerCollections),
                    Maximum(runs, run => run.Wraps),
                    Maximum(runs, run => run.UniqueRouteCells),
                    runs.GroupBy(run => run.DeathCause.ToString().ToLowerInvariant())
                        .ToDictionary(item => item.Key, item => item.Count(), StringComparer.Ordinal));
            })
            .ToArray();

    private static BalanceOutlierReplay[] WriteOutliers(
        string repositoryRoot,
        IReadOnlyList<BalanceRunTrace> traces)
    {
        var candidates = new (string Reason, BalanceRunTrace Trace)[]
        {
            ("highest-score", traces.MaxBy(trace => trace.Score)!),
            ("longest-survival", traces.MaxBy(trace => trace.Steps)!),
            ("most-near-misses", traces.MaxBy(trace => trace.NearMisses)!),
            ("most-power-collections", traces.MaxBy(trace => trace.PowerCollections)!),
            ("most-wraps", traces.MaxBy(trace => trace.Wraps)!),
            ("highest-combo", traces.MaxBy(trace => trace.MaximumCombo)!),
            ("shortest-terminal", traces
                .Where(trace => trace.Status != RunStatus.Running)
                .MinBy(trace => trace.Steps)!),
        };

        return candidates
            .DistinctBy(candidate => (
                candidate.Trace.VariantId,
                candidate.Trace.PolicyId,
                candidate.Trace.Seed))
            .Select(candidate => BalanceLaboratoryReport.WriteOutlierReplay(
                repositoryRoot,
                candidate.Trace,
                candidate.Reason))
            .ToArray();
    }

    private static IReadOnlyList<BalanceScenarioEvidence> RunScenarioMatrix() =>
    [
        ProbeOpenBoardRouting(),
        ProbeLongBodyTrap(),
        ProbeStarvationPressure(),
        ProbePowerOverlap(),
        ProbeLastStandRecovery(),
        ProbeDetachedObstacle(),
        ProbeNearMissScoring(),
        ProbeComboEscalation(),
        ProbeFullGridResolution(),
        ProbeRestartLeaks(),
    ];

    private static BalanceScenarioEvidence ProbeOpenBoardRouting()
    {
        var config = RunModeCatalog.CreateConfig(RunModeCatalog.Vibe, false) with
        {
            Width = 8,
            Height = 6,
            PowerSpawnIntervalTicks = 0,
        };
        var run = SnakeRun.CreateForTesting(
            config,
            [new GridPoint(2, 2)],
            Direction.Right,
            new GridPoint(4, 2),
            hungerTicksRemaining: 100);
        var controller = new BalancePolicyController(BalancePolicyKind.GreedyFood, 1UL);
        StepWithPolicy(run, controller);
        StepWithPolicy(run, controller);
        return Scenario(
            "open-board-routing",
            run.SessionFoodEaten == 1 && run.Status == RunStatus.Running,
            run,
            "Greedy policy reaches visible food on an open wrapped board.");
    }

    private static BalanceScenarioEvidence ProbeLongBodyTrap()
    {
        var config = RunModeCatalog.CreateConfig(RunModeCatalog.Classic) with
        {
            Width = 6,
            Height = 6,
        };
        var run = SnakeRun.CreateForTesting(
            config,
            [
                new GridPoint(1, 1),
                new GridPoint(1, 2),
                new GridPoint(2, 2),
                new GridPoint(3, 2),
                new GridPoint(3, 1),
                new GridPoint(2, 1),
            ],
            Direction.Left,
            new GridPoint(5, 5),
            hungerTicksRemaining: 1);
        StepWithPolicy(
            run,
            new BalancePolicyController(BalancePolicyKind.SafeSurvivor, 2UL));
        return Scenario(
            "long-body-trap",
            run.Status == RunStatus.Running && run.Head == new GridPoint(2, 0),
            run,
            "Safe policy selects the only open exit from a body trap.");
    }

    private static BalanceScenarioEvidence ProbeStarvationPressure()
    {
        var config = RunModeCatalog.CreateConfig(RunModeCatalog.Vibe);
        var support = AdaptiveDifficultyPolicy.Evaluate(config, 1, 0, 100);
        var pressure = AdaptiveDifficultyPolicy.Evaluate(config, 4, 10, 300);
        var run = SnakeRun.Create(3UL, config);
        return Scenario(
            "starvation-pressure",
            support is { State: AdaptiveDifficultyState.Support, HungerDrainTicks: 0 }
                && pressure is { State: AdaptiveDifficultyState.Pressure, HungerDrainTicks: 2 },
            run,
            "Exact DDA support and pressure bounds are reachable.");
    }

    private static BalanceScenarioEvidence ProbePowerOverlap()
    {
        var config = RunModeCatalog.CreateConfig(RunModeCatalog.Vibe, false) with
        {
            Width = 8,
            Height = 6,
            PowerSpawnIntervalTicks = 0,
        };
        var run = SnakeRun.CreateForTesting(
            config,
            [new GridPoint(2, 2)],
            Direction.Right,
            new GridPoint(7, 5),
            hungerTicksRemaining: 100,
            slowMoTicksRemaining: 5,
            boostTicksRemaining: 5);
        var before = run.GetSnapshot();
        run.Step();
        return Scenario(
            "power-overlap",
            before.HasSlowMo
                && before.HasBoost
                && before.MovementCadenceNumerator == 2
                && before.MovementCadenceDenominator == 2
                && run.SlowMoTicksRemaining == 4
                && run.BoostTicksRemaining == 4,
            run,
            "Slow-Mo and Boost compose and advance exactly once.");
    }

    private static BalanceScenarioEvidence ProbeLastStandRecovery()
    {
        var config = RunModeCatalog.CreateConfig(RunModeCatalog.Vibe, false) with
        {
            Width = 6,
            Height = 6,
            PowerSpawnIntervalTicks = 0,
        };
        var run = SnakeRun.CreateForTesting(
            config,
            [
                new GridPoint(1, 1),
                new GridPoint(1, 2),
                new GridPoint(2, 2),
                new GridPoint(2, 1),
                new GridPoint(3, 1),
            ],
            Direction.Left,
            new GridPoint(5, 5),
            hungerTicksRemaining: 100,
            lastStandHeld: true);
        var result = run.Step();
        return Scenario(
            "last-stand-recovery",
            run.Status == RunStatus.Running
                && run.HasLastStandRecovery
                && result.Events.HasFlag(RunEvent.CollisionPrevented)
                && result.Events.HasFlag(RunEvent.PowerConsumed),
            run,
            "Held Last Stand converts collision into bounded recovery.");
    }

    private static BalanceScenarioEvidence ProbeDetachedObstacle()
    {
        var config = RunModeCatalog.CreateConfig(RunModeCatalog.Vibe, false) with
        {
            Width = 8,
            Height = 6,
            PowerSpawnIntervalTicks = 0,
        };
        var run = SnakeRun.CreateForTesting(
            config,
            [new GridPoint(2, 2)],
            Direction.Right,
            new GridPoint(7, 5),
            hungerTicksRemaining: 100,
            detachedObstacles: [new GridPoint(3, 2)],
            detachedObstacleTicksRemaining: 10);
        StepWithPolicy(
            run,
            new BalancePolicyController(BalancePolicyKind.SafeSurvivor, 4UL));
        return Scenario(
            "detached-obstacle",
            run.Status == RunStatus.Running && run.Head != new GridPoint(3, 2),
            run,
            "Safe policy routes around a live detached obstacle.");
    }

    private static BalanceScenarioEvidence ProbeNearMissScoring()
    {
        var config = RunModeCatalog.CreateConfig(RunModeCatalog.Vibe, false) with
        {
            Width = 16,
            Height = 10,
            PowerSpawnIntervalTicks = 0,
        };
        var run = SnakeRun.CreateForTesting(
            config,
            [new GridPoint(5, 5), new GridPoint(6, 5), new GridPoint(7, 5)],
            Direction.Right,
            new GridPoint(8, 5),
            hungerTicksRemaining: 10);
        var result = run.Step();
        return Scenario(
            "near-miss-scoring",
            run.SessionNearMisses == 1
                && result.Events.HasFlag(RunEvent.NearMiss)
                && run.Score > config.FoodScore,
            run,
            "Critical-hunger food awards one typed near miss.");
    }

    private static BalanceScenarioEvidence ProbeComboEscalation()
    {
        var config = RunModeCatalog.CreateConfig(RunModeCatalog.Vibe, false) with
        {
            Width = 16,
            Height = 10,
            PowerSpawnIntervalTicks = 0,
        };
        var run = SnakeRun.CreateForTesting(
            config,
            [new GridPoint(5, 5), new GridPoint(6, 5), new GridPoint(7, 5)],
            Direction.Right,
            new GridPoint(8, 5),
            hungerTicksRemaining: 500,
            comboCount: 9,
            ticksSinceLastFood: 0);
        run.Step();
        return Scenario(
            "combo-escalation",
            run.ComboCount == 10 && run.SessionMaxCombo == 10 && run.Score > config.FoodScore,
            run,
            "A tenth consecutive food crosses the pressure combo threshold.");
    }

    private static BalanceScenarioEvidence ProbeFullGridResolution()
    {
        var config = new RunConfig(Width: 2, Height: 2);
        var run = SnakeRun.CreateForTesting(
            config,
            [new GridPoint(0, 0), new GridPoint(0, 1), new GridPoint(1, 1)],
            Direction.Up,
            new GridPoint(1, 0),
            hungerTicksRemaining: 100);
        var result = run.Step();
        return Scenario(
            "full-grid-resolution",
            run.Status == RunStatus.Won
                && run.Food is null
                && result.Events.HasFlag(RunEvent.Won),
            run,
            "Eating the last free cell produces one deterministic win.");
    }

    private static BalanceScenarioEvidence ProbeRestartLeaks()
    {
        var config = RunModeCatalog.CreateConfig(RunModeCatalog.Vibe, false) with
        {
            Width = 8,
            Height = 6,
            PowerSpawnIntervalTicks = 0,
        };
        var terminal = SnakeRun.CreateForTesting(
            config,
            [
                new GridPoint(1, 1),
                new GridPoint(1, 2),
                new GridPoint(2, 2),
                new GridPoint(2, 1),
            ],
            Direction.Down,
            new GridPoint(7, 5),
            hungerTicksRemaining: 100,
            slowMoTicksRemaining: 5,
            boostTicksRemaining: 5,
            magnetTicksRemaining: 5,
            gluttonyTicksRemaining: 5);
        terminal.Step();
        var restarted = terminal.Restart(10UL);
        return Scenario(
            "restart-leaks",
            terminal.Status == RunStatus.Dead
                && restarted.Status == RunStatus.Running
                && restarted.ConfigHash == terminal.ConfigHash
                && !restarted.HasSlowMo
                && !restarted.HasBoost
                && !restarted.HasMagnet
                && !restarted.HasGluttony
                && restarted.SessionFoodEaten == 0
                && restarted.SessionNearMisses == 0,
            restarted,
            "Restart retains config and fresh seed while clearing every transient.");
    }

    private static void StepWithPolicy(SnakeRun run, BalancePolicyController controller)
    {
        foreach (var command in controller.SelectCommands(run))
        {
            run.QueueDirection(command);
        }

        run.Step();
    }

    private static BalanceScenarioEvidence Scenario(
        string id,
        bool passed,
        SnakeRun run,
        string detail) =>
        new(id, passed, run.ComputeStateHash(), detail);

    private static IReadOnlyList<BalanceVariant> CreateVariants() =>
    [
        new("classic", RunModeCatalog.CreateConfig(RunModeCatalog.Classic)),
        new("vibe-dda-on", RunModeCatalog.CreateConfig(RunModeCatalog.Vibe)),
        new(
            "vibe-dda-off",
            RunModeCatalog.CreateConfig(RunModeCatalog.Vibe, enableAdaptation: false)),
    ];

    private static object ToRunSummary(BalanceRunTrace trace) => new
    {
        trace.VariantId,
        trace.PolicyId,
        trace.Seed,
        trace.Steps,
        trace.Status,
        trace.DeathCause,
        trace.Score,
        trace.FoodEaten,
        trace.MaximumCombo,
        trace.NearMisses,
        trace.PowerCollections,
        trace.Wraps,
        trace.UniqueRouteCells,
        trace.DirectionTransitions,
        trace.FinalStateHash,
        trace.AdaptiveStateSteps,
    };

    private static int Minimum(
        IEnumerable<BalanceRunTrace> runs,
        Func<BalanceRunTrace, int> selector) => runs.Min(selector);

    private static int Maximum(
        IEnumerable<BalanceRunTrace> runs,
        Func<BalanceRunTrace, int> selector) => runs.Max(selector);

    private static int Percentile(
        IEnumerable<BalanceRunTrace> runs,
        Func<BalanceRunTrace, int> selector,
        double percentile)
    {
        var values = runs.Select(selector).Order().ToArray();
        var rank = Math.Clamp((int)Math.Ceiling(percentile * values.Length) - 1, 0, values.Length - 1);
        return values[rank];
    }

    private sealed record BalanceVariant(string Id, RunConfig Configuration);

    private sealed record BalanceRunExecution(
        BalanceRunTrace Trace,
        int ComparedSteps,
        BalanceFirstDivergence? FirstDivergence);
}
