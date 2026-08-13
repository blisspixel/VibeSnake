using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace VibeSnake.Persistence;

public enum ContentPackKind : byte
{
    Core = 0,
    Radio = 1,
}

public sealed record ContentPackVersionRange(string MinInclusive, string MaxExclusive);

public sealed record ContentPackRulesetRange(
    string Id,
    int MinInclusive,
    int MaxExclusive);

public sealed record ContentPackCompatibility(
    ContentPackVersionRange GameVersion,
    ContentPackRulesetRange Ruleset);

public sealed record ContentPackInventoryBinding(
    int SchemaVersion,
    string AssetRoot,
    string PolicySha256);

public sealed record ContentPackDependency(
    string Id,
    string MinInclusive,
    string MaxExclusive);

public sealed record ContentPackFile(
    string Id,
    string Path,
    string MediaType,
    long Bytes,
    string Sha256,
    string Role,
    string RuntimeUse,
    string CreditId);

public sealed record ContentPackCredit(
    string Id,
    string Source,
    string License,
    string Attribution,
    string ReviewEvidence);

public sealed record ContentPackRadio(
    string StationId,
    string StationName,
    IReadOnlyList<string> TrackIds);

/// <summary>
/// Strict schema 1 native content-pack document. Parsing validates the exact
/// reviewed inventory allowlist before any payload may be opened.
/// </summary>
public sealed record ContentPackManifest(
    int SchemaVersion,
    string Id,
    string Version,
    ContentPackKind Kind,
    string DisplayName,
    string Description,
    ContentPackCompatibility Compatibility,
    ContentPackInventoryBinding Inventory,
    IReadOnlyList<ContentPackDependency> Dependencies,
    IReadOnlyList<ContentPackFile> Files,
    IReadOnlyList<ContentPackCredit> Credits,
    ContentPackRadio? Radio)
{
    public const int CurrentSchemaVersion = 1;
    public const string CorePackId = "vibesnake.core";
    public const int MaximumManifestBytes = 1_048_576;
    public const int MaximumFiles = 4_096;
    public const int MaximumCredits = 1_024;
    public const int MaximumDependencies = 64;

    private const int MaximumIdentifierLength = 128;
    private const int MaximumTextLength = 512;
    private const int MaximumPathLength = 512;

    private static readonly Regex IdentifierPattern = new(
        "^[a-z0-9]+(?:[.-][a-z0-9]+)*$",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    private static readonly Regex StationIdentifierPattern = new(
        "^[a-z0-9]+(?:_[a-z0-9]+)*$",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    private static readonly Regex SemanticVersionPattern = new(
        "^(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)$",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    private static readonly Regex Sha256Pattern = new(
        "^[0-9a-f]{64}$",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    public static ContentPackManifest Parse(string json, ContentInventory inventory)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        if (Encoding.UTF8.GetByteCount(json) > MaximumManifestBytes)
        {
            throw new InvalidDataException(
                $"Content pack exceeds the {MaximumManifestBytes}-byte limit.");
        }

        try
        {
            using var document = JsonDocument.Parse(
                json,
                new JsonDocumentOptions
                {
                    AllowTrailingCommas = false,
                    CommentHandling = JsonCommentHandling.Disallow,
                    MaxDepth = 32,
                });
            RejectDuplicateProperties(document.RootElement, "content pack");
            return ParseRoot(document.RootElement, inventory);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Content pack JSON is malformed.", exception);
        }
    }

    public static ContentPackManifest LoadFromFile(string path, ContentInventory inventory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(inventory);
        var fullPath = Path.GetFullPath(path);
        var info = new FileInfo(fullPath);
        if (!info.Exists)
        {
            throw new FileNotFoundException("Content pack does not exist.", fullPath);
        }
        if (info.Length > MaximumManifestBytes)
        {
            throw new InvalidDataException(
                $"Content pack exceeds the {MaximumManifestBytes}-byte limit.");
        }

        try
        {
            var json = File.ReadAllText(fullPath, new UTF8Encoding(false, true));
            return Parse(json, inventory);
        }
        catch (DecoderFallbackException exception)
        {
            throw new InvalidDataException("Content pack is not valid UTF-8.", exception);
        }
    }

    public static ContentPackManifest CheckCanonicalFile(
        string path,
        ContentInventory inventory)
    {
        var manifest = LoadFromFile(path, inventory);
        var fullPath = Path.GetFullPath(path);
        var source = File.ReadAllText(fullPath, new UTF8Encoding(false, true));
        if (!string.Equals(source, manifest.RenderCanonical(), StringComparison.Ordinal))
        {
            throw new InvalidDataException("Content pack is not canonically encoded.");
        }
        return manifest;
    }

    public string RenderCanonical()
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(
            buffer,
            new JsonWriterOptions
            {
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                Indented = true,
            }))
        {
            writer.WriteStartObject();
            writer.WritePropertyName("compatibility");
            WriteCompatibility(writer, Compatibility);
            writer.WritePropertyName("credits");
            writer.WriteStartArray();
            foreach (var credit in Credits)
            {
                writer.WriteStartObject();
                writer.WriteString("attribution", credit.Attribution);
                writer.WriteString("id", credit.Id);
                writer.WriteString("license", credit.License);
                writer.WriteString("reviewEvidence", credit.ReviewEvidence);
                writer.WriteString("source", credit.Source);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WritePropertyName("dependencies");
            writer.WriteStartArray();
            foreach (var dependency in Dependencies)
            {
                writer.WriteStartObject();
                writer.WriteString("id", dependency.Id);
                writer.WriteString("maxExclusive", dependency.MaxExclusive);
                writer.WriteString("minInclusive", dependency.MinInclusive);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteString("description", Description);
            writer.WriteString("displayName", DisplayName);
            writer.WritePropertyName("files");
            writer.WriteStartArray();
            foreach (var file in Files)
            {
                writer.WriteStartObject();
                writer.WriteNumber("bytes", file.Bytes);
                writer.WriteString("creditId", file.CreditId);
                writer.WriteString("id", file.Id);
                writer.WriteString("mediaType", file.MediaType);
                writer.WriteString("path", file.Path);
                writer.WriteString("role", file.Role);
                writer.WriteString("runtimeUse", file.RuntimeUse);
                writer.WriteString("sha256", file.Sha256);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteString("id", Id);
            writer.WritePropertyName("inventory");
            writer.WriteStartObject();
            writer.WriteString("assetRoot", Inventory.AssetRoot);
            writer.WriteString("policySha256", Inventory.PolicySha256);
            writer.WriteNumber("schemaVersion", Inventory.SchemaVersion);
            writer.WriteEndObject();
            writer.WriteString("kind", Kind == ContentPackKind.Core ? "core" : "radio");
            writer.WritePropertyName("radio");
            if (Radio is null)
            {
                writer.WriteNullValue();
            }
            else
            {
                writer.WriteStartObject();
                writer.WriteString("stationId", Radio.StationId);
                writer.WriteString("stationName", Radio.StationName);
                writer.WritePropertyName("trackIds");
                writer.WriteStartArray();
                foreach (var trackId in Radio.TrackIds)
                {
                    writer.WriteStringValue(trackId);
                }
                writer.WriteEndArray();
                writer.WriteEndObject();
            }
            writer.WriteNumber("schemaVersion", SchemaVersion);
            writer.WriteString("version", Version);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray()) + "\n";
    }

    internal static ContentPackSemanticVersion ParseSemanticVersion(
        string value,
        string location)
    {
        var match = SemanticVersionPattern.Match(value);
        if (!match.Success
            || !int.TryParse(match.Groups[1].Value, out var major)
            || !int.TryParse(match.Groups[2].Value, out var minor)
            || !int.TryParse(match.Groups[3].Value, out var patch))
        {
            throw new InvalidDataException($"{location} must use MAJOR.MINOR.PATCH.");
        }
        return new ContentPackSemanticVersion(major, minor, patch);
    }

    private static ContentPackManifest ParseRoot(
        JsonElement root,
        ContentInventory inventory)
    {
        RequireObject(root, "content pack");
        RequireExactProperties(
            root,
            "content pack",
            "schemaVersion", "id", "version", "kind", "displayName", "description",
            "compatibility", "inventory", "dependencies", "files", "credits", "radio");

        var schemaVersion = RequireInt(root, "schemaVersion", "content pack schemaVersion");
        if (schemaVersion != CurrentSchemaVersion)
        {
            throw new InvalidDataException(
                $"Unsupported content pack schema: {schemaVersion}.");
        }

        var id = RequireIdentifier(root, "id", "content pack id");
        var version = RequireText(root, "version", "content pack version");
        _ = ParseSemanticVersion(version, "Content pack version");
        var kindText = RequireText(root, "kind", "content pack kind");
        var kind = kindText switch
        {
            "core" => ContentPackKind.Core,
            "radio" => ContentPackKind.Radio,
            _ => throw new InvalidDataException(
                $"Content pack has invalid kind: {kindText}."),
        };
        var displayName = RequireText(root, "displayName", "content pack displayName");
        var description = RequireText(root, "description", "content pack description");
        var compatibility = ParseCompatibility(root.GetProperty("compatibility"));
        var binding = ParseInventoryBinding(root.GetProperty("inventory"), inventory);
        var dependencies = ParseDependencies(root.GetProperty("dependencies"), id);
        var credits = ParseCredits(root.GetProperty("credits"));
        var files = ParseFiles(root.GetProperty("files"), credits);
        var radio = ParseRadio(root.GetProperty("radio"), kind, id, files);

        if (kind == ContentPackKind.Core)
        {
            if (id != CorePackId)
            {
                throw new InvalidDataException($"The core pack id must be {CorePackId}.");
            }
            if (dependencies.Length != 0)
            {
                throw new InvalidDataException("The offline core pack cannot have dependencies.");
            }
            if (!files.Any(file => file.RuntimeUse == "required"))
            {
                throw new InvalidDataException(
                    "The core pack must contain required runtime content.");
            }
        }
        else
        {
            if (dependencies.Length != 1 || dependencies[0].Id != CorePackId)
            {
                throw new InvalidDataException(
                    $"A radio pack must depend only on {CorePackId}.");
            }
            if (files.Any(file => file.RuntimeUse != "optional"))
            {
                throw new InvalidDataException(
                    "Radio pack files must have optional runtime use.");
            }
        }

        ValidateInventoryAllowlist(id, files, credits, inventory);
        return new ContentPackManifest(
            schemaVersion,
            id,
            version,
            kind,
            displayName,
            description,
            compatibility,
            binding,
            dependencies,
            files,
            credits,
            radio);
    }

    private static ContentPackCompatibility ParseCompatibility(JsonElement element)
    {
        RequireObject(element, "content pack compatibility");
        RequireExactProperties(
            element,
            "content pack compatibility",
            "gameVersion", "ruleset");

        var game = element.GetProperty("gameVersion");
        RequireObject(game, "content pack gameVersion");
        RequireExactProperties(
            game,
            "content pack gameVersion",
            "minInclusive", "maxExclusive");
        var gameMinimum = RequireText(
            game,
            "minInclusive",
            "content pack gameVersion minInclusive");
        var gameMaximum = RequireText(
            game,
            "maxExclusive",
            "content pack gameVersion maxExclusive");
        if (ParseSemanticVersion(gameMinimum, "Content pack gameVersion minInclusive")
            >= ParseSemanticVersion(gameMaximum, "Content pack gameVersion maxExclusive"))
        {
            throw new InvalidDataException(
                "Content pack gameVersion must define a non-empty version range.");
        }

        var ruleset = element.GetProperty("ruleset");
        RequireObject(ruleset, "content pack ruleset");
        RequireExactProperties(
            ruleset,
            "content pack ruleset",
            "id", "minInclusive", "maxExclusive");
        var rulesetId = RequireIdentifier(ruleset, "id", "content pack ruleset id");
        var rulesMinimum = RequirePositiveInt(
            ruleset,
            "minInclusive",
            "content pack minimum rules version");
        var rulesMaximum = RequirePositiveInt(
            ruleset,
            "maxExclusive",
            "content pack maximum rules version");
        if (rulesMinimum >= rulesMaximum)
        {
            throw new InvalidDataException("Content pack ruleset range must be non-empty.");
        }

        return new ContentPackCompatibility(
            new ContentPackVersionRange(gameMinimum, gameMaximum),
            new ContentPackRulesetRange(rulesetId, rulesMinimum, rulesMaximum));
    }

    private static ContentPackInventoryBinding ParseInventoryBinding(
        JsonElement element,
        ContentInventory inventory)
    {
        RequireObject(element, "content pack inventory");
        RequireExactProperties(
            element,
            "content pack inventory",
            "schemaVersion", "assetRoot", "policySha256");
        var schema = RequireInt(element, "schemaVersion", "content pack inventory schemaVersion");
        var assetRoot = RequireText(element, "assetRoot", "content pack inventory assetRoot");
        var policySha256 = RequireText(
            element,
            "policySha256",
            "content pack inventory policySha256");
        if (schema != 1 || schema != inventory.SchemaVersion)
        {
            throw new InvalidDataException(
                "Content pack inventory schema does not match inventory.");
        }
        if (assetRoot != inventory.AssetRoot)
        {
            throw new InvalidDataException(
                "Content pack asset root does not match inventory.");
        }
        if (!Sha256Pattern.IsMatch(policySha256))
        {
            throw new InvalidDataException(
                "Content pack policySha256 must be lowercase SHA-256.");
        }
        if (policySha256 != inventory.PolicySha256)
        {
            throw new InvalidDataException(
                "Content pack policy hash does not match inventory.");
        }
        return new ContentPackInventoryBinding(schema, assetRoot, policySha256);
    }

    private static ContentPackDependency[] ParseDependencies(
        JsonElement element,
        string packId)
    {
        RequireArray(element, "content pack dependencies", MaximumDependencies);
        var dependencies = new List<ContentPackDependency>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var index = 0;
        foreach (var item in element.EnumerateArray())
        {
            var location = $"content pack dependency {index}";
            RequireObject(item, location);
            RequireExactProperties(item, location, "id", "minInclusive", "maxExclusive");
            var id = RequireIdentifier(item, "id", $"{location} id");
            if (id == packId)
            {
                throw new InvalidDataException("A content pack cannot depend on itself.");
            }
            if (!ids.Add(id))
            {
                throw new InvalidDataException($"Duplicate content pack dependency: {id}.");
            }
            var minimum = RequireText(item, "minInclusive", $"{location} minInclusive");
            var maximum = RequireText(item, "maxExclusive", $"{location} maxExclusive");
            if (ParseSemanticVersion(minimum, $"{location} minInclusive")
                >= ParseSemanticVersion(maximum, $"{location} maxExclusive"))
            {
                throw new InvalidDataException(
                    $"{location} must define a non-empty version range.");
            }
            dependencies.Add(new ContentPackDependency(id, minimum, maximum));
            index++;
        }
        return dependencies.ToArray();
    }

    private static ContentPackCredit[] ParseCredits(JsonElement element)
    {
        RequireArray(element, "content pack credits", MaximumCredits);
        var credits = new List<ContentPackCredit>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var index = 0;
        foreach (var item in element.EnumerateArray())
        {
            var location = $"content pack credit {index}";
            RequireObject(item, location);
            RequireExactProperties(
                item,
                location,
                "id", "source", "license", "attribution", "reviewEvidence");
            var id = RequireIdentifier(item, "id", $"{location} id");
            if (!ids.Add(id))
            {
                throw new InvalidDataException($"Duplicate content pack credit: {id}.");
            }
            credits.Add(new ContentPackCredit(
                id,
                RequireText(item, "source", $"{location} source"),
                RequireText(item, "license", $"{location} license"),
                RequireText(item, "attribution", $"{location} attribution"),
                RequireText(item, "reviewEvidence", $"{location} reviewEvidence")));
            index++;
        }
        if (credits.Count == 0)
        {
            throw new InvalidDataException("Content pack credits must not be empty.");
        }
        return credits.ToArray();
    }

    private static ContentPackFile[] ParseFiles(
        JsonElement element,
        ContentPackCredit[] credits)
    {
        RequireArray(element, "content pack files", MaximumFiles);
        var files = new List<ContentPackFile>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var paths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var creditIds = credits.Select(credit => credit.Id).ToHashSet(StringComparer.Ordinal);
        var index = 0;
        foreach (var item in element.EnumerateArray())
        {
            var location = $"content pack file {index}";
            RequireObject(item, location);
            RequireExactProperties(
                item,
                location,
                "id", "path", "mediaType", "bytes", "sha256", "role", "runtimeUse",
                "creditId");
            var id = RequireText(item, "id", $"{location} id");
            if (!id.StartsWith("asset:", StringComparison.Ordinal))
            {
                throw new InvalidDataException($"{location} id must start with asset:.");
            }
            if (!ids.Add(id))
            {
                throw new InvalidDataException($"Duplicate content pack file id: {id}.");
            }
            var path = RequireSafePath(item, "path", $"{location} path");
            if (id != $"asset:{path}")
            {
                throw new InvalidDataException($"{location} id does not match its path.");
            }
            if (paths.TryGetValue(path, out var collision))
            {
                throw new InvalidDataException(
                    $"Content pack paths collide by case: {collision} and {path}.");
            }
            paths.Add(path, path);
            var sha256 = RequireText(item, "sha256", $"{location} sha256");
            if (!Sha256Pattern.IsMatch(sha256))
            {
                throw new InvalidDataException(
                    $"{location} sha256 must be lowercase SHA-256.");
            }
            var runtimeUse = RequireText(item, "runtimeUse", $"{location} runtimeUse");
            if (runtimeUse is not ("required" or "optional"))
            {
                throw new InvalidDataException(
                    $"{location} has invalid runtimeUse: {runtimeUse}.");
            }
            var creditId = RequireIdentifier(item, "creditId", $"{location} creditId");
            if (!creditIds.Contains(creditId))
            {
                throw new InvalidDataException(
                    $"{location} references unknown credit: {creditId}.");
            }
            files.Add(new ContentPackFile(
                id,
                path,
                RequireText(item, "mediaType", $"{location} mediaType"),
                RequirePositiveLong(item, "bytes", $"{location} bytes"),
                sha256,
                RequireText(item, "role", $"{location} role"),
                runtimeUse,
                creditId));
            index++;
        }
        if (files.Count == 0)
        {
            throw new InvalidDataException("Content pack files must not be empty.");
        }
        return files.ToArray();
    }

    private static ContentPackRadio? ParseRadio(
        JsonElement element,
        ContentPackKind kind,
        string packId,
        IReadOnlyList<ContentPackFile> files)
    {
        if (kind == ContentPackKind.Core)
        {
            if (element.ValueKind != JsonValueKind.Null)
            {
                throw new InvalidDataException("The core pack radio value must be null.");
            }
            return null;
        }

        RequireObject(element, "content pack radio");
        RequireExactProperties(
            element,
            "content pack radio",
            "stationId", "stationName", "trackIds");
        var stationId = RequireStationIdentifier(
            element,
            "stationId",
            "content pack stationId");
        var expectedPackId = "vibesnake.radio." + stationId.Replace('_', '-');
        if (packId != expectedPackId)
        {
            throw new InvalidDataException("Radio pack id must match its stationId.");
        }
        var stationName = RequireText(element, "stationName", "content pack stationName");
        var tracksElement = element.GetProperty("trackIds");
        RequireArray(tracksElement, "content pack trackIds", MaximumFiles);
        var trackIds = new List<string>();
        var unique = new HashSet<string>(StringComparer.Ordinal);
        foreach (var track in tracksElement.EnumerateArray())
        {
            if (track.ValueKind != JsonValueKind.String)
            {
                throw new InvalidDataException("Content pack trackIds must contain strings.");
            }
            var id = RequireBoundedText(track.GetString(), "content pack track id");
            if (!unique.Add(id))
            {
                throw new InvalidDataException("Radio trackIds must be unique.");
            }
            trackIds.Add(id);
        }
        if (trackIds.Count == 0)
        {
            throw new InvalidDataException("A radio pack must contain at least one track.");
        }
        var filesById = files.ToDictionary(file => file.Id, StringComparer.Ordinal);
        foreach (var trackId in trackIds)
        {
            if (!filesById.TryGetValue(trackId, out var file))
            {
                throw new InvalidDataException(
                    $"Radio track is not in pack files: {trackId}.");
            }
            if (file.MediaType != "audio/mpeg" || file.Role != "radio-track")
            {
                throw new InvalidDataException(
                    $"Radio track must be audio/mpeg with role radio-track: {trackId}.");
            }
        }
        return new ContentPackRadio(stationId, stationName, trackIds.ToArray());
    }

    private static void ValidateInventoryAllowlist(
        string packId,
        IReadOnlyList<ContentPackFile> files,
        IReadOnlyList<ContentPackCredit> credits,
        ContentInventory inventory)
    {
        var approved = inventory.GetExportEligibleForPack(packId);
        var expectedIds = approved.Select(asset => asset.Id).ToHashSet(StringComparer.Ordinal);
        var actualIds = files.Select(file => file.Id).ToHashSet(StringComparer.Ordinal);
        if (!expectedIds.SetEquals(actualIds))
        {
            var missing = expectedIds.Except(actualIds).Order(StringComparer.Ordinal);
            var unexpected = actualIds.Except(expectedIds).Order(StringComparer.Ordinal);
            var details = new List<string>();
            if (missing.Any())
            {
                details.Add($"missing {string.Join(", ", missing)}");
            }
            if (unexpected.Any())
            {
                details.Add($"unexpected {string.Join(", ", unexpected)}");
            }
            throw new InvalidDataException(
                "Content pack files do not equal the approved inventory allowlist: "
                + string.Join("; ", details)
                + ".");
        }

        var creditById = credits.ToDictionary(credit => credit.Id, StringComparer.Ordinal);
        foreach (var file in files)
        {
            if (!inventory.TryGetAssetById(file.Id, out var asset))
            {
                throw new InvalidDataException($"Pack file is absent from inventory: {file.Id}.");
            }
            if (file.Path != asset.RelativePath
                || file.MediaType != asset.MediaType
                || file.Bytes != asset.Bytes
                || file.Sha256 != asset.Sha256
                || file.Role != asset.Role
                || file.RuntimeUse != asset.RuntimeUse)
            {
                throw new InvalidDataException(
                    $"Pack file metadata does not match inventory: {file.Id}.");
            }
            if (asset.PackId != packId
                || asset.ShipStatus != "approved"
                || !asset.ExportEligible
                || asset.IntegrityStatus != "valid"
                || asset.DuplicateOf is not null)
            {
                throw new InvalidDataException(
                    $"Pack file is not an approved unique valid export: {file.Id}.");
            }
            if (asset.RightsStatus != "cleared")
            {
                throw new InvalidDataException(
                    $"Pack file does not have cleared rights: {file.Id}.");
            }

            var credit = creditById[file.CreditId];
            if (credit.Source != asset.Rights.Source
                || credit.License != asset.Rights.License
                || credit.Attribution != asset.Rights.Attribution
                || credit.ReviewEvidence != asset.Rights.ReviewEvidence)
            {
                throw new InvalidDataException(
                    $"Pack credit {credit.Id} does not match inventory rights.");
            }
        }
    }

    private static void WriteCompatibility(
        Utf8JsonWriter writer,
        ContentPackCompatibility compatibility)
    {
        writer.WriteStartObject();
        writer.WritePropertyName("gameVersion");
        writer.WriteStartObject();
        writer.WriteString("maxExclusive", compatibility.GameVersion.MaxExclusive);
        writer.WriteString("minInclusive", compatibility.GameVersion.MinInclusive);
        writer.WriteEndObject();
        writer.WritePropertyName("ruleset");
        writer.WriteStartObject();
        writer.WriteString("id", compatibility.Ruleset.Id);
        writer.WriteNumber("maxExclusive", compatibility.Ruleset.MaxExclusive);
        writer.WriteNumber("minInclusive", compatibility.Ruleset.MinInclusive);
        writer.WriteEndObject();
        writer.WriteEndObject();
    }

    private static void RejectDuplicateProperties(JsonElement element, string location)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name))
                {
                    throw new InvalidDataException(
                        $"Content pack contains duplicate JSON field: {property.Name}.");
                }
                RejectDuplicateProperties(property.Value, $"{location}.{property.Name}");
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var item in element.EnumerateArray())
            {
                RejectDuplicateProperties(item, $"{location}[{index}]");
                index++;
            }
        }
    }

    private static void RequireObject(JsonElement element, string location)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException($"{location} must be a JSON object.");
        }
    }

    private static void RequireArray(JsonElement element, string location, int maximum)
    {
        if (element.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException($"{location} must be a JSON array.");
        }
        if (element.GetArrayLength() > maximum)
        {
            throw new InvalidDataException($"{location} exceeds its {maximum}-item limit.");
        }
    }

    private static void RequireExactProperties(
        JsonElement element,
        string location,
        params string[] expected)
    {
        var expectedSet = expected.ToHashSet(StringComparer.Ordinal);
        var actual = element.EnumerateObject()
            .Select(property => property.Name)
            .ToHashSet(StringComparer.Ordinal);
        if (expectedSet.SetEquals(actual))
        {
            return;
        }
        var missing = expectedSet.Except(actual).Order(StringComparer.Ordinal).ToArray();
        var unknown = actual.Except(expectedSet).Order(StringComparer.Ordinal).ToArray();
        var details = new List<string>();
        if (missing.Length > 0)
        {
            details.Add($"missing {string.Join(", ", missing)}");
        }
        if (unknown.Length > 0)
        {
            details.Add($"unknown {string.Join(", ", unknown)}");
        }
        throw new InvalidDataException(
            $"{location} has invalid fields: {string.Join("; ", details)}.");
    }

    private static string RequireText(JsonElement parent, string name, string location)
    {
        var element = parent.GetProperty(name);
        if (element.ValueKind != JsonValueKind.String)
        {
            throw new InvalidDataException($"{location} must be a non-empty string.");
        }
        return RequireBoundedText(element.GetString(), location);
    }

    private static string RequireBoundedText(string? value, string location)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > MaximumTextLength)
        {
            throw new InvalidDataException(
                $"{location} must be a non-empty string up to {MaximumTextLength} characters.");
        }
        return value;
    }

    private static string RequireIdentifier(
        JsonElement parent,
        string name,
        string location)
    {
        var value = RequireText(parent, name, location);
        if (value.Length > MaximumIdentifierLength || !IdentifierPattern.IsMatch(value))
        {
            throw new InvalidDataException(
                $"{location} must use lowercase letters, numbers, dots, or hyphens.");
        }
        return value;
    }

    private static string RequireStationIdentifier(
        JsonElement parent,
        string name,
        string location)
    {
        var value = RequireText(parent, name, location);
        if (value.Length > MaximumIdentifierLength || !StationIdentifierPattern.IsMatch(value))
        {
            throw new InvalidDataException(
                $"{location} must use lowercase letters, numbers, or underscores.");
        }
        return value;
    }

    private static string RequireSafePath(
        JsonElement parent,
        string name,
        string location)
    {
        var value = RequireText(parent, name, location);
        if (value.Length > MaximumPathLength
            || value.Contains('\\')
            || value.StartsWith('/')
            || value.EndsWith('/')
            || value.Split('/').Any(segment => segment is "" or "." or ".."))
        {
            throw new InvalidDataException($"{location} must use a safe relative POSIX path.");
        }
        return value;
    }

    private static int RequireInt(JsonElement parent, string name, string location)
    {
        var element = parent.GetProperty(name);
        if (element.ValueKind != JsonValueKind.Number || !element.TryGetInt32(out var value))
        {
            throw new InvalidDataException($"{location} must be an integer.");
        }
        return value;
    }

    private static int RequirePositiveInt(JsonElement parent, string name, string location)
    {
        var value = RequireInt(parent, name, location);
        if (value <= 0)
        {
            throw new InvalidDataException($"{location} must be a positive integer.");
        }
        return value;
    }

    private static long RequirePositiveLong(JsonElement parent, string name, string location)
    {
        var element = parent.GetProperty(name);
        if (element.ValueKind != JsonValueKind.Number
            || !element.TryGetInt64(out var value)
            || value <= 0)
        {
            throw new InvalidDataException($"{location} must be a positive integer.");
        }
        return value;
    }
}

internal readonly record struct ContentPackSemanticVersion(int Major, int Minor, int Patch)
    : IComparable<ContentPackSemanticVersion>
{
    public int CompareTo(ContentPackSemanticVersion other)
    {
        var major = Major.CompareTo(other.Major);
        if (major != 0)
        {
            return major;
        }
        var minor = Minor.CompareTo(other.Minor);
        return minor != 0 ? minor : Patch.CompareTo(other.Patch);
    }

    public static bool operator <(
        ContentPackSemanticVersion left,
        ContentPackSemanticVersion right) => left.CompareTo(right) < 0;

    public static bool operator >(
        ContentPackSemanticVersion left,
        ContentPackSemanticVersion right) => left.CompareTo(right) > 0;

    public static bool operator <=(
        ContentPackSemanticVersion left,
        ContentPackSemanticVersion right) => left.CompareTo(right) <= 0;

    public static bool operator >=(
        ContentPackSemanticVersion left,
        ContentPackSemanticVersion right) => left.CompareTo(right) >= 0;
}
