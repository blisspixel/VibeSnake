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

    public static RunScoreIdentity FromRun(SnakeRun run)
    {
        ArgumentNullException.ThrowIfNull(run);
        return new RunScoreIdentity(
            RulesetId: SnakeRun.RulesetId,
            RulesVersion: SnakeRun.RulesVersion,
            ConfigHash: run.ConfigHash,
            ConfigHashAlgorithm: run.ConfigHashAlgorithm,
            Score: run.Score,
            Status: run.Status,
            DeathCause: run.DeathCause);
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
            && string.Equals(ConfigHash, other.ConfigHash, StringComparison.Ordinal)
            && string.Equals(
                ConfigHashAlgorithm,
                other.ConfigHashAlgorithm,
                StringComparison.Ordinal);
    }
}
