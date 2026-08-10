namespace VibeSnake.Rules;

/// <summary>
/// Stable run-purpose and seed-origin identity for score separation. The
/// context is supplied by the product flow because pure rules cannot infer
/// whether an identical deterministic run is human, tutorial, replay, or AI.
/// </summary>
public sealed record ScoreRunContext(
    string RunKindId,
    string SeedCategoryId,
    bool CompetitiveEligible,
    string DisplayCategoryId);

public static class ScoreRunContextCatalog
{
    public const string NormalHumanRunKind = "normal-human";
    public const string TutorialRunKind = "tutorial";
    public const string PracticeRunKind = "practice";
    public const string SeededChallengeRunKind = "seeded-challenge";
    public const string AiRunKind = "ai";
    public const string ReplayRunKind = "replay";
    public const string ModifiedRunKind = "modified";
    public const string LegacyRunKind = "legacy-0.2";

    public const string FreshLocalSeedCategory = "fresh-local";
    public const string FixedChallengeSeedCategory = "fixed-challenge";
    public const string TutorialSeedCategory = "tutorial-scripted";
    public const string PracticeSeedCategory = "practice-local";
    public const string AiSeedCategory = "ai-simulation";
    public const string ReplaySeedCategory = "recorded-replay";
    public const string ModifiedSeedCategory = "modified-local";
    public const string LegacySeedCategory = "legacy-unknown";

    public const string LegacyDisplayCategory = "Legacy 0.2";

    public static ScoreRunContext NormalHuman { get; } = new(
        NormalHumanRunKind,
        FreshLocalSeedCategory,
        CompetitiveEligible: true,
        DisplayCategoryId: "normal-human");

    public static ScoreRunContext Tutorial { get; } = new(
        TutorialRunKind,
        TutorialSeedCategory,
        CompetitiveEligible: false,
        DisplayCategoryId: "tutorial");

    public static ScoreRunContext Practice { get; } = new(
        PracticeRunKind,
        PracticeSeedCategory,
        CompetitiveEligible: false,
        DisplayCategoryId: "practice");

    public static ScoreRunContext SeededChallenge { get; } = new(
        SeededChallengeRunKind,
        FixedChallengeSeedCategory,
        CompetitiveEligible: true,
        DisplayCategoryId: "seeded-challenge");

    public static ScoreRunContext Ai { get; } = new(
        AiRunKind,
        AiSeedCategory,
        CompetitiveEligible: false,
        DisplayCategoryId: "ai");

    public static ScoreRunContext Replay { get; } = new(
        ReplayRunKind,
        ReplaySeedCategory,
        CompetitiveEligible: false,
        DisplayCategoryId: "replay");

    public static ScoreRunContext Modified { get; } = new(
        ModifiedRunKind,
        ModifiedSeedCategory,
        CompetitiveEligible: false,
        DisplayCategoryId: "modified");

    public static ScoreRunContext Legacy { get; } = new(
        LegacyRunKind,
        LegacySeedCategory,
        CompetitiveEligible: false,
        DisplayCategoryId: LegacyDisplayCategory);

    public static IReadOnlyList<ScoreRunContext> All { get; } =
    [
        NormalHuman,
        Tutorial,
        Practice,
        SeededChallenge,
        Ai,
        Replay,
        Modified,
        Legacy,
    ];

    public static ScoreRunContext Get(string runKindId, string seedCategoryId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runKindId);
        ArgumentException.ThrowIfNullOrWhiteSpace(seedCategoryId);
        return All.SingleOrDefault(context =>
                string.Equals(context.RunKindId, runKindId, StringComparison.Ordinal)
                && string.Equals(
                    context.SeedCategoryId,
                    seedCategoryId,
                    StringComparison.Ordinal))
            ?? throw new ArgumentException(
                "The run-kind and seed-category pair is unsupported.",
                nameof(runKindId));
    }
}
