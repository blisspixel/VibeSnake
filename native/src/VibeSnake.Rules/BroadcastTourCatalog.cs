namespace VibeSnake.Rules;

public enum BroadcastTourTier : byte
{
    LocalFrequency = 0,
    DistrictRelay = 1,
    RegionalCoil = 2,
    CrownBroadcast = 3,
}

public enum TourSeedPolicyKind : byte
{
    Fixed = 0,
    ReviewedCorpus = 1,
}

public sealed record BroadcastTourGoal(
    string Id,
    ProgressionMetric Metric,
    int Target,
    string ExactRequirement,
    bool OptionalStyleGoal);

public sealed record BroadcastTourEvent(
    int SchemaVersion,
    string Id,
    BroadcastTourTier Tier,
    string ModeId,
    int ModeVersion,
    string RulesetId,
    int RulesVersion,
    string ScoreCategoryId,
    TourSeedPolicyKind SeedPolicy,
    ulong? FixedSeed,
    string RivalId,
    string StationId,
    BroadcastTourGoal PrimaryGoal,
    BroadcastTourGoal? StyleGoal,
    ProgressionReward Reward,
    IReadOnlyList<string> PrerequisiteEventIds,
    string IntroCopyId,
    string PostRunCopyId,
    string RetryCopyId,
    string ReplayCopyId,
    string AccessibilityCopyId,
    bool PracticeNoncompetitive,
    bool ImmediateRematch,
    bool ReplayAvailable);

public sealed record BroadcastTourValidation(
    bool Passed,
    int EventCount,
    int TierCount,
    int ReachableEventCount,
    int DuplicateRewardCount,
    int DependencyCycleCount,
    int ImpossibleGoalCount,
    int RulesContaminationCount,
    int MissingCopyCount,
    int MechanicalRewardCount,
    int UnknownContextCount);

public static class BroadcastTourCatalog
{
    public const int SchemaVersion = 1;

