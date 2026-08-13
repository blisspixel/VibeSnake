using System.Text.Json;
using VibeSnake.Rules;

namespace VibeSnake.Persistence;

public sealed record ContentPackCompatibilityResult(
    bool Compatible,
    string Code,
    string Message);

public sealed record ContentPackSetResolution(
    ContentPackCompatibilityResult Core,
    IReadOnlyList<string> AcceptedOptional,
    IReadOnlyDictionary<string, ContentPackCompatibilityResult> RejectedOptional)
{
    public bool CoreReady => Core.Compatible;
}

/// <summary>
/// Evaluates validated content manifests without opening payload files. Invalid
/// optional packs are isolated so core play never depends on radio content.
/// </summary>
public static class ContentPackResolver
{
    public static ContentPackCompatibilityResult Evaluate(
        ContentPackManifest manifest,
        string gameVersion,
        string rulesetId,
        int rulesetVersion,
        IReadOnlyDictionary<string, string> installedPacks)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentException.ThrowIfNullOrWhiteSpace(gameVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(rulesetId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(rulesetVersion);
        ArgumentNullException.ThrowIfNull(installedPacks);

        var currentGame = ContentPackManifest.ParseSemanticVersion(
            gameVersion,
            "Current game version");
        var minimumGame = ContentPackManifest.ParseSemanticVersion(
            manifest.Compatibility.GameVersion.MinInclusive,
            "Compatible minimum game version");
        var maximumGame = ContentPackManifest.ParseSemanticVersion(
            manifest.Compatibility.GameVersion.MaxExclusive,
            "Compatible maximum game version");
        if (currentGame < minimumGame)
        {
            return Incompatible(
                "game-version-too-old",
                $"Pack {manifest.Id} requires game "
                + $"{manifest.Compatibility.GameVersion.MinInclusive} or newer.");
        }
        if (currentGame >= maximumGame)
        {
            return Incompatible(
                "game-version-too-new",
                $"Pack {manifest.Id} does not support game {gameVersion}.");
        }

        var ruleset = manifest.Compatibility.Ruleset;
        if (rulesetId != ruleset.Id)
        {
            return Incompatible(
                "ruleset-mismatch",
                $"Pack {manifest.Id} targets ruleset {ruleset.Id}.");
        }
        if (rulesetVersion < ruleset.MinInclusive)
        {
            return Incompatible(
                "rules-version-too-old",
                $"Pack {manifest.Id} requires rules version {ruleset.MinInclusive} or newer.");
        }
        if (rulesetVersion >= ruleset.MaxExclusive)
        {
            return Incompatible(
                "rules-version-too-new",
                $"Pack {manifest.Id} does not support rules version {rulesetVersion}.");
        }

        foreach (var dependency in manifest.Dependencies)
        {
            if (!installedPacks.TryGetValue(dependency.Id, out var installed))
            {
                return Incompatible(
                    "missing-dependency",
                    $"Pack {manifest.Id} requires {dependency.Id}.");
            }
            var installedVersion = ContentPackManifest.ParseSemanticVersion(
                installed,
                $"Installed version of {dependency.Id}");
            var minimum = ContentPackManifest.ParseSemanticVersion(
                dependency.MinInclusive,
                $"Minimum version of {dependency.Id}");
            var maximum = ContentPackManifest.ParseSemanticVersion(
                dependency.MaxExclusive,
                $"Maximum version of {dependency.Id}");
            if (installedVersion < minimum)
            {
                return Incompatible(
                    "dependency-version-too-old",
                    $"Pack {manifest.Id} requires a newer {dependency.Id}.");
            }
            if (installedVersion >= maximum)
            {
                return Incompatible(
                    "dependency-version-too-new",
                    $"Pack {manifest.Id} does not support installed {dependency.Id}.");
            }
        }

        return new ContentPackCompatibilityResult(
            true,
            "compatible",
            $"Pack {manifest.Id} is compatible.");
    }

    public static ContentPackSetResolution Resolve(
        string coreJson,
        IReadOnlyList<string> optionalJsonDocuments,
        ContentInventory inventory,
        string gameVersion,
        string rulesetId = RulesetIdentity.CurrentId,
        int rulesetVersion = RulesetIdentity.CurrentVersion)
    {
        ArgumentNullException.ThrowIfNull(optionalJsonDocuments);
        ArgumentNullException.ThrowIfNull(inventory);
        var core = ContentPackManifest.Parse(coreJson, inventory);
        if (core.Kind != ContentPackKind.Core)
        {
            throw new InvalidDataException(
                "The required pack-set document is not a core pack.");
        }

        var rejected = new Dictionary<string, ContentPackCompatibilityResult>(
            StringComparer.Ordinal);
        var optional = new Dictionary<string, ContentPackManifest>(StringComparer.Ordinal);
        var claimedIds = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < optionalJsonDocuments.Count; index++)
        {
            var document = optionalJsonDocuments[index];
            var fallbackId = TryReadClaimedId(document) ?? $"optional[{index}]";
            if (!claimedIds.Add(fallbackId))
            {
                optional.Remove(fallbackId);
                rejected[fallbackId] = Incompatible(
                    "invalid-pack",
                    $"Duplicate optional pack id: {fallbackId}.");
                continue;
            }

            try
            {
                var manifest = ContentPackManifest.Parse(document, inventory);
                if (manifest.Kind != ContentPackKind.Radio)
                {
                    throw new InvalidDataException(
                        "An optional pack must use the radio kind.");
                }
                optional[manifest.Id] = manifest;
            }
            catch (Exception exception) when (
                exception is InvalidDataException or ArgumentException)
            {
                rejected[fallbackId] = Incompatible(
                    "invalid-pack",
                    BoundReason(exception.Message));
            }
        }

        var installed = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [core.Id] = core.Version,
        };
        foreach (var pair in optional)
        {
            installed[pair.Key] = pair.Value.Version;
        }

