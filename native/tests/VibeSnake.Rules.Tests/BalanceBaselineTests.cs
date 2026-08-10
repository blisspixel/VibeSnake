using System.Text.Json;

namespace VibeSnake.Rules.Tests;

public sealed class BalanceBaselineTests
{
    private const int MaximumStepsPerRun = 900;

    [Fact]
    public void Fixed_seed_policy_matrix_matches_the_reviewed_observed_baseline()
    {
        var repositoryRoot = BalanceLaboratoryReport.ResolveRepositoryRoot();
        var (seeds, seedCorpusHash) = BalanceBaselineReport.ReadSeeds(repositoryRoot);
        var expected = BalanceBaselineReport.ReadExpectedBaseline(repositoryRoot);
        var variants = CreateVariants();
        var runs = new List<BalanceBaselineRunSummary>(
            variants.Count * BalancePolicyCatalog.All.Count * seeds.Count);

        foreach (var variant in variants)
        {
            foreach (var policy in BalancePolicyCatalog.All)
            {
                foreach (var seed in seeds)
                {
                    runs.Add(RunOne(variant, policy, seed));
                }
            }
        }

        var distributions = BuildDistributions(variants, runs);
        var observedJson = BalanceBaselineReport.SerializeDistributions(distributions);
        var observedHash = BalanceBaselineReport.ComputeSha256(observedJson);
        var baselineMatched = distributions.Count == 27
            && observedHash == expected.ObservedDistributionSha256;
        var passed = baselineMatched
            && runs.Count == 2_700
            && runs.All(run => run.FinalStateHash.Length == 16)
            && distributions.All(distribution => distribution.SampleCount == 100);
        var evidence = new BalanceBaselineEvidence(
            SchemaVersion: BalanceBaselineReport.SchemaVersion,
            Kind: BalanceBaselineReport.Kind,
            Passed: passed,
            Classification: BalanceBaselineReport.Classification,
            AiSimulationOnly: true,
            HumanTargetRangesEstablished: false,
            HumanTargetRanges: Array.Empty<object>(),
            RulesetId: SnakeRun.RulesetId,
            RulesVersion: SnakeRun.RulesVersion,
            ConfigHashAlgorithm: RunConfig.ConfigHashAlgorithmId,
            StateHashAlgorithm: SnakeRun.StateHashAlgorithmId,
            SeedCorpusKind: BalanceBaselineReport.SeedCorpusKind,
            SeedCorpusSha256: seedCorpusHash,
            SeedCorpusReviewed: true,
            SeedCount: seeds.Count,
            MaximumStepsPerRun: MaximumStepsPerRun,
            VariantCount: variants.Count,
            PolicyCount: BalancePolicyCatalog.All.Count,
            ReferenceAiPolicyCount: BalancePolicyCatalog.All.Count(policy => policy.IsReferenceAi),
            SampleCountPerPair: seeds.Count,
            RunCount: runs.Count,
            BaselineDocumentSha256: expected.DocumentSha256,
            ObservedDistributionSha256: observedHash,
            BaselineMatched: baselineMatched,
            Variants: variants.Select(ToEvidence).ToArray(),
            Policies: BalancePolicyCatalog.All,
            Distributions: distributions,
            RunSummaries: runs,
            Notes:
            [
                "These are descriptive AI simulation observations, not human balance targets.",
                "All nine laboratory policies run for coverage; six are classified as reference AI.",
                "Human target ranges remain empty until structured playtest evidence is reviewed.",
            ]);
        var path = BalanceBaselineReport.Write(repositoryRoot, evidence);

        Assert.True(File.Exists(path));
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        Assert.Equal(BalanceBaselineReport.Kind, document.RootElement.GetProperty("kind").GetString());
        Assert.True(document.RootElement.GetProperty("passed").GetBoolean());
        Assert.True(baselineMatched);
        Assert.True(passed);
    }

    private static BalanceBaselineRunSummary RunOne(
        BalanceBaselineVariant variant,
        BalancePolicyDefinition policy,
        ulong seed)
    {
        var run = SnakeRun.Create(seed, variant.Configuration);
        var controllerSeed = seed
            ^ ((ulong)policy.Kind << 48)
            ^ 0xBA5E1A1CUL;
        var controller = new BalancePolicyController(policy.Kind, controllerSeed);
        var maximumLength = run.Body.Count;
        var powerEncounters = 0;
        var powerPickups = 0;
        var powerActivations = 0;
        var steps = 0;

        while (steps < MaximumStepsPerRun && run.Status == RunStatus.Running)
        {
            foreach (var command in controller.SelectCommands(run))
            {
                run.QueueDirection(command);
            }

            var result = run.Step();
            steps++;
            maximumLength = Math.Max(maximumLength, run.Body.Count);
            powerEncounters += result.OrderedEvents.Count(
                item => item.Kind == RunEventKind.PowerSpawned);
            powerPickups += result.OrderedEvents.Count(
                item => item.Kind == RunEventKind.PowerCollected);
            powerActivations += result.OrderedEvents.Count(
                item => item.Kind == RunEventKind.PowerActivated);
        }

        return new BalanceBaselineRunSummary(
            variant.Id,
            policy.Id,
            seed,
            steps,
            Outcome(run.Status),
            Death(run.DeathCause),
            run.Score,
            run.Body.Count,
            maximumLength,
            run.SessionFoodEaten,
            run.SessionMaxCombo,
            powerEncounters,
            powerPickups,
            powerActivations,
            run.ComputeStateHash());
    }

