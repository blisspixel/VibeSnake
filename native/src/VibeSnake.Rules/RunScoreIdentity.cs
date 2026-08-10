namespace VibeSnake.Rules;

/// <summary>
/// Fair-score identity for a finished or in-progress run. Separates ruleset
/// contract and effective config hash from presentation so leaderboard
/// categories can reject mixed-rules entries.
/// </summary>
public sealed record RunScoreIdentity(
    string RulesetId,
    int RulesVersion,
    string ConfigHash,
    string ConfigHashAlgorithm,
    int Score,
    RunStatus Status,
    DeathCause DeathCause)
{
    public string RulesetContractId => $"{RulesetId}@{RulesVersion}";

    public string ModeId { get; init; } = RunModeCatalog.VibeId;

    public int ModeVersion { get; init; } = RunModeCatalog.CurrentModeVersion;

    public string ScoreCategoryId { get; init; } = RunModeCatalog.VibeFixedScoreCategoryId;

    public string DifficultyPolicyId { get; init; } = "vibe-fixed-cadence-v1";

    public bool AdaptationEnabled { get; init; }

    public string AdaptivePolicyId { get; init; } = AdaptiveDifficultyPolicy.DisabledPolicyId;

    public AdaptiveDifficultyState AdaptiveStateAtCapture { get; init; } =
        AdaptiveDifficultyState.Disabled;

    public string RunKindId { get; init; } = ScoreRunContextCatalog.NormalHumanRunKind;

    public string SeedCategoryId { get; init; } =
        ScoreRunContextCatalog.FreshLocalSeedCategory;

    public bool CompetitiveEligible { get; init; } = true;

    public string DisplayCategoryId { get; init; } = "normal-human";

    public static RunScoreIdentity FromRun(
        SnakeRun run,
        ScoreRunContext? context = null)
    {
        ArgumentNullException.ThrowIfNull(run);
        context ??= ScoreRunContextCatalog.NormalHuman;
        var canonicalContext = ScoreRunContextCatalog.Get(
            context.RunKindId,
            context.SeedCategoryId);
        if (canonicalContext != context)
        {
            throw new ArgumentException("The score run context is not canonical.", nameof(context));
        }

        return new RunScoreIdentity(
            RulesetId: SnakeRun.RulesetId,
            RulesVersion: SnakeRun.RulesVersion,
            ConfigHash: run.ConfigHash,
            ConfigHashAlgorithm: run.ConfigHashAlgorithm,
            Score: run.Score,
            Status: run.Status,
            DeathCause: run.DeathCause)
        {
            ModeId = run.Configuration.ModeId,
            ModeVersion = run.Configuration.ModeVersion,
            ScoreCategoryId = run.ScoreCategoryId,
            DifficultyPolicyId = run.Mode.DifficultyPolicyId,
            AdaptationEnabled = run.Configuration.EnableAdaptation,
            AdaptivePolicyId = run.Configuration.AdaptivePolicyId,
            AdaptiveStateAtCapture = run.AdaptiveDifficulty.State,
            RunKindId = context.RunKindId,
            SeedCategoryId = context.SeedCategoryId,
            CompetitiveEligible = context.CompetitiveEligible,
            DisplayCategoryId = context.DisplayCategoryId,
        };
    }

    /// <summary>
    /// Two identities are category-compatible when they share ruleset and config.
    /// Score and terminal status may differ.
    /// </summary>
    public bool IsSameScoreCategory(RunScoreIdentity other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return string.Equals(RulesetId, other.RulesetId, StringComparison.Ordinal)
            && RulesVersion == other.RulesVersion
            && string.Equals(ModeId, other.ModeId, StringComparison.Ordinal)
            && ModeVersion == other.ModeVersion
            && string.Equals(ScoreCategoryId, other.ScoreCategoryId, StringComparison.Ordinal)
            && string.Equals(
                DifficultyPolicyId,
                other.DifficultyPolicyId,
                StringComparison.Ordinal)
            && AdaptationEnabled == other.AdaptationEnabled
            && string.Equals(
                AdaptivePolicyId,
                other.AdaptivePolicyId,
                StringComparison.Ordinal)
            && string.Equals(RunKindId, other.RunKindId, StringComparison.Ordinal)
            && string.Equals(SeedCategoryId, other.SeedCategoryId, StringComparison.Ordinal)
            && CompetitiveEligible == other.CompetitiveEligible
            && string.Equals(ConfigHash, other.ConfigHash, StringComparison.Ordinal)
            && string.Equals(
                ConfigHashAlgorithm,
                other.ConfigHashAlgorithm,
                StringComparison.Ordinal);
    }
}
