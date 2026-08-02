namespace VibeSnake.Rules;

public sealed record RulesetIdentity
{
    public const string CurrentId = "vibesnake-core";
    public const int CurrentVersion = 4;

    public static RulesetIdentity Current { get; } = new(CurrentId, CurrentVersion);

    public RulesetIdentity(string id, int version)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        if (version <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(version));
        }

        Id = id;
        Version = version;
    }

    public string Id { get; }

    public int Version { get; }

    public string ContractId => $"{Id}@{Version}";

    public bool IsCurrent =>
        string.Equals(Id, CurrentId, StringComparison.Ordinal)
        && Version == CurrentVersion;
}