    private static IReadOnlyList<ObservedBalanceDistribution> BuildDistributions(
        IReadOnlyList<BalanceBaselineVariant> variants,
        IReadOnlyList<BalanceBaselineRunSummary> runs) =>
        variants.SelectMany(variant => BalancePolicyCatalog.All.Select(policy =>
        {
            var group = runs.Where(run =>
                run.VariantId == variant.Id && run.PolicyId == policy.Id).ToArray();
            var totalSteps = group.Sum(run => (long)run.SurvivalSteps);
            var foodRate = totalSteps == 0
                ? 0m
                : Math.Round(
                    group.Sum(run => (decimal)run.FoodEaten) * 1_000m / totalSteps,
                    3,
                    MidpointRounding.AwayFromZero);
            return new ObservedBalanceDistribution(
                variant.Id,
                policy.Id,
                group.Length,
                Minimum(group, run => run.Score),
                Percentile(group, run => run.Score, 0.50),
                Percentile(group, run => run.Score, 0.95),
                Percentile(group, run => run.Score, 0.99),
                Maximum(group, run => run.Score),
                Minimum(group, run => run.SurvivalSteps),
                Percentile(group, run => run.SurvivalSteps, 0.50),
                Percentile(group, run => run.SurvivalSteps, 0.95),
                Percentile(group, run => run.SurvivalSteps, 0.99),
                Maximum(group, run => run.SurvivalSteps),
                Percentile(group, run => run.FinalLength, 0.50),
                Percentile(group, run => run.FinalLength, 0.95),
                Maximum(group, run => run.FinalLength),
                Percentile(group, run => run.MaximumLength, 0.50),
                Percentile(group, run => run.MaximumLength, 0.95),
                Maximum(group, run => run.MaximumLength),
                group.Sum(run => run.FoodEaten),
                foodRate,
                Percentile(group, run => run.FoodEaten, 0.50),
                Percentile(group, run => run.FoodEaten, 0.95),
                Maximum(group, run => run.FoodEaten),
                Percentile(group, run => run.ComboPeak, 0.50),
                Percentile(group, run => run.ComboPeak, 0.95),
                Maximum(group, run => run.ComboPeak),
                group.Sum(run => run.PowerEncounters),
                group.Sum(run => run.PowerPickups),
                group.Sum(run => run.PowerActivations),
                group.Count(run => run.DeathCause == "starvation"),
                group.Count(run => run.DeathCause == "self-collision"),
                new Dictionary<string, int>(StringComparer.Ordinal)
                {
                    ["running-at-cap"] = group.Count(run => run.Outcome == "running-at-cap"),
                    ["dead"] = group.Count(run => run.Outcome == "dead"),
                    ["won"] = group.Count(run => run.Outcome == "won"),
                });
        })).ToArray();

    private static IReadOnlyList<BalanceBaselineVariant> CreateVariants() =>
    [
        new("classic", RunModeCatalog.CreateConfig(RunModeCatalog.Classic)),
        new("vibe-dda-on", RunModeCatalog.CreateConfig(RunModeCatalog.Vibe)),
        new(
            "vibe-dda-off",
            RunModeCatalog.CreateConfig(RunModeCatalog.Vibe, enableAdaptation: false)),
    ];

    private static BalanceBaselineVariantEvidence ToEvidence(BalanceBaselineVariant variant) =>
        new(
            variant.Id,
            $"{variant.Configuration.ModeId}@{variant.Configuration.ModeVersion}",
            RunModeCatalog.GetScoreCategoryId(variant.Configuration),
            RunModeCatalog.Get(
                variant.Configuration.ModeId,
                variant.Configuration.ModeVersion).DifficultyPolicyId,
            variant.Configuration.EnableAdaptation,
            variant.Configuration.ComputeConfigHash());

    private static string Outcome(RunStatus status) => status switch
    {
        RunStatus.Running => "running-at-cap",
        RunStatus.Dead => "dead",
        RunStatus.Won => "won",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
    };

    private static string Death(DeathCause cause) => cause switch
    {
        DeathCause.None => "none",
        DeathCause.SelfCollision => "self-collision",
        DeathCause.Starvation => "starvation",
        _ => throw new ArgumentOutOfRangeException(nameof(cause), cause, null),
    };

    private static int Minimum(
        IEnumerable<BalanceBaselineRunSummary> runs,
        Func<BalanceBaselineRunSummary, int> selector) => runs.Min(selector);

    private static int Maximum(
        IEnumerable<BalanceBaselineRunSummary> runs,
        Func<BalanceBaselineRunSummary, int> selector) => runs.Max(selector);

    private static int Percentile(
        IEnumerable<BalanceBaselineRunSummary> runs,
        Func<BalanceBaselineRunSummary, int> selector,
        double percentile)
    {
        var values = runs.Select(selector).Order().ToArray();
        var rank = Math.Clamp(
            (int)Math.Ceiling(percentile * values.Length) - 1,
            0,
            values.Length - 1);
        return values[rank];
    }

    private sealed record BalanceBaselineVariant(string Id, RunConfig Configuration);
}
