using System.Text.Json;
using VibeSnake.Persistence;

namespace VibeSnake.Rules.Tests;

public sealed class AiPersonalityQualificationTests
{
    private static readonly string[] CompatibilityIds =
    [
        "speed_demon",
        "coward",
        "greedy",
        "power_hunter",
        "drunk",
        "optimal",
        "yolo",
        "balanced",
        "wall_hugger",
        "zen_master",
    ];

    private static readonly string[] DisplayNames =
    [
        "Redline",
        "Shelter Coil",
        "Crownchaser",
        "Mutagenist",
        "Noise Coil",
        "The Proof",
        "Edge Prophet",
        "Meanline",
        "Rimkeeper",
        "Stillwater",
    ];

    [Fact]
    public void Personality_truth_custom_schema_and_overlay_share_one_qualified_contract()
    {
        var repositoryRoot = BalanceLaboratoryReport.ResolveRepositoryRoot();
        var league = AiLeagueTests.RunReviewedQualification();
        var claims = EvaluateClaims(league.Distributions);
        var customProbes = RunCustomValidationProbes();
        var overlay = BuildOverlayEvidence();
        var compatibilityIdsRetained = AiPersonalityCatalog.BuiltIn
            .Select(personality => personality.Id)
            .SequenceEqual(CompatibilityIds, StringComparer.Ordinal);
        var namesMatch = AiPersonalityCatalog.BuiltIn
            .Select(personality => personality.Name)
            .SequenceEqual(DisplayNames, StringComparer.Ordinal);
        var allTraitsMaterial = league.TraitSensitivities.All(sensitivity =>
            sensitivity.MateriallyAffectedDecisions);
        var greedConsumed = league.TraitSensitivities
            .Where(sensitivity => sensitivity.Trait == AiPersonalityTrait.Greed)
            .All(sensitivity => sensitivity.MateriallyAffectedDecisions);
        var passed = compatibilityIdsRetained
            && namesMatch
            && claims.Count == 10
            && claims.All(claim => claim.Passed)
            && league.TraitSensitivities.Count == 60
            && allTraitsMaterial
            && greedConsumed
            && customProbes.All(probe => probe.Passed)
            && overlay.Passed;
        var evidence = new AiPersonalityQualificationEvidence(
            AiPersonalityQualificationReport.SchemaVersion,
            AiPersonalityQualificationReport.Kind,
            passed,
            AiPersonalityController.AlgorithmId,
            PersonalityDocument.CurrentSchemaVersion,
            AiPersonalityCatalog.BuiltIn.Count,
            claims.Count,
            league.TraitSensitivities.Count,
            league.TraitSensitivities.Count(sensitivity =>
                !sensitivity.MateriallyAffectedDecisions),
            league.ComparedStepCount,
            compatibilityIdsRetained,
            greedConsumed,
            allTraitsMaterial,
            DisplayNames,
            claims,
            customProbes,
            overlay,
            [
                "Behavior ranges are AI league regression claims, not human balance targets.",
                "Custom files use the same six native traits but remain visibly unofficial and unqualified.",
                "Overlay data is engine-independent; the later spectator package owns full player-flow integration.",
            ]);
        var path = AiPersonalityQualificationReport.Write(repositoryRoot, evidence);

        Assert.True(File.Exists(path));
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        Assert.Equal(AiPersonalityQualificationReport.Kind, root.GetProperty("kind").GetString());
        Assert.True(root.GetProperty("passed").GetBoolean());
        Assert.Equal(10, root.GetProperty("behaviorClaimCount").GetInt32());
        Assert.Equal(60, root.GetProperty("traitSensitivityCount").GetInt32());
        Assert.Equal(0, root.GetProperty("inertTraitCount").GetInt32());
        Assert.Equal(6, root.GetProperty("customValidation").GetArrayLength());
        Assert.True(passed);
    }

    private static IReadOnlyList<AiBehaviorClaimEvidence> EvaluateClaims(
        IReadOnlyList<AiLeagueDistribution> distributions) =>
        AiPersonalityCatalog.BehaviorClaims
            .Select(claim =>
            {
                var distribution = distributions.Single(item =>
                    string.Equals(
                        item.PersonalityId,
                        claim.PersonalityId,
                        StringComparison.Ordinal));
                var value = claim.Metric switch
                {
                    AiBehaviorMetric.ScoreP50 => distribution.ScoreP50,
                    AiBehaviorMetric.SurvivalP50 => distribution.SurvivalP50,
                    AiBehaviorMetric.FoodEfficiencyPerThousandSteps =>
                        distribution.FoodEfficiencyPerThousandSteps,
                    AiBehaviorMetric.PowerPreferenceBasisPoints =>
                        distribution.PowerPreferenceBasisPoints,
                    AiBehaviorMetric.RiskExposureBasisPoints =>
                        distribution.RiskExposureBasisPoints,
                    AiBehaviorMetric.DeadEndBasisPoints => distribution.DeadEndBasisPoints,
                    AiBehaviorMetric.RouteEfficiencyBasisPoints =>
                        distribution.RouteEfficiencyBasisPoints,
                    _ => throw new ArgumentOutOfRangeException(),
                };
                return new AiBehaviorClaimEvidence(
                    claim.PersonalityId,
                    claim.Metric,
                    value,
                    claim.InclusiveMinimum,
                    claim.InclusiveMaximum,
                    claim.PlayerFacingMeaning,
                    value >= claim.InclusiveMinimum && value <= claim.InclusiveMaximum);
            })
            .ToArray();

