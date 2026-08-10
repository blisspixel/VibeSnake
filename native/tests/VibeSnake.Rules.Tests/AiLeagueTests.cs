using System.Security.Cryptography;
using System.Text.Json;

namespace VibeSnake.Rules.Tests;

public sealed class AiLeagueTests
{
    private const int MaximumStepsPerRun = 900;

    [Fact]
    public void Native_ai_league_measures_every_personality_on_the_reviewed_corpus()
    {
        var repositoryRoot = BalanceLaboratoryReport.ResolveRepositoryRoot();
        var (corpora, corpusHash) = BalanceLaboratoryReport.ReadSeedCorpora(repositoryRoot);
        var config = RunModeCatalog.CreateConfig(RunModeCatalog.Vibe);
        var executions = new List<AiLeagueExecution>();

        for (var personalityIndex = 0;
             personalityIndex < AiPersonalityCatalog.BuiltIn.Count;
             personalityIndex++)
        {
            var personality = AiPersonalityCatalog.BuiltIn[personalityIndex];
            foreach (var corpus in corpora)
            {
                foreach (var seed in corpus.Seeds)
                {
                    executions.Add(RunOne(corpus.Id, personalityIndex, personality, seed, config));
                }
            }
        }

        var runs = executions.Select(execution => execution.Run).ToArray();
        var distributions = BuildDistributions(runs);
        var sensitivities = BuildSensitivities(executions);
        var inertTraits = sensitivities
            .Where(sensitivity => !sensitivity.MateriallyAffectedDecisions)
            .ToArray();
        var isolation = new AiLeaderboardIsolation(
            ScoreRunContextCatalog.Ai.RunKindId,
            ScoreRunContextCatalog.Ai.SeedCategoryId,
            ScoreRunContextCatalog.Ai.DisplayCategoryId,
            ScoreRunContextCatalog.Ai.CompetitiveEligible,
            WritesHumanScoreStorage: false,
            Enforcement:
                "The league references only VibeSnake.Rules and emits test evidence; "
                + "the AI score context is noncompetitive and no persistence store is constructed.");
        var expectedRunCount = AiPersonalityCatalog.BuiltIn.Count
            * corpora.Sum(corpus => corpus.Seeds.Count);
        var comparedSteps = executions.Sum(execution => execution.ComparedSteps);
        var passed = corpora.All(corpus => corpus.Reviewed)
            && runs.Length == expectedRunCount
            && distributions.Count == AiPersonalityCatalog.BuiltIn.Count
            && sensitivities.Count == AiPersonalityCatalog.BuiltIn.Count
                * Enum.GetValues<AiPersonalityTrait>().Length
            && inertTraits.Length == 0
            && executions.All(execution => execution.Deterministic)
            && runs.All(run => run.DecisionCount == run.Steps)
            && runs.All(run => run.DecisionTraceSha256.Length == 64)
            && !isolation.CompetitiveEligible
            && !isolation.WritesHumanScoreStorage;
        var evidence = new AiLeagueEvidence(
            AiLeagueReport.SchemaVersion,
            AiLeagueReport.Kind,
            passed,
            AiPersonalityController.AlgorithmId,
            Pcg32.AlgorithmId,
            SnakeRun.RulesetId,
            SnakeRun.RulesVersion,
            BalanceLaboratoryReport.SeedCorpusKind,
            BalanceLaboratoryReport.SeedCorpusSchemaVersion,
            corpusHash,
            corpora,
            MaximumStepsPerRun,
            AiPersonalityCatalog.BuiltIn.Count,
            runs.Length,
            comparedSteps,
            MetricDefinitions,
            AiPersonalityCatalog.BuiltIn,
            distributions,
            sensitivities,
            inertTraits,
            isolation,
            runs,
            [
                "Results describe deterministic AI behavior and do not model human skill.",
                "Survival at the step cap is right-censored rather than treated as a death.",
                "Trait sensitivity uses an opposite-extreme intervention on identical observed states and random samples.",
                "Every built-in trait intervention changes at least one percent of observed decisions after the V08003 truthfulness pass.",
            ]);
        var path = AiLeagueReport.Write(repositoryRoot, evidence);

        Assert.True(File.Exists(path));
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        Assert.Equal(AiLeagueReport.Kind, root.GetProperty("kind").GetString());
        Assert.True(root.GetProperty("passed").GetBoolean());
        Assert.Equal(10, root.GetProperty("personalityCount").GetInt32());
        Assert.Equal(120, root.GetProperty("runCount").GetInt32());
        Assert.Equal(10, root.GetProperty("distributions").GetArrayLength());
        Assert.Equal(60, root.GetProperty("traitSensitivities").GetArrayLength());
        Assert.Equal(0, root.GetProperty("inertTraits").GetArrayLength());
        Assert.False(root.GetProperty("leaderboardIsolation")
            .GetProperty("competitiveEligible").GetBoolean());
        Assert.True(passed);
    }

