using VibeSnake.Rules;

namespace VibeSnake.Persistence;

public sealed record ScoreBrowseCategory(
    string CategoryKey,
    string DisplayName,
    string IdentityLine,
    bool Competitive,
    int? PersonalBest,
    IReadOnlyList<ScoreHistoryEntry> Scores);

/// <summary>
/// Read-only player-facing projection over versioned personal-best and
/// top-ten history documents.
/// </summary>
public sealed record ScoreBrowseReport(IReadOnlyList<ScoreBrowseCategory> Categories)
{
    public bool HasCategories => Categories.Count > 0;

    public static ScoreBrowseReport Create(
        ScoreHistoryDocument history,
        PersonalBestDocument personalBests)
    {
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(personalBests);
        _ = history.SerializeCanonical();
        _ = personalBests.SerializeCanonical();

        var historyByCategory = history.Entries
            .GroupBy(entry => entry.CategoryKey, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<ScoreHistoryEntry>)group
                    .OrderByDescending(entry => entry.Score)
                    .ThenBy(entry => entry.Sequence)
                    .ToArray(),
                StringComparer.Ordinal);
        var bestByCategory = personalBests.Entries.ToDictionary(
            entry => entry.CategoryKey,
            StringComparer.Ordinal);
        var categoryKeys = historyByCategory.Keys
            .Concat(bestByCategory.Keys)
            .Distinct(StringComparer.Ordinal);
        var categories = new List<ScoreBrowseCategory>();
        foreach (var categoryKey in categoryKeys)
        {
            historyByCategory.TryGetValue(categoryKey, out var scores);
            bestByCategory.TryGetValue(categoryKey, out var personalBest);
            var representative = scores?[0].ToPersonalBestEntry() ?? personalBest!;
            var context = ScoreRunContextCatalog.Get(
                representative.RunKindId,
                representative.SeedCategoryId);
            var isLegacy = representative.RunKindId == ScoreRunContextCatalog.LegacyRunKind;
            var displayName = isLegacy
                ? ScoreRunContextCatalog.LegacyDisplayCategory
                : RunModeCatalog.Get(representative.ModeId, representative.ModeVersion)
                    .DisplayName.ToUpperInvariant()
                    + " / "
                    + context.DisplayCategoryId.ToUpperInvariant();
            var adaptive = representative.AdaptationEnabled ? "DDA ON" : "DDA OFF";
            var identityLine = isLegacy
                ? "UNKNOWN HISTORICAL RULES / NONCOMPETITIVE"
                : $"{representative.RulesetId}@{representative.RulesVersion}  "
                    + $"{representative.ScoreCategoryId}  {adaptive}  "
                    + $"CFG {representative.ConfigHash[..8]}";
            categories.Add(new ScoreBrowseCategory(
                categoryKey,
                displayName,
                identityLine,
                context.CompetitiveEligible && !isLegacy,
                personalBest?.BestScore,
                scores ?? Array.Empty<ScoreHistoryEntry>()));
        }

        return new ScoreBrowseReport(categories
            .OrderBy(category => CategoryOrder(category.DisplayName))
            .ThenBy(category => category.DisplayName, StringComparer.Ordinal)
            .ThenBy(category => category.CategoryKey, StringComparer.Ordinal)
            .ToArray());
    }

    private static int CategoryOrder(string displayName)
    {
        if (displayName.EndsWith("/ NORMAL-HUMAN", StringComparison.Ordinal))
        {
            return 0;
        }

        if (displayName.EndsWith("/ SEEDED-CHALLENGE", StringComparison.Ordinal))
        {
            return 1;
        }

        return displayName == ScoreRunContextCatalog.LegacyDisplayCategory ? 3 : 2;
    }
}
