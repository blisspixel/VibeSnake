namespace VibeSnake.Rules;

[Flags]
public enum RunModeFeatures : ushort
{
    None = 0,
    Movement = 1 << 0,
    Wrapping = 1 << 1,
    FoodAndGrowth = 1 << 2,
    FixedSpeed = 1 << 3,
    SelfCollision = 1 << 4,
    Pause = 1 << 5,
    Starvation = 1 << 6,
    ComboScoring = 1 << 7,
    NearMisses = 1 << 8,
    PowerUps = 1 << 9,
    Progression = 1 << 10,
    FullFeedback = 1 << 11,
    AdaptivePolicy = 1 << 12,
}

public enum RunPauseRule : byte
{
    FreezeRulesAndBufferedInput = 0,
}

public enum RunSeedRule : byte
{
    FreshLocalSeedPerRun = 0,
}

public enum RunRestartRule : byte
{
    FreshSeedSameModeAndBoard = 0,
}

public enum RunAdaptiveState : byte
{
    Disabled = 0,
    EnabledByDefault = 1,
}

public sealed record RunModeDefinition(
    string Id,
    int Version,
    string DisplayName,
    string Description,
    string ScoreCategoryId,
    int BoardWidth,
    int BoardHeight,
    RunModeFeatures Features,
    RunPauseRule PauseRule,
    RunSeedRule SeedRule,
    RunRestartRule RestartRule,
    RunAdaptiveState AdaptiveState,
    string AdaptivePolicyId,
    string DifficultyPolicyId,
    string ScoreModelDescription)
{
    public string ContractId => $"{Id}@{Version}";

    public bool Includes(RunModeFeatures feature) => (Features & feature) == feature;
}

/// <summary>
/// Closed product-mode catalog. Modes share the versioned core rules engine but
/// own separate stable identities, configurations, and fair-score categories.
/// </summary>
public static class RunModeCatalog
{
    public const string ClassicId = "classic";
    public const string VibeId = "vibe";
    public const int CurrentModeVersion = 1;
    public const string ClassicScoreCategoryId = "classic-standard-v1";
    public const string VibeAdaptiveScoreCategoryId = "vibe-standard-v1-dda-on";
    public const string VibeFixedScoreCategoryId = "vibe-standard-v1-dda-off";

    public static RunModeDefinition Classic { get; } = new(
        Id: ClassicId,
        Version: CurrentModeVersion,
        DisplayName: "Classic",
        Description: "Route, eat, grow, wrap, and survive your own body.",
        ScoreCategoryId: ClassicScoreCategoryId,
        BoardWidth: 64,
        BoardHeight: 33,
        Features: RunModeFeatures.Movement
            | RunModeFeatures.Wrapping
            | RunModeFeatures.FoodAndGrowth
            | RunModeFeatures.FixedSpeed
            | RunModeFeatures.SelfCollision
            | RunModeFeatures.Pause,
        PauseRule: RunPauseRule.FreezeRulesAndBufferedInput,
        SeedRule: RunSeedRule.FreshLocalSeedPerRun,
        RestartRule: RunRestartRule.FreshSeedSameModeAndBoard,
        AdaptiveState: RunAdaptiveState.Disabled,
        AdaptivePolicyId: AdaptiveDifficultyPolicy.DisabledPolicyId,
        DifficultyPolicyId: "classic-fixed-cadence-v1",
        ScoreModelDescription: "Fixed 10 points per food; no combo, speed, length, near-miss, or power bonus.");

