using System.Globalization;
using VibeSnake.Persistence;

namespace RepositoryChecks;

internal sealed record ContentPackQualificationResult(bool Passed, IReadOnlyList<string> Lines);

internal static class ContentPackQualificationCheck
{
    internal const string StaleInventoryMessage =
        "content inventory is stale; run "
        + "dotnet run --project native/tools/RepositoryChecks/RepositoryChecks.csproj "
        + "-- inventory-write .";

    internal static ContentPackQualificationResult Qualify(
        string repositoryRoot,
        IReadOnlyList<string> manifestPaths,
        string? inventoryPath,
        string gameVersion,
        string rulesetId,
        int rulesetVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        ArgumentNullException.ThrowIfNull(manifestPaths);
        ArgumentException.ThrowIfNullOrWhiteSpace(gameVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(rulesetId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(rulesetVersion);

        var inventory = FreshContentInventory.Load(repositoryRoot, inventoryPath);
        var manifests = new List<ContentPackManifest>(manifestPaths.Count);
        foreach (var path in manifestPaths)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);
            manifests.Add(ContentPackManifest.CheckCanonicalFile(Path.GetFullPath(path), inventory));
        }

        var coreCount = 0;
        ContentPackManifest? core = null;
        var optional = new List<string>();
        foreach (var manifest in manifests)
        {
            if (manifest.Kind == ContentPackKind.Core)
            {
                coreCount++;
                core = manifest;
            }
            else
            {
                optional.Add(manifest.RenderCanonical());
            }
        }

        if (coreCount != 1 || core is null)
        {
            throw new InvalidDataException(
                "expected exactly one core manifest, found "
                + coreCount.ToString(CultureInfo.InvariantCulture));
        }

        var resolution = ContentPackResolver.Resolve(
            core.RenderCanonical(),
            optional,
            inventory,
            gameVersion,
            rulesetId,
            rulesetVersion);
        if (!resolution.CoreReady)
        {
            return new ContentPackQualificationResult(
                false,
                ["Content pack core rejected: " + resolution.Core.Message]);
        }

        if (resolution.RejectedOptional.Count > 0)
        {
            var lines = new List<string>
            {
                "Content pack qualification rejected optional content:",
            };
            foreach (var pair in resolution.RejectedOptional.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                lines.Add(
                    "  "
                    + pair.Key
                    + ": "
                    + pair.Value.Code
                    + ": "
                    + pair.Value.Message);
            }

            return new ContentPackQualificationResult(false, lines);
        }

        return new ContentPackQualificationResult(
            true,
            [
                "Content packs qualified: core="
                    + core.Id
                    + " optional="
                    + resolution.AcceptedOptional.Count.ToString(CultureInfo.InvariantCulture),
            ]);
    }
}

internal static class FreshContentInventory
{
    internal static ContentInventory Load(string repositoryRoot, string? inventoryPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        var root = Path.GetFullPath(repositoryRoot);
        var inspection = ContentInventoryCheck.Inspect(root);
        if (!inspection.Passed)
        {
            var message = inspection.Failures.Count == 0
                ? ContentPackQualificationCheck.StaleInventoryMessage
                : string.Join(" ", inspection.Failures);
            throw new InvalidDataException(message);
        }

        var canonical = Path.Combine(root, ContentInventoryCheck.InventoryRelativePath);
        if (string.IsNullOrWhiteSpace(inventoryPath))
        {
            return ContentInventory.LoadFromFile(canonical);
        }

        var requested = Path.GetFullPath(inventoryPath);
        var canonicalBytes = File.ReadAllBytes(canonical);
        byte[] requestedBytes;
        try
        {
            requestedBytes = File.ReadAllBytes(requested);
        }
        catch (FileNotFoundException exception)
        {
            throw new InvalidDataException(
                "content inventory does not exist: " + requested + "; regenerate it",
                exception);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            throw new InvalidDataException(
                "content inventory is unreadable: " + requested + ": " + exception.Message,
                exception);
        }

        if (!canonicalBytes.AsSpan().SequenceEqual(requestedBytes))
        {
            throw new InvalidDataException(ContentPackQualificationCheck.StaleInventoryMessage);
        }

        return ContentInventory.LoadFromFile(requested);
    }
}