    private static IReadOnlyList<AiCustomValidationProbe> RunCustomValidationProbes()
    {
        const string valid =
            """
            {
              "schemaVersion": 1,
              "name": "Route Planner",
              "description": "Prefers measured routes.",
              "aggression": 0.4,
              "risk_tolerance": 0.2,
              "patience": 0.9,
              "greed": 0.3,
              "chaos": 0.1,
              "power_up_priority": 0.6,
              "color": [80, 180, 255]
            }
            """;
        var validRead = PersonalityDocument.Read(valid, "route_planner.json");
        var unknownRead = PersonalityDocument.Read(
            valid.Replace(
                "\"name\":",
                "\"unexpected\": true, \"name\":",
                StringComparison.Ordinal),
            "unknown.json");
        var duplicateRead = PersonalityDocument.Read(
            valid.Replace(
                "\"greed\": 0.3,",
                "\"greed\": 0.3, \"greed\": 0.2,",
                StringComparison.Ordinal),
            "duplicate.json");
        var oversizedRead = PersonalityDocument.Read(
            new string('x', PersonalityDocument.MaximumDocumentCharacters + 1),
            "oversized.json");
        var reserved = validRead.Document!.ToProfile("balanced");
        var invalidId = validRead.Document.ToProfile("Bad ID");
        return
        [
            Probe("valid", "route_planner.json", validRead, PersonalityLoadCode.Success),
            Probe("unknown", "unknown.json", unknownRead, PersonalityLoadCode.UnknownField),
            Probe("duplicate", "duplicate.json", duplicateRead, PersonalityLoadCode.DuplicateField),
            Probe("oversized", "oversized.json", oversizedRead, PersonalityLoadCode.TooLarge),
            Probe("reserved", "balanced", reserved, PersonalityLoadCode.ReservedId),
            Probe("invalid-id", "Bad ID", invalidId, PersonalityLoadCode.PathUnsafe),
        ];
    }

    private static AiCustomValidationProbe Probe(
        string id,
        string sourceName,
        PersonalityLoadResult result,
        PersonalityLoadCode expected)
    {
        var filenameSpecific = result.Message.Contains(sourceName, StringComparison.Ordinal);
        return new AiCustomValidationProbe(
            id,
            sourceName,
            result.Code,
            expected,
            filenameSpecific,
            result.Code == expected && filenameSpecific);
    }

    private static AiOverlayEvidence BuildOverlayEvidence()
    {
        var profile = AiPersonalityCatalog.BuiltInProfiles.Single(item =>
            item.Personality.Id == "balanced");
        var run = SnakeRun.Create(42UL, RunModeCatalog.CreateConfig(RunModeCatalog.Vibe));
        var controller = new AiPersonalityController(profile.Personality, 80_003UL);
        var decisions = new List<AiDecision>();
        for (var index = 0; index < 7 && run.Status == RunStatus.Running; index++)
        {
            var decision = controller.SelectDecision(run);
            decisions.Add(decision);
            run.QueueDirection(decision.Direction);
            run.Step();
        }

        var current = decisions[^1];
        var builtInOverlay = AiSpectatorOverlay.Create(profile, current, decisions);
        var customProfile = profile with
        {
            ContentKind = AiPersonalityContentKind.Custom,
            StatusLabel = AiPersonalityCatalog.CustomStatusLabel,
            OfficialLeagueQualified = false,
        };
        var customOverlay = AiSpectatorOverlay.Create(customProfile, current, decisions);
        var passed = builtInOverlay.PolicyId
                == "native-personality-controller-v2/balanced"
            && builtInOverlay.Target != "NONE"
            && builtInOverlay.RecentDecisions.Count == 5
            && builtInOverlay.ContentStatus == AiPersonalityCatalog.BuiltInStatusLabel
            && customOverlay.ContentStatus == AiPersonalityCatalog.CustomStatusLabel
            && !customOverlay.OfficialLeagueQualified;
        return new AiOverlayEvidence(
            builtInOverlay.PolicyId,
            builtInOverlay.Target,
            builtInOverlay.Risk,
            builtInOverlay.CurrentDecision,
            builtInOverlay.RecentDecisions.Count,
            builtInOverlay.ContentStatus,
            customOverlay.ContentStatus,
            customOverlay.OfficialLeagueQualified,
            passed);
    }
}