    public static IReadOnlyList<BroadcastTourEvent> Events { get; } =
    [
        Event("local-first-signal", BroadcastTourTier.LocalFrequency, 0UL, "balanced", "global_coil", [], Goal("score-50", ProgressionMetric.HighestScore, 50, "Score 50 points."), null, Reward("shed:first-signal", ProgressionRewardKind.CosmeticSet, "First Signal Shed")),
        Event("local-wrap-school", BroadcastTourTier.LocalFrequency, 7UL, "wall_hugger", "ourotron", ["local-first-signal"], Goal("wrap-3", ProgressionMetric.MostWrapsInRun, 3, "Wrap across an edge 3 times."), Goal("no-dead-end", ProgressionMetric.MostNearMissesInRun, 1, "Earn at least 1 near miss.", true), Reward("replay-frame:ourotron", ProgressionRewardKind.ReplayFrame, "Ourotron Replay Frame")),
        Event("local-hold-line", BroadcastTourTier.LocalFrequency, 42UL, "coward", "flow_signal", ["local-first-signal"], Goal("survive-600", ProgressionMetric.LongestSurvivalTicks, 600, "Survive 600 rules steps."), null, Reward("dossier:shelter-coil", ProgressionRewardKind.Dossier, "Shelter Coil Dossier")),
        Event("district-power-route", BroadcastTourTier.DistrictRelay, 99UL, "power_hunter", "the_pit", ["local-wrap-school", "local-hold-line"], Goal("power-1", ProgressionMetric.MostPowersInRun, 1, "Collect 1 power."), Goal("score-150", ProgressionMetric.HighestScore, 150, "Score 150 points.", true), Reward("shed:mutagenist", ProgressionRewardKind.CosmeticSet, "Mutagenist Shed")),
        Event("district-combo-carrier", BroadcastTourTier.DistrictRelay, 255UL, "greedy", "the_strike", ["district-power-route"], Goal("combo-5", ProgressionMetric.HighestCombo, 5, "Reach a 5x combo."), null, Reward("station-note:strike-1", ProgressionRewardKind.StationMaterial, "Strike Carrier Note")),
        Event("district-noise-test", BroadcastTourTier.DistrictRelay, 65_535UL, "drunk", "chaos_theory", ["district-power-route"], Goal("food-8", ProgressionMetric.MostFoodInRun, 8, "Eat 8 food items."), Goal("wrap-5", ProgressionMetric.MostWrapsInRun, 5, "Wrap 5 times.", true), Reward("run-card:noise", ProgressionRewardKind.RunCardTreatment, "Noise Run Card")),
        Event("regional-proof", BroadcastTourTier.RegionalCoil, 20_260_808UL, "optimal", "the_bureau", ["district-combo-carrier", "district-noise-test"], Goal("score-500", ProgressionMetric.HighestScore, 500, "Score 500 points."), Goal("powers-2", ProgressionMetric.MostPowersInRun, 2, "Collect 2 powers.", true), Reward("dossier:the-proof", ProgressionRewardKind.Dossier, "The Proof Dossier")),
        Event("regional-redline", BroadcastTourTier.RegionalCoil, 32_452_843UL, "speed_demon", "the_pit", ["regional-proof"], Goal("near-miss-5", ProgressionMetric.MostNearMissesInRun, 5, "Earn 5 near misses."), null, Reward("shed:redline", ProgressionRewardKind.CosmeticSet, "Redline Shed")),
        Event("regional-rim-route", BroadcastTourTier.RegionalCoil, 49_979_687UL, "wall_hugger", "ourotron", ["regional-proof"], Goal("wrap-10", ProgressionMetric.MostWrapsInRun, 10, "Wrap 10 times."), Goal("survive-900", ProgressionMetric.LongestSurvivalTicks, 900, "Survive 900 rules steps.", true), Reward("archive:rim-route", ProgressionRewardKind.ArchiveFragment, "Rim Route Fragment")),
        Event("crown-meanline", BroadcastTourTier.CrownBroadcast, 67_867_967UL, "balanced", "global_coil", ["regional-redline", "regional-rim-route"], Goal("score-750", ProgressionMetric.HighestScore, 750, "Score 750 points."), null, Reward("challenge:meanline", ProgressionRewardKind.ChallengeConfiguration, "Meanline Challenge")),
        Event("crown-edge", BroadcastTourTier.CrownBroadcast, 4_294_967_291UL, "yolo", "underground_scales", ["crown-meanline"], Goal("near-miss-10", ProgressionMetric.MostNearMissesInRun, 10, "Earn 10 near misses."), Goal("combo-8", ProgressionMetric.HighestCombo, 8, "Reach an 8x combo.", true), Reward("shed:edge-prophet", ProgressionRewardKind.CosmeticSet, "Edge Prophet Shed")),
        Event("crown-stillwater", BroadcastTourTier.CrownBroadcast, ulong.MaxValue, "zen_master", "flow_signal", ["crown-edge"], Goal("survive-1800", ProgressionMetric.LongestSurvivalTicks, 1_800, "Survive 1,800 rules steps."), Goal("score-1000", ProgressionMetric.HighestScore, 1_000, "Score 1,000 points.", true), Reward("broadcast-theme:crown", ProgressionRewardKind.BroadcastTheme, "Crown Broadcast Theme")),
    ];

    public static BroadcastTourValidation Validate()
    {
        var ids = Events.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        var duplicateRewards = Events
            .GroupBy(item => item.Reward.Id, StringComparer.Ordinal)
            .Count(group => group.Count() > 1);
        var missingCopy = Events.Count(item =>
            string.IsNullOrWhiteSpace(item.IntroCopyId)
            || string.IsNullOrWhiteSpace(item.PostRunCopyId)
            || string.IsNullOrWhiteSpace(item.RetryCopyId)
            || string.IsNullOrWhiteSpace(item.ReplayCopyId)
            || string.IsNullOrWhiteSpace(item.AccessibilityCopyId));
        var contaminated = Events.Count(item =>
            item.ModeId != RunModeCatalog.VibeId
            || item.ModeVersion != RunModeCatalog.CurrentModeVersion
            || item.RulesetId != SnakeRun.RulesetId
            || item.RulesVersion != SnakeRun.RulesVersion
            || item.ScoreCategoryId != RunModeCatalog.VibeAdaptiveScoreCategoryId
            || !item.PracticeNoncompetitive);
        var impossible = Events.Count(item =>
            !IsGoalPossible(item.PrimaryGoal)
            || (item.StyleGoal is { } style && !IsGoalPossible(style)));
        var mechanicalRewards = Events.Count(item => !IsExpressionReward(item.Reward.Kind));
        string[] knownStationIds =
        [
            "flow_signal",
            "chaos_theory",
            "global_coil",
            "ourotron",
            "the_pit",
            "the_bureau",
            "the_strike",
            "underground_scales",
        ];
        var unknownContexts = Events.Count(item =>
            AiPersonalityCatalog.BuiltIn.All(rival => rival.Id != item.RivalId)
            || !knownStationIds.Contains(item.StationId, StringComparer.Ordinal));
        var cycleCount = CountDependencyCycles(ids);
        var reachable = CountReachable(ids);
        return new BroadcastTourValidation(
            Passed: Events.Count == 12
                && duplicateRewards == 0
                && missingCopy == 0
                && contaminated == 0
                && impossible == 0
                && mechanicalRewards == 0
                && unknownContexts == 0
                && cycleCount == 0
                && reachable == Events.Count,
            EventCount: Events.Count,
            TierCount: Events.Select(item => item.Tier).Distinct().Count(),
            ReachableEventCount: reachable,
            DuplicateRewardCount: duplicateRewards,
            DependencyCycleCount: cycleCount,
            ImpossibleGoalCount: impossible,
            RulesContaminationCount: contaminated,
            MissingCopyCount: missingCopy,
            MechanicalRewardCount: mechanicalRewards,
            UnknownContextCount: unknownContexts);
    }

