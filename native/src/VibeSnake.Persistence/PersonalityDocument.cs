using System.Globalization;
using System.Text.Json;
using VibeSnake.Rules;

namespace VibeSnake.Persistence;

/// <summary>
/// Versioned custom AI personality document and fail-closed validation.
/// </summary>
public enum PersonalityLoadCode : byte
{
    Success = 0,
    Empty = 1,
    InvalidJson = 2,
    UnsupportedSchema = 3,
    MissingField = 4,
    InvalidType = 5,
    OutOfRange = 6,
    InvalidColor = 7,
    PathUnsafe = 8,
    IoError = 9,
    UnknownField = 10,
    DuplicateField = 11,
    TooLarge = 12,
    ReservedId = 13,
    CapacityExceeded = 14,
    DuplicateId = 15,
}

public sealed record PersonalityValidationIssue(
    string Field,
    string Message,
    string? Received = null);

public sealed record PersonalityLoadResult(
    PersonalityLoadCode Code,
    string Message,
    PersonalityDocument? Document = null,
    IReadOnlyList<PersonalityValidationIssue>? Issues = null)
{
    public bool IsSuccess => Code == PersonalityLoadCode.Success && Document is not null;
}

public sealed record PersonalityDocument(
    int SchemaVersion,
    string Name,
    string Description,
    double Aggression,
    double RiskTolerance,
    double Patience,
    double Greed,
    double Chaos,
    double PowerUpPriority,
    IReadOnlyList<int> Color)
{
    public const int CurrentSchemaVersion = 1;
    public const int MinimumSchemaVersion = 1;
    public const double TraitMinimum = 0.0;
    public const double TraitMaximum = 1.0;
    public const int MaximumDocumentCharacters = 16_384;
    public const int MaximumDocumentBytes = 32_768;
    public const int MaximumNameCharacters = 48;
    public const int MaximumDescriptionCharacters = 192;

    private static readonly HashSet<string> AllowedFields = new(
        [
            "schema_version",
            "schemaVersion",
            "name",
            "description",
            "aggression",
            "risk_tolerance",
            "patience",
            "greed",
            "chaos",
            "power_up_priority",
            "color",
        ],
        StringComparer.Ordinal);

    public static PersonalityLoadResult Read(string json, string? sourceName = null)
    {
        if (json is not null && json.Length > MaximumDocumentCharacters)
        {
            return Fail(
                PersonalityLoadCode.TooLarge,
                FormatMessage(
                    sourceName,
                    $"Personality document exceeds {MaximumDocumentCharacters} characters."));
        }

        if (string.IsNullOrWhiteSpace(json))
        {
            return Fail(
                PersonalityLoadCode.Empty,
                FormatMessage(sourceName, "Personality document is empty."));
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException exception)
        {
            return Fail(
                PersonalityLoadCode.InvalidJson,
                FormatMessage(sourceName, "Personality JSON is invalid: " + exception.Message));
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return Fail(
                    PersonalityLoadCode.InvalidType,
                    FormatMessage(sourceName, "Personality root must be a JSON object."));
            }

            var root = document.RootElement;
            var issues = new List<PersonalityValidationIssue>();
            var structureCode = ValidateRootFields(root, issues);
            if (structureCode is { } invalidStructure)
            {
                return Fail(
                    invalidStructure,
                    FormatMessage(sourceName, "Personality validation failed."),
                    issues);
            }

            var schemaVersion = ReadSchemaVersion(root, issues);

            if (schemaVersion is null)
            {
                return Fail(
                    PersonalityLoadCode.MissingField,
                    FormatMessage(sourceName, "Personality validation failed."),
                    issues);
            }

            if (schemaVersion.Value > CurrentSchemaVersion)
            {
                return Fail(
                    PersonalityLoadCode.UnsupportedSchema,
                    FormatMessage(
                        sourceName,
                        $"Personality schemaVersion {schemaVersion.Value} is newer than supported {CurrentSchemaVersion}."),
                    issues);
            }

            if (schemaVersion.Value < MinimumSchemaVersion)
            {
                return Fail(
                    PersonalityLoadCode.UnsupportedSchema,
                    FormatMessage(
                        sourceName,
                        $"Personality schemaVersion {schemaVersion.Value} is below the supported floor."),
                    issues);
            }

            var name = ReadRequiredString(root, "name", issues);
            var description = ReadRequiredString(root, "description", issues);
            var aggression = ReadTrait(root, "aggression", issues);
            var risk = ReadTrait(root, "risk_tolerance", issues);
            var patience = ReadTrait(root, "patience", issues);
            var greed = ReadTrait(root, "greed", issues);
            var chaos = ReadTrait(root, "chaos", issues);
            var powerPriority = ReadTrait(root, "power_up_priority", issues);
            var color = ReadColor(root, issues);

            if (issues.Count > 0
                || name is null
                || description is null
                || aggression is null
                || risk is null
                || patience is null
                || greed is null
                || chaos is null
                || powerPriority is null
                || color is null)
            {
                return Fail(
                    PersonalityLoadCode.OutOfRange,
                    FormatMessage(sourceName, "Personality validation failed."),
                    issues);
            }

            return new PersonalityLoadResult(
                PersonalityLoadCode.Success,
                FormatMessage(sourceName, "Personality document is valid."),
                new PersonalityDocument(
                    schemaVersion.Value,
                    name,
                    description,
                    aggression.Value,
                    risk.Value,
                    patience.Value,
                    greed.Value,
                    chaos.Value,
                    powerPriority.Value,
                    color));
        }
    }

    public static PersonalityLoadResult ReadFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.GetFullPath(path);
        var fileName = Path.GetFileName(fullPath);
        if (string.IsNullOrWhiteSpace(fileName)
            || fileName.Contains("..", StringComparison.Ordinal)
            || !fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            return Fail(
                PersonalityLoadCode.PathUnsafe,
                FormatMessage(fileName, "Personality path must be a .json file name without traversal."));
        }

        try
        {
            var attributes = File.GetAttributes(fullPath);
            if (attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                return Fail(
                    PersonalityLoadCode.PathUnsafe,
                    FormatMessage(fileName, "Personality file links are not loaded."));
            }

            if (new FileInfo(fullPath).Length > MaximumDocumentBytes)
            {
                return Fail(
                    PersonalityLoadCode.TooLarge,
                    FormatMessage(
                        fileName,
                        $"Personality file exceeds {MaximumDocumentBytes} bytes."));
            }

            return Read(File.ReadAllText(fullPath), fileName);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Fail(
                PersonalityLoadCode.IoError,
                FormatMessage(fileName, "Personality file could not be read: " + exception.Message));
        }
    }

    public PersonalityLoadResult ToProfile(string id)
    {
        if (!AiPersonality.IsValidId(id))
        {
            return Fail(
                PersonalityLoadCode.PathUnsafe,
                $"{id}: custom personality ID is invalid.");
        }

        if (AiPersonalityCatalog.BuiltIn.Any(personality =>
            string.Equals(personality.Id, id, StringComparison.OrdinalIgnoreCase)))
        {
            return Fail(
                PersonalityLoadCode.ReservedId,
                $"{id}: built-in personality IDs are reserved.");
        }

        return new PersonalityLoadResult(
            PersonalityLoadCode.Success,
            $"{id}: custom personality is valid and unofficial.",
            this);
    }

    public AiPersonalityProfile CreateProfile(string id)
    {
        var validation = ToProfile(id);
        if (!validation.IsSuccess)
        {
            throw new InvalidOperationException(validation.Message);
        }

        var personality = new AiPersonality(
            id,
            Name,
            Description,
            ScaleTrait(Aggression),
            ScaleTrait(RiskTolerance),
            ScaleTrait(Patience),
            ScaleTrait(Greed),
            ScaleTrait(Chaos),
            ScaleTrait(PowerUpPriority),
            new AiDisplayColor((byte)Color[0], (byte)Color[1], (byte)Color[2]));
        personality.Validate();
        return new AiPersonalityProfile(
            personality,
            AiPersonalityContentKind.Custom,
            AiPersonalityCatalog.CustomStatusLabel,
            OfficialLeagueQualified: false);
    }

    private static PersonalityLoadCode? ValidateRootFields(
        JsonElement root,
        List<PersonalityValidationIssue> issues)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        PersonalityLoadCode? code = null;
        foreach (var property in root.EnumerateObject())
        {
            if (!seen.Add(property.Name))
            {
                issues.Add(new PersonalityValidationIssue(
                    property.Name,
                    "Duplicate fields are not allowed."));
                code ??= PersonalityLoadCode.DuplicateField;
            }

            if (!AllowedFields.Contains(property.Name))
            {
                issues.Add(new PersonalityValidationIssue(
                    property.Name,
                    "Unknown fields are not allowed."));
                code ??= PersonalityLoadCode.UnknownField;
            }
        }

        if (seen.Contains("schema_version") && seen.Contains("schemaVersion"))
        {
            issues.Add(new PersonalityValidationIssue(
                "schema_version",
                "Use only one schema-version spelling."));
            code ??= PersonalityLoadCode.DuplicateField;
        }

        return code;
    }

    private static int? ReadSchemaVersion(
        JsonElement root,
        List<PersonalityValidationIssue> issues)
    {
        if (!root.TryGetProperty("schema_version", out var element)
            && !root.TryGetProperty("schemaVersion", out element))
        {
            // Legacy 0.2 custom files omit schema; treat as schema 1 when otherwise valid.
            return CurrentSchemaVersion;
        }

        if (element.ValueKind != JsonValueKind.Number || !element.TryGetInt32(out var version))
        {
            issues.Add(new PersonalityValidationIssue(
                "schema_version",
                "Expected an integer schema version.",
                element.ToString()));
            return null;
        }

        return version;
    }

    private static string? ReadRequiredString(
        JsonElement root,
        string field,
        List<PersonalityValidationIssue> issues)
    {
        if (!root.TryGetProperty(field, out var element))
        {
            issues.Add(new PersonalityValidationIssue(field, "Field is required."));
            return null;
        }

        if (element.ValueKind != JsonValueKind.String)
        {
            issues.Add(new PersonalityValidationIssue(
                field,
                "Expected a string.",
                element.ToString()));
            return null;
        }

        var value = element.GetString()?.Trim() ?? string.Empty;
        if (value.Length == 0)
        {
            issues.Add(new PersonalityValidationIssue(field, "String must be non-empty."));
            return null;
        }

        var maximum = field == "name"
            ? MaximumNameCharacters
            : MaximumDescriptionCharacters;
        if (value.Length > maximum)
        {
            issues.Add(new PersonalityValidationIssue(
                field,
                $"String cannot exceed {maximum} characters.",
                value.Length.ToString(CultureInfo.InvariantCulture)));
            return null;
        }

        return value;
    }

    private static double? ReadTrait(
        JsonElement root,
        string field,
        List<PersonalityValidationIssue> issues)
    {
        if (!root.TryGetProperty(field, out var element))
        {
            issues.Add(new PersonalityValidationIssue(field, "Field is required."));
            return null;
        }

        if (element.ValueKind == JsonValueKind.True || element.ValueKind == JsonValueKind.False)
        {
            issues.Add(new PersonalityValidationIssue(
                field,
                "Expected a number in [0, 1], not a boolean.",
                element.GetBoolean().ToString()));
            return null;
        }

        if (element.ValueKind != JsonValueKind.Number || !element.TryGetDouble(out var value))
        {
            issues.Add(new PersonalityValidationIssue(
                field,
                "Expected a finite number in [0, 1].",
                element.ToString()));
            return null;
        }

        if (double.IsNaN(value) || double.IsInfinity(value)
            || value < TraitMinimum || value > TraitMaximum)
        {
            issues.Add(new PersonalityValidationIssue(
                field,
                "Trait must be a finite number in [0, 1].",
                value.ToString(CultureInfo.InvariantCulture)));
            return null;
        }

        return value;
    }

    private static List<int>? ReadColor(
        JsonElement root,
        List<PersonalityValidationIssue> issues)
    {
        if (!root.TryGetProperty("color", out var element))
        {
            issues.Add(new PersonalityValidationIssue("color", "Field is required."));
            return null;
        }

        if (element.ValueKind != JsonValueKind.Array)
        {
            issues.Add(new PersonalityValidationIssue(
                "color",
                "Expected an RGB array of three integers 0-255.",
                element.ToString()));
            return null;
        }

        var channels = new List<int>(3);
        foreach (var channel in element.EnumerateArray())
        {
            if (channel.ValueKind != JsonValueKind.Number || !channel.TryGetInt32(out var value)
                || value < 0 || value > 255)
            {
                issues.Add(new PersonalityValidationIssue(
                    "color",
                    "Each RGB channel must be an integer in 0-255.",
                    channel.ToString()));
                return null;
            }

            channels.Add(value);
        }

        if (channels.Count != 3)
        {
            issues.Add(new PersonalityValidationIssue(
                "color",
                "Expected exactly three RGB channels.",
                channels.Count.ToString(CultureInfo.InvariantCulture)));
            return null;
        }

        return channels;
    }

    private static PersonalityLoadResult Fail(
        PersonalityLoadCode code,
        string message,
        IReadOnlyList<PersonalityValidationIssue>? issues = null) =>
        new(code, message, Issues: issues ?? Array.Empty<PersonalityValidationIssue>());

    private static string FormatMessage(string? sourceName, string message) =>
        string.IsNullOrWhiteSpace(sourceName) ? message : $"{sourceName}: {message}";

    private static int ScaleTrait(double value) =>
        (int)Math.Round(value * 100, MidpointRounding.AwayFromZero);
}
