namespace VibeSnake.Rules;

public sealed record StationIdentityDefinition(string Id, string DisplayName);

/// <summary>
/// Stable presentation identities for the eight Snake Broadcast Network stations.
/// Audio approval, scheduling, moderation, and content policy belong to higher layers.
/// </summary>
public static class StationIdentityCatalog
{
    public static IReadOnlyList<StationIdentityDefinition> All { get; } =
        Array.AsReadOnly(new[]
        {
            new StationIdentityDefinition("flow_signal", "The Flow Signal"),
            new StationIdentityDefinition("chaos_theory", "Chaos Theory"),
            new StationIdentityDefinition("global_coil", "The Global Coil"),
            new StationIdentityDefinition("ourotron", "Ourotron"),
            new StationIdentityDefinition("the_pit", "The Pit"),
            new StationIdentityDefinition("the_bureau", "The Bureau"),
            new StationIdentityDefinition("the_strike", "The Strike"),
            new StationIdentityDefinition("underground_scales", "Underground Scales"),
        });

    public static StationIdentityDefinition Get(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return All.SingleOrDefault(value => string.Equals(value.Id, id, StringComparison.Ordinal))
            ?? throw new ArgumentException($"Unknown station identity {id}.", nameof(id));
    }
}
