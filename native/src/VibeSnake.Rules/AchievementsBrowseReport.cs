namespace VibeSnake.Rules;

/// <summary>
/// One catalog row projected for progression browse UI.
/// </summary>
public sealed record AchievementBrowseEntry(
    string Id,
    string Name,
    string Description,
    string Rarity,
    bool Unlocked,
    int CatalogIndex);

/// <summary>
/// Pure projection of the rules-local achievement catalog against permanent
/// unlocks. Shells render this; profile storage stays outside rules state.
/// </summary>
public sealed record AchievementsBrowseReport(
    int CatalogCount,
    int UnlockedCount,
    int LockedCount,
    IReadOnlyDictionary<string, int> CatalogCountsByRarity,
    IReadOnlyDictionary<string, int> UnlockedCountsByRarity,
    IReadOnlyList<AchievementBrowseEntry> Entries)
{
    public const int DefaultPreviewLimit = 8;

    public bool HasAnyUnlock => UnlockedCount > 0;

    public bool IsComplete => CatalogCount > 0 && LockedCount == 0;

    /// <summary>
    /// Builds a browse report from permanent unlock IDs. Unknown IDs are ignored
    /// so corrupt profile documents cannot invent catalog rows.
    /// </summary>
    public static AchievementsBrowseReport FromUnlocks(IEnumerable<string>? unlockedIds)
    {
        var unlocked = new HashSet<string>(StringComparer.Ordinal);
        if (unlockedIds is not null)
        {
            foreach (var id in unlockedIds)
            {
                if (string.IsNullOrWhiteSpace(id))
                {
                    continue;
                }

                if (AchievementCatalog.Find(id) is not null)
                {
                    unlocked.Add(id);
                }
            }
        }

        var entries = new List<AchievementBrowseEntry>(AchievementCatalog.Definitions.Count);
        var catalogByRarity = new Dictionary<string, int>(StringComparer.Ordinal);
        var unlockedByRarity = new Dictionary<string, int>(StringComparer.Ordinal);
        var unlockedCount = 0;

        for (var index = 0; index < AchievementCatalog.Definitions.Count; index++)
        {
            var definition = AchievementCatalog.Definitions[index];
            var rarity = string.IsNullOrWhiteSpace(definition.Rarity) ? "unknown" : definition.Rarity;
            Increment(catalogByRarity, rarity);
            var isUnlocked = unlocked.Contains(definition.Id);
            if (isUnlocked)
            {
                unlockedCount++;
                Increment(unlockedByRarity, rarity);
            }

            entries.Add(
                new AchievementBrowseEntry(
                    Id: definition.Id,
                    Name: definition.Name,
                    Description: definition.Description,
                    Rarity: rarity,
                    Unlocked: isUnlocked,
                    CatalogIndex: index));
        }

        return new AchievementsBrowseReport(
            CatalogCount: entries.Count,
            UnlockedCount: unlockedCount,
            LockedCount: entries.Count - unlockedCount,
            CatalogCountsByRarity: catalogByRarity,
            UnlockedCountsByRarity: unlockedByRarity,
            Entries: entries);
    }

    /// <summary>
    /// Compact summary for menu captions, e.g. <c>RUN UNLOCKS 3/17</c>.
    /// </summary>
    public string FormatSummaryLine(string prefix = "RUN UNLOCKS") =>
        $"{prefix} {UnlockedCount}/{CatalogCount}";

    /// <summary>
    /// Preview of unlocked IDs in catalog order, optionally truncated.
    /// </summary>
    public string FormatUnlockedPreview(int limit = DefaultPreviewLimit)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(limit);

        if (UnlockedCount == 0 || limit == 0)
        {
            return string.Empty;
        }

        var selected = new List<string>(Math.Min(limit, UnlockedCount));
        foreach (var entry in Entries)
        {
            if (!entry.Unlocked)
            {
                continue;
            }

            selected.Add(entry.Id.ToUpperInvariant());
            if (selected.Count >= limit)
            {
                break;
            }
        }

        var preview = string.Join(", ", selected);
        if (UnlockedCount > selected.Count)
        {
            preview += ", ...";
        }

        return preview;
    }

    /// <summary>
    /// Rarity progress captions in catalog rarity insertion order, e.g. <c>common 2/8</c>.
    /// </summary>
    public IReadOnlyList<string> FormatRarityProgressLines()
    {
        var lines = new List<string>(CatalogCountsByRarity.Count);
        foreach (var pair in CatalogCountsByRarity)
        {
            var unlocked = UnlockedCountsByRarity.TryGetValue(pair.Key, out var count) ? count : 0;
            lines.Add($"{pair.Key} {unlocked}/{pair.Value}");
        }

        return lines;
    }

    /// <summary>
    /// Entries filtered by unlock state for paged browse UIs.
    /// </summary>
    public IReadOnlyList<AchievementBrowseEntry> Filter(bool? unlockedOnly = null)
    {
        if (unlockedOnly is null)
        {
            return Entries;
        }

        var wantUnlocked = unlockedOnly.Value;
        return Entries.Where(entry => entry.Unlocked == wantUnlocked).ToArray();
    }

    private static void Increment(Dictionary<string, int> counts, string key)
    {
        counts[key] = counts.TryGetValue(key, out var current) ? current + 1 : 1;
    }
}
