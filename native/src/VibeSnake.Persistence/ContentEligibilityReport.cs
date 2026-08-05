namespace VibeSnake.Persistence;

/// <summary>
/// Deterministic summary of inventory export eligibility for pack approval
/// handoffs. Does not claim any asset is release-approved; it only reports the
/// published inventory classification.
/// </summary>
public sealed record ContentEligibilityReport(
    int FileCount,
    int ExportEligibleCount,
    long ExportEligibleBytes,
    int BlockedCount,
    int ExcludedCount,
    IReadOnlyDictionary<string, int> CountsByShipStatus,
    IReadOnlyDictionary<string, int> CountsByRightsStatus,
    IReadOnlyDictionary<string, int> CountsByMediaTypePrefix,
    IReadOnlyList<string> SampleBlockedPaths)
{
    public const int DefaultSampleBlockedPathLimit = 16;

    public bool HasAnyExportEligible => ExportEligibleCount > 0;

    public static ContentEligibilityReport FromInventory(
        ContentInventory inventory,
        int sampleBlockedPathLimit = DefaultSampleBlockedPathLimit)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        if (sampleBlockedPathLimit < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleBlockedPathLimit));
        }

        var byShip = new Dictionary<string, int>(StringComparer.Ordinal);
        var byRights = new Dictionary<string, int>(StringComparer.Ordinal);
        var byMedia = new Dictionary<string, int>(StringComparer.Ordinal);
        var blocked = 0;
        var excluded = 0;
        var eligible = 0;
        long eligibleBytes = 0;
        var blockedPaths = new List<string>(sampleBlockedPathLimit);

        foreach (var asset in inventory.Assets)
        {
            Increment(byShip, string.IsNullOrWhiteSpace(asset.ShipStatus) ? "unknown" : asset.ShipStatus);
            Increment(
                byRights,
                string.IsNullOrWhiteSpace(asset.RightsStatus) ? "unknown" : asset.RightsStatus);
            var mediaPrefix = MediaTypePrefix(asset.MediaType);
            Increment(byMedia, mediaPrefix);

            if (asset.ExportEligible)
            {
                eligible++;
                eligibleBytes = checked(eligibleBytes + asset.Bytes);
            }

            if (string.Equals(asset.ShipStatus, "blocked", StringComparison.Ordinal))
            {
                blocked++;
                if (blockedPaths.Count < sampleBlockedPathLimit)
                {
                    blockedPaths.Add(asset.RelativePath);
                }
            }
            else if (string.Equals(asset.ShipStatus, "excluded", StringComparison.Ordinal))
            {
                excluded++;
            }
        }

        return new ContentEligibilityReport(
            FileCount: inventory.FileCount,
            ExportEligibleCount: eligible,
            ExportEligibleBytes: eligibleBytes,
            BlockedCount: blocked,
            ExcludedCount: excluded,
            CountsByShipStatus: byShip,
            CountsByRightsStatus: byRights,
            CountsByMediaTypePrefix: byMedia,
            SampleBlockedPaths: blockedPaths);
    }

    private static void Increment(Dictionary<string, int> counts, string key)
    {
        counts[key] = counts.TryGetValue(key, out var current) ? current + 1 : 1;
    }

    private static string MediaTypePrefix(string mediaType)
    {
        if (string.IsNullOrWhiteSpace(mediaType))
        {
            return "unknown";
        }

        var separator = mediaType.IndexOf('/');
        return separator <= 0 ? mediaType : mediaType[..separator];
    }
}
