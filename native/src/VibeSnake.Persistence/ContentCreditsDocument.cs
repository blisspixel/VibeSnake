using System.Globalization;
using System.Text;

namespace VibeSnake.Persistence;

/// <summary>
/// Deterministic human-readable credits generated only from validated content
/// pack manifests. It deliberately includes no timestamp or machine path.
/// </summary>
public static class ContentCreditsDocument
{
    public const string DocumentId = "content-credits-v1";
    public const int MaximumPackCount = 64;
    public const int MaximumDocumentCharacters = 4_194_304;

    public static string Render(IReadOnlyList<ContentPackManifest> manifests)
    {
        ArgumentNullException.ThrowIfNull(manifests);
        if (manifests.Count == 0 || manifests.Count > MaximumPackCount)
        {
            throw new ArgumentException(
                $"Credits require between 1 and {MaximumPackCount} manifests.",
                nameof(manifests));
        }

        if (manifests.Any(manifest => manifest is null))
        {
            throw new ArgumentException("Credits cannot contain a null manifest.", nameof(manifests));
        }

        var duplicatePackId = manifests
            .GroupBy(manifest => manifest.Id, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1)?.Key;
        if (duplicatePackId is not null)
        {
            throw new InvalidDataException(
                $"Credits contain duplicate pack id: {duplicatePackId}");
        }

        if (manifests.Count(manifest => manifest.Kind == ContentPackKind.Core) != 1)
        {
            throw new InvalidDataException("Credits require exactly one core manifest.");
        }

        var builder = new StringBuilder();
        builder.AppendLine("# Vibe Snake Content Credits and Third-Party Notices");
        builder.AppendLine();
        builder.AppendLine(
            "This document is generated from exact validated content-pack manifests. " +
            "It contains no machine paths or build timestamps.");
        builder.AppendLine();
        builder.AppendLine($"Document contract: `{DocumentId}`");

        foreach (var manifest in manifests
                     .OrderBy(manifest => manifest.Kind)
                     .ThenBy(manifest => manifest.Id, StringComparer.Ordinal))
        {
            AppendManifest(builder, manifest);
            if (builder.Length > MaximumDocumentCharacters)
            {
                throw new InvalidDataException(
                    $"Generated credits exceed {MaximumDocumentCharacters} characters.");
            }
        }

        return builder.ToString().Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    private static void AppendManifest(StringBuilder builder, ContentPackManifest manifest)
    {
        builder.AppendLine();
        builder.Append("## ")
            .Append(SingleLine(manifest.DisplayName))
            .Append(" (`")
            .Append(manifest.Id)
            .Append("` ")
            .Append(manifest.Version)
            .AppendLine(")");
        builder.AppendLine();
        builder.Append("Kind: ")
            .AppendLine(manifest.Kind == ContentPackKind.Core ? "Core" : "Optional radio");
        builder.Append("Description: ").AppendLine(SingleLine(manifest.Description));
        builder.Append("Files: ").AppendLine(manifest.Files.Count.ToString(CultureInfo.InvariantCulture));
        builder.Append("Inventory policy SHA-256: `")
            .Append(manifest.Inventory.PolicySha256)
            .AppendLine("`");

        var creditsById = manifest.Credits.ToDictionary(credit => credit.Id, StringComparer.Ordinal);
        var unknownCreditId = manifest.Files
            .Select(file => file.CreditId)
            .FirstOrDefault(creditId => !creditsById.ContainsKey(creditId));
        if (unknownCreditId is not null)
        {
            throw new InvalidDataException(
                $"Pack {manifest.Id} file references unknown credit: {unknownCreditId}");
        }

        foreach (var credit in manifest.Credits.OrderBy(credit => credit.Id, StringComparer.Ordinal))
        {
            builder.AppendLine();
            builder.Append("### Credit `").Append(credit.Id).AppendLine("`");
            builder.AppendLine();
            builder.Append("- Source: ").AppendLine(SingleLine(credit.Source));
            builder.Append("- License: ").AppendLine(SingleLine(credit.License));
            builder.Append("- Attribution: ").AppendLine(SingleLine(credit.Attribution));
            builder.Append("- Review evidence: ").AppendLine(SingleLine(credit.ReviewEvidence));
            builder.AppendLine("- Files:");
            foreach (var file in manifest.Files
                         .Where(file => file.CreditId == credit.Id)
                         .OrderBy(file => file.Path, StringComparer.Ordinal))
            {
                builder.Append("  - `").Append(file.Path).AppendLine("`");
            }
        }
    }

    private static string SingleLine(string value) =>
        value.Replace('\r', ' ').Replace('\n', ' ').Trim();
}