    public static RunModeDefinition Vibe { get; } = new(
        Id: VibeId,
        Version: CurrentModeVersion,
        DisplayName: "Vibe",
        Description: "Build combos under hunger pressure and route through powers.",
        ScoreCategoryId: VibeAdaptiveScoreCategoryId,
        BoardWidth: 64,
        BoardHeight: 33,
        Features: RunModeFeatures.Movement
            | RunModeFeatures.Wrapping
            | RunModeFeatures.FoodAndGrowth
            | RunModeFeatures.FixedSpeed
            | RunModeFeatures.SelfCollision
            | RunModeFeatures.Pause
            | RunModeFeatures.Starvation
            | RunModeFeatures.ComboScoring
            | RunModeFeatures.NearMisses
            | RunModeFeatures.PowerUps
            | RunModeFeatures.Progression
            | RunModeFeatures.FullFeedback
            | RunModeFeatures.AdaptivePolicy,
        PauseRule: RunPauseRule.FreezeRulesAndBufferedInput,
        SeedRule: RunSeedRule.FreshLocalSeedPerRun,
        RestartRule: RunRestartRule.FreshSeedSameModeAndBoard,
        AdaptiveState: RunAdaptiveState.EnabledByDefault,
        AdaptivePolicyId: AdaptiveDifficultyPolicy.CurrentPolicyId,
        DifficultyPolicyId: "vibe-fixed-cadence-v1",
        ScoreModelDescription: "Food, combo, speed, length, near-miss, and power-aware scoring.");

    public static IReadOnlyList<RunModeDefinition> All { get; } = [Classic, Vibe];

    public static RunModeDefinition Get(string id, int version)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        var mode = All.SingleOrDefault(
            candidate => string.Equals(candidate.Id, id, StringComparison.Ordinal)
                && candidate.Version == version);
        return mode ?? throw new ArgumentException(
            $"Unsupported run mode identity {id}@{version}.",
            nameof(id));
    }

    public static bool IsSupportedIdentity(string? id, int version) =>
        version == CurrentModeVersion
        && (string.Equals(id, ClassicId, StringComparison.Ordinal)
            || string.Equals(id, VibeId, StringComparison.Ordinal));

    public static RunConfig CreateConfig(
        RunModeDefinition mode,
        bool? enableAdaptation = null)
    {
        ArgumentNullException.ThrowIfNull(mode);
        var canonical = Get(mode.Id, mode.Version);
        if (!ReferenceEquals(canonical, mode) && canonical != mode)
        {
            throw new ArgumentException("Run mode definition does not match the catalog.", nameof(mode));
        }

        if (canonical.Id == ClassicId && enableAdaptation == true)
        {
            throw new ArgumentException("Classic does not permit adaptation.", nameof(enableAdaptation));
        }

        var adaptationEnabled = canonical.Id == VibeId && enableAdaptation != false;
        return canonical.Id switch
        {
            ClassicId => new RunConfig(
                Width: canonical.BoardWidth,
                Height: canonical.BoardHeight,
                PowerSpawnIntervalTicks: 0,
                EnableNearMiss: false,
                EnableComboExpiredEvent: false,
                EnableAchievementCandidates: false,
                ModeId: canonical.Id,
                ModeVersion: canonical.Version,
                EnableStarvation: false,
                EnableComboScoring: false,
                EnableSpeedScoreBonus: false,
                EnableLengthScoreBonus: false,
                EnableAdaptation: false,
                AdaptivePolicyId: AdaptiveDifficultyPolicy.DisabledPolicyId,
                EnablePowerDecisionOffers: false),
            VibeId => new RunConfig(
                Width: canonical.BoardWidth,
                Height: canonical.BoardHeight,
                EnableAchievementCandidates: true,
                ModeId: canonical.Id,
                ModeVersion: canonical.Version,
                EnableAdaptation: adaptationEnabled,
                AdaptivePolicyId: adaptationEnabled
                    ? AdaptiveDifficultyPolicy.CurrentPolicyId
                    : AdaptiveDifficultyPolicy.DisabledPolicyId,
                EnablePowerDecisionOffers: true),
            _ => throw new InvalidOperationException("Unknown catalog run mode."),
        };
    }

    /// <summary>
    /// Stable fair-score category for the full effective mode and DDA contract.
    /// </summary>
    public static string GetScoreCategoryId(RunConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        config.Validate();
        return config.ModeId switch
        {
            ClassicId => ClassicScoreCategoryId,
            VibeId when config.EnableAdaptation => VibeAdaptiveScoreCategoryId,
            VibeId => VibeFixedScoreCategoryId,
            _ => throw new InvalidOperationException("Unknown catalog run mode."),
        };
    }
}