    internal static AiReviewedQualification RunReviewedQualification()
    {
        var repositoryRoot = BalanceLaboratoryReport.ResolveRepositoryRoot();
        var (corpora, _) = BalanceLaboratoryReport.ReadSeedCorpora(repositoryRoot);
        var config = RunModeCatalog.CreateConfig(RunModeCatalog.Vibe);
        var executions = new List<AiLeagueExecution>();
        for (var personalityIndex = 0;
             personalityIndex < AiPersonalityCatalog.BuiltIn.Count;
             personalityIndex++)
        {
            var personality = AiPersonalityCatalog.BuiltIn[personalityIndex];
            foreach (var corpus in corpora)
            {
                foreach (var seed in corpus.Seeds)
                {
                    executions.Add(RunOne(corpus.Id, personalityIndex, personality, seed, config));
                }
            }
        }

        var runs = executions.Select(execution => execution.Run).ToArray();
        return new AiReviewedQualification(
            BuildDistributions(runs),
            BuildSensitivities(executions),
            executions.Sum(execution => execution.ComparedSteps));
    }

    private static AiLeagueExecution RunOne(
        string corpusId,
        int personalityIndex,
        AiPersonality personality,
        ulong seed,
        RunConfig config)
    {
        var controllerSeed = seed
            ^ ((ulong)(personalityIndex + 1) << 48)
            ^ 0xA180_0200_0000_0001UL;
        var left = SnakeRun.Create(seed, config);
        var right = SnakeRun.Create(seed, config);
        var leftController = new AiPersonalityController(personality, controllerSeed);
        var rightController = new AiPersonalityController(personality, controllerSeed);
        var counterfactuals = Enum.GetValues<AiPersonalityTrait>()
            .ToDictionary(
                trait => trait,
                trait => new CounterfactualController(
                    InterventionValue(personality.GetTrait(trait)),
                    new AiPersonalityController(
                        personality.WithTrait(
                            trait,
                            InterventionValue(personality.GetTrait(trait))),
                        controllerSeed)));
        var directions = new List<byte>(MaximumStepsPerRun);
        var powerOpportunities = 0;
        var powerTargets = 0;
        var riskExposures = 0;
        var deadEnds = 0;
        var routeOpportunities = 0;
        var efficientRoutes = 0;
        var deterministic = true;
        var comparedSteps = 0;

        while (left.Status == RunStatus.Running && left.Tick < MaximumStepsPerRun)
        {
            var leftDecision = leftController.SelectDecision(left);
            var rightDecision = rightController.SelectDecision(right);
            deterministic &= leftDecision == rightDecision;
            foreach (var (trait, counterfactual) in counterfactuals)
            {
                var decision = counterfactual.Controller.SelectDecision(left);
                counterfactual.Observe(
                    decision.Direction != leftDecision.Direction
                    || decision.TargetKind != leftDecision.TargetKind);
            }

            directions.Add((byte)leftDecision.Direction);
            if (left.PowerPickup is not null)
            {
                powerOpportunities++;
                if (leftDecision.TargetKind == AiTargetKind.Power)
                {
                    powerTargets++;
                }
            }

            riskExposures += leftDecision.HazardNeighborCount > 0 ? 1 : 0;
            deadEnds += leftDecision.EnteredDeadEnd ? 1 : 0;
            if (leftDecision.Target is not null)
            {
                routeOpportunities++;
                efficientRoutes += leftDecision.ReducedTargetDistance ? 1 : 0;
            }

            left.QueueDirection(leftDecision.Direction);
            right.QueueDirection(rightDecision.Direction);
            var leftResult = left.Step();
            var rightResult = right.Step();
            comparedSteps++;
            deterministic &= leftResult.StateHash == rightResult.StateHash;
        }

        deterministic &= left.ComputeStateHash() == right.ComputeStateHash();
        var scoreIdentity = RunScoreIdentity.FromRun(left, ScoreRunContextCatalog.Ai);
        var run = new AiLeagueRun(
            corpusId,
            personality.Id,
            seed,
            SnakeRun.RulesVersion,
            scoreIdentity.RunKindId,
            scoreIdentity.SeedCategoryId,
            scoreIdentity.DisplayCategoryId,
            scoreIdentity.CompetitiveEligible,
            scoreIdentity.ScoreCategoryId,
            left.Tick,
            left.Status,
            left.DeathCause,
            left.Score,
            left.SessionFoodEaten,
            left.SessionPowerupsCollected,
            directions.Count,
            powerOpportunities,
            powerTargets,
            riskExposures,
            deadEnds,
            routeOpportunities,
            efficientRoutes,
            Convert.ToHexString(SHA256.HashData(directions.ToArray())).ToLowerInvariant(),
            left.ComputeStateHash());
        return new AiLeagueExecution(run, comparedSteps, deterministic, counterfactuals);
    }

