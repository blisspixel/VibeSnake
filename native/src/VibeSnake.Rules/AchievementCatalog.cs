namespace VibeSnake.Rules;

/// <summary>
/// Stable achievement identifier for pure candidate evaluation.
/// IDs match the Python reference catalog where the condition is rules-local.
/// Profile-lifetime and wall-clock achievements are intentionally excluded.
/// </summary>
public sealed record AchievementDefinition(
    string Id,
    string Name,
    string Description,
    string Rarity);

/// <summary>
/// Snapshot of run-local metrics used to decide achievement candidates.
/// Does not include lifetime profile counters or wall-clock hour.
/// </summary>
public readonly record struct RunAchievementMetrics(
    int Score,
    int MaxCombo,
    int Length,
    int FoodEaten,
    int WrapCount,
    int NearMisses,
    int PowerupsCollected,
    int SurvivalTicks,
    bool IsTerminal);

/// <summary>
/// Pure achievement candidate evaluation for scored runs. Returns newly earned
/// IDs without mutating profile storage; shells and progression own unlock writes.
/// </summary>
public static class AchievementCatalog
{
    public static IReadOnlyList<AchievementDefinition> Definitions { get; } =
    [
        new("first_bite", "First Bite", "Score your first point", "common"),
        new("century", "Century", "Reach 100 points", "common"),
        new("high_roller", "High Roller", "Reach 500 points in a single game", "rare"),
        new("legend", "Legend", "Reach 1000 points in a single game", "legendary"),
        new("just_a_taste", "Just a Taste", "Eat 5 food items", "common"),
        new("getting_longer", "Getting Longer", "Reach length 5", "common"),
        new("growing_strong", "Growing Strong", "Reach length 10", "common"),
        new("serpent", "Serpent", "Reach length 25", "rare"),
        new("combo_starter", "Combo Starter", "Get a 5x combo", "common"),
        new("combo_king", "Combo King", "Get a 10x combo", "rare"),
        new("wrap_around", "Wrap Around", "Use screen wrapping 3 times", "common"),
        new("close_call", "Close Call", "Get 10 near-misses in one game", "rare"),
        new("powered_up", "Powered Up", "Collect your first power-up", "common"),
        new("power_hungry", "Power Hungry", "Collect 5 power-ups in one game", "rare"),
        new("quick_reflexes", "Quick Reflexes", "Survive for 30 seconds", "common"),
        new("endurance", "Endurance", "Survive for 180 seconds", "rare"),
        new("marathon", "Marathon", "Survive for 300 seconds", "epic"),
    ];

    private static readonly Dictionary<string, Func<RunAchievementMetrics, bool>> Conditions =
        new(StringComparer.Ordinal)
        {
            ["first_bite"] = metrics => metrics.Score >= 1,
            ["century"] = metrics => metrics.Score >= 100,
            ["high_roller"] = metrics => metrics.Score >= 500,
            ["legend"] = metrics => metrics.Score >= 1000,
            ["just_a_taste"] = metrics => metrics.FoodEaten >= 5,
            ["getting_longer"] = metrics => metrics.Length >= 5,
            ["growing_strong"] = metrics => metrics.Length >= 10,
            ["serpent"] = metrics => metrics.Length >= 25,
            ["combo_starter"] = metrics => metrics.MaxCombo >= 5,
            ["combo_king"] = metrics => metrics.MaxCombo >= 10,
            ["wrap_around"] = metrics => metrics.WrapCount >= 3,
            ["close_call"] = metrics => metrics.NearMisses >= 10,
            ["powered_up"] = metrics => metrics.PowerupsCollected >= 1,
            ["power_hungry"] = metrics => metrics.PowerupsCollected >= 5,
            ["quick_reflexes"] = metrics =>
                metrics.SurvivalTicks * RunConfig.RulesTickMilliseconds >= 30_000,
            ["endurance"] = metrics =>
                metrics.SurvivalTicks * RunConfig.RulesTickMilliseconds >= 180_000,
            ["marathon"] = metrics =>
                metrics.SurvivalTicks * RunConfig.RulesTickMilliseconds >= 300_000,
        };

    /// <summary>
    /// Returns achievement IDs whose conditions are satisfied and not already unlocked.
    /// Empty when metrics are non-terminal if <paramref name="requireTerminal"/> is true.
    /// </summary>
    public static IReadOnlyList<string> EvaluateCandidates(
        RunAchievementMetrics metrics,
        IReadOnlySet<string>? alreadyUnlocked = null,
        bool requireTerminal = true)
    {
        if (requireTerminal && !metrics.IsTerminal)
        {
            return Array.Empty<string>();
        }

        alreadyUnlocked ??= new HashSet<string>(StringComparer.Ordinal);
        var earned = new List<string>();
        foreach (var definition in Definitions)
        {
            if (alreadyUnlocked.Contains(definition.Id))
            {
                continue;
            }

            if (!Conditions.TryGetValue(definition.Id, out var condition))
            {
                continue;
            }

            if (condition(metrics))
            {
                earned.Add(definition.Id);
            }
        }

        return earned;
    }

    public static AchievementDefinition? Find(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        foreach (var definition in Definitions)
        {
            if (string.Equals(definition.Id, id, StringComparison.Ordinal))
            {
                return definition;
            }
        }

        return null;
    }

    /// <summary>
    /// Zero-based catalog index for <paramref name="id"/>, or -1 when unknown.
    /// Used as the <see cref="RunEventDetail.Value"/> payload for candidate events.
    /// </summary>
    public static int IndexOf(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        for (var index = 0; index < Definitions.Count; index++)
        {
            if (string.Equals(Definitions[index].Id, id, StringComparison.Ordinal))
            {
                return index;
            }
        }

        return -1;
    }

    public static AchievementDefinition? DefinitionAt(int index)
    {
        if (index < 0 || index >= Definitions.Count)
        {
            return null;
        }

        return Definitions[index];
    }
}
