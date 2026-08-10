namespace VibeSnake.Rules;

public enum ProgressionGoalLane : byte
{
    Mastery = 0,
    Discovery = 1,
    Identity = 2,
}

public enum ProgressionMetric : byte
{
    HighestScore = 0,
    HighestCombo = 1,
    LongestLength = 2,
    MostFoodInRun = 3,
    MostWrapsInRun = 4,
    MostNearMissesInRun = 5,
    MostPowersInRun = 6,
    LongestSurvivalTicks = 7,
    CompletedHumanRuns = 8,
    SavedLoadouts = 9,
    CosmeticSetsUnlocked = 10,
    TourEventsCompleted = 11,
}

public enum ProgressionRewardKind : byte
{
    AchievementBadge = 0,
    CosmeticSet = 1,
    ReplayFrame = 2,
    RivalRematch = 3,
    StationMaterial = 4,
    Dossier = 5,
    ArchiveFragment = 6,
    RunCardTreatment = 7,
    BroadcastTheme = 8,
    ChallengeConfiguration = 9,
    LoadoutSlot = 10,
}

public enum ProgressionPacingTier : byte
{
    Early = 0,
    Middle = 1,
    Mastery = 2,
}

public sealed record ProgressionReward(
    string Id,
    ProgressionRewardKind Kind,
    string DisplayName);

public sealed record ProgressionGoalDefinition(
    string Id,
    ProgressionGoalLane Lane,
    string Name,
    string ExactRequirement,
    ProgressionMetric Metric,
    int Target,
    AchievementModeEligibility ModeEligibility,
    string RulesetId,
    int RulesVersion,
    ProgressionReward Reward,
    ProgressionPacingTier PacingTier);

public sealed record ProgressionGoalProgress(
    ProgressionGoalDefinition Definition,
    int Current,
    int Target,
    bool Completed,
    bool Highlighted)
{
    public string ExactProgress => $"{Math.Min(Current, Target)}/{Target}";
}

/// <summary>
/// Monotonic human progression facts. AI, replay, tutorial, practice, and
/// modified runs must never be merged into this snapshot.
/// </summary>
public sealed record ProgressionMetrics(
    int HighestScore = 0,
    int HighestCombo = 0,
    int LongestLength = 0,
    int MostFoodInRun = 0,
    int MostWrapsInRun = 0,
    int MostNearMissesInRun = 0,
    int MostPowersInRun = 0,
    int LongestSurvivalTicks = 0,
    int CompletedHumanRuns = 0,
    int SavedLoadouts = 0,
    int CosmeticSetsUnlocked = 0,
    int TourEventsCompleted = 0)
{
    public int ValueFor(ProgressionMetric metric) => metric switch
    {
        ProgressionMetric.HighestScore => HighestScore,
        ProgressionMetric.HighestCombo => HighestCombo,
        ProgressionMetric.LongestLength => LongestLength,
        ProgressionMetric.MostFoodInRun => MostFoodInRun,
        ProgressionMetric.MostWrapsInRun => MostWrapsInRun,
        ProgressionMetric.MostNearMissesInRun => MostNearMissesInRun,
        ProgressionMetric.MostPowersInRun => MostPowersInRun,
        ProgressionMetric.LongestSurvivalTicks => LongestSurvivalTicks,
        ProgressionMetric.CompletedHumanRuns => CompletedHumanRuns,
        ProgressionMetric.SavedLoadouts => SavedLoadouts,
        ProgressionMetric.CosmeticSetsUnlocked => CosmeticSetsUnlocked,
        ProgressionMetric.TourEventsCompleted => TourEventsCompleted,
        _ => throw new ArgumentOutOfRangeException(nameof(metric), metric, "Unknown progression metric."),
    };

    public ProgressionMetrics MergeHumanRun(
        RunAchievementMetrics run,
        ScoreRunContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context != ScoreRunContextCatalog.NormalHuman)
        {
            return this;
        }

        if (!run.IsTerminal)
        {
            throw new ArgumentException("Progression accepts terminal human runs only.", nameof(run));
        }

        return this with
        {
            HighestScore = Math.Max(HighestScore, run.Score),
            HighestCombo = Math.Max(HighestCombo, run.MaxCombo),
            LongestLength = Math.Max(LongestLength, run.Length),
            MostFoodInRun = Math.Max(MostFoodInRun, run.FoodEaten),
            MostWrapsInRun = Math.Max(MostWrapsInRun, run.WrapCount),
            MostNearMissesInRun = Math.Max(MostNearMissesInRun, run.NearMisses),
            MostPowersInRun = Math.Max(MostPowersInRun, run.PowerupsCollected),
            LongestSurvivalTicks = Math.Max(LongestSurvivalTicks, run.SurvivalTicks),
            CompletedHumanRuns = checked(CompletedHumanRuns + 1),
        };
    }

    public ProgressionMetrics WithPresentationProgress(
        int savedLoadouts,
        int cosmeticSetsUnlocked,
        int tourEventsCompleted)
    {
        if (savedLoadouts < 0 || cosmeticSetsUnlocked < 0 || tourEventsCompleted < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(savedLoadouts),
                "Presentation progression cannot be negative.");
        }

        return this with
        {
            SavedLoadouts = Math.Max(SavedLoadouts, savedLoadouts),
            CosmeticSetsUnlocked = Math.Max(CosmeticSetsUnlocked, cosmeticSetsUnlocked),
            TourEventsCompleted = Math.Max(TourEventsCompleted, tourEventsCompleted),
        };
    }
}

