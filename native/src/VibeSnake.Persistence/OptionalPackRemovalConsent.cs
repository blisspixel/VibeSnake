namespace VibeSnake.Persistence;

public sealed record InstalledOptionalPack(
    string Id,
    string Version,
    string DisplayName);

public enum OptionalPackRemovalCode : byte
{
    Ready = 0,
    InvalidPackId = 1,
    CorePackProtected = 2,
    NotInstalled = 3,
    DuplicateInstalledId = 4,
    StaleRequest = 5,
}

public sealed record OptionalPackRemovalRequest(
    OptionalPackRemovalCode Code,
    string Message,
    OptionalPackRemovalConsent? Consent = null)
{
    public bool IsReady => Code == OptionalPackRemovalCode.Ready && Consent is not null;
}

public sealed record OptionalPackRemovalResult(
    OptionalPackRemovalCode Code,
    string Message,
    IReadOnlyList<InstalledOptionalPack> RemainingPacks)
{
    public bool IsSuccess => Code == OptionalPackRemovalCode.Ready;
}

/// <summary>
/// Immutable, explicit consent token for removing one optional pack. The token
/// models pack selection only and deliberately has no save, profile, replay,
/// preference, achievement, or log path.
/// </summary>
public sealed record OptionalPackRemovalConsent
{
    private OptionalPackRemovalConsent(
        string packId,
        string packVersion,
        string displayName)
    {
        PackId = packId;
        PackVersion = packVersion;
        DisplayName = displayName;
    }

    public string PackId { get; }

    public string PackVersion { get; }

    public string DisplayName { get; }

    public bool RequiresExplicitConfirmation => true;

    public bool RemovesSaveData => false;

    public bool RemovesProfiles => false;

    public bool RemovesReplays => false;

    public static OptionalPackRemovalRequest Request(
        IReadOnlyList<InstalledOptionalPack> installedPacks,
        string packId)
    {
        ArgumentNullException.ThrowIfNull(installedPacks);
        if (string.IsNullOrWhiteSpace(packId)
            || packId.Length > 128
            || packId.Split(['.', '-']).Any(string.IsNullOrEmpty)
            || !packId.All(character =>
                char.IsAsciiLetterLower(character)
                || char.IsAsciiDigit(character)
                || character is '.' or '-'))
        {
            return Failure(
                OptionalPackRemovalCode.InvalidPackId,
                "Optional pack id is invalid.");
        }
        if (packId == ContentPackManifest.CorePackId)
        {
            return Failure(
                OptionalPackRemovalCode.CorePackProtected,
                "The required core pack cannot be removed.");
        }
        if (!packId.StartsWith(ContentPackBudgets.RadioPackIdPrefix, StringComparison.Ordinal)
            || packId.Length == ContentPackBudgets.RadioPackIdPrefix.Length)
        {
            return Failure(
                OptionalPackRemovalCode.InvalidPackId,
                "Only optional radio packs may enter the removal flow.");
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var pack in installedPacks)
        {
            ArgumentNullException.ThrowIfNull(pack);
            if (!ids.Add(pack.Id))
            {
                return Failure(
                    OptionalPackRemovalCode.DuplicateInstalledId,
                    "Installed optional pack ids must be unique.");
            }
        }

        var target = installedPacks.SingleOrDefault(pack => pack.Id == packId);
        if (target is null)
        {
            return Failure(
                OptionalPackRemovalCode.NotInstalled,
                "Optional pack is not installed.");
        }
        if (string.IsNullOrWhiteSpace(target.DisplayName)
            || target.DisplayName.Length > 256)
        {
            return Failure(
                OptionalPackRemovalCode.InvalidPackId,
                "Optional pack display name is invalid.");
        }
        try
        {
            _ = ContentPackManifest.ParseSemanticVersion(
                target.Version,
                "Installed optional pack version");
        }
        catch (InvalidDataException)
        {
            return Failure(
                OptionalPackRemovalCode.InvalidPackId,
                "Installed optional pack version is invalid.");
        }

        return new OptionalPackRemovalRequest(
            OptionalPackRemovalCode.Ready,
            $"Confirm removal of {target.DisplayName}. Saves and replays are retained.",
            new OptionalPackRemovalConsent(target.Id, target.Version, target.DisplayName));
    }

    public OptionalPackRemovalResult Confirm(
        IReadOnlyList<InstalledOptionalPack> installedPacks)
    {
        ArgumentNullException.ThrowIfNull(installedPacks);
        var matches = installedPacks
            .Where(pack => pack.Id == PackId && pack.Version == PackVersion)
            .ToArray();
        if (matches.Length != 1)
        {
            return new OptionalPackRemovalResult(
                OptionalPackRemovalCode.StaleRequest,
                "Optional pack changed after removal was requested.",
                installedPacks.ToArray());
        }

        return new OptionalPackRemovalResult(
            OptionalPackRemovalCode.Ready,
            $"{DisplayName} removed. Saves and replays were retained.",
            installedPacks.Where(pack => pack.Id != PackId).ToArray());
    }

    public OptionalPackRemovalResult Cancel(
        IReadOnlyList<InstalledOptionalPack> installedPacks)
    {
        ArgumentNullException.ThrowIfNull(installedPacks);
        return new OptionalPackRemovalResult(
            OptionalPackRemovalCode.Ready,
            "Optional pack removal cancelled.",
            installedPacks.ToArray());
    }

    private static OptionalPackRemovalRequest Failure(
        OptionalPackRemovalCode code,
        string message) => new(code, message);
}