    private static BroadcastTourEvent Event(
        string id,
        BroadcastTourTier tier,
        ulong seed,
        string rivalId,
        string stationId,
        IReadOnlyList<string> prerequisites,
        BroadcastTourGoal primary,
        BroadcastTourGoal? style,
        ProgressionReward reward) =>
        new(
            SchemaVersion,
            id,
            tier,
            RunModeCatalog.VibeId,
            RunModeCatalog.CurrentModeVersion,
            SnakeRun.RulesetId,
            SnakeRun.RulesVersion,
            RunModeCatalog.VibeAdaptiveScoreCategoryId,
            TourSeedPolicyKind.Fixed,
            seed,
            rivalId,
            stationId,
            primary,
            style,
            reward,
            prerequisites,
            $"tour.{id}.intro",
            $"tour.{id}.post",
            $"tour.{id}.retry",
            $"tour.{id}.replay",
            $"tour.{id}.accessibility",
            PracticeNoncompetitive: true,
            ImmediateRematch: true,
            ReplayAvailable: true);

    private static BroadcastTourGoal Goal(
        string id,
        ProgressionMetric metric,
        int target,
        string exactRequirement,
        bool style = false) =>
        new(id, metric, target, exactRequirement, style);

    private static ProgressionReward Reward(
        string id,
        ProgressionRewardKind kind,
        string displayName) =>
        new(id, kind, displayName);

    private static bool IsGoalPossible(BroadcastTourGoal goal) =>
        goal.Target > 0 && goal.Metric switch
        {
            ProgressionMetric.HighestScore => goal.Target <= 10_000,
            ProgressionMetric.HighestCombo => goal.Target <= 10,
            ProgressionMetric.LongestLength => goal.Target <= 64,
            ProgressionMetric.MostFoodInRun => goal.Target <= 64,
            ProgressionMetric.MostWrapsInRun => goal.Target <= 100,
            ProgressionMetric.MostNearMissesInRun => goal.Target <= 100,
            ProgressionMetric.MostPowersInRun => goal.Target <= 10,
            ProgressionMetric.LongestSurvivalTicks => goal.Target <= 6_000,
            _ => false,
        };

    private static bool IsExpressionReward(ProgressionRewardKind kind) =>
        Enum.IsDefined(kind);

    private static int CountDependencyCycles(IReadOnlySet<string> ids)
    {
        var cycles = 0;
        var visiting = new HashSet<string>(StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in Events)
        {
            Visit(item.Id);
        }

        return cycles;

        void Visit(string id)
        {
            if (visited.Contains(id))
            {
                return;
            }

            if (!visiting.Add(id))
            {
                cycles++;
                return;
            }

            var item = Events.Single(candidate => candidate.Id == id);
            foreach (var prerequisite in item.PrerequisiteEventIds.Where(ids.Contains))
            {
                Visit(prerequisite);
            }

            visiting.Remove(id);
            visited.Add(id);
        }
    }

    private static int CountReachable(HashSet<string> ids)
    {
        var reachable = new HashSet<string>(StringComparer.Ordinal);
        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var item in Events)
            {
                if (!reachable.Contains(item.Id)
                    && item.PrerequisiteEventIds.All(prerequisite =>
                        ids.Contains(prerequisite) && reachable.Contains(prerequisite)))
                {
                    reachable.Add(item.Id);
                    changed = true;
                }
            }
        }

        return reachable.Count;
    }
}