public static class ProgressionGoalCatalog
{
    public static IReadOnlyList<ProgressionGoalDefinition> Goals { get; } =
    [
        Goal("first_bite", ProgressionGoalLane.Discovery, "First Bite", "Score at least 1 point in one Vibe run.", ProgressionMetric.HighestScore, 1, "achievement:first_bite", ProgressionRewardKind.AchievementBadge, ProgressionPacingTier.Early),
        Goal("century", ProgressionGoalLane.Mastery, "Century", "Reach 100 points in one Vibe run.", ProgressionMetric.HighestScore, 100, "achievement:century", ProgressionRewardKind.AchievementBadge, ProgressionPacingTier.Early),
        Goal("high_roller", ProgressionGoalLane.Mastery, "High Roller", "Reach 500 points in one Vibe run.", ProgressionMetric.HighestScore, 500, "achievement:high_roller", ProgressionRewardKind.AchievementBadge, ProgressionPacingTier.Middle),
        Goal("legend", ProgressionGoalLane.Mastery, "Legend", "Reach 1,000 points in one Vibe run.", ProgressionMetric.HighestScore, 1_000, "achievement:legend", ProgressionRewardKind.AchievementBadge, ProgressionPacingTier.Mastery),
        Goal("just_a_taste", ProgressionGoalLane.Discovery, "Just a Taste", "Eat 5 food items in one Vibe run.", ProgressionMetric.MostFoodInRun, 5, "achievement:just_a_taste", ProgressionRewardKind.AchievementBadge, ProgressionPacingTier.Early),
        Goal("getting_longer", ProgressionGoalLane.Discovery, "Getting Longer", "Reach length 5 in one Vibe run.", ProgressionMetric.LongestLength, 5, "achievement:getting_longer", ProgressionRewardKind.AchievementBadge, ProgressionPacingTier.Early),
        Goal("growing_strong", ProgressionGoalLane.Mastery, "Growing Strong", "Reach length 10 in one Vibe run.", ProgressionMetric.LongestLength, 10, "achievement:growing_strong", ProgressionRewardKind.AchievementBadge, ProgressionPacingTier.Middle),
        Goal("serpent", ProgressionGoalLane.Mastery, "Serpent", "Reach length 25 in one Vibe run.", ProgressionMetric.LongestLength, 25, "achievement:serpent", ProgressionRewardKind.AchievementBadge, ProgressionPacingTier.Mastery),
        Goal("combo_starter", ProgressionGoalLane.Mastery, "Combo Starter", "Reach a 5x combo in one Vibe run.", ProgressionMetric.HighestCombo, 5, "achievement:combo_starter", ProgressionRewardKind.AchievementBadge, ProgressionPacingTier.Early),
        Goal("combo_king", ProgressionGoalLane.Mastery, "Combo King", "Reach a 10x combo in one Vibe run.", ProgressionMetric.HighestCombo, 10, "achievement:combo_king", ProgressionRewardKind.AchievementBadge, ProgressionPacingTier.Mastery),
        Goal("wrap_around", ProgressionGoalLane.Discovery, "Wrap Around", "Wrap across a board edge 3 times in one Vibe run.", ProgressionMetric.MostWrapsInRun, 3, "achievement:wrap_around", ProgressionRewardKind.AchievementBadge, ProgressionPacingTier.Early),
        Goal("close_call", ProgressionGoalLane.Mastery, "Close Call", "Earn 10 near misses in one Vibe run.", ProgressionMetric.MostNearMissesInRun, 10, "achievement:close_call", ProgressionRewardKind.AchievementBadge, ProgressionPacingTier.Middle),
        Goal("powered_up", ProgressionGoalLane.Discovery, "Powered Up", "Collect 1 power in one Vibe run.", ProgressionMetric.MostPowersInRun, 1, "achievement:powered_up", ProgressionRewardKind.AchievementBadge, ProgressionPacingTier.Early),
        Goal("power_hungry", ProgressionGoalLane.Discovery, "Power Hungry", "Collect 5 powers in one Vibe run.", ProgressionMetric.MostPowersInRun, 5, "achievement:power_hungry", ProgressionRewardKind.AchievementBadge, ProgressionPacingTier.Middle),
        Goal("quick_reflexes", ProgressionGoalLane.Mastery, "Quick Reflexes", "Survive 600 rules steps, equal to 30 seconds at base cadence, in one Vibe run.", ProgressionMetric.LongestSurvivalTicks, 600, "achievement:quick_reflexes", ProgressionRewardKind.AchievementBadge, ProgressionPacingTier.Early),
        Goal("endurance", ProgressionGoalLane.Mastery, "Endurance", "Survive 3,600 rules steps, equal to 180 seconds at base cadence, in one Vibe run.", ProgressionMetric.LongestSurvivalTicks, 3_600, "achievement:endurance", ProgressionRewardKind.AchievementBadge, ProgressionPacingTier.Middle),
        Goal("marathon", ProgressionGoalLane.Mastery, "Marathon", "Survive 6,000 rules steps, equal to 300 seconds at base cadence, in one Vibe run.", ProgressionMetric.LongestSurvivalTicks, 6_000, "achievement:marathon", ProgressionRewardKind.AchievementBadge, ProgressionPacingTier.Mastery),
        Goal("identity_first_loadout", ProgressionGoalLane.Identity, "Save Your Signal", "Save 1 cosmetic loadout.", ProgressionMetric.SavedLoadouts, 1, "loadout-slot:2", ProgressionRewardKind.LoadoutSlot, ProgressionPacingTier.Early),
        Goal("identity_three_sets", ProgressionGoalLane.Identity, "Three Frequencies", "Unlock 3 authored cosmetic sets.", ProgressionMetric.CosmeticSetsUnlocked, 3, "run-card:three-frequencies", ProgressionRewardKind.RunCardTreatment, ProgressionPacingTier.Middle),
        Goal("identity_tour_eight", ProgressionGoalLane.Identity, "Known on the Circuit", "Complete 8 Broadcast Tour events.", ProgressionMetric.TourEventsCompleted, 8, "broadcast-theme:circuit", ProgressionRewardKind.BroadcastTheme, ProgressionPacingTier.Mastery),
    ];

    public static IReadOnlyList<ProgressionGoalProgress> BuildProgress(
        ProgressionMetrics metrics,
        string? highlightedGoalId = null) =>
        Goals.Select(goal => new ProgressionGoalProgress(
                goal,
                metrics.ValueFor(goal.Metric),
                goal.Target,
                metrics.ValueFor(goal.Metric) >= goal.Target,
                string.Equals(goal.Id, highlightedGoalId, StringComparison.Ordinal)))
            .ToArray();

    public static ProgressionGoalDefinition? Find(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return Goals.SingleOrDefault(goal => string.Equals(goal.Id, id, StringComparison.Ordinal));
    }

    private static ProgressionGoalDefinition Goal(
        string id,
        ProgressionGoalLane lane,
        string name,
        string exactRequirement,
        ProgressionMetric metric,
        int target,
        string rewardId,
        ProgressionRewardKind rewardKind,
        ProgressionPacingTier pacingTier) =>
        new(
            id,
            lane,
            name,
            exactRequirement,
            metric,
            target,
            AchievementModeEligibility.Vibe,
            SnakeRun.RulesetId,
            SnakeRun.RulesVersion,
            new ProgressionReward(rewardId, rewardKind, name),
            pacingTier);
}