    private static IReadOnlyList<AiLeagueDistribution> BuildDistributions(
        IReadOnlyList<AiLeagueRun> runs) =>
        runs
            .GroupBy(run => (run.PersonalityId, run.RulesVersion))
            .OrderBy(group => group.Key.PersonalityId, StringComparer.Ordinal)
            .ThenBy(group => group.Key.RulesVersion)
            .Select(group =>
            {
                var samples = group.ToArray();
                return new AiLeagueDistribution(
                    group.Key.PersonalityId,
                    group.Key.RulesVersion,
                    samples.Length,
                    samples.Min(run => run.Steps),
                    Percentile(samples, run => run.Steps, 0.50),
                    Percentile(samples, run => run.Steps, 0.95),
                    samples.Max(run => run.Steps),
                    samples.Min(run => run.Score),
                    Percentile(samples, run => run.Score, 0.50),
                    Percentile(samples, run => run.Score, 0.95),
                    samples.Max(run => run.Score),
                    Rate(samples.Sum(run => run.FoodEaten), samples.Sum(run => run.Steps), 1_000),
                    Rate(samples.Sum(run => run.PowerTargetCount), samples.Sum(run => run.PowerOpportunityCount), 10_000),
                    Rate(samples.Sum(run => run.RiskExposureCount), samples.Sum(run => run.DecisionCount), 10_000),
                    Rate(samples.Sum(run => run.DeadEndCount), samples.Sum(run => run.DecisionCount), 10_000),
                    Rate(samples.Sum(run => run.EfficientRouteCount), samples.Sum(run => run.RouteOpportunityCount), 10_000),
                    samples.GroupBy(run => run.DeathCause.ToString().ToLowerInvariant())
                        .ToDictionary(item => item.Key, item => item.Count(), StringComparer.Ordinal));
            })
            .ToArray();

    private static IReadOnlyList<AiTraitSensitivity> BuildSensitivities(
        IReadOnlyList<AiLeagueExecution> executions) =>
        executions
            .SelectMany(execution => execution.Counterfactuals.Select(pair => new
            {
                execution.Run.PersonalityId,
                Trait = pair.Key,
                Controller = pair.Value,
            }))
            .GroupBy(item => (item.PersonalityId, item.Trait))
            .OrderBy(group => group.Key.PersonalityId, StringComparer.Ordinal)
            .ThenBy(group => group.Key.Trait)
            .Select(group =>
            {
                var observed = group.Sum(item => item.Controller.ObservedDecisions);
                var changed = group.Sum(item => item.Controller.ChangedDecisions);
                var basisPoints = Rate(changed, observed, 10_000);
                var personality = AiPersonalityCatalog.GetBuiltIn(group.Key.PersonalityId);
                return new AiTraitSensitivity(
                    group.Key.PersonalityId,
                    group.Key.Trait,
                    personality.GetTrait(group.Key.Trait),
                    group.First().Controller.InterventionValue,
                    observed,
                    changed,
                    basisPoints,
                    changed > 0 && basisPoints >= AiLeagueReport.MaterialSensitivityBasisPoints);
            })
            .ToArray();

    private static int InterventionValue(int baseline) => baseline < 50 ? 100 : 0;

    private static int Rate(int numerator, int denominator, int scale) =>
        denominator == 0
            ? 0
            : (int)Math.Round(
                numerator * (double)scale / denominator,
                MidpointRounding.AwayFromZero);

    private static int Percentile(
        IEnumerable<AiLeagueRun> runs,
        Func<AiLeagueRun, int> selector,
        double percentile)
    {
        var values = runs.Select(selector).Order().ToArray();
        var index = (int)Math.Ceiling(percentile * values.Length) - 1;
        return values[Math.Clamp(index, 0, values.Length - 1)];
    }

    private static IReadOnlyList<AiLeagueMetricDefinition> MetricDefinitions { get; } =
    [
        new("score", "rules points", "Final deterministic rules score."),
        new("survival", "steps", "Rules steps survived, capped and right-censored at 900."),
        new("food-efficiency", "food per 1,000 steps", "Food collected divided by survival steps."),
        new("power-preference", "basis points", "Power-target decisions divided by decisions with a visible power."),
        new("risk-exposure", "basis points", "Moves ending adjacent to an additional body or detached-obstacle hazard, excluding the expected trailing neck."),
        new("dead-end-rate", "basis points", "Moves leaving at most one non-reversing safe onward choice."),
        new("route-efficiency", "basis points", "Targeted moves that reduce wrapped Manhattan distance to the selected target."),
    ];

    private sealed class CounterfactualController(
        int interventionValue,
        AiPersonalityController controller)
    {
        public int InterventionValue { get; } = interventionValue;

        public AiPersonalityController Controller { get; } = controller;

        public int ObservedDecisions { get; private set; }

        public int ChangedDecisions { get; private set; }

        public void Observe(bool changed)
        {
            ObservedDecisions++;
            ChangedDecisions += changed ? 1 : 0;
        }
    }

    private sealed record AiLeagueExecution(
        AiLeagueRun Run,
        int ComparedSteps,
        bool Deterministic,
        IReadOnlyDictionary<AiPersonalityTrait, CounterfactualController> Counterfactuals);
}

internal sealed record AiReviewedQualification(
    IReadOnlyList<AiLeagueDistribution> Distributions,
    IReadOnlyList<AiTraitSensitivity> TraitSensitivities,
    int ComparedStepCount);