        var coreResult = Evaluate(
            core,
            gameVersion,
            rulesetId,
            rulesetVersion,
            installed);
        if (!coreResult.Compatible)
        {
            foreach (var packId in optional.Keys)
            {
                rejected[packId] = Incompatible(
                    "core-unavailable",
                    $"Pack {packId} cannot load because the offline core is incompatible.");
            }
            return new ContentPackSetResolution(
                coreResult,
                Array.Empty<string>(),
                SortRejected(rejected));
        }

        var accepted = new List<string>();
        foreach (var pair in optional.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            var result = Evaluate(
                pair.Value,
                gameVersion,
                rulesetId,
                rulesetVersion,
                installed);
            if (result.Compatible)
            {
                accepted.Add(pair.Key);
            }
            else
            {
                rejected[pair.Key] = result;
            }
        }

        return new ContentPackSetResolution(
            coreResult,
            accepted.ToArray(),
            SortRejected(rejected));
    }

    private static string? TryReadClaimedId(string json)
    {
        if (string.IsNullOrWhiteSpace(json)
            || System.Text.Encoding.UTF8.GetByteCount(json)
                > ContentPackManifest.MaximumManifestBytes)
        {
            return null;
        }
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("id", out var id)
                && id.ValueKind == JsonValueKind.String)
            {
                var value = id.GetString();
                return string.IsNullOrWhiteSpace(value) || value.Length > 128 ? null : value;
            }
        }
        catch (JsonException)
        {
            // The caller already retains the parse rejection. A malformed
            // claimed ID deliberately falls back to a bounded path identity.
        }
        return null;
    }

    private static Dictionary<string, ContentPackCompatibilityResult> SortRejected(
        IReadOnlyDictionary<string, ContentPackCompatibilityResult> rejected) =>
        rejected
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

    private static ContentPackCompatibilityResult Incompatible(string code, string message) =>
        new(false, code, message);

    private static string BoundReason(string reason)
    {
        const int maximumLength = 512;
        var sanitized = reason.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return sanitized.Length <= maximumLength
            ? sanitized
            : sanitized[..maximumLength];
    }
}
